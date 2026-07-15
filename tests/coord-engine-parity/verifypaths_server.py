#!/usr/bin/env python3
"""Case 23's `verify-paths` world, served over HTTP for the compiled engine.

`verify-paths` answers one question at the merge boundary: did a PR stay INSIDE the touch-set declared by
the issue it implements? The corpus (`tests/fsgg-coord/cases/23-verify-paths-boundary.sh`) certifies the
verdict vocabulary the shim inherits from bash — and the whole point of the command is that "I could not
check" is NEVER one of the verdicts (#322):

    OK      every changed file is inside the declared touch-set.
    DRIFT   a file falls outside it (named), and by default this exits NON-ZERO — the touch-set-drift gate
            greps this line; `--warn` downgrades the DRIFT to advisory (exit 0), the advisory CI mode.
    SKIP    nothing to verify against — the PR implements no tracked item, or the item declared no
            touch-set. Green: a PR with nothing to check is not a failure.
    (error) the head ref / body / file list could not be READ (#322). This is not a verdict and never
            green-with-a-verdict, EVEN under --warn: stamping a verdict on a subject nobody looked at is
            the exact fail-open the command exists to prevent.

This is that world one transport over. The engine resolves the issue a PR implements from its
`item/<n>-…` branch (else what it closes), reads the issue body for the `Paths:` touch-set, and diffs the
PR's changed files against it — so the fixture serves three reads: the PR (`/pulls/<n>` → head.ref), its
files (`/pulls/<n>/files`), and the issue body (`/issues/<n>`). #70's touch-set is the same one case 22
seeds (`src/Scene/**, tests/Scene/**`), so PR 7 (Scene files) is OK and PR 8 (an Audio file + README) is
DRIFT — the corpus's certified pair.

Env toggle (the #322 fail-closed leg spawns a FRESH server):
    FSGG_PARITY_HEADREF_FAIL=<pr>   the PR read (`/pulls/<pr>`) 503s, so the head ref is UNREADABLE and the
                                    engine must reach NO verdict and fail closed — with or without --warn.
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

HEADREF_FAIL = os.environ.get("FSGG_PARITY_HEADREF_FAIL", "")

# PRs: branch (head.ref) + the files they change. Branches drive issue resolution — `item/<n>-…` names the
# issue directly; a `chore/…` branch closes nothing here, so it resolves to SKIP (no tracked item).
PRS = {
    7: {"ref": "item/70-scene-graph", "files": ["src/Scene/Graph.fs", "tests/Scene/GraphTests.fs"]},
    8: {"ref": "item/70-scene-graph", "files": ["src/Scene/Graph.fs", "src/Audio/Mixer.fs", "README.md"]},
    9: {"ref": "chore/no-linked-issue", "files": ["README.md"]},
    10: {"ref": "item/72-no-touch-set-declared", "files": ["src/Whatever.fs"]},
}

# Issue bodies — the touch-sets the PRs are checked against. #70 is case 22's seeded Scene touch-set;
# #72 declares none, so a PR that implements it is SKIP (nothing to verify against).
BODIES = {
    70: "Paths: src/Scene/**, tests/Scene/**",
    72: "",  # no touch-set declared
}


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, payload):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        # The PR's changed files (`Reads.prFiles`).
        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)/files$", p)
        if m:
            pr = int(m.group(1))
            return self._send(200, [{"filename": f} for f in PRS.get(pr, {}).get("files", [])])
        # The PR itself (`Reads.prHeadRef` reads .head.ref). #322: on the fail toggle this 503s, so the
        # head ref is UNREADABLE and the engine must refuse to guess which issue the PR implements.
        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            if str(pr) == HEADREF_FAIL:
                return self._send(503, {"message": "PR read unavailable"})
            ref = PRS.get(pr, {}).get("ref", "")
            return self._send(200, {"number": pr, "head": {"ref": ref}})
        # The issue body (`Reads.issueBody` reads .body → the `Paths:` touch-set).
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4960, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})

    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        p = self.path.split("?", 1)[0]
        # The closing-issue fallback (`Reads.prClosingRef`): a PR whose branch is not `item/<n>-…` asks
        # GraphQL what it closes. Here PR 9 closes NOTHING, so the answer is an empty node set → SKIP.
        if p.rstrip("/") == "/graphql":
            return self._send(200, {
                "data": {"repository": {"pullRequest": {"closingIssuesReferences": {"nodes": []}}}},
                "rateLimit": {"cost": 1, "remaining": 4999},
            })
        self._send(500, {"message": f"unhandled POST {p}"})


def main():
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

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

Env toggles:
    FSGG_PARITY_HEADREF_FAIL=<pr>   the PR read (`/pulls/<pr>`) 503s, so the head ref is UNREADABLE and the
                                    engine must reach NO verdict and fail closed — with or without --warn
                                    (the #322 fail-closed leg spawns a FRESH server with this set).
    FSGG_PARITY_REQLOG=<path>       append every request's METHOD + PATH (one per line) to this file. The
                                    #430 legs assert on the REQUEST, not the verdict: that the PR is read
                                    from the repo the git REMOTE named, and that resolving that repo spent
                                    NO GraphQL — the corpus's own technique (case 23 line 163), since the
                                    verdict would be identical either way (the whole trap).
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

HEADREF_FAIL = os.environ.get("FSGG_PARITY_HEADREF_FAIL", "")
REQLOG = os.environ.get("FSGG_PARITY_REQLOG", "")

# PRs: branch (head.ref) + the files they change. Branches drive issue resolution — `item/<n>-…` names the
# issue directly; a `chore/…` branch that closes nothing resolves to SKIP (no tracked item), while a
# `chore/…` branch that closes ANOTHER repo's issue (PR 11) falls through to the GraphQL closing-ref query.
PRS = {
    7: {"ref": "item/70-scene-graph", "files": ["src/Scene/Graph.fs", "tests/Scene/GraphTests.fs"]},
    8: {"ref": "item/70-scene-graph", "files": ["src/Scene/Graph.fs", "src/Audio/Mixer.fs", "README.md"]},
    9: {"ref": "chore/no-linked-issue", "files": ["README.md"]},
    10: {"ref": "item/72-no-touch-set-declared", "files": ["src/Whatever.fs"]},
    11: {"ref": "chore/closes-another-repo", "files": ["src/Scene/Graph.fs"]},
}

# Closing-ref answers, keyed by PR number (`Reads.prClosingRef` sends the PR in the GraphQL variables). PR 9
# closes NOTHING → empty node set → the unlinked SKIP. PR 11 closes an issue in ANOTHER repo via GitHub's
# cross-repo close (case 24 lines 148-165) → the engine resolves the issue in FS.GG.Rendering, sees it is
# not the PR's own repo (FS.GG.SDD), and SKIPs green, naming the other repo — never a verdict across the line.
CLOSES = {
    11: [{"number": 70, "repository": {"nameWithOwner": "FS-GG/FS.GG.Rendering"}}],
}

# Issue bodies — the touch-sets the PRs are checked against, keyed by (repo, number). #70 is case 22's
# seeded Scene touch-set; #72 declares none, so a PR that implements it is SKIP (nothing to verify against).
#
# #494 is the issue-side repo boundary (case 24): SDD#494 and Rendering#494 share a NUMBER but are
# different issues with DIVERGING touch-sets — SDD#494 covers Scene (so PR 7's Scene files are OK),
# Rendering#494 covers Audio (so the same PR 7 DRIFTs). A store keyed by number alone could not tell
# them apart, and a repo-confused read would come back confident and wrong — so the fixture keys by repo.
BODIES = {
    ("FS.GG.SDD", 70): "Paths: src/Scene/**, tests/Scene/**",
    ("FS.GG.SDD", 72): "",  # no touch-set declared
    ("FS.GG.SDD", 494): "Paths: src/Scene/**, tests/Scene/**",
    ("FS.GG.Rendering", 494): "Paths: src/Audio/**",
}


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _reqlog(self):
        # Record METHOD + PATH so a test can assert on the REQUEST the engine made — the repo it read the
        # PR from, and that it spent no GraphQL to decide it (#430). Path only; the token is not our subject.
        if REQLOG:
            with open(REQLOG, "a") as f:
                f.write(f"{self.command} {self.path.split('?', 1)[0]}\n")

    def _send(self, code, payload):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_GET(self):
        self._reqlog()
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
        # The issue body (`Reads.issueBody` reads .body → the `Paths:` touch-set). Keyed by REPO and
        # number (#494): the same number in two repos is two different touch-sets, and the read is
        # addressed to the one the caller named — the repo segment is not decoration.
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/(\d+)$", p)
        if m:
            repo, n = m.group(1), int(m.group(2))
            if (repo, n) not in BODIES:
                return self._send(404, {"message": f"no issue {repo}#{n}"})
            return self._send(200, {"number": n, "body": BODIES[(repo, n)]})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4960, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})

    def do_POST(self):
        self._reqlog()
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        p = self.path.split("?", 1)[0]
        # The closing-issue fallback (`Reads.prClosingRef`): a PR whose branch is not `item/<n>-…` asks
        # GraphQL what it closes. The PR is in the query variables, so the answer is keyed to it: PR 9 (and
        # anything unlisted) closes NOTHING → empty node set → SKIP; PR 11 closes another repo's issue.
        if p.rstrip("/") == "/graphql":
            try:
                pr = int(json.loads(raw or b"{}").get("variables", {}).get("pr", 0))
            except (ValueError, json.JSONDecodeError):
                pr = 0
            return self._send(200, {
                "data": {"repository": {"pullRequest": {"closingIssuesReferences": {"nodes": CLOSES.get(pr, [])}}}},
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

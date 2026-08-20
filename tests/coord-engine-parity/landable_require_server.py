#!/usr/bin/env python3
"""Case 32 (#737) — `landable --require NAME` and `landable --sha SHA`, the caller's own assertions.

These two flags are what let the LAST hand-rolled copy of the merge gate — `skill-registry-autofix.yml`, an
auto-merge bot that lands a PR with no human in the loop — call `landable` instead of carrying its own
rollup (#724). Each replaces one thing that bot's gate did and the command could not:

    --require NAME   the check that DECIDES the PR is `registry-coherence`, and branch protection does NOT
                     require it (it cannot: its verdict is a function of OTHER repos' mains, so requiring it
                     would let a producer's merge block every open PR here — #549). So nothing but this
                     assertion will ever look at it, and an ABSENT check reads exactly like a passing one to
                     any "is anything red?" rollup (#606).

    --sha SHA        the bot FORCE-PUSHES and then gates. `pulls/{n}` is eventually consistent, so for a
                     moment it still names the PREVIOUS commit — whose checks are green, and are not about
                     the code that would be merged. The caller knows the SHA it pushed; it says so.

BOTH CAN ONLY REFUSE. An unmet assertion is `pending`, never `green` — the verdicts here are the proof.

    901  registry-coherence reported, green            -> green   (0)
    902  every check green, but NO registry-coherence  -> pending (7) with --require, GREEN (0) without
    903  a red check AND no registry-coherence         -> red     (3) — a finding outranks a "not yet"
    904  registry-coherence only in a SUPERSEDED suite -> pending (7) — the dropped check is exactly the
                                                          one whose verdict we do not have (#710)
    905  the PR still names shaOld; --sha shaNew       -> pending (7) with --sha, GREEN (0) without

902 and 905 are the load-bearing pair: the SAME world scores GREEN without the flag and PENDING with it. That
contrast is the whole reason the flags exist — without them the bot would merge on a rollup that never looked
at its subject, or on the previous commit's checks.

Engine exit codes (ADR-0040 §5): green 0, pending 7, red/conflicted 3, unknown 4.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SUBJECT = "registry-coherence"


def wf(suite, rn, status, concl, event="pull_request"):
    """One workflow_run — the concurrency-group key is path+event+branch+prs (#720)."""
    return {"path": ".github/workflows/gate.yml", "event": event, "head_branch": "auto/registry",
            "run_number": rn, "check_suite_id": suite, "status": status,
            "conclusion": concl, "pull_requests": [{"number": 1}] if event == "pull_request" else []}


def cr(name, suite, status, concl):
    """One check_run. `name` is the JOB name — the thing --require matches, and NOTHING else (#698)."""
    return {"name": name, "check_suite": {"id": suite}, "status": status,
            "conclusion": concl, "app": {"slug": "github-actions"}}


# 905's PR names shaOld — the stale head a force-push leaves behind for a moment. Every other PR is settled.
PULLS = {
    901: {"number": 901, "state": "open", "mergeable": True, "head": {"ref": "auto/registry", "sha": "sha901"}},
    902: {"number": 902, "state": "open", "mergeable": True, "head": {"ref": "auto/registry", "sha": "sha902"}},
    903: {"number": 903, "state": "open", "mergeable": True, "head": {"ref": "auto/registry", "sha": "sha903"}},
    904: {"number": 904, "state": "open", "mergeable": True, "head": {"ref": "auto/registry", "sha": "sha904"}},
    905: {"number": 905, "state": "open", "mergeable": True, "head": {"ref": "auto/registry", "sha": "shaOld"}},
}

RUNS = {
    "sha901": [wf(111, 1, "completed", "success")],
    "sha902": [wf(111, 1, "completed", "success")],
    "sha903": [wf(111, 1, "completed", "success")],
    # 904: the first run was CANCELLED and a later run of its own group replaced it — the state a bot that
    # force-pushes and then edits its PR body manufactures on EVERY reconcile (#710).
    "sha904": [wf(111, 1, "completed", "cancelled"), wf(222, 2, "completed", "success")],
    "shaOld": [wf(111, 1, "completed", "success")],
}

CHECKS = {
    # 901: the subject reported, and it is green.
    "sha901": [cr(SUBJECT, 111, "completed", "success"), cr("build", 111, "completed", "success")],
    # 902: everything green — and the subject is simply NOT THERE. Green to any naive rollup (#606).
    "sha902": [cr("build", 111, "completed", "success")],
    # 903: a real failure, and the subject absent too. Red must win — it is settled, "not yet" is not.
    "sha903": [cr("build", 111, "completed", "failure")],
    # 904: the ONLY registry-coherence is in the superseded suite (111), so it is dropped with its run. Its
    # replacement in suite 222 has not registered yet: pending, and the next poll sees it.
    "sha904": [cr(SUBJECT, 111, "completed", "cancelled"), cr("build", 222, "completed", "success")],
    # 905: the OLD commit's checks — green, and about code that is no longer the PR's head.
    "shaOld": [cr(SUBJECT, 111, "completed", "success")],
}
for _sha in ("sha902", "shaOld"):
    CHECKS[_sha].append(cr("registry-coherence", 777, "completed", "success"))


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response
    # races the engine's pooling HttpClient and RSTs away a written response (#761). Pairs with
    # ThreadingHTTPServer below — a kept-alive connection would pin a single-threaded server.
    protocol_version = "HTTP/1.1"

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
        qs = self.path.split("?", 1)[1] if "?" in self.path else ""

        if re.match(r"^/repos/[^/]+/[^/]+/actions/runs/?$", p):
            m = re.search(r"[?&]head_sha=([^&]+)", "?" + qs)
            sha = m.group(1) if m else ""
            runs = RUNS.get(sha, [])
            return self._send(200, {"total_count": len(runs), "workflow_runs": runs})

        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            checks = CHECKS.get(m.group(1), [])
            return self._send(200, {"total_count": len(checks), "check_runs": checks})

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            if pr in PULLS:
                return self._send(200, PULLS[pr])
            return self._send(404, {"message": "Not Found"})

        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})

        self._send(500, {"message": f"unhandled GET {p}"})


def main():
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

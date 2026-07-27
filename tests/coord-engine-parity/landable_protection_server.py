#!/usr/bin/env python3
"""Case 33 (#1575) — a required context that never REPORTED is not a passing one.

THE MEASURED DEFECT. `landable` returned `green`, exit 0, for FS.GG.Rendering#1027 — which GitHub then
refused to merge:

    $ scripts/fsgg-coord landable 1027 --repo FS.GG.Rendering --wait --sha 69367d92
    green
    $ gh pr merge 1027 --squash
    X Pull request FS-GG/FS.GG.Rendering#1027 is not mergeable: the base branch policy prohibits the merge.

`mergeable=MERGEABLE`, `mergeStateStatus=BLOCKED`, and all 18 check runs that reported on that head were
SUCCESS. The required context `skill-union / skill-union` had NO CHECK RUN AT ALL — the workflow that
produces it was added to `main` AFTER that PR's head was pushed, so GitHub never created the run. A
context that never reports is not a context that fails, and an "is anything red?" rollup cannot see the
difference (#606, arriving through the one set a caller cannot be expected to enumerate).

`--require NAME` (#737) already covered exactly this hazard — but only for contexts the CALLER knows to
name. Branch protection is machine-readable, so the must-have-reported set is derived from the BASE
BRANCH instead. A caller who has to know the answer in advance to ask the question correctly is the shape
this repo keeps closing (#1507, #1510/#1515, #1528).

    950  every check green, the REQUIRED context absent    -> pending (7)
    951  the SAME world, that context reported SUCCESS     -> green   (0)
    952  branch protection we may not READ (403)           -> unknown (4) — never green (#266)
    953  the RULESETS endpoint 404s                        -> unknown (4) — `[]` means "no rules", 404 does not
    954  no classic protection; a RULESET requires it      -> pending (7) — two stores, both bind (#574)
    955  a base that requires nothing                      -> green   (0) — the guard adds nothing of its own

950/951 are the load-bearing pair: the SAME world, differing only in whether the required context
reported. A fixture that only exercised a reported-and-FAILING context would prove nothing about this
defect — a failing check is red on either side of the fix.

Engine exit codes (ADR-0040 §5): green 0, pending 7, red/conflicted 3, unknown 4.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SUBJECT = "skill-union / skill-union"


def wf(pr, suite, rn):
    """One green workflow_run — the concurrency-group key is path+event+branch+prs (#720)."""
    return {"path": ".github/workflows/gate.yml", "event": "pull_request", "head_branch": f"item/{pr}-x",
            "run_number": rn, "check_suite_id": suite, "status": "completed",
            "conclusion": "success", "pull_requests": [{"number": pr}]}


def cr(name, suite=111):
    """One green check_run. `name` is the context branch protection matches on."""
    return {"name": name, "check_suite": {"id": suite}, "status": "completed",
            "conclusion": "success", "app": {"slug": "github-actions"}}


# Each PR merges into a DIFFERENT base, so one fixture can serve six branch policies. Which branch a PR
# merges INTO is what decides the policy that governs it, and the engine reads it rather than assuming.
BASE = {950: "main", 951: "main", 952: "forbidden", 953: "norules", 954: "ruleset", 955: "bare"}

# One branch per PR, so the branch TIP can agree with each head independently — otherwise the #995
# stale-head guard demotes every leg to `pending` and this fixture would pass for the wrong reason.
PULLS = {
    n: {"number": n, "state": "open", "mergeable": True,
        "base": {"ref": BASE[n]}, "head": {"ref": f"item/{n}-x", "sha": f"sha{n}"}}
    for n in BASE
}

RUNS = {f"sha{n}": [wf(n, 111, 1)] for n in BASE}

CHECKS = {
    # 950: everything that reported is GREEN, and the required context is simply NOT THERE. Green to any
    # naive rollup, and refused by GitHub.
    "sha950": [cr("build"), cr("test")],
    # 951: the same world, one check richer — the required context reported, and it passed.
    "sha951": [cr("build"), cr("test"), cr(SUBJECT)],
    "sha952": [cr("build")],
    "sha953": [cr("build")],
    "sha954": [cr("build")],
    "sha955": [cr("build")],
}

# CLASSIC branch protection, per base branch. A branch absent here 404s — which is a real answer about
# THIS store ("no classic protection"), and says nothing about rulesets.
PROTECTION = {
    "main": [SUBJECT],
    "bare": [],
}

# RULESETS, per base branch. `[]` is the answer for a branch with no rules; a 404 is NOT (it means "no
# such repo or branch"), which is why `norules` below is served as one.
RULESETS = {
    "ruleset": [{"type": "required_status_checks",
                 "parameters": {"required_status_checks": [{"context": "coherence", "integration_id": 15368}]}}],
}


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response
    # races the engine's pooling HttpClient and RSTs away a written response (#761).
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
            runs = RUNS.get(m.group(1) if m else "", [])
            return self._send(200, {"total_count": len(runs), "workflow_runs": runs})

        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            checks = CHECKS.get(m.group(1), [])
            return self._send(200, {"total_count": len(checks), "check_runs": checks})

        # The head branch's real tip. It AGREES with every PR's head here, so the #995 stale-head guard is
        # satisfied and the only thing left to decide is #1575's question.
        m = re.match(r"^/repos/[^/]+/[^/]+/git/ref/heads/item/(\d+)-x$", p)
        if m:
            n = m.group(1)
            return self._send(200, {"ref": f"refs/heads/item/{n}-x",
                                    "object": {"sha": f"sha{n}", "type": "commit"}})

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            if pr in PULLS:
                return self._send(200, PULLS[pr])
            return self._send(404, {"message": "Not Found"})

        m = re.match(r"^/repos/[^/]+/[^/]+/branches/([^/]+)/protection$", p)
        if m:
            branch = m.group(1)
            if branch == "forbidden":
                # Reading required status checks needs `administration: read`. "I may not look" is not
                # "there is nothing there" — and it must not become a green.
                return self._send(403, {"message": "Resource not accessible by integration"})
            if branch not in PROTECTION:
                return self._send(404, {"message": "Branch not protected"})
            checks = [{"context": c, "app_id": 15368} for c in PROTECTION[branch]]
            return self._send(200, {"required_status_checks": {"strict": False, "checks": checks}})

        m = re.match(r"^/repos/[^/]+/[^/]+/rules/branches/([^/]+)$", p)
        if m:
            branch = m.group(1)
            if branch == "norules":
                # A branch with no rules answers `[]`. A 404 here means "no such repo or branch", and
                # inferring "unprotected" from it would manufacture a green out of nothing (#574).
                return self._send(404, {"message": "Not Found"})
            return self._send(200, RULESETS.get(branch, []))

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

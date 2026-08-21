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
produces it was armed on `main` AFTER that PR's head was pushed, so GitHub never created the run. A
context that never reports is not a context that fails, and an "is anything red?" rollup cannot see the
difference (#606).

#1575 PRESCRIBED DERIVING THE REQUIRED SET FROM `branches/{b}/protection`, and that remedy is the one
thing here that had to be corrected. That read needs `administration: read` — **not a valid
`permissions:` scope for a workflow's GITHUB_TOKEN at all** — and `landable`'s unattended caller,
`skill-registry-autofix.yml`, "runs entirely under GITHUB_TOKEN" by its own words. A verdict resting on
it would return exit 4 there forever: #463 restored, where a protection probe 403'd on every receiver and
stopped the kit landing anywhere. #463's ratified repair was to ask the PULL REQUEST instead.

So the VERDICT is `mergeable_state`, which rides in the PR object the command already reads — no extra
request, no extra scope — and the policy read is DIAGNOSIS that is allowed to fail.

    950  blocked; every reporting check green   -> pending (7)  the defect
    951  the SAME world, clean                  -> green   (0)  work must still land
    952  blocked; protection 403s               -> pending (7)  the refusal STANDS; only the reason is lost
    953  blocked; a RULESET names the context   -> pending (7)  two stores, both diagnosed (#574)
    954  unstable                               -> green   (0)  a NON-required check failed; GitHub merges
    955  no mergeable_state at all              -> green   (0)  no opinion is not a refusal
    956  behind                                 -> pending (7)
    957  draft                                  -> pending (7)

950/951 are the load-bearing pair: the SAME world, differing only in what GitHub says it will do with it.
952 is the one that keeps the fleet alive — it is the leg that would go exit 4 under #1575's own
prescription, and it must be a 7 with a degraded sentence instead.

Engine exit codes (ADR-0040 §5): green 0, pending 7, red/conflicted 3, unknown 4.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SUBJECT = "skill-union / skill-union"

# Per PR: the `mergeable_state` GitHub reports, and the base branch it merges into. One branch per PR so
# one fixture can serve several branch policies.
STATE = {
    950: "blocked",
    951: "clean",
    952: "forbidden",   # served as `blocked`; its BASE is what 403s — see BASE below
    953: "blocked",
    954: "unstable",
    955: None,          # the field is absent entirely
    956: "behind",
    957: "draft",
}

BASE = {950: "main", 951: "main", 952: "forbidden", 953: "ruleset",
        954: "main", 955: "main", 956: "main", 957: "main"}

# 952's state on the wire is an ordinary `blocked`; only its policy read fails.
WIRE_STATE = dict(STATE)
WIRE_STATE[952] = "blocked"


def pull(n):
    p = {"number": n, "state": "open", "mergeable": True,
         "base": {"ref": BASE[n]}, "head": {"ref": f"item/{n}-x", "sha": f"sha{n}"}}
    if WIRE_STATE[n] is not None:
        p["mergeable_state"] = WIRE_STATE[n]
    return p


PULLS = {n: pull(n) for n in STATE}


def wf(pr):
    """One green workflow_run — the concurrency-group key is path+event+branch+prs (#720)."""
    return {"path": ".github/workflows/gate.yml", "event": "pull_request", "head_branch": f"item/{pr}-x",
            "run_number": 1, "check_suite_id": 111, "status": "completed",
            "conclusion": "success", "pull_requests": [{"number": pr}]}


def cr(name):
    """One green check_run."""
    return {"name": name, "check_suite": {"id": 111}, "status": "completed",
            "conclusion": "success", "app": {"slug": "github-actions"}}


RUNS = {f"sha{n}": [wf(n)] for n in STATE}

# Everything that REPORTED is green everywhere. 951 additionally carries the required context; every
# other leg does not — which is the whole point: nothing here is red, and the naive rollup says so.
CHECKS = {f"sha{n}": [cr("build"), cr("test"), cr("registry-coherence")] for n in STATE}
CHECKS["sha951"] = [cr("build"), cr("test"), cr("registry-coherence"), cr(SUBJECT)]

# CLASSIC protection, per base branch. A branch absent here 404s — a real answer about THIS store.
PROTECTION = {"main": [SUBJECT]}

# RULESETS, per base branch. `[]` is the answer for a branch with no rules; a 404 is not.
RULESETS = {
    "ruleset": [{"type": "required_status_checks",
                 "parameters": {"required_status_checks": [{"context": "coherence",
                                                            "integration_id": 15368}]}}],
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
                # Reading required status checks needs `administration: read`, which a workflow's
                # GITHUB_TOKEN cannot hold. This is the shape #463 met on every receiver — and the
                # verdict must NOT depend on it.
                return self._send(403, {"message": "Resource not accessible by integration"})
            if branch not in PROTECTION:
                return self._send(404, {"message": "Branch not protected"})
            checks = [{"context": c, "app_id": 15368} for c in PROTECTION[branch]]
            return self._send(200, {"required_status_checks": {"strict": False, "checks": checks}})

        m = re.match(r"^/repos/[^/]+/[^/]+/rules/branches/([^/]+)$", p)
        if m:
            return self._send(200, RULESETS.get(m.group(1), []))

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

#!/usr/bin/env python3
"""Case 31 (superseded-run-720) — a SUPERSEDED run is not a RED one, driven through the `landable` command.

`pr_landable` once aggregated every check-run on the head SHA. But the SAME workflow can run TWICE on one
SHA — a second trigger starts a fresh run and `concurrency: cancel-in-progress` CANCELS the first — and
BOTH runs' check-runs stay attached to that SHA forever. The raw aggregate sees `cancelled` and calls a
GREEN, force-pushed, mergeable PR red. It is `adopt` that paid: its whole population is PRs whose author was
ITERATING when their harness died — i.e. force-pushed PRs, exactly the ones carrying superseded runs.

`Landable.score`/`supersede` (Core, unit-tested) already fixed the scoring for case 30's single-run worlds.
This fixture drives case 31's MULTI-run worlds through the engine over HTTP, via the first-class `landable`
command — the #697/#720 verdict as a query. Each PR is mergeable; only its runs and check-runs differ, and
the verdict is scored over the head SHA's WORKFLOW RUNS unioned with its CHECK RUNS, a superseded suite
dropped (#720). One PR per leg, no board and no claim machinery — the scoring, isolated.

    801  cancelled run REPLACED by a later run of its own group  -> SUPERSEDED, dropped        -> green
    802  cancelled run NOBODY re-ran                              -> the drop rule is not a hole -> red
    803  cancelled + a workflow_dispatch run (a DIFFERENT group)  -> supersedes nothing (#703)   -> red
    804  ZERO runs                                                -> an empty subject (#606)      -> red
    805  a run still in_progress                                  -> not a run that passed        -> pending
    806  green Actions run + a FAILING third-party check (codecov) -> the rollup must not go blind  -> red
    807  a startup_failure run (NO check-runs) + a green sibling  -> a run can fail with no checks -> red
    808  a green run + a FAILED check-run (continue-on-error)     -> the two rollups' UNION        -> red
    809  a real-SIZED payload (one green run + green check, padded) -> the engine reads JSON, no argv -> green

Leg 9 is the corpus's argv-128KB-cap leg (#720). In bash the rollup piped both lists to jq through
`--argjson`, i.e. through ARGV, and a real ~150KB run set tripped MAX_ARG_STRLEN — jq died, the verdict was
`unknown`, and `adopt` refused every real PR. The engine reads the JSON straight off `HttpClient`; there is
no argv leg at all, so the failure mode is STRUCTURALLY ABSENT. The fat payload is served anyway to prove the
engine rolls a real-sized body up to `green` without a wobble.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"

# ~200KB of padding — past the single-argv ceiling the bash rollup tripped, trivial for a JSON reader.
PAD = "x" * 200000


def wf(suite, rn, status, concl, event="pull_request", **extra):
    """One workflow_run, as GitHub shapes it — the concurrency-group key is path+event+branch+prs (#720)."""
    r = {"path": ".github/workflows/gate.yml", "event": event, "head_branch": "item/1-x",
         "run_number": rn, "check_suite_id": suite, "status": status,
         "conclusion": concl, "pull_requests": [{"number": 1}] if event == "pull_request" else []}
    r.update(extra)
    return r


def cr(suite, status, concl, app="github-actions", **extra):
    """One check_run, as GitHub shapes it."""
    c = {"name": "job", "check_suite": {"id": suite}, "status": status,
         "conclusion": concl, "app": {"slug": app}}
    c.update(extra)
    return c


# Every leg's PR is mergeable; the verdict comes entirely from the runs + checks on its head SHA.
PULLS = {n: {"number": n, "state": "open", "mergeable": True,
             "head": {"ref": "item/1-x", "sha": f"sha{n}"}}
         for n in range(801, 810)}

RUNS = {
    # 1. cancelled run REPLACED by a later run of its own group -> superseded -> green.
    "sha801": [wf(111, 1, "completed", "cancelled"), wf(222, 2, "completed", "success")],
    # 2. cancelled run nobody re-ran -> still a finding -> red.
    "sha802": [wf(111, 1, "completed", "cancelled")],
    # 3. a workflow_dispatch run is a DIFFERENT concurrency group -> supersedes nothing (#703) -> red.
    "sha803": [wf(111, 1, "completed", "cancelled"),
               wf(333, 9, "completed", "success", event="workflow_dispatch")],
    # 4. zero runs -> an empty subject, not a clean one (#606) -> red.
    "sha804": [],
    # 5. a run still going -> pending.
    "sha805": [wf(111, 1, "in_progress", None)],
    # 6. a green Actions run; the FAILING check is third-party (below), never in the runs list.
    "sha806": [wf(111, 1, "completed", "success")],
    # 7. a startup_failure run produces NO check-run; its GREEN SIBLING is the assertion (see below).
    "sha807": [wf(111, 1, "completed", "startup_failure"), wf(222, 1, "completed", "success")],
    # 8. a green run whose check-run FAILED (job-level continue-on-error) -> the union reds it.
    "sha808": [wf(111, 1, "completed", "success")],
    # 9. a real-sized payload — one green run, padded past the argv ceiling.
    "sha809": [wf(111, 1, "completed", "success", repository={"description": PAD})],
}

CHECKS = {
    "sha801": [cr(111, "completed", "cancelled"), cr(222, "completed", "success")],
    "sha802": [cr(111, "completed", "cancelled")],
    "sha803": [cr(111, "completed", "cancelled"), cr(333, "completed", "success")],
    "sha804": [],
    "sha805": [cr(111, "in_progress", None)],
    # 6. the third-party app's suite (999) is never in the runs list, so it is scored by construction (#720).
    "sha806": [cr(111, "completed", "success"), cr(999, "completed", "failure", app="codecov")],
    # 7. only the green sibling's suite has a check-run; the startup_failure run produced none.
    "sha807": [cr(222, "completed", "success")],
    "sha808": [cr(111, "completed", "failure")],
    "sha809": [cr(111, "completed", "success", output={"text": PAD})],
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
        qs = self.path.split("?", 1)[1] if "?" in self.path else ""

        # The head SHA's WORKFLOW RUNS (#720) — the object body GitHub returns, keyed by head_sha.
        if re.match(r"^/repos/[^/]+/[^/]+/actions/runs/?$", p):
            m = re.search(r"[?&]head_sha=([^&]+)", "?" + qs)
            sha = m.group(1) if m else ""
            runs = RUNS.get(sha, [])
            return self._send(200, {"total_count": len(runs), "workflow_runs": runs})

        # The head SHA's CHECK RUNS — scored in union with the runs (#720).
        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            sha = m.group(1)
            checks = CHECKS.get(sha, [])
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
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

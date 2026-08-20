#!/usr/bin/env python3
"""Case 31 (#724) — `landable --wait`, the poll loop that does NOT believe an early green.

The single-shot verdict (`landable_super_server.py`) fixed the SCORING. `--wait` carries the one thing the
recipe's loop did that a single read cannot: it must not settle on a PREMATURE green. GitHub schedules a
PR's workflows over 20-60s, so two premature-green traps are both invisible to a naive "poll until not
pending", and this fixture drives the engine's poll loop through both:

    801  a settled, superseded-but-green PR                 -> agrees with the single-shot verdict -> green (0)
    804  ZERO runs, ever (the registration race never ends) -> not "CI failed", "CI never started"  -> red   (3)
    810  a GROWING run set — 1 green run, then that run PLUS a failed one on the SECOND read
                                                            -> waits for the set to STOP GROWING     -> red   (3)
    704  a CONFLICTED PR (mergeable=false — no CI at all)   -> returns AT ONCE, not after a timeout  -> conflicted (3)

TRAP ONE — THE REGISTRATION RACE (804). For the first 20-60s after a push there are zero runs, and zero runs
score RED (#606, an empty subject is a finding). A waiter that believes that rejects every PR for being new,
so while N==0 it KEEPS WAITING; only if the runs never register does the red stand — the honest #606 verdict.

TRAP TWO — THE PARTIAL ROLLUP (810), the one that MERGES A BAD PR. An early poll can legitimately see "1 run,
green" while the failing one has not been CREATED yet. This server serves a DIFFERENT run/check set on the
SECOND read of sha810 (exactly as GitHub does — the set grows): first poll sees one green run; the next sees
that run PLUS a failed one. A waiter that trusts the first reading returns green; the engine must return red,
because it believes a green only once the subject count has STOPPED GROWING across two consecutive polls.

The engine's exit codes are its own disposed contract (ADR-0040 §5): green 0, pending 7, red/conflicted 3,
unknown 4 — where bash's poll loop numbers green/pending/red 0/3/1. The PROPERTY (green 0; pending a distinct
retryable code; red/conflicted a distinct do-not-wait code) is what run.sh asserts, not bash's literals.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"


def wf(suite, rn, status, concl, event="pull_request"):
    """One workflow_run, as GitHub shapes it — the concurrency-group key is path+event+branch+prs (#720)."""
    return {"path": ".github/workflows/gate.yml", "event": event, "head_branch": "item/1-x",
            "run_number": rn, "check_suite_id": suite, "status": status,
            "conclusion": concl, "pull_requests": [{"number": 1}] if event == "pull_request" else []}


def cr(suite, status, concl, app="github-actions"):
    """One check_run, as GitHub shapes it."""
    return {"name": "job", "check_suite": {"id": suite}, "status": status,
            "conclusion": concl, "app": {"slug": app}}


def registry():
    value = cr(777, "completed", "success")
    value["name"] = "registry-coherence"
    return value


# Every mergeable leg's PR is mergeable=true; 704 is CONFLICTED (mergeable=false, no CI at all).
PULLS = {
    801: {"number": 801, "state": "open", "mergeable": True, "head": {"ref": "item/1-x", "sha": "sha801"}},
    804: {"number": 804, "state": "open", "mergeable": True, "head": {"ref": "item/1-x", "sha": "sha804"}},
    810: {"number": 810, "state": "open", "mergeable": True, "head": {"ref": "item/1-x", "sha": "sha810"}},
    704: {"number": 704, "state": "open", "mergeable": False,
          "head": {"ref": "item/975-conflicted", "sha": "conflicted704"}},
}

# Static worlds: 801 is settled (superseded-but-green), 804 has no runs ever.
RUNS = {
    "sha801": [wf(111, 1, "completed", "cancelled"), wf(222, 2, "completed", "success")],
    "sha804": [],
}
CHECKS = {
    "sha801": [cr(111, "completed", "cancelled"), cr(222, "completed", "success"), registry()],
    "sha804": [],
}

# 810 GROWS between polls: the FIRST read of its runs/checks is one green subject; every read after that is
# that subject PLUS a failed one — the partial rollup the waiter must not believe.
GROW_SHA = "sha810"
GROW_RUNS_FIRST = [wf(111, 1, "completed", "success")]
GROW_RUNS_REST = [wf(111, 1, "completed", "success"), wf(222, 1, "completed", "failure")]
GROW_CHECKS_FIRST = [cr(111, "completed", "success"), registry()]
GROW_CHECKS_REST = [cr(111, "completed", "success"), cr(222, "completed", "failure"), registry()]

# Per-endpoint read counters for the growing SHA — the fixture is STATEFUL, exactly as GitHub's scheduling is.
seen = {"runs": 0, "checks": 0}


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

        # The head SHA's WORKFLOW RUNS (#720) — the set GROWS on the second read of the growing SHA.
        if re.match(r"^/repos/[^/]+/[^/]+/actions/runs/?$", p):
            m = re.search(r"[?&]head_sha=([^&]+)", "?" + qs)
            sha = m.group(1) if m else ""
            if sha == GROW_SHA:
                seen["runs"] += 1
                runs = GROW_RUNS_FIRST if seen["runs"] == 1 else GROW_RUNS_REST
            else:
                runs = RUNS.get(sha, [])
            return self._send(200, {"total_count": len(runs), "workflow_runs": runs})

        # The head SHA's CHECK RUNS — likewise grows on the second read of the growing SHA.
        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            sha = m.group(1)
            if sha == GROW_SHA:
                seen["checks"] += 1
                checks = GROW_CHECKS_FIRST if seen["checks"] == 1 else GROW_CHECKS_REST
            else:
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
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

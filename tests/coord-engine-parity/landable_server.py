#!/usr/bin/env python3
"""Case 30 (pr-existence-697) — WHAT the orphaned PR SAYS, not merely that it exists.

#581 taught the tools that an open `item/<n>-*` PR is proof of life, and stopped there — so `reap` refuses
such a claim and then offers exactly one exit, "close it, then reap". For a PR that is green, reviewed and
mergeable, that exit DESTROYS the best work on the board. #697 reads the CI/mergeability slice of what the
PR says (#720's green / conflicted / pending / red / unknown verdict), so `who` and `reap` can tell an
abandoned branch from work that must be preserved. #2854 prevents `who` from promoting that narrow slice
to a finished/landing verdict without the review ledger.

The world (lifted from the corpus's #697 seeds, one transport over). FS.GG.SDD, two OFF-BOARD stale claims:

    970  stale marker ghost-970, PR #701 OPEN on item/970-finished  -> GREEN checks, changes-required review
    976  stale marker ghost-976, PR #705 OPEN on item/976-running   -> mergeable, checks RUNNING -> pending

The corpus (case 30, `who` + `reap` legs) certifies:
    who (text)  -> #970's row reports green checks and points at `landable`, without saying finished/land;
                   #976's reads `STALE (#705 OPEN — checks running)`.
    who --json  -> #970 carries `prState: "green"`, #976 `prState: "pending"`.
    reap        -> REFUSES #970 as FINISHED and names `adopt` (never "close it, then reap"); REFUSES #976
                   as UNFINISHED ("Do NOT close it"); DELETES NOTHING — both claims survive.

The CI slice is scored over WORKFLOW RUNS (Actions) unioned with CHECK RUNS (#720): #970's head SHA has
one green run + one green check; #976's has a green run + an in-progress run (and matching check-runs).
PR #701's comment thread separately carries a digest-valid initial `changes-required` review record.
"""

import hashlib
import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
RATE = {"cost": 1, "remaining": 4976}

# Every open issue in the repo, with its body (the off-board scan carries bodies for free).
ISSUES = {
    970: {"title": "Finished, green, and orphaned", "body": "Paths: src/Orphan970"},
    976: {"title": "Orphaned mid-CI", "body": "Paths: src/Orphan976"},
}

# Neither is on the board — both are claims the board never knew about (off-board).
BOARD = {}

# The markers: (worker, comment_id). Both 3h old -> well past a 120m lease -> stale.
MARKERS = {970: ("ghost-970", 970), 976: ("ghost-976", 976)}

# One OPEN PR per item, on its `item/<n>-*` branch — the worktree protocol's own proof of life (#581).
# Each carries its head SHA and mergeability; the landable verdict is scored off the SHA's runs + checks.
GREEN_HEAD = "a" * 40
PENDING_HEAD = "b" * 40

PULLS = {
    701: {"number": 701, "state": "open", "mergeable": True,
          "head": {"ref": "item/970-finished", "sha": GREEN_HEAD}},
    705: {"number": 705, "state": "open", "mergeable": True,
          "head": {"ref": "item/976-running", "sha": PENDING_HEAD}},
}
ITEM_PR = {970: 701, 976: 705}

# Workflow runs by head SHA. #970 is one green run; #976 is a green run plus one still in_progress —
# supersedes nothing (distinct run numbers, no cancellation), so #976 is `pending`, not green.
RUNS = {
    GREEN_HEAD: [
        {"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/970-finished",
         "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 1,
         "pull_requests": [{"number": 701}]},
    ],
    PENDING_HEAD: [
        {"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/976-running",
         "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 2,
         "pull_requests": [{"number": 705}]},
        {"path": ".github/workflows/test.yml", "event": "pull_request", "head_branch": "item/976-running",
         "run_number": 1, "status": "in_progress", "conclusion": None, "check_suite_id": 3,
         "pull_requests": [{"number": 705}]},
    ],
}

# Check runs by head SHA (the union the rollup also scores, #720). They agree with the runs above.
CHECKS = {
    GREEN_HEAD: [{"name": "build", "status": "completed", "conclusion": "success",
                  "app": {"slug": "github-actions"}, "check_suite": {"id": 1}}],
    PENDING_HEAD: [{"name": "build", "status": "completed", "conclusion": "success",
                 "app": {"slug": "github-actions"}, "check_suite": {"id": 2}},
                {"name": "test", "status": "in_progress", "conclusion": None,
                 "app": {"slug": "github-actions"}, "check_suite": {"id": 3}}],
}

LOCK = threading.Lock()
_DELETES = []


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _frame(value):
    return f"{len(value.encode('utf-8'))}:{value}"


def _changes_required_review():
    record = {
        "schema": "fsgg.coord.review-decision/v2",
        "subject": "FS-GG/FS.GG.SDD#970/pr/701",
        "revision": 1,
        "previousDigest": None,
        "headSha": GREEN_HEAD,
        "claimGeneration": None,
        "baseSha": None,
        "critic": "heron-970",
        "verdict": "changes-required",
        "acceptedExceptions": [],
        "routeApplicability": "not-meaningful",
        "routeEvidence": ["fixture has no meaningful runtime-route comparison"],
        "policyVersion": "structured-decisions/1",
        "kind": "initial",
        "round": 0,
        "initialReview": None,
        "precedingReview": None,
        "diffAuditRequired": False,
        "diffAuditReceipts": [],
        "succession": None,
        "repairPhaseReceipt": None,
        "timestamp": "2026-08-23T18:00:00Z",
        "digest": "",
    }
    fields = [
        _frame(record["schema"]), _frame(record["subject"]), str(record["revision"]), _frame(""),
        _frame(record["headSha"]), _frame(record["critic"]), _frame(record["verdict"]), "",
        _frame(record["routeApplicability"]), "".join(_frame(x) for x in record["routeEvidence"]),
        _frame(record["policyVersion"]), _frame(record["kind"]), str(record["round"]), _frame(""),
        _frame(""), str(record["diffAuditRequired"]), "", _frame(record["timestamp"]),
    ]
    record["digest"] = hashlib.sha256("|".join(fields).encode()).hexdigest()
    return "<!-- fsgg:review-decision/v2 -->\n" + json.dumps(record, separators=(",", ":"))


def comments(n):
    if n == 701:
        return [{"id": 70101, "body": _changes_required_review(), "html_url": "https://reviews/70101",
                 "user": {"login": "EHotwagner"}, "created_at": "2026-08-23T18:00:00Z",
                 "updated_at": "2026-08-23T18:00:00Z"}]
    if n not in MARKERS:
        return []
    worker, cid = MARKERS[n]
    ts = _iso(3)
    return [{"id": cid, "body": f"<!-- fsgg:claim worker={worker} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]


def open_issues():
    return [{"number": n, "title": ISSUES[n]["title"], "state": "open", "body": ISSUES[n]["body"]}
            for n in sorted(ISSUES)]


def open_pulls():
    return list(PULLS.values())


def board_items():
    nodes = []
    for n, status in BOARD.items():
        nodes.append({
            "status": {"name": status},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": ISSUES[n]["title"], "state": "OPEN",
                        "repository": {"nameWithOwner": f"{OWNER}/{REPO}"}},
        })
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}},
        "rateLimit": RATE}}


def graphql(query):
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "projectItems" in query:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": []}}}}, "rateLimit": RATE}
    if "items(first" in query:
        return board_items()
    return None


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

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        p = self.path.split("?", 1)[0]
        if p.rstrip("/") == "/graphql":
            try:
                q = json.loads(raw).get("query", "")
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            a = graphql(q)
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_DELETE(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            with LOCK:
                _DELETES.append(int(m.group(1)))
            self.send_response(204)
            self.end_headers()
            return
        self._send(500, {"message": f"unhandled DELETE {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        qs = self.path.split("?", 1)[1] if "?" in self.path else ""

        if p == "/_deletes":
            with LOCK:
                return self._send(200, list(_DELETES))

        # The head SHA's WORKFLOW RUNS (#720) — the object body GitHub returns, keyed by head_sha.
        if re.match(r"^/repos/[^/]+/[^/]+/actions/runs/?$", p):
            m = re.search(r"[?&]head_sha=([^&]+)", "?" + qs)
            sha = m.group(1) if m else ""
            return self._send(200, {"total_count": len(RUNS.get(sha, [])), "workflow_runs": RUNS.get(sha, [])})

        # The head SHA's CHECK RUNS — scored in union with the runs (#720).
        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            sha = m.group(1)
            return self._send(200, {"total_count": len(CHECKS.get(sha, [])), "check_runs": CHECKS.get(sha, [])})

        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, open_issues())

        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, open_pulls())

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            if pr in PULLS:
                return self._send(200, PULLS[pr])
            return self._send(404, {"message": "Not Found"})

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in ISSUES:
                return self._send(200, {"number": n, "body": ISSUES[n]["body"]})
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

#!/usr/bin/env python3
"""Case 30 (pr-existence-697) — the `adopt` command: take over an orphan for guarded delivery.

`reap` refuses a stale claim whose PR is open (#581) and then offers exactly one exit, "close it, then
reap" — which for a green, mergeable, reviewed PR DESTROYS a worker's finished work. `adopt` lets a worker
continue another worker's orphaned PR through one verified transfer. The gate is what makes it safe: green
and mergeable is necessary, and an exact-head host-accepted review is necessary independently (#2854).

The world (case 30's #697 seeds, one transport over). FS.GG.SDD, eight off-board claims:

    970  stale ghost-970, PR #701 GREEN + HOST-ACCEPTED    -> transfer to heron-697 for typed delivery
    971  stale ghost-971, PR #702 mergeable=false          -> CONFLICTED  -> refuse, lock survives
    972  stale ghost-972, PR #703 mergeable, ZERO checks   -> NOT green (#606) -> refuse, lock survives
    973  FRESH busy-973 (a LIVE claim)                     -> not an orphan -> refuse, lock survives
    974  stale ghost-974, NO open PR                       -> nothing to land -> refuse, lock survives
    975  stale ghost-975, PR #704 mergeable=null then false-> lazy re-read sees CONFLICTED -> refuse, survives
    976  stale ghost-976, PR #705 mergeable, checks RUNNING-> pending -> refuse, lock survives
    977  stale ghost-977, PR #706 GREEN, changes-required  -> review refusal, lock survives

The accepted transfer reuses `claim`: `adopt 'FS.GG.SDD#970'` POSTs heron-697's marker under the CAS, so after it
the live winner is heron-697 (ghost-970's stale marker is left for reap). #975's PR #704 returns
`mergeable: null` on the FIRST read and `false` on later reads (GitHub computes mergeability lazily) — the
engine re-reads and sees the conflict.
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

ACCEPTED_HEAD = "a" * 40
CHANGES_REQUIRED_HEAD = "d" * 40

ISSUES = {
    970: {"title": "Finished, green, and orphaned", "body": "Paths: src/Orphan970"},
    971: {"title": "Orphaned but conflicted", "body": "Paths: src/Orphan971"},
    972: {"title": "Orphaned, mergeable, never tested", "body": "Paths: src/Orphan972"},
    973: {"title": "Alive and well", "body": "Paths: src/Orphan973"},
    974: {"title": "Dead, nothing to show for it", "body": "Paths: src/Orphan974"},
    975: {"title": "Mergeability not computed yet", "body": "Paths: src/Orphan975"},
    976: {"title": "Orphaned mid-CI", "body": "Paths: src/Orphan976"},
    977: {"title": "Green checks, changes required", "body": "Paths: src/Orphan977"},
}

# (worker, comment_id, hours_since_beat). 3h > 120m lease -> stale; 0h -> fresh -> live.
MARKERS = {
    970: ("ghost-970", 9700, 3),
    971: ("ghost-971", 9710, 3),
    972: ("ghost-972", 9720, 3),
    973: ("busy-973", 9730, 0),
    974: ("ghost-974", 9740, 3),
    975: ("ghost-975", 9750, 3),
    976: ("ghost-976", 9760, 3),
    977: ("ghost-977", 9770, 3),
}

# The open PRs, by number. 703/704 return their mergeable per the lazy/zero-checks worlds.
PULLS = {
    701: {"number": 701, "state": "open", "mergeable": True,
          "head": {"ref": "item/970-finished", "sha": ACCEPTED_HEAD}},
    702: {"number": 702, "state": "open", "mergeable": False,
          "head": {"ref": "item/971-conflicted", "sha": "c0nflict"}},
    703: {"number": 703, "state": "open", "mergeable": True,
          "head": {"ref": "item/972-no-checks", "sha": "n0checks"}},
    705: {"number": 705, "state": "open", "mergeable": True,
          "head": {"ref": "item/976-running", "sha": "pend976"}},
    706: {"number": 706, "state": "open", "mergeable": True,
          "head": {"ref": "item/977-changes-required", "sha": CHANGES_REQUIRED_HEAD}},
}
# 974 has no PR; 973 is a live claim (no PR needed). 704 (item/975-lazy) is served specially below.
LAZY_PR = {"number": 704, "state": "open", "head": {"ref": "item/975-lazy", "sha": "lazysha"}}

RUNS = {
    ACCEPTED_HEAD: [{"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/970-finished",
                  "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 1,
                  "pull_requests": [{"number": 701}]}],
    CHANGES_REQUIRED_HEAD: [{"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/977-changes-required",
                            "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 4,
                            "pull_requests": [{"number": 706}]}],
    "n0checks": [],
    "pend976": [{"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/976-running",
                 "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 2,
                 "pull_requests": [{"number": 705}]},
                {"path": ".github/workflows/test.yml", "event": "pull_request", "head_branch": "item/976-running",
                 "run_number": 1, "status": "in_progress", "conclusion": None, "check_suite_id": 3,
                 "pull_requests": [{"number": 705}]}],
}
CHECKS = {
    ACCEPTED_HEAD: [{"name": "build", "status": "completed", "conclusion": "success",
                  "app": {"slug": "github-actions"}, "check_suite": {"id": 1}}],
    CHANGES_REQUIRED_HEAD: [{"name": "build", "status": "completed", "conclusion": "success",
                            "app": {"slug": "github-actions"}, "check_suite": {"id": 4}}],
    "n0checks": [],
    "pend976": [{"name": "build", "status": "completed", "conclusion": "success",
                 "app": {"slug": "github-actions"}, "check_suite": {"id": 2}},
                {"name": "test", "status": "in_progress", "conclusion": None,
                 "app": {"slug": "github-actions"}, "check_suite": {"id": 3}}],
}

LOCK = threading.Lock()
_POSTED = {}          # issue number -> [comment,...] posted by the transfer
_NEXT_ID = [10000]    # every posted marker id is HIGHER than the seeded stale ones
_LAZY_READS = [0]     # how many times pulls/704 has been read (null first, false after)


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _frame(value):
    return f"{len(value.encode('utf-8'))}:{value}"


def _seal_review(record):
    fields = [
        _frame(record["schema"]), _frame(record["subject"]), str(record["revision"]),
        _frame(record["previousDigest"] or ""), _frame(record["headSha"]), _frame(record["critic"]),
        _frame(record["verdict"]), "".join(_frame(x) for x in record["acceptedExceptions"]),
        _frame(record["routeApplicability"]), "".join(_frame(x) for x in record["routeEvidence"]),
        _frame(record["policyVersion"]), _frame(record["kind"]), str(record["round"]),
        _frame(record["initialReview"] or ""), _frame(record["precedingReview"] or ""),
        str(record["diffAuditRequired"]), "".join(_frame(x) for x in record["diffAuditReceipts"]),
        _frame(record["timestamp"]),
    ]
    if record["claimGeneration"] is not None or record["baseSha"] is not None:
        fields.extend([_frame(record["claimGeneration"] or ""), _frame(record["baseSha"] or "")])
    record["digest"] = hashlib.sha256("|".join(fields).encode()).hexdigest()
    return record


def _initial_review(item, pr, head, critic, verdict):
    return _seal_review({
        "schema": "fsgg.coord.review-decision/v2", "subject": f"FS-GG/FS.GG.SDD#{item}/pr/{pr}",
        "revision": 1, "previousDigest": None, "headSha": head, "claimGeneration": None,
        "baseSha": None, "critic": critic, "verdict": verdict, "acceptedExceptions": [],
        "routeApplicability": "not-meaningful",
        "routeEvidence": ["fixture has no meaningful runtime-route comparison"],
        "policyVersion": "structured-decisions/1", "kind": "initial", "round": 0,
        "initialReview": None, "precedingReview": None, "diffAuditRequired": False,
        "diffAuditReceipts": [], "succession": None, "repairPhaseReceipt": None,
        "timestamp": "2026-08-23T18:00:00Z", "digest": "",
    })


def _review_comment(cid, url, record):
    return {"id": cid, "body": "<!-- fsgg:review-decision/v2 -->\n" + json.dumps(record, separators=(",", ":")),
            "html_url": url, "user": {"login": "EHotwagner"},
            "created_at": record["timestamp"], "updated_at": record["timestamp"]}


_accepted_initial_url = "https://fixture.invalid/reviews/970/1"
_accepted_initial = _initial_review(970, 701, ACCEPTED_HEAD, "heron-critic-970", "pass")
_accepted = dict(_accepted_initial)
_accepted.update({
    "revision": 2, "previousDigest": _accepted_initial["digest"], "claimGeneration": "9700",
    "baseSha": "c" * 40, "verdict": "accepted", "kind": "acceptance",
    "initialReview": _accepted_initial_url, "precedingReview": _accepted_initial_url,
    "timestamp": "2026-08-23T18:01:00Z", "digest": "",
})
_seal_review(_accepted)
_changes_required = _initial_review(977, 706, CHANGES_REQUIRED_HEAD, "swift-critic-977", "changes-required")
REVIEW_COMMENTS = {
    701: [_review_comment(70101, _accepted_initial_url, _accepted_initial),
          _review_comment(70102, "https://fixture.invalid/reviews/970/2", _accepted)],
    706: [_review_comment(70601, "https://fixture.invalid/reviews/977/1", _changes_required)],
}


def comments(n):
    out = list(REVIEW_COMMENTS.get(n, []))
    if n in MARKERS:
        worker, cid, hrs = MARKERS[n]
        ts = _iso(hrs)
        out.append({"id": cid, "body": f"<!-- fsgg:claim worker={worker} lease=120 -->\nheld",
                    "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts})
    out.extend(_POSTED.get(n, []))
    return out


def open_issues():
    return [{"number": n, "title": ISSUES[n]["title"], "state": "open", "body": ISSUES[n]["body"]}
            for n in sorted(ISSUES)]


def open_pulls():
    pulls = list(PULLS.values())
    # The lazy PR is open too (prAlive must find item/975-*); its mergeable is not read from the LIST.
    pulls.append({"number": 704, "state": "open", "head": {"ref": "item/975-lazy", "sha": "lazysha"}})
    return pulls


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
        # Off-board: the issue exists but sits on no board item -> `Ok None` -> the transfer's Status write
        # has nothing to write, and is best-effort (#331).
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": []}}}}, "rateLimit": RATE}
    if "items(first" in query:
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}}, "rateLimit": RATE}}
    if "updateProjectV2ItemFieldValue" in query or "clearProjectV2ItemFieldValue" in query:
        return {"data": {"set": {"projectV2Item": {"id": "PVTI_x"}}}, "rateLimit": RATE}
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

        # The transfer's marker POST — store it and hand back the id (the CAS reads `.id`).
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                return self._send(400, {"message": "bad body"})
            with LOCK:
                cid = _NEXT_ID[0]
                _NEXT_ID[0] += 1
                ts = _iso(0)
                _POSTED.setdefault(n, []).append(
                    {"id": cid, "body": body, "user": {"login": "EHotwagner"},
                     "created_at": ts, "updated_at": ts})
            return self._send(201, {"id": cid})

        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        qs = self.path.split("?", 1)[1] if "?" in self.path else ""

        if re.match(r"^/repos/[^/]+/[^/]+/actions/runs/?$", p):
            m = re.search(r"[?&]head_sha=([^&]+)", "?" + qs)
            sha = m.group(1) if m else ""
            return self._send(200, {"total_count": len(RUNS.get(sha, [])), "workflow_runs": RUNS.get(sha, [])})

        m = re.match(r"^/repos/[^/]+/[^/]+/commits/([^/]+)/check-runs$", p)
        if m:
            sha = m.group(1)
            return self._send(200, {"total_count": len(CHECKS.get(sha, [])), "check_runs": CHECKS.get(sha, [])})

        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, open_issues())

        if re.match(r"^/repos/[^/]+/[^/]+/git/matching-refs/heads/item/", p):
            # #1055: no pushed item/<n>-* branch modeled → prAlive's branch probe finds none. The #974 no-PR
            # leg stays LeaseExpiredNoPr (a dead claim adopt refuses as "no finished work"), not Unknown.
            return self._send(200, [])
        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, open_pulls())

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            if pr == 704:
                # Mergeability is computed LAZILY: `null` on the first read, `false` (conflicted) after.
                with LOCK:
                    _LAZY_READS[0] += 1
                    n = _LAZY_READS[0]
                obj = dict(LAZY_PR)
                obj["mergeable"] = None if n <= 1 else False
                return self._send(200, obj)
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

#!/usr/bin/env python3
"""Case 30 (pr-existence-697) — the `adopt` command: take over an ORPHAN and land it.

`reap` refuses a stale claim whose PR is open (#581) and then offers exactly one exit, "close it, then
reap" — which for a green, mergeable, reviewed PR DESTROYS a worker's finished work. `adopt` lets a worker
land another worker's orphaned PR through ONE verified command that cannot be talked into landing anything
else. The GATE is what makes it safe: it lands FINISHED work (green + mergeable) and nothing else.

The world (case 30's #697 seeds, one transport over). FS.GG.SDD, six off-board claims:

    970  stale ghost-970, PR #701 GREEN + MERGEABLE        -> adopt LANDS it (transfers the lock to heron-697)
    971  stale ghost-971, PR #702 mergeable=false          -> CONFLICTED  -> refuse, lock survives
    972  stale ghost-972, PR #703 mergeable, ZERO checks   -> NOT green (#606) -> refuse, lock survives
    973  FRESH busy-973 (a LIVE claim)                     -> not an orphan -> refuse, lock survives
    974  stale ghost-974, NO open PR                       -> nothing to land -> refuse, lock survives
    975  stale ghost-975, PR #704 mergeable=null then false-> lazy re-read sees CONFLICTED -> refuse, survives
    976  stale ghost-976, PR #705 mergeable, checks RUNNING-> pending -> refuse, lock survives

The transfer reuses `claim`: `adopt 'FS.GG.SDD#970'` POSTs heron-697's marker under the CAS, so after it
the live winner is heron-697 (ghost-970's stale marker is left for reap). #975's PR #704 returns
`mergeable: null` on the FIRST read and `false` on later reads (GitHub computes mergeability lazily) — the
engine re-reads and sees the conflict.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
RATE = {"cost": 1, "remaining": 4976}

ISSUES = {
    970: {"title": "Finished, green, and orphaned", "body": "Paths: src/Orphan970"},
    971: {"title": "Orphaned but conflicted", "body": "Paths: src/Orphan971"},
    972: {"title": "Orphaned, mergeable, never tested", "body": "Paths: src/Orphan972"},
    973: {"title": "Alive and well", "body": "Paths: src/Orphan973"},
    974: {"title": "Dead, nothing to show for it", "body": "Paths: src/Orphan974"},
    975: {"title": "Mergeability not computed yet", "body": "Paths: src/Orphan975"},
    976: {"title": "Orphaned mid-CI", "body": "Paths: src/Orphan976"},
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
}

# The open PRs, by number. 703/704 return their mergeable per the lazy/zero-checks worlds.
PULLS = {
    701: {"number": 701, "state": "open", "mergeable": True,
          "head": {"ref": "item/970-finished", "sha": "green970"}},
    702: {"number": 702, "state": "open", "mergeable": False,
          "head": {"ref": "item/971-conflicted", "sha": "c0nflict"}},
    703: {"number": 703, "state": "open", "mergeable": True,
          "head": {"ref": "item/972-no-checks", "sha": "n0checks"}},
    705: {"number": 705, "state": "open", "mergeable": True,
          "head": {"ref": "item/976-running", "sha": "pend976"}},
}
# 974 has no PR; 973 is a live claim (no PR needed). 704 (item/975-lazy) is served specially below.
LAZY_PR = {"number": 704, "state": "open", "head": {"ref": "item/975-lazy", "sha": "lazysha"}}

RUNS = {
    "green970": [{"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/970-finished",
                  "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 1,
                  "pull_requests": [{"number": 701}]}],
    "n0checks": [],
    "pend976": [{"path": ".github/workflows/build.yml", "event": "pull_request", "head_branch": "item/976-running",
                 "run_number": 1, "status": "completed", "conclusion": "success", "check_suite_id": 2,
                 "pull_requests": [{"number": 705}]},
                {"path": ".github/workflows/test.yml", "event": "pull_request", "head_branch": "item/976-running",
                 "run_number": 1, "status": "in_progress", "conclusion": None, "check_suite_id": 3,
                 "pull_requests": [{"number": 705}]}],
}
CHECKS = {
    "green970": [{"name": "build", "status": "completed", "conclusion": "success",
                  "app": {"slug": "github-actions"}, "check_suite": {"id": 1}}],
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


def comments(n):
    out = []
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
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

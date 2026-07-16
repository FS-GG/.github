#!/usr/bin/env python3
"""Case 24's `reap` MUTATING interleavings (legs h + m) — the reaper does not cause the double-hold it
exists to CLEAN UP, and a failed delete is reported, not swallowed. Served over HTTP for the compiled engine.

`Reapable` is a SNAPSHOT verdict: it is proven against the scan's read, and a holder may heartbeat between
the scan and the delete. So `reap` RE-VERIFIES the marker's freshness immediately before breaking the lock,
and DELETES before it would ever notify. This fixture is the two corpus legs that exercise those two
guarantees, in ONE FS.GG.SDD world, driven by ONE `reap --repo FS.GG.SDD --apply`:

    (h) #91  finch-a3f  marker 816  — STALE on the SCAN read, FRESH on the RE-VERIFY read (the holder
             heartbeated between the two). `reap` must SKIP it: "renewed since the scan", marker SURVIVES.
             This is `GH_REAP_RACE=91` re-expressed at the HTTP layer — the marker's `updated_at` flips
             from 3h-ago to now on the SECOND read of #91's /comments (the re-verify), so the same
             marker id reads stale, then live.
    (m) #96  ghost-555  marker 819  — STALE, and its DELETE FAILS (500). `reap` must REPORT the failure
             ("FAILED"), LEAVE the marker (the item is still held), and — because reap deletes BEFORE it
             would notify (and this engine's reap posts no notify at all) — NOT tell the worker it was
             released. This is `GH_FAIL_DELETE=819`: the DELETE of comment 819 answers 500, so the marker
             is never marked gone and a subsequent read serves it back.

Both items are OFF the board (empty `items`/`projectItems`) and neither has an open `item/<n>-*` PR, so
each passes the #581 proof-of-life gate (`LeaseExpiredNoPr`) and reaches the re-verify — the point of the
legs. Neither leg reaches the post-reap column restore (leg h skips, leg m fails), so the board is minimal.

Writes MUTATE this process — the harness reads `/_deletes` back to prove leg h deleted NOTHING (the skip)
and leg m's DELETE was ATTEMPTED but the marker still stands (the 500). `/comments/<n>` reflects reality:
#91's marker survives (never deleted, only re-timestamped); #96's marker survives (its DELETE 500'd).
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
RATE = {"cost": 1, "remaining": 4977}

LOCK = threading.Lock()
_DELETES = []          # comment ids this process was asked to DELETE.
_DELETED = set()       # comment ids actually gone now (leg m's 819 is NEVER added — its DELETE 500'd).
_FAIL_DELETE = {819}   # comment ids whose DELETE answers 500 (GH_FAIL_DELETE, leg m) — marker STANDS.
_COMMENT_READS = {}    # per-issue /comments read count, so #91 can flip stale→fresh on the RE-VERIFY.

# The two stale off-board claims. Both last beat 3h ago (lease 120m → lapsed) on the SCAN read.
CLAIMS = {
    91: {"marker_id": 816, "worker": "finch-a3f", "lease": 120, "body": "Paths: src/H/**"},
    96: {"marker_id": 819, "worker": "ghost-555", "lease": 120, "body": "Paths: src/M/**"},
}


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _marker(n, ts):
    c = CLAIMS[n]
    return {"id": c["marker_id"],
            "body": f"<!-- fsgg:claim worker={c['worker']} lease={c['lease']} -->\nheld",
            "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}


def comments(n):
    if n not in CLAIMS:
        return []
    if CLAIMS[n]["marker_id"] in _DELETED:
        return []
    with LOCK:
        seen = _COMMENT_READS.get(n, 0)
        _COMMENT_READS[n] = seen + 1
    # (h) #91: STALE on the SCAN (read 1), FRESH on the RE-VERIFY (read 2) and after — the holder
    # heartbeated between the scan and the delete, so the same marker reads stale, then live.
    if n == 91:
        return [_marker(91, _iso(3) if seen == 0 else _iso(0))]
    # (m) #96: always STALE — the reap reaches the delete, which then 500s.
    return [_marker(n, _iso(3))]


def open_issues():
    return [{"number": n, "title": CLAIMS[n]["body"].split(":")[0], "state": "open",
             "body": CLAIMS[n]["body"]} for n in sorted(CLAIMS)]


def graphql(query):
    # Board bootstrap — used only if a successful reap restored a column (neither leg does), so the world
    # is off-board: empty items, empty projectItems.
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
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}}, "rateLimit": RATE}}
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
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_DELETE(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            cid = int(m.group(1))
            with LOCK:
                _DELETES.append(cid)
                fail = cid in _FAIL_DELETE
                if not fail:
                    _DELETED.add(cid)
            if fail:
                return self._send(500, {"message": "delete failed (leg m)"})  # marker STANDS
            self.send_response(204)
            self.end_headers()
            return
        self._send(500, {"message": f"unhandled DELETE {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p == "/_deletes":
            with LOCK:
                return self._send(200, list(_DELETES))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, open_issues())
        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, [])  # no open item/<n>-* PR → LeaseExpiredNoPr → reaches the re-verify
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in CLAIMS:
                return self._send(200, {"number": n, "body": CLAIMS[n]["body"]})
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

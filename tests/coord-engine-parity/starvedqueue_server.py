#!/usr/bin/env python3
"""Case 25's STARVED QUEUE — a busy repo where `batch` hands out NOTHING, and must say so (#428).

The chokepoint this is filed against: in a repo where one file is the touch-set of nearly every item, ONE
claim serialises the whole queue. `batch` correctly schedules nothing — and "nothing schedulable" reads
exactly like an empty backlog, so a worker goes home from a repo with four items in it. The per-item
reasons say WHY each item is out; only a LEASE says whether it is worth waiting, and only a BANNER says the
queue is BUSY rather than empty.

The world (lifted from the corpus's case-25 starved section, one transport over). FS.GG.Rendering:

    221  Ready,       Paths: src/Starve/Sub   -> overlaps the OFF-BOARD live claim #223 (tern-y99, fresh)
    222  Ready,       Paths: src/Solo         -> HELD by its own fresh marker (kite-z01) — a failed flip
    224  Ready,       Paths: src/Dead/Sub     -> overlaps the OFF-BOARD claim #216 whose lease EXPIRED
    225  Ready,       Paths: src/Ghostly/Sub  -> overlaps the MARKERLESS In-progress #226 (no worker/lease)
    226  In progress, Paths: src/Ghostly      -> reserves its files (arm A), but there is nobody to name
    216  OFF board,   Paths: src/Dead         -> ghost-222's STALE marker (a lock only `reap` may break)
    223  OFF board,   Paths: src/Starve       -> tern-y99's FRESH marker (a live claim off the board)

Three items are QUEUED BEHIND LIVE CLAIMS (#221/tern, #222/kite, #224/ghost) — one of those leases has
EXPIRED (ghost-222 on #216), which is a `reap`, not a wait. #225 overlaps a MARKERLESS reserver: it is
reserved (never scheduled over) but it is NOT a holder — no worker, no lease.

The corpus (case 25) certifies:
    batch --repo rendering --json  -> []   (the queue is starved; the locks still hold)
    the banner (stderr):
        "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by: ghost-222, kite-z01, tern-y99"
        "soonest: lease EXPIRED — reapable"
        "this queue is BUSY, not empty"
        "1 of those lease(s) have EXPIRED — collect them: fsgg-coord reap --repo FS.GG.Rendering --apply"
    the per-item reasons (stderr):
        #224 -> overlaps ... held by ghost-222 on FS.GG.Rendering#216 (lease EXPIRED — reapable)
        #225 -> overlaps FS.GG.Rendering#226, which the board says is In progress with NO claim marker
                ... there is no lease to wait out; see: fsgg-coord who
    and NO holder is ever named "—" (the markerless reserver is not dressed up as one).
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.Rendering"
RATE = {"cost": 1, "remaining": 4975}

# Every open issue in the repo, with the body the off-board scan carries for free.
ISSUES = {
    216: {"title": "Overlaps a DEAD holder's claim target", "body": "Paths: src/Dead"},
    221: {"title": "Overlaps a live claim", "body": "Paths: src/Starve/Sub"},
    222: {"title": "Claimed outright", "body": "Paths: src/Solo"},
    223: {"title": "Holds src/Starve", "body": "Paths: src/Starve"},
    224: {"title": "Overlaps a DEAD holder's claim", "body": "Paths: src/Dead/Sub"},
    225: {"title": "Overlaps a MARKERLESS In progress item", "body": "Paths: src/Ghostly/Sub"},
    226: {"title": "In progress, outside the protocol", "body": "Paths: src/Ghostly"},
}

# Only 221–226 minus 223 are on the board. 216 and 223 are claims the board never listed (off-board).
BOARD = {221: "Ready", 222: "Ready", 224: "Ready", 225: "Ready", 226: "In progress"}

# The markers. (worker, hours_since_beat) — a 120m lease. ghost-222 aged 3h -> EXPIRED; the rest fresh.
MARKERS = {
    216: ("ghost-222", 3),   # STALE (lease expired) — a lock only `reap` may break, still reserves
    222: ("kite-z01", 0),    # fresh -> HELD (the board says Ready; the lock disagrees)
    223: ("tern-y99", 0),    # fresh -> a live off-board claim
}
_MARKER_ID = {216: 816, 222: 822, 223: 823}

LOCK = threading.Lock()
_REQUESTS = []  # issue-list requests: {"page": <str|None>, "inm": <bool>}


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    if n not in MARKERS:
        return []
    worker, hrs = MARKERS[n]
    ts = _iso(hrs)
    return [{"id": _MARKER_ID[n],
             "body": f"<!-- fsgg:claim worker={worker} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]


def issue_list():
    return [{"number": n, "title": ISSUES[n]["title"], "state": "open", "body": ISSUES[n]["body"]}
            for n in sorted(ISSUES)]


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
    if "items(first" in query:
        return board_items()
    return None


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, payload, extra_headers=None):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        for k, v in (extra_headers or {}).items():
            self.send_header(k, v)
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

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        qs = self.path.split("?", 1)[1] if "?" in self.path else ""
        page = None
        m = re.search(r"[?&]page=(\d+)", "?" + qs)
        if m:
            page = m.group(1)

        if p == "/_requests":
            with LOCK:
                return self._send(200, list(_REQUESTS))

        # The off-board scan (arm B) — one page of every open issue, recorded so the harness can prove it is
        # unconditional (a 304 could serve a comments:0 captured before a marker was posted).
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            with LOCK:
                _REQUESTS.append({"page": page, "inm": self.headers.get("If-None-Match") is not None})
            return self._send(200, issue_list())

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

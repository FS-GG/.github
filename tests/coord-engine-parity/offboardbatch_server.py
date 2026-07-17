#!/usr/bin/env python3
"""Case 25's BATCH over an OFF-BOARD claim — the scheduler reserves a lock the board never listed.

`batch`/`next`/`take` schedule disjoint touch-sets, and disjointness is only sound if the reserved set is
COMPLETE. A claim marker sits on the ISSUE, not the board column: an item may be Ready on the board (a
column flip that failed) yet HELD by a live marker, or it may never have reached the board at all. The
board scan is blind to the second kind — so a candidate declaring the same files would be handed a tree
another worker is standing in. bash's `active_claims` scans the repo's OPEN ISSUES (arm B) for exactly
this; the engine must too, or `batch` hands out a double-book.

The world (lifted from the corpus's `seed_offboard_world`, one transport over). FS.GG.Rendering:

    210  In progress on the board, NO marker            -> not schedulable (wrong column), reserves nothing
    211  Ready on the board (a column flip FAILED), fresh marker wren-c22  -> HELD (the lock, not the column)
    212  Ready on the board, NO marker, `Paths: src/Free212`  -> the one genuinely free item
    213  Ready on the board, NO marker, `Paths: src/Off/Sub`  -> OVERLAPS the off-board claim below
    215  OFF the board, fresh marker puffin-h11, `Paths: src/Off`   -> a reservation a board scan would miss
    217  OFF the board, a chatty issue with NO marker     -> reserves nothing (a comment is not a claim)

The corpus (case 25) certifies:
    batch --repo rendering --json  -> ["FS.GG.Rendering#212"]  (only the item no live marker touches)
    the skips (stderr):
        #211 -> already claimed by worker wren-c22 (lease frees in ~…)     (a board item held by its lock)
        #213 -> overlaps in-flight work held by puffin-h11 on FS.GG.Rendering#215 (lease frees in ~…):
                src/Off/Sub  ⇄  src/Off                                    (the OFF-BOARD reservation)
    the scan -> PAGINATES (a lock has no 100-issue limit) and is NEVER conditional (a 304 could serve a
                `comments: 0` captured before a marker was posted, hiding a live lock). Re-expressed at the
                HTTP layer: the issue-list is served in TWO pages via `Link: rel=next`, and `/_requests`
                proves page 2 was fetched and no issue-list request carried If-None-Match.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.Rendering"
RATE = {"cost": 1, "remaining": 4976}

# Every open issue in the repo, with its body (the off-board scan carries bodies for free). #213 declares
# a SUBTREE of #215's reservation, so the two collide `src/Off/Sub  ⇄  src/Off`.
ISSUES = {
    210: {"title": "In progress, no marker", "body": "Paths: src/Orphan2"},
    211: {"title": "Board flip failed, still held", "body": "Paths: src/Flip"},
    212: {"title": "Genuinely free", "body": "Paths: src/Free212"},
    213: {"title": "Overlaps the off-board claim", "body": "Paths: src/Off/Sub"},
    215: {"title": "Off-board, held", "body": "Paths: src/Off"},
    217: {"title": "Chatty, no marker", "body": "Paths: src/Chatty"},
}

# Only 210–213 are on the board. 215 and 217 are claims/chatter the board never knew about.
BOARD = {210: "In progress", 211: "Ready", 212: "Ready", 213: "Ready"}

# The markers. (worker, hours_since_beat) — a 120m lease, so both here are fresh -> live. None = no marker.
MARKERS = {
    211: ("wren-c22", 0),      # fresh -> live -> HELD (though the board says Ready)
    215: ("puffin-h11", 0),    # fresh -> live -> HELD (off-board — the reservation a board scan misses)
}
_MARKER_ID = {211: 811, 215: 815}

LOCK = threading.Lock()
_REQUESTS = []  # issue-list requests: {"page": <str|None>, "inm": <bool>} — proves paginate + inm=none.


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


def issue_list_page(page):
    # TWO pages, so pagination is exercised (a lock has no 100-issue limit). Page 1 carries a
    # `Link: rel=next`; page 2 does not. The off-board claim (#215) rides on page 2, so a scan that
    # stopped at page 1 would miss it and hand #213 the files puffin-h11 is standing in.
    nums = sorted(ISSUES)
    first, second = nums[:3], nums[3:]
    chosen = second if page == "2" else first
    return [{"number": n, "title": ISSUES[n]["title"], "state": "open", "body": ISSUES[n]["body"]}
            for n in chosen]


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
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response
    # races the engine's pooling HttpClient and RSTs away a written response (#761). Pairs with
    # ThreadingHTTPServer below — a kept-alive connection would pin a single-threaded server.
    protocol_version = "HTTP/1.1"

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

        # THE OFF-BOARD SCAN — paginated, and it must never be conditional. Record the request (page +
        # whether it carried If-None-Match) so the harness can prove BOTH properties at the HTTP layer.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            with LOCK:
                _REQUESTS.append({"page": page, "inm": self.headers.get("If-None-Match") is not None})
            headers = {}
            if page != "2":
                host = self.headers.get("Host", "127.0.0.1")
                nxt = f"http://{host}/repos/{OWNER}/{REPO}/issues?state=open&per_page=100&page=2"
                headers["Link"] = f"<{nxt}>; rel=\"next\""
            return self._send(200, issue_list_page(page), extra_headers=headers)

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

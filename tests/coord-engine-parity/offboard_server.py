#!/usr/bin/env python3
"""Cases 25 + 26's OFF-BOARD `who` — the claim that lives off the board, and its proof of life (#581).

`who` is the truth read of the LOCK, and the lock is NOT the board column. A claim marker sits on the
ISSUE, and the issue's board Status may be Ready (a column flip that failed), or the item may never have
reached the board at all. So `who` cannot answer off the board's In-progress column alone: it scans the
repo's OPEN ISSUES (case 25's off-board scan), and reads the marker on each — wherever the board thinks
the item is.

The world (lifted from the corpus's `seed_offboard_world` + case 26's proof-of-life seed, one transport
over). FS.GG.Rendering:

    210  In progress on the board, NO marker            -> UNCLAIMED (only the column puts it in flight)
    211  Ready on the board (a column flip FAILED), fresh marker wren-c22  -> HELD (who reads the LOCK)
    215  OFF the board, fresh marker puffin-h11          -> HELD   · body `Paths: src/Off`
    216  OFF the board, STALE marker ghost-222 (3h)      -> STALE, and its work is DEAD (no item PR)  -> bare STALE
    217  OFF the board, a chatty issue with NO marker    -> NOT in flight (dropped)
    890  OFF the board, STALE marker shrike-a91 (3h)     -> STALE, but PR #433 is OPEN on item/890-*  -> STALE (#433 OPEN)

The corpus (cases 25 + 26) certifies:
    who --json  -> exactly [210,211,215,216,890], never the chatty #217; #211 HELD though the board says
                   Ready; #215's off-board claim HELD with its touch-set read from the body; #216 a bare
                   STALE (its work is dead); #890 carries `livePr: "#433 item/890-live-work"` (#581).
    who (text)  -> #890's row reads `STALE (#433 OPEN)`; #216's is a bare `STALE` a reaper may collect.
    the scan    -> PAGINATES (a lock has no 100-issue limit) and is NEVER conditional (a 304 could serve a
                   `comments: 0` captured before a marker was posted, hiding a live lock). Re-expressed at
                   the HTTP layer: the issue-list is served in TWO pages via a `Link: rel=next`, and
                   `/_requests` proves page 2 was fetched and no issue-list request carried If-None-Match.
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.Rendering"
RATE = {"cost": 1, "remaining": 4976}

# Every open issue in the repo, with its body (the off-board scan carries bodies for free). #890 rides
# alongside the case-25 world so ONE run proves both the bare STALE (#216) and the proof of life (#890).
ISSUES = {
    210: {"title": "In progress, no marker", "body": "Paths: src/Orphan2"},
    211: {"title": "Board flip failed, still held", "body": "Paths: src/Flip"},
    215: {"title": "Off-board, held", "body": "Paths: src/Off"},
    216: {"title": "Off-board, dead lease", "body": "Paths: src/Dead"},
    217: {"title": "Chatty, no marker", "body": "Paths: src/Chatty"},
    890: {"title": "Long build, lapsed lease", "body": "Paths: src/Long890"},
}

# Only 210 and 211 are on the board — 210 In progress, 211 Ready (its column flip failed, yet a live
# marker holds it). Everything else is a claim the board never knew about.
BOARD = {210: "In progress", 211: "Ready"}

# The markers. (worker, hours_since_beat) — a 120m lease, so 3h ago is well past it. None = no marker.
MARKERS = {
    211: ("wren-c22", 0),      # fresh -> live -> held (though the board says Ready)
    215: ("puffin-h11", 0),    # fresh -> live -> held (off-board)
    216: ("ghost-222", 3),     # 3h -> stale; no item PR -> dead -> a bare STALE
    890: ("shrike-a91", 3),    # 3h -> stale; PR #433 open on item/890-* -> proof of life
}
_MARKER_ID = {211: 811, 215: 815, 216: 816, 890: 890}

# Proof of life: one OPEN PR, on item/890-*. #216 has none, so its stale claim is genuinely dead.
OPEN_PULLS = [{"number": 433, "state": "open", "head": {"ref": "item/890-live-work"}}]

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
    # `Link: rel=next`; page 2 does not. The split is arbitrary — what matters is that BOTH are fetched.
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

        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, OPEN_PULLS)

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", p)
        if m:
            pr = int(m.group(1))
            hit = next((x for x in OPEN_PULLS if x["number"] == pr), None)
            if hit:
                return self._send(200, hit)
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

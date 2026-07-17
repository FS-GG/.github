#!/usr/bin/env python3
"""Cases 22 + 25's `say` / `inbox` — the worker mailbox, and the message that rides an OFF-BOARD claim.

`inbox` delivers the messages `say` posts, addressed to a worker (or broadcast, `to=*`), across every
in-flight claim. The corpus certifies two things this one fixture proves as a pure engine round-trip
(the engine `say`s a message over HTTP, then the engine `inbox`es it back):

  case 22 — the mailbox contract: a message addressed to me is delivered and names the item it rode in on;
            a broadcast (`to=*`) is delivered; the cursor advances, so a second read shows nothing new; a
            worker does NOT see its own messages; `--peek` shows new mail without advancing the cursor.
  case 25 — off-board delivery: a claim — and the message riding it — can sit on an issue the board never
            listed, so `inbox` must run the SAME off-board open-issue scan `who`/`reap`/`batch` run, or it
            drops a message posted on an off-board claim entirely.

The world (one transport over the corpus's `seed_offboard_world`). FS.GG.Rendering:

    210  In progress on the board, NO marker            -> in the candidate set via arm A (the board column)
    215  OFF the board, fresh marker puffin-h11          -> in the candidate set via arm B (the open-issue
                                                            scan) ONLY — the board never listed it, so a
                                                            mailbox that read the board alone would miss it

Messages are posted on #215 (OFF the board) by `say`. The fixture stores each POSTed comment and serves
it back on the next comments read, so `inbox` sees exactly what `say` wrote — no message is seeded; the
engine writes them itself.
"""

import json
import re
import sys
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.Rendering"
RATE = {"cost": 1, "remaining": 4976}

ISSUES = {
    210: {"title": "In progress, no marker", "body": "Paths: src/Orphan2"},
    215: {"title": "Off-board, held", "body": "Paths: src/Off"},
}

# Only 210 is on the board (In progress). #215 is a claim the board never knew about — the off-board scan
# is the ONLY way `inbox` finds it, which is the whole of case 25's contribution here.
BOARD = {210: "In progress"}

# #215's live claim marker. #210 is markerless (only the board column puts it in flight).
MARKERS = {215: ("puffin-h11", 0)}
_MARKER_ID = {215: 815}

LOCK = threading.Lock()
# The comments `say` POSTs, keyed by issue number. Seeded empty — the engine writes every message itself,
# so this is a genuine write-then-read round-trip, not a replay of a canned body.
_POSTED = {}
_NEXT_ID = [9000]


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    out = []
    if n in MARKERS:
        worker, _ = MARKERS[n]
        ts = _now()
        out.append({"id": _MARKER_ID[n],
                    "body": f"<!-- fsgg:claim worker={worker} lease=120 -->\nheld",
                    "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts})
    out.extend(_POSTED.get(n, []))
    return out


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

        # `say` POSTs its message here. Store it under the issue and hand back the id — the CAS/`say` reads
        # `.id` off the response, and `inbox` reads the stored body back on the next comments GET.
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
                ts = _now()
                _POSTED.setdefault(n, []).append(
                    {"id": cid, "body": body, "user": {"login": "EHotwagner"},
                     "created_at": ts, "updated_at": ts})
            return self._send(201, {"id": cid})

        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]

        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, issue_list())

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            with LOCK:
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

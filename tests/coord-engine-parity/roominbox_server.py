#!/usr/bin/env python3
"""ADR-0051 inbox widening (.github#1215) — a room message reaches a worker BECAUSE it is referenced.

ADR-0051's one real code delta: `inbox`'s subject set widens from the in-flight items alone to those
items PLUS the coordination rooms they reference. This fixture proves the widening is load-bearing —
that it delivers a message NO existing arm would have found.

The trick is isolation. `inbox` already scans, per in-scope repo, every OPEN issue (arm B) and every
board In-progress row (arm A), so a room in a SCANNED repo would be delivered with or without the
widening — a test over one would prove nothing. So the room lives in a repo the scan never visits:

    FS-GG/FS.GG.Rendering#215   off-board, open, in scope. Its body carries
                                `Rooms: FS-GG/FS.GG.SDD#216` — the reference that pulls the room in.
    FS-GG/FS.GG.SDD#216         the ROOM. FS.GG.SDD is NOT on the board and NOT `--repo`, so arm B
                                never lists it. The ONLY path to it is `#215`'s `Rooms:` line.

A message on the room (#216), addressed to the worker running `inbox`, is seeded. Without the widening
the room is outside every candidate arm and the message is dropped; with it, `#215`'s reference brings
#216 into the subject set and the message is delivered. The board is scoped to Rendering, so arm B only
ever lists Rendering's issues (#210/#215) — #216 is unreachable except through the reference.
"""

import json
import re
import sys
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
BOARD_REPO = "FS.GG.Rendering"
ROOM_REPO = "FS.GG.SDD"
ROOM_NUMBER = 216
WORKER = "wren-inbox"
RATE = {"cost": 1, "remaining": 4976}

# #215 references the room; the room lives in a DIFFERENT repo that arm B never scans.
RENDERING_ISSUES = {
    210: {"body": "Paths: src/Orphan2"},
    215: {"body": f"Paths: src/Off\n\nRooms: {OWNER}/{ROOM_REPO}#{ROOM_NUMBER}"},
}
BOARD = {210: "In progress"}  # only #210 is on the board; #215 is off-board (arm B)

LOCK = threading.Lock()


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


# The seeded room message: from a peer, TO the inbox worker. Delivered only if #216 is a subject.
def room_comments():
    ts = _now()
    return [{"id": 7100,
             "body": f"<!-- fsgg:msg from=petrel-x to={WORKER} -->\n**petrel-x → {WORKER}**\n\nlet's split the Scene edge — you take the .fsi",
             "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]


def rendering_issue_list():
    return [{"number": n, "title": f"Rendering #{n}", "state": "open", "body": RENDERING_ISSUES[n]["body"]}
            for n in sorted(RENDERING_ISSUES)]


def board_items():
    nodes = []
    for n, status in BOARD.items():
        nodes.append({
            "status": {"name": status},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": f"Rendering #{n}", "state": "OPEN",
                        "repository": {"nameWithOwner": f"{OWNER}/{BOARD_REPO}"}},
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

    def do_GET(self):
        p = self.path.split("?", 1)[0]

        # Open-issue list (arm B). Only Rendering is scanned; its list NEVER contains #216, so the room is
        # reachable exclusively through #215's `Rooms:` line.
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/?$", p)
        if m:
            if m.group(1) == BOARD_REPO:
                return self._send(200, rendering_issue_list())
            return self._send(200, [])

        # Comments. The room (#216) carries the seeded message; the Rendering items carry none.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, room_comments() if n == ROOM_NUMBER else [])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in RENDERING_ISSUES:
                return self._send(200, {"number": n, "body": RENDERING_ISSUES[n]["body"]})
            return self._send(200, {"number": n, "body": ""})

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

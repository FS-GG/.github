#!/usr/bin/env python3
"""ADR-0051 `room open` (.github#1215), served over HTTP for the compiled engine.

`room open --over N,M` opens a coordination room over a contended cluster: it CREATES the room issue
(off the board — coordination scaffolding, not deliverable work) and writes a `Rooms: #room` back-
reference onto each named item, so their holders share the room's channel via `say`/`inbox`. This
fixture is that world one transport over, and asserts the two writes at the HTTP layer:

    create      POST /repos/{o}/{r}/issues once — the room issue, body carrying `Paths: none`. The
                POST body is recorded and served on /_posts.
    back-ref    PATCH /repos/{o}/{r}/issues/{n} once per member, body gaining a `Rooms: #220` line.
                Each PATCH (number + body) is recorded and served on /_patches.
    idempotent  a member ALREADY referencing the room is NOT re-PATCHed — `room open` reads each body
                first and skips one it need not touch.
    surface     a failed create reports the API's own error, never a guessed cause.

The room is created as #220 (a fresh number the create returns). Members #302/#303 live in
FS-GG/FS.GG.SDD and start with only a `Paths:` line.

Env toggles (each parity leg spawns a FRESH server, since the writes mutate the bodies):
    FSGG_PARITY_ROOM_PREEXISTING=302[,...]   these members already carry `Rooms: #220` (idempotency)
    FSGG_PARITY_CREATE_FAIL=1                the room-create POST 422s (surface-the-API's-error leg)
"""

import json
import os
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
ROOM_NUMBER = 220

PREEXISTING = [int(x) for x in os.environ.get("FSGG_PARITY_ROOM_PREEXISTING", "").split(",") if x.strip()]
CREATE_FAIL = os.environ.get("FSGG_PARITY_CREATE_FAIL", "") != ""

LOCK = threading.Lock()
_POSTS = []    # every issue-create POST body received — read back on /_posts.
_PATCHES = []  # every issue-body PATCH ({number, body}) received — read back on /_patches.


def member_body(n):
    # A member already in the room carries the back-reference the idempotency leg asserts is NOT re-written.
    base = f"Paths: src/Item{n}"
    if n in PREEXISTING:
        return base + f"\n\nRooms: #{ROOM_NUMBER}"
    return base


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response and RST away a written response the
    # engine's pooling HttpClient is still reading (#761). Pairs with ThreadingHTTPServer below.
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
        if p == "/_posts":
            with LOCK:
                return self._send(200, list(_POSTS))
        if p == "/_patches":
            with LOCK:
                return self._send(200, list(_PATCHES))
        # A member's body read (`Reads.issueBody`), so `room open` can skip a member already in the room.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": member_body(n)})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4960, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})

    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        p = self.path.split("?", 1)[0]
        # The room-create POST.
        if re.match(r"^/repos/[^/]+/[^/]+/issues$", p):
            try:
                body = json.loads(raw)
            except json.JSONDecodeError:
                body = raw
            with LOCK:
                _POSTS.append(body)
            if CREATE_FAIL:
                return self._send(422, {"message": "Unprocessable Entity: issue could not be created"})
            return self._send(201, {"number": ROOM_NUMBER})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_PATCH(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        p = self.path.split("?", 1)[0]
        # A member's body PATCH — the `Rooms:` back-reference write.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            try:
                body = json.loads(raw)
            except json.JSONDecodeError:
                body = {}
            with LOCK:
                _PATCHES.append({"number": n, "body": body.get("body", "")})
            return self._send(200, {"number": n})
        self._send(500, {"message": f"unhandled PATCH {p}"})


def main():
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

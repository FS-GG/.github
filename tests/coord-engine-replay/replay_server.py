#!/usr/bin/env python3
"""Serves one recorded `record-board-fixture.py` transcript back to the real compiled
`fsgg-coord-engine`, matching each request by the same (method, path, canonical body) shape the
capture tool keyed it on (`fixture_lib.request_key`).

Unlike `tests/coord-engine-e2e/fixture_server.py`/`stateful_server.py` and the 47 hand-authored servers
in `tests/coord-engine-parity/`, this server invents NOTHING: every response it serves is byte-for-byte
what capture recorded (modulo the rate-limit/header normalization `fixture_lib` applies on both ends),
and an UNMATCHED request is a hard failure — logged to stderr with the `REPLAY-UNMATCHED-REQUEST`
marker `run.sh` greps for, and answered with a 500 the engine surfaces in its own error text — rather
than a guess. A silently-invented answer here would defeat the whole point of `.github#2401`: this
fixture exists to test what GitHub actually said on a real scan, not what this file's author expected
it to be asked.
"""

import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import fixture_lib as lib  # noqa: E402

UNMATCHED_MARKER = "REPLAY-UNMATCHED-REQUEST"

LOCK = threading.Lock()
INDEX = {}
MISSES = []


def load_index(transcript_path):
    with open(transcript_path) as f:
        doc = json.load(f)
    if doc.get("schema") != lib.SCHEMA:
        print(
            f"replay_server: {transcript_path} is not a {lib.SCHEMA} transcript "
            f"(got {doc.get('schema')!r})",
            file=sys.stderr,
        )
        sys.exit(1)
    index = {}
    for entry in doc["entries"]:
        body_text = json.dumps(entry["requestBody"]) if entry.get("requestBody") is not None else ""
        key = lib.request_key(entry["method"], entry["path"], body_text)
        index[key] = entry
    return index


# HTTP/1.1, thread-per-connection — the same reasoning `stateful_server.py` documents at its Handler:
# the compiled engine drives this through .NET's pooling `HttpClient`, which needs a kept-alive
# connection to be legal, and a single-threaded accept loop would deadlock the moment the engine opens
# a second connection.
class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _body(self):
        n = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(n).decode() if n else ""

    def _send(self, status, payload_bytes, extra_headers=None):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        for k, v in (extra_headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Length", str(len(payload_bytes)))
        self.end_headers()
        if payload_bytes:
            self.wfile.write(payload_bytes)

    def _serve(self, method):
        path = self.path.split("?", 1)[0]

        # The harness's own instrumentation — never a real GitHub route, hence the `_fixture/` prefix
        # no real endpoint can collide with. Lets `run.sh` assert "nothing went unmatched" explicitly
        # instead of relying solely on the engine's own error text.
        if path.rstrip("/") == "/_fixture/misses":
            with LOCK:
                return self._send(200, json.dumps({"misses": list(MISSES)}).encode())

        raw = self._body()
        key = lib.request_key(method, path, raw)
        with LOCK:
            entry = INDEX.get(key)
        if entry is None:
            msg = (
                f"{UNMATCHED_MARKER}: {method} {path} — no recorded entry matches this request "
                f"shape. body={raw[:300]!r}"
            )
            print(f"replay_server: {msg}", file=sys.stderr)
            with LOCK:
                MISSES.append(f"{method} {path}")
            # GraphQL errors and REST errors have different envelopes; answer in whichever this path
            # expects, so the engine's own parser surfaces `msg` rather than choking on the wrong shape.
            if path.rstrip("/") == "/graphql":
                body = json.dumps({"errors": [{"message": msg}]}).encode()
            else:
                body = json.dumps({"message": msg}).encode()
            return self._send(500, body)

        status = entry["status"]
        payload = entry["responseBody"]
        body = b"" if status in (204, 304) else json.dumps(payload).encode()
        headers = {}
        if path.rstrip("/") not in ("/graphql", "/rate_limit"):
            # Synthetic healthy REST budget headers — deliberately NOT what capture saw:
            # `fixture_lib.normalize_response_headers` drops the real ones as volatile (see its
            # docstring), and this is the fixed, healthy stand-in every other engine fixture serves.
            headers = {
                "X-RateLimit-Resource": "core",
                "X-RateLimit-Limit": "5000",
                "X-RateLimit-Remaining": "4800",
                "X-RateLimit-Used": "200",
                "X-RateLimit-Reset": "1893456000",
            }
        self._send(status, body, headers)

    def do_GET(self):
        self._serve("GET")

    def do_POST(self):
        self._serve("POST")

    def do_PATCH(self):
        self._serve("PATCH")

    def do_DELETE(self):
        self._serve("DELETE")


def main():
    global INDEX
    if len(sys.argv) != 2:
        print("usage: replay_server.py <transcript.json>", file=sys.stderr)
        sys.exit(2)
    INDEX = load_index(sys.argv[1])
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    print(server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

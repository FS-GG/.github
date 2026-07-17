#!/usr/bin/env python3
"""#523 — `widen` re-checks BEFORE it PATCHes, and an unreadable re-check REFUSES with the body untouched.

The #523 defect (bash, and the engine until this fix): `widen` PATCHed the declaration into the issue body
and re-checked overlap AFTERWARDS. On an exhausted GraphQL budget the #353 collision scan died AFTER the
body had already landed — so the widened touch-set was persisted UNVERIFIED and the workers it now collided
with were never told. The fix orders the scan before the write and lets its verdict gate the PATCH: an
unreadable scan (an exhausted budget) refuses, and the body is left exactly as it was.

This fixture stands up one item the widener HOLDS (#706), lets the REST legs succeed (verifyHeld reads the
claim marker; issueBody reads the body), and then makes the collision scan's GraphQL come back RATE-LIMITED.
The engine must:

    widen SDD#401 --paths 'src/new/**'  (budget exhausted on the scan)
        -> exit EX_RATE (75), naming the budget,
        -> and land ZERO writes: no PATCH to #401's body, no notify comment.

The PATCH and comment POSTs are counted at the HTTP layer, so a widen that wrote the body first and only
then discovered it could not verify it — the exact #523 bug — cannot pass: `/_patches` would be 1.

The verdict-in-hand path (a scan that SUCCEEDS, lands the widen, then notifies) is the positive control, and
it is already certified by case 34 (`widennotify_server.py`); this fixture is the negative leg #523 names.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# The scan's GraphQL is metered EXHAUSTED: every board query answers with the rate-limit signal the engine
# reads as RateLimited (Budget.isRateLimited), so activeCollisions fails before it can return a verdict.
RATE_LIMIT_ERROR = {"errors": [{"message": "API rate limit exceeded for installation"}]}

REPO = "FS-GG/FS.GG.SDD"
BODY_401 = "Paths: scripts/fsgg-coord"
HOLDER = "kite-t01"  # the worker that holds #401 and widens it (#706)

LOCK = threading.Lock()
_PATCHES = {}  # issue number -> count of body PATCHes (the widen write; must stay 0 on a refused widen).
_POSTS = {}    # issue number -> count of comment POSTs (the notify; must stay 0 when the scan never ran).


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments_401():
    return [{"id": 8401, "body": f"<!-- fsgg:claim worker={HOLDER} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "updated_at": _now(0)}]


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
            # Every board query is budget-exhausted — the collision scan cannot complete.
            return self._send(200, RATE_LIMIT_ERROR)
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            with LOCK:
                _POSTS[n] = _POSTS.get(n, 0) + 1
            return self._send(201, {"id": 9000 + n})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_PATCH(self):
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            with LOCK:
                _PATCHES[n] = _PATCHES.get(n, 0) + 1
            return self._send(200, {"number": n, "body": BODY_401})
        self._send(500, {"message": f"unhandled PATCH {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p == "/_patches":
            with LOCK:
                return self._send(200, dict(_PATCHES))
        if p == "/_posts":
            with LOCK:
                return self._send(200, dict(_POSTS))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, comments_401() if n == 401 else [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODY_401}) if n == 401 \
                else self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 0, "limit": 5000}}})
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

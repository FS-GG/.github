#!/usr/bin/env python3
"""Case 40's rate-limited board, served over HTTP for the compiled engine (#418).

The GraphQL budget is the first to die under fan-out — the whole reason this client exists (#418) — and
its exhaustion is a DISTINCT outcome from an empty queue, a lost race, or an unreadable board: it is a
BACK-OFF signal. The corpus certifies it (case 40): a `take` whose scan hits the budget exits `EX_RATE`
(75), the code `/pnext-item` teaches a worker to key on ("if take exits 75, back off until the reset").

Bootstrap (projectsV2) and the fields read succeed; the BOARD ITEMS read — the one that actually spends
the budget — returns HTTP 403 with GitHub's rate-limit body. The engine's transport does NOT retry a
rate limit (retrying spends more calls confirming the same 403 and delays the back-off), classifies it
as `RateLimited`, and `take` fails with exit 75 — NOT a protocol error, NOT a lost race, NOT an empty
queue. This serves that exact shape so the engine can be held to #418's certified exit code over HTTP.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 0}
RL_BODY = {"message": "API rate limit exceeded for installation",
           "documentation_url": "https://docs.github.com/rest#rate-limiting"}


def graphql(q):
    if "projectsV2" in q:
        return 200, {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return 200, {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}]}]}}}, "rateLimit": RATE}}
    if "items(first" in q:
        # The read that spends the budget: 403 rate-limit. This is what `take`'s scan hits.
        return 403, RL_BODY
    return 200, {"errors": [{"message": "unhandled"}]}


class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, payload, headers=None):
        b = json.dumps(payload).encode()
        self.send_response(code)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_POST(self):
        n = int(self.headers.get("Content-Length", 0))
        try:
            q = json.loads(self.rfile.read(n).decode()).get("query", "")
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        code, payload = graphql(q)
        hdr = {"x-ratelimit-remaining": "0", "retry-after": "60"} if code == 403 else None
        self._send(code, payload, hdr)

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 0, "limit": 5000}}})
        # Any REST read is also rate-limited on this board.
        self._send(403, RL_BODY, {"x-ratelimit-remaining": "0"})


def main():
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

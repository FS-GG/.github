#!/usr/bin/env python3
"""Case 43's #497 — a claim scan larger than MAX_ARG_STRLEN, served for the compiled engine.

bash's `active_claims` read each candidate's full BODY and passed the whole accumulated set back through
the jq COMMAND LINE. On Linux a single argument is capped at MAX_ARG_STRLEN (128 KiB), so once the org's
open-issue bodies crossed 128 KiB (July 2026), `execve` returned E2BIG, jq never ran, and EVERY
claim-aware read — who, reap, batch, take, inbox, widen, overlap --active — died at once. It failed CLOSED
(#461 refused to report the empty set as "nobody holds anything"), so it was a loud outage, not a
double-claim — but one no waiting would clear.

STRUCTURALLY ABSENT IN THE ENGINE (disposed on the record, ADR-0040 §5, exactly as case 31 leg 9's
argv-128 KiB cap): the engine reads each body as JSON off `HttpClient` and never marshals the candidate
set through argv, so E2BIG cannot arise. This fixture serves a candidate set BIGGER than the cap only to
prove the engine READS a real-sized set — the property the corpus pins — rather than the plumbing failing.

The world (FS.GG.Audio, off the board — arm B of the claim scan carries the bodies):

    530  fat body (~50 KiB)  fresh marker kite-497   -> HELD, reported with its holder
    531  fat body (~50 KiB)  chatty, NO claim marker -> not in flight (dropped)
    532  fat body (~50 KiB)  chatty, NO claim marker -> not in flight (dropped)

The three bodies together exceed 128 KiB, so a scan that funnelled them through one argv would die; the
engine returns `[530]` with holder kite-497.
"""

import json
import re
import sys
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4978}
OWNER = "FS-GG"
REPO = "FS.GG.Audio"

# ~50 KiB of filler per body: three of them breach MAX_ARG_STRLEN (128 KiB) in aggregate, the exact shape
# of the July-2026 outage (no single body over GitHub's 65,536-char cap — it is the ACCUMULATED set).
FILLER = "x" * 50000
MARKERS = {530: "kite-497"}  # only #530 carries a claim marker; #531/#532 are chatter.
_MARKER_ID = {530: 8530}


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def body(n):
    return f"Paths: src/Fat{n}/**\n\n{FILLER}"


def comments(n):
    ts = _now()
    if n in MARKERS:
        return [{"id": _MARKER_ID[n],
                 "body": f"<!-- fsgg:claim worker={MARKERS[n]} lease=120 -->\nheld",
                 "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]
    # A chatty markerless issue: an `fsgg:msg` is a comment, but it forges no lock.
    return [{"id": 9000 + n, "body": "<!-- fsgg:msg to=* -->\nchatter, no marker",
             "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]


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
        # No board items — every fat issue is off-board, so the scan reaches them through arm B alone.
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}}, "rateLimit": RATE}}
    return None


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
            try:
                q = json.loads(raw).get("query", "")
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            a = graphql(q)
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        # The off-board open-issue scan (arm B) — one page carrying all three fat issues.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, [{"number": n, "title": f"fat body {n}", "state": "open", "body": body(n)}
                                    for n in (530, 531, 532)])
        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": body(n)}) if n in (530, 531, 532) \
                else self._send(404, {"message": "Not Found"})
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

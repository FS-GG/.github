#!/usr/bin/env python3
"""Case 33's fenced-declaration board (`board-pw5`), served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/33-one-item-per-worker-516.sh`, .github#277) certifies that a
`Paths:` line inside a code fence is NOT a declaration. #273's token was UNMATCHABLE (it reserved
nothing and could be named); this one is FABRICATED — every token is well-formed, so a naive parser
sees nothing wrong and the item reserves the WRONG files with complete confidence. A body that quotes a
`Paths:` line — in a repro, in a suggested `widen` — must not ACQUIRE it.

board-pw5:
    #317 Ready  its ONLY `Paths:` line is inside a ``` fence  — reserves NOTHING (an OMISSION)
    #311 Ready  `Paths: scripts/fsgg-coord`                   — a real declaration

so `batch --repo sdd --json` is `["FS.GG.SDD#311"]`: only the honest item schedules, and #317 is passed
over as *"no 'Paths:' declared … this is an OMISSION"* — NOT as *"overlaps batch member #311"*. That
distinction is the whole test: the fail-open (#277) would READ the fenced quote, make #317 declare the
very file #311 declares, and either double-book it or skip it as an overlap. An OMISSION reason proves
the fence was not read.

Lifted verbatim from case 33's board-pw5 + its two seeded bodies, one transport over.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4977}

REPO = {317: "FS.GG.SDD", 311: "FS.GG.SDD"}
# #317's only `Paths:` line is fenced — a quote, not a declaration. #311 declares the same file for real.
BODIES = {
    317: "Repro:\n\n```\nPaths: scripts/fsgg-coord\n```",
    311: "Paths: scripts/fsgg-coord",
}
TITLES = {317: "Fenced only", 311: "Real coord work"}


def board():
    nodes = [{"status": {"name": "Ready"}, "phase": None, "blockedBy": None,
              "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                          "repository": {"nameWithOwner": f"FS-GG/{REPO[n]}"}}} for n in sorted(REPO)]
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}}, "rateLimit": RATE}}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "P"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "s", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "a", "name": "Ready"}]}]}}}, "rateLimit": RATE}}
    if "items(first" in q:
        return board()
    return None


class H(BaseHTTPRequestHandler):
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
        n = int(self.headers.get("Content-Length", 0))
        try:
            q = json.loads(self.rfile.read(n).decode()).get("query", "")
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(q)
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])   # no claims on this board
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES[n]}) if n in BODIES else self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4977, "limit": 5000}}})
        # THE OFF-BOARD OPEN-ISSUE SCAN (case 25). `batch`/`next`/`take` reserve off-board claims too, so
        # every scheduling call fetches this list (bash's `active_claims` arm B). This world's claims are
        # all ON the board, so there is nothing off-board to reserve — an empty list is the honest answer.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, [])

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

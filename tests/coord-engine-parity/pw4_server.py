#!/usr/bin/env python3
"""Case 33's unmatchable-touch-set board (`board-pw4`), served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/33-one-item-per-worker-516.sh`, .github#273) certifies that a
`Paths:` token which matches NO file is refused by the scheduler, never cleared. The docs once promised
"globs"; the matcher implements exact paths + subtree containment. A token that keeps a wildcard after
normalization (`**/x`, `src/*/x`) therefore matches nothing — and a token that matches nothing CONFLICTS
WITH NOTHING. So the failure was OPEN: the scheduler read it as DISJOINT and handed two workers items
whose real files overlapped completely.

board-pw4:
    #300 Ready  Paths: **/packages.lock.json          — unmatchable (a leading '**/' matches nothing)
    #301 Ready  Paths: src/Engine/packages.lock.json   — a real, honest declaration

so `batch --repo sdd --json` is `["FS.GG.SDD#301"]`: only the honest item schedules, and #300 is passed
over WITH ITS REASON — never offered, never called DISJOINT. This serves that exact board so the ENGINE,
over HTTP with no bash in sight, can be held to #273's PROPERTY: an unmatchable token reserves nothing,
so it is refused (unschedulable beats mis-scheduled), not silently cleared into a double-book.

Lifted verbatim from case 33's board-pw4 + its two seeded bodies, one transport over.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4978}

REPO = {300: "FS.GG.SDD", 301: "FS.GG.SDD"}
BODIES = {300: "Paths: **/packages.lock.json", 301: "Paths: src/Engine/packages.lock.json"}
TITLES = {300: "Leading globstar", 301: "Real lockfiles"}


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
            return self._send(200, {"resources": {"graphql": {"remaining": 4978, "limit": 5000}}})
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

#!/usr/bin/env python3
"""#651 — a MARKERLESS item with an open `item/<n>-*` PR is NOT offered (an implementation is in flight).

#581 made the scheduler read an open `item/<n>-*` PR as server-side proof of life — but only THROUGH a claim
marker (the stale-lease leg). A Ready/Backlog item that carries NO marker but DOES have an open PR on its own
branch fell straight through to `Startable` and got handed out a second time, costing a duplicate
implementation. The fix probes the PR on the markerless path too and refuses (`item-pr-open`).

The board (two Ready SDD items, both markerless):
    700 SDD Ready  Paths: src/a/**   no claim marker   — open PR #812 on `item/700-work` (in flight)
    701 SDD Ready  Paths: src/b/**   no claim marker   — NO open PR (the negative control)

`/pulls?state=open` carries one open PR whose head is `item/700-work`; the engine filters it per item by the
`item/<n>-` prefix, so #700 matches and #701 does not. Certified, read-only (no claim writes):

    batch --json  -> ["FS.GG.SDD#701"]    (#700 skipped, #701 chosen)
    batch (stderr) -> names #700 as PR-open / already in flight (#651)
    next          -> FS.GG.SDD#701        (#701, a markerless-but-PR-less item, is STILL startable — the
                                           control that keeps this from passing on a scheduler that simply
                                           stopped offering markerless items)
"""

import json
import re
import sys
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4975}
REPO = "FS-GG/FS.GG.SDD"
STATUS = {700: "Ready", 701: "Ready"}
TITLES = {700: "markerless, PR in flight", 701: "markerless, no PR"}
BODIES = {700: "Paths: src/a/**", 701: "Paths: src/b/**"}
# One open PR, on #700's own item branch. #701 has none.
OPEN_PULLS = [{"number": 812, "state": "open", "head": {"ref": "item/700-work"}}]


def board_items():
    nodes = []
    for n in sorted(STATUS):
        nodes.append({
            "status": {"name": STATUS[n]},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": REPO}},
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
        # prAlive: the per-repo open-PR list the engine filters by `item/<n>-` head prefix.
        if re.match(r"^/repos/[^/]+/[^/]+/pulls$", p):
            return self._send(200, OPEN_PULLS)
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, [])  # markerless — no claim markers on either item
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")}) if n in BODIES \
                else self._send(404, {"message": "Not Found"})
        # The off-board open-issue scan (arm B of active_claims): scheduling reserves off-board claims too.
        # Both items here are ON the board, so there is nothing off-board to reserve — [] is honest.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, [])
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

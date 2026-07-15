#!/usr/bin/env python3
"""Case 45's board-starved (`board-starved`), served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/45-nothing-schedulable-488.sh`, #488) certifies that "nothing
schedulable" is a state that must be OBSERVED, never inferred from an empty stderr — and that its three
causes are told apart. It drives `bash scripts/fsgg-coord take` against this exact board:

    #301 Audio       Ready       blocked by #999 (OPEN, unverifiable)  -> starved: BLOCKED
    #302 Governance  In review   (open, but not a startable column)    -> starved: NON-STARTABLE
    #303 Governance  Done        (CLOSED — must not count as open)      -> excluded
    #304 Templates   Ready       (a real startable item, elsewhere)     -> the control: work exists

so `take --repo audio` is starved-by-blocker, `take --repo governance` is starved-by-column, and
`take --repo sdd` is genuinely empty — three outcomes the old bash code collapsed into one wrong
sentence. This serves that same board so the ENGINE, over HTTP with no bash in sight, can be held to
#488's PROPERTY: a blocked candidate leaves a trace naming its blocker, a non-startable open item leaves
a trace naming its column (so a starved queue is distinguishable from an empty one), a Done item is not a
live candidate, and an unreadable board is a no-verdict — never an empty queue.

The board and its states are lifted verbatim from case 45's `board-starved.json` fixture, so the two
cannot drift: this is that world, one transport over.

FSGG_PARITY_FAIL_BOARD=1 makes the whole-board scan return 401 — the corpus's `GH_FAIL_BOARD=1`, one
transport over. Bootstrap still succeeds; only the items read fails, so the engine gets far enough to
attempt the scan and then must fail CLOSED on it (leg D).
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4988}
FAIL_BOARD = bool(os.environ.get("FSGG_PARITY_FAIL_BOARD"))

# board-starved, verbatim from case 45. Status per item, and the one cross-repo blocker edge (#999).
NODES = [
    {"status": {"name": "Ready"}, "blockedBy": {"text": "FS-GG/FS.GG.SDD#999"},
     "content": {"__typename": "Issue", "number": 301, "title": "Blocked on an open blocker",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Audio"}}},
    {"status": {"name": "In review"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 302, "title": "Open, but in review — not startable",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Governance"}}},
    {"status": {"name": "Done"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 303, "title": "Finished — must not count as open",
                 "state": "CLOSED", "repository": {"nameWithOwner": "FS-GG/FS.GG.Governance"}}},
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 304, "title": "A real, startable item in another repo",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Templates"}}},
]
BODIES = {301: "Paths: src/A301/**", 302: "Paths: src/G302/**",
          303: "Paths: src/G303/**", 304: "Paths: src/T304/**"}
# #999: an OPEN issue (not a PR) — unverifiable, so it blocks (#476's open-blocker leg).
BLOCKERS = {999: {"state": "open"}}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Ready"}, {"id": "o2", "name": "In review"},
                         {"id": "o3", "name": "Done"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "items(first" in q:
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": NODES}}}, "rateLimit": RATE}}
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
        # GH_FAIL_BOARD, one transport over: bootstrap succeeds, the board read is a 401. A failed read
        # must never wear an empty answer's clothes (#488 D).
        if FAIL_BOARD and "items(first" in q:
            return self._send(401, {"message": "Bad credentials"})
        a = graphql(q)
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])   # no claims on this board
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in BODIES:
                return self._send(200, {"number": n, "body": BODIES[n]})
            if n in BLOCKERS:
                return self._send(200, {"number": n, **BLOCKERS[n]})
            return self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})
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

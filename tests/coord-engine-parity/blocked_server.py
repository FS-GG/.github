#!/usr/bin/env python3
"""Case 46's blocked board (`board-blk`), served over HTTP for the compiled engine.

The corpus certifies the #476 blocker rule: a `Blocked by` cleared by a MERGED or a
closed-UNMERGED pull request no longer blocks, while an OPEN one still does. On board-blk:

    #700 blocked by #701 (MERGED)          -> startable
    #702 blocked by #703 (OPEN)            -> NOT offered
    #704 blocked by #705 (closed-unmerged) -> startable

so `batch --repo sdd --json` contains #700 and #704 and excludes #702. This serves that exact
board and those exact blocker states, so the engine's blocker resolution — which reads each
off-board ref over REST (a PR is an issue in REST, carrying `pull_request.merged_at`) — can be
held to the corpus's certified answer.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4980}

# The candidates and their `Blocked by` edges.
BLOCKED_BY = {700: "FS-GG/FS.GG.SDD#701", 702: "FS-GG/FS.GG.SDD#703", 704: "FS-GG/FS.GG.SDD#705"}
BODIES = {700: "Paths: src/S700.fs", 702: "Paths: src/S702.fs", 704: "Paths: src/S704.fs", 799: "Paths: src/S799.fs"}
TITLES = {700: "merged-pr blocker", 702: "open-pr blocker", 704: "closed-pr blocker"}

# The blockers, as REST issue reads (a PR is an issue in REST). #476: merged AND closed-unmerged resolve;
# open blocks.
BLOCKERS = {
    701: {"state": "closed", "pull_request": {"merged_at": "2026-07-11T10:00:00Z"}},  # MERGED -> resolved
    703: {"state": "open", "pull_request": {"merged_at": None}},                       # OPEN   -> blocks
    705: {"state": "closed", "pull_request": {"merged_at": None}},                     # closed unmerged -> resolved
}


def board_items():
    nodes = []
    for n in sorted(BLOCKED_BY):
        nodes.append({
            "status": {"name": "Ready"},
            "blockedBy": {"text": BLOCKED_BY[n]},
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}},
        })
    # #520: a CLOSED issue whose board column was never flipped off Ready. It must NOT be schedulable,
    # however the column reads, and the reason must name the issue state.
    nodes.append({
        "status": {"name": "Ready"},
        "blockedBy": None,
        "content": {"__typename": "Issue", "number": 799, "title": "closed but still Ready", "state": "CLOSED",
                    "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}},
    })
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}}, "rateLimit": RATE}}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "items(first" in q:
        return board_items()
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
        n = int(self.headers.get("Content-Length", 0))
        try:
            q = json.loads(self.rfile.read(n).decode()).get("query", "")
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(q)
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
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
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

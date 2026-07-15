#!/usr/bin/env python3
"""The corpus's parallel-work board (`board-pw`), served over HTTP for the compiled engine.

This is the parity fixture. The shell corpus drives `bash scripts/fsgg-coord batch` against this exact
board through its `gh` stub and CERTIFIES the answer (case 22): `batch --repo sdd --json` →
`["FS.GG.SDD#70","FS.GG.SDD#74"]`, skipping #71 (overlaps the in-flight #42), #72 (no touch-set), and #73
(overlaps batch-member #70). This server presents the SAME board — the same items, the same seeded
touch-sets, the same pre-existing claims — so the ENGINE, over HTTP with no bash in sight, can be held to
that same certified answer. Any divergence is a real gap in the drop-in.

The board and its seeds are lifted verbatim from `tests/fsgg-coord/lib/harness.sh` (board-pw + the
`seed_issue` lines + the #42/#43 claims), so the two fixtures cannot drift: this is that world, one
transport over.
"""

import json
import re
import sys
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4980}

# The seeded touch-sets, verbatim from harness.sh.
BODIES = {
    42: "Paths: src/Audio/**, tests/Audio/**",
    43: "Paths: src/Legacy/**",
    60: "Paths: src/Orphan/**",
    70: "Paths: src/Scene/**, tests/Scene/**",
    71: "Paths: src/Audio/Mixer/**",
    72: "",  # no touch-set declared
    73: "Paths: src/Scene/Sub/**",
    74: "Paths: docs/adr/**",
}

# Board Status per item (In progress for the claimed/orphaned ones, Ready for the candidates).
STATUS = {42: "In progress", 43: "In progress", 60: "In progress",
          70: "Ready", 71: "Ready", 72: "Ready", 73: "Ready", 74: "Ready"}

TITLES = {42: "Audio mixer", 43: "Legacy port", 60: "Nobody claimed me", 70: "Scene graph",
          71: "Mixer tweak", 72: "No touch-set declared", 73: "Scene subtree", 74: "ADR housekeeping"}


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


# Pre-existing claims: #42 held by finch-a3f (FRESH → live, reserves src/Audio), #43 by ghost-000 (5h old →
# STALE, filtered out and therefore NOT reserving), #60 no marker at all.
def comments(n):
    if n == 42:
        return [{"id": 801, "body": "<!-- fsgg:claim worker=finch-a3f lease=120 -->\nheld",
                 "user": {"login": "EHotwagner"}, "updated_at": _now(0)}]
    if n == 43:
        return [{"id": 802, "body": "<!-- fsgg:claim worker=ghost-000 lease=120 -->\ndead",
                 "user": {"login": "EHotwagner"}, "updated_at": _now(-5)}]
    return []


def board_items():
    nodes = []
    for n in sorted(BODIES):
        nodes.append({
            "status": {"name": STATUS[n]},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}},
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
        if self.path.rstrip("/") != "/graphql":
            return self._send(500, {"errors": [{"message": f"unhandled POST {self.path}"}]})
        n = int(self.headers.get("Content-Length", 0))
        try:
            q = json.loads(self.rfile.read(n).decode()).get("query", "")
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(q)
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled query {q[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")}) if n in BODIES else self._send(404, {"message": "Not Found"})
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

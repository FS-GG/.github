#!/usr/bin/env python3
"""Case 34's `overlap` command (#353), served over HTTP for the compiled engine.

`Paths:` tokens are repo-relative, so `TouchSet.conflicts` is only meaningful WITHIN a repo. `overlap`
used to compare an item's tokens against every OTHER repo's live claims, so `scripts/fsgg-coord` in one
repo "collided" with the same string in another — two files in two repositories. The corpus certifies
both shapes are repo-scoped:

    overlap SDD#401 --active                    -> DISJOINT — 402 (Rendering) is another repo; 403/405 (SDD) declare src/Scene
    overlap SDD#401 FS-GG/FS.GG.Rendering#402   -> DISJOINT — different repos, no body read at all
    overlap SDD#403 SDD#405                      -> OVERLAP — a REAL same-repo overlap is still caught
    overlap SDD#405 --active                     -> OVERLAP — ...and so is a same-repo live claim (scoping narrowed the set, not the test)

The board (four items across two repos):
    401 SDD Ready         scripts/fsgg-coord   (the probe, no claim)
    402 Rendering InProg  scripts/fsgg-coord   claim render-x1  — the cross-repo phantom
    403 SDD      InProg   src/Scene/**         claim sdd-sib    — a real SDD neighbour
    405 SDD      InProg   src/Scene/**         claim sdd-sib2   — a second real SDD neighbour
"""

import json
import re
import sys
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4975}

REPO = {401: "FS-GG/FS.GG.SDD", 402: "FS-GG/FS.GG.Rendering",
        403: "FS-GG/FS.GG.SDD", 405: "FS-GG/FS.GG.SDD"}
STATUS = {401: "Ready", 402: "In progress", 403: "In progress", 405: "In progress"}
TITLES = {401: "SDD widen target", 402: "Rendering bystander", 403: "SDD sibling", 405: "SDD sibling 2"}
BODIES = {401: "Paths: scripts/fsgg-coord",
          402: "Paths: scripts/fsgg-coord",
          403: "Paths: src/Scene/**",
          405: "Paths: src/Scene/**"}
# Live claim markers on the three in-flight items (a claim marker IS a comment).
CLAIMS = {402: "render-x1", 403: "sdd-sib", 405: "sdd-sib2"}


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    w = CLAIMS.get(n)
    if not w:
        return []
    return [{"id": 8000 + n, "body": f"<!-- fsgg:claim worker={w} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "updated_at": _now(0)}]


def board_items():
    nodes = []
    for n in sorted(REPO):
        nodes.append({
            "status": {"name": STATUS[n]},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": REPO[n]}},
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
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")}) if n in BODIES \
                else self._send(404, {"message": "Not Found"})
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

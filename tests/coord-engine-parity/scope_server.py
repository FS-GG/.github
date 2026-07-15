#!/usr/bin/env python3
"""Case 13's #480 repo-scope board, served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/13-repo-scope-and-lint.sh`, #480) certifies that a WORKER command
(`next`/`take`/`batch`/`who`) defaults to THE REPO YOU ARE STANDING IN — resolved from the git remote,
free and offline — while a RECONCILER (`ready`) stays org-wide. bash's own bug was the opposite: a bare
`take` in the `.github` checkout initialised `repo=""`, which every board read treats as the whole org,
so it walked past this checkout's items and claimed another repo's — with a worktree command against the
wrong `origin`.

This serves a small multi-repo board so the ENGINE, over HTTP with no bash in sight, can be driven from a
FAKE CHECKOUT (a temp dir whose `origin` points at one repo) and held to #480's property:

    #127 FS.GG.SDD        Ready   Paths: src/sdd/**    -> what a bare next/batch picks from an SDD checkout
    #99  FS.GG.Templates   Ready   Paths: src/tmpl/**   -> what `--repo templates` (a short-id) resolves to
    #141 FS.GG.Game        Ready   Paths: src/game/**   -> the item a bare SDD scope must NOT reach

Every item is Ready (not Backlog) on purpose: #480 is a claim about SCOPE, and the engine's Backlog
promotion is a `--include-backlog` flag by deliberate divergence (case 41 §4, #440) — so keeping the
items Ready tests the scope, not the disposed-of Backlog default. Each carries a disjoint `Paths:` so it
is startable within its own repo scope. There are no claim markers: `who`/`take` read an empty comment
set, and scope is observed on `next`/`batch`/`ready`, not on a lock.
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4988}

# One Ready item per repo, disjoint touch-sets — so scope is the only thing that decides what is picked.
NODES = [
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 127, "title": "SDD work, here in the checkout",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 99, "title": "Templates work, elsewhere",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Templates"}}},
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 141, "title": "Game work, the org-wide temptation",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Game"}}},
]
BODIES = {127: "Paths: src/sdd/**", 99: "Paths: src/tmpl/**", 141: "Paths: src/game/**"}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Ready"}, {"id": "o2", "name": "Backlog"},
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
            return self._send(404, {"message": "Not Found"})
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

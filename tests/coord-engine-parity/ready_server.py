#!/usr/bin/env python3
"""Case 12's `ready` truth-read, served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/12-ready-next-scan.sh`) certifies `ready` as a THRIFTY, machine-
readable TRUTH read of the board:

    * `ready --json` is a JSON ARRAY — the machine contract `/check-board` and `next` consume.
    * it EXCLUDES Done by default, and NOTHING else — Ready and Backlog stay (11 of 14, in the corpus).
    * it is a TRUTH read: it shows what is ON THE BOARD, INCLUDING items the SCHEDULER will refuse — a
      CLOSED-but-Ready issue among them. "The board column is a projection, the issue is the work" (#520);
      `ready` shows the column, and /check-board is what reconciles the two. So a closed row whose column
      still says Ready is SHOWN — hiding it by its issue state is the very drift #520 exists to surface.
    * `--status S` / `--all` WIDEN past the not-Done default: `ready --status Done` shows the Done row;
      `ready --repo .github`, where the only item is Done, is therefore empty by default.
    * `--repo` SCOPES: a namesake column in another repo is not shown.

This serves a board carrying exactly those shapes so the ENGINE — over HTTP, with no bash in the pipeline
— can be held to the SAME certified answers, one transport over. The port gap this closes: the engine's
`ready` ignored `--json` (it always printed the human table) and filtered by the ISSUE STATE rather than
the board Status column, so it BOTH refused the machine contract AND hid the closed-but-Ready row the
truth read exists to show.

    #99  FS.GG.SDD       Ready        OPEN     -> kept (Ready)
    #127 FS.GG.SDD       Backlog      OPEN     -> kept (Backlog)
    #200 FS.GG.SDD       In progress  OPEN     -> kept (not Done)
    #201 FS.GG.SDD       Ready        CLOSED   -> kept: the #520 closed-but-Ready residue (truth read)
    #55  FS.GG.SDD       Done         OPEN     -> excluded by default; shown by --status Done
    #54  .github         Done         OPEN     -> the corpus's #54; .github's only item, so --repo .github
                                                  is empty by default and non-empty under --all
    #202 FS.GG.Rendering Ready        OPEN     -> the scope control: a namesake column, another repo
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4988}

NODES = [
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 99, "title": "A Ready item",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    {"status": {"name": "Backlog"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 127, "title": "A Backlog item",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    {"status": {"name": "In progress"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 200, "title": "Work in flight",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    # #520: CLOSED as completed, but its board column was never flipped off Ready. A TRUTH read shows it;
    # only the scheduler refuses it. The issue state and the column DISAGREE, which is what /check-board
    # is run to find — so `ready` must NOT hide it by its state.
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 201, "title": "Closed, but the column still says Ready",
                 "state": "CLOSED", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    {"status": {"name": "Done"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 55, "title": "A finished SDD item",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}},
    # The corpus's #54: a Done card in .github (an Issue, not a PR — the case-12 fixture's own shape).
    {"status": {"name": "Done"}, "blockedBy": {"text": "RESOLVED: FS-GG/FS.GG.SDD#8 shipped"},
     "content": {"__typename": "Issue", "number": 54, "title": "Dependency Dashboard",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/.github"}}},
    {"status": {"name": "Ready"}, "blockedBy": None,
     "content": {"__typename": "Issue", "number": 202, "title": "A namesake in another repo",
                 "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/FS.GG.Rendering"}}},
]
BODIES = {99: "Paths: src/A99/**", 127: "Paths: src/B127/**", 200: "Paths: src/C200/**",
          201: "Paths: src/D201/**", 55: "Paths: src/E55/**", 54: "Paths: none",
          202: "Paths: src/F202/**"}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Backlog"}, {"id": "o2", "name": "Ready"},
                         {"id": "o3", "name": "In progress"}, {"id": "o4", "name": "Done"}]},
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
            return self._send(200, [])   # no claims on this board — `ready` reads none
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            num = int(m.group(1))
            if num in BODIES:
                return self._send(200, {"number": num, "body": BODIES[num]})
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

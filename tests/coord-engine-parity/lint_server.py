#!/usr/bin/env python3
"""Case 14's NO-TOUCH-SET / BAD-TOUCH-SET world (#496), served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/14-no-touch-set-and-done.sh`) certifies the rule whose absence let
`lint` report `0 error(s)` over a DEAD queue: a Ready/Backlog OPEN issue that no worker can ever pick up —
because it declares no `Paths:` at all (NO-TOUCH-SET), or declared one every token of which is unmatchable
(BAD-TOUCH-SET) — is an ERROR. The NEGATIVES matter as much as the positives: the `Paths: none` sentinel
suppresses it (a deliberate touch-set-less epic/decision item is legitimate, but must SAY so); a fenced-only
`Paths:` line is no declaration (a quotation reserves nothing, #277); and In progress / closed / real-Paths
items are clean.

This serves that board + each candidate's issue body over REST, so the ENGINE reaches the same verdicts
with no bash in sight. The board lives in FS-GG/FS.GG.SDD, plus one clean FS-GG/FS.GG.Game item so the
`--repo` scope + exit codes can be exercised (a repo with errors -> 1, a clean repo -> 0).

    #420  SDD   Ready         no Paths at all           -> NO-TOUCH-SET
    #421  SDD   Ready         ONLY a FENCED Paths line   -> NO-TOUCH-SET (a fenced decl is none, #277)
    #430  SDD   Ready         Paths: **/only-unmatchable -> BAD-TOUCH-SET (every token unmatchable)
    #400  SDD   Ready [epic]  Paths: none                -> clean (the sentinel is the whole point)
    #422  SDD   Ready         Paths: none                -> clean (a decision item, declared none)
    #407  SDD   Ready         Paths: src/Real/**         -> clean (a real touch-set)
    #423  SDD   In progress   (n/a)                      -> clean (already claimed, not a candidate)
    #424  SDD   Ready/CLOSED  (n/a)                      -> clean (closed, nobody needs to pick it up)
    #500  Game  Ready         Paths: src/game/**         -> clean (a second repo, for --repo scope)
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4988}

SDD = "FS-GG/FS.GG.SDD"
GAME = "FS-GG/FS.GG.Game"


def node(n, status, repo, state="OPEN", title=""):
    return {"status": {"name": status}, "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": title or f"item {n}",
                        "state": state, "repository": {"nameWithOwner": repo}}}


NODES = [
    node(420, "Ready", SDD, title="Real work, forgot the touch-set"),
    node(421, "Ready", SDD, title="Declares its paths only inside a fence"),
    node(430, "Ready", SDD, title="Every token unmatchable"),
    node(400, "Ready", SDD, title="[epic] rollup umbrella"),
    node(422, "Ready", SDD, title="A decision item"),
    node(407, "Ready", SDD, title="Ordinary work"),
    node(423, "In progress", SDD, title="Already being worked"),
    node(424, "Ready", SDD, state="CLOSED", title="Closed, nobody needs it"),
    node(500, "Ready", GAME, title="Clean game work"),
]

# #421's ONLY `Paths:` line is fenced — the scheduler cannot see it, so it declares nothing (#277).
FENCED = (
    "Here is how the protocol wants a touch-set declared:\n"
    "\n"
    "```\n"
    "Paths: src/Fenced/**\n"
    "```\n"
    "\n"
    "...but this issue forgot to write a real one outside the fence.\n"
)

BODIES = {
    420: "Real work here, but nobody remembered the touch-set.\n\nJust prose.",
    421: FENCED,
    430: "Paths: **/only-unmatchable",
    400: "An umbrella epic.\n\nPaths: none",
    422: "Should we do X or Y? A decision.\n\nPaths: none",
    407: "Paths: src/Real/**",
    500: "Paths: src/game/**",
    # 423 (In progress) and 424 (closed) are NOT candidates, so the engine must never read their bodies.
}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Ready"}, {"id": "o2", "name": "Backlog"},
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
            return self._send(200, [])
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

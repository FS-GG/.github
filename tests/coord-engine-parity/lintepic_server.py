#!/usr/bin/env python3
"""Case 14's `lint` EPIC-ROLL-UP-GRAPH rules, served over HTTP for the compiled engine.

The corpus (`14-no-touch-set-and-done.sh`) certifies the epic-graph half of `lint`: an [epic] with zero
sub-issues (EPIC-NO-CHILDREN); one whose child list is TRUNCATED so rollup cannot be verified
(EPIC-CHILDREN-TRUNCATED); a board-Done epic over a still-open child (EPIC-DONE-OPEN-CHILD); a board-Done
issue that is still open (DONE-STATUS-OPEN-ISSUE, a note); and — the intricate one — an epic whose BODY
declares a child the sub-issue graph does not contain (EPIC-UNLINKED-CHILD), with a body-cited PR ref
dropped (a PR can never be a sub-issue, #346) and an unresolvable ref KEPT (fail closed, #266).

This serves that world: a board, each epic's sub-issue graph over GraphQL (`subIssues{ totalCount nodes }`),
each item's body over REST, and the PR-probe (`issues/{n}` carries `pull_request` iff it is a PR). All epics
carry `Paths: none` so the schedulability rules stay silent on them (the sentinel is the deliberate out).

    #440  SDD   [epic] Ready OPEN   0 sub-issues                 -> EPIC-NO-CHILDREN
    #404  Rend  [epic] Ready OPEN   5 total, 2 visible           -> EPIC-CHILDREN-TRUNCATED (no unlinked)
    #450  SDD   [epic] Done  OPEN   #451 open, #452 closed       -> EPIC-DONE-OPEN-CHILD + DONE-STATUS note
    #409  SDD   [epic] Ready OPEN   graph {#413}; body declares  -> EPIC-UNLINKED-CHILD names #414
                                     #413,#414,PR #418; prose #415   (#418 pruned as PR, #415 not a child)
    #470  SDD   [epic] Ready OPEN   graph {#471}; body declares #471 -> clean (negative control)
    #460  SDD   (plain) Done OPEN   -                            -> DONE-STATUS-OPEN-ISSUE note only

  FSGG_PARITY_FAIL_ISSUE=<n>  the PR-probe GET for issue <n> returns 502 (the fail-closed leg: an
                              unresolvable ref must be KEPT, not silently dropped).
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4988}
FAIL_ISSUE = os.environ.get("FSGG_PARITY_FAIL_ISSUE")

SDD = "FS-GG/FS.GG.SDD"
REND = "FS-GG/FS.GG.Rendering"
GAME = "FS-GG/FS.GG.Game"


def bnode(n, status, repo, state, title):
    return {"status": {"name": status}, "severity": {"name": "Low"}, "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": title,
                        "state": state, "repository": {"nameWithOwner": repo}}}


NODES = [
    bnode(440, "Ready", SDD, "OPEN", "[epic] no children"),
    bnode(404, "Ready", REND, "OPEN", "[epic] truncated graph"),
    bnode(450, "Done", SDD, "OPEN", "[epic] done over an open child"),
    bnode(409, "Ready", SDD, "OPEN", "[epic] declares an unlinked child"),
    bnode(470, "Ready", SDD, "OPEN", "[epic] healthy"),
    bnode(460, "Done", SDD, "OPEN", "A plain item marked Done but still open"),
    # A Game scope with ONLY a note (no error): lint --repo game exits 0, --strict makes the note fatal.
    bnode(480, "Done", GAME, "OPEN", "A plain Game item marked Done but still open"),
]

# Each epic's sub-issue graph: (totalCount, [(number, state, repo)]).
SUBS = {
    440: (0, []),
    404: (5, [(405, "CLOSED", REND), (406, "CLOSED", REND)]),   # 5 total, 2 visible -> truncated
    450: (2, [(451, "OPEN", SDD), (452, "CLOSED", SDD)]),
    409: (1, [(413, "CLOSED", SDD)]),                            # complete; body declares more
    470: (1, [(471, "CLOSED", SDD)]),                            # complete; body declares only #471
}

# Every Ready/OPEN row here declares a `Class:` line (.github#1588). These fixtures exist to certify the
# EPIC rules, and their assertions are stated as "#470 yields no ERROR" and "--repo rendering yields only
# EPIC-CHILDREN-TRUNCATED" — sentences about the whole finding set, not about the epic rules alone. Without
# a class, CLASS-UNSET fires on each and those sentences stop being about epics at all. Declaring one keeps
# each row a negative control for the rule it was built to control, which is what the assertions read.
BODIES = {
    440: "Paths: none\n\nClass: hardening",
    404: "Umbrella epic.\n\nPaths: none\n\nClass: hardening\n\n- [ ] #499 the missing child",
    450: "Paths: none\n\nClass: hardening",
    409: ("Paths: none\n\n"
          "Class: hardening\n\n"
          "- [x] #413 done\n"
          "- [ ] #414 still open\n"
          "- [x] PR #418 landed the plumbing\n"
          "\n"
          "See also #415 in prose — a mention, not a child.\n"),
    470: "Paths: none\n\nClass: hardening\n\n- [x] #471 done",
    413: "", 414: "", 471: "", 451: "", 452: "", 405: "", 406: "", 499: "",
}

# #418 is the PULL REQUEST cited in #409's checklist — the probe must see `pull_request` and drop it.
PRS = {418}


def graphql(query, variables):
    if "subIssues" in query:
        n = int(variables.get("number"))
        total, nodes = SUBS.get(n, (0, []))
        return {"data": {"repository": {"issue": {"subIssues": {
            "totalCount": total,
            "nodes": [{"number": num, "state": st, "repository": {"nameWithOwner": rp}}
                      for (num, st, rp) in nodes]}}}}, "rateLimit": RATE}
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Ready"}, {"id": "o2", "name": "Backlog"},
                         {"id": "o3", "name": "In progress"}, {"id": "o4", "name": "Done"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "items(first" in query:
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": NODES}}}, "rateLimit": RATE}}
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
            doc = json.loads(self.rfile.read(n).decode())
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(doc.get("query", ""), doc.get("variables", {}) or {})
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {doc.get('query','')[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if FAIL_ISSUE is not None and n == int(FAIL_ISSUE):
                return self._send(502, {"message": "Bad Gateway"})
            payload = {"number": n, "body": BODIES.get(n, "")}
            if n in PRS:
                payload["pull_request"] = {"url": f"https://github.com/x/y/pull/{n}"}
            return self._send(200, payload)
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})
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

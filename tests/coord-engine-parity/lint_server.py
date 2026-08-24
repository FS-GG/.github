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
    #431  SDD   Ready         Paths: real ** /nope        -> BAD-TOUCH-SET (SOME tokens unmatchable, #646)
    #400  SDD   Ready [epic]  Paths: none                -> clean (the sentinel is the whole point)
    #422  SDD   Ready         Paths: none                -> clean (a decision item, declared none)
    #407  SDD   Ready         Paths: src/Real/**         -> clean (a real touch-set)
    #423  SDD   In progress   (n/a)                      -> clean (already claimed, not a candidate)
    #424  SDD   Ready/CLOSED  (n/a)                      -> clean (closed, nobody needs to pick it up)
    #500  Game  Ready         Paths: src/game/**         -> clean (a second repo, for --repo scope)

    The CLASS axis (.github#1588). CLASS-UNSET fires on a Ready/Backlog OPEN row whose own TEXT records no
    class, so it fires on every row above except #423 (In progress), #424 (closed) and #500 (classed, so
    that the --repo game clean-scope leg stays clean). These four exercise the derivations and the refusal:

    #432  SDD   Ready         Class: hardening           -> clean of CLASS-UNSET (an explicit body line)
    #433  SDD   Ready         [decision] title prefix    -> clean of CLASS-UNSET (derived from the TITLE)
    #434  SDD   Ready         Blocked on: human/decision -> clean of CLASS-UNSET (derived, ADR-0045, AC5)
    #435  SDD   Ready         Class: bug                 -> CLASS-INVALID (a word, and NOT a declaration)
    #436  SDD   Ready         Class: Defect  (case)      -> clean (ADR-0066 normalises case and space)

    #435 CHANGED CODE HERE, AND THE CHANGE IS THE POINT (.github#1651). It used to assert CLASS-UNSET,
    which was true of the engine and false of the row: #435 DOES record a `Class:` line. Two live rows
    were reported that way in one run — `FS.GG.Templates#321` (`Class: docs`) and `FS.GG.SDD#747`
    (`Class: enhancement`) — and in both cases the author had written the line and believed it. An
    unknown word is still NO declaration (the refusal #1588 AC3 bought is intact, and #435 is still an
    error); what changed is that the diagnostic now names the fault the row actually has.
"""

import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4988}
MISSING_BODY = int(os.environ.get("FSGG_LINT_MISSING_BODY", "-1"))
BODY_READS = []

SDD = "FS-GG/FS.GG.SDD"
GAME = "FS-GG/FS.GG.Game"


def node(n, status, repo, state="OPEN", title="", severity="Low"):
    return {"status": {"name": status},
            "severity": {"name": severity} if severity else None,
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": title or f"item {n}",
                        "state": state, "repository": {"nameWithOwner": repo}}}


NODES = [
    node(420, "Ready", SDD, title="Real work, forgot the touch-set"),
    node(421, "Ready", SDD, title="Declares its paths only inside a fence"),
    node(430, "Ready", SDD, title="Every token unmatchable"),
    node(431, "Ready", SDD, title="Some tokens unmatchable (#646)"),
    node(400, "Ready", SDD, title="[epic] rollup umbrella"),
    node(422, "Ready", SDD, title="A decision item"),
    node(407, "Ready", SDD, title="Ordinary work"),
    node(423, "In progress", SDD, title="Already being worked"),
    node(424, "Ready", SDD, state="CLOSED", title="Closed, nobody needs it"),
    node(500, "Ready", GAME, title="Clean game work"),
    node(432, "Ready", SDD, title="Declares its class in the body"),
    node(433, "Ready", SDD, title="[decision] should we do X or Y"),
    node(434, "Ready", SDD, title="Parked on a human decision"),
    node(435, "Ready", SDD, title="Declares a class word nobody speaks"),
    node(436, "Ready", SDD, title="Declares a legal class in the wrong case"),
    node(437, "Ready", SDD, title="Severity has not been triaged", severity=None),
    node(438, "Done", SDD, title="Done without severity", severity=None),
    node(439, "Ready", SDD, state="CLOSED", title="Closed without severity", severity=None),
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
    # #646 — a PARTIAL declaration: one real subtree AND one unmatchable token. lint must fire BAD-TOUCH-SET
    # and name ONLY the unmatchable one; the matchable `src/Partial/**` is fine and must not be flagged.
    431: "Paths: src/Partial/** **/nope-unmatchable",
    400: "An umbrella epic.\n\nPaths: none",
    422: "Should we do X or Y? A decision.\n\nPaths: none",
    407: "Paths: src/Real/**",
    # The body-projection lint added by .github#2907 reads every open, non-Done row, including work in
    # progress. Keep #423 ordinary and classed so it isolates that census expansion without adding a
    # schedulability/class finding of its own.
    423: "Paths: src/Real/**\n\nClass: hardening",
    # CLASSED, and it has to be: this is the ONLY row in the `--repo game` scope, and case14 asserts that
    # scope is clean AND empty. An unclassed row here would make CLASS-UNSET fire and turn the corpus's one
    # "a real item, really scanned, and nothing wrong with it" leg into a finding — the assertion would then
    # be certifying the wrong thing rather than failing honestly.
    500: "Paths: src/game/**\n\nClass: hardening",
    # The three derivations (.github#1588 AC3/AC5) and the one refusal.
    432: "Paths: src/Real/**\n\nClass: hardening",
    # No `Class:` line at all — the `[decision]` TITLE prefix is the whole declaration. `Paths: none`
    # keeps NO-TOUCH-SET quiet so the row isolates the class axis.
    433: "Paths: none",
    # Neither a `Class:` line nor a title prefix: ADR-0045's sentinel is read as evidence, so an item that
    # already says "a human must choose" does not have to say it twice.
    434: "Paths: none\n\nBlocked on: human/decision",
    # An unknown word is NOT a declaration. Mapping `bug` onto `defect` would be a guess wearing a parser's
    # authority, and the row would look triaged — so this row must still be an ERROR. It is CLASS-INVALID
    # rather than CLASS-UNSET (.github#1651): the row wrote a line, so telling it that it "records no
    # `Class:`" named a fault it did not have.
    435: "Paths: src/Real/**\n\nClass: bug",
    # ADR-0066: *"value normalised for case and space"*. The AC4 decision made explicit — a case variant of
    # a legal value is ACCEPTED, because `Types.itemClassOfWireName` is the same normalisation `reconcile`
    # reads the board column back with, and refusing it would report a correctly-triaged row as untriaged.
    436: "Paths: src/Real/**\n\nClass:   Defect  ",
    437: "Paths: none\n\nClass: hardening",
    # 424 is closed, so the engine must never read its body.
}


def graphql(q, variables=None):
    if "subIssues" in q:
        # #400 is titled "[epic]", so `lint` reads its sub-issue graph. A healthy, complete graph (one
        # linked, closed child) so the epic rules stay silent — this slice is about the schedulability
        # rules, and #400 is here only to prove `Paths: none` suppresses NO-TOUCH-SET on an epic.
        return {"data": {"repository": {"issue": {"subIssues": {"totalCount": 1, "nodes": [
            {"number": 401, "state": "CLOSED", "repository": {"nameWithOwner": SDD}}]}}}}, "rateLimit": RATE}
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
        if p.rstrip("/") == "/_body_reads":
            return self._send(200, BODY_READS)
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            BODY_READS.append(n)
            if n in BODIES and n != MISSING_BODY:
                return self._send(200, {"number": n, "body": BODIES[n]})
            return self._send(404, {"message": "Not Found"})
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

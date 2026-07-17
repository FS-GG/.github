#!/usr/bin/env python3
"""Case 11's board, served over HTTP for the compiled engine (#448 — `set-field --batch`).

THE ACCEPTANCE CRITERION IS THE CALL COUNT. GitHub bills these field mutations at the 1-point floor, so
cost tracks REQUESTS: three separate writes are three points; the same three aliased into one document is
one. The shell corpus (case 11) counts `gh` invocations to prove it. An F# tool calling HTTPS directly is
invisible to that stub — so this fixture moves the count one transport over: it records every FIELD
mutation document the engine POSTs, and the parity harness asserts that a three-field batch emits exactly
ONE of them (carrying f0/f1/f2), never three. The property ("N fields cost one request") is unchanged; it
is checked at the HTTP layer, which is ADR-0040 C1's whole resolution.

The board:
    Phase    SINGLE_SELECT  (option "P2 SDD" -> opt_p2, "P1 SDD" -> opt_p1)
    Target   DATE
    Contract TEXT
    Status   SINGLE_SELECT  (Ready/In progress/Done) — present so bootstrap resolves a real board
and FS.GG.SDD#42 is item PVTI_item42 on project #12 ("Coordination").

Env toggles select the mutation's outcome, one per invocation:
    (default)          every alias lands — a clean batch, HTTP 200 with a non-null `data.fN` each.
    SF_FAIL_ALIAS=f1   the PARTIAL arm — mutations run SERIALLY, so aliases BEFORE f1 land and f1 does
                       not. HTTP 200 carrying BOTH `data` (f0 non-null, f1 null) and
                       `errors[].path[0]="f1"`, which is exactly how GraphQL reports a mid-document
                       failure. NEVER queued: replaying would rewrite the half that landed.
    SF_RATELIMIT=1     the budget refused the whole document — nothing landed — so the engine defers
                       EVERY pair. HTTP 200 with a rate-limit `errors` message the engine classifies as
                       RateLimited (EX_RATE, 75).
"""

import json
import os
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4960}
ITEM = 42
ITEM_ID = "PVTI_item42"

FAIL_ALIAS = os.environ.get("SF_FAIL_ALIAS", "")
RATELIMIT = os.environ.get("SF_RATELIMIT", "") == "1"

LOCK = threading.Lock()
# Every FIELD mutation document the engine POSTs, in order. The count IS the #448 assertion.
MUTATIONS = []

FIELDS = [
    {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
     "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"},
                 {"id": "opt_done", "name": "Done"}]},
    {"id": "PVTSSF_phase", "name": "Phase", "dataType": "SINGLE_SELECT",
     "options": [{"id": "opt_p1", "name": "P1 SDD"}, {"id": "opt_p2", "name": "P2 SDD"}]},
    {"id": "PVTF_target", "name": "Target", "dataType": "DATE"},
    {"id": "PVTF_contract", "name": "Contract", "dataType": "TEXT"},
]


def _aliases(doc):
    """The alias names the engine put in this document, in order: f0, f1, ..."""
    return re.findall(r"\bf(\d+):", doc)


def _is_field_mutation(doc):
    return "updateProjectV2ItemFieldValue" in doc or "clearProjectV2ItemFieldValue" in doc


def mutation_response(doc):
    """The outcome of a FIELD mutation document, chosen by env — clean / partial / rate-limited."""
    if RATELIMIT:
        # The whole document is refused. `data` is null, and the message matches the engine's rate-limit
        # pattern (`API rate limit ...`) so `graphQlData` classifies it RateLimited BEFORE it is read as a
        # partial. Nothing landed — so every pair is safe to queue.
        return {"data": None, "errors": [{"message": "API rate limit exceeded for installation"}]}

    aliases = ["f" + n for n in _aliases(doc)]

    if FAIL_ALIAS and FAIL_ALIAS in aliases:
        # SERIAL execution: the aliases BEFORE the failing one landed; the failing one is null; the ones
        # AFTER it were never reached (GraphQL stops the document at the first mutation error).
        idx = aliases.index(FAIL_ALIAS)
        data = {a: {"clientMutationId": None} for a in aliases[:idx]}
        data[FAIL_ALIAS] = None
        return {"data": data,
                "errors": [{"path": [FAIL_ALIAS], "message": f"stub: {FAIL_ALIAS} rejected"}]}

    # A clean batch: every alias landed.
    return {"data": {a: {"clientMutationId": None} for a in aliases}, "rateLimit": RATE}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": FIELDS}}}}, "rateLimit": RATE}
    if "projectItems" in q:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
            {"id": ITEM_ID, "project": {"number": 12}}]}}}}, "rateLimit": RATE}
    if _is_field_mutation(q):
        with LOCK:
            MUTATIONS.append(q)
        return mutation_response(q)
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
        # The count and the last document, for the parity harness — the `gh` stub's `gcount`, one
        # transport over.
        if p.rstrip("/") == "/_mutations":
            with LOCK:
                return self._send(200, {"count": len(MUTATIONS), "last": MUTATIONS[-1] if MUTATIONS else ""})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n}) if n == ITEM else self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4960, "limit": 5000}}})
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

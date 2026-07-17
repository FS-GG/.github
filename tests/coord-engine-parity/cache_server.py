#!/usr/bin/env python3
"""The corpus's `cache and budget` world (case 10), served over HTTP for the compiled engine.

The shell corpus drives `bash scripts/fsgg-coord` against a counting `gh` stub and CERTIFIES the resolver
budget: `bootstrap` costs exactly TWO GraphQL calls (projects + fields); `board`/`field-id`/`option-id`
read the day-cached id map and cost ZERO more; `item-id` resolves an issue's board item in exactly ONE
call and then serves it from cache (zero) — including the `owner/repo#n` and full-URL spellings of the
same issue. This server presents that world one transport over: it answers the two bootstrap queries and
the item-id lookup, routes `set-field` by field dataType (single-select → optionId, DATE → date, TEXT →
text, empty → the clear mutation), and COUNTS GraphQL requests by category so every "costs N calls"
assertion can be re-expressed at the HTTP layer (ADR-0040 §3/§5 — the call-counting transformation).

The board and its fields are lifted from harness.sh's fields.json: board #12 'Coordination' in FS-GG, a
single-select `Phase` (option "P2 SDD" → opt_p2), a `Target` DATE, and a `Contract` TEXT. FS.GG.SDD#42 is
the Coordination item PVTI_coord123.
"""

import json
import os
import re
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4980}

FIELDS = [
    {"id": "PVTSSF_phase", "name": "Phase", "dataType": "SINGLE_SELECT",
     "options": [{"id": "opt_p2", "name": "P2 SDD"}, {"id": "opt_p3", "name": "P3 Impl"}]},
    {"id": "PVTF_target", "name": "Target", "dataType": "DATE"},
    {"id": "PVTF_contract", "name": "Contract", "dataType": "TEXT"},
]

LOCK = threading.Lock()
_WRITES = []   # [ {kind, fieldId, value} ] — every board mutation, in order
_GQL = {"projectsV2": 0, "fields": 0, "itemId": 0, "mutations": 0, "total": 0}


def graphql(query, variables):
    with LOCK:
        _GQL["total"] += 1
        # ORDER IS THE CONTRACT — discriminate on the narrowest token, most specific first. The mutations
        # carry `ProjectV2ItemFieldValue`; only the projects query carries `projectsV2` (with the `s`); only
        # the item lookup carries `projectItems`; only the fields query carries `fields(first`.
        if "updateProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            # The value var present names the route the engine chose (valueClause): a single-select write
            # carries `optionId`, a DATE `date`, a TEXT `text`, a NUMBER `number`.
            for k in ("optionId", "date", "text", "number"):
                if k in variables:
                    _WRITES.append({"kind": k, "fieldId": variables.get("fieldId"),
                                    "itemId": variables.get("itemId"), "value": variables.get(k)})
                    break
            return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
        if "clearProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            _WRITES.append({"kind": "clear", "fieldId": variables.get("fieldId"),
                            "itemId": variables.get("itemId"), "value": None})
            return {"data": {"clearProjectV2ItemFieldValue": {"clientMutationId": None}}}
        if "projectsV2" in query:
            _GQL["projectsV2"] += 1
            return {"data": {"organization": {"projectsV2": {"nodes": [
                {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
        if "fields(first" in query:
            _GQL["fields"] += 1
            return {"data": {"organization": {"projectV2": {"fields": {"nodes": FIELDS}}}}, "rateLimit": RATE}
        if "projectItems" in query:
            _GQL["itemId"] += 1
            # Narrowed to OUR board (#12). A second board is present to prove the engine picks ours.
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"id": "PVTI_other", "project": {"number": 7}},
                {"id": "PVTI_coord123", "project": {"number": 12}}]}}}, "rateLimit": RATE}}
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
                doc = json.loads(raw)
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            a = graphql(doc.get("query", ""), doc.get("variables", {}) or {})
            return self._send(200, a if a is not None else
                              {"errors": [{"message": f"unhandled query {doc.get('query','')[:60]}"}]})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p.rstrip("/") == "/_gql":
            with LOCK:
                return self._send(200, dict(_GQL))
        if p.rstrip("/") == "/_writes":
            with LOCK:
                return self._send(200, {"writes": list(_WRITES), "count": len(_WRITES),
                                        "last": _WRITES[-1] if _WRITES else None})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            return self._send(200, {"number": int(m.group(1)), "body": ""})
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

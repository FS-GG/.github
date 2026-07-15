#!/usr/bin/env python3
"""The corpus's `claim restores column` world (case 21, #481), served over HTTP for the compiled engine.

The shell corpus drives `bash scripts/fsgg-coord` against a counting `gh` stub and CERTIFIES that a claim
RECORDS the board column it is about to overwrite — in its own marker, `prev=<column>` — so that `release`
puts that column back rather than guessing `Ready`. This server presents the same world one transport over:
it answers the engine's `fieldValueByName` pre-claim read, records every posted/patched marker body so the
`prev=` key can be read back, records which Status option each board write carried (bash asserts this on
`opt_backlog`/`opt_review` in its `GH_LOG`), and COUNTS GraphQL requests by category so the case's cost pin
— #481 spends exactly ONE item-scoped read, on the winning path only, never a board scan (#418) — can be
re-expressed at the HTTP layer.

Env, all optional (each parity leg spawns a fresh server):
  FSGG_PARITY_STATUS=<name>   the column the item-Status read returns (default "Backlog"); "" = on the
                              board with NO Status set (`fieldValueByName` null).
  FSGG_PARITY_FAIL_STATUS=1   the item-Status read fails (a 502) — the corpus's GH_FAIL_ITEM_STATUS.
  FSGG_PARITY_MARKERS=<json>  a JSON array of pre-existing markers to seed, each
                              {"n":<issue>, "id":<comment-id>, "worker":<w>, "prev":<enc?>, "age_hours":<h?>}.
                              `prev` omitted → a marker minted before #481 (no `prev=` key); `age_hours`
                              defaults to 0 (a fresh, live lease).
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4980}

STATUS_NAME = os.environ.get("FSGG_PARITY_STATUS", "Backlog")
FAIL_STATUS = os.environ.get("FSGG_PARITY_FAIL_STATUS") == "1"

# The full Status option set, verbatim from harness.sh (fields.json): the restore target may be any of them.
OPTIONS = [
    {"id": "opt_backlog", "name": "Backlog"},
    {"id": "opt_ready", "name": "Ready"},
    {"id": "opt_wip", "name": "In progress"},
    {"id": "opt_review", "name": "In review"},
    {"id": "opt_blocked", "name": "Blocked"},
    {"id": "opt_done", "name": "Done"},
]

LOCK = threading.Lock()
_STORE = {}          # issue number -> [ {id, body, user, updated_at} ]
_NEXT_ID = [900]
_WRITES = []         # [ {item, field, optionId} ] — every Status board write, in order
_GQL = {"projectsV2": 0, "fields": 0, "itemId": 0, "itemStatus": 0, "mutations": 0, "total": 0}


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _seed():
    for m in json.loads(os.environ.get("FSGG_PARITY_MARKERS", "[]")):
        prev = f" prev={m['prev']}" if m.get("prev") else ""
        body = f"<!-- fsgg:claim worker={m['worker']} lease=120{prev} -->\nheld"
        _STORE.setdefault(int(m["n"]), []).append(
            {"id": int(m["id"]), "body": body, "user": {"login": "EHotwagner"},
             "updated_at": _now(int(m.get("age_hours", 0)))})


def graphql(query, variables):
    with LOCK:
        _GQL["total"] += 1
        # ORDER IS THE CONTRACT. The item-Status read (`fieldValueByName`) also contains `projectItems`, and
        # the bootstrap `fields` read also contains `first`, so each branch is discriminated on its NARROWEST
        # token, most specific first — exactly as the bash stub keys on `fieldValueByName` before the plain
        # item-id lookup (harness.sh:512).
        if "projectsV2" in query:
            _GQL["projectsV2"] += 1
            return {"data": {"organization": {"projectsV2": {"nodes": [
                {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
        if "fields(first" in query:
            _GQL["fields"] += 1
            return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
                {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT", "options": OPTIONS},
                {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
        if "fieldValueByName" in query:
            _GQL["itemStatus"] += 1
            if FAIL_STATUS:
                return ("FAIL_502", None)
            fv = None if STATUS_NAME == "" else {"name": STATUS_NAME}
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"project": {"number": 7}, "fieldValueByName": {"name": "Wrong board"}},
                {"project": {"number": 12}, "fieldValueByName": fv}]}}}, "rateLimit": RATE}}
        if "projectItems" in query:
            _GQL["itemId"] += 1
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"id": "PVTI_item", "project": {"number": 12}}]}}}, "rateLimit": RATE}}
        if "updateProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            _WRITES.append({"item": variables.get("itemId"), "field": variables.get("fieldId"),
                            "optionId": variables.get("optionId")})
            return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
        if "clearProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            return {"data": {"clearProjectV2ItemFieldValue": {"clientMutationId": None}}}
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
                doc = json.loads(raw)
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            a = graphql(doc.get("query", ""), doc.get("variables", {}) or {})
            if isinstance(a, tuple) and a[0] == "FAIL_502":
                return self._send(502, {"message": "Bad Gateway"})
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled query {doc.get('query','')[:60]}"}]})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            body = ""
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                pass
            with LOCK:
                cid = _NEXT_ID[0]
                _NEXT_ID[0] += 1
                _STORE.setdefault(int(m.group(1)), []).append(
                    {"id": cid, "body": body, "user": {"login": "EHotwagner"}, "updated_at": _now(0)})
            return self._send(201, {"id": cid, "body": body, "updated_at": _now(0)})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_PATCH(self):
        raw = self.rfile.read(int(self.headers.get("Content-Length", 0))).decode()
        p = self.path.split("?", 1)[0]
        # Heartbeat rewrites the WHOLE marker body by comment id. Anything it does not carry forward is
        # destroyed — the case's point (c) — so the store must let the engine's re-emitted body (which
        # carries `prev=` forward) replace the old one.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            cid = int(m.group(1))
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                body = ""
            with LOCK:
                for cs in _STORE.values():
                    for c in cs:
                        if c.get("id") == cid:
                            c["body"] = body
                            c["updated_at"] = _now(0)
            return self._send(200, {"id": cid, "body": body})
        self._send(500, {"message": f"unhandled PATCH {p}"})

    def do_DELETE(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            cid = int(m.group(1))
            with LOCK:
                for n in list(_STORE):
                    _STORE[n] = [c for c in _STORE[n] if c.get("id") != cid]
            return self._send(204, {})
        self._send(500, {"message": f"unhandled DELETE {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p.rstrip("/") == "/_gql":
            with LOCK:
                return self._send(200, dict(_GQL))
        if p.rstrip("/") == "/_writes":
            with LOCK:
                return self._send(200, {"writes": list(_WRITES), "count": len(_WRITES),
                                        "last": _WRITES[-1] if _WRITES else None})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            with LOCK:
                return self._send(200, list(_STORE.get(int(m.group(1)), [])))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            return self._send(200, {"number": int(m.group(1)), "body": ""})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})


def main():
    _seed()
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

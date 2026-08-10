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

THE STATUS COLUMN IS STATEFUL, and #331 is why. A Status write UPDATES what the next item-Status read
returns, so the fixture models a board rather than merely recording writes at it. It was static until
#331: the read's answer was fixed at startup, so a `claim` — whose whole job is to write `In progress`
over the column it recorded — left the fixture still reporting the PRE-claim column. That is a world the
real board cannot produce, and it mattered the moment `release` began READING the live column (#331):
every claim→release leg would have seen the pre-claim column at release time and preserved it, reporting
a defect the engine does not have. A fixture that answers reads from its own writes cannot drift from the
board it stands in for.

Env, all optional (each parity leg spawns a fresh server):
  FSGG_PARITY_STATUS=<name>   the column the item-Status read returns BEFORE any write (default
                              "Backlog"); "" = on the board with NO Status set (`fieldValueByName` null).
                              A Status write moves it — seed the world, then let the engine act on it.
  FSGG_PARITY_BLOCKED_BY=<v>  the `Blocked by` TEXT field's value BEFORE any write (default "" = empty,
                              `fieldValueByName` null) — .github#2079's write-time park gate reads this
                              LIVE before `release --status Blocked` / `set-field Status Blocked` may
                              proceed, exactly as the item-Status read is modelled above. A `Blocked by`
                              write moves it, on the same "answer reads from writes" rule.
  FSGG_PARITY_FAIL_STATUS=1   the item-Status read fails (a 502) — the corpus's GH_FAIL_ITEM_STATUS.
  FSGG_PARITY_DEFER_WRITE=1   the Status mutation is GraphQL-rate-limited, so Board.boardWrite queues it.
  FSGG_PARITY_MARKERS=<json>  a JSON array of pre-existing markers to seed, each
                              {"n":<issue>, "id":<comment-id>, "worker":<w>, "prev":<enc?>, "age_hours":<h?>,
                              "session":<s?>}.
                              `prev` omitted → a marker minted before #481 (no `prev=` key); `age_hours`
                              defaults to 0 (a fresh, live lease); `session` omitted → a sessionless marker
                              (a human, or a pre-#419 marker), the back-compat case case 44 exercises.
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4980}

FAIL_STATUS = os.environ.get("FSGG_PARITY_FAIL_STATUS") == "1"
DEFER_WRITE = os.environ.get("FSGG_PARITY_DEFER_WRITE") == "1"

# The full Status option set, verbatim from harness.sh (fields.json): the restore target may be any of them.
OPTIONS = [
    {"id": "opt_backlog", "name": "Backlog"},
    {"id": "opt_ready", "name": "Ready"},
    {"id": "opt_wip", "name": "In progress"},
    {"id": "opt_review", "name": "In review"},
    {"id": "opt_blocked", "name": "Blocked"},
    {"id": "opt_done", "name": "Done"},
]

_BY_ID = {o["id"]: o["name"] for o in OPTIONS}

LOCK = threading.Lock()
# The board's LIVE Status column, seeded from the env and moved by every Status write — see the module
# docstring. A single cell is enough: each leg drives one item on one board.
_STATUS = [os.environ.get("FSGG_PARITY_STATUS", "Backlog")]
# The board's LIVE `Blocked by` TEXT field, on the SAME "answer reads from writes" rule (.github#2079).
# Empty by default — the shape every pre-#2079 leg in this fixture already assumed.
_BLOCKED_BY = [os.environ.get("FSGG_PARITY_BLOCKED_BY", "")]
_STORE = {}          # issue number -> [ {id, body, user, updated_at} ]
_NEXT_ID = [900]
_WRITES = []         # [ {item, field, optionId} ] — every Status board write, in order
_GQL = {"projectsV2": 0, "fields": 0, "itemId": 0, "itemStatus": 0, "itemBlockedBy": 0, "boardScan": 0,
        "mutations": 0, "total": 0}


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _seed():
    for m in json.loads(os.environ.get("FSGG_PARITY_MARKERS", "[]")):
        prev = f" prev={m['prev']}" if m.get("prev") else ""
        # `session=` rides between the lease and the prev key, exactly as `Writes.markerBody` emits it, so
        # `Reads.sessionRe` (which anchors on `\ssession=`) parses it back. Omitted → a sessionless marker.
        session = f" session={m['session']}" if m.get("session") else ""
        body = f"<!-- fsgg:claim worker={m['worker']} lease=120{session}{prev} -->\nheld"
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
        if "items(first" in query:
            # The board SCAN (`Scan.board`). A bare `claim` (no `--force`) rides `heldElsewhere`, which scans
            # the repo's in-flight items for a live claim by THIS worker on a DIFFERENT item. An empty board
            # answers "you hold nothing else" — so the claim proceeds to the CAS, where the twin/heartbeat
            # decision (case 44) actually lives. A single page, no cursor: the scan reads one board and stops.
            _GQL["boardScan"] += 1
            return {"data": {"organization": {"projectV2": {"items": {
                "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}},
                "rateLimit": RATE}}
        if "fieldValueByName" in query:
            # `Board.itemBlockedBy`'s query (.github#2079's write-time park gate) ALSO carries
            # `fieldValueByName` — over the TEXT fragment (`{ text }`) rather than the SINGLE_SELECT one
            # (`{ name }`) — so it is discriminated by the field NAME it names, `"Blocked by"`, the same
            # way the corpus's own `gh` stub keys `fieldValueByName` queries before the plain item-id
            # lookup (harness.sh:512, the ORDER IS THE CONTRACT comment above).
            if "Blocked by" in query:
                _GQL["itemBlockedBy"] += 1
                fv = None if _BLOCKED_BY[0] == "" else {"text": _BLOCKED_BY[0]}
                return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                    {"project": {"number": 7}, "fieldValueByName": {"text": "wrong board value"}},
                    {"project": {"number": 12}, "fieldValueByName": fv}]}}}, "rateLimit": RATE}}
            _GQL["itemStatus"] += 1
            if FAIL_STATUS:
                return ("FAIL_502", None)
            fv = None if _STATUS[0] == "" else {"name": _STATUS[0]}
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"project": {"number": 7}, "fieldValueByName": {"name": "Wrong board"}},
                {"project": {"number": 12}, "fieldValueByName": fv}]}}}, "rateLimit": RATE}}
        if "projectItems" in query:
            _GQL["itemId"] += 1
            return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
                {"id": "PVTI_item", "project": {"number": 12}}]}}}, "rateLimit": RATE}}
        if "updateProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            if DEFER_WRITE:
                return {"errors": [{"message": "API rate limit exceeded for installation"}]}
            field_id = variables.get("fieldId")
            opt = variables.get("optionId")
            text = variables.get("text")
            _WRITES.append({"item": variables.get("itemId"), "field": field_id, "optionId": opt})
            # THE WRITE MOVES THE COLUMN — OR THE FIELD. Without this the fixture would report the seeded
            # value forever, and a `claim`'s own `In progress` write (or a `--blocked-by` write) would be
            # invisible to a later read (#331, and .github#2079's own re-read inside `release`).
            if field_id == "PVTF_blocked" and text is not None:
                _BLOCKED_BY[0] = text
            elif opt in _BY_ID:
                _STATUS[0] = _BY_ID[opt]
            return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
        if "clearProjectV2ItemFieldValue" in query:
            _GQL["mutations"] += 1
            # Clearing is a write too: it moves the column (or field) to "unset", the `fieldValueByName`
            # null arm above. A fixture that answered reads from its writes for one mutation and not the
            # other would be exactly as incoherent as the static cell this replaced. Routed by field, on
            # the SAME `fieldId` the set-path above keys on.
            if variables.get("fieldId") == "PVTF_blocked":
                _BLOCKED_BY[0] = ""
            else:
                _STATUS[0] = ""
            return {"data": {"clearProjectV2ItemFieldValue": {"clientMutationId": None}}}
        return None


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response
    # races the engine's pooling HttpClient and RSTs away a written response (#761). Pairs with
    # ThreadingHTTPServer below — a kept-alive connection would pin a single-threaded server.
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, code, payload):
        # A 204 carries NO body (RFC 9110 6.4.1), and a client that obeys that will not read one. Writing
        # one anyway leaves the bytes in the stream, where they become the NEXT response's status line on a
        # kept-alive connection (`{}HTTP/1.1 200 OK`) — #761. HTTP/1.0's close-per-response hid this.
        if code == 204:
            self.send_response(204)
            self.end_headers()
            return
        b = json.dumps(payload).encode()
        self.send_response(code)
        if code < 400:
            self.send_header("X-RateLimit-Resource", "core")
            self.send_header("X-RateLimit-Limit", "5000")
            self.send_header("X-RateLimit-Remaining", "4800")
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
        # The open-issue LIST — the set `reap`'s claim scan runs over (#581). Every issue the store knows
        # about, which is exactly the seeded markers' issues. Must precede the single-issue regex below:
        # `/issues` and `/issues/<n>` are one token apart.
        if re.match(r"^/repos/[^/]+/[^/]+/issues$", p):
            with LOCK:
                return self._send(200, [{"number": n, "body": ""} for n in sorted(_STORE)])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            return self._send(200, {"number": int(m.group(1)), "body": ""})
        # `reap`'s PROOF-OF-LIFE probe (#581): the item's own `item/<n>-*` PRs. An empty list is "no PR
        # found", the ONLY state in which a lapsed lease is collectable — which is what these legs want.
        if re.match(r"^/repos/[^/]+/[^/]+/pulls$", p):
            return self._send(200, [])
        # `reap`'s #1055 branch probe: no pushed `item/<n>-*` branch modeled here, so an empty list keeps
        # these lapsed leases at LeaseExpiredNoPr (collectable) rather than LivenessUnknown (refused).
        if re.match(r"^/repos/[^/]+/[^/]+/git/matching-refs/heads/item/", p):
            return self._send(200, [])
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})


def main():
    _seed()
    s = ThreadingHTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

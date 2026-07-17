#!/usr/bin/env python3
"""Case 33's one-item-per-worker board, served over HTTP for the compiled engine (#516).

The claim CAS is keyed on the ITEM, so it guarantees at most one worker per item. NOTHING guaranteed the
converse — one worker holding N items — and the cost model assumes it: a claim RESERVES A TOUCH-SET, so a
second, unattended claim is a live lock on files nobody is editing, and `batch` then refuses everything
overlapping it. Bash was fixed (#516 is CLOSED) and case 33 certifies the guard; the engine's `claim`
never got it (the CAS ran on the one item with no scan of the worker's other holds) — the ADR-0040 "half
that was never ported."

The board:
    #870 In progress  held by godwit-b49 (fresh)   src/A870/**   — the item the worker already holds
    #871 Ready        unclaimed                     src/B871/**   — the second item they try to take

so `claim FS.GG.SDD#871 --worker godwit-b49` must REFUSE (naming #870, "reserves a touch-set", #516) and
take no marker; `--force` overrides and holds two deliberately; and a DIFFERENT worker claims #871 freely
(the rule is one item per WORKER, not per repo). The store is MUTABLE so a claim can post its marker and
win its re-read, and the board mutation is accepted, so the override and different-worker paths reach a
real "claimed".

Lifted from case 33's #516 fixture (#870 held by godwit-b49, #871 unclaimed), one transport over.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4970}

BODIES = {870: "Paths: src/A870/**", 871: "Paths: src/B871/**"}
STATUS = {870: "In progress", 871: "Ready"}
TITLES = {870: "First item", 871: "Second item"}

LOCK = threading.Lock()
_NEXT_ID = [900]


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def _fresh():
    return (datetime.now(timezone.utc) - timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")


# #870 is held by godwit-b49 with a FRESH lease; #871 starts unclaimed. Mutable, so a claim posts + re-reads.
_STORE = {
    870: [{"id": 870, "user": {"login": "bot"}, "created_at": _fresh(), "updated_at": _fresh(),
           "body": "<!-- fsgg:claim worker=godwit-b49 lease=120 -->\nheld"}],
    871: [],
}


def board_items():
    nodes = [{"status": {"name": STATUS[n]}, "blockedBy": None,
              "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                          "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}} for n in sorted(BODIES)]
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}}, "rateLimit": RATE}}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"}]}]}}},
            "rateLimit": RATE}}
    if "items(first" in q:
        return board_items()
    if "projectItems" in q:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
            {"id": "PVTI_item", "project": {"number": 12}}]}}}}, "rateLimit": RATE}
    if "updateProjectV2ItemFieldValue" in q or "clearProjectV2ItemFieldValue" in q:
        return {"data": {"set": {"projectV2Item": {"id": "PVTI_item"}}}, "rateLimit": RATE}
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
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                body = ""
            with LOCK:
                cid = _NEXT_ID[0]
                _NEXT_ID[0] += 1
                _STORE.setdefault(n, []).append({"id": cid, "body": body, "user": {"login": "bot"},
                                                 "created_at": _now(), "updated_at": _now()})
            return self._send(201, {"id": cid, "body": body, "updated_at": _now()})
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/assignees$", p):
            return self._send(201, {})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            with LOCK:
                return self._send(200, list(_STORE.get(int(m.group(1)), [])))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES[n]}) if n in BODIES else self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4970, "limit": 5000}}})
        # THE OFF-BOARD OPEN-ISSUE SCAN (case 25). `batch`/`next`/`take` reserve off-board claims too, so
        # every scheduling call fetches this list (bash's `active_claims` arm B). This world's claims are
        # all ON the board, so there is nothing off-board to reserve — an empty list is the honest answer.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, [])

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

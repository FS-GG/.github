#!/usr/bin/env python3
"""The corpus's parallel-work board (`board-pw`), served over HTTP for the compiled engine.

This is the parity fixture. The shell corpus drives `bash scripts/fsgg-coord batch` against this exact
board through its `gh` stub and CERTIFIES the answer (case 22): `batch --repo sdd --json` →
`["FS.GG.SDD#70","FS.GG.SDD#74"]`, skipping #71 (overlaps the in-flight #42), #72 (no touch-set), #73
(overlaps batch-member #70), and #75 (`Class: decision`, #1887). This server presents that board with
the decision-class negative control added — the same original items, the same seeded
touch-sets, the same pre-existing claims — so the ENGINE, over HTTP with no bash in sight, can be held to
that same certified answer. Any divergence is a real gap in the drop-in.

The board and its seeds are lifted verbatim from `tests/fsgg-coord/lib/harness.sh` (board-pw + the
`seed_issue` lines + the #42/#43 claims), so the two fixtures cannot drift: this is that world, one
transport over.
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# FSGG_PARITY_MALFORMED_COMMENTS=<item> makes that item's claim-marker read return non-JSON bytes with
# HTTP 200 — the corpus's `GH_ISSUE_LIST_MALFORMED` (#461), one transport over: a truncated page / proxy
# error body that a naive client folds into an EMPTY claim set. The engine must fail CLOSED on it.
MALFORMED_COMMENTS = os.environ.get("FSGG_PARITY_MALFORMED_COMMENTS")

RATE = {"cost": 1, "remaining": 4980}

# The seeded touch-sets, verbatim from harness.sh.
BODIES = {
    42: "Paths: src/Audio/**, tests/Audio/**",
    43: "Paths: src/Legacy/**",
    60: "Paths: src/Orphan/**",
    70: "Paths: src/Scene/**, tests/Scene/**",
    71: "Paths: src/Audio/Mixer/**",
    72: "",  # no touch-set declared
    73: "Paths: src/Scene/Sub/**",
    74: "Paths: docs/adr/**",
    # No human/decision sentinel: the item's own Class body line is the authority #1887 requires the
    # scheduler to read. Its neutral title also prevents the compatibility title prefix from proving it.
    75: "Paths: docs/architecture/**\n\nClass: decision",
}

# Board Status per item (In progress for the claimed/orphaned ones, Ready for the candidates).
STATUS = {42: "In progress", 43: "In progress", 60: "In progress",
          70: "Ready", 71: "Ready", 72: "Ready", 73: "Ready", 74: "Ready", 75: "Ready"}

TITLES = {42: "Audio mixer", 43: "Legacy port", 60: "Nobody claimed me", 70: "Scene graph",
          71: "Mixer tweak", 72: "No touch-set declared", 73: "Scene subtree", 74: "ADR housekeeping"}
TITLES[75] = "Architecture delivery choice"


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


# Pre-existing claims, and a MUTABLE store so `take` can post its own marker and win its re-read:
# #42 held by finch-a3f (FRESH → live, reserves src/Audio), #43 by ghost-000 (5h old → STALE, filtered
# out), #60 no marker. The store is seeded lazily (the fresh/stale timestamps are computed at first read).
LOCK = threading.Lock()
_STORE = {}
_NEXT_ID = [900]
_SEEDED = [False]


def _seed():
    if _SEEDED[0]:
        return
    _STORE[42] = [{"id": 801, "body": "<!-- fsgg:claim worker=finch-a3f lease=120 -->\nheld",
                   "user": {"login": "EHotwagner"}, "updated_at": _now(0)}]
    _STORE[43] = [{"id": 802, "body": "<!-- fsgg:claim worker=ghost-000 lease=120 -->\ndead",
                   "user": {"login": "EHotwagner"}, "updated_at": _now(-5)}]
    _SEEDED[0] = True


def comments(n):
    with LOCK:
        _seed()
        return list(_STORE.get(n, []))


def post_comment(n, body):
    with LOCK:
        _seed()
        cid = _NEXT_ID[0]
        _NEXT_ID[0] += 1
        _STORE.setdefault(n, []).append({"id": cid, "body": body, "updated_at": _now(0)})
        return cid


def board_items():
    nodes = []
    for n in sorted(BODIES):
        nodes.append({
            "status": {"name": STATUS[n]},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}},
        })
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}},
        "rateLimit": RATE}}


def graphql(query):
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "items(first" in query:
        return board_items()
    if "projectItems" in query:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
            {"id": "PVTI_item", "project": {"number": 12}}]}}}}, "rateLimit": RATE}
    if "updateProjectV2ItemFieldValue" in query or "clearProjectV2ItemFieldValue" in query:
        return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
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
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled query {q[:60]}"}]})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            body = ""
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                pass
            cid = post_comment(int(m.group(1)), body)
            return self._send(201, {"id": cid, "body": body, "updated_at": _now(0)})
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/assignees$", p)
        if m:
            return self._send(201, {})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        # THE OFF-BOARD CLAIM SCAN (case 25): `who` scans the repo's OPEN ISSUES, not just the board. On
        # THIS board every claim is also a board item, so the list is the same seven issues — but it is
        # REPO-SCOPED: FS.GG.Rendering has none here, so its scan legitimately finds nothing (#461's
        # positive control). No `pull_request` key — these are issues, and `openIssues` filters PRs out.
        m = re.match(r"^/repos/([^/]+)/([^/]+)/issues/?$", p)
        if m:
            if m.group(2) != "FS.GG.SDD":
                return self._send(200, [])
            return self._send(200, [{"number": n, "title": TITLES[n], "state": "open",
                                     "body": BODIES[n]} for n in sorted(BODIES)])
        # Proof of life (#581): `who` probes a STALE row's own `item/<n>-*` PR. There are none on this
        # board (#43's stale claim is a genuinely dead one → a bare STALE), so the list is empty.
        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            if MALFORMED_COMMENTS and n == int(MALFORMED_COMMENTS):
                # A 200 with a NON-JSON body — the failed read wearing an empty set's clothes (#461).
                body = b"<html><body>502 Bad Gateway</body></html>"
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
                return
            return self._send(200, comments(n))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")}) if n in BODIES else self._send(404, {"message": "Not Found"})
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

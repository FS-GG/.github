#!/usr/bin/env python3
"""Case 32's completed-item board, served over HTTP for the compiled engine (#533).

`done --flip` verifies the merge, sets Status Done, and rolls the epic up — and, before #533, it never
touched the CLAIM MARKER. `release` was the only path that dropped the marker, and `release` REWRITES
Status, so running it on an item you just stamped Done clobbers the stamp you just earned. So on the
success path nothing dropped the lock: it stayed live for the rest of the 120m lease, and a live marker's
`Paths:` keep reserving its touch-set. It bites hardest exactly where the protocol is working — the items
most likely to overlap a just-finished one are its own FOLLOW-UP findings, filed BECAUSE you were standing
in those files — so the recipe reliably produced an item its own author had locked out.

Bash carries the fix (case 32, #533 CLOSED); the engine's `done` never dropped the marker (the ADR-0040
"half that was never ported"). This fixture is case 32's board, one transport over.

The board:
    #42  In progress   held by $DONE_HOLDER (fresh)   src/Done42/**   — the finished item
and #42 is CLOSED on the issue record by a MERGED PR #7 (so `done` reaches a Green ClosedByPullRequest
verdict). The store is MUTABLE so `done` can DELETE the marker it drops. $DONE_HOLDER selects who holds
the claim: `vole-533` (our own worker → the marker is dropped) or `other-999` (a stranger → the marker is
LEFT, because deleting a claim that is not ours is `reap`'s job, not `done`'s).
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4960}

ITEM = 42
HOLDER = os.environ.get("DONE_HOLDER", "vole-533")
BODY = "Paths: src/Done42/**"
CLOSING_PR = 7  # a MERGED PR that closed #42 — the Green ClosedByPullRequest path.

LOCK = threading.Lock()
_NEXT_ID = [900]


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _fresh():
    return (datetime.now(timezone.utc) - timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")


# #42 is held by $HOLDER with a FRESH lease. Its comment id IS the lock; `done` deletes it by that id.
_STORE = {
    ITEM: [{"id": 842, "user": {"login": "bot"}, "created_at": _fresh(), "updated_at": _fresh(),
            "body": f"<!-- fsgg:claim worker={HOLDER} lease=120 -->\nheld"}],
}


def done_facts():
    """The one query behind `Done.facts`: #42 is CLOSED, closed by a MERGED PR #7, no children."""
    return {"data": {"repository": {"issue": {
        "number": ITEM, "state": "CLOSED",
        "closedByPullRequestsReferences": {"nodes": [{"number": CLOSING_PR, "merged": True}]},
        "timelineItems": {"nodes": [{"closer": {"__typename": "PullRequest", "number": CLOSING_PR}}]},
        "subIssues": {"totalCount": 0, "nodes": []},
        "projectItems": {"nodes": [{"project": {"number": 12}, "status": {"name": "In progress"}}]},
        "parent": None,
    }}}, "rateLimit": RATE}


def graphql(q):
    # Order matters: the done-facts query ALSO contains "projectItems", so match its unique token first.
    if "closedByPullRequestsReferences" in q:
        return done_facts()
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"},
                         {"id": "opt_done", "name": "Done"}]}]}}}, "rateLimit": RATE}}
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
        # A 204 carries NO body (RFC 9110 6.4.1), and a client that obeys that will not read one. Writing
        # one anyway leaves the bytes in the stream, where they become the NEXT response's status line on a
        # kept-alive connection (`{}HTTP/1.1 200 OK`) — #761. HTTP/1.0's close-per-response hid this.
        if code == 204:
            self.send_response(204)
            self.end_headers()
            return
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
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_DELETE(self):
        p = self.path.split("?", 1)[0]
        # The marker drop: DELETE /repos/{o}/{r}/issues/comments/{id}. Remove it from every issue's store.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            cid = int(m.group(1))
            with LOCK:
                for n in _STORE:
                    _STORE[n] = [c for c in _STORE[n] if c.get("id") != cid]
            return self._send(204, {})
        self._send(500, {"message": f"unhandled DELETE {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            with LOCK:
                return self._send(200, list(_STORE.get(int(m.group(1)), [])))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODY}) if n == ITEM else self._send(404, {"message": "Not Found"})
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

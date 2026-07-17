#!/usr/bin/env python3
"""Case 26's `reap` — expired-lease-vs-open-PR (#581) — served over HTTP for the compiled engine.

`reap` is the command the coordination protocol was missing at BOTH ends of a claim's life (case 25/26):
a lease is EVIDENCE of abandonment, never PROOF. Its false positive is systematic — work that simply
outlasts its lease — and the bash reaper that broke a lock on expiry alone collected the claims of
workers who were visibly still working, TWICE. #581's fix: an open PR on the item's own `item/<n>-*`
branch is the worktree protocol's own server-side artifact, so `reap` LOOKS for it and REFUSES when one
is open.

The world (lifted from the corpus's `seed_offboard_world` / case 26): ONE off-board claim.
    216  FS.GG.Rendering  worker ghost-222  lease=120  updated 3h ago  → STALE (its lease lapsed)
         · body: `Paths: src/Dead/**`
         · #216 is NOT on the board (an off-board claim), so `reap` finds it by scanning OPEN ISSUES, and
           its post-reap "restore the column" resolves to `Ok None` → "not on board (nothing to reset)".

The corpus (cases 26 + 25) certifies:
    reap --repo …           (dry run)  → "would reap  FS.GG.Rendering#216  worker ghost-222"; NOTHING deleted.
    reap --repo … --apply   PR OPEN    → REFUSING, names the PR (#433); the marker SURVIVES (leg reaped live
                                          work twice — the one that matters).
    reap --repo … --apply   NO PR      → "reaped  FS.GG.Rendering#216  worker ghost-222"; the marker is GONE,
                                          and it does NOT claim a board reset it never performed.

Whether the item's PR is open is this fixture's ONE toggle: `REAP_LIVE_PR=216:433` in the environment
makes `/pulls?state=open` carry an OPEN PR #433 on `item/216-live-work`; unset, there is no open PR and
the claim is genuinely dead.

Writes MUTATE this process — a reap that fired would DELETE comment 880 — so the harness reads `/_deletes`
back to prove the refusal deleted nothing and the collect deleted exactly the marker.
"""

import json
import os
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.Rendering"

# The single off-board stale claim. #216, ghost-222, last beat 3h ago (lease 120m → lapsed).
STALE = {"number": 216, "marker_id": 880, "worker": "ghost-222", "lease": 120,
         "body": "Paths: src/Dead/**"}

# REAP_LIVE_PR="216:433" → item 216 has an OPEN PR #433 on item/216-* (proof of life). Unset → dead.
LIVE_PR = {}
_env = os.environ.get("REAP_LIVE_PR", "").strip()
if _env:
    _item, _pr = _env.split(":", 1)
    LIVE_PR[int(_item)] = int(_pr)

RATE = {"cost": 1, "remaining": 4977}
LOCK = threading.Lock()
_DELETES = []  # comment ids this process was asked to DELETE (the reap writes).


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def open_issues():
    # The claim-scan candidate set: one OPEN issue carrying the stale off-board claim. No `pull_request`
    # key — a PR is an issue in REST, and `openIssues` filters those out; an issue is the real thing.
    return [{"number": STALE["number"], "title": "Long build, lapsed lease", "state": "open",
             "body": STALE["body"]}]


def comments(n):
    if n != STALE["number"]:
        return []
    old = _iso(3)  # 3h ago — well past the 120-minute lease.
    return [{"id": STALE["marker_id"],
             "body": f"<!-- fsgg:claim worker={STALE['worker']} lease={STALE['lease']} -->\nheld",
             "user": {"login": "EHotwagner"}, "created_at": old, "updated_at": old}]


def open_pulls():
    pr = LIVE_PR.get(STALE["number"])
    if not pr:
        return []
    return [{"number": pr, "state": "open",
             "head": {"ref": f"item/{STALE['number']}-live-work"}}]


def graphql(query):
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    # `itemId`'s resolver (the post-reap column restore): the issue EXISTS but sits on NO board item —
    # an off-board claim. Empty `projectItems.nodes` → `Ok None` → reap prints "not on board".
    if "projectItems" in query:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": []}}}}, "rateLimit": RATE}
    if "items(first" in query:
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}}, "rateLimit": RATE}}
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

    def do_DELETE(self):
        # The reap write: delete the stale marker comment. Record it so the harness can prove a refusal
        # deleted NOTHING and a collect deleted EXACTLY the marker. 204 No Content, as GitHub returns.
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", p)
        if m:
            with LOCK:
                _DELETES.append(int(m.group(1)))
            self.send_response(204)
            self.end_headers()
            return
        self._send(500, {"message": f"unhandled DELETE {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p == "/_deletes":
            with LOCK:
                return self._send(200, list(_DELETES))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, open_issues())
        if re.match(r"^/repos/[^/]+/[^/]+/pulls/?$", p):
            return self._send(200, open_pulls())
        if re.match(r"^/repos/[^/]+/[^/]+/git/matching-refs/heads/item/", p):
            # #1055: no pushed item/<n>-* branch is modeled here → prAlive's branch probe finds none →
            # LeaseExpiredNoPr (the dead claim these legs are about), not LivenessUnknown.
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n == STALE["number"]:
                return self._send(200, {"number": n, "body": STALE["body"]})
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

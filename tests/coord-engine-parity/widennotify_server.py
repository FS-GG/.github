#!/usr/bin/env python3
"""Case 34's `widen` collision-DETECT-and-notify half (#353), served over HTTP for the compiled engine.

The read-only `overlap` command (#809) ported the repo-scoped collision COMPUTATION. This is the write
half ADR-0021 named — "a worker that must widen its touch-set re-declares AND re-checks overlap before
continuing" — and the part a worker cannot do alone: after the widen lands, re-check the NEW touch-set
against the live claims in THIS item's repo, and NOTIFY each worker it now collides with, on their own
issue. The corpus (case 34) certifies:

    widen SDD#401 --paths 'scripts/fsgg-coord'  -> DISJOINT — the widened token names a file in THIS repo;
                                                   the same string in Rendering#402 is a different file, so
                                                   the cross-repo bystander is NOT a collision and is left
                                                   UNCOMMENTED (its holder is never pestered). exit 0.
    widen SDD#401 --paths 'src/Scene/**'         -> OVERLAP — a REAL same-repo neighbour (#403, sdd-sib)
                                                   now collides; the engine notifies sdd-sib on #403 and
                                                   exits non-zero. Scoping narrowed the set, not the test.

The board (three items across two repos):
    401 SDD      InProg   scripts/fsgg-coord   claim kite-t01   — the widen TARGET (its own holder widens it)
    402 Rendering InProg  scripts/fsgg-coord   claim render-x1  — the cross-repo phantom (same bare token)
    403 SDD      InProg   src/Scene/**         claim sdd-sib    — a real SDD neighbour, the positive control

DISPOSED ON THE RECORD (ADR-0040 §5):
  - The engine's `widen` requires the widener to HOLD the lock (#706) — bash's `widen` does not. So the
    engine's #401 carries a kite-t01 claim the corpus fixture omits; this is an engine STRENGTHENING, not
    a change to the collision-notify property under test. verifyHeld's fail-closed refusal is proven elsewhere.
  - The OVERLAP exit is the engine's ExitContended=6; the corpus's `assert_fails` is the PROPERTY (a real
    collision exits NON-ZERO), not bash's literal 1.

Writes MUTATE this process (the PATCH rewrites a body; the notify POSTs a comment), so a widen re-check
that pestered the wrong item would leave a comment behind. The harness reads `/_posts` back to assert the
cross-repo bystander #402 got ZERO comments and the same-repo neighbour #403 got exactly one.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4975}

REPO = {401: "FS-GG/FS.GG.SDD", 402: "FS-GG/FS.GG.Rendering", 403: "FS-GG/FS.GG.SDD"}
STATUS = {401: "In progress", 402: "In progress", 403: "In progress"}
TITLES = {401: "SDD widen target", 402: "Rendering bystander", 403: "SDD sibling"}
BODIES = {401: "Paths: scripts/fsgg-coord",
          402: "Paths: scripts/fsgg-coord",
          403: "Paths: src/Scene/**"}
# Live claim markers (a claim marker IS a comment). #401's holder is the worker that widens it (#706).
CLAIMS = {401: "kite-t01", 402: "render-x1", 403: "sdd-sib"}

LOCK = threading.Lock()
_POSTS = {}  # issue number -> count of comment POSTs this process received (the notify writes).


def _now(offset_hours=0):
    return (datetime.now(timezone.utc) + timedelta(hours=offset_hours)).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    w = CLAIMS.get(n)
    if not w:
        return []
    return [{"id": 8000 + n, "body": f"<!-- fsgg:claim worker={w} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "updated_at": _now(0)}]


def board_items():
    nodes = []
    for n in sorted(REPO):
        nodes.append({
            "status": {"name": STATUS[n]},
            "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": REPO[n]}},
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
                q = json.loads(raw).get("query", "")
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "bad body"}]})
            a = graphql(q)
            return self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})
        # The notify write — a comment POST onto the colliding worker's own issue. Count it per issue, so
        # the harness can prove the cross-repo bystander was left alone.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            with LOCK:
                _POSTS[n] = _POSTS.get(n, 0) + 1
            return self._send(201, {"id": 9000 + n})
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_PATCH(self):
        # The widen write itself (`patchBody`): rewrite the issue body. Accept it; the engine's re-check
        # compares the touch-set it REWROTE, not what we store, so we need not persist the new body.
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")})
        self._send(500, {"message": f"unhandled PATCH {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if p == "/_posts":
            with LOCK:
                return self._send(200, dict(_POSTS))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODIES.get(n, "")}) if n in BODIES \
                else self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})
        self._send(500, {"message": f"unhandled GET {p}"})


def main():
    s = HTTPServer(("127.0.0.1", 0), H)
    print(s.server_address[1], flush=True)
    s.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

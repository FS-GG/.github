#!/usr/bin/env python3
"""Case 43's kit-digest obligation (#469/#563/#588), served over HTTP for the compiled engine.

`widen` is the cheap moment to learn that editing a kit source obliges a re-digest of
`registry/repos.lock` (ADR-0019) — rather than from a red `main` after the merge, which is how #469 was
found. The obligation is OBSERVED off the tree (`FSGG_KIT_ROOT`), not inferred from what was declared
(#563 pinned the fail-open where declaring `registry/repos.yml` silenced a still-stale lock). That half
is a pure filesystem read the harness drives by standing a throwaway kit tree up and editing it; this
fixture exists only so the `widen` it rides can LAND — the engine's `widen` requires the widener to HOLD
the lock (#706), reads the body, PATCHes it, and re-checks overlap, all over HTTP.

The world — one held item, no neighbour, so the widen lands and the re-check is DISJOINT:

    74  FS.GG.SDD  In progress  `Paths: scripts/fsgg-coord`  claim kite-469 (fresh)  — the widen TARGET

There is no second SDD claim, so `widen`'s #353 re-check clears (DISJOINT, exit 0); the KIT DIGEST /
SKILL ROOTS warnings the corpus greps for are emitted from the tree by the engine, independent of this
transport. The PATCH is accepted but not persisted — the re-check compares the touch-set it REWROTE.
"""

import json
import re
import sys
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4977}
OWNER = "FS-GG"
REPO = "FS.GG.SDD"

# The single held item. Its holder is the worker that widens it (#706).
BODY = "Paths: scripts/fsgg-coord"
HOLDER = "kite-469"


def _now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    if n != 74:
        return []
    ts = _now()
    return [{"id": 8074, "body": f"<!-- fsgg:claim worker={HOLDER} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "created_at": ts, "updated_at": ts}]


def board_items():
    nodes = [{
        "status": {"name": "In progress"},
        "blockedBy": None,
        "content": {"__typename": "Issue", "number": 74, "title": "widen target", "state": "OPEN",
                    "repository": {"nameWithOwner": f"{OWNER}/{REPO}"}},
    }]
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
        self._send(500, {"message": f"unhandled POST {p}"})

    def do_PATCH(self):
        # The widen write itself: accept the body rewrite (not persisted — the re-check uses what it rewrote).
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            return self._send(200, {"number": int(m.group(1)), "body": BODY})
        self._send(500, {"message": f"unhandled PATCH {p}"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(int(m.group(1))))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            return self._send(200, {"number": n, "body": BODY}) if n == 74 \
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

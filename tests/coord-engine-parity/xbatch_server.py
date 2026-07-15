#!/usr/bin/env python3
"""Case 35's cross-repo board (`board-xbatch`), served over HTTP for the compiled engine.

The corpus (`tests/fsgg-coord/cases/35-xrepo-batch-312.sh`, #312) certifies that `batch`, scheduling the
WHOLE board (no `--repo`), qualifies its touch-set comparison BY REPO — because `Paths:` tokens are
repo-relative, so `src/Physics/**` held in one repo names different files than `src/Physics/**` Ready in
another. The old code flattened every live claim's tokens into one bare list, so a token held in one repo
phantom-collided with the same bare token Ready in ANOTHER, and the scheduler passed over an item nothing
was actually holding.

The board:
    #420 Templates   Ready        src/Physics/**          candidate; only a CROSS-repo namesake holds it
    #421 Governance  Ready        src/Physics/**          candidate in a THIRD repo, SAME bare token
    #422 Audio       Ready        src/Physics/Solver.fs   REAL same-repo overlap of the in-flight #424
    #425 Templates   Ready        src/Physics/Gun.fs      REAL same-repo overlap of the batch-mate #420
    #423 Game        In progress  src/Physics/**          cross-repo phantom (its own repo, no candidate)
    #424 Audio       In progress  src/Physics/**          the genuine same-repo neighbour #422 clashes with

so `batch --json` over the whole board is `["FS.GG.Templates#420","FS.GG.Governance#421"]`: the two
cross-repo namesakes ride together (only a phantom holds their token), while the two GENUINE same-repo
overlaps (#422 vs in-flight #424, #425 vs batch-mate #420) are still dropped — scoping narrowed the
comparison, it did not blind the check. This serves that exact board, its touch-sets, and the two live
claims (#423 game-x1, #424 audio-n1) so the ENGINE, over HTTP with no bash in sight, can be held to
#312's PROPERTY: a candidate is never dropped for a cross-repo phantom, and a real same-repo overlap
still is.

The board and its seeds are lifted verbatim from case 35, one transport over, so the two cannot drift.
"""

import json
import re
import sys
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4970}

REPO = {420: "FS.GG.Templates", 421: "FS.GG.Governance", 422: "FS.GG.Audio",
        425: "FS.GG.Templates", 423: "FS.GG.Game", 424: "FS.GG.Audio"}
STATUS = {420: "Ready", 421: "Ready", 422: "Ready", 425: "Ready",
          423: "In progress", 424: "In progress"}
BODIES = {420: "Paths: src/Physics/**", 421: "Paths: src/Physics/**",
          422: "Paths: src/Physics/Solver.fs", 425: "Paths: src/Physics/Gun.fs",
          423: "Paths: src/Physics/**", 424: "Paths: src/Physics/**"}
TITLES = {420: "Templates A", 421: "Governance B", 422: "Audio control",
          425: "Templates mate", 423: "Game phantom", 424: "Audio neighbour"}
# The two in-flight items carry a FRESH claim marker (a claim marker IS a comment).
CLAIMS = {423: "game-x1", 424: "audio-n1"}


def _fresh():
    return (datetime.now(timezone.utc) - timedelta(minutes=5)).strftime("%Y-%m-%dT%H:%M:%SZ")


def board_items():
    nodes = []
    for n in sorted(REPO):
        nodes.append({
            "status": {"name": STATUS[n]}, "phase": None, "blockedBy": None,
            "content": {"__typename": "Issue", "number": n, "title": TITLES[n], "state": "OPEN",
                        "repository": {"nameWithOwner": f"FS-GG/{REPO[n]}"}},
        })
    return {"data": {"organization": {"projectV2": {"items": {
        "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}}, "rateLimit": RATE}}


def graphql(q):
    if "projectsV2" in q:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in q:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "o1", "name": "Ready"}, {"id": "o2", "name": "In progress"}]}]}}},
            "rateLimit": RATE}}
    if "items(first" in q:
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
        n = int(self.headers.get("Content-Length", 0))
        try:
            q = json.loads(self.rfile.read(n).decode()).get("query", "")
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(q)
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {q[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p)
        if m:
            n = int(m.group(1))
            if n in CLAIMS:
                ts = _fresh()
                return self._send(200, [{"id": 8000 + n, "user": {"login": "EHotwagner"},
                                         "created_at": ts, "updated_at": ts,
                                         "body": f"<!-- fsgg:claim worker={CLAIMS[n]} lease=120 -->\nheld"}])
            return self._send(200, [])
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            n = int(m.group(1))
            if n in BODIES:
                return self._send(200, {"number": n, "body": BODIES[n]})
            return self._send(404, {"message": "Not Found"})
        if p.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4970, "limit": 5000}}})
        # THE OFF-BOARD OPEN-ISSUE SCAN (case 25). `batch`/`next`/`take` reserve off-board claims too, so
        # every scheduling call fetches this list (bash's `active_claims` arm B) — per repo, since the touch-set
        # comparison is repo-scoped (#312). This world's claims are all ON the board, so there is nothing
        # off-board to reserve — an empty list is the honest answer.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", p):
            return self._send(200, [])

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

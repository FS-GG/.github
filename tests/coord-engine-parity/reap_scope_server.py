#!/usr/bin/env python3
"""Case 13's #480 leg for `reap` — the DESTRUCTIVE worker command scopes to the checkout you are STANDING IN.

`reap --apply` is the one worker command that DELETES another worker's state — it breaks their claim
marker. So an org-wide default is the worst possible one here: a janitor run from a `.github` checkout
would collect claims in every repo it was never pointed at. Like its siblings (`next`/`take`/`batch`/
`who`), a bare `reap` must take the repo of the checkout you are standing in — resolved FREE and offline
from `git config remote.origin.url` (never `gh repo view`) — and consider ONLY that repo's claims. An
explicit `--repo` spells the scope out and wins; OUTSIDE a checkout there is no repo, so `reap` REFUSES
(`--repo required`) rather than fall back to the org-wide scan that would delete across five repos.

The corpus (case 13, line 54) asserts this on the DRY RUN — the point is WHICH claims it considers, not
that it deletes: a bare `reap` from an FS.GG.SDD checkout must NOT name a Templates/Rendering/Game/… claim.

This fixture is a MULTI-REPO world so a leak is visible at the transport. Two repos, each carrying one
DEAD off-board stale claim (no open PR → reapable):
    FS.GG.SDD#301        worker mole-s1  marker 701  lease 120m, last beat 3h ago  → STALE, no PR
    FS.GG.Rendering#302  worker mole-r1  marker 702  lease 120m, last beat 3h ago  → STALE, no PR
Both are off the board (empty `projectItems`), so the post-reap column restore reports "not on board".

`reap` fetches ONE repo's open issues — the scoped one — so the scope is proven two ways: the dry-run
line names the checkout's repo (`would reap  FS.GG.SDD#301  worker mole-s1`), and the `/_requests`
ledger shows the fixture was asked for the checkout's `/issues`, NEVER the other repo's. bash counts `gh`;
this counts the repo each `/issues` request named, one transport under.
"""

import json
import re
import sys
import threading
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"

# One DEAD stale claim per repo — keyed by the REPO NAME the request names, so a bare reap from a given
# checkout sees ONLY its own repo's claim. Both leases lapsed (3h ago, lease 120m); neither has an open PR.
CLAIMS = {
    "FS.GG.SDD": {"number": 301, "marker_id": 701, "worker": "mole-s1", "lease": 120,
                  "body": "Paths: src/Sdd/**"},
    "FS.GG.Rendering": {"number": 302, "marker_id": 702, "worker": "mole-r1", "lease": 120,
                        "body": "Paths: src/Rnd/**"},
}

RATE = {"cost": 1, "remaining": 4977}
LOCK = threading.Lock()
_REQUESTS = []  # the repo NAME of every /issues request — the scope, counted one transport under.
_DELETES = []   # comment ids DELETEd (a dry run deletes nothing).


def _iso(hours_ago):
    return (datetime.now(timezone.utc) - timedelta(hours=hours_ago)).strftime("%Y-%m-%dT%H:%M:%SZ")


def open_issues(repo):
    claim = CLAIMS.get(repo)
    if not claim:
        return []
    return [{"number": claim["number"], "title": "Long build, lapsed lease", "state": "open",
             "body": claim["body"]}]


def comments(repo, n):
    claim = CLAIMS.get(repo)
    if not claim or n != claim["number"]:
        return []
    old = _iso(3)  # 3h ago — well past the 120-minute lease.
    return [{"id": claim["marker_id"],
             "body": f"<!-- fsgg:claim worker={claim['worker']} lease={claim['lease']} -->\nheld",
             "user": {"login": "EHotwagner"}, "created_at": old, "updated_at": old}]


def graphql(query):
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    # Off-board: the issue exists but sits on NO board item → `Ok None` → reap prints "not on board".
    if "projectItems" in query:
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": []}}}}, "rateLimit": RATE}
    if "items(first" in query:
        return {"data": {"organization": {"projectV2": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": []}}}, "rateLimit": RATE}}
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

    def do_DELETE(self):
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
        if p == "/_requests":
            with LOCK:
                return self._send(200, list(_REQUESTS))
        if p == "/_deletes":
            with LOCK:
                return self._send(200, list(_DELETES))
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/(\d+)/comments$", p)
        if m:
            return self._send(200, comments(m.group(1), int(m.group(2))))
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/?$", p)
        if m:
            # RECORD THE SCOPE: which repo's issues were asked for. A bare reap must name exactly the
            # checkout's repo here, never the other — the org-wide default #480 deletes.
            with LOCK:
                _REQUESTS.append(m.group(1))
            return self._send(200, open_issues(m.group(1)))
        m = re.match(r"^/repos/[^/]+/([^/]+)/pulls/?$", p)
        if m:
            return self._send(200, [])  # no open PR anywhere → both claims are genuinely dead.
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/(\d+)$", p)
        if m:
            repo, n = m.group(1), int(m.group(2))
            claim = CLAIMS.get(repo)
            if claim and n == claim["number"]:
                return self._send(200, {"number": n, "body": claim["body"]})
            return self._send(404, {"message": "Not Found"})
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

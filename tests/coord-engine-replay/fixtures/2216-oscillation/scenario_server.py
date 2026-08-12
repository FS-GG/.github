#!/usr/bin/env python3
"""The HERMETIC source this fixture's `transcript.json` was recorded from (`.github#2401`).

`.github#2216` tabulated three live `reconcile --json` runs against the same board facts (issue OPEN,
row `In review`, implementation PR #2270 OPEN and unclaimed, no live claim marker) that returned
different verdicts — `Status=Ready` twice, a no-op once. This server does not replay that literal
capture: the live board it was taken from is not something a hermetic suite can depend on staying put,
and by the time this merges #2216's own row may have moved. Instead it models the SAME SHAPE — one
unclaimed, OPEN, `In review` row with a genuinely open implementation PR on its own branch — as a small,
deterministic, checked-in board, small enough to read in one sitting (contrast the 47 boards in
`tests/coord-engine-parity/`, some 43KB).

Run against TODAY's compiled engine, it reproduces the WRONG half of #2216's tabulated verdict
deterministically, not intermittently: `reconcile --json` proposes `Status=Ready` for a row with a real
open PR on it. Reading `src/FS.GG.Coord.GitHub/Scan.fs` around the `itemPr` probe (the block discussed
in the comment beginning "AND THE `| _ -> ()` BELOW IS A KNOWN FAIL-OPEN WITH A NEW CONSUMER —
.github#1924") shows why: the probe that would tell `LifecycleProjection.project` about this PR is only
attempted for a markerless `Ready`/`Backlog` row, or a `Blocked` one whose blockers just cleared — never
for a markerless `In review` row, which is exactly this fixture's shape. `Item.ItemPr` therefore reads
`None`, `LifecycleProjection.project`'s open-PR rule (`elif ... pr.Open ...  -> Project(InReview, ...)`)
never fires, and the projection falls through to `Ready`. `.github#1924` (closed) already named the same
`ItemPr` collapse for a different consumer (`BLOCKER-CLEARED`); this is the `In review` sibling, and the
evidence lives on `.github#2384`, the tracked umbrella for this class — see this fixture's
`manifest.json` and the PR that added it for the pointer.

REGENERATING THIS FIXTURE

    python3 tests/coord-engine-replay/fixtures/2216-oscillation/scenario_server.py &
    SRV_PID=$!; sleep 0.3
    PORT=$(curl -s ...)   # or read the server's first stdout line directly
    GITHUB_TOKEN=fixture-token python3 scripts/record-board-fixture.py --repo FS.GG.SDD \\
        --upstream "http://127.0.0.1:$PORT" \\
        --out tests/coord-engine-replay/fixtures/2216-oscillation
    kill $SRV_PID

Capture overwrites `expected/reconcile.json` with whatever the CURRENT engine actually outputs — the
committed one is deliberately NOT that. It asserts the CORRECT no-op verdict (an unclaimed `In review`
row with a genuinely open PR should stay `In review`), so re-running the recipe above requires manually
restoring `expected/reconcile.json` to `[]` afterward. That mismatch, preserved on purpose, is this
fixture's entire point (see `manifest.json`'s `expectFailure`).
"""

import json
import re
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOCK = threading.Lock()

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
ITEM = 500
PR = 777
BRANCH = f"item/{ITEM}-the-work"

ISSUE_BODY = "A row with a genuinely open, unclaimed implementation PR.\n\nPaths: src/Probe/**"

RATE_LIMIT = {"cost": 1, "remaining": 4999}


def now_iso():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def route_receipt_comment():
    receipt = {
        "schema": "fsgg.coord.delivery-route/v1",
        "subject": f"{OWNER}/{REPO}#{ITEM}",
        "subjectRevision": __import__("hashlib").sha256(ISSUE_BODY.encode()).hexdigest(),
        "route": "lightweight",
        "agent": "scenario-fixture",
        "timestamp": "2026-01-01T00:00:00Z",
        "reasonCodes": ["fixture"],
        "rationale": "2216-oscillation scenario fixture route receipt.",
        "declaredImpacts": ["internal"],
        "observedFacts": ["localized"],
        "sddWorkId": None,
        "specHome": None,
        "requiredGates": [],
    }
    return {
        "id": 800000 + ITEM,
        "body": "<!-- fsgg:delivery-route/v1 -->\n" + json.dumps(receipt, separators=(",", ":")),
        "user": {"login": "fixture"},
        "created_at": "2026-01-01T00:00:00Z",
        "updated_at": "2026-01-01T00:00:00Z",
    }


def project_fields():
    return {
        "data": {
            "organization": {
                "projectV2": {
                    "fields": {
                        "nodes": [
                            {
                                "id": "PVTSSF_status",
                                "name": "Status",
                                "dataType": "SINGLE_SELECT",
                                "options": [
                                    {"id": "opt_ready", "name": "Ready"},
                                    {"id": "opt_wip", "name": "In progress"},
                                    {"id": "opt_done", "name": "Done"},
                                    {"id": "opt_blocked", "name": "Blocked"},
                                    {"id": "opt_review", "name": "In review"},
                                    {"id": "opt_backlog", "name": "Backlog"},
                                ],
                            },
                            {"id": "PVTSSF_class", "name": "Class", "dataType": "SINGLE_SELECT",
                             "options": [{"id": "opt_hardening", "name": "hardening"}]},
                            {"id": "PVTSSF_phase", "name": "Phase", "dataType": "SINGLE_SELECT",
                             "options": [{"id": "opt_execution", "name": "execution"}]},
                            {"id": "PVTSSF_severity", "name": "Severity", "dataType": "SINGLE_SELECT",
                             "options": [{"id": "opt_high", "name": "high"}]},
                            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"},
                        ]
                    }
                }
            },
            "rateLimit": RATE_LIMIT,
        }
    }


def board_items():
    return {
        "data": {
            "organization": {
                "projectV2": {
                    "items": {
                        "pageInfo": {"hasNextPage": False, "endCursor": None},
                        "nodes": [
                            {
                                "status": {"name": "In review"},
                                "class": None,
                                "blockedBy": None,
                                "content": {
                                    "__typename": "Issue",
                                    "number": ITEM,
                                    "title": f"item {ITEM}",
                                    "state": "OPEN",
                                    "repository": {"nameWithOwner": f"{OWNER}/{REPO}"},
                                },
                            }
                        ],
                    }
                }
            },
            "rateLimit": RATE_LIMIT,
        }
    }


def graphql(query, variables):
    if "comments(last:" in query:
        n = variables.get("number")
        last = variables.get("last")
        if n is None or last is None:
            return None
        thread = [route_receipt_comment()]
        recent = thread[-int(last):] if int(last) > 0 else []
        return {
            "data": {"repository": {"issue": {"comments": {"nodes": [{"body": c["body"]} for c in recent]}}}},
            "rateLimit": RATE_LIMIT,
        }
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [{"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}}, "rateLimit": RATE_LIMIT}
    if "fields(first" in query:
        return project_fields()
    if "items(first" in query:
        return board_items()
    if "closedByPullRequestsReferences" in query:
        return {
            "data": {
                "repository": {
                    "issue": {
                        "number": ITEM,
                        "state": "OPEN",
                        "closedByPullRequestsReferences": {"nodes": []},
                        "timelineItems": {"nodes": []},
                        "subIssues": {"totalCount": 0, "nodes": []},
                        "projectItems": {"nodes": [{"project": {"number": 12}, "status": {"name": "In review"}}]},
                        "parent": None,
                    }
                },
                "rateLimit": RATE_LIMIT,
            }
        }
    if "projectItems" in query and "mutation" not in query:
        node = {"id": f"PVTI_{ITEM}", "project": {"number": 12}}
        if 'fieldValueByName(name: "Status")' in query:
            node["fieldValueByName"] = {"name": "In review"}
        elif 'fieldValueByName(name: "Blocked by")' in query:
            node["fieldValueByName"] = None
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [node]}}}}, "rateLimit": RATE_LIMIT}
    return None


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, status, payload):
        body = b"" if status in (204, 304) else json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        if self.path.split("?", 1)[0].rstrip("/") not in ("/graphql", "/rate_limit"):
            self.send_header("X-RateLimit-Resource", "core")
            self.send_header("X-RateLimit-Limit", "5000")
            self.send_header("X-RateLimit-Remaining", "4800")
            self.send_header("X-RateLimit-Used", "200")
            self.send_header("X-RateLimit-Reset", "1893456000")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _body(self):
        n = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(n).decode() if n else ""

    def do_POST(self):
        path = self.path.split("?", 1)[0]
        raw = self._body()
        if path.rstrip("/") == "/graphql":
            try:
                doc = json.loads(raw)
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "fixture: bad graphql body"}]})
            answer = graphql(doc.get("query", ""), doc.get("variables", {}))
            if answer is None:
                return self._send(500, {"errors": [{"message": f"fixture: unhandled query {doc.get('query', '')[:80]}"}]})
            return self._send(200, answer)
        self._send(500, {"message": f"fixture: unhandled POST {path}"})

    def do_GET(self):
        path = self.path.split("?", 1)[0]

        if path.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4999, "limit": 5000}}})

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", path)
        if m and int(m.group(1)) == ITEM:
            return self._send(200, [route_receipt_comment()])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if m and int(m.group(1)) == ITEM:
            return self._send(200, {"number": ITEM, "id": ITEM + 1000, "body": ISSUE_BODY, "state": "open"})

        # THE FACT THIS FIXTURE EXISTS TO EXERCISE: a genuinely OPEN PR on the item's own branch.
        m = re.match(r"^/repos/[^/]+/[^/]+/pulls$", path)
        if m:
            return self._send(200, [{"number": PR, "head": {"ref": BRANCH}, "state": "open"}])

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", path)
        if m:
            return self._send(200, {"number": int(m.group(1)), "head": {"ref": BRANCH}})

        m = re.match(rf"^/repos/[^/]+/[^/]+/git/matching-refs/heads/item/{ITEM}-$", path)
        if m:
            return self._send(200, [{"ref": f"refs/heads/{BRANCH}"}])

        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/?$", path)
        if m:
            return self._send(200, [{"number": ITEM, "state": "open", "title": f"item {ITEM}", "body": ISSUE_BODY}])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/sub_issues$", path)
        if m:
            return self._send(200, [])

        self._send(500, {"message": f"fixture: unhandled GET {path}"})


def main():
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    print(server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        import sys
        sys.exit(0)

#!/usr/bin/env python3
"""The HERMETIC source this fixture's `transcript.json` was recorded from (`.github#2401`).

`.github#2450` is the CLAIMED sibling of `.github#2384`/`fixtures/2216-oscillation/`: instead of an
*unclaimed* `In review` row, this fixture models one *claimed* — a live claim marker, `Status = In
review`, and a genuinely open implementation PR on its own branch. Before the fix at `Scan.fs:1298`, the
claimed-row PR probe fired only for `Status = InProgress`, so a row already `In review` never had
`Item.ItemPr` populated — `Client.fs`'s lifecycle projector then read a live claim with no PR fact and
proposed `Status=In progress` for a row that is legitimately `In review`, deterministically, on every
pass (the issue's follow-up comment records this as a live `reconcile`/`reconcile --apply` divergence:
the dry-run and the apply's own re-scan disagreed within one pass over the same two rows).

Run against the FIXED engine, `reconcile --json` returns `[]` for this row twice in a row (this
fixture's `transcript.json` lists `reconcile` TWICE in its `commands`, so `run.sh` genuinely invokes the
compiled engine binary a second time against the same running replay server) — `.github#2450`'s AC2,
"two consecutive reconcile passes ... produce no board write", proven end to end rather than asserted in
prose. Run against the PRE-FIX engine (`git stash` over just `Scan.fs`, or check out the parent commit),
both passes instead propose `Status=In progress` — the gate-inversion evidence for this fixture.

REGENERATING THIS FIXTURE

    python3 tests/coord-engine-replay/fixtures/2450-claimed-review-lag/scenario_server.py &
    SRV_PID=$!; sleep 0.3
    PORT=$(curl -s ...)   # or read the server's first stdout line directly
    GITHUB_TOKEN=fixture-token python3 scripts/record-board-fixture.py --repo FS.GG.SDD \\
        --upstream "http://127.0.0.1:$PORT" \\
        --out tests/coord-engine-replay/fixtures/2450-claimed-review-lag
    kill $SRV_PID

Unlike `fixtures/2216-oscillation/`, this fixture's checked-in `expected/reconcile.json` IS what capture
records against the fixed engine — no manual override, no `manifest.json`. After regenerating, manually
add a second `"reconcile"` entry to `transcript.json`'s top-level `"commands"` array (capture only ever
runs each command once) so `run.sh` still exercises the two-pass claim this fixture exists to prove.
"""

import hashlib
import json
import re
import threading
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[3]))
from route_decision_fixture import route_comment
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOCK = threading.Lock()

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
ITEM = 600
PR = 888
BRANCH = f"item/{ITEM}-the-work"
WORKER = "curlew-8afd"

ISSUE_BODY = "A CLAIMED row already In review, with a genuinely open implementation PR.\n\nPaths: src/Probe/**"

RATE_LIMIT = {"cost": 1, "remaining": 4999}


def route_receipt_comment():
    return {
        "id": 800000 + ITEM,
        "body": route_comment(f"{OWNER}/{REPO}#{ITEM}", ISSUE_BODY, "scenario-fixture",
                              "2450-claimed-review-lag scenario fixture route receipt."),
        "user": {"login": "fixture"},
        "created_at": "2026-01-01T00:00:00Z",
        "updated_at": "2026-01-01T00:00:00Z",
    }


def claim_marker_comment():
    # THE FACT THIS FIXTURE EXISTS TO EXERCISE: a CLAIM MARKER on a row already `In review`. Its
    # `updated_at` is a fixed historical timestamp (matching `route_receipt_comment` above), so by the
    # time this replays it always reads as a LAPSED lease (`Reads.isStale`) rather than within-lease —
    # deliberately, because `.github#2450`'s fix does not depend on lease freshness: `Chore.choresFor`'s
    # RESERVED branch produces no chore for `LeaseExpiredPrOpen` either (`| _ -> ()`, the same as
    # `LeaseHeld`), and the lifecycle reducer's open-PR rule fires before it ever inspects
    # liveness at all. A fixture whose correctness depended on staying within a 120-minute lease forever
    # would be a flaky fixture; this one is not.
    return {
        "id": 810000 + ITEM,
        "body": f"<!-- fsgg:claim worker={WORKER} lease=120 -->\nheld",
        "user": {"login": WORKER},
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
                                # .github#2712 — no `Kind` column on this board, which is the state of
                                # every live board until an operator creates the field.
                                "kind": None,
                                "blockedBy": None,
                                "content": {
                                    "__typename": "Issue",
                                    "number": ITEM,
                                    "title": f"item {ITEM}",
                                    "state": "OPEN",
                                    # Register depth (.github#2712) — served from this scenario's own
                                    # comment thread, so the count cannot contradict the data.
                                    "comments": {"totalCount": len([route_receipt_comment(), claim_marker_comment()])},
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
        # BOTH comments are on the thread — the route receipt AND the live claim marker — so a caller
        # asking for the last N sees whichever is actually most recent, exactly as REST would.
        thread = [route_receipt_comment(), claim_marker_comment()]
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
            return self._send(200, [route_receipt_comment(), claim_marker_comment()])

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

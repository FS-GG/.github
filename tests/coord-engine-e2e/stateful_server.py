#!/usr/bin/env python3
"""A STATEFUL GitHub fixture for the compiled `fsgg-coord-engine` — enough to exercise the WRITE commands.

The read fixture (`fixture_server.py`) is stateless: it serves a canned board. The write commands need
state, because the comment-order CAS is a read-modify-reread: `claim` posts a marker, RE-READS the
comments, and wins only if its marker is the lowest live one. A fixture that forgot the posted marker
would make every claim fail its own re-read.

So this server keeps:
  * comments per issue (POST appends with a monotonic id — the CAS's total order; DELETE/PATCH mutate);
  * the board Status per item (so a `set-field` write is visible to a later read);
  * sub-issue attachments and body edits (so `child` and `widen` land).

It is still deliberately small: one repo, a handful of issues, no auth. The point is to drive the real
binary's real writes across a process boundary and see the state change — not to reimplement GitHub.
"""

import json
import re
import sys
import threading
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer

LOCK = threading.Lock()

# issue number -> {"body": str, "state": "OPEN"|"CLOSED", "status": str}
ISSUES = {
    42: {"body": "A schedulable item.\n\nPaths: src/Thing/**", "state": "OPEN", "status": "Ready"},
    43: {"body": "Another item.\n\nPaths: src/Other/**", "state": "OPEN", "status": "Ready"},
    99: {"body": "A parent epic.\n\nPaths: none", "state": "OPEN", "status": "In progress"},
    44: {"body": "A verify-paths subject.\n\nPaths: src/Verify/**", "state": "OPEN", "status": "In progress"},
}

COMMENTS = {42: [], 43: [], 99: [], 44: []}      # issue -> [{"id", "body", "updated_at"}]
NEXT_COMMENT_ID = [900]
def now_iso():
    # REAL current time, so a just-posted marker is fresh — a fixed timestamp would land it at the
    # lease boundary and flip stale under the wall clock, which is a fixture bug, not a lock bug.
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

RATE_LIMIT = {"cost": 1, "remaining": 4999}


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
                            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"},
                        ]
                    }
                }
            },
            "rateLimit": RATE_LIMIT,
        }
    }


def board_items():
    nodes = []
    for n, issue in sorted(ISSUES.items()):
        nodes.append(
            {
                "status": {"name": issue["status"]} if issue["status"] else None,
                "blockedBy": None,
                "content": {
                    "__typename": "Issue",
                    "number": n,
                    "title": f"item {n}",
                    "state": issue["state"],
                    "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"},
                },
            }
        )
    return {
        "data": {
            "organization": {
                "projectV2": {"items": {"pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}}
            },
            "rateLimit": RATE_LIMIT,
        }
    }


def graphql(query: str, variables: dict):
    if "projectsV2" in query:
        return {
            "data": {
                "organization": {"projectsV2": {"nodes": [{"number": 12, "title": "Coordination", "id": "PVT_coord"}]}},
                "rateLimit": RATE_LIMIT,
            }
        }
    if "fields(first" in query:
        return project_fields()
    if "items(first" in query:
        return board_items()
    if "closedByPullRequestsReferences" in query:
        # done facts — issue 42 was closed by a merged PR #77.
        return {
            "data": {
                "repository": {
                    "issue": {
                        "number": 42,
                        "state": "CLOSED",
                        "closedByPullRequestsReferences": {"nodes": [{"number": 77, "merged": True}]},
                        "timelineItems": {"nodes": []},
                        "subIssues": {"totalCount": 0, "nodes": []},
                        "projectItems": {"nodes": [{"project": {"number": 12}, "status": {"name": "In review"}}]},
                        "parent": None,
                    }
                },
                "rateLimit": RATE_LIMIT,
            }
        }
    if "projectItems" in query:
        # item-id lookup: whatever issue was asked for is on our board.
        return {
            "data": {
                "repository": {"issue": {"projectItems": {"nodes": [{"id": "PVTI_item", "project": {"number": 12}}]}}},
                "rateLimit": RATE_LIMIT,
            }
        }
    if "closingIssuesReferences" in query:
        # PR #500 declares it closes issue #43.
        return {
            "data": {
                "repository": {
                    "pullRequest": {
                        "closingIssuesReferences": {
                            "nodes": [{"number": 44, "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}]
                        }
                    }
                },
                "rateLimit": RATE_LIMIT,
            }
        }
    if "updateProjectV2ItemFieldValue" in query or "clearProjectV2ItemFieldValue" in query:
        return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, status, payload):
        body = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
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
                return self._send(500, {"errors": [{"message": f"fixture: unhandled query {doc.get('query','')[:60]}"}]})
            return self._send(200, answer)

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", path)
        if m:
            n = int(m.group(1))
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                body = ""
            with LOCK:
                cid = NEXT_COMMENT_ID[0]
                NEXT_COMMENT_ID[0] += 1
                COMMENTS.setdefault(n, []).append({"id": cid, "body": body, "updated_at": now_iso()})
            return self._send(201, {"id": cid, "body": body, "updated_at": now_iso()})

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/sub_issues$", path)
        if m:
            return self._send(201, {"id": 1})

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/assignees$", path)
        if m:
            return self._send(201, {})

        self._send(500, {"message": f"fixture: unhandled POST {path}"})

    def do_PATCH(self):
        path = self.path.split("?", 1)[0]
        raw = self._body()

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", path)
        if m:
            cid = int(m.group(1))
            try:
                body = json.loads(raw).get("body", "")
            except json.JSONDecodeError:
                body = ""
            with LOCK:
                for lst in COMMENTS.values():
                    for c in lst:
                        if c["id"] == cid:
                            c["body"] = body
            return self._send(200, {"id": cid, "body": body})

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if m:
            n = int(m.group(1))
            try:
                body = json.loads(raw)
            except json.JSONDecodeError:
                body = {}
            with LOCK:
                if n in ISSUES and "body" in body:
                    ISSUES[n]["body"] = body["body"]
                if n in ISSUES and body.get("state") == "closed":
                    ISSUES[n]["state"] = "CLOSED"
            return self._send(200, {"number": n})

        self._send(500, {"message": f"fixture: unhandled PATCH {path}"})

    def do_DELETE(self):
        path = self.path.split("?", 1)[0]
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", path)
        if m:
            cid = int(m.group(1))
            with LOCK:
                for lst in COMMENTS.values():
                    lst[:] = [c for c in lst if c["id"] != cid]
            return self._send(204, {})
        self._send(500, {"message": f"fixture: unhandled DELETE {path}"})

    def do_GET(self):
        path = self.path.split("?", 1)[0]

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", path)
        if m:
            with LOCK:
                return self._send(200, list(COMMENTS.get(int(m.group(1)), [])))

        # The existing-sub-issues read `child` now makes before it POSTs (#320 — an unreachable read is not
        # an absent edge). No pre-existing edges here, so an empty array: `child` proceeds to link.
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/sub_issues$", path)
        if m:
            return self._send(200, [])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if m:
            n = int(m.group(1))
            if n not in ISSUES:
                return self._send(404, {"message": "Not Found"})
            with LOCK:
                return self._send(200, {"number": n, "id": n + 1000, "body": ISSUES[n]["body"], "state": ISSUES[n]["state"].lower()})

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)/files$", path)
        if m:
            pr = int(m.group(1))
            # PR #500: changes only files under issue #43's touch-set (src/Other/**) → OK.
            # PR #501: also touches docs/x.md, OUTSIDE src/Other/** → DRIFT.
            files = [{"filename": "src/Verify/Foo.fs"}]
            if pr == 501:
                files.append({"filename": "docs/x.md"})
            return self._send(200, files)

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls/(\d+)$", path)
        if m:
            pr = int(m.group(1))
            return self._send(200, {"number": pr, "head": {"ref": "item/44-the-work"}})

        m = re.match(r"^/repos/[^/]+/[^/]+/pulls$", path)
        if m:
            return self._send(200, [])   # no open PRs → prAlive says lease-expired-no-pr

        if path.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4999, "limit": 5000}}})

        self._send(500, {"message": f"fixture: unhandled GET {path}"})


def main():
    server = HTTPServer(("127.0.0.1", 0), Handler)
    print(server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

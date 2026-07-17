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

# issue number -> {"body": str, "state": "OPEN"|"CLOSED", "status": str, "repo": str}
#
# `repo` defaults to FS.GG.SDD — the fixture's original single repo, and what every pre-#733 leg means.
# #733 needs a SECOND repo, because the chore lock is per-repo and `Options.choreLockRef` knows exactly
# one: `.github#1033` (ADR-0041). An `AfterDone` offer in FS.GG.SDD is REFUSED for want of a lock, so a
# one-repo fixture can only ever prove the refusal — never the offer.
ISSUES = {
    42: {"body": "A schedulable item.\n\nPaths: src/Thing/**", "state": "OPEN", "status": "Ready"},
    43: {"body": "Another item.\n\nPaths: src/Other/**", "state": "OPEN", "status": "Ready"},
    99: {"body": "A parent epic.\n\nPaths: none", "state": "OPEN", "status": "In progress"},
    44: {"body": "A verify-paths subject.\n\nPaths: src/Verify/**", "state": "OPEN", "status": "In progress"},
    # #733 — a `.github` item that is CLOSED while its board column still says Ready. That is
    # CLOSED-ISSUE-NOT-DONE: a real chore, derived (never stored), and the one this fixture offers.
    50: {"body": "A closed .github item the board still calls Ready.\n\nPaths: src/X/**",
         "state": "CLOSED", "status": "Ready", "repo": ".github"},
    # #733 — the item whose `done` triggers the AfterDone offer. In `.github`, so its offer resolves a lock.
    51: {"body": "A finished .github item.\n\nPaths: src/Y/**", "state": "CLOSED", "status": "In review",
         "repo": ".github"},
}

def repo_of(n):
    return ISSUES.get(n, {}).get("repo", "FS.GG.SDD")

# 1033 is the CHORE LOCK (ADR-0041) and is deliberately NOT in ISSUES: it is not on the board and must
# never be. `Writes.claim` reaches it as a bare comment thread, which is all a CAS needs.
COMMENTS = {42: [], 43: [], 99: [], 44: [], 50: [], 51: [], 1033: []}
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
                    "repository": {"nameWithOwner": f"FS-GG/{repo_of(n)}"},
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
        # done facts — the asked-for issue was closed by a merged PR. Keyed on the VARIABLE rather than
        # hardcoded to 42 since #733: `done` must be drivable on a `.github` item (#51), because that is
        # the only repo whose AfterDone offer can resolve a chore lock.
        # int(): the wire carries it as a JSON NUMBER, so Python hands us 51.0 — and 51.0 misses every
        # int-keyed lookup below without erroring, which would silently answer for the wrong repo.
        n = int(variables.get("number", 42))
        return {
            "data": {
                "repository": {
                    "issue": {
                        "number": n,
                        "state": "CLOSED",
                        "closedByPullRequestsReferences": {"nodes": [
                            {"number": 77, "merged": True, "mergedAt": "2026-02-01T00:00:00Z",
                             "mergeCommit": {"abbreviatedOid": "77abc12"},
                             "closingIssuesReferences": {"nodes": [
                                 {"number": n, "repository": {"nameWithOwner": f"FS-GG/{repo_of(n)}"}}]}}]},
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

        # THE OFF-BOARD OPEN-ISSUE SCAN (bash's `active_claims` arm B). `Scan.snapshot` asks each in-scope
        # repo for its open issues so it can reserve a live claim on an issue the BOARD never listed.
        #
        # This route did not exist while nothing in this suite called `scan`, and its absence was not quiet:
        # a 500 fails the whole scan CLOSED (#461 — an unreadable scan is never an empty one), so #733's
        # AfterDone offer printed NOTHING rather than something wrong. That is the offer's "silent on every
        # failure" contract working, and it is also why this leg looked like a wiring bug and was not.
        #
        # Scoped by repo, because "off the board" is a question about one repo's issues — serving FS.GG.SDD's
        # list for a `.github` scan would reserve claims that repo never made.
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues/?$", path)
        if m:
            r = m.group(1)
            with LOCK:
                return self._send(200, [{"number": n, "state": i["state"].lower(), "body": i["body"]}
                                        for n, i in sorted(ISSUES.items())
                                        if repo_of(n) == r and i["state"] == "OPEN"])

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
            #
            # #498/ADR-0044 — a GENERATED, CI-gated artifact is outside the touch-set by the letter of the
            # declaration and is NOT drift: §1 forbids declaring it, so reporting it is the gate firing on
            # its own instruction. `registry/repos.lock` is the stand-in, and it is deliberately a path the
            # REAL `scripts/generated-paths` emits, so a leg pointed at the real roster and a leg pointed
            # at a stub agree about what the subtractable set contains.
            # PR #502: regenerated ONLY          → OK, with the artifact reported as expected.
            # PR #503: regenerated + real drift  → DRIFT naming ONLY docs/x.md as reviewable.
            files = [{"filename": "src/Verify/Foo.fs"}]
            if pr == 501:
                files.append({"filename": "docs/x.md"})
            if pr in (502, 503):
                files.append({"filename": "registry/repos.lock"})
            if pr == 503:
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

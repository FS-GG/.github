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
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LOCK = threading.Lock()

# issue number -> {"body": str, "state": "OPEN"|"CLOSED", "status": str, "repo": str}
#
# `repo` defaults to FS.GG.SDD — the fixture's original single repo, and what every pre-#733 leg means.
# #733 needs receiver coverage because the chore lock is per-repo. All seven rostered repos have one
# (#1087), including FS.GG.SDD#518; the receiver legs below prove an offer, while FS.GG.Legacy proves
# the unrostered no-lock refusal.
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
    # #1087 — a chore in a RECEIVER (FS.GG.SDD, the default repo): CLOSED while the board still says Ready.
    # Before #1087 the SDD offer was refused for want of a lock; now SDD#518 exists and this drains. This is
    # the row that proves the rollout — a receiver conscripts a caller, which .github used to do alone.
    45: {"body": "A closed SDD item the board still calls Ready.\n\nPaths: src/Z/**",
         "state": "CLOSED", "status": "Ready"},
    # .github#2157 — the closed #45 dependency has cleared, but this row is still projected as
    # Blocked.  Reconcile must make the coupled Status=Ready / Blocked by='' repair as one batch,
    # then prove BOTH values on a fresh scan.
    47: {"body": "A formerly blocked SDD item.\n\nPaths: src/Blocked/**",
         "state": "OPEN", "status": "Blocked", "blocked_by": "FS-GG/FS.GG.SDD#45"},
    # #1087 — the free-refusal control: an UNROSTERED repo (no chore lock at all). All seven FS-GG repos now
    # have one, so the honest "no lock" case is a repo `choreLockRef` does not know. `done` here stamps and
    # offers nothing, WITHOUT a board read — the #733 free-refusal path, now reachable only off-roster.
    60: {"body": "A finished item in a repo with no chore lock.\n\nPaths: src/Q/**",
         "state": "CLOSED", "status": "In review", "repo": "FS.GG.Legacy"},
    # .github#1779 — AN OPEN ISSUE IN THE REPO THAT IS NOT AN ITEM ON THE BOARD. `off_board` keeps it out
    # of `board_items()` AND makes the `projectItems` lookup answer with no node, which is what makes
    # `claim` report `statusWrite:"not-on-board"` — the state whose reservation no row-derived candidate
    # set can select, because there is no row. It declares `src/Verify/**`, the same touch-set #44 does.
    46: {"body": "A claim that never reached the board.\n\nPaths: src/Verify/**",
         "state": "OPEN", "status": "Ready", "off_board": True},
    # .github#2211 — an organization-board row whose issue belongs to another owner.  GitHub exposes
    # this row from the board side, but the issue-side `projectItems` connection names only rogue3's
    # own project.  The write legs use it to drive the compiled binary through that asymmetry.
    96: {"body": "A cross-owner Coordination row.\n\nPaths: src/External/**",
         "state": "OPEN", "status": "Backlog", "repo": "rogue3", "owner": "EHotwagner"},
}


def off_board(n):
    return ISSUES.get(n, {}).get("off_board", False)

def repo_of(n):
    return ISSUES.get(n, {}).get("repo", "FS.GG.SDD")


def owner_of(n):
    return ISSUES.get(n, {}).get("owner", "FS-GG")

# 1033 is the CHORE LOCK (ADR-0041) and is deliberately NOT in ISSUES: it is not on the board and must
# never be. `Writes.claim` reaches it as a bare comment thread, which is all a CAS needs.
COMMENTS = {42: [], 43: [], 99: [], 44: [], 46: [], 50: [], 51: [], 96: [], 1033: []}
NEXT_COMMENT_ID = [900]
NEXT_ROOM_NUMBER = [700]
def now_iso():
    # REAL current time, so a just-posted marker is fresh — a fixed timestamp would land it at the
    # lease boundary and flip stale under the wall clock, which is a fixture bug, not a lock bug.
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

RATE_LIMIT = {"cost": 1, "remaining": 4999}

# REST budget state is deliberately selectable by the write harness.  The production admission gate
# learns only from response headers, so these modes let the compiled CLI prove its refusal leaves a
# live force-steal target completely untouched.  ``unknown`` omits the authoritative headers rather
# than inventing a sentinel value: absence is the state the client must fail closed on.
REST_BUDGET_MODE = ["healthy"]

# BOARD READS, COUNTED — so a leg can assert what was NOT spent. #733's `AfterDone` offer costs `done` a
# scan it otherwise never makes, and `Chores.offer`'s step 1 (`choreLockRef`) is a pure string match that
# answers None for six of the org's seven repos. Ordering the scan BEFORE that match would buy a board read,
# on the budget the claim lock lives on, for a guaranteed refusal — in the COMMON case. That is a cost
# regression no assertion on output could catch, because the output is identical either way: nothing.
BOARD_READS = [0]

# #1151 — the deferred-write injection. `updateProjectV2ItemFieldValue` normally succeeds; when this
# counter is > 0 it decrements and answers a RATE-LIMIT error instead, so the engine's `boardWrite`
# returns `Deferred` (queued, nothing auto-replays it) exactly as an exhausted GraphQL budget does. It
# is armed for ONE write at a time (via `/_fixture/defer-next-field-write`), so a test can defer a
# specific stamp and then let `flush` replay it against a healthy server. The message matches
# `Budget.isRateLimited`'s pattern, which is how the real transport recognises the exhaustion.
DEFER_FIELD_WRITES = [0]

# .github#1779 — the PERMANENT-failure injection, and it is a DIFFERENT fixture from the one above rather
# than a parameter of it, because the engine's two outcomes are decided by the message. A rate-limit error
# becomes `Deferred` (queued; `flush` replays it). Anything else becomes an `Error`, which `claim` reports
# as `statusWrite:"failed"` and #510 refuses to queue — a write replayed forever is a promise nobody can
# keep. So this arms a NON-rate-limit error, and the leg that uses it asserts `pendingBoardWrites` is 0:
# without that assertion "failed" and "deferred" are indistinguishable from the outside, and the leg would
# be re-testing the deferral queue while claiming to test the case the queue cannot reach.
FAIL_FIELD_WRITES = [0]
RECONCILE_45_PROJECTION = [0]
# .github#2157's negative controls.  A mutation acknowledgement is deliberately not convergence:
# the next scan either sees only the Status half or no row at all.
RECONCILE_47_PARTIAL = [0]
RECONCILE_47_MISSING = [0]
DROP_INTAKE_FIELDS = set()

# .github#1779 AC2 — REST REQUESTS, COUNTED. The cost claim is "one issue-list read, plus one marker read
# per COLLIDING row" and a number in prose rots silently. This is the same instrumentation `BOARD_READS`
# is, one transport down: `_fixture/` routes are excluded (they are the meter, not the spend) and so is
# `/graphql`, which `BOARD_READS` already counts.
REST_READS = []

# The command-contract executable proof needs a wire ledger, not an inference from endpoint names:
# GraphQL reads are POSTs too.  Each fixture leg resets and reads this list, whose rows say whether the
# request was a REST mutation or a GraphQL mutation.
MUTATIONS = []


def record_mutation(method, path, graphql_document=None):
    """Record only shared-server mutations; fixture probes themselves are never evidence."""
    if path.startswith("/_fixture/"):
        return
    if path.rstrip("/") == "/graphql":
        if re.search(r"\bmutation\b", graphql_document or "", re.IGNORECASE):
            MUTATIONS.append({"method": method, "path": path, "kind": "graphql-mutation"})
    elif method in ("POST", "PATCH", "DELETE", "PUT"):
        MUTATIONS.append({"method": method, "path": path, "kind": "rest-mutation"})


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
                            {
                                "id": "PVTSSF_class",
                                "name": "Class",
                                "dataType": "SINGLE_SELECT",
                                "options": [{"id": "opt_hardening", "name": "hardening"}],
                            },
                            {"id": "PVTSSF_phase", "name": "Phase", "dataType": "SINGLE_SELECT", "options": [{"id": "opt_execution", "name": "execution"}]},
                            {"id": "PVTSSF_severity", "name": "Severity", "dataType": "SINGLE_SELECT", "options": [{"id": "opt_high", "name": "high"}]},
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
        if off_board(n):
            continue
        nodes.append(
            {
                "status": {"name": issue["status"]} if issue["status"] else None,
                "class": {"name": issue.get("class")} if issue.get("class") else None,
                "blockedBy": {"text": issue["blocked_by"]} if issue.get("blocked_by") else None,
                "content": {
                    "__typename": "Issue",
                    "number": n,
                    "title": f"item {n}",
                    "state": issue["state"],
                    "repository": {"nameWithOwner": f"{owner_of(n)}/{repo_of(n)}"},
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
    if "issue(number" in query and "projectItems" not in query:
        return {"data": {"repository": {"issue": {"id": "I_contract_probe"}}, "rateLimit": RATE_LIMIT}}
    if "projectsV2" in query:
        return {
            "data": {
                "organization": {"projectsV2": {"nodes": [{"number": 12, "title": "Coordination", "id": "PVT_coord"}]}},
                "rateLimit": RATE_LIMIT,
            }
        }
    if "fields(first" in query:
        return project_fields()
    if "node(id: $projectId)" in query:
        # External-owner item-id lookup: walk the Coordination board itself, because the issue-side
        # connection below deliberately cannot see #96's organization-board placement.
        nodes = []
        for n, issue in sorted(ISSUES.items()):
            if not off_board(n):
                nodes.append({"id": f"PVTI_{n}", "content": {
                    "number": n,
                    "repository": {"nameWithOwner": f"{owner_of(n)}/{repo_of(n)}"},
                }})
        return {"data": {"node": {"items": {
            "pageInfo": {"hasNextPage": False, "endCursor": None}, "nodes": nodes}},
            "rateLimit": RATE_LIMIT}}
    if "node(id: $itemId)" in query:
        item_id = str(variables.get("itemId", ""))
        try:
            n = int(item_id.removeprefix("PVTI_"))
            issue = ISSUES[n]
        except (KeyError, ValueError):
            return {"data": {"node": None, "rateLimit": RATE_LIMIT}}
        field = variables.get("field")
        values = {"Status": issue.get("status"), "Class": issue.get("class"), "Phase": issue.get("phase"), "Severity": issue.get("severity"), "Blocked by": issue.get("blocked_by")}
        value = values.get(field)
        field_value = ({"text": value} if field == "Blocked by" else {"name": value}) if value else None
        return {"data": {"node": {"fieldValueByName": field_value}, "rateLimit": RATE_LIMIT}}
    if "items(first" in query:
        with LOCK:
            BOARD_READS[0] += 1
        return board_items()
    if "closedByPullRequestsReferences" in query:
        # done facts — the asked-for issue was closed by a merged PR. Keyed on the VARIABLE rather than
        # hardcoded to 42 since #733: `done` must be drivable on a `.github` item (#51), because that is
        # a rostered repo whose AfterDone offer can resolve its own chore lock.
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
    if "projectItems" in query and "mutation" not in query:
        # item-id lookup. Every issue is on our board EXCEPT an `off_board` one, which answers with no
        # node — `Board.itemId`'s `Ok None`, which `boardWrite` turns into `NotOnBoard` (.github#1779).
        # int(): the wire carries the number as a JSON number, so Python hands us 46.0, and 46.0 misses an
        # int-keyed lookup without erroring — which would silently put the off-board issue back on it.
        n = int(variables.get("number", 0))
        if off_board(n):
            nodes = []
        elif owner_of(n) != "FS-GG":
            # This is the live issue-side shape for EHotwagner/rogue3#96: its personal project is
            # visible, while the FS-GG organization board's row is absent.
            nodes = [{"id": f"PVTI_user_{n}", "project": {"number": 7}}]
        else:
            node = {"id": f"PVTI_{n}", "project": {"number": 12}}
            if 'fieldValueByName(name: "Status")' in query:
                status = ISSUES.get(n, {}).get("status")
                node["fieldValueByName"] = {"name": status} if status else None
            elif 'fieldValueByName(name: "Blocked by")' in query:
                blocked_by = ISSUES.get(n, {}).get("blocked_by")
                node["fieldValueByName"] = {"text": blocked_by} if blocked_by else None
            nodes = [node]
        return {
            "data": {
                "repository": {"issue": {"projectItems": {"nodes": nodes}}},
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
        # #1151: if a defer is armed, RATE-LIMIT this one field write so `boardWrite` returns `Deferred`.
        with LOCK:
            if DEFER_FIELD_WRITES[0] > 0:
                DEFER_FIELD_WRITES[0] -= 1
                return {"errors": [{"message": "API rate limit exceeded for this fixture"}]}
            # .github#1779: a PERMANENT failure. The message deliberately matches no rate-limit pattern,
            # so `Budget.isRateLimited` says no, `boardWrite` returns `Error` rather than `Deferred`,
            # and #510 does not queue it. `claim` reports `statusWrite:"failed"` and exits GREEN.
            if FAIL_FIELD_WRITES[0] > 0:
                FAIL_FIELD_WRITES[0] -= 1
                return {"errors": [{"message": "fixture: the field write failed permanently"}]}
            # Model the projection separately from the acknowledgement: a successful mutation becomes
            # visible on the next board scan for its specific item.  Batch mutations inline their ids,
            # while the single-field form uses variables, so recognize both wire shapes.
            item_id = str(variables.get("itemId", ""))
            if not item_id:
                match = re.search(r'itemId:\s*"(PVTI_\d+)"', query)
                item_id = match.group(1) if match else ""
            if item_id.startswith("PVTI_"):
                try:
                    n = int(item_id.removeprefix("PVTI_"))
                    # Single-field writes carry the selected option in variables. Batch writes inline
                    # it in the GraphQL document, so read only the inline option values as a separate
                    # shape — matching arbitrary document text would make unrelated batch fields look
                    # like Status writes.
                    value = json.dumps(variables)
                    inline_options = re.findall(r'singleSelectOptionId:\s*"([^"]+)"', query)
                    dropped = set(DROP_INTAKE_FIELDS)
                    DROP_INTAKE_FIELDS.clear()
                    if n == 47 and RECONCILE_47_MISSING[0] > 0:
                        RECONCILE_47_MISSING[0] -= 1
                        ISSUES[n]["off_board"] = True
                    elif n == 47 and RECONCILE_47_PARTIAL[0] > 0:
                        RECONCILE_47_PARTIAL[0] -= 1
                        ISSUES[n]["status"] = "Ready"
                    elif n == 47 and "f0:" in query and "f1:" in query:
                        ISSUES[n]["status"] = "Ready"
                        ISSUES[n]["blocked_by"] = ""
                    elif "opt_done" in value or "opt_done" in inline_options:
                        ISSUES[n]["status"] = "Done"
                    elif "opt_wip" in value or "opt_wip" in inline_options:
                        ISSUES[n]["status"] = "In progress"
                    elif "opt_ready" in value or "opt_ready" in inline_options:
                        ISSUES[n]["status"] = "Ready"
                    elif "opt_backlog" in value or "opt_backlog" in inline_options:
                        ISSUES[n]["status"] = "Backlog"
                    elif "opt_blocked" in value or "opt_blocked" in inline_options:
                        ISSUES[n]["status"] = "Blocked"
                    if "Class" not in dropped and ("opt_hardening" in value or "opt_hardening" in inline_options):
                        ISSUES[n]["class"] = "hardening"
                    if "Phase" not in dropped and ("opt_execution" in value or "opt_execution" in inline_options):
                        ISSUES[n]["phase"] = "execution"
                    if "Severity" not in dropped and ("opt_high" in value or "opt_high" in inline_options):
                        ISSUES[n]["severity"] = "high"
                    blocked = re.search(r'fieldId:\s*"PVTF_blocked"[^}]*value:\s*\{text:\s*"([^"]+)"', query)
                    if blocked:
                        ISSUES[n]["blocked_by"] = blocked.group(1)
                except (ValueError, KeyError):
                    pass
            if RECONCILE_45_PROJECTION[0] > 0 and "opt_done" in json.dumps(variables):
                RECONCILE_45_PROJECTION[0] -= 1
                ISSUES[45]["status"] = "Done"
        return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
    if "addProjectV2ItemById" in query:
        # GitHub's mutation is idempotent.  The issue-side lookup above misses the external row, but
        # adding it again returns the existing Coordination item, whose live Status must then be read.
        # Intake creates an issue before projecting it.  The newest issue is therefore the only
        # newly-created board candidate in this hermetic fixture; return its real project-item id so
        # the subsequent Status mutation and readback exercise the same public transaction.
        created = max(ISSUES)
        ISSUES[created]["off_board"] = False
        return {"data": {"addProjectV2ItemById": {"item": {"id": f"PVTI_{created}"}}}}
    return None


# HTTP/1.1 with a THREAD PER CONNECTION, together, on purpose (#1172). The compiled engine drives this
# through .NET's `HttpClient`, which POOLS keep-alive connections and reuses one across the CAS's
# post-marker → re-read pair. The default `BaseHTTPRequestHandler` speaks HTTP/1.0 and CLOSES every
# connection (`will_close=True`), so the pool reuses a socket the server is tearing down; under CI load
# the client writes the re-read onto that half-closed socket and the framing desyncs — the re-read
# misses the just-posted marker and a second worker "wins" a lock it should have lost. That is #761's
# shape ("a fixture that spoke HTTP/1.0 at a pooling client"), one suite over.
#
# HTTP/1.1 keeps the connection alive with the Content-Length framing `_send` already writes, so the
# pool's reuse is legal instead of a race. But a kept-alive connection the client holds open would
# then BLOCK a single-threaded server's accept loop — deadlocking the moment the engine opens a second
# connection (its multi-repo off-board scan does) — so the server must serve each connection on its own
# thread. The module `LOCK` already guards every shared-state access, which is what makes that safe.
class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _meter(self):
        """Bill one REST request (.github#1779 AC2). `_fixture/` is the meter, `/graphql` is GraphQL."""
        p = self.path.split("?", 1)[0]
        if p.startswith("/_fixture/") or p.rstrip("/") == "/graphql" or p.rstrip("/") == "/rate_limit":
            return
        with LOCK:
            REST_READS.append(f"{self.command} {p}")

    def _send(self, status, payload):
        # A 204/304 carries no body; under HTTP/1.1 keep-alive a stray body would desync the NEXT
        # request on the connection — reintroducing, on the DELETE path, the very race this fix closes.
        body = b"" if status in (204, 304) else json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        # The production client admits a first claim only after a real resource response has
        # established capacity.  This fixture is that real HTTP route, so model GitHub's resource
        # headers rather than leaving every command permanently UNKNOWN.
        if self.path.split("?", 1)[0].rstrip("/") not in ("/graphql", "/rate_limit") and REST_BUDGET_MODE[0] != "unknown":
            remaining = {"healthy": "4800", "constrained": "99", "exhausted": "0"}[REST_BUDGET_MODE[0]]
            self.send_header("X-RateLimit-Resource", "core")
            self.send_header("X-RateLimit-Limit", "5000")
            self.send_header("X-RateLimit-Remaining", remaining)
            self.send_header("X-RateLimit-Used", str(5000 - int(remaining)))
            self.send_header("X-RateLimit-Reset", "1893456000")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        if body:
            self.wfile.write(body)

    def _body(self):
        n = int(self.headers.get("Content-Length", 0))
        return self.rfile.read(n).decode() if n else ""

    def do_POST(self):
        self._meter()
        path = self.path.split("?", 1)[0]
        raw = self._body()

        if path.rstrip("/") == "/graphql":
            try:
                doc = json.loads(raw)
            except json.JSONDecodeError:
                return self._send(500, {"errors": [{"message": "fixture: bad graphql body"}]})
            with LOCK:
                record_mutation("POST", path, doc.get("query", ""))
            answer = graphql(doc.get("query", ""), doc.get("variables", {}))
            if answer is None:
                return self._send(500, {"errors": [{"message": f"fixture: unhandled query {doc.get('query','')[:60]}"}]})
            return self._send(200, answer)

        with LOCK:
            record_mutation("POST", path)
        m = re.match(r"^/repos/[^/]+/([^/]+)/issues$", path)
        if m:
            try:
                payload = json.loads(raw)
            except json.JSONDecodeError:
                payload = {}
            with LOCK:
                number = NEXT_ROOM_NUMBER[0]
                NEXT_ROOM_NUMBER[0] += 1
                ISSUES[number] = {
                    "title": payload.get("title", ""),
                    "body": payload.get("body", ""),
                    "state": "OPEN",
                    "status": None,
                    "class": None,
                    "repo": m.group(1),
                    "off_board": True,
                }
                COMMENTS[number] = []
            return self._send(201, {"number": number})
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
        self._meter()
        path = self.path.split("?", 1)[0]
        raw = self._body()
        with LOCK:
            record_mutation("PATCH", path)

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
        self._meter()
        path = self.path.split("?", 1)[0]
        with LOCK:
            record_mutation("DELETE", path)
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/comments/(\d+)$", path)
        if m:
            cid = int(m.group(1))
            with LOCK:
                for lst in COMMENTS.values():
                    lst[:] = [c for c in lst if c["id"] != cid]
            return self._send(204, {})
        self._send(500, {"message": f"fixture: unhandled DELETE {path}"})

    def do_GET(self):
        self._meter()
        path = self.path.split("?", 1)[0]

        # THE FIXTURE'S OWN INSTRUMENTATION, not a GitHub route — hence the `_fixture/` prefix, which no
        # real endpoint can collide with. It reports what the engine SPENT, so a leg can assert a read that
        # must not happen. There is no other way to see it: a scan that should not have run and a scan that
        # ran and found nothing produce byte-identical output.
        if path.rstrip("/") == "/_fixture/board-reads":
            with LOCK:
                return self._send(200, {"boardReads": BOARD_READS[0]})

        # #1151 — arm ONE deferred field write. The next `updateProjectV2ItemFieldValue` rate-limits, so
        # `boardWrite` returns `Deferred`; the one after it succeeds. Lets a test drive the deferred-stamp
        # path (`done` keeps the green verdict, surfaces the flush remedy) and then `flush` it clean.
        if path.rstrip("/") == "/_fixture/defer-next-field-write":
            with LOCK:
                DEFER_FIELD_WRITES[0] += 1
                return self._send(200, {"deferArmed": DEFER_FIELD_WRITES[0]})

        # .github#1779 — arm ONE PERMANENTLY-FAILING field write. Sibling of the line above, and the
        # difference between them is the whole of what #510 decides: this one is never queued.
        if path.rstrip("/") == "/_fixture/fail-next-field-write":
            with LOCK:
                FAIL_FIELD_WRITES[0] += 1
                return self._send(200, {"failArmed": FAIL_FIELD_WRITES[0]})

        # .github#1779 AC2 — read and RESET the REST request log, so a leg can measure exactly what one
        # command spent. Reset-on-read, because the alternative is every caller remembering a baseline.
        if path.rstrip("/") == "/_fixture/rest-reads":
            with LOCK:
                spent = list(REST_READS)
                REST_READS.clear()
                return self._send(200, {"count": len(spent), "paths": spent})

        if path.rstrip("/") == "/_fixture/mutations":
            with LOCK:
                spent = list(MUTATIONS)
                MUTATIONS.clear()
                return self._send(200, {"count": len(spent), "requests": spent})

        m = re.match(r"^/_fixture/drop-intake-field/(Class|Phase|Severity)$", path)
        if m:
            with LOCK:
                DROP_INTAKE_FIELDS.add(m.group(1))
                return self._send(200, {"drop": m.group(1)})

        # #2268: configure the headers seen by the *real* REST marker scan.  This is not a
        # synthetic engine hook: the following command must derive admission entirely from these
        # response headers, including the genuinely headerless Unknown case.
        m = re.match(r"^/_fixture/rest-budget/(healthy|constrained|exhausted|unknown)$", path)
        if m:
            with LOCK:
                REST_BUDGET_MODE[0] = m.group(1)
                return self._send(200, {"mode": REST_BUDGET_MODE[0]})

        if path.rstrip("/") == "/_fixture/reset-reconcile-45":
            with LOCK:
                ISSUES[45]["status"] = "Ready"
            return self._send(200, {"number": 45, "status": "Ready"})

        if path.rstrip("/") == "/_fixture/arm-reconcile-45-projection":
            with LOCK:
                RECONCILE_45_PROJECTION[0] += 1
            return self._send(200, {"armed": RECONCILE_45_PROJECTION[0]})

        if path.rstrip("/") == "/_fixture/reset-reconcile-47":
            with LOCK:
                ISSUES[47]["off_board"] = False
                ISSUES[47]["status"] = "Blocked"
                ISSUES[47]["blocked_by"] = "FS-GG/FS.GG.SDD#45"
                RECONCILE_47_PARTIAL[0] = 0
                RECONCILE_47_MISSING[0] = 0
            return self._send(200, {"number": 47, "status": "Blocked", "blockedBy": ISSUES[47]["blocked_by"]})

        if path.rstrip("/") == "/_fixture/arm-reconcile-47-partial":
            with LOCK:
                RECONCILE_47_PARTIAL[0] += 1
            return self._send(200, {"armed": RECONCILE_47_PARTIAL[0]})

        if path.rstrip("/") == "/_fixture/arm-reconcile-47-missing":
            with LOCK:
                RECONCILE_47_MISSING[0] += 1
            return self._send(200, {"armed": RECONCILE_47_MISSING[0]})

        # #1569: `reap --apply` must see a genuinely expired marker.  Claims use the real wall
        # clock so all normal CAS legs remain live; this narrow test-only route ages the existing
        # marker after it has been created through the real command.
        m = re.match(r"^/_fixture/expire-claim/(\d+)$", path)
        if m:
            n = int(m.group(1))
            with LOCK:
                for comment in COMMENTS.get(n, []):
                    if "fsgg:claim" in comment.get("body", ""):
                        comment["updated_at"] = "2000-01-01T00:00:00Z"
            return self._send(200, {"expired": n})

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
                return self._send(200, [{"number": n, "state": i["state"].lower(),
                                         "title": i.get("title", f"item {n}"), "body": i["body"]}
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

        m = re.match(r"^/repos/[^/]+/[^/]+/git/matching-refs/heads/item/\d+-$", path)
        if m:
            return self._send(200, [])   # no pushed item branch → the expired claim is reapable

        if path.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4999, "limit": 5000}}})

        self._send(500, {"message": f"fixture: unhandled GET {path}"})


def main():
    # ThreadingHTTPServer: a thread per connection, so a client's idle keep-alive connection never
    # starves the accept loop. `daemon_threads = True` (its default) keeps `kill <pid>` immediate.
    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    print(server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

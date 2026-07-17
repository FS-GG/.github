#!/usr/bin/env python3
"""Case 14's `done` PR-PROVENANCE legs (#342/#558/#543), served over HTTP for the compiled engine.

The corpus (`14-no-touch-set-and-done.sh`) certifies that `done` names the PR that ACTUALLY closed the
issue, never the first prose mention, and that `--pr` cannot launder a mention into a stamp:

    done #84              -> DONE, "merged PR #92 @ 09c836e" — the CLOSER, not the earlier mention #85 (#342)
    done #86              -> NOT-DONE, "no merged PR closes this issue" — a mere mention is not a closer (#342)
    done #88              -> DONE, "merged PR #95 @ 2222bbb" — the LATEST-merged wins, not the lower-numbered #89 (#342)
    done #165 --flip      -> DONE — a keyword in the commit SUBJECT still earns the stamp (GitHub's own closer, #558)
    done #166 --flip      -> DONE — a COMMIT closer resolves through to its associated PR (#558)
    done #96 --pr 97      -> NOT-DONE — PR 97 closes #70, not #96; --pr may not launder a mention (#543)

`closedByPullRequestsReferences` is a SUPERSET: it lists mentions too, lowest-number-first. A node is a
CLOSER iff its own body names this issue (`closingIssuesReferences` -> ClosesThis) OR GitHub's own
CLOSED_EVENT names it (`timelineItems.closer`, a PullRequest directly or the PR associated with a Commit).
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

RATE = {"cost": 1, "remaining": 4980}


def pr(number, merged=True, merged_at="", oid="", closes=None):
    """A closedByPullRequestsReferences node — a PR GitHub associates with closing this issue."""
    node = {"number": number, "merged": merged}
    if merged_at:
        node["mergedAt"] = merged_at
    if oid:
        node["mergeCommit"] = {"abbreviatedOid": oid}
    node["closingIssuesReferences"] = {"nodes": [
        {"number": c, "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}} for c in (closes or [])]}
    return node


def closer_pr(n):
    return {"closer": {"__typename": "PullRequest", "number": n}}


def closer_commit(oid, prs):
    return {"closer": {"__typename": "Commit", "oid": oid,
                       "associatedPullRequests": {"nodes": [{"number": p} for p in prs]}}}


def issue(number, closing_nodes, closer_nodes):
    return {"data": {"repository": {"issue": {
        "number": number, "state": "CLOSED",
        "closedByPullRequestsReferences": {"nodes": closing_nodes},
        "timelineItems": {"nodes": closer_nodes},
        "subIssues": {"totalCount": 0, "nodes": []},
        "projectItems": {"nodes": [{"project": {"number": 12}, "status": {"name": "In progress"}}]},
        "parent": None}}},
        "rateLimit": RATE}


FACTS = {
    # #84 — the closer #92 (names #84 in its body) is the LATEST-merged; #85 merely mentions #84 (its body
    #        names #74) and merged earlier. The stamp must name #92 @ 09c836e, never #85/410843e.
    84: issue(84, [pr(85, merged_at="2026-01-01T00:00:00Z", oid="410843e", closes=[74]),
                   pr(92, merged_at="2026-03-01T00:00:00Z", oid="09c836e", closes=[84])], []),
    # #86 — one merged PR, but its body names #99, and no close event names it. A mention closes nothing.
    86: issue(86, [pr(605, merged_at="2026-01-01T00:00:00Z", oid="ababab0", closes=[99])], []),
    # #88 — two TRUE closers (#89 and #95 both name #88). The LATEST-merged (#95 @ 2222bbb) wins, not the
    #        lower-numbered, earlier-merged #89 @ 1111aaa.
    88: issue(88, [pr(89, merged_at="2026-01-01T00:00:00Z", oid="1111aaa", closes=[88]),
                   pr(95, merged_at="2026-03-01T00:00:00Z", oid="2222bbb", closes=[88])], []),
    # #165 — the keyword was in the commit SUBJECT, so PR 700's body never named #165 (closes=[]). GitHub's
    #         CLOSED_EVENT names the PR directly — that is what earns the stamp (#558).
    165: issue(165, [pr(700, merged_at="2026-02-01T00:00:00Z", oid="7770001", closes=[])], [closer_pr(700)]),
    # #166 — the closer is the COMMIT (a squash); its associated PR is 701. Resolves through (#558).
    166: issue(166, [pr(701, merged_at="2026-02-01T00:00:00Z", oid="7770002", closes=[])],
               [closer_commit("deadbeef", [701])]),
    # #96 — PR 97 is merged and listed, but its body names #70, not #96, and no close event names it. So
    #        `--pr 97` points at a PR that closed a DIFFERENT issue — a mention, refused (#543).
    96: issue(96, [pr(97, merged_at="2026-02-01T00:00:00Z", oid="9990097", closes=[70])], []),
}


def graphql(query, variables):
    number = variables.get("number")
    if "closedByPullRequestsReferences" in query:      # Done.facts — most specific token first
        return FACTS.get(int(number))
    if "updateProjectV2ItemFieldValue" in query:
        return {"data": {"updateProjectV2ItemFieldValue": {"clientMutationId": None}}}
    if "projectsV2" in query:
        return {"data": {"organization": {"projectsV2": {"nodes": [
            {"number": 12, "title": "Coordination", "id": "PVT_coord"}]}}, "rateLimit": RATE}}
    if "fields(first" in query:
        return {"data": {"organization": {"projectV2": {"fields": {"nodes": [
            {"id": "PVTSSF_status", "name": "Status", "dataType": "SINGLE_SELECT",
             "options": [{"id": "opt_ready", "name": "Ready"}, {"id": "opt_wip", "name": "In progress"},
                         {"id": "opt_done", "name": "Done"}]},
            {"id": "PVTF_blocked", "name": "Blocked by", "dataType": "TEXT"}]}}}, "rateLimit": RATE}}
    if "projectItems" in query:                         # Board.itemId
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
            {"id": "PVTI_item", "project": {"number": 12}}]}}}, "rateLimit": RATE}}
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
        n = int(self.headers.get("Content-Length", 0))
        try:
            doc = json.loads(self.rfile.read(n).decode())
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(doc.get("query", ""), doc.get("variables", {}) or {})
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {doc.get('query','')[:60]}"}]})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])   # we hold no claim here — the #533 release is a no-op
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

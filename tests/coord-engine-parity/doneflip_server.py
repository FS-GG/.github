#!/usr/bin/env python3
"""Case 14's `done --flip` EPIC ROLLUP, served over HTTP for the compiled engine.

The corpus (`14-no-touch-set-and-done.sh`) certifies that stamping a child with `--flip` climbs to its
parent epic and rolls it up ONLY when the epic is genuinely finished: it HOLDS while a sibling is still
open (#235/#583), FLIPS when every child is Done + closed, and — the leg this slice adds to the engine —
REFUSES when the epic's BODY declares a child the sub-issue graph does not contain (#325), while a
body-cited PR ref does NOT block the flip (a PR can never be a sub-issue, #346).

This drives the compiled `done --flip` end to end. Four children, four parents:

    done #42 --flip  -> #42 DONE; parent #301 HELD  (graph {#42 closed, #44 OPEN} — a sibling is open)
    done #62 --flip  -> #62 DONE; parent #302 FLIPS (graph {#62 closed}; body declares only #62)
    done #72 --flip  -> #72 DONE; parent #303 REFUSES (body declares #74, absent from the graph -> named)
    done #82 --flip  -> #82 DONE; parent #304 FLIPS (body declares #82 + PR #920; the PR ref is dropped)
"""

import json
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

RATE = {"cost": 1, "remaining": 4980}
SDD = {"name": "FS.GG.SDD", "owner": {"login": "FS-GG"}}


def issue_facts(number, state, closing_prs, sub_total, sub_nodes, status, parent):
    # Each child's own PR names it in its body (closingIssuesReferences -> ClosesThis), the ordinary case:
    # the #342 provenance read keeps the whole set, so a closer must record that it closed THIS issue.
    return {"data": {"repository": {"issue": {
        "number": number, "state": state,
        "closedByPullRequestsReferences": {"nodes": [
            {"number": pr, "merged": True, "mergedAt": "2026-02-01T00:00:00Z",
             "mergeCommit": {"abbreviatedOid": f"c{pr:06d}"},
             "closingIssuesReferences": {"nodes": [
                 {"number": number, "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}]}}
            for pr in closing_prs]},
        "timelineItems": {"nodes": []},
        "subIssues": {"totalCount": sub_total,
                      "nodes": [{"number": n, "state": st} for (n, st) in sub_nodes]},
        "projectItems": {"nodes": [{"project": {"number": 12}, "status": {"name": status}}]},
        "parent": ({"number": parent, "repository": SDD} if parent else None)}}},
        "rateLimit": RATE}


# Per-item facts (the FactsDoc read is keyed on number).
FACTS = {
    42: (issue_facts(42, "CLOSED", [7], 0, [], "Done", 301)),
    301: (issue_facts(301, "OPEN", [], 2, [(42, "CLOSED"), (44, "OPEN")], "In progress", None)),
    62: (issue_facts(62, "CLOSED", [8], 0, [], "Done", 302)),
    302: (issue_facts(302, "OPEN", [], 1, [(62, "CLOSED")], "In progress", None)),
    72: (issue_facts(72, "CLOSED", [9], 0, [], "Done", 303)),
    303: (issue_facts(303, "OPEN", [], 1, [(72, "CLOSED")], "In progress", None)),
    82: (issue_facts(82, "CLOSED", [10], 0, [], "Done", 304)),
    304: (issue_facts(304, "OPEN", [], 1, [(82, "CLOSED")], "In progress", None)),
}

# The standalone sub-issue graph (Reads.subIssues, keyed on number) that the rollup re-reads for refs.
GRAPH = {
    302: (1, [(62, "CLOSED")]),
    303: (1, [(72, "CLOSED")]),
    304: (1, [(82, "CLOSED")]),
}

# Bodies read during the rollup's unlinked check (parents) + the PR-probe targets.
BODIES = {
    302: "- [x] #62 the only child",
    303: "- [x] #72 done\n- [ ] #74 the UNLINKED half",
    304: "- [x] #82 done\n- [x] PR #920 the landing",
    74: "a plain issue",
    920: "the PR that landed it",
}
PRS = {920}


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
    if "subIssues" in query:                            # Reads.subIssues — the rollup's ref read
        total, nodes = GRAPH.get(int(number), (0, []))
        return {"data": {"repository": {"issue": {"subIssues": {
            "totalCount": total,
            "nodes": [{"number": n, "state": st, "repository": {"nameWithOwner": "FS-GG/FS.GG.SDD"}}
                      for (n, st) in nodes]}}}}, "rateLimit": RATE}
    if "projectItems" in query:                         # Board.itemId
        return {"data": {"repository": {"issue": {"projectItems": {"nodes": [
            {"id": "PVTI_item", "project": {"number": 12}}]}}}, "rateLimit": RATE}}
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
            doc = json.loads(self.rfile.read(n).decode())
        except json.JSONDecodeError:
            return self._send(500, {"errors": [{"message": "bad body"}]})
        a = graphql(doc.get("query", ""), doc.get("variables", {}) or {})
        self._send(200, a if a is not None else {"errors": [{"message": f"unhandled {doc.get('query','')[:60]}"}]})

    def do_PATCH(self):
        # closeIssue (#613) — the rollup PATCHes the parent issue closed.
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", self.path.split("?", 1)[0])
        if m:
            return self._send(200, {"number": int(m.group(1)), "state": "closed"})
        self._send(500, {"message": "unhandled PATCH"})

    def do_GET(self):
        p = self.path.split("?", 1)[0]
        if re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", p):
            return self._send(200, [])   # we hold no claim here — the #533 release is a no-op
        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", p)
        if m:
            num = int(m.group(1))
            payload = {"number": num, "body": BODIES.get(num, "")}
            if num in PRS:
                payload["pull_request"] = {"url": f"https://github.com/x/y/pull/{num}"}
            return self._send(200, payload)
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

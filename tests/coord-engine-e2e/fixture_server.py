#!/usr/bin/env python3
"""A hermetic GitHub fixture for the compiled `fsgg-coord-engine`.

The engine's `scan` command reads a board over HTTP through a CONFIGURABLE API base
(`FSGG_GITHUB_API_BASE`, ADR-0040 C1). Every other test points the *unit* transport at an in-process
`HttpListener`; nothing points the *compiled binary* at a real server. This is that server — a tiny,
no-network stand-in for api.github.com that the built engine talks to exactly as it would talk to GitHub.

It answers only what a `scan` needs:
  * POST /graphql  →  discriminated on the query text, like the real corpus's `gh` stub:
      - `projectsV2`   → the project list (bootstrap step 1)
      - `fields(first` → the field/option map (bootstrap step 2)
      - `items(first`  → one page of board items (the scan)
  * GET  /repos/{o}/{r}/issues/{n}          → the issue body (the touch-set)
  * GET  /repos/{o}/{r}/issues/{n}/comments → the claim markers

The board it serves is deliberately trivial and unambiguous: one Ready issue, a real touch-set, no
blockers, no claims — so a correct engine returns exactly one schedulable item and a broken one cannot
accidentally agree. The point is not coverage (the unit suites own that); it is that the BINARY, end to
end, over HTTP, produces the right verdict without a token or a network.
"""

import json
import hashlib
import re
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer

OWNER = "FS-GG"
REPO = "FS.GG.SDD"
PROJECT_TITLE = "Coordination"
PROJECT_NUMBER = 12

# The board: one Ready issue, #42, with a declared touch-set and nothing holding or blocking it.
BOARD_ITEMS = [
    {
        "status": {"name": "Ready"},
        "blockedBy": None,
        "content": {
            "__typename": "Issue",
            "number": 42,
            "title": "a real, schedulable item",
            "state": "OPEN",
            "repository": {"nameWithOwner": f"{OWNER}/{REPO}"},
        },
    }
]

ISSUE_BODIES = {42: "A schedulable item.\n\nPaths: src/Thing/**"}


def delivery_route_comment(number: int, body: str):
    """The ordinary success fixture includes the same source-bound agent decision production requires."""
    receipt = {
        "schema": "fsgg.coord.delivery-route/v1",
        "subject": f"{OWNER}/{REPO}#{number}",
        "subjectRevision": hashlib.sha256(body.encode()).hexdigest(),
        "route": "lightweight",
        "agent": "fixture-route",
        "timestamp": "2026-01-01T00:00:00Z",
        "reasonCodes": ["fixture"],
        "rationale": "Hermetic current route receipt.",
        "declaredImpacts": ["internal"],
        "observedFacts": ["localized"],
        "sddWorkId": None,
        "specHome": None,
        "requiredGates": [],
    }
    return {
        "id": 7042,
        "body": "<!-- fsgg:delivery-route/v1 -->\n" + json.dumps(receipt, separators=(",", ":")),
        "user": {"login": "fixture"},
        "created_at": "2026-01-01T00:00:00Z",
        "updated_at": "2026-01-01T00:00:00Z",
    }


ISSUE_COMMENTS = {42: [delivery_route_comment(42, ISSUE_BODIES[42])]}  # no claim marker → item is free

RATE_LIMIT = {"cost": 1, "remaining": 4999}


def graphql(query: str):
    if "projectsV2" in query:
        return {
            "data": {
                "organization": {
                    "projectsV2": {
                        "nodes": [
                            {"number": PROJECT_NUMBER, "title": PROJECT_TITLE, "id": "PVT_coord"}
                        ]
                    }
                },
                "rateLimit": RATE_LIMIT,
            }
        }
    if "fields(first" in query:
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
    if "items(first" in query:
        return {
            "data": {
                "organization": {
                    "projectV2": {
                        "items": {
                            "pageInfo": {"hasNextPage": False, "endCursor": None},
                            "nodes": BOARD_ITEMS,
                        }
                    }
                },
                "rateLimit": RATE_LIMIT,
            }
        }
    # Anything else is a query the fixture was not built for — fail loudly rather than serve an empty
    # answer, which is the exact fail-open the engine exists to end.
    return None


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass  # keep the fixture quiet; the runner asserts on the binary's output, not the server's

    def _send(self, status: int, payload):
        body = json.dumps(payload).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _fail(self, why: str):
        # A request the fixture does not understand is a test bug, and it must not read as an empty
        # result. 500 with a reason, so the runner sees WHY rather than a silent nothing.
        self._send(500, {"errors": [{"message": f"fixture: unhandled — {why}"}]})

    def do_POST(self):
        if self.path.rstrip("/") != "/graphql":
            return self._fail(f"POST {self.path}")
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length).decode()
        try:
            query = json.loads(raw).get("query", "")
        except json.JSONDecodeError:
            return self._fail("POST body is not JSON")
        answer = graphql(query)
        if answer is None:
            return self._fail(f"GraphQL query not recognised: {query[:80]}")
        self._send(200, answer)

    def do_GET(self):
        path = self.path.split("?", 1)[0]

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", path)
        if m:
            return self._send(200, ISSUE_COMMENTS.get(int(m.group(1)), []))

        # THE OFF-BOARD OPEN-ISSUE SCAN (case 25). `scan`/`batch` reserve off-board claims too, so the
        # scheduling pass fetches the repo's open issues (bash's `active_claims` arm B). Every issue here is
        # ON the board, so the scan finds nothing off-board to reserve — the list is served faithfully.
        if re.match(r"^/repos/[^/]+/[^/]+/issues/?$", path):
            return self._send(200, [{"number": n, "state": "open", "body": b}
                                    for n, b in ISSUE_BODIES.items()])

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if m:
            n = int(m.group(1))
            if n not in ISSUE_BODIES:
                return self._send(404, {"message": "Not Found"})
            return self._send(200, {"number": n, "body": ISSUE_BODIES[n]})

        if path.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4999, "limit": 5000}}})

        self._fail(f"GET {path}")


def main():
    # Port 0 → the OS picks a free port; print it on stdout so the runner can read it.
    server = HTTPServer(("127.0.0.1", 0), Handler)
    print(server.server_address[1], flush=True)
    server.serve_forever()


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        sys.exit(0)

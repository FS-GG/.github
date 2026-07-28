#!/usr/bin/env python3
"""`.github#1794` — `Reads.openIssues` over a PAGINATED list, and over elements it cannot read.

Two gaps in one route, both of which fail OPEN, and both of which needed a REAL HTTP server to reach.

## 1. Pagination was promised and never exercised

`Reads.fsi` promises this read is "PAGINATED, AND UNCONDITIONAL", and the promise is KEPT — by
`Transport.Send`, which follows `Link: rel="next"` and merges the arrays. But nothing served this route a
`Link` header: `ApplicationServiceTests`' `ok` hardcodes `NextLink = None`, and every other fixture
returns the whole list in one response. So the merge that keeps the promise was never run for
`openIssues`, and a claim scan that truncated at page one would report every lock past it as FREE.

A `Fake.Recorder` cannot close this. It implements `IGitHubTransport` DIRECTLY, which is the interface
`Transport.Send` — the thing that paginates — sits BEHIND. A unit test asserting on a recorder therefore
proves nothing about pagination no matter how many pages it pretends to serve. This fixture drives the
compiled engine's real `HttpTransport` against real HTTP responses with real `Link` headers, which is the
only arrangement in which the boundary is genuinely crossed.

The board is arranged so the ANSWER DEPENDS ON PAGE TWO:

    page 1   501  Paths: src/Scene/**   no claim              <- the probe
             502  Paths: docs/**        claim page-bystander  <- a live claim that does NOT collide
    page 2   503  Paths: src/Scene/**   claim page2-holder    <- THE COLLISION, and it is here
             504  Paths: tests/**       no claim

    overlap FS.GG.SDD#501 --active   ->  OVERLAP with #503, held by page2-holder

`OIP_NO_LINK=1` serves page one WITHOUT the `Link` header and changes NOTHING else. That is the negative
control: same data, same split, same command — and the verdict flips to DISJOINT. A test that cannot
produce the wrong answer on demand has not shown that it tests the right one.

## 2. An element it could not read was reported as one that DECLARES NOTHING

`TouchSet.parse ""` answers `Undeclared`, and `TouchSet.conflicts` reads `Undeclared` as colliding with
nothing. So reading an absent or ill-typed `body` as `""` ASSERTS "this issue declares nothing" about a
row nobody read — and since `.github#1779` this read is the #353 collision gate's candidate set, where a
false DISJOINT is final: there is no CAS on a file.

WHY THE MALFORMED ELEMENT LIVES ON PAGE TWO, AND WHY THAT IS NOT DECORATION. A hand-written malformed
element in a single-response fixture would be a shape asserted rather than reached. Page two is a
SEPARATE HTTP RESPONSE to a SEPARATE REQUEST, which `Transport.Send` then CONCATENATES into one array
(`mergeArrays`). That is a production route to a heterogeneous array that does not depend on GitHub ever
serving a bad element: pages are served independently, and the body the parser sees is a merge no single
response ever described. `.github#461`'s founding incident is the same class one route over — a truncated
page of comments read as an empty lock.

`OIP_BODY=<issue>:<mode>` mutates ONE element on page two, as it goes onto the wire:

    absent    the element carries no `body` key at all
    illtyped  `"body": {...}` — an object, e.g. a gateway that rewrote the field
    nonumber  the element carries no `number` key — it cannot be IDENTIFIED at all

    OIP_BODY=503:absent    the unreadable row IS held  -> the gate must refuse, never DISJOINT
    OIP_BODY=504:absent    the unreadable row is NOT held -> nothing is reserved, so the scan is
                           unaffected and still answers OVERLAP for #503. This is the PRECISION leg: an
                           anomaly on an issue nobody holds must not red `widen` for the whole repo, and
                           it is what keeps the cost where `.github#1779` measured it.

## 3. The `.github#1792` interaction, asserted rather than inherited

`.github#1792` landed hours before this and made the gate read `Reads.reserver` instead of `Reads.winner`
— a LAPSED lease still reserves. Composed with the above, that means a lapsed claim on a row whose body
could not be read now refuses too. That is a real widening of the refusal surface, produced by the
COMBINATION of two changes and asserted by neither on its own, and #1792's worker asked for it in writing
rather than let it be inherited silently: the `BodyUnread` state does not exist in `main` without this
diff, so the leg can only live here.

`OIP_LAPSED=1` ages every claim marker past the 120-minute lease. It is deliberately independent of
`OIP_BODY`, so the pair gives all four cells: live/lapsed × readable/unreadable.
"""

import json
import os
import re
import sys
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# The board. `overlap --active` reads NO GraphQL since .github#1779 (measured at 0 points), so there is
# no project query here at all — only the issue body, the open-issue list, and one marker read per
# colliding row. If this fixture ever needs a `/graphql` handler, that is a COST REGRESSION and the
# missing-handler 500 is the alarm.
BODIES = {
    501: "Paths: src/Scene/**",
    502: "Paths: docs/**",
    503: "Paths: src/Scene/**",
    504: "Paths: tests/**",
}
PAGE1 = [501, 502]
PAGE2 = [503, 504]

# Live claim markers (a claim marker IS a comment). 501 is the probe and holds nothing; 504 declares a
# touch-set but nobody is holding it — colliding TOKENS are not a reservation.
CLAIMS = {502: "page-bystander", 503: "page2-holder"}

NO_LINK = os.environ.get("OIP_NO_LINK") == "1"

# Age every marker past the 120m lease. `Reads.reserver` (.github#1792) still reserves it; `Reads.winner`
# would not. Independent of OIP_BODY on purpose — the point is the COMBINATION.
LAPSED = os.environ.get("OIP_LAPSED") == "1"

# OIP_BODY=<issue>:<mode>
_mut = os.environ.get("OIP_BODY", "")
MUT_ISSUE, MUT_MODE = (None, None)
if _mut:
    a, _, b = _mut.partition(":")
    MUT_ISSUE, MUT_MODE = int(a), b
    if MUT_MODE not in ("absent", "illtyped", "nonumber"):
        raise SystemExit(f"OIP_BODY: unknown mode {MUT_MODE!r}")
    if MUT_ISSUE not in PAGE2:
        # Deliberate: the whole warrant for this fixture's malformed element is that it arrives on a
        # SEPARATE response and is merged in. Mutating a page-one element would forfeit that and make the
        # shape asserted rather than reached.
        raise SystemExit(f"OIP_BODY: {MUT_ISSUE} is not on page two — see this file's header")


def _now():
    # `updated_at` is what the lease is measured against. Four hours back is comfortably past 120m, and
    # the engine reads the SERVER's clock, so this is the same fact a real lapsed claim presents.
    age = timedelta(hours=-4) if LAPSED else timedelta(0)
    return (datetime.now(timezone.utc) + age).strftime("%Y-%m-%dT%H:%M:%SZ")


def comments(n):
    w = CLAIMS.get(n)
    if not w:
        return []
    return [{"id": 9000 + n, "body": f"<!-- fsgg:claim worker={w} lease=120 -->\nheld",
             "user": {"login": "EHotwagner"}, "updated_at": _now()}]


def element(n):
    """One issue as it appears in the LIST, after any requested mutation."""
    e = {"number": n, "state": "open", "body": BODIES[n]}
    if n == MUT_ISSUE:
        if MUT_MODE == "absent":
            del e["body"]
        elif MUT_MODE == "illtyped":
            e["body"] = {"rewritten": "by something between us and GitHub"}
        elif MUT_MODE == "nonumber":
            del e["number"]
    return e


class H(BaseHTTPRequestHandler):
    # Keep-alive, so the server does not close after every response: HTTP/1.0's close-per-response races
    # the engine's pooling HttpClient and RSTs away a written response (#761). Pairs with
    # ThreadingHTTPServer below — a kept-alive connection would pin a single-threaded server.
    protocol_version = "HTTP/1.1"

    def log_message(self, *a):
        pass

    def _send(self, code, payload, link=None):
        b = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        if link:
            self.send_header("Link", link)
        self.send_header("Content-Length", str(len(b)))
        self.end_headers()
        self.wfile.write(b)

    def do_GET(self):
        path, _, query = self.path.partition("?")

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)/comments$", path)
        if m:
            return self._send(200, comments(int(m.group(1))))

        m = re.match(r"^/repos/([^/]+)/([^/]+)/issues/?$", path)
        if m:
            page = "2" if "page=2" in query else "1"
            if page == "2":
                return self._send(200, [element(n) for n in PAGE2])
            # PAGE ONE. The `Link` header is the whole subject: `Transport.Send` follows `rel="next"` and
            # merges, and withholding it is the only difference the negative control makes. The absolute
            # URL is what GitHub sends and what the engine's follower expects.
            host = self.headers.get("Host", f"127.0.0.1:{PORT}")
            nxt = f'<http://{host}/repos/{m.group(1)}/{m.group(2)}/issues?state=open&per_page=100&page=2>; rel="next"'
            return self._send(200, [element(n) for n in PAGE1], link=None if NO_LINK else nxt)

        m = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if m:
            n = int(m.group(1))
            # The SINGLE-issue route is never mutated: the subject's own touch-set must stay readable, or
            # the command would refuse for a reason that has nothing to do with the leg under test.
            return self._send(200, {"number": n, "body": BODIES[n]}) if n in BODIES \
                else self._send(404, {"message": "Not Found"})

        if path.rstrip("/") == "/rate_limit":
            return self._send(200, {"resources": {"graphql": {"remaining": 4980, "limit": 5000}}})

        self._send(500, {"message": f"unhandled GET {path}"})

    def do_POST(self):
        # No GraphQL handler ON PURPOSE — see the note by BODIES. A 500 here means the collision scan
        # started reading the board again, which is the cost regression .github#1779 measured away.
        try:
            self.rfile.read(int(self.headers.get("Content-Length", 0)))
        except (TypeError, ValueError):
            pass
        self._send(500, {"message": f"unhandled POST {self.path} — this route must cost 0 GraphQL"})


srv = ThreadingHTTPServer(("127.0.0.1", 0), H)
PORT = srv.server_address[1]
print(PORT, flush=True)
srv.serve_forever()

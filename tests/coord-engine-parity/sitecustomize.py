"""Inject explicit, source-bound lightweight route receipts into parity fixture success worlds.

Every parity server is a small standalone Python process.  Repeating a receipt builder in each one
would make the fixture population an unreviewable second protocol, so this import hook wraps each
server handler's JSON sender once.  On a readable issue-comments response it derives the exact issue
body supplied by that server and appends the same current receipt a real agent would record.  Faulted
comment responses stay faulted, and the environment can deliberately request missing or stale evidence
for the fail-closed drivers.

.github#2300 repair 2 added a SECOND hook here, on `do_POST`: the engine's delivery-route search
(`Client.fs`'s `readDeliveryRouteVerdict`/`requireCurrentDeliveryRoute`/`deliveryRouteFact` — verified
these are the ONLY three callers of `Reads.recentCommentBodies`, `grep -rn "recentCommentBodies" src/`)
now reads a BOUNDED GraphQL `comments(last: N)` query instead of the unbounded REST `commentBodies`.
None of the eleven parity servers answer that query shape themselves, and they must not each grow their
own copy of the receipt logic below — the same "unreviewable second protocol" risk the REST hook exists
to avoid, doubled. So this wraps `do_POST` too, using the EXACT SAME `issue_body()`/`route_receipt()`
functions the REST hook already calls: one receipt builder, two transports.

WHY A SINGLE RECEIPT IN THE RESPONSE IS THE WHOLE, CORRECT ANSWER, not a simplification of one:
`readDeliveryRouteVerdict` (`Client.fs:468-471`) does exactly this and nothing else with the list it is
given —

    comments |> List.rev |> List.tryPick (fun comment ->
        if comment.StartsWith(DeliveryRouteMarker + "\n", …) then decode … else None)

— every comment that is not the marker hits `else None` and is discarded. Claim markers and other
chatter are read by a WHOLLY SEPARATE path (`Scan.snapshot`'s marker scan, still REST, unaffected by
this repair — the subject of `.github#2308`), so the bounded GraphQL response never needs to reproduce
a server's other comments to be correct: `[receipt]` when one exists, `[]` when `route_receipt` returns
`None` (`FSGG_PARITY_ROUTE_MODE=missing`) is the complete, exact input the search consumes either way.

FAILS LOUD ON ANYTHING ELSE. This hook recognises exactly one query shape (`comments(last:`) with all
four variables present and a resolvable issue body; every other POST — including a `comments(last:`
query it cannot fully resolve — replays the exact request bytes into a fresh `BytesIO` and hands off to
that server's REAL `do_POST`, unmodified. It never fabricates an empty result for a query it does not
recognise: an empty "no receipt" answer is fail-closed-but-unschedulable by design when the ENGINE
produces it from a genuinely absent marker, and it would be an invisible test-hook bug if this hook
produced the same shape from a query it simply did not understand.
"""

import hashlib
import io
import json
import os
import re
import sys
from http.server import BaseHTTPRequestHandler


BODY_CACHE = {}


def issue_body(module, number):
    bodies = getattr(module, "BODIES", None)
    if isinstance(bodies, dict) and number in bodies:
        return bodies[number]

    issue_bodies = getattr(module, "ISSUE_BODY", None)
    if isinstance(issue_bodies, dict) and number in issue_bodies:
        return issue_bodies[number]

    issues = getattr(module, "ISSUES", None)
    if isinstance(issues, dict) and number in issues:
        item = issues[number]
        if isinstance(item, dict):
            return item.get("body")
    return None


def route_receipt(owner, repo, number, body):
    mode = os.environ.get("FSGG_PARITY_ROUTE_MODE", "current")
    if mode == "missing":
        return None
    revision = hashlib.sha256(body.encode()).hexdigest()
    if mode == "stale":
        revision = "stale-" + revision
    return {
        "id": -number,
        "body": "<!-- fsgg:delivery-route/v1 -->\n" + json.dumps(
            {
                "schema": "fsgg.coord.delivery-route/v1",
                "subject": f"{owner}/{repo}#{number}",
                "subjectRevision": revision,
                "route": "lightweight",
                "agent": "parity-fixture",
                "timestamp": "2026-01-01T00:00:00Z",
                "reasonCodes": ["fixture"],
                "rationale": "Explicit source-bound parity route receipt.",
                "declaredImpacts": ["internal"],
                "observedFacts": ["fixture"],
                "sddWorkId": None,
                "specHome": None,
                "requiredGates": [],
            },
            separators=(",", ":"),
        ),
        "user": {"login": "parity-fixture"},
        "updated_at": "2026-01-01T00:00:00Z",
    }


def wrap_handler(cls):
    original = cls.__dict__.get("_send")
    if original is None or getattr(original, "_fsgg_route_wrapped", False):
        return

    def send(self, *args, **kwargs):
        if len(args) < 2:
            return original(self, *args, **kwargs)
        code, payload = args[0], args[1]
        path = self.path.split("?", 1)[0]
        issue_match = re.match(r"^/repos/[^/]+/[^/]+/issues/(\d+)$", path)
        if issue_match and isinstance(payload, dict) and isinstance(payload.get("body"), str):
            BODY_CACHE[(cls.__module__, int(issue_match.group(1)))] = payload["body"]

        match = re.match(r"^/repos/([^/]+)/([^/]+)/issues/(\d+)/comments$", path)
        if match and isinstance(payload, list):
            owner, repo, number = match.group(1), match.group(2), int(match.group(3))
            body = issue_body(sys.modules[cls.__module__], number) or BODY_CACHE.get((cls.__module__, number))
            receipt = route_receipt(owner, repo, number, body) if isinstance(body, str) else None
            if receipt is not None and not any("fsgg:delivery-route/v1" in str(row.get("body", "")) for row in payload if isinstance(row, dict)):
                payload = [*payload, receipt]
        return original(self, code, payload, *args[2:], **kwargs)

    send._fsgg_route_wrapped = True
    cls._send = send


def wrap_do_post(cls):
    original = cls.__dict__.get("do_POST")
    if original is None or getattr(original, "_fsgg_route_wrapped", False):
        return

    def do_post(self):
        path = self.path.split("?", 1)[0]
        if path.rstrip("/") != "/graphql":
            return original(self)

        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length)
        real_rfile = self.rfile

        try:
            # REPLAY, ALWAYS. Every branch below either handles the request itself or falls through
            # to `original` — and `original` reads its own body from `self.rfile`, so the bytes this
            # wrapper already consumed must be given back before either happens.
            self.rfile = io.BytesIO(raw)

            try:
                body_payload = json.loads(raw.decode())
                query = body_payload.get("query", "")
                variables = body_payload.get("variables") or {}
            except (json.JSONDecodeError, UnicodeDecodeError):
                return original(self)

            if "comments(last:" not in query:
                return original(self)

            owner, repo = variables.get("owner"), variables.get("repo")
            number, last = variables.get("number"), variables.get("last")
            if not isinstance(owner, str) or not isinstance(repo, str) or number is None or last is None:
                # A `comments(last:` query this hook cannot fully address — NOT a synthesized empty
                # result. The real `do_POST` answers (or refuses) exactly as it would have without
                # this hook.
                return original(self)

            number, last = int(number), int(last)
            module = sys.modules[cls.__module__]
            body = issue_body(module, number) or BODY_CACHE.get((cls.__module__, number))
            if not isinstance(body, str):
                return original(self)

            receipt = route_receipt(owner, repo, number, body)
            nodes = [{"body": receipt["body"]}] if receipt is not None else []
            nodes = nodes[-last:] if last > 0 else []

            response = {
                "data": {"repository": {"issue": {"comments": {"nodes": nodes}}}},
                "rateLimit": {"cost": 1, "remaining": 4977},
            }
            return self._send(200, response)
        finally:
            # RESTORE. `protocol_version = "HTTP/1.1"` keeps several of these servers on one kept-alive
            # connection across many requests (`pw_server.py`'s own docstring names the reason, #761) —
            # a `self.rfile` left as the exhausted `BytesIO` above would break every request after this
            # one on the SAME connection, silently, in a way no single request's assertion would name.
            self.rfile = real_rfile

    do_post._fsgg_route_wrapped = True
    cls.do_POST = do_post


original_init_subclass = BaseHTTPRequestHandler.__init_subclass__


def init_subclass(cls, **kwargs):
    original_init_subclass(**kwargs)
    wrap_handler(cls)
    wrap_do_post(cls)


BaseHTTPRequestHandler.__init_subclass__ = classmethod(init_subclass)

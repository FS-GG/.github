"""Inject explicit, source-bound lightweight route receipts into parity fixture success worlds.

Every parity server is a small standalone Python process.  Repeating a receipt builder in each one
would make the fixture population an unreviewable second protocol, so this import hook wraps each
server handler's JSON sender once.  On a readable issue-comments response it derives the exact issue
body supplied by that server and appends the same current receipt a real agent would record.  Faulted
comment responses stay faulted, and the environment can deliberately request missing or stale evidence
for the fail-closed drivers.
"""

import hashlib
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


original_init_subclass = BaseHTTPRequestHandler.__init_subclass__


def init_subclass(cls, **kwargs):
    original_init_subclass(**kwargs)
    wrap_handler(cls)


BaseHTTPRequestHandler.__init_subclass__ = classmethod(init_subclass)

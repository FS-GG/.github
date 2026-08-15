"""Canonical v2 structured-route fixture builder shared by engine integration servers."""

import hashlib
import json
import re

MARKER = "<!-- fsgg:route-decision/v2 -->"

def _frame(value):
    raw = value.encode("utf-8")
    return f"{len(raw)}:{value}"

def _scalar(value):
    return _frame(value or "")

def _strings(values):
    return "".join(_frame(value) for value in values)

def _touch_set(body):
    values = []
    for match in re.finditer(r"(?im)^ {0,3}paths:\s*(.+)$", body):
        values.extend(token for token in re.split(r"[\s,]+", match.group(1).replace("`", "").strip()) if token)
    return values or ["none"]

def route_record(subject, body, agent="fixture-route", rationale="Hermetic structured route fixture."):
    record = {
        "schema": "fsgg.coord.route-decision/v2",
        "subject": subject,
        "revision": 1,
        "previousDigest": None,
        "scope": ["exercise the coordination engine integration fixture"],
        "dependencies": ["none"],
        "touchSet": _touch_set(body),
        "policyVersion": "structured-decisions/1",
        "route": "lightweight",
        "agent": agent,
        "timestamp": "2026-01-01T00:00:00Z",
        "reasonCodes": ["fixture"],
        "rationale": rationale,
        "sddWorkId": None,
        "specHome": None,
        "requiredGates": [],
    }
    fields = [
        _frame(record["schema"]), _frame(record["subject"]), str(record["revision"]), _scalar(record["previousDigest"]),
        _strings(record["scope"]), _strings(record["dependencies"]), _strings(record["touchSet"]), _frame(record["policyVersion"]),
        _frame(record["route"]), _frame(record["agent"]), _frame(record["timestamp"]), _strings(record["reasonCodes"]),
        _frame(record["rationale"]), _scalar(record["sddWorkId"]), _scalar(record["specHome"]), _strings(record["requiredGates"]),
    ]
    record["digest"] = hashlib.sha256("|".join(fields).encode("utf-8")).hexdigest()
    return record

def route_comment(subject, body, agent="fixture-route", rationale="Hermetic structured route fixture."):
    return MARKER + "\n" + json.dumps(route_record(subject, body, agent, rationale), separators=(",", ":"))

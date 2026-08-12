"""Shared machinery between the capture tool (`scripts/record-board-fixture.py`) and the replay
server / harness in this directory (`.github#2401`).

Three things live here because both sides must agree on them byte-for-byte or the whole scheme falls
apart: the CANONICAL KEY a request is matched on (so a recorded entry from `reconcile` can answer the
identical read `ready` or `driver --events` makes, regardless of which command's capture pass first
saw it), the NORMALIZATION that keeps a fixture deterministic across two captures of an unchanged
board (`.github#2401` AC1 — no timestamps-of-capture on the assertion surface), and the REDACTION that
keeps a token out of a committed artifact.
"""

import json
import re

SCHEMA = "fsgg.coord.board-recording/1"

# GitHub's own token shapes (classic `ghp_`, OAuth `gho_`, refresh `ghr_`, user-to-server `ghu_`,
# server-to-server `ghs_`, and the newer `github_pat_` fine-grained form). This is DEFENSE IN DEPTH,
# not the primary guard: the primary guard is structural — the capture proxy never persists a request
# HEADER at all (see `scripts/record-board-fixture.py`'s `_record`), so `Authorization` never reaches
# this code. This pattern exists for the case a token leaks into a BODY (a query string, an echoed
# error message) rather than the header it belongs in.
_TOKEN_PATTERN = re.compile(r"\b(gh[oprsu]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b")


def redact_text(value):
    if not isinstance(value, str):
        return value
    return _TOKEN_PATTERN.sub("<redacted-token>", value)


def redact_body(value):
    """Walk a parsed JSON value and redact any token-shaped string found anywhere in it."""
    if isinstance(value, str):
        return redact_text(value)
    if isinstance(value, dict):
        return {k: redact_body(v) for k, v in value.items()}
    if isinstance(value, list):
        return [redact_body(v) for v in value]
    return value


def canonical_json(obj):
    return json.dumps(obj, sort_keys=True, separators=(",", ":"))


def request_key(method, path, body_text):
    """The shape a captured request is matched on at replay time: verb, path, and a canonicalized
    body. Two requests that ask the SAME question — same GraphQL document and variables, same REST
    body — key identically regardless of capture order or which command asked first. That is what
    lets one merged transcript answer `reconcile`, `ready` AND `driver --events`: each re-issues the
    same underlying board reads independently, and the replay server does not care which of the three
    is asking.
    """
    body_key = ""
    if body_text:
        try:
            body_key = canonical_json(json.loads(body_text))
        except (json.JSONDecodeError, TypeError):
            body_key = body_text
    return f"{method.upper()} {path}\n{body_key}"


# Only headers the engine's own parsing depends on survive into a recorded entry. Everything else —
# `Date`, `ETag`, `X-GitHub-Request-Id`, and every `X-RateLimit-*` header — is dropped rather than
# recorded: those change on every live call even against an UNCHANGED board (a rate-limit counter
# ticks down; a request id is minted fresh), so storing them would break the "two captures of the same
# board produce an identical fixture" requirement. The replay server supplies its own fixed, healthy
# values instead — the same idiom `tests/coord-engine-e2e/stateful_server.py` already uses.
_SAFE_RESPONSE_HEADERS = {"content-type"}


def normalize_response_headers(headers):
    return {k: v for k, v in headers.items() if k.lower() in _SAFE_RESPONSE_HEADERS}


# The one volatile substructure GraphQL responses carry INLINE, in the body rather than a header:
# `data.rateLimit`. `cost`/`remaining`/`used`/`limit`/`resetAt` all move on every live call against an
# unchanged board, for the same reason the headers above do.
_VOLATILE_RATE_LIMIT_FIELDS = {
    "remaining": 4999,
    "cost": 1,
    "used": 1,
    "limit": 5000,
    "resetAt": "1970-01-01T00:00:00Z",
}


def normalize_body(parsed):
    """Recursively replace `rateLimit`'s volatile fields with fixed sentinels. Everything else — board
    status, comments, PR state, the facts this fixture exists to freeze — is left untouched."""
    if isinstance(parsed, dict):
        out = {}
        for k, v in parsed.items():
            if k == "rateLimit" and isinstance(v, dict):
                sentinel = dict(v)
                for field, placeholder in _VOLATILE_RATE_LIMIT_FIELDS.items():
                    if field in sentinel:
                        sentinel[field] = placeholder
                out[k] = sentinel
            else:
                out[k] = normalize_body(v)
        return out
    if isinstance(parsed, list):
        return [normalize_body(v) for v in parsed]
    return parsed


# `driver --events` stamps the wall-clock read time into `renderedAt` and each transition's
# `observedAt`. Nothing else this suite compares carries a capture-time clock reading. Strip these
# before a fixture's expected output is EITHER written or compared — never before only one of the two,
# or a capture would embed a clock reading a later replay run can never reproduce.
_VOLATILE_OUTPUT_KEYS = {"renderedAt", "observedAt"}


def strip_volatile_output(value):
    if isinstance(value, dict):
        return {k: strip_volatile_output(v) for k, v in value.items() if k not in _VOLATILE_OUTPUT_KEYS}
    if isinstance(value, list):
        return [strip_volatile_output(v) for v in value]
    return value


def canonical_entries(entries):
    """Sort a transcript's entries into a stable, content-derived order — never capture order, which
    depends on which command happened to run first and would make the committed diff noisy without
    changing what was actually recorded."""
    def sort_key(entry):
        body_text = json.dumps(entry["requestBody"]) if entry.get("requestBody") is not None else ""
        return request_key(entry["method"], entry["path"], body_text)

    return sorted(entries, key=sort_key)

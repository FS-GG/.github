#!/usr/bin/env python3
"""Bounded, value-free I/O safety for public routine telemetry aggregates."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import stat
import tempfile
from typing import Any

MAX_INPUT_BYTES = 1024 * 1024
MAX_OUTPUT_BYTES = 64 * 1024
PUBLIC_FIELDS = {
    "schema", "repo", "pr", "baseRef", "baseSha", "expectedHead", "observedHead", "head", "outcome", "codeDelivery",
    "publication", "mergeCommit", "attempts", "reason", "unit", "scope", "freshInputTokens",
    "validationDisposition", "coherentValidation",
    "outcomeAt", "observedAt", "telemetryHealth",
    "routineEligibility", "producerInstallation", "producerSource", "deliveryEvidence",
    "cachedInputTokens", "outputTokens", "reasoningTokens", "productiveTokens", "overheadTokens",
    "unclassifiedTokens", "dataStatus", "completeness", "complete", "attemptsExpected", "attemptsObserved",
}
PUBLIC_SCALARS = (str, int, bool, type(None))


def unsafe_path(path: str) -> bool:
    normalized = path.replace("\\", "/").lower()
    try:
        resolved = str(Path(path).resolve(strict=False)).replace("\\", "/").lower()
    except OSError:
        resolved = normalized
    return ("/.codex/sessions/" in normalized or "/.codex/sessions/" in resolved
            or normalized.endswith("/.codex/sessions") or resolved.endswith("/.codex/sessions")
            or normalized.endswith(".jsonl") or "/raw-receipt" in normalized
            or "/raw-receipt" in resolved or "/receipt-store/" in normalized or "/receipt-store/" in resolved)


def read_public_object(path: str) -> tuple[dict[str, Any] | None, str | None]:
    if unsafe_path(path):
        return None, "unsafe-input-path"
    try:
        target = Path(path)
        info = target.stat()
        if not stat.S_ISREG(info.st_mode):
            return None, "input-not-regular"
        if info.st_size > MAX_INPUT_BYTES:
            return None, "input-too-large"
        with target.open("rb") as stream:
            payload = stream.read(MAX_INPUT_BYTES + 1)
        if len(payload) > MAX_INPUT_BYTES:
            return None, "input-too-large"
        value = json.loads(payload.decode("utf-8"))
        if not isinstance(value, dict):
            return None, "input-root-invalid"
        if not public_shape(value):
            return None, "input-fields-invalid"
        return value, None
    except UnicodeDecodeError:
        return None, "input-encoding-invalid"
    except json.JSONDecodeError:
        return None, "input-json-invalid"
    except OSError:
        return None, "input-unreadable"


def public_shape(value: dict[str, Any]) -> bool:
    for key, item in value.items():
        if key not in PUBLIC_FIELDS:
            return False
        if key == "deliveryEvidence":
            if value.get("schema") != "fsgg.routine-delivery/v1" or not evidence_shape(item):
                return False
        elif key == "completeness":
            if not isinstance(item, dict) or not public_shape(item):
                return False
        elif not isinstance(item, PUBLIC_SCALARS):
            return False
    return True


def evidence_shape(value: Any) -> bool:
    """Admit only the bounded public extension, not arbitrary nested telemetry."""
    if not isinstance(value, dict):
        return False
    scalar_fields = {"schema", "scope", "candidate", "base", "deliveredCommit", "deliveredTree",
                     "parity", "observedAt", "assessment"}
    fields = scalar_fields | {"testedCheckout", "changedFiles", "requiredChecks", "run", "exceptions", "issues"}
    if (set(value) - fields or value.get("schema") != "fsgg.routine-delivery-evidence/1"
            or value.get("scope") not in {"github-pr-checkout", "skill-quality-pilot"}
            or value.get("parity") not in {"identical-tree", "different-tree", "not-delivered", "unavailable"}):
        return False
    def scalar(item):
        return item is None or isinstance(item, str) and len(item) <= 4096
    def record(item, names, integers=()):
        return (isinstance(item, dict) and set(item) == set(names)
                and all(type(v) is int and v > 0 if k in integers else scalar(v) for k, v in item.items()))
    if any(not scalar(value[k]) for k in scalar_fields & value.keys()):
        return False
    if "assessment" in value and value["assessment"] not in {"ready", "delivered", "attention"}:
        return False
    for key in ("exceptions", "issues"):
        rows = value.get(key, [])
        if not isinstance(rows, list) or len(rows) > 128 or any(not isinstance(x, str) or len(x) > 256 for x in rows):
            return False
    records = {
        "testedCheckout": ("commit tree kind association run attempt job artifact coverage".split(), ("run", "attempt", "artifact")),
        "run": ("id run_attempt head_sha head_branch path event status conclusion html_url".split(), ("id", "run_attempt")),
    }
    for key, (names, integers) in records.items():
        if value.get(key) is not None and not record(value[key], names, integers):
            return False
    for key, limit, names in (
        ("changedFiles", 100, "filename previous_filename status"),
        ("requiredChecks", 128, "bucket completedAt event link name startedAt state workflow"),
    ):
        rows = value.get(key)
        if rows is not None and (not isinstance(rows, list) or len(rows) > limit
                                 or any(not record(row, names.split()) for row in rows)):
            return False
    return len(json.dumps(value, separators=(",", ":")).encode()) <= MAX_OUTPUT_BYTES


def write_public_report(path: str, report: dict[str, Any], inputs: list[str]) -> str | None:
    if unsafe_path(path):
        return "unsafe-output-path"
    target = Path(path)
    try:
        if target.is_symlink():
            return "output-symlink-refused"
        absolute = target.resolve(strict=False)
        if any(absolute == Path(source).resolve(strict=False) for source in inputs):
            return "input-output-alias"
        payload = (json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n").encode("utf-8")
        if len(payload) > MAX_OUTPUT_BYTES:
            return "output-too-large"
        target.parent.mkdir(parents=True, exist_ok=True)
        fd, temporary = tempfile.mkstemp(prefix=f".{target.name}.", dir=target.parent)
        try:
            with os.fdopen(fd, "wb") as stream:
                stream.write(payload)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, target)
        except BaseException:
            try:
                os.unlink(temporary)
            except OSError:
                pass
            raise
        return None
    except OSError:
        return "output-write-failed"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    value, problem = read_public_object(args.input)
    if problem:
        print(json.dumps({"status": "refused", "code": problem}, separators=(",", ":")))
        return 2
    problem = write_public_report(args.output, value or {}, [args.input])
    print(json.dumps({"status": "written" if problem is None else "refused", "code": problem}, separators=(",", ":")))
    return 0 if problem is None else 2


if __name__ == "__main__":
    raise SystemExit(main())

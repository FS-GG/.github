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
    "schema", "repo", "pr", "expectedHead", "observedHead", "head", "outcome", "codeDelivery",
    "publication", "mergeCommit", "attempts", "reason", "unit", "scope", "freshInputTokens",
    "validationDisposition", "coherentValidation",
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
        if key == "completeness":
            if not isinstance(item, dict) or not public_shape(item):
                return False
        elif not isinstance(item, PUBLIC_SCALARS):
            return False
    return True


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

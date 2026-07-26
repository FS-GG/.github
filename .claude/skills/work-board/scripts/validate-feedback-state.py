#!/usr/bin/env python3
"""Fail-closed activation envelope for an fs-gg-feedback-report cycle."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


FIELDS = ("activation", "phases", "material events", "zero-event reason")


def fail(message: str) -> None:
    print(f"feedback-state: {message}", file=sys.stderr)
    raise SystemExit(1)


def frontmatter(text: str) -> dict[str, str]:
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        fail("report has no opening frontmatter delimiter")
    try:
        end = next(i for i, line in enumerate(lines[1:], 1) if line.strip() == "---")
    except StopIteration:
        fail("report has no closing frontmatter delimiter")
    result: dict[str, str] = {}
    for line in lines[1:end]:
        if ":" in line:
            key, value = line.split(":", 1)
            result[key.strip()] = value.strip()
    return result


def activation_fields(text: str) -> dict[str, str]:
    result: dict[str, str] = {}
    for field in FIELDS:
        hit = re.findall(
            rf"(?mi)^-\s+\*\*{re.escape(field)}:\*\*\s+(.+?)\s*$",
            text,
        )
        if len(hit) != 1:
            fail(f"report must contain exactly one '- **{field}:** ...' activation field")
        result[field] = hit[0].strip()
    return result


def validate_checkpoints(path: Path, cycle: str) -> int:
    if not path.is_file():
        fail(f"checkpoint file is missing: {path}; repair with the feedback checkpoint command")
    count = 0
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        fail(f"checkpoint file cannot be read: {path}: {exc}")
    for number, line in enumerate(lines, 1):
        if not line.strip():
            fail(f"checkpoint file has an empty line at {number}: {path}")
        try:
            row = json.loads(line)
        except json.JSONDecodeError as exc:
            fail(f"checkpoint file has invalid JSON at line {number}: {exc}")
        if not isinstance(row, dict) or row.get("cycle") != cycle:
            fail(f"checkpoint line {number} does not declare cycle {cycle!r}")
        count += 1
    if count == 0:
        fail(f"checkpoint file is empty: {path}")
    return count


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--cycle", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--phases", required=True)
    args = parser.parse_args()

    root = Path(args.root)
    report = root / args.report
    try:
        text = report.read_text(encoding="utf-8")
    except OSError as exc:
        fail(f"schema-v2 report is missing or unreadable: {report}: {exc}")

    meta = frontmatter(text)
    if meta.get("feedbackSchema") != "2":
        fail("report frontmatter must declare feedbackSchema: 2")
    if meta.get("cycle") != args.cycle:
        fail(f"report cycle {meta.get('cycle')!r} does not match expected {args.cycle!r}")

    fields = activation_fields(text)
    if fields["activation"].lower() != "active":
        fail("activation must be 'active'")

    expected_phases = [value.strip() for value in args.phases.split(",") if value.strip()]
    actual_phases = [value.strip() for value in fields["phases"].split(",") if value.strip()]
    if actual_phases != expected_phases:
        fail(f"phases must be {', '.join(expected_phases)} in that order")

    try:
        declared_events = int(fields["material events"])
    except ValueError:
        fail("material events must be a non-negative integer")
    if declared_events < 0:
        fail("material events must be a non-negative integer")

    checkpoint = root / "feedback" / "checkpoints" / f"{args.cycle}.jsonl"
    if declared_events == 0:
        if checkpoint.exists():
            fail(f"material events is 0 but checkpoint file exists: {checkpoint}")
        if fields["zero-event reason"].lower() in {"", "n/a", "none", "none observed."}:
            fail("zero-event reason must explain why no exercised phase produced a material event")
    else:
        actual_events = validate_checkpoints(checkpoint, args.cycle)
        if actual_events != declared_events:
            fail(
                f"material events declares {declared_events}, but {checkpoint} contains {actual_events}"
            )
        if fields["zero-event reason"].lower() not in {"n/a", "not applicable"}:
            fail("zero-event reason must be 'n/a' when material events is non-zero")

    print(
        f"feedback-state: valid cycle {args.cycle}: "
        f"{declared_events} material event(s), report {args.report}"
    )


if __name__ == "__main__":
    main()

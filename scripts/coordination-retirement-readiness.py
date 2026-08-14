#!/usr/bin/env python3
"""Fail-closed M6 retirement-readiness validator.

The coordination-churn roadmap measures health weekly and permits compatibility
retirement only after three consecutive healthy periods with no open successor
issue.  This validator deliberately does not collect or infer evidence: it checks
that a caller-supplied census is complete, consecutive, non-vacuous where the
roadmap requires an observation, and satisfies every stated health measure.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta
import json
from pathlib import Path
import re
import sys


SHA = re.compile(r"^[0-9a-f]{40}$")
RELEASE_OUTCOMES = {"coherent", "visibly-resumable", "no-release-owed"}


def timestamp(value: object, where: str, failures: list[str]) -> datetime | None:
    if not isinstance(value, str) or not value.endswith("Z"):
        failures.append(f"{where}: timestamp must be UTC and end in Z")
        return None
    try:
        return datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError:
        failures.append(f"{where}: invalid timestamp {value!r}")
        return None


def integer(row: dict, name: str, where: str, failures: list[str]) -> int | None:
    value = row.get(name)
    if not isinstance(value, int) or isinstance(value, bool) or value < 0:
        failures.append(f"{where}.{name}: must be a non-negative integer (unmeasured/null does not pass)")
        return None
    return value


def signed_integer(row: dict, name: str, where: str, failures: list[str]) -> int | None:
    value = row.get(name)
    if not isinstance(value, int) or isinstance(value, bool):
        failures.append(f"{where}.{name}: must be an integer (unmeasured/null does not pass)")
        return None
    return value


def validate(document: object) -> list[str]:
    failures: list[str] = []
    if not isinstance(document, dict):
        return ["root must be an object"]
    if document.get("schema_version") != 1:
        failures.append("schema_version must be 1")
    if not SHA.fullmatch(str(document.get("source_sha", ""))):
        failures.append("source_sha must be an exact lowercase 40-hex commit")

    rows = document.get("candidate_periods")
    if not isinstance(rows, list) or len(rows) < 3:
        failures.append("candidate_periods must contain at least three periods")
        rows = []

    parsed: list[tuple[datetime | None, datetime | None]] = []
    for index, row in enumerate(rows):
        where = f"candidate_periods[{index}]"
        if not isinstance(row, dict):
            failures.append(f"{where}: must be an object")
            parsed.append((None, None))
            continue
        start = timestamp(row.get("start"), f"{where}.start", failures)
        end = timestamp(row.get("end"), f"{where}.end", failures)
        parsed.append((start, end))
        if start is not None and end is not None and end - start != timedelta(days=7):
            failures.append(f"{where}: roadmap measurement period must be exactly seven days")

        created = integer(row, "issues_created", where, failures)
        closed = integer(row, "issues_closed", where, failures)
        if created is not None and closed is not None and not created < closed:
            failures.append(f"{where}: issue creation must be below closure ({created} !< {closed})")

        repairs = integer(row, "repair_commits", where, failures)
        statements = integer(row, "statement_only_repairs", where, failures)
        if repairs is not None and statements is not None:
            if statements > repairs:
                failures.append(f"{where}: statement_only_repairs exceeds repair_commits")
            elif statements * 10 >= repairs and repairs != 0:
                failures.append(f"{where}: statement-only repair rate is not below 10%")

        for name, label in (
            ("intent_reversals", "deliberate scheduling intent reversals"),
            ("partial_success_reads", "successful reads later found partial"),
            ("ambiguous_release_states", "ambiguous release states"),
        ):
            value = integer(row, name, where, failures)
            if value is not None and value != 0:
                failures.append(f"{where}: {label} must be zero (got {value})")

        outcomes = row.get("release_outcomes")
        if not isinstance(outcomes, list) or not outcomes:
            failures.append(f"{where}.release_outcomes: require an observed outcome or no-release-owed")
        elif any(value not in RELEASE_OUTCOMES for value in outcomes):
            failures.append(f"{where}.release_outcomes: contains an unsupported outcome")

        for name in (
            "policy_implementations_start", "policy_implementations_end",
            "check_scripts_start", "check_scripts_end", "workflows_start", "workflows_end",
        ):
            integer(row, name, where, failures)
        for name in ("generated_evidence_bytes_delta", "core_and_test_bytes_delta"):
            signed_integer(row, name, where, failures)

        for prefix in ("policy_implementations", "check_scripts", "workflows"):
            before, after = row.get(f"{prefix}_start"), row.get(f"{prefix}_end")
            if isinstance(before, int) and isinstance(after, int) and after > before:
                failures.append(f"{where}: {prefix} increased ({before} -> {after})")

        evidence = row.get("generated_evidence_bytes_delta")
        implementation = row.get("core_and_test_bytes_delta")
        if isinstance(evidence, int) and isinstance(implementation, int):
            if implementation == 0:
                if evidence != 0:
                    failures.append(f"{where}: generated evidence grew while core/tests did not")
            elif evidence >= implementation:
                failures.append(f"{where}: generated evidence did not grow more slowly than core/tests")

        verification = row.get("verification")
        if not isinstance(verification, list) or not verification or not all(
            isinstance(value, str) and value.strip() for value in verification
        ):
            failures.append(f"{where}.verification: require at least one reproducible basis")

    for index in range(1, len(parsed)):
        previous_end, current_start = parsed[index - 1][1], parsed[index][0]
        if previous_end is not None and current_start is not None and previous_end != current_start:
            failures.append(f"candidate_periods[{index}]: periods are not consecutive")

    if rows and all(isinstance(row, dict) for row in rows):
        prefixes = ("policy_implementations", "check_scripts", "workflows")
        values = [rows[0].get(f"{prefix}_start") for prefix in prefixes]
        final = [rows[-1].get(f"{prefix}_end") for prefix in prefixes]
        if all(isinstance(value, int) for value in values + final) and sum(final) >= sum(values):
            failures.append("policy/check/workflow aggregate must trend down across the measured run")

    successors = document.get("same_class_open")
    if not isinstance(successors, list):
        failures.append("same_class_open must be an array")
    elif successors:
        for row in successors:
            if not isinstance(row, dict) or not row.get("url") or not row.get("reason"):
                failures.append("each same_class_open row must carry url and reason")
                break
        failures.append(f"same-class successor census is not empty ({len(successors)} open)")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    try:
        document = json.loads(args.evidence.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"retirement readiness: ERROR: cannot read {args.evidence}: {error}", file=sys.stderr)
        return 1
    failures = validate(document)
    if failures:
        for failure in failures:
            print(f"BLOCKED: {failure}")
        return 1
    print("retirement readiness: PASS — three consecutive healthy weekly periods and no successor issue")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

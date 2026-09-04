#!/usr/bin/env python3
"""Fail-closed validation for a work-roadmap milestone critique artifact."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


CYCLE_RE = re.compile(r"^roadmap-[a-z0-9]+(?:-[a-z0-9]+)*-m[a-z0-9]+(?:-[a-z0-9]+)*$")
COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
REQUIRED_SCOPE = {"requirements", "diff", "tests", "architecture", "roadmap-evidence"}
SEVERITIES = {"blocker", "major", "minor"}
DISPOSITIONS = {"resolved", "follow-up", "unresolved"}
MAX_REPAIR_ROUNDS = 10

# .github#2087 — the bot-driven player journey gate. A journey is evidence only when it was driven
# through the product's real input surface, from the product's real entry point.
ALLOWED_ENTRY_POINTS = {"product-boot"}
ALLOWED_INPUT_SURFACES = {"player-control-messages"}


def nonempty_string(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def nonempty_strings(value: Any) -> bool:
    return isinstance(value, list) and bool(value) and all(nonempty_string(item) for item in value)


def validate_player_journeys(data: dict) -> list[str]:
    """.github#2087 — the bot-driven player journey gate is blocking, never advisory.

    A journey is evidence only when driven through the product's real input surface
    (`input_surface: "player-control-messages"`) starting at the product's real entry point
    (`entry_point: "product-boot"`). Direct `Msg` injection, a test-only API, or a seeded
    mid-game start are rejected even when the journey reports the functionality reached —
    reachability claimed from an unreachable start is the failure mode this gate exists to stop.
    """
    errors: list[str] = []

    game_functionality = data.get("game_functionality")
    if not isinstance(game_functionality, bool):
        errors.append("game_functionality must be a boolean")

    not_ownable = data.get("entry_point_not_test_ownable")
    if not isinstance(not_ownable, bool):
        errors.append("entry_point_not_test_ownable must be a boolean")

    reason = data.get("entry_point_not_test_ownable_reason")
    if not_ownable is True:
        if not nonempty_string(reason):
            errors.append(
                "entry_point_not_test_ownable_reason must be a non-empty string when "
                "entry_point_not_test_ownable is true"
            )
    elif reason is not None:
        errors.append("entry_point_not_test_ownable_reason must be null unless the entry point is not test-ownable")

    uncovered = data.get("uncovered_functionality")
    if not isinstance(uncovered, list) or not all(nonempty_string(item) for item in uncovered):
        errors.append("uncovered_functionality must be a string array (may be empty)")

    journeys = data.get("player_journeys")
    if not isinstance(journeys, list):
        errors.append("player_journeys must be an array")
        journeys = []

    for index, journey in enumerate(journeys):
        prefix = f"player_journeys[{index}]"
        if not isinstance(journey, dict):
            errors.append(f"{prefix} must be an object")
            continue
        if not nonempty_string(journey.get("functionality")):
            errors.append(f"{prefix}.functionality must be a non-empty string")
        if journey.get("entry_point") not in ALLOWED_ENTRY_POINTS:
            errors.append(
                f"{prefix}.entry_point must be one of {sorted(ALLOWED_ENTRY_POINTS)} — a seeded or "
                "mid-game start is not the product's real entry point"
            )
        if journey.get("input_surface") not in ALLOWED_INPUT_SURFACES:
            errors.append(
                f"{prefix}.input_surface must be one of {sorted(ALLOWED_INPUT_SURFACES)} — direct Msg "
                "injection and test-only APIs are not evidence a player can produce"
            )
        if not isinstance(journey.get("reached"), bool):
            errors.append(f"{prefix}.reached must be a boolean")
        if not nonempty_strings(journey.get("evidence")):
            errors.append(f"{prefix}.evidence must be a non-empty string array")

    if game_functionality is True:
        if not_ownable is not True and len(journeys) == 0:
            errors.append(
                "player_journeys must contain at least one entry when game_functionality is true, "
                "unless entry_point_not_test_ownable is true (fail closed, not a silent pass)"
            )
    elif game_functionality is False:
        if len(journeys) != 0:
            errors.append("player_journeys must be empty when game_functionality is false")
        if not_ownable is True:
            errors.append("entry_point_not_test_ownable is only meaningful when game_functionality is true")

    return errors


def validate(data: Any, expected_cycle: str) -> list[str]:
    errors: list[str] = []
    if not isinstance(data, dict):
        return ["artifact root must be a JSON object"]

    if data.get("schema_version") != 3:
        errors.append("schema_version must be 3")
    if data.get("cycle_id") != expected_cycle:
        errors.append(f"cycle_id must equal {expected_cycle!r}")
    for field in ("milestone", "critic"):
        if not nonempty_string(data.get(field)):
            errors.append(f"{field} must be a non-empty string")
    if not COMMIT_RE.fullmatch(str(data.get("initial_reviewed_commit", ""))):
        errors.append("initial_reviewed_commit must be a lowercase 40-character git SHA")

    scope = data.get("scope")
    if not isinstance(scope, list) or set(scope) != REQUIRED_SCOPE or len(scope) != len(REQUIRED_SCOPE):
        errors.append("scope must contain each required review area exactly once")
    if data.get("initial_verdict") not in {"pass", "changes-required"}:
        errors.append("initial_verdict must be pass or changes-required")

    errors.extend(validate_player_journeys(data))

    repair_rounds = data.get("repair_rounds")
    if (
        not isinstance(repair_rounds, int)
        or isinstance(repair_rounds, bool)
        or not 0 <= repair_rounds <= MAX_REPAIR_ROUNDS
    ):
        errors.append(f"repair_rounds must be an integer from 0 through {MAX_REPAIR_ROUNDS}")

    reviewed_commits = data.get("reviewed_commits")
    valid_reviewed_commits = (
        isinstance(reviewed_commits, list)
        and isinstance(repair_rounds, int)
        and not isinstance(repair_rounds, bool)
        and 0 <= repair_rounds <= MAX_REPAIR_ROUNDS
        and len(reviewed_commits) == repair_rounds + 1
        and all(COMMIT_RE.fullmatch(str(commit)) for commit in reviewed_commits)
        and len(set(reviewed_commits)) == len(reviewed_commits)
        and reviewed_commits[0] == data.get("initial_reviewed_commit")
    )
    if not valid_reviewed_commits:
        errors.append(
            "reviewed_commits must be a unique ordered lowercase-SHA chain containing the initial "
            "review plus exactly one commit per repair round"
        )

    confirmation = data.get("confirmation")
    human_escalation = data.get("human_escalation")
    terminal_failure = (
        repair_rounds == MAX_REPAIR_ROUNDS
        and isinstance(confirmation, dict)
        and (
            confirmation.get("verdict") != "pass"
            or confirmation.get("unresolved_blocker_major") != []
        )
    )
    confirmation_unresolved = (
        confirmation.get("unresolved_blocker_major")
        if isinstance(confirmation, dict)
        else None
    )
    escalation_unresolved = (
        human_escalation.get("unresolved_blocker_major")
        if isinstance(human_escalation, dict)
        else None
    )
    terminal_unresolved_ids = (
        set(confirmation_unresolved)
        if repair_rounds == MAX_REPAIR_ROUNDS
        and nonempty_strings(confirmation_unresolved)
        and confirmation_unresolved == escalation_unresolved
        else set()
    )

    findings = data.get("findings")
    finding_ids: set[str] = set()
    blocker_major_ids: set[str] = set()
    unresolved_finding_ids: set[str] = set()
    if not isinstance(findings, list):
        errors.append("findings must be an array")
        findings = []
    for index, finding in enumerate(findings):
        prefix = f"findings[{index}]"
        if not isinstance(finding, dict):
            errors.append(f"{prefix} must be an object")
            continue
        finding_id = finding.get("id")
        if not nonempty_string(finding_id):
            errors.append(f"{prefix}.id must be a non-empty string")
        elif finding_id in finding_ids:
            errors.append(f"{prefix}.id must be unique")
        else:
            finding_ids.add(finding_id)
        severity = finding.get("severity")
        if severity not in SEVERITIES:
            errors.append(f"{prefix}.severity must be blocker, major, or minor")
        elif severity in {"blocker", "major"} and nonempty_string(finding_id):
            blocker_major_ids.add(finding_id)
        if not nonempty_string(finding.get("summary")):
            errors.append(f"{prefix}.summary must be a non-empty string")
        if not nonempty_strings(finding.get("evidence")):
            errors.append(f"{prefix}.evidence must be a non-empty string array")
        disposition = finding.get("disposition")
        if disposition not in DISPOSITIONS:
            errors.append(f"{prefix}.disposition must be resolved, follow-up, or terminal unresolved")
        if disposition == "unresolved":
            if (
                severity not in {"blocker", "major"}
                or not nonempty_string(finding_id)
                or finding_id not in terminal_unresolved_ids
            ):
                errors.append(f"{prefix}.unresolved is allowed only for a matched terminal escalation")
            elif nonempty_string(finding_id):
                unresolved_finding_ids.add(finding_id)
        elif severity in {"blocker", "major"} and disposition != "resolved":
            errors.append(f"{prefix} blocker/major finding must be resolved or terminally escalated")
        if not nonempty_strings(finding.get("resolution_evidence")):
            errors.append(f"{prefix}.resolution_evidence must be a non-empty string array")

    if blocker_major_ids and data.get("initial_verdict") != "changes-required":
        errors.append("initial_verdict must be changes-required when blocker/major findings exist")
    if repair_rounds and data.get("initial_verdict") != "changes-required":
        errors.append("initial_verdict must be changes-required when repair_rounds is non-zero")
    if data.get("initial_verdict") == "changes-required" and not any(
        isinstance(item, dict) and item.get("disposition") in {"resolved", "unresolved"} for item in findings
    ):
        errors.append("changes-required must have at least one resolved finding")
    if repair_rounds == 0 and any(
        isinstance(item, dict) and item.get("disposition") == "resolved" for item in findings
    ):
        errors.append("repair_rounds cannot be 0 when a finding was resolved")

    if not isinstance(confirmation, dict):
        errors.append("confirmation must be an object")
    else:
        if not COMMIT_RE.fullmatch(str(confirmation.get("reviewed_commit", ""))):
            errors.append("confirmation.reviewed_commit must be a lowercase 40-character git SHA")
        elif valid_reviewed_commits and confirmation.get("reviewed_commit") != reviewed_commits[-1]:
            errors.append("confirmation.reviewed_commit must equal the latest reviewed_commits entry")
        if confirmation.get("verdict") != "pass":
            errors.append("confirmation.verdict must be pass")
        if confirmation.get("unresolved_blocker_major") != []:
            errors.append("confirmation.unresolved_blocker_major must be an empty array")

    if terminal_failure and human_escalation is None:
        errors.append(f"human_escalation is required after a failed round {MAX_REPAIR_ROUNDS}")
    if human_escalation is not None:
        if repair_rounds != MAX_REPAIR_ROUNDS:
            errors.append(f"human_escalation is allowed only after repair round {MAX_REPAIR_ROUNDS}")
        if not isinstance(human_escalation, dict):
            errors.append("human_escalation must be null or an object")
        else:
            if valid_reviewed_commits and human_escalation.get("reviewed_commit") != reviewed_commits[-1]:
                errors.append("human_escalation.reviewed_commit must equal the latest reviewed commit")
            if not nonempty_strings(human_escalation.get("unresolved_blocker_major")):
                errors.append("human_escalation.unresolved_blocker_major must be a non-empty string array")
            if not nonempty_string(human_escalation.get("action_required")):
                errors.append("human_escalation.action_required must be a non-empty string")
        if confirmation_unresolved != escalation_unresolved:
            errors.append("confirmation and human_escalation unresolved IDs must match exactly")
        if terminal_unresolved_ids != unresolved_finding_ids:
            errors.append("terminal unresolved IDs must match unresolved blocker/major findings exactly")
        errors.append("human escalation is terminal and cannot satisfy milestone acceptance")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--cycle", required=True)
    parser.add_argument("--artifact", required=True, type=Path)
    args = parser.parse_args()

    if not CYCLE_RE.fullmatch(args.cycle):
        print("invalid cycle id", file=sys.stderr)
        return 2

    expected_artifact = Path("reviews") / "roadmap" / f"{args.cycle}.json"
    if args.artifact != expected_artifact:
        print(f"artifact must be {expected_artifact}", file=sys.stderr)
        return 2

    root = args.root.resolve()
    artifact = (root / args.artifact).resolve()
    try:
        artifact.relative_to(root)
    except ValueError:
        print("artifact must resolve beneath --root", file=sys.stderr)
        return 2

    try:
        data = json.loads(artifact.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        print(f"cannot read critique artifact {artifact}: {exc}", file=sys.stderr)
        return 2

    errors = validate(data, args.cycle)
    if errors:
        for error in errors:
            print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"valid critique state: {artifact}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

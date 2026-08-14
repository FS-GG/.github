#!/usr/bin/env python3
"""Fail-closed M6 retirement-readiness validator.

The coordination-churn roadmap measures health weekly and permits compatibility
retirement only after three consecutive healthy periods with no open successor
issue. Schema checks alone can only block. A positive result additionally requires
an authenticated live GitHub read that binds the source commit, period issue
counts, and the complete fixed-query successor-candidate census.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


SHA = re.compile(r"^[0-9a-f]{40}$")
RELEASE_OUTCOMES = {"coherent", "visibly-resumable", "no-release-owed"}
CANONICAL_SUCCESSOR_QUERIES = [
    'repo:FS-GG/.github is:open is:issue "LIFECYCLE-PROJECTION-LAG"',
    "repo:FS-GG/.github is:open is:issue GraphQL pagination",
    'repo:FS-GG/.github is:open is:issue "partial read"',
    'repo:FS-GG/.github is:open is:issue "feed coherence"',
    'repo:FS-GG/.github is:open is:issue "partial publish"',
    'repo:FS-GG/.github is:open is:issue "body hash"',
    'repo:FS-GG/.github is:open is:issue "delivery-route receipt"',
    'repo:FS-GG/.github is:open is:issue "legacy-only"',
    'repo:FS-GG/.github is:open is:issue "statement" "projection"',
    'repo:FS-GG/.github is:open is:issue "bulky evidence"',
]
PROVENANCE_FIELDS = (
    "repair_commits", "statement_only_repairs", "intent_reversals", "partial_success_reads",
    "ambiguous_release_states", "release_outcomes", "policy_implementations_start",
    "policy_implementations_end", "check_scripts_start", "check_scripts_end", "workflows_start",
    "workflows_end", "generated_evidence_bytes_delta", "core_and_test_bytes_delta",
)
# Preparatory M6 work must not invent a generic executable collector for qualitative
# health measures. The production CLI therefore has no PASS path until a separately
# reviewed canonical collector can replay those observations. Pure validation remains
# testable, but cannot authorize retirement.
ACCEPTANCE_ENABLED = False


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


def validate(document: object, root: Path = Path(".")) -> list[str]:
    failures: list[str] = []
    if not isinstance(document, dict):
        return ["root must be an object"]
    if document.get("schema_version") != 1:
        failures.append("schema_version must be 1")
    if not SHA.fullmatch(str(document.get("source_sha", ""))):
        failures.append("source_sha must be an exact lowercase 40-hex commit")
    measured_at = timestamp(document.get("measured_at"), "measured_at", failures)

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
        if end is not None and measured_at is not None and end > measured_at:
            failures.append(f"{where}: period has not elapsed at measured_at")

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
        provenance = row.get("provenance")
        if not isinstance(provenance, dict):
            failures.append(f"{where}.provenance: require a content-addressed observation artifact")
        else:
            artifact, digest, reproduce = provenance.get("artifact"), provenance.get("sha256"), provenance.get("reproduce")
            if not isinstance(artifact, str) or not artifact or Path(artifact).is_absolute() or ".." in Path(artifact).parts:
                failures.append(f"{where}.provenance.artifact: require a repository-relative non-escaping path")
            elif not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
                failures.append(f"{where}.provenance.sha256: require an exact lowercase SHA-256")
            elif not isinstance(reproduce, list) or not reproduce or not all(isinstance(value, str) and value for value in reproduce):
                failures.append(f"{where}.provenance.reproduce: require a non-empty argv array")
            else:
                try:
                    resolved_root = root.resolve(strict=True)
                    path = (resolved_root / artifact).resolve(strict=True)
                    path.relative_to(resolved_root)
                    payload = path.read_bytes()
                    observed = json.loads(payload)
                    if hashlib.sha256(payload).hexdigest() != digest:
                        failures.append(f"{where}.provenance: artifact SHA-256 mismatch")
                    elif not isinstance(observed, dict):
                        failures.append(f"{where}.provenance: artifact root must be an object")
                    else:
                        identity = {
                            "source_sha": document.get("source_sha"),
                            "measured_at": document.get("measured_at"),
                            "period_id": row.get("id"),
                            "start": row.get("start"),
                            "end": row.get("end"),
                            "reproduce": reproduce,
                        }
                        for field, expected in identity.items():
                            if observed.get(field) != expected:
                                failures.append(f"{where}.provenance: artifact does not bind {field}")
                        for field in PROVENANCE_FIELDS:
                            if observed.get(field) != row.get(field):
                                failures.append(f"{where}.provenance: artifact does not bind {field}")
                except (OSError, json.JSONDecodeError, ValueError) as error:
                    failures.append(f"{where}.provenance: cannot read artifact: {error}")

    for index in range(1, len(parsed)):
        previous_end, current_start = parsed[index - 1][1], parsed[index][0]
        if previous_end is not None and current_start is not None and previous_end != current_start:
            failures.append(f"candidate_periods[{index}]: periods are not consecutive")
        if isinstance(rows[index - 1], dict) and isinstance(rows[index], dict):
            for prefix in ("policy_implementations", "check_scripts", "workflows"):
                previous = rows[index - 1].get(f"{prefix}_end")
                current = rows[index].get(f"{prefix}_start")
                if isinstance(previous, int) and isinstance(current, int) and previous != current:
                    failures.append(
                        f"candidate_periods[{index}]: {prefix} snapshot is discontinuous "
                        f"({previous} != {current})"
                    )

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
    queries = document.get("successor_queries")
    census = document.get("successor_census")
    if queries != CANONICAL_SUCCESSOR_QUERIES:
        failures.append("successor_queries must exactly equal the validator's canonical query universe")
    if not isinstance(census, list) or not census:
        failures.append("successor_census must be a non-empty classified candidate array")
    else:
        urls: list[str] = []
        blocking: list[str] = []
        for index, row in enumerate(census):
            if not isinstance(row, dict) or not isinstance(row.get("url"), str) or row.get("disposition") not in ("blocking", "not-same-class") or not row.get("reason"):
                failures.append(f"successor_census[{index}]: require url, blocking|not-same-class disposition, and reason")
                continue
            urls.append(row["url"])
            if row["disposition"] == "blocking":
                blocking.append(row["url"])
        if len(set(urls)) != len(urls):
            failures.append("successor_census contains duplicate URLs")
        if isinstance(successors, list):
            declared = sorted(row.get("url") for row in successors if isinstance(row, dict))
            if declared != sorted(blocking):
                failures.append("same_class_open does not equal the blocking successor census rows")
    return failures


def search(query: str) -> list[dict]:
    command = ["gh", "api", "--method", "GET", "search/issues", "-f", f"q={query}",
               "-f", "per_page=100", "--paginate", "--slurp"]
    result = subprocess.run(command, check=True, text=True, capture_output=True)
    pages = json.loads(result.stdout)
    return [item for page in pages for item in page.get("items", [])]


def collect_live(document: dict) -> dict:
    source_sha = document["source_sha"]
    subprocess.run(
        ["gh", "api", f"repos/FS-GG/.github/commits/{source_sha}"],
        check=True, text=True, capture_output=True,
    )
    periods = []
    for row in document["candidate_periods"]:
        start = datetime.fromisoformat(row["start"][:-1] + "+00:00")
        end = datetime.fromisoformat(row["end"][:-1] + "+00:00")
        date_range = f"{start.date()}..{end.date()}"
        created = search(f"org:FS-GG created:{date_range} type:issue")
        closed = search(f"org:FS-GG closed:{date_range} type:issue")
        periods.append({
            "id": row["id"],
            "issues_created": sum(start <= datetime.fromisoformat(item["created_at"].replace("Z", "+00:00")) < end for item in created),
            "issues_closed": sum(item.get("closed_at") is not None and start <= datetime.fromisoformat(item["closed_at"].replace("Z", "+00:00")) < end for item in closed),
        })
    urls = sorted({item["html_url"] for query in CANONICAL_SUCCESSOR_QUERIES for item in search(query)})
    return {"source_sha": source_sha, "periods": periods, "successor_urls": urls}


def validate_live(document: dict, observed: object) -> list[str]:
    if not isinstance(observed, dict):
        return ["live observation must be an object"]
    failures: list[str] = []
    if observed.get("source_sha") != document.get("source_sha"):
        failures.append("live source commit does not match source_sha")
    expected_periods = [
        {"id": row.get("id"), "issues_created": row.get("issues_created"), "issues_closed": row.get("issues_closed")}
        for row in document.get("candidate_periods", []) if isinstance(row, dict)
    ]
    if observed.get("periods") != expected_periods:
        failures.append("live GitHub period counts do not match candidate_periods")
    expected_urls = sorted(row.get("url") for row in document.get("successor_census", []) if isinstance(row, dict))
    if observed.get("successor_urls") != expected_urls:
        failures.append("live fixed-query successor results do not match successor_census")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--live-github", action="store_true")
    parser.add_argument("--root", type=Path, default=Path("."))
    args = parser.parse_args()
    try:
        document = json.loads(args.evidence.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"retirement readiness: ERROR: cannot read {args.evidence}: {error}", file=sys.stderr)
        return 1
    failures = validate(document, args.root)
    if not failures:
        try:
            if args.live_github:
                failures.extend(validate_live(document, collect_live(document)))
                if not failures and not ACCEPTANCE_ENABLED:
                    failures.append(
                        "production retirement acceptance is disabled until a reviewed canonical "
                        "collector independently replays every non-GitHub health measure"
                    )
            else:
                failures.append("positive readiness requires --live-github authenticated evidence")
        except (OSError, json.JSONDecodeError, subprocess.CalledProcessError, KeyError, ValueError) as error:
            failures.append(f"live GitHub evidence could not be established: {error}")
    if failures:
        for failure in failures:
            print(f"BLOCKED: {failure}")
        return 1
    print("retirement readiness: PASS — three consecutive healthy weekly periods and no successor issue")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

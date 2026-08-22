#!/usr/bin/env python3
"""Derive the seven roadmap-health results from raw records and exact Git objects."""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
import sys
from datetime import datetime, timedelta
from pathlib import Path

IDS = (
    "issue-flow",
    "behaviourless-repairs",
    "scheduling-intent",
    "complete-reads",
    "release-coherence",
    "artifact-trend",
    "evidence-growth",
)
ROOT = Path(__file__).resolve().parents[1]
DURABLE_REF = re.compile(
    r"https://github\.com/[^/]+/[^/]+/(?:issues/[1-9][0-9]*(?:#issuecomment-[1-9][0-9]*)?|actions/runs/[1-9][0-9]*|releases/tag/[^/#?]+)"
)


def fail(message: str) -> None:
    raise ValueError(message)


def utc(value: object) -> datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        fail("timestamps must be RFC3339 UTC")
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        fail("timestamps must be RFC3339 UTC")
    if parsed.utcoffset() != timedelta(0):
        fail("timestamps must be RFC3339 UTC")
    return parsed


def canonical_sha256(value: object) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(encoded).hexdigest()


def repository_path(repository: Path, relative: object) -> Path:
    if not isinstance(relative, str) or not relative:
        fail("sourceBoundary.snapshotPath must be a nonempty repository-relative path")
    root = repository.resolve()
    path = (root / relative).resolve()
    try:
        path.relative_to(root)
    except ValueError:
        fail("sourceBoundary.snapshotPath must stay inside the repository")
    return path


def read_fixture(path: Path, repository: Path = ROOT) -> dict:
    try:
        data = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read health fixture {path}: {error}")
    if not isinstance(data, dict) or data.get("schema") != "fsgg.coord.roadmap-health-input/v2":
        fail("fixture schema must be fsgg.coord.roadmap-health-input/v2")
    window = data.get("window", {})
    if (
        not isinstance(window, dict)
        or set(window) != {"start", "end"}
        or utc(window.get("start")) >= utc(window.get("end"))
        or utc(window["end"]) - utc(window["start"]) != timedelta(days=21)
    ):
        fail("window must be one ordered 21-day UTC interval")
    boundary = data.get("sourceBoundary")
    if (
        not isinstance(boundary, dict)
        or set(boundary) != {"repository", "query", "asOf", "method", "snapshotPath", "recordsSha256"}
        or not all(isinstance(boundary.get(key), str) and boundary[key] for key in boundary)
        or not re.fullmatch(r"[0-9a-f]{64}", boundary["recordsSha256"])
        or utc(boundary["asOf"]) < utc(window["end"])
    ):
        fail("sourceBoundary must bind a complete post-window raw issue census")
    snapshot_path = repository_path(repository, boundary["snapshotPath"])
    try:
        snapshot = json.loads(snapshot_path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read issue census snapshot {snapshot_path}: {error}")
    if (
        not isinstance(snapshot, dict)
        or set(snapshot) != {"schema", "records"}
        or snapshot.get("schema") != "fsgg.coord.issue-census/v1"
        or not isinstance(snapshot.get("records"), list)
    ):
        fail("issue census must contain a typed raw record list")
    records = snapshot["records"]
    if canonical_sha256(records) != boundary["recordsSha256"]:
        fail("issue census raw-record digest does not match sourceBoundary")
    as_of = utc(boundary["asOf"])
    seen: set[int] = set()
    for record in records:
        if not isinstance(record, dict) or set(record) != {"number", "createdAt", "closedAt"}:
            fail("issue census records must contain exactly number, createdAt, and closedAt")
        number = record["number"]
        if not isinstance(number, int) or isinstance(number, bool) or number <= 0 or number in seen:
            fail("issue census issue numbers must be unique positive integers")
        seen.add(number)
        created = utc(record["createdAt"])
        closed_value = record["closedAt"]
        closed = None if closed_value is None else utc(closed_value)
        if created > as_of or (closed is not None and (closed < created or closed > as_of)):
            fail("issue census timestamps must follow creation/closure/as-of semantics")
    measures = data.get("measures")
    if not isinstance(measures, dict) or set(measures) != set(IDS):
        fail("fixture must contain exactly the seven typed measure inputs")
    for identifier in ("issue-flow", "behaviourless-repairs", "artifact-trend", "evidence-growth"):
        if measures[identifier] != {}:
            fail(f"{identifier} accepts no asserted summary values")
    git_boundary = data.get("gitBoundary")
    if not isinstance(git_boundary, dict) or set(git_boundary) != {"base", "head"}:
        fail("gitBoundary must contain exact base and head revisions")
    data["_issueRecords"] = records
    return data


def row(identifier: str, verdict: str, reason: str, value=None) -> dict:
    result = {"id": identifier, "verdict": verdict, "reason": reason}
    if value is not None:
        result["value"] = value
    return result


def git(repository: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", "-C", str(repository), *args], text=True, capture_output=True
    )
    if completed.returncode != 0:
        fail(f"git {' '.join(args)} failed: {completed.stderr.strip()}")
    return completed.stdout


def resolve_commit(repository: Path, value: object, field: str) -> str:
    if not isinstance(value, str) or not value.strip():
        fail(f"{field} must be a nonempty resolvable Git revision")
    resolved = git(repository, "rev-parse", "--verify", f"{value}^{{commit}}").strip()
    if not re.fullmatch(r"[0-9a-f]{40}", resolved):
        fail(f"{field} must resolve to one exact commit")
    return resolved


def tree_count(repository: Path, revision: str, prefix: str, matcher) -> int:
    paths = git(repository, "ls-tree", "-r", "--name-only", revision, "--", prefix).splitlines()
    return sum(1 for path in paths if matcher(path))


def net_lines(repository: Path, base: str, head: str, paths: tuple[str, ...]) -> dict:
    additions = deletions = 0
    for line in git(repository, "diff", "--numstat", base, head, "--", *paths).splitlines():
        fields = line.split("\t", 2)
        if len(fields) != 3 or fields[0] == "-" or fields[1] == "-":
            fail(f"line census for {','.join(paths)} encountered an unreadable or binary diff")
        additions += int(fields[0])
        deletions += int(fields[1])
    return {
        "paths": list(paths),
        "method": "git diff --numstat; net = additions - deletions",
        "additions": additions,
        "deletions": deletions,
        "net": additions - deletions,
    }


def incident_measure(data: dict, identifier: str, label: str) -> dict:
    value = data["measures"][identifier]
    if not isinstance(value, dict) or set(value) != {"incidents", "census"}:
        fail(f"{identifier} must contain typed incidents and census")
    incidents, census = value["incidents"], value["census"]
    if not isinstance(incidents, list) or not isinstance(census, dict):
        fail(f"{identifier} incidents/census must be typed")
    if (
        set(census) != {"complete", "asOf", "method"}
        or not isinstance(census.get("complete"), bool)
        or not isinstance(census.get("method"), str)
        or not census["method"].strip()
        or utc(census.get("asOf")) < utc(data["window"]["end"])
    ):
        fail(f"{identifier}.census must be a typed post-window census")
    for incident in incidents:
        if (
            not isinstance(incident, dict)
            or set(incident) != {"ref", "date"}
            or not isinstance(incident.get("ref"), str)
            or DURABLE_REF.fullmatch(incident["ref"]) is None
            or not (utc(data["window"]["start"]) <= utc(incident.get("date")) < utc(data["window"]["end"]))
        ):
            fail(f"{identifier} incidents require accountable durable refs and dates inside the window")
    if incidents:
        return row(identifier, "violated", f"Derived from {len(incidents)} typed {label} incident(s).", incidents)
    if census["complete"]:
        return row(identifier, "met", f"Derived from a complete typed {label} census.", census)
    return row(identifier, "unverified", f"Incident register is empty, but the typed {label} census is incomplete.", census)


def report(data: dict, repository: Path = ROOT) -> dict:
    start = utc(data["window"]["start"])
    records = data.get("_issueRecords")
    if not isinstance(records, list):
        fail("raw issue census was not loaded and validated")
    periods = []
    for index in range(3):
        period_start = start + timedelta(days=index * 7)
        period_end = period_start + timedelta(days=7)
        periods.append(
            {
                "start": period_start.isoformat().replace("+00:00", "Z"),
                "end": period_end.isoformat().replace("+00:00", "Z"),
                "opened": sum(1 for issue in records if period_start <= utc(issue["createdAt"]) < period_end),
                "closed": sum(1 for issue in records if issue["closedAt"] is not None and period_start <= utc(issue["closedAt"]) < period_end),
            }
        )
    issue = row(
        "issue-flow",
        "met" if all(period["opened"] < period["closed"] for period in periods) else "violated",
        f"Derived {len(records)} raw issue records into exact half-open contiguous UTC windows.",
        periods,
    )
    retired = row("behaviourless-repairs", "retired", "Retired 2026-08-22 by operator-delegated host: no authoritative behaviour-changing classifier exists.")

    boundary = data["gitBoundary"]
    base = resolve_commit(repository, boundary.get("base"), "gitBoundary.base")
    head = resolve_commit(repository, boundary.get("head"), "gitBoundary.head")
    checks = {
        "baseline": tree_count(repository, base, "scripts", lambda path: Path(path).name.startswith("check-")),
        "current": tree_count(repository, head, "scripts", lambda path: Path(path).name.startswith("check-")),
        "method": "git ls-tree -r --name-only REV -- scripts; count every file whose basename starts check-",
    }
    workflows = {
        "baseline": tree_count(repository, base, ".github/workflows", lambda path: True),
        "current": tree_count(repository, head, ".github/workflows", lambda path: True),
        "method": "git ls-tree -r --name-only REV -- .github/workflows; count every file",
    }
    artifact_value = {
        "base": base,
        "head": head,
        "policyImplementations": {"verdict": "unverified", "reason": "No authoritative independent-policy-implementation enumerator is defined."},
        "checks": checks,
        "workflows": workflows,
    }
    artifact = row(
        "artifact-trend",
        "violated" if checks["current"] >= checks["baseline"] or workflows["current"] >= workflows["baseline"] else "unverified",
        "Known exact Git-derived check/workflow counters did not both decline; policy-implementation count remains unverified.",
        artifact_value,
    )
    generated = net_lines(repository, base, head, ("work", "readiness"))
    implementation_and_tests = net_lines(repository, base, head, ("src", "tests"))
    evidence_value = {
        "base": base,
        "head": head,
        "generatedEvidence": generated,
        "implementationAndTests": implementation_and_tests,
    }
    evidence = row(
        "evidence-growth",
        "met" if generated["net"] < implementation_and_tests["net"] else "violated",
        "Derived both net line counts from git diff --numstat at one exact boundary.",
        evidence_value,
    )
    source = dict(data["sourceBoundary"])
    source["resultCount"] = len(records)
    return {
        "schema": "fsgg.coord.roadmap-health/v2",
        "window": data["window"],
        "sourceBoundary": source,
        "measures": [
            issue,
            retired,
            incident_measure(data, "scheduling-intent", "scheduling reversal"),
            incident_measure(data, "complete-reads", "partial-read"),
            incident_measure(data, "release-coherence", "ambiguous release"),
            artifact,
            evidence,
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--repo", type=Path, default=ROOT)
    parser.add_argument("--format", choices=("json",), default="json")
    args = parser.parse_args()
    try:
        print(json.dumps(report(read_fixture(args.fixture, args.repo), args.repo), indent=2, sort_keys=True))
    except ValueError as error:
        print(f"report-roadmap-health: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

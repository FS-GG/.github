#!/usr/bin/env python3
"""Collect the canonical weekly health evidence for coordination compatibility retirement.

The collector owns the windows and every measurement.  Its output may be consumed by
``coordination-retirement-readiness.py``; callers choose only the repository and output
directory, never a verdict, period, count, or classification.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys


REPOSITORY = "FS-GG/.github"
FIRST_PERIOD = datetime(2026, 8, 17, tzinfo=timezone.utc)
PERIODS = 3
REPAIR_SUBJECT = re.compile(r"^(?:fix|docs)(?:\([^)]+\))?!?:", re.IGNORECASE)
BEHAVIOUR_PREFIXES = ("src/", "tests/", "scripts/", ".github/", "policy/")
GENERATED_PREFIXES = ("docs/reports/evidence/", "readiness/", "work/")
CORE_PREFIXES = ("src/", "tests/")
EVENT_QUERIES = {
    "intent_reversals": 'repo:FS-GG/.github is:issue label:"health/intent-reversal"',
    "partial_success_reads": 'repo:FS-GG/.github is:issue label:"health/partial-success-read"',
}
SUCCESSOR_QUERIES = [
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
# Every non-blocking result is an explicit, reviewed disposition.  A newly returned
# issue is blocking by default; query drift can therefore never silently authorize M6.
NOT_SAME_CLASS = {
    54: "Dependency dashboard; its body only aggregates unrelated query terms.",
    1858: "Claim-lock executor identity defect, outside the M1-M5 compatibility boundaries.",
    2130: "Broader deterministic-protocol epic, not an M6 compatibility implementation.",
    2230: "Routes-plugin dispatch slice, not legacy structured-decision evidence.",
    2249: "Receiver engine-pin drift, not a compatibility reader or writer.",
    2381: "Standing release-debt measurement, not a superseded publication implementation.",
    2551: "Generic gate-inversion discipline, not a compatibility path.",
    2555: "Test-selector vocabulary defect, outside M6 compatibility scope.",
    2556: "Unwired fixtures, outside M6 compatibility scope.",
    2557: "Review predicate mutation coverage, not the v1 review reader.",
    2584: "Root-cause operating-process proposal, not compatibility retirement.",
}
KNOWN_BLOCKERS = {
    2569: "Private raw-GraphQL compatibility shims remain open work.",
}


def run(argv: list[str], root: Path, *, binary: bool = False) -> str | bytes:
    completed = subprocess.run(argv, cwd=root, check=True, capture_output=True,
                               text=not binary)
    return completed.stdout


def gh_json(args: list[str], root: Path) -> object:
    return json.loads(str(run(["gh", "api", *args], root)))


def search(query: str, root: Path) -> list[dict]:
    pages = gh_json(["--method", "GET", "search/issues", "-f", f"q={query}",
                     "-f", "per_page=100", "--paginate", "--slurp"], root)
    assert isinstance(pages, list)
    return [item for page in pages for item in page.get("items", [])]


def iso(value: datetime) -> str:
    return value.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def observed_at(value: str | None) -> datetime | None:
    if not value:
        return None
    return datetime.fromisoformat(value.replace("Z", "+00:00"))


def exact_windows(now: datetime) -> list[tuple[datetime, datetime]]:
    windows = [(FIRST_PERIOD + timedelta(days=7 * i),
                FIRST_PERIOD + timedelta(days=7 * (i + 1))) for i in range(PERIODS)]
    if windows[-1][1] > now:
        raise ValueError(f"three canonical UTC weeks have not elapsed; earliest collection is {iso(windows[-1][1])}")
    return windows


def first_parent_sha(root: Path, source_sha: str, instant: datetime) -> str:
    value = str(run(["git", "rev-list", "--first-parent", "-1", f"--before={iso(instant)}",
                     source_sha], root)).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", value):
        raise ValueError(f"no first-parent commit exists at {iso(instant)}")
    return value


def tree_paths(root: Path, sha: str) -> list[str]:
    return str(run(["git", "ls-tree", "-r", "--name-only", sha], root)).splitlines()


def tree_bytes(root: Path, sha: str, prefixes: tuple[str, ...]) -> int:
    total = 0
    for line in str(run(["git", "ls-tree", "-rl", sha], root)).splitlines():
        metadata, path = line.split("\t", 1)
        if path.startswith(prefixes):
            size = metadata.rsplit(" ", 1)[-1]
            if size.isdigit():
                total += int(size)
    return total


def inventory(root: Path, sha: str) -> dict[str, int]:
    paths = tree_paths(root, sha)
    return {
        "policy_implementations": sum(path.startswith("policy/") and path.endswith((".json", ".py", ".yml", ".yaml")) for path in paths),
        "check_scripts": sum(path.startswith("scripts/check-") and not path.endswith(".pyc") for path in paths),
        "workflows": sum(path.startswith(".github/workflows/") and path.endswith((".yml", ".yaml")) for path in paths),
    }


def commit_measure(root: Path, start_sha: str, end_sha: str) -> tuple[int, int, list[dict]]:
    commits = str(run(["git", "rev-list", "--first-parent", "--reverse",
                       f"{start_sha}..{end_sha}"], root)).splitlines()
    rows: list[dict] = []
    for commit in commits:
        subject = str(run(["git", "show", "-s", "--format=%s", commit], root)).strip()
        if not REPAIR_SUBJECT.match(subject):
            continue
        paths = str(run(["git", "diff-tree", "--no-commit-id", "--name-only", "-r",
                         commit], root)).splitlines()
        statement_only = bool(paths) and not any(path.startswith(BEHAVIOUR_PREFIXES) for path in paths)
        rows.append({"sha": commit, "subject": subject, "paths": paths,
                     "statement_only": statement_only})
    return len(rows), sum(row["statement_only"] for row in rows), rows


def period_items(query: str, field: str, start: datetime, end: datetime, root: Path) -> list[dict]:
    date_range = f"{start.date()}..{end.date()}"
    rows = search(f"{query} {field.split('_')[0]}:{date_range}", root)
    return [item for item in rows if (stamp := observed_at(item.get(field))) is not None and start <= stamp < end]


def issue_counts(start: datetime, end: datetime, root: Path) -> tuple[list[dict], list[dict]]:
    created = period_items("org:FS-GG type:issue", "created_at", start, end, root)
    closed = period_items("org:FS-GG type:issue", "closed_at", start, end, root)
    return created, closed


def health_events(name: str, start: datetime, end: datetime, root: Path) -> list[dict]:
    # The labels are the machine-owned incident vocabulary.  Free-form issue wording is
    # deliberately ignored; an absent label cannot be supplied through collector input.
    return period_items(EVENT_QUERIES[name], "created_at", start, end, root)


def release_outcomes(start: datetime, end: datetime, root: Path) -> tuple[list[str], list[dict]]:
    releases = gh_json(["--method", "GET", f"repos/{REPOSITORY}/releases", "-f", "per_page=100", "--paginate", "--slurp"], root)
    flattened = [row for page in releases for row in page]
    in_period = [row for row in flattened if (stamp := observed_at(row.get("published_at"))) is not None and start <= stamp < end]
    components: dict[str, set[str]] = {}
    coherent: set[str] = set()
    for row in in_period:
        tag = row.get("tag_name", "")
        match = re.fullmatch(r"(coord-engine|kit|drivers)/v(.+)", tag)
        if match:
            components.setdefault(match.group(2), set()).add(match.group(1))
        match = re.fullmatch(r"coherent-set/v(.+)", tag)
        if match and not row.get("draft") and not row.get("prerelease"):
            coherent.add(match.group(1))
    ambiguous = sorted(version for version in components if version not in coherent)
    outcomes = ["coherent"] if coherent else []
    if ambiguous:
        outcomes.append("ambiguous")
    if not components and not coherent:
        outcomes.append("no-release-owed")
    details = [{"version": version, "components": sorted(parts),
                "coherent_set_release": version in coherent} for version, parts in sorted(components.items())]
    return outcomes, details


def successor_census(root: Path) -> list[dict]:
    candidates: dict[int, str] = {}
    for query in SUCCESSOR_QUERIES:
        for item in search(query, root):
            candidates[int(item["number"])] = item["html_url"]
    rows = []
    for number, url in sorted(candidates.items()):
        if number in NOT_SAME_CLASS:
            rows.append({"url": url, "disposition": "not-same-class", "reason": NOT_SAME_CLASS[number]})
        else:
            rows.append({"url": url, "disposition": "blocking",
                         "reason": KNOWN_BLOCKERS.get(number, "Unclassified fixed-query result; fail closed pending reviewed disposition.")})
    return rows


def collect(root: Path, output: Path, now: datetime) -> Path:
    run(["gh", "api", "user"], root)
    source_sha = str(run(["git", "rev-parse", "HEAD"], root)).strip()
    remote_sha = gh_json([f"repos/{REPOSITORY}/commits/main"], root)["sha"]
    if source_sha != remote_sha:
        raise ValueError(f"HEAD {source_sha} is not current authenticated {REPOSITORY} main {remote_sha}")
    windows = exact_windows(now)
    prose_check = subprocess.run(["python3", "scripts/check-prose-citations.py", "--root", "."],
                                 cwd=root, text=True, capture_output=True)
    if prose_check.returncode != 0:
        raise ValueError("the landed prose-citation boundary is red; statement classification is not authoritative")

    try:
        output_relative = output.relative_to(root)
    except ValueError as error:
        raise ValueError("output directory must be inside the repository") from error
    output.mkdir(parents=True, exist_ok=True)
    rows = []
    for index, (start, end) in enumerate(windows, 1):
        start_sha, end_sha = first_parent_sha(root, source_sha, start), first_parent_sha(root, source_sha, end)
        created, closed = issue_counts(start, end, root)
        repairs, statements, commit_rows = commit_measure(root, start_sha, end_sha)
        reversals = health_events("intent_reversals", start, end, root)
        partials = health_events("partial_success_reads", start, end, root)
        release_values, release_rows = release_outcomes(start, end, root)
        before, after = inventory(root, start_sha), inventory(root, end_sha)
        ambiguous = int("ambiguous" in release_values)
        outcomes = [value for value in release_values if value != "ambiguous"] or ["visibly-resumable"]
        reproduce = ["python3", "scripts/coordination-health-collector.py", "--root", ".",
                     "--output-dir", str(output_relative)]
        observation = {
            "schema_version": 1, "source_sha": source_sha, "measured_at": iso(now),
            "period_id": f"week-{index}", "start": iso(start), "end": iso(end),
            "start_sha": start_sha, "end_sha": end_sha,
            "issues_created": len(created), "issues_closed": len(closed),
            "repair_commits": repairs, "statement_only_repairs": statements,
            "intent_reversals": len(reversals), "partial_success_reads": len(partials),
            "ambiguous_release_states": ambiguous, "release_outcomes": outcomes,
            "policy_implementations_start": before["policy_implementations"],
            "policy_implementations_end": after["policy_implementations"],
            "check_scripts_start": before["check_scripts"], "check_scripts_end": after["check_scripts"],
            "workflows_start": before["workflows"], "workflows_end": after["workflows"],
            "generated_evidence_bytes_delta": tree_bytes(root, end_sha, GENERATED_PREFIXES) - tree_bytes(root, start_sha, GENERATED_PREFIXES),
            "core_and_test_bytes_delta": tree_bytes(root, end_sha, CORE_PREFIXES) - tree_bytes(root, start_sha, CORE_PREFIXES),
            "reproduce": reproduce,
            "raw": {"created": created, "closed": closed, "repair_classification": commit_rows,
                    "intent_reversal_events": reversals, "partial_success_events": partials,
                    "release_classification": release_rows,
                    "prose_citation_gate": prose_check.stdout.strip()},
        }
        payload = json.dumps(observation, sort_keys=True, separators=(",", ":")).encode()
        digest = hashlib.sha256(payload).hexdigest()
        artifact = output / f"week-{index}-{digest}.json"
        artifact.write_bytes(payload)
        row = {key: observation[key] for key in (
            "period_id", "start", "end", "issues_created", "issues_closed", "repair_commits",
            "statement_only_repairs", "intent_reversals", "partial_success_reads",
            "ambiguous_release_states", "release_outcomes", "policy_implementations_start",
            "policy_implementations_end", "check_scripts_start", "check_scripts_end",
            "workflows_start", "workflows_end", "generated_evidence_bytes_delta",
            "core_and_test_bytes_delta")}
        row["id"] = row.pop("period_id")
        row["verification"] = ["canonical collector schema 1", prose_check.stdout.strip()]
        relative = artifact.relative_to(root)
        row["provenance"] = {"artifact": str(relative), "sha256": digest, "reproduce": reproduce}
        rows.append(row)

    census = successor_census(root)
    document = {
        "schema_version": 1,
        "cycle_id": "roadmap-coordination-churn-redesign-m6-retire-compatibility",
        "measured_at": iso(now), "source_sha": source_sha, "verdict": "candidate",
        "collector": {"schema_version": 1, "command": ["python3", "scripts/coordination-health-collector.py",
                    "--root", ".", "--output-dir", str(output_relative)]},
        "candidate_periods": rows, "successor_queries": SUCCESSOR_QUERIES,
        "successor_census": census,
        "same_class_open": [{"url": row["url"], "reason": row["reason"]} for row in census if row["disposition"] == "blocking"],
    }
    destination = output / "retirement-readiness.json"
    destination.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
    return destination


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    try:
        root = args.root.resolve(strict=True)
        output = args.output_dir if args.output_dir.is_absolute() else root / args.output_dir
        destination = collect(root, output.resolve(), datetime.now(timezone.utc).replace(microsecond=0))
    except (OSError, ValueError, KeyError, json.JSONDecodeError, subprocess.CalledProcessError, AssertionError) as error:
        print(f"coordination health collector: BLOCKED: {error}", file=sys.stderr)
        return 1
    print(destination)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

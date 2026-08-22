#!/usr/bin/env python3
"""Emit a complete, explicit reading of the churn-roadmap health measures.

The report intentionally distinguishes a measured false result from a result for
which this repository has no authoritative, machine-readable input.  The latter
is emitted as ``unverified``; it is never silently omitted or guessed.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path


MEASURES = (
    ("issue-flow", "Issue creation stays below closure for three consecutive periods."),
    ("behaviourless-repairs", "Fewer than 10% of repair commits change only statements or projections."),
    ("scheduling-intent", "Reconciliation does not reverse deliberate scheduling intent."),
    ("complete-reads", "No successful read is later discovered to have been partial."),
    ("release-coherence", "Releases complete coherently or remain visibly resumable."),
    ("artifact-trend", "Independent policy implementations, checks, and workflows trend down."),
    ("evidence-growth", "Generated evidence grows more slowly than implementation and tests."),
)


def fail(message: str) -> None:
    raise ValueError(message)


def read_fixture(path: Path) -> dict:
    try:
        data = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read churn fixture {path}: {error}")
    if data.get("schema") != "fsgg.coord.churn-reading/v1":
        fail("fixture schema must be fsgg.coord.churn-reading/v1")
    if not isinstance(data.get("rowsOpened"), int) or not isinstance(data.get("rowsClosed"), int):
        fail("fixture must contain integer rowsOpened and rowsClosed")
    window = data.get("window")
    if not isinstance(window, dict) or not all(isinstance(window.get(key), str) for key in ("start", "end")):
        fail("fixture window must contain string start and end")
    return data


def git_lines(repo: Path, base: str, paths: tuple[str, ...]) -> int | None:
    command = ["git", "-C", str(repo), "diff", "--numstat", f"{base}..HEAD", "--", *paths]
    completed = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if completed.returncode:
        return None
    total = 0
    for line in completed.stdout.splitlines():
        added, removed, *_ = line.split("\t")
        if added.isdigit():
            total += int(added) + int(removed)
    return total


def report(fixture: dict, repo: Path, baseline: str | None) -> dict:
    opened, closed = fixture["rowsOpened"], fixture["rowsClosed"]
    # A single fixture is an observed period, not evidence for the roadmap's
    # three-consecutive-period predicate.
    issue_verdict = "unverified"
    evidence = {"fixture": str(fixture["window"]["start"]) + ".." + str(fixture["window"]["end"])}
    entries = [
        {"id": "issue-flow", "verdict": issue_verdict,
         "value": {"opened": opened, "closed": closed},
         "reason": "One period is insufficient to establish the required three consecutive periods; its observed net is %d." % (opened - closed)},
        {"id": "behaviourless-repairs", "verdict": "unverified",
         "reason": "No authoritative classifier binds a repair commit to behaviour-changing evidence."},
        {"id": "scheduling-intent", "verdict": "unverified",
         "reason": "No complete durable census binds every reconciliation result to deliberate intent."},
        {"id": "complete-reads", "verdict": "unverified",
         "reason": "Later discoveries of partial reads are incident evidence, not a complete historical census."},
        {"id": "release-coherence", "verdict": "unverified",
         "reason": "No authoritative release ledger classifies every channel state for this window."},
        {"id": "artifact-trend", "verdict": "unverified",
         "reason": "A baseline is required to compare the independent check and workflow corpus."},
        {"id": "evidence-growth", "verdict": "unverified",
         "reason": "A baseline is required to compare generated evidence with implementation and tests."},
    ]
    if baseline:
        generated = git_lines(repo, baseline, ("work", "readiness"))
        implementation = git_lines(repo, baseline, ("src", "tests"))
        if generated is not None and implementation is not None:
            entries[-1] = {"id": "evidence-growth", "verdict": "met" if generated < implementation else "violated",
                           "value": {"generatedLines": generated, "implementationAndTestLines": implementation},
                           "reason": f"Measured with git diff --numstat {baseline}..HEAD."}
    if [entry["id"] for entry in entries] != [identifier for identifier, _ in MEASURES]:
        raise AssertionError("health-measure inventory drifted")
    return {"schema": "fsgg.coord.roadmap-health/v1", "window": fixture["window"], "evidence": evidence,
            "measures": entries}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--baseline")
    args = parser.parse_args()
    try:
        print(json.dumps(report(read_fixture(args.fixture), args.repo, args.baseline), indent=2, sort_keys=True))
    except ValueError as error:
        print(f"report-roadmap-health: {error}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

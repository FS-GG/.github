#!/usr/bin/env python3
"""Enforce the single typed GraphQL boundary and reject retired compatibility frontends."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

SELECTOR = re.compile(
    r'(?:TryGetProperty|GetProperty)\s*(?:\(\s*)?["\'](errors|data|pageInfo|hasNextPage|endCursor)["\']'
)
PY_SELECTOR = re.compile(r'(?:\[|\.get\()["\'](data|errors|pageInfo|hasNextPage|endCursor)["\']')
TRANSPORT = re.compile(r'gh(?:["\'],\s*["\']|\s+)api(?:["\'],\s*["\']|\s+)graphql')


# EXPLICIT, NAMED, OWNED EXEMPTIONS. A file listed here is still read by this check, but its raw
# selectors are not findings. Each record must name a reason, an owner and a tracking reference, and the
# exemption is itself a finding when the file is missing or no longer needs it (a stale exemption would
# silently widen the boundary). Remove the record when the file moves behind the typed boundary.
EXEMPTIONS = {
    "scripts/board-drift-fetch.py": {
        "reason": "App-token workflow observer; its Track, Work level, per-field timestamp and repository "
                  "visibility fields are not exposed by the typed archive-scan",
        "owner": "Programme/root",
        "tracking": "Refs #4730 (follow-up: move the read to a typed OperationalGraphQl verb)",
    },
    "scripts/board-linkage.py": {
        "reason": "App-token advisory PR check; it reads one PR's editor and each referenced issue's Project 3 "
                  "item and native parent, which the typed verbs do not expose",
        "owner": "Programme/root",
        "tracking": "Refs #5094 (follow-up: move the read to a typed OperationalGraphQl verb)",
    },
    "scripts/board-currency-fetch.py": {
        "reason": "App-token shadow observer; its Work level, native parent/sub-issue/blocked-by links and "
                  "issue state reasons are not exposed by the typed archive-scan",
        "owner": "Programme/root",
        "tracking": "Refs #4730 (follow-up: move the read to a typed OperationalGraphQl verb)",
    },
}


def exemption_problems(exemptions: dict) -> list[str]:
    problems = []
    for path, record in exemptions.items():
        for field in ("reason", "owner", "tracking"):
            if not str(record.get(field, "")).strip():
                problems.append(f"{path}: exemption lacks a {field}")
        if "#" not in str(record.get("tracking", "")):
            problems.append(f"{path}: exemption tracking must reference an issue")
    return problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    source = args.root / "src" / "FS.GG.Coord.GitHub"
    findings: list[tuple[Path, int, str]] = []

    for path in sorted(source.glob("*.fs")):
        if path.name in {"GraphQl.fs", "GraphQlEnvelope.fs", "OperationalGraphQl.fs"}:
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = SELECTOR.search(line)
            if match:
                findings.append((path.relative_to(args.root), number, match.group(1)))

    production = [
        args.root / "scripts" / "projects-audit.sh",
        args.root / "scripts" / "repos-audit.sh",
        args.root / "scripts" / "coord-board-archive.py",
        args.root / "scripts" / "check-roster-closure.py",
        args.root / ".github" / "workflows" / "coord-board-archive.yml",
    ]
    for retired in (
        args.root / "scripts" / "graphql_complete_read.py",
        args.root / "tests" / "test_graphql_complete_read.py",
    ):
        if retired.exists():
            findings.append((retired.relative_to(args.root), 1, "retired compatibility boundary still exists"))
    for problem in exemption_problems(EXEMPTIONS):
        findings.append((Path("scripts/check-graphql-boundary.py"), 1, problem))
    exempt_seen: dict[str, int] = {}
    for path in production + [args.root / name for name in EXEMPTIONS]:
        relative = path.relative_to(args.root).as_posix()
        if not path.exists():
            if relative in EXEMPTIONS:
                findings.append((Path(relative), 1, "exempt file is missing (stale exemption)"))
                continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = PY_SELECTOR.search(line) or TRANSPORT.search(line)
            if match and not line.lstrip().startswith("#"):
                if relative in EXEMPTIONS:
                    exempt_seen[relative] = exempt_seen.get(relative, 0) + 1
                    continue
                findings.append((path.relative_to(args.root), number, match.group(1) if match.lastindex else "transport"))
    for relative, record in EXEMPTIONS.items():
        if relative not in exempt_seen and (args.root / relative).exists():
            findings.append((Path(relative), 1, "exemption is stale: the file has no raw GraphQL handling left"))
        elif relative in exempt_seen:
            print(f"graphql-boundary: EXEMPT {relative} ({exempt_seen[relative]} raw selector(s)); "
                  f"owner {record['owner']}; {record['tracking']}")

    for path, number, field in findings:
        print(
            f"::error file={path},line={number}::raw GraphQL `{field}` handling must live in "
            "the typed GraphQL boundary"
        )

    if findings:
        print(f"graphql-boundary: INVALID ({len(findings)} raw selector(s))")
        return 1

    print("graphql-boundary: VALID — envelope and pagination selectors are monopolised")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

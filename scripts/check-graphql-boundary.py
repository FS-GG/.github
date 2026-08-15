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
    for path in production:
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = PY_SELECTOR.search(line) or TRANSPORT.search(line)
            if match and not line.lstrip().startswith("#"):
                findings.append((path.relative_to(args.root), number, match.group(1) if match.lastindex else "transport"))

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

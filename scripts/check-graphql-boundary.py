#!/usr/bin/env python3
"""Fail when production code opens a GraphQL envelope or Relay page outside GraphQl.fs."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

SELECTOR = re.compile(
    r'(?:TryGetProperty|GetProperty)\s*(?:\(\s*)?["\'](errors|data|pageInfo|hasNextPage|endCursor)["\']'
)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    source = args.root / "src" / "FS.GG.Coord.GitHub"
    findings: list[tuple[Path, int, str]] = []

    for path in sorted(source.glob("*.fs")):
        if path.name in {"GraphQl.fs", "Budget.fs"}:
            continue
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = SELECTOR.search(line)
            if match:
                findings.append((path.relative_to(args.root), number, match.group(1)))

    for path, number, field in findings:
        print(
            f"::error file={path},line={number}::raw GraphQL `{field}` handling must live in "
            "src/FS.GG.Coord.GitHub/GraphQl.fs"
        )

    if findings:
        print(f"graphql-boundary: INVALID ({len(findings)} raw selector(s))")
        return 1

    print("graphql-boundary: VALID — envelope and pagination selectors are monopolised")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

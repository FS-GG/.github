#!/usr/bin/env python3
"""Fail-closed guard for registry/dependencies.yml's changelog protocol (.github#1672)."""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


class Refused(Exception): pass


def read(path: Path) -> str:
    try: return path.read_text(encoding="utf-8")
    except OSError as exc: raise Refused(f"cannot read {path}: {exc}") from exc


def updated(text: str) -> str:
    hits = re.findall(r'^updated:\s*["\'](\d{4}-\d{2}-\d{2})["\'](?:\s+#.*)?\s*$', text, re.M)
    if len(hits) != 1: raise Refused("dependencies.yml must contain exactly one quoted updated: YYYY-MM-DD")
    return hits[0]


def top_date(text: str) -> str:
    marker = re.search(r'^## Entries\s*$', text, re.M)
    if not marker: raise Refused("CHANGELOG.md lacks its Entries heading")
    hit = re.search(r'^- \*\*(\d{4}-\d{2}-\d{2})\*\*', text[marker.end():], re.M)
    if not hit: raise Refused("CHANGELOG.md has no dated entry")
    return hit[1]


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--dependencies', default='registry/dependencies.yml')
    ap.add_argument('--changelog', default='registry/CHANGELOG.md')
    ap.add_argument('--changed', action='append', default=[], help='changed path; repeatable')
    args = ap.parse_args(argv)
    try:
        changed = set(args.changed)
        if 'registry/dependencies.yml' in changed and 'registry/CHANGELOG.md' not in changed:
            raise Refused('dependencies.yml changed without registry/CHANGELOG.md; prepend the human entry')
        dep_date, log_date = updated(read(Path(args.dependencies))), top_date(read(Path(args.changelog)))
        if dep_date != log_date: raise Refused(f'dependencies.yml updated={dep_date} != top changelog date={log_date}')
    except Refused as exc:
        print(f'::error::check-registry-changelog: {exc}', file=sys.stderr); return 1
    print(f'registry changelog protocol holds (top entry and updated: {dep_date})'); return 0


if __name__ == '__main__': raise SystemExit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""Reject broken repository-local ``path:line`` citations in live documentation.

The gate intentionally answers one narrow question: does a path-shaped citation in live
tracked Markdown prose name a file tracked by this repository? It does not check line ranges, parse
free-form counts, or infer that a bare basename belongs to this repository. ADRs and dated reports
are historical records and are excluded.

Cross-repository citations must either be a URL or use the explicit form
``OWNER/REPOSITORY@REVISION:path:line``. Bare basenames and source roots not owned by this
repository (for example ``src/Canvas/...``) are also outside the local-path grammar.

Exit 0 = checked a non-empty local citation corpus and every target is tracked.
Exit 1 = at least one repository-local citation names an untracked path.
Exit 3 = permanent no-verdict: the subject/corpus is empty or git inventory is unavailable.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

OK, FINDING, NO_VERDICT = 0, 1, 3
EXEMPT_PREFIXES = ("docs/adr/", "docs/reports/")
QUALIFIED = re.compile(
    r"(?<![\w.-])[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[^\s:`]+:"
    r"(?:\.?[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]+:\d+(?:-\d+)?"
)
CITATION = re.compile(
    r"(?<![\w/.-])(?P<path>\.?[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+)"
    r":(?P<line>\d+)(?:-(?P<end>\d+))?"
)


def tracked_files(root: Path) -> set[str]:
    completed = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"], check=False,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.decode(errors="replace").strip())
    return {item.decode() for item in completed.stdout.split(b"\0") if item}


def normalize(raw: str) -> str:
    return raw[2:] if raw.startswith("./") else raw


def local_prefixes(tracked: set[str]) -> tuple[str, ...]:
    """Derive owned roots from git, while keeping foreign ``src/X`` references out of scope."""
    top_levels = {path.split("/", 1)[0] + "/" for path in tracked if "/" in path}
    top_levels.discard("src/")
    source_namespaces = {
        "/".join(path.split("/", 2)[:2]) + "/"
        for path in tracked
        if path.startswith("src/") and path.count("/") >= 2
    }
    return tuple(sorted(top_levels | source_namespaces))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args(argv)
    root = Path(args.root).resolve()
    try:
        tracked = tracked_files(root)
    except (OSError, RuntimeError) as exc:
        print(f"::error::prose-citations: cannot enumerate tracked files: {exc}", file=sys.stderr)
        return NO_VERDICT

    subjects = sorted(path for path in tracked if path.endswith(".md")
                      and not path.startswith(EXEMPT_PREFIXES))
    if not subjects:
        print("::error::prose-citations: no live tracked Markdown subjects", file=sys.stderr)
        return NO_VERDICT

    owned = local_prefixes(tracked)
    examined = 0
    findings: list[str] = []
    for relative in subjects:
        try:
            lines = (root / relative).read_text(encoding="utf-8").splitlines()
        except OSError as exc:
            print(f"::error::prose-citations: cannot read {relative}: {exc}", file=sys.stderr)
            return NO_VERDICT
        for number, original in enumerate(lines, 1):
            line = QUALIFIED.sub("", original)
            for match in CITATION.finditer(line):
                target = normalize(match.group("path"))
                if not target.startswith(owned):
                    continue
                examined += 1
                if target not in tracked:
                    findings.append(
                        f"{relative}:{number}: repository-local citation "
                        f"{target}:{match.group('line')} does not name a tracked file"
                    )

    if examined == 0:
        print("::error::prose-citations: found zero repository-local path:line citations; "
              "the extractor or subject selection has no measurable corpus", file=sys.stderr)
        return NO_VERDICT
    if findings:
        for finding in findings:
            print(f"::error::prose-citations: {finding}", file=sys.stderr)
        return FINDING
    print(f"prose-citations: ok ({len(subjects)} live documents, {examined} local citations)")
    return OK


if __name__ == "__main__":
    raise SystemExit(main())

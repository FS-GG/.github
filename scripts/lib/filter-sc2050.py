#!/usr/bin/env python3
"""filter-sc2050.py — drop only the SC2050 findings shellcheck could not have avoided, by OCCURRENCE,
never by line or by file (.github#2493 review round 2 -> round 3).

WHY THIS EXISTS, AND WHY IT REPLACES TWO EARLIER, WEAKER MECHANISMS. `scripts/lib/extract-workflow-
shell.py` substitutes every GitHub Actions `${{ ... }}` expression with a braced `${GHA_EXPR}` reference
before handing the result to shellcheck (see that script's module docstring for why). One shape it
cannot make clean by substitution alone: `${{ x }}` as the WHOLE content of a single-quoted comparison
operand — `'${{ x }}'` becomes `'${GHA_EXPR}'`, and single quotes never interpolate, so shellcheck sees
two adjacent STRING LITERALS in the common `[ '${{ github.event_name }}' = 'workflow_dispatch' ]` shape
and reports SC2050 ("did you forget the $ on a variable?"). It is not a typo — the left side is
genuinely dynamic, just substituted by GitHub before any shell parses it — so that ONE finding needs
suppressing, and ONLY that one.

Two earlier attempts at "only that one" both proved too coarse, on this exact item:

  - Round 1 shipped `-e SC2050` across the WHOLE workflow-embedded shellcheck invocation (every file).
    The critic proved it wrong with a genuine, unrelated SC2050 typo — `[ 'GITHUB_REF_NAME' = 'main' ]`,
    missing its `$`, ZERO `${{ }}` involvement anywhere in the 360-step subject — that a file-wide
    exclusion hides exactly as completely as it hides the substitution artifact.
  - Round 2 narrowed it to a PER-LINE inline `# shellcheck disable=SC2050` pragma, inserted only before
    lines the substitution actually touched. The critic proved that ALSO wrong: a `${{ }}` embedded with
    literal apostrophes inside a DOUBLE-quoted string (`echo "...'${{ x }}'..."`, the exact shape behind
    an earlier SC2027 false positive) makes the same textual pattern match on a line, and shellcheck's
    `disable` directive suppresses the WHOLE following physical line — so a genuine, unrelated SC2050
    defect sharing that line with the resembling-but-different substitution artifact was silently
    swallowed too. Shellcheck's own directive primitive is line-scoped; no comment placement fixes that.

This script is the third mechanism, and it is occurrence-scoped instead of line-scoped BY CONSTRUCTION:
it filters shellcheck's OWN reported `(file, line, column)` against `extract-workflow-shell.py`'s own
precisely-computed bracket/test-construct spans (`<materialized-file>.sc2050-spans`, one `LINE START END`
per line, both columns 1-indexed and inclusive — see `find_sc2050_protected_spans()` there). An SC2050
finding is dropped ONLY when its reported column falls inside a recorded span; every other finding —
including a SECOND, unrelated SC2050 on the very same physical line, and every finding of every other
check — passes through unfiltered. `.github#2493`'s own review is the record that this property
(two genuine `[ ... ]` constructs sharing one line, only one of them substitution-caused) is not
hypothetical hardening: it is the direct generalization of the exact collision the critic constructed.

USAGE:  shellcheck ... -f gcc <files...> | python3 filter-sc2050.py
Reads shellcheck's gcc-format output on stdin, writes the filtered output to stdout unchanged apart from
dropped SC2050 lines, and exits 1 if any finding-shaped line survives (any severity, any check — not
only SC2050), 0 if none does. A `<file>.sc2050-spans` sidecar that does not exist for a given file is
read as "zero protected spans" — nothing here is ever silently dropped for a file the extractor did not
materialize a sidecar for.
"""
from __future__ import annotations

import re
import sys

# gcc format: `<file>:<line>:<col>: <severity>: <message> [SCxxxx]`
FINDING_LINE = re.compile(r"^(?P<file>.+):(?P<line>\d+):(?P<col>\d+):\s+\w+:.*\[(?P<code>SC\d+)\]\s*$")

_spans_cache: dict[str, list[tuple[int, int, int]]] = {}


def load_spans(sidecar_path: str) -> list[tuple[int, int, int]]:
    if sidecar_path in _spans_cache:
        return _spans_cache[sidecar_path]
    spans: list[tuple[int, int, int]] = []
    try:
        with open(sidecar_path, encoding="utf-8") as fh:
            for raw in fh:
                raw = raw.strip()
                if not raw:
                    continue
                line_s, start_s, end_s = raw.split("\t")
                spans.append((int(line_s), int(start_s), int(end_s)))
    except OSError:
        pass  # no sidecar for this file -> zero protected spans, never a silent drop
    _spans_cache[sidecar_path] = spans
    return spans


def is_protected(file: str, line: int, col: int) -> bool:
    for span_line, col_start, col_end in load_spans(file + ".sc2050-spans"):
        if span_line == line and col_start <= col <= col_end:
            return True
    return False


def filter_stream(lines: list[str]) -> tuple[list[str], bool]:
    """Return (kept_lines, any_finding_survives)."""
    kept: list[str] = []
    any_finding = False
    for raw in lines:
        m = FINDING_LINE.match(raw.rstrip("\n"))
        if m and m.group("code") == "SC2050":
            file = m.group("file")
            line = int(m.group("line"))
            col = int(m.group("col"))
            if is_protected(file, line, col):
                continue  # dropped: attributable to our own ${{ }} substitution at this exact spot
        kept.append(raw)
        if m:
            any_finding = True
    return kept, any_finding


def main(argv: list[str]) -> int:
    if len(argv) != 1:
        print("usage: shellcheck ... -f gcc <files...> | filter-sc2050.py", file=sys.stderr)
        return 2
    lines = sys.stdin.readlines()
    kept, any_finding = filter_stream(lines)
    sys.stdout.writelines(kept)
    return 1 if any_finding else 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv))
    except Exception as e:  # noqa: BLE001 — "I could not check" (exit 3) must never collide with
        # "found a finding" (exit 1): an uncaught crash here is a BUG in this filter, not a defect in
        # the shell it was reading, and `lint-shell.sh`'s combined `0|1) ;; *) exit 2` check treats any
        # code outside {0,1} as "could not check" — exit 3 (distinct from filter-sc2050.py's own
        # documented usage-error exit 2) routes there rather than risking exit 1's meaning by accident.
        print(f"::error::filter-sc2050.py crashed: {e}", file=sys.stderr)
        sys.exit(3)

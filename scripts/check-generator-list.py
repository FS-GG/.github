#!/usr/bin/env python3
"""Every generator answers `--list`, honestly — the convention ADR-0044 rests on (.github#498).

WHAT THIS GUARDS. `scripts/generated-paths` derives the repo's generated, CI-gated artifacts by
ASKING each generator what it emits, and `verify-paths` subtracts that set so a worker who
regenerates an artifact §1 told them not to declare stops seeing a permanent false DRIFT. The whole
construction rests on one promise: a generator, asked `--list`, answers truthfully and touches
nothing. Nothing but this gate holds anyone to that promise.

It is worth being blunt about why the promise needs a gate rather than trust. When this was written,
`generate-skill-union-bundle` parsed its arguments as `[ "${1:-}" = "--check" ] && CHECK=1` — so any
argument that was not exactly `--check` fell through to the WRITE. Asking that generator a question
did not merely fail; it REGENERATED the bundle. The convention's first act was to walk into that, and
the generator whose whole purpose is to be asked was the one that could not be.

WHAT IT DOES NOT DO, AND WHY THAT IS A FINDING RATHER THAN A GAP. It does NOT decide whether an
artifact is CI-GATED by reading workflow `run:` text. #309's rule has two conditions — generated AND
gated — and only the second makes a collision a rebase rather than a decision, so the temptation to
grep the workflows for `scripts/<x> --check` is strong. It is unsound, and this repo has already
measured exactly how: `check-paths-coherence.py` records that scanning `run:` blocks for repo paths
returns three hits against this repo and ALL THREE ARE FALSE — `scripts/fsgg-coord` named inside a
YAML comment. A parser cannot tell a MENTION from a USE (#683). It would also find nothing for
`registry/repos.lock`, whose guard is `repos.sh validate` and not a `--check` at all.

So the gated half is proven the only sound way there is — BY EXECUTION, in
`tests/generator-list/run.sh`: dirty the artifact, run its declared guard, assert it reds. A
generator's claim is not self-certifying, but a claim something EXECUTES is no longer a claim.

Exit: 0 = ok; 1 = a violation (each printed with ::error::); 2 = the gate could not reach a verdict.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path

# The surface this gate reads. Composed, not retyped: `check-paths-coherence.py` folds this by AST and
# reds our workflow if its `paths:` filter does not select every entry, so widening what we walk
# widens the filter that decides whether we run at all.
ROSTER_TOOL = ("scripts/generated-paths",)
GENERATOR_GLOB_DIR = ("scripts",)

PATHS_SUBJECT = ROSTER_TOOL + GENERATOR_GLOB_DIR

# A script matching this is a generator by naming convention, and MUST be rostered. This is the
# forgotten-generator net: the roster is the one hand-kept thing in the design, and while forgetting
# an entry fails CLOSED (its artifacts go unsubtracted, so a worker sees the drift line they see
# today — noise, not silence), "fails safe" is a reason to allow a copy, not a reason to stop
# watching it. `repos.sh` is deliberately not matched: it is a multi-command tool whose GENERATOR is
# the `relock` subcommand, so the roster names that invocation rather than the file.
GENERATOR_PREFIX = "generate-"


class GateError(Exception):
    """The gate cannot reach a verdict — exit 2, never a silent pass."""


def run(root: Path, argv: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        argv, cwd=root, capture_output=True, text=True, timeout=120
    )


def roster(root: Path) -> list[str]:
    """The generators, ASKED FOR rather than parsed out of bash.

    A gate that re-derived the roster by reading the array out of `generated-paths` would be the
    second copy of the fact all over again — and it would be the copy deciding whether the first is
    honest, which is the worst place to put one.
    """
    tool = root / "scripts" / "generated-paths"
    if not tool.is_file():
        raise GateError(f"no roster tool at {tool} — nothing to check, and that is not a pass.")

    p = run(root, [str(tool), "--roster"])
    if p.returncode != 0:
        raise GateError(f"`generated-paths --roster` exited {p.returncode}: {p.stderr.strip()}")

    invocations = [ln for ln in p.stdout.splitlines() if ln.strip()]
    if not invocations:
        # VACUITY IS THE #266 SHAPE, and it is the one this gate exists inside of: an empty roster
        # would make every check below pass, over nothing.
        raise GateError("the roster is EMPTY — every check below would pass over nothing (#266).")
    return invocations


def check_rows(root: Path, inv: str, errors: list[str]) -> list[tuple[str, str, str]]:
    """Run one generator's `--list` and validate every row it answers."""
    p = run(root, inv.split())

    if p.returncode != 0:
        errors.append(
            f"`{inv}` exited {p.returncode} — a generator must answer what it emits without "
            f"failing. stderr: {p.stderr.strip()[:200]}"
        )
        return []

    rows: list[tuple[str, str, str]] = []
    for n, line in enumerate(p.stdout.splitlines(), 1):
        if not line.strip():
            continue
        fields = line.split("\t")
        if len(fields) != 3:
            errors.append(
                f"`{inv}` line {n}: {len(fields)} tab-separated field(s), want exactly 3 "
                f"(kind<TAB>path<TAB>marker). Got: {line!r}"
            )
            continue
        kind, path, marker = fields
        if not kind.strip():
            errors.append(f"`{inv}` line {n}: empty kind.")
        if not path.strip():
            errors.append(f"`{inv}` line {n}: empty path.")
            continue
        if path.startswith("/") or ".." in Path(path).parts:
            # verify-paths compares against a PR's file list, which is repo-relative. An absolute or
            # escaping path could never match one, so it would subtract NOTHING while looking like it
            # subtracted something — a fail-open wearing a green tick.
            errors.append(f"`{inv}` line {n}: path {path!r} is not repo-relative.")
            continue
        if not (root / path).exists():
            errors.append(
                f"`{inv}` line {n}: names {path!r}, which does not exist. A generator that "
                f"cannot name its own output is not one anything downstream may believe."
            )
        rows.append((kind, path, marker))

    if not rows:
        # "I emit nothing" and "I could not tell you" are opposite facts, and only one is safe.
        errors.append(f"`{inv}` listed NOTHING — a rostered generator must name what it emits.")
    return rows


def check_roster_complete(root: Path, invocations: list[str], errors: list[str]) -> None:
    """Every `scripts/generate-*` is rostered."""
    rostered = " ".join(invocations)
    for script in sorted((root / "scripts").glob(f"{GENERATOR_PREFIX}*")):
        if not script.is_file():
            continue
        rel = f"scripts/{script.name}"
        if rel not in rostered:
            errors.append(
                f"{rel} looks like a generator and is NOT in `generated-paths`' roster. Add its "
                f"`--list` invocation there, or its artifacts will keep being reported as drift "
                f"(ADR-0044)."
            )


def check_no_double_claim(all_rows: list[tuple[str, str, str, str]], errors: list[str]) -> None:
    """One path, one story about whether it is authored.

    A path claimed as a whole-file artifact by one generator AND as a region by another is a
    contradiction, and it resolves in the dangerous direction: the whole-file row would subtract a
    file the region row says somebody authors.
    """
    by_path: dict[str, set[bool]] = {}
    for _inv, _kind, path, marker in all_rows:
        by_path.setdefault(path, set()).add(marker == "")
    for path, wholeness in sorted(by_path.items()):
        if len(wholeness) > 1:
            errors.append(
                f"{path} is listed BOTH as a whole-file artifact (empty marker) and as a generated "
                f"region (marker set). Subtracting it would suppress drift on a file somebody authors."
            )


def main(argv: list[str] | None = None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    root = Path.cwd()
    if "--root" in argv:
        root = Path(argv[argv.index("--root") + 1]).resolve()

    try:
        invocations = roster(root)
    except GateError as e:
        print(f"::error::generator-list: {e}", file=sys.stderr)
        return 2

    errors: list[str] = []
    all_rows: list[tuple[str, str, str, str]] = []

    for inv in invocations:
        for kind, path, marker in check_rows(root, inv, errors):
            all_rows.append((inv, kind, path, marker))

    check_roster_complete(root, invocations, errors)
    check_no_double_claim(all_rows, errors)

    if errors:
        for e in errors:
            print(f"::error::generator-list: {e}", file=sys.stderr)
        return 1

    subtractable = sorted({p for _i, _k, p, m in all_rows if m == ""})
    print(
        f"generator-list: {len(invocations)} generator(s) answered --list; "
        f"{len(all_rows)} row(s); {len(subtractable)} subtractable artifact(s):"
    )
    for p in subtractable:
        print(f"  {p}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

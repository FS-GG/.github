#!/usr/bin/env python3
"""Materialise EXACTLY what a workflow's `actions/checkout` sparse-checkout would leave on disk.

.github#1515 (and #1510, the same defect already fired). A reusable workflow that fetches its check
script from the authority repo by NAMING FILES is keeping a hand-maintained copy of that script's
dependency list, in a file that cannot execute the thing it lists. Nothing compares the two, so the
list drifts the moment the script gains a sibling — and the drift is invisible here, because every
suite in this repo runs the script from a FULL checkout where every dependency is present. #1510 was
found by a receiver in another repository, at load, after ~7 seconds, having asserted nothing.

This module is what lets a local fixture see that gap. It answers one question honestly:

    given the sparse patterns THIS workflow file actually declares, which files does the runner
    actually get?

and then hands back a tree containing only those, so the script can be loaded from it for real. A test
that runs the script from the repo root proves nothing about a sparse-checkout; a test that runs it
from the output of this module proves the thing that broke.

HOW THE SELECTION IS COMPUTED: with git, not with a re-implementation of gitignore semantics. A
throwaway repo is built holding EMPTY placeholders at every tracked path of the subject repo — the
path universe, and nothing else, so the pattern match is decided by the same matcher `actions/checkout`
drives — then `git sparse-checkout set` is run with the workflow's own patterns and cone flag, and
whatever survives in that working tree is the selection. Re-deriving the rules by hand here would make
this fixture assert my reading of gitignore rather than git's.

CONTENT COMES FROM THE WORKING TREE, not from HEAD. The subject of this check is the workflow file and
the script as they are RIGHT NOW: a fixture that read committed state would silently grade the previous
commit whenever someone ran it before committing, which is the same class of quiet wrong answer it
exists to catch.

Usage:
    sparse_set.py --repo-root R --out D [--from-workflow F | --pattern P ...] [--cone|--no-cone]

Prints the selected paths, one per line, sorted. Exits non-zero, loudly, if the workflow cannot be
parsed or names no authority checkout — "I could not tell" is not "it is fine" (epic #266).
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

AUTHORITY = re.compile(r"^(\s*)repository:\s*FS-GG/\.github\s*$")
KEY = re.compile(r"^(\s*)([A-Za-z0-9_.-]+):(.*)$")
BLOCK_SCALAR = re.compile(r"^[|>][+-]?\d*$")


class SparseError(Exception):
    pass


def _indent(line: str) -> int:
    return len(line) - len(line.lstrip(" "))


def _is_skippable(line: str) -> bool:
    stripped = line.strip()
    return not stripped or stripped.startswith("#")


def parse_workflow(path: Path) -> tuple[list[str], bool]:
    """Return (patterns, cone_mode) for the workflow's FS-GG/.github checkout step.

    An empty pattern list means the step takes a full checkout, which under-fetches nothing.
    """
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except OSError as error:
        raise SparseError(f"cannot read {path}: {error}") from error

    anchors = [i for i, line in enumerate(lines) if AUTHORITY.match(line)]
    if len(anchors) != 1:
        raise SparseError(
            f"{path}: expected exactly one `repository: FS-GG/.github` checkout, found {len(anchors)}. "
            "This fixture grades that step; it will not guess which one."
        )
    anchor = anchors[0]
    width = _indent(lines[anchor])

    # The `with:` mapping is the run of lines at this indent or deeper, blanks and comments included.
    start = anchor
    while start > 0 and (_is_skippable(lines[start - 1]) or _indent(lines[start - 1]) >= width):
        start -= 1
    end = anchor + 1
    while end < len(lines) and (_is_skippable(lines[end]) or _indent(lines[end]) >= width):
        end += 1

    values: dict[str, str | list[str]] = {}
    index = start
    while index < end:
        line = lines[index]
        match = KEY.match(line)
        if _is_skippable(line) or not match or len(match.group(1)) != width:
            index += 1
            continue
        key, inline = match.group(2), match.group(3).strip()
        if BLOCK_SCALAR.match(inline):
            collected: list[str] = []
            index += 1
            while index < end and (_is_skippable(lines[index]) or _indent(lines[index]) > width):
                if not _is_skippable(lines[index]):
                    collected.append(lines[index].strip())
                index += 1
            values[key] = collected
            continue
        values[key] = inline.strip("\"'")
        index += 1

    raw = values.get("sparse-checkout")
    if raw is None:
        patterns: list[str] = []
    elif isinstance(raw, list):
        patterns = [entry for entry in raw if entry]
    else:
        patterns = [raw] if raw else []

    # `actions/checkout` defaults sparse-checkout-cone-mode to true. An omitted flag is that default,
    # not "unknown" — and it changes what the patterns mean, so it is read rather than assumed.
    cone_raw = values.get("sparse-checkout-cone-mode", "true")
    if isinstance(cone_raw, list):
        raise SparseError(f"{path}: sparse-checkout-cone-mode must be a scalar")
    if cone_raw.lower() not in {"true", "false"}:
        raise SparseError(f"{path}: unreadable sparse-checkout-cone-mode: {cone_raw!r}")
    return patterns, cone_raw.lower() == "true"


def _git(*args: str, cwd: Path) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=cwd,
        capture_output=True,
        text=True,
        check=False,
        env={**os.environ, "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_SYSTEM": os.devnull},
    )
    if result.returncode != 0:
        raise SparseError(f"git {' '.join(args)} failed ({result.returncode}): {result.stderr.strip()}")
    return result.stdout


def tracked_paths(repo_root: Path) -> list[str]:
    out = _git("ls-files", "-z", cwd=repo_root)
    paths = [entry for entry in out.split("\0") if entry]
    if not paths:
        raise SparseError(f"{repo_root} reports no tracked files; refusing to grade an empty universe")
    return paths


def select(repo_root: Path, patterns: list[str], cone: bool) -> list[str]:
    """The paths git would materialise for these patterns, decided by git."""
    universe = tracked_paths(repo_root)
    if not patterns:
        return sorted(universe)

    with tempfile.TemporaryDirectory(prefix="sparse-universe.") as scratch:
        probe = Path(scratch)
        _git("init", "-q", "-b", "main", ".", cwd=probe)
        _git("config", "user.email", "fixture@fs.gg", cwd=probe)
        _git("config", "user.name", "fixture", cwd=probe)
        for rel in universe:
            target = probe / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.touch()
        _git("add", "-A", cwd=probe)
        _git("-c", "commit.gpgsign=false", "commit", "-qm", "universe", cwd=probe)
        _git("sparse-checkout", "set", "--cone" if cone else "--no-cone", "--", *patterns, cwd=probe)

        selected: list[str] = []
        for root, dirs, files in os.walk(probe):
            if ".git" in dirs:
                dirs.remove(".git")
            for name in files:
                selected.append(Path(root, name).relative_to(probe).as_posix())
    return sorted(selected)


def materialise(repo_root: Path, out: Path, selected: list[str]) -> None:
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    for rel in selected:
        source = repo_root / rel
        if not source.is_file():
            # Tracked but absent from the working tree (a staged deletion). The runner would not get
            # it either, so leaving it out is the faithful answer — but say so rather than eliding it.
            print(f"sparse_set: tracked but missing from the working tree: {rel}", file=sys.stderr)
            continue
        target = out / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo-root", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--from-workflow", type=Path)
    parser.add_argument("--pattern", action="append", default=[])
    cone = parser.add_mutually_exclusive_group()
    cone.add_argument("--cone", dest="cone", action="store_true", default=None)
    cone.add_argument("--no-cone", dest="cone", action="store_false")
    args = parser.parse_args(argv)

    if bool(args.from_workflow) == bool(args.pattern):
        parser.error("give exactly one of --from-workflow or --pattern")

    try:
        if args.from_workflow:
            patterns, cone_mode = parse_workflow(args.from_workflow)
            if args.cone is not None:
                cone_mode = args.cone
        else:
            patterns, cone_mode = list(args.pattern), bool(args.cone)
        selected = select(args.repo_root.resolve(), patterns, cone_mode)
        materialise(args.repo_root.resolve(), args.out, selected)
    except SparseError as error:
        print(f"sparse_set: {error}", file=sys.stderr)
        return 2

    print("\n".join(selected))
    return 0


if __name__ == "__main__":
    sys.exit(main())

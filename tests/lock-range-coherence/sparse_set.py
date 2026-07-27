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

HOW THE PATTERNS ARE READ: with PyYAML, not with a hand-rolled scanner. This module's first draft
scanned the file by indentation, and it was WRONG in the one direction that matters — a folded block
scalar (`sparse-checkout: >`) joins its lines with spaces, so the runner would receive ONE pattern
containing a space, match nothing, and fetch an empty tree, while the hand parser reported two clean
patterns and the fixture went green over it. A fixture that fails open on the exact defect class it
exists to catch is worse than no fixture. The repo already pins `PyYAML==6.0.3` and ships
`.github/actions/setup-policy-python` to supply it; sibling fixtures (tests/dispatch-preflight,
tests/feed-coherence) parse workflow YAML the same way.

HOW THE SELECTION IS COMPUTED: with git, not with a re-implementation of gitignore semantics. A
throwaway repo is built holding EMPTY placeholders at every tracked path of the subject repo — the
path universe, and nothing else — then `git sparse-checkout set` is run with the workflow's own
patterns and cone flag, and whatever survives in that working tree is the selection. Re-deriving the
rules by hand would make this fixture assert my reading of gitignore rather than git's.

    Fidelity note: `actions/checkout` writes the pattern lines into `.git/info/sparse-checkout` and
    checks out, where this drives the porcelain `git sparse-checkout set --no-cone`. Same patterns,
    same matcher, same result — but the invocation differs, and `--no-cone` is deprecated (not
    removed) from git 2.37, so this is a divergence to know about rather than one to rely on.

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
import shutil
import subprocess
import sys
import tempfile

AUTHORITY = "FS-GG/.github"


class SparseError(Exception):
    pass


def _authority_steps(document: object) -> list[dict]:
    """Every `actions/checkout` step in the document aimed at the authority repo."""
    found: list[dict] = []
    jobs = document.get("jobs") if isinstance(document, dict) else None
    for job in (jobs or {}).values():
        if not isinstance(job, dict):
            continue
        for step in job.get("steps") or []:
            if not isinstance(step, dict):
                continue
            params = step.get("with")
            if isinstance(params, dict) and str(params.get("repository", "")).strip() == AUTHORITY:
                found.append(params)
    return found


def parse_workflow(path: Path) -> tuple[list[str], bool]:
    """Return (patterns, cone_mode) for the workflow's FS-GG/.github checkout step.

    An empty pattern list means the step declares NO sparse-checkout — a full clone, which
    under-fetches nothing. A declared-but-unreadable one is an error, never that.
    """
    try:
        import yaml
    except ModuleNotFoundError as error:  # pragma: no cover - environment, not logic
        raise SparseError(
            "PyYAML is not installed, so the workflow cannot be read. This is a TOOLING GAP, not a "
            "verdict about the workflow: install it with `pip install -r requirements-test.txt` "
            "(CI gets it from .github/actions/setup-policy-python)."
        ) from error

    try:
        document = yaml.safe_load(path.read_text(encoding="utf-8"))
    except OSError as error:
        raise SparseError(f"cannot read {path}: {error}") from error
    except yaml.YAMLError as error:
        raise SparseError(f"{path} is not parseable YAML: {error}") from error

    steps = _authority_steps(document)
    if len(steps) != 1:
        raise SparseError(
            f"{path}: expected exactly one `repository: {AUTHORITY}` checkout step, found {len(steps)}. "
            "This fixture grades that step; it will not guess which one."
        )
    params = steps[0]

    raw = params.get("sparse-checkout")
    if raw is None:
        patterns: list[str] = []
    else:
        # actions/checkout splits the input on newlines and drops blanks, so a block scalar and a
        # plain one reach git the same way. Mirror that rather than special-casing the YAML shape:
        # `>` folds its lines into ONE space-joined string, and reporting that as several clean
        # patterns is precisely how a hand parser fails OPEN here.
        entries = raw if isinstance(raw, list) else str(raw).split("\n")
        patterns = [entry.strip() for entry in entries if str(entry).strip()]
        if not patterns:
            # Declared and yielded nothing. NOT the same fact as "no sparse-checkout declared":
            # treating it as a full clone would make every coverage assertion below pass over a
            # workflow that fetches an empty tree (#266 — "I could not tell" is not "it is fine").
            raise SparseError(f"{path}: sparse-checkout is declared but yields no patterns")

    # actions/checkout defaults sparse-checkout-cone-mode to true. An omitted flag IS that default,
    # not "unknown" — and it changes what the patterns mean, so it is read rather than assumed.
    cone_raw = params.get("sparse-checkout-cone-mode", True)
    if not isinstance(cone_raw, bool):
        raise SparseError(f"{path}: unreadable sparse-checkout-cone-mode: {cone_raw!r}")
    return patterns, cone_raw


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


MARKER = ".sparse-set-owns-this-directory"


def materialise(repo_root: Path, out: Path, selected: list[str]) -> list[str]:
    # This recursively deletes --out, so it deletes only directories it made. A test helper that
    # rm -rf's whatever path it is handed is one typo away from eating someone's work.
    if out.exists():
        if not (out / MARKER).is_file():
            raise SparseError(f"refusing to overwrite {out}: it exists and sparse_set did not create it")
        shutil.rmtree(out)
    out.mkdir(parents=True)
    (out / MARKER).touch()

    notes: list[str] = []
    for rel in selected:
        source = repo_root / rel
        if not source.is_file():
            # Tracked but absent from the working tree (a staged deletion). The runner would not get
            # it either, so leaving it out is the faithful answer — but say so rather than eliding it.
            notes.append(f"tracked but missing from the working tree: {rel}")
            continue
        target = out / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    return notes


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
    # Cone mode decides what the patterns MEAN, so it is never defaulted silently on the --pattern
    # path. --from-workflow gets its default from the workflow, where an omitted key genuinely is
    # actions/checkout's documented `true`.
    if args.pattern and args.cone is None:
        parser.error("--pattern requires an explicit --cone or --no-cone")

    try:
        if args.from_workflow:
            patterns, cone_mode = parse_workflow(args.from_workflow)
            if args.cone is not None:
                cone_mode = args.cone
        else:
            patterns, cone_mode = list(args.pattern), bool(args.cone)
        selected = select(args.repo_root.resolve(), patterns, cone_mode)
        for note in materialise(args.repo_root.resolve(), args.out, selected):
            print(f"sparse_set: {note}", file=sys.stderr)
    except SparseError as error:
        print(f"sparse_set: {error}", file=sys.stderr)
        return 2

    print("\n".join(selected))
    return 0


if __name__ == "__main__":
    sys.exit(main())

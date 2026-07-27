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

AND THE READING ITSELF IS NOT KEPT HERE (.github#1530). `scripts/check-sparse-checkout-closure.py`
reads the same three non-obvious rules out of the same block, and a second copy of a PARSING RULE is
this repo's most-filed bug class — the very thing #1522's gate exists to close one level down. Both
callers now take `scripts/lib/sparse.py`. That was not a tidy-up: the two copies had ALREADY drifted,
and this file held the worse half of it. A bare `sparse-checkout:` with no value reached
`params.get(...)` as `None`, indistinguishable from an ABSENT key, so this module reported a full
clone for a workflow that fetches an EMPTY TREE — a fail-open, in the module whose header says a
declared-but-unreadable block "is an error, never that". The shared module tests membership with
`in`, which is the only way to tell those two apart.

AND SO IS THE STEP SELECTOR (.github#1553). #1530 shared the PARSE and left "which step's `with:`
block am I about to read" in each caller. Underneath their different filters both had to answer one
identical question — *is this step an `actions/checkout` aimed at repository R* — and this file
answered it twice wrong: it never read `uses:` at all, and it compared `repository:` case-sensitively
where GitHub resolves it casefolded. See `_authority_steps`.

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

# `scripts/` on the path, so `lib.sparse` resolves — the same contract every gate in this repo uses
# (see scripts/lib/gate.py's header). Anchored on THIS FILE rather than on --repo-root: the subject
# being materialised is frequently a synthetic throwaway repo, and reading the shared parse out of
# the tree under test would make the fixture assert whatever that tree happened to contain.
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from lib.sparse import (  # noqa: E402 (path shim first)
    SparseRefusal,
    checkout_steps,
    cone_mode_of,
    patterns_of,
    repository_matches,
)

AUTHORITY = "FS-GG/.github"


class SparseError(Exception):
    pass


def _authority_steps(document: object) -> list[dict]:
    """The `with:` mapping of every `actions/checkout` in the document aimed at the authority repo.

    QUALIFICATION IS `lib/sparse.py`'S, FILTERING IS THIS FIXTURE'S (.github#1553). What remains here
    is the one thing that is genuinely this caller's: it wants the SINGLE authority checkout, and
    `parse_workflow` refuses any other count. The two facts that make a step a candidate at all — the
    `uses:` and the `repository:` — are the shared reading, and this file had BOTH of them wrong:

      * it never read `uses:`, so any step carrying `repository: FS-GG/.github` in its `with:` block
        qualified, including one that is not a checkout and fetches nothing;
      * it compared the repository case-SENSITIVELY, so `repository: fs-gg/.GitHub` — which GitHub
        resolves to the same repo, and which the closure gate's `repo-casing` fixture leg asserts must
        still resolve — yielded zero authority steps and a hard failure on the count.

    Both were fail-CLOSED (a wrong count is an error, never a wrong selection), which is why they
    survived to be filed rather than found in production. Neither is reachable from this fixture's own
    subject, which is why they are asserted in `tests/lock-range-coherence/run.sh` against synthetic
    workflows instead.

    ONE BEHAVIOUR CHANGE #1553 DID NOT ASK FOR, TAKEN DELIBERATELY. The old copy walked
    `(jobs or {}).values()` and `job.get("steps") or []`, so a document whose `jobs:` or `steps:` is
    a truthy NON-mapping died with an uncaught `AttributeError`/`TypeError` — a traceback and exit 1.
    The shared selector type-guards both, so the same document now reaches this function as zero
    steps and leaves as a clean exit-2 `SparseError` naming the count. Fail-closed before and after,
    unreachable from a valid workflow, and the second shape is the one an operator can act on — but
    it is a change, so it is written down rather than discovered.
    """
    return [
        step.params
        for step in checkout_steps(document)
        if repository_matches(step.repository, AUTHORITY)
    ]


def parse_workflow(path: Path) -> tuple[list[str], bool]:
    """Return (patterns, cone_mode) for the workflow's FS-GG/.github checkout step.

    An empty pattern list means the step declares NO sparse-checkout — a full clone, which
    under-fetches nothing. A declared-but-unreadable one is an error, never that, and since #1530
    that sentence is finally TRUE of a bare `sparse-checkout:` too.

    The block itself, and which steps are candidates at all, are read by `scripts/lib/sparse.py`;
    what stays here is the demand for EXACTLY ONE authority step, and translating the shared module's
    refusal into this module's own exit-2 contract.
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

    try:
        # `None` from the shared reader means the key is ABSENT — a full clone. `[]` is not the same
        # fact and is never returned: a DECLARED block that yields nothing is refused there, because
        # treating it as a full clone would make every coverage assertion below pass over a workflow
        # that fetches an empty tree (#266 — "I could not tell" is not "it is fine").
        patterns = patterns_of(params, str(path))
        cone = cone_mode_of(params, str(path))
    except SparseRefusal as error:
        raise SparseError(str(error)) from error
    return (patterns if patterns is not None else []), cone


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

#!/usr/bin/env python3
"""Recompute the Wizard source-graph seal and report or fail on drift (.github#4730, #3010).

`KNOWN_GRAPH_SEAL` in scripts/new_sdd_workspace_successor_admission.py is a seal over the whole
non-report source graph of this repository. It goes stale whenever any non-report file changes, and
nothing noticed: the successor suite's `known_source_fixture` silently falls back to an earlier commit
whose tree still matches, so a stale seal looked current until a person recomputed it by hand during a
publication.

This gate recomputes the seal of the current tree with the suite's OWN readers
(`working_source_tree` / `immutable_fixture_tree` in tests/new-sdd-workspace-successor/run.py and
`graph_seal` in the admission module) and compares it with the pinned constant. It reimplements none
of them: a second sealing algorithm would be the second copy that drifts from the first.

The seal is MEANT to change with every non-report change to main, so an always-failing check would
block every unrelated merge. The outcome therefore depends on what the change touches:

  seal current ............................................................ pass
  seal stale, change touches the Wizard release surface ................... FAIL (exit 1)
  seal stale, change touches the sealed graph elsewhere ................... pass + warning
  seal stale, change touches no sealed leaf (reports only) ................ pass, informational
  seal stale, no change information (workflow_dispatch / local run) ....... pass + warning
                                                       (`--strict`: FAIL, for use at release time)

The Wizard release surface is the set of files whose change prepares or alters a Wizard release, and
for which a stale seal is a real defect. It is defined by SELFTEST_PATHS and EXTRA_RELEASE_PATHS below:
every path pattern of .github/workflows/new-sdd-workspace-selftest.yml is classified there as blocking
or warn-only with a reason, plus the release files that workflow does not list. A control in
tests/wizard-graph-seal/run.py fails when that workflow's list and this classification drift apart, and
when a script the two Wizard release workflows invoke is not on the surface.

Two classes are deliberately WARN-ONLY (decided by Root):
  - scripts/release_successor_*.py (and its tests): shared with other products' releases; release-time
    admission still fails closed on a stale seal, so a stale seal there is not silently accepted.
  - scripts/NewSddWorkspace/** : the shipped Wizard source. Source changes reseal at release time.

Exit status: 0 pass, 1 stale seal on a release-surface change (or --strict), 2 the gate could not
compute a verdict (unreadable tree, unknown base). An unusable gate is never a pass.
"""

from __future__ import annotations

import argparse
import importlib.util
import os
import pathlib
import re
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
SUITE = REPO / "tests/new-sdd-workspace-successor/run.py"
ADMISSION = "scripts/new_sdd_workspace_successor_admission.py"

# Every `pull_request:` path pattern of .github/workflows/new-sdd-workspace-selftest.yml, classified:
# True = Wizard release surface (a stale seal FAILS), False = warn-only, with the reason. The keys must equal
# that workflow's list exactly (a control enforces it), so a path added there cannot go unclassified.
SELFTEST_PATHS = {
    "scripts/NewSddWorkspace/**": False,                    # shipped Wizard source: reseals at release time (Root)
    "scripts/creator-current-coord-dependencies.*": True,   # invoked by both release workflows (preflight/acquire/stage)
    "scripts/creator_source_qualification.py": True,        # source qualification the release evidence is built on
    "scripts/creator-frozen-coord-dependencies.*": True,    # frozen-pin helper the release qualification runs
    "src/FS.GG.Coord.Cli/**": False,                        # engine source, consumed through the pinned package
    "src/FS.GG.Coord.Cli.BoardOps/**": False,
    "src/FS.GG.Coord.Cli.Kernel/**": False,
    "src/FS.GG.Coord.Cli.Lifecycle/**": False,
    "src/FS.GG.Coord.Core/**": False,
    "src/FS.GG.Coord.GitHub/**": False,
    "src/FS.GG.Telemetry.Client/**": False,
    "src/FS.GG.Telemetry.Contracts/**": False,
    "src/FS.GG.Telemetry.Dashboard/**": False,
    "src/FS.GG.Telemetry.Store/**": False,
    "tests/new-sdd-workspace/**": False,                    # tests cannot alter a release effect
    "tests/new-sdd-workspace-successor/**": False,
    "tests/release-effect-sealing/**": False,
    "tests/release-successor-execution/**": False,
    "tests/release-successor-journal/**": False,
    "scripts/new-sdd-workspace-*.py": True,                 # release manifest checker, successor publisher/artifact
    "scripts/new_sdd_workspace_successor_*.py": True,       # admission (holds KNOWN_GRAPH_SEAL), execution, provider
    "scripts/release_successor_*.py": False,                # shared across products; admission fails closed (Root)
    ".github/workflows/release-new-sdd-workspace*.yml": True,   # the two Wizard release workflows
    "Directory.Build.props": False,                         # build configuration
    "dist/dotnet/Directory.Build.props": False,
    "Directory.Packages.props": False,
    ".github/workflows/new-sdd-workspace-selftest.yml": False,   # the qualification workflow itself
    "work/2968-new-sdd-workspace-0-10-1-release/**": False,      # completed 0.10.1 preparation
    "readiness/2968-new-sdd-workspace-0-10-1-release/**": False,
}

# Release files the selftest workflow does not list.
EXTRA_RELEASE_PATHS = (
    "scripts/new_sdd_workspace_promote_recovery.py",        # invoked by release-new-sdd-workspace.yml
    "scripts/creator0*-coord-dependencies.json",            # the coherent-set pins a Wizard release builds against
)

RELEASE_SURFACE = tuple(pattern for pattern, blocking in SELFTEST_PATHS.items() if blocking) + EXTRA_RELEASE_PATHS

STALE_MESSAGE = ("Wizard seal is stale relative to this tree; "
                 "it must be resealed before the next Wizard release")

REF = re.compile(r"[0-9A-Za-z][0-9A-Za-z._/^~-]{0,199}")


class Unusable(RuntimeError):
    """The gate cannot say yes or no; this is exit 2, never a pass."""


def load_suite():
    """Import the successor suite by path, as its own helpers expect (it sets up sys.path itself)."""
    spec = importlib.util.spec_from_file_location("wizard_successor_suite", SUITE)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def glob_regex(pattern):
    """GitHub path-filter semantics: `*` stays inside a segment, `**` crosses segments."""
    out, i = [], 0
    while i < len(pattern):
        if pattern.startswith("**", i):
            out.append(".*"); i += 2
        elif pattern[i] == "*":
            out.append("[^/]*"); i += 1
        else:
            out.append(re.escape(pattern[i])); i += 1
    return re.compile("".join(out))


def release_paths(paths):
    expressions = [glob_regex(pattern) for pattern in RELEASE_SURFACE]
    return sorted(p for p in paths if any(e.fullmatch(p) for e in expressions))


def sealed_paths(paths, tree, admission):
    """Changed paths that are leaves of the sealed graph, i.e. everything except admitted report files."""
    def report(path):
        row = tree.get(path)
        return admission._report_path(path) and (row is None or (row["mode"], row["type"]) == ("100644", "blob"))
    return sorted(p for p in paths if not report(p))


def _git(arguments, repo):
    environment = {"PATH": "/usr/bin:/bin", "HOME": os.environ.get("HOME", "/"), "GIT_CONFIG_NOSYSTEM": "1",
                   "GIT_TERMINAL_PROMPT": "0", "GIT_OPTIONAL_LOCKS": "0", "GIT_NO_LAZY_FETCH": "1"}
    done = subprocess.run(["git", "--no-pager", "-C", str(repo), *arguments], capture_output=True,
                          env=environment, timeout=60)
    if done.returncode != 0:
        raise Unusable(f"git {arguments[0]} refused: {done.stderr.decode(errors='replace').strip()}")
    return done.stdout


def _plain_revision(revision):
    if not REF.fullmatch(revision) or revision.startswith("-"):
        raise Unusable(f"revision {revision!r} is not a plain revision")


def resolve(revision, repo=REPO):
    """Full commit SHA of a plain revision (the suite's immutable reader accepts only 40-hex SHAs)."""
    _plain_revision(revision)
    return _git(["rev-parse", "--verify", f"{revision}^{{commit}}"], repo).decode().strip()


def changed_paths(base, repo=REPO):
    """Paths that differ between `base` and HEAD, both sides of a rename, via fixed git verbs only."""
    _plain_revision(base)
    out = _git(["diff", "--name-only", "-z", "--no-renames", f"{base}^{{commit}}", "HEAD", "--"], repo)
    return sorted(name.decode() for name in out.split(b"\0") if name)


def verdict(computed, pinned, changed, tree, admission, strict=False):
    """Pure classification. Returns (status, exit_code, message, release_touching, sealed_touching)."""
    release = release_paths(changed) if changed is not None else []
    sealed = sealed_paths(changed, tree, admission) if changed is not None else []
    if computed == pinned:
        return "current", 0, "Wizard seal is current for this tree", release, sealed
    if changed is None:
        if strict:
            return "stale-strict", 1, f"{STALE_MESSAGE} (--strict: no change information, treated as release time)", release, sealed
        return "stale-unclassified", 0, f"{STALE_MESSAGE} (no change information was supplied)", release, sealed
    if release:
        return ("stale-release", 1,
                "Wizard seal is stale relative to this tree and this change touches the Wizard release surface "
                f"({', '.join(release)}); reseal KNOWN_GRAPH_SEAL in {ADMISSION} before merging", release, sealed)
    if sealed:
        return "stale-unrelated", 0, STALE_MESSAGE, release, sealed
    return ("stale-not-touched", 0,
            "Wizard seal is stale, but this change touches no sealed leaf (admitted progress reports only); "
            "not reported as drift on this change", release, sealed)


def emit(out, status, code, message, computed, pinned, summary_path):
    """Print the evidence and, under GitHub Actions, a workflow annotation plus a job-summary line."""
    github = os.environ.get("GITHUB_ACTIONS") == "true"
    if github and status in {"stale-unrelated", "stale-unclassified"}:
        print(f"::warning title=Wizard seal is stale::{message}", file=out)
    elif github and code == 1:
        print(f"::error title=Wizard seal is stale::{message}", file=out)
    print(f"wizard-graph-seal: verdict {status} (exit {code}): {message}", file=out)
    if summary_path:
        try:
            with open(summary_path, "a", encoding="utf-8") as summary:
                marker = ("FAILED" if code == 1 else "ok" if status == "current"
                          else "informational" if status == "stale-not-touched" else "warning")
                summary.write(f"### Wizard graph seal: {marker}\n\n- {message}\n"
                              f"- computed `{computed}`\n- pinned `{pinned}`\n\n")
        except OSError as error:
            print(f"wizard-graph-seal: job summary not written: {error}", file=out)


def main(argv=None, out=None, suite=None):
    out = out or sys.stdout
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--base", help="revision the change is measured against (diff BASE..HEAD)")
    parser.add_argument("--changed-file", action="append", metavar="PATH",
                        help="repository path the change touches; repeatable; replaces --base")
    parser.add_argument("--rev", help="seal this commit's immutable tree instead of the working tree")
    parser.add_argument("--strict", action="store_true",
                        help="a stale seal fails even without change information (use at release time)")
    args = parser.parse_args(argv)
    try:
        suite = suite or load_suite()
        admission = suite.source_admission
        tree = suite.immutable_fixture_tree(resolve(args.rev)) if args.rev else suite.working_source_tree()
        computed = admission.graph_seal(tree)
        pinned = admission.KNOWN_GRAPH_SEAL
        if args.changed_file is not None:
            if not all(args.changed_file):
                raise Unusable("an empty --changed-file path is not a path")
            changed = sorted(set(args.changed_file))
        elif args.base is not None:
            changed = changed_paths(args.base)
        else:
            changed = None
        print(f"wizard-graph-seal: tree {'rev ' + args.rev if args.rev else 'working'}, {len(tree)} entries", file=out)
        print(f"wizard-graph-seal: computed {computed}", file=out)
        print(f"wizard-graph-seal: pinned   {pinned}", file=out)
        status, code, message, release, sealed = verdict(computed, pinned, changed, tree, admission, args.strict)
        if changed is not None:
            print(f"wizard-graph-seal: {len(changed)} changed path(s); {len(sealed)} sealed; "
                  f"{len(release)} on the Wizard release surface{': ' + ', '.join(release) if release else ''}", file=out)
        emit(out, status, code, message, computed, pinned, os.environ.get("GITHUB_STEP_SUMMARY"))
        return code
    except Unusable as error:
        print(f"wizard-graph-seal: UNUSABLE: {error}", file=out)
        return 2
    except (RuntimeError, OSError, subprocess.SubprocessError) as error:
        print(f"wizard-graph-seal: UNUSABLE: could not recompute the seal: {error}", file=out)
        return 2


if __name__ == "__main__":
    sys.exit(main())

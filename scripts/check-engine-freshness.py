#!/usr/bin/env python3
"""Gate the engine's SOURCE against the version the fleet can actually restore (.github#1075, epic #266).

THE DEFECT THIS CLOSES. `feed-coherence` asserts `registry.package-version == feed(newest)` —
registry vs feed. `source-coherence` asserts `registry.version == source` — registry vs source.
Neither compares SOURCE to FEED, and that is the direction this engine has failed in four times
(.github#844, #846, #964, #1067): the fix merges, nobody releases, and every receiver keeps
`exec`ing an engine that predates it.

WHY NO VERSION COMPARISON CAN SEE IT. Every other producer's source version moves with the CHANGE
(SDD#387 reds SDD's own PR when a surface edit skips the bump), so `version` drifting ahead of
`package-version` is their legible "merged, not yet published" signal. THIS engine's `<Version>`
moves only at RELEASE time, by hand — `release-coord-engine.yml` evaluates the fsproj property and
requires the tag to match it, so the bump IS the release act. While the source outruns the feed,
`<Version>` is CONSTANT, and `version == package-version` is precisely the state the bug lives in.
The scalars are equal BECAUSE the defect is happening. So this gate counts COMMITS, not scalars.

WHAT IT ASSERTS.

    tag   = coord-engine/v<the newest version LIVE ON THE FEED>
    drift = git log <tag>..HEAD -- <the engine's source trees>

A TAG IS NOT A PUBLISH, which is why the comparison point is derived from the FEED and the tag is
then resolved from it — never "the newest tag, assumed shipped". The `fs-gg-ui-template` PHANTOM
0.9.1 in registry/dependencies.yml is the precedent: all three tags cut, zero packages exist.

THE THRESHOLD, AND WHY IT IS THE WIRE SURFACE. `drift > 0` is the obvious bar and it is the wrong
one: measured on this repo, v0.3.0 was cut at 07:01 and 33 engine commits landed in the 8 hours
after it — more than the 28 that justified v0.3.0 itself. A gate that reds the moment any engine
commit merges is red ~always, and a gate that is red by design on the happy path teaches exactly one
lesson: "FAILED is noise, merge anyway" (the #698 trade, made explicit in pnext-item §5). Age fails
for the same reason from the other side — at this velocity every unreleased commit is hours old, so
an age bar is GREEN on the very drift this issue was filed about.

So the bar is RELEVANCE, and it has a principled definition rather than a magic number:

  * `src/FS.GG.Coord.Core/Protocol.fs` is the engine's WIRE SURFACE — the exit codes and verb
    contract. It is ALSO the file `scripts/generate-projections` emits the exit-code tables into
    every worker-facing SKILL.md from (`source_of()`; 17 regions across both skill roots).
  * So when Protocol.fs has drifted unreleased, main's OWN DOCUMENTS describe an engine the fleet
    cannot run. The org contradicts itself, in the one place a worker looks to find out what a verb
    does. That is not a latent risk; it is the reported experience — a `take` exit table the engine
    disagrees with, a `say` that demands a flag the recipe says is optional, a bare issue ref the
    docs accept and the binary refuses.
  * Drift that is internal-only (a refactor behind the wire) does NOT degrade a worker's ability to
    read the docs and be right. It is REPORTED, never red.

That is "loud exactly when the fleet is degraded, silent otherwise", keyed on a fact rather than a
threshold. It catches three of the four occurrences above (#844 and #846 are literally
"next/take/done/add are all unknown command" — the verb contract; #1067 is the landable exit codes).

IT DOES NOT CATCH ALL FOUR, AND THAT IS STATED RATHER THAN HIDDEN. #964 — "#848's fix is merged but
never published, so set-field writes no single-select" — is a BEHAVIOUR regression behind an
unchanged wire contract, and this gate reports it without reddening. That is a deliberate policy on
a subject the gate can see and does print, not a blind spot: "checked, and it is below the bar" is
reported in full, with the commit list, every run. Widening the red bar to any behavioural drift is
the `drift > 0` gate above, which the velocity measurement rules out.

FAILS CLOSED, which is the whole point of epic #266. "I could not look" and "I looked, and it is
current" must never share an exit code. Every one of these is an ERROR, not a skip and never
"no drift":

  * the feed is unreachable, unauthorised, returns unparsable JSON, or serves zero versions;
  * the feed's newest version has NO matching `coord-engine/v<version>` tag (a publish with no tag,
    or a tag scheme that moved — either way the comparison point is unknown, not "current");
  * the tag exists but git cannot read it, or the repo has no commits;
  * the wire-surface file does not exist at the path this gate names (the protocol moved, and a
    hard-coded path that silently checks nothing is the #266 shape this gate exists to refuse);
  * an engine source tree named below does not exist.

Usage:  scripts/check-engine-freshness.py [--repo <dir>] [--ref HEAD]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a freshness signal,
and it refuses to run unless FSGG_ENGINE_FIXTURE_OK=1 — which only tests/engine-freshness/ sets. A
test hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = the fleet's engine carries every wire-surface commit on this ref.
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys

# The feed reader and NuGet version ordering are SHARED with check-feed-coherence.py and
# check-pin-coherence.py (.github#263) — one implementation of "what does the feed serve", so the
# gates cannot drift into disagreeing about version order. `scripts/` is not a package, so put this
# file's own directory on the path: the test harness loads this gate by path via importlib, which
# sets sys.path[0] to the TEST's directory, not to scripts/.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    ORG,
    GateError,
    feed_versions,
    is_prerelease,
    newest,
)

# The package the fleet restores and `scripts/fsgg-coord` execs (ADR-0034 §4.4).
PACKAGE = "FS.GG.Coord.Cli"

# `release-coord-engine.yml` releases on `coord-engine/v<version>` matching the fsproj <Version>.
TAG_PREFIX = "coord-engine/v"

# The trees whose commits ship in that package. A commit outside them cannot change the engine the
# fleet runs, so counting it would be noise with a gate's authority behind it.
ENGINE_SOURCE = (
    "src/FS.GG.Coord.Cli",
    "src/FS.GG.Coord.Core",
    "src/FS.GG.Coord.GitHub",
)

# The engine's wire surface — see the threshold discussion in the module docstring.
#
# THIS PATH IS COUPLED TO `scripts/generate-projections`' `source_of()`, which names the same file as
# the source of every `fsgg-protocol:*` region. The coupling is deliberate and it is checked rather
# than assumed: `_assert_exists` below reds the gate if this path stops existing, so a protocol that
# MOVES cannot leave this gate quietly measuring nothing. It cannot be read off the generator today —
# `--list` emits a kind's OUTPUTS (kind/path/marker, ADR-0044), not its source — and widening that
# contract to carry the source would reach `scripts/generated-paths`, `check-generator-list.py` and
# their fixture. Filed as the honest way to close it (.github#1075's follow-up).
WIRE_SURFACE = "src/FS.GG.Coord.Core/Protocol.fs"


def git(repo: str, *args: str) -> str:
    """Run git in `repo`, returning stdout. Any failure is a GateError — never a silent empty."""
    try:
        p = subprocess.run(
            ("git", "-C", repo, *args),
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run git: {e}") from e
    if p.returncode != 0:
        raise GateError(
            f"`git {' '.join(args)}` failed (exit {p.returncode}): {p.stderr.strip() or '(no stderr)'}"
        )
    return p.stdout


def _assert_exists(repo: str, ref: str, path: str, what: str) -> None:
    """A path this gate MEASURES must exist on the ref, or the gate is measuring nothing."""
    out = git(repo, "ls-tree", "-r", "--name-only", ref, "--", path)
    if not out.strip():
        raise GateError(
            f"{what} {path!r} does not exist at {ref}. This gate is hard-coded to measure it, so a "
            f"path that has moved would leave the gate reporting green over an unwatched subject — "
            f"the exact fails-open shape epic #266 is about. Update {os.path.basename(__file__)} to "
            f"name the new path (and check `scripts/generate-projections`' source_of(), which names "
            f"the same file)."
        )


def resolve_tag(repo: str, version: str) -> str:
    """The tag for `version`. An absent tag is an ERROR: a publish whose commit we cannot name."""
    tag = f"{TAG_PREFIX}{version}"
    try:
        git(repo, "rev-parse", "--verify", f"refs/tags/{tag}^{{commit}}")
    except GateError as e:
        raise GateError(
            f"the feed's newest {PACKAGE} is {version!r}, but this repo has no tag {tag!r} — so the "
            f"commit that produced the package the fleet restores cannot be named, and the drift "
            f"cannot be measured. This is an ERROR, not 'no drift': either the release published "
            f"without its tag, the tag was deleted, or the tag scheme moved. ({e})"
        ) from e
    return tag


def drift_commits(repo: str, tag: str, ref: str, paths: tuple[str, ...]) -> list[str]:
    """`git log tag..ref -- paths`, one 'sha subject' per line."""
    out = git(repo, "log", "--format=%h %s", f"{tag}..{ref}", "--", *paths)
    return [ln for ln in out.splitlines() if ln.strip()]


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--repo", default=".", help="the git repo to measure (default: cwd)")
    ap.add_argument("--ref", default="HEAD", help="the ref to measure drift up to (default: HEAD)")
    ap.add_argument("--fixture", help="read the feed from a JSON file (tests only, never in CI)")
    args = ap.parse_args(argv)

    if args.fixture:
        # A flag that makes the gate report green without reading the feed is precisely the
        # fails-open shape epic #266 is about, so it is not merely documented as test-only — it is
        # locked. A stray `--fixture` anywhere else (a copy-pasted CI step, a debugging line left
        # behind) fails the gate instead of quietly turning it into a no-op.
        if os.environ.get("FSGG_ENGINE_FIXTURE_OK") != "1":
            print(
                "::error::check-engine-freshness: --fixture reads a canned feed and is NOT a "
                "freshness signal. It is available only to tests/engine-freshness/, which sets "
                "FSGG_ENGINE_FIXTURE_OK=1. Refusing to run.",
                file=sys.stderr,
            )
            return 1
        # Loud on purpose: a fixture run must never be mistaken for a live-feed run in a log.
        print(f"FIXTURE MODE — reading {args.fixture}, NOT the live feed. Not a freshness signal.")

        def resolve() -> list[str]:
            try:
                table = json.load(open(args.fixture, encoding="utf-8"))
            except (OSError, ValueError) as e:
                raise GateError(f"cannot read fixture: {e}") from e
            if PACKAGE not in table:
                raise GateError(f"package {PACKAGE!r} is not on the org feed (fixture: absent)")
            vs = table[PACKAGE]
            if not vs:
                raise GateError(f"the feed served zero versions for {PACKAGE!r}")
            return list(vs)
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-engine-freshness: no GITHUB_TOKEN/GH_TOKEN in the environment. The "
                "org feed cannot be read without one, and an unreadable feed must fail the gate, "
                "not skip it.",
                file=sys.stderr,
            )
            return 1

        def resolve() -> list[str]:
            return feed_versions(PACKAGE, token)

    try:
        live = resolve()
        # The engine may not ship a prerelease at all: release-coord-engine.yml REFUSES one,
        # because the org's Renovate preset sets ignoreUnstable=true and receivers would never see
        # it. So the fleet's version is the newest STABLE, and a stray prerelease on the feed must
        # not become the comparison point.
        stable = [v for v in live if not is_prerelease(v)]
        if not stable:
            raise GateError(
                f"the feed serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
                f"release-coord-engine.yml refuses to publish a prerelease, so this feed cannot be "
                f"the fleet's engine and the comparison point is unknown."
            )
        version = newest(stable)

        # Fail closed on the subjects this gate is hard-coded to measure, BEFORE reporting anything
        # about them.
        for tree in ENGINE_SOURCE:
            _assert_exists(args.repo, args.ref, tree, "engine source tree")
        _assert_exists(args.repo, args.ref, WIRE_SURFACE, "the wire-surface file")

        tag = resolve_tag(args.repo, version)
        all_drift = drift_commits(args.repo, tag, args.ref, ENGINE_SOURCE)
        wire_drift = drift_commits(args.repo, tag, args.ref, (WIRE_SURFACE,))
    except GateError as e:
        print(f"::error::check-engine-freshness: {e}", file=sys.stderr)
        return 1

    print(
        f"the fleet restores {PACKAGE} {version} (newest on the {ORG} feed), cut at {tag}.\n"
        f"engine commits on {args.ref} since {tag}: {len(all_drift)} "
        f"({len(wire_drift)} touching the wire surface {WIRE_SURFACE})"
    )

    # ALWAYS report the drift, whatever the verdict. Drift below the red bar is a subject the gate
    # CAN see, so it is printed in full rather than collapsed into a green — "checked, and it is
    # below the bar" must be legible as such, not indistinguishable from "there is nothing here".
    if all_drift:
        print("\nunreleased engine commits (oldest first):")
        for line in reversed(all_drift):
            mark = "  WIRE " if line in wire_drift else "       "
            print(f"{mark}{line}")

    if wire_drift:
        print()
        print(
            f"::error::check-engine-freshness: the engine's WIRE SURFACE has outrun the feed — "
            f"{len(wire_drift)} commit(s) have changed {WIRE_SURFACE} since {tag}, the release that "
            f"produced the {version} every receiver restores. `scripts/generate-projections` emits "
            f"this repo's OWN exit-code tables into every worker-facing SKILL.md from that file, so "
            f"main now documents a verb contract the fleet's engine does not implement: a worker "
            f"reading the recipe and being refused by the binary is this state. Release the engine "
            f"— bump <Version> in src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj and push the matching "
            f"{TAG_PREFIX}<version> tag (release-coord-engine.yml does the rest).",
            file=sys.stderr,
        )
        return 1

    if all_drift:
        print(
            f"\nok: {len(all_drift)} unreleased engine commit(s), none touching the wire surface. "
            f"Reported, not red: the fleet's engine implements the verb contract this repo "
            f"documents. A release is still owed."
        )
        return 0

    print(f"\nok: no engine commits since {tag} — the fleet runs this ref's engine.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

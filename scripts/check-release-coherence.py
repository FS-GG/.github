#!/usr/bin/env python3
"""Gate POST-PUBLISH completion of the FS.GG.Kit / FS.GG.Drivers / coord-engine coherent set
(.github#2445, residual of .github#2409).

THE GAP THIS CLOSES. `.github#2402` made the three packages' `<Version>` values a coherent set that
cannot diverge (`$(FsggCoherentSetVersion)`, `Directory.Build.props`); `.github#2409` (PR #2441)
closed the maintainer-facing half of that — a sibling-tag PRECONDITION in each of `release-kit.yml`,
`release-drivers.yml` and `release-coord-engine.yml` refuses to publish unless the OTHER TWO
packages' own tags, at the same version, already resolve to the commit being packed. That proves
sibling-tag CO-EXISTENCE at the moment the precondition runs, never sibling publish CO-COMPLETION:
the three workflows are independently triggered by the same tag-push event, share no
`needs:`/`workflow_run` ordering, and each runs its own restore/verify/pack/push sequence with no
synchronization point after the precondition. Push all three tags together (the correct, intended
action), have the precondition pass in each of the three runs (all three tags already exist at the
same commit — that IS what "coherent-set tags" means), and then have ONE package's run fail
downstream of its own precondition — an unrelated flaky check, a transient nuget.org outage, a real
build defect — and that package silently does not publish while its two siblings do.
`check-feed-coherence.py` compares the registry's `package-version` to the feed, but only
`coord-engine` carries a `registry/dependencies.yml` row among the three (.github#2402 DEC-002) — so
a Kit/Drivers feed-version disagreement has no automated gate watching it at all, before or after
`.github#2409`.

WHY THIS DOES NOT ADD registry/dependencies.yml ROWS FOR Kit/Drivers (the item's own explicit
question). `registry/dependencies.yml`'s `package-version` field exists to compare a DECLARED PIN
against the feed (FR-007 publish-before-flip); FS.GG.Kit and FS.GG.Drivers are producer-only
packages this repository pins nowhere, so there is no pin for either to carry, and
`scripts/registry_packages.py` already explains why a bespoke `package-id` mapping lives outside the
registry rather than growing its schema for exactly this shape (ADR-0060 Option C, deferred to P2 /
.github#1261). Adding rows here would restate that decision rather than answer this item's actual
question, which is sibling-to-sibling agreement, not pin-to-feed agreement. This gate reads BOTH
feeds and the release TAGS directly, for the fixed set of three producer package ids, and asserts
nothing else about the registry.

WHY THE COMPARAND IS THE NEWEST FULLY-TAGGED TRIO, NOT "EACH PACKAGE'S OWN NEWEST" — measured live
against this repository, not assumed. A first draft of this gate compared each package's own newest
published version directly against its siblings'. Run against the real feeds (2026-08-12) that read:

    FS.GG.Kit        0.49.0     FS.GG.Drivers   0.18.0     FS.GG.Coord.Cli 0.23.0

— three DIFFERENT numbers, permanently, because the coherent-set convention (.github#2402) is new
and the three packages carried independent version lines before it: Kit's most recent release
happens to be its own newest, and Drivers/coord-engine simply have not needed one since adopting
`$(FsggCoherentSetVersion)`. Comparing "newest against newest" would have reported that state as
BROKEN forever — not because any release ever failed, but because the packages' pre-existing
histories differ, which is not this gate's subject and not a defect. A gate that reds by
construction, for a reason nobody can act on, is the exact "FAILED is noise, merge anyway" lesson
`scripts/check-engine-freshness.py` and this file's own siblings warn against.

The subject that actually exists is narrower and exactly matches what `.github#2409`'s precondition
already proves: a version V is a genuine COORDINATED RELEASE ATTEMPT iff `kit/vV`, `drivers/vV` and
`coord-engine/vV` ALL exist and ALL resolve to the SAME commit — precisely the check
`check_sibling_tag` performs inline, in `git`, before any of the three packages publish. So this gate
finds the NEWEST version for which that is true (an intersection of the three tag sets, filtered to a
single shared commit) and asserts THAT version — and only that version — is what all three packages
actually publish, on both feeds. A version nobody ever attempted as a coordinated release (Kit's lone
0.49.0 tag, published before this convention existed) is correctly never a subject at all: it has no
sibling tags to intersect with, so it never becomes a target.

WHAT IT ASSERTS.
  1. TRIO COMPLETION (the item's own scenario). Find the newest version V with `kit/vV`,
     `drivers/vV`, `coord-engine/vV` all present at one shared commit (the precondition's own test,
     re-run here as the post-publish comparand). If none exists yet, there has been no coordinated
     release since `.github#2409` landed — reported, loudly, but NOT a failure: the subject genuinely
     does not exist yet, not "exists and disagrees". If one exists, every one of the three packages
     must serve V on BOTH the org GitHub Packages feed and nuget.org. A package that does not is
     exactly the item's scenario: its precondition passed and its siblings published, but its own run
     did not complete.
  2. DUAL-PUBLISH (a package's own two feeds disagreeing with EACH OTHER, independent of the trio
     question above). For each package with readable feeds, does the org feed's own newest agree with
     nuget.org's own newest? A "no" is the other way a single release can half-complete: "Push to org
     GitHub Packages feed" succeeds and "Push byte-identical package to nuget.org" (gated on
     `vars.NUGET_ORG_PUBLISH`, a separate network call) does not. Unlike (1), this needs no shared
     baseline across packages, so it stays meaningful even while (1) reports "nothing to compare yet".

FAILS CLOSED (epic #266) on every REAL absence: an unreadable feed, a 404 package, a feed serving
zero stable versions, or `git ls-remote` failing outright is an ERROR, reported and counted, never a
skip. The one deliberate exception is stated above and is not a loophole: "no fully-coherent tag trio
exists yet" is a fact about the subject's age, always re-derived from a successful read, never assumed
or cached — the same posture `check-kit-published-coherence.py`'s tag-arm already takes on "a release
tag with no published version" (reported, never red — there is a real window, and real abandoned
tags, neither of which are this gate's subject).

Usage:  scripts/check-release-coherence.py

`--fixture-org <feed.json> --fixture-nuget <feed.json> --fixture-tags <tags.json>` serve three canned
subjects (all three required together) instead of the live feeds and `git ls-remote`. None of them is
a coherence signal on its own, and all three refuse to run unless
FSGG_RELEASE_COHERENCE_FIXTURE_OK=1 — which only tests/release-coherence/ sets. A test hook that can
silently turn the gate into a no-op is the very defect class above.

Exit 0 = either no coordinated coherent-set release has happened yet, or the newest one that has is
served, identically, by FS.GG.Kit, FS.GG.Drivers and FS.GG.Coord.Cli on both feeds — and every
package's own org-feed newest agrees with its own nuget.org newest.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.parse

# `scripts/` is not a package, so put this file's own directory on the path: the test harness loads
# this gate by path via importlib, which sets sys.path[0] to the TEST's directory, not to scripts/.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    ORG,
    GateError,
    feed_versions,
    is_prerelease,
    newest,
    nuget_org_versions,
    parse_version,
)

# The fixed coherent-set subject (.github#2402): these three, and only these three, publish through
# the sibling-tag precondition .github#2409 added, each behind its own release-tag namespace. A
# future fourth member is a decision that changes this file, not a config a caller can widen from the
# outside — the same "fixed, not discovered" shape `RELEASE_NAMESPACES` uses in
# check-kit-published-coherence.py, for the same reason: a subject list a caller can extend is a
# subject list a caller can also silently narrow.
TAG_PREFIX: dict[str, str] = {
    "FS.GG.Kit": "kit/v",
    "FS.GG.Drivers": "drivers/v",
    "FS.GG.Coord.Cli": "coord-engine/v",
}
SIBLING_PACKAGES: tuple[str, ...] = tuple(TAG_PREFIX)

ORG_FEED = f"org feed ({ORG})"
NUGET_FEED = "nuget.org"

# None of the three ever ships a prerelease (each release workflow refuses a `-preview`/`-*`
# `<Version>` outright), so the tag literal is always a bare `x.y.z` — the same BARE_TRIPLE grammar
# check-kit-published-coherence.py binds to `kit/v*` for the identical reason.
_VERSION_TAG = r"\d+\.\d+\.\d+"
_HEX40 = re.compile(r"\A[0-9a-f]{40}\Z")

DEFAULT_REPOSITORY = "FS-GG/.github"
FORGE_HOST = "github.com"


def _repository_slug() -> str:
    """`GITHUB_REPOSITORY` in CI; the literal fallback for a local run."""
    return (os.environ.get("GITHUB_REPOSITORY") or "").strip() or DEFAULT_REPOSITORY


def _repository_origin(url: str) -> tuple[str, str]:
    """`(host, owner/name)` for a git remote url, compared WHOLE — never by suffix.

    Mirrors check-kit-published-coherence.py's `_repository_origin`: a bare `endswith` test would
    accept a look-alike host or a real slug hidden in a URL fragment, either of which would let this
    gate read tags from a repository it has nothing to do with.
    """
    lowered = url.strip().lower().rstrip("/")
    if lowered.endswith(".git"):
        lowered = lowered[: -len(".git")]
    if scp := re.match(r"\Agit@([^:/]+):(.+)\Z", lowered):  # git@github.com:owner/name
        return scp.group(1), scp.group(2).strip("/")
    split = urllib.parse.urlsplit(lowered)
    host = split.netloc.rsplit("@", 1)[-1].split(":", 1)[0]  # drop userinfo and port
    return host, split.path.strip("/")


def remote_tags(remote: str, prefix: str) -> dict[str, str]:
    """`version -> peeled commit` for every `refs/tags/<prefix><x.y.z>` on `remote`.

    Peeled always wins over the tag object's own id — an annotated tag's sha is not the commit it
    names, and comparing THAT to a sibling would report every annotated release as a disagreement.
    A git failure raises. An empty answer does not: a namespace with no tags yet is part of the
    legitimate bootstrap state this gate's header explains, not a read error.
    """
    try:
        result = subprocess.run(
            ["git", "ls-remote", "--tags", remote, f"refs/tags/{prefix}*"],
            text=True,
            capture_output=True,
            check=False,
            timeout=120,
        )
    except (OSError, subprocess.SubprocessError) as e:
        raise GateError(f"cannot list {prefix}* tags on {remote!r}: {e}") from e
    if result.returncode != 0:
        detail = (result.stderr or result.stdout).strip()
        raise GateError(
            f"cannot list {prefix}* tags on {remote!r}"
            + (f": {detail}" if detail else "")
            + " — a tag set this gate cannot read is an UNRESOLVED verdict, never a passing one."
        )
    return parse_ls_remote(result.stdout, prefix)


def parse_ls_remote(text: str, prefix: str) -> dict[str, str]:
    """Parse `git ls-remote` output into `version -> peeled commit` for one tag prefix."""
    pattern = re.compile(r"\Arefs/tags/" + re.escape(prefix) + r"(" + _VERSION_TAG + r")(\^\{\})?\Z")
    direct: dict[str, str] = {}
    peeled: dict[str, str] = {}
    for lineno, raw in enumerate(text.splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split("\t")
        if len(parts) != 2:
            raise GateError(f"ls-remote line {lineno} is not `<sha>\\t<ref>`: {raw!r}")
        sha, ref = parts[0].strip().lower(), parts[1].strip()
        if not _HEX40.match(sha):
            raise GateError(f"ls-remote line {lineno} carries a non-sha object id {sha!r}")
        match = pattern.match(ref)
        if not match:
            continue  # outside this namespace's grammar — not a version any consumer can resolve.
        (peeled if match.group(2) else direct)[match.group(1)] = sha
    return {**direct, **peeled}


def newest_coherent_trio(tags: dict[str, dict[str, str]]) -> tuple[str, str] | None:
    """The newest version with `kit/vV`, `drivers/vV`, `coord-engine/vV` ALL at one shared commit.

    `tags` must carry all three packages (callers skip this when a tag read failed for any one of
    them — comparing against an unknown would manufacture either a false trio or a false gap).
    Returns `(version, commit)`, or None if no such version exists yet (the legitimate bootstrap
    state this file's header explains — not a failure).
    """
    common = set(tags[SIBLING_PACKAGES[0]])
    for pkg in SIBLING_PACKAGES[1:]:
        common &= set(tags[pkg])
    coherent: list[tuple[str, str]] = []
    for v in common:
        shas = {tags[pkg][v] for pkg in SIBLING_PACKAGES}
        if len(shas) == 1:
            coherent.append((v, next(iter(shas))))
    if not coherent:
        return None
    return max(coherent, key=lambda pair: parse_version(pair[0]))


def check_trio_completion(
    target: tuple[str, str] | None, feeds: dict[str, dict[str, list[str]]]
) -> list[str]:
    """Every readable package must serve `target`'s version on both feeds. See module docstring (1)."""
    if target is None:
        return []
    version, commit = target
    want = parse_version(version)
    problems: list[str] = []
    for pkg in SIBLING_PACKAGES:
        if pkg not in feeds:
            continue  # its own feed read already failed and is already reported.
        for feed_key, feed_label in (("org", ORG_FEED), ("nuget.org", NUGET_FEED)):
            served = feeds[pkg][feed_key]
            if not any(parse_version(v) == want for v in served):
                problems.append(
                    f"{pkg}: {feed_label} does not serve the coherent-set release {version!r} yet, "
                    f"though kit/v{version}, drivers/v{version} and coord-engine/v{version} all "
                    f"exist at commit {commit[:12]} — the sibling-tag precondition (.github#2409) "
                    f"passed for this release but {pkg}'s own publish to {feed_label} did not "
                    f"complete."
                )
    return problems


def check_dual_publish_coherence(newest_per_feed: dict[str, dict[str, str]]) -> list[str]:
    """For each readable package, does the org feed's own newest agree with nuget.org's? See (2)."""
    problems: list[str] = []
    for pkg, per_feed in newest_per_feed.items():
        org_v, nuget_v = per_feed["org"], per_feed["nuget.org"]
        if parse_version(org_v) != parse_version(nuget_v):
            problems.append(
                f"{pkg}: the {ORG_FEED} newest is {org_v!r} but {NUGET_FEED} newest is {nuget_v!r} "
                f"— the dual publish (ADR-0012/0013) did not complete on both feeds for this "
                f"package's most recent release."
            )
    return problems


def _newest_stable(package: str, feed_label: str, versions: list[str]) -> str:
    """The newest STABLE version in `versions`. Raises GateError if none is stable."""
    stable = [v for v in versions if not is_prerelease(v)]
    if not stable:
        raise GateError(
            f"{package}: the {feed_label} feed serves no stable version — only prereleases "
            f"{sorted(versions)}, and none of the coherent-set siblings ever ships one."
        )
    return newest(stable)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--fixture-org", help="read the org feed from a JSON file (tests only, never in CI)")
    ap.add_argument("--fixture-nuget", help="read nuget.org from a JSON file (tests only, never in CI)")
    ap.add_argument("--fixture-tags", help="read release tags from a JSON file (tests only, never in CI)")
    ap.add_argument("--remote", default="", help="the git remote to read release tags from (advanced; defaults to this repository)")
    args = ap.parse_args(argv)

    fixture_flags = (args.fixture_org, args.fixture_nuget, args.fixture_tags)
    if any(fixture_flags) and not all(fixture_flags):
        print(
            "::error::check-release-coherence: --fixture-org, --fixture-nuget and --fixture-tags "
            "must be given together — a fixture that reads one subject live and another canned is "
            "not a coherence signal for either.",
            file=sys.stderr,
        )
        return 1

    if args.fixture_org:
        if os.environ.get("FSGG_RELEASE_COHERENCE_FIXTURE_OK") != "1":
            print(
                "::error::check-release-coherence: --fixture-org/--fixture-nuget/--fixture-tags read "
                "canned subjects and are NOT a coherence signal. They are available only to "
                "tests/release-coherence/, which sets FSGG_RELEASE_COHERENCE_FIXTURE_OK=1. Refusing "
                "to run.",
                file=sys.stderr,
            )
            return 1
        print(
            f"FIXTURE MODE — reading {args.fixture_org} (org), {args.fixture_nuget} (nuget.org) and "
            f"{args.fixture_tags} (release tags), NOT the live subjects. Not a coherence signal."
        )
        try:
            org_table = json.load(open(args.fixture_org, encoding="utf-8"))
            nuget_table = json.load(open(args.fixture_nuget, encoding="utf-8"))
            tags_table = json.load(open(args.fixture_tags, encoding="utf-8"))
        except (OSError, ValueError) as e:
            print(f"::error::check-release-coherence: cannot read fixture: {e}", file=sys.stderr)
            return 1

        def resolve_org(pkg: str) -> list[str]:
            if pkg not in org_table:
                raise GateError(f"package {pkg!r} is not on the org feed (fixture: absent)")
            vs = org_table[pkg]
            if not vs:
                raise GateError(f"the org feed served zero versions for {pkg!r}")
            return list(vs)

        def resolve_nuget(pkg: str) -> list[str]:
            if pkg not in nuget_table:
                raise GateError(f"package {pkg!r} is not on nuget.org (fixture: absent)")
            vs = nuget_table[pkg]
            if not vs:
                raise GateError(f"nuget.org served zero versions for {pkg!r}")
            return list(vs)

        def resolve_tags(prefix: str) -> dict[str, str]:
            if prefix not in tags_table:
                raise GateError(f"no canned tag data for prefix {prefix!r}")
            return dict(tags_table[prefix])
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-release-coherence: no GITHUB_TOKEN/GH_TOKEN in the environment. The "
                "org feed cannot be read without one, and an unreadable feed must fail the gate, "
                "not skip it.",
                file=sys.stderr,
            )
            return 1
        repository = _repository_slug()
        remote = args.remote.strip() or f"https://github.com/{repository}.git"
        if _repository_origin(remote) != (FORGE_HOST, repository.lower()):
            print(
                f"::error::check-release-coherence: --remote {remote!r} is not "
                f"{FORGE_HOST}/{repository}; refusing tags from a different repository.",
                file=sys.stderr,
            )
            return 1

        def resolve_org(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

        def resolve_nuget(pkg: str) -> list[str]:
            return nuget_org_versions(pkg)

        def resolve_tags(prefix: str) -> dict[str, str]:
            return remote_tags(remote, prefix)

    problems: list[str] = []

    print(f"reading release tags for {', '.join(SIBLING_PACKAGES)}:")
    tags: dict[str, dict[str, str]] = {}
    for pkg, prefix in TAG_PREFIX.items():
        try:
            tags[pkg] = resolve_tags(prefix)
            print(f"  ok   {pkg:16} {len(tags[pkg])} {prefix}* tag(s)")
        except GateError as e:
            problems.append(str(e))

    target = newest_coherent_trio(tags) if len(tags) == len(SIBLING_PACKAGES) else None
    if target is not None:
        print(f"newest coordinated coherent-set release: {target[0]} (commit {target[1][:12]})")
    elif len(tags) == len(SIBLING_PACKAGES):
        print(
            "no coordinated coherent-set tag release found yet — kit/v*, drivers/v* and "
            "coord-engine/v* share no version at one commit. This is not a failure: .github#2409's "
            "sibling-tag precondition has simply not completed a full trio yet."
        )

    print(f"\ncomparing {', '.join(SIBLING_PACKAGES)} across the org feed and nuget.org:")
    newest_per_feed: dict[str, dict[str, str]] = {}
    feeds: dict[str, dict[str, list[str]]] = {}
    for pkg in SIBLING_PACKAGES:
        try:
            org_versions = resolve_org(pkg)
            nuget_versions = resolve_nuget(pkg)
            org_newest = _newest_stable(pkg, ORG_FEED, org_versions)
            nuget_newest = _newest_stable(pkg, NUGET_FEED, nuget_versions)
        except GateError as e:
            problems.append(str(e))
            continue
        feeds[pkg] = {"org": org_versions, "nuget.org": nuget_versions}
        newest_per_feed[pkg] = {"org": org_newest, "nuget.org": nuget_newest}
        print(f"  ok   {pkg:16} org={org_newest:12} nuget.org={nuget_newest}")

    problems += check_trio_completion(target, feeds)
    problems += check_dual_publish_coherence(newest_per_feed)

    if problems:
        print()
        for p in problems:
            print(f"::error::check-release-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-release-coherence: {len(problems)} problem(s).", file=sys.stderr)
        return 1

    print("\nok: no coherent-set completion gap found.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

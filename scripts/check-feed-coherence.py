#!/usr/bin/env python3
"""Gate the registry's `package-version` against both feeds consumers use (.github#267/#2580).

`contract-coherence.yml` validates registry/dependencies.yml against its SCHEMA and against its
PROJECTION. Nothing validated it against REALITY: the packages actually served by
nuget.pkg.github.com/FS-GG. So publish-before-flip (FR-007) — the package is live on the feed
before the registry says so — was a convention with no enforcement, and drifted three times
(.github#250; the missed fs-gg-ui-template 0.4.0-preview.1 flip; .github#263).

For every contract carrying `package-version`, this asserts:

    registry.package-version  ==  newest version live on the org feed
                              ==  newest version live on nuget.org

failing on EITHER direction, with different messages:

  * PARTIAL release  -> the feeds disagree; do not flip the registry.
  * BEHIND the feeds -> a release completed on both feeds and the registry was never flipped.
  * AHEAD of the feed -> the registry advertises a version consumers cannot restore (the FR-007
    inversion; never yet observed, and equally undetected).

FAILS CLOSED, which is the whole point of epic #266. "Nothing to check" and "checked, and it's
fine" must not share an exit code. Every one of these is an ERROR, not a skip:

  * a contract carries `package-version` but no package id is mapped below;
  * a MAPPED contract's row is still in the registry but has LOST its `package-version` — it is
    neither a subject nor a stale mapping, so before .github#2567 it left both scopes at once and
    this gate compared one fewer row and still exited 0;
  * a mapped package 404s on the feed;
  * the token is missing, or lacks `read:packages` (401/403);
  * the feed is unreachable, returns unparsable JSON, or an unrecognised shape;
  * the feed returns zero versions for a package;
  * a `package-version` is not a quoted string (YAML coerces `1.10` to the float 1.1);
  * a version literal (registry or feed) does not parse.

Comparison is by NuGet version ORDER, never by substring — the .github#268 defect class, where
`0.4.0` matches inside `0.4.0-preview.1`. Ordering is NuGet's, not strict SemVer: it admits a
4th numeric segment (governance-reference-gate-set is `1.2.1.1`, ADR-0007) and orders a release
above its own prereleases (`0.4.0` > `0.4.0-preview.1`).

Usage:  scripts/check-feed-coherence.py [registry/dependencies.yml]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a coherence
signal, and it refuses to run unless FSGG_FEED_FIXTURE_OK=1 — which only tests/feed-coherence/
sets. A test hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = the registry and the feed agree.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import yaml

# The feed reader and NuGet version ordering are SHARED with scripts/check-pin-coherence.py
# (.github#263) — one implementation of "does this literal equal what the feed serves", so the two
# gates cannot drift into disagreeing about version order. `scripts/` is not a package, so put this
# file's own directory on the path: the test harnesses load this gate by path via importlib, which
# sets sys.path[0] to the TEST's directory, not to scripts/.
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Re-exported into this module's namespace on purpose: tests/feed-coherence/feed_reader_cases.py
# reaches for `gate.feed_versions` / `gate.GateError` / `gate.newest` / `gate.parse_version`, and
# stubs the transport by patching the shared `urllib.request.urlopen`, which still intercepts.
from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    ORG,
    GateError,
    feed_versions,
    is_prerelease,
    newest,
    nuget_org_versions,
    parse_version,
)

# The contract -> package map, the subject-set definition, and the stale-mapping check are SHARED
# with scripts/feed-autofix (ADR-0060 §Amendment, .github#1260): the DETECTION half (this gate) and
# the RESPONSE half (the bot) must act on the same subjects from the same map, or "the gate reds but
# the bot cannot fix it" becomes a new silent gap. See scripts/registry_packages.py for why the map
# lives outside the registry.
from registry_packages import (  # noqa: E402  (path shim above must run first)
    CONTRACT_PACKAGES,
    mapping_problems,
    packages_for,
    subjects,
)


def _channel_versions(contract_id: str, package: str, declared: str, label: str, live: list[str]) -> list[str]:
    """Apply the registry row's stable/preview channel to one feed, failing closed."""
    if is_prerelease(declared):
        return live
    stable = [v for v in live if not is_prerelease(v)]
    if not stable:
        raise GateError(
            f"{contract_id}: {label} serves no stable version of {package} — only prereleases "
            f"{sorted(live)}, while the registry declares the stable {declared!r}, which no "
            f"consumer can restore. Publish the stable package, or move the registry to a "
            f"prerelease channel."
        )
    return stable


def check_contract(contract_id: str, declared: str, resolve_org, resolve_nuget=None) -> list[str]:
    """Compare `declared` to both feeds for every mapped package. Returns problems.

    `resolve_nuget` defaults to `resolve_org` only for the older focused unit callers that exercise
    channel policy with one synthetic feed. Production and the fixture CLI always provide both.
    """
    problems: list[str] = []
    want = parse_version(declared)
    # Channel policy, mirroring Renovate's `ignoreUnstable: true` (default.json, .github#386): a
    # STABLE registry entry ignores prerelease feed versions when deciding "newest". Without it, a
    # stray `0.6.0-preview.1` published beside stable `0.5.0` makes the feed's newest a prerelease
    # and flags the (correct) stable registry as BEHIND — reddening the daily feed-coherence job
    # while Renovate deliberately ignores that same preview. A registry entry that is ITSELF a
    # prerelease is on the preview channel, so it keeps tracking prereleases (fs-gg-audio's shape).
    # The filter lives here, not in newest(): "which channel to compare on" is this gate's policy,
    # not a version-ordering fact — and check-pin-coherence.py shares newest() unchanged.
    stable_channel = not is_prerelease(declared)
    resolve_nuget = resolve_nuget or resolve_org
    for pkg in packages_for(contract_id):
        try:
            org_live = resolve_org(pkg)
            nuget_live = resolve_nuget(pkg)
            if stable_channel:
                org_live = _channel_versions(contract_id, pkg, declared, "the org feed", org_live)
                nuget_live = _channel_versions(contract_id, pkg, declared, "nuget.org", nuget_live)
        except GateError as e:
            problems.append(str(e))
            continue
        org_top = newest(org_live)
        nuget_top = newest(nuget_live)
        if parse_version(org_top) != parse_version(nuget_top):
            problems.append(
                f"{contract_id}: PARTIAL RELEASE — {pkg} newest on the org feed is {org_top!r} but "
                f"newest on nuget.org is {nuget_top!r}. The dual publish did not complete; do not "
                f"flip the registry to either feed's private state. Resume or supersede the release."
            )
            continue
        top = org_top
        got = parse_version(top)
        if got == want:
            print(f"  ok   {contract_id:31} {pkg:33} org=nuget.org={top}")
        elif want < got:
            problems.append(
                f"{contract_id}: registry `package-version` is BEHIND both feeds — declares "
                f"{declared!r} but {pkg} newest on the org feed and nuget.org is {top!r}. A release "
                f"completed and the registry was never flipped (publish-before-flip step 2, FR-007)."
            )
        else:
            problems.append(
                f"{contract_id}: registry `package-version` is AHEAD of the feed — declares "
                f"{declared!r} but {pkg} newest on both feeds is {top!r}. The registry "
                f"advertises a version consumers cannot restore (the FR-007 inversion)."
            )
    return problems


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("registry", nargs="?", default="registry/dependencies.yml")
    ap.add_argument("--fixture", help="read the org feed from a JSON file (tests only, never in CI)")
    ap.add_argument("--fixture-nuget", help="read nuget.org from a JSON file (tests only; requires --fixture)")
    args = ap.parse_args(argv)

    try:
        doc = yaml.safe_load(open(args.registry, encoding="utf-8"))
    except OSError as e:
        print(f"::error::check-feed-coherence: cannot read registry: {e}", file=sys.stderr)
        return 1

    if args.fixture_nuget and not args.fixture:
        print("::error::check-feed-coherence: --fixture-nuget requires --fixture.", file=sys.stderr)
        return 1

    if args.fixture:
        # A flag that makes the gate report green without reading the feed is precisely the
        # fails-open shape epic #266 is about, so it is not merely documented as test-only — it is
        # locked. The fixture harness opts in via the environment; a stray `--fixture` anywhere else
        # (a copy-pasted CI step, a debugging line left behind) fails the gate instead of quietly
        # turning it into a no-op.
        if os.environ.get("FSGG_FEED_FIXTURE_OK") != "1":
            print(
                "::error::check-feed-coherence: --fixture reads a canned feed and is NOT a "
                "coherence signal. It is available only to tests/feed-coherence/, which sets "
                "FSGG_FEED_FIXTURE_OK=1. Refusing to run.",
                file=sys.stderr,
            )
            return 1
        # Loud on purpose: a fixture run must never be mistaken for a live-feed run in a log.
        nuget_fixture = args.fixture_nuget or args.fixture
        print(
            f"FIXTURE MODE — reading {args.fixture} (org) and {nuget_fixture} (nuget.org), "
            "NOT the live feeds. Not a coherence signal."
        )
        try:
            org_table = json.load(open(args.fixture, encoding="utf-8"))
            nuget_table = json.load(open(nuget_fixture, encoding="utf-8"))
        except (OSError, ValueError) as e:
            print(f"::error::check-feed-coherence: cannot read fixture: {e}", file=sys.stderr)
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
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-feed-coherence: no GITHUB_TOKEN/GH_TOKEN in the environment. The "
                "org feed cannot be read without one, and an unreadable feed must fail the gate, "
                "not skip it.",
                file=sys.stderr,
            )
            return 1

        def resolve_org(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

        def resolve_nuget(pkg: str) -> list[str]:
            return nuget_org_versions(pkg)

    contracts = doc.get("contracts") or []
    subject_rows = subjects(contracts)
    if not subject_rows:
        print(
            "::error::check-feed-coherence: no contract in the registry carries a "
            "`package-version`. Either the registry is malformed or the gate is pointed at the "
            "wrong file; either way this is not 'coherent'.",
            file=sys.stderr,
        )
        return 1

    # Reconcile CONTRACT_PACKAGES against the registry rows, in BOTH directions that can silently
    # shrink the subject set:
    #   * a mapping whose contract vanished is stale, and a stale mapping is how the next unchecked
    #     subject hides;
    #   * a mapped row that is STILL PRESENT but has lost its `package-version` is the .github#2567
    #     gap — not a subject (nothing compares it) and not a stale mapping (its id is still known),
    #     so it left both scopes at once and this gate printed "comparing 10" instead of 11 and still
    #     exited 0. "Nothing to check" and "checked, and it's fine" must not share an exit code.
    # Every problem is printed, not just the first: two rows can drift in one edit, and reporting one
    # of them would send the reader back for a second run to discover the other.
    drift = mapping_problems(contracts)
    if drift:
        for problem in drift:
            print(f"::error::check-feed-coherence: {problem}", file=sys.stderr)
        return 1

    print(f"comparing {len(subject_rows)} package-bearing contract(s) against both feeds:")
    problems: list[str] = []
    for c in subject_rows:
        cid = str(c.get("id", "")).strip()
        declared = c["package-version"]
        # An UNQUOTED `package-version: 1.10` is YAML-coerced to the float 1.1 before this gate
        # ever sees it, and `str()` would then compare the wrong literal — silently, and against a
        # version that may well exist. The registry quotes every version; require it, so a dropped
        # quote is a red gate rather than a comparison of a number nobody wrote.
        if not isinstance(declared, str):
            problems.append(
                f"{cid}: `package-version` is {type(declared).__name__} {declared!r}, not a quoted "
                f"string. YAML coerces an unquoted version (1.10 -> 1.1), so the literal the gate "
                f"compares would not be the one written. Quote it."
            )
            continue
        try:
            problems += check_contract(cid, declared, resolve_org, resolve_nuget)
        except GateError as e:
            problems.append(f"{cid}: {e}")

    if problems:
        print()
        for p in problems:
            print(f"::error::check-feed-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-feed-coherence: {len(problems)} problem(s).", file=sys.stderr)
        return 1

    print(f"\nok: every `package-version` equals the newest version live on the {ORG} feed and nuget.org.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

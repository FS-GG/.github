#!/usr/bin/env python3
"""Gate the registry's `package-version` against the live org feed (.github#267, epic #266).

`contract-coherence.yml` validates registry/dependencies.yml against its SCHEMA and against its
PROJECTION. Nothing validated it against REALITY: the packages actually served by
nuget.pkg.github.com/FS-GG. So publish-before-flip (FR-007) — the package is live on the feed
before the registry says so — was a convention with no enforcement, and drifted three times
(.github#250; the missed fs-gg-ui-template 0.4.0-preview.1 flip; .github#263).

For every contract carrying `package-version`, this asserts:

    registry.package-version  ==  newest version live on the org feed

failing on EITHER direction, with different messages:

  * BEHIND the feed  -> a release published and the registry was never flipped (the observed one).
  * AHEAD of the feed -> the registry advertises a version consumers cannot restore (the FR-007
    inversion; never yet observed, and equally undetected).

FAILS CLOSED, which is the whole point of epic #266. "Nothing to check" and "checked, and it's
fine" must not share an exit code. Every one of these is an ERROR, not a skip:

  * a contract carries `package-version` but no package id is mapped below;
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
    parse_version,
)

# contract id -> the package id(s) whose newest feed version `package-version` names.
#
# This mapping is deliberately HERE and not in the registry: adding a `package-id` field to
# dependencies.yml is a change to the registry schema, which is a versioned cross-repo contract
# owned by FS.GG.Contracts (Fsgg.Registry). That is a `contract-change` in its own right and is
# not worth coupling to this gate. The cost is that a NEW package-bearing contract must be added
# below — and forgetting to is an ERROR, not a silent skip (see `_packages_for`).
#
# fs-gg-ui-template is the awkward one: its `version` is the FRAMEWORK pin (FS.GG.UI.*) and its
# `package-version` is the TEMPLATE package. The two decouple across template-only releases, so
# only the template package is feed-comparable here.
CONTRACT_PACKAGES: dict[str, list[str]] = {
    "fsgg-contracts": ["FS.GG.Contracts"],
    "governance-reference-gate-set": ["FS.GG.Governance.ReferenceGateSet"],
    "fs-gg-ui-template": ["FS.GG.UI.Template"],
    "game-sim-core": ["FS.GG.Game.Core"],
    "game-scene-adapter": ["FS.GG.Game.Render"],
    # All four ship as one coherent set at one version; a partial publish is a real defect and
    # should be reported, so every member is compared rather than just .Core.
    "fs-gg-audio": [
        "FS.GG.Audio.Core",
        "FS.GG.Audio.Host",
        "FS.GG.Audio.Engine",
        "FS.GG.Audio.Elmish",
    ],
    # `.github` is a producer too (ADR-0039 §5) and had no row here, so this gate had no subject for
    # the one package whose staleness degrades every worker in the fleet (.github#1067).
    #
    # WHAT THIS ENTRY DOES AND DOES NOT BUY. It catches the registry falling behind the feed — publish
    # 0.4.0 and forget to flip the row, and this reds. That is real and it is the .github#250 class,
    # which has hit other rows 7+ times. It does NOT catch the engine's OWN recurring failure (source
    # merged, never published), and no entry here could: this gate compares the registry to the feed
    # and never looks at source. See the coord-engine row in registry/dependencies.yml, and .github#1075
    # for the detector that measures commits since the release tag instead of comparing two scalars.
    "coord-engine": ["FS.GG.Coord.Cli"],
}

def _packages_for(contract_id: str) -> list[str]:
    pkgs = CONTRACT_PACKAGES.get(contract_id)
    if not pkgs:
        raise GateError(
            f"contract {contract_id!r} declares a `package-version` but no package id is mapped "
            f"in CONTRACT_PACKAGES. Add it — an unmapped package-bearing contract is exactly the "
            f"unchecked subject epic #266 is about."
        )
    return pkgs


def check_contract(contract_id: str, declared: str, resolve) -> list[str]:
    """Compare `declared` to the newest feed version of each mapped package. Returns problems."""
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
    for pkg in _packages_for(contract_id):
        live = resolve(pkg)
        if stable_channel:
            stable = [v for v in live if not is_prerelease(v)]
            if not stable:
                problems.append(
                    f"{contract_id}: the feed serves no stable version of {pkg} — only prereleases "
                    f"{sorted(live)}, while the registry declares the stable {declared!r}, which no "
                    f"consumer can restore. Publish the stable package, or move the registry to a "
                    f"prerelease channel."
                )
                continue
            live = stable
        top = newest(live)
        got = parse_version(top)
        if got == want:
            print(f"  ok   {contract_id:31} {pkg:33} == {top}")
        elif want < got:
            problems.append(
                f"{contract_id}: registry `package-version` is BEHIND the feed — declares "
                f"{declared!r} but {pkg} newest on the org feed is {top!r}. A release published "
                f"and the registry was never flipped (publish-before-flip step 2, FR-007)."
            )
        else:
            problems.append(
                f"{contract_id}: registry `package-version` is AHEAD of the feed — declares "
                f"{declared!r} but {pkg} newest on the org feed is {top!r}. The registry "
                f"advertises a version consumers cannot restore (the FR-007 inversion)."
            )
    return problems


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("registry", nargs="?", default="registry/dependencies.yml")
    ap.add_argument("--fixture", help="read the feed from a JSON file (tests only, never in CI)")
    args = ap.parse_args(argv)

    try:
        doc = yaml.safe_load(open(args.registry, encoding="utf-8"))
    except OSError as e:
        print(f"::error::check-feed-coherence: cannot read registry: {e}", file=sys.stderr)
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
        print(f"FIXTURE MODE — reading {args.fixture}, NOT the live feed. Not a coherence signal.")
        try:
            table = json.load(open(args.fixture, encoding="utf-8"))
        except (OSError, ValueError) as e:
            print(f"::error::check-feed-coherence: cannot read fixture: {e}", file=sys.stderr)
            return 1

        def resolve(pkg: str) -> list[str]:
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on the org feed (fixture: absent)")
            vs = table[pkg]
            if not vs:
                raise GateError(f"the feed served zero versions for {pkg!r}")
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

        def resolve(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

    contracts = doc.get("contracts") or []
    subjects = [c for c in contracts if c.get("package-version") is not None]
    if not subjects:
        print(
            "::error::check-feed-coherence: no contract in the registry carries a "
            "`package-version`. Either the registry is malformed or the gate is pointed at the "
            "wrong file; either way this is not 'coherent'.",
            file=sys.stderr,
        )
        return 1

    # A mapping entry whose contract has vanished from the registry is stale, and a stale mapping
    # is how the next unchecked subject hides. Report it.
    known = {str(c.get("id", "")).strip() for c in contracts}
    for orphan in sorted(set(CONTRACT_PACKAGES) - known):
        print(
            f"::error::check-feed-coherence: CONTRACT_PACKAGES maps {orphan!r}, which is not a "
            f"contract in the registry. Remove the stale mapping.",
            file=sys.stderr,
        )
        return 1

    print(f"comparing {len(subjects)} package-bearing contract(s) against the org feed:")
    problems: list[str] = []
    for c in subjects:
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
            problems += check_contract(cid, declared, resolve)
        except GateError as e:
            problems.append(f"{cid}: {e}")

    if problems:
        print()
        for p in problems:
            print(f"::error::check-feed-coherence: {p}", file=sys.stderr)
        print(f"\ncheck-feed-coherence: {len(problems)} problem(s).", file=sys.stderr)
        return 1

    print(f"\nok: every `package-version` equals the newest version live on the {ORG} feed.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

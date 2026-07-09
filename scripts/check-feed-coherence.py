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
import base64
import json
import os
import re
import sys
import urllib.error
import urllib.request

import yaml

ORG = "FS-GG"

# The NuGet v3 flat-container ("PackageBaseAddress") index, which is precisely what a
# `dotnet restore` resolves a version against — so this reads REALITY as consumers see it.
#
# .github#267 proposed the packages REST API (GET /orgs/FS-GG/packages/nuget/<id>/versions).
# This uses the feed instead, for two reasons: the REST endpoint wants a classic PAT with
# `read:packages` and does not accept the run-scoped GITHUB_TOKEN, which would have forced a
# stored secret onto every caller; and it answers "what does the packages API say" rather than
# "what can a consumer restore". The feed answers the question the gate is actually asking.
# Auth is HTTP Basic (any username + a token), the same scheme `dotnet nuget add source
# --username --password` uses in contract-coherence.yml, so `packages: read` suffices.
FEED = f"https://nuget.pkg.github.com/{ORG}"

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
}

_VERSION_RE = re.compile(
    r"^(?P<nums>\d+(?:\.\d+){0,3})"      # 1 to 4 numeric segments (NuGet, not SemVer)
    r"(?:-(?P<pre>[0-9A-Za-z.-]+))?"     # optional prerelease
    r"(?:\+[0-9A-Za-z.-]+)?$"            # optional build metadata (ignored in ordering)
)


class GateError(Exception):
    """A condition under which the gate must fail rather than skip."""


def parse_version(text: str) -> tuple:
    """Return a sort key ordering NuGet versions. Raises GateError on anything unparsable.

    Numeric segments are padded to 4. A release outranks its own prereleases, so absence of a
    prerelease sorts ABOVE presence (hence the 1/0 flag). Prerelease identifiers compare
    dot-segment-wise: numeric segments numerically, numeric below alphanumeric, else
    case-insensitively.
    """
    m = _VERSION_RE.match((text or "").strip())
    if not m:
        raise GateError(f"cannot parse version literal {text!r} as a NuGet version")
    nums = [int(p) for p in m.group("nums").split(".")]
    nums += [0] * (4 - len(nums))
    pre = m.group("pre")
    if pre is None:
        return (tuple(nums), 1, ())
    ids: list[tuple] = []
    for seg in pre.split("."):
        if seg.isdigit():
            ids.append((0, int(seg), ""))
        else:
            ids.append((1, 0, seg.lower()))
    return (tuple(nums), 0, tuple(ids))


def feed_versions(package: str, token: str) -> list[str]:
    """Every version of `package` live on the org feed. Any failure raises — never returns []."""
    # Flat-container ids are lowercase (NuGet v3 §PackageBaseAddress).
    url = f"{FEED}/download/{package.lower()}/index.json"
    auth = base64.b64encode(f"x:{token}".encode()).decode()
    req = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Basic {auth}",
            "Accept": "application/json",
            "User-Agent": "fsgg-check-feed-coherence",
        },
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        if e.code in (401, 403):
            raise GateError(
                f"the feed rejected the token reading {package!r} (HTTP {e.code}). The gate "
                f"needs `read:packages`. Refusing to report green on an unreadable feed."
            ) from e
        if e.code == 404:
            raise GateError(
                f"package {package!r} is not on the org feed (HTTP 404). The registry names a "
                f"package the feed cannot serve — or the id mapping is wrong."
            ) from e
        raise GateError(f"feed read for {package!r} failed: HTTP {e.code} {e.reason}") from e
    except urllib.error.URLError as e:
        raise GateError(f"feed unreachable while reading {package!r}: {e.reason}") from e
    except ValueError as e:
        raise GateError(f"the feed returned unparsable JSON for {package!r}: {e}") from e

    versions = payload.get("versions") if isinstance(payload, dict) else None
    if not isinstance(versions, list):
        raise GateError(
            f"the feed's response for {package!r} has no `versions` list — the feed's shape "
            f"changed, and an unrecognised response must not read as 'coherent'."
        )
    if not versions:
        raise GateError(f"the feed served zero versions for {package!r}")
    return [str(v) for v in versions]


def _packages_for(contract_id: str) -> list[str]:
    pkgs = CONTRACT_PACKAGES.get(contract_id)
    if not pkgs:
        raise GateError(
            f"contract {contract_id!r} declares a `package-version` but no package id is mapped "
            f"in CONTRACT_PACKAGES. Add it — an unmapped package-bearing contract is exactly the "
            f"unchecked subject epic #266 is about."
        )
    return pkgs


def newest(versions: list[str]) -> str:
    """The greatest version by NuGet order. The feed returns creation order, not version order."""
    return max(versions, key=parse_version)


def check_contract(contract_id: str, declared: str, resolve) -> list[str]:
    """Compare `declared` to the newest feed version of each mapped package. Returns problems."""
    problems: list[str] = []
    want = parse_version(declared)
    for pkg in _packages_for(contract_id):
        live = resolve(pkg)
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

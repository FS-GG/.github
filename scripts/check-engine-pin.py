#!/usr/bin/env python3
"""Gate .github's canonical engine PIN against the newest resolvable engine (.github#1196/#2580).

THE SUBJECT OF THIS GATE. `dist/dotnet/.config/dotnet-tools.json` is `.github`'s own canonical
tool manifest. It is the subject of this gate and is not distributed to receivers: ADR-0068 /
.github#1615 retired that arrangement, as `registry/repos.yml` records. A receiver instead restores
the engine from its own repo-root `.config/dotnet-tools.json`, whose Renovate-managed pins are the
separate subject of .github#2249. Advancing this dist pin therefore does not deliver an engine to a
receiver. This canonical pin is a scalar no gate looked at:

  * `source-coherence` asserts registry.version == source        (registry vs source)
  * `feed-coherence`   asserts registry.package-version == feed   (registry vs feed)
  * `engine-freshness` counts commits since the feed's tag        (source vs feed)
  * `pin-coherence`    asserts every `# renovate:`-ANNOTATED pin  == feed-newest

The dist manifest pin is a NATIVE `dotnet-tools.json` pin — Renovate's nuget manager detects it with
no annotation — so `pin-coherence` (annotated pins only) never sees it, and none of the other three
looks at the pin at all. The result is the reported experience of .github#1196: source, registry and
feed all agree at 0.6.0 and every coherence gate is GREEN, while this canonical pin sits at 0.3.0.

WHY IT KEPT HAPPENING, AND WHY A GATE. Renovate DOES open the bump PR (the pin is a real nuget dep),
but automerge is disabled for it, so it needs a human — and the pin froze by hand at 0.1.0, 0.1.1,
0.2.0, 0.3.0 and then sat at 0.3.0 across 0.4.0 / 0.5.0 / 0.6.0 with the bump PR open and unmerged.
This is the FS.GG.SDD.Cli validator-pin saga (.github#127/#263/#566) one pin over: a Renovate-managed
pin that keeps freezing because nothing gives it teeth. `pin-coherence` was those teeth for the
annotated pin; this gate is the teeth for the native dist pin.

WHAT IT ASSERTS.

    dist/dotnet/.config/dotnet-tools.json  tools['fs.gg.coord.cli'].version
        ==
    the newest STABLE version of FS.GG.Coord.Cli live on nuget.org

THE COMPARISON POINT IS THE READ FEED, NOT THE REGISTRY, AND THAT IS DELIBERATE. Keying on
`registry.package-version` would look local and PR-cheap, but it is WRONG under publish-before-flip
(FR-007): the package is live on the feed BEFORE the registry row is flipped, so at release time the
correct pin is the newly-published, publicly resolvable version while `package-version` still names the old one. A
registry-keyed gate would then red the Renovate pin-bump PR as "AHEAD" — fighting the very mechanism
that keeps the canonical pin fresh. ADR-0039 makes nuget.org the read path used by Renovate and five
of six receivers, so it is the ground truth for the version this manifest may name. The org feed is
also read to distinguish a completed release from a partial publish: an org-ahead version is not a
pin target until nuget.org serves it. This is exactly `engine-freshness`'s reasoning: the subject
is MAIN (the pin) plus the feeds, a property no PR diff carries, fixed by merging the Renovate pin
bump — not by editing some unrelated PR. So the live check runs on main, on a schedule, and on
demand; a PR runs the fixture, whose subject IS the diff. The two gates are duals:
`engine-freshness` asks "is a release owed?" (source ahead of feed); this asks "is `.github`'s
canonical pin current?" (pin behind feed).

Two directions, two messages:

  * pin BEHIND nuget.org-newest -> `.github`'s canonical manifest names a stale engine.
  * pin AHEAD of nuget.org-newest -> the canonical manifest names an unresolvable version.
  * org feed ahead of nuget.org -> the release is partial; retain the nuget.org-resolvable pin.

FAILS CLOSED, which is the whole point of epic #266. "Nothing to check" and "checked, and it's fine"
must not share an exit code. Every one of these is an ERROR, not a skip and never "in sync":

  * the manifest is unreadable, is not JSON, or has no `tools` object;
  * the manifest has no `fs.gg.coord.cli` tool, or that tool carries no `version` string — the pin
    this gate is hard-coded to measure has moved or vanished, the #266 unwatched-subject shape;
  * the token is missing, or the feed is unreachable / unauthorised / returns an unrecognised shape;
  * the feed serves zero versions, or only prereleases (the engine ships no prerelease — see below);
  * a version literal (pin or feed) does not parse.

Comparison is by NuGet version ORDER (shared `parse_version`), never by substring — the .github#268
class where `0.4.0` matches inside `0.4.0-preview.1`.

Usage:  scripts/check-engine-pin.py [--manifest <dotnet-tools.json>]

`--fixture <feed.json>` serves a canned feed instead of the live one. It is NOT a freshness signal,
and it refuses to run unless FSGG_ENGINE_PIN_FIXTURE_OK=1 — which only tests/engine-pin/ sets. A test
hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = .github's canonical pin is the newest stable engine resolvable from nuget.org.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

# The feed reader and NuGet version ordering are SHARED with check-feed-coherence.py /
# check-engine-freshness.py (.github#263) — one implementation of "what does the feed serve", so the
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
    nuget_org_versions,
    parse_version,
)

# The package whose published version this gate compares to `.github`'s canonical manifest.
PACKAGE = "FS.GG.Coord.Cli"

# `.github`'s canonical pin. This path and tool id are the SUBJECT — a hard-coded subject that
# silently stops existing is the #266 fails-open shape, so `read_pin` reds rather than reporting "in
# sync" when either is absent.
MANIFEST = "dist/dotnet/.config/dotnet-tools.json"
TOOL_ID = "fs.gg.coord.cli"


def read_pin(manifest_path: str) -> str:
    """The `fs.gg.coord.cli` version in the tool manifest. Any absence is a GateError, never ''."""
    try:
        doc = json.load(open(manifest_path, encoding="utf-8"))
    except OSError as e:
        raise GateError(f"cannot read the tool manifest {manifest_path!r}: {e}") from e
    except ValueError as e:
        raise GateError(f"the tool manifest {manifest_path!r} is not valid JSON: {e}") from e
    tools = doc.get("tools")
    if not isinstance(tools, dict):
        raise GateError(
            f"the tool manifest {manifest_path!r} has no `tools` object — so the pin this gate "
            f"measures cannot be read, and a manifest with no pin is not 'in sync'."
        )
    tool = tools.get(TOOL_ID)
    if not isinstance(tool, dict):
        raise GateError(
            f"the tool manifest {manifest_path!r} declares no {TOOL_ID!r} tool. That is the pin the "
            f"gate measures; without it, the gate has no subject. This is an "
            f"ERROR, not 'in sync' (epic #266: a subject that has vanished must not report green)."
        )
    version = tool.get("version")
    if not isinstance(version, str):
        raise GateError(
            f"the {TOOL_ID!r} tool in {manifest_path!r} carries no string `version` (got "
            f"{version!r}). The pin this gate is hard-coded to measure is unreadable."
        )
    return version


def newest_stable(resolve, label: str) -> str:
    """The newest STABLE feed version of PACKAGE. Raises GateError on any unreadable/empty feed."""
    live = resolve(PACKAGE)
    # The engine ships no prerelease: release-coord-engine.yml refuses one, because the org's Renovate
    # preset sets ignoreUnstable=true. The canonical manifest is compared to the newest STABLE, and a
    # stray prerelease on the feed must not become the comparison point.
    stable = [v for v in live if not is_prerelease(v)]
    if not stable:
        raise GateError(
            f"{label} serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
            f"release-coord-engine.yml refuses to publish a prerelease, so the comparison point is "
            f"unknown."
        )
    return newest(stable)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument(
        "--manifest",
        default=MANIFEST,
        help=f"the dist tool manifest carrying the pin (default: {MANIFEST})",
    )
    ap.add_argument("--fixture", help="read the org feed from a JSON file (tests only, never in CI)")
    ap.add_argument("--fixture-nuget", help="read nuget.org from a JSON file (tests only; requires --fixture)")
    args = ap.parse_args(argv)

    if args.fixture_nuget and not args.fixture:
        print("::error::check-engine-pin: --fixture-nuget requires --fixture.", file=sys.stderr)
        return 1

    if args.fixture:
        # A flag that makes the gate report green without reading the feed is precisely the
        # fails-open shape epic #266 is about, so it is not merely documented as test-only — it is
        # locked. A stray `--fixture` anywhere else fails the gate instead of quietly no-opping it.
        if os.environ.get("FSGG_ENGINE_PIN_FIXTURE_OK") != "1":
            print(
                "::error::check-engine-pin: --fixture reads a canned feed and is NOT a freshness "
                "signal. It is available only to tests/engine-pin/, which sets "
                "FSGG_ENGINE_PIN_FIXTURE_OK=1. Refusing to run.",
                file=sys.stderr,
            )
            return 1
        nuget_fixture = args.fixture_nuget or args.fixture
        print(
            f"FIXTURE MODE — reading {args.fixture} (org) and {nuget_fixture} (nuget.org), "
            "NOT the live feeds. Not a freshness signal."
        )

        def fixture_versions(path: str, pkg: str, label: str) -> list[str]:
            try:
                table = json.load(open(path, encoding="utf-8"))
            except (OSError, ValueError) as e:
                raise GateError(f"cannot read fixture: {e}") from e
            if pkg not in table:
                raise GateError(f"package {pkg!r} is not on {label} (fixture: absent)")
            vs = table[pkg]
            if not vs:
                raise GateError(f"{label} served zero versions for {pkg!r}")
            return list(vs)

        def resolve_org(pkg: str) -> list[str]:
            return fixture_versions(args.fixture, pkg, "the org feed")

        def resolve_nuget(pkg: str) -> list[str]:
            return fixture_versions(nuget_fixture, pkg, "nuget.org")
    else:
        token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
        if not token:
            print(
                "::error::check-engine-pin: no GITHUB_TOKEN/GH_TOKEN in the environment. The org "
                "feed cannot be read without one, and an unreadable feed must fail the gate, not "
                "skip it.",
                file=sys.stderr,
            )
            return 1

        def resolve_org(pkg: str) -> list[str]:
            return feed_versions(pkg, token)

        def resolve_nuget(pkg: str) -> list[str]:
            return nuget_org_versions(pkg)

    try:
        pin = read_pin(args.manifest)
        org_published = newest_stable(resolve_org, "the org feed")
        resolvable = newest_stable(resolve_nuget, "nuget.org")
        want = parse_version(resolvable)
        got = parse_version(pin)
    except GateError as e:
        print(f"::error::check-engine-pin: {e}", file=sys.stderr)
        return 1

    if got == want:
        if parse_version(org_published) != want:
            print(
                f"ok: .github's canonical {TOOL_ID} pin remains {pin}, the newest {PACKAGE} "
                f"resolvable from nuget.org. The org feed is at {org_published}; this is a PARTIAL "
                f"RELEASE, so the pin must not advance until nuget.org serves the same version."
            )
            return 0
        print(
            f"ok: .github's canonical {TOOL_ID} pin is {pin}, the newest {PACKAGE} resolvable "
            f"from nuget.org; the {ORG} feed agrees."
        )
        return 0

    if got < want:
        print(
            f"::error::check-engine-pin: .github's canonical engine pin is BEHIND the published engine — "
            f"{args.manifest} pins {TOOL_ID} at {pin!r} but the newest {PACKAGE} resolvable from "
            f"nuget.org is {resolvable!r}. This canonical manifest predates {resolvable}. Bump the pin: set {TOOL_ID} to "
            f"{resolvable!r} in {args.manifest} (this is what the open `renovate/{TOOL_ID}-*` PR "
            f"does; merge it, or bump by hand in the release PR).",
            file=sys.stderr,
        )
        return 1

    print(
        f"::error::check-engine-pin: .github's canonical engine pin is AHEAD of the read feed — {args.manifest} "
        f"pins {TOOL_ID} at {pin!r} but the newest {PACKAGE} resolvable from nuget.org is {resolvable!r}. "
        f"The canonical manifest names a version the declared read path does not serve. Complete "
        f"the nuget.org publish, or lower the pin to {resolvable!r}.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

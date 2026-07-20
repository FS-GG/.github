#!/usr/bin/env python3
"""Gate the PUBLISHED FS.GG.Kit against canonical (.github#1291, epic #266).

THE DEFECT THIS CLOSES. FS.GG.Kit (ADR-0062) ships the coordination kit — the shared skills, the
`fsgg-coord` client, and the engine tool manifest `.config/dotnet-tools.json` — as ONE versioned
package. A receiver on `kit-delivery: package` MATERIALIZES that kit onto disk from its pinned
version, so *the package's content is what the fleet actually restores*. `verify-package.sh` proves
the kit is derived-correct AT PACK TIME (its manifest digests == registry/repos.lock). Nothing
rechecked it AFTER publish, as canonical moves.

So the published package is a scalar no gate looked at — the same shape as the engine pin before
`engine-pin-coherence` (.github#1196): a bump to a kit source lands on main, `repos.lock` advances,
every PACK-TIME gate stays green, and the PUBLISHED kit silently carries the old bytes until someone
republishes. That is exactly .github#1291: the `fs.gg.coord.cli` engine pin advanced 0.6.0 -> 0.7.0
(#660) in the manifest, but the published FS.GG.Kit 0.1.0 still carried the 0.6.0 manifest, so the
first receiver to MATERIALIZE FROM SCRATCH (FS.GG.Net) got a kit that drifted from canonical and
`coordination-coherence` red — while every other gate was green.

`engine-pin-coherence` guards the PIN the fleet copies; this guards the PACKAGE the fleet
materializes. They are the same #266 lesson one layer apart: a subject nothing watches goes stale in
silence.

WHAT IT ASSERTS.

    for every coordination-kit member (skill/client/config) in the NEWEST PUBLISHED FS.GG.Kit's
    shipped `kit/kit-manifest.tsv`:
        its content digest is present in registry/repos.lock

i.e. the kit the fleet can restore carries the SAME bytes canonical currently derives. A digest the
published kit ships that repos.lock no longer contains is a member that changed on main without a
republish — the published kit is stale, and a fresh materialize will drift. This mirrors
`verify-package.sh`'s stage-vs-repos.lock parity, but against the PUBLISHED artifact instead of a
freshly-staged one.

build-config members are EXCLUDED, exactly as in `verify-package.sh`: they carry no repos.lock row
(ADR-0036 pin model — their sha256 is a self-consistent integrity record, checked at materialize),
so there is nothing in repos.lock to match them against.

THE COMPARISON POINT IS nuget.org, AND THAT IS DELIBERATE. Five of the six kit receivers restore
FS.GG.* from PUBLIC nuget.org (ADR-0039); it is the registry the fleet actually materializes from,
so it is the registry the published kit must be measured on — the same "measure against what a
receiver can actually restore" reasoning as `engine-pin-coherence`. Reading is anonymous, so this
gate needs no token.

WHY THIS IS NOT A pull_request live gate (it mirrors engine-pin-coherence / engine-freshness). The
staleness is a property of MAIN (repos.lock) plus the FEED (the published kit), not of any PR's
diff: #660 changed the manifest and touched nothing under src/FS.GG.Kit/, so no path-filtered PR
trigger could ever see it, and it is fixed by REPUBLISHING (release-kit.yml), not by editing some
other PR. So the live check runs on main, on a schedule, and on demand; a PR runs the fixture, whose
subject IS the diff.

FAILS CLOSED, the whole point of epic #266. "Nothing to check" and "checked, and it's fine" must not
share an exit code. Every one of these is an ERROR, never a skip and never "coherent":

  * registry/repos.lock is unreadable or empty;
  * the feed is unreachable / unauthorised / returns an unrecognised shape, or serves zero stable
    versions (the kit ships no prerelease — the shared Renovate preset sets ignoreUnstable=true, so
    a prerelease kit would be invisible to the fabric meant to carry it);
  * the published nupkg cannot be downloaded, is not a zip, or carries no `kit/kit-manifest.tsv`;
  * that manifest is empty, malformed, or names zero coordination-kit members — the subject this
    gate measures has vanished, the #266 unwatched-subject shape;
  * a manifest row is not the fixed 4-field `kind<TAB>pkgrel<TAB>dest<TAB>sha` shape.

Comparison is by exact 64-hex digest, never by substring.

Usage:  scripts/check-kit-published-coherence.py [--lock registry/repos.lock]

`--fixture-manifest <tsv>` serves a canned kit-manifest.tsv instead of downloading the live package,
and refuses to run unless FSGG_KIT_COHERENCE_FIXTURE_OK=1 — which only tests/kit-published-coherence/
sets. A test hook that can silently turn the gate into a no-op is the very defect class above.

Exit 0 = the newest published FS.GG.Kit carries the same coordination-kit bytes canonical derives.
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import sys
import urllib.error
import urllib.request
import zipfile

# Shared feed reader + NuGet version ordering (.github#263) — one implementation of "what does the
# feed serve", so the gates cannot drift into disagreeing about version order. `scripts/` is not a
# package, so put this file's own directory on the path (the test harness loads this gate by path).
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fsgg_feed import (  # noqa: E402  (path shim above must run first)
    NUGET_ORG,
    GateError,
    is_prerelease,
    newest,
    nuget_org_versions,
)

# The package the fleet materializes (ADR-0062). Its content — not just its version — is the subject.
PACKAGE = "FS.GG.Kit"
LOCK = "registry/repos.lock"

# coordination-kit members carry a repos.lock digest; build-config members deliberately do not
# (ADR-0036), exactly as verify-package.sh partitions them.
COORDINATION_KINDS = frozenset({"skill", "client", "config"})
_HEX64 = re.compile(r"\A[0-9a-f]{64}\Z")


def read_lock_digests(lock_path: str) -> set[str]:
    """Every content digest in registry/repos.lock. Any absence/emptiness is a GateError, never set()."""
    try:
        text = open(lock_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read the canonical lock {lock_path!r}: {e}") from e
    digests = set()
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        # repos.lock rows are `<sha256>  <path>` (two-space separated, sha256sum style).
        sha = line.split(None, 1)[0].lower()
        if _HEX64.match(sha):
            digests.add(sha)
    if not digests:
        raise GateError(
            f"registry/repos.lock ({lock_path!r}) yielded no digests — the canonical set this gate "
            f"compares against is unreadable, and an empty baseline must not read as 'coherent'."
        )
    return digests


def newest_published_stable() -> str:
    """The newest STABLE FS.GG.Kit on nuget.org. Raises on any unreadable/empty/prerelease-only feed."""
    live = nuget_org_versions(PACKAGE)  # raises GateError on 404/unreachable/empty — never []
    stable = [v for v in live if not is_prerelease(v)]
    if not stable:
        raise GateError(
            f"nuget.org serves no stable version of {PACKAGE} — only prereleases {sorted(live)}. "
            f"release-kit.yml refuses to publish a prerelease (the shared Renovate preset sets "
            f"ignoreUnstable=true, so receivers would never see it), so the fleet's kit cannot be "
            f"one and the comparison point is unknown."
        )
    return newest(stable)


def _download_nupkg(version: str) -> bytes:
    """The published FS.GG.Kit@version .nupkg bytes from nuget.org. Any failure raises — never b''."""
    lid = PACKAGE.lower()
    url = f"{NUGET_ORG}/{lid}/{version}/{lid}.{version}.nupkg"
    req = urllib.request.Request(url, headers={"User-Agent": "fsgg-check-kit-coherence"})
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return resp.read()
    except urllib.error.HTTPError as e:
        raise GateError(
            f"cannot download {PACKAGE} {version} from nuget.org (HTTP {e.code} {e.reason}) — the "
            f"feed named it but will not serve it; a package this gate cannot read must fail, not skip."
        ) from e
    except urllib.error.URLError as e:
        raise GateError(f"nuget.org unreachable while downloading {PACKAGE} {version}: {e.reason}") from e


def manifest_from_nupkg(nupkg: bytes) -> str:
    """The `kit/kit-manifest.tsv` text inside a FS.GG.Kit .nupkg. Any absence is a GateError."""
    try:
        with zipfile.ZipFile(io.BytesIO(nupkg)) as z:
            try:
                return z.read("kit/kit-manifest.tsv").decode("utf-8")
            except KeyError as e:
                raise GateError(
                    "the published FS.GG.Kit carries no kit/kit-manifest.tsv — the manifest this "
                    "gate reads is gone, and a package with no manifest is not 'coherent' (#266)."
                ) from e
    except zipfile.BadZipFile as e:
        raise GateError(f"the downloaded FS.GG.Kit is not a valid .nupkg (zip): {e}") from e


def coordination_digests(manifest_tsv: str) -> dict[str, str]:
    """Map `dest -> sha` for every coordination-kit member in a kit-manifest.tsv. Raises on junk/empty."""
    out: dict[str, str] = {}
    for lineno, raw in enumerate(manifest_tsv.splitlines(), 1):
        if not raw.strip():
            continue
        parts = raw.split("\t")
        if len(parts) != 4:
            raise GateError(
                f"kit-manifest.tsv line {lineno} is not the 4-field kind<TAB>pkgrel<TAB>dest<TAB>sha "
                f"shape (got {len(parts)} field(s)): {raw!r}"
            )
        kind, _pkgrel, dest, sha = parts
        if kind not in COORDINATION_KINDS:
            continue  # build-config etc. — no repos.lock row to match against (verify-package.sh §1)
        sha = sha.strip().lower()
        if not _HEX64.match(sha):
            raise GateError(f"kit-manifest.tsv line {lineno} carries a non-sha256 digest {sha!r}")
        out[dest] = sha
    if not out:
        raise GateError(
            "the published FS.GG.Kit's manifest names zero coordination-kit members (skill/client/"
            "config) — the subject this gate measures has vanished; that is an ERROR, not 'coherent'."
        )
    return out


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--lock", default=LOCK, help=f"canonical digest lock (default: {LOCK})")
    ap.add_argument(
        "--fixture-manifest",
        help="read the published kit-manifest.tsv from a file (tests only, never in CI)",
    )
    args = ap.parse_args(argv)

    try:
        lock_digests = read_lock_digests(args.lock)

        if args.fixture_manifest:
            # A flag that lets the gate pass without reading the live package is the fails-open shape
            # epic #266 is about, so it is LOCKED, not merely documented as test-only.
            if os.environ.get("FSGG_KIT_COHERENCE_FIXTURE_OK") != "1":
                print(
                    "::error::check-kit-published-coherence: --fixture-manifest reads a canned "
                    "manifest and is NOT a coherence signal. It is available only to "
                    "tests/kit-published-coherence/, which sets FSGG_KIT_COHERENCE_FIXTURE_OK=1. "
                    "Refusing to run.",
                    file=sys.stderr,
                )
                return 1
            print(
                f"FIXTURE MODE — reading {args.fixture_manifest}, NOT the live package. "
                f"Not a coherence signal."
            )
            try:
                manifest_tsv = open(args.fixture_manifest, encoding="utf-8").read()
            except OSError as e:
                raise GateError(f"cannot read fixture manifest: {e}") from e
            version = "(fixture)"
        else:
            version = newest_published_stable()
            manifest_tsv = manifest_from_nupkg(_download_nupkg(version))

        shipped = coordination_digests(manifest_tsv)
    except GateError as e:
        print(f"::error::check-kit-published-coherence: {e}", file=sys.stderr)
        return 1

    drifted = {dest: sha for dest, sha in shipped.items() if sha not in lock_digests}
    if not drifted:
        print(
            f"ok: the newest published {PACKAGE} ({version}) carries {len(shipped)} coordination-kit "
            f"member(s), every one byte-identical to canonical registry/repos.lock. A fresh "
            f"materialize is coherent."
        )
        return 0

    lines = "\n".join(f"    {dest}  (ships {sha})" for dest, sha in sorted(drifted.items()))
    print(
        f"::error::check-kit-published-coherence: the newest published {PACKAGE} ({version}) is "
        f"STALE — it carries {len(drifted)} coordination-kit member(s) whose digest is not in the "
        f"canonical registry/repos.lock, so a receiver that materializes it drifts from canonical "
        f"and coordination-coherence reds (.github#1291):\n{lines}\n"
        f"A kit source changed on main without a republish. Bump <Version> in "
        f"src/FS.GG.Kit/FS.GG.Kit.csproj and release (tag kit/v<version> -> release-kit.yml), so "
        f"the published kit carries current canonical.",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

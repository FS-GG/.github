#!/usr/bin/env python3
"""Read the org GitHub Packages NuGet feed, and order NuGet versions.

Extracted verbatim from scripts/check-feed-coherence.py (.github#267) when a SECOND gate needed
the same two things (.github#263): scripts/check-pin-coherence.py compares an embedded version
literal against feed-newest, exactly as check-feed-coherence.py compares a registry row.

Both gates are instances of one question — "does this version literal equal what the feed actually
serves?" — so they must answer it with ONE implementation. Two copies of NuGet version ordering is
how the two copies drift, and a gate that orders versions wrongly reports green on a stale pin,
which is the epic #266 defect class this module exists to close.

Everything here FAILS CLOSED: no function in this module returns an empty list, a None, or a
sentinel to mean "could not tell". It raises GateError. A caller that wants to skip must decide to
skip in the open, in its own code.
"""
from __future__ import annotations

import base64
import json
import re
import urllib.error
import urllib.request

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

_VERSION_RE = re.compile(
    r"^(?P<nums>\d+(?:\.\d+){0,3})"      # 1 to 4 numeric segments (NuGet, not SemVer)
    r"(?:-(?P<pre>[0-9A-Za-z.-]+))?"     # optional prerelease
    r"(?:\+[0-9A-Za-z.-]+)?$"            # optional build metadata (ignored in ordering)
)


class GateError(Exception):
    """A condition under which a gate must fail rather than skip."""


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


def is_prerelease(text: str) -> bool:
    """True if `text` is a NuGet prerelease (carries a `-suffix`). Raises GateError on unparsable.

    Reuses parse_version's ordering key so "is this stable" has ONE definition, shared by every
    caller: element [1] is the release/prerelease rank (1 = release, 0 = prerelease), the same flag
    that makes a release sort above its own prereleases. A channel-aware gate (mirroring Renovate's
    `ignoreUnstable`) asks exactly this question, so it belongs beside the ordering it derives from.
    """
    return parse_version(text)[1] == 0


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


# The public registry the org preset routes FS.GG.* to (default.json). Needs no credential.
NUGET_ORG = "https://api.nuget.org/v3-flatcontainer"


def nuget_org_versions(package: str) -> list[str]:
    """Every version of `package` public on nuget.org. Anonymous. Any failure raises — never [].

    This is the registry RENOVATE resolves FS.GG.* from (default.json routes them here), so it is
    the registry a PIN must be compared against. Reading a different one than the bot reads is how
    a gate ends up demanding a bump the bot cannot see, or blessing a pin the bot could have moved.

    It takes no token, deliberately. Every FS.GG.* package is public here — all 32 of the 32 ids on
    the GitHub Packages feed, at the same latest version (.github#576). The org feed read below
    (`feed_versions`) still exists and is still correct for check-feed-coherence, which asks a
    different question: does the ORG FEED carry what the registry claims it published?
    """
    # Flat-container ids are lowercase (NuGet v3 §PackageBaseAddress).
    url = f"{NUGET_ORG}/{package.lower()}/index.json"
    req = urllib.request.Request(
        url,
        headers={"Accept": "application/json", "User-Agent": "fsgg-check-pin-coherence"},
    )
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            payload = json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        if e.code == 404:
            raise GateError(
                f"package {package!r} is not on nuget.org (HTTP 404) — but the org Renovate preset "
                f"routes FS.GG.* there, so the bot can enumerate NO versions for it and its pin can "
                f"never bump. Either publish it to nuget.org, or stop routing it there."
            ) from e
        raise GateError(f"nuget.org read for {package!r} failed: HTTP {e.code} {e.reason}") from e
    except urllib.error.URLError as e:
        raise GateError(f"nuget.org unreachable while reading {package!r}: {e.reason}") from e
    except ValueError as e:
        raise GateError(f"nuget.org returned unparsable JSON for {package!r}: {e}") from e

    versions = payload.get("versions") if isinstance(payload, dict) else None
    if not isinstance(versions, list):
        raise GateError(
            f"nuget.org's response for {package!r} has no `versions` list — the response shape "
            f"changed, and an unrecognised response must not read as 'coherent'."
        )
    if not versions:
        raise GateError(f"nuget.org served zero versions for {package!r}")
    return [str(v) for v in versions]


def newest(versions: list[str]) -> str:
    """The greatest version by NuGet order. The feed returns creation order, not version order."""
    return max(versions, key=parse_version)

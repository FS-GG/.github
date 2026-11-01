#!/usr/bin/env python3
"""Derive the coherent release route from the version bump (#5156).

Before this module every coherent release needed a hand-made "rebind" PR (#5034 for 0.114.0, #5157 for
0.114.1) that rewrote the version, the predecessor, the journal namespace and the feed-readiness choice in
two workflows, the journal module and their pinning tests. Each of those is a function of two facts that
already exist when a release is ready:

  * THE VERSION is the coherent-set scalar `FsggCoherentSetVersion` in Directory.Build.props, the one
    property all three packages take their <Version> from (check-coherent-set-version.py).
  * THE PREDECESSOR is the promoted stable channel: the `stable-channel.json` asset of the newest
    non-draft, non-prerelease `coherent-set/v*` release older than the version. Its `sourceSha` must be the
    commit the predecessor's tag points at, and its `contentId` is the digest the new manifest records.

From those it derives:

  * the journal namespace, unique per version: `coherent-set-v<version>`. Versions bound by hand before this
    module keep their original allocation (ALLOCATED, immutable), so re-running an existing release still
    reaches its own journal and a consumed journal is never reused.
  * the logical ref and Authority directory of that journal.

FAIL-CLOSED CHECKS, all kept from the hand-bound route:

  * the version must be strictly greater than the predecessor, and no promoted coherent-set release may be
    newer than the predecessor (select_predecessor);
  * the predecessor's channel must name the commit its tag points at (release-saga.py `predecessor`, fed the
    derived tag commit instead of a literal);
  * the version's tag and release must be absent (the candidate workflow, now with the derived version);
  * the predecessor's journal is consumed and is never the current journal: the namespace is unique per
    version (journal_name), and the journal module refuses a current-version intent on any other ref.

Nothing here reads the network. The workflows read the releases list, the channel and the tag, and hand
the bytes to this module, which only decides.

Usage:
  release_route.py version [--props PATH]
  release_route.py predecessor --releases RELEASES.json [--version V] [--props PATH]
  release_route.py route --channel CHANNEL.json --tag-source SHA [--version V] [--props PATH]
Exit: 0 and the derived value on stdout; 1 and `release route refused: <reason>` on stderr.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PROPS = ROOT / "Directory.Build.props"
JOURNAL_PREFIX = "refs/heads/fsgg/v2/journal/release/"
TAG_PREFIX = "coherent-set/v"

STABLE_VERSION = re.compile(r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\Z")
CONTENT_ID = re.compile(r"sha256:[0-9a-f]{64}\Z")
SOURCE_SHA = re.compile(r"[0-9a-f]{40}\Z")
_PROPERTY = re.compile(r"<FsggCoherentSetVersion>([^<]*)</FsggCoherentSetVersion>")

# Journals allocated by hand before this module, one per released version. Immutable: they are consulted
# only so that a re-run of an already-bound version reaches its own journal, and so that a predecessor's
# consumed journal is named exactly. A version absent here derives `coherent-set-v<version>`.
ALLOCATED = {
    "0.110.0": "coord110-observer-20261020-01",
    "0.111.0": "coord111-resource-aware-20261021-01",
    "0.112.0": "coord112-skill-routing-20261021-01",
    "0.113.0": "coord113-claude-usage-20261023-01",
    "0.114.0": "coord114-enrollment-context-20261031-01",
    "0.114.1": "coord1141-board-reader-20261101-01",
}


class RouteRefused(RuntimeError):
    """The route cannot be derived; nothing may proceed on it."""


def parts(version: object) -> tuple[int, int, int]:
    if not isinstance(version, str) or STABLE_VERSION.fullmatch(version) is None:
        raise RouteRefused(f"{version!r} is not a canonical stable version")
    major, minor, patch = (int(part) for part in version.split("."))
    return major, minor, patch


def coherent_version(text: str) -> str:
    """The single `FsggCoherentSetVersion` declared in Directory.Build.props text."""
    found = _PROPERTY.findall(text)
    if len(found) != 1:
        raise RouteRefused(f"Directory.Build.props declares FsggCoherentSetVersion {len(found)} times, want exactly 1")
    parts(found[0])
    return found[0]


def props_version(path: pathlib.Path = PROPS) -> str:
    try:
        return coherent_version(path.read_text(encoding="utf-8"))
    except OSError as error:
        raise RouteRefused(f"cannot read {path}: {error}") from None


def journal_name(version: str) -> str:
    parts(version)
    return ALLOCATED.get(version, f"coherent-set-v{version}")


def current(version: str) -> dict:
    """The offline half of the route: everything the version alone determines."""
    name = journal_name(version)
    return {"version": version, "logicalRef": JOURNAL_PREFIX + name, "directory": f"state/releases/{name}"}


def select_predecessor(releases: object, version: str, *, allow_current: bool = False) -> str:
    """The newest promoted coherent-set version below `version`, from a GitHub releases list.

    Refuses when a promoted coherent-set release is newer than `version` (or equal to it, unless the caller is
    re-running that version's own release), or when none is older.
    """
    target = parts(version)
    if not isinstance(releases, list):
        raise RouteRefused("the releases list is not a JSON array")
    promoted = []
    for row in releases:
        if not isinstance(row, dict):
            raise RouteRefused("a releases row is not an object")
        tag = row.get("tag_name")
        if not isinstance(tag, str) or not tag.startswith(TAG_PREFIX):
            continue
        if row.get("draft") is not False or row.get("prerelease") is not False:
            continue
        candidate = parts(tag.removeprefix(TAG_PREFIX))
        if candidate > target or (candidate == target and not allow_current):
            raise RouteRefused(f"promoted {tag} is not older than {version}; the version must be strictly greater")
        if candidate < target:
            promoted.append(candidate)
    if not promoted:
        raise RouteRefused(f"no promoted coherent-set release is older than {version}")
    return ".".join(str(part) for part in max(promoted))


def channel_identity(raw: bytes | str) -> dict:
    """The promoted stable channel's identity, with release-saga.py's stable_channel_identity checks."""
    try:
        receipt = json.loads(raw)
    except (ValueError, TypeError) as error:
        raise RouteRefused(f"stable channel is not JSON: {error}") from None
    if not isinstance(receipt, dict):
        raise RouteRefused("stable channel is not a JSON object")
    version, content_id, source_sha, promoted_at = (receipt.get(key) for key in
                                                     ("version", "contentId", "sourceSha", "promotedAt"))
    parts(version)
    if not isinstance(content_id, str) or CONTENT_ID.fullmatch(content_id) is None:
        raise RouteRefused("stable channel has a malformed contentId")
    if not isinstance(source_sha, str) or SOURCE_SHA.fullmatch(source_sha) is None:
        raise RouteRefused("stable channel has a malformed sourceSha")
    if not isinstance(promoted_at, str) or not promoted_at.endswith("Z"):
        raise RouteRefused("stable channel has a missing or malformed promotedAt")
    try:
        dt.datetime.fromisoformat(promoted_at.removesuffix("Z") + "+00:00")
    except ValueError:
        raise RouteRefused("stable channel has a malformed promotedAt") from None
    return {"version": version, "sourceSha": source_sha, "contentId": content_id}


def route(version: str, identity: dict, tag_source: str) -> dict:
    """The whole route: the version's journal over the promoted predecessor `identity`."""
    if parts(identity["version"]) >= parts(version):
        raise RouteRefused(f"version {version} is not strictly greater than predecessor {identity['version']}")
    if not isinstance(tag_source, str) or SOURCE_SHA.fullmatch(tag_source) is None:
        raise RouteRefused("the predecessor tag commit is malformed")
    if tag_source != identity["sourceSha"]:
        raise RouteRefused(f"predecessor channel source {identity['sourceSha']} is not its tag's commit {tag_source}")
    value = current(version)
    if value["directory"] == current(identity["version"])["directory"]:
        raise RouteRefused("the current journal is the predecessor's consumed journal")
    value.update(predecessor=identity["version"], predecessorSource=identity["sourceSha"],
                 predecessorContentId=identity["contentId"])
    return value


class CurrentRelease(dict):
    """CURRENT_RELEASE: the version and journal at import, the predecessor once bound.

    Reading a predecessor key before bind() raises RouteRefused, so no check can compare against an
    absent predecessor and pass. The publisher binds it from the authenticated candidate's channel after
    reading that channel's promoted release and tag live.
    """
    _PREDECESSOR = ("predecessor", "predecessorSource", "predecessorContentId")

    def __missing__(self, key):
        if key in self._PREDECESSOR:
            raise RouteRefused(f"the current release's {key} is not bound yet")
        raise KeyError(key)

    def bound(self) -> bool:
        return all(key in self for key in self._PREDECESSOR)

    def bind(self, identity: dict) -> None:
        if parts(identity["version"]) >= parts(self["version"]):
            raise RouteRefused(f"version {self['version']} is not strictly greater than predecessor {identity['version']}")
        value = {"predecessor": identity["version"], "predecessorSource": identity["sourceSha"],
                 "predecessorContentId": identity["contentId"]}
        if self.bound() and {key: self[key] for key in self._PREDECESSOR} != value:
            raise RouteRefused("the current release is already bound to a different predecessor")
        if not isinstance(value["predecessorSource"], str) or SOURCE_SHA.fullmatch(value["predecessorSource"]) is None:
            raise RouteRefused("the predecessor source is malformed")
        if not isinstance(value["predecessorContentId"], str) or CONTENT_ID.fullmatch(value["predecessorContentId"]) is None:
            raise RouteRefused("the predecessor content id is malformed")
        self.update(value)


def current_release(path: pathlib.Path = PROPS) -> CurrentRelease:
    return CurrentRelease(current(props_version(path)))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("version", "predecessor", "route"):
        command = sub.add_parser(name)
        command.add_argument("--props", type=pathlib.Path, default=PROPS)
        if name != "version":
            command.add_argument("--version", dest="override")
    sub.choices["predecessor"].add_argument("--releases", type=pathlib.Path, required=True)
    sub.choices["route"].add_argument("--channel", type=pathlib.Path, required=True)
    sub.choices["route"].add_argument("--tag-source", required=True)
    args = parser.parse_args(argv)
    try:
        version = props_version(args.props)
        if getattr(args, "override", None) not in (None, version):
            raise RouteRefused(f"--version {args.override} is not the props version {version}")
        if args.command == "version":
            print(version)
        elif args.command == "predecessor":
            print(select_predecessor(json.loads(args.releases.read_text(encoding="utf-8")), version))
        else:
            identity = channel_identity(args.channel.read_bytes())
            print(json.dumps(route(version, identity, args.tag_source), sort_keys=True, separators=(",", ":")))
    except (RouteRefused, OSError, ValueError) as error:
        print(f"release route refused: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

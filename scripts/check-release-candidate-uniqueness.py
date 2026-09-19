#!/usr/bin/env python3
"""Read both package feeds before preparing an unpublished coherent-set candidate.

This is a freshness check, not effect admission. The eventual publisher must repeat
the read immediately before each write and reconcile a partially published version.
"""

from __future__ import annotations

import argparse
import os
import sys

from fsgg_feed import GateError, feed_versions, newest, nuget_org_versions, parse_version

PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")


def check(version: str, predecessor: str, token: str) -> list[dict[str, str]]:
    if not token:
        raise GateError("GITHUB_TOKEN with packages: read is required")
    candidate = parse_version(version)
    baseline = parse_version(predecessor)
    if candidate <= baseline or candidate[1] != 1:
        raise GateError("candidate must be a stable version newer than the predecessor")
    rows = []
    for package in PACKAGES:
        for feed, versions in (
            ("github", feed_versions(package, token)),
            ("nuget", nuget_org_versions(package)),
        ):
            if version in versions:
                raise GateError(f"{package} {version} already exists on {feed}")
            latest = newest(versions)
            if parse_version(latest) != baseline:
                raise GateError(
                    f"{package} on {feed} is at {latest}, not predecessor {predecessor}"
                )
            rows.append({"package": package, "feed": feed, "latest": latest})
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--version", required=True)
    parser.add_argument("--predecessor", required=True)
    args = parser.parse_args()
    try:
        rows = check(args.version, args.predecessor, os.environ.get("GITHUB_TOKEN", ""))
    except (GateError, ValueError) as error:
        print(f"release candidate uniqueness refused: {error}", file=sys.stderr)
        return 1
    for row in rows:
        print(f"{row['feed']} {row['package']}: latest {row['latest']}; {args.version} absent")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Require FS.GG.Coord.Cli's release notes to announce the version they ship (.github#1762).

The package listing renders `PackageReleaseNotes`; the release workflow publishes `Version`. The
0.13.0 cut moved only the latter, so consumers saw 0.12.0's breaking-change announcement attached
to 0.13.0. Both values are evaluated MSBuild properties, so this checker asks MSBuild once and
requires the first whitespace-delimited token of the notes to equal the evaluated version.

It deliberately does not judge prose quality. The mechanical invariant catches the measured stale
release without inventing a style gate.

WHERE THE ANNOUNCED VERSION COMES FROM, AND WHY THIS CHECKER NAMES IT (.github#2512). `<Version>` in
the engine project is not a literal — it is `$(FsggCoherentSetVersion)` (.github#2402), declared once
in `Directory.Build.props`. So the left-hand side of this comparison is authored in a DIFFERENT FILE
from the one being evaluated, and this checker reads both: it requires the evaluated `Version` to
resolve from a non-empty `FsggCoherentSetVersion`, so "which version is being announced" can never be
answered by a stray independent literal that this gate would then happily bless.

Exit 0 = coherent. Exit 1 = evaluated values disagree or are empty. Exit 2 = could not evaluate.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys


DEFAULT_PROJECT = "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"

# The file that declares the scalar `<Version>` resolves from. This is a SUBJECT of the comparison,
# not a neighbour of it: move `FsggCoherentSetVersion` here and you have moved the version this gate
# checks the notes against, without touching DEFAULT_PROJECT at all.
VERSION_SOURCE = "Directory.Build.props"
COHERENT_SET_PROPERTY = "FsggCoherentSetVersion"

# Rule (c) of check-paths-coherence reads this declaration. The workflow must rerun when the values
# being compared move; the checker/fixture/workflow paths name their own implementation separately.
#
# VERSION_SOURCE IS IN THIS TUPLE BECAUSE .github#2512 MEASURED WHAT ITS ABSENCE COSTS. This
# declaration used to name DEFAULT_PROJECT alone, so `engine-release-notes.yml` selected only the
# project file. PR #2507 then moved `FsggCoherentSetVersion` 0.50.4 -> 0.50.5 in VERSION_SOURCE and
# never touched DEFAULT_PROJECT — so no path in the filter matched, this gate NEVER RAN on the PR
# that created the mismatch, and it first spoke at RELEASE time. By then FS.GG.Kit 0.50.5 and
# FS.GG.Drivers 0.50.5 had published irrevocably and tags are immutable, so 0.50.5 is a PERMANENT
# two-of-three set (see registry/dependencies.yml). Half of this comparison's inputs sat outside its
# own trigger; a gate that cannot see one operand move is not a gate on that operand.
PATHS_SUBJECT = (DEFAULT_PROJECT, VERSION_SOURCE)


def evaluated_properties(project: str) -> tuple[str, str, str]:
    try:
        run = subprocess.run(
            [
                "dotnet",
                "msbuild",
                project,
                "-getProperty:Version",
                f"-getProperty:{COHERENT_SET_PROPERTY}",
                "-getProperty:PackageReleaseNotes",
            ],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as exc:
        raise RuntimeError(f"cannot run dotnet msbuild: {exc}") from exc

    if run.returncode != 0:
        detail = run.stderr.strip() or run.stdout.strip() or "(no diagnostic)"
        raise RuntimeError(f"dotnet msbuild could not evaluate {project!r}: {detail}")
    try:
        payload = json.loads(run.stdout)
        properties = payload["Properties"]
        version = properties["Version"]
        coherent = properties[COHERENT_SET_PROPERTY]
        notes = properties["PackageReleaseNotes"]
    except (ValueError, KeyError, TypeError) as exc:
        raise RuntimeError(
            f"dotnet msbuild returned no readable Version/{COHERENT_SET_PROPERTY}/"
            "PackageReleaseNotes property document"
        ) from exc
    if not all(isinstance(value, str) for value in (version, coherent, notes)):
        raise RuntimeError(
            f"dotnet msbuild returned a non-text Version, {COHERENT_SET_PROPERTY} or "
            "PackageReleaseNotes value"
        )
    return version.strip(), coherent.strip(), notes.strip()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=DEFAULT_PROJECT)
    args = parser.parse_args(argv)

    try:
        version, coherent, notes = evaluated_properties(args.project)
    except RuntimeError as exc:
        print(f"::error::check-engine-release-notes: {exc}", file=sys.stderr)
        return 2

    if not version:
        print(
            f"::error::check-engine-release-notes: evaluated Version is empty in {args.project}.",
            file=sys.stderr,
        )
        return 1
    if not coherent:
        print(
            f"::error::check-engine-release-notes: evaluated Version is {version}, but "
            f"{COHERENT_SET_PROPERTY} is empty or undeclared. {args.project}'s <Version> is meant "
            f"to resolve from that scalar ({VERSION_SOURCE}), so this gate cannot tell which "
            "version the notes are being checked against.",
            file=sys.stderr,
        )
        return 1
    if coherent != version:
        print(
            f"::error::check-engine-release-notes: evaluated Version is {version}, but "
            f"{COHERENT_SET_PROPERTY} is {coherent}. {args.project} is announcing a version that "
            f"did not come from the coherent-set scalar in {VERSION_SOURCE}.",
            file=sys.stderr,
        )
        return 1
    if not notes:
        print(
            f"::error::check-engine-release-notes: PackageReleaseNotes is empty for Version "
            f"{version} in {args.project}. Consumers would receive a release with no announcement.",
            file=sys.stderr,
        )
        return 1

    announced = notes.split(maxsplit=1)[0]
    if announced != version:
        print(
            f"::error::check-engine-release-notes: Version is {version}, but "
            f"PackageReleaseNotes begins with {announced!r}. The package listing would announce "
            f"another release's notes. Begin PackageReleaseNotes with {version}.",
            file=sys.stderr,
        )
        return 1

    print(
        f"ok: {os.path.basename(args.project)} Version {version} agrees with the first "
        f"PackageReleaseNotes token, and resolves from {COHERENT_SET_PROPERTY} in {VERSION_SOURCE}."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

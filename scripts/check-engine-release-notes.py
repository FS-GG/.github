#!/usr/bin/env python3
"""Require FS.GG.Coord.Cli's release notes to announce the version they ship (.github#1762).

The package listing renders `PackageReleaseNotes`; the release workflow publishes `Version`. The
0.13.0 cut moved only the latter, so consumers saw 0.12.0's breaking-change announcement attached
to 0.13.0. Both values are evaluated MSBuild properties, so this checker asks MSBuild once and
requires the first whitespace-delimited token of the notes to equal the evaluated version.

It deliberately does not judge prose quality. The mechanical invariant catches the measured stale
release without inventing a style gate.

Exit 0 = coherent. Exit 1 = evaluated values disagree or are empty. Exit 2 = could not evaluate.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys


DEFAULT_PROJECT = "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"

# Rule (c) of check-paths-coherence reads this declaration. The workflow must rerun when the values
# being compared move; the checker/fixture/workflow paths name their own implementation separately.
PATHS_SUBJECT = (DEFAULT_PROJECT,)


def evaluated_properties(project: str) -> tuple[str, str]:
    try:
        run = subprocess.run(
            [
                "dotnet",
                "msbuild",
                project,
                "-getProperty:Version",
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
        notes = properties["PackageReleaseNotes"]
    except (ValueError, KeyError, TypeError) as exc:
        raise RuntimeError(
            "dotnet msbuild returned no readable Version/PackageReleaseNotes property document"
        ) from exc
    if not isinstance(version, str) or not isinstance(notes, str):
        raise RuntimeError("dotnet msbuild returned a non-text Version or PackageReleaseNotes value")
    return version.strip(), notes.strip()


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", default=DEFAULT_PROJECT)
    args = parser.parse_args(argv)

    try:
        version, notes = evaluated_properties(args.project)
    except RuntimeError as exc:
        print(f"::error::check-engine-release-notes: {exc}", file=sys.stderr)
        return 2

    if not version:
        print(
            f"::error::check-engine-release-notes: evaluated Version is empty in {args.project}.",
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
        f"PackageReleaseNotes token."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

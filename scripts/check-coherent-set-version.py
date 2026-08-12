#!/usr/bin/env python3
"""Gate FS.GG.Kit, FS.GG.Drivers and coord-engine as ONE coherent-set version (.github#2402).

THE DEFECT THIS CLOSES. The three packages this repo publishes used to carry three independent,
hand-advanced `<Version>` literals — one per `.csproj`/`.fsproj` — and frequently ship the same
behaviour change. On 2026-08-10 that produced exactly the drift this gate now makes structurally
unrepresentable: `FS.GG.Kit` bumped to 0.49.0 and `FS.GG.Drivers` to 0.18.0 for the SAME item
(.github#2135), twenty minutes apart (commits f26da6ed / d48e1ec2), while `coord-engine` stayed at
0.23.0 — three scalars, no gate comparing any two of them to each other.

THE FIX. One MSBuild property, `$(FsggCoherentSetVersion)` (declared once in
`Directory.Build.props`), and each of the three project files resolves its own `<Version>` from it
instead of declaring an independent literal. This gate asserts BOTH halves of that claim, because
either alone is insufficient:

  1. STRUCTURAL — each project's `<Version>` ELEMENT, as authored (source text, not evaluated),
     is exactly `$(FsggCoherentSetVersion)`. This is the AC1 requirement literally: "no
     `.csproj`/`.fsproj` carries an independent `<Version>`". Checking only the EVALUATED value
     (below) would pass three independent literals that merely happen to agree today — exactly the
     shape that let the 2026-08-10 drift happen unnoticed until it didn't agree.
  2. SEMANTIC — `dotnet msbuild -getProperty:Version`, EVALUATED (never a grep — the reasoning
     `check-engine-freshness.py`/`check-engine-release-notes.py` already establish: a raw-text read
     of `<Version>$(FsggCoherentSetVersion)</Version>` would capture the literal token
     `$(FsggCoherentSetVersion)`, not a version), for all three projects, agrees with each other AND
     with `Directory.Build.props`'s own declared `FsggCoherentSetVersion` value. This proves the
     reference actually resolves, not merely that the source text looks right.

FAILS CLOSED (epic #266). "Nothing to check" and "checked, and it's fine" must not share an exit
code. Every one of these is an ERROR, not a skip and never "coherent":

  * `Directory.Build.props` is unreadable, or declares zero or more than one `FsggCoherentSetVersion`
    property — the gate's own subject scalar has vanished or become ambiguous;
  * any of the three project files is unreadable, unparsable as MSBuild, or does not resolve a
    `Version` property (a malformed, missing-`<Version>`, or non-`PropertyGroup` shape);
  * a project's `<Version>` ELEMENT text is not exactly `$(FsggCoherentSetVersion)` (an independent
    literal, or a differently-spelled property reference);
  * the three EVALUATED `Version` values are not all identical, or disagree with the declared
    `FsggCoherentSetVersion` literal.

Usage:  scripts/check-coherent-set-version.py
        (no arguments; the three project paths and the shared-property file are the fixed subject)
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys

PROPS_FILE = "Directory.Build.props"
PROPERTY_NAME = "FsggCoherentSetVersion"

# The coherent set (.github#2402): every package this repo publishes whose <Version> is meant to
# move in lockstep. Adding a fourth member is a deliberate edit to this tuple, not a config file —
# the whole point is that the set is closed and every member is named here, once.
PROJECTS = (
    "src/FS.GG.Kit/FS.GG.Kit.csproj",
    "src/FS.GG.Drivers/FS.GG.Drivers.csproj",
    "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj",
)

EXPECTED_VERSION_ELEMENT = f"<Version>$({PROPERTY_NAME})</Version>"

_PROPERTY_ELEMENT = re.compile(
    r"<" + PROPERTY_NAME + r">\s*([^<\s][^<]*?)\s*</" + PROPERTY_NAME + r">"
)


class GateError(Exception):
    """A subject this gate is hard-coded to measure could not be read or evaluated."""


def declared_set_version(props_path: str) -> str:
    """The single `FsggCoherentSetVersion` value `Directory.Build.props` declares."""
    try:
        text = open(props_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read {props_path!r}: {e}") from e
    found = _PROPERTY_ELEMENT.findall(text)
    if len(found) != 1:
        raise GateError(
            f"{props_path} declares {len(found)} <{PROPERTY_NAME}> element(s); this gate needs "
            f"exactly one shared scalar to compare every project against."
        )
    return found[0]


def project_version_element(project_path: str) -> str:
    """The project's own `<Version>` element text, exactly as authored (never evaluated)."""
    try:
        text = open(project_path, encoding="utf-8").read()
    except OSError as e:
        raise GateError(f"cannot read {project_path!r}: {e}") from e
    found = re.findall(r"<Version>\s*([^<\s][^<]*?)\s*</Version>", text)
    if len(found) != 1:
        raise GateError(
            f"{project_path} declares {len(found)} <Version> element(s); this gate needs exactly "
            f"one to check it references the shared property."
        )
    return found[0]


def evaluated_version(project_path: str) -> str:
    """The EVALUATED `Version` MSBuild property. Never a grep — see the module docstring."""
    try:
        run = subprocess.run(
            ["dotnet", "msbuild", project_path, "-getProperty:Version"],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as e:
        raise GateError(f"cannot run dotnet msbuild to evaluate {project_path!r}: {e}") from e
    if run.returncode != 0:
        detail = run.stderr.strip() or run.stdout.strip() or "(no diagnostic)"
        raise GateError(f"dotnet msbuild could not evaluate {project_path!r}: {detail}")
    version = run.stdout.strip()
    if not version:
        raise GateError(f"{project_path} evaluates to an empty Version.")
    return version


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--props", default=PROPS_FILE, help=f"the shared-property file (default: {PROPS_FILE})")
    ap.add_argument(
        "--project",
        action="append",
        dest="projects",
        default=None,
        help="a coherent-set project path (repeatable); default: the three declared members",
    )
    args = ap.parse_args(argv)
    projects = tuple(args.projects) if args.projects else PROJECTS

    try:
        declared = declared_set_version(args.props)
    except GateError as e:
        print(f"::error::check-coherent-set-version: {e}", file=sys.stderr)
        return 1

    structural_errors: list[str] = []
    semantic_versions: dict[str, str] = {}
    for project in projects:
        try:
            element = project_version_element(project)
        except GateError as e:
            print(f"::error::check-coherent-set-version: {e}", file=sys.stderr)
            return 1
        if element != f"$({PROPERTY_NAME})":
            structural_errors.append(
                f"{project} declares <Version>{element}</Version>, not {EXPECTED_VERSION_ELEMENT!r} "
                f"— an independent literal, reintroducing exactly the drift this gate exists to make "
                f"unrepresentable."
            )
        try:
            semantic_versions[project] = evaluated_version(project)
        except GateError as e:
            print(f"::error::check-coherent-set-version: {e}", file=sys.stderr)
            return 1

    if structural_errors:
        for msg in structural_errors:
            print(f"::error::check-coherent-set-version: {msg}", file=sys.stderr)
        return 1

    disagreements = {p: v for p, v in semantic_versions.items() if v != declared}
    if disagreements:
        detail = "; ".join(f"{p} evaluates to {v!r}" for p, v in sorted(disagreements.items()))
        print(
            f"::error::check-coherent-set-version: {args.props} declares {PROPERTY_NAME}="
            f"{declared!r}, but {detail}. Every coherent-set member must evaluate to the same "
            f"version as the shared property.",
            file=sys.stderr,
        )
        return 1

    names = ", ".join(projects)
    print(
        f"ok: {names} all reference ${{{PROPERTY_NAME}}} and evaluate to {declared!r} — the "
        f"coherent set cannot diverge."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

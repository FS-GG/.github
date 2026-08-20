#!/usr/bin/env python3
"""Refuse an `FS.GG.Coord.Cli` package whose compiled entries embed the checkout path (.github#2688).

WHAT THIS GATE IS FOR, AND WHAT IT DELIBERATELY DOES NOT CLAIM.

`.github#2688` measured two independent reasons a re-pack of one unchanged tree produces different
`payloadSha256` values, and they have opposite answers:

  1. THE CHECKOUT PATH. The repository-built coord projects, each as `.dll` and `.pdb`, embedded the
     absolute directory the package
     happened to be built in. This is fixed, by `DeterministicSourcePaths` on all the coord projects,
     and THIS GATE IS WHAT KEEPS IT FIXED. The production CLI's live ProjectReference graph currently
     yields TEN entries; the gate derives that set rather than repeating it, and grades each for the
     same reason.
  2. AN F# CLOSURE-NAME RACE inside the compiler, which no build setting on SDK 10.0.302 can reach.
     That half is bounded and recorded in `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj`, and this gate
     says nothing about it. A gate that pretended to grade it would have to sample dozens of clean
     compiles to be worth anything, and would still be a probabilistic green.

So the subject here is exactly the half that is deterministic: build the package anywhere, and no
entry may name where. That is not cosmetic. It is the precondition for `payloadSha256` to be
comparable ACROSS machines at all — what `.github#2428` needs in order to compare a locally packed
artifact against the bytes a feed already serves, and what made three agents on `.github#2688` report
three different `payload_id` values for one unchanged tree.

WHY IT GRADES THE PACKAGE AND NOT THE PROJECT FILES. Reading `DeterministicSourcePaths` out of project
files would pass on a tree where the property is set in a place MSBuild ignores — which is
not hypothetical here: `.github#2688` measured the F# SDK setting `ParallelCompilation` and then
passing `@(ParallelCompilation)`, an item reference to a property, so the value never reached the
compiler. A property that is declared and inert is exactly the failure this repo has already paid for
once, so this gate executes the build and grades the bytes it produced.

WHY IT REFUSES EMPTY INPUTS. "Found no path" and "graded no entries" would otherwise share an exit
code, which is `#266`'s shape. A production project with no ProjectReferences and a package with none
of the derived coord assemblies are refusals, not smaller successful inventories.

No `PATHS_SUBJECT` is declared: this gate reads no repository file. Its input is the artifact
`dotnet pack` produces, and `check-paths-coherence.py` rule (c) binds only a gate that declares one.
"""

from __future__ import annotations

import argparse
import glob
import pathlib
import shutil
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET

# The project whose package is the subject, and the assemblies this repository itself builds
# into it. `FSharp.Core` and its satellite resources are NuGet payload — not built here, not ours to
# grade, and not affected by any property this repository sets.
PROJECT = "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
GRADED_SUFFIXES = (".dll", ".pdb")

# What `DeterministicSourcePaths` maps the source root to. Its ABSENCE is a failure as much as the
# checkout path's presence is: it is the non-vacuity half of the question, and it is what separates
# "this entry was path-mapped" from "this entry happens not to mention the string we searched for".
MAPPED_ROOT = b"/_/"


def project_inventory(root: pathlib.Path) -> tuple[tuple[pathlib.Path, str], ...]:
    """Read the production project's direct project references and their emitted assembly names.

    The CLI project is the independent producer inventory: adding or removing a production reference
    must move this gate without a second hand-edited list. The primary project itself is appended after
    the references because its output ships beside theirs.
    """
    project = root / PROJECT
    document = ET.parse(project)
    includes = [node.attrib.get("Include", "") for node in document.findall(".//ProjectReference")]
    if not includes:
        raise ValueError(
            f"production project-reference inventory is empty in {PROJECT}; "
            "refusing a vacuous production graph"
        )

    paths = [(project.parent / include).resolve() for include in includes]
    paths.append(project.resolve())
    inventory: list[tuple[pathlib.Path, str]] = []

    for path in paths:
        if not path.is_file():
            raise ValueError(f"production ProjectReference does not resolve: {path}")
        tree = ET.parse(path)
        assembly = tree.findtext(".//AssemblyName") or path.stem
        inventory.append((path, assembly))

    stems = [stem for _, stem in inventory]
    if len(stems) != len(set(stems)):
        raise ValueError(f"production project outputs are not unique: {stems}")
    return tuple(inventory)


def graded(names: list[str], stems: tuple[str, ...]) -> list[str]:
    return sorted(
        name
        for name in names
        if name.endswith(GRADED_SUFFIXES)
        and pathlib.PurePosixPath(name).name.rsplit(".", 1)[0] in stems
    )


def repository_coord_entries(names: list[str]) -> list[str]:
    """All packed repository-built coord outputs, including one not backed by production inventory."""
    return sorted(
        name for name in names
        if name.endswith(GRADED_SUFFIXES)
        and (
            pathlib.PurePosixPath(name).name.startswith("FS.GG.Coord.")
            or pathlib.PurePosixPath(name).name.startswith("fsgg-coord-engine.")
        )
    )


def clean(root: pathlib.Path, inventory: tuple[tuple[pathlib.Path, str], ...]) -> None:
    """Remove `obj/` and `bin/` for every repository-built coord project before packing.

    This is not tidiness. MSBuild's `CoreCompile` incrementality is keyed on file timestamps, not on
    the property values that produce the compiler command line — so a pack that follows one with a
    DIFFERENT `DeterministicSourcePaths` can reuse the earlier assembly and grade the wrong bytes. The
    gate would then report whichever arm ran first. It is also the shape `.github#2688`'s AC2 states:
    each measurement starts from a clean `obj/` and `bin/`.
    """
    for project, _ in inventory:
        for directory in ("obj", "bin"):
            shutil.rmtree(project.parent / directory, ignore_errors=True)


def pack(root: pathlib.Path, out: pathlib.Path, properties: list[str]) -> pathlib.Path:
    command = [
        "dotnet", "pack", str(root / PROJECT), "-c", "Release",
        "-o", str(out), "--nologo", "-v:q",
    ]
    command += [f"-p:{p}" for p in properties]
    subprocess.run(command, check=True, cwd=root)
    packages = sorted(glob.glob(str(out / "*.nupkg")))
    if len(packages) != 1:
        raise SystemExit(f"expected exactly one .nupkg in {out}, found {len(packages)}")
    return pathlib.Path(packages[0])


def check(package: pathlib.Path, root: pathlib.Path, inventory: tuple[tuple[pathlib.Path, str], ...]) -> int:
    needle = str(root).encode()
    if len(needle) < 8:
        raise SystemExit(f"refusing to grade against an implausibly short build root: {root!r}")

    problems: list[str] = []
    stems = tuple(stem for _, stem in inventory)
    with zipfile.ZipFile(package) as archive:
        entries = graded(archive.namelist(), stems)
        if not entries:
            print(
                f"REFUSED: {package.name} contains none of {', '.join(stems)} — "
                "this gate graded nothing, which is not the same answer as finding nothing",
                file=sys.stderr,
            )
            return 1
        actual = [pathlib.PurePosixPath(name).name for name in entries]
        expected = sorted(stem + suffix for stem in stems for suffix in GRADED_SUFFIXES)
        if actual != expected:
            missing = sorted(set(expected) - set(actual))
            unexpected = sorted(set(actual) - set(expected))
            if missing:
                problems.append(f"missing expected packed coord entries: {', '.join(missing)}")
            if unexpected:
                problems.append(f"unexpected packed coord entries: {', '.join(unexpected)}")

        # Reverse direction: every repository-built coord payload must be backed by the production
        # ProjectReference inventory. Without this comparison, deleting a reference from the producer
        # and from the expected list at once would make both sides forget BoardOps and still pass.
        all_coord = [pathlib.PurePosixPath(name).name for name in repository_coord_entries(archive.namelist())]
        unreferenced = sorted(set(all_coord) - set(expected))
        if unreferenced:
            problems.append(
                "packed coord entries have no production ProjectReference: " + ", ".join(unreferenced)
            )
        for name in entries:
            data = archive.read(name)
            leaks = data.count(needle)
            mapped = data.count(MAPPED_ROOT)
            if leaks:
                problems.append(
                    f"{name}: embeds the build root {root} ({leaks} occurrence(s)) — "
                    "the package is bound to where it was built"
                )
            if not mapped:
                problems.append(
                    f"{name}: carries no {MAPPED_ROOT.decode()} mapped source root — "
                    "DeterministicSourcePaths did not reach this assembly"
                )
            print(f"  {name}: buildRootOccurrences={leaks} mappedRootOccurrences={mapped}")

    print(f"graded {len(entries)} packed entr{'y' if len(entries) == 1 else 'ies'} of {package.name}")
    for problem in problems:
        print(f"FAIL: {problem}", file=sys.stderr)
    return 1 if problems else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repo", default=".", help="repository root (default: the current directory)")
    parser.add_argument("--package", help="grade this .nupkg instead of packing one")
    parser.add_argument(
        "--property", action="append", default=[],
        help="extra MSBuild property for the pack, as Name=Value (the test harness inverts the gate "
             "with DeterministicSourcePaths=false)",
    )
    args = parser.parse_args(argv)

    root = pathlib.Path(args.repo).resolve()
    try:
        inventory = project_inventory(root)
    except (OSError, ET.ParseError, ValueError) as error:
        print(f"REFUSED: cannot derive production project inventory: {error}", file=sys.stderr)
        return 1
    if args.package:
        return check(pathlib.Path(args.package).resolve(), root, inventory)

    with tempfile.TemporaryDirectory(prefix="engine-build-determinism-") as tmp:
        clean(root, inventory)
        package = pack(root, pathlib.Path(tmp), args.property)
        return check(package, root, inventory)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

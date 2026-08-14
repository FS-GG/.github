#!/usr/bin/env python3
"""Require the coherent set's release notes to be publishable: right version, bounded length.

The package listing renders `PackageReleaseNotes`; the release workflow publishes `Version`. The
0.13.0 cut moved only the latter, so consumers saw 0.12.0's breaking-change announcement attached
to 0.13.0. Both values are evaluated MSBuild properties, so this checker asks MSBuild once and
requires the first whitespace-delimited token of the notes to equal the evaluated version.

It deliberately does not judge prose quality. The mechanical invariants catch measured failures
without inventing a style gate.

WHERE THE ANNOUNCED VERSION COMES FROM, AND WHY THIS CHECKER NAMES IT (.github#2512). `<Version>` in
the engine project is not a literal — it is `$(FsggCoherentSetVersion)` (.github#2402), declared once
in `Directory.Build.props`. So the left-hand side of this comparison is authored in a DIFFERENT FILE
from the one being evaluated, and this checker reads both: it requires the evaluated `Version` to
resolve from a non-empty `FsggCoherentSetVersion`, so "which version is being announced" can never be
answered by a stray independent literal that this gate would then happily bless.

WHY THIS GATE ALSO MEASURES LENGTH, AND WHY THAT IS THE SAME GATE (.github#2579). Until 2026-08-14
this file checked the FIRST TOKEN of `PackageReleaseNotes`. nuget.org enforces its LENGTH. Both are
properties of the same field and only one of them 400'd: cutting 0.52.0, the org GitHub Packages feed
accepted all three packages, and nuget.org then refused `FS.GG.Coord.Cli` outright —

    error: A nuget package's ReleaseNotes property may not be more than 35000 characters long.

— leaving `FS.GG.Kit 0.52.0` and `FS.GG.Drivers 0.52.0` published on both feeds and this package
published on the org feed alone. Every local gate the author ran was green, because
`grep -rn "35000" scripts/ .github/workflows/ tests/` returned nothing anywhere in the repository. A
check that measures something ADJACENT to what actually fails is the failure mode this gate now
closes on itself.

THE SUBJECT IS THE WHOLE COHERENT SET, READ AND NEVER RESTATED. Only `FS.GG.Coord.Cli` carries notes
today, but the limit is a property of the REGISTRY, not of this package, and nothing stops a sibling
gaining a `PackageReleaseNotes` tomorrow. The membership is declared once, in
`check-coherent-set-version.py`'s `PROJECTS` tuple — whose own comment says "Adding a fourth member is
a deliberate edit to this tuple" — and is folded out of that file by AST here rather than retyped. A
second copy of the set is how a fourth member escapes this gate. It is read by AST and never
imported: a gate must not execute another gate to learn its own subject.

EVALUATED, NEVER GREPPED — AND HERE THAT IS WORTH 55 CHARACTERS, NOT A STYLE POINT. On the tree this
work repaired, `FS.GG.Coord.Cli.fsproj`'s raw XML inner text was 37,334 characters while the
EVALUATED property was 37,279, because `&lt;`/`&gt;` unescape on evaluation. The nuspec receives the
evaluated value, so the evaluated value is what the registry counts and what this gate scores.

WHY THE STANDING-ADVISORY ARM EXISTS (.github#2579 DEC-002). Bounding the field means an author will
one day trim it against a hard ceiling. `4fccc76d` already did exactly that and deleted every
poisoned-set warning with it — the only channel by which a consumer learns that `FS.GG.Coord.Cli`
0.50.1 and 0.50.5 are permanent two-of-three sets that must not be adopted, on listings that are
immutable and can never be corrected. `5d45ced4` restored them, and its author's own words were "I
mentioned the truncation nowhere, because I had not noticed it". This gate was GREEN across that
deletion. So the advisories now live in their own MSBuild property, `FsggStandingAdvisories`, which
`PackageReleaseNotes` references: trimming the narrative and deleting a warning are edits to
DIFFERENT properties, and the arm below refuses the second. Both halves are checked, for the reason
`check-coherent-set-version.py` states about its own pair — a structural check alone passes text that
does not resolve, and a semantic check alone passes an inlined copy the next trim deletes.

EVERY FAILING ARM SPEAKS ON ONE RUN. The arms do not short-circuit each other: an author with two
problems is told about two problems, and headroom is reported on the red path as well as the green
one. Ordering between arms is therefore not load-bearing, which is deliberate — this gate's own
history is a gate that was silent because something else spoke first.

Exit 0 = publishable. Exit 1 = one or more arms refused. Exit 2 = could not evaluate a subject.
"""

from __future__ import annotations

import argparse
import ast
import json
import os
import re
import subprocess
import sys


DEFAULT_PROJECT = "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"

# The file that declares the scalar `<Version>` resolves from. This is a SUBJECT of the comparison,
# not a neighbour of it: move `FsggCoherentSetVersion` here and you have moved the version this gate
# checks the notes against, without touching DEFAULT_PROJECT at all.
VERSION_SOURCE = "Directory.Build.props"
COHERENT_SET_PROPERTY = "FsggCoherentSetVersion"

# nuget.org's OWN limit, named as the external constraint it is. This org does not choose it and
# cannot relax it; the number is quoted from the 400 that refused FS.GG.Coord.Cli 0.52.0:
#
#     A nuget package's ReleaseNotes property may not be more than 35000 characters long.
#
# It is expressed HERE and nowhere else — no workflow, fixture or document restates it, so there is
# no second copy to drift when nuget.org moves it (.github#2579 DEC-004). Deliberately NOT paired
# with a lower "warning" threshold: a limit this org invented would refuse trees the registry would
# have accepted, and would then have to be maintained against a limit it does not control. The
# signal an author actually lacked was the NUMBER — 0.52.0's author had 1,241 characters of headroom
# and wrote 3,575 — so every run reports headroom instead.
NUGET_ORG_RELEASE_NOTES_LIMIT = 35000

# The coherent set's membership lives in ONE place (.github#2402). This gate folds it out of that
# file rather than keeping a second copy; see the module docstring.
COHERENT_SET_SOURCE = "scripts/check-coherent-set-version.py"
COHERENT_SET_NAME = "PROJECTS"

# The property carrying the standing DO-NOT-ADOPT advisories, separately from the release narrative.
ADVISORY_PROPERTY = "FsggStandingAdvisories"
ADVISORY_REFERENCE = f"$({ADVISORY_PROPERTY})"

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
#
# THE SIBLING PROJECTS AND COHERENT_SET_SOURCE ARE HERE FOR THE SAME REASON, ONE LINK FURTHER OUT
# (.github#2579). The length arm's subject is whatever `PROJECTS` says it is, so an edit to a sibling
# `.csproj` — the only way a sibling can gain release notes at all — must start this workflow, and so
# must an edit to the file that decides the membership. `tests/engine-release-notes/run.sh` asserts
# the whole chain mechanically: every `PROJECTS` entry is in PATHS_SUBJECT, and every PATHS_SUBJECT
# entry is in BOTH of the workflow's `paths:` filters. Add a fourth member to `PROJECTS` and that
# fixture goes red until this tuple and the workflow follow.
SIBLING_PROJECTS = (
    "src/FS.GG.Kit/FS.GG.Kit.csproj",
    "src/FS.GG.Drivers/FS.GG.Drivers.csproj",
)
PATHS_SUBJECT = (DEFAULT_PROJECT, VERSION_SOURCE, COHERENT_SET_SOURCE) + SIBLING_PROJECTS


class GateError(Exception):
    """A subject this gate must measure could not be read or evaluated. Never a verdict."""


def msbuild_properties(project: str, names: list[str]) -> dict[str, str]:
    """The EVALUATED MSBuild properties. Never a grep — see the module docstring.

    AT LEAST TWO NAMES, ALWAYS, AND THAT IS AN MSBUILD CONTRACT RATHER THAN A STYLE RULE:
    `dotnet msbuild -getProperty:A -getProperty:B` emits a JSON `{"Properties": {...}}` document,
    but `-getProperty:A` ALONE emits the bare value with no JSON around it. Asking for one property
    and parsing JSON therefore fails on well-formed output — measured, and it is why the sibling
    projects below are read with a second property they do not need.
    """
    if len(names) < 2:
        raise GateError(
            f"internal: msbuild_properties needs at least two property names (asked for {names!r}); "
            f"a single -getProperty emits a bare value rather than a JSON document."
        )
    try:
        run = subprocess.run(
            ["dotnet", "msbuild", project] + [f"-getProperty:{name}" for name in names],
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as exc:
        raise GateError(f"cannot run dotnet msbuild: {exc}") from exc

    if run.returncode != 0:
        detail = run.stderr.strip() or run.stdout.strip() or "(no diagnostic)"
        raise GateError(f"dotnet msbuild could not evaluate {project!r}: {detail}")
    try:
        properties = json.loads(run.stdout)["Properties"]
        values = {name: properties[name] for name in names}
    except (ValueError, KeyError, TypeError) as exc:
        raise GateError(
            f"dotnet msbuild returned no readable {'/'.join(names)} property document for {project!r}"
        ) from exc
    if not all(isinstance(value, str) for value in values.values()):
        raise GateError(f"dotnet msbuild returned a non-text property value for {project!r}")
    return values


def coherent_set_projects(source_path: str) -> tuple[str, ...]:
    """`PROJECTS` as check-coherent-set-version.py declares it, folded by AST and never imported.

    Folds the same shapes check-paths-coherence rule (c) folds: literals, module-level names, and
    `+`. A source that cannot be read, cannot be parsed, or declares no foldable `PROJECTS` is a
    GateError — "I cannot tell what my subject is" must never share an exit code with "checked, and
    it's fine" (epic #266).
    """
    try:
        text = open(source_path, encoding="utf-8").read()
    except OSError as exc:
        raise GateError(f"cannot read the coherent-set declaration {source_path!r}: {exc}") from exc
    try:
        module = ast.parse(text)
    except SyntaxError as exc:
        raise GateError(f"cannot parse the coherent-set declaration {source_path!r}: {exc}") from exc

    constants: dict[str, object] = {}

    def fold(node: ast.AST) -> list[str]:
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            return fold(node.left) + fold(node.right)
        if isinstance(node, (ast.Tuple, ast.List)):
            return [entry for elt in node.elts for entry in fold(elt)]
        if isinstance(node, ast.Name):
            value = constants[node.id]
            return list(value) if isinstance(value, (list, tuple)) else [value]
        value = ast.literal_eval(node)
        return list(value) if isinstance(value, (list, tuple)) else [value]

    found: tuple[str, ...] | None = None
    for node in module.body:
        if not isinstance(node, ast.Assign) or len(node.targets) != 1:
            continue
        target = node.targets[0]
        if not isinstance(target, ast.Name):
            continue
        if target.id == COHERENT_SET_NAME:
            try:
                found = tuple(fold(node.value))
            except (KeyError, ValueError, TypeError) as exc:
                raise GateError(
                    f"{source_path} declares {COHERENT_SET_NAME}, but this gate cannot fold it into "
                    f"a list of project paths: {exc}"
                ) from exc
        else:
            try:
                constants[target.id] = ast.literal_eval(node.value)
            except (ValueError, TypeError):
                pass

    if not found:
        raise GateError(
            f"{source_path} declares no non-empty {COHERENT_SET_NAME}; this gate reads the coherent "
            f"set from there rather than keeping a second copy, so it cannot tell which packages to "
            f"measure."
        )
    if not all(isinstance(entry, str) and entry for entry in found):
        raise GateError(f"{source_path}'s {COHERENT_SET_NAME} contains a non-text project path.")
    return found


_RELEASE_NOTES_ELEMENT = re.compile(
    r"<PackageReleaseNotes>(.*?)</PackageReleaseNotes>", re.DOTALL
)


def authored_release_notes_element(project_path: str) -> str:
    """The project's `<PackageReleaseNotes>` element text, exactly as authored (never evaluated).

    The STRUCTURAL half of the advisory arm. Evaluating cannot see this: `$(FsggStandingAdvisories)`
    and a hand-inlined copy of the advisories evaluate to the same string, and only one of them
    survives the next trim.
    """
    try:
        text = open(project_path, encoding="utf-8").read()
    except OSError as exc:
        raise GateError(f"cannot read {project_path!r}: {exc}") from exc
    found = _RELEASE_NOTES_ELEMENT.findall(text)
    if len(found) != 1:
        raise GateError(
            f"{project_path} declares {len(found)} <PackageReleaseNotes> element(s); this gate needs "
            f"exactly one to check how it is composed."
        )
    return found[0]


def headroom_line(project: str, notes: str) -> str:
    used = len(notes)
    remaining = NUGET_ORG_RELEASE_NOTES_LIMIT - used
    percent = 100.0 * used / NUGET_ORG_RELEASE_NOTES_LIMIT
    if used == 0:
        return (
            f"  {project}: declares no PackageReleaseNotes — 0 of "
            f"{NUGET_ORG_RELEASE_NOTES_LIMIT} characters used, {remaining} remaining."
        )
    standing = f"OVER BUDGET by {-remaining}" if remaining < 0 else f"{remaining} remaining"
    return (
        f"  {project}: {used} of {NUGET_ORG_RELEASE_NOTES_LIMIT} characters used "
        f"({percent:.1f}% of nuget.org's budget), {standing}."
    )


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project",
        default=DEFAULT_PROJECT,
        help="the ANNOUNCING project: the one whose notes must name the version (default: "
        f"{DEFAULT_PROJECT})",
    )
    parser.add_argument(
        "--coherent-set-source",
        default=COHERENT_SET_SOURCE,
        help="the file declaring the coherent set the LENGTH arm measures (default: "
        f"{COHERENT_SET_SOURCE}). The set is folded out of it by AST, never restated here.",
    )
    args = parser.parse_args(argv)

    problems: list[str] = []

    # ── Subjects. A subject that cannot be read or evaluated is exit 2, never a verdict. ──────────
    try:
        announced = msbuild_properties(
            args.project,
            ["Version", COHERENT_SET_PROPERTY, "PackageReleaseNotes", ADVISORY_PROPERTY],
        )
        members = coherent_set_projects(args.coherent_set_source)
        length_subjects = list(members)
        if args.project not in length_subjects:
            length_subjects.append(args.project)
        notes_by_project = {args.project: announced["PackageReleaseNotes"]}
        for project in length_subjects:
            if project not in notes_by_project:
                notes_by_project[project] = msbuild_properties(
                    project, ["PackageReleaseNotes", "Version"]
                )["PackageReleaseNotes"]
    except GateError as exc:
        print(f"::error::check-engine-release-notes: {exc}", file=sys.stderr)
        return 2

    version = announced["Version"].strip()
    coherent = announced[COHERENT_SET_PROPERTY].strip()
    notes = announced["PackageReleaseNotes"].strip()
    advisories = announced[ADVISORY_PROPERTY].strip()

    # ── The .github#1762 / .github#2512 announce arms, unchanged in behaviour. ────────────────────
    if not version:
        problems.append(f"evaluated Version is empty in {args.project}.")
    elif not coherent:
        problems.append(
            f"evaluated Version is {version}, but {COHERENT_SET_PROPERTY} is empty or undeclared. "
            f"{args.project}'s <Version> is meant to resolve from that scalar ({VERSION_SOURCE}), so "
            f"this gate cannot tell which version the notes are being checked against."
        )
    elif coherent != version:
        problems.append(
            f"evaluated Version is {version}, but {COHERENT_SET_PROPERTY} is {coherent}. "
            f"{args.project} is announcing a version that did not come from the coherent-set scalar "
            f"in {VERSION_SOURCE}."
        )

    if not notes:
        problems.append(
            f"PackageReleaseNotes is empty for Version {version} in {args.project}. Consumers would "
            f"receive a release with no announcement."
        )
    elif version:
        first = notes.split(maxsplit=1)[0]
        if first != version:
            problems.append(
                f"Version is {version}, but PackageReleaseNotes begins with {first!r}. The package "
                f"listing would announce another release's notes. Begin PackageReleaseNotes with "
                f"{version}."
            )

    # ── The .github#2579 standing-advisory arm: structural, then semantic. ────────────────────────
    try:
        authored = authored_release_notes_element(args.project)
    except GateError as exc:
        print(f"::error::check-engine-release-notes: {exc}", file=sys.stderr)
        return 2

    # Skipped when the notes are empty: that tree is already refused above, and an empty field
    # cannot meaningfully be said to have "dropped" a reference. Every other shape is checked.
    if not notes:
        pass
    elif ADVISORY_REFERENCE not in authored:
        problems.append(
            f"{args.project}'s <PackageReleaseNotes> does not reference {ADVISORY_REFERENCE}. The "
            f"standing DO-NOT-ADOPT advisories live in that property so that trimming the release "
            f"narrative and deleting a permanent two-of-three warning are different edits "
            f"(.github#2579). Those listings are immutable and this is the only channel that can "
            f"correct them — 4fccc76d deleted them while this gate was green. Restore the reference."
        )
    if not notes:
        pass
    elif not advisories:
        problems.append(
            f"{ADVISORY_PROPERTY} is empty or undeclared for {args.project}. An empty advisory "
            f"property publishes a listing that silently stops warning about the permanent "
            f"two-of-three sets; if an advisory is genuinely retired, retire it deliberately rather "
            f"than by emptying the property."
        )
    elif advisories not in announced["PackageReleaseNotes"]:
        problems.append(
            f"{ADVISORY_PROPERTY} is declared for {args.project} but its evaluated text does not "
            f"appear in the evaluated PackageReleaseNotes. The reference does not resolve into the "
            f"published field, so the advisories would not reach a consumer."
        )

    # ── The .github#2579 length arm, over every member of the coherent set. ───────────────────────
    for project in length_subjects:
        used = len(notes_by_project[project])
        if used > NUGET_ORG_RELEASE_NOTES_LIMIT:
            problems.append(
                f"{project}'s evaluated PackageReleaseNotes is {used} characters, which exceeds "
                f"nuget.org's limit of {NUGET_ORG_RELEASE_NOTES_LIMIT} by "
                f"{used - NUGET_ORG_RELEASE_NOTES_LIMIT}. nuget.org refuses the push outright "
                f"(\"A nuget package's ReleaseNotes property may not be more than "
                f"{NUGET_ORG_RELEASE_NOTES_LIMIT} characters long.\"), and the org GitHub Packages "
                f"feed does not, so a coherent-set cut from this tree publishes some packages and "
                f"then fails on this one — the .github#2579 shape."
            )

    # ── Headroom is reported on BOTH paths. Pass/fail alone told 0.52.0's author nothing. ─────────
    report = ["release-notes budget (nuget.org's limit, per coherent-set package):"]
    report += [headroom_line(project, notes_by_project[project]) for project in length_subjects]

    if problems:
        for problem in problems:
            print(f"::error::check-engine-release-notes: {problem}", file=sys.stderr)
        print("\n".join(report), file=sys.stderr)
        return 1

    print(
        f"ok: {os.path.basename(args.project)} Version {version} agrees with the first "
        f"PackageReleaseNotes token, resolves from {COHERENT_SET_PROPERTY} in {VERSION_SOURCE}, and "
        f"carries {ADVISORY_REFERENCE} ({len(advisories)} characters of standing advisories)."
    )
    print("\n".join(report))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

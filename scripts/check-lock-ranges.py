#!/usr/bin/env python3
"""Assert every project-reference range in the committed locks tracks the version its project declares
(.github#495, epic #266). Static: git-tracked files only — no restore, no SDK, no network, no feed.

THE BLIND SPOT THIS CLOSES. `dotnet restore --locked-mode` validates package entries — id, version,
`contentHash`. It never looks at the dependency ranges under a `"type": "Project"` entry:

    "fs.gg.audio.engine": {
      "type": "Project",
      "dependencies": { "FS.GG.Audio.Core": "[0.1.0, )" }   <-- the project already declares 0.2.0
    }

So a release bump that edits a version WITHOUT regenerating the locks leaves every sibling project
reference recording the OLD version, and a cold locked restore still passes. FS.GG.Audio's `main` was
green for the whole v0.2.0 cycle with `0.1.0` in six lock files (Audio 2c208f3), until an unrelated
regeneration washed it out by accident.

It is functionally harmless — project references resolve from the project graph, not the lock — and
THAT is why it survives. The cost is a POISONED LOCK DIFF: the next reviewer who has to scrutinise a
`contentHash` (the value proving the package restored is the package the lock recorded) reads it mixed
with release-bump residue. Assert it, don't remember it; a rule kept by a checklist is re-broken at
the next bump.

THE VERSION-OF-TRUTH IS THE PROJECT'S OWN, AND IT HAS TO BE. Audio's original guard (Audio#52) read one
central MSBuild property, `<FsGgAudioVersion>`. That is an Audio-ism, and lifting it verbatim would
produce a gate only Audio can call. The three receivers declare a project's version three different ways:

    FS.GG.Audio      <PackageId>FS.GG.Audio.Core</PackageId>  <Version>$(FsGgAudioVersion)</Version>
    FS.GG.Rendering  <PackageId>FS.GG.UI.Scene</PackageId>    <Version>0.4.0-preview.1</Version>   (per project)
    FS.GG.Game       <PackageId>FS.GG.Game.Core</PackageId>   (absent — inherited from Directory.Build.local.props)

The one rule true of all three — and of NuGet, which is what actually writes the range — is that the
range recorded for a project reference is the REFERENCED PROJECT'S OWN effective `Version`. So this gate
resolves that per project (own PropertyGroup, else the inherited Directory.*.props chain, with `$(Prop)`
substituted) and needs no per-repo configuration at all.

Keying on "packages THIS repo's projects produce" also draws the line the check actually needs: a range
naming a package the repo does not build is a CONSUMPTION pin (Rendering pins the FS.GG.Audio.Core
package), a different question with a different answer, and it is correctly ignored here.

COMPARES AGAINST THE VERSION NUGET WRITES, not the one you typed. SemVer2 build metadata is not part of
NuGet version identity, so it is dropped from a range: a declared `0.2.0+ci` lands in the lock as
`[0.2.0, )`. Comparing raw strings would red the gate on a correct bump, and a guard that cries wolf
gets deleted.

FAILS CLOSED, which is the whole point of epic #266. "Nothing to check" and "checked, and it is fine"
must not share an exit code. Every one of these is an ERROR, not a skip:

  * no committed packages.lock.json (this org pins its restores — ADR-0006);
  * no project files, or not one of them declares a package id;
  * ZERO `"type": "Project"` entries across every lock — the lock schema or this query has drifted,
    and a check that cannot see its own subject must never report green;
  * fewer than --min-ranges project-reference ranges inspected (default 1);
  * a project that IS referenced by another declares a version this gate cannot resolve (an unexpanded
    `$(Prop)`, or nothing at all) — it must not guess the truth it exists to enforce;
  * a range whose shape is not `[<version>, )` — an unrecognised shape is not a passing one.

A repo whose projects genuinely do not reference each other has zero ranges to check (FS.GG.Game today).
That is legitimate, but it must be a DECISION rather than an omission: pass `--min-ranges 0` and say so
at the call site, so the day Game grows a second project the default catches it.

Usage:  scripts/check-lock-ranges.py [--root .] [--min-ranges N]
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# NuGet writes a project reference as an inclusive-minimum range and nothing else. Anything that does
# not match is a shape we have not seen, and we refuse to interpret it rather than pass it.
RANGE_RE = re.compile(r"^\[\s*([^,\[\]]+?)\s*,\s*\)$")
PROP_REF_RE = re.compile(r"\$\(([A-Za-z_][A-Za-z0-9_]*)\)")

# The props files MSBuild imports for a directory, in the order their values take effect. `.local` is
# imported LAST by the org-shared canonical file (see any receiver's Directory.Build.props), so a repo's
# own value wins over the org baseline — mirror that here or Rendering's <Version> reads as the org's.
PROPS_FILES = (
    "Directory.Packages.props",
    "Directory.Packages.local.props",
    "Directory.Build.props",
    "Directory.Build.local.props",
)


class Problem(Exception):
    """A condition under which the gate must go red rather than guess."""


def git_ls(root: Path, *patterns: str) -> list[Path]:
    try:
        out = subprocess.run(
            ["git", "-C", str(root), "ls-files", "-z", *patterns],
            check=True, capture_output=True, text=True,
        ).stdout
    except (subprocess.CalledProcessError, FileNotFoundError) as e:
        raise Problem(f"cannot enumerate committed files under {root} with git: {e}") from e
    return [root / p for p in out.split("\0") if p]


def strip_ns(tag: str) -> str:
    return tag.rsplit("}", 1)[-1] if "}" in tag else tag


_PROPS_CACHE: dict[Path, dict[str, str]] = {}


def read_properties(path: Path) -> dict[str, str]:
    """Scalar <PropertyGroup> properties from an MSBuild file.

    A CONDITIONED value is skipped — whether the condition sits on the property or on the PropertyGroup
    around it. We cannot evaluate an MSBuild condition without MSBuild, and a value we cannot prove
    applies is a value we must not treat as the truth. Skipping the property but honouring the group
    would be worse than not checking at all: `<PropertyGroup Condition="'$(Configuration)'=='Debug'">
    <Version>9.9.9</Version></PropertyGroup>` is idiomatic, and reading it as the version-of-truth reds
    a repo whose locks are perfectly correct. A guard that cries wolf gets deleted."""
    if path in _PROPS_CACHE:
        return _PROPS_CACHE[path]
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as e:
        raise Problem(f"{path}: cannot parse as MSBuild XML: {e}") from e
    props: dict[str, str] = {}
    for group in root.iter():
        if strip_ns(group.tag) != "PropertyGroup" or "Condition" in group.attrib:
            continue
        for prop in group:
            if prop.tag is ET.Comment or "Condition" in prop.attrib:
                continue
            props[strip_ns(prop.tag)] = (prop.text or "").strip()
    _PROPS_CACHE[path] = props
    return props


def inherited_properties(root: Path, project: Path) -> dict[str, str]:
    """The props a project inherits.

    NEAREST WINS, AND ONLY THE NEAREST — per file kind, walking up from the project to the root. That is
    MSBuild's own rule: it imports the FIRST Directory.Build.props it finds walking up and stops; it does
    not implicitly chain to the one above (a nested file that wants the parent must import it explicitly,
    via GetPathOfFileAbove). Unioning the whole ancestor chain instead would hand a project under
    `samples/` (which has its own Directory.Build.props — FS.GG.Rendering does) a version it does not
    actually inherit. Where the real chain is explicit and we therefore miss it, the version simply fails
    to resolve, and the gate says so rather than guessing."""
    props: dict[str, str] = {}
    for name in PROPS_FILES:
        directory = project.parent
        while True:
            f = directory / name
            if f.is_file():
                props.update(read_properties(f))
                break
            if directory == root or root not in directory.parents:
                break
            directory = directory.parent
    return props


def expand(value: str, props: dict[str, str]) -> str:
    """Substitute $(Prop) from the property table. One pass of nesting is plenty for a version, and an
    unresolved reference is left intact on purpose — the caller reports it rather than guessing."""
    for _ in range(3):
        if not PROP_REF_RE.search(value):
            break
        value = PROP_REF_RE.sub(lambda m: props.get(m.group(1), m.group(0)), value)
    return value.strip()


def normalize(version: str) -> str:
    """The version as NuGet records it. Build metadata is not part of NuGet version identity, so it is
    dropped from a range: `0.2.0+ci` is written `[0.2.0, )`."""
    version = version.strip().split("+", 1)[0]
    core, sep, pre = version.partition("-")
    parts = [p.lstrip("0") or "0" for p in core.split(".")]
    while len(parts) < 3:
        parts.append("0")
    if len(parts) == 4 and parts[3] == "0":
        parts = parts[:3]
    return ".".join(parts) + (sep + pre if sep else "")


class Project:
    __slots__ = ("path", "package_id", "version", "raw_version")

    def __init__(self, path: Path, package_id: str, version: str, raw_version: str):
        self.path = path
        self.package_id = package_id
        self.version = version
        self.raw_version = raw_version


def load_projects(root: Path) -> dict[str, list[Project]]:
    """Every package this repo's own projects PRODUCE, keyed by package id (lowercased — the lock keys its
    project entries that way). A project's own PropertyGroup wins over what it inherits.

    The value is a LIST, not a Project, because two project files CAN claim one package id, and then this
    repo has no single version-of-truth for it. Last-writer-wins would silently pick one — reddening a
    correct repo, or worse, greening a stale range that happens to match the loser. The ambiguity is
    reported at the point it would decide a verdict (see check), so an id nothing references cannot red a
    healthy repo over a collision that changes no answer."""
    projects: dict[str, list[Project]] = {}
    for path in git_ls(root, "*.fsproj", "*.csproj", "*.vbproj"):
        if not path.is_file():
            continue
        own = read_properties(path)
        props = inherited_properties(root, path)
        props.update(own)
        # MSBuild's own defaulting chain: PackageId falls back to AssemblyName, which falls back to the
        # project file's name. Game's FS.GG.Game.Core.fsproj is explicit; Rendering's Scene.fsproj is
        # PackageId FS.GG.UI.Scene over a file stem of "Scene" — so the stem alone would miss it.
        package_id = expand(own.get("PackageId") or own.get("AssemblyName") or path.stem, props)
        raw = own.get("Version") or props.get("Version") or ""
        projects.setdefault(package_id.lower(), []).append(
            Project(path, package_id, expand(raw, props), raw)
        )
    return projects


def load_locks(root: Path) -> list[tuple[Path, dict]]:
    locks: list[tuple[Path, dict]] = []
    for path in git_ls(root, "*packages.lock.json"):
        if not path.is_file():
            continue
        try:
            locks.append((path, json.loads(path.read_text(encoding="utf-8"))))
        except (OSError, json.JSONDecodeError) as e:
            raise Problem(f"{path}: cannot read as JSON: {e}") from e
    return locks


def check(root: Path, min_ranges: int) -> int:
    projects = load_projects(root)
    if not projects:
        raise Problem(
            "no project files (*.fsproj/*.csproj/*.vbproj) are committed under "
            f"{root} — there is no version-of-truth to check the locks against."
        )

    locks = load_locks(root)
    if not locks:
        raise Problem(
            "no committed packages.lock.json found — this org pins its restores (ADR-0006), "
            "and a gate that checks zero lock files is not a gate."
        )

    project_entries = 0
    subjects: list[tuple[Path, str, str, str, str]] = []  # file, tfm, project, dependency, range
    for path, doc in locks:
        if not isinstance(doc, dict):
            raise Problem(f"{path}: the lock's top level is not an object — the schema has drifted.")
        deps_by_tfm = doc.get("dependencies") or {}
        if not isinstance(deps_by_tfm, dict):
            raise Problem(f"{path}: 'dependencies' is not an object — the lock schema has drifted.")
        for tfm, entries in deps_by_tfm.items():
            if not isinstance(entries, dict):
                raise Problem(
                    f"{path}: the '{tfm}' entry is not an object — the lock schema has drifted."
                )
            for name, entry in entries.items():
                if not isinstance(entry, dict) or entry.get("type") != "Project":
                    continue
                project_entries += 1
                for dep, rng in (entry.get("dependencies") or {}).items():
                    if dep.lower() in projects:
                        subjects.append((path, tfm, name, dep, rng))

    # The lock records project references at all — if it does not, the schema or this query has moved,
    # and every range below would be vacuously fine. This can never be legitimately zero for a repo with
    # project references, so it is the assertion that catches drift (epic #416, the silent no-op family).
    if project_entries == 0:
        raise Problem(
            f"inspected {len(locks)} lock file(s) and found ZERO '\"type\": \"Project\"' entries. "
            "A repo that pins its restores records its project references; the lock format or this "
            "query has drifted. Refusing to report green off a check with no subject."
        )

    if len(subjects) < min_ranges:
        raise Problem(
            f"inspected {len(subjects)} project-reference range(s) across {len(locks)} lock file(s), "
            f"fewer than the {min_ranges} this repo declares it has. Either the projects stopped "
            "referencing each other (lower --min-ranges deliberately) or this query has drifted."
        )

    problems: list[str] = []
    stale: list[tuple[Path, str, str, str, str, str]] = []
    for path, tfm, holder, dep, rng in subjects:
        candidates = projects[dep.lower()]
        if len(candidates) > 1:
            where = ", ".join(str(c.path.relative_to(root)) for c in candidates)
            problems.append(
                f"'{dep}' is referenced by '{holder}', but {len(candidates)} project files claim that "
                f"package id ({where}). There is no single version-of-truth for it, and this gate will "
                "not pick one."
            )
            continue
        produced = candidates[0]
        if not produced.version or PROP_REF_RE.search(produced.version):
            unresolved = produced.raw_version or "(no <Version> anywhere in its import chain)"
            problems.append(
                f"{produced.path}: project '{produced.package_id}' is referenced by '{holder}', but "
                f"its version does not resolve statically: '{unresolved}'. This gate will not guess "
                "the version it exists to enforce."
            )
            continue
        m = RANGE_RE.match(str(rng))
        if not m:
            problems.append(
                f"{path} [{tfm}] {holder} -> {dep}: unrecognised range '{rng}'. NuGet writes a project "
                "reference as '[<version>, )'; an unrecognised shape is not a passing one."
            )
            continue
        want = normalize(produced.version)
        got = normalize(m.group(1))
        if want != got:
            stale.append((path, tfm, holder, dep, str(rng), f"[{want}, )"))

    for path, tfm, holder, dep, got, want in stale:
        rel = path.relative_to(root)
        print(f"  stale  {rel} [{tfm}]  {holder} -> {dep} : {got}  (project declares {want})")

    if stale:
        print()
        print(
            f"::error::check-lock-ranges: {len(stale)} of {len(subjects)} project-reference range(s) do "
            "not match the version the referenced project declares. A version bump must regenerate the "
            "lock files, so the bump commit carries its own lock churn instead of poisoning a later "
            "reviewer's diff. Fix: dotnet restore --force-evaluate (cold, per ADR-0032) and commit the "
            "updated lock files.",
            file=sys.stderr,
        )
    for p in problems:
        print(f"::error::check-lock-ranges: {p}", file=sys.stderr)

    if stale or problems:
        return 1

    print(
        f"ok: all {len(subjects)} project-reference range(s) across {len(locks)} lock file(s) track the "
        "version their project declares."
    )
    return 0


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter
    )
    ap.add_argument("--root", default=".", help="Repository root to check (default: the cwd).")
    ap.add_argument(
        "--min-ranges",
        type=int,
        default=1,
        help="Fail if fewer than N project-reference ranges are inspected (default: 1). Pass 0 only for "
        "a repo whose projects genuinely do not reference each other, so zero is a declared decision.",
    )
    args = ap.parse_args(argv)

    if args.min_ranges < 0:
        print("::error::check-lock-ranges: --min-ranges cannot be negative.", file=sys.stderr)
        return 2

    root = Path(args.root).resolve()
    if not root.is_dir():
        print(f"::error::check-lock-ranges: --root is not a directory: {root}", file=sys.stderr)
        return 2

    try:
        return check(root, args.min_ranges)
    except Problem as e:
        print(f"::error::check-lock-ranges: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

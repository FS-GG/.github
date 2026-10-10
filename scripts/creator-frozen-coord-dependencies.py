#!/usr/bin/env python3
"""Reuse the selected published implementation inputs without rebuilding them."""
from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import stat
import subprocess
import re
import zipfile
from xml.etree import ElementTree

ROOT = pathlib.Path(__file__).resolve().parents[1]
PIN = ROOT / "scripts/creator-frozen-coord-dependencies.json"
PREFIX = "tools/net10.0/any/"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(body):
    return hashlib.sha256(body).hexdigest()


def regular(path):
    require(not path.is_symlink() and path.is_file(), "missing or linked dependency input: " + str(path))
    require(all(not parent.is_symlink() for parent in path.parents), "linked dependency parent")
    return path.read_bytes()


NORMAL_REFERENCE = "../../src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
FROZEN_REFERENCE = "$(FsggFrozenCoordSource)/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
NORMAL_CONDITION = "'$(FsggFrozenCoordDependencies)' == '' And '$(FsggFrozenCoordSource)' == ''"
FROZEN_CONDITION = "'$(FsggFrozenCoordDependencies)' != '' And '$(FsggFrozenCoordSource)' != ''"
PAIR_ERROR = "('$(FsggFrozenCoordDependencies)' == '' And '$(FsggFrozenCoordSource)' != '') Or ('$(FsggFrozenCoordDependencies)' != '' And '$(FsggFrozenCoordSource)' == '')"


def git_identity(root):
    require(root.is_absolute() and root.is_dir()
            and all(not p.is_symlink() for p in (root, *root.parents)), "missing or linked source root")
    actual = subprocess.check_output(["git", "-C", str(root), "rev-parse", "--show-toplevel"],
                                     timeout=10, text=True).strip()
    require(pathlib.Path(actual) == root, "source root must be the exact Git worktree root")
    head = subprocess.check_output(["git", "-C", str(root), "rev-parse", "HEAD"],
                                   timeout=10, text=True).strip()
    require(re.fullmatch(r"[0-9a-f]{40}", head) is not None, "source root Git identity missing")
    return head


def source_projects(root, pin, producer_root):
    require(producer_root is not None and producer_root != root, "distinct frozen producer source root required")
    require(git_identity(producer_root) == pin["sourceSha"], "published dependency source revision changed")
    creator = root / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
    frozen_copy_contract(ElementTree.fromstring(regular(creator)))
    projects = {"scripts/NewSddWorkspace/NewSddWorkspace.fsproj": "new-sdd-workspace"}
    def visit(path):
        require(not path.is_symlink() and all(not parent.is_symlink() for parent in path.parents),
                "linked project reference")
        path = path.resolve()
        require(path.is_relative_to(producer_root), "foreign frozen project reference")
        relative = path.relative_to(producer_root).as_posix()
        if relative in projects:
            return
        xml = ElementTree.fromstring(regular(path))
        projects[relative] = next((e.text for e in xml.iter("AssemblyName") if e.text), path.stem)
        for reference in xml.iter("ProjectReference"):
            declaration = reference.attrib["Include"]
            require(not any(c in declaration for c in ("$", "*", "?", ";"))
                    and not reference.get("Condition"), "ambiguous frozen project reference")
            visit(path.parent / declaration)
    visit(producer_root / "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj")
    require(projects == pin["projects"], "frozen dependency project graph changed")
    declared = [row["path"] for row in pin["sourceLeaves"]]
    require(len(declared) == len(set(declared))
            and set(declared) == dependency_source_paths(producer_root, projects),
            "frozen dependency source coverage differs")
    for row in pin["sourceLeaves"]:
        require(digest(regular(producer_root / row["path"])) == row["sha256"],
                "published dependency source changed: " + row["path"])
    return projects


def dependency_source_paths(root, projects):
    """Census actual declared inputs, including linked and nested compiler files.

    Creator is built separately; its frozen-copy contract and project graph are
    checked above. The dependency projects use literal includes and repository
    imports. Unsupported expressions refuse rather than guess an MSBuild result.
    """
    inputs = set()

    def local(parent, declaration):
        require(declaration and not any(c in declaration for c in ("$", "*", "?", ";")),
                "unsupported frozen source input: " + str(declaration))
        path = parent / declaration
        require(not path.is_symlink() and all(not p.is_symlink() for p in path.parents),
                "linked frozen source input")
        path = path.resolve()
        require(path.is_relative_to(root), "foreign frozen source input")
        regular(path)
        return path

    def add(path):
        inputs.add(path.relative_to(root).as_posix())

    def configuration(path):
        relative = path.relative_to(root).as_posix()
        if relative in inputs:
            return
        add(path)
        xml = ElementTree.fromstring(regular(path))
        for item in xml.iter("Import"):
            declaration = item.get("Project", "").replace("$(MSBuildThisFileDirectory)", str(path.parent) + "/")
            candidate = path.parent / declaration
            if not candidate.exists() and item.get("Condition") == "Exists('$(MSBuildThisFileDirectory)" + item.get("Project", "") + "')":
                continue
            configuration(local(path.parent, declaration))

    for name in projects:
        path = root / name
        if name == "scripts/NewSddWorkspace/NewSddWorkspace.fsproj":
            continue
        add(path)
        xml = ElementTree.fromstring(regular(path))
        for item in xml.iter():
            if item.tag in ("Compile", "EmbeddedResource", "Content", "None") and "Include" in item.attrib:
                add(local(path.parent, item.attrib["Include"]))
            if item.tag == "Import":
                configuration(local(path.parent, item.attrib["Project"]))
        add(local(path.parent, "packages.lock.json"))
        for parent in (path.parent, *path.parent.parents):
            if not parent.is_relative_to(root):
                break
            for filename in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"):
                candidate = parent / filename
                if candidate.exists():
                    configuration(candidate)
    add(local(root, "global.json"))
    return inputs


def frozen_copy_contract(xml):
    """Refuse source drift that asks unbuilt project references for content."""
    condition = "'$(FsggFrozenCoordDependencies)' != ''"
    groups = [group for group in xml.findall("PropertyGroup") if group.get("Condition") == condition]
    require(len(groups) == 1, "frozen dependency property scope changed")
    for name in ("BuildProjectReferences", "CompileUsingReferenceAssemblies",
                 "_GetChildProjectCopyToOutputDirectoryItems", "_GetChildProjectCopyToPublishDirectoryItems"):
        values = groups[0].findall(name)
        require(len(values) == 1 and values[0].text == "false" and not values[0].attrib,
                "frozen dependency copy guard changed: " + name)
        require(len(list(xml.iter(name))) == 1, "duplicate frozen dependency copy guard: " + name)
    content = [item for group in xml.findall("ItemGroup") if group.get("Condition") == condition
               for item in group.findall("None") if item.get("Include") == "$(FsggFrozenCoordDependencies)/**/*"]
    require(len(content) == 1 and content[0].get("CopyToOutputDirectory") == "Always"
            and content[0].get("CopyToPublishDirectory") == "Always"
            and content[0].get("Exclude") == ";".join("$(FsggFrozenCoordDependencies)/fsgg-coord-engine" + suffix
                                                     for suffix in (".dll", ".pdb", ".xml"))
            and content[0].get("Link") == "%(RecursiveDir)%(Filename)%(Extension)",
            "frozen dependency content copy changed")
    references = list(xml.iter("ProjectReference"))
    groups = [g for g in xml.findall("ItemGroup") if g.findall("ProjectReference")]
    require(groups and all(g.get("Condition") is None for g in groups)
            and sum(len(g.findall("ProjectReference")) for g in groups) == len(references),
            "frozen dependency reference parent scope changed")
    require(len(references) == 2 and {(r.get("Include"), r.get("Condition")) for r in references}
            == {(NORMAL_REFERENCE, NORMAL_CONDITION), (FROZEN_REFERENCE, FROZEN_CONDITION)}
            and all(not r.findall("Private") and "Private" not in r.attrib for r in references),
            "frozen dependency reference copy route changed")
    pair = [t for t in xml.findall("Target") if t.get("Name") == "ValidateFrozenCoordSelection"]
    require(len(pair) == 1 and pair[0].get("BeforeTargets") == "PrepareForBuild"
            and len(pair[0].findall("Error")) == 1
            and pair[0].find("Error").get("Condition") == PAIR_ERROR,
            "frozen dependency paired selection guard changed")
    require(not list(xml.iter("ErrorOnDuplicatePublishOutputFiles")),
            "frozen dependency duplicate publish enforcement changed")


def archive_members(package, pin):
    require(digest(regular(package)) == pin["archiveSha256"], "published dependency archive changed")
    members = {}
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "duplicate dependency archive member")
        require(sum(x.file_size for x in archive.infolist()) <= 134217728, "dependency expansion bound")
        require("FS.GG.Coord.Cli.nuspec" in names, "published dependency package metadata missing")
        metadata = ElementTree.fromstring(archive.read("FS.GG.Coord.Cli.nuspec"))
        def values(name):
            return [node for node in metadata.iter() if node.tag.rsplit("}", 1)[-1] == name]
        package_ids, versions, repositories = values("id"), values("version"), values("repository")
        require(len(package_ids) == len(versions) == len(repositories) == 1
                and package_ids[0].text == "FS.GG.Coord.Cli"
                and versions[0].text == pin["version"]
                and repositories[0].get("commit") == pin["sourceSha"],
                "published dependency package/version/source identity changed")
        for entry in archive.infolist():
            name = entry.filename
            path = pathlib.PurePosixPath(name)
            require(not path.is_absolute() and ".." not in path.parts and "\\" not in name,
                    "unsafe dependency archive path")
            require(not stat.S_ISLNK(entry.external_attr >> 16), "linked dependency archive member")
            if name.startswith(PREFIX) and not entry.is_dir() and name != PREFIX + "DotnetToolSettings.xml":
                relative = name[len(PREFIX):]
                body = archive.read(entry)
                require(relative in pin["members"] and digest(body) == pin["members"][relative],
                        "foreign or changed published dependency member")
                members[relative] = body
    require(set(members) == set(pin["members"]), "incomplete published dependency closure")
    return members


def layout(root, dependencies, pin, producer_root):
    source_projects(root, pin, producer_root)
    require(dependencies.is_dir() and not dependencies.is_symlink(), "missing frozen dependency directory")
    require(all(not p.is_symlink() for p in dependencies.rglob("*")), "linked frozen dependency member")
    files = {p.relative_to(dependencies).as_posix() for p in dependencies.rglob("*") if p.is_file()}
    require(files == set(pin["members"]), "frozen dependency directory census differs")
    for name, expected in pin["members"].items():
        require(digest(regular(dependencies / name)) == expected, "frozen dependency body changed: " + name)
    for project, assembly in pin["projects"].items():
        if assembly == "new-sdd-workspace":
            continue
        output = (producer_root / project).parent / "bin/Release/net10.0"
        for suffix in (".dll", ".pdb", ".xml"):
            name = assembly + suffix
            require(digest(regular(output / name)) == pin["members"][name], "staged project dependency changed")
    return {"currentCreatorSourceSha": git_identity(root), "publishedDependencySourceSha": pin["sourceSha"],
            "archiveSha256": pin["archiveSha256"],
            "coherentVersion": pin["version"], "dependencyMembers": len(pin["members"])}


def stage(root, dependencies, package, pin, producer_root):
    source_projects(root, pin, producer_root)
    bodies = archive_members(package, pin)
    require(not dependencies.exists(), "frozen dependency output already exists")
    require(all(not parent.is_symlink() for parent in (dependencies, *dependencies.parents)),
            "linked frozen dependency destination")
    destinations = []
    for project, assembly in pin["projects"].items():
        if assembly == "new-sdd-workspace":
            continue
        output = (producer_root / project).parent / "bin/Release/net10.0"
        require(all(not parent.is_symlink() for parent in (output, *output.parents)), "linked staged project output")
        require(not output.exists(), "staged project output already exists")
        for suffix in (".dll", ".pdb", ".xml"):
            name = assembly + suffix
            target = output / name
            require(not target.is_symlink(), "linked staged dependency destination")
            destinations.append((target, bodies[name]))
    dependencies.mkdir(parents=True)
    for name, body in bodies.items():
        target = dependencies / name
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(body)
    for target, body in destinations:
        target.parent.mkdir(parents=True, exist_ok=True)
        with target.open("xb") as stream:
            stream.write(body)
    return layout(root, dependencies, pin, producer_root)


def package_closure(package, dependencies, root=ROOT, producer_root=None):
    pin = json.loads(regular(root / "scripts/creator-frozen-coord-dependencies.json"))
    result = layout(root, dependencies, pin, producer_root)
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        require(len(names) == len(set(names)), "duplicate creator package member")
        for name, expected in pin["members"].items():
            require(PREFIX + name in names and digest(archive.read(PREFIX + name)) == expected,
                    "creator package changed published dependency: " + name)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("verify-source", "stage", "verify-layout"))
    parser.add_argument("--producer-source", type=pathlib.Path, required=True)
    parser.add_argument("--dependencies", type=pathlib.Path)
    parser.add_argument("--package", type=pathlib.Path)
    args = parser.parse_args()
    pin = json.loads(regular(PIN))
    if args.command == "verify-source":
        require(args.dependencies is None and args.package is None, "source verification refuses staging inputs")
        projects = source_projects(ROOT, pin, args.producer_source)
        print(json.dumps({"currentCreatorSourceSha": git_identity(ROOT),
                          "publishedDependencySourceSha": pin["sourceSha"], "coherentVersion": pin["version"],
                          "sourceProjects": len(projects)}, sort_keys=True))
        raise SystemExit(0)
    require(args.dependencies is not None, "frozen dependency directory required")
    require(args.dependencies.is_absolute(), "frozen dependency directory must be absolute")
    # Preserve the final path component so a symlink cannot disappear through resolve().
    dependencies = args.dependencies
    if args.command == "stage":
        require(args.package is not None, "stage requires accepted dependency archive")
        result = stage(ROOT, dependencies, args.package, pin, args.producer_source)
    else:
        result = layout(ROOT, dependencies, pin, args.producer_source)
    print(json.dumps(result, sort_keys=True))

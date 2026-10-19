#!/usr/bin/env python3
"""Refusal and recovery controls for independent Wizard release effects."""

from __future__ import annotations

import hashlib
import contextlib
import io
import subprocess
import urllib.error
import ast
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest
import zipfile
import runpy
import shlex
from xml.etree import ElementTree
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_successor_execution import Dispatch, JournalState, Observation, Refused, advance_effects
from release_successor_journal import Refused as JournalRefused
from new_sdd_workspace_successor_admission import WizardAdmission
from new_sdd_workspace_successor_execution import effects, CURRENT_017, CURRENT_018, CURRENT_016, CURRENT_015, CURRENT_014, HISTORICAL_013, ReleaseBinding
from new_sdd_workspace_successor_provider import WizardProvider, NotFound, output_signals, publisher_error, DIAGNOSTIC_LIMIT


SOURCE_FLOWS = {
    "new-sdd-workspace-selftest.yml": (
        "Run the new-sdd-workspace parse fixture",
        "Qualify the compiled product binding and complete driver materialization",
        "Qualify Creator 0.18 normal-source package closure", "source"),
}


def source_flow(workflow: str, names: tuple[str, str, str, str]) -> None:
    """Bound the existing linear producer/consumer commands, without executing a build."""
    def step(name):
        marker = f"      - name: {name}\n"
        if workflow.count(marker) != 1:
            raise ValueError("Creator source flow: missing or repeated step")
        section = workflow.split(marker, 1)[1].split("\n      - ", 1)[0]
        _, sep, body = section.partition("        run: |\n")
        if not sep:
            raise ValueError("Creator source flow: expected explicit run body")
        return "\n".join(line[10:] for line in body.splitlines() if line.startswith("          "))

    producer, materializer, package = map(step, names[:3])
    assignment = ('export FSGG_CREATOR_SOURCE_ARTIFACTS="$RUNNER_TEMP/creator018-'
                  + names[3] + '-build-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT"')
    retention = 'echo "FSGG_CREATOR_SOURCE_ARTIFACTS=$FSGG_CREATOR_SOURCE_ARTIFACTS" >> "$GITHUB_ENV"'
    launch = 'bash tests/new-sdd-workspace/run.sh'
    if (any(producer.splitlines().count(line) != 1 for line in (assignment, retention, launch))
            or not producer.index(assignment) < producer.index(retention) < producer.index(launch)):
        raise ValueError("Creator source flow: fresh root must be retained before the real producer")
    directory = '$FSGG_CREATOR_SOURCE_ARTIFACTS/bin/NewSddWorkspace/release/'
    def commands(body):
        return [shlex.split(line, comments=True) for line in body.replace('\\\n', ' ').splitlines() if line.strip()]
    materializers = [args for args in commands(materializer) if args[:2] == ['dotnet', 'fsi']]
    references = {f'--reference:{directory}{leaf}' for leaf in (
        'new-sdd-workspace.dll', 'fsgg-coord-engine.dll',
        'FS.GG.Coord.GitHub.dll', 'FS.GG.Coord.Core.dll')}
    if (len(materializers) != 1
            or [arg for arg in materializers[0] if arg.startswith('--lib:')] != [f'--lib:{directory}']
            or len([arg for arg in materializers[0] if arg.startswith('--reference:')]) != 4
            or set(arg for arg in materializers[0] if arg.startswith('--reference:')) != references
            or materializers[0][-2:] != ['--exec', 'tests/new-sdd-workspace/product-board.fsx']):
        raise ValueError("Creator source flow: FSI must consume the retained producer")
    packs = [args for args in commands(package) if args[:2] == ['dotnet', 'pack']]
    validators = [args for args in commands(package)
                  if args[:2] == ['python3', 'tests/new-sdd-workspace-successor/run.py'] and '--board-package' in args]
    if len(packs) != 1 or len(validators) != 1:
        raise ValueError("Creator source flow: expected one pack command")
    pack, validator = packs[0], validators[0]
    if (pack[:3] != ['dotnet', 'pack', 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj']
            or pack.count('--no-build') != 1 or pack.count('--artifacts-path') != 1
            or pack[pack.index('--artifacts-path') + 1:pack.index('--artifacts-path') + 2]
                != ['$FSGG_CREATOR_SOURCE_ARTIFACTS']
            or validator.count('--build-artifacts') != 1
            or validator[validator.index('--build-artifacts') + 1:validator.index('--build-artifacts') + 2]
                != ['$FSGG_CREATOR_SOURCE_ARTIFACTS']
            or 'scripts/NewSddWorkspace/bin/' in materializer + package):
        raise ValueError("Creator source flow: pack and validator must use the retained producer")


def manifest():
    return {
        "schema": "fsgg.new-sdd-workspace-release/1", "packageId": "FS.GG.NewSddWorkspace",
        "version": "0.18.0", "tag": "new-sdd-workspace/v0.18.0", "sourceSha": "a" * 40,
        "archiveSha256": "b" * 64, "producerPayloadSha256": "sha256:" + "c" * 64,
    }


def board_package_closure(package: pathlib.Path, source_sha: str, *, source_root: pathlib.Path = ROOT,
                          frozen_dependencies: pathlib.Path | None = None,
                          frozen_source: pathlib.Path | None = None,
                          build_artifacts: pathlib.Path | None = None,
                          release_binding=CURRENT_018):
    """Join the actual current package to its freshly built creator/dependency output."""
    spec = importlib.util.spec_from_file_location("wizard_package_identity", source_root / "scripts/new-sdd-workspace-release.py")
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    if release_binding is not CURRENT_018 and release_binding is not CURRENT_016:
        raise ValueError("unknown Creator package binding")
    if checker.package_identity(package) != (release_binding.package, release_binding.version, source_sha):
        raise ValueError("current creator package/source identity differs")
    dependency_pin = json.loads((source_root / "scripts/creator-frozen-coord-dependencies.json").read_text())
    coherent_version = "0.108.0" if release_binding is CURRENT_018 else dependency_pin["version"]
    if release_binding is CURRENT_018 and (frozen_dependencies is not None or frozen_source is not None):
        raise ValueError("Creator 0.18 requires its normal 0.108 source graph, not retained frozen inputs")
    if (frozen_dependencies is None) != (frozen_source is None):
        raise ValueError("frozen package qualification requires paired source and binary roots")
    project = source_root / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
    projects = {}
    helper = runpy.run_path(str(source_root / "scripts/creator-frozen-coord-dependencies.py")) if frozen_source is not None else None
    if helper is not None:
        if helper["git_identity"](source_root) != source_sha:
            raise ValueError("current Creator source revision differs")
        selected = helper["source_projects"](source_root, dependency_pin, frozen_source)
        projects = {(project if name == "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
                     else frozen_source / name): assembly for name, assembly in selected.items()}
    else:
        def visit(path):
            if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):
                raise ValueError("linked ordinary Creator dependency project")
            path = path.resolve()
            if not path.is_relative_to(source_root.resolve()):
                raise ValueError("foreign ordinary Creator dependency project")
            if path in projects:
                return
            if len(projects) >= 11:
                raise ValueError("ordinary Creator dependency graph exceeds its closed 11 projects")
            xml = ElementTree.parse(path)
            projects[path] = next((e.text for e in xml.iter("AssemblyName") if e.text), path.stem)
            for reference in xml.iter("ProjectReference"):
                condition = reference.get("Condition")
                if condition == "'$(FsggFrozenCoordDependencies)' != '' And '$(FsggFrozenCoordSource)' != ''":
                    continue
                if condition not in (None, "'$(FsggFrozenCoordDependencies)' == '' And '$(FsggFrozenCoordSource)' == ''"):
                    raise ValueError("ambiguous ordinary project reference")
                declared = reference.attrib["Include"]
                if any(character in declared for character in ("$", "*", "?", ";")):
                    raise ValueError("nonliteral ordinary Creator dependency project")
                visit(path.parent / declared)
        visit(project)
    if build_artifacts is not None:
        if release_binding is not CURRENT_018 or not build_artifacts.is_absolute():
            raise ValueError("isolated build artifacts require the current Creator and an absolute root")
        if (build_artifacts.is_symlink() or not build_artifacts.is_dir()
                or build_artifacts.resolve().is_relative_to(source_root.resolve())):
            raise ValueError("Creator build artifacts must be outside the source checkout")
        if len({path.stem for path in projects}) != len(projects):
            raise ValueError("Creator project artifact names collide")
        output = build_artifacts / "bin" / project.stem / "release"
    else:
        output = project.parent / "bin/Release/net10.0"
    required = {name + ".dll" for name in projects.values()}
    required.update({"new-sdd-workspace.deps.json", "new-sdd-workspace.runtimeconfig.json"})
    required.update(str(p.relative_to(output)) for p in output.rglob("*")
                    if p.is_file() and p.relative_to(output).parts[0] != "publish"
                    and p.suffix in {".dll", ".so", ".dylib"})
    if len(projects) != 11 or not {"FS.GG.Coord.GitHub.dll", "fsgg-coord-engine.dll"} <= required:
        raise ValueError("current creator board project closure differs")
    prefix = "tools/net10.0/any/"
    with zipfile.ZipFile(package) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("duplicate creator package members")
        for name in sorted(required):
            built = output / name
            if built.is_symlink() or not built.is_file() or prefix + name not in names:
                raise ValueError("creator package dependency missing: " + name)
            if archive.read(prefix + name) != built.read_bytes():
                raise ValueError("creator package dependency differs from built source: " + name)
        deps = json.loads(archive.read(prefix + "new-sdd-workspace.deps.json"))
        cli = "FS.GG.Coord.Cli/" + coherent_version
        targets = deps.get("targets", {})
        if (deps.get("libraries", {}).get(cli, {}).get("type") != "project"
                or not targets or any(
                    target.get("new-sdd-workspace/" + release_binding.version, {}).get("dependencies", {}).get("FS.GG.Coord.Cli") != coherent_version
                    or "fsgg-coord-engine.dll" not in target.get(cli, {}).get("runtime", {})
                    for target in targets.values())):
            raise ValueError("creator package must carry current coherent CLI dependency metadata")
    result = {"schema": "fsgg.creator-board-package-closure/1", "version": release_binding.version,
            "sourceSha": source_sha, "currentCreatorSourceSha": source_sha, "coherentVersion": coherent_version, "projectCount": len(projects),
            "builtFilesCompared": len(required), "archiveSha256": hashlib.sha256(package.read_bytes()).hexdigest(),
            "mode": "source-only", "releaseEligible": False, "installedAdoptionAccepted": False}
    if frozen_dependencies is not None:
        result["publishedDependencyClosure"] = helper["package_closure"](
            package, frozen_dependencies, source_root, frozen_source)
        result["publishedDependencySourceSha"] = dependency_pin["sourceSha"]
    return result


class Journal:
    def __init__(self, content_id):
        self.state = JournalState(1, content_id, {})

    def read(self):
        return self.state

    def compare_and_swap(self, expected, effect, state):
        if self.state != expected:
            return False
        self.state = JournalState(expected.generation + 1, expected.content_id,
                                  {**expected.effects, effect: state})
        return True


class Admission:
    def __init__(self):
        self.denied = set()

    def authorize(self, content_id, effect, action, request_digest):
        return (effect, action) not in self.denied


class Provider:
    def __init__(self):
        self.visible = {}
        self.writes = []
        self.delayed = set()

    def observe(self, effect):
        value = self.visible.get(effect.identity)
        if value is None:
            return Observation("absent")
        return Observation("matched" if value == effect.target_digest else "mismatched", value)

    def dispatch(self, effect):
        self.writes.append(effect.identity)
        if effect.identity not in self.delayed:
            self.visible[effect.identity] = effect.target_digest
        return Dispatch("unknown")


class WizardReleaseTests(unittest.TestCase):
    def setUp(self):
        self.content_id, self.ordered = effects(manifest())
        self.journal = Journal(self.content_id)
        self.admission = Admission()
        self.provider = Provider()

    def advance(self):
        return advance_effects(self.content_id, self.ordered, self.journal, self.admission, self.provider)

    def test_release_bindings_are_explicit_immutable_and_route_specific(self):
        published = {**manifest(), "version": "0.14.0", "tag": "new-sdd-workspace/v0.14.0"}
        with self.assertRaises(Refused):
            effects(published)
        with self.assertRaises(Refused):
            effects(published, binding=CURRENT_014)
        published015 = {**manifest(), "version": "0.15.0", "tag": "new-sdd-workspace/v0.15.0"}
        with self.assertRaises(Refused):
            effects(published015)
        with self.assertRaises(Refused):
            effects(published015, binding=CURRENT_015)
        historical = {**manifest(), "version": "0.13.0", "tag": "new-sdd-workspace/v0.13.0"}
        with self.assertRaises(Refused):
            effects(historical)
        content, ordered = effects(historical, binding=HISTORICAL_013)
        canonical = json.dumps(historical, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
        self.assertEqual(content, "sha256:" + hashlib.sha256(canonical).hexdigest())
        self.assertEqual([effect.identity for effect in ordered], [effect.identity for effect in self.ordered])
        with self.assertRaises(Refused):
            effects(manifest(), binding=HISTORICAL_013)
        for forged in (ReleaseBinding(CURRENT_018.package, CURRENT_018.version, CURRENT_018.tag),
                       ReleaseBinding(HISTORICAL_013.package, HISTORICAL_013.version, HISTORICAL_013.tag), None):
            with self.assertRaises(Refused):
                effects(manifest(), binding=forged)
        with self.assertRaises(AttributeError):
            CURRENT_018.version = "0.13.0"

    def test_retained016_effect_descriptors_cannot_become_current018_authority(self):
        class NoReads:
            def get(self, path): raise AssertionError("retained route cannot read native authority")
        prior={**manifest(), "version":"0.16.0", "tag":"new-sdd-workspace/v0.16.0"}
        with self.assertRaises(Refused): effects(prior)
        content, ordered=effects(prior,binding=CURRENT_016)
        self.assertEqual(len(ordered),8)
        retained=WizardAdmission(NoReads(),prior,"a"*40,1,"EHotwagner","refs/heads/main",release_binding=CURRENT_016)
        self.assertFalse(retained.authorize(content,"journal","intent",content))
        self.assertEqual((CURRENT_016.version,CURRENT_016.tag),("0.16.0","new-sdd-workspace/v0.16.0"))
        prior017 = {**manifest(), "version": "0.17.0", "tag": "new-sdd-workspace/v0.17.0"}
        with self.assertRaises(Refused): effects(prior017)
        content017, ordered017 = effects(prior017, binding=CURRENT_017)
        self.assertEqual(len(ordered017), 8)
        retained017 = WizardAdmission(NoReads(), prior017, "a"*40, 1, "EHotwagner", "refs/heads/main", release_binding=CURRENT_017)
        self.assertFalse(retained017.authorize(content017, "journal", "intent", content017))
        self.assertEqual((HISTORICAL_013.version,HISTORICAL_013.tag),("0.13.0","new-sdd-workspace/v0.13.0"))

    def test_admission_cannot_cross_current_and_historical_routes(self):
        class NoReads:
            def get(self, path):
                raise AssertionError("route mismatch must refuse before remote reads")
        historical = {**manifest(), "version": "0.13.0", "tag": "new-sdd-workspace/v0.13.0"}
        old = WizardAdmission(NoReads(), historical, "a" * 40, 1, "EHotwagner", "refs/heads/main",
                              release_binding=HISTORICAL_013)
        self.assertFalse(old.authorize(old.content_id, "journal", "intent", old.content_id))
        current = WizardAdmission(NoReads(), manifest(), "a" * 40, 1, "EHotwagner", "refs/heads/main")
        self.assertFalse(current.authorize_recovery(current.content_id, "dispatch", current.requests["promote"],
                                                     {"heldSource": "a" * 40}, "complete"))
        with self.assertRaises(Refused):
            WizardAdmission(NoReads(), historical, "a" * 40, 1, "EHotwagner", "refs/heads/main")

    def test_frozen_package_joins_current_creator_and_only_the_selected_producer(self):
        # Real helper/staging/package wiring with synthetic DLL bytes, never executed.
        with tempfile.TemporaryDirectory() as temporary:
            base = pathlib.Path(temporary)
            creator, producer = base / "creator", base / "producer"
            creator.mkdir(); producer.mkdir()
            helper = runpy.run_path(str(ROOT / "scripts/creator-frozen-coord-dependencies.py"))
            projects = json.loads((ROOT / "scripts/creator-frozen-coord-dependencies.json").read_text())["projects"]
            producer_projects = {p: a for p, a in projects.items() if a != "new-sdd-workspace"}
            for path, assembly in producer_projects.items():
                target = producer / path; target.parent.mkdir(parents=True)
                references = ""
                if assembly == "fsgg-coord-engine":
                    references = ''.join('<ProjectReference Include="../' + pathlib.Path(p).parent.name + '/' + pathlib.Path(p).name + '" />'
                                         for p in producer_projects if p != path)
                target.write_text('<Project><AssemblyName>' + assembly + '</AssemblyName><ItemGroup>' + references + '</ItemGroup></Project>')
                (target.parent / "packages.lock.json").write_text('{}')
            (producer / "global.json").write_text('{}')
            def seal(root):
                for argv in (["git", "init", "-q"], ["git", "add", "."],
                             ["git", "-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "fixture"]):
                    subprocess.run(argv, cwd=root, check=True, capture_output=True, timeout=10)
                return helper["git_identity"](root)
            producer_sha = seal(producer)
            bodies = {a + suffix: ("synthetic, never executed: " + a + suffix).encode()
                      for a in producer_projects.values() for suffix in (".dll", ".pdb", ".xml")}
            dependency_package = base / "published.nupkg"
            prefix = "tools/net10.0/any/"
            with zipfile.ZipFile(dependency_package, "w") as archive:
                archive.writestr("FS.GG.Coord.Cli.nuspec", '<package><metadata><id>FS.GG.Coord.Cli</id><version>0.99.0</version><repository commit="' + producer_sha + '"/></metadata></package>')
                for name, body in bodies.items(): archive.writestr(prefix + name, body)
            pin = {"version": "0.99.0", "sourceSha": producer_sha, "projects": projects,
                   "archiveSha256": hashlib.sha256(dependency_package.read_bytes()).hexdigest(),
                   "members": {n: hashlib.sha256(b).hexdigest() for n, b in bodies.items()},
                   "sourceLeaves": [{"path": n, "sha256": hashlib.sha256((producer / n).read_bytes()).hexdigest()}
                                    for n in sorted(helper["dependency_source_paths"](producer, projects))]}
            for name in ("scripts/NewSddWorkspace/NewSddWorkspace.fsproj", "scripts/new-sdd-workspace-release.py",
                         "scripts/creator-frozen-coord-dependencies.py"):
                target = creator / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes((ROOT / name).read_bytes())
            (creator / "scripts/creator-frozen-coord-dependencies.json").write_text(json.dumps(pin))
            creator_sha = seal(creator)
            dependencies = base / "binaries"
            helper["stage"](creator, dependencies, dependency_package, pin, producer)
            output = creator / "scripts/NewSddWorkspace/bin/Release/net10.0"; output.mkdir(parents=True)
            creator_bodies = {**bodies, "new-sdd-workspace.dll": b"synthetic current creator, never executed",
                "new-sdd-workspace.runtimeconfig.json": b"{}", "new-sdd-workspace.deps.json": json.dumps({
                    "libraries": {"FS.GG.Coord.Cli/0.99.0": {"type": "project"}}, "targets": {"net10.0": {
                        "new-sdd-workspace/0.16.0": {"dependencies": {"FS.GG.Coord.Cli": "0.99.0"}},
                        "FS.GG.Coord.Cli/0.99.0": {"runtime": {"fsgg-coord-engine.dll": {}}}}}}).encode()}
            package = base / "creator.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("FS.GG.NewSddWorkspace.nuspec", '<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.16.0</version><repository commit="' + creator_sha + '"/></metadata></package>')
                for name, body in creator_bodies.items(): (output / name).write_bytes(body); archive.writestr(prefix + name, body)
            with self.assertRaisesRegex(ValueError, "package/source identity differs"):
                board_package_closure(package, creator_sha, source_root=creator)
            result = board_package_closure(package, creator_sha, source_root=creator,
                                           frozen_dependencies=dependencies, frozen_source=producer, release_binding=CURRENT_016)
            self.assertEqual(result["projectCount"], 11)
            self.assertEqual(result["currentCreatorSourceSha"], creator_sha)
            self.assertEqual(result["publishedDependencySourceSha"], producer_sha)
            for selected in (None, creator):
                with self.assertRaises(ValueError):
                    board_package_closure(package, creator_sha, source_root=creator,
                                          frozen_dependencies=dependencies, frozen_source=selected, release_binding=CURRENT_016)
            with self.assertRaisesRegex(ValueError, "package/source identity differs"):
                board_package_closure(package, "b" * 40, source_root=creator,
                                      frozen_dependencies=dependencies, frozen_source=producer, release_binding=CURRENT_016)
            (creator / "new-owner-file").write_text("later Creator source")
            seal(creator)
            with self.assertRaisesRegex(ValueError, "current Creator source revision differs"):
                board_package_closure(package, creator_sha, source_root=creator,
                                      frozen_dependencies=dependencies, frozen_source=producer, release_binding=CURRENT_016)

    def test_package_closure_joins_projects_metadata_and_actual_built_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            files = {}
            def copy_project(path):
                relative = path.relative_to(ROOT)
                if relative in files:
                    return
                files[relative] = path.read_bytes()
                for reference in ElementTree.parse(path).iter('ProjectReference'):
                    if reference.get('Condition') == "'$(FsggFrozenCoordDependencies)' != '' And '$(FsggFrozenCoordSource)' != ''":
                        continue
                    copy_project((path.parent / reference.attrib['Include']).resolve())
            copy_project(ROOT / 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj')
            files[pathlib.Path('scripts/creator-frozen-coord-dependencies.json')] = (ROOT / 'scripts/creator-frozen-coord-dependencies.json').read_bytes()
            files[pathlib.Path('scripts/new-sdd-workspace-release.py')] = (ROOT / 'scripts/new-sdd-workspace-release.py').read_bytes()
            for relative, body in files.items():
                (root / relative).parent.mkdir(parents=True, exist_ok=True)
                (root / relative).write_bytes(body)
            output = root / 'scripts/NewSddWorkspace/bin/Release/net10.0'
            output.mkdir(parents=True)
            members = {}
            for relative in files:
                if relative.suffix == '.fsproj':
                    xml = ElementTree.parse(root / relative)
                    name = next((e.text for e in xml.iter('AssemblyName') if e.text), relative.stem) + '.dll'
                    members[name] = ('synthetic, never executed: ' + name).encode()
            deps = {'libraries': {'FS.GG.Coord.Cli/0.108.0': {'type': 'project'}}, 'targets': {
                '.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.108.0'}},
                                           'FS.GG.Coord.Cli/0.108.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}}
            members['new-sdd-workspace.deps.json'] = json.dumps(deps).encode()
            members['new-sdd-workspace.runtimeconfig.json'] = b'{}'
            for name, body in members.items():
                (output / name).write_bytes(body)
            # PackAsTool stages another copy under publish; package destinations omit that prefix.
            (output / 'publish').mkdir()
            for name, body in members.items():
                (output / 'publish' / name).write_bytes(body)
            nuspec = b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.18.0</version><repository commit="' + b'a' * 40 + b'"/></metadata></package>'
            package = root / 'creator.nupkg'
            def pack(selected):
                with zipfile.ZipFile(package, 'w') as archive:
                    archive.writestr('FS.GG.NewSddWorkspace.nuspec', nuspec)
                    for name, body in selected.items():
                        archive.writestr('tools/net10.0/any/' + name, body)
            pack(members)
            self.assertEqual(board_package_closure(package, 'a' * 40, source_root=root)['projectCount'], 11)
            with tempfile.TemporaryDirectory() as isolated:
                artifacts = pathlib.Path(isolated)
                isolated_output = artifacts / 'bin/NewSddWorkspace/release'
                isolated_output.mkdir(parents=True)
                for name, body in members.items():
                    (isolated_output / name).write_bytes(body)
                # A stale in-tree assembly must neither win selection nor rescue
                # damaged explicitly selected isolated bytes.
                stale = output / 'fsgg-coord-engine.dll'
                stale.write_bytes(b'inert stale in-tree assembly')
                result = board_package_closure(package, 'a' * 40, source_root=root, build_artifacts=artifacts)
                self.assertEqual(result['projectCount'], 11)
                self.assertEqual(result['builtFilesCompared'], len(members))
                stale.write_bytes(members['fsgg-coord-engine.dll'])
                (isolated_output / 'fsgg-coord-engine.dll').write_bytes(b'inert damaged isolated assembly')
                with self.assertRaisesRegex(ValueError, 'differs from built source'):
                    board_package_closure(package, 'a' * 40, source_root=root, build_artifacts=artifacts)
                (isolated_output / 'fsgg-coord-engine.dll').unlink()
                with self.assertRaisesRegex(ValueError, 'dependency missing'):
                    board_package_closure(package, 'a' * 40, source_root=root, build_artifacts=artifacts)
            with self.assertRaisesRegex(ValueError, 'outside the source checkout'):
                board_package_closure(package, 'a' * 40, source_root=root, build_artifacts=root)
            with self.assertRaisesRegex(ValueError, '0.18 requires its normal 0.108 source graph'):
                board_package_closure(package, 'a' * 40, source_root=root,
                                      frozen_dependencies=root, frozen_source=root)
            for omitted in ('FS.GG.Coord.GitHub.dll', 'fsgg-coord-engine.dll', 'new-sdd-workspace.deps.json'):
                with self.subTest(omitted=omitted):
                    pack({name: body for name, body in members.items() if name != omitted})
                    with self.assertRaises(ValueError):
                        board_package_closure(package, 'a' * 40, source_root=root)
            pack({**members, 'FS.GG.Coord.GitHub.dll': b'old assembly'})
            with self.assertRaisesRegex(ValueError, 'differs from built source'):
                board_package_closure(package, 'a' * 40, source_root=root)
            for mutant in ({'libraries': {'FS.GG.Coord.Cli/0.99.0': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.99.0'}}, 'FS.GG.Coord.Cli/0.99.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.97.1': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.1'}}, 'FS.GG.Coord.Cli/0.97.1': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.97.0': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.0'}}, 'FS.GG.Coord.Cli/0.97.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.96.0': {}}},
                           {**deps, 'targets': {}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'FS.GG.Coord.Cli/0.108.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.18.0': {'dependencies': {'FS.GG.Coord.Cli': '0.108.0'}}}}}):
                old_deps = json.dumps(mutant).encode()
                (output / 'new-sdd-workspace.deps.json').write_bytes(old_deps)
                pack({**members, 'new-sdd-workspace.deps.json': old_deps})
                with self.assertRaisesRegex(ValueError, 'current coherent CLI dependency metadata'):
                    board_package_closure(package, 'a' * 40, source_root=root)
            with self.assertRaisesRegex(ValueError, 'package/source identity'):
                board_package_closure(package, 'b' * 40, source_root=root)
            project_path = root / 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj'
            original_project = project_path.read_bytes()
            for declaration, reason in [('../../../../outside.fsproj', 'foreign ordinary'),
                                        ('$(Unknown)/project.fsproj', 'nonliteral ordinary')]:
                xml = ElementTree.fromstring(original_project)
                reference = next(node for node in xml.iter('ProjectReference')
                                 if node.get('Condition') == "'$(FsggFrozenCoordDependencies)' == '' And '$(FsggFrozenCoordSource)' == ''")
                reference.set('Include', declaration)
                project_path.write_bytes(ElementTree.tostring(xml))
                with self.assertRaisesRegex(ValueError, reason):
                    board_package_closure(package, 'a' * 40, source_root=root)
            project_path.write_bytes(original_project)
            xml = ElementTree.fromstring(original_project)
            extra = ElementTree.SubElement(xml, 'ItemGroup')
            ElementTree.SubElement(extra, 'ProjectReference', {'Include':'extra.fsproj'})
            project_path.write_bytes(ElementTree.tostring(xml))
            with self.assertRaisesRegex(ValueError, 'closed 11 projects'):
                board_package_closure(package, 'a' * 40, source_root=root)
            project_path.write_bytes(original_project)

    def test_every_effect_is_admitted_and_read_back_once(self):
        self.assertEqual(len(self.ordered), 8)
        for effect in self.ordered:
            self.assertEqual(self.advance(), "waiting")
            self.assertEqual(self.advance(), "verified")
            self.assertEqual(self.provider.writes[-1], effect.identity)
        self.assertEqual(self.advance(), "complete")
        self.assertEqual(len(self.provider.writes), 8)

    def test_denied_dispatch_never_mutates_provider(self):
        self.admission.denied.add(("tag", "dispatch"))
        with self.assertRaisesRegex(Refused, "dispatch admission denied"):
            self.advance()
        self.assertEqual(self.provider.writes, [])

    def test_delayed_tag_readback_does_not_retry_or_advance(self):
        self.provider.delayed.add("tag")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.provider.writes, ["tag"])
        self.provider.visible["tag"] = self.ordered[0].target_digest
        self.assertEqual(self.advance(), "verified")

    def test_candidate_archive_identity_and_first_attempt_are_required(self):
        path = ROOT / "scripts" / "new-sdd-workspace-successor-artifact.py"
        spec = importlib.util.spec_from_file_location("wizard_artifact", path)
        assert spec and spec.loader
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        source = "a" * 40
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            archive = root / "candidate.zip"
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("manifest.json", json.dumps({**manifest(), "version": "0.17.0", "tag": "new-sdd-workspace/v0.17.0"}))
                zipped.writestr("package-evidence.json", "{}")
                zipped.writestr("FS.GG.NewSddWorkspace.0.17.0.nupkg", "fixture")
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            run = {"id": 123, "path": verifier.WORKFLOW, "head_sha": source,
                   "head_branch": "main", "event": "workflow_dispatch", "conclusion": "success",
                   "run_attempt": 1, "repository": {"id": verifier.REPOSITORY_ID}}
            artifact = {"id": 456, "name": f"new-sdd-workspace-successor-candidate-{source}-123",
                        "expired": False, "digest": "sha256:" + digest,
                        "workflow_run": {"id": 123, "head_sha": source, "head_branch": "main",
                                         "repository_id": verifier.REPOSITORY_ID,
                                         "head_repository_id": verifier.REPOSITORY_ID}}
            with patch.object(verifier.subprocess, "run"):
                self.assertEqual(verifier.verify(artifact, run, archive, root / "good", source, release_binding=CURRENT_017)["version"], "0.17.0")
            old_archive = root / "historical.zip"
            with zipfile.ZipFile(old_archive, "w") as zipped:
                zipped.writestr("manifest.json", json.dumps({**manifest(), "version": "0.13.0",
                                                            "tag": "new-sdd-workspace/v0.13.0"}))
                zipped.writestr("package-evidence.json", "{}")
                zipped.writestr("FS.GG.NewSddWorkspace.0.13.0.nupkg", "fixture")
            old_artifact = {**artifact, "digest": "sha256:" + hashlib.sha256(old_archive.read_bytes()).hexdigest()}
            with patch.object(verifier.subprocess, "run") as package_check:
                with self.assertRaises(ValueError):
                    verifier.verify(old_artifact, run, old_archive, root / "old", source, release_binding=CURRENT_017)
                package_check.assert_not_called()
            altered = {**run, "run_attempt": 2}
            with self.assertRaisesRegex(ValueError, "first-attempt"):
                verifier.verify(artifact, altered, archive, root / "rerun", source, release_binding=CURRENT_017)
            altered_artifact = {**artifact, "digest": "sha256:" + "0" * 64}
            with self.assertRaisesRegex(ValueError, "digest differs"):
                verifier.verify(altered_artifact, run, archive, root / "tampered", source, release_binding=CURRENT_017)

    def test_prior_release_identity_and_wrong_payload_are_refused(self):
        for key, value in (("version", "0.13.0"), ("tag", "new-sdd-workspace/v0.13.0"),
                           ("version", "0.12.0"), ("tag", "new-sdd-workspace/v0.12.0"),
                           ("version", "0.11.2"), ("tag", "new-sdd-workspace/v0.11.2"),
                           ("producerPayloadSha256", "bad")):
            row = manifest(); row[key] = value
            with self.assertRaises(Refused):
                effects(row)

    def test_predecessor_manifest_cannot_reuse_successor_effects(self):
        prior = {**manifest(), "version": "0.12.0", "tag": "new-sdd-workspace/v0.12.0"}
        with self.assertRaisesRegex(Refused, "exact Wizard 0.18.0"):
            effects(prior)

    def test_fresh_journal_and_version_cut_retain_the_existing_boundary(self):
        publisher = (ROOT / "scripts/new-sdd-workspace-successor-publish.py").read_text()
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/board-v2-product-creator-018"', publisher)
        self.assertNotIn('REF = "refs/heads/fsgg/v2/journal/release/svg-d5-wizard-012"', publisher)
        self.assertIn('"version": "0.18.0"', publisher)
        self.assertEqual([e.identity for e in self.ordered],
                         ['tag', 'draft', 'github', 'nuget', 'package-asset', 'manifest-asset',
                          'publication-journal-asset', 'promote'])
        intent = next(node.value for node in ast.walk(ast.parse(publisher))
                      if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == 'intent' for t in node.targets))
        self.assertEqual({key.value for key in intent.keys},
                         {'contentId', 'sourceSha', 'version', 'candidateArchiveSha256', 'operator'})

    def test_source_cut_and_candidate_package_names_agree(self):
        project = (ROOT / 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj').read_text()
        candidate = (ROOT / '.github/workflows/release-new-sdd-workspace-successor-candidate.yml').read_text()
        self.assertIn('<Version>0.18.0</Version>', project)
        self.assertIn('test "$version" = 0.18.0', candidate)
        self.assertIn('FS.GG.NewSddWorkspace.0.18.0.nupkg', candidate)
        self.assertIn('--tag new-sdd-workspace/v0.18.0', candidate)
        props = ElementTree.parse(ROOT / 'Directory.Build.props')
        self.assertEqual(next(node.text for node in props.iter('FsggCoherentSetVersion')), '0.108.0')
        self.assertLess(candidate.index("creator-current-coord-dependencies.py preflight"),
                        candidate.index("actions/setup-dotnet"))
        self.assertIn('creator-current-coord-dependencies.py preflight', candidate)
        self.assertIn('FSGG_CREATOR_DEPENDENCY_MODE: published-dependency', candidate)

    def test_current_source_flows_keep_one_isolated_producer(self):
        workflows = ROOT / '.github/workflows'
        actual = {path.name for path in workflows.glob('*.yml')
                  if ('bash tests/new-sdd-workspace/run.sh' in path.read_text()
                      or '--exec tests/new-sdd-workspace/product-board.fsx' in path.read_text()
                      or 'dotnet pack scripts/NewSddWorkspace/NewSddWorkspace.fsproj' in path.read_text())}
        actual.discard("release-new-sdd-workspace-successor-candidate.yml")
        self.assertEqual(actual, set(SOURCE_FLOWS), 'unreviewed Creator source build consumer')
        for name, steps in SOURCE_FLOWS.items():
            original = (workflows / name).read_text()
            source_flow(original, steps)
            mutations = (
                ('echo "FSGG_CREATOR_SOURCE_ARTIFACTS=$FSGG_CREATOR_SOURCE_ARTIFACTS" >> "$GITHUB_ENV"', ':'),
                ('-build-$GITHUB_RUN_ID-$GITHUB_RUN_ATTEMPT', '-fixed-root'),
                ('$FSGG_CREATOR_SOURCE_ARTIFACTS/bin/NewSddWorkspace/release/', 'scripts/NewSddWorkspace/bin/Release/net10.0/'),
                ('--no-build', ''),
                ('--artifacts-path "$FSGG_CREATOR_SOURCE_ARTIFACTS"', '--artifacts-path "$OTHER_ROOT"'),
                ('--build-artifacts "$FSGG_CREATOR_SOURCE_ARTIFACTS"', ''),
                ('--build-artifacts "$FSGG_CREATOR_SOURCE_ARTIFACTS"', '# --build-artifacts "$FSGG_CREATOR_SOURCE_ARTIFACTS"'),
                ('--artifacts-path "$FSGG_CREATOR_SOURCE_ARTIFACTS"', '--artifacts-path "$FSGG_CREATOR_SOURCE_ARTIFACTS" --artifacts-path "$OTHER_ROOT"'),
            )
            for old, new in mutations:
                with self.subTest(workflow=name, mutation=old):
                    self.assertIn(old, original, 'mutation preimage missing')
                    changed = original.replace(old, new)
                    self.assertNotEqual(original, changed)
                    with self.assertRaisesRegex(ValueError, 'Creator source flow:'):
                        source_flow(changed, steps)

    def test_workflow_retains_exact_historical_title_and_isolates_current_mode(self):
        workflow = (ROOT / '.github/workflows/release-new-sdd-workspace.yml').read_text()
        self.assertIn("inputs.promotion_recovery != 'off' && format('Wizard 0.13 recovery {0} {1} {2}', "
                      "inputs.promotion_recovery, inputs.recovery_correlation, inputs.recovery_binding_sha256)", workflow)
        self.assertIn("format('Wizard 0.18 successor {0}', inputs.candidate_run_id)", workflow)
        self.assertIn("name: new-sdd-workspace-018-publisher", workflow)
        self.assertIn("(inputs.promotion_recovery == 'diagnostic' || inputs.promotion_recovery == 'post-completion-readback') && !inputs.publish && !inputs.verify_nuget_login", workflow)
        self.assertIn("inputs.promotion_recovery == 'complete' && !inputs.publish && !inputs.verify_nuget_login", workflow)
        for mode in ('diagnostic', 'complete'):
            self.assertIn(f"wizard013-recovery-{mode}", workflow)
        publisher = (ROOT / 'scripts/new-sdd-workspace-successor-publish.py').read_text()
        self.assertLess(publisher.index('if args.promotion_recovery:'), publisher.index('manifest ='))
        self.assertIn('==\n                    (CANDIDATE_RUN, ARTIFACT, ARCHIVE)', publisher)

    def test_package_nuspec_must_bind_protected_source_commit(self):
        path = ROOT / "scripts" / "new-sdd-workspace-release.py"
        spec = importlib.util.spec_from_file_location("wizard_release", path)
        assert spec and spec.loader
        checker = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(checker)
        with tempfile.TemporaryDirectory() as temporary:
            package = pathlib.Path(temporary) / "FS.GG.NewSddWorkspace.0.18.0.nupkg"
            def write_package(commit):
                nuspec = ("<package><metadata><id>FS.GG.NewSddWorkspace</id>"
                          "<version>0.18.0</version><repository commit='" + commit + "'/></metadata></package>")
                with zipfile.ZipFile(package, "w") as archive:
                    archive.writestr("FS.GG.NewSddWorkspace.nuspec", nuspec)
            write_package("a" * 40)
            self.assertEqual(checker.package_identity(package),
                             ("FS.GG.NewSddWorkspace", "0.18.0", "a" * 40))
            write_package("b" * 40)
            with self.assertRaisesRegex(ValueError, "repository commit differs"):
                checker.build_manifest(type("Args", (), {"package": str(package), "source_sha": "a" * 40,
                                                       "version": "0.18.0", "tag": "new-sdd-workspace/v0.18.0"})())
            write_package("")
            with self.assertRaisesRegex(ValueError, "repository commit"):
                checker.package_identity(package)

    def test_promotion_requires_clean_public_install(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            row = manifest()
            path = root / "manifest.json"
            path.write_text(json.dumps(row))
            class API:
                def __init__(self): self.writes = []
                def patch(self, route, body): self.writes.append((route, body))
            api = API()
            provider = WizardProvider(api, path, "github-token", "nuget-key")
            provider._release = lambda: {"id": 1, "draft": True}
            provider._remote_journal = lambda: {"observations": {"github": {}, "nuget": {}}}
            with patch.object(provider, "_public_install", return_value=False):
                self.assertEqual(provider.dispatch(self.ordered[-1]).state, "unknown")
                self.assertEqual(api.writes, [])
            with patch.object(provider, "_public_install", return_value=True):
                self.assertEqual(provider.dispatch(self.ordered[-1]).state, "applied")
                self.assertEqual(len(api.writes), 1)

    def test_public_install_uses_isolated_public_only_nuget_state(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            path = root / "manifest.json"
            path.write_text(json.dumps(manifest()))
            provider = WizardProvider(object(), path, "github-token", "nuget-key")
            calls = []
            def run(command, **kwargs):
                calls.append((command, kwargs))
                if command[:3] == ["dotnet", "tool", "install"]:
                    tool = pathlib.Path(command[command.index("--tool-path") + 1]) / "new-sdd-workspace"
                    tool.parent.mkdir(parents=True)
                    tool.write_text("fixture")
                    config = pathlib.Path(command[command.index("--configfile") + 1]).read_text()
                    self.assertIn("<clear/>", config)
                    self.assertIn("https://api.nuget.org/v3/index.json", config)
                return type("Result", (), {"returncode": 0, "stdout": "new-sdd-workspace", "stderr": ""})()
            with patch("new_sdd_workspace_successor_provider.subprocess.run", side_effect=run):
                self.assertTrue(provider._public_install())
            self.assertEqual(len(calls), 2)
            install, kwargs = calls[0]
            self.assertEqual(install[:3], ["dotnet", "tool", "install"])
            for name in ("NUGET_PACKAGES", "NUGET_HTTP_CACHE_PATH", "DOTNET_CLI_HOME"):
                self.assertTrue(kwargs["env"][name].startswith(install[install.index("--tool-path") + 1].rsplit("/tool", 1)[0]))

    def test_live_tag_draft_and_original_asset_readback(self):
        class API:
            tag = None
            release = None
            assets = None
            def __init__(self):
                self.assets = {}
                self.writes = []
            def get(self, path):
                if path.endswith("/git/ref/tags/new-sdd-workspace/v0.18.0") and self.tag:
                    return {"object": {"sha": self.tag}}
                if path.endswith("/releases/tags/new-sdd-workspace/v0.18.0") and self.release and not self.release["draft"]:
                    return self.release
                if path.endswith("/releases?per_page=100&page=1"):
                    return [self.release] if self.release else []
                if path.endswith("/releases/1/assets?per_page=100"):
                    return [{"id": i, "name": name} for i, name in enumerate(self.assets, 1)]
                raise NotFound(path)
            def post(self, path, body):
                self.writes.append((path, body))
                if path.endswith("/git/refs"):
                    self.tag = body["sha"]; return {"object": {"sha": self.tag}}
                if path.endswith("/releases"):
                    self.release = {"id": 1, "tag_name": body["tag_name"],
                                    "body": body["body"], "draft": body["draft"]}
                    return self.release
                raise AssertionError(path)
            def upload_asset(self, release_id, name, path):
                self.assets[name] = path.read_bytes()
                return {"id": len(self.assets), "name": name}
            def download_asset(self, asset_id):
                return list(self.assets.values())[asset_id - 1]
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            row = manifest()
            package = root / "FS.GG.NewSddWorkspace.0.18.0.nupkg"
            package.write_bytes(b"exact original Wizard archive")
            row["archiveSha256"] = hashlib.sha256(package.read_bytes()).hexdigest()
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            api = API()
            provider = WizardProvider(api, manifest_path, "github-token", "nuget-key")
            _, ordered = effects(row)
            tag, draft, archive = ordered[0], ordered[1], ordered[4]
            self.assertEqual(provider.observe(tag).state, "absent")
            self.assertEqual(provider.dispatch(tag).state, "applied")
            self.assertEqual(provider.observe(tag).state, "matched")
            self.assertEqual(provider.dispatch(draft).state, "applied")
            self.assertEqual(provider.observe(draft).state, "matched")
            self.assertEqual(provider.dispatch(archive).state, "applied")
            self.assertEqual(provider.observe(archive).state, "matched")
            self.assertEqual(provider.dispatch(ordered[5]).state, "applied")
            self.assertEqual(provider.observe(ordered[5]).state, "matched")
            api.assets[package.name] = b"different archive"
            self.assertEqual(provider.observe(archive).state, "mismatched")
            api.tag = "e" * 40
            self.assertEqual(provider.observe(tag).state, "mismatched")

    def test_publication_journal_requires_both_exact_feed_readbacks(self):
        class API:
            def __init__(self, raw):
                self.raw = raw
            def get(self, path):
                if path.endswith("/releases/tags/new-sdd-workspace/v0.18.0"):
                    raise NotFound(path)
                if path.endswith("/releases?per_page=100&page=1"):
                    return [{"id": 1, "tag_name": "new-sdd-workspace/v0.18.0", "draft": True}]
                if path.endswith("/releases/1/assets?per_page=100"):
                    return [{"id": 1, "name": "publication-journal.json"}]
                raise AssertionError(path)
            def download_asset(self, asset_id):
                return self.raw
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            row = manifest()
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            raw_package = root / "readback.nupkg"
            raw_package.write_bytes(b"feed package")
            sha = hashlib.sha256(raw_package.read_bytes()).hexdigest()
            content_id, ordered = effects(row)
            journal = {"schema": "fsgg.new-sdd-workspace-release-journal/v1", "manifestSha256": content_id,
                       "observations": {feed: {"archiveSha256": sha,
                                               "payloadSha256": row["producerPayloadSha256"],
                                               "producerPayloadEqual": True} for feed in ("github", "nuget")}}
            api = API(json.dumps(journal).encode())
            provider = WizardProvider(api, manifest_path, "github-token", "nuget-key")
            with patch.object(provider, "_download_package", return_value=raw_package):
                self.assertEqual(provider.observe(ordered[6]).state, "matched")
                journal["observations"]["nuget"]["archiveSha256"] = "0" * 64
                api.raw = json.dumps(journal).encode()
                with self.assertRaisesRegex(Exception, "journal and public feed differ"):
                    provider.observe(ordered[6])

    def test_live_admission_revokes_on_changed_main_or_actor(self):
        class API:
            main = "d" * 40
            actor = "EHotwagner"
            def get(self, path):
                if path.endswith("/.github"):
                    return {"id": 1269292704, "full_name": "FS-GG/.github"}
                if "/actions/runs/123" in path:
                    return {"repository": {"id": 1269292704},
                            "path": ".github/workflows/release-new-sdd-workspace.yml",
                            "event": "workflow_dispatch", "head_branch": "main", "head_sha": "d" * 40,
                            "run_attempt": 1, "actor": {"login": self.actor}, "status": "in_progress"}
                if path.endswith("/git/ref/heads/main"):
                    return {"object": {"sha": self.main}}
                raise AssertionError(path)
        api = API()
        admission = WizardAdmission(api, manifest(), "d" * 40, 123, "EHotwagner", "refs/heads/main")
        effect = self.ordered[0]
        self.assertTrue(admission.authorize(self.content_id, effect.identity, "intent", effect.request_digest))
        api.main = "e" * 40
        self.assertFalse(admission.authorize(self.content_id, effect.identity, "dispatch", effect.request_digest))
        api.main = "d" * 40; api.actor = "someone-else"
        self.assertFalse(admission.authorize(self.content_id, effect.identity, "settle", effect.request_digest))

    def diagnostic_fixture(self, root):
        path = root / "manifest.json"
        path.write_text(json.dumps(manifest()))
        class API:
            def __init__(self): self.writes = []
            def patch(self, route, body): self.writes.append((route, body))
        api = API()
        provider = WizardProvider(api, path, "SECRET-GH-VALUE", "SECRET-NUGET-VALUE")
        provider._release = lambda: {"id": 1, "draft": True}
        provider._remote_journal = lambda: {"observations": {"github": {}, "nuget": {}}}
        return provider, api

    def test_output_projection_withholds_credentials_urls_queries_and_private_paths(self):
        raw = "SECRET-GH-VALUE https://user:SECRET-NUGET-VALUE@example.invalid/private?q=SECRET-GH-VALUE /home/private/key NU3028 certificate hostfxr"
        projected = output_signals(raw, ("SECRET-GH-VALUE", "SECRET-NUGET-VALUE"))
        rendered = json.dumps(projected)
        for value in ("SECRET-GH-VALUE", "SECRET-NUGET-VALUE", "https://", "/home/private", "?q=", "example.invalid"):
            self.assertNotIn(value, rendered)
        self.assertEqual(projected["codes"], ["NU3028"])
        self.assertEqual(projected["signals"], ["certificate", "hostfxr"])
        self.assertEqual(output_signals("NU1301", ("NU1301",))["codes"], [])
        large = output_signals("x" * 9000 + "NU1301")
        self.assertEqual(large["characters"], 9006)
        self.assertEqual(large["scannedCharacters"], 8192)
        self.assertEqual(large["codes"], [])

    def test_public_install_failure_missing_apphost_and_help_diagnostics_preserve_refusal(self):
        for outcome in ("install", "apphost", "help", "help-name"):
            with tempfile.TemporaryDirectory() as temporary:
                provider, api = self.diagnostic_fixture(pathlib.Path(temporary))
                calls = []
                def run(command, **kwargs):
                    calls.append(command)
                    if len(calls) == 1:
                        if outcome not in ("install", "apphost"):
                            tool = pathlib.Path(command[command.index("--tool-path") + 1]) / "new-sdd-workspace"
                            tool.parent.mkdir(parents=True); tool.write_text("PURE FIXTURE")
                        return subprocess.CompletedProcess(command, 17 if outcome == "install" else 0,
                            "SECRET-GH-VALUE /private/path", "NU3028 signature SECRET-NUGET-VALUE")
                    return subprocess.CompletedProcess(command, 33 if outcome == "help" else 0,
                        "unrelated", "framework hostfxr /private/path?credential=SECRET-GH-VALUE")
                output = io.StringIO()
                with patch("new_sdd_workspace_successor_provider.subprocess.run", side_effect=run), contextlib.redirect_stdout(output):
                    self.assertFalse(provider._public_install())
                records = [json.loads(line.split(": ", 1)[1]) for line in output.getvalue().splitlines()]
                if outcome == "install": self.assertEqual((records[-1]["stage"], records[-1]["actualExitCode"]), ("install", 17))
                elif outcome == "apphost": self.assertEqual(records[-1]["reason"], "missing-apphost")
                elif outcome == "help": self.assertEqual((records[-1]["stage"], records[-1]["actualExitCode"]), ("help", 33))
                else: self.assertEqual(records[-1]["reason"], "help-name-absent")
                self.assertEqual(api.writes, [])
                for value in ("SECRET-GH-VALUE", "SECRET-NUGET-VALUE", "/private/path", "?credential"):
                    self.assertNotIn(value, output.getvalue())

    def test_timeout_diagnostic_rethrows_with_safe_bounded_projection(self):
        with tempfile.TemporaryDirectory() as temporary:
            provider, _ = self.diagnostic_fixture(pathlib.Path(temporary));output = io.StringIO()
            error = subprocess.TimeoutExpired(["SECRET-GH-VALUE"], 180,
                output=b"NU1301 /private/path SECRET-NUGET-VALUE", stderr=b"certificate https://private/?key=SECRET-GH-VALUE")
            with patch("new_sdd_workspace_successor_provider.subprocess.run", side_effect=error), contextlib.redirect_stdout(output):
                with self.assertRaises(subprocess.TimeoutExpired): provider._public_install()
            row = json.loads(output.getvalue().split(": ", 1)[1])
            self.assertEqual(row["exceptionKind"], "TimeoutExpired")
            self.assertIsNone(row["actualExitCode"])
            self.assertNotIn("SECRET", output.getvalue());self.assertNotIn("/private", output.getvalue())

    def test_promote_stage_identifies_unknown_boundary_without_retry_or_patch(self):
        for stage in ("promote-release", "promote-journal", "promote-install", "promote-patch"):
            with tempfile.TemporaryDirectory() as temporary:
                provider, api = self.diagnostic_fixture(pathlib.Path(temporary));output = io.StringIO()
                provider._public_install = lambda: True
                if stage == "promote-release": provider._release = lambda: None
                elif stage == "promote-journal": provider._remote_journal = lambda: None
                elif stage == "promote-install": provider._public_install = lambda: False
                else:
                    def fail(*args): raise OSError("SECRET-GH-VALUE https://private/?key=SECRET-NUGET-VALUE")
                    api.patch = fail
                with contextlib.redirect_stdout(output):self.assertEqual(provider.dispatch(self.ordered[-1]).state, "unknown")
                row = json.loads(output.getvalue().split(": ", 1)[1]);self.assertEqual(row["stage"], stage)
                self.assertEqual(api.writes, []);self.assertNotIn("SECRET", output.getvalue());self.assertNotIn("https", output.getvalue())

    def test_diagnostic_emission_cap_and_publisher_catch_never_print_argv(self):
        with tempfile.TemporaryDirectory() as temporary:
            provider, _ = self.diagnostic_fixture(pathlib.Path(temporary));output = io.StringIO()
            with contextlib.redirect_stdout(output):
                for _ in range(DIAGNOSTIC_LIMIT + 20):provider._diagnostic("promote", "promote-patch", error=OSError("secret"))
            self.assertEqual(len(output.getvalue().splitlines()), DIAGNOSTIC_LIMIT)
            self.assertLess(max(len(line) for line in output.getvalue().splitlines()), 4096)
        error = subprocess.CalledProcessError(17, ["dotnet", "--api-key", "SECRET-GH-VALUE"], output="/private/path", stderr="SECRET-NUGET-VALUE")
        row = publisher_error(error);self.assertEqual(row["actualExitCode"], 17)
        self.assertNotIn("SECRET", json.dumps(row));self.assertNotIn("private", json.dumps(row))
        self.assertEqual(publisher_error(Refused("Wizard publication exceeded bounded reconciliation steps"))["detail"], "Wizard publication exceeded bounded reconciliation steps")

    def test_safe_http_cause_and_dispatch_exit_code_do_not_echo_requests(self):
        with tempfile.TemporaryDirectory() as temporary:
            provider, _ = self.diagnostic_fixture(pathlib.Path(temporary));output = io.StringIO()
            cause = urllib.error.HTTPError("https://private/?key=SECRET-GH-VALUE", 403, "SECRET-NUGET-VALUE", {}, None)
            error = RuntimeError("SECRET-GH-VALUE /private/path");error.__cause__ = cause
            with contextlib.redirect_stdout(output):provider._diagnostic("promote", "promote-patch", error=error)
            row = json.loads(output.getvalue().split(": ", 1)[1]);self.assertEqual(row["httpStatus"], 403);self.assertEqual(row["causeKind"], "URLError")
            self.assertNotIn("SECRET", output.getvalue());self.assertNotIn("https", output.getvalue())
            cause.close()
            output = io.StringIO()
            with contextlib.redirect_stdout(output):provider._diagnostic("nuget", "dispatch", error=subprocess.CalledProcessError(17, ["--api-key", "SECRET-NUGET-VALUE"]))
            self.assertEqual(json.loads(output.getvalue().split(": ", 1)[1])["actualExitCode"], 17)
            self.assertNotIn("SECRET", output.getvalue())

    def test_existing_open_promote_readback_logs_draft_without_install_or_write(self):
        with tempfile.TemporaryDirectory() as temporary:
            provider, api = self.diagnostic_fixture(pathlib.Path(temporary));output = io.StringIO()
            with patch.object(provider, "_public_install", side_effect=AssertionError("install forbidden during draft readback")), contextlib.redirect_stdout(output):
                self.assertEqual(provider.observe(self.ordered[-1]).state, "absent")
            row = json.loads(output.getvalue().split(": ", 1)[1]);self.assertEqual(row["reason"], "draft-observed");self.assertEqual(api.writes, [])



def creator_caller():
    spec = importlib.util.spec_from_file_location("tested_creator018_publisher", ROOT / "scripts/new-sdd-workspace-successor-publish.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def creator_fixture_sources():
    """Reuse the actual existing Git adapter without executing its top-level suite."""
    import base64
    import os
    from release_successor_journal import REPOSITORY
    path = ROOT / "tests/release-successor-journal/run.py"
    tree = ast.parse(path.read_text(), filename=str(path))
    selected = next(node for node in tree.body if isinstance(node, ast.ClassDef) and node.name == "RealGit")
    scope = {"base64": base64, "json": json, "os": os, "pathlib": pathlib,
             "subprocess": subprocess, "tempfile": tempfile, "REPOSITORY": REPOSITORY}
    exec(compile(ast.Module(body=[selected], type_ignores=[]), str(path), "exec"), scope)
    spec = importlib.util.spec_from_file_location("creator_shared_policy_fixture", ROOT / "tests/release-successor-live/run.py")
    fixture = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture)
    return scope["RealGit"], fixture


class CreatorPublicationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.caller = creator_caller()
        cls.primitives = cls.caller.publication_primitives()
        cls.RealGit, cls.shared = creator_fixture_sources()

    def setUp(self):
        self.content, self.ordered = effects(manifest())
        self.intent = {"contentId": self.content, "sourceSha": "a" * 40, "version": "0.18.0",
                       "candidateArchiveSha256": "d" * 64, "operator": "EHotwagner"}
        self.report = self.caller.publication_report(1, 2, "d" * 64, "publish")

    def fixture(self, root):
        import base64
        import copy
        from release_successor_journal import REPOSITORY as authority
        real = self.RealGit(root / "authority.git")
        # Preserve complete parent directories before selecting fresh absence.
        parent = real.get(f"repos/{authority}/git/ref/heads/main")["object"]["sha"]
        commit = real.get(f"repos/{authority}/git/commits/{parent}")
        blob = real.post(f"repos/{authority}/git/blobs", {"content": base64.b64encode(b"parent marker\n").decode()})
        tree = real.post(f"repos/{authority}/git/trees", {"base_tree": commit["tree"]["sha"], "tree": [
            {"path": "state/releases/.parent", "mode": "100644", "type": "blob", "sha": blob["sha"]}]})
        commit = real.post(f"repos/{authority}/git/commits", {"tree": tree["sha"], "parents": [parent], "message": "Fixture parents"})
        real.patch(f"repos/{authority}/git/refs/heads/main", {"sha": commit["sha"], "force": False})
        policy = self.shared.AuthorityAPI()
        class Transport:
            def __init__(self):
                self.calls = []; self.writes = []; self.overrides = {}; self.lose_patch = False
                self.quota_remaining = 5000
            def get(self, path):
                self.calls.append(("get", path))
                if path in self.overrides:
                    value = self.overrides[path]
                    if isinstance(value, Exception): raise value
                    return copy.deepcopy(value)
                if path == "rate_limit":
                    return {"resources": {"core": {"limit": 5000, "remaining": self.quota_remaining,
                        "used": 5000-self.quota_remaining, "reset": 4102444800}}}
                if "/rulesets/" in path or "/rules/branches/" in path:
                    return policy.get(path)
                if "/git/ref/heads/fsgg/" in path:
                    raise NotFound(path)
                value = real.get(path)
                if path.endswith("/git/ref/heads/main"):
                    value["object"]["type"] = "commit"
                return value
            def post(self, path, body):
                self.calls.append(("post", path)); self.writes.append(("post", path, body))
                return real.post(path, body)
            def patch(self, path, body):
                self.calls.append(("patch", path)); self.writes.append(("patch", path, body))
                value = real.patch(path, body)
                if self.lose_patch:
                    self.lose_patch = False
                    raise OSError("lost CAS response")
                return value
        native = Transport()
        counted = self.primitives.CountedAuthorityAPI(native)
        counted.admit_quota()
        journal = self.primitives.ReportingJournal(
            self.caller.ProtectedReleaseJournal(counted, self.caller.REF, main_directory=True), self.report)
        class PublisherAPI:
            source = "a" * 40
            def get(self, path):
                if path == "repos/FS-GG/.github":
                    return {"id": 1269292704, "full_name": "FS-GG/.github"}
                if "/actions/runs/123" in path:
                    return {"repository": {"id": 1269292704}, "path": ".github/workflows/release-new-sdd-workspace.yml",
                            "event": "workflow_dispatch", "head_branch": "main", "head_sha": "a"*40,
                            "run_attempt": 1, "actor": {"login": "EHotwagner"}, "status": "in_progress"}
                if path.endswith("/git/ref/heads/main"):
                    return {"object": {"sha": self.source}}
                if "/compare/" in path:
                    return {"status": "ahead"}
                raise AssertionError(path)
        api = PublisherAPI()
        admission = self.primitives.ProtectedPublisherAdmission(
            WizardAdmission(api, manifest(), "a"*40, 123, "EHotwagner", "refs/heads/main"), counted, self.caller.REF)
        provider = self.primitives.ReportingProvider(Provider(), self.report)
        return real, native, counted, journal, api, admission, provider, policy

    def prepare(self, counted, journal, api, admission, provider, preflight=False, source="a"*40):
        return self.caller.prepare_publication(self.primitives, journal, counted, admission, provider, api,
                    self.intent, self.ordered, source, preflight, self.report)

    def test_inert_checkout_import_and_legacy_default(self):
        with patch("subprocess.run", side_effect=AssertionError("import must be inert")):
            primitives = self.caller.publication_primitives()
            primitives.authority_protection()
        self.assertEqual(pathlib.Path(primitives.__file__), ROOT / "scripts/release-successor-publish.py")
        self.assertFalse(self.caller.ProtectedReleaseJournal(self.shared.AuthorityAPI()).main_directory)

    def test_actual_main_directory_all_eight_counted_cold_warm_and_sibling(self):
        with tempfile.TemporaryDirectory() as temporary:
            real, native, counted, journal, api, admission, provider, policy = self.fixture(pathlib.Path(temporary))
            self.assertFalse(self.prepare(counted, journal, api, admission, provider))
            cold = dict(counted.attempted)
            cold_calls = list(native.calls)
            journal.read(); warm = {key: counted.attempted[key]-cold[key] for key in cold}
            warm_calls = native.calls[len(cold_calls):]

            real.sibling("sibling-state.txt")
            self.caller.reconcile_publication(self.content, self.ordered, journal, admission, provider,
                                             self.report, clock=lambda: 0, sleep=lambda _: None)
            state = journal.read()
            self.assertEqual(state.generation, 17)
            self.assertEqual(state.effects, {effect.identity: "verified" for effect in self.ordered})
            self.assertEqual(provider.writes, [effect.identity for effect in self.ordered])
            self.assertEqual(real.command("show", "refs/heads/main:sibling-state.txt"), b"Sibling state\n")
            self.assertEqual(real.command("show", "refs/heads/main:state/releases/.parent"), b"parent marker\n")
            self.assertTrue(all(body["force"] is False for method, path, body in native.writes if method == "patch"))
            self.assertFalse(any("/git/refs" in path and method == "post" for method, path, body in native.writes))
            self.assertEqual(sum(counted.attempted.values()), len(native.calls))
            rules = [path for method, path in native.calls if "/rulesets/" in path]
            self.assertEqual(len(rules), 4 * (1 + 3 * len(self.ordered)))
            def categories(calls):
                result = {}
                for method, path in calls:
                    category = ("quota" if path == "rate_limit" else "protection" if "/rules" in path
                                else "history" if "/commits?" in path else "compare" if "/compare/" in path
                                else "refs" if "/git/ref" in path else "commits" if "/git/commits" in path
                                else "trees" if "/git/trees" in path else "blobs" if "/git/blobs" in path else "repository")
                    key = method + ":" + category; result[key] = result.get(key, 0) + 1
                return result
            total = sum(counted.attempted.values())
            self.assertLess(total, 4200)
            print("Creator018 actual all-eight Authority profile: " + json.dumps(
                {"coldPreparationByMethod": cold, "warmReadByMethod": warm,
                 "coldPreparationCategories": categories(cold_calls), "warmReadCategories": categories(warm_calls),
                 "categories": categories(native.calls),
                 "total": total, "ceiling": 4200, "margin": 4200-total}, sort_keys=True))

    def test_preflight_has_zero_journal_and_provider_writes(self):
        with tempfile.TemporaryDirectory() as temporary:
            _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
            self.assertTrue(self.prepare(counted, journal, api, admission, provider, preflight=True))
            self.assertEqual(native.writes, []); self.assertEqual(provider.writes, [])
            self.assertTrue(self.report["preflightPassed"]); self.assertFalse(self.report["complete"])

    def test_existing_directory_with_legacy_404_validates_original_intent(self):
        with tempfile.TemporaryDirectory() as temporary:
            _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
            self.prepare(counted, journal, api, admission, provider)
            writes = len(native.writes)
            self.assertFalse(self.prepare(counted, journal, api, admission, provider, source="e"*40))
            self.assertFalse(self.report["fresh"])
            with self.assertRaisesRegex(Refused, "preflight requires"):
                self.prepare(counted, journal, api, admission, provider, preflight=True)
            self.intent["candidateArchiveSha256"] = "f"*64
            with self.assertRaisesRegex(JournalRefused, "binds a different candidate"):
                self.prepare(counted, journal, api, admission, provider)
            self.assertEqual(len(native.writes), writes); self.assertEqual(provider.writes, [])

    def test_source_mismatch_prevents_genesis_and_effects(self):
        with tempfile.TemporaryDirectory() as temporary:
            _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
            with self.assertRaisesRegex(Refused, "exact current main"):
                self.prepare(counted, journal, api, admission, provider, source="e"*40)
            self.assertEqual(native.writes, []); self.assertEqual(provider.writes, [])

    def test_physical_classifier_refuses_incomplete_or_ambiguous_population(self):
        from release_successor_journal import REPOSITORY as authority
        for defect in ("parent", "nested404", "truncated", "ambiguous", "repository", "legacy", "moved", "malformed"):
            with self.subTest(defect=defect):
                native = self.shared.AuthorityAPI()
                original = native.get
                def read(path):
                    if defect == "legacy" and path.endswith(self.caller.REF.removeprefix("refs/")):
                        return {"object": {"sha": "e"*40}}
                    if path.endswith(self.caller.REF.removeprefix("refs/")):
                        raise NotFound(path)
                    if defect == "nested404" and path.endswith("/git/trees/"+native.state): raise NotFound(path)
                    value = original(path)
                    if defect == "repository" and path == f"repos/{authority}": value["id"] = 1
                    if "/git/trees/" in path:
                        if defect == "parent" and path.endswith(native.root): value["tree"] = []
                        if defect == "truncated": value["truncated"] = True
                        if defect == "ambiguous" and value["tree"]: value["tree"] *= 2
                        if defect == "malformed": value["sha"] = "x"
                    return value
                if defect == "moved": native.change_main_at = 2
                native.get = read
                with self.assertRaises((Refused, NotFound)):
                    self.primitives.classify_journal_destination(native, self.caller.REF)

    def test_protection_rechecked_and_drift_blocks_actual_execution(self):
        for defect in ("actor", "enforcement", "version", "exclusion", "origin", "source"):
            with self.subTest(defect=defect), tempfile.TemporaryDirectory() as temporary:
                _, native, counted, journal, api, admission, provider, policy = self.fixture(pathlib.Path(temporary))
                self.prepare(counted, journal, api, admission, provider)
                if defect == "source": api.source = "e"*40
                elif defect == "origin":
                    route = "repos/FS-GG/FS.GG.Coordination.Authority/rules/branches/main"
                    rows = policy.get(route)
                    for row in rows: row["ruleset_source"] = "other/repository"
                    native.overrides[route] = rows
                elif defect == "actor": policy.controls["mainWriter"]["bypass_actors"] = []
                elif defect == "enforcement": policy.controls["mainWriter"]["enforcement"] = "disabled"
                elif defect == "version": policy.controls["mainWriter"]["updated_at"] = "2000-01-01T00:00:00Z"
                else: policy.controls["mainWriter"]["conditions"]["ref_name"]["exclude"] = ["refs/heads/main"]
                with self.assertRaises(Refused):
                    advance_effects(self.content, self.ordered, journal, admission, provider)
                self.assertEqual(provider.writes, [])
        with tempfile.TemporaryDirectory() as temporary:
            _, _, counted, journal, api, admission, provider, policy = self.fixture(pathlib.Path(temporary))
            policy.omit_actors = True
            self.assertTrue(self.prepare(counted, journal, api, admission, provider, preflight=True))

    def test_lost_cas_response_is_observed_under_original_identity_without_replay(self):
        with tempfile.TemporaryDirectory() as temporary:
            _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
            self.prepare(counted, journal, api, admission, provider)
            native.lose_patch = True
            with self.assertRaisesRegex(JournalRefused, "CAS response uncertain"):
                advance_effects(self.content, self.ordered, journal, admission, provider)
            self.assertEqual(journal.read().effects, {"tag": "intent"})
            for _ in range(2):
                self.assertEqual(advance_effects(self.content, self.ordered, journal, admission, provider), "waiting")
            self.assertEqual(provider.writes, [])

    def test_delayed_last_effect_exhausts_bound_before_send_and_preserves_intent(self):
        for bound in ("reconciliation", "authority"):
            with self.subTest(bound=bound), tempfile.TemporaryDirectory() as temporary:
                _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
                self.prepare(counted, journal, api, admission, provider)
                provider.delayed.add("promote")
                if bound == "authority":
                    original_dispatch = provider.dispatch
                    def dispatch(effect):
                        outcome = original_dispatch(effect)
                        if effect.identity == "promote":
                            # Exhaust the fixture budget at the actual last send;
                            # the next fresh journal read must refuse before transport.
                            counted.ceiling = sum(counted.attempted.values())
                        return outcome
                    provider.dispatch = dispatch
                reason = "ceiling exhausted" if bound == "authority" else "bounded reconciliation steps"
                with self.assertRaisesRegex(Refused, reason):
                    self.caller.reconcile_publication(self.content, self.ordered, journal, admission, provider,
                                                     self.report, clock=lambda: 0, sleep=lambda _: None)
                total = sum(counted.attempted.values())
                self.assertEqual(len(native.calls), total)
                if bound == "authority":
                    self.assertEqual(total, counted.ceiling)
                    self.assertGreater(total, 0)
                    self.assertLess(total, 4200)
                else:
                    self.assertEqual(counted.ceiling, 4200)
                    self.assertLess(total, counted.ceiling)
                    self.assertEqual(self.report["iterationsAttempted"], len(self.ordered) * 2 + 120)
                self.assertEqual(provider.writes.count("promote"), 1)
                # Read through the original transport for readback; never reset the invocation counter.
                reader = self.caller.ProtectedReleaseJournal(native, self.caller.REF, main_directory=True)
                state = reader.read()
                self.assertEqual(state.generation, 16); self.assertEqual(state.effects["promote"], "intent")
                self.assertFalse(self.report["complete"])

    def test_original_deadline_and_iteration_cap_do_not_allow_another_send(self):
        journal = Journal(self.content)
        provider = Provider()
        ticks = iter((0, 2700))
        with self.assertRaisesRegex(Refused, "deadline expired"):
            self.caller.reconcile_publication(self.content, self.ordered, journal, Admission(), provider,
                                             self.report, clock=lambda: next(ticks), sleep=lambda _: None)
        self.assertEqual(provider.writes, []); self.assertEqual(self.report["iterationsAttempted"], 0)
        journal.state = JournalState(2, self.content, {"tag": "intent"})
        with self.assertRaisesRegex(Refused, "bounded reconciliation steps"):
            self.caller.reconcile_publication(self.content, self.ordered, journal, Admission(), provider,
                                             self.report, clock=lambda: 0, sleep=lambda _: None)
        self.assertEqual(self.report["iterationsAttempted"], 136)
        self.assertEqual(provider.writes, []); self.assertEqual(journal.read().effects, {"tag": "intent"})
        self.assertFalse(self.report["complete"])

    def test_low_and_malformed_quota_refuse_before_journal_construction(self):
        for core in (None, {"limit": 5000, "remaining": 4499, "used": 501, "reset": 4102444800},
                     {"limit": 5000, "remaining": True, "used": 4999, "reset": 4102444800}):
            class API:
                writes = []
                def get(self, path):
                    self.assert_path = path
                    return {"resources": {"core": core}}
            api = API(); counted = self.primitives.CountedAuthorityAPI(api)
            with self.assertRaises(Refused): counted.admit_quota()
            self.assertEqual(counted.attempted, {"get": 1, "post": 0, "patch": 0})
            self.assertEqual(api.writes, [])

    def test_summary_fixed_fields_success_partial_failure_and_exclusive_retention(self):
        for mode in ("preflight", "publish"):
            with tempfile.TemporaryDirectory() as temporary:
                self.report["mode"] = mode
                self.report["complete"] = mode == "publish"
                self.report["preflightPassed"] = mode == "preflight"
                self.report["firstCause"] = self.caller.safe_cause(OSError("SECRET https://signed/?token=x /private/path"))
                self.report["candidateSourceSha"] = "/private/SECRET"
                self.report["lastObservedJournal"] = {"generation": 2, "logicalHead": "a"*40,
                    "physicalHead": "b"*40, "effects": {"tag": "intent", "SECRET": "/private"}}
                path = pathlib.Path(temporary) / "summary.json"
                self.caller.retain_summary(path, self.report, None)
                raw = path.read_text(); row = json.loads(raw)
                self.assertLess(len(raw.encode()), 65536)
                self.assertNotIn("SECRET", raw); self.assertNotIn("/private", raw); self.assertNotIn("https", raw)
                self.assertEqual(row["firstCause"], {"kind": "OSError", "exitCode": None})
                self.assertEqual(row["lastObservedJournal"]["effects"], {"tag": "intent"})
                with self.assertRaises(FileExistsError): self.caller.retain_summary(path, self.report, None)

    def test_actual_entry_preflight_and_failure_reporting_preserve_first_cause(self):
        import os
        for failure in (None, "first", "report", "both"):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary)
                _, native, _, _, publisher_api, _, _, _ = self.fixture(root)
                publisher_api_get = publisher_api.get
                def get(path):
                    if path.endswith("/actions/artifacts/2"):
                        return {"id": 2, "digest": "sha256:"+"d"*64}
                    if path.endswith("/actions/runs/1"):
                        return {"id": 1, "head_sha": "a"*40}
                    return publisher_api_get(path)
                publisher_api.get = get
                def subprocess_fixture(command, **kwargs):
                    if command[0] == "gh": return subprocess.CompletedProcess(command, 0)
                    output = pathlib.Path(command[command.index("--output")+1]); output.mkdir()
                    (output/"manifest.json").write_text(json.dumps(manifest()))
                    (output/"package-evidence.json").write_text("{}")
                    return subprocess.CompletedProcess(command, 0)
                argv = ["creator", "--preflight-only", "--candidate-run-id", "1", "--candidate-artifact-id", "2",
                        "--candidate-archive-sha256", "d"*64, "--workdir", str(root/"candidate-work")]
                env = {"GITHUB_SHA": "a"*40, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "123",
                       "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "FS-GG/.github",
                       "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1", "GH_TOKEN": "SECRET-GH",
                       "ORDINARY_LEDGER_TOKEN": "SECRET-LEDGER", "NUGET_API_KEY": "SECRET-NUGET"}
                output = io.StringIO()
                original = self.caller.retain_summary
                def retain(path, report, counted):
                    if failure in ("report", "both"): raise OSError("SECRET report /private")
                    return original(path, report, counted)
                def api(token): return native if token == "SECRET-LEDGER" else publisher_api
                if failure in ("first", "both"): native.quota_remaining = 1
                real_run_path = runpy.run_path
                def injected_run_path(path, *args, **kwargs):
                    if str(path).endswith("creator-current-coord-dependencies.py"):
                        return {"load_pin": lambda: {"explicitSyntheticUnitFixture": True},
                                "protected_pin": lambda source: {"explicitSyntheticUnitFixture": True},
                                "read_json": lambda body: {}, "verify_evidence": lambda *args: None}
                    return real_run_path(path, *args, **kwargs)
                with patch.object(sys, "argv", argv), patch.dict(os.environ, env, clear=True), \
                        patch.object(self.caller, "GitHubAPI", side_effect=api), \
                        patch("runpy.run_path", side_effect=injected_run_path), \
                        patch.object(self.caller.subprocess, "run", side_effect=subprocess_fixture), \
                        patch.object(self.caller, "WizardProvider", return_value=Provider()), \
                        patch.object(self.caller, "retain_summary", side_effect=retain), contextlib.redirect_stderr(output):
                    # Destination fake uses actual preflight reads but no Git subprocess under this mock.
                    static = self.shared.AuthorityAPI()
                    original_get = native.get
                    def read(path):
                        if path == "rate_limit": return original_get(path)
                        if path.endswith(self.caller.REF.removeprefix("refs/")): raise NotFound(path)
                        return static.get(path)
                    native.get = read
                    actual = self.caller.main()
                self.assertEqual(actual, 0 if failure is None else 1)
                summary = root/"creator018-publication-summary.json"
                if failure not in ("report", "both"):
                    row = json.loads(summary.read_text())
                    self.assertEqual(row["preflightPassed"], failure is None)
                    self.assertFalse(row["complete"])
                if failure == "both":
                    self.assertIn('"code": "authority-quota-floor"', output.getvalue())
                    self.assertIn('"reportingFailure": {"exitCode": null, "kind": "OSError"}', output.getvalue())
                self.assertNotIn("SECRET", output.getvalue()); self.assertNotIn("/private", output.getvalue())
                self.assertEqual(native.writes, [])

    def test_workflow_summary_exact_path_and_historical_job_isolation(self):
        workflow = (ROOT / ".github/workflows/release-new-sdd-workspace.yml").read_text()
        ordinary, historical = workflow.split("  recovery-diagnostic:", 1)
        self.assertIn("name: creator018-publication-summary-${{ github.run_id }}", ordinary)
        self.assertIn("path: ${{ runner.temp }}/creator018-publication-summary.json", ordinary)
        self.assertIn("if-no-files-found: error", ordinary)
        self.assertNotIn("creator018-publication-summary", historical)
        self.assertIn("timeout-minutes: 60", ordinary)
        self.assertIn("cancel-in-progress: false", workflow)
        self.assertIn("promotion_recovery == 'off'", ordinary)


def load_tests(loader, tests, pattern):
    # The existing native selftest route also exercises the actual recovery caller.
    spec = importlib.util.spec_from_file_location("wizard_recovery_controls", pathlib.Path(__file__).with_name("recovery.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    tests.addTests(loader.loadTestsFromModule(module))
    frozen_spec = importlib.util.spec_from_file_location("wizard_frozen_dependency_controls", pathlib.Path(__file__).with_name("frozen_dependencies.py"))
    frozen_module = importlib.util.module_from_spec(frozen_spec)
    frozen_spec.loader.exec_module(frozen_module)
    tests.addTests(loader.loadTestsFromModule(frozen_module))
    readback_spec = importlib.util.spec_from_file_location("wizard_readback_controls", pathlib.Path(__file__).with_name("readback.py"))
    readback_module = importlib.util.module_from_spec(readback_spec)
    readback_spec.loader.exec_module(readback_module)
    tests.addTests(loader.loadTestsFromModule(readback_module))
    current_spec = importlib.util.spec_from_file_location("current_dependency_controls", pathlib.Path(__file__).with_name("current_dependencies.py"))
    current_module = importlib.util.module_from_spec(current_spec)
    current_spec.loader.exec_module(current_module)
    tests.addTests(loader.loadTestsFromModule(current_module))
    return tests


if __name__ == "__main__":
    if "--board-package" in sys.argv:
        import argparse
        parser = argparse.ArgumentParser()
        parser.add_argument("--board-package", type=pathlib.Path, required=True)
        parser.add_argument("--source-sha", required=True)
        parser.add_argument("--release-manifest", type=pathlib.Path)
        parser.add_argument("--build-artifacts", type=pathlib.Path)
        parser.add_argument("--frozen-coord-dependencies", type=pathlib.Path)
        parser.add_argument("--frozen-coord-source", type=pathlib.Path)
        parser.add_argument("--published-coord-source", type=pathlib.Path)
        parser.add_argument("--published-coord-dependencies", type=pathlib.Path)
        parser.add_argument("--creator-source", type=pathlib.Path)
        args = parser.parse_args()
        if args.published_coord_source is not None or args.published_coord_dependencies is not None:
            if args.creator_source is None or args.published_coord_source is None or args.published_coord_dependencies is None or args.build_artifacts is not None or args.frozen_coord_source is not None or args.frozen_coord_dependencies is not None:
                parser.error('published mode requires isolated creator, producer and dependency roots only')
            current = runpy.run_path(str(ROOT / 'scripts/creator-current-coord-dependencies.py'))
            pin = current['protected_pin'](args.source_sha)
            current['old']['layout'](args.creator_source, args.published_coord_dependencies, pin, args.published_coord_source)
            # The normal own-build/package comparison also applies to this conventional staged layout.
            output = args.creator_source / 'scripts/NewSddWorkspace/bin/Release/net10.0'
            with zipfile.ZipFile(args.board_package) as archive:
                for path in output.rglob('*'):
                    if path.is_file() and 'publish' not in path.relative_to(output).parts and path.suffix in ('.dll', '.pdb', '.json', '.xml', '.so', '.dylib'):
                        name = path.relative_to(output).as_posix()
                        if archive.read(current['PREFIX'] + name) != path.read_bytes():
                            raise ValueError('published Creator build/package mismatch: ' + name)
            if not args.release_manifest:
                parser.error('published mode requires manifest')
            checker = runpy.run_path(str(ROOT / 'scripts/new-sdd-workspace-release.py'))
            checker['verify_artifact'](args.release_manifest, args.board_package)
            print(json.dumps(current['evidence'](args.board_package, args.source_sha, pin, fixtures_passed=True), sort_keys=True))
            raise SystemExit(0)
        if args.creator_source is not None:
            parser.error('creator source root is exclusive to published mode')
        closure = board_package_closure(args.board_package, args.source_sha,
                                        frozen_dependencies=args.frozen_coord_dependencies,
                                        frozen_source=args.frozen_coord_source,
                                        build_artifacts=args.build_artifacts)
        if args.release_manifest:
            checker = runpy.run_path(str(ROOT / "scripts/new-sdd-workspace-release.py"))
            evidence = checker["verify_artifact"](args.release_manifest, args.board_package)
            evidence["boardClosure"] = closure
        else:
            evidence = closure
        print(json.dumps(evidence, sort_keys=True))
    else:
        unittest.main()

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
from xml.etree import ElementTree
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_successor_execution import Dispatch, JournalState, Observation, Refused, advance_effects
from new_sdd_workspace_successor_admission import WizardAdmission
from new_sdd_workspace_successor_execution import effects, CURRENT_016, CURRENT_015, CURRENT_014, HISTORICAL_013, ReleaseBinding
from new_sdd_workspace_successor_provider import WizardProvider, NotFound, output_signals, publisher_error, DIAGNOSTIC_LIMIT


def manifest():
    return {
        "schema": "fsgg.new-sdd-workspace-release/1", "packageId": "FS.GG.NewSddWorkspace",
        "version": "0.16.0", "tag": "new-sdd-workspace/v0.16.0", "sourceSha": "a" * 40,
        "archiveSha256": "b" * 64, "producerPayloadSha256": "sha256:" + "c" * 64,
    }


def board_package_closure(package: pathlib.Path, source_sha: str, *, source_root: pathlib.Path = ROOT,
                          frozen_dependencies: pathlib.Path | None = None,
                          frozen_source: pathlib.Path | None = None):
    """Join the actual current package to its freshly built creator/dependency output."""
    spec = importlib.util.spec_from_file_location("wizard_package_identity", source_root / "scripts/new-sdd-workspace-release.py")
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    if checker.package_identity(package) != (CURRENT_016.package, CURRENT_016.version, source_sha):
        raise ValueError("current creator package/source identity differs")
    dependency_pin = json.loads((source_root / "scripts/creator-frozen-coord-dependencies.json").read_text())
    coherent_version = dependency_pin["version"]
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
            path = path.resolve()
            if path in projects:
                return
            xml = ElementTree.parse(path)
            projects[path] = next((e.text for e in xml.iter("AssemblyName") if e.text), path.stem)
            for reference in xml.iter("ProjectReference"):
                condition = reference.get("Condition")
                if condition == "'$(FsggFrozenCoordDependencies)' != '' And '$(FsggFrozenCoordSource)' != ''":
                    continue
                if condition not in (None, "'$(FsggFrozenCoordDependencies)' == '' And '$(FsggFrozenCoordSource)' == ''"):
                    raise ValueError("ambiguous ordinary project reference")
                visit(path.parent / reference.attrib["Include"])
        visit(project)
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
                    target.get("new-sdd-workspace/0.16.0", {}).get("dependencies", {}).get("FS.GG.Coord.Cli") != coherent_version
                    or "fsgg-coord-engine.dll" not in target.get(cli, {}).get("runtime", {})
                    for target in targets.values())):
            raise ValueError("creator package must carry current coherent CLI dependency metadata")
    result = {"schema": "fsgg.creator-board-package-closure/1", "version": CURRENT_016.version,
            "sourceSha": source_sha, "currentCreatorSourceSha": source_sha, "coherentVersion": coherent_version, "projectCount": len(projects),
            "builtFilesCompared": len(required), "archiveSha256": hashlib.sha256(package.read_bytes()).hexdigest(),
            "installedAdoptionAccepted": False}
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
        for forged in (ReleaseBinding(CURRENT_016.package, CURRENT_016.version, CURRENT_016.tag),
                       ReleaseBinding(HISTORICAL_013.package, HISTORICAL_013.version, HISTORICAL_013.tag), None):
            with self.assertRaises(Refused):
                effects(manifest(), binding=forged)
        with self.assertRaises(AttributeError):
            CURRENT_016.version = "0.13.0"

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
            result = board_package_closure(package, creator_sha, source_root=creator,
                                           frozen_dependencies=dependencies, frozen_source=producer)
            self.assertEqual(result["projectCount"], 11)
            self.assertEqual(result["currentCreatorSourceSha"], creator_sha)
            self.assertEqual(result["publishedDependencySourceSha"], producer_sha)
            for selected in (None, creator):
                with self.assertRaises(ValueError):
                    board_package_closure(package, creator_sha, source_root=creator,
                                          frozen_dependencies=dependencies, frozen_source=selected)
            with self.assertRaisesRegex(ValueError, "package/source identity differs"):
                board_package_closure(package, "b" * 40, source_root=creator,
                                      frozen_dependencies=dependencies, frozen_source=producer)
            (creator / "new-owner-file").write_text("later Creator source")
            seal(creator)
            with self.assertRaisesRegex(ValueError, "current Creator source revision differs"):
                board_package_closure(package, creator_sha, source_root=creator,
                                      frozen_dependencies=dependencies, frozen_source=producer)

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
            deps = {'libraries': {'FS.GG.Coord.Cli/0.99.0': {'type': 'project'}}, 'targets': {
                '.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.99.0'}},
                                           'FS.GG.Coord.Cli/0.99.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}}
            members['new-sdd-workspace.deps.json'] = json.dumps(deps).encode()
            members['new-sdd-workspace.runtimeconfig.json'] = b'{}'
            for name, body in members.items():
                (output / name).write_bytes(body)
            # PackAsTool stages another copy under publish; package destinations omit that prefix.
            (output / 'publish').mkdir()
            for name, body in members.items():
                (output / 'publish' / name).write_bytes(body)
            nuspec = b'<package><metadata><id>FS.GG.NewSddWorkspace</id><version>0.16.0</version><repository commit="' + b'a' * 40 + b'"/></metadata></package>'
            package = root / 'creator.nupkg'
            def pack(selected):
                with zipfile.ZipFile(package, 'w') as archive:
                    archive.writestr('FS.GG.NewSddWorkspace.nuspec', nuspec)
                    for name, body in selected.items():
                        archive.writestr('tools/net10.0/any/' + name, body)
            pack(members)
            self.assertEqual(board_package_closure(package, 'a' * 40, source_root=root)['projectCount'], 11)
            for omitted in ('FS.GG.Coord.GitHub.dll', 'fsgg-coord-engine.dll', 'new-sdd-workspace.deps.json'):
                with self.subTest(omitted=omitted):
                    pack({name: body for name, body in members.items() if name != omitted})
                    with self.assertRaises(ValueError):
                        board_package_closure(package, 'a' * 40, source_root=root)
            pack({**members, 'FS.GG.Coord.GitHub.dll': b'old assembly'})
            with self.assertRaisesRegex(ValueError, 'differs from built source'):
                board_package_closure(package, 'a' * 40, source_root=root)
            for mutant in ({'libraries': {'FS.GG.Coord.Cli/0.97.1': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.1'}}, 'FS.GG.Coord.Cli/0.97.1': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.97.0': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.0'}}, 'FS.GG.Coord.Cli/0.97.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.96.0': {}}},
                           {**deps, 'targets': {}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'FS.GG.Coord.Cli/0.99.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.99.0'}}}}}):
                old_deps = json.dumps(mutant).encode()
                (output / 'new-sdd-workspace.deps.json').write_bytes(old_deps)
                pack({**members, 'new-sdd-workspace.deps.json': old_deps})
                with self.assertRaisesRegex(ValueError, 'current coherent CLI dependency metadata'):
                    board_package_closure(package, 'a' * 40, source_root=root)
            with self.assertRaisesRegex(ValueError, 'package/source identity'):
                board_package_closure(package, 'b' * 40, source_root=root)

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
                zipped.writestr("manifest.json", json.dumps(manifest()))
                zipped.writestr("package-evidence.json", "{}")
                zipped.writestr("FS.GG.NewSddWorkspace.0.16.0.nupkg", "fixture")
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
                self.assertEqual(verifier.verify(artifact, run, archive, root / "good", source)["version"], "0.16.0")
            old_archive = root / "historical.zip"
            with zipfile.ZipFile(old_archive, "w") as zipped:
                zipped.writestr("manifest.json", json.dumps({**manifest(), "version": "0.13.0",
                                                            "tag": "new-sdd-workspace/v0.13.0"}))
                zipped.writestr("package-evidence.json", "{}")
                zipped.writestr("FS.GG.NewSddWorkspace.0.13.0.nupkg", "fixture")
            old_artifact = {**artifact, "digest": "sha256:" + hashlib.sha256(old_archive.read_bytes()).hexdigest()}
            with patch.object(verifier.subprocess, "run") as package_check:
                with self.assertRaises(ValueError):
                    verifier.verify(old_artifact, run, old_archive, root / "old", source)
                package_check.assert_not_called()
            altered = {**run, "run_attempt": 2}
            with self.assertRaisesRegex(ValueError, "first-attempt"):
                verifier.verify(artifact, altered, archive, root / "rerun", source)
            altered_artifact = {**artifact, "digest": "sha256:" + "0" * 64}
            with self.assertRaisesRegex(ValueError, "digest differs"):
                verifier.verify(altered_artifact, run, archive, root / "tampered", source)

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
        with self.assertRaisesRegex(Refused, "exact Wizard 0.16.0"):
            effects(prior)

    def test_fresh_journal_and_version_cut_retain_the_existing_boundary(self):
        publisher = (ROOT / "scripts/new-sdd-workspace-successor-publish.py").read_text()
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/board-v2-product-creator-016"', publisher)
        self.assertNotIn('REF = "refs/heads/fsgg/v2/journal/release/svg-d5-wizard-012"', publisher)
        self.assertIn('"version": "0.16.0"', publisher)
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
        self.assertIn('<Version>0.16.0</Version>', project)
        self.assertIn('test "$version" = 0.16.0', candidate)
        self.assertIn('FS.GG.NewSddWorkspace.0.16.0.nupkg', candidate)
        self.assertIn('--tag new-sdd-workspace/v0.16.0', candidate)

    def test_workflow_retains_exact_historical_title_and_isolates_current_mode(self):
        workflow = (ROOT / '.github/workflows/release-new-sdd-workspace.yml').read_text()
        self.assertIn("inputs.promotion_recovery != 'off' && format('Wizard 0.13 recovery {0} {1} {2}', "
                      "inputs.promotion_recovery, inputs.recovery_correlation, inputs.recovery_binding_sha256)", workflow)
        self.assertIn("format('Wizard 0.16 successor {0}', inputs.candidate_run_id)", workflow)
        self.assertIn("name: new-sdd-workspace-014-publisher", workflow)
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
            package = pathlib.Path(temporary) / "FS.GG.NewSddWorkspace.0.16.0.nupkg"
            def write_package(commit):
                nuspec = ("<package><metadata><id>FS.GG.NewSddWorkspace</id>"
                          "<version>0.16.0</version><repository commit='" + commit + "'/></metadata></package>")
                with zipfile.ZipFile(package, "w") as archive:
                    archive.writestr("FS.GG.NewSddWorkspace.nuspec", nuspec)
            write_package("a" * 40)
            self.assertEqual(checker.package_identity(package),
                             ("FS.GG.NewSddWorkspace", "0.16.0", "a" * 40))
            write_package("b" * 40)
            with self.assertRaisesRegex(ValueError, "repository commit differs"):
                checker.build_manifest(type("Args", (), {"package": str(package), "source_sha": "a" * 40,
                                                       "version": "0.16.0", "tag": "new-sdd-workspace/v0.16.0"})())
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
                if path.endswith("/git/ref/tags/new-sdd-workspace/v0.16.0") and self.tag:
                    return {"object": {"sha": self.tag}}
                if path.endswith("/releases/tags/new-sdd-workspace/v0.16.0") and self.release and not self.release["draft"]:
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
            package = root / "FS.GG.NewSddWorkspace.0.16.0.nupkg"
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
                if path.endswith("/releases/tags/new-sdd-workspace/v0.16.0"):
                    raise NotFound(path)
                if path.endswith("/releases?per_page=100&page=1"):
                    return [{"id": 1, "tag_name": "new-sdd-workspace/v0.16.0", "draft": True}]
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
    spec = importlib.util.spec_from_file_location("tested_creator016_publisher", ROOT / "scripts/new-sdd-workspace-successor-publish.py")
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
        self.intent = {"contentId": self.content, "sourceSha": "a" * 40, "version": "0.16.0",
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
            journal.read(); warm = {key: counted.attempted[key]-cold[key] for key in cold}
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
            categories = {}
            for method, path in native.calls:
                category = ("quota" if path == "rate_limit" else "protection" if "/rules" in path
                            else "history" if "/commits?" in path else "compare" if "/compare/" in path
                            else "refs" if "/git/ref" in path else "commits" if "/git/commits" in path
                            else "trees" if "/git/trees" in path else "blobs" if "/git/blobs" in path else "repository")
                key = method + ":" + category; categories[key] = categories.get(key, 0) + 1
            total = sum(counted.attempted.values())
            self.assertLess(total, 4200)
            print("Creator016 actual all-eight Authority profile: " + json.dumps(
                {"coldPreparationByMethod": cold, "warmReadByMethod": warm, "categories": categories,
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
            with self.assertRaises(Refused): self.prepare(counted, journal, api, admission, provider)
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
            with self.assertRaises(Refused): advance_effects(self.content, self.ordered, journal, admission, provider)
            self.assertEqual(journal.read().effects, {"tag": "intent"})
            for _ in range(2):
                self.assertEqual(advance_effects(self.content, self.ordered, journal, admission, provider), "waiting")
            self.assertEqual(provider.writes, [])

    def test_delayed_last_effect_exhausts_bound_before_send_and_preserves_intent(self):
        with tempfile.TemporaryDirectory() as temporary:
            _, native, counted, journal, api, admission, provider, _ = self.fixture(pathlib.Path(temporary))
            self.prepare(counted, journal, api, admission, provider)
            provider.delayed.add("promote")
            with self.assertRaisesRegex(Refused, "ceiling exhausted"):
                self.caller.reconcile_publication(self.content, self.ordered, journal, admission, provider,
                                                 self.report, clock=lambda: 0, sleep=lambda _: None)
            self.assertEqual(sum(counted.attempted.values()), 4200)
            self.assertEqual(len(native.calls), 4200)
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
                with patch.object(sys, "argv", argv), patch.dict(os.environ, env, clear=True), \
                        patch.object(self.caller, "GitHubAPI", side_effect=api), \
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
                summary = root/"creator016-publication-summary.json"
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
        self.assertIn("name: creator016-publication-summary-${{ github.run_id }}", ordinary)
        self.assertIn("path: ${{ runner.temp }}/creator016-publication-summary.json", ordinary)
        self.assertIn("if-no-files-found: error", ordinary)
        self.assertNotIn("creator016-publication-summary", historical)
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
    return tests


if __name__ == "__main__":
    if "--board-package" in sys.argv:
        import argparse
        parser = argparse.ArgumentParser()
        parser.add_argument("--board-package", type=pathlib.Path, required=True)
        parser.add_argument("--source-sha", required=True)
        parser.add_argument("--release-manifest", type=pathlib.Path)
        parser.add_argument("--frozen-coord-dependencies", type=pathlib.Path)
        parser.add_argument("--frozen-coord-source", type=pathlib.Path)
        args = parser.parse_args()
        closure = board_package_closure(args.board_package, args.source_sha,
                                        frozen_dependencies=args.frozen_coord_dependencies,
                                        frozen_source=args.frozen_coord_source)
        if args.release_manifest:
            checker = runpy.run_path(str(ROOT / "scripts/new-sdd-workspace-release.py"))
            evidence = checker["verify_artifact"](args.release_manifest, args.board_package)
            evidence["boardClosure"] = closure
        else:
            evidence = closure
        print(json.dumps(evidence, sort_keys=True))
    else:
        unittest.main()

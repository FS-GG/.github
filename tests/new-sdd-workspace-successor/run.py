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
                          frozen_dependencies: pathlib.Path | None = None):
    """Join the actual current package to its freshly built creator/dependency output."""
    spec = importlib.util.spec_from_file_location("wizard_package_identity", source_root / "scripts/new-sdd-workspace-release.py")
    checker = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(checker)
    if checker.package_identity(package) != (CURRENT_016.package, CURRENT_016.version, source_sha):
        raise ValueError("current creator package/source identity differs")
    project = source_root / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
    projects = {}
    def visit(path):
        path = path.resolve()
        if path in projects:
            return
        xml = ElementTree.parse(path)
        projects[path] = next((e.text for e in xml.iter("AssemblyName") if e.text), path.stem)
        for reference in xml.iter("ProjectReference"):
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
        cli = "FS.GG.Coord.Cli/0.97.1"
        targets = deps.get("targets", {})
        if (deps.get("libraries", {}).get(cli, {}).get("type") != "project"
                or not targets or any(
                    target.get("new-sdd-workspace/0.16.0", {}).get("dependencies", {}).get("FS.GG.Coord.Cli") != "0.97.1"
                    or "fsgg-coord-engine.dll" not in target.get(cli, {}).get("runtime", {})
                    for target in targets.values())):
            raise ValueError("creator package must carry current coherent CLI dependency metadata")
    result = {"schema": "fsgg.creator-board-package-closure/1", "version": CURRENT_016.version,
            "sourceSha": source_sha, "coherentVersion": "0.97.1", "projectCount": len(projects),
            "builtFilesCompared": len(required), "archiveSha256": hashlib.sha256(package.read_bytes()).hexdigest(),
            "installedAdoptionAccepted": False}
    if frozen_dependencies is not None:
        helper = runpy.run_path(str(source_root / "scripts/creator-frozen-coord-dependencies.py"))
        result["publishedDependencyClosure"] = helper["package_closure"](
            package, frozen_dependencies, source_root)
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
                    copy_project((path.parent / reference.attrib['Include']).resolve())
            copy_project(ROOT / 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj')
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
            deps = {'libraries': {'FS.GG.Coord.Cli/0.97.1': {'type': 'project'}}, 'targets': {
                '.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.1'}},
                                           'FS.GG.Coord.Cli/0.97.1': {'runtime': {'fsgg-coord-engine.dll': {}}}}}}
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
            for mutant in ({'libraries': {'FS.GG.Coord.Cli/0.97.0': {'type': 'project'}}, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.0'}}, 'FS.GG.Coord.Cli/0.97.0': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {'libraries': {'FS.GG.Coord.Cli/0.96.0': {}}},
                           {**deps, 'targets': {}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'FS.GG.Coord.Cli/0.97.1': {'runtime': {'fsgg-coord-engine.dll': {}}}}}},
                           {**deps, 'targets': {'.NETCoreApp,Version=v10.0': {'new-sdd-workspace/0.16.0': {'dependencies': {'FS.GG.Coord.Cli': '0.97.1'}}}}}):
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
        args = parser.parse_args()
        closure = board_package_closure(args.board_package, args.source_sha,
                                        frozen_dependencies=args.frozen_coord_dependencies)
        if args.release_manifest:
            checker = runpy.run_path(str(ROOT / "scripts/new-sdd-workspace-release.py"))
            evidence = checker["verify_artifact"](args.release_manifest, args.board_package)
            evidence["boardClosure"] = closure
        else:
            evidence = closure
        print(json.dumps(evidence, sort_keys=True))
    else:
        unittest.main()

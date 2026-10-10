"""Focused file-custody controls; synthetic DLL bytes are never executed."""
import hashlib
import json
import pathlib
import runpy
import subprocess
import tempfile
import unittest
import zipfile
from xml.etree import ElementTree

ROOT = pathlib.Path(__file__).resolve().parents[2]
HELPER = runpy.run_path(str(ROOT / "scripts/creator-frozen-coord-dependencies.py"))
PREFIX = HELPER["PREFIX"]


class FrozenDependencyControls(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name) / "creator"
        self.root.mkdir()
        self.producer = pathlib.Path(self.temp.name) / "producer"
        self.producer.mkdir()
        self.creator = self.root / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
        self.creator.parent.mkdir(parents=True)
        self.creator.write_bytes((ROOT / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj").read_bytes())
        self.project = self.producer / "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text('<Project><AssemblyName>fsgg-coord-engine</AssemblyName></Project>')
        (self.producer / "global.json").write_text("{}")
        (self.project.parent / "packages.lock.json").write_text("{}")
        self.source_sha = self.commit_fixture(self.producer)
        self.commit_fixture(self.root)
        self.members = {"fsgg-coord-engine" + suffix: b"synthetic frozen input " + suffix.encode()
                        for suffix in (".dll", ".pdb", ".xml")}
        self.members["runtimes/linux-x64/native/engine.so"] = b"synthetic native input"
        self.package = pathlib.Path(self.temp.name) / "accepted.nupkg"
        self.write_archive(self.members)
        self.pin = {"version": "0.99.0", "projects": {"scripts/NewSddWorkspace/NewSddWorkspace.fsproj": "new-sdd-workspace",
                                 "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj": "fsgg-coord-engine"},
                    "archiveSha256": self.hash(self.package.read_bytes()), "sourceSha": self.source_sha,
                    "members": {name: self.hash(body) for name, body in self.members.items()}}
        self.pin["sourceLeaves"] = [{"path": name, "sha256": self.hash((self.producer / name).read_bytes())}
                                    for name in sorted(HELPER["dependency_source_paths"](self.producer, self.pin["projects"]))]
        self.dependencies = pathlib.Path(self.temp.name) / "frozen"

    @staticmethod
    def commit_fixture(root):
        for command in (["git", "init", "-q"], ["git", "add", "."],
                        ["git", "-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
                         "commit", "-qm", "immutable fixture"]):
            subprocess.run(command, cwd=root, check=True, capture_output=True, timeout=10)
        return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True, timeout=10).strip()

    @staticmethod
    def hash(body):
        return hashlib.sha256(body).hexdigest()

    def write_archive(self, members):
        with zipfile.ZipFile(self.package, "w") as archive:
            archive.writestr("FS.GG.Coord.Cli.nuspec", '<package><metadata><id>FS.GG.Coord.Cli</id><version>0.99.0</version><repository commit="' + self.source_sha + '"/></metadata></package>')
            for name, body in members.items():
                archive.writestr(PREFIX + name, body)

    def stage(self):
        return HELPER["stage"](self.root, self.dependencies, self.package, self.pin, self.producer)

    def test_stages_exact_runtime_and_project_bodies_and_refuses_repeat(self):
        result = self.stage()
        self.assertEqual(result["dependencyMembers"], 4)
        for name, body in self.members.items():
            self.assertEqual((self.dependencies / name).read_bytes(), body)
        self.assertEqual((self.project.parent / "bin/Release/net10.0/fsgg-coord-engine.dll").read_bytes(),
                         self.members["fsgg-coord-engine.dll"])
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.stage()

    def test_archive_and_source_mutants_refuse_before_output(self):
        original = self.package.read_bytes()
        self.package.write_bytes(b"changed archive")
        with self.assertRaisesRegex(ValueError, "archive changed"):
            self.stage()
        self.assertFalse(self.dependencies.exists())
        self.package.write_bytes(original)
        self.project.write_text('<Project><AssemblyName>fsgg-coord-engine</AssemblyName><!-- changed --></Project>')
        with self.assertRaisesRegex(ValueError, "source changed"):
            self.stage()
        self.assertFalse(self.dependencies.exists())

    def test_metadata_identity_mutants_refuse_before_output(self):
        for key, value in (("version", "0.97.1"), ("version", "0.97.0"), ("sourceSha", "b" * 40)):
            with self.subTest(field=key):
                original = self.pin[key]
                self.pin[key] = value
                with self.assertRaisesRegex(ValueError, "package/version/source identity changed|source revision changed"):
                    self.stage()
                self.assertFalse(self.dependencies.exists())
                self.pin[key] = original

    def test_actual_pin_matches_selected_source_and_candidate_download(self):
        pin = json.loads((ROOT / "scripts/creator-frozen-coord-dependencies.json").read_text())
        self.assertEqual(pin["version"], "0.99.0")
        self.assertEqual(pin["sourceSha"], "64e95ebec1a8294e16edaafdb27e6aa96f32c6f7")
        self.assertEqual(len(pin["projects"]), 11)
        self.assertEqual(len(pin["sourceLeaves"]), 281)
        self.assertEqual(len(pin["members"]), 81)
        historical = pathlib.Path(self.temp.name) / "actual-published-source"
        subprocess.run(["git", "clone", "--shared", "--no-checkout", str(ROOT), str(historical)],
                       check=True, capture_output=True, timeout=15)
        # Keep the local fixture bounded to the complete pinned producer inputs.
        subprocess.run(["git", "config", "core.sparseCheckout", "true"], cwd=historical,
                       check=True, capture_output=True, timeout=10)
        (historical / ".git/info/sparse-checkout").write_text(''.join('/' + row["path"] + '\n' for row in pin["sourceLeaves"]))
        subprocess.run(["git", "checkout", "--detach", pin["sourceSha"]], cwd=historical,
                       check=True, capture_output=True, timeout=15)
        before = {p.relative_to(historical): p.read_bytes() for p in historical.rglob("*") if p.is_file()}
        self.assertEqual(len(HELPER["source_projects"](ROOT, pin, historical)), 11)
        command = ["python3", "-B", str(ROOT / "scripts/creator-frozen-coord-dependencies.py"),
                   "verify-source", "--producer-source", str(historical)]
        passed = subprocess.run(command, capture_output=True, text=True, timeout=20)
        self.assertEqual(passed.returncode, 0, passed.stderr)
        report = json.loads(passed.stdout)
        self.assertEqual(report["sourceProjects"], 11)
        self.assertEqual(report["publishedDependencySourceSha"], pin["sourceSha"])
        self.assertEqual(report["currentCreatorSourceSha"], HELPER["git_identity"](ROOT))
        self.assertEqual(before, {p.relative_to(historical): p.read_bytes() for p in historical.rglob("*") if p.is_file()})
        with self.assertRaisesRegex(ValueError, "distinct frozen|source revision changed"):
            HELPER["source_projects"](ROOT, pin, ROOT)
        no_source = subprocess.run(command[:-2], capture_output=True, text=True, timeout=10)
        self.assertNotEqual(no_source.returncode, 0)
        staging_input = subprocess.run(command + ["--dependencies", str(historical / "frozen")],
                                       capture_output=True, text=True, timeout=20)
        self.assertNotEqual(staging_input.returncode, 0)
        self.assertIn("source verification refuses staging inputs", staging_input.stderr)
        for missing in ("src/FS.GG.Telemetry.Client/CustodyProjection/CustodyProcessLease.fs",
                        "src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs"):
            mutant = {**pin, "sourceLeaves": [r for r in pin["sourceLeaves"] if r["path"] != missing]}
            with self.assertRaisesRegex(ValueError, "source coverage differs"):
                HELPER["stage"](ROOT, historical / "frozen", historical / "missing.nupkg", mutant, historical)
            self.assertFalse((historical / "frozen").exists())
        for leaves in (pin["sourceLeaves"] + [pin["sourceLeaves"][0]],
                       pin["sourceLeaves"] + [{"path": "foreign.fs", "sha256": "a" * 64}]):
            with self.assertRaisesRegex(ValueError, "source coverage differs"):
                HELPER["source_projects"](ROOT, {**pin, "sourceLeaves": leaves}, historical)
        with self.assertRaisesRegex(ValueError, "project graph changed"):
            HELPER["source_projects"](ROOT, {**pin, "projects": {}}, historical)
        changed = historical / "src/FS.GG.Telemetry.Client/CustodyProjection/CustodyProcessLease.fs"
        changed.write_bytes(changed.read_bytes() + b"\n// deliberate unpublished source drift\n")
        with self.assertRaisesRegex(ValueError, "published dependency source changed"):
            HELPER["source_projects"](ROOT, pin, historical)
        workflow = (ROOT / ".github/workflows/release-new-sdd-workspace-successor-candidate.yml").read_text()
        guard = workflow.index("python3 scripts/creator-frozen-coord-dependencies.py verify-source")
        self.assertLess(workflow.index("ref: " + pin["sourceSha"]), guard)
        self.assertLess(guard, workflow.index("actions/setup-dotnet"))
        self.assertLess(guard, workflow.index("Bind exact protected source and unused version"))
        self.assertIn(pin["downloadUrl"], workflow)
        self.assertIn('--producer-source "${FsggFrozenCoordSource:', workflow)
        self.assertIn('--frozen-coord-source "${FsggFrozenCoordSource:', workflow)

    def test_missing_wrong_and_linked_source_roots_refuse(self):
        for producer in (None, self.root, self.producer / "missing"):
            with self.assertRaises((ValueError, subprocess.CalledProcessError)):
                HELPER["source_projects"](self.root, self.pin, producer)
        with self.assertRaisesRegex(ValueError, "source revision changed"):
            HELPER["source_projects"](self.root, {**self.pin, "sourceSha": "b" * 40}, self.producer)
        link = pathlib.Path(self.temp.name) / "linked-source"
        link.symlink_to(self.producer, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "linked source root"):
            HELPER["source_projects"](self.root, self.pin, link)

    def test_staging_refuses_preexisting_producer_output(self):
        output = self.project.parent / "bin/Release/net10.0"
        output.mkdir(parents=True)
        (output / "foreign.dll").write_bytes(b"owned foreign bytes")
        with self.assertRaisesRegex(ValueError, "staged project output already exists"):
            self.stage()
        self.assertFalse(self.dependencies.exists())
        self.assertEqual((output / "foreign.dll").read_bytes(), b"owned foreign bytes")

    def test_reference_conditions_and_paired_property_guard_refuse_drift(self):
        original = self.creator.read_text()
        mutants = [original.replace('<ItemGroup>\n    <ProjectReference', '<ItemGroup Condition="false">\n    <ProjectReference'),
                   original.replace(HELPER["NORMAL_CONDITION"], ""),
                   original.replace(HELPER["FROZEN_CONDITION"], HELPER["NORMAL_CONDITION"]),
                   original.replace(HELPER["FROZEN_REFERENCE"], HELPER["NORMAL_REFERENCE"]),
                   original.replace(HELPER["PAIR_ERROR"], "false"),
                   original.replace('</Project>', '<ItemGroup><ProjectReference Include="foreign.fsproj" /></ItemGroup></Project>')]
        for mutant in mutants:
            self.creator.write_text(mutant)
            with self.assertRaisesRegex(ValueError, "reference copy route|paired selection guard|reference parent scope"):
                self.stage()
            self.assertFalse(self.dependencies.exists())
        self.creator.write_text(original)

    def test_linked_destination_refuses_before_write(self):
        target = self.root / "other"
        target.mkdir()
        link = self.root / "linked"
        link.symlink_to(target, target_is_directory=True)
        self.dependencies = link / "frozen"
        with self.assertRaisesRegex(ValueError, "linked frozen"):
            self.stage()
        self.assertEqual(list(target.iterdir()), [])

    def test_layout_refuses_mutation_missing_extra_and_staged_project_drift(self):
        self.stage()
        body = self.dependencies / "fsgg-coord-engine.dll"
        body.write_bytes(b"changed")
        with self.assertRaisesRegex(ValueError, "body changed"):
            HELPER["layout"](self.root, self.dependencies, self.pin, self.producer)
        body.write_bytes(self.members[body.name])
        body.unlink()
        with self.assertRaisesRegex(ValueError, "census differs"):
            HELPER["layout"](self.root, self.dependencies, self.pin, self.producer)
        body.write_bytes(self.members[body.name])
        extra = self.dependencies / "foreign.dll"
        extra.write_bytes(b"foreign")
        with self.assertRaisesRegex(ValueError, "census differs"):
            HELPER["layout"](self.root, self.dependencies, self.pin, self.producer)
        extra.unlink()
        staged = self.project.parent / "bin/Release/net10.0/fsgg-coord-engine.dll"
        staged.write_bytes(b"rebuilt substitute")
        with self.assertRaisesRegex(ValueError, "staged project dependency changed"):
            HELPER["layout"](self.root, self.dependencies, self.pin, self.producer)

    def test_package_check_requires_every_frozen_body(self):
        self.stage()
        (self.root / "scripts/creator-frozen-coord-dependencies.json").write_text(json.dumps(self.pin))
        self.assertEqual(HELPER["package_closure"](self.package, self.dependencies, self.root, self.producer)["dependencyMembers"], 4)
        self.write_archive({**self.members, "fsgg-coord-engine.dll": b"rebuilt substitute"})
        with self.assertRaisesRegex(ValueError, "package changed published dependency"):
            HELPER["package_closure"](self.package, self.dependencies, self.root, self.producer)

    def test_unbuilt_child_content_guards_refuse_missing_enabled_or_unscoped(self):
        original = self.creator.read_text()
        for name in ("_GetChildProjectCopyToOutputDirectoryItems", "_GetChildProjectCopyToPublishDirectoryItems"):
            for mutant in (original.replace(f'<{name}>false</{name}>', ''),
                           original.replace(f'<{name}>false</{name}>', f'<{name}>true</{name}>'),
                           original.replace("<PropertyGroup Condition=\"'$(FsggFrozenCoordDependencies)' != ''\">", '<PropertyGroup>', 1)):
                with self.subTest(guard=name, mutant=mutant):
                    self.creator.write_text(mutant)
                    with self.assertRaisesRegex(ValueError, "frozen dependency (copy guard|property scope) changed"):
                        self.stage()
                    self.assertFalse(self.dependencies.exists())
        self.creator.write_text(original)

    def test_explicit_frozen_body_copy_cannot_be_dropped(self):
        original = self.creator.read_text()
        self.creator.write_text(original.replace('CopyToOutputDirectory="Always"', 'CopyToOutputDirectory="Never"'))
        with self.assertRaisesRegex(ValueError, "frozen dependency content copy changed"):
            self.stage()
        self.assertFalse(self.dependencies.exists())

    def test_runtime_reference_copy_route_and_duplicate_enforcement_cannot_be_disabled(self):
        original = self.creator.read_text()
        for mutant in (original.replace('<ProjectReference Include=', '<ProjectReference Private="false" Include=', 1),
                       original.replace('Condition="' + HELPER["FROZEN_CONDITION"] + '" />',
                                        'Condition="' + HELPER["FROZEN_CONDITION"] + '"><Private>false</Private></ProjectReference>'),
                       original.replace('</Project>', '<PropertyGroup><ErrorOnDuplicatePublishOutputFiles>false</ErrorOnDuplicatePublishOutputFiles></PropertyGroup></Project>')):
            with self.subTest(mutant=mutant):
                self.creator.write_text(mutant)
                with self.assertRaisesRegex(ValueError, "frozen dependency (reference copy route|duplicate publish enforcement) changed"):
                    self.stage()
                self.assertFalse(self.dependencies.exists())
        self.creator.write_text(original)

    def test_exact_three_overlap_exclusions_cannot_be_missing_or_broadened(self):
        original = self.creator.read_text()
        for mutant in (original.replace('/fsgg-coord-engine.xml', '/foreign.xml'),
                       original.replace('/fsgg-coord-engine.dll', '/*.dll')):
            self.creator.write_text(mutant)
            with self.assertRaisesRegex(ValueError, 'frozen dependency content copy changed'):
                self.stage()
            self.assertFalse(self.dependencies.exists())
        self.creator.write_text(original)


if __name__ == "__main__":
    unittest.main()

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
        self.root = pathlib.Path(self.temp.name)
        self.creator = self.root / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
        self.creator.parent.mkdir(parents=True)
        actual = ElementTree.parse(ROOT / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj").getroot()
        self.creator.write_text('<Project><PropertyGroup><AssemblyName>new-sdd-workspace</AssemblyName></PropertyGroup>'
                                '<ItemGroup><ProjectReference Include="../../src/Engine/Engine.fsproj" /></ItemGroup>'
                                + ''.join(ElementTree.tostring(group, encoding="unicode") for group in actual
                                          if group.get("Condition") == "'$(FsggFrozenCoordDependencies)' != ''") + '</Project>')
        self.project = self.root / "src/Engine/Engine.fsproj"
        self.project.parent.mkdir(parents=True)
        self.project.write_text('<Project><AssemblyName>fsgg-coord-engine</AssemblyName></Project>')
        self.members = {"fsgg-coord-engine" + suffix: b"synthetic frozen input " + suffix.encode()
                        for suffix in (".dll", ".pdb", ".xml")}
        self.members["runtimes/linux-x64/native/engine.so"] = b"synthetic native input"
        self.package = self.root / "accepted.nupkg"
        self.write_archive(self.members)
        self.pin = {"version": "0.99.0", "projects": {"scripts/NewSddWorkspace/NewSddWorkspace.fsproj": "new-sdd-workspace",
                                 "src/Engine/Engine.fsproj": "fsgg-coord-engine"},
                    "sourceLeaves": [{"path": "src/Engine/Engine.fsproj", "sha256": self.hash(self.project.read_bytes())}],
                    "archiveSha256": self.hash(self.package.read_bytes()), "sourceSha": "a" * 40,
                    "members": {name: self.hash(body) for name, body in self.members.items()}}
        (self.root / "global.json").write_text("{}")
        (self.project.parent / "packages.lock.json").write_text("{}")
        self.pin["sourceLeaves"] = [{"path": name, "sha256": self.hash((self.root / name).read_bytes())}
                                    for name in sorted(HELPER["dependency_source_paths"](self.root, self.pin["projects"]))]
        self.dependencies = self.root / "frozen"

    @staticmethod
    def hash(body):
        return hashlib.sha256(body).hexdigest()

    def write_archive(self, members):
        with zipfile.ZipFile(self.package, "w") as archive:
            archive.writestr("FS.GG.Coord.Cli.nuspec", '<package><metadata><id>FS.GG.Coord.Cli</id><version>0.99.0</version><repository commit="' + 'a' * 40 + '"/></metadata></package>')
            for name, body in members.items():
                archive.writestr(PREFIX + name, body)

    def stage(self):
        return HELPER["stage"](self.root, self.dependencies, self.package, self.pin)

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
                with self.assertRaisesRegex(ValueError, "package/version/source identity changed"):
                    self.stage()
                self.assertFalse(self.dependencies.exists())
                self.pin[key] = original

    def test_actual_pin_matches_selected_source_and_candidate_download(self):
        pin = json.loads((ROOT / "scripts/creator-frozen-coord-dependencies.json").read_text())
        self.assertEqual(pin["version"], "0.99.0")
        self.assertEqual(pin["sourceSha"], "64e95ebec1a8294e16edaafdb27e6aa96f32c6f7")
        expected = {"scripts/NewSddWorkspace/NewSddWorkspace.fsproj", *(
            "src/" + name + "/" + name + ".fsproj" for name in (
                "FS.GG.Coord.Cli", "FS.GG.Coord.Core", "FS.GG.Coord.GitHub",
                "FS.GG.Coord.Cli.Kernel", "FS.GG.Coord.Cli.BoardOps", "FS.GG.Coord.Cli.Lifecycle",
                "FS.GG.Telemetry.Store", "FS.GG.Telemetry.Client", "FS.GG.Telemetry.Contracts",
                "FS.GG.Telemetry.Dashboard"))}
        self.assertEqual(set(pin["projects"]), expected)
        self.assertEqual(len(pin["members"]), 81)  # Observed original 0.99 archive census.
        # Dependency bytes belong to the published revision, not the current
        # feature branch. The Creator's own copy contract remains current.
        historical = self.root / "published-dependency-source"
        creator_path = "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
        inputs = {row["path"] for row in pin["sourceLeaves"]} | set(pin["projects"])
        for name in sorted(inputs):
            destination = historical / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            body = ((ROOT / name).read_bytes() if name == creator_path else
                    subprocess.check_output(["git", "show", pin["sourceSha"] + ":" + name], cwd=ROOT))
            destination.write_bytes(body)
        for row in pin["sourceLeaves"]:
            self.assertEqual(self.hash((historical / row["path"]).read_bytes()), row["sha256"])
        self.assertEqual(len(HELPER["source_projects"](historical, pin)), 11)
        # The candidate performs this pure source guard before SDK setup,
        # occupancy requests, or downloading/staging dependency bytes.
        helper_path = historical / "scripts/creator-frozen-coord-dependencies.py"
        helper_path.write_bytes((ROOT / "scripts/creator-frozen-coord-dependencies.py").read_bytes())
        (historical / "scripts/creator-frozen-coord-dependencies.json").write_text(json.dumps(pin))
        before = {path.relative_to(historical): path.read_bytes()
                  for path in historical.rglob("*") if path.is_file()}
        passed = subprocess.run(["python3", str(helper_path), "verify-source"], capture_output=True, text=True)
        self.assertEqual(passed.returncode, 0, passed.stderr)
        self.assertEqual(json.loads(passed.stdout)["sourceProjects"], 11)
        self.assertEqual(before, {path.relative_to(historical): path.read_bytes()
                                  for path in historical.rglob("*") if path.is_file()})
        staging_input = subprocess.run(["python3", str(helper_path), "verify-source",
                                        "--dependencies", str(historical / "frozen")],
                                       capture_output=True, text=True)
        self.assertNotEqual(staging_input.returncode, 0)
        self.assertIn("source verification refuses staging inputs", staging_input.stderr)
        drift = [row["path"] for row in pin["sourceLeaves"]
                 if self.hash((ROOT / row["path"]).read_bytes()) != row["sha256"]]
        if drift:
            with self.assertRaises(ValueError) as refusal:
                HELPER["source_projects"](ROOT, pin)
            self.assertEqual(str(refusal.exception), "published dependency source changed: " + drift[0])
            current = subprocess.run(["python3", str(ROOT / "scripts/creator-frozen-coord-dependencies.py"),
                                      "verify-source"], capture_output=True, text=True)
            self.assertNotEqual(current.returncode, 0)
            self.assertIn("published dependency source changed: " + drift[0], current.stderr)
        else:
            self.assertEqual(len(HELPER["source_projects"](ROOT, pin)), 11)
        # Missing actual compiler inputs must refuse before even opening an archive.
        for missing in ("src/FS.GG.Telemetry.Client/CustodyProjection/CustodyProcessLease.fs",
                        "src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs"):
            with self.subTest(missing=missing):
                mutant = {**pin, "sourceLeaves": [row for row in pin["sourceLeaves"] if row["path"] != missing]}
                self.assertEqual(len(mutant["sourceLeaves"]), len(pin["sourceLeaves"]) - 1)
                with self.assertRaisesRegex(ValueError, "source coverage differs"):
                    HELPER["stage"](historical, historical / "frozen", historical / "missing.nupkg", mutant)
                self.assertFalse((historical / "frozen").exists())
        for leaves in (pin["sourceLeaves"] + [pin["sourceLeaves"][0]],
                       pin["sourceLeaves"] + [{"path": "foreign.fs", "sha256": "a" * 64}]):
            with self.assertRaisesRegex(ValueError, "source coverage differs"):
                HELPER["source_projects"](historical, {**pin, "sourceLeaves": leaves})
        with self.assertRaisesRegex(ValueError, "project graph changed"):
            HELPER["source_projects"](historical, {**pin, "projects": {}})
        previous = json.loads(subprocess.check_output(
            ["git", "show", "3bd43688bc0ff0b7fe17e1bc7eff2475bf5e0b80:scripts/creator-frozen-coord-dependencies.json"], cwd=ROOT))
        self.assertEqual(previous["version"], "0.97.1")
        with self.assertRaisesRegex(ValueError, "source coverage differs"):
            HELPER["source_projects"](ROOT, previous)
        changed = historical / "src/FS.GG.Telemetry.Client/CustodyProjection/CustodyProcessLease.fs"
        changed.write_bytes(changed.read_bytes() + b"\n// deliberate unpublished source drift\n")
        refused = subprocess.run(["python3", str(helper_path), "verify-source"], capture_output=True, text=True)
        self.assertNotEqual(refused.returncode, 0)
        self.assertIn("published dependency source changed", refused.stderr)
        with self.assertRaisesRegex(ValueError, "published dependency source changed"):
            HELPER["source_projects"](historical, pin)
        self.assertFalse((historical / "frozen").exists())
        workflow = (ROOT / ".github/workflows/release-new-sdd-workspace-successor-candidate.yml").read_text()
        guard = workflow.index("python3 scripts/creator-frozen-coord-dependencies.py verify-source")
        self.assertLess(guard, workflow.index("actions/setup-dotnet"))
        self.assertLess(guard, workflow.index("Bind exact protected source and unused version"))
        self.assertLess(guard, workflow.index("Stage the exact accepted public coordination dependency bodies"))
        self.assertIn(pin["downloadUrl"], workflow)
        self.assertNotIn("FS.GG.Coord.Cli.0.97.0.nupkg", workflow)

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
            HELPER["layout"](self.root, self.dependencies, self.pin)
        body.write_bytes(self.members[body.name])
        body.unlink()
        with self.assertRaisesRegex(ValueError, "census differs"):
            HELPER["layout"](self.root, self.dependencies, self.pin)
        body.write_bytes(self.members[body.name])
        extra = self.dependencies / "foreign.dll"
        extra.write_bytes(b"foreign")
        with self.assertRaisesRegex(ValueError, "census differs"):
            HELPER["layout"](self.root, self.dependencies, self.pin)
        extra.unlink()
        staged = self.project.parent / "bin/Release/net10.0/fsgg-coord-engine.dll"
        staged.write_bytes(b"rebuilt substitute")
        with self.assertRaisesRegex(ValueError, "staged project dependency changed"):
            HELPER["layout"](self.root, self.dependencies, self.pin)

    def test_package_check_requires_every_frozen_body(self):
        self.stage()
        (self.root / "scripts/creator-frozen-coord-dependencies.json").write_text(json.dumps(self.pin))
        self.assertEqual(HELPER["package_closure"](self.package, self.dependencies, self.root)["dependencyMembers"], 4)
        self.write_archive({**self.members, "fsgg-coord-engine.dll": b"rebuilt substitute"})
        with self.assertRaisesRegex(ValueError, "package changed published dependency"):
            HELPER["package_closure"](self.package, self.dependencies, self.root)

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
        for mutant in (original.replace('Engine.fsproj" />', 'Engine.fsproj" Private="false" />'),
                       original.replace('Engine.fsproj" />', 'Engine.fsproj"><Private>false</Private></ProjectReference>'),
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

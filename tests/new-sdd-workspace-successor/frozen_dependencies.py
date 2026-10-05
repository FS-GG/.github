"""Focused file-custody controls; synthetic DLL bytes are never executed."""
import hashlib
import json
import pathlib
import runpy
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
        self.pin = {"projects": {"scripts/NewSddWorkspace/NewSddWorkspace.fsproj": "new-sdd-workspace",
                                 "src/Engine/Engine.fsproj": "fsgg-coord-engine"},
                    "sourceLeaves": [{"path": "src/Engine/Engine.fsproj", "sha256": self.hash(self.project.read_bytes())}],
                    "archiveSha256": self.hash(self.package.read_bytes()), "sourceSha": "a" * 40,
                    "members": {name: self.hash(body) for name, body in self.members.items()}}
        self.dependencies = self.root / "frozen"

    @staticmethod
    def hash(body):
        return hashlib.sha256(body).hexdigest()

    def write_archive(self, members):
        with zipfile.ZipFile(self.package, "w") as archive:
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

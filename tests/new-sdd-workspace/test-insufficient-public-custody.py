#!/usr/bin/env python3
"""Pure literal-custody controls: never invoke a tool or Wizard."""
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import warnings
import zipfile

spec = importlib.util.spec_from_file_location('creation', Path(__file__).with_name('knowledge-creation.py'))
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)


class Custody(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.closure = self.root / 'tools' / 'closure'
        self.closure.mkdir(parents=True)
        names = ['FS.GG.SDD.Cli.dll', 'FS.GG.SDD.Commands.dll', 'FS.GG.SDD.Cli.deps.json',
                 'FS.GG.SDD.Cli.runtimeconfig.json', 'DotnetToolSettings.xml'] + [f'locale/{n}.dll' for n in range(31)]
        self.members = {'tools/net10.0/any/' + n: ('public:' + n).encode() for n in names}
        self.archive = self.root / 'public.nupkg'
        self.write_archive()
        for name, content in self.members.items():
            path = self.closure / name.removeprefix('tools/net10.0/any/')
            path.parent.mkdir(exist_ok=True)
            path.write_bytes(content)
        self.exe = self.root / 'tools' / 'fsgg-sdd'
        self.exe.write_bytes(b'\x7fELF\x00closure/FS.GG.SDD.Cli.dll\x00')
        self.m = {'insufficientExecutable': str(self.exe), 'inputs': {}, 'insufficientPublic': {
            'archive': str(self.archive), 'archiveSha256': h.sha(self.archive), 'sourceRevision': h.INSUFFICIENT_SOURCE,
            'closure': str(self.closure), 'apphostTarget': 'closure/FS.GG.SDD.Cli.dll',
            'literalPayloads': {n: __import__('hashlib').sha256(b).hexdigest() for n, b in self.members.items()}}}
        for path in [self.archive, self.exe] + [p for p in self.closure.rglob('*') if p.is_file()]:
            self.m['inputs'][str(path)] = h.sha(path)

    def write_archive(self, extra=None, source=None):
        with zipfile.ZipFile(self.archive, 'w') as z:
            z.writestr('FS.GG.SDD.Cli.nuspec', '<package><metadata><id>FS.GG.SDD.Cli</id><version>2.0.3</version><repository commit="' + (source or h.INSUFFICIENT_SOURCE) + '"/></metadata></package>')
            for name, data in self.members.items(): z.writestr(name, data)
            if extra is not None:
                with warnings.catch_warnings():
                    warnings.simplefilter('ignore', UserWarning)
                    z.writestr(extra, b'bad')

    def validate(self):
        return h.validate_insufficient_public(self.m, expected_archive_sha=self.m['insufficientPublic']['archiveSha256'])

    def repin_archive(self):
        self.m['inputs'][str(self.archive)] = h.sha(self.archive)
        self.m['insufficientPublic']['archiveSha256'] = h.sha(self.archive)

    def test_literal_archive_closure_and_target(self): self.validate()
    def test_old_manifest_has_no_public_binding(self):
        with self.assertRaisesRegex(ValueError, 'binding-required'): h.validate_insufficient_public({'inputs': {}})
    def test_production_pin_cannot_accept_another_archive(self):
        with self.assertRaisesRegex(ValueError, 'public-source'): h.validate_insufficient_public(self.m)
    def test_same_version_wrong_dll_even_when_input_repinned(self):
        p = self.closure / 'FS.GG.SDD.Commands.dll'; p.write_bytes(b'private2.0.3')
        self.m['inputs'][str(p)] = h.sha(p)
        with self.assertRaisesRegex(ValueError, 'closure-payload'): self.validate()
    def test_changed_deps_even_when_input_repinned(self):
        p = self.closure / 'FS.GG.SDD.Cli.deps.json'; p.write_bytes(b'changed core hash')
        self.m['inputs'][str(p)] = h.sha(p)
        with self.assertRaisesRegex(ValueError, 'closure-payload'): self.validate()
    def test_missing_member(self):
        (self.closure / 'DotnetToolSettings.xml').unlink()
        with self.assertRaisesRegex(ValueError, 'exclusive-closure'): self.validate()
    def test_extra_member(self):
        (self.closure / 'private.dll').write_bytes(b'extra')
        with self.assertRaisesRegex(ValueError, 'exclusive-closure'): self.validate()
    def test_symlink_member(self):
        p = self.closure / 'FS.GG.SDD.Commands.dll'; p.unlink(); p.symlink_to(self.exe)
        with self.assertRaisesRegex(ValueError, 'unsafe-input'): self.validate()
    def test_unsafe_archive_path(self):
        self.write_archive('../escape'); self.repin_archive()
        with self.assertRaisesRegex(ValueError, 'unsafe-member'): self.validate()
    def test_duplicate_archive_member(self):
        self.write_archive(next(iter(self.members))); self.repin_archive()
        with self.assertRaisesRegex(ValueError, 'duplicate-members'): self.validate()
    def test_wrong_source_same_version(self):
        self.write_archive(source='private-source'); self.repin_archive()
        with self.assertRaisesRegex(ValueError, 'package-source'): self.validate()
    def test_wrong_apphost_target_even_when_repinned(self):
        self.exe.write_bytes(b'\x7fELF\x00other/FS.GG.SDD.Cli.dll\x00'); self.m['inputs'][str(self.exe)] = h.sha(self.exe)
        with self.assertRaisesRegex(ValueError, 'apphost-target'): self.validate()
    def test_missing_pin(self):
        self.m['inputs'].pop(str(self.exe))
        with self.assertRaisesRegex(ValueError, 'input-pin'): self.validate()
    def test_closed_binding(self):
        self.m['insufficientPublic']['versionOnly'] = '2.0.3'
        with self.assertRaisesRegex(ValueError, 'binding-required'): self.validate()


if __name__ == '__main__': unittest.main(verbosity=2)

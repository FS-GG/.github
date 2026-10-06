"""Source integrity controls: none of these creates runtime custody or admission."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('custody_projection', ROOT / 'tools/sync-custody-lease-source.py')
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CustodyProjectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'receiver'
        self.root.mkdir()
        self.producer = Path(self.temp.name) / 'producer'
        self.producer.mkdir()
        self.lock = module.load_lock(ROOT)
        self.manifest = (ROOT / module.PROJECTION / module.NAMES[2]).read_bytes()
        for source, name in zip(module.SOURCE_PATHS, module.NAMES[:2]):
            path = self.producer / source
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes((ROOT / module.PROJECTION / name).read_bytes())
        path = self.producer / module.PRODUCER_MANIFEST
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(self.manifest)
        self.git('init', '-q')
        self.git('add', '.')
        self.git('-c', 'user.name=Source Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'fixture')
        self.lock['producerCommit'] = self.git('rev-parse', 'HEAD').decode().strip()
        path = self.root / module.LOCK
        path.parent.mkdir(parents=True, exist_ok=True)
        self.write_lock()
        (self.root / module.CLIENT / 'FS.GG.Telemetry.Client.fsproj').write_bytes((ROOT / module.CLIENT / 'FS.GG.Telemetry.Client.fsproj').read_bytes())

    def git(self, *args):
        return subprocess.check_output(['git', '-C', str(self.producer), *args], stderr=subprocess.PIPE)

    def write_lock(self):
        (self.root / module.LOCK).write_bytes(module.encode(self.lock))

    def project(self):
        return module.synchronize(self.root, self.producer)

    def test_exact_projection_and_local_integrity(self):
        self.project()
        self.assertEqual(module.synchronize(self.root, check=True), self.lock['producerCommit'])
        for source, name in zip(module.SOURCE_PATHS, module.NAMES[:2]):
            self.assertEqual((self.producer / source).read_bytes(), (self.root / module.PROJECTION / name).read_bytes())

    def test_working_tree_cannot_substitute_immutable_commit(self):
        (self.producer / module.SOURCE_PATHS[0]).write_bytes(b'bad live bytes')
        self.project()
        self.assertEqual((self.root / module.PROJECTION / module.NAMES[0]).read_bytes(), (ROOT / module.PROJECTION / module.NAMES[0]).read_bytes())

    def test_wrong_commit_preserves_absent_destination(self):
        self.lock['producerCommit'] = '0' * 40
        self.write_lock()
        with self.assertRaises(subprocess.CalledProcessError):
            self.project()
        self.assertFalse((self.root / module.PROJECTION).exists())

    def test_source_hash_mismatch_refuses(self):
        self.lock['manifest']['files'][0]['sha256'] = '0' * 64
        self.write_lock()
        with self.assertRaisesRegex(ValueError, 'manifest commitment'):
            self.project()

    def test_manifest_digest_mismatch_refuses(self):
        self.lock['manifestSha256'] = '0' * 64
        self.write_lock()
        with self.assertRaisesRegex(ValueError, 'manifest commitment'):
            self.project()

    def test_local_namespace_or_newline_rewrite_refuses(self):
        self.project()
        path = self.root / module.PROJECTION / module.NAMES[0]
        for data in (path.read_bytes().replace(b'namespace ', b'namespace Other.'), path.read_bytes().replace(b'\n', b'\r\n')):
            path.write_bytes(data)
            with self.assertRaisesRegex(ValueError, 'source commitment'):
                module.synchronize(self.root, check=True)

    def test_extra_projection_file_refuses(self):
        self.project()
        (self.root / module.PROJECTION / 'Other.fs').write_bytes(b'x')
        with self.assertRaisesRegex(ValueError, 'roster'):
            module.synchronize(self.root, check=True)

    def test_symlink_and_executable_source_refuse(self):
        self.project()
        path = self.root / module.PROJECTION / module.NAMES[0]
        original = path.read_bytes()
        path.chmod(0o755)
        with self.assertRaisesRegex(ValueError, 'mode'):
            module.synchronize(self.root, check=True)
        path.unlink()
        path.symlink_to(self.producer / module.SOURCE_PATHS[0])
        with self.assertRaises(OSError):
            module.synchronize(self.root, check=True)
        path.unlink()
        path.write_bytes(original)

    def test_duplicate_unknown_and_oversize_lock_refuse(self):
        path = self.root / module.LOCK
        examples = [b'{"schema":"a","schema":"b"}', module.encode({**self.lock, 'extra': 1}), b' ' * (module.CAP + 1)]
        for data in examples:
            path.write_bytes(data)
            with self.assertRaises(ValueError):
                self.project()

    def test_missing_fsi_and_compile_order_refuse(self):
        self.project()
        (self.root / module.PROJECTION / module.NAMES[0]).unlink()
        with self.assertRaisesRegex(ValueError, 'roster'):
            module.synchronize(self.root, check=True)
        path = self.root / module.CLIENT / 'FS.GG.Telemetry.Client.fsproj'
        path.write_text(path.read_text().replace('CustodyProjection/CustodyProcessLease.fsi', 'Other.fsi'))
        with self.assertRaisesRegex(ValueError, 'compilation order'):
            module.synchronize(self.root, check=True)

    def test_coordinate_clr_dependency_refuses(self):
        path = self.root / module.CLIENT / 'FS.GG.Telemetry.Client.fsproj'
        path.write_text(path.read_text().replace('</Project>', '<ItemGroup><PackageReference Include="Akka" /></ItemGroup></Project>'))
        with self.assertRaisesRegex(ValueError, 'CLR dependency'):
            self.project()

    def test_readonly_check_never_calls_git_or_rewrites_lock(self):
        self.project()
        before = (self.root / module.LOCK).read_bytes()
        with patch.object(module, 'git', side_effect=AssertionError('unexpected Git operation')):
            module.synchronize(self.root, check=True)
        self.assertEqual(before, (self.root / module.LOCK).read_bytes())

    def test_interrupted_stage_preserves_absent_destination_and_lock(self):
        before = (self.root / module.LOCK).read_bytes()
        with patch.object(module.os, 'rename', side_effect=OSError('staging interrupted')):
            with self.assertRaises(OSError):
                self.project()
        self.assertFalse((self.root / module.PROJECTION).exists())
        self.assertEqual(before, (self.root / module.LOCK).read_bytes())
        self.assertEqual(list((self.root / module.CLIENT).glob('.custody-projection-*')), [])

    def test_dirty_destination_is_not_overwritten(self):
        self.project()
        path = self.root / module.PROJECTION / module.NAMES[0]
        path.write_bytes(b'dirty')
        with self.assertRaises(ValueError):
            self.project()
        self.assertEqual(path.read_bytes(), b'dirty')

    def test_wrong_framework_roster_and_boolean_size_refuse(self):
        for field, value in [('targetFramework', 'net9.0'), ('namespace', 'Other')]:
            bad = json.loads(json.dumps(self.lock['manifest']))
            bad[field] = value
            with self.assertRaises(ValueError):
                module.validate_manifest(bad)
        for files in ([], self.lock['manifest']['files'] * 2):
            with self.assertRaises(ValueError):
                module.validate_manifest({**self.lock['manifest'], 'files': files})
        bad = json.loads(json.dumps(self.lock['manifest']))
        bad['files'][0]['bytes'] = True
        with self.assertRaises(ValueError):
            module.validate_manifest(bad)

    def test_git_symlink_mode_refuses(self):
        path = self.producer / module.SOURCE_PATHS[0]
        path.unlink()
        path.symlink_to('CustodyProcessLease.fs')
        self.git('add', '.')
        self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', 'symlink')
        self.lock['producerCommit'] = self.git('rev-parse', 'HEAD').decode().strip()
        self.write_lock()
        with self.assertRaisesRegex(ValueError, 'Git source mode'):
            self.project()

    def test_oversize_and_executable_git_blobs_refuse(self):
        path = self.producer / module.SOURCE_PATHS[0]
        original = path.read_bytes()
        for data, mode, message in [(b"x" * (module.CAP + 1), 0o644, "size"), (original, 0o755, "mode")]:
            path.write_bytes(data)
            path.chmod(mode)
            self.git('add', '.')
            self.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-qm', message)
            self.lock['producerCommit'] = self.git('rev-parse', 'HEAD').decode().strip()
            self.write_lock()
            with self.assertRaisesRegex(ValueError, message):
                self.project()

    def test_projection_directory_symlink_refuses(self):
        (self.root / module.PROJECTION).symlink_to(self.producer, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'roster'):
            self.project()

    def test_competing_complete_projection_is_not_overwritten(self):
        rename = module.os.rename
        def competing_rename(source, destination):
            destination.mkdir()
            (destination / 'Other.fs').write_bytes(b'other writer')
            return rename(source, destination)
        with patch.object(module.os, 'rename', side_effect=competing_rename):
            with self.assertRaises(OSError):
                self.project()
        self.assertEqual((self.root / module.PROJECTION / 'Other.fs').read_bytes(), b'other writer')
        self.assertEqual(list((self.root / module.CLIENT).glob('.custody-projection-*')), [])

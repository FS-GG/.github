#!/usr/bin/env python3
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).absolute().parents[2]
spec = importlib.util.spec_from_file_location('image_context', ROOT / 'deployment/telemetry-collector/prepare_native_image_context.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)


class ContextTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def write(self, name, raw=b'input'):
        path = self.root / name
        path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        path.write_bytes(raw)
        path.chmod(0o600)
        return path

    def archive(self, entries):
        path = self.root / 'package.nupkg'
        with zipfile.ZipFile(path, 'w') as archive:
            for name, raw in entries.items():
                archive.writestr(name, raw)
        return path

    def test_symlink_parent_refuses(self):
        target = self.root / 'target'
        target.mkdir()
        self.write('target/input')
        (self.root / 'alias').symlink_to(target)
        with self.assertRaisesRegex(ValueError, 'symlink'):
            m.bounded(self.root / 'alias/input')

    def test_shared_writable_refuses(self):
        path = self.write('shared')
        path.chmod(0o666)
        with self.assertRaisesRegex(ValueError, 'writable'):
            m.bounded(path)

    def test_digest_drift_refuses(self):
        with self.assertRaisesRegex(ValueError, 'digest differs'):
            m.pinned(self.write('input'), '0' * 64)

    def test_archive_traversal_refuses(self):
        with self.assertRaisesRegex(ValueError, 'unsafe archive'):
            m.members(self.archive({'tools/../escape': b'x'}), 'tools/')

    def test_archive_symlink_refuses(self):
        path = self.root / 'symlink.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            info = zipfile.ZipInfo('tools/link')
            info.external_attr = (stat.S_IFLNK | 0o777) << 16
            archive.writestr(info, '../target')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            m.members(path, 'tools/')

    def distribution(self):
        package = self.archive({'tools/net10.0/any/fsgg-coord-engine.dll': b'dll', 'tools/net10.0/any/runtimes/linux-x64/native/sqlite.so': b'native'})
        distribution = self.root / 'distribution'
        for name, raw in m.members(package, 'tools/net10.0/any/').items():
            self.write('distribution/' + name, raw)
        engine = self.write('distribution/fsgg-coord-engine', b'publisher executable')
        marker = self.write('distribution/coherent-content.sha256', (m.COORD_CONTENT + '\n').encode())
        evidence = {'schema': 'fsgg.coord.publisher-distribution/1', 'sourceSha': m.COORD_SOURCE, 'contentSha256': m.COORD_CONTENT, 'executableSha256': m.digest(engine), 'markerSha256': m.digest(marker)}
        provenance = self.write('publisher.json', json.dumps(evidence).encode())
        return distribution, package, m.digest(engine), provenance, m.digest(provenance)

    def test_full_publisher_distribution_passes(self):
        inputs = self.distribution()
        self.assertEqual(len(m.verify_distribution(*inputs)), 4)

    def test_apphost_only_refuses(self):
        inputs = self.distribution()
        (inputs[0] / 'fsgg-coord-engine.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'full publisher distribution'):
            m.verify_distribution(*inputs)

    def test_dependency_drift_refuses(self):
        inputs = self.distribution()
        (inputs[0] / 'fsgg-coord-engine.dll').write_bytes(b'rebuild')
        with self.assertRaisesRegex(ValueError, 'dependency bytes'):
            m.verify_distribution(*inputs)

    def test_marker_drift_refuses(self):
        inputs = self.distribution()
        (inputs[0] / 'coherent-content.sha256').write_text('0' * 64)
        with self.assertRaisesRegex(ValueError, 'marker differs'):
            m.verify_distribution(*inputs)

    def test_availability_packet_is_private_and_does_not_make_context(self):
        args = argparse.Namespace(host_served='/unused/host', coord_packages='/unused/coord', coord_manifest='/unused/manifest', codex='/unused/codex', output=str(self.root / 'packet'), coord_distribution=None, receiver_cert=None, native_source=None)
        with patch.object(m, 'verify_public_artifacts', return_value={'verified': True}):
            result = m.prepare(args)
        self.assertFalse(result['contextPrepared'])
        self.assertEqual(len(result['gaps']), 3)
        self.assertFalse((Path(args.output) / 'context').exists())
        self.assertEqual(Path(args.output).stat().st_mode & 0o777, 0o700)
        self.assertEqual((Path(args.output) / 'availability.json').stat().st_mode & 0o777, 0o600)

    def test_complete_context_emits_exact_image_arguments(self):
        source = self.root / 'source'
        pins = {}
        for name in m.SOURCE_ARGS:
            pins[name] = m.digest(self.write('source/' + name, name.encode()))
        pin_file = self.write('pins.json', json.dumps(pins).encode())
        distribution = self.root / 'distribution'
        self.write('distribution/fsgg-coord-engine', b'engine')
        codex = self.write('codex', b'ELF')
        cert = self.write('receiver.crt', b'-----BEGIN CERTIFICATE-----\nAA==\n-----END CERTIFICATE-----\n')
        host = self.root / 'host'
        host.mkdir()
        with zipfile.ZipFile(host / 'package.nupkg', 'w') as archive:
            archive.writestr('tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll', b'host')
        args = argparse.Namespace(host_served=str(host), coord_packages=str(self.root), coord_manifest='/unused/manifest', codex=str(codex), output=str(self.root / 'complete'), coord_distribution=str(distribution), coord_provenance='/unused/provenance', coord_provenance_sha256='a' * 64, coord_engine_sha256=m.digest(distribution / 'fsgg-coord-engine'), receiver_cert=str(cert), native_source=str(source), native_source_pins=str(pin_file), native_source_revision='b' * 40)
        def committed(command, **kwargs):
            name = command[-1].split('/')[-1]
            return argparse.Namespace(stdout=name.encode())
        with patch.object(m, 'verify_public_artifacts', return_value={}), patch.object(m, 'verify_distribution', return_value={'fsgg-coord-engine'}), patch.object(m.ssl, 'PEM_cert_to_DER_cert'), patch.object(m.subprocess, 'run', side_effect=committed):
            result = m.prepare(args)
        self.assertTrue(result['contextPrepared'])
        packet = json.loads((Path(args.output) / 'manifest.json').read_text())
        expected = set(m.SOURCE_ARGS.values()) | {'NATIVE_ELF_SHA256', 'COORD_ENGINE_SHA256', 'RECEIVER_TRUST_CERT_SHA256', 'COORD_SOURCE_SHA', 'COORD_PAYLOAD_SHA256'}
        self.assertEqual(set(packet['buildArguments']['native-development']), expected)
        self.assertEqual(set(packet['buildArguments']['native-collector']), {'NATIVE_ELF_SHA256', 'HOST_VERSION', 'HOST_SOURCE_SHA', 'HOST_PACKAGE_SHA256'})
        self.assertIn('collector/host/FS.GG.Telemetry.Host.dll', packet['files'])
        self.assertEqual((Path(args.output) / 'context/native/codex').stat().st_mode & 0o777, 0o500)

    def test_private_executable_copy_mode(self):
        path = self.root / 'context/native/codex'
        m.write_private(path, b'ELF', 0o500)
        self.assertEqual(path.stat().st_mode & 0o777, 0o500)
        self.assertEqual(path.parent.stat().st_mode & 0o777, 0o700)


if __name__ == '__main__':
    unittest.main()

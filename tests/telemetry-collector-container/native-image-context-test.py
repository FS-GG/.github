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

    def distribution(self, extras=None):
        entries = {'tools/net10.0/any/fsgg-coord-engine.dll': b'dll', 'tools/net10.0/any/fsgg-coord-engine.deps.json': b'deps', 'tools/net10.0/any/fsgg-coord-engine.runtimeconfig.json': b'runtimeconfig', 'tools/net10.0/any/runtimes/linux-x64/native/sqlite.so': b'native'}
        entries.update(extras or {})
        return self.archive(entries)

    def test_packaged_distribution_preserves_every_byte(self):
        package = self.distribution()
        before = m.members(package, 'tools/net10.0/any/')
        after = m.packaged_distribution(package)
        self.assertEqual(len(after), len(before) + 2)
        for name, raw in before.items():
            self.assertEqual(after[name], raw)
        self.assertEqual(after['fsgg-coord-engine'], b'#!/bin/sh\nexec /usr/bin/dotnet /opt/fsgg/coord/fsgg-coord-engine.dll "$@"\n')
        self.assertEqual(after['coherent-content.sha256'], (m.COORD_CONTENT + '\n').encode())

    def test_packaged_runtime_incomplete_refuses(self):
        with self.assertRaisesRegex(ValueError, 'runtime incomplete'):
            m.packaged_distribution(self.archive({'tools/net10.0/any/fsgg-coord-engine.dll': b'dll'}))

    def test_publisher_launcher_collision_refuses(self):
        with self.assertRaisesRegex(ValueError, 'collides'):
            m.packaged_distribution(self.distribution({'tools/net10.0/any/fsgg-coord-engine': b'apphost'}))

    def test_publisher_marker_collision_refuses(self):
        with self.assertRaisesRegex(ValueError, 'collides'):
            m.packaged_distribution(self.distribution({'tools/net10.0/any/coherent-content.sha256': b'marker'}))

    def test_availability_packet_is_private_and_does_not_make_context(self):
        args = argparse.Namespace(host_served='/unused/host', coord_packages='/unused/coord', coord_manifest='/unused/manifest', coord_stable='/unused/stable', codex='/unused/codex', output=str(self.root / 'packet'), coord_distribution=None, receiver_cert=None, native_source=None)
        with patch.object(m, 'verify_public_artifacts', return_value={'verified': True}):
            result = m.prepare(args)
        self.assertFalse(result['contextPrepared'])
        self.assertEqual(len(result['gaps']), 2)
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
        args = argparse.Namespace(host_served=str(host), coord_packages=str(self.root), coord_manifest='/unused/manifest', coord_stable='/unused/stable', codex=str(codex), output=str(self.root / 'complete'), coord_distribution=str(distribution), coord_provenance='/unused/provenance', coord_provenance_sha256='a' * 64, coord_engine_sha256=m.digest(distribution / 'fsgg-coord-engine'), receiver_cert=str(cert), native_source=str(source), native_source_pins=str(pin_file), native_source_revision='b' * 40)
        def committed(command, **kwargs):
            name = command[-1].split('/')[-1]
            return argparse.Namespace(stdout=name.encode())
        with patch.object(m, 'verify_public_artifacts', return_value={'coordManifestSha256': 'a' * 64}), patch.object(m, 'packaged_distribution', return_value={'fsgg-coord-engine': m.COORD_LAUNCHER, 'coherent-content.sha256': (m.COORD_CONTENT + '\n').encode()}), patch.object(m.ssl, 'PEM_cert_to_DER_cert'), patch.object(m.subprocess, 'run', side_effect=committed):
            result = m.prepare(args)
        self.assertTrue(result['contextPrepared'])
        self.assertEqual(result['contextPath'], str(Path(args.output) / 'context'))
        packet = json.loads((Path(args.output) / 'manifest.json').read_text())
        self.assertFalse(packet['adapterRuntimeQualified'])
        self.assertFalse(packet['coordDerivedInputs']['launcher']['publisherBytes'])
        self.assertFalse(packet['coordDerivedInputs']['contentProjection']['publisherBytes'])
        expected = set(m.SOURCE_ARGS.values()) | {'NATIVE_ELF_SHA256', 'COORD_ENGINE_SHA256', 'RECEIVER_TRUST_CERT_SHA256', 'COORD_SOURCE_SHA', 'COORD_PAYLOAD_SHA256'}
        self.assertEqual(set(packet['buildArguments']['native-development']), expected)
        self.assertEqual(set(packet['buildArguments']['native-collector']), {'NATIVE_ELF_SHA256', 'HOST_VERSION', 'HOST_SOURCE_SHA', 'HOST_PACKAGE_SHA256'})
        self.assertIn('collector/host/FS.GG.Telemetry.Host.dll', packet['files'])
        self.assertEqual((Path(args.output) / 'context/native/codex').stat().st_mode & 0o777, 0o500)

    def test_vacuous_host_journal_refuses(self):
        release = {'producerPayloadSha256': 'sha256:' + 'b' * 64}
        journal = {'schema': 'fsgg.telemetry-host-release-journal/v1', 'manifestSha256': 'sha256:' + 'a' * 64, 'observations': {'github': {}, 'nuget': {}}}
        with self.assertRaisesRegex(ValueError, 'identity missing'):
            m.validate_host_journal(release, journal, 'a' * 64, {})

    def test_host_journal_unbound_manifest_refuses(self):
        with self.assertRaisesRegex(ValueError, 'manifest binding'):
            m.validate_host_journal({}, {'schema': 'fsgg.telemetry-host-release-journal/v1', 'manifestSha256': 'sha256:' + 'b' * 64}, 'a' * 64, {})

    def test_private_executable_copy_mode(self):
        path = self.root / 'context/native/codex'
        m.write_private(path, b'ELF', 0o500)
        self.assertEqual(path.stat().st_mode & 0o777, 0o500)
        self.assertEqual(path.parent.stat().st_mode & 0o777, 0o700)


if __name__ == '__main__':
    unittest.main()

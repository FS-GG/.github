#!/usr/bin/env python3
"""Constructed allowed-caller sink overlap; no deployed allocation measurement."""
import base64
import importlib.util
import inspect
import io
import json
import pathlib
import stat
import sys
import types
import unittest
from unittest.mock import patch

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import release_successor_provider as provider


def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise AssertionError('sink copy controls forbid network/processes')
sys.addaudithook(audit)

spec = importlib.util.spec_from_file_location('sink_copy_publisher', ROOT / 'scripts/release-successor-publish.py')
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


def inputs():
    vectors = json.loads((HERE / 'callback-vectors.json').read_bytes())
    binding = vectors['binding']
    frames = []
    for exchange in vectors['exchanges']:
        request = bytes.fromhex(exchange['request'])
        row = json.loads(request[4:])
        if row['kind'] == 'validate':
            raw = base64.b64decode(row['frameBase64'], validate=True)
            decoded = provider.decode_auth_frame(raw)
            # Exercise the current encoder/validator, rather than a size-only fixture.
            actual = provider.encode_auth_frame(binding, decoded['n'], decoded['k'], decoded['p'])
            if actual != raw:
                raise AssertionError('fixture differs from current serialization')
            frames.append(actual)
    return binding, frames


class InertPath:
    """Minimal path protocol; all file content lives in distinct bytearray stores."""
    def __init__(self, fs, name): self.fs, self.name = fs, name
    @property
    def parent(self): return self
    def with_name(self, name): return InertPath(self.fs, name)
    def exists(self): return self.name in self.fs.files
    def is_symlink(self): return False
    def stat(self): return types.SimpleNamespace(st_mode=stat.S_IFREG | 0o600)
    def open(self, mode): return self.fs.open(self.name, mode)


class InertFS:
    def __init__(self):
        self.files = {}
        self.writes = 0
        self.cut = None
        self.corrupt_readback = False
        self.fail_replace = False
        self.events = []
        self.os = types.SimpleNamespace(O_RDONLY=0, O_DIRECTORY=0, open=lambda *a: 7,
            close=lambda *a: None, chmod=lambda *a: None, fsync=self.fsync, replace=self.replace)
        self.fcntl = types.SimpleNamespace(LOCK_EX=1, flock=lambda *a: None)

    def fsync(self, descriptor): self.events.append('fsync')

    def open(self, name, mode):
        fs = self
        if mode == 'rb':
            # bytes(bytearray) is a physical byte copy, not an alias of file storage.
            raw = bytes(self.files[name])
            if self.corrupt_readback and self.cut is not None:
                raw = b'!' + raw[1:]
            return io.BytesIO(raw)
        if mode != 'xb' or name in self.files: raise AssertionError('unexpected file operation')
        self.files[name] = bytearray()
        class Writer:
            def __enter__(self): return self
            def __exit__(self, *args): return False
            def write(self, body):
                fs.files[name].extend(body)
                fs.writes += 1
                return len(body)
            def flush(self): fs.events.append('flush')
            def fileno(self): return 8
        return Writer()

    def replace(self, temporary, path):
        local = inspect.currentframe().f_back.f_locals
        if 'previous_bytes' in local:
            # References preserve identity; they do not create more counted byte copies.
            self.cut = {'old_file': self.files[path.name],
                        'temp_file': self.files[temporary.name],
                        'previous_bytes': local['previous_bytes'],
                        'body': local['body'], 'frame': local['frame']}
            self.events.append('pre-replace')
            if self.fail_replace: raise OSError('inert replace outcome uncertain')
        self.files[path.name] = self.files.pop(temporary.name)


class SinkCopyContractTests(unittest.TestCase):
    def prepared(self, padding=14984):
        binding, frames = inputs()
        self.assertEqual([len(raw) for raw in frames[:2]], [1141, 972])
        fs = InertFS()
        report = {'schema': 'fsgg.release-successor-result/1', 'padding': 'x' * padding}
        return fs, InertPath(fs, 'result.json'), report, binding, frames

    def invoke(self, fs, path, report, binding, frame):
        with patch.object(publisher, 'os', fs.os), patch.object(publisher, 'fcntl', fs.fcntl):
            return publisher.persist_authentication_snapshot(path, report, binding, frame)

    def ready(self, padding=14984):
        fs, path, report, binding, frames = self.prepared(padding)
        self.invoke(fs, path, report, binding, frames[0])
        return fs, path, report, binding, frames

    def test_actual_sink_overlap_refuses_fixed_slot_fit_claim(self):
        fs, path, report, binding, frames = self.ready()
        receipt = self.invoke(fs, path, report, binding, frames[1])
        sizes = {key: len(value) for key, value in fs.cut.items()}
        self.assertEqual(sizes, {'old_file': 16384, 'temp_file': 16215,
            'previous_bytes': 16384, 'body': 16215, 'frame': 972})
        self.assertEqual(len({id(value) for value in fs.cut.values()}), 5)
        self.assertIsInstance(fs.cut['old_file'], bytearray)
        self.assertIsInstance(fs.cut['temp_file'], bytearray)
        for key in ('previous_bytes', 'body', 'frame'): self.assertIsInstance(fs.cut[key], bytes)
        self.assertEqual(fs.cut['old_file'], fs.cut['previous_bytes'])
        self.assertEqual(fs.cut['temp_file'], fs.cut['body'])
        self.assertEqual(fs.events[-3:], ['fsync', 'pre-replace', 'fsync'])
        self.assertTrue(all(sizes[key] <= 16384 for key in ('old_file', 'temp_file', 'body', 'previous_bytes')))
        self.assertEqual(sum(sizes.values()), 66170)
        self.assertGreater(sum(sizes.values()), 65536)
        # The production sink accepts its per-report bound. Policy admission
        # cannot infer fixed-slot fit from that acceptance.
        self.assertEqual(receipt['ordinal'], 2)
        self.assertEqual(json.loads(fs.files[path.name])['authenticationSession'], report['authenticationSession'])

    def test_report_limit_still_refuses_before_creating_a_file(self):
        fs, path, report, binding, frames = self.prepared(14985)
        with self.assertRaisesRegex(publisher.Refused, 'exceeds existing result bound'):
            self.invoke(fs, path, report, binding, frames[0])
        self.assertEqual(fs.files, {})
        self.assertNotIn('authenticationSession', report)

    def test_wrong_order_preserves_ready_and_does_not_write(self):
        fs, path, report, binding, frames = self.ready()
        old = bytes(fs.files[path.name])
        writes = fs.writes
        with self.assertRaisesRegex(publisher.Refused, 'snapshot order differs'):
            self.invoke(fs, path, report, binding, frames[2])
        self.assertEqual(fs.files[path.name], old)
        self.assertEqual(fs.writes, writes)
        self.assertEqual(report['authenticationSession']['ordinal'], 1)

    def test_replace_failure_retains_distinct_old_temp_and_prior_report(self):
        fs, path, report, binding, frames = self.ready()
        fs.fail_replace = True
        with self.assertRaisesRegex(OSError, 'outcome uncertain'):
            self.invoke(fs, path, report, binding, frames[1])
        self.assertEqual(len(fs.files), 2)
        self.assertEqual(len(fs.files[path.name]), 16384)
        self.assertEqual(len(fs.files[path.name + '.authentication-pending']), 16215)
        self.assertEqual(report['authenticationSession']['ordinal'], 1)
        # Existing candidate prevents a replacement attempt; this is a refusal
        # control only and supplies no retry authority.

    def test_failed_readback_returns_no_receipt_and_preserves_reconciliation_hold(self):
        fs, path, report, binding, frames = self.ready()
        fs.corrupt_readback = True
        with self.assertRaisesRegex(publisher.Refused, 'retained readback differs'):
            self.invoke(fs, path, report, binding, frames[1])
        self.assertEqual(report['authenticationSession']['ordinal'], 1)
        self.assertEqual(json.loads(fs.files[path.name])['authenticationSession']['ordinal'], 2)
        fs.corrupt_readback = False
        writes = fs.writes
        with self.assertRaisesRegex(publisher.Refused, 'durable prior differs'):
            self.invoke(fs, path, report, binding, frames[1])
        self.assertEqual(fs.writes, writes)
        self.assertEqual(report['authenticationSession']['ordinal'], 1)


if __name__ == '__main__': unittest.main()

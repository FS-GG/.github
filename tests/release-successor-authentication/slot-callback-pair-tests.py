#!/usr/bin/env python3
"""Closed inert slot pairing and controlled entry tests; no process/network use."""
import base64
import copy
import hashlib
import importlib.util
import io
import json
import pathlib
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import callback_pair_profile as pair


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


entry = load('slot_callback_entry_tests', HERE / 'slot-sink-callback-entry.py')


def generate():
    pair.select(ROOT, pair.SLOT_PROFILE)
    producer = load('slot_pair_actual_producer', HERE / 'run.py')
    vectors = producer.protocol_vectors(pair.SLOT_PROFILE)
    control = producer.CallbackOwnerControls()
    binding = vectors['binding']
    requests = [control.request(binding, 1, 'bind')]
    for index, rawhex in enumerate(vectors['children']):
        raw = bytes.fromhex(rawhex)
        phase = producer.provider.decode_auth_frame(raw)
        for offset, kind in ((2, 'validate'), (3, 'retain')):
            requests.append(control.request(binding, index * 2 + offset, kind, raw, phase))
    output = io.BytesIO()
    evidence = entry.execute(binding, binding['clock'], io.BytesIO(b''.join(requests)), output)
    raw = output.getvalue(); responses = []
    while raw:
        count = 4 + int.from_bytes(raw[:4], 'big')
        responses.append(raw[:count]); raw = raw[count:]
    if len(requests) != len(responses):
        raise AssertionError('slot callback exchange population differs')
    return {'schema': 'fsgg.authentication-callback-vectors/1',
        'qualification': 'inert typed slot callback pairing; actual producer/decoder/owner/controlled sink; synthetic evidence only',
        'binding': binding,
        'exchanges': [{'request': q.hex(), 'response': r.hex()} for q, r in zip(requests, responses)],
        'finalReadback': {'binding': evidence['binding'], 'ordinal': 6,
                          'frameDigest': evidence['slots'][-1]['sha256']},
        'slotEvidence': evidence}


class NoEofInput:
    def __init__(self, raw, fragment=4096):
        self.raw = io.BytesIO(raw); self.reads = 0; self.fragment = fragment
    def read(self, count):
        self.reads += 1
        raw = self.raw.read(min(count, self.fragment))
        if not raw:
            raise AssertionError('entry waited for EOF/child-exit deadlock')
        return raw


class SlotCallbackPairControls(unittest.TestCase):
    def frozen(self):
        return json.loads(pair.select(ROOT, pair.SLOT_PROFILE)[1].read_bytes())

    def test_slot_callback_pair_manifest_closes_sixteen_roles_and_sources(self):
        manifest, _ = pair.select(ROOT, pair.SLOT_PROFILE)
        self.assertEqual(len(manifest['files']), 16)
        self.assertEqual(manifest['bundleDigest'], pair.SLOT_BUNDLE)
        old = json.loads((HERE / 'callback-pair-manifest.json').read_bytes())
        variants = [old]
        for key, value in (('ownerBase', '0' * 40), ('producerRevision', '0' * 40),
                           ('bundleDigest', '0' * 64), ('schema', 'unknown')):
            changed = copy.deepcopy(manifest); changed[key] = value; variants.append(changed)
        for files in (manifest['files'][:-1], manifest['files'] + manifest['files'][:1],
                      list(reversed(manifest['files']))):
            changed = copy.deepcopy(manifest); changed['files'] = files; variants.append(changed)
        for index in range(16):
            changed = copy.deepcopy(manifest); changed['files'][index]['sha256'] = '0' * 64
            variants.append(changed)
        for changed in variants:
            with self.subTest(manifest=changed), patch.object(pathlib.Path, 'read_bytes', return_value=json.dumps(changed).encode()):
                with self.assertRaises(ValueError): pair.select(ROOT, pair.SLOT_PROFILE)
        original = pathlib.Path.read_bytes
        for source in pair.SLOT_GITHUB_ROLES:
            def altered(path):
                raw = original(path)
                return raw + b'\n# source mutation\n' if path == ROOT / source else raw
            with self.subTest(source=source), patch.object(pathlib.Path, 'read_bytes', altered):
                with self.assertRaisesRegex(ValueError, 'source mismatch'): generate()
        with self.assertRaises(ValueError): pair.select(ROOT, 'inert-slot-callback-pair/1')

    def test_slot_callback_pair_regenerates_real_six_slot_journey_without_eof(self):
        frozen = self.frozen()
        self.assertEqual(generate(), frozen)
        raw = b''.join(bytes.fromhex(row['request']) for row in frozen['exchanges'])
        for fragment in (1, 4096):
            input_stream = NoEofInput(raw + b'queued-trailing-input', fragment)
            output = io.BytesIO()
            evidence = entry.execute(frozen['binding'], frozen['binding']['clock'], input_stream, output)
            self.assertEqual(evidence, frozen['slotEvidence'])
            self.assertEqual(output.getvalue(), b''.join(bytes.fromhex(r['response']) for r in frozen['exchanges']))
            self.assertEqual(input_stream.raw.read(), b'queued-trailing-input')
            self.assertFalse(evidence['inputEofObserved']); self.assertFalse(evidence['trailingInputChecked'])
            self.assertFalse(evidence['operationalReceipt'])
            for slot in evidence['slots']:
                frame = bytes.fromhex(slot['bytes'])
                self.assertEqual(hashlib.sha256(frame).hexdigest(), slot['sha256'])
                self.assertEqual(entry.provider.decode_auth_frame(frame)['n'], slot['ordinal'])

    def test_historical_v1_binding_keeps_its_exact_child_and_callback_wire_bytes(self):
        # Frozen hashes measured from protected ed58ca52 /1 vectors, not /2.
        # Selectors still refuse /1 here; only this explicit compatibility control
        # reconstructs its historical source binding with the maintained codec.
        old_bundle = 'b73ce545011cc1156bbcf49f2bb98f174e33b970d221b32c0bad6e83b21b5862'
        producer = load('slot_pair_historical_codec', HERE / 'run.py')
        with patch.object(producer, 'contract_bundle_digest', return_value=old_bundle):
            vectors = producer.protocol_vectors('inert-slot-callback-pair/1')
        binding = vectors['binding']; control = producer.CallbackOwnerControls()
        requests = [control.request(binding, 1, 'bind')]
        for index, rawhex in enumerate(vectors['children']):
            raw = bytes.fromhex(rawhex); phase = producer.provider.decode_auth_frame(raw)
            for offset, kind in ((2, 'validate'), (3, 'retain')):
                requests.append(control.request(binding, index * 2 + offset, kind, raw, phase))
        output = io.BytesIO()
        evidence = entry.execute(binding, binding['clock'], io.BytesIO(b''.join(requests)), output)
        self.assertEqual(hashlib.sha256(b''.join(requests)).hexdigest(),
            'f66fb293743c1225038514cd483d2ba0cd7f8a2ab684cf19513bdb757a775b31')
        self.assertEqual(hashlib.sha256(output.getvalue()).hexdigest(),
            'dde1440ade9f322b0bd351945793c06c62d1658b1582d6f6922491bf49f1fb52')
        self.assertEqual(hashlib.sha256(b''.join(bytes.fromhex(row['bytes']) for row in evidence['slots'])).hexdigest(),
            '583eb273f0d865f834eb5d2c067496f181851d2dfcb4f47c6c6d036027369e59')
        self.assertEqual(evidence['profile'], pair.SLOT_PROFILE)
        self.assertNotEqual(binding['source']['contract'], self.frozen()['binding']['source']['contract'])

    def test_slot_callback_entry_refuses_incomplete_short_write_flush_and_backend_cuts(self):
        frozen = self.frozen(); binding = frozen['binding']
        raw = b''.join(bytes.fromhex(row['request']) for row in frozen['exchanges'])
        for invalid in (b'', b'\0\0', (4097).to_bytes(4, 'big'), b'\0\0\0\0', raw[:-1]):
            with self.subTest(invalid=invalid[:8]), self.assertRaises(entry.slots.Refused):
                entry.execute(binding, binding['clock'], io.BytesIO(invalid), io.BytesIO())
        for stage in ('write', 'short', 'bool', 'flush'):
            class FailingOutput(io.BytesIO):
                def write(self, data):
                    if stage == 'write': raise OSError('private failure must be sanitized')
                    if stage == 'short': return len(data) - 1
                    if stage == 'bool': return True
                    return super().write(data)
                def flush(self):
                    if stage == 'flush': raise OSError('private flush failure')
            with self.subTest(stage=stage), self.assertRaises(entry.slots.Refused):
                entry.execute(binding, binding['clock'], io.BytesIO(raw), FailingOutput())
        # Terminal response loss must not report successful evidence even after
        # all six slot writes succeeded. There is no fallback or replay.
        for failure in ('short', 'flush', 'confirmation'):
            backend = entry.slots.ControlledSlotBackend()
            class TerminalOutput(io.BytesIO):
                count = 0
                def write(self, data):
                    self.count += 1
                    if self.count == 13 and failure == 'short': return len(data) - 1
                    return super().write(data)
                def flush(self):
                    if self.count == 13 and failure == 'flush': raise OSError('terminal flush lost')
            original_confirm = entry.consumer.InertCallbackRequestDecoder.confirm_response_written
            calls = []
            def confirm(decoder, count):
                calls.append(count)
                if len(calls) == 13 and failure == 'confirmation': raise OSError('confirmation refused')
                return original_confirm(decoder, count)
            with self.subTest(terminal=failure), patch.object(entry.consumer.InertCallbackRequestDecoder, 'confirm_response_written', confirm):
                with self.assertRaises(entry.slots.Refused):
                    entry.execute(binding, binding['clock'], io.BytesIO(raw), TerminalOutput(), backend)
            self.assertEqual(len([key for key in backend.stores if key.startswith('phase-')]), 6)
            with self.assertRaises(entry.slots.Refused):
                entry.execute(binding, binding['clock'], io.BytesIO(raw), io.BytesIO(), backend)
        backend = entry.slots.ControlledSlotBackend()
        backend.cut = 'write'
        with self.assertRaises(entry.slots.Refused):
            entry.execute(binding, binding['clock'], io.BytesIO(raw), io.BytesIO(), backend)
        self.assertIsNotNone(backend.reservation)
        with self.assertRaises(entry.slots.Refused):
            entry.execute(binding, binding['clock'], io.BytesIO(raw), io.BytesIO(), backend)

    def test_slot_callback_pair_runtime_import_closure_is_complete(self):
        import ast
        expected = {ROOT / path for path in pair.SLOT_GITHUB_ROLES if path.endswith('.py')}
        pending = [HERE / 'slot-sink-callback-entry.py']; found = set()
        while pending:
            path = pending.pop()
            if path in found: continue
            found.add(path)
            tree = ast.parse(path.read_bytes()); names = set()
            for node in ast.walk(tree):
                if isinstance(node, ast.Import): names.update(alias.name for alias in node.names)
                if isinstance(node, ast.ImportFrom) and node.module: names.add(node.module)
                if (isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
                        and node.func.id == '__import__' and node.args and isinstance(node.args[0], ast.Constant)):
                    names.add(node.args[0].value)
            for name in names:
                for directory in (HERE, ROOT / 'scripts'):
                    candidate = directory / (name.replace('.', '/') + '.py')
                    if candidate.exists(): pending.append(candidate)
            # The entry's one explicit importlib path is part of the closure.
            if path.name == 'slot-sink-callback-entry.py':
                self.assertIn('slot-sink-fixture.py', path.read_text())
                pending.append(HERE / 'slot-sink-fixture.py')
        self.assertEqual(found, expected)
        self.assertIn(ROOT / 'scripts/fsgg_feed.py', found)
        self.assertIn(ROOT / 'scripts/release_successor_execution.py', found)

    def test_slot_callback_entry_cli_emits_closed_bounded_synthetic_evidence(self):
        frozen = self.frozen(); raw = b''.join(bytes.fromhex(r['request']) for r in frozen['exchanges'])
        with tempfile.TemporaryDirectory() as root:
            destination = pathlib.Path(root) / 'evidence.json'
            args = ['slot-sink-callback-entry.py', base64.b64encode(entry.provider._auth_canonical(frozen['binding'])).decode(),
                    base64.b64encode(entry.provider._auth_canonical(frozen['binding']['clock'])).decode(), str(destination)]
            output = io.BytesIO()
            with patch.object(sys, 'argv', args), patch.object(sys, 'stdin', types.SimpleNamespace(buffer=NoEofInput(raw))), \
                 patch.object(sys, 'stdout', types.SimpleNamespace(buffer=output)):
                self.assertEqual(entry.main(), 0)
            self.assertEqual(json.loads(destination.read_bytes()), frozen['slotEvidence'])
            self.assertLessEqual(destination.stat().st_size, entry.EVIDENCE_MAXIMUM)
            before = destination.read_bytes(); errors = io.StringIO()
            with patch.object(sys, 'argv', args), patch.object(sys, 'stdin', types.SimpleNamespace(buffer=NoEofInput(raw))), \
                 patch.object(sys, 'stdout', types.SimpleNamespace(buffer=io.BytesIO())), patch.object(sys, 'stderr', errors):
                self.assertEqual(entry.main(), 3)
            self.assertEqual(errors.getvalue(), 'slot-sink-callback-refused\n')
            self.assertEqual(destination.read_bytes(), before)


if __name__ == '__main__': unittest.main()

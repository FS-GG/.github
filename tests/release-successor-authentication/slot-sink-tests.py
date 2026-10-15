#!/usr/bin/env python3
"""Actual callback owner with the controlled typed sink; no external effects."""
import base64
import copy
import dataclasses
import hashlib
import json
import pathlib
import sys
import tempfile
import unittest

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / 'scripts'))
import inert_phase_slot_sink as sink
import release_successor_auth_consumer as consumer
import release_successor_provider as provider


def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise AssertionError('inert slot controls forbid network/processes')
sys.addaudithook(audit)


def inputs():
    vectors = json.loads((HERE / 'callback-vectors.json').read_bytes())
    frames = []
    for exchange in vectors['exchanges']:
        row = provider.decode_auth_frame(bytes.fromhex(exchange['request']), maximum=4096)
        if row['kind'] == 'validate':
            raw = base64.b64decode(row['frameBase64'], validate=True)
            decoded = provider.decode_auth_frame(raw)
            assert provider.encode_auth_frame(vectors['binding'], decoded['n'], decoded['k'], decoded['p']) == raw
            frames.append(raw)
    return vectors['binding'], frames


class SlotSinkTests(unittest.TestCase):
    def prepared(self, path='inert-slot', backend=None, **changes):
        binding, frames = inputs()
        backend = backend or sink.ControlledSlotBackend()
        identity = {'path': str(path), 'binding': provider.validate_auth_binding(binding),
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        args = dict(independently_expected_binding=binding, original_sink_identity=identity,
                    closed_report_envelope=copy.deepcopy(sink.REPORT), original_window=binding['clock'],
                    reserved_population=sink.population(), slot_backend=backend)
        args.update(changes)
        return sink.InertPhaseSlotSink.prepare(**args), binding, frames, backend

    def test_actual_owner_bind_validate_retain_six_frames(self):
        vectors = json.loads((HERE / 'callback-vectors.json').read_bytes())
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'unused-inert-destination'
            subject, binding, frames, backend = self.prepared(path)
            report = copy.deepcopy(sink.REPORT)
            clock = consumer.InertCallbackClock(binding['clock']['domain'], 1000, 0,
                                               270000, 285000, 300000, lambda: 1)
            owner = consumer.InertCallbackOwner(binding, path, report, subject, clock)
            for index, exchange in enumerate(vectors['exchanges']):
                response = provider.decode_auth_frame(owner.handle(bytes.fromhex(exchange['request'])))
                self.assertEqual(response['kind'], 'bound' if index == 0 else
                                 'validated' if index % 2 else 'retained')
                self.assertEqual(subject.ordinal, index // 2)
            self.assertEqual(subject.ordinal, 6)
            self.assertEqual(report, sink.REPORT)
            self.assertFalse(path.exists())
            self.assertEqual(set(backend.stores), {f'phase-{n}' for n in range(1, 7)})
            for n, raw in enumerate(frames, 1):
                self.assertEqual(backend.stores[f'phase-{n}'], raw)
            self.assertLessEqual(backend.peak_bytes, sink.MAXIMUM_BYTES)
            with self.assertRaises(provider.Refused): subject(path, report, binding, frames[-1])

    def test_closed_preparation_rejects_before_reservation(self):
        binding, _ = inputs()
        for changed in ({'closed_report_envelope': dict(sink.REPORT, padding='x')},
                        {'closed_report_envelope': {'schema': sink.REPORT['schema']}},
                        {'original_window': dict(binding['clock'], total='300001')},
                        {'reserved_population': dict(sink.population(), maximumBytes=sink.MAXIMUM_BYTES - 1)},
                        {'independently_expected_binding': dict(binding, mode='native')}):
            backend = sink.ControlledSlotBackend()
            with self.subTest(changed=tuple(changed)), self.assertRaises(provider.Refused):
                self.prepared(backend=backend, **changed)
            self.assertIsNone(backend.reservation)
            self.assertEqual(backend.stores, {})

    def test_identity_padding_phase_and_order_refuse_without_mutation(self):
        subject, binding, frames, backend = self.prepared()
        foreign = copy.deepcopy(binding)
        foreign['invocation'] = '00000000-0000-0000-0000-000000000099'
        phase = provider.decode_auth_frame(frames[0])
        body = provider._auth_canonical(dict(phase, k='terminal'))
        wrong_phase = len(body).to_bytes(4, 'big') + body
        for path, report, bound, raw in (
                ('foreign', sink.REPORT, binding, frames[0]),
                ('inert-slot', dict(sink.REPORT, padding='x'), binding, frames[0]),
                ('inert-slot', sink.REPORT, foreign, frames[0]),
                ('inert-slot', sink.REPORT, binding, frames[1]),
                ('inert-slot', sink.REPORT, binding, wrong_phase)):
            with self.assertRaises(provider.Refused): subject(path, report, bound, raw)
            self.assertEqual(backend.stores, {})
            self.assertEqual(backend.attempts, set())

    def test_each_ambiguous_cut_consumes_core_and_preserves_observations(self):
        for cut in ('candidate', 'write', 'file-barrier', 'replace', 'directory-barrier', 'readback', 'mismatch'):
            with self.subTest(cut=cut):
                subject, binding, frames, backend = self.prepared()
                subject('inert-slot', sink.REPORT, binding, frames[0])
                backend.cut = cut
                with self.assertRaisesRegex(provider.Refused, 'Unknown'):
                    subject('inert-slot', sink.REPORT, binding, frames[1])
                before = copy.deepcopy(backend.stores)
                with self.assertRaises(provider.Refused): subject('inert-slot', sink.REPORT, binding, frames[1])
                self.assertEqual(backend.stores, before)
                self.assertEqual(subject.first_cause, {'phase': 'issuance-possible', 'code': 'readback'})
                backend.cut = None
                loss = subject.observe('loss', {'phase': 'revocation-possible', 'code': 'child-loss'})
                expiry = subject.observe('expiry', {'phase': 'terminal', 'code': 'deadline'})
                self.assertEqual(loss['firstCause'], expiry['firstCause'])
                self.assertNotEqual(loss['lateCause'], expiry['lateCause'])
                self.assertEqual(loss['result'], 'Unknown')
                self.assertLessEqual(backend.peak_bytes, sink.MAXIMUM_BYTES)
                self.assertEqual(backend.inspect_original(subject.identity)['result'], 'Unknown')
                with self.assertRaisesRegex(provider.Refused, 'restart'): self.prepared(backend=backend)

    def test_ambiguous_observation_never_retries_and_absent_first_cause_stays_absent(self):
        for cut in ('candidate', 'write', 'directory-barrier', 'readback'):
            with self.subTest(cut=cut):
                subject, binding, frames, backend = self.prepared()
                backend.cut = cut
                with self.assertRaisesRegex(provider.Refused, 'Unknown'):
                    subject.observe('loss', {'phase': 'ready', 'code': 'child-loss'})
                before = copy.deepcopy(backend.stores)
                with self.assertRaises(provider.Refused): subject.observe('loss')
                self.assertEqual(backend.stores, before)
                self.assertIsNone(subject.first_cause)
                backend.cut = None
                expiry = subject.observe('expiry', {'phase': 'terminal', 'code': 'deadline'})
                self.assertIsNone(expiry['firstCause'])
                self.assertEqual(expiry['lateCause']['code'], 'deadline')
                with self.assertRaises(provider.Refused): subject('inert-slot', sink.REPORT, binding, frames[0])

    def test_receipt_requires_original_generation_digest_and_every_barrier(self):
        for change in ({'generation': 2}, {'slot': 'phase-2'}, {'ordinal': 2}, {'digest': 'f' * 64},
                       {'original': b'foreign'}, {'written': False}, {'barrier': False},
                       {'barrier': 1}, {'fresh_readback': False}):
            subject, binding, frames, backend = self.prepared()
            original = backend.append_phase
            def altered(reservation, raw):
                return dataclasses.replace(original(reservation, raw), **change)
            backend.append_phase = altered
            with self.subTest(change=change), self.assertRaisesRegex(provider.Refused, 'Unknown'):
                subject('inert-slot', sink.REPORT, binding, frames[0])
            self.assertTrue(subject.frozen)

    def test_backend_ingress_is_exact_frames_and_closed_observations_only(self):
        subject, binding, frames, backend = self.prepared()
        received = []
        original = backend.append_phase
        def capture(reservation, raw):
            self.assertIs(reservation, subject.reservation)
            self.assertIs(type(raw), bytes)
            received.append(raw)
            return original(reservation, raw)
        backend.append_phase = capture
        subject('inert-slot', sink.REPORT, binding, frames[0])
        self.assertEqual(received, [frames[0]])
        self.assertNotIn(b'fsgg.release-successor-result', backend.stores['phase-1'])
        with self.assertRaises(provider.Refused):
            backend.append_phase(subject.reservation, frames[0])
        for cause in ({'phase': 'seventh', 'code': 'readback'},
                      {'phase': 'ready', 'code': 'readback', 'padding': 'x'}):
            with self.assertRaises(provider.Refused): subject.observe('loss', cause)
        self.assertEqual(subject.observations, set())
        row = subject.observe('loss', {'phase': 'ready', 'code': 'child-loss'})
        self.assertEqual(set(row), {'v', 'binding', 'generation', 'kind', 'ordinal',
                                    'firstCause', 'lateCause', 'result'})
        self.assertNotIn('schema', row)

    def test_explicit_bytes_and_serial_entry(self):
        subject, binding, frames, backend = self.prepared()
        subject._lock.acquire()
        try:
            with self.assertRaises(provider.Refused): subject('inert-slot', sink.REPORT, binding, frames[0])
            with self.assertRaises(provider.Refused): subject.observe('loss')
        finally: subject._lock.release()
        self.assertEqual(backend.stores, {})
        self.assertEqual(sink.MAXIMUM_BYTES, 23552)
        # Six stable phase slots + four core roles + six observation roles.
        self.assertEqual(sink.MAXIMUM_BYTES, 10 * 2048 + 6 * 512)
        with self.assertRaises(provider.Refused): backend._store('candidate', b'x' * 2049)
        self.assertEqual(backend.stores, {})


if __name__ == '__main__': unittest.main()

#!/usr/bin/env python3
"""In-process framed decoder/owner/typed-sink controls; no external effects."""
import base64
import copy
import hashlib
import importlib.util
import io
import json
import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / 'scripts'))
import inert_phase_slot_sink as slots
import release_successor_auth_consumer as consumer
import release_successor_provider as provider


def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise AssertionError('framed slot fixture forbids network/processes')
sys.addaudithook(audit)
spec = importlib.util.spec_from_file_location('inert_slot_fixture', HERE / 'slot-sink-fixture.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)


def vectors():
    rows = json.loads((HERE / 'callback-vectors.json').read_bytes())
    requests = [bytes.fromhex(e['request']) for e in rows['exchanges']]
    responses = [bytes.fromhex(e['response']) for e in rows['exchanges']]
    frames = [base64.b64decode(provider.decode_auth_frame(r, maximum=4096)['frameBase64'])
              for r in requests if provider.decode_auth_frame(r, maximum=4096)['kind'] == 'validate']
    return rows['binding'], requests, responses, frames


class Input:
    def __init__(self, raw, fragment=4096, events=None):
        self.raw = io.BytesIO(raw)
        self.fragment = fragment
        self.events = events if events is not None else []
        self.reads = []
    def read(self, count):
        self.reads.append(count)
        self.events.append('read')
        return self.raw.read(min(count, self.fragment))


class Output:
    def __init__(self, events=None):
        self.frames = []
        self.events = events if events is not None else []
        self.fail = None
        self.hook = lambda: None
    def write(self, raw):
        self.events.append('write')
        self.hook()
        if self.fail == 'write': raise OSError('inert write failure')
        if self.fail == 'partial':
            self.frames.append(raw[:-1])
            return len(raw) - 1
        self.frames.append(raw)
        return len(raw)
    def flush(self):
        self.events.append('flush')
        if self.fail == 'flush': raise OSError('inert flush failure')


class FramedSlotFixtureTests(unittest.TestCase):
    def prepared(self, raw=b'', fragment=4096, **changes):
        binding, requests, responses, frames = vectors()
        events = []
        backend = slots.ControlledSlotBackend()
        self.now = 1
        clock = consumer.InertCallbackClock(binding['clock']['domain'], 1000, 0,
                                           270000, 285000, 300000, lambda: self.now)
        identity = {'path': 'inert-framed-destination', 'binding': provider.validate_auth_binding(binding),
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        args = dict(expected_binding=binding, original_sink_identity=identity, report=dict(slots.REPORT),
                    original_window=binding['clock'], reserved_population=slots.population(), backend=backend,
                    clock=clock, input_stream=Input(raw, fragment, events), output_stream=Output(events))
        args.update(changes)
        subject = fixture.FramedSlotSinkFixture(**args)
        return subject, backend, requests, responses, frames, events

    def test_thirteen_exact_exchanges_and_six_exact_stores_through_stream(self):
        _, requests, responses, frames = vectors()
        subject, backend, _, _, _, events = self.prepared(b''.join(requests), fragment=7)
        original = subject.decoder.confirm_response_written
        def confirm(count):
            self.assertEqual(events[-2:], ['write', 'flush'])
            events.append('confirm')
            original(count)
        subject.decoder.confirm_response_written = confirm
        self.assertEqual(subject.run(), 6)
        self.assertEqual(subject.output.frames, responses)
        self.assertEqual(len(subject.output.frames), 13)
        self.assertEqual(subject.sink.ordinal, 6)
        self.assertTrue(subject.completed)
        self.assertEqual(subject.report, slots.REPORT)
        self.assertEqual(set(backend.attempts), {f'phase-{n}' for n in range(1, 7)})
        for n, raw in enumerate(frames, 1): self.assertEqual(backend.stores[f'phase-{n}'], raw)
        for index, event in enumerate(events):
            if event == 'write': self.assertEqual(events[index:index+3], ['write', 'flush', 'confirm'])
        with self.assertRaises(provider.Refused): subject.run()

    def test_fragment_feed_uses_actual_decoder_and_requires_terminal_eof(self):
        subject, backend, requests, responses, frames, _ = self.prepared()
        for request in requests:
            for byte in request: subject.feed(bytes([byte]))
        subject.eof()
        self.assertEqual(subject.output.frames, responses)
        self.assertEqual(len(backend.stores), 6)
        with self.assertRaises(provider.Refused): subject.feed(requests[0])

    def test_oversized_prefix_refuses_before_body_read(self):
        for size in (0, 4093, (1 << 32) - 1):
            subject, backend, *_ = self.prepared(size.to_bytes(4, 'big') + b'body-never-read')
            with self.subTest(size=size), self.assertRaises(provider.Refused): subject.run()
            self.assertEqual(subject.input.reads, [4])
            self.assertEqual(backend.attempts, set())
            self.assertEqual(backend.stores, {})
            self.assertTrue(subject.owner._closed)

    def test_pipelined_malformed_and_incomplete_frames_close_before_mutation(self):
        _, requests, _, _ = vectors()
        cases = (requests[0] + requests[1], b'\0\0\0\x02{}', b'\0\0\0\x01!',
                 b'\0\0', requests[0][:-1], b'')
        for raw in cases:
            subject, backend, *_ = self.prepared()
            with self.subTest(length=len(raw)), self.assertRaises(provider.Refused):
                subject.feed(raw)
                subject.eof()
            self.assertTrue(subject.owner._closed)
            self.assertEqual(backend.attempts, set())
            self.assertEqual(backend.stores, {})
            with self.assertRaises(provider.Refused): subject.feed(requests[0])
        subject, backend, *_ = self.prepared(requests[0][:-1], fragment=3)
        with self.assertRaises(provider.Refused): subject.run()
        self.assertEqual(backend.stores, {})

    def test_partial_write_write_exception_and_flush_exception_preserve_committed_attempt(self):
        for failure in ('partial', 'write', 'flush'):
            subject, backend, requests, *_ = self.prepared()
            for request in requests[:2]: subject.feed(request)
            subject.output.fail = failure
            confirmations = []
            subject.decoder.confirm_response_written = lambda count: confirmations.append(count)
            with self.subTest(failure=failure), self.assertRaises(provider.Refused): subject.feed(requests[2])
            before = copy.deepcopy(backend.stores)
            self.assertEqual(backend.attempts, {'phase-1'})
            self.assertEqual(subject.sink.ordinal, 1)
            self.assertEqual(confirmations, [])
            self.assertIsNotNone(subject.decoder._reply)
            self.assertTrue(subject.owner._closed)
            for request in (requests[2], requests[3]):
                with self.assertRaises(provider.Refused): subject.feed(request)
            self.assertEqual(backend.stores, before)

    def test_request_during_response_write_cannot_start_next_phase(self):
        subject, backend, requests, *_ = self.prepared()
        for request in requests[:2]: subject.feed(request)
        def overlapping_request():
            with self.assertRaises(provider.Refused): subject.feed(requests[3])
        subject.output.hook = overlapping_request
        with self.assertRaises(provider.Refused): subject.feed(requests[2])
        self.assertEqual(backend.attempts, {'phase-1'})
        self.assertEqual(subject.sink.ordinal, 1)
        self.assertTrue(subject.owner._closed)

    def test_no_next_input_read_after_transport_failure(self):
        _, requests, *_ = vectors()
        subject, backend, *_ = self.prepared(b''.join(requests))
        subject.output.fail = 'partial'
        with self.assertRaises(provider.Refused): subject.run()
        self.assertEqual(subject.input.reads, [4, len(requests[0]) - 4])
        self.assertEqual(backend.attempts, set())

    def test_backend_cut_after_success_retains_unknown_and_append_once_observations(self):
        for cut in ('write', 'directory-barrier', 'readback'):
            subject, backend, requests, *_ = self.prepared()
            for request in requests[:4]: subject.feed(request)
            backend.cut = cut
            with self.subTest(cut=cut), self.assertRaises(provider.Refused): subject.feed(requests[4])
            self.assertEqual(backend.attempts, {'phase-1', 'phase-2'})
            self.assertEqual(subject.sink.ordinal, 1)
            cause = {'phase': 'issuance-possible', 'code': 'readback' if cut == 'readback' else 'transport'}
            self.assertEqual(subject.sink.first_cause, cause)
            before = copy.deepcopy(backend.stores)
            with self.assertRaises(provider.Refused): subject.feed(requests[4])
            self.assertEqual(backend.stores, before)
            backend.cut = None
            loss = subject.sink.observe('loss', {'phase': 'revocation-possible', 'code': 'child-loss'})
            expiry = subject.sink.observe('expiry', {'phase': 'terminal', 'code': 'deadline'})
            self.assertEqual(loss['result'], 'Unknown')
            self.assertEqual(loss['firstCause'], cause)
            self.assertEqual(expiry['firstCause'], cause)
            self.assertNotEqual(loss['lateCause'], expiry['lateCause'])
            with self.assertRaises(provider.Refused): subject.sink.observe('loss')

    def test_deadline_before_retain_never_writes(self):
        subject, backend, requests, *_ = self.prepared()
        subject.feed(requests[0])
        subject.feed(requests[1])
        self.now = 270000
        with self.assertRaises(provider.Refused): subject.feed(requests[2])
        self.assertEqual(subject.owner.first_cause, 'deadline')
        self.assertEqual(backend.stores, {})
        self.assertEqual(backend.attempts, set())

    def test_early_terminal_through_framed_stream(self):
        binding, requests, _, frames = vectors()
        facts = provider.empty_auth_facts()
        facts['firstCause'] = {'stage': 'protocol', 'code': 'transport'}
        payload = dict(provider.decode_auth_frame(frames[-1])['p'], facts=facts, qualified=False)
        terminal = provider.encode_auth_frame(binding, 2, 'terminal', payload)
        row = provider.decode_auth_frame(requests[3], maximum=4096)
        row.update(frameBase64=base64.b64encode(terminal).decode(), frameDigest=hashlib.sha256(terminal).hexdigest())
        validation = consumer._callback_frame(row)
        row.pop('frameBase64'); row.update(kind='retain', sequence=5)
        retention = consumer._callback_frame(row)
        subject, backend, *_ = self.prepared(b''.join(requests[:3]) + validation + retention, fragment=11)
        self.assertEqual(subject.run(), 2)
        self.assertEqual(backend.stores['phase-2'], terminal)
        self.assertEqual(subject.sink.previous_phase, 'terminal')

    def test_native_foreign_window_padded_preparation_refuses_before_reservation(self):
        binding, *_ = vectors()
        native = dict(binding, mode='native', workflow=dict(binding['workflow'], run=1, attempt=1))
        identity = {'path': 'inert-framed-destination', 'binding': 'f' * 64,
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        for change in ({'expected_binding': native}, {'original_sink_identity': identity},
                       {'original_window': dict(binding['clock'], total='300001')},
                       {'report': dict(slots.REPORT, padding='x')}):
            backend = slots.ControlledSlotBackend()
            with self.subTest(change=tuple(change)), self.assertRaises(provider.Refused):
                self.prepared(backend=backend, **change)
            self.assertIsNone(backend.reservation)
            self.assertEqual(backend.stores, {})

    def test_foreign_callback_rejected_before_any_phase_attempt(self):
        subject, backend, requests, *_ = self.prepared()
        row = provider.decode_auth_frame(requests[0], maximum=4096)
        row['invocation'] = '00000000-0000-0000-0000-000000000099'
        with self.assertRaises(provider.Refused): subject.feed(consumer._callback_frame(row))
        self.assertEqual(backend.stores, {})
        self.assertEqual(backend.attempts, set())

    def test_original_report_refused_before_deepcopy_can_synthesize_literals(self):
        invoked = []
        class Scalar:
            def __deepcopy__(self, memo):
                invoked.append('scalar')
                return 'fsgg.release-successor-result/1'
        class Envelope(dict):
            def __deepcopy__(self, memo):
                invoked.append('envelope')
                return dict(slots.REPORT)
        for report in (dict(slots.REPORT, schema=Scalar()), Envelope(padding='x')):
            backend = slots.ControlledSlotBackend()
            with self.subTest(report=type(report)), self.assertRaises(provider.Refused):
                self.prepared(report=report, backend=backend)
            self.assertEqual(invoked, [])
            self.assertIsNone(backend.reservation)
            self.assertEqual(backend.stores, {})


if __name__ == '__main__': unittest.main()

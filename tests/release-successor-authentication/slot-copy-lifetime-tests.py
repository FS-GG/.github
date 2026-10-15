#!/usr/bin/env python3
"""Scalar census of one synchronous inert Python cut; no physical-fit claim."""
import base64
import importlib.util
import io
import json
import pathlib
import sys
import types
import unittest
from unittest.mock import patch

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import callback_pair_profile as pair

spec = importlib.util.spec_from_file_location('slot_copy_entry', HERE / 'slot-sink-callback-entry.py')
entry = importlib.util.module_from_spec(spec)
spec.loader.exec_module(entry)


def scalar(value):
    return (id(value), len(value))


class Census:
    """Instrumentation stores numbers, never observed buffers or frame objects."""
    def __init__(self, backend):
        self.backend = backend  # Existing harness-owned backend, not a buffer copy.
        self.transfers = {}
        self.cut = None
        self.after = []
        self.closed = None
        self.representations = {}

    def capture(self, cut, roots):
        """Snapshot selected value graphs; keep only numbers and literal labels.

        Container membership is not storage size. Dictionary keys, scalar ints,
        runtime headers/capacity and unselected reachable objects remain excluded.
        Identity comparisons are valid only within this synchronous snapshot.
        """
        rows = {}
        def visit(role, value, ownership, ancestors):
            if type(value) not in (dict, list, tuple, str, bytes, bytearray):
                return
            kind = type(value).__name__
            quantity = len(value.encode('utf-8')) if kind == 'str' else len(value) if kind in ('bytes', 'bytearray') else 1
            rows[role] = (id(value), kind, quantity,
                          'logical-bytes' if kind in ('str', 'bytes', 'bytearray') else 'object-count', ownership)
            if id(value) in ancestors:
                return
            ancestors = ancestors | {id(value)}
            if kind == 'dict':
                for key, child in value.items():
                    visit(role + '/' + str(key), child, ownership, ancestors)
            elif kind in ('list', 'tuple'):
                for index, child in enumerate(value):
                    visit(role + '/' + str(index), child, ownership, ancestors)
        for role, (value, ownership) in roots.items():
            visit(role, value, ownership, set())
        self.representations[cut] = rows

    def stage(self, backend, stage):
        # Inspect the real synchronous call stack; do not replace any production path.
        append = sys._getframe(2)
        ordinal = append.f_locals['ordinal']
        if stage == 'write':
            self.transfers[ordinal] = scalar(backend.stores['temporary'])
        if stage == 'replace':
            assert scalar(backend.stores[f'phase-{ordinal}']) == self.transfers[ordinal]
            assert 'temporary' not in backend.stores
        if stage != 'readback' or ordinal != 6:
            return
        frames = {}
        current = append
        try:
            while current is not None:
                frames[(pathlib.Path(current.f_code.co_filename).name, current.f_code.co_name)] = current
                current = current.f_back
            owner = frames[('release_successor_auth_consumer.py', '_handle')].f_locals['self']
            decoder_frame = frames[('release_successor_auth_consumer.py', '_feed')]
            decoder = decoder_frame.f_locals['self']
            execute = frames[('slot-sink-callback-entry.py', 'execute')]
            objects = {key: scalar(value) for key, value in backend.stores.items()}
            objects.update(actual=scalar(append.f_locals['actual']), pending=scalar(owner._pending[1]),
                decoder_buffer=scalar(decoder._buffer), decoder_raw=scalar(decoder_frame.f_locals['raw']),
                entry_body=scalar(execute.f_locals['body']), entry_prefix=scalar(execute.f_locals['prefix']),
                previous_reply=scalar(execute.f_locals['reply']))
            # All call-site aliases of the retained frame are the same object.
            assert append.f_locals['raw'] is owner._pending[1]
            assert frames[('release_successor_auth_consumer.py', '_handle')].f_locals['frame'] is owner._pending[1]
            self.cut = objects
            sink = frames[('inert_phase_slot_sink.py', '__call__')].f_locals
            handle = frames[('release_successor_auth_consumer.py', '_handle')].f_locals
            self.capture('sixth-readback-selected-value-graphs', {
                'entry-binding': (execute.f_locals['binding'], 'entry-caller-binding'),
                'entry-identity': (execute.f_locals['identity'], 'entry-local'),
                'owner-binding': (owner._binding, 'callback-owner'),
                'owner-binding-bytes': (owner._bytes, 'callback-owner'),
                'owner-previous': (owner._previous, 'callback-owner'),
                'pending-phase': (owner._pending[0], 'callback-owner'),
                'handle-phase': (handle['phase'], 'callback-frame-alias'),
                'parsed-request': (handle['row'], 'callback-frame'),
                'sink-binding': (sink['self'].binding, 'typed-sink'),
                'sink-identity': (sink['self'].identity, 'typed-sink-and-reservation'),
                'reservation-identity': (backend.reservation.identity, 'backend-reservation-alias'),
                'sink-previous-facts': (sink['self'].previous, 'typed-sink'),
                'sink-parsed-phase': (sink['phase'], 'sink-frame')})
        finally:
            # Frame references can create cycles and artificially extend lifetimes.
            frames.clear()
            del current, append

    def confirmed(self, decoder):
        self.after.append((len(decoder._buffer), decoder._reply is None,
                           decoder._owner._pending is None, decoder._owner._terminal,
                           tuple(sorted(self.backend.stores))))

    def closing(self, fixture):
        self.closed = (fixture.owner.first_cause, fixture.owner._pending is None,
                       fixture.sink.first_cause, fixture.sink.ordinal,
                       tuple(sorted(self.backend.attempts)))


def emission_journey(failure=None):
    """Run actual main/execute/encoder/write/flush with an inert evidence file double."""
    frozen = SlotCopyLifetimeControls().frozen()
    census = Census(None)
    original_encode = entry.encode_evidence
    original_dumps = entry.json.dumps
    def dumps(value, *args, **kwargs):
        text = original_dumps(value, *args, **kwargs)
        if type(value) is dict and value.get('schema') == 'fsgg.inert-slot-callback-test-evidence/1':
            census.capture('evidence-json-text-before-ascii', {
                'evidence': (value, 'encoder-argument'), 'json-text': (text, 'encoder-return')})
        return text
    def encode(value):
        caller = sys._getframe(1)
        try:
            if caller.f_code.co_name == 'execute':
                census.capture('terminal-confirmed-evidence-before-encoding', {
                    'evidence': (value, 'execute-local'),
                    'records': (caller.f_locals['records'], 'execute-local-alias'),
                    'last-slot-raw': (caller.f_locals['raw'], 'execute-local'),
                    'owner-previous': (caller.f_locals['subject'].owner._previous, 'callback-owner')})
            raw = original_encode(value)
            census.capture('evidence-ascii-bytes-after-encoding', {
                'evidence': (value, 'encoder-argument'), 'encoded-bytes': (raw, 'encoder-return')})
            return raw
        finally:
            del caller
    class EvidenceOutput(io.BytesIO):
        writes = 0
        def write(self, raw):
            self.writes += 1
            caller = sys._getframe(1)
            try:
                census.capture('evidence-file-write-before-return', {
                    'evidence': (caller.f_locals['evidence'], 'main-local'),
                    'encoded-bytes': (raw, 'main-local-and-write-argument')})
                if failure == 'short':
                    return super().write(raw[:-1])
                return super().write(raw)
            finally:
                del caller
        def flush(self):
            if failure == 'flush':
                raise OSError('controlled evidence flush failure')
        def close(self):
            pass  # Retain only the independent file-double output for assertions.
    output = EvidenceOutput()
    request = b''.join(bytes.fromhex(row['request']) for row in frozen['exchanges'])
    if failure == 'malformed':
        request = b'\x00\x00\x00\x01{'
    arguments = ['slot-sink-callback-entry.py',
        base64.b64encode(json.dumps(frozen['binding']).encode()).decode(),
        base64.b64encode(json.dumps(frozen['binding']['clock']).encode()).decode(), 'inert-evidence']
    with patch.object(entry, 'encode_evidence', encode), patch.object(entry.json, 'dumps', dumps), \
         patch.object(pathlib.Path, 'open', return_value=output) as opened, \
         patch.object(sys, 'argv', arguments), patch.object(sys, 'stdin', types.SimpleNamespace(buffer=io.BytesIO(request))), \
         patch.object(sys, 'stdout', types.SimpleNamespace(buffer=io.BytesIO())), patch.object(sys, 'stderr', io.StringIO()):
        status = entry.main()
    assert output.writes == opened.call_count  # Exactly one write per file open, zero for malformed input.
    return census, status, output.getvalue(), opened.call_count


class SlotCopyLifetimeControls(unittest.TestCase):
    def frozen(self):
        _, path = pair.select(HERE.parents[1], pair.SLOT_PROFILE)
        return json.loads(path.read_bytes())

    def journey(self, failure=None):
        frozen = self.frozen()
        backend = entry.slots.ControlledSlotBackend()
        census = Census(backend)
        original_stage = entry.slots.ControlledSlotBackend._stage
        original_confirm = entry.consumer.InertCallbackRequestDecoder.confirm_response_written
        original_close = entry.fixture.FramedSlotSinkFixture._close

        def stage(subject, name):
            census.stage(subject, name)
            if name == 'readback' and 'phase-6' in subject.stores and failure == 'readback':
                raise entry.slots.BackendCut('readback')
            return original_stage(subject, name)

        def confirm(subject, count):
            original_confirm(subject, count)
            census.confirmed(subject)

        def close(subject):
            original_close(subject)
            census.closing(subject)

        class Output(io.BytesIO):
            calls = 0
            def write(self, data):
                self.calls += 1
                if self.calls == 13 and failure == 'short':
                    return len(data) - 1
                return super().write(data)
            def flush(self):
                if self.calls == 13 and failure == 'flush':
                    raise OSError('controlled terminal flush failure')

        output = Output()
        request = b''.join(bytes.fromhex(row['request']) for row in frozen['exchanges'])
        with patch.object(entry.slots.ControlledSlotBackend, '_stage', stage), \
             patch.object(entry.consumer.InertCallbackRequestDecoder, 'confirm_response_written', confirm), \
             patch.object(entry.fixture.FramedSlotSinkFixture, '_close', close):
            if failure:
                with self.assertRaises(entry.slots.Refused):
                    entry.execute(frozen['binding'], frozen['binding']['clock'], io.BytesIO(request), output, backend)
            else:
                evidence = entry.execute(frozen['binding'], frozen['binding']['clock'], io.BytesIO(request), output, backend)
                self.assertEqual(evidence, frozen['slotEvidence'])
                self.assertEqual(output.getvalue(), b''.join(bytes.fromhex(row['response']) for row in frozen['exchanges']))
        return frozen, backend, census

    def test_selected_graph_identities_and_emission_cuts(self):
        _, _, census = self.journey()
        rows = census.representations['sixth-readback-selected-value-graphs']
        self.assertEqual(rows['pending-phase'][0], rows['handle-phase'][0])
        self.assertNotEqual(rows['pending-phase'][0], rows['sink-parsed-phase'][0])
        self.assertNotEqual(rows['owner-binding'][0], rows['sink-binding'][0])
        self.assertEqual(rows['sink-identity'][0], rows['reservation-identity'][0])
        self.assertNotEqual(rows['owner-previous/p/facts'][0], rows['sink-previous-facts'][0])
        emission, status, raw, opens = emission_journey()
        self.assertEqual((status, opens), (0, 1))
        self.assertEqual(json.loads(raw), self.frozen()['slotEvidence'])
        constructed = emission.representations['terminal-confirmed-evidence-before-encoding']
        self.assertEqual(constructed['records'][0], constructed['evidence/slots'][0])
        self.assertEqual(constructed['last-slot-raw'][2], 991)
        encoded = emission.representations['evidence-ascii-bytes-after-encoding']
        written = emission.representations['evidence-file-write-before-return']
        self.assertEqual(encoded['encoded-bytes'][0], written['encoded-bytes'][0])
        self.assertEqual(encoded['encoded-bytes'][2], len(raw))
        self.assertEqual(emission.representations['evidence-json-text-before-ascii']['json-text'][2], len(raw))
        for failure in ('malformed', 'short', 'flush'):
            failed, status, partial, opens = emission_journey(failure)
            self.assertEqual(status, 3)
            if failure == 'malformed':
                self.assertEqual((failed.representations, opens, partial), ({}, 0, b''))
            else:
                self.assertIn('evidence-file-write-before-return', failed.representations)
                self.assertEqual(opens, 1)  # No retry after partial write or flush uncertainty.

    def test_terminal_readback_distinct_objects_and_alias_transfer(self):
        frozen, backend, census = self.journey()
        cut = census.cut
        self.assertIsNotNone(cut)
        self.assertEqual(len(census.transfers), 6)
        lengths = tuple(len(bytes.fromhex(row['bytes'])) for row in frozen['slotEvidence']['slots'])
        self.assertEqual(lengths, (1141, 972, 830, 917, 831, 991))
        for ordinal, size in enumerate(lengths, 1):
            self.assertEqual(cut[f'phase-{ordinal}'][1], size)
        self.assertEqual(len(cut), 16)
        self.assertEqual(len({identity for identity, _ in cut.values()}), len(cut))
        self.assertEqual(cut['old'][1], lengths[4])
        for key in ('candidate', 'readback', 'actual', 'pending'):
            self.assertEqual(cut[key][1], lengths[5])
        request_length = len(bytes.fromhex(frozen['exchanges'][-1]['request']))
        previous_length = len(bytes.fromhex(frozen['exchanges'][-2]['response']))
        self.assertEqual((request_length, previous_length), (295, 298))
        self.assertEqual(cut['decoder_buffer'][1], request_length)
        self.assertEqual(cut['decoder_raw'][1], request_length)
        self.assertEqual(cut['entry_body'][1] + cut['entry_prefix'][1], request_length)
        self.assertEqual(cut['previous_reply'][1], previous_length)
        named = sum(size for key, (_, size) in cut.items() if key in backend.stores or key in ('old', 'candidate', 'readback'))
        listed = sum(size for _, size in cut.values())
        self.assertEqual(named, 8495)
        self.assertEqual(listed, sum(lengths) + lengths[4] + 4 * lengths[5] + 3 * request_length + previous_length)
        self.assertEqual(listed, 11660)
        self.assertEqual(len(census.after), 13)
        self.assertTrue(all(row[:3] == (0, True, index % 2 == 0) for index, row in enumerate(census.after)))
        self.assertEqual(census.after[-1], (0, True, True, True, tuple(f'phase-{n}' for n in range(1, 7))))
        self.assertEqual(tuple(sorted(backend.stores)), census.after[-1][-1])
        for ordinal in range(1, 7):
            self.assertEqual(scalar(backend.stores[f'phase-{ordinal}']), census.transfers[ordinal])

    def test_terminal_failures_keep_committed_stores_and_original_attempt(self):
        for failure in ('readback', 'short', 'flush'):
            with self.subTest(failure=failure):
                frozen, backend, census = self.journey(failure)
                self.assertIsNotNone(census.cut)
                self.assertEqual(len(census.after), 12)
                self.assertEqual(backend.attempts, {f'phase-{n}' for n in range(1, 7)})
                for row in frozen['slotEvidence']['slots']:
                    self.assertEqual(backend.stores[f"phase-{row['ordinal']}"], bytes.fromhex(row['bytes']))
                if failure == 'readback':
                    self.assertEqual(census.closed[0], 'retention')
                    self.assertEqual(census.closed[2], {'phase': 'terminal', 'code': 'readback'})
                    self.assertEqual(census.closed[3], 5)
                    self.assertFalse(census.closed[1])  # Pending uncertainty remains owned.
                    self.assertEqual(set(backend.stores), {f'phase-{n}' for n in range(1, 7)} | {'old', 'candidate', 'readback'})
                else:
                    self.assertEqual(census.closed[0], 'internal')
                    self.assertIsNone(census.closed[2])
                    self.assertEqual(census.closed[3], 6)
                    self.assertTrue(census.closed[1])  # Committed before terminal reply transport.
                    self.assertEqual(set(backend.stores), {f'phase-{n}' for n in range(1, 7)})
                before = (tuple(backend.events), tuple(sorted(backend.attempts)),
                          {key: scalar(value) for key, value in backend.stores.items()})
                # Consumed slot cannot be appended again, even with identical bytes.
                with self.assertRaises(entry.slots.Refused):
                    backend.append_phase(backend.reservation, bytes.fromhex(frozen['slotEvidence']['slots'][-1]['bytes']))
                # Reconstructed entry may not reserve or retry the original backend.
                with self.assertRaises(entry.slots.Refused):
                    entry.execute(frozen['binding'], frozen['binding']['clock'], io.BytesIO(b''), io.BytesIO(), backend)
                self.assertEqual(before, (tuple(backend.events), tuple(sorted(backend.attempts)),
                                          {key: scalar(value) for key, value in backend.stores.items()}))


if __name__ == '__main__':
    unittest.main()

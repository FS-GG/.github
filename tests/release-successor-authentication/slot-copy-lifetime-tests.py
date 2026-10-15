#!/usr/bin/env python3
"""Scalar census of one synchronous inert Python cut; no physical-fit claim."""
import importlib.util
import io
import json
import pathlib
import sys
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

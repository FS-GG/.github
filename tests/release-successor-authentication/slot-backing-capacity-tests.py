#!/usr/bin/env python3
"""CPython backing allocation at the original synchronous sixth readback."""
import hashlib
import importlib.util
import json
import pathlib
import platform
import sys
import unittest
from unittest.mock import patch

HERE = pathlib.Path(__file__).resolve().parent
LIFETIME_PATH = HERE / 'slot-copy-lifetime-tests.py'
if hashlib.sha256(LIFETIME_PATH.read_bytes()).hexdigest() != 'fd1c6ab7a6d53fa248e2080e29800f273920484eb079bb141aa57288bb2604da':
    raise ValueError('original lifetime observer source drift')
spec = importlib.util.spec_from_file_location('capacity_lifetime', LIFETIME_PATH)
lifetime = importlib.util.module_from_spec(spec)
spec.loader.exec_module(lifetime)
ORIGINAL_CENSUS = lifetime.Census
ROLES = frozenset([f'phase-{n}' for n in range(1, 7)] + ['old', 'candidate', 'readback', 'decoder_buffer'])
CUT = 'sixth-readback-selected-value-graphs'


def runtime_identity():
    return (platform.python_implementation(), platform.python_version(), sys.version)


def observe(values, original, expected, runtime=None, capability=None):
    """Read exact built-ins directly; return scalars only, with no size fallback."""
    runtime = runtime_identity() if runtime is None else runtime
    capability = getattr(bytearray, '__alloc__', None) if capability is None else capability
    if runtime[0] != 'CPython' or not callable(capability):
        raise ValueError('unsupported bytearray allocation runtime/capability')
    if set(values) != ROLES or set(expected) != ROLES:
        raise ValueError('incomplete or extra allocation role')
    rows = {}
    identities = set()
    for role, value in values.items():
        if type(value) is not bytearray:
            raise ValueError('exact built-in bytearray required')
        identity, length = lifetime.scalar(value)
        if identity in identities or (identity, length) != original[role]:
            raise ValueError('false alias or same-cut object substitution')
        if value != expected[role]:
            raise ValueError('same-cut content mismatch')
        identities.add(identity)
        # Invoke the built-in descriptor, never an object's overridable method.
        capacity = bytearray.__alloc__(value)
        if type(capacity) is not int or capacity <= length:
            raise ValueError('unsupported selected nonempty buffer allocation semantics')
        rows[role] = (identity, 'bytearray', length, capacity)
    return rows


def validate_rows(reported, direct, original):
    """A capacity receipt must preserve the direct same-cut measurement."""
    if set(reported) != ROLES or set(direct) != ROLES:
        raise ValueError('incomplete or extra allocation role')
    if len({row[0] for row in reported.values()}) != len(ROLES):
        raise ValueError('false alias')
    for role, row in reported.items():
        if row != direct[role] or (row[0], row[2]) != original[role]:
            raise ValueError('identity/type/length/allocation mismatch')


class CapacityCensus(ORIGINAL_CENSUS):
    def capture(self, cut, roots):
        if cut != CUT:
            return super().capture(cut, roots)
        # Do not wrap stage: its original _getframe(2) call must keep its depth.
        caller = sys._getframe(1)
        values = {}
        try:
            if caller.f_code is not ORIGINAL_CENSUS.stage.__code__:
                raise ValueError('original stage frame path drift')
            decoder = caller.f_locals['decoder']
            values.update(self.backend.stores)
            values['decoder_buffer'] = decoder._buffer
            frozen = lifetime.SlotCopyLifetimeControls().frozen()
            expected = {f"phase-{row['ordinal']}": bytes.fromhex(row['bytes'])
                        for row in frozen['slotEvidence']['slots']}
            expected.update(old=expected['phase-5'], candidate=expected['phase-6'],
                            readback=expected['phase-6'],
                            decoder_buffer=bytes.fromhex(frozen['exchanges'][-1]['request']))
            self.runtime = runtime_identity()
            self.allocations = observe(values, self.cut, expected, self.runtime)
            validate_rows(self.allocations, self.allocations, self.cut)
            super().capture(cut, roots)
        finally:
            values.clear()
            # No observed object or frame is retained by the instrumentation.
            del caller


class SlotBackingCapacityControls(unittest.TestCase):
    def journey(self, failure=None):
        with patch.object(lifetime, 'Census', CapacityCensus):
            return lifetime.SlotCopyLifetimeControls().journey(failure)

    def test_actual_cut_allocation_and_original_journey(self):
        _, _, census = self.journey()
        self.assertEqual(census.runtime, runtime_identity())
        self.assertEqual(census.runtime[0], 'CPython')
        validate_rows(census.allocations, census.allocations, census.cut)
        self.assertEqual(set(census.allocations), ROLES)
        self.assertEqual(len(census.cut), 16)
        self.assertEqual(len({row[0] for row in census.cut.values()}), 16)
        self.assertEqual(len(census.transfers), 6)
        self.assertEqual(len(census.after), 13)
        for ordinal in range(1, 7):
            row = census.allocations[f'phase-{ordinal}']
            self.assertEqual((row[0], row[2]), census.transfers[ordinal])
        self.assertGreater(sum(row[3] for row in census.allocations.values()),
                           sum(row[2] for row in census.allocations.values()))
        # Qualification binds real runtime values, without portable numeric totals.
        print(json.dumps({'schema': 'fsgg.python-slot-backing-capacity/1',
                          'runtime': census.runtime, 'cut': CUT,
                          'roles': {role: {'logicalBytes': row[2], 'allocationBytes': row[3]}
                                    for role, row in sorted(census.allocations.items())},
                          'distinctObjects': len(census.allocations)}, sort_keys=True))

    def test_original_lifetime_and_failure_controls_with_observer(self):
        with patch.object(lifetime, 'Census', CapacityCensus):
            controls = lifetime.SlotCopyLifetimeControls()
            controls.test_terminal_readback_distinct_objects_and_alias_transfer()
            controls.test_terminal_failures_keep_committed_stores_and_original_attempt()
            controls.test_selected_graph_identities_and_emission_cuts()

    def test_receipt_refuses_role_alias_substitution_length_and_capacity_forgery(self):
        _, _, census = self.journey()
        direct = census.allocations
        mutations = []
        missing = dict(direct); missing.pop('old'); mutations.append(missing)
        extra = dict(direct); extra['unexpected'] = direct['old']; mutations.append(extra)
        for index, replacement in ((0, direct['candidate'][0]), (0, -1),
                                   (1, 'bytes'), (2, direct['old'][2] + 1),
                                   (3, direct['old'][2])):
            altered = dict(direct)
            row = list(altered['old']); row[index] = replacement
            altered['old'] = tuple(row); mutations.append(altered)
        for altered in mutations:
            with self.subTest(altered=altered), self.assertRaises(ValueError):
                validate_rows(altered, direct, census.cut)

    def test_object_type_content_identity_and_runtime_refusals(self):
        values = {role: bytearray(b'abc') for role in ROLES}
        original = {role: lifetime.scalar(value) for role, value in values.items()}
        expected = {role: b'abc' for role in ROLES}
        observe(values, original, expected)
        for altered in ({role: value for role, value in values.items() if role != 'old'},
                        dict(values, unexpected=bytearray(b'abc'))):
            with self.assertRaises(ValueError):
                observe(altered, original, expected)
        class Spoofed(bytearray):
            def __alloc__(self):
                raise AssertionError('spoofed method must never run')
        for replacement in (Spoofed(b'abc'), bytes(b'abc'), bytearray(b'abc'), values['candidate']):
            altered = dict(values); altered['old'] = replacement
            with self.subTest(type=type(replacement)), self.assertRaises(ValueError):
                observe(altered, original, expected)
        values['old'][0] = ord('z')  # Same identity and length, changed content.
        with self.assertRaises(ValueError):
            observe(values, original, expected)
        for runtime, capability in ((('PyPy', 'unknown', 'unknown'), None),
                                    (runtime_identity(), False)):
            with self.assertRaises(ValueError):
                observe({}, {}, {}, runtime, capability)

    def test_grow_shrink_capacity_cannot_be_inferred_from_length(self):
        value = bytearray(b'x' * 1000)
        del value[-1:]  # Retain allocated buffer while logical length shrinks.
        peer = bytearray(value)
        self.assertEqual(value, peer)
        self.assertEqual(len(value), len(peer))
        self.assertNotEqual(bytearray.__alloc__(value), bytearray.__alloc__(peer))
        before = bytearray.__alloc__(value)
        value.extend(b'y' * 1000)
        self.assertGreater(bytearray.__alloc__(value), before)
        grown = bytearray.__alloc__(value)
        del value[1:]
        self.assertLess(bytearray.__alloc__(value), grown)


if __name__ == '__main__':
    unittest.main()

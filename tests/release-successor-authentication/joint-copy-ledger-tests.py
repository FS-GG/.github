#!/usr/bin/env python3
"""Offline controls for source-scoped copy evidence and honest unknowns."""
import copy
from dataclasses import FrozenInstanceError
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import joint_copy_ledger as ledger


class JointCopyLedgerControls(unittest.TestCase):
    def setUp(self):
        self.inputs = ledger.load()

    def test_separate_topologies_keep_logical_subtotal_and_no_fit(self):
        ledger.verify_local_sources(self.inputs)
        result = ledger.reconcile(self.inputs)
        self.assertEqual(result['fit'], 'unproved')
        self.assertFalse(result['admission'])
        self.assertEqual(len(result['groups']), 5)
        python = result['groups'][0]
        self.assertEqual(python['known_object_logical_bytes'], 11660)
        self.assertIsNone(python['physical_byte_lower_bound'])
        for group in result['groups'][1:]:
            self.assertIsNone(group['known_object_logical_bytes'])
            self.assertIsNone(group['physical_byte_lower_bound'])
            self.assertEqual(group['fit'], 'unproved')
        self.assertEqual(len(result['supported_alias_deductions']), 4)
        self.assertTrue(result['missing_obligations'])
        self.assertNotIn('total_bytes', result)

    def test_recorded_python_roles_match_actual_controlled_journey(self):
        spec = importlib.util.spec_from_file_location('joint_ledger_lifetime', HERE / 'slot-copy-lifetime-tests.py')
        witness = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(witness)
        _, _, census = witness.SlotCopyLifetimeControls().journey()
        rows = [row for row in self.inputs['observations'] if row['id'].startswith('python:')]
        concrete = [row for row in rows if row['role'] in census.cut]
        self.assertEqual(len(concrete), 16)
        self.assertEqual(len({identity for identity, _ in census.cut.values()}), 16)
        self.assertEqual({row['role']: row['quantity'] for row in concrete},
                         {role: size for role, (_, size) in census.cut.items()})
        self.assertEqual(sum(row['quantity'] for row in concrete), 11660)

    def test_equal_bytes_never_establish_alias(self):
        # Candidate/readback lengths match, but the actual Python objects differ.
        self.inputs['aliases'].append(dict(left='python:candidate', right='python:readback', basis='same-scoped-object'))
        with self.assertRaises(ValueError): ledger.reconcile(self.inputs)
        self.inputs['aliases'][-1]['basis'] = 'equal-bytes'
        with self.assertRaises(ValueError): ledger.reconcile(self.inputs)

    def test_alias_cannot_cross_run_process_attempt_cut_or_topology(self):
        for field in ('run', 'process', 'attempt', 'topology', 'cut'):
            with self.subTest(field=field):
                altered = copy.deepcopy(self.inputs)
                row = next(row for row in altered['observations'] if row['id'] == 'python:append-raw')
                if field == 'cut': row[field] = 'later-cut'
                else: row['scope'][field] = 'different'
                with self.assertRaises(ValueError): ledger.reconcile(altered)

    def test_model_roles_cannot_become_physical_observations(self):
        row = next(row for row in self.inputs['observations'] if row['id'] == 'host:old')
        row['quantity_type'], row['quantity'] = 'physical-byte-lower-bound', 99999
        with self.assertRaises(ValueError): ledger.reconcile(self.inputs)

    def test_large_logical_sum_still_cannot_establish_physical_overflow(self):
        for row in self.inputs['observations']:
            if row['id'] == 'python:old': row['quantity'] = 100000
        result = ledger.reconcile(self.inputs)
        self.assertGreater(result['groups'][0]['known_object_logical_bytes'], ledger.CAP)
        self.assertEqual(result['fit'], 'unproved')

    def test_supplied_bound_overflow_is_conditional_and_needs_one_compatible_cut(self):
        # Synthetic adversarial control, not an additional observed population.
        physical = copy.deepcopy(self.inputs['observations'][0])
        physical.update(id='synthetic:physical', quantity_type='physical-byte-lower-bound', quantity=65537,
                        alias_class='synthetic-independent-object')
        self.inputs['observations'].append(physical)
        self.assertEqual(ledger.reconcile(self.inputs)['fit'], 'supplied-bound-overflow')
        physical['quantity'] = 40000
        separate = copy.deepcopy(physical)
        separate.update(id='synthetic:other-cut', cut='incompatible-cut')
        self.inputs['observations'].append(separate)
        self.assertEqual(ledger.reconcile(self.inputs)['fit'], 'unproved')

    def test_duplicate_alias_class_counts_once_but_conflicting_quantity_refuses(self):
        original = copy.deepcopy(self.inputs['observations'][0])
        original['id'] = 'python:duplicate-role'
        self.inputs['observations'].append(original)
        self.assertEqual(ledger.reconcile(self.inputs)['groups'][0]['known_object_logical_bytes'], 11660)
        original['quantity'] += 1
        with self.assertRaises(ValueError): ledger.reconcile(self.inputs)

    def test_absent_populations_and_completion_cannot_clear_obligations(self):
        self.inputs['observations'] = [row for row in self.inputs['observations'] if row['id'].startswith('host:')]
        self.inputs['aliases'] = [row for row in self.inputs['aliases'] if row['left'].startswith('host:')]
        result = ledger.reconcile(self.inputs)
        self.assertIsNone(result['groups'][0]['known_object_logical_bytes'])
        self.assertEqual(result['fit'], 'unproved')
        self.inputs['missing_obligations'] = []
        with self.assertRaises(ValueError): ledger.reconcile(self.inputs)

    def test_immutable_observation_and_provenance(self):
        row = ledger.Observation.read(self.inputs['observations'][0])
        with self.assertRaises(FrozenInstanceError): row.quantity = 0
        with self.assertRaises(FrozenInstanceError): row.provenance.revision = '0' * 40
        with self.assertRaises(TypeError): row.scope[0] = 'different'

    def test_malformed_shape_unknown_quantities_and_source_drift_refuse(self):
        for value in (-1, True, '0', 1.5):
            altered = copy.deepcopy(self.inputs)
            altered['observations'][0]['quantity'] = value
            with self.assertRaises(ValueError): ledger.reconcile(altered)
        for key in ('retry_permitted', 'physically_released', 'fit'):
            altered = copy.deepcopy(self.inputs)
            altered['observations'][0][key] = True
            with self.assertRaises(ValueError): ledger.reconcile(altered)
        self.inputs['observations'][0]['provenance']['sha256'] = '0' * 64
        with self.assertRaises(ValueError): ledger.verify_local_sources(self.inputs)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'duplicate.json'
            path.write_text('{"schema":1,"schema":2}')
            with self.assertRaises(ValueError): ledger.load(path)


if __name__ == '__main__': unittest.main()

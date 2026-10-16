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
        self.assertEqual(len(result['groups']), 13)
        python = result['groups'][0]
        self.assertEqual(python['known_object_logical_bytes'], 11660)
        self.assertIsNone(python['physical_byte_lower_bound'])
        for group in result['groups'][1:]:
            if group['scope']['topology'] not in ('python-terminal-controlled', 'managed-python-held-terminal'):
                self.assertIsNone(group['known_object_logical_bytes'])
            self.assertIsNone(group['physical_byte_lower_bound'])
            self.assertEqual(group['fit'], 'unproved')
        self.assertEqual(len(result['supported_alias_deductions']), len(self.inputs['aliases']))
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

    def test_selected_python_graphs_match_actual_cuts_without_cross_cut_sum(self):
        spec = importlib.util.spec_from_file_location('graph_witness', HERE / 'slot-copy-lifetime-tests.py')
        witness = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(witness)
        _, _, readback = witness.SlotCopyLifetimeControls().journey()
        emission, status, _, _ = witness.emission_journey()
        self.assertEqual(status, 0)
        snapshots = readback.representations | emission.representations
        for index, (cut, snapshot) in enumerate(snapshots.items()):
            selected = {role: value for role, value in snapshot.items()
                        if '/' not in role or (role.startswith('evidence/slots/') and role.count('/') == 3)}
            recorded = {row['role']: row for row in self.inputs['observations']
                        if row['id'].startswith(f'python-graph-{index}:')}
            self.assertEqual(set(recorded), set(selected))
            for role, row in recorded.items():
                identity, representation, quantity, kind, ownership = selected[role]
                self.assertEqual((row['cut'], row['representation'], row['quantity'], row['quantity_type'], row['ownership']),
                                 (cut, representation, quantity, kind, ownership))
            roles = tuple(selected)
            for left in roles:
                for right in roles:
                    self.assertEqual(recorded[left]['alias_class'] == recorded[right]['alias_class'],
                                     selected[left][0] == selected[right][0])
        result = ledger.reconcile(self.inputs)
        self.assertEqual((result['fit'], result['admission']), ('unproved', False))
        self.assertNotIn('total_bytes', result)
        self.assertTrue(all(group['physical_byte_lower_bound'] is None for group in result['groups']))

    def managed_rows(self, inputs=None):
        return [row for row in (inputs or self.inputs)['observations']
                if row['id'].startswith('managed-roots:')]

    def assert_managed_backing(self, inputs):
        rows = self.managed_rows(inputs)
        objects = {row['alias_class']: row for row in rows}
        capacities = {key: row['quantity'] for key, row in objects.items()
                      if row['quantity_type'] == 'backing-capacity-bytes'}
        expected = {f'snapshot-{i}': size for i, size in enumerate((1141, 972, 830, 917, 831, 991), 1)}
        expected.update({'result-parser-input': 2048, 'callback-parser-input': 512,
                         'result-binding': 994, 'callback-binding': 994, 'start-frame': 1123})
        self.assertEqual(capacities, expected)
        self.assertEqual(len(objects), 37)
        self.assertEqual(len(rows), 44)
        for row in rows:
            self.assertEqual(row['provenance'], {
                'source_type': 'git-blob', 'repository': 'FS-GG/FS.GG.Coordination',
                'revision': '2af47b82f95dea136e9d9f4c2c8edd0be7f90987',
                'path': 'tests/FS.GG.Coordination.Orchestration.Execution.Tests/TerminalCutCorrelationTests.fs',
                'sha256': '531d296e2a670f2c1a3c2729ae092a5482ccec6cc85efca9beda9ea847a80669'})
            self.assertEqual(row['scope'], {
                'topology': 'managed-python-held-terminal', 'run': 'pr1005-source-witness',
                'process': 'managed-parent-before-python-readback-release', 'attempt': 'original-controlled-attempt'})
            self.assertEqual(row['cut'], 'ordinal-six-python-readback-held-before-release')
        aliases = {row['role']: row['alias_class'] for row in rows}
        for i in range(1, 7):
            self.assertEqual(aliases[f'snapshot-{i}-payload-backing'], aliases[f'snapshot-{i}'])
        self.assertEqual(aliases['discovered-current-request'], aliases['original-request-13'])
        endpoint = {aliases[f'{role}-{i}'] for role in ('original-request', 'endpoint-copy') for i in range(1, 14)}
        self.assertEqual(len(endpoint), 26)
        self.assertTrue(endpoint.isdisjoint(capacities))
        result = ledger.reconcile(inputs)
        group = next(group for group in result['groups'] if group['scope']['run'] == 'pr1005-source-witness')
        self.assertEqual(group['known_object_backing_capacity_bytes'], 11353)
        self.assertEqual(group['known_object_logical_bytes'], 295)
        self.assertIsNone(group['physical_byte_lower_bound'])
        self.assertEqual((result['fit'], result['admission']), ('unproved', False))

    def test_protected_managed_backing_capacity_and_request_alias(self):
        self.assert_managed_backing(self.inputs)

    def test_managed_source_witness_mutations_are_detected(self):
        # Each mutation must fail the pinned source regression, even if syntactically valid.
        for mutation in ('capacity', 'alias', 'provenance', 'cut', 'process', 'payload-allocation'):
            with self.subTest(mutation=mutation):
                altered = copy.deepcopy(self.inputs)
                rows = self.managed_rows(altered)
                if mutation == 'capacity':
                    for row in rows:
                        if row['alias_class'] == 'result-parser-input': row['quantity'] += 1
                elif mutation == 'alias':
                    for row in rows:
                        if row['alias_class'] == 'callback-binding': row['alias_class'] = 'result-binding'
                elif mutation == 'provenance':
                    for row in rows: row['provenance']['revision'] = '0' * 40
                elif mutation in ('cut', 'process'):
                    for row in rows:
                        if mutation == 'cut': row['cut'] = 'after-release'
                        else: row['scope']['process'] = 'python-child'
                else:
                    extra = copy.deepcopy(rows[0])
                    extra.update(id='managed-roots:payload-as-allocation', role='payload-as-allocation',
                                 alias_class='payload-as-allocation', quantity=4835)
                    altered['observations'].append(extra)
                with self.assertRaises((AssertionError, ValueError)):
                    self.assert_managed_backing(altered)

    def test_managed_alias_cannot_cross_provenance_or_cut(self):
        for field in ('provenance', 'cut', 'process'):
            altered = copy.deepcopy(self.inputs)
            row = next(row for row in self.managed_rows(altered) if row['role'] == 'discovered-current-request')
            if field == 'provenance': row['provenance']['sha256'] = '0' * 64
            elif field == 'cut': row['cut'] = 'after-release'
            else: row['scope']['process'] = 'python-child'
            with self.assertRaises(ValueError): ledger.reconcile(altered)

    def test_backing_capacity_cannot_prove_physical_overflow_or_cross_cut_total(self):
        for row in self.managed_rows():
            if row['quantity_type'] == 'backing-capacity-bytes': row['quantity'] += 100000
        result = ledger.reconcile(self.inputs)
        group = next(group for group in result['groups'] if group['scope']['run'] == 'pr1005-source-witness')
        self.assertGreater(group['known_object_backing_capacity_bytes'], ledger.CAP)
        self.assertEqual((group['fit'], result['fit'], result['admission']), ('unproved', 'unproved', False))
        self.assertIsNone(group['physical_byte_lower_bound'])
        self.assertNotIn('total_bytes', result)
        # Two individually scoped capacity claims do not acquire a joined total.
        inputs = copy.deepcopy(self.inputs)
        prototype = copy.deepcopy(self.managed_rows()[0])
        inputs['observations'] = []
        inputs['aliases'] = []
        for cut in ('synthetic-cut-a', 'synthetic-cut-b'):
            row = copy.deepcopy(prototype)
            row.update(id=cut, cut=cut, alias_class='synthetic-backing', quantity=40000)
            inputs['observations'].append(row)
        separate = ledger.reconcile(inputs)
        self.assertEqual([group['known_object_backing_capacity_bytes'] for group in separate['groups']], [40000, 40000])
        self.assertEqual(separate['fit'], 'unproved')
        self.assertNotIn('total_bytes', separate)
        prototype['identity_basis'] = 'modeled-identity'
        with self.assertRaises(ValueError): ledger.Observation.read(prototype)

    def assert_reader_cuts(self, inputs):
        result = ledger.reconcile(inputs)
        early = {
            'ack-supplied-backing': ('ack', 199, 'backing-capacity-bytes'),
            'ack-state-backing': ('ack', 199, 'backing-capacity-bytes'),
            'child-read-supplied-backing': ('child-read', 2048, 'backing-capacity-bytes'),
            'child-read-state-backing': ('child-read', 2048, 'backing-capacity-bytes'),
            'result-parser-input': (None, 1, 'object-count')}
        terminal = {
            'callback-supplied-backing': ('callback-reader', 1, 'backing-capacity-bytes'),
            'callback-state-backing': ('callback-reader', 1, 'backing-capacity-bytes')}
        for role in ('snapshot-1', 'snapshot-2', 'snapshot-3', 'snapshot-4', 'snapshot-5',
                     'snapshot-6', 'result-parser-input', 'callback-parser-input',
                     'result-binding', 'callback-binding', 'start-frame'):
            terminal[role] = (role, 1, 'object-count')
        for population, count in ((early, 9), (terminal, 13)):
            for number in range(1, count + 1):
                for kind in ('original-request', 'endpoint-copy'):
                    role = f'{kind}-{number}'
                    population[role] = (role, 1, 'object-count')
        cases = (
            ('managed-ack-read:', early, '1cc2af9a5e4ba65cd2db39afbc86cfdb7ca8029e',
             '4c52908092508e241878113aa5338783a3ea756d35f87612ad8993910ed4afaa',
             'managed-python-held-ack-read', 'pr1007-source-witness',
             'managed-parent-before-third-ack-write-returns', 'phase-four-ack-write-child-read-five-pending', 2247, 20),
            ('managed-callback-read:', terminal, '6970fd73a6fecafde61d403713c16fb5b0f7aa63',
             '8e34fd991e9e0eb858fb59d225ad1b6d39eb6972b8f0b781a05458714e1e985c',
             'managed-python-held-terminal-callback-read', 'pr1009-source-witness',
             'managed-parent-python-sixth-readback-held', 'ordinal-six-callback-read-pending-after-backing-census', 1, 38))
        for prefix, roles, revision, digest, topology, run, process, cut, capacity, count in cases:
            rows = [row for row in inputs['observations'] if row['id'].startswith(prefix)]
            self.assertEqual({row['role'] for row in rows}, set(roles))
            self.assertEqual(len(rows), len(roles))
            for row in rows:
                alias, quantity, kind = roles[row['role']]
                self.assertEqual((row['alias_class'], row['quantity'], row['quantity_type']), (alias, quantity, kind))
                self.assertEqual(row['representation'], 'managed-byte-array')
                self.assertEqual(row['identity_basis'], 'observed-identity')
                self.assertEqual(row['scope'], dict(topology=topology, run=run, process=process, attempt='original-controlled-attempt'))
                self.assertEqual(row['cut'], cut)
                self.assertEqual(row['provenance'], dict(source_type='git-blob', repository='FS-GG/FS.GG.Coordination',
                    revision=revision, path='tests/FS.GG.Coordination.Orchestration.Execution.Tests/TerminalCutCorrelationTests.fs', sha256=digest))
                expected_ownership = ('managed-endpoint-capture' if row['role'].startswith(('original-request-', 'endpoint-copy-'))
                                      else 'production-pending-state' if row['role'].endswith('state-backing')
                                      else 'selected-live-root')
                self.assertEqual(row['ownership'], expected_ownership)
            self.assertEqual(len({row['alias_class'] for row in rows if row['alias_class'] is not None}), count)
            group = next(group for group in result['groups'] if group['scope']['run'] == run)
            self.assertEqual(group['known_object_backing_capacity_bytes'], capacity)
            self.assertIsNone(group['known_object_logical_bytes'])
            self.assertIsNone(group['physical_byte_lower_bound'])
        original = next(group for group in result['groups'] if group['scope']['run'] == 'pr1005-source-witness')
        self.assertEqual(original['known_object_backing_capacity_bytes'], 11353)
        self.assertEqual(len(result['groups']), 13)
        self.assertTrue(all(group['physical_byte_lower_bound'] is None for group in result['groups']))
        self.assertEqual((result['fit'], result['admission']), ('unproved', False))
        self.assertNotIn('total_bytes', result)

    def test_protected_pending_reader_cuts_keep_separate_capacity_and_partial_identity(self):
        self.assert_reader_cuts(self.inputs)
        parser = next(row for row in self.inputs['observations'] if row['id'] == 'managed-ack-read:result-parser-input')
        self.assertIsNone(parser['alias_class'])
        pairs = {(row['left'], row['right']) for row in self.inputs['aliases']}
        for prefix, left, right in (
                ('managed-ack-read:', 'ack-supplied-backing', 'ack-state-backing'),
                ('managed-ack-read:', 'child-read-supplied-backing', 'child-read-state-backing'),
                ('managed-callback-read:', 'callback-supplied-backing', 'callback-state-backing')):
            self.assertIn((prefix + left, prefix + right), pairs)

    def test_pending_reader_source_mutations_fail_the_source_regression(self):
        for mutation in ('ack-capacity', 'child-capacity', 'callback-capacity', 'parser-distinct',
                         'endpoint-count', 'provenance', 'cut', 'process', 'task-allocation'):
            with self.subTest(mutation=mutation):
                altered = copy.deepcopy(self.inputs)
                rows = [row for row in altered['observations'] if row['id'].startswith(('managed-ack-read:', 'managed-callback-read:'))]
                if mutation.endswith('capacity'):
                    alias = {'ack-capacity': 'ack', 'child-capacity': 'child-read', 'callback-capacity': 'callback-reader'}[mutation]
                    for row in rows:
                        if row['alias_class'] == alias: row['quantity'] += 1
                elif mutation == 'parser-distinct':
                    next(row for row in rows if row['id'] == 'managed-ack-read:result-parser-input')['alias_class'] = 'invented-parser-object'
                elif mutation == 'endpoint-count':
                    altered['observations'].remove(next(row for row in rows if row['id'] == 'managed-ack-read:endpoint-copy-9'))
                elif mutation in ('provenance', 'cut', 'process'):
                    for row in rows:
                        if row['id'].startswith('managed-callback-read:'):
                            if mutation == 'provenance': row['provenance']['revision'] = '0' * 40
                            elif mutation == 'cut': row['cut'] = 'after-read-settlement'
                            else: row['scope']['process'] = 'python-child'
                else:
                    extra = copy.deepcopy(rows[0])
                    extra.update(id='managed-ack-read:task-allocation', role='task-allocation', alias_class='invented-task-bytes')
                    altered['observations'].append(extra)
                with self.assertRaises((AssertionError, ValueError)): self.assert_reader_cuts(altered)

    def test_pending_reader_aliases_refuse_cross_cut_or_old_census_join(self):
        for left, right in (
                ('managed-ack-read:ack-supplied-backing', 'managed-callback-read:callback-supplied-backing'),
                ('managed-callback-read:callback-supplied-backing', 'managed-roots:snapshot-1')):
            altered = copy.deepcopy(self.inputs)
            altered['aliases'].append(dict(left=left, right=right, basis='same-scoped-object'))
            with self.assertRaises(ValueError): ledger.reconcile(altered)
        altered = copy.deepcopy(self.inputs)
        callback = next(row for row in altered['observations'] if row['id'] == 'managed-callback-read:callback-supplied-backing')
        for row in altered['observations']:
            if row['id'].startswith('managed-ack-read:'):
                row['scope'], row['cut'] = callback['scope'], callback['cut']
        with self.assertRaises(ValueError): ledger.reconcile(altered)

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

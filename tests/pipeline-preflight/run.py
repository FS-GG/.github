#!/usr/bin/env python3
"""Observable economics, dependency and launch-gate controls."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SKILL = ROOT / '.agents/skills/pipeline-preflight'
HELPER = SKILL / 'scripts/preflight.py'
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('preflight', HELPER)
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


class PreflightTests(unittest.TestCase):
    def estimate(self, name='one-off'):
        return json.loads((SKILL / f'references/{name}.json').read_text())

    def test_one_off_rejected_even_with_perfect_detection(self):
        result = p.assess(self.estimate())
        self.assertEqual('not-cost-justified', result['decision'])
        self.assertAlmostEqual(-718.505, result['outcomes']['high']['net_benefit'])

    def test_reuse_can_pay_back_with_declared_rates(self):
        result = p.assess(self.estimate('recurring'))
        self.assertEqual('pilot-candidate', result['decision'])
        self.assertAlmostEqual(80, result['outcomes']['low']['net_benefit'])

    def test_uncertainty_not_hidden(self):
        data = self.estimate('recurring')
        data['defect_probability_low'] = 0
        self.assertEqual('uncertain', p.assess(data)['decision'])

    def test_unknown_is_not_zero(self):
        data = self.estimate()
        del data['setup_cost']
        self.assertEqual('insufficient-data', p.assess(data)['decision'])

    def test_triage_and_runtime_can_erase_benefit(self):
        data = self.estimate('recurring')
        data.update(false_block_probability=0.2, preflight_runner_minutes=2)
        result = p.assess(data)
        self.assertEqual('not-cost-justified', result['decision'])
        self.assertIsNone(result['outcomes']['high']['break_even_runs'])

    def test_bad_estimates_rejected(self):
        for key, value in [('setup_cost', -1), ('setup_cost', float('nan')),
                           ('horizon_runs', 1.5), ('detection_probability', 2),
                           ('setup_cost', True), ('assumptions', '')]:
            with self.subTest(key=key, value=value):
                data = self.estimate(); data[key] = value
                with self.assertRaises(ValueError): p.assess(data)
        data = self.estimate(); data['setup_cots'] = 1
        with self.assertRaises(ValueError): p.assess(data)
        data = self.estimate(); data['defect_probability_high'] = 0
        with self.assertRaises(ValueError): p.assess(data)

    def workflow(self):
        return {'jobs': {'build': {}, 'a': {'needs': 'build'}, 'b': {'needs': 'build'},
                         'report': {'needs': ['a', 'b']}}}

    def test_direct_and_transitive_order(self):
        self.assertEqual('passed', p.graph(self.workflow(), ['report:a,b,build'])['decision'])

    def test_missing_edge_names_the_missing_job(self):
        workflow = self.workflow(); workflow['jobs']['report']['needs'] = ['a']
        self.assertEqual(['b'], p.graph(workflow, ['report:a,b'])['missing_ancestors'])

    def test_cycles_unknowns_expressions_and_duplicates_refuse(self):
        for needs in ['report', 'missing', '${{ inputs.needs }}', ['a', 'a'], None]:
            workflow = self.workflow(); workflow['jobs']['report']['needs'] = needs
            with self.subTest(needs=needs), self.assertRaises(ValueError):
                p.graph(workflow, ['report:a,b'])

    def test_yaml_duplicate_keys_refuse(self):
        with self.assertRaises(ValueError): p.read_yaml('jobs:\n  a: {}\n  a: {}\n')

    def test_actual_pipeline_binding_and_mutation(self):
        workflow = p.read_yaml((ROOT / '.github/workflows/coord-engine.yml').read_text())
        self.assertEqual('passed', p.graph(workflow, ['engine:change-completeness'])['decision'])
        workflow['jobs']['engine'].pop('needs')
        self.assertEqual('blocked', p.graph(workflow, ['engine:change-completeness'])['decision'])

    def test_unavailable_yaml_parser_refuses(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'workflow.yml'
            source.write_text(json.dumps(self.workflow()))
            result = subprocess.run([sys.executable, '-S', str(HELPER), 'graph', str(source),
                                     '--requires', 'report:a,b'], capture_output=True, text=True)
            self.assertEqual(2, result.returncode)
            self.assertIn('requires PyYAML', json.loads(result.stderr)['message'])

    def test_launch_gate_good_bad_and_malformed(self):
        with tempfile.TemporaryDirectory() as directory:
            source = Path(directory) / 'workflow.yml'
            marker = Path(directory) / 'expensive-started'
            good = json.dumps(self.workflow())  # JSON is valid YAML; use the real CLI parser.
            bad = self.workflow(); bad['jobs']['report']['needs'] = ['a']
            for text, expected in [(good, 0), (json.dumps(bad), 1), ('jobs: [', 2)]:
                with self.subTest(expected=expected):
                    source.write_text(text); marker.unlink(missing_ok=True)
                    result = subprocess.run([sys.executable, str(HELPER), 'graph', str(source),
                                             '--requires', 'report:a,b'], capture_output=True, text=True)
                    # The same success-only boundary used by shell &&; no costly workload runs here.
                    if result.returncode == 0: marker.touch()
                    self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                    self.assertEqual(expected == 0, marker.exists())
                    message = json.loads(result.stdout or result.stderr)
                    self.assertIn(message['decision'], ('passed', 'blocked', 'error'))


if __name__ == '__main__':
    unittest.main()

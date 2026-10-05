"""Disposable controls; no CLR or fault injection into repository inputs."""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('programme_runner', Path(__file__).with_name('run.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / 'input').write_text('immutable')

    def suite(self, name, code='pass', dependencies=None, inputs=None):
        return dict(id=name, command=[sys.executable, '-c', code], inputs=inputs or ['input'],
                    prerequisites=['python'], dependsOn=dependencies or [], independent=True)

    def collect(self, selected, **kwargs):
        return runner.collect(self.root, selected, reporter=lambda _: None, **kwargs)

    def test_independent_assertions_and_success_retained(self):
        report, code = self.collect([self.suite('a', 'assert False, "first"'),
                                    self.suite('b', 'assert False, "second"'), self.suite('c')])
        self.assertEqual([r['status'] for r in report['suites']], ['failed', 'failed', 'passed'])
        self.assertEqual(code, 1)
        self.assertTrue(report['firstCause'].startswith('a:'))
        self.assertIn('second', report['suites'][1]['evidence'])

    def test_failed_dependency_and_unknown_not_launched(self):
        unknown = self.suite('unknown', 'raise RuntimeError("launched")', ['absent'])
        report, _ = self.collect([self.suite('a', 'assert False'), self.suite('b', 'assert False', ['a']), unknown])
        self.assertEqual([r['status'] for r in report['suites']], ['failed', 'blocked', 'unknown'])
        self.assertIsNone(report['suites'][1]['exit'])
        self.assertIsNone(report['suites'][2]['exit'])

    def test_shared_contamination_blocks_affected_only(self):
        (self.root / 'other').write_text('separate')
        report, _ = self.collect([self.suite('a', 'from pathlib import Path; Path("input").write_text("changed")'),
                                  self.suite('b'), self.suite('c', inputs=['other'])])
        self.assertEqual([r['status'] for r in report['suites']], ['unknown', 'blocked', 'passed'])

    def test_setup_and_missing_input_block_before_launch(self):
        selected = [self.suite('missing', inputs=['absent']), self.suite('setup')]
        selected[1]['prerequisites'] = ['dotnet']
        with patch.object(runner.shutil, 'which', return_value=None):
            report, _ = self.collect(selected)
        self.assertEqual([r['status'] for r in report['suites']], ['blocked', 'blocked'])

    def test_deadline_is_original_and_cleanup_observed(self):
        report, _ = self.collect([self.suite('a', 'import time; time.sleep(10)'), self.suite('b')], seconds=5.15)
        self.assertEqual([r['status'] for r in report['suites']], ['not-run-bound', 'not-run-bound'])
        self.assertEqual(report['suites'][0]['cleanup'], 'passed')
        self.assertLess(report['elapsedSeconds'], 2)

    def test_output_bound_hard_stops(self):
        report, _ = self.collect([self.suite('a', 'print("x" * 10000)'), self.suite('b')], output_limit=32)
        self.assertEqual(report['outputBytes'], 32)
        self.assertEqual(report['suites'][1]['status'], 'not-run-bound')

    def test_reporting_failure_preserves_cause_and_cleanup(self):
        def broken(_):
            raise OSError('fixture reporter')
        report, code = runner.collect(self.root, [self.suite('a', 'assert False')], reporter=broken)
        self.assertEqual(code, 1)
        self.assertTrue(report['firstCause'].startswith('a:'))
        self.assertTrue(report['reporting'].startswith('failed:'))
        self.assertEqual(report['cleanup'][0]['result'], 'passed')

    def test_cleanup_failure_preserves_cause_stops_next(self):
        original = runner.shutil.rmtree
        def broken_cleanup(path):
            original(path)
            raise OSError('fixture cleanup')
        with patch.object(runner.shutil, 'rmtree', side_effect=broken_cleanup):
            report, _ = self.collect([self.suite('a', 'assert False'), self.suite('b')])
        self.assertTrue(report['firstCause'].startswith('a:'))
        self.assertEqual(report['suites'][1]['reason'], 'cleanup-unobserved')
        # The failed cleanup belongs to a disposable fixture, remove it now.
        for row in report['cleanup']:
            self.assertTrue(row['result'].startswith('scratch-cleanup-failed'))

    def test_unobserved_process_cleanup_stops_next(self):
        original = runner.settle
        with patch.object(runner, 'settle', wraps=original) as settle:
            def uncertain(child, deadline):
                original(child, deadline)
                return 'unobserved-process-group'
            settle.side_effect = uncertain
            report, code = self.collect([self.suite('a'), self.suite('b')])
        self.assertEqual(code, 1)
        self.assertEqual(report['suites'][1]['reason'], 'cleanup-unobserved')

    def test_real_shell_entry_controlled_children_success_and_two_failures(self):
        target = self.root / 'tests/work-programme'
        target.mkdir(parents=True)
        for name in ['run.py', 'run.sh']:
            shutil.copy(Path(__file__).with_name(name), target / name)
        for suite in runner.suites(self.root):
            for name in suite['inputs']:
                path = self.root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                if not path.exists():
                    path.write_text('pass\n' if path.suffix == '.py' else 'fixture\n')
        binary = self.root / 'bin'
        binary.mkdir()
        dotnet = binary / 'dotnet'
        dotnet.write_text('#!/usr/bin/env python3\nimport sys\nprint("controlled FSI stand-in", sys.argv[-1])\n')
        dotnet.chmod(0o700)
        env = {**os.environ, 'PATH': str(binary) + os.pathsep + os.environ['PATH']}
        success = subprocess.run(['bash', str(target / 'run.sh')], env=env, capture_output=True, text=True, timeout=10)
        self.assertEqual(success.returncode, 0, success.stderr)
        self.assertEqual(json.loads(success.stdout)['qualification'], 'passed')
        dotnet.write_text('#!/usr/bin/env python3\nimport sys\nassert not any(x in sys.argv[-1] for x in ["acceptance", "context-delta"]), "independent fixture assertion"\n')
        failed = subprocess.run(['bash', str(target / 'run.sh')], env=env, capture_output=True, text=True, timeout=10)
        report = json.loads(failed.stdout)
        self.assertEqual(failed.returncode, 1)
        self.assertEqual([r['status'] for r in report['suites']], ['failed', 'failed', 'passed', 'passed', 'passed'])


if __name__ == '__main__':
    unittest.main()

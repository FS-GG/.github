"""Disposable controls; no CLR or fault injection into repository inputs."""
import importlib.util
import json
import os
import re
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

    def test_preflight_valid_candidate_never_launches(self):
        with patch.object(runner, 'run_child') as child:
            result = runner.preflight(self.root, [self.suite('good')])
        child.assert_not_called()
        self.assertEqual(result['status'], 'passed')
        self.assertFalse(result['executed'])
        self.assertEqual(result['qualification'], 'not-evaluated')
        self.assertIn('input', result['candidate'])

    def test_preflight_missing_tools_inputs_and_unknown_dependencies_cannot_pass(self):
        missing_tool = self.suite('tool')
        missing_tool.update(command=['dotnet', 'fsi', '--exec', 'fixture.fsx'],
                            inputs=['fixture.fsx'], prerequisites=['dotnet'])
        (self.root / 'fixture.fsx').write_text('()')
        with patch.object(runner.shutil, 'which', return_value=None):
            result = runner.preflight(self.root, [missing_tool, self.suite('input', inputs=['absent']),
                                                 self.suite('unknown', dependencies=['absent'])])
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual([r['reason'] for r in result['suites']],
                         ['setup-tool-unavailable', 'shared-input-missing-or-changed', 'unknown-dependency'])

    def test_assembled_entrypoint_omission_blocks_entire_launch(self):
        # A real file being present is insufficient when absent from the hashed closure.
        (self.root / 'caller.py').write_text('raise RuntimeError("must not launch")')
        bad = self.suite('bad')
        bad['command'] = [sys.executable, 'caller.py']
        with patch.object(runner, 'run_child') as child:
            report, code = self.collect([self.suite('earlier'), bad])
        child.assert_not_called()
        self.assertEqual(code, 1)
        self.assertEqual(report['firstCause'], 'bad: entrypoint-not-in-inputs')
        self.assertEqual(report['preflight']['status'], 'blocked')

    def test_assembled_entrypoint_positive_control_reaches_real_caller(self):
        (self.root / 'caller.py').write_text('print("real maintained caller seam")')
        good = self.suite('good', inputs=['caller.py'])
        good['command'] = [sys.executable, 'caller.py']
        report, code = self.collect([good])
        self.assertEqual(code, 0)
        self.assertIn('real maintained caller seam', report['suites'][0]['evidence'])

    def test_preflight_pins_survive_input_mutation_before_first_launch(self):
        original = runner.preflight
        def changed(root, selected):
            result = original(root, selected)
            (self.root / 'input').write_text('changed after preflight')
            return result
        with patch.object(runner, 'preflight', side_effect=changed), patch.object(runner, 'run_child') as child:
            report, code = self.collect([self.suite('first')])
        child.assert_not_called()
        self.assertEqual(code, 1)
        self.assertEqual(report['candidate'], report['preflight']['candidate'])
        self.assertEqual(report['firstCause'], 'first: shared-input-missing-or-changed')

    def test_intermediate_symlink_after_preflight_blocks_identical_target_bytes(self):
        folder = self.root / 'folder'
        folder.mkdir()
        (folder / 'input').write_text('immutable')
        original = runner.preflight
        def changed(root, selected):
            result = original(root, selected)
            folder.rename(self.root / 'original-folder')
            folder.symlink_to(self.root / 'original-folder', target_is_directory=True)
            return result
        with patch.object(runner, 'preflight', side_effect=changed), patch.object(runner, 'run_child') as child:
            report, code = self.collect([self.suite('first', inputs=['folder/input'])])
        child.assert_not_called()
        self.assertEqual(code, 1)
        self.assertEqual(report['firstCause'], 'first: shared-input-missing-or-changed')

    def test_malformed_plan_cannot_launch(self):
        for field, value in [('command', None), ('inputs', ['../outside']),
                             ('prerequisites', ['unexamined-runtime']),
                             ('command', [sys.executable, '--unexamined-mode'])]:
            with self.subTest(field=field):
                bad = self.suite('bad')
                bad[field] = value
                with patch.object(runner, 'run_child') as child:
                    report, code = self.collect([bad])
                child.assert_not_called()
                self.assertEqual(code, 1)
                self.assertEqual(report['preflight']['status'], 'blocked')

    def test_duplicate_identity_and_linked_input_cannot_launch(self):
        (self.root / 'linked').symlink_to(self.root / 'input')
        for selected in ([self.suite('same'), self.suite('same')],
                         [self.suite('linked', inputs=['linked'])]):
            with patch.object(runner, 'run_child') as child:
                report, code = self.collect(selected)
            child.assert_not_called()
            self.assertEqual(code, 1)

    def restart_fixture_reads(self):
        source = Path(__file__).with_name('context-delta.fsx').read_text()
        names = set(re.findall(r'restartFixture "([^"]+)"', source))
        return {'tests/work-programme/context-owner-restart-fixtures/' + name + '.delta-input.json'
                for name in names}

    def test_each_fsi_suite_pins_and_selects_its_actual_receiver(self):
        selected = [suite for suite in runner.suites(self.root) if suite['command'][0] == 'dotnet']
        names = ['acceptance', 'context-delta', 'context-evidence', 'routine-decisions']
        self.assertEqual([suite['id'] for suite in selected], names + [name + '-claude' for name in names])
        prefix = ('#if CLAUDE_SKILLS\n'
                  '#load "../../.claude/skills/work-programme/scripts/programme.fsx"\n'
                  '#else\n#load "../../.agents/skills/work-programme/scripts/programme.fsx"\n#endif\n')
        for index, suite in enumerate(selected):
            claude = index >= len(names)
            skill_root = '.claude' if claude else '.agents'
            other_root = '.agents' if claude else '.claude'
            self.assertIn(skill_root + '/skills/work-programme/scripts/programme.fsx', suite['inputs'])
            self.assertNotIn(other_root + '/skills/work-programme/scripts/programme.fsx', suite['inputs'])
            self.assertEqual(suite['command'], ['dotnet', 'fsi'] +
                             (['--define:CLAUDE_SKILLS'] if claude else []) +
                             ['--exec', 'tests/work-programme/' + names[index % len(names)] + '.fsx'])
            self.assertTrue(Path(__file__).with_name(names[index % len(names)] + '.fsx').read_text().startswith(prefix))

    def test_unknown_or_mismatched_fsi_receiver_blocks_before_launch(self):
        selected = next(suite for suite in runner.suites(self.root) if suite['id'] == 'acceptance-claude')
        for name in selected['inputs'] + ['.agents/skills/work-programme/scripts/programme.fsx']:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('synthetic pinned input')
        script = selected['command'][-1]
        cases = [
            ({**selected, 'command': ['dotnet', 'fsi', '--define:UNKNOWN', '--exec', script]}, 'unknown-launch-closure'),
            ({**selected, 'command': ['dotnet', 'fsi', '--define:CLAUDE_SKILLS', '--define:EXTRA', '--exec', script]},
             'unknown-launch-closure'),
            ({**selected, 'command': ['dotnet', 'fsi', '--exec', script]}, 'fsi-receiver-not-in-inputs'),
            ({**selected, 'inputs': ['.agents/skills/work-programme/scripts/programme.fsx', script]},
             'fsi-receiver-not-in-inputs'),
            ({**selected, 'inputs': [script]}, 'fsi-receiver-not-in-inputs'),
        ]
        for suite, reason in cases:
            with self.subTest(command=suite['command'], inputs=suite['inputs']):
                with patch.object(runner.shutil, 'which', return_value='/controlled/dotnet'), \
                        patch.object(runner, 'run_child') as child:
                    report, code = self.collect([suite])
                child.assert_not_called()
                self.assertEqual(code, 1)
                self.assertEqual(report['preflight']['suites'][0]['reason'], reason)

    def test_missing_claude_helper_blocks_only_its_receiver(self):
        selected = [suite for suite in runner.suites(self.root)
                    if suite['id'] in ('acceptance', 'acceptance-claude')]
        for suite in selected:
            for name in suite['inputs']:
                path = self.root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('synthetic pinned input')
        (self.root / '.claude/skills/work-programme/scripts/programme.fsx').unlink()
        with patch.object(runner.shutil, 'which', return_value='/controlled/dotnet'), \
                patch.object(runner, 'run_child', return_value=dict(
                    exit=0, reason=None, cleanup='passed', output='agents receiver', outputBytes=15)) as child:
            report, code = self.collect(selected)
        self.assertEqual(child.call_count, 1)
        self.assertEqual(child.call_args.args[0], selected[0]['command'])
        self.assertEqual(code, 1)
        self.assertEqual([row['status'] for row in report['preflight']['suites']], ['ready', 'blocked'])
        self.assertEqual([row['status'] for row in report['suites']], ['passed', 'blocked'])
        self.assertIsNone(report['suites'][1]['exit'])
        self.assertIn('.agents/skills/work-programme/scripts/programme.fsx', report['candidate'])
        self.assertNotIn('.claude/skills/work-programme/scripts/programme.fsx', report['candidate'])
        self.assertEqual(report['firstCause'], 'acceptance-claude: shared-input-missing-or-changed')

    def test_routine_decisions_inventory_covers_actual_source_reads(self):
        selected = next(suite for suite in runner.suites(self.root) if suite['id'] == 'routine-decisions')
        self.assertEqual(set(selected['inputs']), {
            '.agents/skills/work-programme/scripts/programme.fsx',
            'tests/work-programme/routine-decisions.fsx'})
        self.assertEqual(selected['command'], ['dotnet', 'fsi', '--exec',
                                              'tests/work-programme/routine-decisions.fsx'])
        self.assertEqual(selected['prerequisites'], ['dotnet'])
        self.assertEqual(selected['dependsOn'], [])

    def test_context_delta_inventory_covers_actual_restart_fixture_reads(self):
        consumed = self.restart_fixture_reads()
        selected = next(suite for suite in runner.suites(self.root) if suite['id'] == 'context-delta')
        self.assertEqual(len(consumed), 6)
        self.assertTrue(consumed <= set(selected['inputs']), consumed - set(selected['inputs']))

    def test_restart_fixture_drift_blocks_next_consumer_without_process_launch(self):
        selected = next(suite for suite in runner.suites(self.root) if suite['id'] == 'context-delta')
        selected['prerequisites'] = ['python']
        selected['command'] = [sys.executable, '-c', 'pass']
        consumed = self.restart_fixture_reads()
        for name in set(selected['inputs']) | consumed:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('synthetic original input')
        changed = self.root / sorted(consumed)[0]
        def synthetic_child(*_args):
            changed.write_text('synthetic drift')
            return dict(exit=0, reason=None, cleanup='passed', output='', outputBytes=0)
        with patch.object(runner, 'run_child', side_effect=synthetic_child) as child:
            report, code = self.collect([selected, {**selected, 'id': 'next-consumer'}])
        self.assertEqual(code, 1)
        self.assertEqual([row['status'] for row in report['suites']], ['unknown', 'blocked'])
        self.assertEqual(report['suites'][0]['reason'], 'shared-input-changed')
        self.assertEqual(child.call_count, 1)

    def restart_python_suites(self):
        return [suite for suite in runner.suites(self.root)
                if suite['id'] in ('context-restart-structure', 'context-restart-watcher')]

    def test_restart_python_inventory_covers_actual_transitive_reads(self):
        source_root = Path(__file__).resolve().parents[2]
        prefix = 'tests/work-programme/context-owner-restart-fixtures/'
        index = json.loads((source_root / (prefix + 'fixture-index.json')).read_text())
        structure, watcher = self.restart_python_suites()
        consumed = {prefix + 'structural-check.py', prefix + 'fixture-index.json'}
        consumed.update(prefix + name + suffix for name in index['fixtures']
                        for suffix in ('.delta-input.json', '.expectations.json'))
        self.assertEqual(set(structure['inputs']), consumed)
        self.assertEqual(set(watcher['inputs']),
                         {prefix + 'watcher-regression.py', 'tools/routine-delivery.py'})
        for suite in (structure, watcher):
            self.assertEqual(suite['command'], [sys.executable, suite['inputs'][0]])
            self.assertEqual(suite['prerequisites'], ['python'])
            self.assertEqual(suite['dependsOn'], [])
            self.assertTrue(suite['independent'])

    def test_each_restart_python_input_drift_invalidates_and_blocks_next_reader(self):
        for selected in self.restart_python_suites():
            for changed_name in selected['inputs']:
                with self.subTest(suite=selected['id'], input=changed_name):
                    for name in selected['inputs']:
                        path = self.root / name
                        path.parent.mkdir(parents=True, exist_ok=True)
                        path.write_text('synthetic original input')
                    changed = self.root / changed_name
                    def synthetic_child(*_args):
                        changed.write_text('synthetic drift')
                        return dict(exit=0, reason=None, cleanup='passed',
                                    output='synthetic evidence', outputBytes=18)
                    with patch.object(runner, 'run_child', side_effect=synthetic_child) as child:
                        report, code = self.collect([selected, {**selected, 'id': 'next-reader'}])
                    self.assertEqual(code, 1)
                    self.assertIn(changed_name, report['candidate'])
                    self.assertEqual([row['status'] for row in report['suites']], ['unknown', 'blocked'])
                    self.assertEqual(report['suites'][0]['reason'], 'shared-input-changed')
                    self.assertEqual(report['suites'][1]['reason'], 'shared-input-missing-or-changed')
                    self.assertEqual(child.call_count, 1)

    def test_missing_restart_input_blocks_only_its_suite_and_retains_first_failure(self):
        selected = self.restart_python_suites()
        for suite in selected:
            for name in suite['inputs']:
                path = self.root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('synthetic original input')
        (self.root / selected[0]['inputs'][-1]).unlink()
        with patch.object(runner, 'run_child', return_value=dict(
                exit=1, reason=None, cleanup='passed', output='watcher assertion', outputBytes=17)) as child:
            report, code = self.collect(selected)
        self.assertEqual(code, 1)
        self.assertEqual([row['status'] for row in report['suites']], ['blocked', 'failed'])
        self.assertEqual(report['firstCause'], 'context-restart-structure: shared-input-missing-or-changed')
        self.assertEqual(report['suites'][1]['evidence'], 'watcher assertion')
        self.assertEqual(report['omittedCoverage'], ['context-restart-structure'])
        self.assertEqual(child.call_count, 1)

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
        (self.root / 'setup.fsx').write_text('()')
        selected[1]['command'] = ['dotnet', 'fsi', '--exec', 'setup.fsx']
        selected[1]['inputs'] = ['setup.fsx']
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

    def test_natural_exit_keeps_leader_until_last_group_signal(self):
        original_popen = runner.subprocess.Popen
        original_signal = runner.os.killpg
        children = []
        signals = []
        def launch(*args, **kwargs):
            child = original_popen(*args, **kwargs)
            children.append(child)
            return child
        def signal_group(group, sig):
            self.assertIsNone(children[-1].returncode, 'leader reaped before group signal')
            signals.append((group, sig))
            return original_signal(group, sig)
        with patch.object(runner.subprocess, 'Popen', side_effect=launch), patch.object(runner.os, 'killpg', side_effect=signal_group):
            report, code = self.collect([self.suite('natural', 'print("natural exit")')])
        self.assertEqual(code, 0)
        self.assertEqual(report['suites'][0]['exit'], 0)
        self.assertEqual(len(signals), 1)
        self.assertEqual(children[0].returncode, 0)
        # A previously reaped leader must never address its reusable number.
        with patch.object(runner.os, 'killpg') as signal_group:
            self.assertEqual(runner.settle(children[0], runner.time.monotonic() + 1), 'leader-already-reaped')
            signal_group.assert_not_called()

    def test_real_shell_entry_controlled_children_success_and_independent_twin_failures(self):
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
        dotnet.write_text('#!/usr/bin/env python3\nimport sys\nprint("controlled FSI stand-in", sys.argv[1:])\n')
        dotnet.chmod(0o700)
        env = {**os.environ, 'PATH': str(binary) + os.pathsep + os.environ['PATH']}
        # The real shell command forwards the preflight flag without invoking a suite.
        marker = self.root / 'launched'
        dotnet.write_text('#!/usr/bin/env python3\nfrom pathlib import Path\nPath(' + repr(str(marker)) + ').write_text("launched")\n')
        ready = subprocess.run(['bash', str(target / 'run.sh'), '--preflight'], env=env,
                               capture_output=True, text=True, timeout=10)
        self.assertEqual(ready.returncode, 0, ready.stderr)
        self.assertEqual(json.loads(ready.stdout)['qualification'], 'not-evaluated')
        self.assertFalse(marker.exists())
        missing = self.root / 'tests/work-programme/acceptance.fsx'
        original = missing.read_bytes()
        missing.unlink()
        blocked = subprocess.run(['bash', str(target / 'run.sh'), '--preflight'], env=env,
                                 capture_output=True, text=True, timeout=10)
        self.assertEqual(blocked.returncode, 1)
        self.assertEqual(json.loads(blocked.stdout)['status'], 'blocked')
        self.assertFalse(marker.exists())
        missing.write_bytes(original)
        dotnet.write_text('#!/usr/bin/env python3\nimport sys\nprint("controlled FSI stand-in", sys.argv[1:])\n')
        success = subprocess.run(['bash', str(target / 'run.sh')], env=env, capture_output=True, text=True, timeout=10)
        self.assertEqual(success.returncode, 0, success.stderr)
        successful = json.loads(success.stdout)
        self.assertEqual(successful['qualification'], 'passed')
        fsi_rows = successful['suites'][:8]
        for row in fsi_rows:
            self.assertEqual('--define:CLAUDE_SKILLS' in row['evidence'], row['id'].endswith('-claude'))
        dotnet.write_text('#!/usr/bin/env python3\nimport sys\nassert not any(x in sys.argv[-1] for x in ["acceptance", "context-delta"]), "independent fixture assertion"\n')
        failed = subprocess.run(['bash', str(target / 'run.sh')], env=env, capture_output=True, text=True, timeout=10)
        report = json.loads(failed.stdout)
        self.assertEqual(failed.returncode, 1)
        self.assertEqual([r['status'] for r in report['suites']],
                         ['failed', 'failed', 'passed', 'passed'] * 2 + ['passed'] * 5)
        self.assertEqual(report['firstCause'], 'acceptance: exit 1')
        self.assertEqual(report['omittedCoverage'], [])


if __name__ == '__main__':
    unittest.main()

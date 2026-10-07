#!/usr/bin/env python3
"""Behavioral controls for bounded reads and honest native context coverage."""
import contextlib
import copy
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('context', ROOT / '.agents/skills/work-programme/scripts/context.py')
ctx = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(ctx)


class ContextTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.pin = self.file('data.json', json.dumps({'boundaries': {'native': 'pending'}, 'secret': 'DO-NOT-EXPOSE'}))
        self.spec = dict(schema='fsgg.programme.context-view-input/1', artifact=self.pin,
                         trust='data', access='allowed', obligationsComplete=False, reason='current boundary',
                         selection={'pointers': ['/boundaries/native']}, maximumBytes=4096)

    def tearDown(self):
        self.tmp.cleanup()

    def file(self, name, text):
        path = self.root / name
        path.write_text(text)
        body = path.read_bytes()
        return dict(path=str(path), bytes=len(body), sha256=hashlib.sha256(body).hexdigest())

    def test_selected_fields_do_not_expose_other_data(self):
        result = ctx.view(self.spec)
        self.assertEqual(result['selected'], {'/boundaries/native': 'pending'})
        self.assertNotIn('DO-NOT-EXPOSE', json.dumps(result))

    def test_raw_drift_refuses_before_selection(self):
        Path(self.pin['path']).write_text('{"boundaries":{"native":"passed"}}')
        with self.assertRaisesRegex(ctx.Refusal, 'pin-drift'):
            ctx.view(self.spec)

    def test_read_access_time_change_is_not_content_drift(self):
        os.utime(self.pin['path'], (1, time.time()))
        self.assertEqual(ctx.view(self.spec)['selected'], {'/boundaries/native': 'pending'})

    def test_missing_pointer_refuses(self):
        self.spec['selection'] = {'pointers': ['/absent']}
        with self.assertRaises(KeyError):
            ctx.view(self.spec)

    def test_json_pointer_escapes_and_arrays(self):
        self.spec['artifact'] = self.file('escaped.json', '{"a/b":{"~key":["selected"]}}')
        self.spec['selection'] = {'pointers': ['/a~1b/~0key/0']}
        self.assertEqual(list(ctx.view(self.spec)['selected'].values()), ['selected'])

    def test_duplicate_json_keys_refuse(self):
        self.spec['artifact'] = self.file('duplicate.json', '{"x":1,"x":2}')
        self.spec['selection'] = {'pointers': ['/x']}
        with self.assertRaisesRegex(ctx.Refusal, 'duplicate-json-key'):
            ctx.view(self.spec)

    def test_denied_access_does_not_read(self):
        self.spec['access'] = 'unknown'
        self.spec['artifact']['path'] = '/does/not/exist'
        with self.assertRaisesRegex(ctx.Refusal, 'access-unestablished'):
            ctx.view(self.spec)

    def test_symlink_ancestor_refuses(self):
        link = self.root / 'alias'
        link.symlink_to(self.root, target_is_directory=True)
        self.spec['artifact']['path'] = str(link / 'data.json')
        with self.assertRaisesRegex(ctx.Refusal, 'linked-path'):
            ctx.view(self.spec)

    def test_oversize_refuses_without_truncation(self):
        self.spec['maximumBytes'] = 1
        with self.assertRaisesRegex(ctx.Refusal, 'selection-oversize'):
            ctx.view(self.spec)

    def test_instruction_excerpt_refuses(self):
        self.spec.update(trust='instruction', obligationsComplete=True)
        with self.assertRaisesRegex(ctx.Refusal, 'instruction-must-be-complete-whole-file'):
            ctx.view(self.spec)

    def test_whole_instruction_preserves_last_obligation(self):
        self.spec.update(trust='instruction', obligationsComplete=True, selection={'whole': True},
                         artifact=self.file('instructions.md', 'first\nlast stopping rule'))
        self.assertEqual(ctx.view(self.spec)['selected'], 'first\nlast stopping rule')

    def test_incomplete_instruction_discovery_refuses(self):
        self.spec.update(trust='instruction', selection={'whole': True})
        with self.assertRaisesRegex(ctx.Refusal, 'instruction-must-be-complete-whole-file'):
            ctx.view(self.spec)

    def test_lines_are_exact_and_invalid_ranges_refuse(self):
        self.spec.update(artifact=self.file('lines.txt', 'one\ntwo\nthree'), selection={'lines': [2, 3]})
        self.assertEqual(ctx.view(self.spec)['selected'], 'two\nthree')
        self.spec['selection'] = {'lines': [1, 4]}
        with self.assertRaisesRegex(ctx.Refusal, 'invalid-lines'):
            ctx.view(self.spec)

    def review(self):
        return dict(schema='fsgg.programme.context-review-input/1', sourceRevision='a'*40,
                    obligations=[dict(id='first-cause', question='Is failure retained?', viewIds=['change'])],
                    views=[dict(id='change', input=copy.deepcopy(self.spec))], unchanged=[self.pin],
                    unknowns=['native acceptance pending'], maximumBytes=4096)

    def test_review_verifies_unchanged_without_importing_bodies(self):
        result = ctx.review(self.review())
        self.assertEqual(result['unchangedCount'], 1)
        self.assertNotIn('DO-NOT-EXPOSE', json.dumps(result))
        self.assertIn('root semantic review', result['authority'])

    def test_uncovered_review_obligation_refuses(self):
        spec = self.review()
        spec['obligations'][0]['viewIds'] = ['omitted']
        with self.assertRaisesRegex(ctx.Refusal, 'uncovered-review-obligation'):
            ctx.review(spec)

    def test_review_closure_drift_refuses(self):
        spec = self.review()
        spec['unchanged'][0] = self.file('dependency', 'original')
        Path(spec['unchanged'][0]['path']).write_text('changed')
        with self.assertRaisesRegex(ctx.Refusal, 'pin-drift'):
            ctx.review(spec)

    def report(self, reset=False, baseline=True, tail=''):
        def event(stamp, count):
            return dict(timestamp='2026-10-07T'+stamp+'Z', type='event_msg', payload=dict(type='token_count', info=dict(
                total_token_usage=dict(input_tokens=count, cached_input_tokens=count//2,
                                       output_tokens=count//10, reasoning_output_tokens=count//20),
                last_token_usage=dict(input_tokens=count))))
        rows = [dict(timestamp='2026-10-07T07:00:00Z', type='session_meta', payload=dict(id='native-root', agent_path=None))]
        if baseline:
            rows.append(event('07:59:00', 100))
        rows += [event('08:01:00', 200), dict(timestamp='2026-10-07T08:02:00Z', type='compacted', payload={}),
                 event('08:03:00', 50 if reset else 300),
                 dict(timestamp='2026-10-07T08:04:00Z', type='response_item', payload=dict(type='custom_tool_call_output', output='PRIVATE CONTENT'))]
        pin = self.file('session.jsonl', '\n'.join(json.dumps(r) for r in rows)+'\n'+tail)
        return dict(schema='fsgg.programme.context-report-input/1', start='2026-10-07T08:00:00Z', end='2026-10-07T08:05:00Z',
                    sessions=[dict(sessionId='native-root', artifact=pin)], expectedSessionIds=['native-root', 'missing-worker'],
                    helperDirectories=[])

    def test_native_compaction_counter_and_missing_coverage(self):
        result = ctx.usage(self.report())
        row = result['sessions'][0]
        self.assertEqual(row['compactions'], ['2026-10-07T08:02:00Z'])
        self.assertEqual(row['tokenCounterDelta']['input_tokens'], 200)
        self.assertEqual(result['missingSessionIds'], ['missing-worker'])
        self.assertNotIn('PRIVATE CONTENT', json.dumps(result))
        self.assertIn('unestablished', result['attribution'])

    def test_native_text_blocks_are_counted_without_exposing_content(self):
        spec = self.report()
        path = Path(spec['sessions'][0]['artifact']['path'])
        rows = [json.loads(line) for line in path.read_text().splitlines()]
        rows[-1]['payload']['output'] = [{'type': 'text', 'text': 'secret one'},
                                        {'type': 'text', 'text': 'secret two'}, {'type': 'image'}]
        spec['sessions'][0]['artifact'] = self.file('session.jsonl', '\n'.join(json.dumps(r) for r in rows)+'\n')
        result = ctx.usage(spec)
        self.assertEqual(result['sessions'][0]['toolOutputTextBytes'], 20)
        self.assertEqual(result['sessions'][0]['unmeasuredOutputBlocks'], 1)
        self.assertNotIn('secret one', json.dumps(result))

    def test_counter_reset_is_unknown_not_negative_or_zero(self):
        self.assertIsNone(ctx.usage(self.report(reset=True))['sessions'][0]['tokenCounterDelta'])

    def test_missing_baseline_is_unknown(self):
        self.assertIsNone(ctx.usage(self.report(baseline=False))['sessions'][0]['tokenCounterDelta'])

    def test_incomplete_tail_is_explicit(self):
        result = ctx.usage(self.report(tail='{"timestamp":"unfinished'))
        self.assertEqual(result['sessions'][0]['incompleteTailRecords'], 1)

    def test_complete_malformed_record_refuses(self):
        with self.assertRaisesRegex(ctx.Refusal, 'malformed-session-record'):
            ctx.usage(self.report(tail='{"broken":}\n'))

    def test_duplicate_complete_tail_without_newline_refuses(self):
        with self.assertRaisesRegex(ctx.Refusal, 'duplicate-json-key'):
            ctx.usage(self.report(tail='{"type":"x","type":"y"}'))

    def test_session_identity_mismatch_refuses(self):
        spec = self.report()
        spec['sessions'][0]['sessionId'] = 'missing-worker'
        with self.assertRaisesRegex(ctx.Refusal, 'session-identity-mismatch'):
            ctx.usage(spec)

    def test_named_helper_directories_do_not_drop_child_activity(self):
        spec = self.report()
        for name in ['parent', 'worker']:
            directory = self.root / name
            directory.mkdir()
            (directory/'20261007T080100000-example.measure.json').write_text(json.dumps(dict(
                schema='fsgg.programme.measurement/1', inputBytes=3, outputBytes=2, artifactBytes=4)))
            spec['helperDirectories'].append(str(directory))
        result = ctx.usage(spec)
        self.assertEqual(result['helper']['operations'], 2)
        self.assertEqual(result['helper']['inputBytes'], 6)

    def command(self):
        pin = self.file('placeholder', 'never executed')
        operations = self.file('operations.json', json.dumps(dict(operationPins=[self.pin])))
        binding = self.file('binding.json', json.dumps(dict(operationIndex=0, operationsPin=operations,
                             recipePin=self.pin, expectedAttempt='original-attempt', cpu=0)))
        now = time.monotonic()
        window = self.file('window.json', json.dumps(dict(attempt='original-attempt', cpu=0,
                           issuedMonotonic=now-1, ownerWorkEndMonotonic=now+30, ownerCleanupEndMonotonic=now+40)))
        return dict(schema='fsgg.programme.guard-command-input/1', python=pin, launcher=pin, operations=operations,
                    window=window, runtime=pin, binding=binding, bindingSha256=binding['sha256'],
                    index=0, receipt=str(self.root/'unused-receipt'))

    def test_guard_command_preserves_window_and_does_not_execute(self):
        spec = self.command()
        result = ctx.command(spec)
        self.assertFalse(result['executed'])
        self.assertFalse(result['windowRenewed'])
        self.assertIn(spec['bindingSha256'], result['argv'])
        self.assertFalse(Path(spec['receipt']).exists())

    def test_existing_receipt_refuses(self):
        spec = self.command()
        Path(spec['receipt']).write_text('prior attempt')
        with self.assertRaisesRegex(ctx.Refusal, 'receipt-already-exists'):
            ctx.command(spec)

    def test_command_cross_attempt_refuses(self):
        spec = self.command()
        value = json.loads(Path(spec['window']['path']).read_text())
        value['attempt'] = 'replacement-attempt'
        spec['window'] = self.file('window.json', json.dumps(value))
        with self.assertRaisesRegex(ctx.Refusal, 'command-window-join'):
            ctx.command(spec)

    def test_cli_retains_oversize_result_as_handle(self):
        self.spec.update(artifact=self.file('large.txt', 'a'*5000), selection={'whole': True}, maximumBytes=6000)
        source = self.file('input.json', json.dumps(self.spec))
        output = self.root/'private'
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = ctx.main(['view', source['path'], str(output), '--input-sha256', source['sha256'], '--stdout-bytes', '1024'])
        self.assertEqual(code, 0)
        response = json.loads(stdout.getvalue())
        self.assertEqual(response['status'], 'retained')
        self.assertLess(len(stdout.getvalue().encode()), 1024)
        self.assertEqual(json.loads(Path(response['artifact']['path']).read_text())['selected'], 'a'*5000)
        self.assertEqual(output.stat().st_mode & 0o777, 0o700)
        self.assertEqual(Path(response['artifact']['path']).stat().st_mode & 0o777, 0o600)

    def test_cli_input_pin_drift_never_exposes_content(self):
        source = self.file('input.json', json.dumps(self.spec))
        stdout = io.StringIO()
        with contextlib.redirect_stdout(stdout):
            code = ctx.main(['view', source['path'], str(self.root/'private'), '--input-sha256', '0'*64])
        self.assertEqual(code, 3)
        self.assertNotIn('DO-NOT-EXPOSE', stdout.getvalue())


if __name__ == '__main__':
    unittest.main()

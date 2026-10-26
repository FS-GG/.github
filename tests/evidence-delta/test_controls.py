#!/usr/bin/env python3
"""Causal local controls: immutable historical reuse, hostile inputs, partial GETs and one owner."""
import argparse
import copy
import hashlib
import contextlib
import io
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
def module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value
D = module('delta', 'scripts/evidence-delta.py')
O = module('observer', 'tools/routine-issue-observer.py')

class Controls(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.addCleanup(self.tmp.cleanup)
        self.doc = {'schema': D.SCHEMA, 'source': {'head': 'a'*40, 'tree': 'b'*40},
                    'files': [], 'runs': [{'id': 'original-1', 'attempt': 1, 'outcome': 'failure',
                    'clocks': {'start': 1, 'deadline': 10}, 'limits': {'wallSeconds': 9}}],
                    'dependencies': [{'id': 'dep', 'sha': 'abc'}], 'evidence': [],
                    'acceptance': {'scope': 'source only', 'reference': 'external-review'}, 'gaps': ['native unobserved']}
        self.add_file('source', b'old')
    def add_file(self, name, raw):
        (self.root / name).write_bytes(raw)
        self.doc['files'].append({'id': name, 'kind': 'source', 'path': name,
                                 'sha256': D.sha(raw), 'bytes': len(raw), 'gitBlob': 'c'*40})
    def args(self):
        path = self.root / 'candidate.json'
        path.write_text(json.dumps(self.doc))
        return argparse.Namespace(manifest=str(path), root=str(self.root), baseline=None, baseline_sha256=None,
                                  baseline_receipt=None, baseline_receipt_sha256=None)
    def baseline(self):
        args = self.args()
        receipt = D.verify(args)
        base = self.root / 'base.json'
        base.write_bytes(Path(args.manifest).read_bytes())
        proof = self.root / 'proof.json'
        proof.write_text(json.dumps(receipt))
        return {'baseline': str(base), 'baseline_sha256': D.sha(base.read_bytes()),
                'baseline_receipt': str(proof), 'baseline_receipt_sha256': D.sha(proof.read_bytes())}
    def test_reuse_does_not_read_or_authenticate_current_candidate(self):
        baseline = self.baseline()
        (self.root / 'source').unlink()
        args = self.args()
        vars(args).update(baseline)
        receipt = D.verify(args)
        self.assertEqual(receipt['currentPayloadReads'], [])
        self.assertEqual(receipt['historicalByteProofReuse'], ['source'])
        self.assertEqual(receipt['sourceGitCorrespondence'], 'unknown')
        self.assertEqual(receipt['nativeAcceptance'], 'unknown')
        self.assertEqual(receipt['runs'][0]['outcome'], 'failure')
        self.assertIsNone(receipt['measurements']['tokenSavings'])
    def test_changed_payload_and_dependency_delta(self):
        baseline = self.baseline()
        self.doc['files'].clear()
        self.add_file('new', b'changed')
        self.doc['dependencies'][0]['sha'] = 'def'
        args = self.args(); vars(args).update(baseline)
        result = D.verify(args)
        self.assertEqual(result['delta']['files']['removed'], ['source'])
        self.assertEqual(result['currentPayloadReads'], ['new'])
        self.assertEqual(result['delta']['dependencies']['changed'], ['dep'])
        (self.root / 'new').write_bytes(b'wrong!!')
        with self.assertRaises(D.EvidenceError): D.verify(args)
    def test_baseline_pins_cannot_be_replaced(self):
        baseline = self.baseline()
        args = self.args(); vars(args).update(baseline)
        Path(args.baseline_receipt).write_text('{}')
        with self.assertRaises(D.EvidenceError): D.verify(args)
        args.baseline_receipt_sha256 = D.sha(b'{}')
        with self.assertRaises(D.EvidenceError): D.verify(args)
        args.baseline_sha256 = None
        with self.assertRaises(D.EvidenceError): D.verify(args)
    def test_terminal_original_cannot_be_relabelled(self):
        baseline = self.baseline()
        self.doc['runs'][0]['outcome'] = 'success'
        args = self.args(); vars(args).update(baseline)
        with self.assertRaises(D.EvidenceError): D.verify(args)
    def test_original_clock_limit_type_mutations_are_rejected(self):
        baseline = self.baseline()
        for field, key, replacement in [('clocks', 'start', True), ('clocks', 'start', 1.0),
                                         ('limits', 'wallSeconds', 9.0)]:
            candidate = copy.deepcopy(self.doc)
            self.doc['runs'][0][field][key] = replacement
            args = self.args(); vars(args).update(baseline)
            with self.assertRaises(D.EvidenceError): D.verify(args)
            self.doc = candidate
        self.assertEqual(D.changes({'dep': {'x': 1}}, {'dep': {'x': True}})['changed'], ['dep'])
        self.assertEqual(D.changes({'dep': {'x': 1}}, {'dep': {'x': 1.0}})['changed'], ['dep'])
    def test_nonfinite_json_and_numeric_overflow_are_rejected(self):
        path = self.root / 'numbers.json'
        for number in ('NaN', 'Infinity', '-Infinity', '1e999'):
            path.write_text('{"number":' + number + '}')
            with self.assertRaises(ValueError): D.load(path)
    def test_later_settlement_metadata_preserves_original_failure(self):
        baseline = self.baseline()
        self.doc['observations'] = {'currentManager': 'absent'}
        args = self.args(); vars(args).update(baseline)
        self.assertEqual(D.verify(args)['runs'][0]['outcome'], 'failure')
    def test_reported_metrics_stay_reported_and_missing_stays_unknown(self):
        result = D.verify(self.args())
        self.assertIsNone(result['measurements']['reportedCoordinatorInterventions'])
        self.doc['reportedMetrics'] = {'coordinatorInterventions': 2,
            'sourceReadyAt': '2026-10-26T10:00:00Z', 'firstNativeAt': '2026-10-26T10:02:00Z',
            'reference': 'existing-primary-issue'}
        result = D.verify(self.args())
        self.assertEqual(result['measurements']['reportedSourceReadyToFirstNativeSeconds'], 120)
        self.assertEqual(result['measurements']['reportedCoordinatorInterventions'], 2)
        self.assertIsNone(result['measurements']['tokenSavings'])
        self.doc['reportedMetrics']['coordinatorInterventions'] = True
        with self.assertRaises(D.EvidenceError): D.verify(self.args())
    def test_additive_cli_route(self):
        args = self.args()
        result = subprocess.run([sys.executable, str(ROOT / 'scripts/evidence-manifest.py'),
            'verify-delta', args.manifest, '--root', args.root], capture_output=True, timeout=5)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout)['byteVerification'], 'verified')
    def test_duplicate_json_id_path_and_traversal(self):
        args = self.args()
        Path(args.manifest).write_text('{"schema":1,"schema":2}')
        with self.assertRaises(D.EvidenceError): D.verify(args)
        for mutation in ('duplicate-id', 'duplicate-path', 'traversal'):
            doc = copy.deepcopy(self.doc)
            if mutation.startswith('duplicate'):
                doc['files'].append(dict(doc['files'][0]))
                if mutation == 'duplicate-path': doc['files'][1]['id'] = 'other'
            else: doc['files'][0]['path'] = '../escape'
            with self.assertRaises(D.EvidenceError): D.manifest(doc)
    def test_actual_symlink_directory_fifo_and_size_controls(self):
        args = self.args()
        payload = self.root / 'source'
        payload.unlink(); payload.symlink_to('/etc/passwd')
        with self.assertRaises(OSError): D.verify(args)
        payload.unlink(); os.mkfifo(payload)
        with self.assertRaises(D.EvidenceError): D.verify(args)
        payload.unlink(); payload.mkdir()
        with self.assertRaises(D.EvidenceError): D.verify(args)
        payload.rmdir(); payload.write_bytes(b'oversize')
        with self.assertRaises(D.EvidenceError): D.verify(args)
        directory = self.root / 'real'; directory.mkdir()
        (directory / 'f').write_bytes(b'x')
        (self.root / 'link').symlink_to(directory, target_is_directory=True)
        with self.assertRaises(OSError): D.read_regular(self.root / 'link/f', 1)
    def test_actual_mutation_during_read(self):
        path = self.root / 'source'
        read = os.read
        def mutate(fd, count):
            result = read(fd, count)
            if result: path.write_bytes(b'new')
            return result
        with patch.object(D.os, 'read', mutate):
            with self.assertRaises(D.EvidenceError): D.read_regular(path, 10)
    def cursor(self):
        return {'schema': O.SCHEMA, 'repository': 'FS-GG/.github', 'issues': [4516],
                'originalRequest': 'original', 'successful': True,
                'comments': {'4516': [{'id': 1, 'updatedAt': 'then', 'bodySha256': 'a'*64}]}}
    def test_edited_message_and_partial_population_cursor(self):
        base = self.cursor()
        changed = copy.deepcopy(base['comments']['4516'])
        changed[0]['bodySha256'] = 'b'*64
        result = O.observe('FS-GG/.github', [4516], 'original', 30, base,
                           read=lambda *args: changed, clock=lambda: 0)
        self.assertEqual(result['event'], 'change')
        self.assertEqual(result['delta']['4516']['changed'], ['1'])
        def partial(repo, issue, page, deadline):
            if page == 1: return [{'id': n+1, 'updatedAt': 'then', 'bodySha256': 'a'*64} for n in range(100)]
            raise O.ObservationError('partial read', partial_bytes=23)
        result = O.observe('FS-GG/.github', [4516], 'original', 30, base, read=partial, clock=lambda: 0)
        self.assertEqual(result['event'], 'observation-failure')
        self.assertEqual(result['lastCompleteCursor'], base)
        self.assertEqual(result['partialBytes'], 23)
        self.assertEqual(result['completePagesInFailedSample'], 1)
    def test_short_remaining_window_never_starts_read(self):
        base = self.cursor()
        with patch.object(O.subprocess, 'Popen') as spawn:
            with self.assertRaises(O.Deadline): O.capture(['gh'], 0.424, clock=lambda: 0)
            spawn.assert_not_called()
        result = O.observe('FS-GG/.github', [4516], 'original', 1, base,
                           read=lambda *a: self.fail('must not read'), clock=lambda: 0)
        self.assertEqual(result['event'], 'deadline')
        self.assertEqual(result['lastCompleteCursor'], base)
    def test_actual_cooperative_flock(self):
        root = self.root / 'private'
        fd = O.lock_operation(root, 'FS-GG/.github', [4516], 'original')
        try:
            with self.assertRaises(BlockingIOError): O.lock_operation(root, 'FS-GG/.github', [4516], 'original')
        finally: os.close(fd)
        fd = O.lock_operation(root, 'FS-GG/.github', [4516], 'original'); os.close(fd)
    def test_actual_inert_child_partial_failure_and_oversize(self):
        popen = subprocess.Popen
        for program, reason in [("import sys;sys.stdout.write('partial');sys.stdout.flush();sys.exit(4)", 'GET failed'),
                                ("import sys;sys.stdout.write('x'*300000);sys.stdout.flush()", 'byte bound')]:
            # Actual fresh inert Python child; replace only the fixed gh transport at the test boundary.
            def inert(*args, **kwargs): return popen([sys.executable, '-I', '-c', program], **kwargs)
            with patch.object(O.subprocess, 'Popen', inert):
                with self.assertRaises(O.ObservationError) as caught:
                    O.capture(['gh'], O.time.monotonic()+10)
                self.assertIn(reason, str(caught.exception))
                self.assertGreater(caught.exception.partial_bytes, 0)
                self.assertIn('direct-child-reaped', caught.exception.cleanup)
    def observer_args(self, request='original'):
        path = self.root / 'cursor.json'
        path.write_text(json.dumps(self.cursor()))
        return ['watch', '--repo', 'FS-GG/.github', '--issue', '4516', '--original-request', request,
                '--seconds', '30', '--custody-dir', str(self.root / 'private'),
                '--baseline', str(path), '--baseline-sha256', D.sha(path.read_bytes())]
    def test_original_request_mismatch_refuses_before_lock_or_get(self):
        args = self.observer_args('different-original')
        output = io.StringIO()
        with patch.object(O, 'lock_operation') as lock, patch.object(O.subprocess, 'Popen') as spawn:
            with contextlib.redirect_stdout(output): self.assertEqual(O.main(args), 3)
            lock.assert_not_called()
            spawn.assert_not_called()
        self.assertIsNone(json.loads(output.getvalue())['lastCompleteCursor'])
    def test_actual_waiting_child_cancelled_and_reaped_before_lock_release(self):
        args = self.observer_args()
        popen, selector_factory, close, acquire = subprocess.Popen, O.selectors.DefaultSelector, os.close, O.lock_operation
        children, locks, release_observations = [], [], []
        def inert(*args, **kwargs):
            child = popen([sys.executable, '-I', '-c',
                "import sys,time;sys.stdout.write('ready');sys.stdout.flush();time.sleep(20)"], **kwargs)
            children.append(child)
            return child
        class InterruptSelector:
            def __init__(self): self.inner = selector_factory()
            def __getattr__(self, name): return getattr(self.inner, name)
            def select(self, timeout):
                events = self.inner.select(timeout)
                if events: raise KeyboardInterrupt()
                return events
        def tracked_lock(*args):
            fd = acquire(*args); locks.append(fd); return fd
        def tracked_close(fd):
            if fd in locks:
                release_observations.append(children[0].returncode)
                self.assertIsNotNone(children[0].returncode, 'child must be reaped before lock release')
            return close(fd)
        output = io.StringIO()
        with patch.object(O.subprocess, 'Popen', inert), patch.object(O.selectors, 'DefaultSelector', InterruptSelector), \
             patch.object(O, 'lock_operation', tracked_lock), patch.object(O.os, 'close', tracked_close):
            with contextlib.redirect_stdout(output): self.assertEqual(O.main(args), 3)
        self.assertEqual(len(release_observations), 1)
        result = json.loads(output.getvalue())
        self.assertEqual(result['readerPrimaryErrorType'], 'KeyboardInterrupt')
        self.assertEqual(result['lastCompleteCursor'], self.cursor())
        self.assertEqual(result['readerCleanup'], 'direct-child-reaped; descendants-unverified')
        self.assertLess(children[0].returncode, 0)
    def test_setup_cancellation_after_spawn_also_reaps(self):
        popen, children = subprocess.Popen, []
        def inert(*args, **kwargs):
            child = popen([sys.executable, '-I', '-c', 'import time;time.sleep(20)'], **kwargs)
            children.append(child); return child
        with patch.object(O.subprocess, 'Popen', inert), \
             patch.object(O.selectors, 'DefaultSelector', side_effect=KeyboardInterrupt):
            with self.assertRaises(O.ObservationError) as caught: O.capture(['gh'], O.time.monotonic()+10)
        self.assertEqual(caught.exception.primary_error, 'KeyboardInterrupt')
        self.assertIsNotNone(children[0].returncode)
    def test_fixed_get_route_and_no_raw_body_output(self):
        seen = []
        def capture(command, deadline):
            seen.append(command)
            return b'[{"id":1,"updated_at":"2026-10-26T10:00:00Z","body":"private text"}]'
        with patch.object(O, 'capture', capture): rows = O.get_page('FS-GG/.github', 4516, 1, 10)
        self.assertEqual(seen[0][0:7], ['gh','api','--hostname','github.com','--method','GET','repos/FS-GG/.github/issues/4516/comments?per_page=100&page=1'])
        self.assertNotIn('private text', json.dumps(rows))
        rows[0]['id'] = True
        base = self.cursor(); base['comments']['4516'] = rows
        with self.assertRaises(O.safe.EvidenceError): O.validate_cursor(base, 'FS-GG/.github', [4516], 'original')

if __name__ == '__main__': unittest.main()

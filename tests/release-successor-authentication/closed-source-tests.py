#!/usr/bin/env python3
"""Closed source qualification; all inspection is excluded test population."""
import ast
import copy
import hashlib
import io
import json
import pathlib
import sys
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT / 'scripts'))
def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise AssertionError('closed source controls forbid effects')
sys.addaudithook(audit)
import release_successor_closed_callback as closed

class ClosedSourceControls(unittest.TestCase):
    def setUp(self):
        self.vectors = json.loads((HERE / 'slot-callback-pair-vectors.json').read_bytes())
        self.raw = b''.join(bytes.fromhex(row['request']) for row in self.vectors['exchanges'])
    def core(self, raw=None, original=None, **cuts):
        original = original or closed._Original(closed._TOKEN)
        return closed._Core(closed._TOKEN, original, self.vectors['binding'], self.raw if raw is None else raw, **cuts)
    def test_public_profile_constructor_refuses_all_effects_and_injection(self):
        raw = (HERE / 'closed-source-profile.json').read_bytes().rstrip(b'\n')
        self.assertEqual(closed.validate_profile(raw), hashlib.sha256(raw).hexdigest())
        with patch.object(closed, '_Original', side_effect=AssertionError('no construction')):
            with self.assertRaisesRegex(closed.Refused, 'unavailable'): closed.prepare(raw)
        for extra in ('writer', 'backend', 'input_stream', 'output_path', 'runtime'):
            with self.subTest(extra=extra), self.assertRaises(TypeError): closed.prepare(raw, **{extra: object()})
        variants = [raw+b' ', b'{}', b'x'*65537, bytearray(raw), raw[:-1]+b',"admission":false}']
        value = json.loads(raw)
        for field in value:
            changed = copy.deepcopy(value); changed[field] = None
            variants.append(closed._canonical(changed))
        for changed in variants:
            with self.subTest(changed=str(changed)[:60]), self.assertRaises(closed.Refused): closed.prepare(changed)
    def test_real_closed_journey_preserves_thirteen_replies_and_six_exact_slots(self):
        for fragment in (1, 4096):
            subject = self.core(self.raw+b'unconsumed-fourteenth', fragment=fragment)
            self.assertIs(subject.run(), closed.Status.COMPLETE)
            self.assertEqual(subject._input.read(), b'unconsumed-fourteenth')
            self.assertEqual(subject._replies.getvalue(), b''.join(bytes.fromhex(row['response']) for row in self.vectors['exchanges']))
            for row in self.vectors['slotEvidence']['slots']:
                self.assertEqual(bytes(subject._original.backend.stores['phase-'+str(row['ordinal'])]), bytes.fromhex(row['bytes']))
            self.assertIs(subject.run(), closed.Status.REFUSED)
            with self.assertRaises(closed.Refused): self.core(original=subject._original)
    def test_failure_cuts_are_sanitized_and_original_never_restarts(self):
        for cut in ('write','short','flush','confirmation','backend'):
            subject = self.core(cut=cut)
            self.assertIs(subject.run(), closed.Status.REFUSED)
            first = subject._owner.first_cause
            subject._owner.close('deadline')
            self.assertEqual(subject._owner.first_cause, first)
            for kind in ('loss', 'expiry'):
                try: subject._sink.observe(kind)
                except closed._slots.Refused:
                    self.assertEqual(cut, 'backend')
                with self.assertRaises(closed._slots.Refused): subject._sink.observe(kind)
            self.assertEqual(subject._sink.observations, {'loss','expiry'})
            self.assertIs(subject._cleanup, closed.Status.UNKNOWN)
            with self.assertRaises(closed.Refused): self.core(original=subject._original)
    def test_bounded_prefix_truncation_aggregate_and_constructor(self):
        for raw in (b'', b'\0\0', (4093).to_bytes(4,'big'), b'\0'*4, self.raw[:-1]):
            subject = self.core(raw)
            self.assertIs(subject.run(), closed.Status.REFUSED)
        self.core(b'x'*40960)
        with self.assertRaises(closed.Refused): self.core(b'x'*40961)
        with self.assertRaises(closed.Refused): closed._Original(object())
        with self.assertRaises(closed.Refused): closed._Core(object(), closed._Original(closed._TOKEN), {}, b'')
    def test_diagnostic_opportunities_are_consumed_without_raw_egress(self):
        subject = self.core(); self.assertIs(subject.run(), closed.Status.COMPLETE)
        for role in closed._ROLES:
            for purpose in closed._PURPOSES:
                expected = closed.Status.REFUSED if purpose == 'retention-expiry-status' else closed.Status.OBSERVED
                self.assertIs(subject.diagnostic(role, purpose), expected)
                self.assertIs(subject.diagnostic(role, purpose), closed.Status.REFUSED)
        self.assertIs(subject.diagnostic('foreign','initial-status'), closed.Status.REFUSED)
        self.assertEqual(subject._original.extractions, 4)
        self.assertEqual(len(subject._original.recipient_slots), 4)
        lost = self.core(); lost._original.delivery_cut = 'loss'
        self.assertIs(lost.diagnostic('programme-acceptance','initial-status'), closed.Status.UNKNOWN)
        self.assertIs(lost.diagnostic('programme-acceptance','initial-status'), closed.Status.REFUSED)
        self.assertEqual(lost._original.extractions, 1)
        for method in ('snapshot','export','read','raw','reset','backend','sink','decoder','writer'):
            self.assertFalse(hasattr(subject, method))
        with self.assertRaises(TypeError): subject.diagnostic('programme-acceptance','initial-status', writer=io.BytesIO())
        self.assertEqual(set(subject._original.opportunities), {(r,p) for r in closed._ROLES for p in closed._PURPOSES})
    def test_closed_recipient_has_no_alternate_output_and_refuses_expired_or_oversize(self):
        subject = self.core()
        output, errors = io.StringIO(), io.StringIO()
        with patch.object(sys, 'stdout', output), patch.object(sys, 'stderr', errors), \
             patch.object(pathlib.Path, 'open', side_effect=AssertionError('filesystem egress')):
            self.assertIs(subject.run(), closed.Status.COMPLETE)
            self.assertIs(subject.diagnostic('programme-acceptance','initial-status'), closed.Status.OBSERVED)
        self.assertEqual(output.getvalue()+errors.getvalue(), '')
        self.assertEqual(len(subject._original.diagnostic_evidence), 546)
        oversized = self.core(); oversized._original.diagnostic_evidence += b'x'
        self.assertIs(oversized.diagnostic('programme-acceptance','initial-status'), closed.Status.UNKNOWN)
        self.assertEqual(oversized._original.recipient_slots, {})
        self.assertIs(oversized.diagnostic('programme-acceptance','initial-status'), closed.Status.REFUSED)
        expired = self.core(); expired._original.expired = True
        self.assertIs(expired.diagnostic('programme-acceptance','initial-status'), closed.Status.REFUSED)
        self.assertEqual(expired._original.extractions, 0)
        self.assertFalse(hasattr(subject, '__dict__'))
    def test_source_import_closure_is_closed(self):
        expected = {ROOT / path for path in closed._SOURCE_PATHS if path.endswith('.py')}
        found = set(); pending = [ROOT / 'scripts/release_successor_closed_callback.py']
        while pending:
            path = pending.pop()
            if path in found: continue
            found.add(path)
            for node in ast.walk(ast.parse(path.read_bytes())):
                names = [a.name for a in node.names] if isinstance(node, ast.Import) else [node.module] if isinstance(node, ast.ImportFrom) else []
                for name in names:
                    if not name: continue
                    for directory in (ROOT / 'scripts', HERE):
                        candidate = directory / (name.replace('.', '/') + '.py')
                        if candidate.exists(): pending.append(candidate)
        self.assertEqual(found, expected)
    def test_source_pins_detect_import_substitution(self):
        original = pathlib.Path.read_bytes
        manifest = json.loads((HERE / 'closed-source-manifest.json').read_bytes())
        for row in manifest['files']:
            def altered(path):
                data = original(path)
                return data+b'\n' if path == ROOT / row['path'] else data
            with self.subTest(path=row['path']), patch.object(pathlib.Path,'read_bytes',altered), self.assertRaises(closed.Refused):
                closed.validate_profile(original(HERE/'closed-source-profile.json').rstrip(b'\n'))

if __name__ == '__main__': unittest.main()

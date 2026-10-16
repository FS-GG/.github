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

class _NonseekableInput:
    """Fixed inert test endpoint: positive bounded reads, no file-like extras."""
    __slots__ = ('data', 'offset', 'reads', 'fragment', 'fail_at')
    def __init__(self, data, *, fragment=4096, fail_at=None):
        self.data, self.fragment, self.fail_at = data, fragment, fail_at
        self.offset, self.reads = 0, []
    def read(self, count):
        assert type(count) is int and 1 <= count <= 4096
        self.reads.append(count)
        if len(self.reads) == self.fail_at: raise OSError('controlled input loss')
        piece = self.data[self.offset:self.offset + min(count, self.fragment)]
        self.offset += len(piece)
        return piece

class _NonseekableReplies:
    """Fixed inert test endpoint with real short/write/flush failure behavior."""
    __slots__ = ('chunks', 'writes', 'flushes', 'cut', 'fail_at')
    def __init__(self, *, cut=None, fail_at=1):
        self.chunks, self.writes, self.flushes = [], 0, 0
        self.cut, self.fail_at = cut, fail_at
    def write(self, raw):
        assert type(raw) is bytes and 1 <= len(raw) <= 512
        self.writes += 1
        if self.cut == 'write' and self.writes == self.fail_at:
            raise OSError('controlled output loss')
        piece = raw[:-1] if self.cut == 'short' and self.writes == self.fail_at else raw
        self.chunks.append(piece)
        return len(piece)
    def flush(self):
        self.flushes += 1
        if self.cut == 'flush' and self.flushes == self.fail_at:
            raise OSError('controlled flush loss')

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
        for extra in ('writer', 'backend', 'input_stream', 'output_path', 'runtime', 'owned_input', 'owned_replies'):
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
    def test_internal_owned_construction_uses_maintained_sink_owner_and_decoder(self):
        # Controlled SOURCE composition only; this is not a private provider test.
        original = closed._Original(closed._TOKEN)
        original.started = True  # Fixed composer owns consumption before reserve.
        binding = self.vectors['binding']
        identity = {'path': 'closed-owned-slot', 'binding': closed._wire.validate_auth_binding(binding),
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        prepared = closed._slots._validate_preparation(binding, identity, dict(closed._slots.REPORT),
            binding['clock'], closed._slots.population())
        owned_sink = closed._slots.InertPhaseSlotSink._initialize_owned(prepared, original.backend)
        subject = object.__new__(closed._Core)
        with patch.object(closed, '_Original', side_effect=AssertionError('no second original')):
            subject._initialize_owned(closed._TOKEN, original, binding, self.raw, owned_sink)
            self.assertIs(subject._sink, owned_sink)
            self.assertIs(type(subject._owner), closed._consumer.InertCallbackOwner)
            self.assertIs(type(subject._decoder), closed._consumer.InertCallbackRequestDecoder)
            self.assertIs(subject.run(), closed.Status.COMPLETE)
            self.assertEqual(subject._replies.getvalue(), b''.join(bytes.fromhex(row['response']) for row in self.vectors['exchanges']))
            self.assertIs(subject.diagnostic('programme-acceptance', 'initial-status'), closed.Status.OBSERVED)
        with self.assertRaises(closed.Refused):
            subject._initialize_owned(closed._TOKEN, original, binding, self.raw, owned_sink)
        with self.assertRaises(closed.Refused):
            subject._initialize_owned(object(), original, binding, self.raw, owned_sink)
        with self.assertRaises(closed.Refused):
            subject._initialize_owned(closed._TOKEN, original, binding, self.raw, object())

    def endpoint_core(self, incoming, replies, **options):
        # Fixed controlled SOURCE construction. No private provider or process.
        original = closed._Original(closed._TOKEN)
        original.started = True
        binding = self.vectors['binding']
        identity = {'path': 'closed-owned-slot', 'binding': closed._wire.validate_auth_binding(binding),
                    'invocation': binding['invocation'], 'storeGeneration': 1}
        prepared = closed._slots._validate_preparation(binding, identity, dict(closed._slots.REPORT),
            binding['clock'], closed._slots.population())
        sink = closed._slots.InertPhaseSlotSink._initialize_owned(prepared, original.backend)
        subject = object.__new__(closed._Core)
        subject._initialize_endpoints_owned(closed._TOKEN, original, binding, sink,
            incoming, replies, **options)
        return subject

    def test_owned_nonseekable_endpoints_complete_without_eof_or_fourteenth_read(self):
        expected = b''.join(bytes.fromhex(row['response']) for row in self.vectors['exchanges'])
        for fragment in (1, 4096):
            for input_fragment in (1, 4096):
                with self.subTest(fragment=fragment, input_fragment=input_fragment):
                    incoming = _NonseekableInput(self.raw + b'unread-fourteenth', fragment=input_fragment)
                    replies = _NonseekableReplies()
                    subject = self.endpoint_core(incoming, replies, fragment=fragment)
                    self.assertEqual(incoming.reads, [])
                    self.assertEqual(replies.writes, 0)
                    for name in ('seek', 'tell', 'getvalue', 'getbuffer', 'fileno', 'close'):
                        self.assertFalse(hasattr(incoming, name))
                        self.assertFalse(hasattr(replies, name))
                    self.assertIs(subject.run(), closed.Status.COMPLETE)
                    self.assertEqual(incoming.offset, len(self.raw))
                    self.assertEqual(b''.join(replies.chunks), expected)
                    self.assertEqual((replies.writes, replies.flushes), (13, 13))
                    self.assertEqual(subject._decoder.observed_request_bytes, len(self.raw))
                    for row in self.vectors['slotEvidence']['slots']:
                        self.assertEqual(bytes(subject._original.backend.stores['phase-'+str(row['ordinal'])]), bytes.fromhex(row['bytes']))
                    self.assertIs(subject.diagnostic('programme-acceptance', 'initial-status'), closed.Status.OBSERVED)
                    read_count = len(incoming.reads)
                    self.assertIs(subject.run(), closed.Status.REFUSED)
                    self.assertEqual(len(incoming.reads), read_count)
                    self.assertIs(subject._cleanup, closed.Status.UNKNOWN)

    def test_owned_endpoint_prefix_truncation_read_loss_and_aggregate_refuse(self):
        for raw in (b'', b'\0\0', (4093).to_bytes(4, 'big') + b'not-read', b'\0'*4, self.raw[:-1]):
            with self.subTest(raw=raw[:12]):
                incoming, replies = _NonseekableInput(raw), _NonseekableReplies()
                subject = self.endpoint_core(incoming, replies)
                self.assertIs(subject.run(), closed.Status.REFUSED)
                if raw[:4] in ((4093).to_bytes(4, 'big'), b'\0'*4):
                    self.assertEqual(incoming.reads, [4])
                    self.assertEqual(replies.writes, 0)
        incoming, replies = _NonseekableInput(self.raw, fail_at=2), _NonseekableReplies()
        subject = self.endpoint_core(incoming, replies)
        self.assertIs(subject.run(), closed.Status.REFUSED)
        self.assertEqual(replies.writes, 0)
        # Controlled counter cut tests the maintained aggregate guard through run;
        # it is not a claim that these maximum vectors naturally exhaust the cap.
        incoming, replies = _NonseekableInput(self.raw), _NonseekableReplies()
        subject = self.endpoint_core(incoming, replies)
        subject._decoder._observed = closed._consumer.CALLBACK_AGGREGATE
        self.assertIs(subject.run(), closed.Status.REFUSED)
        self.assertEqual(incoming.reads, [4])
        self.assertEqual(replies.writes, 0)

    def test_owned_endpoint_output_failures_stop_without_retry_or_confirmation(self):
        for cut in ('short', 'write', 'flush'):
            for fail_at in (1, 13):
                with self.subTest(cut=cut, fail_at=fail_at):
                    incoming = _NonseekableInput(self.raw + b'unread-fourteenth')
                    replies = _NonseekableReplies(cut=cut, fail_at=fail_at)
                    subject = self.endpoint_core(incoming, replies)
                    self.assertIs(subject.run(), closed.Status.REFUSED)
                    self.assertEqual(replies.writes, fail_at)
                    self.assertEqual(replies.flushes, fail_at if cut == 'flush' else fail_at - 1)
                    self.assertIs(subject._cleanup, closed.Status.UNKNOWN)
                    if fail_at == 13:
                        self.assertEqual(subject._sink.ordinal, 6)
                        self.assertEqual(incoming.offset, len(self.raw))
                    before = (len(incoming.reads), replies.writes, replies.flushes)
                    self.assertIs(subject.run(), closed.Status.REFUSED)
                    self.assertEqual((len(incoming.reads), replies.writes, replies.flushes), before)
                    with self.assertRaises(closed.Refused): self.core(original=subject._original)
        incoming, replies = _NonseekableInput(self.raw), _NonseekableReplies()
        subject = self.endpoint_core(incoming, replies, cut='confirmation')
        self.assertIs(subject.run(), closed.Status.REFUSED)
        self.assertEqual((replies.writes, replies.flushes), (1, 1))
        self.assertIsNotNone(subject._decoder._reply)
        self.assertTrue(subject._owner._closed)

    def test_owned_endpoint_initialization_guards_and_bytesio_path_stay_closed(self):
        incoming, replies = _NonseekableInput(self.raw), _NonseekableReplies()
        subject = self.endpoint_core(incoming, replies)
        args = (closed._TOKEN, subject._original, self.vectors['binding'], subject._sink, incoming, replies)
        with self.assertRaises(closed.Refused): subject._initialize_endpoints_owned(*args)
        for token, sink in ((object(), subject._sink), (closed._TOKEN, object())):
            cold = object.__new__(closed._Core)
            with self.assertRaises(closed.Refused):
                cold._initialize_endpoints_owned(token, subject._original, self.vectors['binding'], sink, incoming, replies)
            self.assertFalse(hasattr(cold, '_attempted'))
        for options in ({'cut': 'foreign'}, {'fragment': 0}, {'fragment': 4097}, {'fragment': True}):
            cold = object.__new__(closed._Core)
            with self.assertRaises(closed.Refused): cold._initialize_endpoints_owned(*args, **options)
            self.assertFalse(hasattr(cold, '_attempted'))
        self.assertEqual(incoming.reads, [])
        self.assertEqual((replies.writes, replies.flushes), (0, 0))
        with patch.object(closed.io, 'BytesIO', wraps=io.BytesIO) as buffers:
            buffered = self.core()
            self.assertEqual(buffers.call_args_list, [unittest.mock.call(self.raw), unittest.mock.call()])
            self.assertIs(buffered.run(), closed.Status.COMPLETE)
        original = closed._Original(closed._TOKEN)
        with patch.object(closed._slots.InertPhaseSlotSink, 'prepare', side_effect=closed._slots.Refused('controlled preparation loss')):
            with self.assertRaises(closed._slots.Refused): self.core(original=original)
        self.assertTrue(original.started)
        with self.assertRaises(closed.Refused): self.core(original=original)

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

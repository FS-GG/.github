#!/usr/bin/env python3
"""Pure encoding/copy model checks. Does not qualify an authenticator or backend."""
import ast
import copy
import hashlib
import json
import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(ROOT / 'scripts'))
import domain_contract as c
import release_successor_provider as provider


def audit(event, args):
    if event.startswith('socket.') or event in {'subprocess.Popen', 'os.system', 'os.posix_spawn'}:
        raise RuntimeError('pure contract forbids network/processes')
sys.addaudithook(audit)


def inputs():
    names = ('ORIGINAL_0103_CANDIDATE', 'ORIGINAL_0103_DEPENDENCIES',
             'ORIGINAL_0103_DESCENDANT_DEPENDENCIES', 'ORIGINAL_0103_TAG_CHECKPOINT')
    source = ast.parse((ROOT / 'scripts/release-successor-publish.py').read_text())
    original = {n.targets[0].id: ast.literal_eval(n.value) for n in source.body
        if isinstance(n, ast.Assign) and isinstance(n.targets[0], ast.Name) and n.targets[0].id in names}
    binding = json.loads((HERE / 'callback-vectors.json').read_text())['binding']
    binding['mode'] = 'native'; binding['process']['pid'] = c.U64
    binding['workflow'].update(run=c.U64, attempt=(1 << 32) - 1)
    binding['app'] = {'id': c.U64, 'installation': c.U64}
    for key in ('frequency', 'start', 'normal', 'work', 'total'): binding['clock'][key] = str(c.I63)
    # This is a maximum-width legal ABI binding, not an enabled native operation.
    ready = provider.encode_auth_frame(binding, 1, 'ready', {'binding': binding})
    schemas = c.shapes(original, provider.validate_auth_binding(binding), hashlib.sha256(ready).hexdigest())
    return original, binding, ready, schemas


def vectors():
    _, binding, ready, schemas = inputs()
    frames = c.legacy_bounds(binding)
    return {'schema': 'fsgg.utel.inert-domain-maxima/3',
        'qualification': 'structural unsigned maximum vectors; signatures are canonical 64-byte placeholders, never authenticated positive evidence',
        'envelopes': {k: c.canonical(c.maximum(s)).decode() for k, s in schemas.items()},
        'legacyReadyHex': ready.hex(), 'legacyStructuralUpperBodies': {k: c.canonical(v).decode() for k, v in frames.items()},
        'accounting': c.retained_accounting(schemas, frames)}


class ContractTests(unittest.TestCase):
    def test_source_bound_is_pinned_to_reviewed_provider(self):
        manifest = json.loads((HERE / 'callable-source-manifest.json').read_text())
        pin = next(row for row in manifest['files'] if row['path'] == 'scripts/release_successor_provider.py')
        self.assertEqual(hashlib.sha256((ROOT / pin['path']).read_bytes()).hexdigest(), pin['sha256'])

    def test_committed_vectors_match_current_protected_originals(self):
        self.assertEqual(json.loads((HERE / 'domain-max-vectors.json').read_text()), vectors())
        original, _, _, schemas = inputs()
        self.assertEqual(len(original['ORIGINAL_0103_DEPENDENCIES']), 9)
        self.assertEqual(c.maximum(schemas['descriptor'])['dependencies'], original['ORIGINAL_0103_DEPENDENCIES'])
        self.assertNotEqual(c.maximum(schemas['descriptor'])['authentication']['path'],
                            '.github/workflows/release-successor-publish.yml')

    def test_closed_maximum_envelopes_and_rejections(self):
        _, _, _, schemas = inputs()
        for name, shape in schemas.items():
            value = c.maximum(shape); raw = c.canonical(value)
            self.assertEqual(c.decode(raw, shape), value)
            with self.assertRaises(ValueError): c.decode(b' ' + raw, shape)
            with self.assertRaises(ValueError): c.decode(raw[:-1] + b',"v":3}', shape)
            extra = dict(value, unknown=1)
            with self.assertRaises(ValueError): c.decode(c.canonical(extra), shape)
            for key in value:
                altered = dict(value); del altered[key]
                with self.assertRaises(ValueError): c.decode(c.canonical(altered), shape)
        ack = c.maximum(schemas['ack'])
        self.assertLessEqual(len(c.canonical(ack)) + 4, 512)
        ack['s'] = ack['s'][:-1] + 'B'
        with self.assertRaises(ValueError): c.validate(ack, schemas['ack'])
        ack = c.maximum(schemas['ack']); ack['g'] = c.U64 + 1
        with self.assertRaises(ValueError): c.validate(ack, schemas['ack'])

    def test_existing_complete_ready_and_start_abi(self):
        _, binding, ready, schemas = inputs()
        parsed = provider.decode_auth_frame(ready)
        self.assertEqual(parsed['p']['binding'], binding)
        d = c.maximum(schemas['descriptor'])
        self.assertEqual(d['binding'], parsed['b'])
        self.assertEqual(d['ready'], hashlib.sha256(ready).hexdigest())
        # Exact pinned StartFrame wrapper, including all canonical binding bytes.
        start = c.canonical({'b': parsed['b'], 'k': 'start', 'p': binding, 'v': provider.AUTH_PROTOCOL})
        self.assertLessEqual(len(start) + 4, 2048)
        self.assertLessEqual(len(ready), 2048)
        legacy_ack = c.canonical(c.maximum(schemas['child_ack']))
        self.assertEqual(set(json.loads(legacy_ack)), {'b', 'h', 'k', 'n', 'v'})
        self.assertNotEqual(set(json.loads(legacy_ack)), set(c.maximum(schemas['ack'])))
        self.assertLessEqual(len(legacy_ack) + 4, 512)

    def test_complete_structural_upper_bound_fits(self):
        _, binding, _, schemas = inputs()
        bounds = c.legacy_bounds(binding)
        self.assertEqual(set(bounds), set(provider.AUTH_PHASES))
        for phase, value in bounds.items(): self.assertLessEqual(4 + len(c.canonical(value)), 2048)
        budget = c.retained_accounting(schemas, bounds)
        self.assertLessEqual(budget['total'], 65536)
        self.assertEqual(budget['rows']['peer']['copies'], 3)
        self.assertEqual(budget['rows']['observation']['copies'], 4)
        self.assertEqual(budget['rows']['frame']['copies'], 16)
        # Generic 2048 ABI ceilings alone do not prove this enlarged population.
        self.assertGreater(c.accounting(schemas)['total'], 65536)

    def test_every_write_cut_freezes_gates_but_preserves_observation_slots(self):
        events = ('begin', 'partialwrite', 'fsync', 'rename', 'dirbarrier', 'readback', 'sign',
                  'reserve', 'take', 'full-send', 'consume')
        state = c.Model()
        for event in events:
            state = state.step(event)
            for loss in ('loss', 'restart'):
                held = state.step(loss)
                self.assertEqual(held.step('diagnostic'), held)
                for retry in ('begin', 'reserve', 'take', 'full-send', 'consume'):
                    with self.assertRaises(ValueError): held.step(retry)
                observed = held.step('record-loss').step('expire')
                self.assertTrue(observed.uncertain and observed.expired)
                for duplicate in ('record-loss', 'expire'):
                    with self.assertRaises(ValueError): observed.step(duplicate)

    def test_first_failure_is_monotonic_across_loss_expiry_and_diagnostics(self):
        original = c.Model().step('begin').step('failure:transport')
        self.assertEqual(original.first_failure, ('ready', 'transport'))
        state = original.step('failure:readback')
        self.assertEqual(state, original)
        for event in ('diagnostic', 'loss', 'restart', 'record-loss', 'expire', 'diagnostic'):
            state = state.step(event)
            self.assertEqual(state.first_failure, original.first_failure)
        with self.assertRaises(ValueError): state.step('begin')
        # A newly detected cause cannot rewrite an already frozen core.
        with self.assertRaises(ValueError): c.Model().step('loss').step('failure:readback')

    def test_late_cause_uses_append_once_observation_after_core_freeze(self):
        late = {'phase': 'revocation-possible', 'code': 'child-loss'}
        for core in (c.Model().step('loss'), c.Model().step('failure:transport')):
            original = core.first_failure
            observed = core.step('record-loss', late_cause=late)
            self.assertEqual(observed.first_failure, original)
            self.assertEqual(observed.loss_cause, ('revocation-possible', 'child-loss'))
            expired = observed.step('expire', late_cause={'phase': 'terminal', 'code': 'deadline'})
            self.assertEqual(expired.first_failure, original)
            self.assertEqual(expired.loss_cause, observed.loss_cause)
            self.assertEqual(expired.expiry_cause, ('terminal', 'deadline'))
            self.assertTrue(expired.uncertain and expired.expired)
            # The first possible observation write consumes its sole candidate,
            # including an ambiguous write, loss or restart before completion.
            for cut in ('loss', 'restart', 'diagnostic'):
                held = expired.step(cut)
                self.assertEqual(held.first_failure, original)
                self.assertEqual(held.loss_cause, observed.loss_cause)
                self.assertEqual(held.expiry_cause, expired.expiry_cause)
                for event in ('record-loss', 'expire'):
                    with self.assertRaises(ValueError): held.step(event, late_cause=late)
                with self.assertRaises(ValueError): held.step('begin')
            late['code'] = 'readback'
            self.assertEqual(observed.loss_cause, ('revocation-possible', 'child-loss'))
            late['code'] = 'child-loss'

    def test_late_cause_is_closed_and_cannot_overflow_or_rewrite_core(self):
        _, _, _, schemas = inputs()
        value = c.maximum(schemas['observation'])
        self.assertLessEqual(len(c.canonical(value)), 512)
        self.assertEqual(c.decode(c.canonical(value), schemas['observation']), value)
        without_cause = dict(value, lateCause=None)
        self.assertNotEqual(c.signing_bytes('observation', value),
                            c.signing_bytes('observation', without_cause))
        for bad in ({'phase': 'seventh', 'code': 'readback'},
                    {'phase': 'terminal', 'code': 'x' * 513},
                    {'phase': 'terminal', 'code': 'readback', 'extra': 1}):
            altered = dict(value, lateCause=bad)
            with self.assertRaises(ValueError): c.decode(c.canonical(altered), schemas['observation'])
            with self.assertRaises(ValueError): c.Model().step('loss').step('record-loss', late_cause=bad)
        cause = {'phase': 'terminal', 'code': 'deadline'}
        with self.assertRaises(ValueError): c.Model().step('begin', late_cause=cause)
        self.assertEqual(c.Model().step('loss').step('record-loss').loss_cause, None)

    def test_legacy_field_census_matches_current_validator(self):
        _, binding, _, _ = inputs()
        bounds = c.legacy_bounds(binding)
        expected = provider.empty_auth_facts()
        for phase, row in bounds.items():
            if phase == 'ready':
                self.assertEqual(set(row['p']), {'binding'})
                self.assertEqual(set(row['p']['binding']), set(binding))
                continue
            facts = row['p']['facts']
            self.assertEqual(set(facts), set(expected))
            for key in facts:
                if key != 'firstCause': self.assertEqual(set(facts[key]), set(expected[key]))
            keys = ({'facts', 'request'} if phase in ('issuance-possible', 'revocation-possible') else
                    {'facts', 'publication', 'publicationComplete', 'qualified', 'credentialReferencesDropped'}
                    if phase == 'terminal' else {'facts'})
            self.assertEqual(set(row['p']), keys)
            self.assertEqual(set(facts['firstCause']), {'stage', 'code'})
        self.assertEqual(set(bounds['terminal']['p']['publication']), set(provider.AUTH_PUBLICATION))

    def test_authenticated_join_cannot_repurpose_diagnostics(self):
        receipt = {'d': 'd', 'f': 'f', 'c': 'c', 'q': 'q', 'g': 1, 'n': 1, 'p': 'ready', 'k': 'gate', 'l': 0, 'u': 1}
        expected = dict(receipt)
        def match(value, **kw):
            return c.receipt_matches(value, expected, authenticated=kw.get('authenticated', True),
                clock_authenticated=kw.get('clock_authenticated', True), original_upper=kw.get('original_upper', 2))
        self.assertTrue(match(receipt))  # Conditional predicate, not signature qualification.
        for key in ('d', 'f', 'c', 'q', 'g', 'n', 'p', 'k'):
            altered = dict(receipt); altered[key] = 'substitution'
            self.assertFalse(match(altered))
        for flag in ('authenticated', 'clock_authenticated'): self.assertFalse(match(receipt, **{flag: False}))
        self.assertFalse(match(receipt, original_upper=270))
        self.assertFalse(match(c.diagnostic_read(c.canonical(receipt))))
        _, _, _, schemas = inputs()
        read = c.maximum(schemas['read'])
        self.assertEqual(read['k'], 'diagnostic')
        self.assertEqual(c.decode(c.canonical(read), schemas['read']), read)
        for kind in ('descriptor', 'ack', 'checkpoint', 'observation', 'custodian'):
            _, _, _, schemas = inputs(); maximum = c.maximum(schemas[kind])
            self.assertNotEqual(c.signing_bytes(kind, maximum)[:10], c.canonical(maximum)[:10])

    def test_six_ordinals_one_use_and_clock(self):
        state = c.Model()
        for ordinal in range(1, 7):
            state = state.step('begin')
            with self.assertRaises(ValueError): state.step('begin')
            for event in ('partialwrite', 'fsync', 'rename', 'dirbarrier', 'readback', 'sign'):
                state = state.step(event)
            if ordinal in c.GATES:
                for event in ('reserve', 'take', 'full-send', 'consume'):
                    state = state.step(event)
            else: state = state.step('commit')
        self.assertEqual(state.ordinal, 6)
        self.assertEqual(state.possible, state.taken)
        self.assertEqual(state.sent, state.consumed)
        with self.assertRaises(ValueError): state.step('begin')
        for upper in (-1, 270, 285, 300, c.I63, c.U64 + 1):
            with self.assertRaises(ValueError): c.Model().step('begin', upper=upper)
        with self.assertRaises(ValueError): c.Model().step('begin', clock_ok=False)
        self.assertEqual(state.step('diagnostic', upper=300), state)


if __name__ == '__main__':
    if sys.argv[1:] == ['--write-vectors']:
        (HERE / 'domain-max-vectors.json').write_text(json.dumps(vectors(), indent=2) + '\n')
    else: unittest.main()

#!/usr/bin/env python3
"""Inert trusted-port exercises, never authenticated or deployed service evidence."""
import ast
import base64
import copy
import hashlib
import json
import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
import release_successor_domain_authority as c
import release_successor_provider as provider


def fixture():
    names = ('ORIGINAL_0103_CANDIDATE', 'ORIGINAL_0103_DEPENDENCIES',
             'ORIGINAL_0103_DESCENDANT_DEPENDENCIES', 'ORIGINAL_0103_TAG_CHECKPOINT')
    tree = ast.parse((ROOT / 'scripts/release-successor-publish.py').read_text())
    original = {n.targets[0].id: ast.literal_eval(n.value) for n in tree.body
        if isinstance(n, ast.Assign) and isinstance(n.targets[0], ast.Name) and n.targets[0].id in names}
    binding = json.loads((HERE / 'callback-vectors.json').read_bytes())['binding']
    binding['mode'] = 'native'
    binding['workflow'].update(run=11, attempt=1)
    authentication = dict(binding['workflow'], path='.github/workflows/release-successor-authenticate.yml', run=12)
    ready = provider.encode_auth_frame(binding, 1, 'ready', {'binding': binding})
    enrollment = dict(v=3, storeGeneration=1, **{key: str(index) * 64 for index, key in enumerate(
        ('policy', 'issuer', 'ack', 'reporter', 'custodian', 'supervisor'))})
    # Entirely invented source-test population qualification. No native fit claim.
    policy = dict(v=1, original=original, binding=binding, ready=ready.hex(),
        authentication=authentication, enrollment=enrollment,
        population={'v': 1, 'scope': 'complete-physical-population', 'evidence': 'e' * 64, 'bytes': 65536, 'limit': 65536})
    request = {key: policy[key] for key in ('binding', 'ready', 'authentication')}
    return policy, request


class InertAdmission(c.IssuerAdmission):
    def __init__(self, events):
        self.events = events
        self.deny = False
        self.upper = 1
        self.fresh_upper = 2
        self.mutate = lambda: None
        self.alter = lambda value: None
        self.claim = None

    def authenticate(self, policy, request, evidence):
        self.events.append('authenticate')
        self.mutate()
        if self.deny or evidence != b'inert-evidence': raise c.Refused('inert deny')
        value = json.loads(policy)
        result = {'v': 1, 'policy': hashlib.sha256(policy).hexdigest(),
                  'request': hashlib.sha256(request).hexdigest(),
                  'domain': hashlib.sha256(c.canonical(value['binding']['clock'])).hexdigest(),
                  'lower': 0, 'upper': self.upper}
        result.update({k: 'a' * 64 for k in ('identityEvidence', 'permissionEvidence', 'enrollmentEvidence', 'clockEvidence')})
        result['populationEvidence'] = value['population']['evidence']
        self.alter(result)
        self.claim = c.canonical(result)
        return self.claim

    def recheck(self, policy, request, admission):
        self.events.append('clock')
        result = json.loads(admission)
        result['upper'] = self.fresh_upper
        return c.canonical(result)


class InertStore(c.IssuanceStore):
    def __init__(self, events):
        self.events = events
        self.records = {}
        self.fail = None
        self.hook = lambda: None

    def reserve_once(self, original_key, commitment):
        self.events.append('reserve')
        if original_key in self.records: raise c.Refused('previous possible attempt')
        self.records[original_key] = {'state': 'Unknown', 'commitment': commitment, 'descriptor': None}
        self.hook()
        if self.fail == 'reserve': raise OSError('lost reservation result')
        if self.fail == 'readback': return b'wrong readback'
        return commitment

    def complete(self, original_key, commitment, descriptor):
        self.events.append('complete')
        if self.fail == 'complete': raise OSError('lost completion result')
        assert self.records[original_key]['commitment'] == commitment
        self.records[original_key].update(state='Issued', descriptor=descriptor)
        return b'wrong completion' if self.fail == 'complete-readback' else descriptor

    def observe(self, original_key):
        self.events.append('observe')
        return self.records.get(original_key)


class InertSigner(c.DescriptorSigner):
    def __init__(self, events):
        self.events = events
        self.calls = []
        self.fail = False
        self.result = base64.urlsafe_b64encode(b'x' * 64).rstrip(b'=').decode()

    def issue_descriptor(self, signing_bytes, commitment, admission):
        self.events.append('sign')
        self.calls.append((signing_bytes, commitment, admission))
        if self.fail: raise OSError('signer may have signed; secret diagnostic')
        return self.result


class AuthorityTests(unittest.TestCase):
    def prepare(self, policy=None):
        self.policy, self.request = fixture()
        if policy is not None: self.policy = policy
        self.events = []
        self.admission = InertAdmission(self.events)
        self.store = InertStore(self.events)
        self.signer = InertSigner(self.events)
        return self.construct()

    def construct(self):
        return c.DescriptorAuthority(c.canonical(self.policy), admission=self.admission, store=self.store, signer=self.signer)

    def issue(self, authority, request=None):
        return authority.issue(c.canonical(self.request if request is None else request), b'inert-evidence')

    def test_promoted_codec_matches_frozen_oracle_ast_and_every_fixed_vector(self):
        import domain_contract as oracle
        names = ('uint', 'hexdigest', 'choice', 'signature', 'optional', 'cause_shape',
                 'maximum', 'canonical', 'validate', 'decode', 'shapes', 'signing_bytes')
        def definitions(path):
            return {node.name: ast.dump(node, include_attributes=False)
                    for node in ast.parse(path.read_bytes()).body
                    if isinstance(node, (ast.FunctionDef, ast.ClassDef))}
        frozen = definitions(HERE / 'domain_contract.py')
        promoted = definitions(ROOT / 'scripts/release_successor_domain_authority.py')
        for name in names + ('Field',):
            with self.subTest(definition=name): self.assertEqual(promoted[name], frozen[name])
        for name in ('PHASES', 'GATES', 'U64', 'I63'):
            self.assertEqual(getattr(c, name), getattr(oracle, name))
        vectors = json.loads((HERE / 'domain-max-vectors.json').read_bytes())
        original = fixture()[0]['original']
        ready = bytes.fromhex(vectors['legacyReadyHex'])
        binding = provider.decode_auth_frame(ready)['p']['binding']
        schemas = c.shapes(original, provider.validate_auth_binding(binding), hashlib.sha256(ready).hexdigest())
        for kind, raw in vectors['envelopes'].items():
            with self.subTest(vector=kind):
                self.assertEqual(c.canonical(c.maximum(schemas[kind])).decode(), raw)
                self.assertEqual(c.decode(raw.encode(), schemas[kind]), json.loads(raw))
                if kind in ('descriptor', 'ack', 'checkpoint', 'observation', 'custodian'):
                    value = json.loads(raw)
                    self.assertEqual(c.signing_bytes(kind, value), oracle.signing_bytes(kind, value))

    def test_default_missing_policy_and_unqualified_ports_refuse(self):
        with self.assertRaises(c.Refused): c.DescriptorAuthority()
        self.prepare()
        for policy in (None, {}, b'{}', b'false'):
            with self.subTest(policy=policy), self.assertRaises(c.Refused):
                c.DescriptorAuthority(policy, admission=self.admission, store=self.store, signer=self.signer)
        with self.assertRaises(c.Refused):
            c.DescriptorAuthority(c.canonical(self.policy), admission=True, store=True, signer=True)
        self.assertEqual(self.signer.calls, [])

    def test_success_binds_every_original_byte_and_reserves_before_sign(self):
        authority = self.prepare()
        raw = self.issue(authority)
        descriptor = json.loads(raw)
        self.assertEqual(self.events, ['authenticate', 'reserve', 'clock', 'sign', 'complete'])
        self.assertEqual(descriptor['dependencies'], self.policy['original']['ORIGINAL_0103_DEPENDENCIES'])
        self.assertEqual(len(descriptor['dependencies']), 9)
        self.assertEqual(descriptor['binding'], provider.validate_auth_binding(self.policy['binding']))
        self.assertEqual(descriptor['ready'], hashlib.sha256(bytes.fromhex(self.policy['ready'])).hexdigest())
        self.assertEqual(descriptor['originalCheckpoint'], self.policy['original']['ORIGINAL_0103_TAG_CHECKPOINT'])
        self.assertEqual(descriptor['authentication'], self.policy['authentication'])
        self.assertEqual(descriptor['enrollment'], hashlib.sha256(c.canonical(self.policy['enrollment'])).hexdigest())
        self.assertEqual(self.signer.calls[0][0], c.signing_bytes('descriptor', descriptor))
        self.assertEqual(authority.diagnostic(), {'state': 'Issued', 'firstFailure': None})
        with self.assertRaises(c.Refused): self.issue(authority)
        with self.assertRaises(c.Refused): self.issue(self.construct())
        self.assertEqual(len(self.signer.calls), 1)

    def test_request_identity_ready_source_clock_and_authentication_substitution(self):
        for field in ('binding', 'ready', 'authentication'):
            authority = self.prepare()
            changed = copy.deepcopy(self.request)
            if field == 'ready': changed[field] = '00'
            elif field == 'binding': changed[field]['source']['runnerClosure'] = '0' * 64
            else: changed[field]['run'] += 1
            with self.subTest(field=field), self.assertRaises(c.Refused): self.issue(authority, changed)
            self.assertEqual(self.signer.calls, [])
            self.assertEqual(self.store.records, {})
        for key in ('workflow', 'clock', 'process', 'app', 'invocation'):
            authority = self.prepare(); changed = copy.deepcopy(self.request)
            changed['binding'][key] = None
            with self.subTest(key=key), self.assertRaises(c.Refused): self.issue(authority, changed)
            self.assertEqual(self.events, [])

    def test_policy_completeness_clock_permissions_and_population_fail_closed(self):
        self.prepare()
        mutations = []
        for key in ('enrollment', 'population', 'original'):
            row = copy.deepcopy(self.policy); del row[key]; mutations.append(row)
        for name, value in (('bytes', 65537), ('limit', 131072), ('scope', 'partial')):
            row = copy.deepcopy(self.policy); row['population'][name] = value; mutations.append(row)
        row = copy.deepcopy(self.policy); row['original']['ORIGINAL_0103_DEPENDENCIES'].pop(next(iter(row['original']['ORIGINAL_0103_DEPENDENCIES']))); mutations.append(row)
        for clock in ('normal', 'work', 'total'):
            row = copy.deepcopy(self.policy); row['binding']['clock'][clock] = str(int(row['binding']['clock'][clock]) + 1); mutations.append(row)
        row = copy.deepcopy(self.policy); row['enrollment']['issuer'] = row['enrollment']['ack']; mutations.append(row)
        for row in mutations:
            with self.subTest(policy=row), self.assertRaises(ValueError):
                c.DescriptorAuthority(c.canonical(row), admission=self.admission, store=self.store, signer=self.signer)
        self.assertEqual(self.signer.calls, [])
        for key in ('policy', 'request', 'domain', 'populationEvidence'):
            authority = self.prepare()
            self.admission.alter = lambda result, key=key: result.update({key: '0' * 64})
            with self.subTest(key=key), self.assertRaises(c.Refused): self.issue(authority)
            self.assertEqual(self.store.records, {})
        for missing in ('permissionEvidence', 'enrollmentEvidence', 'identityEvidence', 'clockEvidence'):
            authority = self.prepare(); self.admission.alter = lambda result, key=missing: result.pop(key)
            with self.subTest(missing=missing), self.assertRaises(ValueError): self.issue(authority)
            self.assertEqual(self.signer.calls, [])
        authority = self.prepare(); self.admission.deny = True
        with self.assertRaises(c.Refused): self.issue(authority)
        self.assertEqual(self.events, ['authenticate'])

    def test_original_clock_unknown_rollback_expiry_and_reservation_delay(self):
        for upper in (-1, 270000, 285000, 300000, c.I63 + 1, True):
            authority = self.prepare(); self.admission.upper = upper
            with self.subTest(upper=upper), self.assertRaises(ValueError): self.issue(authority)
            self.assertEqual(self.signer.calls, [])
            self.assertEqual(self.store.records, {})
        for upper in (0, 270000, 300000):
            authority = self.prepare(); self.admission.fresh_upper = upper
            with self.subTest(fresh=upper), self.assertRaises(c.Refused): self.issue(authority)
            self.assertEqual(self.signer.calls, [])
            self.assertEqual(authority.diagnostic(), {'state': 'Unknown', 'firstFailure': 'clock'})
            with self.assertRaises(c.Refused): self.issue(self.construct())
            self.assertEqual(self.signer.calls, [])

    def test_all_possible_attempt_failures_remain_unknown_without_resigning(self):
        for cut, expected_calls in (('reserve', 0), ('readback', 0), ('sign', 1), ('complete', 1), ('complete-readback', 1), ('bad-signature', 1)):
            authority = self.prepare()
            if cut == 'sign': self.signer.fail = True
            elif cut == 'bad-signature': self.signer.result = 'invalid'
            else: self.store.fail = cut
            with self.subTest(cut=cut), self.assertRaises(c.Refused): self.issue(authority)
            first = authority.diagnostic()
            self.assertEqual(first['state'], 'Unknown')
            for i in range(2):
                with self.assertRaises(c.Refused): self.issue(authority)
                self.assertEqual(authority.diagnostic(), first)
            self.store.fail = None; self.signer.fail = False
            with self.assertRaises(c.Refused): self.issue(self.construct())
            self.assertEqual(len(self.signer.calls), expected_calls)
            self.assertNotIn('secret', str(first))

    def test_caller_mutation_cannot_change_validated_bytes(self):
        authority = self.prepare(); request = c.canonical(self.request)
        old = copy.deepcopy(self.policy)
        def mutate():
            self.policy['original']['ORIGINAL_0103_DEPENDENCIES'].clear()
            self.policy['authentication']['run'] = 999
            self.request['binding']['clock']['normal'] = '999999'
            self.policy['enrollment']['issuer'] = 'f' * 64
        self.admission.mutate = mutate
        result = json.loads(authority.issue(request, b'inert-evidence'))
        self.assertEqual(result['dependencies'], old['original']['ORIGINAL_0103_DEPENDENCIES'])
        self.assertEqual(result['authentication'], old['authentication'])
        self.assertEqual(result['enrollment'], hashlib.sha256(c.canonical(old['enrollment'])).hexdigest())

    def test_reentrant_reservation_and_diagnostic_never_issue(self):
        authority = self.prepare()
        def hook():
            self.assertEqual(authority.diagnostic(), {'state': 'Unknown', 'firstFailure': None})
            with self.assertRaises(c.Refused): self.issue(authority)
        self.store.hook = hook
        self.issue(authority)
        before = list(self.events)
        for i in range(3): self.assertEqual(authority.diagnostic()['state'], 'Issued')
        self.assertEqual(self.events, before)
        self.assertEqual(len(self.signer.calls), 1)

    def test_new_execution_policy_or_enrollment_does_not_create_new_original_attempt(self):
        authority = self.prepare(); self.signer.fail = True
        with self.assertRaises(c.Refused): self.issue(authority)
        self.signer.fail = False
        self.policy['authentication']['run'] += 1
        self.policy['enrollment']['policy'] = 'f' * 64
        self.policy['enrollment']['storeGeneration'] += 1
        self.request = {key: self.policy[key] for key in ('binding', 'ready', 'authentication')}
        with self.assertRaises(c.Refused): self.issue(self.construct())
        self.assertEqual(len(self.signer.calls), 1)

    def test_noncanonical_duplicate_and_mutable_inputs_refuse_without_hooks(self):
        authority = self.prepare()
        raw = c.canonical(self.request)
        for changed in (bytearray(raw), b' ' + raw, raw[:-1] + b',"ready":""}'):
            with self.subTest(kind=type(changed)), self.assertRaises(c.Refused): authority.issue(changed, b'inert-evidence')
        self.assertEqual(self.events, [])


if __name__ == '__main__': unittest.main()

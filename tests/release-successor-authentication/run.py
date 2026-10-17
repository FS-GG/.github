#!/usr/bin/env python3
"""Inert authentication factory controls. No credentials, sockets or subprocesses."""
import copy
import datetime as dt
import importlib.util
import io
import json
import pathlib
import socket
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest
from unittest.mock import patch


def forbidden(*args, **kwargs):
    raise AssertionError('authentication controls forbid publication/network/subprocess effects')


def audit(event, args):
    if event in {'subprocess.Popen', 'os.system', 'os.posix_spawn', 'socket.__new__',
                 'socket.connect', 'socket.getaddrinfo', 'socket.bind'}:
        forbidden()


sys.addaudithook(audit)
ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import release_successor_provider as provider
import release_successor_auth_consumer as consumer
import release_successor_journal as journal
spec = importlib.util.spec_from_file_location('authentication_publisher', ROOT / 'scripts/release-successor-publish.py')
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class Response:
    def __init__(self, request, body, status, headers=None):
        self.url, self.status = request.full_url, status
        self.headers = headers or {}
        self.body = io.BytesIO(json.dumps(body).encode() if body is not None else b'')
        self.fp = types.SimpleNamespace(raw=types.SimpleNamespace(_sock=types.SimpleNamespace(settimeout=lambda value: None)))
    def geturl(self): return self.url
    def read1(self, size): return self.body.read(size)
    def __enter__(self): return self
    def __exit__(self, *args): return False


class Opener:
    def __init__(self):
        self.calls = []
        self.rows = [
            {'id': 4882140},
            {'id': 123, 'app_id': 4882140, 'suspended_at': None, 'account': {'login': 'FS-GG'},
             'repository_selection': 'selected', 'permissions': {'contents': 'write', 'workflows': 'write'}},
            {'id': 123, 'app_id': 4882140, 'suspended_at': None},
            {'token': 'inert-owned-token', 'permissions': {'contents': 'write', 'workflows': 'write', 'metadata': 'read'},
             'expires_at': dt.datetime.fromtimestamp(time.time() + 1800, dt.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')},
            {'total_count': 1, 'repositories': [{'id': 1269292704, 'full_name': 'FS-GG/.github'}]}, None]
        self.mutate = None
        self.fail_cleanup = False
    def open(self, request, timeout):
        index = len(self.calls)
        self.calls.append(request)
        assert 0 < timeout <= 15
        if request.method == 'DELETE':
            self.assert_cleanup(request)
            if self.fail_cleanup: raise RuntimeError('secret diagnostic inert-owned-token inert-jwt')
            return Response(request, None, 204)
        if index > 4: forbidden()
        paths = ['/app', '/app/installations/123', '/repos/FS-GG/.github/installation',
                 '/app/installations/123/access_tokens', '/installation/repositories?per_page=100&page=1']
        assert request.full_url == 'https://api.github.com' + paths[index]
        assert request.get_header('Authorization') == ('Bearer inert-owned-token' if index == 4 else 'Bearer inert-jwt')
        assert request.method == ('POST' if index == 3 else 'GET')
        value = copy.deepcopy(self.rows[index])
        response = Response(request, value, 201 if request.method == 'POST' else 200)
        if self.mutate: self.mutate(index, response)
        return response
    @staticmethod
    def assert_cleanup(request):
        assert request.full_url == 'https://api.github.com/installation/token'
        assert request.get_header('Authorization') == 'Bearer inert-owned-token'


def binding():
    return {'mode': 'inert', 'profile': provider.AUTH_PROFILE, 'operation': 'utel-rel-21', 'originalGeneration': 2,
        'invocation': '00000000-0000-0000-0000-000000000001', 'process': {'pid': 1, 'birth': 'a' * 64},
        'source': {'childRevision': 'a' * 40, 'childClosure': 'b' * 64, 'runnerRevision': 'c' * 40,
                   'runnerClosure': 'd' * 64, 'contract': 'e' * 64},
        'workflow': {'repositoryId': 1269292704, 'path': '.github/workflows/release-successor-publish.yml',
                     'revision': 'f' * 40, 'run': 0, 'attempt': 0},
        'clock': {'domain': '00000000-0000-0000-0000-000000000002', 'frequency': '1000',
                  'start': '0', 'normal': '270000', 'work': '285000', 'total': '300000'},
        'app': {'id': 4882140, 'installation': 123}}


class RetainedChannel:
    def __init__(self, root, bound=None):
        self.binding = bound or binding()
        self.path = pathlib.Path(root) / 'retained-result.json'
        self.report = {'schema': 'fsgg.release-successor-result/1'}
        self.frames, self.acks = [], []
        self.deny = None
    def __call__(self, frame):
        row = provider.decode_auth_frame(frame)
        self.frames.append(frame)
        if row['k'] == self.deny: return None
        readback = publisher.persist_authentication_snapshot(self.path, self.report, self.binding, frame)
        assert readback == {'binding': row['b'], 'ordinal': row['n'], 'frameDigest': __import__('hashlib').sha256(frame).hexdigest()}
        if row['k'] not in {'ready', 'issuance-possible', 'revocation-possible'}: return None
        ack = {'v': provider.AUTH_PROTOCOL, 'k': 'ack', 'b': row['b'], 'n': row['n'], 'h': readback['frameDigest']}
        raw = provider._auth_canonical(ack)
        encoded = len(raw).to_bytes(4, 'big') + raw
        self.acks.append(encoded)
        return encoded


def adapter(channel, ticks=lambda: 1):
    return provider.AuthenticationPhaseAdapter(channel.binding, channel, ticks)


class FactoryControls(unittest.TestCase):
    def run_factory(self, opener):
        report = {'schema': 'fsgg.release-successor-result/1'}
        with patch.object(provider.LiveProvider, 'dispatch', forbidden), \
             patch.object(provider.VerifiedOriginalTagTransport, 'dispatch', forbidden), \
             patch.object(provider.GitHubAPI, '_request', forbidden), \
             patch.object(journal.ProtectedReleaseJournal, '__init__', forbidden), \
             patch.object(journal.ProtectedReleaseJournal, 'compare_and_swap', forbidden), \
             patch.object(subprocess, 'run', forbidden):
            with tempfile.TemporaryDirectory() as root:
                channel = RetainedChannel(root)
                channel.report = report
                session = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123, phase_adapter=adapter(channel), opener=opener)
                transport = session.qualify(report)
                self.last_frames = list(channel.frames)
        encoded = json.dumps(report)
        self.assertNotIn('inert-owned-token', encoded)
        self.assertNotIn('inert-jwt', encoded)
        self.assertEqual(set(report['publicationEffects'].values()), {0})
        self.assertFalse(report['publicationComplete'])
        self.assertIsNone(session._jwt)
        self.assertIsNone(session._token)
        with self.assertRaises(provider.Refused): session.qualify({})
        return transport, report

    def test_real_factory_construction_narrows_issuance_and_revokes_exact_token(self):
        opener = Opener()
        transport, report = self.run_factory(opener)
        self.assertTrue(report['qualified'])
        self.assertIs(transport, True)
        self.assertEqual(report['identity']['repositoryId'], 1269292704)
        self.assertEqual([provider.decode_auth_frame(x)['k'] for x in self.last_frames], list(provider.AUTH_PHASES))
        inert = provider.AuthenticationOnlyOriginalTagTransport(provider._ORIGINAL_TAG_TRANSPORT_SEAL, 'fixture', report['identity'])
        with self.assertRaises(provider.Refused): inert.dispatch(None)
        self.assertEqual([x.method for x in opener.calls], ['GET', 'GET', 'GET', 'POST', 'GET', 'DELETE'])
        self.assertEqual(json.loads(opener.calls[3].data),
                         {'repository_ids': [1269292704], 'permissions': {'contents': 'write', 'workflows': 'write'}})
        self.assertEqual(opener.calls[4].get_header('Authorization'), 'Bearer inert-owned-token')
        self.assertEqual(report['cleanup'], 'revoked')

    def test_wrong_app_installation_suspension_and_grants_never_issue(self):
        for index, key, value in [(0, 'id', 8), (1, 'id', 8), (1, 'app_id', 8),
                                  (1, 'suspended_at', '2026-10-14T00:00:00Z'),
                                  (1, 'permissions', {'contents': 'write'}), (2, 'id', 8), (2, 'app_id', 8)]:
            with self.subTest(index=index, key=key):
                opener = Opener(); opener.rows[index][key] = value
                transport, report = self.run_factory(opener)
                self.assertIs(transport, False)
                self.assertFalse(any(x.method == 'POST' for x in opener.calls))

    def test_scope_permissions_expiry_and_forged_identity_refuse_with_cleanup(self):
        for index, key, value in [(4, 'total_count', 2), (4, 'repositories', []),
                                  (3, 'permissions', {'contents': 'write'}),
                                  (3, 'permissions', {'contents': 'write', 'workflows': 'write', 'actions': 'write'}),
                                  (3, 'expires_at', '2020-01-01T00:00:00Z'),
                                  (3, 'expires_at', 'forged'),
                                  (3, 'expires_at', '2099-01-01T00:00:00Z')]:
            with self.subTest(index=index, key=key, value=value):
                opener = Opener(); opener.rows[index][key] = value
                # Caller-authored identity cannot replace actual permission/census fields.
                opener.rows[3]['identity'] = {'appId': 4882140, 'repositoryId': 1269292704,
                                              'permissions': {'contents': 'write', 'workflows': 'write'}}
                transport, report = self.run_factory(opener)
                self.assertIs(transport, False)
                self.assertEqual(report['cleanup'], 'revoked')
                self.assertEqual(opener.calls[-1].method, 'DELETE')

    def test_response_redirect_pagination_oversize_and_status_refuse_without_retry(self):
        for mutation in ('redirect', 'pagination', 'oversize', 'status'):
            with self.subTest(mutation=mutation):
                opener = Opener()
                def mutate(index, response):
                    if index != 0: return
                    if mutation == 'redirect': response.url = 'https://untrusted.invalid/'
                    if mutation == 'pagination': response.headers['Link'] = '<next>'
                    if mutation == 'oversize': response.body = io.BytesIO(b'x' * 65537)
                    if mutation == 'status': response.status = 403
                opener.mutate = mutate
                transport, report = self.run_factory(opener)
                self.assertIs(transport, False)
                self.assertEqual(len(opener.calls), 1)

    def test_endpoint_budget_and_cleanup_failure_are_closed(self):
        opener = Opener(); opener.fail_cleanup = True
        transport, report = self.run_factory(opener)
        self.assertIs(transport, False)
        self.assertEqual(report['cleanup'], 'revocation-unknown')
        session = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
            phase_adapter=provider.AuthenticationPhaseAdapter(binding(), forbidden, lambda: 1), opener=Opener())
        for method, path in [('POST', '/repos/FS-GG/.github/git/refs'), ('GET', '/app?x=1'),
                             ('DELETE', '/installation/token')]:
            with self.assertRaises(provider.Refused): session._request(method, path, 'inert-jwt')
        self.assertEqual(session._opener.calls, [])
        session._gets = 8
        with self.assertRaises(provider.Refused): session._request('GET', '/app', 'inert-jwt')
        session.adapter.normal = 0
        with self.assertRaises(provider.Refused): session._request('GET', '/app', 'inert-jwt')

    def test_repository_census_identity_and_extra_rows_refuse_and_revoke_once(self):
        for repositories in ([{'id': 7, 'full_name': 'FS-GG/.github'}],
                             [{'id': 1269292704, 'full_name': 'FS-GG/other'}],
                             [{'id': 1269292704, 'full_name': 'FS-GG/.github'},
                              {'id': 7, 'full_name': 'FS-GG/other'}]):
            with self.subTest(repositories=repositories):
                opener = Opener(); opener.rows[4]['repositories'] = repositories
                qualified, report = self.run_factory(opener)
                self.assertFalse(qualified)
                self.assertEqual(report['cleanup'], 'revoked')
                self.assertEqual(report['requests']['issuancePosts'], 1)
                self.assertEqual(report['requests']['revocationDeletes'], 1)
                facts = provider.decode_auth_frame(self.last_frames[-1])['p']['facts']
                self.assertEqual(facts['firstCause'], {'stage': 'census', 'code': 'scope'})

    def test_cleanup_response_refusal_keeps_owned_effect_unknown_without_retry(self):
        for fault in ('status', 'redirect', 'pagination', 'body'):
            with self.subTest(fault=fault):
                opener = Opener(); original = opener.open
                def refuse_cleanup(request, timeout):
                    response = original(request, timeout)
                    if request.method == 'DELETE':
                        if fault == 'status': response.status = 403
                        if fault == 'redirect': response.url = 'https://untrusted.invalid/'
                        if fault == 'pagination': response.headers['Link'] = '<next>'
                        if fault == 'body': response.body = io.BytesIO(b'inert-owned-token')
                    return response
                opener.open = refuse_cleanup
                qualified, report = self.run_factory(opener)
                self.assertFalse(qualified)
                self.assertEqual(report['cleanup'], 'revocation-unknown')
                self.assertEqual(report['requests']['issuancePosts'], 1)
                self.assertEqual(report['requests']['revocationDeletes'], 1)
                terminal = provider.decode_auth_frame(self.last_frames[-1])['p']
                self.assertFalse(terminal['qualified'])
                self.assertEqual(terminal['facts']['revocation']['state'], 'unknown')

    def test_normal_production_factory_remains_unavailable(self):
        with self.assertRaises(provider.Refused): provider.approved_original_tag_transport()

    def test_publisher_auth_selection_is_inert_before_every_other_route(self):
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'result.json'
            argv = ['publisher', '--qualify-original-tag-authentication-only', '--result-path', str(path)]
            with patch.object(sys, 'argv', argv), patch.object(publisher, 'execute_publisher', forbidden), \
                 patch.object(provider, 'OriginalTagAuthenticationSession', forbidden):
                self.assertEqual(publisher.main(), 1)
            report = json.loads(path.read_bytes())
            self.assertFalse(report['credentialAcquisitionAttempted'])
            self.assertEqual(report['disposition'], 'blocked-authentication-route-inactive')
            for flag in ['--publish', '--preflight-only', '--recover-original-0103-tag-intent']:
                with patch.object(sys, 'argv', argv + [flag]), patch.object(sys, 'stderr', io.StringIO()), \
                     self.assertRaises(SystemExit):
                    publisher.main()

    def test_workflow_credentials_and_publication_exclude_auth_selection(self):
        text = (ROOT / '.github/workflows/release-successor-publish.yml').read_text()
        for name in ['Mint the ordinary protected-journal writer token', 'Bind the proved installation',
                     'Exchange nuget.org trusted publishing identity',
                     'Run the selected exact-candidate publication or bounded recovery profile']:
            step = text.split('- name: ' + name, 1)[1].split('\n      - ', 1)[0]
            self.assertIn('if: inputs.qualify_original_tag_authentication_only != true', step)
        inactive = text.split('- name: Select the inactive authentication-only route', 1)[1].split('\n      - ', 1)[0]
        self.assertNotIn('secrets.', inactive)
        self.assertIn('--qualify-original-tag-authentication-only', inactive)

class ProtocolControls(unittest.TestCase):
    def test_pinned_existing_wire_contract_labels_only_inert_source_coverage(self):
        pin = json.loads((pathlib.Path(__file__).parent / 'wire-contract.json').read_bytes())
        vectors = protocol_vectors()
        self.assertEqual(pin['protocol'], provider.AUTH_PROTOCOL)
        self.assertEqual(pin['profile'], provider.AUTH_PROFILE)
        self.assertEqual(pin['mode'], vectors['mode'])
        self.assertEqual(pin['gatePhases'], ['ready', 'issuance-possible', 'revocation-possible'])
        children = [bytes.fromhex(raw) for raw in vectors['children']]
        acks = [bytes.fromhex(raw) for raw in vectors['acks']]
        self.assertTrue(all(len(raw) <= pin['maximumFrameBytes'] for raw in children))
        self.assertTrue(all(len(raw) <= pin['maximumAckBytes'] for raw in acks))
        self.assertLessEqual(sum(map(len, children + acks)), pin['maximumAggregateBytes'])

    def test_selected_future_identity_is_distinct_from_synthetic_provider_fixture(self):
        pin = json.loads((pathlib.Path(__file__).parent / 'wire-contract.json').read_bytes())
        synthetic = pin['identities']['syntheticFixture']
        selected = pin['identities']['selectedFuture']
        self.assertEqual(synthetic, {'id': 4882140, 'installation': 123})
        self.assertEqual(selected, {'id': 4995487, 'installation': 162873149})
        self.assertEqual(binding()['app'], synthetic)
        self.assertNotEqual(selected, synthetic)
        vectors = protocol_vectors()
        selected_binding = copy.deepcopy(vectors['binding'])
        selected_binding['app'] = selected
        # This is an inert expected descriptor, never provider identity evidence.
        start = provider._auth_canonical({'v': provider.AUTH_PROTOCOL, 'k': 'start',
            'b': provider.validate_auth_binding(selected_binding), 'p': selected_binding})
        selected_frame = len(start).to_bytes(4, 'big') + start
        for expected, foreign in ((selected_binding, bytes.fromhex(vectors['start'])),
                                  (vectors['binding'], selected_frame)):
            with self.subTest(expected=expected['app']):
                calls = []
                subject = consumer.InertStartConsumer(expected)
                with self.assertRaises(provider.Refused):
                    subject.take(foreign, lambda *_: calls.append('exchange'), lambda: calls.append('clock'))
                self.assertEqual(calls, [])
        subject = consumer.InertStartConsumer(selected_binding)
        admitted = subject.take(selected_frame, forbidden, lambda: 1)
        self.assertEqual(admitted.binding['app'], selected)
        self.assertNotEqual(subject.binding_digest,
                            provider.validate_auth_binding(vectors['binding']))

    def test_independent_readback_mismatch_never_opens_ready_gate(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root); opener = Opener()
            original_open = pathlib.Path.open
            def mismatched_readback(path, mode='r', *args, **kwargs):
                if path == channel.path and mode == 'rb':
                    return io.BytesIO(b'{}\n')
                return original_open(path, mode, *args, **kwargs)
            child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                phase_adapter=adapter(channel), opener=opener)
            with patch.object(pathlib.Path, 'open', mismatched_readback):
                self.assertFalse(child.qualify(channel.report))
            self.assertTrue(channel.path.exists())  # Replacement may have happened.
            self.assertEqual(channel.acks, [])
            self.assertEqual(opener.calls, [])
            self.assertTrue(child.adapter.failed)
            self.assertFalse(channel.report['qualified'])
            self.assertNotIn('authenticationSession', channel.report)

    def test_first_validation_cause_survives_cleanup_and_terminal_reporting_failure(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root); opener = Opener()
            opener.rows[3]['permissions'] = {'contents': 'write'}
            opener.fail_cleanup = True
            def fail_terminal(raw):
                if provider.decode_auth_frame(raw)['k'] == 'terminal':
                    raise RuntimeError('inert-owned-token inert-jwt reporting sentinel')
                return channel(raw)
            subject = provider.AuthenticationPhaseAdapter(channel.binding, fail_terminal, lambda: 1)
            child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                phase_adapter=subject, opener=opener)
            self.assertFalse(child.qualify(channel.report))
            self.assertEqual(child.facts['firstCause'], {'stage': 'permissions', 'code': 'scope'})
            self.assertEqual(child.facts['revocation']['state'], 'unknown')
            self.assertEqual(channel.report['cleanup'], 'revocation-unknown')
            self.assertEqual(channel.report['requests']['revocationDeletes'], 1)
            self.assertEqual(provider.decode_auth_frame(channel.frames[-1])['k'], 'revocation-possible')
            retained = channel.path.read_bytes()
            for sentinel in (b'inert-owned-token', b'inert-jwt', b'reporting sentinel'):
                self.assertNotIn(sentinel, retained)
                self.assertNotIn(sentinel.decode(), json.dumps(channel.report))
            self.assertIsNone(child._jwt); self.assertIsNone(child._token)

    def test_blocked_older_persistence_serializes_before_terminal_without_late_overwrite(self):
        vectors = protocol_vectors(); bound = vectors['binding']
        frames = [bytes.fromhex(x) for x in vectors['children']]
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'result.json'; report = {'schema': 'fsgg.release-successor-result/1'}
            for raw in frames[:3]: publisher.persist_authentication_snapshot(path, report, bound, raw)
            payload = copy.deepcopy(provider.decode_auth_frame(frames[3])['p'])
            payload.pop('request'); payload['facts']['revocation']['state'] = 'unknown'
            payload['facts']['firstCause'] = {'stage': 'cleanup', 'code': 'cancelled'}
            payload.update({'publication': dict(provider.AUTH_PUBLICATION), 'publicationComplete': False,
                            'qualified': False, 'credentialReferencesDropped': True})
            terminal = provider.encode_auth_frame(bound, 5, 'terminal', payload)
            blocked, release, started, done = (threading.Event() for _ in range(4)); failures = []
            actual_fsync = publisher.os.fsync
            def delay(fd):
                if threading.current_thread().name == 'older-snapshot' and not blocked.is_set():
                    blocked.set()
                    if not release.wait(1): raise RuntimeError('bounded fixture wait expired')
                return actual_fsync(fd)
            def write(raw, terminal_writer=False):
                if terminal_writer: started.set()
                try: publisher.persist_authentication_snapshot(path, report, bound, raw)
                except BaseException as error: failures.append(error)
                finally:
                    if terminal_writer: done.set()
            older = threading.Thread(target=write, args=(frames[3],), name='older-snapshot')
            later = threading.Thread(target=write, args=(terminal, True), name='terminal-snapshot')
            with patch.object(publisher.os, 'fsync', delay):
                older.start()
                try:
                    self.assertTrue(blocked.wait(1)); later.start()
                    self.assertTrue(started.wait(1)); self.assertFalse(done.wait(0.01))
                finally:
                    release.set(); older.join(1)
                    if later.ident is not None: later.join(1)
            self.assertFalse(older.is_alive()); self.assertFalse(later.is_alive()); self.assertEqual(failures, [])
            retained = json.loads(path.read_bytes())['authenticationSession']
            self.assertEqual(retained['ordinal'], 5); self.assertEqual(retained['frame']['k'], 'terminal')
            self.assertEqual(retained['frame']['p']['facts']['revocation']['state'], 'unknown')

    def test_fresh_report_cannot_reset_either_durable_possible_effect(self):
        vectors = protocol_vectors(); bound = vectors['binding']
        frames = [bytes.fromhex(x) for x in vectors['children']]
        for last in (1, 3):
            with self.subTest(phase=provider.decode_auth_frame(frames[last])['k']), tempfile.TemporaryDirectory() as root:
                path = pathlib.Path(root) / 'result.json'
                report = {'schema': 'fsgg.release-successor-result/1'}
                for raw in frames[:last + 1]:
                    publisher.persist_authentication_snapshot(path, report, bound, raw)
                preserved = path.read_bytes()
                reset = RetainedChannel(root, bound); reset.path = path
                subject = adapter(reset)
                with self.assertRaises(publisher.Refused): subject.emit('ready', {'binding': bound})
                self.assertEqual(path.read_bytes(), preserved)
                self.assertEqual(reset.acks, [])
                self.assertTrue(subject.failed)

    def test_late_lower_ordinal_cannot_replace_retained_terminal(self):
        vectors = protocol_vectors(); bound = vectors['binding']
        frames = [bytes.fromhex(x) for x in vectors['children']]
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'result.json'; report = {'schema': 'fsgg.release-successor-result/1'}
            stale = None
            for index, raw in enumerate(frames):
                publisher.persist_authentication_snapshot(path, report, bound, raw)
                if index == 2: stale = copy.deepcopy(report)
            preserved = path.read_bytes()
            for caller in (stale, report):
                with self.assertRaises(publisher.Refused):
                    publisher.persist_authentication_snapshot(path, caller, bound, frames[3])
                self.assertEqual(path.read_bytes(), preserved)

    def test_loss_after_each_retained_phase_preserves_facts_and_stops_all_later_calls(self):
        for phase in provider.AUTH_PHASES:
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as root:
                channel = RetainedChannel(root); opener = Opener()
                def lose(raw):
                    ack = channel(raw)
                    if provider.decode_auth_frame(raw)['k'] == phase:
                        raise RuntimeError('synthetic channel loss')
                    return ack
                subject = provider.AuthenticationPhaseAdapter(channel.binding, lose, lambda: 1)
                child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                    phase_adapter=subject, opener=opener)
                self.assertFalse(child.qualify(channel.report))
                retained = json.loads(channel.path.read_bytes())['authenticationSession']['frame']
                self.assertEqual(retained['k'], phase)
                posts = sum(x.method == 'POST' for x in opener.calls)
                deletes = sum(x.method == 'DELETE' for x in opener.calls)
                self.assertEqual(posts, 0 if phase in {'ready', 'issuance-possible'} else 1)
                self.assertEqual(deletes, 1 if phase in {'revoked', 'terminal'} else 0)
                self.assertTrue(subject.failed)

    def test_exact_snapshot_retention_precedes_every_ack_and_terminal_has_no_bearer(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root)
            result = channel.report
            child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                phase_adapter=adapter(channel), opener=Opener())
            self.assertTrue(child.qualify(result))
            retained = json.loads(channel.path.read_bytes())['authenticationSession']
            self.assertEqual(retained, result['authenticationSession'])
            terminal = retained['frame']['p']
            self.assertTrue(terminal['qualified']); self.assertTrue(terminal['credentialReferencesDropped'])
            self.assertEqual(terminal['facts']['counters'], {'getsStarted': 4, 'issuanceReservations': 1, 'revocationReservations': 1})
            self.assertEqual(terminal['facts']['revocation'], {'state': 'verified', 'tokenSlot': 1, 'status': 204})
            for raw in channel.frames + channel.acks:
                self.assertNotIn(b'inert-jwt', raw); self.assertNotIn(b'inert-owned-token', raw)
            self.assertLessEqual(sum(map(len, channel.frames + channel.acks)), 16384)

    def test_every_gate_denial_prevents_its_dependent_effect_and_never_retries(self):
        for phase, posts, deletes in [('ready', 0, 0), ('issuance-possible', 0, 0), ('revocation-possible', 1, 0)]:
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as root:
                channel = RetainedChannel(root); channel.deny = phase
                opener = Opener(); child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                    phase_adapter=adapter(channel), opener=opener)
                self.assertFalse(child.qualify(channel.report))
                self.assertEqual(sum(x.method == 'POST' for x in opener.calls), posts)
                self.assertEqual(sum(x.method == 'DELETE' for x in opener.calls), deletes)
                self.assertTrue(child.adapter.failed)
                self.assertEqual([provider.decode_auth_frame(x)['k'] for x in channel.frames].count(phase), 1)

    def test_validation_failure_after_ownership_survives_cleanup_and_terminal(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root); opener = Opener(); opener.rows[3]['permissions'] = {'contents': 'write'}
            child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                phase_adapter=adapter(channel), opener=opener)
            self.assertFalse(child.qualify(channel.report))
            phases = [provider.decode_auth_frame(x) for x in channel.frames]
            self.assertEqual([x['k'] for x in phases], list(provider.AUTH_PHASES))
            self.assertEqual(phases[2]['p']['facts']['permissions']['status'], 'unknown')
            terminal = phases[-1]['p']['facts']
            self.assertEqual(terminal['permissions']['status'], 'failed')
            self.assertEqual(terminal['firstCause'], {'stage': 'permissions', 'code': 'scope'})
            self.assertEqual(terminal['revocation']['state'], 'verified')
            self.assertEqual(terminal['expiry']['status'], 'unknown')

    def test_issuance_and_revocation_transport_losses_remain_distinct_unknowns(self):
        for lost in ('issuance', 'revocation'):
            with self.subTest(lost=lost), tempfile.TemporaryDirectory() as root:
                channel = RetainedChannel(root); opener = Opener()
                if lost == 'revocation': opener.fail_cleanup = True
                else:
                    original = opener.open
                    def fail(request, timeout):
                        if request.method == 'POST': raise RuntimeError('inert-jwt secret provider body')
                        return original(request, timeout)
                    opener.open = fail
                child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
                    phase_adapter=adapter(channel), opener=opener)
                self.assertFalse(child.qualify(channel.report))
                facts = channel.report['authenticationSession']['frame']['p']['facts']
                if lost == 'issuance':
                    self.assertEqual(facts['issuance'], {'state': 'unknown', 'tokenSlot': None})
                    self.assertEqual(facts['revocation']['state'], 'not-started')
                else:
                    self.assertEqual(facts['issuance']['state'], 'response-owned')
                    self.assertEqual(facts['revocation']['state'], 'unknown')
                self.assertNotIn('secret provider body', json.dumps(channel.report))

    def test_ack_encoding_binding_and_boolean_ordinal_are_closed(self):
        for error in ('boolean', 'digest', 'binding', 'partial', 'duplicate', 'extra'):
            with self.subTest(error=error):
                bound = binding()
                def exchange(raw):
                    row = provider.decode_auth_frame(raw)
                    ack = {'v': provider.AUTH_PROTOCOL, 'k': 'ack', 'b': row['b'], 'n': row['n'],
                           'h': __import__('hashlib').sha256(raw).hexdigest()}
                    if error == 'boolean': ack['n'] = True
                    if error == 'digest': ack['h'] = '0' * 64
                    if error == 'binding': ack['b'] = '0' * 64
                    if error == 'extra': ack['token'] = 'forged'
                    data = provider._auth_canonical(ack)
                    if error == 'duplicate': data = data[:-1] + b',"n":1}'
                    framed = len(data).to_bytes(4, 'big') + data
                    return framed[:-1] if error == 'partial' else framed
                subject = provider.AuthenticationPhaseAdapter(bound, exchange, lambda: 1)
                with self.assertRaises(provider.Refused): subject.emit('ready', {'binding': bound})
                self.assertTrue(subject.failed)
                with self.assertRaises(provider.Refused): subject.emit('ready', {'binding': bound})

    def test_original_tick_cutoffs_are_injected_and_never_renewed(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root); tick = [269999]
            subject = adapter(channel, ticks=lambda: tick[0])
            subject.emit('ready', {'binding': channel.binding})
            tick[0] = 270000
            with self.assertRaises(provider.Refused): subject.check()
            self.assertEqual(subject.check(cleanup=True), 270000)
            tick[0] = 285000
            with self.assertRaises(provider.Refused): subject.check(cleanup=True)
            self.assertEqual(subject.binding['clock']['total'], '300000')

    def test_retention_failure_never_yields_ack_and_keeps_possible_projection(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root); subject = adapter(channel)
            subject.emit('ready', {'binding': channel.binding})
            prior = channel.path.read_bytes()
            with patch.object(publisher.os, 'fsync', side_effect=OSError('inert write failure')):
                with self.assertRaises(OSError):
                    # Writer failure cannot be translated into ACK eligibility.
                    publisher.persist_authentication_snapshot(channel.path, channel.report, channel.binding,
                        provider.encode_auth_frame(channel.binding, 2, 'terminal', {
                            'facts': provider.empty_auth_facts(), 'publication': dict(provider.AUTH_PUBLICATION),
                            'publicationComplete': False, 'qualified': False, 'credentialReferencesDropped': True}))
            self.assertEqual(channel.path.read_bytes(), prior)
            self.assertEqual(len(channel.acks), 1)

    def test_monotone_assertions_success_flags_and_zero_publication_cannot_be_forged(self):
        data = protocol_vectors()
        bound = data['binding']; frames = [provider.decode_auth_frame(bytes.fromhex(x)) for x in data['children']]
        facts = frames[-1]['p']['facts']
        for name in ('app', 'installation', 'permissions', 'expiry', 'census'):
            mutated = copy.deepcopy(facts); mutated[name]['status'] = 'unknown'
            with self.assertRaises(provider.Refused): provider.validate_auth_facts(mutated, bound, facts)
        for key, value in [('publicationComplete', True), ('publication', dict(provider.AUTH_PUBLICATION, tag=1)),
                           ('credentialReferencesDropped', False)]:
            altered = copy.deepcopy(frames[-1]['p']); altered[key] = value
            with self.assertRaises(provider.Refused): provider.validate_auth_payload('terminal', altered, bound)
        failed = copy.deepcopy(facts); failed['permissions']['status'] = 'failed'
        with self.assertRaises(provider.Refused): provider.validate_auth_facts(facts, bound, failed)

    def test_maximum_legal_binding_and_terminal_fit_without_truncation(self):
        bound = binding(); maximum = (1 << 64) - 1; top = (1 << 63) - 1
        bound['process']['pid'] = maximum
        bound['mode'] = 'native'; bound['workflow']['run'] = maximum; bound['workflow']['attempt'] = (1 << 32) - 1
        bound['app'] = {'id': maximum, 'installation': maximum}
        for key in ('frequency', 'start', 'normal', 'work', 'total'): bound['clock'][key] = str(top)
        provider.validate_auth_binding(bound)
        ready = provider.encode_auth_frame(bound, 1, 'ready', {'binding': bound})
        data = protocol_vectors(); terminal = provider.decode_auth_frame(bytes.fromhex(data['children'][-1]))['p']
        terminal['qualified'] = False
        for item in ('app', 'installation', 'permissions', 'expiry', 'census'): terminal['facts'][item]['status'] = 'failed'
        terminal['facts']['app']['id'] = maximum
        terminal['facts']['installation'].update({'id': maximum, 'appId': maximum})
        terminal['facts']['expiry'].update({'unix': top, 'observedUnix': top})
        terminal['facts']['census'].update({'total': maximum, 'repositoryId': maximum})
        terminal['facts']['firstCause'] = {'stage': 'revocation-possible', 'code': 'credential-input'}
        final = provider.encode_auth_frame(bound, 6, 'terminal', terminal)
        self.assertLessEqual(len(ready), 2048); self.assertLessEqual(len(final), 2048)

    def test_inert_consumer_accepts_exact_producer_start_once(self):
        vectors = protocol_vectors(); expected = vectors['binding']
        held = copy.deepcopy(expected)
        subject = consumer.InertStartConsumer(expected)
        expected['app']['id'] = 1  # Caller mutation cannot change the held identity.
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root, held)
            adapter = subject.take(bytes.fromhex(vectors['start']), channel, lambda: 1)
            self.assertEqual(subject.binding_digest, provider.validate_auth_binding(held))
            self.assertEqual(channel.frames, [])  # Start admission emits no child phase.
            adapter.emit('ready', {'binding': held})
            self.assertEqual([provider.decode_auth_frame(x)['k'] for x in channel.frames], ['ready'])
            with self.assertRaises(provider.Refused):
                subject.take(bytes.fromhex(vectors['start']), channel, lambda: 1)

    def test_inert_consumer_refuses_changed_or_ambiguous_start_before_callbacks(self):
        vectors = protocol_vectors(); raw = bytes.fromhex(vectors['start'])
        row = json.loads(raw[4:]); binding = vectors['binding']
        cases = {'short': raw[:-1], 'length': b'\0\0\0\1' + raw[4:],
                 'digest': None, 'binding': None, 'extra': None,
                 'ordinal': None, 'duplicate': None, 'noncanonical': None}
        for name in ('digest', 'binding', 'extra', 'ordinal'):
            changed = copy.deepcopy(row)
            if name == 'digest': changed['b'] = '0' * 64
            if name == 'binding': changed['p']['app']['id'] += 1
            if name == 'extra': changed['credential'] = 'forged'
            if name == 'ordinal': changed['n'] = 1
            body = provider._auth_canonical(changed)
            cases[name] = len(body).to_bytes(4, 'big') + body
        body = raw[4:-1] + b',"v":"fsgg.execution.phase-result/1"}'
        cases['duplicate'] = len(body).to_bytes(4, 'big') + body
        body = json.dumps(row, indent=1).encode('ascii')
        cases['noncanonical'] = len(body).to_bytes(4, 'big') + body
        for name, frame in cases.items():
            with self.subTest(name=name):
                calls = []
                subject = consumer.InertStartConsumer(binding)
                with self.assertRaises(provider.Refused):
                    subject.take(frame, lambda *_: calls.append('exchange'), lambda: calls.append('clock'))
                self.assertEqual(calls, [])
                with self.assertRaises(provider.Refused):
                    subject.take(raw, lambda *_: calls.append('exchange'), lambda: 1)

    def test_inert_consumer_refuses_native_binding_without_a_port(self):
        bound = binding(); bound['mode'] = 'native'; bound['workflow']['run'] = 1; bound['workflow']['attempt'] = 1
        with self.assertRaises(provider.Refused): consumer.InertStartConsumer(bound)


def contract_bundle_digest(profile="factory"):
    if profile != "factory":
        from callback_pair_profile import select
        return select(ROOT, profile)[0]["bundleDigest"]
    import hashlib
    manifest = json.loads((ROOT / 'tests/release-successor-authentication/callable-source-manifest.json').read_bytes())
    rows = sorted(manifest['files'], key=lambda row: row['repository'] + '/' + row['path'])
    for row in rows:
        if row['repository'] == 'FS-GG/.github':
            assert hashlib.sha256((ROOT / row['path']).read_bytes()).hexdigest() == row['sha256']
    raw = ''.join(row['sha256'] + '  ' + row['repository'] + '/' + row['path'] + '\n' for row in rows).encode('ascii')
    digest = hashlib.sha256(raw).hexdigest()
    assert digest == manifest['bundleDigest']
    return digest


def protocol_vectors(profile="factory"):
    import hashlib
    with tempfile.TemporaryDirectory() as root:
        channel = RetainedChannel(root)
        # Deterministic fixture identities/timestamps, never an observed invocation.
        channel.binding['source']['contract'] = contract_bundle_digest(profile)
        opener = Opener()
        opener.rows[3]['expires_at'] = '2030-03-17T18:16:40Z'
        child = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123,
            phase_adapter=adapter(channel), opener=opener, wall_clock=lambda: 1900000000)
        assert child.qualify(channel.report)
        start = provider._auth_canonical({'v': provider.AUTH_PROTOCOL, 'k': 'start',
            'b': provider.validate_auth_binding(channel.binding), 'p': channel.binding})
        return {'binding': channel.binding, 'start': (len(start).to_bytes(4, 'big') + start).hex(),
                'children': [x.hex() for x in channel.frames], 'acks': [x.hex() for x in channel.acks],
                'profile': provider.AUTH_PROFILE, 'mode': 'inert',
                'frameBytes': [len(x) for x in channel.frames]}



def callback_pair_vectors(profile='inert-callback-pair'):
    from callback_pair_profile import select
    select(ROOT, profile)  # Verify the selected source before running the producer.
    vectors = protocol_vectors(profile)
    control = CallbackOwnerControls()
    with tempfile.TemporaryDirectory() as root:
        owner = control.prepared(root, vectors['binding'])
        exchanges = []
        def exchange(request):
            exchanges.append({'request': request.hex(), 'response': owner.handle(request).hex()})
        exchange(control.request(vectors['binding'], 1, 'bind'))
        for index, rawhex in enumerate(vectors['children']):
            raw = bytes.fromhex(rawhex)
            phase = provider.decode_auth_frame(raw)
            for offset, kind in ((2, 'validate'), (3, 'retain')):
                exchange(control.request(vectors['binding'], index * 2 + offset, kind, raw, phase))
        projection = json.loads(control.path.read_bytes())['authenticationSession']
        return {'schema': 'fsgg.authentication-callback-vectors/1',
                'qualification': 'inert callback pairing only; actual Python provider/owner/sink; managed hosted join pending',
                'binding': vectors['binding'], 'exchanges': exchanges,
                'finalReadback': {key: projection[key] for key in ('binding', 'ordinal', 'frameDigest')}}


class CallbackOwnerControls(unittest.TestCase):
    def prepared(self, root, bound, sink=None):
        self.now = 1
        self.path = pathlib.Path(root) / 'callback-result.json'
        self.report = {'schema': 'fsgg.release-successor-result/1'}
        self.clock = consumer.InertCallbackClock(bound['clock']['domain'], 1000, 0, 270000, 285000,
                                                300000, lambda: self.now)
        return consumer.InertCallbackOwner(bound, self.path, self.report,
            sink or publisher.persist_authentication_snapshot, self.clock)

    def request(self, bound, sequence, kind, raw=None, phase=None):
        import base64
        import hashlib
        row = {'v': consumer.CALLBACK_PROTOCOL, 'kind': kind, 'invocation': bound['invocation'],
               'binding': provider.validate_auth_binding(bound), 'sequence': sequence,
               'ordinal': 0 if phase is None else phase['n'],
               'frameDigest': provider.validate_auth_binding(bound) if phase is None else hashlib.sha256(raw).hexdigest()}
        if kind == 'bind': row['bindingBase64'] = base64.b64encode(provider._auth_canonical(bound)).decode('ascii')
        if kind == 'validate': row['frameBase64'] = base64.b64encode(raw).decode('ascii')
        return consumer._callback_frame(row)

    def fixture(self):
        vectors = protocol_vectors()
        return vectors['binding'], [bytes.fromhex(raw) for raw in vectors['children']]

    def test_callback_owner_actual_validator_and_sink_six_phase_roundtrip(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, bound)
            self.assertEqual(provider.decode_auth_frame(subject.handle(self.request(bound, 1, 'bind')))['kind'], 'bound')
            for index, raw in enumerate(frames):
                phase = provider.decode_auth_frame(raw)
                validated = subject.handle(self.request(bound, index * 2 + 2, 'validate', raw, phase))
                self.assertEqual(provider.decode_auth_frame(validated)['kind'], 'validated')
                self.assertEqual(self.report.get('authenticationSession', {}).get('ordinal', 0), index)
                retained = subject.handle(self.request(bound, index * 2 + 3, 'retain', raw, phase))
                self.assertEqual(provider.decode_auth_frame(retained)['kind'], 'retained')
                self.assertEqual(json.loads(self.path.read_bytes())['authenticationSession']['ordinal'], index + 1)
            with self.assertRaises(provider.Refused): subject.handle(self.request(bound, 13, 'retain', raw, phase))
            self.assertLess(subject.charged_bytes, consumer.CALLBACK_AGGREGATE)

    def test_callback_inert_child_entry_runs_actual_library_without_default_http(self):
        with tempfile.TemporaryDirectory() as root:
            channel = RetainedChannel(root)
            prepared = consumer.InertStartConsumer(channel.binding)
            body = provider._auth_canonical({'v': provider.AUTH_PROTOCOL, 'k': 'start',
                'b': prepared.binding_digest, 'p': channel.binding})
            start = len(body).to_bytes(4, 'big') + body
            opener = Opener()
            self.assertTrue(consumer.run_inert_authentication(prepared, start, channel, lambda: 1,
                                                            opener, channel.report, time.time))
            self.assertEqual(len(opener.calls), 6)
            self.assertEqual(len(channel.frames), 6)
            self.assertEqual(channel.report['cleanup'], 'revoked')
            with self.assertRaises(provider.Refused):
                consumer.run_inert_authentication(prepared, start, forbidden, forbidden, None, {}, time.time)

    def test_callback_request_decoder_bounds_fragments_pipeline_and_partial_write(self):
        bound, _ = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, bound)
            decoder = consumer.InertCallbackRequestDecoder(subject)
            raw = self.request(bound, 1, 'bind')
            for index, value in enumerate(raw):
                reply = decoder.feed(bytes([value]))
                self.assertEqual(reply is not None, index == len(raw) - 1)
            self.assertEqual(decoder.observed_request_bytes, len(raw))
            with self.assertRaises(provider.Refused): decoder.confirm_response_written(len(reply) - 1)
            with self.assertRaises(provider.Refused): decoder.feed(raw)
        for data in (b'\x00\x00\x10\x00', self.request(bound, 1, 'bind') * 2):
            with tempfile.TemporaryDirectory() as root:
                decoder = consumer.InertCallbackRequestDecoder(self.prepared(root, bound))
                with self.assertRaises(provider.Refused): decoder.feed(data)
                self.assertEqual(decoder.observed_request_bytes, len(data))
                self.assertFalse(self.path.exists())

    def test_callback_decoder_refuses_overlap_while_validation_or_sink_is_blocked(self):
        bound, frames = self.fixture()
        for block_sink in (False, True):
            with self.subTest(block_sink=block_sink), tempfile.TemporaryDirectory() as root:
                entered, release = threading.Event(), threading.Event()
                sink_calls = []
                def sink(*args):
                    sink_calls.append(1)
                    entered.set()
                    if not release.wait(2): raise AssertionError('fixture sink release missing')
                    return publisher.persist_authentication_snapshot(*args)
                subject = self.prepared(root, bound, sink if block_sink else None)
                decoder = consumer.InertCallbackRequestDecoder(subject)
                bind = self.request(bound, 1, 'bind')
                decoder.confirm_response_written(len(decoder.feed(bind)))
                phase = provider.decode_auth_frame(frames[0])
                validation = self.request(bound, 2, 'validate', frames[0], phase)
                if block_sink: decoder.confirm_response_written(len(decoder.feed(validation)))
                request = self.request(bound, 3, 'retain', frames[0], phase) if block_sink else validation
                original = provider.validate_auth_payload
                def validate(*args):
                    entered.set()
                    if not release.wait(2): raise AssertionError('fixture validator release missing')
                    return original(*args)
                replies, failures = [], []
                def feed():
                    try: replies.append(decoder.feed(request))
                    except Exception as error: failures.append(error)
                with patch.object(provider, 'validate_auth_payload', original if block_sink else validate):
                    worker = threading.Thread(target=feed)
                    worker.start()
                    try:
                        self.assertTrue(entered.wait(2))
                        with self.assertRaises(provider.Refused): decoder.feed(b'x')
                        self.assertEqual(subject.first_cause, 'order')
                    finally:
                        release.set(); worker.join(2)
                    self.assertFalse(worker.is_alive())
                self.assertEqual(failures, [])
                self.assertEqual(provider.decode_auth_frame(replies[0])['kind'], 'refused')
                self.assertEqual(provider.decode_auth_frame(replies[0])['reason'], 'order')
                expected_bytes = len(bind) + len(validation) + (len(request) if block_sink else 0) + 1
                self.assertEqual(decoder.observed_request_bytes, expected_bytes)
                with self.assertRaises(provider.Refused): decoder.confirm_response_written(len(replies[0]))
                with self.assertRaises(provider.Refused): decoder.feed(request)
                self.assertEqual(sink_calls, [1] if block_sink else [])
                self.assertEqual(self.path.exists(), block_sink)

    def test_callback_finalization_rejects_equal_python_values_with_different_json_types(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, bound)
            subject.handle(self.request(bound, 1, 'bind'))
            phase = provider.decode_auth_frame(frames[0])
            subject.handle(self.request(bound, 2, 'validate', frames[0], phase))
            subject.handle(self.request(bound, 3, 'retain', frames[0], phase))
            before = self.path.read_bytes()
            mutations = [
                (['ordinal'], True), (['ordinal'], 1.0),
                (['frame', 'n'], True), (['frame', 'n'], 1.0),
                (['frame', 'p', 'binding', 'originalGeneration'], 2.0),
                (['frame', 'p', 'binding', 'workflow', 'attempt'], False),
                (['frame', 'p', 'binding', 'app', 'id'], float(bound['app']['id']))]
            for keys, value in mutations:
                with self.subTest(keys=keys, value=value):
                    altered = copy.deepcopy(self.report)
                    row = altered['authenticationSession']
                    for key in keys[:-1]: row = row[key]
                    row[keys[-1]] = value
                    # Demonstrate why ordinary Python value equality is insufficient.
                    self.assertEqual(altered['authenticationSession'], self.report['authenticationSession'])
                    with self.assertRaises(publisher.Refused): publisher.write_result(self.path, altered)
                    self.assertEqual(self.path.read_bytes(), before)
                    self.assertFalse(self.path.with_name(self.path.name + '.authentication-finalizing').exists())
            self.report['pendingReason'] = 'typed projection preserved'
            publisher.write_result(self.path, self.report)
            self.assertEqual(json.loads(self.path.read_bytes())['authenticationSession'], self.report['authenticationSession'])

    def test_callback_process_fixture_entry_runs_actual_sink_without_process_launch(self):
        import base64
        import runpy
        fixture = runpy.run_path(str(ROOT / 'tests/release-successor-authentication/callback-fixture.py'), run_name='callback_fixture_unit')
        vectors = json.loads((ROOT / 'tests/release-successor-authentication/callback-vectors.json').read_bytes())
        requests = b''.join(bytes.fromhex(row['request']) for row in vectors['exchanges'])
        expected = b''.join(bytes.fromhex(row['response']) for row in vectors['exchanges'])
        output = io.BytesIO()
        with tempfile.TemporaryDirectory() as root:
            result = pathlib.Path(root) / 'fixture-result.json'
            arguments = ['callback-fixture.py', base64.b64encode(provider._auth_canonical(vectors['binding'])).decode(),
                         base64.b64encode(provider._auth_canonical(vectors['binding']['clock'])).decode(), str(result)]
            with patch.object(sys, 'argv', arguments), patch.object(sys, 'stdin', types.SimpleNamespace(buffer=io.BytesIO(requests))), \
                 patch.object(sys, 'stdout', types.SimpleNamespace(buffer=output)):
                self.assertEqual(fixture['main'](), 0)
            self.assertEqual(output.getvalue(), expected)
            self.assertEqual(json.loads(result.read_bytes())['authenticationSession']['ordinal'], 6)

    def test_callback_manifest_recipe_sorts_by_ascii_repository_path_key(self):
        import hashlib
        manifest = json.loads((ROOT / 'tests/release-successor-authentication/callable-source-manifest.json').read_bytes())
        self.assertIn('sorted by ASCII repository/path key', manifest['digestRecipe'])
        ordered = sorted(manifest['files'], key=lambda row: (row['repository'] + '/' + row['path']).encode('ascii'))
        self.assertEqual(manifest['files'], ordered)
        rows = [row['sha256'] + '  ' + row['repository'] + '/' + row['path'] + '\n' for row in ordered]
        self.assertEqual(hashlib.sha256(''.join(rows).encode('ascii')).hexdigest(), manifest['bundleDigest'])
        self.assertEqual(contract_bundle_digest(), manifest['bundleDigest'])

    def test_callback_frozen_vectors_match_current_manifest_and_actual_sink(self):
        vectors = json.loads((ROOT / 'tests/release-successor-authentication/callback-vectors.json').read_bytes())
        self.assertEqual(vectors['binding']['source']['contract'], contract_bundle_digest())
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, vectors['binding'])
            self.assertEqual(len(vectors['exchanges']), 13)
            for exchange in vectors['exchanges']:
                self.assertEqual(subject.handle(bytes.fromhex(exchange['request'])).hex(), exchange['response'])
            projection = json.loads(self.path.read_bytes())['authenticationSession']
            self.assertEqual({key: projection[key] for key in ('binding', 'ordinal', 'frameDigest')}, vectors['finalReadback'])

    def test_callback_finalization_preserves_the_original_projection(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, bound)
            subject.handle(self.request(bound, 1, 'bind'))
            phase = provider.decode_auth_frame(frames[0])
            subject.handle(self.request(bound, 2, 'validate', frames[0], phase))
            subject.handle(self.request(bound, 3, 'retain', frames[0], phase))
            projection = copy.deepcopy(self.report['authenticationSession'])
            self.report['pendingReason'] = 'source-only inert finalization'
            publisher.write_result(self.path, self.report)
            durable = json.loads(self.path.read_bytes())
            self.assertEqual(durable['authenticationSession'], projection)
            altered = copy.deepcopy(self.report)
            altered['authenticationSession']['frameDigest'] = '0' * 64
            with self.assertRaises(publisher.Refused): publisher.write_result(self.path, altered)
            self.assertEqual(json.loads(self.path.read_bytes()), durable)

    def test_callback_owner_validation_never_persists_and_failed_attempt_cannot_retry(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            calls = []
            def sink(*args):
                calls.append(1)
                return publisher.persist_authentication_snapshot(*args)
            subject = self.prepared(root, bound, sink)
            subject.handle(self.request(bound, 1, 'bind'))
            phase = provider.decode_auth_frame(frames[0])
            validation = self.request(bound, 2, 'validate', frames[0], phase)
            subject.handle(validation)
            self.assertFalse(self.path.exists()); self.assertEqual(calls, [])
            with self.assertRaises(provider.Refused): subject.handle(validation)
            with self.assertRaises(provider.Refused): subject.handle(self.request(bound, 3, 'retain', frames[0], phase))
            self.assertEqual(calls, [])

    def test_callback_terminal_validation_finishing_at_work_cannot_retain(self):
        bound, frames = self.fixture()
        # Early terminal after ready preserves real monotone failure facts.
        facts = provider.empty_auth_facts()
        facts['firstCause'] = {'stage': 'protocol', 'code': 'child-loss'}
        terminal = provider.encode_auth_frame(bound, 2, 'terminal', {
            'facts': facts, 'publication': dict.fromkeys(['tag', 'journal', 'feed', 'release', 'asset', 'channel'], 0),
            'publicationComplete': False, 'qualified': False, 'credentialReferencesDropped': True})
        with tempfile.TemporaryDirectory() as root:
            subject = self.prepared(root, bound)
            subject.handle(self.request(bound, 1, 'bind'))
            ready = provider.decode_auth_frame(frames[0])
            subject.handle(self.request(bound, 2, 'validate', frames[0], ready))
            subject.handle(self.request(bound, 3, 'retain', frames[0], ready))
            phase = provider.decode_auth_frame(terminal)
            original = provider.validate_auth_payload
            def late(*args):
                original(*args)
                self.now = 285000
            self.now = 284999
            with patch.object(provider, 'validate_auth_payload', late):
                reply = provider.decode_auth_frame(subject.handle(self.request(bound, 4, 'validate', terminal, phase)))
            self.assertEqual(reply['kind'], 'refused'); self.assertEqual(reply['reason'], 'deadline')
            with self.assertRaises(provider.Refused): subject.handle(self.request(bound, 5, 'retain', terminal, phase))
            self.assertEqual(self.report['authenticationSession']['ordinal'], 1)

    def test_callback_late_retention_keeps_durable_projection_and_cannot_retry(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            calls = []
            def sink(*args):
                calls.append(1)
                receipt = publisher.persist_authentication_snapshot(*args)
                self.now = 285000
                return receipt
            subject = self.prepared(root, bound, sink)
            subject.handle(self.request(bound, 1, 'bind'))
            phase = provider.decode_auth_frame(frames[0])
            subject.handle(self.request(bound, 2, 'validate', frames[0], phase))
            reply = provider.decode_auth_frame(subject.handle(self.request(bound, 3, 'retain', frames[0], phase)))
            self.assertEqual(reply['kind'], 'refused')
            self.assertEqual(json.loads(self.path.read_bytes())['authenticationSession']['ordinal'], 1)
            with self.assertRaises(provider.Refused): subject.handle(self.request(bound, 4, 'retain', frames[0], phase))
            self.assertEqual(calls, [1])
            with self.assertRaises(provider.Refused): self.prepared(root, bound)

    def test_callback_native_refuses_before_clock_and_domain_rejects_secret_canary(self):
        bound, frames = self.fixture()
        with tempfile.TemporaryDirectory() as root:
            native = copy.deepcopy(bound); native['mode'] = 'native'
            with self.assertRaises(provider.Refused):
                consumer.InertCallbackOwner(native, pathlib.Path(root) / 'result', {}, forbidden, forbidden)
            subject = self.prepared(root, bound)
            subject.handle(self.request(bound, 1, 'bind'))
            phase = provider.decode_auth_frame(frames[0])
            phase['p']['private-canary'] = 'NEVER-RETAIN-TOKEN-OR-PATH'
            body = provider._auth_canonical(phase)
            raw = len(body).to_bytes(4, 'big') + body
            reply = subject.handle(self.request(bound, 2, 'validate', raw, phase))
            self.assertEqual(provider.decode_auth_frame(reply)['reason'], 'domain')
            self.assertNotIn(b'NEVER-RETAIN', reply)
            self.assertFalse(self.path.exists()); self.assertNotIn('NEVER-RETAIN', json.dumps(self.report))

    def test_callback_queued_entry_at_work_refuses_sink_and_wrong_receipt_retains_obligation(self):
        bound, frames = self.fixture()
        for late in (True, False):
            with self.subTest(late=late), tempfile.TemporaryDirectory() as root:
                calls = []
                def sink(*args):
                    calls.append(1)
                    return {'binding': '0' * 64, 'ordinal': 1, 'frameDigest': '0' * 64}
                subject = self.prepared(root, bound, sink)
                subject.handle(self.request(bound, 1, 'bind'))
                phase = provider.decode_auth_frame(frames[0])
                subject.handle(self.request(bound, 2, 'validate', frames[0], phase))
                if late: self.now = 285000
                reply = provider.decode_auth_frame(subject.handle(self.request(bound, 3, 'retain', frames[0], phase)))
                self.assertEqual(reply['reason'], 'deadline' if late else 'readback')
                self.assertEqual(calls, [] if late else [1])
                self.assertEqual(subject.unresolved_retention, not late)


class SelectedFactoryOpener:
    """Synthetic responses to the ACTUAL credential and tag Request/read paths."""
    def __init__(self):
        self.calls = []
        self.rows = Opener().rows
        self.rows[0]['id'] = 4995487
        for index in (1, 2):
            self.rows[index].update(id=162873149, app_id=4995487)
        self.fail_cleanup = False
        self.mutate = None
    def open(self, request, timeout):
        self.calls.append(request)
        assert request.full_url.startswith('https://api.github.com/')
        assert 0 < timeout <= 60
        if request.full_url.endswith('/git/refs'):
            assert request.method == 'POST'
            assert request.get_header('Authorization') == 'Bearer inert-owned-token'
            assert json.loads(request.data) == {'ref': provider.ORIGINAL_TAG_REF, 'sha': provider.ORIGINAL_TAG_SOURCE}
            response = Response(request, {'ref': provider.ORIGINAL_TAG_REF, 'object': {
                'type': 'commit', 'sha': provider.ORIGINAL_TAG_SOURCE,
                'url': 'https://api.github.com/repos/FS-GG/.github/git/commits/' + provider.ORIGINAL_TAG_SOURCE}}, 201)
            response.read = response.read1
            return response
        if request.method == 'DELETE':
            Opener.assert_cleanup(request)
            if self.fail_cleanup: raise RuntimeError('inert-owned-token secret cleanup')
            return Response(request, None, 204)
        index = len([r for r in self.calls if r.method != 'DELETE' and not r.full_url.endswith('/git/refs')]) - 1
        paths = ['/app', '/app/installations/162873149', '/repos/FS-GG/.github/installation',
                 '/app/installations/162873149/access_tokens', '/installation/repositories?per_page=100&page=1']
        assert request.full_url == 'https://api.github.com' + paths[index]
        assert request.method == ('POST' if index == 3 else 'GET')
        assert request.get_header('Authorization') == ('Bearer inert-owned-token' if index == 4 else 'Bearer inert-jwt')
        response = Response(request, copy.deepcopy(self.rows[index]), 201 if index == 3 else 200)
        if self.mutate: self.mutate(index, response)
        return response


class SelectedOriginalFactoryControls(unittest.TestCase):
    def prepare(self, root, *, bound=None, candidate=None, checkpoint=None, admit=None, retain=None, opener=None):
        self.bound = bound or binding()
        if bound is None: self.bound['app'] = {'id': 4995487, 'installation': 162873149}
        self.report, self.credentials, self.snapshots = {}, [], []
        self.opener = opener or SelectedFactoryOpener()
        channel = RetainedChannel(root, self.bound)
        def supply():
            self.credentials.append('jwt')
            return 'inert-jwt'
        return provider.InertOriginalTagCredentialFactory(self.bound,
            candidate or provider.ORIGINAL_TAG_FACTORY_CANDIDATE,
            checkpoint or provider.ORIGINAL_TAG_FACTORY_CHECKPOINT,
            phase_adapter=adapter(channel), jwt_supplier=supply, opener=self.opener,
            admit=admit or (lambda: True), retain=retain or self.snapshots.append, report=self.report)

    def test_selected_factory_actual_proof_one_exact_send_and_lexical_revocation(self):
        with tempfile.TemporaryDirectory() as root:
            factory = self.prepare(root)
            with factory() as token:
                self.assertEqual(token.identity()['appId'], 4995487)
                effect = __import__('release_successor_execution').Effect('tag', provider.ORIGINAL_TAG_SOURCE, provider.ORIGINAL_TAG_CONTENT)
                self.assertEqual(token.dispatch(effect, 100, lambda: 0).state, 'applied')
                with self.assertRaises(provider.Refused): token.dispatch(effect, 100, lambda: 0)
            self.assertEqual([r.method for r in self.opener.calls], ['GET', 'GET', 'GET', 'POST', 'GET', 'POST', 'DELETE'])
            self.assertEqual(json.loads(self.opener.calls[3].data), {'repository_ids': [1269292704],
                'permissions': {'contents': 'write', 'workflows': 'write'}})
            self.assertTrue(self.report['qualified']); self.assertEqual(self.report['cleanup'], 'revoked')
            with self.assertRaises(provider.Refused): token.identity()
            with self.assertRaises(provider.Refused):
                with factory(): pass
            self.assertEqual(self.credentials, ['jwt'])
            self.assertNotIn('inert-owned-token', json.dumps(self.report))
            self.assertNotIn('inert-jwt', json.dumps(self.snapshots))

    def test_factory_candidate_is_complete_original_publisher_tuple(self):
        self.assertEqual(provider.ORIGINAL_TAG_FACTORY_CANDIDATE, publisher.ORIGINAL_0103_CANDIDATE)

    def test_authenticated_wrong_identity_and_scope_reply_refuses_before_token_issuance(self):
        for index, key, value in [(0, 'id', 4882140), (1, 'app_id', 8), (1, 'account', {'login': 'other'}),
                (1, 'suspended_at', '2026-10-15T00:00:00Z'), (2, 'id', 1)]:
            with self.subTest(key=key), tempfile.TemporaryDirectory() as root:
                opener = SelectedFactoryOpener(); opener.rows[index][key] = value
                factory = self.prepare(root, opener=opener)
                with self.assertRaises(provider.Refused):
                    with factory(): forbidden()
                self.assertFalse(any(r.method == 'POST' for r in opener.calls))
                self.assertEqual(self.report['cleanup'], 'not-issued')

    def test_reporter_loss_before_issuance_refuses_and_owned_cleanup_still_runs(self):
        for phase in ('issuance-possible', 'token-owned', 'revocation-possible', 'revoked'):
            with self.subTest(phase=phase), tempfile.TemporaryDirectory() as root:
                def retain(row):
                    if row['lastPhase'] == phase: raise OSError('inert-jwt secret reporter')
                factory = self.prepare(root, retain=retain)
                with self.assertRaises(provider.Refused):
                    with factory(): pass
                methods = [r.method for r in self.opener.calls]
                if phase == 'issuance-possible': self.assertNotIn('POST', methods)
                else: self.assertEqual(methods[-1], 'DELETE')
                self.assertEqual(self.report['firstCause'], {'stage': 'reporting', 'code': 'retention'})
                self.assertFalse(self.report['qualified'])
                self.assertNotIn('secret', json.dumps(self.report))

    def test_before_credential_refusal_for_native_other_app_tuple_checkpoint_and_admission(self):
        for field in ('native', 'app', 'candidate', 'attempt-type', 'checkpoint', 'admission'):
            with self.subTest(field=field), tempfile.TemporaryDirectory() as root:
                bound = binding(); bound['app'] = {'id': 4995487, 'installation': 162873149}
                candidate = copy.deepcopy(provider.ORIGINAL_TAG_FACTORY_CANDIDATE)
                checkpoint = copy.deepcopy(provider.ORIGINAL_TAG_FACTORY_CHECKPOINT)
                if field == 'native': bound['mode'] = 'native'; bound['workflow'].update(run=1, attempt=1)
                if field == 'app': bound['app']['id'] = 4882140
                if field == 'candidate': candidate['sourceSha'] = '0' * 40
                if field == 'attempt-type': candidate['attempt'] = True
                if field == 'checkpoint': checkpoint['generation'] = 3
                with self.assertRaises(provider.Refused):
                    factory = self.prepare(root, bound=bound, candidate=candidate, checkpoint=checkpoint,
                        admit=(lambda: False) if field == 'admission' else None)
                    with factory(): forbidden()
                self.assertEqual(self.credentials, []); self.assertEqual(self.opener.calls, [])

    def test_post_proof_and_all_consumer_refusals_revoke_without_replacing_first_cause(self):
        for stage in ('candidate', 'admission', 'quota', 'attempt-record', 'recheck', 'send', 'report'):
            with self.subTest(stage=stage), tempfile.TemporaryDirectory() as root:
                factory = self.prepare(root)
                self.opener.fail_cleanup = True
                original = RuntimeError(stage)
                with self.assertRaises(RuntimeError) as caught:
                    with factory(): raise original
                self.assertIs(caught.exception, original)
                self.assertEqual(self.opener.calls[-1].method, 'DELETE')
                self.assertEqual(self.report['cleanup'], 'revocation-unknown')
                self.assertEqual(self.report['firstCause'], {'stage': 'consumer', 'code': 'failed'})
                self.assertFalse(self.report['qualified'])

    def test_owned_token_validation_failure_and_cleanup_failure_are_separate(self):
        for index, key, value in [(3, 'permissions', {'contents': 'write'}),
                (3, 'permissions', {'contents': 'write', 'workflows': 'write', 'actions': 'read'}),
                (3, 'expires_at', '2099-01-01T00:00:00Z'),
                (4, 'total_count', 2), (4, 'repositories', [])]:
            with self.subTest(key=key,value=value), tempfile.TemporaryDirectory() as root:
                opener = SelectedFactoryOpener(); opener.rows[index][key] = value; opener.fail_cleanup = True
                factory = self.prepare(root, opener=opener)
                with self.assertRaises(provider.Refused):
                    with factory(): forbidden()
                self.assertEqual(self.opener.calls[-1].method, 'DELETE')
                self.assertIn(self.report['firstCause']['stage'], ['permissions', 'expiry', 'census'])
                self.assertEqual(self.report['cleanup'], 'revocation-unknown')

    def test_cleanup_or_terminal_reporting_failure_cannot_qualify_successful_body(self):
        for cleanup in (True, False):
            with self.subTest(cleanup=cleanup), tempfile.TemporaryDirectory() as root:
                def retain(row):
                    if not cleanup and row['lastPhase'] == 'terminal': raise OSError('reporter')
                factory = self.prepare(root, retain=retain); self.opener.fail_cleanup = cleanup
                with self.assertRaises(provider.Refused):
                    with factory(): pass
                self.assertFalse(self.report['qualified'])
                self.assertEqual(self.opener.calls[-1].method, 'DELETE')

    def test_insufficient_cleanup_lifetime_and_unknown_issuance_do_not_retry(self):
        for unknown in (False, True):
            with self.subTest(unknown=unknown), tempfile.TemporaryDirectory() as root:
                opener = SelectedFactoryOpener()
                if unknown:
                    def mutate(index, response):
                        if index == 3: raise OSError('inert-owned-token secret lost response')
                    opener.mutate = mutate
                else:
                    opener.rows[3]['expires_at'] = dt.datetime.fromtimestamp(time.time() + 60,
                        dt.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ')
                factory = self.prepare(root, opener=opener)
                with self.assertRaises(provider.Refused):
                    with factory(): forbidden()
                self.assertEqual(self.report['cleanup'], 'issuance-unknown' if unknown else 'revoked')
                if not unknown: self.assertEqual(self.report['firstCause'], {'stage': 'expiry', 'code': 'reserve'})
                before = len(opener.calls)
                with self.assertRaises(provider.Refused):
                    with factory(): forbidden()
                self.assertEqual(len(opener.calls), before)
                self.assertEqual(self.credentials, ['jwt'])

    def test_malformed_owned_token_never_enters_request_and_cleanup_is_unknown(self):
        with tempfile.TemporaryDirectory() as root:
            opener = SelectedFactoryOpener(); opener.rows[3]['token'] = 'inert-owned-token\r\nsecret'
            factory = self.prepare(root, opener=opener)
            with self.assertRaises(provider.Refused):
                with factory(): forbidden()
            self.assertEqual([r.method for r in opener.calls], ['GET','GET','GET','POST'])
            self.assertEqual(self.report['cleanup'], 'revocation-unknown')
            self.assertNotIn('secret', json.dumps(self.report))

    def test_moved_admission_after_proof_revokes_and_does_not_send(self):
        calls = []
        def admit(): calls.append(1); return len(calls) == 1
        with tempfile.TemporaryDirectory() as root:
            factory = self.prepare(root, admit=admit)
            with self.assertRaises(provider.Refused):
                with factory(): forbidden()
            self.assertEqual([r.method for r in self.opener.calls], ['GET','GET','GET','POST','GET','DELETE'])

    def test_inactive_native_cli_and_workflow_refuse_before_all_credential_steps(self):
        with tempfile.TemporaryDirectory() as root:
            path = pathlib.Path(root) / 'result.json'
            argv = ['publisher', '--refuse-original-tag-factory-inactive', '--result-path', str(path)]
            with patch.object(sys, 'argv', argv), patch.object(publisher, 'execute_publisher', forbidden), \
                 patch.object(provider, 'OriginalTagAuthenticationSession', forbidden), \
                 patch.object(provider, 'InertOriginalTagCredentialFactory', forbidden):
                self.assertEqual(publisher.main(), 1)
            report = json.loads(path.read_bytes())
            self.assertFalse(report['credentialAcquisitionAttempted']); self.assertFalse(report['publicationComplete'])
            self.assertEqual(report['disposition'], 'blocked-original-tag-factory-native-inactive')
            workflow = (ROOT / '.github/workflows/release-successor-publish.yml').read_text()
            guard = workflow.index('--refuse-original-tag-factory-inactive')
            for step in ('Mint the ordinary protected-journal writer token', 'Exchange nuget.org trusted publishing identity'):
                self.assertLess(guard, workflow.index(step))
            self.assertIn('if: inputs.recover_original_0103_tag_intent == true', workflow[:guard])



class CallbackPairProfileControls(unittest.TestCase):
    def test_callback_pair_profile_closed_selection_and_twelve_roles(self):
        import callback_pair_profile as pair
        manifest, vectors = pair.select(ROOT, pair.PROFILE)
        self.assertEqual(len(manifest['files']), 12)
        self.assertEqual(manifest['bundleDigest'], contract_bundle_digest(pair.PROFILE))
        self.assertEqual(vectors.name, 'callback-pair-vectors.json')
        for profile in ('factory', '', '../callback-pair-manifest.json', 'native'):
            with self.subTest(profile=profile), self.assertRaises(ValueError):
                pair.select(ROOT, profile)
        mutations = []
        default = json.loads((ROOT / 'tests/release-successor-authentication/callable-source-manifest.json').read_bytes())
        mutations.append(default)
        for key, value in (('ownerBase', '0' * 40), ('producerRevision', '0' * 40),
                           ('bundleDigest', '0' * 64), ('schema', 'unknown')):
            changed = copy.deepcopy(manifest); changed[key] = value; mutations.append(changed)
        for files in (manifest['files'][:-1], manifest['files'] + manifest['files'][:1],
                      list(reversed(manifest['files']))):
            changed = copy.deepcopy(manifest); changed['files'] = files; mutations.append(changed)
        changed = copy.deepcopy(manifest); changed['files'][0]['sha256'] = '0' * 64; mutations.append(changed)
        for changed in mutations:
            with self.subTest(mutation=changed), patch.object(pathlib.Path, 'read_bytes', return_value=json.dumps(changed).encode()):
                with self.assertRaises(ValueError): pair.select(ROOT, pair.PROFILE)
        duplicate = json.dumps(manifest)[:-1] + ', "ownerBase": "' + pair.OWNER + '"}'
        with patch.object(pathlib.Path, 'read_bytes', return_value=duplicate.encode()):
            with self.assertRaises(ValueError): pair.select(ROOT, pair.PROFILE)

    def test_callback_pair_profile_refuses_each_wrong_local_source(self):
        import callback_pair_profile as pair
        original = pathlib.Path.read_bytes
        for source in pair.GITHUB_ROLES:
            def altered(path):
                raw = original(path)
                return raw + b'\n# wrong source control\n' if path == ROOT / source else raw
            with self.subTest(source=source), patch.object(pathlib.Path, 'read_bytes', altered):
                with self.assertRaisesRegex(ValueError, 'source mismatch'):
                    callback_pair_vectors()

    def test_callback_pair_vectors_regenerate_actual_producer_owner_and_sink(self):
        import callback_pair_profile as pair
        _, path = pair.select(ROOT, pair.PROFILE)
        frozen = json.loads(path.read_bytes())
        self.assertEqual(callback_pair_vectors(), frozen)
        self.assertEqual(len(frozen['exchanges']), 13)
        self.assertEqual(frozen['finalReadback']['ordinal'], 6)
        # A genuine default-factory callback request cannot enter the paired owner.
        control = CallbackOwnerControls()
        default = protocol_vectors()
        with tempfile.TemporaryDirectory() as root:
            owner = control.prepared(root, frozen['binding'])
            with self.assertRaises(provider.Refused):
                owner.handle(control.request(default['binding'], 1, 'bind'))
            self.assertFalse(control.path.exists())

    def test_callback_pair_fixture_entry_uses_exact_fixed_vectors(self):
        import base64
        import runpy
        import callback_pair_profile as pair
        _, path = pair.select(ROOT, pair.PROFILE)
        vectors = json.loads(path.read_bytes())
        fixture = runpy.run_path(str(ROOT / 'tests/release-successor-authentication/callback-fixture.py'), run_name='callback_pair_fixture_unit')
        requests = b''.join(bytes.fromhex(row['request']) for row in vectors['exchanges'])
        expected = b''.join(bytes.fromhex(row['response']) for row in vectors['exchanges'])
        output = io.BytesIO()
        with tempfile.TemporaryDirectory() as root:
            result = pathlib.Path(root) / 'pair-result.json'
            arguments = ['callback-fixture.py', base64.b64encode(provider._auth_canonical(vectors['binding'])).decode(),
                         base64.b64encode(provider._auth_canonical(vectors['binding']['clock'])).decode(), str(result)]
            with patch.object(sys, 'argv', arguments), patch.object(sys, 'stdin', types.SimpleNamespace(buffer=io.BytesIO(requests))), \
                 patch.object(sys, 'stdout', types.SimpleNamespace(buffer=output)):
                self.assertEqual(fixture['main'](), 0)
            self.assertEqual(output.getvalue(), expected)
            projection = json.loads(result.read_bytes())['authenticationSession']
            self.assertEqual({key: projection[key] for key in vectors['finalReadback']}, vectors['finalReadback'])


def load_tests(loader, tests, pattern):
    # Keep the pure ADR-0093 construction inside the existing hosted test entry.
    path = pathlib.Path(__file__).with_name('domain-contract-tests.py')
    spec = importlib.util.spec_from_file_location('authentication_domain_contract_tests', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    tests.addTests(loader.loadTestsFromModule(module))
    slot_path = pathlib.Path(__file__).with_name('slot-callback-pair-tests.py')
    slot_spec = importlib.util.spec_from_file_location('authentication_slot_callback_pair_tests', slot_path)
    slot_module = importlib.util.module_from_spec(slot_spec)
    slot_spec.loader.exec_module(slot_module)
    tests.addTests(loader.loadTestsFromModule(slot_module))
    authority_path = pathlib.Path(__file__).with_name('domain-authority-tests.py')
    authority_spec = importlib.util.spec_from_file_location('authentication_domain_authority_tests', authority_path)
    authority_module = importlib.util.module_from_spec(authority_spec)
    authority_spec.loader.exec_module(authority_module)
    tests.addTests(loader.loadTestsFromModule(authority_module))
    closed_path = pathlib.Path(__file__).with_name('closed-source-tests.py')
    closed_spec = importlib.util.spec_from_file_location('authentication_closed_source_tests', closed_path)
    closed_module = importlib.util.module_from_spec(closed_spec)
    closed_spec.loader.exec_module(closed_module)
    tests.addTests(loader.loadTestsFromModule(closed_module))
    return tests




# These exact frozen-profile proof legs require their original source closure.
# Their files, vectors, role lists and byte pins remain unchanged historical proof.
# Current-source refusal replaces their activation claim; every other test still runs.
HISTORICAL_AUTHENTICATION_TESTS = frozenset({
    'CallbackOwnerControls.test_callback_decoder_refuses_overlap_while_validation_or_sink_is_blocked',
    'CallbackOwnerControls.test_callback_finalization_preserves_the_original_projection',
    'CallbackOwnerControls.test_callback_finalization_rejects_equal_python_values_with_different_json_types',
    'CallbackOwnerControls.test_callback_frozen_vectors_match_current_manifest_and_actual_sink',
    'CallbackOwnerControls.test_callback_late_retention_keeps_durable_projection_and_cannot_retry',
    'CallbackOwnerControls.test_callback_manifest_recipe_sorts_by_ascii_repository_path_key',
    'CallbackOwnerControls.test_callback_native_refuses_before_clock_and_domain_rejects_secret_canary',
    'CallbackOwnerControls.test_callback_owner_actual_validator_and_sink_six_phase_roundtrip',
    'CallbackOwnerControls.test_callback_owner_validation_never_persists_and_failed_attempt_cannot_retry',
    'CallbackOwnerControls.test_callback_queued_entry_at_work_refuses_sink_and_wrong_receipt_retains_obligation',
    'CallbackOwnerControls.test_callback_request_decoder_bounds_fragments_pipeline_and_partial_write',
    'CallbackOwnerControls.test_callback_terminal_validation_finishing_at_work_cannot_retain',
    'CallbackPairProfileControls.test_callback_pair_fixture_entry_uses_exact_fixed_vectors',
    'CallbackPairProfileControls.test_callback_pair_profile_closed_selection_and_twelve_roles',
    'CallbackPairProfileControls.test_callback_pair_vectors_regenerate_actual_producer_owner_and_sink',
    'ClosedSourceControls.test_public_profile_constructor_refuses_all_effects_and_injection',
    'ClosedSourceControls.test_source_import_closure_is_closed',
    'ContractTests.test_source_bound_is_pinned_to_reviewed_provider',
    'JointCopyLedgerControls.test_python_capacity_matches_protected_actual_runtime_without_logical_merge',
    'JointCopyLedgerControls.test_python_capacity_source_mutations_refuse',
    'JointCopyLedgerControls.test_recorded_python_roles_match_actual_controlled_journey',
    'JointCopyLedgerControls.test_selected_python_graphs_match_actual_cuts_without_cross_cut_sum',
    'ProtocolControls.test_blocked_older_persistence_serializes_before_terminal_without_late_overwrite',
    'ProtocolControls.test_fresh_report_cannot_reset_either_durable_possible_effect',
    'ProtocolControls.test_inert_consumer_accepts_exact_producer_start_once',
    'ProtocolControls.test_inert_consumer_refuses_changed_or_ambiguous_start_before_callbacks',
    'ProtocolControls.test_late_lower_ordinal_cannot_replace_retained_terminal',
    'ProtocolControls.test_maximum_legal_binding_and_terminal_fit_without_truncation',
    'ProtocolControls.test_monotone_assertions_success_flags_and_zero_publication_cannot_be_forged',
    'ProtocolControls.test_pinned_existing_wire_contract_labels_only_inert_source_coverage',
    'ProtocolControls.test_selected_future_identity_is_distinct_from_synthetic_provider_fixture',
    'SelectedOriginalFactoryControls.test_selected_factory_actual_proof_one_exact_send_and_lexical_revocation',
    'SlotBackingCapacityControls.test_actual_cut_allocation_and_original_journey',
    'SlotBackingCapacityControls.test_original_lifetime_and_failure_controls_with_observer',
    'SlotBackingCapacityControls.test_receipt_refuses_role_alias_substitution_length_and_capacity_forgery',
    'SlotCallbackPairControls.test_historical_v1_binding_keeps_its_exact_child_and_callback_wire_bytes',
    'SlotCallbackPairControls.test_slot_callback_entry_cli_emits_closed_bounded_synthetic_evidence',
    'SlotCallbackPairControls.test_slot_callback_entry_refuses_incomplete_short_write_flush_and_backend_cuts',
    'SlotCallbackPairControls.test_slot_callback_pair_manifest_closes_sixteen_roles_and_sources',
    'SlotCallbackPairControls.test_slot_callback_pair_regenerates_real_six_slot_journey_without_eof',
    'SlotCallbackPairControls.test_slot_callback_pair_runtime_import_closure_is_complete',
    'SlotCopyLifetimeControls.test_selected_graph_identities_and_emission_cuts',
    'SlotCopyLifetimeControls.test_terminal_failures_keep_committed_stores_and_original_attempt',
    'SlotCopyLifetimeControls.test_terminal_readback_distinct_objects_and_alias_transfer',
})


class CurrentSourceSuspensionControls(unittest.TestCase):
    def test_historical_manifests_remain_byte_exact_and_do_not_qualify_current_source(self):
        import hashlib
        expected = {'callable-source-manifest.json': '56945728357b8742403604faa7215c86aa144970319831181dbde2c9e55aa8f1', 'callback-pair-manifest.json': '1c26f75494147099988f626346ed2b6f30bf70959bb9853991efd6fee82d0daf', 'slot-callback-pair-manifest.json': 'cea080a7e9240a3cdec1674f5f100233b97dc3fbedaabc3f6848b9e4e5767254', 'closed-source-manifest.json': '4745d629f15a50d5bb98b09015e49242717a68e384112139f752dca5fb695f53', 'closed-source-profile.json': '5c13399a71a782e713e445b3da9a93687b00347ab02bf3112bddce3dc803b5ff'}
        for name,digest in expected.items():
            raw=(pathlib.Path(__file__).parent/name).read_bytes()
            self.assertEqual(hashlib.sha256(raw).hexdigest(),digest)
        for name,paths in (
            ('callable-source-manifest.json',{'scripts/release-successor-publish.py','scripts/release_successor_provider.py'}),
            ('callback-pair-manifest.json',{'scripts/release-successor-publish.py','scripts/release_successor_provider.py'}),
            ('slot-callback-pair-manifest.json',{'scripts/release_successor_provider.py'}),
            ('closed-source-manifest.json',{'scripts/release_successor_provider.py'})):
            rows=json.loads((pathlib.Path(__file__).parent/name).read_bytes())['files']
            changed={row['path'] for row in rows if row.get('repository','FS-GG/.github')=='FS-GG/.github'
                and hashlib.sha256((ROOT/row['path']).read_bytes()).hexdigest()!=row['sha256']}
            self.assertEqual(changed,paths)

    def test_closed_historical_profiles_refuse_exact_changed_source(self):
        import callback_pair_profile as pair
        for profile,path in ((pair.PROFILE,'scripts/release-successor-publish.py'),
                             (pair.SLOT_PROFILE,'scripts/release_successor_provider.py')):
            with self.subTest(profile=profile),self.assertRaisesRegex(ValueError,'source mismatch: '+path):
                pair.select(ROOT,profile)
        import release_successor_closed_callback as closed
        raw=(pathlib.Path(__file__).parent/'closed-source-profile.json').read_bytes().rstrip(b'\n')
        with self.assertRaisesRegex(closed.Refused,'closed source refused'):
            closed._verify_sources()
        with self.assertRaisesRegex(closed.Refused,'closed profile refused'):
            closed.validate_profile(raw)
        with self.assertRaisesRegex(provider.Refused,'factory unavailable'):
            provider.approved_original_tag_transport()

    def test_original_transport_refuses_before_any_request_without_shared_reader(self):
        from release_successor_execution import Effect
        identity={'appId':4995487,'installationId':162873149,'repositoryId':1269292704,
                  'permissions':{'contents':'write','workflows':'write'},'expiresAtUnix':int(time.time())+60}
        token=provider.VerifiedOriginalTagTransport(provider._ORIGINAL_TAG_TRANSPORT_SEAL,'inert',identity)
        with patch.object(provider.urllib.request,'build_opener',side_effect=AssertionError('no request')), \
             self.assertRaisesRegex(provider.Refused,'maintained suspension reader'):
            token.dispatch(Effect('tag',provider.ORIGINAL_TAG_SOURCE,provider.ORIGINAL_TAG_CONTENT),100,lambda:0)


def current_source_suspension_suite():
    """Closed current suite; no failure-driven filtering or new proof profile."""
    full=unittest.defaultTestLoader.loadTestsFromModule(sys.modules[__name__])
    def cases(suite):
        for test in suite:
            if isinstance(test,unittest.TestSuite): yield from cases(test)
            else: yield test
    rows=list(cases(full))
    keys={type(test).__name__+'.'+test._testMethodName for test in rows}
    missing=HISTORICAL_AUTHENTICATION_TESTS-keys
    if missing: raise AssertionError('historical suite inventory drift: '+repr(sorted(missing)))
    selected=[test for test in rows if type(test).__name__+'.'+test._testMethodName not in HISTORICAL_AUTHENTICATION_TESTS]
    print('Current source authentication: '+str(len(selected))+' tests; '+str(len(HISTORICAL_AUTHENTICATION_TESTS))+
          ' exact frozen-profile legs retained as historical proof, not current qualification')
    return unittest.TestSuite(selected)

if __name__ == '__main__':
    if sys.argv[1:] == ['--current-source-suspension']:
        result=unittest.TextTestRunner().run(current_source_suspension_suite())
        raise SystemExit(0 if result.wasSuccessful() else 1)
    unittest.main()

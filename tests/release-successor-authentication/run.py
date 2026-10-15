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


def protocol_vectors():
    import hashlib
    with tempfile.TemporaryDirectory() as root:
        channel = RetainedChannel(root)
        # Deterministic fixture identities/timestamps, never an observed invocation.
        channel.binding['source']['contract'] = hashlib.sha256((ROOT / 'scripts/release_successor_provider.py').read_bytes()).hexdigest()
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



if __name__ == '__main__': unittest.main()

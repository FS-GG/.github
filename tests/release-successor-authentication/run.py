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


class FactoryControls(unittest.TestCase):
    def run_factory(self, opener):
        report = {}
        with patch.object(provider.LiveProvider, 'dispatch', forbidden), \
             patch.object(provider.VerifiedOriginalTagTransport, 'dispatch', forbidden), \
             patch.object(provider.GitHubAPI, '_request', forbidden), \
             patch.object(journal.ProtectedReleaseJournal, '__init__', forbidden), \
             patch.object(journal.ProtectedReleaseJournal, 'compare_and_swap', forbidden), \
             patch.object(subprocess, 'run', forbidden):
            session = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123, opener=opener)
            transport = session.qualify(report)
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
        self.assertIsInstance(transport, provider.VerifiedOriginalTagTransport)
        self.assertEqual(transport.identity()['repositoryId'], 1269292704)
        self.assertIsNone(transport._token)
        with self.assertRaises(provider.Refused): transport.dispatch(None)
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
                self.assertIsNone(transport)
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
                self.assertIsNone(transport)
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
                self.assertIsNone(transport)
                self.assertEqual(len(opener.calls), 1)

    def test_endpoint_budget_and_cleanup_failure_are_closed(self):
        opener = Opener(); opener.fail_cleanup = True
        transport, report = self.run_factory(opener)
        self.assertIsNone(transport)
        self.assertEqual(report['cleanup'], 'revocation-failed')
        session = provider.OriginalTagAuthenticationSession('inert-jwt', 4882140, 123, opener=Opener())
        for method, path in [('POST', '/repos/FS-GG/.github/git/refs'), ('GET', '/app?x=1'),
                             ('DELETE', '/installation/token')]:
            with self.assertRaises(provider.Refused): session._request(method, path, 'inert-jwt')
        self.assertEqual(session._opener.calls, [])
        session._gets = 8
        with self.assertRaises(provider.Refused): session._request('GET', '/app', 'inert-jwt')
        session.deadline = 0
        with self.assertRaises(provider.Refused): session._request('GET', '/app', 'inert-jwt')

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


if __name__ == '__main__': unittest.main()

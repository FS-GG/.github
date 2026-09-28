#!/usr/bin/env python3
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


observer = load('receiver_observer', 'scripts/gs2-09-7-receiver-observer.py')
host = load('receiver_host', 'scripts/gs2-09-7-receiver-observer-host.py')
REPO = {'id': observer.REPO_ID, 'node_id': observer.NODE_ID, 'full_name': observer.FULL_NAME}
TOKEN = 't' * 35


def raw(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':')).encode()


def mint_value():
    return {'token': TOKEN, 'repository_selection': 'selected',
            'permissions': {'contents': 'read', 'metadata': 'read'},
            'repositories': [REPO],
            'expires_at': (dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=50)).strftime('%Y-%m-%dT%H:%M:%SZ')}


class FakeApi:
    def __init__(self, token, override=None):
        assert token == TOKEN
        self.override = override or {}
        self.calls = []
        self.payload = {
            '/installation': {'id': observer.INSTALLATION_ID, 'app_id': observer.APP_ID},
            '/installation/repositories?per_page=100': {'total_count': 1, 'repositories': [REPO]},
            '/repos/' + observer.FULL_NAME: REPO,
            '/repos/' + observer.FULL_NAME + '/git/matching-refs/heads/gs2-09-7/': [
                {'ref': 'refs/heads/gs2-09-7/receiver', 'object': {'sha': 'a' * 40}}],
        }

    def get(self, path):
        self.calls.append(path)
        value = self.override.get(path, self.payload[path])
        if isinstance(value, Exception):
            raise value
        return value, raw(value)


class ObserverTests(unittest.TestCase):
    def test_exact_two_pass_read_only_observation(self):
        api = FakeApi(TOKEN)
        report = observer.probe(raw(mint_value()), lambda token: api)
        self.assertEqual(report['status'], 'observed-no-authority')
        self.assertTrue(report['complete'])
        self.assertFalse(report['copyAuthorized'])
        self.assertFalse(report['refMutationAuthorized'])
        self.assertEqual(len(api.calls), 10)
        self.assertEqual(api.calls.count('/installation'), 2)
        self.assertEqual(report['passes'][0], report['passes'][1])

    def test_missing_installation_endpoint_refuses(self):
        api = FakeApi(TOKEN, {'/installation': ValueError('GET /installation returned HTTP 404')})
        with self.assertRaisesRegex(ValueError, '404'):
            observer.probe(raw(mint_value()), lambda token: api)
        self.assertEqual(api.calls, ['/installation'])

    def test_foreign_or_write_mint_refuses_before_get(self):
        for change in ({'permissions': {'contents': 'write', 'metadata': 'read'}},
                       {'repositories': [{**REPO, 'id': 123}]},
                       {'repository_selection': 'all'}):
            with self.subTest(change=change):
                value = {**mint_value(), **change}
                with self.assertRaises(ValueError):
                    observer.probe(raw(value), FakeApi)

    def test_ref_namespace_and_repository_drift_refuse(self):
        path = '/repos/' + observer.FULL_NAME + '/git/matching-refs/heads/gs2-09-7/'
        for override in ({path: [{'ref': 'refs/heads/main', 'object': {'sha': 'a' * 40}}]},
                         {'/repos/' + observer.FULL_NAME: {**REPO, 'id': 123}}):
            with self.subTest(override=override):
                with self.assertRaises(ValueError):
                    observer.probe(raw(mint_value()), lambda token: FakeApi(token, override))

    def test_duplicate_mint_keys_refuse(self):
        with self.assertRaisesRegex(ValueError, 'duplicate JSON key'):
            observer.validate_mint(b'{"token":"a","token":"b"}')

    def test_denied_receiver_gets_refuse_without_followup(self):
        for code in (401, 403, 404):
            api = FakeApi(TOKEN, {'/installation': ValueError('GET returned HTTP ' + str(code))})
            with self.subTest(code=code), self.assertRaisesRegex(ValueError, str(code)):
                observer.probe(raw(mint_value()), lambda token: api)
            self.assertEqual(api.calls, ['/installation'])


class HostTests(unittest.TestCase):
    def test_container_arguments_and_environment_have_no_bearer_or_host_credentials(self):
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = raw({'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                                           'status': 'observed-no-authority', 'complete': True,
                                           'repositoryId': host.REPO_ID,
                                           'installationId': host.INSTALLATION_ID,
                                           'mint': {'mintResponseSha256': 'a' * 64,
                                                    'tokenSha256': 'b' * 64, 'expiresAt': 'later'},
                                           'passes': [{}, {}], 'copyAuthorized': False,
                                           'refMutationAuthorized': False})
            report = host.run_container(raw(mint_value()))
        self.assertTrue(report['complete'])
        args, kwargs = run.call_args
        argv = args[0]
        self.assertEqual(argv[0:2], ['docker', 'run'])
        self.assertIn('--read-only', argv)
        self.assertIn('--cap-drop=ALL', argv)
        self.assertIn(host.IMAGE, argv)
        self.assertNotIn(TOKEN, ' '.join(argv))
        self.assertNotIn('docker.sock', ' '.join(argv))
        self.assertNotIn('FSGG_DISPATCH_APP_PRIVATE_KEY', str(kwargs['env']))
        self.assertEqual(kwargs['input'], raw(mint_value()))

    def test_exact_read_only_mint_request_and_revocation(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'private'
            app = {'id': host.APP_ID, 'slug': host.APP_SLUG,
                   'permissions': {'contents': 'write', 'metadata': 'read'}}
            installation = {'id': host.INSTALLATION_ID, 'app_id': host.APP_ID,
                            'app_slug': host.APP_SLUG, 'account': {'login': 'FS-GG'},
                            'suspended_at': None,
                            'permissions': {'contents': 'write', 'metadata': 'read'}}
            calls = []

            def request(method, path, bearer, body=None):
                calls.append((method, path, body))
                if path == '/app':
                    return app, raw(app)
                if path == '/app/installations/' + str(host.INSTALLATION_ID):
                    return installation, raw(installation)
                value = mint_value()
                return value, raw(value)

            observed = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                        'status': 'observed-no-authority', 'complete': True,
                        'copyAuthorized': False, 'refMutationAuthorized': False,
                        'mint': {'mintResponseSha256': hashlib.sha256(raw(mint_value())).hexdigest(),
                                 'tokenSha256': hashlib.sha256(TOKEN.encode()).hexdigest()}}
            env = {'GITHUB_REPOSITORY': 'FS-GG/.github', 'GITHUB_REF': 'refs/heads/main',
                   'GITHUB_SHA': 'a' * 40, 'FSGG_PROTECTED_SHA': 'a' * 40,
                   'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1'}
            with patch.dict(os.environ, env), patch.object(host.mint, 'app_jwt', return_value='jwt'), \
                 patch.object(host, 'request_json', side_effect=request), \
                 patch.object(host, 'revoke_token') as revoke, \
                 patch.object(host, 'run_container', return_value=observed):
                result = host.run(root, host.APP_ID, 'PRIVATE KEY')
            self.assertEqual(calls[2][0], 'POST')
            self.assertEqual(calls[2][2], {'repository_ids': [host.REPO_ID],
                                           'permissions': {'contents': 'read'}})
            revoke.assert_called_once_with(TOKEN)
            self.assertFalse((root / 'token.private').exists())
            self.assertFalse((root / 'revocation-pending.json').exists())
            self.assertEqual(result['status'], 'observed-no-authority')
            self.assertNotIn(TOKEN, (root / 'report.json').read_text())

    def test_revoke_failure_retains_private_retry_custody_and_sanitized_pending(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'private'
            root.mkdir()
            (root / 'token.private').write_text(TOKEN)
            (root / 'revocation-pending.json').write_text(json.dumps({'status': 'revocation-pending',
                                                                       'revoked': False}))
            with patch.object(host, 'revoke_token', side_effect=ValueError('HTTP 403')):
                with self.assertRaises(ValueError):
                    host.cleanup(root)
            self.assertEqual((root / 'token.private').read_text(), TOKEN)
            self.assertFalse((root / 'cleanup.json').exists())
            with patch.object(host, 'revoke_token'):
                host.cleanup(root)
            self.assertFalse((root / 'token.private').exists())
            self.assertEqual(json.loads((root / 'cleanup.json').read_text())['status'], 'refused')

    def test_probe_revocation_failure_keeps_sanitized_observation_and_retry_token(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'private'
            app = {'id': host.APP_ID, 'slug': host.APP_SLUG,
                   'permissions': {'contents': 'read', 'metadata': 'read'}}
            installation = {'id': host.INSTALLATION_ID, 'app_id': host.APP_ID,
                            'app_slug': host.APP_SLUG, 'account': {'login': 'FS-GG'},
                            'suspended_at': None,
                            'permissions': {'contents': 'read', 'metadata': 'read'}}
            values = iter((app, installation, mint_value()))
            def request(*_args, **_kwargs):
                value = next(values)
                return value, raw(value)
            observed = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                        'status': 'observed-no-authority', 'complete': True,
                        'copyAuthorized': False, 'refMutationAuthorized': False,
                        'mint': {'mintResponseSha256': hashlib.sha256(raw(mint_value())).hexdigest(),
                                 'tokenSha256': hashlib.sha256(TOKEN.encode()).hexdigest()}}
            env = {'GITHUB_REPOSITORY': 'FS-GG/.github', 'GITHUB_REF': 'refs/heads/main',
                   'GITHUB_SHA': 'a' * 40, 'FSGG_PROTECTED_SHA': 'a' * 40,
                   'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1'}
            with patch.dict(os.environ, env), patch.object(host.mint, 'app_jwt', return_value='jwt'), \
                 patch.object(host, 'request_json', side_effect=request), \
                 patch.object(host, 'revoke_token', side_effect=ValueError('HTTP 403')), \
                 patch.object(host, 'run_container', return_value=observed):
                with self.assertRaisesRegex(ValueError, 'retry custody'):
                    host.run(root, host.APP_ID, 'PRIVATE KEY')
            pending = (root / 'revocation-pending.json').read_text()
            self.assertTrue((root / 'token.private').is_file())
            self.assertNotIn(TOKEN, pending)
            self.assertEqual(json.loads(pending)['status'], 'revocation-pending')
            self.assertFalse((root / 'report.json').exists())


if __name__ == '__main__':
    unittest.main()

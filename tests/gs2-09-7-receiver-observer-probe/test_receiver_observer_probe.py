#!/usr/bin/env python3
import datetime as dt
import hashlib
import importlib.util
import io
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


def pass_value():
    return {'repositoriesSha256': '1' * 64,
            'repositoriesProjectionSha256': '4' * 64,
            'repositorySha256': '2' * 64,
            'repositoryRepeatSha256': '2' * 64,
            'repositoryProjectionSha256': '5' * 64,
            'receiverRefsSha256': '3' * 64,
            'receiverRefCount': 1}


def observed_report(mint=None):
    mint = mint or mint_value()
    observation = pass_value()
    return {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
            'status': 'observed-no-authority', 'complete': True,
            'repositoryId': host.REPO_ID, 'installationId': host.INSTALLATION_ID,
            'mint': {'mintResponseSha256': hashlib.sha256(raw(mint)).hexdigest(),
                     'tokenSha256': hashlib.sha256(TOKEN.encode()).hexdigest(),
                     'expiresAt': mint['expires_at']},
            'passes': [observation, dict(observation)],
            'copyAuthorized': False, 'refMutationAuthorized': False}


class FakeApi:
    def __init__(self, token, override=None):
        assert token == TOKEN
        self.override = override or {}
        self.calls = []
        self.payload = {
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
        self.assertEqual(len(api.calls), 8)
        self.assertEqual(api.calls.count('/installation/repositories?per_page=100'), 2)
        self.assertNotIn('/installation', api.calls)
        self.assertEqual(report['passes'][0], report['passes'][1])

    def test_temporary_clone_tokens_may_rotate_while_raw_digests_remain_independent(self):
        class RotatingCloneTokenApi(FakeApi):
            def get(self, path):
                value, _ = super().get(path)
                sequence = len(self.calls)
                token = 'temporary-clone-token-' + str(sequence).zfill(8)
                if path == '/installation/repositories?per_page=100':
                    value = {**value, 'repositories': [
                        {**value['repositories'][0], 'temp_clone_token': token}]}
                elif path == '/repos/' + observer.FULL_NAME:
                    value = {**value, 'temp_clone_token': token}
                return value, raw(value)

        report = observer.probe(raw(mint_value()), RotatingCloneTokenApi)
        first, second = report['passes']
        self.assertNotEqual(first['repositoriesSha256'], second['repositoriesSha256'])
        self.assertNotEqual(first['repositorySha256'], first['repositoryRepeatSha256'])
        self.assertNotEqual(first['repositorySha256'], second['repositorySha256'])
        self.assertEqual(first['repositoriesProjectionSha256'],
                         second['repositoriesProjectionSha256'])
        self.assertEqual(first['repositoryProjectionSha256'],
                         second['repositoryProjectionSha256'])
        self.assertNotIn('temp_clone_token', json.dumps(report))

    def test_malformed_temporary_clone_token_refuses_at_exact_stage(self):
        path = '/repos/' + observer.FULL_NAME
        api = FakeApi(TOKEN, {path: {**REPO, 'temp_clone_token': 7}})
        with self.assertRaises(observer.ProbeRefusal) as result:
            observer.probe(raw(mint_value()), lambda token: api)
        self.assertEqual(('validation-error', 'pass-1-repository-identity'),
                         (result.exception.code, result.exception.stage))

    def test_repository_repeat_drift_has_only_a_closed_classification(self):
        cases = [
            ({**REPO, 'archived': True}, {**REPO, 'archived': False}, 'other'),
            ({**REPO, 'permissions': {'pull': True}},
             {**REPO, 'permissions': {'pull': False}}, 'permission'),
            ({**REPO, 'updated_at': '2026-09-28T10:00:00Z'},
             {**REPO, 'updated_at': '2026-09-28T10:00:01Z'}, 'counter-time'),
            ({**REPO, 'archived': True}, REPO, 'key-set'),
            (REPO, {**REPO, 'id': 7}, 'identity'),
        ]
        path = '/repos/' + observer.FULL_NAME
        for first, repeat, drift_class in cases:
            class RepeatDriftApi(FakeApi):
                def get(self, requested):
                    index = len(self.calls)
                    value, encoded = super().get(requested)
                    if requested == path:
                        value = first if index == 1 else repeat
                        encoded = raw(value)
                    return value, encoded
            with self.subTest(drift_class=drift_class), \
                 self.assertRaises(observer.ProbeRefusal) as result:
                observer.probe(raw(mint_value()), RepeatDriftApi)
            self.assertEqual(('pass-1-repository-repeat', drift_class),
                             (result.exception.stage, result.exception.drift_class))

    def test_missing_installation_repositories_endpoint_refuses(self):
        path = '/installation/repositories?per_page=100'
        api = FakeApi(TOKEN, {path: ValueError('GET repositories returned HTTP 404')})
        with self.assertRaises(observer.ProbeRefusal) as result:
            observer.probe(raw(mint_value()), lambda token: api)
        self.assertEqual(('validation-error', 'pass-1-repository-roster', None),
                         (result.exception.code, result.exception.stage,
                          result.exception.http_status))
        self.assertEqual(api.calls, [path])

    def test_foreign_or_write_mint_refuses_before_get(self):
        for change in ({'permissions': {'contents': 'write', 'metadata': 'read'}},
                       {'repositories': [{**REPO, 'id': 123}]},
                       {'repository_selection': 'all'}):
            with self.subTest(change=change):
                value = {**mint_value(), **change}
                with self.assertRaises(ValueError):
                    observer.probe(raw(value), FakeApi)

    def test_implicit_or_explicit_metadata_read_are_the_only_narrow_grants(self):
        for permissions in ({'contents': 'read'},
                            {'contents': 'read', 'metadata': 'read'}):
            with self.subTest(permissions=permissions):
                value = {**mint_value(), 'permissions': permissions}
                report = observer.probe(raw(value), FakeApi)
                self.assertTrue(report['complete'])

        for permissions in ({'contents': 'read', 'issues': 'read'},
                            {'contents': 'read', 'metadata': 'read', 'issues': 'read'},
                            {'metadata': 'read'}):
            with self.subTest(permissions=permissions), self.assertRaises(ValueError):
                observer.probe(raw({**mint_value(), 'permissions': permissions}), FakeApi)

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
            path = '/installation/repositories?per_page=100'
            api = FakeApi(TOKEN, {path: observer.ProbeRefusal(
                'github-http-error', 'bearer-read', code)})
            with self.subTest(code=code), self.assertRaises(observer.ProbeRefusal) as result:
                observer.probe(raw(mint_value()), lambda token: api)
            self.assertEqual(code, result.exception.http_status)
            self.assertEqual(api.calls, [path])

    def test_each_validation_stage_is_closed_and_pass_specific(self):
        cases = [
            (-1, {**mint_value(), 'permissions': {'contents': 'write'}}, 'mint-proof'),
            (0, {'total_count': 0, 'repositories': []}, 'pass-1-repository-roster'),
            (1, {**REPO, 'id': 9}, 'pass-1-repository-identity'),
            (2, [{'ref': 'refs/heads/main', 'object': {'sha': 'a' * 40}}],
             'pass-1-receiver-refs'),
            (3, {**REPO, 'id': 9}, 'pass-1-repository-repeat'),
            (4, {'total_count': 0, 'repositories': []}, 'pass-2-repository-roster'),
            (5, {**REPO, 'id': 9}, 'pass-2-repository-identity'),
            (6, [{'ref': 'refs/heads/main', 'object': {'sha': 'a' * 40}}],
             'pass-2-receiver-refs'),
            (7, {**REPO, 'id': 9}, 'pass-2-repository-repeat'),
        ]
        for target, altered, stage in cases:
            with self.subTest(stage=stage):
                if target == -1:
                    invoke = lambda: observer.probe(raw(altered), FakeApi)
                else:
                    class AlteredApi(FakeApi):
                        def get(self, path):
                            index = len(self.calls)
                            value, original = super().get(path)
                            return ((altered, raw(altered)) if index == target
                                    else (value, original))
                    invoke = lambda: observer.probe(raw(mint_value()), AlteredApi)
                with self.assertRaises(observer.ProbeRefusal) as result:
                    invoke()
                self.assertEqual('validation-error', result.exception.code)
                self.assertEqual(stage, result.exception.stage)
                self.assertIsNone(result.exception.http_status)

        class ChangedRawSecondPass(FakeApi):
            def get(self, path):
                index = len(self.calls)
                value, encoded = super().get(path)
                return value, encoded + b' ' if index == 4 else encoded
        report = observer.probe(raw(mint_value()), ChangedRawSecondPass)
        self.assertNotEqual(report['passes'][0]['repositoriesSha256'],
                            report['passes'][1]['repositoriesSha256'])
        self.assertEqual(report['passes'][0]['repositoriesProjectionSha256'],
                         report['passes'][1]['repositoriesProjectionSha256'])

        class ChangedStableSecondPass(FakeApi):
            def get(self, path):
                index = len(self.calls)
                value, _ = super().get(path)
                if index == 4:
                    value = {**value, 'repositories': [
                        {**value['repositories'][0], 'archived': True}]}
                return value, raw(value)
        with self.assertRaises(observer.ProbeRefusal) as result:
            observer.probe(raw(mint_value()), ChangedStableSecondPass)
        self.assertEqual('cross-pass', result.exception.stage)

    def test_main_reduces_provider_failure_to_bounded_refusal(self):
        stdin = type('Input', (), {'buffer': io.BytesIO(raw(mint_value()))})()
        stdout = io.StringIO()
        with patch.object(observer.sys, 'stdin', stdin), patch.object(observer.sys, 'stdout', stdout), \
             patch.object(observer, 'probe', side_effect=observer.ProbeRefusal(
                 'github-http-error', 'bearer-read', 403)):
            self.assertEqual(observer.main(), 1)
        report = json.loads(stdout.getvalue())
        self.assertEqual(report['refusal'], {'code': 'github-http-error',
                                             'stage': 'bearer-read', 'httpStatus': 403,
                                             'driftClass': None})
        self.assertNotIn('reason', report)
        self.assertNotIn(TOKEN, stdout.getvalue())

    def test_main_projects_validation_stage_without_provider_bytes(self):
        path = '/installation/repositories?per_page=100'
        api = FakeApi(TOKEN, {path: {'total_count': 0,
                                    'repositories': [{'private_detail': TOKEN}]}})
        stdin = type('Input', (), {'buffer': io.BytesIO(raw(mint_value()))})()
        stdout = io.StringIO()
        original_probe = observer.probe
        with patch.object(observer.sys, 'stdin', stdin), patch.object(observer.sys, 'stdout', stdout), \
             patch.object(observer, 'probe',
                          side_effect=lambda data: original_probe(data, lambda _: api)):
            self.assertEqual(observer.main(), 1)
        report = json.loads(stdout.getvalue())
        self.assertEqual({'code': 'validation-error',
                          'stage': 'pass-1-repository-roster', 'httpStatus': None,
                          'driftClass': None},
                         report['refusal'])
        self.assertEqual({'schema', 'status', 'complete', 'refusal',
                          'copyAuthorized', 'refMutationAuthorized'}, set(report))
        self.assertNotIn(TOKEN, stdout.getvalue())
        self.assertNotIn('private_detail', stdout.getvalue())


class HostTests(unittest.TestCase):
    def test_host_accepts_only_implicit_or_explicit_metadata_read(self):
        for permissions in ({'contents': 'read'},
                            {'contents': 'read', 'metadata': 'read'}):
            with self.subTest(permissions=permissions):
                host.validate_mint({**mint_value(), 'permissions': permissions})

        for permissions in ({'contents': 'read', 'issues': 'read'},
                            {'contents': 'read', 'metadata': 'read', 'issues': 'read'}):
            with self.subTest(permissions=permissions), self.assertRaises(ValueError):
                host.validate_mint({**mint_value(), 'permissions': permissions})

    def test_container_arguments_and_environment_have_no_bearer_or_host_credentials(self):
        minted = mint_value()
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = raw(observed_report(minted))
            report = host.run_container(raw(minted))
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
        self.assertEqual(kwargs['input'], raw(minted))

    def test_host_keeps_raw_digest_custody_independent_of_stable_projection(self):
        minted = mint_value()
        report = observed_report(minted)
        report['passes'][1]['repositoriesSha256'] = '6' * 64
        report['passes'][1]['repositorySha256'] = '7' * 64
        report['passes'][1]['repositoryRepeatSha256'] = '8' * 64
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = raw(report)
            self.assertEqual(host.run_container(raw(minted)), report)

    def test_host_rejects_stable_projection_drift(self):
        minted = mint_value()
        report = observed_report(minted)
        report['passes'][1]['repositoryProjectionSha256'] = '6' * 64
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 0
            run.return_value.stdout = raw(report)
            with self.assertRaisesRegex(ValueError, 'unexpected data or drift'):
                host.run_container(raw(minted))

    def test_container_retains_only_validated_sanitized_refusal(self):
        minted = mint_value()
        refusal = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                   'status': 'refused', 'complete': False,
                   'refusal': {'code': 'github-http-error', 'stage': 'bearer-read',
                               'httpStatus': 403, 'driftClass': None},
                   'copyAuthorized': False, 'refMutationAuthorized': False}
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 1
            run.return_value.stdout = raw(refusal)
            run.return_value.stderr = b'provider detail that must not be retained'
            self.assertEqual(host.run_container(raw(minted)), refusal)

    def test_host_accepts_only_closed_validation_stages(self):
        minted = mint_value()
        self.assertEqual(observer.VALIDATION_STAGES, host.VALIDATION_STAGES)
        for stage in sorted(host.VALIDATION_STAGES):
            refusal = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                       'status': 'refused', 'complete': False,
                       'refusal': {'code': 'validation-error', 'stage': stage,
                                   'httpStatus': None, 'driftClass': None},
                       'copyAuthorized': False, 'refMutationAuthorized': False}
            with self.subTest(stage=stage), patch.object(host.subprocess, 'run') as run:
                run.return_value.returncode = 1
                run.return_value.stdout = raw(refusal)
                self.assertEqual(refusal, host.run_container(raw(minted)))
        refusal['refusal']['stage'] = 'provider-detail-or-secret'
        with patch.object(host.subprocess, 'run') as run:
            run.return_value.returncode = 1
            run.return_value.stdout = raw(refusal)
            with self.assertRaisesRegex(ValueError, 'sanitized refusal'):
                host.run_container(raw(minted))

    def test_host_accepts_only_closed_repeat_drift_classifications(self):
        minted = mint_value()
        for drift_class in sorted(host.DRIFT_CLASSES):
            refusal = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                       'status': 'refused', 'complete': False,
                       'refusal': {'code': 'validation-error',
                                   'stage': 'pass-1-repository-repeat',
                                   'httpStatus': None, 'driftClass': drift_class},
                       'copyAuthorized': False, 'refMutationAuthorized': False}
            with self.subTest(drift_class=drift_class), \
                 patch.object(host.subprocess, 'run') as run:
                run.return_value.returncode = 1
                run.return_value.stdout = raw(refusal)
                self.assertEqual(host.run_container(raw(minted)), refusal)
        for stage, drift_class in (('pass-1-repository-repeat', 'updated_at'),
                                   ('pass-1-repository-identity', 'other')):
            refusal['refusal']['stage'] = stage
            refusal['refusal']['driftClass'] = drift_class
            with self.subTest(stage=stage, drift_class=drift_class), \
                 patch.object(host.subprocess, 'run') as run:
                run.return_value.returncode = 1
                run.return_value.stdout = raw(refusal)
                with self.assertRaisesRegex(ValueError, 'sanitized refusal'):
                    host.run_container(raw(minted))

    def test_container_rejects_incomplete_or_unbound_success_report(self):
        minted = mint_value()
        for mutate in ('empty-passes', 'wrong-expiry', 'bad-count'):
            report = observed_report(minted)
            if mutate == 'empty-passes':
                report['passes'] = [{}, {}]
            elif mutate == 'wrong-expiry':
                report['mint']['expiresAt'] = '2099-01-01T00:00:00Z'
            else:
                report['passes'][0]['receiverRefCount'] = True
                report['passes'][1]['receiverRefCount'] = True
            with self.subTest(mutate=mutate), patch.object(host.subprocess, 'run') as run:
                run.return_value.returncode = 0
                run.return_value.stdout = raw(report)
                with self.assertRaisesRegex(ValueError, 'unexpected data or drift'):
                    host.run_container(raw(minted))

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

            observed = observed_report()
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

    def test_refusal_is_retained_after_revocation_and_remains_incomplete(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp) / 'private'
            app = {'id': host.APP_ID, 'slug': host.APP_SLUG,
                   'permissions': {'contents': 'read', 'metadata': 'read'}}
            installation = {'id': host.INSTALLATION_ID, 'app_id': host.APP_ID,
                            'app_slug': host.APP_SLUG, 'account': {'login': 'FS-GG'},
                            'suspended_at': None,
                            'permissions': {'contents': 'read', 'metadata': 'read'}}
            values = iter((app, installation, mint_value()))
            refusal = {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                       'status': 'refused', 'complete': False,
                       'refusal': {'code': 'github-http-error', 'stage': 'bearer-read',
                                   'httpStatus': 403, 'driftClass': None},
                       'copyAuthorized': False, 'refMutationAuthorized': False}
            env = {'GITHUB_REPOSITORY': 'FS-GG/.github', 'GITHUB_REF': 'refs/heads/main',
                   'GITHUB_SHA': 'a' * 40, 'FSGG_PROTECTED_SHA': 'a' * 40,
                   'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1'}
            with patch.dict(os.environ, env), patch.object(host.mint, 'app_jwt', return_value='jwt'), \
                 patch.object(host, 'request_json', side_effect=lambda *_args, **_kwargs:
                              (lambda value: (value, raw(value)))(next(values))), \
                 patch.object(host, 'revoke_token') as revoke, \
                 patch.object(host, 'run_container', return_value=refusal):
                result = host.run(root, host.APP_ID, 'PRIVATE KEY')
            revoke.assert_called_once_with(TOKEN)
            self.assertEqual(result['status'], 'refused')
            self.assertFalse(result['complete'])
            self.assertTrue(result['revoked'])
            self.assertEqual(result['observer']['refusal']['httpStatus'], 403)
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
            observed = observed_report()
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

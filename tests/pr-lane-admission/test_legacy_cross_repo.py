"""Closed v2 ownership through the actual entry point, with inert provider boundaries."""
import contextlib
import copy
import json
import pathlib
import unittest
from unittest.mock import patch

import test_legacy_update as v1

lane, helper = v1.lane, v1.recovery


class CrossRepositoryLegacyTests(unittest.TestCase):
    @contextlib.contextmanager
    def fixture(self):
        c = v1.LegacyUpdateTests(); c.setUp()
        try:
            c.plan['schema'] = helper.SCHEMA_V2
            c.repo['owner'] = {'id': 77, 'login': 'FS-GG', 'type': 'Organization'}
            c.owner_repo = {**copy.deepcopy(c.repo), 'id': 19, 'full_name': 'FS-GG/owning'}
            for row in c.plan['pulls']:
                row.update(deliveryRepo='FS-GG/example', deliveryRepositoryId=9)
                issue = c.issues[row['delivery']]
                issue.update(url=f"https://api.github.com/repos/FS-GG/example/issues/{row['delivery']}",
                             html_url=f"https://github.com/FS-GG/example/issues/{row['delivery']}")
            row = c.plan['pulls'][0]
            row.update(deliveryRepo='FS-GG/owning', deliveryRepositoryId=19)
            issue = c.issues[142]
            issue.update(repository_url='https://api.github.com/repos/FS-GG/owning',
                         url='https://api.github.com/repos/FS-GG/owning/issues/142',
                         html_url='https://github.com/FS-GG/owning/issues/142')
            c.pulls[42]['body'] = c.pulls[42]['body'].replace('Refs #142', 'Refs FS-GG/owning#142')
            row['bodySha256'] = lane.text_sha(c.pulls[42]['body'])
            c.original = copy.deepcopy(c.pulls)
            c.body.write_text('Refs FS-GG/owning#142\n\nQualified source.\n')
            old_auth, old_gh = c.auth, c.gh
            def auth():
                old_auth()
                owner = c.plan['pulls'][0].get('deliveryRepo', c.plan['repo'])
                c.comment['issue_url'] = f'https://api.github.com/repos/{owner}/issues/142'
                c.node['issue']['repository']['nameWithOwner'] = owner
            def gh(executable, args, stdin=None):
                if args[0] != 'graphql' and args[-1] == '/repos/FS-GG/owning':
                    c.calls.append(('GET', args[-1])); c.hook('GET', args[-1])
                    if c.owner_repo is None:
                        raise lane.ToolError('inert owning repository unavailable')
                    return copy.deepcopy(c.owner_repo)
                return old_gh(executable, args, stdin)
            c.auth, c.gh = auth, gh
            c.auth()
            yield c
        finally:
            c.doCleanups()

    def test_cross_repo_full_roster_updates_only_selected_and_binds_native_owner(self):
        with self.fixture() as c:
            code, result = c.invoke(); self.assertEqual(0, code, result)
            self.assertEqual([42], c.patches)
            self.assertEqual({n:p for n,p in c.original.items() if n != 42},
                             {n:p for n,p in c.pulls.items() if n != 42})
            self.assertIn(('GET', '/repos/FS-GG/owning/issues/142'), c.calls)
            self.assertIn(('GET', '/repos/FS-GG/owning/issues/comments/100'), c.calls)
            self.assertNotIn(('GET', '/repos/FS-GG/example/issues/142'), c.calls)
            journal = json.loads(next(c.root.glob('legacy-update-*/*.json')).read_text())
            self.assertEqual(helper.SCHEMA_V2, journal['schema'])
            self.assertEqual('complete', journal['phase']); self.assertEqual(6, len(journal['readbackPopulation']))
            self.assertEqual({'id': 2042, 'number': 142, 'bodySha256': c.plan['pulls'][0]['issueBodySha256'],
                              'state': 'open', 'repo': 'FS-GG/owning', 'repositoryId': 19, 'principalId': 77},
                             journal['ownership']['42'])
            self.assertEqual('d'*40, c.pulls[43]['base']['sha'])
            self.assertIn('extra=preserve', c.pulls[42]['body']); self.assertIn('fsgg:future/v8', c.pulls[42]['body'])
            self.assertNotIn('fsgg-pr-lane-admission', c.pulls[42]['body'])
            with self.assertRaises(lane.Refusal): lane.enforce_lane(list(c.pulls.values()), 'new', 'new')

    def test_v2_same_repo_rows_can_share_one_delivery_without_reassigning_other_rows(self):
        with self.fixture() as c:
            row = c.plan['pulls'][2]
            row.update(delivery=143, issueId=c.issues[143]['id'], issueBodySha256=c.plan['pulls'][1]['issueBodySha256'])
            c.pulls[44]['body'] = c.pulls[44]['body'].replace('#144', '#143')
            row['bodySha256'] = lane.text_sha(c.pulls[44]['body'])
            code, result = c.invoke(('--inspect',)); self.assertEqual(0, code, result)
            self.assertFalse(c.patches); self.assertFalse(list(c.root.glob('legacy-update-*/*.json')))

    def test_v2_closed_plan_rejects_foreign_missing_alias_and_duplicate_ownership(self):
        mutations = [
            lambda c: c.plan['pulls'][0].pop('deliveryRepo'),
            lambda c: c.plan['pulls'][0].pop('deliveryRepositoryId'),
            lambda c: c.plan['pulls'][0].update(deliveryRepo='foreign/owning'),
            lambda c: c.plan['pulls'][0].update(deliveryRepo='FS-GG/../owning'),
            lambda c: c.plan['pulls'][0].update(deliveryRepo='FS-GG/owning?x=y'),
            lambda c: c.plan['pulls'][0].update(deliveryRepositoryId=True),
            lambda c: c.plan.update(pulls=[{**c.plan['pulls'][0], 'deliveryRepositoryId':9}]),
            lambda c: c.plan['pulls'][1].update(deliveryRepositoryId=19),
            lambda c: c.plan['pulls'][1].update(issueId=c.plan['pulls'][0]['issueId']),
            lambda c: c.plan['pulls'][1].update(deliveryRepo='FS-GG/owning', deliveryRepositoryId=20),
            lambda c: c.plan['pulls'][1].update(delivery=144, issueId=2044),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate), self.fixture() as c:
                mutate(c); code, result = c.invoke(); self.assertEqual(2, code, result)
                self.assertEqual([], c.calls); self.assertEqual([], c.patches)

    def test_v2_closed_roster_range_and_typed_ownership_values(self):
        with self.fixture() as c:
            template = copy.deepcopy(c.plan['pulls'][0])
            for count in (0, 1, 6, 20, 21):
                c.plan['pulls'] = []
                for i in range(count):
                    row = {**template, 'number':42+i, 'id':1042+i, 'nodeId':f'PR_{42+i}', 'branch':f'routine/legacy{42+i}'}
                    c.plan['pulls'].append(row)
                c.path.write_text(json.dumps(c.plan))
                if 1 <= count <= 20:
                    self.assertEqual(count, len(helper.read_plan(lane,c.path,c.plan['repo'])['pulls']))
                else:
                    with self.assertRaises(lane.ToolError): helper.read_plan(lane,c.path,c.plan['repo'])
            for field, values in [('deliveryRepo', [None, 1, [], {}, '', 'FS-GG/.', 'FS-GG/..', 'FS-GG/x\ny']),
                                  ('deliveryRepositoryId', [None, False, True, 0, -1, 1.5, '19'])]:
                for value in values:
                    c.plan['pulls'] = [copy.deepcopy(template)]; c.plan['pulls'][0][field] = value
                    c.path.write_text(json.dumps(c.plan))
                    with self.subTest(field=field,value=value), self.assertRaises(lane.ToolError):
                        helper.read_plan(lane,c.path,c.plan['repo'])
            self.assertFalse(c.calls)

    def test_v2_authorization_repository_path_principal_and_source_mutants_refuse(self):
        mutations = [
            lambda c: setattr(c, 'owner_repo', None),
            lambda c: c.owner_repo.update(id=29),
            lambda c: c.owner_repo.update(full_name='FS-GG/other'),
            lambda c: c.owner_repo.update(archived=True),
            lambda c: c.owner_repo.update(disabled=True),
            lambda c: c.owner_repo['owner'].update(id=88),
            lambda c: c.owner_repo['owner'].update(login='foreign'),
            lambda c: c.owner_repo['owner'].update(type='User'),
            lambda c: c.repo.pop('owner'),
            lambda c: c.issues[142].update(id=999),
            lambda c: c.issues[142].update(number=143),
            lambda c: c.issues[142].update(repository_url='https://api.github.com/repos/FS-GG/example'),
            lambda c: c.issues[142].update(url='https://api.github.com/repos/FS-GG/other/issues/142'),
            lambda c: c.issues[142].update(html_url='https://github.com/FS-GG/example/issues/142'),
            lambda c: c.issues[142].update(pull_request={}),
            lambda c: c.issues[142].update(state='closed'),
            lambda c: c.issues[142].update(body='Work level: Outcome'),
            lambda c: c.comment.update(issue_url='https://api.github.com/repos/FS-GG/example/issues/142'),
            lambda c: c.node['issue']['repository'].update(nameWithOwner='FS-GG/example'),
            lambda c: c.comment.update(node_id='IC_wrong'),
            lambda c: c.node.update(body='wrong'),
            lambda c: c.node['author'].update(login='attacker'),
            lambda c: c.node.update(lastEditedAt='2026-11-04T00:00:00Z', editor={'login':'attacker'}),
            lambda c: c.pulls[42]['head'].update(sha='f'*40),
            lambda c: c.plan.update(defaultSha='f'*40),
            lambda c: c.comparison.update(status='diverged'),
            lambda c: c.pulls[47].update(title='changed'),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate), self.fixture() as c:
                mutate(c); code, result = c.invoke(); self.assertNotEqual(0, code, result); self.assertFalse(c.patches)

    def test_foreign_issue_unavailable_and_wrong_owning_refs_never_fall_back_to_local_alias(self):
        for mode in ('unavailable', 'native-ref', 'proposed-ref', 'code-only', 'comment-v1'):
            with self.subTest(mode=mode), self.fixture() as c:
                old = c.gh
                def gh(executable, args, stdin=None):
                    if mode == 'unavailable' and args[-1] == '/repos/FS-GG/owning/issues/142':
                        raise lane.ToolError('inert issue404')
                    return old(executable, args, stdin)
                c.gh = gh
                if mode == 'native-ref':
                    c.pulls[42]['body'] = c.pulls[42]['body'].replace('FS-GG/owning#142', '#142')
                    c.plan['pulls'][0]['bodySha256'] = lane.text_sha(c.pulls[42]['body'])
                if mode == 'proposed-ref': c.body.write_text('Refs #142')
                if mode == 'code-only': c.body.write_text('`Refs FS-GG/owning#142`')
                if mode == 'comment-v1':
                    c.comment['body'] = c.comment['body'].replace('legacy-pr-update/v2', 'legacy-pr-update/v1')
                    c.plan['authorization']['bodySha256'] = lane.text_sha(c.comment['body']); c.node['body'] = c.comment['body']
                self.assertNotEqual(0, c.invoke()[0]); self.assertFalse(c.patches)

    def test_v2_rechecks_owner_and_authorization_adjacent_to_send_and_full_readback(self):
        for stage in (2, 3, 4):
            for field in ('issue', 'repo', 'comment', 'local-other-owner'):
                with self.subTest(stage=stage, field=field), self.fixture() as c:
                    reads = 0
                    def hook(method, endpoint):
                        nonlocal reads
                        if endpoint.endswith('pulls?state=open&per_page=100'):
                            reads += 1
                            if reads == stage:
                                if field == 'issue': c.issues[142]['body'] = 'changed'
                                if field == 'repo': c.owner_repo['owner']['id'] = 88
                                if field == 'comment': c.node['editor'] = {'login':'attacker'}; c.node['lastEditedAt'] = 'edited'
                                if field == 'local-other-owner': c.issues[147]['body'] = 'changed'
                    c.hook = hook
                    code, result = c.invoke(); self.assertNotEqual(0, code, result)
                    self.assertEqual([42] if stage == 4 else [], c.patches)
                    if stage == 4:
                        self.assertEqual('legacy-update-custody-exists-no-resend', c.invoke()[1]['reason'])
                        self.assertEqual([42], c.patches)

    def test_schema_change_cannot_bypass_fixed_pr_custody_or_uncertain_send(self):
        for error in (None, 'send', 'not-sent', 'unavailable', 'drift'):
            with self.subTest(error=error), self.fixture() as c:
                c.error = error; c.invoke(); self.assertEqual([42], c.patches)
                before = {str(x):x.read_bytes() for x in c.root.glob('legacy-update-*/*.json')}
                c.error = None
                c.plan['schema'] = helper.SCHEMA
                for row in c.plan['pulls']: row.pop('deliveryRepo'); row.pop('deliveryRepositoryId')
                c.auth()
                code, result = c.invoke(); self.assertEqual('legacy-update-custody-exists-no-resend', result['reason'])
                self.assertEqual([42], c.patches); self.assertEqual(before, {str(x):x.read_bytes() for x in c.root.glob('legacy-update-*/*.json')})

    def test_v2_uncertain_explicit_successor_and_storage_failure_preserve_original(self):
        for error, disposition in (('send', 'delivered'), ('not-sent', 'not-sent')):
            with self.subTest(error=error), self.fixture() as c:
                c.error = error; self.assertNotEqual(0, c.invoke()[0]); c.error = None
                path, original = c.successor(disposition)
                c.body.write_text('Refs FS-GG/owning#142\nExplicit separately authorized continuation.')
                code, result = c.invoke(); self.assertEqual(0, code, result)
                self.assertEqual(original, path.read_bytes()); self.assertEqual([42,42], c.patches)
        with self.fixture() as c:
            original = lane.save_roster_custody
            def save(path, value, **kwargs):
                if value['phase'] == 'possible-send': raise OSError('inert disk-full before send')
                original(path, value, **kwargs)
            with patch.object(lane, 'save_roster_custody', save):
                self.assertEqual(2, c.invoke()[0])
            self.assertFalse(c.patches)
            self.assertEqual('legacy-update-custody-exists-no-resend', c.invoke()[1]['reason'])

    def test_v2_unknown_or_foreign_journal_identity_cannot_authorize_another_send(self):
        for key, value in [('schema','fsgg.legacy-pr-update/99'), ('repo','FS-GG/other'), ('nativeId',99999)]:
            with self.subTest(key=key), self.fixture() as c:
                c.error = 'send'; self.assertNotEqual(0,c.invoke()[0]); c.error = None
                path = next(c.root.glob('legacy-update-*/*.json')); old = json.loads(path.read_text())
                old[key] = value; path.write_text(json.dumps(old)); retained = path.read_bytes()
                code, result = c.invoke(); self.assertEqual(2,code,result)
                self.assertEqual([42],c.patches); self.assertEqual(retained,path.read_bytes())
        with self.fixture() as c:
            c.plan['pulls'][0]['id'] = 99999; c.auth()
            code,result = c.invoke(); self.assertEqual('legacy-update-preimage-identity-mismatch',result['reason'])
            self.assertFalse(c.patches)

    def test_v1_journal_is_same_address_and_needs_explicit_successor_for_v2(self):
        c = v1.LegacyUpdateTests(); c.setUp()
        try:
            self.assertEqual(0, c.invoke()[0]); path = next(c.root.glob('legacy-update-*/*.json')); before = path.read_bytes()
            c.repo['owner'] = {'id':77, 'login':'FS-GG', 'type':'Organization'}
            c.plan['schema'] = helper.SCHEMA_V2
            for row in c.plan['pulls']:
                row.update(deliveryRepo='FS-GG/example', deliveryRepositoryId=9)
                issue = c.issues[row['delivery']]
                issue.update(url=f"https://api.github.com/repos/FS-GG/example/issues/{row['delivery']}",
                             html_url=f"https://github.com/FS-GG/example/issues/{row['delivery']}")
            c.auth(); self.assertEqual('legacy-update-custody-exists-no-resend', c.invoke()[1]['reason'])
            c.successor('delivered'); code, result = c.invoke(); self.assertEqual(0, code, result)
            self.assertEqual(before, path.read_bytes()); self.assertEqual([42,42], c.patches)
            self.assertEqual(helper.SCHEMA_V2, json.loads(sorted(c.root.glob('legacy-update-*/*.json'))[-1].read_text())['schema'])
        finally: c.doCleanups()

    def test_v1_closed_grammar_declaration_and_journal_identity_remain_unchanged(self):
        c = v1.LegacyUpdateTests(); c.setUp()
        try:
            expected = '<!-- fsgg:legacy-pr-update/v1 ' + helper.encoded({
                'repo':'FS-GG/example','prId':1042,'number':42,'delivery':142,'branch':'routine/legacy42',
                'previousHead':'b'*40,'currentHead':'a'*40,'operation':'update-body-title','successor':None}) + ' -->'
            self.assertEqual(expected, helper.declaration(c.plan,c.plan['pulls'][0]))
            c.plan['pulls'][0].update(deliveryRepo='FS-GG/example',deliveryRepositoryId=9)
            self.assertEqual(2, c.invoke()[0]); self.assertFalse(c.calls)
        finally: c.doCleanups()

    def test_packaged_v2_entry_uses_actual_alias_policy_and_closed_ownership(self):
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            stage = v1.load('cross_repo_stage', v1.ROOT/'src/FS.GG.Drivers/stage-drivers.py')
            output = pathlib.Path(directory)/'package'; stage.stage_workspace(str(output))
            staged = v1.load('cross_repo_packaged_lane', output/'workspace/files/tools/pr-lane-admission.py')
            with self.fixture() as c, patch.object(v1, 'lane', staged):
                lock = c.root/'locks'; lock.mkdir(mode=0o700)
                with patch.object(staged,'lock_root',return_value=lock):
                    code, result = c.invoke(('--inspect',)); self.assertEqual(0, code, result)
                self.assertEqual('.fsgg/legacy-pr-root-rules.py', result['rootRulesSource']['path'])
                self.assertFalse(c.patches)


if __name__ == '__main__': unittest.main()

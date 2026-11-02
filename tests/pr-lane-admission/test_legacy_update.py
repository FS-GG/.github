"""Provider-boundary continuation tests: genuine CLI path, actual REST list/detail shapes."""
import contextlib
import copy
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

import test_pr_lane_admission as existing

ROOT = pathlib.Path(__file__).resolve().parents[2]
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module
lane = load('continuation_lane', ROOT/'tools/pr-lane-admission.py')
recovery = load('continuation_helper', ROOT/'tools/legacy-pr-update.py')


class LegacyUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.pulls = {}; self.issues = {}; self.calls = []; self.patches = []
        self.hook = lambda method, endpoint: None
        self.error = None
        self.repo = {'id': 9, 'full_name': 'FS-GG/example', 'default_branch': 'main', 'archived': False, 'disabled': False}
        self.plan = {'schema': recovery.SCHEMA, 'repo': 'FS-GG/example', 'repositoryId': 9,
            'defaultBranch': 'main', 'defaultSha': 'c'*40, 'number': 42, 'previousHead': 'b'*40,
            'authorization': {'commentId': 100, 'nodeId': 'IC_100', 'bodySha256': '0'*64}, 'successor': None, 'pulls': []}
        for n in range(42,48):
            native = existing.PullRequestLaneAdmissionTests().update_state()['pull']
            native.update(number=n, id=1000+n, node_id=f'PR_{n}', html_url=f'https://github.com/FS-GG/example/pull/{n}')
            native['head'].update(ref=f'routine/legacy{n}', sha='a'*40)
            if n in (43,44): native['base'].update(ref=f'routine/legacy{n-1}',sha='d'*40)
            native['body'] = f'Refs #{n+100}\n\n'
            if n != 45:
                native['body'] += f'<!-- fsgg:routine-development/v1 head={"b"*40 if n==42 else "a"*40} operation={"source" if n==47 else "source-change"} extra=preserve -->\n'
            native['body'] += '<!-- fsgg:future/v8 opaque=keep -->\n'
            if n==42: native['body'] += '<!-- fsgg:delivery-continues/v1 -->\n'
            self.pulls[n] = native
            self.issues[n+100] = {'id': n+2000, 'number': n+100, 'state': 'open', 'body': f'Work level: Delivery\nAcceptance for {n}',
                                  'repository_url':'https://api.github.com/repos/FS-GG/example'}
            self.plan['pulls'].append({'number':n,'id':native['id'],'nodeId':native['node_id'],'authorId':native['user']['id'],
                'author':native['user']['login'],'branch':native['head']['ref'],'headSha':native['head']['sha'],
                'base':native['base']['ref'],'baseSha':native['base']['sha'],
                'bodySha256':lane.text_sha(native['body']),'titleSha256':lane.text_sha(native['title']),
                'delivery':n+100,'issueId':n+2000,'issueBodySha256':lane.text_sha(self.issues[n+100]['body'])})
        self.original = copy.deepcopy(self.pulls)
        self.path = self.root/'plan.json'; self.body = self.root/'body.md'; self.body.write_text('Refs #142\n\nQualified source.\n')
        self.comment = {}; self.node = {}; self.auth()
        self.comparison = {'status':'ahead','base_commit':{'sha':'b'*40},'merge_base_commit':{'sha':'b'*40},
                           'ahead_by':1,'behind_by':0,'total_commits':1,'commits':[{'sha':'a'*40}]}

    def auth(self):
        row = self.plan['pulls'][0]; auth = self.plan['authorization']
        body = recovery.declaration(self.plan,row)
        auth['bodySha256'] = lane.text_sha(body)
        self.comment = {'id':auth['commentId'],'node_id':auth['nodeId'],'body':body,'user':{'login':'EHotwagner'},
                        'issue_url':'https://api.github.com/repos/FS-GG/example/issues/142'}
        self.node = {'id':auth['nodeId'],'databaseId':auth['commentId'],'body':body,'lastEditedAt':None,
                     'author':{'login':'EHotwagner'},'editor':None,'issue':{'number':142,'repository':{'nameWithOwner':'FS-GG/example'}}}

    def gh(self, executable, args, stdin=None):
        if args[0] == 'graphql':
            self.hook('GRAPHQL','graphql'); return {'data':{'node':copy.deepcopy(self.node)}}
        method = args[args.index('--method')+1]; endpoint = args[-1] if args[-1]!='-' else args[-3]
        self.calls.append((method,endpoint)); self.hook(method,endpoint)
        if endpoint == '/repos/FS-GG/example': return copy.deepcopy(self.repo)
        if endpoint.endswith('pulls?state=open&per_page=100'):
            rows = copy.deepcopy(list(self.pulls.values()))
            for row in rows: row.pop('merged'); row.pop('maintainer_can_modify')
            return [rows[:3],rows[3:]]
        if '/git/ref/heads/' in endpoint:
            branch = endpoint.split('/git/ref/heads/')[1].replace('%2F','/')
            if branch=='main': return {'object':{'sha':'c'*40}}
            return {'object':{'sha':next(p['head']['sha'] for p in self.pulls.values() if p['head']['ref']==branch)}}
        if '/issues/comments/' in endpoint: return copy.deepcopy(self.comment)
        if '/compare/' in endpoint: return copy.deepcopy(self.comparison)
        if '/issues/' in endpoint: return copy.deepcopy(self.issues[int(endpoint.rsplit('/',1)[1])])
        number = int(endpoint.rsplit('/',1)[1])
        if method=='GET':
            if self.patches and self.error=='unavailable': raise lane.ToolError('read unavailable')
            return copy.deepcopy(self.pulls[number])
        self.assertEqual('PATCH',method); payload=json.loads(stdin); self.assertEqual({'body','title'},set(payload))
        journal=json.loads(sorted(self.root.glob('legacy-update-*/*.json'))[-1].read_text())
        self.assertEqual('possible-send',journal['phase'])
        self.assertEqual(journal['intent']['plan'],self.plan)
        self.assertEqual(journal['after']['bodySha256'],lane.text_sha(payload['body']))
        self.patches.append(number)
        if self.error!='not-sent': self.pulls[number].update(payload)
        if self.error in ('send','not-sent'): raise lane.ToolError('uncertain acknowledgement')
        if self.error=='drift':self.pulls[47]['title']='changed'
        if self.error=='readback':self.pulls[number]['locked']=True
        return copy.deepcopy(self.pulls[number])

    def invoke(self, extra=()):
        self.path.write_text(json.dumps(self.plan))
        with patch.object(lane,'run_gh',self.gh), patch.object(lane,'roster_custody_root',return_value=self.root), contextlib.redirect_stdout(io.StringIO()) as out:
            code=lane.main(['--repo','FS-GG/example','--recover-legacy-update',str(self.path),
                            '--body-file',str(self.body),'--title','Qualified continuation',*extra])
        return code,json.loads(out.getvalue())

    def test_six_pr_continuation_preserves_reservations_and_ordinary_cap(self):
        code,result=self.invoke();self.assertEqual(0,code,result);self.assertEqual([42],self.patches)
        self.assertEqual({n:p for n,p in self.original.items() if n!=42},{n:p for n,p in self.pulls.items() if n!=42})
        self.assertNotIn('fsgg-pr-lane-admission',self.pulls[42]['body'])
        self.assertIn('head='+'a'*40+' operation=source-change extra=preserve',self.pulls[42]['body'])
        self.assertIn('<!-- fsgg:future/v8 opaque=keep -->',self.pulls[42]['body'])
        self.assertIn('<!-- fsgg:delivery-continues/v1 -->',self.pulls[42]['body'])
        with self.assertRaises(lane.Refusal) as caught:lane.enforce_lane(list(self.pulls.values()),'fresh','new')
        self.assertEqual('repository-open-pr-cap',caught.exception.reason)
        journal=json.loads(next(self.root.glob('legacy-update-*/*.json')).read_text())
        self.assertEqual('complete',journal['phase']);self.assertEqual(6,len(journal['readbackPopulation']))
        self.assertEqual(journal['after']['metadataSha256'],journal['readbackPopulation']['42'])

    def test_inspect_is_no_effect_and_historical_base_is_preserved(self):
        code,result=self.invoke(('--inspect',));self.assertEqual(0,code,result)
        self.assertFalse(self.patches);self.assertFalse(list(self.root.glob('legacy-update-*/*.json')))
        self.assertEqual('d'*40,self.pulls[43]['base']['sha'])

    def test_native_population_identity_text_owner_and_authorization_mutants_refuse(self):
        mutations=[lambda c:c.pulls.pop(47),lambda c:c.pulls.update({99:copy.deepcopy(c.pulls[47])}),
            lambda c:c.pulls[47]['head'].update(sha='f'*40),lambda c:c.pulls[47].update(body='changed'),
            lambda c:c.pulls[47].update(title='changed'),lambda c:c.pulls[42]['user'].update(login='other'),
            lambda c:c.repo.update(id=999),lambda c:c.plan.update(defaultSha='f'*40),
            lambda c:c.pulls[43]['base'].update(sha='e'*40),lambda c:c.issues[142].update(state='closed'),
            lambda c:c.issues[147].update(body='changed'),lambda c:c.comment.update(body=c.comment['body']+' changed'),
            lambda c:c.node['author'].update(login='attacker'),lambda c:c.node.update(lastEditedAt='2026-11-02T00:00:00Z',editor={'login':'attacker'}),
            lambda c:c.node.pop('lastEditedAt'),lambda c:c.node.update(databaseId=999),
            lambda c:c.node['issue'].update(number=999),lambda c:c.comment['user'].update(login='attacker'),
            lambda c:c.comparison.update(status='diverged'),lambda c:c.comparison['merge_base_commit'].update(sha='f'*40),
            lambda c:c.comparison.update(commits=[]),lambda c:c.comparison.update(ahead_by=101,total_commits=101)]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                c=LegacyUpdateTests();c.setUp()
                try:
                    mutate(c);code,result=c.invoke();self.assertNotEqual(0,code,result);self.assertEqual([],c.patches)
                finally:c.doCleanups()

    def test_marker_owner_and_mode_injection_refused(self):
        cases=['Refs #142\n<!-- fsgg-pr-lane-admission:v1 campaign=x chain=y -->',
               'Refs #142\n<!-- fsgg:delivery-completes/v1 -->','Refs #142\n<!-- fsgg:new/v1 x=y -->',
               'Only mention #142 without an owning reference', 'Refs #143']
        for body in cases:
            with self.subTest(body=body):
                self.body.write_text(body);code,result=self.invoke();self.assertNotEqual(0,code,result);self.assertFalse(self.patches)
        self.body.write_text('Refs #142')
        for extra in [('--campaign-id','x'),('--adopt-legacy-pr',),('--declare-delivery-continues',),('--update-pr','42')]:
            self.assertNotEqual(0,self.invoke(extra)[0]);self.assertFalse(self.patches)

    def test_native_selected_bad_marker_refused_even_with_updated_preimage(self):
        for replacement in ['operation=source','operation=merge']:
            c=LegacyUpdateTests();c.setUp()
            try:
                c.pulls[42]['body']=c.pulls[42]['body'].replace('operation=source-change',replacement)
                c.plan['pulls'][0]['bodySha256']=lane.text_sha(c.pulls[42]['body'])
                self.assertNotEqual(0,c.invoke()[0]);self.assertFalse(c.patches)
            finally:c.doCleanups()

    def test_deciding_recheck_catches_drift_before_send(self):
        for field in ('locked','auth','issue'):
            c=LegacyUpdateTests();c.setUp();reads=0
            def hook(method,endpoint):
                nonlocal reads
                if endpoint.endswith('pulls?state=open&per_page=100'):
                    reads+=1
                    if reads==3:
                        if field=='locked':c.pulls[47]['locked']=True
                        if field=='auth':c.node.update(lastEditedAt='2026-11-02T00:00:00Z',editor={'login':'EHotwagner'})
                        if field=='issue':c.issues[147]['body']='changed'
            c.hook=hook
            try:self.assertNotEqual(0,c.invoke()[0]);self.assertFalse(c.patches)
            finally:c.doCleanups()

    def test_send_uncertainty_readback_drift_and_changed_plan_never_resend(self):
        for mode in ('send','not-sent','readback','unavailable','drift'):
            c=LegacyUpdateTests();c.setUp()
            try:
                c.error=mode;code,result=c.invoke();self.assertNotEqual(0,code,result);self.assertEqual([42],c.patches)
                c.error=None;c.body.write_text('Refs #142\nchanged retry');c.path=c.root/'another-name.json'
                code,result=c.invoke();self.assertEqual('legacy-update-custody-exists-no-resend',result['reason']);self.assertEqual([42],c.patches)
            finally:c.doCleanups()

    def test_custody_write_failure_stops_before_effect(self):
        original=lane.save_roster_custody
        def save(path,value,**kwargs):
            if value['phase']=='possible-send':raise OSError('disk full')
            original(path,value,**kwargs)
        with patch.object(lane,'save_roster_custody',save):
            code,result=self.invoke();self.assertEqual(2,code,result)
        self.assertFalse(self.patches)
        self.assertEqual('legacy-update-custody-exists-no-resend',self.invoke()[1]['reason'])

    def successor(self, disposition):
        path=sorted(self.root.glob('legacy-update-*/*.json'))[-1];prior=json.loads(path.read_text())
        self.plan['successor']={'custodySha256':lane.text_sha(path.read_text()),'intentSha256':prior['intentSha256'],'disposition':disposition}
        self.plan['authorization'].update(commentId=self.plan['authorization']['commentId']+1,nodeId='IC_successor')
        self.plan['pulls'][0].update(bodySha256=lane.text_sha(self.pulls[42]['body']),titleSha256=lane.text_sha(self.pulls[42]['title']))
        if disposition=='delivered':
            self.plan['previousHead']='a'*40
            self.comparison={'status':'identical','base_commit':{'sha':'a'*40},'merge_base_commit':{'sha':'a'*40},'ahead_by':0,'behind_by':0,'total_commits':0,'commits':[]}
        self.body.write_text('Refs #142\nNext separately authorized continuation.');self.auth()
        return path,path.read_bytes()

    def test_completed_reentry_refuses_but_explicit_authenticated_successor_preserves_original(self):
        self.assertEqual(0,self.invoke()[0]);self.assertEqual('legacy-update-custody-exists-no-resend',self.invoke()[1]['reason'])
        path,original=self.successor('delivered');code,result=self.invoke();self.assertEqual(0,code,result)
        self.assertEqual(original,path.read_bytes());self.assertEqual([42,42],self.patches)
        self.assertEqual(2,len(list(self.root.glob('legacy-update-*/*.json'))))

    def test_uncertain_successors_require_native_disposition_and_new_root_comment(self):
        for mode,disposition in [('send','delivered'),('not-sent','not-sent')]:
            c=LegacyUpdateTests();c.setUp()
            try:
                c.error=mode;self.assertNotEqual(0,c.invoke()[0]);c.error=None
                path,original=c.successor(disposition)
                c.node['author']['login']='attacker';self.assertNotEqual(0,c.invoke()[0]);self.assertEqual([42],c.patches)
                c.auth();code,result=c.invoke();self.assertEqual(0,code,result);self.assertEqual(original,path.read_bytes())
            finally:c.doCleanups()

    def test_successor_wrong_disposition_reused_authorization_and_history_refuse(self):
        self.assertEqual(0,self.invoke()[0]);self.successor('not-sent')
        self.assertEqual('legacy-update-successor-disposition-unproved',self.invoke()[1]['reason']);self.assertEqual([42],self.patches)
        self.plan['successor']['custodySha256']='f'*64;self.auth()
        self.assertEqual('legacy-update-custody-exists-no-resend',self.invoke()[1]['reason'])

    def test_duplicate_keys_extra_fields_oversized_and_symlink_plans_refuse(self):
        self.path.write_text(json.dumps(self.plan))
        samples = ['{"repo":"FS-GG/example","repo":"FS-GG/example"}',
                   json.dumps({**self.plan, 'rootLogins':['attacker']}), ' '*65537]
        for raw in samples:
            self.path.write_text(raw)
            with self.assertRaises(lane.ToolError): recovery.read_plan(lane,self.path,'FS-GG/example')
        self.path.unlink();self.path.symlink_to(self.body)
        with self.assertRaises(lane.ToolError):recovery.read_plan(lane,self.path,'FS-GG/example')
        self.assertFalse(self.calls)

    def test_root_edit_must_have_native_editor_and_issue_must_declare_delivery(self):
        self.node.update(lastEditedAt='2026-11-02T00:00:00Z',editor=None)
        self.assertNotEqual(0,self.invoke()[0]);self.assertFalse(self.patches)
        self.node['editor']={'login':'EHotwagner'}
        self.assertEqual(0,self.invoke(('--inspect',))[0]);self.assertFalse(self.patches)
        self.issues[142]['body']='Work level: Outcome'
        self.plan['pulls'][0]['issueBodySha256']=lane.text_sha(self.issues[142]['body'])
        self.assertNotEqual(0,self.invoke()[0]);self.assertFalse(self.patches)

    def test_plain_lane_footprint_cannot_be_injected_and_missing_source_is_not_empty(self):
        self.body.write_text('Refs #142\nfsgg-pr-lane-admission')
        self.assertNotEqual(0,self.invoke()[0]);self.assertFalse(self.patches)
        self.body.write_text('Refs #142')
        self.comparison={}
        self.assertNotEqual(0,self.invoke()[0]);self.assertFalse(self.patches)

    def test_explicit_successor_can_bind_a_later_fast_forward_without_erasing_previous_operation(self):
        self.assertEqual(0,self.invoke()[0]);path,original=self.successor('delivered')
        self.pulls[42]['head']['sha']='e'*40;self.plan['pulls'][0]['headSha']='e'*40
        self.comparison={'status':'ahead','base_commit':{'sha':'a'*40},'merge_base_commit':{'sha':'a'*40},
                         'ahead_by':1,'behind_by':0,'total_commits':1,'commits':[{'sha':'e'*40}]}
        self.auth();code,result=self.invoke();self.assertEqual(0,code,result)
        self.assertEqual(original,path.read_bytes());self.assertIn('head='+'e'*40,self.pulls[42]['body'])

    def test_successor_cannot_reuse_an_original_root_comment(self):
        self.assertEqual(0,self.invoke()[0]);self.successor('delivered')
        self.plan['authorization'].update(commentId=100,nodeId='IC_100');self.auth()
        self.assertEqual('legacy-update-custody-exists-no-resend',self.invoke()[1]['reason']);self.assertEqual([42],self.patches)

    def test_directory_durability_failure_prevents_any_provider_effect(self):
        with patch('os.fsync',side_effect=OSError('directory flush failed')):
            code,result=self.invoke();self.assertEqual(2,code,result)
        self.assertFalse(self.patches);self.assertFalse(list(self.root.glob('legacy-update-*/*.json')))

if __name__=='__main__':unittest.main()

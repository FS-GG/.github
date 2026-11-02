"""Actual roster operation with only the gh provider boundary replaced; no network or CLR."""
import copy
import contextlib
import importlib.util
import io
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

import test_pr_lane_admission as existing

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location('known_roster_lane', ROOT / 'tools/pr-lane-admission.py')
lane = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(lane)


class KnownRosterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = pathlib.Path(self.temp.name)
        self.pulls = {}
        self.calls = []
        self.hook = lambda method, endpoint: None
        self.error = None
        self.extra = []
        self.spec = {'schema': lane.ROSTER_SCHEMA, 'repo': 'FS-GG/example', 'base': 'main', 'baseSha': 'c'*40, 'pulls': []}
        for n, head in [(42, 'a'*40), (43, 'b'*40)]:
            native = existing.PullRequestLaneAdmissionTests().update_state()['pull']
            native.update(number=n, id=1000+n, node_id=f'PR_{n}', html_url=f'https://github.com/FS-GG/example/pull/{n}')
            native['head'].update(ref=f'routine/legacy{n}', sha=head)
            native['body'] = f'Original{n}\n\n<!-- fsgg:routine-development/v1 head={head} operation=source-change extra=retain -->\n<!-- fsgg:future/v8 opaque=unchanged -->\n'
            self.pulls[n] = native
            self.spec['pulls'].append({'number': n, 'id': native['id'], 'nodeId': native['node_id'],
                'branch': native['head']['ref'], 'headSha': head, 'baseSha': native['base']['sha'], 'campaign': 'campaign', 'chain': f'chain{n}',
                'bodySha256': lane.text_sha(native['body']), 'titleSha256': lane.text_sha(native['title']), 'declareContinues': True})
        self.original = copy.deepcopy(self.pulls)
        self.path = self.root/'roster.json'
        self.patches = []

    def gh(self, executable, args, stdin=None):
        method = args[args.index('--method')+1]
        endpoint = args[-1] if args[-1] != '-' else args[-3]
        self.calls.append((method, endpoint)); self.hook(method, endpoint)
        if endpoint.endswith('pulls?state=open&per_page=100'):
            rows=[copy.deepcopy(p) for p in self.pulls.values()] + copy.deepcopy(self.extra)
            for row in rows:
                # Actual REST list response does not contain these detail-only fields.
                row.pop('merged',None);row.pop('maintainer_can_modify',None)
            return [rows]
        if '/git/ref/heads/' in endpoint:
            branch = endpoint.split('/git/ref/heads/')[1]
            if branch == 'main': return {'object': {'sha': 'c'*40}}
            for p in self.pulls.values():
                if branch.replace('%2F','/') == p['head']['ref']: return {'object': {'sha': p['head']['sha']}}
            raise lane.ToolError('unknown branch')
        n = int(endpoint.rsplit('/',1)[1])
        if method == 'GET': return copy.deepcopy(self.pulls[n])
        self.assertEqual('PATCH',method)
        payload = json.loads(stdin); self.assertEqual({'body'},set(payload))
        # At actual provider invocation, durable possible-send must already be observable.
        journal = json.loads(next(self.root.glob('*.custody.json')).read_text())
        self.assertEqual(('possible-send', n), (journal['phase'],journal['steps'][-1]['number']))
        self.patches.append(n)
        self.pulls[n].update(payload)
        if self.error == 'send': raise lane.ToolError('lost acknowledgement')
        if self.error == 'readback': self.pulls[n]['title'] = 'concurrent title'
        if self.error == 'second-drift' and n == 42: self.pulls[43]['body'] += 'changed'
        if self.error == 'extra-after-first' and n == 42: self.extra.append(copy.deepcopy(self.pulls[43]))
        if self.error == 'second-send' and n == 43: raise lane.ToolError('second acknowledgement lost')
        return copy.deepcopy(self.pulls[n])

    def invoke(self, extra=()):
        self.path.write_text(json.dumps(self.spec))
        # The production path has a stable native-ID key; tests keep it isolated under one durable temp root.
        original_save = lane.save_roster_custody
        def save(path, value, **kw):
            original_save(path,value,**kw)
            (self.root/'observed.custody.json').write_text(path.read_text())
        with patch.object(lane, 'run_gh', self.gh), patch.object(lane,'roster_custody_root',return_value=self.root), \
             patch.object(lane,'save_roster_custody',save), contextlib.redirect_stdout(io.StringIO()) as out:
            code = lane.main(['--repo','FS-GG/example','--adopt-known-roster',str(self.path),*extra])
        return code,json.loads(out.getvalue())

    def test_exact_two_roster_is_adopted_preserving_all_other_native_metadata(self):
        code,result=self.invoke(); self.assertEqual(0,code,result);self.assertEqual([42,43],self.patches)
        for n in self.pulls:
            old=self.original[n];new=self.pulls[n]
            self.assertEqual({k:v for k,v in old.items() if k!='body'},{k:v for k,v in new.items() if k!='body'})
            for value in lane.machine_markers(old['body']).values():self.assertIn(value,new['body'])
            self.assertEqual(('campaign',f'chain{n}'),lane.marker_from_body(new['body']))
            self.assertIn('<!-- fsgg:delivery-continues/v1 -->',new['body'])
        self.assertEqual('complete',json.loads((self.root/'observed.custody.json').read_text())['phase'])

    def test_inspect_never_writes_or_records_possible_send(self):
        code,result=self.invoke(('--inspect',));self.assertEqual(0,code,result);self.assertEqual([],self.patches)
        self.assertEqual([],list(self.root.glob('*.custody.json')))

    def test_historical_native_pr_base_is_bound_without_rewriting_it_to_current_main(self):
        self.pulls[42]['base']['sha']='e'*40;self.spec['pulls'][0]['baseSha']='e'*40
        self.assertEqual(0,self.invoke()[0]);self.assertEqual('e'*40,self.pulls[42]['base']['sha'])

    def test_unaccepted_native_and_assignment_mutants_refuse_before_effects(self):
        mutations=[lambda:setattr(self,'extra',[copy.deepcopy(self.pulls[42])]),
            lambda:self.pulls.pop(43), lambda:self.pulls[43]['head'].update(sha='f'*40),
            lambda:self.spec['pulls'][1].update(chain='chain42'),lambda:self.spec['pulls'][1].update(id=9999),
            lambda:self.spec['pulls'][1].update(nodeId='forged'),lambda:self.spec['pulls'][0].update(campaign='bad space'),
            lambda:self.pulls[42].update(body=self.pulls[42]['body']+'<!-- fsgg-pr-lane-admission:bad -->'),
            lambda:self.pulls[42].update(title='concurrent'),lambda:self.spec.update(baseSha='f'*40),
            lambda:self.spec['pulls'][0].update(baseSha='e'*40),
            lambda:self.spec['pulls'].append(copy.deepcopy(self.spec['pulls'][0]))]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                self.pulls=copy.deepcopy(self.original);self.extra=[]
                saved=copy.deepcopy(self.spec);mutate();code,result=self.invoke();self.assertNotEqual(0,code,result)
                self.assertEqual([],self.patches);self.spec=saved

    def test_first_uncertain_send_and_readback_drift_stop_and_never_resend(self):
        for mode in ['send','readback','second-drift','extra-after-first']:
            with self.subTest(mode=mode):
                # Independent operation/custody sandbox for each causal boundary.
                case=KnownRosterTests();case.setUp()
                try:
                    case.error=mode;code,result=case.invoke();self.assertNotEqual(0,code,result);self.assertEqual([42],case.patches)
                    code,result=case.invoke();self.assertEqual('known-roster-custody-exists-no-resend',result['reason'])
                    self.assertEqual([42],case.patches);self.assertTrue(result['readback'])
                finally:case.doCleanups()

    def test_drift_between_population_read_and_deciding_read_blocks_first_send(self):
        reads=0
        def hook(method,endpoint):
            nonlocal reads
            if endpoint.endswith('pulls?state=open&per_page=100'):
                reads+=1
                if reads==3:self.pulls[43]['locked']=True
        self.hook=hook;code,result=self.invoke();self.assertNotEqual(0,code,result);self.assertEqual([],self.patches)

    def test_complete_reentry_is_also_read_only(self):
        self.assertEqual(0,self.invoke()[0]);self.assertEqual([42,43],self.patches)
        self.assertEqual('known-roster-custody-exists-no-resend',self.invoke()[1]['reason']);self.assertEqual([42,43],self.patches)

    def test_second_uncertain_send_preserves_first_confirmation_and_blocks_changed_plan_replay(self):
        self.error='second-send';code,result=self.invoke();self.assertNotEqual(0,code,result)
        self.assertEqual([42,43],self.patches)
        journal=json.loads((self.root/'observed.custody.json').read_text())
        self.assertEqual(['confirmed','possible-send'],[s['phase'] for s in journal['steps']])
        self.spec['pulls'][0]['campaign']='different-campaign'
        code,result=self.invoke();self.assertEqual('known-roster-custody-exists-no-resend',result['reason'])
        self.assertEqual([42,43],self.patches)

    def test_custody_failure_prevents_any_provider_write(self):
        with patch.object(lane,'save_roster_custody',side_effect=OSError('disk full')):
            code,result=self.invoke();self.assertEqual(2,code,result)
        self.assertEqual([],self.patches)

    def test_readback_unavailable_after_send_leaves_possible_custody_and_never_continues(self):
        original=self.gh
        def failed(executable,args,stdin=None):
            if self.patches and 'PATCH' not in args:raise lane.ToolError('readback unavailable')
            return original(executable,args,stdin)
        with patch.object(self,'gh',failed):
            code,result=self.invoke();self.assertNotEqual(0,code,result)
        self.assertEqual([42],self.patches)
        self.assertEqual('possible-send',json.loads((self.root/'observed.custody.json').read_text())['phase'])
        self.assertEqual('known-roster-custody-exists-no-resend',self.invoke()[1]['reason'])
        self.assertEqual([42],self.patches)

    def test_explicit_initial_continues_operation_rejects_conflicts_and_injection(self):
        p=self.pulls[42];row=self.spec['pulls'][0];f=p['body']
        result=lane.update_body(f,f,row['branch'],row['headSha'],row['headSha'],'campaign','chain42',adopt_legacy=True,declare_continues=True)
        self.assertIn('<!-- fsgg:delivery-continues/v1 -->',result)
        for proposed in [f+'<!-- fsgg:delivery-completes/v1 -->',f+'<!-- fsgg:unrelated/v1 injected=yes -->']:
            with self.assertRaises(lane.ToolError):lane.update_body(f,proposed,row['branch'],row['headSha'],row['headSha'],'campaign','chain42',adopt_legacy=True,declare_continues=True)
        with self.assertRaises(lane.ToolError):lane.update_body(f+'<!-- fsgg:delivery-completes/v1 -->',f,row['branch'],row['headSha'],row['headSha'],'campaign','chain42',adopt_legacy=True,declare_continues=True)

if __name__ == '__main__': unittest.main()

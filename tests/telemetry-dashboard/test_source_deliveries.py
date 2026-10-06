import copy
import hashlib
import json
import unittest
from unittest import mock
from test_compact_ci import compact, envelope
from test_dashboard import public_item
from test_item_projection import D, snapshot, labels
from test_dashboard import host_fixture


def gov():
    value=snapshot()
    for relation in value:
        if relation not in {'populations','outcomes'}: value[relation]=[]
    value['populations']=[{'identity':'derived-budget-population:GOV-423-C3','item_id':'GOV-423-C3','original_item_id':'GOV-423-C3','source_ref':'derived:budget-inputs','fact_revision':1,'state':'open'}]
    value['outcomes']=[{'identity':'native-item-outcome-cbccf7c54361795ec71692b1fd4a47178ed3f0f9','item_id':'GOV-423-C3','repository':'FS-GG/governance_config','pr_number':444,'occurred_at':'2026-10-05T19:00:00Z','observed_at':'2026-10-05T19:01:00Z','fact_revision':1,'outcome':'delivered','code_delivery':'delivered','private_notes':'PRIVATE SENTINEL'}]
    approved=labels(); approved['items']={'GOV-423-C3':{'key':'governance-ci','label':'Governance CI','url':'https://github.com/FS-GG/governance_config/pull/444','repositories':['FS-GG/governance_config'],'notes':[]}}
    return value,approved


class SourceDeliveryTests(unittest.TestCase):
    def project(self,value,approved):
        completed=D.project_completed_items(value,{},approved,{},{} )
        return D.project_source_deliveries(value,approved,completed)

    def test_actual_shape_open_gov_once_without_operational_or_private_claims(self):
        value,approved=gov(); result=self.project(value,approved)
        D.validate_source_deliveries(result)
        self.assertEqual(result['coverage']['published'],1)
        row=result['items'][0]
        self.assertEqual(row['deliveries'][0]['number'],444)
        self.assertEqual(row['operationalCompletion'],'unestablished')
        raw=json.dumps(result)
        for private in ('GOV-423-C3','native-item-outcome','PRIVATE SENTINEL','usage','tokens','cost'): self.assertNotIn(private,raw)
        self.assertEqual(value['expectedDispatches'],[])

    def test_canonical_gzip_build_host_routes_real_source_delivery_and_revision(self):
        value,approved=gov(); compact_value=compact()
        for name in ('ciJobs','ciSteps','ciCoverage','ciPopulationCoverage'): value.pop(name,None)
        compact_value['items']=['GOV-423-C3']; compact_value['ciSummaries'][0]['item']='GOV-423-C3'
        value.update(compact_value)
        value['summaries']=[public_item(item='GOV-423-C3',usageObservations=0,deliveryObservations=0)]
        with mock.patch.object(D,'engine_json',return_value=envelope(value)),mock.patch.object(D,'load_labels',return_value=approved):
            host=D.build_host(resolved_config={'storeRoot':'/private/store','engine':'engine'})
        D.validate_host(host)
        self.assertEqual(host['schema'],'fsgg.telemetry.dashboard-host/4')
        self.assertEqual(host['completedItems']['coverage']['published'],0)
        self.assertEqual(host['sourceDeliveries']['coverage']['published'],1)
        self.assertNotIn('PRIVATE SENTINEL',json.dumps(host))

    def test_duplicate_and_superseded_latest_do_not_duplicate_or_revive_delivery(self):
        value,approved=gov(); value['outcomes'].append(copy.deepcopy(value['outcomes'][0]))
        self.assertEqual(len(self.project(value,approved)['items'][0]['deliveries']),1)
        latest=copy.deepcopy(value['outcomes'][0]); latest.update(outcome='failed',code_delivery='not-delivered',fact_revision=2,observed_at='2026-10-05T20:01:00Z',occurred_at='2026-10-05T20:00:00Z')
        value['outcomes'].insert(0,latest)
        self.assertEqual(self.project(value,approved)['items'],[])

    def test_conflicting_original_or_latest_evidence_is_withheld(self):
        for relation,field,new in [('populations','original_item_id','PRIVATE-CONFLICT'),('outcomes','pr_number',445),('outcomes','occurred_at','2026-10-05T18:00:00Z')]:
            value,approved=gov(); conflict=copy.deepcopy(value[relation][0]); conflict[field]=new; value[relation].append(conflict)
            result=self.project(value,approved)
            self.assertEqual(result['items'],[]); self.assertEqual(result['coverage']['incompatible'],1)

    def test_dirty_unapproved_and_repo_mismatch_are_private(self):
        for mode in ('dirty','unapproved','repository'):
            value,approved=gov()
            if mode=='dirty': value['dirtyItems']=[{'item_id':'GOV-423-C3'}]
            elif mode=='unapproved': approved['items']={}
            else: value['outcomes'][0]['repository']='PRIVATE/secret'
            result=self.project(value,approved); self.assertEqual(result['items'],[])
            self.assertNotIn('PRIVATE',json.dumps(result))
            self.assertEqual(result['coverage'][{'dirty':'dirty','unapproved':'unmapped','repository':'incompatible'}[mode]],1)

    def test_five_completed_groups_stay_identical_and_are_not_republished(self):
        value=snapshot(); approved=labels()
        ci=D.snapshot_ci(value,'child'); budget=D.snapshot_budget(value,'child')
        baseline=D.project_completed_items(value,{},approved,{'child':ci},{'child':budget})
        self.assertEqual(baseline['coverage']['published'],1)
        combined={name:[] for name in value}; approvals=copy.deepcopy(approved); approvals['items']={}; cis={}; budgets={}
        for i in range(5):
            item=f'child-{i}'; original=f'ORIGINAL-{i}'
            approvals['items'][original]={**approved['items']['ORIGINAL'],'key':f'public-{i}'}
            for relation,rows in value.items():
                for source in rows:
                    row=copy.deepcopy(source)
                    if row.get('item_id')=='child': row['item_id']=item
                    if row.get('original_item_id')=='ORIGINAL': row['original_item_id']=original
                    combined[relation].append(row)
            cis[item]=ci; budgets[item]=budget
        before=D.project_completed_items(combined,{},approvals,cis,budgets)
        self.assertEqual(before['coverage']['published'],5)
        self.assertEqual(D.project_source_deliveries(combined,approvals,before)['items'],[])
        gv,ga=gov()
        for relation,rows in gv.items(): combined[relation].extend(rows)
        approvals['items'].update(ga['items'])
        after=D.project_completed_items(combined,{},approvals,cis,budgets)
        self.assertEqual(before,after)
        self.assertEqual(D.project_source_deliveries(combined,approvals,after)['coverage']['published'],1)

    def test_host4_closed_validation_revision_and_host3_backwards_read(self):
        host=host_fixture(); value,approved=gov(); host['sourceDeliveries']=self.project(value,approved)
        def seal():
            host.pop('revision',None); host['revision']=hashlib.sha256(json.dumps(host,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode()).hexdigest()
        seal(); D.validate_host(host)
        old=copy.deepcopy(host); old['schema']='fsgg.telemetry.dashboard-host/3'; old.pop('sourceDeliveries'); old.pop('revision'); old['revision']=hashlib.sha256(json.dumps(old,sort_keys=True,separators=(',',':'),ensure_ascii=True).encode()).hexdigest(); D.validate_host(old)
        old['revision']='0'*64
        with self.assertRaisesRegex(ValueError,'revision mismatch'): D.validate_host(old)
        for mutate in (lambda r:r.update(privateNotes='PRIVATE SENTINEL'),lambda r:r.update(operationalCompletion='completed'),lambda r:r['deliveries'][0].update(url='https://evil.example/')):
            original=copy.deepcopy(host); mutate(host['sourceDeliveries']['items'][0]); seal()
            with self.assertRaises(ValueError): D.validate_host(host)
            host=original
        host['sourceDeliveries']['coverage']['published']=0; seal()
        with self.assertRaises(ValueError): D.validate_host(host)

#!/usr/bin/env python3
"""Pure fake-native controls for the real guarded executable. No CLR/GitHub effects."""
import copy
import datetime as dt
import hashlib
import importlib.machinery
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
TOOL=ROOT/'scripts/project-field-options'
FAKE=ROOT/'tests/project-field-options/fake-gh'
loader=importlib.machinery.SourceFileLoader('guarded_options',str(TOOL))
spec=importlib.util.spec_from_loader(loader.name,loader)
m=importlib.util.module_from_spec(spec);loader.exec_module(m)

def write(path,value):
    path.write_text(json.dumps(value,indent=2)+'\n');path.chmod(0o600)

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def native(product):
    title,repository,repoid=m.PRODUCT_TARGETS[product]
    # Deliberately synthetic transport IDs: never native provisioning authority.
    return {'id':'fixture-project-'+product,'number':80 if product=='game' else 81,
            'title':title,'owner':{'id':'O_kgDOEYAWYw','login':'FS-GG'},
            'createdAt':'2026-10-05T00:00:00Z','closed':False,'viewerCanUpdate':True,
            'repositories':{'nodes':[{'id':repoid,'nameWithOwner':repository}],'pageInfo':{'hasNextPage':False}},
            'fields':{'nodes':[{'id':'fixture-title','name':'Title','dataType':'TITLE'},
                      {'id':'fixture-status','name':'Status','dataType':'SINGLE_SELECT',
                       'options':[{'id':'fixture-'+str(i),'name':name,'color':'GRAY','description':''}
                                  for i,name in enumerate(['Todo','In Progress','Done'])]}],
                      'pageInfo':{'hasNextPage':False}},
            'items':{'totalCount':0,'nodes':[],'pageInfo':{'hasNextPage':False}}}

class InitialStatusControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.paths={k:self.root/(k+'.json') for k in ('state','creation','snapshot','backup','admission','intent')}
        self.selected=native('game')
        self.receipt={'schema':'fsgg.product-project-creation/1','qualificationOnly':True,
                      'baseline':{'nodes':[{'id':'PVT_kwDOEYAWY84Bldpa'}],'pageInfo':{'hasNextPage':False}},'projects':{}}
        for product in m.PRODUCT_TARGETS:
            n=native(product)
            self.receipt['projects'][product]={'response':{'data':{'createProjectV2':{'projectV2':{k:n[k] for k in ('id','number','title','owner')}}}},'initial':n}
        write(self.paths['creation'],self.receipt)
        write(self.paths['snapshot'],m.seal(m.product_snapshot(self.selected)))
        write(self.paths['state'],{'native':self.selected,'remoteBackupText':self.paths['snapshot'].read_text()})
        write(self.paths['backup'],{'repository':'FS-GG/.github','revision':'a'*40,'path':'docs/coordination/fixture-snapshot.json','fileSha256':sha(self.paths['snapshot'])})
        variables={'field':'fixture-status','name':'Status','options':m.product_options(m.product_snapshot(self.selected))}
        request={'query':m.UPDATE_FIELD,'variables':variables}
        now=dt.datetime.now(dt.timezone.utc)
        self.admission={'approved':True,'owner':'root-integrator','operation':'initialize-empty-product-status','product':'game',
                        'projectId':self.selected['id'],'fieldId':'fixture-status','requestSha256':hashlib.sha256(m.canonical(request)).hexdigest(),
                        'sourceSha256':sha(TOOL),'creationReceiptSha256':sha(self.paths['creation']),
                        'snapshotSha256':sha(self.paths['snapshot']),'backupSha256':sha(self.paths['backup']),
                        'intentPath':str(self.paths['intent']),'issuedAt':now.isoformat(),'expiresAt':(now+dt.timedelta(seconds=300)).isoformat(),
                        'qualificationOnly':True,'adoptionAuthorized':False}
        write(self.paths['admission'],self.admission)
    def tearDown(self): self.temp.cleanup()
    def run_tool(self,apply=True,**env):
        args=[str(TOOL),'initialize-product-status','--product','game','--creation-receipt',str(self.paths['creation']),
              '--snapshot',str(self.paths['snapshot']),'--backup',str(self.paths['backup']),
              '--admission',str(self.paths['admission']),'--intent',str(self.paths['intent'])]
        if apply: args.append('--apply')
        return subprocess.run(args,env=dict(os.environ,PROJECT_FIELD_OPTIONS_GH=str(FAKE),PROJECT_FIELD_OPTIONS_FAKE_STATE=str(self.paths['state']),**env),capture_output=True,text=True)
    def mutations(self): return json.loads(self.paths['state'].read_text()).get('fieldMutationCount',0)
    def refuse(self,**env):
        result=self.run_tool(**env)
        self.assertNotEqual(result.returncode,0,result.stdout)
        self.assertEqual(self.mutations(),0,result.stderr)
    def test_success_and_exact_options(self):
        result=self.run_tool();self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(self.mutations(),1)
        row=json.loads(result.stdout);self.assertEqual(row['state'],'verified')
        field=next(f for f in row['native']['fields']['nodes'] if f['name']=='Status')
        self.assertEqual([o['name'] for o in field['options']],list(m.PRODUCT_STATUS))
        self.assertEqual(field['options'][0]['id'],'fixture-0')
        self.assertEqual(field['options'][2]['id'],'fixture-1')
        self.assertEqual(field['options'][4]['id'],'fixture-2')
    def test_retained_option_id_drift_refused_after_one_update(self):
        result=self.run_tool(PROJECT_FIELD_OPTIONS_FAKE_REPLACE_RETAINED_ID='1')
        self.assertNotEqual(result.returncode,0)
        self.assertIn('retained option ID changed',result.stderr)
        self.assertEqual(self.mutations(),1)
        self.assertEqual(json.loads(self.paths['intent'].read_text())['state'],'intent')
        result=self.run_tool()
        self.assertNotEqual(result.returncode,0)
        self.assertEqual(self.mutations(),1)
    def test_preview_zero_writes(self):
        result=self.run_tool(apply=False);self.assertEqual(result.returncode,0,result.stderr)
        self.assertEqual(self.mutations(),0);self.assertFalse(self.paths['intent'].exists())
    def test_lost_response_observes_without_retry(self):
        result=self.run_tool(PROJECT_FIELD_OPTIONS_FAKE_LOST_UPDATE_RESPONSE='1')
        self.assertEqual(result.returncode,0,result.stderr);self.assertEqual(self.mutations(),1)
        self.assertIsNotNone(json.loads(result.stdout)['responseGap'])
        result=self.run_tool();self.assertNotEqual(result.returncode,0);self.assertEqual(self.mutations(),1)
    def test_unknown_unapplied_outcome_never_resends(self):
        result=self.run_tool(PROJECT_FIELD_OPTIONS_FAKE_FAIL_UPDATE_BEFORE_APPLY='1')
        self.assertNotEqual(result.returncode,0);self.assertEqual(self.mutations(),0)
        self.assertTrue(self.paths['intent'].exists())
        result=self.run_tool(PROJECT_FIELD_OPTIONS_FAKE_FAIL_UPDATE_BEFORE_APPLY='1')
        self.assertNotEqual(result.returncode,0)
        self.assertEqual(json.loads(self.paths['state'].read_text())['fieldAttemptCount'],1)
    def test_sibling_identity_collision_refused(self):
        receipt=copy.deepcopy(self.receipt)
        receipt['projects']['rendering']['initial']['id']=self.selected['id']
        receipt['projects']['rendering']['response']['data']['createProjectV2']['projectV2']['id']=self.selected['id']
        write(self.paths['creation'],receipt);self.refuse()
    def test_existing_intent_refused(self):
        write(self.paths['intent'],{'state':'unknown'});self.refuse()
    def test_nonempty_foreign_owner_partial_fields_capability(self):
        for mutate in [lambda n:n['items'].update(totalCount=1),lambda n:n['owner'].update(login='other'),
                       lambda n:n['fields']['pageInfo'].update(hasNextPage=True),lambda n:n.update(viewerCanUpdate=False),
                       lambda n:n['repositories']['nodes'].clear()]:
            state=json.loads(self.paths['state'].read_text());state['native']=copy.deepcopy(self.selected);mutate(state['native'])
            write(self.paths['state'],state);self.refuse()
    def test_other_initial_field_drift(self):
        state=json.loads(self.paths['state'].read_text());state['native']['fields']['nodes'][0]['name']='Changed'
        write(self.paths['state'],state);self.refuse()
    def test_unknown_original_option_and_duplicate(self):
        for mutate in [lambda o:o[0].update(name='unexpected'),lambda o:o[0].update(id=o[1]['id'])]:
            state=json.loads(self.paths['state'].read_text());state['native']=copy.deepcopy(self.selected)
            mutate(state['native']['fields']['nodes'][1]['options']);write(self.paths['state'],state);self.refuse()
    def test_snapshot_and_remote_backup_mismatch(self):
        state=json.loads(self.paths['state'].read_text());state['remoteBackupText']='wrong';write(self.paths['state'],state);self.refuse()
    def test_different_admission_source_request_expiry_adoption(self):
        for key,value in [('sourceSha256','0'*64),('requestSha256','0'*64),('projectId','wrong'),('adoptionAuthorized',True),('expiresAt','2000-01-01T00:00:00Z')]:
            admission=dict(self.admission);admission[key]=value;write(self.paths['admission'],admission);self.refuse()
    def test_old_or_same_resource_creation_refused(self):
        for key in ['game','rendering']:
            receipt=copy.deepcopy(self.receipt)
            receipt['projects'][key]['response']['data']['createProjectV2']['projectV2']['id']='PVT_kwDOEYAWY84Bldpa'
            write(self.paths['creation'],receipt);self.refuse()
    def test_concurrent_membership_fences_settlement(self):
        result=self.run_tool(PROJECT_FIELD_OPTIONS_FAKE_ITEM_AFTER_UPDATE='1')
        self.assertNotEqual(result.returncode,0);self.assertEqual(self.mutations(),1)
        self.assertEqual(json.loads(self.paths['intent'].read_text())['state'],'intent')
    def test_private_input_required(self):
        self.paths['admission'].chmod(0o644);self.refuse()

if __name__=='__main__': unittest.main()

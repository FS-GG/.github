"""Pure readback callers and transport guards; no network/process/crypto/token mint."""
import ast,base64,copy,importlib.util,io,json,pathlib,sys,tempfile,unittest,urllib.request
from unittest.mock import patch
sys.dont_write_bytecode=True
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
import new_sdd_workspace_promote_recovery as r
from release_successor_execution import Refused
from release_successor_journal import ProtectedReleaseJournal,canonical
spec=importlib.util.spec_from_file_location('retained_recovery_controls',pathlib.Path(__file__).with_name('recovery.py'))
f=importlib.util.module_from_spec(spec);spec.loader.exec_module(f)

def binding():
 return {**r.ORIGINAL_H4,'schema':'fsgg.wizard-postcomplete-readback-binding/1','observerSource':'a'*40,'observerTree':'b'*40,'observerMain':'a'*40,'observerWorkflowSha256':'c'*64,'completedJournalHead':'e'*40,'correlation':'d'*32,'selectedAfter':'2026-10-05T12:00:00Z','recipientSha256':'f'*64}

class ReadbackTests(unittest.TestCase):
 def fixture(self):
  root=tempfile.TemporaryDirectory();self.addCleanup(root.cleanup);return f.Fixture(pathlib.Path(root.name))
 def test_binding_separates_original_from_observer(self):
  b=binding();self.assertEqual(r.readback_binding(r.canonical(b).decode()),b)
  for key,value in [('observerSource',b['originalHeldSource']),('observerMain','0'*40),('schema','other'),('originalRunId',True),('originalBindingSha256','0'*64),('correlation',b['originalCorrelation']),('selectedAfter','2026-99-99T00:00:00Z')]:
   changed={**b,key:value}
   with self.subTest(key=key),self.assertRaises((Refused,ValueError)):r.readback_binding(r.canonical(changed).decode())
  for changed in [{**b,'unknown':1},{k:v for k,v in b.items() if k!='observerTree'}]:
   with self.assertRaises(Refused):r.readback_binding(r.canonical(changed).decode())
 def test_binding_duplicate_and_oversize_refuse(self):
  with self.assertRaises(Refused):r.readback_binding('{"schema":1,"schema":2}')
  with self.assertRaises(Refused):r.readback_binding(' '*65537)
 def test_pre_request_write_refusal_no_transport_or_budget(self):
  with tempfile.TemporaryDirectory() as d:
   budget=r.Budget('diagnostic');api=r.ReadbackAPI('synthetic',budget,pathlib.Path(d))
   with patch.object(api.opener,'open') as transport:
    for method,body in [('POST',None),('PATCH',{}),('DELETE',None),('PUT',{}),('GET',{}),('GET',b'')]:
     with self.subTest(method=method,body=body),self.assertRaises(Refused):api.request('https://api.github.com/repos/FS-GG/.github',method=method,body=body)
    for method in ['post','patch','arm_settlement']:
     with self.assertRaises(Refused):getattr(api,method)('x',{})
    with self.assertRaises(Refused):api.request('https://api.github.com/repos/FS-GG/.github',headers={'X-HTTP-Method-Override':'POST'})
    transport.assert_not_called();self.assertEqual(budget.reads,0)
 def test_actual_transport_rejects_legacy_class_alias(self):
  class Opener:
   def __init__(self):self.calls=[]
   def open(self,*a,**kw):self.calls.append(a);raise AssertionError('transport called')
  with tempfile.TemporaryDirectory() as d:
   api=r.ReadbackAPI('synthetic',r.Budget('diagnostic'),pathlib.Path(d));opener=Opener();api.opener=r.ReadbackTransport(opener)
   for method,body in [('POST',{}),('PATCH',{}),('GET',{})]:
    with self.assertRaises(Refused):r.FiniteAPI.request(api,'https://api.github.com/repos/FS-GG/.github',method=method,body=body)
   self.assertEqual(opener.calls,[])
 def test_transport_requires_literal_get_body_none(self):
  class Opener:
   def __init__(self):self.calls=[]
   def open(self,*a,**kw):self.calls.append(a);return 'synthetic-response'
  o=Opener();guard=r.ReadbackTransport(o)
  self.assertEqual(guard.open(urllib.request.Request('https://api.github.com',method='GET'),timeout=1),'synthetic-response')
  for method,data in [('POST',None),('GET',b''),('PATCH',b'x')]:
   with self.assertRaises(Refused):guard.open(urllib.request.Request('https://api.github.com',method=method,data=data),timeout=1)
  with self.assertRaises(Refused):guard.open('https://api.github.com',timeout=1)
  with self.assertRaises(Refused):guard.open(urllib.request.Request('https://api.github.com',method='GET',headers={'X-HTTP-Method-Override':'POST'}),timeout=1)
  self.assertEqual(len(o.calls),1)
 def test_redirect_get_body_none_and_authorization_removal(self):
  redirect=r.ReadbackRedirect(r.Budget('diagnostic'))
  req=urllib.request.Request('https://api.github.com/repos/FS-GG/.github/releases/assets/1',method='GET',headers={'Authorization':'synthetic'})
  target=redirect.redirect_request(req,None,302,'synthetic',{},'https://release-assets.githubusercontent.com/synthetic')
  self.assertEqual(target.get_method(),'GET');self.assertIsNone(target.data);self.assertFalse(target.has_header('Authorization'))
  for method,data in [('POST',None),('GET',b'')]:
   with self.assertRaises(Refused):redirect.redirect_request(urllib.request.Request('https://api.github.com',method=method,data=data),None,302,'synthetic',{},'https://api.github.com/other')
  with self.assertRaises(Refused):redirect.redirect_request(req,None,302,'synthetic',{},'https://foreign.invalid/')
 def test_full17_reader_never_constructs_recovery_or_installs(self):
  fixture=self.fixture();last=copy.deepcopy(fixture.nodes[fixture.head]['state']);last['generation']=17;last['effects']['promote']='verified'
  fixture.nodes['e'*40]={'state':last,'parent':fixture.head};fixture.head='e'*40;fixture.release['draft']=False
  b=binding();original_gate=r.release_gate;gate_calls=[]
  def gate(*args):gate_calls.append(args);return fixture.release,{'github':fixture.root/'github.nupkg','nuget':fixture.root/'nuget.nupkg'}
  for name in ['github','nuget']:(fixture.root/(name+'.nupkg')).write_bytes(b'synthetic-'+name.encode())
  journal=ProtectedReleaseJournal(f.FakeAPI(fixture,True),r.REF)
  with patch.object(r,'readback_native') as native,patch.object(r,'release_gate',side_effect=gate),patch.object(r,'Recovery',side_effect=AssertionError('recovery forbidden')),patch.object(r,'public_install',side_effect=AssertionError('install forbidden')):
   report=r.post_complete_readback(fixture.api,journal,b,fixture.manifest,fixture.original,99)
  self.assertEqual(len(gate_calls),2);self.assertEqual(native.call_count,2);self.assertEqual(journal._lineage_length,17)
  self.assertEqual(report['generation'],17);self.assertFalse(report['newInstalledQualification']);self.assertFalse(report['publicWizardQualified']);self.assertFalse(report['adoptionReceiptEmitted']);self.assertEqual(fixture.patches,0);self.assertEqual(fixture.cas,0);self.assertEqual(fixture.installs,0)
  self.assertIs(r.release_gate,original_gate)
 def test_public_draft_and_release_feed_drift_stay_refusals(self):
  for mutation in ['draft','release','feed']:
   fixture=self.fixture();last=copy.deepcopy(fixture.nodes[fixture.head]['state']);last['generation']=17;last['effects']['promote']='verified';fixture.nodes['e'*40]={'state':last,'parent':fixture.head};fixture.head='e'*40
   first=fixture.root/'first';second=fixture.root/'second';first.write_bytes(b'original');second.write_bytes(b'changed')
   release={**fixture.release,'draft':mutation=='draft'};calls=[]
   def gate(*args):
    calls.append(1)
    return ({**release,'name':'changed'} if mutation=='release' and len(calls)>1 else release),{'github':second if mutation=='feed' and len(calls)>1 else first,'nuget':first}
   journal=ProtectedReleaseJournal(f.FakeAPI(fixture,True),r.REF)
   with patch.object(r,'readback_native'),patch.object(r,'release_gate',side_effect=gate),self.assertRaises(Refused):r.post_complete_readback(fixture.api,journal,binding(),fixture.manifest,fixture.original,99)
 def test_generation16_and_wrong17_head_refuse_before_feeds(self):
  for settled in [False,True]:
   fixture=self.fixture();b=binding()
   if settled:
    last=copy.deepcopy(fixture.nodes[fixture.head]['state']);last['generation']=17;last['effects']['promote']='verified';fixture.nodes['f'*40]={'state':last,'parent':fixture.head};fixture.head='f'*40
   journal=ProtectedReleaseJournal(f.FakeAPI(fixture,True),r.REF)
   with patch.object(r,'readback_native'),patch.object(r,'release_gate') as gate,self.assertRaises(Refused):r.post_complete_readback(fixture.api,journal,b,fixture.manifest,fixture.original,99)
   gate.assert_not_called()
 def test_legacy_diagnostic_still_requires16(self):
  fixture=self.fixture();last=copy.deepcopy(fixture.nodes[fixture.head]['state']);last['generation']=17;last['effects']['promote']='verified';fixture.nodes['e'*40]={'state':last,'parent':fixture.head};fixture.head='e'*40
  with self.assertRaises(Refused):fixture.engine().state()
  with self.assertRaises(Refused):r.validate_binding(binding(),r.READBACK_MODE)
 def test_actual_full17_primer_and_journal_crypto_join(self):
  fixture=self.fixture();last=copy.deepcopy(fixture.nodes[fixture.head]['state']);last['generation']=17;last['effects']['promote']='verified';fixture.nodes['e'*40]={'state':last,'parent':fixture.head};fixture.head='e'*40
  commits=[];contents={}
  oid=fixture.head
  while oid:
   node=fixture.nodes[oid];raw=canonical(node['state']);blob=r.hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest();tree_raw=b'100644 release-state.json\0'+bytes.fromhex(blob);tree=r.hashlib.sha1(b'tree '+str(len(tree_raw)).encode()+b'\0'+tree_raw).hexdigest()
   commits.append({'sha':oid,'parents':[{'sha':node['parent']}] if node['parent'] else [],'commit':{'tree':{'sha':tree}}});contents[oid]={'type':'file','path':'release-state.json','encoding':'base64','sha':blob,'content':base64.b64encode(raw).decode()};oid=node['parent']
  with tempfile.TemporaryDirectory() as root:
   api=r.ReadbackAPI('synthetic',r.Budget('diagnostic'),pathlib.Path(root),authority=True)
   def get(path):
    if '/commits?' in path:return copy.deepcopy(commits)
    if '/contents/' in path:return copy.deepcopy(contents[path.rsplit('=',1)[1]])
    if '/git/ref/' in path:return {'object':{'sha':fixture.head}}
    if path in api.immutable:return api.immutable[path]
    return {'id':1351660651,'full_name':r.AUTHORITY}
   with patch.object(api,'get',side_effect=get):
    api.prime_completed_journal(fixture.head);journal=ProtectedReleaseJournal(api,r.REF);state=journal.read()
   self.assertEqual(state.generation,17);self.assertEqual(journal._lineage_length,17)
   for mutation in ['missing','parent','blob','tree']:
    original_commits=copy.deepcopy(commits);original_contents=copy.deepcopy(contents)
    if mutation=='missing':commits.pop()
    elif mutation=='parent':commits[1]['sha']='f'*40
    elif mutation=='blob':contents[fixture.head]['sha']='0'*40
    else:commits[0]['commit']['tree']['sha']='0'*40
    with patch.object(api,'get',side_effect=get),self.assertRaises(Refused):api.prime_completed_journal(fixture.head)
    commits[:]=original_commits;contents.clear();contents.update(original_contents)
 def test_actual_native_metadata_actor_workflow_and_current_main(self):
  b=binding();workflow=b'controlled observer workflow';b['observerWorkflowSha256']=r.digest(workflow)
  observer={'id':99,'display_title':f"Wizard 0.13 recovery {r.READBACK_MODE} {b['correlation']} {r.digest(r.canonical(b))}",'head_sha':b['observerSource'],'run_attempt':1,'status':'in_progress','path':r.WORKFLOW,'event':'workflow_dispatch','head_branch':'main','actor':{'login':'EHotwagner'},'repository':{'id':1269292704}}
  original={**observer,'id':r.ORIGINAL_H4['originalRunId'],'display_title':f"Wizard 0.13 recovery complete {r.ORIGINAL_H4['originalCorrelation']} {r.ORIGINAL_H4['originalBindingSha256']}",'head_sha':r.ORIGINAL_H4['originalHeldSource'],'status':'completed','conclusion':'success'}
  artifact={'id':r.ORIGINAL_H4['originalArtifactId'],'expired':False,'digest':'sha256:'+r.ORIGINAL_H4['originalArchiveSha256'],'name':f"wizard013-recovery-complete-{r.ORIGINAL_H4['originalRunId']}-{r.ORIGINAL_H4['originalCorrelation']}",'workflow_run':{'id':r.ORIGINAL_H4['originalRunId'],'head_sha':r.ORIGINAL_H4['originalHeldSource'],'repository_id':1269292704,'head_repository_id':1269292704}}
  paths=[]
  class API:
   def get(self,path):
    paths.append(path)
    if '/actions/workflows/' in path:return {'workflow_runs':[observer]}
    if path.endswith('/actions/runs/99'):return observer
    if '/actions/runs/' in path:return original
    if '/actions/artifacts/' in path:return artifact
    if '/git/ref/' in path:return {'object':{'sha':b['observerMain']}}
    if '/git/commits/' in path:return {'sha':b['observerSource'],'tree':{'sha':b['observerTree']}}
    if '/contents/' in path:return {'type':'file','path':r.WORKFLOW,'encoding':'base64','content':base64.b64encode(workflow).decode()}
    return {'id':1269292704}
  r.readback_native(API(),b,99);self.assertFalse(any('/user' in path for path in paths))
  for key,value in [('actor',{'login':'foreign'}),('path','other.yml'),('run_attempt',2),('head_sha','f'*40),('status','completed')]:
   old=observer[key];observer[key]=value
   with self.assertRaises(Refused):r.readback_native(API(),b,99)
   observer[key]=old
  artifact['expired']=True
  with self.assertRaises(Refused):r.readback_native(API(),b,99)
 def test_original_deadline_is_not_renewed(self):
  budget=r.Budget('diagnostic',clock=lambda:580,start=100)
  self.assertEqual(budget.end,700);self.assertEqual(budget.work,580)
  with self.assertRaises(Refused):budget.read()
  budget.reserve=True;self.assertEqual(budget.remaining(25,True),25)
 def test_worker_explicit_selection_refuses_before_effects(self):
  with patch.dict(r.os.environ,{'PROMOTION_RECOVERY_MODE':'diagnostic'},clear=True),patch.object(r,'Runner') as runner,self.assertRaises(Refused):r.readback_worker(binding(),pathlib.Path('/synthetic'),pathlib.Path('/synthetic'),0)
  runner.assert_not_called()
 def test_actual_worker_source_only_callable_preserves_new_scope(self):
  b=binding();env={'PROMOTION_RECOVERY_MODE':r.READBACK_MODE,'GITHUB_EVENT_NAME':'workflow_dispatch','GITHUB_RUN_ATTEMPT':'1','GITHUB_REPOSITORY':r.REPO,'GITHUB_REF':'refs/heads/main','GITHUB_ACTOR':'EHotwagner','GITHUB_SHA':b['observerSource'],'RECOVERY_BINDING_SHA256':r.digest(r.canonical(b)),'RECOVERY_CORRELATION':b['correlation'],'GITHUB_RUN_ID':'99'}
  class Runner:
   def __init__(self,budget,root):self.records=[];self.budget=budget
   def run(self,*args,**kwargs):raise AssertionError('unexpected native child')
  class API:
   def prime_completed_journal(self,head):self.head=head
  with tempfile.TemporaryDirectory() as d:
   root=pathlib.Path(d);(root/r.WORKFLOW).parent.mkdir(parents=True);(root/r.WORKFLOW).write_bytes(b'workflow');b['observerWorkflowSha256']=r.digest(b'workflow');env['RECOVERY_BINDING_SHA256']=r.digest(r.canonical(b))
   with patch.dict(r.os.environ,env,clear=True),patch.object(r,'subreaper'),patch.object(r,'Runner',Runner),patch.object(r,'source_snapshot',return_value={'synthetic':'roster'}) as snapshots,patch.object(r,'child_environment',return_value={}),patch.object(r,'ReadbackAPI',side_effect=lambda *a,**k:API()),patch.object(r,'candidate',return_value=({}, {}, root)),patch.object(r,'ProtectedReleaseJournal'),patch.object(r,'post_complete_readback',return_value={'scope':r.READBACK_MODE,'publicWizardQualified':False,'newInstalledQualification':False}),patch.object(r,'Recovery',side_effect=AssertionError('recovery forbidden')),patch.object(r,'public_install',side_effect=AssertionError('install forbidden')):
    self.assertEqual(r.readback_worker(b,root,root,r.time.monotonic()),0)
   report=json.loads((root/'worker-result.json').read_bytes());self.assertTrue(report['success']);self.assertFalse(report['newInstalledQualification']);self.assertEqual(snapshots.call_count,2);self.assertEqual(report['commandCount'],0)
 def test_workflow_reuses_only_existing_read_grants(self):
  p=pathlib.Path(__file__).resolve().parents[2]/'.github/workflows/release-new-sdd-workspace.yml';s=p.read_text();block=s[s.index('  recovery-diagnostic:'):s.index('  recovery-complete:')]
  self.assertIn('post-completion-readback',block);self.assertIn('packages: read',block);self.assertIn('permission-contents: read',block)
  self.assertNotIn('packages: write',block);self.assertNotIn('permission-contents: write',block);self.assertNotIn('api.get("user")',pathlib.Path(r.__file__).read_text())

if __name__=='__main__':unittest.main()

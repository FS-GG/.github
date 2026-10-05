"""Pure production recovery callers. Synthetic bodies/processes; no SDK/network/crypto."""
import base64,copy,hashlib,io,json,os,pathlib,subprocess,sys,tempfile,unittest,zipfile
from types import SimpleNamespace
from unittest.mock import patch
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
import new_sdd_workspace_promote_recovery as r
from release_successor_journal import ProtectedReleaseJournal,canonical,SCHEMA,REPOSITORY as AUTHORITY,Refused as JournalRefused
from release_successor_execution import Refused

def synthetic_ancestry(held,status="ahead",total=51):
 return {'url':r.ancestry_url(held,False),'base_commit':{'sha':r.CANDIDATE_SOURCE},'merge_base_commit':{'sha':r.CANDIDATE_SOURCE},'status':status,'behind_by':0,'ahead_by':total,'total_commits':total,'commits':[{'sha':'a'*40}]}

class FakeAPI:
 def __init__(self,fixture,authority=False):self.f=fixture;self.authority=authority;self.token='synthetic-native-token'
 def get(self,path):
  self.f.budget.read();self.f.paths.append(('GET',path))
  if self.authority:
   if path==f'repos/{AUTHORITY}':return {'id':1351660651,'full_name':AUTHORITY}
   if '/git/ref/' in path:return {'object':{'sha':self.f.head}}
   if '/git/commits/' in path:
    oid=path.rsplit('/',1)[1];node=self.f.nodes[oid];return {'sha':oid,'tree':{'sha':oid},'parents':[{'sha':node['parent']}] if node['parent'] else []}
   if '/git/trees/' in path:return {'tree':[{'path':'release-state.json','sha':path.rsplit('/',1)[1]}]}
   if '/git/blobs/' in path:
    oid=path.rsplit('/',1)[1];return {'sha':oid,'encoding':'base64','content':base64.b64encode(canonical(self.f.nodes[oid]['state'])).decode()}
  if path==f'repos/{r.REPO}':return {'id':1269292704,'full_name':r.REPO}
  if '/actions/workflows/' in path:return {'total_count':1,'workflow_runs':[self.f.native] * self.f.matches}
  if path.endswith('/actions/artifacts/97'):return self.f.readinessartifact
  if '/actions/runs/' in path:
   ident=int(path.rsplit('/',1)[1]);return copy.deepcopy(self.f.native if ident==99 else self.f.failed if ident==r.FAILED_RUN else self.f.prior)
  if '/compare/' in path:return synthetic_ancestry(self.f.main,self.f.ancestry)
  if path.endswith('git/ref/heads/main'):return {'object':{'sha':self.f.main}}
  if path.endswith('git/ref/tags/'+r.TAG):return {'object':{'sha':r.CANDIDATE_SOURCE}}
  if path.endswith('git/ref/tags/new-sdd-workspace/v0.12.0'):return {'object':{'sha':r.PREVIOUS_SOURCE}}
  if path.endswith('/releases/12'):return {'id':12,'tag_name':'new-sdd-workspace/v0.12.0','draft':False,'prerelease':False}
  if path.endswith('/releases/12/assets?per_page=100'):return [{'name':'previous.json','id':12}]
  if path.endswith(f'/releases/{r.RELEASE}/assets?per_page=100'):return copy.deepcopy(self.f.assets)
  if path.endswith(f'/releases/{r.RELEASE}'):
   self.f.release_gets+=1
   if self.f.drift_at==self.f.release_gets:return {**self.f.release,'body':'drift'}
   return copy.deepcopy(self.f.release)
  raise AssertionError(path)
 def request(self,url,method='GET',body=None,binary=False,headers=None,custody=False):
  assert method=='GET';self.f.budget.read();self.f.paths.append(('GET',url));data=None
  if url.endswith('/actions/artifacts/97/zip'):data=self.f.readinesszip
  elif url.endswith('/assets/12'):data=b'previous'
  elif '/releases/assets/' in url:data=self.f.assetbytes[int(url.rsplit('/',1)[1])]
  elif 'nuget.pkg.github.com' in url:data=self.f.originalzip
  elif 'api.nuget.org' in url:data=self.f.signedzip
  if self.f.feed_unknown and ('api.nuget.org' in url or 'nuget.pkg.github.com' in url):raise Refused('unknown feed')
  assert data is not None,url
  path=self.f.root/f'response-github-{len(self.f.paths)}.raw';path.write_bytes(data)
  if custody:
   rows=[{} for _ in range(len(self.f.paths)+1)];rows[-1]={'method':'GET','origin':'api.github.com','status':200,'path':f'/repos/{r.REPO}/actions/artifacts/97/zip','sha256':r.digest(data),'bytes':len(data)}
   (self.f.root/'transport-github.json').write_bytes(r.canonical(rows))
  return path
 def patch(self,path,body):
  self.f.paths.append(('PATCH',path,body));self.f.patches+=1
  assert path==f'repos/{r.REPO}/releases/{r.RELEASE}' and body=={'draft':False,'make_latest':'false'}
  if self.f.patch_visible:self.f.release['draft']=False
  if self.f.patch_unknown:raise OSError('unknown')
  return {'ignored':'response'}

class Journal(ProtectedReleaseJournal):
 def compare_and_swap(self,current,effect,state):
  self.api.f.cas+=1
  if self.api.f.cas_conflict:return False
  state={**self._observed.state,'generation':17,'effects':{**self._observed.state['effects'],'promote':'verified'}}
  self.api.f.nodes['f'*40]={'state':state,'parent':self.api.f.head};self.api.f.head='f'*40
  if self.api.f.cas_unknown:raise OSError('unknown')
  self.read();return True

class Fixture:
 def __init__(self,root,mode='complete'):
  self.root=root;self.mode=mode;self.budget=r.Budget(mode,clock=lambda:0);self.paths=[];self.main='d'*40;self.matches=1;self.ancestry='ahead';self.release_gets=0;self.drift_at=None
  self.feed_unknown=False;self.patch_visible=True;self.patch_unknown=False;self.cas_conflict=False;self.cas_unknown=False;self.patches=0;self.cas=0;self.installs=0
  self.manifest={'schema':'fsgg.new-sdd-workspace-release/1','packageId':r.PACKAGE,'version':r.VERSION,'tag':r.TAG,'sourceSha':r.CANDIDATE_SOURCE,'archiveSha256':'b'*64,'producerPayloadSha256':'sha256:'+'c'*64}
  self.cid,self.effects=r.effects(self.manifest,binding=r.HISTORICAL_013)
  self.binding={'heldSource':self.main,'heldTree':'e'*40,'journalHead':r.JOURNAL_HEAD,'releaseId':r.RELEASE,'failedRunId':r.FAILED_RUN,'correlation':'1'*32,'selectedAfter':'2026-10-04T00:00:00Z','priorRunIds':[98] if mode=='complete' else [],'predecessorReleaseId':12,'predecessorAssets':{'previous.json':r.digest(b'previous')},'home':os.environ.get('HOME'),'recipientSha256':'2'*64,'readinessRunId':None,'readinessArtifactId':None,'readinessArchiveSha256':None,'readinessBindingSha256':None,'readinessCiphertextSha256':None,'readinessCorrelation':None}
  self.native={'id':99,'display_title':f'Wizard 0.13 recovery {mode} '+self.binding['correlation']+' '+r.digest(r.canonical(self.binding)),'head_sha':self.main,'run_attempt':1,'event':'workflow_dispatch','head_branch':'main','path':r.WORKFLOW,'actor':{'login':'EHotwagner'},'status':'in_progress','repository':{'id':1269292704}}
  self.failed={**self.native,'id':r.FAILED_RUN,'head_sha':r.CANDIDATE_SOURCE,'status':'completed','conclusion':'failure'};self.prior={**self.native,'id':98,'status':'completed','conclusion':'success'}
  self.original={'tools/net10.0/any/x':b'original'}
  def zipped(members):
   stream=io.BytesIO()
   with zipfile.ZipFile(stream,'w') as z:
    for name,raw in members.items():z.writestr(name,raw)
   return stream.getvalue()
  summary={'schema':'fsgg.wizard-recovery-encrypted-custody/1','ciphertextSha256':r.digest(b'synthetic-ciphertext'),'bindingSha256':'3'*64,'nativeRunId':98,'commandExit':0}
  self.readinesszip=zipped({'custody.cms':b'synthetic-ciphertext','summary.json':r.canonical(summary)})
  self.readinessartifact={'id':97,'expired':False,'name':'wizard013-recovery-diagnostic-98-'+'4'*32,'digest':'sha256:'+r.digest(self.readinesszip),'workflow_run':{'id':98,'head_sha':self.main,'repository_id':1269292704,'head_repository_id':1269292704}}
  self.prior['display_title']='Wizard 0.13 recovery diagnostic '+'4'*32+' '+'3'*64
  if mode=='complete':self.binding.update({'readinessRunId':98,'readinessArtifactId':97,'readinessArchiveSha256':r.digest(self.readinesszip),'readinessBindingSha256':'3'*64,'readinessCiphertextSha256':r.digest(b'synthetic-ciphertext'),'readinessCorrelation':'4'*32})
  self.native['display_title']=f'Wizard 0.13 recovery {mode} '+self.binding['correlation']+' '+r.digest(r.canonical(self.binding))
  self.originalzip=zipped(self.original);self.signedzip=zipped({**self.original,'.signature.p7s':b'synthetic-signature'})
  self.release={'id':r.RELEASE,'tag_name':r.TAG,'draft':True,'prerelease':False,'body':'new-sdd-workspace-successor:'+self.cid,'name':f'{r.PACKAGE} {r.VERSION}','target_commitish':r.CANDIDATE_SOURCE}
  remote={'schema':'fsgg.new-sdd-workspace-release-journal/v1','manifestSha256':self.cid,'observations':{feed:{'archiveSha256':r.digest(raw),'payloadSha256':self.manifest['producerPayloadSha256'],'producerPayloadEqual':True} for feed,raw in [('github',self.originalzip),('nuget',self.signedzip)]}}
  self.assetbytes={608700537:self.originalzip,608701085:r.canonical(self.manifest)+b'\n',608701711:r.canonical(remote)}
  self.assets=[{'name':name,'id':ident,'size':len(self.assetbytes[ident])} for name,ident in [(f'{r.PACKAGE}.{r.VERSION}.nupkg',608700537),('manifest.json',608701085),('publication-journal.json',608701711)]]
  self.nodes={};parent=None;state={'schema':SCHEMA,'contentId':self.cid,'sourceSha':r.CANDIDATE_SOURCE,'version':r.VERSION,'candidateArchiveSha256':r.ARCHIVE,'operator':'EHotwagner','generation':1,'effects':{}}
  for generation in range(1,17):
   if generation>1:
    index=(generation-2)//2;phase='intent' if generation%2==0 else 'verified';state={**state,'generation':generation,'effects':{**state['effects'],self.effects[index].identity:phase}}
   oid=r.JOURNAL_HEAD if generation==16 else f'{generation:040x}';self.nodes[oid]={'state':copy.deepcopy(state),'parent':parent};parent=oid
  self.head=parent;self.api=FakeAPI(self);self.journal=Journal(FakeAPI(self,True),r.REF)
  self.admission=r.WizardAdmission(self.api,self.manifest,self.main,99,'EHotwagner','refs/heads/main',release_binding=r.HISTORICAL_013)
 def install(self,root,nuget):self.installs+=1;return {'synthetic':True}
 def engine(self):return r.Recovery(self.api,self.journal,self.admission,self.binding,self.manifest,self.original,99,self.mode,self.root,self.install)
 def run(self):
  # Only immutable known hashes differ for synthetic package/journal bodies.
  # Production behavior, guards, real full journal lineage and callers remain active.
  expected_original=r.digest(self.originalzip);expected_journal=r.digest(self.assetbytes[608701711]);real_digest=r.digest
  def synthetic_digest(raw):
   return '2e1d16a87400d8246cd31237b63e6366025619f26cb81742310de9a8426304c2' if raw==self.assetbytes[608701711] else real_digest(raw)
  with patch.object(r,'ORIGINAL_PACKAGE',expected_original),patch.object(r,'digest',synthetic_digest):return self.engine().run()

class RecoveryTests(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
 def tearDown(self):self.tmp.cleanup()
 def test_actual_complete_caller_full_lineage_fits_budget(self):
  f=Fixture(self.root);result=f.run();self.assertTrue(result['publisherComplete']);self.assertEqual((f.patches,f.cas,f.installs),(1,1,2));self.assertLessEqual(f.budget.reads,120);self.assertEqual(f.journal.read().generation,17)
 def test_diagnostic_caller_has_no_mutation(self):
  f=Fixture(self.root,'diagnostic');result=f.run();self.assertEqual((f.patches,f.cas,f.installs),(0,0,1));self.assertFalse(result['publisherComplete']);self.assertEqual(f.journal.read().generation,16)
 def test_already_public_settles_without_patch_second_install(self):
  f=Fixture(self.root);f.release['draft']=False;f.run();self.assertEqual((f.patches,f.cas,f.installs),(0,1,1))
 def test_unknown_patch_matched_readback_can_settle_no_resend(self):
  f=Fixture(self.root);f.patch_unknown=True;self.assertTrue(f.run()['patchResponseUnknown']);self.assertEqual(f.patches,1)
 def test_unknown_patch_unresolved_refuses_no_resend(self):
  f=Fixture(self.root);f.patch_unknown=True;f.patch_visible=False
  with self.assertRaises(Refused):f.run()
  self.assertEqual((f.patches,f.cas),(1,0));self.assertEqual(f.head,r.JOURNAL_HEAD)
 def test_cas_conflict_does_not_repeat_patch(self):
  f=Fixture(self.root);f.cas_conflict=True
  with self.assertRaises(Refused):f.run()
  self.assertEqual((f.patches,f.cas),(1,1))
 def test_unknown_cas_matched_independent_readback(self):
  f=Fixture(self.root);f.cas_unknown=True;self.assertTrue(f.run()['casResponseUnknown']);self.assertEqual((f.patches,f.cas),(1,1))
 def test_all_native_identity_mutants_refuse(self):
  for field,value in [('id',100),('head_sha','f'*40),('run_attempt',2),('event','push'),('head_branch','other'),('path','other.yml'),('actor',{'login':'other'}),('status','completed'),('repository',{'id':1})]:
   with self.subTest(field=field):
    f=Fixture(self.root);f.native[field]=value
    with self.assertRaises(Refused):f.run()
    self.assertEqual((f.patches,f.cas),(0,0))
 def test_duplicate_missing_correlation_and_bad_ancestry(self):
  for field,value in [('matches',2),('matches',0),('ancestry','diverged'),('main','f'*40)]:
   f=Fixture(self.root);setattr(f,field,value)
   with self.assertRaises(Refused):f.run()
   self.assertEqual((f.patches,f.cas),(0,0))
 def test_failed_run_drift_and_intervening_run_not_terminal(self):
  for row,field,value in [('failed','conclusion','success'),('failed','head_sha','e'*40),('failed','run_attempt',2),('prior','status','in_progress')]:
   f=Fixture(self.root);getattr(f,row)[field]=value
   with self.assertRaises(Refused):f.run()
   self.assertEqual(f.patches,0)
 def test_journal_drift_unknown_effect_and_fixed_intent(self):
  for change in ['source','generation','effect','head','lineage']:
   f=Fixture(self.root)
   if change=='source':f.nodes[f.head]['state']['sourceSha']='e'*40
   if change=='generation':f.nodes[f.head]['state']['generation']=15
   if change=='effect':f.nodes[f.head]['state']['effects']['github']='intent'
   if change=='head':f.binding['journalHead']='f'*40
   if change=='lineage':f.nodes[f.head]['parent']=None
   with self.assertRaises(Exception):f.run()
   self.assertEqual(f.patches,0)
 def test_missing_excess_duplicate_or_drifted_asset(self):
  for mutation in ('missing','excess','duplicate','size','bytes'):
   f=Fixture(self.root)
   if mutation=='missing':f.assets.pop()
   if mutation=='excess':f.assets.append({'name':'extra','id':5})
   if mutation=='duplicate':f.assets[1]['name']=f.assets[0]['name']
   if mutation=='size':f.assets[0]['size']+=1
   if mutation=='bytes':f.assetbytes[608701085]=b'changed';f.assets[1]['size']=len(b'changed')
   with self.assertRaises(Exception):f.run()
   self.assertEqual(f.patches,0)
 def test_feed_unknown_or_payload_drift_refuses(self):
  for mutation in ('unknown','payload'):
   f=Fixture(self.root)
   if mutation=='unknown':f.feed_unknown=True
   else:f.original['tools/net10.0/any/x']=b'drift'
   with self.assertRaises(Refused):f.run()
   self.assertEqual(f.patches,0)
 def test_false_admission_and_trust_install_failure(self):
  for mutation in ('admission','install'):
   f=Fixture(self.root)
   if mutation=='admission':f.admission.authorize_recovery=lambda *args:False
   else:f.install=lambda *args:(_ for _ in ()).throw(Refused('normal trust failure'))
   with self.assertRaises(Refused):f.run()
   self.assertEqual(f.patches,0)
 def test_immediate_draft_drift_prevents_patch(self):
  f=Fixture(self.root);f.drift_at=2
  with self.assertRaises(Refused):f.run()
  self.assertEqual(f.patches,0)
 def test_no_actual_subprocess_or_network_from_pure_suite(self):
  with patch.object(subprocess,'Popen',side_effect=AssertionError('real process forbidden')),patch.object(r.urllib.request.OpenerDirector,'open',side_effect=AssertionError('real network forbidden')):
   self.assertTrue(Fixture(self.root).run()['publisherComplete'])

class FiniteControls(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
 def tearDown(self):self.tmp.cleanup()
 def test_budget_counts_clips_and_reserves(self):
  now=[0];b=r.Budget('diagnostic',clock=lambda:now[0])
  for _ in range(120):b.read()
  with self.assertRaises(Refused):b.read()
  b=r.Budget('diagnostic',clock=lambda:now[0]);now[0]=479;self.assertEqual(b.remaining(25),1)
  now[0]=480
  with self.assertRaises(Refused):b.command(10)
  b.reserve=True;self.assertEqual(b.command(10),10)
 def test_full_sixteen_immutable_objects_cache_and_tree_mutant(self):
  f=Fixture(self.root);responses={};commits=[]
  for oid in reversed(list(f.nodes)):
   node=f.nodes[oid];raw=canonical(node['state']);blob=hashlib.sha1(b'blob '+str(len(raw)).encode()+b'\0'+raw).hexdigest();tree_raw=b'100644 release-state.json\0'+bytes.fromhex(blob);tree=hashlib.sha1(b'tree '+str(len(tree_raw)).encode()+b'\0'+tree_raw).hexdigest()
   commits.append({'sha':oid,'parents':[{'sha':node['parent']}] if node['parent'] else [],'commit':{'tree':{'sha':tree}}})
   responses[f'/contents/release-state.json?ref={oid}']={'type':'file','path':'release-state.json','encoding':'base64','content':base64.b64encode(raw).decode(),'sha':blob}
  responses[f'/commits?sha={r.JOURNAL_HEAD}&per_page=100']=commits
  def request(url,*args,**kwargs):
   budget.read();return copy.deepcopy(responses[url.split(f'repos/{AUTHORITY}',1)[1]])
  budget=r.Budget('diagnostic',clock=lambda:0);api=r.FiniteAPI('mock',budget,self.root,authority=True)
  with patch.object(api,'request',side_effect=request):api.prime_journal()
  self.assertEqual(budget.reads,17);self.assertEqual(len(api.immutable),48)
  path=f'repos/{AUTHORITY}/git/commits/{r.JOURNAL_HEAD}';api.get(path);api.get(path);self.assertEqual(budget.reads,17)
  commits[0]['commit']['tree']['sha']='0'*40
  api=r.FiniteAPI('mock',r.Budget('diagnostic',clock=lambda:0),self.root,authority=True)
  with patch.object(api,'request',side_effect=request),self.assertRaises(Refused):api.prime_journal()
 def test_transport_body_cap_and_unsafe_origins(self):
  b=r.Budget('diagnostic',clock=lambda:0);api=r.FiniteAPI('mock',b,self.root)
  class Response(io.BytesIO):
   status=200
   def __enter__(self):return self
   def __exit__(self,*args):self.close()
  api.opener=SimpleNamespace(open=lambda *args,**kwargs:Response(b'x'*(r.JSON_CAP+1)))
  with self.assertRaises(Refused):api.get(f'repos/{r.REPO}')
  for url in ('http://api.github.com/a','https://evil.example/a','https://user:secret@api.github.com/a'):
   with self.assertRaises(Refused):api.request(url)
 def test_closed_write_allowlist_and_diagnostic_structural_refusal(self):
  for mode in ('diagnostic','complete'):
   api=r.FiniteAPI('mock',r.Budget(mode,clock=lambda:0),self.root,authority=True)
   with self.assertRaises(Refused):api.post(f'repos/{AUTHORITY}/git/refs',{'anything':'new'})
   with self.assertRaises(Refused):api.patch(f'repos/{AUTHORITY}/git/refs/other',{'force':True,'sha':'f'*40})
  api=r.FiniteAPI('mock',r.Budget('diagnostic',clock=lambda:0),self.root)
  with self.assertRaises(Refused):api.patch(f'repos/{r.REPO}/releases/{r.RELEASE}',{'draft':False,'make_latest':'false'})
 def test_redirect_strips_cross_origin_credentials_counts_and_refuses_write(self):
  b=r.Budget('diagnostic',clock=lambda:0);handler=r.Redirect(b)
  request=r.urllib.request.Request('https://api.github.com/a',headers={'Authorization':'secret'})
  new=handler.redirect_request(request,None,302,'',{},'https://release-assets.githubusercontent.com/b?secret=1')
  self.assertIsNone(new.get_header('Authorization'));self.assertEqual(b.reads,1)
  for target in ('http://api.github.com/a','https://evil.example/a'):
   with self.assertRaises(Refused):handler.redirect_request(request,None,302,'',{},target)
  request=r.urllib.request.Request('https://api.github.com/a',method='PATCH')
  with self.assertRaises(Refused):handler.redirect_request(request,None,302,'',{},'https://api.github.com/b')
 def test_raw_non_utf8_capture_and_failure_cleanup(self):
  child=SimpleNamespace(settle=lambda deadline:{'leaderReaped':True,'remaining':[],'errors':[],'members':[]})
  proc=SimpleNamespace(stdout=io.BytesIO(),stderr=io.BytesIO())
  runner=r.Runner(r.Budget('diagnostic'),self.root)
  with patch.object(subprocess,'Popen',return_value=proc),patch.object(r,'OwnedChild',return_value=child),patch.object(r,'capture',return_value={'actualExitCode':17,'stdout':b'\xff','stderr':b'error'}):
   result=runner.run(['mock'],1,{});self.assertEqual(result.returncode,17)
  self.assertEqual((self.root/'command-0-stdout.raw').read_bytes(),b'\xff')
  error=Refused('flood');error.boundedCapture={'stdout':b'partial','stderr':b''}
  with patch.object(subprocess,'Popen',return_value=proc),patch.object(r,'OwnedChild',return_value=child),patch.object(r,'capture',side_effect=error),self.assertRaises(Refused):runner.run(['mock'],1,{})
  self.assertIsNone(runner.records[1]['actualExitCode']);self.assertTrue(runner.records[1]['custody']['leaderReaped']);self.assertEqual((self.root/'command-1-stdout.raw').read_bytes(),b'partial')
 def test_pidfd_acquisition_failure_uses_only_unreaped_child_cleanup(self):
  calls=[];proc=SimpleNamespace(pid=123,kill=lambda:calls.append('kill'),wait=lambda timeout:calls.append(('wait',timeout)))
  with patch.object(os,'pidfd_open',side_effect=OSError('unsupported')),self.assertRaises(OSError):r.OwnedChild(proc)
  self.assertEqual(calls,['kill',('wait',2)])
 def test_escaped_descendant_is_bound_and_pid_reuse_not_signalled(self):
  leader={'pid':100,'start':1,'sid':100,'pgid':100,'ppid':os.getpid(),'state':'S'};escaped={'pid':101,'start':2,'sid':101,'pgid':101,'ppid':100,'state':'S'}
  child=r.OwnedChild.__new__(r.OwnedChild);child.proc=SimpleNamespace(pid=100);child.leader=leader;child.members={(100,1):(dict(leader),9)};child.reaped=False;child.adopted=True
  with patch.object(r,'proc_identity',side_effect=lambda pid:leader if pid==100 else escaped),patch.object(r,'all_processes',return_value=[leader,escaped]),patch.object(os,'pidfd_open',return_value=10):child.observe()
  self.assertIn((101,2),child.members)
  with patch.object(r,'proc_identity',return_value={**escaped,'start':3}),patch.object(signal_module(),'pidfd_send_signal') as signal:
   self.assertFalse(child.signal_member(escaped,10));signal.assert_not_called()
 def test_capture_flood_refuses_before_unbounded_buffer(self):
  class Selector:
   def __init__(self):self.active={}
   def register(self,stream,event,name):self.active[name]=stream
   def get_map(self):return self.active
   def select(self,timeout):return [(SimpleNamespace(fileobj=next(iter(self.active.values())),data=next(iter(self.active))),1)]
   def unregister(self,stream):self.active={}
   def close(self):pass
  stream=SimpleNamespace(fileno=lambda:99,close=lambda:None);proc=SimpleNamespace(stdout=stream,stderr=stream)
  child=SimpleNamespace(exited=lambda:SimpleNamespace(si_status=0,si_code=os.CLD_EXITED),observe=lambda:None)
  with patch.object(r.selectors,'DefaultSelector',Selector),patch.object(os,'set_blocking'),patch.object(os,'read',return_value=b'x'*(r.STREAM_CAP+1)),self.assertRaises(Refused):r.capture(proc,child,r.time.monotonic()+1)
 def test_child_environment_has_cert_false_home_and_no_credentials(self):
  with patch.dict(os.environ,{'GH_TOKEN':'secret','NUGET_API_KEY':'secret','DOTNET_NUGET_SIGNATURE_VERIFICATION':'false'}):env=r.child_environment(self.root/'owned')
  self.assertEqual(env['HOME'],os.environ['HOME']);self.assertEqual(env['DOTNET_GENERATE_ASPNET_CERTIFICATE'],'false');self.assertNotIn('GH_TOKEN',env);self.assertNotIn('NUGET_API_KEY',env);self.assertNotIn('DOTNET_NUGET_SIGNATURE_VERIFICATION',env)
 def test_failed_worker_runs_both_full_post_checks(self):
  f=Fixture(self.root,'diagnostic');source=self.root/'source';source.mkdir();(source/'global.json').write_text('{"sdk":{"version":"10.0.401"}}')
  sdk=self.root/'sdk';sdk.mkdir();(sdk/'dotnet').write_bytes(b'synthetic-sdk-not-executed');selected_sdk_fixture(sdk,source)
  root=self.root/'worker';root.mkdir();calls=[]
  class Runner:
   def __init__(self,budget,root):self.budget=budget
   def run(self,*args,**kwargs):return subprocess.CompletedProcess(args[0],0,'10.0.401','')
  def snapshot(*args):calls.append('source');return {'source':'unchanged'}
  def roster(*args,**kwargs):calls.append('sdk');return {'schema':'fsgg.wizard-selected-sdk-snapshot/1','members':{},'selection':{}}
  with patch.object(r,'subreaper'),patch.object(r,'Runner',Runner),patch.object(r,'source_snapshot',side_effect=snapshot),patch.object(r,'sdk_snapshot',side_effect=roster),patch.object(r.shutil,'which',return_value=str(sdk/'dotnet')),patch.object(r,'candidate',side_effect=Refused('bounded native feed failure')),patch.object(r,'FiniteAPI',return_value=f.api),patch.dict(os.environ,{'GH_TOKEN':'mock','GITHUB_RUN_ID':'99'}):
   self.assertEqual(r.worker('diagnostic',f.binding,root,source),1)
  report=json.loads((root/'worker-report.json').read_bytes());self.assertEqual(calls,['source','sdk','source','sdk']);self.assertTrue(report['postSourceMatches']);self.assertTrue(report['postSdkMatches']);self.assertFalse(report['success'])
 def test_wrong_recipient_and_private_key_refuse_before_crypto(self):
  binding={'recipientSha256':'0'*64}
  for pem in ('-----BEGIN PRIVATE KEY-----x','-----BEGIN CERTIFICATE-----\nYWJj\n-----END CERTIFICATE-----'):
   with patch.dict(os.environ,{'RECOVERY_RECIPIENT_CERTIFICATE':pem}),self.assertRaises(Exception):r.recipient(binding)
 def test_ciphertext_only_export_and_algorithm_failure_no_raw_leak(self):
  pem='-----BEGIN CERTIFICATE-----\n'+base64.b64encode(bytes.fromhex('06092a864886f70d010101')).decode()+'\n-----END CERTIFICATE-----'
  binding={'recipientSha256':r.digest(r.ssl.PEM_cert_to_DER_cert(pem))}
  for mutation in ('good','plaintext','wrong-algorithm','nonzero'):
   root=self.root/mutation;root.mkdir();(root/'raw-private.txt').write_bytes(b'private')
   class Runner:
    budget=r.Budget('diagnostic');records=[]
    def run(self,argv,*args,**kwargs):
     self.argv=argv
     raw=bytes.fromhex('060b2a864886f70d0109100117060960864801650304012e')
     if mutation=='plaintext':raw+=pathlib.Path(argv[argv.index('-in')+1]).read_bytes()[:64]
     if mutation=='wrong-algorithm':raw=b'wrong'
     pathlib.Path(argv[argv.index('-out')+1]).write_bytes(raw);self.records=[{'custody':{'leaderReaped':True,'remaining':[]}}]
     return subprocess.CompletedProcess(argv,17 if mutation=='nonzero' else 0,b'',b'')
   runner=Runner()
   with patch.dict(os.environ,{'RECOVERY_RECIPIENT_CERTIFICATE':pem,'GITHUB_RUN_ID':'99'}):
    if mutation=='good':
     summary=r.encrypted_custody(root,binding,runner);self.assertEqual(set(p.name for p in (root/'export').iterdir()),{'custody.cms','summary.json'});self.assertEqual(summary['recipientSha256'],binding['recipientSha256']);self.assertIn('rsa_padding_mode:oaep',runner.argv)
    else:
     with self.assertRaises(Refused):r.encrypted_custody(root,binding,runner)
     self.assertFalse((root/'export/custody.cms').exists());self.assertFalse((root/'export/summary.json').exists())

class ClosureControls(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
 def tearDown(self):self.tmp.cleanup()
 def test_actual_public_install_and_complete_closure_mutants(self):
  members={'new-sdd-workspace.dll':b'literal-dll','DotnetToolSettings.xml':b'<DotNetCliTool><Commands><Command Name="new-sdd-workspace" EntryPoint="new-sdd-workspace.dll" /></Commands></DotNetCliTool>'}
  members.update({f'dep-{i}.dll':b'fixture' for i in range(19)})
  original={'tools/net10.0/any/'+name:raw for name,raw in members.items()}
  nuget=self.root/'signed.nupkg';nuget.write_bytes(b'synthetic-signed-original')
  for mutation in ('good','missing','extra','DLL','cached','help','help-drift'):
   owner=self.root/mutation;calls=[]
   class Runner:
    def run(self,argv,seconds,env):
     calls.append((argv,seconds,env))
     assert env['HOME']==os.environ['HOME'] and env['DOTNET_GENERATE_ASPNET_CERTIFICATE']=='false' and 'GH_TOKEN' not in env
     if argv[:3]==['dotnet','tool','install']:
      tool=pathlib.Path(argv[argv.index('--tool-path')+1]);closure=tool/'.store/fs.gg.newsddworkspace/0.13.0/fs.gg.newsddworkspace/0.13.0/tools/net10.0/any';closure.mkdir(parents=True)
      for name,raw in members.items():(closure/name).write_bytes(raw)
      (closure.parents[2]/'fs.gg.newsddworkspace.0.13.0.nupkg').write_bytes(nuget.read_bytes())
      (tool/'new-sdd-workspace').write_bytes(b'synthetic-apphost'+(closure/'new-sdd-workspace.dll').relative_to(tool).as_posix().encode()+b'\0')
      if mutation=='missing':(closure/'dep-0.dll').unlink()
      if mutation=='extra':(closure/'extra').write_bytes(b'x')
      if mutation=='DLL':(closure/'new-sdd-workspace.dll').write_bytes(b'version-stamped-substitute')
      if mutation=='cached':(closure.parents[2]/'fs.gg.newsddworkspace.0.13.0.nupkg').write_bytes(b'other')
     else:
      if mutation=='help-drift':(pathlib.Path(argv[0]).parent/'.store/fs.gg.newsddworkspace/0.13.0/fs.gg.newsddworkspace/0.13.0/tools/net10.0/any/new-sdd-workspace.dll').write_bytes(b'changed')
     return subprocess.CompletedProcess(argv,17 if mutation=='help' else 0,'new-sdd-workspace','')
   if mutation=='good':
    proof=r.public_install(owner,Runner(),original,nuget,os.environ);self.assertEqual(len(proof['closure']),21);self.assertEqual([row[1] for row in calls],[180,60]);self.assertIn('--configfile',calls[0][0])
   else:
    with self.assertRaises(Refused):r.public_install(owner,Runner(),original,nuget,os.environ)
 def test_source_full_roster_and_extra_cache_or_blob_mutants(self):
  for mutation in ('good','extra','blob'):
   source=self.root/mutation;source.mkdir();(source/'tracked').write_bytes(b'accepted')
   sha=hashlib.sha1(b'blob 8\0accepted').hexdigest()
   if mutation=='extra':(source/'__pycache__').mkdir();(source/'__pycache__/extra.pyc').write_bytes(b'extra')
   if mutation=='blob':(source/'tracked').write_bytes(b'changed')
   class Runner:
    budget=r.Budget('diagnostic')
    def run(self,argv,*args,**kwargs):
     out='' if argv[1]=='status' else 'a'*40+'\n'+'b'*40+'\n' if argv[1]=='rev-parse' else f'100644 {sha} 0\ttracked\0'
     return subprocess.CompletedProcess(argv,0,out,'')
   if mutation=='good':self.assertEqual(set(r.source_snapshot(source,{'heldSource':'a'*40,'heldTree':'b'*40},Runner(),{})),{'tracked'})
   else:
    with self.assertRaises(Refused):r.source_snapshot(source,{'heldSource':'a'*40,'heldTree':'b'*40},Runner(),{})
 def test_immediate_main_admission_journal_drift_all_refuse(self):
  for mutation in ('main','admission','journal','failed-rerun','prior-active'):
   f=Fixture(self.root)
   def install(root,nuget):
    f.installs+=1
    if f.installs==1:
     if mutation=='main':f.main='f'*40
     if mutation=='admission':f.admission.authorize_recovery=lambda *args:False
     if mutation=='journal':f.nodes[f.head]['state']['effects']['promote']='verified'
     if mutation=='failed-rerun':f.failed['run_attempt']=2
     if mutation=='prior-active':f.prior['status']='in_progress'
    return {'synthetic':True}
   f.install=install
   with self.assertRaises((Refused,JournalRefused)):f.run()
   self.assertEqual((f.patches,f.cas),(0,0))
 def test_genuine_readiness_run_archive_and_ciphertext_mutants(self):
  for mutation in ('native-failure','publisher-substitute','artifact-repo','archive','ciphertext','binding'):
   f=Fixture(self.root)
   if mutation=='native-failure':f.prior['conclusion']='failure'
   if mutation=='publisher-substitute':f.prior['display_title']='ordinary publisher'
   if mutation=='artifact-repo':f.readinessartifact['workflow_run']['repository_id']=1
   if mutation=='archive':f.binding['readinessArchiveSha256']='f'*64
   if mutation=='ciphertext':f.binding['readinessCiphertextSha256']='f'*64
   if mutation=='binding':f.binding['readinessBindingSha256']='f'*64
   # Rebind current selection to test the readiness gate rather than title drift.
   f.native['display_title']=f'Wizard 0.13 recovery complete '+f.binding['correlation']+' '+r.digest(r.canonical(f.binding))
   with self.assertRaises(Refused):f.run()
   self.assertEqual(f.patches,0)
 def test_root_binding_recipient_digest_cannot_change_after_selection(self):
  f=Fixture(self.root);f.binding['recipientSha256']='f'*64
  with self.assertRaises(Refused):f.run()
  self.assertEqual(f.patches,0)
 def test_actual_finite_cas_arm_rejects_identity_drift_and_multiple_writes(self):
  f=Fixture(self.root);api=r.FiniteAPI('mock',r.Budget('complete'),self.root,authority=True)
  state=f.nodes[r.JOURNAL_HEAD]['state'];api.arm_settlement(SimpleNamespace(head=r.JOURNAL_HEAD,state=state))
  value=copy.deepcopy(api.expectedSettlement);value['sourceSha']='f'*40
  with self.assertRaises(Refused):api.post(f'repos/{AUTHORITY}/git/blobs',{'content':base64.b64encode(canonical(value)).decode(),'encoding':'base64'})
  api.budget.cas_writes=[]
  replies=iter([{'sha':'b'*40},{'sha':'c'*40},{'sha':'f'*40},{'ref':'settled'}])
  with patch.object(api,'request',side_effect=lambda *args,**kwargs:next(replies)):
   api.post(f'repos/{AUTHORITY}/git/blobs',{'content':base64.b64encode(canonical(api.expectedSettlement)).decode(),'encoding':'base64'})
   api.post(f'repos/{AUTHORITY}/git/trees',{'tree':[{'path':'release-state.json','mode':'100644','type':'blob','sha':'b'*40}]})
   api.post(f'repos/{AUTHORITY}/git/commits',{'message':'Release successor generation 17','tree':'c'*40,'parents':[r.JOURNAL_HEAD]})
   api.patch(f'repos/{AUTHORITY}/git/refs/'+r.REF.removeprefix('refs/'),{'sha':'f'*40,'force':False})
   with self.assertRaises(Refused):api.post(f'repos/{AUTHORITY}/git/blobs',{})
  self.assertEqual(len(api.budget.cas_writes),4)
 def test_archive_duplicate_and_expansion_or_escape_refuse(self):
  for mutation in ('duplicate','escape','expansion'):
   target=self.root/(mutation+'.zip')
   with zipfile.ZipFile(target,'w') as archive:
    archive.writestr('../escape' if mutation=='escape' else 'file',b'x')
    if mutation=='duplicate':archive.writestr('file',b'duplicate')
   limit=0 if mutation=='expansion' else r.ZIP_CAP
   with patch.object(r,'ZIP_CAP',limit),self.assertRaises(Refused):r.safe_zip(target)

def signal_module():return r.signal


class CustodySizeControls(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
 def tearDown(self):self.tmp.cleanup()
 def fixture(self,name='case',large=False):
  root=self.root/name;root.mkdir();f=Fixture(root)
  if large:
   ciphertext=b'x'*(r.BINARY_CAP+1)
   summary={'schema':'fsgg.wizard-recovery-encrypted-custody/1','ciphertextSha256':r.digest(ciphertext),'bindingSha256':f.binding['readinessBindingSha256'],'nativeRunId':98,'commandExit':0}
   out=io.BytesIO()
   with zipfile.ZipFile(out,'w',compression=zipfile.ZIP_STORED) as z:
    z.writestr('custody.cms',ciphertext);z.writestr('summary.json',r.canonical(summary))
   f.readinesszip=out.getvalue();f.binding['readinessArchiveSha256']=r.digest(f.readinesszip);f.binding['readinessCiphertextSha256']=r.digest(ciphertext);f.readinessartifact['digest']='sha256:'+r.digest(f.readinesszip)
   f.native['display_title']=f"Wizard 0.13 recovery complete {f.binding['correlation']} {r.digest(r.canonical(f.binding))}"
  # Four preceding worker commands: three source Git reads and SDK version.
  f.budget.commands=4
  install=f.install
  def counted_install(*args):
   f.budget.command(180);f.budget.command(60);return install(*args)
  f.install=counted_install
  f.api.storage=r.CustodyStorage(root);f.api.budget=f.budget;f.api.matched_bodies={}
  return f
 def test_authentic_large_readiness_reaches_production_pre_effect_and_settlement(self):
  f=self.fixture(large=True);engine=f.engine()
  real=r.digest
  def synthetic(raw):return '2e1d16a87400d8246cd31237b63e6366025619f26cb81742310de9a8426304c2' if raw==f.assetbytes[608701711] else real(raw)
  with patch.object(r,'ORIGINAL_PACKAGE',real(f.originalzip)),patch.object(r,'digest',synthetic):result=engine.run()
  self.assertTrue(result['publisherComplete']);self.assertEqual((f.patches,f.cas),(1,1));self.assertLess(engine.custody_projection['projectedBytes'],r.CUSTODY_CAP);self.assertEqual(len(f.api.storage.roles),1)
 def test_unregistered_or_forged_large_path_refuses_before_effects(self):
  for mutation in ('unregistered','forged'):
   f=self.fixture(mutation);path=f.root/'response-github-0.raw';path.write_bytes(b'x'*(r.BINARY_CAP+1))
   if mutation=='forged':
    with self.assertRaises(Refused):f.api.storage.register_readiness(path,f.binding)
   with self.assertRaises(Refused):f.run()
   self.assertEqual((f.patches,f.cas),(0,0))
 def test_total_projected_overflow_refuses_before_patch_or_cas(self):
  for draft in (True,False):
   f=self.fixture(str(draft));f.release['draft']=draft
   for i in range(9):(f.root/f'ordinary-{i}.raw').write_bytes(b'x'*r.BINARY_CAP)
   with self.assertRaises(Refused):f.run()
   self.assertEqual((f.patches,f.cas),(0,0))
 def test_ordinary_member_cap_and_global_pending_guard(self):
  f=self.fixture();(f.root/'plain.raw').write_bytes(b'x'*(r.BINARY_CAP+1))
  with self.assertRaises(Refused):f.api.storage.check()
  (f.root/'plain.raw').unlink()
  with self.assertRaises(Refused):f.api.storage.check(r.CUSTODY_CAP)
 def test_registered_original_byte_drift_refuses(self):
  f=self.fixture(large=True);r.readiness(f.api,f.binding)
  relative=next(iter(f.api.storage.roles));path=f.root/relative
  with path.open('r+b') as stream:stream.write(b'!')
  with self.assertRaises(Refused):f.api.storage.check()
 def test_endpoint_json_caps_and_post_effect_body_roles(self):
  f=self.fixture();api=r.FiniteAPI('mock',f.budget,f.root)
  class Response(io.BytesIO):
   status=200
   def __enter__(self):return self
   def __exit__(self,*args):self.close()
  api.opener=SimpleNamespace(open=lambda *a,**k:Response(b'x'*(r.SMALL_JSON_CAP+1)))
  with self.assertRaises(Refused):api.get(f'repos/{r.REPO}')
  api.storage=f.api.storage;api.storage.armed=True
  with self.assertRaises(Refused):api.request('https://api.nuget.org/forged-package',binary=True)

 def test_authentic_large_original_is_retained_whole_in_encrypted_archive(self):
  f=self.fixture(large=True);r.readiness(f.api,f.binding)
  pem='-----BEGIN CERTIFICATE-----\n'+base64.b64encode(bytes.fromhex('06092a864886f70d010101')).decode()+'\n-----END CERTIFICATE-----'
  binding={**f.binding,'recipientSha256':r.digest(r.ssl.PEM_cert_to_DER_cert(pem))}
  class Runner:
   budget=r.Budget('complete');records=[]
   def run(self,argv,*a,**k):
    pathlib.Path(argv[argv.index('-out')+1]).write_bytes(bytes.fromhex('060b2a864886f70d0109100117060960864801650304012e'));self.records=[{'custody':{'leaderReaped':True,'remaining':[]}}]
    return subprocess.CompletedProcess(argv,0,b'',b'')
  with patch.dict(os.environ,{'RECOVERY_RECIPIENT_CERTIFICATE':pem,'GITHUB_RUN_ID':'99'}):summary=r.encrypted_custody(f.root,binding,Runner(),f.api.storage)
  relative=next(iter(f.api.storage.roles))
  with zipfile.ZipFile(f.root/'raw-custody.zip') as z:self.assertEqual(z.read(relative),f.readinesszip)
  self.assertEqual(set(p.name for p in (f.root/'export').iterdir()),{'custody.cms','summary.json'})

 def test_production_runner_typed_archive_cipher_phases_do_not_double_count_plaintext(self):
  root=self.root/'phases';root.mkdir()
  for i in range(8):(root/f'raw-{i}').write_bytes(b'x'*(7*1024*1024))
  pem='-----BEGIN CERTIFICATE-----\n'+base64.b64encode(bytes.fromhex('06092a864886f70d010101')).decode()+'\n-----END CERTIFICATE-----'
  binding={'recipientSha256':r.digest(r.ssl.PEM_cert_to_DER_cert(pem))}
  budget=r.Budget('complete');budget.reserve=True;runner=r.Runner(budget,root)
  fakeproc=SimpleNamespace(stdout=io.BytesIO(),stderr=io.BytesIO())
  custody={'leaderReaped':True,'remaining':[],'errors':[],'members':[]}
  def captured(proc,child,deadline,storage):
   self.assertEqual(storage.phase,'encrypt')
   (root/'custody.pending.cms').write_bytes(bytes.fromhex('060b2a864886f70d0109100117060960864801650304012e')+b'0'*(56*1024*1024))
   storage.check();self.assertGreater(sum(q.stat().st_size for q in root.rglob('*') if q.is_file()),r.CUSTODY_CAP)
   return {'actualExitCode':0,'stdout':b'','stderr':b''}
  with patch.dict(os.environ,{'RECOVERY_RECIPIENT_CERTIFICATE':pem,'GITHUB_RUN_ID':'99'}),patch.object(subprocess,'Popen',return_value=fakeproc),patch.object(r,'OwnedChild',return_value=SimpleNamespace(settle=lambda *a:custody)),patch.object(r,'capture',side_effect=captured):
   summary=r.encrypted_custody(root,binding,runner)
  self.assertEqual(budget.commands,1);self.assertEqual(budget.storage.phase,'export');self.assertEqual(summary['physicalCustodyLimit'],r.PHYSICAL_CUSTODY_CAP)
 def test_concrete_member_path_and_serialized_output_limits(self):
  root=self.root/'limits';root.mkdir();storage=r.CustodyStorage(root)
  for i in range(r.CUSTODY_MEMBER_CAP+1):(root/f'file-{i}').touch()
  with self.assertRaises(Refused):storage.check()
  for q in root.iterdir():q.unlink()
  deep=root/('x'*200);deep.mkdir();(deep/('y'*100)).touch()
  with self.assertRaises(Refused):storage.check()
  (deep/('y'*100)).unlink();deep.rmdir();storage.begin_archive()
  archive=root/'raw-custody.zip'
  with archive.open('wb') as out:out.truncate(r.CUSTODY_CAP+1)
  with self.assertRaises(Refused):storage.check()

 def test_full_rosters_derive_report_reservation_without_truncation(self):
  storage=r.CustodyStorage(self.root);source={'x':{'sha256':'a'*64,'link':None}};sdk={'dotnet':{'sha256':'b'*64,'link':None}}
  storage.bind_report_rosters(source,sdk);n=len(r.canonical(source))+len(r.canonical(sdk))
  self.assertEqual(storage.worker_report_cap,2*n+256*1024);self.assertEqual(storage.report_reservation,4*n+3*256*1024)
  with self.assertRaises(Refused):storage.bind_report_rosters({'oversized':'x'*r.BINARY_CAP},sdk)

class AncestryRoleControls(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name);self.held='e'*40
 def tearDown(self):self.tmp.cleanup()
 def api(self,root,compare=None,status=200):
  root.mkdir();api=r.FiniteAPI('synthetic',r.Budget('diagnostic'),root,held_source=self.held)
  failed={'id':r.FAILED_RUN,'head_sha':r.CANDIDATE_SOURCE,'run_attempt':1,'status':'completed','conclusion':'failure','path':r.WORKFLOW,'actor':{'login':'EHotwagner'},'repository':{'id':1269292704},'event':'workflow_dispatch','head_branch':'main'}
  value=synthetic_ancestry(self.held)
  value['padding']='x'*(229508-len(r.canonical({**value,'padding':''})))
  raw=r.canonical(value) if compare is None else compare
  if compare is None:self.assertEqual(len(raw),229508)
  class Response(io.BytesIO):
   def __enter__(self):return self
   def __exit__(self,*args):self.close()
  def opened(request,timeout):
   self.assertEqual(request.get_method(),'GET');self.assertIsNone(request.data);self.assertLessEqual(timeout,25)
   ancestry='/compare/' in request.full_url
   if ancestry:
    self.assertEqual(request.full_url,r.ancestry_url(self.held))
    self.assertEqual(request.get_header('Accept'),'application/vnd.github+json')
    self.assertEqual(request.get_header('X-github-api-version'),'2022-11-28')
   response=Response(raw if ancestry else r.canonical(failed));response.status=status if ancestry else 200;return response
  api.opener=SimpleNamespace(open=opened);return api
 def test_complete_body_and_repeat_originals_retained_before_effect_projection(self):
  api=self.api(self.root/'complete');api.storage=r.CustodyStorage(api.root)
  binding={'heldSource':self.held,'priorRunIds':[]}
  r.original_runs(api,binding);r.original_runs(api,binding)
  rows=[row for row in api.records if row['role']=='bound-original-to-held-ancestry']
  self.assertEqual(len(rows),2);self.assertTrue(all(row['bytes']==229508 and row['cap']==r.ANCESTRY_JSON_CAP and row['status']==200 for row in rows))
  originals=list(api.root.glob('response-github-*.raw'));ancestry=[p.read_bytes() for p in originals if b'"padding"' in p.read_bytes()]
  self.assertEqual(len(ancestry),2);self.assertEqual(ancestry[0],ancestry[1]);self.assertEqual(rows[0]['sha256'],r.digest(ancestry[0]))
  projection=api.storage.pre_effect(api.budget,{},False)
  self.assertGreaterEqual(projection['currentBytes'],2*229508)
  with self.assertRaises(Refused):r.original_runs(api,binding)
 def test_two_and_larger_populations_do_not_pin_row_to_head(self):
  for total in (2,51,1000):
   value=synthetic_ancestry(self.held,total=total)
   self.assertNotEqual(value['commits'][0]['sha'],self.held)
   api=self.api(self.root/str(total),r.canonical(value));r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
 def test_wrong_origin_path_source_query_and_request_kind_refuse_before_transport(self):
  api=self.api(self.root/'closed');base=r.ancestry_url(self.held,False);exact=r.ancestry_url(self.held)
  urls=[base,base+'?page=1',base+'?per_page=1&page=1',base+'?page=2&per_page=1',base+'?per_page=2&page=2',base+'?per_page=1&page=2&page=2',base+'?per_page=%31&page=2',exact+'#',exact+'#fragment',exact.replace('api.github.com','api.nuget.org'),exact.replace('/FS-GG/','/other/'),exact.replace(r.CANDIDATE_SOURCE,'a'*40),exact.replace(self.held,'b'*40),exact.replace('/compare/','/%63ompare/'),exact.replace('api.github.com','api.github.com:443'),exact.replace('api.github.com','api.github.com:444'),exact.replace('https://','http://'),exact.replace('api.github.com','synthetic@api.github.com'),exact.replace('/compare/','/compare/\n'),exact.replace('/compare/','/compare/\t'),exact.replace('page=2','page=2&'),exact.replace('...','..')]
  api.opener.open=lambda *a,**k:(_ for _ in ()).throw(AssertionError('wrong role reached transport'))
  for url in urls:
   with self.subTest(url=url),self.assertRaises(Refused):api.request(url)
  for kwargs in ({'method':'POST'},{'method':'get'},{'body':{}},{'headers':{}},{'headers':{'Accept':'text/plain'}},{'binary':True},{'custody':True}):
   with self.subTest(kwargs=kwargs),self.assertRaises(Refused):api.request(exact,**kwargs)
  with self.assertRaises(Refused):r.original_runs(api,{'heldSource':'b'*40,'priorRunIds':[]})
 def test_typed_native_subject_count_and_shape_refusals(self):
  cases=[]
  for field in ('url','base_commit','merge_base_commit','status','behind_by','ahead_by','total_commits','commits'):
   value=synthetic_ancestry(self.held);del value[field];cases.append((field+'-missing',value))
  for field,wrong in [('url',r.ancestry_url(self.held)),('url',r.ancestry_url('b'*40,False)),('base_commit',[]),('base_commit',None),('base_commit',{'sha':'b'*40}),('merge_base_commit',{'sha':'b'*40}),('merge_base_commit',[]),('merge_base_commit',None),('status','behind'),('status','diverged'),('status','identical'),('commits',{}),('commits',None),('commits',[]),('commits',[{'sha':'a'*40},{'sha':'b'*40}]),('commits',[None]),('commits',[{}]),('commits',[{'sha':'A'*40}]),('commits',[{'sha':'../unsafe'}]),('commits',[{'sha':123}])]:
   value=synthetic_ancestry(self.held);value[field]=wrong;cases.append((field+'-'+repr(wrong),value))
  for field in ('behind_by','ahead_by','total_commits'):
   for wrong in (True,False,'51',51.0,-1,None):
    value=synthetic_ancestry(self.held);value[field]=wrong;cases.append((field+'-'+repr(wrong),value))
  for total in (0,1,-1):cases.append(('population-'+str(total),synthetic_ancestry(self.held,total=total)))
  value=synthetic_ancestry(self.held);value['behind_by']=1;cases.append(('behind-positive',value))
  value=synthetic_ancestry(self.held);value['ahead_by']=50;cases.append(('count-mismatch',value))
  for files in (None,[],{}):
   value=synthetic_ancestry(self.held);value['files']=files;cases.append(('files-'+repr(files),value))
  cases.extend([('top-list',[]),('top-null',None),('top-string','ahead')])
  for index,(name,value) in enumerate(cases):
   api=self.api(self.root/f'shape-{index}',r.canonical(value))
   with self.subTest(name=name),self.assertRaises(Refused):r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
 def test_oversize_truncated_error_non_json_and_non200_refuse(self):
  for name,raw in [('oversize',r.canonical({**synthetic_ancestry(self.held),'padding':'x'*r.ANCESTRY_JSON_CAP})),('prefix',b'{"status":"ahead",'),('error',b'{"message":"unknown"}'),('nonjson',b'not JSON')]:
   api=self.api(self.root/name,raw)
   with self.assertRaises(Exception):r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
  for status in (201,204,206,301,403,500):
   raw=r.canonical(synthetic_ancestry(self.held));api=self.api(self.root/f'status-{status}',raw,status)
   with self.subTest(status=status),self.assertRaises(Refused):r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
   row=api.records[-1];self.assertEqual(row['status'],status);self.assertEqual(row['sha256'],r.digest(raw));self.assertEqual(row['bytes'],len(raw))
  api=self.api(self.root/'http-error');api.opener.open=lambda *a,**k:(_ for _ in ()).throw(r.urllib.error.HTTPError('https://api.github.com',403,'withheld',{},None))
  with self.assertRaises(Refused):r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
 def test_ordinary_cap_unbound_authority_and_redirects_remain_refusals(self):
  api=self.api(self.root/'ordinary')
  class Response(io.BytesIO):
   status=200
   def __enter__(self):return self
   def __exit__(self,*args):self.close()
  api.opener.open=lambda *a,**k:Response(b'x'*(r.SMALL_JSON_CAP+1))
  with self.assertRaises(Refused):api.get(f'repos/{r.REPO}')
  url=r.ancestry_url(self.held)
  for field,value in [('held_source',None),('authority',True)]:
   previous=getattr(api,field);setattr(api,field,value)
   with self.assertRaises(Refused):api.request(url)
   setattr(api,field,previous)
  request=r.urllib.request.Request(url)
  with self.assertRaises(Refused):r.Redirect(api.budget).redirect_request(request,None,302,'redirect',{},'https://api.github.com/other')
 def test_exhausted_read_deadline_and_custody_refuse_before_acceptance(self):
  for kind in ('reads','deadline','custody'):
   api=self.api(self.root/kind)
   if kind=='reads':api.budget.reads=120
   elif kind=='deadline':api.budget=r.Budget('diagnostic',clock=lambda:601,start=0)
   else:
    api.storage=r.CustodyStorage(api.root)
    for index in range(17):
     with (api.root/f'full-{index}').open('wb') as stream:stream.truncate(r.BINARY_CAP)
   with self.subTest(kind=kind),self.assertRaises(Refused):r.original_runs(api,{'heldSource':self.held,'priorRunIds':[]})
 def test_malformed_ancestry_cannot_install_patch_or_settle(self):
  for index,field in enumerate(('base_commit','total_commits','files')):
   root=self.root/f'effect-refusal-{index}';root.mkdir();fixture=Fixture(root,'complete');get=fixture.api.get
   malformed=synthetic_ancestry(fixture.main)
   if field=='files':malformed[field]=[]
   elif field=='total_commits':malformed[field]=1
   else:malformed[field]={'sha':'b'*40}
   def response(path):return malformed if '/compare/' in path else get(path)
   with patch.object(fixture.api,'get',side_effect=response),self.subTest(field=field),self.assertRaises(Refused):fixture.run()
   self.assertEqual((fixture.installs,fixture.patches,fixture.cas),(0,0,0))



def selected_sdk_fixture(sdk,source):
 (sdk/'dotnet').chmod(0o700)
 (source/'global.json').write_text('{"sdk":{"version":"10.0.401","rollForward":"latestFeature"}}')
 (sdk/'sdk/10.0.401').mkdir(parents=True,exist_ok=True);(sdk/'sdk/10.0.401/dotnet.runtimeconfig.json').write_text('{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')
 (sdk/'host/fxr/10.0.2').mkdir(parents=True,exist_ok=True);(sdk/'host/fxr/10.0.2/libhostfxr.so').write_bytes(b'synthetic; never loaded')
 (sdk/'shared/Microsoft.NETCore.App/10.0.2').mkdir(parents=True,exist_ok=True);(sdk/'shared/Microsoft.NETCore.App/10.0.2/coreclr').write_bytes(b'synthetic; never loaded')

class RecoverySetupObservabilityTests(unittest.TestCase):
 def test_absent_sdk_refuses_before_unrelated_snapshot(self):self.setup_refusal(missing=True)
 def test_setup_deadline_has_closed_phase_code_without_ciphertext_claim(self):self.setup_refusal(missing=False)
 def setup_refusal(self,missing):
  import contextlib
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary);fixture=Fixture(root,'diagnostic');sdk=root/'sdk';sdk.mkdir();(sdk/'dotnet').write_bytes(b'synthetic; never executed')
   source=root/'source';source.mkdir();selected_sdk_fixture(sdk,source);clock=[40.0];calls=[];output=io.StringIO();original=r.physical_roster;budget_type=r.Budget
   def roster(path,**kwargs):
    calls.append(pathlib.Path(path))
    if len(calls)==1:return {'source.txt':{'sha256':'a'*64,'link':None}}
    clock[0]=480.0;return original(sdk,deadline=480.0)
   b=fixture.binding;selected=r.datetime.fromisoformat(b['selectedAfter'].replace('Z','+00:00')).timestamp()
   env={'GITHUB_EVENT_NAME':'workflow_dispatch','GITHUB_RUN_ATTEMPT':'1','GITHUB_REPOSITORY':r.REPO,'GITHUB_REF':'refs/heads/main','GITHUB_ACTOR':'EHotwagner','GITHUB_SHA':b['heldSource'],'RECOVERY_BINDING_SHA256':r.digest(r.canonical(b)),'RECOVERY_CORRELATION':b['correlation']}
   with patch.dict(os.environ,env),patch.object(r,'recipient',return_value='synthetic public cert'),patch.object(r,'subreaper'),patch.object(r,'physical_roster',side_effect=roster),patch.object(r,'selected_sdk_scope',return_value={'scope':['sdk/10.0.401']}),patch.object(r.shutil,'which',return_value=None if missing else str(sdk/'dotnet')),patch.object(r.time,'time',return_value=selected+40),patch.object(r.time,'monotonic',side_effect=lambda:clock[0]),patch.object(r,'Budget',side_effect=lambda mode,start:budget_type(mode,clock=lambda:clock[0],start=start)),patch.object(r,'encrypted_custody') as encrypt,patch.object(r,'Runner',side_effect=AssertionError('worker forbidden')),patch.object(subprocess,'Popen',side_effect=AssertionError('process forbidden')),patch.object(r.urllib.request.OpenerDirector,'open',side_effect=AssertionError('network forbidden')),contextlib.redirect_stdout(output),contextlib.redirect_stderr(output):
    with self.assertRaises(Refused):r.entry('diagnostic',r.canonical(b).decode(),str(root/'operation'),str(source))
    self.assertEqual(encrypt.call_count,0)
   self.assertEqual(len(calls),1 if missing else 2)
   self.assertIn('sdk-unavailable' if missing else 'physical-roster-deadline',output.getvalue())
   self.assertIn('outer-sdk-resolve' if missing else 'outer-sdk-roster',output.getvalue());self.assertNotIn(temporary,output.getvalue())
 def test_safe_refusal_cannot_echo_error_attributes_or_exception_name(self):
  import contextlib
  error=type('SECRETException',(RuntimeError,),{})('SECRET /private/path ?token=SECRET');error.recoveryCode='SECRET';output=io.StringIO();phase=r._current_phase;r._current_phase='SECRET'
  try:
   with contextlib.redirect_stderr(output):r.recovery_refusal(error)
  finally:r._current_phase=phase
  self.assertNotIn('SECRET',output.getvalue());self.assertNotIn('private',output.getvalue());row=json.loads(output.getvalue().split(': ',1)[1]);self.assertEqual((row['phase'],row['code'],row['exceptionKind']),('entry','unclassified','unclassified'))
 def test_existing_sdk_path_has_no_cwd_fallback(self):
  with tempfile.TemporaryDirectory() as temporary:
   root=pathlib.Path(temporary);(root/'dotnet').write_bytes(b'synthetic')
   with patch.object(r.shutil,'which',return_value=str(root/'dotnet')):self.assertEqual(r.existing_sdk_root(),root.resolve())
   with patch.object(r.shutil,'which',return_value=None) as lookup:
    with self.assertRaises(Refused):r.existing_sdk_root()
    lookup.assert_called_once_with('dotnet')
 def test_repair_does_not_raise_time_or_capture_caps(self):
  b=r.Budget('diagnostic',clock=lambda:480,start=0);self.assertEqual((b.work,b.end),(480,600))
  with self.assertRaises(Refused):b.remaining(25)
  self.assertEqual(b.remaining(25,True),25);self.assertEqual((r.CUSTODY_CAP,r.READINESS_CAP,r.STREAM_CAP,r.CUSTODY_MEMBER_CAP),(128*1024*1024,128*1024*1024+8192,1024*1024,323))


import contextlib,time
class SDKSnapshotMetricsTests(unittest.TestCase):
 def refused_snapshot(self):
  with self.assertRaises(r.Refused):r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()+10)
  return None
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name);self.sdk=self.root/'sdk-root';self.sdk.mkdir();self.source=self.root/'source';self.source.mkdir();(self.source/'global.json').write_text('{"sdk":{"version":"10.0.401","rollForward":"latestFeature"}}');(self.sdk/'sdk/10.0.401').mkdir(parents=True);(self.sdk/'sdk/9.0.100').mkdir();(self.sdk/'dotnet').write_bytes(b'synthetic host, never executed');(self.sdk/'sdk/10.0.401/payload').write_bytes(b'selected');(self.sdk/'sdk/9.0.100/payload').write_bytes(b'unrelated deliberately outside amended scope');selected_sdk_fixture(self.sdk,self.source)
 def tearDown(self):self.tmp.cleanup()
 def capture(self,fn):
  output=io.StringIO()
  with contextlib.redirect_stdout(output),patch.object(r.shutil,'which',return_value=str(self.sdk/'dotnet')),patch.object(r,'sdk_input_environment',return_value={'PATH':str(self.sdk)}),patch.object(r.subprocess,'Popen',side_effect=AssertionError('process forbidden')),patch.object(r.urllib.request.OpenerDirector,'open',side_effect=AssertionError('network forbidden')):result=fn()
  return result,[json.loads(x.split(': ',1)[1]) for x in output.getvalue().splitlines()]
 def test_complete_selected_payload_roster_and_unrelated_payload_excluded(self):
  result,rows=self.capture(lambda:r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()+10));self.assertIn('sdk/10.0.401/payload',result['members']);self.assertNotIn('sdk/9.0.100/payload',result['members']);self.assertEqual(rows[-1]['completedMembers'],len(result['members']));self.assertGreater(rows[-1]['hashedBytes'],0);self.assertTrue(rows[-1]['selectedSDKDirectoryExists']);self.assertEqual(rows[-1]['installedSDKDirectoryNames'],['10.0.401','9.0.100'])
 def test_actual_deadline_has_partial_closed_metrics_no_ciphertext(self):
  output=io.StringIO()
  with contextlib.redirect_stdout(output),patch.object(r,'encrypted_custody') as crypto:
   with self.assertRaises(r.Refused):r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()-1)
  rows=[json.loads(x.split(': ',1)[1]) for x in output.getvalue().splitlines()];self.assertEqual(rows[-1]['refusalCode'],'physical-roster-deadline');self.assertEqual(rows[-1]['status'],'refused');self.assertEqual(rows[-1]['completedMembers'],0);self.assertEqual(crypto.call_count,0)
 def test_private_paths_and_arbitrary_sdk_names_not_public(self):
  (self.sdk/'sdk/SECRET-user-token').mkdir();result,rows=self.capture(lambda:self.refused_snapshot());raw=json.dumps(rows);self.assertNotIn(str(self.root),raw);self.assertNotIn('SECRET',raw);self.assertTrue(rows[-1]['unrecognizedSDKDirectoryPresent']);self.assertIsNone(rows[-1]['sdkParentResolvedPath'])
 def test_stock_paths_are_exact_and_nonstock_paths_withheld(self):
  self.assertEqual(r.SDKSnapshotMetrics.public_path(pathlib.Path('/usr/share/dotnet')),'/usr/share/dotnet');self.assertEqual(r.SDKSnapshotMetrics.public_path(pathlib.Path('/usr/share/dotnet/dotnet')),'/usr/share/dotnet/dotnet');self.assertIsNone(r.SDKSnapshotMetrics.public_path(pathlib.Path('/home/private-user/.dotnet')))
 def test_missing_selected_version_emits_false_without_fallback(self):
  import shutil
  shutil.rmtree(self.sdk/'sdk/10.0.401');_,rows=self.capture(lambda:self.refused_snapshot());self.assertFalse(rows[-1]['selectedSDKDirectoryExists']);self.assertIn('9.0.100',rows[-1]['installedSDKDirectoryNames'])
 def test_finite_row_and_member_caps(self):
  m=r.SDKSnapshotMetrics(self.sdk,self.source,'outer-sdk-roster');output=io.StringIO()
  with contextlib.redirect_stdout(output):
   for _ in range(33):m.emit()
   with self.assertRaises(r.Refused):m.emit()
  m.members=50000
  with self.assertRaises(r.Refused):m.progress('member',0)
 def test_original_caps_preserved(self):
  b=r.Budget('diagnostic',clock=lambda:0,start=0);self.assertEqual((b.work,b.end),(480,600));self.assertEqual((r.CUSTODY_CAP,r.STREAM_CAP),(128*1024*1024,1024*1024));self.assertEqual(r.physical_roster.__defaults__[0],50000)
 def test_member_scope_escape_still_refuses(self):
  outside=self.root/'outside';outside.write_bytes(b'input');(self.sdk/'sdk/10.0.401/escape').symlink_to(outside)
  with contextlib.redirect_stdout(io.StringIO()):
   with self.assertRaises(r.Refused):r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()+10)


class SDKScopeProductionControls(unittest.TestCase):
 setUp=SDKSnapshotMetricsTests.setUp
 tearDown=SDKSnapshotMetricsTests.tearDown
 capture=SDKSnapshotMetricsTests.capture
 def snapshot(self):return r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()+10,{'PATH':str(self.sdk)})
 def test_all_four_typed_snapshots_same_complete_selection_and_bytes(self):
  values=[]
  with contextlib.redirect_stdout(io.StringIO()):
   for phase in ('outer-sdk-roster','worker-sdk-roster','worker-sdk-post','outer-sdk-post'):values.append(r.sdk_snapshot(self.sdk,self.source,phase,time.monotonic()+10,{'PATH':str(self.sdk)}))
  self.assertTrue(all(value==values[0] for value in values));self.assertIn('host/fxr/10.0.2/libhostfxr.so',values[0]['members']);self.assertIn('shared/Microsoft.NETCore.App/10.0.2/coreclr',values[0]['members']);self.assertEqual(values[0]['selection']['originalWizardRollForward'],'Major')
 def test_newer_latestFeatureSDK_refuses_without_normalization(self):
  (self.sdk/'sdk/10.0.500').mkdir()
  with contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):self.snapshot()
 def test_runtime_layout_change_even_unselected_patch_is_detected(self):
  with contextlib.redirect_stdout(io.StringIO()):before=self.snapshot();(self.sdk/'shared/Microsoft.NETCore.App/9.0.100').mkdir();after=self.snapshot()
  self.assertNotEqual(before,after);self.assertEqual(before['members'],after['members'])
 def test_exact_selected_runtime_content_changes_entire_input(self):
  with contextlib.redirect_stdout(io.StringIO()):before=self.snapshot();(self.sdk/'shared/Microsoft.NETCore.App/10.0.2/coreclr').write_bytes(b'changed');after=self.snapshot()
  self.assertNotEqual(before,after)
 def test_unrelatedSDKcontent_deliberately_outside_scope_but_layout_not(self):
  with contextlib.redirect_stdout(io.StringIO()):before=self.snapshot();(self.sdk/'sdk/9.0.100/payload').write_bytes(b'changed unrelated');after=self.snapshot();(self.sdk/'sdk/9.0.200').mkdir();layout=self.snapshot()
  self.assertEqual(before,after);self.assertNotEqual(after,layout)
 def test_no_net11_major_fallback_when_net10_missing(self):
  import shutil
  shutil.rmtree(self.sdk/'shared/Microsoft.NETCore.App/10.0.2');(self.sdk/'shared/Microsoft.NETCore.App/11.0.0').mkdir()
  with contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):self.snapshot()
 def test_both_actual_sdk_and_original_wizard_frameworks_join(self):
  (self.sdk/'shared/Microsoft.AspNetCore.App/10.0.3').mkdir(parents=True);(self.sdk/'shared/Microsoft.AspNetCore.App/10.0.3/web').write_bytes(b'synthetic selected runtime')
  cfg={'runtimeOptions':{'frameworks':[{'name':'Microsoft.NETCore.App','version':'10.0.2'},{'name':'Microsoft.AspNetCore.App','version':'10.0.3'}]}};(self.sdk/'sdk/10.0.401/dotnet.runtimeconfig.json').write_text(json.dumps(cfg))
  with contextlib.redirect_stdout(io.StringIO()):value=self.snapshot()
  self.assertEqual(value['selection']['selectedRuntimeClosures'],{'Microsoft.NETCore.App':'10.0.2','Microsoft.AspNetCore.App':'10.0.3'});self.assertIn('shared/Microsoft.AspNetCore.App/10.0.3/web',value['members'])
 def test_resolver_overrides_and_noncanonical_root_refuse(self):
  for env in [{'DOTNET_ROLL_FORWARD':'LatestMajor'},{'DOTNET_STARTUP_HOOKS':'private'},{'DOTNET_ROOT':'/other'},{'DOTNET_ROOT_X64':str(self.sdk)}]:
   with contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):r.sdk_snapshot(self.sdk,self.source,'outer-sdk-roster',time.monotonic()+10,env)
 def test_additional_probing_or_dev_config_refused(self):
  cfg=self.sdk/'sdk/10.0.401/dotnet.runtimeconfig.json';cfg.write_text(json.dumps({'runtimeOptions':{'framework':{'name':'Microsoft.NETCore.App','version':'10.0.0'},'additionalProbingPaths':['/private']}}))
  with contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):self.snapshot()
  selected_sdk_fixture(self.sdk,self.source);(cfg.parent/'dotnet.runtimeconfig.dev.json').write_text('{}')
  with contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):self.snapshot()
 def test_whole_host_fxr_and_cli_metadata_all_versions_pinned(self):
  (self.sdk/'host/fxr/11.0.0').mkdir();(self.sdk/'host/fxr/11.0.0/fxr').write_bytes(b'synthetic host');(self.sdk/'sdk-manifests/9.0.100').mkdir(parents=True);(self.sdk/'sdk-manifests/9.0.100/manifest').write_bytes(b'synthetic metadata');(self.sdk/'metadata/workloads/state').mkdir(parents=True);(self.sdk/'metadata/workloads/state/data').write_bytes(b'synthetic discovery')
  with contextlib.redirect_stdout(io.StringIO()):value=self.snapshot()
  self.assertIn('host/fxr/11.0.0/fxr',value['members']);self.assertIn('sdk-manifests/9.0.100/manifest',value['members']);self.assertIn('metadata/workloads/state/data',value['members'])
 def test_storage_counts_entire_typed_scope_and_refuses_before_effects(self):
  with contextlib.redirect_stdout(io.StringIO()):value=self.snapshot()
  source=r.physical_roster(self.source);storage=r.CustodyStorage(self.root/'storage');storage.root.mkdir();storage.bind_report_rosters(source,value);n=len(r.canonical(source))+len(r.canonical(value));self.assertEqual(storage.report_reservation,4*n+3*256*1024)
  with self.assertRaises(Refused):storage.bind_report_rosters(source,{'schema':'synthetic-oversized','selection':{'layout':'x'*r.BINARY_CAP},'members':{}})
 def test_selection_rederived_after_hash_refuses_metadata_race(self):
  original=r.selected_sdk_roster
  def mutate(root,selection,deadline,progress):
   result=original(root,selection,deadline,progress);(self.sdk/'shared/Microsoft.NETCore.App/9.0.111').mkdir();return result
  with patch.object(r,'selected_sdk_roster',side_effect=mutate),contextlib.redirect_stdout(io.StringIO()),self.assertRaises(Refused):self.snapshot()

class SDKScopeCallerChainControls(unittest.TestCase):
 def run_failed_chain(self,cas_paths=()):
  from types import SimpleNamespace
  with tempfile.TemporaryDirectory() as tmp:
   base=pathlib.Path(tmp);source=base/'source';source.mkdir();sdk=base/'sdk';sdk.mkdir();(sdk/'dotnet').write_bytes(b'synthetic; never executed');selected_sdk_fixture(sdk,source);openssl=base/'openssl';openssl.write_bytes(b'synthetic; never executed')
   fixture=Fixture(base,'diagnostic');binding=fixture.binding;selected=r.datetime.fromisoformat(binding['selectedAfter'].replace('Z','+00:00')).timestamp();stages=[];original_snapshot=r.sdk_snapshot;real_path=pathlib.Path
   def observed(*args,**kwargs):stages.append(args[2]);return original_snapshot(*args,**kwargs)
   class PureRunner:
    def __init__(self,budget,root):
     self.budget=budget;self.root=root;self.records=[]
     if root.name=='worker':budget.cas_writes=list(cas_paths)
    def run(self,argv,seconds,env,cwd=None):
     self.budget.command(seconds)
     if '--worker' in argv:
      with patch.dict(os.environ,env):code=r.worker(argv[3],json.loads(real_path(argv[4]).read_bytes()),real_path(argv[5]),real_path(argv[6]),float(argv[7]))
      return SimpleNamespace(returncode=code,stdout='',stderr='')
     if argv==['dotnet','--version']:
      raw=b'10.0.401\n';(self.root/'command-0-stdout.raw').write_bytes(raw);(self.root/'command-0-stderr.raw').write_bytes(b'');self.records.append({'index':0,'argv':argv,'actualExitCode':0,'stdout':{'file':'command-0-stdout.raw','bytes':len(raw),'sha256':r.digest(raw)},'stderr':{'file':'command-0-stderr.raw','bytes':0,'sha256':r.digest(b'')},'custody':{'leader':{'pid':9999999,'start':1},'leaderReaped':True,'remaining':[],'errors':[],'members':[]}})
      (self.root/'commands.json').write_bytes(r.canonical(self.records));return SimpleNamespace(returncode=0,stdout=raw.decode(),stderr='')
     raise AssertionError('unexpected command or SDK/install/crypto execution')
   def paths(value):
    if str(value) in {'/etc/ssl/openssl.cnf','/usr/lib/x86_64-linux-gnu/ossl-modules'}:return base/'absent-stock-fixture'
    return real_path(value)
   def which(name,**kwargs):return str(sdk/'dotnet') if name=='dotnet' else str(openssl)
   def snapshot(*args,**kwargs):return r.physical_roster(source,deadline=time.monotonic()+10)
   env={'GITHUB_RUN_ID':'99','GH_TOKEN':'synthetic','DOTNET_ROOT':str(sdk),'PATH':str(sdk)}
   with patch.dict(os.environ,env),patch.object(r,'Path',side_effect=paths),patch.object(r.time,'time',return_value=selected+1),patch.object(r,'recipient',return_value='synthetic public cert'),patch.object(r,'subreaper'),patch.object(r.shutil,'which',side_effect=which),patch.object(r,'sdk_snapshot',side_effect=observed),patch.object(r,'Runner',PureRunner),patch.object(r,'source_snapshot',side_effect=snapshot),patch.object(r,'native_context'),patch.object(r,'FiniteAPI',return_value=fixture.api),patch.object(r,'candidate',side_effect=Refused('synthetic genuine-fetch refusal')),patch.object(r,'encrypted_custody',side_effect=Refused('synthetic crypto forbidden')),patch.object(subprocess,'Popen',side_effect=AssertionError('real process forbidden')),patch.object(r.urllib.request.OpenerDirector,'open',side_effect=AssertionError('real network forbidden')),contextlib.redirect_stdout(io.StringIO()),contextlib.redirect_stderr(io.StringIO()):
    result=r.outer('diagnostic',binding,base/'operation',source)
   self.assertEqual(result,1);self.assertEqual(stages,['outer-sdk-roster','worker-sdk-roster','worker-sdk-post','outer-sdk-post'])
   worker=json.loads((base/'operation/worker/worker-report.json').read_bytes());terminal=json.loads((base/'operation/terminal.json').read_bytes());self.assertFalse(worker['success']);self.assertTrue(worker['postSourceMatches']);self.assertTrue(worker['postSdkMatches']);self.assertTrue(terminal['independentSourceMatches']);self.assertTrue(terminal['independentSdkMatches']);self.assertEqual(worker['releasePatchCount'],0);self.assertEqual(worker['casWrites'],len(cas_paths));self.assertEqual(worker['casWritePaths'],list(cas_paths));self.assertEqual(worker['sdkRoster']['selection']['policy'],r.SDK_SCOPE_POLICY)

 def test_failed_actual_outer_worker_chain_runs_four_selected_snapshots(self):self.run_failed_chain()
 def test_nonzero_failed_budget_reports_number_and_private_paths(self):
  # Counter-only fault fixture; actual CAS transport/allowlists are not invoked.
  self.run_failed_chain(('synthetic/git/blobs','synthetic/git/trees'))

if __name__=='__main__':unittest.main(verbosity=2)

class HTTPErrorCustodyControls(unittest.TestCase):
 def setUp(self):self.tmp=tempfile.TemporaryDirectory();self.root=pathlib.Path(self.tmp.name)
 def tearDown(self):self.tmp.cleanup()
 def attempt(self,name,raw=b'{"message":"Resource not accessible by integration"}',headers=None,storage=None,stream=None):
  root=self.root/name;root.mkdir();budget=r.Budget('diagnostic');budget.storage=storage
  api=r.FiniteAPI('synthetic-secret-not-for-custody',budget,root);body=stream if stream is not None else io.BytesIO(raw)
  error=r.urllib.error.HTTPError('https://api.github.com',403,'Forbidden',headers or {},body);calls=[]
  def open_(request,**kwargs):calls.append(request);raise error
  api.opener.open=open_
  with self.assertRaises(Refused) as caught:api.get(f'repos/{r.REPO}/releases/{r.RELEASE}')
  self.assertEqual(caught.exception.httpStatus,403);self.assertEqual(len(calls),1);self.assertEqual(budget.reads,1)
  self.assertEqual(budget.cas_writes,[]);self.assertEqual(budget.release_patches,0);self.assertTrue(body.closed)
  return api,root,caught.exception
 def test_original_permission_rate_empty_malformed_and_exact_cap(self):
  for index,raw in enumerate((b'{"message":"permission"}',b'{"message":"rate limit"}',b'',b'not JSON',b'x'*r.SMALL_JSON_CAP)):
   api,root,_=self.attempt(str(index),raw,{'x-ratelimit-remaining':'0','authorization':'synthetic-secret-not-for-custody','set-cookie':'secret'})
   row=api.records[-1];self.assertTrue(row['bodyComplete']);self.assertFalse(row['bodyOverflow']);self.assertEqual(row['sha256'],r.digest(raw));self.assertEqual(row['bytes'],len(raw))
   path=root/'response-github-0.raw';self.assertEqual(path.read_bytes(),raw);self.assertEqual(path.stat().st_mode&0o777,0o600)
   self.assertEqual(set(row['responseHeaders']),{'x-ratelimit-remaining'});self.assertNotIn(b'synthetic-secret-not-for-custody',(root/'transport-github.json').read_bytes())
 def test_overflow_is_explicit_prefix_never_full_digest(self):
  api,root,_=self.attempt('overflow',b'x'*(r.SMALL_JSON_CAP+1));row=api.records[-1]
  self.assertFalse(row['bodyComplete']);self.assertTrue(row['bodyOverflow']);self.assertNotIn('sha256',row);self.assertEqual(row['bytes'],r.SMALL_JSON_CAP+1);self.assertEqual(row['bodyBytesRetained'],r.SMALL_JSON_CAP);self.assertEqual(row['retainedBodySha256'],r.digest((root/'response-github-0.raw').read_bytes()))
 def test_partial_read_failure_retains_primary_and_partial_original(self):
  class Broken(io.BytesIO):
   def read(self,size=-1):
    if self.tell():raise OSError('secondary synthetic failure')
    return super().read(min(size,3))
  api,root,_=self.attempt('partial',stream=Broken(b'original'));row=api.records[-1]
  self.assertFalse(row['bodyComplete']);self.assertEqual(row['errorCaptureFailureKind'],'OSError');self.assertEqual((root/'response-github-0.raw').read_bytes(),b'ori');self.assertNotIn('sha256',row)
 def test_headers_are_bounded_allowlisted_and_explicit(self):
  api,_,_=self.attempt('headers',headers={'x-accepted-github-permissions':'x'*600,'x-github-sso':'private URL','location':'signed URL'});row=api.records[-1]
  self.assertFalse(row['headersComplete']);self.assertEqual(set(row['responseHeaders']),{'x-accepted-github-permissions'});self.assertEqual(len(row['responseHeaders']['x-accepted-github-permissions']['value'].encode()),512)
 def test_storage_and_transport_failure_never_replace_primary403(self):
  class Full:
   armed=False
   def check(self,pending=0):raise RuntimeError('synthetic full custody')
  api,_,error=self.attempt('full',storage=Full());row=api.records[-1]
  self.assertEqual(row['status'],403);self.assertEqual(row['errorCaptureFailureKind'],'RuntimeError');self.assertEqual(error.transportCaptureFailureKind,'RuntimeError');self.assertFalse(row['bodyComplete'])
 def test_existing_custody_response_role_is_charged_without_new_member(self):
  class Storage:
   armed=False
   def __init__(self):self.charges=[]
   def check(self,pending=0):self.charges.append(pending)
  storage=Storage();api,root,_=self.attempt('charged',raw=b'body',storage=storage)
  self.assertIn(4,storage.charges);self.assertEqual({p.name for p in root.iterdir()},{'response-github-0.raw','transport-github.json'})
 def test_diagnostic_direct_mutations_refuse_before_transport_or_charge(self):
  root=self.root/'guard';root.mkdir();budget=r.Budget('diagnostic');api=r.FiniteAPI('synthetic',budget,root);api.opener.open=lambda *a,**k:self.fail('mutation reached opener')
  for method,body in [('POST',None),('PATCH',{}),('PUT',{}),('GET',{})]:
   with self.subTest(method=method),self.assertRaises(Refused):api.request('https://api.github.com/repos/'+r.REPO,method=method,body=body)
  self.assertEqual(budget.reads,0);self.assertEqual(api.records,[]);self.assertEqual(list(root.iterdir()),[])
 def test_error_original_enters_existing_private_archive_only(self):
  api,root,_=self.attempt('archive',raw=b'private provider permission message')
  storage=r.CustodyStorage(root)
  class FakeEncryptionRunner:
   def __init__(self):self.budget=r.Budget('diagnostic');self.records=[{'custody':{'leaderReaped':True,'remaining':[]}}]
   def run(self,argv,*args):
    # Pure encrypted-custody caller control; no process/crypto/native qualification.
    output=pathlib.Path(argv[argv.index('-out')+1]);output.write_bytes(bytes.fromhex('060b2a864886f70d0109100117')+bytes.fromhex('060960864801650304012e')+b'synthetic ciphertext');return SimpleNamespace(returncode=0)
  with patch.object(r,'recipient',return_value='synthetic public certificate'),patch.dict(os.environ,{'GITHUB_RUN_ID':'99'}):
   summary=r.encrypted_custody(root,{'recipientSha256':'a'*64},FakeEncryptionRunner(),storage)
  with zipfile.ZipFile(root/'raw-custody.zip') as zipped:
   self.assertEqual(zipped.read('response-github-0.raw'),b'private provider permission message')
   entries=json.loads(zipped.read('custody-members.json'));entry=next(x for x in entries if x['path']=='response-github-0.raw');self.assertEqual(entry['sha256'],r.digest(zipped.read(entry['path'])))
  self.assertEqual({p.name for p in (root/'export').iterdir()},{'custody.cms','summary.json'})
  self.assertNotIn(b'private provider permission message',(root/'export/summary.json').read_bytes());self.assertEqual(summary['memberCount'],3)
 def test_stale_error_file_is_not_adopted_as_original(self):
  root=self.root/'stale';root.mkdir();target=root/'response-github-0.raw';target.write_bytes(b'stale original')
  budget=r.Budget('diagnostic');api=r.FiniteAPI('synthetic',budget,root);body=io.BytesIO(b'new error');error=r.urllib.error.HTTPError('https://api.github.com',403,'Forbidden',{},body)
  api.opener.open=lambda *a,**k:(_ for _ in ()).throw(error)
  with self.assertRaises(Refused) as caught:api.get('repos/'+r.REPO)
  self.assertEqual(caught.exception.httpStatus,403);self.assertTrue(body.closed);self.assertEqual(target.read_bytes(),b'stale original');row=api.records[-1]
  self.assertEqual(row['errorCaptureFailureKind'],'FileExistsError');self.assertEqual(row['bodyBytesRetained'],0);self.assertNotIn('retainedBodySha256',row)
 def test_error_capture_uses_original_deadline_and_no_extra_read(self):
  root=self.root/'deadline';root.mkdir();budget=r.Budget('diagnostic');api=r.FiniteAPI('synthetic',budget,root);body=io.BytesIO(b'error');error=r.urllib.error.HTTPError('https://api.github.com',403,'Forbidden',{},body)
  def open_(*args,**kwargs):budget.work=0;raise error
  api.opener.open=open_
  with self.assertRaises(Refused) as caught:api.get('repos/'+r.REPO)
  self.assertEqual(caught.exception.httpStatus,403);self.assertTrue(body.closed);self.assertEqual(budget.reads,1);row=api.records[-1]
  self.assertFalse(row['bodyComplete']);self.assertEqual(row['errorCaptureFailureKind'],'Refused');self.assertEqual(row['bytes'],0)
 def test_content_length_mismatch_is_not_complete_original(self):
  for index,length in enumerate(('100','invalid')):
   api,root,_=self.attempt('length-'+str(index),raw=b'short',headers={'content-length':length});row=api.records[-1]
   self.assertFalse(row['bodyComplete']);self.assertFalse(row['contentLengthMatches']);self.assertNotIn('sha256',row);self.assertEqual(row['retainedBodySha256'],r.digest(b'short'))
  api,_,_=self.attempt('length-match',raw=b'short',headers={'content-length':'5'});self.assertTrue(api.records[-1]['bodyComplete'])
 def test_actual_worker_durably_reports_primary403_when_transport_custody_fails(self):
  class Full:
   armed=False
   def check(self,pending=0):raise RuntimeError('private synthetic storage failure')
  api,_,failure=self.attempt('durable-error',storage=Full())
  source=self.root/'source';source.mkdir();(source/'global.json').write_text('{"sdk":{"version":"10.0.401"}}');sdk=self.root/'sdk';sdk.mkdir();(sdk/'dotnet').write_bytes(b'synthetic not executed');selected_sdk_fixture(sdk,source);root=self.root/'worker';root.mkdir();f=Fixture(self.root,'diagnostic')
  class Runner:
   def __init__(self,budget,root):self.budget=budget
   def run(self,*args,**kwargs):return SimpleNamespace(returncode=0,stdout='10.0.401',stderr='')
  with patch.object(r,'subreaper'),patch.object(r,'Runner',Runner),patch.object(r,'source_snapshot',return_value={'global.json':{'sha256':'a'*64,'link':None}}),patch.object(r,'sdk_snapshot',return_value={'members':{},'selection':{'globalConfigSha256':'a'*64}}),patch.object(r.shutil,'which',return_value=str(sdk/'dotnet')),patch.object(r,'candidate',side_effect=failure),patch.object(r,'FiniteAPI',return_value=f.api),patch.object(r,'native_context'),patch.dict(os.environ,{'GH_TOKEN':'synthetic','GITHUB_RUN_ID':'99'}),contextlib.redirect_stderr(io.StringIO()) as logs:
   self.assertEqual(r.worker('diagnostic',f.binding,root,source),1)
  report=json.loads((root/'worker-report.json').read_bytes());self.assertEqual(report['httpFailure'],{'status':403,'errorCaptureFailureKind':'RuntimeError','transportCaptureFailureKind':'RuntimeError'});self.assertFalse(report['success']);self.assertEqual(report['releasePatchCount'],0);self.assertEqual(report['casWrites'],0);self.assertTrue(report['postSourceMatches']);self.assertTrue(report['postSdkMatches']);self.assertNotIn('private synthetic storage failure',logs.getvalue())
 def test_failure_projection_is_closed_and_never_serializes_raw_fields(self):
  error=Refused('private provider body');error.httpStatus=403;error.errorCaptureFailureKind='private arbitrary string';error.responseHeaders={'authorization':'secret'}
  self.assertEqual(r.http_failure_projection(error),{'status':403,'errorCaptureFailureKind':'unclassified'})
  for value in (True,'403',None,99,600):error.httpStatus=value;self.assertIsNone(r.http_failure_projection(error))

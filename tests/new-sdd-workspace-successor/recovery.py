"""Pure production recovery callers. Synthetic bodies/processes; no SDK/network/crypto."""
import base64,copy,hashlib,io,json,os,pathlib,subprocess,sys,tempfile,unittest,zipfile
from types import SimpleNamespace
from unittest.mock import patch
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[2]/'scripts'))
import new_sdd_workspace_promote_recovery as r
from release_successor_journal import ProtectedReleaseJournal,canonical,SCHEMA,REPOSITORY as AUTHORITY,Refused as JournalRefused
from release_successor_execution import Refused

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
  if '/compare/' in path:return {'status':self.f.ancestry}
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
  path=self.f.root/f'read-{len(self.f.paths)}';path.write_bytes(data);return path
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
  self.cid,self.effects=r.effects(self.manifest)
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
  self.admission=r.WizardAdmission(self.api,self.manifest,self.main,99,'EHotwagner','refs/heads/main')
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
  sdk=self.root/'sdk';sdk.mkdir();(sdk/'dotnet').write_bytes(b'synthetic-sdk-not-executed')
  root=self.root/'worker';root.mkdir();calls=[]
  class Runner:
   def __init__(self,budget,root):self.budget=budget
   def run(self,*args,**kwargs):return subprocess.CompletedProcess(args[0],0,'10.0.401','')
  def snapshot(*args):calls.append('source');return {'source':'unchanged'}
  def roster(*args,**kwargs):calls.append('sdk');return {'sdk':'unchanged'}
  with patch.object(r,'subreaper'),patch.object(r,'Runner',Runner),patch.object(r,'source_snapshot',side_effect=snapshot),patch.object(r,'physical_roster',side_effect=roster),patch.object(r.shutil,'which',return_value=str(sdk/'dotnet')),patch.object(r,'candidate',side_effect=Refused('bounded native feed failure')),patch.object(r,'FiniteAPI',return_value=f.api),patch.dict(os.environ,{'GH_TOKEN':'mock','GITHUB_RUN_ID':'99'}):
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

if __name__=='__main__':unittest.main(verbosity=2)

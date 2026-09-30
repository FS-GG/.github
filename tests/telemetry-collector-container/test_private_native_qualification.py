#!/usr/bin/env python3
import base64, contextlib, hashlib, importlib.util, io, json, os, pathlib, subprocess, tempfile, types, unittest
from unittest import mock
ROOT=pathlib.Path(__file__).resolve().parents[2]
def load():
 p=ROOT/'deployment/telemetry-collector/qualify_native_container.py'; s=importlib.util.spec_from_file_location('qnc',p); m=importlib.util.module_from_spec(s); s.loader.exec_module(m); return m
q=load()
class FakeRunner:
 def __init__(self): self.calls=[]
 def run(self,args,**kw):
  self.calls.append(args)
  out=b''
  if args[0]=='git' and args[-2:]==['rev-parse','HEAD']: out=(self.head+'\n').encode()
  elif args[-1]=='--version': out=b'0.94.0.0\n'
  return subprocess.CompletedProcess(args,0,out,b'')
class Tests(unittest.TestCase):
 @staticmethod
 def workflow_shell(label):
  lines=(ROOT/'deployment/telemetry-collector/private-native-qualification.yml.in').read_text().splitlines()
  start=next(i for i,row in enumerate(lines) if label in row)
  run=next(i for i in range(start,len(lines)) if lines[i].strip()=='run: |')
  body=[]
  for row in lines[run+1:]:
   if row.startswith('      - name:'): break
   if row.strip():
    if not row.startswith('          '): raise AssertionError('workflow shell indentation escaped YAML scalar')
    body.append(row[10:])
   else: body.append('')
  return '\n'.join(body)+'\n'
 @classmethod
 def operation_shell(cls): return cls.workflow_shell('Run once, preserve custody')
 def test_host_scope_and_custody_are_exact(self):
  a=type('A',(),{})(); a.private_root=pathlib.Path('/private/run'); a.run_nonce='run-0001'; a.source_sha='a'*40; a.private_placement_sha='d'*40
  op=q.Operation(a,FakeRunner()); c=op.host_config()
  self.assertEqual('https://0.0.0.0:7443',c['ListenUrl']); self.assertEqual(3,len(c['Credentials']))
  p=c['Credentials'][0]; self.assertEqual(('v2-host-native-qualification','native-prospective-v1','roadmap','generic'),(p['WorkspaceId'],p['ProducerId'],p['StreamId'],p['Role']))
  n=c['Credentials'][1]; self.assertEqual(('native-collector','grant-native-collector-v1'),(n['Role'],n['GrantId']))
  self.assertTrue(c['Credentials'][2]['Revoked'])
 def test_preflight_happens_before_auth_access(self):
  source=ROOT; r=FakeRunner(); r.head='b'*40
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); (t/'manifest').write_text('{}'); (t/'journal').write_text('{}')
   a=type('A',(),{'private_root':t/'private','run_nonce':'run-0001','source_root':source,'source_sha':'a'*40,'private_placement_sha':'d'*40,'host_manifest':t/'manifest','host_journal':t/'journal','native_executable':t/'missing','coord_root':t})()
   op=q.Operation(a,r)
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':'SECRET'},clear=True):
    with self.assertRaisesRegex(q.Refusal,'source-head-drift'): op.preflight()
    self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ)
 def test_template_is_manual_private_exact_and_never_logs_secret(self):
  text=(ROOT/'deployment/telemetry-collector/private-native-qualification.yml.in').read_text()
  self.assertIn('workflow_dispatch:',text); self.assertNotIn('pull_request:',text); self.assertNotIn('push:',text)
  self.assertIn("github.repository == 'FS-GG/FS.GG.GitHub.Substrate.Sandbox'",text)
  self.assertIn('persist-credentials: false',text); self.assertIn('retention-days: 1',text)
  self.assertIn("repository: FS-GG/.github",text); self.assertIn("if: always()",text)
  self.assertIn("trap 'interrupted=1' INT TERM",text); self.assertIn("trap '' INT TERM",text); self.assertIn("@@RECIPE_SOURCE_SHA@@",text)
  self.assertNotIn("@@SOURCE_SHA@@",text)
  self.assertEqual(1,text.count('${{ inputs.placement_sha }}')); self.assertIn('[[ "$PLACEMENT_SHA" =~ ^[0-9a-f]{40}$ ]]',text)
  self.assertIn('podman pull --platform linux/amd64',text); self.assertIn('timeout-minutes: 40',text)
  self.assertNotIn('trap finalize EXIT',text); self.assertIn('exit "$final_rc"',text)
  self.assertNotIn('echo $FSGG_NATIVE_AUTH',text); self.assertNotIn('--auth-json',text)
 def test_rendered_workflow_parses_and_each_shell_block_has_valid_syntax(self):
  if importlib.util.find_spec('yaml') is None: self.skipTest('PyYAML unavailable')
  import yaml
  text=(ROOT/'deployment/telemetry-collector/private-native-qualification.yml.in').read_text()
  rendered=text.replace('@@RECIPE_SOURCE_SHA@@','a'*40).replace('@@QUALIFICATION_REF@@','qualification-v1')
  value=yaml.safe_load(rendered); jobs=value.get('jobs'); self.assertIsInstance(jobs,dict)
  scripts=[step['run'] for step in jobs['qualify']['steps'] if 'run' in step]
  self.assertEqual(2,len(scripts))
  for script in scripts:
   checked=subprocess.run(['bash','-n'],input=script,text=True,capture_output=True)
   self.assertEqual(0,checked.returncode,checked.stderr)
 def test_malformed_placement_is_refused_before_git_or_podman(self):
  script=self.workflow_shell('Refuse non-exact placement')
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); marker=t/'called'; fake=t/'bin'; fake.mkdir()
   for name in ('git','podman','openssl'):
    path=fake/name; path.write_text(f'#!/bin/bash\ntouch "{marker}"\nexit 0\n'); path.chmod(0o700)
   env={'PATH':str(fake)+':/usr/bin:/bin','GITHUB_EVENT_NAME':'workflow_dispatch',
        'GITHUB_REPOSITORY':'FS-GG/FS.GG.GitHub.Substrate.Sandbox','PLACEMENT_SHA':'$(touch injected)',
        'RUN_NONCE':'run-0001','GITHUB_SHA':'a'*40,'RECIPE_SHA':'b'*40}
   refused=subprocess.run(['bash'],input=script,text=True,cwd=t,env=env,capture_output=True)
   self.assertNotEqual(0,refused.returncode); self.assertFalse(marker.exists())
 def test_effect_admission_is_bound_before_private_root_or_auth_read(self):
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); a=type('A',(),{'private_root':t/'private','run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
   op=q.Operation(a,FakeRunner())
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':base64.b64encode(b'{}').decode(),
                                    'FSGG_PRIVATE_EFFECT_ADMISSION':'0'*64},clear=True):
    with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): op.materialize()
    self.assertFalse((t/'private').exists())
    self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ)
 def test_full_operation_sequence_is_real_and_bounded(self):
  source=(ROOT/'deployment/telemetry-collector/qualify_native_container.py').read_text()
  ordered=['prepare_context_and_images','zero-auth-images-qualified','materialize(); op.execute()',
           "'native-readonly-source'",'read-only-source-compatible',"'collect-native'","'export-learning'",'learn-01-analysis.py','wrong-native-selector-was-admitted',
           'receiver-restart-export-drift']
  for value in ordered: self.assertIn(value,source)
  self.assertIn('op.preflight(); op.prepare_context_and_images(); op.r=Runner(time.monotonic()+600); op.materialize(); op.execute()',source)
  self.assertIn("probe('prospective.token',False); probe('collector.token',True); probe('revoked.token',True)",source)
  self.assertIn('cleanup=Runner(time.monotonic()+60)',source)
  self.assertIn("signal.signal(signal.SIGTERM,interrupted)",source)
  self.assertNotIn('prepared-source-only',source)
  for token in ('inspect_container(\'fsgg-native-collector\',t.inspect_receiver)',
                "inspect_container('fsgg-native-egress',t.inspect_egress)",
                "inspect_container('fsgg-native-development',t.inspect_native)",
                'require_collection_handoff','--userns=keep-id:uid=32768,gid=32768'):
   self.assertIn(token,source)
 def test_materialization_creates_driver_output_before_native_start(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td); staging=root/'staging'; (staging/'tls').mkdir(parents=True)
   for name in ('password','key.pem','native-receiver.crt','receiver.pfx'): (staging/'tls'/name).write_bytes(b'x')
   nonce='run-0001'; source='a'*40
   a=type('A',(),{'private_root':root/'private','staging_root':staging,'run_nonce':nonce,'source_sha':source,'private_placement_sha':'d'*40})()
   admission=hashlib.sha256((nonce+'\0'+source+'\0'+q.PROFILE_SHA+'\0'+q.OPERATION).encode()).hexdigest()
   with mock.patch.dict(os.environ,{'FSGG_PRIVATE_EFFECT_ADMISSION':admission,
                                    'FSGG_NATIVE_AUTH_JSON_B64':base64.b64encode(b'{}').decode()},clear=True):
    q.Operation(a,FakeRunner()).materialize()
   output=root/'private/native/qualification-output'
   self.assertTrue(output.is_dir()); self.assertEqual(0o700,output.stat().st_mode&0o777)
 def test_driver_summary_binds_original_private_full_result(self):
  with tempfile.TemporaryDirectory() as td:
   path=pathlib.Path(td)/'result.json'; nonce='run-0001'
   value={'schema':'fsgg.telemetry.native-operation-result/1','status':'qualified','operationId':q.OPERATION,
          'runNonce':nonce,'parentThreadId':'11111111-1111-1111-1111-111111111111','nativeAgent':'child_agent'}
   path.write_text(json.dumps(value)); path.chmod(0o600)
   summary={'schema':value['schema'],'status':'qualified','resultSha256':hashlib.sha256(path.read_bytes()).hexdigest()}
   self.assertEqual('child_agent',q.native_operation_result(json.dumps(summary).encode(),path,nonce)['nativeAgent'])
   summary['resultSha256']='0'*64
   with self.assertRaisesRegex(q.Refusal,'digest'): q.native_operation_result(json.dumps(summary).encode(),path,nonce)
 def test_custody_status_never_calls_missing_or_failed_auth_sealed(self):
  for auth,cleanup,interrupted,expected in (('sealed','true','false',True),('not-materialized','true','false',False),
                                            ('missing','true','false',False),('seal-failed','true','false',False),
                                            ('writer-stop-failed','false','true',False)):
   with self.subTest(auth=auth), tempfile.TemporaryDirectory() as td:
    path=pathlib.Path(td)/'result.json'; nonce='run-0001'; source='a'*40
    q.custody_status(['--result',str(path),'--run-nonce',nonce,'--source-sha',source,'--operation-exit','0',
                      '--cleanup-ok',cleanup,'--interrupted',interrupted,'--auth-state',auth,'--evidence-state','not-present'])
    value=json.loads(path.read_text()); self.assertEqual(expected,value['custodySealed'])
    if auth not in {'sealed','not-materialized'} or cleanup=='false' or interrupted=='true':
     self.assertEqual('custody-incomplete',value['disposition'])
    if auth=='not-materialized': self.assertTrue(value['preservationComplete'])
 def test_workflow_finalization_propagates_seal_failure_and_preserves_plaintext(self):
  script=self.operation_shell(); self.assertEqual(0,subprocess.run(['bash','-n'],input=script,text=True).returncode)
  for seal_fail,expected_rc in ((False,0),(True,86)):
   with self.subTest(seal_fail=seal_fail), tempfile.TemporaryDirectory() as td:
    t=pathlib.Path(td); fake=t/'bin'; fake.mkdir(); (t/'placement/_private-inputs/custody').mkdir(parents=True)
    (t/'placement/_private-inputs/custody/root-public.pem').write_text('public-test-only')
    (t/'recipe').symlink_to(ROOT,target_is_directory=True)
    (fake/'python3').write_text('''#!/bin/bash
if [[ "$1" == recipe/deployment/telemetry-collector/qualify_native_container.py && "$2" != custody-status ]]; then
  mkdir -p "$PRIVATE_ROOT/native/.codex" "$PRIVATE_ROOT/native/qualification-output/run-0001" "$PRIVATE_ROOT/output"
  printf '{}\\n' > "$PRIVATE_ROOT/native/.codex/auth.json"
  printf '{"schema":"fsgg.telemetry.private-native-qualification/1","runNonce":"run-0001","sourceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","phases":[{"name":"private-material-ready"}],"disposition":"source-operation-complete","cleanupComplete":true}\\n' > "$RESULT_ROOT/result.json"
  exit 0
fi
exec /usr/bin/python3 "$@"
''')
    (fake/'podman').write_text('#!/bin/bash\n[[ "$1" == ps ]] && exit 0\nexit 1\n')
    (fake/'timeout').write_text('#!/bin/bash\nshift\nexec "$@"\n')
    (fake/'node').write_text('''#!/bin/bash
test "${SEAL_FAIL:-0}" = 0 || exit 7
printf 'ciphertext-test-only\\n' > "$5"
''')
    (fake/'tar').write_text('''#!/bin/bash
while (($#)); do if [[ "$1" == -cf ]]; then printf 'private-test-only\\n' > "$2"; exit 0; fi; shift; done
exit 2
''')
    for path in fake.iterdir(): path.chmod(0o700)
    env={'PATH':str(fake)+':/usr/bin:/bin','RESULT_ROOT':str(t/'result'),'PRIVATE_ROOT':str(t/'private'),
         'STAGING_ROOT':str(t/'staging'),'RUN_NONCE':'run-0001','RECIPE_SHA':'a'*40,'PLACEMENT_SHA':'d'*40,
         'PROFILE_SHA':q.PROFILE_SHA,'GITHUB_WORKSPACE':str(t),'FSGG_NATIVE_AUTH_JSON_B64':'test',
         'FSGG_PRIVATE_EFFECT_ADMISSION':'test','SEAL_FAIL':'1' if seal_fail else '0'}
    completed=subprocess.run(['bash'],input=script,text=True,cwd=t,env=env,capture_output=True)
    self.assertEqual(expected_rc,completed.returncode,completed.stderr)
    result=json.loads((t/'result/result.json').read_text())
    if seal_fail:
     self.assertEqual((False,'seal-failed','custody-incomplete'),(result['custodySealed'],result['authCustody'],result['disposition']))
     self.assertTrue((t/'private/native/.codex/auth.json').exists())
    else:
     self.assertTrue(result['custodySealed']); self.assertFalse((t/'private').exists())
 def test_cleanup_checks_every_owned_resource_and_reports_survivors(self):
  class CleanupRunner:
   def __init__(self,survives=False): self.calls=[]; self.survives=survives
   def run(self,args,**kw):
    self.calls.append(args); return subprocess.CompletedProcess(args,0 if (self.survives and 'exists' in args) else (1 if 'exists' in args else 0),b'',b'')
  a=type('A',(),{'private_root':pathlib.Path('/private/run'),'run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
  op=q.Operation(a,FakeRunner()); op.containers=['one','two']; op.networks=['three']
  clean=CleanupRunner()
  with mock.patch.object(q,'Runner',return_value=clean): self.assertEqual([],op.cleanup())
  self.assertEqual(3,sum('exists' in call for call in clean.calls))
  dirty=CleanupRunner(True)
  with mock.patch.object(q,'Runner',return_value=dirty): failures=op.cleanup()
  self.assertEqual(3,len(failures))
 def test_owned_run_is_registered_before_timeout_and_dynamic_refusal_still_cleans(self):
  class TimeoutRunner:
   def run(self,args,**kw): raise subprocess.TimeoutExpired(args,1)
  a=type('A',(),{'private_root':pathlib.Path('/private/run'),'run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
  op=q.Operation(a,TimeoutRunner())
  with self.assertRaises(subprocess.TimeoutExpired): op.owned_run(['podman','run','--rm','image'])
  self.assertEqual(['fsgg-native-owned-001'],op.containers)
  class DynamicTopologyRefusal(Exception): pass
  class FakeOperation:
   cleaned=False
   def __init__(self,args,runner): self.result={'schema':q.SCHEMA,'runNonce':args.run_nonce,'sourceSha':args.source_sha,'phases':[],'disposition':'incomplete'}
   def preflight(self): raise DynamicTopologyRefusal('private topology detail')
   def prepare_context_and_images(self): pass
   def materialize(self): pass
   def execute(self): pass
   def cleanup(self): FakeOperation.cleaned=True; return []
   def write_result(self): pass
  with tempfile.TemporaryDirectory() as td:
   a=type('A',(),{'operation_id':q.OPERATION,'source_root':ROOT,'source_sha':'a'*40,'private_placement_sha':'d'*40,
                  'private_root':pathlib.Path(td)/'private','staging_root':pathlib.Path(td)/'staging','run_nonce':'run-0001'})()
   with mock.patch.object(q,'parse',return_value=a), mock.patch.object(q,'Operation',FakeOperation):
    self.assertEqual(2,q.main([]))
  self.assertTrue(FakeOperation.cleaned)
 def test_execute_dataflow_uses_full_driver_result_inspection_handoff_and_analyzer(self):
  nonce='run-0001'; parent='11111111-1111-1111-1111-111111111111'; batch='b'*32+'-000004'; dg='c'*64
  class ProcessRunner:
   def __init__(self,root,summary): self.root=root; self.summary=summary; self.calls=[]
   def run(self,args,**kw):
    self.calls.append(args); out=b''
    joined=' '.join(args)
    if 'fsgg-native-readonly-probe' in joined and args[:3]==['podman','start','-a']:
     out=json.dumps({'schema':'fsgg.telemetry.native-source-readback/1','status':'compatible','resultSha256':'d'*64}).encode()
    elif 'fsgg-native-development' in joined and args[:3]==['podman','start','-a']: out=json.dumps(self.summary).encode()
    elif 'receipt-probe.py' in joined: out=json.dumps({'receiptRefused':True} if '--expect-refusal' in args else {'receiptRecovered':True}).encode()
    elif 'learn-01-analysis.py' in joined: out=b'{"analysis":"accepted"}'
    return subprocess.CompletedProcess(args,0,out,b'')
  class Topology:
   NATIVE_NETWORK='private'
   def __init__(self): self.inspected=[]
   def readonly_probe_create(self,*a): return ['podman','create','fsgg-native-readonly-probe']
   def network_create_commands(self): return [['podman','network','create','private']]
   def collector_create(self,*a): return ['podman','create','fsgg-native-collector']
   def receiver_connect_command(self): return ['podman','network','connect','fsgg-native-collector']
   def egress_create(self,*a): return (['podman','create','fsgg-native-egress'],['podman','network','connect','fsgg-native-egress'])
   def native_create(self,*a): return ['podman','create','fsgg-native-development']
   def inspect_receiver(self,v): pass
   def inspect_egress(self,v): pass
   def inspect_native(self,v): pass
   def require_collection_handoff(self,development,receiver):
    if development or receiver: raise AssertionError('writer remained')
   def snapshot_native_volume(self,root): return types.SimpleNamespace(device=1,inode=2,digest='same',files=1)
   def require_same_original_volume(self,a,b): self.same=(a.device,a.inode)==(b.device,b.inode)
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td); private=root/'private'
   for name in ('native/qualification-output/'+nonce,'credentials','output','evidence','store','tls'):
    (private/name).mkdir(parents=True,mode=0o700)
   for name in ('prospective.token','collector.token','revoked.token'): (private/'credentials'/name).write_text('x'*48)
   full={'schema':'fsgg.telemetry.native-operation-result/1','status':'qualified','operationId':q.OPERATION,'runNonce':nonce,
         'parentThreadId':parent,'nativeAgent':'child_agent'}
   result_path=private/'native/qualification-output'/nonce/'result.json'; result_path.write_text(json.dumps(full)); result_path.chmod(0o600)
   summary={'schema':full['schema'],'status':'qualified','resultSha256':hashlib.sha256(result_path.read_bytes()).hexdigest()}
   (result_path.parent/'telemetry-receipts.json').write_text(json.dumps({'receipts':[{'operation':'child-started','batchId':batch,'digest':dg}]}))
   a=type('A',(),{'private_root':private,'run_nonce':nonce,'source_sha':'a'*40,'private_placement_sha':'d'*40,'source_root':ROOT})()
   topology=Topology(); runner=ProcessRunner(private,summary)
   class IntegratedOperation(q.Operation):
    def topology(self): return topology
    def initialize_store(self): pass
    def inspect_container(self,name,inspector): topology.inspected.append(inspector.__name__)
    def container_running(self,name): return False
    def admin(self,*args,check=True):
     if args[0]=='export-learning': return subprocess.CompletedProcess(args,0,b'{"stable":true}',b'')
     if args[0]=='collect-native' and 'wrong-selector' in args: return subprocess.CompletedProcess(args,2,b'',b'')
     return subprocess.CompletedProcess(args,0,b'{"collected":true}',b'')
   op=IntegratedOperation(a,runner); op.images={name:'sha256:'+'e'*64 for name in ('native-readonly-source','native-collector','native-egress','native-development')}
   with mock.patch.dict(os.environ,{q.CREDENTIAL_ENV:'p'*48},clear=False): op.execute()
   self.assertEqual(['inspect_receiver','inspect_egress','inspect_native'],topology.inspected)
   self.assertTrue(topology.same); self.assertEqual('source-operation-complete',op.result['disposition'])
   analyzer=next(call for call in runner.calls if any(item.endswith('/learn-01-analysis.py') for item in call))
   self.assertIn('--userns=keep-id:uid=32768,gid=32768',analyzer); self.assertIn('--read-only',analyzer)
 def test_receipt_probe_requires_exact_applied_receipt_without_disclosing_token(self):
  batch='a'*32+'-000004'; digest='b'*64
  receipt={'schema':'fsgg.telemetry.receipt/1','workspaceId':q.SCOPE[0],'producerId':q.SCOPE[1],
           'streamId':q.SCOPE[2],'batchId':batch,'digest':digest,'status':'applied','code':None}
  response=mock.MagicMock(); response.status=200; response.read.return_value=json.dumps(receipt).encode()
  response.__enter__.return_value=response
  with mock.patch.dict(os.environ,{'FSGG_RECEIPT_TOKEN':'secret-token-that-is-long-enough-0000'},clear=True), \
       mock.patch.object(q.urllib.request,'urlopen',return_value=response), io.StringIO() as output, contextlib.redirect_stdout(output):
   self.assertEqual(0,q.receipt_probe(['--url','https://native-receiver:7443/v1/receipts/'+batch,
                                      '--batch',batch,'--expected-digest',digest]))
   result=json.loads(output.getvalue()); self.assertTrue(result['receiptRecovered'])
   self.assertNotIn('secret-token',output.getvalue())
 def test_expired_runner_refuses_without_starting_process(self):
  runner=q.Runner(q.time.monotonic()-1)
  with mock.patch.object(q.subprocess,'run') as process, self.assertRaisesRegex(q.Refusal,'operation-deadline'):
   runner.run(['true'])
  process.assert_not_called()
 def test_sealer_uses_standard_aead_wrap_and_identity_aad(self):
  text=(ROOT/'deployment/telemetry-collector/seal_native_custody.mjs').read_text()
  for token in ('aes-256-gcm','RSA_PKCS1_OAEP_PADDING',"oaepHash:'sha256'",'setAAD','randomBytes(32)','randomBytes(12)'): self.assertIn(token,text)
  self.assertNotIn('createPrivateKey',text)
 def test_seal_roundtrip_and_wrong_aad_refusal(self):
  if subprocess.run(['which','node'],capture_output=True).returncode: self.skipTest('node absent')
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); subprocess.run(['openssl','genpkey','-algorithm','RSA','-pkeyopt','rsa_keygen_bits:2048','-out',t/'private.pem'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL); subprocess.run(['openssl','pkey','-in',t/'private.pem','-pubout','-out',t/'public.pem'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
   (t/'plain').write_bytes(b'private-custody\n'); script=ROOT/'deployment/telemetry-collector/seal_native_custody.mjs'; run='run-0001'; source='a'*40; profile='b'*64
   subprocess.run(['node',script,'seal',t/'plain',t/'public.pem',t/'capsule',run,source,profile],check=True)
   subprocess.run(['node',script,'unseal',t/'capsule',t/'private.pem',t/'clear',run,source,profile],check=True)
   self.assertEqual(b'private-custody\n',(t/'clear').read_bytes()); self.assertEqual(0o600,(t/'capsule').stat().st_mode&0o777)
   bad=subprocess.run(['node',script,'unseal',t/'capsule',t/'private.pem',t/'bad',run,'c'*40,profile],capture_output=True)
   self.assertNotEqual(0,bad.returncode); self.assertFalse((t/'bad').exists())
if __name__=='__main__': unittest.main(verbosity=2)

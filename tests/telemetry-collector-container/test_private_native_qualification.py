#!/usr/bin/env python3
import base64, contextlib, importlib.util, io, json, os, pathlib, subprocess, tempfile, unittest
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
  self.assertIn("trap finalize EXIT INT TERM",text); self.assertIn("@@RECIPE_SOURCE_SHA@@",text)
  self.assertNotIn("@@SOURCE_SHA@@",text)
  self.assertNotIn('echo $FSGG_NATIVE_AUTH',text); self.assertNotIn('--auth',text)
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
  self.assertIn('op.preflight(); op.prepare_context_and_images(); op.materialize(); op.execute()',source)
  self.assertIn("probe('prospective.token',False); probe('collector.token',True); probe('revoked.token',True)",source)
  self.assertIn('cleanup=Runner(time.monotonic()+60)',source)
  self.assertNotIn('prepared-source-only',source)
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

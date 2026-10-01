import fcntl,importlib.util,json,os,pathlib,stat,subprocess,sys,tempfile,time,unittest
ROOT=pathlib.Path(__file__).resolve().parents[3]
MOD=ROOT/'deployment/telemetry-collector/persistent/receiver.py'
PROFILE=ROOT/'deployment/telemetry-collector/persistent/profile.json'
spec=importlib.util.spec_from_file_location('persistent_receiver',MOD); r=importlib.util.module_from_spec(spec); spec.loader.exec_module(r)

def private_parent(temp):
 p=pathlib.Path(temp)/'owner'; p.mkdir(mode=0o700); return p
def write_private(path,data=b'x'):
 r.write_new(path,data)
def seal_source(root,data=b'original-source'):
 source=root/'receiver/private/codex-home'; write_private(source/'native.json',data); (source/'native.json').chmod(0o400); source.chmod(0o500)
def prepare_runtime(profile,root):
 store=root/'receiver/private/store'; store.mkdir(mode=0o700); write_private(store/'host.lock'); write_private(store/'receipts.db',b'sqlite-fixture')
 # The configured service lock is adjacent to host.json, matching the real Host config.
 write_private(root/'receiver/private/host.lock')
 sidecar,receipt=r.expected_installer_outputs(profile,root); write_private(root/'receiver/private/host.json.native-collector.json',sidecar); write_private(root/'receiver/private/host.json.native-collector.receipt.json',receipt)
 seal_source(root)
class ReceiverTests(unittest.TestCase):
 def profile(self): return json.loads(PROFILE.read_text())
 def test_inspect_is_effect_free_and_uses_real_command_contracts(self):
  p=self.profile(); result=r.plan(p); c=result['commands']; paths=p['containerPaths']
  self.assertFalse(result['effectsPerformed']); self.assertFalse(result['hostStateInitialized']); self.assertFalse(result['installerApplied'])
  self.assertEqual(paths['hostConfig']+'.native-collector.json',result['managerGenerated']['sidecar'])
  self.assertEqual(['init','--root',paths['store'],'--workspace',p['workspaceId']],c['init'][-5:])
  self.assertIn('enroll-producer',c['enroll']); self.assertEqual('2',c['install'][c['install'].index('--installation-version')+1])
  self.assertEqual(paths['codexHome'],c['install'][c['install'].index('--codex-home')+1]); self.assertEqual(paths['evidence'],c['install'][c['install'].index('--evidence-root')+1])
  self.assertEqual('backup-stopped-host',c['backup'][1]); self.assertEqual('0dd4aa26aca6697f1cc3cece762a4ecae60b5b81',p['manager']['coordinationSource'])
 def test_initialize_is_preparation_not_fake_installation(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; result=r.initialize(self.profile(),root,True)
   self.assertEqual('prepared',result['status']); self.assertFalse(result['hostStateInitialized']); self.assertFalse(result['producerEnrolled']); self.assertFalse(result['installerApplied'])
   self.assertFalse((root/'receiver/private/store').exists()); self.assertFalse((root/'receiver/private/host.json.native-collector.json').exists())
   config=json.loads((root/'receiver/private/host.json').read_text()); self.assertEqual('/receiver/private/host.lock',config['ServiceLockPath'])
   self.assertEqual('exact-replay',r.initialize(self.profile(),root,True)['status'])
 def test_actual_installer_output_shape_and_adjacent_paths_validate(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root)
   r.validate_installer_outputs(self.profile(),root)
   sidecar=json.loads((root/'receiver/private/host.json.native-collector.json').read_text()); self.assertEqual('/receiver/private/codex-home',sidecar['CodexHome']); self.assertEqual('/receiver/private/evidence',sidecar['EvidenceRoot'])
 def test_dotdot_repeated_separator_and_private_alias_refuse(self):
  for value in ('/producer/../receiver/private','/producer//native-source','/producer/./native-source'):
   p=self.profile(); p['containerPaths']['nativeSource']=value
   with self.assertRaises(r.Refusal,msg=value): r.validate_profile(p)
  p=self.profile(); p['containerPaths']['nativeSource']='/receiver/private/child'
  with self.assertRaises(r.Refusal): r.validate_profile(p)
 def test_nested_development_mounts_refuse(self):
  p=self.profile(); p['containerPaths']['producerSpool']='/producer/native-source/spool'
  with self.assertRaises(r.Refusal): r.validate_profile(p)
 def test_interrupted_initialization_leaves_no_root(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'
   with self.assertRaises(RuntimeError): r.initialize(self.profile(),root,True,fault='before-commit')
   self.assertFalse(root.exists()); self.assertEqual([],list(parent.iterdir()))
 def test_parent_replacement_refuses_and_does_not_write_replacement(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; saved=pathlib.Path(t)/'saved-owner'
   def replace(path): path.rename(saved); path.mkdir(mode=0o700)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),root,True,fault=replace)
   self.assertEqual([],list(parent.iterdir())); self.assertEqual([],list(saved.iterdir()))
 def test_symlink_ancestor_and_public_custody_refuse(self):
  with tempfile.TemporaryDirectory() as t:
   base=pathlib.Path(t); actual=base/'actual'; actual.mkdir(mode=0o700); private=actual/'private'; private.mkdir(mode=0o700); alias=base/'alias'; alias.symlink_to(actual,target_is_directory=True)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),alias/'private'/'receiver',True)
   public=base/'public'; public.mkdir(mode=0o755)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),public/'receiver',True)
 def test_changed_pin_and_grant_refuse(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True)
   changed=self.profile(); changed['reader']['profileSha256']='0'*64
   with self.assertRaises(r.Refusal): r.initialize(changed,root,True)
   config=root/'receiver/private/host.json'; value=json.loads(config.read_text()); value['Credentials'][0]['GrantId']='changed'; config.write_text(json.dumps(value)); config.chmod(0o600)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),root,True)
 def test_live_actual_lock_refuses_backup(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root); ready=parent/'ready'; lock=root/'receiver/private/host.lock'
   code='import fcntl,os,pathlib,sys,time; f=os.open(sys.argv[1],os.O_RDWR); fcntl.flock(f,fcntl.LOCK_EX); pathlib.Path(sys.argv[2]).write_text("ready"); time.sleep(30)'
   process=subprocess.Popen([sys.executable,'-c',code,str(lock),str(ready)])
   try:
    for _ in range(100):
     if ready.exists(): break
     time.sleep(.01)
    self.assertTrue(ready.exists())
    with self.assertRaisesRegex(r.Refusal,'host-writer-not-settled'): r.backup(self.profile(),root,parent/'backup',True)
    self.assertFalse((parent/'backup').exists())
   finally: process.terminate(); process.wait(timeout=5)
 def test_unsealed_or_empty_native_source_refuses_backup(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root)
   source=root/'receiver/private/codex-home'; source.chmod(0o700); (source/'native.json').chmod(0o600)
   with self.assertRaises(r.Refusal): r.backup(self.profile(),root,parent/'backup',True)
 def test_source_change_during_backup_refuses_and_removes_output(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root)
   def change(source):
    source.chmod(0o700); item=source/'native.json'; item.chmod(0o600); item.write_bytes(b'changed-during-copy'); item.chmod(0o400); source.chmod(0o500)
   with self.assertRaisesRegex(r.Refusal,'native-source-changed-refused'): r.backup(self.profile(),root,parent/'backup',True,source_fault=change)
   self.assertFalse((parent/'backup').exists())
 def test_stopped_boundary_backup_and_restore_retain_source_and_private_unit(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root)
   write_private(root/'receiver/private/credentials/receiver.pfx',b'tls'); write_private(root/'receiver/private/evidence/capture.json',b'{}')
   backup=parent/'backup'; result=r.backup(self.profile(),root,backup,True)
   self.assertEqual('created-from-exclusive-stopped-boundary',result['status']); self.assertTrue(result['quiescence']['exclusiveHostLockHeld'])
   manifest=json.loads((backup/'backup.json').read_text()); self.assertIn('quiesced-store',manifest['covers']); self.assertIn('retained-native-source',manifest['covers'])
   restored=parent/'restored'; restored_result=r.restore(self.profile(),backup,restored,True); self.assertEqual('restored-inactive-with-retained-source',restored_result['status'])
   self.assertEqual(b'original-source',(restored/'receiver/private/codex-home/native.json').read_bytes()); self.assertEqual(b'sqlite-fixture',(restored/'receiver/private/store/receipts.db').read_bytes())
   self.assertEqual([],list((restored/'producer/spool').iterdir())); self.assertFalse(restored_result['activationAuthorized'])
 def test_backup_parent_replacement_refuses_without_writing_replacement(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root); saved=pathlib.Path(t)/'saved-owner'
   def replace(path): path.rename(saved); path.mkdir(mode=0o700)
   with self.assertRaises(r.Refusal): r.backup(self.profile(),root,parent/'backup',True,fault=replace)
   self.assertEqual([],list(parent.iterdir())); self.assertTrue((saved/'receiver').is_dir()); self.assertFalse((saved/'backup').exists())
 def test_changed_backup_or_missing_source_refuses_without_output(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); prepare_runtime(self.profile(),root); backup=parent/'backup'; r.backup(self.profile(),root,backup,True)
   source=backup/'receiver/private/codex-home/native.json'; source.chmod(0o600); source.write_bytes(b'changed'); source.chmod(0o400); target=parent/'restored'
   with self.assertRaises(r.Refusal): r.restore(self.profile(),backup,target,True)
   self.assertFalse(target.exists())
 def test_default_cli_has_no_effect(self):
  with tempfile.TemporaryDirectory() as t:
   target=pathlib.Path(t)/'not-created'; rc=r.main(['initialize','--profile',str(PROFILE),'--root',str(target)])
   self.assertEqual(0,rc); self.assertFalse(target.exists())
 def test_source_image_and_consumer_source_are_exact(self):
  p=self.profile(); containerfile=ROOT/'deployment/telemetry-collector/persistent/Containerfile'
  self.assertEqual(p['image']['containerfileSha256'],r.sha_file(containerfile)); self.assertTrue(p['image']['resultDigestRequiredBeforeInstall'])
  self.assertEqual('6d722af48f5ee45c35c047c869973345750d8c92dcf740c8d7c82c9b38e101aa',p['manager']['programSha256'])
if __name__=='__main__': unittest.main()

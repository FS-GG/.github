import importlib.util,json,os,pathlib,stat,tempfile,unittest
ROOT=pathlib.Path(__file__).resolve().parents[3]
MOD=ROOT/'deployment/telemetry-collector/persistent/receiver.py'
PROFILE=ROOT/'deployment/telemetry-collector/persistent/profile.json'
spec=importlib.util.spec_from_file_location('persistent_receiver',MOD); r=importlib.util.module_from_spec(spec); spec.loader.exec_module(r)

def private_parent(temp):
    p=pathlib.Path(temp)/'owner'; p.mkdir(mode=0o700); return p
class ReceiverTests(unittest.TestCase):
 def profile(self): return json.loads(PROFILE.read_text())
 def test_inspect_is_effect_free_and_separates_mounts(self):
  with tempfile.TemporaryDirectory() as t:
   root=pathlib.Path(t)/'missing'; result=r.initialize(self.profile(),root,False)
   self.assertFalse(root.exists()); self.assertFalse(result['effectsPerformed']); self.assertFalse(result['activationAuthorized'])
   self.assertEqual(set(),set(result['mounts']['receiverPrivate']) & set(result['mounts']['developmentWritable']))
   self.assertEqual(result['mounts']['receiverPrivate'],result['mounts']['developmentForbidden'])
 def test_initialize_private_exact_replay_and_csprng(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; first=r.initialize(self.profile(),root,True); token=(root/'receiver/credentials/collector.token').read_text().strip()
   self.assertEqual('initialized',first['status']); self.assertGreaterEqual(len(token),48); self.assertEqual(0o600,stat.S_IMODE((root/'receiver/configuration/host.json').stat().st_mode))
   self.assertEqual('exact-replay',r.initialize(self.profile(),root,True)['status'])
 def test_interrupted_initialization_leaves_no_root(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'
   with self.assertRaises(RuntimeError): r.initialize(self.profile(),root,True,fault='before-commit')
   self.assertFalse(root.exists()); self.assertEqual([],list(parent.iterdir()))
 def test_changed_pin_refuses_replay(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True)
   changed=self.profile(); changed['reader']['profileSha256']='0'*64
   with self.assertRaises(r.Refusal): r.initialize(changed,root,True)
 def test_mount_collision_and_scope_refuse(self):
  p=self.profile(); p['containerPaths']['producerSpool']=p['containerPaths']['store']
  with self.assertRaises(r.Refusal): r.validate_profile(p)
  p=self.profile(); p['containerPaths']['credentials']='/producer/stolen'
  with self.assertRaises(r.Refusal): r.validate_profile(p)
 def test_profile_requires_installation_v2_and_stable_identity(self):
  p=self.profile(); p['manager']['installationSchema']='fsgg.telemetry.native-collector-installation/1'
  with self.assertRaises(r.Refusal): r.validate_profile(p)
 def test_source_image_is_exact_and_stays_inactive(self):
  p=self.profile(); containerfile=ROOT/'deployment/telemetry-collector/persistent/Containerfile'
  self.assertEqual(p['image']['containerfileSha256'],r.sha_file(containerfile))
  self.assertTrue(p['image']['resultDigestRequiredBeforeInstall'])
  self.assertEqual('disabled-before-private-readback',p['image']['activation'])
  self.assertIn(p['runtime']['baseImage'],containerfile.read_text())
  p=self.profile(); p['uid']=0
  with self.assertRaises(r.Refusal): r.validate_profile(p)
 def test_drift_and_changed_grant_refuse_replay(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True)
   sidecar=root/'receiver/sidecar/native-collector.json'; value=json.loads(sidecar.read_text()); value['CredentialReference']='other'; sidecar.write_text(json.dumps(value)); sidecar.chmod(0o600)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),root,True)
 def test_custody_refuses_public_parent_and_symlink_root(self):
  with tempfile.TemporaryDirectory() as t:
   parent=pathlib.Path(t)/'public'; parent.mkdir(mode=0o755)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),parent/'receiver',True)
   private=pathlib.Path(t)/'private'; private.mkdir(mode=0o700); target=pathlib.Path(t)/'target'; target.mkdir(); (private/'receiver').symlink_to(target)
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),private/'receiver',True)
 def test_backup_restore_covers_private_state_and_references(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True)
   for rel,data in [('receiver/store/receipt.db',b'receipt'),('receiver/evidence/capture.json',b'{}\n'),('receiver/configuration/server.pfx',b'fixture'),('receiver/credentials/certificate-password',b'pw\n')]: r.write_new(root/rel,data)
   backup=parent/'backup'; result=r.backup(self.profile(),root,backup,True); self.assertGreater(result['files'],4)
   manifest=json.loads((backup/'backup.json').read_text()); self.assertEqual(['configuration','credentials','sidecar','store','evidence','native-source-references'],manifest['covers'])
   restored=parent/'restored'; self.assertEqual('restored',r.restore(self.profile(),backup,restored,True)['status'])
   self.assertEqual(b'receipt',(restored/'receiver/store/receipt.db').read_bytes()); self.assertEqual(b'fixture',(restored/'receiver/configuration/server.pfx').read_bytes())
   self.assertEqual([],list((restored/'producer/native-source').iterdir()))
 def test_changed_backup_refuses_without_output(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'; r.initialize(self.profile(),root,True); backup=parent/'backup'; r.backup(self.profile(),root,backup,True)
   token=backup/'receiver/credentials/collector.token'; token.write_text('x'*64+'\n'); token.chmod(0o600); target=parent/'restored'
   with self.assertRaises(r.Refusal): r.restore(self.profile(),backup,target,True)
   self.assertFalse(target.exists())
 def test_no_effect_cli_does_not_create_paths(self):
  with tempfile.TemporaryDirectory() as t:
   target=pathlib.Path(t)/'not-created'; rc=r.main(['initialize','--profile',str(PROFILE),'--root',str(target)])
   self.assertEqual(0,rc); self.assertFalse(target.exists())
 def test_invalid_token_generator_leaves_no_output(self):
  with tempfile.TemporaryDirectory() as t:
   parent=private_parent(t); root=parent/'receiver'
   with self.assertRaises(r.Refusal): r.initialize(self.profile(),root,True,token_factory=lambda:'short')
   self.assertFalse(root.exists())
if __name__=='__main__': unittest.main()

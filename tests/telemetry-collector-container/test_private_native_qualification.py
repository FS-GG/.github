#!/usr/bin/env python3
import base64, contextlib, hashlib, importlib.util, io, json, os, pathlib, re, shutil, subprocess, tempfile, types, unittest
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
 @classmethod
 def setUpClass(cls):
  project=ROOT/'deployment/telemetry-collector/host-binding/HostBinding.fsproj'
  cls.package_cache=tempfile.TemporaryDirectory(); env=dict(os.environ,NUGET_PACKAGES=cls.package_cache.name)
  subprocess.run(['dotnet','restore',str(project),'--locked-mode'],cwd=ROOT,env=env,check=True,capture_output=True)
  subprocess.run(['dotnet','build',str(project),'--no-restore'],cwd=ROOT,env=env,check=True,capture_output=True)
  cls.binding_dll=project.parent/'bin/Debug/net10.0/HostBinding.dll'
 @classmethod
 def tearDownClass(cls): cls.package_cache.cleanup()
 @classmethod
 def copy_binding_closure(cls,root):
  root.mkdir(parents=True,exist_ok=True)
  for name in ('HostBinding','HostBinding.dll','HostBinding.deps.json','HostBinding.runtimeconfig.json','FSharp.Core.dll'):
   shutil.copy2(cls.binding_dll.parent/name,root/name)
  return root/'HostBinding.dll'
 @classmethod
 def binding_fixture(cls,root):
  repository=root/'repository'; native=repository/'deployment/telemetry-collector'; native.mkdir(parents=True)
  names=('qualify_native.py','native_producer_support.py','native-operation-v1.json','native-producer-config.toml')
  pins={}
  for name in names:
   raw=(ROOT/'deployment/telemetry-collector'/name).read_bytes(); (native/name).write_bytes(raw); pins[name]=hashlib.sha256(raw).hexdigest()
  pin_file=root/'source-pins.json'; pin_file.write_text(json.dumps(pins,separators=(',',':')))
  subprocess.run(['git','init','--quiet'],cwd=repository,check=True); subprocess.run(['git','config','user.email','test@example.invalid'],cwd=repository,check=True)
  subprocess.run(['git','config','user.name','host-binding-test'],cwd=repository,check=True); subprocess.run(['git','add','.'],cwd=repository,check=True)
  subprocess.run(['git','commit','--quiet','-m','fixture'],cwd=repository,check=True)
  source=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repository,text=True).strip()
  command=['/usr/bin/dotnet',str(cls.binding_dll),'inspect','--source-root',str(repository),'--source-sha',source,
           '--profile',str(native/'native-operation-v1.json'),'--source-pins',str(pin_file)]
  binding=json.loads(subprocess.check_output(command,text=True))
  return repository,pin_file,source,binding
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
 def test_production_topology_loader_registers_dataclass_module_and_reuses_it(self):
  a=type('A',(),{})(); a.private_root=pathlib.Path('/private/run'); a.run_nonce='run-0001'; a.source_sha='a'*40; a.private_placement_sha='d'*40; a.source_root=ROOT
  op=q.Operation(a,FakeRunner()); first=op.topology(); second=op.topology()
  self.assertIs(first,second); self.assertEqual('private_native_topology',first.__name__)
  self.assertTrue(hasattr(first,'CustodySnapshot'))
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
  public=(ROOT/'.github/workflows/telemetry-host-package.yml').read_text()
  self.assertIn('workflow_dispatch:',text); self.assertNotIn('pull_request:',text); self.assertNotIn('push:',text)
  self.assertIn("github.repository == 'FS-GG/FS.GG.GitHub.Substrate.Sandbox'",text)
  self.assertIn('persist-credentials: false',text); self.assertIn('retention-days: 1',text)
  self.assertIn("repository: FS-GG/.github",text); self.assertIn("if: always()",text)
  self.assertIn("trap 'interrupted=1' INT TERM",text); self.assertIn("trap '' INT TERM",text); self.assertIn("@@RECIPE_SOURCE_SHA@@",text)
  self.assertNotIn("@@SOURCE_SHA@@",text)
  self.assertEqual(1,text.count('${{ inputs.placement_sha }}')); self.assertIn('[[ "$PLACEMENT_SHA" =~ ^[0-9a-f]{40}$ ]]',text)
  self.assertIn('podman pull --platform linux/amd64',text); self.assertIn('timeout-minutes: 40',text)
  self.assertIn('actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68',text)
  self.assertIn('dotnet-version: 10.0.401',text); self.assertIn('test "$(/usr/bin/dotnet --version)" = 10.0.401',text)
  marker='# BEGIN hosted runner SDK custody normalization'
  self.assertEqual(1,text.count(marker)); self.assertEqual(1,public.count(marker))
  for workflow in (text,public):
   self.assertIn('HOSTED_RUNNER_CUSTODY: ${{ runner.environment }}',workflow)
   self.assertIn('test "$HOSTED_RUNNER_CUSTODY" = github-hosted',workflow)
   self.assertIn('test "$DOTNET_ROOT" = /usr/share/dotnet',workflow)
   self.assertIn('test "$resolved_dotnet" = "$sdk_root/dotnet"',workflow)
   self.assertIn('sudo chown -hR root:root -- "$sdk_root"',workflow)
   self.assertIn('sudo chmod -R go-w -- "$sdk_root"',workflow)
   self.assertIn('! -uid 0 -o -perm /022',workflow)
  self.assertLess(public.index(marker),public.index('python3 -c'))
  self.assertLess(text.index(marker),text.index('/usr/bin/dotnet restore'))
  end='# END hosted runner SDK custody normalization'
  for name,workflow in (('private',text),('public',public)):
   with self.subTest(workflow=name), tempfile.TemporaryDirectory() as td:
    root=pathlib.Path(td); system=root/'system'; sdk=system/'usr/share/dotnet'; binary=sdk/'dotnet'; usr_bin=system/'usr/bin'
    sdk.mkdir(parents=True); usr_bin.mkdir(parents=True)
    binary.write_text('#!/bin/sh\nprintf "10.0.401\\n"\n'); binary.chmod(0o755)
    (usr_bin/'dotnet').symlink_to(binary)
    subprocess.run(['/usr/bin/sudo','chown','root:root',str(system),str(system/'usr'),str(system/'usr/share'),str(usr_bin)],check=True)
    subprocess.run(['/usr/bin/sudo','chmod','755',str(system),str(system/'usr'),str(system/'usr/share'),str(usr_bin)],check=True)
    block=workflow.split(marker,1)[1].split(end,1)[0]
    block=block.replace('/usr/share/dotnet',str(sdk)).replace('/usr/bin/dotnet',str(usr_bin/'dotnet'))
    block=block.replace('test "$custody_path" = / && break',f'test "$custody_path" = {system} && break')
    env=dict(os.environ,DOTNET_ROOT=str(sdk),HOSTED_RUNNER_CUSTODY='github-hosted',PATH=str(sdk)+':/usr/bin:/bin')
    fake=root/'fake'; fake.mkdir(); (fake/'sudo').write_text('#!/bin/sh\nexit 0\n'); (fake/'sudo').chmod(0o755)
    refused=subprocess.run(['bash','-e','-o','pipefail'],input=block,text=True,env=dict(env,PATH=str(fake)+':'+env['PATH']),capture_output=True)
    self.assertNotEqual(0,refused.returncode,'runner-owned SDK closure must be refused without normalization')
    accepted=subprocess.run(['bash','-e','-o','pipefail'],input=block,text=True,env=env,capture_output=True)
    owner=binary.stat().st_uid; writes=binary.stat().st_mode&0o022
    subprocess.run(['/usr/bin/sudo','chown','-hR',f'{os.getuid()}:{os.getgid()}',str(system)],check=True)
    self.assertEqual(0,accepted.returncode,accepted.stderr)
    self.assertEqual(0,owner); self.assertEqual(0,writes)
  self.assertIn('test -r /proc/thread-self/children',text)
  self.assertIn('mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-amd64@sha256:ed6a2d26633ddcd3d42a1d9f9866214ecbbc11ba6ac5e0e843da02c13da24072',text)
  for selected in ('/usr/bin/dotnet','/usr/bin/git','/usr/bin/setsid'): self.assertIn(selected,text)
  self.assertNotIn('trap finalize EXIT',text); self.assertIn('exit "$final_rc"',text)
  self.assertNotIn('echo $FSGG_NATIVE_AUTH',text); self.assertNotIn('--auth-json',text)
 def test_rendered_workflow_parses_and_each_shell_block_has_valid_syntax(self):
  if importlib.util.find_spec('yaml') is None: self.skipTest('PyYAML unavailable')
  import yaml
  workflows=((ROOT/'deployment/telemetry-collector/private-native-qualification.yml.in',2),
             (ROOT/'.github/workflows/telemetry-host-package.yml',6))
  for path,count in workflows:
   with self.subTest(workflow=path.name):
    rendered=path.read_text().replace('@@RECIPE_SOURCE_SHA@@','a'*40).replace('@@QUALIFICATION_REF@@','qualification-v1')
    value=yaml.safe_load(rendered); jobs=value.get('jobs'); self.assertIsInstance(jobs,dict)
    scripts=[step['run'] for job in jobs.values() for step in job['steps'] if 'run' in step]
    self.assertEqual(count,len(scripts))
    for script in scripts:
     checked=subprocess.run(['bash','-n'],input=script,text=True,capture_output=True)
     self.assertEqual(0,checked.returncode,checked.stderr)
 def test_job_environment_uses_only_contexts_available_at_job_scope(self):
  if importlib.util.find_spec('yaml') is None: self.skipTest('PyYAML unavailable')
  import yaml
  template=(ROOT/'deployment/telemetry-collector/private-native-qualification.yml.in').read_text()
  rendered=template.replace('@@RECIPE_SOURCE_SHA@@','a'*40).replace('@@QUALIFICATION_REF@@','qualification-v1')
  workflow=yaml.safe_load(rendered)
  job_env=workflow['jobs']['qualify']['env']
  allowed={'github','needs','strategy','matrix','vars','secrets','inputs'}
  def unavailable(values):
   return sorted({match.group(1) for value in values.values() for match in re.finditer(r'\$\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\.',str(value)) if match.group(1) not in allowed})
  self.assertEqual([],unavailable(job_env))
  self.assertEqual('/tmp/v2-host-result-${{ github.run_id }}-${{ github.run_attempt }}',job_env['RESULT_ROOT'])
  invalid=dict(job_env,RESULT_ROOT='${{ runner.temp }}/v2-host-result')
  self.assertEqual(['runner'],unavailable(invalid))
  step_env={'RESULT_ROOT':'${{ runner.temp }}/v2-host-result'}
  self.assertIn('runner', {match.group(1) for value in step_env.values() for match in re.finditer(r'\$\{\{\s*([a-zA-Z_][a-zA-Z0-9_]*)\.',value)})
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
   t=pathlib.Path(td); repository,pins,source,binding=self.binding_fixture(t)
   a=type('A',(),{'private_root':t/'private','run_nonce':'run-0001','source_sha':source,'private_placement_sha':'d'*40,
                  'source_root':repository,'native_source_pins':pins,'host_binding':self.binding_dll,'result':t/'result.json'})()
   op=q.Operation(a,q.Runner(__import__('time').monotonic()+100)); op.host_binding=binding
   op.record_host_binding_dependencies()
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':base64.b64encode(b'{}').decode(),
                                    'FSGG_PRIVATE_EFFECT_ADMISSION':'0'*64},clear=True):
    with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): op.materialize()
    self.assertFalse((t/'private').exists())
    self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ)
 def test_materialization_policy_is_only_in_compiled_binding_verifier(self):
  source=(ROOT/'deployment/telemetry-collector/qualify_native_container.py').read_text()
  self.assertNotIn("run_nonce+'\\0'+self.a.source_sha",source)
  self.assertNotIn('compare_digest(admission',source)
  self.assertIn("'/usr/bin/dotnet',str(self.a.host_binding),'verify'",source)
  self.assertIn("input_bytes=(admission+'\\n').encode()",source)
 def test_compiled_renderer_derives_recipe_profile_and_producer_slots(self):
  with tempfile.TemporaryDirectory() as td:
   repository,pins,source,binding=self.binding_fixture(pathlib.Path(td))
   command=['/usr/bin/dotnet',str(self.binding_dll),'render','--source-root',str(repository),'--source-sha',source,
            '--profile',str(repository/'deployment/telemetry-collector/native-operation-v1.json'),'--source-pins',str(pins)]
   rendered=json.loads(subprocess.check_output(command,text=True))
   self.assertEqual('fsgg.telemetry.host-binding-render/1',rendered['schema'])
   self.assertEqual((source,binding['profileSha256'],binding['producerSha256'],binding['bindingSha256']),
                    (rendered['recipeSourceSha'],rendered['profileSha256'],rendered['producerSha256'],rendered['bindingSha256']))
 def test_compiled_verifier_refuses_oversized_or_padded_admission_frame(self):
  with tempfile.TemporaryDirectory() as td:
   repository,pins,source,binding=self.binding_fixture(pathlib.Path(td)); nonce='run-0001'
   admission=hashlib.sha256((nonce+'\0'+source+'\0'+binding['profileSha256']+'\0'+q.OPERATION).encode()).hexdigest()
   command=['/usr/bin/dotnet',str(self.binding_dll),'verify','--source-root',str(repository),'--source-sha',source,
            '--profile',str(repository/'deployment/telemetry-collector/native-operation-v1.json'),'--source-pins',str(pins),
            '--nonce',nonce,'--expected-binding-sha',binding['bindingSha256']]
   for candidate in (' '*(1024*1024)+admission+'\n',' '+admission+'\n',admission+'\r\n',admission+'\nextra'):
    with self.subTest(bytes=len(candidate)):
     refused=subprocess.run(command,input=candidate.encode(),capture_output=True,timeout=10)
     self.assertEqual(2,refused.returncode); self.assertEqual(b'',refused.stdout)
   accepted=subprocess.run(command,input=(admission+'\n').encode(),capture_output=True,timeout=10)
   self.assertEqual(0,accepted.returncode)
 def test_python_adapter_refuses_oversized_admission_before_child_or_auth(self):
  class NoChild:
   def run(self,*args,**kwargs): raise AssertionError('verifier child must not start')
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); a=type('A',(),{'private_root':t/'private','run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
   op=q.Operation(a,NoChild()); op.host_binding={'bindingSha256':'b'*64}
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':'synthetic-auth','FSGG_PRIVATE_EFFECT_ADMISSION':' '*(1024*1024)+'0'*64},clear=True):
    with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): op.materialize()
    self.assertFalse((t/'private').exists()); self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ)
 def test_host_binding_budget_and_dependency_drift_refuse_before_auth(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td); helper=self.copy_binding_closure(root/'binding')
   a=type('A',(),{'private_root':root/'private','run_nonce':'run-0001','source_sha':'a'*40,
                  'private_placement_sha':'d'*40,'host_binding':helper,'result':root/'result.json'})()
   short=q.Operation(a,q.Runner(__import__('time').monotonic()+10)); short.host_binding={'bindingSha256':'b'*64}
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':'synthetic-auth','FSGG_PRIVATE_EFFECT_ADMISSION':'0'*64},clear=True):
    with self.assertRaisesRegex(q.Refusal,'host-binding-budget-refused'): short.materialize()
    self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ); self.assertFalse((root/'private').exists())
   stable=q.Operation(a,q.Runner(__import__('time').monotonic()+100)); stable.host_binding={'bindingSha256':'b'*64}
   stable.record_host_binding_dependencies()
   (helper.parent/'HostBinding.runtimeconfig.json').write_text('{}')
   with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':'synthetic-auth','FSGG_PRIVATE_EFFECT_ADMISSION':'0'*64},clear=True):
    with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): stable.materialize()
    self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ); self.assertFalse((root/'private').exists())
 def test_each_local_runtime_closure_class_and_manifest_are_revalidated(self):
  for changed in ('HostBinding.deps.json','HostBinding.runtimeconfig.json','FSharp.Core.dll','HostBinding','manifest'):
   with self.subTest(changed=changed), tempfile.TemporaryDirectory() as td:
    root=pathlib.Path(td); helper=self.copy_binding_closure(root/'binding')
    a=type('A',(),{'private_root':root/'private','run_nonce':'run-0001','source_sha':'a'*40,
                   'private_placement_sha':'d'*40,'host_binding':helper,'result':root/'result.json'})()
    op=q.Operation(a,q.Runner(__import__('time').monotonic()+100)); op.host_binding={'bindingSha256':'b'*64}
    op.record_host_binding_dependencies()
    target=root/'host-binding-dependencies.json' if changed=='manifest' else helper.parent/changed
    target.write_bytes(target.read_bytes()+b'drift')
    with mock.patch.dict(os.environ,{'FSGG_NATIVE_AUTH_JSON_B64':'synthetic-auth','FSGG_PRIVATE_EFFECT_ADMISSION':'0'*64},clear=True):
     with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): op.materialize()
     self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ); self.assertFalse((root/'private').exists())
 def test_real_compiled_pre_materialization_boundary_has_seven_closed_probes(self):
  class ReachedBoundary(Exception): pass
  cases=('valid','old-profile','profile-mutated','pins-mutated','source-revision','producer-mutated','producer-missing')
  for case in cases:
   with self.subTest(case=case), tempfile.TemporaryDirectory() as td:
    root=pathlib.Path(td); repository,pins,source,binding=self.binding_fixture(root); nonce='run-0001'
    admission_profile=binding['profileSha256'] if case!='old-profile' else '5a30fc507f023d542521aac66c8f49c5ae6ee8d9e34dc90c1a3bf3ab30f6b08f'
    admission=hashlib.sha256((nonce+'\0'+source+'\0'+admission_profile+'\0'+q.OPERATION).encode()).hexdigest()
    executable=self.copy_binding_closure(root/'binding'); selected_source=source
    a=type('A',(),{'private_root':root/'private','run_nonce':nonce,'source_sha':selected_source,'private_placement_sha':'d'*40,
                   'source_root':repository,'native_source_pins':pins,'host_binding':executable,'result':root/'result.json'})()
    op=q.Operation(a,q.Runner(__import__('time').monotonic()+100)); op.host_binding=binding
    op.record_host_binding_dependencies()
    if case=='profile-mutated': (repository/'deployment/telemetry-collector/native-operation-v1.json').write_text('{}')
    elif case=='pins-mutated': pins.write_text('{}')
    elif case=='source-revision': a.source_sha='a'*40
    elif case=='producer-mutated': executable.write_bytes(executable.read_bytes()+b'changed')
    elif case=='producer-missing': executable.unlink()
    with mock.patch.object(q,'private_dir',side_effect=ReachedBoundary), mock.patch.dict(os.environ,{
         'FSGG_NATIVE_AUTH_JSON_B64':'synthetic-auth-not-read','FSGG_PRIVATE_EFFECT_ADMISSION':admission},clear=True):
     if case=='valid':
      with self.assertRaises(ReachedBoundary): op.materialize()
     else:
      with self.assertRaisesRegex(q.Refusal,'effect-admission-refused'): op.materialize()
     self.assertFalse((root/'private').exists()); self.assertIn('FSGG_NATIVE_AUTH_JSON_B64',os.environ)
 def test_full_operation_sequence_is_real_and_bounded(self):
  source=(ROOT/'deployment/telemetry-collector/qualify_native_container.py').read_text()
  ordered=['prepare_context_and_images','zero-auth-readonly-topology-qualified','zero-auth-images-qualified','materialize(); op.execute()',
           "'native-readonly-source'",'read-only-source-compatible',"'collect-native'","'export-learning'",'learn-01-analysis.py','wrong-native-selector-was-admitted',
           'receiver-restart-export-drift']
  for value in ordered: self.assertIn(value,source)
  self.assertIn('op.preflight(); op.prepare_context_and_images(); op.r=Runner(time.monotonic()+600,op.command_diagnostic); op.materialize(); op.execute()',source)
  self.assertIn("probe('prospective.token',False); probe('collector.token',True); probe('revoked.token',True)",source)
  self.assertIn('cleanup=Runner(time.monotonic()+60)',source)
  self.assertIn("signal.signal(signal.SIGTERM,interrupted)",source)
  self.assertLess(source.index("diagnostic='zero-auth-readonly-create'"),source.index('def materialize(self):'))
  self.assertNotIn('prepared-source-only',source)
  for token in ("inspect_container('fsgg-native-collector',t.inspect_receiver,'receiver-inspect')",
                "inspect_container('fsgg-native-egress',t.inspect_egress,'egress-inspect')",
                "inspect_container('fsgg-native-development',t.inspect_native,'native-inspect')",
                'require_collection_handoff','--userns=keep-id:uid=32768,gid=32768'):
   self.assertIn(token,source)
 def test_materialization_creates_driver_output_before_native_start(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td); repository,pins,source,binding=self.binding_fixture(root); staging=root/'staging'; (staging/'tls').mkdir(parents=True)
   for name in ('password','key.pem','native-receiver.crt','receiver.pfx'): (staging/'tls'/name).write_bytes(b'x')
   nonce='run-0001'
   a=type('A',(),{'private_root':root/'private','staging_root':staging,'run_nonce':nonce,'source_sha':source,'private_placement_sha':'d'*40,
                  'source_root':repository,'native_source_pins':pins,'host_binding':self.binding_dll,'result':root/'result.json'})()
   admission=hashlib.sha256((nonce+'\0'+source+'\0'+binding['profileSha256']+'\0'+q.OPERATION).encode()).hexdigest()
   with mock.patch.dict(os.environ,{'FSGG_PRIVATE_EFFECT_ADMISSION':admission,
                                    'FSGG_NATIVE_AUTH_JSON_B64':base64.b64encode(b'{}').decode()},clear=True):
    op=q.Operation(a,q.Runner(__import__('time').monotonic()+100)); op.host_binding=binding
    op.record_host_binding_dependencies(); op.materialize()
   output=root/'private/native/qualification-output'
   self.assertTrue(output.is_dir()); self.assertEqual(0o700,output.stat().st_mode&0o777)
 def test_exact_readonly_topology_is_qualified_before_private_root_exists(self):
  class ProbeRunner:
   def __init__(self): self.calls=[]
   def run(self,args,**kwargs):
    self.calls.append((args,kwargs)); output=b''; code=0
    if args[:3]==['podman','start','-a']:
     output=json.dumps({'schema':'fsgg.telemetry.native-source-readback/1','status':'compatible','resultSha256':'e'*64}).encode()
    if args[:3]==['podman','container','exists']: code=1
    return subprocess.CompletedProcess(args,code,output,b'')
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td); runner=ProbeRunner()
   a=type('A',(),{'private_root':root/'private','staging_root':root/'staging','run_nonce':'run-0001',
                  'source_sha':'a'*40,'private_placement_sha':'d'*40,'source_root':ROOT})()
   a.staging_root.mkdir(mode=0o700)
   op=q.Operation(a,runner); op.images['native-readonly-source']='sha256:'+'f'*64
   op.qualify_zero_auth_readonly()
   self.assertFalse(a.private_root.exists())
   create=runner.calls[0][0]; rendered=' '.join(create)
   for required in ('--read-only','--cap-drop=all','--security-opt=no-new-privileges','--pids-limit=128',
                    '--userns=keep-id:uid=32768,gid=32768','--network none',
                    '/qualification/native:ro,rprivate','/qualification/readback-output:rw,noexec,nosuid,nodev,size=8m,mode=0700,U'):
    self.assertIn(required,rendered)
   self.assertEqual(['zero-auth-readonly-create','zero-auth-readonly-start','zero-auth-readonly-remove','zero-auth-readonly-refusal'],
                    [kwargs['diagnostic'] for _,kwargs in runner.calls])
   self.assertTrue(all('env' not in kwargs for _,kwargs in runner.calls))
   self.assertEqual(['fsgg-native-readonly-probe'],op.containers)
   self.assertEqual('zero-auth-readonly-topology-qualified',op.result['phases'][-1]['name'])
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
  prefix=script.split('if python3 recipe/deployment/telemetry-collector/qualify_native_container.py',1)[0]+'printf OPERATION_SENTINEL\\n\n'
  with tempfile.TemporaryDirectory() as td:
   t=pathlib.Path(td); cases=[]
   cases.append(('missing',t/'missing',None))
   mode=t/'mode'; mode.mkdir(mode=0o755); cases.append(('mode0755',mode,None))
   target=t/'target'; target.mkdir(mode=0o700); link=t/'link'; link.symlink_to(target); cases.append(('symlink',link,None))
   owner=t/'owner'; owner.mkdir(mode=0o700); fake=t/'fake'; fake.mkdir(); identity=fake/'id'
   identity.write_text('#!/bin/sh\nprintf 999999\\n\n'); identity.chmod(0o700); cases.append(('wrong-owner',owner,str(fake)+':/usr/bin:/bin'))
   valid=t/'valid'; valid.mkdir(mode=0o700); cases.append(('valid',valid,None))
   for name,root,path in cases:
    env=dict(os.environ,RESULT_ROOT=str(root));
    if path: env['PATH']=path
    checked=subprocess.run(['bash'],input=prefix,text=True,env=env,capture_output=True)
    reached='OPERATION_SENTINEL' in checked.stdout
    if name=='valid': self.assertEqual((0,True),(checked.returncode,reached),checked.stderr)
    else: self.assertNotEqual(0,checked.returncode,name); self.assertFalse(reached,name)
  for operation_fail,seal_fail,expected_rc in ((False,False,0),(False,True,86),(True,False,2)):
   with self.subTest(operation_fail=operation_fail,seal_fail=seal_fail), tempfile.TemporaryDirectory() as td:
    t=pathlib.Path(td); fake=t/'bin'; fake.mkdir(); (t/'placement/_private-inputs/custody').mkdir(parents=True)
    (t/'result').mkdir(mode=0o700)
    (t/'placement/_private-inputs/custody/root-public.pem').write_text('public-test-only')
    (t/'recipe').symlink_to(ROOT,target_is_directory=True)
    (fake/'python3').write_text('''#!/bin/bash
if [[ "$1" == recipe/deployment/telemetry-collector/qualify_native_container.py && "$2" != custody-status ]]; then
  mkdir -p "$PRIVATE_ROOT/native/.codex" "$PRIVATE_ROOT/native/qualification-output/run-0001" "$PRIVATE_ROOT/output"
  printf '{}\\n' > "$PRIVATE_ROOT/native/.codex/auth.json"
  printf '{"schema":"fsgg.telemetry.private-native-qualification/1","runNonce":"run-0001","sourceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","phases":[{"name":"private-material-ready"}],"disposition":"source-operation-complete","cleanupComplete":true}\\n' > "$RESULT_ROOT/result.json"
  test "${OPERATION_FAIL:-0}" = 0 && exit 0 || exit 2
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
         'PROFILE_SHA':'1ef6d54eb3f9572580407efe9f266f643645af3af17c33723c0aa8368e5f4f34','GITHUB_WORKSPACE':str(t),'FSGG_NATIVE_AUTH_JSON_B64':'test',
         'FSGG_PRIVATE_EFFECT_ADMISSION':'test','SEAL_FAIL':'1' if seal_fail else '0','OPERATION_FAIL':'1' if operation_fail else '0'}
    completed=subprocess.run(['bash','-e','-o','pipefail'],input=script,text=True,cwd=t,env=env,capture_output=True)
    self.assertEqual(expected_rc,completed.returncode,completed.stderr)
    result=json.loads((t/'result/result.json').read_text())
    if seal_fail:
     self.assertEqual((False,'seal-failed','custody-incomplete'),(result['custodySealed'],result['authCustody'],result['disposition']))
     self.assertTrue((t/'private/native/.codex/auth.json').exists())
    else:
     self.assertTrue(result['custodySealed']); self.assertFalse((t/'private').exists())
     self.assertEqual(2 if operation_fail else 0,result['operationExit'])
     self.assertEqual('sealed',result['evidenceCustody'])
     self.assertTrue((t/'result/auth-capsule.json').is_file()); self.assertTrue((t/'result/evidence-capsule.json').is_file())
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
   def public_failure_diagnostic(self): return None
   def write_private_diagnostics(self): pass
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
    def inspect_container(self,name,inspector,diagnostic): topology.inspected.append(inspector.__name__)
    def container_running(self,name,diagnostic): return False
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
 def test_private_command_diagnostics_are_bounded_and_never_store_command_or_output(self):
  records=[]
  completed=subprocess.CompletedProcess(['podman'],7,b'private-stdout',b'Error: name demo already in use')
  with mock.patch.object(q.subprocess,'run',return_value=completed):
   runner=q.Runner(q.time.monotonic()+10,records.append)
   with self.assertRaisesRegex(q.Refusal,'command-failed:podman'):
    runner.run(['podman','create','--env','PRIVATE=value','image'],diagnostic='readonly-probe-create')
  self.assertEqual(1,len(records)); record=records[0]
  self.assertEqual(('readonly-probe-create','podman',7,'name-conflict'),(record['command'],record['executable'],record['exitCode'],record['stderrCategory']))
  encoded=json.dumps(record)
  for forbidden in ('PRIVATE=value','private-stdout','already in use','podman create','image'):
   self.assertNotIn(forbidden,encoded)
  self.assertEqual(hashlib.sha256(b'private-stdout').hexdigest(),record['stdoutSha256'])
 def test_actual_podman_49_tmpfs_owner_failure_has_exact_safe_category(self):
  stderr=b'Error: unknown mount option "uid=32768": invalid mount option\n'
  self.assertEqual(62,len(stderr))
  self.assertEqual('3573f6436c2d1814b72c7379a02850f6754da3d818f975c85f525fabafcabce9',hashlib.sha256(stderr).hexdigest())
  self.assertEqual('tmpfs-owner-option-refused',q.Runner.stderr_category(stderr))
 def test_only_bounded_printable_zero_auth_topology_stderr_is_retained(self):
  safe=b'Error: crun: mount `/qualification/readback-output`: Invalid argument\n'
  cases=(
   ('zero-auth-readonly-start',safe,safe.decode()),
   ('readonly-probe-start',safe,None),
   ('zero-auth-readonly-start',b'credentialed\x00detail',None),
   ('zero-auth-readonly-start',b'x'*4097,None),
  )
  for diagnostic,stderr,expected in cases:
   with self.subTest(diagnostic=diagnostic,size=len(stderr)):
    self.assertEqual(expected,q.Runner.safe_zero_auth_stderr(diagnostic,stderr))
  records=[]
  completed=subprocess.CompletedProcess(['podman'],2,b'',safe)
  with mock.patch.object(q.subprocess,'run',return_value=completed):
   runner=q.Runner(q.time.monotonic()+10,records.append)
   with self.assertRaisesRegex(q.Refusal,'command-failed:podman'):
    runner.run(['podman','start','-a','fsgg-native-readonly-probe'],diagnostic='zero-auth-readonly-start',limit=4096)
  self.assertEqual(safe.decode(),records[0]['stderrText'])
  self.assertEqual(hashlib.sha256(safe).hexdigest(),records[0]['stderrSha256'])
 def test_public_failure_diagnostic_exposes_only_bounded_safe_fields(self):
  a=type('A',(),{'private_root':pathlib.Path('/private/run'),'run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
  op=q.Operation(a,FakeRunner())
  op.command_diagnostic({'command':'expected-refusal','executable':'podman','exitCode':1,'stdoutBytes':0,'stdoutSha256':'a'*64,'stderrBytes':0,'stderrSha256':'b'*64,'stderrCategory':'empty','requiredSuccess':False})
  self.assertIsNone(op.public_failure_diagnostic())
  op.command_diagnostic({'command':'zero-auth-readonly-create','executable':'podman','exitCode':125,'stdoutBytes':0,'stdoutSha256':'c'*64,'stderrBytes':62,'stderrSha256':'d'*64,'stderrCategory':'tmpfs-owner-option-refused','requiredSuccess':True})
  value=op.public_failure_diagnostic()
  self.assertEqual('tmpfs-owner-option-refused',value['stderrCategory'])
  self.assertNotIn('requiredSuccess',value); self.assertNotIn('phase',value); self.assertEqual(8,len(value))
  op.command_diagnostic({'command':'zero-auth-readonly-start','executable':'podman','exitCode':2,'stdoutBytes':0,'stdoutSha256':'e'*64,'stderrBytes':12,'stderrSha256':'f'*64,'stderrCategory':'unclassified','requiredSuccess':True,'stderrText':'safe detail\n'})
  self.assertEqual('safe detail\n',op.public_failure_diagnostic()['stderrText'])
  op.command_diagnostic({'command':'native-create','executable':'podman','exitCode':125,'stdoutBytes':1,'stdoutSha256':'e'*64,'stderrBytes':1,'stderrSha256':'f'*64,'stderrCategory':'unclassified','requiredSuccess':True})
  private_phase=op.public_failure_diagnostic(); self.assertNotIn('stdoutSha256',private_phase); self.assertNotIn('stderrSha256',private_phase)
 def test_private_diagnostics_file_is_not_added_to_public_result(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td)/'private'; (root/'output').mkdir(parents=True,mode=0o700)
   a=type('A',(),{'private_root':root,'run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
   op=q.Operation(a,FakeRunner()); op.command_diagnostic({'command':'readonly-probe-create','executable':'podman','exitCode':125,'stdoutBytes':0,'stdoutSha256':hashlib.sha256(b'').hexdigest(),'stderrBytes':1,'stderrSha256':hashlib.sha256(b'x').hexdigest(),'stderrCategory':'unclassified'})
   op.write_private_diagnostics(); value=json.loads((root/'output/command-diagnostics.json').read_text())
   self.assertEqual('fsgg.telemetry.private-command-diagnostics/1',value['schema'])
   self.assertEqual('before-first-phase',value['records'][0]['phase'])
   self.assertNotIn('commandDiagnostics',op.result); self.assertEqual(0o600,(root/'output/command-diagnostics.json').stat().st_mode&0o777)
 def test_failed_native_start_stderr_is_bounded_private_extension_only(self):
  sentinel=b'private native-start sentinel: exact failure bytes\x00\xff'
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td)/'private'; (root/'output').mkdir(parents=True,mode=0o700)
   a=type('A',(),{'private_root':root,'run_nonce':'run-0001','source_sha':'a'*40,'private_placement_sha':'d'*40})()
   op=q.Operation(a,FakeRunner()); op.phase('effective-topology-inspected')
   failed=subprocess.CompletedProcess(['podman'],2,b'',sentinel)
   with mock.patch.object(q.subprocess,'run',return_value=failed):
    runner=q.Runner(q.time.monotonic()+10,op.command_diagnostic)
    with self.assertRaisesRegex(q.Refusal,'command-failed:podman'):
     runner.run(['podman','start','-a','fsgg-native-development'],diagnostic='native-start')
   op.write_private_diagnostics(); raw=(root/'output/command-diagnostics.json').read_text(); saved=json.loads(raw)
   extension=saved['extensions']['fsgg.telemetry.private-native-start-failure/1']
   self.assertEqual(1,len(extension['records'])); private=extension['records'][0]
   self.assertEqual({'command','encoding','maximumDecodedBytes','stderrBytes','stderrSha256','stderrBase64'},set(private))
   self.assertEqual(('native-start','base64',4096,len(sentinel),hashlib.sha256(sentinel).hexdigest()),
                    (private['command'],private['encoding'],private['maximumDecodedBytes'],private['stderrBytes'],private['stderrSha256']))
   self.assertEqual(sentinel,base64.b64decode(private['stderrBase64'],validate=True))
   self.assertNotIn('_privateNativeStartStderrBase64',raw)
   public=json.dumps({'result':op.result,'failureDiagnostic':op.public_failure_diagnostic()},sort_keys=True)
   self.assertNotIn(base64.b64encode(sentinel).decode(),public); self.assertNotIn(sentinel.decode('utf-8','replace'),public)
   self.assertNotIn('stderrBase64',public); self.assertNotIn('private-native-start-failure',public)
 def test_private_native_start_extension_is_exact_command_failure_and_limit(self):
  cases=(
   ('native-start',2,True,b'x'*4096,True),
   ('native-start',2,True,b'x'*4097,False),
   ('native-start',0,True,b'x',False),
   ('native-start',2,False,b'x',False),
   ('receiver-start',2,True,b'x',False),
  )
  for diagnostic,exit_code,required,stderr,retained in cases:
   with self.subTest(diagnostic=diagnostic,exit_code=exit_code,required=required,size=len(stderr)):
    records=[]; completed=subprocess.CompletedProcess(['podman'],exit_code,b'',stderr)
    with mock.patch.object(q.subprocess,'run',return_value=completed):
     runner=q.Runner(q.time.monotonic()+10,records.append)
     try: runner.run(['podman','start','-a','fixed'],diagnostic=diagnostic,check=required)
     except q.Refusal: pass
    self.assertEqual(retained,'_privateNativeStartStderrBase64' in records[0])
 def test_receiver_refusal_adds_only_namespaced_projection_to_sealed_diagnostics(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td)/'private'; (root/'output').mkdir(parents=True,mode=0o700)
   a=type('A',(),{'private_root':root,'run_nonce':'run-0001','source_sha':'a'*40,
                  'private_placement_sha':'d'*40,'source_root':ROOT})()
   op=q.Operation(a,FakeRunner()); topology=op.topology()
   value={'Config':{'User':{'malformed':'private-user'},
                    'Env':['FSGG_TELEMETRY_CREDENTIAL_PRIVATE=secret-value',{'malformed':'private-env'}],
                    'Cmd':{'malformed':'/private/secret.json'}},
          'HostConfig':{'NetworkMode':['host',{'malformed':'private-network-mode'}],
                        'ReadonlyRootfs':True,'PidsLimit':128,'Memory':1024**3,
                        'NanoCpus':1_000_000_000,'PortBindings':{},
                        'CapDrop':[{'malformed':'private-capability'}]},
          'NetworkSettings':{'Networks':{topology.COLLECTOR_NETWORK:{}}},
          'Mounts':[{'Source':'/private/source','Destination':path,'RW':writable}
                    for path,writable in topology.RECEIVER_MOUNTS.items()] +
                   [{'Source':'/private/malformed-source',
                     'Destination':['/private/malformed-destination'],'RW':True}]}
   class InspectRunner:
    def run(self,args,**kwargs): return subprocess.CompletedProcess(args,0,json.dumps(value).encode(),b'')
   op.r=InspectRunner(); op.phase('read-only-source-compatible')
   with self.assertRaisesRegex(topology.Refusal,'network-set') as refused:
    op.inspect_container('fsgg-native-collector',topology.inspect_receiver,'receiver-inspect')
   self.assertIs(type(refused.exception),topology.Refusal)
   op.write_private_diagnostics(); raw=(root/'output/command-diagnostics.json').read_text(); saved=json.loads(raw)
   extension=saved['extensions']['fsgg.telemetry.private-container-inspection/1']
   self.assertEqual(('read-only-source-compatible','receiver','container-network-set-refused'),
                    (extension['records'][0]['phase'],extension['records'][0]['inspection'],
                     extension['records'][0]['projection']['failureCode']))
   self.assertEqual([],saved['records']); self.assertNotIn('extensions',op.result)
   for private in ('secret-value','PRIVATE','private-user','private-env','private-network-mode',
                   'private-capability','/private/source','/private/secret.json',
                   '/private/malformed-source','/private/malformed-destination'):
    self.assertNotIn(private,raw)
   self.assertEqual(0o600,(root/'output/command-diagnostics.json').stat().st_mode&0o777)
 def test_native_refusal_preserves_exception_and_adds_only_closed_typed_projection(self):
  with tempfile.TemporaryDirectory() as td:
   root=pathlib.Path(td)/'private'; (root/'output').mkdir(parents=True,mode=0o700)
   a=type('A',(),{'private_root':root,'run_nonce':'run-0001','source_sha':'a'*40,
                  'private_placement_sha':'d'*40,'source_root':ROOT})()
   op=q.Operation(a,FakeRunner()); topology=op.topology()
   value={'Config':{'User':'32768:32768',
                    'Env':[*topology.FIXED_ENV,
                           'FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1=secret-value']},
          'HostConfig':{'NetworkMode':'bridge','ReadonlyRootfs':True,'PidsLimit':128,
                        'Memory':2*1024**3,'NanoCpus':2_000_000_000,
                        'CapAdd':[],'CapDrop':['CAP_CHOWN','CAP_SETUID'],
                        'SecurityOpt':['no-new-privileges'],'Privileged':False,
                        'UsernsMode':'private','PidMode':'private','UTSMode':'private'},
          'EffectiveCaps':None,'BoundingCaps':None,
          'NetworkSettings':{'Networks':{topology.NATIVE_NETWORK:{}}},
          'Mounts':[{'Source':'/private/source','Destination':path,'RW':writable}
                    for path,writable in topology.NATIVE_MOUNTS.items()]}
   class InspectRunner:
    def run(self,args,**kwargs): return subprocess.CompletedProcess(args,0,json.dumps(value).encode(),b'')
   op.r=InspectRunner(); op.phase('private-material-ready')
   op.inspect_container('fsgg-native-development',topology.inspect_native,'native-inspect')
   value['BoundingCaps']=['CAP_CHOWN']
   captured=[]
   def inspect(candidate):
    try: topology.inspect_native(candidate)
    except topology.Refusal as exception:
     captured.append(exception); raise
   with self.assertRaises(topology.Refusal) as refused:
    op.inspect_container('fsgg-native-development',inspect,'native-inspect')
   self.assertIs(refused.exception,captured[0])
   op.write_private_diagnostics(); raw=(root/'output/command-diagnostics.json').read_text(); saved=json.loads(raw)
   extension=saved['extensions']['fsgg.telemetry.private-container-inspection/1']
   record=extension['records'][0]
   self.assertEqual(('private-material-ready','native','native-capability-fence-refused'),
                    (record['phase'],record['inspection'],record['projection']['failureCode']))
   self.assertEqual(('empty','null','list'),
                    (record['projection']['privilege']['capAdd']['shape'],
                     record['projection']['privilege']['ociEffective']['shape'],
                     record['projection']['privilege']['ociBounding']['shape']))
   self.assertEqual([],saved['records']); self.assertNotIn('extensions',op.result)
   for private in ('secret-value','CAP_CHOWN','CAP_SETUID','/private/source'):
    self.assertNotIn(private,raw)
   self.assertEqual(0o600,(root/'output/command-diagnostics.json').stat().st_mode&0o777)
 def test_diagnostic_write_failure_cannot_skip_cleanup(self):
  class FakeOperation:
   cleaned=False
   def __init__(self,args,runner): self.result={'schema':q.SCHEMA,'runNonce':args.run_nonce,'sourceSha':args.source_sha,'phases':[],'disposition':'incomplete'}
   def preflight(self): pass
   def prepare_context_and_images(self): pass
   def materialize(self): pass
   def execute(self): pass
   def command_diagnostic(self,value): pass
   def write_private_diagnostics(self): raise OSError('private detail')
   def cleanup(self): FakeOperation.cleaned=True; return []
   def write_result(self): pass
  with tempfile.TemporaryDirectory() as td:
   a=type('A',(),{'operation_id':q.OPERATION,'source_root':ROOT,'source_sha':'a'*40,'private_placement_sha':'d'*40,
                  'private_root':pathlib.Path(td)/'private','staging_root':pathlib.Path(td)/'staging','run_nonce':'run-0001'})()
   with mock.patch.object(q,'parse',return_value=a), mock.patch.object(q,'Operation',FakeOperation): self.assertEqual(2,q.main([]))
  self.assertTrue(FakeOperation.cleaned)
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

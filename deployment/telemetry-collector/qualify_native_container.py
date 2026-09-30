#!/usr/bin/env python3
"""Fail-closed orchestration for the one V2-HOST-01.8 private native qualification."""
from __future__ import annotations
import argparse, base64, hashlib, importlib.util, json, os, pathlib, re, secrets, shutil, signal, ssl, stat, subprocess, sys, time, urllib.error, urllib.request, zipfile

SCHEMA="fsgg.telemetry.private-native-qualification/1"
OPERATION="v2-host-01.8a-native-collaboration-v1"
SOURCE_BASE="783ff2d5a1bba6f01b553bb0c550308ab41b8513"
HOST_SOURCE="0145bd2c852847d00da8b3a0c35d27cd64a87781"
HOST_ARCHIVE="4847c15ab207a33556462873840ad4109cf1c1e16fd7a15d5fffae5a8162f589"
HOST_PAYLOAD="207843031c78c2711e85a2db14b46a83ec0da9fbd3ee0f144a736320f7e8c2c9"
COORD_SOURCE="337b6a1d53571b07ca8e1417e18e52546ad319a7"
COORD_PAYLOAD="9b9486a54e014fd5d21b65ed71a00b9021562a56909a1303f89c9646bca4a585"
NATIVE_SHA="167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9"
PROFILE_SHA="5a30fc507f023d542521aac66c8f49c5ae6ee8d9e34dc90c1a3bf3ab30f6b08f"
LEARN_CONTRACT="91713679fd486459188f2144e75cc69b77720c7841b6e75cd5d4d35620ed4179"
HOST_LAUNCHER_SHA="6b881a6f1b346776ca5cb974a655ac041802ed27ad80e44dee92faeed59cc1bb"
SCOPE=("v2-host-native-qualification","native-prospective-v1","roadmap")
CREDENTIAL_ENV="FSGG_TELEMETRY_CREDENTIAL_NATIVE_PROSPECTIVE_V1"
MAX_OUTPUT=4*1024*1024

class Refusal(Exception): pass
def require(v,m):
    if not v: raise Refusal(m)
def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()
def private_dir(path):
    path.mkdir(mode=0o700,parents=True,exist_ok=False); return path
def private_write(path,data):
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    with os.fdopen(fd,'wb') as f: f.write(data); f.flush(); os.fsync(f.fileno())
def canonical(value): return (json.dumps(value,sort_keys=True,separators=(',',':'))+'\n').encode()
def stable_json(value):
    if isinstance(value,dict): return {k:stable_json(v) for k,v in value.items() if k not in {'observedAt','exportedAt','generatedAt'}}
    if isinstance(value,list): return [stable_json(v) for v in value]
    return value
def regular(path,maximum,mode=None):
    s=path.lstat(); require(stat.S_ISREG(s.st_mode) and not path.is_symlink() and 0<s.st_size<=maximum,'unsafe-input')
    if mode is not None: require(stat.S_IMODE(s.st_mode)==mode,'input-mode-refused')
def fixed_env(extra=None):
    result={'PATH':'/usr/local/bin:/usr/bin:/bin','LANG':'C.UTF-8','LC_ALL':'C.UTF-8'}
    result.update(extra or {}); return result

def receipt_probe(argv):
    """Run inside the private native network; disclose no credential or receipt body."""
    p=argparse.ArgumentParser(); p.add_argument('--url',required=True); p.add_argument('--batch',required=True)
    p.add_argument('--expected-digest'); p.add_argument('--expect-refusal',action='store_true'); a=p.parse_args(argv)
    require(re.fullmatch(r'https://native-receiver:7443/v1/receipts/[0-9a-f]{32}-[0-9]{6}',a.url)!=None,'receipt-url-refused')
    require(a.url.endswith('/'+a.batch) and re.fullmatch(r'[0-9a-f]{32}-[0-9]{6}',a.batch)!=None,'receipt-batch-refused')
    token=os.environ.pop('FSGG_RECEIPT_TOKEN',None); require(token is not None and 32<=len(token)<=256,'receipt-token-refused')
    request=urllib.request.Request(a.url,headers={'Authorization':'Bearer '+token},method='GET'); token=''
    context=ssl.create_default_context(); body=None; status=None
    for attempt in range(8):
        try:
            with urllib.request.urlopen(request,context=context,timeout=1) as response:
                body=response.read(4097); require(len(body)<=4096,'receipt-response-limit'); status=response.status; break
        except urllib.error.HTTPError as error:
            body=error.read(4097); require(len(body)<=4096,'receipt-response-limit'); status=error.code; break
        except urllib.error.URLError:
            if attempt==7: raise Refusal('receipt-endpoint-unavailable')
            time.sleep(0.25)
    if a.expect_refusal:
        require(status in {401,403,404},'receipt-refusal-not-enforced')
        print(json.dumps({'receiptRefused':True,'status':status},sort_keys=True,separators=(',',':'))); return 0
    require(status==200 and re.fullmatch(r'[0-9a-f]{64}',a.expected_digest or '')!=None,'receipt-success-refused')
    value=json.loads(body); require(set(value)=={'schema','workspaceId','producerId','streamId','batchId','digest','status','code'},'receipt-shape-refused')
    require(value=={'schema':'fsgg.telemetry.receipt/1','workspaceId':SCOPE[0],'producerId':SCOPE[1],
                    'streamId':SCOPE[2],'batchId':a.batch,'digest':a.expected_digest,'status':'applied','code':None},
            'receipt-content-refused')
    print(json.dumps({'receiptRecovered':True,'batchId':a.batch,'digest':a.expected_digest},sort_keys=True,separators=(',',':'))); return 0

class Runner:
    def __init__(self,deadline): self.deadline=deadline
    def run(self,args,*,env=None,input_bytes=None,limit=MAX_OUTPUT,check=True):
        require(isinstance(args,list) and all(isinstance(x,str) and x for x in args),'command-refused')
        remaining=self.deadline-time.monotonic()
        require(remaining>0,'operation-deadline')
        left=max(1,min(120,int(remaining)))
        p=subprocess.run(args,input=input_bytes,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env or fixed_env(),timeout=left,check=False)
        require(len(p.stdout)<=limit and len(p.stderr)<=limit,'command-output-limit')
        if check: require(p.returncode==0,'command-failed:'+args[0])
        return p

class Operation:
    def __init__(self,args,runner):
        self.a=args; self.r=runner; self.root=args.private_root.resolve(); self.created=[]; self.containers=[]; self.networks=[]
        self.result={'schema':SCHEMA,'runNonce':args.run_nonce,'sourceSha':args.source_sha,
                     'privatePlacementSha':args.private_placement_sha,'phases':[],'disposition':'incomplete'}
        self.images={}
    def phase(self,name,**facts): self.result['phases'].append({'ordinal':len(self.result['phases'])+1,'name':name,**facts})
    def preflight(self):
        require(re.fullmatch(r'[a-z0-9][a-z0-9-]{7,63}',self.a.run_nonce)!=None,'run-nonce-refused')
        require(re.fullmatch(r'[0-9a-f]{40}',self.a.source_sha)!=None,'source-sha-refused')
        head=self.r.run(['git','-C',str(self.a.source_root),'rev-parse','HEAD'],env=fixed_env(),limit=128).stdout.decode().strip()
        require(head==self.a.source_sha,'source-head-drift')
        dirty=self.r.run(['git','-C',str(self.a.source_root),'status','--porcelain'],env=fixed_env(),limit=65536).stdout
        require(dirty==b'','source-worktree-dirty')
        profile=self.a.source_root/'deployment/telemetry-collector/native-operation-v1.json'
        contract=self.a.source_root/'policy/learn-01-current-focused-v1.json'
        require(digest(profile)==PROFILE_SHA and digest(contract)==LEARN_CONTRACT,'source-payload-drift')
        manifest=json.loads(self.a.host_manifest.read_text())
        require(manifest=={**manifest,'version':'0.2.1'},'host-version-drift')
        require(manifest.get('sourceSha')==HOST_SOURCE and manifest.get('archiveSha256')==HOST_ARCHIVE and manifest.get('producerPayloadSha256')=='sha256:'+HOST_PAYLOAD,'host-manifest-drift')
        journal=json.loads(self.a.host_journal.read_text())
        require(journal.get('schema')=='fsgg.telemetry-host-release-journal/v1','host-journal-drift')
        observations=journal.get('observations',{})
        require(set(observations)=={'github','nuget'} and all(x.get('producerPayloadEqual') is True and x.get('payloadSha256')=='sha256:'+HOST_PAYLOAD for x in observations.values()),'host-readback-drift')
        require(digest(self.a.native_executable)==NATIVE_SHA,'native-drift')
        self.phase('pre-auth-preflight',hostSource=HOST_SOURCE,coordSource=COORD_SOURCE,nativeSha256=NATIVE_SHA)
    def prepare_context_and_images(self):
        """Verify all public bytes and build/smoke immutable images before auth restoration."""
        tls=self.a.staging_root/'tls'; tls.mkdir(mode=0o700,parents=True,exist_ok=False)
        password=secrets.token_urlsafe(32); private_write(tls/'password',(password+'\n').encode())
        self.r.run(['openssl','req','-x509','-newkey','rsa:3072','-sha256','-days','2','-nodes',
                    '-subj','/CN=native-receiver','-addext','subjectAltName=DNS:native-receiver',
                    '-keyout',str(tls/'key.pem'),'-out',str(tls/'native-receiver.crt')])
        self.r.run(['openssl','pkcs12','-export','-inkey',str(tls/'key.pem'),'-in',str(tls/'native-receiver.crt'),
                    '-out',str(tls/'receiver.pfx'),'-passout','file:'+str(tls/'password')])
        context=self.a.staging_root/'verified-context'
        helper=[sys.executable,str(self.a.context_helper),'--host-served',str(self.a.host_served),
                '--coord-packages',str(self.a.coord_packages),'--coord-manifest',str(self.a.coord_manifest),
                '--coord-stable',str(self.a.coord_stable),'--codex',str(self.a.native_executable),
                '--output',str(context),'--receiver-cert',str(tls/'native-receiver.crt'),
                '--native-source',str(self.a.source_root/'deployment/telemetry-collector'),
                '--native-source-pins',str(self.a.native_source_pins),'--native-source-revision',self.a.source_sha]
        reply=json.loads(self.r.run(helper,limit=65536).stdout)
        require(reply.get('contextPrepared') is True and pathlib.Path(reply.get('contextPath',''))==context/'context',
                'verified-context-refused')
        manifest_path=context/'manifest.json'; require(digest(manifest_path)==reply.get('manifestSha256'),'context-manifest-drift')
        manifest=json.loads(manifest_path.read_text()); require(manifest.get('contextPrepared') is True,'context-incomplete')
        # Add only exact reviewed egress source to the already verified context.
        build=context/'context'
        for name in ('native_egress_gate.py','native-network-policy.json'):
            shutil.copyfile(self.a.source_root/'deployment/telemetry-collector'/name,build/name)
        recipe=self.a.source_root/'deployment/telemetry-collector/NativeContainerfile'
        for target in ('native-development','native-readonly-source','native-collector','native-egress'):
            iid=self.a.staging_root/(target+'.iid')
            args=['podman','build','--pull=never','--target',target,'--iidfile',str(iid),'-f',str(recipe)]
            argument_target='native-development' if target=='native-readonly-source' else target
            for key,value in sorted(manifest['buildArguments'].get(argument_target,{}).items()): args += ['--build-arg',f'{key}={value}']
            args.append(str(build)); self.r.run(args,limit=2*1024*1024)
            image=iid.read_text().strip(); require(re.fullmatch(r'sha256:[0-9a-f]{64}',image)!=None,'built-image-id-refused')
            self.images[target]=image
        # Actual target-runtime smoke as the fixed nonroot user; no auth or network.
        python=self.r.run(['podman','run','--rm','--network','none','--entrypoint','/usr/local/bin/python3',
                           self.images['native-development'],'-c','import hashlib,json,socket,ssl; print(ssl.OPENSSL_VERSION)'],limit=4096)
        require(bool(python.stdout.strip()),'target-python-runtime-refused')
        self.r.run(['podman','run','--rm','--network','none','--entrypoint','/opt/fsgg/coord/fsgg-coord-engine',
                    self.images['native-development'],'--version'],limit=1024)
        host_smoke=self.r.run(['podman','run','--rm','--network','none','--entrypoint','/usr/bin/dotnet',
                    self.images['native-collector'],'/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.dll','status','--config','/missing'],
                   limit=4096,check=False)
        require(host_smoke.returncode==2,'target-host-runtime-refused')
        self.result['images']=dict(self.images); self.phase('zero-auth-images-qualified',contextSha256=digest(manifest_path))
    def materialize(self):
        expected=hashlib.sha256((self.a.run_nonce+'\0'+self.a.source_sha+'\0'+PROFILE_SHA+'\0'+OPERATION).encode()).hexdigest()
        admission=os.environ.pop('FSGG_PRIVATE_EFFECT_ADMISSION',None)
        require(admission is not None and secrets.compare_digest(admission,expected),'effect-admission-refused')
        private_dir(self.root); self.created.append(self.root)
        for n in ('native','producer-spool','store-seed','evidence','tls','credentials','output','build/collector/host','build/native/fsgg-coord-engine'):
            p=self.root/n; p.mkdir(mode=0o700,parents=True,exist_ok=False)
        # Restore only auth.json; the secret is removed before any child starts.
        encoded=os.environ.pop('FSGG_NATIVE_AUTH_JSON_B64',None); require(encoded is not None,'auth-capsule-unavailable')
        auth=base64.b64decode(encoded,validate=True); require(2<=len(auth)<=1024*1024,'auth-capsule-refused')
        json.loads(auth); codex=self.root/'native/.codex'; codex.mkdir(mode=0o700)
        (self.root/'native/work').mkdir(mode=0o700); (self.root/'native/telemetry').mkdir(mode=0o700)
        private_write(codex/'auth.json',auth); auth=b''
        prospective=secrets.token_urlsafe(48); collector=secrets.token_urlsafe(48); revoked=secrets.token_urlsafe(48)
        for name,value in (('prospective.token',prospective),('collector.token',collector),('revoked.token',revoked)):
            private_write(self.root/'credentials'/name,(value+'\n').encode())
        os.environ[CREDENTIAL_ENV]=prospective
        for name in ('password','key.pem','native-receiver.crt','receiver.pfx'):
            shutil.copyfile(self.a.staging_root/'tls'/name,self.root/'tls'/name); (self.root/'tls'/name).chmod(0o600)
        browser=secrets.token_bytes(32); private_write(self.root/'credentials/browser.sha256',canonical({
            'schema':'fsgg.telemetry.browser-key/1','algorithm':'sha256','keyHash':hashlib.sha256(browser).hexdigest()}))
        host=self.host_config(); private_write(self.root/'host.json',canonical(host))
        install={'Schema':'fsgg.telemetry.native-collector-installation/2','CredentialReference':'native-collector-v1','ExecutablePath':'/opt/fsgg/codex/codex','ExecutableSha256':NATIVE_SHA,'CodexHome':'/qualification/native/.codex','EvidenceRoot':'/qualification/evidence','Provider':'openai','Model':'gpt-5.6-sol','Effort':'medium'}
        private_write(self.root/'host.json.native-collector.json',canonical(install))
        workspace={'schema':'fsgg.telemetry.workspace-config/1','engine':'fsgg-coord-engine','associations':[{'workspaceId':SCOPE[0],'producerId':SCOPE[1],'streamId':SCOPE[2],'repositories':['FS-GG/.github'],'destination':{'kind':'remote','endpoint':'https://native-receiver:7443/','credentialReference':SCOPE[1],'spoolRoot':'/qualification/native/telemetry/spool'}}],'retiredAssociations':[]}
        private_write(self.root/'roadmap.json',canonical(workspace))
        self.phase('private-material-ready',receiverScope='/'.join(SCOPE))
    def topology(self):
        path=self.a.source_root/'deployment/telemetry-collector/native_topology.py'
        spec=importlib.util.spec_from_file_location('private_native_topology',path); module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module
    def admin(self,*args,check=True):
        mounts=['--volume',f'{self.root}/host.json:/qualification/host.json:ro,rprivate',
                '--volume',f'{self.root}/host.json.native-collector.json:/qualification/host.json.native-collector.json:ro,rprivate',
                '--volume',f'{self.root}/native:/qualification/native:rw,rprivate','--volume',f'{self.root}/evidence:/qualification/evidence:rw,rprivate',
                '--volume',f'{self.root}/store:/qualification/store:rw,rprivate','--volume',f'{self.root}/tls:/qualification/tls:ro,rprivate',
                '--volume',f'{self.root}/credentials:/qualification/credentials:ro,rprivate']
        return self.r.run(['podman','run','--rm','--network','none','--read-only','--cap-drop=all','--security-opt=no-new-privileges',
                           '--userns=keep-id:uid=32768,gid=32768','--user=32768:32768',*mounts,
                           self.images['native-collector'],*args],limit=MAX_OUTPUT,check=check)
    def initialize_store(self):
        seed=self.root/'store-seed'
        self.r.run(['podman','run','--rm','--network','none','--read-only','--cap-drop=all',
                    '--security-opt=no-new-privileges','--userns=keep-id:uid=32768,gid=32768','--user=32768:32768',
                    '--volume',f'{seed}:/qualification/state:rw,rprivate',self.images['native-collector'],
                    'init','--root','/qualification/state/store','--workspace',SCOPE[0]])
        created=seed/'store'; require(created.is_dir() and not created.is_symlink(),'initialized-store-refused')
        created.rename(self.root/'store'); seed.rmdir()
    def execute(self):
        t=self.topology()
        readback=t.readonly_probe_create(self.images['native-readonly-source'],self.root/'native',self.a.run_nonce)
        self.r.run(readback); self.containers.append('fsgg-native-readonly-probe')
        readback_result=json.loads(self.r.run(['podman','start','-a','fsgg-native-readonly-probe'],limit=4096).stdout)
        require(readback_result.get('schema')=='fsgg.telemetry.native-source-readback/1'
                and readback_result.get('status')=='compatible'
                and re.fullmatch(r'[0-9a-f]{64}',readback_result.get('resultSha256',''))!=None,
                'read-only-source-compatibility-refused')
        self.phase('read-only-source-compatible',resultSha256=readback_result['resultSha256'],fullReportRetained=False)
        for command in t.network_create_commands(): self.r.run(command); self.networks.append(command[-1])
        # Fresh schema-12 receiver and exact three declared principals.
        self.initialize_store()
        for row in self.host_config()['Credentials']:
            command=['enroll-producer','--config','/qualification/host.json','--reference',row['Reference'],
                     '--secret-file',row['SecretFile'],'--workspace',row['WorkspaceId'],'--producer',row['ProducerId'],'--stream',row['StreamId']]
            if row['Revoked']: command.append('--revoked')
            self.admin(*command)
        self.admin('preflight','--config','/qualification/host.json')
        collector=t.collector_create(self.images['native-collector'],self.root); self.r.run(collector); self.containers.append('fsgg-native-collector')
        self.r.run(t.receiver_connect_command()); self.r.run(['podman','start','fsgg-native-collector'])
        ecreate,econnect=t.egress_create(self.images['native-egress']); self.r.run(ecreate); self.containers.append('fsgg-native-egress'); self.r.run(econnect); self.r.run(['podman','start','fsgg-native-egress'])
        ncreate=t.native_create(self.images['native-development'],self.root/'native',self.root/'roadmap.json',self.root/'producer-spool',self.a.run_nonce)
        self.r.run(ncreate,env=fixed_env({CREDENTIAL_ENV:os.environ[CREDENTIAL_ENV]})); self.containers.append('fsgg-native-development')
        run=self.r.run(['podman','start','-a','fsgg-native-development'],limit=MAX_OUTPUT)
        operation=json.loads(run.stdout); require(operation.get('status')=='qualified','native-operation-refused')
        # Stop all writers before protected collection over the same original volume.
        self.r.run(['podman','stop','--time','10','fsgg-native-collector']); self.r.run(['podman','stop','--time','10','fsgg-native-egress'],check=False)
        before=self.topology().snapshot_native_volume(self.root/'native')
        receipts=json.loads((self.root/'native/qualification-output'/self.a.run_nonce/'telemetry-receipts.json').read_text())
        child=next(x for x in receipts['receipts'] if x['operation']=='child-started'); dispatch=child['batchId'].rsplit('-',1)[0]
        collected=self.admin('collect-native','--config','/qualification/host.json','--dispatch',dispatch,
                             '--parent-thread',operation['parentThreadId'],'--native-agent',operation['nativeAgent'])
        export=self.admin('export-learning','--config','/qualification/host.json'); private_write(self.root/'output/export.json',export.stdout)
        after=self.topology().snapshot_native_volume(self.root/'native'); self.topology().require_same_original_volume(before,after)
        require(before.digest==after.digest,'collector-mutated-original-rollouts')
        # Replay is byte stable and does not invoke the reader again.
        replay=self.admin('collect-native','--config','/qualification/host.json','--dispatch',dispatch,
                          '--parent-thread',operation['parentThreadId'],'--native-agent',operation['nativeAgent'])
        require(replay.stdout==collected.stdout,'collection-replay-drift')
        # Trusted analyzer acquires through the exact Host process inside the collector image.
        analysis=self.a.source_root/'tools/learn-01-analysis.py'; policy=self.a.source_root/'policy/learn-01-current-focused-v1.json'
        analyzer=self.r.run(['podman','run','--rm','--network','none','--entrypoint','/usr/local/bin/python3',
            '--volume',f'{self.a.source_root}:/qualification/source:ro,rprivate',
            '--volume',f'{self.root}/host.json:/qualification/host.json:ro,rprivate',
            '--volume',f'{self.root}/host.json.native-collector.json:/qualification/host.json.native-collector.json:ro,rprivate',
            '--volume',f'{self.root}/native:/qualification/native:ro,rprivate','--volume',f'{self.root}/evidence:/qualification/evidence:rw,rprivate',
            '--volume',f'{self.root}/store:/qualification/store:rw,rprivate','--volume',f'{self.root}/tls:/qualification/tls:ro,rprivate',
            '--volume',f'{self.root}/credentials:/qualification/credentials:ro,rprivate',self.images['native-collector'],
            '/qualification/source/tools/learn-01-analysis.py','/qualification/source/policy/learn-01-current-focused-v1.json',
            '--protected-host-executable','/opt/fsgg/telemetry-host/fsgg-telemetry-host','--protected-host-sha256',
            HOST_LAUNCHER_SHA,
            '--protected-host-config','/qualification/host.json'],limit=MAX_OUTPUT)
        private_write(self.root/'output/analysis.json',analyzer.stdout)
        refused=self.admin('collect-native','--config','/qualification/host.json','--dispatch',dispatch,
                           '--parent-thread',operation['parentThreadId'],'--native-agent','wrong-selector',check=False)
        require(refused.returncode!=0,'wrong-native-selector-was-admitted')
        self.r.run(['podman','start','fsgg-native-collector'])
        probe_mount=f'{pathlib.Path(__file__).resolve()}:/qualification/receipt-probe.py:ro,rprivate'
        def probe(token_file,refused):
            args=['podman','run','--rm','--pull=never','--read-only','--cap-drop=all','--security-opt=no-new-privileges',
                  '--pids-limit','32','--memory','128m','--cpus','0.5','--userns=keep-id:uid=32768,gid=32768','--user=32768:32768',
                  '--network',t.NATIVE_NETWORK,'--http-proxy=false','--env','HTTPS_PROXY=','--env','HTTP_PROXY=',
                  '--env','NO_PROXY=native-receiver','--env','FSGG_RECEIPT_TOKEN','--volume',probe_mount,
                  '--entrypoint','/usr/local/bin/python3',self.images['native-development'],'/qualification/receipt-probe.py',
                  'receipt-probe','--url','https://native-receiver:7443/v1/receipts/'+child['batchId'],'--batch',child['batchId']]
            if refused: args.append('--expect-refusal')
            else: args += ['--expected-digest',child['digest']]
            token=(self.root/'credentials'/token_file).read_text().strip()
            reply=json.loads(self.r.run(args,env=fixed_env({'FSGG_RECEIPT_TOKEN':token}),limit=4096).stdout); token=''
            require(reply.get('receiptRefused') is True if refused else reply.get('receiptRecovered') is True,'receipt-probe-refused')
        probe('prospective.token',False); probe('collector.token',True); probe('revoked.token',True)
        self.r.run(['podman','stop','--time','10','fsgg-native-collector'])
        recovered=self.admin('export-learning','--config','/qualification/host.json')
        require(stable_json(json.loads(recovered.stdout))==stable_json(json.loads(export.stdout)),'receiver-restart-export-drift')
        self.phase('genuine-operation-collected',dispatchSha256=hashlib.sha256(dispatch.encode()).hexdigest(),
                   replayStable=True,restartStable=True,restartReceiptRecovered=True,
                   wrongProducerRefused=True,revokedCredentialRefused=True,wrongSelectorRefused=True)
        self.result['disposition']='source-operation-complete'
    def host_config(self):
        def c(ref,file,producer,stream,role,grant,revoked=False): return {'Reference':ref,'SecretFile':'/qualification/credentials/'+file,'WorkspaceId':SCOPE[0],'ProducerId':producer,'StreamId':stream,'Role':role,'GrantId':grant,'GrantGeneration':1,'Revoked':revoked}
        return {'Schema':'fsgg.telemetry.host-config/2','ListenUrl':'https://0.0.0.0:7443','CertificatePath':'/qualification/tls/receiver.pfx','CertificatePasswordFile':'/qualification/tls/password','ServiceLockPath':'/qualification/store/host.lock','Stores':[{'WorkspaceId':SCOPE[0],'Root':'/qualification/store'}],'Credentials':[c(SCOPE[1],'prospective.token',SCOPE[1],SCOPE[2],'generic','grant-prospective-v1'),c('native-collector-v1','collector.token','native-collector-v1','native-inventory','native-collector','grant-native-collector-v1'),c('native-revoked-v1','revoked.token','native-revoked-v1','native-inventory','native-collector','grant-native-revoked-v1',True)],'BrowserPrincipals':[{'PrincipalId':'private-preflight','KeyHashFile':'/qualification/credentials/browser.sha256','WorkspaceIds':[SCOPE[0]],'Revoked':False}],'BrowserSession':{'IdleSeconds':300,'AbsoluteSeconds':600,'MaximumSessions':2,'LoginAttemptsPerMinute':2,'LoginAdmission':1,'QueryAdmission':1,'QueryTimeoutSeconds':10}}
    def cleanup(self):
        cleanup=Runner(time.monotonic()+60)
        for name in reversed(self.containers): cleanup.run(['podman','rm','-f',name],check=False,limit=4096)
        for name in reversed(self.networks): cleanup.run(['podman','network','rm','-f',name],check=False,limit=4096)
        for key in ('FSGG_NATIVE_AUTH_JSON_B64','FSGG_PRIVATE_EFFECT_ADMISSION',CREDENTIAL_ENV): os.environ.pop(key,None)
    def write_result(self):
        self.a.result.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
        private_write(self.a.result,canonical(self.result))

def parse(argv):
    p=argparse.ArgumentParser(); p.add_argument('--operation-id',required=True); p.add_argument('--source-root',type=pathlib.Path,required=True); p.add_argument('--source-sha',required=True); p.add_argument('--private-placement-sha',required=True); p.add_argument('--private-root',type=pathlib.Path,required=True); p.add_argument('--staging-root',type=pathlib.Path,required=True); p.add_argument('--run-nonce',required=True); p.add_argument('--host-package',type=pathlib.Path,required=True); p.add_argument('--host-manifest',type=pathlib.Path,required=True); p.add_argument('--host-journal',type=pathlib.Path,required=True); p.add_argument('--host-served',type=pathlib.Path,required=True); p.add_argument('--coord-packages',type=pathlib.Path,required=True); p.add_argument('--coord-manifest',type=pathlib.Path,required=True); p.add_argument('--coord-stable',type=pathlib.Path,required=True); p.add_argument('--context-helper',type=pathlib.Path,required=True); p.add_argument('--native-source-pins',type=pathlib.Path,required=True); p.add_argument('--native-executable',type=pathlib.Path,required=True); p.add_argument('--seal-public-key',type=pathlib.Path,required=True); p.add_argument('--result',type=pathlib.Path,required=True); return p.parse_args(argv)
def main(argv=None):
    argv=list(sys.argv[1:] if argv is None else argv)
    if argv and argv[0]=='receipt-probe': return receipt_probe(argv[1:])
    a=parse(argv); require(a.operation_id==OPERATION,'operation-refused'); a.source_root=a.source_root.resolve()
    require(re.fullmatch(r'[0-9a-f]{40}',a.private_placement_sha)!=None,'private-placement-sha-refused')
    require(a.staging_root.is_absolute() and not a.staging_root.exists(),'staging-root-refused')
    # Public verification/build has its own finite allowance; credentialed work starts a fresh
    # ten-minute deadline, matching the admitted operation bound that excludes prior builds.
    a.staging_root.mkdir(mode=0o700,parents=True); op=Operation(a,Runner(time.monotonic()+1200))
    def interrupted(signum,frame): raise Refusal('operation-interrupted')
    signal.signal(signal.SIGINT,interrupted); signal.signal(signal.SIGTERM,interrupted)
    try:
        op.preflight(); op.prepare_context_and_images(); op.r=Runner(time.monotonic()+600); op.materialize(); op.execute()
        op.write_result(); return 0
    except Refusal as e:
        print('private-native-qualification-refused:'+str(e),file=sys.stderr); return 2
    finally: op.cleanup()
if __name__=='__main__': raise SystemExit(main())

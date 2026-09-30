#!/usr/bin/env python3
"""Fail-closed orchestration for the one V2-HOST-01.8 private native qualification."""
from __future__ import annotations
import argparse, base64, hashlib, json, os, pathlib, re, secrets, shutil, signal, stat, subprocess, sys, time, zipfile

SCHEMA="fsgg.telemetry.private-native-qualification/1"
OPERATION="v2-host-01.8a-native-collaboration-v1"
SOURCE_BASE="783ff2d5a1bba6f01b553bb0c550308ab41b8513"
HOST_SOURCE="0145bd2c852847d00da8b3a0c35d27cd64a87781"
HOST_ARCHIVE="4847c15ab207a33556462873840ad4109cf1c1e16fd7a15d5fffae5a8162f589"
HOST_PAYLOAD="207843031c78c2711e85a2db14b46a83ec0da9fbd3ee0f144a736320f7e8c2c9"
COORD_SOURCE="337b6a1d53571b07ca8e1417e18e52546ad319a7"
COORD_PAYLOAD="9b9486a54e014fd5d21b65ed71a00b9021562a56909a1303f89c9646bca4a585"
NATIVE_SHA="167c0148a849d2444f1b5a7fb5f8bb2de1de5ae13a2a504b833fc765980f5cd9"
PROFILE_SHA="8990f6b43f217f7ee21832f3f704ce44af8d25d58b674171ec796a890c87d7c4"
LEARN_CONTRACT="91713679fd486459188f2144e75cc69b77720c7841b6e75cd5d4d35620ed4179"
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
def regular(path,maximum,mode=None):
    s=path.lstat(); require(stat.S_ISREG(s.st_mode) and not path.is_symlink() and 0<s.st_size<=maximum,'unsafe-input')
    if mode is not None: require(stat.S_IMODE(s.st_mode)==mode,'input-mode-refused')
def fixed_env(extra=None):
    result={'PATH':'/usr/local/bin:/usr/bin:/bin','LANG':'C.UTF-8','LC_ALL':'C.UTF-8'}
    result.update(extra or {}); return result

class Runner:
    def __init__(self,deadline): self.deadline=deadline
    def run(self,args,*,env=None,input_bytes=None,limit=MAX_OUTPUT,check=True):
        require(isinstance(args,list) and all(isinstance(x,str) and x for x in args),'command-refused')
        left=max(1,min(120,int(self.deadline-time.monotonic())))
        require(left>0,'operation-deadline')
        p=subprocess.run(args,input=input_bytes,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env or fixed_env(),timeout=left,check=False)
        require(len(p.stdout)<=limit and len(p.stderr)<=limit,'command-output-limit')
        if check: require(p.returncode==0,'command-failed:'+args[0])
        return p

class Operation:
    def __init__(self,args,runner):
        self.a=args; self.r=runner; self.root=args.private_root.resolve(); self.created=[]; self.containers=[]; self.networks=[]
        self.result={'schema':SCHEMA,'runNonce':args.run_nonce,'sourceSha':args.source_sha,'phases':[],'disposition':'incomplete'}
    def phase(self,name,**facts): self.result['phases'].append({'ordinal':len(self.result['phases'])+1,'name':name,**facts})
    def preflight(self):
        require(re.fullmatch(r'[a-z0-9][a-z0-9-]{7,63}',self.a.run_nonce)!=None,'run-nonce-refused')
        require(re.fullmatch(r'[0-9a-f]{40}',self.a.source_sha)!=None,'source-sha-refused')
        head=self.r.run(['git','rev-parse','HEAD'],env=fixed_env(),limit=128).stdout.decode().strip()
        require(head==self.a.source_sha,'source-head-drift')
        profile=self.a.source_root/'deployment/telemetry-collector/native-operation-v1.json'
        contract=self.a.source_root/'policy/learn-01-current-focused-v1.json'
        require(digest(profile)==PROFILE_SHA and digest(contract)==LEARN_CONTRACT,'source-payload-drift')
        manifest=json.loads(self.a.host_manifest.read_text())
        require(manifest=={**manifest,'version':'0.2.1'},'host-version-drift')
        require(manifest.get('sourceSha')==HOST_SOURCE and manifest.get('archiveSha256')==HOST_ARCHIVE and manifest.get('producerPayloadSha256')=='sha256:'+HOST_PAYLOAD,'host-manifest-drift')
        journal=json.loads(self.a.host_journal.read_text())
        require(journal.get('schema')=='fsgg.telemetry-host-release-journal/v1','host-journal-drift')
        require(all(x.get('producerPayloadEqual') is True and x.get('payloadSha256')=='sha256:'+HOST_PAYLOAD for x in journal.get('observations',{}).values()),'host-readback-drift')
        require(digest(self.a.native_executable)==NATIVE_SHA,'native-drift')
        require((self.a.coord_root/'coherent-content.sha256').read_text()==COORD_PAYLOAD+'\n','coord-marker-drift')
        require((self.r.run([str(self.a.coord_root/'fsgg-coord-engine'),'--version'],limit=128).stdout.decode())=='0.94.0.0\n','coord-version-drift')
        self.phase('pre-auth-preflight',hostSource=HOST_SOURCE,coordSource=COORD_SOURCE,nativeSha256=NATIVE_SHA)
    def materialize(self):
        expected=hashlib.sha256((self.a.run_nonce+'\0'+self.a.source_sha+'\0'+PROFILE_SHA+'\0'+OPERATION).encode()).hexdigest()
        admission=os.environ.pop('FSGG_PRIVATE_EFFECT_ADMISSION',None)
        require(admission is not None and secrets.compare_digest(admission,expected),'effect-admission-refused')
        private_dir(self.root); self.created.append(self.root)
        for n in ('native','producer-spool','store','evidence','tls','credentials','output','build/collector/host','build/native/fsgg-coord-engine'):
            p=self.root/n; p.mkdir(mode=0o700,parents=True,exist_ok=False)
        # Restore only auth.json; the secret is removed before any child starts.
        encoded=os.environ.pop('FSGG_NATIVE_AUTH_JSON_B64',None); require(encoded is not None,'auth-capsule-unavailable')
        auth=base64.b64decode(encoded,validate=True); require(2<=len(auth)<=1024*1024,'auth-capsule-refused')
        json.loads(auth); codex=self.root/'native/.codex'; codex.mkdir(mode=0o700)
        private_write(codex/'auth.json',auth); auth=b''
        prospective=secrets.token_urlsafe(48); collector=secrets.token_urlsafe(48); revoked=secrets.token_urlsafe(48)
        for name,value in (('prospective.token',prospective),('collector.token',collector),('revoked.token',revoked)):
            private_write(self.root/'credentials'/name,(value+'\n').encode())
        os.environ[CREDENTIAL_ENV]=prospective
        password=secrets.token_urlsafe(32); private_write(self.root/'tls/password',(password+'\n').encode())
        self.r.run(['openssl','req','-x509','-newkey','rsa:3072','-sha256','-days','2','-nodes','-subj','/CN=native-receiver','-addext','subjectAltName=DNS:native-receiver','-keyout',str(self.root/'tls/key.pem'),'-out',str(self.root/'tls/native-receiver.crt')])
        self.r.run(['openssl','pkcs12','-export','-inkey',str(self.root/'tls/key.pem'),'-in',str(self.root/'tls/native-receiver.crt'),'-out',str(self.root/'tls/receiver.pfx'),'-passout','file:'+str(self.root/'tls/password')])
        for p in (self.root/'tls/key.pem',self.root/'tls/native-receiver.crt',self.root/'tls/receiver.pfx'): p.chmod(0o600)
        browser=secrets.token_bytes(32); private_write(self.root/'credentials/browser.sha256',(hashlib.sha256(browser).hexdigest()+'\n').encode())
        host=self.host_config(); private_write(self.root/'host.json',canonical(host))
        install={'Schema':'fsgg.telemetry.native-collector-installation/2','CredentialReference':'native-collector-v1','ExecutablePath':'/opt/fsgg/codex/codex','ExecutableSha256':NATIVE_SHA,'CodexHome':'/qualification/native/.codex','EvidenceRoot':'/qualification/evidence','Provider':'openai','Model':'gpt-5.6-sol','Effort':'medium'}
        private_write(self.root/'host.json.native-collector.json',canonical(install))
        workspace={'schema':'fsgg.telemetry.workspace-config/1','engine':'fsgg-coord-engine','associations':[{'workspaceId':SCOPE[0],'producerId':SCOPE[1],'streamId':SCOPE[2],'repositories':['FS-GG/.github'],'destination':{'kind':'remote','endpoint':'https://native-receiver:7443/','credentialReference':SCOPE[1],'spoolRoot':'/qualification/native/telemetry/spool'}}],'retiredAssociations':[]}
        private_write(self.root/'roadmap.json',canonical(workspace))
        self.phase('private-material-ready',receiverScope='/'.join(SCOPE))
    def host_config(self):
        def c(ref,file,producer,stream,role,grant,revoked=False): return {'Reference':ref,'SecretFile':'/qualification/credentials/'+file,'WorkspaceId':SCOPE[0],'ProducerId':producer,'StreamId':stream,'Role':role,'GrantId':grant,'GrantGeneration':1,'Revoked':revoked}
        return {'Schema':'fsgg.telemetry.host-config/2','ListenUrl':'https://0.0.0.0:7443','CertificatePath':'/qualification/tls/receiver.pfx','CertificatePasswordFile':'/qualification/tls/password','ServiceLockPath':'/qualification/store/host.lock','Stores':[{'WorkspaceId':SCOPE[0],'Root':'/qualification/store'}],'Credentials':[c(SCOPE[1],'prospective.token',SCOPE[1],SCOPE[2],'generic','grant-prospective-v1'),c('native-collector-v1','collector.token','native-collector-v1','native-inventory','native-collector','grant-native-collector-v1'),c('native-revoked-v1','revoked.token','native-revoked-v1','native-inventory','native-collector','grant-native-revoked-v1',True)],'BrowserPrincipals':[{'PrincipalId':'private-preflight','KeyHashFile':'/qualification/credentials/browser.sha256','WorkspaceIds':[SCOPE[0]],'Revoked':False}],'BrowserSession':{'IdleSeconds':300,'AbsoluteSeconds':600,'MaximumSessions':2,'LoginAttemptsPerMinute':2,'LoginAdmission':1,'QueryAdmission':1,'QueryTimeoutSeconds':10}}
    def cleanup(self):
        for name in reversed(self.containers): self.r.run(['podman','rm','-f',name],check=False,limit=4096)
        for name in reversed(self.networks): self.r.run(['podman','network','rm','-f',name],check=False,limit=4096)
        for key in ('FSGG_NATIVE_AUTH_JSON_B64','FSGG_PRIVATE_EFFECT_ADMISSION',CREDENTIAL_ENV): os.environ.pop(key,None)
    def write_result(self):
        self.a.result.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
        private_write(self.a.result,canonical(self.result))

def parse(argv):
    p=argparse.ArgumentParser(); p.add_argument('--operation-id',required=True); p.add_argument('--source-root',type=pathlib.Path,required=True); p.add_argument('--source-sha',required=True); p.add_argument('--private-root',type=pathlib.Path,required=True); p.add_argument('--run-nonce',required=True); p.add_argument('--host-package',type=pathlib.Path,required=True); p.add_argument('--host-manifest',type=pathlib.Path,required=True); p.add_argument('--host-journal',type=pathlib.Path,required=True); p.add_argument('--coord-root',type=pathlib.Path,required=True); p.add_argument('--native-executable',type=pathlib.Path,required=True); p.add_argument('--seal-public-key',type=pathlib.Path,required=True); p.add_argument('--result',type=pathlib.Path,required=True); return p.parse_args(argv)
def main(argv=None):
    a=parse(argv); require(a.operation_id==OPERATION,'operation-refused'); a.source_root=a.source_root.resolve()
    op=Operation(a,Runner(time.monotonic()+600))
    try:
        op.preflight(); op.materialize()
        # Effects are deliberately gated until the joined private workflow supplies immutable image IDs.
        op.result['disposition']='prepared-source-only'; op.phase('effect-gate',status='pending-root-admission')
        op.write_result(); return 0
    except Refusal as e:
        print('private-native-qualification-refused:'+str(e),file=sys.stderr); return 2
    finally: op.cleanup()
if __name__=='__main__': raise SystemExit(main())

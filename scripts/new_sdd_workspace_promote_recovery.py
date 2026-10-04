"""Wizard-only bounded forward completion of the retained 0.13 promotion intent.

No effect occurs at import. Normal publication never calls this adapter. An open
intent is corrected only in the uniquely selected native branch; uncertainty
never causes resend. Generic executor and protected journal remain unchanged.
"""
from __future__ import annotations
import ssl, base64, ctypes, hashlib, importlib.util, json, os, pathlib, re, selectors, shutil, signal, stat, subprocess, sys, time, urllib.error, urllib.parse, urllib.request, zipfile
from xml.etree import ElementTree as ET
from datetime import datetime, timezone
from release_successor_execution import Refused, JournalState
from release_successor_journal import ProtectedReleaseJournal, REPOSITORY as AUTHORITY
from new_sdd_workspace_successor_admission import WizardAdmission
from new_sdd_workspace_successor_execution import effects, PACKAGE, VERSION, TAG
from new_sdd_workspace_successor_provider import output_signals

Path=pathlib.Path
STREAM_CAP=1024*1024
PROC_CAP=8192
MEMBER_CAP=128
JSON_CAP=4*1024*1024
BINARY_CAP=8*1024*1024
ZIP_CAP=32*1024*1024
REF="refs/heads/fsgg/v2/journal/release/tsdd-knowledge-wizard-013"
REPO="FS-GG/.github"
CANDIDATE_SOURCE="f891b5b0723070c67e08d1a87b7d12b0b4d8bebe"
CANDIDATE_RUN=37159899280
ARTIFACT=11287256853
ARCHIVE="46055667bb35affacbc7c3be47a4eaea8983ae786267ff00bd9173d66b538244"
ORIGINAL_PACKAGE="accab9375f0adff5cc770f096ada7c56428674fc5c801449ad91cff4c39ccc37"
FAILED_RUN=37160659521
RELEASE=402727082
JOURNAL_HEAD="dbc2c4e578cb41e9f6aa31f18557cd95745346f7"
PREVIOUS_SOURCE="4889c446de0a431d1168a61a89ab660fc2062314"
WORKFLOW=".github/workflows/release-new-sdd-workspace.yml"

def require(ok, reason):
    if not ok: raise Refused(reason)
def digest(raw): return hashlib.sha256(raw).hexdigest()
def canonical(value): return json.dumps(value,sort_keys=True,separators=(",",":"),ensure_ascii=False).encode()
def load_module(name):
    spec=importlib.util.spec_from_file_location(name.replace("-","_"),Path(__file__).with_name(name+".py"))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

def validate_binding(binding, mode):
    fields={"heldSource","heldTree","journalHead","releaseId","failedRunId","correlation","selectedAfter","priorRunIds","predecessorReleaseId","predecessorAssets","home","recipientSha256","readinessRunId","readinessArtifactId","readinessArchiveSha256","readinessBindingSha256","readinessCiphertextSha256","readinessCorrelation"}
    require(set(binding)==fields,"recovery binding shape")
    require(mode in {"diagnostic","complete"},"recovery mode")
    require(all(re.fullmatch(r"[0-9a-f]{40}",binding[k]) for k in ("heldSource","heldTree","journalHead")),"recovery revision")
    require(binding["journalHead"]==JOURNAL_HEAD and binding["releaseId"]==RELEASE and binding["failedRunId"]==FAILED_RUN,"original recovery identities")
    require(re.fullmatch(r"[0-9a-f]{32}",binding["correlation"]) is not None,"correlation")
    require(re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z",binding["selectedAfter"]) is not None,"selection instant")
    require(isinstance(binding["priorRunIds"],list) and len(binding["priorRunIds"])<=8 and len(set(binding["priorRunIds"]))==len(binding["priorRunIds"]) and all(type(x)is int and x>0 for x in binding["priorRunIds"]),"prior selected runs")
    require(type(binding["predecessorReleaseId"])is int and binding["predecessorReleaseId"]>0,"predecessor identity")
    require(isinstance(binding["predecessorAssets"],dict) and 1<=len(binding["predecessorAssets"])<=8 and all(isinstance(n,str) and re.fullmatch(r"[A-Za-z0-9._-]+",n) and re.fullmatch(r"[0-9a-f]{64}",v) for n,v in binding["predecessorAssets"].items()),"predecessor assets")
    require(re.fullmatch(r"[0-9a-f]{64}",binding["recipientSha256"]) is not None,"recipient digest")
    readiness=("readinessRunId","readinessArtifactId","readinessArchiveSha256","readinessBindingSha256","readinessCiphertextSha256","readinessCorrelation")
    if mode=="diagnostic":require(all(binding[k] is None for k in readiness),"diagnostic has no future readiness assertion")
    else:
        require(type(binding["readinessRunId"])is int and binding["readinessRunId"] in binding["priorRunIds"] and type(binding["readinessArtifactId"])is int and binding["readinessArtifactId"]>0,"accepted diagnostic identities required")
        require(all(re.fullmatch(r"[0-9a-f]{64}",binding[k] or "") for k in ("readinessArchiveSha256","readinessBindingSha256","readinessCiphertextSha256")) and re.fullmatch(r"[0-9a-f]{32}",binding["readinessCorrelation"] or ""),"actual readiness custody required")
    require(binding["home"]==os.environ.get("HOME"),"actual HOME")
    return binding

class Budget:
    def __init__(self,mode,clock=time.monotonic,start=None):
        self.clock=clock;self.start=clock() if start is None else start;self.end=self.start+(600 if mode=="diagnostic" else 1200);self.work=self.start+(480 if mode=="diagnostic" else 1000)
        self.reserve=False;self.reads=0;self.commands=0;self.release_patches=0;self.cas_writes=[];self.mode=mode
    def remaining(self,maximum,reserve=False):
        left=(self.end if reserve else self.work)-self.clock();require(left>0,"operation deadline");return min(maximum,left)
    def read(self):
        self.reads+=1;require(self.reads<=120,"read request cap");return self.remaining(25)
    def command(self,seconds):
        self.commands+=1;require(self.commands<=(11 if self.mode=="diagnostic" else 19),"command cap");return self.remaining(seconds,self.reserve)

class Redirect(urllib.request.HTTPRedirectHandler):
    def __init__(self,budget):self.budget=budget
    def redirect_request(self,req,fp,code,msg,headers,newurl):
        parsed=urllib.parse.urlsplit(newurl)
        require(req.get_method()=="GET" and parsed.scheme=="https" and not parsed.username and not parsed.password,"redirect trust")
        host=parsed.hostname or ""
        require(host in {"api.github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","api.nuget.org"} or host.endswith(".blob.core.windows.net"),"redirect host")
        self.budget.read()
        result=super().redirect_request(req,fp,code,msg,headers,newurl)
        if parsed.netloc!=urllib.parse.urlsplit(req.full_url).netloc:result.remove_header("Authorization")
        return result

class FiniteAPI:
    def __init__(self,token,budget,root,authority=False):
        require(bool(token),"native read credential absent");self.token=token;self.budget=budget;self.root=root;self.authority=authority;self.records=[]
        self.expectedSettlement=None;self.cas_objects={};self.immutable={}
        self.opener=urllib.request.build_opener(Redirect(budget))
    def request(self,url,method="GET",body=None,binary=False,headers=None,custody=False):
        parsed=urllib.parse.urlsplit(url);require(parsed.scheme=="https" and parsed.hostname in {"api.github.com","nuget.pkg.github.com","api.nuget.org"} and not parsed.username and not parsed.password,"request origin")
        timeout=self.budget.read() if method=="GET" else self.budget.remaining(25)
        rawbody=None if body is None else canonical(body)
        auth={"Authorization":"Bearer "+self.token,"User-Agent":"fsgg-wizard-recovery","Accept":"application/vnd.github+json","X-GitHub-Api-Version":"2022-11-28"} if parsed.hostname=="api.github.com" else {"User-Agent":"fsgg-wizard-recovery"}
        request=urllib.request.Request(url,data=rawbody,method=method,headers={**auth,**(headers or {}),**({"Content-Type":"application/json"} if body is not None else {})})
        cap=(128*1024*1024+8192 if custody else BINARY_CAP) if binary else JSON_CAP
        require(not custody or (method=="GET" and binary and parsed.hostname=="api.github.com" and re.fullmatch(r"/repos/FS-GG/\.github/actions/artifacts/[0-9]+/zip",parsed.path)),"encrypted readiness archive route")
        index=len(self.records);target=self.root/f"response-{('authority' if self.authority else 'github')}-{index}.raw"
        row={"method":method,"origin":parsed.hostname,"path":parsed.path,"query":"withheld","status":None,"bytes":0}
        self.records.append(row)
        try:
            with self.opener.open(request,timeout=timeout) as response,target.open("xb") as stream:
                row["status"]=response.status
                while True:
                    self.budget.remaining(25)
                    data=response.read(min(65536,cap-row["bytes"]+1))
                    if not data:break
                    row["bytes"]+=len(data);require(row["bytes"]<=cap,"response byte cap");stream.write(data)
            row["sha256"]=digest(target.read_bytes());return target if binary else json.loads(target.read_bytes())
        except urllib.error.HTTPError as error:
            row["status"]=error.code;raise Refused("native HTTP read/write refused") from error
        finally:
            (self.root/f"transport-{('authority' if self.authority else 'github')}.json").write_bytes(canonical(self.records))
    def get(self,path):
        base=f"repos/{AUTHORITY if self.authority else REPO}"
        require(path==base or path.startswith(base+"/"),"repository request scope")
        if path in self.immutable:return json.loads(json.dumps(self.immutable[path]))
        return self.request("https://api.github.com/"+path)
    def prime_journal(self):
        require(self.authority,"Authority-only immutable cache")
        prefix=f"repos/{AUTHORITY}"
        commits=self.get(prefix+f"/commits?sha={JOURNAL_HEAD}&per_page=100")
        require(isinstance(commits,list) and len(commits)==16,"exact original sixteen commit roster")
        parent=JOURNAL_HEAD;seen=set()
        for commit in commits:
            oid=commit.get("sha");require(oid==parent and oid not in seen,"immutable commit lineage identity")
            seen.add(oid);parents=commit.get("parents",[]);require(len(parents)<=1,"immutable commit parent roster")
            parent=parents[0]["sha"] if parents else None
            contents=self.get(prefix+f"/contents/release-state.json?ref={oid}")
            require(contents.get("type")=="file" and contents.get("path")=="release-state.json" and contents.get("encoding")=="base64","exact protected blob source")
            raw=base64.b64decode(contents["content"]);blob=contents.get("sha")
            require(hashlib.sha1(b"blob "+str(len(raw)).encode()+b"\0"+raw).hexdigest()==blob,"immutable Git blob digest")
            value=json.loads(raw);require(raw==canonical(value)+b"\n","immutable canonical journal blob")
            tree_raw=b"100644 release-state.json\0"+bytes.fromhex(blob)
            tree=hashlib.sha1(b"tree "+str(len(tree_raw)).encode()+b"\0"+tree_raw).hexdigest()
            require(commit.get("commit",{}).get("tree",{}).get("sha")==tree,"complete one-file Git tree digest")
            self.immutable[prefix+"/git/commits/"+oid]={"sha":oid,"parents":[{"sha":p["sha"]} for p in parents],"tree":{"sha":tree}}
            self.immutable[prefix+"/git/trees/"+tree]={"tree":[{"path":"release-state.json","sha":blob}]}
            self.immutable[prefix+"/git/blobs/"+blob]={"sha":blob,"encoding":"base64","content":contents["content"]}
        require(parent is None,"protected original lineage must terminate")
    def arm_settlement(self,observed):
        require(self.budget.mode=="complete" and observed.head==JOURNAL_HEAD and observed.state["generation"]==16 and observed.state["effects"].get("promote")=="intent","settlement arm")
        self.expectedSettlement={**observed.state,"generation":17,"effects":{**observed.state["effects"],"promote":"verified"}}
    def post(self,path,body):
        require(self.authority and self.budget.mode=="complete","diagnostic mutation forbidden")
        suffix=path.removeprefix(f"repos/{AUTHORITY}/")
        expected=("git/blobs","git/trees","git/commits","git/refs/"+REF.removeprefix("refs/"))
        require(len(self.budget.cas_writes)<4 and suffix==expected[len(self.budget.cas_writes)],"one protected CAS allowlist")
        if suffix=="git/blobs":
            require(set(body)=={"content","encoding"} and body["encoding"]=="base64","CAS blob envelope")
            raw=base64.b64decode(body["content"],validate=True);value=json.loads(raw)
            require(self.expectedSettlement is not None and value==self.expectedSettlement and raw==canonical(value)+b"\n","CAS immutable blob target")
        if suffix=="git/trees":require(body=={"tree":[{"path":"release-state.json","mode":"100644","type":"blob","sha":self.cas_objects.get("git/blobs")}]},"CAS one blob tree")
        if suffix=="git/commits":require(body=={"message":"Release successor generation 17","tree":self.cas_objects.get("git/trees"),"parents":[JOURNAL_HEAD]},"CAS original parent/tree")
        self.budget.cas_writes.append(suffix)
        result=self.request("https://api.github.com/"+path,"POST",body)
        require(re.fullmatch(r"[0-9a-f]{40}",result.get("sha","")) is not None,"CAS object identity")
        self.cas_objects[suffix]=result["sha"];return result
    def patch(self,path,body):
        if self.authority:
            require(body=={"sha":self.cas_objects.get("git/commits"),"force":False} and self.cas_objects.get("git/commits") is not None,"protected nonforced bound ref")
            # Reuse the closed CAS sequence without permitting a POST to the ref.
            expected="git/refs/"+REF.removeprefix("refs/")
            require(self.budget.mode=="complete" and len(self.budget.cas_writes)==3 and path==f"repos/{AUTHORITY}/"+expected,"one CAS ref target")
            self.budget.cas_writes.append(expected)
        else:
            require(self.budget.mode=="complete" and path==f"repos/{REPO}/releases/{RELEASE}" and body=={"draft":False,"make_latest":"false"} and self.budget.release_patches==0,"one promotion PATCH allowlist")
            self.budget.release_patches+=1
        return self.request("https://api.github.com/"+path,"PATCH",body)

def proc_identity(pid):
    try:
        with (Path('/proc') / str(pid) / 'stat').open() as stream: raw = stream.read(4096)
        fields = raw[raw.rfind(')') + 2:].split()
        return dict(pid=pid, state=fields[0], ppid=int(fields[1]), pgid=int(fields[2]), sid=int(fields[3]), start=int(fields[19]))
    except (FileNotFoundError, ProcessLookupError): return None


def all_processes():
    entries = list(Path('/proc').iterdir()); require(len(entries) <= PROC_CAP, 'proc-scan-cap')
    return [identity for entry in entries if entry.name.isdigit() and (identity := proc_identity(int(entry.name))) is not None]


def same_process(one, two):
    return two is not None and all(one[k] == two[k] for k in ('pid', 'start'))


class OwnedChild:
    """Leader stays unreaped while its session is observed; signals use bound pidfds only."""
    def __init__(self, proc, adopted=False):
        self.proc = proc; self.members = {}; self.reaped = False; self.adopted = adopted
        # Popen has not polled/waited: this child PID is reserved until our reap.
        try: fd = os.pidfd_open(proc.pid, 0)
        except BaseException:
            # Popen.kill polls under its wait lock; only this unreaped child is eligible.
            proc.kill(); proc.wait(timeout=2); raise
        try:
            self.leader = proc_identity(proc.pid)
            require(self.leader is not None and self.leader['pgid'] == self.leader['sid'] == proc.pid and self.leader['ppid'] == os.getpid(), 'owned-session-identity')
            self.members[(self.leader['pid'], self.leader['start'])] = (dict(self.leader), fd)
        except BaseException:
            try: signal.pidfd_send_signal(fd, signal.SIGKILL, None, 0)
            except ProcessLookupError: pass
            try: proc.wait(timeout=2)
            finally: os.close(fd)
            raise

    def acquire(self, identity):
        key = (identity['pid'], identity['start'])
        if key in self.members: return
        require(len(self.members) < MEMBER_CAP, 'owned-member-cap')
        fd = os.pidfd_open(identity['pid'], 0)
        if not same_process(identity, proc_identity(identity['pid'])):
            os.close(fd); raise ValueError('pidfd-start-identity-drift')
        self.members[key] = (dict(identity), fd)

    def observe(self):
        require(not self.reaped and same_process(self.leader, proc_identity(self.proc.pid)), 'leader-start-identity-drift')
        current = proc_identity(self.proc.pid)
        require(current['sid'] == current['pgid'] == self.leader['pid'], 'leader-session-drift')
        identities = all_processes()
        # The held leader PID cannot be reused; same-session members are owned.
        owned = [i for i in identities if i['sid'] == self.leader['sid'] or (self.adopted and i['ppid'] == os.getpid())]
        # Retain observed descendants even if they change their process group/session.
        known = {i['pid'] for i, fd in self.members.values() if same_process(i, proc_identity(i['pid']))}
        changed = True
        while changed:
            added = [i for i in identities if i['ppid'] in known and i['pid'] not in known]
            changed = bool(added); known.update(i['pid'] for i in added); owned.extend(added)
        for identity in owned:
            try: self.acquire(identity)
            except ProcessLookupError: pass
        return owned

    def exited(self):
        # WNOWAIT reserves leader PID/session authority until settlement completes.
        return os.waitid(os.P_PID, self.proc.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT)

    def signal_member(self, identity, fd):
        # Even if numeric PID was reused, this pidfd can only signal its original process.
        current = proc_identity(identity['pid'])
        if not same_process(identity, current): return False
        identity['lastObserved'] = current
        try: signal.pidfd_send_signal(fd, signal.SIGKILL, None, 0)
        except ProcessLookupError: pass
        return True

    def settle(self, deadline):
        require(not self.reaped, 'already-reaped-custody')
        errors = []
        while time.monotonic() < deadline:
            for identity, fd in self.members.values(): self.signal_member(identity, fd)
            try: self.observe()
            except BaseException as error:
                errors.append(type(error).__name__ + ':' + str(error)); break
            for identity, fd in self.members.values(): self.signal_member(identity, fd)
            if self.adopted:
                for identity, fd in self.members.values():
                    current = proc_identity(identity['pid'])
                    if identity['pid'] != self.proc.pid and same_process(identity, current) and current['ppid'] == os.getpid() and current['state'] == 'Z':
                        os.waitid(os.P_PID, identity['pid'], os.WEXITED | os.WNOHANG)
            remaining = [i for i, fd in self.members.values() if i['pid'] != self.proc.pid and same_process(i, proc_identity(i['pid']))]
            if not remaining and self.exited() is not None: break
            time.sleep(min(0.02, max(0, deadline - time.monotonic())))
        remaining = [dict(i) for i, fd in self.members.values() if i['pid'] != self.proc.pid and same_process(i, proc_identity(i['pid']))]
        exited = self.exited()
        if exited is not None:
            # No more session scans or numeric-ID signals after this reap.
            self.proc.wait(timeout=0); self.reaped = True
        else: remaining.append(dict(self.leader))
        record = {'leader': self.leader, 'members': [dict(i) for i, fd in self.members.values()], 'leaderReaped': self.reaped, 'remaining': remaining, 'errors': errors}
        for identity, fd in self.members.values(): os.close(fd)
        return record


def capture(proc, custody, deadline):
    buffers = {'stdout': bytearray(), 'stderr': bytearray()}; selector = selectors.DefaultSelector()
    for name in buffers:
        stream = getattr(proc, name); os.set_blocking(stream.fileno(), False); selector.register(stream, selectors.EVENT_READ, name)
    try:
        while selector.get_map() or custody.exited() is None:
            require(time.monotonic() < deadline, 'command-deadline')
            custody.observe()
            for key, mask in selector.select(min(0.05, max(0, deadline - time.monotonic()))):
                data = os.read(key.fileobj.fileno(), 16 * 1024)
                if not data: selector.unregister(key.fileobj); continue
                require(len(buffers[key.data]) + len(data) <= STREAM_CAP, 'command-stream-cap:' + key.data)
                buffers[key.data].extend(data)
        status = custody.exited()
        code = status.si_status if status.si_code == os.CLD_EXITED else -status.si_status
        return dict(actualExitCode=code, **{name: bytes(data) for name, data in buffers.items()})
    except BaseException as error:
        error.boundedCapture={name:bytes(data) for name,data in buffers.items()}
        raise
    finally:
        selector.close()
        for name in buffers: getattr(proc, name).close()


class Runner:
    def __init__(self,budget,root):self.budget=budget;self.root=root;self.records=[]
    def run(self,argv,seconds,env,cwd=None):
        timeout=self.budget.command(seconds);index=len(self.records)
        row={"index":index,"argv":argv,"actualExitCode":None,"custody":None,"errorKind":None}
        self.records.append(row);child=None;proc=None;result=None
        try:
            proc=subprocess.Popen(argv,stdout=subprocess.PIPE,stderr=subprocess.PIPE,start_new_session=True,env=env,cwd=cwd)
            child=OwnedChild(proc,adopted=True)
            result=capture(proc,child,min(self.budget.end if self.budget.reserve else self.budget.work,self.budget.clock()+timeout))
            row["actualExitCode"]=result["actualExitCode"]
            return subprocess.CompletedProcess(argv,row["actualExitCode"],result["stdout"].decode("utf-8",errors="replace"),result["stderr"].decode("utf-8",errors="replace"))
        except BaseException as error:
            row["errorKind"]=type(error).__name__;result=getattr(error,"boundedCapture",result)
            raise
        finally:
            if child is not None:
                try:row["custody"]=child.settle(min(self.budget.end,self.budget.clock()+20))
                except BaseException as error:row["custody"]={"leaderReaped":False,"remaining":[],"errors":[type(error).__name__],"members":[]}

            elif proc is not None:
                for stream in (proc.stdout,proc.stderr):
                    if stream:stream.close()
                row["errorKind"]=row["errorKind"] or "identity-unbound"
            for name in ("stdout","stderr"):
                raw=(result or {}).get(name,b"");target=self.root/f"command-{index}-{name}.raw"
                require(len(raw)<=STREAM_CAP,"raw capture cap");target.write_bytes(raw)
                row[name]={"file":target.name,"bytes":len(raw),"sha256":digest(raw),"safe":output_signals(raw)}
            (self.root/"commands.json").write_bytes(canonical(self.records))
            require(row["custody"] is not None and row["custody"].get("leaderReaped") and not row["custody"].get("remaining") and not row["custody"].get("errors"),"child custody failed")

def subreaper():
    require(sys.platform=="linux" and hasattr(os,"pidfd_open") and hasattr(signal,"pidfd_send_signal"),"pidfd custody unavailable")
    libc=ctypes.CDLL(None,use_errno=True);require(libc.prctl(36,1,0,0,0)==0,"subreaper unavailable")

def child_environment(root):
    root.mkdir(exist_ok=True)
    allowed={"HOME","PATH","DOTNET_ROOT","LANG","LC_ALL","TZ","TERM","SSL_CERT_FILE","SSL_CERT_DIR"}
    env={k:v for k,v in os.environ.items() if k in allowed}
    env.update({"DOTNET_GENERATE_ASPNET_CERTIFICATE":"false","DOTNET_SKIP_FIRST_TIME_EXPERIENCE":"1","DOTNET_CLI_HOME":str(root/"cli"),"NUGET_PACKAGES":str(root/"packages"),"NUGET_HTTP_CACHE_PATH":str(root/"http"),"TMPDIR":str(root/"temp"),"PYTHONDONTWRITEBYTECODE":"1"})
    for name in ("cli","packages","http","temp"): (root/name).mkdir()
    require(not any(any(word in k.upper() for word in ("TOKEN","SECRET","KEY","PASSWORD","CREDENTIAL")) for k in env),"credential child environment")
    return env

def physical_roster(root,cap=50000,deadline=None):
    root=Path(root).resolve();rows={}
    for path in sorted(root.rglob("*")):
        relative=path.relative_to(root).as_posix()
        if relative==".git" or relative.startswith(".git/"):continue
        if path.is_file() or path.is_symlink():
            require(len(rows)<cap,"physical roster cap")
            require(path.resolve().is_relative_to(root),"physical source escape")
            require(path.stat().st_size<=256*1024*1024,"physical member byte cap")
            hashed=hashlib.sha256()
            with path.open("rb") as stream:
                for chunk in iter(lambda:stream.read(65536),b""):
                    if deadline is not None:require(time.monotonic()<deadline,"physical roster deadline")
                    hashed.update(chunk)
            rows[relative]={"sha256":hashed.hexdigest(),"link":os.readlink(path) if path.is_symlink() else None}
    return rows

def source_snapshot(source,binding,runner,env):
    source=Path(source).resolve()
    def call(argv):
        result=runner.run(argv,10,env,cwd=source);require(result.returncode==0,"git source proof failed");return result.stdout
    require(call(["git","status","--porcelain","--untracked-files=all"])=="","source checkout not clean")
    require(call(["git","rev-parse","HEAD","HEAD^{tree}"]).splitlines()==[binding["heldSource"],binding["heldTree"]],"held source/tree mismatch")
    tracked=call(["git","ls-files","--stage","-z"]).split("\0")
    rows=physical_roster(source,deadline=runner.budget.end if runner.budget.reserve else runner.budget.work)
    names=set()
    for record in tracked:
        if not record:continue
        info,name=record.split("\t",1);mode,oid,stage=info.split();require(stage=="0","source index conflict")
        path=source/name;require(name in rows and mode in {"100644","100755","120000"},"source index membership")
        raw=os.readlink(path).encode() if mode=="120000" else path.read_bytes()
        require(hashlib.sha1(b"blob "+str(len(raw)).encode()+b"\0"+raw).hexdigest()==oid,"source blob mismatch")
        names.add(name)
    require(set(rows)==names,"source full membership differs")
    return rows

def safe_zip(path,required=None,custody=False):
    with zipfile.ZipFile(path) as archive:
        names=archive.namelist();require(len(names)==len(set(names)) and len(names)<=128,"archive duplicate/roster cap")
        if required is not None:require(set(names)==set(required),"literal archive members")
        require(sum(x.file_size for x in archive.infolist())<=(128*1024*1024+8192 if custody else ZIP_CAP),"archive expansion cap")
        rows={}
        for member in archive.infolist():
            name=Path(member.filename);require(not member.is_dir() and not name.is_absolute() and '..' not in name.parts and not stat.S_ISLNK(member.external_attr>>16) and member.file_size<=(128*1024*1024+8192 if custody else BINARY_CAP),"unsafe archive member")
            rows[member.filename]=archive.read(member)
        return rows

def native_context(api,binding,mode,run_id):
    title=f"Wizard 0.13 recovery {mode} {binding['correlation']} {digest(canonical(binding))}"
    matches=[]
    for page in (1,2):
        query=urllib.parse.urlencode({"event":"workflow_dispatch","branch":"main","created":">="+binding["selectedAfter"],"per_page":100,"page":page})
        data=api.get(f"repos/{REPO}/actions/workflows/release-new-sdd-workspace.yml/runs?{query}")
        require(data.get("total_count",0)<=200,"correlation enumeration exhausted")
        matches.extend(row for row in data.get("workflow_runs",[]) if row.get("display_title")==title)
        if len(data.get("workflow_runs",[]))<100:break
    require(len(matches)==1 and matches[0].get("id")==run_id,"native correlation missing/ambiguous")
    run=api.get(f"repos/{REPO}/actions/runs/{run_id}")
    require(run.get("id")==run_id and run.get("display_title")==title and run.get("head_sha")==binding["heldSource"] and run.get("run_attempt")==1 and run.get("event")=="workflow_dispatch" and run.get("head_branch")=="main" and run.get("path")==WORKFLOW and run.get("actor",{}).get("login")=="EHotwagner" and run.get("status")=="in_progress" and run.get("repository",{}).get("id")==1269292704,"selected native role")
    require(api.get(f"repos/{REPO}/git/ref/heads/main").get("object",{}).get("sha")==binding["heldSource"],"held main drift")

def original_runs(api,binding):
    original=api.get(f"repos/{REPO}/actions/runs/{FAILED_RUN}")
    require(original.get("id")==FAILED_RUN and original.get("head_sha")==CANDIDATE_SOURCE and original.get("run_attempt")==1 and original.get("status")=="completed" and original.get("conclusion")=="failure" and original.get("path")==WORKFLOW and original.get("actor",{}).get("login")=="EHotwagner" and original.get("repository",{}).get("id")==1269292704 and original.get("event")=="workflow_dispatch" and original.get("head_branch")=="main","original failed publisher drift")
    for ident in binding["priorRunIds"]:
        row=api.get(f"repos/{REPO}/actions/runs/{ident}")
        require(row.get("id")==ident and row.get("status")=="completed" and row.get("path")==WORKFLOW and row.get("repository",{}).get("id")==1269292704 and row.get("actor",{}).get("login")=="EHotwagner" and row.get("run_attempt")==1,"intervening writer not terminal")
    require(api.get(f"repos/{REPO}/compare/{CANDIDATE_SOURCE}...{binding['heldSource']}").get("status")=="ahead","candidate ancestry differs")

def readiness(api,binding):
    ident=binding["readinessRunId"];artifact_id=binding["readinessArtifactId"]
    run=api.get(f"repos/{REPO}/actions/runs/{ident}")
    title=f"Wizard 0.13 recovery diagnostic {binding['readinessCorrelation']} {binding['readinessBindingSha256']}"
    require(run.get("id")==ident and run.get("display_title")==title and run.get("head_sha")==binding["heldSource"] and run.get("run_attempt")==1 and run.get("conclusion")=="success" and run.get("status")=="completed" and run.get("path")==WORKFLOW and run.get("event")=="workflow_dispatch" and run.get("head_branch")=="main" and run.get("actor",{}).get("login")=="EHotwagner" and run.get("repository",{}).get("id")==1269292704,"accepted native diagnostic required")
    artifact=api.get(f"repos/{REPO}/actions/artifacts/{artifact_id}")
    require(artifact.get("id")==artifact_id and artifact.get("expired")is False and artifact.get("name")==f"wizard013-recovery-diagnostic-{ident}-{binding['readinessCorrelation']}" and artifact.get("digest")=="sha256:"+binding["readinessArchiveSha256"] and artifact.get("workflow_run",{}).get("id")==ident and artifact.get("workflow_run",{}).get("head_sha")==binding["heldSource"] and artifact.get("workflow_run",{}).get("repository_id")==1269292704 and artifact.get("workflow_run",{}).get("head_repository_id")==1269292704,"accepted diagnostic artifact origin")
    archive=api.request(f"https://api.github.com/repos/{REPO}/actions/artifacts/{artifact_id}/zip",binary=True,custody=True)
    require(digest(archive.read_bytes())==binding["readinessArchiveSha256"],"original diagnostic archive")
    members=safe_zip(archive,{"custody.cms","summary.json"},custody=True);summary=json.loads(members["summary.json"])
    require(digest(members["custody.cms"])==binding["readinessCiphertextSha256"] and summary.get("ciphertextSha256")==binding["readinessCiphertextSha256"] and summary.get("bindingSha256")==binding["readinessBindingSha256"] and summary.get("nativeRunId")==ident and summary.get("schema")=="fsgg.wizard-recovery-encrypted-custody/1" and summary.get("commandExit")==0,"accepted readiness ciphertext/native join")

def predecessor(api,binding):
    release=api.get(f"repos/{REPO}/releases/{binding['predecessorReleaseId']}")
    require(release.get("id")==binding["predecessorReleaseId"] and release.get("tag_name")=="new-sdd-workspace/v0.12.0" and release.get("draft")is False and release.get("prerelease")is False,"immutable predecessor release")
    require(api.get(f"repos/{REPO}/git/ref/tags/new-sdd-workspace/v0.12.0").get("object",{}).get("sha")==PREVIOUS_SOURCE,"predecessor source")
    assets=api.get(f"repos/{REPO}/releases/{release['id']}/assets?per_page=100")
    require(len(assets)==len(binding["predecessorAssets"]) and len({r.get('name') for r in assets})==len(assets),"predecessor roster")
    for row in assets:
        require(row.get("name") in binding["predecessorAssets"],"predecessor asset")
        target=api.request(f"https://api.github.com/repos/{REPO}/releases/assets/{row['id']}",binary=True,headers={"Accept":"application/octet-stream"})
        require(digest(target.read_bytes())==binding["predecessorAssets"][row["name"]],"predecessor bytes")

def candidate(api,root):
    run=api.get(f"repos/{REPO}/actions/runs/{CANDIDATE_RUN}");artifact=api.get(f"repos/{REPO}/actions/artifacts/{ARTIFACT}")
    require(run.get("id")==CANDIDATE_RUN and artifact.get("id")==ARTIFACT and artifact.get("digest")=="sha256:"+ARCHIVE,"original candidate IDs")
    raw=api.request(f"https://api.github.com/repos/{REPO}/actions/artifacts/{ARTIFACT}/zip",binary=True)
    require(digest(raw.read_bytes())==ARCHIVE,"original archive bytes")
    names={f"{PACKAGE}.{VERSION}.nupkg","manifest.json","package-evidence.json"};members=safe_zip(raw,names)
    target=root/"candidate";target.mkdir()
    for name,data in members.items():(target/name).write_bytes(data)
    verifier=load_module("new-sdd-workspace-successor-artifact")
    # Reuse exact original candidate/run/artifact semantic verification, replacing
    # its only subprocess with a bounded in-process package verifier below.
    require(run.get("path")==verifier.WORKFLOW and run.get("head_sha")==CANDIDATE_SOURCE and run.get("head_branch")=="main" and run.get("event")=="workflow_dispatch" and run.get("conclusion")=="success" and run.get("run_attempt")==1 and run.get("repository",{}).get("id")==1269292704,"original candidate run")
    binding=artifact.get("workflow_run",{})
    require(artifact.get("name")==f"new-sdd-workspace-successor-candidate-{CANDIDATE_SOURCE}-{CANDIDATE_RUN}" and artifact.get("expired")is False and binding.get("id")==CANDIDATE_RUN and binding.get("head_sha")==CANDIDATE_SOURCE and binding.get("head_branch")=="main" and binding.get("repository_id")==1269292704 and binding.get("head_repository_id")==1269292704,"original artifact provenance")
    manifest=load_module("new-sdd-workspace-release").load_manifest(target/"manifest.json")
    require(manifest["sourceSha"]==CANDIDATE_SOURCE,"original producer source")
    package=target/f"{PACKAGE}.{VERSION}.nupkg";require(digest(package.read_bytes())==ORIGINAL_PACKAGE,"original package bytes")
    original=safe_zip(package);require(len(original)==26,"original 26 member closure")
    observation=load_module("new-sdd-workspace-release").verify_artifact(target/"manifest.json",package)
    require(observation["preparedArchiveEqual"] and observation["producerPayloadEqual"],"original package semantic proof")
    return manifest,original,target
def release_gate(api,manifest,original):
    cid,ordered=effects(manifest)
    release=api.get(f"repos/{REPO}/releases/{RELEASE}")
    require(release.get("id")==RELEASE and release.get("tag_name")==TAG and release.get("prerelease")is False and type(release.get("draft"))is bool and f"new-sdd-workspace-successor:{cid}" in release.get("body","") and release.get("name")==f"{PACKAGE} {VERSION}" and release.get("target_commitish")==CANDIDATE_SOURCE,"exact release binding")
    require(api.get(f"repos/{REPO}/git/ref/tags/{TAG}").get("object",{}).get("sha")==CANDIDATE_SOURCE,"release original tag source")
    assets=api.get(f"repos/{REPO}/releases/{RELEASE}/assets?per_page=100")
    expected={f"{PACKAGE}.{VERSION}.nupkg":608700537,"manifest.json":608701085,"publication-journal.json":608701711}
    require(len(assets)==3 and len({row.get('name') for row in assets})==3 and {r.get('name'):r.get('id') for r in assets}==expected,"literal original asset roster")
    bodies={}
    for row in assets:
        path=api.request(f"https://api.github.com/repos/{REPO}/releases/assets/{row['id']}",binary=True,headers={"Accept":"application/octet-stream"})
        require(path.stat().st_size==row.get("size"),"asset size binding");bodies[row["name"]]=path.read_bytes()
    require(digest(bodies[f"{PACKAGE}.{VERSION}.nupkg"])==ORIGINAL_PACKAGE and bodies["manifest.json"]==canonical(manifest)+b"\n","original asset bytes")
    remote=json.loads(bodies["publication-journal.json"])
    require(digest(bodies["publication-journal.json"])=="2e1d16a87400d8246cd31237b63e6366025619f26cb81742310de9a8426304c2" and remote.get("schema")=="fsgg.new-sdd-workspace-release-journal/v1" and remote.get("manifestSha256")==cid and set(remote.get("observations",{}))=={"github","nuget"},"original publication journal")
    feed_paths={}
    for feed in ("github","nuget"):
        ident=f"fs.gg.newsddworkspace.{VERSION}.nupkg"
        url=f"https://nuget.pkg.github.com/FS-GG/download/fs.gg.newsddworkspace/{VERSION}/{ident}" if feed=="github" else f"https://api.nuget.org/v3-flatcontainer/fs.gg.newsddworkspace/{VERSION}/{ident}"
        headers={"Authorization":"Basic "+base64.b64encode(("x:"+api.token).encode()).decode()} if feed=="github" else None
        path=api.request(url,binary=True,headers=headers);members=safe_zip(path)
        require(set(members)==set(original) if feed=="github" else set(members)==set(original)|{".signature.p7s"},"exact feed member/signature roster")
        require(all(members[name]==raw for name,raw in original.items()),"literal feed payload drift")
        row=remote["observations"][feed]
        require(row.get("archiveSha256")==digest(path.read_bytes()) and row.get("payloadSha256")==manifest["producerPayloadSha256"] and row.get("producerPayloadEqual")is True,"genuine feed journal join")
        feed_paths[feed]=path
    return release,feed_paths

def installed_closure(tool,original,nuget):
    closure=tool/".store/fs.gg.newsddworkspace/0.13.0/fs.gg.newsddworkspace/0.13.0/tools/net10.0/any"
    members={name.removeprefix("tools/net10.0/any/"):raw for name,raw in original.items() if name.startswith("tools/net10.0/any/")}
    require(len(members)==21,"original literal tool roster")
    require(closure.is_dir() and set(physical_roster(closure))==set(members),"installed complete closure")
    require(all((closure/name).read_bytes()==raw for name,raw in members.items()),"installed literal tool bytes")
    cached=closure.parents[2]/"fs.gg.newsddworkspace.0.13.0.nupkg"
    require(cached.is_file() and cached.read_bytes()==nuget.read_bytes(),"installed original signed archive")
    commands=[x for x in ET.parse(closure/"DotnetToolSettings.xml").iter() if x.tag.endswith("Command")]
    require(len(commands)==1 and commands[0].attrib.get("Name")=="new-sdd-workspace" and commands[0].attrib.get("EntryPoint")=="new-sdd-workspace.dll","literal tool target")
    apphost=tool/"new-sdd-workspace";require(apphost.is_file() and not apphost.is_symlink(),"literal apphost")
    relative=(closure/"new-sdd-workspace.dll").relative_to(tool).as_posix()
    require(relative.encode()+b"\0" in apphost.read_bytes(),"physical apphost target")
    return {"apphost":str(apphost),"sha256":digest(apphost.read_bytes()),"closure":physical_roster(closure),"cachedArchiveSha256":digest(cached.read_bytes())}

def public_install(root,runner,original,nuget,env):
    root.mkdir();tool=root/"tool";config=root/"NuGet.Config"
    config.write_text('<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    owned=child_environment(root)
    # Child environments contain no native GitHub/App/NuGet publisher credentials.
    require(owned['HOME']==env['HOME'] and owned['DOTNET_GENERATE_ASPNET_CERTIFICATE']=='false',"public child HOME/certificate guard")
    result=runner.run(["dotnet","tool","install",PACKAGE,"--version",VERSION,"--tool-path",str(tool),"--configfile",str(config)],180,owned)
    require(result.returncode==0,"public install exit")
    proof=installed_closure(tool,original,nuget)
    result=runner.run([proof["apphost"],"--help"],60,owned)
    require(result.returncode==0 and "new-sdd-workspace" in result.stdout+result.stderr,"public help predicate")
    require(installed_closure(tool,original,nuget)==proof,"help closure drift")
    return proof

class Recovery:
    """Production state decisions; transports and process calls are injectable in pure controls."""
    def __init__(self,api,journal,admission,binding,manifest,original,run_id,mode,root,installer):
        self.api=api;self.journal=journal;self.admission=admission;self.binding=binding;self.manifest=manifest;self.original=original;self.run_id=run_id;self.mode=mode;self.root=root;self.installer=installer
        self.cid,self.ordered=effects(manifest);self.install_count=0
    def state(self):
        current=self.journal.read()
        require(current==JournalState(16,self.cid,{**{e.identity:"verified" for e in self.ordered[:-1]},"promote":"intent"}),"exact generation16 seven verified/open promote")
        require(self.journal._observed.head==self.binding["journalHead"],"journal head drift")
        self.journal.validate_intent({"contentId":self.cid,"sourceSha":CANDIDATE_SOURCE,"version":VERSION,"candidateArchiveSha256":ARCHIVE,"operator":"EHotwagner"})
        return current
    def authority(self,action):
        require(self.admission.authorize_recovery(self.cid,action,self.ordered[-1].request_digest,self.binding,self.mode),"fresh corrective admission denied")
    def matched_gate(self):
        release,feeds=release_gate(self.api,self.manifest,self.original)
        self.install_count+=1;require(self.install_count<=(1 if self.mode=="diagnostic" else 2),"install count cap")
        proof=self.installer(self.root/f"public-install-{self.install_count}",feeds["nuget"])
        return release,proof
    def run(self):
        self.authority("dispatch" if self.mode=="complete" else "settle")
        original_runs(self.api,self.binding)
        if self.mode=="complete":readiness(self.api,self.binding)
        predecessor(self.api,self.binding);current=self.state()
        release,proof=self.matched_gate()
        if self.mode=="diagnostic":
            require(self.state()==current,"diagnostic journal drift")
            native_context(self.api,self.binding,self.mode,self.run_id)
            return {"scope":"diagnostic","publisherComplete":False,"publicInstall":proof,"releaseDraft":release["draft"]}
        patched=False;patch_unknown=False
        if release["draft"]:
            self.authority("dispatch");original_runs(self.api,self.binding);require(self.state()==current,"prepatch journal drift")
            immediate=self.api.get(f"repos/{REPO}/releases/{RELEASE}")
            require(all(immediate.get(key)==release.get(key) for key in ("id","tag_name","draft","prerelease","body","name","target_commitish","created_at")),"immediate draft identity/state drift")
            try:self.api.patch(f"repos/{REPO}/releases/{RELEASE}",{"draft":False,"make_latest":"false"})
            except Exception:patch_unknown=True
            patched=True
            # The response is deliberately not inspected as completion evidence.
            observed,proof=self.matched_gate();require(observed["draft"]is False,"promotion unresolved; no resend")
        self.authority("settle");require(self.state()==current,"presettle journal drift")
        uncertain=False
        if hasattr(self.journal.api,"arm_settlement"):self.journal.api.arm_settlement(self.journal._observed)
        try:require(self.journal.compare_and_swap(current,"promote","verified"),"settlement CAS conflict")
        except Exception:uncertain=True
        final=self.journal.read() if uncertain else JournalState(self.journal._observed.state["generation"],self.journal._observed.state["contentId"],self.journal._observed.state["effects"])
        require(final==JournalState(17,self.cid,{e.identity:"verified" for e in self.ordered}),"settlement unresolved; no CAS retry")
        self.journal.validate_intent({"contentId":self.cid,"sourceSha":CANDIDATE_SOURCE,"version":VERSION,"candidateArchiveSha256":ARCHIVE,"operator":"EHotwagner"})
        return {"scope":"forward-promotion-settlement","publisherComplete":True,"releasePatchCount":int(patched),"patchResponseUnknown":patch_unknown,"casResponseUnknown":uncertain,"generation":17,"authorityHead":self.journal._observed.head,"publicInstall":proof}

def worker(mode,binding,root,source,start=None):
    budget=Budget(mode,start=start);runner=Runner(budget,root);env=child_environment(root/"checks")
    before=None;sdk_before=None;report={"success":False,"historicalCause":"UNKNOWN","publicWizardQualified":False,"adoptionReceiptEmitted":False}
    try:
        subreaper();before=source_snapshot(source,binding,runner,env)
        sdk_root=Path(shutil.which("dotnet") or "").resolve().parent;require((sdk_root/"dotnet").is_file(),"existing SDK absent")
        sdk_before=physical_roster(sdk_root,deadline=budget.work)
        api=FiniteAPI(os.environ.get("GH_TOKEN"),budget,root)
        report["stage"]="selected-native-admission"
        native_context(api,binding,mode,int(os.environ["GITHUB_RUN_ID"]))
        report["stage"]="existing-sdk-version"
        version=runner.run(["dotnet","--version"],10,env,cwd=source)
        selected=json.loads((source/"global.json").read_text())["sdk"]["version"]
        require(version.returncode==0 and version.stdout.strip()==selected,"exact held SDK unavailable; no fallback")
        report["sdkVersion"]=version.stdout.strip()
        report["stage"]="original-candidate"
        manifest,original,_=candidate(api,root)
        ledger=FiniteAPI(os.environ.get("ORDINARY_LEDGER_TOKEN"),budget,root,authority=True)
        report["stage"]="immutable-authority-primer"
        ledger.prime_journal()
        journal=ProtectedReleaseJournal(ledger,REF)
        admission=WizardAdmission(api,manifest,binding["heldSource"],int(os.environ["GITHUB_RUN_ID"]),os.environ["GITHUB_ACTOR"],os.environ["GITHUB_REF"])
        engine=Recovery(api,journal,admission,binding,manifest,original,int(os.environ["GITHUB_RUN_ID"]),mode,root,lambda p,n:public_install(p,runner,original,n,env))
        report["stage"]="promotion-reconciliation"
        report.update(engine.run());report["success"]=True
    except BaseException as error:report["errorKind"]=type(error).__name__;report["success"]=False
    finally:
        # Failed operations retain the same full source/SDK membership checks.
        budget.reserve=True
        report["sourceRoster"]=before;report["sdkRoster"]=sdk_before
        report["sdkRoot"]=str(sdk_root) if sdk_before is not None else None
        for name,read,expected in (("Source",lambda:source_snapshot(source,binding,runner,env),before),
                                   ("Sdk",lambda:physical_roster(sdk_root,deadline=budget.end) if Path(shutil.which("dotnet") or "").resolve().parent==sdk_root else None,sdk_before)):
            try:
                observed=read();report["post"+name+"Roster"]=observed
                report["post"+name+"Matches"]=expected is not None and observed==expected
                if not report["post"+name+"Matches"]:report["success"]=False
            except BaseException as error:
                report["post"+name+"ErrorKind"]=type(error).__name__;report["post"+name+"Matches"]=False;report["success"]=False
        report["readCount"]=budget.reads;report["commandCount"]=budget.commands;report["releasePatchCount"]=budget.release_patches;report["casWrites"]=budget.cas_writes
        data=canonical(report);require(len(data)<=BINARY_CAP,"report cap");(root/"worker-report.json").write_bytes(data)
    return 0 if report["success"] else 1
def recipient(binding):
    pem=os.environ.get("RECOVERY_RECIPIENT_CERTIFICATE","")
    require(len(pem)<=8192 and pem.count("-----BEGIN CERTIFICATE-----")==1 and "PRIVATE KEY" not in pem,"public recipient only")
    der=ssl.PEM_cert_to_DER_cert(pem)
    require(digest(der)==binding["recipientSha256"],"root-selected recipient differs")
    # RSA key transport is fixed to OAEP/SHA256; unsupported recipient/provider
    # refuses through the one standard OpenSSL CMS command without fallback.
    require(bytes.fromhex("06092a864886f70d010101") in der,"RSA recipient required")
    return pem

def encrypted_custody(root,binding,runner):
    pem=recipient(binding);certificate=root/"recipient.pem";certificate.write_text(pem)
    export=root/"export";require(not export.exists(),"fresh ciphertext export");export.mkdir()
    archive=root/"raw-custody.zip";entries=[];total=0
    with zipfile.ZipFile(archive,"x",compression=zipfile.ZIP_DEFLATED) as zipped:
        for path in sorted(root.rglob("*")):
            if path==archive or path.is_dir():continue
            relative=path.relative_to(root).as_posix()
            require(not path.is_symlink() and path.resolve().is_relative_to(root.resolve()),"raw archive physical scope")
            require(len(entries)<10000 and path.stat().st_size<=BINARY_CAP,"raw custody member cap")
            total+=path.stat().st_size;require(total<=128*1024*1024,"raw custody total cap")
            runner.budget.remaining(25,True)
            raw=path.read_bytes();entries.append({"path":relative,"bytes":len(raw),"sha256":digest(raw)});zipped.writestr(relative,raw)
        zipped.writestr("custody-members.json",canonical(entries))
    require(archive.stat().st_size<=128*1024*1024,"custody archive cap")
    ciphertext=root/"custody.pending.cms"
    # Source basis: OpenSSL 3.0 CMS docs specify AES-GCM AuthEnvelopedData and
    # -recip/-keyopt for RSA-OAEP. No provider, signature or TLS override.
    command=["openssl","cms","-encrypt","-binary","-aes-256-gcm","-outform","DER","-in",str(archive),"-out",str(ciphertext),"-recip",str(certificate),"-keyopt","rsa_padding_mode:oaep","-keyopt","rsa_oaep_md:sha256","-keyopt","rsa_mgf1_md:sha256"]
    result=runner.run(command,45,{k:v for k,v in os.environ.items() if k in {"HOME","PATH","LANG","LC_ALL"}})
    require(result.returncode==0 and ciphertext.is_file(),"CMS encryption unavailable/refused")
    raw=ciphertext.read_bytes();require(0<len(raw)<=128*1024*1024+8192,"ciphertext cap")
    require(bytes.fromhex("060b2a864886f70d0109100117") in raw and bytes.fromhex("060960864801650304012e") in raw,"AuthEnvelopedData AES256GCM required")
    require(archive.read_bytes()[:64] not in raw,"plaintext archive leak")
    ciphertext.replace(export/"custody.cms")
    summary={"schema":"fsgg.wizard-recovery-encrypted-custody/1","nativeRunId":int(os.environ["GITHUB_RUN_ID"]),"bindingSha256":digest(canonical(binding)),"recipientSha256":binding["recipientSha256"],"ciphertextSha256":digest(raw),"ciphertextBytes":len(raw),"plaintextArchiveSha256":digest(archive.read_bytes()),"plaintextMemberManifestSha256":digest(canonical(entries)),"memberCount":len(entries),"commandExit":result.returncode,"encryptionProcessCustodyClean":bool(runner.records[-1]["custody"]["leaderReaped"] and not runner.records[-1]["custody"]["remaining"]),"encryptionCommandRecordSha256":digest(canonical(runner.records[-1]))}
    (export/"summary.json").write_bytes(canonical(summary))
    require({p.name for p in export.iterdir()}=={"custody.cms","summary.json"},"ciphertext-only export allowlist")
    return summary

def outer(mode,binding,root,source):
    """Independent observer: only this parent may emit the terminal recovery receipt."""
    validate_binding(binding,mode);recipient(binding);subreaper()
    selected=datetime.fromisoformat(binding["selectedAfter"].replace("Z","+00:00")).timestamp()
    elapsed=time.time()-selected;require(elapsed>=-5,"selection clock future drift")
    budget=Budget(mode,start=time.monotonic()-max(0,elapsed));budget.commands=3
    budget.remaining(25)
    require(not root.exists(),"fresh recovery root required");root.mkdir(mode=0o700,parents=True)
    source=source.resolve();source_before=physical_roster(source,deadline=budget.work)
    sdk_root=Path(shutil.which("dotnet") or "").resolve().parent
    sdk_before=physical_roster(sdk_root,deadline=budget.work)
    openssl=Path(shutil.which("openssl") or "").resolve()
    require(openssl.is_file(),"stock OpenSSL unavailable; no setup fallback")
    crypto_before={str(openssl):digest(openssl.read_bytes())}
    for path in (Path('/etc/ssl/openssl.cnf'),):
        if path.is_file():crypto_before[str(path.resolve())]=digest(path.read_bytes())
    provider=Path('/usr/lib/x86_64-linux-gnu/ossl-modules')
    if provider.is_dir():
        for path in sorted(provider.glob('*.so')):crypto_before[str(path.resolve())]=digest(path.read_bytes())
    binding_path=root/"binding.json";binding_path.write_bytes(canonical(binding));binding_path.chmod(0o600)
    worker_root=root/"worker";worker_root.mkdir(mode=0o700)
    # Worker context requires only the existing ordinary read/write roles. Public
    # SDK children receive a distinct credential-free child_environment.
    env={k:v for k,v in os.environ.items() if k in {"HOME","PATH","DOTNET_ROOT","LANG","LC_ALL","TZ","SSL_CERT_FILE","SSL_CERT_DIR","GH_TOKEN","ORDINARY_LEDGER_TOKEN","GITHUB_RUN_ID","GITHUB_ACTOR","GITHUB_REF","GITHUB_SHA","GITHUB_EVENT_NAME","GITHUB_RUN_ATTEMPT","GITHUB_REPOSITORY"}}
    env["PYTHONDONTWRITEBYTECODE"]="1"
    runner=Runner(budget,root);report={"success":False,"historicalCause":"UNKNOWN","publicWizardQualified":False,"adoptionReceiptEmitted":False,"cryptoPhysicalInputs":crypto_before,"nativeSetupInventory":[{"action":"actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1","stepSeconds":180},{"action":"actions/create-github-app-token@bcd2ba49218906704ab6c1aa796996da409d3eb1","stepSeconds":120}],"commandInventoryPolicy":"two bounded native action units, entrypoint, observer worker, encryption and every supervised SDK/git command; descendants separately enumerated by identity"}
    try:
        result=runner.run([sys.executable,str(Path(__file__).resolve()),"--worker",mode,str(binding_path),str(worker_root),str(source),str(budget.start)],480 if mode=="diagnostic" else 1000,env,cwd=source)
        require(result.returncode==0,"recovery worker failed")
        raw=(worker_root/"worker-report.json").read_bytes();require(len(raw)<=BINARY_CAP,"worker report cap");inner=json.loads(raw)
        require(inner.get("success")is True and inner.get("postSourceMatches")is True and inner.get("postSdkMatches")is True,"worker post/input refusal")
        require(inner.get("sourceRoster")==source_before and inner.get("sdkRoster")==sdk_before,"independent input snapshot disagreement")
        commands=json.loads((worker_root/"commands.json").read_bytes())
        require(len(commands)==inner["commandCount"] and len(commands)+5<=(16 if mode=="diagnostic" else 24),"independent command count")
        for row in commands:
            custody=row.get("custody") or {};require(custody.get("leaderReaped")is True and not custody.get("remaining") and not custody.get("errors"),"independent command custody")
            for identity in custody["members"]:require(not same_process(identity,proc_identity(identity["pid"])),"owned identity remains")
            for name in ("stdout","stderr"):
                saved=row[name];raw=(worker_root/saved["file"]).read_bytes();require(len(raw)==saved["bytes"]<=STREAM_CAP and digest(raw)==saved["sha256"],"independent raw capture proof")
        package=worker_root/"candidate"/f"{PACKAGE}.{VERSION}.nupkg";require(digest(package.read_bytes())==ORIGINAL_PACKAGE,"independent original package proof")
        original=safe_zip(package)
        for install in worker_root.glob("public-install-*"):
            cached=install/"tool/.store/fs.gg.newsddworkspace/0.13.0/fs.gg.newsddworkspace/0.13.0/fs.gg.newsddworkspace.0.13.0.nupkg"
            proof=installed_closure(install/"tool",original,cached)
            require(proof["closure"]=={name.removeprefix("tools/net10.0/any/"):{"sha256":digest(raw),"link":None} for name,raw in original.items() if name.startswith("tools/net10.0/any/")},"independent installed members")
        report.update({k:v for k,v in inner.items() if k not in {"sourceRoster","sdkRoster"}})
    except BaseException as error:report["success"]=False;report["errorKind"]=type(error).__name__
    finally:
        try:
            require(os.environ.get("HOME")==binding["home"],"actual HOME changed")
            report["independentSourceMatches"]=physical_roster(source,deadline=budget.end)==source_before
            report["independentSdkMatches"]=physical_roster(sdk_root,deadline=budget.end)==sdk_before
            require(report["independentSourceMatches"] and report["independentSdkMatches"],"independent post-input failure")
        except BaseException as error:report["success"]=False;report["postErrorKind"]=type(error).__name__
        report["bindingSha256"]=digest(binding_path.read_bytes());report["actualSelectedRunId"]=int(os.environ["GITHUB_RUN_ID"])
        report["elapsedSeconds"]=budget.clock()-budget.start
        report["totalLaunchedCommands"]=report.get("commandCount",0)+budget.commands+1
        (root/"terminal.json").write_bytes(canonical(report))
    try:
        # Also encrypt failed-operation captures. The public export contains only
        # ciphertext and fixed safe hashes; raw files never reach upload-artifact.
        budget.reserve=True
        report["encryptedCustody"]=encrypted_custody(root,binding,runner)
        require(Path(shutil.which("openssl") or "").resolve()==openssl and all(Path(p).is_file() and digest(Path(p).read_bytes())==sha for p,sha in crypto_before.items()),"encryption physical input drift")
        report["cryptoPhysicalInputs"]=crypto_before
        summary_path=root/"export/summary.json";summary=json.loads(summary_path.read_bytes())
        summary["cryptoInputsUnchanged"]=True;summary["cryptoPhysicalInputDigest"]=digest(canonical(crypto_before));summary["totalLaunchedCommands"]=report["totalLaunchedCommands"]
        summary_path.write_bytes(canonical(summary))
    except BaseException as error:
        report["success"]=False;report["custodyErrorKind"]=type(error).__name__
    (root/"terminal.json").write_bytes(canonical(report))
    if report["success"]:
        (root/"recovery-receipt.json").write_bytes(canonical(report))
    print(json.dumps({"scope":report.get("scope",mode),"success":report["success"],"historicalCause":"UNKNOWN","publicWizardQualified":False},sort_keys=True))
    return 0 if report["success"] else 1

def entry(mode,binding_text,root,source):
    require(os.environ.get("GITHUB_EVENT_NAME")=="workflow_dispatch" and os.environ.get("GITHUB_RUN_ATTEMPT")=="1" and os.environ.get("GITHUB_REPOSITORY")==REPO and os.environ.get("GITHUB_REF")=="refs/heads/main" and os.environ.get("GITHUB_ACTOR")=="EHotwagner","native entrypoint role")
    binding=json.loads(binding_text);validate_binding(binding,mode)
    require(binding["heldSource"]==os.environ.get("GITHUB_SHA"),"native held source")
    require(digest(canonical(binding))==os.environ.get("RECOVERY_BINDING_SHA256") and binding["correlation"]==os.environ.get("RECOVERY_CORRELATION"),"root-selected dispatch binding digest/correlation")
    return outer(mode,binding,Path(root),Path(source))

if __name__=="__main__":
    require(len(sys.argv)==7 and sys.argv[1]=="--worker","only supervised worker entry")
    _,_,mode,binding_path,root,source,start=sys.argv
    binding=json.loads(Path(binding_path).read_bytes());validate_binding(binding,mode)
    raise SystemExit(worker(mode,binding,Path(root),Path(source),float(start)))

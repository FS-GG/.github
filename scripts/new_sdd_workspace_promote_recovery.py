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
from new_sdd_workspace_successor_execution import effects, HISTORICAL_013
from new_sdd_workspace_successor_provider import output_signals

PACKAGE, VERSION, TAG = HISTORICAL_013.package, HISTORICAL_013.version, HISTORICAL_013.tag

Path=pathlib.Path
STREAM_CAP=1024*1024
PROC_CAP=8192
MEMBER_CAP=128
JSON_CAP=4*1024*1024
BINARY_CAP=8*1024*1024
ZIP_CAP=32*1024*1024
CUSTODY_CAP=128*1024*1024
READINESS_CAP=CUSTODY_CAP+8192
SMALL_JSON_CAP=64*1024
ANCESTRY_JSON_CAP=1024*1024
INSTALL_TREE_CAP=8*1024*1024
# Closed source roles: 120 GET originals + four CAS response originals;
# 19 inner + worker + encryption command streams, two command records/two
# transport records/five bound reports-inputs, three candidate leaves,
# sixteen SDK first-use leaves, two at-most-64-file install trees, manifest.
CUSTODY_MEMBER_CAP=323
CUSTODY_PATH_CAP=256
PHYSICAL_CUSTODY_CAP=3*CUSTODY_CAP+8192
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
    if not ok:
        error=Refused(reason)
        error.recoveryCode=SAFE_REFUSAL_CODES.get(reason,"unclassified")
        raise error
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


# Closed recovery-only public observability. Never emit free text, raw capture,
# argv, path, HOME, credential, HTTP query or SDK/provider inventory.
SAFE_REFUSAL_CODES={
    'SDK scope selected version unavailable or newer normal selection':'sdk-version-refusal',
    'SDK scope normal net10 runtime unavailable':'sdk-runtime-refusal',
    'SDK scope unsupported runtime policy':'sdk-runtime-policy-refusal',
    'SDK scope external runtime config':'sdk-runtime-policy-refusal',
    'SDK scope selected runtime config':'sdk-runtime-policy-refusal',
    'SDK scope unsupported runtime family':'sdk-runtime-policy-refusal',
    'SDK scope runtime config':'sdk-runtime-policy-refusal',
    'SDK scope runtime framework roster':'sdk-runtime-policy-refusal',
    'SDK scope framework ambiguity':'sdk-layout-refusal',
    'SDK scope ambiguous version layout':'sdk-layout-refusal',
    'SDK scope ambiguous shared layout':'sdk-layout-refusal',
    'SDK scope required directory absent':'sdk-layout-refusal',
    'SDK scope canonical host/fxr absent':'sdk-layout-refusal',
    'SDK scope shared root':'sdk-layout-refusal',
    'SDK scope resolver override':'sdk-resolution-refusal',
    'SDK scope ordinary root disagreement':'sdk-resolution-refusal',
    'SDK scope normal PATH absent':'sdk-resolution-refusal',
    'SDK scope canonical PATH host disagreement':'sdk-resolution-refusal',
    'SDK scope canonical host absent':'sdk-resolution-refusal',
    'SDK scope held policy changed':'sdk-resolution-refusal',
    'SDK scope global config':'sdk-runtime-policy-refusal',
    'SDK scope config hash disagreement':'sdk-input-drift',
    'SDK scope source config disagreement':'sdk-input-drift',
    'SDK scope selection drift':'sdk-input-drift',
    'SDK scope CLI metadata escape':'physical-scope-refusal',

    "operation deadline":"operation-deadline",
    "physical roster deadline":"physical-roster-deadline",
    "physical roster cap":"physical-member-cap",
    "physical member byte cap":"physical-member-byte-cap",
    "physical source escape":"physical-scope-refusal",
    "outer full roster report cap":"outer-report-cap",
    "existing SDK absent":"sdk-unavailable",
    "exact held SDK unavailable; no fallback":"sdk-version-refusal",
    "stock OpenSSL unavailable; no setup fallback":"crypto-unavailable",
    "CMS encryption unavailable/refused":"crypto-command-refusal",
    "ciphertext cap":"crypto-size-refusal",
    "AuthEnvelopedData AES256GCM required":"crypto-envelope-refusal",
    "recovery worker failed":"worker-refusal",
    "whole/work deadline":"operation-deadline",
    "native HTTP read/write refused":"native-http-refusal",
}
SAFE_PHASES=frozenset({"entry","outer-source-roster","outer-sdk-resolve",
    "outer-sdk-roster","outer-report-reservation","outer-crypto-snapshot",
    "outer-worker-start","outer-source-post","outer-sdk-post",
    "outer-terminal-reservation","outer-encrypted-custody","outer-final-report"})
_current_phase="entry"
def recovery_phase(phase):
    global _current_phase
    require(phase in SAFE_PHASES,"closed recovery phase")
    _current_phase=phase
    print("Wizard recovery phase: "+canonical({"schema":"fsgg.wizard-recovery-phase/1","phase":phase}).decode(),flush=True)
def recovery_refusal(error):
    code=getattr(error,"recoveryCode","unclassified")
    if code not in set(SAFE_REFUSAL_CODES.values()):code="unclassified"
    kind=type(error).__name__
    if kind not in {"Refused","OSError","ValueError","KeyError","CalledProcessError"}:kind="unclassified"
    print("Wizard recovery refusal: "+canonical({"schema":"fsgg.wizard-recovery-refusal/1","phase":_current_phase if _current_phase in SAFE_PHASES else "entry","code":code,"exceptionKind":kind}).decode(),file=sys.stderr,flush=True)
def http_failure_projection(error):
    """Finite private report fallback when original transport custody fails."""
    status=getattr(error,"httpStatus",None)
    if type(status) is not int or not 100<=status<=599:return None
    kinds={"Refused","OSError","RuntimeError","ValueError","FileExistsError","TimeoutError","unclassified"}
    value={"status":status}
    for field in ("errorCaptureFailureKind","errorCloseFailureKind","transportCaptureFailureKind"):
        kind=getattr(error,field,None)
        if kind is not None:value[field]=kind if kind in kinds else "unclassified"
    return value
def existing_sdk_root():
    selected=shutil.which("dotnet")
    require(bool(selected),"existing SDK absent")
    executable=Path(selected).resolve()
    require(executable.is_file(),"existing SDK absent")
    return executable.parent

class CustodyStorage:
    """Closed authenticated large role and one shared finite physical envelope."""
    def __init__(self,root):self.root=Path(root);self.roles={};self.armed=False;self.phase="plaintext";self.serialized={};self.report_reservation=3*BINARY_CAP;self.worker_report_cap=BINARY_CAP
    def register_readiness(self,path,binding):
        path=Path(path);relative=path.relative_to(self.root).as_posix()
        require(re.fullmatch(r"(?:worker/)?response-github-[0-9]+\.raw",relative),"literal readiness response role")
        require(not self.roles and not path.is_symlink(),"one physical readiness role")
        records_path=path.parent/"transport-github.json"
        require(records_path.is_file(),"readiness authenticated transport record absent")
        rows=json.loads(records_path.read_bytes());index=int(path.stem.rsplit('-',1)[1])
        require(index<len(rows),"readiness original response identity")
        row=rows[index]
        require(row.get("method")=="GET" and row.get("origin")=="api.github.com" and row.get("status")==200 and row.get("path")==f"/repos/{REPO}/actions/artifacts/{binding['readinessArtifactId']}/zip" and row.get("sha256")==binding["readinessArchiveSha256"] and row.get("bytes")==path.stat().st_size,"readiness original authenticated route")
        raw=path.read_bytes();require(len(raw)<=READINESS_CAP and digest(raw)==binding["readinessArchiveSha256"],"authenticated readiness original")
        members=safe_zip(path,{"custody.cms","summary.json"},custody=True);summary=json.loads(members["summary.json"])
        require(digest(members["custody.cms"])==binding["readinessCiphertextSha256"] and summary.get("ciphertextSha256")==binding["readinessCiphertextSha256"] and summary.get("bindingSha256")==binding["readinessBindingSha256"] and summary.get("nativeRunId")==binding["readinessRunId"] and summary.get("schema")=="fsgg.wizard-recovery-encrypted-custody/1" and summary.get("commandExit")==0,"authenticated readiness member join")
        self.roles[relative]={"role":"authenticated-native-diagnostic-archive","bytes":len(raw),"sha256":digest(raw)}
    def inventory(self):
        total=0;count=0;overhead=0;serialized_bytes=0
        for path in self.root.rglob("*"):
            if path.is_dir():continue
            relative=path.relative_to(self.root).as_posix()
            require(not path.is_symlink() and path.resolve().is_relative_to(self.root.resolve()),"custody physical scope")
            require(len(relative.encode())<=CUSTODY_PATH_CAP,"custody named-path bound")
            if relative in self.serialized:
                typed=self.serialized[relative];size=path.stat().st_size
                require(size<=typed["cap"],"serialized custody cap")
                if typed.get("sha256"):require(size==typed["bytes"] and digest(path.read_bytes())==typed["sha256"],"serialized archive drift")
                serialized_bytes+=size;continue
            size=path.stat().st_size;role=self.roles.get(relative)
            require(size<=(READINESS_CAP if role else BINARY_CAP),"ordinary custody member remains 8MiB")
            if role:require(size==role["bytes"] and digest(path.read_bytes())==role["sha256"],"registered original drift")
            total+=size;count+=1;overhead+=512+3*len(relative.encode())
        require(count<=CUSTODY_MEMBER_CAP,"finite source custody member count")
        require(total+serialized_bytes<=PHYSICAL_CUSTODY_CAP,"finite transient physical custody cap")
        return total,overhead,count
    def check(self,pending=0):
        total,overhead,count=self.inventory()
        require(total+overhead+pending+65536<=CUSTODY_CAP,"streaming custody total/archive cap")
        for install in self.root.glob("**/public-install-*"):
            if install.is_dir():
                files=[p for p in install.rglob('*') if p.is_file()]
                require(len(files)<=64 and sum(p.stat().st_size for p in files)<=INSTALL_TREE_CAP,"owned install tree cap/count")
        for checks in self.root.glob("**/checks"):
            if checks.is_dir():require(sum(1 for p in checks.rglob('*') if p.is_file())<=16,"SDK first-use member count")
        return total,overhead,count
    def bind_report_rosters(self,source,sdk):
        roster_bytes=len(canonical(source))+len(canonical(sdk))
        self.worker_report_cap=2*roster_bytes+256*1024
        self.outer_report_cap=roster_bytes+256*1024
        require(max(self.worker_report_cap,self.outer_report_cap)<=BINARY_CAP,"full retained roster report schema exceeds ordinary cap")
        self.report_reservation=self.worker_report_cap+2*self.outer_report_cap
    def begin_archive(self):
        require(self.phase=="plaintext" and not (self.root/"raw-custody.zip").exists(),"fresh archive phase")
        self.check();self.phase="archive"
        self.serialized["raw-custody.zip"]={"cap":CUSTODY_CAP}
    def seal_archive(self):
        require(self.phase=="archive","archive phase")
        path=self.root/"raw-custody.zip";raw=path.read_bytes()
        require(len(raw)<=CUSTODY_CAP,"literal original archive cap")
        self.serialized["raw-custody.zip"].update({"bytes":len(raw),"sha256":digest(raw)})
        self.check();self.phase="encrypt"
        require(not (self.root/"custody.pending.cms").exists(),"fresh pending ciphertext")
        self.serialized["custody.pending.cms"]={"cap":READINESS_CAP}
    def exported(self):
        require(self.phase=="encrypt","encryption phase")
        self.serialized.pop("custody.pending.cms")
        raw=(self.root/"export/custody.cms").read_bytes()
        self.serialized["export/custody.cms"]={"cap":READINESS_CAP,"bytes":len(raw),"sha256":digest(raw)}
        self.phase="export";self.check()
    def pre_effect(self,budget,binary_bodies,second_gate):
        total,overhead,count=self.check()
        # Concrete remaining roles: at most four bounded workflow pages;
        # remaining small JSON responses including four CAS responses; one
        # repeated five-body gate; remaining inner command streams; two outer
        # command streams; worker/outer reports, command/transport records and
        # fresh second install tree. Extra role calls refuse at streaming time.
        # Both initial and immediate pre-PATCH ancestry originals already appear
        # in this full physical inventory. No comparison may follow arming;
        # their larger cap cannot consume the smaller future JSON reservation.
        future_binary=sum(binary_bodies.values()) if second_gate else 0
        require(not second_gate or len(binary_bodies)==5,"exact matched gate reservation")
        future_json=(max(0,120-budget.reads)+4)*SMALL_JSON_CAP+4*1024*1024
        future_streams=max(0,19-budget.commands)*2*STREAM_CAP+4*STREAM_CAP
        reports=self.report_reservation+4*1024*1024+2*1024*1024
        future_install=INSTALL_TREE_CAP if second_gate else 0
        structure=(CUSTODY_MEMBER_CAP-count)*(512+3*CUSTODY_PATH_CAP)+65536 # finite remaining source roles, names, manifest and stored-ZIP headers
        projected=total+overhead+future_binary+future_json+future_streams+reports+future_install+structure
        require(projected<=CUSTODY_CAP,"projected required custody exceeds 128MiB before effect")
        self.armed=True
        return {"currentBytes":total,"zipOverhead":overhead,"futureBinary":future_binary,"futureJson":future_json,"futureStreams":future_streams,"futureReports":reports,"futureInstall":future_install,"structure":structure,"projectedBytes":projected,"limit":CUSTODY_CAP}

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
        require("/compare" not in urllib.parse.unquote(urllib.parse.urlsplit(req.full_url).path),"ancestry compare redirects forbidden")
        require(req.get_method()=="GET" and parsed.scheme=="https" and not parsed.username and not parsed.password,"redirect trust")
        host=parsed.hostname or ""
        require(host in {"api.github.com","release-assets.githubusercontent.com","objects.githubusercontent.com","api.nuget.org"} or host.endswith(".blob.core.windows.net"),"redirect host")
        self.budget.read()
        result=super().redirect_request(req,fp,code,msg,headers,newurl)
        if parsed.netloc!=urllib.parse.urlsplit(req.full_url).netloc:result.remove_header("Authorization")
        return result

class FiniteAPI:
    def __init__(self,token,budget,root,authority=False,held_source=None):
        require(bool(token),"native read credential absent");self.token=token;self.budget=budget;self.root=root;self.authority=authority;self.records=[]
        require(held_source is None or re.fullmatch(r"[0-9a-f]{40}",held_source),"bound ancestry held source")
        self.held_source=held_source
        self.expectedSettlement=None;self.cas_objects={};self.immutable={};self.storage=getattr(budget,"storage",None);self.matched_bodies={};self.future_pages=0;self.future_binaries=0
        self.opener=urllib.request.build_opener(Redirect(budget))
    def capture_http_error(self,error,target,row,cap,custody):
        """One original bounded error response; never a successful API value."""
        row.update({"bodyComplete":False,"bodyBytesRetained":0,"bodyOverflow":False,
                    "responseHeaders":{},"headersComplete":True})
        allowed=("content-type","content-length","x-github-request-id","x-accepted-github-permissions",
                 "x-ratelimit-limit","x-ratelimit-remaining","x-ratelimit-reset","x-ratelimit-resource",
                 "retry-after","x-github-api-version-selected")
        created=False
        try:
            header_bytes=0
            for name in allowed:
                value=error.headers.get(name) if error.headers is not None else None
                if value is None:continue
                prefix=value[:513].encode("utf-8");complete=len(value)<=513 and len(prefix)<=512
                retained=prefix[:512].decode("utf-8",errors="ignore")
                header_bytes+=len(name.encode())+len(retained.encode())
                require(header_bytes<=8192,"HTTP error header metadata cap")
                row["responseHeaders"][name]={"value":retained,"complete":complete}
                row["headersComplete"]=row["headersComplete"] and complete
            fd=os.open(target,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
            created=True
            with os.fdopen(fd,"wb") as stream:
                while True:
                    self.budget.remaining(25)
                    data=error.read(min(65536,cap-row["bodyBytesRetained"]+1))
                    if not data:
                        row["bodyComplete"]=True
                        length=row["responseHeaders"].get("content-length")
                        if length is not None:
                            valid=length["complete"] and re.fullmatch(r"[0-9]+",length["value"]) is not None
                            row["contentLengthMatches"]=bool(valid and int(length["value"])==row["bytes"])
                            row["bodyComplete"]=row["contentLengthMatches"]
                        break
                    row["bytes"]+=len(data)
                    if self.storage and not custody:self.storage.check(len(data))
                    retained=data[:cap-row["bodyBytesRetained"]]
                    stream.write(retained);row["bodyBytesRetained"]+=len(retained)
                    if len(retained)!=len(data):
                        row["bodyOverflow"]=True;break
            raw=target.read_bytes();row["retainedBodySha256"]=digest(raw)
            if row["bodyComplete"]:row["sha256"]=row["retainedBodySha256"]
        except BaseException as secondary:
            row["bodyComplete"]=False
            row["errorCaptureFailureKind"]=type(secondary).__name__
            if created and target.is_file():
                try:
                    raw=target.read_bytes();row["bodyBytesRetained"]=len(raw);row["retainedBodySha256"]=digest(raw)
                except BaseException:pass
        finally:
            try:error.close()
            except BaseException as secondary:row["errorCloseFailureKind"]=type(secondary).__name__
    def request(self,url,method="GET",body=None,binary=False,headers=None,custody=False):
        parsed=urllib.parse.urlsplit(url);require(parsed.scheme=="https" and parsed.hostname in {"api.github.com","nuget.pkg.github.com","api.nuget.org"} and not parsed.username and not parsed.password,"request origin")
        require(self.budget.mode!="diagnostic" or (method=="GET" and body is None),"diagnostic direct GET-only transport")
        timeout=self.budget.read() if method=="GET" else self.budget.remaining(25)
        rawbody=None if body is None else canonical(body)
        auth={"Authorization":"Bearer "+self.token,"User-Agent":"fsgg-wizard-recovery","Accept":"application/vnd.github+json","X-GitHub-Api-Version":"2022-11-28"} if parsed.hostname=="api.github.com" else {"User-Agent":"fsgg-wizard-recovery"}
        request=urllib.request.Request(url,data=rawbody,method=method,headers={**auth,**(headers or {}),**({"Content-Type":"application/json"} if body is not None else {})})
        cap=(READINESS_CAP if custody else BINARY_CAP) if binary else (1024*1024 if "/actions/workflows/" in parsed.path else 256*1024 if parsed.path.endswith("/commits") else SMALL_JSON_CAP)
        ancestry="/compare" in urllib.parse.unquote(parsed.path)
        if ancestry:
            require(not self.authority and self.held_source is not None and method=="GET" and body is None and headers is None and not binary and not custody and url==ancestry_url(self.held_source),"exact bound paged ancestry response role")
            require(not (self.storage and self.storage.armed),"ancestry reads precede effect reservation")
            cap=ANCESTRY_JSON_CAP
        if self.storage and self.storage.armed:
            if binary:
                require(url in self.matched_bodies and self.future_binaries<5,"closed future binary role")
                self.future_binaries+=1;cap=self.matched_bodies[url]
            elif "/actions/workflows/" in parsed.path:
                self.future_pages+=1;require(self.future_pages<=4,"closed future workflow pages")
        require(not custody or (method=="GET" and binary and parsed.hostname=="api.github.com" and re.fullmatch(r"/repos/FS-GG/\.github/actions/artifacts/[0-9]+/zip",parsed.path)),"encrypted readiness archive route")
        index=len(self.records);target=self.root/f"response-{('authority' if self.authority else 'github')}-{index}.raw"
        row={"method":method,"origin":parsed.hostname,"path":parsed.path,"query":"withheld","status":None,"bytes":0,"cap":cap,"role":"bound-original-to-held-ancestry" if ancestry else "ordinary"}
        self.records.append(row);primary=None
        try:
            with self.opener.open(request,timeout=timeout) as response,target.open("xb") as stream:
                row["status"]=response.status
                while True:
                    self.budget.remaining(25)
                    data=response.read(min(65536,cap-row["bytes"]+1))
                    if not data:break
                    row["bytes"]+=len(data);require(row["bytes"]<=cap,"response byte cap")
                    if self.storage and not custody:self.storage.check(len(data))
                    stream.write(data)
            row["sha256"]=digest(target.read_bytes())
            if ancestry:require(row["status"]==200,"paged ancestry HTTP status")
            return target if binary else json.loads(target.read_bytes())
        except urllib.error.HTTPError as error:
            row["status"]=error.code
            primary=Refused("native HTTP read/write refused");primary.httpStatus=error.code
            self.capture_http_error(error,target,row,cap,custody)
            for field in ("errorCaptureFailureKind","errorCloseFailureKind"):
                if field in row:setattr(primary,field,row[field])
            raise primary from error
        finally:
            try:
                transport=canonical(self.records);require(len(transport)<=1024*1024,"transport metadata cap")
                if self.storage and not custody:self.storage.check(len(transport))
                (self.root/f"transport-{('authority' if self.authority else 'github')}.json").write_bytes(transport)
            except BaseException as secondary:
                if primary is None:raise
                row["transportCaptureFailureKind"]=type(secondary).__name__
                primary.transportCaptureFailureKind=type(secondary).__name__
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


def capture(proc, custody, deadline,storage=None):
    buffers = {'stdout': bytearray(), 'stderr': bytearray()}; selector = selectors.DefaultSelector()
    for name in buffers:
        stream = getattr(proc, name); os.set_blocking(stream.fileno(), False); selector.register(stream, selectors.EVENT_READ, name)
    try:
        while selector.get_map() or custody.exited() is None:
            require(time.monotonic() < deadline, 'command-deadline')
            custody.observe()
            if storage:storage.check(sum(len(b) for b in buffers.values()))
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
            result=capture(proc,child,min(self.budget.end if self.budget.reserve else self.budget.work,self.budget.clock()+timeout),getattr(self.budget,"storage",None))
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
                require(len(raw)<=STREAM_CAP,"raw capture cap")
                if getattr(self.budget,"storage",None):self.budget.storage.check(len(raw))
                target.write_bytes(raw)
                row[name]={"file":target.name,"bytes":len(raw),"sha256":digest(raw),"safe":output_signals(raw)}
            records=canonical(self.records);require(len(records)<=2*1024*1024,"command metadata cap")
            if getattr(self.budget,"storage",None):self.budget.storage.check(len(records))
            (self.root/"commands.json").write_bytes(records)
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

# Root-selected architecture amendment: exact selected closure, no hidden fallback.
SDK_SCOPE_POLICY={"schema":"fsgg.wizard-sdk-scope-policy/1","sdkVersion":"10.0.401","runtimeFamily":"10.0","hostFxr":"whole-canonical-tree","cliMetadata":"whole-applicable-trees","snapshotMembers":50000,"snapshotMemberBytes":256*1024*1024}
ORIGINAL_RUNTIME_CONFIG_MEMBER="tools/net10.0/any/new-sdd-workspace.runtimeconfig.json"
ORIGINAL_RUNTIME_CONFIG_SHA256="e4b3a5d18f436095616519e1c99f954e4b0a8952d2dc6681915a69bbae0c5f30"
ORIGINAL_RUNTIME_OPTIONS={"tfm":"net10.0","rollForward":"Major","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"},"configProperties":{"System.Globalization.Invariant":True,"System.Globalization.PredefinedCulturesOnly":True,"System.Reflection.Metadata.MetadataUpdater.IsSupported":False,"System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization":False}}
SDK_FORBIDDEN_ENV={"DOTNET_ADDITIONAL_DEPS","DOTNET_SHARED_STORE","DOTNET_MULTILEVEL_LOOKUP","DOTNET_STARTUP_HOOKS","MSBuildSDKsPath"}

def sdk_input_environment():
    # Same resolver inputs as the closed SDK child environment. Other ambient
    # settings cannot affect child resolution because they are never forwarded.
    return {k:v for k,v in os.environ.items() if k in {"HOME","PATH","DOTNET_ROOT","LANG","LC_ALL","TZ","TERM","SSL_CERT_FILE","SSL_CERT_DIR"}}

def sdk_scope_versions(directory):
    require(directory.is_dir() and not directory.is_symlink(),"SDK scope required directory absent")
    names=[]
    for index,path in enumerate(directory.iterdir()):
        require(index<64 and path.is_dir() and not path.is_symlink() and re.fullmatch(r"[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,6}",path.name),"SDK scope ambiguous version layout")
        names.append(path.name)
    return sorted(names,key=lambda value:tuple(map(int,value.split("."))))

def sdk_scope_requirements(config):
    require(isinstance(config,dict) and isinstance(config.get("runtimeOptions"),dict),"SDK scope runtime config")
    options=config["runtimeOptions"]
    require(set(options)<={"tfm","framework","frameworks","rollForward","applyPatches","configProperties"} and options.get("tfm","net10.0")=="net10.0","SDK scope external runtime config")
    properties=options.get("configProperties",{});require(isinstance(properties,dict) and all(isinstance(k,str) and k.startswith("System.") and type(v) in {bool,int} for k,v in properties.items()),"SDK scope external runtime config")
    require(options.get("rollForward","Minor") in {"Minor","LatestPatch","Major"} and options.get("applyPatches",True)is True,"SDK scope unsupported runtime policy")
    require(not ("framework" in options and "frameworks" in options),"SDK scope framework ambiguity")
    rows=options["frameworks"] if "frameworks" in options else [options["framework"]] if "framework" in options else []
    require(isinstance(rows,list) and 1<=len(rows)<=2 and all(isinstance(row,dict) for row in rows),"SDK scope runtime framework roster")
    require(len({row.get("name") for row in rows})==len(rows),"SDK scope framework ambiguity")
    for row in rows:require(set(row)=={"name","version"} and row["name"] in {"Microsoft.NETCore.App","Microsoft.AspNetCore.App"} and isinstance(row["version"],str) and re.fullmatch(r"10\.0\.[0-9]{1,6}",row["version"]),"SDK scope unsupported runtime family")
    return rows

def selected_sdk_scope(root,source,environment,deadline):
    root=Path(root).resolve();require(time.monotonic()<deadline,"physical roster deadline")
    require(not any(k in SDK_FORBIDDEN_ENV or k.startswith(("DOTNET_ROLL_FORWARD","DOTNET_MSBUILD_SDK_RESOLVER_","DOTNET_ROOT_")) for k in environment),"SDK scope resolver override")
    require("DOTNET_ROOT" not in environment or Path(environment["DOTNET_ROOT"]).resolve()==root,"SDK scope ordinary root disagreement")
    require(isinstance(environment.get("PATH"),str) and 0<len(environment["PATH"])<=8192,"SDK scope normal PATH absent")
    chosen_host=shutil.which("dotnet",path=environment["PATH"]);require(bool(chosen_host) and Path(chosen_host).resolve()==root/"dotnet","SDK scope canonical PATH host disagreement")
    host=root/"dotnet";require((root/"host").is_dir() and not (root/"host").is_symlink(),"SDK scope canonical host/fxr absent");require(host.is_file() and not host.is_symlink(),"SDK scope canonical host absent")
    global_path=Path(source)/"global.json";require(global_path.is_file() and global_path.stat().st_size<=SMALL_JSON_CAP,"SDK scope global config")
    global_raw=global_path.read_bytes();global_sdk=json.loads(global_raw).get("sdk",{});require(isinstance(global_sdk,dict),"SDK scope global config")
    require(global_sdk.get("version")==SDK_SCOPE_POLICY["sdkVersion"] and global_sdk.get("rollForward")=="latestFeature" and "paths" not in global_sdk,"SDK scope held policy changed")
    installed=sdk_scope_versions(root/"sdk");compatible=[name for name in installed if name.startswith("10.0.")]
    require(compatible and compatible[-1]==SDK_SCOPE_POLICY["sdkVersion"],"SDK scope selected version unavailable or newer normal selection")
    fxr=sdk_scope_versions(root/"host"/"fxr");require(fxr,"SDK scope canonical host/fxr absent")
    sdk_config_path=root/"sdk"/SDK_SCOPE_POLICY["sdkVersion"]/"dotnet.runtimeconfig.json"
    require(sdk_config_path.is_file() and sdk_config_path.resolve().is_relative_to(root/"sdk"/SDK_SCOPE_POLICY["sdkVersion"]) and sdk_config_path.stat().st_size<=SMALL_JSON_CAP,"SDK scope selected runtime config")
    require(not sdk_config_path.with_name("dotnet.runtimeconfig.dev.json").exists(),"SDK scope external runtime config")
    sdk_raw=sdk_config_path.read_bytes();sdk_config=json.loads(sdk_raw)
    requirements=sdk_scope_requirements(sdk_config)+sdk_scope_requirements({"runtimeOptions":ORIGINAL_RUNTIME_OPTIONS})
    frameworks={row["name"] for row in requirements};runtime_layout={};selected={}
    scope=["dotnet","sdk/"+SDK_SCOPE_POLICY["sdkVersion"],"host/fxr"]
    shared_root=root/"shared";require(shared_root.is_dir() and not shared_root.is_symlink(),"SDK scope shared root")
    shared_names=[]
    for index,path in enumerate(shared_root.iterdir()):
        require(index<64 and path.is_dir() and not path.is_symlink() and re.fullmatch(r"[A-Za-z][A-Za-z0-9.]{0,63}",path.name),"SDK scope ambiguous shared layout");shared_names.append(path.name)
    for framework in sorted(frameworks):
        versions=sdk_scope_versions(shared_root/framework);runtime_layout[framework]=versions
        matching=[version for version in versions if version.startswith("10.0.")]
        minimum=max(tuple(map(int,row["version"].split("."))) for row in requirements if row["name"]==framework)
        require(matching and tuple(map(int,matching[-1].split(".")))>=minimum,"SDK scope normal net10 runtime unavailable")
        selected[framework]=matching[-1];scope.append("shared/"+framework+"/"+matching[-1])
    metadata={}
    for relative in ("sdk-manifests","metadata/workloads"):
        target=root/relative;metadata[relative]=target.exists()
        if target.exists():require(target.is_dir() and not target.is_symlink(),"SDK scope CLI metadata escape");scope.append(relative)
    require(time.monotonic()<deadline,"physical roster deadline")
    return {"schema":"fsgg.wizard-sdk-scope-selection/1","policy":SDK_SCOPE_POLICY,"globalConfigSha256":digest(global_raw),"sdkRuntimeConfigSha256":digest(sdk_raw),"originalWizardRuntimeConfigSha256":ORIGINAL_RUNTIME_CONFIG_SHA256,"originalWizardRollForward":"Major","installedSDKDirectoryNames":installed,"hostFxrDirectoryNames":fxr,"sharedFrameworkDirectoryNames":sorted(shared_names),"runtimeDirectoryNames":runtime_layout,"selectedRuntimeClosures":selected,"cliMetadataLayout":metadata,"scope":scope}

def selected_sdk_roster(root,selection,deadline,progress):
    root=Path(root).resolve();members={}
    for relative in selection["scope"]:
        target=root/relative;require(target.resolve().is_relative_to(root) and not target.is_symlink(),"physical source escape")
        if target.is_dir():
            rows=physical_roster(target,deadline=deadline,progress=progress)
            for name,row in rows.items():
                key=relative+"/"+name;require(key not in members and len(members)<50000,"physical roster cap");members[key]=row
        else:
            require(relative=="dotnet" and target.is_file() and target.stat().st_size<=256*1024*1024,"physical member byte cap")
            hashed=hashlib.sha256();progress("member",target.stat().st_size)
            with target.open("rb") as stream:
                for chunk in iter(lambda:stream.read(65536),b""):
                    require(time.monotonic()<deadline,"physical roster deadline");hashed.update(chunk);progress("hashed",len(chunk))
            progress("complete",0);members[relative]={"sha256":hashed.hexdigest(),"link":None}
    return {"schema":"fsgg.wizard-selected-sdk-snapshot/1","selection":selection,"members":members}

# Closed SDK progress; scope is the affirmative selected SDK architecture amendment.
class SDKSnapshotMetrics:
    MAX_ROWS=33
    def __init__(self,root,source,phase):
        require(phase in {"outer-sdk-roster","worker-sdk-roster","worker-sdk-post","outer-sdk-post"},"closed SDK snapshot phase")
        self.root=Path(root).resolve();self.phase=phase;self.started=time.monotonic();self.last=self.started;self.rows=0;self.members=0;self.member_bytes=0;self.hashed_bytes=0;self.completed=0
        version=None
        try:
            raw=(Path(source)/"global.json").read_bytes()
            if len(raw)<=SMALL_JSON_CAP:
                selected=json.loads(raw).get("sdk",{}).get("version")
                if selected=="10.0.401":version=selected
        except (OSError,ValueError,TypeError):pass
        self.version=version;self.names=[];self.directory_complete=True;self.unrecognized=False;self.dotnet=None
        selected_host=shutil.which("dotnet")
        if selected_host:
            resolved_host=Path(selected_host).resolve()
            if resolved_host.parent==self.root:self.dotnet=resolved_host
        try:
            for index,path in enumerate((self.root/"sdk").iterdir()):
                if index>=64:self.directory_complete=False;break
                if not path.is_dir() or re.fullmatch(r"[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,6}(?:-[A-Za-z0-9][A-Za-z0-9.-]{0,24})?",path.name) is None:self.unrecognized=True;continue
                self.names.append(path.name)
        except OSError:self.directory_complete=False
        self.exists=(self.root/"sdk"/version).is_dir() if version else None
    @staticmethod
    def public_path(path):
        text=str(path)
        pattern=r"/(?:usr/share/dotnet|usr/lib/dotnet|opt/dotnet|opt/hostedtoolcache/dotnet(?:/[0-9]+\.[0-9]+\.[0-9]+/x64)?|home/runner/\.dotnet)(?:/dotnet)?"
        return text if re.fullmatch(pattern,text) else None
    def emit(self,status="progress",code=None):
        require(status in {"progress","complete","refused"} and (code is None or code in set(SAFE_REFUSAL_CODES.values())|{"unclassified"}),"closed SDK metric status")
        elapsed=max(0,int((time.monotonic()-self.started)*1000));require(elapsed<=1200000 and self.rows<self.MAX_ROWS,"finite SDK metrics")
        row={"schema":"fsgg.wizard-sdk-snapshot-progress/1","phase":self.phase,"status":status,"refusalCode":code,"dotnetResolvedPath":self.public_path(self.dotnet) if self.dotnet else None,"sdkParentResolvedPath":self.public_path(self.root),"pathDisclosure":"stock-public" if self.public_path(self.root) else "withheld-nonstock","installedSDKDirectoryNames":sorted(self.names),"completeSDKDirectoryEnumeration":self.directory_complete,"unrecognizedSDKDirectoryPresent":self.unrecognized,"selectedGlobalSDKVersion":self.version,"selectedSDKDirectoryExists":self.exists,"observedMembers":self.members,"observedMemberBytes":self.member_bytes,"hashedBytes":self.hashed_bytes,"completedMembers":self.completed,"elapsedMilliseconds":elapsed}
        raw=canonical(row);require(len(raw)<=4096,"SDK metric row cap")
        print("Wizard SDK snapshot: "+raw.decode(),flush=True);self.rows+=1;self.last=time.monotonic()
    def progress(self,event,amount):
        if event=="member":self.members+=1;self.member_bytes+=amount
        elif event=="hashed":self.hashed_bytes+=amount
        elif event=="complete":self.completed+=1
        else:require(False,"closed SDK metric event")
        require(self.members<=50000 and self.completed<=self.members and self.member_bytes<=50000*256*1024*1024 and self.hashed_bytes<=self.member_bytes,"finite SDK metric counters")
        if self.rows<self.MAX_ROWS-1 and time.monotonic()-self.last>=15:self.emit()

def sdk_snapshot(root,source,phase,deadline,environment=None):
    metrics=SDKSnapshotMetrics(root,source,phase);metrics.emit()
    try:
        selection=selected_sdk_scope(root,source,sdk_input_environment() if environment is None else environment,deadline)
        rows=selected_sdk_roster(root,selection,deadline,metrics.progress)
        require(rows["members"]["sdk/"+SDK_SCOPE_POLICY["sdkVersion"]+"/dotnet.runtimeconfig.json"]["sha256"]==selection["sdkRuntimeConfigSha256"],"SDK scope config hash disagreement")
        require(selected_sdk_scope(root,source,sdk_input_environment() if environment is None else environment,deadline)==selection,"SDK scope selection drift")
    except BaseException as error:
        metrics.emit("refused",getattr(error,"recoveryCode","unclassified") if getattr(error,"recoveryCode","unclassified") in set(SAFE_REFUSAL_CODES.values()) else "unclassified")
        raise
    metrics.emit("complete");return rows

def physical_roster(root,cap=50000,deadline=None,progress=None):
    root=Path(root).resolve();rows={}
    for path in sorted(root.rglob("*")):
        relative=path.relative_to(root).as_posix()
        if relative==".git" or relative.startswith(".git/"):continue
        if path.is_file() or path.is_symlink():
            require(len(rows)<cap,"physical roster cap")
            require(path.resolve().is_relative_to(root),"physical source escape")
            require(path.stat().st_size<=256*1024*1024,"physical member byte cap")
            hashed=hashlib.sha256()
            if progress:progress("member",path.stat().st_size)
            with path.open("rb") as stream:
                for chunk in iter(lambda:stream.read(65536),b""):
                    if deadline is not None:require(time.monotonic()<deadline,"physical roster deadline")
                    hashed.update(chunk)
                    if progress:progress("hashed",len(chunk))
            if progress:progress("complete",0)
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

def ancestry_url(held_source,paged=True):
    require(isinstance(held_source,str) and re.fullmatch(r"[0-9a-f]{40}",held_source),"exact ancestry held source")
    return f"https://api.github.com/repos/{REPO}/compare/{CANDIDATE_SOURCE}...{held_source}"+("?per_page=1&page=2" if paged else "")

def validate_ancestry(response,held_source):
    """Native descendant assertion from one complete, exact second page."""
    require(isinstance(response,dict) and response.get("url")==ancestry_url(held_source,False),"native ancestry subject")
    for field in ("base_commit","merge_base_commit"):
        require(isinstance(response.get(field),dict) and response[field].get("sha")==CANDIDATE_SOURCE,"native ancestry original base")
    require(response.get("status")=="ahead" and type(response.get("behind_by")) is int and response["behind_by"]==0,"native ancestry ahead only")
    ahead=response.get("ahead_by");total=response.get("total_commits")
    require(type(ahead) is int and type(total) is int and ahead==total and total>=2,"paged ancestry requires at least two commits")
    commits=response.get("commits")
    require(isinstance(commits,list) and len(commits)==1 and isinstance(commits[0],dict) and isinstance(commits[0].get("sha"),str) and re.fullmatch(r"[0-9a-f]{40}",commits[0]["sha"]) and "files" not in response,"exact second-page ancestry shape")
    return response

def original_runs(api,binding):
    if hasattr(api,"held_source"):require(api.held_source==binding["heldSource"],"original runs held source drift")
    original=api.get(f"repos/{REPO}/actions/runs/{FAILED_RUN}")
    require(original.get("id")==FAILED_RUN and original.get("head_sha")==CANDIDATE_SOURCE and original.get("run_attempt")==1 and original.get("status")=="completed" and original.get("conclusion")=="failure" and original.get("path")==WORKFLOW and original.get("actor",{}).get("login")=="EHotwagner" and original.get("repository",{}).get("id")==1269292704 and original.get("event")=="workflow_dispatch" and original.get("head_branch")=="main","original failed publisher drift")
    for ident in binding["priorRunIds"]:
        row=api.get(f"repos/{REPO}/actions/runs/{ident}")
        require(row.get("id")==ident and row.get("status")=="completed" and row.get("path")==WORKFLOW and row.get("repository",{}).get("id")==1269292704 and row.get("actor",{}).get("login")=="EHotwagner" and row.get("run_attempt")==1,"intervening writer not terminal")
    path=ancestry_url(binding["heldSource"]).removeprefix("https://api.github.com/")
    validate_ancestry(api.get(path),binding["heldSource"])

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
    storage=getattr(api,"storage",None)
    if storage:storage.register_readiness(archive,binding)

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
    cid,ordered=effects(manifest,binding=HISTORICAL_013)
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
    if getattr(api,"storage",None) and not api.storage.armed:
        api.matched_bodies={"https://api.github.com/repos/"+REPO+"/releases/assets/"+str(row['id']):len(bodies[row['name']]) for row in assets}
        api.matched_bodies.update({("https://nuget.pkg.github.com/FS-GG/download/fs.gg.newsddworkspace/"+VERSION+"/fs.gg.newsddworkspace."+VERSION+".nupkg" if feed=="github" else "https://api.nuget.org/v3-flatcontainer/fs.gg.newsddworkspace/"+VERSION+"/fs.gg.newsddworkspace."+VERSION+".nupkg"):path.stat().st_size for feed,path in feed_paths.items()})
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
        self.cid,self.ordered=effects(manifest,binding=HISTORICAL_013);self.install_count=0
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
            if getattr(self.api,"storage",None):self.custody_projection=self.api.storage.pre_effect(self.api.budget,self.api.matched_bodies,True)
            try:self.api.patch(f"repos/{REPO}/releases/{RELEASE}",{"draft":False,"make_latest":"false"})
            except Exception:patch_unknown=True
            patched=True
            # The response is deliberately not inspected as completion evidence.
            observed,proof=self.matched_gate();require(observed["draft"]is False,"promotion unresolved; no resend")
        self.authority("settle");require(self.state()==current,"presettle journal drift")
        if getattr(self.api,"storage",None) and not self.api.storage.armed:self.custody_projection=self.api.storage.pre_effect(self.api.budget,self.api.matched_bodies,False)
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
    budget.storage=CustodyStorage(root.parent if (root.parent/"binding.json").exists() else root)
    before=None;sdk_before=None;report={"success":False,"historicalCause":"UNKNOWN","publicWizardQualified":False,"adoptionReceiptEmitted":False}
    try:
        subreaper();before=source_snapshot(source,binding,runner,env)
        sdk_root=existing_sdk_root();require((sdk_root/"dotnet").is_file(),"existing SDK absent")
        sdk_before=sdk_snapshot(sdk_root,source,"worker-sdk-roster",budget.work,env)
        require(sdk_before["selection"]["globalConfigSha256"]==before.get("global.json",{}).get("sha256"),"SDK scope source config disagreement")
        budget.storage.bind_report_rosters(before,sdk_before)
        api=FiniteAPI(os.environ.get("GH_TOKEN"),budget,root,held_source=binding["heldSource"])
        report["stage"]="selected-native-admission"
        native_context(api,binding,mode,int(os.environ["GITHUB_RUN_ID"]))
        report["stage"]="existing-sdk-version"
        version=runner.run(["dotnet","--version"],10,env,cwd=source)
        selected=json.loads((source/"global.json").read_text())["sdk"]["version"]
        require(version.returncode==0 and version.stdout.strip()==selected,"exact held SDK unavailable; no fallback")
        report["sdkVersion"]=version.stdout.strip()
        report["stage"]="original-candidate"
        manifest,original,_=candidate(api,root)
        require(digest(original[ORIGINAL_RUNTIME_CONFIG_MEMBER])==ORIGINAL_RUNTIME_CONFIG_SHA256 and json.loads(original[ORIGINAL_RUNTIME_CONFIG_MEMBER]).get("runtimeOptions")==ORIGINAL_RUNTIME_OPTIONS,"original Wizard runtime config changed")
        ledger=FiniteAPI(os.environ.get("ORDINARY_LEDGER_TOKEN"),budget,root,authority=True)
        report["stage"]="immutable-authority-primer"
        ledger.prime_journal()
        journal=ProtectedReleaseJournal(ledger,REF)
        admission=WizardAdmission(api,manifest,binding["heldSource"],int(os.environ["GITHUB_RUN_ID"]),os.environ["GITHUB_ACTOR"],os.environ["GITHUB_REF"],release_binding=HISTORICAL_013)
        engine=Recovery(api,journal,admission,binding,manifest,original,int(os.environ["GITHUB_RUN_ID"]),mode,root,lambda p,n:public_install(p,runner,original,n,env))
        report["stage"]="promotion-reconciliation"
        report.update(engine.run());report["success"]=True
    except BaseException as error:
        recovery_refusal(error);report["errorKind"]=type(error).__name__;report["success"]=False
        failure=http_failure_projection(error)
        if failure is not None:report["httpFailure"]=failure
    finally:
        # Failed operations retain the same full source/SDK membership checks.
        budget.reserve=True
        report["sourceRoster"]=before;report["sdkRoster"]=sdk_before
        report["sdkRoot"]=str(sdk_root) if sdk_before is not None else None
        for name,read,expected in (("Source",lambda:source_snapshot(source,binding,runner,env),before),
                                   ("Sdk",lambda:sdk_snapshot(sdk_root,source,"worker-sdk-post",budget.end,env) if existing_sdk_root()==sdk_root else None,sdk_before)):
            try:
                observed=read();report["post"+name+"Roster"]=observed
                report["post"+name+"Matches"]=expected is not None and observed==expected
                if not report["post"+name+"Matches"]:report["success"]=False
            except BaseException as error:
                report["post"+name+"ErrorKind"]=type(error).__name__;report["post"+name+"Matches"]=False;report["success"]=False
        report["custodyRoles"]=budget.storage.roles
        report["custodyProjection"]=getattr(locals().get("engine"),"custody_projection",None)
        report["readCount"]=budget.reads;report["commandCount"]=budget.commands;report["releasePatchCount"]=budget.release_patches;report["casWrites"]=len(budget.cas_writes);report["casWritePaths"]=budget.cas_writes
        data=canonical(report);require(len(data)<=budget.storage.worker_report_cap,"full schema report cap");(root/"worker-report.json").write_bytes(data)
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

def encrypted_custody(root,binding,runner,storage=None):
    storage=storage or CustodyStorage(root)
    storage.check()
    pem=recipient(binding);certificate=root/"recipient.pem";certificate.write_text(pem)
    export=root/"export";require(not export.exists(),"fresh ciphertext export");export.mkdir()
    archive=root/"raw-custody.zip";entries=[];total=0
    storage.begin_archive()
    with zipfile.ZipFile(archive,"x",compression=zipfile.ZIP_STORED) as zipped:
        for path in sorted(root.rglob("*")):
            if path==archive or path.is_dir():continue
            relative=path.relative_to(root).as_posix()
            require(not path.is_symlink() and path.resolve().is_relative_to(root.resolve()),"raw archive physical scope")
            require(len(entries)<CUSTODY_MEMBER_CAP and path.stat().st_size<=(READINESS_CAP if relative in storage.roles else BINARY_CAP),"raw custody member cap")
            total+=path.stat().st_size;require(total<=128*1024*1024,"raw custody total cap")
            runner.budget.remaining(25,True)
            raw=path.read_bytes();entries.append({"path":relative,"bytes":len(raw),"sha256":digest(raw)});zipped.writestr(relative,raw)
            storage.check()
        zipped.writestr("custody-members.json",canonical(entries))
    require(archive.stat().st_size<=128*1024*1024,"custody archive cap")
    storage.seal_archive();runner.budget.storage=storage
    ciphertext=root/"custody.pending.cms"
    # Source basis: OpenSSL 3.0 CMS docs specify AES-GCM AuthEnvelopedData and
    # -recip/-keyopt for RSA-OAEP. No provider, signature or TLS override.
    command=["openssl","cms","-encrypt","-binary","-aes-256-gcm","-outform","DER","-in",str(archive),"-out",str(ciphertext),"-recip",str(certificate),"-keyopt","rsa_padding_mode:oaep","-keyopt","rsa_oaep_md:sha256","-keyopt","rsa_mgf1_md:sha256"]
    result=runner.run(command,45,{k:v for k,v in os.environ.items() if k in {"HOME","PATH","LANG","LC_ALL"}})
    require(result.returncode==0 and ciphertext.is_file(),"CMS encryption unavailable/refused")
    raw=ciphertext.read_bytes();require(0<len(raw)<=128*1024*1024+8192,"ciphertext cap")
    require(bytes.fromhex("060b2a864886f70d0109100117") in raw and bytes.fromhex("060960864801650304012e") in raw,"AuthEnvelopedData AES256GCM required")
    require(archive.read_bytes()[:64] not in raw,"plaintext archive leak")
    ciphertext.replace(export/"custody.cms");storage.exported()
    summary={"schema":"fsgg.wizard-recovery-encrypted-custody/1","nativeRunId":int(os.environ["GITHUB_RUN_ID"]),"bindingSha256":digest(canonical(binding)),"recipientSha256":binding["recipientSha256"],"ciphertextSha256":digest(raw),"ciphertextBytes":len(raw),"plaintextArchiveSha256":digest(archive.read_bytes()),"plaintextMemberManifestSha256":digest(canonical(entries)),"memberCount":len(entries),"physicalCustodyLimit":PHYSICAL_CUSTODY_CAP,"serializedPhasePolicy":"plaintext128MiB + archive128MiB + ciphertext128MiB8KiB; distinct literal phase roles", "commandExit":result.returncode,"encryptionProcessCustodyClean":bool(runner.records[-1]["custody"]["leaderReaped"] and not runner.records[-1]["custody"]["remaining"]),"encryptionCommandRecordSha256":digest(canonical(runner.records[-1]))}
    (export/"summary.json").write_bytes(canonical(summary))
    require({p.name for p in export.iterdir()}=={"custody.cms","summary.json"},"ciphertext-only export allowlist")
    storage.check()
    return summary

def outer(mode,binding,root,source):
    """Independent observer: only this parent may emit the terminal recovery receipt."""
    validate_binding(binding,mode);recipient(binding);subreaper()
    selected=datetime.fromisoformat(binding["selectedAfter"].replace("Z","+00:00")).timestamp()
    elapsed=time.time()-selected;require(elapsed>=-5,"selection clock future drift")
    budget=Budget(mode,start=time.monotonic()-max(0,elapsed));budget.commands=3
    budget.remaining(25)
    require(not root.exists(),"fresh recovery root required");root.mkdir(mode=0o700,parents=True)
    recovery_phase("outer-source-roster")
    source=source.resolve();source_before=physical_roster(source,deadline=budget.work)
    recovery_phase("outer-sdk-resolve")
    sdk_root=existing_sdk_root()
    recovery_phase("outer-sdk-roster")
    sdk_before=sdk_snapshot(sdk_root,source,"outer-sdk-roster",budget.work)
    require(sdk_before["selection"]["globalConfigSha256"]==source_before.get("global.json",{}).get("sha256"),"SDK scope source config disagreement")
    recovery_phase("outer-report-reservation")
    outer_report_cap=len(canonical(source_before))+len(canonical(sdk_before))+256*1024
    require(outer_report_cap<=BINARY_CAP,"outer full roster report cap")
    recovery_phase("outer-crypto-snapshot")
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
    runner=Runner(budget,root);report={"success":False,"historicalCause":"UNKNOWN","publicWizardQualified":False,"adoptionReceiptEmitted":False,"cryptoPhysicalInputs":crypto_before,"cryptoInputCoverage":"executable, present config and provider files only; loaded libcrypto/libssl/loader not observed", "nativeSetupInventory":[{"action":"actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1","stepSeconds":180},{"action":"actions/create-github-app-token@bcd2ba49218906704ab6c1aa796996da409d3eb1","stepSeconds":120}],"commandInventoryPolicy":"two bounded native action units, entrypoint, observer worker, encryption and every supervised SDK/git command; descendants separately enumerated by identity"}
    try:
        recovery_phase("outer-worker-start")
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
    except BaseException as error:recovery_refusal(error);report["success"]=False;report["errorKind"]=type(error).__name__
    finally:
        try:
            require(os.environ.get("HOME")==binding["home"],"actual HOME changed")
            recovery_phase("outer-source-post")
            report["independentSourceMatches"]=physical_roster(source,deadline=budget.end)==source_before
            recovery_phase("outer-sdk-post")
            report["independentSdkMatches"]=sdk_snapshot(sdk_root,source,"outer-sdk-post",budget.end)==sdk_before
            require(report["independentSourceMatches"] and report["independentSdkMatches"],"independent post-input failure")
        except BaseException as error:recovery_refusal(error);report["success"]=False;report["postErrorKind"]=type(error).__name__
        report["bindingSha256"]=digest(binding_path.read_bytes());report["actualSelectedRunId"]=int(os.environ["GITHUB_RUN_ID"])
        report["elapsedSeconds"]=budget.clock()-budget.start
        report["totalLaunchedCommands"]=report.get("commandCount",0)+budget.commands+1
        recovery_phase("outer-terminal-reservation")
        terminal_raw=canonical(report);require(len(terminal_raw)<=outer_report_cap,"terminal schema report cap")
    if "storage" in locals():storage.check(len(terminal_raw))
    (root/"terminal.json").write_bytes(terminal_raw)
    try:
        # Also encrypt failed-operation captures. The public export contains only
        # ciphertext and fixed safe hashes; raw files never reach upload-artifact.
        budget.reserve=True
        storage=CustodyStorage(root)
        if (worker_root/"worker-report.json").is_file():
            worker_report=json.loads((worker_root/"worker-report.json").read_bytes())
            for relative,role in worker_report.get("custodyRoles",{}).items():
                require(set(role)=={"role","bytes","sha256"} and role["role"]=="authenticated-native-diagnostic-archive","closed custody role")
                path=root/relative
                # The original digest and ciphertext/native/binding joins are
                # rechecked independently; arbitrary large paths are refused.
                storage.register_readiness(path,binding)
                require(storage.roles[relative]==role,"outer readiness role drift")
        recovery_phase("outer-encrypted-custody")
        report["encryptedCustody"]=encrypted_custody(root,binding,runner,storage)
        require(Path(shutil.which("openssl") or "").resolve()==openssl and all(Path(p).is_file() and digest(Path(p).read_bytes())==sha for p,sha in crypto_before.items()),"encryption physical input drift")
        report["cryptoPhysicalInputs"]=crypto_before
        summary_path=root/"export/summary.json";summary=json.loads(summary_path.read_bytes())
        summary["cryptoInputCoverage"]=report["cryptoInputCoverage"];summary["cryptoInputsUnchanged"]=True;summary["cryptoPhysicalInputDigest"]=digest(canonical(crypto_before));summary["totalLaunchedCommands"]=report["totalLaunchedCommands"]
        summary_path.write_bytes(canonical(summary))
    except BaseException as error:
        recovery_refusal(error)
        report["success"]=False;report["custodyErrorKind"]=type(error).__name__
    recovery_phase("outer-final-report")
    terminal_raw=canonical(report);require(len(terminal_raw)<=outer_report_cap,"terminal schema report cap")
    if "storage" in locals():storage.check(len(terminal_raw))
    (root/"terminal.json").write_bytes(terminal_raw)
    if report["success"]:
        if "storage" in locals():storage.check(len(terminal_raw))
        (root/"recovery-receipt.json").write_bytes(terminal_raw)
        if "storage" in locals():storage.check()
    print(json.dumps({"scope":report.get("scope",mode),"success":report["success"],"historicalCause":"UNKNOWN","publicWizardQualified":False},sort_keys=True))
    return 0 if report["success"] else 1

# Explicit post-completion observer; no recovery/admission/effect engine is constructed.
READBACK_MODE="post-completion-readback"
ORIGINAL_H4={"originalHeldSource":"49a5e93668d4647999cd7171a26d4ad4d151b66b",
 "originalHeldTree":"d1f2214d702be0b6e41aa4a26f6799caf656ba24","originalRunId":37288088411,
 "originalArtifactId":11335084216,"originalArchiveSha256":"f34e9f33a3a0518e2a8d0c7613b048b8ad08f64b5c10de75a09aba8b9535a93e",
 "originalBindingSha256":"bdaecd6734185366328035595ffa189364870171cffbeab71f4c3cad21ccfe48",
 "originalCiphertextSha256":"eb2ad03c07e1934438c63a491ebee5bfc2ff8e7e3e21a33a2082f88d39de1648",
 "originalCorrelation":"6af5fbe162112ef890a4761b74d06831"}

def readback_binding(raw):
    def unique(pairs):
        value={}
        for key,item in pairs:
            require(key not in value,"readback duplicate binding property");value[key]=item
        return value
    require(isinstance(raw,str) and len(raw.encode())<=SMALL_JSON_CAP,"readback binding byte cap")
    binding=json.loads(raw,object_pairs_hook=unique)
    fields=set(ORIGINAL_H4)|{"schema","observerSource","observerTree","observerWorkflowSha256","observerMain","completedJournalHead","correlation","selectedAfter","recipientSha256"}
    require(isinstance(binding,dict) and set(binding)==fields,"readback binding shape")
    require(binding["schema"]=="fsgg.wizard-postcomplete-readback-binding/1","readback binding schema")
    require(all(binding[k]==v and type(binding[k]) is type(v) for k,v in ORIGINAL_H4.items()),"original H4 provenance differs")
    require(all(isinstance(binding[k],str) and re.fullmatch(r"[0-9a-f]{40}",binding[k]) for k in ("observerSource","observerTree","observerMain","completedJournalHead")),"readback revisions")
    require(binding["observerSource"]==binding["observerMain"] and binding["observerSource"]!=binding["originalHeldSource"],"distinct current observer source")
    require(all(isinstance(binding[k],str) and re.fullmatch(r"[0-9a-f]{64}",binding[k]) for k in ("observerWorkflowSha256","recipientSha256")),"readback digests")
    require(isinstance(binding["correlation"],str) and re.fullmatch(r"[0-9a-f]{32}",binding["correlation"]) and binding["correlation"]!=binding["originalCorrelation"],"fresh readback correlation")
    require(isinstance(binding["selectedAfter"],str) and re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z",binding["selectedAfter"]),"readback selection instant")
    datetime.fromisoformat(binding["selectedAfter"].replace("Z","+00:00"))
    return binding

class ReadbackRedirect(Redirect):
    def redirect_request(self,req,fp,code,msg,headers,newurl):
        require(req.get_method()=="GET" and req.data is None,"readback redirect write forbidden")
        result=super().redirect_request(req,fp,code,msg,headers,newurl)
        require(result is not None and result.get_method()=="GET" and result.data is None,"readback redirected request write forbidden")
        return result

class ReadbackTransport:
    """Actual pre-transport guard, including direct legacy class-method aliases."""
    def __init__(self,opener):self._opener=opener
    def open(self,request,*args,**kwargs):
        require(isinstance(request,urllib.request.Request) and request.get_method()=="GET" and request.data is None,"readback transport write forbidden")
        require({key.lower() for key,_ in request.header_items()}<={"authorization","user-agent","accept","x-github-api-version"},"readback transport closed headers")
        require(not args and set(kwargs)=={"timeout"},"readback transport arguments")
        return self._opener.open(request,**kwargs)

class ReadbackAPI(FiniteAPI):
    def __init__(self,*args,**kwargs):
        super().__init__(*args,**kwargs)
        self.opener=ReadbackTransport(urllib.request.build_opener(ReadbackRedirect(self.budget)))
    def request(self,url,method="GET",body=None,**kwargs):
        require(method=="GET" and body is None,"readback request write forbidden")
        headers=kwargs.get("headers")
        require(headers is None or (isinstance(headers,dict) and set(headers)<={"Accept","Authorization"}),"readback closed request headers")
        return super().request(url,method,body,**kwargs)
    def post(self,*args,**kwargs):require(False,"readback journal write forbidden")
    def patch(self,*args,**kwargs):require(False,"readback release write forbidden")
    def arm_settlement(self,*args,**kwargs):require(False,"readback settlement forbidden")
    def prime_completed_journal(self,head):
        require(self.authority,"readback Authority scope")
        prefix=f"repos/{AUTHORITY}"
        commits=self.get(prefix+f"/commits?sha={head}&per_page=100")
        require(isinstance(commits,list) and len(commits)==17,"readback full17 commit roster")
        require(commits[0].get("sha")==head and commits[1].get("sha")==JOURNAL_HEAD,"readback original16 parent")
        parent=head;seen=set()
        for commit in commits:
            oid=commit.get("sha");require(oid==parent and oid not in seen,"readback full17 lineage identity")
            seen.add(oid);parents=commit.get("parents",[]);require(len(parents)<=1,"readback full17 parent roster")
            parent=parents[0]["sha"] if parents else None
            contents=self.get(prefix+f"/contents/release-state.json?ref={oid}")
            require(contents.get("type")=="file" and contents.get("path")=="release-state.json" and contents.get("encoding")=="base64","readback exact journal blob")
            raw=base64.b64decode(contents["content"],validate=True);blob=contents.get("sha")
            require(hashlib.sha1(b"blob "+str(len(raw)).encode()+b"\0"+raw).hexdigest()==blob,"readback Git blob identity")
            require(raw==canonical(json.loads(raw))+b"\n","readback canonical journal blob")
            tree_raw=b"100644 release-state.json\0"+bytes.fromhex(blob)
            tree=hashlib.sha1(b"tree "+str(len(tree_raw)).encode()+b"\0"+tree_raw).hexdigest()
            require(commit.get("commit",{}).get("tree",{}).get("sha")==tree,"readback complete Git tree identity")
            self.immutable[prefix+"/git/commits/"+oid]={"sha":oid,"parents":[{"sha":p["sha"]} for p in parents],"tree":{"sha":tree}}
            self.immutable[prefix+"/git/trees/"+tree]={"tree":[{"path":"release-state.json","sha":blob}]}
            self.immutable[prefix+"/git/blobs/"+blob]={"sha":blob,"encoding":"base64","content":contents["content"]}
        require(parent is None,"readback full17 lineage root")

def readback_native(api,binding,run_id):
    # Job GITHUB_TOKEN has no /user contract. Authenticate existing run/repo/workflow roles.
    require(api.get(f"repos/{REPO}").get("id")==1269292704,"readback repository identity")
    title=f"Wizard 0.13 recovery {READBACK_MODE} {binding['correlation']} {digest(canonical(binding))}"
    matches=[]
    for page in range(1,5):
        data=api.get(f"repos/{REPO}/actions/workflows/release-new-sdd-workspace.yml/runs?event=workflow_dispatch&branch=main&per_page=100&page={page}")
        rows=data.get("workflow_runs",[]);require(isinstance(rows,list) and len(rows)<=100,"readback workflow page bound")
        matches.extend(row for row in rows if row.get("display_title")==title)
        if len(rows)<100:break
    require(len(matches)==1 and matches[0].get("id")==run_id,"readback native correlation")
    observer=api.get(f"repos/{REPO}/actions/runs/{run_id}")
    require(observer.get("id")==run_id and observer.get("display_title")==title and observer.get("head_sha")==binding["observerSource"] and observer.get("run_attempt")==1 and observer.get("status")=="in_progress" and observer.get("path")==WORKFLOW and observer.get("event")=="workflow_dispatch" and observer.get("head_branch")=="main" and observer.get("actor",{}).get("login")=="EHotwagner" and observer.get("repository",{}).get("id")==1269292704,"readback native observer role")
    require(api.get(f"repos/{REPO}/git/ref/heads/main").get("object",{}).get("sha")==binding["observerMain"],"readback current main drift")
    current=api.get(f"repos/{REPO}/git/commits/{binding['observerSource']}")
    require(current.get("sha")==binding["observerSource"] and current.get("tree",{}).get("sha")==binding["observerTree"],"readback observer commit/tree")
    workflow=api.get(f"repos/{REPO}/contents/{WORKFLOW}?ref={binding['observerSource']}")
    require(workflow.get("type")=="file" and workflow.get("path")==WORKFLOW and workflow.get("encoding")=="base64","readback observer workflow role")
    require(digest(base64.b64decode(workflow["content"],validate=True))==binding["observerWorkflowSha256"],"readback observer workflow bytes")
    run=api.get(f"repos/{REPO}/actions/runs/{ORIGINAL_H4['originalRunId']}")
    title=f"Wizard 0.13 recovery complete {ORIGINAL_H4['originalCorrelation']} {ORIGINAL_H4['originalBindingSha256']}"
    require(run.get("id")==ORIGINAL_H4["originalRunId"] and run.get("display_title")==title and run.get("head_sha")==ORIGINAL_H4["originalHeldSource"] and run.get("run_attempt")==1 and run.get("conclusion")=="success" and run.get("status")=="completed" and run.get("path")==WORKFLOW and run.get("event")=="workflow_dispatch" and run.get("head_branch")=="main" and run.get("actor",{}).get("login")=="EHotwagner" and run.get("repository",{}).get("id")==1269292704,"readback original H4 native metadata")
    artifact=api.get(f"repos/{REPO}/actions/artifacts/{ORIGINAL_H4['originalArtifactId']}")
    origin=artifact.get("workflow_run",{})
    require(artifact.get("id")==ORIGINAL_H4["originalArtifactId"] and artifact.get("expired")is False and artifact.get("digest")=="sha256:"+ORIGINAL_H4["originalArchiveSha256"] and artifact.get("name")==f"wizard013-recovery-complete-{ORIGINAL_H4['originalRunId']}-{ORIGINAL_H4['originalCorrelation']}" and origin.get("id")==ORIGINAL_H4["originalRunId"] and origin.get("head_sha")==ORIGINAL_H4["originalHeldSource"] and origin.get("repository_id")==1269292704 and origin.get("head_repository_id")==1269292704,"readback original H4 artifact metadata")

def post_complete_readback(api,journal,binding,manifest,original,run_id):
    cid,ordered=effects(manifest,binding=HISTORICAL_013)
    expected=JournalState(17,cid,{e.identity:"verified" for e in ordered})
    def state():
        current=journal.read()
        require(current==expected and journal._observed.head==binding["completedJournalHead"] and journal._lineage_length==17,"readback exact settled full17")
        journal.validate_intent({"contentId":cid,"sourceSha":CANDIDATE_SOURCE,"version":VERSION,"candidateArchiveSha256":ARCHIVE,"operator":"EHotwagner"})
        return current
    readback_native(api,binding,run_id);state()
    release,feeds=release_gate(api,manifest,original)
    require(release["draft"]is False,"readback public release required")
    state()
    after,after_feeds=release_gate(api,manifest,original)
    require(after==release and {name:digest(path.read_bytes()) for name,path in after_feeds.items()}=={name:digest(path.read_bytes()) for name,path in feeds.items()},"readback release/feed drift")
    state();readback_native(api,binding,run_id)
    return {"scope":"post-completion-readback","generation":17,"authorityHead":binding["completedJournalHead"],"observerSource":binding["observerSource"],"observerTree":binding["observerTree"],"observerWorkflowSha256":binding["observerWorkflowSha256"],"originalH4":dict(ORIGINAL_H4),"freshFeeds":{name:{"bytes":path.stat().st_size,"sha256":digest(path.read_bytes())} for name,path in feeds.items()},"freshPublicRelease":True,"newInstalledQualification":False,"publicWizardQualified":False,"adoptionReceiptEmitted":False,"publisherComplete":False}

def readback_worker(binding,root,source,start):
    require(os.environ.get("PROMOTION_RECOVERY_MODE")==READBACK_MODE and os.environ.get("GITHUB_EVENT_NAME")=="workflow_dispatch" and os.environ.get("GITHUB_RUN_ATTEMPT")=="1" and os.environ.get("GITHUB_REPOSITORY")==REPO and os.environ.get("GITHUB_REF")=="refs/heads/main" and os.environ.get("GITHUB_ACTOR")=="EHotwagner" and os.environ.get("GITHUB_SHA")==binding["observerSource"] and os.environ.get("RECOVERY_BINDING_SHA256")==digest(canonical(binding)) and os.environ.get("RECOVERY_CORRELATION")==binding["correlation"],"readback worker explicit native role")
    budget=Budget("diagnostic",start=start);runner=Runner(budget,root)
    before=None;report={"success":False,"scope":READBACK_MODE,"publicWizardQualified":False,"adoptionReceiptEmitted":False,"newInstalledQualification":False}
    source_binding={"heldSource":binding["observerSource"],"heldTree":binding["observerTree"]}
    env=child_environment(root/"checks")
    try:
        subreaper();before=source_snapshot(source,source_binding,runner,env)
        require(digest((source/WORKFLOW).read_bytes())==binding["observerWorkflowSha256"],"readback local workflow bytes")
        api=ReadbackAPI(os.environ.get("GH_TOKEN"),budget,root,held_source=binding["observerSource"])
        manifest,original,_=candidate(api,root)
        ledger=ReadbackAPI(os.environ.get("ORDINARY_LEDGER_TOKEN"),budget,root,authority=True)
        ledger.prime_completed_journal(binding["completedJournalHead"])
        report.update(post_complete_readback(api,ProtectedReleaseJournal(ledger,REF),binding,manifest,original,int(os.environ["GITHUB_RUN_ID"])));report["success"]=True
    except BaseException as error:
        recovery_refusal(error);report["errorKind"]=type(error).__name__;report["success"]=False
    finally:
        budget.reserve=True
        report["sourceRoster"]=before
        try:
            after=source_snapshot(source,source_binding,runner,env)
            report["postSourceRoster"]=after;report["postSourceMatches"]=before is not None and after==before
            if not report["postSourceMatches"]:report["success"]=False
        except BaseException as error:
            report["postSourceErrorKind"]=type(error).__name__;report["postSourceMatches"]=False;report["success"]=False
        report["readCount"]=budget.reads;report["commandCount"]=budget.commands
        require(len(canonical(report))<=BINARY_CAP,"readback report cap")
        (root/"worker-result.json").write_bytes(canonical(report))
        (root/"commands.json").write_bytes(canonical(runner.records))
    return 0 if report["success"] else 1

def readback_entry(binding_text,root,source):
    require(os.environ.get("GITHUB_EVENT_NAME")=="workflow_dispatch" and os.environ.get("GITHUB_RUN_ATTEMPT")=="1" and os.environ.get("GITHUB_REPOSITORY")==REPO and os.environ.get("GITHUB_REF")=="refs/heads/main" and os.environ.get("GITHUB_ACTOR")=="EHotwagner","readback native entry role")
    require(os.environ.get("PROMOTION_RECOVERY_MODE")==READBACK_MODE,"readback explicit selection")
    binding=readback_binding(binding_text)
    require(binding["observerSource"]==os.environ.get("GITHUB_SHA") and digest(canonical(binding))==os.environ.get("RECOVERY_BINDING_SHA256") and binding["correlation"]==os.environ.get("RECOVERY_CORRELATION"),"readback selected source/binding")
    require(os.environ.get("CANDIDATE_RUN_ID")==str(CANDIDATE_RUN) and os.environ.get("CANDIDATE_ARTIFACT_ID")==str(ARTIFACT) and os.environ.get("CANDIDATE_ARCHIVE_SHA256")==ARCHIVE,"readback original candidate selection")
    recipient(binding);subreaper()
    elapsed=time.time()-datetime.fromisoformat(binding["selectedAfter"].replace("Z","+00:00")).timestamp()
    require(elapsed>=-5,"readback future selection")
    budget=Budget("diagnostic",start=time.monotonic()-max(0,elapsed));budget.commands=3;budget.remaining(25)
    root=Path(root);source=Path(source).resolve()
    require(not root.exists(),"fresh readback root required");root.mkdir(mode=0o700,parents=True)
    before=physical_roster(source,deadline=budget.work)
    openssl=Path(shutil.which("openssl") or "").resolve();require(openssl.is_file(),"stock OpenSSL unavailable; no setup fallback")
    crypto={str(openssl):digest(openssl.read_bytes())}
    for path in [Path("/etc/ssl/openssl.cnf"),*sorted(Path("/usr/lib/x86_64-linux-gnu/ossl-modules").glob("*.so"))]:
        if path.is_file():crypto[str(path.resolve())]=digest(path.read_bytes())
    binding_path=root/"binding.json";binding_path.write_bytes(canonical(binding));binding_path.chmod(0o600)
    worker_root=root/"worker";worker_root.mkdir(mode=0o700)
    env={k:v for k,v in os.environ.items() if k in {"HOME","PATH","LANG","LC_ALL","TZ","SSL_CERT_FILE","SSL_CERT_DIR","GH_TOKEN","ORDINARY_LEDGER_TOKEN","GITHUB_RUN_ID","GITHUB_EVENT_NAME","GITHUB_RUN_ATTEMPT","GITHUB_REPOSITORY","GITHUB_REF","GITHUB_ACTOR","GITHUB_SHA","RECOVERY_BINDING_SHA256","RECOVERY_CORRELATION","PROMOTION_RECOVERY_MODE"}}
    env["PYTHONDONTWRITEBYTECODE"]="1"
    runner=Runner(budget,root);report={"success":False,"scope":READBACK_MODE,"publicWizardQualified":False,"adoptionReceiptEmitted":False,"newInstalledQualification":False}
    try:
        result=runner.run([sys.executable,str(Path(__file__).resolve()),"--readback-worker",str(binding_path),str(worker_root),str(source),str(budget.start)],480,env,cwd=source)
        inner=json.loads((worker_root/"worker-result.json").read_bytes());report.update(inner)
        require(result.returncode==0 and inner["success"],"readback worker refused")
    except BaseException as error:
        report["success"]=False;report["errorKind"]=type(error).__name__;recovery_refusal(error)
    budget.reserve=True
    try:
        require(physical_roster(source,deadline=budget.end)==before,"readback observer source drift")
        require(Path(shutil.which("openssl") or "").resolve()==openssl and all(Path(p).is_file() and digest(Path(p).read_bytes())==sha for p,sha in crypto.items()),"readback crypto input drift")
        commands=json.loads((worker_root/"commands.json").read_bytes())
        require(len(commands)<=6 and len(commands)==report.get("commandCount"),"readback inner command count")
        require(all(c.get("custody",{}).get("leaderReaped") and not c.get("custody",{}).get("remaining") for c in [*runner.records,*commands]),"readback process custody unresolved")
        report["cryptoPhysicalInputs"]=crypto
        report["cryptoInputCoverage"]="executable, present config and provider files only; loaded libcrypto/libssl/loader not observed"
        # This newly attributed custody never replaces or accepts original H4.
        (root/"observer-result.json").write_bytes(canonical(report))
        report["encryptedCustody"]=encrypted_custody(root,binding,runner)
        require(Path(shutil.which("openssl") or "").resolve()==openssl and all(Path(p).is_file() and digest(Path(p).read_bytes())==sha for p,sha in crypto.items()),"readback crypto input drift")
        budget.remaining(25,True)
    except BaseException as error:
        report["success"]=False;report["custodyErrorKind"]=type(error).__name__;recovery_refusal(error)
    terminal=canonical(report);require(len(terminal)<=BINARY_CAP,"readback terminal report cap")
    (root/"terminal.json").write_bytes(terminal)
    try:budget.remaining(25,True)
    except BaseException as error:
        report["success"]=False;report["publicationErrorKind"]=type(error).__name__
        (root/"terminal.json").write_bytes(canonical(report))
    print(json.dumps({"scope":READBACK_MODE,"success":report["success"],"publicWizardQualified":False,"newInstalledQualification":False,"adoptionReceiptEmitted":False},sort_keys=True))
    return 0 if report["success"] else 1

def entry(mode,binding_text,root,source):
    recovery_phase("entry")
    try:
        require(os.environ.get("GITHUB_EVENT_NAME")=="workflow_dispatch" and os.environ.get("GITHUB_RUN_ATTEMPT")=="1" and os.environ.get("GITHUB_REPOSITORY")==REPO and os.environ.get("GITHUB_REF")=="refs/heads/main" and os.environ.get("GITHUB_ACTOR")=="EHotwagner","native entrypoint role")
        binding=json.loads(binding_text);validate_binding(binding,mode)
        require(binding["heldSource"]==os.environ.get("GITHUB_SHA"),"native held source")
        require(digest(canonical(binding))==os.environ.get("RECOVERY_BINDING_SHA256") and binding["correlation"]==os.environ.get("RECOVERY_CORRELATION"),"root-selected dispatch binding digest/correlation")
        return outer(mode,binding,Path(root),Path(source))
    except BaseException as error:
        recovery_refusal(error)
        raise

if __name__=="__main__":
    if len(sys.argv)==5 and sys.argv[1]=="--post-completion-readback":
        raise SystemExit(readback_entry(sys.argv[2],sys.argv[3],sys.argv[4]))
    if len(sys.argv)==6 and sys.argv[1]=="--readback-worker":
        _,_,binding_path,root,source,start=sys.argv
        raise SystemExit(readback_worker(readback_binding(Path(binding_path).read_text()),Path(root),Path(source),float(start)))
    require(len(sys.argv)==7 and sys.argv[1]=="--worker","only supervised worker entry")
    _,_,mode,binding_path,root,source,start=sys.argv
    binding=json.loads(Path(binding_path).read_bytes());validate_binding(binding,mode)
    raise SystemExit(worker(mode,binding,Path(root),Path(source),float(start)))

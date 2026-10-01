#!/usr/bin/env python3
"""Inert persistent receiver preparation with held-path custody and stopped-store recovery."""
from __future__ import annotations
import argparse, fcntl, hashlib, json, os, pathlib, re, secrets, shutil, stat, sys

PROFILE_SCHEMA="fsgg.telemetry.persistent-receiver-profile/1"
STATE_SCHEMA="fsgg.telemetry.persistent-receiver-state/2"
BACKUP_SCHEMA="fsgg.telemetry.persistent-receiver-backup/2"
CONFIG_SCHEMA="fsgg.telemetry.host-config/2"
INSTALL_SCHEMA="fsgg.telemetry.native-collector-installation/2"
HEX=re.compile(r"[0-9a-f]{64}")
IDENT=re.compile(r"[a-z][a-z0-9-]{2,63}")
MAX_FILE=64*1024*1024
MAX_FILES=20000

class Refusal(Exception): pass
def require(value,message):
    if not value: raise Refusal(message)
def canonical(value): return (json.dumps(value,sort_keys=True,separators=(",",":"))+"\n").encode()
def sha_bytes(data): return hashlib.sha256(data).hexdigest()
def sha_file(path):
    h=hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda:source.read(1024*1024),b""): h.update(block)
    return h.hexdigest()
def write_new(path,data,mode=0o600):
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,mode)
    with os.fdopen(fd,"wb") as target:
        target.write(data); target.flush(); os.fsync(target.fileno())
def mkdir_new(path,mode=0o700): path.mkdir(mode=mode,parents=False,exist_ok=False)
def identity(info): return (info.st_dev,info.st_ino)
def safe_basename(name): require(re.fullmatch(r"[a-z][a-z0-9-]{2,79}",name) is not None,"name-refused"); return name

def ancestry_without_links(path):
    require(path.is_absolute(),"absolute-path-required")
    current=pathlib.Path(path.anchor)
    for part in path.parts[1:]:
        current=current/part
        info=current.lstat(); require(not stat.S_ISLNK(info.st_mode),"symlink-ancestor-refused")

def private_info(info,label,directory=True,readonly=False):
    require((stat.S_ISDIR(info.st_mode) if directory else stat.S_ISREG(info.st_mode)),f"{label}-type-refused")
    require(info.st_uid==os.getuid(),f"{label}-owner-refused")
    allowed={0o500} if readonly and directory else {0o400} if readonly else {0o700} if directory else {0o600}
    require(stat.S_IMODE(info.st_mode) in allowed,f"{label}-mode-refused")
    if not directory: require(info.st_nlink==1 and info.st_size<=MAX_FILE,f"{label}-custody-refused")

class HeldDirectory:
    def __init__(self,path,label="directory",readonly=False):
        self.path=pathlib.Path(path)
        self.descriptor_path=str(self.path).startswith("/proc/self/fd/")
        descriptor=re.fullmatch(r"/proc/self/fd/([0-9]+)",str(self.path))
        if not self.descriptor_path: ancestry_without_links(self.path)
        self.fd=os.dup(int(descriptor.group(1))) if descriptor else os.open(self.path,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        self.info=os.fstat(self.fd); private_info(self.info,label,True,readonly)
        self.proc=pathlib.Path(f"/proc/self/fd/{self.fd}")
    def assert_current(self):
        if not self.descriptor_path: ancestry_without_links(self.path)
        observed=self.path.stat() if re.fullmatch(r"/proc/self/fd/[0-9]+",str(self.path)) else self.path.lstat()
        require(identity(observed)==identity(self.info),"directory-replaced-refused")
    def child_exists(self,name):
        try: os.stat(name,dir_fd=self.fd,follow_symlinks=False); return True
        except FileNotFoundError: return False
    def create_child(self,name):
        safe_basename(name); os.mkdir(name,0o700,dir_fd=self.fd); info=os.stat(name,dir_fd=self.fd,follow_symlinks=False); private_info(info,"created-directory"); return identity(info)
    def remove_created(self,name,expected):
        if not self.child_exists(name): return
        info=os.stat(name,dir_fd=self.fd,follow_symlinks=False); require(identity(info)==expected and stat.S_ISDIR(info.st_mode),"cleanup-identity-refused")
        owned=self.proc/name
        for directory,children,files in os.walk(owned,topdown=False,followlinks=False):
            for entry in files:
                path=pathlib.Path(directory)/entry; require(not path.is_symlink(),"cleanup-link-refused"); path.chmod(0o600)
            for entry in children:
                path=pathlib.Path(directory)/entry; require(not path.is_symlink(),"cleanup-link-refused"); path.chmod(0o700)
        shutil.rmtree(owned)
    def commit(self,stage,target,expected):
        safe_basename(stage); safe_basename(target); self.assert_current(); require(not self.child_exists(target),"target-exists-refused")
        info=os.stat(stage,dir_fd=self.fd,follow_symlinks=False); require(identity(info)==expected,"stage-identity-refused")
        os.rename(stage,target,src_dir_fd=self.fd,dst_dir_fd=self.fd); os.fsync(self.fd)
    def close(self): os.close(self.fd)
    def __enter__(self): return self
    def __exit__(self,*_): self.close()

def private_file(path,label,readonly=False):
    info=path.lstat(); require(not path.is_symlink(),f"{label}-link-refused"); private_info(info,label,False,readonly); return info
def load_json(path,label,private=False):
    if private: private_file(path,label)
    else:
        info=path.lstat(); require(stat.S_ISREG(info.st_mode) and not path.is_symlink() and info.st_size<=MAX_FILE,f"{label}-refused")
    try: return json.loads(path.read_text())
    except (OSError,UnicodeDecodeError,json.JSONDecodeError) as error: raise Refusal(f"{label}-json-refused") from error

def canonical_posix(value,label):
    require(isinstance(value,str) and value.startswith("/") and not value.endswith("/") and "//" not in value,f"{label}-path-refused")
    parts=value.split("/")[1:]; require(parts and all(x not in {"",".",".."} for x in parts),f"{label}-path-refused")
    require(str(pathlib.PurePosixPath(value))==value,f"{label}-path-refused"); return value
def beneath(path,anchor): return path.startswith(anchor+"/")
def overlap(left,right): return left==right or beneath(left,right) or beneath(right,left)

def validate_profile(profile):
    require(set(profile)=={"schema","receiverId","workspaceId","producerId","streamId","uid","gid","host","runtime","image","manager","reader","containerPaths"},"profile-shape-refused")
    require(profile["schema"]==PROFILE_SCHEMA,"profile-schema-refused")
    for key in ("receiverId","workspaceId","producerId","streamId"): require(isinstance(profile[key],str) and IDENT.fullmatch(profile[key]),f"profile-{key}-refused")
    require(profile["uid"]==32768 and profile["gid"]==32768,"profile-identity-refused")
    host=profile["host"]; require(set(host)=={"version","sourceSha","packageSha256","manifestSha256","journalSha256","payloadSha256"},"host-pins-shape-refused")
    require(host["version"]=="0.2.1" and re.fullmatch(r"[0-9a-f]{40}",host["sourceSha"]),"host-identity-refused")
    for key in ("packageSha256","manifestSha256","journalSha256","payloadSha256"): require(HEX.fullmatch(host[key]),f"host-{key}-refused")
    runtime=profile["runtime"]; require(set(runtime)=={"platform","baseImage"} and runtime["platform"]=="linux/amd64" and "@sha256:" in runtime["baseImage"] and HEX.fullmatch(runtime["baseImage"].rsplit("@sha256:",1)[1]),"runtime-pin-refused")
    image=profile["image"]; require(set(image)=={"containerfileSha256","resultDigestRequiredBeforeInstall","activation"} and HEX.fullmatch(image["containerfileSha256"]) and image["resultDigestRequiredBeforeInstall"] is True and image["activation"]=="disabled-before-private-readback","image-pin-refused")
    manager=profile["manager"]; require(set(manager)=={"coordinationSource","programSha256","command","backupCommand","installationSchema"} and re.fullmatch(r"[0-9a-f]{40}",manager["coordinationSource"]) and HEX.fullmatch(manager["programSha256"]) and manager["command"]=="install-native-collector" and manager["backupCommand"]=="backup-stopped-host" and manager["installationSchema"]==INSTALL_SCHEMA,"manager-pin-refused")
    reader=profile["reader"]; require(set(reader)=={"executablePath","executableSha256","provider","model","effort","profileSha256"},"reader-shape-refused")
    canonical_posix(reader["executablePath"],"reader"); require(HEX.fullmatch(reader["executableSha256"]) and HEX.fullmatch(reader["profileSha256"]),"reader-pin-refused")
    require(reader["provider"]=="openai" and reader["model"]=="gpt-5.6-sol" and reader["effort"]=="medium","reader-profile-refused")
    paths=profile["containerPaths"]; expected={"configAnchor","hostConfig","credentials","store","evidence","codexHome","nativeSource","producerSpool"}; require(set(paths)==expected,"mount-shape-refused")
    for key in expected: canonical_posix(paths[key],key)
    anchor=paths["configAnchor"]; require(paths["hostConfig"]==anchor+"/host.json","host-config-anchor-refused")
    for key in ("credentials","store","evidence","codexHome"): require(beneath(paths[key],anchor),f"common-anchor-refused:{key}")
    development=(paths["nativeSource"],paths["producerSpool"]); private=(anchor,)
    require(not overlap(*development),"development-mount-overlap-refused")
    for dev in development:
        require(dev.startswith("/producer/") and all(not overlap(dev,item) for item in private),"development-private-overlap-refused")
    return sha_bytes(canonical(profile))

def commands(profile):
    p=profile["containerPaths"]; reference="learn-native-collector-v1"
    host=["/usr/bin/dotnet","/opt/fsgg/telemetry-host/FS.GG.Telemetry.Host.dll"]
    manager=["/opt/fsgg/telemetry-host-manager/TelemetryHostManager"]
    return {"init":host+["init","--root",p["store"],"--workspace",profile["workspaceId"]],"enroll":host+["enroll-producer","--config",p["hostConfig"],"--reference",reference,"--secret-file",p["credentials"]+"/collector.token","--workspace",profile["workspaceId"],"--producer",profile["producerId"],"--stream",profile["streamId"]],"preflight":host+["status","--config",p["hostConfig"]],"install":manager+["install-native-collector","--host-config",p["hostConfig"],"--credential-reference",reference,"--executable",profile["reader"]["executablePath"],"--executable-sha256",profile["reader"]["executableSha256"],"--installation-version","2","--codex-home",p["codexHome"],"--evidence-root",p["evidence"],"--provider",profile["reader"]["provider"],"--model",profile["reader"]["model"],"--effort",profile["reader"]["effort"]],"backup":manager+["backup-stopped-host","--operator","/opt/fsgg/telemetry-operator","--deployment","/receiver/deployment","--backup-id","ROOT_SUPPLIED","--host-unit","fsgg-telemetry-host-podman.service"]}

def plan(profile):
    digest=validate_profile(profile); p=profile["containerPaths"]
    return {"schema":"fsgg.telemetry.persistent-receiver-plan/2","profileSha256":digest,"receiverId":profile["receiverId"],"effectsPerformed":False,"activationAuthorized":False,"hostStateInitialized":False,"producerEnrolled":False,"installerApplied":False,"mainRequired":False,"publishedHostReused":True,"host":profile["host"],"runtime":profile["runtime"],"image":profile["image"],"manager":profile["manager"],"reader":profile["reader"],"uid":profile["uid"],"gid":profile["gid"],"mounts":{"receiverPrivate":[p["configAnchor"]],"developmentWritable":[p["nativeSource"],p["producerSpool"]],"developmentForbidden":[p["configAnchor"]],"sameNativeSourceVolume":{"developmentTarget":p["nativeSource"],"collectorReadOnlyTarget":p["codexHome"]}},"commands":commands(profile),"managerGenerated":{"sidecar":p["hostConfig"]+".native-collector.json","receipt":p["hostConfig"]+".native-collector.receipt.json"},"nextEffect":"root-owned image build/digest readback, permanent placement, Host init/enrollment and installer-v2"}

def initial_payloads(profile,digest,token):
    p=profile["containerPaths"]; reference="learn-native-collector-v1"
    credential={"Reference":reference,"SecretFile":p["credentials"]+"/collector.token","WorkspaceId":profile["workspaceId"],"ProducerId":profile["producerId"],"StreamId":profile["streamId"],"Role":"native-collector","GrantId":"grant-learn-native-collector-v1","GrantGeneration":1,"Revoked":False}
    config={"Schema":CONFIG_SCHEMA,"ListenUrl":"https://0.0.0.0:7443","CertificatePath":p["credentials"]+"/receiver.pfx","CertificatePasswordFile":p["credentials"]+"/certificate-password","ServiceLockPath":p["configAnchor"]+"/host.lock","Stores":[{"WorkspaceId":profile["workspaceId"],"Root":p["store"]}],"Credentials":[credential],"BrowserPrincipals":[],"BrowserSession":{"IdleSeconds":300,"AbsoluteSeconds":600,"MaximumSessions":2,"LoginAttemptsPerMinute":2,"LoginAdmission":1,"QueryAdmission":1,"QueryTimeoutSeconds":10}}
    refs={"schema":"fsgg.telemetry.persistent-source-references/2","profileSha256":digest,"nativeSourceVolume":"learn-native-source-v1","developmentTarget":p["nativeSource"],"collectorReadOnlyTarget":p["codexHome"],"readerProfileSha256":profile["reader"]["profileSha256"],"captureQualified":False}
    return {"receiver/private/host.json":canonical(config),"receiver/private/credentials/collector.token":(token+"\n").encode(),"receiver/private/source-reference.json":canonical(refs),"receiver/state.json":canonical({"schema":STATE_SCHEMA,"profileSha256":digest,"receiverId":profile["receiverId"],"initialized":False,"producerEnrolled":False,"installerApplied":False,"activationAuthorized":False,"captureQualified":False})}

def permitted_mode(path,info):
    readonly="receiver/private/codex-home" in path
    return stat.S_IMODE(info.st_mode) in ({0o500,0o700} if readonly and stat.S_ISDIR(info.st_mode) else {0o400,0o600} if readonly else {0o700} if stat.S_ISDIR(info.st_mode) else {0o600})
def inventory(root):
    rows=[]
    for path in sorted(root.rglob("*")):
        rel=path.relative_to(root).as_posix(); info=path.lstat(); require(not path.is_symlink(),"inventory-link-refused")
        require(info.st_uid==os.getuid() and permitted_mode(rel,info),"inventory-custody-refused")
        if stat.S_ISDIR(info.st_mode): continue
        require(stat.S_ISREG(info.st_mode) and info.st_nlink==1 and info.st_size<=MAX_FILE,"inventory-file-refused")
        rows.append({"path":rel,"sha256":sha_file(path),"bytes":info.st_size,"mode":format(stat.S_IMODE(info.st_mode),"04o")}); require(len(rows)<=MAX_FILES,"inventory-count-refused")
    return rows

def write_layout(stage,profile,digest,token):
    for rel in ("receiver","receiver/private","receiver/private/credentials","receiver/private/evidence","receiver/private/codex-home","producer","producer/spool"): mkdir_new(stage/rel)
    for rel,data in initial_payloads(profile,digest,token).items(): write_new(stage/rel,data)
    sealed=inventory(stage); write_new(stage/"receiver/inventory.json",canonical({"schema":"fsgg.telemetry.persistent-receiver-inventory/2","profileSha256":digest,"sealedFiles":sealed}))

def verify_root(root,profile_digest):
    with HeldDirectory(root,"receiver-root") as held:
        current={x["path"]:x for x in inventory(held.proc)}
        state=load_json(held.proc/"receiver/state.json","state",True); require(state.get("schema")==STATE_SCHEMA and state.get("profileSha256")==profile_digest,"state-refused")
        manifest=load_json(held.proc/"receiver/inventory.json","inventory",True); require(manifest.get("schema")=="fsgg.telemetry.persistent-receiver-inventory/2" and manifest.get("profileSha256")==profile_digest,"inventory-refused")
        for expected in manifest.get("sealedFiles") or []: require(current.get(expected.get("path"))==expected,"receiver-drift-refused")
        held.assert_current(); return state

def initialize(profile,root,apply=False,token_factory=lambda:secrets.token_urlsafe(48),fault=None):
    digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"initialize","target":str(root)}
    require(root.is_absolute(),"root-absolute-required")
    with HeldDirectory(root.parent,"root-parent") as parent:
        name=safe_basename(root.name)
        if parent.child_exists(name):
            state=verify_root(parent.proc/name,digest); return {"schema":"fsgg.telemetry.persistent-receiver-initialization/2","status":"exact-replay","profileSha256":digest,"receiverId":state["receiverId"],"activationAuthorized":False}
        stage="receiver-initializing-"+secrets.token_hex(8); created=parent.create_child(stage)
        try:
            token=token_factory(); require(isinstance(token,str) and 48<=len(token)<=256 and "\n" not in token,"credential-generation-refused")
            with HeldDirectory(parent.proc/stage,"initialization-stage") as stage_dir: write_layout(stage_dir.proc,profile,digest,token)
            if callable(fault): fault(parent.path)
            elif fault=="before-commit": raise RuntimeError("injected interruption")
            parent.commit(stage,name,created); verify_root(parent.proc/name,digest)
            return {"schema":"fsgg.telemetry.persistent-receiver-initialization/2","status":"prepared","profileSha256":digest,"receiverId":profile["receiverId"],"hostStateInitialized":False,"producerEnrolled":False,"installerApplied":False,"activationAuthorized":False}
        except BaseException:
            parent.remove_created(stage,created); raise

def strict_json_bytes(data,label):
    require(isinstance(data,bytes) and len(data)<=MAX_FILE,f"{label}-size-refused")
    def closed_pairs(pairs):
        result={}
        for key,value in pairs:
            require(isinstance(key,str) and key not in result,f"{label}-duplicate-field-refused")
            result[key]=value
        return result
    try:
        text=data.decode("utf-8")
        return json.loads(text,object_pairs_hook=closed_pairs,parse_constant=lambda _value: (_ for _ in ()).throw(Refusal(f"{label}-constant-refused")))
    except Refusal: raise
    except (UnicodeDecodeError,json.JSONDecodeError) as error: raise Refusal(f"{label}-json-refused") from error

def exact_object(value,expected,label,types):
    require(isinstance(value,dict) and set(value)==set(expected),f"{label}-shape-refused")
    for key,expected_value in expected.items():
        require(type(value[key]) is types[key] and value[key]==expected_value,f"{label}-{key}-refused")

def validate_installer_outputs(profile,root):
    config=root/"receiver/private/host.json"; sidecar=config.with_name(config.name+".native-collector.json"); receipt=config.with_name(config.name+".native-collector.receipt.json")
    private_file(sidecar,"installer-sidecar"); private_file(receipt,"installer-receipt")
    sidecar_bytes=sidecar.read_bytes(); receipt_bytes=receipt.read_bytes(); p=profile["reader"]; cp=profile["containerPaths"]
    expected_sidecar={"Schema":INSTALL_SCHEMA,"CredentialReference":"learn-native-collector-v1","ExecutablePath":p["executablePath"],"CodexHome":cp["codexHome"],"EvidenceRoot":cp["evidence"],"Provider":p["provider"],"Model":p["model"],"Effort":p["effort"],"ExecutableSha256":p["executableSha256"]}
    exact_object(strict_json_bytes(sidecar_bytes,"installer-sidecar"),expected_sidecar,"installer-sidecar",{key:str for key in expected_sidecar})
    expected_receipt={"schema":"fsgg.telemetry.native-collector-installation-receipt/2","status":"installed","ownerUid":os.getuid(),"hostConfigSha256":sha_file(config),"sidecarSha256":sha_bytes(sidecar_bytes),"executableSha256":p["executableSha256"],"credentialReference":"learn-native-collector-v1","workspaceId":profile["workspaceId"],"producerId":profile["producerId"],"streamId":profile["streamId"],"grantId":"grant-learn-native-collector-v1","grantGeneration":1,"sourceVerification":"unknown","snapshotOrigin":"unknown","sharedCostCompleteness":"unknown","activationAuthorized":False}
    receipt_types={key:str for key in expected_receipt}; receipt_types.update({"ownerUid":int,"grantGeneration":int,"activationAuthorized":bool})
    exact_object(strict_json_bytes(receipt_bytes,"installer-receipt"),expected_receipt,"installer-receipt",receipt_types)

def sealed_source(root):
    source=root/"receiver/private/codex-home"; with_held=HeldDirectory(source,"native-source",readonly=True)
    try:
        rows=inventory(root); selected=[x for x in rows if x["path"].startswith("receiver/private/codex-home/")]
        require(selected,"native-source-empty-refused"); with_held.assert_current(); return selected
    finally: with_held.close()

def copy_tree(source,target):
    mkdir_new(target); readonly_directories=[]
    for path in sorted(source.rglob("*")):
        rel=path.relative_to(source); out=target/rel; info=path.lstat(); require(not path.is_symlink(),"copy-link-refused")
        if stat.S_ISDIR(info.st_mode):
            mkdir_new(out,0o700)
            if stat.S_IMODE(info.st_mode)==0o500: readonly_directories.append(out)
        else: private_file(path,"copy-input",readonly=stat.S_IMODE(info.st_mode)==0o400); write_new(out,path.read_bytes(),stat.S_IMODE(info.st_mode))
    for directory in reversed(readonly_directories): directory.chmod(0o500)

def acquire_host_lock(root):
    lock=root/"receiver/private/host.lock"; private_file(lock,"host-lock")
    fd=os.open(lock,os.O_RDWR|os.O_NOFOLLOW)
    try: fcntl.flock(fd,fcntl.LOCK_EX|fcntl.LOCK_NB)
    except BlockingIOError: os.close(fd); raise Refusal("host-writer-not-settled")
    return fd,os.fstat(fd)

def backup(profile,root,output,apply=False,fault=None,source_fault=None):
    digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"backup","source":str(root),"target":str(output),"requires":"actual exclusive Host lock and manager-generated installation outputs"}
    with HeldDirectory(root,"receiver-root") as held_root:
        verify_root(held_root.proc,digest); validate_installer_outputs(profile,held_root.proc)
        lock_fd,lock_info=acquire_host_lock(held_root.proc)
        try:
            source_before=sealed_source(held_root.proc); store=held_root.proc/"receiver/private/store"
            with HeldDirectory(store,"host-store") as held_store, HeldDirectory(output.parent,"backup-parent") as parent:
                name=safe_basename(output.name); require(not parent.child_exists(name),"backup-output-refused")
                stage="backup-writing-"+secrets.token_hex(8); created=parent.create_child(stage)
                try:
                    with HeldDirectory(parent.proc/stage,"backup-stage") as stage_dir: copy_tree(held_root.proc/"receiver",stage_dir.proc/"receiver")
                    if callable(source_fault): source_fault(held_root.proc/"receiver/private/codex-home")
                    require(source_before==sealed_source(held_root.proc),"native-source-changed-refused"); held_store.assert_current(); held_root.assert_current()
                    rows=inventory(parent.proc/stage)
                    binding={"lockDevice":lock_info.st_dev,"lockInode":lock_info.st_ino,"storeDevice":held_store.info.st_dev,"storeInode":held_store.info.st_ino,"exclusiveHostLockHeld":True}
                    write_new(parent.proc/stage/"backup.json",canonical({"schema":BACKUP_SCHEMA,"profileSha256":digest,"files":rows,"covers":["configuration","credentials","manager-sidecar","manager-receipt","quiesced-store","evidence","retained-native-source","source-references"],"quiescence":binding,"activationAuthorized":False}))
                    if callable(fault): fault(parent.path)
                    parent.commit(stage,name,created)
                    return {"schema":"fsgg.telemetry.persistent-receiver-backup-result/2","status":"created-from-exclusive-stopped-boundary","profileSha256":digest,"files":len(rows),"quiescence":binding}
                except BaseException:
                    parent.remove_created(stage,created); raise
        finally:
            fcntl.flock(lock_fd,fcntl.LOCK_UN); os.close(lock_fd)

def restore(profile,backup_root,target,apply=False,fault=None):
    digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"restore","source":str(backup_root),"target":str(target)}
    with HeldDirectory(backup_root,"backup-root") as held_backup:
        manifest=load_json(held_backup.proc/"backup.json","backup-manifest",True)
        require(set(manifest)=={"schema","profileSha256","files","covers","quiescence","activationAuthorized"} and manifest.get("schema")==BACKUP_SCHEMA and manifest.get("profileSha256")==digest and manifest.get("activationAuthorized") is False,"backup-manifest-refused")
        require(manifest["covers"]==["configuration","credentials","manager-sidecar","manager-receipt","quiesced-store","evidence","retained-native-source","source-references"],"backup-coverage-refused")
        quiescence=manifest["quiescence"]; require(isinstance(quiescence,dict) and set(quiescence)=={"lockDevice","lockInode","storeDevice","storeInode","exclusiveHostLockHeld"} and quiescence["exclusiveHostLockHeld"] is True and all(isinstance(quiescence[x],int) and quiescence[x]>0 for x in ("lockDevice","lockInode","storeDevice","storeInode")),"backup-quiescence-refused")
        actual=[x for x in inventory(held_backup.proc) if x["path"]!="backup.json"]; require(actual==manifest.get("files"),"backup-drift-refused")
        require(any(x["path"].startswith("receiver/private/codex-home/") for x in actual),"retained-native-source-missing")
        with HeldDirectory(target.parent,"restore-parent") as parent:
            name=safe_basename(target.name); require(not parent.child_exists(name),"restore-target-refused")
            stage="restore-writing-"+secrets.token_hex(8); created=parent.create_child(stage)
            try:
                with HeldDirectory(parent.proc/stage,"restore-stage") as stage_dir:
                    copy_tree(held_backup.proc/"receiver",stage_dir.proc/"receiver"); mkdir_new(stage_dir.proc/"producer"); mkdir_new(stage_dir.proc/"producer/spool")
                    state=load_json(stage_dir.proc/"receiver/state.json","restored-state",True); state.update({"activationAuthorized":False,"captureQualified":False}); (stage_dir.proc/"receiver/state.json").unlink(); write_new(stage_dir.proc/"receiver/state.json",canonical(state))
                    inv=stage_dir.proc/"receiver/inventory.json"; inv.unlink(); sealed=inventory(stage_dir.proc); write_new(inv,canonical({"schema":"fsgg.telemetry.persistent-receiver-inventory/2","profileSha256":digest,"sealedFiles":sealed}))
                if callable(fault): fault(parent.path)
                parent.commit(stage,name,created); verify_root(parent.proc/name,digest)
                return {"schema":"fsgg.telemetry.persistent-receiver-restore-result/2","status":"restored-inactive-with-retained-source","profileSha256":digest,"activationAuthorized":False}
            except BaseException:
                parent.remove_created(stage,created); raise

def parser():
    p=argparse.ArgumentParser(); p.add_argument("operation",choices=("inspect","initialize","backup","restore")); p.add_argument("--profile",type=pathlib.Path,required=True); p.add_argument("--root",type=pathlib.Path); p.add_argument("--backup",type=pathlib.Path); p.add_argument("--output",type=pathlib.Path); p.add_argument("--apply",action="store_true"); return p
def main(argv=None):
    args=parser().parse_args(argv); profile=load_json(args.profile,"profile")
    try:
        if args.operation=="inspect": result=plan(profile)
        elif args.operation=="initialize": require(args.root is not None,"root-required"); result=initialize(profile,args.root,args.apply)
        elif args.operation=="backup": require(args.root is not None and args.output is not None,"root-and-output-required"); result=backup(profile,args.root,args.output,args.apply)
        else: require(args.backup is not None and args.root is not None,"backup-and-root-required"); result=restore(profile,args.backup,args.root,args.apply)
        print(json.dumps(result,sort_keys=True,separators=(",",":"))); return 0
    except (Refusal,OSError,ValueError) as error: print(f"persistent-receiver-refused:{error}",file=sys.stderr); return 2
if __name__=="__main__": raise SystemExit(main())

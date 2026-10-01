#!/usr/bin/env python3
"""Effect-free inspection and explicitly applied private persistent receiver preparation."""
from __future__ import annotations
import argparse, hashlib, json, os, pathlib, re, secrets, shutil, stat, sys

PROFILE_SCHEMA="fsgg.telemetry.persistent-receiver-profile/1"
STATE_SCHEMA="fsgg.telemetry.persistent-receiver-state/1"
BACKUP_SCHEMA="fsgg.telemetry.persistent-receiver-backup/1"
CONFIG_SCHEMA="fsgg.telemetry.host-config/2"
INSTALL_SCHEMA="fsgg.telemetry.native-collector-installation/2"
HEX=re.compile(r"[0-9a-f]{64}")
IDENT=re.compile(r"[a-z][a-z0-9-]{2,63}")
PRIVATE_DIRS=("receiver/configuration","receiver/credentials","receiver/store","receiver/evidence","receiver/sidecar")
PRODUCER_DIRS=("producer/native-source","producer/spool")
BACKUP_PREFIXES=PRIVATE_DIRS+("receiver/source-references",)
MAX_FILE=16*1024*1024
MAX_FILES=10000

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
def private_directory(path):
    path.mkdir(mode=0o700,parents=False,exist_ok=False)
def safe_existing_directory(path,label):
    require(path.is_absolute(),f"{label}-absolute-required")
    info=path.lstat(); require(stat.S_ISDIR(info.st_mode) and not path.is_symlink(),f"{label}-directory-refused")
    require(info.st_uid==os.getuid() and stat.S_IMODE(info.st_mode)==0o700,f"{label}-custody-refused")
def private_file(path,label):
    info=path.lstat(); require(stat.S_ISREG(info.st_mode) and not path.is_symlink(),f"{label}-file-refused")
    require(info.st_uid==os.getuid() and stat.S_IMODE(info.st_mode)==0o600 and info.st_nlink==1,f"{label}-custody-refused")
    require(0<info.st_size<=MAX_FILE,f"{label}-size-refused")
def load_json(path,label,private=False):
    if private: private_file(path,label)
    else:
        info=path.lstat(); require(stat.S_ISREG(info.st_mode) and not path.is_symlink() and info.st_size<=MAX_FILE,f"{label}-refused")
    try: return json.loads(path.read_text())
    except (OSError,UnicodeDecodeError,json.JSONDecodeError) as error: raise Refusal(f"{label}-json-refused") from error

def validate_profile(profile):
    require(set(profile)=={"schema","receiverId","workspaceId","producerId","streamId","uid","gid","host","runtime","image","manager","reader","containerPaths"},"profile-shape-refused")
    require(profile["schema"]==PROFILE_SCHEMA,"profile-schema-refused")
    for key in ("receiverId","workspaceId","producerId","streamId"): require(isinstance(profile[key],str) and IDENT.fullmatch(profile[key]),f"profile-{key}-refused")
    require(profile["uid"]==32768 and profile["gid"]==32768,"profile-identity-refused")
    host=profile["host"]; require(set(host)=={"version","sourceSha","packageSha256","manifestSha256","journalSha256","payloadSha256"},"host-pins-shape-refused")
    require(host["version"]=="0.2.1" and re.fullmatch(r"[0-9a-f]{40}",host["sourceSha"]) is not None,"host-identity-refused")
    for key in ("packageSha256","manifestSha256","journalSha256","payloadSha256"): require(HEX.fullmatch(host[key]) is not None,f"host-{key}-refused")
    runtime=profile["runtime"]; require(set(runtime)=={"platform","baseImage"} and runtime["platform"]=="linux/amd64" and "@sha256:" in runtime["baseImage"] and HEX.fullmatch(runtime["baseImage"].rsplit("@sha256:",1)[1]) is not None,"runtime-pin-refused")
    image=profile["image"]; require(set(image)=={"containerfileSha256","resultDigestRequiredBeforeInstall","activation"} and HEX.fullmatch(image["containerfileSha256"]) is not None and image["resultDigestRequiredBeforeInstall"] is True and image["activation"]=="disabled-before-private-readback","image-pin-refused")
    manager=profile["manager"]; require(set(manager)=={"coordinationSource","command","installationSchema"} and re.fullmatch(r"[0-9a-f]{40}",manager["coordinationSource"]) is not None and manager["command"]=="install-native-collector" and manager["installationSchema"]==INSTALL_SCHEMA,"manager-pin-refused")
    reader=profile["reader"]; require(set(reader)=={"executablePath","executableSha256","provider","model","effort","profileSha256"},"reader-shape-refused")
    require(reader["executablePath"].startswith("/opt/fsgg/") and HEX.fullmatch(reader["executableSha256"]) and HEX.fullmatch(reader["profileSha256"]),"reader-pin-refused")
    require(reader["provider"]=="openai" and reader["model"]=="gpt-5.6-sol" and reader["effort"]=="medium","reader-profile-refused")
    paths=profile["containerPaths"]; require(set(paths)=={"configuration","credentials","store","evidence","sidecar","nativeSource","producerSpool"},"mount-shape-refused")
    values=list(paths.values()); require(len(values)==len(set(values)),"mount-collision-refused")
    for key,value in paths.items(): require(value.startswith("/receiver/") if key in {"configuration","credentials","store","evidence","sidecar"} else value.startswith("/producer/"),f"mount-scope-refused:{key}")
    return sha_bytes(canonical(profile))

def plan(profile):
    digest=validate_profile(profile)
    return {"schema":"fsgg.telemetry.persistent-receiver-plan/1","profileSha256":digest,"receiverId":profile["receiverId"],"effectsPerformed":False,"activationAuthorized":False,"mainRequired":False,"publishedHostReused":True,"host":profile["host"],"runtime":profile["runtime"],"image":profile["image"],"manager":profile["manager"],"reader":profile["reader"],"uid":profile["uid"],"gid":profile["gid"],"mounts":{"receiverPrivate":[profile["containerPaths"][x] for x in ("configuration","credentials","store","evidence","sidecar")],"developmentWritable":[profile["containerPaths"][x] for x in ("nativeSource","producerSpool")],"developmentForbidden":[profile["containerPaths"][x] for x in ("configuration","credentials","store","evidence","sidecar")]},"nextEffect":"root-owned image build/digest readback, permanent placement and private provisioning"}

def payloads(profile,profile_digest,token):
    cp=profile["containerPaths"]; reference="learn-native-collector-v1"; grant="grant-learn-native-collector-v1"
    credential={"Reference":reference,"SecretFile":cp["credentials"]+"/collector.token","WorkspaceId":profile["workspaceId"],"ProducerId":profile["producerId"],"StreamId":profile["streamId"],"Role":"native-collector","GrantId":grant,"GrantGeneration":1,"Revoked":False}
    config={"Schema":CONFIG_SCHEMA,"ListenUrl":"https://0.0.0.0:7443","CertificatePath":cp["credentials"]+"/receiver.pfx","CertificatePasswordFile":cp["credentials"]+"/certificate-password","ServiceLockPath":cp["store"]+"/host.lock","Stores":[{"WorkspaceId":profile["workspaceId"],"Root":cp["store"]}],"Credentials":[credential],"BrowserPrincipals":[],"BrowserSession":{"IdleSeconds":300,"AbsoluteSeconds":600,"MaximumSessions":2,"LoginAttemptsPerMinute":2,"LoginAdmission":1,"QueryAdmission":1,"QueryTimeoutSeconds":10}}
    install={"Schema":INSTALL_SCHEMA,"CredentialReference":reference,"ExecutablePath":profile["reader"]["executablePath"],"ExecutableSha256":profile["reader"]["executableSha256"],"CodexHome":"/receiver/configuration/codex-home","EvidenceRoot":cp["evidence"],"Provider":profile["reader"]["provider"],"Model":profile["reader"]["model"],"Effort":profile["reader"]["effort"]}
    refs={"schema":"fsgg.telemetry.persistent-source-references/1","profileSha256":profile_digest,"nativeSourceAnchor":cp["nativeSource"],"producerSpool":cp["producerSpool"],"readerProfileSha256":profile["reader"]["profileSha256"],"captureQualified":False}
    return {"receiver/configuration/host.json":canonical(config),"receiver/credentials/collector.token":(token+"\n").encode(),"receiver/sidecar/native-collector.json":canonical(install),"receiver/source-references/native-source.json":canonical(refs)}

def inventory(root, prefixes=None):
    records=[]
    starts=tuple(prefixes or ("receiver","producer"))
    for path in sorted(root.rglob("*")):
        rel=path.relative_to(root).as_posix()
        if not rel.startswith(starts): continue
        info=path.lstat(); require(not path.is_symlink(),"inventory-link-refused")
        if stat.S_ISDIR(info.st_mode): require(stat.S_IMODE(info.st_mode)==0o700,"inventory-directory-mode-refused"); continue
        require(stat.S_ISREG(info.st_mode) and info.st_nlink==1 and stat.S_IMODE(info.st_mode)==0o600,"inventory-file-custody-refused")
        require(info.st_size<=MAX_FILE,"inventory-file-size-refused")
        records.append({"path":rel,"sha256":sha_file(path),"bytes":info.st_size,"mode":"0600"})
        require(len(records)<=MAX_FILES,"inventory-count-refused")
    return records

def write_layout(stage,profile,profile_digest,token):
    private_directory(stage)
    for top in ("receiver","producer"): private_directory(stage/top)
    for rel in PRIVATE_DIRS+PRODUCER_DIRS+("receiver/source-references",): private_directory(stage/rel)
    for rel,data in payloads(profile,profile_digest,token).items(): write_new(stage/rel,data)
    state={"schema":STATE_SCHEMA,"profileSha256":profile_digest,"receiverId":profile["receiverId"],"initialized":True,"activationAuthorized":False,"captureQualified":False}
    write_new(stage/"receiver/state.json",canonical(state))
    manifest={"schema":"fsgg.telemetry.persistent-receiver-inventory/1","profileSha256":profile_digest,"files":inventory(stage)}
    write_new(stage/"receiver/inventory.json",canonical(manifest))

def verify_root(root,profile_digest):
    safe_existing_directory(root,"receiver-root")
    state=load_json(root/"receiver/state.json","state",True); require(state=={"schema":STATE_SCHEMA,"profileSha256":profile_digest,"receiverId":state.get("receiverId"),"initialized":True,"activationAuthorized":False,"captureQualified":False},"state-refused")
    manifest=load_json(root/"receiver/inventory.json","inventory",True); require(manifest.get("schema")=="fsgg.telemetry.persistent-receiver-inventory/1" and manifest.get("profileSha256")==profile_digest,"inventory-refused")
    # Runtime store/evidence and later owner-supplied TLS files are expected to
    # grow. Replay authenticates every initially sealed file while the full walk
    # below still refuses links, public modes, hardlinks and oversized files.
    current={row["path"]:row for row in inventory(root)}
    for expected in manifest.get("files") or []:
        require(current.get(expected.get("path"))==expected,"receiver-drift-refused")
    return state

def initialize(profile,root,apply=False,token_factory=lambda:secrets.token_urlsafe(48),fault=None):
    profile_digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"initialize","target":str(root)}
    require(root.is_absolute(),"root-absolute-required"); safe_existing_directory(root.parent,"root-parent")
    if root.exists() or root.is_symlink():
        state=verify_root(root,profile_digest); return {"schema":"fsgg.telemetry.persistent-receiver-initialization/1","status":"exact-replay","profileSha256":profile_digest,"receiverId":state["receiverId"],"activationAuthorized":False}
    stage=root.parent/("."+root.name+".initializing-"+secrets.token_hex(8))
    try:
        token=token_factory(); require(isinstance(token,str) and 48<=len(token)<=256 and "\n" not in token,"credential-generation-refused")
        write_layout(stage,profile,profile_digest,token)
        if fault=="before-commit": raise RuntimeError("injected interruption")
        os.rename(stage,root)
        parent_fd=os.open(root.parent,os.O_RDONLY|os.O_DIRECTORY)
        try: os.fsync(parent_fd)
        finally: os.close(parent_fd)
        verify_root(root,profile_digest)
        return {"schema":"fsgg.telemetry.persistent-receiver-initialization/1","status":"initialized","profileSha256":profile_digest,"receiverId":profile["receiverId"],"activationAuthorized":False}
    except BaseException:
        if stage.exists() and not stage.is_symlink(): shutil.rmtree(stage)
        raise

def copy_private_tree(source,target,prefixes):
    if target.exists(): safe_existing_directory(target,"copy-target")
    else: private_directory(target)
    wanted=[]
    for prefix in prefixes:
        src=source/prefix; safe_existing_directory(src,"backup-source")
        dst=target/prefix; dst.parent.mkdir(mode=0o700,parents=True,exist_ok=True)
        private_directory(dst)
        for path in sorted(src.rglob("*")):
            rel=path.relative_to(src); out=dst/rel; info=path.lstat(); require(not path.is_symlink(),"backup-link-refused")
            if stat.S_ISDIR(info.st_mode): private_directory(out)
            else:
                private_file(path,"backup-input"); write_new(out,path.read_bytes()); wanted.append(out.relative_to(target).as_posix())
    return wanted

def backup(profile,root,output,apply=False):
    digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"backup","source":str(root),"target":str(output)}
    verify_root(root,digest); require(output.is_absolute() and not output.exists() and not output.is_symlink(),"backup-output-refused"); safe_existing_directory(output.parent,"backup-parent")
    stage=output.parent/("."+output.name+".writing-"+secrets.token_hex(8))
    try:
        copy_private_tree(root,stage,BACKUP_PREFIXES)
        records=inventory(stage,BACKUP_PREFIXES)
        write_new(stage/"backup.json",canonical({"schema":BACKUP_SCHEMA,"profileSha256":digest,"files":records,"covers":["configuration","credentials","sidecar","store","evidence","native-source-references"]}))
        os.rename(stage,output); return {"schema":"fsgg.telemetry.persistent-receiver-backup-result/1","status":"created","profileSha256":digest,"files":len(records)}
    except BaseException:
        if stage.exists() and not stage.is_symlink(): shutil.rmtree(stage)
        raise

def restore(profile,backup_root,target,apply=False):
    digest=validate_profile(profile)
    if not apply: return {**plan(profile),"operation":"restore","source":str(backup_root),"target":str(target)}
    safe_existing_directory(backup_root,"backup-root"); manifest=load_json(backup_root/"backup.json","backup-manifest",True)
    require(manifest.get("schema")==BACKUP_SCHEMA and manifest.get("profileSha256")==digest and manifest.get("covers")==["configuration","credentials","sidecar","store","evidence","native-source-references"],"backup-manifest-refused")
    actual=inventory(backup_root,BACKUP_PREFIXES); require(actual==manifest.get("files"),"backup-drift-refused")
    require(target.is_absolute() and not target.exists() and not target.is_symlink(),"restore-target-refused"); safe_existing_directory(target.parent,"restore-parent")
    stage=target.parent/("."+target.name+".restoring-"+secrets.token_hex(8))
    try:
        private_directory(stage); private_directory(stage/"receiver"); private_directory(stage/"producer")
        # Restore receiver-owned data, then recreate empty producer writes. The development container never receives receiver mounts.
        for prefix in BACKUP_PREFIXES: copy_private_tree(backup_root,stage,(prefix,))
        for rel in PRODUCER_DIRS: private_directory(stage/rel)
        state={"schema":STATE_SCHEMA,"profileSha256":digest,"receiverId":profile["receiverId"],"initialized":True,"activationAuthorized":False,"captureQualified":False}
        write_new(stage/"receiver/state.json",canonical(state))
        records=inventory(stage); write_new(stage/"receiver/inventory.json",canonical({"schema":"fsgg.telemetry.persistent-receiver-inventory/1","profileSha256":digest,"files":records}))
        os.rename(stage,target); verify_root(target,digest)
        return {"schema":"fsgg.telemetry.persistent-receiver-restore-result/1","status":"restored","profileSha256":digest,"activationAuthorized":False}
    except BaseException:
        if stage.exists() and not stage.is_symlink(): shutil.rmtree(stage)
        raise

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
    except (Refusal,OSError,ValueError) as error:
        print(f"persistent-receiver-refused:{error}",file=sys.stderr); return 2
if __name__=="__main__": raise SystemExit(main())

#!/usr/bin/env python3
"""Fixed mechanism adapter; the compiled F# producer owns all decisions."""
import argparse,json,os,re,selectors,signal,subprocess,tempfile,time,urllib.parse
from pathlib import Path
SCHEMA="fsgg.telemetry.host-attempt-actions/1";REPOSITORY="FS-GG/FS.GG.GitHub.Substrate.Sandbox";WORKFLOW="v2-host-native-private.yml";BRANCH="qualification/v2-host-native-20260930";RECIPE_SHA="8ad0da67004d670c6803f34755dfe759a7fc84e7";ENVIRONMENT="v2-host-01-8-native-private"
SECRETS={"native-auth":"FSGG_V2_HOST_018_NATIVE_AUTH_JSON_B64","effect-admission":"FSGG_V2_HOST_018_EFFECT_ADMISSION"}
EFFECTS={"request-branch-fast-forward","transfer-secret","dispatch-once","cancel-owned-run","delete-secret"}
LIMITS={"read-public-identity":60,"request-branch-fast-forward":60,"inspect-auth-metadata":45,"invoke-binding":90,"transfer-secret":45,"dispatch-once":60,"list-runs":60,"get-run":60,"cancel-owned-run":60,"delete-secret":45,"read-secret-absence":45,"download-run-artifacts":120,"emit-root-readback":30}
class Refusal(Exception):pass
def pairs(values):
 out={}
 for key,value in values:
  if key in out:raise Refusal("json-duplicate-key-refused")
  out[key]=value
 return out
def load(path,maximum):
 p=Path(path)
 if not p.is_file() or p.is_symlink() or not 0<p.stat().st_size<=maximum:raise Refusal("input-refused")
 return json.loads(p.read_text(),object_pairs_hook=pairs)
def exact(value,keys):
 if type(value) is not dict or set(value)!=set(keys):raise Refusal("json-fields-refused")
def write_new(path,value):
 target=Path(path).resolve()
 if not target.parent.is_dir() or target.exists():raise Refusal("output-refused")
 data=(json.dumps(value,sort_keys=True,separators=(",",":"))+"\n").encode();fd,name=tempfile.mkstemp(prefix=target.name+".",dir=target.parent)
 try:os.fchmod(fd,0o600);os.write(fd,data);os.fsync(fd);os.close(fd);os.link(name,target);os.unlink(name)
 except BaseException:
  try:os.close(fd)
  except OSError:pass
  try:os.unlink(name)
  except OSError:pass
  raise
def action_command(action,context,sensitive_input=None):
 exact(action,["name","argument"]);name=action["name"];argument=action["argument"]
 if name not in LIMITS:raise Refusal("action-refused")
 if argument is not None and (type(argument)is not str or not 0<len(argument)<=64):raise Refusal("action-argument-refused")
 if name=="read-public-identity":return ["gh","api","--include",f"repos/{REPOSITORY}"],None
 if name=="request-branch-fast-forward":return ["gh","api","--method","PATCH",f"repos/{REPOSITORY}/git/refs/heads/{BRANCH}","-f",f"sha={context['placementSha']}","-F","force=false"],None
 if name=="inspect-auth-metadata":return ["gh","api","--include",f"repos/{REPOSITORY}/environments/{ENVIRONMENT}/secrets?per_page=100&page=1"],None
 if name=="invoke-binding":return ["/usr/bin/dotnet",context["hostBindingDll"],"render","--source-root",context["recipeRoot"],"--source-sha",context["recipeSha"],"--profile",context["profilePath"],"--source-pins",context["sourcePinsPath"]],None
 if name=="transfer-secret":
  if argument not in SECRETS:raise Refusal("secret-role-refused")
  value=sensitive_input if argument=="effect-admission" else os.environ.get(SECRETS[argument])
  if not value or "\n" in value or "\r" in value:raise Refusal("secret-value-missing")
  return ["gh","secret","set",SECRETS[argument],"--repo",REPOSITORY,"--env",ENVIRONMENT],value
 if name=="dispatch-once":return ["gh","workflow","run",WORKFLOW,"--repo",REPOSITORY,"--ref",BRANCH,"-f",f"placement_sha={context['placementSha']}","-f",f"run_nonce={context['nonce']}"],None
 if name=="list-runs":
  start=context.get("discoveryStart")
  if type(start)is not str or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z",start):raise Refusal("discovery-start-refused")
  return ["gh","api","--include",f"repos/{REPOSITORY}/actions/workflows/{WORKFLOW}/runs?branch={BRANCH}&event=workflow_dispatch&created=%3E%3D{urllib.parse.quote(start,safe='')}&per_page=20&page=1"],None
 if name in {"get-run","cancel-owned-run","download-run-artifacts"}:
  if argument!=context.get("ownedRunId"):raise Refusal("owned-run-refused")
  if name=="get-run":return ["gh","api","--include",f"repos/{REPOSITORY}/actions/runs/{argument}"],None
  if name=="cancel-owned-run":return ["gh","run","cancel",argument,"--repo",REPOSITORY],None
  return ["gh","run","download",argument,"--repo",REPOSITORY,"--dir",context["artifactOutput"]],None
 if name in {"delete-secret","read-secret-absence"}:
  if argument not in SECRETS:raise Refusal("secret-role-refused")
  if name=="delete-secret":return ["gh","secret","delete",SECRETS[argument],"--repo",REPOSITORY,"--env",ENVIRONMENT],None
  return ["gh","api","--include",f"repos/{REPOSITORY}/environments/{ENVIRONMENT}/secrets?per_page=100&page=1"],None
 if name=="emit-root-readback":raise Refusal("readback-owned-by-fsharp")
 raise Refusal("action-refused")
def bounded_run(command,input=None,text=True,capture_output=True,timeout=1,check=False):
 final_deadline=time.monotonic()+timeout
 deadline=final_deadline-min(0.1,max(0.001,timeout/2))
 payload=None if input is None else input.encode()
 if payload is not None and len(payload)>1048576:raise Refusal("transport-input-limit")
 process=subprocess.Popen(command,stdin=subprocess.PIPE if input is not None else subprocess.DEVNULL,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=False,start_new_session=True)
 selected=selectors.DefaultSelector();selected.register(process.stdout,selectors.EVENT_READ,"stdout");selected.register(process.stderr,selectors.EVENT_READ,"stderr");data={"stdout":bytearray(),"stderr":bytearray()};caps={"stdout":1048576,"stderr":65536};written=0
 if payload:
  os.set_blocking(process.stdin.fileno(),False);selected.register(process.stdin,selectors.EVENT_WRITE,"stdin")
 elif payload is not None:process.stdin.close()
 try:
  while selected.get_map():
   remaining=deadline-time.monotonic()
   if remaining<=0:raise subprocess.TimeoutExpired(command,timeout)
   for key,_ in selected.select(remaining):
    if key.data=="stdin":
     try:count=os.write(key.fileobj.fileno(),payload[written:written+65536])
     except BlockingIOError:continue
     written+=count
     if written==len(payload):selected.unregister(key.fileobj);key.fileobj.close()
     continue
    chunk=os.read(key.fileobj.fileno(),65536)
    if not chunk:selected.unregister(key.fileobj);continue
    data[key.data].extend(chunk)
    if len(data[key.data])>caps[key.data]:raise Refusal("transport-output-limit")
  remaining=deadline-time.monotonic()
  if remaining<=0:raise subprocess.TimeoutExpired(command,timeout)
  code=process.wait(timeout=remaining)
  return subprocess.CompletedProcess(command,code,data["stdout"].decode(errors="strict"),data["stderr"].decode(errors="strict"))
 except BaseException:
  try:os.killpg(process.pid,signal.SIGKILL)
  except OSError:pass
  try:process.wait(timeout=max(0,final_deadline-time.monotonic()))
  except subprocess.TimeoutExpired:raise Refusal("transport-settlement-unknown")
  raise
def run_fixed(command,name,runner,stdin,timeout):
 return runner(command,input=stdin,text=True,capture_output=True,timeout=timeout,check=False)
def run_actions(actions,context,lease_path,runner=bounded_run,sensitive_input=None,allow_batch=False):
 exact(actions,["schema","actions"])
 if actions["schema"]!=SCHEMA or type(actions["actions"])is not list or len(actions["actions"])<1 or (len(actions["actions"])!=1 and not allow_batch):raise Refusal("actions-refused")
 results=[];lease=Path(lease_path).resolve()
 if lease.exists():raise Refusal("lease-exists-refused")
 for index,action in enumerate(actions["actions"]):
  command,stdin=action_command(action,context,sensitive_input);name=action["name"]
  if name in EFFECTS:write_new(Path(str(lease)+f".{index}.json"),{"schema":"fsgg.telemetry.host-attempt-intention/1","action":action,"mayHaveEffect":True,"sequence":index})
  timeout=max(1,min(LIMITS[name],context["remainingSeconds"]))
  try:
   completed=run_fixed(command,name,runner,stdin,timeout);stdout=completed.stdout or "";stderr=completed.stderr or ""
   if len(stdout.encode())>1048576 or len(stderr.encode())>65536:raise Refusal("transport-output-limit")
   results.append({"name":name,"argument":action["argument"],"exitCode":completed.returncode,"stdout":stdout,"stderr":stderr,"outcome":"returned"})
  except subprocess.TimeoutExpired:results.append({"name":name,"argument":action["argument"],"exitCode":None,"stdout":"","stderr":"","outcome":"unknown"})
 return {"schema":"fsgg.telemetry.host-attempt-transport-results/1","results":results}
def emergency_cleanup(context,lease_path,runner=bounded_run):
 items=[]
 for index,(name,role) in enumerate([("delete-secret","native-auth"),("delete-secret","effect-admission"),("read-secret-absence","native-auth"),("read-secret-absence","effect-admission")]):
  try:items.extend(run_actions({"schema":SCHEMA,"actions":[{"name":name,"argument":role}]},context,str(lease_path)+f".{index}",runner)["results"])
  except Exception:items.append({"name":name,"argument":role,"exitCode":None,"stdout":"","stderr":"","outcome":"unknown"})
 return {"schema":"fsgg.telemetry.host-attempt-transport-results/1","results":items}
def context(path):
 value=load(path,65536);exact(value,["schema","placementSha","nonce","hostBindingDll","recipeRoot","recipeSha","profilePath","sourcePinsPath","bindingSha256","ownedRunId","artifactOutput","discoveryStart","remainingSeconds"])
 if value["schema"]!="fsgg.telemetry.host-attempt-transport-context/2":raise Refusal("context-schema-refused")
 for name in ["placementSha","nonce","hostBindingDll","recipeRoot","recipeSha","profilePath","sourcePinsPath","bindingSha256","artifactOutput"]:
  if type(value[name])is not str:raise Refusal("context-scalar-refused")
 if type(value["remainingSeconds"])is not int or not 1<=value["remainingSeconds"]<=2700:raise Refusal("context-time-refused")
 if value["discoveryStart"] is not None and (type(value["discoveryStart"])is not str or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z",value["discoveryStart"])):raise Refusal("context-discovery-refused")
 if not re.fullmatch(r"[0-9a-f]{40}",value["placementSha"]) or value["recipeSha"]!=RECIPE_SHA:raise Refusal("context-source-refused")
 if not re.fullmatch(r"[0-9a-f]{64}",value["bindingSha256"]) or not re.fullmatch(r"[a-z0-9][a-z0-9-]{7,63}",value["nonce"]):raise Refusal("context-identity-refused")
 if value["ownedRunId"]is not None and(type(value["ownedRunId"])is not str or not 0<len(value["ownedRunId"])<=32):raise Refusal("context-run-refused")
 for name in ["hostBindingDll","recipeRoot","profilePath","sourcePinsPath","artifactOutput"]:
  if not Path(value[name]).is_absolute():raise Refusal("context-path-refused")
 return value
def main():
 parser=argparse.ArgumentParser();parser.add_argument("--actions",required=True);parser.add_argument("--context",required=True);parser.add_argument("--lease",required=True);parser.add_argument("--output",required=True);parser.add_argument("--sensitive-stdin",action="store_true");a=parser.parse_args();interrupted=lambda *_:(_ for _ in()).throw(InterruptedError());signal.signal(signal.SIGTERM,interrupted);value=None
 try:
  value=context(a.context);sensitive=None
  if a.sensitive_stdin:
   sensitive=os.read(0,66).decode().rstrip("\n")
   if not re.fullmatch(r"[0-9a-f]{64}",sensitive):raise Refusal("sensitive-input-refused")
  write_new(a.output,run_actions(load(a.actions,65536),value,a.lease,sensitive_input=sensitive))
 except(KeyboardInterrupt,InterruptedError):
  if value is not None:
   try:emergency_cleanup(value,a.lease+".emergency")
   except Exception:pass
  raise SystemExit("host-attempt-transport-interrupted-cleanup-unconfirmed")
 except(Refusal,json.JSONDecodeError,OSError,UnicodeError)as error:raise SystemExit("host-attempt-transport-refused:"+str(error))
if __name__=="__main__":main()

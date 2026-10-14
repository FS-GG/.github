#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
# Static source-fixture prerequisite before compiler/model qualification.
test -x /usr/bin/docker || { printf 'FAIL inactive source fixture requires hosted Docker\n' >&2; exit 1; }
itf_root="$(mktemp -d)"
custody_source_started=false
custody_cleanup() {
  local custody_exit_code=$?
  if test "${FSGG_LEARN_NATIVE_FIXTURE:-false}" = true && test -f "$itf_root/native-fixture.log"; then
    local native_evidence="${RUNNER_TEMP:-$itf_root}/learn-native-custody-evidence"
    mkdir -p -- "$native_evidence"
    # Only the fixed controller's bounded diagnostics enter this artifact.
    if test "$(wc -c < "$itf_root/native-fixture.log")" -le 131072; then
      cp -- "$itf_root/native-fixture.log" "$native_evidence/native-fixture.log"
    else
      printf 'FAIL native diagnostic artifact bound\n' >&2
      custody_exit_code=1
    fi
  fi
  if test "$custody_exit_code" -eq 0 || test "$custody_source_started" = false; then
    rm -rf -- "$itf_root"
  else
    printf "FAIL retaining source fixture root after failed/unknown qualification: %s\n" "$itf_root" >&2
  fi
  return "$custody_exit_code"
}
trap custody_cleanup EXIT
# Source qualification requires an independently accepted provider/toolchain
# closure. Hash equality below checks that selection; it does not authenticate it.
# The existing root source-operation admission supplies trust and outer resource
# custody. Missing closure inputs refuse before compiler or test invocation.
custody_compiler="${FSGG_LEARN_CUSTODY_CORE_CC:?root-selected C compiler required}"
custody_compiler_sha="${FSGG_LEARN_CUSTODY_CORE_CC_SHA256:?compiler byte pin required}"
custody_toolchain="${FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256_FILE:?accepted complete toolchain closure required}"
custody_toolchain_sha="${FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256:?accepted closure manifest pin required}"
[[ "$custody_compiler" = /* && "$custody_toolchain" = /* ]]
[[ "$custody_compiler_sha" =~ ^[a-f0-9]{64}$ && "$custody_toolchain_sha" =~ ^[a-f0-9]{64}$ ]]
printf '%s  %s\n' "$custody_toolchain_sha" "$custody_toolchain" | sha256sum --check --strict
grep -Fx -- "$custody_compiler_sha  $custody_compiler" "$custody_toolchain"
sha256sum --check --strict "$custody_toolchain"
if test "${GITHUB_ACTIONS:-}" = true; then
  test "${RUNNER_ENVIRONMENT:-}" = github-hosted
  test "${RUNNER_OS:-}" = Linux
  test "${RUNNER_ARCH:-}" = X64
  custody_image="${FSGG_LEARN_CUSTODY_CORE_IMAGE_VERSION:?root-selected immutable hosted image required}"
  test "${ImageVersion:-}" = "$custody_image"
  printf 'custody-core hosted-image=%s compiler=%s closure=%s\n' "$custody_image" "$custody_compiler_sha" "$custody_toolchain_sha"
fi
custody_source="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/native/custody_core.c"
custody_adapter_tests="$repo_root/tests/telemetry-collector-persistent-v3-image-closure/CustodyAdapterTests.c"
custody_tests="$repo_root/tests/telemetry-collector-persistent-v3-image-closure/CustodyCoreTests.c"
# One original deadline covers four serial compiles and ordinary C controls.
# The enclosing admitted source profile must separately enforce RSS/census and
# complete descendant retirement; timeout alone supplies no custody acceptance.
# The maintained owner predicates below run only against mocked kernel and
# child inputs. They qualify source semantics, never process/cgroup custody.
/usr/bin/timeout --kill-after=5s 15s /usr/bin/python3 -I -S - "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/native-preflight/owned_install_guards.py" 20bb67be5004feb023499b7393f4352602010ddfb1e61eef086ec7e0496a5fc0 <<'PY_OWNER_SOURCE'
# Existing source qualification only: actual methods, mocked kernel/process inputs.
import array,hashlib,json,os,pathlib,socket,struct,sys,tempfile,time,types
from unittest.mock import patch
path=pathlib.Path(sys.argv[1])
with path.open('rb') as stream:source=stream.read(65537)
if len(source)>65536:raise RuntimeError('source control owner byte bound')
if hashlib.sha256(source).hexdigest()!=sys.argv[2]:raise RuntimeError('source control owner pin')
m=types.ModuleType('maintained_owner_source_controls');exec(compile(source,str(path),'exec'),m.__dict__)
count=0

def check(value):
    global count
    if not value:raise AssertionError('owner source control '+str(count))
    count+=1

def refused(call,reason):
    try:call()
    except (RuntimeError,TimeoutError) as e:check(reason in str(e))
    else:raise AssertionError('expected '+reason)

# Root-selected all-thread resource predicates: causal mocked controls, no native effects.
resource_row={'pid':42,'tid':43,'processStart':100,'threadStart':101,'uid':1000,'exeSha256':'a'*64,'cgroup':'0::/runtime\n','affinity':[0,1,2]}
resource_row['credentials']=m.resource_credentials(b'Uid: 1000 1000 1000 1000\nGid: 1000 1000 1000 1000\nGroups: 1000\nCapInh: 0\nCapPrm: 0\nCapEff: 0\nCapBnd: 0\nCapAmb: 0\nNoNewPrivs: 0\nSeccomp: 2\n')
resource_profile={'cpu':1,'ambient':[],'heldBirths':[],'excludedCpus':[0,2,6,8,9,12,13,14,15,19,20,21,22,23],'rootAuthenticated':True}
resource_admission={'startedAtEpoch':100,'absoluteDeadline':700,'launchBeforeEpoch':160,'originalMonotonicDeadline':1000}
def resources(profile=resource_profile,rows=None,wall=110,mono=900):
    return m.admit_thread_resources(profile,resource_admission,census_reader=lambda:[resource_row] if rows is None else rows,clock=lambda:mono,wall=lambda:wall)
refused(lambda:resources(),'unadmitted-affinity-intersections')
refused(lambda:resources(dict(resource_profile,rootAuthenticated=False)),'root-selection')
refused(lambda:resources(dict(resource_profile,cpu=0)),'held-cpu')
refused(lambda:resources(wall=160),'original-deadline')
refused(lambda:resources(mono=1000),'original-deadline')
refused(lambda:resources(rows=[]),'population-bound')
refused(lambda:resources(rows=[resource_row,resource_row]),'affinity-shape')
refused(lambda:resources(dict(resource_profile,heldBirths=[{'pid':42,'processStart':100}])),'held-birth-intersects')
with tempfile.TemporaryDirectory() as temporary:
    provenance=pathlib.Path(temporary)/'root-selected-input';provenance.write_bytes(b'independently reviewed root input')
    selected={k:v for k,v in resource_row.items() if k not in ['affinity','credentials']}
    selected['provenance']={'path':str(provenance),'bytes':provenance.stat().st_size,'sha256':hashlib.sha256(provenance.read_bytes()).hexdigest()}
    profile=dict(resource_profile,ambient=[selected])
    result=resources(profile);check(result['sharedAmbient']==[resource_row] and result['cpuExclusive'] is False and result['effectGranted'] is False)
    check(resources(dict(profile,cpu=0,excludedCpus=[]))['cpuExclusive'] is False) # Shared helper has no programme-global CPU assumption.
    for field,value in [('threadStart',102),('processStart',102),('uid',0),('exeSha256','b'*64),('cgroup','0::/other\n')]:
        refused(lambda:resources(profile,rows=[dict(resource_row,**{field:value},credentials=({**resource_row['credentials'],'Uid':('1000',str(value),'1000','1000')} if field=='uid' else resource_row['credentials']))]),'unadmitted-affinity-intersections')
    check(resources(profile,rows=[dict(resource_row,affinity=[1])])['cpuExclusive'] is False)
    refused(lambda:resources(profile,rows=[resource_row,dict(resource_row,pid=55,tid=56)]),'unadmitted-affinity-intersections')
    sequence=iter([[resource_row],[dict(resource_row,affinity=[1])]])
    refused(lambda:m.admit_thread_resources(profile,resource_admission,census_reader=lambda:next(sequence),clock=lambda:900,wall=lambda:110),'census-unstable')
    sequence=iter([[resource_row],[dict(resource_row,credentials={**resource_row['credentials'],'Uid':('1001','1000','1000','1000')})]])
    refused(lambda:m.admit_thread_resources(profile,resource_admission,census_reader=lambda:next(sequence),clock=lambda:900,wall=lambda:110),'census-unstable')
    provenance.write_bytes(b'changed')
    refused(lambda:resources(profile),'provenance-extent')
# Exercise the actual census against status-counter churn, not a duplicate predicate.
import io
credential_status=b'Uid:\t1000 1000 1000 1000\nGid:\t1000 1000 1000 1000\nGroups:\t1000\nCapInh:\t0\nCapPrm:\t0\nCapEff:\t0\nCapBnd:\t0\nCapAmb:\t0\nNoNewPrivs:\t0\nSeccomp:\t2\n'
class ResourceStream(io.BytesIO):
    def fileno(self):return 777
metadata=types.SimpleNamespace(st_dev=1,st_ino=2,st_size=3,st_mtime_ns=4,st_ctime_ns=5,st_mode=0o100444)
def census_fixture(change_uid=False,oversize=False,zombie=False,zombie_drift=False,no_exe=False):
    reads={'task':0,'counter':0};events=[]
    def listing(path):
        if str(path)=='/proc':return iter([pathlib.Path('/proc/42')])
        if str(path)=='/proc/42/task':return iter([pathlib.Path('/proc/42/task/43')])
        raise AssertionError('unexpected population')
    def birth(pid,tid=None):
        if tid is not None:events.append('birth')
        return {'pid':pid,'tid':pid if tid is None else tid,'start':100 if tid is None else 101,'ppid':30,'pgid':42,'sid':42}
    def opening(path,*a,**k):
        if path.name=='exe':
            if zombie:raise AssertionError('zombie executable must stay absent')
            if no_exe:raise RuntimeError('live executable unavailable')
            return ResourceStream(b'abc')
        if path.name=='cgroup':return ResourceStream(b'0::/runtime\n')
        if path.name=='status':
            reads['counter']+=1;body=credential_status
            if '/task/' in str(path):
                events.append('status');reads['task']+=1
                if change_uid and reads['task']==2:body=body.replace(b'1000 1000 1000 1000',b'1000 1001 1000 1000',1)
            return ResourceStream(body+b'VmRSS:\t'+str(reads['counter']).encode()+b' kB\nvoluntary_ctxt_switches:\t'+str(reads['counter']).encode()+b'\n')
        raise AssertionError('unexpected read '+str(path))
    states=iter(['Z','S']) if zombie_drift else None
    def state(pid,tid=None):return next(states) if states is not None else ('Z' if zombie else 'S')
    selected=metadata if not oversize else types.SimpleNamespace(**dict(vars(metadata),st_size=536870913))
    with patch.object(pathlib.Path,'iterdir',listing),patch.object(pathlib.Path,'open',opening),patch.object(m,'resource_birth',birth),patch.object(m,'resource_state',state),patch.object(m.os,'fstat',return_value=selected),patch.object(m.os,'stat',return_value=selected),patch.object(m.os,'sched_getaffinity',return_value={0,1,2}):
        result=m.resource_thread_census(1000,clock=lambda:900)
    check(events.index('birth')<events.index('status'))
    return result
check(census_fixture()==[dict(resource_row,exeSha256=hashlib.sha256(b'abc').hexdigest())])
refused(lambda:census_fixture(change_uid=True),'thread-drift')
refused(lambda:census_fixture(oversize=True),'executable-bound')
refused(lambda:census_fixture(no_exe=True),'live executable unavailable')
refused(lambda:census_fixture(zombie=True,zombie_drift=True),'zombie-drift')
zombie_row=dict(resource_row,state='Z',exeSha256=None)
check(census_fixture(zombie=True)==[zombie_row])
result=resources(rows=[zombie_row]);check(result['nonExecuting']==[zombie_row] and not result['intersections'] and result['effectGranted'] is False)
result=resources(dict(resource_profile,heldBirths=[{'pid':42,'processStart':100}]),rows=[zombie_row])
check(result['nonExecuting']==[zombie_row] and result['cpuExclusive'] is False) # Observation does not retire the held birth.
refused(lambda:resources(rows=[dict(zombie_row,exeSha256='a'*64)]),'zombie-executable')
refused(lambda:resources(rows=[dict(zombie_row,state='S')]),'closed-shape')
refused(lambda:resources(rows=[dict(resource_row,exeSha256=None)]),'identity-source')
sequence=iter([[zombie_row],[resource_row]])
refused(lambda:m.admit_thread_resources(resource_profile,resource_admission,census_reader=lambda:next(sequence),clock=lambda:900,wall=lambda:110),'census-unstable')
# Birth/source/provenance matches cannot authenticate an unrelated broad-affinity process.
row={'pid':42,'start':100,'state':'S','rss':4096,'ppid':30,'pgid':42}
check(m.inner_live_join(row,row,True,True,True)==row)
for before,after,a,b,member,reason in [(None,row,True,True,True,'unavailable'),(row,None,True,True,True,'unavailable'),(row,row,False,True,True,'unavailable'),(row,row,True,False,True,'unavailable'),(dict(row,state='Z'),row,True,True,True,'zombie'),(row,dict(row,start=101),True,True,True,'changed'),(row,row,True,True,False,'unjoined')]:
    refused(lambda:m.inner_live_join(before,after,a,b,member),reason)
check(sum(r['rss'] for r in m.inner_union_rows([row],[row]))==4096)
check(sum(r['rss'] for r in m.inner_union_rows([row],[dict(row,rss=8192)]))==8192)
refused(lambda:m.inner_union_rows([row],[dict(row,start=101)]),'pid-reuse')

J=m.InnerCgroupJoin
j=J.__new__(J);j.joined=True;j.operation_drained=False;j.aggregate_drained=False
record={'fd':20,'birth':row,'role':'command'};j.records={(42,100):record};j.controller=None;j.identities={42:100};j.held=[20];j._live=lambda fd:True
seen=[];j._member=lambda r,roles,**kwargs:(seen.append((r,roles)) or (row,'command'))
check(j.classify([row])==[] and len(seen)==1)
foreign=[dict(row,pid=43,uid=1001,path='/sys/fs/cgroup/system.slice/owned-prefix-foreign'),dict(row,pid=44,uid=0),dict(row,start=101)]
check(j.classify(foreign)==foreign)
j.aggregate_drained=True;check(j.classify([row])==[row])
j.controller=record;check(j.classify([row])==[])

j=J.__new__(J);j._proc=lambda pid,**kwargs:row;j._live=lambda fd:True
j._members=lambda role:{42};check(j._member(record,('command',))==(row,'command'))
reads=iter([{42},set()]);j._members=lambda role:next(reads)
refused(lambda:j._member(record,('command',)),'unjoined')
j._members=lambda role:{42};j._live=lambda fd:False
refused(lambda:j._member(record,('command',)),'unavailable')
j._live=lambda fd:True;j._proc=lambda pid,**kwargs:dict(row,start=101)
refused(lambda:j._member(record,('command',)),'retained-birth-changed')

j=J.__new__(J);j.joined=True;j.failed=True;j.operation_drained=False;j.active=None;j.case=-1
message={'version':1,'kind':'CASE','nonce':'a'*64,'ordinal':0,'roles':['requesterPidfd','abortSocket']}
refused(lambda:j._case(message,[10,11],(42,0,0)),'case-order')
j.failed=False
for changed,fds in [(dict(message,ordinal=1),[10,11]),(dict(message,ordinal=True),[10,11]),(dict(message,roles=['abortSocket','requesterPidfd']),[10,11]),(message,[10])]:
    refused(lambda:j._case(changed,fds,(42,0,0)),'case-order')

j=J.__new__(J);j.failed=False;j.first=None;j.extra=[]
class Abort:
    def __init__(self):self.calls=[]
    def shutdown(self,how):self.calls.append(how)
a=Abort();j.active={'abort':a}
j.abort(RuntimeError('original-first'));j.abort(RuntimeError('later'))
check(j.failed and j.first=='RuntimeError: original-first' and a.calls==[socket.SHUT_RDWR,socket.SHUT_RDWR])
j.joined=True;j.operation_drained=False;j.case=-1
refused(lambda:j._case(message,[10,11],(42,0,0)),'case-order')

# Invalid earlier ancillary must not hide rights in a later entry.
class Receiver:
    def __init__(self,packet):self.packet=packet
    def recvmsg(self,*args):p=self.packet;self.packet=None;return p

def received(packet,reason):
    j=J.__new__(J);j.socket=Receiver(packet);j.held=[];j.messages=0;j.aggregate_drained=False;j.selected={'nonce':'a'*64}
    j._time=lambda work:None;j._hold=lambda fd:j.held.append(fd)
    j._join=lambda *args:(_ for _ in ()).throw(RuntimeError('test-shape-refusal'))
    closed=[]
    with patch.object(m.os,'close',side_effect=closed.append):refused(j.receive,reason)
    return closed,j
rights=(socket.SOL_SOCKET,socket.SCM_RIGHTS,array.array('i',[91,92]).tobytes())
cred=(socket.SOL_SOCKET,socket.SCM_CREDENTIALS,struct.pack('3i',42,0,0))
closed,j=received((b'{}',[(socket.SOL_SOCKET,999,b'x'),rights],0,None),'ancillary')
check(closed==[91,92] and j.held==[])
closed,j=received((b'{}',[rights,cred],socket.MSG_CTRUNC,None),'bound')
check(closed==[91,92])
closed,j=received((json.dumps({'version':True,'nonce':'a'*64,'kind':'JOIN'}).encode(),[rights,cred],0,None),'challenge')
check(closed==[91,92])
closed,j=received((json.dumps({'version':1,'nonce':'b'*64,'kind':'JOIN'}).encode(),[rights,cred],0,None),'challenge')
check(closed==[91,92])
closed,j=received((json.dumps({'version':1,'nonce':'a'*64,'kind':'JOIN'}).encode(),[rights,cred],0,None),'shape-refusal')
check(closed==[] and j.held==[91,92]) # adopted before failed ACK: original ownership retained


closed,j=received((b'{"version":1,"version":1}',[rights,cred],0,None),'duplicate-message')
check(closed==[91,92] and j.held==[])

# Actual directory/control guards with supplied kernel observations.
import io,stat
j=J.__new__(J);j._identity=lambda fd:(1,2,36);j._at=lambda fd,name:b'domain\n'
def directory_case(uid,mode,access,mount,reason=None):
    with patch.object(m.os,'fstat',return_value=types.SimpleNamespace(st_mode=stat.S_IFDIR|mode,st_uid=uid)),patch.object(m.fcntl,'fcntl',return_value=access),patch.object(m.pathlib.Path,'open',side_effect=lambda *a,**k:io.BytesIO(mount)):
        if reason:refused(lambda:j._directory(10),reason)
        else:check(j._directory(10)==(1,2,36))
directory_case(0,0o755,os.O_RDONLY,b'36 30 0:30 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n')
directory_case(1001,0o755,os.O_RDONLY,b'', 'readonly-custody')
directory_case(0,0o777,os.O_RDONLY,b'', 'readonly-custody')
directory_case(0,0o755,os.O_WRONLY,b'', 'readonly-custody')
directory_case(0,0o755,os.O_RDONLY,b'37 30 0:30 / /sys/fs/cgroup rw - cgroup2 cgroup rw\n','actual-cgroup2')
directory_case(0,0o755,os.O_RDONLY,b'36 30 0:30 / /sys/fs/cgroup rw - tmpfs tmpfs rw\n','actual-cgroup2')
j=J.__new__(J);j.operation_drained=False;j.aggregate_drained=False
j.handles={'parent':{'fd':10,'identity':(1,2,36)}};j._directory=lambda fd:(1,2,36)
j.controls={'parent':{'pids.max':1,'memory.max':2,'cpu.max':3,'memory.current':4}}
values={1:b'32\n',2:b'536870912\n',3:b'100000 100000\n',4:b'4096\n'};j._read=lambda fd:values[fd]
j._validate_limits();check(j.last_memory==4096)
for fd,bad in [(1,b'33\n'),(2,b'max\n'),(3,b'max 100000\n')]:
    original=values[fd];values[fd]=bad;refused(j._validate_limits,'limits-changed');values[fd]=original
for wrong in [(1,3,36),(2,2,36),(1,2,37)]:
    j._directory=lambda fd:wrong;refused(j._validate_limits,'identity-changed')

# Per-message sender and live pidfd/lineage checks use real guard methods.
j=J.__new__(J);j.controller={'fd':20,'birth':row};j._live=lambda fd:True;j._proc=lambda pid,**kwargs:row
j._sender((42,0,0));check(True)
refused(lambda:j._sender((43,0,0)),'sender')
refused(lambda:j._sender((42,1001,1001)),'sender')
j._live=lambda fd:False;refused(lambda:j._sender((42,0,0)),'sender')
j._live=lambda fd:True;j._proc=lambda pid,**kwargs:dict(row,start=101);refused(lambda:j._sender((42,0,0)),'birth-changed')
j=J.__new__(J);j.leader={'birth':dict(row,pid=30,start=99)}
j._retain_process=lambda pid:{'birth':dict(row,pid=pid,ppid=1)}
refused(lambda:j._lineage(record),'lineage-unjoined')
j._retain_process=lambda pid:{'birth':dict(row,pid=pid,ppid=pid)}
refused(lambda:j._lineage(record),'lineage-cycle')
j=J.__new__(J);j._live=lambda fd:False
with patch.object(m.os,'readlink',return_value='anon_inode:[pidfd]'),patch.object(m.pathlib.Path,'open',side_effect=lambda *a,**k:io.BytesIO(b'Pid:\t42\n')):
    refused(lambda:j._pidfd(20,42),'first-pidfd-exited')
j._live=lambda fd:True
with patch.object(m.os,'readlink',return_value='anon_inode:[pidfd]'),patch.object(m.pathlib.Path,'open',side_effect=lambda *a,**k:io.BytesIO(b'Pid:\t42\n')):
    check(j._pidfd(20,42)==42)
    refused(lambda:j._pidfd(20,43),'pid-join')

# Exact joined invocation rejects the old physical timeout and any replacement.
selected={'version':1,'nonce':'a'*64,'pins':{k:'a'*64 for k in J.PINS},'originalParentUtcMs':1000,'originalParentBootNs':1000,'originalBootDeadlineNs':120000001000,'callerArgvSha256':'a'*64,'maximumCases':8}
old_watchdog=['/usr/bin/sudo','-n','--','/usr/bin/timeout','--kill-after=15s','120s','/usr/bin/python3','-I','-S','-c','literal','payload']
replacement=['/usr/bin/sudo','-n','--','/usr/bin/timeout','120s','/usr/bin/python3','-c','literal','payload']
with patch.object(m.socket,'socketpair',side_effect=AssertionError('must reject before transport')):
    refused(lambda:J(selected,old_watchdog,10),'fixed-root-invocation')
    refused(lambda:J(selected,replacement,10),'fixed-root-invocation')
class PairEnd:
    def setsockopt(self,*args):pass
    def setblocking(self,*args):pass
    def close(self):closed_pair.append(self)
new_argv=['/usr/bin/sudo','-n','--','/usr/bin/python3','-I','-S','-c','literal','payload']
selected=dict(selected,callerArgvSha256=hashlib.sha256(json.dumps(new_argv,separators=(',',':')).encode()).hexdigest())
closed_pair=[];pair=(PairEnd(),PairEnd())
with patch.object(J,'_time',return_value=None),patch.object(m.socket,'socketpair',return_value=pair) as created:
    joined=J(selected,new_argv,10);check(created.call_count==1 and joined.deadline==10)
    joined.close();check(closed_pair==list(pair))

# Actual static pidfd readiness never equates an invalid/error handle to exit.
class PidfdPoll:
    def __init__(self,events):self.events=events
    def register(self,fd,flags):check(fd==20)
    def poll(self,timeout):return self.events
for events,expected,reason in [([],True,None),([(20,m.select.POLLIN)],False,None),([(20,m.select.POLLHUP)],False,None),([(20,m.select.POLLIN|m.select.POLLHUP)],False,None),([(20,m.select.POLLNVAL)],None,'readiness-unknown'),([(20,m.select.POLLERR)],None,'readiness-unknown'),([(21,m.select.POLLIN)],None,'readiness-unknown')]:
    with patch.object(m.os,'readlink',return_value='anon_inode:[pidfd]'),patch.object(m.select,'poll',return_value=PidfdPoll(events)):
        if reason:refused(lambda:J._live(20),reason)
        else:check(J._live(20)==expected)
with patch.object(m.os,'readlink',return_value='pipe:[1]'):
    refused(lambda:J._live(20),'role-lost')

# Actual CASE ACK and DRAIN ordering; no child or native descriptor is created.
class CaseSocket:
    family=socket.AF_UNIX;type=socket.SOCK_STREAM
    def getsockopt(self,*args):return struct.pack('3i',42,60001,60001)
    def detach(self):return 11
    def fileno(self):return 11
    def close(self):events.append('close-abort')
class AckSocket:
    def send(self,data):events.append('ack');return len(data)
events=[];j=J.__new__(J)
j.joined=True;j.failed=False;j.operation_drained=False;j.active=None;j.case=-1;j.case_drains=[];j.aggregate_drained=False;j.held=[10,11]
j.selected={'nonce':'a'*64};j.socket=AckSocket();j._sender=lambda cred:events.append('sender');j._time=lambda work:events.append('work' if work else 'cleanup')
j._validate_limits=lambda:events.append('limits');j._topology=lambda:events.append('topology')
j._pidfd=lambda fd:42;j._retain_process=lambda *args:record;j._member=lambda *args:events.append('member')
with patch.object(m.socket,'socket',side_effect=lambda **kwargs:CaseSocket()):j._case(message,[10,11],(42,0,0))
check(events.index('member')<events.index('ack') and j.active is not None and j.case==0)
refused(lambda:j._case(message,[10,11],(42,0,0)),'case-order')
def noempty(roles):raise RuntimeError('actual-live-member')
j._empty=noempty
drain={'version':1,'kind':'DRAIN','nonce':'a'*64,'ordinal':0,'phase':'case','roles':[]}
refused(lambda:j._drain(drain,[],(42,0,0)),'actual-live-member')
check(j.active is not None and j.case_drains==[])
j._empty=lambda roles:events.append('empty');events.clear();j._drain(drain,[],(42,0,0))
check(events.index('empty')<events.index('ack') and j.active is None and j.case_drains==[0] and j.held==[10])
refused(lambda:j._drain(drain,[],(42,0,0)),'drain-order')

# Fixed selector refuses malformed declarations before creating socketpair.
for selected,reason in [({},'shape'),({'version':True,'nonce':'a'*64,'pins':{},'originalParentUtcMs':1,'originalParentBootNs':1,'originalBootDeadlineNs':120000000001,'callerArgvSha256':'a'*64,'maximumCases':8},'shape'),({'version':1,'nonce':'a'*64,'pins':{},'originalParentUtcMs':1,'originalParentBootNs':1,'originalBootDeadlineNs':120000000001,'callerArgvSha256':'a'*64,'maximumCases':8},'pins')]:
    with patch.object(m.socket,'socketpair',side_effect=AssertionError('must not create transport')):refused(lambda:J(selected,[],10),reason)

# Locally opened descriptors are closed if retention capacity refuses adoption.
j=J.__new__(J);j.records={};j.identities={};j._hold=lambda fd:(_ for _ in ()).throw(RuntimeError('retention-cap'))
closed=[]
with patch.object(m.os,'pidfd_open',return_value=91),patch.object(m.os,'close',side_effect=closed.append):
    refused(lambda:j._retain_process(42),'retention-cap')
check(closed==[91])
j.handles={'parent':{'fd':10}};closed=[]
with patch.object(m.os,'open',return_value=92),patch.object(m.os,'close',side_effect=closed.append):
    refused(lambda:j._controls('parent'),'retention-cap')
check(closed==[92])

# Actual retirement predicate never treats callback/text or live pidfd as empty.
j=J.__new__(J);j.joined=True;j.aggregate_drained=False;j.active=None;j.records={(42,100):record}
j._live=lambda fd:False;check(not j.resources_retired())
j.aggregate_drained=True;j._live=lambda fd:True;check(not j.resources_retired())
j._live=lambda fd:False;check(j.resources_retired())
j.failed=False;j.case=7;j.case_drains=list(range(8));check(j.terminal())
j.failed=True;check(not j.terminal())

# Actual run() legacy and optional cleanup dispatch; no Popen, signal or kernel mutation.
class Child:
    pid=42
    def poll(self):return 0
    def wait(self,timeout=0):return 0
class ChildSocket:
    def close(self):pass
class Joined:
    def __init__(self,*args):self.child=ChildSocket();self.aborts=[]
    def bind_leader(self,leader):pass
    def receive(self):pass
    def sample(self):return []
    def classify(self,rows):return rows
    def terminal(self):return True
    def resources_retired(self):return True
    def abort(self,error):self.aborts.append(str(error))
    def close(self):pass
    def report(self):return {'wholePrivilegedWrapperRetirement':'UNKNOWN','aborts':self.aborts}
with tempfile.TemporaryDirectory() as directory:
    calls=[]
    def fakeproc(pid):return row if pid==42 else dict(row,pid=pid,rss=2048)
    with patch.object(m,'proc',side_effect=fakeproc),patch.object(m,'census',return_value=[]),patch.object(m,'observed_escapes',return_value=[]),patch.object(m.subprocess,'Popen',return_value=Child()) as popen,patch.object(m.os,'killpg',side_effect=lambda *args:calls.append(('killpg',args))),patch.object(m.os,'kill',side_effect=lambda *args:calls.append(('kill',args))),patch.object(m.time,'monotonic',return_value=1):
        result=m.run(['fixed'],directory,{},10,15,536870912,str(pathlib.Path(directory)/'legacy.log'))
        check('stdin' not in popen.call_args.kwargs and 'innerCgroupJoin' not in result and result['firstCause'] is None)
        check(calls==[('killpg',(42,m.signal.SIGTERM))])
        calls.clear()
        def legacy_fail(event,*args):
            if event=='after-birth':raise RuntimeError('legacy-first')
        refused(lambda:m.run(['fixed'],directory,{},10,15,536870912,str(pathlib.Path(directory)/'legacy-failed.log'),control=legacy_fail),'legacy-first')
        legacy_receipt=json.loads((pathlib.Path(directory)/'legacy-failed.log.result.json').read_text())
        check(legacy_receipt['firstCause']=='RuntimeError: legacy-first' and 'innerCgroupJoin' not in legacy_receipt and calls==[('killpg',(42,m.signal.SIGTERM))])
        calls.clear()
        with patch.object(m,'InnerCgroupJoin',Joined):
            result=m.run(['fixed'],directory,{},10,15,536870912,str(pathlib.Path(directory)/'joined.log'),inner_cgroup_join={})
            check('stdin' in popen.call_args.kwargs and calls==[] and result['innerCgroupJoin']['wholePrivilegedWrapperRetirement']=='UNKNOWN')
            def fail(event,*args):
                if event=='after-birth':raise RuntimeError('first-hook-failure')
            refused(lambda:m.run(['fixed'],directory,{},10,15,536870912,str(pathlib.Path(directory)/'failed.log'),control=fail,inner_cgroup_join={}),'first-hook-failure')
            receipt=json.loads((pathlib.Path(directory)/'failed.log.result.json').read_text())
            check(calls==[] and receipt['firstCause']=='RuntimeError: first-hook-failure' and receipt['innerCgroupJoin']['aborts']==['first-hook-failure'])
# Census retirement is limited to an already-held exact birth. No native
# descriptor/process is created; kernel observations and all I/O are injected.
def fail(error):raise error
def proc_gone(message='gone'):
    error=FileNotFoundError(2,message,'/proc/42')
    J._context(error,'proc-directory-open','injected-proc',42,'command',100)
    return error
def refused_io(call,kind,number=None):
    try:call()
    except kind as error:check(number is None or error.errno==number)
    else:raise AssertionError('expected I/O refusal')
def census_fixture():
    j=J.__new__(J);j.operation_drained=False;j.aggregate_drained=False
    j.records={(42,100):record};j.identities={42:100};j.held=[20]
    j.census_retirements=[];j.extra=[];j._live=lambda fd:False
    j.operation_topology_reread_used=False;j.operation_topology_reread_observed=False
    j.operation_drain_acknowledged=False
    j._proc=lambda pid,**kwargs:fail(proc_gone())
    j._members=lambda role:set();return j
j=census_fixture();member_reads=[];j._members=lambda role:(member_reads.append(role) or set())
check(j._census_member(record,('command',),'sample')==(None,None))
check(member_reads==['command'] and j.census_retirements[0]['originalPidfdTerminal'] and j.held==[20])
j=census_fixture();j._proc=lambda pid,**kwargs:dict(row,state='Z')
check(j._census_member(record,('command',),'escape-classify')==(None,None))
j=census_fixture();j._live=lambda fd:True;j._member=lambda *args,**kwargs:fail(proc_gone())
refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
check(j.census_retirements==[])
j=census_fixture();j._members=lambda role:{42}
refused(lambda:j._census_member(record,('command',),'sample'),'still-member')
j=census_fixture();states=iter([True,False,False]);j._live=lambda fd:next(states)
j._member=lambda *args,**kwargs:fail(proc_gone())
check(j._census_member(record,('command',),'sample')==(None,None))
for roles in [(),('parent',),('command','command')]:
    j=census_fixture();refused(lambda:j._census_member(record,roles,'sample'),'known-role')
j=census_fixture();refused(lambda:j._census_member(record,('control',),'sample'),'original-role-not-read')
for change in ['missing-record','wrong-start','missing-held-fd','different-record']:
    j=census_fixture()
    if change=='missing-record':j.records={}
    elif change=='wrong-start':j.identities={42:101}
    elif change=='missing-held-fd':j.held=[]
    else:j.records={(42,100):dict(record)}
    refused(lambda:j._census_member(record,('command',),'sample'),'original-held-birth')
j=census_fixture();j._proc=lambda pid,**kwargs:dict(row,start=101)
refused(lambda:j._census_member(record,('command',),'sample'),'pid-reused')
for primitive in ['proc','pidfd','members']:
    for error_type,number in [(FileNotFoundError,2),(PermissionError,13)]:
        if primitive=='proc' and error_type is FileNotFoundError:continue # Qualified exit case above.
        j=census_fixture();injected=lambda *args,**kwargs:fail(error_type(number,'unavailable'))
        if primitive=='proc':j._proc=injected
        elif primitive=='pidfd':j._live=injected
        else:j._members=injected
        refused_io(lambda:j._census_member(record,('command',),'sample'),error_type,number)
        check(j.census_retirements==[])
j=census_fixture();j._proc=lambda *args,**kwargs:fail(ValueError('malformed stat'))
refused_io(lambda:j._census_member(record,('command',),'sample'),ValueError)
j=census_fixture();j._live=lambda fd:fail(RuntimeError('pidfd-readiness-unknown'))
refused(lambda:j._census_member(record,('command',),'sample'),'readiness-unknown')
j=census_fixture();j._members=lambda role:fail(RuntimeError('task-discovery-bound'))
refused(lambda:j._census_member(record,('command',),'sample'),'task-discovery-bound')
# A qualified existing census record may retire during paired stat/poll reads;
# no observed different birth can be reconciled as that original's termination.
for message,before,after in [('inner-live-birth-unavailable',row,row),('inner-first-seen-zombie',row,dict(row,state='Z'))]:
    j=census_fixture();states=iter([True,False,False]);j._live=lambda fd:next(states)
    j._member=lambda *args,**kwargs:fail(m.InnerProcessUnavailable(message,before,after))
    check(j._census_member(record,('command',),'sample')==(None,None))
for before,after in [(row,dict(row,start=101)),(dict(row,start=101),dict(row,state='Z'))]:
    j=census_fixture();j._live=lambda fd:True
    j._member=lambda *args,**kwargs:fail(m.InnerProcessUnavailable('inner-live-birth-unavailable',before,after))
    refused(lambda:j._census_member(record,('command',),'sample'),'unavailable')
    check(j.census_retirements==[])
# A first non-proc ENOENT must not be hidden by later valid retirement.
for primitive in ['retained-pidfd-role-readlink','retained-descriptor-fdinfo-read','cgroup-members-read',None]:
    j=census_fixture();calls=[];error=FileNotFoundError(2,'non-proc gone')
    if primitive:J._context(error,primitive,'injected-non-proc')
    states=iter([True,False,False]);j._live=lambda fd:next(states)
    j._member=lambda *args,**kwargs:fail(error)
    j._members=lambda role:(calls.append(role) or set())
    refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
    check(j.census_retirements==[] and calls==[])
for failed_read in ['pidfd','members']:
    j=census_fixture();j._proc=lambda *args,**kwargs:row;reads=[]
    def actual_live(fd):
        reads.append('pidfd')
        if failed_read=='pidfd' and reads.count('pidfd')==2:raise FileNotFoundError(2,'pidfd role gone')
        return len(reads)==1 or failed_read=='members'
    def actual_members(role):
        reads.append('members')
        if failed_read=='members':raise FileNotFoundError(2,'retained cgroup.procs gone')
        return set()
    j._live=actual_live;j._members=actual_members
    refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
    check(j.census_retirements==[] and (reads==['pidfd','pidfd'] if failed_read=='pidfd' else reads==['pidfd','pidfd','members']))
# Even an exception raised inside _proc needs its actual proc primitive.
j=census_fixture();j._proc=lambda *args,**kwargs:fail(FileNotFoundError(2,'unclassified gone'))
refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
check(j.census_retirements==[])
# Admission stays strict even with a terminal or disappearing process.
j=census_fixture();j._proc=lambda pid,**kwargs:row
refused(lambda:j._member(record,('command',)),'unavailable')
j=census_fixture();j.records={};j.identities={};j._pidfd=lambda *args:42;j._live=lambda fd:True
refused_io(lambda:j._retain_process(42,20,'requester'),FileNotFoundError,2)
check(j.census_retirements==[] and j.records=={})
# Primitive/caller context survives without replacing the original errno.
j=census_fixture()
with patch.object(m.os,'open',side_effect=FileNotFoundError(2,'gone','/proc/42')):
    try:J._proc(j,42,phase='sample',role='command',expected_start=100)
    except FileNotFoundError as error:
        check(error.errno==2 and error.inner_observation_context=={'primitive':'proc-directory-open','phase':'sample','pid':42,'role':'command','expectedStart':100,'errno':2})
    else:raise AssertionError('proc disappearance expected')
j=census_fixture();j._at=lambda *args:fail(FileNotFoundError(2,'gone','stat'))
with patch.object(m.os,'open',return_value=90),patch.object(m.os,'fstat',return_value=types.SimpleNamespace(st_dev=1,st_ino=2)),patch.object(m.os,'close',side_effect=PermissionError(13,'close denied')):
    try:J._proc(j,42,phase='sample',role='command',expected_start=100)
    except FileNotFoundError as error:check(error.errno==2 and error.inner_proc_close_unknown and error.inner_observation_context['primitive']=='held-proc-stat-read' and len(j.extra)==1)
    else:raise AssertionError('original stat failure expected')
j=census_fixture();j._read=lambda *args:fail(FileNotFoundError(2,'read gone','stat'))
with patch.object(m.os,'open',return_value=91),patch.object(m.os,'close',side_effect=PermissionError(13,'close denied')):
    try:J._at(j,90,'stat')
    except FileNotFoundError as error:check(error.errno==2 and error.inner_proc_close_unknown and len(j.extra)==1)
    else:raise AssertionError('original read failure expected')
j=census_fixture();error=proc_gone();error.inner_proc_close_unknown=True
j._live=lambda fd:True;j._member=lambda *args,**kwargs:fail(error)
refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
check(j.census_retirements==[])
j=census_fixture();states=iter([True,False]);j._live=lambda fd:next(states)
j._member=lambda *args,**kwargs:fail(proc_gone('original gone'))
j._proc=lambda *args,**kwargs:fail(PermissionError(13,'reconciliation denied'))
refused_io(lambda:j._census_member(record,('command',),'sample'),FileNotFoundError,2)
check(j.census_retirements==[] and len(j.extra)==1 and 'PermissionError' in j.extra[0])
# Actual topology branches: only witnessed post-DRAIN exact-name disappearance
# admits one fresh retained-parent/home observation, never group retirement.
def topology_case(stage='stat',drained=True,fresh=('home',),denied=False,changed=None,broken_control=False,initial=('home','operation-fixed')):
    j=census_fixture();j.operation_drained=drained;j.operation_drain_acknowledged=drained;j.operation_name='operation-fixed'
    j.handles={role:{'fd':fd,'identity':role} for role,fd in [('parent',10),('home',11),('operation',12)]}
    j._time=lambda work:None
    j._directory=lambda fd:'changed' if changed and fd=={'parent':10,'home':21,'operation':22}.get(changed) else {10:'parent',21:'home',22:'operation'}[fd]
    directory=j._directory
    def read_directory(fd):
        if broken_control and fd==22:raise FileNotFoundError(2,'operation cgroup.type gone')
        return directory(fd)
    j._directory=read_directory
    lists=iter([list(initial),list(fresh)])
    stat_failures=[False]
    def stat_name(name,**kwargs):
        if name=='home' and stage=='home-stat':raise FileNotFoundError(2,'home gone')
        if name=='operation-fixed' and stage=='stat' and not stat_failures[0]:
            stat_failures[0]=True
            raise PermissionError(13,'denied') if denied else FileNotFoundError(2,'gone')
        return types.SimpleNamespace(st_mode=0o040700)
    def open_name(name,*args,**kwargs):
        if name=='home' and stage=='home-open':raise FileNotFoundError(2,'home gone')
        if name=='operation-fixed' and stage=='open':
            raise PermissionError(13,'denied') if denied else FileNotFoundError(2,'gone')
        return 21 if name=='home' else 22
    with patch.object(m.os,'listdir',side_effect=lambda fd:next(lists)),patch.object(m.os,'stat',side_effect=stat_name),patch.object(m.os,'open',side_effect=open_name),patch.object(m.os,'close'):
        J._topology(j)
    return j
for stage in ['stat','open']:
    j=topology_case(stage)
    check(j.operation_topology_reread_used and j.operation_topology_reread_observed and j.operation_drained and not j.aggregate_drained)
    refused(lambda:j._retiring_topology_reread(),'unadmitted')
    refused_io(lambda:topology_case(stage,denied=True),PermissionError,13)
refused_io(lambda:topology_case('stat',drained=False),FileNotFoundError,2)
refused(lambda:topology_case(initial=('foreign','operation-fixed','home')),'foreign-child-before-reread')
for fresh in [('home','foreign'),('home','operation-fixed'),()]:
    refused(lambda:topology_case('stat',fresh=fresh),'reappeared-or-foreign')
refused_io(lambda:topology_case('home-stat'),FileNotFoundError,2)
refused_io(lambda:topology_case('home-open'),FileNotFoundError,2)
refused_io(lambda:topology_case('present',broken_control=True),FileNotFoundError,2)
refused(lambda:topology_case('present',changed='operation'),'ancestry')
# Retained identities and independent home read remain mandatory.
for role,reason in [('parent','parent-identity-changed'),('home','ancestry')]:
    refused(lambda:topology_case(changed=role),reason)
j=census_fixture();j.operation_drained=True
refused(j._retiring_topology_reread,'unadmitted')
# Missing home or a fresh membership observation is never excused by op absence.
j=census_fixture();j.operation_drained=True;j.operation_drain_acknowledged=True;j.operation_topology_reread_used=False
j.handles={'parent':{'fd':10,'identity':'parent'}};j._time=lambda work:None;j._directory=lambda fd:'parent'
with patch.object(m.os,'listdir',return_value=['home']),patch.object(m.os,'stat',side_effect=FileNotFoundError(2,'home gone')):
    refused_io(j._retiring_topology_reread,FileNotFoundError,2)
check(j.operation_topology_reread_used and not j.operation_topology_reread_observed)
# Contract text is never an actual self-restoration witness.
j=census_fixture();j.joined=True;j.case=1;j.case_drains=[0,1];j.first=None;j.first_context=None;j.messages=5;j.last_memory=0
check(j.report()['aggregateDrainObserved'] is False)
j.aggregate_drained=True;check(j.report()['aggregateDrainObserved'] is True)
# Execute the actual assembled native/preflight loader prefixes only. Stop
# before module execution, mkdir, sealing, subprocess or privileged work.
import ast,copy,textwrap
shell=path.parents[1]/'run.sh';shell_text=shell.read_text()
native_text=shell_text.split("<<'PY_NATIVE'\n",1)[1].split('\nPY_NATIVE',1)[0]
def prefix_tree(text):
    tree=ast.parse(text);stop=next(i for i,node in enumerate(tree.body) if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='runner' for t in node.targets))
    return ast.Module(body=tree.body[:stop],type_ignores=[])
native_prefix=prefix_tree(native_text)
repo=path.parents[3]
workflow=(repo/'.github/workflows/telemetry-host-package.yml').read_text()
start=workflow.index('          import hashlib, json, os, pathlib, time\n          inputs=')
end=workflow.index("          runner={'__name__':",start)
preflight_prefix=ast.parse(textwrap.dedent(workflow[start:end]))
def prefix_run(tree,workspace):
    with patch.dict(os.environ,{'GITHUB_WORKSPACE':str(workspace)}),patch.object(sys,'argv',['native-prefix','fixed-controller','fixed-assemblies']):
        namespace={};exec(compile(ast.fix_missing_locations(tree),'<actual-loader-prefix>','exec'),namespace)
    return namespace
native_selected=prefix_run(copy.deepcopy(native_prefix),repo)
check(native_selected['runner_source']==source and native_selected['runner_size']==len(source))
preflight_selected=prefix_run(copy.deepcopy(preflight_prefix),repo)
check(preflight_selected['selected']['runner']==source and set(preflight_selected['selected'])=={'literal','parser','runner'})
def native_fixture(payload,declared=None,missing=False):
    with tempfile.TemporaryDirectory() as temporary:
        workspace=pathlib.Path(temporary);target=workspace/'tests/telemetry-collector-persistent-v3-image-closure/native-preflight/owned_install_guards.py';target.parent.mkdir(parents=True)
        if not missing:target.write_bytes(payload)
        tree=copy.deepcopy(native_prefix)
        if declared is not None:
            for node in tree.body:
                if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='runner_size' for t in node.targets):node.value=ast.Constant(declared)
        return prefix_run(tree,workspace)
for payload in [source[:-1],source+b'x',source+b'x'*65536,bytes([source[0]^1])+source[1:]]:
    refused(lambda:native_fixture(payload),'component-maintained-owner-input-refused')
for declared in [0,-1,65537,True,'61187']:
    refused(lambda:native_fixture(source,declared),'component-maintained-owner-size-bound')
refused(lambda:native_fixture(source,37928),'component-maintained-owner-input-refused')
refused_io(lambda:native_fixture(source,missing=True),FileNotFoundError,2)
with patch.object(pathlib.Path,'open',side_effect=PermissionError(13,'selected owner denied')):
    refused_io(lambda:prefix_run(copy.deepcopy(native_prefix),repo),PermissionError,13)
# Actual dormant preflight selection validates all three declared sources;
# corrupt each one independently, leaving all other selected leaves exact.
inputs=preflight_selected['inputs']
for name in inputs:
    for mutation in ['short','long','digest','missing']:
        with tempfile.TemporaryDirectory() as temporary:
            workspace=pathlib.Path(temporary)
            for role,row_input in inputs.items():
                target=workspace/row_input['path'];target.parent.mkdir(parents=True,exist_ok=True);payload=(repo/row_input['path']).read_bytes()
                if role==name:
                    if mutation=='missing':continue
                    if mutation=='short':payload=payload[:-1]
                    elif mutation=='long':payload+=b'x'
                    else:payload=bytes([payload[0]^1])+payload[1:]
                target.write_bytes(payload)
            if mutation=='missing':refused_io(lambda:prefix_run(copy.deepcopy(preflight_prefix),workspace),FileNotFoundError,2)
            else:refused(lambda:prefix_run(copy.deepcopy(preflight_prefix),workspace),'observation-input-byte-selection:'+name)
# Prefix completion never executes or adopts the selected owner, nor creates
# a native output directory. The real source guards below remain separate.
check('runner' not in native_selected and 'destination' not in native_selected and 'runner' not in preflight_selected)
print('custody owner source-controls='+str(count)+' native=NOT_RUN mocked-kernel-inputs=true')
PY_OWNER_SOURCE
custody_source_started=true
timeout --kill-after=15s 120s bash -c '
  set -euo pipefail
  unset LD_PRELOAD LD_LIBRARY_PATH GCC_EXEC_PREFIX COMPILER_PATH LIBRARY_PATH CPATH C_INCLUDE_PATH CPLUS_INCLUDE_PATH OBJC_INCLUDE_PATH DEPENDENCIES_OUTPUT SUNPRO_DEPENDENCIES
  export PATH=/usr/bin:/bin LC_ALL=C LANG=C TZ=UTC
  compiler="$1"; source="$2"; controls="$3"; destination="$4"; adapter_controls="$5"
  headers=(-nostdinc -isystem /usr/lib/gcc/x86_64-linux-gnu/13/include -isystem /usr/include/x86_64-linux-gnu -isystem /usr/include)
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" "$source" "$controls" -o "$destination/custody-core-tests"
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" -fPIC -shared -DCC_TEST_LIBRARY -Wl,-z,relro,-z,now -Wl,--no-undefined "$source" "$controls" -o "$destination/libfsgg-learn-custody-core-tests.so"
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" -Wl,-z,relro,-z,now "$source" "$adapter_controls" -o "$destination/custody-adapter-tests"
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" -fPIC -shared -DLC_TEST_LIBRARY -Wl,-z,relro,-z,now -Wl,--no-undefined "$source" "$adapter_controls" -o "$destination/libfsgg-learn-custody-adapter-tests.so"
  "$destination/custody-adapter-tests"
  "$destination/custody-core-tests"
  sha256sum -- "$destination/custody-core-tests" "$destination/libfsgg-learn-custody-core-tests.so" "$destination/custody-adapter-tests" "$destination/libfsgg-learn-custody-adapter-tests.so"
' custody-core-source "$custody_compiler" "$custody_source" "$custody_tests" "$itf_root" "$custody_adapter_tests"
export FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY="$itf_root/libfsgg-learn-custody-adapter-tests.so"
FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY_SHA256="$(sha256sum -- "$FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY" | cut -d' ' -f1)"
export FSGG_LEARN_CUSTODY_ADAPTER_TEST_LIBRARY_SHA256
export FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY="$itf_root/libfsgg-learn-custody-core-tests.so"
FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY_SHA256="$(sha256sum -- "$FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY" | cut -d' ' -f1)"
export FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY_SHA256
quint typecheck "$model"
quint test "$model_test" --backend typescript \
  --match '^(success|lost|mismatch|cancellation|cleanupFailureCase|stale|duplicateStore|duplicateProcess|lateDeadline|lateCancellation|c4Success|c4Stale|c4LateDeadline|c4LateCancellation|c4CleanupFailure|c4SealFailure)$' \
  --max-samples 1 --out-itf "$itf_root/{test}-{seq}.itf.json" | tee "$itf_root/quint-tests.log"
grep -E '^  16 passing \(' "$itf_root/quint-tests.log"
test "$(find "$itf_root" -maxdepth 1 -name '*.itf.json' -type f | wc -l)" -eq 16
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
# The actual inactive source fixture must create/check UID32768 files. Keep the
# ordinary caller nonroot; execute only the finite inert recipe route in a
# disposable namespace. This source fixture cannot select the native route.
/usr/bin/python3 -I -S - "$repo_root" "$itf_root" <<'PY_INACTIVE_SOURCE'
import hashlib,json,os,pathlib,shlex,subprocess,sys,time,uuid
repo=pathlib.Path(sys.argv[1]); evidence=pathlib.Path(sys.argv[2])
image='mcr.microsoft.com/dotnet/sdk@sha256:0eeb52c76e35a5431ca707ad2bc75e38006a05393045d8532ae44c15d9474523'
config='sha256:4df5db956df7e072bd410b3f1b69b60194ab4cdd163e8d0f44bdb0d57446371d'
docker='/usr/bin/docker'; timeout='/usr/bin/timeout'
if not pathlib.Path(docker).is_file(): raise RuntimeError('inactive source fixture requires hosted Docker')
if not repo.is_absolute() or repo.is_symlink() or not repo.is_dir(): raise RuntimeError('inactive source checkout path')
name='learn-inactive-source-'+uuid.uuid4().hex
cidfile=evidence/(name+'.cid'); log=evidence/(name+'.log')
client=[docker,'--host=unix:///var/run/docker.sock','--config',str(evidence/(name+'.docker-config'))]
pathlib.Path(client[-1]).mkdir(mode=0o700)
def bounded(argv,seconds,destination,kill=30,cap=16777216):
    # Timeout encloses the entire pipefail pipeline; a cap+1 byte result refuses.
    pipeline=shlex.join(argv)+' 2>&1 | head -c '+str(cap+1)+' > '+shlex.quote(str(destination))
    result=subprocess.run([timeout,'--signal=TERM','--kill-after='+str(kill)+'s',str(seconds)+'s','/usr/bin/bash','-c','set -o pipefail; '+pipeline])
    if destination.stat().st_size>cap: raise RuntimeError('inactive source output exceeded16MiB')
    return result.returncode
pull=evidence/(name+'.pull.log')
if bounded(client+['pull','--platform=linux/amd64',image],120,pull,15)!=0: raise RuntimeError('inactive source immutable SDK acquisition failed')
def inspect(argv,operation,seconds=25):
    destination=evidence/(name+'.'+operation+'.json')
    if bounded(client+argv,seconds,destination,5,65536)!=0: raise RuntimeError('inactive source '+operation+' unreadable')
    return json.loads(destination.read_bytes())[0]
selected=inspect(['image','inspect',image],'image')
if selected['Id']!=config or selected['Architecture']!='amd64' or selected['Os']!='linux' or selected['Size']>1073741824: raise RuntimeError('inactive source SDK image correspondence')
if image not in selected['RepoDigests']: raise RuntimeError('inactive source immutable manifest correspondence')
loaded=repo/'tests/telemetry-collector-persistent-v3-image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.Tests.dll'
argv=client+['run','--pull=never','--name',name,'--cidfile',str(cidfile),'--label=fsgg.inactive-source='+name,
      '--user=0:0','--read-only','--network=none','--ipc=private',
      '--cap-drop=ALL','--cap-add=CAP_CHOWN','--cap-add=CAP_DAC_OVERRIDE','--cap-add=CAP_FOWNER',
      '--security-opt=no-new-privileges','--cpus=2','--memory=6g','--memory-swap=6g','--pids-limit=512',
      '--tmpfs=/tmp:rw,exec,nosuid,nodev,size=1g,mode=1777',
      '--mount','type=bind,src='+str(repo)+',dst='+str(repo)+',readonly',
      '--workdir',str(repo),'--env=HOME=/tmp','--env=DOTNET_CLI_HOME=/tmp',
      '--env=DOTNET_PROCESSOR_COUNT=2','--env=DOTNET_CLI_TELEMETRY_OPTOUT=1',
      '--env=PERSISTENT_V3_RECIPE_CONTROLS_ONLY=1','--entrypoint=/usr/share/dotnet/dotnet',image,str(loaded)]
def authenticate(observed,cid):
    host=observed['HostConfig']; config_row=observed['Config']; mounts=observed['Mounts']
    binding=[row for row in mounts if row['Destination']==str(repo)]
    if (observed['Id']!=cid or observed['Name']!='/'+name or observed['Image']!=config or
        config_row['Labels'].get('fsgg.inactive-source')!=name or config_row['User']!='0:0' or
        config_row['Entrypoint']!=['/usr/share/dotnet/dotnet'] or config_row['Cmd']!=[str(loaded)] or
        'PERSISTENT_V3_RECIPE_CONTROLS_ONLY=1' not in config_row['Env'] or
        not host['ReadonlyRootfs'] or host['NetworkMode']!='none' or host['PidMode']!='' or host['IpcMode']!='private' or
        host['CapDrop']!=['ALL'] or host['CapAdd']!=['CAP_CHOWN','CAP_DAC_OVERRIDE','CAP_FOWNER'] or
        host['SecurityOpt']!=['no-new-privileges'] or host['NanoCpus']!=2000000000 or
        host['Memory']!=6442450944 or host['MemorySwap']!=6442450944 or host['PidsLimit']!=512 or
        host['Tmpfs']!={'/tmp':'rw,exec,nosuid,nodev,size=1g,mode=1777'} or
        len(binding)!=1 or binding[0]['Type']!='bind' or binding[0]['Source']!=str(repo) or binding[0]['RW'] or
        any(row['Destination'] not in [str(repo),'/tmp'] or (row['Destination']=='/tmp' and row['Type']!='tmpfs') for row in mounts)):
        raise RuntimeError('inactive source container scope/identity mismatch')
def retire(cid):
    # Authentication is independent of the work result and CID-file recovery.
    authenticate(inspect(['container','inspect',cid],'cleanup-inspect',15),cid)
    if bounded(client+['container','rm','--force',cid],65,evidence/(name+'.remove.log'),10,65536)!=0:
        raise RuntimeError('inactive source exact container removal unknown')
    absent=evidence/(name+'.absence.log')
    if bounded(client+['container','ls','--all','--no-trunc','--filter=id='+cid,'--format={{.ID}}'],15,absent,5,65536)!=0 or absent.read_bytes().strip():
        raise RuntimeError('inactive source exact container absence unknown')
first=None; cleanup=[]; cid=None
try:
    result=bounded(argv,60,log,15)
    if not cidfile.is_file(): raise RuntimeError('inactive source exact CID missing')
    cid=cidfile.read_text().strip()
    if len(cid)!=64 or any(c not in '0123456789abcdef' for c in cid): raise RuntimeError('inactive source CID shape')
    observed=inspect(['container','inspect',cid],'work-inspect')
    authenticate(observed,cid)
    if result!=0 or observed['State']['Running'] or observed['State']['ExitCode']!=0: raise RuntimeError('inactive source fixture failed')
except BaseException as error: first=error
finally:
    # A CID file survives run (no --rm); only this invocation owns its object.
    if cid is None and cidfile.is_file():
        value=cidfile.read_text().strip()
        if len(value)==64 and all(c in '0123456789abcdef' for c in value): cid=value
    if cid is not None:
        try:
            retire(cid)
        except BaseException as error: cleanup.append(str(error))
    else: cleanup.append('exact container CID unavailable; invocation name retained '+name)
    if log.exists():
        # Bounded source diagnostics remain with the existing fixture root on failure.
        sys.stdout.buffer.write(log.read_bytes());sys.stdout.buffer.flush()
    print('inactive source fixture image='+image+' cid='+str(cid)+' cleanup='+str(cleanup),flush=True)
if first is not None: raise first
if cleanup: raise RuntimeError('inactive source container cleanup unknown')
PY_INACTIVE_SOURCE
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.Tests.dll"
if test "${FSGG_LEARN_NATIVE_FIXTURE:-false}" = true; then
  # The protected admitted source selects one fixed TEST component operation.
  # Optional retained inner join supplements the same sampled outer owner; whole sudo/PAM retirement remains unknown.
  /usr/bin/python3 -I -S - "$itf_root/custody-adapter-tests" "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/bin/Debug/net10.0" <<'PY_NATIVE'
import hashlib,json,os,pathlib,time,sys
started_utc=time.time_ns()//1000000
started_boot=time.clock_gettime_ns(time.CLOCK_BOOTTIME)
original_deadline=time.monotonic()+120
def original_work():
    utc=time.time_ns()//1000000;boot=time.clock_gettime_ns(time.CLOCK_BOOTTIME)
    if utc<started_utc or boot<started_boot or utc>=started_utc+105000 or boot>=started_boot+105000000000 or time.monotonic()>=original_deadline-15:
        raise TimeoutError('component original input/setup work interval exhausted')
controller=pathlib.Path(sys.argv[1]); assemblies=pathlib.Path(sys.argv[2])
workspace=pathlib.Path(os.environ['GITHUB_WORKSPACE'])
runner_path=workspace/'tests/telemetry-collector-persistent-v3-image-closure/native-preflight/owned_install_guards.py'
runner_size=63010
if type(runner_size) is not int or not 0<runner_size<=65536:
    raise RuntimeError('component-maintained-owner-size-bound')
with runner_path.open('rb') as stream: runner_source=stream.read(runner_size+1)
if len(runner_source)!=runner_size or hashlib.sha256(runner_source).hexdigest()!='20bb67be5004feb023499b7393f4352602010ddfb1e61eef086ec7e0496a5fc0':
    raise RuntimeError('component-maintained-owner-input-refused')
runner={'__name__':'accepted_owner_runner_inner_join'}
exec(compile(runner_source,'<accepted-owner-runner-inner-join>','exec'),runner)
destination=pathlib.Path(os.environ['RUNNER_TEMP'])/'learn-native-custody-evidence'
original_work();destination.mkdir(mode=0o700)
output=destination/'original-component.log'
# One original interval starts before hashing/sealing/enrollment/fixture setup.
nonce=os.urandom(32).hex()
observer=runner['proc'](os.getpid())
if observer is None or observer['state']=='Z': raise RuntimeError('component original observer birth unavailable')
join_context={'nonce':nonce,'pid':os.getpid(),'uid':os.getuid(),'gid':os.getgid(),'start':observer['start']}
names=['controller','PersistentV3.ImageClosure.Tests.dll','PersistentV3.ImageClosure.Tests.deps.json',
       'PersistentV3.ImageClosure.Tests.runtimeconfig.json','PersistentV3.ImageClosure.dll','PersistentV3.Preparation.dll','FSharp.Core.dll']
rows=[]; total=0
for name in names:
    path=controller if name=='controller' else assemblies/name
    with path.open('rb') as stream:
        before=os.fstat(stream.fileno())
        digest=hashlib.sha256(); size=0
        while True:
            original_work()
            value=stream.read(65536)
            if not value: break
            size+=len(value);total+=len(value)
            if total>67108864: raise RuntimeError('component outer seal byte bound')
            digest.update(value)
        after=os.fstat(stream.fileno())
    if (before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns,before.st_ctime_ns)!=(after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns,after.st_ctime_ns) or size!=before.st_size:
        raise RuntimeError('component selected input changed')
    rows.append({'name':name,'path':str(path),'bytes':size,'sha256':digest.hexdigest()})
metadata={key:os.environ[key] for key in ['FSGG_LEARN_NATIVE_ADMISSION_SHA256','FSGG_LEARN_NATIVE_SOURCE_SHA256',
          'FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256','FSGG_LEARN_NATIVE_PROFILE_SHA256']}
metadata['FSGG_LEARN_NATIVE_ADAPTER_SHA256']=rows[0]['sha256']
payload={'inputs':rows,'startUtc':started_utc,'startBoot':started_boot,'dotnet':'/usr/share/dotnet/dotnet','metadata':metadata,'join':join_context}
literal="import hashlib,json,os,stat,sys,tempfile,time\np=json.loads(sys.argv[1])\nif set(p)!={'inputs','startUtc','startBoot','dotnet','metadata','join'} or os.geteuid()!=0: raise RuntimeError('root-seal-input')\nfixed=['controller','PersistentV3.ImageClosure.Tests.dll','PersistentV3.ImageClosure.Tests.deps.json','PersistentV3.ImageClosure.Tests.runtimeconfig.json','PersistentV3.ImageClosure.dll','PersistentV3.Preparation.dll','FSharp.Core.dll']\nif not isinstance(p['inputs'],list) or [r.get('name') for r in p['inputs']]!=fixed or p['dotnet']!='/usr/share/dotnet/dotnet': raise RuntimeError('root-seal-fixed-inputs')\nkeys={'FSGG_LEARN_NATIVE_ADMISSION_SHA256','FSGG_LEARN_NATIVE_SOURCE_SHA256','FSGG_LEARN_NATIVE_ADAPTER_SHA256','FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256','FSGG_LEARN_NATIVE_PROFILE_SHA256'}\nif set(p['metadata'])!=keys or any(not isinstance(v,str) or len(v)!=64 or any(c not in '0123456789abcdef' for c in v) for v in p['metadata'].values()): raise RuntimeError('root-seal-metadata')\njoin=p['join']\nif not isinstance(join,dict) or set(join)!={'nonce','pid','uid','gid','start'} or not isinstance(join['nonce'],str) or len(join['nonce'])!=64 or any(c not in '0123456789abcdef' for c in join['nonce']): raise RuntimeError('root-seal-join-shape')\nif any(type(join[k]) is not int or join[k]<0 for k in ('pid','uid','gid','start')) or join['pid']<=1 or join['start']<1: raise RuntimeError('root-seal-observer-shape')\nif type(p['startUtc']) is not int or type(p['startBoot']) is not int or p['startUtc']<1 or p['startBoot']<1: raise RuntimeError('root-seal-clock')\ndef live():\n    boot=time.clock_gettime_ns(time.CLOCK_BOOTTIME); utc=time.time_ns()//1000000\n    if boot<p['startBoot'] or utc<p['startUtc'] or boot>=p['startBoot']+105000000000 or utc>=p['startUtc']+105000: raise TimeoutError('original component work deadline')\ndef cleanup_live():\n    boot=time.clock_gettime_ns(time.CLOCK_BOOTTIME); utc=time.time_ns()//1000000\n    if boot<p['startBoot'] or utc<p['startUtc'] or boot>=p['startBoot']+120000000000 or utc>=p['startUtc']+120000: raise TimeoutError('original component cleanup deadline')\nlive(); os.umask(0o077)\nroot=tempfile.mkdtemp(prefix='fsgg-learn-root-seal-',dir='/tmp'); held=os.open(root,os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW)\ncreated=[];total=0;first=None;cleanup_failures=[]\ntry:\n    for row in p['inputs']:\n        live()\n        if set(row)!={'name','path','bytes','sha256'} or not isinstance(row['path'],str) or not row['path'].startswith('/') or type(row['bytes']) is not int or row['bytes']<0: raise RuntimeError('root-seal-row')\n        source=os.open(row['path'],os.O_RDONLY|os.O_CLOEXEC|os.O_NOFOLLOW)\n        target=-1\n        try:\n            before=os.fstat(source)\n            if not stat.S_ISREG(before.st_mode) or before.st_size!=row['bytes']: raise RuntimeError('root-seal-source-size-type')\n            target=os.open(row['name'],os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_CLOEXEC|os.O_NOFOLLOW,0o600,dir_fd=held);created.append(row['name'])\n            digest=hashlib.sha256();size=0\n            while True:\n                live();value=os.read(source,65536)\n                if not value: break\n                size+=len(value);total+=len(value)\n                if total>67108864: raise RuntimeError('root-seal-64MiB-write-bound')\n                digest.update(value); at=0\n                while at<len(value):\n                    live(); written=os.write(target,value[at:])\n                    if written<=0: raise RuntimeError('root-seal-write')\n                    at+=written\n            after=os.fstat(source)\n            if digest.hexdigest()!=row['sha256'] or size!=row['bytes'] or (before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns,before.st_ctime_ns)!=(after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns,after.st_ctime_ns): raise RuntimeError('root-seal-byte-identity')\n            live(); os.fchmod(target,0o555 if row['name']=='controller' else 0o444)\n        finally:\n            os.close(source)\n            if target>=0: os.close(target)\n    live()\n    env={'PATH':'/usr/bin:/bin','LC_ALL':'C','LANG':'C','TZ':'UTC',**p['metadata'],'FSGG_LEARN_INNER_NONCE':join['nonce'],\n         'FSGG_LEARN_INNER_OBSERVER_PID':str(join['pid']),'FSGG_LEARN_INNER_OBSERVER_UID':str(join['uid']),\n         'FSGG_LEARN_INNER_OBSERVER_GID':str(join['gid']),'FSGG_LEARN_INNER_OBSERVER_START':str(join['start'])}\n    # The exact C TEST controller consumes this owned seal and removes it only\n    # after joined inner cleanup, or retains it with the original unknown.\n    os.execve(root+'/controller',[root+'/controller','--native-fixture',p['dotnet'],root+'/'+fixed[1],str(p['startUtc']),str(p['startBoot'])],env)\nexcept BaseException as error:\n    first=error\nfinally:\n    for name in created:\n        try: cleanup_live(); os.unlink(name,dir_fd=held)\n        except BaseException as error: cleanup_failures.append((type(error).__name__+': '+str(error))[:512])\n    try: os.close(held)\n    except BaseException as error: cleanup_failures.append((type(error).__name__+': '+str(error))[:512])\n    try: cleanup_live(); os.rmdir(root)\n    except BaseException as error: cleanup_failures.append((type(error).__name__+': '+str(error))[:512])\n    if cleanup_failures:\n        print('ROOT_SEAL_CLEANUP_UNKNOWN '+json.dumps({'retainedRoot':root,'failures':cleanup_failures}),file=sys.stderr,flush=True)\nif first is not None: raise first\nif cleanup_failures: raise RuntimeError('root seal cleanup unknown; original bounded details retained')\n"
encoded=json.dumps(payload,sort_keys=True,separators=(',',':'))
if len(encoded.encode())>65536: raise RuntimeError('component fixed input bound')
argv=['/usr/bin/sudo','-n','--','/usr/bin/python3','-I','-S','-c',literal,encoded]
selected_join={'version':1,'nonce':nonce,'pins':metadata,'originalParentUtcMs':started_utc,
    'originalParentBootNs':started_boot,'originalBootDeadlineNs':started_boot+120000000000,
    'callerArgvSha256':hashlib.sha256(json.dumps(argv,separators=(',',':')).encode()).hexdigest(),'maximumCases':8}
result=None;failure=None;additional=[]
try:
    result=runner['run'](argv,str(workspace),{'PATH':'/usr/bin:/bin','LC_ALL':'C','LANG':'C','TZ':'UTC'},
        original_deadline,15,536870912,str(output),output_cap=131072,inner_cgroup_join=selected_join)
except BaseException as error:
    failure=type(error).__name__+': '+str(error)
finally:
    retained=None
    receipt=output.with_suffix(output.suffix+'.result.json')
    try:
        if not receipt.exists(): raise RuntimeError('component retained outer receipt missing')
        with receipt.open('rb') as stream: raw=stream.read(131073)
        if len(raw)>131072: raise RuntimeError('component retained outer receipt bound')
        retained=json.loads(raw)
        if not isinstance(retained,dict): raise RuntimeError('component retained outer receipt type')
        if result is not None and result!=retained: raise RuntimeError('component returned retained outer receipt mismatch')
    except BaseException as error:
        reason=(type(error).__name__+': '+str(error))[:2048]
        if failure is None: failure=reason
        else: additional.append(reason)
    evidence={'schema':'fsgg.learn.native-component-qualification/1','source':os.environ['GITHUB_SHA'],
        'run':os.environ['GITHUB_RUN_ID'],'attempt':os.environ['GITHUB_RUN_ATTEMPT'],
        'admissionJoin':metadata['FSGG_LEARN_NATIVE_ADMISSION_SHA256'],'profileJoin':metadata['FSGG_LEARN_NATIVE_PROFILE_SHA256'],
        'originalParentUtcMs':started_utc,'originalParentBootNs':started_boot,'originalParentBootDeadlineNs':started_boot+120000000000,
        'innerSelectedContext':selected_join,'dataCapacityBytes':{'outerSeal':67108864,'fixture':65011712,'keeperMounts':2097152,'aggregate':134217728},
        'metadataAccounting':'inner cgroup memory; pre-enrollment/root-seal metadata sampled outer',
        'inputs':rows,'outerReturnedReceipt':result,'outerRetainedReceipt':retained,'outerFirstCause':failure,'outerAdditionalFailures':additional,
        'wholePrivilegedWrapperRetirement':'UNKNOWN','hardInnerQuotasApplyBeforeEnrollment':False,
        'hardInnerAccountingEnds':'actual successful self-restoration to retained original home','postRestorationCustody':'SAMPLED_OUTER',
        'innerOutcome':'original-component.log; handle-backed controller result independent of outer sampled receipt'}
    value=json.dumps(evidence,sort_keys=True).encode()
    if len(value)>131072: raise RuntimeError('component evidence bound')
    (destination/'component.json').write_bytes(value)
if failure: raise RuntimeError(failure)
PY_NATIVE
fi
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
runner_tool="${PERSISTENT_V3_RUNNER_TOOL:-$(type -P quint)}"
PERSISTENT_V3_RUNNER_ITF_ROOT="$itf_root" \
PERSISTENT_V3_RUNNER_MODEL="$model" \
PERSISTENT_V3_RUNNER_TOOL="$runner_tool" \
PERSISTENT_V3_RUNNER_ADAPTER="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/Runner.fs" \
PERSISTENT_V3_RUNNER_PROFILE="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" \
PERSISTENT_V3_RUNNER_PRODUCTION_DLL="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" \
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/bin/Debug/net10.0/PersistentV3.ImageClosure.Correspondence.dll"
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
set +e
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/bin/Debug/net10.0/PersistentV3.ImageClosure.Mutant.dll"
mutant_code=$?
set -e
test "$mutant_code" -eq 1
set +e
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" >"$itf_root/production.stdout" 2>"$itf_root/production.stderr"
code=$?
set -e
test "$code" -eq 2
test ! -s "$itf_root/production.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/production.stderr"
cli="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll"
printf '%s' '{"IdentityClass":"synthetic-test","IdentityClass":"synthetic-test"}' >"$itf_root/duplicate.json"
set +e
dotnet "$cli" "$itf_root/duplicate.json" >"$itf_root/duplicate.stdout" 2>"$itf_root/duplicate.stderr"
duplicate_code=$?
set -e
test "$duplicate_code" -eq 3
test ! -s "$itf_root/duplicate.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-is-not-closed' "$itf_root/duplicate.stderr"
truncate -s 4194305 "$itf_root/oversize.json"
set +e
dotnet "$cli" "$itf_root/oversize.json" >"$itf_root/oversize.stdout" 2>"$itf_root/oversize.stderr"
oversize_code=$?
set -e
test "$oversize_code" -eq 3
test ! -s "$itf_root/oversize.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-size' "$itf_root/oversize.stderr"

# Explicit command preserves the immutable placeholder before any effect.
set +e
dotnet "$cli" prepare --selection "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" --trusted-native-selection "$itf_root/missing-trusted.json" >"$itf_root/explicit.stdout" 2>"$itf_root/explicit.stderr"
explicit_code=$?
set -e
test "$explicit_code" -eq 2
test ! -s "$itf_root/explicit.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/explicit.stderr"
set +e
dotnet "$cli" prepare --selection "$itf_root/duplicate.json" --selection "$itf_root/duplicate.json" >"$itf_root/flags.stdout" 2>"$itf_root/flags.stderr"
flags_code=$?
set -e
test "$flags_code" -eq 3
test ! -s "$itf_root/flags.stdout"
grep -Fx 'persistent-v3-image-closure-refused: duplicate-flag-or-relative-path' "$itf_root/flags.stderr"

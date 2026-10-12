"""Pending fixed-0.101 owner successor; original 4e428 remains immutable.

One direct child and one synchronous hook, no second owner/thread. Unexecuted source.
Sampled RSS/disk are stops, not hard containment. Unknown birth never licenses a signal.
"""
import hashlib, json, os, pathlib, signal, subprocess, time
import array, errno, fcntl, select, socket, stat, struct
import math

def resource_birth(pid, tid=None):
    """Read an exact kernel birth; disappearance is uncertainty, never zero load."""
    path=pathlib.Path('/proc',str(pid))
    if tid is not None:path=path/'task'/str(tid)
    text=(path/'stat').read_text(); fields=text[text.rindex(')')+2:].split()
    if int(text.split(' ',1)[0])!=(pid if tid is None else tid):raise RuntimeError('resource-birth-identity')
    return {'pid':pid,'tid':pid if tid is None else tid,'start':int(fields[19]),
            'ppid':int(fields[1]),'pgid':int(fields[2]),'sid':int(fields[3])}

def resource_credentials(raw):
    # Status counters/RSS/scheduling diagnostics are not credential identity.
    names={'Uid','Gid','Groups','CapInh','CapPrm','CapEff','CapBnd','CapAmb','NoNewPrivs','Seccomp','Seccomp_filters'}
    fields={}
    for line in raw.decode().splitlines():
        name,separator,value=line.partition(':')
        if separator and name in names:
            if name in fields:raise RuntimeError('resource-credential-duplicate')
            fields[name]=tuple(value.split())
    if not names-{'Seccomp_filters'}<=set(fields):raise RuntimeError('resource-credential-fields')
    for name in ['Uid','Gid']:
        if len(fields[name])!=4 or any(not v.isdecimal() for v in fields[name]):raise RuntimeError('resource-credential-ids')
    return fields

def resource_thread_census(deadline, clock=time.monotonic):
    """Stable bounded all-thread observation, without scheduling or ambient exemptions."""
    def live():
        if clock()>=deadline:raise TimeoutError('resource-census-original-deadline')
    def population():
        live(); result=[]
        for path in pathlib.Path('/proc').iterdir():
            if path.name.isdigit():
                for task in (path/'task').iterdir():
                    if task.name.isdigit():result.append((int(path.name),int(task.name)))
        if not result or len(result)>4096:raise RuntimeError('resource-census-population-bound')
        return sorted(result)
    def bounded(path,maximum):
        with path.open('rb') as stream:body=stream.read(maximum+1)
        if len(body)>maximum:raise RuntimeError('resource-proc-read-bound')
        return body
    initial=population(); processes={}; rows=[]; exe_cache={}
    for pid,tid in initial:
        live()
        if pid not in processes:
            before=resource_birth(pid); root=pathlib.Path('/proc',str(pid))
            credentials=resource_credentials(bounded(root/'status',65536))
            cgroup=bounded(root/'cgroup',65536)
            if not cgroup or len(cgroup)>65536:raise RuntimeError('resource-census-cgroup-bound')
            with (root/'exe').open('rb') as stream:
                metadata=os.fstat(stream.fileno())
                identity=(metadata.st_dev,metadata.st_ino,metadata.st_size,metadata.st_mtime_ns,metadata.st_ctime_ns)
                if not stat.S_ISREG(metadata.st_mode) or not 0<metadata.st_size<=536870912:raise RuntimeError('resource-census-executable-bound')
                if identity not in exe_cache:
                    digest=hashlib.sha256();extent=0
                    while True:
                        live();body=stream.read(65536)
                        if not body:break
                        extent+=len(body)
                        if extent>metadata.st_size:raise RuntimeError('resource-census-executable-extent')
                        digest.update(body)
                    if extent!=metadata.st_size:raise RuntimeError('resource-census-executable-extent')
                    exe_cache[identity]=digest.hexdigest()
                after=os.fstat(stream.fileno())
                if identity!=(after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns,after.st_ctime_ns):raise RuntimeError('resource-census-executable-drift')
            if resource_birth(pid)!=before or bounded(root/'cgroup',65536)!=cgroup or resource_credentials(bounded(root/'status',65536))!=credentials:raise RuntimeError('resource-census-process-drift')
            current=os.stat(root/'exe')
            if identity!=(current.st_dev,current.st_ino,current.st_size,current.st_mtime_ns,current.st_ctime_ns):raise RuntimeError('resource-census-executable-replaced')
            processes[pid]=(before,int(credentials['Uid'][1]),exe_cache[identity],cgroup.decode('utf-8'))
        leader,uid,exe,cgroup=processes[pid]
        task=pathlib.Path('/proc',str(pid),'task',str(tid))
        before=resource_birth(pid,tid)
        credentials=resource_credentials(bounded(task/'status',65536));task_group=bounded(task/'cgroup',65536)
        if not task_group:raise RuntimeError('resource-thread-cgroup-shape')
        uid=int(credentials['Uid'][1]);cgroup=task_group.decode()
        affinity=sorted(os.sched_getaffinity(tid))
        if not affinity or resource_birth(pid,tid)!=before or sorted(os.sched_getaffinity(tid))!=affinity or resource_birth(pid)!=leader or resource_credentials(bounded(task/'status',65536))!=credentials or bounded(task/'cgroup',65536)!=task_group:raise RuntimeError('resource-census-thread-drift')
        rows.append({'pid':pid,'tid':tid,'processStart':leader['start'],'threadStart':before['start'],
                     'uid':uid,'exeSha256':exe,'cgroup':cgroup,'affinity':affinity,'credentials':credentials})
    if population()!=initial:raise RuntimeError('resource-census-population-drift')
    live();return rows

def admit_thread_resources(profile, admission, *, census_reader=None, clock=time.monotonic, wall=time.time):
    """Check root-selected identities; permitted ambient remains explicitly SHARED LOAD.

    Root authenticates each provenance pin independently before selecting this existing
    admission profile. Byte equality below cannot authenticate its producer or grant an effect.
    No basename, idle state, broad affinity or same-executable rule supplies an exemption.
    """
    required={'cpu','ambient','heldBirths','excludedCpus','rootAuthenticated'}
    if not isinstance(profile,dict) or set(profile)!=required or profile['rootAuthenticated'] is not True:raise RuntimeError('resource-profile-root-selection')
    cpu=profile['cpu']
    if type(cpu) is not int or cpu<0 or not isinstance(profile['excludedCpus'],list) or any(type(v) is not int or v<0 for v in profile['excludedCpus']) or cpu in profile['excludedCpus']:raise RuntimeError('resource-profile-held-cpu')
    if not isinstance(profile['ambient'],list) or len(profile['ambient'])>4096 or not isinstance(profile['heldBirths'],list) or len(profile['heldBirths'])>4096:raise RuntimeError('resource-profile-population-bound')
    identity_keys={'pid','tid','processStart','threadStart','uid','exeSha256','cgroup'}
    def identity(row):
        if not isinstance(row,dict) or not identity_keys<=set(row):raise RuntimeError('resource-identity-shape')
        if any(type(row[k]) is not int or row[k]<(0 if k=='uid' else 1) for k in ['pid','tid','processStart','threadStart','uid']):raise RuntimeError('resource-identity-birth')
        digest=row['exeSha256']
        if not isinstance(digest,str) or len(digest)!=64 or any(v not in '0123456789abcdef' for v in digest) or not isinstance(row['cgroup'],str) or not 0<len(row['cgroup'])<=65536:raise RuntimeError('resource-identity-source')
        return tuple(row[k] for k in sorted(identity_keys))
    def live():
        for key in ['startedAtEpoch','absoluteDeadline','originalMonotonicDeadline','launchBeforeEpoch']:
            if type(admission.get(key)) not in [int,float] or not math.isfinite(admission[key]):raise RuntimeError('resource-original-clock-shape')
        if admission['absoluteDeadline']!=admission['startedAtEpoch']+600 or admission['launchBeforeEpoch']!=admission['startedAtEpoch']+60:raise RuntimeError('resource-original-clock-binding')
        now=wall()
        if now<admission['startedAtEpoch'] or now>=min(admission['absoluteDeadline'],admission['launchBeforeEpoch']) or clock()>=admission['originalMonotonicDeadline']:raise TimeoutError('resource-original-deadline')
    live()
    selected={}
    for row in profile['ambient']:
        if not isinstance(row,dict) or set(row)!=identity_keys|{'provenance'}:raise RuntimeError('resource-ambient-closed-shape')
        key=identity(row)
        if key in selected:raise RuntimeError('resource-ambient-duplicate')
        pin=row['provenance']
        if not isinstance(pin,dict) or set(pin)!={'path','bytes','sha256'} or not isinstance(pin['path'],str) or not pathlib.Path(pin['path']).is_absolute() or type(pin['bytes']) is not int or not 0<pin['bytes']<=2097152:raise RuntimeError('resource-provenance-shape')
        live();path=pathlib.Path(pin['path'])
        if path.is_symlink() or path.resolve()!=path:raise RuntimeError('resource-provenance-linked')
        fd=os.open(path,os.O_RDONLY|os.O_CLOEXEC|os.O_NOFOLLOW|os.O_NONBLOCK)
        try:
            before=os.fstat(fd)
            if not stat.S_ISREG(before.st_mode) or before.st_size!=pin['bytes']:raise RuntimeError('resource-provenance-extent')
            body=os.read(fd,pin['bytes']+1);after=os.fstat(fd)
            keys=['st_dev','st_ino','st_mode','st_uid','st_gid','st_size','st_mtime_ns','st_ctime_ns']
            if any(getattr(before,k)!=getattr(after,k) for k in keys):raise RuntimeError('resource-provenance-unstable')
        finally:os.close(fd)
        live()
        if len(body)!=pin['bytes'] or hashlib.sha256(body).hexdigest()!=pin['sha256']:raise RuntimeError('resource-provenance-drift')
        selected[key]=row
    held=set()
    for row in profile['heldBirths']:
        if not isinstance(row,dict) or set(row)!={'pid','processStart'} or any(type(v) is not int or v<1 for v in row.values()):raise RuntimeError('resource-held-birth-shape')
        held.add((row['pid'],row['processStart']))
    live(); reader=census_reader if census_reader is not None else lambda:resource_thread_census(min(admission['originalMonotonicDeadline'],clock()+max(0,admission['launchBeforeEpoch']-wall())),clock)
    first=reader();live();second=reader();live()
    def normalized(rows):
        if not isinstance(rows,list) or not rows or len(rows)>4096:raise RuntimeError('resource-census-population-bound')
        seen=set();out={}
        for row in rows:
            if not isinstance(row,dict) or set(row)!=identity_keys|{'affinity','credentials'}:raise RuntimeError('resource-census-closed-shape')
            key=identity(row);birth=(row['pid'],row['tid'])
            affinity=row['affinity']
            if birth in seen or not isinstance(affinity,list) or not affinity or any(type(v) is not int or v<0 for v in affinity) or len(set(affinity))!=len(affinity):raise RuntimeError('resource-census-affinity-shape')
            credentials=row['credentials']
            if not isinstance(credentials,dict) or any(not isinstance(v,(tuple,list)) or any(not isinstance(x,str) for x in v) for v in credentials.values()):raise RuntimeError('resource-census-credentials-shape')
            canonical=resource_credentials(('\n'.join(k+': '+ ' '.join(v) for k,v in credentials.items())+'\n').encode())
            if set(canonical)!=set(credentials) or int(canonical['Uid'][1])!=row['uid']:raise RuntimeError('resource-census-credentials-identity')
            seen.add(birth);out[key]=(sorted(affinity),canonical)
        return out
    a=normalized(first); b=normalized(second)
    if a!=b:raise RuntimeError('resource-census-unstable')
    intersections=[];unknown=[]
    for row in second:
        if cpu not in row['affinity']:continue
        if (row['pid'],row['processStart']) in held:raise RuntimeError('resource-held-birth-intersects')
        intersections.append(row)
        if identity(row) not in selected:unknown.append(row)
    if unknown:
        error=RuntimeError('resource-unadmitted-affinity-intersections')
        error.resource_observation={'cpu':cpu,'intersections':intersections,'unadmitted':unknown,'cpuExclusive':False,'effectGranted':False}
        raise error
    live();return {'cpu':cpu,'intersections':intersections,'sharedAmbient':intersections,
                   'cpuExclusive':False,'scope':'Stable sampled all-thread affinity intersections; root-selected ambient is SHARED LOAD, no hard scheduler exclusion','effectGranted':False}

def describe(error):
    try:return type(error).__name__+': '+str(error)
    except BaseException:return type(error).__name__+': <message-unavailable>'

def proc(pid):
    try:
        s=pathlib.Path(f'/proc/{pid}/stat').read_text(); a=s[s.rindex(')')+2:].split()
        return {'pid':pid,'state':a[0],'ppid':int(a[1]),'pgid':int(a[2]),'start':int(a[19]),'rss':int(a[21])*os.sysconf('SC_PAGE_SIZE')}
    except (FileNotFoundError, ProcessLookupError): return None

def census(pgid):
    found=[]
    for p in pathlib.Path('/proc').iterdir():
        if p.name.isdigit():
            row=proc(int(p.name))
            if row and row['pgid']==pgid and row['state']!='Z':found.append(row)
    return found

def observed_escapes(leader, known):
    all_rows=[]
    for p in pathlib.Path('/proc').iterdir():
        if p.name.isdigit():
            row=proc(int(p.name))
            if row:all_rows.append(row)
    by_pid={r['pid']:r for r in all_rows}
    progress=True
    while progress:
        progress=False
        for row in all_rows:
            parent=by_pid.get(row['ppid'])
            if parent and known.get(parent['pid'])==parent['start'] and row['pid'] not in known:
                known[row['pid']]=row['start'];progress=True
    return [r for r in all_rows if known.get(r['pid'])==r['start'] and r['pgid']!=leader['pgid'] and r['state']!='Z']

# Optional fixed LP25 component join. This is inside the same maintained owner,
# not a new supervisor. The root-selected caller authenticates these inputs;
# hashes, root credentials and this JSON shape cannot grant an operation.
def inner_union_rows(outer, inner):
    by_identity={}; births={}
    for row in outer+inner:
        key=(row['pid'],row['start'])
        if row['pid'] in births and births[row['pid']]!=row['start']:
            raise RuntimeError('inner-accounting-pid-reuse')
        births[row['pid']]=row['start']
        if key not in by_identity or row['rss']>by_identity[key]['rss']:
            by_identity[key]=row
    return list(by_identity.values())

class InnerProcessUnavailable(RuntimeError):
    def __init__(self,message,before,after):
        super().__init__(message);self.before=before;self.after=after

def inner_live_join(before, after, live_before, live_after, exact_member):
    # Actual admission paths call this after held-proc/pidfd/membership reads.
    # Source controls can supply kernel observations; that is not native proof.
    if not live_before or not live_after or before is None or after is None:
        raise InnerProcessUnavailable('inner-live-birth-unavailable',before,after)
    if before['state']=='Z' or after['state']=='Z':
        raise InnerProcessUnavailable('inner-first-seen-zombie',before,after)
    if (before['pid'],before['start'])!=(after['pid'],after['start']):
        raise RuntimeError('inner-birth-changed')
    if not exact_member:raise RuntimeError('inner-exact-member-unjoined')
    return after

def inner_closed_object(pairs):
    result={}
    for key,value in pairs:
        if key in result:raise RuntimeError('inner-duplicate-message-field')
        result[key]=value
    return result

class InnerCgroupJoin:
    ROLES=('parent','home','operation','control','requester','command')
    JOIN_ROLES=('controllerPidfd',)+ROLES
    PINS={'FSGG_LEARN_NATIVE_ADMISSION_SHA256','FSGG_LEARN_NATIVE_SOURCE_SHA256',
          'FSGG_LEARN_NATIVE_ADAPTER_SHA256','FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256',
          'FSGG_LEARN_NATIVE_PROFILE_SHA256'}
    def __init__(self, selected, argv, deadline):
        keys={'version','nonce','pins','originalParentUtcMs','originalParentBootNs',
              'originalBootDeadlineNs','callerArgvSha256','maximumCases'}
        if not isinstance(selected,dict) or set(selected)!=keys or type(selected['version']) is not int or selected['version']!=1:
            raise RuntimeError('inner-fixed-input-shape')
        nonce=selected['nonce']
        if not isinstance(nonce,str) or len(nonce)!=64 or any(c not in '0123456789abcdef' for c in nonce):
            raise RuntimeError('inner-fixed-nonce')
        pins=selected['pins']
        if not isinstance(pins,dict) or set(pins)!=self.PINS or any(not isinstance(v,str) or len(v)!=64 or any(c not in '0123456789abcdef' for c in v) for v in pins.values()):
            raise RuntimeError('inner-fixed-pins')
        for key in ('originalParentUtcMs','originalParentBootNs','originalBootDeadlineNs','maximumCases'):
            if type(selected[key]) is not int or selected[key]<1:raise RuntimeError('inner-fixed-clock-count')
        if selected['maximumCases']!=8 or selected['originalBootDeadlineNs']!=selected['originalParentBootNs']+120000000000:
            raise RuntimeError('inner-fixed-original-endpoint')
        if len(argv)!=9 or argv[:7]!=['/usr/bin/sudo','-n','--','/usr/bin/python3','-I','-S','-c']:
            raise RuntimeError('inner-fixed-root-invocation')
        if hashlib.sha256(json.dumps(argv,separators=(',',':')).encode()).hexdigest()!=selected['callerArgvSha256']:
            raise RuntimeError('inner-fixed-argv-join')
        self.selected=dict(selected); self.deadline=deadline
        self._time(False)
        self.socket,self.child=socket.socketpair(socket.AF_UNIX,socket.SOCK_SEQPACKET|socket.SOCK_CLOEXEC)
        try:
            self.socket.setsockopt(socket.SOL_SOCKET,socket.SO_PASSCRED,1)
            self.socket.setblocking(False)
        except BaseException:
            self.socket.close();self.child.close();raise
        self.controller=None; self.leader=None; self.handles={}; self.controls={}
        self.records={}; self.identities={}; self.held=[]; self.messages=0
        self.joined=False; self.case=-1; self.active=None; self.case_drains=[]
        self.operation_drained=False; self.aggregate_drained=False; self.failed=False
        self.first=None; self.extra=[]; self.closed=False; self.last_memory=None
        self.first_context=None; self.census_retirements=[]
        self.operation_topology_reread_used=False;self.operation_topology_reread_observed=False
        self.operation_drain_acknowledged=False
    def _time(self, work):
        utc=time.time_ns()//1000000; boot=time.clock_gettime_ns(time.CLOCK_BOOTTIME)
        start_utc=self.selected['originalParentUtcMs']; start_boot=self.selected['originalParentBootNs']
        interval=105 if work else 120
        if utc<start_utc or boot<start_boot or utc>=start_utc+interval*1000 or boot>=start_boot+interval*1000000000 or time.monotonic()>=self.deadline:
            raise TimeoutError('inner-original-'+('work' if work else 'cleanup')+'-endpoint')
    def _hold(self, fd):
        if len(self.held)>=128:raise RuntimeError('inner-retained-descriptor-bound')
        os.set_inheritable(fd,False);self.held.append(fd);return fd
    def _read(self, fd, maximum=4096):
        self._time(False)
        data=os.pread(fd,maximum+1,0)
        if len(data)>maximum:raise RuntimeError('inner-read-bound')
        return data
    def _at(self, fd, name, maximum=4096):
        child=os.open(name,os.O_RDONLY|os.O_CLOEXEC|os.O_NOFOLLOW,dir_fd=fd)
        first=None
        try:return self._read(child,maximum)
        except BaseException as error:first=error;raise
        finally:
            try:os.close(child)
            except BaseException as error:
                if first is None:
                    self._context(error,'temporary-control-close','capability-read');raise
                first.inner_proc_close_unknown=True
                self.extra.append('temporary-control-close:'+describe(error))
    @staticmethod
    def _identity(fd):
        primitive='retained-descriptor-fstat'
        try:
            s=os.fstat(fd);primitive='retained-descriptor-fdinfo-read'
            with pathlib.Path('/proc/self/fdinfo/'+str(fd)).open('rb') as stream:raw=stream.read(4097)
            if len(raw)>4096:raise RuntimeError('inner-fdinfo-bound')
            primitive='retained-descriptor-fdinfo-parse';text=raw.decode()
            matches=[x.split(':',1)[1].strip() for x in text.splitlines() if x.startswith('mnt_id:')]
            if len(matches)!=1 or not matches[0].isdigit():raise RuntimeError('inner-fd-mount-id')
            return (s.st_dev,s.st_ino,int(matches[0]))
        except BaseException as error:
            InnerCgroupJoin._context(error,primitive,'retained-capability-identity');raise
    def _directory(self, fd):
        info=os.fstat(fd)
        if not stat.S_ISDIR(info.st_mode) or info.st_uid!=0 or info.st_mode&0o022 or fcntl.fcntl(fd,fcntl.F_GETFL)&os.O_ACCMODE!=os.O_RDONLY:
            raise RuntimeError('inner-cgroup-readonly-custody')
        identity=self._identity(fd)
        with pathlib.Path('/proc/self/mountinfo').open('rb') as stream:mount=stream.read(1048577)
        if len(mount)>1048576:raise RuntimeError('inner-mountinfo-bound')
        matches=[line for line in mount.splitlines() if line.split(b' ',1)[0]==str(identity[2]).encode()]
        if len(matches)!=1 or b' - cgroup2 ' not in matches[0]:raise RuntimeError('inner-actual-cgroup2-mount')
        if self._at(fd,'cgroup.type')!=b'domain\n':raise RuntimeError('inner-cgroup-domain')
        return identity
    def _pidfd(self, fd, expected=None):
        primitive='pidfd-role-readlink';pid=expected
        try:
            if os.readlink('/proc/self/fd/'+str(fd))!='anon_inode:[pidfd]':raise RuntimeError('inner-pidfd-role')
            primitive='pidfd-fdinfo-read'
            with pathlib.Path('/proc/self/fdinfo/'+str(fd)).open('rb') as stream:text=stream.read(4097)
            if len(text)>4096:raise RuntimeError('inner-pidfd-info-bound')
            primitive='pidfd-fdinfo-parse'
            ids=[line.split(b':',1)[1].strip() for line in text.splitlines() if line.startswith(b'Pid:')]
            if len(ids)!=1 or not ids[0].isdigit():raise RuntimeError('inner-live-pidfd-unavailable')
            pid=int(ids[0])
            if pid<=1 or (expected is not None and pid!=expected):raise RuntimeError('inner-pidfd-pid-join')
            if not self._live(fd):raise RuntimeError('inner-first-pidfd-exited')
            return pid
        except BaseException as error:
            self._context(error,primitive,'live-pidfd-admission',pid,None,getattr(self,'identities',{}).get(pid));raise
    @staticmethod
    def _live(fd):
        # Accepted terminal poll events witness termination only. No status0
        # or cause of termination follows; invalid/error masks stay fatal.
        primitive='retained-pidfd-role-readlink'
        try:
            if os.readlink('/proc/self/fd/'+str(fd))!='anon_inode:[pidfd]':
                raise RuntimeError('inner-retained-pidfd-role-lost')
            primitive='retained-pidfd-poll-register'
            p=select.poll();p.register(fd,select.POLLIN|select.POLLERR|select.POLLHUP)
            primitive='retained-pidfd-poll';events=p.poll(0)
            primitive='retained-pidfd-event-mask'
            for observed,flags in events:
                if observed!=fd or not flags or flags&~(select.POLLIN|select.POLLHUP):
                    raise RuntimeError('inner-retained-pidfd-readiness-unknown')
            return not events
        except BaseException as error:
            InnerCgroupJoin._context(error,primitive,'pidfd-observation');raise
    @staticmethod
    def _context(error, primitive, phase, pid=None, role=None, expected_start=None):
        # Preserve the actual exception/errno; diagnostics never grant retirement.
        if not hasattr(error,'inner_observation_context'):
            error.inner_observation_context={'primitive':primitive,'phase':phase,
                'pid':pid,'role':role,'expectedStart':expected_start,
                'errno':error.errno if isinstance(error,OSError) else None}
        else:
            context=error.inner_observation_context
            for key,value in [('pid',pid),('role',role),('expectedStart',expected_start)]:
                if context.get(key) is None and value is not None:context[key]=value
            if context['phase']!=phase:
                phases=context.setdefault('callerPhases',[])
                if phase not in phases and len(phases)<3:phases.append(phase)
        return error
    def _proc(self, pid, *, phase='proc', role=None, expected_start=None):
        root=None;first=None;primitive='proc-directory-open'
        try:
            root=os.open('/proc/'+str(pid),os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW)
            primitive='held-proc-fstat-before';before=os.fstat(root)
            primitive='held-proc-stat-read';text=self._at(root,'stat').decode()
            primitive='held-proc-stat-parse';tail=text[text.rindex(')')+2:].split()
            row={'pid':pid,'state':tail[0],'ppid':int(tail[1]),'pgid':int(tail[2]),'start':int(tail[19]),'rss':int(tail[21])*os.sysconf('SC_PAGE_SIZE')}
            primitive='held-proc-fstat-after';after=os.fstat(root)
            if (before.st_dev,before.st_ino)!=(after.st_dev,after.st_ino):raise RuntimeError('inner-held-proc-object-changed')
            return row
        except BaseException as error:
            first=error;self._context(error,primitive,phase,pid,role,expected_start);raise
        finally:
            if root is not None:
                try:os.close(root)
                except BaseException as error:
                    if first is None:
                        self._context(error,'held-proc-close',phase,pid,role,expected_start);raise
                    first.inner_proc_close_unknown=True
                    self.extra.append('held-proc-close:'+describe(error))
    @staticmethod
    def _proc_disappearance(error):
        context=getattr(error,'inner_observation_context',{})
        return (isinstance(error,(FileNotFoundError,ProcessLookupError))
                and error.errno in (errno.ENOENT,errno.ESRCH)
                and context.get('primitive') in ('proc-directory-open','held-proc-stat-read')
                and not getattr(error,'inner_proc_close_unknown',False))
    def _census_retired(self, record, roles, phase):
        birth=record['birth'];pid=birth['pid'];start=birth['start']
        allowed=('home',) if self.operation_drained else ('home','control','requester','command')
        if not roles or len(set(roles))!=len(roles) or any(role not in allowed for role in roles):
            raise RuntimeError('inner-census-independent-known-role-unavailable')
        if record['role'] in allowed and record['role'] not in roles:
            raise RuntimeError('inner-census-original-role-not-read')
        if self.records.get((pid,start)) is not record or self.identities.get(pid)!=start or record['fd'] not in self.held:
            raise RuntimeError('inner-census-original-held-birth-unavailable')
        # A retained original pidfd's accepted terminal poll, never proc
        # absence, witnesses termination. It does not measure exit status. Permission/FD/parse errors remain lost observation.
        if self._live(record['fd']):return False
        try:current=self._proc(pid,phase=phase+'-retirement',role=record['role'],expected_start=start)
        except (FileNotFoundError,ProcessLookupError) as error:
            if not self._proc_disappearance(error):raise
            current=None
        if current is not None and current['start']!=start:raise RuntimeError('inner-census-retired-pid-reused')
        members={role:self._members(role) for role in roles}
        if not members or any(pid in values for values in members.values()):return False
        if self._live(record['fd']):raise RuntimeError('inner-census-retirement-pidfd-changed')
        evidence={'pid':pid,'start':start,'role':record['role'],'phase':phase,
                  'originalPidfdTerminal':True,'absentFromRoles':list(roles)}
        if not any(x['pid']==pid and x['start']==start for x in self.census_retirements):
            if len(self.census_retirements)>=32:raise RuntimeError('inner-census-retirement-report-bound')
            self.census_retirements.append(evidence)
        return True
    def _census_member(self, record, roles, phase):
        try:
            birth=record['birth']
            if self.records.get((birth['pid'],birth['start'])) is not record or self.identities.get(birth['pid'])!=birth['start'] or record['fd'] not in self.held:
                raise RuntimeError('inner-census-original-held-birth-unavailable')
            if not self._live(record['fd']):
                if self._census_retired(record,roles,phase):return None,None
                raise RuntimeError('inner-census-exited-original-still-member')
            try:return self._member(record,roles,phase=phase)
            except (FileNotFoundError,ProcessLookupError,InnerProcessUnavailable) as error:
                if isinstance(error,InnerProcessUnavailable):
                    if any(row is not None and (row['pid'],row['start'])!=(record['birth']['pid'],record['birth']['start']) for row in (error.before,error.after)):raise
                elif not self._proc_disappearance(error):raise
                try:retired=self._census_retired(record,roles,phase)
                except BaseException as problem:
                    self.extra.append('census-retirement-check:'+describe(problem))
                    raise error from problem
                if retired:return None,None
                raise
        except BaseException as error:
            self._context(error,'census-original-observation',phase,record['birth']['pid'],record['role'],record['birth']['start']);raise
    def _retain_process(self, pid, supplied=None, role=None):
        if pid==os.getpid():raise RuntimeError('inner-observer-cannot-own-self')
        previous=self.records.get((pid,self.identities.get(pid)))
        fd=supplied if supplied is not None else (previous['fd'] if previous is not None else os.pidfd_open(pid,0))
        if supplied is None and previous is None:
            try:self._hold(fd)
            except BaseException:os.close(fd);raise
        self._pidfd(fd,pid);live_before=self._live(fd);before=self._proc(pid,phase="live-retain-before",role=role,expected_start=self.identities.get(pid))
        old=self.identities.get(pid)
        if old is not None and old!=before['start']:raise RuntimeError('inner-retained-pid-reuse')
        after=self._proc(pid,phase="live-retain-after",role=role,expected_start=old)
        inner_live_join(before,after,live_before,self._live(fd),True)
        key=(pid,after['start'])
        existing=self.records.get(key)
        if existing is None:
            self.records[key]={'fd':fd,'birth':after,'role':role}
            self.identities[pid]=after['start']
        else:
            if fd!=existing['fd'] and supplied is None:
                raise RuntimeError('inner-duplicate-internal-process-descriptor')
            if role is not None:existing['role']=role
        return self.records[key]
    def bind_leader(self, leader):
        self.leader=self._retain_process(leader['pid'])
        if self.leader['birth']['start']!=leader['start']:raise RuntimeError('inner-original-direct-child-birth')
    def _lineage(self, record):
        pid=record['birth']['pid'];seen=set()
        for unused in range(32):
            if pid in seen:raise RuntimeError('inner-exec-lineage-cycle')
            seen.add(pid)
            r=self._retain_process(pid)
            if r['birth']['pid']==self.leader['birth']['pid'] and r['birth']['start']==self.leader['birth']['start']:return
            pid=r['birth']['ppid']
            if pid<=1:break
        raise RuntimeError('inner-original-exec-lineage-unjoined')
    def _controls(self, role):
        fd=self.handles[role]['fd']
        names=['cgroup.procs','cgroup.events','cgroup.type']
        if role=='parent':names+=['pids.max','memory.max','cpu.max','memory.current']
        held={}
        for name in names:
            child=os.open(name,os.O_RDONLY|os.O_CLOEXEC|os.O_NOFOLLOW,dir_fd=fd)
            try:self._hold(child)
            except BaseException:os.close(child);raise
            info=os.fstat(child)
            if not stat.S_ISREG(info.st_mode) or info.st_uid!=0 or info.st_mode&0o022 or fcntl.fcntl(child,fcntl.F_GETFL)&os.O_ACCMODE!=os.O_RDONLY:
                raise RuntimeError('inner-control-readonly-custody')
            held[name]=child
        self.controls[role]=held
    def _validate_limits(self):
        for role, row in self.handles.items():
            if self.operation_drained and role in ('operation','control','requester','command'):continue
            if self.aggregate_drained:continue
            try:
                if self._directory(row['fd'])!=row['identity']:raise RuntimeError('inner-retained-cgroup-identity-changed')
            except BaseException as error:
                self._context(error,'cgroup-role-validation','validate-limits',role=role);raise
        if not self.aggregate_drained:
            c=self.controls['parent']
            if self._read(c['pids.max'])!=b'32\n' or self._read(c['memory.max'])!=b'536870912\n' or self._read(c['cpu.max'])!=b'100000 100000\n':
                raise RuntimeError('inner-actual-hard-limits-changed')
            memory=self._read(c['memory.current']).strip()
            if not memory.isdigit():raise RuntimeError('inner-hard-memory-sample-unavailable')
            self.last_memory=int(memory)
    def _direct(self, parent_role, name, child_role):
        fd=os.open(name,os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW,dir_fd=self.handles[parent_role]['fd'])
        try:
            if self._directory(fd)!=self.handles[child_role]['identity']:raise RuntimeError('inner-exact-role-ancestry')
        finally:os.close(fd)
    def _members(self, role):
        value=self._read(self.controls[role]['cgroup.procs'])
        parts=value.split()
        if len(parts)>32 or any(not x.isdigit() or int(x)<=1 for x in parts):raise RuntimeError('inner-task-discovery-bound')
        return set(map(int,parts))
    def _member(self, record, roles, *, phase="live-member"):
        pid=record['birth']['pid'];before=self._proc(pid,phase=phase+"-before",role=record["role"],expected_start=record["birth"]["start"])
        live_before=self._live(record['fd'])
        matched=[role for role in roles if pid in self._members(role)]
        after=self._proc(pid,phase=phase+"-after",role=record["role"],expected_start=record["birth"]["start"])
        matched_after=[role for role in roles if pid in self._members(role)]
        row=inner_live_join(before,after,live_before,self._live(record['fd']),len(matched)==1 and matched_after==matched)
        if row['start']!=record['birth']['start']:raise RuntimeError('inner-retained-birth-changed')
        # Membership is actual read from exact retained cgroup object, not a path prefix.
        return row,matched[0]
    def _retiring_topology_reread(self):
        # Topology alone never proves group/process retirement. Only an already
        # acknowledged original operation DRAIN permits this single reread.
        if not self.operation_drained or not self.operation_drain_acknowledged or self.aggregate_drained or self.operation_topology_reread_used:
            raise RuntimeError('inner-retiring-topology-reread-unadmitted')
        self.operation_topology_reread_used=True;self._time(False)
        parent=self.handles['parent']
        if self._directory(parent['fd'])!=parent['identity']:
            raise RuntimeError('inner-retiring-parent-identity-changed')
        actual=set()
        for name in os.listdir(parent['fd']):
            if stat.S_ISDIR(os.stat(name,dir_fd=parent['fd'],follow_symlinks=False).st_mode):actual.add(name)
        if actual!={'home'}:raise RuntimeError('inner-retiring-operation-reappeared-or-foreign-child')
        self._direct('parent','home','home')
        self.operation_topology_reread_observed=True
    def _topology(self):
        if self.aggregate_drained:return
        expected={'parent':{'home'} if self.operation_drained else {'home',self.operation_name},'operation':{'control','requester','command'}}
        for role,names in expected.items():
            if self.operation_drained and role=='operation':continue
            actual=set()
            for name in os.listdir(self.handles[role]['fd']):
                try:info=os.stat(name,dir_fd=self.handles[role]['fd'],follow_symlinks=False)
                except FileNotFoundError as error:
                    self._context(error,'cgroup-child-stat','topology',role=role)
                    if error.errno==errno.ENOENT and self.operation_drained and role=='parent' and name==self.operation_name:
                        if not actual.issubset({'home',self.operation_name}):
                            raise RuntimeError('inner-retiring-foreign-child-before-reread') from error
                        self._retiring_topology_reread();return
                    raise
                if stat.S_ISDIR(info.st_mode):actual.add(name)
            if self.operation_drained and role=='parent':
                if not actual.issubset({'home',self.operation_name}) or 'home' not in actual:raise RuntimeError('inner-unregistered-retiring-topology')
                self._direct('parent','home','home')
                if self.operation_name in actual:
                    try:fd=os.open(self.operation_name,os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW,dir_fd=self.handles['parent']['fd'])
                    except FileNotFoundError as error:
                        if error.errno!=errno.ENOENT:raise
                        self._retiring_topology_reread();return
                    try:
                        if self._directory(fd)!=self.handles['operation']['identity']:raise RuntimeError('inner-exact-role-ancestry')
                    finally:os.close(fd)
            elif actual!=names:raise RuntimeError('inner-unregistered-cgroup-topology')
    def _ack(self, message):
        value={'version':1,'nonce':self.selected['nonce'],'kind':message['kind'],'accepted':True}
        for key in ('ordinal','phase'):
            if key in message:value[key]=message[key]
        data=json.dumps(value,separators=(',',':')).encode()
        self._time(message['kind']!='DRAIN')
        if len(data)>1024 or self.socket.send(data)!=len(data):raise RuntimeError('inner-ack-send')
    def _join(self, message, fds, credential):
        keys={'version','kind','nonce','pins','startUtc','startBoot','deadlineBoot','rootName','operationName','roles'}
        if self.joined or self.failed or set(message)!=keys or message['roles']!=list(self.JOIN_ROLES) or len(fds)!=7:
            raise RuntimeError('inner-join-shape-replay')
        s=self.selected
        if message['pins']!=s['pins'] or message['startUtc']!=s['originalParentUtcMs'] or message['startBoot']!=s['originalParentBootNs'] or message['deadlineBoot']!=s['originalBootDeadlineNs'] or message['rootName']!='fsgg-learn-component-'+s['nonce'][:32]:
            raise RuntimeError('inner-original-input-joins')
        name=message['operationName']
        if not isinstance(name,str) or len(name)!=50 or not name.startswith('fsgg-learn-native-') or any(c not in '0123456789abcdef' for c in name[18:]):
            raise RuntimeError('inner-fixed-operation-name')
        self._time(True)
        if credential[1:]!=(0,0):raise RuntimeError('inner-controller-root-credentials')
        pid=self._pidfd(fds[0],credential[0])
        self.controller=self._retain_process(pid,fds[0],'controller')
        self._lineage(self.controller)
        for role,fd in zip(self.ROLES,fds[1:]):self.handles[role]={'fd':fd,'identity':self._directory(fd)}
        if len({r['identity'] for r in self.handles.values()})!=6:raise RuntimeError('inner-duplicate-cgroup-role')
        self.operation_name=name
        root_path=os.readlink('/proc/self/fd/'+str(self.handles['parent']['fd']))
        if root_path!='/sys/fs/cgroup/system.slice/'+message['rootName']:raise RuntimeError('inner-exact-fresh-root-selection')
        anchor=os.open('/sys/fs/cgroup/system.slice',os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW)
        try:
            actual=os.open(message['rootName'],os.O_RDONLY|os.O_DIRECTORY|os.O_CLOEXEC|os.O_NOFOLLOW,dir_fd=anchor)
            try:
                if self._directory(actual)!=self.handles['parent']['identity']:raise RuntimeError('inner-fresh-root-inode-join')
            finally:os.close(actual)
        finally:os.close(anchor)
        self._direct('parent','home','home');self._direct('parent',name,'operation')
        for role in ('control','requester','command'):self._direct('operation',role,role)
        for role in self.ROLES:self._controls(role)
        self._validate_limits();self._topology();self._member(self.controller,('control',))
        if self._members('requester') or self._members('command'):raise RuntimeError('inner-workload-born-before-join')
        self.joined=True;self._ack(message)
    def _case(self, message, fds, credential):
        if not self.joined or self.failed or self.operation_drained or self.active is not None or set(message)!={'version','kind','nonce','ordinal','roles'} or type(message['ordinal']) is not int or message['ordinal']!=self.case+1 or message['ordinal']>=8 or message['roles']!=['requesterPidfd','abortSocket'] or len(fds)!=2:
            raise RuntimeError('inner-case-order-role-replay')
        self._sender(credential);self._time(True);self._validate_limits();self._topology()
        pid=self._pidfd(fds[0]);record=self._retain_process(pid,fds[0],'requester')
        self._member(record,('requester',))
        abort=socket.socket(fileno=fds[1])
        # Only the actual connected adapter-side endpoint to this requester is delegated.
        if abort.family!=socket.AF_UNIX or abort.type&0xf!=socket.SOCK_STREAM or abort.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12)!=struct.pack('3i',pid,60001,60001):
            abort.detach();raise RuntimeError('inner-exact-case-abort-peer')
        self.case=message['ordinal'];self.active={'ordinal':self.case,'requester':record,'abort':abort}
        self._ack(message)
    def _sender(self, credential):
        if self.controller is None or credential!=(self.controller['birth']['pid'],0,0) or not self._live(self.controller['fd']):raise RuntimeError('inner-original-controller-sender')
        now=self._proc(credential[0],phase='live-sender',role='controller',expected_start=self.controller['birth']['start'])
        if now['start']!=self.controller['birth']['start']:raise RuntimeError('inner-controller-birth-changed')
    def _empty(self, roles):
        for role in roles:
            if self._members(role):raise RuntimeError('inner-pre-removal-members-not-empty')
            lines=self._read(self.controls[role]['cgroup.events']).splitlines()
            if b'populated 0' not in lines:raise RuntimeError('inner-pre-removal-populated-not-empty')
        for r in self.records.values():
            if r['role'] in ('command','requester') and self._live(r['fd']):raise RuntimeError('inner-terminal-pidfd-still-live')
    def _drain(self, message, fds, credential):
        if not self.joined or fds or set(message)!={'version','kind','nonce','ordinal','phase','roles'} or message['roles']!=[]:raise RuntimeError('inner-drain-shape')
        self._sender(credential);self._time(False);self._validate_limits()
        phase=message['phase'];ordinal=message['ordinal']
        if type(ordinal) is not int:raise RuntimeError('inner-drain-ordinal')
        if phase=='case':
            if self.active is None or ordinal!=self.case:raise RuntimeError('inner-case-drain-order')
            self._empty(('requester','command'))
            self.case_drains.append(ordinal)
            fd=self.active['abort'].fileno();self.active['abort'].close()
            if fd in self.held:self.held.remove(fd)
            self.active=None
        elif phase=='operation':
            if self.active is not None or self.operation_drained or ordinal!=8 or self.case_drains!=list(range(self.case+1)):raise RuntimeError('inner-operation-drain-order')
            self._empty(('operation','control','requester','command'))
            self._member(self.controller,('home',));self.operation_drained=True
        elif phase=='aggregate':
            if not self.operation_drained or self.aggregate_drained or ordinal!=8:raise RuntimeError('inner-aggregate-drain-order')
            self._empty(('parent','home'));self.aggregate_drained=True
        else:raise RuntimeError('inner-drain-phase')
        self._ack(message)
        if phase=='operation':self.operation_drain_acknowledged=True
    def receive(self):
        for unused in range(20):
            try:data,ancillary,flags,address=self.socket.recvmsg(8193,socket.CMSG_SPACE(32*4)+socket.CMSG_SPACE(12),socket.MSG_DONTWAIT|socket.MSG_CMSG_CLOEXEC)
            except BlockingIOError:return
            fds=[];kept=False;message=None
            try:
                credentials=[];ancillary_bad=False
                for level,kind,value in ancillary:
                    if level==socket.SOL_SOCKET and kind==socket.SCM_RIGHTS:
                        a=array.array('i');a.frombytes(value[:len(value)//a.itemsize*a.itemsize]);fds.extend(a)
                        if not value or len(value)%a.itemsize:ancillary_bad=True
                    elif level==socket.SOL_SOCKET and kind==socket.SCM_CREDENTIALS and len(value)==12:credentials.append(struct.unpack('3i',value))
                    else:ancillary_bad=True
                # Parse all ancillary entries first so every transferred FD is
                # accounted even when an earlier unrelated entry is invalid.
                if ancillary_bad:raise RuntimeError('inner-unexpected-ancillary-role')
                if not data:
                    if self.aggregate_drained:return
                    raise RuntimeError('inner-registration-channel-lost')
                self.messages+=1
                if self.messages>19 or len(fds)>32 or flags&(socket.MSG_TRUNC|socket.MSG_CTRUNC) or len(data)>8192 or len(credentials)!=1:raise RuntimeError('inner-message-credential-bound')
                message=json.loads(data,object_pairs_hook=inner_closed_object)
                if not isinstance(message,dict) or type(message.get('version')) is not int or message.get('version')!=1 or message.get('nonce')!=self.selected['nonce']:raise RuntimeError('inner-message-challenge')
                self._time(False)
                for fd in fds:self._hold(fd)
                if message.get('kind')=='JOIN':self._join(message,fds,credentials[0])
                elif message.get('kind')=='CASE':self._case(message,fds,credentials[0])
                elif message.get('kind')=='DRAIN':self._drain(message,fds,credentials[0])
                else:raise RuntimeError('inner-message-kind')
                kept=True
            except BaseException as error:
                kind=message.get('kind') if isinstance(message,dict) else None
                self._context(error,'protocol-message','receive-'+(kind if kind in ('JOIN','CASE','DRAIN') else 'unknown'));raise
            finally:
                if not kept:
                    # Adopted capabilities stay owned through abort/cleanup even
                    # after a failed ACK; never leave records pointing at closed
                    # descriptor integers that the kernel could reuse.
                    for fd in fds:
                        if fd not in self.held:
                            try:os.close(fd)
                            except OSError:pass
        raise RuntimeError('inner-message-batch-bound')
    def sample(self):
        if not self.joined:return []
        self._time(False);self._validate_limits();self._topology()
        if self.aggregate_drained:return []
        roles=('home',) if self.operation_drained else ('home','control','requester','command')
        rows=[];seen=set()
        for role in roles:
            for pid in self._members(role):
                if pid in seen:raise RuntimeError('inner-ambiguous-leaf-membership')
                seen.add(pid);record=self.records.get((pid,self.identities.get(pid)))
                if record is None:record=self._retain_process(pid,role=role) # New births remain strict.
                row,actual=self._census_member(record,(role,),'sample')
                if row is not None:record['role']=actual;rows.append(row)
        self._validate_limits()
        return rows
    def classify(self, candidates):
        bad=[]
        for row in candidates:
            record=self.records.get((row['pid'],row['start']))
            if not self.joined or record is None or record['role'] not in ('controller','home','control','requester','command'):
                bad.append(row);continue
            roles=('home',) if self.operation_drained else ('home','control','requester','command')
            if self.aggregate_drained:
                # Retained original controller may finish sampled outer teardown;
                # no new process is accepted after empty aggregate readback.
                if record is not self.controller:bad.append(row)
            else:self._census_member(record,roles,'escape-classify')
        return bad
    def abort(self, error):
        self.failed=True
        if self.first is None:
            self.first=describe(error);self.first_context=getattr(error,'inner_observation_context',None)
        if self.active is not None:
            try:self.active['abort'].shutdown(socket.SHUT_RDWR)
            except BaseException as problem:self.extra.append('exact-case-abort:'+describe(problem))
        # A previously admitted singleton clone may race with this shutdown.
        # Its original hard subtree/adapter cleanup must cover that race.
    def resources_retired(self):
        if not self.joined or not self.aggregate_drained or self.active is not None:return False
        return all(not self._live(r['fd']) for r in self.records.values())
    def terminal(self):
        return not self.failed and self.case==7 and self.case_drains==list(range(8)) and self.resources_retired()
    def report(self):
        return {'joined':self.joined,'cases':self.case+1,'caseDrains':self.case_drains,
                'operationEmptyBeforeRemoval':self.operation_drained,'aggregateEmptyBeforeRemoval':self.aggregate_drained,
                'firstCause':self.first,'firstObservationContext':self.first_context,
                'additionalFailures':self.extra,'messages':self.messages,'censusRetirements':self.census_retirements,
                'hardInnerMemoryCurrentLast':self.last_memory,'hardInnerMemoryLimit':536870912,
                'alreadyAdmittedCloneRace':'possible; original retained inner custody required, not atomic revocation',
                'wholePrivilegedWrapperRetirement':'UNKNOWN','hardInnerAccountingEnd':'actual controller self-restoration',
                'aggregateDrainObserved':self.aggregate_drained,
                'postOperationDrainTopologyReread':{'attempted':self.operation_topology_reread_used,
                    'observedKnownNameAbsence':self.operation_topology_reread_observed,'groupRetirementProof':False}}
    def close(self):
        if self.closed:return
        self.closed=True
        if self.active is not None:
            # detach keeps one unambiguous descriptor owner in held list.
            self.active['abort'].detach()
        for fd in set(self.held):
            try:os.close(fd)
            except OSError:self.extra.append('retained-descriptor-close-unknown')
        self.socket.close();self.child.close()
        if 'retained-descriptor-close-unknown' in self.extra:raise RuntimeError('inner-retained-descriptor-close-unknown')

def run(argv, cwd, env, deadline, cleanup_seconds, rss_limit, out,
        output_cap=10*1024*1024, control=None, *, inner_cgroup_join=None):
    """Retain a receipt even when a spawned child's birth cannot be authenticated."""
    inherited=os.getpid(); output=pathlib.Path(out); output.parent.mkdir(exist_ok=True)
    leader=None; child=None; known={}; escaped=[]; rows=[]; residual=[]; escape_residual=[]
    primary=None; additional=[]; peak=0; cleanup='unknown'; result=None; birth_unknown=False
    hook=control if control is not None else lambda event, child, leader, known: None
    join=InnerCgroupJoin(inner_cgroup_join,argv,deadline) if inner_cgroup_join is not None else None
    inner_rows=[]
    try:log=output.open('xb')
    except BaseException:
        if join is not None:join.close()
        raise
    try:
        hook('before-popen',None,None,known)
        if time.monotonic()>=deadline:raise TimeoutError('stage deadline exhausted before child launch')
        if join is None:
            child=subprocess.Popen(argv,cwd=cwd,env=env,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
        else:
            child=subprocess.Popen(argv,cwd=cwd,env=env,stdin=join.child,stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
            join.child.close()
        # Until the first authenticated PID/start/group read completes, even
        # an exception from proc() leaves this spawned child unqualified.
        birth_unknown=True
        leader=proc(child.pid)
        if leader is None or leader['pgid']!=child.pid:
            raise RuntimeError('spawned child birth identity unavailable')
        birth_unknown=False
        known[child.pid]=leader['start']
        if join is not None:join.bind_leader(leader)
        hook('after-birth',child,leader,known)
        while True:
            hook('poll',child,leader,known)
            escaped=observed_escapes(leader,known)
            if join is not None:
                join.receive() # Registration/ACK precedes escape classification.
                inner_rows=join.sample()
                escaped=join.classify(escaped)
            if escaped:raise RuntimeError('observed descendant escaped owned process group; custody/accounting lost')
            rows=census(child.pid); root=proc(inherited)
            for row in rows:
                if join is not None and row['pid'] in known and known[row['pid']]!=row['start']:raise RuntimeError('inner-outer-discovery-pid-reuse')
                known[row['pid']]=row['start']
            if root is None:raise RuntimeError('supervisor root accounting unknown')
            total=root['rss']+sum(r['rss'] for r in (inner_union_rows(rows,inner_rows) if join is not None else rows));peak=max(peak,total)
            if total>rss_limit:raise RuntimeError('sampled aggregate RSS exceeded')
            if output.stat().st_size>output_cap:raise RuntimeError('output cap exceeded')
            if time.monotonic()>=deadline:raise TimeoutError('original stage deadline exhausted')
            code=child.poll()
            if code is not None:
                rows=census(child.pid)
                if rows:raise RuntimeError('child exited with live owned-group descendants')
                if code:raise RuntimeError('command failed with exit '+str(code))
                if join is not None:
                    join.receive()
                    if not join.terminal():raise RuntimeError('inner-original-terminal-readback-unqualified')
                break
            time.sleep(.1)
        hook('before-success',child,leader,known)
    except BaseException as error:
        primary=error
    finally:
        # No diagnostics or user hook is allowed to interrupt cleanup.
        end=min(time.monotonic()+cleanup_seconds,deadline+cleanup_seconds)
        try:
            if child is None:
                cleanup='not-started'
            elif birth_unknown or leader is None:
                # A Popen PID is retained for evidence only. No numeric signal is safe.
                child.poll()
                cleanup='unknown-birth-identity'
            elif join is not None:
                # No killpg/numeric signal targets registered inner members.
                # Only the exact active adapter-side socket can request abort.
                if primary is not None:join.abort(primary)
                while time.monotonic()<end:
                    try:join.receive()
                    except BaseException as error:
                        if primary is None:primary=error;join.abort(error)
                        else:additional.append('inner-cleanup-observation:'+describe(error))
                        break
                    child.poll()
                    residual=census(child.pid)
                    if child.poll() is not None and not residual:break
                    time.sleep(.1)
                try:child.wait(timeout=max(0,end-time.monotonic()))
                except subprocess.TimeoutExpired:pass
                residual=census(child.pid)
                escape_residual=join.classify(observed_escapes(leader,known))
                cleanup='observed-empty-group-and-joined-inner' if not residual and not escape_residual and child.poll() is not None and join.resources_retired() else 'unknown'
            else:
                now=proc(child.pid)
                if now and now['start']==leader['start'] and now['pgid']==leader['pgid']:
                    try:os.killpg(child.pid,signal.SIGTERM)
                    except ProcessLookupError:pass
                while census(child.pid) and time.monotonic()<end:
                    child.poll();time.sleep(.1)
                residual=census(child.pid)
                if residual or escaped:
                    for row in residual+escaped:
                        now=proc(row['pid'])
                        if now and now['start']==row['start'] and known.get(now['pid'])==now['start']:
                            try:os.kill(row['pid'],signal.SIGKILL)
                            except ProcessLookupError:pass
                try:child.wait(timeout=max(0,end-time.monotonic()))
                except subprocess.TimeoutExpired:pass
                residual=census(child.pid);escape_residual=observed_escapes(leader,known)
                cleanup='observed-empty-group' if not residual and not escape_residual and child.poll() is not None else 'unknown'
        except BaseException as error:
            if primary is None:primary=error
            else:additional.append(describe(error))
        try:
            hook('after-cleanup',child,leader,known)
        except BaseException as error:
            if primary is None:primary=error
            else:additional.append(describe(error))
        if primary is None and cleanup!=('observed-empty-group-and-joined-inner' if join is not None else 'observed-empty-group'):
            primary=RuntimeError('cleanup unqualified')
        final_exit=None
        if child is not None:
            try:final_exit=child.poll()
            except BaseException as error:
                if primary is None:primary=error
                else:additional.append('terminal-poll:'+describe(error))
        try:log.close()
        except BaseException as error:
            if primary is None:primary=error
            else:additional.append('output-close:'+describe(error))
        result={'argv':argv,'leader':leader,'ownedPopenPid':child.pid if child else None,
            'birthIdentityUnavailable':birth_unknown,'exit':final_exit,
            'firstCause':describe(primary) if primary else None,
            'additionalFailures':additional,'cleanup':cleanup,'residual':residual,
            'escapedDescendants':escaped,'escapedResidual':escape_residual,
            'custody':'unknown' if birth_unknown else ('no-child' if child is None else
                       ('lost' if escaped else 'observed-owned-group')),
            'sampledPeakAggregateRss':peak,
            'accountingScope':'supervisor plus observed owned process group at100ms; escaped/transient descendants not contained; no hard whole-tree guarantee'}
        if join is not None:
            try:join.close()
            except BaseException as error:
                if primary is None:primary=error
                else:additional.append('inner-resource-close:'+describe(error))
            result['innerCgroupJoin']=join.report()
            result['firstCause']=describe(primary) if primary else None
            result['accountingScope']='supervisor plus deduplicated observed outer-group and joined-inner RSS at100ms; hard-inner memory sampled separately; whole privileged wrapper UNKNOWN'
        try:output.with_suffix(output.suffix+'.result.json').write_text(json.dumps(result,indent=2)+'\n')
        except BaseException as error:
            if primary is None:primary=error
            else:additional.append('receipt-report:'+describe(error))
    if primary is not None:raise primary
    return result

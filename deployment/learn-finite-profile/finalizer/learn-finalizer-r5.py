#!/usr/bin/python3 -I
"""LEARN finite executor: FINALIZER r5 for the pod profile (design docs/coordination/2026-10-30-learn-finite-executor-pod-design.md sections 6, 8 and 14.1;
Refs FS-GG/.github#4647). Finalizer r4 (baseline/libexec/learn-finalizer-r4.py, 7f875245) archives ONE container's /out. This revision keeps r4's custody pattern and
applies it to a pod. THIS FILE IS SOURCE: nothing is installed, admitted or run here.

Runs ONLY as uid 30034, as the transient user unit learn-fin-<id>.service that the pod operator starts BEFORE the pod unit (which BindsTo it):
    learn-finalizer-r5.py --id <op-id> --source-identity <64 hex> [--input synthetic]   Environment: FSGG_NONCE=<128-bit hex>, FSGG_PG_IMAGE=<64 hex image Id of the pg image>
`--input synthetic` (Refs FS-GG/.github#5184) is the explicit declaration of a SYNTHETIC-input op (the pod harness and the install receipt op): the claim and the
sealed manifest are read from synthetic-claims/ and synthetic-inputs/ with the synthetic record types (learn-input-claim-synthetic/1, provenance synthetic,
realSpoolProvenance false), and every record says `inputProvenance`. Without it nothing changes: a synthetic claim is never read, and a synthetic op whose
finalizer lacks the flag finds no real claim and ends in a retirement failure (and the reverse), so neither kind can be retired as the other.
It is the ONLY custody actor. Names it expects from the pod operator (a later PR provides them): pod and pod unit learn-pod-<id>(.service), containers
learn-pod-<id>-{host,recv,feed,pg}, the loop volume mounted at <volume_root>/<id>.mnt, the per-run credential tmpfs at /run/user/30034/learn-cred/<id>.

What it does (all of it recorded in one hash-chained record):
  1 start      exits 70 and writes nothing if the custody, checkpoint or evidence directory already exists; else creates the first two; reads the claim and the sealed
               manifest through pinned descriptors, and cross-checks the pg image against the admission (admissions/<id>.admit).
  2 wait       anchors on the pod unit; polls; samples the volume with statvfs every 5 s (90% used: trigger `budget`); serves checkpoint requests; triggers when
               every role has /out/<role>/.done, at the soft deadline (1800 s), on the budget, or when the pod unit ends first (unrecoverable).
  3 archive    for host, recv, feed in turn: pause, stream `podman unshare tar` of /proc/<pid>/root/out/<role> into custody, check the container identity before and
               after, unpause; inventory and verdict under ONE shared budget of 4096 members and 256 MiB for the whole op (checkpoints included); extract if accepted.
  4 pg         SIGINT (fast shutdown) to the pg container; stop and remove the pod; one network-less container from the pinned pg image runs pg_controldata on the
               stopped cluster through a read-only bind. The cluster must read `shut down`.
  5 retire     the checks of design section 8, each recorded: pod and containers gone, pod cgroup unpopulated, no unit of this op other than inactive after
               reset-failed, pod secrets and the credential tmpfs gone, the volume a mount point of at most 1 GiB with no process left, the sealed snapshot and the
               claim byte-identical to the start, and the slice back near its starting memory and pids.
  6 record     evidence tree and record.json. Schemas: learn-op-pod-success/1, learn-op-pod-killed-retired/1, learn-op-pod-unrecoverable/1,
               learn-op-pod-failed-to-start/1, and learn-op-retirement-failure/1 (the schema the acknowledgement gate keys on). Every record carries `opId`,
               `inputRead`, `containersStartedWithInput` (a list, or null when it could not be determined), `runNonceSha256` and `finalizerSha256`, which the claim
               tooling's void and supersede read. Exit 0 after writing a record; 70 otherwise.

Checkpoint handshake (the recovery driver, same account): the driver creates <home>/.local/state/learn-checkpoint/<id>/req-<n> (n = 1, 2, ... consecutive, at most 8,
JSON {"n": n, "label": "<slug>"}, at most 512 bytes) and waits for ack-<n>. The finalizer archives every role as it is NOW, extracts the accepted ones into custody,
writes ack-<n> (O_EXCL) and only then does the driver act. So the evidence a restart would wipe from a role's tmpfs is already in custody.

Verbatim from finalizer r4 (checked byte for byte by tests/learn-finalizer-r5): the constants, drop, now, mark, run, open_nofollow, chain, show, archive, extract,
markers, evidence_dir, evidence_file, write_record, finish, cgroup_retired. Verbatim from the pod operator: DIR_FLAGS, _check_dir_fd, open_dir_chain, read_pinned.
Cooperative against `developer`, who has full sudo.
"""
import hashlib, json, os, re, select, signal, stat, subprocess, sys, tarfile, time
from datetime import datetime, timezone

T0, REC, CHAIN = 0.0, {}, {}

# ====================================================================================== VERBATIM from finalizer r4 (baseline/libexec/learn-finalizer-r4.py)
CAP = 256 * 1024 * 1024 + 4096 * 1024 + 10240
CONTENT_CAP, MAX_MEMBERS, MAX_DEPTH = 256 * 1024 * 1024, 4096, 8
SOFT_S, CUSTODY_DEADLINE_S, ANCHOR_WAIT_S, POLL_S, CHAIN_ANCHOR_S = 1800, 1890, 30, 2, 300
DPATH = os.O_PATH | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
DROP_EVERY = 32 << 20


def drop(fd, written=False):
    """r4 (3): drop fd's page cache (after fdatasync for written data); best effort"""
    try:
        if written: os.fdatasync(fd)
        os.posix_fadvise(fd, 0, 0, os.POSIX_FADV_DONTNEED)
    except OSError: pass


def now(): return datetime.now(timezone.utc).isoformat()


def mark(tag, **kv):
    line = f'FSGG {NONCE} {tag} ' + ' '.join(f'{k}={v}' for k, v in kv.items())
    print(line, flush=True); return line


def run(argv, timeout=60, cap=1 << 20):
    try:
        p = subprocess.run(argv, capture_output=True, timeout=timeout, cwd='/')
        return p.returncode, p.stdout[:cap].decode('utf-8', 'replace'), p.stderr[-2000:].decode('utf-8', 'replace')
    except subprocess.TimeoutExpired: return None, '', 'timeout'


def open_nofollow(path, flags, mode=0o600):
    """open path via an O_PATH|O_DIRECTORY|O_NOFOLLOW openat chain from '/' (search permission only); last component O_NOFOLLOW"""
    comps = [c for c in path.split('/') if c]
    if any(c in ('.', '..') for c in comps): raise ValueError('bad path')
    fd = os.open('/', DPATH)
    try:
        for c in comps[:-1]:
            nfd = os.open(c, DPATH, dir_fd=fd); os.close(fd); fd = nfd
        ffd = os.open(comps[-1], flags | os.O_NOFOLLOW | os.O_CLOEXEC, mode, dir_fd=fd)
        if not stat.S_ISREG(os.fstat(ffd).st_mode): os.close(ffd); raise ValueError('not a regular file: ' + path)
        return ffd
    finally: os.close(fd)


def chain(event, **kv):
    entry = {'utc': now(), 'event': event, **kv, 'prev': CHAIN['head']}
    b = json.dumps(entry, sort_keys=True, separators=(',', ':'))
    CHAIN['head'] = hashlib.sha256(b.encode()).hexdigest(); CHAIN['lines'] += 1
    with open(f'{CUST}/chain.jsonl', 'a') as f: f.write(b + '\n'); f.flush(); os.fsync(f.fileno())
    if event not in ('wait',): REC['events'].append({'event': event, **kv})
    if time.monotonic() - CHAIN['lastAnchor'] >= CHAIN_ANCHOR_S:
        mark('ANCHOR', head=CHAIN['head'], lines=CHAIN['lines']); CHAIN['lastAnchor'] = time.monotonic()


def show(unit, *props):
    rc, out, _ = run(['systemctl', '--user', 'show', unit] + [f'-p{p}' for p in props], 10)
    return dict(l.split('=', 1) for l in out.splitlines() if '=' in l)


def archive(argv, out_fd, err_fd, cap, timeout_s=300):
    """stream argv's stdout into out_fd up to cap+1 bytes; stderr straight to err_fd (no pipe); select()-bounded by timeout_s
    in total, so a silent writer cannot block past it; returns (rc, bytes, over) with over True=overflow, None=timeout"""
    p = subprocess.Popen(argv, stdout=subprocess.PIPE, stderr=err_fd, stdin=subprocess.DEVNULL, cwd='/', start_new_session=True)
    n, over, t = 0, False, time.monotonic()
    try:
        while True:
            left = timeout_s - (time.monotonic() - t)
            if left <= 0: over = None; break
            r, _, _ = select.select([p.stdout], [], [], left)
            if not r: over = None; break
            b = p.stdout.read1(1 << 20)
            if not b: break
            if n + len(b) > cap:
                os.write(out_fd, b[: cap + 1 - n]); n = cap + 1; over = True; break
            os.write(out_fd, b); n += len(b)
            if n // DROP_EVERY != (n - len(b)) // DROP_EVERY: drop(out_fd, written=True)   # r4 (3)
    finally:
        if over is not False:                                       # overflow or timeout: EPIPE the writer, kill the group
            p.stdout.close()
            try: os.killpg(p.pid, signal.SIGKILL)
            except ProcessLookupError: pass
        else: p.stdout.close()
        try: rc = p.wait(timeout=60)
        except subprocess.TimeoutExpired:
            try: os.killpg(p.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            rc = p.wait(timeout=10)
    return rc, n, over


def extract(path, dest):
    os.mkdir(dest, 0o700); dfd = os.open(dest, DPATH); manifest = []
    sfd = open_nofollow(path, os.O_RDONLY); f = os.fdopen(sfd, 'rb')
    try:
        with tarfile.open(fileobj=f, mode='r:') as tf:
            for m in tf:
                tf.members = []
                nm = m.name[2:] if m.name.startswith('./') else m.name
                if nm in ('', '.'): continue
                if nm.startswith('/') or '..' in nm.split('/'): raise ValueError('unsafe name at extraction')
                parts = nm.split('/'); pfd = os.dup(dfd)
                try:
                    for c in parts[:-1]:
                        try: nfd = os.open(c, DPATH, dir_fd=pfd)
                        except FileNotFoundError: os.mkdir(c, 0o700, dir_fd=pfd); nfd = os.open(c, DPATH, dir_fd=pfd)
                        os.close(pfd); pfd = nfd
                    if m.isdir():
                        try: os.mkdir(parts[-1], 0o700, dir_fd=pfd)
                        except FileExistsError: pass
                    elif m.isreg():
                        fh, hh, n = tf.extractfile(m), hashlib.sha256(), 0
                        fd = os.open(parts[-1], os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600, dir_fd=pfd)
                        with os.fdopen(fd, 'wb') as o:
                            while True:
                                c = fh.read(1 << 20)
                                if not c: break
                                o.write(c); hh.update(c); n += len(c)
                                if n % DROP_EVERY < len(c): o.flush(); drop(o.fileno(), written=True); drop(f.fileno())   # r4 (3)
                            o.flush(); os.fsync(o.fileno()); os.posix_fadvise(o.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)   # r4 (3)
                        manifest.append({'name': nm, 'size': n, 'sha256': hh.hexdigest()})
                    else: raise ValueError('non-regular member at extraction')
                finally: os.close(pfd)
    finally:
        os.close(dfd)
        try: os.posix_fadvise(f.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)            # r4 (3)
        except OSError: pass
        f.close()
    return manifest


def markers(exec_main_pid):
    rc, out, _ = run(['journalctl', '--user', '-o', 'json', '--no-pager', f'_SYSTEMD_USER_UNIT={OPU}', '+', f'_SYSTEMD_USER_UNIT={FINU}'], 30, 64 << 20)
    acc, prefix = [], f'FSGG {NONCE} '
    for line in out.splitlines():
        try: e = json.loads(line)
        except ValueError: continue
        msg = e.get('MESSAGE')
        if not isinstance(msg, str) or not msg.startswith(prefix): continue
        unit = e.get('_SYSTEMD_USER_UNIT')
        if unit == OPU and (e.get('_PID') == str(exec_main_pid) or e.get('_COMM') in ('podman', 'conmon')): continue
        if unit not in (OPU, FINU): continue
        acc.append({'unit': 'op' if unit == OPU else 'fin', 'pid': e.get('_PID'), 'comm': e.get('_COMM'), 'msg': msg[len(prefix):]})
    b = json.dumps(acc, sort_keys=True).encode()
    return {'count': len(acc), 'sha256': hashlib.sha256(b).hexdigest(), 'lines': acc}


def evidence_dir():
    """mkdir under umask 027; never chmod a directory: the setgid 2710 parent gives setgid + group to new dirs"""
    old = os.umask(0o027)
    try:
        try: os.mkdir(EVID, 0o2750)
        except FileExistsError: pass
    finally: os.umask(old)


def evidence_file(rel, data_iter):
    old = os.umask(0o027)
    try:
        parts = rel.split('/'); d = EVID
        for c in parts[:-1]:
            d = f'{d}/{c}'
            try: os.mkdir(d, 0o2750)
            except FileExistsError: pass
        fd = open_nofollow(f'{EVID}/{rel}', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o640)
        with os.fdopen(fd, 'wb') as o:
            n = 0
            for c in data_iter:
                o.write(c); n += len(c)
                if n % DROP_EVERY < len(c): o.flush(); drop(o.fileno(), written=True)   # r4 (3)
            o.flush(); os.fsync(o.fileno()); os.posix_fadvise(o.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)   # r4 (3)
    finally: os.umask(old)


def write_record():
    evidence_dir()
    REC['chain'] = {'head': CHAIN['head'], 'lines': CHAIN['lines']}; REC['endUtc'] = now()
    b = json.dumps(REC, indent=1, sort_keys=True).encode()
    evidence_file('record.json.tmp', [b]); os.rename(f'{EVID}/record.json.tmp', f'{EVID}/record.json')   # atomic
    mark('RECORD', schema=REC['schema'], sha256=hashlib.sha256(b).hexdigest())


def finish(schema):
    REC['schema'] = schema
    try: write_record()
    except Exception as e:
        mark('RECORD_FAILED', error=type(e).__name__); sys.exit(70)
    sys.exit(0)


def cgroup_retired(path):
    try: return all(l.strip() != 'populated 1' for l in open(path + '/cgroup.events'))
    except FileNotFoundError: return True


# ====================================================================================== VERBATIM from the pod operator (pod-operator/learn-pod-operator-r1.py)
DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC


def _check_dir_fd(fd, owner_uid, label):
    st = os.fstat(fd)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022):
        os.close(fd)
        refuse(f'write path not root-safe at {label}')
    return st


def open_dir_chain(path, start, owner_uid=0):
    """Open `path` as a directory descriptor by walking from `start` with O_DIRECTORY|O_NOFOLLOW and openat. Every directory
    opened (start included) is fstat-checked on its descriptor: owner, directory, not group/other-writable. A symlink or a
    missing component refuses. Returns the final descriptor; intermediate descriptors are closed."""
    if start != '/' and not (path == start or path.startswith(start + '/')):
        refuse(f'write path not root-safe at {path}: outside the chain start {start}')
    try:
        fd = os.open(start, DIR_FLAGS)
    except OSError as e:
        refuse(f'write path not root-safe at {start}: {e.strerror}')
    _check_dir_fd(fd, owner_uid, start)
    cur = start
    for comp in [p for p in path[len(start):].split('/') if p]:
        cur = cur.rstrip('/') + '/' + comp
        try:
            nfd = os.open(comp, DIR_FLAGS, dir_fd=fd)
        except OSError as e:
            os.close(fd)
            refuse(f'write path not root-safe at {cur}: {e.strerror}')
        os.close(fd)
        fd = nfd
        _check_dir_fd(fd, owner_uid, cur)
    return fd


def read_pinned(path, start, owner_uid, cap):
    """Bytes of a regular file reached through pinned descriptors: every directory from `start` is opened O_DIRECTORY|O_NOFOLLOW and fstat-checked
    (open_dir_chain), the file is opened O_NOFOLLOW|O_NONBLOCK and fstat-checked (regular, owner, not group/other-writable, at most `cap` bytes).
    Refuses with a reason; never returns None."""
    parent, name = os.path.split(path)
    dfd = open_dir_chain(parent, start, owner_uid)
    try:
        try:
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=dfd)
        except FileNotFoundError:
            refuse(f'absent: {name}')
        except OSError:
            refuse(f'not a root-owned regular file: {name}')
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > cap:
                refuse(f'not a root-owned regular file closed to group and other writers, within {cap} bytes: {name}')
            return f.read(cap + 1)
    finally:
        os.close(dfd)


# ====================================================================================== r5 configuration and helpers
class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


SCHEMA_OK, SCHEMA_KILLED, SCHEMA_UNRECOVERABLE = 'learn-op-pod-success/1', 'learn-op-pod-killed-retired/1', 'learn-op-pod-unrecoverable/1'
SCHEMA_FAILED_START, SCHEMA_RETIREMENT_FAILURE = 'learn-op-pod-failed-to-start/1', 'learn-op-retirement-failure/1'
STATUS_SCHEMA = 'learn-pod-status/1'
ROLES = ('host', 'recv', 'feed')
OK_REASONS = {'host': ('ok', 'signalled'), 'recv': ('ok', 'signalled'), 'feed': ('ok',)}
STATUS_REASONS = ('ok', 'error', 'gate-refused', 'enospc', 'signalled')
OPID_RE = re.compile(r'^[a-z0-9][a-z0-9-]{2,58}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
# where each input kind's claim and sealed snapshot live, and their record types (learn_seal_claim.py's constants; #5184). 'real' is r5's original reading.
INPUT_KINDS = {'real': {'claims': 'claims', 'claim': 'learn-input-claim/1', 'inputs': 'inputs', 'sealed': 'learn-input-snapshot/1'},
               'synthetic': {'claims': 'synthetic-claims', 'claim': 'learn-input-claim-synthetic/1', 'inputs': 'synthetic-inputs', 'sealed': 'learn-input-snapshot-synthetic/1'}}
VOLUME_ROOT = '/mnt/storage/containers/learn-qualification/learn-pod'
VOLUME_CAP, VOLUME_BUDGET, VOLUME_SAMPLE_S, VOLUME_LOW = 1 << 30, 0.90, 5, 1 << 20
MAX_CHECKPOINTS = 8
SEALED_FILE_MODE = 0o644                                       # the seal tool chmods every copy to 0644 whatever the source mode (the manifest's `mode` is the source's)
PG_STOP_WAIT_S, PGCD_TIMEOUT_S, SLICE_WAIT_S = 30, 60, 10
SLICE_MEM_SLACK, SLICE_PIDS_SLACK = 256 << 20, 16
SECCOMP = '/etc/fsgg/learn/seccomp-8bd52f8d.json'
CONTROLDATA_KEYS = {'Database cluster state': 'clusterState', 'Latest checkpoint location': 'latestCheckpoint', "Latest checkpoint's TimeLineID": 'timeline'}
DEFAULTS = {'home': '/mnt/storage/containers/learn-qualification/account', 'view': '/usr/local/libexec/fsgg-learn', 'proc': '/proc', 'cgroot': '/sys/fs/cgroup',
            'slice_dir': '/sys/fs/cgroup/user.slice/user-30034.slice', 'unshare': ['podman', 'unshare'], 'tar': ['/usr/bin/tar'], 'statvfs': os.statvfs,
            'owner_uid': 0, 'chain_start': '/', 'volume_root': VOLUME_ROOT, 'cred_root': '/run/user/30034/learn-cred', 'is_mountpoint': os.path.ismount, 'production': True}
CFG = dict(DEFAULTS)
BUDGET = {'members': MAX_MEMBERS, 'content': CONTENT_CAP}      # what is left of the 4096-member and 256 MiB caps, across every archive of the op
SLICE0, STATE = {}, {}
VOLUME_STATE = {'samples': 0, 'maxUsed': 0.0, 'last': None, 'errors': 0, 'lastAt': None}


def configure(argv, environ, euid=None, **over):
    """Validate the invocation and set the module state the r4 blocks below read (OPID, NONCE, CUST, EVID, OPU, FINU, REC, CHAIN, T0, ...)."""
    global OPID, IDENT, INPUT, NONCE, PGIMG, POD, PGC, CTRS, OPU, FINU, HOME, VIEW, CUST, EVID, CKDIR, VOLUME, CRED, MARKER, REC, CHAIN, T0, SELF_SHA, CFG
    CFG = dict(DEFAULTS, **over)
    if (os.geteuid() if euid is None else euid) != 30034:
        sys.exit('refuse: run as uid 30034')
    if CFG['production']:
        os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus', HOME=CFG['home'], PATH='/usr/bin', LC_ALL='C')
        os.umask(0o077)
        os.chdir('/')
    SELF_SHA = hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest()
    if (len(argv) not in (4, 6) or argv[0] != '--id' or argv[2] != '--source-identity' or not OPID_RE.fullmatch(argv[1]) or not HEX64.fullmatch(argv[3])
            or (len(argv) == 6 and argv[4:] != ['--input', 'synthetic'])):
        sys.exit('usage: --id <op-id> --source-identity <64 hex> [--input synthetic]')
    OPID, IDENT = argv[1], argv[3]
    INPUT = 'synthetic' if len(argv) == 6 else 'real'
    NONCE = environ.get('FSGG_NONCE', '')
    if not re.fullmatch(r'[0-9a-f]{32}', NONCE):
        sys.exit('refuse: FSGG_NONCE missing')
    PGIMG = environ.get('FSGG_PG_IMAGE', '')
    if not HEX64.fullmatch(PGIMG):
        sys.exit('refuse: FSGG_PG_IMAGE missing')
    POD = f'learn-pod-{OPID}'
    OPU, FINU, PGC = f'{POD}.service', f'learn-fin-{OPID}.service', f'{POD}-pg'
    CTRS = {r_: f'{POD}-{r_}' for r_ in ROLES}
    HOME, VIEW = CFG['home'], CFG['view']
    CUST, EVID = f'{HOME}/.local/state/learn-custody/{OPID}', f'{HOME}/.local/state/learn-evidence/{OPID}'
    CKDIR, MARKER = f'{HOME}/.local/state/learn-checkpoint/{OPID}', f'{HOME}/.local/state/learn-input/{OPID}/input-read'
    VOLUME, CRED = f"{CFG['volume_root']}/{OPID}.mnt", f"{CFG['cred_root']}/{OPID}"
    T0 = time.monotonic()
    CHAIN = {'head': '0' * 64, 'lines': 0, 'lastAnchor': time.monotonic()}
    REC = {'schema': None, 'opId': OPID, 'finalizerSha256': SELF_SHA, 'runNonceSha256': hashlib.sha256(NONCE.encode()).hexdigest(),
           'inputProvenance': INPUT, 'startUtc': datetime.now(timezone.utc).isoformat(), 'events': []}
    BUDGET.update(members=MAX_MEMBERS, content=CONTENT_CAP)
    VOLUME_STATE.update(samples=0, maxUsed=0.0, last=None, errors=0, lastAt=None)
    SLICE0.clear()
    STATE.clear()


UNIT_UNKNOWN_MAX, CG_READ_TRIES = 5, 5          # consecutive unreadable unit-state polls before the unit is concluded to have ended; tries to read the pod unit's ControlGroup


def sleep(s):
    time.sleep(s)


def insp(ctr, fmt):
    rc, out, _ = run(['podman', 'inspect', '--format', fmt, ctr], 15)
    return out.strip() if rc == 0 else None


def done_marker(pid, role):
    a, _, _ = run(CFG['unshare'] + ['test', '-f', f"{CFG['proc']}/{pid}/root/out/{role}/.done"], 10)
    b, _, _ = run(CFG['unshare'] + ['test', '-L', f"{CFG['proc']}/{pid}/root/out/{role}/.done"], 10)
    return a == 0 and b != 0


def start_time(pid):
    try: return open(f"{CFG['proc']}/{pid}/stat").read().rsplit(')', 1)[1].split()[19]
    except (OSError, IndexError): return None


def frozen_line(cg):
    try: return next((l.strip() for l in open(cg + '/cgroup.events') if l.startswith('frozen')), None)
    except OSError: return None


def ident_sample(ctr, pid, cid):
    try: cg = CFG['cgroot'] + next(l.split('::', 1)[1].strip() for l in open(f"{CFG['proc']}/{pid}/cgroup") if l.startswith('0::'))
    except (OSError, StopIteration): cg = None
    return {'idc': bool(cg and cid and cid in cg), 'start': start_time(pid), 'frozen': frozen_line(cg) if cg else None,
            'state': insp(ctr, '{{.State.Status}}'), 'cg': cg}


# ---------------- pinned reads of the claim and the sealed snapshot (verbatim blocks from the pod operator) ----------------


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def snapshot_read():
    """The claim names this op and this source identity, and the sealed manifest it records exists with that sha256. Only metadata is read here.
    Returns the state the end-of-run check compares against; raises Refuse."""
    k = INPUT_KINDS[INPUT]
    raw = read_pinned(f"{VIEW}/{k['claims']}/{IDENT}.claim", CFG['chain_start'], CFG['owner_uid'], 65536)
    claim = json.loads(raw)
    if claim.get('schema') != k['claim'] or claim.get('opId') != OPID or claim.get('sourceIdentity') != IDENT:
        refuse('the live claim is not this op\'s claim for this source identity')
    if INPUT == 'synthetic' and (claim.get('provenance') != 'synthetic' or claim.get('realSpoolProvenance') is not False):
        refuse('the synthetic claim does not say synthetic provenance')
    sid = claim.get('snapshotId')
    if not isinstance(sid, str) or not OPID_RE.fullmatch(sid):
        refuse('the claim has no valid snapshot id')
    smraw = read_pinned(f"{VIEW}/{k['inputs']}/{sid}.manifest.json", CFG['chain_start'], CFG['owner_uid'], 1 << 20)
    if sha_bytes(smraw) != claim.get('sealedManifestSha256'):
        refuse('the sealed manifest does not have the sha256 the claim records')
    sm = json.loads(smraw)
    if sm.get('schema') != k['sealed'] or sm.get('snapshotId') != sid or sm.get('sourceIdentity') != IDENT or not isinstance(sm.get('files'), list):
        refuse('the sealed manifest names another snapshot or source')
    if INPUT == 'synthetic' and (sm.get('provenance') != 'synthetic' or sm.get('realSpoolProvenance') is not False):
        refuse('the synthetic sealed manifest does not say synthetic provenance')
    return {'claimSha256': sha_bytes(raw), 'sealedManifestSha256': sha_bytes(smraw), 'snapshotId': sid, 'files': sm['files']}


def hash_pinned(path, size):
    """sha256 of one sealed file reached through pinned descriptors (directories root-safe, file O_NOFOLLOW|O_NONBLOCK, regular, not group/other-writable)."""
    parent, name = os.path.split(path)
    dfd = open_dir_chain(parent, CFG['chain_start'], CFG['owner_uid'])
    try:
        fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=dfd)
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != CFG['owner_uid'] or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size != size:
                refuse(f'sealed file is not a root-owned regular file of the recorded size: {name}')
            h = hashlib.sha256()
            for c in iter(lambda: f.read(1 << 20), b''):
                h.update(c)
            return h.hexdigest(), st
    finally:
        os.close(dfd)


def snapshot_verify(start):
    """At retirement: the claim file and the sealed manifest are byte-identical to the start, and the sealed snapshot is exactly the manifest (each file's size,
    sha256, mode and mtime; no extra file, no symlink). Returns a dict with ok; never raises."""
    out = {'ok': False, 'filesChecked': 0}
    try:
        now_ = snapshot_read()
        out['claimUnchanged'] = now_['claimSha256'] == start['claimSha256']
        out['manifestUnchanged'] = now_['sealedManifestSha256'] == start['sealedManifestSha256']
        snap = f"{VIEW}/{INPUT_KINDS[INPUT]['inputs']}/{start['snapshotId']}"
        want = {}
        for f in start['files']:
            p = f.get('path')
            if not isinstance(p, str) or p.startswith('/') or '..' in p.split('/') or f.get('type') != 'file' or p in want:
                refuse('the sealed manifest holds an unsafe or duplicate path')
            want[p] = f
        extra = []
        for root, dirs, files in os.walk(snap, followlinks=False):
            for d in list(dirs):
                if os.path.islink(os.path.join(root, d)):
                    extra.append(os.path.relpath(os.path.join(root, d), snap))
            for fn in files:
                rel = os.path.relpath(os.path.join(root, fn), snap)
                if rel not in want:
                    extra.append(rel)
        out['extra'] = sorted(extra)[:20]
        bad = []
        for p, f in sorted(want.items()):
            try:
                h, st = hash_pinned(f'{snap}/{p}', f['size'])
            except (OSError, Refuse):
                bad.append(p)
                continue
            if h != f['sha256'] or st.st_mtime_ns != f['mtime_ns'] or stat.S_IMODE(st.st_mode) != SEALED_FILE_MODE:
                bad.append(p)
            out['filesChecked'] += 1
        out['changed'] = bad[:20]
        out['ok'] = bool(out['claimUnchanged'] and out['manifestUnchanged'] and not extra and not bad)
    except (Refuse, OSError, ValueError, KeyError, TypeError) as e:
        out['error'] = f'{type(e).__name__}: {str(e)[:160]}'
    return out


def admission_check():
    """Cross-check what the pod operator passed in FSGG_PG_IMAGE against the admission Root wrote for THIS op (read through pinned descriptors): same op id, same source
    identity, this finalizer's own sha256 as the admitted finalizer, and the pg image Id among the admitted image Ids. The lifetime is not checked here (the operator
    did that when the run started). Returns {ok, reasons, sha256}; never raises."""
    try:
        raw = read_pinned(f'{VIEW}/admissions/{OPID}.admit', CFG['chain_start'], CFG['owner_uid'], 65536)
        a = json.loads(raw)
    except (Refuse, OSError, ValueError) as e:
        return {'ok': False, 'reasons': [f'admission unreadable: {type(e).__name__}: {str(e)[:120]}']}
    why = []
    if not isinstance(a, dict) or a.get('schema') != 'learn-pod-admission/1':
        return {'ok': False, 'reasons': ['admission has another schema'], 'sha256': sha_bytes(raw)}
    if a.get('opId') != OPID: why.append('admission names another op')
    if a.get('sourceIdentity') != IDENT: why.append('admission names another source identity')
    if a.get('finalizerSha256') != SELF_SHA: why.append('admission names another finalizer')
    ids = a.get('imageIds')
    if not isinstance(ids, list) or not all(isinstance(x, str) and HEX64.fullmatch(x) for x in ids): why.append('admission has no valid image Id list')
    elif PGIMG not in ids: why.append('the pg image Id is not among the admitted image Ids')
    return {'ok': not why, 'reasons': why, 'sha256': sha_bytes(raw)}


# ---------------- the loop volume ----------------
def volume_sample():
    """statvfs of the loop mount, bytes only (the finalizer cannot read inside the subuid-owned trees). Records the sample; returns the used fraction or None."""
    try:
        v = CFG['statvfs'](VOLUME)
    except OSError:
        VOLUME_STATE['errors'] += 1
        return None
    total, avail = v.f_blocks * v.f_frsize, v.f_bavail * v.f_frsize
    used = 1.0 - (avail / total) if total else 1.0
    VOLUME_STATE.update(samples=VOLUME_STATE['samples'] + 1, maxUsed=max(VOLUME_STATE['maxUsed'], used), last={'totalBytes': total, 'availBytes': avail, 'usedFraction': round(used, 4)},
                        lastAt=now())
    return used


def volume_check():
    """Retirement: the volume is a fixed-size filesystem of its own (a mount point, not larger than the 1 GiB cap), and the last sample is readable."""
    last = VOLUME_STATE['last']
    mounted = bool(CFG['is_mountpoint'](VOLUME))
    return {'mountPoint': mounted, 'withinCap': bool(last and 0 < last['totalBytes'] <= VOLUME_CAP), 'sampled': VOLUME_STATE['samples'] > 0,
            'maxUsedFraction': VOLUME_STATE['maxUsed'], 'last': last, 'sampleErrors': VOLUME_STATE['errors'],
            'budgetHit': VOLUME_STATE['maxUsed'] >= VOLUME_BUDGET}


# ---------------- inventory and status (r5: caps are parameters, the status is learn-pod-status/1 for a role) ----------------
def inventory(path, ident_ok, tar_rc, tarerr, over, members_left, content_left):
    fd = open_nofollow(path, os.O_RDONLY)
    f = os.fdopen(fd, 'rb')
    try:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode): return {'verdict': 'rejected:truncated', 'reason': 'stream not regular'}, None, False
        inv = {'streamBytes': st.st_size}
        if over is True or st.st_size > content_left + members_left * 1024 + 10240: inv['verdict'] = 'rejected:budget'; return inv, None, False
        if over is None: inv['verdict'] = 'rejected:truncated'; inv['reason'] = 'archive timed out'; return inv, None, False
        f.seek(max(0, st.st_size - 1024)); tail = f.read(1024); f.seek(0)
        if not (st.st_size >= 1024 and st.st_size % 512 == 0 and tail == b'\0' * 1024):
            inv['verdict'] = 'rejected:truncated'; return inv, None, False
        members, status, has_done, problems, count, apparent = [], None, False, set(), 0, 0
        rd = [0]
        try:
            with tarfile.open(fileobj=f, mode='r:', errorlevel=2) as tf:
                for m in tf:
                    tf.members = []                                 # no unbounded member cache
                    nm = m.name[2:] if m.name.startswith('./') else m.name
                    if nm in ('', '.'): continue
                    count += 1
                    if count > members_left:                        # remember only that the cap was exceeded
                        continue
                    kind = ('reg' if m.isreg() else 'dir' if m.isdir() else 'symlink' if m.issym() else 'hardlink' if m.islnk()
                            else 'fifo' if m.isfifo() else 'device' if (m.ischr() or m.isblk()) else 'other')
                    depth = nm.strip('/').count('/'); h = None
                    if kind == 'reg':
                        fh, hh, n = tf.extractfile(m), hashlib.sha256(), 0
                        keep = nm == '.status' and m.size == 4096; buf = []
                        while True:
                            c = fh.read(1 << 20)
                            if not c: break
                            hh.update(c); n += len(c); rd[0] += len(c)
                            if rd[0] >= DROP_EVERY: drop(f.fileno()); rd[0] = 0
                            if keep: buf.append(c)
                        if n != m.size: raise tarfile.ReadError('short member')
                        h = hh.hexdigest(); apparent += m.size
                        if nm == '.status': status = b''.join(buf) if keep else ('size', m.size)
                        if nm == '.done': has_done = True
                    members.append({'name': nm, 'kind': kind, 'size': m.size, 'depth': depth, 'sha256': h})
                    if kind not in ('reg', 'dir'): problems.add('type')
                    if depth > MAX_DEPTH: problems.add('depth')
                    if nm.startswith('/') or '..' in nm.split('/'): problems.add('path')
        except (tarfile.TarError, EOFError, OSError):
            inv['verdict'] = 'rejected:truncated'; return inv, None, False
        inv['memberCount'] = count
        if not ident_ok:
            inv['verdict'] = 'rejected:identity'; return inv, None, False
        inv['apparentBytes'] = apparent
        if count > members_left: inv['verdict'] = 'rejected:count'
        elif apparent > content_left: inv['verdict'] = 'rejected:size'
        elif problems: inv['verdict'] = 'rejected:type'; inv['problems'] = sorted(problems)
        elif tar_rc != 0 or tarerr > 0: inv['verdict'] = 'rejected:tar'
        else: inv['verdict'] = 'accepted'
        inv['members'] = members
        return inv, status, has_done
    finally:
        try: os.posix_fadvise(f.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)
        except OSError: pass
        f.close()


def parse_status(b, has_done, role):
    if not has_done: return {'status': 'absent'}                    # no .done -> absent, never ok
    if b is None: return {'status': 'malformed', 'reason': 'missing'}
    if isinstance(b, tuple): return {'status': 'malformed', 'reason': f'size {b[1]}'}
    try: j = json.loads(b.decode('utf-8').rstrip(' '))
    except (UnicodeDecodeError, ValueError): return {'status': 'malformed', 'reason': 'invalid json'}
    if not isinstance(j, dict) or j.get('schema') != STATUS_SCHEMA or j.get('role') != role: return {'status': 'malformed', 'reason': 'wrong schema or role'}
    if j.get('phase') != 'terminal' or j.get('reason') not in STATUS_REASONS or isinstance(j.get('exit'), bool) or not isinstance(j.get('exit'), int):
        return {'status': 'malformed', 'reason': 'not final'}
    return {'status': 'valid', 'value': {k: j.get(k) for k in ('exit', 'reason', 'detail')}}


def role_outcome(role, st, inv, vol_low):
    """Whether this role ended the way the run needs: accepted archive, a valid terminal status, and a reason the role is allowed to end with. Also
    whether an enospc claim is consistent with the volume's last sample (roles that write the volume only)."""
    ok = inv.get('verdict') == 'accepted' and st.get('status') == 'valid' and st['value']['reason'] in OK_REASONS[role]
    enospc = 'unknown' if st.get('status') != 'valid' or inv.get('verdict') != 'accepted' else 'n/a' if role == 'feed' else (
        'yes' if (st['value']['reason'] == 'enospc' and vol_low) else 'inconsistent' if (st['value']['reason'] == 'enospc') != vol_low else 'no')
    return ok, enospc


# ---------------- archiving one role from its live container ----------------
def archive_role(role, dest_dir, tag):
    """Pause the role's container, stream its /out/<role> through `podman unshare tar` into dest_dir, check the identity before and after, unpause, and judge the
    stream. Returns (result dict, inventory, status bytes, has_done). Charges the shared member and content budgets."""
    ctr = CTRS[role]
    res = {'role': role, 'container': ctr}
    cid, pid = insp(ctr, '{{.Id}}'), insp(ctr, '{{.State.Pid}}')
    if not cid or not pid or pid == '0':
        res.update(archive={'skipped': 'no running container'}, custody={'verdict': 'rejected:identity', 'reason': 'no running container'})
        return res, {'verdict': 'rejected:identity'}, None, False
    res['pauseRc'] = run(['podman', 'pause', ctr], 30)[0]
    try:
        s0 = ident_sample(ctr, pid, cid)
        os.makedirs(dest_dir, 0o700, exist_ok=True)
        out_fd = open_nofollow(f'{dest_dir}/{role}.tar', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        err_fd = open_nofollow(f'{dest_dir}/{role}.err', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        try:
            budget_s = max(1.0, min(300.0, CUSTODY_DEADLINE_S - ((time.monotonic() * 1e6 - STATE['anchor']) / 1e6 if 'anchor' in STATE else time.monotonic() - T0)))
            members_left, content_left = BUDGET['members'], BUDGET['content']
            tar_rc, nbytes, over = archive(CFG['unshare'] + CFG['tar'] + ['--format=ustar', '--one-file-system', '-C', f"{CFG['proc']}/{pid}/root/out/{role}", '-cf', '-', '.'],
                                           out_fd, err_fd, content_left + members_left * 1024 + 10240, timeout_s=budget_s)
            os.fsync(out_fd); os.posix_fadvise(out_fd, 0, 0, os.POSIX_FADV_DONTNEED)
        finally:
            os.close(out_fd); os.close(err_fd)
        s1 = ident_sample(ctr, pid, cid)
    finally:
        res['unpauseRc'] = run(['podman', 'unpause', ctr], 30)[0]
    tarerr = os.stat(f'{dest_dir}/{role}.err').st_size
    ident_ok = (s0['idc'] and s1['idc'] and s0['start'] == s1['start'] and s0['start'] is not None
                and s0['frozen'] == s1['frozen'] == 'frozen 1' and s0['state'] == s1['state'] == 'paused')
    res['identity'] = {'before': {k: v for k, v in s0.items() if k != 'cg'}, 'after': {k: v for k, v in s1.items() if k != 'cg'}, 'identOk': ident_ok}
    res['archive'] = {'tarRc': tar_rc, 'tarerrBytes': tarerr, 'streamBytes': nbytes, 'overCap': over is True, 'timedOut': over is None}
    mark('FIN', role=role, phase=tag, ident_ok=int(ident_ok), tar_rc=tar_rc, tarerr_bytes=tarerr, over=int(over is True))
    inv, status_b, has_done = inventory(f'{dest_dir}/{role}.tar', ident_ok, tar_rc, tarerr, over, BUDGET['members'], BUDGET['content'])
    BUDGET['members'] = max(0, BUDGET['members'] - inv.get('memberCount', 0))
    BUDGET['content'] = max(0, BUDGET['content'] - inv.get('apparentBytes', 0))
    res['custody'] = {k: v for k, v in inv.items() if k != 'members'}
    res['custody']['memberNames'] = len(inv.get('members') or [])
    return res, inv, status_b, has_done


def extract_role(role, dest_dir, inv):
    """For an accepted stream: extract it (r4 extract) into <dest_dir>/extract/<role>; the stream is then removed. Returns the file manifest."""
    manifest = extract(f'{dest_dir}/{role}.tar', f'{dest_dir}/extract-{role}') if inv.get('verdict') == 'accepted' else []
    if os.path.lexists(f'{dest_dir}/{role}.tar'):
        os.unlink(f'{dest_dir}/{role}.tar')
    return manifest


# ---------------- checkpoints: the recovery driver asks, the finalizer archives and acknowledges, then the driver acts ----------------
def read_request(n):
    fd = open_nofollow(f'{CKDIR}/req-{n}', os.O_RDONLY | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as f:
        b = f.read(513)
    if len(b) > 512:
        refuse('checkpoint request over 512 bytes')
    j = json.loads(b)
    if not isinstance(j, dict) or j.get('n') != n or not re.fullmatch(r'[a-z0-9][a-z0-9-]{0,39}', str(j.get('label', ''))):
        refuse('checkpoint request: bad n or label')
    return j, sha_bytes(b)


def process_checkpoints(done, state):
    """Serve every consecutive request req-<n> (n = done+1, done+2, ...) up to MAX_CHECKPOINTS; one acknowledgement ack-<n> per request. Anything else in the
    directory is ignored and counted. Returns the new count."""
    while done < MAX_CHECKPOINTS and os.path.lexists(f'{CKDIR}/req-{done + 1}'):
        n = done + 1
        entry = {'n': n}
        try:
            req, req_sha = read_request(n)
            entry.update(label=req['label'], requestSha256=req_sha)
        except (OSError, ValueError, Refuse) as e:
            entry['error'] = f'request unusable: {type(e).__name__}'
            req = None
        chain('checkpoint-request', n=n, ok=req is not None)
        roles = {}
        dest = f'{CUST}/checkpoints/{n}'
        if req is not None:
            for role in ROLES:
                res, inv, status_b, has_done = archive_role(role, dest, f'checkpoint-{n}')
                man = extract_role(role, dest, inv)                    # [] and the stream removed unless accepted
                roles[role] = {'custody': res['custody'], 'status': parse_status(status_b, has_done, role), 'extracted': man, 'unpauseRc': res.get('unpauseRc')}
        entry['roles'] = roles
        state.append(entry)
        ack = json.dumps({'n': n, 'requestSha256': entry.get('requestSha256'), 'ok': req is not None,
                          'roles': {r_: v['custody'].get('verdict') for r_, v in roles.items()}, 'chainHead': CHAIN['head']}, sort_keys=True).encode()
        fd = open_nofollow(f'{CKDIR}/ack-{n}', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'wb') as o:
            o.write(ack); o.flush(); os.fsync(o.fileno())
        chain('checkpoint-ack', n=n, ackSha256=sha_bytes(ack))
        mark('FIN', checkpoint=n)
        done = n
    return done


# ---------------- what the pod did with the input, from the containers before they are removed ----------------
def inputs_evidence():
    """inputRead: the pod operator's marker exists. containersStartedWithInput: names of the pod's containers that were STARTED with a mount at /in; None when
    it cannot be determined (the claim tooling treats None as 'not empty', so a void is refused): the census failed, an inspect failed, or the marker exists and no
    container remains to ask."""
    out = {'inputRead': os.path.lexists(MARKER), 'containersStartedWithInput': None}
    rc, names, _ = run(['podman', 'ps', '-a', '--filter', f'name=^{POD}-', '--format', '{{.Names}}'], 15)
    if rc != 0:
        return out
    ctrs = sorted(names.split())
    if not ctrs:
        # A census that succeeded and lists nothing. The pod operator creates the input-read marker BEFORE it first reads or mounts the input (design section 4), so with
        # the marker absent no container can have started with /in and the answer is the empty list. With the marker present, nothing remains to ask: not determinable.
        if not out['inputRead']:
            out['containersStartedWithInput'] = []
        return out
    started, ok = [], True
    for ctr in ctrs:
        v = insp(ctr, '{{json .Mounts}}|{{.State.StartedAt}}')
        if v is None or '|' not in v:
            ok = False
            continue
        mounts, started_at = v.rsplit('|', 1)
        try:
            dests = [m.get('Destination') for m in json.loads(mounts) or []]
        except ValueError:
            ok = False
            continue
        if '/in' in dests and not started_at.startswith('0001-01-01'):
            started.append(ctr)
    out['containersStartedWithInput'] = started if ok else None
    return out


# ---------------- pg: SIGINT (fast shutdown), then pg_controldata of the stopped cluster ----------------
def stop_pg():
    info = {'container': PGC}
    fmt = '{{.State.Status}}|{{.State.ExitCode}}|{{.State.OOMKilled}}|{{.Image}}'
    before = insp(PGC, fmt)
    info['before'] = before
    parts = (before or '').split('|')
    info['imageMatchesPin'] = len(parts) == 4 and parts[3] == PGIMG
    info['killRc'] = run(['podman', 'kill', '--signal', 'INT', PGC], 15)[0]
    for _ in range(PG_STOP_WAIT_S):
        v = insp(PGC, fmt)
        if v is None or v.split('|')[0] in ('exited', 'stopped'):
            break
        sleep(1)
    after = insp(PGC, fmt)
    info['after'] = after
    a = (after or '').split('|')
    info['exited'] = len(a) == 4 and a[0] in ('exited', 'stopped')
    info['exitCode'] = int(a[1]) if info['exited'] and a[1].lstrip('-').isdigit() else None     # a running container's ExitCode is 0 and means nothing
    info['oomKilled'] = a[2] == 'true' if len(a) == 4 else None
    return info


def controldata():
    """One short, network-less container from the pinned pg image reads the stopped cluster's control file through a read-only bind of /pgdata."""
    name = f'learn-fin-{OPID}-pgcd'
    argv = ['podman', 'run', '--rm', '--name', name, '--pull=never', '--network=none', '--user', '999:999', '--cap-drop=all', '--security-opt=no-new-privileges',
            '--security-opt', f'seccomp={SECCOMP}', '--read-only', '--memory', '128m', '--pids-limit', '32', '-v', f'{VOLUME}/pgdata:/pgdata:ro', PGIMG,
            'pg_controldata', '/pgdata/data']
    rc, out, err = run(argv, PGCD_TIMEOUT_S)
    info = {'rc': rc, 'outputBytes': len(out), 'outputSha256': sha_bytes(out.encode()), 'stderrTail': err[-300:]}
    for line in out.splitlines():
        k, _, v = line.partition(':')
        if k.strip() in CONTROLDATA_KEYS:
            info[CONTROLDATA_KEYS[k.strip()]] = v.strip()
    run(['podman', 'rm', '-f', name], 20)
    return info


def pg_ok(stop, cd):
    return bool(stop.get('imageMatchesPin') and stop.get('exitCode') == 0 and stop.get('oomKilled') is False and cd.get('rc') == 0 and cd.get('clusterState') == 'shut down')


# ---------------- retirement (design section 8) ----------------
def slice_now():
    try:
        return {'memory': int(open(CFG['slice_dir'] + '/memory.current').read()), 'pids': int(open(CFG['slice_dir'] + '/pids.current').read())}
    except (OSError, ValueError):
        return None


def retirement(cg_rel, snapshot_start, extra=None):
    """Every check of design section 8, recorded individually. Returns (block, retired)."""
    r = dict(extra or {})
    rc, pods, _ = run(['podman', 'pod', 'ps', '-a', '--filter', f'name=^{POD}$', '--format', '{{.Name}}'], 15)
    r['podGone'] = rc == 0 and pods.strip() == ''
    rc1, cs, _ = run(['podman', 'ps', '-a', '--filter', f'name=^{POD}-', '--format', '{{.Names}}'], 15)
    rc2, cs2, _ = run(['podman', 'ps', '-a', '--filter', f'name=^learn-fin-{OPID}-', '--format', '{{.Names}}'], 15)
    r['containersGone'] = rc1 == 0 and rc2 == 0 and cs.strip() == '' and cs2.strip() == ''
    unit_cg = (CFG['cgroot'] + cg_rel) if cg_rel.startswith('/') else f"{CFG['cgroot']}/user.slice/user-30034.slice/user@30034.service/app.slice/{OPU}"
    r['podCgroupPath'], r['podCgroupFromSystemd'] = unit_cg, cg_rel.startswith('/')
    r['podCgroupRetired'] = cgroup_retired(unit_cg)
    try:
        r['podCgroupProcsEmpty'] = open(unit_cg + '/cgroup.procs').read().strip() == ''
    except FileNotFoundError:
        r['podCgroupProcsEmpty'] = True
    except OSError:
        r['podCgroupProcsEmpty'] = False
    # Design section 8: no unit of this op is failed, activating or deactivating, with `reset-failed` run first. A pod unit that the stop left `failed` is normal and is
    # recorded; but operator r6's sweep refuses a failed unit as busy, so a unit that STAYS failed after the reset would block the next op: that is the retirement failure.
    def op_units():
        rc_, rows_, _ = run(['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-pod-*', 'learn-fin-*'], 30)
        units = [l.replace('●', ' ').split() for l in rows_.splitlines()]
        return rc_, [x for x in units if len(x) >= 4 and (x[0] == OPU or x[0].startswith((f'{POD}-', f'learn-fin-{OPID}-')))]      # the finalizer's own unit is not listed by these names
    rc, mine = op_units()
    r['unitStatesBeforeReset'] = {x[0]: x[2] for x in mine}
    r['resetFailedRc'] = [run(['systemctl', '--user', 'reset-failed', u], 10)[0] for u in sorted({OPU, *[x[0] for x in mine]})]
    rc2, mine2 = op_units()
    r['unitStates'] = {x[0]: x[2] for x in mine2}
    r['unitsClean'] = rc == 0 and rc2 == 0 and all(x[2] == 'inactive' for x in mine2)
    rc, sec, _ = run(['podman', 'secret', 'ls', '--format', '{{.Name}}'], 15)
    r['secretsGone'] = rc == 0 and not any(n.startswith(f'{POD}-') for n in sec.split())
    r['credentialTmpfsGone'] = not os.path.lexists(CRED)
    if VOLUME_STATE['samples'] == 0:
        volume_sample()                                    # a path that never reached the polling loop still reads the volume once
    r['volume'] = volume_check()
    r['volumeOk'] = bool(r['volume']['mountPoint'] and r['volume']['withinCap'] and r['volume']['sampled'] and r['podCgroupProcsEmpty'])
    r['snapshot'] = snapshot_verify(snapshot_start) if snapshot_start else {'ok': False, 'error': 'no snapshot state was read at the start'}
    deadline = time.monotonic() + SLICE_WAIT_S
    while True:
        cur, base = slice_now(), SLICE0.get('base')
        returned = bool(cur and base and cur['memory'] <= base['memory'] + SLICE_MEM_SLACK and cur['pids'] <= base['pids'] + SLICE_PIDS_SLACK)
        if returned or time.monotonic() >= deadline:
            break
        sleep(1)
    r['slice'] = {'baseline': SLICE0.get('base'), 'end': cur, 'returned': returned}
    keys = ('podGone', 'containersGone', 'podCgroupRetired', 'podCgroupProcsEmpty', 'unitsClean', 'secretsGone', 'credentialTmpfsGone', 'volumeOk')
    retired = all(r[k] for k in keys) and r['snapshot']['ok'] and r['slice']['returned']
    return r, retired


# ====================================================================================== the run
def file_chunks(path):
    with open(path, 'rb') as a:
        n = 0
        while True:
            c = a.read(1 << 20)
            if not c: os.posix_fadvise(a.fileno(), 0, 0, os.POSIX_FADV_DONTNEED); return
            n += len(c)
            if n % DROP_EVERY < len(c): drop(a.fileno())
            yield c


def copy_evidence(src_root, prefix, manifest):
    for e in manifest:
        evidence_file(f'{prefix}/{e["name"]}', file_chunks(f'{src_root}/{e["name"]}'))


def stop_pod():
    return {'stopRc': run(['podman', 'pod', 'stop', '-t', '10', POD], 40)[0], 'rmRc': run(['podman', 'pod', 'rm', '-f', POD], 40)[0]}


def wait_unit_inactive(n=60):
    for _ in range(n):
        if show(OPU, 'ActiveState').get('ActiveState') in ('inactive', 'failed', ''): return
        sleep(1)


def safe_inputs():
    """inputs_evidence() that cannot raise: every record, on every path, carries the two fields the claim tooling reads. On an error the marker is still looked at and
    the list is null."""
    try:
        return inputs_evidence()
    except Exception:
        try:
            read = os.path.lexists(MARKER)
        except Exception:
            read = None
        return {'inputRead': read, 'containersStartedWithInput': None}


def role_done(role):
    pid = insp(CTRS[role], '{{.State.Pid}}')
    return bool(pid and pid != '0' and done_marker(pid, role))


def early_finish(kind, schema_retired, cg_rel):
    """The pod unit never became active, or ended before every role was done: the inputs evidence is taken from whatever containers remain, the pod is
    removed, and the same retirement checks as any other path decide between `schema_retired` and a retirement failure."""
    REC.update(safe_inputs())
    wait_unit_inactive()
    REC['stop'] = stop_pod()
    REC['opUnit'] = show(OPU, 'ActiveState', 'Result')
    block, retired = retirement(cg_rel, STATE.get('snap0'), {'earlyPath': kind})
    REC['retirement'] = block
    finish(schema_retired if retired else SCHEMA_RETIREMENT_FAILURE)


def claim_ids():
    """An op id is used once. If the evidence, custody OR checkpoint directory exists, this id was run (or is running): exit 70 and write NOTHING, so a second finalizer can
    never replace the first one's record. All three are looked at before any is created; a race on either creation exits 70 before any side effect (a lost race on the second removes the first again)."""
    if any(os.path.lexists(d) for d in (EVID, CUST, CKDIR)):
        mark('FIN', error='op id already used'); sys.exit(70)
    try:
        os.makedirs(CUST, 0o700, exist_ok=False)
    except FileExistsError:                                  # another finalizer won the race: nothing of ours exists to undo, and main's handler must never run (it would stop the winner's pod)
        mark('FIN', error='op id already used'); sys.exit(70)
    try:
        os.makedirs(CKDIR, 0o700, exist_ok=False)
    except FileExistsError:
        os.rmdir(CUST)
        mark('FIN', error='op id already used'); sys.exit(70)


def flow():
    claim_ids()
    chain('start', finalizer=SELF_SHA)
    SLICE0['base'] = slice_now()
    try:
        snap0 = snapshot_read()
        REC['snapshotStart'] = {'claimSha256': snap0['claimSha256'], 'sealedManifestSha256': snap0['sealedManifestSha256'], 'snapshotId': snap0['snapshotId'], 'files': len(snap0['files'])}
    except (Refuse, OSError, ValueError, KeyError) as e:
        snap0 = None
        REC['snapshotStart'] = {'error': f'{type(e).__name__}: {str(e)[:160]}'}
    STATE['snap0'] = snap0
    REC['admission'] = admission_check()
    # 1. anchor on the pod unit
    while True:
        s = show(OPU, 'ActiveEnterTimestampMonotonic', 'ActiveState', 'ExecMainPID')
        anchor = int(s.get('ActiveEnterTimestampMonotonic', '0') or 0)
        if anchor > 0: break
        if time.monotonic() - T0 > ANCHOR_WAIT_S:
            mark('FIN', outcome='pod_failed_to_start'); chain('failed-to-start')
            early_finish('failed-to-start', SCHEMA_FAILED_START, '')
        sleep(1)
    exec_main_pid = s.get('ExecMainPID'); REC['anchorMonotonicUs'] = anchor; REC['execMainPid'] = exec_main_pid
    STATE['anchor'] = anchor
    for _try in range(CG_READ_TRIES):                       # a failed or empty read must not leave the retirement checks to a guessed path
        cg_rel = show(OPU, 'ControlGroup').get('ControlGroup', '')
        if cg_rel.startswith('/'): break
        sleep(1)
    REC['podControlGroupReadTries'] = _try + 1
    STATE['cg_rel'] = cg_rel; REC['podControlGroup'] = cg_rel
    chain('anchor', anchorUs=anchor, execMainPid=exec_main_pid)
    # 2. trigger: every role done, the soft deadline, the volume budget, or the unit ending
    trigger, last_wait, last_vol, ck_done, unknown = None, time.monotonic(), -1e9, 0, 0
    cks = REC['checkpoints'] = []
    while trigger is None:
        st = show(OPU, 'ActiveState').get('ActiveState')
        ck_done = process_checkpoints(ck_done, cks)
        if time.monotonic() - last_vol >= VOLUME_SAMPLE_S:
            last_vol = time.monotonic()
            used = volume_sample()
            if used is not None and used >= VOLUME_BUDGET: trigger = 'budget'; break
        if all(role_done(r_) for r_ in ROLES): trigger = 'done'; break
        if int(time.monotonic() * 1e6) - anchor >= SOFT_S * 1e6: trigger = 'time'; break
        unknown = unknown + 1 if st in (None, '') else 0     # a failed `systemctl show` says nothing about the unit: it is not "ended"
        if st in (None, '') and unknown <= UNIT_UNKNOWN_MAX:
            if unknown == 1: chain('unit-state-unknown')
        elif st not in ('active', 'activating'):
            if st in (None, ''): REC['unitStateUnknown'] = {'consecutivePolls': unknown, 'concluded': 'ended'}
            mark('FIN', outcome='pod_ended_without_done', state=st); chain('ended-without-done', state=st)
            early_finish('ended-without-done', SCHEMA_UNRECOVERABLE, cg_rel)
        if time.monotonic() - last_wait >= 60: chain('wait'); last_wait = time.monotonic()
        sleep(POLL_S)
    mark('FIN', trigger=trigger); chain('trigger', trigger=trigger); REC['trigger'] = trigger
    # 3. what the pod did with the input, then each role from its live container
    REC.update(safe_inputs())
    volume_sample()
    roles, final_extracts = {}, {}
    vol_last = VOLUME_STATE['last']
    vol_low = bool(vol_last and vol_last['availBytes'] < VOLUME_LOW)
    for role in ROLES:
        res, inv, status_b, has_done = archive_role(role, f'{CUST}/final', 'final')
        if inv['verdict'] in ('rejected:budget', 'rejected:truncated', 'rejected:identity', 'rejected:count'): res['status'] = {'status': 'unavailable'}
        else: res['status'] = parse_status(status_b, has_done, role)
        res['outcomeOk'], res['enospc'] = role_outcome(role, res['status'], inv, vol_low)
        final_extracts[role] = extract_role(role, f'{CUST}/final', inv)
        res['custody']['extracted'] = final_extracts[role]
        roles[role] = res
        chain('verdict', role=role, verdict=inv['verdict'])
    REC['roles'] = roles
    late = int(time.monotonic() * 1e6) - anchor > CUSTODY_DEADLINE_S * 1e6
    REC['custodyLate'] = late
    # 4. pg: SIGINT, then the pod, then pg_controldata of the stopped cluster
    pg = REC['pg'] = {'stop': stop_pg()}
    REC['stop'] = stop_pod()
    wait_unit_inactive()
    volume_sample()
    pg['controldata'] = controldata()
    pg['ok'] = pg_ok(pg['stop'], pg['controldata'])
    mark('FIN', pg_ok=int(pg['ok']), stop_rc=REC['stop']['stopRc'])
    # 5. markers, retirement
    fin = show(OPU, 'ActiveState', 'Result', 'ExecMainStatus'); REC['opUnit'] = fin
    mk = markers(exec_main_pid); REC['markers'] = mk
    memev = [m for m in mk['lines'] if m['unit'] == 'op' and m['msg'].startswith('MEMEV ')]
    if len(memev) != 1: REC['oom'] = 'unknown'
    else:
        kv = dict(x.split('=', 1) for x in memev[0]['msg'].split()[1:] if '=' in x)
        REC['oom'] = 'yes' if int(kv.get('oom_kill', '0') or 0) > 0 else 'no'
    REC['deadline'] = fin.get('Result') == 'timeout'
    block, retired = retirement(cg_rel, snap0, {'trigger': trigger})
    REC['retirement'] = block
    # 6. evidence copy and the record
    evidence_dir()
    for role in ROLES:
        copy_evidence(f'{CUST}/final/extract-{role}', f'out/{role}', final_extracts[role])
    for c in cks:
        for role, v in c['roles'].items():
            copy_evidence(f"{CUST}/checkpoints/{c['n']}/extract-{role}", f"checkpoints/{c['n']}/{role}", v['extracted'])
    if late: REC['custodyNote'] = 'custody completed after the 31:30 deadline; partial/late'; finish(SCHEMA_RETIREMENT_FAILURE)
    if not retired: finish(SCHEMA_RETIREMENT_FAILURE)
    ok = (all(r_['outcomeOk'] and r_.get('unpauseRc', 0) == 0 for r_ in roles.values()) and pg['ok'] and REC['oom'] == 'no' and not REC['deadline'] and trigger == 'done'
          and REC['admission']['ok'] and block['podCgroupFromSystemd'] and REC['containersStartedWithInput'] is not None and not block['volume']['budgetHit']
          and all(c['roles'] and all(v['custody'].get('verdict') == 'accepted' and v.get('unpauseRc') == 0 for v in c['roles'].values()) for c in cks))      # a failed checkpoint has no roles
    finish(SCHEMA_OK if ok else SCHEMA_KILLED)


def main(argv=None, environ=None, euid=None, **over):
    configure(sys.argv[1:] if argv is None else argv, os.environ if environ is None else environ, euid, **over)
    try:
        flow()
    except SystemExit:
        raise
    except Exception as e:
        REC['error'] = f'{type(e).__name__}: {str(e)[:200]}'; mark('FIN', error=type(e).__name__)
        if 'containersStartedWithInput' not in REC:
            REC.update(safe_inputs())                      # before the pod is removed: the containers are what it asks
        try:
            for ctr in CTRS.values(): run(['podman', 'unpause', ctr], 30)
            REC['stop'] = stop_pod()
        except Exception: pass
        if 'retirement' not in REC:
            try: REC['retirement'], _ = retirement(STATE.get('cg_rel') or '', STATE.get('snap0'), {'earlyPath': 'exception'})
            except Exception as e2: REC['retirement'] = {'earlyPath': 'exception', 'error': type(e2).__name__}
        finish(SCHEMA_RETIREMENT_FAILURE)


if __name__ == '__main__':
    main()

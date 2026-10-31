#!/usr/bin/python3 -I
"""LEARN finite executor: POD OPERATOR GATE LAYER r1 (design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md sections 3, 4, 10
and 14.1; Refs FS-GG/.github#4647). Runs ONLY as uid 30034 (fsgg-learn-qual):
    learn-pod-operator-r1.py --precheck-only|--run --id <op-id> --argv-json '<JSON list>' --source-identity <64 hex>

THIS REVISION IS THE GATE LAYER ONLY. It decides whether a pod op may start and it records the decision; it starts nothing. The images, the
role entrypoint, finalizer r5, the loop-volume and container prechecks and the pod start are later PRs. Until then:
  * --precheck-only never exits 0 (it appends 'pod containers precheck is not implemented in this revision'), so Root cannot take a claim
    from it by mistake;
  * --run stops after the gates and the claim verification, BEFORE the input-read marker is created, so a run of this revision consumes
    no claim ('pod start is not implemented in this revision').
The marker and inputMounted ordering are implemented in begin_input_access and tested through an injected start function.

Gates, in order (the first refusal wins; nothing is started or written before the lock except the RESULT on stdout):
  1 identity     euid is 30034; arguments are exact; image ids and the finalizer sha256 are pinned in this file (empty in this revision: refuse).
  2 pins         every pinned image id is present locally by Id; the finalizer file hashes to its pin through a pinned descriptor chain.
  3 slice        the live user-30034.slice limits are exactly 6 GiB and 1280 pids (a missing drop-in fails closed).
  4 lock         the SHARED operator lock ~/.local/state/learn-operator.lock, the same flock semantics as operator r6, held to exit.
  5 exclusion    SYMMETRIC: refuse while ANY learn-op-* reader op or ANY learn-pod-* pod unit is active or transitioning (failed, activating,
                 deactivating, reloading), a failed or transitioning learn-fin-* unit, or a leftover learn-pod-* pod.
  6 custody      an existing custody or evidence directory for the op id (or <id>-pre) refuses.
  7 acks         operator r6's retirement gate, verbatim, reading the persistent ack directory through pinned descriptors.
  8 evidence     operator r6's evidence-size refusal, verbatim.
  9 admission    the admission reader of learn_seal_claim.py, VERBATIM with its pinned chain: argv hash, source identity, this file's sha256,
                 the finalizer pin and the image ids must equal the admission written by Root, which must be unexpired and live at most 600 s.
 10 headroom     slice memory.max - memory.current >= 4.5 GiB, pids headroom >= 1000, MemAvailable >= 4 GiB, custody disk >= 1 GiB.
 --run adds: 11 claim  the live claim names this op, this source identity and a sealed manifest whose sha256 the claim records;
             12 nonce  runNonceSha256 = sha256(nonce) is recorded in the RESULT before anything can start;
             13 input  begin_input_access: verify the claim, create the input-read marker (O_EXCL), set gates.inputMounted BEFORE the mount, mount.

Verbatim blocks below are checked byte for byte against learn_seal_claim.py and operator r6 by tests/learn-pod-operator. Cooperative against `developer`.
"""
import fcntl, hashlib, json, os, re, secrets, stat, subprocess, sys, time
from datetime import datetime, timedelta, timezone

SCHEMA = 'home-learn-pod-operator/1'
REVISION = 'r1-gates'
MAX_TTL_S = 600
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,58}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
POD_MEMORY_HEADROOM, POD_PIDS_HEADROOM, POD_MEMAVAILABLE = int(4.5 * (1 << 30)), 1000, 4 << 30
SLICE_MEMORY_MAX, SLICE_PIDS_MAX = 6 << 30, 1280
TRANSITIONING = ('active', 'activating', 'deactivating', 'reloading', 'refreshing', 'failed')


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


PROD = {
    'uid': 30034, 'owner_uid': 0, 'chain_start': '/',
    'home': '/mnt/storage/containers/learn-qualification/account',
    'view': '/usr/local/libexec/fsgg-learn',
    'slice': '/sys/fs/cgroup/user.slice/user-30034.slice',
    'meminfo': '/proc/meminfo',
    'fin': '/usr/local/libexec/fsgg-learn/learn-finalizer-r5.py',
    'finalizer_sha': None,          # pinned by the PR that adds finalizer r5; None refuses
    'image_ids': [],                # pinned by the PR that adds the images; empty refuses
    'lock_wait_s': 120,
    'pod_precheck_implemented': False,
    'start_implemented': False,
}


def paths(env):
    st = f"{env['home']}/.local/state"
    return {'lock': f'{st}/learn-operator.lock', 'custody': f'{st}/learn-custody', 'evidence': f'{st}/learn-evidence', 'markers': f'{st}/learn-input',
            'ack_dir': f"{env['view']}/retirement-acks", 'claims': f"{env['view']}/claims", 'inputs': f"{env['view']}/inputs"}


# ============================================================ VERBATIM from deployment/learn-finite-profile/seal/learn_seal_claim.py
DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC


ADMIT_FIELDS = ('opId', 'argvJsonSha256', 'sourceIdentity', 'operatorSha256', 'finalizerSha256', 'imageIds')


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


def check_admission(path_root, op_id, argv_sha, ident, operator_sha, finalizer_sha, image_ids, chain_start, owner_uid=0, now=None):
    """Read-only reader side; returns [] when the admission matches, else the reasons. The pod operator is a separate
    single-file script run with -I, so it cannot import this module: it must carry this logic verbatim. The test vectors in
    tests/learn-seal-claim are the conformance set for that copy, and this CLI subcommand needs no root (it only reads)."""
    try:
        dfd = open_dir_chain(f'{path_root}/admissions', chain_start, owner_uid)
    except Refuse as e:
        return [str(e).replace('write path', 'admission path')] if 'not root-safe' in str(e) else ['admission is absent or unreadable']
    except OSError:
        return ['admission is absent or unreadable']
    try:
        try:
            fd = os.open(f'{op_id}.admit', os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=dfd)
        except OSError:
            return ['admission is absent or unreadable']
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > 65536:
                return ['admission path not root-safe: not a root-owned regular file closed to group and other writers']
            try:
                a = json.loads(f.read())
            except ValueError:
                return ['admission is absent or unreadable']
    finally:
        os.close(dfd)
    why = []
    want = {'opId': op_id, 'argvJsonSha256': argv_sha, 'sourceIdentity': ident, 'operatorSha256': operator_sha,
            'finalizerSha256': finalizer_sha, 'imageIds': sorted(image_ids)}
    for k in ADMIT_FIELDS:
        if a.get(k) != want[k]:
            why.append(f'{k} differs')
    now = now or datetime.now(timezone.utc)
    try:
        issued, expires = datetime.fromisoformat(a['issuedUtc']), datetime.fromisoformat(a['expiresUtc'])
    except (KeyError, ValueError, TypeError):
        return why + ['the admission has no valid issue or expiry time']
    if issued.tzinfo is None or expires.tzinfo is None:
        why.append('the admission times carry no time zone')
    else:
        if expires <= issued:
            why.append('the admission expires before it was issued')
        if (expires - issued).total_seconds() > MAX_TTL_S:
            why.append(f'the admission lifetime exceeds {MAX_TTL_S} seconds')
        if issued > now:
            why.append('the admission is dated in the future')
        if now > expires:
            why.append('the admission has expired')
    return why


# ============================================================ VERBATIM from deployment/learn-finite-profile/operator/learn-operator-r6.py
EVID_MAX_BYTES, EVID_MAX_ENTRIES = 32 << 30, 1 << 20


def read_record_nofollow(path, cap=16 << 20):
    """r5: bytes of a record file opened O_NOFOLLOW (regular, <= cap), else None"""
    try: fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    except OSError: return None
    try:
        st = os.fstat(fd)
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap: return None
        b = b''
        while len(b) <= cap:
            c = os.read(fd, 1 << 20)
            if not c: break
            b += c
        return b if len(b) <= cap else None
    finally: os.close(fd)


def evidence_usage(root):
    """r5 (3): allocated bytes (st_blocks * 512) of every file under the evidence root, no symlinks followed; stops early once
    over EVID_MAX_BYTES or EVID_MAX_ENTRIES entries (then 'bounded': True)"""
    total, n, stack = 0, 0, [root]
    while stack:
        d = stack.pop()
        try: it = list(os.scandir(d))
        except OSError: return {'bytes': total, 'entries': n, 'error': 'unreadable ' + d}
        for e in it:
            n += 1
            try: st = e.stat(follow_symlinks=False)
            except OSError: continue
            total += st.st_blocks * 512
            if stat.S_ISDIR(st.st_mode): stack.append(e.path)
            if total > EVID_MAX_BYTES or n > EVID_MAX_ENTRIES: return {'bytes': total, 'entries': n, 'bounded': True}
    return {'bytes': total, 'entries': n, 'bounded': False}


def read_ack_pinned(path, start='/', owner_uid=0):
    """r6: bytes of an acknowledgement reached through pinned descriptors, or None. `path` is walked from `start` ('/' in production) with
    O_DIRECTORY|O_NOFOLLOW openat; every directory is fstat-checked on its descriptor (directory, owner, not group/other-writable); the file
    is opened O_NOFOLLOW|O_NONBLOCK (a FIFO is refused, never waited on) and fstat-checked (regular, owner, not group/other-writable, <= 4096 B).
    Nothing is decided from a path lstat."""
    flags = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
    if start != '/' and not path.startswith(start + '/'): return None
    parent, name = os.path.split(path)
    try: fd = os.open(start, flags)
    except OSError: return None
    try:
        def safe(f):
            st = os.fstat(f)
            return stat.S_ISDIR(st.st_mode) and st.st_uid == owner_uid and not (stat.S_IMODE(st.st_mode) & 0o022)
        if not safe(fd): return None
        for comp in [c for c in parent[len(start):].split('/') if c]:
            nfd = os.open(comp, flags, dir_fd=fd); os.close(fd); fd = nfd
            if not safe(fd): return None
        ffd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK | os.O_CLOEXEC, dir_fd=fd)      # O_NONBLOCK: a FIFO must be refused, not waited on
        try:
            st = os.fstat(ffd)
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > 4096: return None
            return os.read(ffd, 4097)
        finally: os.close(ffd)
    except OSError: return None
    finally: os.close(fd)


def retirement_gate(evid_root, ack_dir=None, chain_start='/', owner_uid=0):
    """r5 (§7): every retirement-failure (or unreadable) record must have a matching root-owned acknowledgement. r6: the acknowledgement is
    read from the persistent ACK_DIR through read_ack_pinned (ack_dir, chain_start and owner_uid are parameters only so the gate can be tested)"""
    ack_dir = ack_dir or ACK_DIR
    failures, acked, unacked = [], [], []
    try: entries = sorted(e.name for e in os.scandir(evid_root) if e.is_dir(follow_symlinks=False))
    except OSError as e: return {'error': f'{type(e).__name__}'}, ['<evidence root unreadable>']
    for name in entries:
        rp = f'{evid_root}/{name}/record.json'
        if not os.path.lexists(rp): continue
        b = read_record_nofollow(rp)
        try: sch = json.loads(b).get('schema') if b is not None else None
        except ValueError: sch = None
        if b is not None and sch != 'learn-op-retirement-failure/1' and sch is not None: continue
        failures.append(name); want = hashlib.sha256(b).hexdigest() if b is not None else None
        ap = f'{ack_dir}/{name}.retirement-ack'; ok = False
        if want:
            a = read_ack_pinned(ap, chain_start, owner_uid)
            ok = a is not None and a.decode('ascii', 'replace').strip() == want
        (acked if ok else unacked).append(name)
    return {'failures': failures, 'acked': acked, 'unacked': unacked, 'ackDir': ack_dir}, unacked


def parse_unit_rows(out):
    """r6: rows of `systemctl --user list-units` with at least four columns"""
    rows = [l.replace('●', ' ').split() for l in out.splitlines()]
    return [r_ for r_ in rows if len(r_) >= 4]


def used_ids(opid, custody_root, evid_root):
    """r6: existing custody or evidence directories for the op id or its precheck id; any of them refuses the run"""
    return [p for p in (f'{custody_root}/{opid}', f'{custody_root}/{opid}-pre', f'{evid_root}/{opid}', f'{evid_root}/{opid}-pre') if os.path.lexists(p)]


# ============================================================ pod operator code (new in this PR)
HELD_LOCKS = []
ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'      # the default retirement_gate falls back to; the gate is always called with the env's directory


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


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


def take_lock(path, wait_s, deps):
    """The SHARED operator lock, with operator r6's semantics: O_RDWR|O_CREAT|O_NOFOLLOW|O_CLOEXEC 0600, an exclusive non-blocking flock tried every
    0.5 s for up to `wait_s`, then 'operator lock busy'. The descriptor is returned and kept open to exit, so the lock covers the whole run."""
    lfd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    t0 = deps['monotonic']()
    while True:
        try:
            fcntl.flock(lfd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            return lfd, round(deps['monotonic']() - t0, 1)
        except BlockingIOError:
            if deps['monotonic']() - t0 > wait_s:
                os.close(lfd)
                refuse('operator lock busy')
            deps['sleep'](0.5)


def exclusion_conflicts(rows):
    """SYMMETRIC exclusion for a pod op: any learn-op-* reader op (precheck ops included) or learn-pod-* pod unit that is active or
    transitioning, and any failed or transitioning learn-fin-* unit. Returns the conflicting unit names."""
    out = []
    for r_ in rows:
        name, state = r_[0], r_[2]
        if name.startswith(('learn-op-', 'learn-pod-')) and state in TRANSITIONING:
            out.append(name)
        elif name.startswith('learn-fin-') and state in ('failed', 'activating', 'deactivating', 'reloading', 'refreshing'):
            out.append(name)
    return sorted(out)


def headroom(slice_dir, meminfo, custody_root, statvfs=os.statvfs):
    def rd(p):
        try:
            return open(p).read().strip()
        except OSError:
            return None
    pm, pc, mm, mc = (rd(f'{slice_dir}/{x}') for x in ('pids.max', 'pids.current', 'memory.max', 'memory.current'))
    mav = next((int(l.split()[1]) * 1024 for l in open(meminfo) if l.startswith('MemAvailable:')), 0)
    st = statvfs(custody_root)
    free = st.f_bavail * st.f_frsize
    ok = {'pids': pm == 'max' or bool(pm and pc and int(pm) - int(pc) >= POD_PIDS_HEADROOM),
          'memory': mm == 'max' or bool(mm and mc and int(mm) - int(mc) >= POD_MEMORY_HEADROOM),
          'memAvailable': mav >= POD_MEMAVAILABLE, 'custodyDisk': free >= 1 << 30}
    return ok, {'pidsMax': pm, 'pidsCurrent': pc, 'memMax': mm, 'memCurrent': mc, 'memAvailable': mav, 'custodyFree': free}


def verify_claim(env, ident, opid):
    """The live claim must name this op and this source identity, and its sealed manifest on disk must have the sha256 the claim records and
    the same source identity. Only metadata is read: the sealed COPY of the input is not opened (hashing it would be an input read)."""
    p = paths(env)
    raw = read_pinned(f"{p['claims']}/{ident}.claim", env['chain_start'], env['owner_uid'], 65536)
    claim = json.loads(raw)
    if claim.get('schema') != 'learn-input-claim/1':
        refuse('claim has another schema')
    if claim.get('opId') != opid:
        refuse('the live claim names another op')
    if claim.get('sourceIdentity') != ident:
        refuse('the claim has another source identity')
    sid = claim.get('snapshotId')
    if not isinstance(sid, str) or not OPID.match(sid):
        refuse('the claim has no valid snapshot id')
    smraw = read_pinned(f"{p['inputs']}/{sid}.manifest.json", env['chain_start'], env['owner_uid'], 1 << 20)
    if sha_bytes(smraw) != claim.get('sealedManifestSha256'):
        refuse('the sealed manifest does not have the sha256 the claim records')
    sm = json.loads(smraw)
    if sm.get('sourceIdentity') != ident or sm.get('snapshotId') != sid:
        refuse('the sealed manifest names another source or snapshot')
    return claim, sha_bytes(raw), sid


def create_marker(marker_root, opid, claim_sha, now):
    """The input-read marker is owned by THIS operator: <marker_root>/<op-id>/input-read, directory 0700, file O_EXCL. It is created before the
    first read or mount of the input and never removed. A second attempt for the same op id refuses."""
    os.makedirs(marker_root, 0o700, exist_ok=True)
    d = f'{marker_root}/{opid}'
    try:
        os.mkdir(d, 0o700)
    except FileExistsError:
        refuse('input-read directory already exists for this op id')
    p = f'{d}/input-read'
    fd = os.open(p, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(fd, 'w') as f:
        f.write(json.dumps({'opId': opid, 'claimSha256': claim_sha, 'utc': now().isoformat()}, sort_keys=True) + '\n')
        f.flush()
        os.fsync(f.fileno())
    return p


def begin_input_access(env, R, opid, ident, mount, now):
    """Ordering contract: (1) the claim is verified, and a refusal here creates nothing; (2) the input-read marker is created (O_EXCL);
    (3) gates.inputMounted is set to true; (4) only then is the input mounted. A run that dies during the mount therefore cannot show
    inputMounted false, and the marker exists before the first read or mount."""
    claim, claim_sha, sid = verify_claim(env, ident, opid)
    R['gates']['claim'] = {'generation': claim.get('generation'), 'snapshotId': sid, 'claimSha256': claim_sha}
    R['gates']['inputReadMarker'] = create_marker(paths(env)['markers'], opid, claim_sha, now)
    R['gates']['inputMounted'] = True
    mount(claim, sid)


def parse_args(argv):
    mode = argv[0] if argv else ''
    opts, i = {}, 1
    while i < len(argv):
        if not argv[i].startswith('--') or i + 1 >= len(argv) or argv[i] in opts:
            refuse('usage: --precheck-only|--run --id <op-id> --argv-json <JSON list> --source-identity <64 hex>')
        opts[argv[i]] = argv[i + 1]
        i += 2
    if mode not in ('--precheck-only', '--run') or set(opts) != {'--id', '--argv-json', '--source-identity'}:
        refuse('usage: --precheck-only|--run --id <op-id> --argv-json <JSON list> --source-identity <64 hex>')
    if not OPID.match(opts['--id']):
        refuse('bad op id')
    if not HEX64.match(opts['--source-identity']):
        refuse('source identity must be 64 lowercase hex')
    try:
        argv_list = json.loads(opts['--argv-json'])
    except ValueError:
        refuse('argv-json is not JSON')
    if not isinstance(argv_list, list) or not argv_list or not all(isinstance(x, str) for x in argv_list):
        refuse('argv-json must be a non-empty JSON list of strings')
    return mode, opts['--id'], opts['--argv-json'], argv_list, opts['--source-identity']


def default_deps():
    def run(argv, timeout=60, cap=1 << 22):
        try:
            p = subprocess.run(argv, capture_output=True, timeout=timeout, cwd='/')
            return p.returncode, p.stdout[:cap].decode('utf-8', 'replace'), p.stderr[-2000:].decode('utf-8', 'replace')
        except subprocess.TimeoutExpired:
            return None, '', 'timeout'
    def start_pod(claim, sid):
        refuse('pod start is not implemented in this revision')
    return {'run': run, 'geteuid': os.geteuid, 'now': lambda: datetime.now(timezone.utc), 'sleep': time.sleep, 'monotonic': time.monotonic,
            'start_pod': start_pod, 'nonce': lambda: secrets.token_hex(16)}


def gates(env, deps, R, mode, opid, argv_raw, ident):
    p = paths(env)
    me = os.path.abspath(__file__)
    self_sha = sha_bytes(open(me, 'rb').read())
    if deps['geteuid']() != env['uid']:
        refuse(f"refuse: run as fsgg-learn-qual (uid {env['uid']})")
    os.umask(0o077)
    if not env['image_ids']:
        refuse('no image ids are pinned in this revision')
    if not env['finalizer_sha']:
        refuse('the finalizer pin is empty in this revision (finalizer r5 is a later PR)')
    # 2 pins
    for iid in env['image_ids']:
        rc, out, _ = deps['run'](['podman', 'image', 'inspect', '--format', '{{.Id}}', iid], 30)
        if rc != 0 or out.strip() != iid:
            refuse(f'image {iid[:12]} is not present locally by Id')
    if sha_bytes(read_pinned(env['fin'], env['chain_start'], env['owner_uid'], 1 << 20)) != env['finalizer_sha']:
        refuse('the finalizer does not hash to its pin')
    R['gates']['pins'] = {'imageIds': list(env['image_ids']), 'finalizerSha256': env['finalizer_sha'], 'operatorSha256': self_sha}
    # 3 slice
    def rd(x):
        try:
            return open(f"{env['slice']}/{x}").read().strip()
        except OSError:
            return None
    lim = {'memoryMax': rd('memory.max'), 'pidsMax': rd('pids.max')}
    R['gates']['sliceLimits'] = {**lim, 'expected': {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}}
    if lim != {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}:
        refuse(f'slice limits absent: {lim}')
    # 4 lock
    os.makedirs(os.path.dirname(p['lock']), 0o700, exist_ok=True)
    lfd, waited = take_lock(p['lock'], env['lock_wait_s'], deps)
    R['gates']['operatorLockWaitSeconds'] = waited
    HELD_LOCKS.append(lfd)                                   # held open to process exit; tests close it
    # 5 exclusion
    rc, out, _ = deps['run'](['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-op-*', 'learn-fin-*', 'learn-pod-*'], 30)
    if rc != 0:
        refuse('the unit census failed')
    rows = parse_unit_rows(out)
    conflicts = exclusion_conflicts(rows)
    rc2, pods, _ = deps['run'](['podman', 'pod', 'ps', '-a', '--format', '{{.Name}}'], 30)
    if rc2 != 0:
        refuse('the pod census failed')
    leftover = sorted(n for n in pods.split() if n.startswith('learn-pod-'))
    R['gates']['exclusion'] = {'conflicts': conflicts, 'leftoverPods': leftover}
    if conflicts:
        refuse('exclusion refused: an op or pod is active or transitioning: ' + ','.join(conflicts))
    if leftover:
        refuse('exclusion refused: a pod exists: ' + ','.join(leftover))
    # 6 custody
    used = used_ids(opid, p['custody'], p['evidence'])
    if used or any(r_[0].startswith((f'learn-op-{opid}', f'learn-fin-{opid}', f'learn-pod-{opid}')) for r_ in rows):
        refuse('op id already used: ' + ','.join(used))
    # 7 acks
    R['gates']['retirementAcks'], unacked = retirement_gate(p['evidence'], p['ack_dir'], env['chain_start'], env['owner_uid'])
    if unacked:
        refuse('retirement-failure-unacknowledged: ' + ','.join(unacked))
    # 8 evidence size
    ev = evidence_usage(p['evidence'])
    ev['maxBytes'] = EVID_MAX_BYTES
    R['gates']['evidenceUsage'] = ev
    if ev.get('error') or ev['bytes'] > EVID_MAX_BYTES or ev['entries'] > EVID_MAX_ENTRIES:
        refuse(f"evidence size refused: {ev['bytes']} B in {ev['entries']} entries (max {EVID_MAX_BYTES} B)")
    # 9 admission (verbatim reader)
    why = check_admission(env['view'], opid, sha_bytes(argv_raw.encode()), ident, self_sha, env['finalizer_sha'], env['image_ids'], env['chain_start'],
                          env['owner_uid'], deps['now']())
    R['gates']['admission'] = {'reasons': why}
    if why:
        refuse('admission refused: ' + '; '.join(why))
    # 10 headroom
    hg, vals = headroom(env['slice'], env['meminfo'], p['custody'])
    R['gates']['headroom'] = {**hg, 'values': vals}
    if not all(hg.values()):
        refuse('headroom refused: ' + ','.join(k for k, v in hg.items() if not v))
    return self_sha


def main(argv, env=None, deps=None):
    env = dict(PROD, **(env or {}))
    deps = dict(default_deps(), **(deps or {}))
    R = {'schema': SCHEMA, 'operatorRevision': REVISION, 'utc': deps['now']().isoformat(), 'gates': {'inputMounted': False},
         'containers': [], 'errors': []}
    code = 0
    try:
        mode, opid, argv_raw, argv_list, ident = parse_args(argv)
        R.update(mode=mode, opId=opid, argv=argv_list, sourceIdentity=ident)
        R['operatorSha256'] = gates(env, deps, R, mode, opid, argv_raw, ident)
        if mode == '--precheck-only':
            if not env['pod_precheck_implemented']:
                refuse('pod containers precheck is not implemented in this revision')
        else:
            claim, claim_sha, sid = verify_claim(env, ident, opid)
            R['gates']['claim'] = {'generation': claim.get('generation'), 'snapshotId': sid, 'claimSha256': claim_sha}
            nonce = deps['nonce']()
            R['runNonceSha256'] = sha_bytes(nonce.encode())                     # recorded BEFORE anything can start
            if not env['start_implemented']:
                refuse('pod start is not implemented in this revision; no input-read marker was created')
            begin_input_access(env, R, opid, ident, deps['start_pod'], deps['now'])
    except Refuse as e:
        R['errors'].append(str(e))
        code = 5
    except Exception as e:
        R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}')
        code = 5
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': sha_bytes(b.encode()), 'exit': code}))
    return code


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

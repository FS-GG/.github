#!/usr/bin/python3 -I
"""LEARN #4745 C2 operator r6 = operator r5 (318edb4e...) plus the changes of Root decision 4647#6533215654 item 3 and #4988 section 10
(Refs FS-GG/.github#4647); everything not listed here is r5 byte for byte:
r6: (1) ACK_DIR is the PERSISTENT bind /usr/local/libexec/fsgg-learn/retirement-acks (not /etc/fsgg/learn, which fdev's root disk loses
on every image upgrade), and an acknowledgement is read through a pinned descriptor chain walked from / (O_DIRECTORY|O_NOFOLLOW openat,
every directory and the file fstat-checked on the descriptor: owner, type, not group/other-writable, regular, <= 4096 B), written by
learn-retirement-ack-r2.py; (2) pod/reader EXCLUSION is enforced: the sweep also lists learn-pod-* units, a failed or transitioning pod unit
refuses like any other, and an ACTIVE pod unit refuses this op ('exclusion refused'); the pod operator refuses symmetrically (its own PR);
(3) the real-op RESULT carries runNonceSha256 = sha256(nonce) BEFORE the units start, so the finalizer record (runNonceSha256) binds to it
even if the start fails; mode, opId, operatorSha256 and an integer exitCode were already in the RESULT; (4) an existing custody or evidence
directory for the op id (or <id>-pre) still refuses ('op id already used'): unchanged behaviour, now a function with its own test.
The reader profile mounts no input, so the input-read marker and a gates.inputMounted ordering are NOT part of r6: they belong to the pod
operator. No change to IMAGE_ID, FIN, FIN_SHA, limits, seccomp, units or custody.
Earlier header (r5): LEARN #4745 C2 operator r5 (governing spec rev 11 r2 ec0a031d §7/§11.7; decision 4516#6352952638). Earlier header: r3
(spec rev 10 bfae7887 + M1/R13/R14; proposal v1 44a3359b; probe option (a) 4516#6347898882; review 4516#6348028489).
r5: (1) FIN -> learn-finalizer-r4.py (C3 r4); (2) retirement-ack gate (§7): after the sweep and the op-id reuse check, C2 reads
every <EVID_ROOT>/<id>/record.json (O_NOFOLLOW, regular, <= 16 MiB); a record whose schema is learn-op-retirement-failure/1, or
that cannot be read/parsed, BLOCKS until an acknowledgement exists: ACK_DIR/<id>.retirement-ack, a regular root-owned file not
group/other-writable on a root-owned, non-writable path (root_chain_ok), whose content is exactly the sha256 of that record's
bytes. Otherwise refuse (exit 5, errors: retirement-failure-unacknowledged: <ids>). ACK_DIR absent = no acknowledgements.
(3) aggregate limits (#4777 decision 4516#6358883043): refuse while 2 ops are already active (concurrency refused); refuse while
the evidence root holds more than EVID_MAX_BYTES (32 GiB, Home's proposal) of allocated file data (walk bounded by
EVID_MAX_ENTRIES; an unmeasurable tree refuses too). The user-30034.slice drop-in (CPUQuota=200%, MemoryMax=6G, TasksMax=1280)
is installed by C1 r8b, not by C2.
r5b (review 4516#6359587710; erratum E5 4516#6359541244): (4) headroom is measured against user-30034.slice: slice
memory.max - memory.current >= 2.5 GiB and pids.max - pids.current >= 576 (one op + its finalizer); the host MemAvailable >= 4 GiB
guard stays; (5) the slice limits must be live (memory.max == 6442450944, pids.max == 1280), else refuse 'slice limits absent'
(a missing drop-in fails closed); (6) the sweep-to-start window is serialised: an exclusive flock on ~/.local/state/
learn-operator.lock is taken before the sweep and held until C2 exits (after the real op units are started), so two operators
cannot both pass the 2-op cap; the lock is waited for up to LOCK_WAIT_S, then refuse 'operator lock busy'.
r4: FIN re-pinned to C3 r3 (learn-finalizer-r3.py).
r3: refuses if custody/evidence of <id> OR <id>-pre exist and binds the precheck record to this run (runNonceSha256);
defaultErrnoRet must be equal (no fallback); uid/gid parsed by line order plus the HOST uid of the payload (/proc/<pid>/status)
must be 428679 (never 0/30034); the op unit starts only if the finalizer started; ExecStop commands are time-bounded (timeout 5 +
timeout 9 with stop -t 3) and TimeoutStopSec=10, so the worst-case hard kill is 32:00 + 24 s < 32:30; log Size 16MB is gated;
/usr/bin/python3 is gated to 375532f8; FIN_SHA re-pinned to C3 r2.
r2: the precheck runs the reader-image r4 socket probe with the expected AF_VSOCK errno computed from the source profile
(the ERRNO rule for socket with arg0 == 40, for an empty cap set); the probe must print exactly `PROBE ok`. The op image is
the admitted reader image r4 (Id pinned below once admitted). Runs ONLY as uid 30034 (fsgg-learn-qual):
    learn-operator.py --run --id <op-id> --argv-json '<JSON list: reader command>'
It (1) gates, (2) sweeps, (3) checks headroom, (4) runs the exact-option PRECHECK op `<id>-pre` (same unit/podman options, probe
command) and asserts its options, then (5) starts the real op `<id>`: finalizer unit FIRST, then the op unit. It returns once both
units are active and prints the plan; it never touches custody files (C3 owns custody) and never stops a sibling op.
Also: --precheck-only --id <id>   (steps 1-4 only).

(1) Gates: euid 30034; op image is exactly Id 37fcf870… (pinned by Id, admitted 4516#6347405511; the tag is never used);
    seccomp /etc/fsgg/learn/seccomp-8bd52f8d.json root-owned with every component root-owned and not g/o-writable, and its
    sha256 = 8bd52f8d…; the installed finalizer /usr/local/libexec/fsgg-learn/learn-finalizer.py has the pinned sha256 (FIN_SHA).
(2) Sweep (§1): refuse if any learn-op-*/learn-fin-* unit is failed, activating, deactivating or reloading, or if any learn-op-*
    container exists whose op unit is not active (orphan). Siblings are never stopped, killed or removed.
(3) Headroom (§2): user-30034.slice pids.max-current >= 1000; a finite memory.max-current >= 2.5 GiB; MemAvailable >= 4 GiB;
    custody filesystem free >= 1 GiB x (active ops + 1).
(4) Precheck (§3/§4): the probe op prints `id`, /proc/self/status Cap*/NoNewPrivs and the owners of /out /work /tmp /dev/shm,
    then runs the socket probe (PROBE, see the delivery note: pending fdev's choice of probe tool); C2 asserts from podman inspect,
    the OCI config.json and the probe output: CapAdd empty, all five capability sets 0, NoNewPrivs 1, uid/gid 1000, tmpfs owners =
    1000, read-only rootfs, network none, memory/pids/cpu limits, log options, the seccomp path, and the §4 rule-set equality:
    the effective OCI linux.seccomp normalized per (action, errnoRet, name, args) EQUALS the set computed from the source for an
    empty cap set, x86_64 and the running kernel; every excluded rule and every socket/chroot rule is recorded.
(5) Units (§1/§3/§7): learn-fin-<id> (RuntimeMaxSec=1980, MemoryMax=512M, TasksMax=64, Environment=FSGG_NONCE) first; then
    learn-op-<id> (BindsTo/After fin, KillMode=control-group, RuntimeMaxSec=1920, Delegate=yes, CPUQuota=100%,
    MemoryMax=2G, MemorySwapMax=0, TasksMax=512, TimeoutStopSec=10; ExecStop = timeout 5 podman unpause, then timeout 9 podman
    stop -t 3 (both '-'-prefixed; they run before systemd's cgroup kill so podman records the exit even if the finalizer died
    mid-custody; worst-case hard kill 32:00 + 24 s); ExecStopPost = C4 dump with the nonce INLINE, then podman rm -f).
C4 dump (M1): a /bin/sh ExecStopPost that reads the unit cgroup's memory.events/memory.peak/pids.peak with shell builtins and
prints `FSGG <nonce> MEMEV k=v …`, `FSGG <nonce> PEAK …`, `FSGG <nonce> STOPPOST …` with the shell's own echo; it never execs.
Result (plan + gates + precheck) to stdout (RESULT-BEGIN/RESULT-END). Exit 0 started; 5 refused (nothing started, or the precheck
op ran but failed its assertions - then the real op is NOT started).
"""
import fcntl, hashlib, json, os, re, secrets, stat, subprocess, sys, time
from datetime import datetime, timezone

if os.geteuid() != 30034: sys.exit('refuse: run as fsgg-learn-qual (uid 30034)')
os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus',
                  HOME='/mnt/storage/containers/learn-qualification/account', PATH='/usr/bin', LC_ALL='C')
os.umask(0o077); os.chdir('/')
SELF_SHA = hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest()
IMAGE_ID = '6625dfb774feabc256d94d15d112cf3f554d3cd7d388e1d717d74ada97c1de06'   # reader image r4, admitted 4516#6348395818
SECCOMP = '/etc/fsgg/learn/seccomp-8bd52f8d.json'; SECCOMP_SHA = '8bd52f8d249b79693751ceba153038d6b4fb7b507e6ab16133a0cb207ca1e835'
FIN = '/usr/local/libexec/fsgg-learn/learn-finalizer-r4.py'; FIN_SHA = '7f875245504434580b2c1ff97bce25393d5506e383bef3aaf964504b37c651af'
ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'   # r6: persistent bind (r5: /etc/fsgg/learn/retirement-acks, lost on image upgrade)
MAX_ACTIVE_OPS = 2                                      # r5 (3): #4777 aggregate limit
EVID_MAX_BYTES, EVID_MAX_ENTRIES = 32 << 30, 1 << 20    # r5 (3): evidence-size refusal (accepted 4516#6359541244), walk bound
SLICE_MEMORY_MAX, SLICE_PIDS_MAX = 6 << 30, 1280         # r5b (5): the C1 r8b drop-in must be live (E5)
LOCK, LOCK_WAIT_S = f"{os.environ['HOME']}/.local/state/learn-operator.lock", 120   # r5b (6)
PY_SHA = '375532f83be620283ab780c14ca6bcc5907dc379d2b621de0c5d0a112cf5458b'   # /usr/bin/python3 (P0)
HOST_UID_1000 = 427680 + 999                                                   # container uid 1000 -> host (P0)
HOME = os.environ['HOME']; CUSTODY_ROOT = f'{HOME}/.local/state/learn-custody'
SLICE = '/sys/fs/cgroup/user.slice/user-30034.slice'
PROBE = '/usr/local/libexec/learn/probe'            # static C socket probe in reader image r4 (learn-probe.c)
R = {'schema': 'home-learn-operator/1', 'operatorRevision': 'r6', 'operatorSha256': SELF_SHA, 'utc': datetime.now(timezone.utc).isoformat(), 'gates': {}, 'errors': []}


def finish(code=None):
    if code is None: code = 5 if R['errors'] else 0
    R['exitCode'] = code; b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN'); print(b); print('RESULT-END'); print(json.dumps({'sha256': hashlib.sha256(b.encode()).hexdigest(), 'exit': code}))
    sys.exit(code)


def refuse(why): R['errors'].append(why); finish(5)


def run(argv, timeout=60, cap=1 << 22):
    try:
        p = subprocess.run(argv, capture_output=True, timeout=timeout, cwd='/')
        return p.returncode, p.stdout[:cap].decode('utf-8', 'replace'), p.stderr[-2000:].decode('utf-8', 'replace')
    except subprocess.TimeoutExpired: return None, '', 'timeout'


def sha_file(p): return hashlib.sha256(open(p, 'rb').read()).hexdigest()


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


def root_chain_ok(path):
    for i in range(1, len(path.split('/')) + 1):
        p = '/'.join(path.split('/')[:i]) or '/'; s = os.lstat(p)
        if s.st_uid != 0 or stat.S_ISLNK(s.st_mode) or (stat.S_IMODE(s.st_mode) & 0o022): return False, p
    return True, None


# ---------------- seccomp expected set (§4) ----------------
def kver(s):
    return tuple(int(x) for x in re.findall(r'\d+', s)[:2]) if s else (0, 0)


def norm_rule(action, errno_ret, name, args):
    na = sorted(json.dumps({'index': a.get('index'), 'op': a.get('op'), 'value': a.get('value'), 'valueTwo': a.get('valueTwo') or 0}, sort_keys=True) for a in (args or []))
    return (action, errno_ret, name, tuple(na))


def expected_set(src, kernel):
    exp, excluded = set(), []
    for rule in src.get('syscalls', []):
        inc, exc = rule.get('includes') or {}, rule.get('excludes') or {}
        ok = True
        if inc.get('caps'): ok = False                                      # empty cap set: an includes.caps rule never applies
        if inc.get('arches') and 'amd64' not in inc['arches'] and 'x86_64' not in inc['arches'] and 'x32' not in inc['arches']: ok = False
        if inc.get('minKernel') and kver(kernel) < kver(inc['minKernel']): ok = False
        if exc.get('caps'): pass                                            # empty cap set: an excludes.caps rule is never excluded
        if exc.get('arches') and ('amd64' in exc['arches'] or 'x86_64' in exc['arches']): ok = False
        if exc.get('minKernel') and kver(kernel) >= kver(exc['minKernel']): ok = False
        er = rule.get('errnoRet', src.get('defaultErrnoRet'))
        names = rule.get('names') or ([rule['name']] if rule.get('name') else [])
        for n in names:
            t = norm_rule(rule.get('action'), er if rule.get('action') in ('SCMP_ACT_ERRNO', 'SCMP_ACT_TRACE') else None, n, rule.get('args'))
            (exp.add(t) if ok else excluded.append({'name': n, 'action': rule.get('action'), 'includes': inc, 'excludes': exc}))
    return exp, excluded


def effective_set(oci, default_errno):
    eff = set()
    for rule in oci.get('syscalls', []):
        er = rule.get('errnoRet', default_errno)
        for n in rule.get('names') or []:
            eff.add(norm_rule(rule.get('action'), er if rule.get('action') in ('SCMP_ACT_ERRNO', 'SCMP_ACT_TRACE') else None, n, rule.get('args')))
    return eff


# ---------------- units ----------------
TMPFS = ['--tmpfs', '/out:rw,noexec,nosuid,nodev,size=256m,mode=0700,U', '--tmpfs', '/work:rw,exec,nosuid,nodev,size=512m,mode=0700,U',
         '--tmpfs', '/tmp:rw,noexec,nosuid,nodev,size=64m,U', '--tmpfs', '/dev/shm:rw,noexec,nosuid,nodev,size=64m,U']


def podman_argv(name, argv):
    return (['/usr/bin/podman', 'run', '--cgroups=split', '--name', name, '--network=none', '--pull=never', '--user', '1000:1000',
             '--cap-drop=all', '--security-opt=no-new-privileges', '--security-opt', f'seccomp={SECCOMP}', '--read-only',
             '--read-only-tmpfs=false', '--cpus=1', '--memory=1920m', '--memory-swap=1920m', '--pids-limit=512',
             '--log-driver=k8s-file', '--log-opt', 'max-size=16m', '-a', 'stderr'] + TMPFS + [IMAGE_ID] + list(argv))


def dump_cmd(nonce):
    # C4 (M1): values read with the shell's read builtin, markers printed with the shell's own echo (the $(sed ...) for the
    # cgroup path forks, harmlessly); no exec; no podman output; nonce inline
    p = f'FSGG {nonce}'
    return ('-/bin/sh -c \'C=/sys/fs/cgroup$$(sed -n "s/^0:://p" /proc/self/cgroup); C=$${C%/.control}; '
            'E=""; while read k v; do E="$$E $$k=$$v"; done < "$$C/memory.events"; '
            f'echo "{p} MEMEV$$E"; read MP < "$$C/memory.peak"; read PP < "$$C/pids.peak"; '
            f'echo "{p} PEAK mem=$$MP pids=$$PP"; echo "{p} STOPPOST SR=$$SERVICE_RESULT EC=$$EXIT_CODE ES=$$EXIT_STATUS"\'')


def start_pair(opid, argv, nonce):
    fin = ['systemd-run', '--user', '--unit', f'learn-fin-{opid}', '-p', 'RuntimeMaxSec=1980', '-p', 'MemoryMax=512M', '-p', 'TasksMax=64',
           '-p', f'Environment=FSGG_NONCE={nonce}', '-p', f'ExecStopPost=/usr/bin/podman rm -f --time 0 learn-op-{opid}',
           '/usr/bin/python3', '-I', FIN, '--id', opid]
    op = ['systemd-run', '--user', '--unit', f'learn-op-{opid}', '-p', f'BindsTo=learn-fin-{opid}.service', '-p', f'After=learn-fin-{opid}.service',
          '-p', 'KillMode=control-group', '-p', 'RuntimeMaxSec=1920', '-p', 'TimeoutStopSec=10', '-p', 'Delegate=yes', '-p', 'CPUQuota=100%',
          '-p', 'MemoryMax=2G', '-p', 'MemorySwapMax=0', '-p', 'TasksMax=512',
          # ExecStop runs BEFORE systemd's cgroup kill: unpause + graceful stop let podman/conmon record the exit even when the
          # finalizer died mid-custody (otherwise conmon is SIGKILLed with the frozen payload and podman's state sticks at 'paused')
          # bounded: 5 s + 9 s, then TimeoutStopSec=10 for the main process -> worst-case hard kill 32:00 + 24 s < 32:30 (§1)
          '-p', f'ExecStop=-/usr/bin/timeout 5 /usr/bin/podman unpause learn-op-{opid}',
          '-p', f'ExecStop=-/usr/bin/timeout 9 /usr/bin/podman stop -t 3 learn-op-{opid}',
          '-p', f'ExecStopPost={dump_cmd(nonce)}',
          '-p', f'ExecStopPost=/usr/bin/podman rm -f --time 0 learn-op-{opid}', '--'] + [a.replace('$', '$$') for a in podman_argv(f'learn-op-{opid}', argv)]
    # systemd expands $VAR in ExecStart arguments: every '$' is passed as '$$' so the reader argv arrives verbatim
    a = run(fin, 30)
    if a[0] != 0: return {'finalizerStart': a[0], 'opStart': None, 'errs': [a[2][-300:], 'op unit not started: the finalizer did not start']}
    b = run(op, 30)
    return {'finalizerStart': a[0], 'opStart': b[0], 'errs': [a[2][-300:], b[2][-300:]]}


def wait_record(opid, seconds):
    path = f'{HOME}/.local/state/learn-evidence/{opid}/record.json'; t = time.monotonic()
    while time.monotonic() - t < seconds:
        if os.path.exists(path): return json.loads(open(path).read())
        time.sleep(2)
    return None


def parse_unit_rows(out):
    """r6: rows of `systemctl --user list-units` with at least four columns"""
    rows = [l.replace('●', ' ').split() for l in out.splitlines()]
    return [r_ for r_ in rows if len(r_) >= 4]


def classify_units(rows):
    """r6: (busy units, active reader ops, active POD units). A failed or transitioning unit of any kind, pod included, is busy; an active
    learn-pod-* unit is an active pod op, which excludes this reader op."""
    busy = [r_[0] for r_ in rows if r_[2] in ('failed', 'activating', 'deactivating', 'reloading')]
    active_ops = {r_[0][:-len('.service')] for r_ in rows if r_[0].startswith('learn-op-') and r_[2] == 'active'}
    active_pods = sorted(r_[0][:-len('.service')] for r_ in rows if r_[0].startswith('learn-pod-') and r_[2] == 'active')
    return busy, active_ops, active_pods


def recheck_sweep(out, opid, max_ops):
    """r6 hardening: the unit census taken AGAIN immediately before the real op starts. The operator lock serialises our own operators only; between
    the first census and the start a pod op or another reader op could have started. Returns a refusal reason, or None. This op's own precheck
    units are not counted against it."""
    busy, active_ops, active_pods = classify_units(parse_unit_rows(out))
    own = (f'learn-op-{opid}-pre.service', f'learn-fin-{opid}-pre.service')
    stray = [u for u in busy if u not in own]
    real = sorted(u for u in active_ops if not u.endswith('-pre'))
    if active_pods: return 'exclusion refused at the start: a pod op became active: ' + ','.join(active_pods)
    if stray: return 'sweep refused at the start: failed or transitioning units: ' + ','.join(stray)
    if len(real) >= max_ops: return f'concurrency refused at the start: {len(real)} ops already active (max {max_ops})'
    return None


def start_census(opid, max_ops, run=None):
    """r6 hardening: take the census and apply recheck_sweep to it. A census that did not succeed (a non-zero or missing return code, which is what
    a failed systemctl or a timeout gives, with an empty list) refuses instead of reading as 'no units'. Returns (rc, refusal reason or None)."""
    rc, out, _ = (run or globals()['run'])(['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-op-*', 'learn-fin-*', 'learn-pod-*'], 30)
    return rc, (recheck_sweep(out, opid, max_ops) if rc == 0 else 'unit census failed at the start')


def used_ids(opid, custody_root, evid_root):
    """r6: existing custody or evidence directories for the op id or its precheck id; any of them refuses the run"""
    return [p for p in (f'{custody_root}/{opid}', f'{custody_root}/{opid}-pre', f'{evid_root}/{opid}', f'{evid_root}/{opid}-pre') if os.path.lexists(p)]


def start_real(opid, argv, nonce):
    """r6: start the real op. The RESULT carries sha256(nonce) BEFORE the units start, so the finalizer record's runNonceSha256 binds to this
    RESULT even when the start fails."""
    R['runNonceSha256'] = hashlib.sha256(nonce.encode()).hexdigest()
    R['start'] = start_pair(opid, argv, nonce)
    time.sleep(2)
    R['units'] = {u: run(['systemctl', '--user', 'is-active', u], 10)[1].strip() for u in (f'learn-fin-{opid}.service', f'learn-op-{opid}.service')}
    if R['start']['finalizerStart'] != 0 or R['start']['opStart'] != 0: R['errors'].append('unit start failed (the finalizer handles any partial start)')


# ---------------- main ----------------
args = sys.argv[1:]
try:
    mode = args[0] if args else ''
    opid = args[args.index('--id') + 1] if '--id' in args else ''
    if mode not in ('--run', '--precheck-only') or not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,58}', opid): refuse('usage: --run|--precheck-only --id <op-id> [--argv-json JSON]')
    argv = json.loads(args[args.index('--argv-json') + 1]) if mode == '--run' else []
    if mode == '--run' and (not isinstance(argv, list) or not argv or not all(isinstance(x, str) for x in argv)): refuse('argv-json must be a non-empty JSON list of strings')
    R.update(mode=mode, opId=opid, argv=argv)
    # (1) gates
    rc, out, _ = run(['podman', 'image', 'inspect', '--format', '{{.Id}}', IMAGE_ID], 30)
    ok, bad = root_chain_ok(SECCOMP)
    g = {'image': rc == 0 and out.strip() == IMAGE_ID, 'seccompChain': ok, 'seccompSha': sha_file(SECCOMP) == SECCOMP_SHA,
         'finalizerSha': os.path.isfile(FIN) and sha_file(FIN) == FIN_SHA and root_chain_ok(FIN)[0],
         'python3': sha_file('/usr/bin/python3') == PY_SHA}
    R['gates']['P1'] = g
    if not all(g.values()): refuse('gate failed: ' + ','.join(k for k, v in g.items() if not v) + (f' (seccomp component {bad})' if bad else ''))
    def rd_(p):
        try: return open(p).read().strip()
        except OSError: return None
    lim = {'memoryMax': rd_(SLICE + '/memory.max'), 'pidsMax': rd_(SLICE + '/pids.max')}
    R['gates']['sliceLimits'] = {**lim, 'expected': {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}}
    if lim != {'memoryMax': str(SLICE_MEMORY_MAX), 'pidsMax': str(SLICE_PIDS_MAX)}: refuse(f'slice limits absent: {lim}')   # r5b (5)
    lfd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600); t_lock = time.monotonic()   # r5b (6), held to exit
    while True:
        try: fcntl.flock(lfd, fcntl.LOCK_EX | fcntl.LOCK_NB); break
        except BlockingIOError:
            if time.monotonic() - t_lock > LOCK_WAIT_S: refuse('operator lock busy')
            time.sleep(0.5)
    R['gates']['operatorLockWaitSeconds'] = round(time.monotonic() - t_lock, 1)
    # (2) sweep
    rc, out, _ = run(['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-op-*', 'learn-fin-*', 'learn-pod-*'], 30)
    if rc != 0: refuse('unit census failed')              # r6: a failed systemctl prints nothing, which would read as 'no units' (fail closed)
    rows = parse_unit_rows(out)
    busy, active_ops, active_pods = classify_units(rows)
    rc, out, _ = run(['podman', 'ps', '-a', '--filter', 'name=^learn-op-', '--format', '{{.Names}}'], 30)
    if rc != 0: refuse('container census failed')         # r6: likewise, a failed podman prints nothing, which would read as 'no orphans'
    orphans = [n for n in out.split() if n not in active_ops]
    R['gates']['sweep'] = {'units': [' '.join(r_[:4]) for r_ in rows], 'busy': busy, 'orphans': orphans, 'activeOps': sorted(active_ops)}
    if busy or orphans: refuse('sweep refused (failed/transitioning units or orphan containers; siblings untouched)')
    R['gates']['exclusion'] = {'activePods': active_pods}                                  # r6: pod/reader exclusion is enforced, not de facto
    if active_pods: refuse('exclusion refused: a pod op is active: ' + ','.join(active_pods))
    real_active = sorted(u for u in active_ops if not u.endswith('-pre'))
    R['gates']['concurrency'] = {'activeOps': real_active, 'max': MAX_ACTIVE_OPS}
    if len(real_active) >= MAX_ACTIVE_OPS: refuse(f'concurrency refused: {len(real_active)} ops already active (max {MAX_ACTIVE_OPS})')
    EVID_ROOT = f'{HOME}/.local/state/learn-evidence'
    used = used_ids(opid, CUSTODY_ROOT, EVID_ROOT)
    if any(u.startswith(f'learn-op-{opid}') or u.startswith(f'learn-fin-{opid}') for u in [r_[0] for r_ in rows]) or used:
        refuse('op id already used: ' + ','.join(used))
    R['gates']['retirementAcks'], unacked = retirement_gate(EVID_ROOT)      # r5 (§7): retirement-failure blocks further ops
    if unacked: refuse('retirement-failure-unacknowledged: ' + ','.join(unacked))
    ev = evidence_usage(EVID_ROOT); ev['maxBytes'] = EVID_MAX_BYTES; R['gates']['evidenceUsage'] = ev   # r5 (3)
    if ev.get('error') or ev['bytes'] > EVID_MAX_BYTES or ev['entries'] > EVID_MAX_ENTRIES:
        refuse(f"evidence size refused: {ev['bytes']} B in {ev['entries']} entries (max {EVID_MAX_BYTES} B)")
    # (3) headroom
    def rd(p):
        try: return open(p).read().strip()
        except OSError: return None
    pm, pc, mm, mc = rd(SLICE + '/pids.max'), rd(SLICE + '/pids.current'), rd(SLICE + '/memory.max'), rd(SLICE + '/memory.current')
    mav = next((int(l.split()[1]) * 1024 for l in open('/proc/meminfo') if l.startswith('MemAvailable:')), 0)
    st = os.statvfs(CUSTODY_ROOT); free = st.f_bavail * st.f_frsize
    hg = {'pids': pm == 'max' or (pm and pc and int(pm) - int(pc) >= 576),          # r5b (4): slice-based (E5)
          'memory': mm == 'max' or (mm and mc and int(mm) - int(mc) >= int(2.5 * (1 << 30))),
          'memAvailable': mav >= 4 << 30, 'custodyDisk': free >= (1 << 30) * (len(active_ops) + 1)}
    R['gates']['headroom'] = {**hg, 'values': {'pidsMax': pm, 'pidsCurrent': pc, 'memMax': mm, 'memCurrent': mc, 'memAvailable': mav, 'custodyFree': free}}
    if not all(hg.values()): refuse('headroom refused: ' + ','.join(k for k, v in hg.items() if not v))
    # (4) precheck op
    pre = f'{opid}-pre'; nonce_p = secrets.token_hex(16)
    src0 = json.loads(open(SECCOMP).read()); exp0, _ = expected_set(src0, os.uname().release)
    vs = sorted({t[1] for t in exp0 if t[2] == 'socket' and t[0] == 'SCMP_ACT_ERRNO'
                 and any(json.loads(a) == {'index': 0, 'op': 'SCMP_CMP_EQ', 'value': 40, 'valueTwo': 0} for a in t[3])})
    if len(vs) != 1 or not isinstance(vs[0], int): refuse(f'cannot determine the expected AF_VSOCK errnoRet from the profile: {vs}')
    R['gates']['vsockErrnoExpected'] = vs[0]
    # the image ENTRYPOINT (catatonit -> C5 entrypoint) wraps this argv; output to stderr (only stderr is attached)
    probe = ['/bin/sh', '-c', 'exec 1>&2; id -u; id -g; grep -E "^(Cap(Inh|Prm|Eff|Bnd|Amb)|NoNewPrivs):" /proc/self/status; '
             'stat -c "%u %g %n" /out /work /tmp /dev/shm; if [ -x ' + PROBE + ' ]; then ' + PROBE + ' ' + str(vs[0]) + '; else echo "PROBE absent"; fi; sleep 8']
    # the probe holds 8 s so C2 can inspect the RUNNING precheck container (podman inspect + OCI config.json)
    R['precheck'] = {'start': start_pair(pre, probe, nonce_p)}
    j, oci, t_, host_uids = None, {}, time.monotonic(), []
    while time.monotonic() - t_ < 30:                          # inspect as soon as the precheck container is running
        rc, ins, _ = run(['podman', 'inspect', f'learn-op-{pre}'], 15)
        try: cand = json.loads(ins)[0]
        except (ValueError, IndexError): cand = None
        if cand and (cand.get('State') or {}).get('Status') == 'running':
            p_ = cand.get('OCIConfigPath')
            if p_ and os.path.isfile(p_):
                j, oci = cand, json.loads(open(p_).read())
                try: host_uids = next(l.split()[1:] for l in open(f"/proc/{cand['State']['Pid']}/status") if l.startswith('Uid:'))
                except (OSError, StopIteration, KeyError): host_uids = []
                break
        time.sleep(0.5)
    if j is None: refuse('precheck container not inspectable while running')
    hc = j.get('HostConfig') or {}
    lin = oci.get('linux') or {}; sc = lin.get('seccomp') or {}
    src = json.loads(open(SECCOMP).read())
    exp, excluded = expected_set(src, os.uname().release)
    eff = effective_set(sc, sc.get('defaultErrnoRet'))          # no fallback: a missing OCI value must not be hidden
    inv_id = run(['systemctl', '--user', 'show', '-p', 'InvocationID', '--value', f'learn-op-{pre}.service'], 10)[1].strip()
    rec = wait_record(pre, 120) or {}
    bound = rec.get('runNonceSha256') == hashlib.sha256(nonce_p.encode()).hexdigest()
    # this invocation only (journal entries of an earlier same-named unit must not count)
    logs = run(['journalctl', '--user', '-o', 'cat', '--no-pager', f'_SYSTEMD_INVOCATION_ID={inv_id}'], 30)[1] if re.fullmatch(r'[0-9a-f]{32}', inv_id) else ''
    caps = dict(re.findall(r'^(Cap\w+|NoNewPrivs):\s*(\S+)', logs, re.M))
    owners = re.findall(r'^(\d+) (\d+) (/out|/work|/tmp|/dev/shm)$', logs, re.M)
    pc_ = {'capAddEmpty': not hc.get('CapAdd'), 'capsZero': all(caps.get(k) == '0000000000000000' for k in ('CapInh', 'CapPrm', 'CapEff', 'CapBnd', 'CapAmb')),
           'noNewPrivs': caps.get('NoNewPrivs') == '1', 'uidGid1000': re.findall(r'^(\d+)$', logs, re.M)[:2] == ['1000', '1000'],
           'hostUid': len(host_uids) == 4 and all(int(u) == HOST_UID_1000 and int(u) not in (0, 30034) for u in host_uids),
           'tmpfsOwners': len(owners) == 4 and all(u == '1000' for u, _, _ in owners),
           'readOnlyRoot': bool(hc.get('ReadonlyRootfs')), 'networkNone': hc.get('NetworkMode') == 'none',
           'memory': hc.get('Memory') == 1920 * (1 << 20), 'pids': hc.get('PidsLimit') == 512, 'cpus': hc.get('NanoCpus') == 10 ** 9,
           'logOpts': (hc.get('LogConfig') or {}).get('Type') == 'k8s-file' and (hc.get('LogConfig') or {}).get('Size') == '16MB', 'seccompPath': f'seccomp={SECCOMP}' in (hc.get('SecurityOpt') or []),
           'seccompDefault': sc.get('defaultAction') == src.get('defaultAction'),
           'seccompDefaultErrno': 'defaultErrnoRet' in sc and sc.get('defaultErrnoRet') == src.get('defaultErrnoRet'), 'seccompEqual': exp == eff,
           # whole-line matches only: systemd's "Started ..." journal line quotes the probe command, including its fallback text
           'probe': re.search(r'^PROBE ok$', logs, re.M) is not None and re.search(r'^PROBE (absent|fail)', logs, re.M) is None,
           'precheckRecord': rec.get('schema') == 'learn-op-success/1' and bound}
    R['precheck'].update(assertions=pc_, record=rec.get('schema'), recordBound=bound, hostUids=host_uids, invocationId=inv_id,
                         seccomp={'expected': len(exp), 'effective': len(eff), 'missing': sorted(map(str, exp - eff))[:50], 'extra': sorted(map(str, eff - exp))[:50],
                                  'excluded': excluded, 'socketChroot': sorted(map(str, {t for t in exp | eff if t[2] in ('socket', 'chroot')}))})
    if not all(pc_.values()): refuse('precheck failed: ' + ','.join(k for k, v in pc_.items() if not v) + ' - the real op is NOT started')
    if mode == '--precheck-only': finish(0)
    # (5) the real op
    rc, why = start_census(opid, MAX_ACTIVE_OPS)
    R['gates']['startCensus'] = {'rc': rc, 'refused': why}
    if why: refuse(why)
    nonce = secrets.token_hex(16)
    start_real(opid, argv, nonce)
    finish()
except SystemExit:
    raise
except Exception as e:
    R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}'); finish(5)

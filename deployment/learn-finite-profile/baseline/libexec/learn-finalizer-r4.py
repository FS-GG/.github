#!/usr/bin/python3 -I
"""LEARN #4745 C3 finalizer r4 (governing spec rev 11 r2 ec0a031d; scope 4516#6352661446, 4516#6352952638, 6358821566).
r4: (1) the early paths (op_failed_to_start, op_ended_without_done) now check and record retirement like every other op
(§1; rev 11 §11.1): wait up to 60 s for the op unit to be inactive/failed, then containerGone + op cgroup absent or populated 0
(+ reset-failed); not retired -> learn-op-retirement-failure/1 with earlyPath; the exception path records a best-effort
retirement block; (2) an archive TIMEOUT is no longer labelled budget: over=None -> rejected:truncated, reason 'archive timed
out' (only overflow, over=True, or a stream over the cap is budget; §11.4); (3) page cache (§11.5): every large write or read
drops its cache every DROP_EVERY (32 MiB) - writes fdatasync + POSIX_FADV_DONTNEED (the stream while archiving, each extracted
file, each evidence file), reads POSIX_FADV_DONTNEED (the stream during inventory and extraction, the extracted files during the
evidence copy) - and once more at the end, so a 256 MiB custody stays well below MemoryMax=512M instead of reclaiming at
memory.max (a whole-file drop alone was measured insufficient: 29 max events); (4) the unused DFL constant is removed.
r3: (1) every fd chain (stream, tar.err, extraction, evidence writes) opens intermediate directories with O_PATH and fstat-checks
the final fd as a regular file; the chain uses O_PATH (O_RDONLY needs READ permission and failed with EACCES
on the root-0711 /mnt/storage/containers/learn-qualification ancestor - A0 run 1); (2) rejected:count -> status unavailable,
enospc unknown (like budget/truncated/identity); (3) the archive waits with select() bounded by min(300, 1890 - elapsed since
the anchor), so a silent writer cannot outlast the custody deadline; (4) the op cgroup is taken from `systemctl --user show -p
ControlGroup` at anchor time (not assumed), and checked absent / populated 0 at retirement.
Runs ONLY as uid 30034, as the transient user unit learn-fin-<id>.service started by C2 BEFORE the op unit:
    ExecStart=/usr/bin/python3 -I /usr/local/libexec/fsgg-learn/learn-finalizer.py --id <id>
    Environment=FSGG_NONCE=<128-bit hex>      (finalizer only; the op unit gets the nonce inline in its ExecStopPost)
    MemoryMax=512M TasksMax=64 RuntimeMaxSec=1980
The finalizer is the ONLY custody actor (S3). r2 changes:
  1 archive: tar stderr goes to a custody FILE (no pipe deadlock); tar runs in its own process group; on overflow the stdout pipe
    is closed and the whole group is killed (SIGKILL), so the budget verdict is reachable; every wait is bounded.
  2 R13: the evidence tree is created under umask 027 and NO chmod is ever applied to a directory (a chmod by a non-member of
    the directory's group clears S_ISGID); new dirs inherit setgid + group fsgg-learn-evidence from the 2710 parent; files are
    created 0640 by open() under that umask. record.json is written atomically (tmp + rename).
  3 memory: the inventory keeps per-member records only up to 4097 members and clears tarfile's member cache as it goes; no
    allocation proportional to a member's size (members are hashed in 1 MiB chunks; .status is read only when exactly 4096 B).
  4 status absent: without a .done member, .status is `absent` and the outcome can never be success (§1).
  5 the stream is opened through an O_DIRECTORY|O_NOFOLLOW openat chain, fstat-checked regular and size-capped (§5.4).
  non-blocking: CUSTODY_DEADLINE_S enforced (custody not complete 1890 s after the anchor -> retirement-failure, partial
  flagged); chain anchors keep flowing while waiting (one chain entry per minute); the op unit and container cgroups must be
  absent or unpopulated at retirement; the record carries runNonceSha256 = sha256(nonce) so C2 can bind a record to its run.
Paths (per op id, owned by uid 30034): custody ~/.local/state/learn-custody/<id>/ (0700: stream.tar, tar.err, extract/,
chain.jsonl); evidence ~/.local/state/learn-evidence/<id>/ (2750 inherited, group fsgg-learn-evidence; record.json 0640; out/).
Verdict precedence (§5.6): budget > truncated > identity > count > size > type/path > tar > accepted. Disclosure (§5.7): no names,
sizes or content unless ident_ok=1; .status only from a parsed ident_ok=1 inventory (R14). Counting note: .status and .done are
2 of the 4096 member slots and 4 KiB of /out. Markers (§7, M1): this finalizer prints `FSGG <nonce> FIN ...`; the op ExecStopPost
(C4) prints MEMEV/PEAK/STOPPOST with the shell's echo. Record schemas: learn-op-success/1, learn-op-killed-retired/1,
learn-op-unrecoverable/1, learn-op-failed-to-start/1, learn-op-retirement-failure/1. Exit 0 after writing a record; 70 otherwise.
"""
import hashlib, json, os, re, select, signal, stat, subprocess, sys, tarfile, time
from datetime import datetime, timezone

if os.geteuid() != 30034: sys.exit('refuse: run as uid 30034')
os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus',
                  HOME='/mnt/storage/containers/learn-qualification/account', PATH='/usr/bin', LC_ALL='C')
os.umask(0o077); os.chdir('/')
SELF_SHA = hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest()
ARGS = sys.argv[1:]
if len(ARGS) != 2 or ARGS[0] != '--id' or not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', ARGS[1]): sys.exit('usage: --id <op-id>')
OPID = ARGS[1]; NONCE = os.environ.get('FSGG_NONCE', '')
if not re.fullmatch(r'[0-9a-f]{32}', NONCE): sys.exit('refuse: FSGG_NONCE missing')
OPU, FINU, CTR = f'learn-op-{OPID}.service', f'learn-fin-{OPID}.service', f'learn-op-{OPID}'
HOME = os.environ['HOME']; CUST = f'{HOME}/.local/state/learn-custody/{OPID}'; EVID = f'{HOME}/.local/state/learn-evidence/{OPID}'
APP = '/sys/fs/cgroup/user.slice/user-30034.slice/user@30034.service/app.slice'
CAP = 256 * 1024 * 1024 + 4096 * 1024 + 10240                 # 272 640 000 B (§5.5)
CONTENT_CAP, MAX_MEMBERS, MAX_DEPTH = 256 * 1024 * 1024, 4096, 8
SOFT_S, CUSTODY_DEADLINE_S, ANCHOR_WAIT_S, POLL_S, CHAIN_ANCHOR_S = 1800, 1890, 30, 2, 300
DPATH = os.O_PATH | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC     # search-only traversal (r3 item 1)
DROP_EVERY = 32 << 20                                                 # r4 (3): page-cache drop interval


def drop(fd, written=False):
    """r4 (3): drop fd's page cache (after fdatasync for written data); best effort"""
    try:
        if written: os.fdatasync(fd)
        os.posix_fadvise(fd, 0, 0, os.POSIX_FADV_DONTNEED)
    except OSError: pass


T0 = time.monotonic()
REC = {'schema': None, 'opId': OPID, 'finalizerSha256': SELF_SHA, 'runNonceSha256': hashlib.sha256(NONCE.encode()).hexdigest(),
       'startUtc': datetime.now(timezone.utc).isoformat(), 'events': []}


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


# ---------------- chain log ----------------
CHAIN = {'head': '0' * 64, 'lines': 0, 'lastAnchor': time.monotonic()}
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


def insp(fmt):
    rc, out, _ = run(['podman', 'inspect', '--format', fmt, CTR], 15)
    return out.strip() if rc == 0 else None


def done_marker(pid):
    a, _, _ = run(['podman', 'unshare', 'test', '-f', f'/proc/{pid}/root/out/.done'], 10)
    b, _, _ = run(['podman', 'unshare', 'test', '-L', f'/proc/{pid}/root/out/.done'], 10)
    return a == 0 and b != 0


def start_time(pid):
    try: return open(f'/proc/{pid}/stat').read().rsplit(')', 1)[1].split()[19]
    except (OSError, IndexError): return None


def frozen_line(cg):
    try: return next((l.strip() for l in open(cg + '/cgroup.events') if l.startswith('frozen')), None)
    except OSError: return None


def ident_sample(pid, cid):
    try: cg = '/sys/fs/cgroup' + next(l.split('::', 1)[1].strip() for l in open(f'/proc/{pid}/cgroup') if l.startswith('0::'))
    except (OSError, StopIteration): cg = None
    return {'idc': bool(cg and cid and cid in cg), 'start': start_time(pid), 'frozen': frozen_line(cg) if cg else None,
            'state': insp('{{.State.Status}}'), 'cg': cg}


# ---------------- archive (r2 item 1) ----------------
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


# ---------------- inventory / verdict (§5.6, §5.7, R14; r2 items 3-5) ----------------
def inventory(path, ident_ok, tar_rc, tarerr, over):
    fd = open_nofollow(path, os.O_RDONLY)
    f = os.fdopen(fd, 'rb')
    try:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode): return {'verdict': 'rejected:truncated', 'reason': 'stream not regular'}, None, False
        inv = {'streamBytes': st.st_size}
        if over is True or st.st_size > CAP: inv['verdict'] = 'rejected:budget'; return inv, None, False
        if over is None: inv['verdict'] = 'rejected:truncated'; inv['reason'] = 'archive timed out'; return inv, None, False   # r4 (2)
        f.seek(max(0, st.st_size - 1024)); tail = f.read(1024); f.seek(0)
        if not (st.st_size >= 1024 and st.st_size % 512 == 0 and tail == b'\0' * 1024):
            inv['verdict'] = 'rejected:truncated'; return inv, None, False
        members, status, has_done, problems, count, apparent = [], None, False, set(), 0, 0
        rd = [0]
        try:
            with tarfile.open(fileobj=f, mode='r:', errorlevel=2) as tf:
                for m in tf:
                    tf.members = []                                 # no unbounded member cache (item 3)
                    nm = m.name[2:] if m.name.startswith('./') else m.name
                    if nm in ('', '.'): continue
                    count += 1
                    if count > MAX_MEMBERS:                         # remember only that the cap was exceeded
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
                            if rd[0] >= DROP_EVERY: drop(f.fileno()); rd[0] = 0      # r4 (3)
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
        if count > MAX_MEMBERS: inv['verdict'] = 'rejected:count'
        elif apparent > CONTENT_CAP: inv['verdict'] = 'rejected:size'
        elif problems: inv['verdict'] = 'rejected:type'; inv['problems'] = sorted(problems)
        elif tar_rc != 0 or tarerr > 0: inv['verdict'] = 'rejected:tar'
        else: inv['verdict'] = 'accepted'
        inv['members'] = members
        return inv, status, has_done
    finally:
        try: os.posix_fadvise(f.fileno(), 0, 0, os.POSIX_FADV_DONTNEED)            # r4 (3): drop the stream's read cache
        except OSError: pass
        f.close()


def parse_status(b, has_done):
    if not has_done: return {'status': 'absent'}                    # item 4: no .done -> absent, never ok
    if b is None: return {'status': 'malformed', 'reason': 'missing'}
    if isinstance(b, tuple): return {'status': 'malformed', 'reason': f'size {b[1]}'}
    try: j = json.loads(b.decode('utf-8').rstrip(' '))
    except (UnicodeDecodeError, ValueError): return {'status': 'malformed', 'reason': 'invalid json'}
    if j.get('schema') != 'learn-op-status/1' or j.get('reason') in (None, 'running'): return {'status': 'malformed', 'reason': 'not final'}
    return {'status': 'valid', 'value': {k: j.get(k) for k in ('exit', 'reason', 'detail')}}


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


# ---------------- markers (§7) ----------------
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


# ---------------- evidence (R13; r2 item 2) ----------------
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


def retire_early(path_kind, cg_rel):
    """r4 (1): retirement for the early paths (no container sample): wait up to 60 s for the op unit to end, then the same
    assertion as §1 (container gone, op cgroup absent/unpopulated, unit inactive/failed) and reset-failed; True if retired"""
    for _ in range(60):
        if show(OPU, 'ActiveState').get('ActiveState') in ('inactive', 'failed', ''): break
        time.sleep(1)
    fin = show(OPU, 'ActiveState', 'Result')
    rc, ps, _ = run(['podman', 'ps', '-a', '--filter', f'name=^{CTR}$', '--format', '{{.Names}}'], 15)
    unit_cg = ('/sys/fs/cgroup' + cg_rel) if cg_rel.startswith('/') else f'{APP}/{OPU}'
    REC['opUnit'] = fin
    REC['retirement'] = {'earlyPath': path_kind, 'containerGone': rc == 0 and ps.strip() == '', 'opUnitState': fin.get('ActiveState'),
                         'opCgroupRetired': cgroup_retired(unit_cg), 'opCgroupPath': unit_cg, 'opCgroupFromSystemd': cg_rel.startswith('/'),
                         'containerCgroupRetired': None, 'containerCgroupNote': 'no container sample on an early path',
                         'resetFailedRc': run(['systemctl', '--user', 'reset-failed', OPU], 10)[0]}
    r = REC['retirement']
    return bool(r['containerGone'] and r['opUnitState'] in ('inactive', 'failed', '') and r['opCgroupRetired'])


try:
    os.makedirs(CUST, 0o700, exist_ok=False)
    chain('start', finalizer=SELF_SHA)
    # 1. anchor
    while True:
        s = show(OPU, 'ActiveEnterTimestampMonotonic', 'ActiveState', 'ExecMainPID')
        anchor = int(s.get('ActiveEnterTimestampMonotonic', '0') or 0)
        if anchor > 0: break
        if time.monotonic() - T0 > ANCHOR_WAIT_S:
            mark('FIN', outcome='op_failed_to_start'); chain('failed-to-start')
            finish('learn-op-failed-to-start/1' if retire_early('failed-to-start', '') else 'learn-op-retirement-failure/1')
        time.sleep(1)
    exec_main_pid = s.get('ExecMainPID'); REC['anchorMonotonicUs'] = anchor; REC['execMainPid'] = exec_main_pid
    cg_rel = show(OPU, 'ControlGroup').get('ControlGroup', '')     # r3 item 4: the real op cgroup, read while the unit is active
    REC['opControlGroup'] = cg_rel
    chain('anchor', anchorUs=anchor, execMainPid=exec_main_pid)
    # 2. trigger (chain entries once a minute while waiting, so 5-min anchors keep flowing: A14)
    trigger, last_wait = None, time.monotonic()
    while trigger is None:
        st = show(OPU, 'ActiveState').get('ActiveState')
        pid = insp('{{.State.Pid}}')
        if pid and pid != '0' and done_marker(pid): trigger = 'done'; break
        if int(time.monotonic() * 1e6) - anchor >= SOFT_S * 1e6: trigger = 'time'; break
        if st not in ('active', 'activating'):
            mark('FIN', outcome='op_ended_without_done', state=st); chain('ended-without-done', state=st)
            finish('learn-op-unrecoverable/1' if retire_early('ended-without-done', cg_rel) else 'learn-op-retirement-failure/1')
        if time.monotonic() - last_wait >= 60: chain('wait'); last_wait = time.monotonic()
        time.sleep(POLL_S)
    mark('FIN', trigger=trigger); chain('trigger', trigger=trigger); REC['trigger'] = trigger
    # 3. pause + identity + archive
    REC['pauseRc'] = run(['podman', 'pause', CTR], 30)[0]
    cid, pid = insp('{{.Id}}'), insp('{{.State.Pid}}')
    s0 = ident_sample(pid, cid)
    out_fd = open_nofollow(f'{CUST}/stream.tar', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    err_fd = open_nofollow(f'{CUST}/tar.err', os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        budget_s = max(1.0, min(300.0, CUSTODY_DEADLINE_S - (time.monotonic() * 1e6 - anchor) / 1e6))
        tar_rc, nbytes, over = archive(['podman', 'unshare', '/usr/bin/tar', '--format=ustar', '--one-file-system',
                                        '-C', f'/proc/{pid}/root/out', '-cf', '-', '.'], out_fd, err_fd, CAP, timeout_s=budget_s)
        os.fsync(out_fd); os.posix_fadvise(out_fd, 0, 0, os.POSIX_FADV_DONTNEED)   # drop charged page cache
    finally: os.close(out_fd); os.close(err_fd)
    tarerr = os.stat(f'{CUST}/tar.err').st_size
    s1 = ident_sample(pid, cid)
    ident_ok = (s0['idc'] and s1['idc'] and s0['start'] == s1['start'] and s0['start'] is not None
                and s0['frozen'] == s1['frozen'] == 'frozen 1' and s0['state'] == s1['state'] == 'paused')
    REC['identity'] = {'before': {k: v for k, v in s0.items() if k != 'cg'}, 'after': {k: v for k, v in s1.items() if k != 'cg'}, 'identOk': ident_ok}
    REC['archive'] = {'tarRc': tar_rc, 'tarerrBytes': tarerr, 'streamBytes': nbytes, 'overCap': over is True, 'timedOut': over is None}
    if ident_ok and tarerr:
        with open(f'{CUST}/tar.err', 'rb') as e_: REC['archive']['tarerrHead'] = e_.read(200).decode('utf-8', 'replace')
    mark('FIN', ident_ok=int(ident_ok), tar_rc=tar_rc, tarerr_bytes=tarerr, over=int(over is True))
    rc, sf, _ = run(['podman', 'unshare', 'stat', '-f', '-c', '%a %S %b', f'/proc/{pid}/root/out'], 10)
    try: a_, bs, b_ = (int(x) for x in sf.split()); REC['outStatfs'] = {'freeBytes': a_ * bs, 'totalBytes': b_ * bs}
    except ValueError: REC['outStatfs'] = None
    # 4. inventory + verdict + status
    inv, status_b, has_done = inventory(f'{CUST}/stream.tar', ident_ok, tar_rc, tarerr, over)      # r4 (2): True/None/False as is
    REC['custody'] = dict(inv)
    if inv['verdict'] in ('rejected:budget', 'rejected:truncated', 'rejected:identity', 'rejected:count'):
        REC['status'] = {'status': 'unavailable'}; REC['enospc'] = 'unknown'
    else:
        REC['status'] = parse_status(status_b, has_done)
        if REC['status']['status'] != 'valid': REC['enospc'] = 'unknown'
        else:
            low = REC['outStatfs'] is not None and REC['outStatfs']['freeBytes'] < 1 << 20
            r = REC['status']['value']['reason']
            REC['enospc'] = 'yes' if (r == 'enospc' and low) else 'inconsistent' if (r == 'enospc') != low else 'no'
    chain('verdict', verdict=inv['verdict'])
    if inv['verdict'] == 'accepted': REC['custody']['extracted'] = extract(f'{CUST}/stream.tar', f'{CUST}/extract')
    os.unlink(f'{CUST}/stream.tar')
    late = int(time.monotonic() * 1e6) - anchor > CUSTODY_DEADLINE_S * 1e6
    REC['custodyLate'] = late
    # 5. unpause + stop
    REC['unpauseRc'] = run(['podman', 'unpause', CTR], 30)[0]; REC['stopRc'] = run(['podman', 'stop', '-t', '10', CTR], 40)[0]
    mark('FIN', unpause_rc=REC['unpauseRc'], stop_rc=REC['stopRc'])
    # 6. wait for the op unit to finish ExecStopPost, then markers, retirement
    for _ in range(60):
        if show(OPU, 'ActiveState').get('ActiveState') in ('inactive', 'failed'): break
        time.sleep(1)
    fin = show(OPU, 'ActiveState', 'Result', 'ExecMainStatus')
    REC['opUnit'] = fin
    mk = markers(exec_main_pid); REC['markers'] = mk
    memev = [m for m in mk['lines'] if m['unit'] == 'op' and m['msg'].startswith('MEMEV ')]
    if len(memev) != 1: REC['oom'] = 'unknown'
    else:
        kv = dict(x.split('=', 1) for x in memev[0]['msg'].split()[1:] if '=' in x)
        REC['oom'] = 'yes' if int(kv.get('oom_kill', '0') or 0) > 0 else 'no'
    REC['deadline'] = fin.get('Result') == 'timeout'
    rc, ps, _ = run(['podman', 'ps', '-a', '--filter', f'name=^{CTR}$', '--format', '{{.Names}}'], 15)
    unit_cg = ('/sys/fs/cgroup' + cg_rel) if cg_rel.startswith('/') else f'{APP}/{OPU}'
    REC['retirement'] = {'containerGone': ps.strip() == '', 'opUnitState': fin.get('ActiveState'),
                         'opCgroupRetired': cgroup_retired(unit_cg), 'opCgroupPath': unit_cg, 'opCgroupFromSystemd': cg_rel.startswith('/'), 'containerCgroupRetired': cgroup_retired(s0['cg']) if s0.get('cg') else True,
                         'resetFailedRc': run(['systemctl', '--user', 'reset-failed', OPU], 10)[0]}
    retired = (REC['retirement']['containerGone'] and fin.get('ActiveState') in ('inactive', 'failed')
               and REC['retirement']['opCgroupRetired'] and REC['retirement']['containerCgroupRetired'])
    # 7. evidence copy + record
    evidence_dir()
    if inv['verdict'] == 'accepted':
        for e in REC['custody']['extracted']:
            def chunks(path=f'{CUST}/extract/{e["name"]}'):
                with open(path, 'rb') as a:
                    n = 0
                    while True:
                        c = a.read(1 << 20)
                        if not c: os.posix_fadvise(a.fileno(), 0, 0, os.POSIX_FADV_DONTNEED); return   # r4 (3)
                        n += len(c)
                        if n % DROP_EVERY < len(c): drop(a.fileno())                                  # r4 (3)
                        yield c
            evidence_file(f'out/{e["name"]}', chunks())
    if late: REC['custodyNote'] = 'custody completed after the 31:30 deadline; partial/late'; finish('learn-op-retirement-failure/1')
    if not retired: finish('learn-op-retirement-failure/1')
    ok = (inv['verdict'] == 'accepted' and REC['status'].get('status') == 'valid' and REC['status']['value']['reason'] == 'ok'
          and REC['oom'] == 'no' and not REC['deadline'])
    finish('learn-op-success/1' if ok else 'learn-op-killed-retired/1')
except SystemExit:
    raise
except Exception as e:
    REC['error'] = f'{type(e).__name__}: {str(e)[:200]}'; mark('FIN', error=type(e).__name__)
    try: run(['podman', 'unpause', CTR], 30); run(['podman', 'stop', '-t', '10', CTR], 40)
    except Exception: pass
    if 'retirement' not in REC:                                    # r4 (1): best-effort retirement block on the exception path
        try: retire_early('exception', REC.get('opControlGroup') or '')
        except Exception as e2: REC['retirement'] = {'earlyPath': 'exception', 'error': type(e2).__name__}
    finish('learn-op-retirement-failure/1')

#!/usr/bin/python3 -I
"""LEARN finite executor: input sealing, claim and admission tooling (design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md
sections 4 and 10; Refs FS-GG/.github#4647). Source only until Root admits an install.

One self-contained file, because root tools run as `python3 -I <file>` and -I drops the script directory from sys.path.

  Root only (write):
    manifest   --out FILE
    quiesce    --m1 FILE --m2 FILE --out FILE [--timer-asserted-stopped yes]
    seal       --m1 FILE --quiesce FILE --snapshot-id ID [--timer-asserted-stopped yes]
    claim      --source-identity HEX --op-id ID --snapshot-id ID --sealed-manifest-sha256 HEX --precheck-result FILE
    void       --source-identity HEX --op-id ID --operator-result FILE --finalizer-record FILE --corroboration-ref URL
               [--operator-exit-code N]
    supersede  --source-identity HEX --old-op-id ID --new-op-id ID --snapshot-id ID --sealed-manifest-sha256 HEX
               --precheck-result FILE --old-finalizer-record FILE --decision-ref URL --reason TEXT
    admit      --op-id ID --argv-json-sha256 HEX --source-identity HEX --operator-sha256 HEX --finalizer-sha256 HEX
               --image-id HEX [--image-id HEX ...] --ttl-seconds N
  Read only (any uid that can read the mounted view, for example the pod operator's account):
    check-admission --op-id ID --argv-json-sha256 HEX --source-identity HEX --operator-sha256 HEX --finalizer-sha256 HEX
               --image-id HEX [...]
    status     --source-identity HEX

The source is FIXED here, not chosen by arguments: the base directory and exactly the two `spool/` subtrees below
(Root decision, #4647). A different selection cannot produce an identity.

Writes go ONLY through the persistent source path /srv/bulk/services/usr-local-libexec-fsgg-learn (the in-VM bind
/usr/local/libexec/fsgg-learn is read-only). Every file is created O_CREAT|O_EXCL|O_NOFOLLOW, root-owned, never overwritten.
The chain from /srv/bulk/services down must be root-owned, not symlinked, not group/other-writable, and the source directory
must be the same inode as the mounted view, so a directory swapped after the mount is refused. The check and the writes use
ONE pinned directory descriptor: /srv/bulk is developer-owned, so a path check followed by path-based writes could be raced by
renaming a directory in between. Every directory is opened with O_DIRECTORY|O_NOFOLLOW through openat and fstat-checked on its
descriptor, and the operation then runs on /proc/self/fd/<fd>, which names that directory whatever happens to the path.

What this tool does and does not prove (stated plainly):
  * Cooperative. `developer` has full sudo and can edit any file this tool writes. The records detect mistakes; they do not
    defend against Root.
  * A void is attested by artifacts written by the operator account (fsgg-learn-qual) and read by Root: the operator RESULT,
    the finalizer record and the marker. That account can write them. Only Root's independent corroboration (recorded as a
    reference, never verified by this tool) strengthens a void. It proves "never mounted", not "never read".
  * Decision and corroboration references are format-checked only (an issue or pull-request comment URL). Root verifies the
    comment; this tool does not.
  * The quiesce gap between M1 and M2 is self-reported by the manifest command; the real elapsed time since M1 is re-checked
    against the clock at seal time. The drain timer and service are MEASURED when this tool can run `systemctl`; otherwise the
    caller's assertion is recorded as an assertion.

Ownership of the per-op state (no shared writer):
  * custody directory ~/.local/state/learn-custody/<op-id>/ : created by the FINALIZER (exist_ok=False); the operator refuses a
    run whose custody directory already exists. Nothing here touches it.
  * input-read marker ~/.local/state/learn-input/<op-id>/input-read : created by the POD OPERATOR (uid 30034, O_EXCL, directory
    0700) immediately before the first read or mount of the input and only after verifying the claim. The finalizer records its
    presence. This tool only reads it.
"""
import functools, hashlib, inspect, json, os, re, shutil, stat, subprocess, sys
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone

SERVICES = '/srv/bulk/services'
SOURCE_ROOT = SERVICES + '/usr-local-libexec-fsgg-learn'
VIEW_ROOT = '/usr/local/libexec/fsgg-learn'
ACCOUNT_STATE = '/mnt/storage/containers/learn-qualification/account/.local/state'
MARKER_ROOT = ACCOUNT_STATE + '/learn-input'
SPOOL_BASE = '/home/developer/.local/share/fs-gg-private/telemetry-spool'
ALLOWED_SUBTREES = ('passive-home-canonical-v1/spool', 'passive-prospective-0106-20261018/spool')
DRAIN_UNITS = ('fdev-telemetry-drain.timer', 'fdev-telemetry-drain.service')
DRAIN_USER, DRAIN_UID = 'developer', 1000
OPERATOR_RESULT_SCHEMA = 'home-learn-pod-operator/1'
FINALIZER_EVIDENCE_SCHEMA_PREFIX = 'learn-op-'
MAX_FILES, MAX_BYTES, MAX_DEPTH, MIN_GAP_S, MAX_TTL_S = 64, 16 << 20, 4, 300, 600
HEX64 = re.compile(r'^[0-9a-f]{64}$')
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,58}$')
COMMENT_URL = re.compile(r'^https://github\.com/FS-GG/[A-Za-z0-9._-]+/(?:issues|pull)/[0-9]+#issuecomment-[0-9]+$')
ACTIVE_STATES = {'active', 'activating', 'reloading', 'deactivating', 'refreshing'}
INACTIVE_STATES = {'inactive', 'failed'}


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


def canon(v):
    return json.dumps(v, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode() + b'\n'


def sha(b):
    return hashlib.sha256(b).hexdigest()


def sha_file(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for blk in iter(lambda: f.read(1 << 20), b''):
            h.update(blk)
    return h.hexdigest()


def utc(dt=None):
    return (dt or datetime.now(timezone.utc)).isoformat()


def parse_utc(s):
    try:
        return datetime.fromisoformat(s)
    except (TypeError, ValueError):
        refuse(f'not an ISO timestamp: {s!r}')


# ---------------------------------------------------------------- path safety
def root_chain_ok(path, start, owner_uid=0):
    """`start` and every component of `path` below it are owner-owned, not symlinks, not group/other-writable."""
    if start != '/' and not (path == start or path.startswith(start + '/')):
        return False, path
    cur = start
    todo = [p for p in path[len(start):].split('/') if p]
    while True:
        s = os.lstat(cur)
        if s.st_uid != owner_uid or stat.S_ISLNK(s.st_mode) or (stat.S_IMODE(s.st_mode) & 0o022):
            return False, cur
        if not todo:
            return True, None
        cur = cur.rstrip('/') + '/' + todo.pop(0)


DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
PINNED_PREFIX = '/proc/self/fd/'


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


@contextmanager
def pinned_source(source_root, view_root, services, owner_uid=0):
    """Pin the writable source directory. /srv/bulk is developer-owned, so a path check followed by path-based writes could be
    raced by renaming a directory in between. The directory is opened once through a chain of O_DIRECTORY|O_NOFOLLOW openat
    calls with every directory fstat-checked on its descriptor, compared by device and inode with the mounted view, and the
    body runs on /proc/self/fd/<fd>, a path that resolves to THAT directory whatever happens to the name afterwards. Everything
    below it is root-owned, mode-checked by sub(), and not writable by the developer."""
    if not os.path.isdir('/proc/self/fd'):
        refuse('/proc is not available; cannot pin the write directory')
    fd = open_dir_chain(source_root, services, owner_uid)
    try:
        try:
            v = os.stat(view_root)
        except OSError:
            refuse('the mounted view is absent')
        a = os.fstat(fd)
        if (a.st_dev, a.st_ino) != (v.st_dev, v.st_ino):
            refuse('the persistent source directory is not the mounted view (swapped or not mounted)')
        yield f'{PINNED_PREFIX}{fd}'
    finally:
        os.close(fd)


def _show(value, pinned, shown):
    if isinstance(value, str) and value.startswith(pinned):
        return shown + value[len(pinned):]
    if isinstance(value, dict):
        return {k: _show(v, pinned, shown) for k, v in value.items()}
    if isinstance(value, list):
        return [_show(v, pinned, shown) for v in value]
    return value


def pinned(fn):
    """Run a write function on a pinned source directory. A call that already carries a pinned path (an internal call from a
    function that pinned) passes through, so one operation never pins twice and cannot straddle two directories."""
    sig = inspect.signature(fn)

    @functools.wraps(fn)
    def wrapper(*args, **kwargs):
        bound = sig.bind(*args, **kwargs)
        bound.apply_defaults()
        sr = bound.arguments['source_root']
        if sr.startswith(PINNED_PREFIX):
            return fn(*bound.args, **bound.kwargs)
        with pinned_source(sr, bound.arguments['view_root'], bound.arguments['services'], bound.arguments.get('owner_uid', 0)) as p:
            bound.arguments['source_root'] = p
            return _show(fn(*bound.args, **bound.kwargs), p, sr)
    return wrapper


def sub(source_root, name, owner_uid=0, mode=0o755):
    p = f'{source_root}/{name}'
    if not os.path.lexists(p):
        os.mkdir(p, mode)
        os.chmod(p, mode)
    s = os.lstat(p)
    if not stat.S_ISDIR(s.st_mode) or s.st_uid != owner_uid or (stat.S_IMODE(s.st_mode) & 0o022):
        refuse(f'{name} is not a root-safe directory')
    return p


def write_new(path, data, mode=0o644, owner_uid=0):
    """O_EXCL|O_NOFOLLOW create, fsync, owner check. Never overwrites."""
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, mode)
    except FileExistsError:
        refuse(f'already exists: {os.path.basename(path)}')
    with os.fdopen(fd, 'wb') as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    if os.geteuid() == 0:
        os.chown(path, owner_uid, owner_uid)
    os.chmod(path, mode)


def read_json(path, cap=16 << 20):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap:
            refuse(f'{os.path.basename(path)} is not a bounded regular file')
        b = f.read()
    return json.loads(b), sha(b)


# ---------------------------------------------------------------- source manifest and identity
def check_subtrees(subtrees, allowed=ALLOWED_SUBTREES):
    if sorted(subtrees) != sorted(allowed):
        refuse('the subtree set must be exactly the pinned pair: ' + ', '.join(allowed))


def safe_rel(path, subtrees):
    """A manifest path must be a normalised relative path inside one pinned subtree, with no .. or empty component."""
    if not isinstance(path, str) or not path or path.startswith('/') or '\\' in path or '\x00' in path:
        return False
    parts = path.split('/')
    if any(p in ('', '.', '..') for p in parts) or os.path.normpath(path) != path:
        return False
    return any(path.startswith(s + '/') for s in subtrees)


def validate_manifest(m, allowed=ALLOWED_SUBTREES):
    """Structural validation done BEFORE any copy: pinned subtrees, safe unique paths, bounded counts and sizes."""
    if not isinstance(m, dict) or m.get('schema') != 'learn-input-source-manifest/1':
        refuse('not a source manifest')
    if sorted(m.get('subtrees', [])) != sorted(allowed):
        refuse('manifest subtrees are not the pinned pair')
    files = m.get('files')
    if not isinstance(files, list) or not files or len(files) > MAX_FILES:
        refuse('manifest file list is empty or over the bound')
    seen, total = set(), 0
    for f in files:
        if not isinstance(f, dict) or not safe_rel(f.get('path'), allowed):
            refuse(f"manifest path is not a safe relative path inside a pinned subtree: {f.get('path') if isinstance(f, dict) else f!r}")
        if f['path'] in seen:
            refuse(f"duplicate manifest path: {f['path']}")
        seen.add(f['path'])
        if f.get('type') != 'file' or not HEX64.match(str(f.get('sha256', ''))) or not isinstance(f.get('size'), int) or f['size'] < 0:
            refuse(f"manifest entry is malformed: {f['path']}")
        if not isinstance(f.get('mtime_ns'), int) or f['mtime_ns'] < 0:
            refuse(f"manifest mtime is malformed: {f['path']}")
        total += f['size']
    if total > MAX_BYTES:
        refuse('manifest total size is over the bound')


def take_manifest(base, subtrees, now=None, allowed=ALLOWED_SUBTREES):
    check_subtrees(subtrees, allowed)
    base = os.path.abspath(base)
    files, total = [], 0
    for st_rel in sorted(subtrees):
        root = f'{base}/{st_rel}'
        if not os.path.isdir(root) or os.path.islink(root):
            refuse(f'subtree is not a real directory: {st_rel}')
        stack = [(root, 0)]
        while stack:
            d, depth = stack.pop()
            if depth > MAX_DEPTH:
                refuse('subtree too deep')
            for e in sorted(os.scandir(d), key=lambda x: x.name):
                s = e.stat(follow_symlinks=False)
                rel = os.path.relpath(e.path, base)
                if stat.S_ISDIR(s.st_mode):
                    stack.append((e.path, depth + 1))
                elif stat.S_ISREG(s.st_mode):
                    total += s.st_size
                    if len(files) >= MAX_FILES or total > MAX_BYTES:
                        refuse('subtree exceeds the file or byte bound')
                    files.append({'path': rel, 'type': 'file', 'size': s.st_size, 'sha256': sha_file(e.path),
                                  'mtime_ns': s.st_mtime_ns, 'ctime_ns': s.st_ctime_ns, 'ino': s.st_ino,
                                  'mode': oct(stat.S_IMODE(s.st_mode))})
                else:
                    refuse(f'not a regular file or directory (symlink, fifo, device, socket): {rel}')
    files.sort(key=lambda f: f['path'])
    return {'schema': 'learn-input-source-manifest/1', 'base': base, 'subtrees': sorted(subtrees),
            'takenUtc': utc(now), 'files': files}


def source_identity(m):
    """Depends on the base and the per-file path, size, sha256 and mtime_ns. The subtree set is pinned (a different selection
    cannot reach this function), and seal() requires the manifest's base to be the base it was given."""
    return sha(canon({'base': m['base'], 'subtrees': sorted(m['subtrees']), 'files': [
        {'path': f['path'], 'size': f['size'], 'sha256': f['sha256'], 'mtime_ns': f['mtime_ns']} for f in m['files']]}))


def manifest_diff(a, b):
    out = []
    fa, fb = {f['path']: f for f in a['files']}, {f['path']: f for f in b['files']}
    for p in sorted(set(fa) | set(fb)):
        if p not in fa or p not in fb:
            out.append(f'{p}: present in only one manifest')
            continue
        for k in ('type', 'size', 'sha256', 'mtime_ns', 'ctime_ns', 'ino', 'mode'):
            if fa[p][k] != fb[p][k]:
                out.append(f'{p}: {k} differs')
    if a['subtrees'] != b['subtrees']:
        out.append('subtrees differ')
    return out


# ---------------------------------------------------------------- quiesce
def open_pids_under(dirs, proc='/proc'):
    dirs = [os.path.realpath(d) + '/' for d in dirs]
    found = set()
    for pid in (n for n in os.listdir(proc) if n.isdigit()):
        for kind in ('fd', 'map_files'):
            try:
                names = os.listdir(f'{proc}/{pid}/{kind}')
            except OSError:
                continue
            for n in names:
                try:
                    target = os.readlink(f'{proc}/{pid}/{kind}/{n}')
                except OSError:
                    continue
                if any((target + '/').startswith(d) or target.startswith(d) for d in dirs):
                    found.add(int(pid))
        for link in ('cwd', 'root'):
            try:
                target = os.readlink(f'{proc}/{pid}/{link}')
            except OSError:
                continue
            if any((target + '/').startswith(d) for d in dirs):
                found.add(int(pid))
    return sorted(found)


def measure_units(runner=subprocess.run, units=DRAIN_UNITS, user=DRAIN_USER, uid=DRAIN_UID):
    """MEASURE the drain units' states with `systemctl --user is-active` in the user's manager. Returns
    {'measured': bool, 'states': {unit: state}}. Any failure to run or an unrecognised state is 'unmeasured'."""
    states, measured = {}, True
    for u in units:
        try:
            p = runner(['runuser', '-u', user, '--', 'env', f'XDG_RUNTIME_DIR=/run/user/{uid}', 'systemctl', '--user', 'is-active', u],
                       capture_output=True, text=True, timeout=15)
            state = (p.stdout or '').strip().splitlines()[0] if (p.stdout or '').strip() else 'unknown'
        except (OSError, subprocess.SubprocessError):
            state = 'unknown'
        states[u] = state
        if state not in ACTIVE_STATES | INACTIVE_STATES:
            measured = False
    return {'measured': measured, 'states': states}


def timer_verdict(measure, asserted):
    """-> (ok, evidence, reasons). A measured active unit refuses. If it could not be measured, only an explicit caller
    assertion lets the proof continue, and it is recorded as an assertion, not a measurement."""
    if measure['measured']:
        active = [u for u, s in measure['states'].items() if s in ACTIVE_STATES]
        return (not active), 'measured', [f'{u} is {measure["states"][u]}' for u in active]
    if asserted:
        return True, 'asserted-by-caller', []
    return False, 'unmeasured', ['the drain units could not be measured and no caller assertion was given']


def quiesce_verdict(m1, m2, dirs, measure, asserted=False, now=None, proc='/proc'):
    validate_manifest(m1)
    validate_manifest(m2)
    why = []
    t_ok, t_evidence, t_why = timer_verdict(measure, asserted)
    why += t_why
    pids = open_pids_under(dirs, proc)
    if pids:
        why.append(f'files are open under the subtrees (pids {pids})')
    why += [f'M1/M2 {d}' for d in manifest_diff(m1, m2)]
    t1, t2 = parse_utc(m1['takenUtc']), parse_utc(m2['takenUtc'])
    now = now or datetime.now(timezone.utc)
    gap = (t2 - t1).total_seconds()
    if gap < MIN_GAP_S:
        why.append(f'M1 and M2 are {gap:.0f}s apart; at least {MIN_GAP_S}s is required')
    if t2 > now:
        why.append('M2 is dated in the future')
    return {'schema': 'learn-input-quiesce/1', 'ok': not why, 'reasons': why, 'gapSeconds': gap,
            'gapBasis': 'takenUtc fields written by the manifest command; self-reported, re-checked against the clock at seal time',
            'timerEvidence': t_evidence, 'timerStates': measure['states'], 'quiescedUtc': utc(now),
            'm1Sha256': sha(canon(m1)), 'm2Sha256': sha(canon(m2)), 'sourceIdentity': source_identity(m1)}


# ---------------------------------------------------------------- seal
@pinned
def seal(base, m1, quiesce, snapshot_id, source_root, view_root, services, owner_uid=0, copy=shutil.copyfile,
         measure=None, asserted=False, now=None, proc='/proc'):
    if not OPID.match(snapshot_id):
        refuse('bad snapshot id')
    validate_manifest(m1)                                # BEFORE anything is created: no copy path can leave the snapshot
    if m1.get('base') != os.path.abspath(base):
        refuse('the manifest was taken from another base directory')
    if not quiesce.get('ok') or quiesce.get('m1Sha256') != sha(canon(m1)):
        refuse('quiesce proof is absent, failed, or for another manifest')
    now = now or datetime.now(timezone.utc)
    elapsed = (now - parse_utc(m1['takenUtc'])).total_seconds()
    if elapsed < MIN_GAP_S:
        refuse(f'only {elapsed:.0f}s have passed since M1 by the clock; at least {MIN_GAP_S}s is required')
    # re-check at seal time: the timer may have been restarted and a process may hold a file open again
    ok, _, why = timer_verdict(measure if measure is not None else measure_units(), asserted)
    if not ok:
        refuse('at seal time: ' + '; '.join(why))
    dirs = [f'{os.path.abspath(base)}/{s}' for s in m1['subtrees']]
    pids = open_pids_under(dirs, proc)
    if pids:
        refuse(f'at seal time files are open under the subtrees (pids {pids})')
    inputs = sub(source_root, 'inputs', owner_uid)
    dest = f'{inputs}/{snapshot_id}'
    if os.path.lexists(dest):
        refuse('snapshot already exists')
    os.mkdir(dest, 0o755)
    try:
        for f in m1['files']:
            src = f"{os.path.abspath(base)}/{f['path']}"
            dst = f"{dest}/{f['path']}"
            if os.path.commonpath([dest, os.path.abspath(dst)]) != dest:        # belt and braces after validate_manifest
                refuse(f"destination escapes the snapshot: {f['path']}")
            os.makedirs(os.path.dirname(dst), mode=0o755, exist_ok=True)
            copy(src, dst)
            os.chmod(dst, 0o644)
            os.utime(dst, ns=(f['mtime_ns'], f['mtime_ns']))
            if os.geteuid() == 0:
                os.chown(dst, owner_uid, owner_uid)
            if sha_file(dst) != f['sha256']:
                refuse(f"copy of {f['path']} does not match its source hash")
        for d, ds, _ in os.walk(dest):
            os.chmod(d, 0o755)
            if os.geteuid() == 0:
                os.chown(d, owner_uid, owner_uid)
        m3 = take_manifest(base, m1['subtrees'])
        diff = manifest_diff(m1, m3)
        if diff:
            refuse('the source changed during the copy: ' + '; '.join(diff))
        sealed = {'schema': 'learn-input-snapshot/1', 'snapshotId': snapshot_id, 'sourceIdentity': source_identity(m1),
                  'quiesceSha256': sha(canon(quiesce)), 'sealedUtc': utc(),
                  'files': [{k: f[k] for k in ('path', 'type', 'size', 'sha256', 'mtime_ns', 'mode')} for f in m1['files']]}
        data = canon(sealed)
        write_new(f'{inputs}/{snapshot_id}.manifest.json', data, owner_uid=owner_uid)
    except BaseException:
        # a partial copy must not survive any failure: remove the whole snapshot directory and verify it is gone
        shutil.rmtree(dest, ignore_errors=True)
        if os.path.lexists(dest):
            shutil.rmtree(dest, ignore_errors=False)
        raise
    return {'snapshotDir': dest, 'sealedManifestSha256': sha(data), 'sourceIdentity': sealed['sourceIdentity'],
            'files': len(sealed['files'])}


# ---------------------------------------------------------------- claim history, state and generation
def _check_ids(ident=None, op=None, other=None):
    if ident is not None and not HEX64.match(ident):
        refuse('source identity must be 64 lowercase hex')
    for o in (op, other):
        if o is not None and not OPID.match(o):
            refuse('bad op id')


def _claim_path(claims, ident):
    return f'{claims}/{ident}.claim'


def claim_history(claims, ident):
    """Every claim ever written for this source identity, from the files on disk: live, consumed (superseded) and voided.
    Returns (claims sorted by generation, intents) where an intent is a supersede record."""
    items, intents = [], []
    if not os.path.isdir(claims):
        return items, intents
    for name in sorted(os.listdir(claims)):
        if not name.startswith(ident):
            continue
        path = f'{claims}/{name}'
        rest = name[len(ident):]
        kind = None
        if rest == '.claim':
            kind = 'live'
        elif rest.startswith('.claim.consumed-'):
            kind = 'consumed'
        elif rest.startswith('.claim.voided-'):
            kind = 'voided'
        elif rest.startswith('.supersede-') and rest.endswith('.json'):
            try:
                rec, _ = read_json(path)
            except (OSError, ValueError, Refuse):
                rec = {}
            intents.append({'file': name, 'old': rec.get('oldOpId'), 'new': rec.get('newOpId'), 'record': rec})
            continue
        if kind:
            c, csha = read_json(path)
            items.append({'file': name, 'kind': kind, 'generation': c.get('generation', 1), 'opId': c.get('opId'),
                          'claimedUtc': c.get('claimedUtc', ''), 'sha256': csha, 'content': c})
    items.sort(key=lambda i: (i['generation'], i['claimedUtc']))
    return items, intents


def _pending(items, intents):
    """Unmatched work: a supersede intent whose successor claim does not exist, or a consumed claim with no successor."""
    ops = {i['opId'] for i in items}
    pend = []
    for it in intents:
        if it['new'] not in ops:
            pend.append({'kind': 'intent-without-successor', 'old': it['old'], 'new': it['new'], 'file': it['file']})
    succ_of = {it['old']: it['new'] for it in intents}
    for i in items:
        if i['kind'] == 'consumed':
            nxt = succ_of.get(i['opId'])
            if nxt is None or nxt not in ops:
                if not any(p['old'] == i['opId'] for p in pend):
                    pend.append({'kind': 'consumed-without-successor', 'old': i['opId'], 'new': nxt, 'file': i['file']})
    return pend


def derive_state(claims, ident):
    """State and next generation from the LATEST event, not from which files happen to exist."""
    items, intents = claim_history(claims, ident)
    pend = _pending(items, intents)
    live = next((i for i in items if i['kind'] == 'live'), None)
    latest = items[-1] if items else None
    if pend:
        state = 'supersede-pending'
    elif live:
        state = 'claimed'
    elif latest and latest['kind'] == 'voided':
        state = 'voided'
    elif latest:
        state = 'consumed'
    else:
        state = 'unclaimed'
    return {'state': state, 'live': live, 'latest': latest, 'nextGeneration': (latest['generation'] + 1) if latest else 1,
            'pending': pend, 'history': [{k: i[k] for k in ('file', 'kind', 'generation', 'opId')} for i in items]}


def status(ident, source_root):
    _check_ids(ident)
    d = derive_state(f'{source_root}/claims', ident)
    out = {'sourceIdentity': ident, 'state': d['state'], 'live': d['live'] is not None, 'nextGeneration': d['nextGeneration'],
           'history': d['history'], 'pending': d['pending']}
    for p in d['pending']:
        if p['kind'] == 'intent-without-successor':
            out.setdefault('resumeWith', []).append(f"supersede --old-op-id {p['old']} --new-op-id {p['new']} (same decision and reason)")
    return out


# ---------------------------------------------------------------- claim
def _precheck_ok(result, op_id):
    if result.get('schema') != OPERATOR_RESULT_SCHEMA:
        refuse('precheck result has another schema')
    if result.get('opId') != op_id:
        refuse('precheck result is for another op')
    if result.get('mode') != '--precheck-only' or result.get('exitCode') != 0 or result.get('errors'):
        refuse('the non-input prechecks did not pass')
    if (result.get('gates') or {}).get('inputMounted') is not False:
        refuse('the precheck result does not show the input untouched')


def _check_seal(inputs_dir, snapshot_id, ident, sealed_sha):
    """The claim is bound to a real seal: the sealed manifest must exist with that sha256 and the same source identity."""
    if not OPID.match(snapshot_id or ''):
        refuse('bad snapshot id')
    mp = f'{inputs_dir}/{snapshot_id}.manifest.json'
    if not os.path.isfile(mp):
        refuse('no sealed manifest for this snapshot id')
    sm, msha = read_json(mp)
    if msha != sealed_sha:
        refuse('the sealed manifest on disk has another sha256')
    if sm.get('schema') != 'learn-input-snapshot/1' or sm.get('snapshotId') != snapshot_id:
        refuse('the sealed manifest names another snapshot')
    if sm.get('sourceIdentity') != ident:
        refuse('the sealed manifest has another source identity')
    snap = f'{inputs_dir}/{snapshot_id}'
    for f in sm.get('files', []):
        p = f'{snap}/{f["path"]}'
        # existence and size only: hashing the sealed copy would be an input read, and the claim precedes the first read
        if not os.path.isfile(p) or os.path.islink(p) or os.path.getsize(p) != f['size']:
            refuse(f'the sealed snapshot is missing or resized: {f["path"]}')


@pinned
def claim(ident, op_id, snapshot_id, sealed_sha, precheck, precheck_sha, source_root, view_root, services, owner_uid=0, supersedes=None):
    _check_ids(ident, op_id)
    if not HEX64.match(sealed_sha):
        refuse('sealed manifest sha256 must be 64 lowercase hex')
    _precheck_ok(precheck, op_id)
    claims = sub(source_root, 'claims', owner_uid)
    d = derive_state(claims, ident)
    if supersedes is None:
        if d['pending']:
            refuse('an unfinished supersede or a consumed claim without a successor exists; resume the supersede: ' +
                   '; '.join(f"{p['kind']} {p['old']}->{p['new']}" for p in d['pending']))
    _check_seal(f'{source_root}/inputs', snapshot_id, ident, sealed_sha)
    generation = d['nextGeneration']
    body = {'schema': 'learn-input-claim/1', 'generation': generation, 'sourceIdentity': ident, 'opId': op_id,
            'snapshotId': snapshot_id, 'sealedManifestSha256': sealed_sha, 'precheckResultSha256': precheck_sha,
            'claimedUtc': utc(), 'supersedes': supersedes}
    data = canon(body)
    write_new(_claim_path(claims, ident), data, owner_uid=owner_uid)
    return {'claim': _claim_path(claims, ident), 'claimSha256': sha(data), 'generation': generation}


# ---------------------------------------------------------------- void
def _void_blockers(op_id, finalizer_record, marker_root):
    """FAIL-CLOSED, for void: reasons the claim may have consumed the input. A missing or odd field counts as a reason."""
    why = []
    if os.path.lexists(f'{marker_root}/{op_id}/input-read'):
        why.append('the input-read marker exists')
    if finalizer_record.get('inputRead') is not False:
        why.append('finalizer record does not state inputRead false')
    if finalizer_record.get('containersStartedWithInput') != []:
        why.append('finalizer record does not show an empty containersStartedWithInput list')
    return why


def _positive_consumption(op_id, finalizer_record, marker_root):
    """POSITIVE evidence, for supersede: the marker exists, inputRead is exactly True, or a container started with the input.
    A missing field, a junk record or an unrelated schema is NOT evidence of consumption."""
    ev = []
    if os.path.lexists(f'{marker_root}/{op_id}/input-read'):
        ev.append('the input-read marker exists')
    if finalizer_record.get('inputRead') is True:
        ev.append('the finalizer record states inputRead true')
    started = finalizer_record.get('containersStartedWithInput')
    if isinstance(started, list) and started:
        ev.append('the finalizer record lists containers started with the input')
    return ev


def _evidence_contract(op_id, operator_result, finalizer_record, marker_root, admission=None):
    """Return a list of reasons the claim is NOT voidable; empty means it is. These artifacts are written by the operator
    account; this check makes them consistent with each other and with the admission, it does not authenticate them."""
    why = _void_blockers(op_id, finalizer_record, marker_root)
    if operator_result.get('schema') != OPERATOR_RESULT_SCHEMA or operator_result.get('opId') != op_id:
        why.append('operator RESULT is for another schema or op')
    if operator_result.get('mode') != '--run':
        why.append('operator RESULT is not from a --run (a precheck result is not evidence about a run)')
    code = operator_result.get('exitCode')
    if not isinstance(code, int) or isinstance(code, bool):
        why.append('operator RESULT has no integer exit code')
    elif code == 0:
        why.append('operator RESULT shows a successful run, which consumed the input')
    if (operator_result.get('gates') or {}).get('inputMounted') is not False:
        why.append('operator RESULT does not show the input unmounted')
    if 'containers' not in operator_result:
        why.append('operator RESULT lacks the container list')
    for c in operator_result.get('containers') or []:
        if any(m.get('destination') == '/in' for m in c.get('mounts') or []):
            why.append(f"operator RESULT shows a container with /in ({c.get('name')})")
    if not str(finalizer_record.get('schema', '')).startswith(FINALIZER_EVIDENCE_SCHEMA_PREFIX) or finalizer_record.get('opId') != op_id:
        why.append('finalizer record is for another schema or op')
    n1, n2 = operator_result.get('runNonceSha256'), finalizer_record.get('runNonceSha256')
    if not (isinstance(n1, str) and HEX64.match(n1) and n1 == n2):
        why.append('the finalizer record is not bound to the RESULT by run nonce')
    if admission is None:
        why.append('no admission file for this op to check the pins against')
    else:
        if operator_result.get('operatorSha256') != admission.get('operatorSha256'):
            why.append('RESULT operator pin differs from the admission')
        if finalizer_record.get('finalizerSha256') != admission.get('finalizerSha256'):
            why.append('finalizer record pin differs from the admission')
    return why


@pinned
def void(ident, op_id, operator_result, op_sha, finalizer_record, fin_sha, corroboration_ref, source_root, view_root, services,
         marker_root=MARKER_ROOT, owner_uid=0, observed_exit_code=None):
    _check_ids(ident, op_id)
    if not COMMENT_URL.match(corroboration_ref or ''):
        refuse('a Root corroboration reference (a GitHub issue or pull request comment URL) is required')
    claims = sub(source_root, 'claims', owner_uid)
    cp = _claim_path(claims, ident)
    if not os.path.isfile(cp):
        refuse('no live claim for this source identity')
    c, csha = read_json(cp)
    if c.get('opId') != op_id:
        refuse('the claim names another op')
    adm = None
    try:
        adm, _ = read_json(f'{source_root}/admissions/{op_id}.admit', 65536)
    except (OSError, ValueError, Refuse):
        adm = None
    why = _evidence_contract(op_id, operator_result, finalizer_record, marker_root, adm)
    if observed_exit_code is not None:
        # Root's own capture of the operator process exit status (for example from the shell): it must match the RESULT
        if isinstance(observed_exit_code, bool) or not isinstance(observed_exit_code, int):
            why.append('the observed exit code is not an integer')
        elif observed_exit_code != operator_result.get('exitCode'):
            why.append(f"the observed exit code {observed_exit_code} differs from the RESULT's {operator_result.get('exitCode')}")
    if why:
        refuse('not voidable (the claim is consumed): ' + '; '.join(why))
    rec = {'schema': 'learn-input-claim-void/1', 'sourceIdentity': ident, 'opId': op_id, 'generation': c.get('generation', 1),
           'claimSha256': csha, 'operatorResultSha256': op_sha, 'finalizerRecordSha256': fin_sha,
           'rootCorroborationRef': corroboration_ref, 'rootCorroborationVerifiedByTool': False, 'observedExitCode': observed_exit_code,
           'voidedUtc': utc(),
           'proves': 'the input was never mounted into any container of this op, as the operator account reports it',
           'doesNotProve': 'that the input was never read',
           'attestation': ('the operator RESULT, the finalizer record and the marker are written by the operator account '
                           '(fsgg-learn-qual); this void is consistent with them, not independent of them. Only Root\'s '
                           'independent corroboration, referenced above and not verified by this tool, strengthens it.')}
    write_new(f'{claims}/{ident}.void-{op_id}.json', canon(rec), owner_uid=owner_uid)
    os.rename(cp, f'{cp}.voided-{op_id}')
    return {'voided': f'{cp}.voided-{op_id}', 'claimSha256': csha}


# ---------------------------------------------------------------- supersede (resumable)
@pinned
def supersede(ident, old_op, new_op, snapshot_id, sealed_sha, precheck, precheck_sha, old_finalizer, old_fin_sha, decision_ref,
              reason, source_root, view_root, services, marker_root=MARKER_ROOT, owner_uid=0, rename=os.rename):
    _check_ids(ident, old_op, new_op)
    if old_op == new_op:
        refuse('the successor must be a different op')
    if not HEX64.match(sealed_sha):
        refuse('sealed manifest sha256 must be 64 lowercase hex')
    _precheck_ok(precheck, new_op)                  # validate the successor before anything is written or renamed
    if not COMMENT_URL.match(decision_ref or ''):
        refuse('a Root decision reference (a GitHub issue or pull request comment URL) is required; Root verifies the comment, this tool does not')
    if not reason or len(reason) > 1000:
        refuse('a bounded reason is required')
    claims = sub(source_root, 'claims', owner_uid)
    _check_seal(f'{source_root}/inputs', snapshot_id, ident, sealed_sha)
    items, intents = claim_history(claims, ident)
    cp = _claim_path(claims, ident)
    consumed_path = f'{cp}.consumed-{old_op}'
    intent_path = f'{claims}/{ident}.supersede-{old_op}-to-{new_op}.json'
    live = next((i for i in items if i['kind'] == 'live'), None)
    consumed = next((i for i in items if i['kind'] == 'consumed' and i['opId'] == old_op), None)
    successor = next((i for i in items if i['opId'] == new_op), None)
    resuming = os.path.lexists(intent_path)
    pending = _pending(items, intents)
    others = [p for p in pending if not (p['old'] == old_op and p['new'] == new_op)]
    if others:
        # another supersede is unfinished: a second one would strand the first. Name what must be resumed.
        refuse('another supersede is unfinished; resume it first: ' +
               '; '.join(f"supersede --old-op-id {p['old']} --new-op-id {p['new']}" for p in others))
    if old_finalizer.get('opId') != old_op:
        refuse('the finalizer record is for another op')
    if resuming:
        rec, _ = read_json(intent_path)
        if rec.get('decisionRef') != decision_ref or rec.get('reason') != reason or rec.get('oldFinalizerRecordSha256') != old_fin_sha:
            refuse('the recorded supersede intent has a different decision, reason or evidence; resume with the same values')
        if successor:
            refuse('the supersede already completed')
    else:
        # a voidable claim must be voided, not superseded: supersede is only for a consumed claim
        if not live or live['opId'] != old_op:
            refuse('the live claim names another op' if live else 'no live claim to supersede')
        if not _positive_consumption(old_op, old_finalizer, marker_root):
            refuse('no positive evidence that the claim was consumed (marker, inputRead true, or a container started with the input); '
                   'a claim without it is voided, not superseded')
        old = live
        rec = {'schema': 'learn-input-claim-supersede/1', 'sourceIdentity': ident, 'oldOpId': old_op, 'newOpId': new_op,
               'oldClaimSha256': old['sha256'], 'oldFinalizerRecordSha256': old_fin_sha, 'decisionRef': decision_ref,
               'decisionRefVerifiedByTool': False, 'reason': reason, 'supersededUtc': utc(), 'generationFrom': old['generation']}
        write_new(intent_path, canon(rec), owner_uid=owner_uid)                  # intent first
    if live and live['opId'] == old_op and not consumed:
        rename(cp, consumed_path)
    elif not consumed and not os.path.lexists(consumed_path):
        refuse('neither a live nor a consumed claim for the old op exists')
    out = claim(ident, new_op, snapshot_id, sealed_sha, precheck, precheck_sha, source_root, view_root, services, owner_uid, supersedes=old_op)
    return {'consumed': consumed_path, 'resumed': resuming, **out}


# ---------------------------------------------------------------- admission
ADMIT_FIELDS = ('opId', 'argvJsonSha256', 'sourceIdentity', 'operatorSha256', 'finalizerSha256', 'imageIds')


@pinned
def admit(op_id, argv_sha, ident, operator_sha, finalizer_sha, image_ids, ttl, source_root, view_root, services, owner_uid=0, now=None):
    _check_ids(ident, op_id)
    for h in (argv_sha, operator_sha, finalizer_sha, *image_ids):
        if not HEX64.match(h):
            refuse('every pin must be 64 lowercase hex')
    if not image_ids:
        refuse('at least one image id is required')
    if not (1 <= ttl <= MAX_TTL_S):
        refuse(f'ttl must be between 1 and {MAX_TTL_S} seconds')
    adm = sub(source_root, 'admissions', owner_uid)
    now = now or datetime.now(timezone.utc)
    body = {'schema': 'learn-pod-admission/1', 'opId': op_id, 'argvJsonSha256': argv_sha, 'sourceIdentity': ident,
            'operatorSha256': operator_sha, 'finalizerSha256': finalizer_sha, 'imageIds': sorted(image_ids),
            'issuedUtc': utc(now), 'expiresUtc': utc(now + timedelta(seconds=ttl))}
    data = canon(body)
    write_new(f'{adm}/{op_id}.admit', data, owner_uid=owner_uid)
    return {'admission': f'{adm}/{op_id}.admit', 'admissionSha256': sha(data)}


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


# ---------------------------------------------------------------- CLI
def _args(argv, multi=('--image-id',)):
    out, i = {}, 1
    while i < len(argv):
        k = argv[i]
        if not k.startswith('--') or i + 1 >= len(argv):
            refuse(f'bad arguments near {k}')
        if k in multi:
            out.setdefault(k, []).append(argv[i + 1])
        else:
            if k in out:
                refuse(f'{k} given twice')
            out[k] = argv[i + 1]
        i += 2
    return out


READ_ONLY = ('check-admission', 'status')


def main(argv):
    R = {'schema': 'home-learn-seal-claim/2', 'scriptSha256': sha_file(os.path.abspath(__file__)), 'errors': []}
    code = 0
    try:
        cmd = argv[0] if argv else ''
        if cmd not in READ_ONLY and os.geteuid() != 0:
            refuse('run as root')
        os.umask(0o022)
        a = _args(argv)
        sr, vr, sv = SOURCE_ROOT, VIEW_ROOT, SERVICES
        asserted = a.get('--timer-asserted-stopped') == 'yes'
        if cmd == 'manifest':
            m = take_manifest(SPOOL_BASE, list(ALLOWED_SUBTREES))
            data = canon(m)
            write_new(a['--out'], data, mode=0o600)
            R.update(manifestSha256=sha(data), sourceIdentity=source_identity(m), files=len(m['files']))
        elif cmd == 'quiesce':
            m1, _ = read_json(a['--m1'])
            m2, _ = read_json(a['--m2'])
            dirs = [f'{SPOOL_BASE}/{s}' for s in ALLOWED_SUBTREES]
            v = quiesce_verdict(m1, m2, dirs, measure_units(), asserted)
            write_new(a['--out'], canon(v), mode=0o600)
            R.update(verdict=v)
            code = 0 if v['ok'] else 5
        elif cmd == 'seal':
            m1, _ = read_json(a['--m1'])
            q, _ = read_json(a['--quiesce'])
            R.update(seal(SPOOL_BASE, m1, q, a['--snapshot-id'], sr, vr, sv, asserted=asserted))
        elif cmd == 'claim':
            pr, psha = read_json(a['--precheck-result'])
            R.update(claim(a['--source-identity'], a['--op-id'], a['--snapshot-id'], a['--sealed-manifest-sha256'], pr, psha, sr, vr, sv))
        elif cmd == 'void':
            o, osha = read_json(a['--operator-result'])
            f, fsha = read_json(a['--finalizer-record'])
            oc = int(a['--operator-exit-code']) if '--operator-exit-code' in a else None
            R.update(void(a['--source-identity'], a['--op-id'], o, osha, f, fsha, a['--corroboration-ref'], sr, vr, sv, observed_exit_code=oc))
        elif cmd == 'supersede':
            pr, psha = read_json(a['--precheck-result'])
            f, fsha = read_json(a['--old-finalizer-record'])
            R.update(supersede(a['--source-identity'], a['--old-op-id'], a['--new-op-id'], a['--snapshot-id'], a['--sealed-manifest-sha256'],
                               pr, psha, f, fsha, a['--decision-ref'], a['--reason'], sr, vr, sv))
        elif cmd == 'admit':
            R.update(admit(a['--op-id'], a['--argv-json-sha256'], a['--source-identity'], a['--operator-sha256'],
                           a['--finalizer-sha256'], a['--image-id'], int(a['--ttl-seconds']), sr, vr, sv))
        elif cmd == 'check-admission':
            why = check_admission(vr, a['--op-id'], a['--argv-json-sha256'], a['--source-identity'], a['--operator-sha256'],
                                  a['--finalizer-sha256'], a['--image-id'], '/')
            R.update(admissionReasons=why)
            code = 0 if not why else 5
        elif cmd == 'status':
            R.update(status(a['--source-identity'], vr))
        else:
            refuse('usage: manifest | quiesce | seal | claim | void | supersede | admit | check-admission | status')
    except Refuse as e:
        R['errors'].append(str(e))
        code = 5
    except KeyError as e:
        R['errors'].append(f'missing argument {e}')
        code = 5
    except Exception as e:
        R['errors'].append(f'{type(e).__name__}: {str(e)[:200]}')
        code = 5
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': sha(b.encode()), 'exit': code}))
    return code


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

#!/usr/bin/python3 -I
"""LEARN finite executor: input sealing, claim and admission tooling (design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md
sections 4 and 10; Refs FS-GG/.github#4647). Root only. Source only until Root admits an install.

One self-contained file, because root tools run as `python3 -I <file>` and -I drops the script directory from sys.path.

    manifest   --base DIR --subtree REL [--subtree REL ...] --out FILE
    quiesce    --m1 FILE --m2 FILE --base DIR --subtree REL [...] --timer-active yes|no --out FILE
    seal       --base DIR --subtree REL [...] --m1 FILE --quiesce FILE --snapshot-id ID
    claim      --source-identity HEX --op-id ID --sealed-manifest-sha256 HEX --precheck-result FILE
    void       --source-identity HEX --op-id ID --operator-result FILE --finalizer-record FILE
    supersede  --source-identity HEX --old-op-id ID --new-op-id ID --sealed-manifest-sha256 HEX --precheck-result FILE
               --old-finalizer-record FILE --decision-ref URL --reason TEXT
    admit      --op-id ID --argv-json-sha256 HEX --source-identity HEX --operator-sha256 HEX --finalizer-sha256 HEX
               --image-id HEX [--image-id HEX ...] --ttl-seconds N
    check-admission --op-id ID --argv-json-sha256 HEX --source-identity HEX --operator-sha256 HEX --finalizer-sha256 HEX
               --image-id HEX [...]
    status     --source-identity HEX

Writes go ONLY through the persistent source path /srv/bulk/services/usr-local-libexec-fsgg-learn (the in-VM bind
/usr/local/libexec/fsgg-learn is read-only). Every file is created O_CREAT|O_EXCL|O_NOFOLLOW, root-owned, never
overwritten. The chain from /srv/bulk/services down must be root-owned, not symlinked, not group/other-writable, and the
source directory must be the same inode as the mounted view, so a directory swapped after the mount is refused.

Cooperative: `developer` has full sudo and can edit any file this tool writes. The records detect mistakes and a
compromised `fsgg-learn-qual`; they do not defend against Root.

Void proves "never mounted", not "never read". A claim is voidable only when (1) no `input-read` marker exists in the op's
custody directory, (2) the operator RESULT shows the input was never mounted and no container had `/in`, and (3) the
finalizer record says no container started with the input. Otherwise the claim is consumed and can only be superseded.
"""
import hashlib, json, os, re, shutil, stat, sys, time
from datetime import datetime, timedelta, timezone

SERVICES = '/srv/bulk/services'
SOURCE_ROOT = SERVICES + '/usr-local-libexec-fsgg-learn'
VIEW_ROOT = '/usr/local/libexec/fsgg-learn'
CUSTODY_ROOT = '/mnt/storage/containers/learn-qualification/account/.local/state/learn-custody'
OPERATOR_RESULT_SCHEMA = 'home-learn-pod-operator/1'
FINALIZER_EVIDENCE_SCHEMA_PREFIX = 'learn-op-'
MAX_FILES, MAX_BYTES, MAX_DEPTH, MIN_GAP_S, MAX_TTL_S = 64, 16 << 20, 4, 300, 600
HEX64 = re.compile(r'^[0-9a-f]{64}$')
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,58}$')


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
        cur = (cur.rstrip('/') + '/' + todo.pop(0))


def guard_write_root(source_root, view_root, services, owner_uid=0):
    """Refuse unless the writable source root is root-safe and is the same directory as the mounted view."""
    ok, bad = root_chain_ok(source_root, services, owner_uid)
    if not ok:
        refuse(f'write path not root-safe at {bad}')
    try:
        a, b = os.stat(source_root), os.stat(view_root)
    except OSError:
        refuse('the mounted view is absent')
    if (a.st_dev, a.st_ino) != (b.st_dev, b.st_ino):
        refuse('the persistent source directory is not the mounted view (swapped or not mounted)')


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
def take_manifest(base, subtrees, now=None):
    base = os.path.abspath(base)
    files, total = [], 0
    for st_rel in sorted(subtrees):
        if st_rel.startswith('/') or '..' in st_rel.split('/'):
            refuse(f'subtree must be a relative path without ..: {st_rel}')
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
    return sha(canon({'subtrees': m['subtrees'], 'files': [
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


def quiesce_verdict(m1, m2, dirs, timer_active, proc='/proc'):
    why = []
    if timer_active:
        why.append('the drain timer or its service is active')
    pids = open_pids_under(dirs, proc)
    if pids:
        why.append(f'files are open under the subtrees (pids {pids})')
    why += [f'M1/M2 {d}' for d in manifest_diff(m1, m2)]
    gap = (datetime.fromisoformat(m2['takenUtc']) - datetime.fromisoformat(m1['takenUtc'])).total_seconds()
    if gap < MIN_GAP_S:
        why.append(f'M1 and M2 are {gap:.0f}s apart; at least {MIN_GAP_S}s is required')
    return {'schema': 'learn-input-quiesce/1', 'ok': not why, 'reasons': why, 'gapSeconds': gap,
            'm1Sha256': sha(canon(m1)), 'm2Sha256': sha(canon(m2)), 'sourceIdentity': source_identity(m1)}


# ---------------------------------------------------------------- seal
def seal(base, subtrees, m1, quiesce, snapshot_id, source_root, view_root, services, owner_uid=0, copy=shutil.copyfile):
    if not OPID.match(snapshot_id):
        refuse('bad snapshot id')
    if not quiesce.get('ok') or quiesce.get('m1Sha256') != sha(canon(m1)):
        refuse('quiesce proof is absent, failed, or for another manifest')
    guard_write_root(source_root, view_root, services, owner_uid)
    inputs = sub(source_root, 'inputs', owner_uid)
    dest = f'{inputs}/{snapshot_id}'
    if os.path.lexists(dest):
        refuse('snapshot already exists')
    os.mkdir(dest, 0o755)
    try:
        for f in m1['files']:
            src = f"{os.path.abspath(base)}/{f['path']}"
            dst = f"{dest}/{f['path']}"
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
        m3 = take_manifest(base, subtrees)
        diff = manifest_diff(m1, m3)
        if diff:
            refuse('the source changed during the copy: ' + '; '.join(diff))
    except Exception:
        shutil.rmtree(dest, ignore_errors=True)
        raise
    sealed = {'schema': 'learn-input-snapshot/1', 'snapshotId': snapshot_id, 'sourceIdentity': source_identity(m1),
              'quiesceSha256': sha(canon(quiesce)), 'sealedUtc': utc(),
              'files': [{k: f[k] for k in ('path', 'type', 'size', 'sha256', 'mtime_ns', 'mode')} for f in m1['files']]}
    data = canon(sealed)
    write_new(f'{inputs}/{snapshot_id}.manifest.json', data, owner_uid=owner_uid)
    return {'snapshotDir': dest, 'sealedManifestSha256': sha(data), 'sourceIdentity': sealed['sourceIdentity'],
            'files': len(sealed['files'])}


# ---------------------------------------------------------------- claim, void, supersede
def _claim_path(claims, ident):
    return f'{claims}/{ident}.claim'


def _check_ids(ident=None, op=None, other=None):
    if ident is not None and not HEX64.match(ident):
        refuse('source identity must be 64 lowercase hex')
    for o in (op, other):
        if o is not None and not OPID.match(o):
            refuse('bad op id')


def _precheck_ok(result, op_id):
    if result.get('schema') != OPERATOR_RESULT_SCHEMA:
        refuse('precheck result has another schema')
    if result.get('opId') != op_id:
        refuse('precheck result is for another op')
    if result.get('mode') != '--precheck-only' or result.get('exitCode') != 0 or result.get('errors'):
        refuse('the non-input prechecks did not pass')
    if (result.get('gates') or {}).get('inputMounted') is not False:
        refuse('the precheck result does not show the input untouched')


def claim(ident, op_id, sealed_sha, precheck, precheck_sha, source_root, view_root, services, owner_uid=0, generation=1, supersedes=None):
    _check_ids(ident, op_id)
    if not HEX64.match(sealed_sha):
        refuse('sealed manifest sha256 must be 64 lowercase hex')
    _precheck_ok(precheck, op_id)
    guard_write_root(source_root, view_root, services, owner_uid)
    claims = sub(source_root, 'claims', owner_uid)
    body = {'schema': 'learn-input-claim/1', 'generation': generation, 'sourceIdentity': ident, 'opId': op_id,
            'sealedManifestSha256': sealed_sha, 'precheckResultSha256': precheck_sha, 'claimedUtc': utc(),
            'supersedes': supersedes}
    data = canon(body)
    write_new(_claim_path(claims, ident), data, owner_uid=owner_uid)
    return {'claim': _claim_path(claims, ident), 'claimSha256': sha(data), 'generation': generation}


def _evidence_contract(op_id, operator_result, finalizer_record, custody_root):
    """Return a list of reasons the claim is NOT voidable; empty means it is."""
    why = []
    if os.path.lexists(f'{custody_root}/{op_id}/input-read'):
        why.append('the input-read marker exists')
    if operator_result.get('schema') != OPERATOR_RESULT_SCHEMA or operator_result.get('opId') != op_id:
        why.append('operator RESULT is for another schema or op')
    if (operator_result.get('gates') or {}).get('inputMounted') is not False:
        why.append('operator RESULT does not show the input unmounted')
    for c in operator_result.get('containers') or []:
        if any(m.get('destination') == '/in' for m in c.get('mounts') or []):
            why.append(f"operator RESULT shows a container with /in ({c.get('name')})")
    if 'containers' not in operator_result:
        why.append('operator RESULT lacks the container list')
    if not str(finalizer_record.get('schema', '')).startswith(FINALIZER_EVIDENCE_SCHEMA_PREFIX) or finalizer_record.get('opId') != op_id:
        why.append('finalizer record is for another schema or op')
    if finalizer_record.get('inputRead') is not False:
        why.append('finalizer record does not state inputRead false')
    if finalizer_record.get('containersStartedWithInput') != []:
        why.append('finalizer record does not show an empty containersStartedWithInput list')
    return why


def void(ident, op_id, operator_result, op_sha, finalizer_record, fin_sha, source_root, view_root, services,
         custody_root=CUSTODY_ROOT, owner_uid=0):
    _check_ids(ident, op_id)
    guard_write_root(source_root, view_root, services, owner_uid)
    claims = sub(source_root, 'claims', owner_uid)
    cp = _claim_path(claims, ident)
    if not os.path.isfile(cp):
        refuse('no live claim for this source identity')
    c, csha = read_json(cp)
    if c.get('opId') != op_id:
        refuse('the claim names another op')
    why = _evidence_contract(op_id, operator_result, finalizer_record, custody_root)
    if why:
        refuse('not voidable (the claim is consumed): ' + '; '.join(why))
    rec = {'schema': 'learn-input-claim-void/1', 'sourceIdentity': ident, 'opId': op_id, 'claimSha256': csha,
           'operatorResultSha256': op_sha, 'finalizerRecordSha256': fin_sha, 'voidedUtc': utc(),
           'proves': 'the input was never mounted into any container of this op',
           'doesNotProve': 'that the input was never read'}
    write_new(f'{claims}/{ident}.void-{op_id}.json', canon(rec), owner_uid=owner_uid)
    os.rename(cp, f'{cp}.voided-{op_id}')
    return {'voided': f'{cp}.voided-{op_id}', 'claimSha256': csha}


def supersede(ident, old_op, new_op, sealed_sha, precheck, precheck_sha, old_finalizer, old_fin_sha, decision_ref, reason,
              source_root, view_root, services, custody_root=CUSTODY_ROOT, owner_uid=0):
    _check_ids(ident, old_op, new_op)
    if not HEX64.match(sealed_sha):
        refuse('sealed manifest sha256 must be 64 lowercase hex')
    _precheck_ok(precheck, new_op)      # validate the successor before anything is written or renamed
    if not re.match(r'^https://github\.com/FS-GG/[^ ]+$', decision_ref or ''):
        refuse('a Root decision reference (https://github.com/FS-GG/...) is required')
    if not reason or len(reason) > 1000:
        refuse('a bounded reason is required')
    guard_write_root(source_root, view_root, services, owner_uid)
    claims = sub(source_root, 'claims', owner_uid)
    cp = _claim_path(claims, ident)
    if not os.path.isfile(cp):
        refuse('no live claim to supersede')
    c, csha = read_json(cp)
    if c.get('opId') != old_op:
        refuse('the live claim names another op')
    # a voidable claim must be voided, not superseded: supersede is only for a consumed claim
    if old_finalizer.get('opId') != old_op:
        refuse('the finalizer record is for another op')
    if not _evidence_contract(old_op, {'schema': OPERATOR_RESULT_SCHEMA, 'opId': old_op, 'gates': {'inputMounted': False},
                                       'containers': []}, old_finalizer, custody_root):
        refuse('the claim is voidable; void it instead of superseding it')
    rec = {'schema': 'learn-input-claim-supersede/1', 'sourceIdentity': ident, 'oldOpId': old_op, 'newOpId': new_op,
           'oldClaimSha256': csha, 'oldFinalizerRecordSha256': old_fin_sha, 'decisionRef': decision_ref,
           'reason': reason, 'supersededUtc': utc(), 'generationFrom': c.get('generation', 1)}
    write_new(f'{claims}/{ident}.supersede-{old_op}-to-{new_op}.json', canon(rec), owner_uid=owner_uid)  # intent first
    os.rename(cp, f'{cp}.consumed-{old_op}')
    out = claim(ident, new_op, sealed_sha, precheck, precheck_sha, source_root, view_root, services, owner_uid,
                generation=c.get('generation', 1) + 1, supersedes=old_op)
    return {'consumed': f'{cp}.consumed-{old_op}', **out}


def status(ident, source_root):
    _check_ids(ident)
    claims = f'{source_root}/claims'
    names = sorted(n for n in os.listdir(claims) if n.startswith(ident)) if os.path.isdir(claims) else []
    live = os.path.isfile(_claim_path(claims, ident))
    intents = [n for n in names if '.supersede-' in n and n.endswith('.json')]
    if live:
        state = 'claimed'
    elif intents and any('.consumed-' in n for n in names):
        state = 'supersede-incomplete'
    elif any('.voided-' in n for n in names):
        state = 'voided'
    else:
        state = 'unclaimed'
    return {'sourceIdentity': ident, 'live': live, 'state': state, 'files': names}


# ---------------------------------------------------------------- admission
ADMIT_FIELDS = ('opId', 'argvJsonSha256', 'sourceIdentity', 'operatorSha256', 'finalizerSha256', 'imageIds')


def admit(op_id, argv_sha, ident, operator_sha, finalizer_sha, image_ids, ttl, source_root, view_root, services, owner_uid=0, now=None):
    _check_ids(ident, op_id)
    for h in (argv_sha, operator_sha, finalizer_sha, *image_ids):
        if not HEX64.match(h):
            refuse('every pin must be 64 lowercase hex')
    if not image_ids:
        refuse('at least one image id is required')
    if not (1 <= ttl <= MAX_TTL_S):
        refuse(f'ttl must be between 1 and {MAX_TTL_S} seconds')
    guard_write_root(source_root, view_root, services, owner_uid)
    adm = sub(source_root, 'admissions', owner_uid)
    now = now or datetime.now(timezone.utc)
    body = {'schema': 'learn-pod-admission/1', 'opId': op_id, 'argvJsonSha256': argv_sha, 'sourceIdentity': ident,
            'operatorSha256': operator_sha, 'finalizerSha256': finalizer_sha, 'imageIds': sorted(image_ids),
            'issuedUtc': utc(now), 'expiresUtc': utc(now + timedelta(seconds=ttl))}
    data = canon(body)
    write_new(f'{adm}/{op_id}.admit', data, owner_uid=owner_uid)
    return {'admission': f'{adm}/{op_id}.admit', 'admissionSha256': sha(data)}


def check_admission(path_root, op_id, argv_sha, ident, operator_sha, finalizer_sha, image_ids, chain_start, owner_uid=0, now=None):
    """Reader side (used by the pod operator): returns [] when the admission matches, else the reasons."""
    p = f'{path_root}/admissions/{op_id}.admit'
    try:
        ok, bad = root_chain_ok(p, chain_start, owner_uid)
    except OSError:
        return ['admission is absent or unreadable']
    if not ok:
        return [f'admission path not root-safe at {bad}']
    try:
        a, _ = read_json(p, 65536)
    except (OSError, ValueError):
        return ['admission is absent or unreadable']
    why = []
    want = {'opId': op_id, 'argvJsonSha256': argv_sha, 'sourceIdentity': ident, 'operatorSha256': operator_sha,
            'finalizerSha256': finalizer_sha, 'imageIds': sorted(image_ids)}
    for k in ADMIT_FIELDS:
        if a.get(k) != want[k]:
            why.append(f'{k} differs')
    now = now or datetime.now(timezone.utc)
    try:
        if now > datetime.fromisoformat(a['expiresUtc']):
            why.append('the admission has expired')
    except (KeyError, ValueError):
        why.append('the admission has no valid expiry')
    return why


# ---------------------------------------------------------------- CLI
def _args(argv, multi=('--subtree', '--image-id')):
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


def _need(a, *keys):
    for k in keys:
        if k not in a:
            refuse(f'missing {k}')
    return [a[k] for k in keys]


def main(argv):
    R = {'schema': 'home-learn-seal-claim/1', 'scriptSha256': sha_file(os.path.abspath(__file__)), 'errors': []}
    code = 0
    try:
        if os.geteuid() != 0:
            refuse('run as root')
        os.umask(0o022)
        cmd = argv[0] if argv else ''
        a = _args(argv[0:] if argv else [])
        sr, vr, sv = SOURCE_ROOT, VIEW_ROOT, SERVICES
        if cmd == 'manifest':
            m = take_manifest(a['--base'], a['--subtree'])
            data = canon(m)
            write_new(a['--out'], data, mode=0o600)
            R.update(manifestSha256=sha(data), sourceIdentity=source_identity(m), files=len(m['files']))
        elif cmd == 'quiesce':
            m1, _ = read_json(a['--m1'])
            m2, _ = read_json(a['--m2'])
            dirs = [f"{os.path.abspath(a['--base'])}/{s}" for s in a['--subtree']]
            if a.get('--timer-active') not in ('yes', 'no'):
                refuse('--timer-active must be yes or no')
            v = quiesce_verdict(m1, m2, dirs, a['--timer-active'] == 'yes')
            write_new(a['--out'], canon(v), mode=0o600)
            R.update(verdict=v)
            code = 0 if v['ok'] else 5
        elif cmd == 'seal':
            m1, _ = read_json(a['--m1'])
            q, _ = read_json(a['--quiesce'])
            R.update(seal(a['--base'], a['--subtree'], m1, q, a['--snapshot-id'], sr, vr, sv))
        elif cmd == 'claim':
            pr, psha = read_json(a['--precheck-result'])
            R.update(claim(a['--source-identity'], a['--op-id'], a['--sealed-manifest-sha256'], pr, psha, sr, vr, sv))
        elif cmd == 'void':
            o, osha = read_json(a['--operator-result'])
            f, fsha = read_json(a['--finalizer-record'])
            R.update(void(a['--source-identity'], a['--op-id'], o, osha, f, fsha, sr, vr, sv))
        elif cmd == 'supersede':
            pr, psha = read_json(a['--precheck-result'])
            f, fsha = read_json(a['--old-finalizer-record'])
            R.update(supersede(a['--source-identity'], a['--old-op-id'], a['--new-op-id'], a['--sealed-manifest-sha256'],
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

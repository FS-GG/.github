#!/usr/bin/python3 -I
"""LEARN #4745 retirement acknowledgement writer r2 (governing spec rev 11 r2 section 7; decision 4516#6352952638;
persistence: Root decision 4647#6533215654 item 3; design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md
section 10). Refs FS-GG/.github#4647. Supersedes r1 (aadd55b1..., baseline/libexec/learn-retirement-ack.py).
Root only, from a root-owned exec copy, and ONLY through an fdev admission that cites the op id and the record's sha256:
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --ack    --op-id <id> --record-sha256 <64 hex>
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --check  --op-id <id> --record-sha256 <64 hex>
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --remove --op-id <id>
What changed from r1: the acknowledgement directory is no longer /etc/fsgg/learn/retirement-acks (lost with the root disk on
every fdev image upgrade). It is <fsgg-learn>/retirement-acks on the persistent bulk bind. The in-VM bind
/usr/local/libexec/fsgg-learn is READ-ONLY, so the writer writes through the persistent source
/srv/bulk/services/usr-local-libexec-fsgg-learn and the operator (r6) reads the mounted view.
--ack    creates the directory (root:root 0755) if absent, then <dir>/<id>.retirement-ack (root:root 0644, content "<sha256>\\n",
         O_CREAT|O_EXCL|O_NOFOLLOW; refuses if it exists). It does NOT read the record: the admitted sha256 is written as given,
         and the operator compares it with the record's bytes at every launch. After writing it reads the file back through the
         mounted view and records whether the view shows the same bytes.
--check  read-only: the acknowledgement is a regular root-owned file, not group/other-writable, on a root-safe chain, in the
         mounted view, and its content equals --record-sha256. Exit 0 only then.
--remove unlinks <dir>/<id>.retirement-ack if it is a regular root-owned file (cleanup after acceptance tests).
Safety: every component from /srv/bulk/services down must be root-owned, not a symlink, not group/other-writable (/srv/bulk
itself is developer-owned, which is why the chain starts at /srv/bulk/services), and the source directory must be the same
inode as the mounted view, so a directory swapped after the mount is refused. This is cooperative against `developer`, who
has full sudo; it detects mistakes and a compromised fsgg-learn-qual, not Root. Output: RESULT JSON on stdout.
Exit 0 done; 5 refused.
"""
import hashlib, json, os, re, stat, sys

SERVICES = '/srv/bulk/services'
SOURCE_ROOT = SERVICES + '/usr-local-libexec-fsgg-learn'
VIEW_ROOT = '/usr/local/libexec/fsgg-learn'
SUBDIR = 'retirement-acks'
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,62}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


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


def guard_source(source_root, view_root, services, owner_uid):
    ok, bad = root_chain_ok(source_root, services, owner_uid)
    if not ok:
        refuse(f'source path not root-safe at {bad}')
    try:
        a, b = os.stat(source_root), os.stat(view_root)
    except OSError:
        refuse('the mounted view is absent')
    if (a.st_dev, a.st_ino) != (b.st_dev, b.st_ino):
        refuse('the persistent source directory is not the mounted view (swapped or not mounted)')


def read_regular(path, cap=4096):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap:
            refuse('not a bounded regular file')
        return f.read()


def ack(opid, sha, source_root, view_root, services, owner_uid):
    guard_source(source_root, view_root, services, owner_uid)
    d = f'{source_root}/{SUBDIR}'
    R = {}
    if not os.path.lexists(d):
        os.mkdir(d, 0o755)
        os.chmod(d, 0o755)
        R['ackDirCreated'] = True
    ok, bad = root_chain_ok(d, services, owner_uid)
    if not ok:
        refuse(f'ack directory not root-safe at {bad}')
    path = f'{d}/{opid}.retirement-ack'
    try:
        fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o644)
    except FileExistsError:
        refuse('acknowledgement already exists')
    with os.fdopen(fd, 'w') as f:
        f.write(sha + '\n')
        f.flush()
        os.fsync(f.fileno())
    if os.geteuid() == 0:
        os.chown(path, owner_uid, owner_uid)
    os.chmod(path, 0o644)
    st = os.lstat(path)
    R.update(path=path, recordSha256=sha, written={'mode': oct(stat.S_IMODE(st.st_mode)), 'uid': st.st_uid, 'gid': st.st_gid})
    try:
        R['viewShowsSameBytes'] = read_regular(f'{view_root}/{SUBDIR}/{opid}.retirement-ack') == (sha + '\n').encode()
    except (OSError, Refuse):
        R['viewShowsSameBytes'] = False
    return R


def check(opid, sha, source_root, view_root, services, owner_uid):
    guard_source(source_root, view_root, services, owner_uid)
    path = f'{view_root}/{SUBDIR}/{opid}.retirement-ack'
    try:
        s = os.lstat(path)
    except OSError:
        refuse('no such acknowledgement in the mounted view')
    if not stat.S_ISREG(s.st_mode) or s.st_uid != owner_uid or (stat.S_IMODE(s.st_mode) & 0o022):
        refuse('acknowledgement is not a root-owned regular file that is closed to group and other writers')
    ok, bad = root_chain_ok(path, view_root, owner_uid)
    if not ok:
        refuse(f'acknowledgement path not root-safe at {bad}')
    if read_regular(path).decode('ascii', 'replace').strip() != sha:
        refuse('acknowledgement content differs from the given record sha256')
    return {'path': path, 'matches': True}


def remove(opid, source_root, view_root, services, owner_uid):
    guard_source(source_root, view_root, services, owner_uid)
    path = f'{source_root}/{SUBDIR}/{opid}.retirement-ack'
    ok, bad = root_chain_ok(f'{source_root}/{SUBDIR}', services, owner_uid) if os.path.lexists(f'{source_root}/{SUBDIR}') else (True, None)
    if not ok:
        refuse(f'ack directory not root-safe at {bad}')
    if not os.path.lexists(path):
        refuse('no such acknowledgement')
    st = os.lstat(path)
    if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid:
        refuse('acknowledgement is not a regular root-owned file')
    os.unlink(path)
    return {'path': path, 'removed': True}


def parse(argv):
    """Return (mode, op id, record sha256 or None) or refuse."""
    mode = argv[0] if argv else None
    opts = {argv[i]: argv[i + 1] for i in range(1, len(argv) - 1, 2)}
    want = {'--ack': 5, '--check': 5, '--remove': 3}
    if mode not in want or len(argv) != want[mode] or '--op-id' not in opts or (mode != '--remove' and '--record-sha256' not in opts):
        refuse('usage: --ack|--check --op-id <id> --record-sha256 <hex> | --remove --op-id <id>')
    if not OPID.match(opts['--op-id']):
        refuse('bad op id')
    sha = opts.get('--record-sha256')
    if mode != '--remove' and not HEX64.match(sha):
        refuse('record sha256 must be 64 lowercase hex')
    return mode, opts['--op-id'], sha


def main(argv):
    R = {'schema': 'home-learn-retirement-ack/2', 'scriptSha256': hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest(),
         'sourceRoot': SOURCE_ROOT, 'errors': []}
    code = 0
    try:
        if os.geteuid() != 0:
            refuse('run as root')
        os.umask(0o022)
        mode, opid, sha = parse(argv)
        R.update(mode=mode, opId=opid)
        if mode == '--remove':
            R.update(remove(opid, SOURCE_ROOT, VIEW_ROOT, SERVICES, 0))
        else:
            R.update((ack if mode == '--ack' else check)(opid, sha, SOURCE_ROOT, VIEW_ROOT, SERVICES, 0))
    except Refuse as e:
        R['errors'].append(str(e))
        code = 5
    except Exception as e:
        R['errors'].append(f'{type(e).__name__}: {str(e)[:200]}')
        code = 5
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': hashlib.sha256(b.encode()).hexdigest(), 'exit': code}))
    return code


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

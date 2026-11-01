#!/usr/bin/python3 -I
"""LEARN #4745 retirement acknowledgement writer r2 (governing spec rev 11 r2 section 7; decision 4516#6352952638;
persistence: Root decision 4647#6533215654 item 3; design: docs/coordination/2026-10-30-learn-finite-executor-pod-design.md
section 10). Refs FS-GG/.github#4647. Supersedes r1 (aadd55b1..., baseline/libexec/learn-retirement-ack.py).
Root only, from a root-owned exec copy, and ONLY through an fdev admission that cites the op id and the record's sha256:
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --ack    --op-id <id> --record-sha256 <64 hex> [--record-file <path>]
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --check  --op-id <id> (--record-sha256 <64 hex> | --record-file <path>)
    /usr/bin/python3 -I <copy>/learn-retirement-ack-r2.py --remove --op-id <id>
--ack REQUIRES the admitted --record-sha256: what is written is the digest Root admitted, never one computed from a file at
write time. --record-file is then a CROSS-CHECK ONLY: it computes the sha256 of that file's bytes (a regular file, at most
16 MiB, opened O_NOFOLLOW) and the two must be equal, so the admitted digest and the record on disk are compared before
anything is written. For the read-only --check either one is enough.
What changed from r1: the acknowledgement directory is no longer /etc/fsgg/learn/retirement-acks (lost with the root disk on
every fdev image upgrade). It is <fsgg-learn>/retirement-acks on the persistent bulk bind. The in-VM bind
/usr/local/libexec/fsgg-learn is READ-ONLY, so the writer writes through the persistent source
/srv/bulk/services/usr-local-libexec-fsgg-learn and the operator (r6) reads the mounted view.
--ack    creates the directory (root:root 0755) if absent, then <dir>/<id>.retirement-ack (root:root 0644, content "<sha256>\\n",
         O_CREAT|O_EXCL|O_NOFOLLOW; refuses if it exists). It does NOT read the record unless --record-file is given. After
         writing it reads the file back through the mounted view; if the view does not show the same bytes it REMOVES the file
         it just wrote and REFUSES (exit 5), so a write the operator cannot see never leaves a half-state.
--check  read-only: the acknowledgement, reached through the mounted view and a chain walked from / (as the operator walks it),
         is a regular root-owned file, not group/other-writable, and its content equals the digest. Exit 0 only then.
--remove unlinks <dir>/<id>.retirement-ack if it is a regular root-owned file (cleanup after acceptance tests).
Safety, and why the writes use pinned directory descriptors: /srv/bulk is developer-owned, so a path check followed by a
path-based write can be raced by renaming a directory in between. Every directory from /srv/bulk/services down is therefore
opened once with O_DIRECTORY|O_NOFOLLOW through openat, fstat-checked on the descriptor (root-owned, not group/other-writable),
compared by device and inode with the mounted view, and every later operation (mkdir, create, unlink) goes through those
descriptors. A directory swapped after the pin is not the directory written. This is cooperative against `developer`, who
has full sudo; it detects mistakes and a swap, not Root. Output: RESULT JSON on stdout. Exit 0 done; 5 refused.
"""
import hashlib, json, os, re, stat, sys

SERVICES = '/srv/bulk/services'
SOURCE_ROOT = SERVICES + '/usr-local-libexec-fsgg-learn'
VIEW_ROOT = '/usr/local/libexec/fsgg-learn'
SUBDIR = 'retirement-acks'
OPID = re.compile(r'^[a-z0-9][a-z0-9-]{2,62}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
DIR_FLAGS = os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW | os.O_CLOEXEC
RECORD_CAP = 16 << 20


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


def _check_dir_fd(fd, owner_uid, label):
    st = os.fstat(fd)
    if not stat.S_ISDIR(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022):
        os.close(fd)
        refuse(f'not root-safe at {label}')
    return st


def open_dir_chain(path, start, owner_uid):
    """Open `path` as a directory descriptor by walking from `start` with O_DIRECTORY|O_NOFOLLOW and openat. Every directory
    opened (start included) is fstat-checked on its descriptor: owner, directory, not group/other-writable. A symlink or a
    missing component refuses. Returns the final descriptor; intermediate descriptors are closed."""
    if start != '/' and not (path == start or path.startswith(start + '/')):
        refuse(f'not root-safe at {path}: outside the chain start {start}')
    try:
        fd = os.open(start, DIR_FLAGS)
    except OSError as e:
        refuse(f'not root-safe at {start}: {e.strerror}')
    _check_dir_fd(fd, owner_uid, start)
    cur = start
    for comp in [p for p in path[len(start):].split('/') if p]:
        cur = cur.rstrip('/') + '/' + comp
        try:
            nfd = os.open(comp, DIR_FLAGS, dir_fd=fd)
        except OSError as e:
            os.close(fd)
            refuse(f'not root-safe at {cur}: {e.strerror}')
        os.close(fd)
        fd = nfd
        _check_dir_fd(fd, owner_uid, cur)
    return fd


def pin_source(source_root, view_root, services, owner_uid):
    """Pinned descriptor of the writable source directory, refused unless it is the same directory as the mounted view."""
    fd = open_dir_chain(source_root, services, owner_uid)
    try:
        v = os.stat(view_root)
    except OSError:
        os.close(fd)
        refuse('the mounted view is absent')
    a = os.fstat(fd)
    if (a.st_dev, a.st_ino) != (v.st_dev, v.st_ino):
        os.close(fd)
        refuse('the persistent source directory is not the mounted view (swapped or not mounted)')
    return fd


def read_regular(path, cap=4096):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
    with os.fdopen(fd, 'rb') as f:
        st = os.fstat(f.fileno())
        if not stat.S_ISREG(st.st_mode) or st.st_size > cap:
            refuse('not a bounded regular file')
        return f.read()


def record_digest(path):
    """sha256 of a record file's bytes: a regular file, bounded, opened O_NOFOLLOW."""
    try:
        b = read_regular(path, RECORD_CAP)
    except OSError as e:
        refuse(f'cannot read the record file: {e.strerror}')
    return hashlib.sha256(b).hexdigest()


def _open_ack_dir(srcfd, owner_uid, create):
    created = False
    if create:
        try:
            os.mkdir(SUBDIR, 0o755, dir_fd=srcfd)
            created = True
        except FileExistsError:
            pass
    try:
        adfd = os.open(SUBDIR, DIR_FLAGS, dir_fd=srcfd)
    except OSError as e:
        refuse(f'ack directory not root-safe: {e.strerror}')
    if created:
        os.fchmod(adfd, 0o755)
        if os.geteuid() == 0:
            os.fchown(adfd, owner_uid, owner_uid)
    try:
        _check_dir_fd(adfd, owner_uid, SUBDIR)
    except Refuse:
        refuse('ack directory not root-safe')
    return adfd, created


def ack(opid, sha, source_root, view_root, services, owner_uid):
    srcfd = pin_source(source_root, view_root, services, owner_uid)
    adfd = None
    try:
        adfd, created = _open_ack_dir(srcfd, owner_uid, create=True)
        R = {'ackDirCreated': True} if created else {}
        name = f'{opid}.retirement-ack'
        try:
            fd = os.open(name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o644, dir_fd=adfd)
        except FileExistsError:
            refuse('acknowledgement already exists')
        with os.fdopen(fd, 'w') as f:
            f.write(sha + '\n')
            f.flush()
            os.fsync(f.fileno())
            if os.geteuid() == 0:
                os.fchown(f.fileno(), owner_uid, owner_uid)
            os.fchmod(f.fileno(), 0o644)
        os.fsync(adfd)
        st = os.stat(name, dir_fd=adfd, follow_symlinks=False)
        path = f'{source_root}/{SUBDIR}/{name}'
        R.update(path=path, recordSha256=sha, written={'mode': oct(stat.S_IMODE(st.st_mode)), 'uid': st.st_uid, 'gid': st.st_gid})
        # The mounted view must show the same bytes; otherwise remove what was just written and refuse (fail closed).
        try:
            same = read_regular(f'{view_root}/{SUBDIR}/{name}') == (sha + '\n').encode()
        except (OSError, Refuse):
            same = False
        if not same:
            os.unlink(name, dir_fd=adfd)
            refuse('the mounted view does not show the bytes just written; the acknowledgement was removed')
        R['viewShowsSameBytes'] = True
        return R
    finally:
        if adfd is not None:
            os.close(adfd)
        os.close(srcfd)


def _walk_file(path, start, owner_uid):
    """Open `path` for reading through a chain of pinned descriptors from `start`; every directory and the file itself are
    fstat-checked on their descriptors. Returns the file's bytes."""
    if start != '/' and not path.startswith(start + '/'):
        refuse(f'acknowledgement path not root-safe at {path}: outside the chain start {start}')
    parent, name = os.path.split(path)
    dfd = open_dir_chain(parent, start, owner_uid)
    try:
        try:
            fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC, dir_fd=dfd)
        except FileNotFoundError:
            refuse('no such acknowledgement in the mounted view')
        except OSError:
            refuse('acknowledgement is not a root-owned regular file that is closed to group and other writers')
        with os.fdopen(fd, 'rb') as f:
            st = os.fstat(f.fileno())
            if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid or (stat.S_IMODE(st.st_mode) & 0o022) or st.st_size > 4096:
                refuse('acknowledgement is not a root-owned regular file that is closed to group and other writers')
            return f.read()
    finally:
        os.close(dfd)


def check(opid, sha, source_root, view_root, services, owner_uid, chain_start='/'):
    """chain_start is '/' in production: the view path is walked from the top, as the operator walks it. Tests pass a temporary root."""
    srcfd = pin_source(source_root, view_root, services, owner_uid)
    os.close(srcfd)
    path = f'{view_root}/{SUBDIR}/{opid}.retirement-ack'
    if _walk_file(path, chain_start, owner_uid).decode('ascii', 'replace').strip() != sha:
        refuse('acknowledgement content differs from the given record sha256')
    return {'path': path, 'matches': True}


def remove(opid, source_root, view_root, services, owner_uid):
    srcfd = pin_source(source_root, view_root, services, owner_uid)
    adfd = None
    try:
        adfd, _ = _open_ack_dir(srcfd, owner_uid, create=False)
        name = f'{opid}.retirement-ack'
        try:
            st = os.stat(name, dir_fd=adfd, follow_symlinks=False)
        except FileNotFoundError:
            refuse('no such acknowledgement')
        if not stat.S_ISREG(st.st_mode) or st.st_uid != owner_uid:
            refuse('acknowledgement is not a regular root-owned file')
        os.unlink(name, dir_fd=adfd)
        return {'path': f'{source_root}/{SUBDIR}/{name}', 'removed': True}
    finally:
        if adfd is not None:
            os.close(adfd)
        os.close(srcfd)


def parse(argv):
    """Return (mode, op id, record sha256 or None) or refuse."""
    mode = argv[0] if argv else None
    opts, i = {}, 1
    while i < len(argv):
        k = argv[i]
        if not k.startswith('--') or i + 1 >= len(argv) or k in opts:
            refuse('usage: --ack --op-id <id> --record-sha256 <hex> [--record-file <path>] | --check --op-id <id> (--record-sha256 <hex> | --record-file <path>) | --remove --op-id <id>')
        opts[k] = argv[i + 1]
        i += 2
    allowed = {'--ack': {'--op-id', '--record-sha256', '--record-file'}, '--check': {'--op-id', '--record-sha256', '--record-file'},
               '--remove': {'--op-id'}}
    if mode not in allowed or '--op-id' not in opts or not set(opts) <= allowed[mode]:
        refuse('usage: --ack --op-id <id> --record-sha256 <hex> [--record-file <path>] | --check --op-id <id> (--record-sha256 <hex> | --record-file <path>) | --remove --op-id <id>')
    if not OPID.match(opts['--op-id']):
        refuse('bad op id')
    if mode == '--remove':
        return mode, opts['--op-id'], None
    if '--record-sha256' not in opts and '--record-file' not in opts:
        refuse('usage: --ack needs --record-sha256; --check needs --record-sha256 or --record-file')
    if mode == '--ack' and '--record-sha256' not in opts:
        refuse('--ack requires the admitted --record-sha256 (--record-file is only a cross-check)')
    sha = opts.get('--record-sha256')
    if sha is not None and not HEX64.match(sha):
        refuse('record sha256 must be 64 lowercase hex')
    if '--record-file' in opts:
        computed = record_digest(opts['--record-file'])
        if sha is not None and sha != computed:
            refuse('the record file does not hash to the given --record-sha256')
        sha = computed
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
        elif mode == '--ack':
            R.update(ack(opid, sha, SOURCE_ROOT, VIEW_ROOT, SERVICES, 0))
        else:
            R.update(check(opid, sha, SOURCE_ROOT, VIEW_ROOT, SERVICES, 0, '/'))
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

#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/ack/learn-retirement-ack-r2.py (Refs FS-GG/.github#4647).

Drives the functions the root CLI calls against temporary trees owned by the running user (the CLI fixes the owner uid to 0
and refuses to run unless root; --check walks the view from / in production and from a temporary root here). stdlib only,
offline, no container. Prints its own assertion count. The cases marked MUTANT were added to kill surviving mutants of the
review of #5009: readback always true, --check chain starting at the view root, and a missing O_NOFOLLOW in read_regular."""
import contextlib, hashlib, importlib.util, io, json, os, stat, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
spec = importlib.util.spec_from_file_location('ack2', os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'ack', 'learn-retirement-ack-r2.py'))
A = importlib.util.module_from_spec(spec)
spec.loader.exec_module(A)
UID = os.getuid()
SHA = '3db3a5f38aec1a01d2d5fbbb471bb272c44fc88c633537523245a67620b7210e'
OP = 'a0-20261022t192443z-f1-pre'
n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def refused(fn, needle, what):
    global n
    n += 1
    try:
        fn()
    except A.Refuse as e:
        if needle not in str(e):
            print(f'FAIL: {what}: refused with "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: not refused')
    sys.exit(1)


def tree(tmp):
    services = os.path.join(tmp, 'services')
    root = os.path.join(services, 'learn')
    os.makedirs(root)
    os.chmod(services, 0o755)
    os.chmod(root, 0o755)
    return services, root


def kw(services, root, view=None):
    return dict(source_root=root, view_root=view or root, services=services, owner_uid=UID)


def ckw(services, root, view=None, start=None):
    return dict(kw(services, root, view), chain_start=start or services)


# ---- argument parsing (the CLI refuses non-root first, so the parser is tested directly)
check(A.parse(['--ack', '--op-id', OP, '--record-sha256', SHA]) == ('--ack', OP, SHA), 'ack arguments parse')
check(A.parse(['--check', '--op-id', OP, '--record-sha256', SHA])[0] == '--check', 'check arguments parse')
check(A.parse(['--remove', '--op-id', OP]) == ('--remove', OP, None), 'remove arguments parse')
for argv, needle, what in (([], 'usage', 'no arguments'), (['--ack', '--op-id', OP], 'usage', 'ack without a digest'),
                           (['--check', '--op-id', OP], 'usage', 'check without a digest'),
                           (['--remove', '--op-id', OP, '--record-sha256', SHA], 'usage', 'remove with a sha'),
                           (['--remove', '--op-id', OP, '--record-file', '/x'], 'usage', 'remove with a record file'),
                           (['--frob', '--op-id', OP], 'usage', 'an unknown mode'),
                           (['--ack', '--op-id', OP, '--record-sha256', SHA, '--x', 'y'], 'usage', 'an extra option'),
                           (['--ack', '--op-id', OP, '--op-id', OP, '--record-sha256', SHA], 'usage', 'a repeated option'),
                           (['--ack', '--op-id', 'Bad Id', '--record-sha256', SHA], 'bad op id', 'a bad op id'),
                           (['--ack', '--op-id', 'ab', '--record-sha256', SHA], 'bad op id', 'a too-short op id'),
                           (['--ack', '--op-id', OP, '--record-sha256', SHA.upper()], '64 lowercase hex', 'an uppercase sha'),
                           (['--ack', '--op-id', OP, '--record-sha256', 'zz'], '64 lowercase hex', 'a short sha')):
    refused(lambda a=argv: A.parse(a), needle, f'{what} is refused')
if os.geteuid() != 0:
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code = A.main(['--ack', '--op-id', OP, '--record-sha256', SHA])
    check(code == 5 and 'run as root' in out.getvalue(), 'the CLI refuses to run unless root')

# ---- --record-file: the digest is computed from the record and compared with the admitted one
with tempfile.TemporaryDirectory() as tmp:
    rec = os.path.join(tmp, 'record.json')
    open(rec, 'wb').write(b'{"schema":"learn-op-retirement-failure/1"}\n')
    want = hashlib.sha256(open(rec, 'rb').read()).hexdigest()
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-file', rec]), 'requires the admitted --record-sha256', '--ack with only a record file is refused: the digest must be the admitted one')
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-file', os.path.join(tmp, 'absent')]), 'requires the admitted --record-sha256', '--ack with only a missing record file is refused for the missing digest first')
    check(A.parse(['--ack', '--op-id', OP, '--record-sha256', want, '--record-file', rec]) == ('--ack', OP, want), '--ack with --record-sha256 and a matching --record-file passes (cross-check)')
    check(A.parse(['--ack', '--op-id', OP, '--record-sha256', SHA]) == ('--ack', OP, SHA), '--ack with --record-sha256 alone passes and writes the admitted digest as given')
    check(A.parse(['--check', '--op-id', OP, '--record-file', rec]) == ('--check', OP, want), '--check with only a record file still works (read only)')
    check(A.parse(['--check', '--op-id', OP, '--record-file', rec, '--record-sha256', want]) == ('--check', OP, want), '--check with the matching --record-sha256 passes')
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-file', rec, '--record-sha256', SHA]), 'does not hash to the given', 'a record file that does not hash to the admitted digest is refused')
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-sha256', SHA, '--record-file', os.path.join(tmp, 'absent')]), 'cannot read the record file', 'a missing record file is refused')
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-sha256', SHA, '--record-file', tmp]), 'cannot read the record file', 'a directory as record file is refused')
    link = os.path.join(tmp, 'link.json')
    os.symlink(rec, link)
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-sha256', want, '--record-file', link]), 'cannot read the record file', 'a symlinked record file is refused (O_NOFOLLOW)')
    big = os.path.join(tmp, 'big.json')
    with open(big, 'wb') as f:
        f.truncate(A.RECORD_CAP + 1)
    refused(lambda: A.parse(['--ack', '--op-id', OP, '--record-sha256', want, '--record-file', big]), 'not a bounded regular file', 'a record file over 16 MiB is refused')

# ---- MUTANT: read_regular must not follow a symlink
with tempfile.TemporaryDirectory() as tmp:
    real = os.path.join(tmp, 'real')
    open(real, 'w').write('x')
    os.symlink(real, os.path.join(tmp, 'l'))
    try:
        A.read_regular(os.path.join(tmp, 'l'))
        check(False, 'read_regular refuses a symlink')
    except OSError:
        check(True, 'read_regular refuses a symlink (O_NOFOLLOW)')
    check(A.read_regular(real) == b'x', 'read_regular reads a regular file')

# ---- ack: positive path
with tempfile.TemporaryDirectory() as tmp:
    services, root = tree(tmp)
    r = A.ack(OP, SHA, **kw(services, root))
    path = os.path.join(root, 'retirement-acks', OP + '.retirement-ack')
    check(r['path'] == path and r.get('ackDirCreated') is True, 'the ack directory is created on first use')
    check(open(path, 'rb').read() == (SHA + '\n').encode(), 'the content is exactly the sha256 and a newline')
    check(stat.S_IMODE(os.lstat(path).st_mode) == 0o644 and stat.S_IMODE(os.lstat(os.path.dirname(path)).st_mode) == 0o755, 'the file is 0644 and the directory 0755')
    check(r['written']['uid'] == UID and r['viewShowsSameBytes'] is True, 'the mounted view shows the same bytes')
    refused(lambda: A.ack(OP, SHA, **kw(services, root)), 'already exists', 'a second ack for the same op is refused')
    r2 = A.ack('c7-ackt-acked', '4c190280' + '0' * 56, **kw(services, root))
    check('ackDirCreated' not in r2, 'a second ack reuses the directory')

    # ---- check
    check(A.check(OP, SHA, **ckw(services, root)) == {'path': path, 'matches': True}, 'check passes for the right sha256')
    refused(lambda: A.check(OP, '0' * 64, **ckw(services, root)), 'content differs', 'check refuses another sha256')
    refused(lambda: A.check('no-such-op', SHA, **ckw(services, root)), 'no such acknowledgement', 'check refuses a missing ack')
    os.chmod(path, 0o666)
    refused(lambda: A.check(OP, SHA, **ckw(services, root)), 'closed to group and other writers', 'check refuses a world-writable ack')
    os.chmod(path, 0o644)
    os.unlink(path)
    os.symlink(os.path.join(root, 'retirement-acks', 'c7-ackt-acked.retirement-ack'), path)
    refused(lambda: A.check(OP, SHA, **ckw(services, root)), 'root-owned regular file', 'check refuses a symlinked ack')
    os.unlink(path)
    open(path, 'w').write(SHA + '\n')
    os.chmod(path, 0o644)
    k = ckw(services, root)
    k['owner_uid'] = UID + 1
    refused(lambda: A.check(OP, SHA, **k), 'not root-safe', 'check refuses a chain owned by another uid')
    real_fstat = os.fstat

    def fstat_foreign_file(fd):
        st = real_fstat(fd)
        if stat.S_ISREG(st.st_mode):
            vals = list(st)
            vals[4] = st.st_uid + 1                        # the file belongs to someone else; the directories do not
            return os.stat_result(vals)
        return st
    try:
        os.fstat = fstat_foreign_file
        refused(lambda: A.check(OP, SHA, **ckw(services, root)), 'root-owned regular file', 'MUTANT: check refuses an ack file owned by another uid')
    finally:
        os.fstat = real_fstat

    # ---- remove: positive and negative
    check(A.remove(OP, **kw(services, root)) == {'path': path, 'removed': True}, 'remove deletes a regular root-owned ack')
    check(not os.path.lexists(path), 'the removed ack is gone')
    check(A.ack(OP, SHA, **kw(services, root))['viewShowsSameBytes'] is True, 'after a remove the ack can be written again')
    refused(lambda: A.remove('no-such-op', **kw(services, root)), 'no such acknowledgement', 'remove refuses a missing ack')
    os.unlink(path)
    os.symlink('/etc/hostname', path)
    refused(lambda: A.remove(OP, **kw(services, root)), 'regular root-owned file', 'remove refuses a symlink')
    check(os.path.islink(path), 'the refused remove left the symlink alone')
    os.unlink(path)
    open(path, 'w').write('x')
    k = kw(services, root)
    k['owner_uid'] = UID + 1
    refused(lambda: A.remove(OP, **k), 'not root-safe', 'remove refuses under a chain owned by another uid')
    os.chmod(os.path.join(root, 'retirement-acks'), 0o777)
    refused(lambda: A.remove(OP, **kw(services, root)), 'not root-safe', 'remove refuses a world-writable ack directory')
    os.chmod(os.path.join(root, 'retirement-acks'), 0o755)

# ---- MUTANT: the readback must refuse, and remove what it wrote, when the mounted view does not show the same bytes
with tempfile.TemporaryDirectory() as tmp:
    services, root = tree(tmp)
    real_read = A.read_regular
    ackfile = os.path.join(root, 'retirement-acks', OP + '.retirement-ack')
    try:
        A.read_regular = lambda p, cap=4096: b'some other bytes\n'
        refused(lambda: A.ack(OP, SHA, **kw(services, root)), 'does not show the bytes just written', 'a readback that differs refuses the ack')
        check(not os.path.lexists(ackfile), 'a refused readback removes the acknowledgement it wrote (no half-state)')
        A.read_regular = lambda p, cap=4096: (_ for _ in ()).throw(OSError('view unreadable'))
        refused(lambda: A.ack(OP, SHA, **kw(services, root)), 'does not show the bytes just written', 'an unreadable view refuses the ack')
        check(not os.path.lexists(ackfile), 'an unreadable view also leaves no acknowledgement')
    finally:
        A.read_regular = real_read
    check(A.ack(OP, SHA, **kw(services, root))['viewShowsSameBytes'] is True, 'with a faithful view the same ack succeeds (the earlier refusals left nothing behind)')
    with contextlib.redirect_stdout(io.StringIO()):
        pass

# ---- MUTANT: --check walks from the chain start (/ in production), so an unsafe ANCESTOR of the view is seen
with tempfile.TemporaryDirectory() as tmp:
    top = os.path.join(tmp, 'top')
    anc = os.path.join(top, 'anc')
    view = os.path.join(anc, 'learn')
    os.makedirs(view)
    for d in (top, anc, view):
        os.chmod(d, 0o755)
    A.ack(OP, SHA, source_root=view, view_root=view, services=view, owner_uid=UID)       # services == the source: only its own chain is guarded
    base = dict(source_root=view, view_root=view, services=view, owner_uid=UID)
    check(A.check(OP, SHA, chain_start=top, **base)['matches'] is True, 'check passes when every ancestor of the view is safe')
    os.chmod(anc, 0o777)
    refused(lambda: A.check(OP, SHA, chain_start=top, **base), f'not root-safe at {anc}', 'check refuses a world-writable ancestor of the view')
    os.chmod(anc, 0o775)
    refused(lambda: A.check(OP, SHA, chain_start=top, **base), f'not root-safe at {anc}', 'check refuses a group-writable ancestor of the view')
    os.chmod(anc, 0o755)
    os.chmod(top, 0o777)
    refused(lambda: A.check(OP, SHA, chain_start=top, **base), f'not root-safe at {top}', 'check refuses an unsafe chain start')
    os.chmod(top, 0o755)
    refused(lambda: A.check(OP, SHA, chain_start=os.path.join(tmp, 'elsewhere'), **base), 'outside the chain start', 'check refuses a view outside the chain start')
    anc2 = os.path.join(tmp, 'moved')
    os.rename(anc, anc2)
    os.symlink(anc2, anc)
    refused(lambda: A.check(OP, SHA, chain_start=top, **base), 'not root-safe', 'check refuses a symlinked ancestor of the view')

# ---- write-path guards on ack
with tempfile.TemporaryDirectory() as tmp:
    services, root = tree(tmp)
    os.chmod(services, 0o777)
    refused(lambda: A.ack(OP, SHA, **kw(services, root)), 'not root-safe', 'a world-writable services directory is refused')
    os.chmod(services, 0o755)
    os.chmod(root, 0o775)
    refused(lambda: A.ack(OP, SHA, **kw(services, root)), 'not root-safe', 'a group-writable source directory is refused')
    os.chmod(root, 0o755)
    k = kw(services, root)
    k['owner_uid'] = UID + 1
    refused(lambda: A.ack(OP, SHA, **k), 'not root-safe', 'a directory owned by another uid is refused')
    other = os.path.join(tmp, 'other')
    os.mkdir(other)
    refused(lambda: A.ack(OP, SHA, **kw(services, root, view=other)), 'not the mounted view', 'a source directory that is not the mounted view is refused')
    refused(lambda: A.ack(OP, SHA, **kw(services, root, view=os.path.join(tmp, 'absent'))), 'mounted view is absent', 'an absent view is refused')
    services2 = os.path.join(tmp, 'services2')
    os.mkdir(services2)
    os.chmod(services2, 0o755)
    os.symlink(root, os.path.join(services2, 'learn'))
    refused(lambda: A.ack(OP, SHA, source_root=os.path.join(services2, 'learn'), view_root=root, services=services2, owner_uid=UID),
            'not root-safe', 'a symlinked source directory is refused')
    elsewhere = os.path.join(tmp, 'elsewhere')
    os.mkdir(elsewhere)
    os.chmod(elsewhere, 0o755)
    refused(lambda: A.ack(OP, SHA, source_root=root, view_root=root, services=elsewhere, owner_uid=UID),
            'outside the chain start', 'a source directory outside an existing, safe services chain is refused')
    check(not os.path.lexists(os.path.join(root, 'retirement-acks')), 'every refused write left no ack directory behind')
    A.ack(OP, SHA, **kw(services, root))
    ad = os.path.join(root, 'retirement-acks')
    os.chmod(ad, 0o777)
    refused(lambda: A.ack('second-op', SHA, **kw(services, root)), 'ack directory not root-safe', 'a world-writable ack directory is refused')
    os.chmod(ad, 0o755)
    moved = os.path.join(tmp, 'moved')
    os.rename(ad, moved)
    os.symlink(moved, ad)
    refused(lambda: A.ack('second-op', SHA, **kw(services, root)), 'ack directory not root-safe', 'an ack directory swapped for a symlink is refused')
    check(not os.path.lexists(os.path.join(moved, 'second-op.retirement-ack')), 'nothing was written through the swapped directory')

# ---- the swap window: the writes go through the pinned descriptor, not through the path
with tempfile.TemporaryDirectory() as tmp:
    services, root = tree(tmp)
    view = os.path.join(services, 'view')
    os.symlink(root, view)                                    # stands for the bind mount: it keeps resolving to the ORIGINAL directory
    real_open = A.open_dir_chain

    def swap_after_pin(path, start, owner_uid):
        fd = real_open(path, start, owner_uid)                # the chain check passes and the descriptor is pinned...
        os.rename(root, root + '.old')                        # ...then the developer swaps the directory at that path
        os.mkdir(root)
        os.chmod(root, 0o755)
        os.unlink(view)
        os.symlink(root + '.old', view)
        return fd
    try:
        A.open_dir_chain = swap_after_pin
        r = A.ack(OP, SHA, source_root=root, view_root=view, services=services, owner_uid=UID)
    finally:
        A.open_dir_chain = real_open
    orig = os.path.join(root + '.old', 'retirement-acks', OP + '.retirement-ack')
    check(os.path.isfile(orig) and r['viewShowsSameBytes'] is True, 'the ack was written into the pinned (original) directory')
    check(not os.path.lexists(os.path.join(root, 'retirement-acks')), 'nothing was written into the directory swapped in at the path')
    # a swap BEFORE the pin is caught by the inode comparison with the view
    services, root = tree(tempfile.mkdtemp(dir=tmp))
    view = os.path.join(services, 'view')
    os.symlink(root, view)
    os.rename(root, root + '.old')
    os.mkdir(root)
    os.chmod(root, 0o755)
    os.unlink(view)
    os.symlink(root + '.old', view)                           # the mount keeps the original inode
    refused(lambda: A.ack(OP, SHA, source_root=root, view_root=view, services=services, owner_uid=UID), 'not the mounted view', 'a swap before the pin is refused by the inode comparison')
    # remove goes through the pinned descriptor too
    services, root = tree(tempfile.mkdtemp(dir=tmp))
    A.ack(OP, SHA, **kw(services, root))
    real_open = A.open_dir_chain

    def swap_for_remove(path, start, owner_uid):
        fd = real_open(path, start, owner_uid)
        os.rename(root, root + '.old')
        os.mkdir(root)
        os.chmod(root, 0o755)
        return fd
    try:
        A.open_dir_chain = swap_for_remove
        # the mounted view in this case is the original directory's name, now taken by the new one, so the pin must stay with the original
        A.remove(OP, source_root=root, view_root=root + '.old', services=services, owner_uid=UID)
    finally:
        A.open_dir_chain = real_open
    check(not os.path.lexists(os.path.join(root + '.old', 'retirement-acks', OP + '.retirement-ack')), 'remove acted on the pinned (original) directory')

# ---- r2 supersedes the imported r1 baseline; the content format is unchanged
base = json.load(open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', 'BASELINE.json')))
r1 = base['files']['/usr/local/libexec/fsgg-learn/learn-retirement-ack.py']
check(r1['sha256'].startswith('aadd55b1') and r1['imported'] is True, 'the imported r1 baseline is the admitted aadd55b1 writer')
r1_src = open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', r1['repoPath'])).read()
r2_src = open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'ack', 'learn-retirement-ack-r2.py')).read()
check("f.write(h + '\\n')" in r1_src and "f.write(sha + '\\n')" in r2_src, 'r1 and r2 both write exactly "<sha256>\\n"')
check("O_CREAT | os.O_EXCL | os.O_NOFOLLOW" in r1_src and "O_CREAT | os.O_EXCL | os.O_NOFOLLOW" in r2_src, 'r1 and r2 both create with O_EXCL and O_NOFOLLOW')
check("ACK_DIR = '/etc/fsgg/learn/retirement-acks'" in r1_src and '/etc/fsgg' not in r2_src.split('"""', 2)[2], 'r2 no longer uses /etc/fsgg in its code')
check('dir_fd=' in r2_src and 'O_DIRECTORY' in r2_src and 'os.path.lexists' not in r2_src, 'r2 writes through pinned directory descriptors, not through paths')

print(f'learn-ack-writer: {n} assertions passed')

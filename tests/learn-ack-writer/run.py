#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/ack/learn-retirement-ack-r2.py (Refs FS-GG/.github#4647).

Drives the functions the root CLI calls against temporary trees owned by the running user (the CLI fixes the owner uid to 0
and refuses to run unless root). stdlib only, offline, no container. Prints its own assertion count."""
import contextlib, hashlib, importlib.util, io, os, stat, sys, tempfile

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


# ---- argument parsing (the CLI refuses non-root first, so the parser is tested directly)
check(A.parse(['--ack', '--op-id', OP, '--record-sha256', SHA]) == ('--ack', OP, SHA), 'ack arguments parse')
check(A.parse(['--check', '--op-id', OP, '--record-sha256', SHA])[0] == '--check', 'check arguments parse')
check(A.parse(['--remove', '--op-id', OP]) == ('--remove', OP, None), 'remove arguments parse')
for argv, needle, what in (([], 'usage', 'no arguments'), (['--ack', '--op-id', OP], 'usage', 'ack without a sha'),
                           (['--remove', '--op-id', OP, '--record-sha256', SHA], 'usage', 'remove with a sha'),
                           (['--frob', '--op-id', OP], 'usage', 'an unknown mode'),
                           (['--ack', '--op-id', 'Bad Id', '--record-sha256', SHA], 'bad op id', 'a bad op id'),
                           (['--ack', '--op-id', 'ab', '--record-sha256', SHA], 'bad op id', 'a too-short op id'),
                           (['--ack', '--op-id', OP, '--record-sha256', SHA.upper()], '64 lowercase hex', 'an uppercase sha'),
                           (['--ack', '--op-id', OP, '--record-sha256', 'zz'], '64 lowercase hex', 'a short sha')):
    refused(lambda a=argv: A.parse(a), needle, f'{what} is refused')
if os.geteuid() != 0:
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code = A.main(['--ack', '--op-id', OP, '--record-sha256', SHA])
    check(code == 5 and 'run as root' in out.getvalue(), 'the CLI refuses to run unless root')

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
    check(A.check(OP, SHA, **kw(services, root)) == {'path': path, 'matches': True}, 'check passes for the right sha256')
    refused(lambda: A.check(OP, '0' * 64, **kw(services, root)), 'content differs', 'check refuses another sha256')
    refused(lambda: A.check('no-such-op', SHA, **kw(services, root)), 'no such acknowledgement', 'check refuses a missing ack')
    os.chmod(path, 0o666)
    refused(lambda: A.check(OP, SHA, **kw(services, root)), 'closed to group and other writers', 'check refuses a world-writable ack')
    os.chmod(path, 0o644)
    os.unlink(path)
    os.symlink(os.path.join(root, 'retirement-acks', 'c7-ackt-acked.retirement-ack'), path)
    refused(lambda: A.check(OP, SHA, **kw(services, root)), 'root-owned regular file', 'check refuses a symlinked ack')
    os.unlink(path)
    open(path, 'w').write(SHA + '\n')
    os.chmod(path, 0o644)
    k = kw(services, root)
    k['owner_uid'] = UID + 1
    refused(lambda: A.check(OP, SHA, **k), 'not root-safe', 'check refuses a chain owned by another uid')

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
    refused(lambda: A.ack(OP, SHA, source_root=root, view_root=root, services=os.path.join(tmp, 'elsewhere'), owner_uid=UID),
            'not root-safe', 'a source directory outside the services chain is refused')
    check(not os.path.lexists(os.path.join(root, 'retirement-acks')), 'every refused write left no ack directory behind')
    # an ack directory swapped for a symlink or made world-writable after it was created
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

# ---- r2 supersedes the imported r1 baseline; the content format is unchanged
import json
base = json.load(open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', 'BASELINE.json')))
r1 = base['files']['/usr/local/libexec/fsgg-learn/learn-retirement-ack.py']
check(r1['sha256'].startswith('aadd55b1') and r1['imported'] is True, 'the imported r1 baseline is the admitted aadd55b1 writer')
r1_src = open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', r1['repoPath'])).read()
r2_src = open(os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'ack', 'learn-retirement-ack-r2.py')).read()
check("f.write(h + '\\n')" in r1_src and "f.write(sha + '\\n')" in r2_src, 'r1 and r2 both write exactly "<sha256>\\n"')
check("O_CREAT | os.O_EXCL | os.O_NOFOLLOW" in r1_src and "O_CREAT | os.O_EXCL | os.O_NOFOLLOW" in r2_src, 'r1 and r2 both create with O_EXCL and O_NOFOLLOW')
check("ACK_DIR = '/etc/fsgg/learn/retirement-acks'" in r1_src and '/etc/fsgg' not in r2_src.split('"""', 2)[2], 'r2 no longer uses /etc/fsgg in its code')

print(f'learn-ack-writer: {n} assertions passed')

#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/seal/learn_seal_claim.py (Refs FS-GG/.github#4647).

Drives the library functions the root CLI calls, against temporary trees owned by the running user (the tool takes the owner
uid as a parameter; the CLI fixes it to 0 and refuses write commands unless root). stdlib only, offline, no container.
Sections F1..F8 are the eight findings of the review of #5000; each has cases that failed against the previous head.
Prints its own assertion count."""
import contextlib, copy, importlib.util, io, json, os, shutil, stat, sys, tempfile, time
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(os.path.dirname(os.path.dirname(HERE)), 'deployment', 'learn-finite-profile', 'seal', 'learn_seal_claim.py')
spec = importlib.util.spec_from_file_location('lsc', TOOL)
L = importlib.util.module_from_spec(spec)
spec.loader.exec_module(L)
UID = os.getuid()
SUBS = list(L.ALLOWED_SUBTREES)
n = 0
H = lambda c: c * 64
URL = 'https://github.com/FS-GG/.github/issues/4647#issuecomment-6534175822'
INACTIVE = {'measured': True, 'states': {u: 'inactive' for u in L.DRAIN_UNITS}}


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
    except L.Refuse as e:
        if needle not in str(e):
            print(f'FAIL: {what}: refused with "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: not refused')
    sys.exit(1)


class Env:
    """services/learn stands for /srv/bulk/services/usr-local-libexec-fsgg-learn; the view is the same directory."""
    def __init__(self, tmp):
        self.tmp = tmp
        self.services = os.path.join(tmp, 'services')
        self.root = os.path.join(self.services, 'learn')
        os.makedirs(self.root)
        os.chmod(self.services, 0o755)
        os.chmod(self.root, 0o755)
        self.base = os.path.join(tmp, 'telemetry-spool')
        for s, files in ((SUBS[0], {'outcomes/a.json': b'{"a":1}\n', 'drain-cursor': b'cur'}),
                         (SUBS[1], {'one.ready': b'ready-one', 'x/y.json': b'y' * 300})):
            for rel, data in files.items():
                p = os.path.join(self.base, s, rel)
                os.makedirs(os.path.dirname(p), exist_ok=True)
                open(p, 'wb').write(data)
                os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
        self.markers = os.path.join(tmp, 'learn-input')
        os.makedirs(self.markers)

    def kw(self):
        return dict(source_root=self.root, view_root=self.root, services=self.services, owner_uid=UID)

    def seal(self, snap, m1=None):
        m1 = m1 or L.take_manifest(self.base, SUBS)
        m2 = copy.deepcopy(m1)
        m2['takenUtc'] = (L.parse_utc(m1['takenUtc']) + timedelta(seconds=301)).isoformat()
        now = L.parse_utc(m1['takenUtc']) + timedelta(seconds=400)
        q = L.quiesce_verdict(m1, m2, [f'{self.base}/{s}' for s in SUBS], INACTIVE, now=now)
        r = L.seal(self.base, m1, q, snap, measure=INACTIVE, now=now, **self.kw())
        return m1, q, r


def precheck(op, **over):
    r = {'schema': L.OPERATOR_RESULT_SCHEMA, 'opId': op, 'mode': '--precheck-only', 'exitCode': 0, 'errors': [], 'gates': {'inputMounted': False}}
    r.update(over)
    return r


def run_result(op, **over):
    r = {'schema': L.OPERATOR_RESULT_SCHEMA, 'opId': op, 'mode': '--run', 'exitCode': 5, 'gates': {'inputMounted': False},
         'runNonceSha256': H('7'), 'operatorSha256': H('2'),
         'containers': [{'name': 'pg', 'mounts': [{'destination': '/pgdata'}]}, {'name': 'feed', 'mounts': [{'destination': '/work'}]}]}
    r.update(over)
    return r


def fin(op, **over):
    r = {'schema': 'learn-op-success/1', 'opId': op, 'inputRead': False, 'containersStartedWithInput': [],
         'runNonceSha256': H('7'), 'finalizerSha256': H('3')}
    r.update(over)
    return r


def admit(e, op, ident):
    return L.admit(op, H('1'), ident, H('2'), H('3'), [H('4')], 300, **e.kw())


def claim(e, ident, op, snap, sealed, **kw):
    return L.claim(ident, op, snap, sealed, precheck(op), H('b'), **{**e.kw(), **kw})


now = datetime.now(timezone.utc)
# The vectors below are written against ONE frozen `now`. Anything the tool timestamps with its own clock and a vector then checks against `now + 5 s` would depend on how
# long the run took. LEARN_SEAL_TEST_DELAY_S inserts an artificial delay right here (the CI workflow runs the suite once with it) so a vector that reads the wall clock fails.
time.sleep(float(os.environ.get('LEARN_SEAL_TEST_DELAY_S', '0')))

# ============================================================ F4: the subtree set is pinned
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1 = L.take_manifest(e.base, SUBS)
    check(m1['subtrees'] == sorted(SUBS) and len(m1['files']) == 4, 'manifest lists both pinned subtrees and 4 files')
    ident = L.source_identity(m1)
    check(len(ident) == 64 and ident == L.source_identity(L.take_manifest(e.base, list(reversed(SUBS)))), 'identity is deterministic and order independent')
    for bad, what in (([SUBS[0]], 'one subtree'), ([SUBS[1]], 'the other single subtree'), (SUBS + ['passive-extra/spool'], 'an extra subtree'),
                      ([SUBS[0], 'passive-other/spool'], 'a swapped subtree'), ([], 'no subtree'), (SUBS + SUBS[:1], 'a repeated subtree'),
                      (['passive-home-canonical-v1', SUBS[1]], 'the parent directory instead of spool/'), (['../x', SUBS[1]], 'a .. subtree')):
        refused(lambda b=bad: L.take_manifest(e.base, b), 'exactly the pinned pair', f'{what} is refused (no fresh identity)')
    other = os.path.join(tmp, 'other-spool')
    shutil.copytree(e.base, other)
    check(L.source_identity(L.take_manifest(other, SUBS)) != ident, 'the same files under another base give another identity')
    mb = copy.deepcopy(m1)
    mb['subtrees'] = [SUBS[0]]
    refused(lambda: L.validate_manifest(mb), 'not the pinned pair', 'a manifest with a smaller subtree set is refused')
    refused(lambda: L.seal(other, m1, {'ok': True, 'm1Sha256': L.sha(L.canon(m1))}, 'snap-b', measure=INACTIVE, now=now + timedelta(days=1), **e.kw()),
            'another base directory', 'sealing from a base other than the manifest\'s is refused')
    # identity moves with content and mtime, and returns with them
    p = os.path.join(e.base, SUBS[1], 'one.ready')
    open(p, 'wb').write(b'ready-ONE')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, SUBS)) != ident, 'identity changes with content')
    open(p, 'wb').write(b'ready-one')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_999_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, SUBS)) != ident, 'identity changes with mtime')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, SUBS)) == ident, 'identity is restored with the original bytes and mtime')
    # unsafe entries in the pinned subtrees
    os.symlink('one.ready', os.path.join(e.base, SUBS[1], 'link'))
    refused(lambda: L.take_manifest(e.base, SUBS), 'symlink, fifo, device, socket', 'a symlink in a subtree is refused')
    os.unlink(os.path.join(e.base, SUBS[1], 'link'))
    os.mkfifo(os.path.join(e.base, SUBS[1], 'fifo'))
    refused(lambda: L.take_manifest(e.base, SUBS), 'symlink, fifo, device, socket', 'a fifo in a subtree is refused')
    os.unlink(os.path.join(e.base, SUBS[1], 'fifo'))
    for i in range(L.MAX_FILES):
        open(os.path.join(e.base, SUBS[0], f'f{i}'), 'w').write('x')
    refused(lambda: L.take_manifest(e.base, SUBS), 'file or byte bound', 'too many files is refused')
    for i in range(L.MAX_FILES):
        os.unlink(os.path.join(e.base, SUBS[0], f'f{i}'))

# ============================================================ F6: every M1 path is validated before any copy, and failures clean up
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1 = L.take_manifest(e.base, SUBS)
    good = m1['files'][0]
    outside = os.path.join(tmp, 'outside.txt')
    open(outside, 'w').write('SECRET')
    evil_paths = ['../outside.txt', '/etc/passwd', SUBS[0] + '/../../outside.txt', SUBS[0] + '/../' + SUBS[1] + '/one.ready', SUBS[0] + '//x',
                  './' + SUBS[0] + '/x', SUBS[0] + '/./x', SUBS[0] + '/', 'passive-other/spool/x', SUBS[0], SUBS[0] + '\\x',
                  SUBS[0] + '/x\x00y', '', 'spool/x']
    for ep in evil_paths:
        evil = copy.deepcopy(m1)
        evil['files'].append(dict(good, path=ep, sha256=L.sha_file(outside), size=6))
        refused(lambda ev=evil: L.validate_manifest(ev), 'not a safe relative path', f'validate_manifest refuses path {ep!r}')
    evil = copy.deepcopy(m1)
    evil['files'].append(dict(good, path='../outside.txt', sha256=L.sha_file(outside), size=6))
    evil2 = copy.deepcopy(evil)
    evil2['takenUtc'] = (L.parse_utc(evil['takenUtc']) + timedelta(seconds=400)).isoformat()
    refused(lambda: L.quiesce_verdict(evil, evil2, [e.base], INACTIVE), 'not a safe relative path', 'a doctored manifest cannot get a quiesce proof')
    doctored_q = {'ok': True, 'm1Sha256': L.sha(L.canon(evil))}          # a proof forged to match the doctored manifest
    refused(lambda: L.seal(e.base, evil, doctored_q, 'snap-evil', measure=INACTIVE, now=now + timedelta(days=1), **e.kw()),
            'not a safe relative path', 'seal refuses a ../ path before copying')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-evil')) and not os.path.lexists(os.path.join(e.root, 'inputs', 'outside.txt')),
          'the refused seal created no snapshot and nothing outside it')
    dup = copy.deepcopy(m1)
    dup['files'].append(dict(good))
    refused(lambda: L.validate_manifest(dup), 'duplicate manifest path', 'a duplicate path is refused')
    for field, val, needle in (('sha256', 'zz', 'malformed'), ('size', -1, 'malformed'), ('type', 'dir', 'malformed'), ('mtime_ns', -5, 'mtime is malformed')):
        bad = copy.deepcopy(m1)
        bad['files'][0][field] = val
        refused(lambda b=bad: L.validate_manifest(b), needle, f'a malformed {field} is refused')
    bad = copy.deepcopy(m1)
    bad['files'] = []
    refused(lambda: L.validate_manifest(bad), 'empty or over the bound', 'an empty file list is refused')
    bad = copy.deepcopy(m1)
    bad['files'][0]['size'] = L.MAX_BYTES + 1
    refused(lambda: L.validate_manifest(bad), 'over the bound', 'a total size over the bound is refused')
    # partial copies are removed completely, whatever the failure
    m1 = L.take_manifest(e.base, SUBS)
    m2 = copy.deepcopy(m1)
    m2['takenUtc'] = (L.parse_utc(m1['takenUtc']) + timedelta(seconds=301)).isoformat()
    t_now = L.parse_utc(m1['takenUtc']) + timedelta(seconds=400)
    q = L.quiesce_verdict(m1, m2, [f'{e.base}/{s}' for s in SUBS], INACTIVE, now=t_now)
    calls = []

    def fail_second(src, dst):
        calls.append(dst)
        if len(calls) == 2:
            raise OSError('disk full')
        shutil.copyfile(src, dst)
    refused_oserr = False
    try:
        L.seal(e.base, m1, q, 'snap-part', copy=fail_second, measure=INACTIVE, now=t_now, **e.kw())
    except OSError:
        refused_oserr = True
    check(refused_oserr and len(calls) == 2, 'a copy failure after the first file propagates')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-part')) and not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-part.manifest.json')),
          'a copy failure leaves no snapshot directory and no manifest')

    def interrupt(src, dst):
        shutil.copyfile(src, dst)
        raise KeyboardInterrupt()
    try:
        L.seal(e.base, m1, q, 'snap-int', copy=interrupt, measure=INACTIVE, now=t_now, **e.kw())
    except KeyboardInterrupt:
        pass
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-int')), 'an interrupt after a partial copy leaves no snapshot directory')

    def hashmismatch(src, dst):
        open(dst, 'wb').write(b'corrupt')
    refused(lambda: L.seal(e.base, m1, q, 'snap-bad', copy=hashmismatch, measure=INACTIVE, now=t_now, **e.kw()), 'does not match its source hash',
            'a copy that does not hash to the manifest is refused')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-bad')), 'a hash mismatch leaves no snapshot directory')

    def mutate_copy(src, dst):
        shutil.copyfile(src, dst)
        open(src, 'ab').write(b'!')                                    # the source changes during the copy
    refused(lambda: L.seal(e.base, m1, q, 'snap-mut', copy=mutate_copy, measure=INACTIVE, now=t_now, **e.kw()), 'changed during the copy',
            'a source that changes during the copy is refused')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-mut')), 'a source change leaves no snapshot directory')

# ============================================================ F5: quiesce measures the drain units and re-checks at seal time
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1 = L.take_manifest(e.base, SUBS)
    m2 = copy.deepcopy(m1)
    m2['takenUtc'] = (L.parse_utc(m1['takenUtc']) + timedelta(seconds=301)).isoformat()
    dirs = [f'{e.base}/{s}' for s in SUBS]
    t_now = L.parse_utc(m2['takenUtc']) + timedelta(seconds=5)
    v = L.quiesce_verdict(m1, m2, dirs, INACTIVE, now=t_now)
    check(v['ok'] and v['timerEvidence'] == 'measured' and v['sourceIdentity'] == L.source_identity(m1), 'a stable source with measured inactive units is quiescent')
    check('self-reported' in v['gapBasis'] and v['gapSeconds'] == 301, 'the verdict says the gap is self-reported')
    for unit in L.DRAIN_UNITS:
        for state in ('active', 'activating', 'reloading', 'deactivating'):
            ms = {'measured': True, 'states': dict(INACTIVE['states'], **{unit: state})}
            check(not L.quiesce_verdict(m1, m2, dirs, ms, now=t_now)['ok'], f'{unit} {state} is refused')
    unm = {'measured': False, 'states': {u: 'unknown' for u in L.DRAIN_UNITS}}
    vu = L.quiesce_verdict(m1, m2, dirs, unm, now=t_now)
    check(not vu['ok'] and vu['timerEvidence'] == 'unmeasured', 'an unmeasurable timer is refused without an assertion')
    va = L.quiesce_verdict(m1, m2, dirs, unm, asserted=True, now=t_now)
    check(va['ok'] and va['timerEvidence'] == 'asserted-by-caller', 'an unmeasurable timer with the caller\'s assertion passes and is recorded as an assertion')
    check(L.quiesce_verdict(m1, m2, dirs, {'measured': True, 'states': dict(INACTIVE['states'], **{L.DRAIN_UNITS[0]: 'active'})}, asserted=True, now=t_now)['ok'] is False,
          'an assertion cannot override a measured active unit')

    class P:
        def __init__(self, out, rc=0): self.stdout, self.returncode = out, rc
    check(L.measure_units(lambda *a, **k: P('inactive\n')) == {'measured': True, 'states': {u: 'inactive' for u in L.DRAIN_UNITS}}, 'measure_units reads inactive')
    check(L.measure_units(lambda *a, **k: P('active\n'))['states'][L.DRAIN_UNITS[0]] == 'active', 'measure_units reads active')
    check(L.measure_units(lambda *a, **k: P('garbage\n'))['measured'] is False, 'an unrecognised state is unmeasured')
    check(L.measure_units(lambda *a, **k: P(''))['measured'] is False, 'empty output is unmeasured')

    def boom(*a, **k): raise FileNotFoundError('runuser')
    check(L.measure_units(boom)['measured'] is False, 'a missing runuser is unmeasured')
    seen = []
    L.measure_units(lambda argv, **k: (seen.append(argv), P('inactive\n'))[1])
    check(all(a[:3] == ['runuser', '-u', 'developer'] and 'is-active' in a for a in seen) and len(seen) == 2, 'the units are measured with runuser and systemctl --user is-active')
    # other quiesce rules
    mm = copy.deepcopy(m2)
    mm['takenUtc'] = (L.parse_utc(m1['takenUtc']) + timedelta(seconds=299)).isoformat()
    check('299s apart' in ' '.join(L.quiesce_verdict(m1, mm, dirs, INACTIVE, now=t_now)['reasons']), 'a gap under 300 s is refused')
    check('future' in ' '.join(L.quiesce_verdict(m1, m2, dirs, INACTIVE, now=L.parse_utc(m1['takenUtc']))['reasons']), 'an M2 dated in the future is refused')
    for field, val in (('size', 1), ('sha256', H('0')), ('mtime_ns', 5), ('ctime_ns', 5), ('ino', 5), ('mode', '0o600')):
        mx = copy.deepcopy(m2)
        mx['files'][0][field] = val
        check(any(field in r for r in L.quiesce_verdict(m1, mx, dirs, INACTIVE, now=t_now)['reasons']), f'a {field} difference between M1 and M2 is refused')
    mx = copy.deepcopy(m2)
    mx['files'].pop()
    check(any('only one manifest' in r for r in L.quiesce_verdict(m1, mx, dirs, INACTIVE, now=t_now)['reasons']), 'a file missing from M2 is refused')
    fh = open(os.path.join(e.base, SUBS[1], 'one.ready'), 'rb')
    vo = L.quiesce_verdict(m1, m2, dirs, INACTIVE, now=t_now)
    check(not vo['ok'] and any('open under' in r and str(os.getpid()) in r for r in vo['reasons']), 'an open file under a subtree is detected by pid')
    # seal-time re-checks while the file is still open
    q = {'ok': True, 'm1Sha256': L.sha(L.canon(m1))}          # a proof that matches M1; only the seal-time re-checks are under test here
    refused(lambda: L.seal(e.base, m1, q, 'snap-open', measure=INACTIVE, now=t_now, **e.kw()), 'at seal time files are open', 'seal re-scans open files')
    fh.close()
    active = {'measured': True, 'states': dict(INACTIVE['states'], **{L.DRAIN_UNITS[0]: 'active'})}
    refused(lambda: L.seal(e.base, m1, q, 'snap-timer', measure=active, now=t_now, **e.kw()), 'at seal time', 'seal re-checks the timer and refuses when it is active again')
    refused(lambda: L.seal(e.base, m1, q, 'snap-timer', measure=unm, now=t_now, **e.kw()), 'at seal time', 'seal refuses an unmeasurable timer without an assertion')
    ok_unm = L.seal(e.base, m1, q, 'snap-asserted', measure=unm, asserted=True, now=t_now, **e.kw())
    check(ok_unm['files'] == 4, 'seal accepts an unmeasurable timer only on the caller\'s assertion')
    refused(lambda: L.seal(e.base, m1, q, 'snap-early', measure=INACTIVE, now=L.parse_utc(m1['takenUtc']) + timedelta(seconds=100), **e.kw()),
            'have passed since M1 by the clock', 'seal checks the real elapsed time since M1 against the clock, not the self-reported gap')
    refused(lambda: L.seal(e.base, m1, dict(q, ok=False), 'snap-q', measure=INACTIVE, now=t_now, **e.kw()), 'quiesce proof', 'a failed quiesce proof is refused')
    refused(lambda: L.seal(e.base, m1, dict(q, m1Sha256=H('1')), 'snap-q', measure=INACTIVE, now=t_now, **e.kw()), 'quiesce proof', 'a proof for another manifest is refused')
    refused(lambda: L.seal(e.base, m1, q, 'Bad Id', measure=INACTIVE, now=t_now, **e.kw()), 'bad snapshot id', 'a bad snapshot id is refused')

# ============================================================ seal basics and write-path guards
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1, q, res = e.seal('snap-1')
    snap = os.path.join(e.root, 'inputs', 'snap-1')
    sm, sm_sha = L.read_json(os.path.join(e.root, 'inputs', 'snap-1.manifest.json'))
    check(res['files'] == 4 and sm_sha == res['sealedManifestSha256'] and sm['sourceIdentity'] == L.source_identity(m1), 'seal copies 4 files and returns the sealed manifest hash')
    check(all(L.sha_file(os.path.join(snap, f['path'])) == f['sha256'] for f in sm['files']), 'every sealed file hashes to the manifest')
    check(all(os.stat(os.path.join(snap, f['path'])).st_mtime_ns == f['mtime_ns'] for f in sm['files']), 'mtimes are preserved')
    check(all(oct(os.stat(os.path.join(snap, f['path'])).st_mode & 0o777) == '0o644' for f in sm['files']), 'sealed files are 0644')
    refused(lambda: e.seal('snap-1'), 'snapshot already exists', 'an existing snapshot is not overwritten')
    os.chmod(e.services, 0o777)
    refused(lambda: e.seal('snap-w'), 'not root-safe', 'a world-writable services directory is refused')
    os.chmod(e.services, 0o755)
    kw = e.kw(); kw['owner_uid'] = UID + 1
    refused(lambda: L.claim(H('a'), 'op-one', 'snap-1', H('a'), precheck('op-one'), H('b'), **kw), 'not root-safe', 'a directory owned by another uid is refused')
    other = os.path.join(tmp, 'other'); os.mkdir(other)
    kw = e.kw(); kw['view_root'] = other
    refused(lambda: L.claim(H('a'), 'op-one', 'snap-1', H('a'), precheck('op-one'), H('b'), **kw), 'not the mounted view', 'a source directory that is not the mounted view is refused')
    kw = e.kw(); kw['view_root'] = os.path.join(tmp, 'absent')
    refused(lambda: L.claim(H('a'), 'op-one', 'snap-1', H('a'), precheck('op-one'), H('b'), **kw), 'mounted view is absent', 'an absent view is refused')
    sw = os.path.join(tmp, 'services2'); os.mkdir(sw); os.chmod(sw, 0o755)
    os.symlink(e.root, os.path.join(sw, 'learn'))
    refused(lambda: L.claim(H('a'), 'op-one', 'snap-1', H('a'), precheck('op-one'), H('b'), source_root=os.path.join(sw, 'learn'), view_root=e.root, services=sw, owner_uid=UID),
            'not root-safe', 'a symlinked source directory is refused')

# ============================================================ F3: a claim is bound to a real seal
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1, q, res = e.seal('snap-1')
    ident, sealed = L.source_identity(m1), res['sealedManifestSha256']
    refused(lambda: claim(e, ident, 'op-one', 'snap-none', H('9')), 'no sealed manifest', 'a claim with no sealed manifest is refused')
    refused(lambda: claim(e, ident, 'op-one', 'snap-1', H('9')), 'another sha256', 'a claim with the wrong sealed hash is refused')
    refused(lambda: claim(e, H('c'), 'op-one', 'snap-1', sealed), 'another source identity', 'a claim for another source identity is refused')
    refused(lambda: claim(e, ident, 'op-one', 'Bad Id', sealed), 'bad snapshot id', 'a claim with a bad snapshot id is refused')
    sf = os.path.join(e.root, 'inputs', 'snap-1', m1['files'][0]['path'])
    data = open(sf, 'rb').read()
    open(sf, 'ab').write(b'+')
    refused(lambda: claim(e, ident, 'op-one', 'snap-1', sealed), 'missing or resized', 'a claim over a resized sealed file is refused')
    open(sf, 'wb').write(data)
    os.unlink(sf)
    refused(lambda: claim(e, ident, 'op-one', 'snap-1', sealed), 'missing or resized', 'a claim over a missing sealed file is refused')
    open(sf, 'wb').write(data)
    sm_path = os.path.join(e.root, 'inputs', 'snap-1.manifest.json')
    smj = json.load(open(sm_path))
    smj['snapshotId'] = 'snap-1'          # a copy of the manifest of snap-1 filed under another snapshot id
    other_sha = L.sha(L.canon(smj))
    open(os.path.join(e.root, 'inputs', 'snap-9.manifest.json'), 'wb').write(L.canon(smj))
    refused(lambda: claim(e, ident, 'op-one', 'snap-9', other_sha), 'names another snapshot', 'a sealed manifest that names another snapshot is refused')
    for bad, needle, what in ((precheck('op-one', mode='--run'), 'did not pass', 'a run result is not a precheck'),
                              (precheck('op-one', exitCode=5), 'did not pass', 'a failed precheck is refused'),
                              (precheck('op-one', errors=['x']), 'did not pass', 'a precheck with errors is refused'),
                              (precheck('op-one', gates={'inputMounted': True}), 'input untouched', 'a precheck that touched the input is refused'),
                              (precheck('op-one', gates={}), 'input untouched', 'a precheck without the input gate is refused'),
                              (precheck('op-two'), 'another op', 'a precheck for another op is refused'),
                              (precheck('op-one', schema='x/1'), 'another schema', 'a precheck with another schema is refused')):
        refused(lambda b=bad: L.claim(ident, 'op-one', 'snap-1', sealed, b, H('b'), **e.kw()), needle, what)
    c1 = claim(e, ident, 'op-one', 'snap-1', sealed)
    cj, _ = L.read_json(c1['claim'])
    check(os.path.basename(c1['claim']) == ident + '.claim' and cj['generation'] == 1 and cj['supersedes'] is None, 'the claim is named by the source identity, generation 1')
    check(cj['snapshotId'] == 'snap-1' and cj['sealedManifestSha256'] == sealed and cj['precheckResultSha256'] == H('b'), 'the claim records the snapshot, sealed manifest and precheck hashes')
    refused(lambda: claim(e, ident, 'op-one', 'snap-1', sealed), 'already exists', 'a second claim for the same op is refused')
    refused(lambda: claim(e, ident, 'op-two', 'snap-1', sealed), 'already exists', 'a second op cannot claim the same source')

# ============================================================ F1 and F8: void
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1, q, res = e.seal('snap-1')
    ident, sealed = L.source_identity(m1), res['sealedManifestSha256']
    claim(e, ident, 'op-one', 'snap-1', sealed)
    vk = lambda **o: dict(ident=ident, op_id='op-one', operator_result=run_result('op-one'), op_sha=H('1'), finalizer_record=fin('op-one'), fin_sha=H('2'),
                          corroboration_ref=URL, marker_root=e.markers, **e.kw()) | o
    admit(e, 'op-one', ident)
    refused(lambda: L.void(**vk(op_id='op-two')), 'names another op', 'void of another op is refused')
    refused(lambda: L.void(**vk(operator_result=precheck('op-one', containers=[]))), 'not from a --run', 'a precheck RESULT is not evidence for a void')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', mode='--precheck-only'))), 'not from a --run', 'a run RESULT labelled precheck-only is refused')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', exitCode=0))), 'successful run', 'a successful run RESULT cannot void')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', exitCode=None))), 'integer exit code', 'a RESULT without an exit code is refused')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', exitCode=True))), 'integer exit code', 'a boolean exit code is refused')
    bad = run_result('op-one'); del bad['exitCode']
    refused(lambda: L.void(**vk(operator_result=bad)), 'integer exit code', 'a RESULT lacking exitCode is refused')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', gates={'inputMounted': True}))), 'input unmounted', 'a mounted input blocks a void')
    mounted = run_result('op-one'); mounted['containers'][1]['mounts'].append({'destination': '/in'})
    refused(lambda: L.void(**vk(operator_result=mounted)), 'container with /in', 'a container with /in blocks a void')
    nolist = run_result('op-one'); del nolist['containers']
    refused(lambda: L.void(**vk(operator_result=nolist)), 'lacks the container list', 'a missing container list blocks a void')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', schema='x/1'))), 'another schema', 'a RESULT with another schema blocks a void')
    refused(lambda: L.void(**vk(finalizer_record=fin('op-one', inputRead=True))), 'inputRead false', 'inputRead true blocks a void')
    refused(lambda: L.void(**vk(finalizer_record=fin('op-one', containersStartedWithInput=['feed']))), 'containersStartedWithInput', 'a container started with the input blocks a void')
    noin = fin('op-one'); del noin['inputRead']
    refused(lambda: L.void(**vk(finalizer_record=noin)), 'inputRead false', 'a finalizer record without inputRead blocks a void')
    refused(lambda: L.void(**vk(finalizer_record=fin('op-one', schema='x/1'))), 'another schema', 'a finalizer record with another schema blocks a void')
    refused(lambda: L.void(**vk(finalizer_record=fin('op-one', runNonceSha256=H('8')))), 'not bound to the RESULT by run nonce', 'a finalizer record with another run nonce is refused')
    nn = fin('op-one'); del nn['runNonceSha256']
    refused(lambda: L.void(**vk(finalizer_record=nn)), 'run nonce', 'a finalizer record without a run nonce is refused')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', runNonceSha256='zz'), finalizer_record=fin('op-one', runNonceSha256='zz'))), 'run nonce', 'a malformed run nonce is refused even when both match')
    refused(lambda: L.void(**vk(operator_result=run_result('op-one', operatorSha256=H('9')))), 'operator pin differs', 'a RESULT operator pin that differs from the admission is refused')
    refused(lambda: L.void(**vk(finalizer_record=fin('op-one', finalizerSha256=H('9')))), 'finalizer record pin differs', 'a finalizer pin that differs from the admission is refused')
    refused(lambda: L.void(**vk(corroboration_ref='')), 'corroboration reference', 'a void without a Root corroboration reference is refused')
    refused(lambda: L.void(**vk(corroboration_ref='https://example.org/x')), 'corroboration reference', 'a non-GitHub corroboration reference is refused')
    refused(lambda: L.void(**vk(corroboration_ref='https://github.com/FS-GG/.github/issues/4647')), 'corroboration reference', 'an issue URL without a comment is refused')
    os.makedirs(os.path.join(e.markers, 'op-one'))
    open(os.path.join(e.markers, 'op-one', 'input-read'), 'w').close()
    refused(lambda: L.void(**vk()), 'input-read marker exists', 'the input-read marker (owned by the pod operator) blocks a void')
    os.unlink(os.path.join(e.markers, 'op-one', 'input-read'))
    check(os.path.isfile(os.path.join(e.root, 'claims', ident + '.claim')), 'every refused void leaves the claim in place')
    os.unlink(os.path.join(e.root, 'admissions', 'op-one.admit'))
    refused(lambda: L.void(**vk()), 'no admission file', 'a void with no admission to check the pins against is refused')
    admit(e, 'op-one', ident)
    refused(lambda: L.void(**vk(observed_exit_code=0)), 'observed exit code 0 differs', 'an observed exit code that differs from the RESULT is refused')
    refused(lambda: L.void(**vk(observed_exit_code=True)), 'not an integer', 'a boolean observed exit code is refused')
    refused(lambda: L.void(**vk(observed_exit_code='5')), 'not an integer', 'a string observed exit code is refused')
    vres = L.void(**vk(observed_exit_code=5))
    vr, _ = L.read_json(os.path.join(e.root, 'claims', f'{ident}.void-op-one.json'))
    check(vr['operatorResultSha256'] == H('1') and vr['finalizerRecordSha256'] == H('2') and vr['claimSha256'] == vres['claimSha256'], 'the void record carries the evidence hashes')
    check('never mounted' in vr['proves'] and 'never read' in vr['doesNotProve'], 'the void record states what it proves and does not prove')
    check(vr['observedExitCode'] == 5, 'the void record carries the observed exit code Root captured')
    check(vr['rootCorroborationRef'] == URL and vr['rootCorroborationVerifiedByTool'] is False, 'the void record carries Root\'s corroboration reference and says the tool did not verify it')
    check('written by the operator account' in vr['attestation'] and 'not independent' in vr['attestation'], 'the void record says the artifacts come from the operator account')
    check('compromised' not in json.dumps(vr).lower(), 'the void record does not claim to detect a compromised operator account')
    check(not os.path.isfile(vres['voided'][:-len('.voided-op-one')]) and os.path.isfile(vres['voided']), 'the voided claim is renamed, not deleted')
    src = open(TOOL).read()
    check('detect mistakes and a compromised' not in src and 'defend against a compromise' not in src, 'the tool\'s own text no longer claims to detect a compromised operator account')
    readme = open(os.path.join(os.path.dirname(TOOL), 'README.md')).read()
    check('compromised' not in readme.lower().replace('does not detect a compromised', '') and 'attested by artifacts' in readme, 'the README says a void is attested by operator-account artifacts')

# ============================================================ F8: ownership of the custody directory and the marker
check('learn-custody' not in L.MARKER_ROOT and L.MARKER_ROOT.endswith('/learn-input'), 'the input-read marker lives outside the finalizer-owned custody directory')
readme = open(os.path.join(os.path.dirname(TOOL), 'README.md')).read()
check('finalizer' in readme and 'exist_ok=False' in readme and 'pod operator' in readme.lower() and 'learn-input' in readme, 'the README names the owner of the custody directory and of the marker')

# ============================================================ F2: supersede, status, generation
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    m1, q, res = e.seal('snap-1')
    ident, sealed = L.source_identity(m1), res['sealedManifestSha256']
    kws = lambda **o: dict(ident=ident, old_op='op-two', new_op='op-three', snapshot_id='snap-1', sealed_sha=sealed, precheck=precheck('op-three'), precheck_sha=H('e'),
                           old_finalizer=fin('op-two'), old_fin_sha=H('f'), decision_ref=URL, reason='root decision to re-run', marker_root=e.markers, **e.kw()) | o
    check(L.status(ident, e.root)['state'] == 'unclaimed', 'status starts unclaimed')
    claim(e, ident, 'op-two', 'snap-1', sealed)
    check(L.status(ident, e.root)['state'] == 'claimed', 'status reports claimed')
    refused(lambda: L.supersede(**kws()), 'no positive evidence', 'a claim with no consumption evidence cannot be superseded')
    # F2b: supersede needs POSITIVE evidence; a junk or incomplete finalizer record is not evidence of consumption
    for what, rec in (('only an opId', {'opId': 'op-two'}), ('inputRead missing and no containers field', fin('op-two')),
                      ('inputRead false', fin('op-two', inputRead=False)), ('inputRead the string "true"', fin('op-two', inputRead='true')),
                      ('inputRead 1', fin('op-two', inputRead=1)), ('an empty containers list', fin('op-two', inputRead=None, containersStartedWithInput=[])),
                      ('containers not a list', fin('op-two', inputRead=None, containersStartedWithInput='feed'))):
        if what == 'inputRead missing and no containers field':
            rec = {k: v for k, v in rec.items() if k not in ('inputRead', 'containersStartedWithInput')}
        refused(lambda r=rec: L.supersede(**kws(old_finalizer=r)), 'no positive evidence', f'supersede refuses a finalizer record with {what}')
    check(os.path.isfile(os.path.join(e.root, 'claims', ident + '.claim')) and not [f for f in os.listdir(os.path.join(e.root, 'claims')) if 'supersede' in f],
          'refused supersedes on junk records write and rename nothing')
    os.makedirs(os.path.join(e.markers, 'op-two'))
    open(os.path.join(e.markers, 'op-two', 'input-read'), 'w').close()
    for key, bad, needle, what in (('decision_ref', 'no', 'decision reference', 'a malformed decision reference'),
                                   ('decision_ref', 'https://github.com/FS-GG/.github/issues/4647', 'decision reference', 'an issue URL with no comment id'),
                                   ('decision_ref', 'https://evil.example/FS-GG/x/issues/1#issuecomment-1', 'decision reference', 'a URL on another host'),
                                   ('reason', '', 'reason', 'an empty reason'), ('old_op', 'op-nine', 'another op', 'another old op with the old finalizer record'),
                                   ('new_op', 'op-two', 'different op', 'the same op as successor'),
                                   ('snapshot_id', 'snap-zz', 'no sealed manifest', 'a successor over an unsealed snapshot'),
                                   ('sealed_sha', H('9'), 'another sha256', 'a successor with another sealed hash'),
                                   ('old_finalizer', fin('op-nine'), 'another op', 'a finalizer record for another op'),
                                   ('precheck', precheck('op-three', gates={'inputMounted': True}), 'input untouched', 'a dirty precheck for the new op')):
        refused(lambda k=key, b=bad: L.supersede(**kws(**{k: b})), needle, f'supersede refuses {what}')
    refused(lambda: L.supersede(**kws(old_op='op-nine', old_finalizer=fin('op-nine'))), 'live claim names another op', 'supersede refuses an old op that is not the live claim\'s')
    check(os.path.isfile(os.path.join(e.root, 'claims', ident + '.claim')) and not [f for f in os.listdir(os.path.join(e.root, 'claims')) if 'supersede' in f],
          'refused supersedes write and rename nothing')
    sres = L.supersede(**kws())
    live = L.read_json(os.path.join(e.root, 'claims', ident + '.claim'))[0]
    check(live['opId'] == 'op-three' and live['generation'] == 2 and live['supersedes'] == 'op-two', 'the successor claim is generation 2 and names what it supersedes')
    check(os.path.isfile(sres['consumed']) and sres['resumed'] is False, 'the consumed claim is kept')
    sr, _ = L.read_json(os.path.join(e.root, 'claims', f'{ident}.supersede-op-two-to-op-three.json'))
    check(sr['decisionRef'] == URL and sr['decisionRefVerifiedByTool'] is False and sr['generationFrom'] == 1 and sr['oldFinalizerRecordSha256'] == H('f'), 'the supersede record carries the decision, says the tool did not verify it, and the evidence')
    st = L.status(ident, e.root)
    check(st['state'] == 'claimed' and st['nextGeneration'] == 3 and [h['kind'] for h in st['history']] == ['consumed', 'live'], 'status after a completed supersede reports claimed with the right history')
    refused(lambda: claim(e, ident, 'op-four', 'snap-1', sealed), 'already exists', 'a fresh claim over a live claim still hits EEXIST')
    # void the successor: the state must be voided (not supersede-incomplete) and the next generation continues
    admit(e, 'op-three', ident)
    L.void(ident, 'op-three', run_result('op-three'), H('1'), fin('op-three'), H('2'), URL, marker_root=e.markers, **e.kw())
    st = L.status(ident, e.root)
    check(st['state'] == 'voided' and st['nextGeneration'] == 3 and st['pending'] == [], 'after the successor is voided the state is voided, not supersede-incomplete')
    c3 = claim(e, ident, 'op-five', 'snap-1', sealed)
    check(c3['generation'] == 3, 'a claim after a voided successor continues the generation instead of restarting at 1')
    check(L.status(ident, e.root)['state'] == 'claimed', 'status reports the new claim')
    # consume op-five, then crash a supersede right after the intent is written (the rename fails)
    os.makedirs(os.path.join(e.markers, 'op-five'))
    open(os.path.join(e.markers, 'op-five', 'input-read'), 'w').close()
    k5 = lambda **o: kws(old_op='op-five', new_op='op-six', precheck=precheck('op-six'), old_finalizer=fin('op-five'), **o)

    def crash(a, b): raise OSError('crash after the intent')
    try:
        L.supersede(**k5(rename=crash))
        check(False, 'the injected crash propagates')
    except OSError:
        check(True, 'the injected crash propagates')
    st = L.status(ident, e.root)
    check(st['state'] == 'supersede-pending' and st['live'] is True and st['pending'][0]['kind'] == 'intent-without-successor', 'an intent-only crash is reported as supersede-pending')
    check(any('supersede --old-op-id op-five --new-op-id op-six' in r for r in st['resumeWith']), 'status says how to resume')
    refused(lambda: claim(e, ident, 'op-seven', 'snap-1', sealed), 'unfinished supersede', 'claim refuses while a supersede is pending')
    # F2a: a competing supersede while an intent is pending is refused and names the resumable one (it would strand the first)
    os.makedirs(os.path.join(e.markers, 'op-nine'), exist_ok=True)
    competing = lambda: L.supersede(**kws(old_op='op-five', new_op='op-nine', precheck=precheck('op-nine'), old_finalizer=fin('op-five')))
    refused(competing, 'supersede --old-op-id op-five --new-op-id op-six', 'a competing supersede during a pending intent is refused and names the resumable one')
    check(not os.path.lexists(os.path.join(e.root, 'claims', f'{ident}.supersede-op-five-to-op-nine.json')), 'the refused competing supersede wrote no intent')
    check(L.status(ident, e.root)['state'] == 'supersede-pending' and L.status(ident, e.root)['live'] is True, 'the pending state is unchanged by the refused competing supersede')
    refused(lambda: L.supersede(**k5(reason='another reason')), 'different decision, reason or evidence', 'resuming with another reason is refused')
    refused(lambda: L.supersede(**k5(old_fin_sha=H('0'))), 'different decision, reason or evidence', 'resuming with other evidence is refused')
    rres = L.supersede(**k5())
    check(rres['resumed'] is True and L.read_json(os.path.join(e.root, 'claims', ident + '.claim'))[0]['opId'] == 'op-six', 'an intent-only crash is resumable')
    check(L.read_json(os.path.join(e.root, 'claims', ident + '.claim'))[0]['generation'] == 4, 'the resumed supersede continues the generation')
    st = L.status(ident, e.root)
    check(st['state'] == 'claimed' and st['pending'] == [], 'after the resume the state is claimed with nothing pending')
    refused(lambda: L.supersede(**k5()), 'already completed', 'a completed supersede cannot be repeated')
    # a rename-only crash (the old claim moved, no successor) resumes too, and a competing supersede is refused there as well
    os.makedirs(os.path.join(e.markers, 'op-six'), exist_ok=True)
    open(os.path.join(e.markers, 'op-six', 'input-read'), 'w').close()
    def after_rename(a, b):
        os.rename(a, b)
        raise OSError('crash right after the rename')
    k6b = lambda **o: kws(old_op='op-six', new_op='op-six2', precheck=precheck('op-six2'), old_finalizer=fin('op-six'), **o)
    try:
        L.supersede(**k6b(rename=after_rename))
    except OSError:
        pass
    st = L.status(ident, e.root)
    check(st['state'] == 'supersede-pending' and st['live'] is False, 'a rename-only crash leaves no live claim and reports supersede-pending')
    refused(lambda: L.supersede(**kws(old_op='op-six', new_op='op-other', precheck=precheck('op-other'), old_finalizer=fin('op-six'))),
            'supersede --old-op-id op-six --new-op-id op-six2', 'a competing supersede after a rename-only crash is refused and names the resumable one')
    check(L.supersede(**k6b())['resumed'] is True, 'a rename-only crash resumes to the successor claim')
    check(L.status(ident, e.root)['state'] == 'claimed', 'after the resume the state is claimed')
    # crash after the rename but before the successor claim exists
    os.makedirs(os.path.join(e.markers, 'op-six2'), exist_ok=True)
    open(os.path.join(e.markers, 'op-six2', 'input-read'), 'w').close()
    k6 = lambda **o: kws(old_op='op-six2', new_op='op-eight', precheck=precheck('op-eight'), old_finalizer=fin('op-six2'), **o)
    real_claim = L.claim
    L.claim = lambda *a, **k: (_ for _ in ()).throw(OSError('crash before the successor claim'))
    try:
        L.supersede(**k6())
    except OSError:
        pass
    finally:
        L.claim = real_claim
    st = L.status(ident, e.root)
    check(st['state'] == 'supersede-pending' and st['live'] is False and {p['kind'] for p in st['pending']} >= {'intent-without-successor'}, 'a crash after the rename is reported as supersede-pending with no live claim')
    refused(lambda: claim(e, ident, 'op-nine', 'snap-1', sealed), 'unfinished supersede', 'claim refuses a consumed claim without a successor')
    L.supersede(**k6())
    check(L.status(ident, e.root)['state'] == 'claimed', 'the second resume completes the supersede')
    # a consumed claim with no intent at all (hand-edited state) also blocks a plain claim
    claims_dir = os.path.join(e.root, 'claims')
    os.rename(os.path.join(claims_dir, ident + '.claim'), os.path.join(claims_dir, ident + '.claim.consumed-op-eight'))
    ss = L.status(ident, e.root)
    check(ss['state'] == 'supersede-pending' and ss['pending'][0]['kind'] == 'consumed-without-successor', 'a consumed claim with no successor is reported')
    refused(lambda: claim(e, ident, 'op-ten', 'snap-1', sealed), 'unfinished supersede', 'claim refuses when a consumed claim has no successor')

# ============================================================ F2b: each kind of positive evidence lets a supersede through
for label, rec_over, marker in (('inputRead true', dict(inputRead=True), False), ('a container started with the input', dict(inputRead=None, containersStartedWithInput=['feed']), False),
                                ('the input-read marker alone', {}, True)):
    with tempfile.TemporaryDirectory() as tmp:
        e = Env(tmp)
        m1, q, res = e.seal('snap-1')
        ident, sealed = L.source_identity(m1), res['sealedManifest' + 'Sha256']
        claim(e, ident, 'op-a', 'snap-1', sealed)
        fr = {k: v for k, v in dict(fin('op-a'), **rec_over).items() if v is not None}
        if marker:
            os.makedirs(os.path.join(e.markers, 'op-a'))
            open(os.path.join(e.markers, 'op-a', 'input-read'), 'w').close()
            fr = {'opId': 'op-a'}                              # a bare record plus the marker is positive evidence
        out = L.supersede(ident, 'op-a', 'op-b', 'snap-1', sealed, precheck('op-b'), H('e'), fr, H('f'), URL, 'root decision', marker_root=e.markers, **e.kw())
        check(out['generation'] == 2, f'supersede is allowed on positive evidence: {label}')

# ============================================================ F7: admission reader
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    ident = H('a')
    adm = dict(op_id='op-adm', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'), image_ids=[H('5'), H('4')], ttl=300, now=now)
    refused(lambda: L.admit(**dict(adm, ttl=601), **e.kw()), 'ttl must be', 'a ttl over 600 s is refused')
    refused(lambda: L.admit(**dict(adm, ttl=0), **e.kw()), 'ttl must be', 'a ttl of 0 is refused')
    refused(lambda: L.admit(**dict(adm, image_ids=[]), **e.kw()), 'at least one image', 'an admission without an image id is refused')
    refused(lambda: L.admit(**dict(adm, argv_sha='zz'), **e.kw()), '64 lowercase hex', 'a malformed pin is refused')
    L.admit(**adm, **e.kw())
    refused(lambda: L.admit(**adm, **e.kw()), 'already exists', 'a second admission for the same op is refused')
    chk = dict(path_root=e.root, op_id='op-adm', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'),
               image_ids=[H('4'), H('5')], chain_start=e.services, owner_uid=UID, now=now + timedelta(seconds=10))

    def hand(op, issued, expires, naive=False, raw=None):
        body = dict(schema='learn-pod-admission/1', opId=op, argvJsonSha256=H('1'), sourceIdentity=ident, operatorSha256=H('2'), finalizerSha256=H('3'),
                    imageIds=[H('4')], issuedUtc=issued.replace(tzinfo=None).isoformat() if naive else issued.isoformat(),
                    expiresUtc=expires.replace(tzinfo=None).isoformat() if naive else expires.isoformat())
        if raw:
            body.update(raw)
        p = os.path.join(e.root, 'admissions', op + '.admit')
        open(p, 'w').write(json.dumps({k: v for k, v in body.items() if v is not None}))
        os.chmod(p, 0o644)
        return dict(chk, op_id=op, image_ids=[H('4')], now=now + timedelta(seconds=5))

    # F7 conformance vectors for the pod operator's copy of the reader: (id, name, kwargs builder, expected reason substring or None)
    VECTORS = [
        ('A01', 'match-passes', lambda: chk, None),
        ('A02', 'argv-differs', lambda: dict(chk, argv_sha=H('9')), 'argvJsonSha256 differs'),
        ('A03', 'source-identity-differs', lambda: dict(chk, ident=H('9')), 'sourceIdentity differs'),
        ('A04', 'operator-differs', lambda: dict(chk, operator_sha=H('9')), 'operatorSha256 differs'),
        ('A05', 'finalizer-differs', lambda: dict(chk, finalizer_sha=H('9')), 'finalizerSha256 differs'),
        ('A06', 'image-set-differs', lambda: dict(chk, image_ids=[H('4')]), 'imageIds differs'),
        ('A07', 'expired', lambda: dict(chk, now=now + timedelta(seconds=301)), 'has expired'),
        ('A08', 'absent', lambda: dict(chk, op_id='op-nope'), 'absent or unreadable'),
        ('A09', 'lifetime-one-year', lambda: hand('op-year', now - timedelta(days=300), now + timedelta(days=300)), 'lifetime exceeds 600 seconds'),
        ('A10', 'lifetime-601s', lambda: hand('op-601', now, now + timedelta(seconds=601)), 'lifetime exceeds 600 seconds'),
        ('A11', 'lifetime-600s-ok', lambda: hand('op-600', now, now + timedelta(seconds=600)), None),
        ('A12', 'issued-in-future', lambda: hand('op-future', now + timedelta(seconds=60), now + timedelta(seconds=300)), 'dated in the future'),
        ('A13', 'expires-before-issued', lambda: dict(hand('op-back', now, now - timedelta(seconds=5)), now=now + timedelta(seconds=1)), 'expires before it was issued'),
        ('A14', 'naive-times', lambda: hand('op-naive', now, now + timedelta(seconds=300), naive=True), 'no time zone'),
        ('A15', 'unparsable-times', lambda: hand('op-junk', now, now, raw={'issuedUtc': 'yesterday'}), 'no valid issue or expiry time'),
        ('A16', 'missing-times', lambda: hand('op-nots', now, now, raw={'issuedUtc': None, 'expiresUtc': None}), 'no valid issue or expiry time'),
    ]
    for vid, name, build, expect in VECTORS:
        got = L.check_admission(**build())
        if expect is None:
            check(got == [], f'conformance vector {vid} {name}: passes ({got})')
        else:
            check(any(expect in r for r in got), f'conformance vector {vid} {name}: refused with "{expect}" (got {got})')
    os.chmod(e.root, 0o777)
    check('not root-safe' in ' '.join(L.check_admission(**chk)), 'conformance vector A17 world-writable-directory: refused')
    os.chmod(e.root, 0o755)
    check('not root-safe' in ' '.join(L.check_admission(**dict(chk, owner_uid=UID + 1))), 'conformance vector A18 foreign-owner-chain: refused')
    readme_text = open(os.path.join(os.path.dirname(TOOL), 'README.md')).read()
    ids = [v[0] for v in VECTORS] + ['A17', 'A18']
    check(all(i in readme_text for i in ids) and len(ids) == 18, 'the README lists all 18 admission conformance vectors by id')
    # the read-only subcommands need no root; the write commands still do
    if os.geteuid() != 0:
        with contextlib.redirect_stdout(io.StringIO()) as out:
            code = L.main(['status', '--source-identity', H('a')])
        check(code == 0 and 'run as root' not in out.getvalue() and 'unclaimed' in out.getvalue(), 'status runs without root')
        with contextlib.redirect_stdout(io.StringIO()) as out:
            code = L.main(['check-admission', '--op-id', 'op-x', '--argv-json-sha256', H('1'), '--source-identity', H('a'), '--operator-sha256', H('2'),
                           '--finalizer-sha256', H('3'), '--image-id', H('4')])
        check('run as root' not in out.getvalue(), 'check-admission runs without root')
        for cmd in ('manifest', 'quiesce', 'seal', 'claim', 'void', 'supersede', 'admit'):
            with contextlib.redirect_stdout(io.StringIO()) as out:
                code = L.main([cmd])
            check(code == 5 and 'run as root' in out.getvalue(), f'{cmd} refuses to run unless root')

# ============================================================ pinned directory descriptors: the writes go to the directory that was checked
def swap_hook(e, view):
    """Make L.open_dir_chain swap the source directory (and re-point the stand-in mount) right after it pins it."""
    real = L.open_dir_chain

    def hooked(path, start, owner_uid=0):
        fd = real(path, start, owner_uid)
        if path == e.root:
            os.rename(e.root, e.root + '.old')
            os.mkdir(e.root)
            os.chmod(e.root, 0o755)
            os.unlink(view)
            os.symlink(e.root + '.old', view)              # the mount keeps the ORIGINAL inode
        return fd
    return real, hooked


def with_swap(e, view, fn):
    real, hooked = swap_hook(e, view)
    L.open_dir_chain = hooked
    try:
        return fn()
    finally:
        L.open_dir_chain = real


def swapped_kw(e, view):
    return dict(source_root=e.root, view_root=view, services=e.services, owner_uid=UID)


def fresh_swap_env(tmp):
    e = Env(tmp)
    view = os.path.join(e.services, 'view')
    os.symlink(e.root, view)
    return e, view


# admit
with tempfile.TemporaryDirectory() as tmp:
    e, view = fresh_swap_env(tmp)
    r = with_swap(e, view, lambda: L.admit('op-pin', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, **swapped_kw(e, view)))
    check(os.path.isfile(os.path.join(e.root + '.old', 'admissions', 'op-pin.admit')), 'admit wrote into the pinned (original) directory after a swap')
    check(not os.path.lexists(os.path.join(e.root, 'admissions')), 'admit wrote nothing into the directory swapped in at the path')
    check(r['admission'] == os.path.join(e.root, 'admissions', 'op-pin.admit'), 'the reported path is the path the operator named, not /proc/self/fd')

# seal
with tempfile.TemporaryDirectory() as tmp:
    e, view = fresh_swap_env(tmp)
    m1 = L.take_manifest(e.base, SUBS)
    m2 = copy.deepcopy(m1)
    m2['takenUtc'] = (L.parse_utc(m1['takenUtc']) + timedelta(seconds=301)).isoformat()
    t_now = L.parse_utc(m1['takenUtc']) + timedelta(seconds=400)
    q = L.quiesce_verdict(m1, m2, [f'{e.base}/{s}' for s in SUBS], INACTIVE, now=t_now)
    r = with_swap(e, view, lambda: L.seal(e.base, m1, q, 'snap-pin', measure=INACTIVE, now=t_now, **swapped_kw(e, view)))
    check(os.path.isfile(os.path.join(e.root + '.old', 'inputs', 'snap-pin.manifest.json')) and os.path.isdir(os.path.join(e.root + '.old', 'inputs', 'snap-pin')),
          'seal wrote the snapshot into the pinned (original) directory after a swap')
    check(not os.path.lexists(os.path.join(e.root, 'inputs')), 'seal wrote nothing into the directory swapped in at the path')

# claim, void, supersede (a live claim and an admission are prepared in the original directory first)
with tempfile.TemporaryDirectory() as tmp:
    e, view = fresh_swap_env(tmp)
    m1, q, res = e.seal('snap-1')
    ident, sealed = L.source_identity(m1), res['sealedManifestSha256']
    c = with_swap(e, view, lambda: L.claim(ident, 'op-pin', 'snap-1', sealed, precheck('op-pin'), H('b'), **swapped_kw(e, view)))
    check(os.path.isfile(os.path.join(e.root + '.old', 'claims', ident + '.claim')) and not os.path.lexists(os.path.join(e.root, 'claims')),
          'claim read the seal from, and wrote the claim into, the pinned (original) directory after a swap')
    check(c['claim'] == os.path.join(e.root, 'claims', ident + '.claim'), 'the claim path is reported as the operator named it')
    # swap the directory back so the next operations start from a normal layout
    os.rmdir(e.root)
    os.rename(e.root + '.old', e.root)
    os.unlink(view)
    os.symlink(e.root, view)
    L.admit('op-pin', H('1'), ident, H('2'), H('3'), [H('4')], 300, **e.kw())
    v = with_swap(e, view, lambda: L.void(ident, 'op-pin', run_result('op-pin'), H('1'), fin('op-pin'), H('2'), URL, marker_root=e.markers, **swapped_kw(e, view)))
    check(os.path.isfile(os.path.join(e.root + '.old', 'claims', ident + '.claim.voided-op-pin')) and not os.path.lexists(os.path.join(e.root, 'claims')),
          'void renamed the claim inside the pinned (original) directory after a swap')
    os.rmdir(e.root)
    os.rename(e.root + '.old', e.root)
    os.unlink(view)
    os.symlink(e.root, view)
    L.claim(ident, 'op-two', 'snap-1', sealed, precheck('op-two'), H('b'), **e.kw())
    os.makedirs(os.path.join(e.markers, 'op-two'))
    open(os.path.join(e.markers, 'op-two', 'input-read'), 'w').close()
    s2 = with_swap(e, view, lambda: L.supersede(ident, 'op-two', 'op-three', 'snap-1', sealed, precheck('op-three'), H('e'), fin('op-two'), H('f'), URL, 'root decision',
                                                marker_root=e.markers, **swapped_kw(e, view)))
    cl = os.path.join(e.root + '.old', 'claims')
    check(os.path.isfile(os.path.join(cl, ident + '.claim')) and os.path.isfile(os.path.join(cl, ident + '.claim.consumed-op-two')) and not os.path.lexists(os.path.join(e.root, 'claims')),
          'supersede (consume, intent, successor) acted entirely inside the pinned directory after a swap')
    check(L.read_json(os.path.join(cl, ident + '.claim'))[0]['opId'] == 'op-three', 'the nested claim inside supersede used the same pinned directory')

# a swap BEFORE the pin is caught by the inode comparison with the view, for every write function
with tempfile.TemporaryDirectory() as tmp:
    e, view = fresh_swap_env(tmp)
    os.rename(e.root, e.root + '.old')
    os.mkdir(e.root)
    os.chmod(e.root, 0o755)
    os.unlink(view)
    os.symlink(e.root + '.old', view)
    kws_ = swapped_kw(e, view)
    refused(lambda: L.admit('op-x', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, **kws_), 'not the mounted view', 'admit refuses a directory swapped before the pin')
    refused(lambda: L.claim(H('a'), 'op-x', 'snap-1', H('a'), precheck('op-x'), H('b'), **kws_), 'not the mounted view', 'claim refuses a directory swapped before the pin')
    refused(lambda: L.void(H('a'), 'op-x', run_result('op-x'), H('1'), fin('op-x'), H('2'), URL, marker_root=e.markers, **kws_), 'not the mounted view', 'void refuses a directory swapped before the pin')
    refused(lambda: L.supersede(H('a'), 'op-x', 'op-y', 'snap-1', H('a'), precheck('op-y'), H('e'), fin('op-x'), H('f'), URL, 'root decision', marker_root=e.markers, **kws_),
            'not the mounted view', 'supersede refuses a directory swapped before the pin')
    sm1 = L.take_manifest(e.base, SUBS)
    sm2 = copy.deepcopy(sm1)
    sm2['takenUtc'] = (L.parse_utc(sm1['takenUtc']) + timedelta(seconds=301)).isoformat()
    s_now = L.parse_utc(sm1['takenUtc']) + timedelta(seconds=400)
    sq = L.quiesce_verdict(sm1, sm2, [f'{e.base}/{x}' for x in SUBS], INACTIVE, now=s_now)
    refused(lambda: L.seal(e.base, sm1, sq, 'snap-x', measure=INACTIVE, now=s_now, **kws_), 'not the mounted view', 'seal refuses a directory swapped before the pin')
    check(not os.path.lexists(os.path.join(e.root, 'inputs')) and not os.path.lexists(os.path.join(e.root + '.old', 'inputs')), 'no refused write created anything on either side of the swap')

# the pin itself
check(L.PINNED_PREFIX == '/proc/self/fd/', 'the pinned path is anchored on a descriptor')
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    with L.pinned_source(e.root, e.root, e.services, UID) as sr:
        check(sr.startswith('/proc/self/fd/') and os.path.isdir(sr), 'pinned_source yields a descriptor path that resolves to the directory')
        fdn = int(sr.rsplit('/', 1)[1])
        check(os.fstat(fdn).st_ino == os.stat(e.root).st_ino, 'the descriptor is the source directory')
    try:
        os.fstat(fdn)
        check(False, 'the pin is released on exit')
    except OSError:
        check(True, 'the pin is released on exit')
    os.symlink(e.root, os.path.join(e.services, 'sym'))
    refused(lambda: L.open_dir_chain(os.path.join(e.services, 'sym'), e.services, UID), 'not root-safe', 'open_dir_chain refuses a symlink component (O_NOFOLLOW)')
    refused(lambda: L.open_dir_chain(os.path.join(e.services, 'absent'), e.services, UID), 'not root-safe', 'open_dir_chain refuses a missing component')
    refused(lambda: L.open_dir_chain(e.root, os.path.join(e.tmp, 'nowhere'), UID), 'not root-safe', 'open_dir_chain refuses a missing chain start')
    other_start = os.path.join(e.tmp, 'other-start')
    os.mkdir(other_start)
    os.chmod(other_start, 0o755)
    refused(lambda: L.open_dir_chain(e.root, other_start, UID), 'outside the chain start', 'open_dir_chain refuses a path outside an existing chain start')
    os.chmod(e.root, 0o775)
    refused(lambda: L.open_dir_chain(e.root, e.services, UID), 'not root-safe', 'open_dir_chain refuses a group-writable directory (checked on the descriptor)')
    os.chmod(e.root, 0o755)
    # an internal call that already carries a pinned path passes through and does not pin again
    with L.pinned_source(e.root, e.root, e.services, UID) as sr:
        before = len(os.listdir('/proc/self/fd'))
        L.admit('op-nest', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, source_root=sr, view_root=e.root, services=e.services, owner_uid=UID)
        check(len(os.listdir('/proc/self/fd')) == before, 'a call with a pinned path does not open another pin')

# the /proc refusal and the pinned-root assertion (non-blocking items of the review of #5010)
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    real_isdir = os.path.isdir
    try:
        os.path.isdir = lambda p: False if p == '/proc/self/fd' else real_isdir(p)
        refused(lambda: L.admit('op-noproc', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, **e.kw()), '/proc is not available', 'a write function refuses when /proc is not available')
        refused(lambda: L.pinned_source(e.root, e.root, e.services, UID).__enter__(), '/proc is not available', 'pinned_source refuses when /proc is not available')
    finally:
        os.path.isdir = real_isdir
    check(not os.path.lexists(os.path.join(e.root, 'admissions')), 'the /proc refusal created nothing')
    check(L._PINS == {}, 'no pin is registered outside pinned_source')
    with L.pinned_source(e.root, e.root, e.services, UID) as sr:
        L.assert_pinned_root(sr)
        fdn = int(sr.rsplit('/', 1)[1])
        check(L._PINS.get(fdn) == (os.stat(e.root).st_dev, os.stat(e.root).st_ino), 'a live pin is registered with the directory\'s device and inode')
        check(L.admit('op-open', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, source_root=sr, view_root=e.root, services=e.services, owner_uid=UID)['admission'] == sr + '/admissions/op-open.admit',
              'a live pin passes through (the nested-call path)')
    check(L._PINS == {}, 'the pin is deregistered when the body ends')
    # a syntactic variant of a LIVE pin must be refused by the regex itself, not only by the registry
    with L.pinned_source(e.root, e.root, e.services, UID) as live:
        n_ = live.rsplit('/', 1)[1]
        for bad, what in ((live + '\n', 'a trailing newline'), (live + '/', 'a trailing slash'), (live + '/x', 'a sub-path'), (live + '/..', 'a .. tail'),
                          (live + ' ', 'a trailing space'), (f'{L.PINNED_PREFIX}0{n_}', 'a leading zero'), (f'{L.PINNED_PREFIX}{n_}x', 'a suffix'),
                          (f'{L.PINNED_PREFIX}+{n_}', 'a plus sign')):
            refused(lambda b=bad: L.assert_pinned_root(b), 'not a pinned root', f'a live pin with {what} is refused')
            refused(lambda b=bad: L.admit('op-var', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, source_root=b, view_root=e.root, services=e.services, owner_uid=UID),
                    'not a pinned root', f'admit refuses a live pin with {what}')
        check(not os.path.lexists(os.path.join(e.root, 'admissions', 'op-var.admit')), 'no variant of a live pin wrote anything')
    try:
        with L.pinned_source(e.root, e.root, e.services, UID) as sr2:
            raise RuntimeError('boom')
    except RuntimeError:
        pass
    check(L._PINS == {}, 'the pin is deregistered when the body raises')
    # the reviewer's bypass: an UNRELATED open directory descriptor, even a world-writable one, is not a pin
    other = os.path.join(tmp, 'unrelated')
    os.mkdir(other)
    os.chmod(other, 0o777)
    ofd = os.open(other, os.O_RDONLY | os.O_DIRECTORY)
    ffd = os.open(os.path.join(tmp, 'plain-file'), os.O_RDWR | os.O_CREAT, 0o600)
    kwp = dict(view_root='/nonexistent-view', services='/nonexistent-services', owner_uid=UID)
    try:
        for bad, what in ((f'{L.PINNED_PREFIX}{ofd}', 'an unrelated open directory descriptor (world-writable, not a pin)'),
                          (f'{L.PINNED_PREFIX}{ffd}', 'a descriptor that is not a directory'), (f'{L.PINNED_PREFIX}999999', 'a descriptor that is not open'),
                          (f'{L.PINNED_PREFIX}{ofd}/claims', 'a sub-path of a descriptor'), (f'{L.PINNED_PREFIX}{ofd}/..', 'a .. tail'),
                          (f'{L.PINNED_PREFIX}{ofd}/', 'a trailing slash'), (f'{L.PINNED_PREFIX}{ofd}\n', 'a trailing newline'),
                          (f'{L.PINNED_PREFIX}0{ofd}', 'a leading zero'), (f'{L.PINNED_PREFIX}00', 'a double zero'),
                          (L.PINNED_PREFIX, 'an empty tail'), (f'{L.PINNED_PREFIX}abc', 'a non-numeric tail'), (f'{L.PINNED_PREFIX}-1', 'a negative descriptor'),
                          (f'{L.PINNED_PREFIX}{ofd}x', 'a tail with a suffix'), (f'{L.PINNED_PREFIX}9' * 1 + '9' * 40, 'a descriptor number far beyond any limit'),
                          (f'{L.PINNED_PREFIX}' + '9' * 400, 'a 400-digit descriptor number')):
            refused(lambda b=bad: L.admit('op-bad', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, source_root=b, **kwp), 'not a pinned root', f'{what} is refused before anything is written')
            refused(lambda b=bad: L.assert_pinned_root(b), 'not a pinned root', f'{what} is refused by assert_pinned_root')
        check(os.listdir(other) == [], 'nothing was written into the unrelated world-writable directory')
        # a descriptor number reused after the pin closed
        with L.pinned_source(e.root, e.root, e.services, UID) as sr3:
            pfd = int(sr3.rsplit('/', 1)[1])
        fillers = []
        reused = os.open(other, os.O_RDONLY | os.O_DIRECTORY)
        while reused < pfd:                                   # lower free numbers are taken first; walk up to the closed pin's number
            fillers.append(reused)
            reused = os.open(other, os.O_RDONLY | os.O_DIRECTORY)
        for f_ in fillers:
            os.close(f_)
        check(reused == pfd, 'the closed pin\'s descriptor number is reused by an unrelated directory')
        refused(lambda: L.admit('op-stale', H('1'), H('a'), H('2'), H('3'), [H('4')], 300, source_root=f'{L.PINNED_PREFIX}{reused}', **kwp), 'not a live pin', 'a reused descriptor number is refused')
        # a registered number whose descriptor now refers to another directory
        L._PINS[reused] = (os.stat(e.root).st_dev, os.stat(e.root).st_ino)
        refused(lambda: L.assert_pinned_root(f'{L.PINNED_PREFIX}{reused}'), 'no longer refers to the pinned directory', 'a registered descriptor that refers to another directory is refused')
        L._PINS.pop(reused)
        os.close(reused)
        L._PINS[ffd] = (0, 0)
        refused(lambda: L.assert_pinned_root(f'{L.PINNED_PREFIX}{ffd}'), 'is not a directory', 'a registered descriptor that is not a directory is refused')
        L._PINS.pop(ffd)
        L._PINS[999999] = (0, 0)
        refused(lambda: L.assert_pinned_root(f'{L.PINNED_PREFIX}999999'), 'is not open in this process', 'a registered but closed descriptor is refused')
        L._PINS.pop(999999)
        L._PINS[10 ** 30] = (0, 0)
        refused(lambda: L.assert_pinned_root(f'{L.PINNED_PREFIX}{10 ** 30}'), 'is not open in this process', 'a registered descriptor number beyond the OS limit refuses instead of raising OverflowError')
        L._PINS.pop(10 ** 30)
    finally:
        for f_ in (ofd, ffd):
            if f_ >= 0:
                try:
                    os.close(f_)
                except OSError:
                    pass
    check(L._PINS == {}, 'the registry is empty again after the descriptor tests')
    readme = open(os.path.join(os.path.dirname(TOOL), 'README.md')).read()
    check('open_dir_chain' in readme.split('## Admission reader')[1].split('| Id |')[0] and 'pinned reader' in readme and 'assert_pinned_root' in readme,
          'the README operator-copy note covers the pinned reader, open_dir_chain and the pinned-root assertion')
    check('live pin' in readme and 'registry' in readme, 'the README says the pass-through accepts only a live, registered pin')

# check_admission reads through a pinned chain too
with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    ident = H('a')
    L.admit('op-r', H('1'), ident, H('2'), H('3'), [H('4')], 300, now=now, **e.kw())
    base_chk = dict(path_root=e.root, op_id='op-r', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'), image_ids=[H('4')],
                    chain_start=e.services, owner_uid=UID, now=now + timedelta(seconds=5))
    check(L.check_admission(**base_chk) == [], 'check_admission passes through the pinned chain')
    adm_path = os.path.join(e.root, 'admissions', 'op-r.admit')
    os.chmod(adm_path, 0o666)
    check(any('not root-safe' in r for r in L.check_admission(**base_chk)), 'check_admission refuses a group/other-writable admission file')
    os.chmod(adm_path, 0o644)
    os.unlink(adm_path)
    copy_path = os.path.join(e.root, 'admissions', 'valid-copy.admit')
    L.admit('valid-copy', H('1'), ident, H('2'), H('3'), [H('4')], 300, now=now, **e.kw())
    body = json.load(open(copy_path))
    body['opId'] = 'op-r'
    json.dump(body, open(copy_path, 'w'))
    os.symlink(copy_path, adm_path)                     # a symlink to a perfectly valid admission for op-r
    check(L.check_admission(**base_chk) == ['admission is absent or unreadable'], 'MUTANT: check_admission does not follow a symlinked admission file (O_NOFOLLOW)')
    os.unlink(adm_path)
    shutil.copyfile(copy_path, adm_path)
    os.chmod(adm_path, 0o644)
    check(L.check_admission(**base_chk) == [], 'a regular copy of the same admission passes')


# CONTROL for the clock: the tool's own clock is skewed two minutes ahead (an artificial delay between the vector's `now` and the moment the tool stamps an admission).
# Stamped from the frozen `now`, the admission still passes; stamped from the tool's clock it is "dated in the future" for a check at now + 5 s, which is what made the
# vector depend on how long the suite took.
class Skewed(datetime):
    @classmethod
    def now(cls, tz=None):
        return datetime.now(tz) + timedelta(seconds=120)


with tempfile.TemporaryDirectory() as tmp_c:
    ec = Env(tmp_c)
    ident_c = H('a')
    real_dt = L.datetime
    L.datetime = Skewed
    try:
        L.admit('op-frozen', H('1'), ident_c, H('2'), H('3'), [H('4')], 300, now=now, **ec.kw())
        L.admit('op-wall', H('1'), ident_c, H('2'), H('3'), [H('4')], 300, **ec.kw())
    finally:
        L.datetime = real_dt
    chk_c = dict(path_root=ec.root, argv_sha=H('1'), ident=ident_c, operator_sha=H('2'), finalizer_sha=H('3'), image_ids=[H('4')], chain_start=ec.services, owner_uid=UID,
                 now=now + timedelta(seconds=5))
    check(L.check_admission(op_id='op-frozen', **chk_c) == [], 'control: an admission stamped from the frozen `now` passes whatever the tool\'s clock says')
    check(any('dated in the future' in r for r in L.check_admission(op_id='op-wall', **chk_c)), 'control: an admission stamped from a clock two minutes ahead fails the same check (the old vector\'s failure mode)')
    real_fstat = os.fstat

    def fstat_foreign(fd):
        st = real_fstat(fd)
        if stat.S_ISREG(st.st_mode):
            vals = list(st)
            vals[4] = st.st_uid + 1
            return os.stat_result(vals)
        return st
    try:
        os.fstat = fstat_foreign
        check(any('not root-safe' in r for r in L.check_admission(**base_chk)), 'MUTANT: check_admission refuses an admission file owned by another uid')
    finally:
        os.fstat = real_fstat

# ---- CLI argument handling
check(L._args(['claim', '--op-id', 'a', '--image-id', 's1', '--image-id', 's2']) == {'--op-id': 'a', '--image-id': ['s1', 's2']}, 'repeatable arguments are collected')
refused(lambda: L._args(['claim', '--op-id', 'a', '--op-id', 'b']), 'given twice', 'a repeated single argument is refused')
refused(lambda: L._args(['claim', 'oops']), 'bad arguments', 'a stray argument is refused')
check(not any(a in open(TOOL).read().split('def main')[1] for a in ("'--subtree'", "'--base'")), 'the CLI takes no subtree or base argument: the source is pinned')

src_all = open(TOOL).read()
check('a consumed claim exists; supersede it' not in src_all and '_consumed_signals' not in src_all, 'the unreachable consumed guard in claim is removed')
check('--operator-exit-code' in src_all, 'the CLI offers --operator-exit-code')

print(f'learn-seal-claim: {n} assertions passed')

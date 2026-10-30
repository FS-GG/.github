#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/seal/learn_seal_claim.py (Refs FS-GG/.github#4647).

Drives the library functions the root CLI calls, against temporary trees owned by the running user (the tool takes the
owner uid as a parameter; the CLI fixes it to 0 and refuses to run unless root). stdlib only, offline, no container.
Prints its own assertion count."""
import contextlib, copy, importlib.util, io, json, os, shutil, sys, tempfile
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
TOOL = os.path.join(os.path.dirname(os.path.dirname(HERE)), 'deployment', 'learn-finite-profile', 'seal', 'learn_seal_claim.py')
spec = importlib.util.spec_from_file_location('lsc', TOOL)
L = importlib.util.module_from_spec(spec)
spec.loader.exec_module(L)
UID = os.getuid()
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
    except L.Refuse as e:
        if needle not in str(e):
            print(f'FAIL: {what}: refused with "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: not refused')
    sys.exit(1)


class Env:
    """services/<root> stands for /srv/bulk/services/usr-local-libexec-fsgg-learn; the view is the same directory."""
    def __init__(self, tmp):
        self.tmp = tmp
        self.services = os.path.join(tmp, 'services')
        self.root = os.path.join(self.services, 'learn')
        os.makedirs(self.root)
        os.chmod(self.services, 0o755)
        os.chmod(self.root, 0o755)
        self.base = os.path.join(tmp, 'telemetry-spool')
        self.subs = ['passive-b/spool', 'passive-a/spool']
        for s, files in ((self.subs[0], {'one.ready': b'{"a":1}\n', 'x/y.json': b'y' * 300}), (self.subs[1], {'cur': b'abc', 'z.json': b'zz'})):
            for rel, data in files.items():
                p = os.path.join(self.base, s, rel)
                os.makedirs(os.path.dirname(p), exist_ok=True)
                open(p, 'wb').write(data)
                os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
        self.custody = os.path.join(tmp, 'custody')
        os.makedirs(self.custody)

    def kw(self):
        return dict(source_root=self.root, view_root=self.root, services=self.services, owner_uid=UID)


def precheck(op, **over):
    r = {'schema': L.OPERATOR_RESULT_SCHEMA, 'opId': op, 'mode': '--precheck-only', 'exitCode': 0, 'errors': [],
         'gates': {'inputMounted': False}}
    r.update(over)
    return r


def fin(op, **over):
    r = {'schema': 'learn-op-success/1', 'opId': op, 'inputRead': False, 'containersStartedWithInput': []}
    r.update(over)
    return r


def opres(op, **over):
    r = {'schema': L.OPERATOR_RESULT_SCHEMA, 'opId': op, 'gates': {'inputMounted': False}, 'containers': [
        {'name': 'pg', 'mounts': [{'destination': '/pgdata'}]}, {'name': 'feed', 'mounts': [{'destination': '/work'}]}]}
    r.update(over)
    return r


H = lambda c: c * 64
now = datetime.now(timezone.utc)

with tempfile.TemporaryDirectory() as tmp:
    e = Env(tmp)
    # ---- manifest and identity
    m1 = L.take_manifest(e.base, e.subs)
    check(m1['subtrees'] == sorted(e.subs) and len(m1['files']) == 4, 'manifest lists both subtrees and 4 files')
    check([f['path'] for f in m1['files']] == sorted(f['path'] for f in m1['files']), 'files are sorted')
    check(all(f['size'] == os.path.getsize(os.path.join(e.base, f['path'])) for f in m1['files']), 'sizes are apparent bytes')
    check(all(f['mtime_ns'] == 1_700_000_123_000_000_000 for f in m1['files']), 'mtime_ns is recorded')
    ident = L.source_identity(m1)
    check(len(ident) == 64 and ident == L.source_identity(L.take_manifest(e.base, list(reversed(e.subs)))), 'identity is deterministic and order independent')
    p = os.path.join(e.base, e.subs[1], 'cur')
    open(p, 'wb').write(b'abd')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, e.subs)) != ident, 'identity changes with content')
    open(p, 'wb').write(b'abc')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_999_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, e.subs)) != ident, 'identity changes with mtime')
    os.utime(p, ns=(1_700_000_000_000_000_000, 1_700_000_123_000_000_000))
    check(L.source_identity(L.take_manifest(e.base, e.subs)) == ident, 'identity is restored with the original bytes and mtime')
    check(L.source_identity(L.take_manifest(e.base, e.subs[:1])) != ident, 'identity depends on the subtree set')
    refused(lambda: L.take_manifest(e.base, ['../x']), 'relative path without ..', 'a .. subtree is refused')
    refused(lambda: L.take_manifest(e.base, ['/etc']), 'relative path without ..', 'an absolute subtree is refused')
    refused(lambda: L.take_manifest(e.base, ['nope/spool']), 'not a real directory', 'a missing subtree is refused')
    os.symlink('one.ready', os.path.join(e.base, e.subs[0], 'link'))
    refused(lambda: L.take_manifest(e.base, e.subs), 'symlink, fifo, device, socket', 'a symlink in the subtree is refused')
    os.unlink(os.path.join(e.base, e.subs[0], 'link'))
    os.mkfifo(os.path.join(e.base, e.subs[0], 'fifo'))
    refused(lambda: L.take_manifest(e.base, e.subs), 'symlink, fifo, device, socket', 'a fifo in the subtree is refused')
    os.unlink(os.path.join(e.base, e.subs[0], 'fifo'))
    big = L.MAX_FILES
    for i in range(big):
        open(os.path.join(e.base, e.subs[0], f'f{i}'), 'w').write('x')
    refused(lambda: L.take_manifest(e.base, e.subs), 'file or byte bound', 'too many files is refused')
    for i in range(big):
        os.unlink(os.path.join(e.base, e.subs[0], f'f{i}'))

    m1 = L.take_manifest(e.base, e.subs)      # the experiments above touched ctime; the real flow takes M1 once, after them
    ident = L.source_identity(m1)
    # ---- quiesce
    m2 = copy.deepcopy(m1)
    m2['takenUtc'] = (datetime.fromisoformat(m1['takenUtc']) + timedelta(seconds=301)).isoformat()
    dirs = [os.path.join(e.base, s) for s in e.subs]
    v = L.quiesce_verdict(m1, m2, dirs, False)
    check(v['ok'] and v['reasons'] == [] and v['sourceIdentity'] == ident, 'a stable source past the gap is quiescent')
    check(not L.quiesce_verdict(m1, m2, dirs, True)['ok'], 'an active timer is refused')
    m2s = copy.deepcopy(m2)
    m2s['takenUtc'] = (datetime.fromisoformat(m1['takenUtc']) + timedelta(seconds=299)).isoformat()
    check('299s apart' in ' '.join(L.quiesce_verdict(m1, m2s, dirs, False)['reasons']), 'a gap under 300 s is refused')
    for field, val in (('size', 1), ('sha256', H('0')), ('mtime_ns', 5), ('ctime_ns', 5), ('ino', 5), ('mode', '0o600')):
        mm = copy.deepcopy(m2)
        mm['files'][0][field] = val
        check(any(field in r for r in L.quiesce_verdict(m1, mm, dirs, False)['reasons']), f'a {field} difference between M1 and M2 is refused')
    mm = copy.deepcopy(m2)
    mm['files'].pop()
    check(any('only one manifest' in r for r in L.quiesce_verdict(m1, mm, dirs, False)['reasons']), 'a file missing from M2 is refused')
    fh = open(os.path.join(e.base, e.subs[0], 'one.ready'), 'rb')
    vo = L.quiesce_verdict(m1, m2, dirs, False)
    check(not vo['ok'] and any('open under' in r and str(os.getpid()) in r for r in vo['reasons']), 'an open file under a subtree is detected by pid')
    fh.close()
    check(L.quiesce_verdict(m1, m2, dirs, False)['ok'], 'closing the file clears the finding')

    # ---- seal
    q = L.quiesce_verdict(m1, m2, dirs, False)
    res = L.seal(e.base, e.subs, m1, q, 'snap-1', **e.kw())
    snap = os.path.join(e.root, 'inputs', 'snap-1')
    check(res['files'] == 4 and res['sourceIdentity'] == ident, 'seal copies 4 files and keeps the source identity')
    sm, sm_sha = L.read_json(os.path.join(e.root, 'inputs', 'snap-1.manifest.json'))
    check(sm['schema'] == 'learn-input-snapshot/1' and sm_sha == res['sealedManifestSha256'], 'the sealed manifest hash is the one returned')
    check(all(L.sha_file(os.path.join(snap, f['path'])) == f['sha256'] for f in sm['files']), 'every sealed file hashes to the manifest')
    check(all(os.stat(os.path.join(snap, f['path'])).st_mtime_ns == f['mtime_ns'] for f in sm['files']), 'mtimes are preserved')
    check(all(oct(os.stat(os.path.join(snap, f['path'])).st_mode & 0o777) == '0o644' for f in sm['files']), 'sealed files are 0644')
    refused(lambda: L.seal(e.base, e.subs, m1, q, 'snap-1', **e.kw()), 'snapshot already exists', 'an existing snapshot is not overwritten')
    refused(lambda: L.seal(e.base, e.subs, m1, dict(q, ok=False), 'snap-2', **e.kw()), 'quiesce proof', 'a failed quiesce proof is refused')
    refused(lambda: L.seal(e.base, e.subs, m1, dict(q, m1Sha256=H('1')), 'snap-2', **e.kw()), 'quiesce proof', 'a proof for another manifest is refused')
    refused(lambda: L.seal(e.base, e.subs, m1, q, 'Bad Id', **e.kw()), 'bad snapshot id', 'a bad snapshot id is refused')

    def mutate_copy(src, dst):
        shutil.copyfile(src, dst)
        open(src, 'ab').write(b'!')          # the source changes during the copy
    refused(lambda: L.seal(e.base, e.subs, m1, q, 'snap-3', copy=mutate_copy, **e.kw()), 'changed during the copy', 'a source that changes during the copy is refused')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-3')), 'a refused seal leaves no snapshot directory')
    check(not os.path.lexists(os.path.join(e.root, 'inputs', 'snap-3.manifest.json')), 'a refused seal leaves no sealed manifest')

    # ---- write-path guards
    os.chmod(e.services, 0o777)
    refused(lambda: L.seal(e.base, e.subs, m1, q, 'snap-4', **e.kw()), 'not root-safe', 'a world-writable services directory is refused')
    os.chmod(e.services, 0o755)
    kw = e.kw(); kw['owner_uid'] = UID + 1
    refused(lambda: L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **kw), 'not root-safe', 'a directory owned by another uid is refused')
    other = os.path.join(tmp, 'other'); os.mkdir(other)
    kw = e.kw(); kw['view_root'] = other
    refused(lambda: L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **kw), 'not the mounted view', 'a source directory that is not the mounted view is refused')
    kw = e.kw(); kw['view_root'] = os.path.join(tmp, 'absent')
    refused(lambda: L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **kw), 'mounted view is absent', 'an absent view is refused')
    swapped = os.path.join(tmp, 'services2'); os.mkdir(swapped); os.chmod(swapped, 0o755)
    os.symlink(e.root, os.path.join(swapped, 'learn'))
    kw = dict(source_root=os.path.join(swapped, 'learn'), view_root=e.root, services=swapped, owner_uid=UID)
    refused(lambda: L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **kw), 'not root-safe', 'a symlinked source directory is refused')

    # ---- claim
    for bad, needle, what in ((precheck('op-one', mode='--run'), 'did not pass', 'a run result is not a precheck'),
                              (precheck('op-one', exitCode=5), 'did not pass', 'a failed precheck is refused'),
                              (precheck('op-one', errors=['x']), 'did not pass', 'a precheck with errors is refused'),
                              (precheck('op-one', gates={'inputMounted': True}), 'input untouched', 'a precheck that touched the input is refused'),
                              (precheck('op-one', gates={}), 'input untouched', 'a precheck without the input gate is refused'),
                              (precheck('op-two'), 'another op', 'a precheck for another op is refused'),
                              (precheck('op-one', schema='x/1'), 'another schema', 'a precheck with another schema is refused')):
        refused(lambda b=bad: L.claim(ident, 'op-one', H('a'), b, H('b'), **e.kw()), needle, what)
    refused(lambda: L.claim('zz', 'op-one', H('a'), precheck('op-one'), H('b'), **e.kw()), 'source identity', 'a bad identity is refused')
    refused(lambda: L.claim(ident, 'op-one', 'zz', precheck('op-one'), H('b'), **e.kw()), 'sealed manifest sha256', 'a bad sealed hash is refused')
    c1 = L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **e.kw())
    cj, _ = L.read_json(c1['claim'])
    check(os.path.basename(c1['claim']) == ident + '.claim', 'the claim is named by the source identity')
    check(cj['sourceIdentity'] == ident and cj['opId'] == 'op-one' and cj['generation'] == 1 and cj['supersedes'] is None, 'the claim records identity, op, generation 1')
    check(cj['sealedManifestSha256'] == H('a') and cj['precheckResultSha256'] == H('b'), 'the claim records the sealed manifest and precheck hashes')
    refused(lambda: L.claim(ident, 'op-one', H('a'), precheck('op-one'), H('b'), **e.kw()), 'already exists', 'a second claim for the same op is refused')
    refused(lambda: L.claim(ident, 'op-two', H('a'), precheck('op-two'), H('b'), **e.kw()), 'already exists', 'a second op cannot claim the same source')
    refused(lambda: L.claim(ident, 'op-two', H('c'), precheck('op-two'), H('b'), **e.kw()), 'already exists', 'a re-sealed copy (other manifest hash) cannot claim the same source')
    check(L.status(ident, e.root)['state'] == 'claimed', 'status reports claimed')
    check(os.stat(c1['claim']).st_mode & 0o222 == 0o200, 'the claim is not group or other writable')

    # ---- void
    cust = e.custody
    ok_kw = dict(custody_root=cust, **e.kw())
    refused(lambda: L.void(ident, 'op-two', opres('op-two'), H('1'), fin('op-two'), H('2'), **ok_kw), 'names another op', 'void of another op is refused')
    os.makedirs(os.path.join(cust, 'op-one'))
    open(os.path.join(cust, 'op-one', 'input-read'), 'w').close()
    refused(lambda: L.void(ident, 'op-one', opres('op-one'), H('1'), fin('op-one'), H('2'), **ok_kw), 'input-read marker exists', 'the input-read marker blocks a void')
    os.unlink(os.path.join(cust, 'op-one', 'input-read'))
    refused(lambda: L.void(ident, 'op-one', opres('op-one', gates={'inputMounted': True}), H('1'), fin('op-one'), H('2'), **ok_kw), 'input unmounted', 'a mounted input blocks a void')
    mounted = opres('op-one')
    mounted['containers'][1]['mounts'].append({'destination': '/in'})
    refused(lambda: L.void(ident, 'op-one', mounted, H('1'), fin('op-one'), H('2'), **ok_kw), 'container with /in', 'a container with /in blocks a void')
    nolist = opres('op-one'); del nolist['containers']
    refused(lambda: L.void(ident, 'op-one', nolist, H('1'), fin('op-one'), H('2'), **ok_kw), 'lacks the container list', 'a missing container list blocks a void')
    refused(lambda: L.void(ident, 'op-one', opres('op-one', schema='x/1'), H('1'), fin('op-one'), H('2'), **ok_kw), 'another schema', 'an operator result with another schema blocks a void')
    refused(lambda: L.void(ident, 'op-one', opres('op-one'), H('1'), fin('op-one', inputRead=True), H('2'), **ok_kw), 'inputRead false', 'a finalizer record with inputRead true blocks a void')
    refused(lambda: L.void(ident, 'op-one', opres('op-one'), H('1'), fin('op-one', containersStartedWithInput=['feed']), H('2'), **ok_kw), 'containersStartedWithInput', 'a container started with the input blocks a void')
    noinput = fin('op-one'); del noinput['inputRead']
    refused(lambda: L.void(ident, 'op-one', opres('op-one'), H('1'), noinput, H('2'), **ok_kw), 'inputRead false', 'a finalizer record without inputRead blocks a void')
    refused(lambda: L.void(ident, 'op-one', opres('op-one'), H('1'), fin('op-one', schema='x/1'), H('2'), **ok_kw), 'another schema', 'a finalizer record with another schema blocks a void')
    check(os.path.isfile(c1['claim']), 'every refused void leaves the claim in place')
    vres = L.void(ident, 'op-one', opres('op-one'), H('1'), fin('op-one'), H('2'), **ok_kw)
    vr, _ = L.read_json(os.path.join(e.root, 'claims', f'{ident}.void-op-one.json'))
    check(vr['operatorResultSha256'] == H('1') and vr['finalizerRecordSha256'] == H('2') and vr['claimSha256'] == vres['claimSha256'], 'the void record carries the evidence hashes')
    check('never mounted' in vr['proves'] and 'never read' in vr['doesNotProve'], 'the void record states what it proves and does not prove')
    check(not os.path.isfile(c1['claim']) and os.path.isfile(c1['claim'] + '.voided-op-one'), 'the voided claim is renamed, not deleted')
    check(L.status(ident, e.root)['state'] == 'voided', 'status reports voided')
    c2 = L.claim(ident, 'op-two', H('c'), precheck('op-two'), H('b'), **e.kw())
    check(L.read_json(c2['claim'])[0]['opId'] == 'op-two', 'after a void the source can be claimed again')

    # ---- supersede (op-two claim is live; it is consumed when the marker exists)
    sup_kw = lambda **o: dict(ident=ident, old_op='op-two', new_op='op-three', sealed_sha=H('d'), precheck=precheck('op-three'), precheck_sha=H('e'),
                              old_finalizer=fin('op-two'), old_fin_sha=H('f'), decision_ref='https://github.com/FS-GG/.github/issues/4647#issuecomment-1',
                              reason='root decision to re-run', custody_root=cust, **e.kw()) | o
    refused(lambda: L.supersede(**sup_kw()), 'void it instead', 'a voidable claim cannot be superseded')
    os.makedirs(os.path.join(cust, 'op-two'))
    open(os.path.join(cust, 'op-two', 'input-read'), 'w').close()      # now consumed
    refused(lambda: L.supersede(**sup_kw(decision_ref='no')), 'decision reference', 'supersede needs a Root decision reference')
    refused(lambda: L.supersede(**sup_kw(reason='')), 'reason', 'supersede needs a reason')
    refused(lambda: L.supersede(**sup_kw(old_op='op-nine')), 'names another op', 'supersede of another op is refused')
    refused(lambda: L.supersede(**sup_kw(old_finalizer=fin('op-nine'))), 'another op', 'a finalizer record for another op is refused')
    refused(lambda: L.supersede(**sup_kw(precheck=precheck('op-three', gates={'inputMounted': True}))), 'input untouched', 'supersede needs a clean precheck for the new op')
    check(os.path.isfile(c2['claim']), 'refused supersedes leave the live claim in place')
    sres = L.supersede(**sup_kw())
    live = L.read_json(c2['claim'])[0]
    check(live['opId'] == 'op-three' and live['generation'] == 2 and live['supersedes'] == 'op-two', 'the successor claim is generation 2 and names what it supersedes')
    check(os.path.isfile(c2['claim'] + '.consumed-op-two'), 'the consumed claim is kept')
    sr, _ = L.read_json(os.path.join(e.root, 'claims', f'{ident}.supersede-op-two-to-op-three.json'))
    check(sr['oldFinalizerRecordSha256'] == H('f') and sr['decisionRef'].startswith('https://github.com/FS-GG/') and sr['generationFrom'] == 1, 'the supersede record carries the evidence and the decision')
    check(L.status(ident, e.root)['state'] == 'claimed', 'status reports the successor claim')
    refused(lambda: L.claim(ident, 'op-four', H('a'), precheck('op-four'), H('b'), **e.kw()), 'already exists', 'a fresh claim at the same name still hits EEXIST')
    # an interrupted supersede (intent and rename done, successor missing) is visible
    os.rename(c2['claim'], c2['claim'] + '.consumed-op-three')
    open(os.path.join(e.root, 'claims', f'{ident}.supersede-op-three-to-op-four.json'), 'w').write('{}')
    check(L.status(ident, e.root)['state'] == 'supersede-incomplete', 'status reports an interrupted supersede')

    # ---- admission
    adm = dict(op_id='op-adm', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'), image_ids=[H('5'), H('4')], ttl=300, now=now)
    refused(lambda: L.admit(**dict(adm, ttl=601), **e.kw()), 'ttl must be', 'a ttl over 600 s is refused')
    refused(lambda: L.admit(**dict(adm, ttl=0), **e.kw()), 'ttl must be', 'a ttl of 0 is refused')
    refused(lambda: L.admit(**dict(adm, image_ids=[]), **e.kw()), 'at least one image', 'an admission without an image id is refused')
    refused(lambda: L.admit(**dict(adm, argv_sha='zz'), **e.kw()), '64 lowercase hex', 'a malformed pin is refused')
    a1 = L.admit(**adm, **e.kw())
    refused(lambda: L.admit(**adm, **e.kw()), 'already exists', 'a second admission for the same op is refused')
    chk = dict(path_root=e.root, op_id='op-adm', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'),
               image_ids=[H('4'), H('5')], chain_start=e.services, owner_uid=UID, now=now + timedelta(seconds=10))
    check(L.check_admission(**chk) == [], 'a matching, unexpired admission passes')
    for key, bad in (('argv_sha', H('9')), ('ident', H('9')), ('operator_sha', H('9')), ('finalizer_sha', H('9')), ('image_ids', [H('4')])):
        check(len(L.check_admission(**dict(chk, **{key: bad}))) == 1, f'a different {key} is refused')
    check('expired' in ' '.join(L.check_admission(**dict(chk, now=now + timedelta(seconds=301)))), 'an expired admission is refused')
    check(L.check_admission(**dict(chk, op_id='op-nope')) != [], 'an absent admission is refused')
    os.chmod(e.root, 0o777)
    check('not root-safe' in ' '.join(L.check_admission(**chk)), 'an admission under a world-writable directory is refused')
    os.chmod(e.root, 0o755)

# ---- CLI argument handling and the root-only rule
check(L._args(['claim', '--op-id', 'a', '--subtree', 's1', '--subtree', 's2']) == {'--op-id': 'a', '--subtree': ['s1', 's2']}, 'repeatable arguments are collected')
refused(lambda: L._args(['claim', '--op-id', 'a', '--op-id', 'b']), 'given twice', 'a repeated single argument is refused')
refused(lambda: L._args(['claim', 'oops']), 'bad arguments', 'a stray argument is refused')
if os.geteuid() != 0:
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code = L.main(['status', '--source-identity', H('a')])
    check(code == 5 and 'run as root' in out.getvalue(), 'the CLI refuses to run unless root')

print(f'learn-seal-claim: {n} assertions passed')

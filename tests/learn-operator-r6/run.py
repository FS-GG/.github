#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/operator/learn-operator-r6.py (Refs FS-GG/.github#4647).

r6 is operator r5 (the imported baseline) plus a small reviewed diff. This suite:
  * pins the size of that diff against the imported r5 (a drift guard);
  * tests the changed functions in isolation (extracted by AST from the r6 source, so the test runs the shipped bytes);
  * drives the whole script end to end through its refusal gates with a scripted fake of `subprocess.run`, after substituting the
    machine paths and pins for temporary fixtures. Every substitution must match exactly once, and the fixtures are only paths and
    hashes: the logic under test is r6's.
stdlib only, offline, no container, no root, no systemd. Prints its own assertion count."""
import ast, contextlib, difflib, hashlib, io, json, os, re, stat, sys, tempfile, time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
R6 = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'operator', 'learn-operator-r6.py')
R5 = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', 'libexec', 'learn-operator-r5.py')
SRC = open(R6).read()
UID = os.getuid()
n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def load(names, **extra):
    """Execute only the named top-level functions of the r6 source (the shipped bytes) in a controlled namespace."""
    tree = ast.parse(SRC)
    nodes = [x for x in tree.body if isinstance(x, ast.FunctionDef) and x.name in names]
    check({x.name for x in nodes} == set(names), f'r6 defines {sorted(names)}')
    ns = {'os': os, 'stat': stat, 'json': json, 'hashlib': hashlib, 'time': time, '__builtins__': __builtins__}
    ns.update(extra)
    exec(compile(ast.Module(nodes, []), R6, 'exec'), ns)
    return ns


# ============================================================ drift guard against the imported r5
HUNKS, REMOVED, ADDED = 13, 20, 115                                      # pinned; see the README
a = open(R5).read().splitlines()
b = SRC.splitlines()
ops = [o for o in difflib.SequenceMatcher(None, a, b, autojunk=False).get_opcodes() if o[0] != 'equal']
removed, added = sum(o[2] - o[1] for o in ops), sum(o[4] - o[3] for o in ops)
check(hashlib.sha256(open(R5, 'rb').read()).hexdigest().startswith('318edb4e'), 'the imported baseline is the admitted r5 (318edb4e)')
if not os.environ.get('LEARN_R6_SKIP_DRIFT_GUARD'):                  # set only by the mutation analysis, so line-count changes are not what kills a mutant
  check((len(ops), removed, added) == (HUNKS, REMOVED, ADDED), f'r6 differs from r5 in exactly {HUNKS} hunks, {REMOVED} lines removed, {ADDED} added (got {len(ops)}, {removed}, {added})')
for kept in ('IMAGE_ID = ', 'FIN = ', 'SECCOMP = ', 'PY_SHA = ', 'MAX_ACTIVE_OPS = 2', 'EVID_MAX_BYTES, EVID_MAX_ENTRIES', 'SLICE_MEMORY_MAX, SLICE_PIDS_MAX', 'HOST_UID_1000'):
    l5 = [x for x in a if x.startswith(kept)]
    check(l5 and l5 == [x for x in b if x.startswith(kept)], f'r6 keeps the r5 line {kept!r} byte for byte')
check("'operatorRevision': 'r6'" in SRC and "ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'" in SRC and '/etc/fsgg/learn/retirement-acks' not in SRC.split('"""', 2)[2].replace('(r5: /etc/fsgg/learn/retirement-acks, lost on image upgrade)', ''),
      'r6 names itself r6 and reads acknowledgements from the persistent bind, not /etc')

# ============================================================ read_ack_pinned: the pinned, descriptor-checked reader
RAP = load(['read_ack_pinned'])['read_ack_pinned']


def acktree(tmp):
    base = os.path.join(tmp, 'top')
    d = os.path.join(base, 'a', 'retirement-acks')
    os.makedirs(d)
    for x in (base, os.path.join(base, 'a'), d):
        os.chmod(x, 0o755)
    f = os.path.join(d, 'op.retirement-ack')
    open(f, 'w').write('abc\n')
    os.chmod(f, 0o644)
    return base, d, f


with tempfile.TemporaryDirectory() as tmp:
    base, d, f = acktree(tmp)
    check(RAP(f, base, UID) == b'abc\n', 'a safe acknowledgement is read through the pinned chain')
    check(RAP(f, base, UID + 1) is None, 'a chain owned by another uid yields nothing')
    check(RAP(f, os.path.join(tmp, 'elsewhere'), UID) is None, 'a path outside the chain start yields nothing')
    # a sibling whose name merely starts with the chain start must not be re-rooted under it
    sib_start = os.path.join(tmp, 'top2')
    os.makedirs(os.path.join(sib_start, 'x', 'retirement-acks'))
    for q in (sib_start, os.path.join(sib_start, 'x'), os.path.join(sib_start, 'x', 'retirement-acks')):
        os.chmod(q, 0o755)
    open(os.path.join(sib_start, 'x', 'retirement-acks', 'op.retirement-ack'), 'w').write('inside\n')
    os.chmod(os.path.join(sib_start, 'x', 'retirement-acks', 'op.retirement-ack'), 0o644)
    check(RAP(os.path.join(tmp, 'top2x', 'retirement-acks', 'op.retirement-ack'), sib_start, UID) is None,
          'a path whose name only begins with the chain start is not re-rooted under it')
    check(RAP(os.path.join(d, 'absent.retirement-ack'), base, UID) is None, 'a missing acknowledgement yields nothing')
    os.chmod(f, 0o666)
    check(RAP(f, base, UID) is None, 'a world-writable acknowledgement yields nothing')
    os.chmod(f, 0o664)
    check(RAP(f, base, UID) is None, 'a group-writable acknowledgement yields nothing')
    os.chmod(f, 0o644)
    for unsafe in (d, os.path.join(base, 'a'), base):
        os.chmod(unsafe, 0o777)
        check(RAP(f, base, UID) is None, f'a world-writable directory in the chain ({os.path.relpath(unsafe, tmp)}) yields nothing')
        os.chmod(unsafe, 0o755)
    os.unlink(f)
    target = os.path.join(d, 'valid-target')
    open(target, 'w').write('abc\n')
    os.chmod(target, 0o644)
    os.symlink(target, f)
    check(RAP(target, base, UID) == b'abc\n' and RAP(f, base, UID) is None, 'a symlink to a perfectly valid acknowledgement yields nothing (O_NOFOLLOW)')
    os.unlink(f)
    os.mkfifo(f)
    import signal
    class Blocked(BaseException):                      # not an OSError, so read_ack_pinned cannot swallow it
        pass
    signal.signal(signal.SIGALRM, lambda *a: (_ for _ in ()).throw(Blocked('read_ack_pinned blocked on a FIFO')))
    signal.alarm(5)
    try:
        check(RAP(f, base, UID) is None, 'a FIFO in place of the acknowledgement yields nothing and does not block (O_NONBLOCK)')
    finally:
        signal.alarm(0)
    os.unlink(f)
    # the owner of a DIRECTORY and of the FILE are checked separately (fstat patched so only one of them belongs to someone else)
    open(f, 'w').write('abc\n')
    os.chmod(f, 0o644)
    real_fstat = os.fstat

    def foreign(kind):
        def fs(fd):
            st = real_fstat(fd)
            if (kind == 'dir') == stat.S_ISDIR(st.st_mode):
                vals = list(st)
                vals[4] = st.st_uid + 1
                return os.stat_result(vals)
            return st
        return fs
    try:
        for kind in ('dir', 'file'):
            os.fstat = foreign(kind)
            check(RAP(f, base, UID) is None, f'an acknowledgement chain whose {kind} belongs to another uid yields nothing')
    finally:
        os.fstat = real_fstat
    check(RAP(f, base, UID) == b'abc\n', 'with the real owners the same acknowledgement is read')
    os.unlink(f)
    open(f, 'w').write('x' * 4097)
    check(RAP(f, base, UID) is None, 'an acknowledgement over 4096 bytes yields nothing')
    os.unlink(f)
    open(f, 'w').write('x' * 4096)
    check(RAP(f, base, UID) == b'x' * 4096, 'an acknowledgement of exactly 4096 bytes is read')
    os.unlink(f)
    os.mkdir(f)
    check(RAP(f, base, UID) is None, 'a directory in place of the acknowledgement yields nothing')
    os.rmdir(f)
    open(f, 'w').write('abc\n')
    os.chmod(f, 0o644)
    moved = os.path.join(base, 'a-real')
    os.rename(os.path.join(base, 'a'), moved)
    os.symlink(moved, os.path.join(base, 'a'))
    check(RAP(f, base, UID) is None, 'a symlinked directory in the chain yields nothing (O_NOFOLLOW)')
    ffd_before = len(os.listdir('/proc/self/fd'))
    RAP(f, base, UID)
    check(len(os.listdir('/proc/self/fd')) == ffd_before, 'the reader leaks no descriptor')

# ============================================================ retirement_gate: persistent, pinned, content-bound
RG_NS = load(['read_ack_pinned', 'read_record_nofollow', 'retirement_gate'], ACK_DIR='/nonexistent/default-ack-dir')
RG = RG_NS['retirement_gate']


def evidence(tmp, records):
    ev = os.path.join(tmp, 'evidence')
    os.makedirs(ev)
    for name, body in records.items():
        os.makedirs(os.path.join(ev, name))
        if body is not None:
            open(os.path.join(ev, name, 'record.json'), 'wb').write(body)
    return ev


FAIL = b'{"schema":"learn-op-retirement-failure/1","x":1}'
OK = b'{"schema":"learn-op-success/1"}'
with tempfile.TemporaryDirectory() as tmp:
    base = os.path.join(tmp, 'top')
    acks = os.path.join(base, 'retirement-acks')
    os.makedirs(acks)
    for x in (base, acks):
        os.chmod(x, 0o755)
    ev = evidence(tmp, {'a-fail': FAIL, 'b-ok': OK, 'c-junk': b'not json', 'd-none': None})
    want = hashlib.sha256(FAIL).hexdigest()
    res, unacked = RG(ev, acks, base, UID)
    check(res['failures'] == ['a-fail', 'c-junk'] and unacked == ['a-fail', 'c-junk'], 'a failure record and an unreadable record are unacknowledged without acks')
    check(res['ackDir'] == acks, 'the gate reports the ack directory it used')
    open(os.path.join(acks, 'a-fail.retirement-ack'), 'w').write(want + '\n')
    os.chmod(os.path.join(acks, 'a-fail.retirement-ack'), 0o644)
    res, unacked = RG(ev, acks, base, UID)
    check(res['acked'] == ['a-fail'] and unacked == ['c-junk'], 'an acknowledgement with the record\'s sha256 acknowledges it')
    open(os.path.join(acks, 'c-junk.retirement-ack'), 'w').write('0' * 64 + '\n')
    os.chmod(os.path.join(acks, 'c-junk.retirement-ack'), 0o644)
    res, unacked = RG(ev, acks, base, UID)
    check(unacked == ['c-junk'], 'an acknowledgement with another sha256 does not acknowledge')
    open(os.path.join(acks, 'a-fail.retirement-ack'), 'w').write(want + '\n')
    os.chmod(os.path.join(acks, 'a-fail.retirement-ack'), 0o666)
    check(RG(ev, acks, base, UID)[1] == ['a-fail', 'c-junk'], 'a world-writable acknowledgement no longer acknowledges')
    os.chmod(os.path.join(acks, 'a-fail.retirement-ack'), 0o644)
    os.chmod(acks, 0o777)
    check(RG(ev, acks, base, UID)[1] == ['a-fail', 'c-junk'], 'a world-writable ack directory acknowledges nothing')
    os.chmod(acks, 0o755)
    check(RG(ev, acks, base, UID + 1)[1] == ['a-fail', 'c-junk'], 'acknowledgements owned by another uid acknowledge nothing')
    check(RG(ev, os.path.join(base, 'absent'), base, UID)[1] == ['a-fail', 'c-junk'], 'an absent ack directory means no acknowledgements')
    other_dir = os.path.join(base, 'old-etc-acks')                     # the r5 location stand-in: acks there must not count
    os.makedirs(other_dir)
    os.chmod(other_dir, 0o755)
    open(os.path.join(other_dir, 'c-junk.retirement-ack'), 'w').write(hashlib.sha256(b'not json').hexdigest() + '\n')
    os.chmod(os.path.join(other_dir, 'c-junk.retirement-ack'), 0o644)
    check(RG(ev, acks, base, UID)[1] == ['c-junk'], 'an acknowledgement only in another directory does not count')
    check(RG(ev, other_dir, base, UID)[1] == ['a-fail'], 'the same acknowledgement counts when that directory is the ack directory')
    check(RG(os.path.join(tmp, 'no-evidence'), acks, base, UID)[1] == ['<evidence root unreadable>'], 'an unreadable evidence root refuses')
    # the default directory is the module's ACK_DIR, read at call time
    check(RG(ev, None, base, UID)[0]['ackDir'] == '/nonexistent/default-ack-dir', 'with no ack_dir the gate uses ACK_DIR')

# ============================================================ sweep: parse and classify (pod/reader exclusion)
CU = load(['parse_unit_rows', 'classify_units'])
rows = CU['parse_unit_rows']('\n'.join([
    'learn-op-a.service loaded active running A', '● learn-fin-a.service loaded failed failed F', 'learn-pod-p1.service loaded active running P',
    'learn-pod-p2.service loaded activating start P2', 'short line', '']))
check(len(rows) == 4, 'parse_unit_rows keeps rows of four columns or more and drops the rest (the bullet is stripped)')
busy, ops, pods = CU['classify_units'](rows)
check(busy == ['learn-fin-a.service', 'learn-pod-p2.service'], 'a failed unit and an activating POD unit are busy')
check(ops == {'learn-op-a'} and pods == ['learn-pod-p1'], 'an active reader op and an active pod unit are classified apart')
b2, o2, p2 = CU['classify_units'](CU['parse_unit_rows']('learn-pod-x.service loaded inactive dead X\nlearn-pod-y.service loaded deactivating stop Y'))
check(p2 == [] and b2 == ['learn-pod-y.service'], 'an inactive pod is not active; a deactivating pod is busy')
check(CU['classify_units']([])[2] == [], 'no units, no active pod')

# ============================================================ used_ids: an existing custody or evidence directory for the op refuses
UI = load(['used_ids'])['used_ids']
with tempfile.TemporaryDirectory() as tmp:
    cu, ev = os.path.join(tmp, 'custody'), os.path.join(tmp, 'evidence')
    os.makedirs(cu)
    os.makedirs(ev)
    check(UI('op-1', cu, ev) == [], 'no directories, nothing used')
    for sub, where in (('op-1', cu), ('op-1-pre', cu), ('op-1', ev), ('op-1-pre', ev)):
        p = os.path.join(where, sub)
        os.mkdir(p)
        check(UI('op-1', cu, ev) == [p], f'an existing {os.path.basename(where)} directory {sub} is reported')
        os.rmdir(p)
    os.symlink('/nonexistent', os.path.join(cu, 'op-1'))
    check(len(UI('op-1', cu, ev)) == 1, 'a dangling symlink at the custody path counts as used (lexists)')
    os.mkdir(os.path.join(cu, 'op-2'))
    check(UI('op-1', cu, ev) == [os.path.join(cu, 'op-1')], 'another op\'s directory is not reported')

# ============================================================ start_real: the nonce hash is in the RESULT before the units start
order = []
Rd = {'errors': []}


def fake_start_pair(opid, argv, nonce):
    order.append(('start_pair', dict(Rd)))
    return {'finalizerStart': 0, 'opStart': 0, 'errs': []}


class Slept:
    @staticmethod
    def sleep(_): order.append(('sleep', None))


ns = load(['start_real'], R=Rd, start_pair=fake_start_pair, time=Slept, run=lambda argv, t=0: (0, 'active\n', ''))
ns['start_real']('op-9', ['/bin/true'], 'ab' * 16)
want_nonce = hashlib.sha256(('ab' * 16).encode()).hexdigest()
check(order[0][0] == 'start_pair' and order[0][1].get('runNonceSha256') == want_nonce, 'runNonceSha256 is in the RESULT before start_pair is called')
check(Rd['runNonceSha256'] == want_nonce and len(want_nonce) == 64, 'runNonceSha256 is sha256(nonce) in lowercase hex')
check(Rd['errors'] == [] and set(Rd['units']) == {'learn-fin-op-9.service', 'learn-op-op-9.service'}, 'a clean start records both units and no error')
Rd2 = {'errors': []}
ns2 = load(['start_real'], R=Rd2, start_pair=lambda o, a, nn: {'finalizerStart': 1, 'opStart': None, 'errs': []}, time=Slept, run=lambda argv, t=0: (0, 'inactive\n', ''))
ns2['start_real']('op-8', ['x'], 'cd' * 16)
check(Rd2['runNonceSha256'] == hashlib.sha256(('cd' * 16).encode()).hexdigest() and any('unit start failed' in e for e in Rd2['errors']),
      'a failed start still carries the nonce hash and reports the failure')
ns3 = load(['start_real'], R={'errors': []}, start_pair=lambda o, a, nn: (_ for _ in ()).throw(RuntimeError('boom')), time=Slept, run=lambda *a, **k: (0, '', ''))
try:
    ns3['start_real']('op-7', ['x'], 'ee' * 16)
except RuntimeError:
    pass
check(ns3['R']['runNonceSha256'] == hashlib.sha256(('ee' * 16).encode()).hexdigest(), 'the nonce hash is recorded even when start_pair raises')

# ============================================================ recheck_sweep: the census taken again immediately before the start
_rs = load(['parse_unit_rows', 'classify_units', 'recheck_sweep', 'start_census'])
RS = _rs['recheck_sweep']
U = lambda *rows: '\n'.join(rows) + '\n'
check(RS('', 'op-r', 2) is None and RS(U('learn-op-a.service loaded active running A'), 'op-r', 2) is None, 'an empty census and one active reader op pass the recheck')
check('a pod op became active' in (RS(U('learn-pod-p1.service loaded active running P'), 'op-r', 2) or ''), 'an active pod unit that appeared since the first census refuses at the start')
check('failed or transitioning' in (RS(U('learn-fin-x.service loaded failed failed X'), 'op-r', 2) or ''), 'a failed unit that appeared refuses at the start')
check('failed or transitioning' in (RS(U('learn-op-a.service loaded activating start A'), 'op-r', 2) or ''), 'a transitioning reader op that appeared refuses at the start')
check('concurrency refused at the start: 2 ops' in (RS(U('learn-op-a.service loaded active running A', 'learn-op-b.service loaded active running B'), 'op-r', 2) or ''), 'a second reader op that appeared makes the cap refuse at the start')
check(RS(U('learn-op-op-r-pre.service loaded active running P', 'learn-op-a.service loaded active running A'), 'op-r', 2) is None, 'this op\'s own active precheck unit is not counted against the cap')
check(RS(U('learn-fin-op-r-pre.service loaded deactivating stop F'), 'op-r', 2) is None, '... even while its own precheck finalizer is deactivating')
check('failed or transitioning' in (RS(U('learn-fin-other-pre.service loaded deactivating stop F'), 'op-r', 2) or ''), 'another op\'s precheck finalizer that is deactivating is not excused')
check(RS(U('learn-pod-p1.service loaded inactive dead P'), 'op-r', 2) is None, 'an inactive pod unit does not refuse')
# a pod is never excused, not even one named like this op's precheck unit
for row, what in (('learn-pod-op-r-pre.service loaded active running P', 'active'), ('learn-pod-op-r.service loaded active running P', 'active, named for the op'),
                  ('learn-pod-op-r-pre.service loaded activating start P', 'activating'), ('learn-pod-op-r-pre.service loaded failed failed P', 'failed'),
                  ('learn-pod-op-r-pre.service loaded deactivating stop P', 'deactivating')):
    check(RS(U(row), 'op-r', 2) is not None, f'a pod unit named for this op\'s precheck is never excused ({what})')
# a failed census refuses, behaviourally (start_census runs the same code as main)
SC = _rs['start_census']
calls_ = []
for rc_, out_, what in ((1, '', 'a non-zero rc with an empty list (a failed systemctl)'), (None, '', 'a timeout (rc None)'), (3, 'learn-op-a.service loaded active running A\n', 'a non-zero rc even with output')):
    rc_got, why_ = SC('op-r', 2, run=lambda argv, t=0, rc=rc_, out=out_: (rc, out, ''))
    check(rc_got == rc_ and why_ == 'unit census failed at the start', f'a failed census refuses: {what}')
rc_got, why_ = SC('op-r', 2, run=lambda argv, t=0: (calls_.append(argv) or 0, '', ''))
check(rc_got == 0 and why_ is None and calls_[0][-3:] == ['learn-op-*', 'learn-fin-*', 'learn-pod-*'], 'a successful empty census passes and lists reader ops, finalizers and pods')
check(SC('op-r', 2, run=lambda argv, t=0: (0, U('learn-pod-p.service loaded active running P'), ''))[1].startswith('exclusion refused at the start'), 'a successful census with an active pod refuses through the same function')
body = SRC.split('# ---------------- main ----------------', 1)[1]
i_pre, i_re, i_start = body.index("if mode == '--precheck-only': finish(0)"), body.index('start_census(opid, MAX_ACTIVE_OPS)'), body.index('start_real(opid, argv, nonce)')
check(i_pre < i_re < i_start, 'the recheck runs after the precheck-only exit and immediately before start_real')
check(body[i_re:i_start].count("refuse(") == 1 and "if why: refuse(why)" in body[i_re:i_start] and 'secrets.token_hex' in body[i_re:i_start], 'a recheck refusal stops the run before the nonce is drawn and before any unit starts')
check("'unit census failed at the start'" in SRC, 'a failed census at the start refuses')

# ============================================================ end to end through the refusal gates
SUBS = []


def build_source(home, slicedir, seccomp, secsha, fin, finsha, py, pysha, ackdir, base):
    s = SRC
    s = sub1(s, r"HOME='/mnt/storage/containers/learn-qualification/account'", f"HOME={home!r}")
    s = sub1(s, r"SLICE = '/sys/fs/cgroup/user.slice/user-30034.slice'", f"SLICE = {slicedir!r}")
    s = sub1(s, r"SECCOMP = '/etc/fsgg/learn/seccomp-8bd52f8d.json'; SECCOMP_SHA = '[0-9a-f]+'", f"SECCOMP = {seccomp!r}; SECCOMP_SHA = {secsha!r}")
    s = sub1(s, r"FIN = '/usr/local/libexec/fsgg-learn/learn-finalizer-r4.py'; FIN_SHA = '[0-9a-f]+'", f"FIN = {fin!r}; FIN_SHA = {finsha!r}")
    s = sub1(s, r"PY_SHA = '[0-9a-f]+'", f"PY_SHA = {pysha!r}")
    s = sub1(s, r"sha_file\('/usr/bin/python3'\)", f"sha_file({py!r})")
    s = sub1(s, r"ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'", f"ACK_DIR = {ackdir!r}")
    s = sub1(s, r"chain_start='/', owner_uid=0\):", f"chain_start={base!r}, owner_uid={UID}):")
    s = sub1(s, r"def root_chain_ok\(path\):\n", "def root_chain_ok(path):\n    return True, None   # TEST FIXTURE: the r5 chain walk for SECCOMP and FIN is not under test\n")
    return s


def sub1(s, pat, rep):
    out, k = re.subn(pat, lambda m: rep, s, count=0)
    if k != 1:
        print(f'FAIL: substitution {pat!r} matched {k} times')
        sys.exit(1)
    return out


IMAGE_ID = re.search(r"IMAGE_ID = '([0-9a-f]+)'", SRC).group(1)


class P:
    def __init__(self, out='', rc=0):
        self.returncode, self.stdout, self.stderr = rc, out.encode(), b''


def run_operator(tmp, argv, units='', containers='', records=None, acks=None, custody=(), evid_dirs=(), memcur=0, mode_args=None, build_override=None, census_rc=0, podman_rc=0):
    home = os.path.join(tmp, 'home')
    state = os.path.join(home, '.local', 'state')
    for d in ('learn-custody', 'learn-evidence'):
        os.makedirs(os.path.join(state, d), exist_ok=True)
    slicedir = os.path.join(tmp, 'slice')
    os.makedirs(slicedir, exist_ok=True)
    for fn, val in (('memory.max', str(6 << 30)), ('pids.max', '1280'), ('memory.current', str(memcur)), ('pids.current', '30')):
        open(os.path.join(slicedir, fn), 'w').write(val + '\n')
    fx = {k: os.path.join(tmp, k) for k in ('seccomp.json', 'finalizer.py', 'python3')}
    for k, p in fx.items():
        open(p, 'w').write(f'fixture {k}\n')
    shas = {k: hashlib.sha256(open(p, 'rb').read()).hexdigest() for k, p in fx.items()}
    base = os.path.join(tmp, 'acktop')
    ackdir = os.path.join(base, 'retirement-acks')
    os.makedirs(ackdir, exist_ok=True)
    os.chmod(base, 0o755)
    os.chmod(ackdir, 0o755)
    for name, body in (records or {}).items():
        d = os.path.join(state, 'learn-evidence', name)
        os.makedirs(d, exist_ok=True)
        open(os.path.join(d, 'record.json'), 'wb').write(body)
    for name, content in (acks or {}).items():
        p = os.path.join(ackdir, name + '.retirement-ack')
        open(p, 'w').write(content + '\n')
        os.chmod(p, 0o644)
    for c in custody:
        os.makedirs(os.path.join(state, 'learn-custody', c), exist_ok=True)
    for c in evid_dirs:
        os.makedirs(os.path.join(state, 'learn-evidence', c), exist_ok=True)
    src = build_source(home, slicedir, fx['seccomp.json'], shas['seccomp.json'], fx['finalizer.py'], shas['finalizer.py'], fx['python3'], shas['python3'], ackdir, base)
    if build_override:
        src = build_override(src)
    calls = []

    def fake_run(argv_, capture_output=True, timeout=None, cwd=None):
        calls.append(list(argv_))
        if argv_[:3] == ['podman', 'image', 'inspect']:
            return P(IMAGE_ID + '\n')
        if argv_[:2] == ['systemctl', '--user'] and 'list-units' in argv_:
            return P(units, census_rc) if census_rc is not None else type('T', (), {'returncode': None, 'stdout': b'', 'stderr': b''})()
        if argv_[:2] == ['podman', 'ps']:
            return P(containers, podman_rc)
        raise AssertionError(f'unexpected command {argv_}')
    import subprocess
    saved = (subprocess.run, os.geteuid, os.umask(0o022), os.getcwd(), dict(os.environ), sys.argv, time.sleep)
    os.umask(saved[2])
    buf = io.StringIO()
    code = None
    try:
        subprocess.run, os.geteuid, sys.argv, time.sleep = fake_run, (lambda: 30034), ['learn-operator-r6.py'] + argv, (lambda s_: None)
        with contextlib.redirect_stdout(buf):
            try:
                exec(compile(src, R6, 'exec'), {'__name__': '__main__', '__file__': R6})
            except SystemExit as e:
                code = e.code
    finally:
        subprocess.run, os.geteuid, _, cwd, env, sys.argv, time.sleep = saved
        os.umask(saved[2])
        os.chdir(cwd)
        os.environ.clear()
        os.environ.update(env)
    out = buf.getvalue()
    result = json.loads(out.split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0])
    return code, result, calls, state


def refused_with(result, needle):
    return any(needle in e for e in result['errors'])


RUN = ['--run', '--id', 'op-test-1', '--argv-json', '["/bin/true"]']
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-pod-p1.service loaded active running P\n')
    check(code == 5 and r['exitCode'] == 5 and type(r['exitCode']) is int, 'exclusion: the exit code is the integer 5')
    check(refused_with(r, 'exclusion refused: a pod op is active: learn-pod-p1'), 'exclusion: an ACTIVE pod unit refuses the reader op')
    check(r['gates']['exclusion'] == {'activePods': ['learn-pod-p1']} and r['mode'] == '--run' and r['opId'] == 'op-test-1', 'exclusion: the RESULT records the pod, the mode and the op id')
    check(r['schema'] == 'home-learn-operator/1' and r['operatorRevision'] == 'r6' and r['operatorSha256'] == hashlib.sha256(open(R6, 'rb').read()).hexdigest(), 'the RESULT names operator r6 with the sha256 of r6')
    check(not any('exception' in e for e in r['errors']) and 'retirementAcks' not in r['gates'] and 'precheck' not in r, 'exclusion refuses before the ack gate and the precheck')
    lu = [c for c in calls if 'list-units' in c][0]
    check(lu[-3:] == ['learn-op-*', 'learn-fin-*', 'learn-pod-*'], 'the sweep lists learn-pod-* units as well as learn-op-* and learn-fin-*')
    check('runNonceSha256' not in r, 'a refused run carries no nonce hash (nothing was started)')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-pod-p2.service loaded failed failed P2\n')
    check(code == 5 and refused_with(r, 'sweep refused'), 'a FAILED pod unit refuses like any failed unit (sweep)')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-pod-p3.service loaded deactivating stop P3\n')
    check(code == 5 and refused_with(r, 'sweep refused'), 'a deactivating pod unit refuses (sweep)')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-pod-p4.service loaded inactive dead P4\n', records={'x-fail': FAIL})
    check(not refused_with(r, 'exclusion') and not refused_with(r, 'sweep'), 'an inactive pod unit does not exclude the reader op')
    check(refused_with(r, 'retirement-failure-unacknowledged: x-fail'), '... and the run goes on to the next gate (the unacknowledged record)')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-op-a.service loaded active running A\nlearn-op-b.service loaded active running B\n')
    check(code == 5 and refused_with(r, 'concurrency refused: 2 ops already active'), 'the two-op cap is unchanged')
    check(r['gates']['exclusion'] == {'activePods': []}, '... and with no pod the exclusion gate records an empty list')

# a failed FIRST census refuses (an empty list from a failed systemctl must not read as 'no units')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, census_rc=1)
    check(code == 5 and refused_with(r, 'unit census failed') and 'exclusion' not in r['gates'], 'a failed systemctl (rc 1, empty list) refuses the run before any later gate')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, census_rc=None)
    check(code == 5 and refused_with(r, 'unit census failed'), 'a census that timed out (rc None) refuses the run')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, census_rc=0)
    check(not refused_with(r, 'unit census failed'), 'a successful empty census does not refuse')

# a failed container census refuses (an empty list from a failed podman must not read as 'no orphan containers')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, podman_rc=125)
    check(code == 5 and refused_with(r, 'container census failed') and 'sweep' not in r['gates'], 'a failed podman census (rc 125, empty list) refuses the run')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, podman_rc=0)
    check(not refused_with(r, 'container census failed'), 'a successful empty container census does not refuse')

# custody directory: an existing one refuses
for label, kw in (('custody', dict(custody=['op-test-1'])), ('custody of the precheck id', dict(custody=['op-test-1-pre'])),
                  ('evidence', dict(evid_dirs=['op-test-1'])), ('evidence of the precheck id', dict(evid_dirs=['op-test-1-pre']))):
    with tempfile.TemporaryDirectory() as tmp:
        code, r, calls, state = run_operator(tmp, RUN, **kw)
        check(code == 5 and refused_with(r, 'op id already used: '), f'an existing {label} directory refuses the run')
        check('retirementAcks' not in r['gates'], f'... before any later gate ({label})')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, units='learn-op-op-test-1.service loaded active running X\n')
    check(refused_with(r, 'op id already used'), 'an existing unit named for the op id refuses the run')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, custody=['some-other-op'])
    check(not refused_with(r, 'op id already used'), 'another op\'s custody directory does not refuse this one')

# the persistent ack: end to end
with tempfile.TemporaryDirectory() as tmp:
    want = hashlib.sha256(FAIL).hexdigest()
    code, r, calls, state = run_operator(tmp, RUN, records={'x-fail': FAIL})
    check(refused_with(r, 'retirement-failure-unacknowledged: x-fail') and r['gates']['retirementAcks']['unacked'] == ['x-fail'], 'ack gate: a failure record without an acknowledgement refuses')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, records={'x-fail': FAIL}, acks={'x-fail': hashlib.sha256(FAIL).hexdigest()}, memcur=(6 << 30) - (1 << 30))
    check(r['gates']['retirementAcks']['acked'] == ['x-fail'] and r['gates']['retirementAcks']['unacked'] == [], 'ack gate: an acknowledgement in the persistent directory with the record\'s sha256 passes')
    check(refused_with(r, 'headroom refused'), '... and the run reaches the next gate (headroom)')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, RUN, records={'x-fail': FAIL}, acks={'x-fail': '0' * 64})
    check(refused_with(r, 'retirement-failure-unacknowledged: x-fail'), 'ack gate: an acknowledgement with another sha256 refuses')

# mode and exit code in the RESULT of a --precheck-only that refuses
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, ['--precheck-only', '--id', 'op-test-2'], units='learn-pod-p1.service loaded active running P\n')
    check(r['mode'] == '--precheck-only' and r['exitCode'] == 5 and type(r['exitCode']) is int, 'a refused --precheck-only reports its mode and an integer exit code')
with tempfile.TemporaryDirectory() as tmp:
    code, r, calls, state = run_operator(tmp, ['--frob'])
    check(code == 5 and r['exitCode'] == 5 and any('usage' in e for e in r['errors']), 'a usage error is a refusal with an integer exit code')

print(f'learn-operator-r6: {n} assertions passed')

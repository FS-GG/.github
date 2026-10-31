#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/pod-operator/learn-pod-operator-r1.py, the pod operator GATE LAYER (Refs FS-GG/.github#4647).

Drives main() through its gates with injected dependencies (a scripted `run`, the effective uid, the clock, the nonce, the pod start) against
temporary fixtures; no container, no systemd, no root, no network. Checks that the verbatim blocks are byte for byte the ones in
learn_seal_claim.py and operator r6, runs the admission vectors A01 to A18 against BOTH the original reader and the copy and requires the same
answers, and feeds the pod operator's RESULT to the claim tooling's own checks. Prints its own assertion count."""
import ast, contextlib, hashlib, importlib.util, io, json, os, re, stat, sys, tempfile
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEP = os.path.join(ROOT, 'deployment', 'learn-finite-profile')
POD = os.path.join(DEP, 'pod-operator', 'learn-pod-operator-r1.py')
SEAL = os.path.join(DEP, 'seal', 'learn_seal_claim.py')
R6 = os.path.join(DEP, 'operator', 'learn-operator-r6.py')


def mod(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


M = mod('pod', POD)
L = mod('seal', SEAL)
UID = os.getuid()
n = 0
H = lambda c: c * 64


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
    except M.Refuse as e:
        if needle not in str(e):
            print(f'FAIL: {what}: refused with "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: not refused')
    sys.exit(1)


# ============================================================ the verbatim blocks are byte for byte the originals
def segs(path):
    s = open(path).read()
    out = {}
    for node in ast.parse(s).body:
        if isinstance(node, ast.FunctionDef):
            out[node.name] = ast.get_source_segment(s, node)
        elif isinstance(node, ast.Assign):
            out[ast.get_source_segment(s, node).split(' = ')[0]] = ast.get_source_segment(s, node)
    return out


pod_s, seal_s, r6_s = segs(POD), segs(SEAL), segs(R6)
for name in ('DIR_FLAGS', 'ADMIT_FIELDS', '_check_dir_fd', 'open_dir_chain', 'check_admission'):
    check(pod_s[name] == seal_s[name], f'{name} is byte for byte the one in learn_seal_claim.py')
for name in ('EVID_MAX_BYTES, EVID_MAX_ENTRIES', 'read_record_nofollow', 'evidence_usage', 'read_ack_pinned', 'retirement_gate', 'parse_unit_rows', 'used_ids'):
    check(pod_s[name] == r6_s[name], f'{name} is byte for byte the one in operator r6')
check(M.MAX_TTL_S == L.MAX_TTL_S == 600, 'the admission lifetime cap equals the claim tooling\'s (600 s)')
check(M.SLICE_MEMORY_MAX == 6 << 30 and M.SLICE_PIDS_MAX == 1280, 'the slice limits are the profile\'s 6 GiB and 1280 pids')
pod_src, r6_src = open(POD).read(), open(R6).read()
for r6frag, podfrag in (("lfd = os.open(LOCK, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)", "lfd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)"),
                        ("fcntl.flock(lfd, fcntl.LOCK_EX | fcntl.LOCK_NB)", "fcntl.flock(lfd, fcntl.LOCK_EX | fcntl.LOCK_NB)"),
                        ("refuse('operator lock busy')", "refuse('operator lock busy')"), ("time.sleep(0.5)", "deps['sleep'](0.5)")):
    check(r6frag in r6_src and podfrag in pod_src, f'the lock code {podfrag!r} is operator r6\'s {r6frag!r}')
check("LOCK_WAIT_S = f\"{os.environ['HOME']}/.local/state/learn-operator.lock\", 120" in r6_src.replace("LOCK, LOCK_WAIT_S = ", "LOCK_WAIT_S = ") or 'learn-operator.lock", 120' in r6_src, 'r6 waits 120 s for the lock')
check(M.PROD['lock_wait_s'] == 120, 'the pod operator waits 120 s for the same lock')
check(M.paths(M.PROD)['lock'] == '/mnt/storage/containers/learn-qualification/account/.local/state/learn-operator.lock', 'the lock path is the account\'s ~/.local/state/learn-operator.lock')
check(M.PROD['image_ids'] == [] and M.PROD['finalizer_sha'] is None and not M.PROD['pod_precheck_implemented'] and not M.PROD['start_implemented'],
      'the production configuration pins nothing and implements neither the pod precheck nor the start')


# ============================================================ fixtures
class World:
    """A temporary account, view and slice. The view is root-safe for the running user (owner_uid = UID, chain start = the temporary base)."""
    def __init__(self, tmp, nested=False):
        self.base = os.path.join(tmp, 'top')
        os.makedirs(self.base)
        os.chmod(self.base, 0o755)
        self.mid = os.path.join(self.base, 'mid')
        if nested:
            os.makedirs(self.mid)
            os.chmod(self.mid, 0o755)
        self.view = os.path.join(self.mid if nested else self.base, 'view')
        self.home = os.path.join(tmp, 'home')
        self.slice = os.path.join(tmp, 'slice')
        self.state = os.path.join(self.home, '.local', 'state')
        for d in (self.view, os.path.join(self.view, 'retirement-acks'), os.path.join(self.view, 'claims'), os.path.join(self.view, 'inputs'), os.path.join(self.view, 'admissions')):
            os.makedirs(d)
            os.chmod(d, 0o755)
        for d in ('learn-custody', 'learn-evidence'):
            os.makedirs(os.path.join(self.state, d))
        os.makedirs(self.slice)
        self.set_slice(mem_current=0, pids_current=30)
        self.meminfo = os.path.join(tmp, 'meminfo')
        self.set_meminfo(16 << 30)
        self.fin = os.path.join(self.base if nested else self.view, 'learn-finalizer-r5.py')
        open(self.fin, 'w').write('# finalizer r5 fixture\n')
        os.chmod(self.fin, 0o644)
        self.fin_sha = hashlib.sha256(open(self.fin, 'rb').read()).hexdigest()
        self.image = 'a1' * 32
        self.ident = hashlib.sha256(b'source').hexdigest()
        self.argv = '["/opt/fsgg/orchestration/host/fsgg-coord-orchestration-host","serve"]'
        self.self_sha = hashlib.sha256(open(POD, 'rb').read()).hexdigest()
        self.units, self.pods, self.containers, self.images = '', '', '', {self.image}
        self.now = datetime.now(timezone.utc)
        self.euid = 424242

    def set_slice(self, mem_current, pids_current, mem_max=6 << 30, pids_max=1280):
        for fn, v in (('memory.max', mem_max), ('pids.max', pids_max), ('memory.current', mem_current), ('pids.current', pids_current)):
            open(os.path.join(self.slice, fn), 'w').write(f'{v}\n')

    def set_meminfo(self, avail):
        open(self.meminfo, 'w').write(f'MemTotal: 32000000 kB\nMemAvailable: {avail // 1024} kB\n')

    def env(self, **over):
        e = {'uid': self.euid, 'owner_uid': UID, 'chain_start': self.base, 'home': self.home, 'view': self.view, 'slice': self.slice, 'meminfo': self.meminfo,
             'fin': self.fin, 'finalizer_sha': self.fin_sha, 'image_ids': [self.image], 'lock_wait_s': 0, 'pod_precheck_implemented': False, 'start_implemented': False}
        e.update(over)
        return e

    def deps(self, **over):
        calls = []

        def run(argv, timeout=60, cap=0):
            calls.append(list(argv))
            if argv[:3] == ['podman', 'image', 'inspect']:
                return (0, argv[-1] + '\n', '') if argv[-1] in self.images else (1, '', 'no such image')
            if argv[:2] == ['systemctl', '--user']:
                return 0, self.units, ''
            if argv[:3] == ['podman', 'pod', 'ps']:
                return 0, self.pods, ''
            if argv[:2] == ['podman', 'ps']:
                pref = argv[argv.index('--filter') + 1].split('=^', 1)[1]                    # emulate --filter name=^<prefix>
                return 0, ''.join(l + '\n' for l in self.containers.split() if l.startswith(pref)), ''
            raise AssertionError(f'unexpected command {argv}')
        d = {'run': run, 'geteuid': lambda: self.euid, 'now': lambda: self.now, 'sleep': lambda s: None, 'monotonic': iter(range(0, 10 ** 6)).__next__,
             'nonce': lambda: 'cd' * 16, 'start_pod': lambda claim, sid: None}
        d.update(over)
        d['calls'] = calls
        return d

    def admit(self, opid, argv=None, ident=None, operator_sha=None, fin_sha=None, images=None, ttl=300, now=None):
        return L.admit(opid, hashlib.sha256((argv or self.argv).encode()).hexdigest(), ident or self.ident, operator_sha or self.self_sha,
                       fin_sha or self.fin_sha, images or [self.image], ttl, source_root=self.view, view_root=self.view, services=self.view, owner_uid=UID,
                       now=now or self.now)

    def claim_files(self, opid, snapshot='snap-1', claim_op=None, sealed_sha=None, ident=None, claim_ident=None, manifest_ident=None, schema=None, claim_snapshot=None, manifest_snapshot=None):
        sm = {'schema': 'learn-input-snapshot/1', 'snapshotId': manifest_snapshot or snapshot, 'sourceIdentity': manifest_ident or ident or self.ident, 'files': []}
        smb = L.canon(sm)
        with open(os.path.join(self.view, 'inputs', snapshot + '.manifest.json'), 'wb') as f:
            f.write(smb)
        os.chmod(os.path.join(self.view, 'inputs', snapshot + '.manifest.json'), 0o644)
        cl = {'schema': schema or 'learn-input-claim/1', 'generation': 1, 'sourceIdentity': claim_ident or ident or self.ident, 'opId': claim_op or opid, 'snapshotId': snapshot if claim_snapshot is None else claim_snapshot,
              'sealedManifestSha256': sealed_sha or hashlib.sha256(smb).hexdigest(), 'precheckResultSha256': H('b'), 'claimedUtc': self.now.isoformat(), 'supersedes': None}
        p = os.path.join(self.view, 'claims', (ident or self.ident) + '.claim')
        with open(p, 'wb') as f:
            f.write(L.canon(cl))
        os.chmod(p, 0o644)
        return p


def go(w, mode, opid, env=None, deps=None, argv=None, ident=None):
    d = w.deps(**(deps or {}))
    buf = io.StringIO()
    umask = os.umask(0o022)
    try:
        with contextlib.redirect_stdout(buf):
            code = M.main([mode, '--id', opid, '--argv-json', argv or w.argv, '--source-identity', ident or w.ident], w.env(**(env or {})), d)
    finally:
        os.umask(umask)
        while M.HELD_LOCKS:
            os.close(M.HELD_LOCKS.pop())
    out = buf.getvalue()
    return code, json.loads(out.split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0]), d


def has(r, needle):
    return any(needle in e for e in r['errors'])


PRE, RUN = '--precheck-only', '--run'

# ============================================================ arguments and identity
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    for argv, needle, what in (([], 'usage', 'no arguments'), (['--frob', '--id', 'op-aaa', '--argv-json', '["x"]', '--source-identity', H('a')], 'usage', 'an unknown mode'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', '["x"]'], 'usage', 'a missing source identity'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', '["x"]', '--source-identity', H('a'), '--x', 'y'], 'usage', 'an extra option'),
                               ([PRE, '--id', 'op-aaa', '--id', 'op-bbb', '--argv-json', '["x"]', '--source-identity', H('a')], 'usage', 'a repeated option'),
                               ([PRE, '--id', 'Bad', '--argv-json', '["x"]', '--source-identity', H('a')], 'bad op id', 'a bad op id'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', '["x"]', '--source-identity', 'zz'], 'source identity must be', 'a bad source identity'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', 'not json', '--source-identity', H('a')], 'not JSON', 'argv that is not JSON'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', '[]', '--source-identity', H('a')], 'non-empty', 'an empty argv'),
                               ([PRE, '--id', 'op-aaa', '--argv-json', '[1]', '--source-identity', H('a')], 'non-empty', 'a non-string argv')):
        refused(lambda a=argv: M.parse_args(a), needle, f'{what} is refused')
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        code = M.main([], w.env(), w.deps())
    check(code == 5 and type(json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0])['exitCode']) is int, 'a usage error is a refusal with an integer exit code')
    code, r, d = go(w, PRE, 'op-pod-1', deps={'geteuid': lambda: 1000})
    check(code == 5 and has(r, 'run as fsgg-learn-qual'), 'a run as another uid is refused')
    check(d['calls'] == [], 'nothing is run before the uid is checked')
    code, r, d = go(w, PRE, 'op-pod-1', env={'image_ids': []})
    check(has(r, 'no image ids are pinned'), 'an empty image pin refuses (this revision pins none)')
    code, r, d = go(w, PRE, 'op-pod-1', env={'finalizer_sha': None})
    check(has(r, 'finalizer pin is empty'), 'an empty finalizer pin refuses (finalizer r5 is a later PR)')
    # the production configuration refuses for exactly that reason when run as the right uid
    code, r, d = go(w, PRE, 'op-pod-1', env={k: M.PROD[k] for k in ('image_ids', 'finalizer_sha')})
    check(code == 5 and has(r, 'no image ids are pinned'), 'the production pins refuse every run of this revision')

# ============================================================ pins, slice, lock
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.images = set()
    code, r, d = go(w, PRE, 'op-pod-2')
    check(has(r, 'is not present locally by Id'), 'a pinned image that is not present by Id refuses')
    w.images = {w.image}
    open(w.fin, 'w').write('# tampered\n')
    code, r, d = go(w, PRE, 'op-pod-2')
    check(has(r, 'finalizer does not hash to its pin'), 'a finalizer that does not hash to its pin refuses')
    open(w.fin, 'w').write('# finalizer r5 fixture\n')
    os.chmod(w.fin, 0o666)
    code, r, d = go(w, PRE, 'op-pod-2')
    check(has(r, 'closed to group and other writers'), 'a group/other-writable finalizer refuses')
    os.chmod(w.fin, 0o644)
    os.unlink(w.fin)
    os.symlink(os.path.join(w.view, 'claims'), w.fin)
    code, r, d = go(w, PRE, 'op-pod-2')
    check(has(r, 'not a root-owned regular file'), 'a symlinked finalizer refuses (O_NOFOLLOW)')
    os.unlink(w.fin)
    open(w.fin, 'w').write('# finalizer r5 fixture\n')
    os.chmod(w.fin, 0o644)
    for kw, what in ((dict(mem_max=8 << 30), 'a 8 GiB memory limit'), (dict(pids_max=2000), 'a 2000-pid limit')):
        w.set_slice(0, 30, **kw)
        code, r, d = go(w, PRE, 'op-pod-2')
        check(has(r, 'slice limits absent'), f'{what} refuses (the profile is exact)')
    w.set_slice(0, 30)
    os.unlink(os.path.join(w.slice, 'memory.max'))
    code, r, d = go(w, PRE, 'op-pod-2')
    check(has(r, 'slice limits absent') and 'operatorLockWaitSeconds' not in r['gates'], 'a missing memory.max fails closed before the lock')
    w.set_slice(0, 30)

# the shared lock
with tempfile.TemporaryDirectory() as tmp:
    import fcntl
    w = World(tmp)
    lockp = os.path.join(w.state, 'learn-operator.lock')
    r6_holder = os.open(lockp, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)      # operator r6's lock code, byte for byte
    fcntl.flock(r6_holder, fcntl.LOCK_EX | fcntl.LOCK_NB)
    code, r, d = go(w, PRE, 'op-pod-3')
    check(code == 5 and has(r, 'operator lock busy'), 'a pod op refuses while an r6 reader op holds the shared lock')
    check('exclusion' not in r['gates'], '... before the sweep')
    os.close(r6_holder)
    code, r, d = go(w, PRE, 'op-pod-3')
    check(not has(r, 'operator lock busy') and r['gates']['operatorLockWaitSeconds'] >= 0, 'with the lock free the pod op takes it')
    w.units = ''
    buf = io.StringIO()
    with contextlib.redirect_stdout(buf):
        M.main([PRE, '--id', 'op-pod-3', '--argv-json', w.argv, '--source-identity', w.ident], w.env(), w.deps())
    check(len(M.HELD_LOCKS) == 1, 'the lock descriptor is kept open after the run (held to process exit)')
    other = os.open(lockp, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    try:
        fcntl.flock(other, fcntl.LOCK_EX | fcntl.LOCK_NB)
        check(False, 'an r6 operator cannot take the lock while the pod op holds it')
    except BlockingIOError:
        check(True, 'an r6 operator cannot take the lock while the pod op holds it')
    os.close(other)
    while M.HELD_LOCKS:
        os.close(M.HELD_LOCKS.pop())
    # waiting: busy for a while then free
    holder = os.open(lockp, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)
    ticks = []

    def sleeper(s):
        ticks.append(s)
        if len(ticks) == 3:
            fcntl.flock(holder, fcntl.LOCK_UN)
    lfd, waited = M.take_lock(lockp, 120, {'monotonic': iter(range(0, 1000)).__next__, 'sleep': sleeper})
    check(ticks == [0.5, 0.5, 0.5] and waited > 0, 'the lock is retried every 0.5 s until it is free')
    os.close(lfd)
    os.close(holder)
    holder = os.open(lockp, os.O_RDWR)
    fcntl.flock(holder, fcntl.LOCK_EX | fcntl.LOCK_NB)
    try:
        M.take_lock(lockp, 3, {'monotonic': iter(range(0, 1000)).__next__, 'sleep': lambda s: None})
        check(False, 'the lock wait is bounded')
    except M.Refuse as e:
        check('operator lock busy' in str(e), 'the lock wait is bounded by wait_s')
    os.close(holder)

# ============================================================ symmetric exclusion
cases_busy = [
    ('an ACTIVE reader op', 'learn-op-x1.service loaded active running X'), ('a reader PRECHECK op', 'learn-op-x1-pre.service loaded active running X'),
    ('an activating reader op', 'learn-op-x1.service loaded activating start X'), ('a deactivating reader op', 'learn-op-x1.service loaded deactivating stop X'),
    ('a reloading reader op', 'learn-op-x1.service loaded reloading reload X'), ('a FAILED reader op', 'learn-op-x1.service loaded failed failed X'),
    ('an ACTIVE pod unit', 'learn-pod-p1.service loaded active running P'), ('another pod that is activating', 'learn-pod-p1.service loaded activating start P'),
    ('a failed pod unit', 'learn-pod-p1.service loaded failed failed P'), ('a deactivating pod unit', 'learn-pod-p1.service loaded deactivating stop P'),
    ('a failed finalizer unit', 'learn-fin-f1.service loaded failed failed F'), ('a transitioning finalizer unit', 'learn-fin-f1.service loaded deactivating stop F'),
]
for what, row in cases_busy:
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.units = row + '\n'
        code, r, d = go(w, PRE, 'op-pod-4')
        check(code == 5 and has(r, 'exclusion refused: an op or pod is active or transitioning'), f'{what} refuses a pod op')
        check(r['gates']['exclusion']['conflicts'] and 'retirementAcks' not in r['gates'], f'... and is recorded, before the ack gate ({what})')
for what, row in (('an inactive reader op', 'learn-op-x1.service loaded inactive dead X'), ('an inactive pod', 'learn-pod-p1.service loaded inactive dead P'),
                  ('an active finalizer alone', 'learn-fin-f1.service loaded active running F')):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.units = row + '\n'
        code, r, d = go(w, PRE, 'op-pod-4')
        check(not has(r, 'exclusion refused') and 'retirementAcks' in r['gates'], f'{what} does not exclude a pod op')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.pods = 'learn-pod-old\nsomething-else\n'
    code, r, d = go(w, PRE, 'op-pod-4')
    check(has(r, 'exclusion refused: a pod exists: learn-pod-old') and r['gates']['exclusion']['leftoverPods'] == ['learn-pod-old'], 'a leftover learn-pod-* pod refuses even with no unit')
    w.pods = 'something-else\n'
    code, r, d = go(w, PRE, 'op-pod-4')
    check(not has(r, 'exclusion refused'), 'an unrelated pod does not')
    code, r, d = go(w, PRE, 'op-pod-4', deps={'run': lambda argv, timeout=60, cap=0: (1, '', 'boom') if argv[:2] == ['systemctl', '--user'] else ((0, argv[-1] + '\n', '') if argv[:3] == ['podman', 'image', 'inspect'] else (0, '', ''))})
    check(has(r, 'unit census failed'), 'a failed unit census refuses (fail closed)')
    code, r, d = go(w, PRE, 'op-pod-4', deps={'run': lambda argv, timeout=60, cap=0: (1, '', 'boom') if argv[:3] == ['podman', 'pod', 'ps'] else ((0, argv[-1] + '\n', '') if argv[:3] == ['podman', 'image', 'inspect'] else (0, '', ''))})
    check(has(r, 'pod census failed'), 'a failed pod census refuses (fail closed)')
    code, r, d = go(w, PRE, 'op-pod-4')
    check([c for c in d['calls'] if 'list-units' in c][0][-3:] == ['learn-op-*', 'learn-fin-*', 'learn-pod-*'], 'the census lists reader ops, finalizers and pods')
cl = M.exclusion_conflicts(M.parse_unit_rows('learn-op-a.service loaded active running A\nlearn-pod-b.service loaded failed failed B\nlearn-fin-c.service loaded active running C\nlearn-fin-d.service loaded failed failed D\nlearn-op-e.service loaded inactive dead E'))
check(cl == ['learn-fin-d.service', 'learn-op-a.service', 'learn-pod-b.service'], 'exclusion_conflicts names every conflicting unit and only those')

# ============================================================ custody, acks, evidence, admission, headroom
for what, mk in (('custody', lambda w: os.makedirs(os.path.join(w.state, 'learn-custody', 'op-pod-5'))),
                 ('custody of the precheck id', lambda w: os.makedirs(os.path.join(w.state, 'learn-custody', 'op-pod-5-pre'))),
                 ('evidence', lambda w: os.makedirs(os.path.join(w.state, 'learn-evidence', 'op-pod-5'))),
                 ('evidence of the precheck id', lambda w: os.makedirs(os.path.join(w.state, 'learn-evidence', 'op-pod-5-pre')))):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        mk(w)
        code, r, d = go(w, PRE, 'op-pod-5')
        check(code == 5 and has(r, 'op id already used: '), f'an existing {what} directory refuses a pod op')
        check('retirementAcks' not in r['gates'], f'... before the ack gate ({what})')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.units = 'learn-pod-op-pod-5.service loaded inactive dead X\n'
    code, r, d = go(w, PRE, 'op-pod-5')
    check(has(r, 'op id already used'), 'an existing unit named for the op id refuses')
    w.units = ''
    os.makedirs(os.path.join(w.state, 'learn-custody', 'other-op'))
    code, r, d = go(w, PRE, 'op-pod-5')
    check(not has(r, 'op id already used'), 'another op\'s custody directory does not')

FAIL = b'{"schema":"learn-op-retirement-failure/1","x":1}'
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    ev = os.path.join(w.state, 'learn-evidence', 'f-fail')
    os.makedirs(ev)
    open(os.path.join(ev, 'record.json'), 'wb').write(FAIL)
    code, r, d = go(w, PRE, 'op-pod-6')
    check(has(r, 'retirement-failure-unacknowledged: f-fail') and r['gates']['retirementAcks']['unacked'] == ['f-fail'], 'the ack gate refuses an unacknowledged failure record')
    ackp = os.path.join(w.view, 'retirement-acks', 'f-fail.retirement-ack')
    open(ackp, 'w').write(hashlib.sha256(FAIL).hexdigest() + '\n')
    os.chmod(ackp, 0o644)
    code, r, d = go(w, PRE, 'op-pod-6')
    check(r['gates']['retirementAcks']['acked'] == ['f-fail'] and not has(r, 'retirement-failure'), 'an acknowledgement in the persistent directory passes the gate')
    check(has(r, 'admission refused'), '... and the run reaches the admission gate')
    open(ackp, 'w').write('0' * 64 + '\n')
    code, r, d = go(w, PRE, 'op-pod-6')
    check(has(r, 'retirement-failure-unacknowledged'), 'an acknowledgement with another sha256 refuses')
    open(ackp, 'w').write(hashlib.sha256(FAIL).hexdigest() + '\n')
    os.chmod(ackp, 0o666)
    code, r, d = go(w, PRE, 'op-pod-6')
    check(has(r, 'retirement-failure-unacknowledged'), 'a world-writable acknowledgement refuses')
    os.chmod(ackp, 0o644)
    os.chmod(os.path.join(w.view, 'retirement-acks'), 0o777)
    code, r, d = go(w, PRE, 'op-pod-6')
    check(has(r, 'retirement-failure-unacknowledged'), 'a world-writable ack directory refuses')
    os.chmod(os.path.join(w.view, 'retirement-acks'), 0o755)
    # evidence size
    real_max = M.EVID_MAX_BYTES
    try:
        M.EVID_MAX_BYTES = 1
        code, r, d = go(w, PRE, 'op-pod-6')
        check(has(r, 'evidence size refused'), 'an evidence root over the cap refuses')
    finally:
        M.EVID_MAX_BYTES = real_max

with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    code, r, d = go(w, PRE, 'op-pod-7')
    check(has(r, 'admission refused') and 'admission is absent or unreadable' in r['gates']['admission']['reasons'], 'a run with no admission file refuses')
    w.admit('op-pod-7')
    code, r, d = go(w, PRE, 'op-pod-7')
    check(not has(r, 'admission refused') and r['gates']['admission']['reasons'] == [], 'a matching admission passes')
    for what, kw, needle in (('another argv', dict(argv='["other"]'), 'argvJsonSha256 differs'), ('another source identity', dict(ident=H('9')), 'sourceIdentity differs'),
                             ('another operator pin', dict(operator_sha=H('9')), 'operatorSha256 differs'), ('another finalizer pin', dict(fin_sha=H('9')), 'finalizerSha256 differs'),
                             ('another image set', dict(images=[H('9')]), 'imageIds differs')):
        os.unlink(os.path.join(w.view, 'admissions', 'op-pod-7.admit'))
        w.admit('op-pod-7', **kw)
        code, r, d = go(w, PRE, 'op-pod-7')
        check(has(r, needle), f'an admission with {what} refuses')
    os.unlink(os.path.join(w.view, 'admissions', 'op-pod-7.admit'))
    w.admit('op-pod-7')
    w.now = w.now + timedelta(seconds=601)
    code, r, d = go(w, PRE, 'op-pod-7')
    check(has(r, 'has expired'), 'an expired admission refuses')
    w.now = w.now - timedelta(seconds=601)

with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit('op-pod-8')
    w.set_slice((6 << 30) - (4 << 30), 30)
    code, r, d = go(w, PRE, 'op-pod-8')
    check(has(r, 'headroom refused: memory') and r['gates']['headroom']['pids'] is True, 'less than 4.5 GiB of slice memory headroom refuses')
    w.set_slice(0, 1280 - 999)
    code, r, d = go(w, PRE, 'op-pod-8')
    check(has(r, 'headroom refused: pids') and r['gates']['headroom']['memory'] is True, 'less than 1000 pids of headroom refuses')
    w.set_slice(0, 30)
    w.set_meminfo((4 << 30) - 4096)
    code, r, d = go(w, PRE, 'op-pod-8')
    check(has(r, 'headroom refused: memAvailable'), 'less than 4 GiB of MemAvailable refuses')
    w.set_meminfo(16 << 30)
    ok, vals = M.headroom(w.slice, w.meminfo, w.state, statvfs=lambda p: type('S', (), {'f_bavail': 100, 'f_frsize': 4096})())
    check(ok['custodyDisk'] is False and ok['memory'] and ok['pids'] and ok['memAvailable'], 'less than 1 GiB of custody disk refuses (headroom function)')
    ok, vals = M.headroom(w.slice, w.meminfo, w.state)
    check(all(ok.values()) and vals['memMax'] == str(6 << 30), 'the fixtures have headroom everywhere')

# ============================================================ hardening: orphan container sweep, evidence-size branches, the admission chain start
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit('op-pod-20')
    w.containers = 'learn-op-ghost\nunrelated\n'
    code, r, d = go(w, PRE, 'op-pod-20')
    check(code == 5 and has(r, 'orphan learn-op-* containers: learn-op-ghost') and r['gates']['exclusion']['orphanContainers'] == ['learn-op-ghost'],
          'a learn-op-* container whose op unit is not active refuses a pod op (parity with operator r6)')
    ps = [c for c in d['calls'] if c[:2] == ['podman', 'ps']][0]
    check(ps == ['podman', 'ps', '-a', '--filter', 'name=^learn-op-', '--format', '{{.Names}}'], 'the sweep asks for learn-op-* containers exactly as operator r6 does')
    w.containers = 'unrelated\nlearn-pod-x-infra\n'
    code, r, d = go(w, PRE, 'op-pod-20')
    check(not has(r, 'orphan') and r['gates']['exclusion']['orphanContainers'] == [], 'other containers are not orphans')
    code, r, d = go(w, PRE, 'op-pod-20', deps={'run': lambda argv, timeout=60, cap=0: (1, '', 'boom') if argv[:2] == ['podman', 'ps'] else (
        (0, argv[-1] + '\n', '') if argv[:3] == ['podman', 'image', 'inspect'] else (0, '', ''))})
    check(has(r, 'container census failed'), 'a failed container census refuses (fail closed)')
    w.containers = ''
    code, r, d = go(w, PRE, 'op-pod-20')
    check(not has(r, 'orphan') and not has(r, 'container census'), 'with no containers the sweep passes')

with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    ev = os.path.join(w.state, 'learn-evidence')
    os.makedirs(os.path.join(ev, 'op-a'))
    os.makedirs(os.path.join(ev, 'op-b'))
    real_entries = M.EVID_MAX_ENTRIES
    try:
        M.EVID_MAX_ENTRIES = 1
        code, r, d = go(w, PRE, 'op-pod-21')
        check(has(r, 'evidence size refused') and r['gates']['evidenceUsage']['entries'] >= 2, 'an evidence root over the entry cap refuses')
    finally:
        M.EVID_MAX_ENTRIES = real_entries
    real_usage = M.evidence_usage
    try:
        M.evidence_usage = lambda root: {'bytes': 0, 'entries': 0, 'error': 'unreadable ' + root}
        code, r, d = go(w, PRE, 'op-pod-21')
        check(has(r, 'evidence size refused') and 'error' in r['gates']['evidenceUsage'], 'an evidence tree that cannot be measured refuses (the error branch)')
    finally:
        M.evidence_usage = real_usage
    if os.geteuid() != 0:                                                       # a real unreadable subdirectory (root ignores modes)
        os.chmod(os.path.join(ev, 'op-a'), 0o000)
        try:
            code, r, d = go(w, PRE, 'op-pod-21')
            check(has(r, 'evidence size refused') and 'unreadable' in r['gates']['evidenceUsage'].get('error', ''), 'a real unreadable evidence subdirectory refuses')
        finally:
            os.chmod(os.path.join(ev, 'op-a'), 0o755)
    code, r, d = go(w, PRE, 'op-pod-21')
    check(not has(r, 'evidence size refused'), 'a readable, small evidence tree passes')

with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp, nested=True)
    w.admit('op-pod-22')
    code, r, d = go(w, PRE, 'op-pod-22')
    check(not has(r, 'admission refused') and r['gates']['admission']['reasons'] == [], 'with every directory between the chain start and the view safe, the admission passes')
    os.chmod(w.mid, 0o777)
    code, r, d = go(w, PRE, 'op-pod-22')
    check(code == 5 and has(r, 'admission refused') and any('admission path not root-safe at ' + w.mid in x for x in r['gates']['admission']['reasons']),
          'a world-writable directory BETWEEN the chain start and the view refuses at the admission gate, naming that directory')
    os.chmod(w.mid, 0o775)
    code, r, d = go(w, PRE, 'op-pod-22')
    check(has(r, 'admission path not root-safe'), 'a group-writable directory between the chain start and the view refuses too')
    os.chmod(w.mid, 0o755)
    moved = os.path.join(tmp, 'moved')
    os.rename(w.mid, moved)
    os.symlink(moved, w.mid)
    code, r, d = go(w, PRE, 'op-pod-22')
    check(has(r, 'admission refused'), 'a symlinked directory between the chain start and the view refuses')
    os.unlink(w.mid)
    os.rename(moved, w.mid)
    os.chmod(w.base, 0o777)
    code, r, d = go(w, PRE, 'op-pod-22')
    check(has(r, 'admission refused') or has(r, 'not root-safe'), 'an unsafe chain start refuses')
    os.chmod(w.base, 0o755)

# the marker directory and its parent are fsynced after the file
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    markers = os.path.join(w.state, 'learn-input')
    w.claim_files('op-pod-23')
    synced = []
    real_fsync = os.fsync

    def spy(fd):
        try:
            synced.append(os.readlink(f'/proc/self/fd/{fd}'))
        except OSError:
            synced.append('?')
        return real_fsync(fd)
    os.fsync = spy
    try:
        M.begin_input_access(w.env(), {'gates': {'inputMounted': False}}, 'op-pod-23', w.ident, lambda c, sid: None, lambda: w.now)
    finally:
        os.fsync = real_fsync
    d_ = os.path.join(markers, 'op-pod-23')
    check(synced[-3:] == [os.path.join(d_, 'input-read'), d_, markers], 'the marker file is fsynced, then its directory, then the directory\'s parent, in that order')
    try:
        M.fsync_dir(os.path.join(tmp, 'absent'))
        check(False, 'fsync_dir does not swallow a missing directory')
    except OSError:
        check(True, 'fsync_dir does not swallow a missing directory')

# ============================================================ --precheck-only never succeeds in this revision; with the flag set it is the claim tool's precheck
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit('op-pod-9')
    code, r, d = go(w, PRE, 'op-pod-9')
    check(code == 5 and has(r, 'pod containers precheck is not implemented in this revision'), 'in this revision --precheck-only refuses, so no claim can be taken from it')
    check(all(k in r['gates'] for k in ('pins', 'sliceLimits', 'exclusion', 'retirementAcks', 'evidenceUsage', 'admission', 'headroom')), '... after recording every gate')
    code, r, d = go(w, PRE, 'op-pod-9', env={'pod_precheck_implemented': True})
    check(code == 0 and r['exitCode'] == 0 and type(r['exitCode']) is int and r['errors'] == [], 'with the precheck marked implemented every gate passing is exit 0')
    check(r['schema'] == 'home-learn-pod-operator/1' and r['mode'] == '--precheck-only' and r['opId'] == 'op-pod-9' and r['gates']['inputMounted'] is False and r['containers'] == [],
          'the RESULT has the claim tooling\'s fields: schema, mode, opId, inputMounted false, a container list')
    check('runNonceSha256' not in r and r['operatorSha256'] == w.self_sha, 'a precheck carries no nonce; it carries this file\'s sha256')
    L._precheck_ok(r, 'op-pod-9')
    check(True, 'the claim tooling accepts this RESULT as a passing non-input precheck')
    for bad, what in (('op-other', 'another op'),):
        refused_ = False
        try:
            L._precheck_ok(r, bad)
        except L.Refuse:
            refused_ = True
        check(refused_, f'... and refuses it for {what}')
    check(r['gates']['inputMounted'] is False and not os.path.lexists(os.path.join(w.state, 'learn-input')), 'a precheck touches no input and creates no marker')

# ============================================================ --run: claim verification, nonce, no marker before the start is implemented
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit('op-pod-10')
    markers = os.path.join(w.state, 'learn-input')
    code, r, d = go(w, RUN, 'op-pod-10')
    check(code == 5 and has(r, 'absent: ') and not os.path.lexists(markers), 'a --run with no claim refuses and creates no marker')
    cp = w.claim_files('op-pod-10', claim_op='op-someone-else')
    code, r, d = go(w, RUN, 'op-pod-10')
    check(has(r, 'the live claim names another op') and not os.path.lexists(markers), 'a claim that names another op refuses')
    w.claim_files('op-pod-10', sealed_sha=H('9'))
    os.unlink(cp)
    w.claim_files('op-pod-10', sealed_sha=H('9'))
    code, r, d = go(w, RUN, 'op-pod-10')
    check(has(r, 'does not have the sha256 the claim records') and not os.path.lexists(markers), 'a sealed manifest with another sha256 refuses')
    os.unlink(cp)
    w.claim_files('op-pod-10')
    os.unlink(os.path.join(w.view, 'inputs', 'snap-1.manifest.json'))
    code, r, d = go(w, RUN, 'op-pod-10')
    check(has(r, 'absent: snap-1.manifest.json') and not os.path.lexists(markers), 'a missing sealed manifest refuses')
    for what, kw, needle in (('another source identity in the claim only', dict(claim_ident=H('7')), 'the claim has another source identity'),
                             ('another source identity in the sealed manifest only', dict(manifest_ident=H('7')), 'names another source or snapshot'),
                             ('another snapshot id in the sealed manifest only', dict(manifest_snapshot='snap-2'), 'names another source or snapshot'),
                             ('another schema', dict(schema='learn-input-claim/9'), 'claim has another schema'),
                             ('an invalid snapshot id', dict(claim_snapshot='../x'), 'no valid snapshot id'),
                             ('an empty snapshot id', dict(claim_snapshot=''), 'no valid snapshot id')):
        os.unlink(cp)
        w.claim_files('op-pod-10', **kw)
        code, r, d = go(w, RUN, 'op-pod-10')
        check(has(r, needle) and not os.path.lexists(markers), f'a claim with {what} refuses and creates no marker')
    os.unlink(cp)
    w.claim_files('op-pod-10')
    os.chmod(cp, 0o666)
    code, r, d = go(w, RUN, 'op-pod-10')
    check(has(r, 'closed to group and other writers'), 'a world-writable claim refuses')
    os.chmod(cp, 0o644)
    os.unlink(cp)
    os.symlink(os.path.join(w.view, 'inputs', 'snap-1.manifest.json'), cp)
    code, r, d = go(w, RUN, 'op-pod-10')
    check(has(r, 'not a root-owned regular file') and not os.path.lexists(markers), 'a symlinked claim refuses (O_NOFOLLOW)')
    os.unlink(cp)
    w.claim_files('op-pod-10')
    code, r, d = go(w, RUN, 'op-pod-10')
    check(code == 5 and has(r, 'pod start is not implemented in this revision; no input-read marker was created'), 'with a valid claim this revision stops before the marker')
    check(not os.path.lexists(markers), 'no input-read marker was created: a run of this revision consumes no claim')
    check(r['gates']['inputMounted'] is False and r['mode'] == '--run' and r['gates']['claim']['snapshotId'] == 'snap-1', 'inputMounted is false and the claim is recorded')
    nonce_hash = hashlib.sha256(('cd' * 16).encode()).hexdigest()
    check(r['runNonceSha256'] == nonce_hash, 'runNonceSha256 = sha256(nonce) is in the RESULT before anything could start')
    check(r['exitCode'] == 5 and type(r['exitCode']) is int and r['containers'] == [], 'an integer exit code and an empty container list')
    # the claim tooling's void check consumes exactly this RESULT
    fin_rec = {'schema': 'learn-op-failed-to-start/1', 'opId': 'op-pod-10', 'inputRead': False, 'containersStartedWithInput': [], 'runNonceSha256': nonce_hash,
               'finalizerSha256': w.fin_sha}
    adm, _ = L.read_json(os.path.join(w.view, 'admissions', 'op-pod-10.admit'))
    check(L._evidence_contract('op-pod-10', r, fin_rec, markers, adm) == [], 'the claim tooling\'s void contract accepts this RESULT with a finalizer record of the same nonce and pins')
    fin_bad = dict(fin_rec, runNonceSha256=H('8'))
    check(any('run nonce' in x for x in L._evidence_contract('op-pod-10', r, fin_bad, markers, adm)), '... and refuses a finalizer record of another nonce')

# ============================================================ begin_input_access: the ordering contract
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    markers = os.path.join(w.state, 'learn-input')
    w.claim_files('op-pod-11')
    env = w.env()
    old_umask = os.umask(0o022)                                  # the modes below must come from the explicit modes, not from a strict umask
    R = {'gates': {'inputMounted': False}, 'errors': []}
    seen = {}

    def mount(claim, sid):
        mp = os.path.join(markers, 'op-pod-11', 'input-read')
        seen['marker_exists'] = os.path.isfile(mp)
        seen['inputMounted'] = R['gates']['inputMounted']
        seen['sid'] = sid
        seen['claim_op'] = claim['opId']
    M.begin_input_access(env, R, 'op-pod-11', w.ident, mount, lambda: w.now)
    check(seen == {'marker_exists': True, 'inputMounted': True, 'sid': 'snap-1', 'claim_op': 'op-pod-11'}, 'at the moment of the mount the marker exists and gates.inputMounted is already true')
    mp = os.path.join(markers, 'op-pod-11', 'input-read')
    mj = json.load(open(mp))
    check(mj['opId'] == 'op-pod-11' and mj['claimSha256'] == R['gates']['claim']['claimSha256'] and stat.S_IMODE(os.stat(mp).st_mode) == 0o600
          and stat.S_IMODE(os.stat(os.path.dirname(mp)).st_mode) == 0o700, 'the marker records the op and the claim hash; the file is 0600 and its directory 0700')
    check(R['gates']['inputReadMarker'] == mp, 'the RESULT names the marker')
    refused(lambda: M.begin_input_access(env, {'gates': {'inputMounted': False}}, 'op-pod-11', w.ident, mount, lambda: w.now), 'already exists', 'a second access for the same op id refuses (the marker is never recreated)')
    # claim failures create no marker and leave inputMounted false
    for what, setup, needle in (('no claim', lambda: os.unlink(os.path.join(w.view, 'claims', w.ident + '.claim')), 'absent'),
                                ('a claim of another op', lambda: w.claim_files('op-pod-12', claim_op='op-zzz'), 'names another op')):
        setup()
        R2 = {'gates': {'inputMounted': False}}
        called = []
        refused(lambda: M.begin_input_access(env, R2, 'op-pod-12', w.ident, lambda c, s: called.append(1), lambda: w.now), needle, f'{what} refuses')
        check(R2['gates']['inputMounted'] is False and not called and not os.path.lexists(os.path.join(markers, 'op-pod-12')), f'... with no marker, no mount and inputMounted false ({what})')
    # a mount that fails leaves the marker and inputMounted true: the claim is consumed
    w.claim_files('op-pod-13')
    R3 = {'gates': {'inputMounted': False}}

    def bad_mount(claim, sid):
        raise RuntimeError('mount failed')
    try:
        M.begin_input_access(env, R3, 'op-pod-13', w.ident, bad_mount, lambda: w.now)
    except RuntimeError:
        pass
    check(R3['gates']['inputMounted'] is True and os.path.isfile(os.path.join(markers, 'op-pod-13', 'input-read')), 'a failed mount leaves the marker and inputMounted true')
    os.umask(old_umask)
    # the marker blocks a void in the claim tooling
    check(any('marker exists' in x for x in L._void_blockers('op-pod-13', {'inputRead': False, 'containersStartedWithInput': []}, markers)), 'the claim tooling reads this marker as consumption')

# ============================================================ --run end to end with an injected start
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit('op-pod-14')
    w.claim_files('op-pod-14')
    started = []
    markers = os.path.join(w.state, 'learn-input')

    def start(claim, sid):
        started.append((os.path.isfile(os.path.join(markers, 'op-pod-14', 'input-read')), claim['opId'], sid))
    code, r, d = go(w, RUN, 'op-pod-14', env={'start_implemented': True}, deps={'start_pod': start})
    check(code == 0 and r['errors'] == [] and started == [(True, 'op-pod-14', 'snap-1')], 'a --run with a valid claim starts only after the marker exists')
    check(r['gates']['inputMounted'] is True and r['gates']['inputReadMarker'].endswith('/learn-input/op-pod-14/input-read'), 'the RESULT shows inputMounted true and names the marker')
    check(r['runNonceSha256'] == hashlib.sha256(('cd' * 16).encode()).hexdigest(), 'the nonce hash is in the RESULT of a started run')
    # replay: the marker exists, the custody directory does not yet; the second attempt must not start again
    code, r, d = go(w, RUN, 'op-pod-14', env={'start_implemented': True}, deps={'start_pod': start})
    check(code == 5 and has(r, 'already exists') and len(started) == 1, 'a second --run of the same op id refuses at the marker and starts nothing')
    check(r['gates']['inputMounted'] is False, '... and its RESULT says inputMounted false (nothing was mounted by this attempt)')

# ============================================================ admission vectors A01 to A18, differentially against the original reader
with tempfile.TemporaryDirectory() as tmp:
    base = os.path.join(tmp, 'top')
    root = os.path.join(base, 'learn')
    os.makedirs(os.path.join(root, 'admissions'))
    for d in (base, root, os.path.join(root, 'admissions')):
        os.chmod(d, 0o755)
    ident = H('a')
    now = datetime.now(timezone.utc)
    chk = dict(path_root=root, op_id='op-adm', argv_sha=H('1'), ident=ident, operator_sha=H('2'), finalizer_sha=H('3'), image_ids=[H('4'), H('5')],
               chain_start=base, owner_uid=UID, now=now + timedelta(seconds=10))
    L.admit('op-adm', H('1'), ident, H('2'), H('3'), [H('5'), H('4')], 300, source_root=root, view_root=root, services=root, owner_uid=UID, now=now)

    def hand(op, issued, expires, naive=False, raw=None):
        body = dict(schema='learn-pod-admission/1', opId=op, argvJsonSha256=H('1'), sourceIdentity=ident, operatorSha256=H('2'), finalizerSha256=H('3'),
                    imageIds=[H('4')], issuedUtc=issued.replace(tzinfo=None).isoformat() if naive else issued.isoformat(),
                    expiresUtc=expires.replace(tzinfo=None).isoformat() if naive else expires.isoformat())
        if raw:
            body.update(raw)
        p = os.path.join(root, 'admissions', op + '.admit')
        open(p, 'w').write(json.dumps({k: v for k, v in body.items() if v is not None}))
        os.chmod(p, 0o644)
        return dict(chk, op_id=op, image_ids=[H('4')], now=now + timedelta(seconds=5))
    VECTORS = [
        ('A01', 'match-passes', lambda: chk, None), ('A02', 'argv-differs', lambda: dict(chk, argv_sha=H('9')), 'argvJsonSha256 differs'),
        ('A03', 'source-identity-differs', lambda: dict(chk, ident=H('9')), 'sourceIdentity differs'), ('A04', 'operator-differs', lambda: dict(chk, operator_sha=H('9')), 'operatorSha256 differs'),
        ('A05', 'finalizer-differs', lambda: dict(chk, finalizer_sha=H('9')), 'finalizerSha256 differs'), ('A06', 'image-set-differs', lambda: dict(chk, image_ids=[H('4')]), 'imageIds differs'),
        ('A07', 'expired', lambda: dict(chk, now=now + timedelta(seconds=301)), 'has expired'), ('A08', 'absent', lambda: dict(chk, op_id='op-nope'), 'absent or unreadable'),
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
        kwv = build()
        got_pod, got_orig = M.check_admission(**kwv), L.check_admission(**kwv)
        check(got_pod == got_orig, f'vector {vid} {name}: the copy answers exactly as the original reader ({got_pod})')
        check((got_pod == []) if expect is None else any(expect in x for x in got_pod), f'vector {vid} {name}: expected {expect or "pass"}')
    os.chmod(root, 0o777)
    a17p, a17o = M.check_admission(**chk), L.check_admission(**chk)
    check(a17p == a17o and any('not root-safe' in x for x in a17p), 'vector A17 world-writable-directory: refused, as the original')
    os.chmod(root, 0o755)
    a18p, a18o = M.check_admission(**dict(chk, owner_uid=UID + 1)), L.check_admission(**dict(chk, owner_uid=UID + 1))
    check(a18p == a18o and any('not root-safe' in x for x in a18p), 'vector A18 foreign-owner-chain: refused, as the original')
    seal_readme = open(os.path.join(DEP, 'seal', 'README.md')).read()
    ids = [v[0] for v in VECTORS] + ['A17', 'A18']
    check(len(ids) == 18 and all(i in seal_readme for i in ids), 'all 18 vector ids A01 to A18 are the ones listed in the claim tooling\'s README')

# ============================================================ read_pinned
with tempfile.TemporaryDirectory() as tmp:
    base = os.path.join(tmp, 'top')
    os.makedirs(os.path.join(base, 'd'))
    os.chmod(base, 0o755)
    os.chmod(os.path.join(base, 'd'), 0o755)
    f = os.path.join(base, 'd', 'x')
    open(f, 'w').write('hello')
    os.chmod(f, 0o644)
    check(M.read_pinned(f, base, UID, 100) == b'hello', 'read_pinned reads a safe file through the pinned chain')
    refused(lambda: M.read_pinned(f, base, UID + 1, 100), 'not root-safe', 'a chain owned by another uid refuses')
    refused(lambda: M.read_pinned(f, base, UID, 3), 'within 3 bytes', 'a file over the cap refuses')
    refused(lambda: M.read_pinned(os.path.join(base, 'd', 'absent'), base, UID, 100), 'absent: absent', 'a missing file refuses')
    os.chmod(f, 0o666)
    refused(lambda: M.read_pinned(f, base, UID, 100), 'closed to group and other writers', 'a world-writable file refuses')
    os.chmod(f, 0o644)
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
        refused(lambda: M.read_pinned(f, base, UID, 100), 'not a root-owned regular file', 'a file owned by another uid refuses (directories are the right owner)')
    finally:
        os.fstat = real_fstat
    os.mkfifo(os.path.join(base, 'd', 'fifo'))
    import signal

    class Blocked(BaseException):
        pass
    signal.signal(signal.SIGALRM, lambda *a: (_ for _ in ()).throw(Blocked()))
    signal.alarm(5)
    try:
        refused(lambda: M.read_pinned(os.path.join(base, 'd', 'fifo'), base, UID, 100), 'not a root-owned regular file', 'a FIFO refuses and does not block')
    finally:
        signal.alarm(0)

print(f'learn-pod-operator: {n} assertions passed')

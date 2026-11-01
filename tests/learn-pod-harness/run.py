#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/pod-harness/learn-pod-harness-r1.py, the fdev pod harness (Refs FS-GG/.github#5184, #4647).

What runs for real here: the harness's own code; operator r6's whole script (the shipped bytes, with its machine paths and pins substituted for
temporary fixtures exactly as tests/learn-operator-r6 does, every substitution matching once) for the cases whose verdict r6 decides before any
container (A27, exclusion, A29); the seal tool's synthetic manifest on the harness's synthetic tree. What is scripted: systemd and podman (a fake world
of units, pods and containers), the r6 RESULT of the A0 summary success path (it needs a running container runtime; its gate data is r6's own), the pod
operator's RESULTs and finalizer records of phase C, and the /proc and cgroup tree the N5b spike reads. No container, no systemd, no root, no network.
Clock: the world's own counter (`monotonic`) and one frozen `now`. Every refusal and every failing verdict has a causal control. Prints its count."""
import contextlib, hashlib, importlib.util, io, json, os, re, shutil, stat, subprocess, sys, tempfile, time
from datetime import datetime, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEP = os.path.join(ROOT, 'deployment', 'learn-finite-profile')
HARN = os.path.join(DEP, 'pod-harness', 'learn-pod-harness-r1.py')
R6 = os.path.join(DEP, 'operator', 'learn-operator-r6.py')
SEAL = os.path.join(DEP, 'seal', 'learn_seal_claim.py')
POD2 = os.path.join(DEP, 'pod-operator', 'learn-pod-operator-r2.py')
UID = os.getuid()
NOW = datetime(2026, 11, 1, 12, 0, 0, tzinfo=timezone.utc)
URL = 'https://github.com/FS-GG/.github/issues/5184#issuecomment-1'
n = 0
sha = lambda b: hashlib.sha256(b).hexdigest()


def mod(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


Hm = mod('harness', HARN)
L = mod('seal', SEAL)
P2 = mod('pod2', POD2)
R6SRC = open(R6).read()


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def raises(fn, needle, what):
    global n
    n += 1
    try:
        fn()
    except Hm.Refuse as e:
        if needle not in str(e):
            print(f'FAIL: {what}: refused with "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: not refused')
    sys.exit(1)


# ============================================================ pins: the harness's constants are the repository's own
check(Hm.R6_SHA == sha(open(R6, 'rb').read()), 'R6_SHA is the sha256 of operator r6 in this tree (what INSTALL-FDEV installs)')
check(Hm.READER_IMAGE == re.search(r"IMAGE_ID = '([0-9a-f]+)'", R6SRC).group(1), 'READER_IMAGE is operator r6\'s pinned reader image (its first gate)')
check(Hm.BASE_IMAGE in open(os.path.join(DEP, 'pod-image', 'learn-build-pod-app-r1.py')).read(), 'BASE_IMAGE is the pod image\'s pinned base')
check(Hm.F1_PRE_SHA.startswith('3db3a5f3') and Hm.F1_PRE in open(os.path.join(DEP, 'operator', 'README.md')).read(), 'the f1-pre record and its digest are the admitted ones')
check(Hm.SYNTHETIC_TAG == P2.SYNTHETIC_TAG, 'the synthetic declaration is the pod operator\'s')
check(Hm.SYN_SUBTREES == L.ALLOWED_SUBTREES, 'the synthetic tree has the seal tool\'s two pinned subtree names')

# ============================================================ plan: the admission request
PR = {'r6': '/v/learn-operator-r6.py', 'pod': '/v/learn-pod-operator-r2.py', 'self': '/v/learn-pod-harness-r1.py', 'home': '/h', 'view': '/v'}
p1 = Hm.plan(dict(Hm.PROD), 'run1')
check(p1 == Hm.plan(dict(Hm.PROD), 'run1') and p1['order'] == list(Hm.CASE_IDS), 'the plan is deterministic and lists every case in order')
check(all(c['uid'] == 30034 for c in p1['cases']) and p1['uid'] == 30034, 'every case runs as uid 30034')
check(all(set(c) >= {'id', 'phase', 'title', 'commands', 'creates', 'writes', 'expected', 'receipts', 'held', 'precondition'} for c in p1['cases']),
      'every case states what runs, what it creates and writes, the expected verdict, the receipts, what it is held on and its precondition')
names = ' '.join(x for c in p1['cases'] for x in c['creates'])
check(all(re.search(r'hq-run1-', x) for c in p1['cases'] for x in c['creates'] if 'learn-' in x and 'hq-' in x)
      and not re.search(r'learn-(op|pod|fin|pre)-(?!hq-run1-)', names.replace('learn-op-success', '')), 'every unit, pod and container the harness creates is named hq-<run>-*')
check({c['id'] for c in p1['cases'] if c['held']} == {'a0-r4-smoke', 'syn-precheck', 'syn-run', 'syn-reuse'}, 'held: the A0 r4 smoke (Home source) and phase C (the image pins)')
a27 = next(c for c in p1['cases'] if c['id'] == 'a27-unacked')
check(a27['commands'] == [['/usr/bin/python3', '-I', '/usr/local/libexec/fsgg-learn/learn-operator-r6.py', '--precheck-only', '--id', 'hq-run1-a27']],
      'A27 runs the installed operator r6 --precheck-only once, under the harness op id')
n5 = next(c for c in p1['cases'] if c['id'] == 'n5b-cgroups')['commands'][0]
check(n5[:4] == ['systemd-run', '--user', '--unit', 'learn-pod-hq-run1-n5b'] and 'Delegate=yes' in n5 and 'KillMode=control-group' in n5 and '--collect' in n5
      and n5[n5.index('--') + 1:-1] == ['/usr/bin/python3', '-I', Hm.PROD['self'], 'spike-cgroups', '--run-id', 'run1', '--nonce'],
      'the N5b spike runs inside a delegated learn-pod-* unit with the pod unit\'s cgroup properties, bounded by its own RuntimeMaxSec')
check(all('RuntimeMaxSec=' in ' '.join(cmd) for c in p1['cases'] for cmd in c['commands'] if cmd[:2] == ['systemd-run', '--user']),
      'every unit the harness starts carries its own RuntimeMaxSec (nothing outlives a dead harness)')
check(p1['casesSha256'] == sha(json.dumps(p1['cases'], sort_keys=True, separators=(',', ':')).encode()) and Hm.plan(dict(Hm.PROD), 'run2')['casesSha256'] != p1['casesSha256'],
      'the plan carries its cases digest, which differs per run id')
code_, out = None, io.StringIO()
with contextlib.redirect_stdout(out):
    code_ = Hm.main(['plan', '--run-id', 'run1'], deps={'self_sha': lambda: 'ab' * 32})
pj = json.loads(out.getvalue().split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0])
check(code_ == 0 and pj['harnessSha256'] == 'ab' * 32 and pj['casesSha256'] == p1['casesSha256'], 'the plan command prints the request with the harness sha256')
for bad, needle in ((['plan', '--run-id', 'AB'], 'run id'), (['plan', '--run-id', 'a-b-c'], 'run id'), (['case', '--run-id', 'run1', '--case', 'x', '--admission-ref', URL], 'unknown case'),
                    (['plan', '--run-id', 'run1', '--extra', 'x'], 'usage'), (['frob'], 'usage'), (['case', '--run-id', 'run1', '--case', 'census'], 'usage'),
                    (['plan', '--run-id', 'run1', '--run-id', 'run2'], 'usage')):
    raises(lambda bad=bad: Hm.parse(bad), needle, f'parse refuses {bad}')
check(Hm.parse(['case', '--run-id', 'run1', '--case', 'census', '--admission-ref', URL])[0] == 'case', 'parse control: a well-formed case invocation')


# ============================================================ a fake world: units, pods, containers, and operator r6 run for real
def sub1(s, pat, rep):
    out, k = re.subn(pat, lambda m: rep, s)
    if k != 1:
        print(f'FAIL: substitution {pat!r} matched {k} times')
        sys.exit(1)
    return out


class ReachedPrecheck(BaseException):
    """operator r6 catches Exception; this is not one, so reaching the precheck (the first container-runtime call) ends the run visibly"""


class Proc:
    def __init__(self, out='', rc=0):
        self.returncode, self.stdout, self.stderr = rc, out.encode(), b''


class World:
    def __init__(self, tmp):
        self.tmp = tmp
        self.home = os.path.join(tmp, 'home')
        self.state = os.path.join(self.home, '.local', 'state')
        for d in ('learn-custody', 'learn-evidence'):
            os.makedirs(os.path.join(self.state, d))
        self.view = os.path.join(tmp, 'top', 'view')
        self.acks = os.path.join(self.view, 'retirement-acks')
        os.makedirs(self.acks)
        for d in (os.path.join(tmp, 'top'), self.view, self.acks):
            os.chmod(d, 0o755)
        self.units, self.pods, self.ctrs = {}, set(), set()
        self.images = {Hm.READER_IMAGE, Hm.BASE_IMAGE}
        self.calls, self.clock, self.r6_runs, self.pod_results, self.records = [], 0.0, [], {}, {}
        self.stop_fails, self.start_fails = set(), set()
        self.slice = os.path.join(tmp, 'slice')
        os.makedirs(self.slice)
        for fn, v in (('memory.max', 6 << 30), ('pids.max', 1280), ('memory.current', 0), ('pids.current', 30)):
            open(os.path.join(self.slice, fn), 'w').write(f'{v}\n')
        # the f1-pre failure record, as on fdev
        os.makedirs(os.path.join(self.state, 'learn-evidence', Hm.F1_PRE))
        self.f1 = b'{"schema":"learn-op-retirement-failure/1","opId":"' + Hm.F1_PRE.encode() + b'"}'
        open(os.path.join(self.state, 'learn-evidence', Hm.F1_PRE, 'record.json'), 'wb').write(self.f1)
        self.r6_success = None

    def ack(self, content=None):
        p = os.path.join(self.acks, Hm.F1_PRE + '.retirement-ack')
        open(p, 'w').write((content or sha(self.f1)) + '\n')
        os.chmod(p, 0o644)

    def env(self, **over):
        e = dict(Hm.PROD, uid=30034, home=self.home, view=self.view, r6=R6, pod='/x/pod', self='/x/harness', production=False)
        e.update(over)
        return e

    def unit_rows(self, prefixes):
        return ''.join(f'{u}.service loaded {st} {"running" if st == "active" else "dead"} X\n' for u, st in sorted(self.units.items())
                       if any(re.fullmatch(p.replace('*', '.*'), u) for p in prefixes))

    def run_r6(self, argv):
        """operator r6, the shipped bytes, with machine paths substituted for this world's fixtures (as tests/learn-operator-r6 does)"""
        fx = {k: os.path.join(self.tmp, k) for k in ('seccomp.json', 'finalizer.py', 'python3')}
        for k, p in fx.items():
            if not os.path.exists(p):
                if k == 'seccomp.json':
                    shutil.copy(os.path.join(DEP, 'baseline', 'etc', 'seccomp-8bd52f8d.json'), p)
                else:
                    open(p, 'w').write(f'fixture {k}\n')
        shas = {k: sha(open(p, 'rb').read()) for k, p in fx.items()}
        s = R6SRC
        s = sub1(s, r"HOME='/mnt/storage/containers/learn-qualification/account'", f"HOME={self.home!r}")
        s = sub1(s, r"SLICE = '/sys/fs/cgroup/user.slice/user-30034.slice'", f"SLICE = {self.slice!r}")
        s = sub1(s, r"SECCOMP = '/etc/fsgg/learn/seccomp-8bd52f8d.json'; SECCOMP_SHA = '[0-9a-f]+'", f"SECCOMP = {fx['seccomp.json']!r}; SECCOMP_SHA = {shas['seccomp.json']!r}")
        s = sub1(s, r"FIN = '/usr/local/libexec/fsgg-learn/learn-finalizer-r4.py'; FIN_SHA = '[0-9a-f]+'", f"FIN = {fx['finalizer.py']!r}; FIN_SHA = {shas['finalizer.py']!r}")
        s = sub1(s, r"PY_SHA = '[0-9a-f]+'", f"PY_SHA = {shas['python3']!r}")
        s = sub1(s, r"sha_file\('/usr/bin/python3'\)", f"sha_file({fx['python3']!r})")
        s = sub1(s, r"ACK_DIR = '/usr/local/libexec/fsgg-learn/retirement-acks'", f"ACK_DIR = {self.acks!r}")
        s = sub1(s, r"chain_start='/', owner_uid=0\):", f"chain_start={os.path.join(self.tmp, 'top')!r}, owner_uid={UID}):")
        s = sub1(s, r"def root_chain_ok\(path\):\n", "def root_chain_ok(path):\n    return True, None   # TEST FIXTURE\n")
        world = self

        def fake_run(a, capture_output=True, timeout=None, cwd=None):
            if a[:3] == ['podman', 'image', 'inspect']:
                return Proc(Hm.READER_IMAGE + '\n') if Hm.READER_IMAGE in world.images else Proc('', 125)
            if a[:2] == ['systemctl', '--user'] and 'list-units' in a:
                return Proc(world.unit_rows(a[a.index('--plain') + 1:]))
            if a[:2] == ['podman', 'ps']:
                return Proc('\n'.join(sorted(world.ctrs)))
            raise ReachedPrecheck(a[:3])
        saved = (subprocess.run, os.geteuid, os.umask(0o022), os.getcwd(), dict(os.environ), sys.argv, time.sleep)
        os.umask(saved[2])
        buf, code = io.StringIO(), None
        def live_fds():
            out = set()
            for fd in os.listdir('/proc/self/fd'):           # the listing's own descriptor is in the listing and closed after it: keep only live ones
                try:
                    os.fstat(int(fd))
                    out.add(fd)
                except OSError:
                    pass
            return out
        fds0 = live_fds()
        try:
            subprocess.run, os.geteuid, sys.argv, time.sleep = fake_run, (lambda: 30034), ['learn-operator-r6.py'] + argv, (lambda s_: None)
            with contextlib.redirect_stdout(buf):
                try:
                    exec(compile(s, R6, 'exec'), {'__name__': '__main__', '__file__': R6})
                except SystemExit as e:
                    code = e.code
        except ReachedPrecheck:
            code = 'precheck'
        finally:
            subprocess.run, os.geteuid, _, cwd, env_, sys.argv, time.sleep = saved
            os.umask(saved[2])
            os.chdir(cwd)
            os.environ.clear()
            os.environ.update(env_)
            for fd in live_fds() - fds0:     # r6 holds its operator lock to process exit: release it as the process exit would
                try:
                    os.close(int(fd))
                except OSError:
                    pass
        return code, buf.getvalue()

    def run(self, argv, timeout=60):
        a = list(argv)
        self.calls.append(a)
        if a[:2] == ['/usr/bin/python3', '-I'] and a[2] == R6:
            opid = a[a.index('--id') + 1]
            self.r6_runs.append(opid)
            if getattr(self, 'r6_forge', None):            # a forged RESULT, for the verdict checks real r6 output cannot reach
                r = self.r6_forge(opid)
                return r['exitCode'], f'RESULT-BEGIN\n{json.dumps(r)}\nRESULT-END\n', ''
            code, out = self.run_r6(a[3:])
            if getattr(self, 'after_r6', None):
                self.after_r6()
            if code == 'precheck':                    # the success path needs a container runtime: the scripted RESULT carries r6's own gate data
                if self.r6_success is None:
                    return 1, '', 'precheck not scriptable'
                r = self.r6_success(opid)
                b = json.dumps(r)
                return r['exitCode'], f'RESULT-BEGIN\n{b}\nRESULT-END\n', ''
            return code, out, ''
        if a[:2] == ['/usr/bin/python3', '-I'] and a[2] == '/x/pod':
            opid, mode = a[a.index('--id') + 1], a[3]
            r = self.pod_results[(mode, len([c for c in self.calls if c[:3] == a[:3] and c[3] == mode]))]
            if mode == '--run' and opid in self.records:
                d = os.path.join(self.state, 'learn-evidence', opid)
                os.makedirs(d, exist_ok=True)
                open(os.path.join(d, 'record.json'), 'w').write(json.dumps(self.records[opid]))
                dd = os.path.join(self.state, 'learn-drive', opid)
                os.makedirs(dd, exist_ok=True)
                open(os.path.join(dd, 'drive.jsonl'), 'w').write('\n'.join(json.dumps(x) for x in self.drive_log) + '\n')
            return r['exitCode'], f'RESULT-BEGIN\n{json.dumps(r)}\nRESULT-END\n', ''
        if a[:2] == ['systemd-run', '--user'] and '--wait' not in a:
            name = a[a.index('--unit') + 1]
            if name in self.start_fails:
                return 1, '', 'Failed to start'
            self.units[name] = 'active'
            return 0, '', ''
        if a[:2] == ['systemd-run', '--user'] and '--wait' in a:
            return self.spike_unit(a)
        if a[:3] == ['systemctl', '--user', 'is-active']:
            u = a[3][:-len('.service')]
            if u in getattr(self, 'isactive_lies', ()) and u not in self.units:
                return 0, 'active\n', ''
            return (0 if self.units.get(u) == 'active' else 3), self.units.get(u, 'unknown') + '\n', ''
        if a[:3] == ['systemctl', '--user', 'stop']:
            for x in a[3:]:
                u = x[:-len('.service')]
                if u in self.units and u not in self.stop_fails:
                    del self.units[u]                       # a stopped transient unit is collected
            return 0, '', ''
        if a[:3] == ['systemctl', '--user', 'reset-failed']:
            return 0, '', ''
        if a[:3] == ['systemctl', '--user', 'list-units']:
            return 0, self.unit_rows(a[a.index('--plain') + 1:]), ''
        if a[:3] == ['podman', 'pod', 'ps']:
            return 0, '\n'.join(sorted(self.pods)), ''
        if a[:2] == ['podman', 'ps']:
            return 0, '\n'.join(sorted(self.ctrs)), ''
        if a[:3] == ['podman', 'image', 'exists']:
            return (0 if a[3] in self.images else 1), '', ''
        raise AssertionError(f'unexpected command {a}')

    def spike_unit(self, a):
        self.spiked = a
        if getattr(self, 'spike_json', None) is not None:
            sd = Hm.state_dir(self.env(), a[a.index('--run-id') + 1])
            os.makedirs(sd, 0o700, exist_ok=True)
            j = dict({'nonce': a[a.index('--nonce') + 1]}, **self.spike_json)       # the spike records the nonce it was given, unless a test says otherwise
            open(os.path.join(sd, 'n5b-spike.json'), 'w').write(json.dumps(j))
        return getattr(self, 'spike_rc', 0), '', ''

    def deps(self, **over):
        def read_json(p):
            try:
                return json.load(open(p))
            except (OSError, ValueError):
                return None

        def read_lines(p):
            try:
                return [json.loads(l) for l in open(p) if l.strip()]
            except (OSError, ValueError):
                return []

        def tick():
            self.clock += 1.0
            return self.clock
        d = {'run': self.run, 'geteuid': lambda: 30034, 'getpid': lambda: 4242, 'now': lambda: NOW, 'monotonic': tick, 'sleep': lambda s_: None,
             'read': lambda p: open(p).read(), 'read_json': read_json, 'read_lines': read_lines, 'exists': os.path.lexists, 'walk': os.walk,
             'sha_file': lambda p: sha(open(p, 'rb').read()) if os.path.exists(p) else None,
             'self_stat': lambda: os.stat(HARN), 'self_sha': lambda: sha(open(HARN, 'rb').read()), 'nonce': lambda: '9f' * 16}
        d.update(over)
        return d


def case(w, cid, env=None, deps=None, extra=None):
    a = {'--run-id': 'run1', '--case': cid, '--admission-ref': URL}
    a.update(extra or {})
    return Hm.run_case(w.env(**(env or {})), w.deps(**(deps or {})), 'run1', cid, a)


def sd(w):
    return Hm.state_dir(w.env(), 'run1')


def r6_success(w):
    """The A0 summary's success RESULT: r6's real gate data (taken from a real r6 run of this world up to its precheck) plus exit 0 and the precheck."""
    def make(opid):
        code, out = w.run_r6(['--precheck-only', '--id', 'probe-' + opid])
        assert code == 'precheck', code
        gates = {'retirementAcks': {'failures': [Hm.F1_PRE], 'acked': [Hm.F1_PRE], 'unacked': []}}
        d = os.path.join(w.state, 'learn-evidence', opid + '-pre')
        os.makedirs(d, exist_ok=True)
        open(os.path.join(d, 'record.json'), 'w').write(json.dumps({'schema': w.pre_schema}))
        return {'schema': 'home-learn-operator/1', 'exitCode': 0, 'errors': [], 'gates': gates,
                'precheck': {'assertions': {'capsZero': True, 'seccompEqual': True, 'precheckRecord': True}, 'recordBound': True}}
    return make


# ============================================================ phase A, case by case, each with its causal control
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'census')
    check(code == 0 and R['verdict'] == 'pass' and R['facts']['operatorR6Sha256'] == Hm.R6_SHA and all(R['facts']['images'].values()), f'census: a clean world passes: {R}')
    check(os.path.isfile(os.path.join(sd(w), 'census.json')) and json.load(open(os.path.join(sd(w), 'census.json')))['verdict'] == 'pass', 'census: the RESULT is written to the run directory')
    R, code = case(w, 'census')
    check(code == 5 and any('already ran' in e for e in R['errors']), 'a case runs once per run')
for what, mut in (('an active learn-op unit', lambda w: w.units.update({'learn-op-x': 'active'})), ('a failed learn-fin unit', lambda w: w.units.update({'learn-fin-x': 'failed'})),
                  ('a leftover pod', lambda w: w.pods.add('learn-pod-old')), ('a leftover container', lambda w: w.ctrs.add('learn-op-old')),
                  ('the reader image absent', lambda w: w.images.discard(Hm.READER_IMAGE)), ('the base image absent', lambda w: w.images.discard(Hm.BASE_IMAGE))):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        mut(w)
        R, code = case(w, 'census')
        check(code == 3 and R['verdict'] == 'fail' and os.path.isfile(os.path.join(sd(w), 'STOP')), f'census: {what} fails and stops the run')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'census', env={'r6': HARN})
    check(R['verdict'] == 'fail' and R['facts']['operatorR6Sha256'] != Hm.R6_SHA, 'census: an operator r6 off its pin fails')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.units['learn-op-inert'] = 'inactive'
    R, code = case(w, 'census')
    check(R['verdict'] == 'pass', 'census control: an inactive unit is not busy')

# A27: operator r6, for real, refuses on the unacknowledged f1-pre record
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'a27-unacked')
    check(code == 0 and R['verdict'] == 'pass' and R['facts']['errors'] == [f'retirement-failure-unacknowledged: {Hm.F1_PRE}'] and R['facts']['rc'] == 5,
          f'A27: operator r6 (the shipped bytes) refuses with exactly the f1-pre record, and the harness passes the case: {R.get("facts")}')
    check(w.r6_runs == ['hq-run1-a27'] and R['censusBefore']['ok'] and R['censusAfter']['ok'], 'A27: one r6 run under the harness op id, clean census before and after')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    os.makedirs(os.path.join(w.state, 'learn-evidence', 'other-fail'))
    open(os.path.join(w.state, 'learn-evidence', 'other-fail', 'record.json'), 'wb').write(b'{"schema":"learn-op-retirement-failure/1"}')
    R, code = case(w, 'a27-unacked')
    check(R['verdict'] == 'fail' and code == 3 and os.path.isfile(os.path.join(sd(w), 'STOP')), 'A27: a second unacknowledged record is not the expected refusal: fail and STOP')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.r6_forge = lambda opid: {'exitCode': 5, 'errors': [f'retirement-failure-unacknowledged: {Hm.F1_PRE}'], 'gates': {'retirementAcks': {'unacked': [Hm.F1_PRE]}},
                               'precheck': {'start': 'x'}}
    R, code = case(w, 'a27-unacked')
    check(R['verdict'] == 'fail', 'A27: the right refusal with a precheck op started anyway fails the case (the gate must refuse BEFORE the precheck)')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    R, code = case(w, 'a27-unacked')
    check(R['verdict'] == 'not-applicable' and w.r6_runs == [] and not os.path.exists(os.path.join(sd(w), 'STOP')), 'A27: with the ack restored the negative case does not apply and r6 is not run')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.units['learn-pod-busy'] = 'active'
    R, code = case(w, 'a27-unacked')
    check(code == 5 and R['verdict'] == 'refused' and w.r6_runs == [] and not os.path.exists(os.path.join(sd(w), 'a27-unacked.json')),
          'a case whose census before is not clean refuses, runs nothing and writes nothing')

# A0 summary: the success path is scripted (container runtime); the ack and the record decide
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.r6_success, w.pre_schema = r6_success(w), 'learn-op-success/1'
    R, code = case(w, 'a0-summary')
    check(code == 0 and R['verdict'] == 'pass' and R['facts']['retirementAcks'] == {'failures': [Hm.F1_PRE], 'acked': [Hm.F1_PRE], 'unacked': []}
          and R['facts']['precheckRecordSchema'] == 'learn-op-success/1', f'A0 summary: failures == acked == [f1-pre], unacked == [], the precheck record retired: {R.get("facts")}')
    check(w.r6_runs[0] == 'hq-run1-a0', 'A0 summary: r6 runs under the harness op id (its gates are r6\'s own up to the precheck)')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.r6_success, w.pre_schema = r6_success(w), 'learn-op-retirement-failure/1'
    R, code = case(w, 'a0-summary')
    check(R['verdict'] == 'fail', 'A0 summary: a precheck op that ended in a retirement failure fails the case')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'a0-summary')
    check(R['verdict'] == 'fail' and w.r6_runs == [], 'A0 summary: without the ack (INSTALL-FDEV step 2 not run) it fails without running r6')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack('0' * 64)
    w.r6_success, w.pre_schema = r6_success(w), 'learn-op-success/1'
    R, code = case(w, 'a0-summary')
    check(R['verdict'] == 'fail' and any('retirement-failure-unacknowledged' in e for e in R['facts']['errors']), 'A0 summary: an ack with another digest makes r6 refuse: fail (A27b negative)')

# exclusion and A29: real r6 against units the harness starts and stops by name
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    R, code = case(w, 'exclusion-reader')
    check(code == 0 and R['verdict'] == 'pass' and 'exclusion refused: a pod op is active: learn-pod-hq-run1-x' in R['facts']['errors'], f'exclusion: r6 refuses while the dummy pod unit is active: {R.get("facts")}')
    started = [c for c in w.calls if c[:2] == ['systemd-run', '--user']]
    check(started == [Hm.dummy_argv('learn-pod-hq-run1-x')] and 'learn-pod-hq-run1-x' not in w.units and R['censusAfter']['ok'], 'exclusion: one dummy unit, started and stopped by its exact name')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.stop_fails.add('learn-pod-hq-run1-x')
    R, code = case(w, 'exclusion-reader')
    check(R['verdict'] == 'fail' and os.path.isfile(os.path.join(sd(w), 'STOP')), 'exclusion: a dummy that does not stop fails the case and stops the run')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.start_fails.add('learn-pod-hq-run1-x')
    R, code = case(w, 'exclusion-reader')
    check(R['verdict'] == 'fail' and w.r6_runs == [] and any('did not start' in e for e in R['errors']), 'exclusion: a dummy that does not start fails before r6 runs')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    R, code = case(w, 'a29-cap')
    check(code == 0 and R['verdict'] == 'pass' and 'concurrency refused: 2 ops already active (max 2)' in R['facts']['errors'], f'A29: r6 refuses a third op while two are active: {R.get("facts")}')
    check(not any(u.startswith('learn-op-hq-run1-c') for u in w.units) and R['censusAfter']['ok'], 'A29: both dummy units are stopped')
# the controls: the same r6 with no dummy proceeds past those gates (so the refusals above are caused by the dummies)
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    code_, out_ = w.run_r6(['--precheck-only', '--id', 'hq-run1-ctl'])
    check(code_ == 'precheck', 'control: without dummy units r6 passes the sweep, exclusion, concurrency and ack gates and reaches its precheck')

# N2 (#5204 review): the census after is taken on every path past the identity guards: a refusal before the case, an exception inside it
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.units['learn-pod-busy'] = 'active'
    R, code = case(w, 'a27-unacked')
    check(R['verdict'] == 'refused' and 'censusAfter' in R and R['censusAfter']['busyUnits'] == ['learn-pod-busy.service'], 'N2: a refused case records the census after')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.start_fails.add('learn-pod-hq-run1-x')
    R, code = case(w, 'exclusion-reader')
    check(R['verdict'] == 'fail' and R['censusAfter']['ok'] is True, 'N2: a case that raised records the census after')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'census', deps={'geteuid': lambda: 1000})
    check('censusAfter' not in R and not w.calls, 'N2 control: before the identity guards pass nothing runs, not even the census')
# H6: the census after alone fails a case that otherwise passed (a stray pod appears while r6 runs)
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.after_r6 = lambda: w.pods.add('learn-pod-stray')
    R, code = case(w, 'a27-unacked')
    check(R['verdict'] == 'fail' and R['facts']['rc'] == 5 and any('census after' in e for e in R['errors']), 'N3: a case whose own verdict passes fails on an unclean census after')
# H7: a dummy unit that is still active after the stop fails the case even when the listing no longer shows it
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    w.isactive_lies = {'learn-pod-hq-run1-x'}
    R, code = case(w, 'exclusion-reader')
    check(R['verdict'] == 'fail' and R['censusAfter']['ok'] and R['facts']['dummyStates']['learn-pod-hq-run1-x'] == 'active',
          'N3: a dummy that reads active after the stop fails the case on its own readback')
# H16: leftover learn-pre-* and learn-op-* pods make the census unclean
for pod_ in ('learn-pre-old', 'learn-op-old'):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.pods.add(pod_)
        R, code = case(w, 'census')
        check(R['verdict'] == 'fail' and R['facts']['census']['pods'] == [pod_], f'N3: a leftover {pod_} pod makes the census unclean')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.pods.add('unrelated-pod')
    R, code = case(w, 'census')
    check(R['verdict'] == 'pass', 'N3 control: a pod that is not learn-* is not the harness\'s concern')
# H21: the stops name only the units the harness started
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    case(w, 'exclusion-reader')
    stops = [x for c in w.calls if c[:3] in (['systemctl', '--user', 'stop'], ['systemctl', '--user', 'reset-failed']) for x in c[3:]]
    check(stops and set(stops) == {'learn-pod-hq-run1-x.service'}, f'N3: exclusion stops and resets only its own dummy unit: {stops}')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    case(w, 'a29-cap')
    stops = [x for c in w.calls if c[:3] in (['systemctl', '--user', 'stop'], ['systemctl', '--user', 'reset-failed']) for x in c[3:]]
    check(set(stops) == {'learn-op-hq-run1-c1.service', 'learn-op-hq-run1-c2.service'}, f'N3: A29 stops and resets only its own two dummy units: {stops}')
# H2b: a root-owned copy that group or other can write is refused
for mode_ in (0o100664, 0o100646):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        st_ = type('S', (), {'st_uid': 0, 'st_mode': mode_})()
        R, code = case(w, 'census', env={'production': True, 'owner_uid': 0}, deps={'self_stat': lambda: st_})
        check(code == 5 and any('root-owned copy' in e for e in R['errors']) and not w.calls, f'N3: a root-owned harness copy of mode {oct(mode_ & 0o777)} refuses')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    st_ = type('S', (), {'st_uid': 0, 'st_mode': 0o100755})()
    R, code = case(w, 'census', env={'production': True, 'owner_uid': 0}, deps={'self_stat': lambda: st_})
    check(R['verdict'] == 'pass', 'N3 control: a root-owned 0755 copy runs')
# H3: a case RESULT is created exclusively, never overwritten
with tempfile.TemporaryDirectory() as tmp:
    f_ = os.path.join(tmp, 'r.json')
    Hm.write_once(f_, b'one')
    try:
        Hm.write_once(f_, b'two')
        check(False, 'N3: write_once refuses an existing file')
    except FileExistsError:
        check(open(f_, 'rb').read() == b'one', 'N3: write_once refuses an existing file and leaves it unchanged (O_EXCL)')

# held and STOP
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'a0-r4-smoke')
    check(code == 0 and R['verdict'] == 'held' and 'A0 r4' in R['held'] and not w.calls and not os.path.exists(sd(w)), 'a held case runs nothing and writes nothing')
    w.units['learn-op-x'] = 'active'
    case(w, 'census')
    w.units.clear()
    R, code = case(w, 'a27-unacked')
    check(code == 5 and any('STOP' in e for e in R['errors']) and w.r6_runs == [], 'after a failed case, every later case refuses (STOP)')
# the guards
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    R, code = case(w, 'census', deps={'geteuid': lambda: 1000})
    check(code == 5 and any('uid 30034' in e for e in R['errors']) and not w.calls, 'another uid refuses before anything')
    R, code = case(w, 'census', extra={'--admission-ref': 'https://example.org/x'})
    check(code == 5 and any('admission-ref' in e for e in R['errors']) and not w.calls, 'an admission reference that is not a GitHub comment URL refuses')
    R, code = case(w, 'census', env={'production': True, 'owner_uid': UID + 1})
    check(code == 5 and any('root-owned copy' in e for e in R['errors']) and not w.calls, 'a harness copy not owned by root refuses (production)')
    R, code = case(w, 'census', env={'production': True, 'owner_uid': os.stat(HARN).st_uid}) if not (stat.S_IMODE(os.stat(HARN).st_mode) & 0o022) else (None, 0)
    check(R is None or R['verdict'] == 'pass', 'guard control: a copy owned by the expected owner and closed to writers runs')

# ============================================================ phase B: the N5b spike, against a fake podman, /proc and cgroup tree
UNIT = '/user.slice/user-30034.slice/user@30034.service/app.slice/learn-pod-hq-run1-n5b.service'


class SpikeWorld:
    def __init__(self, tmp, placement=None, split_refused=False, rm_fails=False, leftover=False, outside=False):
        self.tmp, self.proc, self.cg = tmp, os.path.join(tmp, 'proc'), os.path.join(tmp, 'cg')
        self.home = os.path.join(tmp, 'home')
        os.makedirs(os.path.join(self.proc, 'self'))
        me = '/user.slice/user-30034.slice/session-1.scope' if outside else UNIT
        open(os.path.join(self.proc, 'self', 'cgroup'), 'w').write(f'0::{me}\n')
        os.makedirs(self.cg + UNIT)
        open(self.cg + UNIT + '/cgroup.procs', 'w').write('4242\n')
        self.pids = {'learn-pod-hq-run1-n5b-infra': (501, 601), 'learn-pod-hq-run1-n5b-a': (502, 602), 'learn-pod-hq-run1-n5b-b': (503, 603)}
        default = {pid: UNIT + '/libpod-' + str(pid) for v in self.pids.values() for pid in v}
        self.placement = dict(default)
        self.placement.update(placement or {})
        for pid, cgp in self.placement.items():
            os.makedirs(os.path.join(self.proc, str(pid)), exist_ok=True)
            open(os.path.join(self.proc, str(pid), 'cgroup'), 'w').write(f'0::{cgp}\n')
            if leftover and pid == 502:
                os.makedirs(self.cg + cgp, exist_ok=True)
                open(self.cg + cgp + '/cgroup.procs', 'w').write('502\n')
        self.split_refused, self.rm_fails, self.calls, self.exists = split_refused, rm_fails, [], False

    def run(self, a, timeout=60):
        self.calls.append(a)
        if a[:3] == ['podman', 'pod', 'create']:
            self.exists = True
            return 0, 'podid\n', ''
        if a[:3] == ['podman', 'run', '-d']:
            return (125, '', 'Error: --cgroups=split is not supported in a pod') if self.split_refused else (0, 'cid\n', '')
        if a[:3] == ['podman', 'pod', 'inspect']:
            return 0, '/user.slice/libpod-pod.slice\n', ''
        if a[:2] == ['podman', 'inspect']:
            p_, c_ = self.pids[a[-1]]
            return 0, f'{p_} {c_}\n', ''
        if a[:3] == ['podman', 'pod', 'rm']:
            if not self.rm_fails:
                self.exists = False
            return 0, '', ''
        if a[:3] == ['podman', 'pod', 'exists']:
            return (0 if self.exists else 1), '', ''
        raise AssertionError(f'unexpected {a}')

    def go(self):
        env = dict(Hm.PROD, home=self.home, proc=self.proc, cgroot=self.cg)
        deps = {'run': self.run, 'read': lambda p: open(p).read(), 'walk': os.walk, 'getpid': lambda: 4242}
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            code = Hm.spike_cgroups(env, deps, 'run1', '9f' * 16)
        return code, json.load(open(os.path.join(Hm.state_dir(env, 'run1'), 'n5b-spike.json')))


with tempfile.TemporaryDirectory() as tmp:
    code, S = SpikeWorld(tmp).go()
    check(code == 0 and S['verdict'] == 'members-and-infra-under-unit' and S['podRemoved'] and S['leftoverProcs'] == [] and S['unitCgroup'] == UNIT,
          f'spike: members, conmon and infra under the unit cgroup: established, the pod removed and read back gone: {S}')
    check(set(S['members']) == {'learn-pod-hq-run1-n5b-infra', 'learn-pod-hq-run1-n5b-a', 'learn-pod-hq-run1-n5b-b'} and all(m['pidUnderUnit'] and m['conmonUnderUnit'] for m in S['members'].values()),
          'spike: every member is recorded with its pid and conmon cgroup')
    runs = [c for c in SpikeWorld(os.path.join(tmp, 'b')).calls]
for what, kw, verdict in (('the infra container outside the unit', dict(placement={501: '/user.slice/user-30034.slice/user@30034.service/user.slice/libpod-501'}), 'not-under-unit'),
                          ('a member\'s conmon outside the unit', dict(placement={602: '/user.slice/user-30034.slice/user@30034.service/user.slice/libpod-conmon'}), 'not-under-unit'),
                          ('a sibling prefix that only LOOKS like the unit', dict(placement={503: UNIT + '-other/x'}), 'not-under-unit'),
                          ('--cgroups=split refused for pod members', dict(split_refused=True), 'split-refused'),
                          ('a pod that is not removed', dict(rm_fails=True), 'incomplete'),
                          ('a process left in the unit cgroup', dict(leftover=True), 'incomplete'),
                          ('a spike not inside its unit', dict(outside=True), 'incomplete')):
    with tempfile.TemporaryDirectory() as tmp:
        sw = SpikeWorld(tmp, **kw)
        code, S = sw.go()
        check(S['verdict'] == verdict and sw.calls[-2][:3] == ['podman', 'pod', 'rm'] and sw.calls[-1][:3] == ['podman', 'pod', 'exists'],
              f'spike: {what} gives {verdict}, and the pod is removed and read back on every path')
        if kw.get('outside'):
            check(not any(c[:3] == ['podman', 'pod', 'create'] for c in sw.calls), 'spike: outside its unit it creates nothing')
with tempfile.TemporaryDirectory() as tmp:
    sw = SpikeWorld(tmp)
    sw.go()
    pr = [c for c in sw.calls if c[:3] == ['podman', 'run', '-d']]
    check(len(pr) == 2 and all('--cgroups=split' in c and '--pull=never' in c and '--cap-drop=all' in c and c[c.index('--pod') + 1] == 'learn-pod-hq-run1-n5b' for c in pr)
          and [c for c in sw.calls if c[:3] == ['podman', 'pod', 'create']][0] == ['podman', 'pod', 'create', '--name', 'learn-pod-hq-run1-n5b', '--network=none', '--share=net',
                                                                                    '--infra-name', 'learn-pod-hq-run1-n5b-infra'],
          'spike: the pod is created as the pod operator creates its pod (network none, shared net, named infra) and both members use --cgroups=split')
    check(P2.common_opts({'seccomp': '/s'}, True)[0] == '--cgroups=split' and "'--network=none', '--share=net', '--infra-name'" in open(POD2).read(),
          'spike: the options it tests are the pod operator\'s own (the run uses --cgroups=split; the pod is created network-less with a named infra)')
# the n5b case reads the spike's RESULT
for what, sj, verdict in (('established', {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'members-and-infra-under-unit', 'podRemoved': True, 'leftoverProcs': []}, 'pass'),
                          ('not under the unit', {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'not-under-unit', 'podRemoved': True, 'leftoverProcs': []}, 'finding'),
                          ('split refused', {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'split-refused', 'podRemoved': True, 'leftoverProcs': []}, 'finding'),
                          ('incomplete', {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'incomplete', 'podRemoved': True, 'leftoverProcs': []}, 'fail'),
                          ('a pod not removed', {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'members-and-infra-under-unit', 'podRemoved': False, 'leftoverProcs': []}, 'fail'),
                          ('no spike RESULT', None, 'fail')):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.spike_json = sj
        R, code = case(w, 'n5b-cgroups')
        check(R['verdict'] == verdict and w.spiked == Hm.spike_unit_argv(w.env(), 'run1', '9f' * 16), f'n5b case: {what} is {verdict}')
# N1 (#5204 review): a stale spike RESULT is refused before the spike starts; the unit must succeed; the RESULT must carry this invocation's nonce
OKSJ = {'schema': Hm.SPIKE_SCHEMA, 'verdict': 'members-and-infra-under-unit', 'podRemoved': True, 'leftoverProcs': []}
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    os.makedirs(sd(w), 0o700)
    open(os.path.join(sd(w), 'n5b-spike.json'), 'w').write(json.dumps(dict(OKSJ, nonce='9f' * 16)))
    R, code = case(w, 'n5b-cgroups')
    check(code == 5 and R['verdict'] == 'refused' and any('already exists' in e for e in R['errors']) and not getattr(w, 'spiked', None),
          'N1: a spike RESULT that predates the invocation refuses the case before the spike unit starts (the reviewer\'s stale-spike probe)')
for what, kw, verdict in (('the unit failed (rc 1)', dict(spike_rc=1), 'fail'), ('another invocation\'s nonce', dict(spike_json=dict(OKSJ, nonce='00' * 16)), 'fail'),
                          ('another schema', dict(spike_json=dict(OKSJ, schema='other/1')), 'fail'), ('control: rc 0, this nonce, the schema', {}, 'pass')):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.spike_json = OKSJ
        for k_, v_ in kw.items():
            setattr(w, k_, v_)
        R, code = case(w, 'n5b-cgroups')
        check(R['verdict'] == verdict, f'N1: {what} is {verdict}')
for v_ in Hm.SPIKE_VERDICTS:
    with tempfile.TemporaryDirectory() as tmp:
        sw = SpikeWorld(tmp, **({'split_refused': True} if v_ == 'split-refused' else {'placement': {501: '/elsewhere'}} if v_ == 'not-under-unit' else {}))
        code, S_ = sw.go()
        check(S_['verdict'] == v_ and code == 0 and S_['nonce'] == '9f' * 16, f'N1: the spike exits 0 for the definite verdict {v_} and records its nonce')
with tempfile.TemporaryDirectory() as tmp:
    code, S_ = SpikeWorld(tmp, rm_fails=True).go()
    check(code == 3 and S_['verdict'] == 'incomplete', 'N1: an incomplete spike exits 3, so its unit fails')
raises(lambda: Hm.parse(['spike-cgroups', '--run-id', 'run1']), 'usage', 'the spike needs its nonce')

# ============================================================ phase C: the synthetic op (scripted pod operator RESULTs and finalizer records)
IDENT = 'ab' * 32
SYN = json.dumps(['learn-pod-plan/1', 'cd' * 32, 'input:synthetic'], separators=(',', ':'))
OPID = 'hq-run1-syn'


def syn_world(tmp, rec_over=None, drive_end=None, run_over=None):
    w = World(tmp)
    ok_pre = {'exitCode': 0, 'errors': [], 'inputProvenance': 'synthetic', 'gates': {'inputMounted': False}}
    ok_run = dict({'exitCode': 0, 'errors': [], 'inputProvenance': 'synthetic', 'gates': {'inputMounted': True}}, **(run_over or {}))
    w.pod_results = {('--precheck-only', 1): ok_pre, ('--run', 1): ok_run,
                     ('--precheck-only', 2): {'exitCode': 5, 'errors': [f'op id already used: {OPID}'], 'gates': {'inputMounted': False}}}
    ck = {'roles': {r_: {'custody': {'verdict': 'accepted'}} for r_ in ('host', 'recv', 'feed')}}
    rec = {'schema': 'learn-op-pod-success/1', 'inputProvenance': 'synthetic', 'inputRead': True, 'containersStartedWithInput': [f'learn-pod-{OPID}-feed'],
           'checkpoints': [ck] * 5, 'opUnit': {'ExecMainStatus': '0'}}
    rec.update(rec_over or {})
    w.records[OPID] = rec
    w.drive_log = [{'event': 'authority'}, drive_end or {'event': 'end', 'verdict': 'complete'}]
    return w


SA = {'--argv-json': SYN, '--source-identity': IDENT}
with tempfile.TemporaryDirectory() as tmp:
    w = syn_world(tmp)
    for cid in ('syn-precheck', 'syn-run', 'syn-reuse'):
        R, code = case(w, cid, extra=SA, env={'held_override': True})
        check(R['verdict'] == 'held' and code == 0, f'{cid} is held in this revision (the image pins), whatever the arguments')


def unheld(cid):
    """phase C's executors, driven directly (their held flag lifts with the pin revision): the verdict logic is what is tested"""
    real = Hm.cases

    def cs(env, run_id, argv_json=None, ident=None):
        return [dict(x, held=None) for x in real(env, run_id, argv_json, ident)]
    return cs


real_cases = Hm.cases
try:
    Hm.cases = unheld(None)
    with tempfile.TemporaryDirectory() as tmp:
        w = syn_world(tmp)
        R, code = case(w, 'syn-precheck', extra=SA)
        check(R['verdict'] == 'pass', f'syn-precheck: exit 0, synthetic, input untouched: {R}')
        pc = [c for c in w.calls if c[2:3] == ['/x/pod']][0]
        check(pc == ['/usr/bin/python3', '-I', '/x/pod', '--precheck-only', '--id', OPID, '--argv-json', SYN, '--source-identity', IDENT], 'syn-precheck: the exact pod operator argv')
        R, code = case(w, 'syn-run', extra=SA)
        check(R['verdict'] == 'pass' and R['facts']['checkpoints'] == 5, f'syn-run: success record, synthetic, the feed alone with /in, five accepted checkpoints, ExecMainStatus 0, drive complete: {R.get("facts")}')
        R, code = case(w, 'syn-reuse', extra=SA)
        check(R['verdict'] == 'pass', 'syn-reuse: a second precheck of the same op id is refused (op id already used)')
    with tempfile.TemporaryDirectory() as tmp:
        w = syn_world(tmp)
        w.pod_results[('--precheck-only', 1)]['inputProvenance'] = 'real'
        R, code = case(w, 'syn-precheck', extra=SA)
        check(R['verdict'] == 'fail', 'N3: a syn-precheck RESULT that is not synthetic fails the case')
    for what, kw in (('a real-provenance record', dict(rec_over={'inputProvenance': 'real'})), ('a killed-retired record', dict(rec_over={'schema': 'learn-op-pod-killed-retired/1'})),
                     ('input never read', dict(rec_over={'inputRead': False})), ('another container with /in', dict(rec_over={'containersStartedWithInput': [f'learn-pod-{OPID}-feed', 'x']})),
                     ('four checkpoints', dict(rec_over={'checkpoints': [{'roles': {'host': {'custody': {'verdict': 'accepted'}}}}] * 4})),
                     ('a rejected checkpoint archive', dict(rec_over={'checkpoints': [{'roles': {'host': {'custody': {'verdict': 'rejected:budget'}}}}] * 5})),
                     ('a pod unit that exited 3 (driver aborted)', dict(rec_over={'opUnit': {'ExecMainStatus': '3'}})),
                     ('a drive log that ends aborted', dict(drive_end={'event': 'end', 'verdict': 'aborted'})),
                     ('a run RESULT that is not synthetic', dict(run_over={'inputProvenance': 'real'}))):
        with tempfile.TemporaryDirectory() as tmp:
            w = syn_world(tmp, **kw)
            R, code = case(w, 'syn-run', extra=SA)
            check(R['verdict'] == 'fail' and os.path.isfile(os.path.join(sd(w), 'STOP')), f'syn-run: {what} fails the case and stops the run')
    with tempfile.TemporaryDirectory() as tmp:
        w = syn_world(tmp)
        del w.records[OPID]
        R, code = case(w, 'syn-run', extra=SA)
        check(R['verdict'] == 'fail' and R['facts']['recordSchema'] is None, 'syn-run: no finalizer record within the wait fails the case')
    with tempfile.TemporaryDirectory() as tmp:
        w = syn_world(tmp)
        for bad in ({'--argv-json': json.dumps(['learn-pod-plan/1', 'cd' * 32]), '--source-identity': IDENT}, {'--argv-json': SYN, '--source-identity': 'x'}, {}):
            R, code = case(w, 'syn-precheck', extra=bad)
            check(code == 5 and R['verdict'] == 'refused' and not [c for c in w.calls if c[2:3] == ['/x/pod']], f'syn cases refuse a non-synthetic argv or a bad identity before anything runs: {list(bad)}')
finally:
    Hm.cases = real_cases

# ============================================================ the synthetic tree: the seal tool's synthetic mode accepts it, and it holds no real data
with tempfile.TemporaryDirectory() as tmp:
    real = os.path.join(tmp, 'real-spool')
    for st_ in L.ALLOWED_SUBTREES:
        os.makedirs(os.path.join(real, st_))
        open(os.path.join(real, st_, 'x.ready'), 'w').write('real')
    out = os.path.join(tmp, 'syn')
    r = Hm.synth_tree(out, 'run1-n1')
    m = L.take_synthetic_manifest(out, now=NOW, extra_real_roots=[real])
    check(m['provenance'] == 'synthetic' and len(m['files']) == 3 and m['subtrees'] == sorted(L.ALLOWED_SUBTREES), 'synth-tree: the seal tool takes a synthetic manifest of it (three files, both pinned subtrees)')
    check(not any(f['path'].endswith('.ready') for f in m['files']), 'synth-tree: no *.ready batch (the drain has nothing to send; the receiver\'s acceptance stays the genuine run\'s)')
    check(L.synthetic_identity(m) != L.synthetic_identity(L.take_synthetic_manifest(Hm.synth_tree(os.path.join(tmp, 'syn2'), 'run1-n2')['out'], now=NOW, extra_real_roots=[real])),
          'synth-tree: another nonce is another synthetic identity')
    check(L.synthetic_identity(m) == L.synthetic_identity(L.take_synthetic_manifest(Hm.synth_tree(os.path.join(tmp, 'syn3'), 'run1-n1')['out'], now=NOW, extra_real_roots=[real])),
          'synth-tree: the same nonce gives the same identity (deterministic bytes and mtimes)')
    raises(lambda: Hm.synth_tree(out, 'run1-n1'), 'must not exist', 'synth-tree refuses an existing directory')
    raises(lambda: Hm.synth_tree(os.path.join(tmp, 'syn4'), 'bad nonce'), 'bad nonce', 'synth-tree refuses a bad nonce')

# ============================================================ summary
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.ack()
    case(w, 'census')
    case(w, 'a27-unacked')
    s = Hm.summary(w.env(), 'run1', w.deps()['read_json'])
    check(s['cases']['census'] == 'pass' and s['cases']['a27-unacked'] == 'not-applicable' and s['cases']['a0-summary'] == 'not-run' and s['stop'] is None
          and sum(s['counts'].values()) == len(Hm.CASE_IDS), 'summary: one verdict per case, not-run for the rest, no STOP')

print(f'learn-pod-harness: {n} assertions passed')

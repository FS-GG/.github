#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile/pod-operator/learn-pod-operator-r2.py, the pod operator START (Refs FS-GG/.github#4647, #5183).

Drives main(), the precheck, the credential generator and the recovery driver against fake worlds: a scripted podman/systemctl (the same style as the
finalizer r5 and r1 suites), a fake /proc and cgroup tree, a fake mount table and statvfs, and a fake finalizer that answers checkpoint requests in
finalizer r5's own ack format (tests/learn-finalizer-r5 checks that format against the real finalizer). The step runner and the probe run for real under
/bin/sh with /out replaced by a temporary directory; the credential generator runs the real openssl in a temporary directory. No container, no systemd,
no root, no network. Every refusal has a causal control: the same world passes once the one broken fact is restored. Prints its own assertion count."""
import ast, contextlib, copy, hashlib, importlib.util, io, json, os, re, shutil, stat, subprocess, sys, tempfile
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DEP = os.path.join(ROOT, 'deployment', 'learn-finite-profile')
POD = os.path.join(DEP, 'pod-operator', 'learn-pod-operator-r2.py')
POD1 = os.path.join(DEP, 'pod-operator', 'learn-pod-operator-r1.py')
SEAL = os.path.join(DEP, 'seal', 'learn_seal_claim.py')
R6 = os.path.join(DEP, 'operator', 'learn-operator-r6.py')
FIN = os.path.join(DEP, 'finalizer', 'learn-finalizer-r5.py')
SECCOMP = os.path.join(DEP, 'baseline', 'etc', 'seccomp-8bd52f8d.json')
OPENSSL = shutil.which('openssl')


def mod(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


M = mod('pod2', POD)
L = mod('seal', SEAL)
UID = os.getuid()
SELF_SHA = hashlib.sha256(open(POD, 'rb').read()).hexdigest()
n = 0
H = lambda c: c * 64
sha = lambda b: hashlib.sha256(b).hexdigest()


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def raises(fn, exc, needle, what):
    global n
    n += 1
    try:
        fn()
    except exc as e:
        if needle not in str(e):
            print(f'FAIL: {what}: raised "{e}", expected "{needle}"')
            sys.exit(1)
        return
    print(f'FAIL: {what}: did not raise')
    sys.exit(1)


# ============================================================ verbatim blocks: byte for byte the originals
def segs(path):
    s = open(path).read()
    out = {}
    for node in ast.parse(s).body:
        if isinstance(node, (ast.FunctionDef, ast.ClassDef)):
            out[node.name] = ast.get_source_segment(s, node)
        elif isinstance(node, ast.Assign):
            out[ast.get_source_segment(s, node).split(' = ')[0]] = ast.get_source_segment(s, node)
    return out


p2, p1, ls, r6 = segs(POD), segs(POD1), segs(SEAL), segs(R6)
for name in ('DIR_FLAGS', 'ADMIT_FIELDS', '_check_dir_fd', 'open_dir_chain', 'check_admission'):
    check(p2[name] == ls[name], f'{name} is byte for byte the one in learn_seal_claim.py')
for name in ('EVID_MAX_BYTES, EVID_MAX_ENTRIES', 'read_record_nofollow', 'evidence_usage', 'read_ack_pinned', 'retirement_gate', 'parse_unit_rows', 'used_ids',
             'kver', 'norm_rule', 'expected_set', 'effective_set', 'dump_cmd'):
    check(p2[name] == r6[name], f'{name} is byte for byte the one in operator r6')
R1_SAME = ('SCHEMA', 'MAX_TTL_S', 'OPID', 'HEX64', 'POD_MEMORY_HEADROOM, POD_PIDS_HEADROOM, POD_MEMAVAILABLE', 'SLICE_MEMORY_MAX, SLICE_PIDS_MAX', 'TRANSITIONING',
           'Refuse', 'refuse', 'HELD_LOCKS', 'ACK_DIR', 'sha_bytes', 'read_pinned', 'take_lock', 'exclusion_conflicts', 'headroom', 'verify_claim', 'fsync_dir',
           'create_marker', 'begin_input_access')
for name in R1_SAME:
    check(p2[name] == p1[name], f'{name} is byte for byte the one in pod operator r1 (the gate layer is unchanged)')
src2 = open(POD).read()
check("'podman', 'exec'" not in src2 and "'exec'," not in src2, 'r2 never builds a podman exec argv')
check(M.PROD['finalizer_sha'] == sha(open(FIN, 'rb').read()), 'the finalizer pin is the sha256 of finalizer r5 in this tree')
check(M.PROD['seccomp_sha'] == sha(open(SECCOMP, 'rb').read()), 'the seccomp pin is the admitted profile in the baseline')
check(M.PROD['app_image'] is None and M.PROD['pg_image'] is None, 'the production image Ids are not pinned (they are admitted after the build and the pull)')
check(M.PROD['volume_root'] == '/mnt/storage/containers/learn-qualification/learn-pod' and M.PROD['cred_root'] == '/run/user/30034/learn-cred', 'the volume and credential roots are finalizer r5\'s')
F5 = mod('fin5', FIN)
check(F5.VOLUME_ROOT == M.PROD['volume_root'] and F5.DEFAULTS['cred_root'] == M.PROD['cred_root'] and F5.MAX_CHECKPOINTS == M.MAX_CHECKPOINTS and F5.ROLES == M.ROLES,
      'volume root, credential root, checkpoint cap and role names equal finalizer r5\'s constants')
check(M.HOST_UID == {'pg': 428678, 'app': 428679}, 'the host uid gate is design section 3\'s {428678, 428679}')
errs, held = M.validate_plan(M.PLAN)
check(errs == [], f'the production plan keeps every invariant: {errs}')
check(len(held) == 8 and all('#5184' in h for h in held), 'the production plan holds exactly the eight fixture-dependent values, each naming #5184')
check(sorted({h.split(':')[0] for h in held}) == ['feed-admin.prepare', 'feed-admin.resume', 'feed-replay.activate', 'feed-replay.drain', 'host-init.service',
                                                  'host-init.verify', 'host-restart.service', 'host-restart.verify'], 'the held values are the Host fixture steps and the producer association')
check(sum(1 for a in M.PLAN['actions'] if a[0] == 'checkpoint') == 5, 'the production plan uses five of finalizer r5\'s eight checkpoints')


def complete_plan():
    """The production plan with every HELD value replaced by a harmless command: what a complete plan looks like to the driver."""
    p = copy.deepcopy(M.PLAN)
    for g in p['generations'].values():
        g['steps'] = [(nm, ['/bin/true'] if M.is_held(a) else a) for nm, a in g.get('steps', [])]
        if M.is_held(g.get('service')):
            g['service'] = ['/bin/sleep', 'infinity']
    return p


TP = complete_plan()
check(M.validate_plan(TP) == ([], []), 'the completed plan validates with no error and nothing held')


# ============================================================ plan invariants: one causal control each
def mutate(fn):
    p = copy.deepcopy(TP)
    fn(p)
    return M.validate_plan(p)[0]


def acts_replace(p, old, new):
    i = p['actions'].index(old)
    p['actions'][i:i + 1] = new


def has(errs_, needle):
    return any(needle in e for e in errs_)


def _nine(p):
    i = p['actions'].index(['checkpoint', 'admitted'])
    p['actions'][i:i + 1] = [['checkpoint', f'c{k}'] for k in range(5)]


def _early_ck(p):
    acts_replace(p, ['checkpoint', 'admitted'], [])
    p['actions'].insert(p['actions'].index(['start', 'feed-admin']), ['checkpoint', 'early'])


def _no_ck(*labels):
    def f(p):
        for lb in labels:
            acts_replace(p, ['checkpoint', lb], [])
    return f


def _before_end(*acts):
    def f(p):
        for a in acts:
            p['actions'].insert(len(p['actions']) - 2, a)
    return f


def _all_done_early(p):
    i = p['actions'].index(['wait-done', 'feed'])
    p['actions'][i + 1:] = [['stop', 'host'], ['stop', 'recv'], ['sleep', 1]]


def _gen(name, **kv):
    def f(p):
        for k, v in kv.items():
            if v is None:
                p['generations'][name].pop(k)
            else:
                p['generations'][name][k] = v
    return f


def _insert(at, a):
    return lambda p: p['actions'].insert(at, a)


PLAN_CASES = [
    ('nine checkpoints', _nine, 'finalizer r5 serves at most 8'),
    ('a checkpoint while the feed slot is empty', _early_ck, 'all three role containers running'),
    ('a kill of host with no checkpoint since its restart', _no_ck('pre-kill-host'), "['kill', 'host']: no checkpoint holds this container"),
    ('a remove before the clean-restart checkpoint', _no_ck('clean-restart'), 'no checkpoint holds this container'),
    ('a remove of recv before the recv-down checkpoint', _no_ck('recv-down'), 'no checkpoint holds this container'),
    ('a second stop of recv', _before_end(['stop', 'recv']), 'nothing running to stop'),
    ('all three roles done before the end', _all_done_early, 'the finalizer would trigger early'),
    ('a second replay', _gen('feed-admin', input=True), 'replay exactly once'),
    ('no replay', _gen('feed-replay', input=None), 'replay exactly once'),
    ('pg removed', _before_end(['remove', 'pg']), 'pg is never removed'),
    ('pg stopped at the end', _before_end(['stop', 'pg']), 'pg running'),
    ('a restart of a role', _before_end(['restart', 'host']), 'only a stopped pg restarts'),
    ('wait-ready on a generation without a probe', _gen('feed-admin', ready=None), 'no readiness probe'),
    ('wait-done on a generation with a service', _insert(9, ['wait-done', 'host']), 'only a generation without a service'),
    ('an unknown action', _insert(1, ['exec', 'host']), 'not one of'),
    ('a bad checkpoint label', lambda p: p['actions'].__setitem__(p['actions'].index(['checkpoint', 'admitted']), ['checkpoint', 'Bad Label']), 'bad label'),
    ('a sleep of 10 minutes', lambda p: p['actions'].__setitem__(p['actions'].index(['sleep', 30]), ['sleep', 600]), 'sleep 1 to 300'),
    ('an unknown credential file', lambda p: p['generations']['recv-init']['creds'].append('id_rsa'), 'unknown credential'),
    ('/in outside feed', _gen('recv-init', input=True), 'only the feed slot mounts /in'),
    ('a step named service', lambda p: p['generations']['recv-init']['steps'].append(('service', ['/bin/true'])), 'malformed step'),
    ('a step with an empty argv', lambda p: p['generations']['recv-init']['steps'].append(('x', [])), 'not a non-empty list'),
    ('a reconcile without a kill', _insert(9, ['reconcile', 'host']), 'reconcile follows a kill'),
    ('a start into an occupied slot', _insert(3, ['start', 'recv-restart']), 'the slot is occupied'),
    ('another schema', lambda p: p.__setitem__('schema', 'x/1'), 'another schema'),
]
for what, fn, needle in PLAN_CASES:
    e = mutate(fn)
    check(has(e, needle), f'plan: {what} is refused ({needle}): {e[:2]}')
check(mutate(lambda p: None) == [], 'plan: the unmutated completed plan passes (the causal control for every case above)')

# ============================================================ the step runner, for real under /bin/sh
SH = '/bin/sh'


def runner(tmp, role, args, env=None):
    out = os.path.join(tmp, 'out')
    os.makedirs(os.path.join(out, role), exist_ok=True)
    script = M.STEP_RUNNER.replace('/out/', out + '/')
    p = subprocess.run([SH, '-c', script, 'step-runner', role] + args, capture_output=True, text=True, timeout=30, env=dict(os.environ, **(env or {})))
    return p.returncode, os.path.join(out, role, 'steps'), p


with tempfile.TemporaryDirectory() as tmp:
    rc, d, p = runner(tmp, 'host', ['one', '2', '/bin/echo', 'hello', 'two', '3', '/bin/sh', '-c', 'echo err >&2'])
    check(rc == 0 and open(f'{d}/01-one/stdout').read() == 'hello\n' and open(f'{d}/01-one/rc').read() == '0\n' and open(f'{d}/02-two/stderr').read() == 'err\n'
          and os.path.isfile(f'{d}/complete'), 'runner: each step\'s stdout, stderr and exit code land under /out/<role>/steps/<k>-<name>/, then steps/complete')
with tempfile.TemporaryDirectory() as tmp:
    rc, d, p = runner(tmp, 'recv', ['a', '1', '/bin/false', 'b', '2', '/bin/echo', 'never'])
    check(rc == 1 and open(f'{d}/01-a/rc').read() == '1\n' and not os.path.exists(f'{d}/02-b') and not os.path.exists(f'{d}/complete'), 'runner: the first failing step stops the list with its exit code')
with tempfile.TemporaryDirectory() as tmp:
    rc, d, p = runner(tmp, 'feed', ['a', '1', '/bin/true', 'service', '3', '/bin/sh', '-c', 'echo SERVICE $$'])
    check(rc == 0 and os.path.isfile(f'{d}/complete') and p.stdout.startswith('SERVICE ') and os.path.isdir(f'{d}/01-a') and not os.path.exists(f'{d}/02-service'),
          'runner: the service is exec\'d after the steps (its output is the container log, not a step), with steps/complete written first')
with tempfile.TemporaryDirectory() as tmp:
    marker = os.path.join(tmp, 'pwned')
    rc, d, p = runner(tmp, 'host', ['a', '2', '/bin/echo', f'$(touch {marker}) `touch {marker}` ; touch {marker}'])
    check(rc == 0 and not os.path.exists(marker) and open(f'{d}/01-a/stdout').read().startswith('$(touch'), 'runner: an argument is passed literally; eval never sees a value')
for what, args, code in (('a bad step name', ['Bad', '1', '/bin/true'], 64), ('a count past the end', ['a', '5', '/bin/true'], 64), ('a non-numeric count', ['a', 'x', '/bin/true'], 64),
                         ('a service that is not last', ['service', '1', '/bin/true', 'b', '1', '/bin/true'], 64), ('a dangling name', ['a'], 64)):
    with tempfile.TemporaryDirectory() as tmp:
        rc, d, p = runner(tmp, 'host', args)
        check(rc == code, f'runner: {what} exits {code}')
with tempfile.TemporaryDirectory() as tmp:
    out = os.path.join(tmp, 'out')
    os.makedirs(os.path.join(out, 'pg'))
    p = subprocess.run([SH, '-c', M.STEP_RUNNER.replace('/out/', out + '/'), 'step-runner', 'pg', 'a', '1', '/bin/true'], capture_output=True)
    check(p.returncode == 64, 'runner: only host, recv and feed')
with tempfile.TemporaryDirectory() as tmp:
    os.makedirs(os.path.join(tmp, 'out', 'host', 'steps'))
    rc, d, p = runner(tmp, 'host', ['a', '1', '/bin/true'])
    check(rc == 70, 'runner: an existing steps directory refuses (a container generation starts on a fresh tmpfs)')
with tempfile.TemporaryDirectory() as tmp:
    secret = 'S3CR3T-' + 'x' * 40
    sf = os.path.join(tmp, 'collector.secret')
    open(sf, 'w').write(secret)
    rc, d, p = runner(tmp, 'feed', ['credential', '3', 'env-file', 'FSGG_TELEMETRY_CREDENTIAL_COLLECTOR', sf,
                                    'use', '3', '/bin/sh', '-c', 'test "$FSGG_TELEMETRY_CREDENTIAL_COLLECTOR" = "$(cat ' + sf + ')" && echo matched'])
    allout = p.stdout + p.stderr + ''.join(open(os.path.join(r_, f)).read() for r_, _, fs in os.walk(d) for f in fs)
    check(rc == 0 and open(f'{d}/02-use/stdout').read() == 'matched\n' and secret not in allout, 'runner: env-file exports the credential to later steps and never prints it')
    rc, d, p = runner(tmp + '/x', 'feed', ['credential', '3', 'env-file', 'PATH', sf])
    check(rc == 64, 'runner: env-file only sets FSGG_TELEMETRY_CREDENTIAL_* variables')
with tempfile.TemporaryDirectory() as tmp:
    rc, d, p = runner(tmp, 'host', ['wait', '3', 'wait-tcp', '1', '1'])
    check(rc == 1 and open(f'{d}/01-wait/rc').read() == '1\n', 'runner: wait-tcp times out with exit 1 when nothing listens on the port')
    rc, d, p = runner(tmp + '/y', 'host', ['wait', '3', 'wait-tcp', 'x', '1'])
    check(rc == 64, 'runner: wait-tcp needs numeric arguments')

# ============================================================ the probe script and its parser
with tempfile.TemporaryDirectory() as tmp:
    p = subprocess.run([SH, '-c', M.PROBE_SH.replace('exec sleep 120', 'exit 0'), 'probe', 'none', '0', tmp], capture_output=True, text=True, timeout=30)
    pr = M.parse_probe(p.stdout)
    check(pr['end'] and pr['id'] == [str(os.getuid()), str(os.getgid())] and 'CapEff' in pr['status'] and 'NoNewPrivs' in pr['status'] and pr['own'] == {tmp: str(UID)}
          and 'lo' in pr['ifs'] and pr['route4'] is not None and pr['route6'] is not None, 'the probe prints uid/gid, the capability sets, NoNewPrivs, owners, interfaces and routes')

# ============================================================ fixtures shared by the gate, precheck and run cases
SRC = json.load(open(SECCOMP))
KERNEL = '6.12.0'
EXP, _ = M.expected_set(SRC, KERNEL)


def oci_seccomp(exp, drop=0):
    rules = []
    for action, errno_ret, name, args in sorted(exp, key=str)[drop:]:
        r_ = {'action': action, 'names': [name]}
        if errno_ret is not None:
            r_['errnoRet'] = errno_ret
        if args:
            r_['args'] = [json.loads(a) for a in args]
        rules.append(r_)
    return {'defaultAction': SRC['defaultAction'], 'defaultErrnoRet': SRC.get('defaultErrnoRet'), 'syscalls': rules}


class World:
    """An account, view, slice, loop volume, mount table and a scripted podman for the precheck pod."""
    def __init__(self, tmp):
        self.tmp = tmp
        self.base = os.path.join(tmp, 'top')
        self.view = os.path.join(self.base, 'view')
        for d in (self.base, self.view, *(os.path.join(self.view, x) for x in ('retirement-acks', 'claims', 'inputs', 'admissions'))):
            os.makedirs(d, exist_ok=True)
            os.chmod(d, 0o755)
        self.home = os.path.join(tmp, 'home')
        self.state = os.path.join(self.home, '.local', 'state')
        for d in ('learn-custody', 'learn-evidence', 'learn-checkpoint'):
            os.makedirs(os.path.join(self.state, d))
        self.slice = os.path.join(tmp, 'slice')
        os.makedirs(self.slice)
        for fn, v in (('memory.max', 6 << 30), ('pids.max', 1280), ('memory.current', 0), ('pids.current', 30)):
            open(os.path.join(self.slice, fn), 'w').write(f'{v}\n')
        self.meminfo = os.path.join(tmp, 'meminfo')
        open(self.meminfo, 'w').write('MemAvailable: 16000000 kB\n')
        self.fin = os.path.join(self.view, 'learn-finalizer-r5.py')
        open(self.fin, 'w').write('# finalizer r5 fixture\n')
        self.seccomp = os.path.join(self.view, 'seccomp.json')
        shutil.copy(SECCOMP, self.seccomp)
        for f in (self.fin, self.seccomp):
            os.chmod(f, 0o644)
        self.volroot = os.path.join(tmp, 'vol')
        self.cred = os.path.join(tmp, 'cred')
        self.proc = os.path.join(tmp, 'proc')
        self.cg = os.path.join(tmp, 'cg')
        for d in (self.volroot, self.proc, self.cg):
            os.makedirs(d)
        self.app, self.pg = 'a1' * 32, 'b2' * 32
        self.ident = sha(b'source')
        self.opid = 'op-pod-1'
        self.now = datetime.now(timezone.utc)
        self.euid = 424242
        self.units, self.pods, self.orphans, self.images = '', '', '', {self.app, self.pg}
        self.make_volume(self.opid)
        self.mountinfo = os.path.join(tmp, 'mountinfo')
        self.mounts = {}
        self.add_mount(os.path.join(self.volroot, f'{self.opid}.mnt'))
        self.add_mount(os.path.dirname(self.cred), fstype='tmpfs', source='tmpfs', opts='rw,nosuid,nodev')
        self.vfs = dict(f_blocks=262144, f_frsize=4096, f_bfree=262144 - 6000, f_bavail=262144 - 6000, f_files=65536, f_ffree=65536 - 13)
        self.fail = set()
        self.calls = []
        self.probe_over = {}
        self.inspect_over = {}
        self.oci_drop = 0
        self.created = []

    def make_volume(self, opid):
        mp = os.path.join(self.volroot, f'{opid}.mnt')
        for d in (mp, os.path.join(mp, 'pgdata'), os.path.join(mp, 'recv'), os.path.join(mp, 'lost+found')):
            os.makedirs(d, exist_ok=True)
        os.chmod(mp, 0o755)
        for d in ('pgdata', 'recv'):
            os.chmod(os.path.join(mp, d), 0o700)

    def add_mount(self, point, fstype='ext4', source='/dev/loop7', opts='rw,nosuid,nodev,noexec,relatime'):
        self.mounts[point] = (fstype, source, opts)
        self.write_mounts()

    def write_mounts(self):
        lines = ['22 1 0:21 / / rw,relatime shared:1 - ext4 /dev/vda1 rw']
        for i, (pt, (fs, src_, opts)) in enumerate(sorted(self.mounts.items())):
            lines.append(f'{100 + i} 22 7:{i} / {pt.replace(" ", chr(92) + "040")} {opts} shared:{5 + i} - {fs} {src_} rw')
        open(self.mountinfo, 'w').write('\n'.join(lines) + '\n')

    def statvfs(self, path):
        class V:
            pass
        v = V()
        for k, val in self.vfs.items():
            setattr(v, k, val)
        return v

    def env(self, **over):
        e = {'uid': self.euid, 'owner_uid': UID, 'chain_start': self.base, 'home': self.home, 'view': self.view, 'slice': self.slice, 'meminfo': self.meminfo,
             'fin': self.fin, 'finalizer_sha': sha(open(self.fin, 'rb').read()), 'app_image': self.app, 'pg_image': self.pg, 'seccomp': self.seccomp,
             'seccomp_sha': sha(open(self.seccomp, 'rb').read()), 'volume_root': self.volroot, 'cred_root': self.cred, 'proc': self.proc, 'cgroot': self.cg,
             'mountinfo': self.mountinfo, 'unshare': [], 'openssl': OPENSSL, 'host_uid': {'pg': UID, 'app': UID}, 'lock_wait_s': 0, 'plan': TP,
             'self': '/x/learn-pod-operator-r2.py', 'python': '/usr/bin/python3'}
        e.update(over)
        return e

    def probe_out(self, slot):
        kind = 'pg' if slot == 'pg' else 'app'
        o = self.probe_over.get(slot, {})
        uid = o.get('uid', '999' if kind == 'pg' else '1000')
        lines = [f'ID {uid} {uid}'] + [f'STATUS {k} {o.get(k, "0" * 16)}' for k in ('CapInh', 'CapPrm', 'CapEff', 'CapBnd', 'CapAmb')]
        lines.append(f'STATUS NoNewPrivs {o.get("nnp", "1")}')
        paths_ = ['/tmp', '/dev/shm', '/var/run/postgresql'] if kind == 'pg' else [f'/out/{slot}', '/work', '/tmp', '/dev/shm']
        lines += [f'OWN {o.get("own", uid)} {p}' for p in paths_]
        lines += [f'IF {i}' for i in o.get('ifs', ['lo'])] + [f'ROUTE4 {o.get("r4", 0)}', f'ROUTE6 {o.get("r6", 0)}']
        if kind == 'app':
            lines.append(o.get('probe', 'PROBE ok'))
        if not o.get('noend'):
            lines.append('END')
        return '\n'.join(lines) + '\n'

    def inspect_json(self, name, slot):
        kind = 'pg' if slot == 'pg' else 'app'
        mem, pids, _ = M.LIMITS[slot]
        pid = 9000 + M.SLOTS.index(slot)
        os.makedirs(os.path.join(self.proc, str(pid)), exist_ok=True)
        hu = self.probe_over.get(slot, {}).get('hostuid', str(UID))
        open(os.path.join(self.proc, str(pid), 'status'), 'w').write(f'Name: x\nUid:\t{hu}\t{hu}\t{hu}\t{hu}\n')
        ocip = os.path.join(self.tmp, f'oci-{slot}.json')
        json.dump({'linux': {'seccomp': oci_seccomp(EXP, self.oci_drop)}}, open(ocip, 'w'))
        j = {'Image': self.pg if kind == 'pg' else self.app, 'State': {'Status': 'running', 'Pid': pid}, 'OCIConfigPath': ocip,
             'HostConfig': {'CapAdd': [], 'ReadonlyRootfs': True, 'Memory': int(mem[:-1]) << 20, 'PidsLimit': int(pids), 'SecurityOpt': [f'seccomp={self.seccomp}']}}
        for k, v in self.inspect_over.get(slot, {}).items():
            if k in ('CapAdd', 'ReadonlyRootfs', 'Memory', 'PidsLimit', 'SecurityOpt'):
                j['HostConfig'][k] = v
            else:
                j[k] = v
        return json.dumps([j])

    def run(self, argv, timeout=60, cap=1 << 22):
        a = list(argv)
        self.calls.append(a)
        if a[:3] == ['podman', 'image', 'inspect']:
            return (0, a[-1] + '\n', '') if a[-1] in self.images else (1, '', 'no such image')
        if a[:3] == ['systemctl', '--user', 'list-units']:
            return (1, '', 'x') if 'units' in self.fail else (0, self.units, '')
        if a[:3] == ['podman', 'pod', 'ps'] and '--filter' not in a:
            return 0, self.pods, ''
        if a[:2] == ['podman', 'ps'] and a[a.index('--filter') + 1] == 'name=^learn-op-':
            return 0, self.orphans, ''
        if a[:3] == ['podman', 'pod', 'create']:
            if 'pod_create' in self.fail:
                return 125, '', 'Error: Operation not permitted'
            self.pre_pod = True
            return 0, 'podid\n', ''
        if a[:3] == ['podman', 'run', '-d']:
            name = a[a.index('--name') + 1]
            slot = name.rsplit('-', 1)[1]
            self.created.append(a)
            if f'start_{slot}' in self.fail:
                return 126, '', self.start_err
            return 0, 'cid\n', ''
        if a[:2] == ['podman', 'logs']:
            return 0, self.probe_out(a[2].rsplit('-', 1)[1]), ''
        if a[:2] == ['podman', 'inspect'] and len(a) == 3:
            return 0, self.inspect_json(a[2], a[2].rsplit('-', 1)[1]), ''
        if a[:3] == ['podman', 'pod', 'rm']:
            self.pre_pod = False
            return (1, '', 'x') if 'pod_rm' in self.fail else (0, '', '')
        if a[:3] == ['podman', 'pod', 'ps'] and '--filter' in a:
            return 0, ('learn-pre-x\n' if 'leftover' in self.fail else ''), ''
        if a[:2] == ['podman', 'ps'] and '--filter' in a:
            return 0, '', ''
        if a[:2] == ['systemd-run', '--user']:
            unit = a[a.index('--unit') + 1]
            self.started = getattr(self, 'started', []) + [(unit, os.path.isfile(os.path.join(self.state, 'learn-input', self.opid, 'input-read')))]
            return (1, '', 'x') if f'unit_{unit.split("-")[1]}' in self.fail else (0, '', '')
        if a[:3] == ['systemctl', '--user', 'is-active']:
            return 0, 'active\n', ''
        raise AssertionError(f'unexpected command {a}')

    def deps(self, **over):
        d = {'run': self.run, 'geteuid': lambda: self.euid, 'now': lambda: self.now, 'sleep': lambda s: None, 'monotonic': iter(range(0, 10 ** 7)).__next__,
             'nonce': lambda: 'cd' * 16, 'statvfs': self.statvfs, 'kernel': lambda: KERNEL, 'token': M.secrets.token_urlsafe,
             'read': lambda p: open(p).read(), 'environ': {}}
        d.update(over)
        return d

    def admit(self, opid=None, plan=None, ttl=300, operator_sha=None):
        argv = M.plan_argv(plan or TP)
        return L.admit(opid or self.opid, sha(argv.encode()), self.ident, operator_sha or SELF_SHA, sha(open(self.fin, 'rb').read()), [self.app, self.pg], ttl,
                       source_root=self.view, view_root=self.view, services=self.view, owner_uid=UID, now=self.now)

    def claim(self, opid=None, snapshot='snap-1', files=None):
        sm = {'schema': 'learn-input-snapshot/1', 'snapshotId': snapshot, 'sourceIdentity': self.ident,
              'files': files if files is not None else [{'path': f'{M.REPLAY_SUBTREE}/a.ready', 'type': 'file', 'size': 3, 'sha256': H('c'), 'mtime_ns': 1}]}
        smb = L.canon(sm)
        mp = os.path.join(self.view, 'inputs', snapshot + '.manifest.json')
        open(mp, 'wb').write(smb)
        os.chmod(mp, 0o644)
        cl = {'schema': 'learn-input-claim/1', 'generation': 1, 'sourceIdentity': self.ident, 'opId': opid or self.opid, 'snapshotId': snapshot,
              'sealedManifestSha256': sha(smb), 'precheckResultSha256': H('b'), 'claimedUtc': self.now.isoformat(), 'supersedes': None}
        p = os.path.join(self.view, 'claims', self.ident + '.claim')
        if os.path.exists(p):
            os.unlink(p)
        open(p, 'wb').write(L.canon(cl))
        os.chmod(p, 0o644)


def go(w, mode, env=None, deps=None, argv=None):
    d = w.deps(**(deps or {}))
    buf = io.StringIO()
    umask = os.umask(0o022)
    try:
        with contextlib.redirect_stdout(buf):
            code = M.main([mode, '--id', w.opid, '--argv-json', argv if argv is not None else M.plan_argv((env or {}).get('plan', TP)), '--source-identity', w.ident],
                          w.env(**(env or {})), d)
    finally:
        os.umask(umask)
        while M.HELD_LOCKS:
            os.close(M.HELD_LOCKS.pop())
    out = buf.getvalue()
    return code, json.loads(out.split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0]), out


def err(r, needle):
    return any(needle in e for e in r['errors'])


PRE, RUN = '--precheck-only', '--run'

# ============================================================ --precheck-only: exits 0 when everything holds, and the claim tool takes it
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    code, r, out = go(w, PRE)
    check(code == 0 and r['errors'] == [] and r['exitCode'] == 0, f'--precheck-only exits 0 when every gate, the volume and the container precheck hold: {r["errors"]}')
    check(r['gates']['inputMounted'] is False and not any(m['destination'] == '/in' for c in r['containers'] for m in c['mounts']), '... with the input untouched and no container with /in')
    L._precheck_ok(r, w.opid)
    check(True, '... and the claim tooling\'s own _precheck_ok accepts the RESULT')
    check(r['gates']['podPrecheck']['retired'] is True and [c for c in w.calls if c[:3] == ['podman', 'pod', 'rm']] == [['podman', 'pod', 'rm', '-f', '-t', '0', f'learn-pre-{w.opid}']], 'the precheck pod is removed and the removal read back')
    check(all(all(v for v in a['checks'].values()) for a in r['gates']['podPrecheck']['containers'].values()) and len(r['gates']['podPrecheck']['containers']) == 4,
          'all four precheck containers pass every assertion')
    check(r['gates']['podPrecheck']['vsockErrnoExpected'] == M.vsock_errno(SRC, KERNEL), 'the probe is given the AF_VSOCK errno computed from the profile')
    check(r['gates']['volume']['totalBytes'] == 1 << 30 and r['gates']['plan']['held'] == [] and r['gates']['pins']['planSha256'] == M.plan_digest(TP), 'the RESULT records the volume, the plan and its digest')
    check(not os.path.exists(os.path.join(w.state, 'learn-input', w.opid)) and not getattr(w, 'started', None), 'the precheck creates no marker and starts no unit')
    # the precheck containers use the run's own options
    for a in w.created:
        slot = a[a.index('--name') + 1].rsplit('-', 1)[1]
        gname = 'pg' if slot == 'pg' else 'hold'
        run_argv = M.ctr_argv(w.env(), w.opid, gname, slot, 'snap-1')
        strip = lambda v, pre: [x for x in v[:v.index(w.pg if slot == 'pg' else w.app)] if x not in ('--cgroups=split',)]
        pre_opts = [x.replace('learn-pre-', 'learn-pod-') for x in strip(a, True)]
        run_opts = strip(run_argv, False)
        extra = [x for x in run_opts if x not in pre_opts]
        check(all(x in run_opts for x in pre_opts) and all(e_ in ('-v',) or e_.startswith(('/', 'PGDATA', 'POSTGRES', 'SSL_CERT')) or e_ == '--env' for e_ in extra),
              f'precheck {slot}: the same options as the run, less only credentials, volume and input mounts ({extra[:4]})')

# the production configuration and plan refuse a real run of this revision
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    code, r, _ = go(w, PRE, env={'app_image': None, 'pg_image': None})
    check(code == 5 and err(r, 'no image ids are pinned'), 'the production image pins (None) refuse')
    code, r, _ = go(w, PRE, env={'plan': M.PLAN})
    check(code == 5 and err(r, 'the plan has held steps') and 'podPrecheck' not in r['gates'], 'the production plan refuses (held steps) before any container starts')
    code, r, _ = go(w, PRE, argv='["x"]')
    check(code == 5 and err(r, 'argv-json must name this operator\'s plan'), 'argv-json that does not name the plan refuses')
    bad = copy.deepcopy(TP)
    bad['actions'].insert(1, ['exec', 'host'])
    code, r, _ = go(w, PRE, env={'plan': bad})
    check(code == 5 and err(r, 'the plan is invalid'), 'a plan that breaks an invariant refuses')
    w.admit()
    code, r, _ = go(w, PRE)
    check(code == 0, 'causal control: the same world with the pins and the completed plan passes')
    w.opid = 'op-pod-2'
    w.make_volume('op-pod-2')
    w.add_mount(os.path.join(w.volroot, 'op-pod-2.mnt'))
    code, r, _ = go(w, PRE, env={'seccomp_sha': H('9')})
    check(code == 5 and err(r, 'seccomp profile does not hash to its pin'), 'a seccomp profile off its pin refuses')

# ============================================================ the loop volume (carry-forward item 3): every refusal and its control
def vol_case(what, mut, needle, undo=None):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        mut(w)
        code, r, _ = go(w, PRE)
        check(code == 5 and err(r, needle) and 'podPrecheck' not in r['gates'], f'volume: {what} refuses before the container precheck ({needle}): {r["errors"]}')


mp_of = lambda w: os.path.join(w.volroot, f'{w.opid}.mnt')
VOL_CASES = [
    ('not mounted', lambda w: (w.mounts.pop(mp_of(w)), w.write_mounts()), 'is not mounted exactly once (0 mounts)'),
    ('mounted twice', lambda w: open(w.mountinfo, 'a').write(f'300 22 7:9 / {mp_of(w)} rw,nosuid,nodev,noexec - ext4 /dev/loop9 rw\n'), '(2 mounts)'),
    ('not ext4', lambda w: w.add_mount(mp_of(w), fstype='xfs'), 'type xfs'),
    ('not a loop device', lambda w: w.add_mount(mp_of(w), source='/dev/vdb'), 'source /dev/vdb'),
    ('without nosuid', lambda w: w.add_mount(mp_of(w), opts='rw,nodev,noexec'), 'missing nosuid'),
    ('without nodev', lambda w: w.add_mount(mp_of(w), opts='rw,nosuid,noexec'), 'missing nodev'),
    ('without noexec', lambda w: w.add_mount(mp_of(w), opts='rw,nosuid,nodev'), 'missing noexec'),
    ('read-only', lambda w: w.add_mount(mp_of(w), opts='ro,nosuid,nodev,noexec'), 'missing rw'),
    ('2 GiB', lambda w: w.vfs.update(f_blocks=524288, f_bfree=524288 - 6000), 'not a 1 GiB filesystem'),
    ('512 MiB', lambda w: w.vfs.update(f_blocks=131072, f_bfree=131072 - 6000), 'not a 1 GiB filesystem'),
    ('used (100 MiB)', lambda w: w.vfs.update(f_bfree=262144 - 25600), 'not fresh'),
    ('used (an initdb: 1000 inodes)', lambda w: w.vfs.update(f_ffree=65536 - 1000), 'not fresh'),
    ('a group-writable top directory', lambda w: os.chmod(mp_of(w), 0o775), 'top directory'),
    ('an extra entry', lambda w: os.mkdir(os.path.join(mp_of(w), 'extra')), 'exactly pgdata and recv'),
    ('no recv', lambda w: os.rmdir(os.path.join(mp_of(w), 'recv')), 'exactly pgdata and recv'),
    ('pgdata 0755', lambda w: os.chmod(os.path.join(mp_of(w), 'pgdata'), 0o755), 'pgdata is not a'),
    ('recv owned by another uid', lambda w: setattr(w, 'host_over', True), 'recv is not a'),
    ('pgdata a symlink', lambda w: (os.rmdir(os.path.join(mp_of(w), 'pgdata')), os.symlink('recv', os.path.join(mp_of(w), 'pgdata'))), 'pgdata is not a'),
]
for what, mut, needle in VOL_CASES:
    if what == 'recv owned by another uid':
        with tempfile.TemporaryDirectory() as tmp:
            w = World(tmp)
            w.admit()
            code, r, _ = go(w, PRE, env={'host_uid': {'pg': UID, 'app': UID + 1}})
            check(code == 5 and err(r, needle), f'volume: {what} refuses ({needle})')
        continue
    vol_case(what, mut, needle)
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    real_lstat = os.lstat

    def lstat_other_dev(p, *a, **k):
        st = real_lstat(p, *a, **k)
        if p.endswith('/recv'):
            v = list(st)
            v[2] = st.st_dev + 1
            return os.stat_result(v)
        return st
    os.lstat = lstat_other_dev
    try:
        code, r, _ = go(w, PRE)
    finally:
        os.lstat = real_lstat
    check(code == 5 and err(r, 'recv is not a') , 'volume: a recv directory on another filesystem (a mount over it) refuses')
    code, r, _ = go(w, PRE)
    check(code == 0, 'volume: causal control, the same world passes')

# ============================================================ the container precheck: each assertion and its refusal
PRE_CASES = [
    ('host started as uid 0', 'host', {'uid': '0'}, 'host.uidGid'), ('a capability set', 'recv', {'CapEff': '00000000a80425fb'}, 'recv.capsZero'),
    ('NoNewPrivs 0', 'feed', {'nnp': '0'}, 'feed.noNewPrivs'), ('a tmpfs owned by root', 'host', {'own': '0'}, 'host.tmpfsOwners'),
    ('an eth0 interface', 'pg', {'ifs': ['lo', 'eth0']}, 'pg.loopbackOnly'), ('an IPv4 route', 'recv', {'r4': 1}, 'recv.loopbackOnly'),
    ('an IPv6 route off lo', 'host', {'r6': 2}, 'host.loopbackOnly'), ('a failed socket probe', 'feed', {'probe': 'PROBE fail unix=1/0'}, 'feed.socketProbe'),
    ('a payload at host uid 0', 'host', {'hostuid': '0'}, 'host.hostUid'), ('a payload at the account uid (30034 in production)', 'pg', {'hostuid': 'account'}, 'pg.hostUid'),
]
for what, slot, over, key in PRE_CASES:
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        w.probe_over[slot] = over
        env = {}
        if over.get('hostuid') == 'account':           # the payload runs at the account's own uid: equal to the map here, refused because it is the account
            over['hostuid'], env, w.euid = str(UID), {'uid': UID}, UID
        code, r, _ = go(w, PRE, env=env)
        check(code == 5 and err(r, key) and r['gates']['podPrecheck']['retired'] is True, f'precheck: {what} refuses ({key}) and the precheck pod is still removed')
INSPECT_CASES = [('CapAdd non-empty', 'host', {'CapAdd': ['NET_RAW']}, 'host.capAddEmpty'), ('a writable root', 'recv', {'ReadonlyRootfs': False}, 'recv.readOnlyRoot'),
                 ('another memory limit', 'feed', {'Memory': 1 << 30}, 'feed.memory'), ('another pids limit', 'pg', {'PidsLimit': 4096}, 'pg.pids'),
                 ('no seccomp option', 'host', {'SecurityOpt': []}, 'host.seccompPath'), ('another image', 'pg', {'Image': 'cc' * 32}, 'pg.image'),
                 ('a stopped container', 'feed', {'State': {'Status': 'exited', 'Pid': 0}}, 'feed.running')]
for what, slot, over, key in INSPECT_CASES:
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        w.inspect_over[slot] = over
        code, r, _ = go(w, PRE)
        check(code == 5 and err(r, key), f'precheck: {what} refuses ({key})')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.oci_drop = 1
    code, r, _ = go(w, PRE)
    check(code == 5 and err(r, '.seccompEqual') and r['gates']['podPrecheck']['containers']['host']['seccomp']['missing'], 'precheck: an OCI rule set missing one rule refuses (seccompEqual) and names it')
for what, errtext, needle in (('EPERM', 'Error: crun: Operation not permitted', 'EPERM'), ('ENOSYS', 'Error: Function not implemented', 'ENOSYS'), ('another error', 'boom', 'start failed')):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        w.fail.add('start_recv')
        w.start_err = errtext
        code, r, _ = go(w, PRE)
        check(code == 5 and err(r, f'precheck container recv did not start: {needle}') and r['gates']['podPrecheck']['retired'], f'precheck: a start failing with {what} refuses and names it')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.probe_over['feed'] = {'noend': True}
    code, r, _ = go(w, PRE)
    check(code == 5 and err(r, 'printed no complete probe'), 'precheck: a probe that never ends is refused after its bound')
for what, f, needle in (('the pod create fails', 'pod_create', 'precheck pod create failed: EPERM'), ('the precheck pod is left behind', 'leftover', 'was not retired'),
                        ('the pod removal fails', 'pod_rm', 'was not retired')):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        w.fail.add(f)
        code, r, _ = go(w, PRE)
        check(code == 5 and err(r, needle), f'precheck: {what} refuses ({needle})')

# ============================================================ exclusion now also sees precheck pods; custody covers every directory the start creates
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.pods = 'learn-pre-op-old\n'
    code, r, _ = go(w, PRE)
    check(code == 5 and err(r, 'a pod exists: learn-pre-op-old'), 'a leftover precheck pod refuses a new op')
    w.pods = ''
    for d in ('learn-checkpoint', 'learn-drive', 'learn-input'):
        p = os.path.join(w.state, d, w.opid)
        os.makedirs(p)
        code, r, _ = go(w, PRE)
        check(code == 5 and err(r, 'op id already used') and err(r, f'{d}/{w.opid}'), f'an existing {d}/<id> refuses the op id')
        os.rmdir(p)
    os.makedirs(os.path.join(w.cred, w.opid))
    code, r, _ = go(w, PRE)
    check(code == 5 and err(r, 'op id already used'), 'an existing credential directory refuses the op id')
    os.rmdir(os.path.join(w.cred, w.opid))
    code, r, _ = go(w, PRE)
    check(code == 0, 'causal control: the same world with none of them passes')

# ============================================================ --run: order, units, the containers list, and the claim tooling's view
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.claim()
    code, r, out = go(w, RUN)
    check(code == 0 and r['errors'] == [], f'--run with every gate, the claim and both units starting exits 0: {r["errors"]}')
    check(w.started == [(f'learn-fin-{w.opid}', True), (f'learn-pod-{w.opid}', True)], 'the input-read marker exists before the finalizer unit starts, and the finalizer starts before the pod unit')
    check(r['gates']['inputMounted'] is True and r['runNonceSha256'] == sha(('cd' * 16).encode()), 'the RESULT shows inputMounted and the run nonce')
    planned = [c for c in r['containers'] if c['mode'] == 'planned']
    with_in = [c['generation'] for c in planned if any(m['destination'] == '/in' for m in c['mounts'])]
    check(with_in == ['feed-replay'] and all(m['readOnly'] for c in planned for m in c['mounts'] if m['destination'] == '/in'), 'the RESULT lists the planned containers; only feed-replay mounts /in, read-only')
    fin_c = next(c for c in w.calls if c[:2] == ['systemd-run', '--user'] and 'learn-fin-' in c[3])
    pod_c = next(c for c in w.calls if c[:2] == ['systemd-run', '--user'] and 'learn-pod-' in c[3])
    check(fin_c == M.fin_unit_argv(w.env(), w.opid, w.ident, 'cd' * 16) and pod_c == M.pod_unit_argv(w.env(), w.opid, w.ident, 'cd' * 16), 'the units started are exactly the builders\' argv')
    ev = L._evidence_contract(w.opid, r, {'schema': 'learn-op-pod-success/1', 'opId': w.opid, 'inputRead': True, 'containersStartedWithInput': [f'learn-pod-{w.opid}-feed'],
                                          'runNonceSha256': r['runNonceSha256'], 'finalizerSha256': sha(open(w.fin, 'rb').read())},
                              os.path.join(w.state, 'learn-input'), {'operatorSha256': SELF_SHA, 'finalizerSha256': sha(open(w.fin, 'rb').read())})
    check(any('marker exists' in e for e in ev) and any('/in' in e for e in ev), 'the claim tooling reads the run as consumed (marker, a container with /in)')
    code2, r2, _ = go(w, RUN)
    check(code2 == 5 and err(r2, 'op id already used') and len(w.started) == 2, 'a second --run of the same op id refuses before anything starts')
for unit, needle, pods in (('fin', 'finalizer unit did not start', 0), ('pod', 'pod unit did not start', 1)):
    with tempfile.TemporaryDirectory() as tmp:
        w = World(tmp)
        w.admit()
        w.claim()
        w.fail.add(f'unit_{unit}')
        code, r, _ = go(w, RUN)
        check(code == 5 and err(r, needle) and r['gates']['inputMounted'] is True and len([s for s in w.started if s[0].startswith('learn-pod-')]) == pods,
              f'a {unit} unit that fails to start refuses; the marker stands (the claim is consumed){" and the pod unit is never started" if unit == "fin" else ""}')
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.claim(opid='op-other')
    code, r, _ = go(w, RUN)
    check(code == 5 and err(r, 'names another op') and not os.path.exists(os.path.join(w.state, 'learn-input', w.opid)) and not getattr(w, 'started', None),
          'a claim naming another op refuses with no marker and no unit')
    vb = L._evidence_contract(w.opid, r, {'schema': 'learn-op-pod-failed-to-start/1', 'opId': w.opid, 'inputRead': False, 'containersStartedWithInput': [],
                                          'runNonceSha256': None, 'finalizerSha256': None}, os.path.join(w.state, 'learn-input'), None)
    check(not any('container with /in' in e for e in vb) and not any('marker exists' in e for e in vb), 'such a RESULT shows no container with /in and no marker to the claim tooling')

# ============================================================ units: the design section 3 and 8 contract
U = M.fin_unit_argv({'pg_image': 'b2' * 32, 'python': '/usr/bin/python3', 'fin': '/f'}, 'op-x', H('a'), 'cd' * 16)
check(U[:4] == ['systemd-run', '--user', '--unit', 'learn-fin-op-x'] and 'RuntimeMaxSec=1980' in U and 'MemoryMax=512M' in U and 'TasksMax=64' in U
      and f'Environment=FSGG_NONCE={"cd" * 16}' in U and f'Environment=FSGG_PG_IMAGE={"b2" * 32}' in U and U[-5:] == ['/f', '--id', 'op-x', '--source-identity', H('a')],
      'finalizer unit: 1980 s, 512M, 64 tasks, the nonce and the pg image Id, --id and --source-identity')
check(not any('pod rm' in x for x in U), 'the finalizer unit removes no pod (a finalizer that loses the custody race must not touch the winner\'s pod, #5029 B1)')
PU = M.pod_unit_argv({'python': '/usr/bin/python3', 'self': '/s', 'cred_root': '/run/user/30034/learn-cred'}, 'op-x', H('a'), 'cd' * 16)
for prop in ('BindsTo=learn-fin-op-x.service', 'After=learn-fin-op-x.service', 'RuntimeMaxSec=1920', 'MemoryMax=4G', 'MemorySwapMax=0', 'TasksMax=900', 'CPUQuota=150%',
             'Delegate=yes', 'KillMode=control-group', f'ExecStopPost={M.dump_cmd("cd" * 16)}', 'ExecStopPost=-/usr/bin/podman unshare rm -rf --one-file-system /run/user/30034/learn-cred/op-x'):
    check(prop in PU, f'pod unit: {prop[:60]}')
check(PU[PU.index('--') + 1:] == ['/usr/bin/python3', '-I', '/s', '--drive', '--id', 'op-x', '--source-identity', H('a')], 'pod unit: its main process is this file in --drive mode')
check('MEMEV' in M.dump_cmd('cd' * 16) and 'FSGG ' + 'cd' * 16 in M.dump_cmd('cd' * 16), 'the pod unit prints the MEMEV line finalizer r5 reads, with the nonce inline')

# ============================================================ the recovery driver (--drive) in a fake pod world
class DriveWorld(World):
    """The pod side of the world: containers with a fake /proc/<pid>/root/out/<role>, the pod's shared network table, cgroups, and a fake finalizer that answers
    checkpoint requests in finalizer r5's ack format (n, requestSha256, ok, roles, chainHead) after extracting each role's /out into custody, and that triggers,
    archives and removes the pod once every role is done."""
    def __init__(self, tmp):
        super().__init__(tmp)
        self.unit_cg = f'/user.slice/user-30034.slice/user@30034.service/app.slice/learn-pod-{self.opid}.service'
        self.selfcg = os.path.join(tmp, 'selfcg')
        open(self.selfcg, 'w').write(f'0::{self.unit_cg}\n')
        self.ctrs, self.pod, self.next_pid, self.clock, self.served = {}, False, 20000, 0.0, 0
        self.fin_active, self.ackmode, self.extra_ports, self.cg_outside, self.survivor = True, {}, set(), set(), False
        self.ready_fail, self.term_ignored, self.hostuid_over, self.custody_break, self.no_complete = set(), set(), {}, None, set()
        self.ack_delay, self.finalized, self.order = 0, False, []
        self.euid = UID                                      # the credential root must belong to the running account
        self.owned, self.locked = {}, []
        self.trigger, self.archived, self.archive_s, self.fin_dead, self.partial_ack, self.no_cgroup_line = None, {}, 0, False, False, set()
        self.term_late = set()
        self.cust = os.path.join(self.state, 'learn-custody', self.opid)
        self.ckdir = os.path.join(self.state, 'learn-checkpoint', self.opid)
        os.makedirs(self.cust)
        os.makedirs(self.ckdir)
        os.makedirs(os.path.join(self.state, 'learn-input', self.opid))
        open(os.path.join(self.state, 'learn-input', self.opid, 'input-read'), 'w').write('{}\n')
        self.admit(ttl=600)
        self.claim()

    def unlock(self):
        """read a chowned credential directory as the container (user namespace) would: the test's view inside"""
        for d in self.locked:
            if os.path.isdir(d):
                os.chmod(d, 0o700)
        return self

    def pdir(self, pid):
        return os.path.join(self.proc, str(pid))

    def write_net(self):
        ports = set(self.extra_ports)
        for c in self.ctrs.values():
            if c['state'] == 'running':
                ports |= c['ports']
        body = '  sl  local_address rem_address   st\n' + ''.join(f'   0: 0100007F:{p:04X} 00000000:0000 0A\n' for p in sorted(ports))
        for c in self.ctrs.values():
            if c['state'] == 'running':
                os.makedirs(os.path.join(self.pdir(c['pid']), 'net'), exist_ok=True)
                open(os.path.join(self.pdir(c['pid']), 'net', 'tcp'), 'w').write(body)
                open(os.path.join(self.pdir(c['pid']), 'net', 'tcp6'), 'w').write('  sl  local_address\n')

    def spawn(self, c):
        self.next_pid += 1
        pid = c['pid'] = self.next_pid
        d = self.pdir(pid)
        os.makedirs(d)
        hu = self.hostuid_over.get(c['slot'], str(UID))
        open(os.path.join(d, 'status'), 'w').write(f'Name: x\nUid:\t{hu}\t{hu}\t{hu}\t{hu}\n')
        cg = ('/user.slice/elsewhere' if c['slot'] in self.cg_outside else self.unit_cg) + f"/libpod-payload-{c['id'][:12]}-{pid}"
        open(os.path.join(d, 'cgroup'), 'w').write(f'0::{cg}\n' if c['slot'] not in self.no_cgroup_line else '1:name=systemd:/\n')
        os.makedirs(self.cg + cg, exist_ok=True)
        open(self.cg + cg + '/cgroup.procs', 'w').write(f'{pid}\n')
        c['cg'], c['state'] = cg, 'running'

    def status(self, c, **j):
        out = os.path.join(self.pdir(c['pid']), 'root', 'out', c['slot'])
        body = json.dumps(dict({'schema': 'learn-pod-status/1', 'role': c['slot']}, **j)).encode().ljust(4096, b' ')
        open(os.path.join(out, '.status'), 'wb').write(body)

    def create(self, a):
        name = a[a.index('--name') + 1]
        slot = name[len(f'learn-pod-{self.opid}-'):]
        image = self.pg if slot == 'pg' else self.app
        tail = a[a.index(image) + 1:]
        c = {'name': name, 'slot': slot, 'id': sha(name.encode() + str(self.next_pid).encode()), 'argv': a, 'ports': set(), 'exit': 0}
        self.spawn(c)
        if slot == 'pg':
            c['ports'] = {M.PG_PORT}
        else:
            steps = tail[tail.index('step-runner') + 2:]
            out = os.path.join(self.pdir(c['pid']), 'root', 'out', slot)
            os.makedirs(os.path.join(out, 'steps'))
            k, i, svc = 0, 0, None
            while i < len(steps):
                nm, cnt = steps[i], int(steps[i + 1])
                if nm == 'service':
                    svc = steps[i + 2:i + 2 + cnt]
                else:
                    k += 1
                    sd = os.path.join(out, 'steps', f'{k:02d}-{nm}')
                    os.makedirs(sd)
                    open(os.path.join(sd, 'rc'), 'w').write('0\n')
                    open(os.path.join(sd, 'stdout'), 'w').write(json.dumps({'step': nm}) + '\n')
                i += 2 + cnt
            if slot not in self.no_complete:
                open(os.path.join(out, 'steps', 'complete'), 'w').close()
            c['gen_service'] = svc
            if slot in self.ready_fail:
                self.status(c, phase='terminal', exit=1, reason='error', detail='rc=1')
                open(os.path.join(out, '.done'), 'w').close()
            elif svc is None:
                bad = 'replay_fail' in self.fail
                self.status(c, phase='terminal', exit=3 if bad else 0, reason='error' if bad else 'ok', detail='rc=3' if bad else 'rc=0')
                open(os.path.join(out, '.done'), 'w').close()
            else:
                self.status(c, phase='ready')
                c['ports'] = {'host': {M.HOST_PORT, M.RUNNER_PORT}, 'recv': {M.RECV_PORT}}.get(slot, set()) if '--ready-tcp' in tail else set()
        self.ctrs[name] = c
        self.write_net()
        self.order.append(('start', name))

    def kill(self, name, sig):
        c = self.ctrs.get(name)
        if c is None:
            return 1
        self.order.append(('kill', name, sig))
        if sig == 'KILL' or c['slot'] == 'pg':
            c['state'], c['exit'] = 'exited', (137 if sig == 'KILL' else 0)
            shutil.rmtree(os.path.join(self.pdir(c['pid']), 'root'), ignore_errors=True)        # the tmpfs dies with the container
            open(self.cg + c['cg'] + '/cgroup.procs', 'w').write('4321\n' if self.survivor and sig == 'KILL' else '')
            c['ports'] = set()
        elif c['slot'] in M.ROLES and c['state'] == 'running' and os.path.exists(os.path.join(self.pdir(c['pid']), 'root', 'out', c['slot'], '.done')):
            # the role contract: a role that is done blocks; a SECOND TERM makes it exit, and its tmpfs /out is lost with it
            self.order.append(('second-term', name))
            c['state'] = 'exited'
            shutil.rmtree(os.path.join(self.pdir(c['pid']), 'root'), ignore_errors=True)
            c['ports'] = set()
        elif name in self.term_late:                       # this TERM is slow: the role ends only after the driver's 60 s bound
            self.term_late.discard(name)
        elif name not in self.term_ignored:
            out = os.path.join(self.pdir(c['pid']), 'root', 'out', c['slot'])
            self.status(c, phase='terminal', exit=143, reason='signalled', detail='rc=143')
            open(os.path.join(out, '.done'), 'w').close()
            c['ports'] = set()
        self.write_net()
        return 0

    def sleep(self, s):
        self.clock += s
        self.fin_tick()

    def fin_tick(self):
        """The fake finalizer: consecutive req-<n>, archive every role as it is now, ack; trigger when every role is done."""
        while True:
            k = self.served + 1
            rq = os.path.join(self.ckdir, f'req-{k}')
            if not os.path.exists(rq):
                break
            self.req_seen = getattr(self, 'req_seen', {})
            self.req_seen.setdefault(k, self.clock)
            if self.clock < self.req_seen[k] + self.ack_delay:          # a slow finalizer
                break
            if self.ackmode.get('skip') == k:                  # a finalizer that never answers
                break
            body = open(rq, 'rb').read()
            roles = {}
            for r_ in M.ROLES:
                c = next((c for c in self.ctrs.values() if c['slot'] == r_ and c['state'] == 'running'), None)
                dest = os.path.join(self.cust, 'checkpoints', str(k), f'extract-{r_}')
                if c is None:
                    roles[r_] = 'rejected:identity'
                    continue
                if not os.path.exists(dest):                        # a request served across two ticks (a half-written ack) is archived once
                    shutil.copytree(os.path.join(self.pdir(c['pid']), 'root', 'out', r_), dest)
                roles[r_] = self.ackmode.get(('role', r_), 'accepted')
            if self.custody_break:
                self.custody_break(k)
            ack = {'n': k, 'requestSha256': self.ackmode.get('sha', sha(body)), 'ok': self.ackmode.get('ok', True), 'roles': roles, 'chainHead': H('e')}
            ap = os.path.join(self.ckdir, f'ack-{k}')
            data = json.dumps(ack, sort_keys=True).encode()
            if self.partial_ack and not os.path.exists(ap):     # the driver may see the file before the finalizer finished writing it
                fd = os.open(ap, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
                os.write(fd, data[:10])
                os.close(fd)
                break
            fd = os.open(ap, os.O_WRONLY | os.O_CREAT | (os.O_TRUNC if self.partial_ack else os.O_EXCL), 0o600)
            os.write(fd, data)
            os.close(fd)
            self.order.append(('ack', k))
            self.served = k
        roles_done = [c for c in self.ctrs.values() if c['slot'] in M.ROLES and c['state'] == 'running'
                      and os.path.exists(os.path.join(self.pdir(c['pid']), 'root', 'out', c['slot'], '.done'))]
        if self.pod and (len({c['slot'] for c in roles_done}) == 3 or self.clock >= 1800) and not self.finalized:      # trigger `done`, or the soft deadline `time`
            self.finalized = True
            self.trigger = 'done' if len({c['slot'] for c in roles_done}) == 3 else 'time'
            self.archived = {r_: any(c['slot'] == r_ and c['state'] == 'running' and os.path.exists(os.path.join(self.pdir(c['pid']), 'root', 'out', r_, '.status'))
                                     for c in self.ctrs.values()) for r_ in M.ROLES}
            self.order.append(('finalizer-trigger',))
            if self.archive_s:                               # the finalizer pauses and tars each role before it removes the pod
                self.removal_at = self.clock + self.archive_s
            else:
                self.pod = False
                self.ctrs.clear()
        if self.finalized and self.pod and getattr(self, 'removal_at', None) is not None and self.clock >= self.removal_at and not self.fin_dead:
            self.pod = False
            self.ctrs.clear()
            self.order.append(('finalizer-removed-pod',))

    def run(self, argv, timeout=60, cap=1 << 22):
        a = list(argv)
        self.calls.append(a)
        if a[0] in (OPENSSL, '/bin/false'):
            p = subprocess.run(a, capture_output=True, timeout=60)
            return p.returncode, '', p.stderr.decode()[-300:]
        if a[0] == 'chown':
            # model `podman unshare chown -R U:U <dir>`: inside the user namespace the tree now belongs to U; seen from the account, it belongs to a
            # subordinate uid and its 0700 directory can no longer be entered, so a host-side lstat inside it raises PermissionError
            if 'chown' in self.fail:
                return 1, '', 'x'
            u, g = a[2].split(':')
            d = a[3]
            for root, _, fs in os.walk(d):
                for pth in [root] + [os.path.join(root, f) for f in fs]:
                    st_ = os.lstat(pth)
                    self.owned[pth] = [u if 'chown_uid' not in self.fail else '0', g if 'chown_gid' not in self.fail else '0',
                                       format(stat.S_IMODE(st_.st_mode) | (0o044 if 'chown_mode' in self.fail and pth != d else 0), 'o'),
                                       'directory' if stat.S_ISDIR(st_.st_mode) else 'regular file']
            os.chmod(d, 0o000)
            self.locked.append(d)
            return 0, '', ''
        if a[0] == 'stat' and a[1:4] == ['-c', '%u %g %a %F', '--']:
            if any(pth not in self.owned for pth in a[4:]):
                return 1, '', 'cannot stat'
            return 0, ''.join(' '.join(self.owned[pth]) + '\n' for pth in a[4:]), ''
        if a[:3] == ['rm', '-rf', '--one-file-system']:
            self.unlock()
            shutil.rmtree(a[3], ignore_errors=True)
            return 0, '', ''
        if a[0] in ('cat', 'test'):
            if a[0] == 'cat':
                try:
                    return 0, open(a[1]).read(), ''
                except OSError:
                    return 1, '', 'no'
            return subprocess.run(a).returncode, '', ''
        if a[:3] == ['podman', 'pod', 'create']:
            if 'pod_create' in self.fail:
                return 125, '', 'Operation not permitted'
            self.pod = True
            infra = {'name': f'learn-pod-{self.opid}-infra', 'slot': 'infra', 'id': H('f'), 'ports': set(), 'exit': 0}
            self.spawn(infra)
            if 'infra' in self.cg_outside:
                open(os.path.join(self.pdir(infra['pid']), 'cgroup'), 'w').write('0::/user.slice/elsewhere/infra\n')
            self.ctrs[infra['name']] = infra
            return 0, '', ''
        if a[:3] == ['podman', 'run', '-d']:
            name = a[a.index('--name') + 1]
            if any(f'start_{x}' in self.fail and name.endswith('-' + x) for x in ('host', 'recv', 'feed', 'pg')) and M.HOLD[-1] not in a:   # the abort's hold still starts
                return 126, '', 'Error: Operation not permitted'
            if name in self.ctrs:
                return 125, '', 'name in use'
            self.create(a)
            return 0, 'cid\n', ''
        if a[:2] == ['podman', 'inspect']:
            c = self.ctrs.get(a[-1])
            if c is None:
                return 125, '', 'no such container'
            return 0, {'{{.Id}}': c['id'], '{{.State.Pid}}': str(c['pid'] if c['state'] == 'running' else 0), '{{.State.Status}}': c['state'],
                       '{{.State.ExitCode}}': str(c['exit'])}[a[3]] + '\n', ''
        if a[:3] == ['podman', 'kill', '--signal']:
            return self.kill(a[4], a[3]), '', ''
        if a[:2] == ['podman', 'rm']:
            self.order.append(('rm', a[-1]))
            if 'rm_stuck' in self.fail:
                return 1, '', 'cannot remove'               # the container stays
            return (0 if self.ctrs.pop(a[-1], None) else 1), '', ''
        if a[:3] == ['podman', 'container', 'exists']:
            if 'exists_err' in self.fail:
                return 125, '', 'error'                     # neither "exists" (0) nor "does not exist" (1)
            return (0 if (a[3] in self.ctrs or 'rm_stuck' in self.fail) else 1), '', ''
        if a[:2] == ['podman', 'start']:
            c = self.ctrs[a[2]]
            self.spawn(c)
            c['ports'] = {M.PG_PORT}
            self.write_net()
            self.order.append(('restart', a[2]))
            return 0, '', ''
        if a[:3] == ['podman', 'pod', 'exists']:
            if self.finalized and 'exists_raise_once' in self.fail:
                self.fail.discard('exists_raise_once')
                raise RuntimeError('transient')
            if self.finalized and 'exists_unknown' in self.fail:
                return None, '', 'timeout'
            return (0 if self.pod else 1), '', ''
        if a[:3] == ['systemctl', '--user', 'is-active']:
            if self.finalized and 'active_blip' in self.fail:   # a timed-out `systemctl is-active` during the archive: unknown, not ended
                self.fail.discard('active_blip')
                return None, '', 'timeout'
            if self.fin_dead:
                return 3, 'failed\n', ''
            return 0, ('active\n' if self.fin_active else 'inactive\n'), ''
        raise AssertionError(f'unexpected command {a}')

    def drive_deps(self, **over):
        d = {'run': self.run, 'geteuid': lambda: self.euid, 'now': lambda: self.now, 'sleep': self.sleep, 'monotonic': lambda: self.clock,
             'token': M.secrets.token_urlsafe, 'read': lambda p: open(p).read(), 'environ': {'FSGG_NONCE': 'cd' * 16}, 'statvfs': self.statvfs, 'kernel': lambda: KERNEL}
        d.update(over)
        return d


def drive(w, env=None, deps=None, argv=None):
    buf = io.StringIO()
    umask = os.umask(0o022)
    try:
        with contextlib.redirect_stdout(buf):
            code = M.main(argv or ['--drive', '--id', w.opid, '--source-identity', w.ident], w.env(self_cgroup=w.selfcg, **(env or {})), w.drive_deps(**(deps or {})))
    finally:
        os.umask(umask)
    out = buf.getvalue()
    res = json.loads(out.split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0]) if 'RESULT-BEGIN' in out else json.loads(out.strip().splitlines()[-1])
    logp = os.path.join(w.state, 'learn-drive', w.opid, 'drive.jsonl')
    log = [json.loads(l) for l in open(logp)] if os.path.exists(logp) else []
    return code, res, out, log


def events(log, ev):
    return [e for e in log if e['event'] == ev]


# ---- the whole plan
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    seen = []
    code, r, out, log = drive(w, deps={'token': lambda k: (seen.append(M.secrets.token_urlsafe(k)), seen[-1])[1]})
    check(code == 0 and r['verdict'] == 'complete' and r['errors'] == [], f'the driver runs the complete plan and exits 0: {r["errors"]}')
    check(r['checkpoints'] == 5 and [e['label'] for e in events(log, 'checkpoint-ack')] == ['admitted', 'clean-restart', 'pre-kill-host', 'recv-down', 'pre-replay'],
          'five checkpoints, each acknowledged, in the plan\'s order')
    check(w.finalized and ('finalizer-trigger',) == w.order[-1], 'the last action leaves every role done: the finalizer triggers')
    check(r['credentialsRemoved'] is True and not os.path.exists(os.path.join(w.cred, w.opid)), 'the credential tmpfs directory is removed at the end')
    prev, ok_chain = '0' * 64, True
    for line in open(os.path.join(w.state, 'learn-drive', w.opid, 'drive.jsonl')).read().splitlines():
        e = json.loads(line)
        ok_chain &= e['prev'] == prev
        prev = sha(line.encode())
    check(ok_chain and r['chain']['head'] == prev and r['chain']['lines'] == len(log), 'drive.jsonl is hash-chained and the RESULT names its head')
    check(out.count(f'FSGG {"cd" * 16} DRIVE ') == len(log), 'every logged action is also a FSGG <nonce> DRIVE journal line')
    # readback custody: every remove and kill of a role follows an ack whose readback named that role
    seq = w.order
    for i, ev in enumerate(seq):
        if ev[0] == 'rm' or (ev[0] == 'kill' and ev[2] == 'KILL' and not ev[1].endswith('-pg')):
            last_ack = max((j for j in range(i) if seq[j][0] == 'ack'), default=-1)
            last_start = max((j for j in range(i) if seq[j] == ('start', ev[1])), default=-1)
            check(last_ack > last_start, f'pre-restart evidence: {ev[0]} of {ev[1]} comes after a checkpoint acknowledged since that container started')
    ck = events(log, 'checkpoint-ack')
    check(all(set(c['custody']) == set(M.ROLES) and all(v['files'] > 0 and M.HEX64.match(v['manifestSha256']) for v in c['custody'].values()) for c in ck),
          'each ack is read back from custody: a status and a file manifest digest per role')
    check(all(os.path.isfile(os.path.join(w.cust, 'checkpoints', '1', f'extract-{r_}', 'steps', 'complete')) for r_ in M.ROLES), 'custody holds each role\'s step evidence at the first checkpoint')
    kills = [o for o in seq if o[0] == 'kill']
    check([k[2] for k in kills if k[1].endswith('-pg')] == ['INT', 'KILL'], 'pg is stopped with SIGINT (fast shutdown) in the clean restart and SIGKILLed in its own case')
    check(('kill', f'learn-pod-{w.opid}-host', 'KILL') in kills, 'host is SIGKILLed in the restart-reconciliation case')
    rec = events(log, 'reconcile')
    check([e['slot'] for e in rec] == ['host', 'pg'] and all(e['survivors'] == 0 for e in rec), 'reconcile reads the killed container\'s own cgroup: no surviving process (not orphan detection)')
    starts = [e['generation'] for e in events(log, 'start')]
    check(starts == ['pg', 'recv-init', 'host-init', 'feed-admin', 'pg', 'recv-restart', 'host-restart', 'host-restart', 'pg', 'recv-restart', 'feed-replay'],
          f'the generations start in the plan\'s order (pg restarts are re-adopted): {starts}')
    feed_replay = next(c for c in w.calls if c[:3] == ['podman', 'run', '-d'] and 'feed-replay' not in c and any(x.endswith(':/in:ro,nosuid,nodev,noexec') for x in c))
    check(feed_replay[feed_replay.index('step-runner') + 1] == 'feed' and sum(1 for c in w.calls if c[:3] == ['podman', 'run', '-d'] and any(x.endswith(':/in:ro,nosuid,nodev,noexec') for x in c)) == 1,
          'exactly one container, the feed replay, mounts the sealed snapshot at /in, read-only')
    allout = json.dumps(w.calls) + out + open(os.path.join(w.state, 'learn-drive', w.opid, 'drive.jsonl')).read()
    check(len(seen) == 6 and not any(x in allout for x in seen) and 'Password=' not in allout, 'no generated secret appears in any argv, the RESULT, the journal lines or the drive log')
    check(set(r['credentials']['holders']['recv-init']) == {'server.pfx', 'server.password', 'collector.secret', 'host-config.json', 'browser-key-hash.json', 'ca.pem'}
          and r['credentials']['holders']['pg'] == ['pg-password'] and 'collector.secret' not in r['credentials']['holders']['host-init'],
          'each generation holds only the credential files it names (host never holds the collector grant)')

# ---- the credential generator, with the real openssl
CRED_GENS = {gn: sorted(g['creds']) for gn, g in TP['generations'].items() if g.get('creds')}
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    seen = []
    tok = lambda k: (seen.append(M.secrets.token_urlsafe(k)), seen[-1])[1]
    env, deps = w.env(), w.drive_deps(token=tok)
    rec = M.gen_credentials(env, deps, w.opid, [{'path': f'{M.REPLAY_SUBTREE}/a.ready', 'type': 'file', 'sha256': H('c')}])
    d = os.path.join(w.cred, w.opid)
    if os.geteuid() != 0:
        try:
            os.lstat(os.path.join(d, 'pg', 'pg-password'))
            check(False, 'B3 model: after the chown, a host-side lstat inside the directory fails')
        except PermissionError:
            check(True, 'B3 model: after the chown, a host-side lstat inside the directory raises PermissionError, and the generator still verifies (in the user namespace)')
    stats = [c for c in w.calls if c[:4] == ['stat', '-c', '%u %g %a %F', '--']]
    check(len(stats) == len(CRED_GENS) and all(c[4] == os.path.join(d, os.path.basename(c[4])) for c in stats),
          'credentials: owner, group and mode are read inside the user namespace, once per generation directory')
    w.unlock()
    modes_ok = all(stat.S_IMODE(os.lstat(os.path.join(d, gn)).st_mode) == 0o700 for gn in CRED_GENS) and all(
        stat.S_IMODE(os.lstat(os.path.join(d, gn, f)).st_mode) == 0o600 for gn in CRED_GENS for f in os.listdir(os.path.join(d, gn)))
    check(modes_ok and sorted(os.listdir(d)) == sorted(CRED_GENS), 'credentials: one 0700 directory per generation that has credentials, every file 0600, nothing else (no work directory)')
    check(all(sorted(os.listdir(os.path.join(d, gn))) == CRED_GENS[gn] for gn in CRED_GENS) and rec['holders'] == CRED_GENS, 'credentials: each generation holds exactly the files it names')
    check('pg.connection' not in CRED_GENS['feed-replay'] and 'operator.token' not in CRED_GENS['feed-replay'] and 'collector.secret' not in CRED_GENS['feed-admin']
          and not any('collector.secret' in CRED_GENS[gn] for gn in ('host-init', 'host-restart')) and 'hold' not in CRED_GENS,
          'N4: the /in replay gets no pg connection or operator token, feed-admin no collector grant, host never the grant, hold nothing')
    check(not any(f.endswith(('.key', '.csr', '.ext')) for _, _, fs in os.walk(d) for f in fs), 'credentials: no private key, request or extension file survives')
    check(len(seen) == 6 and len(set(seen)) == 6 and all(len(x) >= 43 for x in seen), 'credentials: six distinct CSPRNG values (secrets.token_urlsafe), none shorter than 32 bytes of entropy')
    check(M.default_deps()['token'] is M.secrets.token_urlsafe, 'credentials: production draws from secrets.token_urlsafe')
    recv = os.path.join(d, 'recv-init')
    pv = subprocess.run([OPENSSL, 'pkcs12', '-in', os.path.join(recv, 'server.pfx'), '-passin', 'file:' + os.path.join(recv, 'server.password'), '-nokeys', '-clcerts'],
                        capture_output=True, text=True)
    ca = os.path.join(d, 'feed-replay', 'ca.pem')
    leaf = os.path.join(tmp, 'leaf.pem')
    open(leaf, 'w').write(pv.stdout)
    vr = subprocess.run([OPENSSL, 'verify', '-CAfile', ca, leaf], capture_output=True, text=True)
    san = subprocess.run([OPENSSL, 'x509', '-in', leaf, '-noout', '-ext', 'subjectAltName'], capture_output=True, text=True).stdout
    check(pv.returncode == 0 and vr.returncode == 0 and 'DNS:localhost' in san, 'credentials: the PFX opens with its password file and its certificate chains to the per-run CA, for DNS:localhost')
    check(sha(open(ca, 'rb').read()) == rec['caCertSha256'] and open(os.path.join(d, 'host-init', 'ca.pem'), 'rb').read() == open(ca, 'rb').read(), 'credentials: every generation gets the same CA, recorded by digest')
    hc = json.load(open(os.path.join(recv, 'host-config.json')))
    g = hc['Credentials'][0]
    check(hc['Schema'] == 'fsgg.telemetry.host-config/2' and hc['ListenUrl'] == f'https://localhost:{M.RECV_PORT}' and len(hc['Credentials']) == 1
          and (g['Role'], g['WorkspaceId'], g['ProducerId'], g['StreamId']) == ('native-collector', M.WS, M.PRODUCER, M.STREAM) and g['SecretFile'] == '/cred/collector.secret',
          'credentials: host-config/2 grants one native-collector principal for the single replay identity (design 2.3)')
    pgc = open(os.path.join(d, 'host-init', 'pg.connection')).read()
    check(pgc.endswith('SSL Mode=Disable;Pooling=false') and f'Password={open(os.path.join(d, "pg", "pg-password")).read()};' in pgc, 'credentials: the host\'s connection string carries the pg password')
    check(open(os.path.join(d, 'feed-replay', 'spool.sha256')).read() == f'{H("c")}  /work/spool/{M.REPLAY_SUBTREE}/a.ready\n', 'credentials: the replay check list names the copy, not the snapshot')
    rec_s = json.dumps(rec)
    check(not any(x in rec_s for x in seen), 'credentials: the record carries no secret')
    chowns = sorted((os.path.basename(c[3]), c[2]) for c in w.calls if c[0] == 'chown')
    check(chowns == sorted((gn, '999:999' if gn == 'pg' else '1000:1000') for gn in CRED_GENS), 'credentials: pg\'s directory goes to container uid 999, the others to 1000')
    check(not any(x in json.dumps(w.calls) for x in seen), 'credentials: no secret is passed in an argv (openssl reads the PFX password from a file)')
    raises(lambda: M.gen_credentials(env, deps, w.opid, []), M.Refuse, 'already exists', 'credentials: a second generation for the same op id refuses')
    # write_private never follows or reuses: O_EXCL and O_NOFOLLOW
    tgt = os.path.join(tmp, 'exists')
    open(tgt, 'w').write('old')
    raises(lambda: M.write_private(tgt, b'new'), FileExistsError, '', 'credentials: write_private refuses an existing file (O_EXCL)')
    os.symlink(os.path.join(tmp, 'elsewhere'), os.path.join(tmp, 'link'))
    raises(lambda: M.write_private(os.path.join(tmp, 'link'), b'x'), FileExistsError, '', 'credentials: write_private refuses a dangling symlink (no file is created through it)')
    check(not os.path.exists(os.path.join(tmp, 'elsewhere')) and open(tgt).read() == 'old', '... and nothing was written through either')
CRED_CASES = [
    ('a credential root not on a tmpfs', lambda w: (w.mounts.pop(os.path.dirname(w.cred)), w.write_mounts()), 'not on a tmpfs'),
    ('a credential root of mode 0755', lambda w: (os.makedirs(w.cred), os.chmod(w.cred, 0o755)), 'not a 0700 directory'),
    ('a credential root of another account', lambda w: setattr(w, 'deps_over', {'geteuid': lambda: UID + 1}), 'not a 0700 directory of this account'),
    ('a failing openssl', lambda w: setattr(w, 'env_over', {'openssl': '/bin/false'}), 'openssl ca failed'),
    ('a failing chown', lambda w: w.fail.add('chown'), 'is not handed to container uid'),
    ('a chown that leaves another owner', lambda w: w.fail.add('chown_uid'), 'is not handed to container uid'),
    ('a chown that leaves another group', lambda w: w.fail.add('chown_gid'), 'is not handed to container uid'),
    ('files readable by others after the chown', lambda w: w.fail.add('chown_mode'), 'is not handed to container uid'),
    ('an unsafe sealed path', lambda w: setattr(w, 'files_over', [{'path': '../x', 'type': 'file', 'sha256': H('c')}]), 'cannot carry'),
]
for what, mut, needle in CRED_CASES:
    with tempfile.TemporaryDirectory() as tmp:
        w = DriveWorld(tmp)
        w.env_over, w.deps_over, w.files_over = {}, {}, [{'path': 'a/b', 'type': 'file', 'sha256': H('c')}]
        mut(w)
        raises(lambda: M.gen_credentials(w.env(**w.env_over), w.drive_deps(**w.deps_over), w.opid, w.files_over), M.Refuse, needle, f'credentials: {what} refuses')
        w.unlock()
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    M.gen_credentials(w.env(), w.drive_deps(), w.opid, [{'path': 'a/b', 'type': 'file', 'sha256': H('c')}])
    check(True, 'credentials: causal control, the same world generates')
    w.unlock()
# the mounts: read-only, per generation, none for a generation without credentials
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    for gn, g in TP['generations'].items():
        slot = g['slot'] if g['slot'] != '*' else 'feed'
        cm = [m for m in M.mounts_of(M.ctr_argv(w.env(), w.opid, gn, slot, 'snap-1')) if m['destination'] == '/cred']
        if g.get('creds'):
            check(cm == [{'type': 'bind', 'source': os.path.join(w.cred, w.opid, gn), 'destination': '/cred', 'readOnly': True}], f'{gn}: /cred is its own directory, read-only')
        else:
            check(cm == [], f'{gn}: no credentials, no /cred mount')

# ---- authority: each refusal leaves nothing behind
AUTH_CASES = [
    ('another uid', lambda w: None, {'geteuid': lambda: UID + 1}, 'run as fsgg-learn-qual'),
    ('no nonce', lambda w: None, {'environ': {}}, 'FSGG_NONCE missing'),
    ('outside the pod unit', lambda w: open(w.selfcg, 'w').write('0::/user.slice/session-1.scope\n'), {}, 'not running inside learn-pod-<id>.service'),
    ('the finalizer unit not active', lambda w: setattr(w, 'fin_active', False), {}, 'finalizer unit is not active'),
    ('an admission for another operator', lambda w: (os.unlink(os.path.join(w.view, 'admissions', w.opid + '.admit')), w.admit(operator_sha=H('9'))), {}, 'does not name this op'),
    ('no input-read marker', lambda w: os.unlink(os.path.join(w.state, 'learn-input', w.opid, 'input-read')), {}, 'no input-read marker'),
    ('a claim of another op', lambda w: w.claim(opid='op-zzz'), {}, 'names another op'),
    ('a held plan', lambda w: setattr(w, 'env_over', {'plan': M.PLAN}), {}, 'the plan cannot run'),
    ('a driver that already ran', lambda w: os.makedirs(os.path.join(w.state, 'learn-drive', w.opid)), {}, 'already ran'),
]
for what, mut, dover, needle in AUTH_CASES:
    with tempfile.TemporaryDirectory() as tmp:
        w = DriveWorld(tmp)
        w.env_over = {}
        mut(w)
        code, r, out, log = drive(w, env=w.env_over, deps=dover)
        check(code == 5 and any(needle in e for e in r['errors']) and not any(c[:2] == ['podman', 'run'] or c[:3] == ['podman', 'pod', 'create'] for c in w.calls)
              and not os.path.exists(os.path.join(w.cred, w.opid)), f'drive authority: {what} refuses and starts nothing, generates nothing ({needle})')
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    code, r, out, log = drive(w, argv=['--drive', '--id', w.opid, '--source-identity', w.ident, '--argv-json', '[]'])
    check(code == 5 and not w.calls, 'drive: extra arguments refuse before anything')

# ---- fail closed: each abort stops before the destructive act, fills empty slots, stops every role, never removes evidence


def abort_case(what, setup, needle, forbid=None, env=None, trigger='done'):
    with tempfile.TemporaryDirectory() as tmp:
        w = DriveWorld(tmp)
        setup(w)
        code, r, out, log = drive(w, env=env)
        ab = events(log, 'abort')
        ok = code == 3 and r['verdict'] == 'aborted' and ab and needle in ab[0]['reason'] and w.finalized and r['credentialsRemoved']
        check(ok, f'abort: {what} aborts ({needle}), every role is stopped so the finalizer retires, and the credentials are removed: {r["errors"]}')
        check(w.trigger == trigger and all(w.archived.values()) and not any(o[0] == 'second-term' for o in w.order),
              f'abort: {what}: the finalizer triggers on {trigger} with every role\'s /out still there; no done role got a second TERM')
        if forbid:
            i = next(i for i, e in enumerate(log) if e['event'] == 'abort')
            check(not any(forbid(e) for e in log[:i]) and not any(forbid(e) for e in log[i:] if e['event'] not in ('start', 'abort', 'abort-stopped', 'end')),
                  f'abort: {what}: the forbidden act never happens')
        return w, log


def ack_after(k, **kv):
    def f(w):
        w.ackmode.update(kv)
    return f


abort_case('an ack that is not ok', ack_after(1, ok=False), 'does not acknowledge', lambda e: e['event'] in ('remove', 'kill'))
abort_case('an ack with a rejected role', lambda w: w.ackmode.update({('role', 'host'): 'rejected:budget'}), 'every role accepted', lambda e: e['event'] in ('remove', 'kill'))
abort_case('an ack for another request', lambda w: w.ackmode.update(sha=H('0')), 'does not acknowledge', lambda e: e['event'] in ('remove', 'kill'))
abort_case('an ack that never comes', lambda w: w.ackmode.update(skip=1), 'timed out: ack-1', lambda e: e['event'] in ('remove', 'kill'))


def drop_status(k):
    def f(w):
        def br(n_):
            if n_ == 2:
                os.unlink(os.path.join(w.cust, 'checkpoints', '2', 'extract-host', '.status'))
        w.custody_break = br
    return f


w2, log2 = abort_case('custody without host\'s status at the clean-restart checkpoint', drop_status(2), 'no readable status for host in checkpoints', lambda e: e['event'] == 'remove')
abort_case('custody without the step evidence of a done role', lambda w: w.no_complete.add('recv'), 'no step evidence of recv', lambda e: e['event'] == 'remove')
abort_case('a payload at the wrong host uid', lambda w: w.hostuid_over.update(recv='0'), 'payload host uids', lambda e: e['event'] == 'ready' and e.get('slot') == 'recv')
abort_case('a container outside the pod unit cgroup', lambda w: w.cg_outside.add('feed'), 'outside the pod unit cgroup: feed', lambda e: e['event'] == 'checkpoint-request')
abort_case('the infra container outside the pod unit cgroup', lambda w: w.cg_outside.add('infra'), 'outside the pod unit cgroup: infra', lambda e: e['event'] == 'checkpoint-request')
abort_case('an undeclared listener', lambda w: w.extra_ports.add(8080), 'are not the declared', lambda e: e['event'] == 'checkpoint-request')
abort_case('a role that ends before it is ready', lambda w: w.ready_fail.add('host'), 'host ended before it was ready', lambda e: e['event'] == 'checkpoint-request')
w3, log3 = abort_case('a process surviving the SIGKILL of host', lambda w: setattr(w, 'survivor', True), 'still has processes')
k3 = max(i for i, e in enumerate(log3) if e['event'] == 'kill' and e['slot'] == 'host')
check(not any(e['event'] == 'remove' for e in log3[k3:]) and not any(e['event'] == 'start' and e.get('generation') == 'host-restart' for e in log3[k3:]),
      'abort: a survivor of the SIGKILL stops the restart: host is neither removed nor started again')
abort_case('a start refused with EPERM', lambda w: w.fail.add('start_recv'), 'start recv-init: EPERM', lambda e: e['event'] == 'checkpoint-request')
abort_case('a removal that leaves the container', lambda w: w.fail.add('rm_stuck'), 'still exists', None)
abort_case('a TERM that is ignored', lambda w: w.term_ignored.add(f'learn-pod-{w.opid}-host'), 'timed out: host done after TERM', lambda e: e['event'] == 'checkpoint-ack' and e['n'] >= 2,
           trigger='time')                                 # host ignores every TERM: only the finalizer's own deadline ends it


def slow_world(w):
    w.ack_delay = 2000
abort_case('a drive that outruns its budget', slow_world, 'timed out: ack-1', lambda e: e['event'] == 'remove')

# ---- the actions refuse destructive acts directly, whatever the plan says
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    env, deps = w.env(self_cgroup=w.selfcg), w.drive_deps()
    os.makedirs(os.path.join(w.state, 'learn-drive', w.opid))
    drv = M.Driver(env, deps, w.opid, w.ident, 'cd' * 16, 'snap-1')
    drv.unit_cg = w.unit_cg
    M.gen_credentials(env, deps, w.opid, [])
    w.run(['podman', 'pod', 'create'])
    with contextlib.redirect_stdout(io.StringIO()):
        drv.a_start('recv-init')
        n_calls = len(w.calls)
        raises(lambda: drv.a_kill('recv'), M.Abort, 'custody does not hold', 'kill: a role container whose state no checkpoint holds is not killed')
        raises(lambda: drv.a_remove('recv'), M.Abort, 'custody does not hold', 'remove: a role container whose state no checkpoint holds is not removed')
        check(len(w.calls) == n_calls, '... and no podman command was issued for either')
        drv.a_start('host-init')
        drv.a_start('feed-admin')
        drv.a_checkpoint('x')
        drv.a_stop('recv')
        raises(lambda: drv.a_remove('recv'), M.Abort, 'custody does not hold', 'remove: a container stopped after the last checkpoint is not removed (its terminal state is not in custody)')
        drv.a_checkpoint('y')
        drv.a_remove('recv')
        check(drv.slots['recv'] is None, 'remove: causal control, after a checkpoint it is removed')
        raises(lambda: drv.a_checkpoint('z'), M.Abort, 'the recv slot is not running', 'checkpoint: refused while a role slot is empty (finalizer r5 would reject it)')
        check(not os.path.exists(os.path.join(w.ckdir, 'req-3')), '... before any request is written')
    req = open(os.path.join(w.ckdir, 'req-1'), 'rb').read()
    check(json.loads(req) == {'label': 'x', 'n': 1} and len(req) <= 512 and stat.S_IMODE(os.stat(os.path.join(w.ckdir, 'req-1')).st_mode) == 0o600
          and not any(f.startswith('tmp-req') for f in os.listdir(w.ckdir)), 'requests are {"n","label"} JSON within 512 bytes, 0600, renamed into place (no partial request)')

# ---- B1: abort never sends a second TERM to a done role (the role contract: it would exit and lose /out), and never removes a running container
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.term_late.add(f'learn-pod-{w.opid}-recv')          # recv does not end within 60 s of its TERM in the clean restart; host is already done then
    code, r, out, log = drive(w)
    ab = events(log, 'abort')
    check(code == 3 and ab and 'recv done after TERM' in ab[0]['reason'], 'B1: the clean-restart TERM of recv times out, so the driver aborts while host is done')
    i = next(i for i, e in enumerate(log) if e['event'] == 'abort')
    check(not any(o[0] == 'second-term' for o in w.order) and events(log, 'abort-stopped')[0]['termed'] == 'recv,feed',
          'B1: abort TERMs only the roles that are not done (recv, and feed\'s hold); host, done and blocking, gets no second TERM')
    check(w.trigger == 'done' and w.archived['host'] and w.archived['feed'],
          'B1: the finalizer then triggers on done and finds host\'s /out (its terminal state) intact')
    rms = [o for o in w.order if o[0] == 'rm']
    check(not any(o[1].endswith(('-host', '-recv', '-feed')) for o in w.order[w.order.index(('kill', f'learn-pod-{w.opid}-recv', 'TERM')):] if o[0] == 'rm'),
          'B1: abort removes no running container')

# ---- B2: the final wait leaves only on a definite state, and an exception never ends it early
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.archive_s = 60                                       # the finalizer pauses and tars each role for 60 s before it removes the pod
    w.fail |= {'active_blip', 'exists_raise_once'}
    code, r, out, log = drive(w)
    check(code == 0 and r['podGone'] is True and not w.pod and ('finalizer-removed-pod',) in w.order and w.order.index(('finalizer-removed-pod',)) < len(w.order),
          'B2: a timed-out is-active and a raised exception during the archive do not end the wait; the driver leaves when the pod is gone')
    check(not os.path.exists(os.path.join(w.cred, w.opid)), 'B2: ... and only then removes the credentials')
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.archive_s, w.fin_dead_at = 10 ** 6, True
    orig_tick = w.fin_tick

    def tick_then_die(w=w, orig=orig_tick):
        orig()
        if w.finalized:
            w.fin_dead = True                              # the finalizer unit fails during the archive; the pod remains
    w.fin_tick = tick_then_die
    code, r, out, log = drive(w)
    check(r['finalizerEnded'] is True and r['podGone'] is False and w.clock < M.FIN_RUNTIME_S,
          'B2: a finalizer unit that reads `failed` ends the wait at once (the pod unit\'s ExecStop then removes the pod), well inside the bound')
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.archive_s = 10 ** 6
    w.fail.add('exists_unknown')
    code, r, out, log = drive(w)
    check(r.get('finalWaitTimedOut') is True and r['podGone'] is False and r['finalizerEnded'] is False and w.clock >= M.FIN_RUNTIME_S,
          'B2: an answer that is never definite keeps the driver waiting until the finalizer\'s RuntimeMaxSec bound, and no longer')
check(M.FIN_RUNTIME_S == F5.CUSTODY_DEADLINE_S + 90 and M.DRIVE_BUDGET_S + 300 <= F5.SOFT_S, 'the final wait is bounded by the finalizer unit\'s 1980 s; the drive budget leaves 300 s before the finalizer\'s soft deadline')
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.mounts.pop(os.path.dirname(w.cred))
    w.write_mounts()
    code, r, out, log = drive(w)
    check(code == 3 and r['verdict'] == 'aborted' and events(log, 'abort') and any('not on a tmpfs' in e for e in r['errors']),
          'a refusal after the driver exists (credentials not on a tmpfs) is an abort with exit 3, never 0 and never without the abort')

with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.ackmode.update(ok=False)                              # an abort is needed
    real_abort = M.Driver.abort

    def broken_abort(self, why):
        raise RuntimeError('abort itself failed')
    M.Driver.abort = broken_abort
    try:
        code, r, out, log = drive(w)
    finally:
        M.Driver.abort = real_abort
    check(code == 3 and any('abort: RuntimeError' in e for e in r['errors']) and r['credentialsRemoved'] is True and events(log, 'end'),
          'B2: an abort that itself raises is caught: the driver still waits for the finalizer, removes the credentials and exits 3')

# ---- N2: an ack seen half-written is read again until it parses
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.partial_ack = True
    code, r, out, log = drive(w)
    check(code == 0 and r['checkpoints'] == 5, 'N2: a partially written ack-<n> is retried until it parses; the run completes')
# ---- N3: the replay must end ok
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.fail.add('replay_fail')
    code, r, out, log = drive(w)
    check(code == 3 and 'feed ended error' in events(log, 'abort')[0]['reason'], 'N3: a replay that ends error aborts the drive (wait-done reads feed\'s reason)')

# ---- N5: a sealed path the replay check cannot carry refuses BEFORE the marker
with tempfile.TemporaryDirectory() as tmp:
    w = World(tmp)
    w.admit()
    w.claim(files=[{'path': '../escape', 'type': 'file', 'size': 1, 'sha256': H('c'), 'mtime_ns': 1}])
    code, r, _ = go(w, RUN)
    check(code == 5 and err(r, 'cannot carry') and not os.path.exists(os.path.join(w.state, 'learn-input', w.opid)) and not getattr(w, 'started', None),
          'N5: an unsafe sealed path refuses the --run before the input-read marker and before any unit')

# ---- N6: argv[0] rule
for what, fn, needle in (('a relative step command', lambda p: p['generations']['recv-init']['steps'].append(('x', ['true'])), 'neither an absolute path nor a runner built-in'),
                         ('a relative service', _gen('recv-init', service=['dotnet', 'x']), 'service argv[0] is not an absolute path'),
                         ('a generation name that is not a slug', lambda p: p['generations'].__setitem__('Bad/Gen', {'slot': 'feed', 'creds': [], 'steps': [], 'service': None}),
                          'not a valid name')):
    check(has(mutate(fn), needle), f'N6 plan: {what} is refused')
check(has(mutate(lambda p: None), 'x') is False and mutate(lambda p: p['generations']['recv-init']['steps'].append(('w', ['wait-tcp', '1', '1']))) == [],
      'N6 plan: a built-in passes the argv[0] rule')

# ---- N6: the step runner stops a step's whole process group on TERM, and checks its own writes
with tempfile.TemporaryDirectory() as tmp:
    out = os.path.join(tmp, 'out')
    os.makedirs(os.path.join(out, 'host'))
    script = M.STEP_RUNNER.replace('/out/', out + '/')
    p = subprocess.Popen([SH, '-c', script, 'step-runner', 'host', 'long', '3', '/bin/sh', '-c', 'sleep 60 & sleep 60 & wait'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    pg = os.path.join(out, 'host', 'steps', '01-long', 'pgid')
    for _ in range(100):
        if os.path.exists(pg) and os.path.getsize(pg):
            break
        __import__('time').sleep(0.05)
    __import__('time').sleep(0.3)
    gid = int(open(pg).read())
    alive_before = subprocess.run(['pgrep', '-g', str(gid)], capture_output=True, text=True).stdout.split()
    t_term = __import__('time').monotonic()
    p.send_signal(15)
    rc = p.wait(timeout=30)
    took = __import__('time').monotonic() - t_term
    __import__('time').sleep(0.2)
    alive_after = subprocess.run(['pgrep', '-g', str(gid)], capture_output=True, text=True).stdout.split()
    check(len(alive_before) >= 3 and rc == 143 and alive_after == [], f'N6 runner: TERM stops the step\'s whole process group (the sleeps included) and exits 143 ({len(alive_before)} before, {alive_after} after)')
    check(gid != os.getpgrp(), 'N6 runner: the step runs in its own process group (setsid)')
    check(took < 5, f'N6 runner: the TERM reaches every member of the group at once (the runner is done in {took:.1f} s, not after its 15 s KILL fallback)')
if os.geteuid() != 0:
    with tempfile.TemporaryDirectory() as tmp:
        rc, d, p = runner(tmp, 'host', ['a', '3', '/bin/chmod', '500', os.path.join(tmp, 'out', 'host', 'steps', '01-a')])
        check(rc == 70 and not os.path.exists(os.path.join(d, '01-a', 'rc')), 'N6 runner: an rc file that cannot be written exits 70')
    with tempfile.TemporaryDirectory() as tmp:
        rc, d, p = runner(tmp, 'host', ['a', '3', '/bin/chmod', '500', os.path.join(tmp, 'out', 'host', 'steps')])
        check(rc == 70 and not os.path.exists(os.path.join(d, 'complete')), 'N6 runner: a steps/complete that cannot be written exits 70')
        os.chmod(d, 0o700)
with tempfile.TemporaryDirectory() as tmp:
    rc, d, p = runner(tmp, 'feed', ['a', '1', '/bin/true', 'service', '3', '/bin/sh', '-c', f'test -f {os.path.join(tmp, "out", "feed", "steps", "complete")} && echo SAW-COMPLETE'])
    check(rc == 0 and 'SAW-COMPLETE' in p.stdout, 'runner: steps/complete exists before the service starts (it is written, then the service is exec\'d)')

# ---- N8: authority survivors
for what, mut, needle in (('an admission for another source identity', lambda w: (os.unlink(os.path.join(w.view, 'admissions', w.opid + '.admit')),
                                                                                   setattr(w, 'ident', H('7')), w.admit(), setattr(w, 'ident', sha(b'source'))), 'does not name this op'),
                          ('an admission for another op id', lambda w: (os.unlink(os.path.join(w.view, 'admissions', w.opid + '.admit')),
                                                                         os.rename(w.admit(opid='op-pod-9')['admission'], os.path.join(w.view, 'admissions', w.opid + '.admit'))),
                           'does not name this op'),
                          ('the cgroup of another op\'s pod unit', lambda w: open(w.selfcg, 'w').write('0::/user.slice/x/app.slice/learn-pod-op-pod-11.service\n'), 'not running inside'),
                          ('a cgroup below the pod unit\'s', lambda w: open(w.selfcg, 'w').write(f'0::{w.unit_cg}/sub\n'), 'not running inside')):
    with tempfile.TemporaryDirectory() as tmp:
        w = DriveWorld(tmp)
        mut(w)
        code, r, out, log = drive(w)
        check(code == 5 and any(needle in e for e in r['errors']) and not any(c[:3] == ['podman', 'pod', 'create'] for c in w.calls), f'N8 authority: {what} refuses')

# ---- N8: reconcile, remove, checkpoint and readback survivors, driven action by action
def driver_at(tmp):
    w = DriveWorld(tmp)
    env, deps = w.env(self_cgroup=w.selfcg), w.drive_deps()
    os.makedirs(os.path.join(w.state, 'learn-drive', w.opid))
    drv = M.Driver(env, deps, w.opid, w.ident, 'cd' * 16, 'snap-1')
    drv.unit_cg = w.unit_cg
    with contextlib.redirect_stdout(io.StringIO()):
        M.gen_credentials(env, deps, w.opid, [])
        w.run(['podman', 'pod', 'create'])
        for g in ('pg', 'recv-init', 'host-init', 'feed-admin'):
            drv.a_start(g)
    return w, drv


with tempfile.TemporaryDirectory() as tmp:
    w, drv = driver_at(tmp)
    with contextlib.redirect_stdout(io.StringIO()):
        drv.a_checkpoint('a')
        drv.a_kill('host')
        os.remove(w.cg + w.ctrs[f'learn-pod-{w.opid}-host']['cg'] + '/cgroup.procs')
        os.mkdir(w.cg + w.ctrs[f'learn-pod-{w.opid}-host']['cg'] + '/cgroup.procs')
        raises(lambda: drv.a_reconcile('host'), M.Abort, 'still has processes', 'N8 reconcile: an unreadable cgroup.procs is not "no process"')
        os.rmdir(w.cg + w.ctrs[f'learn-pod-{w.opid}-host']['cg'] + '/cgroup.procs')
        drv.a_reconcile('host')
        check(True, 'N8 reconcile: causal control, a cgroup that is gone reconciles')
        drv.slots['host']['cgroup'] = None
        raises(lambda: drv.a_reconcile('host'), M.Abort, 'still has processes', 'N8 reconcile: a container whose cgroup was never read is not reconciled')
        drv.slots['host']['cgroup'] = w.ctrs[f'learn-pod-{w.opid}-host']['cg']
        w.ctrs[f'learn-pod-{w.opid}-host']['state'] = 'running'
        raises(lambda: drv.a_reconcile('host'), M.Abort, 'not exited', 'N8 reconcile: a container that is not exited is not reconciled, even with an empty cgroup')
with tempfile.TemporaryDirectory() as tmp:
    w, drv = driver_at(tmp)
    with contextlib.redirect_stdout(io.StringIO()):
        drv.a_checkpoint('a')
        w.fail.add('exists_err')
        raises(lambda: drv.a_remove('recv'), M.Abort, 'still exists', 'N8 remove: an error from `podman container exists` is not "gone"')
with tempfile.TemporaryDirectory() as tmp:
    w, drv = driver_at(tmp)
    drv.ck = M.MAX_CHECKPOINTS
    raises(lambda: drv.a_checkpoint('ninth'), M.Abort, 'checkpoint budget exhausted', 'N8 checkpoint: a ninth checkpoint is refused before its request')
    check(not os.path.exists(os.path.join(w.ckdir, f'req-{M.MAX_CHECKPOINTS + 1}')), '... and no request is written')
body = b'{"label":"x","n":1}'
good = {'n': 1, 'requestSha256': sha(body), 'ok': True, 'roles': {r_: 'accepted' for r_ in M.ROLES}, 'chainHead': H('e')}
check(M.ack_problems(json.dumps(good).encode(), 1, body) == [], 'ack_problems: causal control, a complete ack passes')
for what, mut in (('no chain head', lambda a: a.pop('chainHead')), ('a short chain head', lambda a: a.__setitem__('chainHead', 'e' * 10)), ('ok false', lambda a: a.__setitem__('ok', False)),
                  ('a role missing', lambda a: a['roles'].pop('feed'))):
    a_ = copy.deepcopy(good)
    mut(a_)
    check(M.ack_problems(json.dumps(a_).encode(), 1, body) != [], f'N8 ack: {what} is refused')
with tempfile.TemporaryDirectory() as tmp:
    gen = {'steps': [('x', ['/y'])], 'service': ['/s']}

    def extract(st, complete=True):
        d = tempfile.mkdtemp(dir=tmp)
        open(os.path.join(d, '.status'), 'wb').write(json.dumps(st).encode().ljust(4096, b' '))
        if complete:
            os.makedirs(os.path.join(d, 'steps'))
            open(os.path.join(d, 'steps', 'complete'), 'w').close()
        return d
    ok_st = {'schema': 'learn-pod-status/1', 'role': 'host', 'phase': 'ready'}
    check(M.readback_extract(extract(ok_st), 'host', gen)['phase'] == 'ready', 'readback: causal control, a matching status with step evidence passes')
    for what, st, complete, needle in (('another role', dict(ok_st, role='recv'), True, 'another status'), ('another schema', dict(ok_st, schema='x/1'), True, 'another status'),
                                       ('signalled without step evidence', dict(ok_st, phase='terminal', reason='signalled', exit=143), False, 'no step evidence'),
                                       ('ready without step evidence', ok_st, False, 'no step evidence')):
        raises(lambda: M.readback_extract(extract(st, complete), 'host', gen), M.Abort, needle, f'N8 readback: {what} is refused')
    check(M.readback_extract(extract(dict(ok_st, phase='terminal', reason='error', exit=1), False), 'host', gen)['phase'] == 'terminal',
          'readback: a step that failed has no steps/complete, and its evidence is still read back')
# ---- N8: the drive budget is enforced
with tempfile.TemporaryDirectory() as tmp:
    w = DriveWorld(tmp)
    w.ack_delay = 400                                       # each ack within its own 600 s bound, but five of them exceed the 1500 s budget
    code, r, out, log = drive(w)
    ab = events(log, 'abort')
    check(code == 3 and ab and ('budget' in ab[0]['reason'] or 'timed out' in ab[0]['reason']) and log[[e['event'] for e in log].index('abort')]['utc'] and w.clock < M.DRIVE_BUDGET_S + 700,
          'N8: a drive that would run past its 1500 s budget aborts inside it')

print(f'learn-pod-operator-r2: {n} assertions passed')

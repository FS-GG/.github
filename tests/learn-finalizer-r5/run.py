#!/usr/bin/python3 -I
# Gates for LEARN finalizer r5 (deployment/learn-finite-profile/finalizer), Refs #4647.
#
# Drives main() through a fake pod world (tests/learn-finalizer-r5/world.py): podman, systemctl and journalctl are fakes, /proc and the cgroup tree are
# temporary directories, the clock is fake (sleep advances it and fires scheduled events), the loop volume is a statvfs stub. The archive step runs the REAL tar
# on a fake /proc/<pid>/root/out/<role>. It checks the contract the claim tooling reads (opId, inputRead, containersStartedWithInput, runNonceSha256,
# finalizerSha256), the shared member/content budget, the checkpoint handshake, pg's SIGINT and pg_controldata, every retirement check, and that the blocks taken
# from finalizer r4 and from the pod operator are byte for byte the originals. stdlib only; no container, no root, no systemd, no network.
#
# The two `paths:` lists of the workflow are identical on purpose; `check-paths-coherence.py` rule (a) reds a divergence.
import ast, hashlib, importlib.util, io, json, os, shutil, stat, sys, tarfile, tempfile, time
from contextlib import redirect_stdout

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import world as W

FIN = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'finalizer', 'learn-finalizer-r5.py')
R4 = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'baseline', 'libexec', 'learn-finalizer-r4.py')
POD = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'pod-operator', 'learn-pod-operator-r1.py')
n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


spec = importlib.util.spec_from_file_location('fin', FIN)
F = importlib.util.module_from_spec(spec)
spec.loader.exec_module(F)
FIN_SHA = hashlib.sha256(open(FIN, 'rb').read()).hexdigest()
sha = lambda b: hashlib.sha256(b).hexdigest()


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


r5s, r4s, pods = segs(FIN), segs(R4), segs(POD)
for name in ('CAP', 'CONTENT_CAP, MAX_MEMBERS, MAX_DEPTH', 'SOFT_S, CUSTODY_DEADLINE_S, ANCHOR_WAIT_S, POLL_S, CHAIN_ANCHOR_S', 'DPATH', 'DROP_EVERY', 'drop', 'now', 'mark', 'run', 'open_nofollow',
             'chain', 'show', 'archive', 'extract', 'markers', 'evidence_dir', 'evidence_file', 'write_record', 'finish', 'cgroup_retired'):
    check(r5s[name] == r4s[name], f'{name} is byte for byte the one in finalizer r4')
for name in ('DIR_FLAGS', '_check_dir_fd', 'open_dir_chain', 'read_pinned'):
    check(r5s[name] == pods[name], f'{name} is byte for byte the one in the pod operator')
check(hashlib.sha256(open(R4, 'rb').read()).hexdigest().startswith('7f875245'), 'the imported r4 is the admitted finalizer (7f875245)')
check(F.CAP == 272640000 and F.MAX_MEMBERS == 4096 and F.CONTENT_CAP == 256 << 20 and F.SOFT_S == 1800 and F.CUSTODY_DEADLINE_S == 1890, 'the r4 caps and timings are unchanged')
check(F.VOLUME_CAP == 1 << 30 and F.VOLUME_BUDGET == 0.90 and F.VOLUME_SAMPLE_S == 5 and F.MAX_CHECKPOINTS == 8, 'volume cap 1 GiB, budget 90%, sample every 5 s, at most 8 checkpoints')
check(F.SECCOMP == '/etc/fsgg/learn/seccomp-8bd52f8d.json', 'the one-shot pg_controldata container uses the admitted seccomp profile')
src = open(FIN).read()
check('podman exec' not in src and "'exec'" not in src and 'pod create' not in src and 'pod start' not in src, 'the finalizer never execs into, creates or starts anything in the pod')
check(src.count("'podman', 'run'") == 1, 'the only container the finalizer runs is the pg_controldata one-shot')


# ============================================================ driving main()
class Res:
    pass


def go(w, patch=None, argv=None, env=None, euid=30034, over=None):
    saved = {}
    patch = dict(patch or {})
    patch.setdefault('run', w.run)
    patch.setdefault('time', W.FakeTime(w))
    for k, v in patch.items():
        saved[k] = getattr(F, k)
        setattr(F, k, v)
    cfg = dict(home=w.home, view=w.view, proc=w.proc, cgroot=w.cg, slice_dir=w.slice, unshare=[], tar=['/usr/bin/tar'], statvfs=w.statvfs, owner_uid=os.getuid(), chain_start=w.top,
               volume_root=w.volroot, cred_root=w.cred, is_mountpoint=lambda p: w.mounted, production=False)
    cfg.update(over or {})
    buf, r = io.StringIO(), Res()
    r.code, r.w, r.exc = None, w, None
    try:
        with redirect_stdout(buf):
            try:
                F.main(argv if argv is not None else ['--id', W.OPID, '--source-identity', W.IDENT],
                       env if env is not None else {'FSGG_NONCE': W.NONCE, 'FSGG_PG_IMAGE': W.PGIMG}, euid, **cfg)
            except SystemExit as e:
                r.code = e.code
    finally:
        for k, v in saved.items():
            setattr(F, k, v)
    r.out = buf.getvalue()
    r.evid = os.path.join(w.home, '.local', 'state', 'learn-evidence', W.OPID)
    r.cust = os.path.join(w.home, '.local', 'state', 'learn-custody', W.OPID)
    r.ck = os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID)
    try:
        r.rec = json.load(open(os.path.join(r.evid, 'record.json')))
    except (OSError, ValueError):
        r.rec = None
    r.calls = w.calls
    return r


def world(tmp, done_at=20, activate_at=1, reasons=None, files=True):
    w = W.World(tmp)
    w.write_admission(FIN_SHA)
    if activate_at is not None:
        w.at(activate_at, w.activate)
    if done_at is not None:
        reasons = reasons or {'host': 'signalled', 'recv': 'signalled', 'feed': 'ok'}

        def done():
            for role in W.ROLES:
                if files:
                    w.put(role, 'out.json', b'{"role":"%s"}' % role.encode())
                    w.put(role, 'sub/deep.txt', b'deep ' + role.encode())
                w.finish_role(role, 0 if reasons[role] == 'ok' else 143 if reasons[role] == 'signalled' else 1, reasons[role])
        w.at(done_at, done)
    return w


def idx(calls, prefix, start=0):
    for i in range(start, len(calls)):
        if calls[i][:len(prefix)] == prefix:
            return i
    return -1


# ============================================================ configure refuses what it cannot run with
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    for what, argv, env, euid in (
            ('another uid', None, None, 1000), ('no arguments', [], None, 30034), ('only --id', ['--id', W.OPID], None, 30034),
            ('a bad op id', ['--id', 'Op', '--source-identity', W.IDENT], None, 30034), ('a short op id', ['--id', 'a', '--source-identity', W.IDENT], None, 30034),
            ('a bad source identity', ['--id', W.OPID, '--source-identity', 'zz'], None, 30034), ('an upper-case source identity', ['--id', W.OPID, '--source-identity', 'AB' * 32], None, 30034),
            ('arguments in the wrong order', ['--source-identity', W.IDENT, '--id', W.OPID], None, 30034), ('an extra argument', ['--id', W.OPID, '--source-identity', W.IDENT, 'x'], None, 30034),
            ('no nonce', None, {'FSGG_PG_IMAGE': W.PGIMG}, 30034), ('a short nonce', None, {'FSGG_NONCE': 'cd' * 8, 'FSGG_PG_IMAGE': W.PGIMG}, 30034),
            ('an upper-case nonce', None, {'FSGG_NONCE': 'CD' * 16, 'FSGG_PG_IMAGE': W.PGIMG}, 30034),
            ('no pg image', None, {'FSGG_NONCE': W.NONCE}, 30034), ('a short pg image', None, {'FSGG_NONCE': W.NONCE, 'FSGG_PG_IMAGE': 'ee' * 8}, 30034)):
        r = go(w, argv=argv, env=env, euid=euid)
        check(r.code not in (0, None) and isinstance(r.code, str) and not os.path.exists(r.cust) and w.calls == [], f'refused before anything is created or run: {what}')

# ============================================================ a good run
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    r = go(w)
    rec = r.rec
    check(r.code == 0 and rec and rec['schema'] == 'learn-op-pod-success/1', 'every role done, pg shut down, retirement clean: learn-op-pod-success/1, exit 0')
    check(rec['opId'] == W.OPID and rec['inputRead'] is False and rec['containersStartedWithInput'] == [], 'the record carries opId, inputRead false and an EMPTY containersStartedWithInput list (what a void needs)')
    check(rec['runNonceSha256'] == sha(W.NONCE.encode()) and rec['finalizerSha256'] == FIN_SHA, 'the record binds the run nonce and the finalizer\'s own sha256')
    check(rec['trigger'] == 'done' and rec['oom'] == 'no' and rec['deadline'] is False and rec['custodyLate'] is False, 'trigger done, no OOM, no deadline, custody on time')
    for role in W.ROLES:
        ro = rec['roles'][role]
        check(ro['custody']['verdict'] == 'accepted' and ro['status']['status'] == 'valid' and ro['outcomeOk'] and ro['identity']['identOk'], f'{role}: accepted archive, valid status, identity ok')
        check(ro['pauseRc'] == 0 and ro['unpauseRc'] == 0, f'{role}: paused and unpaused')
        names = sorted(e['name'] for e in ro['custody']['extracted'])
        check(names == ['.done', '.status', 'out.json', 'sub/deep.txt'], f'{role}: the extracted files are the role\'s /out/{role}')
        check(open(os.path.join(r.evid, 'out', role, 'out.json'), 'rb').read() == b'{"role":"%s"}' % role.encode() and os.path.exists(os.path.join(r.evid, 'out', role, 'sub', 'deep.txt')),
              f'{role}: the evidence tree holds the role\'s files, byte for byte')
    check(rec['roles']['feed']['status']['value'] == {'exit': 0, 'reason': 'ok', 'detail': 'rc=0'} and rec['roles']['host']['status']['value']['reason'] == 'signalled', 'the statuses are read from each role\'s .status')
    check(rec['roles']['host']['enospc'] == 'no' and rec['roles']['feed']['enospc'] == 'n/a', 'enospc is checked for the roles that write the volume only')
    check(sorted(os.listdir(os.path.join(r.cust, 'final'))) == sorted([f'{x}.err' for x in W.ROLES] + [f'extract-{x}' for x in W.ROLES]), 'the streams are removed from custody; the extractions and tar error files remain')
    check(oct(os.stat(r.cust).st_mode & 0o777) == '0o700' and oct(os.stat(r.ck).st_mode & 0o777) == '0o700', 'the custody and checkpoint directories are 0700')
    pg = rec['pg']
    check(pg['ok'] and pg['stop']['imageMatchesPin'] and pg['stop']['exitCode'] == 0 and pg['stop']['oomKilled'] is False and pg['stop']['killRc'] == 0, 'pg: the pinned image, SIGINT sent, exit 0, no OOM')
    check(pg['controldata']['clusterState'] == 'shut down' and pg['controldata']['latestCheckpoint'] == '0/1A2B3C' and pg['controldata']['timeline'] == '1'
          and pg['controldata']['outputSha256'] == sha(w.controldata[0].encode()), 'pg_controldata: cluster state shut down, checkpoint and timeline, and the output hash')
    kill = [c for c in w.calls if c[:2] == ['podman', 'kill']]
    check(kill == [['podman', 'kill', '--signal', 'INT', 'learn-pod-op-pod-1-pg']], 'pg gets SIGINT (fast shutdown), not TERM, and nothing else is killed')
    runs = [c for c in w.calls if c[:2] == ['podman', 'run']]
    check(len(runs) == 1 and runs[0][-2:] == ['pg_controldata', '/pgdata/data'], 'exactly one container is run: pg_controldata /pgdata/data')
    ra = runs[0]
    check(all(x in ra for x in ('--rm', '--pull=never', '--network=none', '--cap-drop=all', '--security-opt=no-new-privileges', '--read-only')) and '999:999' in ra
          and f'seccomp={F.SECCOMP}' in ra and ra[ra.index('-v') + 1] == f'{w.volroot}/{W.OPID}.mnt/pgdata:/pgdata:ro' and ra.count(W.PGIMG) == 1,
          'the controldata container: no network, no caps, read-only root, the admitted seccomp, user 999, /pgdata bound read-only, the pinned pg image Id, no pull')
    i_kill, i_stop, i_rm, i_run, i_ps = idx(w.calls, ['podman', 'kill']), idx(w.calls, ['podman', 'pod', 'stop']), idx(w.calls, ['podman', 'pod', 'rm']), idx(w.calls, ['podman', 'run']), idx(w.calls, ['podman', 'pod', 'ps'])
    check(0 < i_kill < i_stop < i_rm < i_run < i_ps, 'order: pg SIGINT, pod stop, pod rm, pg_controldata, then the retirement checks')
    for role in W.ROLES:
        c = f'learn-pod-op-pod-1-{role}'
        ip, iu = idx(w.calls, ['podman', 'pause', c]), idx(w.calls, ['podman', 'unpause', c])
        check(0 < ip < iu < i_kill, f'{role} is paused, archived and unpaused before pg is stopped')
    check(not any(c[:2] in (['podman', 'exec'], ['podman', 'create'], ['podman', 'start']) for c in w.calls), 'no exec, create or start')
    ret = rec['retirement']
    check(all(ret[k] for k in ('podGone', 'containersGone', 'podCgroupRetired', 'podCgroupProcsEmpty', 'unitsClean', 'secretsGone', 'credentialTmpfsGone', 'volumeOk')), 'every retirement check passes')
    check(ret['snapshot']['ok'] and ret['snapshot']['filesChecked'] == 3 and ret['slice']['returned'] and ret['volume']['mountPoint'] and ret['volume']['withinCap'], 'the snapshot, slice and volume checks pass')
    check(rec['snapshotStart']['files'] == 3 and rec['snapshotStart']['snapshotId'] == 'snap-1', 'the claim and sealed manifest were read at the start')
    # the hash chain
    lines = open(os.path.join(r.cust, 'chain.jsonl')).read().splitlines()
    head, okc = '0' * 64, True
    for l in lines:
        okc = okc and json.loads(l)['prev'] == head
        head = sha(l.encode())
    check(okc and head == rec['chain']['head'] and rec['chain']['lines'] == len(lines), 'chain.jsonl is a hash chain and the record carries its head')
    check([json.loads(l)['event'] for l in lines][:2] == ['start', 'anchor'] and 'trigger' in [json.loads(l)['event'] for l in lines], 'the chain logs start, anchor and the trigger')
    check(r.out.count('RECORD schema=') == 1 and f'FSGG {W.NONCE} FIN trigger=done' in r.out, 'markers carry the nonce; one RECORD marker')
    # a second run for the same op id cannot overwrite anything
    before = open(os.path.join(r.evid, 'record.json'), 'rb').read()
    r2 = go(w)
    check(r2.code == 70 and open(os.path.join(r.evid, 'record.json'), 'rb').read() == before and 'RECORD schema=' not in r2.out, 'a second run for the same op id exits 70 and writes nothing: the first record is untouched')
for what, make in (('custody', lambda w: os.makedirs(os.path.join(w.home, '.local/state/learn-custody', W.OPID))), ('evidence', lambda w: os.makedirs(os.path.join(w.home, '.local/state/learn-evidence', W.OPID))),
                   ('checkpoint', lambda w: os.makedirs(os.path.join(w.home, '.local/state/learn-checkpoint', W.OPID)))):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        make(w)
        r = go(w)
        check(r.code == 70 and w.calls == [] and 'RECORD' not in r.out, f'an existing {what} directory for the op id: exit 70 before anything is run')

# ============================================================ the pod never becomes active, or ends before the roles are done
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, activate_at=None, done_at=None)
    r = go(w)
    check(r.code == 0 and r.rec['schema'] == 'learn-op-pod-failed-to-start/1' and r.rec['retirement']['earlyPath'] == 'failed-to-start', 'a pod unit that never becomes active ends failed-to-start, retired')
    check(r.rec['containersStartedWithInput'] == [] and r.rec['inputRead'] is False and 'roles' not in r.rec, 'the early path still states the input evidence, and archives nothing')
    check(idx(w.calls, ['podman', 'pod', 'rm']) > 0, 'the leftover pod is removed')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, activate_at=None, done_at=None)
    w.fail.add('pod_rm')
    r = go(w)
    check(r.rec['schema'] == 'learn-op-retirement-failure/1' and not r.rec['retirement']['podGone'], 'a pod that cannot be removed after a failed start is a retirement failure')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, activate_at=40, done_at=None)
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-failed-to-start/1', 'a pod unit that becomes active only after the 30 s anchor wait is failed-to-start')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)

    def die():
        w.settle()
    w.at(10, die)
    r = go(w)
    check(r.code == 0 and r.rec['schema'] == 'learn-op-pod-unrecoverable/1' and r.rec['retirement']['earlyPath'] == 'ended-without-done', 'a pod unit that ends before every role is done is unrecoverable, retired')
    check('unitStateUnknown' not in r.rec, 'a unit observed ended is not recorded as an unknown-state conclusion')
    check(r.rec['containersStartedWithInput'] == [] and r.rec['inputRead'] is False, 'the containers gone, the census succeeded and the input-read marker absent: the list is EMPTY (the marker is created before the first mount)')

# ============================================================ soft deadline and the volume budget
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    r = go(w, patch={'SOFT_S': 30})
    check(r.rec['trigger'] == 'time' and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'the soft deadline triggers; roles without .done make it killed-retired')
    check(all(r.rec['roles'][x]['status'] == {'status': 'absent'} and not r.rec['roles'][x]['outcomeOk'] for x in W.ROLES), 'a role without .done has status absent and is never ok')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)

    def fill():
        w.vol_used = 0.95
    w.at(12, fill)
    r = go(w)
    check(r.rec['trigger'] == 'budget' and r.rec['schema'] == 'learn-op-pod-killed-retired/1' and r.rec['retirement']['volume']['budgetHit'], 'a volume at 95% triggers `budget` and ends killed-retired, never success')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def fill2():
        w.vol_used = 0.89
    w.at(5, fill2)
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and not r.rec['retirement']['volume']['budgetHit'], 'a volume at 89% is under the budget')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.vol_used = 0.90
    r = go(w)
    check(r.rec['trigger'] == 'budget', 'exactly 90% used triggers the budget')

# ============================================================ statuses
STATUS_CASES = [
    ('host', dict(reason='signalled', exit_=143), True, 'host stopped by TERM'), ('host', dict(reason='ok'), True, 'host ok'), ('recv', dict(reason='signalled', exit_=143), True, 'recv stopped by TERM'),
    ('feed', dict(reason='ok'), True, 'feed ok'), ('feed', dict(reason='signalled', exit_=143), False, 'feed signalled'), ('feed', dict(reason='error', exit_=3), False, 'feed error'),
    ('host', dict(reason='error', exit_=3), False, 'host error'), ('host', dict(reason='gate-refused', exit_=64), False, 'host gate-refused'), ('recv', dict(reason='enospc', exit_=5), False, 'recv enospc'),
    ('host', dict(reason='weird'), 'malformed', 'an unknown reason'), ('host', dict(schema='learn-op-status/1'), 'malformed', 'the reader schema'),
    ('host', dict(role_field='recv'), 'malformed', 'another role\'s status'), ('host', dict(phase='ready'), 'malformed', 'phase ready with .done'), ('host', dict(phase='starting'), 'malformed', 'phase starting with .done'),
]
for role, kw, want, what in STATUS_CASES:
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp, done_at=None)

        def done(role=role, kw=kw):
            for x in W.ROLES:
                params = {'exit_': 0, 'reason': 'signalled' if x != 'feed' else 'ok'}
                if x == role:
                    params.update(kw)
                w.finish_role(x, **params)
        w.at(15, done)
        r = go(w)
        st = r.rec['roles'][role]['status']['status']
        if want is True:
            check(r.rec['schema'] == 'learn-op-pod-success/1' and st == 'valid', f'status {what}: valid and the run succeeds')
        elif want is False:
            check(st == 'valid' and not r.rec['roles'][role]['outcomeOk'] and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'status {what}: valid, but not an acceptable end for {role}: killed-retired')
        else:
            check(st == 'malformed' and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'status {what}: malformed, killed-retired')
for what, raw in (('invalid JSON', b'{"x"'.ljust(4096, b' ')), ('a status of the wrong size', b'{"schema":"learn-pod-status/1"}'), ('a non-UTF-8 status', b'\xff\xfe'.ljust(4096, b' ')),
                  ('a JSON list', b'[1]'.ljust(4096, b' ')), ('exit as a boolean', json.dumps({'schema': 'learn-pod-status/1', 'role': 'host', 'phase': 'terminal', 'exit': True, 'reason': 'ok'}).encode().ljust(4096, b' ')),
                  ('exit as a string', json.dumps({'schema': 'learn-pod-status/1', 'role': 'host', 'phase': 'terminal', 'exit': '0', 'reason': 'ok'}).encode().ljust(4096, b' '))):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)

        def bad(raw=raw):
            open(os.path.join(w.containers['learn-pod-op-pod-1-host']['out'], '.status'), 'wb').write(raw)
        w.at(25, bad)
        w.events[-1][0] = 21
        r = go(w)
        check(r.rec['roles']['host']['status']['status'] == 'malformed' and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'a {what} is malformed: killed-retired')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def nostatus():
        os.unlink(os.path.join(w.containers['learn-pod-op-pod-1-host']['out'], '.status'))
    w.at(21, nostatus)
    r = go(w)
    check(r.rec['roles']['host']['status'] == {'status': 'malformed', 'reason': 'missing'} and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a .done without a .status is malformed (missing)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    w.vol_used = 0.60
    w.at(12, lambda: [w.finish_role(x, 5 if x == 'recv' else 0, 'enospc' if x == 'recv' else 'ok') for x in W.ROLES])
    w.vol.f_blocks = 262144

    def low():
        w.vol_used = 1.0
    w.at(11, low)
    r = go(w)
    check(r.rec['roles']['recv']['enospc'] in ('yes', 'unknown') and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a full volume is never a success')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    w.at(12, lambda: [w.finish_role(x, 5 if x == 'recv' else 0, 'enospc' if x == 'recv' else 'ok') for x in W.ROLES])
    r = go(w)
    check(r.rec['roles']['recv']['enospc'] == 'inconsistent', 'a role that claims enospc while the volume has space is flagged inconsistent')

# ============================================================ the archive and its verdicts
def role_case(setup, patch=None, role='host', what='', verdict=None, status=None, schema='learn-op-pod-killed-retired/1', extra=None):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp, done_at=None)

        def done():
            for x in W.ROLES:
                w.finish_role(x, 0, 'ok')
            setup(w, w.containers[f'learn-pod-op-pod-1-{role}']['out'])
        w.at(15, done)
        r = go(w, patch=patch)
        ro = r.rec['roles'][role]
        if verdict:
            check(ro['custody']['verdict'] == verdict, f'{what}: verdict {verdict} (got {ro["custody"].get("verdict")})')
        if status:
            check(ro['status']['status'] == status, f'{what}: status {status} (got {ro["status"]})')
        check(r.rec['schema'] == schema, f'{what}: {schema}')
        if extra:
            extra(r, w, ro)
        return r


def mk_symlink(w, out):
    os.symlink('/etc/passwd', os.path.join(out, 'link'))


def mk_fifo(w, out):
    os.mkfifo(os.path.join(out, 'fifo'))


def mk_deep(w, out):
    d = out
    for i in range(10):
        d = os.path.join(d, f'd{i}')
    os.makedirs(d)
    open(os.path.join(d, 'f'), 'w').write('x')


def mk_unreadable(w, out):
    p = os.path.join(out, 'secret')
    open(p, 'w').write('x')
    os.chmod(p, 0o000)


role_case(mk_symlink, what='a symlink in /out', verdict='rejected:type', status='valid')
role_case(mk_fifo, what='a FIFO in /out', verdict='rejected:type')
role_case(mk_deep, what='a tree deeper than 8', verdict='rejected:type')
if os.geteuid() != 0:
    role_case(mk_unreadable, what='a file tar cannot read', verdict='rejected:tar')
role_case(lambda w, out: None, what='a normal /out', verdict='accepted', schema='learn-op-pod-success/1')
# the shared budget: members and content, across roles
role_case(lambda w, out: [open(os.path.join(out, f'f{i}'), 'w').write('x') for i in range(14)], patch={'MAX_MEMBERS': 12}, role='host', what='the member cap reached by the first role', verdict='rejected:count')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)

    def d2():
        for x in W.ROLES:
            w.finish_role(x, 0, 'ok')
            w.put(x, 'a', b'1'); w.put(x, 'b', b'2'); w.put(x, 'c', b'3')
    w.at(15, d2)
    r = go(w, patch={'MAX_MEMBERS': 7})
    v = {x: r.rec['roles'][x]['custody'].get('verdict') for x in W.ROLES}
    check(v == {'host': 'accepted', 'recv': 'rejected:count', 'feed': 'rejected:count'} and r.rec['schema'] == 'learn-op-pod-killed-retired/1',
          'the member budget is shared: host (5 members) leaves 2 of 7, so recv and feed (5 each) are rejected:count')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)

    def d3():
        for x in W.ROLES:
            w.finish_role(x, 0, 'ok')
            w.put(x, 'big', b'x' * 3000)
    w.at(15, d3)
    r = go(w, patch={'CONTENT_CAP': 16000})
    v = {x: r.rec['roles'][x]['custody'].get('verdict') for x in W.ROLES}
    check(v['host'] == 'accepted' and v['recv'] == 'accepted' and v['feed'] == 'rejected:size', 'the content budget is shared: each role is 7096 B (3000 + the 4096 B status), so host and recv fit in 16000 and feed does not')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    w.at(15, lambda: [(w.finish_role(x, 0, 'ok'), w.put(x, 'big', b'x' * 200000)) for x in W.ROLES])
    r = go(w, patch={'CONTENT_CAP': 1000, 'MAX_MEMBERS': 2})
    ro = r.rec['roles']['host']
    check(ro['custody']['verdict'] == 'rejected:budget' and ro['status'] == {'status': 'unavailable'} and ro['enospc'] == 'unknown', 'a stream over the cap is rejected:budget, its status is unavailable, never read')
    check(ro['archive']['overCap'] and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'the archive records that the cap was exceeded')
# identity
for what, hook, key in (('a cgroup that is not frozen', lambda c: open(os.path.join(c['cgdir'], 'cgroup.events'), 'w').write('populated 1\nfrozen 0\n'), 'identOk'),):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.pause_hook = lambda c, hook=hook: hook(c) if c['role'] == 'host' else None
        r = go(w)
        check(not r.rec['roles']['host']['identity']['identOk'] and r.rec['roles']['host']['custody']['verdict'] == 'rejected:identity' and r.rec['roles']['host']['status'] == {'status': 'unavailable'},
              f'{what}: identity not ok, rejected:identity, status unavailable')
        check(r.rec['schema'] == 'learn-op-pod-killed-retired/1' and r.rec['roles']['recv']['identity']['identOk'], f'{what}: only that role is affected, the run is killed-retired')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.fail.add('pause')
    r = go(w)
    check(r.rec['roles']['host']['pauseRc'] == 1 and r.rec['roles']['host']['custody']['verdict'] == 'rejected:identity', 'a container that cannot be paused cannot be archived (state is not paused)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    real_archive = F.archive

    def swap_start(*a, **k):
        res = real_archive(*a, **k)
        c = w.containers['learn-pod-op-pod-1-recv']
        c['start_time'] += 1
        w.write_stat(c)
        return res
    r = go(w, patch={'archive': swap_start})
    check(r.rec['roles']['host']['identity']['identOk'] is False or r.rec['roles']['recv']['identity']['identOk'] is False, 'a process whose start time changes during the archive fails the identity check')
    check(r.rec['roles']['recv']['custody']['verdict'] == 'rejected:identity', 'the container whose start time changed is rejected:identity')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(21, lambda: w.containers.pop('learn-pod-op-pod-1-feed'))
    r = go(w)
    check(r.rec['roles']['feed']['archive'] == {'skipped': 'no running container'} and r.rec['roles']['feed']['custody']['verdict'] == 'rejected:identity' and r.rec['schema'] == 'learn-op-pod-killed-retired/1',
          'a role whose container is gone cannot be archived: rejected:identity, killed-retired')

# ============================================================ checkpoints
def request(w, n, body=None, name=None):
    def fn():
        p = os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID, name or f'req-{n}')
        data = body if body is not None else json.dumps({'n': n, 'label': f'before-kill-{n}'}).encode()
        open(p, 'wb').write(data)
    return fn


with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    for role in W.ROLES:
        w.put(role, 'early.json', b'{"early":"%s"}' % role.encode())
    w.at(6, request(w, 1))
    w.at(12, lambda: [(w.put(x, 'late.json', b'{"late":1}'), w.finish_role(x, 0, 'ok')) for x in W.ROLES])
    r = go(w)
    ack = json.load(open(os.path.join(r.ck, 'ack-1')))
    check(r.rec['schema'] == 'learn-op-pod-success/1' and len(r.rec['checkpoints']) == 1, 'one checkpoint is served and the run still succeeds')
    check(ack['n'] == 1 and ack['ok'] is True and ack['requestSha256'] == r.rec['checkpoints'][0]['requestSha256'] and set(ack['roles']) == set(W.ROLES) and set(ack['roles'].values()) == {'accepted'},
          'ack-1 names the request hash and the verdict of every role')
    check(oct(os.stat(os.path.join(r.ck, 'ack-1')).st_mode & 0o777) == '0o600', 'the acknowledgement is 0600')
    check(os.path.exists(os.path.join(r.cust, 'checkpoints', '1', 'extract-host', 'early.json')) and not os.path.exists(os.path.join(r.cust, 'checkpoints', '1', 'extract-host', 'late.json')),
          'the checkpoint archive holds the role as it was when asked (early.json, not late.json)')
    check(open(os.path.join(r.evid, 'checkpoints', '1', 'host', 'early.json'), 'rb').read() == b'{"early":"host"}' and os.path.exists(os.path.join(r.evid, 'out', 'host', 'late.json')),
          'the evidence tree holds the checkpoint under checkpoints/1/<role>/ and the final state under out/<role>/')
    check('FIN checkpoint=1' in r.out, 'the checkpoint is marked')
    cp = [json.loads(l) for l in open(os.path.join(r.cust, 'chain.jsonl')).read().splitlines()]
    ev = [e['event'] for e in cp]
    check(ev.index('checkpoint-request') < ev.index('checkpoint-ack') < ev.index('trigger'), 'the chain logs request, then ack, then the trigger')
    check(not any(f.endswith('.tar') for _, _, fs in os.walk(r.cust) for f in fs), 'no stream is left in custody')
for what, body, name, ok_ in (('invalid JSON', b'{"n":', None, False), ('another n', json.dumps({'n': 2, 'label': 'x'}).encode(), None, False), ('a bad label', json.dumps({'n': 1, 'label': 'Bad Label!'}).encode(), None, False),
                              ('no label', json.dumps({'n': 1}).encode(), None, False), ('over 512 bytes', json.dumps({'n': 1, 'label': 'a' * 600}).encode(), None, False), ('a list', b'[1]', None, False)):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.at(6, request(w, 1, body=body))
        r = go(w)
        ack = json.load(open(os.path.join(r.ck, 'ack-1')))
        check(ack['ok'] is False and ack['roles'] == {} and r.rec['checkpoints'][0].get('error') and r.rec['schema'] == 'learn-op-pod-killed-retired/1',
              f'a checkpoint request with {what} is acknowledged as not ok, archives nothing, and the run cannot succeed')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def symreq():
        p = os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID, 'req-1')
        os.symlink('/etc/hostname', p)
    w.at(6, symreq)
    r = go(w)
    check(json.load(open(os.path.join(r.ck, 'ack-1')))['ok'] is False, 'a request that is a symlink is not followed')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 3))
    r = go(w)
    check(not os.path.exists(os.path.join(r.ck, 'ack-3')) and r.rec['checkpoints'] == [] and r.rec['schema'] == 'learn-op-pod-success/1', 'req-3 without req-1 and req-2 is ignored (consecutive numbers only)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    for i, t in ((1, 6), (2, 8), (3, 10)):
        w.at(t, request(w, i))
    r = go(w, patch={'MAX_CHECKPOINTS': 2})
    check(sorted(f for f in os.listdir(r.ck) if f.startswith('ack-')) == ['ack-1', 'ack-2'] and len(r.rec['checkpoints']) == 2, 'at most MAX_CHECKPOINTS requests are served')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1, name='req-01'))
    w.at(7, request(w, 1, name='request-1'))
    r = go(w)
    check(not any(f.startswith('ack-') for f in os.listdir(r.ck)), 'files that are not req-<n> are ignored')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1))
    w.at(8, request(w, 2))
    r = go(w)
    check(sorted(f for f in os.listdir(r.ck) if f.startswith('ack-')) == ['ack-1', 'ack-2'] and [c['n'] for c in r.rec['checkpoints']] == [1, 2], 'consecutive requests are acknowledged in order')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1))
    w.at(5, lambda: open(os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID, 'ack-1'), 'w').write('planted'))
    r = go(w)
    check(r.rec['schema'] == 'learn-op-retirement-failure/1' and 'FileExistsError' in r.rec['error'], 'a pre-existing acknowledgement is never overwritten: the run fails closed into a retirement failure')
# the checkpoint shares the budget
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    for role in W.ROLES:
        w.put(role, 'a', b'1')
    w.at(6, request(w, 1))
    r = go(w, patch={'MAX_MEMBERS': 8})
    c1 = r.rec['checkpoints'][0]['roles']
    fin = {x: r.rec['roles'][x]['custody'].get('verdict') for x in W.ROLES}
    check(all(c1[x]['custody']['verdict'] == 'accepted' for x in W.ROLES) and 'rejected:count' in fin.values(), 'checkpoint archives are charged to the same member budget as the final archives')

# ============================================================ pg
for what, mut, key in (('a pg container whose image is not the pin', lambda w: w.containers['learn-pod-op-pod-1-pg'].update(image='11' * 32), 'imageMatchesPin'),
                       ('a pg that exits 137', lambda w: w.fail.add('pg_exit'), 'exitCode'), ('a pg killed by the OOM killer', lambda w: w.containers['learn-pod-op-pod-1-pg'].update(oom=True), 'oomKilled')):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        mut(w)
        r = go(w)
        check(r.rec['pg']['ok'] is False and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'{what}: pg not ok, killed-retired')
for what, cd, rc in (('a cluster still in production', ('Database cluster state:               in production\n', 0), 0), ('an empty control file read', ('', 0), 0),
                     ('a failing pg_controldata', ('Database cluster state:               shut down\n', 0), 1), ('shut down in recovery', ('Database cluster state:               shut down in recovery\n', 0), 0)):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.controldata, w.pgcd_rc = cd, rc
        r = go(w)
        check(r.rec['pg']['ok'] is False and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'{what}: pg not ok, killed-retired')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.fail.add('kill')
    r = go(w)
    check(r.rec['pg']['stop']['killRc'] == 1 and r.rec['pg']['ok'] is False and r.rec['pg']['stop']['exitCode'] is None, 'a SIGINT that cannot be delivered leaves pg without an exit code and not ok (the pod is stopped regardless)')

# ============================================================ what the pod did with the input
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    os.makedirs(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID))
    open(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID, 'input-read'), 'w').close()
    r = go(w)
    check(r.rec['inputRead'] is True and r.rec['containersStartedWithInput'] == [], 'the input-read marker makes inputRead true')
for what, mounts, started, want in (('a started container with /in', [{'Destination': '/in', 'Source': '/x'}, {'Destination': '/work'}], '2026-10-31T08:00:00Z', ['learn-pod-op-pod-1-feed']),
                                    ('a container with /in that never started', [{'Destination': '/in'}], '0001-01-01T00:00:00Z', []),
                                    ('a container with /input (not /in)', [{'Destination': '/input'}], '2026-10-31T08:00:00Z', []), ('a container with no mounts', None, '2026-10-31T08:00:00Z', [])):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.containers['learn-pod-op-pod-1-feed']['mounts'] = mounts
        w.containers['learn-pod-op-pod-1-feed']['started'] = started
        r = go(w)
        check(r.rec['containersStartedWithInput'] == want, f'{what}: {want}')
        check((r.rec['schema'] == 'learn-op-pod-success/1') == True, f'{what}: the run is not otherwise affected')
for what, fail in (('a failing container inspect', 'inspect'), ('a failing container list', 'ps')):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.fail.add(fail)
        r = go(w)
        check(r.rec['containersStartedWithInput'] is None, f'{what}: null, not an empty list')
        if fail == 'inspect':
            check(r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'{what}: a run whose input evidence is unknown is not a success')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    orig = w.run

    def badjson(argv, timeout=60, cap=1 << 20):
        if argv[:2] == ['podman', 'inspect'] and 'Mounts' in argv[3]:
            return 0, 'not json|2026-10-31T08:00:00Z\n', ''
        return orig(argv, timeout, cap)
    r = go(w, patch={'run': badjson})
    check(r.rec['containersStartedWithInput'] is None, 'unparsable mounts: null')

# ============================================================ markers and the unit result
for what, mut, oom, schema in (('an OOM kill in the pod', lambda w: setattr(w, 'memev', 'MEMEV oom_kill=2'), 'yes', 'learn-op-pod-killed-retired/1'),
                               ('no MEMEV marker', lambda w: setattr(w, 'memev', None), 'unknown', 'learn-op-pod-killed-retired/1')):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        mut(w)
        r = go(w)
        check(r.rec['oom'] == oom and r.rec['schema'] == schema, f'{what}: oom {oom}, killed-retired')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.unit_result = 'timeout'
    r = go(w)
    check(r.rec['deadline'] is True and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a pod unit that hit RuntimeMaxSec is killed-retired')

# ============================================================ retirement
def retire_case(mut, key, what, patch=None, schema='learn-op-retirement-failure/1', expect=False, setup=None):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        mut(w)
        r = go(w, patch=patch)
        block = r.rec['retirement']
        check(r.rec['schema'] == schema, f'{what}: {schema} (got {r.rec["schema"]})')
        if key:
            val = block
            for k in key.split('.'):
                val = val[k]
            check(val == expect, f'{what}: {key} is {expect}')
        return r


retire_case(lambda w: w.fail.add('pod_rm'), 'podGone', 'a pod that survives its removal', expect=False)
retire_case(lambda w: setattr(w, 'leave_pgcd', True), 'containersGone', 'a controldata container left behind', expect=False)
retire_case(lambda w: w.fail.add('pod_ps'), 'podGone', 'a failing pod listing (fail closed)', expect=False)
retire_case(lambda w: w.fail.add('secret'), 'secretsGone', 'a failing secret listing (fail closed)', expect=False)
retire_case(lambda w: w.secrets.append('learn-pod-op-pod-1-cred'), 'secretsGone', 'a pod secret left behind', expect=False)
retire_case(lambda w: w.secrets.append('learn-pod-other-op-cred'), 'secretsGone', 'another op\'s secret is not this op\'s concern', expect=True, schema='learn-op-pod-success/1')
retire_case(lambda w: os.makedirs(os.path.join(w.cred, W.OPID)), 'credentialTmpfsGone', 'a credential tmpfs left behind', expect=False)
retire_case(lambda w: w.fail.add('cg_populated'), 'podCgroupRetired', 'a pod cgroup still populated', expect=False)
retire_case(lambda w: w.fail.add('cg_populated'), 'podCgroupProcsEmpty', 'a pod cgroup with processes', expect=False)
retire_case(lambda w: w.fail.add('cg_populated'), 'volumeOk', 'a process in the pod cgroup means an open handle on the volume', expect=False)
r_ = retire_case(lambda w: w.fail.add('unit_failed'), 'unitsClean', 'a pod unit that the stop left failed is clean once reset-failed has cleared it', expect=True, schema='learn-op-pod-success/1')
check(r_.rec['retirement']['unitStatesBeforeReset'] == {'learn-pod-op-pod-1.service': 'failed'} and r_.rec['retirement']['unitStates'] == {'learn-pod-op-pod-1.service': 'inactive'}, 'the failed state before the reset is recorded, and the unit reads inactive after it')
retire_case(lambda w: w.fail.update({'unit_failed', 'reset_stuck'}), 'unitsClean', 'a pod unit that STAYS failed after reset-failed (the next op\'s sweep would refuse it)', expect=False)
retire_case(lambda w: w.extra_units.append(f'learn-pod-{W.OPID}-x.service loaded failed failed X'), 'unitsClean', 'another failed unit of this op is cleared by the reset', expect=True, schema='learn-op-pod-success/1')
retire_case(lambda w: (w.extra_units.append(f'learn-pod-{W.OPID}-x.service loaded failed failed X'), w.fail.add('reset_stuck')), 'unitsClean', 'another failed unit that the reset does not clear', expect=False)
retire_case(lambda w: w.fail.add('units'), 'unitsClean', 'a failing unit census', expect=False)
retire_case(lambda w: w.fail.add('units_before_reset'), 'unitsClean', 'a unit census that fails only BEFORE the reset', expect=False)
retire_case(lambda w: w.fail.add('units_after_reset'), 'unitsClean', 'a unit census that fails only AFTER the reset (the state to trust)', expect=False)
retire_case(lambda w: w.extra_units.append(f'learn-pod-{W.OPID}-x.service loaded activating start X'), 'unitsClean', 'another unit of this op still activating', expect=False)
retire_case(lambda w: w.extra_units.append(f'learn-fin-{W.OPID}-y.service loaded deactivating stop Y'), 'unitsClean', 'a finalizer-side unit of this op still deactivating', expect=False)
retire_case(lambda w: w.extra_units.append('learn-pod-other.service loaded active running X'), 'unitsClean', 'another op\'s active unit is not this op\'s concern', expect=True, schema='learn-op-pod-success/1')
retire_case(lambda w: setattr(w, 'mounted', False), 'volume.mountPoint', 'a volume that is not a mount point', expect=False)
retire_case(lambda w: setattr(w.vol, 'f_blocks', (2 << 30) // 4096), 'volume.withinCap', 'a volume larger than 1 GiB (the host filesystem, not the loop image)', expect=False)
retire_case(lambda w: w.fail.add('statvfs'), 'volume.sampled', 'a volume that cannot be sampled at all', expect=False)
retire_case(lambda w: w.fail.add('slice_stuck'), 'slice.returned', 'a slice whose memory and pids do not return', expect=False)
retire_case(lambda w: None, 'slice.returned', 'a slice back at its starting values', expect=True, schema='learn-op-pod-success/1')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    r = go(w, patch={'CUSTODY_DEADLINE_S': 5})
    check(r.rec['schema'] == 'learn-op-retirement-failure/1' and r.rec['custodyLate'] is True and 'late' in r.rec['custodyNote'], 'custody finished after the deadline is a retirement failure, flagged partial/late')


# --- the sealed snapshot and the claim
def snap_case(mut, what, field=None, want=False, ok=False):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)

        def doit():
            mut(w)
        w.at(10, doit)
        r = go(w)
        s = r.rec['retirement']['snapshot']
        check(s['ok'] is ok, f'{what}: snapshot ok is {ok}')
        check(r.rec['schema'] == ('learn-op-pod-success/1' if ok else 'learn-op-retirement-failure/1'), f'{what}: {"success" if ok else "retirement failure"}')
        if field:
            check(s.get(field) == want, f'{what}: {field} is {want}')
        return r


snap_case(lambda w: None, 'an untouched snapshot', ok=True)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.make_snapshot(src_modes={'passive-a/spool/one.json': '0o600'})
    r = go(w)
    check(r.rec['retirement']['snapshot']['ok'] and r.rec['schema'] == 'learn-op-pod-success/1', 'a source file of mode 0600 is sealed as 0644; the manifest records the SOURCE mode, and the check expects the sealed 0644')
snap_case(lambda w: open(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json'), 'wb').write(b'{"a":2}'), 'a sealed file with other content of the same size', field='changed', want=['passive-a/spool/one.json'])
snap_case(lambda w: open(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json'), 'ab').write(b' '), 'a sealed file that grew', field='changed', want=['passive-a/spool/one.json'])
snap_case(lambda w: open(os.path.join(w.view, 'inputs', 'snap-1', 'new.json'), 'w').write('x'), 'an extra file in the snapshot', field='extra', want=['new.json'])
snap_case(lambda w: os.symlink('/etc/passwd', os.path.join(w.view, 'inputs', 'snap-1', 'link')), 'a symlink in the snapshot', field='extra', want=['link'])
snap_case(lambda w: os.utime(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/two.json'), ns=(5, 5)), 'a changed mtime (the original clocks)', field='changed', want=['passive-a/spool/two.json'])
snap_case(lambda w: os.chmod(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/two.json'), 0o640), 'a changed mode', field='changed', want=['passive-a/spool/two.json'])
snap_case(lambda w: os.unlink(os.path.join(w.view, 'inputs', 'snap-1', 'passive-b/spool/producer.lock')), 'a missing sealed file', field='changed', want=['passive-b/spool/producer.lock'])
snap_case(lambda w: (os.unlink(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json')), os.symlink('/etc/hostname', os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json'))),
          'a sealed file replaced by a symlink', field='changed', want=['passive-a/spool/one.json'])
snap_case(lambda w: os.chmod(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json'), 0o666), 'a group/other-writable sealed file', field='changed', want=['passive-a/spool/one.json'])
snap_case(lambda w: (os.unlink(w.claim_path), open(w.claim_path, 'wb').write(b'{}')), 'a claim replaced by another document', field='ok', want=False)
snap_case(lambda w: os.unlink(w.claim_path), 'a claim that disappeared', field='ok', want=False)
snap_case(lambda w: open(w.manifest_path, 'ab').write(b' '), 'a changed sealed manifest', field='ok', want=False)
snap_case(lambda w: os.chmod(os.path.join(w.view, 'inputs'), 0o777), 'a world-writable inputs directory (the pinned chain)', field='ok', want=False)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.write_claim('snap-1', json.load(open(w.claim_path))['sealedManifestSha256'], op='other-op')
    r = go(w)
    check('error' in r.rec['snapshotStart'] and r.rec['schema'] == 'learn-op-retirement-failure/1' and not r.rec['retirement']['snapshot']['ok'], 'a claim for another op at the start: no snapshot state, a retirement failure at the end')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.write_claim('snap-1', '0' * 64)
    r = go(w)
    check('sealed manifest does not have the sha256' in r.rec['snapshotStart']['error'], 'a claim recording another manifest hash is refused at the start')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    os.chmod(os.path.join(w.view, 'claims'), 0o777)
    r = go(w)
    check('not root-safe' in r.rec['snapshotStart']['error'], 'the claim is read through the pinned chain: a writable claims directory is refused')

# ============================================================ a crash inside the run
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def boom():
        raise RuntimeError('pg stop exploded')
    r = go(w, patch={'stop_pg': boom})
    check(r.code == 0 and r.rec['schema'] == 'learn-op-retirement-failure/1' and 'pg stop exploded' in r.rec['error'], 'an unexpected exception ends in a retirement-failure record that names it')
    check(all(idx(w.calls, ['podman', 'unpause', f'learn-pod-op-pod-1-{x}']) > 0 for x in W.ROLES) and idx(w.calls, ['podman', 'pod', 'rm']) > 0, 'on a crash every role is unpaused and the pod is removed')
    check(r.rec['retirement']['earlyPath'] == 'exception' and r.rec['retirement']['podGone'], 'a crash still runs the retirement checks')
    check('inputRead' in r.rec and 'containersStartedWithInput' in r.rec, 'a crash record still carries the input evidence the claim tooling reads')

# ============================================================ second round: what the first mutation run found unpinned
def pg_case(setup, what, **expect):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        setup(w)
        r = go(w)
        for k, v in expect.items():
            val = r.rec
            for part in k.split('__'):
                val = val[part]
            check(val == v, f'{what}: {k} is {v}')
        return r


# claim and sealed manifest identities at the start
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.write_claim('snap-1', json.load(open(w.claim_path))['sealedManifestSha256'], ident='11' * 32)
    r = go(w)
    check('not this op' in r.rec['snapshotStart']['error'], 'a claim file whose sourceIdentity field is another identity is refused at the start')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.make_snapshot(sm_ident='22' * 32)
    r = go(w)
    check('another snapshot or source' in r.rec['snapshotStart']['error'], 'a sealed manifest for another source identity is refused at the start')
# the claim re-written with other (valid) content, same manifest hash
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def rewrite():
        c = json.load(open(w.claim_path))
        os.unlink(w.claim_path)
        c['generation'] = 2
        open(w.claim_path, 'w').write(json.dumps(c, sort_keys=True))
        os.chmod(w.claim_path, 0o644)
    w.at(10, rewrite)
    r = go(w)
    s_ = r.rec['retirement']['snapshot']
    check(s_['claimUnchanged'] is False and s_['manifestUnchanged'] is True and not s_['ok'] and r.rec['schema'] == 'learn-op-retirement-failure/1',
          'a claim re-written with other valid content (same manifest) is a change: the claim must be byte-identical')
# a sealed file with other content, same size, same mtime
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def swap():
        p = os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json')
        ns = os.stat(p).st_mtime_ns
        open(p, 'wb').write(b'{"a":2}')
        os.utime(p, ns=(ns, ns))
    w.at(10, swap)
    r = go(w)
    check(r.rec['retirement']['snapshot']['changed'] == ['passive-a/spool/one.json'], 'other content of the same size with the mtime restored is caught by the sha256 alone')
# unsafe manifest paths, and non-file entries
for what, entry in (('a ../ path', {'path': '../evil', 'type': 'file', 'size': 0, 'sha256': hashlib.sha256(b'').hexdigest(), 'mtime_ns': 1, 'mode': '0o644'}),
                    ('an absolute path', {'path': '/etc/passwd', 'type': 'file', 'size': 0, 'sha256': hashlib.sha256(b'').hexdigest(), 'mtime_ns': 1, 'mode': '0o644'}),
                    ('a duplicate path', {'path': 'passive-a/spool/one.json', 'type': 'file', 'size': 7, 'sha256': '0' * 64, 'mtime_ns': 1, 'mode': '0o644'}),
                    ('a non-file type', {'path': 'passive-a/dir', 'type': 'dir', 'size': 0, 'sha256': '0' * 64, 'mtime_ns': 1, 'mode': '0o755'})):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        w.make_snapshot(extra_entries=[entry])
        r = go(w)
        check(not r.rec['retirement']['snapshot']['ok'] and 'unsafe or duplicate' in r.rec['retirement']['snapshot'].get('error', '') and r.rec['schema'] == 'learn-op-retirement-failure/1',
              f'a sealed manifest with {what} fails the snapshot check')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(10, lambda: os.symlink('/etc', os.path.join(w.view, 'inputs', 'snap-1', 'linkdir')))
    r = go(w)
    check(r.rec['retirement']['snapshot']['extra'] == ['linkdir'], 'a symlink to a directory inside the snapshot is an extra entry (not followed)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def fifo():
        p = os.path.join(w.view, 'inputs', 'snap-1', 'passive-b/spool/producer.lock')
        ns = os.stat(p).st_mtime_ns
        os.unlink(p)
        os.mkfifo(p, 0o644)
        os.utime(p, ns=(ns, ns))
    w.at(10, fifo)
    r = go(w)
    check(r.rec['retirement']['snapshot']['changed'] == ['passive-b/spool/producer.lock'] and r.rec['schema'] == 'learn-op-retirement-failure/1',
          'an empty sealed file replaced by a FIFO of the same size, mode and mtime is caught by the type check (it would read as the empty hash)')

# the volume budget is >= 90%, exactly
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    w.vol_exact = (1000, 100)
    r = go(w)
    check(r.rec['trigger'] == 'budget' and r.rec['retirement']['volume']['budgetHit'] is True and r.rec['retirement']['volume']['maxUsedFraction'] == 0.9, 'exactly 90.0% used triggers the budget and is recorded as budgetHit')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=5)
    w.at(3, lambda: setattr(w, 'vol_used', 0.95))
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-killed-retired/1' and r.rec['retirement']['volume']['budgetHit'], 'every role done but the volume over budget: never a success')
# roles finishing at different times: the trigger waits for ALL of them
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    for role, t in (('host', 10), ('recv', 14), ('feed', 18)):
        w.at(t, lambda role=role: (w.put(role, 'o.json', b'1'), w.finish_role(role, 0, 'ok')))
    r = go(w)
    check(r.rec['trigger'] == 'done' and r.rec['schema'] == 'learn-op-pod-success/1' and all(r.rec['roles'][x]['status']['status'] == 'valid' for x in W.ROLES), 'the trigger fires when the LAST role is done, not the first')
# identity details
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def running(c):
        if c['role'] == 'host':
            c['state'] = 'running'                      # frozen 1 in the cgroup, but the container reports running
    w.pause_hook = running
    r = go(w)
    check(r.rec['roles']['host']['custody']['verdict'] == 'rejected:identity', 'a container that does not report state paused is rejected:identity even if the cgroup says frozen')
for what, when in (('after', 'after'), ('before', 'before')):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        host = w.containers['learn-pod-op-pod-1-host']
        cgf = os.path.join(w.proc, str(host['pid']), 'cgroup')
        os.makedirs(w.cg + '/user.slice/other.scope')                      # a cgroup that is frozen but is not this container's (no container id in its path)
        open(w.cg + '/user.slice/other.scope/cgroup.events', 'w').write('populated 1\nfrozen 1\n')
        good = open(cgf).read()
        real_archive = F.archive

        def wrapped(*a, _w=when, **k):
            if _w == 'before':
                open(cgf, 'w').write(good)                      # restored after s0 was sampled: only the BEFORE sample saw the wrong cgroup
            res = real_archive(*a, **k)
            if _w == 'after':
                open(cgf, 'w').write('0::/user.slice/other.scope\n')
            return res
        if when == 'before':
            w.pause_hook = lambda c: open(cgf, 'w').write('0::/user.slice/other.scope\n') if c['role'] == 'host' else None
        r = go(w, patch={'archive': wrapped})
        check(r.rec['roles']['host']['custody']['verdict'] == 'rejected:identity' and not r.rec['roles']['host']['identity']['identOk'],
              f'a cgroup that does not carry the container id {what} the archive fails the identity check')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(21, lambda: w.containers['learn-pod-op-pod-1-feed'].update(state='exited'))
    r = go(w)
    check(r.rec['roles']['feed']['archive'] == {'skipped': 'no running container'} and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a role whose container has exited (pid 0) is not archived from a stale /proc path')
# a valid request padded past 512 bytes
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1, body=json.dumps({'n': 1, 'label': 'padded'}).encode() + b' ' * 600))
    r = go(w)
    check(json.load(open(os.path.join(r.ck, 'ack-1')))['ok'] is False, 'a request over 512 bytes is refused even when its first 512 bytes are valid JSON')
# failing censuses are never read as "nothing there"
retire_case(lambda w: w.fail.add('ps'), 'containersGone', 'a failing container listing at retirement (fail closed)', expect=False)
retire_case(lambda w: w.fail.add('slice_mem_stuck'), 'slice.returned', 'memory that does not return', expect=False)
retire_case(lambda w: w.fail.add('slice_pids_stuck'), 'slice.returned', 'pids that do not return', expect=False)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.unit_lag = 3
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['opUnit']['ActiveState'] == 'inactive', 'the finalizer waits for the pod unit to go inactive before it reads the unit and retires (a unit that takes 3 s to stop)')
# unpause failures
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.fail.add('unpause')
    r = go(w)
    check(r.rec['roles']['host']['unpauseRc'] == 1 and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a role that could not be unpaused after its archive is never a success')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1))
    w.fail.add('unpause')
    r = go(w)
    check(r.rec['checkpoints'][0]['roles']['host']['unpauseRc'] == 1 and r.rec['schema'] == 'learn-op-pod-killed-retired/1', 'a checkpoint that left a role paused is recorded and the run cannot succeed')
# a role left paused by a CHECKPOINT only (the final archives unpause fine): recorded, and never a success
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1))
    w.at(6, lambda: w.fail.add('unpause'))
    w.at(12, lambda: w.fail.discard('unpause'))
    r = go(w)
    check(r.rec['checkpoints'][0]['roles']['host']['unpauseRc'] == 1 and all(v.get('unpauseRc', 0) == 0 for v in r.rec['roles'].values()) and r.rec['schema'] == 'learn-op-pod-killed-retired/1',
          'a role left paused by a checkpoint alone (the final unpause succeeds) is still never a success')
# a crash before any archive: the handler itself unpauses everything and removes the pod, and the record still carries the input evidence
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    real_vs, calls_ = F.volume_sample, []

    def boom_once():
        calls_.append(1)
        if len(calls_) == 1:
            raise RuntimeError('statvfs exploded')
        return real_vs()
    r = go(w, patch={'volume_sample': boom_once})
    check(all(idx(w.calls, ['podman', 'unpause', f'learn-pod-op-pod-1-{x}']) > 0 for x in W.ROLES) and idx(w.calls, ['podman', 'pod', 'rm']) > 0 and r.rec['schema'] == 'learn-op-retirement-failure/1'
          and 'statvfs exploded' in r.rec['error'], 'a crash before any archive still unpauses every role and removes the pod')
    check(r.rec['inputRead'] is False and r.rec['containersStartedWithInput'] == [] and r.rec['retirement']['earlyPath'] == 'exception',
          'the exception path computes the input evidence too (before the pod is removed): every record carries the fields the claim tooling reads')
    ps_i, rm_i = idx(w.calls, ['podman', 'ps']), idx(w.calls, ['podman', 'pod', 'rm'])
    check(0 < ps_i < rm_i, 'the container census for the input evidence ran before the pod was removed')
# a crash AFTER the input evidence was taken does not take it a second time (the census is asked once before the pod is removed)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def boom_archive(*a_, **k_):
        raise RuntimeError('archive exploded')
    r = go(w, patch={'archive': boom_archive})
    rm_i = idx(w.calls, ['podman', 'pod', 'rm'])
    census_before_rm = [c for c in w.calls[:rm_i] if c[:3] == ['podman', 'ps', '-a'] and c[-1] == '{{.Names}}']
    check(r.rec['schema'] == 'learn-op-retirement-failure/1' and 'archive exploded' in r.rec['error'] and len(census_before_rm) == 1,
          f'a crash after the input evidence was taken: one container census before the pod is removed (got {len(census_before_rm)})')
# the evidence function itself failing never loses the contract fields, on any path
for what, kw, mk in (('the final path', dict(done_at=20), None), ('the early path', dict(activate_at=None, done_at=None), None)):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp, **kw)

        def explode():
            raise RuntimeError('inputs exploded')
        r = go(w, patch={'inputs_evidence': explode})
        check('inputRead' in r.rec and r.rec['containersStartedWithInput'] is None and r.rec['inputRead'] is False, f'{what}: an inputs_evidence that raises still yields inputRead and a null list')
        check(r.rec['schema'] != 'learn-op-pod-success/1', f'{what}: unknown input evidence is never a success' if what == 'the final path' else f'{what}: still a record')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)

    def explode2():
        raise RuntimeError('x')
    os.makedirs(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID))
    open(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID, 'input-read'), 'w').close()
    r = go(w, patch={'inputs_evidence': explode2})
    check(r.rec['inputRead'] is True and r.rec['containersStartedWithInput'] is None, 'a failing evidence function still reports the marker it can see')

# the census that succeeds and lists no container
for what, marker, want in (('no marker', False, []), ('the marker present', True, None)):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp, activate_at=None, done_at=None)
        if marker:
            os.makedirs(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID))
            open(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID, 'input-read'), 'w').close()
        w.containers.clear()                                  # `podman ps` succeeds and lists nothing
        r = go(w)
        check(r.rec['containersStartedWithInput'] == want and r.rec['inputRead'] is marker, f'a successful census listing zero containers with {what}: containersStartedWithInput is {want}')
        v, sup = None, None
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.fail.add('ps')
    r = go(w)
    check(r.rec['containersStartedWithInput'] is None, 'a FAILED census is null whatever the marker says')

# ============================================================ third round: review findings
SEAL = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'seal', 'learn_seal_claim.py')
spec_l = importlib.util.spec_from_file_location('lsc', SEAL)
L = importlib.util.module_from_spec(spec_l)
spec_l.loader.exec_module(L)


def contract(r, marker_present):
    """What the claim tooling decides about this record: (void blockers, supersede evidence)."""
    mk = os.path.join(r.w.home, '.local', 'state', 'learn-input')
    opres = {'schema': L.OPERATOR_RESULT_SCHEMA, 'opId': W.OPID, 'mode': '--run', 'exitCode': 5, 'errors': ['x'], 'gates': {'inputMounted': False}, 'containers': [],
             'runNonceSha256': r.rec.get('runNonceSha256'), 'operatorSha256': 'b2' * 32}
    adm = {'operatorSha256': 'b2' * 32, 'finalizerSha256': r.rec.get('finalizerSha256')}
    return L._evidence_contract(W.OPID, opres, r.rec, mk, adm), L._positive_consumption(W.OPID, r.rec, mk)


# void and supersede against REAL records (the deadlock the review found: a null list with no marker made a claim neither voidable nor supersedable)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, activate_at=None, done_at=None)
    w.containers.clear()
    r = go(w)
    void_why, sup = contract(r, False)
    check(r.rec['schema'] == 'learn-op-pod-failed-to-start/1' and void_why == [] and sup == [], 'a pod that never started, marker absent: the claim tooling finds the claim VOIDABLE and nothing to supersede (no deadlock)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, activate_at=None, done_at=None)
    os.makedirs(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID))
    open(os.path.join(w.home, '.local', 'state', 'learn-input', W.OPID, 'input-read'), 'w').close()
    w.containers.clear()
    r = go(w)
    void_why, sup = contract(r, True)
    check(void_why and sup, 'marker present and no container left to ask: not voidable, and supersedable (the marker is positive evidence)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.containers['learn-pod-op-pod-1-feed']['mounts'] = [{'Destination': '/in'}]
    r = go(w)
    void_why, sup = contract(r, False)
    check(void_why and sup == ['the finalizer record lists containers started with the input'], 'a container started with /in: not voidable, supersedable on the record alone')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.fail.add('ps')
    r = go(w)
    void_why, sup = contract(r, False)
    check(void_why and not sup, 'a census that failed: neither provable as clean nor as consumed: the void is refused (fail closed) and nothing is claimed as consumption')

# an op id with ONLY the checkpoint directory present: exit 70 and nothing created
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    os.makedirs(os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID))
    r = go(w)
    check(r.code == 70 and not os.path.exists(r.cust) and not os.path.exists(r.evid) and w.calls == [] and 'RECORD' not in r.out, 'only the checkpoint directory exists: exit 70 and the custody directory is NOT created')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    real_makedirs = os.makedirs

    def racing(path, *a, **k):
        if str(path).endswith('/learn-checkpoint/' + W.OPID):
            real_makedirs(path)                                     # another process creates it between the check and the creation
        return real_makedirs(path, *a, **k)
    os.makedirs = racing
    try:
        r = go(w)
    finally:
        os.makedirs = real_makedirs
    check(r.code == 70 and not os.path.exists(r.cust) and w.calls == [], 'a checkpoint directory that appears between the check and the creation: custody is removed again and the exit is 70')

# B1: a second finalizer that loses the race on the CUSTODY directory exits 70 before any side effect (it must never reach main's generic handler, which would stop the winner's pod)
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    real_makedirs = os.makedirs

    def racing_cust(path, *a, **k):
        if str(path).endswith('/learn-custody/' + W.OPID):
            real_makedirs(path)                                     # the winner creates it between the existence check and the creation
        return real_makedirs(path, *a, **k)
    os.makedirs = racing_cust
    try:
        r = go(w)
    finally:
        os.makedirs = real_makedirs
    check(r.code == 70 and w.calls == [] and not os.path.exists(r.evid) and 'RECORD' not in r.out and os.path.isdir(r.cust) and not os.path.exists(os.path.join(r.cust, 'chain.jsonl')),
          'a lost race on the custody directory: exit 70, no podman or systemctl call, no evidence directory, no record, the winner\'s directory untouched')
    check(os.path.isdir(r.cust) and not os.path.exists(r.ck), 'the loser created nothing of its own (no checkpoint directory)')

# N1: one transient `systemctl show` failure is not the unit ending
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(4, lambda: setattr(w, 'show_state_fails', 1))
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['trigger'] == 'done' and 'earlyPath' not in r.rec['retirement'] and all(r.rec['roles'][x]['custody']['verdict'] == 'accepted' for x in W.ROLES),
          'one failing `systemctl show` in the wait loop: the run still archives every role and succeeds')
    check(any(e['event'] == 'unit-state-unknown' for e in r.rec['events']) and 'unitStateUnknown' not in r.rec, 'the unreadable poll is chained, and it is not recorded as a conclusion')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(4, lambda: setattr(w, 'show_state_fails', 5))
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and [e['event'] for e in r.rec['events']].count('unit-state-unknown') == 1, '5 consecutive unreadable polls (the bound) are still not the unit ending, and the run is chained as unknown once')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    w.at(4, lambda: setattr(w, 'show_state_fails', 1000))
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-unrecoverable/1' and r.rec['retirement']['earlyPath'] == 'ended-without-done' and r.rec['unitStateUnknown'] == {'consecutivePolls': 6, 'concluded': 'ended'},
          'a unit state that stays unreadable past the bound is concluded as ended, and that conclusion is recorded explicitly')

# N2: the pod unit's cgroup must come from systemd on the paths that can succeed
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.cg_read_fails = 3
    r = go(w)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['retirement']['podCgroupFromSystemd'] is True and r.rec['podControlGroupReadTries'] == 4,
          'a ControlGroup read that fails three times is retried and the run succeeds with the systemd path')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.cg_read_fails = 1000
    r = go(w)
    check(r.rec['retirement']['podCgroupFromSystemd'] is False and r.rec['schema'] == 'learn-op-pod-killed-retired/1' and r.rec['podControlGroupReadTries'] == 5,
          'a ControlGroup that stays empty: the checks ran on a guessed path, so the run is never a success')

# pg takes time to exit after SIGINT: the stop-wait loop is pinned
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.pg_exit_after = 4
    r = go(w)
    kill_i = idx(w.calls, ['podman', 'kill'])
    polls = [i for i, c in enumerate(w.calls) if i > kill_i and c[:2] == ['podman', 'inspect'] and c[-1].endswith('-pg') and c[3].startswith('{{.State.Status}}|')]
    check(r.rec['pg']['ok'] and r.rec['pg']['stop']['exited'] and len(polls) >= 5 and r.rec['schema'] == 'learn-op-pod-success/1',
          'pg that needs 4 s to exit after SIGINT is polled (about once a second) until it has exited, then read: pg ok')
    check(r.rec['pg']['stop']['before'].startswith('running|') and r.rec['pg']['stop']['after'].startswith('exited|'), 'the state before the signal was running and after the wait exited')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.pg_exit_after = 45
    r = go(w)
    kill_i = idx(w.calls, ['podman', 'kill'])
    polls = [c for i, c in enumerate(w.calls) if i > kill_i and c[:2] == ['podman', 'inspect'] and c[-1].endswith('-pg') and c[3].startswith('{{.State.Status}}|')]
    check(r.rec['pg']['stop']['exited'] is False and r.rec['pg']['stop']['exitCode'] is None and r.rec['pg']['ok'] is False and r.rec['schema'] == 'learn-op-pod-killed-retired/1',
          'pg that has not exited within 30 s is not ok (its exit code is not read from a running container); the pod is stopped regardless')
    check(30 <= len(polls) <= 33, f'the wait is bounded at about 30 polls (got {len(polls)})')

# the admission cross-check of the pg image
for what, mut in (('no admission file', lambda w: os.unlink(w.admission_path)), ('only an admission for another op', lambda w: (os.unlink(w.admission_path), w.write_admission(FIN_SHA, op='op-other'))),
                  ('an admission whose file names another op', lambda w: (w.write_admission(FIN_SHA, op=W.OPID, raw=json.dumps({'schema': 'learn-pod-admission/1', 'opId': 'op-other', 'sourceIdentity': W.IDENT, 'finalizerSha256': FIN_SHA, 'imageIds': [W.PGIMG]})))),
                  ('another source identity', lambda w: w.write_admission(FIN_SHA, ident='12' * 32)), ('another finalizer', lambda w: w.write_admission('34' * 32)),
                  ('a pg image that was not admitted', lambda w: w.write_admission(FIN_SHA, image_ids=['56' * 32])), ('an empty image list', lambda w: w.write_admission(FIN_SHA, image_ids=[])),
                  ('a malformed image Id', lambda w: w.write_admission(FIN_SHA, image_ids=[W.PGIMG, 'xyz'])), ('a non-string image Id (the check never raises)', lambda w: w.write_admission(FIN_SHA, image_ids=[W.PGIMG, 5])), ('another schema', lambda w: w.write_admission(FIN_SHA, schema='x/1')),
                  ('invalid JSON', lambda w: w.write_admission(FIN_SHA, raw='{"x"')), ('a list', lambda w: w.write_admission(FIN_SHA, raw='[1]')),
                  ('a group-writable file', lambda w: w.write_admission(FIN_SHA, mode=0o664)),
                  ('a symlink', lambda w: (os.rename(w.admission_path, w.admission_path + '.real'), os.symlink(w.admission_path + '.real', w.admission_path)))):
    with tempfile.TemporaryDirectory() as tmp:
        w = world(tmp)
        mut(w)
        r = go(w)
        check(r.rec['admission']['ok'] is False and r.rec['admission']['reasons'] and r.rec['schema'] == 'learn-op-pod-killed-retired/1', f'{what}: the admission cross-check fails and the run cannot succeed')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    r = go(w)
    check(r.rec['admission']['ok'] and r.rec['admission']['reasons'] == [] and r.rec['admission']['sha256'] == sha(open(w.admission_path, 'rb').read()), 'an admission naming this op, source, finalizer and the pg image passes and its hash is recorded')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    os.chmod(os.path.join(w.view, 'admissions'), 0o777)
    r = go(w)
    check('not root-safe' in ' '.join(r.rec['admission']['reasons']), 'the admission is read through the pinned chain: a writable admissions directory is refused')

# pod cgroup: populated 1 in cgroup.events with an empty cgroup.procs is still not retired
retire_case(lambda w: w.fail.add('cg_events_only'), 'podCgroupRetired', 'a pod cgroup whose events file still says populated', expect=False)

# the archive command: tar stays on one filesystem and writes ustar
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    seen_ = []
    real_arch = F.archive

    def spy(argv, *a, **k):
        seen_.append(list(argv))
        return real_arch(argv, *a, **k)
    r = go(w, patch={'archive': spy})
    check(len(seen_) == 3 and all('--one-file-system' in a_ and '--format=ustar' in a_ and a_[a_.index('-C') + 2:a_.index('-C') + 4] == ['-cf', '-'] for a_ in seen_)
          and [a_[a_.index('-C') + 1].split('/')[-1] for a_ in seen_] == list(W.ROLES), 'tar is run once per role over that role\'s /out/<role>, on one filesystem, in ustar format')

# the acknowledgement is written only after that checkpoint's archives are done
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.at(6, request(w, 1))
    acks_at_archive = []
    real_arch2 = F.archive

    def spy2(argv, *a, **k):
        acks_at_archive.append(os.path.exists(os.path.join(w.home, '.local/state/learn-checkpoint', W.OPID, 'ack-1')))
        return real_arch2(argv, *a, **k)
    r = go(w, patch={'archive': spy2})
    check(acks_at_archive[:3] == [False, False, False] and os.path.exists(os.path.join(r.ck, 'ack-1')), 'ack-1 does not exist while the checkpoint\'s three archives run; it is written after them')
    ev_ = [json.loads(l)['event'] for l in open(os.path.join(r.cust, 'chain.jsonl')).read().splitlines()]
    check(ev_.index('checkpoint-request') < ev_.index('checkpoint-ack'), 'the chain logs the request before the acknowledgement')

# ============================================================ unit tests of the pieces
# parse_status
G = lambda **k: json.dumps({'schema': 'learn-pod-status/1', 'role': 'host', 'phase': 'terminal', 'exit': 0, 'reason': 'ok', **k}).encode().ljust(4096, b' ')
check(F.parse_status(G(), True, 'host')['status'] == 'valid', 'parse_status: a good terminal status')
check(F.parse_status(G(), False, 'host') == {'status': 'absent'}, 'parse_status: no .done is absent whatever the file says')
check(F.parse_status(None, True, 'host')['reason'] == 'missing', 'parse_status: .done without a status is malformed')
check(F.parse_status(('size', 7), True, 'host')['reason'] == 'size 7', 'parse_status: a status of another size is malformed')
for what, kw in (('schema', {'schema': 'x'}), ('role', {'role': 'recv'}), ('phase', {'phase': 'ready'}), ('reason', {'reason': 'nope'}), ('exit bool', {'exit': False}), ('exit str', {'exit': '1'}), ('exit none', {'exit': None})):
    check(F.parse_status(G(**kw), True, 'host')['status'] == 'malformed', f'parse_status: bad {what} is malformed')
check(F.parse_status(G(detail='x' * 50), True, 'host')['value'] == {'exit': 0, 'reason': 'ok', 'detail': 'x' * 50}, 'parse_status: only exit, reason and detail are kept')
# role_outcome
for role, reason, want in (('host', 'ok', True), ('host', 'signalled', True), ('recv', 'signalled', True), ('feed', 'ok', True), ('feed', 'signalled', False), ('host', 'error', False), ('recv', 'enospc', False),
                           ('host', 'gate-refused', False)):
    ok_, _ = F.role_outcome(role, {'status': 'valid', 'value': {'reason': reason}}, {'verdict': 'accepted'}, False)
    check(ok_ is want, f'role_outcome: {role} ending {reason} is {"acceptable" if want else "not acceptable"}')
check(F.role_outcome('host', {'status': 'valid', 'value': {'reason': 'ok'}}, {'verdict': 'rejected:tar'}, False)[0] is False, 'role_outcome: a rejected archive is never ok')
check(F.role_outcome('host', {'status': 'absent'}, {'verdict': 'accepted'}, False) == (False, 'unknown'), 'role_outcome: an absent status is not ok and enospc is unknown')
check(F.role_outcome('recv', {'status': 'valid', 'value': {'reason': 'enospc'}}, {'verdict': 'accepted'}, True)[1] == 'yes', 'role_outcome: enospc with a low volume is yes')
check(F.role_outcome('recv', {'status': 'valid', 'value': {'reason': 'ok'}}, {'verdict': 'accepted'}, True)[1] == 'inconsistent', 'role_outcome: a low volume without an enospc status is inconsistent')


# inventory, on crafted tar files
def make_tar(tmp, name, members, end=True, pad=True):
    p = os.path.join(tmp, name)
    with tarfile.open(p, 'w', format=tarfile.USTAR_FORMAT) as tf:
        for m in members:
            ti = tarfile.TarInfo(m[0])
            kind, data = m[1], m[2] if len(m) > 2 else b''
            ti.type = {'file': tarfile.REGTYPE, 'dir': tarfile.DIRTYPE, 'sym': tarfile.SYMTYPE, 'hard': tarfile.LNKTYPE, 'fifo': tarfile.FIFOTYPE, 'chr': tarfile.CHRTYPE}[kind]
            ti.size = len(data) if kind == 'file' else 0
            if kind in ('sym', 'hard'):
                ti.linkname = 'x'
            tf.addfile(ti, io.BytesIO(data) if kind == 'file' else None)
    return p


def inv(tmp, members, **kw):
    p = make_tar(tmp, 't.tar', members)
    return F.inventory(p, kw.get('ident_ok', True), kw.get('tar_rc', 0), kw.get('tarerr', 0), kw.get('over', False), kw.get('members_left', 4096), kw.get('content_left', 256 << 20))


with tempfile.TemporaryDirectory() as tmp:
    base = [('.', 'dir'), ('./.status', 'file', b'S' * 4096), ('./.done', 'file', b''), ('./a', 'dir'), ('./a/b', 'file', b'hi')]
    i, st, done = inv(tmp, base)
    check(i['verdict'] == 'accepted' and done and st == b'S' * 4096 and i['memberCount'] == 4 and i['apparentBytes'] == 4096 + 2, 'inventory: an accepted stream; .status is kept at exactly 4096 B; "." is not counted')
    by = {m['name']: m for m in i['members']}
    check(by['a']['sha256'] is None and by['a']['kind'] == 'dir' and by['.done']['sha256'] == sha(b'') and by['a/b']['depth'] == 1, 'inventory: members carry kind, depth and sha256 (null for a directory)')
    i, st, done = inv(tmp, [('./.status', 'file', b'S' * 100), ('./.done', 'file')])
    check(st == ('size', 100) and i['verdict'] == 'accepted', 'inventory: a .status of another size is reported by size, not read')
    for what, mem, vd in (('a symlink', base + [('./l', 'sym')], 'rejected:type'), ('a hard link', base + [('./h', 'hard')], 'rejected:type'), ('a fifo', base + [('./f', 'fifo')], 'rejected:type'),
                          ('a device', base + [('./c', 'chr')], 'rejected:type'), ('an absolute name', base + [('/etc/x', 'file', b'x')], 'rejected:type'),
                          ('a .. name', base + [('./../x', 'file', b'x')], 'rejected:type'), ('depth 9', base + [('./' + '/'.join(['d'] * 9) + '/f', 'file', b'x')], 'rejected:type')):
        try:
            i, st, done = inv(tmp, mem)
            check(i['verdict'] == vd or i['verdict'] == 'rejected:truncated', f'inventory: {what} is rejected ({i["verdict"]})')
        except Exception:
            check(True, f'inventory: {what} cannot even be put in a tar here')
    i, _, _ = inv(tmp, base, members_left=3)
    check(i['verdict'] == 'rejected:count' and len(i.get('members', [])) <= 3, 'inventory: more members than are left is rejected:count')
    i, _, _ = inv(tmp, base, content_left=100)
    check(i['verdict'] == 'rejected:size', 'inventory: more content than is left is rejected:size')
    i, _, _ = inv(tmp, base, ident_ok=False)
    check(i['verdict'] == 'rejected:identity' and 'members' not in i, 'inventory: an identity failure discloses no member names or sizes')
    i, _, _ = inv(tmp, base, tar_rc=2)
    check(i['verdict'] == 'rejected:tar', 'inventory: a tar exit code other than 0 is rejected:tar')
    i, _, _ = inv(tmp, base, tarerr=5)
    check(i['verdict'] == 'rejected:tar', 'inventory: tar stderr output is rejected:tar')
    i, _, _ = inv(tmp, base, over=True)
    check(i['verdict'] == 'rejected:budget', 'inventory: an overflowed stream is rejected:budget')
    i, _, _ = inv(tmp, base, over=None)
    check(i['verdict'] == 'rejected:truncated' and i['reason'] == 'archive timed out', 'inventory: a timed-out archive is rejected:truncated, not budget')
    p = os.path.join(tmp, 'cut.tar')
    open(p, 'wb').write(open(make_tar(tmp, 'full.tar', base), 'rb').read()[:1500])
    check(F.inventory(p, True, 0, 0, False, 4096, 256 << 20)[0]['verdict'] == 'rejected:truncated', 'inventory: a stream cut short is rejected:truncated')
    p2 = os.path.join(tmp, 'odd.tar')
    open(p2, 'wb').write(open(make_tar(tmp, 'full.tar', base), 'rb').read() + b'x')
    check(F.inventory(p2, True, 0, 0, False, 4096, 256 << 20)[0]['verdict'] == 'rejected:truncated', 'inventory: a stream that is not a whole number of blocks is rejected:truncated')
    p3 = os.path.join(tmp, 'link.tar')
    os.symlink(p, p3)
    try:
        F.inventory(p3, True, 0, 0, False, 4096, 256 << 20)
        check(False, 'inventory follows a symlink')
    except OSError:
        check(True, 'inventory: the stream is opened O_NOFOLLOW')
    huge = os.path.join(tmp, 'huge.tar')
    open(huge, 'wb').write(b'\0' * (1024 * 1024))
    check(F.inventory(huge, True, 0, 0, False, 2, 100)[0]['verdict'] == 'rejected:budget', 'inventory: a stream larger than content + 1 KiB per remaining member + 10 KiB is rejected:budget')
# the stream cap formula equals r4's CAP when nothing has been used
check(F.CONTENT_CAP + F.MAX_MEMBERS * 1024 + 10240 == F.CAP, 'the per-archive stream cap formula equals finalizer r4\'s CAP for a fresh budget')

# volume
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    F.configure(['--id', W.OPID, '--source-identity', W.IDENT], {'FSGG_NONCE': W.NONCE, 'FSGG_PG_IMAGE': W.PGIMG}, 30034, home=w.home, view=w.view, statvfs=w.statvfs, volume_root=w.volroot,
                is_mountpoint=lambda p: True, production=False, cred_root=w.cred)
    check(abs(F.volume_sample() - w.vol_used) < 1e-4 and F.VOLUME_STATE['last']['totalBytes'] == 1 << 30 and F.VOLUME_STATE['samples'] == 1, 'volume_sample: the used fraction from f_bavail and the total from f_blocks')
    w.vol_used = 0.5
    F.volume_sample()
    w.vol_used = 0.2
    F.volume_sample()
    check(abs(F.VOLUME_STATE['maxUsed'] - 0.5) < 1e-3 and abs(F.volume_check()['maxUsedFraction'] - 0.5) < 1e-3, 'volume_sample: the maximum used fraction is kept')
    w.fail.add('statvfs')
    check(F.volume_sample() is None and F.VOLUME_STATE['errors'] == 1, 'volume_sample: an unreadable mount is an error, not a sample')
    F.configure(['--id', W.OPID, '--source-identity', W.IDENT], {'FSGG_NONCE': W.NONCE, 'FSGG_PG_IMAGE': W.PGIMG}, 30034, home=w.home, view=w.view, production=False)
    check(F.VOLUME_STATE['samples'] == 0 and F.BUDGET == {'members': 4096, 'content': 256 << 20} and F.SLICE0 == {}, 'configure resets the volume state, the shared budget and the slice baseline')
    check(F.VOLUME == f'{F.DEFAULTS["volume_root"]}/{W.OPID}.mnt' and F.CRED == f'/run/user/30034/learn-cred/{W.OPID}' and F.MARKER.endswith(f'/learn-input/{W.OPID}/input-read'), 'the volume, credential and marker paths are derived from the op id')
    check(F.CTRS == {x: f'learn-pod-{W.OPID}-{x}' for x in W.ROLES} and F.PGC == f'learn-pod-{W.OPID}-pg' and F.OPU == f'learn-pod-{W.OPID}.service' and F.FINU == f'learn-fin-{W.OPID}.service', 'unit and container names')


# ============================================================ #5184: an explicitly declared SYNTHETIC-input op (the pod harness and the install receipt op)
# Clock: the world's scenario clock (FakeTime). Source: temporary trees; the synthetic records are the seal tool's record types, written by the world.
SYN = ['--id', W.OPID, '--source-identity', W.IDENT, '--input', 'synthetic']


def syn_world(tmp, sm_extra=None, claim_extra=None, keep_real=False):
    w = world(tmp)
    if not keep_real:
        os.unlink(w.claim_path)                                  # nothing real is claimed: the op holds only the synthetic claim
        shutil.rmtree(os.path.join(w.view, 'inputs', 'snap-1'))
        os.unlink(w.manifest_path)
    w.make_snapshot(kind='synthetic', sm_extra=sm_extra, claim_extra=claim_extra)
    return w


with tempfile.TemporaryDirectory() as tmp:
    w = syn_world(tmp)
    r = go(w, argv=SYN)
    check(r.code == 0 and r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['inputProvenance'] == 'synthetic', 'SYN: a declared synthetic op with a synthetic claim and seal retires as a success and says inputProvenance synthetic')
    check(r.rec['retirement']['snapshot']['ok'] and r.rec['retirement']['snapshot']['filesChecked'] == 3 and r.rec['snapshotStart']['snapshotId'] == 'snap-1',
          'SYN: the synthetic claim and sealed snapshot are read at the start and re-verified at retirement (every file)')
    check(not os.path.exists(os.path.join(w.view, 'claims', W.IDENT + '.claim')), 'SYN control: no real claim exists in this world, so the success rests on the synthetic records alone')
with tempfile.TemporaryDirectory() as tmp:
    w = syn_world(tmp)
    r = go(w)
    check(r.rec['inputProvenance'] == 'real' and 'error' in r.rec['snapshotStart'] and r.rec['schema'] == 'learn-op-retirement-failure/1',
          'SYN: the same synthetic op WITHOUT the declaration finds no real claim and is a retirement failure (a synthetic claim is never read implicitly)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    r = go(w, argv=SYN)
    check('error' in r.rec['snapshotStart'] and r.rec['schema'] == 'learn-op-retirement-failure/1', 'SYN: a REAL op declared synthetic finds no synthetic claim and is a retirement failure (a real claim is never read as synthetic)')
    r2_ = go(world(os.path.join(tmp, 'b')))
    check(r2_.rec['inputProvenance'] == 'real' and r2_.rec['schema'] == 'learn-op-pod-success/1', 'SYN control: the undeclared real op is unchanged and records inputProvenance real')
for what, kw, needle in (('a synthetic claim without provenance', dict(claim_extra={'provenance': None}), 'does not say synthetic provenance'),
                         ('a synthetic claim that claims real-spool provenance', dict(claim_extra={'realSpoolProvenance': True}), 'does not say synthetic provenance'),
                         ('a synthetic seal that claims real-spool provenance', dict(sm_extra={'realSpoolProvenance': True}), 'does not say synthetic provenance'),
                         ('a synthetic claim with no realSpoolProvenance field', dict(claim_extra={'realSpoolProvenance': W.DROP}), 'does not say synthetic provenance'),
                         ('a synthetic seal with no realSpoolProvenance field', dict(sm_extra={'realSpoolProvenance': W.DROP}), 'does not say synthetic provenance'),
                         ('a synthetic claim with no provenance field', dict(claim_extra={'provenance': W.DROP}), 'does not say synthetic provenance'),
                         ('a real-schema claim placed in synthetic-claims/', dict(claim_extra={'schema': 'learn-input-claim/1'}), 'not this op\'s claim'),
                         ('a real-schema seal placed in synthetic-inputs/', dict(sm_extra={'schema': 'learn-input-snapshot/1'}), 'names another snapshot or source')):
    with tempfile.TemporaryDirectory() as tmp:
        r = go(syn_world(tmp, **kw), argv=SYN)
        check(needle in r.rec['snapshotStart'].get('error', '') and r.rec['schema'] == 'learn-op-retirement-failure/1', f'SYN: {what} is refused at the start and the op is a retirement failure')
with tempfile.TemporaryDirectory() as tmp:
    w = syn_world(tmp)
    w.at(10, lambda: open(os.path.join(w.view, 'synthetic-inputs', 'snap-1', 'passive-a/spool/one.json'), 'wb').write(b'{"a":2}'))
    r = go(w, argv=SYN)
    check(r.rec['retirement']['snapshot']['changed'] == ['passive-a/spool/one.json'] and r.rec['schema'] == 'learn-op-retirement-failure/1',
          'SYN: a synthetic sealed file changed during the run is found at retirement (the synthetic-inputs/ tree is the one re-hashed)')
with tempfile.TemporaryDirectory() as tmp:
    w = syn_world(tmp, keep_real=True)                          # a real claim for the same identity also exists: the declaration decides which is read
    open(os.path.join(w.view, 'inputs', 'snap-1', 'passive-a/spool/one.json'), 'wb').write(b'{"a":9}')
    r = go(w, argv=SYN)
    check(r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['retirement']['snapshot']['ok'], 'SYN: with both kinds present, a synthetic op reads only the synthetic records (a damaged real snapshot is not consulted)')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    for what, argv in (('--input real (only the synthetic declaration exists)', ['--id', W.OPID, '--source-identity', W.IDENT, '--input', 'real']),
                       ('--input with another value', ['--id', W.OPID, '--source-identity', W.IDENT, '--input', 'Synthetic']),
                       ('the declaration before the identity', ['--id', W.OPID, '--input', 'synthetic', '--source-identity', W.IDENT]),
                       ('a dangling --input', ['--id', W.OPID, '--source-identity', W.IDENT, '--input'])):
        r = go(w, argv=argv)
        check(r.code is not None and 'usage' in str(r.code) and r.rec is None, f'SYN: configure refuses {what} and writes nothing')


# ============================================================ pod operator r2 (the start and the recovery driver) against THIS finalizer
P2PATH = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'pod-operator', 'learn-pod-operator-r2.py')
spec2 = importlib.util.spec_from_file_location('pod2', P2PATH)
P2 = importlib.util.module_from_spec(spec2)
spec2.loader.exec_module(P2)
check(P2.PROD['finalizer_sha'] == FIN_SHA, 'pod operator r2 pins this finalizer\'s sha256')
# the finalizer unit r2 starts: its Environment= values and arguments are exactly what configure() accepts
fa = P2.fin_unit_argv({'pg_image': W.PGIMG, 'python': '/usr/bin/python3', 'fin': FIN}, W.OPID, W.IDENT, W.NONCE)
fenv = dict(x[len('Environment='):].split('=', 1) for x in fa if x.startswith('Environment='))
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    F.configure(fa[fa.index(FIN) + 1:], fenv, 30034, home=w.home, view=w.view, production=False)
    check(F.OPID == W.OPID and F.IDENT == W.IDENT and F.NONCE == W.NONCE and F.PGIMG == W.PGIMG, 'r2\'s finalizer unit argv and environment pass configure() (op id, source identity, nonce, pg image)')
    penv = {'python': '/usr/bin/python3', 'self': '/s', 'cred_root': F.DEFAULTS['cred_root'], 'volume_root': F.DEFAULTS['volume_root'], 'view': '/v', 'seccomp': F.SECCOMP,
            'app_image': 'a1' * 32, 'pg_image': W.PGIMG, 'plan': P2.PLAN}
    pu = P2.pod_unit_argv(penv, W.OPID, W.IDENT, W.NONCE)
    names = {s_: (lambda a: a[a.index('--name') + 1])(P2.ctr_argv(penv, W.OPID, 'pg' if s_ == 'pg' else 'hold', s_)) for s_ in P2.SLOTS}
    check(pu[3] + '.service' == F.OPU and f'BindsTo={F.FINU}' in pu and names == dict(F.CTRS, pg=F.PGC), 'r2 names the pod unit and the four containers as this finalizer expects')
    check(F.CRED == f"{penv['cred_root']}/{W.OPID}" and any(x == f'ExecStopPost=-/usr/bin/podman unshare rm -rf --one-file-system {F.CRED}' for x in pu),
          'r2\'s credential tmpfs is the path the finalizer checks is gone, and the pod unit removes it on stop')
    vol = [x for x in P2.ctr_argv(penv, W.OPID, 'pg', 'pg') if x.endswith(':/pgdata:rw,nosuid,nodev,noexec')]
    check(vol == [f'{F.VOLUME}/pgdata:/pgdata:rw,nosuid,nodev,noexec'], 'r2 mounts pgdata from the volume the finalizer samples and pg_controldata reads')
    replay = P2.ctr_argv(penv, W.OPID, 'feed-replay', 'feed', 'snap-1')
    check(P2.mounts_of(replay)[-1]['destination'] == '/in' and sum(1 for m in P2.mounts_of(replay) if m['destination'] == '/in') == 1,
          'r2\'s replay container mounts the snapshot at /in, the destination the finalizer\'s containersStartedWithInput keys on')
# r2's checkpoint request is served by this finalizer, and r2 accepts the real ack and reads the real custody back
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp, done_at=None)
    ckd = os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID)
    for role in W.ROLES:
        w.put(role, 'steps/01-init/stdout', b'{"init":1}\n')
        w.put(role, 'steps/complete', b'')
    w.at(6, lambda: P2.write_request(ckd, 1, 'pre-kill-host'))
    w.at(12, lambda: [w.finish_role(x, 143 if x != 'feed' else 0, 'signalled' if x != 'feed' else 'ok') for x in W.ROLES])
    r = go(w)
    raw, body = open(os.path.join(ckd, 'ack-1'), 'rb').read(), open(os.path.join(ckd, 'req-1'), 'rb').read()
    check(r.rec['schema'] == 'learn-op-pod-success/1' and r.rec['checkpoints'][0]['label'] == 'pre-kill-host', 'the real finalizer serves r2\'s request and the run still succeeds')
    check(P2.ack_problems(raw, 1, body) == [], 'r2 accepts the real finalizer\'s ack-1 (n, request sha256, ok, every role accepted, chain head)')
    check(P2.ack_problems(raw, 1, body + b' ') != [] and P2.ack_problems(raw, 2, body) != [], '... and refuses it for another request body or another n')
    gen = {'steps': [('init', ['x'])], 'service': ['y']}
    rb = {role: P2.readback_extract(os.path.join(r.cust, 'checkpoints', '1', f'extract-{role}'), role, gen) for role in W.ROLES}
    check(all(v['phase'] == 'ready' and v['files'] >= 3 for v in rb.values()), 'r2 reads the real custody extract back: status, step evidence and file manifest per role')
    os.unlink(os.path.join(r.cust, 'checkpoints', '1', 'extract-host', 'steps', 'complete'))
    try:
        P2.readback_extract(os.path.join(r.cust, 'checkpoints', '1', 'extract-host'), 'host', gen)
        check(False, 'r2 refuses a custody extract without the step evidence')
    except P2.Abort:
        check(True, 'r2 refuses a custody extract without the step evidence')
with tempfile.TemporaryDirectory() as tmp:
    w = world(tmp)
    w.ackfix = None
    w.at(6, lambda: (w.containers['learn-pod-op-pod-1-recv'].update(state='exited'), P2.write_request(os.path.join(w.home, '.local', 'state', 'learn-checkpoint', W.OPID), 1, 'x')))
    r = go(w)
    raw = open(os.path.join(r.ck, 'ack-1'), 'rb').read()
    check(any('roles' in x for x in P2.ack_problems(raw, 1, open(os.path.join(r.ck, 'req-1'), 'rb').read())), 'r2 refuses the real ack when a role was not running (rejected:identity): why r2 checkpoints only with all three running')
check(F.OK_REASONS == {'host': ('ok', 'signalled'), 'recv': ('ok', 'signalled'), 'feed': ('ok',)} and P2.PLAN['generations']['feed-replay']['service'] is None
      and P2.PLAN['actions'][-2:] == [['stop', 'host'], ['stop', 'recv']], 'r2 ends host and recv with TERM (signalled) and feed by itself (ok): the reasons this finalizer accepts')
print(f'learn-finalizer-r5: {n} assertions passed')

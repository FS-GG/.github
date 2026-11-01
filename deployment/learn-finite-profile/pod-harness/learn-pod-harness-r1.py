#!/usr/bin/python3 -I
"""LEARN finite executor: the fdev POD HARNESS r1 (Refs FS-GG/.github#5184, FS-GG/.github#4647). It replaces Home's harness r3b, whose source is not in
custody (design docs/coordination/2026-10-30-learn-finite-executor-pod-design.md, Unknown 7). THIS FILE IS SOURCE: nothing here has run on fdev. Root
installs it from a root-owned copy and admits every run; the `plan` command prints the admission request.

    learn-pod-harness-r1.py plan    --run-id <id> [--argv-json <synthetic argv>] [--source-identity <64 hex>]     (any uid; no effect)
    learn-pod-harness-r1.py case    --run-id <id> --case <case> --admission-ref <comment URL> [--argv-json ...] [--source-identity ...]   (uid 30034)
    learn-pod-harness-r1.py summary --run-id <id>                                                                  (uid 30034; read only)
    learn-pod-harness-r1.py synth-tree --out <new dir> --nonce <nonce>                                            (any uid; writes only <new dir>)
    learn-pod-harness-r1.py spike-cgroups --run-id <id> --nonce <hex>   (internal: the main process of learn-pod-hq-<id>-n5b.service, uid 30034)

The cases, in order (`plan` lists each with what runs, as which uid, what it may create or write, the expected verdict and the receipts):
  phase A, operator r6 re-qualification (design section 10): census; a27-unacked (the f1-pre failure record refuses while its acknowledgement is absent);
           a0-summary (A27 positive: failures == acked == [f1-pre], unacked == [], the precheck op retires); exclusion-reader (an active learn-pod-* unit
           refuses a reader op); a29-cap (two active learn-op-* units refuse a third op); a0-r4-smoke is HELD (Home's A0 r4 source is not in custody).
  phase B, the N5b spike: n5b-cgroups (inside a delegated learn-pod-* unit, a pod whose members run with --cgroups=split: are the members, their conmon
           and the infra container under the unit's cgroup?).
  phase C, the synthetic-snapshot pod qualification, which is also the install receipt op: syn-precheck, syn-run (the whole plan, the recovery driver
           included, on a synthetic claim), syn-reuse. HELD until the pod operator's image pins exist.

Rules that make the harness safe to admit as one finite sequence: it runs only as uid 30034 from a root-owned copy; every name it creates is
hq-<run-id>-<case>; it stops and resets only units it started, by exact name; every unit it starts has its own RuntimeMaxSec, so nothing outlives a dead
harness; a census before and after each case must be clean; each case's RESULT is written once (O_EXCL); the first failed case writes STOP and every
later case refuses. A case never retries. Cooperative: it detects mistakes and does not defend against Root.
"""
import hashlib, json, os, re, secrets, stat, subprocess, sys, time
from datetime import datetime, timezone

SCHEMA = 'learn-pod-harness/1'
PLAN_SCHEMA = 'learn-pod-harness-plan/1'
SPIKE_SCHEMA = 'learn-pod-harness-spike/1'
REVISION = 'r1'
RUN_ID = re.compile(r'^[a-z0-9]{4,12}$')
HEX64 = re.compile(r'^[0-9a-f]{64}$')
COMMENT_URL = re.compile(r'^https://github\.com/FS-GG/[A-Za-z0-9._-]+/(?:issues|pull)/[0-9]+#issuecomment-[0-9]+$')
F1_PRE = 'a0-20261022t192443z-f1-pre'
F1_PRE_SHA = '3db3a5f38aec1a01d2d5fbbb471bb272c44fc88c633537523245a67620b7210e'
R6_SHA = '16b6da1e576222353318a73dc63268533e655995dd9ad5afe0239237e58ce035'           # operator r6 as INSTALL-FDEV installs it
READER_IMAGE = '6625dfb774feabc256d94d15d112cf3f554d3cd7d388e1d717d74ada97c1de06'      # reader image r4: operator r6's IMAGE_ID (its first gate)
BASE_IMAGE = '20a6df20169ce6721ed91fddd0d24ae7fe8de655008b422df0c3299821b7d4b8'        # learn-qualification:v1, already in the account's storage
SYNTHETIC_TAG = 'input:synthetic'
DUMMY_S, RECORD_WAIT_S, POD_RECORD_WAIT_S = 240, 300, 2160
SPIKE_RUNTIME_S, SPIKE_SLEEP_S = 300, 180

PROD = {
    'uid': 30034, 'owner_uid': 0,
    'home': '/mnt/storage/containers/learn-qualification/account',
    'view': '/usr/local/libexec/fsgg-learn',
    'self': '/usr/local/libexec/fsgg-learn/learn-pod-harness-r1.py',
    'r6': '/usr/local/libexec/fsgg-learn/learn-operator-r6.py',
    'pod': '/usr/local/libexec/fsgg-learn/learn-pod-operator-r2.py',
    'python': '/usr/bin/python3',
    'proc': '/proc', 'cgroot': '/sys/fs/cgroup',
    'production': True,
}


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


def sha_bytes(b):
    return hashlib.sha256(b).hexdigest()


def state_dir(env, run_id):
    return f"{env['home']}/.local/state/learn-harness/{run_id}"


def op(run_id, case):
    return f'hq-{run_id}-{case}'


# ============================================================ the cases: the admission request is generated from this table
def r6_argv(env, opid):
    return [env['python'], '-I', env['r6'], '--precheck-only', '--id', opid]


def pod_argv(env, mode, opid, argv_json, ident):
    return [env['python'], '-I', env['pod'], mode, '--id', opid, '--argv-json', argv_json, '--source-identity', ident]


def dummy_argv(name, seconds=DUMMY_S):
    """A transient user unit that does nothing but exist (sleep), bounded by its own RuntimeMaxSec so it cannot outlive a dead harness."""
    return ['systemd-run', '--user', '--unit', name, '-p', f'RuntimeMaxSec={seconds + 30}', '-p', 'TasksMax=4', '-p', 'MemoryMax=16M', '--', '/usr/bin/sleep', str(seconds)]


def spike_unit_argv(env, run_id, nonce='<a fresh 128-bit hex nonce per invocation>'):
    """The N5b spike runs INSIDE a learn-pod-* unit with the pod unit's cgroup properties (Delegate, KillMode), so the question it answers is the one the
    pod operator's real pod unit poses. While it runs, its name makes both operators (r6 and the pod one) refuse."""
    return ['systemd-run', '--user', '--unit', f'learn-pod-{op(run_id, "n5b")}', '--wait', '--collect', '-p', 'Delegate=yes', '-p', 'KillMode=control-group',
            '-p', f'RuntimeMaxSec={SPIKE_RUNTIME_S}', '-p', 'TasksMax=256', '-p', 'MemoryMax=1G', '-p', 'MemorySwapMax=0',
            '--', env['python'], '-I', env['self'], 'spike-cgroups', '--run-id', run_id, '--nonce', nonce]


def cases(env, run_id, argv_json=None, ident=None):
    """Every case in order, with its exact commands, effects, expected verdict and receipts. `held` names what a case waits for."""
    st = state_dir(env, run_id)
    ev = f"{env['home']}/.local/state/learn-evidence"
    aj, idt = argv_json or '<the synthetic argv-json Root admits>', ident or '<the synthetic source identity>'
    census_cmds = [['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-*'], ['podman', 'pod', 'ps', '-a', '--format', '{{.Name}}'],
                   ['podman', 'ps', '-a', '--filter', 'name=^learn-', '--format', '{{.Names}}']]
    syn_op = op(run_id, 'syn')
    c = [
        {'id': 'census', 'phase': 'A', 'title': 'live readback before anything runs',
         'commands': census_cmds + [['podman', 'image', 'exists', READER_IMAGE], ['podman', 'image', 'exists', BASE_IMAGE]],
         'creates': [], 'writes': [f'{st}/census.json'],
         'expected': 'no learn-* unit other than inactive, no pod or container named learn-*, reader image r4 and the base image present, operator r6 hashes to '
                     f'{R6_SHA[:8]}', 'receipts': ['census.json sha256']},
        {'id': 'a27-unacked', 'phase': 'A', 'title': 'A27: an unacknowledged retirement failure refuses (run BEFORE INSTALL-FDEV step 2 restores the ack)',
         'precondition': f'no {F1_PRE}.retirement-ack in the persistent retirement-acks directory',
         'commands': [r6_argv(env, op(run_id, 'a27'))], 'creates': [], 'writes': [f'{st}/a27-unacked.json'],
         'expected': f'operator r6 exits 5 with exactly ["retirement-failure-unacknowledged: {F1_PRE}"], unacked == [{F1_PRE}], no precheck op started',
         'receipts': ['the RESULT sha256', 'census after: clean']},
        {'id': 'a0-summary', 'phase': 'A', 'title': 'A27 positive and the A0 summary (run AFTER INSTALL-FDEV step 2)',
         'precondition': f'{F1_PRE}.retirement-ack present with content {F1_PRE_SHA[:8]}',
         'commands': [r6_argv(env, op(run_id, 'a0'))],
         'creates': [f'learn-op-{op(run_id, "a0")}-pre.service', f'learn-fin-{op(run_id, "a0")}-pre.service', f'container learn-op-{op(run_id, "a0")}-pre (reader image r4)'],
         'writes': [f'{st}/a0-summary.json', f'{ev}/{op(run_id, "a0")}-pre/ (finalizer r4)', f"{env['home']}/.local/state/learn-custody/{op(run_id, 'a0')}-pre/ (finalizer r4)"],
         'expected': f'exit 0; retirementAcks failures == acked == [{F1_PRE}], unacked == []; every precheck assertion true; the precheck record is '
                     'learn-op-success/1 bound to the run nonce; census after: clean',
         'receipts': ['the RESULT sha256', 'the precheck record sha256', 'census after: clean']},
        {'id': 'exclusion-reader', 'phase': 'A', 'title': 'exclusion: an active learn-pod-* unit refuses a reader op',
         'commands': [dummy_argv(f'learn-pod-{op(run_id, "x")}'), r6_argv(env, op(run_id, 'ex')), ['systemctl', '--user', 'stop', f'learn-pod-{op(run_id, "x")}.service']],
         'creates': [f'learn-pod-{op(run_id, "x")}.service (sleep {DUMMY_S}, RuntimeMaxSec {DUMMY_S + 30})'], 'writes': [f'{st}/exclusion-reader.json'],
         'expected': f'exit 5 with "exclusion refused: a pod op is active: learn-pod-{op(run_id, "x")}"; the dummy unit stopped; census after: clean',
         'receipts': ['the RESULT sha256', 'census after: clean']},
        {'id': 'a29-cap', 'phase': 'A', 'title': 'A29: two active ops refuse a third',
         'commands': [dummy_argv(f'learn-op-{op(run_id, "c1")}'), dummy_argv(f'learn-op-{op(run_id, "c2")}'), r6_argv(env, op(run_id, 'cap')),
                      ['systemctl', '--user', 'stop', f'learn-op-{op(run_id, "c1")}.service', f'learn-op-{op(run_id, "c2")}.service']],
         'creates': [f'learn-op-{op(run_id, "c1")}.service', f'learn-op-{op(run_id, "c2")}.service'], 'writes': [f'{st}/a29-cap.json'],
         'expected': 'exit 5 with "concurrency refused: 2 ops already active (max 2)"; both dummy units stopped; census after: clean',
         'receipts': ['the RESULT sha256', 'census after: clean']},
        {'id': 'a0-r4-smoke', 'phase': 'A', 'title': 'the A0 r4 9/9 smoke against the hash of r6',
         'held': 'Home\'s A0 r4 harness (learn-c7-a0-r4.py, 12ec5ebf) drives the F1-F7 fault cases against the reader image and pins operator r5\'s sha256; its '
                 'source is not in this repository. Re-pinning it to r6 needs that source from custody (Home-only unless Root holds a copy)',
         'commands': [], 'creates': [], 'writes': [], 'expected': 'n/a', 'receipts': []},
        {'id': 'n5b-cgroups', 'phase': 'B', 'title': 'N5b: are --cgroups=split pod members, their conmon and the infra container under the pod unit\'s cgroup?',
         'commands': [spike_unit_argv(env, run_id)],
         'creates': [f'learn-pod-{op(run_id, "n5b")}.service (Delegate=yes, KillMode=control-group, RuntimeMaxSec {SPIKE_RUNTIME_S})',
                     f'pod learn-pod-{op(run_id, "n5b")} (--network=none) with infra and two containers of the base image running sleep {SPIKE_SLEEP_S}'],
         'writes': [f'{st}/n5b-spike.json', f'{st}/n5b-cgroups.json'],
         'expected': 'the spike completes, removes its pod and reads it back gone; verdict members-and-infra-under-unit (else the finding not-under-unit, which '
                     'blocks phase C until the pod start is changed); census after: clean',
         'receipts': ['the spike RESULT sha256', 'every member\'s cgroup path', 'census after: clean']},
        {'id': 'syn-precheck', 'phase': 'C', 'title': 'the synthetic op\'s non-input precheck (the install receipt op begins here)',
         'held': 'the image pins of the pod start (after INSTALL-FDEV step 7)',
         'precondition': 'Root: synthetic seal (learn_seal_claim.py --mode synthetic manifest and seal of a synth-tree), the admission for '
                         f'{syn_op} with the synthetic argv-json, the loop volume',
         'commands': [pod_argv(env, '--precheck-only', syn_op, aj, idt)], 'creates': [f'pod learn-pre-{syn_op} (removed by the operator)'],
         'writes': [f'{st}/syn-precheck.json'],
         'expected': 'exit 0, inputProvenance synthetic, inputMounted false; then Root takes the synthetic claim from this RESULT',
         'receipts': ['the RESULT sha256']},
        {'id': 'syn-run', 'phase': 'C', 'title': 'the whole plan on a synthetic claim: the recovery driver end to end, without a real claim',
         'held': 'as syn-precheck', 'precondition': 'Root: the synthetic claim (learn_seal_claim.py --mode synthetic claim) from the syn-precheck RESULT',
         'commands': [pod_argv(env, '--run', syn_op, aj, idt)],
         'creates': [f'learn-fin-{syn_op}.service', f'learn-pod-{syn_op}.service', f'pod learn-pod-{syn_op} and its containers', 'the input-read marker'],
         'writes': [f'{st}/syn-run.json', f'{ev}/{syn_op}/ (finalizer r5)'],
         'expected': 'exit 0; the finalizer record is learn-op-pod-success/1 with inputProvenance synthetic, inputRead true, the feed container the only one '
                     'started with /in, five accepted checkpoints and the pod unit ExecMainStatus 0; drive.jsonl ends complete; census after: clean',
         'receipts': ['the RESULT sha256', 'the finalizer record sha256', 'the drive.jsonl chain head', 'census after: clean']},
        {'id': 'syn-reuse', 'phase': 'C', 'title': 'an op id is used once', 'held': 'as syn-precheck',
         'commands': [pod_argv(env, '--precheck-only', syn_op, aj, idt)], 'creates': [], 'writes': [f'{st}/syn-reuse.json'],
         'expected': 'exit 5 with "op id already used"', 'receipts': ['the RESULT sha256']},
    ]
    for x in c:
        x.setdefault('uid', env['uid'])
        x.setdefault('held', None)
        x.setdefault('precondition', None)
    return c


CASE_IDS = ('census', 'a27-unacked', 'a0-summary', 'exclusion-reader', 'a29-cap', 'a0-r4-smoke', 'n5b-cgroups', 'syn-precheck', 'syn-run', 'syn-reuse')


def plan(env, run_id, argv_json=None, ident=None, self_sha=None):
    cs = cases(env, run_id, argv_json, ident)
    body = {'schema': PLAN_SCHEMA, 'harnessRevision': REVISION, 'harnessSha256': self_sha, 'runId': run_id, 'uid': env['uid'],
            'install': {'path': env['self'], 'mode': '0755', 'owner': 'root:root'},
            'stateDir': state_dir(env, run_id), 'order': [x['id'] for x in cs], 'cases': cs,
            'rules': ['one case per invocation; a case never retries', 'the first failed case writes STOP; later cases refuse',
                      'a census before and after each case must be clean', f'every name created is hq-{run_id}-*',
                      'units are stopped and reset by exact name only, and each has its own RuntimeMaxSec']}
    body['casesSha256'] = sha_bytes(json.dumps(cs, sort_keys=True, separators=(',', ':')).encode())
    return body


# ============================================================ the census and the units the harness owns
def census(deps):
    run = deps['run']
    rc1, units, _ = run(['systemctl', '--user', 'list-units', '--all', '--no-legend', '--plain', 'learn-*'], 30)
    rc2, pods, _ = run(['podman', 'pod', 'ps', '-a', '--format', '{{.Name}}'], 30)
    rc3, ctrs, _ = run(['podman', 'ps', '-a', '--filter', 'name=^learn-', '--format', '{{.Names}}'], 30)
    rows = [l.replace('●', ' ').split() for l in (units or '').splitlines() if l.strip()]
    rows = [r_ for r_ in rows if len(r_) >= 4]
    busy = sorted(r_[0] for r_ in rows if r_[2] != 'inactive')
    pods_ = sorted(p for p in (pods or '').split() if p.startswith(('learn-pod-', 'learn-pre-', 'learn-op-')))
    ctrs_ = sorted((ctrs or '').split())
    ok = rc1 == 0 and rc2 == 0 and rc3 == 0 and not busy and not pods_ and not ctrs_
    return {'ok': ok, 'rc': [rc1, rc2, rc3], 'busyUnits': busy, 'pods': pods_, 'containers': ctrs_}


def operator_result(out):
    try:
        return json.loads(out.split('RESULT-BEGIN\n', 1)[1].split('RESULT-END', 1)[0])
    except (IndexError, ValueError):
        return None


def start_dummy(deps, name):
    rc, _, err = deps['run'](dummy_argv(name), 30)
    if rc != 0:
        refuse(f'the dummy unit {name} did not start: {err[-200:]}')
    t = deps['monotonic']()
    while deps['monotonic']() - t < 30:
        if deps['run'](['systemctl', '--user', 'is-active', f'{name}.service'], 10)[1].strip() == 'active':
            return
        deps['sleep'](1)
    refuse(f'the dummy unit {name} is not active')


def stop_dummies(deps, names):
    """Stop and reset ONLY the named units this harness started, then read each back: inactive, or unknown to systemd (a collected transient unit)."""
    out = {}
    for n_ in names:
        deps['run'](['systemctl', '--user', 'stop', f'{n_}.service'], 30)
        deps['run'](['systemctl', '--user', 'reset-failed', f'{n_}.service'], 10)
        state = deps['run'](['systemctl', '--user', 'is-active', f'{n_}.service'], 10)[1].strip()
        out[n_] = state
    return out, all(v in ('inactive', 'unknown', '') for v in out.values())


def run_r6(env, deps, opid):
    rc, out, err = deps['run'](r6_argv(env, opid), 600)
    return rc, operator_result(out), sha_bytes(out.encode())


def errs(r):
    return (r or {}).get('errors') or []


# ============================================================ case executors: each returns (verdict, facts). verdict: pass | fail | finding | not-applicable
def c_census(env, deps, run_id, a):
    cz = census(deps)
    imgs = {i: deps['run'](['podman', 'image', 'exists', i], 30)[0] == 0 for i in (READER_IMAGE, BASE_IMAGE)}
    r6 = deps['sha_file'](env['r6'])
    ok = cz['ok'] and all(imgs.values()) and r6 == R6_SHA
    return ('pass' if ok else 'fail'), {'census': cz, 'images': imgs, 'operatorR6Sha256': r6}


def ack_present(env, deps):
    return deps['exists'](f"{env['view']}/retirement-acks/{F1_PRE}.retirement-ack")


def c_a27(env, deps, run_id, a):
    if ack_present(env, deps):
        return 'not-applicable', {'why': f'the {F1_PRE} acknowledgement already exists: A27 negative runs only before INSTALL-FDEV step 2'}
    rc, r, rsha = run_r6(env, deps, op(run_id, 'a27'))
    acks = ((r or {}).get('gates') or {}).get('retirementAcks') or {}
    ok = rc == 5 and r is not None and r.get('exitCode') == 5 and errs(r) == [f'retirement-failure-unacknowledged: {F1_PRE}'] and acks.get('unacked') == [F1_PRE] \
        and 'precheck' not in r
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r), 'retirementAcks': acks}


def c_a0(env, deps, run_id, a):
    if not ack_present(env, deps):
        return 'fail', {'why': f'no {F1_PRE} acknowledgement: INSTALL-FDEV step 2 has not run'}
    rc, r, rsha = run_r6(env, deps, op(run_id, 'a0'))
    acks = ((r or {}).get('gates') or {}).get('retirementAcks') or {}
    pre = (r or {}).get('precheck') or {}
    rec_path = f"{env['home']}/.local/state/learn-evidence/{op(run_id, 'a0')}-pre/record.json"
    rec = deps['read_json'](rec_path)
    ok = (rc == 0 and r is not None and r.get('exitCode') == 0 and errs(r) == [] and acks.get('failures') == [F1_PRE] and acks.get('acked') == [F1_PRE]
          and acks.get('unacked') == [] and all((pre.get('assertions') or {}).values()) and bool(pre.get('assertions')) and pre.get('recordBound') is True
          and (rec or {}).get('schema') == 'learn-op-success/1')
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r), 'retirementAcks': acks, 'precheckAssertions': pre.get('assertions'),
                                        'precheckRecordSchema': (rec or {}).get('schema'), 'precheckRecordSha256': deps['sha_file'](rec_path) if rec else None}


def c_exclusion(env, deps, run_id, a):
    dummy = f'learn-pod-{op(run_id, "x")}'
    started, r, rc, rsha = [], None, None, None
    try:
        start_dummy(deps, dummy)
        started.append(dummy)
        rc, r, rsha = run_r6(env, deps, op(run_id, 'ex'))
    finally:
        stopped, clean = stop_dummies(deps, started or [dummy])
    ok = rc == 5 and r is not None and f'exclusion refused: a pod op is active: {dummy}' in errs(r) \
        and ((r.get('gates') or {}).get('exclusion') or {}).get('activePods') == [dummy] and clean
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r), 'dummyStates': stopped}


def c_cap(env, deps, run_id, a):
    names = [f'learn-op-{op(run_id, "c1")}', f'learn-op-{op(run_id, "c2")}']
    started, r, rc, rsha = [], None, None, None
    try:
        for n_ in names:
            start_dummy(deps, n_)
            started.append(n_)
        rc, r, rsha = run_r6(env, deps, op(run_id, 'cap'))
    finally:
        stopped, clean = stop_dummies(deps, names)
    ok = rc == 5 and r is not None and 'concurrency refused: 2 ops already active (max 2)' in errs(r) and clean
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r), 'dummyStates': stopped}


def c_held(env, deps, run_id, a):
    return 'held', {}


def n5b_prestart(env, deps, run_id, a):
    """N1 (#5204): a spike RESULT that exists before this invocation (an earlier or aborted spike) is never read as this one's."""
    if deps['exists'](f'{state_dir(env, run_id)}/n5b-spike.json'):
        refuse('a spike RESULT already exists for this run: it is not this invocation\'s')


def c_n5b(env, deps, run_id, a):
    nonce = deps['nonce']()
    rc, out, err = deps['run'](spike_unit_argv(env, run_id, nonce), SPIKE_RUNTIME_S + 60)
    sp = deps['read_json'](f'{state_dir(env, run_id)}/n5b-spike.json')
    facts = {'unitRc': rc, 'spike': sp, 'nonceSha256': sha_bytes(nonce.encode()),
             'spikeSha256': deps['sha_file'](f'{state_dir(env, run_id)}/n5b-spike.json') if sp else None}
    if (rc != 0 or not sp or sp.get('schema') != SPIKE_SCHEMA or sp.get('nonce') != nonce or sp.get('verdict') not in SPIKE_VERDICTS
            or not sp.get('podRemoved') or sp.get('leftoverProcs')):
        return 'fail', facts
    return ('pass' if sp['verdict'] == 'members-and-infra-under-unit' else 'finding'), facts


def synthetic_args(a):
    aj, ident = a.get('--argv-json'), a.get('--source-identity')
    try:
        al = json.loads(aj or '')
    except ValueError:
        al = None
    if not (isinstance(al, list) and len(al) == 3 and al[2] == SYNTHETIC_TAG) or not HEX64.match(ident or ''):
        refuse('the synthetic cases need --argv-json naming a synthetic op (third element input:synthetic) and --source-identity')
    return aj, ident


def run_pod(env, deps, mode, opid, aj, ident):
    rc, out, err = deps['run'](pod_argv(env, mode, opid, aj, ident), 900)
    return rc, operator_result(out), sha_bytes(out.encode())


def c_syn_precheck(env, deps, run_id, a):
    aj, ident = synthetic_args(a)
    rc, r, rsha = run_pod(env, deps, '--precheck-only', op(run_id, 'syn'), aj, ident)
    ok = rc == 0 and r is not None and r.get('exitCode') == 0 and errs(r) == [] and r.get('inputProvenance') == 'synthetic' \
        and (r.get('gates') or {}).get('inputMounted') is False
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r)}


def c_syn_run(env, deps, run_id, a):
    aj, ident = synthetic_args(a)
    opid = op(run_id, 'syn')
    rc, r, rsha = run_pod(env, deps, '--run', opid, aj, ident)
    facts = {'rc': rc, 'resultSha256': rsha, 'errors': errs(r)}
    if not (rc == 0 and r is not None and r.get('inputProvenance') == 'synthetic' and (r.get('gates') or {}).get('inputMounted') is True):
        return 'fail', facts
    rp = f"{env['home']}/.local/state/learn-evidence/{opid}/record.json"
    t, rec = deps['monotonic'](), None
    while deps['monotonic']() - t < POD_RECORD_WAIT_S:
        rec = deps['read_json'](rp)
        if rec is not None:
            break
        deps['sleep'](10)
    drive = deps['read_lines'](f"{env['home']}/.local/state/learn-drive/{opid}/drive.jsonl")
    last = drive[-1] if drive else {}
    cks = (rec or {}).get('checkpoints') or []
    facts.update(recordSchema=(rec or {}).get('schema'), recordSha256=deps['sha_file'](rp) if rec else None, driveEnd=last, checkpoints=len(cks))
    ok = (rec is not None and rec.get('schema') == 'learn-op-pod-success/1' and rec.get('inputProvenance') == 'synthetic' and rec.get('inputRead') is True
          and rec.get('containersStartedWithInput') == [f'learn-pod-{opid}-feed'] and len(cks) == 5
          and all(c_.get('roles') and all(v['custody'].get('verdict') == 'accepted' for v in c_['roles'].values()) for c_ in cks)
          and str(((rec.get('opUnit') or {}).get('ExecMainStatus'))) == '0' and last.get('event') == 'end' and last.get('verdict') == 'complete')
    return ('pass' if ok else 'fail'), facts


def c_syn_reuse(env, deps, run_id, a):
    aj, ident = synthetic_args(a)
    rc, r, rsha = run_pod(env, deps, '--precheck-only', op(run_id, 'syn'), aj, ident)
    ok = rc == 5 and any('op id already used' in e for e in errs(r))
    return ('pass' if ok else 'fail'), {'rc': rc, 'resultSha256': rsha, 'errors': errs(r)}


SPIKE_VERDICTS = ('members-and-infra-under-unit', 'not-under-unit', 'split-refused')     # 'incomplete' is never one of them
PRESTART = {'n5b-cgroups': n5b_prestart}
EXECUTORS = {'census': c_census, 'a27-unacked': c_a27, 'a0-summary': c_a0, 'exclusion-reader': c_exclusion, 'a29-cap': c_cap, 'a0-r4-smoke': c_held,
             'n5b-cgroups': c_n5b, 'syn-precheck': c_syn_precheck, 'syn-run': c_syn_run, 'syn-reuse': c_syn_reuse}
NO_CENSUS_BEFORE = ('census',)


def write_once(path, data):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
    with os.fdopen(fd, 'wb') as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())


def run_case(env, deps, run_id, case, a):
    """One case: the gates, the census before, the executor, the census after, the RESULT written once; a failure writes STOP."""
    R = {'schema': SCHEMA, 'harnessRevision': REVISION, 'runId': run_id, 'case': case, 'utc': deps['now']().isoformat(), 'errors': []}
    code, executing, guarded = 5, False, False
    try:
        if deps['geteuid']() != env['uid']:
            refuse(f"run as uid {env['uid']}")
        if not COMMENT_URL.match(a.get('--admission-ref') or ''):
            refuse('--admission-ref must be the GitHub comment URL of Root\'s admission')
        R['admissionRef'] = a['--admission-ref']
        me = deps['self_stat']()
        if env['production'] and (me.st_uid != env['owner_uid'] or stat.S_IMODE(me.st_mode) & 0o022):
            refuse('the harness must run from a root-owned copy closed to group and other writers')
        R['harnessSha256'] = deps['self_sha']()
        spec = next(x for x in cases(env, run_id) if x['id'] == case)
        if spec['held']:
            R.update(verdict='held', held=spec['held'], exitCode=0)
            return R, 0
        guarded = True                                           # from here every path takes the census after (N2, #5204)
        if case.startswith('syn-'):
            synthetic_args(a)                                    # checked before anything runs
        sd = state_dir(env, run_id)
        os.makedirs(sd, 0o700, exist_ok=True)
        if os.path.lexists(f'{sd}/STOP'):
            refuse('an earlier case of this run failed (STOP): nothing more runs')
        if os.path.lexists(f'{sd}/{case}.json'):
            refuse('this case already ran in this run')
        if case not in NO_CENSUS_BEFORE:
            R['censusBefore'] = census(deps)
            if not R['censusBefore']['ok']:
                refuse('the census before the case is not clean')
        if case in PRESTART:
            PRESTART[case](env, deps, run_id, a)                 # refusals that need nothing to have run
        executing = True                                         # from here a refusal is a failure: something may have run
        verdict, facts = EXECUTORS[case](env, deps, run_id, a)
        R['facts'] = facts
        R['verdict'] = verdict
        code = 0 if verdict in ('pass', 'finding', 'not-applicable') else 3
    except Refuse as e:
        R['errors'].append(str(e))
        R['verdict'] = 'fail' if executing else 'refused'
        code = 3 if executing else 5
    except Exception as e:
        R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}')
        R['verdict'] = 'fail'
        code = 3
    if guarded:                                                  # every path, a refusal or an exception included: what is left behind is recorded
        try:
            R['censusAfter'] = census(deps)
        except Exception as e:
            R['censusAfter'] = {'ok': False, 'error': f'{type(e).__name__}: {str(e)[:200]}'}
        if not R['censusAfter']['ok'] and R.get('verdict') in ('pass', 'finding'):
            R['verdict'], code = 'fail', 3
            R['errors'].append('the census after the case is not clean')
    R['exitCode'] = code
    sd = state_dir(env, run_id)
    if R.get('verdict') in ('pass', 'fail', 'finding', 'not-applicable') and os.path.isdir(sd):
        try:
            write_once(f'{sd}/{case}.json', json.dumps(R, sort_keys=True).encode())
        except FileExistsError:
            R['errors'].append('the case RESULT already exists')
            code = R['exitCode'] = 3
        if R['verdict'] == 'fail':
            try:
                write_once(f'{sd}/STOP', case.encode())
            except FileExistsError:
                pass
    return R, code


def summary(env, run_id, read_json):
    sd = state_dir(env, run_id)
    out = {'schema': 'learn-pod-harness-summary/1', 'runId': run_id, 'cases': {}, 'stop': None}
    for cid in CASE_IDS:
        r = read_json(f'{sd}/{cid}.json')
        out['cases'][cid] = (r or {}).get('verdict', 'not-run')
    try:
        out['stop'] = open(f'{sd}/STOP').read().strip() or None
    except OSError:
        out['stop'] = None
    counts = {}
    for v in out['cases'].values():
        counts[v] = counts.get(v, 0) + 1
    out['counts'] = counts
    return out


# ============================================================ the N5b spike (runs INSIDE learn-pod-hq-<id>-n5b.service)
def under(path, root):
    return path == root or path.startswith(root.rstrip('/') + '/')


def cgroup_of(env, deps, pid):
    try:
        return next(l.split('::', 1)[1].strip() for l in deps['read'](f"{env['proc']}/{pid}/cgroup").splitlines() if l.startswith('0::'))
    except (OSError, StopIteration, ValueError):
        return None


def procs_under(env, deps, cg_rel):
    """Every pid in the unit's cgroup tree (cgroup.procs of each directory below it)."""
    root = env['cgroot'] + cg_rel
    pids = []
    for d, _, fs in deps['walk'](root):
        if 'cgroup.procs' in fs:
            try:
                pids += [int(x) for x in deps['read'](f'{d}/cgroup.procs').split()]
            except (OSError, ValueError):
                pids.append(-1)
    return sorted(pids)


def spike_cgroups(env, deps, run_id, nonce):
    pod = f'learn-pod-{op(run_id, "n5b")}'
    unit = f'{pod}.service'
    S = {'schema': SPIKE_SCHEMA, 'runId': run_id, 'nonce': nonce, 'pod': pod, 'members': {}, 'startErrors': {}, 'podRemoved': False, 'verdict': 'incomplete'}
    try:
        me = cgroup_of(env, deps, 'self')
        S['unitCgroup'] = me
        if not me or not me.endswith('/' + unit):
            refuse(f'the spike must run inside {unit}')
        rc, _, err = deps['run'](['podman', 'pod', 'create', '--name', pod, '--network=none', '--share=net', '--infra-name', f'{pod}-infra'], 60)
        if rc != 0:
            S['startErrors']['pod'] = err[-300:]
            refuse('pod create failed')
        for k in ('a', 'b'):
            rc, _, err = deps['run'](['podman', 'run', '-d', '--pod', pod, '--name', f'{pod}-{k}', '--cgroups=split', '--pull=never', '--cap-drop=all',
                                      '--security-opt=no-new-privileges', '--read-only', '--memory', '64m', '--pids-limit', '16', BASE_IMAGE,
                                      '/usr/bin/sleep', str(SPIKE_SLEEP_S)], 60)
            if rc != 0:
                S['startErrors'][k] = err[-300:]
        if S['startErrors']:
            S['verdict'] = 'split-refused'
            refuse('a --cgroups=split member did not start in the pod')
        rc, out, _ = deps['run'](['podman', 'pod', 'inspect', pod, '--format', '{{.CgroupPath}}'], 30)
        S['podCgroupPath'] = out.strip() if rc == 0 else None
        for name in (f'{pod}-infra', f'{pod}-a', f'{pod}-b'):
            rc, out, _ = deps['run'](['podman', 'inspect', '--format', '{{.State.Pid}} {{.State.ConmonPid}}', name], 30)
            parts = out.split() if rc == 0 else []
            m = {'inspectRc': rc}
            if len(parts) == 2 and all(p.isdigit() and int(p) > 0 for p in parts):
                m['pid'], m['conmonPid'] = int(parts[0]), int(parts[1])
                m['pidCgroup'], m['conmonCgroup'] = cgroup_of(env, deps, parts[0]), cgroup_of(env, deps, parts[1])
                m['pidUnderUnit'] = bool(m['pidCgroup']) and under(m['pidCgroup'], me)
                m['conmonUnderUnit'] = bool(m['conmonCgroup']) and under(m['conmonCgroup'], me)
            S['members'][name] = m
        complete = all('pid' in m for m in S['members'].values()) and len(S['members']) == 3
        if complete:
            S['verdict'] = 'members-and-infra-under-unit' if all(m['pidUnderUnit'] and m['conmonUnderUnit'] for m in S['members'].values()) else 'not-under-unit'
    except Refuse as e:
        S.setdefault('errors', []).append(str(e))
    except Exception as e:
        S.setdefault('errors', []).append(f'exception: {type(e).__name__}: {str(e)[:200]}')
        S['verdict'] = 'incomplete'
    finally:
        deps['run'](['podman', 'pod', 'rm', '-f', '-t', '0', pod], 60)
        S['podRemoved'] = deps['run'](['podman', 'pod', 'exists', pod], 30)[0] == 1
        own = {deps['getpid']()}
        S['leftoverProcs'] = [p for p in procs_under(env, deps, S['unitCgroup']) if p not in own] if S.get('unitCgroup') else []
        if not S['podRemoved'] or S['leftoverProcs']:
            S['verdict'] = 'incomplete'
    data = json.dumps(S, sort_keys=True).encode()
    sd = state_dir(env, run_id)
    os.makedirs(sd, 0o700, exist_ok=True)
    write_once(f'{sd}/n5b-spike.json', data)
    print(data.decode())
    return 0 if S['verdict'] in SPIKE_VERDICTS else 3              # a definite answer (a finding included) exits 0; the unit then succeeds


# ============================================================ the synthetic tree Root seals with learn_seal_claim.py --mode synthetic
SYN_SUBTREES = ('passive-home-canonical-v1/spool', 'passive-prospective-0106-20261018/spool')
NONCE = re.compile(r'^[A-Za-z0-9._-]{1,64}$')


def synth_tree(out, nonce, mtime_ns=1_700_000_000_000_000_000):
    """A synthetic spool with the two pinned subtree names and NO data from the real spool: fixed placeholder bytes, a fixed mtime, the declaration.
    It has no *.ready batch, so the replay's drain has nothing to send: the run qualifies the copy, the check, the association and the recovery path,
    not the receiver's acceptance of real batches (that is the genuine run's)."""
    if not NONCE.match(nonce or ''):
        refuse('bad nonce')
    if os.path.lexists(out):
        refuse('the output directory must not exist')
    os.makedirs(out, 0o755)
    files = {f'{SYN_SUBTREES[0]}/drain-cursor': b'synthetic-cursor 0\n', f'{SYN_SUBTREES[1]}/drain-cursor': b'synthetic-cursor 0\n',
             f'{SYN_SUBTREES[1]}/SYNTHETIC-README': b'synthetic spool for the LEARN pod harness (#5184); not telemetry\n'}
    for rel, data in files.items():
        p = os.path.join(out, rel)
        os.makedirs(os.path.dirname(p), 0o755, exist_ok=True)
        fd = os.open(p, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o644)
        with os.fdopen(fd, 'wb') as f:
            f.write(data)
        os.utime(p, ns=(mtime_ns, mtime_ns))
    decl = {'schema': 'learn-synthetic-source/1', 'synthetic': True, 'generator': f'learn-pod-harness-{REVISION}', 'nonce': nonce}
    fd = os.open(os.path.join(out, 'SYNTHETIC-SOURCE.json'), os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o644)
    with os.fdopen(fd, 'w') as f:
        f.write(json.dumps(decl, sort_keys=True))
    return {'out': out, 'files': sorted(files), 'declaration': decl}


# ============================================================ arguments and main
USAGE = ('usage: plan --run-id ID [--argv-json J --source-identity H] | case --run-id ID --case C --admission-ref URL [--argv-json J --source-identity H] '
         '| summary --run-id ID | synth-tree --out DIR --nonce N | spike-cgroups --run-id ID --nonce HEX')
ALLOWED = {'plan': ({'--run-id'}, {'--argv-json', '--source-identity'}), 'case': ({'--run-id', '--case', '--admission-ref'}, {'--argv-json', '--source-identity'}),
           'summary': ({'--run-id'}, set()), 'synth-tree': ({'--out', '--nonce'}, set()), 'spike-cgroups': ({'--run-id', '--nonce'}, set())}


def parse(argv):
    cmd = argv[0] if argv else ''
    if cmd not in ALLOWED:
        refuse(USAGE)
    a, i = {}, 1
    while i < len(argv):
        if not argv[i].startswith('--') or i + 1 >= len(argv) or argv[i] in a:
            refuse(USAGE)
        a[argv[i]] = argv[i + 1]
        i += 2
    need, may = ALLOWED[cmd]
    if not need <= set(a) or not set(a) <= need | may:
        refuse(USAGE)
    if '--run-id' in a and not RUN_ID.match(a['--run-id']):
        refuse('the run id is 4 to 12 lowercase letters and digits')
    if cmd == 'case' and a['--case'] not in CASE_IDS:
        refuse('unknown case: ' + a['--case'])
    return cmd, a


def default_deps(env):
    child_env = {'XDG_RUNTIME_DIR': f"/run/user/{env['uid']}", 'DBUS_SESSION_BUS_ADDRESS': f"unix:path=/run/user/{env['uid']}/bus", 'HOME': env['home'],
                 'PATH': '/usr/bin', 'LC_ALL': 'C'}

    def run(argv, timeout=60):
        try:
            p = subprocess.run(argv, capture_output=True, timeout=timeout, cwd='/', env=child_env)
            return p.returncode, p.stdout[:1 << 22].decode('utf-8', 'replace'), p.stderr[-4000:].decode('utf-8', 'replace')
        except subprocess.TimeoutExpired:
            return None, '', 'timeout'

    def read(path):
        with open(path) as f:
            return f.read(1 << 20)

    def read_json(path):
        try:
            fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_CLOEXEC)
            with os.fdopen(fd, 'rb') as f:
                return json.loads(f.read(16 << 20))
        except (OSError, ValueError):
            return None

    def read_lines(path):
        try:
            return [json.loads(l) for l in open(path) if l.strip()]
        except (OSError, ValueError):
            return []

    def sha_file(path):
        try:
            return sha_bytes(open(path, 'rb').read())
        except OSError:
            return None
    me = os.path.abspath(__file__)
    return {'run': run, 'read': read, 'read_json': read_json, 'read_lines': read_lines, 'sha_file': sha_file, 'exists': os.path.lexists,
            'geteuid': os.geteuid, 'getpid': os.getpid, 'now': lambda: datetime.now(timezone.utc), 'monotonic': time.monotonic, 'sleep': time.sleep,
            'walk': os.walk, 'self_stat': lambda: os.stat(me), 'self_sha': lambda: sha_file(me), 'nonce': lambda: secrets.token_hex(16)}


def emit(obj):
    b = json.dumps(obj, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': sha_bytes(b.encode())}))


def main(argv, env=None, deps=None):
    env = dict(PROD, **(env or {}))
    deps = dict(default_deps(env), **(deps or {}))
    try:
        cmd, a = parse(argv)
        if cmd == 'plan':
            emit(plan(env, a['--run-id'], a.get('--argv-json'), a.get('--source-identity'), deps['self_sha']()))
            return 0
        if cmd == 'synth-tree':
            emit(synth_tree(a['--out'], a['--nonce']))
            return 0
        if cmd == 'summary':
            emit(summary(env, a['--run-id'], deps['read_json']))
            return 0
        if cmd == 'spike-cgroups':
            if deps['geteuid']() != env['uid']:
                refuse(f"run as uid {env['uid']}")
            if not re.fullmatch(r'[0-9a-f]{32}', a['--nonce']):
                refuse('the spike nonce is 32 lowercase hex')
            return spike_cgroups(env, deps, a['--run-id'], a['--nonce'])
        R, code = run_case(env, deps, a['--run-id'], a['--case'], a)
        emit(R)
        return code
    except Refuse as e:
        emit({'schema': SCHEMA, 'errors': [str(e)], 'exitCode': 5})
        return 5


if __name__ == '__main__':
    os.umask(0o077)
    sys.exit(main(sys.argv[1:]))

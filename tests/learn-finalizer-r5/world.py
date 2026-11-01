# A fake pod world for the finalizer r5 tests: podman, systemctl, journalctl, a fake /proc and cgroup tree, a loop volume, a claim and a sealed snapshot,
# and a fake clock whose sleep() advances time and fires scheduled events. Nothing here starts a container or touches the real system.
import hashlib, json, os, shutil, stat, subprocess, sys, tarfile, io

OPID = 'op-pod-1'
IDENT = 'ab' * 32
NONCE = 'cd' * 16
PGIMG = 'ee' * 32
ROLES = ('host', 'recv', 'feed')
POD = f'learn-pod-{OPID}'
OPU = POD + '.service'


def status_bytes(role, exit_=0, reason='ok', phase='terminal', schema='learn-pod-status/1', detail='rc=0', role_field=None):
    j = {'schema': schema, 'role': role_field or role, 'phase': phase}
    if phase == 'terminal':
        j.update(exit=exit_, reason=reason, detail=detail)
    b = json.dumps(j, separators=(',', ':')).encode()
    return b.ljust(4096, b' ')


class VFS:
    def __init__(self, blocks=262144, frsize=4096):
        self.f_blocks, self.f_frsize, self.f_bavail = blocks, frsize, blocks


class FakeTime:
    def __init__(self, w):
        self.w = w

    def monotonic(self):
        return self.w.clock

    def sleep(self, s):
        self.w.clock += s
        self.w.fire()


class World:
    def __init__(self, tmp):
        self.tmp = tmp
        self.clock = 1000.0
        self.t_start = self.clock
        self.top = os.path.join(tmp, 'top')
        self.view = os.path.join(self.top, 'view')
        self.home = os.path.join(tmp, 'home')
        self.proc, self.cg, self.slice, self.volroot, self.cred = (os.path.join(tmp, n) for n in ('proc', 'cg', 'slice', 'volroot', 'cred'))
        for d in (self.top, self.view, os.path.join(self.view, 'claims'), os.path.join(self.view, 'inputs'), self.proc, self.cg, self.slice, self.volroot, self.cred):
            os.makedirs(d, exist_ok=True)
        for d in (self.top, self.view, os.path.join(self.view, 'claims'), os.path.join(self.view, 'inputs')):
            os.chmod(d, 0o755)
        st = os.path.join(self.home, '.local', 'state')
        for d in ('learn-custody', 'learn-evidence', 'learn-checkpoint', 'learn-input'):
            os.makedirs(os.path.join(st, d), exist_ok=True)
        os.makedirs(os.path.join(self.volroot, f'{OPID}.mnt'))
        self.events, self.calls = [], []
        self.unit_state, self.unit_result, self.enter_us, self.exec_pid = 'inactive', 'success', 0, '4242'
        self.cg_unit = f'/user.slice/user-30034.slice/user@30034.service/app.slice/{OPU}'
        os.makedirs(self.cg + self.cg_unit)
        self.set_cg_unit(populated=0)
        self.pod_exists, self.containers, self.secrets, self.extra_units = True, {}, [], []
        self.mounted, self.vol_used, self.vol, self.vol_exact, self.unit_lag = True, 0.05, VFS(), None, 0
        self.leave_pgcd = False
        self.controldata = ('Database cluster state:               shut down\nLatest checkpoint location:           0/1A2B3C\nLatest checkpoint\'s TimeLineID:      1\n', 0)
        self.pgcd_rc = 0
        self.fail = set()
        self.mem0, self.pids0 = 900 << 20, 128
        self.mem, self.pids = self.mem0, self.pids0
        self.write_slice()
        self.memev = 'MEMEV oom_kill=0'
        self.pause_hook = None
        self.proc_pid = 5000
        for r_ in ROLES:
            self.add_container(f'{POD}-{r_}', role=r_)
        self.add_container(f'{POD}-pg', pg=True)
        self.add_container(f'{POD}-infra', infra=True)
        self.make_snapshot()
        self.pg_exit_after = 0

    def write_admission(self, fin_sha, image_ids=None, op=OPID, ident=IDENT, schema='learn-pod-admission/1', raw=None, mode=0o644):
        d = os.path.join(self.view, 'admissions')
        os.makedirs(d, exist_ok=True)
        os.chmod(d, 0o755)
        p = os.path.join(d, op + '.admit')
        if os.path.lexists(p):
            os.unlink(p)
        body = raw if raw is not None else json.dumps({'schema': schema, 'opId': op, 'argvJsonSha256': 'a1' * 32, 'sourceIdentity': ident, 'operatorSha256': 'b2' * 32, 'finalizerSha256': fin_sha,
                                                          'imageIds': (list(image_ids) if image_ids is not None else sorted([PGIMG, 'ff' * 32])), 'issuedUtc': '2026-10-31T08:00:00+00:00', 'expiresUtc': '2026-10-31T08:05:00+00:00'}, sort_keys=True)
        open(p, 'w').write(body)
        os.chmod(p, mode)
        self.admission_path = p

    # ------------------------------------------------------------ containers
    def add_container(self, name, role=None, pg=False, infra=False):
        self.proc_pid += 1
        pid = self.proc_pid
        cid = hashlib.sha256(name.encode()).hexdigest()
        c = {'name': name, 'id': cid, 'pid': pid, 'state': 'running', 'exit': 0, 'oom': False, 'image': PGIMG if pg else 'ff' * 32, 'role': role, 'pg': pg,
             'mounts': [], 'started': '2026-10-31T08:00:00Z', 'start_time': 777000 + pid}
        self.containers[name] = c
        d = os.path.join(self.proc, str(pid))
        os.makedirs(d, exist_ok=True)
        cgp = f'/user.slice/user-30034.slice/user@30034.service/app.slice/libpod-{cid}.scope/container'
        open(os.path.join(d, 'cgroup'), 'w').write(f'0::{cgp}\n')
        self.write_stat(c)
        os.makedirs(self.cg + cgp, exist_ok=True)
        c['cgdir'] = self.cg + cgp
        self.set_frozen(c, 0)
        if role:
            os.makedirs(os.path.join(d, 'root', 'out', role))
            open(os.path.join(d, 'root', 'out', role, '.status'), 'wb').write(status_bytes(role, phase='ready'))
            c['out'] = os.path.join(d, 'root', 'out', role)
        return c

    def write_stat(self, c):
        open(os.path.join(self.proc, str(c['pid']), 'stat'), 'w').write(f"{c['pid']} (x y) S " + ' '.join(['0'] * 18 + [str(c['start_time'])] + ['0'] * 5) + '\n')

    def set_frozen(self, c, v):
        open(os.path.join(c['cgdir'], 'cgroup.events'), 'w').write(f'populated 1\nfrozen {v}\n')

    def finish_role(self, role, exit_=0, reason='ok', **kw):
        c = self.containers[f'{POD}-{role}']
        open(os.path.join(c['out'], '.status'), 'wb').write(status_bytes(role, exit_, reason, **kw))
        open(os.path.join(c['out'], '.done'), 'wb').close()

    def put(self, role, name, data=b'x', mode=None):
        p = os.path.join(self.containers[f'{POD}-{role}']['out'], name)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'wb').write(data)

    # ------------------------------------------------------------ cgroup, slice, volume
    def set_cg_unit(self, populated):
        open(self.cg + self.cg_unit + '/cgroup.events', 'w').write(f'populated {populated}\n')
        open(self.cg + self.cg_unit + '/cgroup.procs', 'w').write('123\n' if populated else '')
        if 'cg_events_only' in getattr(self, 'fail', ()) and not populated:
            open(self.cg + self.cg_unit + '/cgroup.events', 'w').write('populated 1\n')                  # the events file still says populated; the procs list is empty

    def write_slice(self):
        open(os.path.join(self.slice, 'memory.current'), 'w').write(f'{self.mem}\n')
        open(os.path.join(self.slice, 'pids.current'), 'w').write(f'{self.pids}\n')

    def statvfs(self, path):
        if path != os.path.join(self.volroot, f'{OPID}.mnt') or 'statvfs' in self.fail:
            raise OSError(2, 'no such mount')
        if self.vol_exact:
            self.vol.f_blocks, self.vol.f_bavail = self.vol_exact
        else:
            self.vol.f_bavail = int((1 - self.vol_used) * self.vol.f_blocks)
        return self.vol

    # ------------------------------------------------------------ claim and sealed snapshot
    def make_snapshot(self, sid='snap-1', files=None, src_modes=None, sm_ident=None, extra_entries=None, kind='real', sm_extra=None, claim_extra=None):
        """kind='synthetic' writes the seal tool's synthetic records (synthetic-inputs/, synthetic-claims/, learn-input-*-synthetic/1, provenance synthetic)."""
        files = files or {'passive-a/spool/one.json': b'{"a":1}', 'passive-a/spool/two.json': b'{"b":22}', 'passive-b/spool/producer.lock': b''}
        inputs = os.path.join(self.view, 'synthetic-inputs' if kind == 'synthetic' else 'inputs')
        os.makedirs(inputs, exist_ok=True)
        os.chmod(inputs, 0o755)
        snap = os.path.join(inputs, sid)
        os.makedirs(snap, exist_ok=True)
        os.chmod(snap, 0o755)
        entries = []
        for rel, data in files.items():
            p = os.path.join(snap, rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            open(p, 'wb').write(data)
            os.chmod(p, 0o644)
            ns = 1_700_000_000_000_000_000 + len(entries) * 1_000_000_007
            os.utime(p, ns=(ns, ns))
            entries.append({'path': rel, 'type': 'file', 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest(), 'mtime_ns': ns, 'mode': (src_modes or {}).get(rel, oct(0o644))})
        for root, ds, _ in os.walk(snap):
            os.chmod(root, 0o755)
        entries += extra_entries or []
        self.sid, self.entries = sid, entries
        sm = {'schema': 'learn-input-snapshot/1', 'snapshotId': sid, 'sourceIdentity': sm_ident or IDENT, 'files': entries}
        if kind == 'synthetic':
            sm.update(schema='learn-input-snapshot-synthetic/1', provenance='synthetic', realSpoolProvenance=False)
        sm.update(sm_extra or {})
        smb = json.dumps(sm, sort_keys=True, separators=(',', ':')).encode()
        self.manifest_path = os.path.join(inputs, sid + '.manifest.json')
        open(self.manifest_path, 'wb').write(smb)
        os.chmod(self.manifest_path, 0o644)
        self.write_claim(sid, hashlib.sha256(smb).hexdigest(), kind=kind, extra=claim_extra)

    def write_claim(self, sid, sealed_sha, op=OPID, ident=IDENT, schema='learn-input-claim/1', kind='real', extra=None):
        c = {'schema': schema, 'generation': 1, 'sourceIdentity': ident, 'opId': op, 'snapshotId': sid, 'sealedManifestSha256': sealed_sha}
        claims = os.path.join(self.view, 'synthetic-claims' if kind == 'synthetic' else 'claims')
        if kind == 'synthetic':
            c.update(schema='learn-input-claim-synthetic/1', provenance='synthetic', realSpoolProvenance=False)
        c.update(extra or {})
        os.makedirs(claims, exist_ok=True)
        os.chmod(claims, 0o755)
        self.claim_path = os.path.join(claims, IDENT + '.claim')
        if os.path.exists(self.claim_path):
            os.unlink(self.claim_path)
        open(self.claim_path, 'wb').write(json.dumps(c, sort_keys=True).encode())
        os.chmod(self.claim_path, 0o644)

    # ------------------------------------------------------------ the scenario clock
    def at(self, t, fn):
        self.events.append([t, fn, False])

    def fire(self):
        for ev in self.events:
            if not ev[2] and self.clock - self.t_start >= ev[0]:
                ev[2] = True
                ev[1]()

    def activate(self):
        self.unit_state = 'active'
        self.enter_us = int(self.clock * 1e6)
        self.set_cg_unit(populated=1)
        self.mem, self.pids = self.mem0 + (2 << 30), self.pids0 + 300
        self.write_slice()

    def settle(self):
        """what stopping and removing the pod does to the world"""
        self.pod_exists = False
        for name in [n for n in self.containers if n.startswith(POD + '-')]:
            c = self.containers[name]
            if c['state'] != 'exited':
                c['state'] = 'exited'
            del self.containers[name]
        end_state = 'inactive' if 'unit_failed' not in self.fail else 'failed'

        def end():
            self.unit_state = end_state
        if self.unit_lag:
            self.at(self.clock - self.t_start + self.unit_lag, end)
        else:
            end()
        self.set_cg_unit(populated=0 if 'cg_populated' not in self.fail else 1)
        if 'slice_stuck' not in self.fail and 'slice_mem_stuck' not in self.fail:
            self.mem = self.mem0
        if 'slice_stuck' not in self.fail and 'slice_pids_stuck' not in self.fail:
            self.pids = self.pids0
        self.write_slice()

    # ------------------------------------------------------------ the command dispatcher
    def run(self, argv, timeout=60, cap=1 << 20):
        a = list(argv)
        self.calls.append(a)
        key = ' '.join(a[:3])
        if a[0] == 'test':
            p = subprocess.run(a, capture_output=True, text=True)
            return p.returncode, '', ''
        if a[:3] == ['systemctl', '--user', 'show']:
            if a[4:] == ['-pActiveState'] and getattr(self, 'show_state_fails', 0) > 0:        # a transient `systemctl show` failure (timeout or nonzero rc): nothing on stdout
                self.show_state_fails -= 1
                return 1, '', 'timeout'
            if a[4:] == ['-pControlGroup'] and getattr(self, 'cg_read_fails', 0) > 0:
                self.cg_read_fails -= 1
                return 0, 'ControlGroup=\n', ''
            props = {'ActiveEnterTimestampMonotonic': str(self.enter_us), 'ActiveState': self.unit_state, 'ExecMainPID': self.exec_pid, 'ControlGroup': self.cg_unit,
                     'Result': self.unit_result, 'ExecMainStatus': '0'}
            return 0, ''.join(f'{p[2:]}={props.get(p[2:], "")}\n' for p in a[4:]), ''
        if a[:3] == ['systemctl', '--user', 'list-units']:
            self.unit_census = getattr(self, 'unit_census', 0) + 1
            if 'units' in self.fail or ('units_before_reset' in self.fail and not getattr(self, 'reset_seen', False)) or ('units_after_reset' in self.fail and getattr(self, 'reset_seen', False)):
                return 1, '', 'boom'
            rows = [f'{OPU} loaded {self.unit_state} running Learn pod'] + self.extra_units
            return 0, '\n'.join(rows) + '\n', ''
        if a[:3] == ['systemctl', '--user', 'reset-failed']:
            self.reset_seen = True
            if 'reset_stuck' not in self.fail:
                if a[3] == OPU and self.unit_state == 'failed':
                    self.unit_state = 'inactive'
                self.extra_units = [r for r in self.extra_units if not (r.split()[0] == a[3] and r.split()[2] == 'failed')]
            return 0, '', ''
        if a[:2] == ['podman', 'inspect']:
            name, fmt = a[-1], a[3]
            c = self.containers.get(name)
            if c is None or ('inspect' in self.fail and 'Mounts' in fmt):
                return 1, '', 'no such container'
            if fmt == '{{.Id}}':
                return 0, c['id'] + '\n', ''
            if fmt == '{{.State.Pid}}':
                return 0, str(c['pid'] if c['state'] in ('running', 'paused') else 0) + '\n', ''
            if fmt == '{{.State.Status}}':
                return 0, c['state'] + '\n', ''
            if fmt == '{{.State.Status}}|{{.State.ExitCode}}|{{.State.OOMKilled}}|{{.Image}}':
                return 0, f"{c['state']}|{c['exit']}|{'true' if c['oom'] else 'false'}|{c['image']}\n", ''
            if fmt == '{{json .Mounts}}|{{.State.StartedAt}}':
                return 0, json.dumps(c['mounts']) + '|' + c['started'] + '\n', ''
            return 1, '', 'unknown format'
        if a[:2] == ['podman', 'pause']:
            c = self.containers.get(a[2])
            if c is None or 'pause' in self.fail:
                return 1, '', 'no'
            c['state'] = 'paused'
            self.set_frozen(c, 1)
            if self.pause_hook:
                self.pause_hook(c)
            return 0, '', ''
        if a[:2] == ['podman', 'unpause']:
            c = self.containers.get(a[2])
            if c is None or 'unpause' in self.fail:
                return 1, '', 'no'
            c['state'] = 'running'
            self.set_frozen(c, 0)
            return 0, '', ''
        if a[:2] == ['podman', 'kill']:
            c = self.containers.get(a[-1])
            if c is None or 'kill' in self.fail:
                return 1, '', 'no'
            def stop(c=c):
                c['state'] = 'exited'
                c['exit'] = 0 if 'pg_exit' not in self.fail else 137
            if self.pg_exit_after:
                self.at(self.clock - self.t_start + self.pg_exit_after, stop)         # the container keeps running until the event fires
            else:
                stop()
            return 0, '', ''
        if a[:3] == ['podman', 'pod', 'stop']:
            for c in self.containers.values():
                c['state'] = 'exited'
            return 0, '', ''
        if a[:3] == ['podman', 'pod', 'rm']:
            if 'pod_rm' in self.fail:
                return 1, '', 'cannot remove'
            self.settle()
            return 0, '', ''
        if a[:3] == ['podman', 'pod', 'ps']:
            if 'pod_ps' in self.fail:
                return 1, '', 'x'
            return 0, (POD + '\n') if self.pod_exists else '', ''
        if a[:2] == ['podman', 'ps']:
            if 'ps' in self.fail:
                return 1, '', 'x'
            pref = a[a.index('--filter') + 1].split('=^', 1)[1]
            names = [n for n in self.containers if n.startswith(pref)]
            if pref == f'learn-fin-{OPID}-' and self.leave_pgcd:
                names.append(f'learn-fin-{OPID}-pgcd')
            return 0, ''.join(n + '\n' for n in names), ''
        if a[:3] == ['podman', 'secret', 'ls']:
            return (1, '', 'x') if 'secret' in self.fail else (0, ''.join(s + '\n' for s in self.secrets), '')
        if a[:2] == ['podman', 'run']:
            self.last_run = a
            return self.pgcd_rc, self.controldata[0], ''
        if a[:3] == ['podman', 'rm', '-f']:
            return 0, '', ''
        if a[0] == 'journalctl':
            lines = [json.dumps({'MESSAGE': f'FSGG {NONCE} {self.memev}', '_SYSTEMD_USER_UNIT': OPU, '_PID': '31337', '_COMM': 'sh'})] if self.memev else []
            lines.append(json.dumps({'MESSAGE': f'FSGG {NONCE} FIN trigger=done', '_SYSTEMD_USER_UNIT': f'learn-fin-{OPID}.service', '_PID': '99', '_COMM': 'python3'}))
            return 0, '\n'.join(lines) + '\n', ''
        return 1, '', f'unexpected: {key}'

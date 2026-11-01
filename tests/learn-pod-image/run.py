#!/usr/bin/python3 -I
# Gates for the LEARN pod image source (deployment/learn-finite-profile/pod-image), Refs #4647.
#
# Three parts, none of which builds an image or starts a container:
#   1. the role entrypoint, run for real under /bin/sh with its paths substituted into a temporary directory: the learn-pod-status/1 contract (pre-allocation,
#      in-place updates, ready, terminal reasons, the TERM/KILL sequence, blocking, refusals);
#   2. the Containerfile: its structure, and its RUN step executed in a sandbox against fixtures (hash checks, runtime minimum, user entries);
#   3. the builder: context verification, tree extraction, runtime parsing, image gates and main() against a fake podman, plus the repository pins.
# stdlib only; no credential, no network, no container, no root, no systemd.
#
# The two `paths:` lists of the workflow are identical on purpose; `check-paths-coherence.py` rule (a) reds a divergence.
import hashlib, importlib.util, io, json, os, re, shutil, signal, socket, stat, subprocess, sys, tempfile, time, zipfile
from contextlib import redirect_stdout

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
IMG = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'pod-image')
ROLE, CF, BUILDER, TREE, CLITREE = (os.path.join(IMG, n) for n in ('role', 'Containerfile', 'learn-build-pod-app-r1.py', 'telemetry-host-tree.json', 'coord-cli-tree.json'))
n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def sha(path):
    return hashlib.sha256(open(path, 'rb').read()).hexdigest()


spec = importlib.util.spec_from_file_location('builder', BUILDER)
B = importlib.util.module_from_spec(spec)
spec.loader.exec_module(B)
ROLE_SRC, CF_SRC, BUILDER_SRC = open(ROLE).read(), open(CF).read(), open(BUILDER).read()

# ============================================================ 1. the role entrypoint
PROCS = []


def sub_once(src, old, new):
    check(src.count(old) == 1, f'the production text {old!r} occurs exactly once (so the test substitution is exact)')
    return src.replace(old, new)


class Role:
    """The production role script with /out and /work moved into tmp (and optionally constants and the /proc/net files replaced), run under /bin/sh."""
    def __init__(self, tmp, name='host', grace=None, min_free=None, proc_tcp=None, make_dir=True, work_ok=True, uid=1000, ddwrap=None):
        self.tmp, self.name = tmp, name
        self.out = os.path.join(tmp, 'out')
        self.dir = os.path.join(self.out, name)
        self.work = os.path.join(tmp, 'work')
        if make_dir:
            os.makedirs(self.dir)
        if work_ok:
            os.makedirs(self.work)
        else:
            open(self.work, 'w').write('x')
        s = sub_once(ROLE_SRC, 'D=/out/$name', f'D={self.out}/$name')
        s = sub_once(s, '"$(id -u)" != 0', f'"$(echo {uid})" != 0')                  # the tests do not run as root; the guard is tested with uid 0
        s = sub_once(s, 'mkdir -p /work/home /work/tmp', f'mkdir -p {self.work}/home {self.work}/tmp')
        if grace is not None:
            s = sub_once(s, 'GRACE=20\n', f'GRACE={grace}\n')
        if min_free is not None:
            s = sub_once(s, 'MIN_FREE=1048576\n', f'MIN_FREE={min_free}\n')
        if proc_tcp is not None:
            s = sub_once(s, '/proc/net/tcp /proc/net/tcp6', proc_tcp)
        self.script = os.path.join(tmp, 'role.sh')
        open(self.script, 'w').write(s)
        self.p = None
        self.env = None
        self.ddlog = os.path.join(tmp, 'dd.log')
        if ddwrap is not None:                                                  # a dd in front of the real one: logs its arguments, can delay the terminal write or cut a write short
            wd = os.path.join(tmp, 'wrap')
            os.makedirs(wd)
            real_dd = shutil.which('dd')
            open(os.path.join(wd, 'dd'), 'w').write(f'''#!/bin/sh
t=$(mktemp)
cat > "$t"
echo "$*" >> {self.ddlog}
if grep -q terminal "$t"; then sleep {ddwrap.get('terminal_delay', 0)}; fi
if [ -n "{ddwrap.get('short', '')}" ]; then head -c 100 "$t" | {real_dd} "$@"; else {real_dd} "$@" < "$t"; fi
rm -f "$t"
''')
            os.chmod(os.path.join(wd, 'dd'), 0o755)
            self.env = dict(os.environ, PATH=wd + os.pathsep + os.environ['PATH'])

    def start(self, *args, **kw):
        self.p = subprocess.Popen(['/bin/sh', self.script, *args], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True, env=self.env,
                                  preexec_fn=lambda: signal.signal(signal.SIGINT, signal.SIG_DFL), **kw)    # a test run started in the background inherits SIGINT ignored; a shell cannot trap what it inherits ignored
        PROCS.append(self.p)
        return self.p

    def status_raw(self):
        try:
            return open(os.path.join(self.dir, '.status'), 'rb').read()
        except FileNotFoundError:
            return None

    def status(self):
        raw = self.status_raw()
        return json.loads(raw.decode().rstrip(' ')) if raw else None

    def done(self):
        return os.path.exists(os.path.join(self.dir, '.done'))

    def inode(self):
        return os.stat(os.path.join(self.dir, '.status')).st_ino


def wait_for(cond, secs=8.0):
    end = time.monotonic() + secs
    while time.monotonic() < end:
        if cond():
            return True
        time.sleep(0.02)
    return cond()


def alive(p):
    return p.poll() is None


def finish(r, secs=8.0):
    """TERM a role until it exits (a running command takes the first TERM and then the role blocks; the second TERM exits); returns its exit code"""
    for _ in range(3):
        if not alive(r.p):
            break
        r.p.send_signal(signal.SIGTERM)
        wait_for(lambda: not alive(r.p) or r.done(), secs)
        wait_for(lambda: not alive(r.p), 0.3)
    try:
        return r.p.wait(secs)
    except subprocess.TimeoutExpired:
        os.killpg(r.p.pid, signal.SIGKILL)
        return None


def kill_all():
    for p in PROCS:
        if p.poll() is None:
            try:
                os.killpg(p.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            p.wait()


try:
    # --- refusals before anything is written
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('db', '--', 'true')
        check(r.p.wait(5) == 64 and r.status_raw() is None and not r.done(), 'an unknown role exits 64 and writes nothing')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start()
        check(r.p.wait(5) == 64, 'no role exits 64')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, make_dir=False)
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 70 and not os.path.exists(r.dir), 'a missing /out/<role> exits 70 and creates nothing (it does not make its own status directory)')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, make_dir=False)
        os.makedirs(r.out)
        os.symlink(tmp, r.dir)
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 70, 'a symlink at /out/<role> exits 70')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        open(os.path.join(r.dir, '.status'), 'w').write('old')
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 70 and open(os.path.join(r.dir, '.status')).read() == 'old', 'an existing .status exits 70 and is not touched')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        open(os.path.join(r.dir, '.done'), 'w').close()
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 70 and r.status_raw() is None, 'an existing .done exits 70 and no status is written')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, work_ok=False)
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 71 and not r.done(), 'a /work that cannot be prepared exits 71 and runs nothing (status stays at starting, no .done)')
        check(r.status()['phase'] == 'starting', 'the status written before the failure says starting, never ready or terminal')

    # --- the container is meant to run as 1000: uid 0 is refused before anything is touched
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, uid=0)
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 64 and r.status_raw() is None and not r.done() and not os.path.exists(os.path.join(r.work, 'home')), 'run as uid 0 the role exits 64 and touches nothing (no status, no /work)')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, uid=1000)
        r.start('host', '--', 'true')
        check(wait_for(r.done, 5), 'run as uid 1000 the role runs')
        finish(r)
    check('"$(id -u)" != 0' in ROLE_SRC and ROLE_SRC.index('"$(id -u)"') < ROLE_SRC.index('D=/out/$name'), 'the production guard asks the real id -u, and does so before it reads or writes /out')

    # --- status writes: .done follows the terminal status; updates are in place; the first write sizes the file
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, ddwrap={'terminal_delay': 1})
        r.start('host', '--', 'sh', '-c', 'sleep 0.3')
        seen = {}

        def watch():
            if r.done():
                seen['phase'] = (r.status() or {}).get('phase')
                return True
            return False
        check(wait_for(watch, 8) and seen['phase'] == 'terminal', '.done appears only AFTER the terminal status is in place (the finalizer polls .done and then archives): the terminal write is delayed 1 s and .done still waits for it')
        finish(r)
        calls = open(r.ddlog).read().splitlines()
        check(len(calls) >= 3 and 'conv=fsync' in calls[0] and 'notrunc' not in calls[0], 'the first write creates the file (no notrunc)')
        check(all('conv=notrunc,fsync' in c for c in calls[1:]), 'every later status write is in place (conv=notrunc,fsync)')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, ddwrap={'short': '1'})
        r.start('host', '--', 'true')
        check(r.p.wait(5) == 70 and not r.done(), 'a pre-allocation that does not produce exactly 4096 bytes exits 70 and runs nothing')

    # --- a command that succeeds on its own
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--', 'sh', '-c', 'sleep 0.7; exit 0')
        check(wait_for(lambda: r.status_raw() is not None), 'the status is pre-allocated')
        first = r.status_raw()
        check(len(first) == 4096 and first.rstrip(b' ') == b'{"schema":"learn-pod-status/1","role":"host","phase":"starting"}' or json.loads(r.status_raw().decode().rstrip(' '))['phase'] in ('starting', 'ready'),
              'the first status is exactly 4096 B of learn-pod-status/1 JSON for this role')
        ino = r.inode()
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'with no probe the role is ready as soon as the command runs')
        check(len(r.status_raw()) == 4096 and r.inode() == ino, 'the ready update is in place: same inode, still exactly 4096 B')
        check(wait_for(r.done), 'a command that exits 0 on its own gets .done')
        st = r.status()
        check(st == {'schema': 'learn-pod-status/1', 'role': 'host', 'phase': 'terminal', 'exit': 0, 'reason': 'ok', 'detail': 'rc=0'}, 'terminal status: exit 0, reason ok')
        check(len(r.status_raw()) == 4096 and r.inode() == ino, 'the terminal update is in place as well')
        check(os.path.getsize(os.path.join(r.dir, '.done')) == 0, '.done is empty')
        check(os.path.isdir(os.path.join(r.work, 'home')) and os.path.isdir(os.path.join(r.work, 'tmp')), 'HOME and TMPDIR directories are created under /work')
        time.sleep(0.4)
        check(alive(r.p), 'after the terminal status the role blocks, so the live container can be archived')
        check(finish(r) == 0, 'a TERM in the blocked state exits 0 at once')

    # --- failures
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, name='recv')
        r.start('recv', '--', 'sh', '-c', 'exit 3')
        check(wait_for(r.done), 'a failing command ends')
        check(r.status() == {'schema': 'learn-pod-status/1', 'role': 'recv', 'phase': 'terminal', 'exit': 3, 'reason': 'error', 'detail': 'rc=3'}, 'exit 3 is reason error')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, name='feed')
        r.start('feed', '--', 'no-such-command-for-the-role-test')
        check(wait_for(r.done), 'a command that cannot be executed ends')
        check(r.status()['exit'] == 127 and r.status()['reason'] == 'error', 'an unexecutable command is exit 127, reason error')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, min_free=10 ** 18)
        r.start('host', '--', 'sh', '-c', 'exit 5')
        check(wait_for(r.done), 'a failing command ends (no free space)')
        check(r.status()['reason'] == 'enospc' and r.status()['exit'] == 5, 'a non-zero exit with /out below the free-space floor is reason enospc')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, min_free=10 ** 18)
        r.start('host', '--', 'true')
        check(wait_for(r.done), 'a succeeding command ends (no free space)')
        check(r.status()['reason'] == 'ok', 'exit 0 stays reason ok even when space is low (enospc only explains a failure)')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        vol = os.path.join(tmp, 'vol')
        os.makedirs(vol)
        r = Role(tmp, min_free=1)
        r.start('host', '--volume', vol, '--', 'sh', '-c', 'exit 5')
        check(wait_for(r.done) and r.status()['reason'] == 'error', 'with enough space on /out and the volume a failure is reason error')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        vol = os.path.join(tmp, 'vol')
        os.makedirs(vol)
        r = Role(tmp)
        s2 = sub_once(open(r.script).read(), 'avail_vol=$(free_bytes "$volume")', 'avail_vol=0')
        open(r.script, 'w').write(s2)
        r.start('host', '--volume', vol, '--', 'sh', '-c', 'exit 5')
        check(wait_for(r.done) and r.status()['reason'] == 'enospc', 'a full volume (not only a full /out) makes a failure reason enospc')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--volume', os.path.join(tmp, 'absent'), '--', 'sh', '-c', 'exit 5')
        check(wait_for(r.done) and r.status()['reason'] == 'error', 'an unmeasurable volume does not claim enospc')
        finish(r)

    # --- the command does not inherit stdin
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--', 'cat')
        check(wait_for(r.done, 5) and r.status()['reason'] == 'ok', 'the command reads /dev/null, not the role\'s stdin')
        finish(r)

    # --- TERM: the first one stops the command, the second one exits
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--', 'sleep', '1000')
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'a long-running command becomes ready')
        ino = r.inode()
        r.p.send_signal(signal.SIGTERM)
        check(wait_for(r.done), 'the first TERM stops the command and writes .done')
        st = r.status()
        check(st['phase'] == 'terminal' and st['reason'] == 'signalled' and st['exit'] == 143 and st['detail'] == 'rc=143', 'the stopped command is terminal, reason signalled, exit 143')
        check(r.inode() == ino and len(r.status_raw()) == 4096, 'the terminal status of a stopped command is written in place')
        time.sleep(0.3)
        check(alive(r.p), 'the role still blocks after the first TERM (the finalizer can archive the live container)')
        check(finish(r) == 0, 'the second TERM exits 0')
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--', 'sleep', '1000')
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'ready before an INT')
        r.p.send_signal(signal.SIGINT)
        check(wait_for(r.done) and r.status()['reason'] == 'signalled', 'INT stops the command like TERM')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp, grace=1)
        flag = os.path.join(tmp, 'trapped')
        r.start('host', '--', 'sh', '-c', f'trap "" TERM; : > {flag}; while :; do sleep 0.1; done')
        check(wait_for(lambda: os.path.exists(flag)) and wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'a TERM-ignoring command is ready')
        t0 = time.monotonic()
        r.p.send_signal(signal.SIGTERM)
        check(wait_for(r.done, 10), 'a command that ignores TERM is KILLed after the grace period')
        el = time.monotonic() - t0
        check(0.8 <= el < 6 and r.status()['exit'] == 137 and r.status()['reason'] == 'signalled', 'the KILL came after the grace period, not before, and the exit is 137')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        flag = os.path.join(tmp, 'term-seen')
        r.start('host', '--', 'sh', '-c', f'trap "echo seen > {flag}; exit 0" TERM; while :; do sleep 0.1; done')
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'a TERM-handling command is ready')
        time.sleep(0.2)
        r.p.send_signal(signal.SIGTERM)
        check(wait_for(r.done) and os.path.exists(flag), 'a command that handles TERM sees it and ends')
        check(r.status()['reason'] == 'signalled' and r.status()['exit'] == 0, 'a stop requested by TERM is reason signalled even when the command exits 0')
        finish(r)

    # --- readiness probes
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        probe = os.path.join(tmp, 'listening')
        r.start('host', '--ready-file', probe, '--', 'sleep', '1000')
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'starting'), 'with a ready-file probe the role stays at starting')
        time.sleep(0.6)
        check(r.status()['phase'] == 'starting', 'still starting while the file is absent')
        os.symlink('/etc/hostname', probe)
        time.sleep(0.6)
        check(r.status()['phase'] == 'starting', 'a symlink does not make it ready')
        os.unlink(probe)
        open(probe, 'w').close()
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready'), 'a regular file makes it ready')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        probe = os.path.join(tmp, 'never')
        r.start('host', '--ready-file', probe, '--ready-timeout', '1', '--', 'sleep', '1000')
        check(wait_for(r.done, 10), 'a probe that never passes ends in a terminal status')
        st = r.status()
        check(st['phase'] == 'terminal' and st['reason'] == 'error' and st['detail'] == 'ready-timeout', 'a readiness timeout is reason error, detail ready-timeout')
        check(st['exit'] == 143, 'the command was stopped (TERM) when the readiness timed out')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        port = None
        s = socket.socket()
        s.bind(('127.0.0.1', 0))
        port = s.getsockname()[1]
        s.close()
        r = Role(tmp)
        py = f'import socket,time;time.sleep(1.2);s=socket.socket();s.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1);s.bind(("127.0.0.1",{port}));s.listen();time.sleep(60)'
        r.start('host', '--ready-tcp', str(port), '--', sys.executable, '-c', py)
        check(wait_for(lambda: r.status() is not None), 'the tcp-probe role starts')
        time.sleep(0.5)
        check(r.status()['phase'] == 'starting', 'not ready before the listener is up')
        check(wait_for(lambda: (r.status() or {}).get('phase') == 'ready', 10), 'ready once a listener is up on the port (real /proc/net/tcp)')
        finish(r)
        finish(r)

    # --- the /proc/net/tcp match, against fixture tables
    HDR = '  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n'

    def tcp_fixture(tmp, lines4, lines6=()):
        p4, p6 = os.path.join(tmp, 'tcp'), os.path.join(tmp, 'tcp6')
        open(p4, 'w').write(HDR + ''.join(lines4))
        open(p6, 'w').write(HDR + ''.join(lines6))
        return f'{p4} {p6}'

    def l4(port, st='0A', remote='00000000:0000'):
        return f'   0: 0100007F:{port:04X} {remote} {st} 00000000:00000000 00:00000000 00000000     0        0 12345 1 0000000000000000 100 0 0 10 0\n'

    def l6(port, st='0A', remote='00000000000000000000000000000000:0000'):
        return f'   0: 00000000000000000000000001000000:{port:04X} {remote} {st} 00000000:00000000 00:00000000 00000000     0        0 12345 1 0000000000000000 100 0 0 10 0\n'

    def ready_with(tmp, lines4, lines6, port, wait_s=1.5):
        r = Role(tmp, proc_tcp=tcp_fixture(tmp, lines4, lines6))
        r.start('host', '--ready-tcp', str(port), '--', 'sleep', '1000')
        ok = wait_for(lambda: (r.status() or {}).get('phase') == 'ready', wait_s)
        finish(r)
        return ok
    for what, l4s, l6s, port, want in (
            ('a LISTEN socket on the port (IPv4)', [l4(5432)], [], 5432, True),
            ('a LISTEN socket on the port (IPv6)', [], [l6(5432)], 5432, True),
            ('a LISTEN socket on another port', [l4(5433)], [], 5432, False),
            ('a port whose hex is a prefix of the listener\'s', [l4(0x15380 & 0xFFFF)], [], 0x153, False),
            ('a port whose hex is a suffix of the listener\'s', [l4(0x1538)], [], 0x538, False),
            ('an ESTABLISHED socket on the port', [l4(5432, '01', '0100007F:D431')], [], 5432, False),
            ('a socket in TIME_WAIT on the port', [l4(5432, '06')], [], 5432, False),
            ('a connection whose remote port is the probed port', [l4(6000, '0A', '0100007F:1538')], [], 5432, False),
            ('an empty table', [], [], 5432, False)):
        with tempfile.TemporaryDirectory() as tmp:
            check(ready_with(tmp, l4s, l6s, port, 1.5 if want else 1.0) == want, f'readiness from /proc/net/tcp: {what} is {"ready" if want else "not ready"}')

    # --- bad invocations are terminal gate-refused, never a silent exit
    bad_cases = (
        (('host',), 'no --'), (('host', '--'), 'empty command'), (('host', '--bogus', '--', 'true'), 'unknown option'), (('host', 'true'), 'a command without --'),
        (('host', '--ready-tcp', '--', 'true'), 'missing value'), (('host', '--ready-tcp', '0', '--', 'true'), 'port 0'), (('host', '--ready-tcp', '65536', '--', 'true'), 'port 65536'),
        (('host', '--ready-tcp', 'abc', '--', 'true'), 'non-numeric port'), (('host', '--ready-tcp', '5432x', '--', 'true'), 'port with a suffix'),
        (('host', '--ready-tcp', '1', '--ready-file', '/x', '--', 'true'), 'two probes'), (('host', '--ready-file', '/x', '--ready-tcp', '1', '--', 'true'), 'two probes, other order'),
        (('host', '--ready-file', 'rel', '--', 'true'), 'relative ready path'), (('host', '--ready-file', '/a/../b', '--', 'true'), 'ready path with ..'),
        (('host', '--ready-file', '/a b', '--', 'true'), 'ready path with a space'), (('host', '--ready-file', '/a/..', '--', 'true'), 'ready path ending in ..'),
        (('host', '--ready-timeout', '0', '--', 'true'), 'timeout 0'), (('host', '--ready-timeout', '601', '--', 'true'), 'timeout 601'), (('host', '--ready-timeout', 'x', '--', 'true'), 'timeout x'),
        (('host', '--volume', 'rel', '--', 'true'), 'relative volume'), (('host', '--volume', '/a/../b', '--', 'true'), 'volume with ..'), (('host', '--volume', '/a;b', '--', 'true'), 'volume with ;'),
        (('host', '--volume'), 'volume without a value'))
    for args, what in bad_cases:
        with tempfile.TemporaryDirectory() as tmp:
            r = Role(tmp)
            r.start(*args)
            ok = wait_for(r.done, 5)
            st = r.status() if ok else None
            check(ok and st['phase'] == 'terminal' and st['reason'] == 'gate-refused' and st['exit'] == 64, f'a bad invocation ({what}) is terminal gate-refused, exit 64')
            check(alive(r.p), f'after a refusal ({what}) the role blocks like any terminal state')
            finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        marker = os.path.join(tmp, 'ran')
        r.start('host', '--bogus', '--', 'sh', '-c', f': > {marker}')
        check(wait_for(r.done, 5) and not os.path.exists(marker), 'a refused invocation runs nothing')
        finish(r)
    with tempfile.TemporaryDirectory() as tmp:
        r = Role(tmp)
        r.start('host', '--ready-tcp', '5432', '--ready-timeout', '30', '--volume', '/recv', '--', 'true')
        check(wait_for(r.done, 8) and r.status()['reason'] in ('ok', 'error'), 'a full valid option set is accepted')
        finish(r)
finally:
    kill_all()

# the substitutions above are the only differences from the production script
check(ROLE_SRC.startswith('#!/bin/sh\n'), 'role is a /bin/sh script (the Containerfile sets its mode)')
check('/out/$name' in ROLE_SRC and '/work/home /work/tmp' in ROLE_SRC and 'GRACE=20\n' in ROLE_SRC and 'MIN_FREE=1048576\n' in ROLE_SRC, 'the production role has the real /out and /work paths, a 20 s grace and a 1 MiB floor')
for token in ('learn-slot', 'real-dotnet', '/usr/local/libexec/learn'):
    check(token not in ROLE_SRC, f'role carries nothing of the reader profile ({token})')
for token in ('curl', 'wget', 'nc', 'ssh', 'sudo', 'eval'):
    check(not re.search(r'\b' + token + r'\b', ROLE_SRC), f'role contains no {token}')

# ============================================================ 2. the Containerfile
lines = [l for l in CF_SRC.splitlines() if l.strip() and not l.lstrip().startswith('#')]
joined, cur = [], ''
for l in CF_SRC.splitlines():
    if l.lstrip().startswith('#') or not l.strip():
        continue
    cur += l.rstrip('\\').rstrip() + ' ' if l.endswith('\\') else l + '\n'
    if not l.endswith('\\'):
        joined.append(cur.strip())
        cur = ''
check(sum(1 for l in joined if l.startswith('FROM ')) == 1 and joined[0] == f'FROM {B.BASE}', 'one FROM, and it is the admitted learn-qualification:v1 Id 20a6df20 (not reader image r4)')
check(B.BASE.startswith('20a6df20') and 'learn-reader' not in CF_SRC.replace('NOT from reader image r4', ''), 'nothing refers to reader image r4 as a base')
args = [l.split()[1] for l in joined if l.startswith('ARG ')]
check(sorted(args) == sorted(list(B.PIN_ARGS) + [t['list_arg'] for t in B.TREES.values()] + ['MIN_NETCORE', 'MIN_ASPNET']), 'the ARGs are exactly the builder\'s pin arguments')
check([l for l in joined if l.startswith('ENTRYPOINT')] == ['ENTRYPOINT ["/usr/bin/catatonit", "--", "/opt/fsgg/pod/role"]'], 'ENTRYPOINT is catatonit -- /opt/fsgg/pod/role')
check(json.loads(re.search(r'ENTRYPOINT (\[.*\])', CF_SRC).group(1)) == B.WANT_ENTRYPOINT, 'the builder checks the same entrypoint')
check([l for l in joined if l.startswith('CMD')] == ['CMD []'], 'CMD is empty')
users = [l for l in joined if l.startswith('USER ')]
check(users == ['USER 0:0', 'USER 1000:1000'], 'root only while building, USER 1000:1000 at the end')
check(joined.index('USER 1000:1000') > max(i for i, l in enumerate(joined) if l.startswith(('COPY', 'RUN'))), 'no COPY or RUN runs as the unprivileged user')
envs = ' '.join(l for l in joined if l.startswith('ENV '))
check('PATH=' not in envs and 'DOTNET_gcServer=0' in envs and 'HOME=/work/home' in envs and 'TMPDIR=/work/tmp' in envs, 'ENV sets no PATH (no shim), workstation GC off, HOME and TMPDIR under /work')
for tok in ('ADD ', 'curl', 'wget', 'pip ', 'dnf', 'apt', 'pacman', 'dotnet restore', 'dotnet tool', 'git clone', 'http://', 'https://'):
    check(tok not in '\n'.join(joined), f'the Containerfile fetches nothing ({tok.strip()})')
copies = [l for l in joined if l.startswith('COPY ')]
want_copies = {
    'COPY --chmod=0555 role /opt/fsgg/pod/role': 'role',
    'COPY --chmod=0555 learn-probe /opt/fsgg/pod/probe': 'learn-probe',
    'COPY --chmod=0555 host/fsgg-coord-orchestration-host /opt/fsgg/orchestration/host/fsgg-coord-orchestration-host': 'host/fsgg-coord-orchestration-host',
    'COPY --chmod=0555 runner/fsgg-coord-orchestration-runner /opt/fsgg/orchestration/runner/fsgg-coord-orchestration-runner': 'runner/fsgg-coord-orchestration-runner',
    'COPY --chmod=0444 manifest.json /opt/fsgg/orchestration/manifest.json': 'manifest.json',
    'COPY --from=telemetry . /opt/fsgg/telemetry-host/': None,
    'COPY --from=cli . /opt/fsgg/producer/': None}
check(copies == list(want_copies), 'the COPY lines are exactly the pinned artifacts with their modes (executables 0555, manifest 0444, tree from the staged build context)')
for line, rel in want_copies.items():
    if rel:
        check(rel in B.CONTEXT, f'{rel} is a pinned context file')
run_body = next(l for l in joined if l.startswith('RUN '))
for arg, rel in B.PIN_ARGS.items():
    dest = {'role': '/opt/fsgg/pod/role', 'learn-probe': '/opt/fsgg/pod/probe'}.get(rel, '/opt/fsgg/' + rel.replace('host/', 'orchestration/host/').replace('runner/', 'orchestration/runner/').replace('producer/', 'producer/') if rel not in ('manifest.json',) else '/opt/fsgg/orchestration/manifest.json')
    check(f'"${arg}" {dest}' in run_body, f'the RUN step re-hashes {dest} against ${arg}')
check('"$(treelist /opt/fsgg/telemetry-host)" = "$SHA_TELEMETRY_TREE"' in run_body and '"$(treelist /opt/fsgg/producer)" = "$SHA_CLI_TREE"' in run_body and 'sha256sum -c -' in run_body,
      'the RUN step checks both tool trees\' list hashes and the artifact hashes')
check('test -f /opt/fsgg/producer/fsgg-coord-engine.dll' in run_body and 'SHA_CLI"' not in run_body.replace('SHA_CLI_TREE"', ''), 'the producer CLI is a tool tree run with the base\'s dotnet (its dll must be there), not a pinned executable')
check(run_body.index('sha256sum -c -') < run_body.index('RUNTIME'), 'the hashes are checked before anything else is recorded')
check('set -eu' in run_body, 'the RUN step stops at the first failure')

# --- the RUN step, executed in a sandbox
BODY = run_body[len('RUN '):].replace(' \\ ', ' ')


def sandbox(tmp, mutate=None, passwd=None, group=None, runtimes=(('Microsoft.NETCore.App', ['10.0.12']), ('Microsoft.AspNetCore.App', ['10.0.12'])), mins=('10.0.0', '10.0.0'), tamper=None):
    """Lay out what the COPY lines would, in tmp, and run the RUN step with its absolute paths pointed there. Returns (rc, output, tmp-paths)."""
    opt = os.path.join(tmp, 'opt', 'fsgg')
    files = {'pod/role': b'role', 'pod/probe': b'probe', 'orchestration/host/fsgg-coord-orchestration-host': b'host', 'orchestration/runner/fsgg-coord-orchestration-runner': b'runner',
             'orchestration/manifest.json': b'{}'}
    for rel, data in files.items():
        p = os.path.join(opt, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'wb').write(data)
    tree = {'a.dll': b'A', 'b/c.dll': b'C', 'FS.GG.Telemetry.Host.runtimeconfig.json': b'{}'}
    for rel, data in tree.items():
        p = os.path.join(opt, 'telemetry-host', rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'wb').write(data)
    cli_tree = {'fsgg-coord-engine.dll': b'D', 'fsgg-coord-engine.deps.json': b'{}', 'sub/lib.dll': b'L'}
    for rel, data in cli_tree.items():
        p = os.path.join(opt, 'producer', rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        open(p, 'wb').write(data)
    if tamper:
        tamper(opt)
    for fw, vs in runtimes:
        for v in vs:
            os.makedirs(os.path.join(tmp, 'dotnet', 'shared', fw, v))
    os.makedirs(os.path.join(tmp, 'usr', 'bin'), exist_ok=True)
    open(os.path.join(tmp, 'usr', 'bin', 'catatonit'), 'w').write('x')
    os.chmod(os.path.join(tmp, 'usr', 'bin', 'catatonit'), 0o755)
    pw, gr = os.path.join(tmp, 'passwd'), os.path.join(tmp, 'group')
    open(pw, 'w').write(passwd if passwd is not None else 'root:x:0:0:root:/root:/bin/sh\n')
    open(gr, 'w').write(group if group is not None else 'root:x:0:\n')
    s = BODY
    for old, new in (('/opt/fsgg', opt), ('/usr/share/dotnet', os.path.join(tmp, 'dotnet')), ('/etc/passwd', pw), ('/etc/group', gr), ('/usr/bin/catatonit', os.path.join(tmp, 'usr', 'bin', 'catatonit'))):
        check(old in s, f'the RUN step names {old}')
        s = s.replace(old, new)
    h = lambda b: hashlib.sha256(b).hexdigest()
    lst = ''.join(f'{h(d)}  {r}\n' for r, d in sorted(tree.items()))
    lst2 = ''.join(f'{h(d)}  {r}\n' for r, d in sorted(cli_tree.items()))
    env = {'PATH': os.environ['PATH'], 'SHA_ROLE': h(b'role'), 'SHA_PROBE': h(b'probe'), 'SHA_HOST': h(b'host'), 'SHA_RUNNER': h(b'runner'), 'SHA_MANIFEST': h(b'{}'),
           'SHA_TELEMETRY_TREE': h(lst.encode()), 'SHA_CLI_TREE': h(lst2.encode()), 'MIN_NETCORE': mins[0], 'MIN_ASPNET': mins[1]}
    if mutate:
        mutate(env)
    p = subprocess.run(['/bin/sh', '-c', s], capture_output=True, text=True, env=env, timeout=60)
    return p.returncode, p.stdout + p.stderr, {'opt': opt, 'passwd': pw, 'group': gr, 'tmp': tmp}


with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp)
    check(rc == 0, f'the RUN step passes on a consistent layout ({out[-200:]!r})')
    check('RUNTIME Microsoft.NETCore.App 10.0.12 (minimum 10.0.0)' in out and 'RUNTIME Microsoft.AspNetCore.App 10.0.12 (minimum 10.0.0)' in out, 'it prints the runtime versions it found')
    check(B.parse_runtimes(out, {'Microsoft.NETCore.App': '10.0.0', 'Microsoft.AspNetCore.App': '10.0.0'}) == {'Microsoft.NETCore.App': '10.0.12', 'Microsoft.AspNetCore.App': '10.0.12'},
          'the builder parses exactly what the RUN step prints')
    check(oct(os.stat(os.path.join(P['opt'], 'telemetry-host')).st_mode & 0o777) == '0o555' and oct(os.stat(os.path.join(P['opt'], 'telemetry-host', 'b')).st_mode & 0o777) == '0o555'
          and oct(os.stat(os.path.join(P['opt'], 'telemetry-host', 'b', 'c.dll')).st_mode & 0o777) == '0o444', 'the telemetry tree is read-only: directories 0555, files 0444')
    check('pod:x:1000:1000' in open(P['passwd']).read() and 'pod:x:1000:' in open(P['group']).read(), 'a pod user and group at 1000 are added when absent')
for what, mut in (('role', lambda e: e.update(SHA_ROLE='0' * 64)), ('probe', lambda e: e.update(SHA_PROBE='0' * 64)), ('host', lambda e: e.update(SHA_HOST='0' * 64)),
                  ('runner', lambda e: e.update(SHA_RUNNER='0' * 64)), ('manifest', lambda e: e.update(SHA_MANIFEST='0' * 64)),
                  ('telemetry tree list', lambda e: e.update(SHA_TELEMETRY_TREE='0' * 64)), ('CLI tree list', lambda e: e.update(SHA_CLI_TREE='0' * 64))):
    with tempfile.TemporaryDirectory() as tmp:
        rc, out, P = sandbox(tmp, mutate=mut)
        check(rc != 0 and 'RUNTIME' not in out, f'a {what} that differs from the builder\'s pin fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: open(os.path.join(opt, 'telemetry-host', 'b', 'c.dll'), 'w').write('X'))
    check(rc != 0, 'a changed file in the telemetry tree fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: open(os.path.join(opt, 'telemetry-host', 'extra.dll'), 'w').write('X'))
    check(rc != 0, 'an extra file in the telemetry tree fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: os.unlink(os.path.join(opt, 'telemetry-host', 'a.dll')))
    check(rc != 0, 'a missing file in the telemetry tree fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: open(os.path.join(opt, 'producer', 'sub', 'lib.dll'), 'w').write('X'))
    check(rc != 0, 'a changed file in the producer CLI tree fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: open(os.path.join(opt, 'producer', 'extra.dll'), 'w').write('X'))
    check(rc != 0, 'an extra file in the producer CLI tree fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, tamper=lambda opt: os.unlink(os.path.join(opt, 'producer', 'fsgg-coord-engine.dll')))
    check(rc != 0, 'a producer CLI tree without its dll fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp)
    check(oct(os.stat(os.path.join(P['opt'], 'producer')).st_mode & 0o777) == '0o555' and oct(os.stat(os.path.join(P['opt'], 'producer', 'sub', 'lib.dll')).st_mode & 0o777) == '0o444', 'the producer CLI tree is read-only too')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, runtimes=(('Microsoft.NETCore.App', ['10.0.12']),))
    check(rc != 0, 'a missing AspNetCore runtime fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, runtimes=(('Microsoft.NETCore.App', ['9.0.5']), ('Microsoft.AspNetCore.App', ['10.0.12'])))
    check(rc != 0, 'a NETCore runtime below the minimum fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, runtimes=(('Microsoft.NETCore.App', ['10.0.12']), ('Microsoft.AspNetCore.App', ['10.0.9'])), mins=('10.0.0', '10.0.10'))
    check(rc != 0, 'a runtime below a patch-level minimum fails the RUN step')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, runtimes=(('Microsoft.NETCore.App', ['10.0.9', '10.0.12', '10.0.2']), ('Microsoft.AspNetCore.App', ['10.0.9'])), mins=('10.0.10', '10.0.0'))
    check(rc == 0 and 'Microsoft.NETCore.App 10.0.12 (minimum 10.0.10)' in out, 'the highest installed version is compared, by version order (10.0.12 above 10.0.9)')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, runtimes=(('Microsoft.NETCore.App', ['10.0.0']), ('Microsoft.AspNetCore.App', ['10.0.0'])), mins=('10.0.0', '10.0.0'))
    check(rc == 0, 'a runtime equal to the minimum passes')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, passwd='root:x:0:0:r:/root:/bin/sh\nsomeone:x:1000:1000:x:/home/s:/bin/sh\n', group='root:x:0:\nsomeone:x:1000:\n')
    check(rc == 0 and 'pod:' not in open(P['passwd']).read() and 'pod:' not in open(P['group']).read(), 'an existing uid and gid 1000 are left as they are')
with tempfile.TemporaryDirectory() as tmp:
    rc, out, P = sandbox(tmp, passwd='root:x:0:0:r:/root:/bin/sh\npod:x:5:5:x:/:/bin/sh\n')
    check(rc != 0, 'a pod entry at another uid refuses instead of being duplicated')
with tempfile.TemporaryDirectory() as tmp:
    os.makedirs(os.path.join(tmp, 'x'))
    rc, out, P = sandbox(tmp)
    os.unlink(os.path.join(tmp, 'usr', 'bin', 'catatonit'))
    s = BODY.replace('/opt/fsgg', P['opt']).replace('/usr/bin/catatonit', os.path.join(tmp, 'usr', 'bin', 'catatonit'))
    p = subprocess.run(['/bin/sh', '-c', s.split('find ')[0]], capture_output=True, text=True)
    check(p.returncode != 0, 'a base without catatonit fails the RUN step')

# ============================================================ 3. the builder
check(set(B.CONTEXT) == {'Containerfile', 'role', 'learn-probe', 'host/fsgg-coord-orchestration-host', 'runner/fsgg-coord-orchestration-runner', 'manifest.json',
                         'telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg', 'telemetry-host-tree.json', 'producer/FS.GG.Coord.Cli.0.113.0.nupkg', 'coord-cli-tree.json'}, 'the pinned context is exactly the documented file set')
check(B.CONTEXT['Containerfile'] == sha(CF) and B.CONTEXT['role'] == sha(ROLE), 'the pins of the Containerfile and role equal the files in the repository (update the builder when either changes)')
check(B.CONTEXT['telemetry-host-tree.json'] == sha(TREE) and B.CONTEXT['coord-cli-tree.json'] == sha(CLITREE), 'the tree list pins equal the files in the repository')
for rel, want in (('learn-probe', '66f8254e'), ('host/fsgg-coord-orchestration-host', 'b3d6a6e6'), ('runner/fsgg-coord-orchestration-runner', '0e07e813'), ('manifest.json', '6144181f'),
                  ('telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg', '44273495'), ('producer/FS.GG.Coord.Cli.0.113.0.nupkg', '100f0879')):
    check(B.CONTEXT[rel].startswith(want) and re.fullmatch(r'[0-9a-f]{64}', B.CONTEXT[rel]), f'{rel} is pinned to {want}')
check('producer/fsgg-coord-engine' not in B.CONTEXT and not any(v.startswith('5e517e8d') for v in B.CONTEXT.values()), 'the 78 KB apphost (5e517e8d) is no longer a pinned artifact: it cannot run without its .store tree')
check(all(re.fullmatch(r'[0-9a-f]{64}', v) for v in B.CONTEXT.values()) and B.BASE.startswith('20a6df20') and len(B.BASE) == 64, 'every pin is a full sha256')
TREEFILES = {'telemetry': (TREE, 55, 'FS.GG.Telemetry.Host.dll', 'FS.GG.Telemetry.Host.runtimeconfig.json'), 'cli': (CLITREE, 82, 'fsgg-coord-engine.dll', 'fsgg-coord-engine.runtimeconfig.json')}
for key, (path, count, dll, rc_) in TREEFILES.items():
    spec = B.TREES[key]
    tj = json.load(open(path))
    check(tj['schema'] == 'learn-pod-tool-tree/1' and tj['root'] == spec['root'], f'{key}: the tree list has the tool-tree schema and the builder\'s root')
    check(B.tree_list_sha(tj['files']) == tj['listSha256'] == spec['list_sha'], f'{key}: the tree list hash recomputes from the list and equals the builder\'s pin')
    check(tj['archiveSha256'] == B.CONTEXT[spec['nupkg']] and tj['fileCount'] == len(tj['files']) == count and tj['totalBytes'] == sum(f['size'] for f in tj['files']), f'{key}: the tree list names the pinned archive, {count} files')
    check(all(B.NAME_OK.match(f['path']) and '..' not in f['path'].split('/') for f in tj['files']), f'{key}: every listed name is a plain relative path')
    check([f['path'] for f in tj['files']] == sorted((f['path'] for f in tj['files']), key=lambda x: x.encode()), f'{key}: the list is sorted')
    check(dll in {f['path'] for f in tj['files']} and rc_ in {f['path'] for f in tj['files']}, f'{key}: the assembly and its runtimeconfig are in the tree')
    check(max(f['size'] for f in tj['files']) <= spec['per_file_cap'] and tj['totalBytes'] <= spec['total_cap'] and len(tj['files']) <= spec['max_files'], f'{key}: the tree fits its own caps')
check('fsgg-coord-engine.deps.json' in {f['path'] for f in json.load(open(CLITREE))['files']}, 'the CLI tree carries its deps.json (the dll resolves its dependencies from it)')
check(set(B.TREES) == {'telemetry', 'cli'} and B.TREES['cli']['frameworks'] == ('Microsoft.NETCore.App',) and 'Microsoft.AspNetCore.App' in B.TREES['telemetry']['frameworks'],
      'the Telemetry Host needs NETCore and AspNetCore; the CLI needs NETCore only')
check(BUILDER_SRC.startswith('#!/usr/bin/python3 -I\n'), 'the builder is a python3 -I script')

# --- fixtures for a miniature context
UID = os.getuid()


def mini_tree():
    return {'FS.GG.Telemetry.Host.dll': b'dll', 'FS.GG.Telemetry.Host.runtimeconfig.json': json.dumps(
        {'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}, {'name': 'Microsoft.AspNetCore.App', 'version': '10.0.0'}]}}).encode(),
        'sub/x.so': b'so'}


def mini_cli_tree(netcore='10.0.0'):
    return {'fsgg-coord-engine.dll': b'cli-dll', 'fsgg-coord-engine.deps.json': b'{}', 'fsgg-coord-engine.runtimeconfig.json': json.dumps(
        {'runtimeOptions': {'tfm': 'net10.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': netcore}}}).encode(), 'runtimes/native/lib.so': b'native'}


def build_tree(ctx, key, tree, nupkg_extra=None, tree_override=None, drop_member=None, symlink_member=None, sizes_wrong=False, wrong_sha=False, symlink_listed=False, stale_list=False):
    """Write one miniature tool-tree archive and its list under the builder's real names. Returns the list hash. The keyword arguments plant one defect each."""
    spec = B.TREES[key]
    nupkg = os.path.join(ctx, spec['nupkg'])
    with zipfile.ZipFile(nupkg, 'w') as z:
        z.writestr('README.md', 'x')
        for rel, data in tree.items():
            if rel == drop_member:
                continue
            z.writestr(spec['root'] + rel, data)
        for rel, data in (nupkg_extra or {}).items():
            z.writestr(rel, data)
        if symlink_member:
            zi = zipfile.ZipInfo(spec['root'] + symlink_member)
            zi.external_attr = (stat.S_IFLNK | 0o777) << 16
            z.writestr(zi, '/etc/passwd')
    nsha = sha(nupkg)
    if symlink_listed:
        tree = dict(tree, **{'sub/link': b'/etc/passwd'})
        with zipfile.ZipFile(nupkg, 'a') as z:
            zi = zipfile.ZipInfo(spec['root'] + 'sub/link')
            zi.external_attr = (stat.S_IFLNK | 0o777) << 16
            z.writestr(zi, '/etc/passwd')
        nsha = sha(nupkg)
    files = sorted(({'path': r, 'size': len(d) + (1 if sizes_wrong else 0), 'sha256': hashlib.sha256(d).hexdigest()} for r, d in tree.items()), key=lambda f: f['path'])
    if wrong_sha:
        files[0]['sha256'] = '1' * 64
    lsha = B.tree_list_sha(files)
    if stale_list:
        files[0]['sha256'] = '2' * 64                  # the list changed after its hash was taken: listSha256 is the pin but no longer the list's hash
    tj_ = {'schema': 'learn-pod-tool-tree/1', 'package': key, 'version': '0', 'archiveSha256': nsha, 'root': spec['root'], 'fileCount': len(files),
           'totalBytes': sum(f['size'] for f in files), 'listSha256': lsha, 'files': files}
    if tree_override:
        tree_override(tj_)
    open(os.path.join(ctx, spec['json']), 'w').write(json.dumps(tj_))
    return lsha


def mini_context(tmp, tree=None, cli_tree=None, cli=None, **tele):
    """A miniature pinned context: the same layout, small contents, both tool trees. The keyword arguments plant defects in the Telemetry tree, `cli=dict(...)` in the CLI
    tree. Returns (ctx, pins, list_sha by tree, home)."""
    ctx = os.path.join(tmp, 'top', 'ctx')
    for sub in ('telemetry-host', 'host', 'runner', 'producer'):
        os.makedirs(os.path.join(ctx, sub))
    for d in (os.path.join(tmp, 'top'), ctx) + tuple(os.path.join(ctx, x) for x in ('telemetry-host', 'host', 'runner', 'producer')):
        os.chmod(d, 0o755)
    lsha = {'telemetry': build_tree(ctx, 'telemetry', tree if tree is not None else mini_tree(), **tele),
            'cli': build_tree(ctx, 'cli', cli_tree if cli_tree is not None else mini_cli_tree(), **(cli or {}))}
    plain = {'Containerfile': b'FROM x\n', 'role': b'#!/bin/sh\n', 'learn-probe': b'probe', 'host/fsgg-coord-orchestration-host': b'host',
             'runner/fsgg-coord-orchestration-runner': b'runner', 'manifest.json': b'{}'}
    for rel, data in plain.items():
        open(os.path.join(ctx, rel), 'wb').write(data)
    for root, dirs, fs in os.walk(ctx):
        for f in fs:
            os.chmod(os.path.join(root, f), 0o644)
    pins = {rel: sha(os.path.join(ctx, rel)) for rel in list(plain) + [t[k] for t in B.TREES.values() for k in ('nupkg', 'json')]}
    home = os.path.join(tmp, 'home')
    os.makedirs(home)
    return ctx, pins, lsha, home


def deps_for(tmp, ctx, pins, lsha, home, run):
    trees = {k: dict(spec, list_sha=lsha[k]) for k, spec in B.TREES.items()}
    return {'run': run, 'euid': 30034, 'owner_uid': UID, 'ctx': ctx, 'context': pins, 'trees': trees, 'home': home, 'self_path': BUILDER, 'chain_start': os.path.join(tmp, 'top')}


BASE_LAYERS = ['sha256:aa', 'sha256:bb']


def image_json(**over):
    j = {'Id': 'c' * 64, 'Digest': 'sha256:' + 'd' * 64, 'Created': '1970-01-01T00:00:00Z',
         'Config': {'User': '1000:1000', 'Entrypoint': B.WANT_ENTRYPOINT, 'Cmd': None, 'Env': ['PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin', 'DOTNET_gcServer=0', 'HOME=/work/home']},
         'RootFS': {'Layers': BASE_LAYERS + ['sha256:cc']}}
    for k, v in over.items():
        j['Config'][k] = v
    return j


class FakePodman:
    def __init__(self, image=None, build_rc=0, runtimes=True, base_ok=True, boom=False, base_out=None, base_layers=None):
        self.calls, self.image, self.build_rc, self.runtimes, self.base_ok, self.boom = [], image or image_json(), build_rc, runtimes, base_ok, boom
        self.context_listing = None
        self.base_out, self.base_layers = base_out or B.BASE, BASE_LAYERS if base_layers is None else base_layers

    def __call__(self, argv, timeout=900):
        self.calls.append(argv)
        if argv[:3] == ['podman', 'image', 'inspect'] and argv[3:4] == ['--format']:
            return (0, self.base_out + ' ' + json.dumps(self.base_layers), '') if self.base_ok else (1, '', 'no such image')
        if argv[:2] == ['podman', 'build']:
            if self.boom:
                raise RuntimeError('podman exploded')
            self.contexts = {argv[i + 1].split('=', 1)[0]: argv[i + 1].split('=', 1)[1] for i, a in enumerate(argv) if a == '--build-context'}
            self.context_listings = {k: sorted(os.listdir(v)) for k, v in self.contexts.items()}
            self.context_listing = self.context_listings.get('telemetry')
            ba = {argv[i + 1].split('=', 1)[0]: argv[i + 1].split('=', 1)[1] for i, a in enumerate(argv) if a == '--build-arg'}
            out = '' if not self.runtimes else ('STEP\nRUNTIME Microsoft.NETCore.App 10.0.12 (minimum %s)\nRUNTIME Microsoft.AspNetCore.App 10.0.12 (minimum %s)\n'
                                              % (ba['MIN_NETCORE'], ba['MIN_ASPNET']))
            return self.build_rc, out, ''
        if argv[:3] == ['podman', 'image', 'inspect']:
            return 0, json.dumps([self.image]), ''
        return 1, '', 'unexpected'


def run_main(tmp, podman, **ctx_kw):
    ctx, pins, lsha, home = mini_context(tmp, **ctx_kw)
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(deps_for(tmp, ctx, pins, lsha, home, podman))
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    return code, res, buf.getvalue(), (ctx, pins, lsha, home)


# --- a good build
with tempfile.TemporaryDirectory() as tmp:
    pm = FakePodman()
    code, res, text, (ctx, pins, lsha, home) = run_main(tmp, pm)
    check(code == 0 and res['errors'] == [] and res['exitCode'] == 0 and res['schema'] == 'home-learn-build-pod-app/1', 'a consistent context and a good image give exit 0, no errors')
    build = next(c for c in pm.calls if c[:2] == ['podman', 'build'])
    check(build[:7] == ['podman', 'build', '--network=none', '--pull=never', '--no-cache', '--timestamp', '0'], 'the build runs with no network, no pull, no cache, timestamp 0')
    check(build[-5:-3] == ['-f', ctx + '/Containerfile'] and build[-3:-1] == ['-t', B.TAG] and build[-1] == ctx, 'the Containerfile and the tag are the pinned ones, the context is the root-owned copy')
    ba = {build[i + 1].split('=', 1)[0]: build[i + 1].split('=', 1)[1] for i, a in enumerate(build) if a == '--build-arg'}
    check(ba == {'SHA_ROLE': pins['role'], 'SHA_PROBE': pins['learn-probe'], 'SHA_HOST': pins['host/fsgg-coord-orchestration-host'], 'SHA_RUNNER': pins['runner/fsgg-coord-orchestration-runner'],
                 'SHA_MANIFEST': pins['manifest.json'], 'SHA_TELEMETRY_TREE': lsha['telemetry'], 'SHA_CLI_TREE': lsha['cli'], 'MIN_NETCORE': '10.0.0', 'MIN_ASPNET': '10.0.0'},
          'the build arguments are the builder\'s own pins, and the runtime minimums read from the tool trees')
    check(sorted(ba) == sorted(args), 'the build arguments are exactly the ARGs the Containerfile declares')
    check(pm.context_listings == {'telemetry': ['FS.GG.Telemetry.Host.dll', 'FS.GG.Telemetry.Host.runtimeconfig.json', 'sub'],
                                  'cli': ['fsgg-coord-engine.deps.json', 'fsgg-coord-engine.dll', 'fsgg-coord-engine.runtimeconfig.json', 'runtimes']}, 'the two extra build contexts held the extracted tool trees when podman ran')
    ctxs = [build[i + 1] for i, a in enumerate(build) if a == '--build-context']
    check(len(ctxs) == 2 and ctxs[0].startswith('telemetry=') and ctxs[1].startswith('cli='), 'the build has exactly the build contexts telemetry and cli')
    stg = ctxs[0].split('=', 1)[1]
    check(stg.startswith(home + '/.local/state/learn-build/pod-app-r1-') and not os.path.exists(os.path.dirname(stg)) and res['stagingRemoved'], 'the staging directory is in the account state and is removed afterwards')
    check(res['runtimes'] == {'Microsoft.NETCore.App': '10.0.12', 'Microsoft.AspNetCore.App': '10.0.12'} and res['toolTrees'] == {'telemetry': {'files': 3, 'listSha256': lsha['telemetry']}, 'cli': {'files': 4, 'listSha256': lsha['cli']}},
          'the RESULT records the runtime versions and the extracted file counts of both trees')
    check(res['image']['id'] == 'c' * 64 and all(res['gates'].values()) and set(res['gates']) == {'user1000', 'entrypointRole', 'cmdEmpty', 'baseLayersPrefix', 'noShimOnPath', 'workstationGcOff'},
          'the RESULT records the image Id and all six image gates')
    check(res['context'] == pins and res['base'] == B.BASE and res['tag'] == B.TAG, 'the RESULT records the verified context hashes')
    m = re.search(r'RESULT-END\n(\{.*\})\n$', text)
    check(m and json.loads(m.group(1))['exit'] == 0 and json.loads(m.group(1))['sha256'] == hashlib.sha256(text.split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0].encode()).hexdigest(),
          'the last line carries the RESULT\'s own sha256 and the exit code')
    check(not any(c[:2] in (['podman', 'run'], ['podman', 'start'], ['podman', 'exec']) for c in pm.calls), 'no container is run')
    check(set(c[1] for c in pm.calls) == {'image', 'build'}, 'podman is only asked to inspect and to build')

with tempfile.TemporaryDirectory() as tmp:
    try:
        B.main({'euid': 1000})
        check(False, 'the builder refuses other accounts')
    except SystemExit as e:
        check('fsgg-learn-qual' in str(e), 'the builder refuses to run as any account but fsgg-learn-qual')


def refused_build(tmp, what, needle, mutate_ctx=None, pm=None, expect_build=False, **kw):
    pm = pm or FakePodman()
    ctx, pins, lsha, home = mini_context(tmp, **kw)
    if mutate_ctx:
        mutate_ctx(ctx, pins)
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(deps_for(tmp, ctx, pins, lsha, home, pm))
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    check(code == 5 and res['exitCode'] == 5 and any(needle in e for e in res['errors']), f'{what}: refused ({res["errors"]})')
    check(any(c[:2] == ['podman', 'build'] for c in pm.calls) == expect_build, f'{what}: ' + ('built, then refused on the result' if expect_build else 'nothing was built'))
    return res


def edit(rel, data=b'tampered'):
    def f(ctx, pins):
        p = os.path.join(ctx, rel)
        os.chmod(p, 0o644)
        open(p, 'wb').write(data)
    return f


for rel in sorted(B.CONTEXT):
    with tempfile.TemporaryDirectory() as tmp:
        refused_build(tmp, f'a changed {rel}', 'context hash mismatch', mutate_ctx=edit(rel))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an extra file', 'extra', mutate_ctx=lambda ctx, pins: open(os.path.join(ctx, 'surprise'), 'w').write('x'))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a missing file', 'missing', mutate_ctx=lambda ctx, pins: os.unlink(os.path.join(ctx, 'manifest.json')))
with tempfile.TemporaryDirectory() as tmp:
    def sym(ctx, pins):
        p = os.path.join(ctx, 'role')
        os.unlink(p)
        os.symlink(os.path.join(ctx, 'manifest.json'), p)
    refused_build(tmp, 'a symlink in place of a file', 'not a regular file', mutate_ctx=sym)
with tempfile.TemporaryDirectory() as tmp:
    def symdir(ctx, pins):
        shutil.rmtree(os.path.join(ctx, 'host'))
        os.symlink(os.path.join(ctx, 'runner'), os.path.join(ctx, 'host'))
    refused_build(tmp, 'a symlink in place of a directory', 'not a directory', mutate_ctx=symdir)
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a FIFO in the context', 'not a regular file', mutate_ctx=lambda ctx, pins: os.mkfifo(os.path.join(ctx, 'fifo')))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a group-writable file', 'file not root-owned or writable', mutate_ctx=lambda ctx, pins: os.chmod(os.path.join(ctx, 'role'), 0o664))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a world-writable file', 'file not root-owned or writable', mutate_ctx=lambda ctx, pins: os.chmod(os.path.join(ctx, 'role'), 0o646))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a group-writable subdirectory', 'directory not root-owned or writable', mutate_ctx=lambda ctx, pins: os.chmod(os.path.join(ctx, 'host'), 0o775))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a group-writable context directory', 'directory not root-owned or writable', mutate_ctx=lambda ctx, pins: os.chmod(ctx, 0o775))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a world-writable ancestor of the context', 'directory not root-owned or writable', mutate_ctx=lambda ctx, pins: os.chmod(os.path.dirname(ctx), 0o757))
with tempfile.TemporaryDirectory() as tmp:
    def foreign(ctx, pins):
        pass
    ctx, pins, lsha, home = mini_context(tmp)
    buf = io.StringIO()
    d = deps_for(tmp, ctx, pins, lsha, home, FakePodman())
    d['owner_uid'] = UID + 1
    with redirect_stdout(buf):
        code = B.main(d)
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    check(code == 5 and any('not root-owned' in e for e in res['errors']), 'a context owned by another uid than root is refused')
with tempfile.TemporaryDirectory() as tmp:
    ctx, pins, lsha, home = mini_context(tmp)
    d = deps_for(tmp, ctx, pins, lsha, home, FakePodman())
    d['chain_start'] = os.path.join(tmp, 'elsewhere')
    os.makedirs(d['chain_start'])
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(d)
    check(code == 5 and 'not under the chain start' in buf.getvalue(), 'a context outside the chain start is refused')

# --- the tree list and the archive
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list with another list hash', 'list hash differs', tree_override=lambda t: t.update(listSha256='0' * 64))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list naming another archive', 'another archive', tree_override=lambda t: t.update(archiveSha256='0' * 64))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list with a wrong schema', 'wrong schema', tree_override=lambda t: t.update(schema='x/1'))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list with a wrong file count', 'file count', tree_override=lambda t: t.update(fileCount=99))
with tempfile.TemporaryDirectory() as tmp:
    def dotdot(t):
        t['files'][0]['path'] = '../escape'
        t['listSha256'] = B.tree_list_sha(t['files'])
    refused_build(tmp, 'a tree list with a path escaping the tree', 'bad entry', tree_override=dotdot)
with tempfile.TemporaryDirectory() as tmp:
    def absolute(t):
        t['files'][0]['path'] = '/abs'
        t['listSha256'] = B.tree_list_sha(t['files'])
    refused_build(tmp, 'a tree list with an absolute path', 'bad entry', tree_override=absolute)
with tempfile.TemporaryDirectory() as tmp:
    def dup(t):
        t['files'].append(dict(t['files'][0]))
        t['fileCount'] = len(t['files'])
        t['totalBytes'] = sum(f['size'] for f in t['files'])
        t['listSha256'] = B.tree_list_sha(t['files'])
    refused_build(tmp, 'a tree list with a duplicated path', 'bad entry', tree_override=dup)
with tempfile.TemporaryDirectory() as tmp:
    def huge(t):
        t['files'][0]['size'] = B.TREES['telemetry']['per_file_cap'] + 1
        t['totalBytes'] = sum(f['size'] for f in t['files'])
    refused_build(tmp, 'a tree list with an oversize file', 'bad entry', tree_override=huge)
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive with an extra member in the tool tree', 'not hold exactly', nupkg_extra={B.TREES['telemetry']['root'] + 'extra.dll': 'x'})
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive with a member escaping the tree', 'not hold exactly', nupkg_extra={B.TREES['telemetry']['root'] + '../../escape': 'x'})
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive with a missing member', 'not hold exactly', drop_member='sub/x.so')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive with a symlink member', 'not hold exactly', symlink_member='sub/link')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive member whose content differs from the list', 'differs from the list', wrong_sha=True)
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'an archive member whose size differs from the list', 'size differs', sizes_wrong=True)
with tempfile.TemporaryDirectory() as tmp:
    bad = mini_tree()
    bad['FS.GG.Telemetry.Host.runtimeconfig.json'] = json.dumps({'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}]}}).encode()
    refused_build(tmp, 'a tool tree that needs only one of the two frameworks', 'unexpected frameworks', tree=bad)
with tempfile.TemporaryDirectory() as tmp:
    bad = mini_tree()
    bad['FS.GG.Telemetry.Host.runtimeconfig.json'] = json.dumps({'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}, {'name': 'Microsoft.AspNetCore.App', 'version': 'latest'}]}}).encode()
    refused_build(tmp, 'a tool tree with a non-numeric framework version', 'unexpected frameworks', tree=bad)

# --- the base, the build and the image
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a missing base image', 'base image not present', pm=FakePodman(base_ok=False))
with tempfile.TemporaryDirectory() as tmp:
    pm = FakePodman(build_rc=1)
    ctx, pins, lsha, home = mini_context(tmp)
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(deps_for(tmp, ctx, pins, lsha, home, pm))
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    check(code == 5 and 'build failed' in res['errors'] and 'image' not in res and res['stagingRemoved'], 'a failed build is exit 5, records no image, and still removes the staging directory')
with tempfile.TemporaryDirectory() as tmp:
    ctx, pins, lsha, home = mini_context(tmp)
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(deps_for(tmp, ctx, pins, lsha, home, FakePodman(boom=True)))
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    check(code == 5 and any('podman exploded' in e for e in res['errors']) and res['stagingRemoved'], 'an unexpected exception is exit 5 and the staging directory is still removed')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a build log without the runtime lines', 'both shared runtimes', pm=FakePodman(runtimes=False), expect_build=True)
for what, over, gate in (('another user', {'User': '0:0'}, 'user1000'), ('no user', {'User': ''}, 'user1000'),
                         ('another entrypoint', {'Entrypoint': ['/usr/local/libexec/learn/entrypoint']}, 'entrypointRole'), ('catatonit without role', {'Entrypoint': ['/usr/bin/catatonit', '--']}, 'entrypointRole'),
                         ('a command', {'Cmd': ['sh']}, 'cmdEmpty'), ('the shim on PATH', {'Env': ['PATH=/usr/local/libexec/learn/bin:/usr/bin', 'DOTNET_gcServer=0']}, 'noShimOnPath'),
                         ('the pod directory on PATH', {'Env': ['PATH=/opt/fsgg/pod:/usr/bin', 'DOTNET_gcServer=0']}, 'noShimOnPath'), ('server GC on', {'Env': ['PATH=/usr/bin']}, 'workstationGcOff'),
                         ('server GC set to 1', {'Env': ['PATH=/usr/bin', 'DOTNET_gcServer=1']}, 'workstationGcOff')):
    with tempfile.TemporaryDirectory() as tmp:
        refused_build(tmp, f'an image with {what}', 'image gate failed: ' + gate, pm=FakePodman(image=image_json(**over)), expect_build=True)
with tempfile.TemporaryDirectory() as tmp:
    j = image_json()
    j['RootFS']['Layers'] = ['sha256:aa', 'sha256:zz']
    refused_build(tmp, 'an image whose layers do not start with the base\'s', 'image gate failed: baseLayersPrefix', pm=FakePodman(image=j), expect_build=True)
with tempfile.TemporaryDirectory() as tmp:
    j = image_json()
    j['RootFS']['Layers'] = ['sha256:aa']
    refused_build(tmp, 'an image with fewer layers than the base', 'image gate failed: baseLayersPrefix', pm=FakePodman(image=j), expect_build=True)

# --- owners are checked separately for directories and files (a patched lstat reports another uid for one path)
def refused_with_uid(tmp, what, suffix, needle):
    pm = FakePodman()
    ctx, pins, lsha, home = mini_context(tmp)
    real = os.lstat

    def lstat(p, *a, **k):
        st = real(p, *a, **k)
        if str(p).endswith(suffix):
            v = list(st)
            v[4] = UID + 1
            return os.stat_result(v)
        return st
    buf = io.StringIO()
    os.lstat = lstat
    try:
        with redirect_stdout(buf):
            code = B.main(deps_for(tmp, ctx, pins, lsha, home, pm))
    finally:
        os.lstat = real
    res = json.loads(buf.getvalue().split('RESULT-BEGIN\n', 1)[1].split('\nRESULT-END', 1)[0])
    check(code == 5 and any(needle in e for e in res['errors']) and not pm.calls, f'{what}: refused ({res["errors"]})')


with tempfile.TemporaryDirectory() as tmp:
    refused_with_uid(tmp, 'a subdirectory owned by another uid', '/ctx/host', 'directory not root-owned')
with tempfile.TemporaryDirectory() as tmp:
    refused_with_uid(tmp, 'a file owned by another uid', '/ctx/role', 'file not root-owned')
with tempfile.TemporaryDirectory() as tmp:
    refused_with_uid(tmp, 'an ancestor owned by another uid', '/top', 'directory not root-owned')

# --- the tree list is bound to the pin, to its own list and to the archive
with tempfile.TemporaryDirectory() as tmp:
    ctx, pins, lsha, home = mini_context(tmp)
    d = deps_for(tmp, ctx, pins, lsha, home, FakePodman())
    d['trees']['telemetry']['list_sha'] = '0' * 64
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = B.main(d)
    check(code == 5 and 'list hash differs from the pin' in buf.getvalue(), 'a self-consistent tree list whose hash is not the builder\'s pin is refused')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list edited after its hash was taken (listSha256 still equals the pin)', 'list hash differs', stale_list=True)
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a tree list with a wrong totalBytes', 'total size', tree_override=lambda t: t.update(totalBytes=1))
real_cap = B.TREES['telemetry']['total_cap']
B.TREES['telemetry']['total_cap'] = 5
try:
    with tempfile.TemporaryDirectory() as tmp:
        refused_build(tmp, 'a tree list over the total size cap', 'total size')
finally:
    B.TREES['telemetry']['total_cap'] = real_cap
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a listed archive member that is a symlink', 'not a regular file', symlink_listed=True)
with tempfile.TemporaryDirectory() as tmp:
    ctx, pins, lsha, home = mini_context(tmp)
    spec_t = dict(B.TREES['telemetry'], list_sha=lsha['telemetry'])
    files = B.load_tree(ctx, spec_t, pins[spec_t['nupkg']])
    dest = os.path.join(tmp, 'dest')
    os.makedirs(dest)
    open(os.path.join(dest, 'FS.GG.Telemetry.Host.dll'), 'w').write('planted')
    try:
        B.extract_tree(os.path.join(ctx, spec_t['nupkg']), files, dest, spec_t)
        check(False, 'extract_tree overwrote a file in the staging directory')
    except FileExistsError:
        check(True, 'extract_tree never overwrites: a file already in the staging directory is an error (O_EXCL)')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a base image Id that is not the pinned one', 'base image not present', pm=FakePodman(base_out='9' * 64))
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a base image with no layers', 'image gate failed: baseLayersPrefix', pm=FakePodman(base_layers=[]), expect_build=True)

# --- the producer CLI tool tree (FS.GG.Coord.Cli 0.113.0): a framework-dependent tool, pinned and extracted like the Telemetry Host
with tempfile.TemporaryDirectory() as tmp:
    pm = FakePodman()
    code, res, text, _ = run_main(tmp, pm, cli_tree=mini_cli_tree(netcore='10.0.5'))
    build = next(c for c in pm.calls if c[:2] == ['podman', 'build'])
    ba = {build[i + 1].split('=', 1)[0]: build[i + 1].split('=', 1)[1] for i, a in enumerate(build) if a == '--build-arg'}
    check(code == 0 and ba['MIN_NETCORE'] == '10.0.5' and ba['MIN_ASPNET'] == '10.0.0' and res['runtimeMinimums'] == {'Microsoft.NETCore.App': '10.0.5', 'Microsoft.AspNetCore.App': '10.0.0'},
          'the minimum for a framework is the highest any tree names (the CLI needs NETCore 10.0.5, the Host AspNetCore 10.0.0)')
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a CLI tool tree needing AspNetCore too', 'unexpected frameworks', cli_tree={**mini_cli_tree(), 'fsgg-coord-engine.runtimeconfig.json': json.dumps(
        {'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}, {'name': 'Microsoft.AspNetCore.App', 'version': '10.0.0'}]}}).encode()})
with tempfile.TemporaryDirectory() as tmp:
    refused_build(tmp, 'a CLI tool tree whose runtimeconfig names a framework twice', 'unexpected frameworks', cli_tree={**mini_cli_tree(), 'fsgg-coord-engine.runtimeconfig.json': json.dumps(
        {'runtimeOptions': {'framework': {'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}, 'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}]}}).encode()})
for what, kw, needle in (('a CLI tool tree member whose content differs from the list', dict(wrong_sha=True), 'differs from the list'),
                         ('a CLI tool tree list edited after its hash was taken', dict(stale_list=True), 'list hash differs'),
                         ('an extra member in the CLI archive', dict(nupkg_extra={B.TREES['cli']['root'] + 'extra.dll': 'x'}), 'not hold exactly'),
                         ('a missing member in the CLI archive', dict(drop_member='runtimes/native/lib.so'), 'not hold exactly'),
                         ('a listed CLI archive member that is a symlink', dict(symlink_listed=True), 'not a regular file'),
                         ('a CLI tool tree list with a wrong size', dict(sizes_wrong=True), 'size differs'),
                         ('a CLI tool tree list with a wrong totalBytes', dict(tree_override=lambda t: t.update(totalBytes=1)), 'total size'),
                         ('a CLI tool tree list naming another archive', dict(tree_override=lambda t: t.update(archiveSha256='0' * 64)), 'another archive'),
                         ('a CLI tool tree list with the Telemetry root', dict(tree_override=lambda t: t.update(root=B.TREES['telemetry']['root'])), 'wrong schema or root')):
    with tempfile.TemporaryDirectory() as tmp:
        refused_build(tmp, what, needle, cli=kw)
real_total = B.TREES['cli']['total_cap']
B.TREES['cli']['total_cap'] = 5
try:
    with tempfile.TemporaryDirectory() as tmp:
        refused_build(tmp, 'a CLI tool tree over its own total size cap', 'total size')
finally:
    B.TREES['cli']['total_cap'] = real_total
check(B.TREES['cli']['total_cap'] > 69140925 and B.TREES['cli']['total_cap'] > B.TREES['telemetry']['total_cap'], 'the CLI cap fits the real 69 MB tree and is its own (larger than the Host\'s)')
with tempfile.TemporaryDirectory() as tmp:
    d_ = os.path.join(tmp, 'cfg')
    os.makedirs(d_)
    one = lambda body: (open(os.path.join(d_, 'fsgg-coord-engine.runtimeconfig.json'), 'w').write(json.dumps(body)), B.runtime_minimums(d_, B.TREES['cli']))[1]
    check(one({'runtimeOptions': {'framework': {'name': 'Microsoft.NETCore.App', 'version': '10.0.0'}}}) == {'Microsoft.NETCore.App': '10.0.0'}, 'runtime_minimums reads the singular framework shape the CLI uses')
    check(one({'runtimeOptions': {'frameworks': [{'name': 'Microsoft.NETCore.App', 'version': '10.0.3'}]}}) == {'Microsoft.NETCore.App': '10.0.3'}, 'runtime_minimums reads the list shape too')
    for what, body in (('no runtimeOptions', {}), ('a prerelease version', {'runtimeOptions': {'framework': {'name': 'Microsoft.NETCore.App', 'version': '10.0.0-rc.1'}}}),
                       ('another framework', {'runtimeOptions': {'framework': {'name': 'Microsoft.WindowsDesktop.App', 'version': '10.0.0'}}}),
                       ('a non-string version', {'runtimeOptions': {'framework': {'name': 'Microsoft.NETCore.App', 'version': 10}}})):
        try:
            one(body)
            check(False, f'runtime_minimums accepted {what}')
        except B.Refuse:
            check(True, f'runtime_minimums refuses {what}')
check(B.merge_minimums({'a': {'Microsoft.NETCore.App': '10.0.9', 'Microsoft.AspNetCore.App': '10.0.1'}, 'b': {'Microsoft.NETCore.App': '10.0.10'}}) == {'Microsoft.NETCore.App': '10.0.10', 'Microsoft.AspNetCore.App': '10.0.1'},
      'merge_minimums takes the highest per framework by version order (10.0.10 above 10.0.9)')

# --- parse_runtimes
MINS = {'Microsoft.NETCore.App': '10.0.0', 'Microsoft.AspNetCore.App': '10.0.0'}
L1, L2 = 'RUNTIME Microsoft.NETCore.App 10.0.12 (minimum 10.0.0)\n', 'RUNTIME Microsoft.AspNetCore.App 10.0.12 (minimum 10.0.0)\n'
check(B.parse_runtimes('x\n' + L1 + 'y\n' + L2, MINS) == {'Microsoft.NETCore.App': '10.0.12', 'Microsoft.AspNetCore.App': '10.0.12'}, 'both runtime lines are parsed from a longer log')
for what, log in (('one line missing', L1), ('a repeated line', L1 + L1 + L2), ('a line below the minimum', L1.replace('10.0.12', '9.0.5') + L2),
                  ('a line for another minimum', L1.replace('minimum 10.0.0', 'minimum 9.0.0') + L2), ('an indented line', ' ' + L1 + L2), ('a line with a suffix', L1.strip() + ' x\n' + L2)):
    try:
        B.parse_runtimes(log, MINS)
        check(False, f'parse_runtimes refuses {what}')
    except B.Refuse:
        check(True, f'parse_runtimes refuses {what}')

# --- the extraction function against the real archive, when it is available (not counted: CI does not hold the 6.8 MB private archive)
print(f'learn-pod-image: {n} assertions passed')

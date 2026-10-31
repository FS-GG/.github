#!/usr/bin/python3 -I
"""LEARN finite executor: build of the pod image learn-pod-app:r1 (design docs/coordination/2026-10-30-learn-finite-executor-pod-design.md section 2.2 and
2.3; Refs FS-GG/.github#4647). Runs as the LEARN account only:
    sudo -n -u fsgg-learn-qual /usr/local/libexec/fsgg-learn/learn-build-pod-app-r1.py
THIS FILE IS SOURCE. Nothing here is installed, admitted or run: Root installs it from a root-owned copy and admits every build.

Build context = the root-owned copy under /usr/local/libexec/fsgg-learn/pod-image-r1/ (layout in README.md). Before the build it
  1 checks the context: every directory up to / and inside it is a root-owned directory not writable by group or other, every file is a root-owned regular
    file not writable by group or other, there is no symlink, and the file set and every sha256 equal the constants below (no extra, no missing file);
  2 extracts the Telemetry Host tool tree from the pinned nupkg into a private staging directory, member by member (no path with .., no absolute path, no
    symlink, no member outside tools/net10.0/linux-x64/, a size cap), and checks the file set and every sha256 against telemetry-host-tree.json, whose own
    sha256 and list hash are pinned here; it reads the minimum .NET runtime versions from the tree's runtimeconfig.json;
  3 builds:  podman build --network=none --pull=never --no-cache --timestamp 0 --build-arg <pins> --build-context telemetry=<staging> -f Containerfile
            -t localhost/learn-pod-app:r1 <context>
    The pins go in as build arguments taken from THIS file's constants, and the Containerfile's RUN step re-hashes what was copied into the image, so a file
    swapped between step 1 and the build fails inside the build. The same RUN step fails the build unless both shared runtimes exist at or above the
    minimum, and prints their versions, which this script records;
  4 asserts on the built image: Config.User 1000:1000, Entrypoint [catatonit, --, /opt/fsgg/pod/role], empty Cmd, the base's layers are a prefix of the
    image's, PATH is the base's (no shim), DOTNET_gcServer=0; and records its Id (the digest to admit).
It writes only the account's own podman storage and the staging directory (removed afterwards). It starts no container.
Result to stdout between RESULT-BEGIN and RESULT-END, then one line with its sha256. Exit: 0 ok; 5 refused or failed.
"""
import hashlib, json, os, re, shutil, stat, subprocess, sys, tempfile, zipfile
from datetime import datetime, timezone

SCHEMA = 'home-learn-build-pod-app/1'
CTX = '/usr/local/libexec/fsgg-learn/pod-image-r1'
BASE = '20a6df20169ce6721ed91fddd0d24ae7fe8de655008b422df0c3299821b7d4b8'
TAG = 'localhost/learn-pod-app:r1'
# context file -> sha256. The Containerfile and role pins change whenever those files change (tests/learn-pod-image checks them against the repository).
CONTEXT = {
    'Containerfile': '5cdda3598184c186be6af9d65e7ef69713f8b4167b225b60f945ad821ead49c9',
    'role': 'd1fbbbb398b85748723e6e04b20053a00187ab49a7ef73eabc130f1d58f0eff2',
    'learn-probe': '66f8254ec7abf6469a3dd354f3846dbc557fd19762ca8189c484bc03557fb303',
    'host/fsgg-coord-orchestration-host': 'b3d6a6e664cf55ad25a110daa3bf1578a6f6210755e22857f5bddff139877c47',
    'runner/fsgg-coord-orchestration-runner': '0e07e81306aed69f443d3abe9449a54ef38e05d436146c24a811c903be9f9768',
    'manifest.json': '6144181f413c7f9dfe971c9170de92e54a9bc54c21019efd452809d2100256cf',
    'producer/fsgg-coord-engine': '5e517e8da543c753e9f15fa23acfc268f0c70deaa52e9b683af24d61cebff725',
    'telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg': '44273495ed12083b0c9762818ca0ef5688418e6fbc46bde9f2565b400c5aba89',
    'telemetry-host-tree.json': 'dac7f73e851e74fc18156aa20384749cf72df70dd4bef59fd1f99f4724560ee1',
}
TREE_LIST_SHA = '4b4006978a5ac45ffdbb328b51c3431acd80d3fa0c1efc667d05cfb4a42b3fca'
NUPKG = 'telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg'
TREE_JSON = 'telemetry-host-tree.json'
TREE_ROOT = 'tools/net10.0/linux-x64/'
PER_FILE_CAP, TREE_CAP, TREE_MAX_FILES = 16 << 20, 64 << 20, 200
FRAMEWORKS = ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')
PIN_ARGS = {'SHA_ROLE': 'role', 'SHA_PROBE': 'learn-probe', 'SHA_HOST': 'host/fsgg-coord-orchestration-host',
            'SHA_RUNNER': 'runner/fsgg-coord-orchestration-runner', 'SHA_MANIFEST': 'manifest.json', 'SHA_CLI': 'producer/fsgg-coord-engine'}
WANT_ENTRYPOINT = ['/usr/bin/catatonit', '--', '/opt/fsgg/pod/role']
NAME_OK = re.compile(r'^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$')
VERSION = re.compile(r'^\d+\.\d+\.\d+$')
RUNTIME_LINE = re.compile(r'^RUNTIME (Microsoft\.(?:NETCore|AspNetCore)\.App) (\d+\.\d+\.\d+) \(minimum (\d+\.\d+\.\d+)\)$', re.M)


class Refuse(Exception):
    pass


def sha_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for c in iter(lambda: f.read(1 << 20), b''):
            h.update(c)
    return h.hexdigest()


def version_tuple(v):
    return tuple(int(x) for x in v.split('.'))


def check_dir(path, owner_uid):
    st = os.lstat(path)
    if not stat.S_ISDIR(st.st_mode):
        raise Refuse(f'not a directory: {path}')
    if st.st_uid != owner_uid or (st.st_mode & 0o022):
        raise Refuse(f'directory not root-owned or writable by group or other: {path}')


def verify_context(ctx, pins, owner_uid=0, chain_start='/'):
    """Returns {relative path: sha256}. Raises Refuse. The chain from chain_start (/ in production) to ctx, every directory under it and every file are
    checked first; hashing comes last."""
    if ctx != chain_start and not ctx.startswith(chain_start.rstrip('/') + '/'):
        raise Refuse('the context is not under the chain start')
    parts = [c for c in ctx[len(chain_start):].split('/') if c]
    cur = chain_start
    check_dir(cur, owner_uid)
    for c in parts:
        cur = os.path.join(cur, c)
        check_dir(cur, owner_uid)
    found = {}
    for root, dirs, files in os.walk(ctx, followlinks=False, onerror=lambda e: (_ for _ in ()).throw(Refuse(f'cannot list: {e}'))):
        for d in dirs:
            check_dir(os.path.join(root, d), owner_uid)        # a symlink to a directory is not a directory by lstat: refused here
        for f in files:
            p = os.path.join(root, f)
            st = os.lstat(p)
            if not stat.S_ISREG(st.st_mode):
                raise Refuse(f'not a regular file: {os.path.relpath(p, ctx)}')
            if st.st_uid != owner_uid or (st.st_mode & 0o022):
                raise Refuse(f'file not root-owned or writable by group or other: {os.path.relpath(p, ctx)}')
            found[os.path.relpath(p, ctx)] = p
    if sorted(found) != sorted(pins):
        extra, missing = sorted(set(found) - set(pins)), sorted(set(pins) - set(found))
        raise Refuse(f'the build context is not exactly the pinned file set (extra {extra}, missing {missing})')
    got = {}
    for rel in sorted(found):
        got[rel] = sha_file(found[rel])
        if got[rel] != pins[rel]:
            raise Refuse(f'context hash mismatch: {rel}')
    return got


def tree_list_sha(files):
    text = ''.join(f"{f['sha256']}  {f['path']}\n" for f in sorted(files, key=lambda f: f['path'].encode()))
    return hashlib.sha256(text.encode()).hexdigest()


def load_tree(ctx, list_sha, nupkg_sha):
    j = json.loads(open(os.path.join(ctx, TREE_JSON), 'rb').read())
    files = j.get('files')
    if j.get('schema') != 'learn-pod-telemetry-tree/1' or j.get('root') != TREE_ROOT or not isinstance(files, list):
        raise Refuse('telemetry tree list: wrong schema')
    if j.get('archiveSha256') != nupkg_sha:
        raise Refuse('telemetry tree list names another archive')
    if not 0 < len(files) <= TREE_MAX_FILES or j.get('fileCount') != len(files):
        raise Refuse('telemetry tree list: file count')
    seen = set()
    for f in files:
        p = f.get('path')
        if (not isinstance(p, str) or not NAME_OK.match(p) or '..' in p.split('/') or p in seen or not isinstance(f.get('size'), int)
                or not 0 <= f['size'] <= PER_FILE_CAP or not re.fullmatch(r'[0-9a-f]{64}', str(f.get('sha256')))):
            raise Refuse(f'telemetry tree list: bad entry {str(p)[:80]}')
        seen.add(p)
    if sum(f['size'] for f in files) > TREE_CAP or j.get('totalBytes') != sum(f['size'] for f in files):
        raise Refuse('telemetry tree list: total size')
    if tree_list_sha(files) != j.get('listSha256') or j['listSha256'] != list_sha:
        raise Refuse('telemetry tree list: list hash differs from the pin')
    return files


def extract_tree(nupkg, files, dest):
    """Extract exactly the listed members of nupkg into dest (an existing private directory). Raises Refuse on any difference from the list."""
    want = {f['path']: f for f in files}
    done = set()
    with zipfile.ZipFile(nupkg) as z:
        infos = [i for i in z.infolist() if i.filename.startswith(TREE_ROOT) and not i.filename.endswith('/')]
        names = [i.filename[len(TREE_ROOT):] for i in infos]
        if len(set(names)) != len(names) or sorted(names) != sorted(want):
            raise Refuse('the archive does not hold exactly the listed tool tree')
        for i in infos:
            rel = i.filename[len(TREE_ROOT):]
            f = want[rel]
            kind = (i.external_attr >> 16) & 0o170000
            if kind not in (0, stat.S_IFREG):
                raise Refuse(f'archive member is not a regular file: {rel}')
            if i.file_size != f['size'] or i.file_size > PER_FILE_CAP:
                raise Refuse(f'archive member size differs: {rel}')
            target = os.path.join(dest, *rel.split('/'))
            os.makedirs(os.path.dirname(target), mode=0o700, exist_ok=True)
            h, n = hashlib.sha256(), 0
            fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o644)
            with os.fdopen(fd, 'wb') as o, z.open(i) as src:
                for c in iter(lambda: src.read(1 << 20), b''):
                    n += len(c)
                    if n > f['size']:
                        raise Refuse(f'archive member larger than its size: {rel}')
                    h.update(c)
                    o.write(c)
                o.flush()
                os.fsync(o.fileno())
            if n != f['size'] or h.hexdigest() != f['sha256']:
                raise Refuse(f'extracted file differs from the list: {rel}')
            done.add(rel)
    if done != set(want):
        raise Refuse('not every listed file was extracted')
    return sorted(done)


def runtime_minimums(dest):
    j = json.loads(open(os.path.join(dest, 'FS.GG.Telemetry.Host.runtimeconfig.json'), 'rb').read())
    fw = {x.get('name'): x.get('version') for x in (j.get('runtimeOptions') or {}).get('frameworks') or []}
    if sorted(fw) != sorted(FRAMEWORKS) or not all(isinstance(v, str) and VERSION.match(v) for v in fw.values()):
        raise Refuse(f'the tool tree needs unexpected frameworks: {sorted(fw)}')
    return fw


def build_argv(ctx, staging, mins, pins):
    args = []
    for name, rel in PIN_ARGS.items():
        args += ['--build-arg', f'{name}={pins[rel]}']
    args += ['--build-arg', f'SHA_TREE_LIST={TREE_LIST_SHA}', '--build-arg', f'MIN_NETCORE={mins["Microsoft.NETCore.App"]}',
             '--build-arg', f'MIN_ASPNET={mins["Microsoft.AspNetCore.App"]}']
    return (['podman', 'build', '--network=none', '--pull=never', '--no-cache', '--timestamp', '0'] + args +
            ['--build-context', f'telemetry={staging}', '-f', ctx + '/Containerfile', '-t', TAG, ctx])


def parse_runtimes(log, mins):
    """The RUN step prints one RUNTIME line per framework. Returns {name: version}; raises Refuse if one is missing, repeated or below its minimum."""
    seen = {}
    for name, version, minimum in RUNTIME_LINE.findall(log):
        if name in seen or minimum != mins[name] or version_tuple(version) < version_tuple(minimum):
            raise Refuse(f'runtime line for {name} is repeated, not for the pinned minimum, or below it')
        seen[name] = version
    if sorted(seen) != sorted(FRAMEWORKS):
        raise Refuse('the build log does not show both shared runtimes')
    return seen


def image_gates(j, base_layers):
    cfg = j.get('Config') or {}
    layers = (j.get('RootFS') or {}).get('Layers') or []
    env = cfg.get('Env') or []
    path = next((e[5:] for e in env if e.startswith('PATH=')), '')
    return {'user1000': cfg.get('User') == '1000:1000', 'entrypointRole': cfg.get('Entrypoint') == WANT_ENTRYPOINT, 'cmdEmpty': not cfg.get('Cmd'),
            'baseLayersPrefix': bool(base_layers) and layers[:len(base_layers)] == base_layers, 'noShimOnPath': 'learn' not in path and '/opt/fsgg' not in path,
            'workstationGcOff': 'DOTNET_gcServer=0' in env}


def default_deps():
    def run(argv, timeout=900):
        try:
            p = subprocess.run(argv, capture_output=True, text=True, timeout=timeout, cwd='/')
            return p.returncode, p.stdout, p.stderr
        except subprocess.TimeoutExpired:
            return None, '', 'timeout'
    return {'run': run, 'euid': os.geteuid(), 'owner_uid': 0, 'ctx': CTX, 'context': CONTEXT, 'tree_list_sha': TREE_LIST_SHA,
            'home': '/mnt/storage/containers/learn-qualification/account', 'self_path': os.path.abspath(__file__), 'production': True}


def main(deps=None):
    d = deps or default_deps()
    if d['euid'] != 30034:
        sys.exit('refuse: run as fsgg-learn-qual')
    if d.get('production'):
        os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus', HOME=d['home'], PATH='/usr/bin', LC_ALL='C')
        os.chdir('/')
    run = d['run']
    R = {'schema': SCHEMA, 'scriptSha256': sha_file(d['self_path']), 'base': BASE, 'tag': TAG, 'utc': datetime.now(timezone.utc).isoformat(),
         'account': 'fsgg-learn-qual', 'errors': []}
    staging = None
    try:
        R['context'] = verify_context(d['ctx'], d['context'], d['owner_uid'], d.get('chain_start', '/'))
        files = load_tree(d['ctx'], d['tree_list_sha'], d['context'][NUPKG])
        rc, out, _ = run(['podman', 'image', 'inspect', '--format', '{{.Id}} {{json .RootFS.Layers}}', BASE], 60)
        if rc != 0 or not out.startswith(BASE):
            raise Refuse('base image not present')
        base_layers = json.loads(out.split(' ', 1)[1])
        state = d['home'] + '/.local/state/learn-build'
        os.makedirs(state, mode=0o700, exist_ok=True)
        staging = tempfile.mkdtemp(prefix='pod-app-r1-', dir=state)
        os.chmod(staging, 0o700)
        R['telemetryTree'] = {'files': len(extract_tree(os.path.join(d['ctx'], NUPKG), files, staging)), 'listSha256': d['tree_list_sha']}
        mins = runtime_minimums(staging)
        R['runtimeMinimums'] = mins
        argv = build_argv(d['ctx'], staging, mins, R['context'])
        R['buildArgv'] = argv
        rc, out, err = run(argv, 1800)
        R['build'] = {'rc': rc, 'stdoutTail': out[-3000:], 'stderrTail': err[-2000:]}
        if rc != 0:
            raise Refuse('build failed')
        R['runtimes'] = parse_runtimes(out + '\n' + err, mins)
        rc, out, _ = run(['podman', 'image', 'inspect', TAG], 60)
        j = json.loads(out)[0]
        cfg = j.get('Config') or {}
        layers = (j.get('RootFS') or {}).get('Layers') or []
        R['image'] = {'id': j.get('Id'), 'digest': j.get('Digest'), 'configUser': cfg.get('User'), 'entrypoint': cfg.get('Entrypoint'), 'cmd': cfg.get('Cmd'),
                      'envNames': sorted(e.split('=', 1)[0] for e in cfg.get('Env') or []), 'layers': len(layers), 'created': j.get('Created')}
        R['gates'] = image_gates(j, base_layers)
        if not all(R['gates'].values()):
            raise Refuse('image gate failed: ' + ','.join(k for k, v in R['gates'].items() if not v))
    except Refuse as e:
        R['errors'].append(str(e))
    except Exception as e:
        R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}')
    finally:
        if staging:
            shutil.rmtree(staging, ignore_errors=True)
            R['stagingRemoved'] = not os.path.exists(staging)
    code = 5 if R['errors'] else 0
    R['exitCode'] = code
    b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN')
    print(b)
    print('RESULT-END')
    print(json.dumps({'sha256': hashlib.sha256(b.encode()).hexdigest(), 'exit': code}))
    return code


if __name__ == '__main__':
    sys.exit(main())

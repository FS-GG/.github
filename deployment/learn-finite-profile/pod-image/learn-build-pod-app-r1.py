#!/usr/bin/python3 -I
"""LEARN finite executor: build of the pod image learn-pod-app:r1 (design docs/coordination/2026-10-30-learn-finite-executor-pod-design.md section 2.2 and
2.3; Refs FS-GG/.github#4647). Runs as the LEARN account only:
    sudo -n -u fsgg-learn-qual /usr/local/libexec/fsgg-learn/learn-build-pod-app-r1.py
THIS FILE IS SOURCE. Nothing here is installed, admitted or run: Root installs it from a root-owned copy and admits every build.

Build context = the root-owned copy under /usr/local/libexec/fsgg-learn/pod-image-r1/ (layout in README.md). Before the build it
  1 checks the context: every directory up to / and inside it is a root-owned directory not writable by group or other, every file is a root-owned regular
    file not writable by group or other, there is no symlink, and the file set and every sha256 equal the constants below (no extra, no missing file);
  2 extracts the two .NET tool trees, the Telemetry Host 0.8.0 and the producer CLI FS.GG.Coord.Cli 0.113.0 (both run with the base's dotnet), from their pinned nupkgs
    into private staging directories, member by member (no path with .., no absolute path, no symlink, nothing outside the tree's root, size caps), and checks each
    file set and every sha256 against telemetry-host-tree.json and coord-cli-tree.json, whose own sha256 and list hash are pinned here; it reads the minimum .NET
    runtime versions from each tree's runtimeconfig.json;
  3 builds:  podman build --network=none --pull=never --no-cache --timestamp 0 --build-arg <pins> --build-context telemetry=<staging> --build-context cli=<staging>
            -f Containerfile -t localhost/learn-pod-app:r1 <context>
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
    'Containerfile': '02c0c69cbde58c1bc5c8b4fa337e182d8f6616e4789119728144490584ac7fe1',
    'role': 'f2d9a062113032a94309dd030df768663bf7e1a9895e8c986e20a5575d3801d9',
    'learn-probe': '66f8254ec7abf6469a3dd354f3846dbc557fd19762ca8189c484bc03557fb303',
    'host/fsgg-coord-orchestration-host': 'b3d6a6e664cf55ad25a110daa3bf1578a6f6210755e22857f5bddff139877c47',
    'runner/fsgg-coord-orchestration-runner': '0e07e81306aed69f443d3abe9449a54ef38e05d436146c24a811c903be9f9768',
    'manifest.json': '6144181f413c7f9dfe971c9170de92e54a9bc54c21019efd452809d2100256cf',
    'telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg': '44273495ed12083b0c9762818ca0ef5688418e6fbc46bde9f2565b400c5aba89',
    'telemetry-host-tree.json': 'cf91c97abee180ea08accbdf81a53205bd87595ec01c77b16014efaac955b4a6',
    'producer/FS.GG.Coord.Cli.0.113.0.nupkg': '100f0879436d41df98aa0879aa8ea279dde3e39d3119733875744ac8e2f79789',
    'coord-cli-tree.json': '79267cd19b4fa78f9bad20416497ac7cda2d73ba17e8102307fe29410b255acb',
}
# The two .NET tool trees. `list_arg` is the Containerfile ARG that carries the tree's list hash; `frameworks` are the shared frameworks its runtimeconfig.json
# may name. The CLI is a framework-dependent tool (an apphost of 78 KB plus its dll and dependencies), not a self-contained executable.
TREES = {
    'telemetry': {'nupkg': 'telemetry-host/FS.GG.Telemetry.Host.0.8.0.nupkg', 'json': 'telemetry-host-tree.json', 'root': 'tools/net10.0/linux-x64/',
                  'list_sha': '4b4006978a5ac45ffdbb328b51c3431acd80d3fa0c1efc667d05cfb4a42b3fca', 'list_arg': 'SHA_TELEMETRY_TREE',
                  'runtimeconfig': 'FS.GG.Telemetry.Host.runtimeconfig.json', 'frameworks': ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'),
                  'per_file_cap': 16 << 20, 'total_cap': 64 << 20, 'max_files': 200},
    'cli': {'nupkg': 'producer/FS.GG.Coord.Cli.0.113.0.nupkg', 'json': 'coord-cli-tree.json', 'root': 'tools/net10.0/any/',
            'list_sha': '815791cd2b47fa2e79d5446bb8268447b2148391980d1300ee343ab99473a54d', 'list_arg': 'SHA_CLI_TREE',
            'runtimeconfig': 'fsgg-coord-engine.runtimeconfig.json', 'frameworks': ('Microsoft.NETCore.App',),
            'per_file_cap': 16 << 20, 'total_cap': 96 << 20, 'max_files': 200},
}
FRAMEWORKS = ('Microsoft.NETCore.App', 'Microsoft.AspNetCore.App')
PIN_ARGS = {'SHA_ROLE': 'role', 'SHA_PROBE': 'learn-probe', 'SHA_HOST': 'host/fsgg-coord-orchestration-host',
            'SHA_RUNNER': 'runner/fsgg-coord-orchestration-runner', 'SHA_MANIFEST': 'manifest.json'}
WANT_ENTRYPOINT = ['/usr/bin/catatonit', '--', '/opt/fsgg/pod/role']
NAME_OK = re.compile(r'^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)*$')
VERSION = re.compile(r'^\d+\.\d+\.\d+$')
RUNTIME_LINE = re.compile(r'^RUNTIME (Microsoft\.(?:NETCore|AspNetCore)\.App) (\d+\.\d+\.\d+) \(minimum (\d+\.\d+\.\d+)\)$', re.M)


class Refuse(Exception):
    pass


def refuse(why):
    raise Refuse(why)


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


def load_tree(ctx, spec, nupkg_sha):
    """The tree list of one tool tree, validated against its spec (root, caps, list hash pin) and against the archive it names."""
    j = json.loads(open(os.path.join(ctx, spec['json']), 'rb').read())
    files = j.get('files')
    if j.get('schema') != 'learn-pod-tool-tree/1' or j.get('root') != spec['root'] or not isinstance(files, list):
        refuse('tool tree list: wrong schema or root')
    if j.get('archiveSha256') != nupkg_sha:
        refuse('tool tree list names another archive')
    if not 0 < len(files) <= spec['max_files'] or j.get('fileCount') != len(files):
        refuse('tool tree list: file count')
    seen = set()
    for f in files:
        p = f.get('path')
        if (not isinstance(p, str) or not NAME_OK.match(p) or '..' in p.split('/') or p in seen or not isinstance(f.get('size'), int)
                or not 0 <= f['size'] <= spec['per_file_cap'] or not re.fullmatch(r'[0-9a-f]{64}', str(f.get('sha256')))):
            refuse(f'tool tree list: bad entry {str(p)[:80]}')
        seen.add(p)
    if sum(f['size'] for f in files) > spec['total_cap'] or j.get('totalBytes') != sum(f['size'] for f in files):
        refuse('tool tree list: total size')
    if tree_list_sha(files) != j.get('listSha256') or j['listSha256'] != spec['list_sha']:
        refuse('tool tree list: list hash differs from the pin')
    return files


def extract_tree(nupkg, files, dest, spec):
    """Extract exactly the listed members of nupkg's tool root into dest (an existing private directory). Raises Refuse on any difference from the list."""
    root = spec['root']
    want = {f['path']: f for f in files}
    done = set()
    with zipfile.ZipFile(nupkg) as z:
        infos = [i for i in z.infolist() if i.filename.startswith(root) and not i.filename.endswith('/')]
        names = [i.filename[len(root):] for i in infos]
        if len(set(names)) != len(names) or sorted(names) != sorted(want):
            refuse('the archive does not hold exactly the listed tool tree')
        for i in infos:
            rel = i.filename[len(root):]
            f = want[rel]
            kind = (i.external_attr >> 16) & 0o170000
            if kind not in (0, stat.S_IFREG):
                refuse(f'archive member is not a regular file: {rel}')
            if i.file_size != f['size'] or i.file_size > spec['per_file_cap']:
                refuse(f'archive member size differs: {rel}')
            target = os.path.join(dest, *rel.split('/'))
            os.makedirs(os.path.dirname(target), mode=0o700, exist_ok=True)
            h, n = hashlib.sha256(), 0
            fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o644)
            with os.fdopen(fd, 'wb') as o, z.open(i) as src:
                for c in iter(lambda: src.read(1 << 20), b''):
                    n += len(c)
                    if n > f['size']:                       # defence in depth: the zip reader already stops at the header size
                        refuse(f'archive member larger than its size: {rel}')
                    h.update(c)
                    o.write(c)
                o.flush()
                os.fsync(o.fileno())
            if n != f['size'] or h.hexdigest() != f['sha256']:
                refuse(f'extracted file differs from the list: {rel}')
            done.add(rel)
    if done != set(want):
        refuse('not every listed file was extracted')
    return sorted(done)


def runtime_minimums(dest, spec):
    """The shared frameworks and minimum versions the tree's runtimeconfig.json names, in either shape ('frameworks': [...] or 'framework': {...})."""
    j = json.loads(open(os.path.join(dest, spec['runtimeconfig']), 'rb').read())
    ro = j.get('runtimeOptions') or {}
    items = list(ro.get('frameworks') or []) + ([ro['framework']] if isinstance(ro.get('framework'), dict) else [])
    fw = {x.get('name'): x.get('version') for x in items}
    if len(items) != len(fw) or sorted(fw) != sorted(spec['frameworks']) or not all(isinstance(v, str) and VERSION.match(v) for v in fw.values()):
        refuse(f'the tool tree needs unexpected frameworks: {sorted(fw)}')
    return fw


def merge_minimums(per_tree):
    """The highest minimum per framework across the trees: what the base image has to satisfy."""
    out = {}
    for fw in per_tree.values():
        for name, v in fw.items():
            if name not in out or version_tuple(v) > version_tuple(out[name]):
                out[name] = v
    return out


def build_argv(ctx, stagings, mins, pins, trees):
    args = []
    for name, rel in PIN_ARGS.items():
        args += ['--build-arg', f'{name}={pins[rel]}']
    for key, spec in trees.items():
        args += ['--build-arg', f"{spec['list_arg']}={spec['list_sha']}"]
    args += ['--build-arg', f'MIN_NETCORE={mins["Microsoft.NETCore.App"]}', '--build-arg', f'MIN_ASPNET={mins["Microsoft.AspNetCore.App"]}']
    contexts = []
    for key in trees:
        contexts += ['--build-context', f'{key}={stagings[key]}']
    return (['podman', 'build', '--network=none', '--pull=never', '--no-cache', '--timestamp', '0'] + args + contexts +
            ['-f', ctx + '/Containerfile', '-t', TAG, ctx])


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
    return {'run': run, 'euid': os.geteuid(), 'owner_uid': 0, 'ctx': CTX, 'context': CONTEXT, 'trees': TREES,
            'home': '/mnt/storage/containers/learn-qualification/account', 'self_path': os.path.abspath(__file__), 'production': True}


def main(deps=None):
    d = deps or default_deps()
    if d['euid'] != 30034:
        sys.exit('refuse: run as fsgg-learn-qual')
    if d.get('production'):
        os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus', HOME=d['home'], PATH='/usr/bin', LC_ALL='C')
        os.chdir('/')
    run = d['run']
    trees = d['trees']
    R = {'schema': SCHEMA, 'scriptSha256': sha_file(d['self_path']), 'base': BASE, 'tag': TAG, 'utc': datetime.now(timezone.utc).isoformat(),
         'account': 'fsgg-learn-qual', 'errors': []}
    staging = None
    try:
        R['context'] = verify_context(d['ctx'], d['context'], d['owner_uid'], d.get('chain_start', '/'))
        lists = {key: load_tree(d['ctx'], spec, d['context'][spec['nupkg']]) for key, spec in trees.items()}
        rc, out, _ = run(['podman', 'image', 'inspect', '--format', '{{.Id}} {{json .RootFS.Layers}}', BASE], 60)
        if rc != 0 or not out.startswith(BASE):
            raise Refuse('base image not present')
        base_layers = json.loads(out.split(' ', 1)[1])
        state = d['home'] + '/.local/state/learn-build'
        os.makedirs(state, mode=0o700, exist_ok=True)
        staging = tempfile.mkdtemp(prefix='pod-app-r1-', dir=state)
        os.chmod(staging, 0o700)
        stagings, per_tree = {}, {}
        R['toolTrees'] = {}
        for key, spec in trees.items():
            stagings[key] = os.path.join(staging, key)
            os.mkdir(stagings[key], 0o700)
            R['toolTrees'][key] = {'files': len(extract_tree(os.path.join(d['ctx'], spec['nupkg']), lists[key], stagings[key], spec)), 'listSha256': spec['list_sha']}
            per_tree[key] = runtime_minimums(stagings[key], spec)
        mins = merge_minimums(per_tree)
        R['runtimeMinimums'] = mins
        argv = build_argv(d['ctx'], stagings, mins, R['context'], trees)
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

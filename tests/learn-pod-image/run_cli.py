#!/usr/bin/python3 -I
# Runs the pinned producer CLI the way the pod image carries it: the FS.GG.Coord.Cli 0.113.0 tool tree, copied member by member from the pinned archive into an
# otherwise empty directory (no NuGet store, no apphost, no package cache beside it), and executed with `dotnet` and nothing else. Refs #4647.
#
# Why this exists: the first version of the image pinned `fsgg-coord-engine` (5e517e8d...), a 78 KB apphost that needs `.store/.../fsgg-coord-engine.dll` beside it, and
# described it as a self-contained ELF. It could not have run in the image. This test fails if the tree stops being runnable from a copy-only layout.
#
# It needs the public archive (release coherent-set/v0.113.0 of FS-GG/.github, asset FS.GG.Coord.Cli.0.113.0.nupkg, sha256 100f0879...) and a `dotnet` with
# Microsoft.NETCore.App 10.0.0 or later. Set LEARN_CLI_NUPKG to its path (the workflow downloads it). Without the archive it exits 2 and says so: it is a separate
# command from tests/learn-pod-image/run.py, which needs neither. What it does NOT show: that the image's own runtime (10.0.12 in the base) runs it, which only the
# first build and the pod harness can.
import hashlib, importlib.util, json, os, re, shutil, stat, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
IMG = os.path.join(ROOT, 'deployment', 'learn-finite-profile', 'pod-image')
PRIVATE = os.path.expanduser('~/.local/share/fs-gg-private/learn-4647-bundle-5d525ad2-20261030/coord-cli-0.113.0/FS.GG.Coord.Cli.0.113.0.nupkg')
ARCHIVE = os.environ.get('LEARN_CLI_NUPKG') or (PRIVATE if os.path.exists(PRIVATE) else '')
n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


if not ARCHIVE or not os.path.exists(ARCHIVE):
    print('learn-pod-image run_cli: the pinned FS.GG.Coord.Cli.0.113.0.nupkg is not available (set LEARN_CLI_NUPKG); nothing was run')
    sys.exit(2)
dotnet = shutil.which('dotnet')
if not dotnet:
    print('learn-pod-image run_cli: no dotnet on PATH; nothing was run')
    sys.exit(2)

spec = importlib.util.spec_from_file_location('builder', os.path.join(IMG, 'learn-build-pod-app-r1.py'))
B = importlib.util.module_from_spec(spec)
spec.loader.exec_module(B)
cli = B.TREES['cli']
tree = json.load(open(os.path.join(IMG, cli['json'])))

check(hashlib.sha256(open(ARCHIVE, 'rb').read()).hexdigest() == B.CONTEXT[cli['nupkg']], 'the archive is the pinned FS.GG.Coord.Cli.0.113.0.nupkg (100f0879...)')
check(not any(f['path'] == 'fsgg-coord-engine' for f in tree['files']), 'the tree holds no apphost: the CLI is started as `dotnet fsgg-coord-engine.dll`')
runtimes = subprocess.run([dotnet, '--list-runtimes'], capture_output=True, text=True).stdout
core = [tuple(int(x) for x in m.group(1).split('.')) for m in re.finditer(r'^Microsoft\.NETCore\.App (\d+\.\d+\.\d+) ', runtimes, re.M)]
check(any(v >= (10, 0, 0) for v in core), 'this machine has Microsoft.NETCore.App 10.0.0 or later (the tree\'s runtimeconfig minimum, rollForward Major)')

with tempfile.TemporaryDirectory() as tmp:
    dest = os.path.join(tmp, 'opt', 'fsgg', 'producer')
    os.makedirs(dest)
    got = B.extract_tree(ARCHIVE, tree['files'], dest, cli)
    check(len(got) == 82 and got == [f['path'] for f in tree['files']], 'the tree extracts member by member to exactly the 82 listed files')
    present = sorted(os.path.relpath(os.path.join(r, f), dest) for r, _, fs in os.walk(dest) for f in fs)
    check(present == sorted(f['path'] for f in tree['files']), 'copy-only layout: nothing is in the directory but the listed files (no .store, no apphost, no package cache)')
    check(not any(p.startswith('.') or 'nupkg' in p for p in present), 'no NuGet metadata or package file is beside the dll')
    for r, ds, fs in os.walk(dest):                                                   # as in the image: directories 0555, files 0444
        for d in ds:
            os.chmod(os.path.join(r, d), 0o555)
        for f in fs:
            os.chmod(os.path.join(r, f), 0o444)
    os.chmod(dest, 0o555)
    work = os.path.join(tmp, 'work')
    os.makedirs(os.path.join(work, 'home'))
    env = {'PATH': os.path.dirname(dotnet) + ':/usr/bin:/bin', 'HOME': os.path.join(work, 'home'), 'TMPDIR': work, 'DOTNET_CLI_TELEMETRY_OPTOUT': '1', 'DOTNET_NOLOGO': '1', 'DOTNET_gcServer': '0',
           'DOTNET_CLI_HOME': os.path.join(work, 'home')}
    dll = os.path.join(dest, 'fsgg-coord-engine.dll')
    p = subprocess.run([dotnet, dll, '--version'], capture_output=True, text=True, env=env, cwd=work, timeout=120)
    check(p.returncode == 0 and p.stdout.strip() == '0.113.0.0', f'`dotnet fsgg-coord-engine.dll --version` prints 0.113.0.0 from the copy-only, read-only tree (rc {p.returncode}: {p.stderr[-200:]!r})')
    p = subprocess.run([dotnet, dll, 'telemetry', 'workspace', 'status'], capture_output=True, text=True, env=env, cwd=work, timeout=120)
    j = json.loads(p.stdout)
    check(j == {'schema': 'fsgg.telemetry.workspace-status/1', 'status': 'unconfigured'}, f'`telemetry workspace status`, the verb `feed` replays with, runs and reports unconfigured with no workspace (got {p.stdout.strip()!r})')
    check(sorted(os.listdir(work)) == ['home'] or all(not x.endswith('.dll') for x in os.listdir(work)), 'running it wrote no assembly or package next to the work directory')
    # a layout without the dependency manifest or the dll does not run: the check above is not vacuous
    broken = os.path.join(tmp, 'broken')
    shutil.copytree(dest, broken)
    for r, ds, fs in os.walk(broken):
        for d in ds:
            os.chmod(os.path.join(r, d), 0o755)
    os.chmod(broken, 0o755)
    os.chmod(os.path.join(broken, 'FS.GG.Coord.Core.dll'), 0o644)
    os.unlink(os.path.join(broken, 'FS.GG.Coord.Core.dll'))
    p = subprocess.run([dotnet, os.path.join(broken, 'fsgg-coord-engine.dll'), '--version'], capture_output=True, text=True, env=env, cwd=work, timeout=120)
    check(p.returncode != 0, 'control: the same tree without one of its dependencies does not run, so the success above depends on the whole tree being copied')
    for r, ds, fs in os.walk(dest):                                                   # let the temporary directory be removed
        for d in ds:
            os.chmod(os.path.join(r, d), 0o755)
    os.chmod(dest, 0o755)

print(f'learn-pod-image run_cli: {n} assertions passed')

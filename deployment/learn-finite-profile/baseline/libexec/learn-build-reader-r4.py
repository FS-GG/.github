#!/usr/bin/python3 -I
"""LEARN #4745 reader-image build r4 (adds the socket probe; 4516#6347898882 option (a); r3 build 6ee16257 admitted 4516#6346749790). Runs as the LEARN account only:
    sudo -n -u fsgg-learn-qual /usr/local/libexec/fsgg-learn/learn-build-reader.py        (or runuser -u fsgg-learn-qual)
Build context = the root-owned copy installed by C1 under /usr/local/libexec/fsgg-learn/reader-image/ (each file re-hashed
against the constants below before the build). Build:
    podman build --network=none --pull=never --no-cache --timestamp 0 --build-arg REAL_DOTNET=/usr/share/dotnet/dotnet
                 -f Containerfile -t localhost/learn-reader:r4 <context>
then asserts on the built image: Config.User == "1000:1000"; Entrypoint == [catatonit, --, /usr/local/libexec/learn/entrypoint];
Cmd empty; parent/base layers include every layer of 20a6df20…; and records its Id (the digest to admit). Writes only the
account's own podman storage. Result to stdout (RESULT-BEGIN/RESULT-END). Exit: 0 ok; 5 refused/failed.
"""
import hashlib, json, os, subprocess, sys
from datetime import datetime, timezone

if os.geteuid() != 30034: sys.exit('refuse: run as fsgg-learn-qual')
os.environ.update(XDG_RUNTIME_DIR='/run/user/30034', DBUS_SESSION_BUS_ADDRESS='unix:path=/run/user/30034/bus',
                  HOME='/mnt/storage/containers/learn-qualification/account', PATH='/usr/bin', LC_ALL='C')
os.chdir('/')
SELF_SHA = hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest()
CTX = '/usr/local/libexec/fsgg-learn/reader-image-r4'
BASE = '20a6df20169ce6721ed91fddd0d24ae7fe8de655008b422df0c3299821b7d4b8'
TAG = 'localhost/learn-reader:r4'
CONTEXT = {'Containerfile': '563d8b2697aeaa5d88ec808ae1e686904abbaaf2ad54ca60caa0fd5b5a7e81e2', 'learn-entrypoint.sh': '915197d1f53b95d2569a3145f5c36a091efaa3628eef82183f569f658c37be20', 'learn-slot.sh': 'b0f25037e1d35ff7c7223906b2a7c2e34328f35098a3d830a3689675a396a034', 'learn-dotnet-shim.sh': '5c9f981744d195cf1c4ea48b6a448b901eda7db53e8458baf6ddc5dce8dc2fcf', 'learn-probe': '66f8254ec7abf6469a3dd354f3846dbc557fd19762ca8189c484bc03557fb303'}
R = {'schema': 'home-learn-build-reader/2', 'request': '4516#6347898882', 'scriptSha256': SELF_SHA, 'base': BASE, 'tag': TAG,
     'utc': datetime.now(timezone.utc).isoformat(), 'account': 'fsgg-learn-qual', 'machine': 'Home (OS hostname Main)', 'errors': []}


def run(argv, timeout=900):
    try:
        p = subprocess.run(argv, capture_output=True, text=True, timeout=timeout, cwd='/')
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired: return None, '', 'timeout'


def finish():
    code = 5 if R['errors'] else 0; R['exitCode'] = code; b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN'); print(b); print('RESULT-END'); print(json.dumps({'sha256': hashlib.sha256(b.encode()).hexdigest(), 'exit': code}))
    sys.exit(code)


try:
    st = os.lstat(CTX)
    if st.st_uid != 0 or (st.st_mode & 0o022) or os.path.islink(CTX): R['errors'].append('context dir not root-owned/read-only'); finish()
    for name, want in CONTEXT.items():
        p = f'{CTX}/{name}'; s = os.lstat(p)
        if os.path.islink(p) or s.st_uid != 0 or (s.st_mode & 0o022): R['errors'].append(f'context file not root-owned/read-only: {name}'); finish()
        if hashlib.sha256(open(p, 'rb').read()).hexdigest() != want: R['errors'].append(f'context hash mismatch: {name}'); finish()
    if sorted(os.listdir(CTX)) != sorted(CONTEXT): R['errors'].append('unexpected files in the build context'); finish()
    rc, out, err = run(['podman', 'image', 'inspect', '--format', '{{.Id}} {{json .RootFS.Layers}}', BASE], 60)
    if rc != 0 or not out.startswith(BASE): R['errors'].append('base image not present'); finish()
    base_layers = json.loads(out.split(' ', 1)[1])
    rc, out, err = run(['podman', 'build', '--network=none', '--pull=never', '--no-cache', '--timestamp', '0',
                        '--build-arg', 'REAL_DOTNET=/usr/share/dotnet/dotnet', '-f', CTX + '/Containerfile', '-t', TAG, CTX])
    R['build'] = {'rc': rc, 'stdoutTail': out[-3000:], 'stderrTail': err[-2000:]}
    if rc != 0: R['errors'].append('build failed'); finish()
    rc, out, err = run(['podman', 'image', 'inspect', TAG], 60)
    j = json.loads(out)[0]; cfg = j.get('Config') or {}
    layers = (j.get('RootFS') or {}).get('Layers') or []
    R['image'] = {'id': j.get('Id'), 'digest': j.get('Digest'), 'configUser': cfg.get('User'), 'entrypoint': cfg.get('Entrypoint'),
                  'cmd': cfg.get('Cmd'), 'envNames': sorted(e.split('=', 1)[0] for e in cfg.get('Env') or []),
                  'layers': len(layers), 'created': j.get('Created')}
    g = {'user1000': cfg.get('User') == '1000:1000',
         'entrypointCatatonit': cfg.get('Entrypoint') == ['/usr/bin/catatonit', '--', '/usr/local/libexec/learn/entrypoint'],
         'cmdEmpty': not cfg.get('Cmd'), 'baseLayersPrefix': layers[:len(base_layers)] == base_layers,
         'msbuildNodeReuseOff': 'MSBUILDDISABLENODEREUSE=1' in (cfg.get('Env') or [])}   # value checked, not only the name
    R['gates'] = g
    if not all(g.values()): R['errors'].append('image gate failed: ' + ','.join(k for k, v in g.items() if not v))
except Exception as e:
    R['errors'].append(f'exception: {type(e).__name__}: {str(e)[:200]}')
finish()

#!/usr/bin/python3 -I
"""LEARN #4745 retirement acknowledgement writer r1 (governing spec rev 11 r2 §7; C2 r5 gate; decision 4516#6352952638).
Root only, from a root-owned exec copy, and ONLY through an fdev admission that cites the op id and the record's sha256:
    /usr/bin/python3 -I <copy>/learn-retirement-ack.py --ack --op-id <id> --record-sha256 <64 hex>
    /usr/bin/python3 -I <copy>/learn-retirement-ack.py --remove --op-id <id>
--ack   creates ACK_DIR (root:root 0755) if absent, then ACK_DIR/<id>.retirement-ack (root:root 0644, content "<sha256>\\n",
        O_CREAT|O_EXCL|O_NOFOLLOW; refuses if it exists). It does NOT read the record: the admitted sha256 is written as given,
        and C2 r5 compares it with the record's bytes at every launch (a wrong sha256 simply leaves the record unacknowledged).
--remove unlinks ACK_DIR/<id>.retirement-ack if it is a regular root-owned file (cleanup after acceptance tests).
Every path component of ACK_DIR must be root-owned, not a symlink, not group/other-writable. Output: RESULT JSON on stdout.
Exit 0 done; 5 refused.
"""
import hashlib, json, os, re, stat, sys

ACK_DIR = '/etc/fsgg/learn/retirement-acks'
R = {'schema': 'home-learn-retirement-ack/1', 'scriptSha256': hashlib.sha256(open(os.path.abspath(__file__), 'rb').read()).hexdigest(),
     'ackDir': ACK_DIR, 'errors': []}


def finish(code):
    R['exitCode'] = code; b = json.dumps(R, indent=1, sort_keys=True)
    print('RESULT-BEGIN'); print(b); print('RESULT-END'); print(json.dumps({'sha256': hashlib.sha256(b.encode()).hexdigest(), 'exit': code}))
    sys.exit(code)


def refuse(why): R['errors'].append(why); finish(5)


def root_chain_ok(path):
    for i in range(1, len(path.split('/')) + 1):
        p = '/'.join(path.split('/')[:i]) or '/'; s = os.lstat(p)
        if s.st_uid != 0 or stat.S_ISLNK(s.st_mode) or (stat.S_IMODE(s.st_mode) & 0o022): return False, p
    return True, None


if os.geteuid() != 0: refuse('run as root')
os.umask(0o022)
a = sys.argv[1:]
mode = a[0] if a else None
opts = {a[i]: a[i + 1] for i in range(1, len(a) - 1, 2)}
if mode not in ('--ack', '--remove') or len(a) != (5 if mode == '--ack' else 3) or '--op-id' not in opts:
    refuse('usage: --ack --op-id <id> --record-sha256 <hex> | --remove --op-id <id>')
opid = opts['--op-id']
if not re.fullmatch(r'[a-z0-9][a-z0-9-]{2,62}', opid): refuse('bad op id')
R.update(mode=mode, opId=opid)
ok, bad = root_chain_ok(os.path.dirname(ACK_DIR))
if not ok: refuse(f'parent of ACK_DIR not root-safe at {bad}')
path = f'{ACK_DIR}/{opid}.retirement-ack'; R['path'] = path
if mode == '--ack':
    h = opts.get('--record-sha256', '')
    if not re.fullmatch(r'[0-9a-f]{64}', h): refuse('record sha256 must be 64 lowercase hex')
    if not os.path.lexists(ACK_DIR): os.mkdir(ACK_DIR, 0o755); R['ackDirCreated'] = True
    ok, bad = root_chain_ok(ACK_DIR)
    if not ok: refuse(f'ACK_DIR not root-safe at {bad}')
    try: fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o644)
    except FileExistsError: refuse('acknowledgement already exists')
    with os.fdopen(fd, 'w') as f: f.write(h + '\n'); f.flush(); os.fsync(f.fileno())
    st = os.lstat(path); R.update(recordSha256=h, written={'mode': oct(stat.S_IMODE(st.st_mode)), 'uid': st.st_uid, 'gid': st.st_gid})
else:
    if not os.path.lexists(path): refuse('no such acknowledgement')
    st = os.lstat(path)
    if not stat.S_ISREG(st.st_mode) or st.st_uid != 0: refuse('acknowledgement is not a regular root-owned file')
    os.unlink(path); R['removed'] = True
finish(0)

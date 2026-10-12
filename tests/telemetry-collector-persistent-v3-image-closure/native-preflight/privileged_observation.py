import json, os, select, stat, time

# Literal source for a separately admitted read-only provider observation.
# No fixture births, cgroup writes, signals, namespace or filesystem changes.
LIMIT = 1048576
used = 0
owned = []
started = time.clock_gettime_ns(time.CLOCK_BOOTTIME)
deadline = started + 15000000000
report = {'schema': 'fsgg.learn.native-capability-observation/1',
          'fixtureExecuted': False, 'fixtureCustodyQualified': False,
          'hardWrapperResourceQuotasProved': False,
          'ancestryCompleteness': 'unknown-siblings-and-detached-monitors'}

def require(value, reason):
    if not value:
        raise RuntimeError(reason)

def bounded_read(path):
    global used
    require(time.clock_gettime_ns(time.CLOCK_BOOTTIME) < deadline, 'original-deadline')
    fd = os.open(path, os.O_RDONLY | os.O_CLOEXEC | os.O_NOFOLLOW)
    try:
        chunks = []
        count = 0
        while True:
            require(time.clock_gettime_ns(time.CLOCK_BOOTTIME) < deadline, 'original-deadline')
            block = os.read(fd, min(4096, 65537 - count))
            if not block:
                break
            count += len(block)
            used += len(block)
            require(count <= 65536 and used <= LIMIT, 'read-bound')
            chunks.append(block)
        return b''.join(chunks).decode('utf-8', 'strict')
    finally:
        os.close(fd)

def live(fd):
    poll = select.poll()
    poll.register(fd, select.POLLIN)
    return not poll.poll(0)

def process(pid):
    fd = os.pidfd_open(pid, 0)
    owned.append(fd)
    require(live(fd), 'process-already-exited')
    first = bounded_read('/proc/%d/stat' % pid)
    split = first.rfind(') ')
    require(split > 0, 'proc-stat-shape')
    fields = first[split + 2:].split()
    require(len(fields) >= 20, 'proc-stat-fields')
    parent, birth = int(fields[1]), fields[19]
    status = {}
    for line in bounded_read('/proc/%d/status' % pid).splitlines():
        if ':' in line:
            key, value = line.split(':', 1)
            if key in ('Uid', 'Gid', 'CapEff', 'NoNewPrivs', 'Threads'):
                status[key] = value.strip()
    cgroup = bounded_read('/proc/%d/cgroup' % pid)
    info = bounded_read('/proc/self/fdinfo/%d' % fd)
    require(('Pid:\t%d\n' % pid) in info, 'pidfd-number-join')
    second = bounded_read('/proc/%d/stat' % pid)
    second_split = second.rfind(') ')
    require(second_split > 0 and second[second_split + 2:].split()[19] == birth
            and second[second_split + 2:].split()[1] == str(parent)
            and live(fd), 'process-observation-race')
    return {'pid': pid, 'parent': parent, 'startTimeTicks': birth,
            'status': status, 'cgroup': cgroup}

try:
    require(os.geteuid() == 0, 'actual-root-required')
    report['uid'] = os.getuid()
    report['euid'] = os.geteuid()
    report['utcNs'] = time.time_ns()
    report['bootNs'] = started
    report['uidMap'] = bounded_read('/proc/self/uid_map')
    report['gidMap'] = bounded_read('/proc/self/gid_map')
    report['namespaces'] = {}
    for name in ('user', 'mnt', 'pid', 'cgroup'):
        info = os.stat('/proc/self/ns/' + name)
        report['namespaces'][name] = {'device': info.st_dev, 'inode': info.st_ino}
    chain = []
    pid = os.getpid()
    for _ in range(16):
        require(pid > 0 and all(row['pid'] != pid for row in chain), 'ancestry-cycle')
        row = process(pid)
        chain.append(row)
        if row['parent'] == 0:
            break
        pid = row['parent']
    else:
        raise RuntimeError('ancestry-bound')
    report['ancestry'] = chain
    mount_lines = bounded_read('/proc/self/mountinfo').splitlines()
    mounts = [line.split() for line in mount_lines if ' - cgroup2 ' in line]
    require(len(mounts) == 1, 'ambiguous-cgroup-v2-mount')
    mount = mounts[0]
    require(len(mount) >= 10 and mount[3] == '/' and mount[4] == '/sys/fs/cgroup',
            'cgroup-mount-root-scope')
    entries = chain[0]['cgroup'].splitlines()
    require(len(entries) == 1 and entries[0].startswith('0::/'), 'self-cgroup-shape')
    relative = entries[0][3:]
    require(all(part not in ('.', '..') for part in relative.split('/'))
            and '\\' not in relative and '\x00' not in relative, 'cgroup-path')
    path = '/sys/fs/cgroup' + (relative if relative != '/' else '')
    rows = []
    for _ in range(16):
        fd = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_CLOEXEC | os.O_NOFOLLOW)
        owned.append(fd)
        info = os.fstat(fd)
        require(stat.S_ISDIR(info.st_mode), 'cgroup-descriptor-type')
        fd_info = bounded_read('/proc/self/fdinfo/%d' % fd)
        mount_ids = [line.split(':', 1)[1].strip() for line in fd_info.splitlines() if line.startswith('mnt_id:')]
        require(len(mount_ids) == 1 and mount_ids[0] == mount[0], 'cgroup-descriptor-mount-join')
        row = {'path': path, 'device': info.st_dev, 'inode': info.st_ino,
               'owner': info.st_uid, 'mode': stat.S_IMODE(info.st_mode),
               'mountId': mount_ids[0],
               'accessWriteHintOnly': os.access(path, os.W_OK, effective_ids=True),
               'files': {}}
        for name in ('cgroup.type', 'cgroup.controllers', 'cgroup.subtree_control',
                     'cgroup.events', 'cgroup.procs', 'pids.max', 'memory.max', 'cpu.max'):
            try:
                row['files'][name] = bounded_read('/proc/self/fd/%d/%s' % (fd, name))
            except FileNotFoundError:
                row['files'][name] = None
        after = os.stat(path, follow_symlinks=False)
        require((info.st_dev, info.st_ino) == (after.st_dev, after.st_ino), 'cgroup-path-race')
        rows.append(row)
        if path == '/sys/fs/cgroup':
            break
        path = path.rsplit('/', 1)[0]
    else:
        raise RuntimeError('cgroup-ancestor-bound')
    report['cgroupMount'] = {'root': mount[3], 'mountpoint': mount[4],
                             'mountOptions': mount[5], 'record': mount}
    report['cgroupAncestors'] = rows
    require(all(live(fd) for fd in owned[:len(chain)]), 'late-ancestry-race')
    report['outcome'] = 'OBSERVED_READ_ONLY'
except Exception as error:
    report['outcome'] = 'BLOCKED_OR_UNKNOWN'
    report['reason'] = str(error)[:512]
finally:
    for fd in reversed(owned):
        os.close(fd)
    report['readBytes'] = used
    report['completedBootNs'] = time.clock_gettime_ns(time.CLOCK_BOOTTIME)
    if report['completedBootNs'] >= deadline:
        report['outcome'] = 'BLOCKED_OR_UNKNOWN'
        report['reason'] = 'original-deadline'
    encoded = json.dumps(report, sort_keys=True, separators=(',', ':'))
    if len(encoded.encode('utf-8')) > 65536:
        report['outcome'] = 'BLOCKED_OR_UNKNOWN'
        encoded = json.dumps({'schema': report['schema'], 'outcome': 'BLOCKED_OR_UNKNOWN',
                              'reason': 'output-bound', 'fixtureExecuted': False})
    print(encoded)
raise SystemExit(0 if report['outcome'] == 'OBSERVED_READ_ONLY' else 42)

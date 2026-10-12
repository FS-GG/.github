import json

# Bounded evidence parser input for the existing maintained owner runner stage.
# No process launch, capture, signals, wait, receipt or authority machinery.
def validate_observation(raw, original_boot_ns, original_boot_deadline_ns):
    if not isinstance(raw, bytes) or len(raw) > 65536:
        raise RuntimeError('observation-output-bound')
    def unique(pairs):
        value = {}
        for key, entry in pairs:
            if key in value:
                raise RuntimeError('duplicate-output-key')
            value[key] = entry
        return value
    observed = json.loads(raw.decode('utf-8', 'strict'), object_pairs_hook=unique)
    required = {'schema', 'fixtureExecuted', 'fixtureCustodyQualified',
        'hardWrapperResourceQuotasProved', 'ancestryCompleteness', 'uid', 'euid',
        'utcNs', 'bootNs', 'uidMap', 'gidMap', 'namespaces', 'ancestry',
        'cgroupMount', 'cgroupAncestors', 'outcome', 'readBytes', 'completedBootNs'}
    if set(observed) != required:
        raise RuntimeError('output-fields')
    if observed['schema'] != 'fsgg.learn.native-capability-observation/1' or observed['outcome'] != 'OBSERVED_READ_ONLY':
        raise RuntimeError('output-schema-or-outcome')
    if type(observed['euid']) is not int or observed['euid'] != 0 or any(observed[name] is not False for name in
        ('fixtureExecuted', 'fixtureCustodyQualified', 'hardWrapperResourceQuotasProved')):
        raise RuntimeError('output-root-or-overclaim')
    if type(observed['readBytes']) is not int or not 0 <= observed['readBytes'] <= 1048576:
        raise RuntimeError('output-read-bound')
    if not original_boot_ns <= observed['bootNs'] <= observed['completedBootNs'] <= original_boot_deadline_ns:
        raise RuntimeError('output-original-parent-interval')
    if observed['ancestryCompleteness'] != 'unknown-siblings-and-detached-monitors':
        raise RuntimeError('output-lineage-overclaim')
    for key in ('uidMap', 'gidMap'):
        if not isinstance(observed[key], str) or len(observed[key]) > 65536:
            raise RuntimeError('output-map-size')
        for line in observed[key].splitlines():
            fields = line.split()
            if len(fields) != 3 or any(not word.isdigit() for word in fields):
                raise RuntimeError('output-map-shape')
    if set(observed['namespaces']) != {'user', 'mnt', 'pid', 'cgroup'}:
        raise RuntimeError('output-namespace-fields')
    for value in observed['namespaces'].values():
        if set(value) != {'device', 'inode'} or any(type(n) is not int or n < 0 for n in value.values()):
            raise RuntimeError('output-namespace-identity')
    ancestry = observed['ancestry']
    if not isinstance(ancestry, list) or not 1 <= len(ancestry) <= 16:
        raise RuntimeError('output-ancestry-bound')
    for index, row in enumerate(ancestry):
        if set(row) != {'pid', 'parent', 'startTimeTicks', 'status', 'cgroup'} or type(row['pid']) is not int or row['pid'] <= 0 or type(row['parent']) is not int or row['parent'] < 0:
            raise RuntimeError('output-ancestry-fields')
        if index and ancestry[index-1]['parent'] != row['pid']:
            raise RuntimeError('output-ancestry-join')
    if ancestry[-1]['parent'] != 0 or len({row['pid'] for row in ancestry}) != len(ancestry):
        raise RuntimeError('output-ancestry-terminal')
    mount = observed['cgroupMount']
    if set(mount) != {'root', 'mountpoint', 'mountOptions', 'record'} or mount['root'] != '/' or mount['mountpoint'] != '/sys/fs/cgroup' or not isinstance(mount['record'], list) or len(mount['record']) < 10 or not isinstance(observed['cgroupAncestors'], list) or not 1 <= len(observed['cgroupAncestors']) <= 16:
        raise RuntimeError('output-cgroup-scope')
    if any(set(row) != {'path', 'device', 'inode', 'owner', 'mode', 'mountId', 'accessWriteHintOnly', 'files'} or row['mountId'] != mount['record'][0] for row in observed['cgroupAncestors']):
        raise RuntimeError('output-cgroup-mount-join')
    return observed

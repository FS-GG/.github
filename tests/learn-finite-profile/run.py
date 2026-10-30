#!/usr/bin/env python3
"""Gates for deployment/learn-finite-profile (Refs FS-GG/.github#4647).

Drives verify_baseline.py through its real entry points against the shipped baseline and against synthetic
trees that each break one rule. stdlib only, offline, no credential. Prints its own assertion count."""
import copy, hashlib, importlib.util, json, os, shutil, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
PROFILE = os.path.join(os.path.dirname(os.path.dirname(HERE)), 'deployment', 'learn-finite-profile')
spec = importlib.util.spec_from_file_location('verify_baseline', os.path.join(PROFILE, 'verify_baseline.py'))
vb = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vb)

n = 0


def check(cond, what):
    global n
    n += 1
    if not cond:
        print(f'FAIL: {what}')
        sys.exit(1)


def has(errors, needle):
    return any(needle in e for e in errors)


def clone(tmp):
    dst = os.path.join(tmp, 'profile')
    shutil.copytree(PROFILE, dst, ignore=shutil.ignore_patterns('__pycache__'))
    return dst


def write_manifest(root, m):
    with open(os.path.join(root, 'baseline', 'BASELINE.json'), 'w') as f:
        json.dump(m, f, indent=1, sort_keys=True)


def manifest(root):
    return json.load(open(os.path.join(root, 'baseline', 'BASELINE.json')))


# 1. The shipped baseline verifies, and its counts are what the manifest says.
errors, counts = vb.verify_repo(PROFILE)
check(errors == [], f'shipped baseline verifies: {errors}')
m = manifest(PROFILE)
check(counts['journalFiles'] == len(m['files']) == 20, 'manifest lists the 20 files of the install journal')
check(counts['imported'] == 10 and counts['notImported'] == 10, '10 imported, 10 not imported')
check(counts['hashVerified'] == counts['imported'], 'every imported file is hash-verified')
check(counts['scanned'] == counts['imported'], 'every imported file is scanned')
check(counts['redactedFiles'] == 0, 'no file is redacted')
check(counts['allowlistedScanMatches'] == 1, 'exactly one allowlisted scan match')
check(all(e.get('reason') for e in m['files'].values() if not e['imported']), 'every non-imported file has a reason')

# 2. Mutations of the tree are refused.
with tempfile.TemporaryDirectory() as tmp:
    root = clone(tmp)
    rp = os.path.join(root, 'baseline', 'libexec', 'learn-operator-r5.py')
    data = open(rp, 'rb').read()
    open(rp, 'wb').write(data[:-1] + (b'X' if data[-1:] != b'X' else b'Y'))
    check(has(vb.verify_repo(root)[0], 'learn-operator-r5.py: sha256 mismatch'), 'a changed byte is refused')
    open(rp, 'wb').write(data)
    check(vb.verify_repo(root)[0] == [], 'restored file verifies again')
    os.unlink(rp)
    check(has(vb.verify_repo(root)[0], 'missing or not a regular file'), 'a missing imported file is refused')
    open(rp, 'wb').write(data)
    open(os.path.join(root, 'baseline', 'libexec', 'extra.py'), 'w').write('x = 1\n')
    check(has(vb.verify_repo(root)[0], 'extra.py: file under baseline/ is not in the manifest'), 'an unlisted file is refused')
    os.unlink(os.path.join(root, 'baseline', 'libexec', 'extra.py'))
    os.unlink(rp)
    os.symlink('learn-finalizer-r4.py', rp)
    check(has(vb.verify_repo(root)[0], 'missing or not a regular file'), 'a symlink in place of a file is refused')
    os.unlink(rp)
    open(rp, 'wb').write(data)

    # manifest shape rules
    bad = copy.deepcopy(m)
    ip = '/usr/local/libexec/fsgg-learn/learn-operator-r5.py'
    bad['files'][ip]['sha256'] = 'zz'
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'sha256 is not 64 lowercase hex'), 'a malformed sha256 is refused')
    bad = copy.deepcopy(m)
    bad['files']['/usr/local/libexec/fsgg-learn/reader-image-r4/learn-probe']['reason'] = ''
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'not imported without a reason'), 'a non-imported file without a reason is refused')
    bad = copy.deepcopy(m)
    bad['files'][ip]['repoSha256'] = '0' * 64
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'repoSha256 differs from sha256 without a recorded redaction'), 'an unrecorded difference is refused')
    bad = copy.deepcopy(m)
    bad['scan']['allowlist'] = []
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'scan finding email: user@30034.service'), 'removing the allowlist re-exposes the scan finding')
    write_manifest(root, m)

# 3. The scan finds each class and honours the allowlist.
samples = {
    'token': 'x = "ghp_' + 'a' * 36 + '"', 'private-key': '-----BEGIN RSA PRIVATE KEY-----',
    'secret-assignment': 'password = "x"', 'email': 'who@example.org', 'ipv4': 'host 192.168.1.9',
    'private-hostname': 'telemetry.main.internal', 'person': 'owner Eugen', 'home-path': '/home/someone/x',
    'ssh-key': 'ssh-ed25519 AAAA',
}
check(set(samples) == set(vb.SCAN), 'a sample exists for every scan class')
for cls, text in samples.items():
    check(any(f[0] == cls for f in vb.scan_text('f.txt', text, set())), f'scan finds {cls}')
check(vb.scan_text('f.txt', 'who@example.org', {('f.txt', 'who@example.org')}) == [], 'an allowlisted match is not reported')

# 4. Install-journal comparison against a synthetic installed tree.
def synthetic_install(tmp, mutate=None):
    inst = os.path.join(tmp, 'inst')
    journal = {'schema': m['journalSchema'], 'installScriptSha256': m['installScriptSha256'], 'files': {}}
    for ip, e in m['files'].items():
        real = os.path.join(inst, ip.lstrip('/'))
        os.makedirs(os.path.dirname(real), exist_ok=True)
        if e['imported']:
            shutil.copyfile(os.path.join(PROFILE, 'baseline', e['repoPath']), real)
        else:
            payload = ('synthetic ' + ip).encode()
            open(real, 'wb').write(payload)
            journal['files'][ip] = {'sha256': hashlib.sha256(payload).hexdigest(), 'mode': e['mode']}
            continue
        journal['files'][ip] = {'sha256': e['sha256'], 'mode': e['mode']}
    if mutate:
        mutate(journal, inst)
    jp = os.path.join(tmp, 'journal.json')
    json.dump(journal, open(jp, 'w'))
    return jp, inst


# Non-imported files are synthetic here, so the manifest used for this leg carries their synthetic hashes.
with tempfile.TemporaryDirectory() as tmp:
    root = clone(tmp)
    m2 = manifest(root)
    for ip, e in m2['files'].items():
        if not e['imported']:
            e['sha256'] = hashlib.sha256(('synthetic ' + ip).encode()).hexdigest()
    write_manifest(root, m2)
    m = m2

    def ok_install(t, mutate=None):
        jp, inst = synthetic_install(t, mutate)
        return vb.verify_install(root, jp, {'/': inst + '/'})

    with tempfile.TemporaryDirectory() as t:
        errors, c = ok_install(t)
        check(errors == [] and c['installedVerified'] == 20, f'matching install verifies 20 files: {errors}')
    with tempfile.TemporaryDirectory() as t:
        errors, _ = ok_install(t, lambda j, i: j['files'].pop(next(iter(j['files']))))
        check(has(errors, 'in the manifest but not in the journal'), 'a file missing from the journal is refused')
    with tempfile.TemporaryDirectory() as t:
        errors, _ = ok_install(t, lambda j, i: j['files'].update({'/etc/extra': {'sha256': '0' * 64, 'mode': '0o644'}}))
        check(has(errors, 'in the journal but not in the manifest'), 'a journal file missing from the manifest is refused')
    with tempfile.TemporaryDirectory() as t:
        errors, _ = ok_install(t, lambda j, i: j['files'][next(iter(j['files']))].update(mode='0o777'))
        check(has(errors, 'journal mode differs'), 'a mode difference is refused')
    with tempfile.TemporaryDirectory() as t:
        def alter(j, inst):
            p = os.path.join(inst, 'usr/local/libexec/fsgg-learn/learn-operator-r5.py')
            open(p, 'ab').write(b'#')
        errors, _ = ok_install(t, alter)
        check(has(errors, 'installed bytes differ from the journal sha256'), 'an altered installed file is refused')
    with tempfile.TemporaryDirectory() as t:
        errors, _ = ok_install(t, lambda j, i: os.unlink(os.path.join(i, 'etc/fsgg/learn/seccomp-8bd52f8d.json')))
        check(has(errors, 'installed file is absent'), 'an absent installed file is refused')
    with tempfile.TemporaryDirectory() as t:
        errors, _ = ok_install(t, lambda j, i: j.update(installScriptSha256='0' * 64))
        check(has(errors, 'install script sha256 differs'), 'a different install script is refused')

# 5. The recorded-redaction mechanism (none is used today): it must reconstruct and must refuse a mismatch.
with tempfile.TemporaryDirectory() as tmp:
    original = b'line one\nTOKEN=hunter2-secret-value\nline three\n'
    line = original.split(b'\n')[1]
    rec = [{'line': 2, 'originalLineSha256': hashlib.sha256(line).hexdigest(), 'replacement': 'TOKEN=<redacted>'}]
    out = vb.apply_redactions(original, rec)
    check(out == b'line one\nTOKEN=<redacted>\nline three\n', 'a recorded redaction is applied to the installed bytes')
    check(b'hunter2' not in out and b'hunter2' not in json.dumps(rec).encode(), 'neither the output nor the record carries the secret')
    try:
        vb.apply_redactions(original.replace(b'hunter2', b'other'), rec)
        check(False, 'a redaction over a different original line is refused')
    except ValueError:
        check(True, 'a redaction over a different original line is refused')
    try:
        vb.apply_redactions(original, [dict(rec[0], line=9)])
        check(False, 'a redaction past the end of the file is refused')
    except ValueError:
        check(True, 'a redaction past the end of the file is refused')
    bad = {'schema': vb.SCHEMA, 'journalSchema': 'x', 'installScriptSha256': '0' * 64, 'scan': {}, 'files': {
        '/p': {'mode': '0o644', 'sha256': '1' * 64, 'imported': True, 'repoPath': 'p', 'repoSha256': '2' * 64,
               'redactions': [{'line': 'x'}]}}}
    errs = []
    vb.check_manifest_shape(bad, errs)
    check(has(errs, 'malformed redaction record'), 'a malformed redaction record is refused')

print(f'learn-finite-profile: {n} assertions passed')

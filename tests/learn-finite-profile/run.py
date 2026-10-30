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
check(counts['redactedFiles'] == 0, 'no file is redacted')
check(counts['allowlistedScanMatches'] == 6, 'exactly six allowlisted scan matches, each with a reason')
check(all(a.get('why') for a in m['scan']['allowlist']), 'every allowlist entry carries its reason')
check(counts['sources'] == 2 and counts['importedSources'] == 2, 'both probe sources are imported')
check(counts['hashVerified'] == counts['imported'] + counts['importedSources'] == 12, '12 imported files are hash-verified')
check(counts['manualReads'] == 12 and counts['scanned'] == 12, 'every imported file is scanned and has a recorded manual read')
check(counts['custodyReferences'] == 3 and counts['scanClasses'] == 16, '3 custody references and 16 scan classes')
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
    errs = vb.verify_repo(root)[0]
    check(has(errs, 'scan finding email: user@30034.service') and has(errs, 'scan finding ipv6: 0::') and has(errs, 'scan finding person-context: Runs as eugen'), 'removing the allowlist re-exposes the findings')
    write_manifest(root, m)

# 3. The scan finds each class and honours the allowlist, the person names and the public URL prefixes.
samples = {
    'token': 'x = "ghp_' + 'a' * 36 + '"', 'bearer': 'Authorization: Bearer abcdefgh12345678', 'private-key': '-----BEGIN RSA PRIVATE KEY-----',
    'secret-assignment': 'password = "x"', 'email': 'who@example.org', 'ipv4': 'host 192.168.1.9',
    'ipv6': 'addr fe80::1ff:fe23:4567:890a', 'mac': 'link 00:1A:2b:3c:4D:5e', 'uuid': 'id 123e4567-e89b-12d3-a456-426614174000',
    'url': 'see https://example.org/a/b', 'hostname': 'connect telemetry.main.internal', 'home-path': '/home/someone/x',
    'ssh-key': 'ssh-ed25519 AAAA', 'mention': 'thanks @someone for this', 'person-context': 'Owner: Alexandra Example',
}
check(set(samples) == set(vb.SCAN), 'a sample exists for every scan class')
for cls, text in samples.items():
    check(any(f[0] == cls for f in vb.scan_text('f.txt', text, set())), f'scan finds {cls}')
check(any(f[0] == 'ipv6' for f in vb.scan_text('f.txt', 'at ::1 now', set())), 'scan finds the loopback ::1')
check(any(f[0] == 'ipv6' for f in vb.scan_text('f.txt', '2001:db8:0:0:0:0:0:1', set())), 'scan finds a full IPv6 address')
for benign in ('18:35:18', '2026-10-22T18:35:18.880263+00:00', 'std::string', 'a/b/c.py', 'version 10.0.12', 'sha ' + 'ab' * 32, 'user@30034'):
    check(not any(f[0] in ('ipv6', 'mac', 'uuid', 'hostname', 'ipv4') for f in vb.scan_text('f.txt', benign, set())), f'benign text is not flagged: {benign}')
check(vb.scan_text('f.txt', 'who@example.org', {('f.txt', 'who@example.org'), ('f.txt', 'example.org')}) == [], 'allowlisted matches are not reported')
check([f[0] for f in vb.scan_text('f.txt', 'who@example.org', {('f.txt', 'who@example.org')})] == ['hostname'], 'an allowlist entry covers only its own exact match')
check(any(f[0] == 'person' for f in vb.scan_text('f.txt', 'written for Zed', set(), persons=['zed'])), 'a listed person name is flagged')
check(vb.scan_text('f.txt', 'see https://github.com/FS-GG/.github/issues/1', set(), url_prefixes=['https://github.com/FS-GG/']) == [], 'a public org URL prefix is not flagged')
check(any(f[0] == 'url' for f in vb.scan_text('f.txt', 'see https://github.com/other/x', set(), url_prefixes=['https://github.com/FS-GG/'])), 'another URL is flagged')
check(vb.scan_text('f.txt', 'operator lock busy', set()) == [] and vb.scan_text('f.txt', 'the owner of the file', set()) == [], 'role words before common words are not flagged')

# 3b. Sources, sameBytesAs, builtFrom, the manual-read gate and custody.
with tempfile.TemporaryDirectory() as tmp:
    root = clone(tmp)
    m3 = manifest(root)
    rp = os.path.join(root, 'baseline', 'source', 'learn-probe.c')
    data = open(rp, 'rb').read()
    open(rp, 'ab').write(b'\n')
    check(has(vb.verify_repo(root)[0], 'source/learn-probe.c: sha256 mismatch'), 'a changed imported source is refused')
    open(rp, 'wb').write(data)
    bad = copy.deepcopy(m3)
    bad['scan']['manualRead'] = [r for r in bad['scan']['manualRead'] if r['file'] != 'source/learn-probe.c']
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'source/learn-probe.c: no recorded manual read'), 'an imported file without a recorded manual read is refused')
    bad = copy.deepcopy(m3)
    bad['scan']['personNames'].append('probe')
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'scan finding person'), 'an added person name is searched for')
    bad = copy.deepcopy(m3)
    key = '/usr/local/libexec/fsgg-learn/reader-image/learn-slot.sh'
    bad['files'][key]['sha256'] = '1' * 64
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'sameBytesAs'), 'a sameBytesAs claim with different bytes is refused')
    bad = copy.deepcopy(m3)
    bad['files']['/usr/local/libexec/fsgg-learn/reader-image-r4/learn-probe']['builtFrom'] = ['nope.c']
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'builtFrom nope.c is not a recorded source'), 'a builtFrom naming no source is refused')
    bad = copy.deepcopy(m3)
    bad['sources']['learn-probe.c']['reason'] = ''
    bad['sources']['learn-probe.c']['imported'] = False
    write_manifest(root, bad)
    errs = vb.verify_repo(root)[0]
    check(has(errs, 'source learn-probe.c: not imported without a reason') and has(errs, 'file under baseline/ is not in the manifest'), 'an un-imported source needs a reason and its file is then unlisted')
    bad = copy.deepcopy(m3)
    del bad['custody']
    write_manifest(root, bad)
    check(has(vb.verify_repo(root)[0], 'manifest lacks custody'), 'a manifest without custody is refused')
    write_manifest(root, m3)
    check(m3['custody']['pinsChecked'] == 24 and m3['custody']['pinsMismatched'] == 0, 'the custody pin check recorded 24 pins and 0 mismatches')
    check(m3['custody']['acceptedInstalledManifestEqualsInstallManifest'] is True, 'the accepted installed manifest equals the install manifest')
    check(len(m3['custody']['installScripts']) == 5 and sum('NOT in custody' in v for v in m3['custody']['installScripts'].values()) == 4,
          'five install scripts are accounted for, four without custody source')
    cust = os.path.join(tmp, 'custody')
    for name, e in {**m3['sources'], **m3['custody']['files']}.items():
        dst = os.path.join(cust, e.get('custodyName', name))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        open(dst, 'wb').write(open(os.path.join(root, 'baseline', e['repoPath']), 'rb').read() if e.get('imported') else b'')
    errs, c = vb.verify_custody(root, cust)
    check(c['custodyEntries'] == 5 and c['custodyVerified'] == 2 and len(errs) == 3, 'custody check: the 2 imported sources match; the 3 reference files differ in the synthetic tree')
    os.unlink(os.path.join(cust, 'learn-probe.c'))
    check(has(vb.verify_custody(root, cust)[0], 'learn-probe.c: absent from custody'), 'a file absent from custody is refused')

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

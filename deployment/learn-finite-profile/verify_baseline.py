#!/usr/bin/env python3
"""Verify the LEARN finite-profile baseline import (Refs FS-GG/.github#4647).

The baseline directory holds the admitted, installed bytes of the #4745 finite profile that had no maintained
repository (#4745 closure gap 1). BASELINE.json records, for every file in the install journal
(`home-learn-install-journal/1`), its installed path, mode and sha256, and whether the bytes are imported here.

Two checks:

  verify_repo(root)                    every imported file hashes to the manifest, no unlisted file sits under
                                       baseline/, every non-imported file carries a reason, and the secrets and
                                       identifier scan finds nothing outside its recorded allowlist.
  verify_install(root, journal, ...)   against the private installed copy: the manifest agrees with the install
                                       journal's `files` (same set, sha256, mode), every installed file hashes to
                                       the journal, and every imported file equals the installed file after the
                                       recorded redactions (none today) are applied to the installed bytes.

stdlib only. Exit 0 verified, 1 refused. No network, no credential, no write outside stdout.
"""
import hashlib, json, os, re, sys

SCHEMA = 'fsgg.learn-finite-profile.baseline/1'
HEX = re.compile(r'^[0-9a-f]{64}$')

# Secrets and identifier scan. Each pattern is a class; an allowlist entry is (file basename, exact match text).
SCAN = {
    'token': r'(ghp_|gho_|github_pat_|xox[bap]-|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9]{20,}|eyJ[A-Za-z0-9_-]{20,})',
    'private-key': r'BEGIN [A-Z ]*PRIVATE KEY',
    'secret-assignment': r'(?i)(password|passwd|secret|api[_-]?key|bearer)\s*[:=]',
    'email': r'[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}',
    'ipv4': r'\b(?:\d{1,3}\.){3}\d{1,3}\b',
    'private-hostname': r'\b[a-z0-9-]+\.(?:internal|lan|local|home)\b',
    'person': r'(?i)\b(eugen|ehotwagner|hotwagner)\b',
    'home-path': r'/home/[a-z0-9_-]+',
    'ssh-key': r'ssh-(rsa|ed25519)',
}


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def scan_text(name, text, allow):
    """Return [(class, line, match)] not covered by the allowlist {(basename, match)}."""
    found = []
    for cls, pat in SCAN.items():
        for m in re.finditer(pat, text):
            if (os.path.basename(name), m.group(0)) in allow:
                continue
            found.append((cls, text.count('\n', 0, m.start()) + 1, m.group(0)[:60]))
    return found


def load_manifest(path):
    with open(path, 'rb') as f:
        m = json.loads(f.read())
    return m


def check_manifest_shape(m, errors):
    if m.get('schema') != SCHEMA:
        errors.append('manifest schema differs')
    for k in ('journalSchema', 'installScriptSha256', 'files', 'scan'):
        if k not in m:
            errors.append(f'manifest lacks {k}')
    files = m.get('files', {})
    for ip, e in files.items():
        if not HEX.match(e.get('sha256', '')):
            errors.append(f'{ip}: sha256 is not 64 lowercase hex')
        if not re.match(r'^0o[0-7]{3,4}$', e.get('mode', '')):
            errors.append(f'{ip}: mode is not an octal string')
        if e.get('imported') is True:
            if not e.get('repoPath') or not HEX.match(e.get('repoSha256', '')):
                errors.append(f'{ip}: imported without repoPath/repoSha256')
            red = e.get('redactions', [])
            if not red and e.get('repoSha256') != e.get('sha256'):
                errors.append(f'{ip}: repoSha256 differs from sha256 without a recorded redaction')
            for r in red:
                if not (isinstance(r.get('line'), int) and HEX.match(r.get('originalLineSha256', '')) and 'replacement' in r):
                    errors.append(f'{ip}: malformed redaction record')
        elif e.get('imported') is False:
            if not e.get('reason'):
                errors.append(f'{ip}: not imported without a reason')
        else:
            errors.append(f'{ip}: imported must be true or false')


def verify_repo(root):
    """root = deployment/learn-finite-profile. Returns (errors, counts)."""
    errors = []
    m = load_manifest(os.path.join(root, 'baseline', 'BASELINE.json'))
    check_manifest_shape(m, errors)
    files = m.get('files', {})
    allow = {(a['file'], a['match']) for a in m.get('scan', {}).get('allowlist', [])}
    imported = {e['repoPath']: ip for ip, e in files.items() if e.get('imported') is True}
    n_ok = n_scan = 0
    for rp, ip in sorted(imported.items()):
        p = os.path.join(root, 'baseline', rp)
        if not os.path.isfile(p) or os.path.islink(p):
            errors.append(f'{rp}: missing or not a regular file')
            continue
        e = files[ip]
        if sha256_file(p) != e['repoSha256']:
            errors.append(f'{rp}: sha256 mismatch')
            continue
        if os.path.getsize(p) != e.get('repoSize', os.path.getsize(p)):
            errors.append(f'{rp}: size mismatch')
            continue
        n_ok += 1
        with open(p, 'rb') as f:
            text = f.read().decode('utf-8', 'replace')
        for cls, line, match in scan_text(p, text, allow):
            errors.append(f'{rp}:{line}: scan finding {cls}: {match}')
        n_scan += 1
    on_disk = set()
    base = os.path.join(root, 'baseline')
    for d, _, names in os.walk(base):
        for n in names:
            rel = os.path.relpath(os.path.join(d, n), base)
            if rel not in ('BASELINE.json',):
                on_disk.add(rel)
    for extra in sorted(on_disk - set(imported)):
        errors.append(f'{extra}: file under baseline/ is not in the manifest')
    counts = {'journalFiles': len(files), 'imported': len(imported), 'notImported': len(files) - len(imported),
              'hashVerified': n_ok, 'scanned': n_scan,
              'redactedFiles': sum(1 for e in files.values() if e.get('redactions')),
              'allowlistedScanMatches': len(allow)}
    return errors, counts


def apply_redactions(original, redactions):
    lines = original.split(b'\n')
    for r in redactions:
        i = r['line'] - 1
        if i < 0 or i >= len(lines) or sha256_bytes(lines[i]) != r['originalLineSha256']:
            raise ValueError(f'redaction at line {r["line"]} does not match the original line')
        lines[i] = r['replacement'].encode()
    return b'\n'.join(lines)


def verify_install(root, journal_path, install_roots):
    """Compare the manifest with the private install journal and installed files.
    install_roots maps an install path prefix to a directory override, e.g. {'/usr/local/libexec/fsgg-learn': dir}."""
    errors = []
    m = load_manifest(os.path.join(root, 'baseline', 'BASELINE.json'))
    with open(journal_path, 'rb') as f:
        j = json.loads(f.read())
    if j.get('schema') != m.get('journalSchema'):
        errors.append('journal schema differs from the manifest')
    if j.get('installScriptSha256') != m.get('installScriptSha256'):
        errors.append('install script sha256 differs')
    jf = j.get('files', {})
    mf = m.get('files', {})
    for ip in sorted(set(jf) | set(mf)):
        if ip not in mf:
            errors.append(f'{ip}: in the journal but not in the manifest')
        elif ip not in jf:
            errors.append(f'{ip}: in the manifest but not in the journal')
        else:
            if jf[ip].get('sha256') != mf[ip]['sha256']:
                errors.append(f'{ip}: journal sha256 differs from the manifest')
            if jf[ip].get('mode') != mf[ip]['mode']:
                errors.append(f'{ip}: journal mode differs from the manifest')
    checked = 0
    for ip, e in sorted(mf.items()):
        real = ip
        for prefix, repl in install_roots.items():
            if ip.startswith(prefix):
                real = repl + ip[len(prefix):]
        if not os.path.isfile(real):
            errors.append(f'{ip}: installed file is absent')
            continue
        data = open(real, 'rb').read()
        if sha256_bytes(data) != e['sha256']:
            errors.append(f'{ip}: installed bytes differ from the journal sha256')
            continue
        checked += 1
        if e.get('imported') is True:
            try:
                out = apply_redactions(data, e.get('redactions', [])) if e.get('redactions') else data
            except ValueError as err:
                errors.append(f'{ip}: {err}')
                continue
            if sha256_bytes(out) != e['repoSha256']:
                errors.append(f'{ip}: installed bytes after redactions differ from the imported file')
            repo = os.path.join(root, 'baseline', e['repoPath'])
            if os.path.isfile(repo) and open(repo, 'rb').read() != out:
                errors.append(f'{ip}: imported file differs from the installed bytes after redactions')
    return errors, {'installedVerified': checked, 'journalFiles': len(jf)}


def main(argv):
    root = os.path.dirname(os.path.abspath(__file__))
    errors, counts = verify_repo(root)
    out = {'repo': counts}
    if '--install-journal' in argv:
        journal = argv[argv.index('--install-journal') + 1]
        e2, c2 = verify_install(root, journal, {})
        errors += e2
        out['install'] = c2
    print(json.dumps({'errors': errors, **out}, indent=1, sort_keys=True))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

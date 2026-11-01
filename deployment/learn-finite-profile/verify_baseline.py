#!/usr/bin/env python3
"""Verify the LEARN finite-profile baseline import (Refs FS-GG/.github#4647).

The baseline directory holds the admitted, installed bytes of the #4745 finite profile that had no maintained
repository (#4745 closure gap 1). BASELINE.json records, for every file in the install journal
(`home-learn-install-journal/1`), its installed path, mode and sha256, and whether the bytes are imported here.

Two checks:

  verify_repo(root)                    every imported file (journal files and custody sources) hashes to the manifest,
                                       no unlisted file sits under baseline/, every non-imported file carries a
                                       reason, `sameBytesAs` claims hold, the secrets and identifier scan finds
                                       nothing outside its recorded allowlist, and every imported file has a recorded
                                       manual read (the scan detects classes; it cannot prove a file is clean).
  verify_install(root, journal, ...)   against the private installed copy: the manifest agrees with the install
                                       journal's `files` (same set, sha256, mode), every installed file hashes to
                                       the journal, and every imported file equals the installed file after the
                                       recorded redactions (none today) are applied to the installed bytes.
  verify_custody(root, dir)            against the private source custody directory: every `sources` and
                                       `custody.files` entry has a file with the recorded sha256.

stdlib only. Exit 0 verified, 1 refused. No network, no credential, no write outside stdout.
"""
import hashlib, json, os, re, sys

SCHEMA = 'fsgg.learn-finite-profile.baseline/1'
HEX = re.compile(r'^[0-9a-f]{64}$')

# Secrets and identifier scan. Each pattern is a class; an allowlist entry is (file basename, exact match text).
# The scan DETECTS classes. It cannot prove a file is clean: a person's name, a private path or a project codename has no
# reliable pattern, so the manifest also requires a recorded manual read of every imported file (`scan.manualRead`).
# `scan.personNames` is published with the manifest, so it may only hold names that are ALREADY public (for example an
# account name that appears in a public issue). Never add a name here to catch it: the list itself would then disclose it.
# Manual-read records are self-asserted: `reader` is a claim by whoever recorded it, not an authenticated identity.
HEXG = r'[0-9a-fA-F]{1,4}'
SCAN = {
    'token': r'(ghp_|gho_|github_pat_|xox[bap]-|AKIA[0-9A-Z]{16}|sk-[A-Za-z0-9]{20,}|eyJ[A-Za-z0-9_-]{20,})',
    'bearer': r'(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{8,}',
    'private-key': r'BEGIN [A-Z ]*PRIVATE KEY',
    'secret-assignment': r'(?i)(password|passwd|secret|api[_-]?key|bearer)\s*[:=]',
    'email': r'[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}',
    'ipv4': r'\b(?:\d{1,3}\.){3}\d{1,3}\b',
    'ipv6': (r'(?<![\w:])(?:%s:){7}%s(?![\w:])|(?<![\w:])(?:%s:){1,7}:(?:%s(?::%s){0,6})?(?![\w:])|(?<![\w:])::%s(?::%s){0,6}(?![\w:])'
             % (HEXG, HEXG, HEXG, HEXG, HEXG, HEXG, HEXG)),
    'mac': r'\b(?:[0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}\b',
    'uuid': r'\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b',
    'url': r'\b[a-zA-Z][a-zA-Z0-9+.-]*://[^\s\'"<>)\]]+',
    'hostname': r'\b(?:[a-z0-9-]+\.)+(?:com|org|net|io|dev|app|cloud|xyz|lan|internal|local|home|arpa|test|example)\b|\blocalhost\b',
    'home-path': r'/home/[a-z0-9_-]+',
    'ssh-key': r'ssh-(rsa|ed25519)',
    # people: a mention, a role word followed by a name token, and (data, not code) the names in scan.personNames
    'mention': r'(?<![\w@.])@[A-Za-z0-9][A-Za-z0-9-]{1,38}\b',
    'person-context': r'(?i)\b(?:author|owner|custodian|maintainer|operator|signed-off-by|co-authored-by|runs as|written by)\b[:\s]+\[?([A-Za-z][A-Za-z0-9_.-]{2,})',
}
PERSON_CONTEXT_STOP = {'the', 'and', 'for', 'root', 'only', 'one', 'that', 'this', 'with', 'from', 'its', 'not', 'any',
                       'each', 'into', 'files', 'file', 'script', 'scripts', 'entry', 'account', 'fsgg-learn-qual', 'home',
                       'supplied', 'owns', 'proposal', 'unit', 'units', 'uid', 'lock'}


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def sha256_bytes(b):
    return hashlib.sha256(b).hexdigest()


def scan_text(name, text, allow, persons=(), url_prefixes=()):
    """Return [(class, line, match)] not covered by the allowlist {(basename, match)}.
    `persons` are names that are always flagged; `url_prefixes` are public URL prefixes that are not flagged."""
    found = []
    base = os.path.basename(name)
    # spans of URLs under a public prefix: a host name inside one is the URL's, not a separate finding
    public = [m.span() for m in re.finditer(SCAN['url'], text) if any(m.group(0).startswith(p) for p in url_prefixes)]
    for cls, pat in SCAN.items():
        for m in re.finditer(pat, text):
            hit = m.group(0)
            if (base, hit) in allow:
                continue
            if cls == 'url' and any(hit.startswith(p) for p in url_prefixes):
                continue
            if cls in ('hostname', 'ipv4', 'ipv6', 'uuid', 'home-path') and any(a <= m.start() and m.end() <= b for a, b in public):
                continue
            if cls == 'person-context' and m.group(1).lower() in PERSON_CONTEXT_STOP:
                continue
            found.append((cls, text.count('\n', 0, m.start()) + 1, hit[:60]))
    for who in persons:
        for m in re.finditer(r'(?i)\b' + re.escape(who) + r'\b', text):
            if (base, m.group(0)) in allow:
                continue
            found.append(('person', text.count('\n', 0, m.start()) + 1, m.group(0)))
    return found


def load_manifest(path):
    with open(path, 'rb') as f:
        m = json.loads(f.read())
    return m


def _check_entry(label, e, errors):
    if not HEX.match(e.get('sha256', '')):
        errors.append(f'{label}: sha256 is not 64 lowercase hex')
    if e.get('imported') is True:
        if not e.get('repoPath') or not HEX.match(e.get('repoSha256', '')):
            errors.append(f'{label}: imported without repoPath/repoSha256')
        red = e.get('redactions', [])
        if not red and e.get('repoSha256') != e.get('sha256'):
            errors.append(f'{label}: repoSha256 differs from sha256 without a recorded redaction')
        for r in red:
            if not (isinstance(r.get('line'), int) and HEX.match(r.get('originalLineSha256', '')) and 'replacement' in r):
                errors.append(f'{label}: malformed redaction record')
    elif e.get('imported') is False:
        if not e.get('reason'):
            errors.append(f'{label}: not imported without a reason')
    else:
        errors.append(f'{label}: imported must be true or false')


def check_manifest_shape(m, errors):
    if m.get('schema') != SCHEMA:
        errors.append('manifest schema differs')
    for k in ('journalSchema', 'installScriptSha256', 'files', 'scan', 'sources', 'custody'):
        if k not in m:
            errors.append(f'manifest lacks {k}')
    sc = m.get('scan', {})
    if sc.get('personNames') and not sc.get('personNamesPolicy'):
        errors.append('scan.personNames needs personNamesPolicy (only names that are already public)')
    if not sc.get('manualReadNote'):
        errors.append('scan.manualReadNote must state that readers are self-asserted')
    files = m.get('files', {})
    for ip, e in files.items():
        _check_entry(ip, e, errors)
        if not re.match(r'^0o[0-7]{3,4}$', e.get('mode', '')):
            errors.append(f'{ip}: mode is not an octal string')
        same = e.get('sameBytesAs')
        if same is not None and (same not in files or files[same]['sha256'] != e.get('sha256')):
            errors.append(f'{ip}: sameBytesAs {same} does not name a journal file with the same sha256')
        for src in e.get('builtFrom', []):
            if src not in m.get('sources', {}):
                errors.append(f'{ip}: builtFrom {src} is not a recorded source')
    for name, e in m.get('sources', {}).items():
        _check_entry(f'source {name}', e, errors)
    for name, e in m.get('custody', {}).get('files', {}).items():
        if not HEX.match(e.get('sha256', '')) or not e.get('role'):
            errors.append(f'custody {name}: needs sha256 and role')


def _imported(m):
    """repoPath -> (label, entry) over journal files and custody sources."""
    out = {}
    for ip, e in m.get('files', {}).items():
        if e.get('imported') is True:
            out[e['repoPath']] = (ip, e)
    for name, e in m.get('sources', {}).items():
        if e.get('imported') is True:
            out[e['repoPath']] = (f'source {name}', e)
    return out


def verify_repo(root):
    """root = deployment/learn-finite-profile. Returns (errors, counts)."""
    errors = []
    m = load_manifest(os.path.join(root, 'baseline', 'BASELINE.json'))
    check_manifest_shape(m, errors)
    scan = m.get('scan', {})
    allow = {(a['file'], a['match']) for a in scan.get('allowlist', [])}
    persons = scan.get('personNames', [])
    url_prefixes = scan.get('urlAllowPrefixes', [])
    imported = _imported(m)
    read = set()
    for i, r in enumerate(scan.get('manualRead', [])):
        f, who, h = r.get('file'), r.get('reader'), r.get('sha256')
        if not isinstance(f, str) or not f.strip():
            errors.append(f'manualRead[{i}]: file is empty')
        if not isinstance(who, str) or not who.strip():
            errors.append(f'manualRead[{i}]: reader is empty')
        if not isinstance(h, str) or not HEX.match(h):
            errors.append(f'manualRead[{i}]: sha256 is not 64 lowercase hex')
        if isinstance(f, str) and f.strip():
            if f not in imported:
                errors.append(f'manualRead[{i}]: {f} is not an imported file')
            elif imported[f][1].get('repoSha256') != h:
                errors.append(f'manualRead[{i}]: {f} was read at other bytes than the current file')
            else:
                read.add((f, h))
    n_ok = n_scan = n_read = 0
    for rp, (label, e) in sorted(imported.items()):
        p = os.path.join(root, 'baseline', rp)
        if not os.path.isfile(p) or os.path.islink(p):
            errors.append(f'{rp}: missing or not a regular file')
            continue
        if sha256_file(p) != e['repoSha256']:
            errors.append(f'{rp}: sha256 mismatch')
            continue
        if os.path.getsize(p) != e.get('repoSize', os.path.getsize(p)):
            errors.append(f'{rp}: size mismatch')
            continue
        n_ok += 1
        with open(p, 'rb') as f:
            text = f.read().decode('utf-8', 'replace')
        for cls, line, match in scan_text(p, text, allow, persons, url_prefixes):
            errors.append(f'{rp}:{line}: scan finding {cls}: {match}')
        n_scan += 1
        if (rp, e['repoSha256']) in read:
            n_read += 1
        else:
            errors.append(f'{rp}: no recorded manual read for these bytes')
    on_disk = set()
    base = os.path.join(root, 'baseline')
    for d, _, names in os.walk(base):
        for n in names:
            rel = os.path.relpath(os.path.join(d, n), base)
            if rel not in ('BASELINE.json',):
                on_disk.add(rel)
    for extra in sorted(on_disk - set(imported)):
        errors.append(f'{extra}: file under baseline/ is not in the manifest')
    files = m.get('files', {})
    counts = {'journalFiles': len(files),
              'imported': sum(1 for e in files.values() if e.get('imported') is True),
              'notImported': sum(1 for e in files.values() if e.get('imported') is False),
              'sources': len(m.get('sources', {})),
              'importedSources': sum(1 for e in m.get('sources', {}).values() if e.get('imported') is True),
              'custodyReferences': len(m.get('custody', {}).get('files', {})),
              'hashVerified': n_ok, 'scanned': n_scan, 'manualReads': n_read,
              'redactedFiles': sum(1 for e in list(files.values()) + list(m.get('sources', {}).values()) if e.get('redactions')),
              'allowlistedScanMatches': len(allow), 'scanClasses': len(SCAN) + (1 if persons else 0)}
    return errors, counts


def verify_custody(root, custody_dir):
    """Every `sources` and `custody.files` entry has a custody file with the recorded sha256."""
    errors, n = [], 0
    m = load_manifest(os.path.join(root, 'baseline', 'BASELINE.json'))
    entries = {**{k: v for k, v in m.get('sources', {}).items()}, **m.get('custody', {}).get('files', {})}
    for name, e in sorted(entries.items()):
        p = os.path.join(custody_dir, e.get('custodyName', name))
        if not os.path.isfile(p):
            errors.append(f'{name}: absent from custody')
        elif sha256_file(p) != e['sha256']:
            errors.append(f'{name}: custody bytes differ from the recorded sha256')
        else:
            n += 1
    return errors, {'custodyVerified': n, 'custodyEntries': len(entries)}


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
    if '--custody-dir' in argv:
        e3, c3 = verify_custody(root, argv[argv.index('--custody-dir') + 1])
        errors += e3
        out['custody'] = c3
    print(json.dumps({'errors': errors, **out}, indent=1, sort_keys=True))
    return 1 if errors else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))

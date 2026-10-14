#!/usr/bin/env python3
"""Atlas: curated Git knowledge, immutable history, rebuildable SQLite FTS5 retrieval."""
from __future__ import annotations
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sqlite3
import sys
import tempfile

SCHEMA = 'fsgg.atlas/1'
WARNING = 50 * 1024 * 1024
CEILING = 100 * 1024 * 1024
RECORD_CAP = 32 * 1024
CATALOG_CAP = 8 * 1024 * 1024
RESULT_CAP = 64 * 1024
KINDS = {'architecture', 'decision', 'contract', 'dependency', 'lesson', 'incident', 'boundary', 'question'}
FIELDS = {'schema', 'id', 'repository', 'kind', 'title', 'summary', 'status', 'recordedAt', 'tags', 'citations', 'supersedes', 'conflictsWith'}
HEX = re.compile(r'^[0-9a-f]{64}$')
REPO = re.compile(r'^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')
SECRET = re.compile(r'(?i)(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|-----BEGIN (?:RSA |OPENSSH )?PRIVATE KEY-----|(?:authorization|password|secret|token)\s*[:=]\s*["\']?[A-Za-z0-9+/]{24,})')
GUIDANCE = '''Use $atlas as the first source for FS.GG project knowledge before substantial
investigation or implementation. Retrieve relevant records and follow their cited evidence.
Record reusable findings through Atlas before completing work; exclude logs, bulk reports,
source copies, credentials and runtime assets. Preserve superseded and conflicting findings.
Atlas owns curated knowledge; source defines implementation and live systems establish
current operational facts. Verify those facts before action. If Atlas is unavailable, state
the gap and use cited source directly; do not block otherwise authorized work.
'''

def require(condition, message):
    if not condition:
        raise ValueError(message)

def canonical(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':')) + '\n').encode()

def digest(raw):
    return hashlib.sha256(raw).hexdigest()

def unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate JSON key: ' + key)
        result[key] = value
    return result

def read_json(path, cap):
    require(path.is_file() and not path.is_symlink(), 'regular file required: ' + str(path))
    with path.open('rb') as stream:
        raw = stream.read(cap + 1)
    require(len(raw) <= cap, 'file byte cap exceeded: ' + str(path))
    return json.loads(raw, object_pairs_hook=unique), raw

def record_valid(record):
    require(isinstance(record, dict) and set(record) == FIELDS and record['schema'] == SCHEMA, 'closed Atlas record schema required')
    require(isinstance(record['id'], str) and re.fullmatch(r'[a-z0-9][a-z0-9./-]{2,159}', record['id']), 'invalid stable record id')
    require(isinstance(record['repository'], str) and REPO.fullmatch(record['repository']), 'invalid repository')
    require(record['kind'] in KINDS and record['status'] in {'recorded', 'disputed', 'question'}, 'invalid kind/status')
    for key, bound in (('title', 160), ('summary', 8000)):
        require(isinstance(record[key], str) and 0 < len(record[key].strip()) <= bound, 'invalid ' + key)
    require(not any(ord(c) < 32 and c not in '\n\t' for c in record['summary']), 'control bytes refused')
    require(isinstance(record['recordedAt'], str) and re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z', record['recordedAt']), 'UTC timestamp required')
    dt.datetime.fromisoformat(record['recordedAt'].replace('Z', '+00:00'))
    require(isinstance(record['tags'], list) and len(record['tags']) <= 20 and all(isinstance(t, str) and re.fullmatch(r'[a-z0-9][a-z0-9.-]{0,63}', t) for t in record['tags']), 'invalid tags')
    for key in ('supersedes', 'conflictsWith'):
        require(isinstance(record[key], list) and len(record[key]) <= 16 and len(set(record[key])) == len(record[key]) and all(isinstance(x, str) and HEX.fullmatch(x) for x in record[key]), 'invalid history relation')
    citations = record['citations']
    require(isinstance(citations, list) and 1 <= len(citations) <= 16, 'one to sixteen citations required')
    for citation in citations:
        require(isinstance(citation, dict) and set(citation) == {'repository', 'revision', 'path', 'sha256'}, 'closed citation schema required')
        require(isinstance(citation['repository'], str) and REPO.fullmatch(citation['repository']), 'invalid citation repository')
        require(isinstance(citation['revision'], str) and re.fullmatch(r'[0-9a-f]{40}', citation['revision']), 'immutable Git revision required')
        path = citation['path']
        require(isinstance(path, str) and len(path) <= 512 and path and not PurePosixPath(path).is_absolute() and '\\' not in path and all(x not in ('', '.', '..') for x in path.split('/')), 'unsafe citation path')
        require(isinstance(citation['sha256'], str) and HEX.fullmatch(citation['sha256']), 'citation content digest required')
    raw = canonical(record)
    require(len(raw) <= RECORD_CAP and not SECRET.search(raw.decode()), 'oversized record or credential pattern refused')
    return raw

def load_archives(paths):
    records = {}
    fingerprints = []
    total = 0
    for archive in paths:
        require(archive.is_dir() and not archive.is_symlink(), 'real archive directory required')
        catalog, raw = read_json(archive / 'catalog.json', CATALOG_CAP)
        require(isinstance(catalog, dict) and set(catalog) == {'schema', 'records'} and catalog['schema'] == SCHEMA and isinstance(catalog['records'], list), 'invalid catalog')
        require(len(catalog['records']) <= 100000 and all(isinstance(x, str) and HEX.fullmatch(x) for x in catalog['records']), 'invalid catalog digests')
        require(len(set(catalog['records'])) == len(catalog['records']), 'duplicate catalog digest')
        total += len(raw)
        fingerprints.append(digest(raw))
        directory = archive / 'records'
        require(directory.is_dir() and not directory.is_symlink(), 'records directory required')
        entries = set()
        # Count all archive payloads, reject arbitrary files before parsing them.
        for base, dirs, files in os.walk(archive, followlinks=False):
            for name in dirs:
                require(not (Path(base) / name).is_symlink(), 'archive directory symlink refused')
                require(Path(base) == archive and name == 'records', 'non-knowledge directory refused')
            for name in files:
                path = Path(base) / name
                relative = path.relative_to(archive).as_posix()
                require(not path.is_symlink() and path.is_file(), 'archive symlink/special file refused')
                require(relative in {'catalog.json', 'README.md'} or re.fullmatch(r'records/[0-9a-f]{64}\.json', relative), 'non-knowledge payload refused: ' + relative)
                if relative != 'catalog.json':
                    total += path.stat().st_size
                require(total <= CEILING, 'aggregate curated archive exceeds 100 MiB')
                if relative.startswith('records/'):
                    entries.add(name[:-5])
        require(entries == set(catalog['records']), 'missing or unindexed immutable records')
        for sha in catalog['records']:
            record, raw = read_json(directory / (sha + '.json'), RECORD_CAP)
            require(digest(raw) == sha and record_valid(record) == raw, 'record digest/canonical bytes differ')
            require(sha not in record['supersedes'] + record['conflictsWith'], 'self relation refused')
            records[sha] = record
    for sha, record in records.items():
        for previous in record['supersedes']:
            require(previous in records and records[previous]['id'] == record['id'] and records[previous]['repository'] == record['repository'], 'supersession must join existing same-owner history')
        require(all(other in records for other in record['conflictsWith']), 'missing conflict history')
    visiting = set()
    visited = set()
    def visit(sha):
        require(sha not in visiting, 'supersession cycle refused')
        if sha in visited:
            return
        visiting.add(sha)
        for previous in records[sha]['supersedes']:
            visit(previous)
        visiting.remove(sha); visited.add(sha)
    for sha in records:
        visit(sha)
    hidden = {previous for r in records.values() for previous in r['supersedes']}
    return records, hidden, digest(canonical(sorted(fingerprints))), total

def build_database(paths, destination):
    records, hidden, fingerprint, total = load_archives(paths)
    require(not destination.exists() and not destination.is_symlink(), 'database destination occupied')
    destination.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix='.atlas-', dir=destination.parent)
    os.close(fd)
    try:
        with sqlite3.connect(temporary) as db:
            db.execute('CREATE TABLE metadata (fingerprint TEXT NOT NULL)')
            db.execute('INSERT INTO metadata VALUES (?)', (fingerprint,))
            db.execute('CREATE TABLE records (digest TEXT PRIMARY KEY, id TEXT, repository TEXT, active INTEGER, payload TEXT)')
            db.execute('CREATE INDEX records_id ON records(id)')
            db.execute('CREATE VIRTUAL TABLE search USING fts5(digest UNINDEXED, title, summary, tags, repository)')
            for sha, record in sorted(records.items()):
                db.execute('INSERT INTO records VALUES (?,?,?,?,?)', (sha, record['id'], record['repository'], int(sha not in hidden), canonical(record).decode()))
                db.execute('INSERT INTO search VALUES (?,?,?,?,?)', (sha, record['title'], record['summary'], ' '.join(record['tags']), record['repository']))
        require(Path(temporary).stat().st_size <= 3 * CEILING, 'derived SQLite cache exceeds 300 MiB')
        os.link(temporary, destination)  # Never replace a live database or symlink.
    finally:
        Path(temporary).unlink(missing_ok=True)
    return {'records': len(records), 'archiveBytes': total, 'databaseBytes': destination.stat().st_size, 'fingerprint': fingerprint}

def emit(value):
    raw = canonical(value)
    require(len(raw) <= RESULT_CAP, 'retrieval exceeds 64 KiB; narrow query or lower limit')
    sys.stdout.write(raw.decode())

def capture(archive, input_path):
    record, _ = read_json(input_path, RECORD_CAP)
    raw = record_valid(record); sha = digest(raw)
    records, hidden, _, _ = load_archives([archive])
    if sha in records:
        return {'digest': sha, 'changed': False}
    require(all(x in records for x in record['supersedes'] + record['conflictsWith']), 'history must already exist in owner archive')
    existing = {sha for sha, item in records.items() if item['id'] == record['id'] and sha not in hidden}
    require(not existing or bool(existing.intersection(record['supersedes'] + record['conflictsWith'])), 'existing active knowledge needs an explicit supersession or conflict')
    for x in record['supersedes']:
        require(records[x]['id'] == record['id'] and records[x]['repository'] == record['repository'], 'cross-owner supersession refused')
    require(sum(p.stat().st_size for p in archive.rglob('*') if p.is_file()) + len(raw) + 128 <= CEILING, 'archive ceiling exceeded')
    catalog_path = archive / 'catalog.json'
    catalog, original = read_json(catalog_path, CATALOG_CAP)
    target = archive / 'records' / (sha + '.json')
    catalog['records'].append(sha); catalog['records'].sort()
    pending = archive / '.catalog.pending'
    created = False
    try:
        with pending.open('xb') as stream:
            created = True
            identity = os.fstat(stream.fileno()).st_ino
            stream.write(canonical(catalog))
        with target.open('xb') as stream:
            stream.write(raw)
        require(catalog_path.read_bytes() == original, 'concurrent capture detected; retain unindexed record for reconciliation')
        os.replace(pending, catalog_path)
    finally:
        if created and pending.exists() and pending.lstat().st_ino == identity:
            pending.unlink()
    return {'digest': sha, 'changed': True}

def guidance(root, check=False):
    policy, _ = read_json(root / '.fsgg/atlas.json', RECORD_CAP)
    require(policy.get('schema') == SCHEMA and policy.get('primaryKnowledgeStore') == 'Atlas', 'Atlas guidance policy missing')
    block = '<!-- atlas:begin -->\n' + GUIDANCE + '<!-- atlas:end -->\n'
    for name in ('AGENTS.md', 'CLAUDE.md'):
        path = root / name; text = path.read_text()
        require(text.count('<!-- atlas:begin -->') == text.count('<!-- atlas:end -->') <= 1, 'ambiguous Atlas guidance')
        updated = re.sub(r'<!-- atlas:begin -->.*?<!-- atlas:end -->\n?', block, text, flags=re.S) if '<!-- atlas:begin -->' in text else text.rstrip() + '\n\n' + block
        require(not check or updated == text, 'stale Atlas guidance: ' + name)
        if not check:
            path.write_text(updated)

def default_archives():
    local = Path.cwd() / 'docs/atlas'
    shared = Path.cwd().parent / '.github/docs/atlas'
    seed = Path(__file__).resolve().parents[1] / 'assets/seed'
    candidates = [local, shared if (shared / 'catalog.json').is_file() else seed]
    selected = []
    catalogs = set()
    for path in candidates:
        if not (path / 'catalog.json').is_file():
            continue
        _, raw = read_json(path / 'catalog.json', CATALOG_CAP)
        # Identical packaged/current catalogs are one logical source, not two budgets.
        if digest(raw) not in catalogs:
            selected.append(path); catalogs.add(digest(raw))
    require(selected, 'Atlas archive unavailable; provide --archive')
    return selected

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', action='append', type=Path, help='curated archive; repeat to aggregate repositories')
    parser.add_argument('--database', type=Path, help='explicit SQLite cache; queries verify archive freshness')
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('validate'); sub.add_parser('restore'); sub.add_parser('init')
    search = sub.add_parser('search'); search.add_argument('query'); search.add_argument('--limit', type=int, default=8); search.add_argument('--repository'); search.add_argument('--history', action='store_true')
    get = sub.add_parser('get'); get.add_argument('id'); get.add_argument('--history', action='store_true')
    add = sub.add_parser('capture'); add.add_argument('record', type=Path)
    guide = sub.add_parser('guidance'); guide.add_argument('--root', type=Path, default=Path.cwd()); guide.add_argument('--check', action='store_true')
    args = parser.parse_args(argv)
    try:
        if args.command == 'guidance':
            guidance(args.root, args.check); emit({'guidance': 'current'}); return 0
        paths = args.archive or ([Path.cwd() / 'docs/atlas'] if args.command in {'init', 'capture'} else default_archives())
        if args.command == 'init':
            require(len(paths) == 1 and not paths[0].exists() and not paths[0].is_symlink(), 'init requires one fresh owner archive')
            (paths[0] / 'records').mkdir(parents=True)
            (paths[0] / 'catalog.json').write_bytes(canonical({'schema': SCHEMA, 'records': []}))
            emit({'archive': str(paths[0]), 'initialized': True}); return 0
        if args.command == 'capture':
            require(len(paths) == 1, 'capture selects one owner archive'); emit(capture(paths[0], args.record)); return 0
        records, hidden, fingerprint, total = load_archives(paths)
        if total >= WARNING:
            print('Atlas: curated archives exceed 50 MiB warning budget', file=sys.stderr)
        if args.command == 'validate':
            emit({'records': len(records), 'archiveBytes': total, 'fingerprint': fingerprint}); return 0
        if args.command == 'restore':
            require(args.database is not None, '--database is required for restore'); emit(build_database(paths, args.database)); return 0
        with tempfile.TemporaryDirectory(prefix='atlas-query-') as temporary:
            path = args.database or Path(temporary) / 'atlas.sqlite'
            if args.database is None:
                build_database(paths, path)
            require(path.is_file() and not path.is_symlink(), 'database must be regular')
            with sqlite3.connect(path.resolve().as_uri() + '?mode=ro', uri=True) as db:
                require(db.execute('SELECT fingerprint FROM metadata').fetchone() == (fingerprint,), 'stale cache; rebuild from current archives to a fresh path')
                require(db.execute('SELECT count(*) FROM records').fetchone() == (len(records),), 'cache population differs from source')
                if args.command == 'search':
                    require(1 <= args.limit <= 30 and 0 < len(args.query) <= 512, 'query/limit outside bound')
                    terms = ' AND '.join('"' + word.replace('"', '""') + '"' for word in args.query.split())
                    sql = 'SELECT r.digest,r.payload,r.active FROM search s JOIN records r ON r.digest=s.digest WHERE search MATCH ?'
                    params = [terms]
                    if not args.history: sql += ' AND r.active=1'
                    if args.repository: sql += ' AND r.repository=?'; params.append(args.repository)
                    sql += ' ORDER BY bm25(search),r.id,r.digest LIMIT ?'; params.append(args.limit)
                    rows = db.execute(sql, params).fetchall()
                else:
                    sql = 'SELECT digest,payload,active FROM records WHERE id=?' + ('' if args.history else ' AND active=1') + ' ORDER BY digest LIMIT 31'
                    rows = db.execute(sql, (args.id,)).fetchall()
                    require(len(rows) <= 30, 'history exceeds retrieval bound; use narrower archive')
                for sha, payload, active in rows:
                    require(sha in records and payload.encode() == canonical(records[sha]) and bool(active) == (sha not in hidden), 'cache result differs from cited archive')
                emit([{'digest': sha, 'active': bool(active), **records[sha]} for sha, payload, active in rows])
        return 0
    except (ValueError, OSError, sqlite3.Error, TypeError, KeyError, RecursionError) as error:
        print('Atlas refused: ' + str(error), file=sys.stderr); return 2

if __name__ == '__main__':
    raise SystemExit(main())

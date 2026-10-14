#!/usr/bin/env python3
"""Actual Atlas storage/retrieval invariants and archive delivery checks; no network."""
import argparse
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / '.agents/skills/atlas/scripts/atlas.py'
spec = importlib.util.spec_from_file_location('atlas', HELPER)
atlas = importlib.util.module_from_spec(spec); spec.loader.exec_module(atlas)

def fixture(title='A useful architectural finding'):
    return {'schema': atlas.SCHEMA, 'id': 'fixture/boundary', 'repository': 'FS-GG/Fixture',
            'kind': 'architecture', 'title': title, 'summary': 'Governance is optional; inspect the cited source before action.',
            'status': 'recorded', 'recordedAt': '2026-10-14T00:00:00Z', 'tags': ['governance'],
            'citations': [{'repository': 'FS-GG/Fixture', 'revision': 'a'*40, 'path': 'README.md', 'sha256': 'b'*64}],
            'supersedes': [], 'conflictsWith': []}

def archive(path):
    (path/'records').mkdir(parents=True)
    (path/'catalog.json').write_bytes(atlas.canonical({'schema': atlas.SCHEMA, 'records': []}))

class AtlasControls(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(); self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name); self.archive = self.root/'archive'; archive(self.archive)
    def add(self, record):
        path = self.root/'input.json'; path.write_bytes(atlas.canonical(record))
        return atlas.capture(self.archive, path)['digest']
    def query(self, *args):
        result = subprocess.run([sys.executable, '-B', str(HELPER), '--archive', str(self.archive), *args], capture_output=True, text=True)
        return result
    def test_actual_restore_search_and_citation_roundtrip(self):
        sha = self.add(fixture()); db = self.root/'cache.sqlite'
        atlas.build_database([self.archive], db)
        result = self.query('--database', str(db), 'search', 'governance')
        self.assertEqual(result.returncode, 0, result.stderr)
        record = json.loads(result.stdout)[0]
        self.assertEqual(record['digest'], sha); self.assertEqual(record['citations'], fixture()['citations'])
        self.assertEqual(db.stat().st_mode & 0o777, 0o600)
    def test_supersession_and_conflict_preserve_explicit_history(self):
        first = self.add(fixture()); second = fixture('Second finding'); second['supersedes']=[first]
        later = self.add(second); competing=fixture('Conflicting finding'); competing['conflictsWith']=[later]
        other=self.add(competing)
        active=json.loads(self.query('get','fixture/boundary').stdout)
        self.assertEqual({r['digest'] for r in active}, {later,other})
        self.assertEqual(len(json.loads(self.query('get','fixture/boundary','--history').stdout)),3)
        self.assertTrue((self.archive/'records'/f'{first}.json').exists())
    def test_capture_idempotent_and_database_no_replace(self):
        self.add(fixture()); self.add(fixture())
        self.assertEqual(len(atlas.load_archives([self.archive])[0]),1)
        db=self.root/'cache';db.write_bytes(b'occupied')
        with self.assertRaises(ValueError):atlas.build_database([self.archive],db)
        self.assertEqual(db.read_bytes(),b'occupied')
        link=self.root/'dangling';link.symlink_to(self.root/'absent')
        with self.assertRaises(ValueError):atlas.build_database([self.archive],link)
        self.assertTrue(link.is_symlink())
    def test_stale_cache_cannot_answer_as_current(self):
        self.add(fixture()); db=self.root/'cache';atlas.build_database([self.archive],db)
        newer=fixture('Updated'); newer['supersedes']=[atlas.digest(atlas.canonical(fixture()))];self.add(newer)
        result=self.query('--database',str(db),'search','governance')
        self.assertEqual(result.returncode,2);self.assertIn('stale cache',result.stderr)
    def test_cache_cannot_replace_archived_truth_or_hide_population(self):
        self.add(fixture()); db=self.root/'cache';atlas.build_database([self.archive],db)
        with sqlite3.connect(db) as connection:
            connection.execute('UPDATE records SET payload=?', ('{}',))
        result=self.query('--database',str(db),'search','governance')
        self.assertEqual(result.returncode,2);self.assertIn('differs from cited archive',result.stderr)
        with sqlite3.connect(db) as connection:connection.execute('DELETE FROM records')
        result=self.query('--database',str(db),'search','governance')
        self.assertEqual(result.returncode,2);self.assertIn('population differs',result.stderr)
    def test_reject_bulk_logs_credentials_and_large_records(self):
        for name in ['run.log','report.html','image.zip','source.fs','store.sqlite']:
            path=self.archive/name;path.write_bytes(b'payload')
            with self.assertRaises(ValueError):atlas.load_archives([self.archive])
            path.unlink()
        record=fixture();record['summary']='ghp_'+'a'*35
        with self.assertRaises(ValueError):atlas.record_valid(record)
        record=fixture();record['summary']='a'*9000
        with self.assertRaises(ValueError):atlas.record_valid(record)
    def test_missing_history_changed_bytes_and_symlinks_refuse(self):
        sha=self.add(fixture()); path=self.archive/'records'/f'{sha}.json';raw=path.read_bytes()
        path.write_bytes(raw+b' ')
        with self.assertRaises(ValueError):atlas.load_archives([self.archive])
        path.unlink();path.symlink_to(self.root/'input.json')
        with self.assertRaises(ValueError):atlas.load_archives([self.archive])
        path.unlink();path.write_bytes(raw)
        row=fixture('Orphan');row['supersedes']=['c'*64]
        with self.assertRaises(ValueError):self.add(row)
    def test_cross_owner_history_and_duplicate_json_keys_refuse(self):
        first=self.add(fixture()); row=fixture('Wrong owner');row['repository']='FS-GG/Other';row['supersedes']=[first]
        with self.assertRaises(ValueError):self.add(row)
        path=self.root/'bad.json';path.write_text('{"schema":1,"schema":2}')
        with self.assertRaises(ValueError):atlas.read_json(path,1024)
    def test_aggregate_size_and_retrieval_caps_are_enforced(self):
        first=self.add(fixture())
        old=atlas.CEILING
        try:
            atlas.CEILING=100
            with self.assertRaises(ValueError):atlas.load_archives([self.archive])
        finally:atlas.CEILING=old
        with self.assertRaises(ValueError):atlas.emit({'oversized':'a'*atlas.RESULT_CAP})
        self.assertEqual(self.query('search','governance','--limit','31').returncode,2)
    def test_two_archives_aggregate_and_keep_owner_conflicts(self):
        self.add(fixture()); other=self.root/'other';archive(other)
        row=fixture('Other repository');row['repository']='FS-GG/Other';row['id']='other/finding'
        path=self.root/'other.json';path.write_bytes(atlas.canonical(row));atlas.capture(other,path)
        records,_,_,_=atlas.load_archives([self.archive,other]);self.assertEqual(len(records),2)

def delivery_checks(baseline=None):
    records,_,_,size=atlas.load_archives([ROOT/'docs/atlas'])
    for skill_root in ['.agents/skills','.claude/skills']:
        seed=ROOT/skill_root/'atlas/assets/seed'
        atlas.load_archives([seed])
        for path in (ROOT/'docs/atlas').rglob('*'):
            if path.is_file():
                assert (seed/path.relative_to(ROOT/'docs/atlas')).read_bytes()==path.read_bytes(), 'seed drift'
    atlas.guidance(ROOT,check=True)
    policy=json.loads((ROOT/'.fsgg/atlas.json').read_text())
    assert policy['warningBytes']==atlas.WARNING and policy['ceilingBytes']==atlas.CEILING and policy['recordBytes']==atlas.RECORD_CAP and policy['retrievalBytes']==atlas.RESULT_CAP
    assert 'knowledgePolicy: .fsgg/atlas.json' in (ROOT/'.fsgg/agents.yml').read_text()
    if baseline:
        assert len(baseline)==40 and all(c in '0123456789abcdef' for c in baseline)
        subprocess.run(['git','cat-file','-e',baseline+'^{commit}'],cwd=ROOT,check=True)
        listing=subprocess.check_output(['git','ls-tree',baseline,'docs/atlas/catalog.json'],cwd=ROOT)
        if listing:
            catalog=json.loads(subprocess.check_output(['git','show',baseline+':docs/atlas/catalog.json'],cwd=ROOT))
            assert set(catalog['records']).issubset(records), 'baseline history discarded'
    # Verify .github citations using the actual pinned Git objects, without network or mutable-file inference.
    checked=set()
    for record in records.values():
        for citation in record['citations']:
            if citation['repository']!='FS-GG/.github':continue
            key=(citation['revision'],citation['path'],citation['sha256'])
            if key in checked:continue
            available=subprocess.run(['git','cat-file','-e',key[0]+'^{commit}'],cwd=ROOT,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
            if available.returncode:
                # Closed-PR commit citations remain valid immutable GitHub references, but
                # a default-branch clone does not necessarily contain those Git objects.
                print('Atlas citation not locally available; verify at retrieval: '+key[0]+':'+key[1])
                continue
            raw=subprocess.check_output(['git','show',key[0]+':'+key[1]],cwd=ROOT)
            assert hashlib.sha256(raw).hexdigest()==key[2], 'pinned citation drift'
            checked.add(key)
    print(f'Atlas delivery: {len(records)} records, {size} curated bytes, {len(checked)} local immutable citations verified')

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--baseline');args=parser.parse_args()
    suite=unittest.defaultTestLoader.loadTestsFromTestCase(AtlasControls)
    result=unittest.TextTestRunner(verbosity=1).run(suite)
    if not result.wasSuccessful():raise SystemExit(1)
    delivery_checks(args.baseline)

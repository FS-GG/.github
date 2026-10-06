"""In-memory SQL controls for the actual additive schema; no canonical store access."""
import re
import json
import sqlite3
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[2] / 'src/FS.GG.Coord.Cli/TelemetryStoreApplication.fs'

class SchemaControls(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(':memory:')
        self.db.execute('PRAGMA foreign_keys=ON')
        text = SOURCE.read_text()
        names = ['migrationSql'] + [f'migration{i}Sql' for i in range(2, 15)]
        migrations = []
        for name in names:
            match = re.search(r'let private '+name+r'\s*=\s*"""(.*?)"""', text, re.S)
            self.assertIsNotNone(match)
            migrations.append(match[1])
        for sql in migrations[:-1]:
            self.db.executescript(sql)
        self.db.execute("INSERT INTO ingest_facts VALUES('u','runtime-turn-usage','OLD',0,'sha256:raw','{}')")
        self.db.commit()
        self.db.executescript(migrations[-1])
        self.db.execute("INSERT INTO ingest_facts VALUES('a','efficiency-resource-allocation/1',NULL,0,'sha256:allocation','{}')")
        self.db.execute("INSERT INTO efficiency_records VALUES('a','efficiency-resource-allocation/1',NULL,0,'sha256:allocation','{}')")

    def tearDown(self):
        self.db.close()

    def test_additive_migration_preserves_existing_raw_facts_and_historical_clock_unknown(self):
        self.assertEqual(14, self.db.execute('PRAGMA user_version').fetchone()[0])
        self.assertEqual(('OLD', 0, 'sha256:raw'), self.db.execute("SELECT item_id,revision,content_digest FROM ingest_facts WHERE identity='u'").fetchone())
        self.assertEqual(0, self.db.execute('SELECT count(*) FROM fact_acceptance_times').fetchone()[0])

    def test_global_allocation_has_one_resource_key_not_another_usage_event(self):
        row = ('a', 'u', 0, 'sha256:raw', 'OLD', None, None, 'tokens', 'fixture', 'native', 'tokens-total')
        self.db.execute('INSERT INTO efficiency_allocation_context VALUES(?,?,?,?,?,?,?,?,?,?,?)', row)
        self.db.execute("INSERT INTO ingest_facts VALUES('b','efficiency-resource-allocation/1',NULL,0,'other','{}')")
        self.db.execute("INSERT INTO efficiency_records VALUES('b','efficiency-resource-allocation/1',NULL,0,'other','{}')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute('INSERT INTO efficiency_allocation_context VALUES(?,?,?,?,?,?,?,?,?,?,?)', ('b',)+row[1:])
        self.assertEqual(1, self.db.execute("SELECT count(*) FROM ingest_facts WHERE kind='runtime-turn-usage'").fetchone()[0])

    def test_source_revision_change_invalidates_classification_without_dropping_source(self):
        self.db.execute("INSERT INTO efficiency_allocation_context VALUES('a','u',0,'sha256:raw','OLD',NULL,NULL,'tokens','fixture','native','tokens-total')")
        self.assertEqual(1, self.db.execute('SELECT classification_current FROM efficiency_current_allocations').fetchone()[0])
        self.db.execute("UPDATE ingest_facts SET revision=1,content_digest='new' WHERE identity='u'")
        self.assertEqual(0, self.db.execute('SELECT classification_current FROM efficiency_current_allocations').fetchone()[0])
        self.assertEqual(1, self.db.execute("SELECT count(*) FROM ingest_facts WHERE kind='runtime-turn-usage'").fetchone()[0])

    def test_queue_uses_stable_outcome_identity_not_corrected_item_alias(self):
        for identity, item in [('old','OLD'),('new','NEW')]:
            self.db.execute("INSERT INTO efficiency_analysis_requests(request_id,stable_outcome_identity,outcome_epoch,effective_item_id,scope,evidence_digest,policy_version,state,revision,content_digest,canonical,evidence_packet,owner_producer,owner_stream,requested_at,updated_at) VALUES(?,?,1,?,'native-item','digest','policy','pending',0,'content','{}',?,'producer','stream','now','now')", (identity,'outcome',item,b'{}'))
        def reserve(request, claim):
            self.db.execute("INSERT INTO efficiency_analysis_reservations VALUES('outcome','1','policy',1,?,?,'producer','stream','dispatch',1,'reserved')", (request,claim))
        reserve('old','claim-old')
        with self.assertRaises(sqlite3.IntegrityError):
            reserve('new','claim-new')

    def test_only_one_active_claim_across_scopes_and_corrected_aliases(self):
        for identity,item,scope in [('first','OLD','native-item'),('second','NEW','provisional-delivery')]:
            self.db.execute("INSERT INTO efficiency_analysis_requests(request_id,stable_outcome_identity,effective_item_id,scope,evidence_digest,policy_version,state,revision,content_digest,canonical,evidence_packet,owner_producer,owner_stream,requested_at,updated_at) VALUES(?, 'outcome', ?, ?, 'digest','policy','pending',0,'content','{}',?,'p','s','now','now')",(identity,item,scope,b'{}'))
        self.db.execute("INSERT INTO efficiency_analysis_reservations VALUES('outcome','unknown','policy',1,'first','claim1','p','s','dispatch1',1,'reserved')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_analysis_reservations VALUES('outcome','unknown','policy',2,'second','claim2','p','s','dispatch2',2,'reserved')")
        self.db.execute("UPDATE efficiency_analysis_reservations SET state='unknown' WHERE claim_id='claim1'")
        self.db.execute("INSERT INTO efficiency_analysis_reservations VALUES('outcome','unknown','policy',2,'second','claim2','p','s','dispatch2',2,'reserved')")
        self.assertEqual(2,self.db.execute('SELECT count(*) FROM efficiency_analysis_reservations').fetchone()[0])

    def test_queue_refuses_running_vocabulary_and_fourth_reservation(self):
        self.db.execute("INSERT INTO efficiency_analysis_requests(request_id,stable_outcome_identity,effective_item_id,scope,evidence_digest,policy_version,state,revision,content_digest,canonical,evidence_packet,owner_producer,owner_stream,requested_at,updated_at) VALUES('r','outcome','I','native-item','digest','policy','pending',0,'content','{}',?,'p','s','now','now')", (b'{}',))
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE efficiency_analysis_requests SET state='running'")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_analysis_reservations VALUES('outcome','unknown','policy',4,'r','claim','p','s','dispatch',1,'reserved')")

    def test_allocation_dimension_rename_cannot_duplicate_a_real_counter(self):
        self.db.execute("INSERT INTO efficiency_allocation_context VALUES('a','u',0,'sha256:raw','OLD',NULL,NULL,'model-tokens','fixture','native','tokens-total')")
        self.db.execute("INSERT INTO ingest_facts VALUES('b','efficiency-resource-allocation/1',NULL,0,'other','{}')")
        self.db.execute("INSERT INTO efficiency_records VALUES('b','efficiency-resource-allocation/1',NULL,0,'other','{}')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_allocation_context VALUES('b','u',0,'sha256:raw','OLD',NULL,NULL,'renamed-dimension','fixture','native','tokens-total')")

    def test_new_queue_and_export_sql_resolves_actual_schema(self):
        text = SOURCE.read_text()
        regions = [text[text.index('    let private efficiencyNativeWitnesses '):text.index('    let private recordEfficiencyAcceptance ')],
                   text[text.index('    let private efficiencyJson '):text.index('    let provisionReceiptWorkspace ')],
                   text[text.index('    let private efficiencySourceFingerprint '):text.index('    let private dashboardSnapshotBound\n')],
                   text[text.index('    let efficiencyAnalysisInspect '):text.index('    let dashboardSnapshotWithHooks ')]]
        checked = 0
        for region in regions:
            triple = re.findall(r'"""(.*?)"""',region,re.S)
            without_triple = re.sub(r'""".*?"""','',region,flags=re.S)
            literals = triple + re.findall(r'"([^"\\]*(?:\\.[^"\\]*)*)"', without_triple)
            for literal in literals:
                sql = literal.strip()
                if not re.match(r'^(SELECT|UPDATE|INSERT|DELETE|WITH) ', sql):
                    continue
                parameters = {name: None for name in re.findall(r'\$([A-Za-z][A-Za-z0-9_]*)', sql)}
                self.db.execute('EXPLAIN ' + sql, parameters).fetchall()
                checked += 1
        self.assertGreater(checked, 60)

    def test_native_witness_queries_resolve_real_tables_without_widening_native_ingest(self):
        text = SOURCE.read_text()
        region = text[text.index('    let private efficiencyNativeWitnesses '):text.index('    let private efficiencyResourceCounter ')]
        sqls = re.findall(r'"""(.*?)"""', region, re.S)
        self.assertEqual(2, len(sqls))
        for sql in sqls:
            parameters = {name: None for name in re.findall(r'\$([A-Za-z][A-Za-z0-9_]*)', sql)}
            self.db.execute('EXPLAIN '+sql,parameters).fetchall()
        whitelist = text[text.index('set [ "runtime-native-inventory/1"'):]
        whitelist = whitelist[:whitelist.index(']')]
        self.assertNotIn('runtime-admission', whitelist)
        self.assertNotIn('efficiency-resource-allocation', whitelist)

    def test_epoch_duplicate_open_and_out_of_order_close_are_rejected(self):
        self.db.execute("INSERT INTO efficiency_receiver_order(receipt_key,producer,stream,accepted_at) VALUES('begin','p','s','observed')")
        self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',1,'dispatch','OLD','begin',1,'open','[]',NULL,NULL,NULL,NULL)")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',2,'new-dispatch','NEW','begin',1,'open','[]',NULL,NULL,NULL,NULL)")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("UPDATE efficiency_outcome_epochs SET state='closed',outcome_identity='outcome',outcome_revision=0,close_sequence=0,close_refs='[]'")
        self.db.execute("UPDATE efficiency_outcome_epochs SET state='closed',outcome_identity='outcome',outcome_revision=0,close_sequence=1,close_refs='[]'")
        self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',2,'new-dispatch','NEW','begin',1,'open','[]',NULL,NULL,NULL,NULL)")
        self.assertEqual([1,2], [row[0] for row in self.db.execute('SELECT epoch FROM efficiency_outcome_epochs ORDER BY epoch')])

    def test_original_group_selection_deduplicates_members_without_minting_legacy_facts(self):
        for identity,item,source in [('parent-pop','OLD','p'),('child-pop','CHILD','c'),('parent-replay','OLD','p2')]:
            self.db.execute("INSERT INTO budget_population_facts VALUES(?,?,'GROUP','open','fixture',?,0)",(identity,item,source))
        self.db.execute("UPDATE ingest_facts SET canonical='{\"total\":10}' WHERE identity='u'")
        self.db.execute("INSERT INTO ingest_facts VALUES('u2','runtime-turn-usage','CHILD',0,'child','{\"total\":20}')")
        text=SOURCE.read_text()
        query=re.search(r'"(SELECT identity,kind,revision,content_digest,canonical FROM current_ingest_facts WHERE item_id=\$item[^"\n]+)"',text[text.index('    let private efficiencyExportInSnapshot '):])[1]
        grouped=query.replace('item_id=$item','item_id IN (SELECT value FROM json_each($members))')
        rows=self.db.execute(grouped,{'item':'GROUP','members':json.dumps(['OLD','CHILD'])}).fetchall()
        self.assertEqual(['u','u2'],[row[0] for row in rows])
        self.assertEqual(30,sum(json.loads(row[4])['total'] for row in rows))
        self.assertEqual(2,len(set(row[0] for row in rows)))
        self.assertEqual(0,self.db.execute('SELECT count(*) FROM fact_acceptance_times').fetchone()[0])

    def test_genuine_reopen_can_close_a_new_revision_of_same_outcome_identity(self):
        self.db.execute("INSERT INTO efficiency_receiver_order(receipt_key,producer,stream,accepted_at) VALUES('first','p','s','first')")
        self.db.execute("INSERT INTO efficiency_receiver_order(receipt_key,producer,stream,accepted_at) VALUES('reopen','p','s','later')")
        self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',1,'dispatch1','OLD','first',1,'closed','[]','same-outcome',0,1,'[]')")
        self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',2,'dispatch2','NEW','reopen',2,'closed','[]','same-outcome',1,2,'[]')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_outcome_epochs VALUES('original',3,'dispatch3','NEW','reopen',2,'closed','[]','same-outcome',1,2,'[]')")
        self.assertEqual([(1,0),(2,1)],self.db.execute('SELECT epoch,outcome_revision FROM efficiency_outcome_epochs ORDER BY epoch').fetchall())

    def test_action_replay_returns_original_receipt_after_later_queue_revision(self):
        first = {'revision':1,'lastAction':'claim','lastInputDigest':'original-input','claimedAt':'receiver-first'}
        later = {'revision':2,'lastAction':'attach-invocation','lastInputDigest':'later-input','claimedAt':'receiver-first'}
        for record in [first,later]:
            self.db.execute('INSERT INTO efficiency_analysis_history VALUES(?,?,?,?,?)',('request',record['revision'],'digest',json.dumps(record),'receiver'))
        text = SOURCE.read_text()
        query = re.search(r'replayLookup.CommandText <- "([^"]+)"',text)[1]
        receipt = self.db.execute(query,{'id':'request','action':'claim','input':'original-input'}).fetchone()[0]
        self.assertEqual(first,json.loads(receipt))
        self.assertIsNone(self.db.execute(query,{'id':'request','action':'claim','input':'changed-input'}).fetchone())
        self.assertEqual(2,self.db.execute('SELECT count(*) FROM efficiency_analysis_history').fetchone()[0])

    def test_historical_records_cannot_overwrite_same_revision(self):
        self.db.execute("INSERT INTO efficiency_record_history VALUES('a',0,'first','{}','real-receiver-time','receipt')")
        with self.assertRaises(sqlite3.IntegrityError):
            self.db.execute("INSERT INTO efficiency_record_history VALUES('a',0,'second','{}','later','receipt2')")

if __name__ == '__main__':
    unittest.main()

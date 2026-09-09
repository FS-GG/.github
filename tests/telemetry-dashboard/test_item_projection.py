import importlib.util
import json
import pathlib
import sqlite3
import tempfile
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("dashboard_items",ROOT/"tools/telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)

SCHEMA="""
PRAGMA journal_mode=WAL;
CREATE TABLE budget_dirty_items(item_id TEXT PRIMARY KEY);
CREATE TABLE budget_population_facts(identity TEXT PRIMARY KEY,item_id TEXT,original_item_id TEXT,state TEXT,source_ref TEXT,fact_revision INTEGER);
CREATE TABLE native_item_outcomes(identity TEXT PRIMARY KEY,item_id TEXT,repository TEXT,pr_number INTEGER,base_ref TEXT,base_sha TEXT,head TEXT,outcome TEXT,code_delivery TEXT,merge_commit TEXT,occurred_at TEXT,observed_at TEXT,source_kind TEXT,source_ref TEXT,fact_revision INTEGER);
CREATE TABLE runtime_terminals(identity TEXT PRIMARY KEY,item_id TEXT,invocation_id TEXT,thread_id TEXT,outcome TEXT,exit_code INTEGER);
CREATE TABLE invocation_lineage(identity TEXT PRIMARY KEY,item_id TEXT,dispatch_id TEXT,invocation_id TEXT,relation TEXT,parent_invocation_id TEXT,root_invocation_id TEXT,runtime TEXT,fact_revision INTEGER);
CREATE TABLE operational_event_times(identity TEXT PRIMARY KEY,item_id TEXT,invocation_id TEXT,event TEXT,occurred_at TEXT,occurred_clock_provenance TEXT,observed_at TEXT,observed_clock_provenance TEXT,fact_revision INTEGER);
CREATE TABLE runtime_turn_usage(identity TEXT PRIMARY KEY,item_id TEXT,invocation_id TEXT,thread_id TEXT,turn_id TEXT,turn_sequence INTEGER,provider TEXT,requested_model TEXT,observed_model TEXT,requested_effort TEXT,observed_effort TEXT,backend TEXT,accounting_scope TEXT,provenance TEXT,input_count INTEGER,cached_input INTEGER,output_count INTEGER,reasoning INTEGER,total INTEGER);
CREATE TABLE runtime_gaps(identity TEXT PRIMARY KEY,item_id TEXT,invocation_id TEXT,code TEXT);
CREATE TABLE ci_runs(identity TEXT PRIMARY KEY,item_id TEXT,repository TEXT,run_id INTEGER,attempt INTEGER,workflow TEXT,event TEXT,head TEXT,status TEXT,conclusion TEXT,created_at TEXT,started_at TEXT,updated_at TEXT);
PRAGMA user_version=7;
"""

def labels():
    return {"schema":D.LABELS_SCHEMA,"items":{"ORIGINAL":{"key":"public-item","label":"Public item","url":"https://github.com/FS-GG/.github/issues/1","repositories":["FS-GG/.github"],"notes":[{"kind":"complication","text":"Documented complication.","evidenceUrl":"https://github.com/FS-GG/.github/pull/7"}]}},"models":{"model-r":"Requested","model-o":"Observed"},"efforts":{"medium":"Medium","high":"High"},"scopes":{"provider-a|scope-a":"Scope A","provider-b|scope-b":"Scope B"}}

def item_detail(item="child"):
    sentinel="PRIVATE prose digest path /secret/thread-id"
    evidence=[{"kind":"test","digest":"a"*64}]
    return {"schema":"fsgg.telemetry.item-detail/1","item":item,
      "activities":[{"activityId":"private-activity","invocationId":"private-invocation","attemptId":"private-attempt","category":"repair","startedAt":"2026-09-09T07:00:00Z","endedAt":"2026-09-09T07:01:00Z","clockProvenance":"host-wall","evidence":evidence,"summary":sentinel,"revision":1}],"activityTruncated":False,
      "usageAttributions":[{"usageIdentity":"private-usage","activityId":"private-activity","classification":"direct","input":100,"cachedInput":40,"output":20,"reasoning":None,"total":120,"revision":1}],"attributionTruncated":False,
      "complications":[{"attemptId":"private-attempt","activityId":"private-activity","trigger":"test-failure","cause":"product-defect","occurredAt":"2026-09-09T07:00:30Z","synopsis":sentinel,"evidence":evidence,"revision":1}],"complicationTruncated":False,
      "reviews":[{"scope":"attempt","attemptId":"private-attempt","revision":2,"outcomeSynopsis":sentinel,"wentWell":[sentinel],"problems":[sentinel],"avoidableDelayOrRework":[],"processObservations":[sentinel],"remainingRisks":[],"concreteImprovements":[sentinel],"evidence":evidence,"evidenceCoverage":"partial","populationCoverage":"complete","confidence":"high","reviewerModel":"model-o","reviewerEffort":"high","reviewedAt":"2026-09-09T07:02:00Z","durationSeconds":30}],"reviewTruncated":False,
      "accounting":{"nativeTotal":120,"direct":120,"mixed":0,"unclassified":0,"missingAttribution":1,"allocation":"native-exact-only"}}

class ItemProjectionTests(unittest.TestCase):
    def make_store(self,root):
        db=sqlite3.connect(root/"telemetry.sqlite3"); db.executescript(SCHEMA)
        db.execute("INSERT INTO budget_population_facts VALUES(?,?,?,?,?,?)",("pop","child","ORIGINAL","completed","derived:population",2))
        outcome="INSERT INTO native_item_outcomes VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)"
        db.execute(outcome,("old","child","FS-GG/.github",6,"main","a"*40,"b"*40,"delivered","delivered","c"*40,"2026-09-09T07:00:00Z","2026-09-09T07:01:00Z","routine-delivery","delivery:6",1))
        db.execute(outcome,("new","child","FS-GG/.github",7,"main","a"*40,"d"*40,"delivered-after-readback","delivered","e"*40,"2026-09-09T08:00:00Z","2026-09-09T08:01:00Z","routine-delivery","delivery:7",2))
        db.executemany("INSERT INTO runtime_terminals VALUES(?,?,?,?,?,?)",[("t1","child","inv-root","thread","completed",0),("t2","child","inv-child","thread","failed",1)])
        db.executemany("INSERT INTO invocation_lineage VALUES(?,?,?,?,?,?,?,?,?)",[("l1","child","d1","inv-root","root",None,"inv-root","codex-exec",1),("l2","child","d2","inv-child","child","inv-root","inv-root","codex-exec",1)])
        db.executemany("INSERT INTO operational_event_times VALUES(?,?,?,?,?,?,?,?,?)",[("s1","child","inv-root","start","2026-09-09T07:00:00Z","host-wall",None,None,1),("e1","child","inv-root","terminal","2026-09-09T07:01:40Z","host-wall",None,None,1),("s2","child","inv-child","start","2026-09-09T07:00:20Z","host-wall",None,None,1),("e2","child","inv-child","terminal","2026-09-09T07:01:20Z","host-wall",None,None,1)])
        usage="INSERT INTO runtime_turn_usage VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)"
        db.execute(usage,("u1","child","inv-root","thread","turn",1,"provider-a","model-r","model-o","medium","high","codex","scope-a","native",100,40,20,None,120))
        db.execute(usage,("u2","child","inv-child","thread","turn",1,"provider-b","model-r","model-o","medium","high","codex","scope-b","native",200,50,30,10,230))
        db.execute("INSERT INTO ci_runs VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?)",("c1","child","FS-GG/.github",1,2,"build","push","head","completed","failure",None,None,None)); db.commit(); db.close()

    def test_grouped_item_keeps_separate_time_token_ci_budget_and_delivery_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); self.make_store(root)
            ci={"child":{"runs":1,"attempts":2,"jobs":2,"steps":3,"runnerSeconds":60,"wallSeconds":40,"queueSeconds":None,"usefulValidationSeconds":20,"administrativeSeconds":10,"necessarySetupSeconds":5,"mixedSeconds":0,"unclassifiedSeconds":5}}
            budgets={"child":{"dimensions":[{"epoch":"old","dimension":"model-usage","verdict":"breach","numerator":11,"denominator":100,"severe":False}]}}
            value=D.project_completed_items(str(root),{"epoch":"current"},labels(),ci,budgets); D.validate_completed_items(value)
            item=value["items"][0]
            self.assertEqual([r["summedSeconds"] for r in item["runtime"]["duration"]["rows"]],[100,60])
            self.assertEqual(len(item["deliveries"]),2); self.assertEqual(item["ci"]["seconds"]["runnerSeconds"]["totalItemSeconds"],60)
            self.assertEqual(item["budget"]["assessments"][0]["epoch"],"historical")
            self.assertNotIn("ORIGINAL",json.dumps(value)); self.assertNotIn("provider-a",json.dumps(value))

    def test_dirty_or_reopened_item_is_not_published(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); self.make_store(root)
            db=sqlite3.connect(root/"telemetry.sqlite3"); db.execute("INSERT INTO budget_dirty_items VALUES('child')"); db.commit(); db.close()
            value=D.project_completed_items(str(root),{"epoch":"current"},labels(),{},{}); self.assertEqual(value["items"],[]); self.assertEqual(value["coverage"]["dirty"],1)

    def test_mapping_conflict_and_private_nested_sentinel_refuse(self):
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/"labels.json"; value=labels(); value["items"]["OTHER"]=dict(value["items"]["ORIGINAL"])
            path.write_text(json.dumps(value)); path.chmod(0o600)
            with self.assertRaises(ValueError): D.load_labels(path)
            value=labels(); value["scopes"]["provider-c|scope-c"]="Scope A"; path.write_text(json.dumps(value))
            with self.assertRaises(ValueError): D.load_labels(path)

    def test_unsupported_store_schema_refuses_before_projection(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); db=sqlite3.connect(root/"telemetry.sqlite3"); db.execute("PRAGMA journal_mode=WAL"); db.execute("PRAGMA user_version=6"); db.close()
            with self.assertRaises(D.HostSourceError): D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {})

    def test_schema8_engine_detail_is_closed_namespaced_and_non_atomic(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); self.make_store(root)
            db=sqlite3.connect(root/"telemetry.sqlite3"); db.execute("PRAGMA user_version=8"); db.commit(); db.close()
            calls=[]
            value=D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {},lambda item:(calls.append(item) or item_detail(item)))
            D.validate_completed_items(value); process=value["items"][0]["process"]
            self.assertEqual(calls,["child"]); self.assertEqual(process["activities"]["summary"][0]["summedSeconds"],60)
            self.assertEqual(process["attribution"]["rows"][0]["activityCategory"],"repair")
            self.assertEqual(process["reviews"]["rows"][0]["revision"],2)
            self.assertEqual(process["complications"]["rows"][0]["cause"],"product-defect")
            self.assertEqual(process["attribution"]["crossRead"],"partial")
            public=json.dumps(value)
            for private in ("private-activity","private-invocation","private-attempt","private-usage","PRIVATE prose","/secret/"):
                self.assertNotIn(private,public)

    def test_detail_truncation_and_invalid_or_mismatched_detail_refuse_snapshot(self):
        with tempfile.TemporaryDirectory() as directory:
            root=pathlib.Path(directory); self.make_store(root)
            db=sqlite3.connect(root/"telemetry.sqlite3"); db.execute("PRAGMA user_version=8"); db.commit(); db.close()
            truncated=item_detail(); truncated.update(activityTruncated=True,attributionTruncated=True,complicationTruncated=True,reviewTruncated=True)
            value=D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {},lambda _:truncated)
            self.assertTrue(all(value["items"][0]["process"]["truncated"].values()))
            bad=item_detail(); bad["reviews"][0]["privateField"]="sentinel"
            with self.assertRaises(D.HostSourceError): D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {},lambda _:bad)
            mismatch=item_detail(); mismatch["accounting"]["direct"]=119
            with self.assertRaises(D.HostSourceError): D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {},lambda _:mismatch)
            impossible=item_detail(); impossible["attributionTruncated"]=True; impossible["accounting"]["direct"]=119
            with self.assertRaises(D.HostSourceError): D.project_completed_items(str(root),{"epoch":"current"},labels(),{}, {},lambda _:impossible)

if __name__=="__main__": unittest.main()

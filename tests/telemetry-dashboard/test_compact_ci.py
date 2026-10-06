"""Compact engine snapshots preserve metrics and fail closed on incomplete CI joins."""
import copy
import hashlib
import importlib.util
import json
import pathlib
import unittest
from unittest import mock

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("compact_dashboard",ROOT/"tools/telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)

SECONDS=("runnerSeconds","wallSeconds","queueSeconds","usefulValidationSeconds","administrativeSeconds","necessarySetupSeconds","mixedSeconds","unclassifiedSeconds")
COVERAGE=("inventoryCoverage","checkCoverage","attemptCoverage","jobPageCoverage","terminalCoverage","timestampCoverage","lineageCoverage","classificationCoverage","criticalPathCoverage")

def compact():
    summary={"schema":"fsgg.telemetry.ci-summary/1","item":"item","runs":1,"attempts":2,"jobs":3,"steps":36674,
             **{key:None for key in SECONDS},**{key:"unknown" for key in COVERAGE}}
    return {"workspaceId":"private-workspace","selection":{"mode":"all","complete":True},"items":["item"],
            "store":{"schemaVersion":13,"journalMode":"wal"},"ciRuns":[],"ciProjection":"fsgg.telemetry.ci-summary/1","ciSummaries":[summary]}

def envelope(value,schema="fsgg.telemetry.item-detail/3"):
    raw=json.dumps(value,sort_keys=True,separators=(",",":")).encode()
    return {"schema":schema,"workspaceId":"private-workspace","observedAt":"2026-10-06T00:00:00Z",
            "revision":hashlib.sha256(raw).hexdigest(),"canonicalSnapshotGzip":D.base64.b64encode(D.gzip.compress(raw)).decode(),
            "operational":{"pendingBatches":0,"consistency":"observed-outside-database-transaction"}}

class CompactCiTests(unittest.TestCase):
    def test_exact_large_counts_and_unknown_durations(self):
        value=compact()
        with mock.patch.object(D,"engine_json",return_value=envelope(value)) as engine:
            snapshot,_=D._read_host_snapshot("/private/store","engine")
        self.assertEqual(engine.call_args.args[1][3],"3")
        result=D.snapshot_ci(snapshot,"item")
        self.assertEqual(result["steps"],36674)
        for key in SECONDS: self.assertIsNone(result[key])

    def test_all_classification_and_overlap_metrics_reach_public_reducer_unchanged(self):
        value=compact()
        for index,key in enumerate(SECONDS): value["ciSummaries"][0][key]=index*15
        result=D.snapshot_ci(value,"item")
        for index,key in enumerate(SECONDS): self.assertEqual(result[key],index*15)

    def test_incomplete_duplicate_or_unbound_summary_refused(self):
        cases=[]
        missing=compact(); missing["ciSummaries"]=[]; cases.append(missing)
        duplicate=compact(); duplicate["ciSummaries"]*=2; cases.append(duplicate)
        wrong=compact(); wrong["ciSummaries"][0]["item"]="other"; cases.append(wrong)
        raw=compact(); raw["ciSteps"]=[]; cases.append(raw)
        malformed=compact(); malformed["items"]=[{}]; cases.append(malformed)
        for value in cases:
            with self.subTest(value=value),mock.patch.object(D,"engine_json",return_value=envelope(value)):
                with self.assertRaises(D.HostSourceError): D._read_host_snapshot("/private/store","engine")

    def test_workspace_mismatch_digest_corruption_and_future_schema_refused(self):
        for change in (lambda x:x.update(workspaceId="other"),lambda x:x.update(revision="0"*64),lambda x:x.update(schema="fsgg.telemetry.item-detail/4")):
            raw=envelope(compact()); change(raw)
            with mock.patch.object(D,"engine_json",return_value=raw):
                with self.assertRaises(D.HostSourceError): D._read_host_snapshot("/private/store","engine")

    def test_negative_or_boolean_counts_refused(self):
        for count in (-1,True):
            value=compact();value["ciSummaries"][0]["steps"]=count
            with self.assertRaises((D.HostSourceError,ValueError)): D.snapshot_ci(value,"item")

    def test_old_closed_contract_does_not_admit_compact_metadata(self):
        with mock.patch.object(D,"engine_json",return_value=envelope(compact(),"fsgg.telemetry.item-detail/2")):
            with self.assertRaises(D.HostSourceError): D._read_host_snapshot("/private/store","engine")

    def test_empty_job_unknown_is_explicit_in_completed_public_item(self):
        spec=importlib.util.spec_from_file_location("existing_item_fixture",ROOT/"tests/telemetry-dashboard/test_item_projection.py")
        fixture=importlib.util.module_from_spec(spec);spec.loader.exec_module(fixture)
        value=fixture.snapshot()
        ci=D.snapshot_ci(value,"child");self.assertEqual(ci["runnerSeconds"],0)
        native={"schema":"fsgg.telemetry.ci-summary/1","item":"child",**ci,"runnerSeconds":None}
        value["ciProjection"]="fsgg.telemetry.ci-summary/1";value["ciSummaries"]=[native]
        projected_ci=D.snapshot_ci(value,"child")
        result=D.project_completed_items(value,{"epoch":None},fixture.labels(),{"child":projected_ci},{"child":{"dimensions":[]}})
        self.assertEqual(result["coverage"]["published"],1)
        item=result["items"][0]
        self.assertEqual(item["ci"]["seconds"]["runnerSeconds"],{"knownItems":0,"unknownItems":1,"totalItemSeconds":0})
        self.assertEqual(item["complications"]["observed"]["failedOrCancelledCiRuns"],1)
        self.assertEqual(item["complications"]["observed"]["repeatedCiRuns"],1)

    def test_original_canonical_byte_bound_still_applies(self):
        value=compact(); value["oversized"]="x"*(D.MAX_CANONICAL_SNAPSHOT+1)
        with mock.patch.object(D,"engine_json",return_value=envelope(value)):
            with self.assertRaises(D.HostSourceError): D._read_host_snapshot("/private/store","engine")

if __name__=="__main__": unittest.main()

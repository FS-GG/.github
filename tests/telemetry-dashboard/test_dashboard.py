import importlib.util
import hashlib
import json
import pathlib
import tempfile
import textwrap
import unittest
from unittest import mock
from types import SimpleNamespace

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("telemetry_dashboard", ROOT / "tools/telemetry-dashboard.py")
D = importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)


def public_item(**changes):
    value={"schema":"fsgg.telemetry.public-summary/1","item":"PRIVATE-ITEM","factCount":1,"usageObservations":1,"deliveryObservations":1,
      "usage":{"input":100,"cachedInput":20,"cacheWriteInput":0,"output":40,"reasoning":10,"total":140},
      "launcherPopulation":{"admitted":1,"started":1,"terminal":1,"usage":1,"missingAdmission":0,"missingStart":0,"missingTerminal":0,"missingUsage":0},
      "recordValidity":"valid","joinIntegrity":"matched","populationCoverage":"complete","qualification":"not-evaluated"}
    value.update(changes); return value


def host_fixture():
    return D.aggregate_host({"schema":"fsgg.telemetry.public-export/1","items":[public_item()]},
      [{"runs":1,"attempts":1,"jobs":2,"steps":3,"runnerSeconds":20,"wallSeconds":10,"queueSeconds":None,"usefulValidationSeconds":8,"administrativeSeconds":2,"necessarySetupSeconds":1,"mixedSeconds":0,"unclassifiedSeconds":0,
        "inventoryCoverage":"complete","checkCoverage":"partial","attemptCoverage":"complete","jobPageCoverage":"complete","terminalCoverage":"complete","timestampCoverage":"complete","lineageCoverage":"complete","classificationCoverage":"partial","criticalPathCoverage":"unknown"}],
      [{"dimensions":[{"dimension":"model-usage","verdict":"pass","severe":False,"epoch":"e1"},{"dimension":"owner-effort","verdict":"breach","severe":True,"epoch":"old"}]}],
      {"epoch":"e1","distinctBreaches":14,"dirtyItems":1,"intervention":"open"},"2026-09-09T08:00:00Z",[],[],{"status":"ready","schemaVersion":7,"journalMode":"wal","pendingBatches":0})


def actions_fixture():
    return {"schema":"fsgg.telemetry.public-actions/1","observedAt":"2026-09-09T08:00:00Z","repository":"FS-GG/.github",
      "selection":{"order":"created-descending","cap":1000,"pagesFetched":1,"returned":1,"repositoryTotalAtObservation":40000,"truncated":True,"newestCreatedAt":"2026-09-09T08:00:00Z","oldestCreatedAt":"2026-09-09T08:00:00Z","semantics":"bounded multi-page sample, deduplicated by run id; latest observed attempt; not an atomic inventory"},
      "runs":[{"id":1,"workflow":"build","event":"push","status":"completed","conclusion":"success","createdAt":"2026-09-09T08:00:00Z","startedAt":"2026-09-09T08:00:01Z","updatedAt":"2026-09-09T08:00:03Z","durationSeconds":2,"attempt":1,"url":"https://github.com/FS-GG/.github/actions/runs/1"}]}


def deliveries_fixture():
    return {"schema":D.DELIVERIES_SCHEMA,"observedAt":"2026-09-09T08:01:00Z","repository":"FS-GG/.github","selection":{"order":"closed-updated-descending","cap":200,"pagesFetched":1,"closedScanned":1,"returned":1,"semantics":"merged pull requests found in a bounded updated-ordered closed-PR scan; public delivery evidence, not proof of a whole completed item or effort"},"deliveries":[{"number":7,"title":"Ship telemetry","url":"https://github.com/FS-GG/.github/pull/7","createdAt":"2026-09-09T07:00:00Z","mergedAt":"2026-09-09T08:00:00Z","elapsedSeconds":3600}]}


class DashboardTests(unittest.TestCase):
    def test_closed_host_aggregate_removes_private_identity_and_free_text(self):
        value=host_fixture(); raw=json.dumps(value)
        self.assertNotIn("PRIVATE-ITEM",raw); self.assertNotIn("secret free text",raw)
        self.assertEqual(value["budget"]["severeItems"],0) # severe dimension belonged to another epoch
        self.assertEqual(value["budget"]["intervention"],"open")
        D.validate_host(value)

    def test_unknown_is_distinct_from_zero_and_invalid_nested_fields_refuse(self):
        value=host_fixture(); self.assertEqual(value["localCi"]["seconds"]["queueSeconds"],{"knownItems":0,"unknownItems":1,"totalItemSeconds":0})
        value["usage"]["privatePrompt"]="sentinel"
        with self.assertRaises(ValueError): D.validate_host(value)

    def test_counts_enums_time_and_bytes_are_bounded(self):
        for mutation in [lambda a:a["runs"][0].update(durationSeconds=-1),lambda a:a["runs"][0].update(status="mystery"),lambda a:a.update(observedAt="not-time")]:
            value=actions_fixture(); mutation(value)
            with self.assertRaises(ValueError): D.validate_actions(value)
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/"large.json"; path.write_bytes(b"x"*(D.MAX_JSON+1))
            with self.assertRaises(ValueError): D.load(path)

    def test_budget_boundaries_are_renderer_inputs_not_frontend_reductions(self):
        value=host_fixture(); value["budget"]["distinctBreaches"]=15; value["budget"]["intervention"]="open"; value.pop("revision"); value["revision"]=hashlib.sha256(json.dumps(value,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest(); D.validate_host(value)
        value["budget"]["intervention"]="verified"; value.pop("revision"); value["revision"]=hashlib.sha256(json.dumps(value,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()).hexdigest(); D.validate_host(value)
        value["budget"]["intervention"]="required"
        with self.assertRaises(ValueError): D.validate_host(value)

    def test_known_ci_seconds_survive_unknown_items(self):
        public={"schema":"fsgg.telemetry.public-export/1","items":[public_item(),public_item(),public_item()]}
        base={"runs":0,"attempts":0,"jobs":0,"steps":0,"runnerSeconds":0,"wallSeconds":0,"queueSeconds":None,"usefulValidationSeconds":0,"administrativeSeconds":0,"necessarySetupSeconds":0,"mixedSeconds":0,"unclassifiedSeconds":0,"inventoryCoverage":"unknown","checkCoverage":"unknown","attemptCoverage":"unknown","jobPageCoverage":"unknown","terminalCoverage":"unknown","timestampCoverage":"unknown","lineageCoverage":"unknown","classificationCoverage":"unknown","criticalPathCoverage":"unknown"}
        ci=[]
        for queue in (10,None,20): row=dict(base); row["queueSeconds"]=queue; ci.append(row)
        value=D.aggregate_host(public,ci,[],{"epoch":"e1","distinctBreaches":0,"dirtyItems":0,"intervention":"none"},"2026-09-09T08:00:00Z",[],[],{"status":"ready","schemaVersion":7,"journalMode":"wal","pendingBatches":0})
        self.assertEqual(value["localCi"]["seconds"]["queueSeconds"],{"knownItems":2,"unknownItems":1,"totalItemSeconds":30})

    def test_compose_recursively_validates_and_binds_both_revisions(self):
        result=D.compose(actions_fixture(),deliveries_fixture(),host_fixture(),"a"*40,"b"*40)
        self.assertEqual(result["hostRevision"],"b"*40)
        bad=host_fixture(); bad["scope"]["rawItems"]=[]
        with self.assertRaises(ValueError): D.compose(actions_fixture(),deliveries_fixture(),bad,"a"*40,"b"*40)

    def test_concurrent_ref_conflict_never_forces_or_retries(self):
        calls=[]
        def api(*args,**kwargs):
            calls.append((args,kwargs))
            if len(calls)==6: raise D.RefConflict("race")
            return answers.pop(0)
        answers=[({"object":{"sha":"a"*40}},{}),({"tree":{"sha":"b"*40}},{}),({"sha":"c"*40},{}),({"sha":"d"*40},{}),({"sha":"e"*40},{})]
        with mock.patch.object(D,"github",side_effect=api), self.assertRaises(D.RefConflict): D.publish("FS-GG/.github","telemetry-data","host.json","token",host_fixture())
        self.assertEqual(len(calls),6); self.assertFalse(calls[-1][0][3]["force"])

    def test_collection_deduplicates_page_drift_and_keeps_timestamp_rules(self):
        raw={"id":9,"name":"build","event":"push","status":"completed","conclusion":"success","created_at":"2026-09-09T08:00:00Z","run_started_at":"2026-09-09T08:00:01Z","updated_at":"2026-09-09T08:00:03Z","run_attempt":2}
        pages=[({"total_count":200,"workflow_runs":[raw]*100},{}),({"total_count":200,"workflow_runs":[raw]}, {})]
        with mock.patch.object(D,"github",side_effect=pages): value=D.collect_actions("FS-GG/.github","token",200)
        self.assertEqual(value["selection"]["returned"],1); self.assertEqual(value["runs"][0]["attempt"],2); self.assertEqual(value["runs"][0]["durationSeconds"],2)

    def test_delivery_collection_keeps_unknown_reversed_elapsed(self):
        raw={"number":9,"title":"Public delivery","merged_at":"2026-09-09T07:00:00Z","created_at":"2026-09-09T08:00:00Z"}
        with mock.patch.object(D,"github",return_value=([raw],{})):
            value=D.collect_deliveries("FS-GG/.github","token",100)
        self.assertIsNone(value["deliveries"][0]["elapsedSeconds"]); D.validate_deliveries(value)

    def test_engine_detail_failure_and_oversized_output_use_fixed_diagnostics(self):
        with mock.patch.object(D.subprocess,"run",return_value=SimpleNamespace(returncode=1,stdout="private",stderr="private")):
            with self.assertRaisesRegex(D.HostSourceError,"HOST_ENGINE_PROJECTION_FAILED"): D.engine_json("engine",["telemetry","item-detail"])
        with mock.patch.object(D.subprocess,"run",return_value=SimpleNamespace(returncode=0,stdout="x"*(D.MAX_JSON+1),stderr="")):
            with self.assertRaisesRegex(D.HostSourceError,"HOST_ENGINE_OUTPUT_TOO_LARGE"): D.engine_json("engine",["telemetry","item-detail"])

    def test_base64_accepts_fully_sized_wrapped_payload_and_refuses_invalid_or_oversized_input(self):
        payload=b"x"*D.MAX_JSON
        wrapped="\n".join(textwrap.wrap(D.base64.b64encode(payload).decode(),60))
        self.assertEqual(D.bounded_base64(wrapped,D.MAX_JSON,"INVALID"),payload)
        with self.assertRaisesRegex(D.HostSourceError,"INVALID"):
            D.bounded_base64(wrapped+"!",D.MAX_JSON,"INVALID")
        oversized=D.base64.b64encode(payload+b"x").decode()
        with self.assertRaisesRegex(D.HostSourceError,"INVALID"):
            D.bounded_base64(oversized,D.MAX_JSON,"INVALID")
        transport_too_large=" \n".join(D.base64.b64encode(payload).decode())
        with self.assertRaisesRegex(D.HostSourceError,"INVALID"):
            D.bounded_base64(transport_too_large,D.MAX_JSON,"INVALID")

    def test_engine_canonical_bytes_bind_non_ascii_escaping_and_numeric_shape(self):
        relations=("populations","dirtyItems","outcomes","admissions","starts","terminals","expectedDispatches","lineage","times","usage","runtimeGaps","ciRuns","ciJobs","ciSteps","ciCoverage","ciPopulationCoverage","budgetAssessments","budgetMembership","budgetEpochs","budgetBreaches","budgetInterventions","activities","activityUsageAttributions","complications","reviews")
        snapshot={name:[] for name in relations}; snapshot.update({"selection":{"mode":"all","complete":True},"store":{"schemaVersion":8,"journalMode":"wal"},"items":[],"summaries":[],"encodingEdge":'café <tag> "quoted"',"numericEdge":1.0,"realSizePadding":"x"*600_000})
        canonical=json.dumps(snapshot,sort_keys=True,separators=(",",":"),ensure_ascii=False).encode()
        envelope={"schema":"fsgg.telemetry.item-detail/2","observedAt":"2026-09-09T08:00:00Z","revision":hashlib.sha256(canonical).hexdigest(),"canonicalSnapshotGzip":D.base64.b64encode(D.gzip.compress(canonical)).decode(),"operational":{"pendingBatches":0,"consistency":"observed-outside-database-transaction"}}
        self.assertLess(len(json.dumps(envelope).encode()),D.MAX_JSON)
        self.assertGreater(len(json.dumps({**envelope,"canonicalSnapshot":D.base64.b64encode(canonical).decode(),"snapshot":snapshot}).encode()),D.MAX_JSON)
        with mock.patch.object(D,"config",return_value=(pathlib.Path("/config"),{"storeRoot":"/store","engine":"engine"})),mock.patch.object(D,"engine_json",return_value=envelope):
            value=D.build_host()
        self.assertEqual(value["store"]["schemaVersion"],8); self.assertNotIn("encodingEdge",json.dumps(value))


if __name__ == "__main__": unittest.main()

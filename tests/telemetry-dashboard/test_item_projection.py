import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT=pathlib.Path(__file__).resolve().parents[2]
SPEC=importlib.util.spec_from_file_location("dashboard_items",ROOT/"tools/telemetry-dashboard.py")
D=importlib.util.module_from_spec(SPEC); SPEC.loader.exec_module(D)

def labels():
    return {"schema":D.LABELS_SCHEMA,"items":{"ORIGINAL":{"key":"public-item","label":"Public item","url":"https://github.com/FS-GG/.github/issues/1","repositories":["FS-GG/.github"],"notes":[{"kind":"complication","text":"Documented complication.","evidenceUrl":"https://github.com/FS-GG/.github/pull/7"}]}},"models":{"model-r":"Requested","model-o":"Observed"},"efforts":{"medium":"Medium","high":"High"},"scopes":{"provider-a|scope-a":"Scope A","provider-b|scope-b":"Scope B"}}

def snapshot():
    empty=("runtimeGaps","ciJobs","ciSteps","ciCoverage","ciPopulationCoverage","budgetMembership","budgetEpochs","budgetBreaches","budgetInterventions","activities","activityUsageAttributions","complications","reviews")
    value={name:[] for name in empty}
    value.update({"populations":[{"identity":"pop","item_id":"child","original_item_id":"ORIGINAL","state":"completed","source_ref":"derived:population","fact_revision":2}],"dirtyItems":[],"outcomes":[{"identity":"new","item_id":"child","repository":"FS-GG/.github","pr_number":7,"outcome":"delivered-after-readback","code_delivery":"delivered","occurred_at":"2026-09-09T08:00:00Z","observed_at":"2026-09-09T08:01:00Z","fact_revision":2},{"identity":"old","item_id":"child","repository":"FS-GG/.github","pr_number":6,"outcome":"delivered","code_delivery":"delivered","occurred_at":"2026-09-09T07:00:00Z","observed_at":"2026-09-09T07:01:00Z","fact_revision":1}],"admissions":[{"item_id":"child","invocation_id":"inv-root"},{"item_id":"child","invocation_id":"inv-child"}],"starts":[{"item_id":"child","invocation_id":"inv-root"},{"item_id":"child","invocation_id":"inv-child"}],"terminals":[{"item_id":"child","invocation_id":"inv-root","outcome":"completed"},{"item_id":"child","invocation_id":"inv-child","outcome":"failed"}],"expectedDispatches":[{"item_id":"child","dispatch_id":"dispatch-root","relation":"root","runtime":"codex-exec"},{"item_id":"child","dispatch_id":"dispatch-child","relation":"child","runtime":"codex-exec"}],"lineage":[{"item_id":"child","identity":"l1","dispatch_id":"dispatch-root","invocation_id":"inv-root","relation":"root","parent_invocation_id":None,"root_invocation_id":"inv-root","runtime":"codex-exec","fact_revision":1},{"item_id":"child","identity":"l2","dispatch_id":"dispatch-child","invocation_id":"inv-child","relation":"child","parent_invocation_id":"inv-root","root_invocation_id":"inv-root","runtime":"codex-exec","fact_revision":1}],"times":[{"item_id":"child","invocation_id":"inv-root","event":"start","occurred_at":"2026-09-09T07:00:00Z","occurred_clock_provenance":"host-wall"},{"item_id":"child","invocation_id":"inv-root","event":"terminal","occurred_at":"2026-09-09T07:01:40Z","occurred_clock_provenance":"host-wall"},{"item_id":"child","invocation_id":"inv-child","event":"start","occurred_at":"2026-09-09T07:00:20Z","occurred_clock_provenance":"host-wall"},{"item_id":"child","invocation_id":"inv-child","event":"terminal","occurred_at":"2026-09-09T07:01:20Z","occurred_clock_provenance":"host-wall"}],"usage":[{"identity":"u1","item_id":"child","invocation_id":"inv-root","provider":"provider-a","requested_model":"model-r","observed_model":"model-o","requested_effort":"medium","observed_effort":"high","accounting_scope":"scope-a","input_count":100,"cached_input":40,"output_count":20,"reasoning":None,"total":120},{"identity":"u2","item_id":"child","invocation_id":"inv-child","provider":"provider-b","requested_model":"model-r","observed_model":"model-o","requested_effort":"medium","observed_effort":"high","accounting_scope":"scope-b","input_count":200,"cached_input":50,"output_count":30,"reasoning":10,"total":230}],"ciRuns":[{"item_id":"child","repository":"FS-GG/.github","run_id":1,"attempt":2,"conclusion":"failure"}],"budgetAssessments":[{"item_id":"child","dimension":"model-usage","provider":"provider-a","accounting_scope":"scope-a","assessment_revision":1,"epoch_id":"old","verdict":"breach","numerator":11,"denominator":100,"severe":0,"reason":"fixture"}]})
    return value

class ItemProjectionTests(unittest.TestCase):
    def test_grouped_item_keeps_separate_time_token_ci_budget_and_delivery_evidence(self):
        source=snapshot(); ci={"child":D.snapshot_ci(source,"child")}; budgets={"child":D.snapshot_budget(source,"child")}
        value=D.project_completed_items(source,{"epoch":"current"},labels(),ci,budgets); D.validate_completed_items(value)
        item=value["items"][0]
        self.assertEqual([r["summedSeconds"] for r in item["runtime"]["duration"]["rows"]],[100,60]); self.assertEqual(len(item["deliveries"]),2)
        self.assertEqual(item["runtime"]["tokens"]["coverage"]["status"],"complete")
        self.assertEqual(item["runtime"]["tokens"]["total"]["status"],"not-proven")
        self.assertFalse(item["runtime"]["tokens"]["total"]["unknownRemainder"])
        self.assertEqual(item["budget"]["assessments"][0]["epoch"],"historical"); self.assertNotIn("ORIGINAL",json.dumps(value)); self.assertNotIn("provider-a",json.dumps(value))

    def test_complete_total_sums_root_child_and_follow_up_once_only_on_proven_population(self):
        source=snapshot(); source["usage"][1]["provider"]="provider-a"; source["usage"][1]["accounting_scope"]="scope-a"
        source["admissions"].append({"item_id":"child","invocation_id":"inv-follow"}); source["starts"].append({"item_id":"child","invocation_id":"inv-follow"}); source["terminals"].append({"item_id":"child","invocation_id":"inv-follow","outcome":"completed"})
        source["expectedDispatches"].append({"item_id":"child","dispatch_id":"dispatch-follow","relation":"follow-up","runtime":"codex-exec"})
        source["lineage"].append({"item_id":"child","identity":"l3","dispatch_id":"dispatch-follow","invocation_id":"inv-follow","relation":"follow-up","parent_invocation_id":"inv-root","root_invocation_id":"inv-root","runtime":"codex-exec","fact_revision":1})
        source["usage"].append({"identity":"u3","item_id":"child","invocation_id":"inv-follow","provider":"provider-a","requested_model":"model-r","observed_model":"model-o","requested_effort":"medium","observed_effort":"high","accounting_scope":"scope-a","input_count":40,"cached_input":10,"output_count":10,"reasoning":0,"total":50})
        value=D.project_completed_items(source,{"epoch":"current"},labels(),{},{}); tokens=value["items"][0]["runtime"]["tokens"]
        self.assertEqual(tokens["coverage"]["linkedInvocations"],3); self.assertEqual(tokens["total"]["status"],"complete"); self.assertEqual(tokens["total"]["total"],400); self.assertFalse(tokens["total"]["unknownRemainder"])
        source["runtimeGaps"]=[{"item_id":"child","invocation_id":"inv-child","code":"missing-usage"}]
        tokens=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})["items"][0]["runtime"]["tokens"]
        self.assertEqual(tokens["total"]["status"],"not-proven"); self.assertIsNone(tokens["total"]["total"]); self.assertTrue(tokens["total"]["unknownRemainder"])
        source["runtimeGaps"]=[]; source["lineage"][2]["invocation_id"]="inv-child"
        duplicate=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})
        self.assertEqual(duplicate["items"],[]); self.assertEqual(duplicate["coverage"]["incompatible"],1)

    def test_all_runtime_types_remain_in_usage_coverage_without_inferred_tokens(self):
        source=snapshot()
        source["expectedDispatches"][1]["runtime"]="collaboration-spawn-agent"
        source["lineage"][1]["runtime"]="collaboration-spawn-agent"
        source["usage"]=source["usage"][:1]
        tokens=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})["items"][0]["runtime"]["tokens"]
        expected={"status":"partial","expectedDispatches":2,"linkedInvocations":2,"invocationsWithUsage":1,"invocationsWithoutUsage":1}
        for key,value in expected.items(): self.assertEqual(tokens["coverage"][key],value)
        self.assertEqual(tokens["total"]["status"],"not-proven"); self.assertIsNone(tokens["total"]["total"])

        source["expectedDispatches"][0]["runtime"]="collaboration-spawn-agent"
        source["lineage"][0]["runtime"]="collaboration-spawn-agent"
        source["usage"]=[]
        tokens=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})["items"][0]["runtime"]["tokens"]
        self.assertEqual(tokens["coverage"]["status"],"unknown")
        self.assertEqual(tokens["coverage"]["expectedDispatches"],2)
        self.assertEqual(tokens["coverage"]["linkedInvocations"],2)
        self.assertEqual(tokens["coverage"]["invocationsWithUsage"],0)
        self.assertEqual(tokens["coverage"]["invocationsWithoutUsage"],2)
        self.assertIsNone(tokens["total"]["input"]); self.assertIsNone(tokens["total"]["total"])

    def test_runtime_and_lineage_ambiguity_never_proves_a_total(self):
        source=snapshot()
        source["lineage"][1]["runtime"]="collaboration-spawn-agent"
        tokens=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})["items"][0]["runtime"]["tokens"]
        self.assertEqual(tokens["coverage"]["status"],"partial")
        self.assertIsNone(tokens["total"]["total"])
        source=snapshot(); source["expectedDispatches"].append(dict(source["expectedDispatches"][0]))
        value=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})
        self.assertEqual(value["items"],[]); self.assertEqual(value["coverage"]["incompatible"],1)

    def test_current_coverage_contract_rejects_false_complete_and_accepts_legacy_boundary(self):
        source=snapshot(); source["usage"]=source["usage"][:1]
        value=D.project_completed_items(source,{"epoch":"current"},labels(),{}, {})
        item=value["items"][0]; D.validate_completed_items(value)
        item["runtime"]["tokens"]["coverage"]["status"]="complete"
        with self.assertRaises(ValueError): D.validate_completed_items(value)

        value=D.project_completed_items(snapshot(),{"epoch":"current"},labels(),{}, {})
        tokens=value["items"][0]["runtime"]["tokens"]
        tokens["coverage"]["boundary"]="canonical completed member items and their codex-exec expected dispatches"
        tokens["coverage"]["status"]="incomplete"
        tokens["total"]["semantics"]="complete only when the exact expected native invocation population is linked, admitted, started, terminal, gap-free, usage-covered, and has one compatible accounting basis"
        tokens["total"]["status"]="not-proven"
        for name in ("input","cachedInput","output","reasoning","total"): tokens["total"][name]=None
        tokens["total"]["unknownRemainder"]=True
        D.validate_completed_items(value)

    def test_dirty_or_reopened_item_is_not_published(self):
        source=snapshot(); source["dirtyItems"]=[{"item_id":"child"}]
        value=D.project_completed_items(source,{"epoch":"current"},labels(),{},{}); self.assertEqual(value["items"],[]); self.assertEqual(value["coverage"]["dirty"],1)

    def test_process_detail_is_filtered_from_the_same_snapshot(self):
        source=snapshot(); source["activities"]=[{"item_id":"child","activity_id":"a1","category":"implementation","started_at":"2026-09-09T07:00:00Z","ended_at":"2026-09-09T07:01:00Z","summary":"PRIVATE-SENTINEL"}]
        source["activityUsageAttributions"]=[{"item_id":"child","usage_identity":"u1","activity_id":"a1","classification":"direct","input_count":100,"cached_input":40,"output_count":20,"reasoning":None,"total":120}]
        source["complications"]=[{"item_id":"child","activity_id":"a1","trigger":"test-failure","cause":"product-defect","occurred_at":"2026-09-09T07:00:30Z","synopsis":"PRIVATE-SENTINEL"}]
        source["reviews"]=[{"item_id":"child","scope":"item","fact_revision":1,"went_well":"[\"PRIVATE-SENTINEL\"]","problems":"[]","avoidable_delay_rework":"[]","process_observations":"[]","remaining_risks":"[]","concrete_improvements":"[]","evidence_coverage":"partial","population_coverage":"complete","confidence":"high","reviewer_model":"model-o","reviewer_effort":"high","reviewed_at":"2026-09-09T07:02:00Z","duration_seconds":10}]
        ci={"child":D.snapshot_ci(source,"child")}; budgets={"child":D.snapshot_budget(source,"child")}; value=D.project_completed_items(source,{"epoch":"current"},labels(),ci,budgets)
        D.validate_completed_items(value); process=value["items"][0]["process"]
        self.assertEqual(process["attribution"]["crossRead"],"matched"); self.assertEqual(process["activities"]["summary"][0]["summedSeconds"],60); self.assertNotIn("PRIVATE-SENTINEL",json.dumps(value))

    def test_mapping_conflict_private_sentinel_and_exact_mode_refuse(self):
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/"labels.json"; value=labels(); value["items"]["OTHER"]=dict(value["items"]["ORIGINAL"]); path.write_text(json.dumps(value)); path.chmod(0o600)
            with self.assertRaises(ValueError): D.load_labels(path)
            value=labels(); value["scopes"]["provider-c|scope-c"]="Scope A"; path.write_text(json.dumps(value))
            with self.assertRaises(ValueError): D.load_labels(path)
            path.chmod(0o400)
            with self.assertRaises(D.HostSourceError): D.load_labels(path)

    def test_malformed_or_unbounded_engine_relations_refuse(self):
        with self.assertRaises(D.HostSourceError): D.snapshot_rows({"usage":"not-a-list"},"usage")
        with self.assertRaises(D.HostSourceError): D.snapshot_rows({"usage":[{}]*10001},"usage")

if __name__=="__main__": unittest.main()

import copy
import importlib.util
import json
import pathlib
import tempfile
import unittest

from test_item_projection import labels, snapshot


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("dashboard_pipeline", ROOT / "tools/telemetry-dashboard.py")
D = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(D)


def approved():
    value = labels()
    value["schema"] = D.MEMBER_LABELS_SCHEMA
    value["members"] = {
        "root-private": {"key": "root", "label": "Approved parent", "url": "https://github.com/FS-GG/.github/issues/1"},
        "child": {"key": "child", "label": "Approved subitem", "url": "https://github.com/FS-GG/.github/issues/2"},
    }
    return value


def two_members():
    value = snapshot()
    value["usage"][1]["provider"] = "provider-a"
    value["usage"][1]["accounting_scope"] = "scope-a"
    value["activities"] = [{"item_id": "child", "activity_id": "activity-private", "category": "implementation",
                            "started_at": "2026-09-09T07:00:00Z", "ended_at": "2026-09-09T07:02:00Z", "summary": "PRIVATE-SENTINEL"}]
    value["activityUsageAttributions"] = [
        {"item_id": "child", "usage_identity": row["identity"], "activity_id": "activity-private", "classification": "direct",
         "input_count": row["input_count"], "cached_input": row["cached_input"], "output_count": row["output_count"],
         "reasoning": row["reasoning"], "total": row["total"]} for row in value["usage"]]
    population = copy.deepcopy(value["populations"][0]); population.update(item_id="root-private", identity="root-pop")
    outcome = copy.deepcopy(value["outcomes"][0]); outcome.update(item_id="root-private", identity="root-outcome")
    value["populations"].append(population); value["outcomes"].append(outcome)
    for relation in ("admissions", "starts", "terminals", "expectedDispatches", "lineage", "times", "usage"):
        row = copy.deepcopy(value[relation][0]); row["item_id"] = "root-private"
        if "invocation_id" in row: row["invocation_id"] = "root-invocation-private"
        if "dispatch_id" in row: row["dispatch_id"] = "root-dispatch-private"
        if relation == "lineage": row.update(identity="root-lineage", root_invocation_id="root-invocation-private")
        if relation == "usage": row.update(identity="root-usage", input_count=80, cached_input=20, output_count=20, total=100)
        if relation == "times": row["occurred_at"] = "2026-09-09T06:00:00Z"
        value[relation].append(row)
    terminal_time = copy.deepcopy(value["times"][1]); terminal_time.update(item_id="root-private", invocation_id="root-invocation-private", occurred_at="2026-09-09T06:01:00Z")
    value["times"].append(terminal_time)
    value["activities"].append({"item_id": "root-private", "activity_id": "root-activity", "category": "planning",
                                "started_at": "2026-09-09T06:00:00Z", "ended_at": "2026-09-09T06:01:00Z", "summary": "PRIVATE-SENTINEL"})
    value["activityUsageAttributions"].append({"item_id": "root-private", "usage_identity": "root-usage", "activity_id": "root-activity",
                                                 "classification": "direct", "input_count": 80, "cached_input": 20,
                                                 "output_count": 20, "reasoning": None, "total": 100})
    return value


class ItemPipelineTests(unittest.TestCase):
    def test_approved_members_are_ordered_with_exact_time_and_native_tokens(self):
        private = two_members()
        projected = D.project_completed_items(private, {"epoch": None}, approved(), {}, {})
        D.validate_completed_items(projected)
        pipeline = projected["items"][0]["pipeline"]
        self.assertEqual(pipeline["coverage"], {"eligible": 2, "published": 2, "unmapped": 0})
        self.assertEqual([row["key"] for row in pipeline["nodes"]], ["root", "child"])
        self.assertEqual([row["stage"] for row in pipeline["nodes"]], ["planning", "implementation"])
        self.assertEqual([row["time"] for row in pipeline["nodes"]],
                         [{"status": "known", "seconds": 60, "basis": "same-clock-invocation-union", "open": 0, "missing": 0, "overlap": "no"},
                          {"status": "known", "seconds": 100, "basis": "same-clock-invocation-union", "open": 0, "missing": 0, "overlap": "yes"}])
        self.assertEqual([row["tokens"]["total"] for row in pipeline["nodes"]], [100, 350])
        self.assertEqual([row["tokens"]["attribution"] for row in pipeline["nodes"]], ["direct", "direct"])
        self.assertNotIn("PRIVATE-SENTINEL", json.dumps(projected))
        self.assertNotIn("root-private", json.dumps(projected))
        self.assertNotIn("activity-private", json.dumps(projected))

    def test_missing_alias_partial_usage_and_mixed_scopes_remain_explicit(self):
        private = two_members(); aliases = approved(); aliases["members"].pop("root-private")
        private["usage"] = [row for row in private["usage"] if row.get("identity") != "u2"]
        pipeline = D.project_item_pipeline(private, ["root-private", "child"], aliases)
        self.assertEqual(pipeline["coverage"], {"eligible": 2, "published": 1, "unmapped": 1})
        self.assertEqual(pipeline["nodes"][0]["tokens"], {"status": "partial", "total": 120, "attribution": "direct"})
        private = two_members(); private["usage"][1]["accounting_scope"] = "scope-b"
        unknown = D.project_item_pipeline(private, ["child"], approved())["nodes"][0]
        self.assertEqual((unknown["tokens"]["status"], unknown["tokens"]["total"]), ("unknown", None))

    def test_classified_ci_steps_do_not_classify_entire_member(self):
        private = two_members()
        private["ciCoverage"] = [{"item_id": "child", "classification": "complete"}]
        private["ciSteps"] = [{"item_id": "child", "classification": "admin"}]
        node = D.project_item_pipeline(private, ["child"], approved())["nodes"][0]
        self.assertEqual(node["workClass"], "unknown")

    def test_open_missing_and_incompatible_clock_intervals_are_explicit(self):
        private = two_members()
        private["times"] = [row for row in private["times"] if not (row["item_id"] == "child" and row["event"] == "terminal" and row["invocation_id"] == "inv-child")]
        observed = D.project_item_pipeline(private, ["child"], approved())["nodes"][0]["time"]
        self.assertEqual(observed, {"status": "partial", "seconds": 100, "basis": "same-clock-invocation-union", "open": 1, "missing": 1, "overlap": "no"})
        private = two_members()
        private["times"] = [row for row in private["times"] if not (row["item_id"] == "child" and row["event"] == "start" and row["invocation_id"] == "inv-child")]
        observed = D.project_item_pipeline(private, ["child"], approved())["nodes"][0]["time"]
        self.assertEqual((observed["status"], observed["open"], observed["missing"]), ("partial", 0, 1))
        private = two_members()
        for row in private["times"]:
            if row["item_id"] == "child" and row["invocation_id"] == "inv-child": row["occurred_clock_provenance"] = "provider-native"
        observed = D.project_item_pipeline(private, ["child"], approved())["nodes"][0]["time"]
        self.assertEqual((observed["status"], observed["seconds"], observed["overlap"]), ("unknown", None, "unknown"))

    def test_v1_labels_keep_pipeline_absent_and_v2_aliases_are_closed(self):
        old = D.project_completed_items(two_members(), {"epoch": None}, labels(), {}, {})
        self.assertNotIn("pipeline", old["items"][0])
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "labels.json"; value = approved()
            path.write_text(json.dumps(value)); path.chmod(0o600)
            self.assertEqual(D.load_labels(path)["members"]["child"]["key"], "child")
            value["members"]["child"]["url"] = "https://example.com/private"
            path.write_text(json.dumps(value))
            with self.assertRaises(ValueError): D.load_labels(path)

    def test_validator_rejects_private_fields_and_false_coverage(self):
        pipeline = D.project_item_pipeline(two_members(), ["root-private", "child"], approved())
        pipeline["nodes"][0]["privateItemId"] = "root-private"
        with self.assertRaises(ValueError): D.validate_item_pipeline(pipeline)
        pipeline["nodes"][0].pop("privateItemId")
        pipeline["coverage"]["eligible"] = 3
        with self.assertRaises(ValueError): D.validate_item_pipeline(pipeline)
        pipeline["coverage"]["eligible"] = 2
        pipeline["nodes"][0]["time"]["basis"] = "human-effort"
        with self.assertRaises(ValueError): D.validate_item_pipeline(pipeline)


if __name__ == "__main__":
    unittest.main()

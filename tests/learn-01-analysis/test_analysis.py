import copy
import importlib.util
import json
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("learn01", ROOT / "tools/learn-01-analysis.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
CONTRACT = json.loads((ROOT / "policy/learn-01-current-focused-v1.json").read_text())
CORPUS = json.loads((ROOT / "tests/learn-01-analysis/fixtures/synthetic.json").read_text())


class Learn01ContractTests(unittest.TestCase):
    def test_frozen_contract_and_fixture_are_reviewable(self):
        MODULE.validate_contract(CONTRACT)
        result = MODULE.validate_corpus(CONTRACT, CORPUS)
        self.assertEqual(6, result["originalItems"])
        self.assertEqual(1, result["childItems"])
        self.assertEqual({"accepted": 3, "cancelled": 1, "failed": 1, "open": 1}, result["outcomes"])
        self.assertEqual(3, result["fixed14DayCompletions"])
        self.assertEqual(3, result["repairMatureAcceptedItems"])
        self.assertEqual(1, result["materialRepairsInMatureFollowup"])
        self.assertEqual(["I-004"], result["openCensoredOriginalItems"])
        self.assertEqual({"current": 3, "focused": 2}, result["completeTokenItemsByArm"])
        self.assertEqual({"current": 2700, "focused": 1800}, result["providerTotalTokensByArm"])
        self.assertEqual(["I-004"], result["incompleteTokenOriginalItems"])
        self.assertIn("nonterminal-invocation:inv-root-004", result["incompleteTokenReasons"]["I-004"])
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertEqual("synthetic-fixture-only-no-efficiency-result", result["claim"])

    def test_outcome_derived_baseline_label_is_rejected(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["items"][1]["assignmentSource"] = "successful-outcome-reclassified-as-current"
        with self.assertRaisesRegex(MODULE.Refusal, "outcome-derived"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_analysis_unit_cannot_be_invocation(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["analysisUnit"] = "invocation"
        with self.assertRaisesRegex(MODULE.Refusal, "independent successes"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_actual_child_is_grouped_under_canonical_original(self):
        result = MODULE.validate_corpus(CONTRACT, CORPUS)
        self.assertEqual(6, result["originalItems"])
        self.assertEqual(1, result["childItems"])
        self.assertEqual(1200, result["providerTotalTokensByOriginalItem"]["I-005"])
        self.assertEqual(2700, result["providerTotalTokensByArm"]["current"])

    def test_child_cannot_mint_a_new_original_success(self):
        corpus = copy.deepcopy(CORPUS)
        child = copy.deepcopy(corpus["items"][0])
        child.update({"itemId": "I-child", "originalItemId": "I-child", "parentItemId": "I-001"})
        corpus["items"].append(child)
        with self.assertRaisesRegex(MODULE.Refusal, "child lineage"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_missing_expected_root_usage_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "usage-root-001"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-001", result["incompleteTokenOriginalItems"])
        self.assertIn("missing-expected-usage:inv-root-001/turn-root-001", result["incompleteTokenReasons"]["I-001"])

    def test_missing_expected_child_usage_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "usage-rescue-005"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-005", result["incompleteTokenOriginalItems"])
        self.assertIn("missing-expected-usage:inv-rescue-005/turn-rescue-005", result["incompleteTokenReasons"]["I-005"])

    def test_missing_expected_shared_cost_stays_incomplete(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"] = [cost for cost in corpus["costs"] if cost["costId"] != "shared-plan-001-002"]
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertIn("I-001", result["incompleteTokenOriginalItems"])
        self.assertIn("I-002", result["incompleteTokenOriginalItems"])

    def test_duplicate_native_turn_usage_is_rejected(self):
        corpus = copy.deepcopy(CORPUS)
        duplicate = copy.deepcopy(corpus["costs"][0])
        duplicate["costId"] = "usage-root-001-copy"
        corpus["costs"].append(duplicate)
        with self.assertRaisesRegex(MODULE.Refusal, "duplicate usage"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_unbounded_missing_tokens_cannot_be_zero(self):
        corpus = copy.deepcopy(CORPUS)
        missing = next(cost for cost in corpus["costs"] if cost["costId"] == "usage-root-004")
        missing["completeness"] = "imputed-zero"
        missing["providerTotalTokens"] = 0
        with self.assertRaisesRegex(MODULE.Refusal, "cannot be zero"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_legal_zero_total_is_complete_for_arithmetic_primary(self):
        corpus = copy.deepcopy(CORPUS)
        cost = next(cost for cost in corpus["costs"] if cost["costId"] == "usage-root-002")
        cost["providerTotalTokens"] = 0
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertEqual(1000, result["providerTotalTokensByArm"]["focused"])
        self.assertNotIn("I-002", result["incompleteTokenOriginalItems"])

    def test_cache_and_reasoning_cannot_change_provider_total(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"][0]["cacheTokens"] = 900
        corpus["costs"][0]["reasoningTokens"] = 300
        result = MODULE.validate_corpus(CONTRACT, corpus)
        self.assertEqual(2700, result["providerTotalTokensByArm"]["current"])

    def test_shared_cost_is_allocated_once(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["costs"][-1]["allocations"][1]["fraction"] = 1.0
        with self.assertRaisesRegex(MODULE.Refusal, "fractions must sum to one"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_delayed_repair_must_follow_acceptance(self):
        corpus = copy.deepcopy(CORPUS)
        item = next(item for item in corpus["items"] if item["itemId"] == "I-006")
        item["materialRepairAt"] = "2026-01-01T12:00:00Z"
        with self.assertRaisesRegex(MODULE.Refusal, "repair does not follow acceptance"):
            MODULE.validate_corpus(CONTRACT, corpus)


if __name__ == "__main__":
    unittest.main()

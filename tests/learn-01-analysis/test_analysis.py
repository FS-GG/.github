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
        self.assertEqual({"accepted": 3, "cancelled": 1, "failed": 1, "open": 1}, result["outcomes"])
        self.assertEqual({"current": 3, "focused": 2}, result["completeTokenItemsByArm"])
        self.assertEqual({"current": 2700, "focused": 1800}, result["providerTotalTokensByArm"])
        self.assertEqual(["I-004"], result["incompleteTokenOriginalItems"])
        self.assertFalse(result["tokenComparisonQualified"])
        self.assertEqual("synthetic-fixture-only-no-efficiency-result", result["claim"])

    def test_outcome_derived_baseline_label_is_rejected(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["issues"][1]["labelSource"] = "successful-outcome-reclassified-as-current"
        with self.assertRaisesRegex(MODULE.Refusal, "outcome-derived baseline"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_subissues_cannot_be_independent_successes(self):
        corpus = copy.deepcopy(CORPUS)
        corpus["analysisUnit"] = "invocation"
        with self.assertRaisesRegex(MODULE.Refusal, "independent successes"):
            MODULE.validate_corpus(CONTRACT, corpus)

    def test_unbounded_missing_tokens_cannot_be_zero(self):
        corpus = copy.deepcopy(CORPUS)
        missing = next(cost for cost in corpus["costs"] if cost["costId"] == "root-004")
        missing["completeness"] = "imputed-zero"
        missing["providerTotalTokens"] = 0
        with self.assertRaisesRegex(MODULE.Refusal, "cannot be zero"):
            MODULE.validate_corpus(CONTRACT, corpus)

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


if __name__ == "__main__":
    unittest.main()

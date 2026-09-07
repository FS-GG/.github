#!/usr/bin/env python3
"""Exercise conservative cohort accounting and insufficient-evidence verdicts."""

import copy
import importlib.util
from datetime import datetime, timedelta, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("routine_cohort", ROOT / "tools/routine-cohort.py")
module = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(module)


def at(days: int) -> str:
    return (datetime(2026, 1, 1, tzinfo=timezone.utc) + timedelta(days=days)).isoformat().replace("+00:00", "Z")


def unit(index: int, arm: str, productive: int, overhead: int) -> dict:
    return {
        "id": f"{arm}-{index}", "arm": arm, "kind": "code", "eligible": True,
        "selectedBeforeOutcome": arm == "routine", "delivery": "delivered", "deliveredAt": at(0),
        "usage": {"status": "measured", "productiveTokens": productive, "overheadTokens": overhead,
                  "unclassifiedTokens": 0, "headlineTokens": productive + overhead},
        "repairFollowup": {"status": "complete", "through": at(31), "processAttributableRollbacks": 0},
    }


document = {
    "schema": "fsgg.routine-cohort-observation/1", "cohort": "fixture", "asOf": at(31),
    "targetPerArm": 10, "providerPopulation": {"status": "measured", "totalHeadlineTokens": 3000},
    "units": [unit(i, "routine", 90, 10) for i in range(10)] + [unit(i, "baseline", 150, 50) for i in range(10)],
}
result = module.evaluate(document)
assert result["status"] == "sufficient", result
assert result["arms"]["routine"]["conservativeOverheadRatio"] == 0.1
assert result["arms"]["routine"]["costPerDeliveredUnit"] == 100
assert result["arms"]["baseline"]["costPerDeliveredUnit"] == 200

missing = copy.deepcopy(document)
missing["units"][0]["usage"] = {"status": "missing", "upperBoundTokens": None}
missing["providerPopulation"] = {"status": "unknown"}
result = module.evaluate(missing)
assert result["status"] == "insufficient"
assert "routine-usage-unbounded" in result["reasons"]
assert "usage-coverage-unknown" in result["reasons"]
assert result["arms"]["routine"]["costPerDeliveredUnit"] is None

missing_baseline = copy.deepcopy(document)
for unit_value in missing_baseline["units"]:
    if unit_value["arm"] == "baseline":
        unit_value["usage"] = {"status": "missing", "upperBoundTokens": None}
result = module.evaluate(missing_baseline)
assert result["status"] == "insufficient"
assert "baseline-usage-unbounded" in result["reasons"]
assert result["arms"]["baseline"]["costPerDeliveredUnit"] is None

over_budget = copy.deepcopy(document)
over_budget["units"][0]["usage"] = {"status": "measured", "productiveTokens": 70,
                                     "overheadTokens": 30, "unclassifiedTokens": 0, "headlineTokens": 100}
result = module.evaluate(over_budget)
assert result["status"] == "insufficient"
assert result["arms"]["routine"]["overBudgetUnits"] == ["routine-0"]

pending = copy.deepcopy(document)
pending["units"] = []
pending["providerPopulation"] = {"status": "unknown"}
result = module.evaluate(pending)
assert result["status"] == "insufficient"
assert "routine-cohort-short" in result["reasons"] and "baseline-cohort-short" in result["reasons"]

duplicate = copy.deepcopy(document)
duplicate["units"].append(copy.deepcopy(duplicate["units"][0]))
try:
    module.evaluate(duplicate)
except module.InvalidCohort as error:
    assert "duplicate unit id" in str(error)
else:
    raise AssertionError("duplicate cohort unit was accepted")

print("routine-cohort: fixed denominators, conservative gaps, per-unit ceiling, and 30-day follow-up pass")

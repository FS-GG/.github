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

historical_pending = copy.deepcopy(document)
historical_pending["units"][0]["repairFollowup"]["through"] = at(29)
result = module.evaluate(historical_pending)
assert "routine-30-day-followup-pending" in result["reasons"]

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


def item_unit(index: int, *, original: str | None = None, selected_day: int = 0,
              completed_day: int | None = None, delivery: str = "delivered") -> dict:
    value = unit(index, "routine", 90, 10)
    value.update({
        "id": f"attempt-{index}", "lane": "BARC-01",
        "originalItem": original or f"BARC-01.{index}", "selectedAt": at(selected_day),
        "delivery": delivery,
        "repairFollowup": {"status": "complete", "through": at(10),
                           "processAttributableRollbacks": 0, "severeEvents": []},
    })
    value.pop("deliveredAt", None)
    if completed_day is not None:
        value["completedAt"] = at(completed_day)
        value["finalCompletion"] = {
            "owningAcceptance": "Done", "nativeDoneReadback": True, "sourceDelivered": True,
        }
    return value


item_document = {
    "schema": "fsgg.routine-cohort-observation/2", "cohort": "completed-item-fixture", "asOf": at(20),
    "targetPerArm": 10, "providerPopulation": {"status": "measured", "totalHeadlineTokens": 3000},
    "repairObservation": {
        "mode": "completed-items", "declaredAt": at(0), "completedItemTarget": 10,
        "enrolledLanes": [
            {"id": lane, "statusAtDeclaration": "unresolved", "priorUsage": "missing"}
            for lane in ("BARC-01", "SC2C-01", "LEARN-01")
        ],
    },
    "units": [item_unit(i, completed_day=i + 1) for i in range(10)]
             + [unit(i, "baseline", 150, 50) for i in range(10)],
}
result = module.evaluate(item_document)
assert result["status"] == "sufficient", result
assert result["repairObservation"]["completedDistinctItems"] == 10
assert result["repairObservation"]["cutoff"] == at(10)
assert "routine-30-day-followup-pending" not in result["reasons"]

unknown_coverage = copy.deepcopy(item_document)
unknown_coverage["providerPopulation"] = {"status": "unknown"}
result = module.evaluate(unknown_coverage)
assert result["repairObservation"]["completedDistinctItems"] == 10
assert result["status"] == "insufficient"
assert "usage-coverage-unknown" in result["reasons"]
assert result["deliveryAuthority"] == "none-observer-only"

nine = copy.deepcopy(item_document)
nine["units"][9].pop("completedAt")
nine["units"][9].pop("finalCompletion")
nine["units"][9]["delivery"] = "cancelled"
result = module.evaluate(nine)
assert "routine-completed-item-window-pending" in result["reasons"]
assert result["repairObservation"]["completedDistinctItems"] == 9
assert result["arms"]["routine"]["selected"] == 10
assert result["arms"]["routine"]["delivered"] == 9

duplicate_lineage = copy.deepcopy(item_document)
duplicate_lineage["units"][9]["originalItem"] = duplicate_lineage["units"][8]["originalItem"]
result = module.evaluate(duplicate_lineage)
assert result["repairObservation"]["completedDistinctItems"] == 9
assert "routine-completed-item-window-pending" in result["reasons"]

pre_enrollment = copy.deepcopy(item_document)
pre_enrollment["units"][9]["selectedAt"] = at(-2)
result = module.evaluate(pre_enrollment)
assert result["repairObservation"]["completedDistinctItems"] == 9
pre_enrollment["units"][9]["originalItem"] = "BARC-01"
result = module.evaluate(pre_enrollment)
assert result["repairObservation"]["completedDistinctItems"] == 10

unfinished = copy.deepcopy(item_document)
unfinished["units"][9]["finalCompletion"]["nativeDoneReadback"] = False
result = module.evaluate(unfinished)
assert result["repairObservation"]["completedDistinctItems"] == 9

cutoff = copy.deepcopy(item_document)
cutoff["units"][0]["repairFollowup"]["through"] = at(9)
cutoff["units"].insert(10, item_unit(10, selected_day=11, completed_day=11))
result = module.evaluate(cutoff)
assert "routine-completed-item-repair-accounting-pending" in result["reasons"]
assert result["arms"]["routine"]["selected"] == 10

severe = copy.deepcopy(item_document)
severe["units"][0]["repairFollowup"]["severeEvents"] = [
    {"kind": "authority-breach", "observedAt": at(5)}
]
result = module.evaluate(severe)
assert "candidate-admission-stopped-severe-event" in result["reasons"]

rollbacks = copy.deepcopy(item_document)
rollbacks["units"][0]["repairFollowup"]["processAttributableRollbacks"] = 2
result = module.evaluate(rollbacks)
assert "candidate-expansion-stopped-two-process-rollbacks" in result["reasons"]

print("routine-cohort: completed-item policy covers 9/10, lineage, enrollment, completion, cutoff, and stops")

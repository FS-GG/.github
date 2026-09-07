#!/usr/bin/env python3
"""Evaluate a predeclared routine/baseline cohort without authorizing delivery."""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path
from typing import Any


INPUT_SCHEMA = "fsgg.routine-cohort-observation/1"
OUTPUT_SCHEMA = "fsgg.routine-cohort-result/1"


class InvalidCohort(ValueError):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise InvalidCohort(message)


def nonnegative(value: Any, name: str) -> int:
    require(type(value) is int and value >= 0, f"{name} must be a non-negative integer")
    return value


def instant(value: Any, name: str) -> datetime:
    require(isinstance(value, str) and value.endswith("Z"), f"{name} must be canonical UTC")
    try:
        return datetime.fromisoformat(value.removesuffix("Z") + "+00:00")
    except ValueError as error:
        raise InvalidCohort(f"{name} must be an ISO-8601 instant") from error


def measured_usage(unit: dict[str, Any]) -> tuple[int, int, int] | None:
    usage = unit.get("usage")
    require(isinstance(usage, dict), f"{unit['id']}.usage must be an object")
    status = usage.get("status")
    require(status in ("measured", "missing"), f"{unit['id']}.usage.status is unsupported")
    if status == "missing":
        bound = usage.get("upperBoundTokens")
        if bound is not None:
            nonnegative(bound, f"{unit['id']}.usage.upperBoundTokens")
        return None
    productive = nonnegative(usage.get("productiveTokens"), f"{unit['id']}.usage.productiveTokens")
    overhead = nonnegative(usage.get("overheadTokens"), f"{unit['id']}.usage.overheadTokens")
    unclassified = nonnegative(usage.get("unclassifiedTokens"), f"{unit['id']}.usage.unclassifiedTokens")
    headline = nonnegative(usage.get("headlineTokens"), f"{unit['id']}.usage.headlineTokens")
    require(productive + overhead + unclassified == headline, f"{unit['id']}.usage does not reconcile")
    return productive, overhead + unclassified, headline


def arm_result(arm: str, units: list[dict[str, Any]], target: int, as_of: datetime) -> dict[str, Any]:
    selected = [unit for unit in units if unit["arm"] == arm]
    delivered = [unit for unit in selected if unit["delivery"] == "delivered"]
    measured: list[tuple[dict[str, Any], tuple[int, int, int]]] = []
    bounded_missing = 0
    unbounded_missing: list[str] = []
    over_budget: list[str] = []
    followup_pending: list[str] = []
    for unit in selected:
        usage = measured_usage(unit)
        if usage is None:
            bound = unit["usage"].get("upperBoundTokens")
            if bound is None:
                unbounded_missing.append(unit["id"])
            else:
                bounded_missing += bound
        else:
            measured.append((unit, usage))
            productive, overhead, total = usage
            if total and overhead / total > 0.20:
                over_budget.append(unit["id"])
        if unit["delivery"] == "delivered":
            followup = unit.get("repairFollowup")
            if not isinstance(followup, dict) or followup.get("status") != "complete":
                followup_pending.append(unit["id"])
            else:
                through = instant(followup.get("through"), f"{unit['id']}.repairFollowup.through")
                delivered_at = instant(unit.get("deliveredAt"), f"{unit['id']}.deliveredAt")
                if (through - delivered_at).total_seconds() < 30 * 24 * 60 * 60 or through > as_of:
                    followup_pending.append(unit["id"])
    productive = sum(value[0] for _, value in measured)
    overhead = sum(value[1] for _, value in measured)
    observed = sum(value[2] for _, value in measured)
    denominator = productive + overhead + bounded_missing
    conservative = None if denominator == 0 or unbounded_missing else (overhead + bounded_missing) / denominator
    delivered_cost = None if not delivered or unbounded_missing else (observed + bounded_missing) / len(delivered)
    return {
        "arm": arm,
        "target": target,
        "selected": len(selected),
        "delivered": len(delivered),
        "deliveryFraction": None if not selected else len(delivered) / len(selected),
        "measuredUnits": len(measured),
        "observedHeadlineTokens": observed,
        "productiveTokens": productive,
        "overheadAndUnclassifiedTokens": overhead,
        "boundedMissingTokens": bounded_missing,
        "unboundedMissingUnits": sorted(unbounded_missing),
        "conservativeOverheadRatio": conservative,
        "costPerDeliveredUnit": delivered_cost,
        "overBudgetUnits": sorted(over_budget),
        "followupPendingUnits": sorted(followup_pending),
    }


def evaluate(document: dict[str, Any]) -> dict[str, Any]:
    require(document.get("schema") == INPUT_SCHEMA, "unsupported cohort schema")
    cohort = document.get("cohort")
    require(isinstance(cohort, str) and cohort, "cohort must be non-empty")
    as_of = instant(document.get("asOf"), "asOf")
    target = document.get("targetPerArm")
    require(type(target) is int and target >= 10, "targetPerArm must be at least 10")
    units = document.get("units")
    require(isinstance(units, list), "units must be an array")
    ids: set[str] = set()
    for unit in units:
        require(isinstance(unit, dict), "every unit must be an object")
        unit_id = unit.get("id")
        require(isinstance(unit_id, str) and unit_id, "every unit needs an id")
        require(unit_id not in ids, f"duplicate unit id: {unit_id}")
        ids.add(unit_id)
        require(unit.get("arm") in ("routine", "baseline"), f"{unit_id}.arm is unsupported")
        require(unit.get("kind") == "code", f"{unit_id}.kind must be code")
        require(unit.get("delivery") in ("delivered", "not-delivered", "pending"), f"{unit_id}.delivery is unsupported")
        require(type(unit.get("eligible")) is bool, f"{unit_id}.eligible must be boolean")
        require(unit["eligible"], f"{unit_id} is not eligible for this cohort")
        if unit["arm"] == "routine":
            require(unit.get("selectedBeforeOutcome") is True, f"{unit_id} was not selected before its outcome")
        measured_usage(unit)

    routine = arm_result("routine", units, target, as_of)
    baseline = arm_result("baseline", units, target, as_of)
    population = document.get("providerPopulation")
    require(isinstance(population, dict), "providerPopulation must be an object")
    population_status = population.get("status")
    require(population_status in ("measured", "unknown"), "providerPopulation.status is unsupported")
    observed = routine["observedHeadlineTokens"] + baseline["observedHeadlineTokens"]
    if population_status == "measured":
        population_total = nonnegative(population.get("totalHeadlineTokens"), "providerPopulation.totalHeadlineTokens")
        require(population_total >= observed, "provider population is smaller than observed usage")
        coverage = None if population_total == 0 else observed / population_total
    else:
        population_total = None
        coverage = None

    reasons: list[str] = []
    for arm in (routine, baseline):
        if arm["selected"] < target:
            reasons.append(f"{arm['arm']}-cohort-short")
        if arm["unboundedMissingUnits"]:
            reasons.append(f"{arm['arm']}-usage-unbounded")
    if coverage is None:
        reasons.append("usage-coverage-unknown")
    elif coverage < 0.95:
        reasons.append("usage-coverage-below-95-percent")
    if routine["conservativeOverheadRatio"] is None or routine["conservativeOverheadRatio"] > 0.20:
        reasons.append("routine-overhead-ceiling-not-met")
    if routine["overBudgetUnits"]:
        reasons.append("routine-unit-over-budget")
    if routine["followupPendingUnits"]:
        reasons.append("routine-30-day-followup-pending")
    if routine["deliveryFraction"] is None or baseline["deliveryFraction"] is None or routine["deliveryFraction"] < baseline["deliveryFraction"]:
        reasons.append("delivery-fraction-not-preserved")
    routine_cost, baseline_cost = routine["costPerDeliveredUnit"], baseline["costPerDeliveredUnit"]
    if routine_cost is None or baseline_cost is None or routine_cost >= baseline_cost:
        reasons.append("delivered-unit-cost-not-improved")
    reasons = sorted(set(reasons))
    return {
        "schema": OUTPUT_SCHEMA,
        "cohort": cohort,
        "asOf": document["asOf"],
        "status": "sufficient" if not reasons else "insufficient",
        "reasons": reasons,
        "providerPopulation": {"status": population_status, "totalHeadlineTokens": population_total, "coverage": coverage},
        "arms": {"routine": routine, "baseline": baseline},
        "deliveryAuthority": "none-observer-only",
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        document = json.loads(Path(args.input).read_text(encoding="utf-8"))
        require(isinstance(document, dict), "cohort root must be an object")
        result = evaluate(document)
        encoded = json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n"
        Path(args.output).write_text(encoded, encoding="utf-8")
        print(encoded, end="")
        return 0
    except (OSError, json.JSONDecodeError, InvalidCohort) as error:
        print(json.dumps({"schema": OUTPUT_SCHEMA, "status": "invalid", "reason": str(error)}, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

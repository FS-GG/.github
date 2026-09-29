#!/usr/bin/env python3
"""Evaluate a predeclared routine/baseline cohort without authorizing delivery."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime
from pathlib import Path
from typing import Any


INPUT_SCHEMA = "fsgg.routine-cohort-observation/1"
INPUT_SCHEMA_V2 = "fsgg.routine-cohort-observation/2"
OUTPUT_SCHEMA = "fsgg.routine-cohort-result/1"
SHA1 = re.compile(r"^[0-9a-f]{40}$")


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


def completed_item_policy(document: dict[str, Any]) -> dict[str, Any] | None:
    if document["schema"] == INPUT_SCHEMA:
        return None
    policy = document.get("repairObservation")
    require(isinstance(policy, dict), "repairObservation must be an object for schema 2")
    require(policy.get("mode") == "completed-items", "repairObservation.mode is unsupported")
    declared_at = instant(policy.get("declaredAt"), "repairObservation.declaredAt")
    target = policy.get("completedItemTarget")
    require(type(target) is int and target == 10, "repairObservation.completedItemTarget must be 10")
    lanes = policy.get("enrolledLanes")
    require(isinstance(lanes, list) and lanes, "repairObservation.enrolledLanes must be non-empty")
    lane_ids: set[str] = set()
    lane_enrollments: dict[str, datetime] = {}
    predeclared_originals: set[tuple[str, str]] = set()
    for lane in lanes:
        require(isinstance(lane, dict), "every enrolled lane must be an object")
        lane_id = lane.get("id")
        require(isinstance(lane_id, str) and lane_id, "every enrolled lane needs an id")
        require(lane_id not in lane_ids, f"duplicate enrolled lane: {lane_id}")
        lane_ids.add(lane_id)
        enrolled_at = instant(lane.get("enrolledAt"), f"{lane_id}.enrolledAt")
        require(enrolled_at >= declared_at, f"{lane_id}.enrolledAt precedes the policy declaration")
        lane_enrollments[lane_id] = enrolled_at
        require(
            lane.get("statusAtDeclaration") in ("unresolved", "future-selection"),
            f"{lane_id}.statusAtDeclaration is unsupported",
        )
        require(lane.get("priorUsage") in ("measured", "missing"), f"{lane_id}.priorUsage is unsupported")
        originals = lane.get("predeclaredUnresolvedOriginals")
        require(isinstance(originals, list), f"{lane_id}.predeclaredUnresolvedOriginals must be an array")
        for original in originals:
            require(isinstance(original, str) and original, f"{lane_id} has an invalid predeclared original")
            require(
                original == lane_id or original.startswith(f"{lane_id}."),
                f"{original} is not a canonical original for {lane_id}",
            )
            key = (lane_id, original)
            require(key not in predeclared_originals, f"duplicate predeclared original: {original}")
            predeclared_originals.add(key)
    current_selection = document.get("currentSelection")
    require(isinstance(current_selection, dict), "currentSelection must be an object for schema 2")
    require(current_selection.get("revision") == "r5-completed-items/1", "currentSelection.revision is unsupported")
    require(
        instant(current_selection.get("declaredAt"), "currentSelection.declaredAt") == declared_at,
        "currentSelection.declaredAt must match repairObservation.declaredAt",
    )
    require(current_selection.get("outcomesKnownAtSelection") is False, "current selection was not prospective")
    return {
        "declaredAt": declared_at,
        "target": target,
        "laneIds": lane_ids,
        "laneEnrollments": lane_enrollments,
        "predeclaredOriginals": predeclared_originals,
    }


def current_unit_identity(unit: dict[str, Any], policy: dict[str, Any], as_of: datetime) -> tuple[str, str, datetime]:
    lane = unit.get("lane")
    original = unit.get("originalItem")
    require(lane in policy["laneIds"], f"{unit['id']}.lane is not enrolled")
    require(isinstance(original, str) and original, f"{unit['id']}.originalItem must be non-empty")
    require(
        original == lane or original.startswith(f"{lane}."),
        f"{unit['id']}.originalItem is not canonical for its lane",
    )
    selected_at = instant(unit.get("selectedAt"), f"{unit['id']}.selectedAt")
    require(selected_at <= as_of, f"{unit['id']}.selectedAt is after asOf")
    return lane, original, selected_at


def current_unit_is_enrolled(unit: dict[str, Any], policy: dict[str, Any], as_of: datetime) -> bool:
    lane, original, selected_at = current_unit_identity(unit, policy, as_of)
    return selected_at >= policy["laneEnrollments"][lane] or (lane, original) in policy["predeclaredOriginals"]


def validated_completion(unit: dict[str, Any], selected_at: datetime, as_of: datetime) -> datetime | None:
    completed_at_value = unit.get("completedAt")
    if completed_at_value is None or unit["delivery"] != "delivered":
        return None
    completed_at = instant(completed_at_value, f"{unit['id']}.completedAt")
    require(completed_at <= as_of, f"{unit['id']}.completedAt is after asOf")
    require(completed_at >= selected_at, f"{unit['id']}.completedAt precedes selection")
    completion = unit.get("finalCompletion")
    if not isinstance(completion, dict) or completion.get("owningAcceptance") != "Done":
        return None
    readback = completion.get("nativeDoneReadback")
    source = completion.get("sourceDelivery")
    if not isinstance(readback, dict) or not isinstance(source, dict):
        return None
    require(readback.get("state") == "Done", f"{unit['id']}.nativeDoneReadback.state must be Done")
    read_at = instant(readback.get("readAt"), f"{unit['id']}.nativeDoneReadback.readAt")
    require(read_at == completed_at, f"{unit['id']}.completedAt must equal the native Done readback")
    require(isinstance(readback.get("url"), str) and readback["url"], f"{unit['id']}.nativeDoneReadback.url is required")
    require(isinstance(source.get("url"), str) and source["url"], f"{unit['id']}.sourceDelivery.url is required")
    require(SHA1.fullmatch(source.get("commit", "")) is not None, f"{unit['id']}.sourceDelivery.commit must be a SHA-1")
    require(SHA1.fullmatch(source.get("tree", "")) is not None, f"{unit['id']}.sourceDelivery.tree must be a SHA-1")
    merged_at = instant(source.get("mergedAt"), f"{unit['id']}.sourceDelivery.mergedAt")
    require(merged_at <= read_at, f"{unit['id']}.source delivery follows Done readback")
    checks = completion.get("checks")
    require(isinstance(checks, list) and checks, f"{unit['id']}.finalCompletion.checks must be non-empty")
    for check in checks:
        require(isinstance(check, dict), f"{unit['id']}.finalCompletion.checks entries must be objects")
        require(isinstance(check.get("name"), str) and check["name"], f"{unit['id']} has an unnamed check")
        require(check.get("status") == "SUCCESS", f"{unit['id']} has a non-successful completion check")
        require(isinstance(check.get("url"), str) and check["url"], f"{unit['id']} has a check without a URL")
    return completed_at


def completed_item_window(
    units: list[dict[str, Any]], policy: dict[str, Any], as_of: datetime
) -> dict[str, Any]:
    completions: dict[str, datetime] = {}
    for unit in units:
        if unit["arm"] != "routine":
            continue
        _, original, selected_at = current_unit_identity(unit, policy, as_of)
        if not current_unit_is_enrolled(unit, policy, as_of):
            continue
        completed_at = validated_completion(unit, selected_at, as_of)
        if completed_at is None or completed_at < policy["declaredAt"]:
            continue
        previous = completions.get(original)
        if previous is None or completed_at < previous:
            completions[original] = completed_at
    ordered = sorted(completions.items(), key=lambda value: (value[1], value[0]))
    cutoff = ordered[policy["target"] - 1][1] if len(ordered) >= policy["target"] else None
    return {
        "target": policy["target"],
        "completedDistinctItems": len(ordered),
        "countedOriginalItems": [item for item, _ in ordered[: policy["target"]]],
        "cutoff": cutoff,
    }


def arm_result(
    arm: str,
    units: list[dict[str, Any]],
    target: int,
    as_of: datetime,
    item_window: dict[str, Any] | None,
    policy: dict[str, Any] | None,
) -> dict[str, Any]:
    selected = [unit for unit in units if unit["arm"] == arm]
    if arm == "routine" and item_window is not None and policy is not None:
        selected = [unit for unit in selected if current_unit_is_enrolled(unit, policy, as_of)]
        if item_window["cutoff"] is not None:
            selected = [
                unit for unit in selected
                if instant(unit.get("selectedAt"), f"{unit['id']}.selectedAt") <= item_window["cutoff"]
            ]
        selected_originals = {unit["originalItem"] for unit in selected}
        delivered_originals = {unit["originalItem"] for unit in selected if unit["delivery"] == "delivered"}
    else:
        selected_originals = {unit["id"] for unit in selected}
        delivered_originals = {unit["id"] for unit in selected if unit["delivery"] == "delivered"}
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
        if unit["delivery"] == "delivered" and item_window is None:
            followup = unit.get("repairFollowup")
            if not isinstance(followup, dict) or followup.get("status") != "complete":
                followup_pending.append(unit["id"])
            else:
                through = instant(followup.get("through"), f"{unit['id']}.repairFollowup.through")
                delivered_at = instant(unit.get("deliveredAt"), f"{unit['id']}.deliveredAt")
                if (through - delivered_at).total_seconds() < 30 * 24 * 60 * 60 or through > as_of:
                    followup_pending.append(unit["id"])
        elif arm == "routine" and item_window is not None and item_window["cutoff"] is not None:
            followup = unit.get("repairFollowup")
            if not isinstance(followup, dict) or followup.get("status") != "complete":
                followup_pending.append(unit["id"])
            else:
                through = instant(followup.get("through"), f"{unit['id']}.repairFollowup.through")
                if through != item_window["cutoff"] or through > as_of:
                    followup_pending.append(unit["id"])
    productive = sum(value[0] for _, value in measured)
    overhead = sum(value[1] for _, value in measured)
    observed = sum(value[2] for _, value in measured)
    denominator = productive + overhead + bounded_missing
    conservative = None if denominator == 0 or unbounded_missing else (overhead + bounded_missing) / denominator
    delivered_cost = None if not delivered_originals or unbounded_missing else (observed + bounded_missing) / len(delivered_originals)
    return {
        "arm": arm,
        "target": target,
        "selected": len(selected_originals),
        "selectedAttempts": len(selected),
        "delivered": len(delivered_originals),
        "deliveryFraction": None if not selected_originals else len(delivered_originals) / len(selected_originals),
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
    require(document.get("schema") in (INPUT_SCHEMA, INPUT_SCHEMA_V2), "unsupported cohort schema")
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
        require(unit.get("delivery") in ("delivered", "not-delivered", "cancelled", "pending"), f"{unit_id}.delivery is unsupported")
        require(type(unit.get("eligible")) is bool, f"{unit_id}.eligible must be boolean")
        require(unit["eligible"], f"{unit_id} is not eligible for this cohort")
        if unit["arm"] == "routine":
            require(unit.get("selectedBeforeOutcome") is True, f"{unit_id} was not selected before its outcome")
        measured_usage(unit)

    policy = completed_item_policy(document)
    item_window = completed_item_window(units, policy, as_of) if policy is not None else None
    routine = arm_result("routine", units, target, as_of, item_window, policy)
    baseline = arm_result("baseline", units, target, as_of, None, None)
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
    if item_window is None:
        if routine["followupPendingUnits"]:
            reasons.append("routine-30-day-followup-pending")
    else:
        if item_window["completedDistinctItems"] < item_window["target"]:
            reasons.append("routine-completed-item-window-pending")
        if routine["followupPendingUnits"]:
            reasons.append("routine-completed-item-repair-accounting-pending")
        cohort_units = [
            unit for unit in units
            if unit["arm"] == "routine" and current_unit_is_enrolled(unit, policy, as_of)
        ]
        if item_window["cutoff"] is not None:
            cohort_units = [
                unit for unit in cohort_units
                if instant(unit.get("selectedAt"), f"{unit['id']}.selectedAt") <= item_window["cutoff"]
            ]
        severe_events: list[dict[str, Any]] = []
        rollback_events: list[dict[str, Any]] = []
        allowed_severe_events = {"credential-exposure", "irreversible-data-loss", "authority-breach"}
        for unit in cohort_units:
            followup = unit.get("repairFollowup")
            if not isinstance(followup, dict):
                continue
            events = followup.get("severeEvents", [])
            require(isinstance(events, list), f"{unit['id']}.repairFollowup.severeEvents must be an array")
            for event in events:
                require(isinstance(event, dict), f"{unit['id']}.repairFollowup.severeEvents entries must be objects")
                require(
                    event.get("kind") in allowed_severe_events,
                    f"{unit['id']}.repairFollowup.severeEvents contains an unsupported event",
                )
                observed_at = instant(event.get("observedAt"), f"{unit['id']}.repairFollowup.severeEvents.observedAt")
                require(observed_at <= as_of, f"{unit['id']} has a severe event after asOf")
                if item_window["cutoff"] is None or observed_at <= item_window["cutoff"]:
                    severe_events.append(event)
            require(
                "processAttributableRollbacks" not in followup,
                f"{unit['id']}.repairFollowup must use timestamped processAttributableRollbackEvents",
            )
            events = followup.get("processAttributableRollbackEvents", [])
            require(isinstance(events, list), f"{unit['id']}.repairFollowup.processAttributableRollbackEvents must be an array")
            for event in events:
                require(isinstance(event, dict), f"{unit['id']} has an invalid rollback event")
                observed_at = instant(event.get("observedAt"), f"{unit['id']}.repairFollowup.rollback.observedAt")
                require(observed_at <= as_of, f"{unit['id']} has a rollback after asOf")
                if item_window["cutoff"] is None or observed_at <= item_window["cutoff"]:
                    rollback_events.append(event)
        if severe_events:
            reasons.append("candidate-admission-stopped-severe-event")
        if len(rollback_events) >= 2:
            reasons.append("candidate-expansion-stopped-two-process-rollbacks")
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
        "repairObservation": None if item_window is None else {
            "mode": "completed-items",
            "target": item_window["target"],
            "completedDistinctItems": item_window["completedDistinctItems"],
            "countedOriginalItems": item_window["countedOriginalItems"],
            "cutoff": None if item_window["cutoff"] is None else item_window["cutoff"].isoformat().replace("+00:00", "Z"),
        },
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

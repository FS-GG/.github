#!/usr/bin/env python3
"""Validate and summarize the frozen LEARN-01.1 synthetic contract corpus."""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import sys
from collections import Counter, defaultdict
from datetime import datetime, timedelta


class Refusal(ValueError):
    pass


def load(path: pathlib.Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise Refusal(f"{path}: root must be an object")
    return value


def validate_contract(contract: dict) -> None:
    if contract.get("schema") != "fsgg.learn.context-experiment/v1":
        raise Refusal("unsupported contract schema")
    if contract.get("status") != "source-contract-not-enrolled":
        raise Refusal("LEARN-01.1 cannot activate enrollment")
    arms = [arm.get("id") for arm in contract.get("arms", [])]
    if arms != ["current", "focused"]:
        raise Refusal("contract must freeze current and focused arms")
    assignment = contract.get("assignment", {})
    if assignment.get("unit") != "original-item" or assignment.get("descendantsIndependentSamples") is not False:
        raise Refusal("original item must be the assignment and analysis unit")
    if not assignment.get("outcomeDerivedLabelsForbidden"):
        raise Refusal("outcome-derived labels must be forbidden")
    accounting = contract.get("accounting", {})
    if accounting.get("missingUsage") != "unknown-unbounded" or not accounting.get("zeroImputationForbidden"):
        raise Refusal("unbounded missing usage must remain unknown")
    window = contract.get("window", {})
    if window.get("minimumEnrollmentDays") != 28 or window.get("maximumEnrollmentDays") != 84:
        raise Refusal("window cadence drifted")
    budget = window.get("buildAndAnalysisBudget", {})
    if budget != {"maximumEngineeringDays": 5, "scopeCheckDay": 2}:
        raise Refusal("build and analysis budget drifted")
    decision = contract.get("decision", {})
    primary = decision.get("primary", {})
    if (decision.get("confidenceLevel"), decision.get("power"), primary.get("endpoint"),
            primary.get("practicalReduction"), primary.get("minimumIndependentOriginalItemsPerArm")) != (
            0.95, 0.8, "arithmetic-mean-provider-total-tokens-per-assigned-original-item", 0.10, 6908):
        raise Refusal("primary decision or precision plan drifted")
    mapping = contract.get("fieldMapping", [])
    concepts = {row.get("concept") for row in mapping if isinstance(row, dict)}
    required_concepts = {
        "feature and item identity", "invocation lineage", "requested execution profile",
        "observed execution profile and usage", "terminal native outcome",
        "pre-dispatch task rubric snapshot", "context recipe and manifest identity",
        "persisted experiment window and assignment", "late repair link"
    }
    if concepts != required_concepts:
        raise Refusal("conceptual-to-existing field mapping is incomplete")
    analysis = contract.get("analysis", {})
    if analysis.get("version") != "learn-01-synthetic-analysis-v1" or analysis.get("claimBoundary") != "synthetic-fixture-only-no-efficiency-result":
        raise Refusal("analysis version or claim boundary drifted")
    claims = contract.get("claims", {})
    if not claims or any(value is not False for value in claims.values()):
        raise Refusal("LEARN-01.1 cannot claim publication, operation, observation or feature exit")


def validate_corpus(contract: dict, corpus: dict) -> dict:
    if corpus.get("schema") != "fsgg.learn.synthetic-corpus/v1":
        raise Refusal("unsupported corpus schema")
    if corpus.get("contractId") != contract.get("contractId"):
        raise Refusal("corpus contract identity mismatch")
    if corpus.get("analysisUnit") != "original-item":
        raise Refusal("subissues or invocations cannot be independent successes")

    items = corpus.get("items")
    expected_invocations = corpus.get("expectedInvocations")
    expected_shared_costs = corpus.get("expectedSharedCosts")
    costs = corpus.get("costs")
    if not isinstance(items, list) or not items:
        raise Refusal("corpus requires items")
    if not isinstance(expected_invocations, list) or not expected_invocations:
        raise Refusal("corpus requires an expected invocation inventory")
    if not isinstance(expected_shared_costs, list):
        raise Refusal("corpus requires an expected shared-cost inventory")
    if not isinstance(costs, list) or not costs:
        raise Refusal("corpus requires costs")

    items_by_id = {}
    for item in items:
        item_id = item.get("itemId")
        if not isinstance(item_id, str) or not item_id or item_id in items_by_id:
            raise Refusal("item identities must be unique non-empty strings")
        items_by_id[item_id] = item

    roots = {}
    for item_id, item in items_by_id.items():
        original = item.get("originalItemId")
        parent = item.get("parentItemId")
        if parent is None:
            if original != item_id:
                raise Refusal(f"{item_id}: an original root must own its canonical identity")
            roots[item_id] = item
        else:
            if (parent not in items_by_id or original == item_id or
                    items_by_id[parent].get("originalItemId") != original):
                raise Refusal(f"{item_id}: child lineage does not identify a distinct canonical original")

    if not roots:
        raise Refusal("corpus requires original roots")

    outcomes = Counter()
    required_scenarios = {"failure", "cancellation", "open", "rescue", "delayed-repair", "shared-cost"}
    scenarios = set()
    arm_by_issue = {}
    fixed_14_day_completions = 0
    repair_mature = 0
    repairs_in_mature_followup = 0
    open_censored = []

    def timestamp(value, field, item_id):
        if not isinstance(value, str):
            raise Refusal(f"{item_id}: {field} must be an RFC3339 timestamp")
        try:
            parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
        except ValueError as error:
            raise Refusal(f"{item_id}: invalid {field}") from error
        if parsed.tzinfo is None:
            raise Refusal(f"{item_id}: {field} must include an offset")
        return parsed

    for item_id, item in items_by_id.items():
        original = item.get("originalItemId")
        root = roots.get(original)
        if root is None:
            raise Refusal(f"{item_id}: canonical original is absent")
        arm = item.get("assignedArm")
        if arm not in {"current", "focused"}:
            raise Refusal(f"{item_id}: invalid assigned arm")
        if arm != root.get("assignedArm"):
            raise Refusal(f"{item_id}: child assignment differs from its canonical original")
        expected_source = "persisted-pre-treatment-assignment" if item_id == original else "inherited-original-assignment"
        if item.get("assignmentSource") != expected_source:
            raise Refusal(f"{item_id}: outcome-derived or redrawn assignment is forbidden")
        if item.get("assignmentOrdinal", 0) >= item.get("firstTreatmentWorkOrdinal", 0):
            raise Refusal(f"{item_id}: assignment must precede treatment work")
        outcome = item.get("outcome")
        if outcome not in {"accepted", "failed", "cancelled", "open"}:
            raise Refusal(f"{item_id}: invalid outcome")
        if item_id != original:
            continue
        scenarios.update(item.get("scenarios", []))
        outcome_scenario = {"failed": "failure", "cancelled": "cancellation", "open": "open"}.get(outcome)
        if outcome_scenario:
            scenarios.add(outcome_scenario)
        outcomes[outcome] += 1
        arm_by_issue[item_id] = arm
        assigned = timestamp(item.get("assignedAt"), "assignedAt", item_id)
        cutoff = timestamp(item.get("cutoffAt"), "cutoffAt", item_id)
        if cutoff < assigned:
            raise Refusal(f"{item_id}: cutoff precedes assignment")
        accepted_value = item.get("acceptedAt")
        accepted = timestamp(accepted_value, "acceptedAt", item_id) if accepted_value is not None else None
        repair_value = item.get("materialRepairAt")
        repair = timestamp(repair_value, "materialRepairAt", item_id) if repair_value is not None else None
        if accepted is not None and not assigned <= accepted <= cutoff:
            raise Refusal(f"{item_id}: acceptance falls outside assignment and cutoff")
        if outcome == "accepted" and accepted is None:
            raise Refusal(f"{item_id}: accepted outcome lacks acceptance time")
        if outcome != "accepted" and accepted is not None:
            raise Refusal(f"{item_id}: non-accepted outcome has an acceptance time")
        if repair is not None and (accepted is None or not accepted < repair <= cutoff):
            raise Refusal(f"{item_id}: repair does not follow acceptance before cutoff")
        if "delayed-repair" in item.get("scenarios", []) and repair is None:
            raise Refusal(f"{item_id}: delayed repair scenario lacks a repair time")
        if accepted is not None and accepted <= assigned + timedelta(days=14):
            fixed_14_day_completions += 1
        if accepted is not None and cutoff >= accepted + timedelta(days=30):
            repair_mature += 1
            if repair is not None and repair <= accepted + timedelta(days=30):
                repairs_in_mature_followup += 1
        if outcome == "open":
            open_censored.append(item_id)

    expected_pairs = {}
    expected_by_invocation = {}
    inventoried_items = set()
    root_invocation_items = set()
    for invocation in expected_invocations:
        invocation_id = invocation.get("invocationId")
        item_id = invocation.get("itemId")
        original = invocation.get("originalItemId")
        turns = invocation.get("expectedTurnIds")
        if not isinstance(invocation_id, str) or not invocation_id or invocation_id in expected_by_invocation:
            raise Refusal("expected invocation identities must be unique non-empty strings")
        if item_id not in items_by_id or original != items_by_id[item_id].get("originalItemId"):
            raise Refusal(f"{invocation_id}: invocation lineage disagrees with item inventory")
        if not isinstance(turns, list) or not turns or len(set(turns)) != len(turns) or not all(isinstance(turn, str) and turn for turn in turns):
            raise Refusal(f"{invocation_id}: expected turn identities must be unique non-empty strings")
        if not isinstance(invocation.get("terminal"), bool):
            raise Refusal(f"{invocation_id}: terminal assertion must be boolean")
        if invocation.get("kind") not in {"root", "child", "retry", "review", "integration", "rescue", "repair"}:
            raise Refusal(f"{invocation_id}: invalid invocation kind")
        if invocation.get("kind") == "root":
            if item_id != original:
                raise Refusal(f"{invocation_id}: root invocation belongs to a child item")
            root_invocation_items.add(item_id)
        expected_by_invocation[invocation_id] = invocation
        inventoried_items.add(item_id)
        for turn in turns:
            expected_pairs[(invocation_id, turn)] = original
    if inventoried_items != set(items_by_id):
        raise Refusal("expected invocation inventory does not cover every root and child item")
    if root_invocation_items != set(roots):
        raise Refusal("expected invocation inventory lacks one root invocation per original")

    expected_shared = {}
    for shared in expected_shared_costs:
        cost_id = shared.get("costId")
        if not isinstance(cost_id, str) or not cost_id or cost_id in expected_shared:
            raise Refusal("expected shared-cost identities must be unique non-empty strings")
        allocations = shared.get("allocations")
        if not isinstance(allocations, list) or len(allocations) < 2:
            raise Refusal(f"{cost_id}: expected shared cost requires multiple allocations")
        identities = [allocation.get("originalItemId") for allocation in allocations]
        fractions = [allocation.get("fraction") for allocation in allocations]
        if (len(set(identities)) != len(identities) or any(identity not in roots for identity in identities) or
                any(not isinstance(fraction, (int, float)) or isinstance(fraction, bool) or fraction <= 0 for fraction in fractions) or
                not math.isclose(sum(float(fraction) for fraction in fractions), 1.0, rel_tol=0.0, abs_tol=1e-9)):
            raise Refusal(f"{cost_id}: invalid expected shared-cost allocation")
        expected_shared[cost_id] = shared
        scenarios.add("shared-cost")

    totals = defaultdict(float)
    incomplete_reasons = defaultdict(set)
    cost_ids = set()
    observed_pairs = set()
    observed_shared = set()
    for cost in costs:
        cost_id = cost.get("costId")
        if not isinstance(cost_id, str) or not cost_id or cost_id in cost_ids:
            raise Refusal("cost identities must be unique non-empty strings")
        cost_ids.add(cost_id)
        completeness = cost.get("completeness")
        if completeness == "imputed-zero":
            raise Refusal(f"{cost_id}: unbounded missing tokens cannot be zero")
        if completeness not in {"complete", "unknown-unbounded"}:
            raise Refusal(f"{cost_id}: invalid completeness")
        amount = cost.get("providerTotalTokens")
        if completeness == "complete" and (not isinstance(amount, int) or isinstance(amount, bool) or amount < 0):
            raise Refusal(f"{cost_id}: complete provider total must be a non-negative integer")
        if completeness == "unknown-unbounded" and amount is not None:
            raise Refusal(f"{cost_id}: unknown usage cannot carry a guessed amount")
        allocations = cost.get("allocations")
        if not isinstance(allocations, list) or not allocations:
            raise Refusal(f"{cost_id}: missing allocation")
        fraction_sum = 0.0
        allocated_items = set()
        for allocation in allocations:
            item = allocation.get("originalItemId")
            fraction = allocation.get("fraction")
            if item not in roots or item in allocated_items:
                raise Refusal(f"{cost_id}: invalid or duplicate original-item allocation")
            if not isinstance(fraction, (int, float)) or isinstance(fraction, bool) or fraction <= 0:
                raise Refusal(f"{cost_id}: invalid allocation fraction")
            allocated_items.add(item)
            fraction_sum += float(fraction)
            if completeness == "complete":
                totals[item] += amount * float(fraction)
            else:
                incomplete_reasons[item].add("unknown-unbounded-usage")
        if not math.isclose(fraction_sum, 1.0, rel_tol=0.0, abs_tol=1e-9):
            raise Refusal(f"{cost_id}: shared-cost fractions must sum to one")
        if len(allocations) > 1:
            scenarios.add("shared-cost")
        kind = cost.get("kind")
        if kind == "invocation":
            pair = (cost.get("invocationId"), cost.get("turnId"))
            if pair not in expected_pairs:
                raise Refusal(f"{cost_id}: usage does not match an expected invocation turn")
            if pair in observed_pairs:
                raise Refusal(f"{cost_id}: duplicate usage for one invocation turn")
            observed_pairs.add(pair)
            if allocated_items != {expected_pairs[pair]} or not math.isclose(fraction_sum, 1.0):
                raise Refusal(f"{cost_id}: invocation usage must belong wholly to its canonical original")
        elif kind == "shared":
            if cost_id not in expected_shared or cost_id in observed_shared:
                raise Refusal(f"{cost_id}: unexpected or duplicate shared cost")
            observed_shared.add(cost_id)
            expected_allocations = expected_shared[cost_id].get("allocations")
            if allocations != expected_allocations:
                raise Refusal(f"{cost_id}: shared allocation differs from expected inventory")
        else:
            raise Refusal(f"{cost_id}: invalid cost kind")

    for pair, original in expected_pairs.items():
        invocation = expected_by_invocation[pair[0]]
        if pair not in observed_pairs:
            incomplete_reasons[original].add("missing-expected-usage:" + "/".join(pair))
        if not invocation["terminal"]:
            incomplete_reasons[original].add("nonterminal-invocation:" + pair[0])
    for cost_id, shared in expected_shared.items():
        if cost_id not in observed_shared:
            for allocation in shared.get("allocations", []):
                original = allocation.get("originalItemId")
                if original in roots:
                    incomplete_reasons[original].add("missing-expected-shared-cost:" + cost_id)

    missing = required_scenarios - scenarios
    if missing:
        raise Refusal("synthetic scenarios missing: " + ", ".join(sorted(missing)))

    arm_totals = {arm: [] for arm in ("current", "focused")}
    incomplete = []
    for item in sorted(roots):
        if not incomplete_reasons[item]:
            arm_totals[arm_by_issue[item]].append(totals[item])
        else:
            incomplete.append(item)
    return {
        "schema": "fsgg.learn.synthetic-summary/v1",
        "contractId": contract["contractId"],
        "originalItems": len(roots),
        "childItems": len(items_by_id) - len(roots),
        "outcomes": dict(sorted(outcomes.items())),
        "fixed14DayCompletions": fixed_14_day_completions,
        "repairMatureAcceptedItems": repair_mature,
        "materialRepairsInMatureFollowup": repairs_in_mature_followup,
        "openCensoredOriginalItems": sorted(open_censored),
        "completeTokenItemsByArm": {arm: len(values) for arm, values in arm_totals.items()},
        "providerTotalTokensByArm": {arm: int(sum(values)) for arm, values in arm_totals.items()},
        "providerTotalTokensByOriginalItem": {item: int(totals[item]) for item in sorted(roots) if not incomplete_reasons[item]},
        "incompleteTokenOriginalItems": incomplete,
        "incompleteTokenReasons": {item: sorted(incomplete_reasons[item]) for item in incomplete},
        "tokenComparisonQualified": not incomplete,
        "claim": "synthetic-fixture-only-no-efficiency-result"
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("contract", type=pathlib.Path)
    parser.add_argument("corpus", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path)
    args = parser.parse_args(argv)
    try:
        contract = load(args.contract)
        validate_contract(contract)
        result = validate_corpus(contract, load(args.corpus))
    except (OSError, json.JSONDecodeError, Refusal) as error:
        print(f"refused: {error}", file=sys.stderr)
        return 2
    rendered = json.dumps(result, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

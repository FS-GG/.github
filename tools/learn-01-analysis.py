#!/usr/bin/env python3
"""Validate and summarize the frozen LEARN-01.1 synthetic contract corpus."""
from __future__ import annotations

import argparse
import json
import math
import pathlib
import sys
from collections import Counter, defaultdict


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
    if (decision.get("confidenceLevel"), decision.get("power"), primary.get("practicalReduction"),
            primary.get("minimumIndependentOriginalItemsPerArm")) != (0.95, 0.8, 0.10, 875):
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

    issues = corpus.get("issues")
    costs = corpus.get("costs")
    if not isinstance(issues, list) or not issues:
        raise Refusal("corpus requires issues")
    if not isinstance(costs, list) or not costs:
        raise Refusal("corpus requires costs")

    known = set()
    outcomes = Counter()
    required_scenarios = {"failure", "cancellation", "open", "rescue", "delayed-repair", "shared-cost"}
    scenarios = set()
    arm_by_issue = {}
    for issue in issues:
        item = issue.get("originalItemId")
        if not isinstance(item, str) or not item or item in known:
            raise Refusal("original item identities must be unique non-empty strings")
        known.add(item)
        arm = issue.get("assignedArm")
        if arm not in {"current", "focused"}:
            raise Refusal(f"{item}: invalid assigned arm")
        if issue.get("labelSource") != "persisted-pre-treatment-assignment":
            raise Refusal(f"{item}: outcome-derived baseline labels are forbidden")
        if issue.get("assignmentOrdinal", 0) >= issue.get("firstTreatmentWorkOrdinal", 0):
            raise Refusal(f"{item}: assignment must precede treatment work")
        outcome = issue.get("outcome")
        if outcome not in {"accepted", "failed", "cancelled", "open"}:
            raise Refusal(f"{item}: invalid outcome")
        outcomes[outcome] += 1
        scenarios.update(issue.get("scenarios", []))
        arm_by_issue[item] = arm

    totals = defaultdict(float)
    complete = {item: True for item in known}
    cost_ids = set()
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
            if item not in known or item in allocated_items:
                raise Refusal(f"{cost_id}: invalid or duplicate original-item allocation")
            if not isinstance(fraction, (int, float)) or isinstance(fraction, bool) or fraction <= 0:
                raise Refusal(f"{cost_id}: invalid allocation fraction")
            allocated_items.add(item)
            fraction_sum += float(fraction)
            if completeness == "complete":
                totals[item] += amount * float(fraction)
            else:
                complete[item] = False
        if not math.isclose(fraction_sum, 1.0, rel_tol=0.0, abs_tol=1e-9):
            raise Refusal(f"{cost_id}: shared-cost fractions must sum to one")
        if len(allocations) > 1:
            scenarios.add("shared-cost")

    missing = required_scenarios - scenarios
    if missing:
        raise Refusal("synthetic scenarios missing: " + ", ".join(sorted(missing)))

    arm_totals = {arm: [] for arm in ("current", "focused")}
    incomplete = []
    for item in sorted(known):
        if complete[item]:
            arm_totals[arm_by_issue[item]].append(totals[item])
        else:
            incomplete.append(item)
    return {
        "schema": "fsgg.learn.synthetic-summary/v1",
        "contractId": contract["contractId"],
        "originalItems": len(known),
        "outcomes": dict(sorted(outcomes.items())),
        "completeTokenItemsByArm": {arm: len(values) for arm, values in arm_totals.items()},
        "providerTotalTokensByArm": {arm: int(sum(values)) for arm, values in arm_totals.items()},
        "incompleteTokenOriginalItems": incomplete,
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

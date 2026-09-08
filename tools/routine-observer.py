#!/usr/bin/env python3
"""Coalesce bounded public routine evidence without gating native delivery."""

from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
from typing import Any


SCHEMA = "fsgg.routine-unit-economics/2"
USAGE_SCHEMA = "fsgg.routine-usage/1"
DELIVERY_SCHEMA = "fsgg.routine-delivery/v1"
ECONOMICS_SCHEMA = "fsgg.coordination.qualification-cadence-report/2"

_SAFETY_SPEC = importlib.util.spec_from_file_location(
    "routine_telemetry_safety", Path(__file__).with_name("routine-telemetry-safety.py")
)
assert _SAFETY_SPEC and _SAFETY_SPEC.loader
_SAFETY = importlib.util.module_from_spec(_SAFETY_SPEC)
_SAFETY_SPEC.loader.exec_module(_SAFETY)


def load(path: str | None) -> tuple[dict[str, Any] | None, str | None]:
    if path is None:
        return None, "not-provided"
    return _SAFETY.read_public_object(path)


def delivery_view(value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "unknown", "code": problem}
    code_delivery = value.get("codeDelivery")
    status = code_delivery if code_delivery in {"delivered", "not-delivered", "unknown"} else "unknown"
    return {
        "status": status,
        "pr": value.get("pr"),
        "expectedHead": value.get("expectedHead"),
        "observedHead": value.get("observedHead") if value.get("observedHead") is not None else value.get("head"),
        "mergeCommit": value.get("mergeCommit"),
    }


def delivery_assessments(value: dict[str, Any] | None, problem: str | None) -> tuple[dict[str, str], dict[str, str]]:
    if value is None:
        return ({"status": "invalid", "code": problem or "delivery-missing"},
                {"status": "not-evaluated", "code": "delivery-invalid"})
    if value.get("schema") != DELIVERY_SCHEMA:
        return ({"status": "invalid", "code": "delivery-schema-unsupported"},
                {"status": "not-evaluated", "code": "delivery-invalid"})
    if (not isinstance(value.get("repo"), str) or type(value.get("pr")) is not int or value.get("pr") <= 0
            or value.get("outcome") not in {"ready", "refused", "delivered", "delivered-after-readback", "indeterminate"}
            or value.get("codeDelivery") not in {"delivered", "not-delivered", "unknown"}
            or value.get("publication") not in {"pending", "not-required"}
            or type(value.get("attempts")) is not int or value.get("attempts") < 0
            or not isinstance(value.get("reason"), (str, type(None)))):
        return ({"status": "invalid", "code": "delivery-fields-invalid"},
                {"status": "not-evaluated", "code": "delivery-invalid"})
    expected, observed, legacy = value.get("expectedHead"), value.get("observedHead"), value.get("head")
    if legacy is not None and observed is not None and legacy != observed:
        return ({"status": "invalid", "code": "delivery-head-conflict"},
                {"status": "invalid", "code": "delivery-head-conflict"})
    observed = observed if observed is not None else legacy
    for head in (expected, observed):
        if head is not None and (not isinstance(head, str) or len(head) != 40 or any(c not in "0123456789abcdef" for c in head)):
            return ({"status": "invalid", "code": "delivery-head-invalid"},
                    {"status": "not-evaluated", "code": "delivery-invalid"})
    merge_commit = value.get("mergeCommit")
    if merge_commit is not None and (not isinstance(merge_commit, str) or len(merge_commit) != 40
                                     or any(c not in "0123456789abcdef" for c in merge_commit)):
        return ({"status": "invalid", "code": "delivery-merge-commit-invalid"},
                {"status": "not-evaluated", "code": "delivery-invalid"})
    if expected is None:
        if observed is not None:
            return ({"status": "valid", "code": "legacy-delivery-record"},
                    {"status": "unproven", "code": "expected-head-absent"})
        return ({"status": "invalid", "code": "delivery-head-absent"},
                {"status": "not-evaluated", "code": "delivery-invalid"})
    if observed is None:
        return ({"status": "valid", "code": "delivery-record-valid"},
                {"status": "unproven", "code": "observed-head-absent"})
    if expected != observed:
        return ({"status": "valid", "code": "delivery-record-valid"},
                {"status": "invalid", "code": "head-mismatch"})
    return ({"status": "valid", "code": "delivery-record-valid"},
            {"status": "matched", "code": "expected-observed-match"})


def usage_view(unit: str, value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "missing", "code": problem, "scope": "whole-unit"}
    try:
        if value.get("schema") != USAGE_SCHEMA or value.get("unit") != unit or value.get("scope") != "whole-unit":
            raise ValueError("usage-identity-invalid")
        names = (
            "freshInputTokens", "cachedInputTokens", "outputTokens", "reasoningTokens",
            "productiveTokens", "overheadTokens", "unclassifiedTokens",
        )
        counts = {name: value[name] for name in names}
        if any(type(count) is not int or count < 0 for count in counts.values()):
            raise ValueError("usage-count-invalid")
        headline = counts["freshInputTokens"] + counts["cachedInputTokens"] + counts["outputTokens"]
        classified = counts["productiveTokens"] + counts["overheadTokens"] + counts["unclassifiedTokens"]
        if counts["reasoningTokens"] > counts["outputTokens"] or headline != classified:
            raise ValueError("usage-accounting-invalid")
        conservative = None if headline == 0 else (counts["overheadTokens"] + counts["unclassifiedTokens"]) / headline
        return {
            "status": "measured", "scope": "whole-unit", **counts, "headlineTokens": headline,
            "conservativeOverheadRatio": conservative,
            "withinTenPercentCeiling": None if conservative is None else conservative <= 0.10,
        }
    except KeyError:
        return {"status": "invalid", "code": "usage-field-missing", "scope": "whole-unit"}
    except (TypeError, ValueError) as error:
        return {"status": "invalid", "code": str(error), "scope": "whole-unit"}


def economics_view(value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "missing", "code": problem}
    if value.get("schema") != ECONOMICS_SCHEMA:
        return {"status": "invalid", "code": "economics-schema-unsupported"}
    if not isinstance(value.get("dataStatus"), str):
        return {"status": "invalid", "code": "economics-fields-invalid"}
    completeness = value.get("completeness") if isinstance(value.get("completeness"), dict) else {}
    expected, observed = completeness.get("attemptsExpected"), completeness.get("attemptsObserved")
    if (type(completeness.get("complete")) is not bool or type(expected) is not int or expected < 0
            or type(observed) is not int or observed < 0 or observed > expected
            or completeness.get("complete") != (expected == observed)):
        return {"status": "invalid", "code": "economics-completeness-invalid"}
    return {
        "status": "observed" if completeness.get("complete") is True else "incomplete",
        "dataStatus": value.get("dataStatus"),
        "completeness": completeness,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unit", required=True)
    parser.add_argument("--delivery", required=True)
    parser.add_argument("--usage")
    parser.add_argument("--economics")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    delivery, delivery_problem = load(args.delivery)
    usage, usage_problem = load(args.usage)
    economics, economics_problem = load(args.economics)
    delivery_result = delivery_view(delivery, delivery_problem)
    usage_result = usage_view(args.unit, usage, usage_problem)
    economics_result = economics_view(economics, economics_problem)
    record_validity, join_integrity = delivery_assessments(delivery, delivery_problem)
    population_coverage = {"status": "unknown", "code": "independent-population-collector-absent"}
    qualification = {
        "status": "not-evaluated",
        "code": "population-coverage-unknown" if usage_result["status"] == "measured" else "whole-unit-usage-insufficient",
    }
    report = {
        "schema": SCHEMA,
        "unit": args.unit,
        "delivery": delivery_result,
        "usage": usage_result,
        "economics": economics_result,
        "recordValidity": record_validity,
        "joinIntegrity": join_integrity,
        "populationCoverage": population_coverage,
        "qualification": qualification,
        "observerStatus": "complete" if usage_result["status"] == "measured" and economics_result["status"] == "observed" else "degraded",
    }
    write_problem = _SAFETY.write_public_report(args.output, report, [p for p in (args.delivery, args.usage, args.economics) if p])
    if write_problem is None:
        print(json.dumps(report, sort_keys=True, separators=(",", ":")))
    else:
        print(json.dumps({"schema": SCHEMA, "unit": args.unit, "observerStatus": "unavailable", "code": write_problem}, sort_keys=True))
    # Observation is explicitly advisory. Its missing inputs or output failure never changes delivery.
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

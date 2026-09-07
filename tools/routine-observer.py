#!/usr/bin/env python3
"""Coalesce routine delivery, usage, and CI economics without gating delivery."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


SCHEMA = "fsgg.routine-unit-economics/1"
USAGE_SCHEMA = "fsgg.routine-usage/1"


def load(path: str | None) -> tuple[dict[str, Any] | None, str | None]:
    if path is None:
        return None, "not-provided"
    try:
        value = json.loads(Path(path).read_text(encoding="utf-8"))
        if not isinstance(value, dict):
            raise ValueError("root must be an object")
        return value, None
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return None, f"unreadable:{type(error).__name__}"


def delivery_view(value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "unknown", "reason": problem}
    delivered = value.get("codeDelivery") == "delivered" or value.get("outcome") == "delivered"
    return {
        "status": "delivered" if delivered else "not-delivered",
        "pr": value.get("pr"),
        "head": value.get("head"),
        "mergeCommit": value.get("mergeCommit"),
    }


def usage_view(unit: str, value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "missing", "reason": problem, "scope": "whole-unit"}
    try:
        if value.get("schema") != USAGE_SCHEMA or value.get("unit") != unit or value.get("scope") != "whole-unit":
            raise ValueError("schema, unit, or whole-unit scope mismatch")
        names = (
            "freshInputTokens", "cachedInputTokens", "outputTokens", "reasoningTokens",
            "productiveTokens", "overheadTokens", "unclassifiedTokens",
        )
        counts = {name: value[name] for name in names}
        if any(type(count) is not int or count < 0 for count in counts.values()):
            raise ValueError("token counts must be non-negative integers")
        headline = counts["freshInputTokens"] + counts["cachedInputTokens"] + counts["outputTokens"]
        classified = counts["productiveTokens"] + counts["overheadTokens"] + counts["unclassifiedTokens"]
        if counts["reasoningTokens"] > counts["outputTokens"] or headline != classified:
            raise ValueError("token accounting does not reconcile")
        conservative = None if headline == 0 else (counts["overheadTokens"] + counts["unclassifiedTokens"]) / headline
        return {
            "status": "measured", "scope": "whole-unit", **counts, "headlineTokens": headline,
            "conservativeOverheadRatio": conservative,
            "withinTwentyPercentCeiling": None if conservative is None else conservative <= 0.20,
        }
    except (KeyError, TypeError, ValueError) as error:
        return {"status": "invalid", "reason": str(error), "scope": "whole-unit"}


def economics_view(value: dict[str, Any] | None, problem: str | None) -> dict[str, Any]:
    if value is None:
        return {"status": "missing", "reason": problem}
    if value.get("schema") != "fsgg.coordination.qualification-cadence-report/2":
        return {"status": "invalid", "reason": "unsupported-schema"}
    completeness = value.get("completeness") if isinstance(value.get("completeness"), dict) else {}
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
    report = {
        "schema": SCHEMA,
        "unit": args.unit,
        "delivery": delivery_result,
        "usage": usage_result,
        "economics": economics_result,
        "measurementStatus": "sufficient" if usage_result["status"] == "measured" else "insufficient",
        "observerStatus": "complete" if usage_result["status"] == "measured" and economics_result["status"] == "observed" else "degraded",
    }
    try:
        target = Path(args.output)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    except OSError as error:
        print(json.dumps({"schema": SCHEMA, "unit": args.unit, "observerStatus": "unavailable", "reason": type(error).__name__}, sort_keys=True))
    else:
        print(json.dumps(report, sort_keys=True, separators=(",", ":")))
    # Observation is explicitly advisory. Its missing inputs or output failure never changes delivery.
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

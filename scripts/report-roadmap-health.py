#!/usr/bin/env python3
"""Derive the seven roadmap-health results from one typed historical reading."""
from __future__ import annotations
import argparse
import json
import sys
from datetime import datetime, timedelta
from pathlib import Path

IDS = ("issue-flow", "behaviourless-repairs", "scheduling-intent", "complete-reads", "release-coherence", "artifact-trend", "evidence-growth")

def fail(message: str) -> None:
    raise ValueError(message)

def utc(value: object) -> datetime:
    if not isinstance(value, str) or not value.endswith("Z"):
        fail("timestamps must be RFC3339 UTC")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        fail("timestamps must be RFC3339 UTC")

def read_fixture(path: Path) -> dict:
    try:
        data = json.loads(path.read_text())
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read health fixture {path}: {error}")
    if data.get("schema") != "fsgg.coord.roadmap-health-input/v1":
        fail("fixture schema must be fsgg.coord.roadmap-health-input/v1")
    window = data.get("window", {})
    if not isinstance(window.get("start"), str) or not isinstance(window.get("end"), str) or utc(window["start"]) >= utc(window["end"]):
        fail("window must have ordered start and end")
    measures = data.get("measures")
    if not isinstance(measures, dict) or set(measures) != set(IDS):
        fail("fixture must contain exactly the seven typed measure inputs")
    periods = measures["issue-flow"].get("periods")
    if not isinstance(periods, list) or not periods:
        fail("issue-flow periods are required")
    for period in periods:
        if not isinstance(period, dict) or not isinstance(period.get("start"), str) or not isinstance(period.get("end"), str) or utc(period["start"]) >= utc(period["end"]) or utc(period["end"]) - utc(period["start"]) != timedelta(days=7) or not isinstance(period.get("opened"), int) or isinstance(period.get("opened"), bool) or not isinstance(period.get("closed"), int) or isinstance(period.get("closed"), bool):
            fail("issue-flow periods must be ordered typed rows")
    if any(periods[index]["end"] != periods[index + 1]["start"] for index in range(len(periods) - 1)):
        fail("issue-flow periods must be contiguous")
    return data

def row(identifier: str, verdict: str, reason: str, value=None) -> dict:
    result = {"id": identifier, "verdict": verdict, "reason": reason}
    if value is not None:
        result["value"] = value
    return result

def report(data: dict) -> dict:
    measures = data["measures"]
    periods = measures["issue-flow"]["periods"]
    issue = row("issue-flow", "met" if len(periods) >= 3 and all(p["opened"] < p["closed"] for p in periods[-3:]) else ("violated" if len(periods) >= 3 else "unverified"), "Derived from ordered contiguous period rows.", periods)
    retired = row("behaviourless-repairs", "retired", "Retired 2026-08-22 by operator-delegated host: no authoritative behaviour-changing classifier exists.")
    def binary(identifier: str, key: str, label: str) -> dict:
        value = measures[identifier].get(key)
        return row(identifier, "unverified", f"No typed {label} evidence in this reading.") if value is None else row(identifier, "violated" if value else "met", f"Derived from typed {label} input.", value)
    inventory = measures["artifact-trend"]
    baseline, current = inventory.get("baseline"), inventory.get("current")
    artifact = row("artifact-trend", "unverified", "No typed baseline/current inventory.") if not isinstance(baseline, dict) or not isinstance(current, dict) else row("artifact-trend", "met" if current["checks"] < baseline["checks"] and current["workflows"] < baseline["workflows"] else "violated", "Derived from typed baseline/current inventory.", [baseline, current])
    growth = measures["evidence-growth"]
    evidence = row("evidence-growth", "unverified", "No single typed git boundary.") if not isinstance(growth.get("generatedLines"), int) or not isinstance(growth.get("implementationLines"), int) else row("evidence-growth", "met" if growth["generatedLines"] < growth["implementationLines"] else "violated", f"Derived at {growth.get('baseline')}..{growth.get('head')}.", growth)
    return {"schema": "fsgg.coord.roadmap-health/v1", "window": data["window"], "sourceBoundary": data.get("sourceBoundary"), "measures": [issue, retired, binary("scheduling-intent", "reversed", "scheduling reversal"), binary("complete-reads", "partialDiscovered", "partial-read incident"), binary("release-coherence", "ambiguous", "ambiguous release"), artifact, evidence]}

def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True)
    parser.add_argument("--format", choices=("json",), default="json")
    args = parser.parse_args()
    try:
        print(json.dumps(report(read_fixture(args.fixture)), indent=2, sort_keys=True))
    except ValueError as error:
        print(f"report-roadmap-health: {error}", file=sys.stderr)
        return 2
    return 0

if __name__ == "__main__":
    raise SystemExit(main())

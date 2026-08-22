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
    boundary = data.get("sourceBoundary")
    if (
        not isinstance(boundary, dict)
        or set(boundary) != {"repository", "query", "asOf", "method", "resultCount"}
        or not all(isinstance(boundary.get(key), str) and boundary[key] for key in ("repository", "query", "asOf", "method"))
        or utc(boundary["asOf"]) < utc(window["end"])
    ):
        fail("sourceBoundary must identify a complete repository query observed after the window")
    integer(boundary.get("resultCount"), "sourceBoundary.resultCount")
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
    if window["start"] != periods[0]["start"] or window["end"] != periods[-1]["end"]:
        fail("root window must exactly match the issue-flow period boundary")
    return data

def row(identifier: str, verdict: str, reason: str, value=None) -> dict:
    result = {"id": identifier, "verdict": verdict, "reason": reason}
    if value is not None:
        result["value"] = value
    return result

def integer(value: object, field: str) -> int:
    if not isinstance(value, int) or isinstance(value, bool):
        fail(f"{field} must be an integer")
    return value

def report(data: dict) -> dict:
    measures = data["measures"]
    periods = measures["issue-flow"]["periods"]
    issue = row("issue-flow", "met" if len(periods) >= 3 and all(p["opened"] < p["closed"] for p in periods[-3:]) else ("violated" if len(periods) >= 3 else "unverified"), "Derived from ordered contiguous period rows.", periods)
    retired = row("behaviourless-repairs", "retired", "Retired 2026-08-22 by operator-delegated host: no authoritative behaviour-changing classifier exists.")
    def incident_measure(identifier: str, label: str) -> dict:
        value = measures[identifier]
        if not isinstance(value, dict) or set(value) != {"incidents", "census"}:
            fail(f"{identifier} must contain typed incidents and census")
        incidents, census = value["incidents"], value["census"]
        if not isinstance(incidents, list) or not isinstance(census, dict):
            fail(f"{identifier} incidents/census must be typed")
        if (
            set(census) != {"complete", "asOf", "method"}
            or not isinstance(census.get("complete"), bool)
            or not isinstance(census.get("asOf"), str)
            or not isinstance(census.get("method"), str)
            or not census["method"]
            or utc(census["asOf"]) < utc(data["window"]["end"])
        ):
            fail(f"{identifier}.census must be a typed post-window census")
        for incident in incidents:
            if (
                not isinstance(incident, dict)
                or set(incident) != {"ref", "date"}
                or not isinstance(incident.get("ref"), str)
                or not incident["ref"]
                or not isinstance(incident.get("date"), str)
                or not (utc(data["window"]["start"]) <= utc(incident["date"]) < utc(data["window"]["end"]))
            ):
                fail(f"{identifier} incidents require durable refs and dates inside the window")
        if incidents:
            return row(identifier, "violated", f"Derived from {len(incidents)} typed {label} incident(s).", incidents)
        if census["complete"]:
            return row(identifier, "met", f"Derived from a complete typed {label} census.", census)
        return row(identifier, "unverified", f"No incident is recorded, but the typed {label} census is incomplete.", census)
    inventory = measures["artifact-trend"]
    baseline, current = inventory.get("baseline"), inventory.get("current")
    if not isinstance(baseline, dict) or not isinstance(current, dict):
        artifact = row("artifact-trend", "unverified", "No typed baseline/current inventory.")
    elif not all(isinstance(scope.get(key), int) and not isinstance(scope.get(key), bool) for scope in (baseline, current) for key in ("policyImplementations", "checks", "workflows")):
        fail("artifact-trend baseline/current maps require integer policy implementations, checks, and workflows")
    else:
        artifact = row("artifact-trend", "met" if all(current[key] < baseline[key] for key in ("policyImplementations", "checks", "workflows")) else "violated", "Derived from all three typed baseline/current inventories.", [baseline, current])
    growth = measures["evidence-growth"]
    generated = growth.get("generatedLines")
    implementation_and_test = growth.get("implementationAndTestLines")
    if generated is None and implementation_and_test is None:
        evidence = row("evidence-growth", "unverified", "No single typed git boundary.")
    else:
        generated = integer(generated, "evidence-growth.generatedLines")
        implementation_and_test = integer(
            implementation_and_test,
            "evidence-growth.implementationAndTestLines",
        )
        evidence = row(
            "evidence-growth",
            "met" if generated < implementation_and_test else "violated",
            f"Derived at {growth.get('baseline')}..{growth.get('head')}.",
            growth,
        )
    return {"schema": "fsgg.coord.roadmap-health/v1", "window": data["window"], "sourceBoundary": data["sourceBoundary"], "measures": [issue, retired, incident_measure("scheduling-intent", "scheduling reversal"), incident_measure("complete-reads", "partial-read"), incident_measure("release-coherence", "ambiguous release"), artifact, evidence]}

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

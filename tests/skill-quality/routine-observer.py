#!/usr/bin/env python3
"""Exercise whole-unit accounting and the non-blocking routine observer."""

import json
import subprocess
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
TOOL = ROOT / "tools/routine-observer.py"


def run(root: Path, *arguments: str) -> tuple[subprocess.CompletedProcess[str], dict]:
    output = root / "report.json"
    result = subprocess.run([str(TOOL), *arguments, "--output", str(output)], text=True, capture_output=True)
    return result, json.loads(output.read_text()) if output.exists() else json.loads(result.stdout)


with tempfile.TemporaryDirectory(prefix="fsgg-routine-observer-") as scratch:
    root = Path(scratch)
    delivery = root / "delivery.json"
    usage = root / "usage.json"
    economics = root / "economics.json"
    delivery.write_text(json.dumps({"codeDelivery": "delivered", "pr": 7, "head": "a" * 40, "mergeCommit": "b" * 40}))
    usage.write_text(json.dumps({
        "schema": "fsgg.routine-usage/1", "unit": "R2", "scope": "whole-unit",
        "freshInputTokens": 40, "cachedInputTokens": 40, "outputTokens": 20, "reasoningTokens": 5,
        "productiveTokens": 80, "overheadTokens": 10, "unclassifiedTokens": 10,
    }))
    economics.write_text(json.dumps({
        "schema": "fsgg.coordination.qualification-cadence-report/2", "dataStatus": "available",
        "completeness": {"complete": True, "attemptsExpected": 2, "attemptsObserved": 2},
    }))

    result, report = run(root, "--unit", "R2", "--delivery", str(delivery), "--usage", str(usage), "--economics", str(economics))
    assert result.returncode == 0, result.stderr
    assert report["delivery"]["status"] == "delivered"
    assert report["measurementStatus"] == "sufficient"
    assert report["usage"]["headlineTokens"] == 100
    assert report["usage"]["conservativeOverheadRatio"] == 0.2
    assert report["usage"]["withinTwentyPercentCeiling"] is True
    assert report["observerStatus"] == "complete"

    result, report = run(root, "--unit", "R2", "--delivery", str(delivery))
    assert result.returncode == 0
    assert report["delivery"]["status"] == "delivered"
    assert report["measurementStatus"] == "insufficient"
    assert report["usage"]["status"] == "missing"
    assert report["usage"].get("conservativeOverheadRatio") is None

    usage.write_text("not-json")
    result, report = run(root, "--unit", "R2", "--delivery", str(delivery), "--usage", str(usage))
    assert result.returncode == 0, "a broken observer input blocked an already delivered unit"
    assert report["delivery"]["status"] == "delivered"
    assert report["usage"]["status"] == "missing"

    usage.write_text(json.dumps({
        "schema": "fsgg.routine-usage/1", "unit": "other", "scope": "phase",
        "freshInputTokens": 1, "cachedInputTokens": 0, "outputTokens": 0, "reasoningTokens": 0,
        "productiveTokens": 1, "overheadTokens": 0, "unclassifiedTokens": 0,
    }))
    result, report = run(root, "--unit", "R2", "--delivery", str(delivery), "--usage", str(usage))
    assert result.returncode == 0
    assert report["usage"]["status"] == "invalid"
    assert report["measurementStatus"] == "insufficient"

print("routine-observer: whole-unit joins, conservative bounds, and non-blocking failures pass")

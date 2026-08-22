#!/usr/bin/env python3
"""Contract tests for typed roadmap-health derivation."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/report-roadmap-health.py"
FIXTURE = ROOT / "tests/FS.GG.Coord.Core.Tests/fixtures/roadmap-health/roadmap-8813c463.json"
spec = importlib.util.spec_from_file_location("roadmap_health", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def invalid_fixture(source, mutate, expected=""):
    invalid = json.loads(json.dumps(source))
    mutate(invalid)
    with tempfile.TemporaryDirectory() as directory:
        bad = Path(directory) / "bad.json"
        bad.write_text(json.dumps(invalid))
        try:
            module.read_fixture(bad)
            module.report(invalid)
        except ValueError as error:
            assert expected in str(error)
        else:
            raise AssertionError("malformed typed input must fail closed")


def test_complete_inventory_and_production_route():
    source = module.read_fixture(FIXTURE)
    reading = module.report(source)
    assert [item["id"] for item in reading["measures"]] == list(module.IDS)
    assert [item["verdict"] for item in reading["measures"]] == [
        "violated", "retired", "violated", "violated", "violated", "violated", "met"
    ]
    cli = subprocess.run(
        ["python3", str(SCRIPT), "--fixture", str(FIXTURE), "--format", "json"],
        text=True,
        capture_output=True,
        check=True,
    )
    assert json.loads(cli.stdout) == reading


def test_three_period_success():
    source = module.read_fixture(FIXTURE)
    three = json.loads(json.dumps(source))
    for period in three["measures"]["issue-flow"]["periods"]:
        period["opened"], period["closed"] = 1, 2
    assert module.report(three)["measures"][0]["verdict"] == "met"


def test_weekly_window_mutation():
    source = module.read_fixture(FIXTURE)
    invalid_fixture(
        source,
        lambda item: item["measures"]["issue-flow"]["periods"][1].update(
            {"start": "2026-08-09T00:00:00Z"}
        ),
        "period",
    )


def test_typed_measure_mutations():
    source = module.read_fixture(FIXTURE)
    for mutate in (
        lambda item: item["measures"].pop("release-coherence"),
        lambda item: item["measures"]["scheduling-intent"].update({"reversed": "true"}),
        lambda item: item["measures"]["artifact-trend"].update({"current": {"checks": 53}}),
    ):
        invalid_fixture(source, mutate)


def test_integer_fields_reject_python_booleans():
    source = module.read_fixture(FIXTURE)
    for mutate in (
        lambda item: item["measures"]["issue-flow"]["periods"][0].update({"opened": True}),
        lambda item: item["measures"]["artifact-trend"]["current"].update({"checks": False}),
        lambda item: item["measures"]["evidence-growth"].update({"generatedLines": True}),
        lambda item: item["measures"]["evidence-growth"].update(
            {"implementationAndTestLines": False}
        ),
    ):
        invalid_fixture(source, mutate)


def main():
    test_complete_inventory_and_production_route()
    test_three_period_success()
    test_weekly_window_mutation()
    test_typed_measure_mutations()
    test_integer_fields_reject_python_booleans()
    print("report-roadmap-health: typed seven-measure derivation and invalid windows hold")


if __name__ == "__main__":
    main()

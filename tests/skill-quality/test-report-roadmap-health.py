#!/usr/bin/env python3
"""Contract tests for raw-evidence roadmap-health derivation."""
from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/report-roadmap-health.py"
FIXTURE = ROOT / "tests/FS.GG.Coord.Core.Tests/fixtures/roadmap-health/roadmap-8813c463.json"
ISSUES = ROOT / "tests/FS.GG.Coord.Core.Tests/fixtures/roadmap-health/issues-2026-08-22.json"
ROADMAP = ROOT / "docs/reports/2026-08-14-090508-coordination-churn-redesign-roadmap.md"
spec = importlib.util.spec_from_file_location("roadmap_health", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def invalid_fixture(source, mutate, expected=""):
    invalid = json.loads(json.dumps(source))
    invalid.pop("_issueRecords", None)
    mutate(invalid)
    with tempfile.TemporaryDirectory() as directory:
        bad = Path(directory) / "bad.json"
        bad.write_text(json.dumps(invalid))
        try:
            loaded = module.read_fixture(bad)
            module.report(loaded)
        except ValueError as error:
            assert expected in str(error)
        else:
            raise AssertionError("malformed typed input must fail closed")


def invalid_census(mutate, expected="", preserve_digest=False):
    source = json.loads(FIXTURE.read_text())
    snapshot = json.loads(ISSUES.read_text())
    mutate(snapshot)
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        source["sourceBoundary"]["snapshotPath"] = "issues.json"
        if not preserve_digest:
            source["sourceBoundary"]["recordsSha256"] = module.canonical_sha256(snapshot.get("records"))
        (root / "issues.json").write_text(json.dumps(snapshot))
        fixture = root / "fixture.json"
        fixture.write_text(json.dumps(source))
        try:
            module.read_fixture(fixture, root)
        except ValueError as error:
            assert expected in str(error)
        else:
            raise AssertionError("malformed raw census must fail closed")


def test_complete_inventory_and_production_route():
    source = module.read_fixture(FIXTURE)
    reading = module.report(source)
    assert [item["id"] for item in reading["measures"]] == list(module.IDS)
    assert [item["verdict"] for item in reading["measures"]] == [
        "violated", "retired", "violated", "violated", "violated", "violated", "met"
    ]
    assert reading["sourceBoundary"]["resultCount"] == 1210
    assert [
        (period["opened"], period["closed"])
        for period in reading["measures"][0]["value"]
    ] == [(83, 64), (146, 151), (78, 86)]
    artifact = reading["measures"][5]["value"]
    assert (artifact["checks"]["baseline"], artifact["checks"]["current"]) == (49, 54)
    assert (artifact["workflows"]["baseline"], artifact["workflows"]["current"]) == (100, 115)
    assert artifact["policyImplementations"]["verdict"] == "unverified"
    growth = reading["measures"][6]["value"]
    assert growth["generatedEvidence"]["net"] == -19694
    assert growth["implementationAndTests"]["net"] == 1972
    cli = subprocess.run(
        ["python3", str(SCRIPT), "--fixture", str(FIXTURE), "--repo", str(ROOT), "--format", "json"],
        text=True,
        capture_output=True,
        check=True,
    )
    assert json.loads(cli.stdout) == reading


def test_asserted_summaries_and_bad_git_boundaries_are_rejected():
    source = module.read_fixture(FIXTURE)
    for mutate in (
        lambda item: item["measures"]["issue-flow"].update({"periods": []}),
        lambda item: item["measures"]["artifact-trend"].update({"checks": 54}),
        lambda item: item["measures"]["evidence-growth"].update({"implementationAndTestLines": 1972}),
        lambda item: item["gitBoundary"].update({"base": ""}),
        lambda item: item["gitBoundary"].update({"head": "not-a-commit"}),
        lambda item: item["gitBoundary"].update({"head": None}),
    ):
        invalid_fixture(source, mutate)


def test_raw_census_digest_and_record_semantics_are_rejected():
    invalid_census(lambda item: item["records"].pop(), "digest", preserve_digest=True)
    invalid_census(lambda item: item["records"][1].update({"number": item["records"][0]["number"]}), "unique positive")
    invalid_census(lambda item: item["records"][0].update({"number": 0}), "unique positive")
    invalid_census(lambda item: item["records"][0].update({"number": True}), "unique positive")
    invalid_census(lambda item: item["records"][0].update({"createdAt": None}), "timestamps")
    invalid_census(lambda item: item["records"][0].update({"createdAt": "not-utc"}), "timestamps")
    invalid_census(lambda item: item["records"][0].update({"closedAt": "2020-01-01T00:00:00Z"}), "semantics")
    invalid_census(lambda item: item["records"][0].update({"createdAt": "2026-09-01T00:00:00Z"}), "semantics")
    invalid_census(lambda item: item["records"][0].update({"opened": -1}), "exactly")


def test_incident_and_census_bypasses_are_rejected():
    source = module.read_fixture(FIXTURE)
    for mutate in (
        lambda item: item["measures"]["scheduling-intent"].update({"incidents": ["x"]}),
        lambda item: item["measures"]["scheduling-intent"]["incidents"][0].update({"ref": "x"}),
        lambda item: item["measures"]["complete-reads"]["incidents"][0].update({"ref": ""}),
        lambda item: item["measures"]["complete-reads"]["incidents"][0].update({"date": "2026-07-31T23:59:59Z"}),
        lambda item: item["measures"]["release-coherence"]["census"].update({"count": -1}),
        lambda item: item["measures"]["release-coherence"]["census"].update({"complete": 1}),
        lambda item: item["measures"]["release-coherence"].update({"incidents": "known incident"}),
    ):
        invalid_fixture(source, mutate)


def test_incident_measure_requires_complete_census_for_met():
    source = module.read_fixture(FIXTURE)
    incomplete = json.loads(json.dumps(source))
    incomplete["measures"]["scheduling-intent"]["incidents"] = []
    assert module.report(incomplete)["measures"][2]["verdict"] == "unverified"
    complete = json.loads(json.dumps(incomplete))
    complete["measures"]["scheduling-intent"]["census"]["complete"] = True
    assert module.report(complete)["measures"][2]["verdict"] == "met"


def test_milestone_exit_table_is_checkbox_authority():
    roadmap = ROADMAP.read_text()
    for milestone in range(7):
        assert f"- [ ] **M{milestone} " in roadmap
        assert f"| M{milestone} |" in roadmap
    assert "The per-milestone table above is the checkbox authority" in roadmap


def test_m6_named_successor_census():
    roadmap = ROADMAP.read_text()
    m6 = next(line for line in roadmap.splitlines() if line.startswith("| M6 |"))
    assert all(subject in m6 for subject in ("`.github#266`", "`.github#2752`", "`.github#2691`"))
    assert "Three healthy cycles fail" in m6


def test_freeze_decision_records_authority():
    roadmap = ROADMAP.read_text()
    prose = " ".join(roadmap.split())
    assert "Freeze decision state: **approved** by the operator on **2026-08-17**" in prose
    assert "board analyst `avocet-bb9a`" in prose
    assert "It remains in force until the seven" in prose


def test_retirement_is_explicit_in_document():
    roadmap = ROADMAP.read_text()
    prose = " ".join(roadmap.split())
    assert "Measure 2 is retired in this document by the" in prose
    assert "operator-delegated host, effective 2026-08-22, with state `retired`" in prose
    assert "no authoritative behaviour-changing classifier exists" in prose


def main():
    test_complete_inventory_and_production_route()
    test_asserted_summaries_and_bad_git_boundaries_are_rejected()
    test_raw_census_digest_and_record_semantics_are_rejected()
    test_incident_and_census_bypasses_are_rejected()
    test_incident_measure_requires_complete_census_for_met()
    test_milestone_exit_table_is_checkbox_authority()
    test_m6_named_successor_census()
    test_freeze_decision_records_authority()
    test_retirement_is_explicit_in_document()
    print("report-roadmap-health: raw census, exact Git derivation, and invalid evidence controls hold")


if __name__ == "__main__":
    main()

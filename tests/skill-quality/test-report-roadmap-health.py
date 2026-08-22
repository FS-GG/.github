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
GIT_OBJECTS = (
    "cb33188c46ee51825315e79af9ef0c54223bce07",
    "8813c46303588af6f159ef70bec0869e41266a64",
)
spec = importlib.util.spec_from_file_location("roadmap_health", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def ensure_historical_git_objects():
    """Materialize the two exact positive-control commits in shallow CI clones."""
    for revision in GIT_OBJECTS:
        present = subprocess.run(
            ["git", "-C", str(ROOT), "cat-file", "-e", f"{revision}^{{commit}}"],
            capture_output=True,
        )
        if present.returncode != 0:
            subprocess.run(
                ["git", "-C", str(ROOT), "fetch", "--no-tags", "--depth=1", "origin", revision],
                check=True,
            )


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
    ensure_historical_git_objects()
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
    assert module.incident_measure(incomplete, "scheduling-intent", "scheduling reversal")["verdict"] == "unverified"
    complete = json.loads(json.dumps(incomplete))
    complete["measures"]["scheduling-intent"]["census"]["complete"] = True
    assert module.incident_measure(complete, "scheduling-intent", "scheduling reversal")["verdict"] == "met"


def test_milestone_exit_table_is_checkbox_authority():
    source = module.read_fixture(FIXTURE)
    scores = module.report(source, roadmap=ROADMAP.read_text())["milestoneScores"]
    assert [score["id"] for score in scores] == [f"M{index}" for index in range(7)]
    assert [score["verdict"] for score in scores] == [
        "unverified", "violated", "violated", "violated", "unverified", "violated", "violated"
    ]
    assert all(score["checkboxExpected"] is False for score in scores)
    assert all(
        predicate["exitPredicate"] and predicate["gap"] and predicate["evidence"]
        for score in scores
        for predicate in score["predicates"]
    )
    assert [[predicate["id"] for predicate in score["predicates"]] for score in scores] == [
        ["main-green", "repairs-settled", "baseline-reproducible"],
        ["reconciliation-intent"],
        ["complete-read-boundary"],
        ["coherent-release"],
        ["structured-decisions"],
        ["artifact-decline"],
        ["healthy-cycles", "no-open-successor"],
    ]
    assert scores[0]["predicates"][0] == {
        "id": "main-green",
        "exitPredicate": "Main has no standing red checks",
        "verdict": "unverified",
        "gap": "No complete required-check census is bound to the window.",
        "evidence": ["milestoneEvidence.M0.mainChecksCensusComplete"],
    }


def test_milestone_authority_mutations_fail_closed():
    source = module.read_fixture(FIXTURE)
    expected = module.report(source, roadmap=ROADMAP.read_text())["milestoneScores"]

    def rejected(mutate, roadmap=None):
        candidate = json.loads(json.dumps(expected))
        mutate(candidate)
        try:
            module.validate_milestone_scores(candidate, expected, roadmap or ROADMAP.read_text())
        except ValueError:
            return
        raise AssertionError("mutated milestone authority must fail closed")

    rejected(lambda scores: scores[0].update({"verdict": "met", "checkboxExpected": True}))
    rejected(lambda scores: scores[1]["predicates"][0].update({"gap": "wrong", "evidence": ["x"]}))
    rejected(lambda scores: None, ROADMAP.read_text().replace("- [ ] **M2", "- [x] **M2", 1))
    rejected(lambda scores: scores.pop(3))
    rejected(lambda scores: scores.__setitem__(3, json.loads(json.dumps(scores[2]))))
    rejected(lambda scores: scores[6].update({"verdict": "met", "checkboxExpected": True}))


def test_m6_named_successor_census():
    source = module.read_fixture(FIXTURE)
    m6 = module.report(source)["milestoneScores"][6]
    successor = next(predicate for predicate in m6["predicates"] if predicate["id"] == "no-open-successor")
    assert successor["verdict"] == "violated"
    assert all(subject in " ".join(successor["evidence"]) for subject in ("/266:", "/2752:", "/2691:"))
    assert m6["checkboxExpected"] is False


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
    test_milestone_authority_mutations_fail_closed()
    test_m6_named_successor_census()
    test_freeze_decision_records_authority()
    test_retirement_is_explicit_in_document()
    print("report-roadmap-health: raw census, exact Git derivation, and invalid evidence controls hold")


if __name__ == "__main__":
    main()

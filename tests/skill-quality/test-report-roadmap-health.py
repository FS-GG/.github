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


def invalid_roadmap(mutate, expected="milestone"):
    roadmap = mutate(ROADMAP.read_text())
    with tempfile.TemporaryDirectory() as directory:
        bad = Path(directory) / "roadmap.md"
        bad.write_text(roadmap)
        completed = subprocess.run(
            ["python3", str(SCRIPT), "--fixture", str(FIXTURE), "--repo", str(ROOT), "--roadmap", str(bad)],
            text=True,
            capture_output=True,
        )
        assert completed.returncode == 2
        assert expected in completed.stderr


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
    invalid_census(lambda item: item["records"].__setitem__(slice(None), [record for record in item["records"] if record["number"] != 266]), "successor inventory")


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
        "gap": "No complete required-check census source is bound to the window.",
        "evidence": ["missing-source:required-check-census"],
    }
    try:
        module.milestone_predicate("main-green", "Main has no standing red checks", "met", ["missing-source:required-check-census"])
    except ValueError as error:
        assert "cannot derive milestone gap" in str(error)
    else:
        raise AssertionError("a verdict cannot independently select a contradictory gap")
    assert module.parse_milestone_table(ROADMAP.read_text()) == scores


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


def test_roadmap_score_table_mutations_fail_in_production_cli():
    invalid_roadmap(lambda text: text.replace("| M0 | unverified | false |", "| M0 | met | true |", 1))
    invalid_roadmap(lambda text: text.replace("No complete required-check census source is bound to the window.", "", 1))
    invalid_roadmap(lambda text: text.replace('"id":"complete-read-boundary"', '"id":"wrong-predicate"', 1))
    invalid_roadmap(lambda text: text.replace("| M4 | unverified | false |", "| M4 | violated | false |", 1))
    invalid_roadmap(lambda text: text.replace("https://github.com/FS-GG/.github/issues/266:open", "https://github.com/FS-GG/.github/issues/266:closed", 1))
    invalid_roadmap(lambda text: text.replace("- [ ] **M2", "- [x] **M2", 1), "checkbox")

    def m0_met_without_gap(text):
        return text.replace("| M0 | unverified | false |", "| M0 | met | true |", 1).replace(
            '"gap":"No complete required-check census source is bound to the window.","id":"main-green","verdict":"unverified"',
            '"gap":"","id":"main-green","verdict":"met"',
            1,
        )

    invalid_roadmap(m0_met_without_gap)


def test_every_typed_table_field_is_validated():
    source = module.read_fixture(FIXTURE)
    scores = module.report(source, roadmap=ROADMAP.read_text())["milestoneScores"]

    def rejected(mutated):
        roadmap = module.MILESTONE_TABLE.sub(module.render_milestone_table(mutated), ROADMAP.read_text())
        try:
            module.report(source, ROOT, roadmap)
        except ValueError as error:
            assert "milestone" in str(error)
        else:
            raise AssertionError("every typed roadmap table field must be validated")

    for score_index, score in enumerate(scores):
        for field, value in (("id", score["id"] + "x"), ("verdict", "met"), ("checkboxExpected", not score["checkboxExpected"])):
            mutated = json.loads(json.dumps(scores))
            mutated[score_index][field] = value
            rejected(mutated)
        for predicate_index, predicate in enumerate(score["predicates"]):
            for field, value in (
                ("id", predicate["id"] + "x"),
                ("exitPredicate", predicate["exitPredicate"] + " wrong"),
                ("verdict", "met" if predicate["verdict"] != "met" else "violated"),
                ("gap", predicate["gap"] + " wrong"),
                ("evidence", predicate["evidence"] + ["wrong"]),
            ):
                mutated = json.loads(json.dumps(scores))
                mutated[score_index]["predicates"][predicate_index][field] = value
                rejected(mutated)


def test_legacy_verdict_inputs_and_co_mutations_fail_closed():
    source = module.read_fixture(FIXTURE)
    legacy = {
        "M0": {"mainChecksCensusComplete": True, "openRepairCensusComplete": True},
        "M4": {"effectiveDecisionCensusComplete": True},
        "M6": {
            "asOf": source["sourceBoundary"]["asOf"],
            "successors": [
                {"ref": f"https://github.com/FS-GG/.github/issues/{number}", "state": "closed"}
                for number in (266, 2752, 2691)
            ],
        },
    }
    invalid_fixture(source, lambda item: item.update({"milestoneEvidence": legacy}), "verdict inputs are forbidden")

    # Co-mutating an asserted boolean authority and its checkbox still fails at the raw-input boundary.
    mutated = json.loads(FIXTURE.read_text())
    mutated["milestoneEvidence"] = legacy
    with tempfile.TemporaryDirectory() as directory:
        fixture = Path(directory) / "fixture.json"
        roadmap = Path(directory) / "roadmap.md"
        fixture.write_text(json.dumps(mutated))
        roadmap.write_text(ROADMAP.read_text().replace("- [ ] **M0", "- [x] **M0", 1))
        completed = subprocess.run(
            ["python3", str(SCRIPT), "--fixture", str(fixture), "--repo", str(ROOT), "--roadmap", str(roadmap)],
            text=True,
            capture_output=True,
        )
        assert completed.returncode == 2
        assert "verdict inputs are forbidden" in completed.stderr


def test_all_closed_successor_claim_contradicting_raw_projection_fails():
    source = json.loads(FIXTURE.read_text())
    snapshot = json.loads(ISSUES.read_text())
    for record in snapshot["records"]:
        if record["number"] in {266, 2691}:
            record["closedAt"] = "2026-08-22T12:00:00Z"
    source["sourceBoundary"]["snapshotPath"] = "issues.json"
    source["sourceBoundary"]["recordsSha256"] = module.canonical_sha256(snapshot["records"])
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "issues.json").write_text(json.dumps(snapshot))
        fixture = root / "fixture.json"
        fixture.write_text(json.dumps(source))
        loaded = module.read_fixture(fixture, root)
        try:
            module.report(loaded, ROOT, ROADMAP.read_text())
        except ValueError as error:
            assert "milestone score table" in str(error)
        else:
            raise AssertionError("an all-closed successor derivation cannot match the committed open-state table")


def test_m6_health_cycles_compose_every_active_measure():
    source = module.read_fixture(FIXTURE)
    reading = module.report(source, roadmap=ROADMAP.read_text())
    measures = json.loads(json.dumps(reading["measures"]))
    issue_flow = next(measure for measure in measures if measure["id"] == "issue-flow")
    issue_flow["verdict"] = "met"
    issue_flow["value"] = [
        {"start": f"period-{index}", "end": f"period-{index + 1}", "opened": 1, "closed": 2}
        for index in range(3)
    ]
    all_closed_source = json.loads(json.dumps(source))
    for record in all_closed_source["_issueRecords"]:
        if record["number"] in {266, 2691}:
            record["closedAt"] = "2026-08-22T12:00:00Z"
    m6 = module.derive_milestone_scores(all_closed_source, measures)[6]
    assert m6["predicates"][0]["verdict"] == "violated"
    assert m6["predicates"][1]["verdict"] == "met"
    assert m6["verdict"] == "violated"

    all_met = json.loads(json.dumps(measures))
    for measure in all_met:
        if measure["verdict"] != "retired":
            measure["verdict"] = "met"
    assert module.health_cycles_predicate(all_met)["verdict"] == "met"
    next(measure for measure in all_met if measure["id"] == "complete-reads")["verdict"] = "unverified"
    assert module.health_cycles_predicate(all_met)["verdict"] == "unverified"


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
    test_roadmap_score_table_mutations_fail_in_production_cli()
    test_every_typed_table_field_is_validated()
    test_legacy_verdict_inputs_and_co_mutations_fail_closed()
    test_all_closed_successor_claim_contradicting_raw_projection_fails()
    test_m6_health_cycles_compose_every_active_measure()
    test_m6_named_successor_census()
    test_freeze_decision_records_authority()
    test_retirement_is_explicit_in_document()
    print("report-roadmap-health: raw census, exact Git derivation, and invalid evidence controls hold")


if __name__ == "__main__":
    main()

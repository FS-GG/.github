#!/usr/bin/env python3
"""Offline measurement controls: missing evidence cannot become measured savings."""

import importlib.util
import copy
import json
from pathlib import Path
import tempfile
import unittest
import yaml


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("measurement", ROOT / "scripts/measure-ci-fanout.py")
measurement = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(measurement)
START, END = "2026-11-01T00:00:00Z", "2026-11-02T00:00:00Z"

FOLDED = {
    "closing-keywords": {"declared-closes": "closing-keywords"},
    "kit-published-coherence": {"pr-arm": "kit-published-coherence", "published": "kit-published-coherence"},
    "job-uniqueness-coherence": {"job-names-stable": "job-uniqueness"},
    "reusable-job-id-coherence": {"job-ids-stable": "reusable-job-ids"},
    "scheduled-engine-provisioning": {"provisioned": "scheduled-engine-provisioning"},
}


def validate_fixture_boundary(document, expected):
    """Independent wiring invariant: no production path can lose its fixture."""
    jobs = document["jobs"]
    assert set(jobs) == set(expected), "fixture consolidation changed the check job names"
    events = document.get("on", document.get(True, {}))
    assert "workflow_call" not in events, "this consolidation must not remove a reusable job API"
    for name, suite in expected.items():
        job = jobs[name]
        assert not job.get("continue-on-error"), "job masks fixture failure"
        steps = job["steps"]
        fixtures = [i for i, step in enumerate(steps) if step.get("run") == f"bash tests/{suite}/run.sh"]
        assert len(fixtures) == 1, "fixture missing or duplicated"
        index = fixtures[0]
        assert not steps[index].get("continue-on-error"), "fixture failure is masked"
        assert "if" not in steps[index], "fixture can be omitted"
        production = [i for i, step in enumerate(steps) if "scripts/check-" in step.get("run", "")]
        assert production and index < min(production), "production runs before its fixture"
        assert all(not steps[i].get("continue-on-error") for i in production), "production failure is masked"


def run(identity=1, **extra):
    return dict(id=identity, event="pull_request", head_sha="a" * 40,
                path=".github/workflows/example.yml", created_at="2026-11-01T01:00:00Z",
                updated_at="2026-11-01T01:02:00Z", **extra)


def job(**extra):
    result = dict(created_at="2026-11-01T01:00:00Z", started_at="2026-11-01T01:00:30Z",
                  completed_at="2026-11-01T01:02:00Z", runner_name="runner", conclusion="success")
    result.update(extra)
    return result


class MeasurementTests(unittest.TestCase):
    def test_shipped_consolidation_keeps_fixture_before_production(self):
        for workflow, expected in FOLDED.items():
            with self.subTest(workflow=workflow):
                document = yaml.safe_load((ROOT / f".github/workflows/{workflow}.yml").read_text())
                validate_fixture_boundary(document, expected)
                if workflow == "closing-keywords":
                    steps = document["jobs"]["declared-closes"]["steps"]
                    fixture = next(i for i, step in enumerate(steps) if step.get("id") == "fixture")
                    self.assertTrue(all("github.event_name == 'pull_request'" in step.get("if", "")
                                        for step in steps[fixture + 1:]))
                if workflow == "kit-published-coherence":
                    self.assertEqual("github.event_name == 'pull_request'", document["jobs"]["pr-arm"]["if"])
                    self.assertEqual("github.event_name != 'pull_request'", document["jobs"]["published"]["if"])

    def test_boundary_probes_catch_missing_masked_reordered_or_conditional_fixtures(self):
        probes = 0
        for workflow, expected in FOLDED.items():
            source = yaml.safe_load((ROOT / f".github/workflows/{workflow}.yml").read_text())
            for name, suite in expected.items():
                for defect in ("missing", "masked", "reordered", "conditional", "masked-job"):
                    mutant = copy.deepcopy(source)
                    job = mutant["jobs"][name]
                    steps = job["steps"]
                    index = next(i for i, step in enumerate(steps) if step.get("run") == f"bash tests/{suite}/run.sh")
                    if defect == "missing":
                        steps.pop(index)
                    elif defect == "masked":
                        steps[index]["continue-on-error"] = True
                    elif defect == "reordered":
                        steps.append(steps.pop(index))
                    elif defect == "conditional":
                        steps[index]["if"] = "false"
                    else:
                        job["continue-on-error"] = True
                    with self.subTest(workflow=workflow, job=name, defect=defect):
                        with self.assertRaises(AssertionError):
                            validate_fixture_boundary(mutant, expected)
                    probes += 1
        self.assertEqual(30, probes)

    def test_absent_job_evidence_is_unknown(self):
        result = measurement.summarize([run()], {}, START, END)
        self.assertIsNone(result["observedRunnerMinutes"])
        self.assertIsNone(result["queueSeconds"]["p50"])
        self.assertEqual(1, result["jobRunsWithoutObservations"])

    def test_job_queue_and_runner_time_are_separate(self):
        result = measurement.summarize([run()], {"1": [job()]}, START, END)
        self.assertEqual(30, result["queueSeconds"]["p50"])
        self.assertEqual(1.5, result["observedRunnerMinutes"])

    def test_skipped_and_partial_and_invalid_jobs_do_not_create_zero_samples(self):
        result = measurement.summarize([run()], {"1": [
            job(conclusion="skipped"), job(runner_name=""), job(started_at=None),
            job(completed_at="2026-11-01T00:00:00Z"),
        ]}, START, END)
        self.assertIsNone(result["observedRunnerMinutes"])
        self.assertEqual(2, result["workflows"][0]["jobsWithoutCompleteTiming"])
        self.assertEqual(1, result["workflows"][0]["invalidTimingJobs"])

    def test_end_is_exclusive_and_non_pr_events_are_excluded(self):
        rows = [run()]
        for identity, changes in [(2, {"created_at": END}), (3, {"event": "push"})]:
            value = run(identity); value.update(changes); rows.append(value)
        self.assertEqual(1, measurement.summarize(rows, {}, START, END)["runs"])

    def test_same_sha_is_not_called_a_push(self):
        rows = [run(), run(2)]
        result = measurement.summarize(rows, {}, START, END)
        self.assertEqual(1, result["headPopulations"])
        self.assertEqual(2, result["runsPerHeadPopulation"]["p50"])
        self.assertNotIn("pushes", result)

    def test_duplicate_pages_are_deduplicated_and_conflicting_identity_refuses(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runs.json"
            path.write_text(json.dumps([run()]))
            rows, _ = measurement.load_runs([path, path])
            self.assertEqual(1, len(rows))
            other = Path(directory) / "other.json"
            changed = run(); changed["head_sha"] = "b" * 40
            other.write_text(json.dumps([changed]))
            with self.assertRaisesRegex(ValueError, "conflicting head_sha"):
                measurement.load_runs([path, other])

    def test_filter_replay_needs_complete_files_and_preserves_unknown(self):
        filters = {(run()["path"], "pull_request"): {
            "unsupported": False, "patterns": ["scripts/**"],
            "selects": lambda p, patterns: p.startswith("scripts/"),
        }}
        for files, expected in [(None, "unknown"), ({"complete": False, "paths": []}, "unknown"),
                                ({"complete": True, "paths": ["README.md"]}, "excluded"),
                                ({"complete": True, "paths": ["scripts/a.py"]}, "matched")]:
            result = measurement.summarize([run()], {}, START, END, filters, {"a" * 40: files})
            self.assertEqual({expected: 1}, result["workflows"][0]["currentFilterReplay"])

    def test_current_filter_reader_reuses_directory_sensitive_globs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "scripts").mkdir()
            (root / "scripts/check-paths-coherence.py").write_text(
                (ROOT / "scripts/check-paths-coherence.py").read_text())
            (root / ".github/workflows").mkdir(parents=True)
            path = root / run()["path"]
            path.write_text('on:\n  pull_request:\n    paths: ["src/*"]\njobs: {}\n')
            filters = measurement.workflow_filters(root)
            files = {"a" * 40: {"complete": True, "paths": ["src/deep/a.fs"]}}
            result = measurement.summarize([run()], {}, START, END, filters, files)
            self.assertEqual({"excluded": 1}, result["workflows"][0]["currentFilterReplay"])
            path.write_text('on:\n  pull_request:\n    paths: ["!src/**"]\njobs: {}\n')
            result = measurement.summarize([run()], {}, START, END, measurement.workflow_filters(root), files)
            self.assertEqual({"unknown": 1}, result["workflows"][0]["currentFilterReplay"])

    def test_invalid_window_and_naive_times_refuse(self):
        for start, end in [(END, START), (START, START), ("2026-11-01", END)]:
            with self.assertRaises(ValueError):
                measurement.summarize([], {}, start, end)


if __name__ == "__main__":
    unittest.main()

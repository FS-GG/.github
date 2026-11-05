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
    "reusable-job-id-coherence": {"job-contexts": ("job-uniqueness", "reusable-job-ids")},
    "scheduled-engine-provisioning": {"provisioned": "scheduled-engine-provisioning"},
}


def validate_fixture_boundary(document, expected):
    """Independent wiring invariant: no production path can lose its fixture."""
    jobs = document["jobs"]
    assert set(jobs) == set(expected), "fixture consolidation changed the check job names"
    events = document.get("on", document.get(True, {}))
    assert "workflow_call" not in events, "this consolidation must not remove a reusable job API"
    for name, suites in expected.items():
        job = jobs[name]
        assert not job.get("continue-on-error"), "job masks fixture failure"
        steps = job["steps"]
        for suite in (suites,) if isinstance(suites, str) else suites:
            fixtures = [i for i, step in enumerate(steps) if step.get("run") == f"bash tests/{suite}/run.sh"]
            assert len(fixtures) == 1, "fixture missing or duplicated"
            index = fixtures[0]
            assert not steps[index].get("continue-on-error"), "fixture failure is masked"
            production = [i for i, step in enumerate(steps) if f"scripts/check-{suite}.py" in step.get("run", "")]
            assert production and index < min(production), "production missing or runs before its fixture"
            assert len(production) == (2 if suite == "kit-published-coherence" else 1), "one production arm is missing"
            assert all(not steps[i].get("continue-on-error") for i in production), "production failure is masked"
            if name == "job-contexts":
                assert "if" not in job, "combined source verdict can be skipped"
                fixture_guard = "${{ !cancelled() && steps.checkout.outcome == 'success' && steps.policy_python.outcome == 'success' }}"
                assert steps[index].get("if") == fixture_guard, "fixture loses shared prerequisites or independence"
                assert steps[0].get("id") == "checkout" and steps[0].get("uses") == "actions/checkout@v7", "checkout prerequisite is not bound"
                assert steps[1].get("id") == "policy_python" and steps[1].get("uses") == "./.github/actions/setup-policy-python", "Python prerequisite is not bound"
                gate = "uniqueness" if suite == "job-uniqueness" else "reusable"
                guard = "${{ !cancelled() && steps." + gate + "_fixture.outcome == 'success' }}"
                assert steps[index].get("id") == gate + "_fixture", "fixture result is not bound"
                assert len(production) == 1 and steps[production[0]].get("id") == gate, "production result is not bound"
                assert steps[production[0]].get("if") == guard, "production can be omitted or aborted by the independent gate"
            else:
                assert "if" not in steps[index], "fixture can be omitted"


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
            for name, suites in expected.items():
              for suite in (suites,) if isinstance(suites, str) else suites:
                defects = ["missing", "masked", "reordered", "conditional", "masked-job", "missing-gate", "masked-gate"]
                if name == "job-contexts":
                    defects += ["missing-checkout-prerequisite", "missing-python-prerequisite"]
                for defect in defects:
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
                    elif defect == "masked-job":
                        job["continue-on-error"] = True
                    elif defect == "missing-checkout-prerequisite":
                        steps[index]["if"] = steps[index]["if"].replace(" && steps.checkout.outcome == 'success'", "")
                    elif defect == "missing-python-prerequisite":
                        steps[index]["if"] = steps[index]["if"].replace(" && steps.policy_python.outcome == 'success'", "")
                    else:
                        production = next(i for i, step in enumerate(steps) if f"scripts/check-{suite}.py" in step.get("run", ""))
                        if defect == "missing-gate":
                            steps.pop(production)
                        else:
                            steps[production]["continue-on-error"] = True
                    with self.subTest(workflow=workflow, job=name, defect=defect):
                        with self.assertRaises(AssertionError):
                            validate_fixture_boundary(mutant, expected)
                    probes += 1
        self.assertEqual(46, probes)

    def test_combined_events_preserve_running_and_pending_head_validation(self):
        document = yaml.safe_load((ROOT / ".github/workflows/reusable-job-id-coherence.yml").read_text())
        events = document.get("on", document.get(True))
        self.assertEqual({"opened", "synchronize", "reopened", "edited", "labeled", "unlabeled"},
                         set(events["pull_request"]["types"]))
        self.assertEqual("Job contexts: unique names and stable reusable IDs", document["jobs"]["job-contexts"]["name"])
        validate_fixture_boundary(document, FOLDED["reusable-job-id-coherence"])
        spec = importlib.util.spec_from_file_location("concurrency", ROOT / "scripts/check-pr-concurrency.py")
        model = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(model)
        block, workflow = document["concurrency"], "reusable-job-id-coherence"
        for action in ("opened", "synchronize", "reopened"):
            original = model.pr(7, "a" * 40, "pull_request", action)
            for metadata in ("edited", "labeled", "unlabeled"):
                arriving = model.pr(7, "a" * 40, "pull_request", metadata)
                self.assertEqual("none", model.displaced(workflow, block, original, arriving))
                self.assertFalse(model.pending_replaced(workflow, block, original, arriving))
            newer = model.pr(7, "b" * 40, "pull_request", "synchronize")
            self.assertEqual("cancelled", model.displaced(workflow, block, original, newer))
        # All three bad policies lose the original pending source validation;
        # disabling cancellation alone does not protect a queued head run.
        bad = copy.deepcopy(block)
        bad["group"] = "reusable-job-id-coherence-${{ github.event.pull_request.number || github.run_id }}"
        variants = [bad, dict(bad, **{"cancel-in-progress": False})]
        bad = copy.deepcopy(block)
        bad["group"] = bad["group"].replace('"reopened"]', '"reopened","edited","labeled","unlabeled"]')
        variants.append(bad)
        for bad in variants:
            head = model.pr(7, "a" * 40, "pull_request", "opened")
            edit = model.pr(7, "a" * 40, "pull_request", "edited")
            self.assertTrue(model.pending_replaced(workflow, bad, head, edit))

    def test_concurrency_filters_cover_the_actual_governed_inventory(self):
        spec = importlib.util.spec_from_file_location("concurrency", ROOT / "scripts/check-pr-concurrency.py")
        model = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(model)
        spec = importlib.util.spec_from_file_location("paths", ROOT / "scripts/check-paths-coherence.py")
        paths = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(paths)
        document = yaml.safe_load((ROOT / ".github/workflows/pr-concurrency-coherence.yml").read_text())
        governed = [f".github/workflows/{name}.yml" for name in model.GOVERNED + model.PR_ONLY]
        self.assertTrue(governed)
        for path in governed:
            self.assertTrue((ROOT / path).is_file(), path)

        def missing(candidate):
            events = candidate.get("on", candidate.get(True))
            return {(event, path) for event in ("pull_request", "push") for path in governed
                    if not paths.selects(path, events[event]["paths"])}

        self.assertEqual(set(), missing(document))
        # The same instrument must notice a removed, known-present subject in
        # either trigger; a successful YAML parse alone cannot establish closure.
        known = ".github/workflows/always-gates.yml"
        for event in ("pull_request", "push"):
            mutant = copy.deepcopy(document)
            mutant.get("on", mutant.get(True))[event]["paths"].remove(known)
            self.assertEqual({(event, known)}, missing(mutant))

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

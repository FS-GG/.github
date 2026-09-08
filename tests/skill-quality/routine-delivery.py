#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import hashlib
import json
import pathlib
import subprocess
import sys
import unittest
from dataclasses import asdict


ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "routine_delivery", ROOT / "tools/routine-delivery.py"
)
assert SPEC and SPEC.loader
MODULE = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)

HEAD = "a" * 40
MERGE = "b" * 40


def opened(head: str = HEAD) -> dict:
    return {
        "head": {"sha": head}, "state": "open", "draft": False, "merged": False,
        "merged_at": None, "mergeable": True, "mergeable_state": "clean",
        "base": {"ref": "main", "sha": "d" * 40},
    }


def merged(head: str = HEAD) -> dict:
    return {
        "head": {"sha": head}, "state": "closed", "draft": False, "merged": True,
        "merged_at": "2026-09-07T00:00:00Z", "merge_commit_sha": MERGE,
        "base": {"ref": "main", "sha": "d" * 40},
    }


class FakeApi:
    def __init__(self, reads: list[dict], writes: list[object] | None = None,
                 runs: list[dict] | None = None, selections: dict[int, bytes] | None = None,
                 jobs: list[dict] | None = None, job_reads: list[list[dict]] | None = None):
        self.reads = iter(reads)
        self.writes = iter(writes or [])
        self.attempts = 0
        self.runs = runs or []
        self.selections = selections or {}
        self.jobs = jobs or []
        self.job_reads = iter(job_reads) if job_reads is not None else None

    def get_pr(self, repo: str, pr: int) -> dict:
        return next(self.reads)

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict:
        self.attempts += 1
        value = next(self.writes)
        if isinstance(value, BaseException):
            raise value
        return value

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict]:
        return self.runs

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None:
        return self.selections.get(run_id)

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict]:
        return next(self.job_reads) if self.job_reads is not None else self.jobs


def run(status: str = "in_progress", conclusion: str | None = None, run_id: int = 7) -> dict:
    return {"id": run_id, "head_sha": HEAD, "status": status, "conclusion": conclusion,
            "updated_at": "2026-09-08T00:00:00Z", "run_attempt": 1}


def selection(disposition: str) -> bytes:
    prior = None
    empty = False
    if disposition == "reused":
        prior = {"candidateObligationSha256": "1" * 64, "runId": 4, "attempt": 1,
                 "executedReceiptSha256": "2" * 64, "completedAt": "2020-09-07T00:00:00Z",
                 "expiresAt": "2099-10-07T00:00:00Z", "authentic": True, "complete": True}
        empty = True
    value = {
        "schema": "fsgg.coordination.qualification-selection/1",
        "candidateObligationSha256": "3" * 64,
        "disposition": disposition,
        "reason": "fixture",
        "prior": prior,
        "semanticDelta": {"evaluatorSha256": "4" * 64, "deltaSha256": "5" * 64, "empty": empty},
        "bindingCorrespondenceSha256": None,
        "coherentRunPending": True,
        "coherentState": "pending",
    }
    payload = json.dumps(value, separators=(",", ":")).encode()
    value["selectionSha256"] = hashlib.sha256(payload).hexdigest()
    return json.dumps(value, separators=(",", ":")).encode() + b"\n"


class RoutineDeliveryTests(unittest.TestCase):
    def call(self, api: FakeApi, *, apply: bool = True, publication: bool = False,
             coherent: bool = False):
        return MODULE.summarize(
            api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=publication, apply=apply,
            coherent_workflow="optimistic.yml" if coherent else None,
        )

    def test_dry_run_is_ready_without_a_write(self):
        api = FakeApi([opened()])
        code, result = self.call(api, apply=False)
        self.assertEqual((code, result.outcome, api.attempts), (0, "ready", 0))
        self.assertEqual((asdict(result)["expectedHead"], asdict(result)["observedHead"]), (HEAD, HEAD))

    def test_changed_head_refuses_before_a_write(self):
        api = FakeApi([opened("c" * 40)])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 0))
        self.assertIn("changed head", result.reason)
        self.assertEqual(asdict(result)["observedHead"], "c" * 40)

    def test_success_requires_native_merged_readback(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, result = self.call(api, publication=True)
        self.assertEqual((code, result.codeDelivery, result.publication), (0, "delivered", "pending"))
        self.assertEqual((result.mergeCommit, result.attempts), (MERGE, 1))
        self.assertEqual(asdict(result)["observedHead"], HEAD)

    def test_ambiguous_write_reads_back_before_retry(self):
        api = FakeApi([opened(), merged()], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered-after-readback", 1))
        self.assertEqual(asdict(result)["expectedHead"], HEAD)

    def test_definitely_unmerged_readback_allows_one_retry(self):
        api = FakeApi(
            [opened(), opened(), merged()],
            [MODULE.AmbiguousWrite("timeout"), {"merged": True, "sha": MERGE}],
        )
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered", 2))

    def test_two_ambiguous_writes_stop_indeterminate(self):
        api = FakeApi(
            [opened(), opened(), opened()],
            [MODULE.AmbiguousWrite("timeout"), MODULE.AmbiguousWrite("timeout")],
        )
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 2))
        self.assertEqual(asdict(result)["observedHead"], HEAD)

    def test_retry_requires_current_native_merge_eligibility(self):
        blocked = {**opened(), "mergeable_state": "blocked"}
        api = FakeApi([opened(), blocked], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 1))
        self.assertIn("blocked", result.reason)

    def test_current_candidate_waits_for_coherent_pass(self):
        api = FakeApi([opened()], runs=[run()], selections={7: selection("current")})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.outcome, result.validationDisposition,
                          result.coherentValidation, api.attempts),
                         (2, "refused", "current", "pending", 0))

    def test_valid_reuse_can_merge_while_coherent_run_is_pending(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}],
                      [run()], {7: selection("reused")})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.codeDelivery, result.validationDisposition,
                          result.coherentValidation, api.attempts),
                         (0, "delivered", "reused", "pending", 1))

    def test_missing_or_tampered_reuse_cannot_bypass_current_run(self):
        bad = selection("reused").replace(b'"reason":"fixture"', b'"reason":"tampered"')
        api = FakeApi([opened()], runs=[run()], selections={7: bad})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.validationDisposition, api.attempts), (2, "invalid", 0))

    def test_current_candidate_can_merge_after_coherent_pass(self):
        completed = run("completed", "success")
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}],
                      [completed], {7: selection("current")})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.codeDelivery, result.coherentValidation),
                         (0, "delivered", "passed"))

    def test_merged_reuse_stays_pending_without_becoming_disputed(self):
        api = FakeApi([merged()], runs=[run()], selections={7: selection("reused")})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.codeDelivery, result.coherentValidation),
                         (0, "delivered", "pending"))

    def test_late_coherent_failure_marks_merged_delivery_disputed(self):
        failed = run("completed", "failure")
        api = FakeApi([merged()], runs=[failed], selections={7: selection("reused")})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.codeDelivery, result.coherentValidation),
                         (4, "delivered", "disputed"))

    def test_deferred_or_failed_selection_never_launches_delivery(self):
        for disposition in ("deferred", "failed"):
            with self.subTest(disposition=disposition):
                api = FakeApi([opened()], runs=[run()], selections={7: selection(disposition)})
                code, result = self.call(api, coherent=True)
                self.assertEqual((code, result.validationDisposition, api.attempts),
                                 (2, disposition, 0))

    def test_failed_partition_blocks_reuse_before_run_finishes(self):
        jobs = [{"status": "completed", "conclusion": "failure", "name": "run-partition (3)"}]
        api = FakeApi([opened()], runs=[run()], selections={7: selection("reused")}, jobs=jobs)
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.validationDisposition, result.coherentValidation, api.attempts),
                         (2, "failed", "failed", 0))

    def test_failure_after_unsuccessful_merge_attempt_is_not_called_delivered(self):
        failure = {"status": "completed", "conclusion": "failure", "name": "run-partition (3)"}
        api = FakeApi([opened(), opened()], [{"merged": False}], [run()], {7: selection("reused")},
                      job_reads=[[], [failure]])
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.outcome, result.codeDelivery),
                         (2, "refused", "not-delivered"))

    def test_merged_source_with_invalid_selection_is_disputed_not_undelivered(self):
        bad = selection("reused").replace(b'"reason":"fixture"', b'"reason":"tampered"')
        api = FakeApi([merged()], runs=[run()], selections={7: bad})
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.outcome, result.codeDelivery, result.coherentValidation),
                         (4, "delivered-disputed", "delivered", "disputed"))

    def test_advisory_ci_hook_receives_exact_generated_delivery_json(self):
        summary = MODULE.Summary(
            "fsgg.routine-delivery/v1", "FS-GG/.github", 7, HEAD, HEAD,
            "ready", "not-delivered", "not-required", None, 0, None, "current", "unobserved",
        )
        observed: dict = {}

        def runner(command, **kwargs):
            delivery = pathlib.Path(command[command.index("--delivery") + 1])
            observed.update(json.loads(delivery.read_text(encoding="utf-8")))
            self.assertEqual(command[:4], ["engine", "telemetry", "ci", "reconcile"])
            self.assertEqual(command[command.index("--assignment") + 1], "/private/assignment.json")
            self.assertEqual(command[command.index("--store-root") + 1], "/private/store")
            self.assertEqual(kwargs["timeout"], 35)
            return subprocess.CompletedProcess(command, 0, "{}", "")

        self.assertTrue(MODULE.observe_candidate(
            summary, assignment="/private/assignment.json", store_root="/private/store",
            engine="engine", runner=runner,
        ))
        self.assertEqual(observed, asdict(summary))

    def test_observation_failure_does_not_change_native_delivery(self):
        callbacks: list[str] = []

        def unavailable(summary):
            callbacks.append(summary.outcome)
            MODULE.observe_candidate(
                summary, assignment="/private/assignment.json", store_root="/private/store",
                engine="missing", runner=lambda *args, **kwargs: (_ for _ in ()).throw(OSError("offline")),
            )

        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, result = MODULE.summarize(
            api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=False, apply=True,
            candidate_observer=unavailable,
        )
        self.assertEqual(callbacks, ["ready"])
        self.assertEqual((code, result.codeDelivery, api.attempts), (0, "delivered", 1))

    def test_changed_head_never_calls_population_observer(self):
        callbacks: list[object] = []
        code, result = MODULE.summarize(
            FakeApi([opened("c" * 40)]), repo="FS-GG/.github", pr_number=1,
            expected_head=HEAD, merge_method="squash", publication_required=False,
            apply=True, candidate_observer=callbacks.append,
        )
        self.assertEqual((code, result.outcome, callbacks), (2, "refused", []))

    def test_exact_head_but_ineligible_pr_never_calls_population_observer(self):
        callbacks: list[object] = []
        draft = {**opened(), "draft": True}
        code, result = MODULE.summarize(
            FakeApi([draft]), repo="FS-GG/.github", pr_number=1,
            expected_head=HEAD, merge_method="squash", publication_required=False,
            apply=True, candidate_observer=callbacks.append,
        )
        self.assertEqual((code, result.outcome, callbacks), (2, "refused", []))

    def test_exact_head_but_failed_coherent_validation_never_calls_population_observer(self):
        callbacks: list[object] = []
        api = FakeApi([opened()], runs=[run(conclusion="failure")])
        code, result = MODULE.summarize(
            api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=False, apply=True,
            coherent_workflow="coherent.yml", candidate_observer=callbacks.append,
        )
        self.assertEqual((code, result.outcome, callbacks), (2, "refused", []))


if __name__ == "__main__":
    unittest.main()

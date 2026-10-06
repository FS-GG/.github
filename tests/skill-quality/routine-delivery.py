#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import hashlib
import io
import json
import os
import pathlib
import subprocess
import sys
import unittest
import warnings
import zipfile
from dataclasses import asdict
from datetime import datetime, timezone
from unittest import mock


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
        self.read_attempts = 0

    def get_pr(self, repo: str, pr: int) -> dict:
        self.read_attempts += 1
        return next(self.reads)

    def merge(self, request) -> dict:
        self.attempts += 1
        self.last_request = request
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


def selection_archive(entries: list[tuple[str, bytes]]) -> bytes:
    output = io.BytesIO()
    with warnings.catch_warnings():
        warnings.simplefilter("ignore", UserWarning)
        with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_DEFLATED) as bundle:
            for name, payload in entries:
                bundle.writestr(name, payload)
    return output.getvalue()


class RoutineDeliveryTests(unittest.TestCase):
    def setUp(self):
        # Legacy main-path fixtures must remain pure: an implicit advisory
        # discovery may otherwise execute an installed CLR helper during tests.
        def unavailable(*_args, **_kwargs):
            raise OSError("pure fixture: compiled telemetry unavailable")
        patcher = mock.patch.dict(MODULE.discover_telemetry_config.__kwdefaults__, {"runner": unavailable})
        patcher.start()
        self.addCleanup(patcher.stop)

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

    def test_native_merge_is_head_conditioned_and_uses_no_bypass(self):
        api = MODULE.GhApi()
        completed = subprocess.CompletedProcess([], 0, json.dumps({"merged": True, "sha": MERGE}), "")
        with mock.patch.object(MODULE.subprocess, "run", return_value=completed) as invoked:
            response = api.merge(MODULE.merge_effect_request(
                "FS-GG/.github", 7, HEAD, "main", "d" * 40, "squash",
            ))
        self.assertEqual(response, {"merged": True, "sha": MERGE})
        command = invoked.call_args.args[0]
        self.assertEqual(
            command,
            ["gh", "api", "--method", "PUT", "repos/FS-GG/.github/pulls/7/merge", "--input", "-"],
        )
        self.assertNotIn("--admin", command)
        self.assertEqual(
            json.loads(invoked.call_args.kwargs["input"]),
            {"merge_method": "squash", "sha": HEAD},
        )

    def test_native_merge_maps_synchronous_provider_refusal(self):
        completed = subprocess.CompletedProcess([], 1, "", "required status check failed")
        with mock.patch.object(MODULE.subprocess, "run", return_value=completed) as invoked:
            with self.assertRaisesRegex(MODULE.NativeMergeRefused, "required status check"):
                MODULE.GhApi().merge(MODULE.merge_effect_request(
                    "FS-GG/.github", 7, HEAD, "main", "d" * 40, "squash",
                ))
        self.assertEqual(invoked.call_count, 1)

    def qualification_selection(self, archive: bytes) -> bytes | None:
        artifact = {
            "name": f"qualification-selection-{HEAD}",
            "expired": False,
            "archive_download_url": "https://api.github.test/artifact.zip",
        }
        api = MODULE.GhApi()
        with mock.patch.object(api, "_run", return_value=[{"artifacts": [artifact]}]), \
             mock.patch.object(api, "_run_bytes", return_value=archive):
            return api.qualification_selection("FS-GG/FS.GG.Coordination", 7, HEAD)

    def test_qualification_selection_accepts_bounded_producer_ancillary_files(self):
        expected = selection("current")
        archive = selection_archive([
            ("selection.json", expected),
            ("profile.json", b'{"schema":"profile"}\n'),
            ("artifact-page-1.json", b""),
            ("artifact-pages.jsonl", b""),
        ])
        self.assertEqual(self.qualification_selection(archive), expected)

    def test_qualification_selection_rejects_duplicate_root_selection(self):
        archive = selection_archive([
            ("selection.json", selection("current")),
            ("selection.json", selection("reused")),
        ])
        with self.assertRaisesRegex(RuntimeError, "unsafe shape"):
            self.qualification_selection(archive)

    def test_qualification_selection_rejects_oversize_uncompressed_archive(self):
        archive = selection_archive([
            ("selection.json", selection("current")),
            ("profile.json", b"x" * 1_048_576),
        ])
        self.assertLess(len(archive), 1_048_576)
        with self.assertRaisesRegex(RuntimeError, "exceeds 1 MiB uncompressed"):
            self.qualification_selection(archive)

    def test_qualification_selection_rejects_unsafe_ancillary_path(self):
        archive = selection_archive([
            ("selection.json", selection("current")),
            ("../profile.json", b"{}\n"),
        ])
        with self.assertRaisesRegex(RuntimeError, "unsafe shape"):
            self.qualification_selection(archive)

    def test_merge_effect_request_binds_complete_source_and_target_identity(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, _ = self.call(api)
        request = api.last_request
        self.assertEqual(code, 0)
        self.assertEqual(
            (request.repository, request.pullRequest, request.expectedHead,
             request.baseRef, request.baseSha, request.mergeMethod),
            ("FS-GG/.github", 1, HEAD, "main", "d" * 40, "squash"),
        )

    def test_unreadable_base_identity_refuses_before_admission(self):
        unreadable = {**opened(), "base": {"ref": "main"}}
        api = FakeApi([unreadable])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 0))
        self.assertIn("base identity", result.reason)

    def test_required_check_provider_refusal_is_read_back_without_retry(self):
        unstable = {**opened(), "mergeable_state": "unstable"}
        api = FakeApi([unstable, unstable], [MODULE.NativeMergeRefused("required check failed")])
        code, result = self.call(api)
        self.assertEqual(
            (code, result.outcome, result.codeDelivery, result.attempts),
            (2, "refused", "not-delivered", 1),
        )
        self.assertEqual((api.read_attempts, api.attempts), (2, 1))
        self.assertIn("required check failed", result.reason)

    def test_changed_head_refuses_before_a_write(self):
        api = FakeApi([opened("c" * 40)])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 0))
        self.assertIn("changed head", result.reason)
        self.assertEqual(asdict(result)["observedHead"], "c" * 40)

    def test_open_pr_with_contradictory_merge_fields_refuses_before_a_write(self):
        contradictory = {**opened(), "merged_at": "", "merge_commit_sha": MERGE}
        api = FakeApi([contradictory])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (2, "refused", 0))
        self.assertIn("contradictory", result.reason)

    def test_closed_pr_with_malformed_merge_time_is_not_delivered(self):
        contradictory = {**merged(), "merged_at": "not-a-time"}
        api = FakeApi([contradictory])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (2, "refused", "not-delivered", 0))

    def test_success_requires_native_merged_readback(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, result = self.call(api, publication=True)
        self.assertEqual((code, result.codeDelivery, result.publication), (0, "delivered", "pending"))
        self.assertEqual((result.mergeCommit, result.attempts), (MERGE, 1))
        self.assertEqual(asdict(result)["observedHead"], HEAD)

    def test_ambiguous_write_with_exact_merged_readback_is_delivered(self):
        api = FakeApi([opened(), merged()], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (0, "delivered", "delivered", 1))
        self.assertEqual(result.mergeCommit, MERGE)
        self.assertEqual(asdict(result)["expectedHead"], HEAD)

    def test_open_readback_cannot_exclude_delayed_merge_or_allow_retry(self):
        api = FakeApi(
            [opened(), opened()],
            [MODULE.AmbiguousWrite("timeout"), {"merged": True, "sha": MERGE}],
        )
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (3, "indeterminate", 1))

    def test_ambiguous_write_rejects_merged_readback_for_a_changed_base(self):
        changed_base = {**merged(), "base": {"ref": "main", "sha": "e" * 40}}
        api = FakeApi([opened(), changed_base], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 1))

    def test_failed_readback_after_ambiguous_write_stays_indeterminate(self):
        api = FakeApi([opened()], [MODULE.AmbiguousWrite("timeout")])
        api.get_pr = mock.Mock(side_effect=[opened(), RuntimeError("read unavailable")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 1))
        self.assertIsNone(result.observedHead)

    def test_blocked_readback_does_not_settle_ambiguous_write(self):
        blocked = {**opened(), "mergeable_state": "blocked"}
        api = FakeApi([opened(), blocked], [MODULE.AmbiguousWrite("timeout")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, api.attempts), (3, "indeterminate", 1))

    def test_generic_merge_error_with_exact_merged_readback_is_delivered(self):
        api = FakeApi([opened(), merged()], [RuntimeError("provider error")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (0, "delivered", "delivered", 1))

    def test_response_commit_must_match_native_readback(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": "c" * 40}])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 1))

    def test_positive_response_with_contradictory_native_merge_stays_indeterminate(self):
        contradictory = {**merged(), "merged": False, "merged_at": ""}
        api = FakeApi([opened(), contradictory], [{"merged": True, "sha": MERGE}])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 1))

    def test_non_object_merge_response_uses_exact_native_readback(self):
        api = FakeApi([opened(), merged()], [[]])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (0, "delivered", "delivered", 1))

    def test_positive_response_without_readback_stays_indeterminate(self):
        api = FakeApi([opened()], [{"merged": True, "sha": MERGE}])
        api.get_pr = mock.Mock(side_effect=[opened(), RuntimeError("read unavailable")])
        code, result = self.call(api)
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts),
                         (3, "indeterminate", "unknown", 1))

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

    def test_post_merge_coherent_read_failure_keeps_one_attempt(self):
        completed = run("completed", "success")
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}],
                      [completed], {7: selection("current")})
        api.coherent_runs = mock.Mock(side_effect=[[completed], RuntimeError("read unavailable")])
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.outcome, result.codeDelivery, result.attempts),
                         (3, "indeterminate", "unknown", 1))
        self.assertEqual(result.observedHead, HEAD)

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

    def test_failure_after_unconfirmed_merge_attempt_stays_indeterminate(self):
        failure = {"status": "completed", "conclusion": "failure", "name": "run-partition (3)"}
        api = FakeApi([opened(), opened()], [{"merged": False}], [run()], {7: selection("reused")},
                      job_reads=[[], [failure]])
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.outcome, result.codeDelivery),
                         (3, "indeterminate", "unknown"))

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

    def test_workspace_ci_hook_routes_config_and_repository_without_store_root(self):
        summary = MODULE.Summary(
            "fsgg.routine-delivery/v1", "FS-GG/.github", 7, HEAD, HEAD,
            "ready", "not-delivered", "not-required", None, 0, None, "current", "unobserved",
        )

        def runner(command, **kwargs):
            self.assertEqual(command[:4], ["engine", "telemetry", "ci", "reconcile"])
            self.assertEqual(command[command.index("--config") + 1], "/private/telemetry.json")
            self.assertEqual(command[command.index("--repository") + 1], "FS-GG/.github")
            self.assertNotIn("--store-root", command)
            return subprocess.CompletedProcess(command, 0, '{"driverHealth":"pending"}', "")

        self.assertEqual("pending", MODULE.observe_candidate(
            summary, assignment="/private/assignment.json", config="/private/telemetry.json",
            repository="FS-GG/.github", engine="engine", runner=runner,
        ))

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

    def test_driver_observes_pre_admission_then_later_final_native_readback(self):
        moments = iter([
            datetime(2026, 9, 7, 0, 0, 0, tzinfo=timezone.utc),
            datetime(2026, 9, 7, 0, 2, 0, tzinfo=timezone.utc),
        ])

        class Clock:
            @staticmethod
            def now(zone):
                self.assertEqual(zone, timezone.utc)
                return next(moments)

            fromisoformat = staticmethod(datetime.fromisoformat)

        observations = []
        prior_api, prior_observe, prior_datetime = MODULE.GhApi, MODULE.observe_candidate, MODULE.datetime
        try:
            MODULE.GhApi = lambda: FakeApi(
                [opened(), {**merged(), "merged_at": "2026-09-07T00:01:00Z"}],
                [{"merged": True, "sha": MERGE}],
            )
            MODULE.datetime = Clock
            MODULE.observe_candidate = lambda summary, **_: observations.append(summary) or "complete"
            code = MODULE.main([
                "--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD, "--apply",
                "--telemetry-assignment", "/private/assignment.json",
                "--telemetry-store-root", "/private/store",
            ])
        finally:
            MODULE.GhApi, MODULE.observe_candidate, MODULE.datetime = prior_api, prior_observe, prior_datetime
        self.assertEqual(code, 0)
        self.assertEqual([value.outcome for value in observations], ["ready", "delivered"])
        pre = datetime.fromisoformat(observations[0].observedAt.replace("Z", "+00:00"))
        final = datetime.fromisoformat(observations[1].observedAt.replace("Z", "+00:00"))
        occurred = datetime.fromisoformat(observations[1].outcomeAt.replace("Z", "+00:00"))
        self.assertLess(pre, final)
        self.assertLessEqual(occurred, final)
        self.assertEqual(observations[1].telemetryHealth, "not-configured")

    def test_changed_head_never_calls_population_observer(self):
        callbacks: list[object] = []
        code, result = MODULE.summarize(
            FakeApi([opened("c" * 40)]), repo="FS-GG/.github", pr_number=1,
            expected_head=HEAD, merge_method="squash", publication_required=False,
            apply=True, candidate_observer=callbacks.append,
        )
        self.assertEqual((code, result.outcome, callbacks), (2, "refused", []))

    def test_driver_records_native_refusal_without_creating_pre_admission(self):
        observations = []
        prior_api, prior_observe = MODULE.GhApi, MODULE.observe_candidate
        try:
            MODULE.GhApi = lambda: FakeApi([opened("c" * 40)])
            MODULE.observe_candidate = lambda summary, **_: observations.append(summary) or "complete"
            code = MODULE.main([
                "--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD, "--apply",
                "--telemetry-assignment", "/private/assignment.json",
                "--telemetry-store-root", "/private/store",
            ])
        finally:
            MODULE.GhApi, MODULE.observe_candidate = prior_api, prior_observe
        self.assertEqual(code, 2)
        self.assertEqual([(value.outcome, value.codeDelivery) for value in observations], [("refused", "not-delivered")])

    def test_driver_discovers_host_config_and_creates_assignment_from_route_identities(self):
        observations = []
        created = []
        config = MODULE.TelemetryConfig(
            "/private/config.json", "/private/store", "configured-engine", None, False,
        )
        prior = MODULE.GhApi, MODULE.observe_candidate, MODULE.discover_telemetry_config, MODULE.create_ci_assignment
        try:
            MODULE.GhApi = lambda: FakeApi([opened()])
            MODULE.observe_candidate = lambda summary, **kwargs: observations.append((summary, kwargs)) or "complete"
            MODULE.discover_telemetry_config = lambda *_args, **_kwargs: config
            MODULE.create_ci_assignment = lambda selected, **kwargs: created.append((selected, kwargs)) or "/private/assignment.json"
            code = MODULE.main([
                "--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD,
                "--telemetry-feature", "GS2-08", "--telemetry-item", "GS2-08.3",
                "--telemetry-attempt", "attempt-1",
            ])
        finally:
            (MODULE.GhApi, MODULE.observe_candidate, MODULE.discover_telemetry_config,
             MODULE.create_ci_assignment) = prior
        self.assertEqual(code, 0)
        self.assertEqual(created[0][1]["item"], "GS2-08.3")
        self.assertEqual(observations[0][1], {
            "assignment": "/private/assignment.json", "store_root": "/private/store", "engine": "configured-engine",
        })

    def test_configured_host_without_route_identities_is_fail_visible(self):
        config = MODULE.TelemetryConfig(
            "/private/config.json", "/private/store", "engine", None, False,
        )
        prior = MODULE.GhApi, MODULE.discover_telemetry_config
        try:
            MODULE.GhApi = lambda: FakeApi([opened()])
            MODULE.discover_telemetry_config = lambda *_args, **_kwargs: config
            code = MODULE.main(["--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD])
        finally:
            MODULE.GhApi, MODULE.discover_telemetry_config = prior
        self.assertEqual(code, 0)

    def test_compiled_discovery_projection_is_the_only_configuration_input(self):
        projection = {
            "schema": "fsgg.telemetry.config-discovery/1", "status": "configured",
            "configPath": "/private/config.json", "storeRoot": "/private/store",
            "engine": "configured-engine", "repository": "FS-GG/.github", "workspace": True,
        }
        calls = []
        def runner(command, **kwargs):
            calls.append((command, kwargs))
            return subprocess.CompletedProcess(command, 0, json.dumps(projection) + "\n", "secret-stderr")
        actual = MODULE.discover_telemetry_config(
            "/private/config.json", command_engine="published-0.92.0", runner=runner,
        )
        self.assertEqual(actual, MODULE.TelemetryConfig(
            "/private/config.json", "/private/store", "configured-engine", "FS-GG/.github", True,
        ))
        self.assertEqual(calls[0][0], ["published-0.92.0", "skill", "telemetry-config", "discover",
                                      "--config", "/private/config.json"])

    def test_compiled_discovery_refusal_does_not_fall_back_to_python(self):
        def runner(command, **_kwargs):
            return subprocess.CompletedProcess(command, 1, "", "synthetic-secret")
        with self.assertRaisesRegex(RuntimeError, "invalid JSON"):
            MODULE.discover_telemetry_config(None, runner=runner)

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


class FakeWatchClock:
    def __init__(self):
        self.value = 0.0
        self.sleeps = []

    def __call__(self):
        return self.value

    def sleep(self, seconds):
        assert seconds >= 0
        self.sleeps.append(seconds)
        self.value += seconds


def native_check(bucket="pending", *, link="https://github.test/run/1", started="2026-10-05T00:00:00Z"):
    return {"bucket": bucket, "completedAt": "", "event": "pull_request", "link": link,
            "name": "test", "startedAt": started, "state": "IN_PROGRESS" if bucket == "pending" else "SUCCESS",
            "workflow": "CI"}


class FakeWatchApi:
    def __init__(self, samples, *, heads=None, fail_at=None):
        self.samples = iter(samples)
        self.heads = iter(heads) if heads is not None else None
        self.query_count = 0
        self.fail_at = fail_at

    def get_pr(self, _repo, _pr):
        self.query_count += 1
        if self.query_count == self.fail_at:
            raise RuntimeError("synthetic unreadable observation")
        result = opened(next(self.heads) if self.heads is not None else HEAD)
        result['updated_at'] = str(self.query_count)
        return result

    def checks(self, _repo, _pr):
        self.query_count += 1
        if self.query_count == self.fail_at:
            raise RuntimeError("synthetic unreadable observation")
        return next(self.samples)


class CheckWatchTests(unittest.TestCase):
    def observe(self, api, seconds=12, *, sleep=None):
        clock = FakeWatchClock()
        events = []
        code = MODULE.watch_checks("FS-GG/.github", 7, HEAD, seconds,
            clock=clock, sleep=sleep or clock.sleep,
            utc_now=lambda: datetime(2026, 10, 5, 0, 0, int(clock.value), tzinfo=timezone.utc),
            api_factory=lambda *_args, **_kwargs: api, emit=events.append)
        return code, events, clock

    def test_unchanged_samples_emit_no_heartbeat_and_keep_actual_observation_time(self):
        api = FakeWatchApi([[native_check()]] * 3)
        code, events, clock = self.observe(api)
        self.assertEqual((code, [e['event'] for e in events], api.query_count), (3, ['initial', 'deadline'], 9))
        self.assertEqual(events[0]['observedAt'], '2026-10-05T00:00:00+00:00')
        self.assertEqual(events[-1]['observedAt'], '2026-10-05T00:00:11+00:00')
        self.assertEqual(clock.value, 11.5)
        self.assertEqual(events[0]['projection']['sourceUpdatedAt'], '3')
        self.assertTrue(all(not e['applyAuthorized'] and e['readiness']=='not-evaluated' for e in events))

    def test_changed_identity_and_terminal_check_emit_material_revisions(self):
        api = FakeWatchApi([[native_check()], [native_check(link='https://github.test/run/2')], [native_check('pass')]])
        code, events, _clock = self.observe(api)
        self.assertEqual((code, [e['event'] for e in events]), (0, ['initial', 'revision', 'terminal']))
        self.assertEqual([e['revision'] for e in events], [1, 2, 3])

    def test_check_source_timestamp_change_is_material_not_observer_heartbeat(self):
        api = FakeWatchApi([[native_check()], [native_check(started='2026-10-05T00:00:01Z')], [native_check('pass')]])
        code, events, _clock = self.observe(api)
        self.assertEqual((code, [e['event'] for e in events]), (0, ['initial', 'revision', 'terminal']))

    def test_failed_or_cancelled_check_ends_without_waiting_for_pending_sibling(self):
        for bucket in ('fail', 'cancel'):
            code, events, _clock = self.observe(FakeWatchApi([[native_check(bucket), native_check(link='other')]]))
            self.assertEqual((code, len(events), events[0]['event']), (2, 1, 'terminal'))

    def test_empty_native_population_remains_unknown_until_deadline(self):
        code, events, _clock = self.observe(FakeWatchApi([[], [], []]))
        self.assertEqual((code, [e['event'] for e in events]), (3, ['initial', 'deadline']))
        self.assertIn('unknown', events[0]['reason'])

    def test_head_drift_before_or_after_checks_refuses_same_original_watch(self):
        for heads, count in ((['c'*40], 1), ([HEAD, 'c'*40], 3)):
            api = FakeWatchApi([[native_check()]], heads=heads)
            code, events, _clock = self.observe(api)
            self.assertEqual((code, api.query_count, events[-1]['event']), (3, count, 'observation-failure'))

    def test_failed_read_is_not_retried(self):
        api = FakeWatchApi([], fail_at=2)
        code, events, clock = self.observe(api)
        self.assertEqual((code, api.query_count, clock.sleeps), (3, 2, []))
        self.assertEqual(events[0]['event'], 'observation-failure')

    def test_caller_cancellation_is_fail_visible_without_next_query(self):
        def interrupt(_seconds):
            raise KeyboardInterrupt()
        api = FakeWatchApi([[native_check()]])
        code, events, _clock = self.observe(api, sleep=interrupt)
        self.assertEqual((code, api.query_count, [e['event'] for e in events]), (130, 3, ['initial', 'observation-failure']))

    def test_deadline_during_read_does_not_accept_late_terminal_data(self):
        clock=FakeWatchClock();events=[]
        api=FakeWatchApi([[native_check('pass')]])
        original=api.checks
        def late(*args):
            value=original(*args);clock.value=13;return value
        api.checks=late
        code=MODULE.watch_checks('FS-GG/.github',7,HEAD,12,clock=clock,sleep=clock.sleep,
            api_factory=lambda *_a,**_k:api,emit=events.append)
        self.assertEqual((code,[event['event'] for event in events]),(3,['deadline']))
        self.assertFalse(events[0]['applyAuthorized'])

    def test_native_adapter_uses_exact_commands_and_json_buckets_not_exit_policy(self):
        calls = []
        def query(command, deadline, **kwargs):
            calls.append((command, deadline))
            return 0, json.dumps([native_check('fail')]).encode() if command[1]=='pr' else json.dumps(opened()).encode()
        api = MODULE.WatchGhApi(12, query=query, clock=lambda:0)
        self.assertEqual(api.get_pr('FS-GG/.github', 7)['head']['sha'], HEAD)
        self.assertEqual(api.checks('FS-GG/.github', 7)[0]['bucket'], 'fail')
        self.assertEqual(calls[0][0], ['gh','api','repos/FS-GG/.github/pulls/7'])
        self.assertEqual(calls[1][0], ['gh','pr','checks','7','--repo','FS-GG/.github','--json',MODULE.WATCH_FIELDS])
        self.assertEqual(api.query_count, 2)

    def test_native_adapter_rejects_unknown_missing_duplicate_oversized_and_failed_reads(self):
        bad = [[dict(native_check(), bucket='unknown')], [dict(native_check(), state=None)],
               [{'name':'test'}], [native_check(), native_check()], [native_check(link=str(i)) for i in range(129)]]
        for value in bad:
            api = MODULE.WatchGhApi(12, query=lambda *_a, **_k:(0,json.dumps(value).encode()), clock=lambda:0)
            with self.assertRaises(RuntimeError):api.checks('FS-GG/.github',7)
            self.assertEqual(api.query_count,1)
        for code, raw in ((1,b'[]'),(8,b'[]'),(0,b'{bad'),(0,b'x'*(MODULE.WATCH_QUERY_BYTES+1))):
            api = MODULE.WatchGhApi(12, query=lambda *_a, **_k:(code,raw), clock=lambda:0)
            with self.assertRaises(RuntimeError):api.checks('FS-GG/.github',7)
            self.assertEqual(api.query_count,1)

    def test_query_count_and_original_deadline_prevent_another_launch(self):
        query = mock.Mock(return_value=(0,json.dumps(opened()).encode()))
        api = MODULE.WatchGhApi(12, query=query, clock=lambda:0)
        api.query_count = MODULE.WATCH_MAX_QUERIES
        with self.assertRaises(RuntimeError):api.get_pr('FS-GG/.github',7)
        query.assert_not_called()
        api = MODULE.WatchGhApi(.5, query=query, clock=lambda:0)
        with self.assertRaises(MODULE.WatchDeadline):api.get_pr('FS-GG/.github',7)
        query.assert_not_called()

    def test_emitted_output_bound_preserves_one_final_failure(self):
        with mock.patch.object(MODULE,'WATCH_EMIT_BYTES',5000):
            api = FakeWatchApi([[native_check(link='x'*600)]] * 3)
            code, events, _clock = self.observe(api)
        self.assertEqual((code,events[-1]['event']), (3,'observation-failure'))
        self.assertLessEqual(sum(len(json.dumps(e,separators=(',',':'),sort_keys=True).encode())+1 for e in events),5000)

    def test_watch_cli_bypasses_telemetry_and_delivery_and_refuses_apply(self):
        args=['--repo','FS-GG/.github','--pr','7','--head',HEAD,'--watch-checks','--watch-seconds','12']
        with mock.patch.object(MODULE,'watch_checks',return_value=3) as watch, \
             mock.patch.object(MODULE,'discover_telemetry_config',side_effect=AssertionError('telemetry')), \
             mock.patch.object(MODULE,'summarize',side_effect=AssertionError('delivery')):
            self.assertEqual(MODULE.main(args),3)
            watch.assert_called_once_with('FS-GG/.github',7,HEAD,12)
            with self.assertRaises(SystemExit):MODULE.main(args+['--apply'])
            self.assertEqual(watch.call_count,1)
        for extra in (['--watch-checks'],['--watch-seconds','12'],['--watch-checks','--watch-seconds','601']):
            with self.assertRaises(SystemExit):MODULE.main(args[:6]+extra)


class BoundedQueryTests(unittest.TestCase):
    def query(self, *, chunks=None, stderr_chunks=None, never=False, interrupt=False, seconds=5, reaping_unknown=False):
        clock=FakeWatchClock();streams=[mock.Mock(),mock.Mock()]
        streams[0].fileno.return_value=101;streams[1].fileno.return_value=102
        process=mock.Mock(stdout=streams[0],stderr=streams[1],returncode=0)
        process.poll.side_effect=lambda:None if (never or interrupt) and not process.kill.called else 0
        if reaping_unknown:
            process.wait.side_effect=subprocess.TimeoutExpired('original-query',.5)
        mapping={};reads={101:iter((chunks or [b'[]'])+[b'']),102:iter((stderr_chunks or [])+[b''])}
        selector=mock.Mock();selector.get_map.side_effect=lambda:mapping
        def register(stream,_events,data):mapping[stream.fileno()]=type('Key',(),{'fileobj':stream,'data':data})()
        selector.register.side_effect=register;selector.unregister.side_effect=lambda stream:mapping.pop(stream.fileno())
        def selected(timeout):
            clock.value+=timeout
            if interrupt:raise KeyboardInterrupt()
            return [] if never else [(key,1) for key in list(mapping.values())]
        selector.select.side_effect=selected
        with mock.patch.object(MODULE.selectors,'DefaultSelector',return_value=selector), \
             mock.patch.object(MODULE.os,'set_blocking'), \
             mock.patch.object(MODULE.os,'read',side_effect=lambda fd,_count:next(reads[fd])):
            try:
                result=MODULE.bounded_watch_query(['gh','api','read-only'],seconds,clock=clock,popen=lambda *_a,**_k:process)
                return result,process,clock
            except BaseException as error:
                return error,process,clock

    def test_streams_are_read_bounded_without_communicate_and_owned_child_reaped(self):
        result,process,_clock=self.query()
        self.assertEqual(result,(0,b'[]'))
        process.communicate.assert_not_called();process.kill.assert_not_called()
        self.assertGreaterEqual(process.wait.call_count,1)
        process.stdout.close.assert_called_once();process.stderr.close.assert_called_once()

    def test_original_timeout_and_cancellation_kill_and_reap_only_owned_child(self):
        for kwargs in ({'never':True},{'interrupt':True}):
            result,process,clock=self.query(**kwargs)
            self.assertIsInstance(result,(MODULE.WatchDeadline,KeyboardInterrupt))
            process.kill.assert_called_once();process.wait.assert_called_once()
            self.assertLessEqual(process.wait.call_args.kwargs['timeout'],.5)
            self.assertLessEqual(clock.value,5)

    def test_output_overflow_is_charged_before_retention_then_query_is_reaped(self):
        result,process,_clock=self.query(chunks=[b'x'*(MODULE.WATCH_QUERY_BYTES+1)])
        self.assertIsInstance(result,RuntimeError)
        self.assertIn('byte bound',str(result));process.wait.assert_called_once()
        process.communicate.assert_not_called()

    def test_stderr_shares_stdout_budget_and_failed_reaping_is_explicit_unknown(self):
        result,process,_clock=self.query(chunks=[b'x'*(MODULE.WATCH_QUERY_BYTES//2)],stderr_chunks=[b'y'*(MODULE.WATCH_QUERY_BYTES//2+1)])
        self.assertIsInstance(result,RuntimeError)
        self.assertIn('byte bound',str(result))
        result,process,_clock=self.query(never=True,reaping_unknown=True)
        self.assertIsInstance(result,RuntimeError)
        self.assertIn('retirement is unknown',str(result))
        process.kill.assert_called_once()

    def test_per_query_timeout_is_clipped_to_original_watch_and_not_renewed(self):
        result,process,clock=self.query(never=True,seconds=100)
        self.assertIsInstance(result,RuntimeError)
        self.assertIn('clipped timeout',str(result))
        self.assertLessEqual(clock.value,30.1)
        process.kill.assert_called_once();process.wait.assert_called_once()

    def test_no_child_is_started_inside_original_cleanup_reserve(self):
        popen=mock.Mock()
        with self.assertRaises(MODULE.WatchDeadline):
            MODULE.bounded_watch_query(['gh','api','read-only'],.5,clock=lambda:0,popen=popen)
        popen.assert_not_called()



class DashboardEventTests(unittest.TestCase):
    def event(self, **kwargs):
        return MODULE.observe_dashboard_event(engine="selected-engine", config="/private/config",
                                             clock=lambda: 10, **kwargs)

    def test_no_budget_or_exhausted_never_launches(self):
        query=mock.Mock()
        self.assertEqual(self.event(deadline=None,query=query),"not-run-budget-unavailable")
        self.assertEqual(self.event(deadline=10.5,query=query),"not-run-budget-exhausted")
        query.assert_not_called()

    def test_absent_config_never_activates(self):
        query=mock.Mock()
        self.assertEqual(MODULE.observe_dashboard_event(engine="engine",config=None,deadline=20,
                         clock=lambda:10,query=query),"not-run-config-unavailable")
        query.assert_not_called()

    def test_exact_event_and_original_boundary(self):
        def query(command, deadline, **kwargs):
            self.assertEqual(command,["selected-engine","telemetry","dashboard","publisher-event",
                                      "--config","/private/config"])
            self.assertEqual((deadline,kwargs["max_seconds"],kwargs["max_bytes"]),(80,60,8192))
            return 0,json.dumps(dict(schema="fsgg.telemetry.dashboard-event-health/1",status="published",
                 reason="PUBLICATION_VERIFIED",publicRevision="a"*64,commit=MERGE)).encode()
        self.assertEqual(self.event(deadline=80,query=query),"published")

    def test_nonzero_malformed_and_unverified_never_green(self):
        for code,raw in [(1,b"{}"),(0,b"garbled"),(0,b'[]'), (0,b'{"status":[],"reason":{}}'),
                         (0,b'{"status":"published","reason":"PUBLICATION_VERIFIED"}')]:
            self.assertEqual(self.event(deadline=80,query=lambda *a,**k:(code,raw)),"unavailable")

    def test_timeout_failure_and_skip_do_not_retry(self):
        query=mock.Mock(side_effect=subprocess.TimeoutExpired("private-not-reported",60))
        self.assertEqual(self.event(deadline=80,query=query),"unavailable")
        self.assertEqual(query.call_count,1)
        self.assertEqual(self.event(deadline=80,query=lambda *a,**k:(0,b'{"status":"skipped","reason":"NO_CHANGE"}')),"skipped")

    def test_late_positive_result_is_not_accepted(self):
        clock=iter([10,81])
        self.assertEqual(MODULE.observe_dashboard_event(engine="engine",config="config",deadline=80,
                         clock=lambda:next(clock),query=lambda *a,**k:(0,b'{}')),"unavailable")

    def test_clipped_ci_uses_existing_boundary(self):
        summary=MODULE.Summary("fsgg.routine-delivery/v1","FS-GG/.github",7,HEAD,HEAD,
                              "delivered","delivered","not-required",MERGE,1,None,"current","unobserved")
        with mock.patch.object(MODULE,"bounded_watch_query",return_value=(0,b'{"driverHealth":"complete"}')) as query:
            self.assertEqual(MODULE.observe_candidate(summary,assignment="private",store_root="store",
                             engine="engine",deadline=80),"complete")
            self.assertEqual(query.call_args.args[1],80)
            self.assertEqual(query.call_args.kwargs["max_seconds"],35)

    def test_main_refreshes_once_after_final_ci_without_changing_merge(self):
        observations=[]
        def observe(summary,**kwargs):
            observations.append(summary.outcome)
            self.assertEqual(kwargs["deadline"],80)
            return "complete"
        with mock.patch.object(MODULE,"GhApi",return_value=FakeApi([opened(),merged()],[{"merged":True,"sha":MERGE}])), \
             mock.patch.object(MODULE,"observe_candidate",side_effect=observe), \
             mock.patch.object(MODULE,"observe_dashboard_event",return_value="unavailable") as event, \
             mock.patch("sys.stdout",new_callable=io.StringIO) as output:
            code=MODULE.main(["--repo","FS-GG/.github","--pr","7","--head",HEAD,"--apply",
                              "--telemetry-assignment","assignment","--telemetry-store-root","store",
                              "--telemetry-config","config","--telemetry-publisher-event",
                              "--telemetry-advisory-deadline-monotonic","80"])
            self.assertEqual((code,observations),(0,["ready","delivered"]))
            self.assertEqual(event.call_count,1)
            self.assertEqual(event.call_args.kwargs["config"],"config")
            result=json.loads(output.getvalue())
            self.assertEqual((result["codeDelivery"],result["telemetryHealth"],result["dashboardPublicationHealth"]),
                             ("delivered","complete","unavailable"))

    def test_failed_final_ci_blocks_event(self):
        with mock.patch.object(MODULE,"GhApi",return_value=FakeApi([opened("c"*40)])), \
             mock.patch.object(MODULE,"observe_candidate",return_value="unavailable"), \
             mock.patch.object(MODULE,"observe_dashboard_event") as event, \
             mock.patch("sys.stdout",new_callable=io.StringIO):
            self.assertEqual(MODULE.main(["--repo","FS-GG/.github","--pr","7","--head",HEAD,
                             "--telemetry-assignment","assignment","--telemetry-store-root","store",
                             "--telemetry-publisher-event"]),2)
            event.assert_not_called()


if __name__ == "__main__":
    unittest.main()

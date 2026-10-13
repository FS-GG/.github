#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import contextlib
import hashlib
import io
import json
import os
import pathlib
import subprocess
import copy
import tempfile
import shutil
from types import SimpleNamespace
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
        "head": {"sha": head, "ref": "feature/fixture"}, "body": "Fixture prose", "state": "open", "draft": False, "merged": False,
        "merged_at": None, "mergeable": True, "mergeable_state": "clean",
        "base": {"ref": "main", "sha": "d" * 40},
    }


def merged(head: str = HEAD) -> dict:
    return {
        "head": {"sha": head, "ref": "feature/fixture"}, "body": "Fixture prose", "state": "closed", "draft": False, "merged": True,
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

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None:
        pass


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

    def test_routine_body_refuses_before_coherent_or_candidate_observation(self):
        for body in (None, "missing", f"<!-- fsgg:routine-development/v1 head={'b' * 40} operation=source-change -->"):
            with self.subTest(body=body):
                pr = opened(); pr['head']['ref'] = 'routine/fixture'; pr['body'] = body
                api = FakeApi([pr]); api.coherent_runs = mock.Mock(side_effect=AssertionError('expensive query'))
                observer = mock.Mock()
                code, result = MODULE.summarize(api, repo='FS-GG/.github', pr_number=1, expected_head=HEAD,
                    merge_method='squash', publication_required=False, apply=True,
                    coherent_workflow='coherent.yml', candidate_observer=observer)
                self.assertEqual((code, api.attempts, result.routineEligibility), (2, 0, 'unresolved'))
                observer.assert_not_called()
        for branch in ('feature/fixture', 'item/7-fixture'):
            with self.subTest(branch=branch):
                pr=opened(); pr['head']['ref']=branch; pr['body']=None
                code,result=self.call(FakeApi([pr]),apply=False)
                self.assertEqual((code,result.outcome,result.routineEligibility),(0,'ready','not-required'))

    def test_routine_late_body_change_and_new_failed_check_stop_merge(self):
        pr = opened(); pr['head']['ref'] = 'routine/fixture'
        pr['body'] = f'<!-- fsgg:routine-development/v1 head={HEAD} operation=source-change -->'
        for late_body, eligibility in ((pr['body'] + '\nChanged prose', None), (pr['body'], RuntimeError('newest failed'))):
            with self.subTest(late_body=late_body):
                late = json.loads(json.dumps(pr)); late['body'] = late_body
                api = FakeApi([pr, late]); api.routine_eligibility = mock.Mock(side_effect=[None, eligibility])
                code, result = self.call(api)
                self.assertEqual((code, result.attempts, api.attempts), (2, 0, 0))
        api = FakeApi([pr, pr, merged()], [{'merged': True, 'sha': MERGE}])
        code, result = self.call(api)
        self.assertEqual((code, result.codeDelivery, api.attempts, result.routineEligibility), (0, 'delivered', 1, 'passed'))

    def test_merged_routine_failure_preserves_delivery_and_coherent_truth(self):
        pr = merged(); pr['head']['ref'] = 'routine/fixture'; pr['body'] = 'missing'
        api = FakeApi([pr], runs=[run('completed', 'success')])
        code, result = self.call(api, coherent=True)
        self.assertEqual((code, result.codeDelivery, result.mergeCommit, result.coherentValidation,
            result.routineEligibility, api.attempts), (4, 'delivered', MERGE, 'passed', 'unresolved', 0))

    def test_native_routine_provenance_and_latest_selection(self):
        repo = 'FS-GG/.github'; workflow = {'id':352355034, 'path':'.github/workflows/routine-eligibility.yml'}
        def evidence(job_id=11, status='completed', conclusion='success', time='2026-10-13T00:00:00Z'):
            check = {'id':job_id, 'name':'routine-eligibility', 'app':{'id':15368}, 'head_sha':HEAD,
                'details_url':f'https://github.com/{repo}/actions/runs/7/job/{job_id}',
                'check_suite':{'id':13}, 'status':status, 'conclusion':conclusion, 'started_at':time}
            run = {'id':7, 'workflow_id':352355034, 'path':workflow['path'], 'event':'pull_request_target',
                'head_sha':HEAD, 'head_branch':'routine/fixture', 'status':'completed', 'conclusion':'success',
                'check_suite_id':13, 'repository':{'full_name':repo}, 'run_attempt':1, 'pull_requests':[]}
            job = {'id':job_id, 'run_id':7, 'head_sha':HEAD, 'name':'routine-eligibility',
                'check_run_url':f'https://api.github.com/repos/{repo}/check-runs/{job_id}',
                'run_attempt':1, 'status':status, 'conclusion':conclusion}
            return check, run, job
        check, native_run, job = evidence()
        variants = [(None,None), ('check','head_sha'), ('check','app'), ('run','event'), ('run','workflow_id'),
            ('run','repository'), ('run','check_suite_id'), ('job','run_attempt'), ('job','head_sha')]
        for section, field in variants:
            c,r,j = json.loads(json.dumps([check,native_run,job]))
            if section: {'check':c,'run':r,'job':j}[section][field] = 'wrong'
            api = MODULE.GhApi()
            with mock.patch.object(api, '_run', side_effect=[{'total_count':1,'check_runs':[c]},workflow,r,j]):
                if section:
                    with self.assertRaises(RuntimeError): api.routine_eligibility(repo,HEAD,'routine/fixture')
                else: api.routine_eligibility(repo,HEAD,'routine/fixture')
        for status, conclusion in [('in_progress',None),('completed','failure')]:
            c,r,j = evidence(12,status,conclusion,'2026-10-13T00:01:00Z')
            api=MODULE.GhApi()
            with mock.patch.object(api,'_run',side_effect=[{'total_count':2,'check_runs':[check,c]},workflow,native_run,job,r,j]):
                with self.assertRaisesRegex(RuntimeError,'latest'): api.routine_eligibility(repo,HEAD,'routine/fixture')
        api=MODULE.GhApi()
        with mock.patch.object(api,'_run',return_value={'total_count':2,'check_runs':[check]}):
            with self.assertRaisesRegex(RuntimeError,'incomplete'): api.routine_eligibility(repo,HEAD,'routine/fixture')
        for attempt,status,conclusion,branch in [(2,'in_progress',None,'routine/fixture'),
                (2,'completed','failure','routine/fixture'),(2,'completed','success','routine/fixture'),
                (1,'in_progress',None,'routine/fixture'),(1,'completed','failure','routine/fixture'),
                (1,'completed','success','routine/wrong')]:
            with self.subTest(attempt=attempt,status=status,conclusion=conclusion,branch=branch):
                r=dict(native_run,run_attempt=attempt,status=status,conclusion=conclusion,head_branch=branch)
                api=MODULE.GhApi()
                with mock.patch.object(api,'_run',side_effect=[{'total_count':1,'check_runs':[check]},workflow,r,job]):
                    with self.assertRaisesRegex(RuntimeError,'current run attempt/branch'):
                        api.routine_eligibility(repo,HEAD,'routine/fixture')

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

    def test_ci_failure_reports_safe_admission_reason_without_provider_text(self):
        summary = MODULE.Summary(
            "fsgg.routine-delivery/v1", "FS-GG/.github", 7, HEAD, HEAD,
            "delivered", "delivered", "not-required", MERGE, 1, None, "current", "unobserved",
        )
        error = io.StringIO()
        refusal = "first CI population admission requires ready, not-delivered, and an explicit matching observed head"
        with contextlib.redirect_stderr(error):
            health = MODULE.observe_candidate(
                summary, assignment="/private/assignment", engine="engine",
                runner=lambda *a, **k: subprocess.CompletedProcess(a[0], 2, "",
                    "fsgg-coord-engine: telemetry ci: " + refusal + "\nprovider-secret=/private/token"),
            )
        self.assertEqual(health, "unavailable")
        self.assertIn("reconcile-exit-2:prospective-ci-admission-missing", error.getvalue())
        self.assertNotIn("provider-secret", error.getvalue())
        self.assertNotIn("/private/", error.getvalue())

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


class ExplicitHostStoreEnvironmentTests(unittest.TestCase):
    def summary(self):
        return MODULE.Summary("fsgg.routine-delivery/v1", "FS-GG/.github", 7, HEAD, HEAD,
                              "ready", "not-delivered", "not-required", None, 0, None,
                              "current", "unobserved")

    def test_direct_store_reconcile_excludes_only_inherited_config(self):
        calls = []
        def runner(command, **kwargs):
            calls.append((command, kwargs))
            return subprocess.CompletedProcess(command, 0, '{"driverHealth":"complete"}', "")
        inherited = {"FSGG_TELEMETRY_CONFIG": "/private/host-config", "GH_TOKEN": "inert-test-token",
                     "FSGG_TELEMETRY_STORE": "/private/fallback", "PATH": "/inert/path"}
        with mock.patch.dict(os.environ, inherited, clear=True):
            original = dict(os.environ)
            self.assertEqual(MODULE.observe_candidate(self.summary(), assignment="assignment",
                             store_root="/private/selected", engine="engine", runner=runner), "complete")
            self.assertEqual(dict(os.environ), original)
        command, options = calls[0]
        self.assertEqual(command[-2:], ["--store-root", "/private/selected"])
        self.assertNotIn("--config", command)
        self.assertEqual(options["env"], {key: value for key, value in inherited.items()
                                         if key != "FSGG_TELEMETRY_CONFIG"})
        self.assertEqual(options["timeout"], 35)

    def test_bounded_store_reconcile_uses_same_child_environment(self):
        with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CONFIG": "host", "GH_TOKEN": "inert"}, clear=True), \
             mock.patch.object(MODULE, "bounded_watch_query", return_value=(0, b'{"driverHealth":"complete"}')) as query:
            self.assertEqual(MODULE.observe_candidate(self.summary(), assignment="assignment",
                             store_root="store", engine="engine", deadline=80), "complete")
            self.assertEqual(query.call_args.kwargs["env"], {"GH_TOKEN": "inert"})
            self.assertEqual(query.call_args.args[1], 80)
            self.assertEqual(query.call_args.kwargs["max_seconds"], 35)
            self.assertEqual(os.environ["FSGG_TELEMETRY_CONFIG"], "host")

    def test_workspace_reconcile_keeps_inherited_environment_selection(self):
        calls = []
        def runner(command, **kwargs):
            calls.append((command, kwargs))
            return subprocess.CompletedProcess(command, 0, '{"driverHealth":"complete"}', "")
        with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CONFIG": "workspace"}, clear=True):
            self.assertEqual(MODULE.observe_candidate(self.summary(), assignment="assignment",
                             config="workspace", repository="FS-GG/.github", engine="engine", runner=runner), "complete")
            self.assertEqual(os.environ["FSGG_TELEMETRY_CONFIG"], "workspace")
        self.assertNotIn("env", calls[0][1])
        self.assertEqual(calls[0][0][-4:], ["--config", "workspace", "--repository", "FS-GG/.github"])

    def test_explicit_store_ambiguity_remains_advisory_refusal_without_retry(self):
        runner = mock.Mock(return_value=subprocess.CompletedProcess("engine", 1, "",
            "fsgg-coord-engine: telemetry ci: workspace config and legacy store root cannot both be selected"))
        with mock.patch.dict(os.environ, {"FSGG_TELEMETRY_CONFIG": "host"}, clear=True):
            self.assertEqual(MODULE.observe_candidate(self.summary(), assignment="assignment",
                             store_root="store", engine="engine", runner=runner), "unavailable")
        self.assertEqual(runner.call_count, 1)


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
    def query(self, *, chunks=None, stderr_chunks=None, never=False, interrupt=False, seconds=5, reaping_unknown=False,
              env=None, spawn_calls=None):
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
                def popen(*args, **kwargs):
                    if spawn_calls is not None:
                        spawn_calls.append((args, kwargs))
                    return process
                options = {} if env is None else {"env": env}
                result=MODULE.bounded_watch_query(['gh','api','read-only'],seconds,clock=clock,popen=popen,**options)
                return result,process,clock
            except BaseException as error:
                return error,process,clock

    def test_streams_are_read_bounded_without_communicate_and_owned_child_reaped(self):
        result,process,_clock=self.query()
        self.assertEqual(result,(0,b'[]'))
        process.communicate.assert_not_called();process.kill.assert_not_called()
        self.assertGreaterEqual(process.wait.call_count,1)
        process.stdout.close.assert_called_once();process.stderr.close.assert_called_once()

    def test_selected_child_environment_reaches_popen_without_changing_original_retirement(self):
        environment = {"GH_TOKEN": "inert", "PATH": "/inert/path"}
        for never in (False, True):
            calls = []
            result, process, clock = self.query(env=environment, spawn_calls=calls, never=never)
            self.assertEqual(len(calls), 1)
            self.assertIs(calls[0][1]["env"], environment)
            self.assertNotIn("FSGG_TELEMETRY_CONFIG", calls[0][1]["env"])
            self.assertTrue(calls[0][1]["close_fds"])
            if never:
                self.assertIsInstance(result, MODULE.WatchDeadline)
                process.kill.assert_called_once()
                self.assertLessEqual(process.wait.call_args.kwargs["timeout"], .5)
                self.assertLessEqual(clock.value, 5)
            else:
                self.assertEqual(result, (0, b'[]'))
                process.kill.assert_not_called()
            process.stdout.close.assert_called_once()
            process.stderr.close.assert_called_once()
        calls = []
        self.query(spawn_calls=calls)
        self.assertNotIn("env", calls[0][1])

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

    def test_projection_config_never_replaces_workspace_observation_authority(self):
        cases = [
            (None, None, "/private/workspace"),
            (None, "/private/dashboard-env", "/private/dashboard-env"),
            ("/private/dashboard-cli", "/private/dashboard-env", "/private/dashboard-cli"),
            ("relative.json", "/private/dashboard-env", None),
            (None, "relative.json", None),
            ("", None, None),
        ]
        for explicit, environment, expected in cases:
            with self.subTest(explicit=explicit, environment=environment):
                env = {} if environment is None else {"FSGG_TELEMETRY_DASHBOARD_CONFIG": environment}
                with mock.patch.dict(MODULE.os.environ, env, clear=True), \
                     mock.patch.object(MODULE, "GhApi", return_value=FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])), \
                     mock.patch.object(MODULE, "observe_candidate", return_value="complete") as observer, \
                     mock.patch.object(MODULE, "observe_dashboard_event", return_value="published") as event, \
                     mock.patch("sys.stdout", new_callable=io.StringIO) as output:
                    args = ["--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD, "--apply",
                            "--telemetry-assignment", "assignment", "--telemetry-config", "/private/workspace",
                            "--telemetry-publisher-event", "--telemetry-advisory-deadline-monotonic", "80"]
                    if explicit is not None:
                        args += ["--telemetry-publisher-config", explicit]
                    self.assertEqual(MODULE.main(args), 0)
                    self.assertTrue(observer.call_args_list)
                    self.assertTrue(all(call.kwargs["config"] == "/private/workspace" for call in observer.call_args_list))
                    result = json.loads(output.getvalue())
                    self.assertEqual((result["codeDelivery"], result["telemetryHealth"]), ("delivered", "complete"))
                    if expected is None:
                        event.assert_not_called()
                        self.assertEqual(result["dashboardPublicationHealth"], "not-run-config-invalid")
                    else:
                        event.assert_called_once_with(engine="fsgg-coord-engine", config=expected, deadline=80)
                        self.assertEqual(result["dashboardPublicationHealth"], "published")


# Only these fixed inert Git repositories may launch Git; no Actions/native transport.
GIT_FIXTURE_ROOTS = set()
GIT_FIXTURE_EXECUTABLE = shutil.which("git")


def fixture_git(root, *args):
    env = {"PATH": os.environ.get("PATH", ""), "GIT_AUTHOR_NAME": "Fixture",
           "GIT_AUTHOR_EMAIL": "fixture@example.invalid", "GIT_COMMITTER_NAME": "Fixture",
           "GIT_COMMITTER_EMAIL": "fixture@example.invalid"}
    return subprocess.run([GIT_FIXTURE_EXECUTABLE, "-c", "core.hooksPath=/dev/null", *args],
                          cwd=root, env=env, check=True, capture_output=True, timeout=5).stdout


class InstallationApi(MODULE.GhApi):
    def __init__(self, fixture, reads, writes=()):
        self.fixture = fixture
        self.reads, self.writes = iter(reads), iter(writes)
        self.overrides, self.attempts = {}, 0
        self.required = [dict(name="test", bucket="pass", state="SUCCESS",
                              completedAt="2026-10-13T00:00:00Z",event="pull_request",workflow="CI",
                              startedAt="2026-10-13T00:00:00Z",
                              link="https://github.com/FS-GG/Receiver/actions/runs/7/job/8")]
        self.runs = [dict(id=7, head_sha=fixture["head"], status="completed", conclusion="success",
                          updated_at="2026-10-13T00:00:00Z", run_attempt=1)]

    def installation_read(self, endpoint):
        if endpoint in self.overrides:
            value = self.overrides[endpoint]
            if isinstance(value, Exception): raise value
            return copy.deepcopy(value)
        return copy.deepcopy(self.fixture["responses"][endpoint])

    def get_pr(self, repo, number):
        return copy.deepcopy(next(self.reads))

    def _run(self, args, **kwargs):
        # Exercise the maintained native merge adapter's exact request serialization.
        self.attempts += 1
        assert args[:3] == ["--method", "PUT", "repos/FS-GG/Receiver/pulls/1/merge"]
        assert json.loads(kwargs["input_text"]) == {"sha": self.fixture["head"], "merge_method": "squash"}
        value = next(self.writes)
        if isinstance(value, Exception): raise value
        return value

    def coherent_runs(self, *_): return self.runs
    def coherent_jobs(self, *_): return []
    def qualification_selection(self, *_): return None


class ProducerInstallationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory(prefix="routine-installation-git-")
        cls.directory = pathlib.Path(cls.tmp.name)
        cls.owner, cls.receiver = cls.directory / "owner", cls.directory / "receiver"
        for root in (cls.owner, cls.receiver):
            root.mkdir(); GIT_FIXTURE_ROOTS.add(str(root)); fixture_git(root, "init", "-q")
        def commit(root):
            fixture_git(root, "add", "--all"); fixture_git(root, "commit", "-qm", "inert fixture")
            return fixture_git(root, "rev-parse", "HEAD").decode().strip()
        for path in (*MODULE.PRODUCER_PATHS, "tools/routine-delivery.py", "src/FS.GG.Drivers/workspace-inventory.json"):
            target = cls.owner / path; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((ROOT / path).read_bytes()); target.chmod((ROOT / path).stat().st_mode & 0o777)
        source = commit(cls.owner)
        policy = cls.owner / MODULE.PRODUCER_PATHS[0]; original = policy.read_bytes()
        policy.write_bytes(original + b" "); obsolete = commit(cls.owner)
        policy.write_bytes(original); (cls.owner / "README.md").write_text("unrelated owner change")
        owner_head = commit(cls.owner)
        (cls.receiver / "README.md").write_text("base")
        base = commit(cls.receiver)
        for path in MODULE.PRODUCER_PATHS:
            target = cls.receiver / path; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((cls.owner / path).read_bytes()); target.chmod((cls.owner / path).stat().st_mode & 0o777)
        head = commit(cls.receiver)
        (cls.receiver / "README.md").write_text("ordinary routine change")
        cls.successor = commit(cls.receiver)
        responses = {}
        def objects(repo, root, revision):
            tree_sha = fixture_git(root, "rev-parse", revision + "^{tree}").decode().strip()
            rows = []
            for raw in fixture_git(root, "ls-tree", "-rz", revision).split(b"\0"):
                if not raw: continue
                meta, path = raw.split(b"\t", 1); mode, kind, sha = meta.decode().split()
                rows.append(dict(path=path.decode(), mode=mode, type=kind, sha=sha))
                body = fixture_git(root, "cat-file", "blob", sha)
                responses[f"repos/{repo}/git/blobs/{sha}"] = dict(sha=sha, encoding="base64", size=len(body),
                                                                content=MODULE.base64.b64encode(body).decode())
            responses[f"repos/{repo}/git/commits/{revision}"] = dict(sha=revision, tree={"sha": tree_sha})
            responses[f"repos/{repo}/git/trees/{tree_sha}?recursive=1"] = dict(sha=tree_sha, truncated=False, tree=rows)
            return rows, tree_sha
        objects("FS-GG/.github", cls.owner, source); objects("FS-GG/.github", cls.owner, obsolete)
        objects("FS-GG/.github", cls.owner, owner_head)
        objects("FS-GG/Receiver", cls.receiver, base); rows, tree = objects("FS-GG/Receiver", cls.receiver, head)
        responses["repos/FS-GG/Receiver"] = dict(id=42, full_name="FS-GG/Receiver", default_branch="main")
        responses["repos/FS-GG/.github"] = dict(id=1269292704, full_name="FS-GG/.github", default_branch="main")
        for revision in (source, obsolete):
            responses[f"repos/FS-GG/.github/compare/{revision}...main"] = dict(status="ahead", merge_base_commit={"sha": revision}, head_commit={"sha": owner_head})
        responses["repos/FS-GG/Receiver/git/ref/heads/main"] = {"object": {"sha": base}}
        responses["repos/FS-GG/Receiver/pulls/1/files?per_page=100"] = [dict(filename=row["path"], status="added", sha=row["sha"]) for row in rows if row["path"] in MODULE.PRODUCER_PATHS]
        responses["repos/FS-GG/Receiver/actions/workflows?per_page=100"] = dict(total_count=0, workflows=[])
        responses[f"repos/FS-GG/Receiver/commits/{head}/check-runs?per_page=100"] = dict(total_count=1, check_runs=[dict(id=8,name="test",head_sha=head,app={"id":15368},check_suite={"id":9},status="completed",conclusion="success",details_url="https://github.com/FS-GG/Receiver/actions/runs/7/job/8")])
        responses["repos/FS-GG/Receiver/actions/runs/7"] = dict(id=7, head_sha=head, repository={"full_name": "FS-GG/Receiver"}, status="completed", conclusion="success", run_attempt=1,check_suite_id=9)
        responses["repos/FS-GG/Receiver/actions/jobs/8"] = dict(id=8, run_id=7, head_sha=head, name="test", status="completed", conclusion="success", run_attempt=1,check_run_url="https://api.github.com/repos/FS-GG/Receiver/check-runs/8")
        responses[f"repos/FS-GG/Receiver/compare/{head}...{head}"] = dict(status="identical", merge_base_commit={"sha": head})
        cls.fixture = dict(source=source, obsolete=obsolete, head=head, base=base, tree=tree, responses=responses)
        cls.pr = opened(head); cls.pr.update(number=1, changed_files=5)
        cls.pr["base"].update(sha=base, repo=dict(id=42, full_name="FS-GG/Receiver"))
        cls.pr["head"].update(ref="routine/install-producer", repo=dict(id=42, full_name="FS-GG/Receiver"))
        cls.pr["body"] = f"<!-- fsgg:routine-development/v1 head={head} operation=source-change -->"
        cls.after = copy.deepcopy(cls.pr); cls.after.update(state="closed", merged=True, merged_at="2026-10-13T00:00:00Z", merge_commit_sha=head)

    @classmethod
    def tearDownClass(cls):
        for root in (cls.owner, cls.receiver): GIT_FIXTURE_ROOTS.discard(str(root))
        cls.tmp.cleanup()

    def api(self, reads=None, writes=()):
        return InstallationApi(self.fixture, reads or [self.pr], writes)

    def invoke(self, api, apply=False, source=None, prior=None):
        with mock.patch.object(MODULE.WatchGhApi, "_watch_read", side_effect=lambda command, accepted: (0, api.required)):
            return MODULE.summarize(api, repo="FS-GG/Receiver", pr_number=1,
                expected_head=self.fixture["head"], merge_method="squash", publication_required=False,
                apply=apply, coherent_workflow="CI.yml", producer_source=source or self.fixture["source"], installation_prior=prior)

    def test_exact_real_git_payload_is_ready_without_fake_eligibility(self):
        api = self.api(); code, result = self.invoke(api)
        self.assertEqual((code, result.outcome, result.producerInstallation, result.routineEligibility, api.attempts),
                         (0, "ready", "ready", "unavailable-first-install", 0))

    def test_merge_uses_existing_cas_and_exact_protected_payload_readback(self):
        api = self.api([self.pr, self.pr, self.after], [{"merged": True, "sha": self.fixture["head"]}])
        original = api.installation_contract
        def transition(*args, **kwargs):
            if kwargs.get("delivered"): api.overrides["repos/FS-GG/Receiver/git/ref/heads/main"] = {"object": {"sha": self.fixture["head"]}}
            return original(*args, **kwargs)
        with mock.patch.object(api, "installation_contract", side_effect=transition): code, result = self.invoke(api, True)
        self.assertEqual((code, api.attempts, result.producerInstallation, result.routineEligibility), (0, 1, "delivered-activation-pending", "unavailable-first-install"))

    def test_stale_protected_ancestor_policy_refuses_but_identical_closure_ancestor_passes(self):
        self.assertEqual(self.invoke(self.api(), source=self.fixture["obsolete"])[0], 2)
        self.assertEqual(self.invoke(self.api(), source=self.fixture["source"])[0], 0)

    def test_native_absence_incomplete_denied_disabled_and_existing_check_refuse(self):
        cases = [
            ("repos/FS-GG/Receiver/actions/workflows?per_page=100", RuntimeError("denied")),
            ("repos/FS-GG/Receiver/actions/workflows?per_page=100", dict(total_count=1, workflows=[])),
            ("repos/FS-GG/Receiver/actions/workflows?per_page=100", dict(total_count=1, workflows=[dict(path=MODULE.PRODUCER_PATHS[1], state="disabled_manually")])),
            (f"repos/FS-GG/Receiver/commits/{self.fixture['head']}/check-runs?per_page=100", dict(total_count=1, check_runs=[dict(name="routine-eligibility", conclusion="failure")]))]
        for endpoint, value in cases:
            with self.subTest(endpoint=endpoint, value=value):
                api=self.api(); api.overrides[endpoint]=value
                self.assertEqual((self.invoke(api)[0], api.attempts), (2, 0))

    def test_incomplete_file_list_status_and_payload_mutations_refuse(self):
        endpoint="repos/FS-GG/Receiver/pulls/1/files?per_page=100"
        for status in ("modified", "renamed", "removed"):
            api=self.api(); rows=copy.deepcopy(self.fixture["responses"][endpoint]); rows[0]["status"]=status;api.overrides[endpoint]=rows
            self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.overrides[endpoint]=self.fixture["responses"][endpoint][:-1]
        self.assertEqual(self.invoke(api)[0],2)
        endpoint=f"repos/FS-GG/Receiver/git/trees/{self.fixture['tree']}?recursive=1"
        for path in MODULE.PRODUCER_PATHS:
            for field,value in (("sha","f"*40),("mode","120000"),("mode","160000"),("mode","100755" if next(r for r in self.fixture['responses'][endpoint]['tree'] if r['path']==path)['mode']=="100644" else "100644")):
                with self.subTest(path=path,field=field):
                    api=self.api();tree=copy.deepcopy(self.fixture['responses'][endpoint]);next(r for r in tree['tree'] if r['path']==path)[field]=value;api.overrides[endpoint]=tree
                    self.assertEqual(self.invoke(api)[0],2)
        for transform in (lambda tree: tree.update(truncated=True), lambda tree: tree['tree'].append(dict(path="extra",mode="100644",type="blob",sha="f"*40)), lambda tree: tree['tree'].pop()):
            api=self.api();tree=copy.deepcopy(self.fixture['responses'][endpoint]);transform(tree);api.overrides[endpoint]=tree
            self.assertEqual(self.invoke(api)[0],2)

    def test_repo_fork_base_head_body_and_source_identity_controls(self):
        variants=[]
        for area,key,value in (("head","sha","f"*40),("head","ref","feature/unbound"),("base","ref","other"),("base","sha","f"*40)):
            pr=copy.deepcopy(self.pr);pr[area][key]=value;variants.append(pr)
        pr=copy.deepcopy(self.pr);pr['head']['repo']['id']=43;variants.append(pr)
        pr=copy.deepcopy(self.pr);pr['body']=pr['body'].replace('source-change','internal-docs');variants.append(pr)
        for pr in variants:self.assertEqual(self.invoke(self.api([pr]))[0],2)
        api=self.api();api.overrides['repos/FS-GG/Receiver']=dict(id=42,full_name='Redirect/Receiver',default_branch='main');self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.overrides[f"repos/FS-GG/.github/compare/{self.fixture['source']}...main"]={};self.assertEqual(self.invoke(api)[0],2)

    def test_existing_partial_installation_and_blob_corruption_refuse(self):
        endpoint=f"repos/FS-GG/Receiver/git/commits/{self.fixture['base']}"
        api=self.api();value=copy.deepcopy(self.fixture['responses'][endpoint]);value['tree']['sha']=self.fixture['tree'];api.overrides[endpoint]=value
        self.assertEqual(self.invoke(api)[0],2)
        row=next(r for r in self.fixture['responses'][f"repos/FS-GG/Receiver/git/trees/{self.fixture['tree']}?recursive=1"]['tree'] if r['path']==MODULE.PRODUCER_PATHS[0])
        endpoint=f"repos/FS-GG/.github/git/blobs/{row['sha']}"
        api=self.api();value=copy.deepcopy(self.fixture['responses'][endpoint]);value['content']=MODULE.base64.b64encode(b'wrong').decode();api.overrides[endpoint]=value
        self.assertEqual(self.invoke(api)[0],2)

    def test_required_and_coherent_checks_must_complete_and_exact_native_provenance(self):
        for field,value in (("bucket","pending"),("state","FAILURE"),("link","https://evil.invalid/check"),("completedAt","")):
            api=self.api();api.required[0][field]=value;self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.required=[];self.assertEqual(self.invoke(api)[0],2)
        for field,value in (("head_sha","f"*40),("run_attempt",2),("conclusion","failure")):
            api=self.api();endpoint='repos/FS-GG/Receiver/actions/jobs/8';job=copy.deepcopy(self.fixture['responses'][endpoint]);job[field]=value;api.overrides[endpoint]=job;self.assertEqual(self.invoke(api)[0],2)
        for status,conclusion in (("in_progress",None),("completed","failure")):
            api=self.api();api.runs[0].update(status=status,conclusion=conclusion);self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.runs[0].update(status='in_progress',conclusion=None)
        api.qualification_selection=lambda *_: selection('reused')
        self.assertEqual(self.invoke(api)[0],2)
        endpoint=f"repos/FS-GG/Receiver/commits/{self.fixture['head']}/check-runs?per_page=100"
        for field,value in (('head_sha','f'*40),('app',{'id':1}),('check_suite',{'id':10}),('details_url','https://evil.invalid')):
            api=self.api();roster=copy.deepcopy(self.fixture['responses'][endpoint]);roster['check_runs'][0][field]=value;api.overrides[endpoint]=roster
            self.assertEqual(self.invoke(api)[0],2)

    def test_last_moment_envelope_race_never_writes(self):
        for field in ('body','base','head'):
            late=copy.deepcopy(self.pr)
            if field=='body':late[field]+=' changed'
            else:late[field]['sha']='f'*40
            api=self.api([self.pr,late]);self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))

    def test_unknown_write_observes_once_and_never_replays_within_attempt(self):
        api=self.api([self.pr,self.pr,self.pr],[MODULE.AmbiguousWrite('lost response')]);code,result=self.invoke(api,True)
        self.assertEqual((code,api.attempts,result.codeDelivery),(3,1,'unknown'))
        api=self.api([self.after]);api.overrides['repos/FS-GG/Receiver/git/ref/heads/main']={'object':{'sha':self.fixture['head']}}
        code,result=self.invoke(api,True)
        self.assertEqual((code,api.attempts,result.producerInstallation),(0,0,'delivered-activation-pending'))

    def test_retained_uncertain_effect_is_readback_only_across_invocations(self):
        first=self.api([self.pr,self.pr,self.pr],[MODULE.AmbiguousWrite('lost response')])
        code,uncertain=self.invoke(first,True);self.assertEqual(code,3)
        prior=asdict(uncertain)
        later=self.api([self.pr]);later.required[0]['bucket']='pending'
        code,result=self.invoke(later,True,prior=prior)
        self.assertEqual((code,later.attempts,result.codeDelivery,result.attempts),(3,0,'unknown',0))
        later=self.api([self.after]);later.overrides['repos/FS-GG/Receiver/git/ref/heads/main']={'object':{'sha':self.fixture['head']}}
        code,result=self.invoke(later,True,prior=prior)
        self.assertEqual((code,later.attempts,result.producerInstallation),(0,0,'delivered-activation-pending'))
        for field,value in (('repo','Other/Repo'),('pr',2),('expectedHead','f'*40),('producerSource','f'*40),
                            ('attempts',None),('attempts',True),('attempts',2),('schema','other'),('baseSha','f'*40)):
            bad=copy.deepcopy(prior);bad[field]=value;api=self.api()
            with self.subTest(field=field),self.assertRaises(RuntimeError):self.invoke(api,True,prior=bad)
            self.assertEqual(api.attempts,0)
        missing=copy.deepcopy(prior);missing.pop('attempts')
        with self.assertRaises(RuntimeError):self.invoke(self.api(),True,prior=missing)
        path=self.directory/'prior.json';path.write_text(json.dumps(prior))
        self.assertEqual(MODULE.installation_prior_result(str(path)),prior)
        for raw in ('{"attempts":1,"attempts":1}', '{}', '{', ' '*65537):
            path.write_text(raw)
            with self.assertRaises(RuntimeError):MODULE.installation_prior_result(str(path))

    def test_postmerge_wrong_protected_payload_stays_disputed(self):
        api=self.api([self.pr,self.pr,self.after],[{'merged':True,'sha':self.fixture['head']}]);code,result=self.invoke(api,True)
        self.assertEqual((code,api.attempts,result.codeDelivery,result.producerInstallation),(4,1,'delivered','disputed'))

    def test_delivered_bootstrap_does_not_unlock_ordinary_missing_eligibility(self):
        api=self.api();api.routine_eligibility=mock.Mock(side_effect=RuntimeError('missing'))
        code,result=MODULE.summarize(api,repo='FS-GG/Receiver',pr_number=1,expected_head=self.fixture['head'],merge_method='squash',publication_required=False,apply=False)
        self.assertEqual((code,result.routineEligibility,api.attempts),(2,'unresolved',0))

    def test_actual_owner_validator_security_controls_on_inert_git_repository(self):
        spec=importlib.util.spec_from_file_location('installation_trusted_gate', self.owner / 'scripts/check-claim-generation.py')
        gate=importlib.util.module_from_spec(spec);sys.modules[spec.name]=gate;spec.loader.exec_module(gate)
        def trusted_git(command, **kwargs):
            self.assertEqual(command[0], 'git')
            self.assertIn(command[1], ('show','diff'))
            raw=fixture_git(self.receiver,*command[1:])
            return subprocess.CompletedProcess(command,0,stdout=raw.decode(),stderr='')
        args=SimpleNamespace(base_sha=self.fixture['head'],head_sha=self.successor,head_ref='routine/ordinary',
            routine_policy='.fsgg/routine-development.json',routine_policy_ref=self.fixture['head'])
        body=f'<!-- fsgg:routine-development/v1 head={self.successor} operation=source-change -->'
        with mock.patch.object(gate,'subprocess',SimpleNamespace(run=trusted_git)):
            self.assertIsNone(gate.evaluate_routine(args,body)[0])
            for candidate in ('missing',body+body,body.replace(self.successor,'f'*40),body.replace('source-change','publish')):
                self.assertIsNotNone(gate.evaluate_routine(args,candidate)[0])
        spec=importlib.util.spec_from_file_location('installation_trusted_envelope', self.owner / 'scripts/check-routine-eligibility-envelope.py')
        envelope=importlib.util.module_from_spec(spec);spec.loader.exec_module(envelope)
        body_path=self.directory/'body.txt';body_path.write_text(body)
        argv=['envelope','--git-dir',str(self.receiver),'--repository','FS-GG/Receiver','--default-branch','main',
            '--base-ref','main','--base-revision',self.fixture['head'],'--base-sha',self.fixture['head'],
            '--head-ref','routine/ordinary','--head-revision',self.successor,'--head-sha',self.successor,
            '--body',str(body_path),'--validator',str(self.owner/'scripts/check-claim-generation.py')]
        def transport(command, **kwargs):
            if command[0]=='git':
                self.assertEqual(command[1:3],['-C',str(self.receiver)])
                return subprocess.CompletedProcess(command,0,stdout=fixture_git(self.receiver,*command[3:]).decode(),stderr='')
            self.assertEqual(command[:2],[sys.executable,str(self.owner/'scripts/check-claim-generation.py')])
            self.assertEqual(kwargs['cwd'],self.receiver)
            return subprocess.CompletedProcess(command,0)
        with mock.patch.object(envelope,'subprocess',SimpleNamespace(run=transport)),mock.patch.object(sys,'argv',argv):
            self.assertEqual(envelope.main(),0)
        for flag,value in (('--base-ref','other'),('--base-sha','f'*40),('--head-sha','f'*40),('--head-ref','feature/wrong')):
            wrong=argv.copy();wrong[wrong.index(flag)+1]=value
            with mock.patch.object(envelope,'subprocess',SimpleNamespace(run=transport)),mock.patch.object(sys,'argv',wrong):
                with self.assertRaises(SystemExit):envelope.main()

    def test_final_absence_required_and_owner_closure_races_refuse_without_effect(self):
        for failure in ('workflow','required','owner'):
            api=self.api([self.pr,self.pr]);original=api.installation_contract;calls=0
            def changed(*args,**kwargs):
                nonlocal calls
                calls+=1
                if calls==2:
                    if failure=='workflow':api.overrides['repos/FS-GG/Receiver/actions/workflows?per_page=100']=dict(total_count=1,workflows=[dict(path=MODULE.PRODUCER_PATHS[1])])
                    elif failure=='required':api.required[0]['bucket']='pending'
                    else:api.overrides[f"repos/FS-GG/.github/compare/{self.fixture['source']}...main"]=dict(status='behind',merge_base_commit={'sha':self.fixture['obsolete']})
                return original(*args,**kwargs)
            with mock.patch.object(api,'installation_contract',side_effect=changed):
                self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))

    def test_actual_bounded_native_transport_rejects_unknown_and_malformed_read(self):
        api=MODULE.GhApi()
        with mock.patch.object(MODULE,'bounded_watch_query',return_value=(0,b'{"id":42}')) as query:
            self.assertEqual(api.installation_read('repos/FS-GG/Receiver'),{'id':42})
            self.assertEqual(query.call_args.args[0],['gh','api','repos/FS-GG/Receiver'])
            self.assertEqual(query.call_args.kwargs['max_bytes'],1048576)
        for code,body in ((1,b'{}'),(0,b'not JSON')):
            with mock.patch.object(MODULE,'bounded_watch_query',return_value=(code,body)):
                with self.assertRaises(RuntimeError):api.installation_read('repos/FS-GG/Receiver')


if __name__ == "__main__":
    def inert(event, args):
        if event == "subprocess.Popen":
            executable, command, cwd, _env = args
            if (executable == GIT_FIXTURE_EXECUTABLE and isinstance(command, list)
                    and command[:3] == [GIT_FIXTURE_EXECUTABLE, "-c", "core.hooksPath=/dev/null"]
                    and command[3] in ("init", "add", "commit", "rev-parse", "ls-tree", "cat-file", "show", "diff")
                    and str(cwd) in GIT_FIXTURE_ROOTS):
                return
        if event in ("socket.connect", "subprocess.Popen", "os.system", "os.exec"):
            raise RuntimeError("offline delivery qualification refuses native effects")
    sys.addaudithook(inert)
    unittest.main()

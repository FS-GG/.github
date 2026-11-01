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
import zlib
from dataclasses import asdict, fields
from datetime import datetime, timezone
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]
# The board-gate mutation controls (routine-delivery-board-mutate.py) point this at a mutated copy.
SPEC = importlib.util.spec_from_file_location(
    "routine_delivery", os.environ.get("ROUTINE_DELIVERY_UNDER_TEST") or ROOT / "tools/routine-delivery.py"
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


class DeliveryEvidenceTests(unittest.TestCase):
    def fixture(self, checkout=HEAD, delivered_tree="c" * 40):
        _, summary = MODULE.summarize(FakeApi([merged()]), repo="FS-GG/.github", pr_number=1,
            expected_head=HEAD, merge_method="squash", publication_required=False, apply=False)
        pr = dict(merged(), changed_files=1)
        fact = dict(schema="fsgg.tested-checkout/1", repository=summary.repo, pr=1,
            candidate=HEAD, base=summary.baseSha, run=7, attempt=1,
            job="semantic-skill-quality", commit=checkout, tree="c" * 40)
        native_run = dict(id=7, run_attempt=1, head_sha=HEAD, event="pull_request",
            path=".github/workflows/skill-quality.yml", repository=dict(full_name=summary.repo),
            pull_requests=[dict(number=1, head=dict(sha=HEAD), base=dict(sha=summary.baseSha))],
            status="completed", conclusion="success")
        responses = {
            f"git/commits/{MERGE}": dict(sha=MERGE, tree=dict(sha=delivered_tree)),
            f"git/commits/{checkout}": dict(sha=checkout, tree=dict(sha=fact["tree"]),
                parents=[dict(sha=summary.baseSha), dict(sha=HEAD)]),
            "pulls/1/files?per_page=25&page=1": [dict(filename="tools/routine-delivery.py", status="modified")],
            "actions/runs/7": native_run,
            "actions/runs/7/artifacts?per_page=100": dict(total_count=1, artifacts=[dict(
                id=11, name="tested-checkout-7-1", expired=False, size_in_bytes=1024)]),
        }
        api = mock.Mock(deadline=100, clock=lambda: 0)
        api.get_pr.return_value = pr
        def read(command, accepted):
            if command[1:3] == ["pr", "checks"]:
                return 0, []
            return 0, responses[command[-1].split(f"repos/{summary.repo}/", 1)[1]]
        api._watch_read.side_effect = read
        def archive(*args, **kwargs):
            return 0, selection_archive([("checkout.json", json.dumps(fact).encode())])
        api.query.side_effect = archive
        return summary, api, fact, responses

    def test_changed_files_are_projected_before_bounded_read_and_complete_over_pages(self):
        summary,api,_,responses=self.fixture()
        api.get_pr.return_value['changed_files']=90
        rows=[dict(filename=f'src/file-{i}.fs',status='renamed' if i==0 else 'modified',
                   previous_filename='src/original.fs' if i==0 else None) for i in range(90)]
        for page in range(1,5):responses[f'pulls/1/files?per_page=25&page={page}']=rows[(page-1)*25:page*25]
        result=MODULE.delivery_evidence(summary,7,api=api,assembled=True)['deliveryEvidence']
        self.assertEqual(result['changedFiles'],rows)
        commands=[c.args[0] for c in api._watch_read.call_args_list if '/files?' in c.args[0][-1]]
        self.assertEqual(len(commands),4)
        self.assertTrue(all(c[2:4]==['--jq','[.[] | {filename, previous_filename, status}]'] for c in commands))
        for defect in ('missing','duplicate','renamed','count-drift','denied'):
            with self.subTest(defect=defect):
                second=copy.deepcopy(responses);pr=copy.deepcopy(api.get_pr.return_value)
                if defect=='missing':second['pulls/1/files?per_page=25&page=4'].pop()
                if defect=='duplicate':second['pulls/1/files?per_page=25&page=2'][0]=second['pulls/1/files?per_page=25&page=1'][0]
                if defect=='renamed':second['pulls/1/files?per_page=25&page=1'][0]['previous_filename']=None
                def read(command,accepted):
                    if command[1:3]==['pr','checks']:return 0,[]
                    endpoint=command[-1].split('repos/FS-GG/.github/',1)[1]
                    if defect=='denied' and endpoint.endswith('page=2'):raise RuntimeError('denied')
                    return 0,second[endpoint]
                api._watch_read.side_effect=read
                if defect=='count-drift':
                    final=copy.deepcopy(pr);final['changed_files']=89;api.get_pr.side_effect=[pr,final]
                else:api.get_pr.side_effect=None;api.get_pr.return_value=pr
                evidence=MODULE.delivery_evidence(summary,7,api=api,assembled=True)['deliveryEvidence']
                self.assertIsNone(evidence['changedFiles'])
                self.assertTrue(any('unavailable' in value for value in evidence['exceptions']))

    def automatic_fixture(self):
        summary, api, fact, responses = self.fixture(checkout="e" * 40)
        summary = MODULE.replace(summary, coherentValidation="passed", routineEligibility="passed")
        responses["actions/runs/7"]["path"] = ".github/workflows/coherence.yml"
        fact["job"] = "projection"
        row = dict.fromkeys(MODULE.WATCH_FIELDS.split(","), "")
        row.update(name="projection", workflow="coherence", event="pull_request", state="SUCCESS",
                   bucket="pass", link="https://github.com/FS-GG/.github/actions/runs/7/job/12")
        responses["actions/jobs/12"] = dict(id=12, run_id=7, run_attempt=1, name="projection", head_sha=HEAD)
        original = api._watch_read.side_effect
        def read(command, accepted):
            return (0, [row]) if command[1:3] == ["pr", "checks"] else original(command, accepted)
        api._watch_read.side_effect = read
        return summary, api, fact, responses, row

    def test_automatic_required_projection_discovers_run_and_assesses_delivery(self):
        summary, api, _, _, _ = self.automatic_fixture()
        result = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
        evidence = result.pop("deliveryEvidence")
        self.assertEqual(result, asdict(summary))
        self.assertEqual((evidence["assessment"], evidence["issues"], evidence["run"]["id"]), ("delivered", [], 7))
        self.assertEqual(evidence["testedCheckout"]["kind"], "synthetic-pr-merge")

    def test_native_ordinary_docs_route_does_not_require_routine_route_too(self):
        for delivered in (False, True):
            with self.subTest(delivered=delivered):
                _, api, _, _, _ = self.automatic_fixture()
                pr = dict(merged() if delivered else opened(), changed_files=1)
                pr["head"]["ref"] = "codex/documentation-update"
                # No routine marker: the existing native route explicitly says not-required.
                source = FakeApi([pr], runs=[run("completed", "success")])
                code, summary = MODULE.summarize(source, repo="FS-GG/.github", pr_number=1,
                    expected_head=HEAD, merge_method="merge", publication_required=False,
                    apply=False, coherent_workflow="coherence.yml")
                self.assertEqual((code, summary.routineEligibility, summary.coherentValidation),
                                 (0, "not-required", "passed"))
                api.get_pr.return_value = pr
                output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
                evidence = output.pop("deliveryEvidence")
                self.assertEqual(output, asdict(summary))
                self.assertEqual(evidence["assessment"], "delivered" if delivered else "ready")
                self.assertEqual(evidence["issues"], [])
                self.assertEqual(source.attempts, 0)

    def test_ordinary_docs_route_does_not_mask_original_coherent_refusal(self):
        _, api, _, _, _ = self.automatic_fixture()
        pr = dict(opened(), changed_files=1)
        pr["head"]["ref"] = "codex/documentation-update"
        source = FakeApi([pr], runs=[run("completed", "failure")])
        code, summary = MODULE.summarize(source, repo="FS-GG/.github", pr_number=1,
            expected_head=HEAD, merge_method="merge", publication_required=False,
            apply=True, coherent_workflow="coherence.yml")
        self.assertEqual((code, summary.outcome, summary.routineEligibility, source.attempts),
                         (2, "refused", "not-required", 0))
        api.get_pr.return_value = pr
        output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
        evidence = output.pop("deliveryEvidence")
        self.assertEqual(output, asdict(summary))
        self.assertEqual(evidence["assessment"], "attention")
        self.assertIn("coherent-validation-failed", evidence["issues"])
        self.assertIn("delivery-refused", evidence["issues"])

    def test_missing_unresolved_and_installation_routine_states_still_need_attention(self):
        for state in ("unobserved", "unresolved", "unavailable-first-install", "unknown"):
            summary, api, _, _, _ = self.automatic_fixture()
            summary = MODULE.replace(summary, routineEligibility=state)
            output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
            evidence = output.pop("deliveryEvidence")
            self.assertEqual(output, asdict(summary))
            self.assertEqual(evidence["assessment"], "attention")
            self.assertIn("routine-eligibility-" + state, evidence["issues"])

    def test_automatic_missing_duplicate_wrong_and_changed_projection_refuse(self):
        for case in ("missing", "duplicate", "foreign-link", "wrong-workflow", "changed"):
            with self.subTest(case=case):
                summary, api, _, _, row = self.automatic_fixture()
                rows = [row]
                if case == "missing": rows = []
                if case == "duplicate": rows = [row, row]
                if case == "foreign-link": row["link"] = row["link"].replace("FS-GG", "other")
                if case == "wrong-workflow": row["workflow"] = "skill-quality"
                original, reads = api._watch_read.side_effect, 0
                def read(command, accepted):
                    nonlocal reads
                    if command[1:3] == ["pr", "checks"]:
                        reads += 1
                        return 0, ([dict(row, link=row["link"] + "0")] if case == "changed" and reads > 1 else rows)
                    return original(command, accepted)
                api._watch_read.side_effect = read
                evidence = MODULE.delivery_evidence(summary, None, api=api, assembled=True)["deliveryEvidence"]
                self.assertIsNone(evidence["testedCheckout"])
                self.assertEqual(evidence["assessment"], "attention")
                self.assertTrue(evidence["exceptions"])

    def test_automatic_stale_job_attempt_and_crossed_producer_refuse(self):
        for key, value in (("run_attempt", 2), ("head_sha", "f" * 40), ("name", "unrelated"), ("run_id", 8)):
            summary, api, _, responses, _ = self.automatic_fixture()
            responses["actions/jobs/12"][key] = value
            result = MODULE.delivery_evidence(summary, None, api=api)
            self.assertIsNone(result["testedCheckout"])
        summary, api, fact, responses, _ = self.automatic_fixture()
        responses["actions/runs/7"]["path"] = ".github/workflows/skill-quality.yml"
        fact["job"] = "semantic-skill-quality"
        self.assertIsNone(MODULE.delivery_evidence(summary, None, api=api)["testedCheckout"])

    def test_automatic_ready_dispute_failure_and_mismatch_preserve_original(self):
        for state in ("ready", "disputed", "failed-check", "different-tree"):
            summary, api, _, responses, row = self.automatic_fixture()
            if state == "ready":
                summary = MODULE.replace(summary, outcome="ready", codeDelivery="not-delivered", mergeCommit=None)
                api.get_pr.return_value = dict(opened(), changed_files=1)
            if state == "disputed":
                summary = MODULE.replace(summary, outcome="delivered-disputed", coherentValidation="disputed")
            if state == "failed-check": row.update(bucket="fail", state="FAILURE")
            if state == "different-tree": responses[f"git/commits/{MERGE}"]["tree"]["sha"] = "f" * 40
            output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
            evidence = output.pop("deliveryEvidence")
            self.assertEqual(output, asdict(summary))
            self.assertEqual(evidence["assessment"], "ready" if state == "ready" else "attention")

    def test_large_automatic_result_roundtrips_existing_custody_bound(self):
        summary, api, _, _, row = self.automatic_fixture()
        original = api._watch_read.side_effect
        def read(command, accepted):
            return (0, [dict(row, name="x" * 500 + str(i)) for i in range(127)] + [row]) if command[1:3] == ["pr", "checks"] else original(command, accepted)
        api._watch_read.side_effect = read
        output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
        self.assertIsNone(output["deliveryEvidence"]["requiredChecks"])
        self.assertIn("result-populations-over-budget", output["deliveryEvidence"]["exceptions"])
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "result.json"
            path.write_text(json.dumps(output, separators=(",", ":"), sort_keys=True) + "\n")
            self.assertLessEqual(path.stat().st_size, 65536)
            self.assertEqual(MODULE.installation_prior_result(str(path)), output)

    def test_default_ordinary_collection_is_repository_scoped(self):
        summary, _, _, _ = self.fixture()
        for repo, expected in (("FS-GG/.github", True), ("FS-GG/other", False)):
            output = io.StringIO()
            with mock.patch.object(MODULE, "summarize", return_value=(0, MODULE.replace(summary, repo=repo))), \
                 mock.patch.object(MODULE, "delivery_evidence", return_value=dict(asdict(summary), deliveryEvidence={})) as evidence, \
                 contextlib.redirect_stdout(output), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(MODULE.main(["--repo", repo, "--pr", "1", "--head", HEAD,
                    "--telemetry-unavailable", "local-clr-resource-hold"]), 0)
            self.assertEqual(evidence.called, expected)
            if expected: self.assertEqual(evidence.call_args.args[1], None)

    def test_rerun_during_collection_and_installation_default_refuse_extension(self):
        summary, api, _, responses, _ = self.automatic_fixture()
        original, reads = api._watch_read.side_effect, 0
        def read(command, accepted):
            nonlocal reads
            if command[-1].endswith("actions/runs/7"):
                reads += 1
                if reads > 1:
                    return 0, dict(responses["actions/runs/7"], run_attempt=2)
            return original(command, accepted)
        api._watch_read.side_effect = read
        output = MODULE.delivery_evidence(summary, None, api=api, assembled=True)
        self.assertIsNone(output["deliveryEvidence"]["testedCheckout"])
        self.assertIn("final-producer-run-unavailable", output["deliveryEvidence"]["exceptions"])
        capture = io.StringIO()
        with mock.patch.object(MODULE, "summarize", return_value=(0, summary)), \
             mock.patch.object(MODULE, "delivery_evidence") as evidence, \
             contextlib.redirect_stdout(capture), contextlib.redirect_stderr(io.StringIO()):
            self.assertEqual(MODULE.main(["--repo", summary.repo, "--pr", "1", "--head", HEAD,
                "--install-routine-producer", "--routine-producer-source", "f" * 40,
                "--coherent-workflow", "coherence.yml", "--telemetry-unavailable", "local-clr-resource-hold"]), 0)
        evidence.assert_not_called()
        self.assertEqual(json.loads(capture.getvalue()), asdict(MODULE.replace(summary, telemetryHealth="unavailable")))

    def test_one_reader_accepts_exact_live_and_historic_producer_pairs(self):
        pairs = [("coherence.yml", "projection", "github-pr-checkout"),
                 ("skill-quality.yml", "semantic-skill-quality", "skill-quality-pilot")]
        for workflow, job, scope in pairs:
            with self.subTest(workflow=workflow):
                summary, api, fact, responses = self.fixture(checkout="e" * 40)
                responses["actions/runs/7"]["path"] = ".github/workflows/" + workflow
                fact["job"] = job
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertEqual((result["scope"], result["testedCheckout"]["job"], result["parity"]),
                    (scope, job, "identical-tree"))
                self.assertEqual(result["exceptions"], [])

    def test_unsupported_or_crossed_workflow_job_pair_cannot_supply_checkout(self):
        for workflow, job in [("coherence.yml", "semantic-skill-quality"),
                ("skill-quality.yml", "projection"), ("unrelated.yml", "projection")]:
            with self.subTest(workflow=workflow, job=job):
                summary, api, fact, responses = self.fixture(checkout="e" * 40)
                responses["actions/runs/7"]["path"] = ".github/workflows/" + workflow
                fact["job"] = job
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertIsNone(result["testedCheckout"])
                self.assertEqual(result["parity"], "unavailable")

    def test_universal_producer_is_single_and_runs_in_existing_unfiltered_projection_job(self):
        import yaml  # Existing requirements-test.txt dependency; no new package.
        emitters = []
        for path in (ROOT / ".github/workflows").glob("*.yml"):
            workflow = yaml.load(path.read_text(), Loader=yaml.BaseLoader)
            for job_name, job in workflow.get("jobs", {}).items():
                for step in job.get("steps", []):
                    if "fsgg.tested-checkout/1" in step.get("run", ""):
                        emitters.append((path.name, job_name, workflow, job, step))
        self.assertEqual([(row[0], row[1]) for row in emitters], [("coherence.yml", "projection")])
        suite = yaml.load((ROOT / ".github/workflows/skill-quality.yml").read_text(), Loader=yaml.BaseLoader)
        for event in ("pull_request", "push"):
            self.assertIn(".github/workflows/coherence.yml", suite["on"][event]["paths"])
        _, _, workflow, job, capture = emitters[0]
        self.assertEqual(set(workflow["on"]["pull_request"]), {"types"})
        # .github#4730: `edited` moved to claim-generation.yml with the only job that reads the PR body. The
        # tested-checkout fact records head/base/PR number, none of which a body edit changes, so the
        # producer's run for the current head is still the one delivery evidence selects.
        self.assertEqual(workflow["on"]["pull_request"]["types"], ["opened", "synchronize", "reopened"])
        self.assertNotIn("if", job)
        self.assertEqual(capture["if"], "github.event_name == 'pull_request'")
        self.assertEqual(capture["continue-on-error"], "true")
        steps = job["steps"]
        checkout = next(step for step in steps if step.get("uses", "").startswith("actions/checkout@"))
        validation = next(step for step in steps if step.get("name") == "Run projection gate negative controls")
        upload = next(step for step in steps if step.get("with", {}).get("name", "").startswith("tested-checkout-"))
        self.assertLess(steps.index(checkout), steps.index(capture))
        self.assertLess(steps.index(capture), steps.index(validation))
        self.assertLess(steps.index(validation), steps.index(upload))
        self.assertEqual(upload["if"], "always() && github.event_name == 'pull_request'")
        self.assertEqual(upload["continue-on-error"], "true")
        self.assertNotIn("continue-on-error", validation)
        self.assertEqual(upload["with"]["retention-days"], "7")
        self.assertEqual(upload["with"]["name"], "tested-checkout-${{ github.run_id }}-${{ github.run_attempt }}")

    def test_workflow_producer_emits_exact_observed_git_identity_with_existing_schema(self):
        import yaml
        workflow = yaml.load((ROOT / ".github/workflows/coherence.yml").read_text(), Loader=yaml.BaseLoader)
        step = next(step for step in workflow["jobs"]["projection"]["steps"]
                    if "fsgg.tested-checkout/1" in step.get("run", ""))
        with tempfile.TemporaryDirectory() as directory:
            environment = dict(os.environ, CHECKOUT_CANDIDATE=HEAD, CHECKOUT_BASE="d" * 40,
                CHECKOUT_PR="1", GITHUB_REPOSITORY="FS-GG/.github", GITHUB_RUN_ID="7",
                GITHUB_RUN_ATTEMPT="1", GITHUB_JOB="projection", RUNNER_TEMP=directory)
            body = step["run"].split("python3 - <<'PYTHON'\n", 1)[1].rsplit("PYTHON", 1)[0]
            observed = {"HEAD": "e" * 40, "HEAD^{tree}": "c" * 40}
            def git_read(command, **options):
                self.assertEqual(command[:2], ["git", "rev-parse"])
                self.assertTrue(options["text"])
                return observed[command[2]] + "\n"
            with mock.patch.dict(os.environ, environment, clear=True), \
                 mock.patch.object(subprocess, "check_output", side_effect=git_read) as reads:
                exec(compile(body, ".github/workflows/coherence.yml:projection", "exec"), {})
            self.assertEqual(reads.call_count, 2)
            fact = json.loads((pathlib.Path(directory) / "checkout.json").read_text())
        self.assertEqual(set(fact), {"schema", "repository", "pr", "candidate", "base", "run", "attempt", "job", "commit", "tree"})
        self.assertEqual((fact["schema"], fact["job"], fact["candidate"]), ("fsgg.tested-checkout/1", "projection", HEAD))
        self.assertEqual((fact["commit"], fact["tree"]), (observed["HEAD"], observed["HEAD^{tree}"]))

    def test_candidate_and_synthetic_checkout_use_native_tree_parity(self):
        for checkout, kind in [(HEAD, "candidate"), ("e" * 40, "synthetic-pr-merge")]:
            with self.subTest(kind=kind):
                summary, api, _, _ = self.fixture(checkout)
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertEqual((result["testedCheckout"]["kind"], result["parity"]), (kind, "identical-tree"))
                self.assertEqual(result["deliveredCommit"], MERGE)
                self.assertEqual(result["exceptions"], [])
                self.assertEqual(len(result["changedFiles"]), 1)

    def test_run_pr_link_disappears_after_native_merge_without_base_drift(self):
        # PR4597/run38860185845 retained head/base but returned pull_requests:[] after merge.
        summary, api, _, responses = self.fixture(checkout="e" * 40)
        branch = api.get_pr.return_value["head"]["ref"]
        run = responses["actions/runs/7"]
        run["head_branch"] = branch
        api.get_pr.return_value = dict(opened(), changed_files=1)
        prepared = MODULE.replace(summary, outcome="ready", codeDelivery="not-delivered", mergeCommit=None)
        before = MODULE.delivery_evidence(prepared, 7, api=api)
        self.assertEqual((before["testedCheckout"]["association"], before["parity"]),
            ("native-pr-link", "not-delivered"))
        api.get_pr.return_value = dict(merged(), changed_files=1)
        run["pull_requests"] = []
        after = MODULE.delivery_evidence(summary, 7, api=api)
        self.assertEqual((after["testedCheckout"]["association"], after["parity"]),
            ("merged-pr-exact-checkout-parents", "identical-tree"))
        self.assertEqual(after["exceptions"], [])
        self.assertEqual(before["base"], after["base"])
        self.assertEqual(before["candidate"], after["candidate"])

    def test_merged_missing_run_link_still_refuses_unrelated_or_unproven_identity(self):
        cases = ("absent-links", "null-links", "other-pr", "branch", "open", "ambiguous",
                 "candidate-only", "wrong-parent", "reversed-parents", "head", "base", "late-base")
        for change in cases:
            with self.subTest(change=change):
                checkout = HEAD if change == "candidate-only" else "e" * 40
                summary, api, _, responses = self.fixture(checkout=checkout)
                run = responses["actions/runs/7"]
                run.update(head_branch=api.get_pr.return_value["head"]["ref"], pull_requests=[])
                if change == "absent-links": del run["pull_requests"]
                elif change == "null-links": run["pull_requests"] = None
                elif change == "other-pr": run["pull_requests"] = [dict(number=2, head=dict(sha=HEAD), base=dict(sha=summary.baseSha))]
                elif change == "branch": run["head_branch"] = "unrelated"
                elif change == "open": api.get_pr.return_value = dict(opened(), changed_files=1)
                elif change == "ambiguous": summary = MODULE.replace(summary, codeDelivery="unknown", mergeCommit=None)
                elif change == "wrong-parent": responses[f"git/commits/{checkout}"]["parents"][0]["sha"] = "f" * 40
                elif change == "reversed-parents": responses[f"git/commits/{checkout}"]["parents"].reverse()
                elif change in ("head", "base"): api.get_pr.return_value[change]["sha"] = "f" * 40
                elif change == "late-base":
                    original = api.get_pr.return_value
                    changed = copy.deepcopy(original); changed["base"]["sha"] = "f" * 40
                    api.get_pr.side_effect = [original, changed]
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertIsNone(result["testedCheckout"])
                self.assertEqual(result["parity"], "unavailable")

    def test_entire_run_pr_link_population_is_validated_before_selection(self):
        valid = dict(number=1, head=dict(sha=HEAD), base=dict(sha="d" * 40))
        wrong_head = copy.deepcopy(valid); wrong_head["head"]["sha"] = "f" * 40
        wrong_base = copy.deepcopy(valid); wrong_base["base"]["sha"] = "f" * 40
        malformed = [None, {}, dict(number=True), dict(number=2, head=None, base=dict(sha="d" * 40)),
                     dict(number=2, head=dict(sha="bad"), base=dict(sha="d" * 40))]
        for bad in [wrong_head, wrong_base, valid, *malformed]:
            for links in ([valid, bad], [bad, valid]):
                with self.subTest(links=links):
                    summary, api, _, responses = self.fixture(checkout="e" * 40)
                    responses["actions/runs/7"]["pull_requests"] = links
                    result = MODULE.delivery_evidence(summary, 7, api=api)
                    self.assertIsNone(result["testedCheckout"])
                    self.assertEqual(result["parity"], "unavailable")
                    self.assertIn("tested-checkout-unavailable", result["exceptions"])
        # A fully formed different-PR row remains permissible; it cannot satisfy the target join.
        summary, api, _, responses = self.fixture(checkout="e" * 40)
        responses["actions/runs/7"]["pull_requests"] = [dict(valid, number=2), valid]
        self.assertEqual(MODULE.delivery_evidence(summary, 7, api=api)["parity"], "identical-tree")

    def test_final_branch_and_native_merged_predicate_are_rechecked(self):
        for fallback in (False, True):
            for change in ("branch", "merged", "state", "merged_at"):
                with self.subTest(fallback=fallback, change=change):
                    summary, api, _, responses = self.fixture(checkout="e" * 40)
                    original = api.get_pr.return_value
                    if fallback:
                        responses["actions/runs/7"].update(head_branch=original["head"]["ref"], pull_requests=[])
                    changed = copy.deepcopy(original)
                    if change == "branch": changed["head"]["ref"] = "unrelated"
                    elif change == "merged": changed["merged"] = False
                    elif change == "state": changed["state"] = "open"
                    else: changed["merged_at"] = None
                    api.get_pr.side_effect = [original, changed]
                    result = MODULE.delivery_evidence(summary, 7, api=api)
                    self.assertIsNone(result["testedCheckout"])
                    self.assertIsNone(result["changedFiles"])
                    self.assertIsNone(result["requiredChecks"])
                    self.assertEqual(result["parity"], "unavailable")
                    self.assertIn("final-pr-identity-unavailable", result["exceptions"])

    def test_merged_association_fallback_reports_tree_mismatch_and_preserves_dispute(self):
        summary, api, _, responses = self.fixture(checkout="e" * 40, delivered_tree="f" * 40)
        responses["actions/runs/7"].update(head_branch=api.get_pr.return_value["head"]["ref"], pull_requests=[])
        summary = MODULE.replace(summary, outcome="delivered-disputed", coherentValidation="disputed")
        result = MODULE.delivery_evidence(summary, 7, api=api)
        self.assertEqual(result["parity"], "different-tree")
        self.assertEqual(summary.outcome, "delivered-disputed")
        self.assertEqual(summary.coherentValidation, "disputed")

    def test_different_delivered_tree_is_explicit(self):
        summary, api, _, _ = self.fixture(delivered_tree="f" * 40)
        result = MODULE.delivery_evidence(summary, 7, api=api)
        self.assertEqual(result["parity"], "different-tree")
        self.assertEqual(summary.outcome, "delivered")

    def test_artifact_tree_must_match_native_checkout(self):
        summary, api, fact, _ = self.fixture()
        fact["tree"] = "f" * 40
        result = MODULE.delivery_evidence(summary, 7, api=api)
        self.assertIsNone(result["testedCheckout"])
        self.assertEqual(result["parity"], "unavailable")

    def test_changed_head_or_base_invalidates_sample(self):
        for stage in (0, 1):
            for field in ("head", "base"):
                with self.subTest(stage=stage, field=field):
                    summary, api, _, _ = self.fixture()
                    original = api.get_pr.return_value
                    changed = copy.deepcopy(original); changed[field]["sha"] = "f" * 40
                    api.get_pr.side_effect = [changed, original] if stage == 0 else [original, changed]
                    result = MODULE.delivery_evidence(summary, 7, api=api)
                    self.assertIsNone(result["testedCheckout"])
                    self.assertEqual(result["parity"], "unavailable")

    def test_missing_expired_ambiguous_and_misbound_artifacts(self):
        for change in ("missing", "expired", "duplicate", "attempt", "candidate", "base", "job", "run", "size"):
            with self.subTest(change=change):
                summary, api, fact, responses = self.fixture()
                population = responses["actions/runs/7/artifacts?per_page=100"]
                if change == "missing": population.update(total_count=0, artifacts=[])
                elif change == "expired": population["artifacts"][0]["expired"] = True
                elif change == "duplicate": population.update(total_count=2, artifacts=population["artifacts"] * 2)
                elif change == "size": population["artifacts"][0]["size_in_bytes"] = 65537
                else: fact[change] = "wrong"
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertIsNone(result["testedCheckout"])
                self.assertEqual(result["parity"], "unavailable")
                self.assertEqual(result["changedFiles"][0]["status"], "modified")

    def test_absent_evidence_does_not_hide_independent_results(self):
        summary, api, _, responses = self.fixture()
        del responses["actions/runs/7"]
        result = MODULE.delivery_evidence(summary, 7, api=api)
        self.assertEqual(result["deliveredTree"], "c" * 40)
        self.assertEqual(result["requiredChecks"], [])
        self.assertIsNone(result["testedCheckout"])

    def test_late_coherent_failure_keeps_original_dispute(self):
        api = FakeApi([opened(), merged()], [dict(merged=True, sha=MERGE)],
            runs=[run()], selections={7: selection("reused")},
            job_reads=[[], [dict(status="completed", conclusion="failure")]])
        code, summary = MODULE.summarize(api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=False, apply=True, coherent_workflow="coherent.yml")
        _, observer, _, _ = self.fixture()
        result = MODULE.delivery_evidence(summary, 7, api=observer)
        self.assertEqual((code, summary.outcome, summary.coherentValidation), (4, "delivered-disputed", "disputed"))
        self.assertEqual(result["parity"], "identical-tree")

    def test_ambiguous_merge_cannot_gain_delivery_from_checkout(self):
        code, summary = MODULE.summarize(FakeApi([opened(), opened()], [MODULE.AmbiguousWrite("lost")]),
            repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
            merge_method="squash", publication_required=False, apply=True)
        _, observer, _, _ = self.fixture()
        result = MODULE.delivery_evidence(summary, 7, api=observer)
        self.assertEqual((code, summary.codeDelivery, summary.attempts), (3, "unknown", 1))
        self.assertIsNone(result["deliveredCommit"])
        self.assertEqual(result["parity"], "unavailable")

    def test_corrupt_deflate_and_eof_preserve_independent_evidence(self):
        summary, api, fact, _ = self.fixture()
        raw = bytearray(selection_archive([("checkout.json", json.dumps(fact).encode())]))
        offset = 30 + int.from_bytes(raw[26:28], "little") + int.from_bytes(raw[28:30], "little")
        raw[offset] = (raw[offset] & ~7) | 7  # reserved DEFLATE block type
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            with self.assertRaises(zlib.error):
                archive.read("checkout.json")
        for failure in (bytes(raw), EOFError("truncated archive")):
            with self.subTest(failure=type(failure).__name__):
                api.query.side_effect = failure if isinstance(failure, Exception) else None
                api.query.return_value = (0, failure)
                result = MODULE.delivery_evidence(summary, 7, api=api)
                self.assertIsNone(result["testedCheckout"])
                self.assertEqual(result["parity"], "unavailable")
                self.assertEqual(result["deliveredTree"], "c" * 40)
                self.assertEqual(result["exceptions"], ["tested-checkout-unavailable"])
                self.assertEqual(summary.outcome, "delivered")

    def test_failed_required_check_rows_are_retained_and_validated(self):
        for malformed in (False, True):
            summary, api, _, _ = self.fixture()
            original = api._watch_read.side_effect
            row = {name: "" for name in MODULE.WATCH_FIELDS.split(",")}
            row.update(name="required-suite", state="FAILURE", bucket="fail",
                link="https://github.com/FS-GG/.github/actions/runs/7/job/11")
            if malformed:
                row["bucket"] = "unknown"
            def read(command, accepted):
                if command[1:3] == ["pr", "checks"]:
                    self.assertIn(1, accepted)
                    return 1, [row]
                return original(command, accepted)
            api._watch_read.side_effect = read
            result = MODULE.delivery_evidence(summary, 7, api=api)
            self.assertEqual(result["requiredChecks"], None if malformed else [row])
            self.assertEqual(result["parity"], "identical-tree")

    def test_optional_failure_cannot_suppress_final_delivery_json_or_exit_code(self):
        original, _, _, _ = self.fixture()
        for code, outcome, delivery in [(0, "delivered", "delivered"),
                (4, "delivered-disputed", "delivered"), (3, "indeterminate", "unknown")]:
            for failure in (zlib.error("corrupt"), EOFError("truncated"), OSError("library missing")):
                with self.subTest(outcome=outcome, failure=type(failure).__name__):
                    summary = MODULE.replace(original, outcome=outcome, codeDelivery=delivery,
                        coherentValidation="disputed" if code == 4 else original.coherentValidation,
                        mergeCommit=None if code == 3 else MERGE, attempts=1)
                    output = io.StringIO()
                    with mock.patch.object(MODULE, "summarize", return_value=(code, summary)), \
                         mock.patch.object(MODULE, "delivery_evidence", side_effect=failure), \
                         contextlib.redirect_stdout(output):
                        actual = MODULE.main(["--repo", summary.repo, "--pr", "1", "--head", HEAD,
                            "--evidence-run", "7", "--telemetry-unavailable", "local-clr-resource-hold"])
                    result = json.loads(output.getvalue())
                    self.assertEqual(actual, code)
                    self.assertEqual({k: v for k, v in result.items() if k != "deliveryEvidence"},
                        asdict(MODULE.replace(summary, telemetryHealth="unavailable")))
                    self.assertEqual(result["deliveryEvidence"]["exceptions"], ["collection-unavailable"])

    def test_installation_pilot_combination_refuses_before_effect_or_acquisition(self):
        with mock.patch.object(MODULE, "summarize") as effect, \
             mock.patch.object(MODULE, "delivery_evidence") as evidence, \
             contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as error:
            MODULE.main(["--repo", "FS-GG/.github", "--pr", "1", "--head", HEAD,
                "--evidence-run", "7", "--install-routine-producer", "--apply"])
        self.assertEqual(error.exception.code, 2)
        effect.assert_not_called()
        evidence.assert_not_called()

    def test_prior_result_accepts_legacy_and_versioned_extension_only(self):
        summary, _, _, _ = self.fixture()
        legacy = asdict(summary)
        extended = dict(legacy, deliveryEvidence={"schema": "fsgg.routine-delivery-evidence/1"})
        for value in (legacy, extended):
            self.assertTrue(MODULE.installation_result_shape(value))
            with tempfile.TemporaryDirectory() as directory:
                path = pathlib.Path(directory) / "result.json"
                path.write_text(json.dumps(value))
                self.assertEqual(MODULE.installation_prior_result(str(path)), value)
        self.assertFalse(MODULE.installation_result_shape(dict(legacy, unknown=True)))
        self.assertFalse(MODULE.installation_result_shape(dict(legacy, deliveryEvidence={"schema": "other"})))


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

    def test_native_routine_producer_on_second_page_is_authenticated(self):
        repo = 'FS-GG/.github'
        workflow = {'id':352355034, 'path':'.github/workflows/routine-eligibility.yml'}
        check = {'id':11, 'name':'routine-eligibility', 'app':{'id':15368}, 'head_sha':HEAD,
            'details_url':f'https://github.com/{repo}/actions/runs/7/job/11',
            'check_suite':{'id':13}, 'status':'completed', 'conclusion':'success',
            'started_at':'2026-10-13T00:00:00Z'}
        native_run = {'id':7, 'workflow_id':352355034, 'path':workflow['path'], 'event':'pull_request_target',
            'head_sha':HEAD, 'head_branch':'routine/fixture', 'status':'completed', 'conclusion':'success',
            'check_suite_id':13, 'repository':{'full_name':repo}, 'run_attempt':1}
        job = {'id':11, 'run_id':7, 'head_sha':HEAD, 'name':'routine-eligibility',
            'check_run_url':f'https://api.github.com/repos/{repo}/check-runs/11',
            'run_attempt':1, 'status':'completed', 'conclusion':'success'}
        filler = [{'id':1000+i, 'name':'source-fixture'} for i in range(100)]
        endpoint = f'repos/{repo}/commits/{HEAD}/check-runs?per_page=100'
        for corrupted in (False, True):
            producer = copy.deepcopy(check)
            if corrupted: producer['app']['id'] = 1
            api = MODULE.GhApi()
            with mock.patch.object(api, '_run', side_effect=[
                    {'total_count':101, 'check_runs':filler}, {'total_count':101, 'check_runs':[producer]},
                    workflow, native_run, job]) as reads:
                if corrupted:
                    with self.assertRaisesRegex(RuntimeError, 'native producer'):
                        api.routine_eligibility(repo, HEAD, 'routine/fixture')
                else:
                    api.routine_eligibility(repo, HEAD, 'routine/fixture')
                self.assertEqual(reads.call_args_list[:2], [mock.call([endpoint]), mock.call([endpoint+'&page=2'])])
        for conclusion in ('failure', 'cancelled'):
            latest = {**check, 'id':12, 'conclusion':conclusion,
                'started_at':'2026-10-13T00:01:00Z',
                'details_url':f'https://github.com/{repo}/actions/runs/7/job/12'}
            latest_job = {**job, 'id':12, 'conclusion':conclusion,
                'check_run_url':f'https://api.github.com/repos/{repo}/check-runs/12'}
            api = MODULE.GhApi()
            with mock.patch.object(api, '_run', side_effect=[
                    {'total_count':101, 'check_runs':[check, *filler[:99]]},
                    {'total_count':101, 'check_runs':[latest]},
                    workflow, native_run, job, native_run, latest_job]):
                with self.assertRaisesRegex(RuntimeError, 'latest routine eligibility'):
                    api.routine_eligibility(repo, HEAD, 'routine/fixture')

    def test_native_routine_pagination_refuses_incomplete_or_changed_population(self):
        first = {'total_count':101, 'check_runs':[{'id':1000+i, 'name':'source-fixture'} for i in range(100)]}
        variants = [
            {'total_count':101, 'check_runs':[]},
            {'total_count':102, 'check_runs':[{'id':12}]},
            {'total_count':True, 'check_runs':[{'id':12}]},
            {'total_count':101, 'check_runs':[{'id':1000}]},
            {'total_count':101, 'check_runs':[{'id':12}, {'id':13}]},
            {'total_count':101, 'check_runs':[None]},
            {'total_count':101, 'check_runs':[{'id':True}]},
        ]
        for second in variants:
            with self.subTest(second=second):
                api = MODULE.GhApi()
                with mock.patch.object(api, '_run', side_effect=[first, second]) as reads:
                    with self.assertRaisesRegex(RuntimeError, 'population is incomplete'):
                        api.routine_eligibility('FS-GG/.github', HEAD, 'routine/fixture')
                    self.assertEqual(reads.call_count, 2)
        api = MODULE.GhApi()
        with mock.patch.object(api, '_run', return_value={**first, 'total_count':1001}) as reads:
            with self.assertRaisesRegex(RuntimeError, 'population is incomplete'):
                api.routine_eligibility('FS-GG/.github', HEAD, 'routine/fixture')
            self.assertEqual(reads.call_count, 1)

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


class TelemetryUnavailableTests(unittest.TestCase):
    base = ["--repo", "FS-GG/.github", "--pr", "7", "--head", HEAD]
    flag = ["--telemetry-unavailable", "local-clr-resource-hold"]

    def invoke(self, api, extra=(), environment=None):
        with mock.patch.object(MODULE, "GhApi", return_value=api), \
             mock.patch.object(MODULE, "discover_telemetry_config", side_effect=AssertionError("discovery")), \
             mock.patch.object(MODULE, "create_ci_assignment", side_effect=AssertionError("assignment")), \
             mock.patch.object(MODULE, "observe_candidate", side_effect=AssertionError("observer")), \
             mock.patch.object(MODULE, "_compiled_json", side_effect=AssertionError("compiled")), \
             mock.patch.dict(os.environ, environment or {}, clear=True), \
             mock.patch("sys.stdout", new_callable=io.StringIO) as output, \
             mock.patch("sys.stderr", new_callable=io.StringIO) as error:
            code = MODULE.main(self.base + self.flag + list(extra))
        self.assertEqual(len(error.getvalue().splitlines()), 1)
        self.assertLess(len(error.getvalue().encode()), 4096)
        result, gap = json.loads(output.getvalue()), json.loads(error.getvalue())
        self.assertEqual(result["telemetryHealth"], "unavailable")
        self.assertEqual(gap, dict(
            schema="fsgg.routine-telemetry-unavailable/1", reason="local-clr-resource-hold",
            repo="FS-GG/.github", pr=7, expectedHead=HEAD,
            outcome=result["outcome"], codeDelivery=result["codeDelivery"],
            **({"identity": gap["identity"]} if "identity" in gap else {})))
        return code, result, gap

    def test_success_retains_exact_merge_and_native_readback(self):
        api = FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}])
        code, result, gap = self.invoke(api, ["--apply", "--telemetry-feature", "F",
            "--telemetry-item", "I", "--telemetry-attempt", "A", "--telemetry-parent-attempt", "P"])
        self.assertEqual((code, result["codeDelivery"], api.attempts, api.read_attempts), (0, "delivered", 1, 2))
        self.assertEqual(api.last_request.expectedHead, HEAD)
        self.assertEqual(gap["identity"], dict(feature="F", item="I", attempt="A", parentAttempt="P"))

    def test_refusal_pending_readback_and_publication_semantics_survive(self):
        cases = [
            (FakeApi([opened("c" * 40)]), ["--apply"], 2, "refused", 0),
            (FakeApi([opened()], runs=[run()]), ["--apply", "--coherent-workflow", "CI.yml"], 2, "refused", 0),
            (FakeApi([opened(), opened()], [MODULE.AmbiguousWrite("lost")]), ["--apply"], 3, "indeterminate", 1),
            (FakeApi([opened(), merged()], [{"merged": True, "sha": MERGE}]),
             ["--apply", "--publication", "required"], 0, "delivered", 1),
            (FakeApi([opened()]), [], 0, "ready", 0),
        ]
        for api, extra, expected_code, outcome, attempts in cases:
            with self.subTest(outcome=outcome):
                code, result, gap = self.invoke(api, extra, {"FSGG_TELEMETRY_CONFIG": ""})
                self.assertEqual((code, result["outcome"], api.attempts), (expected_code, outcome, attempts))
                self.assertNotIn("identity", gap)
                if "--publication" in extra:
                    self.assertEqual(result["publication"], "pending")
                if "--coherent-workflow" in extra:
                    self.assertEqual(result["coherentValidation"], "pending")

    def test_delivered_reuse_pending_is_not_disputed(self):
        api = FakeApi([merged()], runs=[run()], selections={7: selection("reused")})
        code, result, _gap = self.invoke(api, ["--apply", "--coherent-workflow", "CI.yml"])
        self.assertEqual((code, result["codeDelivery"], result["coherentValidation"], api.attempts),
                         (0, "delivered", "pending", 0))

    def test_conflicts_and_invalid_audit_refuse_before_any_native_call(self):
        cases = [([option, "value"], {}) for option in (
            "--telemetry-assignment", "--telemetry-store-root", "--telemetry-config",
            "--telemetry-engine", "--telemetry-publisher-config")]
        cases += [(["--watch-checks", "--watch-seconds", "1"], {}),
                  (["--telemetry-publisher-event"], {}),
                  (["--telemetry-advisory-deadline-monotonic", "80"], {}),
                  ([], {"FSGG_TELEMETRY_CONFIG": "/private/config"}),
                  (["--telemetry-feature", "F"], {}),
                  (["--telemetry-parent-attempt", "P"], {}),
                  (["--telemetry-feature", "F", "--telemetry-item", "I", "--telemetry-attempt", ""], {}),
                  (["--telemetry-feature", "F" * 2048, "--telemetry-item", "I", "--telemetry-attempt", "A"], {})]
        for extra, environment in cases:
            with self.subTest(extra=extra), \
                 mock.patch.object(MODULE, "GhApi", side_effect=AssertionError("native")), \
                 mock.patch.object(MODULE, "discover_telemetry_config", side_effect=AssertionError("compiled")), \
                 mock.patch.dict(os.environ, environment, clear=True), \
                 contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as refused:
                MODULE.main(self.base + self.flag + extra)
            self.assertEqual(refused.exception.code, 2)

    def test_reason_enum_is_closed_and_default_still_discovers(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            MODULE.main(self.base + ["--telemetry-unavailable", "overload"])
        with mock.patch.object(MODULE, "GhApi", return_value=FakeApi([opened()])), \
             mock.patch.object(MODULE, "discover_telemetry_config", return_value=None) as discover, \
             mock.patch.dict(os.environ, {}, clear=True), contextlib.redirect_stdout(io.StringIO()) as output, \
             contextlib.redirect_stderr(io.StringIO()) as error:
            self.assertEqual(MODULE.main(self.base), 0)
        discover.assert_called_once()
        self.assertEqual(json.loads(output.getvalue())["telemetryHealth"], "not-configured")
        self.assertEqual(error.getvalue(), "")


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
              env=None, spawn_calls=None, stderr_output=None, max_bytes=None):
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
                if stderr_output is not None: options["stderr_output"]=stderr_output
                if max_bytes is not None: options["max_bytes"]=max_bytes
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
                              link="https://github.com/FS-GG/FS.GG.Coordination/actions/runs/7/job/8")]
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
        assert args[:3] == ["--method", "PUT", "repos/FS-GG/FS.GG.Coordination/pulls/1/merge"]
        assert json.loads(kwargs["input_text"]) == {"sha": self.fixture["head"], "merge_method": "squash"}
        value = next(self.writes)
        if isinstance(value, Exception): raise value
        return value

    def coherent_runs(self, *_): return self.runs
    def coherent_jobs(self, *_): return []
    def qualification_selection(self, *_): return None

    def board_pr(self, *_):
        # The advisory board gate reads through its own methods; `_run` above is the merge transport only.
        raise RuntimeError("fixture: board-currency reads are outside installation fixtures")


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
        bundle = cls.owner / MODULE.ASSERTION_BUNDLE
        bundle.parent.mkdir(parents=True, exist_ok=True)
        bundle.write_bytes(b"#!/bin/bash\n# retained assertion alarms\nexit 0\n")
        cls.assertion_old_pin = commit(cls.owner)
        for path in (*MODULE.PRODUCER_PATHS, "tools/routine-delivery.py", "src/FS.GG.Drivers/workspace-inventory.json"):
            target = cls.owner / path; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((ROOT / path).read_bytes()); target.chmod((ROOT / path).stat().st_mode & 0o777)
        source = commit(cls.owner)
        policy = cls.owner / MODULE.PRODUCER_PATHS[0]; original = policy.read_bytes()
        policy.write_bytes(original + b" "); obsolete = commit(cls.owner)
        policy.write_bytes(original); (cls.owner / "README.md").write_text("unrelated owner change")
        owner_head = commit(cls.owner)
        (cls.receiver / "README.md").write_text("base")
        registration=cls.receiver/MODULE.PRODUCER_REGISTRATION
        registration.parent.mkdir(parents=True,exist_ok=True)
        # Native PR971 protected base 45f71c3785d6e2fbf84ab8fc68eee0855be10411; exact retained extension bytes.
        cls.registration_base=MODULE.base64.b64decode('ewogICJzY2hlbWEiOiAiZnNnZy5jb29yZGluYXRpb24uZXhlY3V0aW9uLXBpbnMtZXh0ZW5zaW9uLzEiLAogICJjb21wbGV0ZSI6IHRydWUsCiAgImRvY3VtZW50cyI6IFsKICAgIHsKICAgICAgImtpbmQiOiAiYWN0aW9uIiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi9hY3Rpb25zL2Nvb3JkaW5hdGlvbi1zZXR1cC9hY3Rpb24ueW1sIiwKICAgICAgInNoYTI1NiI6ICI5YTI3OWI5MmE5ZjdmMGYwNTkwYzZlZWNlNGZhOWRhN2UwZmRkZWNhZGMwMmE0OTE2NWE2YWU5ODMwMzcxYjJiIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9zZXR1cC1kb3RuZXRAYTk4YjU2ODUyYzM1YjhlMzE5MGFjMjhjOGMyMjcxZGE1OTEwNmM2OCIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL29wdGltaXN0aWMtcGFyYWxsZWwtdmFsaWRhdGlvbi55bWwiLAogICAgICAic2hhMjU2IjogIjU2ZTI5ZjE1MjU4MGE5ZjRiMjJiNjQ3YTVmZWM4NWIxYWJiYTk1NTJkYjdkNzM0YmFiYThjZDA3OTMyZGYxZTkiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QDNlNWY0NWIyY2ZiOTE3MjA1NGI0MDg3YTQwZThlMGI1YTU0NjFlN2MiLAogICAgICAgICJhY3Rpb25zL3NldHVwLWRvdG5ldEBhOThiNTY4NTJjMzViOGUzMTkwYWMyOGM4YzIyNzFkYTU5MTA2YzY4IiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3JldXNhYmxlLW9ibGlnYXRpb24tc2VsZWN0aW9uLnltbCIsCiAgICAgICJzaGEyNTYiOiAiMDIyZGI5YjU5NzE5YzRkNTMzYTZhMDZjNGMyNDBlNjA0MWE1ZmMxMGRhNDQzOWMyYjgwZTFkZThiZWMyNTg5MSIsCiAgICAgICJleHRlcm5hbFJlZmVyZW5jZXMiOiBbCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiLi8uZ2l0aHViL2FjdGlvbnMvY29vcmRpbmF0aW9uLXNldHVwIgogICAgICBdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3dvcmtmbG93LXNlbGVjdGlvbi1zZW50aW5lbC55bWwiLAogICAgICAic2hhMjU2IjogImQxNWUwMzAwOTQyN2I3ZGFhNGRkYmZmNDI4MmUzODkzN2YwMzg0NGNkNWZkNjgwZDg0OGExZTM1YTk3MjExY2YiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL3VwbG9hZC1hcnRpZmFjdEAwNDNmYjQ2ZDFhOTNjNzdhYWU2NTZlN2MxYzY0YTg3NWQxZmM2YTBhIgogICAgICBdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogWwogICAgICAgICIuLy5naXRodWIvYWN0aW9ucy9jb29yZGluYXRpb24tc2V0dXAiCiAgICAgIF0KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3Mvb3JjaGVzdHJhdGlvbi1ob3N0LWNhbmRpZGF0ZS55bWwiLAogICAgICAic2hhMjU2IjogIjgxZDIzYjlkNjY0MGJiMWYyNDdiNmY2MDk2YTY4NjkyY2E0YzEyNmUyYTIwOTg3ZDJhZjVhYzYzNTI3OWY1NGQiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL3NldHVwLWRvdG5ldEBhOThiNTY4NTJjMzViOGUzMTkwYWMyOGM4YzIyNzFkYTU5MTA2YzY4IiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIsCiAgICAgICAgImFjdGlvbnMvZG93bmxvYWQtYXJ0aWZhY3RAM2U1ZjQ1YjJjZmI5MTcyMDU0YjQwODdhNDBlOGUwYjVhNTQ2MWU3YyIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL29yY2hlc3RyYXRpb24tcnVubmVyLWNsaWVudC1jYW5kaWRhdGUueW1sIiwKICAgICAgInNoYTI1NiI6ICI2MDAwMGQ2ZDY1ZDE4ZDYwYzUzZDRjNTk3MDA0ZGJlZGQzNTE2MmUwODAwNzFmMGRhMzAwZDUwYzNjYjgwNTMyIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEAzZDNjNDJlNWFhYzViYTgwNTgyNWRhNzY0MTBjMTgxMjczYmE5MGIxIiwKICAgICAgICAiYWN0aW9ucy9zZXR1cC1kb3RuZXRAYTk4YjU2ODUyYzM1YjhlMzE5MGFjMjhjOGMyMjcxZGE1OTEwNmM2OCIsCiAgICAgICAgImFjdGlvbnMvdXBsb2FkLWFydGlmYWN0QDA0M2ZiNDZkMWE5M2M3N2FhZTY1NmU3YzFjNjRhODc1ZDFmYzZhMGEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QDNlNWY0NWIyY2ZiOTE3MjA1NGI0MDg3YTQwZThlMGI1YTU0NjFlN2MiCiAgICAgIF0sCiAgICAgICJsb2NhbFJlZmVyZW5jZXMiOiBbXQogICAgfSwKICAgIHsKICAgICAgImtpbmQiOiAid29ya2Zsb3ciLAogICAgICAicGF0aCI6ICIuZ2l0aHViL3dvcmtmbG93cy9vcmNoZXN0cmF0aW9uLWNvbnRhaW5lci1idW5kbGUueW1sIiwKICAgICAgInNoYTI1NiI6ICIzMTdiY2U5MTQwZGZlZWFhOGI1M2JlOTFkYzdiYjg3MTAwMDIzN2JiZDZkMzg5NDYwM2RiZTA3MjhhZTFkZWQ1IiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEAzZDNjNDJlNWFhYzViYTgwNTgyNWRhNzY0MTBjMTgxMjczYmE5MGIxIiwKICAgICAgICAiYWN0aW9ucy9zZXR1cC1kb3RuZXRAYTk4YjU2ODUyYzM1YjhlMzE5MGFjMjhjOGMyMjcxZGE1OTEwNmM2OCIsCiAgICAgICAgImFjdGlvbnMvdXBsb2FkLWFydGlmYWN0QDA0M2ZiNDZkMWE5M2M3N2FhZTY1NmU3YzFjNjRhODc1ZDFmYzZhMGEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QDNlNWY0NWIyY2ZiOTE3MjA1NGI0MDg3YTQwZThlMGI1YTU0NjFlN2MiCiAgICAgIF0sCiAgICAgICJsb2NhbFJlZmVyZW5jZXMiOiBbXQogICAgfSwKICAgIHsKICAgICAgImtpbmQiOiAid29ya2Zsb3ciLAogICAgICAicGF0aCI6ICIuZ2l0aHViL3dvcmtmbG93cy9jYWxsYWJsZS1jbGktcmVsZWFzZS1wcmVwYXJlLnltbCIsCiAgICAgICJzaGEyNTYiOiAiZGVlMjBmNzExMmQzZTdiMGM4NGY4YjIxYmQ2YmZjMWIxNGQ0YjkzNzJmZmE1NWYyNWQ2MzhhZDZlMGE5NDMxMiIsCiAgICAgICJleHRlcm5hbFJlZmVyZW5jZXMiOiBbCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIsCiAgICAgICAgImFjdGlvbnMvc2V0dXAtZG90bmV0QGE5OGI1Njg1MmMzNWI4ZTMxOTBhYzI4YzhjMjI3MWRhNTkxMDZjNjgiLAogICAgICAgICJhY3Rpb25zL3VwbG9hZC1hcnRpZmFjdEAwNDNmYjQ2ZDFhOTNjNzdhYWU2NTZlN2MxYzY0YTg3NWQxZmM2YTBhIgogICAgICBdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogW10KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3MvY2FsbGFibGUtY2xpLXJlbGVhc2UtcHVibGlzaC55bWwiLAogICAgICAic2hhMjU2IjogIjY3OTE1NDI5NzZhYzFhNTg3ZTAwNjAxMDlmY2YyYzQzMzQxMTE0ODc0NDZkZDQ1Y2ZhZTIyM2JjYzYzNWY1YmUiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJOdUdldC9sb2dpbkA4ZDE5Njc1NGI0MDM2MTUwNTM3ZjgwYWM1MzllMTVjMmYxMDI4ODQxIiwKICAgICAgICAiYWN0aW9ucy9hdHRlc3RAMWU2OWY0OGFjYjgyZDE5NjZhMzk0ZGE5MTZiNGMxNjk4YWE1NjlkNiIsCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIsCiAgICAgICAgImFjdGlvbnMvZG93bmxvYWQtYXJ0aWZhY3RAM2U1ZjQ1YjJjZmI5MTcyMDU0YjQwODdhNDBlOGUwYjVhNTQ2MWU3YyIsCiAgICAgICAgImFjdGlvbnMvc2V0dXAtZG90bmV0QGE5OGI1Njg1MmMzNWI4ZTMxOTBhYzI4YzhjMjI3MWRhNTkxMDZjNjgiLAogICAgICAgICJhY3Rpb25zL3VwbG9hZC1hcnRpZmFjdEAwNDNmYjQ2ZDFhOTNjNzdhYWU2NTZlN2MxYzY0YTg3NWQxZmM2YTBhIgogICAgICBdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogW10KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3MvY2FsbGFibGUtY2xpLXJlbGVhc2UtcHJlcGFyZS0wMTEueW1sIiwKICAgICAgInNoYTI1NiI6ICIxNDY5OWZiMGRhNzU2NGJhMDkzYmI0ZmQ0YmRmOGVkZDk3ZGFmMWQ0MDQ3ZmRiNjA0MzQ5NGIxYmZhNzdjNGRlIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEAzZDNjNDJlNWFhYzViYTgwNTgyNWRhNzY0MTBjMTgxMjczYmE5MGIxIiwKICAgICAgICAiYWN0aW9ucy9zZXR1cC1kb3RuZXRAYTk4YjU2ODUyYzM1YjhlMzE5MGFjMjhjOGMyMjcxZGE1OTEwNmM2OCIsCiAgICAgICAgImFjdGlvbnMvdXBsb2FkLWFydGlmYWN0QDA0M2ZiNDZkMWE5M2M3N2FhZTY1NmU3YzFjNjRhODc1ZDFmYzZhMGEiCiAgICAgIF0sCiAgICAgICJsb2NhbFJlZmVyZW5jZXMiOiBbXQogICAgfSwKICAgIHsKICAgICAgImtpbmQiOiAid29ya2Zsb3ciLAogICAgICAicGF0aCI6ICIuZ2l0aHViL3dvcmtmbG93cy9jYWxsYWJsZS1jbGktcmVsZWFzZS1wdWJsaXNoLTAxMS55bWwiLAogICAgICAic2hhMjU2IjogIjkzMGVjYTk0M2VlNThiZjk0MTJiYjlkZjM0ODhhMmM5OGQzZTJiMzNkYjIyMThkMTNjM2NiM2Y3ZjU5NDcwODciLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJOdUdldC9sb2dpbkA4ZDE5Njc1NGI0MDM2MTUwNTM3ZjgwYWM1MzllMTVjMmYxMDI4ODQxIiwKICAgICAgICAiYWN0aW9ucy9hdHRlc3RAMWU2OWY0OGFjYjgyZDE5NjZhMzk0ZGE5MTZiNGMxNjk4YWE1NjlkNiIsCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIsCiAgICAgICAgImFjdGlvbnMvc2V0dXAtZG90bmV0QGE5OGI1Njg1MmMzNWI4ZTMxOTBhYzI4YzhjMjI3MWRhNTkxMDZjNjgiLAogICAgICAgICJhY3Rpb25zL3VwbG9hZC1hcnRpZmFjdEAwNDNmYjQ2ZDFhOTNjNzdhYWU2NTZlN2MxYzY0YTg3NWQxZmM2YTBhIgogICAgICBdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogW10KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3MvY2FsbGFibGUtaXNvbGF0ZWQtdjItZWZmZWN0LXJlbGVhc2UueW1sIiwKICAgICAgInNoYTI1NiI6ICJjYjAxODNmZTE5YjZiM2EwNmUyN2Y1MTEyZTRlNjViYmQ4ZDdjZWVhYzFjMGQ2NjkzYmNmMjYwY2IwYmUxODIwIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEAzZDNjNDJlNWFhYzViYTgwNTgyNWRhNzY0MTBjMTgxMjczYmE5MGIxIiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL2NhbGxhYmxlLWlzb2xhdGVkLXYyLWV4ZWN1dGUueW1sIiwKICAgICAgInNoYTI1NiI6ICI3NDA2ZWZlYWJiNWFjYTA1MDRlOGFlMzlhNTE0MDM4ZDc0NzNiNmZkOTdhNjZkMmU0ZWZjYzViMmM5NDc5OWRiIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFtdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogW10KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3MvY2FsbGFibGUtaXNvbGF0ZWQtdjItcnVudGltZS1jYW5kaWRhdGUueW1sIiwKICAgICAgInNoYTI1NiI6ICJhYTg0ODY0OGNmZjBiNzhjMTIwZGIzMDUwYjRiNTZmZjE2N2I3MDNjMDAzY2EyZDNhYTY0ZTdkNjRiMGU5NzY1IiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEAzZDNjNDJlNWFhYzViYTgwNTgyNWRhNzY0MTBjMTgxMjczYmE5MGIxIiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3YyLWhvc3QtZml4ZWQtcXVhbGlmaWNhdGlvbi55bWwiLAogICAgICAic2hhMjU2IjogIjcyMTgxODQwNTg4OWY1MjA1ZmIyYWJmNWEyMzZlOTU5MmU1MmViNjkyNzg3M2VhMGFiOTRiZTczMDIzYzk2MmEiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QDNlNWY0NWIyY2ZiOTE3MjA1NGI0MDg3YTQwZThlMGI1YTU0NjFlN2MiLAogICAgICAgICJhY3Rpb25zL3VwbG9hZC1hcnRpZmFjdEAwNDNmYjQ2ZDFhOTNjNzdhYWU2NTZlN2MxYzY0YTg3NWQxZmM2YTBhIgogICAgICBdLAogICAgICAibG9jYWxSZWZlcmVuY2VzIjogW10KICAgIH0sCiAgICB7CiAgICAgICJraW5kIjogIndvcmtmbG93IiwKICAgICAgInBhdGgiOiAiLmdpdGh1Yi93b3JrZmxvd3MvdjItY2ktb3JkaW5hcnktc2V0dGxlbWVudC55bWwiLAogICAgICAic2hhMjU2IjogIjJjMTJhZGE2ODg5NzRlOTA5MjE3M2ViNDJjMTBlMGMyMjQ3MGRlM2EwMzQ4Y2E4YTMzODkyMGU4NDdlN2Q4NGUiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QGQzZjg2YTEwNmEwYmFjNDViOTc0YTYyODg5NmM5MGRiZGY1YzgwOTMiLAogICAgICAgICJhY3Rpb25zL3NldHVwLWRvdG5ldEBhOThiNTY4NTJjMzViOGUzMTkwYWMyOGM4YzIyNzFkYTU5MTA2YzY4IiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL2Nvb3JkaW5hdGlvbi1ib2FyZC12Mi1pbXBvcnQueW1sIiwKICAgICAgInNoYTI1NiI6ICI5MmVmNTY2NWZkY2U4NGJhODllZTc3M2YxYzljNDE4MWYwNWY2OTAzZDc3Y2U5YzJmODBjODhiNWQ2NmZlZWYyIiwKICAgICAgImV4dGVybmFsUmVmZXJlbmNlcyI6IFsKICAgICAgICAiYWN0aW9ucy9jaGVja291dEBkMjM0NDFhNDhlNTE2YjZjMzRhZWE0ZmE0MTU1MWEzMGUzMGFmODAzIiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAYjdjNTY2YTc3MmU2YjZiZmI1OGVkMGRjMjUwNTMyYTQ3OWQ3Nzg5ZiIsCiAgICAgICAgImFjdGlvbnMvZG93bmxvYWQtYXJ0aWZhY3RANjM0ZjkzY2IyOTE2ZTNmZGZmNjc4ODU1MWI5OWIwNjJkMDMzNWNlMCIsCiAgICAgICAgImFjdGlvbnMvY3JlYXRlLWdpdGh1Yi1hcHAtdG9rZW5AYmNkMmJhNDkyMTg5MDY3MDRhYjZjMWFhNzk2OTk2ZGE0MDlkM2ViMSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3BvcnRhYmxlLXdvcmtzcGFjZS1pbWFnZS1xdWFsaWZpY2F0aW9uLnltbCIsCiAgICAgICJzaGEyNTYiOiAiNjRmNTE4MmEwMDM2MTJmYTQxNTkxZDZiNjRiNDUwZmVlYjBhNDc4ZTE3YWQ0NzdiYmQ4ZGJlNzNhNGM1NzMxYiIsCiAgICAgICJleHRlcm5hbFJlZmVyZW5jZXMiOiBbCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIsCiAgICAgICAgImFjdGlvbnMvdXBsb2FkLWFydGlmYWN0QDA0M2ZiNDZkMWE5M2M3N2FhZTY1NmU3YzFjNjRhODc1ZDFmYzZhMGEiCiAgICAgIF0sCiAgICAgICJsb2NhbFJlZmVyZW5jZXMiOiBbXQogICAgfSwKICAgIHsKICAgICAgImtpbmQiOiAid29ya2Zsb3ciLAogICAgICAicGF0aCI6ICIuZ2l0aHViL3dvcmtmbG93cy9wb3J0YWJsZS13b3Jrc3BhY2UtZXhlY3V0b3ItcXVhbGlmaWNhdGlvbi55bWwiLAogICAgICAic2hhMjU2IjogIjU4MjdmNjYyYzMzMTY1NmFiNWExNDIzOTQ4NzkwNzA5ZjdhYWQ1ZDE3YTQ0OWZiNjljNGMyYzJhNWM3MGQyZDciLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL3NldHVwLWRvdG5ldEBhOThiNTY4NTJjMzViOGUzMTkwYWMyOGM4YzIyNzFkYTU5MTA2YzY4IiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3BvcnRhYmxlLXdvcmtzcGFjZS1weXRob24tcHJvdmlkZXItcXVhbGlmaWNhdGlvbi55bWwiLAogICAgICAic2hhMjU2IjogIjA0ZGY5MjFhYzc0ZmJlZTRjZWFkOTk1ZThjYWMxNzkxMWQxOWNjNzAxNDE2M2ViOWM4YjNkZDVkYmVjYjU1OGUiLAogICAgICAiZXh0ZXJuYWxSZWZlcmVuY2VzIjogWwogICAgICAgICJhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEiLAogICAgICAgICJhY3Rpb25zL2Rvd25sb2FkLWFydGlmYWN0QDNlNWY0NWIyY2ZiOTE3MjA1NGI0MDg3YTQwZThlMGI1YTU0NjFlN2MiLAogICAgICAgICJhY3Rpb25zL3NldHVwLWRvdG5ldEBhOThiNTY4NTJjMzViOGUzMTkwYWMyOGM4YzIyNzFkYTU5MTA2YzY4IiwKICAgICAgICAiYWN0aW9ucy91cGxvYWQtYXJ0aWZhY3RAMDQzZmI0NmQxYTkzYzc3YWFlNjU2ZTdjMWM2NGE4NzVkMWZjNmEwYSIKICAgICAgXSwKICAgICAgImxvY2FsUmVmZXJlbmNlcyI6IFtdCiAgICB9LAogICAgewogICAgICAia2luZCI6ICJ3b3JrZmxvdyIsCiAgICAgICJwYXRoIjogIi5naXRodWIvd29ya2Zsb3dzL3RlbGVtZXRyeS1ob3N0LW1hbmFnZXItYnVuZGxlLnltbCIsCiAgICAgICJzaGEyNTYiOiAiNzdmMDBiOTAxNmNlMmU5MWZjYWZhZTE5NmFmMWYwZjlmNzNkZmZkZTMxM2MyZGI5M2ZjMTdmYTM2ZThiZmU0ZSIsCiAgICAgICJleHRlcm5hbFJlZmVyZW5jZXMiOiBbCiAgICAgICAgImFjdGlvbnMvY2hlY2tvdXRAM2QzYzQyZTVhYWM1YmE4MDU4MjVkYTc2NDEwYzE4MTI3M2JhOTBiMSIsCiAgICAgICAgImFjdGlvbnMvZG93bmxvYWQtYXJ0aWZhY3RAM2U1ZjQ1YjJjZmI5MTcyMDU0YjQwODdhNDBlOGUwYjVhNTQ2MWU3YyIsCiAgICAgICAgImFjdGlvbnMvdXBsb2FkLWFydGlmYWN0QDA0M2ZiNDZkMWE5M2M3N2FhZTY1NmU3YzFjNjRhODc1ZDFmYzZhMGEiCiAgICAgIF0sCiAgICAgICJsb2NhbFJlZmVyZW5jZXMiOiBbXQogICAgfQogIF0KfQo=')
        registration.write_bytes(cls.registration_base)
        ci = cls.receiver / ".github/workflows/ci.yml"
        ci.parent.mkdir(parents=True, exist_ok=True)
        ci.write_text("""name: ci
on:
  push:
    branches: [main]
  pull_request:
  workflow_dispatch:
  schedule:
    - cron: '7 3 * * *'
permissions:
  contents: read
jobs:
  qualify:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v6
        with:
          global-json-file: global.json
      - name: Provision pinned Quint
        run: bash eng/provision-quint.sh "$RUNNER_TEMP/quint-tools"
      - name: Check independent CI preflight example
        env:
          QUINT_BIN: ${{ runner.temp }}/quint-tools/quint
          QUINT_HOME: ${{ runner.temp }}/quint-tools/home
        run: bash examples/CiPreflight/check.sh
      - name: Qualify core, tooling and external package consumer
        env:
          QUINT_BIN: ${{ runner.temp }}/quint-tools/quint
          QUINT_HOME: ${{ runner.temp }}/quint-tools/home
        run: bash eng/check.sh
""")
        assertion_pin = cls.receiver / MODULE.TEMPLATES_ASSERTION_PIN
        assertion_pin.parent.mkdir(parents=True, exist_ok=True)
        cls.assertion_pin_base = b'# retained header\nSKILL_ASSERT_REF="' + cls.assertion_old_pin.encode() + b'"\n# retained 14d alarm\n'
        assertion_pin.write_bytes(cls.assertion_pin_base)
        # Authenticated BAR base workflow retained verbatim; synthetic Git objects are not native evidence.
        cls.bar_prearm_base = MODULE.base64.b64decode('bmFtZTogcHJlYXJtLXNvdXJjZS1jb250cm9scwoKb246CiAgcHVsbF9yZXF1ZXN0OgogICAgcGF0aHM6CiAgICAgIC0gLmdpdGh1Yi93b3JrZmxvd3MvcHJlYXJtLXNvdXJjZS1jb250cm9scy55bWwKICAgICAgLSBEaXJlY3RvcnkuQnVpbGQucHJvcHMKICAgICAgLSAuY29uZmlnL2RvdG5ldC10b29scy5qc29uCiAgICAgIC0gZW5nLyoqCiAgICAgIC0gc3JjL0Jyb2tlci5Db250cmFjdHMvKioKICAgICAgLSBzcmMvQnJva2VyLkNvcmUvKioKICAgICAgLSBzcmMvQnJva2VyLlByb3RvY29sLyoqCiAgICAgIC0gc3JjL0Jyb2tlci5Ccm93c2VyLkNvbnRyYWN0cy8qKgogICAgICAtIHNyYy9Ccm9rZXIuQnJvd3Nlci5MaXZlLyoqCiAgICAgIC0gc3JjL0Jyb2tlci5Ccm93c2VyLkdhdGV3YXkvKioKICAgICAgLSBzcmMvQnJva2VyLkJyb3dzZXIuQ2xpZW50L2xpdmUtcnVudGltZS5qcwogICAgICAtIHRlc3RzL0Jyb2tlci5Ccm93c2VyLkxpdmUuVGVzdHMvKioKICAgICAgLSB0ZXN0cy9Ccm9rZXIuTmF0aXZlUHJvb2YvdGFjdGljYWwtbmF0aXZlLWpvdXJuZXkuc3BlYy5qcwogICAgICAtIHRlc3RzL0Jyb2tlci5OYXRpdmVQcm9vZi90YWN0aWNhbC1uYXRpdmUtcmVhZGluZXNzLnRlc3QubWpzCiAgICAgIC0gdGVzdHMvQnJva2VyLk5hdGl2ZVByb29mL29uZS11bml0LXByZWFybS50ZXN0Lm1qcwogICAgICAtIHRlc3RzL0Jyb2tlci5OYXRpdmVQcm9vZi9Qb2xpY3kvYnVpbGQtc3RvY2stc2VsZWN0aW9uLm1qcwogICAgICAtIHRlc3RzL0Jyb2tlci5OYXRpdmVQcm9vZi9Qb2xpY3kvYnVpbGQtc3RvY2stc2VsZWN0aW9uLnRlc3QubWpzCiAgd29ya2Zsb3dfZGlzcGF0Y2g6CgpwZXJtaXNzaW9uczoKICBjb250ZW50czogcmVhZAoKY29uY3VycmVuY3k6CiAgZ3JvdXA6IHByZWFybS1zb3VyY2UtY29udHJvbHMtJHt7IGdpdGh1Yi5ldmVudC5wdWxsX3JlcXVlc3QubnVtYmVyIHx8IGdpdGh1Yi5yZWYgfX0KICBjYW5jZWwtaW4tcHJvZ3Jlc3M6IHRydWUKCmpvYnM6CiAgcHJlYXJtLXNvdXJjZS1jb250cm9sczoKICAgIHJ1bnMtb246IHVidW50dS0yNC4wNAogICAgdGltZW91dC1taW51dGVzOiAxMAogICAgZW52OgogICAgICBDSTogJ3RydWUnCiAgICAgIERPVE5FVF9QUk9DRVNTT1JfQ09VTlQ6ICcxJwogICAgc3RlcHM6CiAgICAgIC0gbmFtZTogQ2hlY2sgb3V0IHRoZSBleGFjdCBjYW5kaWRhdGUKICAgICAgICB1c2VzOiBhY3Rpb25zL2NoZWNrb3V0QDNkM2M0MmU1YWFjNWJhODA1ODI1ZGE3NjQxMGMxODEyNzNiYTkwYjEgIyB2NwogICAgICAgIHdpdGg6CiAgICAgICAgICByZWY6ICR7eyBnaXRodWIuZXZlbnQucHVsbF9yZXF1ZXN0LmhlYWQuc2hhIHx8IGdpdGh1Yi5zaGEgfX0KICAgICAgICAgIGZldGNoLWRlcHRoOiAyCiAgICAgICAgICBwZXJzaXN0LWNyZWRlbnRpYWxzOiBmYWxzZQoKICAgICAgLSBuYW1lOiBCaW5kIHRoZSBjaGVja2VkLW91dCBjYW5kaWRhdGUKICAgICAgICBzaGVsbDogYmFzaAogICAgICAgIGVudjoKICAgICAgICAgIEVYUEVDVEVEX1NIQTogJHt7IGdpdGh1Yi5ldmVudC5wdWxsX3JlcXVlc3QuaGVhZC5zaGEgfHwgZ2l0aHViLnNoYSB9fQogICAgICAgIHJ1bjogfAogICAgICAgICAgc2V0IC1ldW8gcGlwZWZhaWwKICAgICAgICAgIHRlc3QgIiQoZ2l0IHJldi1wYXJzZSBIRUFEKSIgPSAiJEVYUEVDVEVEX1NIQSIKICAgICAgICAgIHRlc3QgLXogIiQoZ2l0IHN0YXR1cyAtLXBvcmNlbGFpbiAtLXVudHJhY2tlZC1maWxlcz1hbGwpIgoKICAgICAgLSBuYW1lOiBTZXQgdXAgdGhlIGV4YWN0IC5ORVQgU0RLCiAgICAgICAgdXNlczogYWN0aW9ucy9zZXR1cC1kb3RuZXRAYTk4YjU2ODUyYzM1YjhlMzE5MGFjMjhjOGMyMjcxZGE1OTEwNmM2OCAjIHY2CiAgICAgICAgd2l0aDoKICAgICAgICAgIGRvdG5ldC12ZXJzaW9uOiAxMC4wLjQwMQoKICAgICAgLSBuYW1lOiBTZXQgdXAgdGhlIGV4YWN0IE5vZGUuanMgcnVudGltZQogICAgICAgIHVzZXM6IGFjdGlvbnMvc2V0dXAtbm9kZUAyNDk5NzA3MjljYjBlZjM1ODk2NDRlMjg5NjY0NWU1ZGM1YmE5YzM4ICMgdjYKICAgICAgICB3aXRoOgogICAgICAgICAgbm9kZS12ZXJzaW9uOiAyNC44LjAKCiAgICAgIC0gbmFtZTogUnVuIHRoZSBmb3VydGVlbiBKYXZhU2NyaXB0IHNvdXJjZSBjb250cm9scwogICAgICAgIHNoZWxsOiBiYXNoCiAgICAgICAgcnVuOiBub2RlIC0tdGVzdCB0ZXN0cy9Ccm9rZXIuTmF0aXZlUHJvb2YvdGFjdGljYWwtbmF0aXZlLXJlYWRpbmVzcy50ZXN0Lm1qcwoKICAgICAgLSBuYW1lOiBSdW4gdGhlIG9uZS11bml0IGFuZCBjb21waWxlci1jYXB0dXJlIHNvdXJjZSBjb250cm9scwogICAgICAgIHNoZWxsOiBiYXNoCiAgICAgICAgcnVuOiBub2RlIC0tdGVzdCB0ZXN0cy9Ccm9rZXIuTmF0aXZlUHJvb2Yvb25lLXVuaXQtcHJlYXJtLnRlc3QubWpzIHRlc3RzL0Jyb2tlci5OYXRpdmVQcm9vZi9Qb2xpY3kvYnVpbGQtc3RvY2stc2VsZWN0aW9uLnRlc3QubWpzCgogICAgICAtIG5hbWU6IFJlc3RvcmUgYW5kIGJ1aWxkIHRoZSBsaXZlIGJvdW5kYXJ5IHNvdXJjZQogICAgICAgIHNoZWxsOiBiYXNoCiAgICAgICAgZW52OgogICAgICAgICAgIyBncnBjLWZzaGFycCAwLjIgdGFyZ2V0cyBuZXQ2LjA7IHJldGFpbiB0aGUgTGF0ZXN0TWFqb3IgcG9saWN5IGluCiAgICAgICAgICAjIHNwZWNzLzAwMS10dWktZ3JwYy1icm9rZXIvcmVhZGluZXNzL2ZlYXR1cmUtYmFzZWxpbmUubWQuCiAgICAgICAgICBET1RORVRfUk9MTF9GT1JXQVJEOiBMYXRlc3RNYWpvcgogICAgICAgIHJ1bjogfAogICAgICAgICAgc2V0IC1ldW8gcGlwZWZhaWwKICAgICAgICAgIHByb2plY3Q9dGVzdHMvQnJva2VyLkJyb3dzZXIuTGl2ZS5UZXN0cy9Ccm9rZXIuQnJvd3Nlci5MaXZlLlRlc3RzLmZzcHJvagogICAgICAgICAgZG90bmV0IHJlc3RvcmUgIiRwcm9qZWN0IiAtLWRpc2FibGUtcGFyYWxsZWwKICAgICAgICAgIGRvdG5ldCBidWlsZCAiJHByb2plY3QiIC0tY29uZmlndXJhdGlvbiBSZWxlYXNlIC0tbm8tcmVzdG9yZSAtLW1heGNwdWNvdW50OjEKCiAgICAgIC0gbmFtZTogUnVuIGV4YWN0bHkgdGhyZWUgc3ludGhldGljIGNvbXBpbGVkIGNvbnRyb2xzCiAgICAgICAgc2hlbGw6IGJhc2gKICAgICAgICBydW46IHwKICAgICAgICAgIHNldCAtZXVvIHBpcGVmYWlsCiAgICAgICAgICByZXN1bHRzPSQobWt0ZW1wIC1kKQogICAgICAgICAgdHJhcCAncm0gLXJmICIkcmVzdWx0cyInIEVYSVQKICAgICAgICAgIGRvdG5ldCB0ZXN0IHRlc3RzL0Jyb2tlci5Ccm93c2VyLkxpdmUuVGVzdHMvQnJva2VyLkJyb3dzZXIuTGl2ZS5UZXN0cy5mc3Byb2ogXAogICAgICAgICAgICAtLWNvbmZpZ3VyYXRpb24gUmVsZWFzZSAtLW5vLXJlc3RvcmUgLS1uby1idWlsZCBcCiAgICAgICAgICAgIC0tZmlsdGVyICdGdWxseVF1YWxpZmllZE5hbWV+cHJlLWFybSBwYWlyaW5nfEZ1bGx5UXVhbGlmaWVkTmFtZX5uYXRpdmUgcmVwb3J0LCBhdXRoZW50aWNhdGVkIFdlYlNvY2tldCwgYXV0aG9yaXR5IEFDSywgYW5kIElEMCBjb21tYW5kIHNoYXJlIGV4YWN0IGlkZW50aXRpZXMnIFwKICAgICAgICAgICAgLS1yZXN1bHRzLWRpcmVjdG9yeSAiJHJlc3VsdHMiIC0tbG9nZ2VyICd0cng7TG9nRmlsZU5hbWU9Y29udHJvbHMudHJ4JwogICAgICAgICAgcHl0aG9uMyAtICIkcmVzdWx0cy9jb250cm9scy50cngiIDw8J1BZJwogICAgICAgICAgaW1wb3J0IHN5cwogICAgICAgICAgaW1wb3J0IHhtbC5ldHJlZS5FbGVtZW50VHJlZSBhcyBFVAogICAgICAgICAgbnMgPSB7InQiOiAiaHR0cDovL21pY3Jvc29mdC5jb20vc2NoZW1hcy9WaXN1YWxTdHVkaW8vVGVhbVRlc3QvMjAxMCJ9CiAgICAgICAgICByb290ID0gRVQucGFyc2Uoc3lzLmFyZ3ZbMV0pLmdldHJvb3QoKQogICAgICAgICAgZXhwZWN0ZWQgPSB7CiAgICAgICAgICAgICAgImdhdGV3YXkgZGlhZ25vc3RpYyBjbGFzc2lmaWNhdGlvbi5wcmUtYXJtIHBhaXJpbmcgcmVhc29ucyBhcmUgZmluaXRlIGFuZCBjYWxsYmFja3MgY2Fubm90IGV4cG9zZSBwcml2YXRlIG1hdGVyaWFsIiwKICAgICAgICAgICAgICAicHJvZHVjdGlvbiBsaXZlIGJvdW5kYXJ5LnByZS1hcm0gcGFpcmluZyBlbWl0cyBjbG9zZWQgcmVhc29ucyBhbmQgcHJlc2VydmVzIHJlZnVzYWwgd2l0aG91dCBhdXRob3JpdHkiLAogICAgICAgICAgICAgICJwcm9kdWN0aW9uIGxpdmUgYm91bmRhcnkubmF0aXZlIHJlcG9ydCwgYXV0aGVudGljYXRlZCBXZWJTb2NrZXQsIGF1dGhvcml0eSBBQ0ssIGFuZCBJRDAgY29tbWFuZCBzaGFyZSBleGFjdCBpZGVudGl0aWVzIiwKICAgICAgICAgIH0KICAgICAgICAgIHJlc3VsdHMgPSByb290LmZpbmRhbGwoInQ6UmVzdWx0cy90OlVuaXRUZXN0UmVzdWx0IiwgbnMpCiAgICAgICAgICBpZiBsZW4ocmVzdWx0cykgIT0gMyBvciB7ci5hdHRyaWJbInRlc3ROYW1lIl0gZm9yIHIgaW4gcmVzdWx0c30gIT0gZXhwZWN0ZWQgb3IgYW55KHIuYXR0cmliWyJvdXRjb21lIl0gIT0gIlBhc3NlZCIgZm9yIHIgaW4gcmVzdWx0cyk6CiAgICAgICAgICAgICAgcmFpc2UgU3lzdGVtRXhpdCgiRXhwZWN0ZWQgZXhhY3RseSB0aGUgdGhyZWUgcGFzc2luZyBzeW50aGV0aWMgY29udHJvbHM7IHplcm8sIG1pc3NpbmcsIHNraXBwZWQgb3IgZXh0cmEgdGVzdHMgcmVmdXNlIHF1YWxpZmljYXRpb24iKQogICAgICAgICAgcHJpbnQoIlBBU1M6IGV4YWN0IHRocmVlIHN5bnRoZXRpYyBjb21waWxlZCBzb3VyY2UgY29udHJvbHMiKQogICAgICAgICAgUFkK')
        prearm = cls.receiver / MODULE.BAR_PREARM
        prearm.write_bytes(cls.bar_prearm_base)
        base = commit(cls.receiver)
        for path in MODULE.PRODUCER_PATHS:
            target = cls.receiver / path; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes((cls.owner / path).read_bytes()); target.chmod((cls.owner / path).stat().st_mode & 0o777)
        head = commit(cls.receiver)
        prearm.write_bytes(MODULE.installation_bar_workflow(cls.bar_prearm_base))
        cls.bar_head = commit(cls.receiver)
        prearm.write_bytes(cls.bar_prearm_base)
        assertion_pin.write_bytes(cls.assertion_pin_base.replace(cls.assertion_old_pin.encode(), source.encode()))
        cls.templates_head = commit(cls.receiver)
        assertion_pin.write_bytes(cls.assertion_pin_base)
        registration.write_bytes(MODULE.installation_registration(cls.registration_base,(cls.owner/MODULE.PRODUCER_PATHS[1]).read_bytes()))
        cls.registered_head=commit(cls.receiver)
        registration.write_bytes(cls.registration_base)
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
            # Actual Git one-directory objects supply the maintained selected-path traversal.
            pending = [tree_sha]
            while pending:
                directory_sha = pending.pop()
                endpoint = f"repos/{repo}/git/trees/{directory_sha}"
                if endpoint in responses:
                    continue
                children = []
                for raw in fixture_git(root, "ls-tree", "-z", directory_sha).split(b"\0"):
                    if not raw:
                        continue
                    meta, name = raw.split(b"\t", 1)
                    mode, kind, sha = meta.decode().split()
                    children.append(dict(path=name.decode(), mode=mode, type=kind, sha=sha))
                    if kind == "tree":
                        pending.append(sha)
                responses[endpoint] = dict(sha=directory_sha, truncated=False, tree=children)
            return rows, tree_sha
        objects("FS-GG/.github", cls.owner, source); objects("FS-GG/.github", cls.owner, obsolete)
        objects("FS-GG/.github", cls.owner, cls.assertion_old_pin)
        responses[f"repos/FS-GG/.github/compare/{cls.assertion_old_pin}...{source}?per_page=1&page=2"] = dict(status="ahead", base_commit={"sha":cls.assertion_old_pin}, merge_base_commit={"sha":cls.assertion_old_pin})
        objects("FS-GG/.github", cls.owner, owner_head)
        objects("FS-GG/FS.GG.Coordination", cls.receiver, base); rows, tree = objects("FS-GG/FS.GG.Coordination", cls.receiver, head)
        cls.bar_rows,cls.bar_tree=objects("FS-GG/FS.GG.Coordination",cls.receiver,cls.bar_head)
        cls.registered_rows,cls.registered_tree=objects("FS-GG/FS.GG.Coordination",cls.receiver,cls.registered_head)
        cls.templates_rows,cls.templates_tree=objects("FS-GG/FS.GG.Coordination",cls.receiver,cls.templates_head)
        objects("FS-GG/FS.GG.Coordination",cls.receiver,cls.successor)
        responses["repos/FS-GG/FS.GG.Coordination"] = dict(id=1346720714, full_name="FS-GG/FS.GG.Coordination", default_branch="main")
        responses["repos/FS-GG/.github"] = dict(id=1269292704, full_name="FS-GG/.github", default_branch="main")
        for revision in (source, obsolete):
            responses[f"repos/FS-GG/.github/compare/{revision}...{owner_head}"] = dict(status="ahead", base_commit={"sha":revision}, merge_base_commit={"sha": revision})
        responses["repos/FS-GG/.github/git/ref/heads/main"] = dict(ref="refs/heads/main",object=dict(type="commit",sha=owner_head))
        responses["repos/FS-GG/FS.GG.Coordination/git/ref/heads/main"] = {"object": {"sha": base}}
        responses["repos/FS-GG/FS.GG.Coordination/pulls/1/files?per_page=100"] = [dict(filename=row["path"], status="added", sha=row["sha"]) for row in rows if row["path"] in MODULE.PRODUCER_PATHS]
        responses["repos/FS-GG/FS.GG.Coordination/actions/workflows?per_page=100"] = dict(total_count=0, workflows=[])
        responses[f"repos/FS-GG/FS.GG.Coordination/commits/{head}/check-runs?per_page=100"] = dict(total_count=1, check_runs=[dict(id=8,name="test",head_sha=head,app={"id":15368},check_suite={"id":9},status="completed",conclusion="success",details_url="https://github.com/FS-GG/FS.GG.Coordination/actions/runs/7/job/8")])
        responses["repos/FS-GG/FS.GG.Coordination/actions/runs/7"] = dict(id=7, head_sha=head, repository={"full_name": "FS-GG/FS.GG.Coordination"}, status="completed", conclusion="success", run_attempt=1,check_suite_id=9)
        responses["repos/FS-GG/FS.GG.Coordination/actions/jobs/8"] = dict(id=8, run_id=7, head_sha=head, name="test", status="completed", conclusion="success", run_attempt=1,check_run_url="https://api.github.com/repos/FS-GG/FS.GG.Coordination/check-runs/8")
        responses[f"repos/FS-GG/FS.GG.Coordination/compare/{head}...{head}"] = dict(status="identical", merge_base_commit={"sha": head})
        cls.fixture = dict(owner_head=owner_head, source=source, obsolete=obsolete, head=head, base=base, tree=tree, responses=responses)
        cls.pr = opened(head); cls.pr.update(number=1, changed_files=5)
        cls.pr["base"].update(sha=base, repo=dict(id=1346720714, full_name="FS-GG/FS.GG.Coordination"))
        cls.pr["head"].update(ref="routine/install-producer", repo=dict(id=1346720714, full_name="FS-GG/FS.GG.Coordination"))
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
            return MODULE.summarize(api, repo="FS-GG/FS.GG.Coordination", pr_number=1,
                expected_head=self.fixture["head"], merge_method="squash", publication_required=False,
                apply=apply, coherent_workflow="CI.yml", producer_source=source or self.fixture["source"], installation_prior=prior)

    def fsquint_transport(self, *, apply=False, change=None, drift=None, prior=None,
                           selector="ci.yml", merged=False, uncertain=False, required=None, fresh=None, receiver="FS-GG/FsQuint"):
        repo = receiver
        repo_id = MODULE.FOURD_REPOSITORY_ID if repo == MODULE.FOURD_REPOSITORY else MODULE.SC2_REPOSITORY_ID if repo == MODULE.SC2_REPOSITORY else MODULE.BAR_REPOSITORY_ID if repo == MODULE.BAR_REPOSITORY else 1375501179
        workflow_id = MODULE.FOURD_VERIFY_ID if repo == MODULE.FOURD_REPOSITORY else MODULE.SC2_VERIFY_ID if repo == MODULE.SC2_REPOSITORY else MODULE.BAR_PREARM_ID if repo == MODULE.BAR_REPOSITORY else 361154226
        workflow_file = "verify.yml" if repo in (MODULE.SC2_REPOSITORY, MODULE.FOURD_REPOSITORY) else "prearm-source-controls.yml" if repo == MODULE.BAR_REPOSITORY else "ci.yml"
        job_name = "verify" if repo in (MODULE.SC2_REPOSITORY, MODULE.FOURD_REPOSITORY) else "prearm-source-controls" if repo == MODULE.BAR_REPOSITORY else "qualify"
        alias = lambda value: json.loads(json.dumps(value).replace("FS-GG/FS.GG.Coordination", repo).replace("1346720714", str(repo_id)))
        fixture = alias(self.fixture)
        responses = fixture["responses"]
        pr = alias(self.pr)
        pr["head"]["ref"] = "routine/install-routine-producer-90ccedb7"
        after = alias(self.after)
        after["head"]["ref"] = pr["head"]["ref"]
        if repo == MODULE.BAR_REPOSITORY:
            old_head = fixture['head']; fixture['head'] = self.bar_head
            for value in (pr, after):
                value['head']['sha'] = self.bar_head; value['changed_files'] = 6
                value['body'] = value['body'].replace(old_head, self.bar_head)
            after['merge_commit_sha'] = self.bar_head
            responses[f"repos/{repo}/pulls/1/files?per_page=100"] = [dict(filename=row['path'],sha=row['sha'],status='modified' if row['path']==MODULE.BAR_PREARM else 'added') for row in self.bar_rows if row['path'] in (*MODULE.PRODUCER_PATHS,MODULE.BAR_PREARM)]
            checks = responses.pop(f"repos/{repo}/commits/{old_head}/check-runs?per_page=100")
            checks['check_runs'][0]['head_sha'] = self.bar_head
            responses[f"repos/{repo}/commits/{self.bar_head}/check-runs?per_page=100"] = checks
            responses[f"repos/{repo}/actions/runs/7"]['head_sha'] = self.bar_head
            responses[f"repos/{repo}/actions/jobs/8"]['head_sha'] = self.bar_head
            responses[f"repos/{repo}/compare/{self.bar_head}...{self.bar_head}"] = dict(status='identical',merge_base_commit={'sha':self.bar_head})
        head = fixture["head"]
        responses[f"repos/{repo}/git/ref/heads/main"] = dict(ref="refs/heads/main",object=dict(type="commit",sha=fixture['base']))
        metadata = responses[f"repos/{repo}"]
        metadata.update(private=repo in (MODULE.SC2_REPOSITORY, MODULE.FOURD_REPOSITORY), permissions=dict(admin=True, push=True, pull=True))
        wf = dict(id=workflow_id, path=".github/workflows/"+workflow_file, state="active",name=workflow_file[:-4])
        responses[f"repos/{repo}/actions/workflows/{workflow_file}"] = wf
        responses[f"repos/{repo}/actions/workflows?per_page=100"] = dict(total_count=1, workflows=[wf])
        run = responses[f"repos/{repo}/actions/runs/7"]
        run.update(workflow_id=workflow_id, path=wf['path'], event="pull_request", head_branch=pr['head']['ref'])
        run['repository']['id'] = repo_id
        responses[f"repos/{repo}/actions/workflows/{workflow_id}/runs?head_sha={head}&per_page=100"] = dict(total_count=1,workflow_runs=[copy.deepcopy(run)])
        job = responses[f"repos/{repo}/actions/jobs/8"]
        job.update(name=job_name,completed_at="2026-10-13T00:00:00Z",steps=[dict(name=name,status="completed",conclusion="success") for name in
            ["Set up job", "Run actions/checkout@v7", "Run actions/setup-dotnet@v6", "Provision pinned Quint",
             "Check independent CI preflight example", "Qualify core, tooling and external package consumer"]])
        if repo == MODULE.SC2_REPOSITORY:
            # Synthetic native blobs exercise correspondence without exporting private verifier source.
            retained = {'.github/workflows/verify.yml': b'synthetic reviewed workflow\n',
                        'scripts/verify.sh': b'synthetic reviewed entrypoint\n'}
            for path, body in retained.items():
                sha=hashlib.sha1(f"blob {len(body)}\0".encode()+body).hexdigest()
                row=dict(path=path,type="blob",mode="100644",sha=sha)
                responses[f"repos/{repo}/git/blobs/{sha}"]=dict(sha=sha,encoding="base64",size=len(body),content=MODULE.base64.b64encode(body).decode())
                for revision in (fixture['base'],head):
                    tree=responses[f"repos/{repo}/git/commits/{revision}"]['tree']['sha']
                    responses[f"repos/{repo}/git/trees/{tree}?recursive=1"]['tree'].append(copy.deepcopy(row))
            job['steps']=[dict(number=index,name=name,status='completed',conclusion='success') for index,name in enumerate([
                'Set up job','Check out the complete product history','Install the pinned .NET SDK',
                'Install the pinned Node runtime','Install the pinned Rust toolchain','Verify the product',
                'Retain the complete qualified product','Post Install the pinned Node runtime',
                'Post Check out the complete product history','Complete job'],1)]
            responses[f"repos/{repo}/actions/runs/7/artifacts?per_page=100"]=dict(total_count=1,artifacts=[dict(
                id=11,name=f'sc2-qualified-product-{"e"*40}-7',expired=False,size_in_bytes=100,
                workflow_run=dict(id=7,head_sha=head))])
            checkout='e'*40
            native_commit=copy.deepcopy(responses[f"repos/{repo}/git/commits/{head}"])
            native_commit.update(sha=checkout,parents=[dict(sha=fixture['base']),dict(sha=head)])
            responses[f"repos/{repo}/git/commits/{checkout}"]=native_commit
        if repo == MODULE.BAR_REPOSITORY:
            job['steps'] = [dict(number=index,name=name,status='completed',conclusion='success') for index,name in enumerate((*MODULE.BAR_PREARM_TECHNICAL_STEPS,'Complete job'),1)]
        responses[f"repos/{repo}/actions/runs/7/attempts/1/jobs?per_page=100"] = dict(total_count=1,jobs=[copy.deepcopy(job)])
        responses[f"repos/{repo}/commits/{head}/check-runs?per_page=100"]['check_runs'][0]['name'] = job_name
        responses[f"repos/{repo}/commits/{head}/status?per_page=100"] = dict(sha=head,total_count=0,statuses=[])
        policy = {
            f"repos/{repo}/branches/main/protection/required_status_checks": (1,404,dict(message="Branch not protected",documentation_url="https://docs.github.com/rest/branches/branch-protection#get-status-checks-protection",status="404")),
            f"repos/{repo}/rules/branches/main?per_page=100": (0,200,[]),
            f"repos/{repo}/rulesets?includes_parents=true&per_page=100": (0,200,[]),
        }
        if repo == MODULE.FOURD_REPOSITORY:
            pr['changed_files'] = after['changed_files'] = 7
            baseline = self.fourd_retained
            def install_blob(path, body, revision, mode='100644'):
                sha = hashlib.sha1(f"blob {len(body)}\0".encode() + body).hexdigest()
                row = dict(path=path, type='blob', mode=mode, sha=sha)
                responses[f"repos/{repo}/git/blobs/{sha}"] = dict(sha=sha,encoding='base64',size=len(body),content=MODULE.base64.b64encode(body).decode())
                tree = responses[f"repos/{repo}/git/commits/{revision}"]['tree']['sha']
                rows = responses[f"repos/{repo}/git/trees/{tree}?recursive=1"]['tree']
                rows[:] = [value for value in rows if value['path'] != path] + [row]
                return row
            for path,body in baseline.items():
                for revision in (fixture['base'],head):
                    selected = (MODULE.installation_fourd_workflow(body) if path == MODULE.FOURD_SOURCE else MODULE.installation_fourd_inventory(body) if path == MODULE.FOURD_INVENTORY_TEST else body) if revision == head else body
                    install_blob(path,selected,revision,'100755' if path=='scripts/verify.sh' else '100644')
            tree = responses[f"repos/{repo}/git/commits/{head}"]['tree']['sha']
            selected_rows = responses[f"repos/{repo}/git/trees/{tree}?recursive=1"]['tree']
            responses[f"repos/{repo}/pulls/1/files?per_page=100"] = [dict(filename=row['path'],sha=row['sha'],status='modified' if row['path'] in (MODULE.FOURD_SOURCE,MODULE.FOURD_INVENTORY_TEST) else 'added') for row in selected_rows if row['path'] in (*MODULE.PRODUCER_PATHS,MODULE.FOURD_SOURCE,MODULE.FOURD_INVENTORY_TEST)]
            job['steps'] = [dict(number=i,name=name,status='completed',conclusion='success') for i,name in enumerate((*MODULE.FOURD_VERIFY_STEPS,'Post Set up Node.js','Post Check out the candidate','Complete job'),1)]
            responses[f"repos/{repo}/actions/runs/7/attempts/1/jobs?per_page=100"]['jobs'] = [copy.deepcopy(job)]
            checkout = 'e'*40
            commit = copy.deepcopy(responses[f"repos/{repo}/git/commits/{head}"])
            commit.update(sha=checkout,parents=[dict(sha=fixture['base']),dict(sha=head)])
            responses[f"repos/{repo}/git/commits/{checkout}"] = commit
            responses[f"repos/{repo}/actions/runs/7/artifacts?per_page=100"] = dict(total_count=1,artifacts=[dict(id=11,name=f'fourd-evaluation-candidate-{checkout}-7-1',expired=False,size_in_bytes=100,workflow_run=dict(id=7,head_sha=head))])
            prep = dict(id=MODULE.FOURD_SOURCE_ID,path=MODULE.FOURD_SOURCE,state='active',name='portable-workspace-source-preparation')
            responses[f"repos/{repo}/actions/workflows/{MODULE.FOURD_SOURCE.rsplit('/',1)[1]}"] = prep
            responses[f"repos/{repo}/actions/workflows?per_page=100"] = dict(total_count=2,workflows=[wf,prep])
            source_run = copy.deepcopy(run);source_run.update(id=17,workflow_id=MODULE.FOURD_SOURCE_ID,path=MODULE.FOURD_SOURCE,check_suite_id=19)
            responses[f"repos/{repo}/actions/runs/17"] = source_run
            responses[f"repos/{repo}/actions/workflows/{MODULE.FOURD_SOURCE_ID}/runs?head_sha={head}&per_page=100"] = dict(total_count=1,workflow_runs=[copy.deepcopy(source_run)])
            source_job = copy.deepcopy(job);source_job.update(id=18,run_id=17,name='source-preparation',check_run_url=f'https://api.github.com/repos/{repo}/check-runs/18',steps=[dict(number=i,name=name,status='completed',conclusion='success') for i,name in enumerate((*MODULE.FOURD_SOURCE_STEPS,*MODULE.FOURD_SOURCE_TAIL),1)])
            responses[f"repos/{repo}/actions/jobs/18"] = source_job
            responses[f"repos/{repo}/actions/runs/17/attempts/1/jobs?per_page=100"] = dict(total_count=1,jobs=[copy.deepcopy(source_job)])
            source_check = copy.deepcopy(responses[f"repos/{repo}/commits/{head}/check-runs?per_page=100"]['check_runs'][0]);source_check.update(id=18,name='source-preparation',details_url=f'https://github.com/{repo}/actions/runs/17/job/18',check_suite=dict(id=19))
            responses[f"repos/{repo}/commits/{head}/check-runs?per_page=100"] = dict(total_count=2,check_runs=[responses[f"repos/{repo}/commits/{head}/check-runs?per_page=100"]['check_runs'][0],source_check])
            for identifier,body in self.fourd_policy.items():
                responses[f"repos/{repo}/issues/comments/{identifier}"] = dict(id=identifier,html_url=f'https://github.com/{repo}/issues/39#issuecomment-{identifier}',user=dict(id=MODULE.FOURD_POLICY_ACTOR_ID,login='EHotwagner'),body=body)
            policy = {f'repos/{repo}/{route}':(1,403,dict(message=MODULE.FOURD_PLAN_MESSAGE,status='403',documentation_url=url)) for route,url in MODULE.FOURD_POLICY_ROUTES.items()}
        if change: change(responses, policy, pr)
        api = InstallationApi(fixture, [after] if merged else [pr,pr,after], [dict(merged=True,sha=head)])
        original_get_pr=api.get_pr
        pr_reads=0
        def fresh_pr(*args):
            nonlocal pr_reads
            pr_reads+=1
            if fresh and pr_reads==2: fresh(responses,policy,pr)
            return original_get_pr(*args)
        api.get_pr=fresh_pr
        api.required = []
        api.installation_read = MODULE.GhApi.installation_read.__get__(api)
        reads = []
        policy_reads = 0
        def query(command, *_args, **_kwargs):
            nonlocal policy_reads
            reads.append(command)
            if command[:3] == ["gh","pr","checks"]:
                native = (1,b"",b"no required checks reported on the 'routine/install-routine-producer-90ccedb7' branch\n")
                response = native if required is None else required
                if _kwargs.get("stderr_output") is not None:
                    _kwargs["stderr_output"].extend(response[2] if len(response)==3 else b"")
                return response[:2]
            endpoint = command[-1]
            if endpoint in policy:
                if endpoint.endswith("required_status_checks"):
                    policy_reads += 1
                    if drift and policy_reads == 2: drift(responses, policy, pr)
                code,status,body = policy[endpoint]
                return code,(f"HTTP/2.0 {status} Result\r\nContent-Type: application/json\r\n\r\n" + (body if isinstance(body,str) else json.dumps(body))).encode()
            if endpoint not in responses:
                raise AssertionError(endpoint)
            return 0,json.dumps(responses[endpoint]).encode()
        def native_write(args, **kwargs):
            api.attempts += 1
            self.assertEqual(args[:3],["--method","PUT",f"repos/{repo}/pulls/1/merge"])
            self.assertEqual(json.loads(kwargs['input_text']),dict(sha=head,merge_method="squash"))
            responses[f"repos/{repo}/git/ref/heads/main"] = dict(object=dict(sha=head))
            if uncertain: raise MODULE.AmbiguousWrite("lost response")
            return dict(merged=True,sha=head)
        api._run = native_write
        if merged: responses[f"repos/{repo}/git/ref/heads/main"] = dict(object=dict(sha=head))
        watcher=MODULE.WatchGhApi
        with mock.patch.multiple(MODULE,
                SC2_VERIFY_SHA256=hashlib.sha256(retained['.github/workflows/verify.yml']).hexdigest() if repo == MODULE.SC2_REPOSITORY else MODULE.SC2_VERIFY_SHA256,
                SC2_VERIFY_ENTRY_SHA256=hashlib.sha256(retained['scripts/verify.sh']).hexdigest() if repo == MODULE.SC2_REPOSITORY else MODULE.SC2_VERIFY_ENTRY_SHA256), \
                mock.patch.object(MODULE,'bounded_watch_query',side_effect=query), \
                mock.patch.object(MODULE,'WatchGhApi',side_effect=lambda deadline:watcher(deadline,query=query)):
            code,result = MODULE.summarize(api,repo=repo,pr_number=1,expected_head=head,merge_method="squash",
                publication_required=False,apply=apply,coherent_workflow=selector,producer_source=fixture['source'],installation_prior=prior)
        return code,result,api,reads

    def fourd_transport(self, **kwargs):
        self.fourd_retained = {
            '.github/workflows/verify.yml': b'synthetic reviewed FourD verifier\n',
            'scripts/verify.sh': b'synthetic reviewed FourD verifier entrypoint\n',
            'tests/portable-workspace/validate-source-preparation.py': b'synthetic reviewed source entrypoint\n',
            MODULE.FOURD_SOURCE: b'on:\n  pull_request:\n    paths:\n      - old/**\n  push:\n    paths:\n      - old/**\n',
            MODULE.FOURD_INVENTORY_TEST: b'                    self.assertIn("runs-on: ubuntu-24.04", text)\n        self.assertEqual(set(self.workflows), {"fourd-native-image-qualification.yml", "portable-workspace-source-preparation.yml", "verify.yml"})\n',
        }
        if kwargs.pop('large_inventory', False):
            body = self.fourd_retained[MODULE.FOURD_INVENTORY_TEST]
            self.fourd_retained[MODULE.FOURD_INVENTORY_TEST] = body + b'#' * (248864 - len(body))
        inventory = self.fourd_retained[MODULE.FOURD_INVENTORY_TEST]
        with mock.patch.object(MODULE, 'FOURD_INVENTORY_BASE_SHA256', hashlib.sha256(inventory).hexdigest()):
            derived_digest = hashlib.sha256(MODULE.installation_fourd_inventory(inventory)).hexdigest()
        self.fourd_policy = {identifier:'synthetic policy '+str(identifier) for identifier in MODULE.FOURD_POLICY_COMMENTS}
        with mock.patch.multiple(MODULE,
                FOURD_VERIFY_SHA256=hashlib.sha256(self.fourd_retained['.github/workflows/verify.yml']).hexdigest(),
                FOURD_VERIFY_ENTRY_SHA256=hashlib.sha256(self.fourd_retained['scripts/verify.sh']).hexdigest(),
                FOURD_SOURCE_ENTRY_SHA256=hashlib.sha256(self.fourd_retained['tests/portable-workspace/validate-source-preparation.py']).hexdigest(),
                FOURD_SOURCE_BASE_SHA256=hashlib.sha256(self.fourd_retained[MODULE.FOURD_SOURCE]).hexdigest(),
                FOURD_INVENTORY_BASE_SHA256=hashlib.sha256(self.fourd_retained[MODULE.FOURD_INVENTORY_TEST]).hexdigest(),
                FOURD_INVENTORY_DERIVED_SHA256=derived_digest,
                FOURD_POLICY_COMMENTS={k:hashlib.sha256(v.encode()).hexdigest() for k,v in self.fourd_policy.items()}):
            return self.fsquint_transport(receiver=MODULE.FOURD_REPOSITORY,selector=kwargs.pop('selector','verify.yml'),**kwargs)

    def test_fourd_native_inventory_envelope_above_ordinary_bound_is_closed(self):
        code, result, api, reads = self.fourd_transport(large_inventory=True)
        self.assertEqual(code, 0, result.reason)
        body = self.fourd_retained[MODULE.FOURD_INVENTORY_TEST]
        self.assertEqual(len(body), 248864)
        sha = hashlib.sha1(f"blob {len(body)}\0".encode() + body).hexdigest()
        row = dict(type="blob", mode="100644", sha=sha)
        envelope = dict(sha=sha, encoding="base64", size=len(body), content=MODULE.base64.b64encode(body).decode())
        reader = MODULE.GhApi()
        with mock.patch.object(reader, "installation_read", return_value=envelope):
            with self.assertRaisesRegex(RuntimeError, "identity/encoding"):
                reader.installation_blob(MODULE.FOURD_REPOSITORY, row)
            for repo, path in [("FS-GG/Other", MODULE.FOURD_INVENTORY_TEST), (MODULE.FOURD_REPOSITORY, "other.py")]:
                with self.assertRaisesRegex(RuntimeError, "path/receiver"):
                    reader.installation_fourd_inventory_blob(repo, path, row, derived=False)
            with self.assertRaisesRegex(RuntimeError, "digest"):
                reader.installation_fourd_inventory_blob(MODULE.FOURD_REPOSITORY, MODULE.FOURD_INVENTORY_TEST, row, derived=False)
            for size in (262145, True, len(body) + 1):
                with mock.patch.object(reader, "installation_read", return_value={**envelope, 'size': size}):
                    with self.assertRaises(RuntimeError):
                        reader.installation_fourd_inventory_blob(MODULE.FOURD_REPOSITORY, MODULE.FOURD_INVENTORY_TEST, row, derived=False)

    def test_fourd_installation_refuses_non_squash_before_any_native_call(self):
        for method in ("merge", "rebase"):
            api = mock.Mock()
            with self.assertRaisesRegex(RuntimeError, "accepted squash"):
                MODULE.summarize(api, repo=MODULE.FOURD_REPOSITORY, pr_number=1,
                    expected_head="a" * 40, merge_method=method, publication_required=False,
                    apply=True, coherent_workflow="verify.yml", producer_source="b" * 40)
            self.assertEqual(api.mock_calls, [])

    def test_fourd_closed_seven_file_install_requires_both_technical_workflows_and_keeps_activation_pending(self):
        for apply in (False,True):
            code,result,api,reads=self.fourd_transport(apply=apply)
            self.assertEqual((code,api.attempts),(0,int(apply)),result.reason)
            self.assertEqual(result.reason,'native-policy-unavailable; no-required-contexts-reported')
            self.assertEqual(result.producerInstallation,'delivered-activation-pending' if apply else 'ready')
            self.assertEqual(result.routineEligibility,'unavailable-first-install')
            self.assertTrue(any(f'/actions/workflows/{MODULE.FOURD_VERIFY_ID}/runs?' in c[-1] for c in reads))
            self.assertTrue(any(f'/actions/workflows/{MODULE.FOURD_SOURCE_ID}/runs?' in c[-1] for c in reads))

    def test_fourd_policy_denial_is_not_empty_or_a_generic_permission_exception(self):
        repo=MODULE.FOURD_REPOSITORY
        for required in [(1,b'[]'),(0,b'[]'),(1,b'',b'HTTP 403: denied'),(1,b'',b'no required checks reported on wrong branch\n')]:
            code,result,api,_=self.fourd_transport(apply=True,required=required)
            self.assertEqual((code,api.attempts),(2,0),result.reason)
        for change in [
            lambda r,p,pr:r[f'repos/{repo}/issues/comments/6335828296'].update(body='changed'),
            lambda r,p,pr:r[f'repos/{repo}/issues/comments/6335951089']['user'].update(id=1),
            lambda r,p,pr:p.update({f'repos/{repo}/rules/branches/main?per_page=100':(0,200,[])}),
            lambda r,p,pr:p.update({f'repos/{repo}/rulesets?includes_parents=true&per_page=100':(1,403,dict(message='Resource not accessible by personal access token',status='403',documentation_url=MODULE.FOURD_POLICY_ROUTES['rulesets?includes_parents=true&per_page=100']))}),
            lambda r,p,pr:p.update({f'repos/{repo}/rules/branches/main?per_page=100':(1,403,'{"message":"a","message":"b"}')}),
        ]:
            code,result,api,_=self.fourd_transport(apply=True,change=change)
            self.assertEqual((code,api.attempts),(2,0),result.reason)

    def test_fourd_each_technical_workflow_requires_full_native_identity_and_steps(self):
        repo=MODULE.FOURD_REPOSITORY;head=self.fixture['head']
        for runid,jobid,wfid,name in [(7,8,MODULE.FOURD_VERIFY_ID,'verify'),(17,18,MODULE.FOURD_SOURCE_ID,'source-preparation')]:
            population=f'repos/{repo}/actions/workflows/{wfid}/runs?head_sha={head}&per_page=100'
            for alter in [
                lambda r,p,pr:r[population].update(total_count=0,workflow_runs=[]),
                lambda r,p,pr:r[population].update(total_count=2,workflow_runs=r[population]['workflow_runs']*2),
                lambda r,p,pr:r[population]['workflow_runs'][0].update(run_attempt=2),
                lambda r,p,pr:r[population]['workflow_runs'][0].update(status='in_progress',conclusion=None),
                lambda r,p,pr:r[f'repos/{repo}/actions/runs/{runid}'].update(check_suite_id=99),
                lambda r,p,pr:r[f'repos/{repo}/actions/jobs/{jobid}'].update(run_attempt=2),
                lambda r,p,pr:r[f'repos/{repo}/actions/runs/{runid}/attempts/1/jobs?per_page=100']['jobs'][0]['steps'][1].update(conclusion='skipped'),
                lambda r,p,pr:next(x for x in r[f'repos/{repo}/commits/{head}/check-runs?per_page=100']['check_runs'] if x['name']==name)['app'].update(id=42),
                lambda r,p,pr:r[f'repos/{repo}/commits/{head}/check-runs?per_page=100'].update(total_count=1,check_runs=[x for x in r[f'repos/{repo}/commits/{head}/check-runs?per_page=100']['check_runs'] if x['name']!=name]),
            ]:
                code,result,api,_=self.fourd_transport(apply=True,change=alter)
                self.assertEqual((code,api.attempts),(2,0),(name,result.reason))

    def test_fourd_refuses_extra_missing_or_mutated_seventh_and_wrong_receiver(self):
        repo=MODULE.FOURD_REPOSITORY;head=self.fixture['head'];tree=self.fixture['responses'][f'repos/FS-GG/FS.GG.Coordination/git/commits/{head}']['tree']['sha'];endpoint=f'repos/{repo}/git/trees/{tree}?recursive=1'
        for alter in [
            lambda r,p,pr:pr.update(changed_files=6),
            lambda r,p,pr:r[f'repos/{repo}'].update(private=False),
            lambda r,p,pr:r[f'repos/{repo}'].update(id=1390567199),
            lambda r,p,pr:r[endpoint].update(tree=[x for x in r[endpoint]['tree'] if x['path']!=MODULE.FOURD_INVENTORY_TEST]),
            lambda r,p,pr:next(x for x in r[endpoint]['tree'] if x['path']==MODULE.FOURD_INVENTORY_TEST).update(mode='100755'),
            lambda r,p,pr:r[f'repos/{repo}/pulls/1/files?per_page=100'][-1].update(status='added'),
            lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100']['artifacts'][0].update(name=f'fourd-evaluation-candidate-{head}-7-1'),
        ]:
            code,result,api,_=self.fourd_transport(apply=True,change=alter)
            self.assertEqual((code,api.attempts),(2,0),result.reason)

    def test_fourd_mutated_derived_blobs_and_unrelated_seventh_do_not_qualify(self):
        repo=MODULE.FOURD_REPOSITORY;head=self.fixture['head'];base=self.fixture['base']
        def replace_blob(responses,revision,path,change):
            tree=responses[f'repos/{repo}/git/commits/{revision}']['tree']['sha']
            row=next(x for x in responses[f'repos/{repo}/git/trees/{tree}?recursive=1']['tree'] if x['path']==path)
            value=responses[f"repos/{repo}/git/blobs/{row['sha']}"]
            body=change(MODULE.base64.b64decode(value['content']))
            sha=hashlib.sha1(f'blob {len(body)}\0'.encode()+body).hexdigest();row['sha']=sha
            responses[f'repos/{repo}/git/blobs/{sha}']=dict(sha=sha,encoding='base64',size=len(body),content=MODULE.base64.b64encode(body).decode())
            if revision==head:
                for file in responses[f'repos/{repo}/pulls/1/files?per_page=100']:
                    if file['filename']==path:file['sha']=sha
        for path in [MODULE.FOURD_SOURCE,MODULE.FOURD_INVENTORY_TEST]:
            for revision in [base,head]:
                def change(r,p,pr):replace_blob(r,revision,path,lambda b:b+b'# unrelated edit\n')
                code,result,api,_=self.fourd_transport(apply=True,change=change)
                self.assertEqual((code,api.attempts),(2,0),result.reason)
                self.assertIn('FourD',result.reason)
        # A readable empty policy is a different capability from the selected unavailable one.
        for route in MODULE.FOURD_POLICY_ROUTES:
            for response in [(0,403,dict(message=MODULE.FOURD_PLAN_MESSAGE,status='403',documentation_url=MODULE.FOURD_POLICY_ROUTES[route])),(1,403,dict(message=MODULE.FOURD_PLAN_MESSAGE,status='403',documentation_url='https://wrong.invalid')),(1,404,dict(message='Not Found'))]:
                code,result,api,_=self.fourd_transport(apply=True,change=lambda r,p,pr:p.update({f'repos/{repo}/{route}':response}))
                self.assertEqual((code,api.attempts),(2,0),result.reason)
        code,result,api,_=self.fsquint_transport(apply=True,change=lambda r,p,pr:pr.update(changed_files=7))
        self.assertEqual((code,api.attempts),(2,0),result.reason)

    def test_fourd_reported_required_contexts_are_additional_and_uncertain_apply_is_not_replayed(self):
        repo=MODULE.FOURD_REPOSITORY
        row=dict(name='source-preparation',bucket='pass',state='SUCCESS',completedAt='2026-10-13T00:00:00Z',event='pull_request',workflow='portable-workspace-source-preparation',startedAt='2026-10-13T00:00:00Z',link=f'https://github.com/{repo}/actions/runs/17/job/18')
        code,result,api,_=self.fourd_transport(required=(0,json.dumps([row]).encode()))
        self.assertEqual(code,0,result.reason)
        self.assertEqual(result.reason,'native-policy-unavailable; required-contexts-passed')
        row['bucket']='pending';row['state']='IN_PROGRESS'
        code,result,api,_=self.fourd_transport(apply=True,required=(0,json.dumps([row]).encode()))
        self.assertEqual((code,api.attempts),(2,0),result.reason)
        code,result,api,_=self.fourd_transport()
        prior=asdict(result);prior.update(attempts=1,codeDelivery='unknown',outcome='indeterminate',producerInstallation='indeterminate')
        code,result,api,_=self.fourd_transport(apply=True,prior=prior)
        self.assertEqual((code,api.attempts,result.attempts),(3,0,1))

    def test_fourd_source_closure_artifact_and_duplicate_roster_refusals(self):
        repo=MODULE.FOURD_REPOSITORY;head=self.fixture['head'];base=self.fixture['base']
        for change in [
            lambda r,p,pr:r[f'repos/{repo}/actions/runs/17'].update(head_branch='wrong'),
            lambda r,p,pr:r[f'repos/{repo}/commits/{head}/check-runs?per_page=100'].update(total_count=3,check_runs=r[f'repos/{repo}/commits/{head}/check-runs?per_page=100']['check_runs']+[copy.deepcopy(r[f'repos/{repo}/commits/{head}/check-runs?per_page=100']['check_runs'][0])]),
            lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100']['artifacts'][0]['workflow_run'].update(head_sha='a'*40),
            lambda r,p,pr:r[f'repos/{repo}/git/commits/{"e"*40}'].update(parents=[dict(sha=head),dict(sha=base)]),
            lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100'].update(total_count=0,artifacts=[]),
            lambda r,p,pr:r[f'repos/{repo}/actions/runs/17/attempts/1/jobs?per_page=100']['jobs'][0]['steps'][1].update(number=True),
            lambda r,p,pr:r[f'repos/{repo}/issues/comments/6335828296']['user'].update(id=float(MODULE.FOURD_POLICY_ACTOR_ID)),
            lambda r,p,pr:r[f'repos/{repo}/commits/{head}/check-runs?per_page=100']['check_runs'][1]['app'].update(id=15368.0),
        ]:
            code,result,api,_=self.fourd_transport(apply=True,change=change)
            self.assertEqual((code,api.attempts),(2,0),result.reason)

    def sc2_transport(self, **kwargs):
        return self.fsquint_transport(receiver=MODULE.SC2_REPOSITORY,selector=kwargs.pop('selector','verify.yml'),**kwargs)

    def test_source_phase_does_not_require_a_pr_or_candidate_checks(self):
        api=self.api()
        api.fixture=copy.deepcopy(api.fixture)
        for endpoint in list(api.fixture['responses']):
            if '/pulls/' in endpoint or '/check-runs' in endpoint or '/actions/runs/' in endpoint:
                del api.fixture['responses'][endpoint]
        facts=api.installation_source('FS-GG/FS.GG.Coordination',self.fixture['head'],'main',self.fixture['base'],self.fixture['source'])
        self.assertEqual(facts['expected'],set(MODULE.PRODUCER_PATHS))
        self.assertEqual(api.attempts,0)
        for revision in [self.successor,self.fixture['base']]:
            with self.assertRaises(RuntimeError):
                api.installation_source('FS-GG/FS.GG.Coordination',revision,'main',self.fixture['base'],self.fixture['source'])

    def test_sc2_native_no_required_contexts_still_requires_exact_full_verify(self):
        for apply in (False,True):
            code,result,api,reads=self.sc2_transport(apply=apply)
            self.assertEqual((code,api.attempts),(0,int(apply)))
            self.assertEqual(result.reason,'no-required-contexts-reported')
            self.assertFalse(any('protection' in c[-1] or 'rules' in c[-1] for c in reads))
            self.assertTrue(any('/actions/workflows/368229845/runs?' in c[-1] for c in reads))

    def test_sc2_denied_or_ambiguous_required_read_is_not_empty(self):
        for response in [(1,b'',b'Resource not accessible by personal access token'),(4,b'',b''),
                         (0,b'[]'),(1,b'[]'),(1,b'',b''),(1,b'',b'no required checks reported on wrong branch\n')]:
            with self.subTest(response=response):
                code,_,api,_=self.sc2_transport(apply=True,required=response)
                self.assertEqual((code,api.attempts),(2,0))

    def test_sc2_required_contexts_remain_additional_requirements(self):
        row=dict(name='verify',bucket='pass',state='SUCCESS',completedAt='2026-10-13T00:00:00Z',event='pull_request',
                 workflow='verify',startedAt='2026-10-13T00:00:00Z',link=f'https://github.com/{MODULE.SC2_REPOSITORY}/actions/runs/7/job/8')
        self.assertEqual(self.sc2_transport(required=(0,json.dumps([row]).encode()))[0],0)
        row.update(bucket='fail',state='FAILURE')
        code,_,api,_=self.sc2_transport(apply=True,required=(0,json.dumps([row]).encode()))
        self.assertEqual((code,api.attempts),(2,0))

    def test_sc2_full_verify_identity_population_steps_and_artifact_refusals(self):
        repo=MODULE.SC2_REPOSITORY;head=self.fixture['head']
        runs=f'repos/{repo}/actions/workflows/368229845/runs?head_sha={head}&per_page=100'
        jobs=f'repos/{repo}/actions/runs/7/attempts/1/jobs?per_page=100'
        checks=f'repos/{repo}/commits/{head}/check-runs?per_page=100'
        variants=[lambda r,p,pr:r[f'repos/{repo}'].update(private=False),
                  lambda r,p,pr:r[f'repos/{repo}'].update(id=1),
                  lambda r,p,pr:r[runs]['workflow_runs'][0].update(head_sha='f'*40),
                  lambda r,p,pr:r[runs]['workflow_runs'][0].update(run_attempt=2),
                  lambda r,p,pr:r[runs].update(total_count=2),
                  lambda r,p,pr:r[jobs]['jobs'][0]['steps'][5].update(conclusion='skipped'),
                  lambda r,p,pr:r[checks]['check_runs'][0]['app'].update(id=1),
                  lambda r,p,pr:r[jobs]['jobs'][0].update(head_sha='f'*40),
                  lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100']['artifacts'][0].update(expired=True),
                  lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100'].update(total_count=0,artifacts=[]),
                  lambda r,p,pr:r[f'repos/{repo}/git/commits/'+('e'*40)]['parents'].reverse(),
                  lambda r,p,pr:r[f'repos/{repo}/git/commits/'+('e'*40)].update(sha=head),
                  lambda r,p,pr:r[f'repos/{repo}/actions/runs/7/artifacts?per_page=100']['artifacts'][0].update(name=f'sc2-qualified-product-{head}-7')]
        for change in variants:
            with self.subTest(change=change):
                code,_,api,_=self.sc2_transport(apply=True,change=change)
                self.assertEqual((code,api.attempts),(2,0))
        code,_,api,_=self.sc2_transport(apply=True,selector='ci.yml');self.assertEqual((code,api.attempts),(2,0))

    def bar_transport(self, **kwargs):
        return self.fsquint_transport(receiver=MODULE.BAR_REPOSITORY,selector=kwargs.pop('selector','prearm-source-controls.yml'),**kwargs)

    def test_bar_authentic_empty_policy_nonempty_technical_cohort_and_expected_head_merge(self):
        for apply in (False,True):
            code,result,api,reads=self.bar_transport(apply=apply)
            self.assertEqual((code,api.attempts,result.routineEligibility),(0,int(apply),'unavailable-first-install'))
            self.assertEqual(result.producerInstallation,'delivered-activation-pending' if apply else 'ready')
            self.assertEqual(sum(c[-1].endswith('required_status_checks') for c in reads),2 if apply else 1)
            self.assertEqual(result.reason,'required-policy-observed-empty')
            self.assertTrue(any(f'/actions/workflows/{MODULE.BAR_PREARM_ID}/runs?' in c[-1] for c in reads))

    def test_bar_exact_source_derivation_modes_and_file_population(self):
        base=self.bar_prearm_base;derived=MODULE.installation_bar_workflow(base)
        appended=b''.join(('      - '+path+'\n').encode() for path in MODULE.PRODUCER_PATHS)
        self.assertEqual(derived,base.replace(b'  workflow_dispatch:\n',appended+b'  workflow_dispatch:\n',1))
        for changed in (base+b' ',base.replace(b'contents: read',b'contents: write'),base.replace(b'timeout-minutes: 10',b'timeout-minutes: 11')):
            with self.assertRaises(RuntimeError):MODULE.installation_bar_workflow(changed)
        repo=MODULE.BAR_REPOSITORY
        tree_path=f"repos/{repo}/git/trees/{self.bar_tree}?recursive=1"
        def edit_tree(r,p,pr,change):
            row=next(row for row in r[tree_path]['tree'] if row['path']==MODULE.BAR_PREARM);change(row)
        for change in (lambda row:row.update(mode='100755'),lambda row:row.update(mode='120000'),lambda row:row.update(type='commit'),lambda row:row.update(path='.github/workflows/renamed.yml')):
            code,_,api,_=self.bar_transport(apply=True,change=lambda r,p,pr:edit_tree(r,p,pr,change))
            self.assertEqual((code,api.attempts),(2,0))
        def extra_byte(r,p,pr):
            row=next(row for row in r[tree_path]['tree'] if row['path']==MODULE.BAR_PREARM)
            original=MODULE.base64.b64decode(r[f"repos/{repo}/git/blobs/{row['sha']}"]['content'])+b' '
            sha=hashlib.sha1(b'blob '+str(len(original)).encode()+b'\0'+original).hexdigest();row['sha']=sha
            r[f"repos/{repo}/git/blobs/{sha}"]=dict(sha=sha,encoding='base64',size=len(original),content=MODULE.base64.b64encode(original).decode())
        code,_,api,_=self.bar_transport(apply=True,change=extra_byte);self.assertEqual((code,api.attempts),(2,0))
        for change in (lambda r,p,pr:pr.update(changed_files=5),lambda r,p,pr:r[f'repos/{repo}/pulls/1/files?per_page=100'][0].update(previous_filename='renamed'),lambda r,p,pr:next(row for row in r[f'repos/{repo}/pulls/1/files?per_page=100'] if row['filename']==MODULE.BAR_PREARM).update(status='added')):
            code,_,api,_=self.bar_transport(apply=True,change=change);self.assertEqual((code,api.attempts),(2,0))

    def test_bar_denied_truncated_nonempty_policy_and_empty_native_checks_refuse(self):
        repo=MODULE.BAR_REPOSITORY
        endpoints=(f'repos/{repo}/branches/main/protection/required_status_checks',f'repos/{repo}/rules/branches/main?per_page=100',f'repos/{repo}/rulesets?includes_parents=true&per_page=100')
        changes=[lambda r,p,pr:p.update({endpoints[0]:(1,404,dict(message='Not Found',status='404'))}),
                 lambda r,p,pr:p.update({endpoints[0]:(1,403,dict(message='denied'))}),
                 lambda r,p,pr:p.update({endpoints[1]:(0,200,'[')}),lambda r,p,pr:p.update({endpoints[1]:(0,200,[dict(type='required_status_checks')])}),
                 lambda r,p,pr:p.update({endpoints[2]:(0,200,[dict(id=1)])}),
                 lambda r,p,pr:r[f'repos/{repo}'].update(permissions=dict(push=True,pull=True)),
                 lambda r,p,pr:r[f'repos/{repo}/commits/{self.bar_head}/check-runs?per_page=100'].update(total_count=0,check_runs=[])]
        for change in changes:
            code,_,api,_=self.bar_transport(apply=True,change=change);self.assertEqual((code,api.attempts),(2,0))
        for required in ((0,b'[]'),(1,b'',b'denied'),(1,b'[]'),(4,b'')):
            code,_,api,_=self.bar_transport(apply=True,required=required);self.assertEqual((code,api.attempts),(2,0))

    def test_bar_native_cohort_source_run_step_and_check_provenance_refuse(self):
        repo=MODULE.BAR_REPOSITORY;head=self.bar_head;runs=f'repos/{repo}/actions/workflows/{MODULE.BAR_PREARM_ID}/runs?head_sha={head}&per_page=100';jobs=f'repos/{repo}/actions/runs/7/attempts/1/jobs?per_page=100';checks=f'repos/{repo}/commits/{head}/check-runs?per_page=100'
        mutations=[lambda r:r[f'repos/{repo}'].update(id=42),lambda r:r[f'repos/{repo}/actions/workflows/prearm-source-controls.yml'].update(id=42),lambda r:r[f'repos/{repo}/actions/workflows/prearm-source-controls.yml'].update(state='disabled'),lambda r:r[runs].update(total_count=0,workflow_runs=[]),lambda r:r[runs].update(total_count=2),lambda r:r[jobs].update(total_count=0,jobs=[]),lambda r:r[checks].update(total_count=0,check_runs=[])]
        for field,value in (('head_sha','f'*40),('head_branch','foreign'),('event','workflow_dispatch'),('workflow_id',42),('run_attempt',2),('run_attempt',True),('conclusion','skipped'),('status','in_progress')):
            mutations.append(lambda r,f=field,v=value:r[runs]['workflow_runs'][0].update({f:v}))
        for field,value in (('head_sha','f'*40),('run_id',42),('run_attempt',2),('name','trivial'),('steps',[]),('conclusion','skipped')):
            mutations.append(lambda r,f=field,v=value:r[jobs]['jobs'][0].update({f:v}))
        mutations.extend([lambda r:r[jobs]['jobs'][0]['steps'][8].update(conclusion='skipped'),lambda r:r[jobs]['jobs'][0]['steps'][2].update(name='changed binding'),lambda r:r[jobs]['jobs'][0]['steps'][-1].update(name='foreign setup'),lambda r:r[jobs]['jobs'][0]['steps'][1].update(number=True),lambda r:r[checks]['check_runs'][0].update(app=dict(id=42)),lambda r:r[checks]['check_runs'][0].update(check_suite=dict(id=42)),lambda r:r[f'repos/{repo}/actions/runs/7'].update(head_branch='foreign')])
        for mutation in mutations:
            code,_,api,_=self.bar_transport(apply=True,change=lambda r,p,pr:mutation(r));self.assertEqual((code,api.attempts),(2,0))
        code,_,api,_=self.bar_transport(apply=True,selector='renamed.yml');self.assertEqual((code,api.attempts),(2,0))

    def test_bar_fresh_policy_and_prior_uncertain_result_never_renew_merge(self):
        rules=f'repos/{MODULE.BAR_REPOSITORY}/rules/branches/main?per_page=100'
        code,_,api,_=self.bar_transport(apply=True,drift=lambda r,p,pr:p.update({rules:(0,200,[dict(type='required_status_checks')])}));self.assertEqual((code,api.attempts),(2,0))
        code,ready,_,_=self.bar_transport()
        prior=asdict(ready);prior.update(outcome='indeterminate',codeDelivery='unknown',attempts=1,producerInstallation='indeterminate',routineEligibility='unresolved')
        code,result,api,reads=self.bar_transport(apply=True,prior=prior)
        self.assertEqual((code,api.attempts,result.attempts),(3,0,1));self.assertIn('readback-only',result.reason)
        self.assertFalse(reads)

    def test_bar_nonempty_real_required_checks_keep_native_predicate_and_prearm_cohort(self):
        row=dict(name='prearm-source-controls',bucket='pass',state='SUCCESS',completedAt='2026-10-13T00:00:00Z',event='pull_request',workflow='prearm-source-controls',startedAt='2026-10-13T00:00:00Z',link=f'https://github.com/{MODULE.BAR_REPOSITORY}/actions/runs/7/job/8')
        code,result,api,reads=self.bar_transport(required=(0,json.dumps([row]).encode()))
        self.assertEqual((code,api.attempts,result.reason),(0,0,None));self.assertFalse(any('--include' in c for c in reads))
        row['bucket']='fail';row['state']='FAILURE'
        code,_,api,_=self.bar_transport(apply=True,required=(0,json.dumps([row]).encode()));self.assertEqual((code,api.attempts),(2,0))

    def test_fsquint_authenticated_empty_policy_full_transport_ready_and_cas(self):
        for apply in (False,True):
            code,result,api,reads = self.fsquint_transport(apply=apply)
            self.assertEqual((code,api.attempts,result.producerInstallation,result.routineEligibility),
                (0,int(apply),'delivered-activation-pending' if apply else 'ready','unavailable-first-install'))
            self.assertEqual(sum(c[-1].endswith('required_status_checks') for c in reads),2 if apply else 1)
            self.assertEqual(result.reason,'required-policy-observed-empty')

    def test_fsquint_nonempty_required_checks_keep_original_path(self):
        row=dict(name="qualify",bucket="pass",state="SUCCESS",completedAt="2026-10-13T00:00:00Z",
                 event="pull_request",workflow="ci",startedAt="2026-10-13T00:00:00Z",
                 link="https://github.com/FS-GG/FsQuint/actions/runs/7/job/8")
        code,result,api,reads=self.fsquint_transport(required=(0,json.dumps([row]).encode()))
        self.assertEqual((code,api.attempts,result.reason),(0,0,None))
        self.assertFalse(any('--include' in command for command in reads))
        for required in ((0,b'[]'),(1,b'{}'),(1,b'[]'),(1,json.dumps([row]).encode()),(4,b'')):
            code,_,api,_=self.fsquint_transport(apply=True,required=required)
            self.assertEqual((code,api.attempts),(2,0))

    def test_fsquint_authentic_no_required_diagnostic_transport_is_exact(self):
        diagnostic=b"no required checks reported on the 'routine/install-routine-producer-90ccedb7' branch\n"
        # Retained authenticated FsQuint PR44 gh2.102.0 transport: exit1/stdout0/stderr86.
        self.assertEqual(len(diagnostic),86)
        for value in [(1,b"",b""),(1,b"",b"permission denied\n"),(1,b"",diagnostic+b"extra\n"),
                      (1,b"",diagnostic.rstrip()),(1,b"",diagnostic.replace(b"90ccedb7",b"foreign")),
                      (1,b"[]",diagnostic),(1,b" ",diagnostic),(4,b"",diagnostic),(0,b"",diagnostic)]:
            with self.subTest(value=value):
                code,_,api,_=self.fsquint_transport(apply=True,required=value)
                self.assertEqual((code,api.attempts),(2,0))

    def test_bounded_native_query_optional_stderr_preserves_default_and_combined_cap(self):
        query=BoundedQueryTests().query
        result,process,_=query(chunks=[b'out'],stderr_chunks=[b'err'])
        self.assertEqual(result,(0,b'out'))
        stderr=bytearray()
        result,process,_=query(chunks=[b'out'],stderr_chunks=[b'err'],stderr_output=stderr)
        self.assertEqual(result,(0,b'out'));self.assertEqual(stderr,b'err')
        self.assertGreaterEqual(process.wait.call_count,1)
        process.stdout.close.assert_called_once();process.stderr.close.assert_called_once()
        result,process,_=query(chunks=[b'out'],stderr_chunks=[b'err'],max_bytes=5,stderr_output=bytearray())
        self.assertIsInstance(result,RuntimeError);self.assertIn('combined stdout/stderr',str(result))
        for options in [dict(never=True),dict(reaping_unknown=True),dict(interrupt=True)]:
            result,process,_=query(stderr_output=bytearray(),**options)
            self.assertIsInstance(result,BaseException)
            process.stdout.close.assert_called_once();process.stderr.close.assert_called_once()

    def test_fsquint_policy_http_transport_refuses_pagination_and_ambiguity(self):
        api=MODULE.GhApi()
        cases=[(0,b'HTTP/2.0 200 OK\r\nLink: <next>; rel="next"\r\n\r\n[]'),
               (0,b'HTTP/2.0 200 OK\r\n\r\n[]extra'),(1,b'HTTP/2.0 401 Denied\r\n\r\n[]'),
               (0,b'HTTP/2.0 200 OK\r\n\r\n'),(0,b'unknown\n\n[]'),
               (0,b'HTTP/2.0 200 OK\r\n\r\n{"x":1,"x":2}')]
        for value in cases:
            with self.subTest(value=value), mock.patch.object(MODULE,'bounded_watch_query',return_value=value), self.assertRaises(RuntimeError):
                api.installation_policy_read('repos/FS-GG/FsQuint/rulesets')
        with mock.patch.object(MODULE,'bounded_watch_query',side_effect=RuntimeError('native query combined stdout/stderr exceeded byte bound')),self.assertRaises(RuntimeError):
            api.installation_policy_read('repos/FS-GG/FsQuint/rulesets')

    def test_fsquint_empty_policy_full_transport_policy_refusals(self):
        repo='FS-GG/FsQuint'
        classic=f'repos/{repo}/branches/main/protection/required_status_checks'
        rules=f'repos/{repo}/rules/branches/main?per_page=100'
        sets=f'repos/{repo}/rulesets?includes_parents=true&per_page=100'
        cases=[(classic,(1,404,dict(message='Not Found',status='404'))),
               (classic,(1,403,dict(message='denied'))),(classic,(0,200,dict(contexts=['new']))),
               (classic,(0,404,dict(message='Branch not protected'))),
               (classic,(1,404,'{"message":"Branch not protected","message":"Branch not protected"}')),
               (rules,(0,200,[dict(type='required_status_checks')])),(sets,(0,200,[dict(id=1)])),
               (rules,(1,404,[])),(sets,(0,200,{})),(rules,(0,200,'['))]
        for endpoint,value in cases:
            with self.subTest(endpoint=endpoint,value=value):
                code,result,api,_=self.fsquint_transport(apply=True,change=lambda r,p,pr:p.update({endpoint:value}))
                self.assertEqual((code,api.attempts,result.codeDelivery),(2,0,'not-delivered'))

    def test_fsquint_empty_policy_full_transport_native_refusals(self):
        repo='FS-GG/FsQuint';head=self.fixture['head']
        checks=f'repos/{repo}/commits/{head}/check-runs?per_page=100'
        runs=f'repos/{repo}/actions/workflows/361154226/runs?head_sha={head}&per_page=100'
        jobs=f'repos/{repo}/actions/runs/7/attempts/1/jobs?per_page=100'
        mutations=[
            lambda r:r[f'repos/{repo}'].update(id=42),
            lambda r:r[f'repos/{repo}'].update(permissions=dict(push=True,pull=True)),
            lambda r:r[f'repos/{repo}'].update(default_branch='other'),
            lambda r:r[f'repos/{repo}/git/ref/heads/main'].update(ref='refs/heads/other'),
            lambda r:r[f'repos/{repo}/git/ref/heads/main']['object'].update(type='tag'),
            lambda r:r[f'repos/{repo}/git/ref/heads/main']['object'].update(sha='f'*40),
            lambda r:r[f'repos/{repo}/actions/workflows/ci.yml'].update(id=42),
            lambda r:r[f'repos/{repo}/actions/workflows/ci.yml'].update(state='disabled_manually'),
            lambda r:r[f'repos/{repo}/actions/workflows/ci.yml'].update(path='.github/workflows/trivial.yml'),
            lambda r:r[runs].update(total_count=2), lambda r:r[runs].update(workflow_runs=[]),
            lambda r:r[jobs].update(total_count=0,jobs=[]),lambda r:r[jobs].update(total_count=True),
            lambda r:r[checks].update(total_count=0,check_runs=[]),
            lambda r:r[checks].update(total_count=2),
            lambda r:r[f'repos/{repo}/commits/{head}/status?per_page=100'].update(total_count=1,statuses=[dict(state='success')]),
        ]
        for field,value in [('head_sha','f'*40),('workflow_id',42),('event','push'),('head_branch','other'),('run_attempt',2),('run_attempt',True),('conclusion','failure'),('conclusion','skipped'),('status','in_progress'),('check_suite_id',42),('repository',dict(id=42,full_name=repo))]:
            mutations.append(lambda r,f=field,v=value:r[runs]['workflow_runs'][0].update({f:v}))
        for field,value in [('app',dict(id=42)),('check_suite',dict(id=42)),('head_sha','f'*40),('conclusion','skipped'),('conclusion','failure'),('details_url','https://evil.invalid/check')]:
            mutations.append(lambda r,f=field,v=value:r[checks]['check_runs'][0].update({f:v}))
        for field,value in [('run_id',42),('run_attempt',2),('head_sha','f'*40),('conclusion','failure'),('conclusion','skipped'),('status','in_progress'),('name','trivial'),('check_run_url','foreign'),('steps',[])]:
            mutations.append(lambda r,f=field,v=value:r[jobs]['jobs'][0].update({f:v}))
        mutations.append(lambda r:r[jobs]['jobs'][0]['steps'][5].update(conclusion='skipped'))
        mutations.append(lambda r:r[f'repos/{repo}/actions/jobs/8'].update(run_id=42))
        for mutation in mutations:
            with self.subTest(mutation=mutation):
                code,_,api,_=self.fsquint_transport(apply=True,change=lambda r,p,pr:mutation(r))
                self.assertEqual((code,api.attempts),(2,0))
        code,_,api,_=self.fsquint_transport(apply=True,selector='trivial.yml')
        self.assertEqual((code,api.attempts),(2,0))

    def test_fsquint_empty_policy_fresh_policy_drift_and_uncertain_readback(self):
        rules='repos/FS-GG/FsQuint/rules/branches/main?per_page=100'
        code,_,api,_=self.fsquint_transport(apply=True,drift=lambda r,p,pr:p.update({rules:(0,200,[dict(type='required_status_checks')])}))
        self.assertEqual((code,api.attempts),(2,0))
        for change in (lambda r,p,pr:r['repos/FS-GG/FsQuint'].update(default_branch='other'),
                       lambda r,p,pr:r['repos/FS-GG/FsQuint/git/ref/heads/main']['object'].update(sha='f'*40),
                       lambda r,p,pr:pr['head'].update(sha='f'*40)):
            code,_,api,_=self.fsquint_transport(apply=True,fresh=change)
            self.assertEqual((code,api.attempts),(2,0))
        code,result,api,_=self.fsquint_transport(apply=True,uncertain=True)
        self.assertEqual((code,api.attempts,result.producerInstallation),(0,1,'delivered-activation-pending'))
        prior=MODULE.asdict(result)
        code,result,api,_=self.fsquint_transport(apply=True,prior=prior,merged=True)
        self.assertEqual((code,api.attempts,result.producerInstallation),(0,0,'delivered-activation-pending'))

    def test_exact_real_git_payload_is_ready_without_fake_eligibility(self):
        api = self.api(); code, result = self.invoke(api)
        self.assertEqual((code, result.outcome, result.producerInstallation, result.routineEligibility, api.attempts),
                         (0, "ready", "ready", "unavailable-first-install", 0))

    def test_merge_uses_existing_cas_and_exact_protected_payload_readback(self):
        api = self.api([self.pr, self.pr, self.after], [{"merged": True, "sha": self.fixture["head"]}])
        original = api.installation_contract
        def transition(*args, **kwargs):
            if kwargs.get("delivered"): api.overrides["repos/FS-GG/FS.GG.Coordination/git/ref/heads/main"] = {"object": {"sha": self.fixture["head"]}}
            return original(*args, **kwargs)
        with mock.patch.object(api, "installation_contract", side_effect=transition): code, result = self.invoke(api, True)
        self.assertEqual((code, api.attempts, result.producerInstallation, result.routineEligibility), (0, 1, "delivered-activation-pending", "unavailable-first-install"))

    def test_stale_protected_ancestor_policy_refuses_but_identical_closure_ancestor_passes(self):
        self.assertEqual(self.invoke(self.api(), source=self.fixture["obsolete"])[0], 2)
        self.assertEqual(self.invoke(self.api(), source=self.fixture["source"])[0], 0)

    def registration_api(self):
        pr=copy.deepcopy(self.pr);pr['head']['sha']=self.registered_head;pr['changed_files']=6
        pr['body']=pr['body'].replace(self.fixture['head'],self.registered_head)
        api=self.api([pr]);files=[dict(filename=r['path'],status='modified' if r['path']==MODULE.PRODUCER_REGISTRATION else 'added',sha=r['sha'])
            for r in self.registered_rows if r['path'] in (*MODULE.PRODUCER_PATHS,MODULE.PRODUCER_REGISTRATION)]
        api.overrides['repos/FS-GG/FS.GG.Coordination/pulls/1/files?per_page=100']=files
        roster=copy.deepcopy(self.fixture['responses'][f"repos/FS-GG/FS.GG.Coordination/commits/{self.fixture['head']}/check-runs?per_page=100"])
        roster['check_runs'][0]['head_sha']=self.registered_head
        api.overrides[f"repos/FS-GG/FS.GG.Coordination/commits/{self.registered_head}/check-runs?per_page=100"]=roster
        for endpoint in ('repos/FS-GG/FS.GG.Coordination/actions/runs/7','repos/FS-GG/FS.GG.Coordination/actions/jobs/8'):
            value=copy.deepcopy(self.fixture['responses'][endpoint]);value['head_sha']=self.registered_head;api.overrides[endpoint]=value
        api.fixture=dict(self.fixture,head=self.registered_head)
        api.runs[0]['head_sha']=self.registered_head
        return api,pr

    def registration_contract(self,api,pr,delivered=False):
        with mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
            return api.installation_contract('FS-GG/FS.GG.Coordination',1,pr,self.registered_head,self.fixture['source'],delivered=delivered)

    def templates_api(self):
        repo = 'FS-GG/FS.GG.Templates'
        alias = lambda value: json.loads(json.dumps(value).replace('FS-GG/FS.GG.Coordination', repo).replace('1346720714', '1281961814'))
        fixture = alias(self.fixture); fixture['head'] = self.templates_head
        responses = fixture['responses']
        pr = alias(self.pr); pr['changed_files'] = 6; pr['head']['sha'] = self.templates_head
        pr['body'] = pr['body'].replace(self.fixture['head'], self.templates_head)
        responses[f'repos/{repo}/pulls/1/files?per_page=100'] = [dict(filename=r['path'],status='modified' if r['path']==MODULE.TEMPLATES_ASSERTION_PIN else 'added',sha=r['sha'])
            for r in self.templates_rows if r['path'] in (*MODULE.PRODUCER_PATHS,MODULE.TEMPLATES_ASSERTION_PIN)]
        roster = responses.pop(f"repos/{repo}/commits/{self.fixture['head']}/check-runs?per_page=100")
        roster['check_runs'][0]['head_sha'] = self.templates_head
        responses[f'repos/{repo}/commits/{self.templates_head}/check-runs?per_page=100'] = roster
        for endpoint in (f'repos/{repo}/actions/runs/7', f'repos/{repo}/actions/jobs/8'):
            responses[endpoint]['head_sha'] = self.templates_head
        after = copy.deepcopy(pr); after.update(state='closed',merged=True,merged_at='2026-10-13T00:00:00Z',merge_commit_sha=self.templates_head)
        responses[f'repos/{repo}/compare/{self.templates_head}...{self.templates_head}'] = dict(status='identical',merge_base_commit=dict(sha=self.templates_head))
        api = InstallationApi(fixture,[pr,pr,after]); api.runs[0]['head_sha']=self.templates_head
        api.required = alias(api.required)
        def merge(args, **kwargs):
            api.attempts += 1
            self.assertEqual(args[:3],['--method','PUT',f'repos/{repo}/pulls/1/merge'])
            self.assertEqual(json.loads(kwargs['input_text']),dict(sha=self.templates_head,merge_method='squash'))
            responses[f'repos/{repo}/git/ref/heads/main']['object']['sha']=self.templates_head
            return dict(merged=True,sha=self.templates_head)
        api._run = merge
        return api,pr

    def templates_contract(self,api,pr,delivered=False):
        with mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
            return api.installation_contract('FS-GG/FS.GG.Templates',1,pr,self.templates_head,self.fixture['source'],delivered=delivered)

    def templates_blob(self,api,treeid,path,body):
        repo='FS-GG/FS.GG.Templates'; endpoint=f'repos/{repo}/git/trees/{treeid}?recursive=1'
        tree=copy.deepcopy(api.overrides.get(endpoint,api.fixture['responses'][endpoint]))
        row=next(r for r in tree['tree'] if r['path']==path)
        sha=MODULE.hashlib.sha1(b'blob '+str(len(body)).encode()+b'\0'+body).hexdigest(); row['sha']=sha
        api.overrides[endpoint]=tree
        api.overrides[f'repos/{repo}/git/blobs/{sha}']=dict(sha=sha,encoding='base64',size=len(body),content=MODULE.base64.b64encode(body).decode())

    def test_templates_pin_refresh_preserves_bytes_and_single_cas_protected_readback(self):
        api,pr=self.templates_api(); self.templates_contract(api,pr)
        with mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
            code,result=MODULE.summarize(api,repo='FS-GG/FS.GG.Templates',pr_number=1,expected_head=self.templates_head,
                merge_method='squash',publication_required=False,apply=True,coherent_workflow='composition.yml',producer_source=self.fixture['source'])
        self.assertEqual((code,api.attempts,result.producerInstallation,result.routineEligibility),(0,1,'delivered-activation-pending','unavailable-first-install'))

    def test_templates_pin_wrong_receiver_mode_population_and_status_refuse(self):
        repo='FS-GG/FS.GG.Templates'; files=f'repos/{repo}/pulls/1/files?per_page=100'
        for change in ('id','name','mode','symlink','absent','extra','count','status','rename','sha'):
            with self.subTest(change=change):
                api,pr=self.templates_api(); r=api.fixture['responses']
                endpoint=f'repos/{repo}/git/trees/{self.templates_tree}?recursive=1'
                row=next(x for x in r[endpoint]['tree'] if x['path']==MODULE.TEMPLATES_ASSERTION_PIN)
                pin=next(x for x in r[files] if x['filename']==MODULE.TEMPLATES_ASSERTION_PIN)
                if change=='id': r[f'repos/{repo}']['id']=42; pr['base']['repo']['id']=pr['head']['repo']['id']=42
                elif change=='name': r[f'repos/{repo}']['full_name']='FS-GG/Templates'
                elif change=='mode': row['mode']='100755'
                elif change=='symlink': row['mode']='120000'
                elif change=='absent': r[endpoint]['tree'].remove(row)
                elif change=='extra': next(x for x in r[endpoint]['tree'] if x['path']=='README.md')['sha']='f'*40
                elif change=='count': pr['changed_files']=5
                elif change=='status': pin['status']='added'
                elif change=='rename': pin['previous_filename']='other'
                elif change=='sha': pin['sha']='f'*40
                with self.assertRaises(RuntimeError): self.templates_contract(api,pr)

    def test_templates_pin_bad_original_assignment_or_extra_candidate_byte_refuse(self):
        repo='FS-GG/FS.GG.Templates'; base_tree=self.fixture['responses'][f"repos/{repo.replace('FS.GG.Templates','FS.GG.Coordination')}/git/commits/{self.fixture['base']}"]['tree']['sha']
        pin=self.assertion_old_pin.encode()
        for body in (self.assertion_pin_base.replace(pin,b'main'),self.assertion_pin_base.replace(pin,pin[:-1]),
                     self.assertion_pin_base.replace(b'"',b"'"),self.assertion_pin_base+b'SKILL_ASSERT_REF="'+pin+b'"\n',
                     self.assertion_pin_base.replace(b'SKILL_ASSERT_REF=',b'export SKILL_ASSERT_REF='),
                     self.assertion_pin_base.replace(b'SKILL_ASSERT_REF=',b' SKILL_ASSERT_REF=')):
            api,pr=self.templates_api();self.templates_blob(api,base_tree,MODULE.TEMPLATES_ASSERTION_PIN,body)
            with self.assertRaises(RuntimeError):self.templates_contract(api,pr)
        derived=self.assertion_pin_base.replace(pin,self.fixture['source'].encode())
        for body in (derived+b'\n',derived.replace(b'14d',b'30d'),derived.replace(self.fixture['source'].encode(),b'f'*40),derived.replace(b'header',b'changed')):
            api,pr=self.templates_api();self.templates_blob(api,self.templates_tree,MODULE.TEMPLATES_ASSERTION_PIN,body)
            with self.assertRaises(RuntimeError):self.templates_contract(api,pr)

    def test_templates_pin_old_ancestry_commit_tree_bundle_identity_refuse(self):
        old=self.assertion_old_pin; source=self.fixture['source']; prefix='repos/FS-GG/.github'
        for change in ('lineage','base','mergebase','commit','tree','truncated','bundle','mode','forgedblob'):
            with self.subTest(change=change):
                api,pr=self.templates_api();r=api.fixture['responses']; lineage=r[f'{prefix}/compare/{old}...{source}?per_page=1&page=2']
                root=r[f"{prefix}/git/trees/{r[f'{prefix}/git/commits/{old}']['tree']['sha']}"]
                dist=next(x['sha'] for x in root['tree'] if x['path']=='dist')
                tree=r[f"{prefix}/git/trees/{dist}"]
                row=next(x for x in tree['tree'] if x['path']=='skill-union-assert.sh')
                if change=='lineage':lineage['status']='diverged'
                elif change=='base':lineage['base_commit']['sha']='f'*40
                elif change=='mergebase':lineage['merge_base_commit']['sha']='f'*40
                elif change=='commit':r[f'{prefix}/git/commits/{old}']['sha']='f'*40
                elif change=='tree':tree['sha']='f'*40
                elif change=='truncated':tree['truncated']=True
                elif change=='bundle':row['sha']='f'*40
                elif change=='mode':
                    # A different old mode has a different immutable directory identity;
                    # do not mutate the content-addressed node shared with the source.
                    altered=copy.deepcopy(tree);altered['sha']='e'*40
                    next(x for x in altered['tree'] if x['path']=='skill-union-assert.sh')['mode']='100755'
                    next(x for x in root['tree'] if x['path']=='dist')['sha']='e'*40
                    r[f"{prefix}/git/trees/"+('e'*40)]=altered
                elif change=='forgedblob':r[f"{prefix}/git/blobs/{row['sha']}"]['content']=MODULE.base64.b64encode(b'forged').decode()
                with self.assertRaises(RuntimeError):self.templates_contract(api,pr)

    def test_templates_pin_fresh_cas_and_delivered_sixth_readback_refuse_drift(self):
        api,pr=self.templates_api(); original=api.installation_contract
        count=0
        def contract(*args,**kwargs):
            nonlocal count
            count+=1
            if count==2:
                self.templates_blob(api,self.templates_tree,MODULE.TEMPLATES_ASSERTION_PIN,self.assertion_pin_base)
            return original(*args,**kwargs)
        with mock.patch.object(api,'installation_contract',side_effect=contract),mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
            code,result=MODULE.summarize(api,repo='FS-GG/FS.GG.Templates',pr_number=1,expected_head=self.templates_head,merge_method='squash',publication_required=False,apply=True,coherent_workflow='composition.yml',producer_source=self.fixture['source'])
        self.assertEqual((code,api.attempts),(2,0))
        api,pr=self.templates_api();pr.update(merged=True,merge_commit_sha=self.templates_head)
        api.fixture['responses']['repos/FS-GG/FS.GG.Templates/git/ref/heads/main']['object']['sha']=self.successor
        with self.assertRaises(RuntimeError):self.templates_contract(api,pr,delivered=True)

    def test_templates_pin_bounded_ancestry_metadata_and_transport_refusal(self):
        endpoint=f"repos/FS-GG/.github/compare/{self.assertion_old_pin}...{self.fixture['source']}?per_page=1&page=2"
        api,pr=self.templates_api(); reads=[]; original=api.installation_read
        def observe(path):
            reads.append(path)
            return original(path)
        api.installation_read=observe; self.templates_contract(api,pr)
        self.assertIn(endpoint,reads)
        self.assertNotIn(endpoint.split('?')[0],reads)
        for refusal in (RuntimeError('native query combined stdout/stderr exceeded byte bound'),
                        RuntimeError('native query direct-child retirement is unknown'),
                        dict(status='ahead',base_commit=dict(sha=self.assertion_old_pin)),
                        dict(status='ahead',base_commit=dict(sha=self.assertion_old_pin),merge_base_commit=dict(sha='f'*40))):
            api,pr=self.templates_api();api.overrides[endpoint]=refusal
            with self.assertRaises(RuntimeError):self.templates_contract(api,pr)
            self.assertEqual(api.attempts,0)

    def test_derived_registration_preserves_original_bytes_and_native_file_boundary(self):
        api,pr=self.registration_api();self.registration_contract(api,pr);self.assertEqual(api.attempts,0)
        baseline=self.registration_base
        derived=MODULE.installation_registration(baseline,(self.owner/MODULE.PRODUCER_PATHS[1]).read_bytes())
        record=dict(kind='workflow',path=MODULE.PRODUCER_PATHS[1],sha256=MODULE.hashlib.sha256((self.owner/MODULE.PRODUCER_PATHS[1]).read_bytes()).hexdigest(),externalReferences=[],localReferences=[])
        closing=baseline.rindex(b']')
        self.assertEqual(derived,baseline[:closing]+b',\n'+json.dumps(record,indent=2).encode()+b'\n'+baseline[closing:])
        self.assertEqual(MODULE.hashlib.sha256(baseline).hexdigest(),'9ff2bfe47d1a881cd67e70577b876089af3763bd09f4a9d7224dd6a84d517687')
        for field,value in (('changed_files',5),('changed_files',7)):
            bad=copy.deepcopy(pr);bad[field]=value
            with self.assertRaises(RuntimeError):self.registration_contract(self.registration_api()[0],bad)
        endpoint='repos/FS-GG/FS.GG.Coordination/pulls/1/files?per_page=100'
        for status in ('added','removed','renamed'):
            api,pr=self.registration_api();rows=copy.deepcopy(api.overrides[endpoint]);next(r for r in rows if r['filename']==MODULE.PRODUCER_REGISTRATION)['status']=status;api.overrides[endpoint]=rows
            with self.assertRaises(RuntimeError):self.registration_contract(api,pr)
        for kind,mode in (('blob','100755'),('blob','120000'),('commit','160000')):
            api,pr=self.registration_api();endpoint=f"repos/FS-GG/FS.GG.Coordination/git/trees/{self.registered_tree}?recursive=1";tree=copy.deepcopy(self.fixture['responses'][endpoint]);row=next(r for r in tree['tree'] if r['path']==MODULE.PRODUCER_REGISTRATION);row.update(type=kind,mode=mode);api.overrides[endpoint]=tree
            with self.assertRaises(RuntimeError):self.registration_contract(api,pr)

    def test_six_file_merge_and_protected_registration_readback_remain_guarded(self):
        for corrupt in (False,True):
            api,pr=self.registration_api();after=copy.deepcopy(pr);after.update(state='closed',merged=True,merged_at='2026-10-13T00:00:00Z',merge_commit_sha=self.registered_head)
            api.reads=iter([pr,pr,after]);api.writes=iter([dict(merged=True,sha=self.registered_head)])
            original=api.installation_contract
            def transition(*args,**kwargs):
                if kwargs.get('delivered'):
                    api.overrides['repos/FS-GG/FS.GG.Coordination/git/ref/heads/main']={'object':{'sha':self.registered_head}}
                    api.overrides[f'repos/FS-GG/FS.GG.Coordination/compare/{self.registered_head}...{self.registered_head}']=dict(status='identical',merge_base_commit={'sha':self.registered_head})
                    if corrupt:
                        # Actual successor retains all core5 bytes but restores the old registration.
                        api.overrides['repos/FS-GG/FS.GG.Coordination/git/ref/heads/main']={'object':{'sha':self.successor}}
                return original(*args,**kwargs)
            with mock.patch.object(api,'installation_contract',side_effect=transition),mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
                code,result=MODULE.summarize(api,repo='FS-GG/FS.GG.Coordination',pr_number=1,expected_head=self.registered_head,merge_method='squash',publication_required=False,apply=True,coherent_workflow='CI.yml',producer_source=self.fixture['source'])
            self.assertEqual((code,api.attempts,result.producerInstallation,result.codeDelivery),(4 if corrupt else 0,1,'disputed' if corrupt else 'delivered-activation-pending','delivered'))

    def test_registration_base_creation_deletion_and_wrong_native_receiver_refuse(self):
        base_tree=self.fixture['responses'][f"repos/FS-GG/FS.GG.Coordination/git/commits/{self.fixture['base']}"]['tree']['sha']
        for area,mode in (('base','absent'),('base','100755'),('candidate','absent')):
            api,pr=self.registration_api();treeid=base_tree if area=='base' else self.registered_tree;endpoint=f'repos/FS-GG/FS.GG.Coordination/git/trees/{treeid}?recursive=1';tree=copy.deepcopy(self.fixture['responses'][endpoint])
            if mode=='absent':tree['tree']=[r for r in tree['tree'] if r['path']!=MODULE.PRODUCER_REGISTRATION]
            else:next(r for r in tree['tree'] if r['path']==MODULE.PRODUCER_REGISTRATION)['mode']=mode
            api.overrides[endpoint]=tree
            with self.assertRaises(RuntimeError):self.registration_contract(api,pr)
            self.assertEqual(api.attempts,0)
        api,pr=self.registration_api();api.overrides['repos/FS-GG/FS.GG.Coordination']=dict(id=42,full_name='FS-GG/FS.GG.Coordination',default_branch='main')
        pr['base']['repo']['id']=pr['head']['repo']['id']=42
        with self.assertRaises(RuntimeError):self.registration_contract(api,pr)
        self.assertEqual(api.attempts,0)

    def test_other_native_receiver_still_accepts_exact_five_files(self):
        api=self.api();pr=copy.deepcopy(self.pr);other='FS-GG/OtherReceiver';pr['base']['repo']=pr['head']['repo']=dict(id=42,full_name=other)
        original=api.installation_read
        def aliases(endpoint):
            if endpoint=='repos/'+other:return dict(id=42,full_name=other,default_branch='main')
            value=original(endpoint.replace('repos/'+other,'repos/FS-GG/FS.GG.Coordination'))
            if isinstance(value,dict):
                value=copy.deepcopy(value)
                if 'check_runs' in value:
                    for row in value['check_runs']:row['details_url']=row['details_url'].replace('FS-GG/FS.GG.Coordination',other)
                if 'repository' in value:value['repository']['full_name']=other
                if 'check_run_url' in value:value['check_run_url']=value['check_run_url'].replace('FS-GG/FS.GG.Coordination',other)
            return value
        api.required[0]['link']=api.required[0]['link'].replace('FS-GG/FS.GG.Coordination',other)
        with mock.patch.object(api,'installation_read',side_effect=aliases),mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)):
            api.installation_contract(other,1,pr,self.fixture['head'],self.fixture['source'])
        self.assertEqual(api.attempts,0)

    def test_shortened_receiver_name_refuses_even_with_authentic_repository_id(self):
        api,pr=self.registration_api();short='FS-GG/Coordination'
        pr['base']['repo']['full_name']=pr['head']['repo']['full_name']=short
        original=api.installation_read
        def aliases(endpoint):
            if endpoint=='repos/'+short:return dict(id=1346720714,full_name=short,default_branch='main')
            return original(endpoint.replace('repos/'+short,'repos/FS-GG/FS.GG.Coordination'))
        with mock.patch.object(api,'installation_read',side_effect=aliases),self.assertRaisesRegex(RuntimeError,'belongs only'):
            api.installation_contract(short,1,pr,self.registered_head,self.fixture['source'])
        self.assertEqual(api.attempts,0)

    def test_registration_schema_duplicate_existing_reference_and_content_controls(self):
        workflow=(self.owner/MODULE.PRODUCER_PATHS[1]).read_bytes();baseline=json.loads(self.registration_base)
        cases=[b'{',self.registration_base.replace(b'"complete": true',b'"complete": true,"complete": true')]
        for edit in (lambda x:x.update(complete=False),lambda x:x.update(extra=1),lambda x:x['documents'].append(copy.deepcopy(x['documents'][0])),
                     lambda x:x['documents'][0].update(path=MODULE.PRODUCER_PATHS[1]),lambda x:x['documents'][0].update(sha256='wrong'),
                     lambda x:x['documents'][0].update(extra=True)):
            v=copy.deepcopy(baseline);edit(v);cases.append(json.dumps(v).encode())
        for raw in cases:
            with self.subTest(raw=raw[:50]),self.assertRaises(RuntimeError):MODULE.installation_registration(raw,workflow)
        with self.assertRaises(RuntimeError):MODULE.installation_registration(self.registration_base,workflow+b'\n - uses: other/action@main\n')
        api,pr=self.registration_api();endpoint=f"repos/FS-GG/FS.GG.Coordination/git/trees/{self.registered_tree}?recursive=1";tree=copy.deepcopy(self.fixture['responses'][endpoint]);row=next(r for r in tree['tree'] if r['path']==MODULE.PRODUCER_REGISTRATION)
        original=MODULE.installation_registration(self.registration_base,workflow)
        mutations=[]
        document=json.loads(original)
        for edit in (lambda x:x['documents'][-1].update(sha256='f'*64),lambda x:x['documents'][-1].update(externalReferences=['other/action@main']),
                     lambda x:x['documents'][-1].update(localReferences=['./local']),lambda x:x['documents'].reverse(),lambda x:x['documents'].pop(0)):
            v=copy.deepcopy(document);edit(v);mutations.append(json.dumps(v).encode())
        mutations.append(original.replace(b'"complete": true',b'"complete": true,"complete": true'))
        for body in mutations:
            api,pr=self.registration_api();tree=copy.deepcopy(self.fixture['responses'][endpoint]);row=next(r for r in tree['tree'] if r['path']==MODULE.PRODUCER_REGISTRATION);sha=MODULE.hashlib.sha1(b'blob '+str(len(body)).encode()+b'\0'+body).hexdigest();row['sha']=sha
            api.overrides[endpoint]=tree;api.overrides[f'repos/FS-GG/FS.GG.Coordination/git/blobs/{sha}']=dict(sha=sha,encoding='base64',size=len(body),content=MODULE.base64.b64encode(body).decode())
            with self.assertRaises(RuntimeError):self.registration_contract(api,pr)

    def test_explicit_protected_ref_and_real_compare_schema_fail_closed(self):
        self.assertEqual(self.invoke(self.api())[0],0) # Authentic compare shape has no head_commit.
        endpoint='repos/FS-GG/.github/git/ref/heads/main'
        for value in ({},dict(ref='refs/heads/other',object=dict(type='commit',sha=self.fixture['owner_head'])),
                      dict(ref='refs/heads/main',object=dict(type='tag',sha=self.fixture['owner_head'])),
                      dict(ref='refs/heads/main',object=dict(type='commit',sha='wrong'))):
            api=self.api();api.overrides[endpoint]=value;self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))
        endpoint=f"repos/FS-GG/.github/compare/{self.fixture['source']}...{self.fixture['owner_head']}"
        for field in ('base_commit','merge_base_commit'):
            api=self.api();v=copy.deepcopy(self.fixture['responses'][endpoint]);v[field]={'sha':'f'*40};api.overrides[endpoint]=v
            self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))

    def test_protected_owner_ref_movement_during_contract_never_writes(self):
        api=self.api();original=api.installation_read;reads=0
        def moved(endpoint):
            nonlocal reads
            if endpoint=='repos/FS-GG/.github/git/ref/heads/main':
                reads+=1
                if reads>1:return dict(ref='refs/heads/main',object=dict(type='commit',sha='f'*40))
            return original(endpoint)
        with mock.patch.object(api,'installation_read',side_effect=moved):code,result=self.invoke(api,True)
        self.assertEqual((code,api.attempts),(2,0))

    def test_native_absence_incomplete_denied_disabled_and_existing_check_refuse(self):
        cases = [
            ("repos/FS-GG/FS.GG.Coordination/actions/workflows?per_page=100", RuntimeError("denied")),
            ("repos/FS-GG/FS.GG.Coordination/actions/workflows?per_page=100", dict(total_count=1, workflows=[])),
            ("repos/FS-GG/FS.GG.Coordination/actions/workflows?per_page=100", dict(total_count=1, workflows=[dict(path=MODULE.PRODUCER_PATHS[1], state="disabled_manually")])),
            (f"repos/FS-GG/FS.GG.Coordination/commits/{self.fixture['head']}/check-runs?per_page=100", dict(total_count=1, check_runs=[dict(name="routine-eligibility", conclusion="failure")]))]
        for endpoint, value in cases:
            with self.subTest(endpoint=endpoint, value=value):
                api=self.api(); api.overrides[endpoint]=value
                self.assertEqual((self.invoke(api)[0], api.attempts), (2, 0))

    def test_incomplete_file_list_status_and_payload_mutations_refuse(self):
        endpoint="repos/FS-GG/FS.GG.Coordination/pulls/1/files?per_page=100"
        for status in ("modified", "renamed", "removed"):
            api=self.api(); rows=copy.deepcopy(self.fixture["responses"][endpoint]); rows[0]["status"]=status;api.overrides[endpoint]=rows
            self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.overrides[endpoint]=self.fixture["responses"][endpoint][:-1]
        self.assertEqual(self.invoke(api)[0],2)
        endpoint=f"repos/FS-GG/FS.GG.Coordination/git/trees/{self.fixture['tree']}?recursive=1"
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
        api=self.api();api.overrides['repos/FS-GG/FS.GG.Coordination']=dict(id=1346720714,full_name='Redirect/Receiver',default_branch='main');self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.overrides[f"repos/FS-GG/.github/compare/{self.fixture['source']}...{self.fixture['owner_head']}"]={};self.assertEqual(self.invoke(api)[0],2)

    def test_existing_partial_installation_and_blob_corruption_refuse(self):
        endpoint=f"repos/FS-GG/FS.GG.Coordination/git/commits/{self.fixture['base']}"
        api=self.api();value=copy.deepcopy(self.fixture['responses'][endpoint]);value['tree']['sha']=self.fixture['tree'];api.overrides[endpoint]=value
        self.assertEqual(self.invoke(api)[0],2)
        row=next(r for r in self.fixture['responses'][f"repos/FS-GG/FS.GG.Coordination/git/trees/{self.fixture['tree']}?recursive=1"]['tree'] if r['path']==MODULE.PRODUCER_PATHS[0])
        endpoint=f"repos/FS-GG/.github/git/blobs/{row['sha']}"
        api=self.api();value=copy.deepcopy(self.fixture['responses'][endpoint]);value['content']=MODULE.base64.b64encode(b'wrong').decode();api.overrides[endpoint]=value
        self.assertEqual(self.invoke(api)[0],2)

    def test_selected_producer_paths_share_ancestors_without_unrelated_history(self):
        api=self.api();owner='FS-GG/.github';revision=self.fixture['owner_head']
        root_sha=self.fixture['responses'][f'repos/{owner}/git/commits/{revision}']['tree']['sha']
        root_endpoint=f'repos/{owner}/git/trees/{root_sha}'
        root=copy.deepcopy(self.fixture['responses'][root_endpoint])
        root['tree'].append(dict(path='unrelated-history',type='tree',mode='040000',sha='f'*40))
        api.overrides[root_endpoint]=root
        api.overrides[f'repos/{owner}/git/trees/'+('f'*40)]=RuntimeError('unrelated history must not be read')
        api.overrides[f'repos/{owner}/git/trees/{root_sha}?recursive=1']=RuntimeError('recursive owner read forbidden')
        paths=(*MODULE.PRODUCER_PATHS,'tools/routine-delivery.py','src/FS.GG.Drivers/workspace-inventory.json')
        with mock.patch.object(api,'installation_read',wraps=api.installation_read) as reads:
            selected=api.installation_paths(owner,revision,paths)
        self.assertEqual(set(selected),set(paths))
        endpoints=[call.args[0] for call in reads.call_args_list]
        self.assertEqual(len(endpoints),len(set(endpoints)))
        self.assertLessEqual(len(endpoints),1+sum(len(path.split('/')) for path in paths))
        self.assertFalse(any('recursive=' in endpoint for endpoint in endpoints))

    def test_selected_producer_directories_refuse_incomplete_or_unsafe_identity(self):
        owner='FS-GG/.github';revision=self.fixture['owner_head']
        commit_endpoint=f'repos/{owner}/git/commits/{revision}'
        root_sha=self.fixture['responses'][commit_endpoint]['tree']['sha']
        endpoint=f'repos/{owner}/git/trees/{root_sha}'
        original=self.fixture['responses'][endpoint]
        mutations=[]
        for key,value in [('sha','e'*40),('truncated',True),('tree',None)]:
            tree=copy.deepcopy(original);tree[key]=value;mutations.append(tree)
        tree=copy.deepcopy(original);tree['tree'].append(copy.deepcopy(tree['tree'][0]));mutations.append(tree)
        for key,value in [('mode','120000'),('type','blob'),('sha','bad'),('path','scripts/escape')]:
            tree=copy.deepcopy(original);row=next(r for r in tree['tree'] if r['path']=='scripts');row[key]=value;mutations.append(tree)
        tree=copy.deepcopy(original);tree['tree']=[r for r in tree['tree'] if r['path']!='scripts'];mutations.append(tree)
        tree=copy.deepcopy(original);tree['tree']=[dict(path=f'p{i}',mode='100644',type='blob',sha='a'*40) for i in range(10001)];mutations.append(tree)
        for tree in mutations:
            api=self.api();api.overrides[endpoint]=tree
            with self.assertRaises(RuntimeError):api.installation_paths(owner,revision,MODULE.PRODUCER_PATHS)
        api=self.api();api.overrides[endpoint]=RuntimeError('native read denied or exceeded unchanged byte bound')
        with self.assertRaises(RuntimeError):api.installation_paths(owner,revision,MODULE.PRODUCER_PATHS)
        api=self.api();wrong=copy.deepcopy(self.fixture['responses'][commit_endpoint]);wrong['sha']='e'*40;api.overrides[commit_endpoint]=wrong
        with self.assertRaises(RuntimeError):api.installation_paths(owner,revision,MODULE.PRODUCER_PATHS)

    def test_self_correspondence_is_exact_and_other_blob_bound_is_unchanged(self):
        owner='FS-GG/.github'
        owner_tree=self.fixture['responses'][f"repos/{owner}/git/commits/{self.fixture['owner_head']}"]['tree']['sha']
        root=self.fixture['responses'][f'repos/{owner}/git/trees/{owner_tree}']
        tools=next(row['sha'] for row in root['tree'] if row['path']=='tools')
        endpoint=f'repos/{owner}/git/trees/{tools}'
        for wrong_path in (True,False):
            api=self.api();tree=copy.deepcopy(self.fixture['responses'][endpoint])
            helper=next(row for row in tree['tree'] if row['path']=='routine-delivery.py')
            if wrong_path:helper['path']='not-the-maintained-helper.py'
            else:
                body=b'other real Git blob bytes'
                sha=hashlib.sha1(f'blob {len(body)}\0'.encode()+body).hexdigest();helper['sha']=sha
                api.overrides[f'repos/{owner}/git/blobs/{sha}']=dict(sha=sha,encoding='base64',size=len(body),content=MODULE.base64.b64encode(body).decode())
            api.overrides[endpoint]=tree
            self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))
        api=self.api();row=dict(type='blob',mode='100644',sha='f'*40)
        api.overrides[f'repos/{owner}/git/blobs/'+('f'*40)]=dict(sha='f'*40,encoding='base64',size=131073,content='YQ==')
        with self.assertRaises(RuntimeError):api.installation_blob(owner,row)

    def test_required_and_coherent_checks_must_complete_and_exact_native_provenance(self):
        for field,value in (("bucket","pending"),("state","FAILURE"),("link","https://evil.invalid/check"),("completedAt","")):
            api=self.api();api.required[0][field]=value;self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.required=[];self.assertEqual(self.invoke(api)[0],2)
        for field,value in (("head_sha","f"*40),("run_attempt",2),("conclusion","failure")):
            api=self.api();endpoint='repos/FS-GG/FS.GG.Coordination/actions/jobs/8';job=copy.deepcopy(self.fixture['responses'][endpoint]);job[field]=value;api.overrides[endpoint]=job;self.assertEqual(self.invoke(api)[0],2)
        for status,conclusion in (("in_progress",None),("completed","failure")):
            api=self.api();api.runs[0].update(status=status,conclusion=conclusion);self.assertEqual(self.invoke(api)[0],2)
        api=self.api();api.runs[0].update(status='in_progress',conclusion=None)
        api.qualification_selection=lambda *_: selection('reused')
        self.assertEqual(self.invoke(api)[0],2)
        endpoint=f"repos/FS-GG/FS.GG.Coordination/commits/{self.fixture['head']}/check-runs?per_page=100"
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
        api=self.api([self.after]);api.overrides['repos/FS-GG/FS.GG.Coordination/git/ref/heads/main']={'object':{'sha':self.fixture['head']}}
        code,result=self.invoke(api,True)
        self.assertEqual((code,api.attempts,result.producerInstallation),(0,0,'delivered-activation-pending'))

    def test_retained_uncertain_effect_is_readback_only_across_invocations(self):
        first=self.api([self.pr,self.pr,self.pr],[MODULE.AmbiguousWrite('lost response')])
        code,uncertain=self.invoke(first,True);self.assertEqual(code,3)
        prior=asdict(uncertain)
        later=self.api([self.pr]);later.required[0]['bucket']='pending'
        code,result=self.invoke(later,True,prior=prior)
        self.assertEqual((code,later.attempts,result.codeDelivery,result.attempts),(3,0,'unknown',1))
        second=self.api([self.pr]);code,second_result=self.invoke(second,True,prior=asdict(result))
        self.assertEqual((code,second.attempts,second_result.codeDelivery,second_result.attempts),(3,0,'unknown',1))
        later=self.api([self.after]);later.overrides['repos/FS-GG/FS.GG.Coordination/git/ref/heads/main']={'object':{'sha':self.fixture['head']}}
        code,result=self.invoke(later,True,prior=prior)
        self.assertEqual((code,later.attempts,result.producerInstallation),(0,0,'delivered-activation-pending'))
        self.assertEqual(result.attempts,1)
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

    def test_prior_result_special_files_and_symlinks_refuse_before_read(self):
        fifo=self.directory/'prior.fifo';os.mkfifo(fifo)
        try:
            with self.assertRaises(RuntimeError):MODULE.installation_prior_result(str(fifo))
            with self.assertRaises(RuntimeError):MODULE.installation_prior_result(str(self.directory))
            link=self.directory/'prior.link';link.symlink_to(fifo)
            try:
                with self.assertRaises(RuntimeError):MODULE.installation_prior_result(str(link))
            finally:link.unlink()
        finally:fifo.unlink()

    def test_cli_disputed_readbacks_exit_nonzero_and_failed_prior_retains_custody(self):
        args=['--repo','FS-GG/FS.GG.Coordination','--pr','1','--head',self.fixture['head'],
              '--install-routine-producer','--routine-producer-source',self.fixture['source'],
              '--coherent-workflow','CI.yml','--apply']
        for reads,writes in (([self.after],()),
                             ([self.pr,self.pr,self.after],[{'merged':True,'sha':self.fixture['head']}])):
            api=self.api(reads,writes)
            with mock.patch.object(MODULE,'GhApi',return_value=api), \
                 mock.patch.object(MODULE.WatchGhApi,'_watch_read',side_effect=lambda command,accepted:(0,api.required)), \
                 mock.patch.dict(os.environ,{},clear=True),mock.patch('sys.stdout',new_callable=io.StringIO) as output:
                code=MODULE.main(args)
            result=json.loads(output.getvalue())
            self.assertEqual((code,result['outcome'],result['producerInstallation']),(4,'delivered-disputed','disputed'))
        first=self.api([self.pr,self.pr,self.pr],[MODULE.AmbiguousWrite('lost response')])
        _,uncertain=self.invoke(first,True)
        path=self.directory/'prior-failure.json';path.write_text(json.dumps(asdict(uncertain)))
        api=self.api();api.get_pr=mock.Mock(side_effect=RuntimeError('readback unavailable'))
        with mock.patch.object(MODULE,'GhApi',return_value=api),mock.patch.dict(os.environ,{},clear=True), \
             mock.patch('sys.stdout',new_callable=io.StringIO) as output:
            code=MODULE.main(args+['--installation-prior-result',str(path)])
        result=json.loads(output.getvalue())
        self.assertEqual((code,api.attempts,result['attempts'],result['producerInstallation']),(3,0,1,'indeterminate'))
        later=self.api([self.pr]);code,result=self.invoke(later,True,prior=result)
        self.assertEqual((code,later.attempts,result.attempts),(3,0,1))

    def test_delivered_bootstrap_does_not_unlock_ordinary_missing_eligibility(self):
        api=self.api();api.routine_eligibility=mock.Mock(side_effect=RuntimeError('missing'))
        code,result=MODULE.summarize(api,repo='FS-GG/FS.GG.Coordination',pr_number=1,expected_head=self.fixture['head'],merge_method='squash',publication_required=False,apply=False)
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
        argv=['envelope','--git-dir',str(self.receiver),'--repository','FS-GG/FS.GG.Coordination','--default-branch','main',
            '--base-ref','main','--base-revision',self.fixture['head'],'--base-sha',self.fixture['head'],
            '--head-ref','routine/ordinary','--head-revision',self.successor,'--head-sha',self.successor,
            '--body',str(body_path),'--validator',str(self.owner/'scripts/check-claim-generation.py')]
        def transport(command, **kwargs):
            if command[0]=='git':
                self.assertEqual(command[1:3],['-C',str(self.receiver)])
                try:
                    return subprocess.CompletedProcess(command,0,stdout=fixture_git(self.receiver,*command[3:]).decode(),stderr='')
                except subprocess.CalledProcessError as error:  # the envelope runs git with check=False
                    return subprocess.CompletedProcess(command,error.returncode,stdout=(error.stdout or b'').decode(),stderr=(error.stderr or b'').decode())
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
                    if failure=='workflow':api.overrides['repos/FS-GG/FS.GG.Coordination/actions/workflows?per_page=100']=dict(total_count=1,workflows=[dict(path=MODULE.PRODUCER_PATHS[1])])
                    elif failure=='required':api.required[0]['bucket']='pending'
                    else:api.overrides[f"repos/FS-GG/.github/compare/{self.fixture['source']}...{self.fixture['owner_head']}"]=dict(status='behind',merge_base_commit={'sha':self.fixture['obsolete']})
                return original(*args,**kwargs)
            with mock.patch.object(api,'installation_contract',side_effect=changed):
                self.assertEqual((self.invoke(api,True)[0],api.attempts),(2,0))

    def test_actual_bounded_native_transport_rejects_unknown_and_malformed_read(self):
        api=MODULE.GhApi()
        with mock.patch.object(MODULE,'bounded_watch_query',return_value=(0,b'{"id":42}')) as query:
            self.assertEqual(api.installation_read('repos/FS-GG/FS.GG.Coordination'),{'id':42})
            self.assertEqual(query.call_args.args[0],['gh','api','repos/FS-GG/FS.GG.Coordination'])
            self.assertEqual(query.call_args.kwargs['max_bytes'],1048576)
        for code,body in ((1,b'{}'),(0,b'not JSON')):
            with mock.patch.object(MODULE,'bounded_watch_query',return_value=(code,body)):
                with self.assertRaises(RuntimeError):api.installation_read('repos/FS-GG/FS.GG.Coordination')


# ---------------------------------------------------------------------------------------------------
# Board-currency merge gate (design §2b; Refs #5094). Every branch runs against a fake native API and the
# real shared R1 implementation (scripts/board-currency-rules.py); nothing here reaches GitHub.

BOARD_RULES = MODULE.load_board_rules()
BOARD_STATUSES = ("Backlog", "Ready", "In progress", "In review", "Blocked", "Done")
BOARD_OPTION = {name: f"opt{index}" for index, name in enumerate(BOARD_STATUSES)}
BOARD_DELIVERY = "FS-GG/.github#900"
BOARD_OUTCOME = "FS-GG/.github#800"
BOARD_MARKER = f"<!-- fsgg:routine-development/v1 head={HEAD} operation=source-change -->"
BOARD_OVERRIDE = '<!-- fsgg:board-gate-override/v1 rule=R1 reason="private fixture reason" -->'


def board_body(*lines: str) -> str:
    return "\n".join((BOARD_MARKER, "Private fixture prose.") + lines)


def board_routine_pr(body: str) -> dict:
    pr = opened()
    pr["head"]["ref"] = "routine/fixture"
    pr["body"] = body
    return pr


def board_card(ref: str, level: str, *, status: str = "In progress", state: str = "OPEN",
               parent: str | None = None) -> dict:
    repo, number = ref.rsplit("#", 1)
    card = {"repo": repo, "number": int(number), "state": state, "workLevel": level, "status": status, "parent": None}
    if parent:
        prepo, pnumber = parent.rsplit("#", 1)
        card["parent"] = {"repo": prepo, "number": int(pnumber)}
    return card


def board_linked_cards() -> dict:
    return {BOARD_DELIVERY: board_card(BOARD_DELIVERY, "Delivery", parent=BOARD_OUTCOME),
            BOARD_OUTCOME: board_card(BOARD_OUTCOME, "Outcome")}


def board_item(status: str | None = "In progress", state: str = "OPEN", values: list | None = None) -> dict:
    return {"state": state, "itemId": "PVTI_item", "projectId": "PVT_project", "statusFieldId": "PVTSSF_status",
            "options": [[BOARD_OPTION[name], name] for name in BOARD_STATUSES], "status": status,
            "values": list(values if values is not None else ['{"text":"Title"}'])}


class BoardApi(FakeApi):
    """FakeApi plus the gate's four board methods, each counted, each able to fail."""

    def __init__(self, reads, writes=None, *, body: str, author: str = "agent", edited: bool = False,
                 editor: str | None = None, pr_meta=None, cards=None, items=(), send=None, delivery_body=None):
        super().__init__(reads, writes)
        self.delivery_body = delivery_body
        self.pr_meta = pr_meta if pr_meta is not None else {
            "repo": "FS-GG/.github", "number": 1, "state": "OPEN", "body": body,
            "author": author, "edited": edited, "editor": editor}
        self.cards = board_linked_cards() if cards is None else cards
        self.items = list(items)
        self.send = send
        self.calls: list[str] = []
        self.sends: list[tuple] = []
        self.custody: list[bool] = []
        self.item_reads = 0

    def board_pr(self, repo, number):
        self.calls.append("board_pr")
        if isinstance(self.pr_meta, BaseException):
            raise self.pr_meta
        return copy.deepcopy(self.pr_meta)

    def board_cards(self, refs):
        self.calls.append("board_cards")
        if isinstance(self.cards, BaseException):
            raise self.cards
        return {**{ref: None for ref in refs}, **copy.deepcopy(self.cards)}

    def board_item(self, ref):
        self.calls.append("board_item")
        self.item_reads += 1
        value = self.items.pop(0) if self.items else AssertionError("unexpected board item read")
        if isinstance(value, BaseException):
            raise value
        return copy.deepcopy(value)

    def board_done_when(self, ref):
        self.calls.append("board_done_when")
        if isinstance(self.delivery_body, BaseException):
            raise self.delivery_body
        return self.delivery_body

    def board_set_status(self, project_id, item_id, field_id, option_id):
        self.calls.append("board_set_status")
        log = getattr(sys.stderr, "getvalue", lambda: "")()
        self.custody.append(log.count("possible-send recorded") == len(self.sends) + 1)
        self.sends.append((project_id, item_id, field_id, option_id))
        if isinstance(self.send, BaseException):
            raise self.send


class BoardGateTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(BOARD_RULES, "the shared rules module must load from this checkout")

    def private_lock(self, **kwargs):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        directory = pathlib.Path(tmp.name)
        return MODULE.BoardWriterLock(MODULE.board_writer_lock_paths(directory, directory), **kwargs)

    def gate(self, api: BoardApi, mode: str, *, loader=None, apply: bool = True, custody="fresh", lock="fresh"):
        if custody == "fresh":
            tmp = tempfile.TemporaryDirectory()
            self.addCleanup(tmp.cleanup)
            custody = MODULE.BoardCustody(pathlib.Path(tmp.name) / "custody") if mode == "enforce" else None
        if lock == "fresh":
            lock = self.private_lock() if mode == "enforce" else None
        self.last_custody = custody
        self.last_lock = lock
        with contextlib.redirect_stderr(io.StringIO()) as log, \
             mock.patch.object(MODULE, "board_enforce_allowed", return_value=True):
            code, result = MODULE.summarize(
                api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                publication_required=False, apply=apply, board_gate=mode,
                board_rules_loader=loader or (lambda: BOARD_RULES), board_custody=custody, board_lock=lock)
        self.last_log = log.getvalue()
        # Possible-send custody is recorded exactly when (and before) the one send is attempted.
        self.assertEqual(log.getvalue().count("possible-send recorded"), len(api.sends))
        self.assertTrue(all(api.custody), "possible-send must be recorded before the send")
        # Done has one writer (the built-in project workflow): no branch of the gate ever sends it.
        self.assertNotIn(BOARD_OPTION["Done"], [send[3] for send in api.sends])
        # Private body text, the override reason and logins never reach the result.
        text = json.dumps(asdict(result))
        for private in ("Private fixture prose", "private fixture reason", "EHotwagner", "agent"):
            self.assertNotIn(private, text)
        return code, result

    def merging(self, body: str, **kwargs) -> BoardApi:
        pr = board_routine_pr(body)
        return BoardApi([pr, pr, merged()], [{"merged": True, "sha": MERGE}], body=body, **kwargs)

    def holding(self, body: str, **kwargs) -> BoardApi:
        pr = board_routine_pr(body)
        return BoardApi([pr, pr], body=body, **kwargs)

    # ---- the delivery declaration (board-reconciler design §12 PR 5) ----

    def marker_gate(self, body: str, *, mode: str = "advisory", **kwargs):
        api = self.merging(body, items=[board_item("In progress")], **kwargs)
        with contextlib.redirect_stderr(io.StringIO()), mock.patch.object(MODULE, "board_enforce_allowed", return_value=True):
            code, result = MODULE.summarize(
                api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                publication_required=False, apply=True, board_gate="advisory",
                board_rules_loader=lambda: BOARD_RULES, delivery_marker=mode)
        return api, code, result

    def test_advisory_marker_records_the_declaration_and_never_changes_the_outcome(self):
        cases = {
            "none": (board_body("Refs #900"), "neither"),
            "both": (board_body("Refs #900", MODULE.BOARD_CONTINUES, MODULE.BOARD_COMPLETES), "both"),
            "continues": (board_body("Refs #900", MODULE.BOARD_CONTINUES), None),
            "completes": (board_body("Refs #900", MODULE.BOARD_COMPLETES), None),
            # A marker quoted in code does not count.
            "none-quoted": (board_body("Refs #900", "`" + MODULE.BOARD_COMPLETES + "`"), "neither"),
        }
        for name, (body, warning) in cases.items():
            with self.subTest(name):
                api, code, result = self.marker_gate(body)
                marker = result.deliveryMarker
                self.assertEqual(marker["declared"], name.split("-")[0])
                self.assertTrue(marker["advisory"])
                if warning is None:
                    self.assertIsNone(marker["warning"])
                else:
                    self.assertIn(warning, marker["warning"])
                self.assertEqual((code, result.outcome, result.attempts), (0, "delivered", 1))
                self.assertEqual(result.schemaMinor, 1)

    def test_completes_on_a_delivery_whose_done_when_names_a_release_warns(self):
        delivery = "Work level: Delivery\n\n## Done when\nThe engine is released and installed on fdev, with readback.\n\n## Owner\nRoot\n"
        api, code, result = self.marker_gate(board_body("Refs #900", MODULE.BOARD_COMPLETES), delivery_body=delivery)
        self.assertIn("release, install or readback", result.deliveryMarker["warning"])
        self.assertEqual((code, result.outcome), (0, "delivered"))
        # A source-only Done when, or an unreadable Delivery, gives no warning.
        _, _, quiet = self.marker_gate(board_body("Refs #900", MODULE.BOARD_COMPLETES), delivery_body="## Done when\nThe parser change is merged.\n")
        self.assertIsNone(quiet.deliveryMarker["warning"])
        _, _, unreadable = self.marker_gate(board_body("Refs #900", MODULE.BOARD_COMPLETES), delivery_body=RuntimeError("down"))
        self.assertIsNone(unreadable.deliveryMarker["warning"])
        # The warning names no private text.
        self.assertNotIn("fdev", json.dumps(asdict(result)))

    def test_required_marker_refuses_an_undeclared_or_double_declaration_before_merge(self):
        for name, body in (("none", board_body("Refs #900")),
                           ("both", board_body("Refs #900", MODULE.BOARD_CONTINUES, MODULE.BOARD_COMPLETES))):
            with self.subTest(name):
                pr = board_routine_pr(body)
                api = BoardApi([pr, pr], body=body)
                with contextlib.redirect_stderr(io.StringIO()), mock.patch.object(MODULE, "board_enforce_allowed", return_value=True):
                    code, result = MODULE.summarize(
                        api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                        publication_required=False, apply=True, board_gate="advisory",
                        board_rules_loader=lambda: BOARD_RULES, delivery_marker="required")
                self.assertEqual((code, result.outcome, result.attempts, result.reason), (2, "refused", 0, f"delivery-marker {name}"))
                self.assertFalse(result.deliveryMarker["advisory"])
                self.assertEqual(api.attempts, 0)  # no merge attempt
        _, code, result = self.marker_gate(board_body("Refs #900", MODULE.BOARD_COMPLETES), mode="required")
        self.assertEqual((code, result.outcome), (0, "delivered"))

    def test_marker_is_absent_for_a_pr_that_is_not_routine_and_prior_results_keep_their_shape(self):
        current = {field.name for field in fields(MODULE.Summary)}
        prior = {name: None for name in current - MODULE.MARKER_RESULT_FIELDS - MODULE.BOARD_RESULT_FIELDS}
        self.assertTrue(MODULE.installation_result_shape(prior))
        self.assertTrue(MODULE.installation_result_shape({name: None for name in current - MODULE.MARKER_RESULT_FIELDS}))
        self.assertFalse(MODULE.installation_result_shape({**prior, "unexpected": 1}))
        self.assertIsNone(MODULE.Summary("s", "r", 1, HEAD, None, "ready", "not-delivered", "not-required", None, 0, None, "current", "not-required").deliveryMarker)
        with self.assertRaises(RuntimeError):
            MODULE.summarize(BoardApi([], body=""), repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                             publication_required=False, apply=True, delivery_marker="strict")

    # ---- verdict x mode ----

    def test_linked_advisory_records_would_write_and_never_sends(self):
        api = self.merging(board_body("Refs #900"), items=[board_item("In progress")])
        code, result = self.gate(api, "advisory")
        self.assertEqual((code, result.outcome, api.attempts, result.attempts), (0, "delivered", 1, 1))
        self.assertEqual((result.boardGate, result.boardGateReason, result.boardTransition),
                         ("linked", None, "would-write:In review"))
        self.assertEqual((api.sends, api.item_reads), ([], 1))

    def test_linked_enforce_merges_then_writes_in_review_exactly_once(self):
        before = board_item("In progress")
        api = self.merging(board_body("Refs #900", "Refs #800"),
                           items=[before, before, board_item("In review")])
        code, result = self.gate(api, "enforce")
        self.assertEqual((code, result.outcome, result.codeDelivery, api.attempts), (0, "delivered", "delivered", 1))
        self.assertEqual((result.boardGate, result.boardTransition), ("linked", "written"))
        self.assertEqual(api.sends, [("PVT_project", "PVTI_item", "PVTSSF_status", BOARD_OPTION["In review"])])
        # Order: gate reads, merge, then preimage, adjacent preimage, send, independent readback.
        self.assertEqual(api.calls, ["board_pr", "board_cards", "board_item", "board_item", "board_set_status", "board_item"])

    def test_refused_enforce_makes_no_merge_call_and_leaves_attempts(self):
        for refs, reason in (("Refs #800", "R1 no-delivery"), ("Refs #901", "R1 no-delivery"),
                             ("Refs #900, #902", "R1 multiple-deliveries")):
            with self.subTest(refs=refs):
                cards = board_linked_cards()
                cards["FS-GG/.github#902"] = board_card("FS-GG/.github#902", "Delivery", parent=BOARD_OUTCOME)
                api = self.holding(board_body(refs), cards=cards)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, result.codeDelivery, result.attempts, api.attempts),
                                 (2, "refused", "not-delivered", 0, 0))
                self.assertEqual(result.reason, f"board-linkage {reason}")
                self.assertEqual((result.boardGate, result.boardGateReason, result.boardTransition),
                                 ("refused", reason, "not-applicable"))
                self.assertNotIn("board_item", api.calls)

    def test_refused_enforce_covers_parent_closed_and_done_delivery(self):
        for card, reason in (
                (board_card(BOARD_DELIVERY, "Delivery"), "R1 no-parent-outcome"),
                (board_card(BOARD_DELIVERY, "Delivery", state="CLOSED", status="Done", parent=BOARD_OUTCOME), "R1 delivery-closed"),
                (board_card(BOARD_DELIVERY, "Delivery", status="Done", parent=BOARD_OUTCOME), "R1 delivery-done")):
            with self.subTest(reason=reason):
                cards = board_linked_cards()
                cards[BOARD_DELIVERY] = card
                api = self.holding(board_body("Refs #900"), cards=cards)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, api.attempts, result.reason),
                                 (2, "refused", 0, f"board-linkage {reason}"))

    def test_refused_advisory_never_changes_the_merge(self):
        api = self.merging(board_body("Refs #800"))
        code, result = self.gate(api, "advisory")
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered", 1))
        self.assertEqual((result.boardGate, result.boardGateReason, result.boardTransition),
                         ("refused", "R1 no-delivery", "not-applicable"))
        self.assertEqual(api.sends, [])

    def test_unknown_enforce_is_indeterminate_without_merge(self):
        unreadable = board_linked_cards()
        unreadable[BOARD_DELIVERY] = {"repo": "FS-GG/.github", "number": 900, "unreadable": True}
        parent_unreadable = board_linked_cards()
        parent_unreadable[BOARD_OUTCOME] = {"repo": "FS-GG/.github", "number": 800, "unreadable": True}
        for label, kwargs, reason in (
                ("cards failed", {"cards": RuntimeError("provider text")}, "board-read-failed"),
                ("pr failed", {"pr_meta": RuntimeError("provider text")}, "pr-read-failed"),
                ("reference unreadable", {"cards": unreadable}, "U r1-reference-unreadable"),
                ("parent unreadable", {"cards": parent_unreadable}, "U r1-parent-unreadable")):
            with self.subTest(label):
                api = self.holding(board_body("Refs #900"), **kwargs)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, result.codeDelivery, result.attempts, api.attempts),
                                 (3, "indeterminate", "not-delivered", 0, 0))
                self.assertEqual(result.reason, "board-linkage unknown")
                self.assertEqual((result.boardGate, result.boardGateReason), ("unknown", reason))
                self.assertNotIn("provider text", json.dumps(asdict(result)))

    def test_unknown_advisory_merges_and_records(self):
        api = self.merging(board_body("Refs #900"), cards=RuntimeError("provider text"))
        code, result = self.gate(api, "advisory")
        self.assertEqual((code, result.outcome, api.attempts), (0, "delivered", 1))
        self.assertEqual((result.boardGate, result.boardGateReason, result.boardTransition),
                         ("unknown", "board-read-failed", "not-applicable"))

    def test_moved_body_or_state_between_reads_is_unknown(self):
        for change in ({"body": board_body("Refs #900", "edited later")}, {"state": "MERGED"}, {"edited": None}):
            with self.subTest(change=change):
                api = self.holding(board_body("Refs #900"))
                api.pr_meta.update(change)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, api.attempts, result.boardGateReason), (3, 0, "pr-read-inconsistent"))
                self.assertNotIn("board_cards", api.calls)

    # ---- override first ----

    def test_override_first_never_reads_the_board(self):
        for mode in ("advisory", "enforce"):
            with self.subTest(mode=mode):
                body = board_body("Refs #4730", BOARD_OVERRIDE)
                api = self.merging(body, author="EHotwagner", cards=AssertionError("board read"))
                code, result = self.gate(api, mode)
                self.assertEqual((code, result.outcome, api.attempts), (0, "delivered", 1))
                self.assertEqual((result.boardGate, result.boardGateReason, result.boardTransition),
                                 ("overridden", "valid-override", "not-applicable"))
                self.assertEqual(api.calls, ["board_pr"])

    def test_invalid_override_is_judged_as_absent(self):
        body = board_body("Refs #800", BOARD_OVERRIDE)
        for label, kwargs in (("agent author", {"author": "someone"}),
                              ("agent editor", {"author": "EHotwagner", "edited": True, "editor": "someone"}),
                              ("override in code span", {"author": "EHotwagner"})):
            with self.subTest(label):
                text = board_body("Refs #800", f"`{BOARD_OVERRIDE}`") if "code" in label else body
                api = self.holding(text, **kwargs)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, api.attempts, result.boardGateReason),
                                 (2, "refused", 0, "R1 no-delivery"))

    # ---- module availability ----

    def test_unavailable_module_is_advisory_record_or_enforce_indeterminate(self):
        api = self.merging(board_body("Refs #900"))
        code, result = self.gate(api, "advisory", loader=lambda: None)
        self.assertEqual((code, result.outcome, api.attempts, result.boardGate, result.boardGateReason),
                         (0, "delivered", 1, "unavailable", "rules-module-unavailable"))
        self.assertEqual(api.calls, [])
        api = self.holding(board_body("Refs #900"))
        code, result = self.gate(api, "enforce", loader=lambda: None)
        self.assertEqual((code, result.outcome, result.reason, api.attempts, result.boardGate),
                         (3, "indeterminate", "board-linkage unknown", 0, "unavailable"))
        self.assertEqual(api.calls, [])

    def test_receiver_layout_without_rules_module_loads_nothing(self):
        with tempfile.TemporaryDirectory() as directory:
            fake = pathlib.Path(directory) / "tools" / "routine-delivery.py"
            with mock.patch.object(MODULE, "__file__", str(fake)):
                self.assertIsNone(MODULE.load_board_rules())
            (pathlib.Path(directory) / "scripts").mkdir()
            (pathlib.Path(directory) / "scripts" / "board-currency-rules.py").write_text("raise SystemExit(9)\n")
            with mock.patch.object(MODULE, "__file__", str(fake)):
                self.assertIsNone(MODULE.load_board_rules())

    # ---- applicability ----

    def test_gate_runs_only_under_apply_for_routine_marker_on_open_pr(self):
        plain = opened()
        api = BoardApi([plain, merged()], [{"merged": True, "sha": MERGE}], body="Fixture prose")
        code, result = self.gate(api, "enforce")
        self.assertEqual((code, result.boardGate, api.calls), (0, "not-applicable", []))
        api = self.holding(board_body("Refs #800"))
        api.reads = iter([board_routine_pr(board_body("Refs #800"))])
        code, result = self.gate(api, "enforce", apply=False)
        self.assertEqual((code, result.outcome, result.boardGate, api.calls), (0, "ready", "not-applicable", []))
        done = merged(); done["head"]["ref"] = "routine/fixture"; done["body"] = board_body("Refs #800")
        api = BoardApi([done], body=done["body"])
        code, result = self.gate(api, "enforce")
        self.assertEqual((code, result.outcome, result.boardGate, api.calls), (0, "delivered", "not-applicable", []))

    def test_invalid_mode_refuses_before_any_read(self):
        api = self.holding(board_body("Refs #900"))
        with self.assertRaises(RuntimeError):
            self.gate(api, "required")
        self.assertEqual(api.read_attempts, 0)

    # ---- post-merge transition ----

    def test_preimage_cases_enforce(self):
        cases = (
            ("Backlog", "written"), ("Ready", "written"), ("In progress", "written"),
            ("In review", "noop"), ("Blocked", "refused-preimage"), ("Done", "refused-preimage"),
            (None, "refused-preimage"), ("Unknown option", "refused-preimage"))
        for status, expected in cases:
            with self.subTest(status=status):
                before = board_item(status)
                items = [before, before, board_item("In review")] if expected == "written" else [before]
                api = self.merging(board_body("Refs #900"), items=items)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, result.boardTransition), (0, "delivered", expected))
                self.assertEqual(len(api.sends), 1 if expected == "written" else 0)

    def test_preimage_cases_advisory_never_write(self):
        cases = (("Backlog", "would-write:In review"), ("In review", "noop"),
                 ("Blocked", "would-refuse-preimage"), ("Done", "would-refuse-preimage"),
                 (None, "would-refuse-preimage"))
        for status, expected in cases:
            with self.subTest(status=status):
                api = self.merging(board_body("Refs #900"), items=[board_item(status)])
                code, result = self.gate(api, "advisory")
                self.assertEqual((result.boardTransition, api.sends, api.item_reads), (expected, [], 1))
        api = self.merging(board_body("Refs #900"), items=[RuntimeError("provider text")])
        code, result = self.gate(api, "advisory")
        self.assertEqual((result.boardTransition, api.sends), ("would-refuse-preimage", []))

    def test_unreadable_or_malformed_preimage_refuses_without_send(self):
        malformed = board_item("Ready"); malformed["itemId"] = "bad id with spaces"
        missing_option = board_item("Ready"); missing_option["options"] = [o for o in missing_option["options"] if o[1] != "In review"]
        for label, value in (("read failed", RuntimeError("provider text")), ("malformed", malformed),
                             ("option absent", missing_option), ("wrong shape", {"status": "Ready"})):
            with self.subTest(label):
                api = self.merging(board_body("Refs #900"), items=[value])
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, result.boardTransition, api.sends),
                                 (0, "delivered", "refused-preimage", []))

    def test_adjacent_preimage_change_refuses_without_send(self):
        api = self.merging(board_body("Refs #900"), items=[board_item("Ready"), board_item("Blocked")])
        code, result = self.gate(api, "enforce")
        self.assertEqual((result.boardTransition, api.sends), ("refused-preimage", []))

    def test_closed_delivery_is_never_written_and_never_set_done(self):
        for mode in ("advisory", "enforce"):
            with self.subTest(mode=mode, route="fresh read closed"):
                api = self.merging(board_body("Refs #900"), items=[board_item("In progress", state="CLOSED")])
                code, result = self.gate(api, mode)
                self.assertEqual((code, result.boardTransition, api.sends), (0, "not-applicable", []))
            with self.subTest(mode=mode, route="closing keyword"):
                api = self.merging(board_body("Closes #900"))
                code, result = self.gate(api, mode)
                self.assertEqual((result.boardGate, result.boardTransition, api.sends, api.item_reads),
                                 ("linked", "not-applicable", [], 0))

    def test_continues_marker_targets_in_progress_only_when_visible(self):
        before = board_item("Ready")
        api = self.merging(board_body("Refs #900", "<!-- fsgg:delivery-continues/v1 -->"),
                           items=[before, before, board_item("In progress")])
        code, result = self.gate(api, "enforce")
        self.assertEqual((result.boardTransition, api.sends[0][3]), ("written", BOARD_OPTION["In progress"]))
        api = self.merging(board_body("Refs #900", "<!-- fsgg:delivery-continues/v1 -->"), items=[board_item("In progress")])
        self.assertEqual(self.gate(api, "enforce")[1].boardTransition, "noop")
        api = self.merging(board_body("Refs #900", "`<!-- fsgg:delivery-continues/v1 -->`"), items=[board_item("Ready")])
        self.assertEqual(self.gate(api, "advisory")[1].boardTransition, "would-write:In review")

    def test_uncertain_send_or_readback_is_never_resent(self):
        before = board_item("In progress")
        changed = board_item("In review", values=['{"text":"Other title"}'])
        for label, items, send in (
                ("send failed", [before, before, board_item("In review")], RuntimeError("provider text")),
                ("readback failed", [before, before, RuntimeError("provider text")], None),
                ("readback unchanged", [before, before, before], None),
                ("unrelated value moved", [before, before, changed], None),
                ("readback set Done", [before, before, board_item("Done")], None)):
            with self.subTest(label):
                api = self.merging(board_body("Refs #900"), items=items, send=send)
                code, result = self.gate(api, "enforce")
                self.assertEqual((code, result.outcome, result.boardTransition), (0, "delivered", "uncertain"))
                self.assertEqual(len(api.sends), 1)
                self.assertEqual(api.calls.count("board_set_status"), 1)

    def test_no_transition_without_confirmed_merge(self):
        pr = board_routine_pr(board_body("Refs #900"))
        api = BoardApi([pr, pr, pr], [MODULE.AmbiguousWrite("lost")], body=pr["body"])
        code, result = self.gate(api, "enforce")
        self.assertEqual((code, result.outcome, result.boardGate, result.boardTransition, api.calls),
                         (3, "indeterminate", "linked", "not-applicable", ["board_pr", "board_cards"]))

    # ---- result shape and CLI ----

    def test_result_shape_accepts_pre_gate_and_gate_results_only(self):
        _, summary = MODULE.summarize(FakeApi([merged()]), repo="FS-GG/.github", pr_number=1, expected_head=HEAD,
                                      merge_method="squash", publication_required=False, apply=False)
        current = asdict(summary)
        legacy = {k: v for k, v in current.items() if k not in MODULE.BOARD_RESULT_FIELDS}
        evidence = {"deliveryEvidence": {"schema": "fsgg.routine-delivery-evidence/1"}}
        for value in (current, legacy, dict(current, **evidence), dict(legacy, **evidence)):
            self.assertTrue(MODULE.installation_result_shape(value))
        partial = dict(legacy, boardGate="linked")
        self.assertFalse(MODULE.installation_result_shape(partial))
        self.assertFalse(MODULE.installation_result_shape(dict(partial, **evidence)))
        self.assertFalse(MODULE.installation_result_shape(dict(current, deliveryEvidence={"schema": "other"})))
        self.assertEqual((summary.boardGate, summary.boardGateReason, summary.boardTransition),
                         ("not-applicable", None, "not-applicable"))

    def test_cli_mode_defaults_to_advisory_and_rejects_other_values(self):
        base = ["--repo", "FS-GG/.github", "--pr", "1", "--head", HEAD]
        self.assertEqual(MODULE.parser().parse_args(base).board_gate, "advisory")
        self.assertEqual(MODULE.parser().parse_args(base + ["--board-gate", "enforce"]).board_gate, "enforce")
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            MODULE.parser().parse_args(base + ["--board-gate", "required"])
        captured = {}
        def fake_summarize(*args, **kwargs):
            captured.update(kwargs)
            raise RuntimeError("stop")
        with mock.patch.object(MODULE, "summarize", side_effect=fake_summarize), \
             mock.patch.object(MODULE, "discover_telemetry_config", return_value=None), \
             mock.patch.object(MODULE, "delivery_evidence", side_effect=RuntimeError("off")), \
             mock.patch.object(MODULE, "board_enforce_allowed", return_value=True), \
             contextlib.redirect_stdout(io.StringIO()) as out:
            self.assertEqual(MODULE.main(base + ["--apply", "--board-gate", "enforce"]), 3)
        self.assertEqual(captured["board_gate"], "enforce")
        self.assertIsInstance(captured["board_custody"], MODULE.BoardCustody)
        self.assertEqual(json.loads(out.getvalue())["boardGate"], "not-applicable")

    # ---- pre-enforce controls: durable custody, .github-only enforce, PR-typed refs ----

    def test_custody_is_durable_written_before_the_send_and_verified_after_readback(self):
        before = board_item("In progress")
        api = self.merging(board_body("Refs #900"), items=[before, before, board_item("In review")])
        seen = {}
        original = api.board_set_status
        def send(*args):
            seen["record"] = self.last_custody.existing("FS-GG/.github", 1)
            return original(*args)
        api.board_set_status = send
        code, result = self.gate(api, "enforce")
        self.assertEqual(result.boardTransition, "written")
        self.assertEqual((seen["record"]["state"], seen["record"]["delivery"], seen["record"]["from"], seen["record"]["to"]),
                         ("possible-send", BOARD_DELIVERY, "In progress", "In review"))
        self.assertEqual(self.last_custody.existing("FS-GG/.github", 1)["state"], "verified")
        path = self.last_custody.path("FS-GG/.github", 1)
        self.assertEqual(oct(path.parent.stat().st_mode & 0o777), "0o700")
        self.assertNotIn("Private fixture prose", path.read_text())
        self.assertEqual([p.suffix for p in path.parent.iterdir()], [".json"])  # no temp file left behind

    def test_enforce_without_custody_or_with_unwritable_custody_never_sends(self):
        before = board_item("In progress")
        api = self.merging(board_body("Refs #900"), items=[before, before])
        code, result = self.gate(api, "enforce", custody=None)
        self.assertEqual((result.outcome, result.boardTransition, api.sends), ("delivered", "refused-custody", []))
        with tempfile.TemporaryDirectory() as tmp:
            blocker = pathlib.Path(tmp) / "file"
            blocker.write_text("not a directory")
            api = self.merging(board_body("Refs #900"), items=[before, before])
            code, result = self.gate(api, "enforce", custody=MODULE.BoardCustody(blocker / "custody"))
            self.assertEqual((result.boardTransition, api.sends), ("refused-custody", []))

    def _crash_after_send(self, custody):
        """Run 1: enforce merges, records custody, sends, then the process dies before the readback."""
        before = board_item("In progress")
        api = self.merging(board_body("Refs #900"), items=[before, before, KeyboardInterrupt("crash after send")])
        with self.assertRaises(KeyboardInterrupt), contextlib.redirect_stderr(io.StringIO()), \
             mock.patch.object(MODULE, "board_enforce_allowed", return_value=True):
            MODULE.summarize(api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                             publication_required=False, apply=True, board_gate="enforce",
                             board_rules_loader=lambda: BOARD_RULES, board_custody=custody,
                             board_lock=self.private_lock())
        self.assertEqual(len(api.sends), 1)
        self.assertEqual(custody.existing("FS-GG/.github", 1)["state"], "possible-send")

    def _merged_rerun(self, custody, items, mode="enforce"):
        pr = merged()
        pr["body"] = board_body("Refs #900")
        pr["head"]["ref"] = "routine/fixture"
        api = BoardApi([pr, pr], body=pr["body"], items=items)
        code, result = self.gate(api, mode, custody=custody)
        return api, result

    def test_defence_existing_record_on_an_unmerged_pr_is_never_overwritten(self):
        # Not a reachable recovery route (records are written only after a merge, and the merged path reads
        # back instead); this pins the defence in board_transition that refuses to overwrite a record.
        with tempfile.TemporaryDirectory() as tmp:
            custody = MODULE.BoardCustody(pathlib.Path(tmp) / "c")
            custody.write("FS-GG/.github", 1, {"delivery": BOARD_DELIVERY, "to": "In review", "state": "possible-send"})
            before = board_item("In progress")
            api = self.merging(board_body("Refs #900"), items=[before, before])
            code, result = self.gate(api, "enforce", custody=custody)
            self.assertEqual((result.boardTransition, api.sends), ("uncertain", []))
            self.assertEqual(custody.existing("FS-GG/.github", 1)["state"], "possible-send")

    def test_rerun_after_a_crash_on_the_merged_pr_reads_back_and_never_resends(self):
        with tempfile.TemporaryDirectory() as tmp:
            custody = MODULE.BoardCustody(pathlib.Path(tmp) / "c")
            self._crash_after_send(custody)
            api, result = self._merged_rerun(custody, [board_item("In review")])
            self.assertEqual((result.outcome, result.boardGateReason, result.boardTransition, api.sends, api.item_reads),
                             ("delivered", "custody-readback", "written", [], 1))
            self.assertEqual(custody.existing("FS-GG/.github", 1)["state"], "verified")
            # Once verified, a further rerun reports written without reading or sending.
            api, result = self._merged_rerun(custody, [])
            self.assertEqual((result.boardTransition, api.sends, api.item_reads), ("written", [], 0))

    def test_rerun_after_a_crash_with_status_not_at_target_is_uncertain_and_keeps_custody(self):
        with tempfile.TemporaryDirectory() as tmp:
            custody = MODULE.BoardCustody(pathlib.Path(tmp) / "c")
            self._crash_after_send(custody)
            for items in ([board_item("In progress")], [RuntimeError("read failed")]):
                api, result = self._merged_rerun(custody, items)
                self.assertEqual((result.boardTransition, api.sends), ("uncertain", []))
                self.assertEqual(custody.existing("FS-GG/.github", 1)["state"], "possible-send")

    def test_merged_rerun_without_custody_or_in_advisory_reports_nothing(self):
        with tempfile.TemporaryDirectory() as tmp:
            custody = MODULE.BoardCustody(pathlib.Path(tmp) / "c")
            api, result = self._merged_rerun(custody, [])
            self.assertEqual((result.boardGateReason, result.boardTransition, api.item_reads), (None, "not-applicable", 0))
            self._crash_after_send(custody)
            api, result = self._merged_rerun(None, [], mode="advisory")
            self.assertEqual((result.boardTransition, api.item_reads, api.sends), ("not-applicable", 0, []))

    def test_malformed_or_foreign_custody_record_is_uncertain(self):
        with tempfile.TemporaryDirectory() as tmp:
            custody = MODULE.BoardCustody(pathlib.Path(tmp) / "c")
            custody.write("FS-GG/.github", 1, {"delivery": BOARD_DELIVERY, "to": "In review", "state": "possible-send"})
            path = custody.path("FS-GG/.github", 1)
            path.write_text(path.read_text().replace('"pr": 1', '"pr": 2'))
            api, result = self._merged_rerun(custody, [])
            self.assertEqual((result.boardTransition, api.sends, api.item_reads), ("uncertain", [], 0))

    def test_existing_custody_leaf_is_tightened_to_0700(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp) / "c"
            root.mkdir(mode=0o755)
            os.chmod(root, 0o755)
            MODULE.BoardCustody(root).write("FS-GG/.github", 1, {"delivery": BOARD_DELIVERY, "to": "In review",
                                                                 "state": "possible-send"})
            self.assertEqual(oct(root.stat().st_mode & 0o777), "0o700")

    def test_advisory_never_touches_custody(self):
        api = self.merging(board_body("Refs #900"), items=[board_item("In progress")])
        code, result = self.gate(api, "advisory")
        self.assertIsNone(self.last_custody)
        self.assertEqual(result.boardTransition, "would-write:In review")

    def test_origin_is_parsed_exactly(self):
        good = ("https://github.com/FS-GG/.github", "https://github.com/FS-GG/.github.git",
                "https://github.com/fs-gg/.github.git/", "https://GitHub.com/FS-GG/.github/",
                "git@github.com:FS-GG/.github.git", "git@github.com:FS-GG/.github",
                "ssh://git@github.com/FS-GG/.github.git")
        bad = ("https://evil.example/FS-GG/.github", "https://github.com/evil/FS-GG/.github",
               "https://x:y@evil.example/a/b/FS-GG/.github.git", "file:///tmp/fake/FS-GG/.github",
               "https://github.com.evil.example/FS-GG/.github", "https://user@github.com/FS-GG/.github",
               "https://github.com:8443/FS-GG/.github", "https://github.com/FS-GG/.github?x=1",
               "https://github.com/FS-GG/.github-fork", "https://github.com/other/.github",
               "git@evil.example:FS-GG/.github.git", "git@github.com:evil/FS-GG/.github",
               "ssh://evil@github.com/FS-GG/.github", "/srv/mirror/FS-GG/.github", "", "github.com/FS-GG/.github")
        for url in good:
            self.assertTrue(MODULE.board_origin_is_github(url), url)
        for url in bad:
            self.assertFalse(MODULE.board_origin_is_github(url), url)

    def test_enforce_runs_only_from_the_github_checkout(self):
        def origin(url, code=0):
            return subprocess.CompletedProcess([], code, url + "\n", "")
        for url, allowed in (("git@github.com:FS-GG/.github.git", True), ("https://evil.example/FS-GG/.github", False)):
            with self.subTest(url=url), mock.patch.object(MODULE.subprocess, "run", return_value=origin(url)):
                self.assertIs(MODULE.board_enforce_allowed(), allowed)
        with mock.patch.object(MODULE.subprocess, "run", return_value=origin("", 1)):
            self.assertFalse(MODULE.board_enforce_allowed())
        with mock.patch.object(MODULE.subprocess, "run", side_effect=OSError("no git")):
            self.assertFalse(MODULE.board_enforce_allowed())
        base = ["--repo", "FS-GG/.github", "--pr", "1", "--head", HEAD]
        with mock.patch.object(MODULE, "board_enforce_allowed", return_value=False), \
             contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            MODULE.main(base + ["--apply", "--board-gate", "enforce"])
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            MODULE.main(base + ["--apply", "--board-gate-custody", "/x"])

    def test_summarize_itself_refuses_enforce_outside_the_github_checkout(self):
        api = self.merging(board_body("Refs #900"))
        with mock.patch.object(MODULE, "board_enforce_allowed", return_value=False), \
             contextlib.redirect_stderr(io.StringIO()), self.assertRaises(RuntimeError):
            MODULE.summarize(api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                             publication_required=False, apply=True, board_gate="enforce",
                             board_rules_loader=lambda: BOARD_RULES, board_custody=None)
        self.assertEqual((api.attempts, api.sends, api.calls), (0, [], []))

    def test_distributed_default_stays_advisory(self):
        self.assertEqual(MODULE.parser().get_default("board_gate"), "advisory")

    def test_pr_typed_reference_is_never_a_delivery(self):
        node = {"__typename": "PullRequest", "number": 5, "state": "OPEN", "repository": {"nameWithOwner": "FS-GG/x"},
                "projectItems": {"totalCount": 1, "nodes": [{"project": {"number": 3, "owner": {
                    "__typename": "Organization", "login": "FS-GG"}}, "workLevel": {"name": "Delivery"},
                    "status": {"name": "In progress"}}]}}
        self.assertIsNone(MODULE.board_card(node))

    # ---- native adapter ----

    def test_native_send_is_one_exact_item_edit_and_failure_raises(self):
        api = MODULE.GhApi()
        ok = subprocess.CompletedProcess([], 0, "{}", "")
        with mock.patch.object(MODULE.subprocess, "run", return_value=ok) as run:
            api.board_set_status("PVT_p", "PVTI_i", "PVTSSF_s", "opt3")
        self.assertEqual(run.call_count, 1)
        self.assertEqual(run.call_args.args[0], ["gh", "project", "item-edit", "--id", "PVTI_i", "--project-id", "PVT_p",
                                                 "--field-id", "PVTSSF_s", "--single-select-option-id", "opt3"])
        with mock.patch.object(MODULE.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, "", "secret")) as run:
            with self.assertRaises(RuntimeError) as error:
                api.board_set_status("PVT_p", "PVTI_i", "PVTSSF_s", "opt3")
        self.assertEqual(run.call_count, 1)
        self.assertNotIn("secret", str(error.exception))

    @staticmethod
    def native_item(status="Ready", *, values_total=None, extra=None, option_id=None):
        options = [{"id": BOARD_OPTION[name], "name": name} for name in BOARD_STATUSES]
        nodes = [{"__typename": "ProjectV2ItemFieldTextValue", "text": "Title",
                  "field": {"__typename": "ProjectV2Field", "id": "F_title", "name": "Title"}}]
        if status is not None:
            nodes.append({"__typename": "ProjectV2ItemFieldSingleSelectValue", "name": status,
                          "optionId": option_id or BOARD_OPTION[status],
                          "field": {"__typename": "ProjectV2SingleSelectField", "id": "PVTSSF_status", "name": "Status"}})
        nodes += extra or []
        project = {"id": "PVT_project", "number": 3, "owner": {"__typename": "Organization", "login": "FS-GG"},
                   "field": {"__typename": "ProjectV2SingleSelectField", "id": "PVTSSF_status", "name": "Status", "options": options}}
        other = {"id": "PVTI_other", "project": {"id": "PVT_x", "number": 4, "owner": {"login": "FS-GG"}},
                 "fieldValues": {"totalCount": 0, "nodes": []}}
        return {"state": "OPEN", "projectItems": {"totalCount": 2, "nodes": [
            other, {"id": "PVTI_item", "project": project,
                    "fieldValues": {"totalCount": values_total if values_total is not None else len(nodes), "nodes": nodes}}]}}

    def test_native_item_normalizes_live_schema_and_refuses_partial_reads(self):
        state = MODULE.board_item_state(self.native_item("Ready"))
        self.assertEqual((state["itemId"], state["projectId"], state["statusFieldId"], state["status"]),
                         ("PVTI_item", "PVT_project", "PVTSSF_status", "Ready"))
        self.assertEqual(state["options"], [[BOARD_OPTION[n], n] for n in BOARD_STATUSES])
        self.assertEqual(len(state["values"]), 1)
        self.assertIsNone(MODULE.board_item_state(self.native_item(None))["status"])
        truncated_labels = [{"__typename": "ProjectV2ItemFieldLabelValue", "labels": {"totalCount": 3, "nodes": [{"name": "a"}]},
                             "field": {"__typename": "ProjectV2Field", "id": "F_labels", "name": "Labels"}}]
        for label, issue in (("values truncated", self.native_item(values_total=9)),
                             ("nested truncated", self.native_item(extra=truncated_labels)),
                             ("stale option", self.native_item(option_id="gone")),
                             ("not on board", {"state": "OPEN", "projectItems": {"totalCount": 0, "nodes": []}}),
                             ("items truncated", {"state": "OPEN", "projectItems": {"totalCount": 30, "nodes": []}})):
            with self.subTest(label):
                with self.assertRaises(RuntimeError):
                    MODULE.board_item_state(issue)
        api = MODULE.GhApi()
        with mock.patch.object(api, "_run", return_value={"data": {"repository": {"issue": self.native_item("Ready")}}}) as run:
            self.assertEqual(api.board_item("FS-GG/.github#900"), state)
        args = run.call_args.args[0]
        self.assertEqual(args[:2], ["graphql", "-f"])
        self.assertIn("-F", args); self.assertIn("number=900", args)
        with mock.patch.object(api, "_run", return_value={"data": {}, "errors": [{"message": "x"}]}):
            with self.assertRaises(RuntimeError):
                api.board_item("FS-GG/.github#900")

    def test_native_cards_resolve_parent_and_classify_board_membership(self):
        def item(level, status="In progress", number=3, login="FS-GG"):
            return {"project": {"number": number, "owner": {"__typename": "Organization", "login": login}},
                    "workLevel": {"__typename": "ProjectV2ItemFieldSingleSelectValue", "name": level},
                    "status": {"__typename": "ProjectV2ItemFieldSingleSelectValue", "name": status}}
        def node(number, items, *, total=None, parent=None, kind="Issue", repo="FS-GG/.github"):
            value = {"__typename": kind, "number": number, "state": "OPEN", "repository": {"nameWithOwner": repo},
                     "projectItems": {"totalCount": len(items) if total is None else total, "nodes": items}}
            if kind == "Issue":
                value["parent"] = parent
            return value
        outcome = node(800, [item("Outcome")])
        data = {"data": {
            "r0": {"issueOrPullRequest": node(900, [item("Delivery", number=4), item("Delivery")], parent=outcome)},
            "r1": {"issueOrPullRequest": node(901, [item("Delivery", login="other")])},
            "r2": {"issueOrPullRequest": node(902, [item("Delivery")], total=40)},
            "r3": {"issueOrPullRequest": node(5, [item("Delivery")], kind="PullRequest", repo="FS-GG/Other")}}}
        api = MODULE.GhApi()
        refs = ["FS-GG/.github#900", "FS-GG/.github#901", "FS-GG/.github#902", "FS-GG/Other#5"]
        with mock.patch.object(api, "_run", return_value=data) as run:
            cards = api.board_cards(refs)
        self.assertEqual(cards["FS-GG/.github#900"], {"repo": "FS-GG/.github", "number": 900, "state": "OPEN",
                         "workLevel": "Delivery", "status": "In progress", "parent": {"repo": "FS-GG/.github", "number": 800}})
        self.assertEqual(cards["FS-GG/.github#800"]["workLevel"], "Outcome")
        self.assertIsNone(cards["FS-GG/.github#901"])
        self.assertTrue(cards["FS-GG/.github#902"]["unreadable"])
        # A PR is never a Delivery: the gate, board-linkage and the observer agree (design §2b).
        self.assertIsNone(cards["FS-GG/Other#5"])
        args = run.call_args.args[0]
        self.assertIn("o3=FS-GG", args); self.assertIn("n3=Other", args); self.assertIn("k3=5", args)
        with self.assertRaises(RuntimeError):
            api.board_cards([f"FS-GG/.github#{n}" for n in range(1, 22)])
        with self.assertRaises(RuntimeError):
            api.board_cards(["bad owner/x#1"])
        with mock.patch.object(api, "_run", return_value={"data": {"r0": {"issueOrPullRequest": None}}}):
            with self.assertRaises(RuntimeError):
                api.board_cards(["FS-GG/.github#900"])

    def test_native_pr_read_projects_author_editor_and_edit_state(self):
        api = MODULE.GhApi()
        node = {"number": 1, "state": "OPEN", "body": "b", "lastEditedAt": "2026-10-31T00:00:00Z",
                "author": {"login": "A"}, "editor": {"login": "E"}}
        with mock.patch.object(api, "_run", return_value={"data": {"repository": {"pullRequest": node}}}):
            self.assertEqual(api.board_pr("FS-GG/.github", 1), {"repo": "FS-GG/.github", "number": 1, "state": "OPEN",
                             "body": "b", "author": "A", "edited": True, "editor": "E"})
        node = dict(node, lastEditedAt=None, editor=None)
        with mock.patch.object(api, "_run", return_value={"data": {"repository": {"pullRequest": node}}}):
            pr = api.board_pr("FS-GG/.github", 1)
        self.assertEqual((pr["edited"], pr["editor"]), (False, None))
        with mock.patch.object(api, "_run", return_value={"data": {"repository": {"pullRequest": dict(node, number=2)}}}):
            with self.assertRaises(RuntimeError):
                api.board_pr("FS-GG/.github", 1)


class BoardWriterLockReplayTests(unittest.TestCase):
    """The shared writer lock, deferred-transition custody, the start-of-run sweep and replay (board-reconciler
    design §6 and §8, #5136 PR 1, #5126)."""

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.dir = pathlib.Path(tmp.name)
        self.custody = MODULE.BoardCustody(self.dir / "custody")

    def lock(self, **kwargs):
        return MODULE.BoardWriterLock(MODULE.board_writer_lock_paths(self.dir / "lock", self.dir / "legacy"), **kwargs)

    def record(self, pr, state, *, delivery=BOARD_DELIVERY, merged_at="2026-10-31T10:00:00Z", frm="In progress",
               to="In review", item="PVTI_item"):
        record = {"delivery": delivery, "itemId": item, "from": frm, "to": to, "state": state}
        if merged_at is not None:
            record["mergedAt"] = merged_at
        self.custody.write("FS-GG/.github", pr, record)

    def state(self, pr):
        return self.custody.existing("FS-GG/.github", pr)["state"]

    def replay(self, items, *, lock=None, send=None, current=None):
        api = BoardApi([], body="", items=items, send=send)
        with contextlib.redirect_stderr(io.StringIO()) as log:
            counts = MODULE.board_replay(api, self.custody, lock or self.lock(wait=0), current)
        self.assertEqual(log.getvalue().count("possible-send recorded"), len(api.sends))
        return api, counts

    # ---- the lock ----

    def test_production_paths_need_xdg_runtime_dir_and_use_the_literal_tmp_legacy_path(self):
        with mock.patch.dict(os.environ, {}, clear=True):
            with self.assertRaises(RuntimeError):
                MODULE.board_writer_lock_paths()
        with mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": "relative/run"}, clear=True):
            with self.assertRaises(RuntimeError):
                MODULE.board_writer_lock_paths()
        with mock.patch.dict(os.environ, {"XDG_RUNTIME_DIR": "/run/user/1000"}, clear=True):
            self.assertEqual(MODULE.board_writer_lock_paths(), [
                pathlib.Path("/run/user/1000/fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock"),
                pathlib.Path("/tmp/fsgg-v2-migration-PVT_kwDOEYAWY84Bldpa.lock"),
                pathlib.Path("/tmp/fsgg-v2-observation-PVT_kwDOEYAWY84Bldpa.lock")])

    def test_paths_agree_with_the_engine_writers(self):
        source = (ROOT / "src/FS.GG.Coord.GitHub/V2WriterLock.fs").read_text(encoding="utf-8")
        for name in (MODULE.BOARD_WRITER_LOCK_NAME.replace("PVT_kwDOEYAWY84Bldpa", ""),
                     MODULE.BOARD_LEGACY_LOCK_NAME.replace("PVT_kwDOEYAWY84Bldpa", ""),
                     MODULE.BOARD_LEGACY_OBSERVATION_LOCK_NAME.replace("PVT_kwDOEYAWY84Bldpa", "")):
            self.assertIn(name.removesuffix(".lock"), source)
        self.assertIn('"/tmp"', source)
        self.assertIn('"XDG_RUNTIME_DIR"', source)

    def test_lock_excludes_a_second_holder_on_either_path_and_releases(self):
        first = self.lock(wait=0)
        self.assertTrue(first.acquire())
        self.assertFalse(self.lock(wait=0).acquire())
        first.release()
        for name in (MODULE.BOARD_LEGACY_LOCK_NAME, MODULE.BOARD_LEGACY_OBSERVATION_LOCK_NAME):
            legacy_only = MODULE.BoardWriterLock([self.dir / "legacy" / name], wait=0)
            self.assertTrue(legacy_only.acquire())
            self.assertFalse(self.lock(wait=0).acquire(), f"the legacy engine lock {name} must also exclude the gate")
            legacy_only.release()
        again = self.lock(wait=0)
        self.assertTrue(again.acquire())
        again.release()

    def test_configuration_errors_raise_and_refuse_the_write_rather_than_look_busy(self):
        blocker = self.dir / "file"
        blocker.write_text("not a directory")
        broken = MODULE.BoardWriterLock([blocker / "sub" / MODULE.BOARD_WRITER_LOCK_NAME], wait=0)
        with self.assertRaises(OSError):
            broken.acquire()
        # A symlinked lock file is refused (O_NOFOLLOW), not followed.
        (self.dir / "lock").mkdir()
        (self.dir / "lock" / MODULE.BOARD_WRITER_LOCK_NAME).symlink_to(self.dir / "elsewhere")
        with self.assertRaises(OSError):
            self.lock(wait=0).acquire()
        self.assertFalse((self.dir / "elsewhere").exists())
        api = BoardApi([], body="", items=[board_item("In progress")])
        result = MODULE.board_transition(api, BOARD_RULES, "FS-GG/.github", board_body("Refs #900"), BOARD_DELIVERY,
                                         "enforce", 1, self.custody, broken)
        self.assertEqual((result, api.sends), ("refused-lock", []))
        self.assertIsNone(self.custody.existing("FS-GG/.github", 1))  # no deferred record for a broken lock
        self.record(2, "deferred")
        counts = MODULE.board_replay(BoardApi([], body="", items=[]), self.custody, broken)
        self.assertEqual((counts["lock-error"], counts["lock-busy"], self.state(2)), (1, 0, "deferred"))

    def test_bounded_wait_polls_until_60_seconds_then_gives_up(self):
        holder = self.lock(wait=0)
        self.assertTrue(holder.acquire())
        self.addCleanup(holder.release)
        now = [0.0]
        slept = []
        def sleep(seconds):
            slept.append(seconds)
            now[0] += seconds
        waiter = self.lock(clock=lambda: now[0], sleep=sleep)
        self.assertEqual(waiter.wait, 60.0)
        self.assertFalse(waiter.acquire())
        self.assertAlmostEqual(sum(slept), 60.0)
        self.assertEqual(waiter.held, [])

    def test_missing_runtime_dir_refuses_the_write_not_the_merge(self):
        args = MODULE.parser().parse_args(["--repo", "FS-GG/.github", "--pr", "1", "--head", HEAD,
                                           "--apply", "--board-gate", "enforce"])
        with mock.patch.dict(os.environ, {}, clear=True), contextlib.redirect_stderr(io.StringIO()):
            self.assertIsNone(MODULE.board_lock_from_args(args))
        api = BoardApi([], body="", items=[board_item("In progress")])
        result = MODULE.board_transition(api, BOARD_RULES, "FS-GG/.github", board_body("Refs #900"), BOARD_DELIVERY,
                                         "enforce", 1, self.custody, None)
        self.assertEqual((result, api.sends), ("refused-lock", []))

    # ---- deferral when the lock stays busy ----

    def test_busy_lock_defers_with_merged_at_and_never_sends(self):
        holder = self.lock(wait=0)
        self.assertTrue(holder.acquire())
        self.addCleanup(holder.release)
        api = BoardApi([], body="", items=[board_item("In progress")])
        result = MODULE.board_transition(api, BOARD_RULES, "FS-GG/.github", board_body("Refs #900"), BOARD_DELIVERY,
                                         "enforce", 1, self.custody, self.lock(wait=0), "2026-10-31T12:00:00Z")
        self.assertEqual((result, api.sends, api.item_reads), ("deferred", [], 1))
        record = self.custody.existing("FS-GG/.github", 1)
        self.assertEqual((record["state"], record["from"], record["to"], record["mergedAt"]),
                         ("deferred", "In progress", "In review", "2026-10-31T12:00:00Z"))
        # A second deferral for the same PR never overwrites the record.
        api = BoardApi([], body="", items=[board_item("Ready")])
        result = MODULE.board_transition(api, BOARD_RULES, "FS-GG/.github", board_body("Refs #900"), BOARD_DELIVERY,
                                         "enforce", 1, self.custody, self.lock(wait=0), "2026-10-31T13:00:00Z")
        self.assertEqual((result, self.custody.existing("FS-GG/.github", 1)["from"]), ("uncertain", "In progress"))

    def test_validator_accepts_every_state_and_refuses_others(self):
        for index, state in enumerate(MODULE.BOARD_CUSTODY_STATES, start=1):
            self.record(index, state)
            self.assertEqual(self.state(index), state)
        self.assertEqual(set(MODULE.BOARD_CUSTODY_STATES),
                         {"possible-send", "verified", "deferred", "superseded", "refused", "not-applicable"})
        self.custody.write("FS-GG/.github", 99, {"delivery": BOARD_DELIVERY, "state": "sent-maybe"})
        with self.assertRaises(RuntimeError):
            self.custody.existing("FS-GG/.github", 99)

    # ---- replay ----

    def test_replay_moves_to_possible_send_before_the_one_send_then_verifies(self):
        self.record(1, "deferred")
        before = board_item("In progress")
        writes = []
        real = self.custody.write
        sent = []
        api = BoardApi([], body="", items=[before, board_item("In review")])
        original_send = api.board_set_status
        def send(*args):
            original_send(*args)
            sent.append(args)
        api.board_set_status = send
        def spy(repo, pr, record):
            writes.append((record["state"], len(sent)))
            real(repo, pr, record)
        with mock.patch.object(self.custody, "write", side_effect=spy), contextlib.redirect_stderr(io.StringIO()):
            counts = MODULE.board_replay(api, self.custody, self.lock(wait=0))
        self.assertEqual(api.sends, [("PVT_project", "PVTI_item", "PVTSSF_status", BOARD_OPTION["In review"])])
        self.assertEqual(writes, [("possible-send", 0), ("verified", 1)])
        self.assertEqual((self.state(1), counts["verified"]), ("verified", 1))

    def test_replay_refuses_on_any_preimage_mismatch(self):
        for status in ("Ready", "In review", "Blocked", None):
            with self.subTest(status=status):
                self.record(1, "deferred")
                api, counts = self.replay([board_item(status)])
                self.assertEqual((api.sends, self.state(1), counts["refused"]), ([], "refused", 1))

    def test_replay_of_a_closed_delivery_is_not_applicable(self):
        self.record(1, "deferred")
        api, counts = self.replay([board_item("In progress", state="CLOSED")])
        self.assertEqual((api.sends, self.state(1), counts["not-applicable"]), ([], "not-applicable", 1))

    def test_newest_record_per_delivery_supersedes_older_and_replay_runs_in_merge_order(self):
        other = "FS-GG/.github#901"
        self.record(1, "deferred", merged_at="2026-10-31T09:00:00Z")
        self.record(2, "deferred", merged_at="2026-10-31T11:00:00Z")
        self.record(3, "deferred", delivery=other, merged_at="2026-10-31T10:00:00Z", item="PVTI_other")
        other_item = dict(board_item("In progress"), itemId="PVTI_other")
        api, counts = self.replay([other_item, dict(other_item, status="In review"),
                                   board_item("In progress"), board_item("In review")])
        self.assertEqual([send[1] for send in api.sends], ["PVTI_other", "PVTI_item"])  # merge order
        self.assertEqual((self.state(1), self.state(2), self.state(3)), ("superseded", "verified", "verified"))
        self.assertEqual(counts["superseded"], 1)

    def test_older_deferred_never_sends_when_a_newer_record_is_past_deferred(self):
        for newer in ("verified", "possible-send", "refused", "not-applicable", "superseded"):
            with self.subTest(newer=newer):
                self.record(1, "deferred", merged_at="2026-10-31T09:00:00Z")
                self.record(2, newer, merged_at="2026-10-31T11:00:00Z")
                items = [board_item("In review")] if newer == "possible-send" else []
                api, _ = self.replay(items)
                self.assertEqual((api.sends, self.state(1), api.item_reads), ([], "superseded", len(items)))

    def test_record_without_merged_at_is_ordered_oldest(self):
        self.record(1, "deferred", merged_at=None)
        self.record(2, "deferred", merged_at="2026-10-31T09:00:00Z")
        api, _ = self.replay([board_item("In progress"), board_item("In review")])
        self.assertEqual((len(api.sends), self.state(1), self.state(2)), (1, "superseded", "verified"))

    def test_crash_between_supersession_and_possible_send_sends_at_most_once_for_the_newer(self):
        self.record(1, "deferred", merged_at="2026-10-31T09:00:00Z")
        self.record(2, "deferred", merged_at="2026-10-31T11:00:00Z")
        real = self.custody.write
        def crash(repo, pr, record):
            if record["state"] == "possible-send":
                raise KeyboardInterrupt("crash before possible-send")
            real(repo, pr, record)
        with mock.patch.object(self.custody, "write", side_effect=crash), self.assertRaises(KeyboardInterrupt), \
             contextlib.redirect_stderr(io.StringIO()):
            MODULE.board_replay(BoardApi([], body="", items=[board_item("In progress")]), self.custody, self.lock(wait=0))
        self.assertEqual((self.state(1), self.state(2)), ("superseded", "deferred"))
        api, _ = self.replay([board_item("In progress"), board_item("In review")])
        self.assertEqual((len(api.sends), self.state(1), self.state(2)), (1, "superseded", "verified"))

    def test_replay_readback_that_does_not_prove_the_target_stays_possible_send(self):
        for after in (board_item("In progress"), board_item("In review", values=['{"text":"Changed"}']), RuntimeError("read")):
            with self.subTest(after=after):
                self.record(1, "deferred")
                api, counts = self.replay([board_item("In progress"), after])
                self.assertEqual((len(api.sends), self.state(1), counts["uncertain"]), (1, "possible-send", 1))

    def test_lock_that_errors_refuses_the_write(self):
        broken = mock.Mock(acquire=mock.Mock(side_effect=OSError("no lock directory")))
        api = BoardApi([], body="", items=[board_item("In progress")])
        result = MODULE.board_transition(api, BOARD_RULES, "FS-GG/.github", board_body("Refs #900"), BOARD_DELIVERY,
                                         "enforce", 1, self.custody, broken)
        self.assertEqual((result, api.sends), ("refused-lock", []))
        self.assertIsNone(self.custody.existing("FS-GG/.github", 1))

    def test_uncertain_replay_send_keeps_possible_send_and_is_never_resent(self):
        self.record(1, "deferred")
        api, counts = self.replay([board_item("In progress")], send=RuntimeError("network"))
        self.assertEqual((len(api.sends), self.state(1), counts["uncertain"]), (1, "possible-send", 1))
        api, counts = self.replay([board_item("In progress")])  # the sweep reads back only
        self.assertEqual((api.sends, self.state(1), counts["swept-uncertain"]), ([], "possible-send", 1))

    # ---- the read-only sweep ----

    def test_sweep_reads_back_possible_send_records_and_never_sends(self):
        self.record(1, "possible-send")
        api, counts = self.replay([board_item("In review")])
        self.assertEqual((api.sends, self.state(1), counts["swept-verified"]), ([], "verified", 1))
        self.record(2, "possible-send")
        api, counts = self.replay([board_item("In progress")])
        self.assertEqual((api.sends, self.state(2), counts["swept-uncertain"]), ([], "possible-send", 1))

    def test_sweep_skips_the_current_prs_own_record(self):
        self.record(1, "possible-send")
        api, counts = self.replay([], current=("FS-GG/.github", 1))
        self.assertEqual((api.item_reads, self.state(1)), (0, "possible-send"))

    def test_busy_lock_at_start_of_run_replays_nothing(self):
        self.record(1, "deferred")
        holder = self.lock(wait=0)
        self.assertTrue(holder.acquire())
        self.addCleanup(holder.release)
        api, counts = self.replay([])
        self.assertEqual((api.item_reads, self.state(1), counts["lock-busy"]), (0, "deferred", 1))

    def test_no_pending_record_takes_no_lock_and_reads_nothing(self):
        self.record(1, "verified")
        lock = mock.Mock(wraps=self.lock(wait=0))
        api, counts = self.replay([], lock=lock)
        self.assertEqual((api.item_reads, lock.acquire.call_count), (0, 0))

    def test_enforce_invocation_replays_another_prs_deferred_record_first(self):
        self.record(7, "deferred", delivery="FS-GG/.github#901", item="PVTI_other")
        other = dict(board_item("In progress"), itemId="PVTI_other")
        before = board_item("In progress")
        pr = board_routine_pr(board_body("Refs #900"))
        api = BoardApi([pr, pr, merged()], [{"merged": True, "sha": MERGE}], body=board_body("Refs #900"),
                       items=[other, dict(other, status="In review"), before, before, board_item("In review")])
        with contextlib.redirect_stderr(io.StringIO()) as log, \
             mock.patch.object(MODULE, "board_enforce_allowed", return_value=True):
            code, result = MODULE.summarize(
                api, repo="FS-GG/.github", pr_number=1, expected_head=HEAD, merge_method="squash",
                publication_required=False, apply=True, board_gate="enforce",
                board_rules_loader=lambda: BOARD_RULES, board_custody=self.custody, board_lock=self.lock(wait=0))
        self.assertEqual((code, result.boardTransition), (0, "written"))
        self.assertEqual([send[1] for send in api.sends], ["PVTI_other", "PVTI_item"])
        self.assertEqual((self.state(7), self.state(1)), ("verified", "verified"))
        self.assertIn("fsgg routine board replay: verified=1", log.getvalue())
        self.assertNotIn("#901", log.getvalue())


if __name__ == "__main__":
    def inert(event, args):
        if event == "subprocess.Popen":
            executable, command, cwd, _env = args
            if (executable == GIT_FIXTURE_EXECUTABLE and isinstance(command, list)
                    and command[:3] == [GIT_FIXTURE_EXECUTABLE, "-c", "core.hooksPath=/dev/null"]
                    and command[3] in ("init", "add", "commit", "rev-parse", "ls-tree", "cat-file", "show", "diff", "merge-base")
                    and str(cwd) in GIT_FIXTURE_ROOTS):
                return
        if event in ("socket.connect", "subprocess.Popen", "os.system", "os.exec"):
            raise RuntimeError("offline delivery qualification refuses native effects")
    sys.addaudithook(inert)
    unittest.main()

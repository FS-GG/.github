#!/usr/bin/env python3
"""Hermetic native-observation and workflow-boundary checks."""
from __future__ import annotations

import copy
import hashlib
import base64
import importlib.util
import json
import pathlib
import re
import subprocess
import unittest
import tempfile
import shutil
import time
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("v2_ci_ordinary_observe", ROOT / "tools/v2-ci-ordinary-observe.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
SOURCE = "53a0f6c8f03bb8c4a60d55c3ce8a38c78a26b1b5"
HEAD = "353ff86a50808959770a73863385646efaf69969"
RETIRED_FIXTURE = ROOT / "tests/v2-ci-ordinary-observe/fixtures/retired-rehearsal"


class NativeObservationTests(unittest.TestCase):
    def setUp(self):
        self.env = {
            "GITHUB_SHA": SOURCE, "GITHUB_REPOSITORY": "FS-GG/.github",
            "GITHUB_EVENT_NAME": "push", "GITHUB_REF": "refs/heads/main",
            "EXPECTED_WORKFLOW_SHA": SOURCE,
            "GITHUB_WORKFLOW_REF": "FS-GG/.github/.github/workflows/v2-ci-ordinary-settlement.yml@refs/heads/main",
            "GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "1",
        }
        self.pull = {
            "number": 3662, "node_id": "PR_kwDOOrdinary3662", "state": "closed", "merged_at": "2026-09-24T13:09:45Z",
            "merge_commit_sha": SOURCE, "head": {"sha": HEAD},
            "base": {"ref": "main", "sha": "af5a748d075d6578300822c8b64251c7c85b3f91", "repo": {"full_name": "FS-GG/.github"}},
        }
        qualification = MODULE.QUALIFICATION.read_json(str(MODULE.POLICY_PATH))["qualification"]
        names = sorted(set(qualification["requiredChecks"] + qualification["requiredGateChecks"]))
        self.checks = {
            "total_count": len(names),
            "check_runs": [
                {"id": index + 1, "name": name, "status": "completed", "conclusion": "success",
                 "started_at": "2026-09-24T15:00:00Z", "head_sha": HEAD, "app": {"id": 15368}}
                for index, name in enumerate(names)
            ],
        }
        for check in self.checks["check_runs"]:
            self.native_identity(check)
        self.run_overrides = {}
        self.tree = "c" * 40
        self.parent = "b" * 40
        self.merged_tree = "e" * 40
        self.merge_base = self.pull["base"]["sha"]
        self.merge_status = 0
        self.current_policy = MODULE.POLICY_PATH.read_bytes()
        self.repository_identity = 1269292704
        self.live_checks = [{"context": name, "app_id": 15368}
                            for name in qualification["requiredGateChecks"]]

    def native_identity(self, check):
        check["check_suite"] = {"id": 2000 + check["id"]}
        check["details_url"] = (
            f"https://github.com/{self.env['GITHUB_REPOSITORY']}/actions/runs/"
            f"{1000 + check['id']}/job/{check['id']}")

    def select_audio(self):
        self.env["FSGG_V2_SOURCE_PROFILE"] = "audio-v1"
        self.env["GITHUB_REPOSITORY"] = "FS-GG/FS.GG.Audio"
        self.env["GITHUB_WORKFLOW_REF"] = (
            "FS-GG/FS.GG.Audio/.github/workflows/v2-ci-ordinary-settlement.yml@refs/heads/main")
        self.pull["base"]["repo"]["full_name"] = "FS-GG/FS.GG.Audio"
        self.repository_identity = 1292226968
        profile = MODULE.QUALIFICATION.AUDIO_SOURCE_PROFILE
        names = sorted(set(profile["requiredChecks"] + profile["requiredGateChecks"]))
        self.checks = {
            "total_count": len(names),
            "check_runs": [
                {"id": index + 1, "name": name, "status": "completed", "conclusion": "success",
                 "started_at": "2026-09-24T15:00:00Z", "head_sha": HEAD, "app": {"id": 15368}}
                for index, name in enumerate(names)
            ],
        }
        for check in self.checks["check_runs"]:
            self.native_identity(check)
        self.live_checks = [{"context": name, "app_id": 15368}
                            for name in profile["requiredGateChecks"]]

    def run_observation(self, rehearsal=False):
        def fake_run(args, **_kwargs):
            if args[:2] == ["git", "rev-parse"]:
                return subprocess.CompletedProcess(args, 0, SOURCE + "\n", "")
            if args[:4] == ["git", "-c", "credential.helper=", "fetch"]:
                return subprocess.CompletedProcess(args, 0, "", "")
            if args[:2] == ["git", "merge-tree"]:
                return subprocess.CompletedProcess(args, self.merge_status, self.merged_tree + "\n", "")
            path = args[-1]
            if path in ("repos/FS-GG/.github", "repos/FS-GG/FS.GG.Audio"):
                body = {"id": self.repository_identity,
                        "full_name": self.env["GITHUB_REPOSITORY"], "default_branch": "main"}
            elif path.endswith("/pulls?per_page=100"):
                body = [self.pull]
            elif path.endswith("/pulls/3662"):
                body = self.pull
            elif path.endswith("/check-runs?per_page=100"):
                body = self.checks
            elif path.endswith("/git/ref/heads/main"):
                body = {"object": {"sha": SOURCE}}
            elif path.endswith("/contents/policy/v2-ci-ordinary-settlement.json?ref=" + SOURCE):
                body = {"encoding": "base64", "content": base64.b64encode(self.current_policy).decode()}
            elif path.endswith("/contents/policy/v2-ci-ordinary-settlement-anchor.json?ref=" + SOURCE):
                anchor = (ROOT / "policy/v2-ci-ordinary-settlement-anchor.json").read_bytes()
                body = {"encoding": "base64", "content": base64.b64encode(anchor).decode()}
            elif path.endswith("/contents/.github/workflows/v2-ci-ordinary-settlement.yml?ref=" + SOURCE):
                body = {"encoding": "base64", "content": base64.b64encode((ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_bytes()).decode()}
            elif path.endswith("/branches/main"):
                body = {"protected": True, "protection": {"required_status_checks": {"checks": self.live_checks}}}
            elif path.endswith(f"/compare/{self.parent}...{HEAD}"):
                body = {"base_commit": {"sha": self.parent}, "merge_base_commit": {"sha": self.merge_base}}
            elif match := re.search(r"/actions/runs/(\d+)$", path):
                check = next(x for x in self.checks["check_runs"] if 1000 + x["id"] == int(match.group(1)))
                policy = MODULE.QUALIFICATION.read_json(str(MODULE.POLICY_PATH))
                profile = MODULE.QUALIFICATION.source_profile(
                    policy, self.env.get("FSGG_V2_SOURCE_PROFILE", ""))
                producer = profile["checkProducers"][check["name"]]
                body = {"id": 1000 + check["id"], "workflow_id": producer["workflowId"],
                        "path": producer["path"], "event": producer["event"], "head_sha": HEAD,
                        "check_suite_id": 2000 + check["id"],
                        "repository": {"full_name": self.env["GITHUB_REPOSITORY"]},
                        "run_attempt": 1}
                body.update(self.run_overrides.get(1000 + check["id"], {}))
            elif match := re.search(r"/actions/jobs/(\d+)$", path):
                check = next(x for x in self.checks["check_runs"] if x["id"] == int(match.group(1)))
                body = {"id": check["id"], "run_id": 1000 + check["id"], "head_sha": HEAD,
                        "name": check["name"], "run_attempt": 1, "status": check["status"],
                        "conclusion": check["conclusion"],
                        "check_run_url": (
                            f"https://api.github.com/repos/{self.env['GITHUB_REPOSITORY']}"
                            f"/check-runs/{check['id']}")}
            elif "/git/commits/" in path:
                body = {"sha": path.rsplit("/", 1)[1], "tree": {"sha": self.tree if path.endswith(SOURCE) else getattr(self, "head_tree", self.tree)}}
                if path.endswith(SOURCE):
                    body["parents"] = [{"sha": self.parent}]
            else:
                raise AssertionError(path)
            return subprocess.CompletedProcess(args, 0, json.dumps(body), "")
        def fake_graph(_repo, _head, _source, _parent, base, _trees):
            if base != getattr(self, "expected_graph_base", self.pull["base"]["sha"]):
                raise MODULE.QUALIFICATION.Refusal("native merge base disagrees")
            if self.merge_status:
                raise MODULE.QUALIFICATION.Refusal("does not merge cleanly")
            if self.merged_tree != self.tree:
                raise MODULE.QUALIFICATION.Refusal("qualified three-way merge differs")
            return self.tree
        with patch.object(MODULE.subprocess, "run", side_effect=fake_run), patch.object(MODULE, "prove_graph", side_effect=fake_graph):
            return MODULE.observe(self.env, rehearsal=rehearsal)

    def test_native_commit_parent_fast_path_and_final_pr_movement_refuse(self):
        original_api = MODULE.api
        def invalid_parent(path):
            body = original_api(path)
            if path.endswith("/git/commits/" + SOURCE):
                body["parents"] = []
            return body
        with patch.object(MODULE, "api", side_effect=invalid_parent):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "unique squash parent"):
                self.run_observation()
        calls = 0
        def moving_pull(path):
            nonlocal calls
            body = original_api(path)
            if path.endswith("/pulls/3662"):
                calls += 1
                if calls == 2:
                    body = copy.deepcopy(body)
                    body["base"]["sha"] = "f" * 40
            return body
        with patch.object(MODULE, "api", side_effect=moving_pull):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "during graph/check"):
                self.run_observation()

    def test_current_target_base_is_distinct_from_graph_base(self):
        self.head_tree = "d" * 40
        self.merged_tree = self.tree
        self.expected_graph_base = self.merge_base
        self.pull["base"]["sha"] = self.parent
        receipt = self.run_observation()
        self.assertEqual(self.parent, receipt["pullRequestBaseSha"])
        self.assertEqual(HEAD, receipt["qualificationSha"])
        self.assertEqual(self.tree, receipt["qualifiedTreeSha"])
        self.pull["base"]["sha"] = "f" * 40
        receipt = self.run_observation()
        self.assertEqual("f" * 40, receipt["pullRequestBaseSha"])

    def test_native_commit_and_compare_identity_contradictions_refuse(self):
        self.head_tree = "d" * 40
        original_api = MODULE.api
        for kind in ("head-identity", "source-tree", "compare-parent", "compare-base"):
            with self.subTest(kind=kind):
                def corrupt(path):
                    body = original_api(path)
                    if kind == "head-identity" and path.endswith("/git/commits/" + HEAD):
                        body["sha"] = "f" * 40
                    if kind == "source-tree" and path.endswith("/git/commits/" + SOURCE):
                        body["tree"]["sha"] = "invalid"
                    if "/compare/" in path:
                        if kind == "compare-parent":
                            body["base_commit"]["sha"] = "f" * 40
                        if kind == "compare-base":
                            body["merge_base_commit"]["sha"] = "invalid"
                    return body
                with patch.object(MODULE, "api", side_effect=corrupt):
                    with self.assertRaises(MODULE.QUALIFICATION.Refusal):
                        self.run_observation()

    def test_produce_verify_recomputes_exact_source_tree_receipt(self):
        self.head_tree = "d" * 40
        self.merged_tree = self.tree
        receipt = self.run_observation()
        with tempfile.TemporaryDirectory() as root:
            target = pathlib.Path(root, "receipt.json")
            with patch.object(MODULE, "observe", return_value=receipt):
                with patch.object(MODULE.sys, "argv", ["observer", "produce", str(target)]):
                    self.assertEqual(0, MODULE.main())
                with patch.object(MODULE.sys, "argv", ["observer", "verify", str(target)]):
                    self.assertEqual(0, MODULE.main())
                for field in ("qualifiedTreeSha", "pullRequestBaseSha"):
                    altered = dict(receipt, **{field: "f" * 40})
                    target.write_text(json.dumps(altered))
                    with patch.object(MODULE.sys, "argv", ["observer", "verify", str(target)]):
                        self.assertEqual(3, MODULE.main())
            target.unlink()
            with patch.object(MODULE, "observe", side_effect=MODULE.QUALIFICATION.Refusal("unqualified")):
                with patch.object(MODULE.sys, "argv", ["observer", "produce", str(target)]):
                    self.assertEqual(3, MODULE.main())
            self.assertFalse(target.exists())

    def test_native_success_is_run_and_pr_head_bound_and_active(self):
        receipt = self.run_observation()
        self.assertEqual(SOURCE, receipt["sourceSha"])
        self.assertEqual("dotgithub-v1", receipt["sourceProfile"])
        self.assertEqual(1269292704, receipt["sourceRepositoryId"])
        self.assertEqual(HEAD, receipt["qualificationSha"])
        self.assertEqual("PR_kwDOOrdinary3662", receipt["pullRequestNodeId"])
        self.assertEqual(123, receipt["runId"])
        self.assertTrue(receipt["activation"])
        self.assertEqual(self.tree, receipt["qualifiedTreeSha"])
        self.assertEqual(2, len(receipt["requiredChecks"]))
        self.assertEqual(8, len(receipt["requiredGateChecks"]))

    def test_audio_profile_binds_fixed_repository_and_exact_native_check_policy(self):
        self.select_audio()
        receipt = self.run_observation()
        self.assertEqual("audio-v1", receipt["sourceProfile"])
        self.assertEqual("FS-GG/FS.GG.Audio", receipt["sourceRepository"])
        self.assertEqual(1292226968, receipt["sourceRepositoryId"])
        self.assertFalse(receipt["activation"])
        self.assertEqual(
            set(MODULE.QUALIFICATION.AUDIO_SOURCE_PROFILE["requiredChecks"]),
            {check["name"] for check in receipt["requiredChecks"]},
        )
        self.assertEqual(
            set(MODULE.QUALIFICATION.AUDIO_SOURCE_PROFILE["requiredGateChecks"]),
            {check["name"] for check in receipt["requiredGateChecks"]},
        )
        self.assertEqual(
            {
                "Build + test (locked restore, net10.0, headless)": 308685627,
                "lock-ranges / lock-ranges": 308685627,
                "kit / coordination-kit": 310136059,
                "materialize / receiver-validate": 316870238,
                "routine-eligibility": 357682791,
            },
            {name: producer["workflowId"] for name, producer in
             MODULE.QUALIFICATION.AUDIO_SOURCE_PROFILE["checkProducers"].items()},
        )

    def test_audio_profile_refuses_wrong_identity_unknown_selector_and_rehearsal(self):
        self.select_audio()
        self.repository_identity = 1269292704
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "repository identity differs"):
            self.run_observation()
        self.repository_identity = 1292226968
        self.env["FSGG_V2_SOURCE_PROFILE"] = "caller-supplied"
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "unknown ordinary-v2 source profile"):
            self.run_observation()
        self.env["FSGG_V2_SOURCE_PROFILE"] = "audio-v1"
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "no rehearsal activation"):
            self.run_observation(rehearsal=True)

    def test_no_request_event_or_stale_workflow_revision(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request"
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "pinned protected-main event"):
            self.run_observation()
        self.env["GITHUB_EVENT_NAME"] = "push"
        self.env["EXPECTED_WORKFLOW_SHA"] = "a" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "workflow revision"):
            self.run_observation()

    def test_ambiguous_association_stale_check_and_wrong_app_refuse(self):
        self.pull["merge_commit_sha"] = "a" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "associated merge commit"):
            self.run_observation()
        self.pull["merge_commit_sha"] = SOURCE
        self.checks["check_runs"][0]["head_sha"] = "b" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "stale"):
            self.run_observation()
        self.checks["check_runs"][0]["head_sha"] = HEAD
        self.checks["check_runs"][0]["app"]["id"] = 99
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "wrong-app"):
            self.run_observation()

    def test_duplicate_successful_check_runs_are_valid_but_any_failure_refuses(self):
        original = next(check for check in self.checks["check_runs"] if check["name"] == "routine-eligibility")
        duplicate = copy.deepcopy(original)
        duplicate["id"] = 100
        duplicate["started_at"] = "2026-09-24T15:01:00Z"
        self.native_identity(duplicate)
        self.checks["check_runs"].append(duplicate)
        self.checks["total_count"] += 1
        self.assertEqual("qualified", self.run_observation()["status"])
        duplicate["conclusion"] = "failure"
        self.checks["check_runs"][-1] = duplicate
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "not successful"):
            self.run_observation()
        duplicate["conclusion"] = "success"
        self.checks["check_runs"][-1] = duplicate
        original["conclusion"] = "failure"
        self.assertEqual("qualified", self.run_observation()["status"])

    def test_unrelated_workflow_cannot_supersede_a_failed_check(self):
        original = next(check for check in self.checks["check_runs"] if check["name"] == "routine-eligibility")
        original["conclusion"] = "failure"
        replacement = copy.deepcopy(original)
        replacement["id"] = 100
        replacement["conclusion"] = "success"
        replacement["started_at"] = "2026-09-24T15:01:00Z"
        self.native_identity(replacement)
        self.checks["check_runs"].append(replacement)
        self.checks["total_count"] += 1
        self.run_overrides[1100] = {"path": ".github/workflows/unrelated.yml"}
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "unexpected workflow"):
            self.run_observation()

    def test_incomplete_check_population_refuses(self):
        self.checks["total_count"] += 1
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "incomplete native check-run"):
            self.run_observation()

    def test_live_gate_drift_and_merged_tree_difference_refuse(self):
        self.live_checks.append({"context": "new-required-gate", "app_id": 15368})
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "population differs"):
            self.run_observation()
        self.live_checks.pop()
        self.head_tree = "d" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "qualified three-way merge"):
            self.run_observation()

    def test_concurrent_main_change_accepts_only_exact_clean_merge(self):
        self.head_tree = "d" * 40
        self.merged_tree = self.tree
        receipt = self.run_observation()
        self.assertEqual(self.tree, receipt["qualifiedTreeSha"])
        self.merged_tree = "e" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "qualified three-way merge"):
            self.run_observation()
        self.merged_tree = self.tree
        self.merge_base = "f" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "native merge base"):
            self.run_observation()
        self.merge_base = self.pull["base"]["sha"]
        self.merge_status = 1
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "does not merge cleanly"):
            self.run_observation()

    def test_current_policy_revocation_refuses_old_run(self):
        policy = json.loads(self.current_policy)
        policy["status"] = "revoked"
        self.current_policy = json.dumps(policy, indent=2).encode() + b"\n"
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "current protected authority changed"):
            self.run_observation()

    def test_historical_rehearsal_fixture_uses_distinct_dispatch_policy_and_environment(self):
        self.env["GITHUB_EVENT_NAME"] = "workflow_dispatch"
        self.env["GITHUB_WORKFLOW_REF"] = (
            "FS-GG/.github/.github/workflows/v2-ci-ordinary-rehearsal.yml@refs/heads/main")
        original_read = MODULE.QUALIFICATION.read_json
        def historical_policy(path):
            if pathlib.Path(path) == MODULE.REHEARSAL_POLICY_PATH:
                return original_read(str(RETIRED_FIXTURE / "v2-ci-ordinary-settlement-rehearsal.json"))
            return original_read(path)
        with patch.object(MODULE.QUALIFICATION, "read_json", side_effect=historical_policy), patch.object(MODULE, "current_authority", return_value=None):
            receipt = self.run_observation(rehearsal=True)
        self.assertEqual("v2-ci-i1-ordinary-settlement-rehearsal-v1", receipt["policyId"])
        self.assertEqual("ordinary-v2-rehearsal", receipt["environment"])
        self.assertTrue(receipt["activation"])
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "pinned protected-main event"):
            self.run_observation()

    def test_retired_live_rehearsal_refuses_before_native_observation(self):
        policy = MODULE.QUALIFICATION.read_json(str(MODULE.REHEARSAL_POLICY_PATH))
        self.assertEqual("retired", policy["status"])
        self.assertFalse(policy["credentialJob"]["installed"])
        self.env["GITHUB_EVENT_NAME"] = "workflow_dispatch"
        with patch.object(MODULE, "api") as api:
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "unexpected protected repository, workflow or activation"):
                self.run_observation(rehearsal=True)
        api.assert_not_called()

    def test_retired_current_policy_fences_old_rehearsal_checkout(self):
        historical = (RETIRED_FIXTURE / "v2-ci-ordinary-settlement-rehearsal.json").read_bytes()
        with tempfile.TemporaryDirectory() as directory:
            old_root = pathlib.Path(directory)
            old_policy_path = old_root / "policy/v2-ci-ordinary-settlement-rehearsal.json"
            old_policy_path.parent.mkdir()
            old_policy_path.write_bytes(historical)
            with patch.object(MODULE, "ROOT", old_root), patch.object(MODULE, "api", return_value={"object": {"sha": SOURCE}}), patch.object(MODULE, "current_file", return_value=MODULE.REHEARSAL_POLICY_PATH.read_bytes()):
                with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "current protected authority changed: policy/v2-ci-ordinary-settlement-rehearsal.json"):
                    MODULE.current_authority("FS-GG/.github", json.loads(historical), old_policy_path)

    def test_unavailable_native_api_refuses_without_treating_403_as_absence(self):
        unavailable = subprocess.CompletedProcess(["gh", "api"], 1, "", "HTTP 403")
        with patch.object(MODULE.subprocess, "run", return_value=unavailable) as run:
            with self.assertRaisesRegex(
                MODULE.QUALIFICATION.Refusal,
                r"native GitHub evidence unavailable: repos/FS-GG/\.github/pulls/3662 \(HTTP 403\)",
            ):
                MODULE.api("repos/FS-GG/.github/pulls/3662")
        self.assertEqual(1, run.call_count)

    def test_native_api_retries_a_transient_process_failure(self):
        unavailable = subprocess.CompletedProcess(["gh", "api"], 1, "", "temporary failure")
        available = subprocess.CompletedProcess(["gh", "api"], 0, "{}", "")
        with patch.object(MODULE.subprocess, "run", side_effect=[unavailable, available]) as run:
            self.assertEqual({}, MODULE.api("repos/FS-GG/.github/pulls/3662"))
        self.assertEqual(2, run.call_count)

    def test_native_api_retries_server_failure_then_keeps_sanitized_status(self):
        unavailable = subprocess.CompletedProcess(["gh", "api"], 1, "", "upstream failed (HTTP 503)")
        with patch.object(MODULE.subprocess, "run", return_value=unavailable) as run:
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, r"\(HTTP 503\)"):
                MODULE.api("repos/FS-GG/.github/pulls/3662")
        self.assertEqual(3, run.call_count)

    def test_native_api_retries_rate_limit_but_not_permanent_forbidden(self):
        limited = subprocess.CompletedProcess(["gh", "api"], 1, "", "API rate limit exceeded (HTTP 403)")
        available = subprocess.CompletedProcess(["gh", "api"], 0, "{}", "")
        with patch.object(MODULE.subprocess, "run", side_effect=[limited, available]) as run:
            self.assertEqual({}, MODULE.api("repos/FS-GG/.github/pulls/3662"))
        self.assertEqual(2, run.call_count)

    def test_workflow_has_no_manual_or_pr_trigger_and_settlement_needs_receipt(self):
        workflow = (ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_text()
        self.assertIn("  push:\n    branches: [main]", workflow)
        for forbidden in ("workflow_dispatch:", "repository_dispatch:", "pull_request:", "pull_request_target:"):
            self.assertNotIn(forbidden, workflow)
        self.assertIn("needs: [preflight]", workflow)
        self.assertIn("if: needs.preflight.outputs.activation == 'true'", workflow)
        self.assertIn("environment: ordinary-v2", workflow)
        self.assertIn("  checks: read\n  pull-requests: read", workflow)
        self.assertIn("  actions: read", workflow)
        self.assertIn("python3 tools/v2-ci-ordinary-observe.py verify", workflow)
        self.assertIn("ordinary-settlement execute", workflow)
        self.assertIn("V2_ORDINARY_APP_PRIVATE_KEY: ${{ secrets.V2_ORDINARY_APP_PRIVATE_KEY }}", workflow)
        self.assertIn("PACKAGE_VERSION: 0.1.2", workflow)
        self.assertIn("PACKAGE_SHA256: 627d9f54d038d47ef59635f92bd4fd4af2da7bfc971a938b6de1292503b0307e", workflow)
        self.assertNotIn("__SERVED_SHA256__", workflow)
        self.assertIn('test "$(sha256sum "$package" | cut -d \' \' -f 1)" = "$PACKAGE_SHA256"', workflow)
        self.assertNotIn("secrets.", workflow.split("  preflight:", 1)[1].split("  settle:", 1)[0])

    def test_historical_workflow_is_inert_byte_preserved_and_uses_distinct_custody(self):
        self.assertFalse((ROOT / ".github/workflows/v2-ci-ordinary-rehearsal.yml").exists())
        historical = RETIRED_FIXTURE / "v2-ci-ordinary-rehearsal.yml"
        self.assertEqual("58781a61d50d808f4aa5c90a683ac0cefdc407f5330fb6368319010ce9f4ac54", hashlib.sha256(historical.read_bytes()).hexdigest())
        workflow = historical.read_text()
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertNotIn("  push:", workflow)
        self.assertIn("needs: [preflight]", workflow)
        self.assertIn("if: needs.preflight.outputs.activation == 'true'", workflow)
        self.assertIn("environment: ordinary-v2-rehearsal", workflow)
        self.assertIn("produce-rehearsal", workflow)
        self.assertIn("verify-rehearsal", workflow)
        self.assertIn("ordinary-settlement rehearse", workflow)
        self.assertIn("V2_ORDINARY_REHEARSAL_APP_PRIVATE_KEY: ${{ secrets.V2_ORDINARY_REHEARSAL_APP_PRIVATE_KEY }}", workflow)
        self.assertNotIn("V2_ORDINARY_APP_PRIVATE_KEY: ${{ secrets.V2_ORDINARY_APP_PRIVATE_KEY }}", workflow)
        self.assertNotIn("secrets.", workflow.split("  preflight:", 1)[1].split("  rehearse:", 1)[0])
        production = (ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_text()
        for key in ("PACKAGE_VERSION", "PACKAGE_SHA256"):
            pattern = rf"^      {key}: (.+)$"
            self.assertEqual(re.search(pattern, production, re.MULTILINE).group(1),
                             re.search(pattern, workflow, re.MULTILINE).group(1))



class RealGraphTests(unittest.TestCase):
    """Actual local object graphs; only the fixed fetch URL is replaced."""
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.repo = pathlib.Path(self.directory.name, "source")
        self.repo.mkdir()
        self.run = subprocess.run
        self.command("init", "--bare", "--quiet")
        self.env = dict(MODULE.os.environ, GIT_AUTHOR_NAME="fixture", GIT_AUTHOR_EMAIL="a@example.invalid",
                        GIT_COMMITTER_NAME="fixture", GIT_COMMITTER_EMAIL="a@example.invalid")
        self.root = self.commit(self.tree({}), [])
        self.base = self.commit(self.tree({"base": "base"}), [self.root])
        self.parent = self.commit(self.tree({"base": "base", "p": "p"}), [self.base])
        self.head = self.commit(self.tree({"base": "base", "h": "h"}), [self.base])
        merged = self.command("merge-tree", "--write-tree", self.parent, self.head)
        self.source = self.commit(merged, [self.parent])
        self.depth = "256"
        self.directories = []

    def tearDown(self):
        for path in self.directories:
            self.assertFalse(pathlib.Path(path).exists(), "owned temporary graph survived")
        self.directory.cleanup()

    def command(self, *args, input=None):
        r = self.run(["git", "-C", str(self.repo), *args], input=input, capture_output=True,
                     text=True, env=getattr(self, "env", None), timeout=5)
        if r.returncode:
            raise AssertionError(r.stderr)
        return r.stdout.strip()

    def tree(self, contents):
        lines = []
        for name, body in sorted(contents.items()):
            oid = self.command("hash-object", "-w", "--stdin", input=body)
            lines.append(f"100644 blob {oid}\t{name}\n")
        return self.command("mktree", input="".join(lines))

    def commit(self, tree, parents):
        return self.command("commit-tree", tree, *[x for p in parents for x in ("-p", p)], input="fixture\n")

    def proof(self, base=None):
        base = base or self.base
        trees = {x: self.command("rev-parse", x + "^{tree}")
                 for x in (self.parent, self.head, self.source, base)}
        def transport(args, **kwargs):
            args = list(args)
            if "fetch" in args:
                directory = pathlib.Path(args[args.index("-C") + 1])
                self.directories.append(str(directory))
                self.assertIn("https://github.com/FS-GG/.github.git", args)
                self.assertIn("--depth=256", args)
                shutil.copytree(self.repo / "objects", directory / "objects", dirs_exist_ok=True)
                # Native shallow metadata on real objects, with the same finite
                # ancestry cutoff a depth-limited transport supplies.
                distances = {}
                queue = [(oid, 0) for oid in (self.parent, self.head, base, self.source)]
                while queue:
                    oid, distance = queue.pop(0)
                    if oid in distances and distances[oid] <= distance:
                        continue
                    distances[oid] = distance
                    if distance < int(self.depth) - 1:
                        queue.extend((parent, distance + 1) for parent in
                                     self.command("show", "-s", "--format=%P", oid).split())
                boundaries = [oid for oid, distance in distances.items()
                              if distance == int(self.depth) - 1
                              and self.command("show", "-s", "--format=%P", oid)]
                if boundaries:
                    (directory / "shallow").write_text("\n".join(boundaries) + "\n")
                return subprocess.CompletedProcess(args, 0)
            return native_graph_run(args, **kwargs)
        native_graph_run = MODULE.graph_run
        with patch.object(MODULE, "graph_run", side_effect=transport):
            return MODULE.prove_graph("FS-GG/.github", self.head, self.source, self.parent, base, trees)

    def test_real_concurrent_clean_squash_returns_source_tree(self):
        self.assertEqual(self.command("rev-parse", self.source + "^{tree}"), self.proof())
        self.assertNotEqual(self.command("rev-parse", self.head + "^{tree}"), self.proof())

    def test_shallow_only_at_common_base_is_complete_enough(self):
        self.depth = "1"
        # Fetching B alone at depth1 does not prove P/H ancestry: must refuse.
        with self.assertRaises(MODULE.QUALIFICATION.Refusal):
            self.proof()
        self.depth = "2"
        self.assertEqual(self.command("rev-parse", self.source + "^{tree}"), self.proof())

    def test_shallow_boundary_above_base_refuses(self):
        self.parent = self.commit(self.command("rev-parse", self.parent + "^{tree}"), [self.parent])
        self.parent = self.commit(self.command("rev-parse", self.parent + "^{tree}"), [self.parent])
        self.source = self.commit(self.command("rev-parse", self.source + "^{tree}"), [self.parent])
        self.depth = "2"
        with self.assertRaises(MODULE.QUALIFICATION.Refusal):
            self.proof()

    def test_wrong_base_extra_source_and_conflict_refuse(self):
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "bases differ"):
            self.proof(self.root)
        self.source = self.commit(self.tree({"extra": "unauthorized"}), [self.parent])
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "three-way merge"):
            self.proof()
        self.head = self.commit(self.tree({"base": "changed"}), [self.base])
        self.parent = self.commit(self.tree({"base": "conflict"}), [self.base])
        self.source = self.commit(self.tree({}), [self.parent])
        with self.assertRaises(MODULE.QUALIFICATION.Refusal):
            self.proof()

    def test_criss_cross_two_maximal_bases_refuse(self):
        a, b = self.parent, self.head
        self.parent = self.commit(self.tree({}), [a, b])
        self.head = self.commit(self.tree({"later": "head"}), [b, a])
        self.source = self.commit(self.tree({"later": "head", "extra": "source"}), [self.parent])
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "not unique"):
            self.proof(a)

    def test_actual_owned_command_timeout_reaps_direct_child(self):
        with tempfile.TemporaryFile() as output:
            with self.assertRaises(subprocess.TimeoutExpired):
                MODULE.graph_run(["python3", "-c", "import time;time.sleep(5)"],
                                 stdout=output, stderr=output, timeout=0.02)

    def test_zero_common_bases_and_native_object_disagreement_refuse(self):
        self.head = self.commit(self.tree({"unrelated": "root"}), [])
        with self.assertRaises(MODULE.QUALIFICATION.Refusal):
            self.proof()
        trees = {x: self.command("rev-parse", x + "^{tree}") for x in (self.parent, self.head, self.source, self.base)}
        trees[self.source] = "f" * 40
        def no_fetch(args, **kwargs):
            if "fetch" in args:
                directory = pathlib.Path(args[args.index("-C") + 1])
                self.directories.append(str(directory))
                shutil.copytree(self.repo / "objects", directory / "objects", dirs_exist_ok=True)
                return subprocess.CompletedProcess(args, 0)
            return native_run(args, **kwargs)
        native_run = MODULE.graph_run
        with patch.object(MODULE, "graph_run", side_effect=no_fetch):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "native tree differs"):
                MODULE.prove_graph("FS-GG/.github", self.head, self.source, self.parent, self.base, trees)

    def test_shared_graph_deadline_refuses_before_launch(self):
        trees = {x: self.command("rev-parse", x + "^{tree}") for x in (self.parent, self.head, self.source, self.base)}
        with patch.object(MODULE.time, "monotonic", side_effect=[0, 121]):
            with patch.object(MODULE, "graph_run") as launch:
                with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "deadline exhausted"):
                    MODULE.prove_graph("FS-GG/.github", self.head, self.source, self.parent, self.base, trees)
                launch.assert_not_called()

    def test_timeout_and_overbound_output_refuse_with_cleanup(self):
        def timeout(*args, **kwargs):
            raise subprocess.TimeoutExpired(args[0], 1)
        trees = {x: self.command("rev-parse", x + "^{tree}") for x in (self.parent, self.head, self.source, self.base)}
        with patch.object(MODULE, "graph_run", side_effect=timeout):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "timed out"):
                MODULE.prove_graph("FS-GG/.github", self.head, self.source, self.parent, self.base, trees)
        def oversized(args, **kwargs):
            kwargs["stdout"].write(b"x" * 65537)
            return subprocess.CompletedProcess(args, 0)
        trees = {x: self.command("rev-parse", x + "^{tree}") for x in (self.parent, self.head, self.source, self.base)}
        with patch.object(MODULE, "graph_run", side_effect=oversized):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "output exceeds"):
                MODULE.prove_graph("FS-GG/.github", self.head, self.source, self.parent, self.base, trees)

if __name__ == "__main__":
    unittest.main()

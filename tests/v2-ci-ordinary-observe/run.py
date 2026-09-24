#!/usr/bin/env python3
"""Hermetic native-observation and workflow-boundary checks."""
from __future__ import annotations

import copy
import base64
import importlib.util
import json
import pathlib
import subprocess
import unittest
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("v2_ci_ordinary_observe", ROOT / "tools/v2-ci-ordinary-observe.py")
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)
SOURCE = "53a0f6c8f03bb8c4a60d55c3ce8a38c78a26b1b5"
HEAD = "353ff86a50808959770a73863385646efaf69969"


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
        self.checks = {
            "total_count": 8,
            "check_runs": [
                {"name": name, "conclusion": "success", "head_sha": HEAD, "app": {"id": 15368}}
                for name in MODULE.QUALIFICATION.read_json(str(MODULE.POLICY_PATH))["qualification"]["requiredChecks"]
            ],
        }
        self.tree = "c" * 40
        self.current_policy = MODULE.POLICY_PATH.read_bytes()
        self.live_checks = [{"context": check["name"], "app_id": 15368}
                            for check in self.checks["check_runs"]]

    def run_observation(self):
        def fake_run(args, **_kwargs):
            if args[:2] == ["git", "rev-parse"]:
                return subprocess.CompletedProcess(args, 0, SOURCE + "\n", "")
            path = args[-1]
            if path.endswith("/pulls?per_page=100"):
                body = [self.pull]
            elif path.endswith("/pulls/3662"):
                body = self.pull
            elif path.endswith("/check-runs?per_page=100"):
                body = self.checks
            elif path.endswith("/git/ref/heads/main"):
                body = {"object": {"sha": SOURCE}}
            elif path.endswith("/contents/policy/v2-ci-ordinary-settlement.json?ref=" + SOURCE):
                body = {"encoding": "base64", "content": base64.b64encode(self.current_policy).decode()}
            elif path.endswith("/contents/.github/workflows/v2-ci-ordinary-settlement.yml?ref=" + SOURCE):
                body = {"encoding": "base64", "content": base64.b64encode((ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_bytes()).decode()}
            elif path.endswith("/branches/main"):
                body = {"protected": True, "protection": {"required_status_checks": {"checks": self.live_checks}}}
            elif path.endswith("/git/commits/" + HEAD) or path.endswith("/git/commits/" + SOURCE):
                body = {"tree": {"sha": self.tree if path.endswith(SOURCE) else getattr(self, "head_tree", self.tree)}}
            else:
                raise AssertionError(path)
            return subprocess.CompletedProcess(args, 0, json.dumps(body), "")
        with patch.object(MODULE.subprocess, "run", side_effect=fake_run):
            return MODULE.observe(self.env)

    def test_native_success_is_run_and_pr_head_bound_but_inactive(self):
        receipt = self.run_observation()
        self.assertEqual(SOURCE, receipt["sourceSha"])
        self.assertEqual(HEAD, receipt["qualificationSha"])
        self.assertEqual("PR_kwDOOrdinary3662", receipt["pullRequestNodeId"])
        self.assertEqual(123, receipt["runId"])
        self.assertFalse(receipt["activation"])
        self.assertEqual(self.tree, receipt["qualifiedTreeSha"])

    def test_no_request_event_or_stale_workflow_revision(self):
        self.env["GITHUB_EVENT_NAME"] = "pull_request"
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "protected-main push"):
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
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "failed, stale"):
            self.run_observation()
        self.checks["check_runs"][0]["head_sha"] = HEAD
        self.checks["check_runs"][0]["app"]["id"] = 99
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "wrong-app"):
            self.run_observation()

    def test_duplicate_successful_check_runs_are_valid_but_any_failure_refuses(self):
        duplicate = copy.deepcopy(self.checks["check_runs"][0])
        self.checks["check_runs"].append(duplicate)
        self.checks["total_count"] += 1
        self.assertEqual("qualified", self.run_observation()["status"])
        duplicate["conclusion"] = "failure"
        self.checks["check_runs"][-1] = duplicate
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "failed"):
            self.run_observation()

    def test_incomplete_check_population_refuses(self):
        self.checks["total_count"] = 3
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "incomplete native check-run"):
            self.run_observation()

    def test_live_gate_drift_and_merged_tree_difference_refuse(self):
        self.live_checks.append({"context": "new-required-gate", "app_id": 15368})
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "population differs"):
            self.run_observation()
        self.live_checks.pop()
        self.head_tree = "d" * 40
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "merged source tree differs"):
            self.run_observation()

    def test_current_policy_revocation_refuses_old_run(self):
        self.current_policy = self.current_policy.replace(b"source-qualified-not-installed", b"revoked")
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "current protected authority changed"):
            self.run_observation()

    def test_unavailable_native_api_refuses_without_treating_403_as_absence(self):
        unavailable = subprocess.CompletedProcess(["gh", "api"], 1, "", "HTTP 403")
        with patch.object(MODULE.subprocess, "run", return_value=unavailable):
            with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "native GitHub evidence unavailable"):
                MODULE.api("repos/FS-GG/.github/pulls/3662")

    def test_workflow_has_no_manual_or_pr_trigger_and_settlement_needs_receipt(self):
        workflow = (ROOT / ".github/workflows/v2-ci-ordinary-settlement.yml").read_text()
        self.assertIn("  push:\n    branches: [main]", workflow)
        for forbidden in ("workflow_dispatch:", "repository_dispatch:", "pull_request:", "pull_request_target:"):
            self.assertNotIn(forbidden, workflow)
        self.assertIn("needs: [preflight]", workflow)
        self.assertIn("if: needs.preflight.outputs.activation == 'true'", workflow)
        self.assertIn("environment: ordinary-v2", workflow)
        self.assertIn("  checks: read\n  pull-requests: read", workflow)
        self.assertNotIn("  actions: read", workflow)
        self.assertIn("python3 tools/v2-ci-ordinary-observe.py verify", workflow)
        self.assertIn("exit 3", workflow)


if __name__ == "__main__":
    unittest.main()

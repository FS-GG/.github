#!/usr/bin/env python3
"""Hermetic native-observation and workflow-boundary checks."""
from __future__ import annotations

import copy
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
            "number": 3662, "state": "closed", "merged_at": "2026-09-24T13:09:45Z",
            "merge_commit_sha": SOURCE, "head": {"sha": HEAD},
            "base": {"ref": "main", "repo": {"full_name": "FS-GG/.github"}},
        }
        self.checks = {
            "total_count": 2,
            "check_runs": [
                {"name": "contract-coherence / coherence", "conclusion": "success",
                 "head_sha": HEAD, "app": {"id": 15368}},
                {"name": "routine-eligibility", "conclusion": "success",
                 "head_sha": HEAD, "app": {"id": 15368}},
            ],
        }

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
            else:
                raise AssertionError(path)
            return subprocess.CompletedProcess(args, 0, json.dumps(body), "")
        with patch.object(MODULE.subprocess, "run", side_effect=fake_run):
            return MODULE.observe(self.env)

    def test_native_success_is_run_and_pr_head_bound_but_inactive(self):
        receipt = self.run_observation()
        self.assertEqual(SOURCE, receipt["sourceSha"])
        self.assertEqual(HEAD, receipt["qualificationSha"])
        self.assertEqual(123, receipt["runId"])
        self.assertFalse(receipt["activation"])

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
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "failed or stale"):
            self.run_observation()
        self.checks["check_runs"][0]["head_sha"] = HEAD
        self.checks["check_runs"][0]["app"]["id"] = 99
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "wrong-app"):
            self.run_observation()

    def test_incomplete_check_population_refuses(self):
        self.checks["total_count"] = 3
        with self.assertRaisesRegex(MODULE.QUALIFICATION.Refusal, "incomplete native check-run"):
            self.run_observation()

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

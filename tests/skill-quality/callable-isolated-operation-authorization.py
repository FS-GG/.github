#!/usr/bin/env python3
import argparse
import importlib.util
import json
import pathlib
import subprocess
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/callable-isolated-operation-authorize.yml"
TOOL = ROOT / "tools/callable-isolated-operation-grant.py"
spec = importlib.util.spec_from_file_location("callable_grant", TOOL)
grant = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(grant)


def valid_args(**changes):
    values = {
        "phase": "creation",
        "plan_seal": "a" * 64,
        "contract_sha256": grant.CONTRACT_SHA256,
        "source_sha256": grant.SOURCE_SHA256,
        "coordination_revision": grant.COORDINATION_REVISION,
        "grant_repository": grant.AUTHORITY_REPOSITORY,
        "grant_event": "workflow_dispatch",
        "grant_ref": "refs/heads/main",
        "workflow_revision": "b" * 40,
        "workflow_sha256": "c" * 64,
        "run_id": "6001",
        "app_id": "7001",
        "installation_id": "8001",
        "approved_at": "2026-09-18T12:00:00Z",
        "lifetime_minutes": 120,
        "output": "unused",
    }
    values.update(changes)
    return argparse.Namespace(**values)


class CallableIsolatedOperationAuthorizationTests(unittest.TestCase):
    def test_workflow_is_manual_main_exact_source_and_environment_protected(self):
        source = WORKFLOW.read_text()
        trigger = source.split("permissions:", 1)[0]
        self.assertIn("workflow_dispatch:", trigger)
        for forbidden in ("pull_request:", "push:", "schedule:", "repository_dispatch:"):
            self.assertNotIn(forbidden, trigger)
        for required in (
            "environment: callable-isolated-operation",
            "test \"$GITHUB_REPOSITORY\" = 'FS-GG/.github'",
            "test \"$GITHUB_EVENT_NAME\" = 'workflow_dispatch'",
            "test \"$GITHUB_REF\" = 'refs/heads/main'",
            "test \"$GITHUB_SHA\" = \"$DOTGITHUB_REVISION\"",
            "ref: ${{ github.sha }}",
            "persist-credentials: false",
        ):
            self.assertIn(required, source)

    def test_workflow_is_minimal_grant_only_and_pins_actions(self):
        source = WORKFLOW.read_text()
        self.assertIn("permissions:\n  contents: read\n", source)
        self.assertIn("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", source)
        self.assertIn("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", source)
        lowered = source.lower()
        for forbidden in (
            "secrets.", "create-github-app-token", "private-key", "github_token", "gh api", "curl ",
            "git push", "repository_dispatch", "contents: write", "administration: write",
            "pull-requests: write", "delete repository", "create repository",
        ):
            self.assertNotIn(forbidden, lowered)

    def test_grant_matches_coordination_contract_and_is_canonical(self):
        value = grant.build(valid_args())
        self.assertEqual("fsgg.coordination.callable-isolated-operation-grant/1", value["schema"])
        self.assertTrue(value["authorized"])
        self.assertEqual("v2-call-01-4b-isolated-native-v1", value["operationIdentity"])
        self.assertEqual(grant.CONTRACT_SHA256, value["contractSha256"])
        self.assertEqual(grant.SOURCE_SHA256, value["sourceSha256"])
        self.assertEqual(grant.COORDINATION_REVISION, value["coordinationRevision"])
        self.assertEqual({"id": 1645484, "login": "EHotwagner"}, value["requiredReviewer"])
        self.assertEqual("2026-09-18T14:00:00Z", value["expiresAt"])
        self.assertEqual(7001, value["appId"])
        self.assertEqual(8001, value["installationId"])
        self.assertEqual(
            {
                "environment": "callable-isolated-operation",
                "repository": "FS-GG/.github",
                "runId": 6001,
                "workflowPath": ".github/workflows/callable-isolated-operation-authorize.yml",
                "workflowRevision": "b" * 40,
                "workflowSha256": "c" * 64,
            },
            value["authority"],
        )
        with tempfile.TemporaryDirectory() as directory:
            output = pathlib.Path(directory) / "grant.json"
            command = [
                "python3", str(TOOL), "--phase", "creation", "--plan-seal", "a" * 64,
                "--contract-sha256", grant.CONTRACT_SHA256, "--source-sha256", grant.SOURCE_SHA256,
                "--coordination-revision", grant.COORDINATION_REVISION,
                "--grant-repository", grant.AUTHORITY_REPOSITORY,
                "--grant-event", "workflow_dispatch", "--grant-ref", "refs/heads/main",
                "--workflow-revision", "b" * 40, "--workflow-sha256", "c" * 64,
                "--run-id", "6001", "--app-id", "7001", "--installation-id", "8001",
                "--approved-at", "2026-09-18T12:00:00Z", "--lifetime-minutes", "120",
                "--output", str(output),
            ]
            completed = subprocess.run(command, capture_output=True, text=True, timeout=5, check=False)
            self.assertEqual(0, completed.returncode, completed.stderr)
            raw = output.read_text()
            self.assertEqual(json.dumps(json.loads(raw), sort_keys=True, separators=(",", ":")) + "\n", raw)
            self.assertEqual(0o600, output.stat().st_mode & 0o777)

    def test_contract_revision_authority_identity_and_time_drift_refuse(self):
        cases = (
            ("phase", "release"),
            ("plan_seal", "A" * 64),
            ("contract_sha256", "0" * 64),
            ("source_sha256", "0" * 64),
            ("coordination_revision", "0" * 40),
            ("grant_repository", "FS-GG/other"),
            ("grant_event", "push"),
            ("grant_ref", "refs/heads/topic"),
            ("workflow_revision", "0" * 39),
            ("workflow_sha256", "0" * 63),
            ("run_id", "0"),
            ("run_id", "9999999999999999999"),
            ("app_id", "secret"),
            ("installation_id", "-1"),
            ("approved_at", "2026-09-18T12:00:00+00:00"),
            ("lifetime_minutes", 0),
            ("lifetime_minutes", 121),
        )
        for field, value in cases:
            with self.subTest(field=field, value=value):
                with self.assertRaises(grant.Refused):
                    grant.build(valid_args(**{field: value}))

    def test_workflow_binds_every_required_grant_input_without_a_token(self):
        source = WORKFLOW.read_text()
        for value in (
            "--phase \"$PHASE\"", "--plan-seal \"$PLAN_SEAL\"",
            "--contract-sha256 \"$CONTRACT_SHA256\"", "--source-sha256 \"$SOURCE_SHA256\"",
            "--coordination-revision \"$COORDINATION_REVISION\"", "--workflow-revision \"$GITHUB_SHA\"",
            "--workflow-sha256 \"$workflow_sha256\"", "--run-id \"$GITHUB_RUN_ID\"",
            "--app-id \"$APP_ID\"", "--installation-id \"$INSTALLATION_ID\"",
            "--lifetime-minutes \"$LIFETIME_MINUTES\"",
        ):
            self.assertIn(value, source)
        self.assertNotIn("token", source.lower())


if __name__ == "__main__":
    unittest.main()

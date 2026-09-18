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
EXECUTOR_WORKFLOW = ROOT / ".github/workflows/callable-isolated-operation-execute.yml"
EXECUTOR_TOOL = ROOT / "tools/callable-isolated-operation-executor.py"
spec = importlib.util.spec_from_file_location("callable_grant", TOOL)
grant = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(grant)
executor_spec = importlib.util.spec_from_file_location("callable_executor", EXECUTOR_TOOL)
executor = importlib.util.module_from_spec(executor_spec)
assert executor_spec.loader is not None
executor_spec.loader.exec_module(executor)


def credential_bindings():
    return json.dumps({role: {"appId": 7000 + index, "installationId": 8000 + index}
                       for index, role in enumerate(grant.ROLE_PERMISSIONS, start=1)}, separators=(",", ":"))


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
        "run_attempt": "2",
        "environment_id": "5001",
        "credential_bindings_json": credential_bindings(),
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
        self.assertFalse(value["authorized"])
        self.assertEqual("prepared-not-authorized", value["status"])
        self.assertEqual("v2-call-01-4b-isolated-native-v1", value["operationIdentity"])
        self.assertEqual(grant.CONTRACT_SHA256, value["contractSha256"])
        self.assertEqual(grant.SOURCE_SHA256, value["sourceSha256"])
        self.assertEqual(grant.COORDINATION_REVISION, value["coordinationRevision"])
        self.assertEqual({"id": 1645484, "login": "EHotwagner"}, value["requiredReviewer"])
        self.assertEqual("2026-09-18T14:00:00Z", value["expiresAt"])
        self.assertEqual(set(grant.ROLE_PERMISSIONS), set(value["credentials"]))
        self.assertEqual("github-app-jwt", value["credentials"]["app-installation-observer"]["kind"])
        self.assertEqual(grant.ROLE_PERMISSIONS["setup"], value["credentials"]["setup"]["permissions"])
        self.assertEqual("server-assigned-coordinates-unavailable-before-upload", value["artifact"]["status"])
        self.assertEqual(
            {
                "environment": "callable-isolated-operation",
                "environmentId": 5001,
                "repository": "FS-GG/.github",
                "runId": 6001,
                "runAttempt": 2,
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
                "--run-id", "6001", "--run-attempt", "2", "--environment-id", "5001",
                "--credential-bindings-json", credential_bindings(),
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
            ("run_attempt", "0"),
            ("environment_id", "secret"),
            ("credential_bindings_json", "{}"),
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
            "--run-attempt \"$GITHUB_RUN_ATTEMPT\"", "--environment-id \"$ENVIRONMENT_ID\"",
            "--credential-bindings-json \"$CREDENTIAL_BINDINGS_JSON\"",
            "--lifetime-minutes \"$LIFETIME_MINUTES\"",
        ):
            self.assertIn(value, source)
        self.assertNotIn("token", source.lower())

    def test_executor_workflow_is_manual_constant_concurrency_and_deliberately_unavailable(self):
        source = EXECUTOR_WORKFLOW.read_text()
        trigger = source.split("permissions:", 1)[0]
        self.assertIn("workflow_dispatch:", trigger)
        for forbidden in ("pull_request:", "push:", "schedule:", "repository_dispatch:"):
            self.assertNotIn(forbidden, trigger)
        for required in (
            "group: callable-isolated-operation-execute",
            "cancel-in-progress: false",
            "environment: callable-isolated-operation",
            "execution-unavailable:prepared-not-authorized",
            "echo 'available=false'",
            "ref: 79ffe01f5cc2a0269797f3ec7ff54bf2c23b5c91",
            "c56cbca184c6477f5d95c3863c227a1eb3eb537e6ed058a193023de6c86a4d3b",
            "0335c253aea68061f338cded634f29268303ec1eea31c6b0472b472d7974ba1e",
        ):
            self.assertIn(required, source)
        for forbidden_input in ("candidate_revision:", "repository:", "api_endpoint:", "request_path:", "token_name:"):
            self.assertNotIn(forbidden_input, trigger)
        lowered = source.lower()
        for forbidden in ("secrets.", "create-github-app-token", "private-key", "gh api", "curl ",
                          "git push", "contents: write", "administration: write", "pull-requests: write"):
            self.assertNotIn(forbidden, lowered)

    def test_executor_packet_binds_exact_evidence_and_reports_every_blocker(self):
        args = argparse.Namespace(
            phase="identity-bound-operation", plan_seal="1" * 64,
            authorization_run_id="10", authorization_run_attempt="2",
            grant_artifact_id="11", grant_artifact_sha256="2" * 64,
            plan_run_id="12", plan_run_attempt="3", plan_artifact_id="13", plan_artifact_sha256="3" * 64,
            creation_receipt_run_id="14", creation_receipt_run_attempt="4",
            creation_receipt_artifact_id="15", creation_receipt_artifact_sha256="4" * 64,
            checkpoint_run_id="16", checkpoint_run_attempt="5", checkpoint_artifact_id="17",
            checkpoint_artifact_sha256="5" * 64, authority_revision="6" * 40,
            workflow_sha256="7" * 64, output="unused")
        packet = executor.build(args)
        self.assertFalse(packet["authorized"])
        self.assertEqual("unavailable", packet["execution"])
        self.assertEqual("prepared-not-authorized", packet["status"])
        self.assertFalse(packet["mutationCredentialsMinted"])
        self.assertFalse(packet["tokenMinting"]["allowed"])
        self.assertFalse(packet["recovery"]["automaticMutationRetry"])
        self.assertEqual(executor.COORDINATION_REVISION, packet["bindings"]["coordinationRevision"])
        self.assertEqual(executor.PACKAGE_SERVED_SHA256, packet["bindings"]["package"]["servedSha256"])
        self.assertEqual(executor.INSTALLED_COMMAND_SHA256, packet["bindings"]["package"]["installedCommandSha256"])
        self.assertEqual(2, packet["evidence"]["grant"]["runAttempt"])
        self.assertEqual("success", packet["evidence"]["grant"]["requiredRunConclusion"])
        self.assertEqual(executor.AUTHORIZATION_WORKFLOW, packet["evidence"]["grant"]["workflowPath"])
        self.assertIn("grant-payload-artifact-coordinate-self-reference-incompatible-with-coordination-v3",
                      packet["blockingReasons"])
        self.assertRegex(packet["seal"], r"^[0-9a-f]{64}$")

    def test_executor_rejects_cross_phase_partial_and_malformed_evidence(self):
        base = dict(
            phase="creation", plan_seal="1" * 64,
            authorization_run_id="10", authorization_run_attempt="2", grant_artifact_id="11",
            grant_artifact_sha256="2" * 64, plan_run_id="12", plan_run_attempt="3",
            plan_artifact_id="13", plan_artifact_sha256="3" * 64,
            creation_receipt_run_id="", creation_receipt_run_attempt="", creation_receipt_artifact_id="",
            creation_receipt_artifact_sha256="", checkpoint_run_id="", checkpoint_run_attempt="",
            checkpoint_artifact_id="", checkpoint_artifact_sha256="", authority_revision="6" * 40,
            workflow_sha256="7" * 64, output="unused")
        cases = (
            {"plan_seal": "A" * 64},
            {"authorization_run_attempt": "0"},
            {"grant_artifact_sha256": "2" * 63},
            {"checkpoint_run_id": "16"},
            {"creation_receipt_run_id": "14", "creation_receipt_run_attempt": "4",
             "creation_receipt_artifact_id": "15", "creation_receipt_artifact_sha256": "4" * 64},
            {"phase": "identity-bound-operation"},
        )
        for changes in cases:
            with self.subTest(changes=changes), self.assertRaises(executor.Refused):
                executor.build(argparse.Namespace(**{**base, **changes}))


if __name__ == "__main__":
    unittest.main()

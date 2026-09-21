#!/usr/bin/env python3
import argparse
import hashlib
import importlib.util
import io
import json
import pathlib
import subprocess
import tempfile
import unittest
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/callable-isolated-operation-authorize.yml"
TOOL = ROOT / "tools/callable-isolated-operation-grant.py"
EXECUTOR_WORKFLOW = ROOT / ".github/workflows/callable-isolated-operation-execute.yml"
EXECUTOR_TOOL = ROOT / "tools/callable-isolated-operation-executor.py"
PLAN_WORKFLOW = ROOT / ".github/workflows/callable-isolated-operation-plan.yml"
ARTIFACT_TOOL = ROOT / "tools/callable-isolated-operation-artifact.py"
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


def authority_observation(app_id=7001, installation_id=8001):
    return json.dumps({
        "schema": "fsgg.github.callable-isolated-operation-authority-observation/1",
        "environment": {"id": 5001, "name": "callable-isolated-operation", "preventSelfReview": False,
                        "requiredReviewerId": 1645484, "branchPolicy": "custom-main"},
        "reviewerMembership": {"id": 1645484, "login": "EHotwagner", "state": "active"},
        "installation": {"appId": app_id, "installationId": installation_id, "account": "FS-GG",
                         "accountType": "Organization", "repositorySelection": "all", "suspendedAt": None,
                         "permissions": {"actions": "write", "administration": "write", "checks": "read",
                                         "contents": "write", "members": "read", "metadata": "read",
                                         "organization_administration": "read", "pull_requests": "write",
                                         "workflows": "write"}},
    }, separators=(",", ":"))


def valid_args(**changes):
    values = {
        "phase": "creation",
        "plan_seal": "a" * 64,
        "plan_run_id": "4001",
        "plan_run_attempt": "1",
        "plan_artifact_id": "4002",
        "plan_artifact_sha256": "d" * 64,
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
        "credential_bindings_json": json.dumps({role: {"appId": 7001, "installationId": 8001}
                                                  for role in grant.ROLE_PERMISSIONS}, separators=(",", ":")),
        "authority_observation_json": authority_observation(),
        "approved_at": "2026-09-18T12:00:00Z",
        "lifetime_minutes": 120,
        "output": "unused",
    }
    values.update(changes)
    return argparse.Namespace(**values)


class CallableIsolatedOperationAuthorizationTests(unittest.TestCase):
    def test_plan_workflow_is_source_only_and_exact_revision_bound(self):
        source = PLAN_WORKFLOW.read_text()
        self.assertIn("workflow_dispatch:", source)
        self.assertIn("ref: bcb8453c3b92a9ea6097e66e326bd1ec3350b667", source)
        self.assertIn("prepare-create", source)
        self.assertIn("prepare-operation", source)
        self.assertIn("callable-isolated-operation-artifact.py extract", source)
        self.assertIn("callable-isolated-operation-creation-receipt-$CREATION_RECEIPT_RUN_ID-$CREATION_RECEIPT_RUN_ATTEMPT", source)
        self.assertIn("35410522020:1:10573846475:f9702e73fa619e63b4c0e6ee8c5efebfbefe744a6e1397cb6a88640e022fd762", source)
        self.assertIn("callable-isolated-operation-checkpoint-creation-35410522020-1", source)
        self.assertIn("receipt_revision=5abd2daeaf6edadc218673fd041eeae5ec3424c8", source)
        self.assertIn(".digest <<<\"$receipt_artifact\"", source)
        self.assertIn(".head_sha <<<\"$receipt_run\"", source)
        for forbidden in ("secrets.", "create-github-app-token", "git push", "contents: write"):
            self.assertNotIn(forbidden, source.lower())

    def test_artifact_extractor_accepts_one_exact_member_and_rejects_extra(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            archive = root / "artifact.zip"
            output = root / "output.json"
            with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
                bundle.writestr("expected.json", b"{}")
            completed = subprocess.run(["python3", str(ARTIFACT_TOOL), "extract", "--archive", str(archive),
                                        "--member", "expected.json", "--output", str(output)],
                                       capture_output=True, text=True, timeout=5, check=False)
            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertEqual(b"{}", output.read_bytes())
            extra = root / "extra.zip"
            with zipfile.ZipFile(extra, "w") as bundle:
                bundle.writestr("expected.json", b"{}")
                bundle.writestr("extra", b"x")
            refused = subprocess.run(["python3", str(ARTIFACT_TOOL), "extract", "--archive", str(extra),
                                      "--member", "expected.json", "--output", str(root / "extra.json")],
                                     capture_output=True, text=True, timeout=5, check=False)
            self.assertEqual(3, refused.returncode)

    def test_executor_recovers_exact_creation_checkpoint_and_retains_future_receipts(self):
        source = EXECUTOR_WORKFLOW.read_text()
        self.assertIn("35410522020:1:10573846475:f9702e73fa619e63b4c0e6ee8c5efebfbefe744a6e1397cb6a88640e022fd762", source)
        self.assertIn("callable-isolated-operation-checkpoint-creation-35410522020-1", source)
        self.assertIn("receipt_revision=5abd2daeaf6edadc218673fd041eeae5ec3424c8", source)
        self.assertIn('cp "$RUNNER_TEMP/checkpoint.json" "$RUNNER_TEMP/callable-isolated-operation-creation-receipt.json"', source)
        self.assertIn("Materialize restart-safe checkpoint after every provider attempt", source)
        self.assertIn('if [ -f "$RUNNER_TEMP/checkpoint.json" ]; then', source)
        self.assertIn("35411661144:1:10574618280:68d7bf46c5ea0ffe901717a304f552246dd4497684e32e0b7fb7bf991018c6b7", source)
        self.assertIn("checkpoint_revision=a1a5abcda7d2fb6d49949e1e6f3afdc3c7f9f2b1", source)
        self.assertIn("Diagnose a structured installed-plan refusal without mutation", source)
        self.assertNotIn("jq -c '.creationReceipt'", source)

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

    def test_workflow_observes_live_authority_and_pins_actions(self):
        source = WORKFLOW.read_text()
        self.assertIn("permissions:\n  contents: read\n", source)
        self.assertIn("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", source)
        self.assertIn("actions/upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", source)
        self.assertIn("actions/create-github-app-token@bcd2ba49218906704ab6c1aa796996da409d3eb1", source)
        self.assertIn("client-id: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_CLIENT_ID }}", source)
        self.assertNotIn("app-id: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_ID }}", source)
        self.assertIn("APP_ID: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_ID }}", source)
        self.assertIn("secrets.CALLABLE_ISOLATED_OPERATION_APP_PRIVATE_KEY", source)
        self.assertIn("gh api orgs/FS-GG/memberships/EHotwagner", source)
        self.assertIn("deployment-branch-policies", source)
        self.assertIn('.prevent_self_review ] | if length==1 then .[0] else null end', source)
        lowered = source.lower()
        for forbidden in (
            "git push", "repository_dispatch", "contents: write",
            "delete repository", "create repository",
        ):
            self.assertNotIn(forbidden, lowered)

    def test_grant_matches_coordination_contract_and_is_canonical(self):
        value = grant.build(valid_args())
        self.assertEqual("fsgg.coordination.callable-isolated-operation-grant/1", value["schema"])
        self.assertTrue(value["authorized"])
        self.assertEqual("authorized", value["status"])
        self.assertEqual("v2-call-01-4b-isolated-native-v1", value["operationIdentity"])
        self.assertEqual(grant.CONTRACT_SHA256, value["contractSha256"])
        self.assertEqual(grant.SOURCE_SHA256, value["sourceSha256"])
        self.assertEqual(grant.COORDINATION_REVISION, value["coordinationRevision"])
        self.assertEqual({"id": 1645484, "login": "EHotwagner"}, value["requiredReviewer"])
        self.assertEqual("2026-09-18T14:00:00Z", value["expiresAt"])
        self.assertEqual(set(grant.ROLE_PERMISSIONS), set(value["credentials"]))
        self.assertEqual("github-app-jwt", value["credentials"]["app-installation-observer"]["kind"])
        self.assertEqual(grant.ROLE_PERMISSIONS["setup"], value["credentials"]["setup"]["permissions"])
        self.assertEqual({"artifactId": 4002, "repository": "FS-GG/.github", "runAttempt": 1,
                          "runId": 4001, "sha256": "d" * 64}, value["planArtifact"])
        self.assertNotIn("artifact", value)
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
                "--plan-run-id", "4001", "--plan-run-attempt", "1",
                "--plan-artifact-id", "4002", "--plan-artifact-sha256", "d" * 64,
                "--contract-sha256", grant.CONTRACT_SHA256, "--source-sha256", grant.SOURCE_SHA256,
                "--coordination-revision", grant.COORDINATION_REVISION,
                "--grant-repository", grant.AUTHORITY_REPOSITORY,
                "--grant-event", "workflow_dispatch", "--grant-ref", "refs/heads/main",
                "--workflow-revision", "b" * 40, "--workflow-sha256", "c" * 64,
                "--run-id", "6001", "--run-attempt", "2", "--environment-id", "5001",
                "--credential-bindings-json", valid_args().credential_bindings_json,
                "--authority-observation-json", authority_observation(),
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
            ("plan_run_id", "0"),
            ("plan_run_attempt", "0"),
            ("plan_artifact_id", "0"),
            ("plan_artifact_sha256", "D" * 64),
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
            ("authority_observation_json", "{}"),
            ("approved_at", "2026-09-18T12:00:00+00:00"),
            ("lifetime_minutes", 0),
            ("lifetime_minutes", 121),
        )
        for field, value in cases:
            with self.subTest(field=field, value=value):
                with self.assertRaises(grant.Refused):
                    grant.build(valid_args(**{field: value}))

    def test_live_authority_drift_refuses_before_grant(self):
        baseline = json.loads(authority_observation())
        cases = []
        changed = json.loads(json.dumps(baseline)); changed["environment"]["preventSelfReview"] = True; cases.append(changed)
        changed = json.loads(json.dumps(baseline)); changed["environment"]["branchPolicy"] = "other"; cases.append(changed)
        changed = json.loads(json.dumps(baseline)); changed["reviewerMembership"]["state"] = "pending"; cases.append(changed)
        changed = json.loads(json.dumps(baseline)); changed["installation"]["repositorySelection"] = "selected"; cases.append(changed)
        changed = json.loads(json.dumps(baseline)); changed["installation"]["permissions"].pop("checks"); cases.append(changed)
        changed = json.loads(json.dumps(baseline)); changed["installation"]["suspendedAt"] = "2026-09-18T12:00:00Z"; cases.append(changed)
        for observation in cases:
            with self.subTest(observation=observation), self.assertRaises(grant.Refused):
                grant.build(valid_args(authority_observation_json=json.dumps(observation, separators=(",", ":"))))

    def test_workflow_binds_every_required_grant_input_to_live_observation(self):
        source = WORKFLOW.read_text()
        for value in (
            "--phase \"$PHASE\"", "--plan-seal \"$PLAN_SEAL\"",
            "--plan-run-id \"$PLAN_RUN_ID\" --plan-run-attempt \"$PLAN_RUN_ATTEMPT\"",
            "--plan-artifact-id \"$PLAN_ARTIFACT_ID\" --plan-artifact-sha256 \"$PLAN_ARTIFACT_SHA256\"",
            "--contract-sha256 \"$CONTRACT_SHA256\"", "--source-sha256 \"$SOURCE_SHA256\"",
            "--coordination-revision \"$COORDINATION_REVISION\"", "--workflow-revision \"$GITHUB_SHA\"",
            "--workflow-sha256 \"$workflow_sha256\"", "--run-id \"$GITHUB_RUN_ID\"",
            "--run-attempt \"$GITHUB_RUN_ATTEMPT\"", "--environment-id \"$ENVIRONMENT_ID\"",
            "--credential-bindings-json \"$credential_bindings\"",
            "--authority-observation-json \"$authority_observation\"",
            "--lifetime-minutes \"$LIFETIME_MINUTES\"",
        ):
            self.assertIn(value, source)
        self.assertIn("GH_TOKEN: ${{ steps.observer.outputs.token }}", source)
        self.assertIn(".digest <<<\"$plan_artifact\"", source)
        self.assertIn(".head_sha <<<\"$plan_run\"", source)
        self.assertIn("name: callable-isolated-operation-grant-${{ github.run_id }}-${{ github.run_attempt }}", source)
        self.assertEqual(1, source.count("${{ runner.temp }}/callable-isolated-operation-grant.json"))

    def test_executor_workflow_is_manual_constant_concurrency_and_live_admission_guarded(self):
        source = EXECUTOR_WORKFLOW.read_text()
        trigger = source.split("permissions:", 1)[0]
        self.assertIn("workflow_dispatch:", trigger)
        for forbidden in ("pull_request:", "push:", "schedule:", "repository_dispatch:"):
            self.assertNotIn(forbidden, trigger)
        for required in (
            "group: callable-isolated-operation-execute",
            "cancel-in-progress: false",
            "environment: callable-isolated-operation",
            "ref: bcb8453c3b92a9ea6097e66e326bd1ec3350b667",
            "--grant-payload-sha256 '${{ inputs.grant_payload_sha256 }}'",
            "--grant-artifact-expires-at '${{ inputs.grant_artifact_expires_at }}'",
            "coordination/eng/callable-cli-isolated-operation.py execute",
            "client-id: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_CLIENT_ID }}",
            "secrets.CALLABLE_ISOLATED_OPERATION_APP_PRIVATE_KEY",
            "repositories: FS.GG.Coordination.CallableSandbox",
            "callable-isolated-operation-plan-${{ inputs.phase }}-${{ inputs.plan_run_id }}-${{ inputs.plan_run_attempt }}",
            ".digest <<<\"$plan_artifact\"",
            ".digest <<<\"$checkpoint_artifact\"",
            "35611476251:1:10644976229:819cc1661bfdbd88fa50f8f462d56af45726a46d3acb7a26db72f1fc2d757aa7",
            "35619835923:1:10647264981:f98b32505b081a4c051d253f36e3a7d51e2fd1271a26596c18a7902b345a4318",
            "dotnet tool install FS.GG.Coordination.Cli --version 0.1.1",
            ".digest <<<\"$receipt_artifact\"",
            ".creationReceiptSha256",
            "repository-permissions",
            ".permissions // {} | {admin,maintain,push,triage,pull}",
        ):
            self.assertIn(required, source)
        self.assertEqual(
            source.count("actions/create-github-app-token@"),
            source.count("client-id: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_CLIENT_ID }}"),
        )
        self.assertNotIn("app-id: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_ID }}", source)
        self.assertIn("APP_ID: ${{ secrets.CALLABLE_ISOLATED_OPERATION_APP_ID }}", source)
        for forbidden_input in ("candidate_revision:", "repository:", "api_endpoint:", "request_path:", "token_name:"):
            self.assertNotIn(forbidden_input, trigger)
        self.assertNotIn("${{ github.token }}", source)
        self.assertEqual(2, source.count("permission-administration: read"))

    def test_executor_packet_binds_exact_evidence_and_reports_every_blocker(self):
        args = argparse.Namespace(
            phase="identity-bound-operation", plan_seal="1" * 64,
            grant_run_id="10", grant_run_attempt="2",
            grant_artifact_id="11", grant_artifact_sha256="2" * 64,
            grant_payload_sha256="8" * 64, grant_artifact_expires_at="2026-09-19T12:00:00Z",
            plan_run_id="12", plan_run_attempt="3", plan_artifact_id="13", plan_artifact_sha256="3" * 64,
            creation_receipt_run_id="14", creation_receipt_run_attempt="4",
            creation_receipt_artifact_id="15", creation_receipt_artifact_sha256="4" * 64,
            checkpoint_run_id="16", checkpoint_run_attempt="5", checkpoint_artifact_id="17",
            checkpoint_artifact_sha256="5" * 64, workflow_revision="6" * 40,
            workflow_sha256="7" * 64, output="unused")
        packet = executor.build(args)
        self.assertFalse(packet["authorized"])
        self.assertEqual("pending-live-admission", packet["execution"])
        self.assertEqual("runtime-admission-required", packet["status"])
        self.assertFalse(packet["mutationCredentialsMinted"])
        self.assertTrue(packet["tokenMinting"]["allowed"])
        self.assertFalse(packet["recovery"]["automaticMutationRetry"])
        self.assertEqual(executor.COORDINATION_REVISION, packet["bindings"]["coordinationRevision"])
        self.assertEqual(executor.PACKAGE_SERVED_SHA256, packet["bindings"]["package"]["servedSha256"])
        self.assertEqual(executor.INSTALLED_COMMAND_SHA256, packet["bindings"]["package"]["installedCommandSha256"])
        self.assertEqual(2, packet["evidence"]["authorizationRun"]["runAttempt"])
        self.assertEqual("success", packet["evidence"]["authorizationRun"]["requiredRunConclusion"])
        self.assertEqual(executor.AUTHORIZATION_WORKFLOW, packet["evidence"]["authorizationRun"]["workflowPath"])
        envelope = packet["evidence"]["grantArtifactEnvelope"]
        self.assertEqual("callable-isolated-operation-grant-10-2", envelope["artifactName"])
        self.assertEqual(10, envelope["workflowRunId"])
        self.assertEqual(2, envelope["workflowRunAttempt"])
        self.assertEqual("8" * 64, envelope["payloadSha256"])
        self.assertNotIn("grant-payload-artifact-coordinate-self-reference-incompatible-with-coordination-v3",
                         packet["blockingReasons"])
        self.assertEqual("--grant-artifact-envelope", packet["operatorIntegration"]["argument"])
        self.assertEqual(executor.GRANT_ARTIFACT_SCHEMA, packet["operatorIntegration"]["envelopeSchema"])
        self.assertRegex(packet["seal"], r"^[0-9a-f]{64}$")

    def test_executor_rejects_cross_phase_partial_and_malformed_evidence(self):
        base = dict(
            phase="creation", plan_seal="1" * 64,
            grant_run_id="10", grant_run_attempt="2", grant_artifact_id="11",
            grant_artifact_sha256="2" * 64, grant_payload_sha256="8" * 64,
            grant_artifact_expires_at="2026-09-19T12:00:00Z",
            plan_run_id="12", plan_run_attempt="3",
            plan_artifact_id="13", plan_artifact_sha256="3" * 64,
            creation_receipt_run_id="", creation_receipt_run_attempt="", creation_receipt_artifact_id="",
            creation_receipt_artifact_sha256="", checkpoint_run_id="", checkpoint_run_attempt="",
            checkpoint_artifact_id="", checkpoint_artifact_sha256="", workflow_revision="6" * 40,
            workflow_sha256="7" * 64, output="unused")
        cases = (
            {"plan_seal": "A" * 64},
            {"grant_run_attempt": "0"},
            {"grant_artifact_sha256": "2" * 63},
            {"grant_payload_sha256": "8" * 63},
            {"grant_artifact_expires_at": "2026-09-19T12:00:00+00:00"},
            {"checkpoint_run_id": "16"},
            {"creation_receipt_run_id": "14", "creation_receipt_run_attempt": "4",
             "creation_receipt_artifact_id": "15", "creation_receipt_artifact_sha256": "4" * 64},
            {"phase": "identity-bound-operation"},
        )
        for changes in cases:
            with self.subTest(changes=changes), self.assertRaises(executor.Refused):
                executor.build(argparse.Namespace(**{**base, **changes}))

    def test_grant_artifact_envelope_verifies_exact_archive_and_payload(self):
        payload = json.dumps(grant.build(valid_args()), sort_keys=True, separators=(",", ":")).encode()

        def bundle(files):
            output = io.BytesIO()
            with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
                for name, contents in files:
                    archive.writestr(name, contents)
            return output.getvalue()

        archive = bundle([(executor.GRANT_ARTIFACT_FILE, payload)])
        envelope = {
            "schema": executor.GRANT_ARTIFACT_SCHEMA,
            "repository": executor.AUTHORITY_REPOSITORY,
            "artifactId": 11,
            "artifactName": "callable-isolated-operation-grant-10-2",
            "artifactSha256": hashlib.sha256(archive).hexdigest(),
            "payloadSha256": hashlib.sha256(payload).hexdigest(),
            "workflowRunId": 10,
            "workflowRunAttempt": 2,
            "expiresAt": "2099-09-19T12:00:00Z",
        }
        self.assertEqual(grant.OPERATION_IDENTITY, executor.verify_grant_artifact_bytes(envelope, archive)["operationIdentity"])

        cases = (
            ({**envelope, "artifactSha256": "0" * 64}, archive),
            ({**envelope, "payloadSha256": "0" * 64}, archive),
            ({**envelope, "artifactName": "callable-isolated-operation-grant-10-3"}, archive),
            ({**envelope, "workflowRunAttempt": 3}, archive),
            ({**envelope, "expiresAt": "2026-09-17T12:00:00Z"}, archive),
            ({**envelope, "unexpected": True}, archive),
            (envelope, b"not-a-zip"),
            ({**envelope, "artifactSha256": "", "payloadSha256": ""}, bundle([("wrong.json", payload)])),
            ({**envelope, "artifactSha256": "", "payloadSha256": ""}, bundle([
                (executor.GRANT_ARTIFACT_FILE, payload), ("extra", b"x")
            ])),
        )
        for candidate, candidate_archive in cases:
            candidate = dict(candidate)
            if candidate["artifactSha256"] == "":
                candidate["artifactSha256"] = hashlib.sha256(candidate_archive).hexdigest()
                candidate["payloadSha256"] = hashlib.sha256(payload).hexdigest()
            with self.subTest(candidate=candidate), self.assertRaises(executor.Refused):
                executor.verify_grant_artifact_bytes(candidate, candidate_archive)

        substituted = dict(grant.build(valid_args()))
        substituted["artifact"] = {"artifactId": 1}
        substituted_payload = json.dumps(substituted, sort_keys=True, separators=(",", ":")).encode()
        substituted_archive = bundle([(executor.GRANT_ARTIFACT_FILE, substituted_payload)])
        with self.assertRaises(executor.Refused):
            executor.verify_grant_artifact_bytes({
                **envelope,
                "artifactSha256": hashlib.sha256(substituted_archive).hexdigest(),
                "payloadSha256": hashlib.sha256(substituted_payload).hexdigest(),
            }, substituted_archive)


if __name__ == "__main__":
    unittest.main()

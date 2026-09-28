import datetime as dt
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "seed_admission_authorizer", ROOT / "scripts/gs2-09-7-seed-admission-authorizer.py")
gate = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(gate)


class AuthorizerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="seed-authorizer-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.now = dt.datetime(2026, 9, 28, 8, 6, tzinfo=dt.timezone.utc)
        self.candidate = "b" * 40
        self.executor_sha = "a" * 40
        self.producer_sha = "f" * 40
        self.run_id = 12345
        self.attempt = 2
        self.nonce = f"{self.run_id}-{self.attempt}-{self.candidate}"
        self.final_subject = {
            "workflowRepository": "FS-GG/.github", "workflowPath": gate.EXECUTOR_WORKFLOW,
            "environment": "github-substrate-v2-sandbox", "workflowSha": self.executor_sha,
            "runId": self.run_id, "runAttempt": self.attempt, "candidateSha": self.candidate,
            "runNonce": self.nonce, "approvedArtifactSourceSha256": "c" * 64,
            "sourceManifestSha256": "c" * 64,
            "prestateSha256": "5" * 64,
            "prestateSnapshotSha256": "6" * 64,
            "prestateEvidenceSha256": "7" * 64,
            "expectedRefAbsent": True,
            "sandboxRepositoryId": 1353050537, "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
            "projectNodeId": "PVT_kwDOEYAWY84BiESo", "appId": 4166418,
            "installationId": 143110413, "seedPlanSha256": "d" * 64,
            "s2DeclarationSha256": "e" * 64, "mintProofSha256": "9" * 64,
            "tokenSha256": "8" * 64,
            "refName": f"refs/heads/gs2-09-7/{self.nonce}/seed-journal",
            "blobOid": "1" * 40, "treeOid": "2" * 40, "commitOid": "3" * 40,
            "expectedOldOid": None, "operation": "genesis-nonce-seed-journal",
        }
        self.prepare_subject = {
            key: self.final_subject[key] for key in (
                "workflowRepository", "workflowPath", "environment", "workflowSha",
                "candidateSha", "runId", "runAttempt", "runNonce",
                "approvedArtifactSourceSha256", "sandboxRepositoryId",
                "sandboxRepositoryNodeId", "projectNodeId", "appId", "installationId")
        }
        self.prepare_subject.update({"sourceManifestSha256": "c" * 64,
                                     "operation": "prepare-only-no-effect"})
        self.executor_run = {
            "id": self.run_id, "run_attempt": self.attempt, "path": gate.EXECUTOR_WORKFLOW,
            "head_sha": self.executor_sha, "head_branch": "main", "event": "workflow_dispatch",
            "repository": {"id": gate.REPOSITORY_ID}, "status": "in_progress", "conclusion": None,
        }
        self.producer_run = {
            "id": 9001, "run_attempt": 1, "path": gate.AUTHORIZER_WORKFLOW,
            "head_sha": self.producer_sha, "head_branch": "main", "event": "workflow_dispatch",
            "repository": {"id": gate.REPOSITORY_ID}, "status": "in_progress", "conclusion": None,
            "actor": gate.OWNER, "created_at": "2026-09-28T08:00:00Z",
        }
        env = {"id": gate.ENVIRONMENT_ID, "name": gate.ENVIRONMENT}
        self.approvals = [
            {"state": "approved", "comment": "approved", "user": gate.OWNER,
             "environments": [env]},
            {"state": "approved", "comment": "5 minute wait timer", "user": gate.TIMER_ACTOR,
             "environments": [env]},
        ]
        self.environment = {
            "id": gate.ENVIRONMENT_ID, "name": gate.ENVIRONMENT,
            "can_admins_bypass": True,
            "deployment_branch_policy": {"protected_branches": False,
                                           "custom_branch_policies": True},
            "protection_rules": [
                {"id": gate.REVIEWER_RULE_ID, "node_id": "review", "type": "required_reviewers",
                 "prevent_self_review": False,
                 "reviewers": [{"type": "User", "reviewer": gate.OWNER}]},
                {"id": gate.WAIT_RULE_ID, "node_id": "wait", "type": "wait_timer",
                 "wait_timer": 5},
                {"id": gate.BRANCH_RULE_ID, "node_id": "branch", "type": "branch_policy"},
            ],
        }
        self.policies = {"total_count": 1, "branch_policies": [
            {"id": gate.MAIN_POLICY_ID, "node_id": "main", "name": "main", "type": "branch"}]}

    def request(self, phase, subject):
        path = self.root / f"{phase}.zip"
        raw = gate.canonical({"schema": gate.REQUEST_SCHEMA, "phase": phase, "subject": subject})
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("seed-admission-request.json", raw)
        archive_raw = path.read_bytes()
        archive_digest = "sha256:" + hashlib.sha256(archive_raw).hexdigest()
        artifact = {
            "id": 456, "name": f"gs2-09-7-seed-admission-request-{phase}-{self.run_id}-{self.attempt}",
            "expired": False, "digest": archive_digest,
            "workflow_run": {"id": self.run_id, "head_sha": self.executor_sha,
                             "head_branch": "main", "repository_id": gate.REPOSITORY_ID,
                             "head_repository_id": gate.REPOSITORY_ID},
        }
        return path, archive_digest, artifact

    def authorize(self, phase="final", subject=None, existing=None):
        subject = subject or (self.prepare_subject if phase == "prepare" else self.final_subject)
        path, archive_digest, artifact = self.request(phase, subject)
        return gate.authorize(
            phase=phase, artifact=artifact, executor_run=self.executor_run,
            request_archive=path, producer_run=self.producer_run, approvals=self.approvals,
            environment=self.environment, policies=self.policies, artifact_id=456,
            existing_decisions=(existing if existing is not None
                                else {"total_count": 0, "artifacts": []}),
            executor_run_id=self.run_id, executor_run_attempt=self.attempt,
            archive_digest=archive_digest, producer_sha=self.producer_sha,
            producer_run_id=9001, producer_attempt=1, now=self.now)

    def test_prepare_and_final_are_disjoint_bounded_decisions(self):
        prepared = self.authorize("prepare")
        final = self.authorize("final")
        self.assertEqual("prepare-only-no-effect", prepared["subject"]["operation"])
        self.assertNotIn("tokenSha256", prepared["subject"])
        self.assertEqual("genesis-nonce-seed-journal", final["subject"]["operation"])
        self.assertIsNone(final["subject"]["expectedOldOid"])
        self.assertEqual(5, final["authorization"]["environment"]["waitTimerMinutes"])
        self.assertNotIn("token", gate.canonical(final).decode("ascii").replace("tokenSha256", ""))

    def test_final_refuses_non_genesis_or_unbound_s1_s2_and_mint(self):
        for name, value in (("expectedOldOid", "4" * 40), ("operation", "seed-issue"),
                            ("s2DeclarationSha256", "bad"), ("blobOid", "bad"),
                            ("mintProofSha256", "bad"), ("tokenSha256", "bad"),
                            ("prestateEvidenceSha256", "bad"),
                            ("expectedRefAbsent", False),
                            ("sourceManifestSha256", "f" * 64)):
            with self.subTest(name=name), self.assertRaises(gate.Refused):
                self.authorize("final", {**self.final_subject, name: value})

    def test_prepare_refuses_raw_manifest_approved_source_mismatch(self):
        with self.assertRaisesRegex(gate.Refused, "prepare-credential-only"):
            self.authorize("prepare", {**self.prepare_subject,
                                       "approvedArtifactSourceSha256": "f" * 64})

    def test_request_artifact_and_executor_identity_are_exact(self):
        path, archive_digest, artifact = self.request("final", self.final_subject)
        artifact["workflow_run"]["id"] += 1
        with self.assertRaisesRegex(gate.Refused, "executor-artifact"):
            gate.authorize(
                phase="final", artifact=artifact, executor_run=self.executor_run,
                request_archive=path, producer_run=self.producer_run, approvals=self.approvals,
                environment=self.environment, policies=self.policies, artifact_id=456,
                existing_decisions={"total_count": 0, "artifacts": []},
                executor_run_id=self.run_id, executor_run_attempt=self.attempt,
                archive_digest=archive_digest, producer_sha=self.producer_sha,
                producer_run_id=9001, producer_attempt=1, now=self.now)

    def test_existing_decision_for_the_request_refuses(self):
        with self.assertRaisesRegex(gate.Refused, "duplicate-decision"):
            self.authorize(existing={"total_count": 1, "artifacts": [{"id": 99}]})

    def test_native_owner_timer_and_main_policy_are_authority(self):
        self.authorize()
        for mutation, reason in (
            (lambda: self.approvals.pop(), "native-approvals"),
            (lambda: self.approvals.append(self.approvals[0]), "native-approvals"),
            (lambda: self.environment["protection_rules"][0].update(prevent_self_review=True),
             "environment-rules"),
            (lambda: self.policies["branch_policies"][0].update(id=1),
             "environment-main-policy"),
        ):
            with self.subTest(reason=reason):
                self.setUp()
                mutation()
                with self.assertRaisesRegex(gate.Refused, reason):
                    self.authorize()

    def test_workflow_uses_only_the_native_owner_environment_and_sanitized_artifact(self):
        source = (ROOT / ".github/workflows/gs2-09-7-seed-admission-authorize.yml").read_text()
        self.assertIn("environment: fleet-v1-admission-owner", source)
        self.assertIn("existing-decisions.json", source)
        self.assertNotIn("FSGG_DISPATCH_APP_PRIVATE_KEY", source)
        self.assertNotIn("operation/79", source)


if __name__ == "__main__":
    unittest.main()

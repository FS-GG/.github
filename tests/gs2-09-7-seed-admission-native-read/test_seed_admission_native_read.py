import datetime as dt
import hashlib
import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile


ROOT = Path(__file__).resolve().parents[2]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


auth = module("seed_authorizer_fixture", ROOT / "scripts/gs2-09-7-seed-admission-authorizer.py")
reader = module("seed_native_reader", ROOT / "scripts/gs2-09-7-seed-admission-native-read.py")


class NativeReadTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="seed-native-read-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.now = dt.datetime(2026, 9, 28, 8, 7, tzinfo=dt.timezone.utc)
        candidate = "b" * 40
        self.executor_run_id = 12345
        self.executor_attempt = 2
        nonce = f"{self.executor_run_id}-{self.executor_attempt}-{candidate}"
        self.subject = {
            "workflowRepository": "FS-GG/.github", "workflowPath": auth.EXECUTOR_WORKFLOW,
            "environment": "github-substrate-v2-sandbox", "workflowSha": "a" * 40,
            "runId": self.executor_run_id, "runAttempt": self.executor_attempt,
            "candidateSha": candidate, "runNonce": nonce,
            "approvedArtifactSourceSha256": "c" * 64,
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
            "refName": f"refs/heads/gs2-09-7/{nonce}/seed-journal",
            "blobOid": "1" * 40, "treeOid": "2" * 40, "commitOid": "3" * 40,
            "expectedOldOid": None, "operation": "genesis-nonce-seed-journal",
        }
        env_ref = {"id": auth.ENVIRONMENT_ID, "name": auth.ENVIRONMENT}
        self.approvals = [
            {"state": "approved", "comment": "reviewed", "user": auth.OWNER,
             "environments": [env_ref]},
            {"state": "approved", "comment": "5 minute wait timer", "user": auth.TIMER_ACTOR,
             "environments": [env_ref]},
        ]
        self.environment = {
            "id": auth.ENVIRONMENT_ID, "name": auth.ENVIRONMENT,
            "can_admins_bypass": True,
            "deployment_branch_policy": {"protected_branches": False,
                                           "custom_branch_policies": True},
            "protection_rules": [
                {"id": auth.REVIEWER_RULE_ID, "node_id": "review", "type": "required_reviewers",
                 "prevent_self_review": False,
                 "reviewers": [{"type": "User", "reviewer": auth.OWNER}]},
                {"id": auth.WAIT_RULE_ID, "node_id": "wait", "type": "wait_timer", "wait_timer": 5},
                {"id": auth.BRANCH_RULE_ID, "node_id": "branch", "type": "branch_policy"},
            ],
        }
        self.policies = {"total_count": 1, "branch_policies": [
            {"id": auth.MAIN_POLICY_ID, "node_id": "main", "name": "main", "type": "branch"}]}
        self.producer_run_id = 9001
        self.producer_sha = "f" * 40
        self.producer_run = {
            "id": self.producer_run_id, "run_attempt": 1, "path": auth.AUTHORIZER_WORKFLOW,
            "head_sha": self.producer_sha, "head_branch": "main", "event": "workflow_dispatch",
            "repository": {"id": auth.REPOSITORY_ID}, "status": "completed",
            "conclusion": "success", "actor": auth.OWNER,
            "created_at": "2026-09-28T08:00:00Z",
        }
        request_archive = self.root / "request.zip"
        request_raw = auth.canonical({"schema": auth.REQUEST_SCHEMA, "phase": "final",
                                      "subject": self.subject})
        with zipfile.ZipFile(request_archive, "w") as bundle:
            bundle.writestr("seed-admission-request.json", request_raw)
        request_digest = "sha256:" + hashlib.sha256(request_archive.read_bytes()).hexdigest()
        request_artifact = {
            "id": 456,
            "name": f"gs2-09-7-seed-admission-request-final-{self.executor_run_id}-{self.executor_attempt}",
            "expired": False, "digest": request_digest,
            "workflow_run": {"id": self.executor_run_id, "head_sha": "a" * 40,
                             "head_branch": "main", "repository_id": auth.REPOSITORY_ID,
                             "head_repository_id": auth.REPOSITORY_ID},
        }
        executor_run = {
            "id": self.executor_run_id, "run_attempt": self.executor_attempt,
            "path": auth.EXECUTOR_WORKFLOW, "head_sha": "a" * 40,
            "head_branch": "main", "event": "workflow_dispatch",
            "repository": {"id": auth.REPOSITORY_ID}, "status": "in_progress", "conclusion": None,
        }
        self.decision = auth.authorize(
            phase="final", artifact=request_artifact, executor_run=executor_run,
            request_archive=request_archive,
            producer_run={**self.producer_run, "status": "in_progress", "conclusion": None},
            approvals=self.approvals, environment=self.environment, policies=self.policies,
            existing_decisions={"total_count": 0, "artifacts": []},
            artifact_id=456, executor_run_id=self.executor_run_id,
            executor_run_attempt=self.executor_attempt, archive_digest=request_digest,
            producer_sha=self.producer_sha, producer_run_id=self.producer_run_id,
            producer_attempt=1, now=self.now)
        self.archive = self.root / "decision.zip"
        self.write_archive()
        self.artifact_id = 777
        self.archive_digest = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.artifact = {
            "id": self.artifact_id,
            "name": f"gs2-09-7-seed-admission-decision-final-{self.executor_run_id}-{self.executor_attempt}",
            "expired": False, "digest": self.archive_digest,
            "workflow_run": {"id": self.producer_run_id, "head_sha": self.producer_sha,
                             "head_branch": "main", "repository_id": auth.REPOSITORY_ID,
                             "head_repository_id": auth.REPOSITORY_ID},
        }
        self.decision_artifacts = {"total_count": 1, "artifacts": [self.artifact]}

    def write_archive(self, extra=None):
        with zipfile.ZipFile(self.archive, "w") as bundle:
            bundle.writestr(reader.DECISION_MEMBER, auth.canonical(self.decision))
            if extra is not None:
                bundle.writestr(extra, b"duplicate")

    def verify(self, subject=None, now=None):
        return reader.verify_decision(
            phase="final", expected_subject=subject or self.subject,
            expected_producer_sha=self.producer_sha, artifact=self.artifact,
            decision_artifacts=self.decision_artifacts, producer_run=self.producer_run,
            approvals=self.approvals, environment=self.environment, policies=self.policies,
            archive=self.archive, artifact_id=self.artifact_id,
            producer_run_id=self.producer_run_id, producer_run_attempt=1,
            archive_digest=self.archive_digest, now=now or self.now)

    def test_exact_native_source_approval_and_unique_bytes_are_readable(self):
        decision = self.verify()
        port = reader.NativeDecisionPort(decision, "final")
        self.assertTrue(port.describe()["candidateCanRead"])
        self.assertFalse(port.describe()["candidateCanWrite"])
        self.assertFalse(port.describe()["executorWorkflowCanWrite"])
        self.assertTrue(port.describe()["authorizerWorkflowCanWrite"])
        self.assertEqual(decision, port.read_decision(self.executor_run_id, self.executor_attempt))

    def test_forged_subject_and_source_drift_refuse(self):
        with self.assertRaises(reader.Refused):
            self.verify({**self.subject, "tokenSha256": "0" * 64})
        expected = self.producer_sha
        self.producer_sha = "0" * 40
        with self.assertRaisesRegex(reader.Refused, "producer-run-binding"):
            self.verify()
        self.producer_sha = expected
        self.producer_run["path"] = ".github/workflows/foreign.yml"
        with self.assertRaisesRegex(reader.Refused, "producer-run"):
            self.verify()

    def test_duplicate_artifact_or_archive_member_refuses(self):
        self.decision_artifacts = {"total_count": 2, "artifacts": [self.artifact, self.artifact]}
        with self.assertRaisesRegex(reader.Refused, "unique"):
            self.verify()
        self.decision_artifacts = {"total_count": 1, "artifacts": [self.artifact]}
        self.write_archive("second.json")
        self.archive_digest = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.artifact["digest"] = self.archive_digest
        with self.assertRaisesRegex(reader.Refused, "members"):
            self.verify()

    def test_approval_policy_drift_and_expiry_refuse(self):
        self.approvals[0]["user"] = {"login": "forger", "id": 1}
        with self.assertRaises(ValueError):
            self.verify()
        self.setUp()
        with self.assertRaisesRegex(reader.Refused, "expiry"):
            self.verify(now=dt.datetime(2026, 9, 28, 8, 17, tzinfo=dt.timezone.utc))


if __name__ == "__main__":
    unittest.main()

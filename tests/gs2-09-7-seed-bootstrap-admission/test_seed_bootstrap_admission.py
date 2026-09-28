import datetime as dt
import importlib.util
from pathlib import Path
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "seed_bootstrap_admission", ROOT / "scripts/gs2-09-7-seed-bootstrap-admission.py")
admission = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(admission)


class Port:
    def __init__(self, subject, now):
        self.subject = subject
        self.now = now
    def describe(self):
        return {"schema": admission.SCHEMA, "origin": "https://authority.example",
                "resourceId": "protected-q4-seed", "endpoint": "https://authority.example/q4",
                "durable": True, "immutable": True, "nativeReadback": True,
                "credentialScope": "actions-read", "candidateCanRead": True,
                "candidateCanWrite": False, "executorWorkflowCanWrite": False,
                "authorizerWorkflowCanWrite": True,
                "decisionWriter": ".github/workflows/gs2-09-7-seed-admission-authorize.yml"}
    def read_decision(self, run_id, attempt):
        return {"schema": admission.DECISION_SCHEMA, "resourceId": "protected-q4-seed",
                "decisionId": admission.decision_id(self.subject), "state": "admitted",
                "phase": "final",
                "subject": self.subject, "issuedAt": "2026-09-28T08:00:00Z",
                "expiresAt": "2026-09-28T08:10:00Z", "sealed": True,
                "executorRequest": {"artifactId": 456, "archiveDigest": "sha256:" + "7" * 64,
                                    "requestSha256": "6" * 64, "runId": self.subject["runId"],
                                    "runAttempt": self.subject["runAttempt"]},
                "authorization": {
                    "repository": "FS-GG/.github", "repositoryId": 1269292704,
                    "workflowPath": ".github/workflows/gs2-09-7-seed-admission-authorize.yml",
                    "workflowSha": "4" * 40, "runId": 9001, "runAttempt": 1,
                    "environment": {"id": 22582241959, "branchPolicyId": 60823087,
                                    "waitTimerMinutes": 5}},
                }


class AdmissionTests(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime(2026, 9, 28, 8, 5, tzinfo=dt.timezone.utc)
        candidate = "b" * 40
        nonce = "12345-2-" + candidate
        self.subject = {
            "workflowRepository": "FS-GG/.github",
            "workflowPath": ".github/workflows/github-substrate-v2-sandbox-qualification.yml",
            "environment": "github-substrate-v2-sandbox", "workflowSha": "a" * 40,
            "runId": 12345, "runAttempt": 2, "candidateSha": candidate,
            "runNonce": nonce, "approvedArtifactSourceSha256": "c" * 64,
            "sourceManifestSha256": "c" * 64,
            "prestateSha256": "5" * 64,
            "prestateSnapshotSha256": "6" * 64,
            "prestateEvidenceSha256": "7" * 64,
            "expectedRefAbsent": True,
            "sandboxRepositoryId": 1353050537, "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
            "projectNodeId": "PVT_kwDOEYAWY84BiESo", "appId": 4166418,
            "installationId": 143110413, "seedPlanSha256": "d" * 64,
            "s2DeclarationSha256": "e" * 64,
            "mintProofSha256": "9" * 64, "tokenSha256": "8" * 64,
            "refName": "refs/heads/gs2-09-7/" + nonce + "/seed-journal",
            "blobOid": "1" * 40, "treeOid": "2" * 40, "commitOid": "3" * 40,
            "expectedOldOid": None, "operation": "genesis-nonce-seed-journal",
        }
        self.port = Port(self.subject, self.now)

    def installed(self):
        return mock.patch.multiple(admission, PINNED_ORIGIN="https://authority.example",
                                   PINNED_RESOURCE_ID="protected-q4-seed",
                                   PINNED_ENDPOINT="https://authority.example/q4")

    def test_unconfigured_is_a_hard_boundary(self):
        with self.assertRaisesRegex(admission.Refused, "unconfigured"):
            admission.require_admitted(self.port, self.subject, self.now)
        with self.assertRaisesRegex(admission.Refused, "prepare-admission-unconfigured"):
            admission.require_prepare_admitted(None, {
                "workflowSha": "a" * 40, "candidateSha": "b" * 40,
                "runId": 12345, "runAttempt": 2,
                "runNonce": "12345-2-" + "b" * 40,
            }, "c" * 64, "d" * 64, self.now)

    def test_exact_protected_readback_admits_only_one_genesis(self):
        with self.installed():
            record = admission.require_admitted(self.port, self.subject, self.now)
            self.assertEqual("admitted", record["state"])
            for name, replacement in (("candidateSha", "f" * 40),
                                      ("seedPlanSha256", "f" * 64),
                                      ("tokenSha256", "f" * 64),
                                      ("prestateEvidenceSha256", "f" * 64),
                                      ("expectedRefAbsent", False),
                                      ("commitOid", "f" * 40),
                                      ("expectedOldOid", "f" * 40),
                                      ("operation", "create-nonce-issue")):
                with self.subTest(name=name):
                    changed = {**self.subject, name: replacement}
                    with self.assertRaises(admission.Refused):
                        admission.require_admitted(self.port, changed, self.now)

    def test_stale_or_foreign_readback_refuses(self):
        with self.installed(), mock.patch.object(self.port, "read_decision", wraps=self.port.read_decision) as read:
            with self.assertRaisesRegex(admission.Refused, "expiry"):
                admission.require_admitted(self.port, self.subject,
                                           self.now + dt.timedelta(minutes=6))
            self.assertTrue(read.called)
        with self.installed(), mock.patch.object(self.port, "describe", return_value={"origin": "foreign"}):
            with self.assertRaisesRegex(admission.Refused, "port"):
                admission.require_admitted(self.port, self.subject, self.now)


if __name__ == "__main__":
    unittest.main()

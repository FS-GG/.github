import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/gs2-09-7-seed-execution-binding.py"
SPEC = importlib.util.spec_from_file_location("seed_execution_binding", SCRIPT)
binding = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(binding)


class SeedExecutionBindingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.evidence = Path(self.temporary.name)
        self.plan = self.evidence / "seed-plan.json"
        self.corpus = self.evidence / "seed-corpus.json"
        self.mint = self.evidence / "mint-grants.json"
        self.output = self.evidence / "seed-execution-binding.json"
        self.plan.write_bytes(b'{"plan":"retained"}\n')
        self.corpus.write_bytes(b'{"corpus":"retained"}\n')
        self.token = "ghs_" + "t" * 40
        self.now = dt.datetime(2026, 9, 28, 8, 0, tzinfo=dt.timezone.utc)
        self.workflow_sha = "a" * 40
        self.candidate_sha = "b" * 40
        self.run_nonce = f"12345-2-{self.candidate_sha}"
        self.journal_ref = f"refs/heads/gs2-09-7/{self.run_nonce}/seed-journal"
        self.artifact_source_sha256 = "e" * 64
        self.mint.write_text(json.dumps({
            "schema": binding.MINT_SCHEMA,
            "appId": binding.APP_ID,
            "appSlug": binding.APP_SLUG,
            "actor": binding.ACTOR,
            "installationId": binding.INSTALLATION_ID,
            "repositorySelection": "selected",
            "repository": {"id": binding.SANDBOX_ID, "nodeId": binding.SANDBOX_NODE,
                           "fullName": binding.SANDBOX_NAME},
            "permissions": binding.REQUIRED_GRANTS,
            "expiresAt": "2026-09-28T09:00:00Z",
            "tokenSha256": hashlib.sha256(self.token.encode()).hexdigest(),
            "mintResponseSha256": "c" * 64,
            "viewerResponseSha256": "d" * 64,
        }, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
        self.environment = {
            "GITHUB_ACTIONS": "true",
            "CI": "true",
            "GITHUB_EVENT_NAME": "workflow_dispatch",
            "GITHUB_REPOSITORY": binding.HOST_REPOSITORY,
            "GITHUB_REF": binding.WORKFLOW_REF,
            "GITHUB_WORKFLOW_REF": f"{binding.HOST_REPOSITORY}/{binding.WORKFLOW_PATH}@{binding.WORKFLOW_REF}",
            "GITHUB_WORKFLOW_SHA": self.workflow_sha,
            "GITHUB_SHA": self.workflow_sha,
            "FSGG_PROTECTED_SHA": self.workflow_sha,
            "FSGG_PROTECTED_ENVIRONMENT": binding.PROTECTED_ENVIRONMENT,
            "GITHUB_RUN_ID": "12345",
            "GITHUB_RUN_ATTEMPT": "2",
            "FSGG_CANDIDATE_SHA": self.candidate_sha,
            "FSGG_SANDBOX_RUN_NONCE": self.run_nonce,
            "FSGG_SANDBOX_REPOSITORY_ID": str(binding.SANDBOX_ID),
            "FSGG_SANDBOX_REPOSITORY_NODE_ID": binding.SANDBOX_NODE,
            "FSGG_SANDBOX_PROJECT_NUMBER": str(binding.PROJECT_NUMBER),
            "FSGG_SANDBOX_PROJECT_NODE_ID": binding.PROJECT_NODE,
            "FSGG_SEED_JOURNAL_REF": self.journal_ref,
            "FSGG_APPROVED_ARTIFACT_SOURCE_SHA256": self.artifact_source_sha256,
            "FSGG_SANDBOX_EVIDENCE_DIR": str(self.evidence),
            "FSGG_SEED_PLAN_PATH": str(self.plan),
            "FSGG_SEED_CORPUS_PATH": str(self.corpus),
            "FSGG_SANDBOX_MINT_PROOF": str(self.mint),
            "FSGG_SANDBOX_TOKEN": self.token,
            "FSGG_SEED_EXECUTION_BINDING": str(self.output),
        }
        self.provenance = {
            "checkoutHead": self.workflow_sha,
            "builder": {"path": binding.BUILDER_PATH, "sha256": "1" * 64},
            "workflow": {"path": binding.WORKFLOW_PATH, "sha256": "2" * 64},
            "nativeCas": {"path": binding.NATIVE_CAS_PATH, "sha256": "3" * 64},
        }

    def installed(self):
        return mock.patch.object(binding, "INSTALLATION_STATUS",
                                 "installed-fixed-main-workflow")

    def protected_checkout(self):
        return mock.patch.object(binding, "checked_out_provenance",
                                 return_value=self.provenance)

    def build(self):
        with mock.patch.dict(os.environ, self.environment, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            return binding.build_document()

    def test_source_status_is_explicitly_uninstalled_and_cannot_build(self):
        self.assertEqual({
            "schema": binding.STATUS_SCHEMA,
            "installation": "source-only-uninstalled",
            "protectedProvenance": "workflow-sha-event-sha-checkout-head-and-git-blobs",
            "installationJoin": "install-fixed-main-native-cas-and-independent-readback",
            "seedRulesetPrerequisite": False,
            "privateRulesetRead": "unsupported-403-do-not-infer",
            "productionProtectionAuthority": "GS2-08.2-separate-evidence",
            "writesEnabled": False,
            "authority": "unavailable",
        }, binding.source_status())
        with mock.patch.dict(os.environ, self.environment, clear=True):
            with self.assertRaisesRegex(binding.Refused, "uninstalled"):
                binding.build_document()

    def test_build_binds_exact_context_mint_and_retained_bytes(self):
        raw = self.build()
        value = binding.strict_json(raw)
        self.assertFalse(value["activation"])
        self.assertEqual("bound-no-write-authority", value["status"])
        self.assertEqual(self.workflow_sha, value["source"]["workflowSha"])
        self.assertEqual(binding.NATIVE_CAS_PATH, value["source"]["nativeCasPath"])
        self.assertEqual(self.provenance["nativeCas"],
                         value["source"]["protectedCheckout"]["nativeCas"])
        self.assertEqual(self.candidate_sha, value["source"]["candidateSha"])
        self.assertEqual(binding.PROTECTED_ENVIRONMENT,
                         value["source"]["protectedEnvironment"])
        self.assertEqual(self.journal_ref, value["source"]["seedJournalRef"])
        self.assertEqual(self.artifact_source_sha256,
                         value["provenanceInterface"]["approvedArtifactSourceSha256"])
        self.assertEqual(hashlib.sha256(self.plan.read_bytes()).hexdigest(),
                         value["artifacts"]["seedPlan"]["sha256"])
        self.assertEqual(hashlib.sha256(self.corpus.read_bytes()).hexdigest(),
                         value["artifacts"]["corpus"]["sha256"])
        self.assertEqual(hashlib.sha256(self.mint.read_bytes()).hexdigest(),
                         value["mint"]["proofSha256"])
        self.assertEqual(binding.APP_ID, value["mint"]["appId"])
        self.assertEqual(binding.INSTALLATION_ID, value["mint"]["installationId"])
        self.assertEqual("selected", value["mint"]["repositorySelection"])
        self.assertEqual(binding.REQUIRED_GRANTS, value["mint"]["permissions"])
        self.assertEqual(binding.ALLOWED_EFFECT_KINDS,
                         value["journal"]["allowedClosedEffectKinds"])
        profile = value["journal"]["profile"]
        self.assertEqual(self.journal_ref, profile["ref"])
        self.assertEqual("expected-absent-or-exact-old-oid",
                         profile["compareAndSwap"]["lease"])
        self.assertEqual(["ref", "commit", "tree", "blob"],
                         profile["nativeReadback"]["requiredObjects"])
        self.assertFalse(profile["protectionEvidence"]["q4SeedRulesetPrerequisite"])
        self.assertEqual("GS2-08.2-separate-evidence",
                         profile["protectionEvidence"]["productionProtectionAuthority"])
        self.assertNotIn(self.token, raw.decode())
        self.assertEqual(raw, binding.canonical(value))

    def test_context_spoof_wrong_run_ref_project_candidate_and_nonce_refuse(self):
        cases = [
            ("FSGG_WORKFLOW_SHA", self.workflow_sha, "caller-context-spoof"),
            ("GITHUB_WORKFLOW_SHA", "e" * 40, "protected-context"),
            ("FSGG_PROTECTED_ENVIRONMENT", "unprotected", "protected-context"),
            ("GITHUB_RUN_ID", "12346", "protected-context"),
            ("GITHUB_RUN_ATTEMPT", "3", "protected-context"),
            ("FSGG_SANDBOX_PROJECT_NODE_ID", "PVT_wrong", "protected-context"),
            ("FSGG_SANDBOX_PROJECT_NUMBER", "3", "protected-context"),
            ("FSGG_CANDIDATE_SHA", "C" * 40, "seed-journal-run-nonce"),
            ("FSGG_SANDBOX_RUN_NONCE", "spoofed", "protected-context"),
            ("FSGG_SEED_JOURNAL_REF", "refs/heads/main", "protected-context"),
            ("FSGG_APPROVED_ARTIFACT_SOURCE_SHA256", "f" * 63, "protected-context"),
        ]
        for name, value, reason in cases:
            with self.subTest(name=name):
                changed = {**self.environment, name: value}
                with mock.patch.dict(os.environ, changed, clear=True), self.installed(), \
                        self.protected_checkout(), \
                        mock.patch.object(binding, "utc_now", return_value=self.now):
                    with self.assertRaisesRegex(binding.Refused, reason):
                        binding.build_document()

    def test_wrong_token_mint_proof_and_expiry_refuse(self):
        changed = {**self.environment, "FSGG_SANDBOX_TOKEN": self.token + "x"}
        with mock.patch.dict(os.environ, changed, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            with self.assertRaisesRegex(binding.Refused, "mint-proof-digest"):
                binding.build_document()
        original = self.mint.read_bytes()
        self.mint.write_bytes(original.replace(b'"appId":4166418', b'"appId":4166419'))
        with self.assertRaisesRegex(binding.Refused, "mint-proof-identity"):
            self.build()
        self.mint.write_bytes(original.replace(b'"installationId":143110413',
                                               b'"installationId":143110414'))
        with self.assertRaisesRegex(binding.Refused, "mint-proof-identity"):
            self.build()
        self.mint.write_bytes(original.replace(b'"metadata":"read",', b''))
        with self.assertRaisesRegex(binding.Refused, "mint-proof-identity"):
            self.build()
        changed_grants = json.loads(original)
        changed_grants["permissions"]["workflows"] = "write"
        self.mint.write_text(json.dumps(changed_grants, sort_keys=True, separators=(",", ":")) + "\n")
        with self.assertRaisesRegex(binding.Refused, "mint-proof-identity"):
            self.build()
        self.mint.write_bytes(original.replace(b"2026-09-28T09:00:00Z", b"2026-09-28T07:00:00Z"))
        with self.assertRaisesRegex(binding.Refused, "mint-proof-expiry"):
            self.build()

    def test_missing_or_ambiguous_retained_bytes_refuse(self):
        self.plan.unlink()
        with self.assertRaisesRegex(binding.Refused, "retained-bytes-unavailable"):
            self.build()
        self.plan.write_bytes(self.corpus.read_bytes())
        with self.assertRaisesRegex(binding.Refused, "retained-artifacts-ambiguous"):
            self.build()

    def test_verifier_refuses_changed_source_candidate_plan_and_ambiguous_output(self):
        raw = self.build()
        self.output.write_bytes(raw)
        with mock.patch.dict(os.environ, self.environment, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            binding.verify_document(self.output)
            with self.assertRaisesRegex(binding.Refused, "ambiguous-output"):
                binding.write_new(self.output, raw)
        self.plan.write_bytes(b'{"plan":"changed"}\n')
        with mock.patch.dict(os.environ, self.environment, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            with self.assertRaisesRegex(binding.Refused, "execution-binding-mismatch"):
                binding.verify_document(self.output)
        self.plan.write_bytes(b'{"plan":"retained"}\n')
        changed = {**self.environment, "FSGG_CANDIDATE_SHA": "e" * 40,
                   "FSGG_SANDBOX_RUN_NONCE": f'12345-2-{"e" * 40}',
                   "FSGG_SEED_JOURNAL_REF":
                       f'refs/heads/gs2-09-7/12345-2-{"e" * 40}/seed-journal'}
        with mock.patch.dict(os.environ, changed, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            with self.assertRaisesRegex(binding.Refused, "execution-binding-mismatch"):
                binding.verify_document(self.output)
        original_read = binding.read_regular
        def changed_source(path, limit):
            raw = original_read(path, limit)
            return raw + b"\n" if path == SCRIPT else raw
        with mock.patch.dict(os.environ, self.environment, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now), \
                mock.patch.object(binding, "read_regular", side_effect=changed_source):
            with self.assertRaisesRegex(binding.Refused, "execution-binding-mismatch"):
                binding.verify_document(self.output)

    def test_checkout_head_builder_and_workflow_drift_refuse(self):
        with mock.patch.object(binding, "git_bytes", return_value=b"e" * 40 + b"\n"):
            with self.assertRaisesRegex(binding.Refused, "protected-checkout-head"):
                binding.checked_out_provenance(self.workflow_sha)

        current_builder = b"builder"
        current_workflow = b"workflow"
        with mock.patch.object(binding, "git_bytes", side_effect=[
                    self.workflow_sha.encode() + b"\n", b"changed-builder"]), \
                mock.patch.object(binding, "read_regular", return_value=current_builder):
            with self.assertRaisesRegex(binding.Refused, "protected-builder-drift"):
                binding.checked_out_provenance(self.workflow_sha)

        with mock.patch.object(binding, "git_bytes", side_effect=[
                    self.workflow_sha.encode() + b"\n", current_builder, b"changed-workflow"]), \
                mock.patch.object(binding, "read_regular",
                                  side_effect=[current_builder, current_workflow]):
            with self.assertRaisesRegex(binding.Refused, "protected-workflow-drift"):
                binding.checked_out_provenance(self.workflow_sha)

        with mock.patch.object(binding, "git_bytes", side_effect=[
                    self.workflow_sha.encode() + b"\n", current_builder,
                    current_workflow, b"changed-native-cas"]), \
                mock.patch.object(binding, "read_regular",
                                  side_effect=[current_builder, current_workflow, b"native-cas"]):
            with self.assertRaisesRegex(binding.Refused, "protected-nativeCas-drift"):
                binding.checked_out_provenance(self.workflow_sha)

    def test_duplicate_json_and_noncanonical_binding_refuse(self):
        with self.assertRaisesRegex(binding.Refused, "duplicate-json-member"):
            binding.strict_json(b'{"schema":"x","schema":"y"}')
        self.output.write_bytes(self.build().replace(b'"activation":false', b'"activation": false'))
        with mock.patch.dict(os.environ, self.environment, clear=True), self.installed(), \
                self.protected_checkout(), \
                mock.patch.object(binding, "utc_now", return_value=self.now):
            with self.assertRaisesRegex(binding.Refused, "execution-binding-mismatch"):
                binding.verify_document(self.output)

    def test_cas_profile_ref_and_generation_contract_fail_closed(self):
        profile = binding.cas_profile(self.journal_ref)
        self.assertEqual("genesis-zero-then-strict-successor", profile["chain"]["journalGeneration"])
        self.assertEqual("genesis-zero-then-strict-successor", profile["chain"]["stateGeneration"])
        self.assertEqual("state.json", profile["object"]["path"])
        self.assertEqual("pending-until-exact-readback",
                         profile["nativeReadback"]["lostResponse"])
        for bad in ("refs/heads/main",
                    f"refs/heads/gs2-09-7/{self.run_nonce}/other",
                    "refs/heads/gs2-09-7/1-1-" + "F" * 40 + "/seed-journal"):
            with self.assertRaises(binding.Refused):
                binding.cas_profile(bad)


if __name__ == "__main__":
    unittest.main()

import hashlib
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "host_recovery_worker", ROOT / "scripts/gs2-09-7-host-recovery-worker.py")
worker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(worker)

ORIGIN = "https://protected.example.invalid"
FINALIZER_RESOURCE = "finalizer-ledger-test"
FINALIZER_ENDPOINT = ORIGIN + "/pending"
VAULT = "protected-token-vault-test"
REVOKER = "protected-native-revoker-test"
RECOVERY_RESOURCE = "recovery-journal-test"
RECOVERY_ENDPOINT = ORIGIN + "/recovery"
WORKER_ID = "protected-recovery-worker-test"
MINT_ID = "a" * 64
BINDING_ID = "b" * 64
TOKEN = "fake-recovery-token-never-valid-outside-test"


class FakePort:
    def __init__(self):
        self.calls = []
        self.token = TOKEN
        self.token_sha256 = hashlib.sha256(TOKEN.encode()).hexdigest()
        self.claim = None
        self.receipt = None
        self.claim_response_lost = False
        self.false_claim_commit = False
        self.receipt_response_lost = False
        self.revoke_response_lost = False
        self.observations = ["active", "revoked"]
        self.finalizer_descriptor = {
            "schema": worker.finalizer.FINALIZER_SCHEMA,
            "origin": ORIGIN, "resourceId": FINALIZER_RESOURCE,
            "endpoint": FINALIZER_ENDPOINT, "vaultId": VAULT,
            "durable": True, "atomicCas": True, "nativeReadback": True,
            "escrowEncrypted": True, "credentialScope": "protected-host-only",
            "candidateCanWrite": False, "apiOrigin": "https://api.github.com",
            "nativeRevocation": True,
        }
        self.vault_descriptor = {
            "schema": worker.finalizer.VAULT_SCHEMA, "vaultId": VAULT,
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
            "encrypted": True, "durable": True,
        }
        self.revoker_descriptor = {
            "schema": worker.finalizer.REVOKER_SCHEMA, "resourceId": REVOKER,
            "apiOrigin": "https://api.github.com",
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
            "nativeObservation": True,
        }
        self.recovery_descriptor = {
            "schema": worker.WORKER_SCHEMA, "origin": ORIGIN,
            "resourceId": RECOVERY_RESOURCE, "endpoint": RECOVERY_ENDPOINT,
            "workerId": WORKER_ID, "durable": True, "atomicCas": True,
            "nativeReadback": True, "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
        }
        self.mint = {
            "schema": worker.finalizer.MINT_SCHEMA, "mintId": MINT_ID,
            "tokenSha256": self.token_sha256, "contextSha256": "c" * 64,
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": 123, "vaultId": VAULT, "escrowed": True,
        }
        self.binding = {
            "schema": worker.BINDING_SCHEMA, "mintId": MINT_ID,
            "bindingId": BINDING_ID, "tokenSha256": self.token_sha256,
            "contextSha256": self.mint["contextSha256"],
            "sandboxRepositoryId": worker.finalizer.release.host.SANDBOX_ID,
            "appId": worker.finalizer.release.host.APP_ID,
            "actor": worker.finalizer.release.host.ACTOR,
            "installationId": self.mint["installationId"],
            "vaultId": VAULT, "finalizerResourceId": FINALIZER_RESOURCE,
            "verifiedAtCommit": True,
        }
        self.pending = {
            "schema": worker.finalizer.PENDING_SCHEMA, "mintId": MINT_ID,
            "tokenSha256": self.token_sha256, "revokeRequired": True,
        }

    def describe(self):
        return self.finalizer_descriptor

    def describe_vault(self):
        return self.vault_descriptor

    def describe_revoker(self):
        return self.revoker_descriptor

    def describe_recovery(self):
        return self.recovery_descriptor

    def load_mint(self, mint_id):
        self.calls.append("load-mint")
        return self.mint

    def recover_token(self, mint_id):
        self.calls.append("recover-token")
        return self.token

    def append_pending(self, mint_id, token_sha256):
        raise AssertionError("recovery must not create a new release intent")

    def read_pending(self, mint_id):
        self.calls.append("read-pending")
        return self.pending

    def revoke(self, token):
        self.calls.append("native-revoke")
        if self.revoke_response_lost:
            raise OSError("response lost after native revoke")
        return "accepted"

    def observe(self, token):
        self.calls.append("native-observe")
        return self.observations.pop(0) if self.observations else "unknown"

    def append_revoked(self, mint_id, token_sha256):
        raise AssertionError("recovery must use its bound receipt")

    def read_revoked(self, mint_id):
        return None

    def read_binding(self, mint_id):
        self.calls.append("read-binding")
        return self.binding

    def claim_recovery_once(self, mint_id, binding_id):
        self.calls.append("claim-recovery")
        if self.claim is not None:
            return "duplicate"
        if self.false_claim_commit:
            return "committed"
        self.claim = {
            "schema": worker.CLAIM_SCHEMA, "mintId": mint_id,
            "bindingId": binding_id, "tokenSha256": self.token_sha256,
            "workerId": WORKER_ID,
        }
        if self.claim_response_lost:
            raise OSError("response lost after recovery claim")
        return "committed"

    def read_recovery_claim(self, mint_id):
        self.calls.append("read-claim")
        return self.claim

    def append_recovery_receipt(self, mint_id, binding_id, token_sha256):
        self.calls.append("append-receipt")
        self.receipt = {
            "schema": worker.RECEIPT_SCHEMA, "mintId": mint_id,
            "bindingId": binding_id, "tokenSha256": token_sha256,
            "workerId": WORKER_ID, "providerState": "revoked",
        }
        if self.receipt_response_lost:
            raise OSError("response lost after receipt commit")
        return "committed"

    def read_recovery_receipt(self, mint_id):
        self.calls.append("read-receipt")
        return self.receipt


class RecoveryWorkerTests(unittest.TestCase):
    def setUp(self):
        self.port = FakePort()
        pins = {
            (worker, "PINNED_RECOVERY_ORIGIN"): ORIGIN,
            (worker, "PINNED_RECOVERY_RESOURCE_ID"): RECOVERY_RESOURCE,
            (worker, "PINNED_RECOVERY_ENDPOINT"): RECOVERY_ENDPOINT,
            (worker, "PINNED_RECOVERY_WORKER_ID"): WORKER_ID,
            (worker.finalizer, "PINNED_FINALIZER_ORIGIN"): ORIGIN,
            (worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"): FINALIZER_RESOURCE,
            (worker.finalizer, "PINNED_FINALIZER_ENDPOINT"): FINALIZER_ENDPOINT,
            (worker.finalizer, "PINNED_TOKEN_VAULT_ID"): VAULT,
            (worker.finalizer, "PINNED_REVOKER_ID"): REVOKER,
        }
        for (module, name), value in pins.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def run_worker(self):
        return worker.recover_one(self.port, MINT_ID, BINDING_ID)

    def test_fresh_exact_claim_revokes_once_and_requires_native_and_receipt_readback(self):
        result = self.run_worker()
        self.assertEqual("fresh", result["claim"])
        self.assertEqual("revoked", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(1, self.port.calls.count("native-revoke"))
        self.assertEqual(2, self.port.calls.count("native-observe"))
        self.assertIn("read-receipt", self.port.calls)
        self.assertNotIn(TOKEN, json.dumps(result))

    def test_duplicate_active_claim_never_repeats_native_effect(self):
        self.port.claim = {"schema": worker.CLAIM_SCHEMA, "mintId": MINT_ID,
                           "bindingId": BINDING_ID,
                           "tokenSha256": self.port.token_sha256,
                           "workerId": WORKER_ID}
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("duplicate", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_lost_claim_response_never_authorizes_native_effect(self):
        self.port.claim_response_lost = True
        self.port.observations = ["active"]
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertIsNotNone(self.port.claim)
        self.assertNotIn("native-revoke", self.port.calls)

    def test_false_committed_claim_without_readback_refuses_native_effect(self):
        self.port.false_claim_commit = True
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertNotIn("native-observe", self.port.calls)

    def test_duplicate_revoked_claim_can_finish_receipt_without_new_effect(self):
        self.port.claim = {"schema": worker.CLAIM_SCHEMA, "mintId": MINT_ID,
                           "bindingId": BINDING_ID,
                           "tokenSha256": self.port.token_sha256,
                           "workerId": WORKER_ID}
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIn("append-receipt", self.port.calls)

    def test_lost_claim_response_can_finish_only_preobserved_revocation(self):
        self.port.claim_response_lost = True
        self.port.observations = ["revoked"]
        result = self.run_worker()
        self.assertEqual("unknown", result["claim"])
        self.assertEqual("revoked", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_unknown_native_observation_refuses_mutation(self):
        self.port.observations = ["unknown"]
        result = self.run_worker()
        self.assertEqual("pending", result["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_lost_native_response_needs_provider_readback(self):
        self.port.revoke_response_lost = True
        self.port.observations = ["active", "revoked"]
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.port = FakePort()
        self.port.revoke_response_lost = True
        self.port.observations = ["active", "active"]
        second = self.run_worker()
        self.assertEqual("pending", second["revocation"])

    def test_lost_receipt_response_requires_exact_durable_readback(self):
        self.port.receipt_response_lost = True
        result = self.run_worker()
        self.assertEqual("revoked", result["revocation"])
        self.port = FakePort()
        self.port.read_recovery_receipt = lambda _mint_id: None
        second = self.run_worker()
        self.assertEqual("pending", second["revocation"])

    def test_wrong_binding_or_mint_identity_refuses_before_claim(self):
        self.port.binding["bindingId"] = "d" * 64
        with self.assertRaisesRegex(worker.Refused, "recovery-binding"):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)
        self.port.binding["bindingId"] = BINDING_ID
        self.port.mint["vaultId"] = "foreign-vault"
        with self.assertRaises(worker.finalizer.Refused):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)

    def test_missing_pending_intent_or_foreign_store_refuses_before_claim(self):
        self.port.pending = None
        with self.assertRaisesRegex(worker.Refused, "pending-intent"):
            self.run_worker()
        self.port = FakePort()
        self.port.finalizer_descriptor["candidateCanWrite"] = True
        with self.assertRaises(worker.finalizer.Refused):
            self.run_worker()
        self.assertNotIn("claim-recovery", self.port.calls)

    def test_absent_pin_or_candidate_writable_worker_refuses_before_vault(self):
        worker.PINNED_RECOVERY_RESOURCE_ID = ""
        with self.assertRaisesRegex(worker.Refused, "recovery-unconfigured"):
            self.run_worker()
        self.assertEqual([], self.port.calls)
        worker.PINNED_RECOVERY_RESOURCE_ID = RECOVERY_RESOURCE
        self.port.recovery_descriptor["candidateCanWrite"] = True
        with self.assertRaisesRegex(worker.Refused, "recovery-authority"):
            self.run_worker()
        self.assertEqual([], self.port.calls)


if __name__ == "__main__":
    unittest.main()

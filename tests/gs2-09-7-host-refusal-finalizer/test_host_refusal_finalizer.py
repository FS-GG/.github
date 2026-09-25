import hashlib
import importlib.util
import json
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


finalizer = load("host_refusal_finalizer", ROOT / "scripts/gs2-09-7-host-refusal-finalizer.py")
fixture_module = load("host_binding_fixture_for_finalizer", ROOT / "tests/gs2-09-7-host-run-binding/test_host_run_binding.py")

ORIGIN = "https://protected.example.invalid"
RESOURCE = "finalizer-ledger-test"
ENDPOINT = ORIGIN + "/pending"
VAULT = "protected-token-vault-test"
REVOKER = "protected-native-revoker-test"
MINT_ID = "a" * 64


class FakeFinalizerPort:
    def __init__(self, token, context):
        self.token = token
        self.calls = []
        self.pending = None
        self.revoked = None
        self.lose_pending_response = False
        self.false_pending_commit = False
        self.cancel_pending = False
        self.lose_revoked_response = False
        self.native_response_lost = False
        self.native_observation = "revoked"
        self.descriptor = {
            "schema": finalizer.FINALIZER_SCHEMA,
            "origin": ORIGIN,
            "resourceId": RESOURCE,
            "endpoint": ENDPOINT,
            "vaultId": VAULT,
            "durable": True,
            "atomicCas": True,
            "nativeReadback": True,
            "escrowEncrypted": True,
            "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
            "apiOrigin": "https://api.github.com",
            "nativeRevocation": True,
        }
        self.vault_descriptor = {
            "schema": finalizer.VAULT_SCHEMA, "vaultId": VAULT,
            "credentialScope": "protected-host-only",
            "candidateCanRead": False, "candidateCanWrite": False,
            "encrypted": True, "durable": True,
        }
        self.revoker_descriptor = {
            "schema": finalizer.REVOKER_SCHEMA, "resourceId": REVOKER,
            "apiOrigin": "https://api.github.com",
            "credentialScope": "protected-host-only", "candidateCanWrite": False,
            "nativeObservation": True,
        }
        self.mint = {
            "schema": finalizer.MINT_SCHEMA, "mintId": MINT_ID,
            "tokenSha256": hashlib.sha256(token.encode()).hexdigest(),
            "contextSha256": finalizer.digest_context(context),
            "sandboxRepositoryId": finalizer.release.host.SANDBOX_ID,
            "appId": finalizer.release.host.APP_ID,
            "actor": finalizer.release.host.ACTOR,
            "installationId": 123,
            "vaultId": VAULT, "escrowed": True,
        }

    def describe(self):
        self.calls.append("describe-store")
        return self.descriptor

    def describe_vault(self):
        self.calls.append("describe-vault")
        return self.vault_descriptor

    def describe_revoker(self):
        self.calls.append("describe-revoker")
        return self.revoker_descriptor

    def load_mint(self, mint_id):
        self.calls.append("load-mint")
        return self.mint

    def recover_token(self, mint_id):
        self.calls.append("recover-token")
        return self.token

    def append_pending(self, mint_id, token_sha256):
        self.calls.append("append-pending")
        if self.pending is not None:
            return "duplicate"
        if self.false_pending_commit:
            return "committed"
        self.pending = {"schema": finalizer.PENDING_SCHEMA,
                        "mintId": mint_id, "tokenSha256": token_sha256,
                        "revokeRequired": True}
        if self.cancel_pending:
            raise KeyboardInterrupt()
        if self.lose_pending_response:
            raise OSError("lost after pending commit")
        return "committed"

    def read_pending(self, mint_id):
        self.calls.append("read-pending")
        return self.pending

    def revoke(self, token):
        self.calls.append("native-revoke")
        if self.native_response_lost:
            raise OSError("native response lost")
        return "accepted"

    def observe(self, token):
        self.calls.append("native-observe")
        return self.native_observation

    def append_revoked(self, mint_id, token_sha256):
        self.calls.append("append-revoked")
        self.revoked = {"schema": finalizer.REVOKED_SCHEMA,
                        "mintId": mint_id, "tokenSha256": token_sha256}
        if self.lose_revoked_response:
            raise OSError("lost after receipt commit")
        return "committed"

    def read_revoked(self, mint_id):
        self.calls.append("read-revoked")
        return self.revoked


class FakeReleasePort:
    def __init__(self):
        self.claims = set()
        self.invocations = 0
        self.invoke_result = "complete"
        self.on_invoke = None

    def claim_once(self, binding_id):
        if binding_id in self.claims:
            return "duplicate"
        self.claims.add(binding_id)
        return "granted"

    def invoke_candidate_once(self, token, binding):
        self.invocations += 1
        if self.on_invoke is not None:
            self.on_invoke()
        return self.invoke_result

    def revoke(self, token):
        return "confirmed"


class HostRefusalFinalizerTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        fixture_module.HostRunBindingTests.setUpClass()

    @classmethod
    def tearDownClass(cls):
        fixture_module.HostRunBindingTests.tearDownClass()

    def setUp(self):
        self.fixture = fixture_module.HostRunBindingTests(
            "test_exact_host_envelope_matches_candidate_canonical_signature_contract")
        self.fixture.setUp()
        self.envelope = self.fixture.build()
        self.proof = self.fixture.raw(self.fixture.proof)
        self.public = self.fixture.public_path.read_bytes()
        self.port = FakeFinalizerPort(self.fixture.token, self.fixture.context)
        self.release_port = FakeReleasePort()
        pins = (finalizer.PINNED_FINALIZER_ORIGIN,
                finalizer.PINNED_FINALIZER_RESOURCE_ID,
                finalizer.PINNED_FINALIZER_ENDPOINT,
                finalizer.PINNED_TOKEN_VAULT_ID,
                finalizer.PINNED_REVOKER_ID,
                finalizer.release.host.PINNED_SPKI_SHA256,
                finalizer.release.host.PINNED_WORKFLOW_SHA)
        finalizer.PINNED_FINALIZER_ORIGIN = ORIGIN
        finalizer.PINNED_FINALIZER_RESOURCE_ID = RESOURCE
        finalizer.PINNED_FINALIZER_ENDPOINT = ENDPOINT
        finalizer.PINNED_TOKEN_VAULT_ID = VAULT
        finalizer.PINNED_REVOKER_ID = REVOKER
        finalizer.release.host.PINNED_SPKI_SHA256 = self.fixture.pin
        finalizer.release.host.PINNED_WORKFLOW_SHA = self.fixture.context["workflowSha"]
        fixture_module.configure_admission(
            self, finalizer.release.host, self.fixture.context, self.fixture.pin,
            self.fixture.now)
        for name, value in zip(("PINNED_FINALIZER_ORIGIN",
                                "PINNED_FINALIZER_RESOURCE_ID",
                                "PINNED_FINALIZER_ENDPOINT",
                                "PINNED_TOKEN_VAULT_ID",
                                "PINNED_REVOKER_ID"), pins[:5]):
            self.addCleanup(setattr, finalizer, name, value)
        self.addCleanup(setattr, finalizer.release.host,
                        "PINNED_SPKI_SHA256", pins[5])
        self.addCleanup(setattr, finalizer.release.host,
                        "PINNED_WORKFLOW_SHA", pins[6])

    def run_finalizer(self, envelope=None, proof=None):
        return finalizer.execute_with_finalizer(
            self.envelope if envelope is None else envelope,
            self.proof if proof is None else proof,
            self.fixture.token, self.public, self.fixture.context,
            self.fixture.now, self.release_port, self.port, MINT_ID)

    def test_refused_envelope_still_records_pending_and_revokes(self):
        envelope = json.loads(self.envelope)
        envelope.pop("signatureBase64")
        result = self.run_finalizer(envelope=self.fixture.raw(envelope))
        self.assertEqual("refused-or-unknown", result["release"])
        self.assertEqual("revoked", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertEqual("revoked", self.port.native_observation)
        self.assertIn("append-pending", self.port.calls)
        self.assertIn("native-revoke", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)
        self.assertNotIn(self.fixture.token, json.dumps(result))

    def test_refused_mint_proof_still_revokes_without_candidate_invocation(self):
        result = self.run_finalizer(proof=self.proof + b" ")
        self.assertEqual("refused-or-unknown", result["release"])
        self.assertEqual("revoked", result["revocation"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)

    def test_fake_success_is_still_pending_without_protected_receipt(self):
        result = self.run_finalizer()
        self.assertEqual("complete", result["release"])
        self.assertEqual("revoked", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(1, self.release_port.invocations)

    def test_unknown_invocation_is_not_retried_and_is_pending(self):
        self.release_port.invoke_result = "unknown"
        first = self.run_finalizer()
        second = self.run_finalizer()
        self.assertEqual("pending", first["release"])
        self.assertEqual("pending", first["disposition"])
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual(1, self.release_port.invocations)

    def test_lost_pending_response_blocks_handoff_despite_readback(self):
        self.port.lose_pending_response = True
        first = self.run_finalizer()
        second = self.run_finalizer()
        self.assertEqual("not-invoked", first["release"])
        self.assertEqual("revoked", first["revocation"])
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual(0, self.release_port.invocations)

    def test_false_pending_commit_without_native_readback_blocks_handoff(self):
        self.port.false_pending_commit = True
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual("pending", result["revocation"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)

    def test_pending_readback_without_exact_revoke_intent_blocks_handoff(self):
        def wrong_pending(_mint_id):
            return {"schema": finalizer.PENDING_SCHEMA,
                    "mintId": MINT_ID,
                    "tokenSha256": self.port.mint["tokenSha256"],
                    "revokeRequired": 1}
        self.port.read_pending = wrong_pending
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual("pending", result["revocation"])
        self.assertEqual(0, self.release_port.invocations)

    def test_crash_after_pending_recovers_only_revocation(self):
        digest = hashlib.sha256(self.fixture.token.encode()).hexdigest()
        self.assertEqual("committed", self.port.append_pending(MINT_ID, digest))
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("revoked", recovered["revocation"])
        self.assertEqual("pending", recovered["disposition"])
        self.assertEqual(0, self.release_port.invocations)
        later = self.run_finalizer()
        self.assertEqual("not-invoked", later["release"])

    def test_crash_after_mint_before_pending_attempts_revoke_but_stays_pending(self):
        result = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertIn("native-revoke", self.port.calls)
        self.assertNotIn("append-revoked", self.port.calls)
        self.assertEqual(0, self.release_port.invocations)

    def test_unknown_native_observation_or_receipt_keeps_pending(self):
        self.port.native_observation = "unknown"
        first = self.run_finalizer()
        self.assertEqual("pending", first["revocation"])
        self.assertIsNone(self.port.revoked)
        self.port.native_observation = "revoked"
        self.port.read_revoked = lambda _mint_id: None
        second = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", second["disposition"])

    def test_lost_native_and_receipt_responses_need_readback(self):
        self.port.native_response_lost = True
        self.port.lose_revoked_response = True
        result = self.run_finalizer()
        self.assertEqual("revoked", result["revocation"])
        self.assertIn("native-observe", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)

    def test_store_failure_after_handoff_still_attempts_native_revoke(self):
        def break_store():
            self.port.descriptor["durable"] = False
        self.release_port.on_invoke = break_store
        result = self.run_finalizer()
        self.assertEqual(1, self.release_port.invocations)
        self.assertEqual("pending", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertIn("native-revoke", self.port.calls)
        self.assertIn("native-observe", self.port.calls)
        self.assertNotIn("append-revoked", self.port.calls)

    def test_cancellation_during_handoff_runs_finalizer(self):
        def cancel():
            raise KeyboardInterrupt()
        self.release_port.on_invoke = cancel
        with self.assertRaises(KeyboardInterrupt):
            self.run_finalizer()
        self.assertEqual(1, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)
        self.assertIn("native-observe", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)

    def test_cancellation_after_pending_commit_runs_finalizer(self):
        self.port.cancel_pending = True
        with self.assertRaises(KeyboardInterrupt):
            self.run_finalizer()
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)
        self.assertIn("native-observe", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)

    def test_cancellation_during_mint_lookup_attempts_emergency_revoke(self):
        def cancel_lookup(_mint_id):
            raise KeyboardInterrupt()
        self.port.load_mint = cancel_lookup
        with self.assertRaises(KeyboardInterrupt):
            self.run_finalizer()
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)
        self.assertIn("native-observe", self.port.calls)
        self.assertNotIn("append-revoked", self.port.calls)

    def test_store_failure_on_crash_recovery_uses_separate_vault_and_revoker(self):
        digest = hashlib.sha256(self.fixture.token.encode()).hexdigest()
        self.port.append_pending(MINT_ID, digest)
        self.port.descriptor["candidateCanWrite"] = True
        result = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", result["disposition"])
        self.assertIn("recover-token", self.port.calls)
        self.assertIn("native-revoke", self.port.calls)
        self.assertNotIn("append-revoked", self.port.calls)

    def test_missing_pins_or_foreign_revoker_block_release(self):
        finalizer.PINNED_REVOKER_ID = ""
        with self.assertRaisesRegex(finalizer.Refused, "revoker-unconfigured"):
            self.run_finalizer()
        self.assertEqual(0, self.release_port.invocations)
        finalizer.PINNED_REVOKER_ID = REVOKER
        self.port.revoker_descriptor["credentialScope"] = "candidate-token"
        with self.assertRaisesRegex(finalizer.Refused, "revoker-authority"):
            self.run_finalizer()
        self.assertEqual(0, self.release_port.invocations)

    def test_candidate_writable_store_or_vault_blocks_release_and_attempts_revoke(self):
        for change in ({"candidateCanWrite": True}, {"endpoint": ORIGIN + "/workspace"},
                       {"durable": 1}):
            with self.subTest(change=change):
                self.port.descriptor = {**FakeFinalizerPort(
                    self.fixture.token, self.fixture.context).descriptor, **change}
                self.port.calls.clear()
                result = self.run_finalizer()
                self.assertEqual("not-invoked", result["release"])
                self.assertEqual("pending", result["revocation"])
                self.assertIn("native-revoke", self.port.calls)
                self.assertEqual(0, self.release_port.invocations)
        self.port.descriptor = FakeFinalizerPort(
            self.fixture.token, self.fixture.context).descriptor
        self.port.vault_descriptor["candidateCanWrite"] = True
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertIn("native-revoke", self.port.calls)

    def test_vault_without_explicit_candidate_read_denial_refuses_custody(self):
        self.port.vault_descriptor.pop("candidateCanRead")
        with self.assertRaisesRegex(finalizer.Refused, "vault-authority"):
            finalizer.check_vault(self.port)
        self.port.vault_descriptor["candidateCanRead"] = True
        with self.assertRaisesRegex(finalizer.Refused, "vault-authority"):
            finalizer.check_vault(self.port)

    def test_equal_but_non_string_escrow_cannot_authorize_handoff(self):
        class EqualToAnyToken:
            def __eq__(self, _other):
                return True

        self.port.token = EqualToAnyToken()
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual("pending", result["revocation"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)

    def test_foreign_mint_or_missing_escrow_blocks_release_and_attempts_revoke(self):
        self.port.mint["sandboxRepositoryId"] = 1
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertIn("native-revoke", self.port.calls)
        self.port.mint["sandboxRepositoryId"] = finalizer.release.host.SANDBOX_ID
        self.port.token = "another-fake-token-not-the-minted-one"
        second = self.run_finalizer()
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual(0, self.release_port.invocations)


if __name__ == "__main__":
    unittest.main()

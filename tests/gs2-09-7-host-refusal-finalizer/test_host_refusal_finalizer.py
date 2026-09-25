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
NATIVE_ATTEMPT_RESOURCE = "protected-native-attempt-test"
MINT_ID = "a" * 64


class FakeFinalizerPort:
    def __init__(self, token, context):
        self.token = token
        self.calls = []
        self.pending = None
        self.revoked = None
        self.native_attempt = None
        self.lose_pending_response = False
        self.false_pending_commit = False
        self.cancel_pending = False
        self.lose_revoked_response = False
        self.native_response_lost = False
        self.native_attempt_response_lost = False
        self.stale_native_attempt_commit = False
        self.cancel_native_revoke = False
        self.native_observation = "active"
        self.observations_override = []
        self.descriptor = {
            "schema": finalizer.FINALIZER_SCHEMA,
            "origin": ORIGIN,
            "resourceId": RESOURCE,
            "endpoint": ENDPOINT,
            "vaultId": VAULT,
            "nativeAttemptResourceId": NATIVE_ATTEMPT_RESOURCE,
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
        self.native_observation = "revoked"
        if self.cancel_native_revoke:
            raise KeyboardInterrupt("fake crash after native revoke")
        if self.native_response_lost:
            raise OSError("native response lost")
        return "accepted"

    def observe(self, token):
        self.calls.append("native-observe")
        if self.observations_override:
            return self.observations_override.pop(0)
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

    def claim_native_attempt_once(self, mint_id, token_sha256, attempt_id):
        self.calls.append("claim-native-attempt")
        if self.native_attempt is not None:
            return "duplicate"
        self.native_attempt = finalizer.native_attempt_record(
            mint_id, token_sha256, self.mint["contextSha256"],
            "e" * 64 if self.stale_native_attempt_commit else attempt_id)
        if not self.stale_native_attempt_commit and self.native_attempt["attemptId"] != attempt_id:
            raise AssertionError("foreign native attempt")
        if self.native_attempt_response_lost:
            raise OSError("lost after native-attempt claim")
        return "committed"

    def read_native_attempt(self, mint_id):
        self.calls.append("read-native-attempt")
        return self.native_attempt


class FakeReleasePort:
    def __init__(self):
        self.claims = set()
        self.invocations = 0
        self.revoke_calls = 0
        self.invoke_result = "complete"
        self.on_invoke = None
        self.decision_state = "admitted"

    def claim_once(self, decision_id, binding_id, token_sha256):
        if decision_id in self.claims:
            return "duplicate"
        self.claims.add(decision_id)
        return "granted"

    def invoke_candidate_if_admitted_once(self, decision_id, binding_id,
                                           token_sha256, token, binding):
        if self.decision_state != "admitted":
            return "refused"
        self.invocations += 1
        if self.on_invoke is not None:
            self.on_invoke()
        return self.invoke_result

    def revoke(self, token):
        self.revoke_calls += 1
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
                finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID,
                finalizer.release.host.PINNED_SPKI_SHA256,
                finalizer.release.host.PINNED_WORKFLOW_SHA)
        finalizer.PINNED_FINALIZER_ORIGIN = ORIGIN
        finalizer.PINNED_FINALIZER_RESOURCE_ID = RESOURCE
        finalizer.PINNED_FINALIZER_ENDPOINT = ENDPOINT
        finalizer.PINNED_TOKEN_VAULT_ID = VAULT
        finalizer.PINNED_REVOKER_ID = REVOKER
        finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID = NATIVE_ATTEMPT_RESOURCE
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
        self.addCleanup(setattr, finalizer, "PINNED_NATIVE_ATTEMPT_RESOURCE_ID", pins[5])
        self.addCleanup(setattr, finalizer.release.host,
                        "PINNED_SPKI_SHA256", pins[6])
        self.addCleanup(setattr, finalizer.release.host,
                        "PINNED_WORKFLOW_SHA", pins[7])

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

    def test_missing_shared_attempt_namespace_blocks_candidate_handoff(self):
        self.port.descriptor.pop("nativeAttemptResourceId", None)
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual(0, self.release_port.invocations)

    def test_blank_or_foreign_shared_attempt_pin_blocks_candidate_handoff(self):
        for pin in ("", "foreign-native-attempt-store"):
            with self.subTest(pin=pin):
                self.port = FakeFinalizerPort(self.fixture.token, self.fixture.context)
                finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID = pin
                result = self.run_finalizer()
                self.assertEqual("not-invoked", result["release"])
                self.assertEqual(0, self.release_port.invocations)

    def test_boolean_shared_attempt_identity_cannot_alias_protected_namespace(self):
        finalizer.PINNED_NATIVE_ATTEMPT_RESOURCE_ID = True
        self.port.descriptor["nativeAttemptResourceId"] = True
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual(0, self.release_port.invocations)

    def test_emergency_revoke_unknown_readback_has_no_one_use_proof(self):
        self.port.claim_native_attempt_once = None
        self.port.native_response_lost = True
        self.port.observations_override = ["active", "unknown", "active", "unknown"]
        first = self.run_finalizer()
        second = self.run_finalizer()
        self.assertEqual("not-invoked", first["release"])
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual("pending", first["revocation"])
        self.assertEqual("pending", second["revocation"])
        self.assertEqual(0, self.release_port.invocations)
        self.assertEqual(2, self.port.calls.count("native-revoke"))

    def test_unknown_invocation_is_not_retried_and_is_pending(self):
        self.release_port.invoke_result = "unknown"
        first = self.run_finalizer()
        second = self.run_finalizer()
        self.assertEqual("pending", first["release"])
        self.assertEqual("pending", first["disposition"])
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual(1, self.release_port.invocations)

    def test_unknown_launch_and_unknown_native_observation_stay_pending(self):
        self.release_port.invoke_result = "unknown"
        self.port.native_observation = "unknown"
        first = self.run_finalizer()
        self.assertEqual("pending", first["release"])
        self.assertEqual("pending", first["revocation"])
        self.assertEqual("pending", first["disposition"])
        self.assertEqual(1, self.release_port.revoke_calls)
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIsNone(self.port.revoked)
        second = self.run_finalizer()
        self.assertEqual("not-invoked", second["release"])
        self.assertEqual(1, self.release_port.invocations)

    def test_restarted_finalizer_does_not_revoke_native_revoked_token_again(self):
        digest = hashlib.sha256(self.fixture.token.encode()).hexdigest()
        self.assertEqual("committed", self.port.append_pending(MINT_ID, digest))
        self.port.native_observation = "revoked"
        self.port.calls.clear()
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("revoked", recovered["revocation"])
        self.assertEqual("pending", recovered["disposition"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIn("native-observe", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)

    def test_unknown_native_readback_keeps_pending_without_new_revoke(self):
        self.port.native_observation = "unknown"
        first = self.run_finalizer()
        self.assertEqual("pending", first["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIsNone(self.port.revoked)
        self.port.native_observation = "revoked"
        self.port.calls.clear()
        second = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("revoked", second["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertEqual(1, self.release_port.invocations)

    def test_cancellation_after_native_revoke_recovers_without_duplicate_effect_or_launch(self):
        self.port.cancel_native_revoke = True
        with self.assertRaises(KeyboardInterrupt):
            self.run_finalizer()
        self.assertIsNotNone(self.port.pending)
        self.assertIsNone(self.port.revoked)
        self.assertEqual(1, self.port.calls.count("native-revoke"))
        self.assertEqual(1, self.release_port.invocations)
        self.port.cancel_native_revoke = False
        self.port.calls.clear()
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("revoked", recovered["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIn("read-revoked", self.port.calls)
        rerun = self.run_finalizer()
        self.assertEqual("not-invoked", rerun["release"])
        self.assertEqual(1, self.release_port.invocations)

    def test_crash_with_unknown_revoke_readback_never_retries_native_effect(self):
        self.port.observations_override = ["active", "unknown"]
        first = self.run_finalizer()
        self.assertEqual("pending", first["revocation"])
        self.assertEqual(1, self.port.calls.count("native-revoke"))
        self.assertEqual(1, self.release_port.invocations)
        self.port.observations_override = ["active"]
        self.port.calls.clear()
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", recovered["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        rerun = self.run_finalizer()
        self.assertEqual("not-invoked", rerun["release"])
        self.assertEqual(1, self.release_port.invocations)

    def test_lost_native_attempt_claim_stays_pending_without_provider_retry(self):
        self.port.native_attempt_response_lost = True
        first = self.run_finalizer()
        self.assertEqual("pending", first["revocation"])
        self.assertIsNotNone(self.port.native_attempt)
        self.assertNotIn("native-revoke", self.port.calls)
        self.port.native_attempt_response_lost = False
        self.port.calls.clear()
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", recovered["revocation"])
        rerun = self.run_finalizer()
        self.assertEqual("not-invoked", rerun["release"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertEqual(1, self.release_port.invocations)

    def test_stale_committed_native_attempt_response_cannot_revoke(self):
        self.port.stale_native_attempt_commit = True
        first = self.run_finalizer()
        self.assertEqual("pending", first["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertIsNotNone(self.port.native_attempt)

    def test_missing_shared_attempt_port_blocks_candidate_handoff(self):
        self.port.claim_native_attempt_once = None
        result = self.run_finalizer()
        self.assertEqual("not-invoked", result["release"])
        self.assertEqual("pending", result["disposition"])
        self.assertEqual(0, self.release_port.invocations)

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

    def test_crash_after_pending_defers_native_mutation_to_one_use_recovery(self):
        digest = hashlib.sha256(self.fixture.token.encode()).hexdigest()
        self.assertEqual("committed", self.port.append_pending(MINT_ID, digest))
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", recovered["revocation"])
        self.assertEqual("pending", recovered["disposition"])
        self.assertNotIn("native-revoke", self.port.calls)
        self.assertEqual(0, self.release_port.invocations)
        later = self.run_finalizer()
        self.assertEqual("not-invoked", later["release"])
        self.assertNotIn("native-revoke", self.port.calls)

    def test_crash_after_mint_before_pending_attempts_revoke_but_stays_pending(self):
        result = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", result["revocation"])
        self.assertEqual("pending", result["disposition"])
        self.assertIn("native-revoke", self.port.calls)
        self.assertNotIn("append-revoked", self.port.calls)
        self.assertEqual(0, self.release_port.invocations)

    def test_emergency_recovery_skips_native_revoke_if_already_revoked(self):
        self.port.native_observation = "revoked"
        recovered = finalizer.recover_pending(self.port, MINT_ID)
        self.assertEqual("pending", recovered["revocation"])
        self.assertNotIn("native-revoke", self.port.calls)
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
        self.assertEqual(1, self.release_port.revoke_calls)
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
                self.port = FakeFinalizerPort(self.fixture.token, self.fixture.context)
                self.port.descriptor = {**self.port.descriptor, **change}
                result = self.run_finalizer()
                self.assertEqual("not-invoked", result["release"])
                self.assertEqual("pending", result["revocation"])
                self.assertIn("native-revoke", self.port.calls)
                self.assertEqual(0, self.release_port.invocations)
        self.port = FakeFinalizerPort(self.fixture.token, self.fixture.context)
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

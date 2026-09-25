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


authority = load("host_claim_authority", ROOT / "scripts/gs2-09-7-host-claim-authority.py")
release = load("host_token_release_for_claim", ROOT / "scripts/gs2-09-7-host-token-release.py")
fixture_module = load("host_binding_fixture_for_claim", ROOT / "tests/gs2-09-7-host-run-binding/test_host_run_binding.py")

ORIGIN = "https://protected.example.invalid"
RESOURCE = "host-claim-ledger-test"
ENDPOINT = ORIGIN + "/claims"
TOKEN = "fake-app-token-never-valid-outside-this-test"


class FakeStore:
    def __init__(self):
        self.descriptor = {
            "schema": authority.STORE_SCHEMA,
            "origin": ORIGIN,
            "resourceId": RESOURCE,
            "endpoint": ENDPOINT,
            "durable": True,
            "atomicCas": True,
            "nativeReadback": True,
            "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
        }
        self.claims = {}
        self.calls = []
        self.lose_claim_response = False
        self.claim_result = None
        self.intent_result = "committed"
        self.receipt_result = "committed"

    def describe(self):
        return self.descriptor

    def cas_claim_once(self, binding_id, token_sha256):
        self.calls.append("cas")
        if self.claim_result is not None:
            return self.claim_result
        if binding_id in self.claims:
            return "duplicate"
        self.claims[binding_id] = token_sha256
        if self.lose_claim_response:
            raise OSError("response lost after commit")
        return "committed"

    def read_claim(self, binding_id):
        self.calls.append("read")
        return self.claims.get(binding_id, "absent")

    def append_revoke_intent(self, binding_id, token_sha256):
        self.calls.append("intent")
        return self.intent_result

    def append_revoked_receipt(self, binding_id, token_sha256):
        self.calls.append("receipt")
        return self.receipt_result


class FakeRevoker:
    def __init__(self):
        self.descriptor = {
            "schema": authority.REVOKER_SCHEMA,
            "apiOrigin": "https://api.github.com",
            "credentialScope": "protected-host-only",
            "candidateCanWrite": False,
            "nativeObservation": True,
        }
        self.calls = []
        self.observation = "revoked"
        self.revoke_raises = False

    def describe(self):
        return self.descriptor

    def revoke(self, token):
        self.calls.append("revoke")
        if self.revoke_raises:
            raise OSError("native response lost")
        return "accepted"

    def observe(self, token):
        self.calls.append("observe")
        return self.observation


class ReleasePort:
    def __init__(self, claim):
        self.claim = claim
        self.invocations = 0

    def claim_once(self, binding_id):
        return self.claim.claim_once(binding_id)

    def invoke_candidate_once(self, token, binding):
        self.invocations += 1
        return "complete"

    def revoke(self, token):
        return self.claim.revoke(token)


class HostClaimAuthorityTests(unittest.TestCase):
    def setUp(self):
        self.binding_id = "a" * 64
        self.token_digest = hashlib.sha256(TOKEN.encode()).hexdigest()
        self.store = FakeStore()
        self.revoker = FakeRevoker()
        original_origin = authority.PINNED_STORE_ORIGIN
        original_resource = authority.PINNED_STORE_RESOURCE_ID
        original_endpoint = authority.PINNED_STORE_ENDPOINT
        authority.PINNED_STORE_ORIGIN = ORIGIN
        authority.PINNED_STORE_RESOURCE_ID = RESOURCE
        authority.PINNED_STORE_ENDPOINT = ENDPOINT
        self.addCleanup(setattr, authority, "PINNED_STORE_ORIGIN", original_origin)
        self.addCleanup(setattr, authority, "PINNED_STORE_RESOURCE_ID", original_resource)
        self.addCleanup(setattr, authority, "PINNED_STORE_ENDPOINT", original_endpoint)

    def claim(self):
        return authority.HostClaimAuthority(self.binding_id, self.token_digest,
                                            self.store, self.revoker)

    def test_unconfigured_store_refuses_before_any_claim_or_revocation(self):
        authority.PINNED_STORE_ORIGIN = ""
        with self.assertRaisesRegex(authority.Refused, "store-unconfigured"):
            self.claim().claim_once(self.binding_id)
        with self.assertRaisesRegex(authority.Refused, "store-unconfigured"):
            self.claim().revoke(TOKEN)
        self.assertEqual([], self.store.calls)
        self.assertEqual([], self.revoker.calls)

    def test_candidate_writable_or_non_durable_store_refuses_before_cas(self):
        changes = [
            {"endpoint": "file:///tmp/claims"},
            {"endpoint": "https://protected.example.invalid.evil/claims"},
            {"endpoint": "https://protected.example.invalid@evil.invalid/claims"},
            {"endpoint": "https://protected.example.invalid:bad/claims"},
            {"endpoint": ORIGIN + "/candidate-writable/claims"},
            {"endpoint": ORIGIN + "/claims/../workspace"},
            {"candidateCanWrite": True},
            {"credentialScope": "candidate-token"},
            {"durable": False},
            {"atomicCas": False},
            {"nativeReadback": False},
            {"resourceId": "candidate-owned"},
        ]
        for change in changes:
            with self.subTest(change=change):
                self.store.descriptor = {**FakeStore().descriptor, **change}
                with self.assertRaises(authority.Refused):
                    self.claim().claim_once(self.binding_id)
        self.assertEqual([], self.store.calls)

    def test_foreign_revoker_or_binding_refuses_before_cas(self):
        self.revoker.descriptor["credentialScope"] = "candidate-token"
        with self.assertRaisesRegex(authority.Refused, "revoker-authority"):
            self.claim().claim_once(self.binding_id)
        self.revoker.descriptor["credentialScope"] = "protected-host-only"
        with self.assertRaisesRegex(authority.Refused, "binding-identity"):
            self.claim().claim_once("b" * 64)
        self.assertEqual([], self.store.calls)

    def test_durable_claim_allows_one_handoff_even_after_new_host_instance(self):
        self.assertEqual("granted", self.claim().claim_once(self.binding_id))
        self.assertEqual("duplicate", self.claim().claim_once(self.binding_id))
        self.assertEqual(["cas", "read", "cas"], self.store.calls)

    def test_lost_cas_response_never_grants_even_if_readback_says_committed(self):
        self.store.lose_claim_response = True
        self.assertEqual("unknown", self.claim().claim_once(self.binding_id))
        self.assertEqual(self.token_digest, self.store.claims[self.binding_id])
        self.assertEqual(["cas", "read"], self.store.calls)
        self.assertEqual("duplicate", self.claim().claim_once(self.binding_id))

    def test_revoke_requires_durable_intent_native_observation_and_receipt(self):
        self.assertEqual("confirmed", self.claim().revoke(TOKEN))
        self.assertEqual(["intent", "receipt"], self.store.calls)
        self.assertEqual(["revoke", "observe"], self.revoker.calls)
        self.store.calls.clear()
        self.revoker.calls.clear()
        self.store.intent_result = "unknown"
        self.assertEqual("unknown", self.claim().revoke(TOKEN))
        self.assertEqual(["intent"], self.store.calls)
        self.assertEqual(["revoke", "observe"], self.revoker.calls)
        self.store.intent_result = "committed"
        self.revoker.observation = "unknown"
        self.assertEqual("unknown", self.claim().revoke(TOKEN))
        self.revoker.observation = "revoked"
        self.store.receipt_result = "unknown"
        self.assertEqual("unknown", self.claim().revoke(TOKEN))

    def test_lost_native_revoke_response_needs_native_observation(self):
        self.revoker.revoke_raises = True
        self.revoker.observation = "unknown"
        self.assertEqual("unknown", self.claim().revoke(TOKEN))
        self.revoker.observation = "revoked"
        self.assertEqual("confirmed", self.claim().revoke(TOKEN))
        self.assertNotIn(TOKEN, json.dumps(self.store.calls + self.revoker.calls))

    def test_foreign_token_refuses_without_revocation(self):
        with self.assertRaisesRegex(authority.Refused, "token-digest"):
            self.claim().revoke("another-fake-app-token-never-valid")
        self.assertEqual([], self.revoker.calls)


class ReleaseCompositionTests(unittest.TestCase):
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
        self.store = FakeStore()
        self.revoker = FakeRevoker()
        pins = (authority.PINNED_STORE_ORIGIN, authority.PINNED_STORE_RESOURCE_ID,
                authority.PINNED_STORE_ENDPOINT, release.host.PINNED_SPKI_SHA256)
        authority.PINNED_STORE_ORIGIN = ORIGIN
        authority.PINNED_STORE_RESOURCE_ID = RESOURCE
        authority.PINNED_STORE_ENDPOINT = ENDPOINT
        release.host.PINNED_SPKI_SHA256 = self.fixture.pin
        self.addCleanup(setattr, authority, "PINNED_STORE_ORIGIN", pins[0])
        self.addCleanup(setattr, authority, "PINNED_STORE_RESOURCE_ID", pins[1])
        self.addCleanup(setattr, authority, "PINNED_STORE_ENDPOINT", pins[2])
        self.addCleanup(setattr, release.host, "PINNED_SPKI_SHA256", pins[3])
        binding = release.verify_envelope(self.envelope, self.proof, self.fixture.token,
                                          self.public, self.fixture.pin,
                                          self.fixture.context, self.fixture.now)
        self.binding_id = hashlib.sha256(release.host.canonical_payload(binding)).hexdigest()
        self.claim = authority.HostClaimAuthority(
            self.binding_id, hashlib.sha256(self.fixture.token.encode()).hexdigest(),
            self.store, self.revoker)
        self.port = ReleasePort(self.claim)

    def run_release(self):
        return release.release_once(self.envelope, self.proof, self.fixture.token,
                                    self.public, self.fixture.context,
                                    self.fixture.now, self.port)

    def test_crash_after_claim_before_handoff_refuses_rerun(self):
        self.assertEqual("granted", self.claim.claim_once(self.binding_id))
        # A new host process sees the durable CAS, so it cannot hand off again.
        result = self.run_release()
        self.assertEqual("duplicate-refused", result["outcome"])
        self.assertEqual(0, result["invocationCount"])
        self.assertEqual(0, self.port.invocations)

    def test_lost_claim_response_refuses_handoff_and_rerun(self):
        self.store.lose_claim_response = True
        first = self.run_release()
        second = self.run_release()
        self.assertEqual("claim-unknown", first["outcome"])
        self.assertEqual("duplicate-refused", second["outcome"])
        self.assertEqual(0, self.port.invocations)
        self.assertNotIn(self.fixture.token, json.dumps([first, second]))


if __name__ == "__main__":
    unittest.main()

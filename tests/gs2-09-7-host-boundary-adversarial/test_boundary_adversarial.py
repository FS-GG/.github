import hashlib
import importlib.util
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


claim_source = load("adversarial_claim", ROOT / "scripts/gs2-09-7-host-claim-authority.py")
release = load("adversarial_release", ROOT / "scripts/gs2-09-7-host-token-release.py")
fixture_source = load("adversarial_binding_fixture", ROOT / "tests/gs2-09-7-host-run-binding/test_host_run_binding.py")
census = load("adversarial_census", ROOT / "scripts/gs2-09-7-host-pending-census.py")
census_fixture = load("adversarial_census_fixture", ROOT / "tests/gs2-09-7-host-pending-census/test_host_pending_census.py")

ORIGIN = "https://protected.example.invalid"
RESOURCE = "host-claim-ledger-test"
ENDPOINT = ORIGIN + "/claims"


class ClaimStore:
    def __init__(self):
        self.records = {}
        self.false_commit = False
        self.lost_response = False
        self.calls = []

    def describe(self):
        return {"schema": claim_source.STORE_SCHEMA,
                "origin": ORIGIN, "resourceId": RESOURCE,
                "endpoint": ENDPOINT, "durable": True,
                "atomicCas": True, "atomicAdmissionClaim": True,
                "admissionResourceId": fixture_source.ADMISSION_RESOURCE,
                "nativeReadback": True,
                "credentialScope": "protected-host-only",
                "candidateCanWrite": False}

    def cas_claim_if_admitted(self, decision_id, binding_id, token_sha256):
        self.calls.append("cas")
        if self.false_commit:
            return "committed"
        if decision_id in self.records:
            return "duplicate"
        self.records[decision_id] = {
            "schema": claim_source.CLAIM_SCHEMA,
            "decisionId": decision_id, "bindingId": binding_id,
            "tokenSha256": token_sha256,
            "admissionResourceId": fixture_source.ADMISSION_RESOURCE,
            "admissionStateAtClaim": "admitted"}
        if self.lost_response:
            raise OSError("lost after durable commit")
        return "committed"

    def read_claim(self, decision_id):
        self.calls.append("read")
        return self.records.get(decision_id, "absent")

    def append_revoke_intent(self, binding_id, token_sha256):
        return "committed"

    def append_revoked_receipt(self, binding_id, token_sha256):
        return "committed"


class Revoker:
    def describe(self):
        return {"schema": claim_source.REVOKER_SCHEMA,
                "apiOrigin": "https://api.github.com",
                "credentialScope": "protected-host-only",
                "candidateCanWrite": False,
                "nativeObservation": True}

    def revoke(self, token):
        return "accepted"

    def observe(self, token):
        return "revoked"


class ReleasePort:
    def __init__(self, authority):
        self.authority = authority
        self.invocations = 0

    def claim_once(self, decision_id, binding_id, token_sha256):
        return self.authority.claim_once(decision_id, binding_id, token_sha256)

    def invoke_candidate_once(self, token, binding):
        self.invocations += 1
        return "complete"

    def revoke(self, token):
        return self.authority.revoke(token)


class ClaimBoundaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        fixture_source.HostRunBindingTests.setUpClass()

    @classmethod
    def tearDownClass(cls):
        fixture_source.HostRunBindingTests.tearDownClass()

    def setUp(self):
        fixture = fixture_source.HostRunBindingTests(
            "test_exact_host_envelope_matches_candidate_canonical_signature_contract")
        fixture.setUp()
        self.fixture = fixture
        self.envelope = fixture.build()
        self.proof = fixture.raw(fixture.proof)
        self.public = fixture.public_path.read_bytes()
        self.store = ClaimStore()
        pins = (claim_source.PINNED_STORE_ORIGIN,
                claim_source.PINNED_STORE_RESOURCE_ID,
                claim_source.PINNED_STORE_ENDPOINT,
                release.host.PINNED_SPKI_SHA256,
                release.host.PINNED_WORKFLOW_SHA)
        claim_source.PINNED_STORE_ORIGIN = ORIGIN
        claim_source.PINNED_STORE_RESOURCE_ID = RESOURCE
        claim_source.PINNED_STORE_ENDPOINT = ENDPOINT
        release.host.PINNED_SPKI_SHA256 = fixture.pin
        release.host.PINNED_WORKFLOW_SHA = fixture.context["workflowSha"]
        fixture_source.configure_admission(
            self, release.host, fixture.context, fixture.pin, fixture.now)
        for name, value in zip(("PINNED_STORE_ORIGIN", "PINNED_STORE_RESOURCE_ID",
                                "PINNED_STORE_ENDPOINT"), pins[:3]):
            self.addCleanup(setattr, claim_source, name, value)
        self.addCleanup(setattr, release.host, "PINNED_SPKI_SHA256", pins[3])
        self.addCleanup(setattr, release.host, "PINNED_WORKFLOW_SHA", pins[4])
        binding = release.verify_envelope(self.envelope, self.proof, fixture.token,
                                          self.public, fixture.pin,
                                          fixture.context, fixture.now)
        binding_id = hashlib.sha256(release.host.canonical_payload(binding)).hexdigest()
        token_sha256 = hashlib.sha256(fixture.token.encode()).hexdigest()
        decision_id = release.host.ADMISSION_PORT.record["decisionId"]
        self.authority = claim_source.HostClaimAuthority(
                                                         fixture_source.ADMISSION_RESOURCE,
                                                         decision_id,
                                                         binding_id, token_sha256,
                                                         self.store, Revoker())
        self.port = ReleasePort(self.authority)

    def run_release(self, context=None):
        return release.release_once(self.envelope, self.proof,
                                    self.fixture.token, self.public,
                                    self.fixture.context if context is None else context,
                                    self.fixture.now, self.port)

    def test_false_committed_claim_cannot_handoff_without_exact_readback(self):
        self.store.false_commit = True
        result = self.run_release()
        self.assertEqual("claim-unknown", result["outcome"])
        self.assertEqual(0, self.port.invocations)
        self.assertEqual(["cas", "read"], self.store.calls)

    def test_lost_claim_response_does_not_gain_authority_from_readback(self):
        self.store.lost_response = True
        result = self.run_release()
        self.assertEqual("claim-unknown", result["outcome"])
        self.assertEqual(0, self.port.invocations)
        self.assertEqual(["cas", "read"], self.store.calls)

    def test_duplicate_mint_binding_never_hands_off_again(self):
        first = self.run_release()
        second = self.run_release()
        self.assertEqual("candidate-complete", first["outcome"])
        self.assertEqual("duplicate-refused", second["outcome"])
        self.assertEqual(1, self.port.invocations)

    def test_stale_run_candidate_or_nonce_refuses_before_claim(self):
        context = self.fixture.context
        new_run = context["runId"] + 1
        new_candidate = "d" * 40
        changes = (
            {"runId": new_run,
             "runNonce": f'{new_run}-{context["runAttempt"]}-{context["candidateSha"]}'},
            {"candidateSha": new_candidate,
             "runNonce": f'{context["runId"]}-{context["runAttempt"]}-{new_candidate}'},
            {"runNonce": "stale-nonce"},
        )
        for change in changes:
            with self.subTest(change=change):
                with self.assertRaises(release.Refused):
                    self.run_release({**self.fixture.context, **change})
        self.assertEqual([], self.store.calls)
        self.assertEqual(0, self.port.invocations)


class CensusBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.port = census_fixture.FakeCensusPort()
        # Deliberately leave the recovery worker endpoint and the finalizer
        # revoker unconfigured; IDs alone must not advertise ready subjects.
        pins = {
            (census, "PINNED_QUEUE_ORIGIN"): census_fixture.ORIGIN,
            (census, "PINNED_QUEUE_RESOURCE_ID"): census_fixture.QUEUE_ID,
            (census, "PINNED_QUEUE_ENDPOINT"): census_fixture.QUEUE_ENDPOINT,
            (census, "PINNED_JOURNAL_ORIGIN"): census_fixture.ORIGIN,
            (census, "PINNED_JOURNAL_RESOURCE_ID"): census_fixture.JOURNAL_ID,
            (census, "PINNED_JOURNAL_ENDPOINT"): census_fixture.JOURNAL_ENDPOINT,
            (census.worker, "PINNED_RECOVERY_RESOURCE_ID"): census_fixture.RECOVERY_ID,
            (census.worker, "PINNED_RECOVERY_WORKER_ID"): census_fixture.WORKER_ID,
            (census.worker.finalizer, "PINNED_FINALIZER_RESOURCE_ID"):
                census_fixture.FINALIZER_ID,
            (census.worker.finalizer, "PINNED_TOKEN_VAULT_ID"):
                census_fixture.VAULT_ID,
        }
        for (module, name), value in pins.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def fully_pin_recovery(self):
        additional = {
            (census.worker, "PINNED_RECOVERY_ORIGIN"): census_fixture.ORIGIN,
            (census.worker, "PINNED_RECOVERY_ENDPOINT"):
                census_fixture.ORIGIN + "/recovery",
            (census.worker.finalizer, "PINNED_FINALIZER_ORIGIN"):
                census_fixture.ORIGIN,
            (census.worker.finalizer, "PINNED_FINALIZER_ENDPOINT"):
                census_fixture.ORIGIN + "/pending",
            (census.worker.finalizer, "PINNED_REVOKER_ID"):
                "protected-native-revoker-test",
        }
        for (module, name), value in additional.items():
            original = getattr(module, name)
            setattr(module, name, value)
            self.addCleanup(setattr, module, name, original)

    def test_partial_protected_identity_cannot_emit_recovery_subjects(self):
        with self.assertRaisesRegex(census.Refused, "census-unconfigured"):
            census.census_pending(self.port)
        self.assertEqual([], self.port.calls)

    def test_omitted_pending_token_cannot_emit_partial_subject_list(self):
        self.fully_pin_recovery()
        self.port.pages["0"]["nextCursor"] = None
        with self.assertRaisesRegex(census.Refused, "census-omission"):
            census.census_pending(self.port)

    def test_wrong_protected_journal_or_stale_high_water_refuses(self):
        self.fully_pin_recovery()
        self.port.journal_descriptor["vaultId"] = "foreign-vault"
        with self.assertRaisesRegex(census.Refused, "journal-authority"):
            census.census_pending(self.port)
        self.assertNotIn("seal-snapshot", self.port.calls)
        self.port = census_fixture.FakeCensusPort()
        self.port.high_water = 4
        with self.assertRaisesRegex(census.Refused, "high-water-drift"):
            census.census_pending(self.port)


if __name__ == "__main__":
    unittest.main()

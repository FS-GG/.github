import base64
import datetime as dt
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


release = load("host_token_release", ROOT / "scripts/gs2-09-7-host-token-release.py")
host_fixture = load("host_binding_fixture", ROOT / "tests/gs2-09-7-host-run-binding/test_host_run_binding.py")


class FakePort:
    def __init__(self):
        self.claimed = set()
        self.calls = []
        self.claim_result = None
        self.invoke_result = "complete"
        self.revoke_result = "confirmed"

    def claim_once(self, binding_id):
        self.calls.append("claim")
        if self.claim_result is not None:
            return self.claim_result
        if binding_id in self.claimed:
            return "duplicate"
        self.claimed.add(binding_id)
        return "granted"

    def invoke_candidate_once(self, token, binding):
        self.calls.append("invoke")
        return self.invoke_result

    def revoke(self, token):
        self.calls.append("revoke")
        return self.revoke_result


class HostTokenReleaseTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        host_fixture.HostRunBindingTests.setUpClass()

    @classmethod
    def tearDownClass(cls):
        host_fixture.HostRunBindingTests.tearDownClass()

    def setUp(self):
        self.fixture = host_fixture.HostRunBindingTests(
            "test_exact_host_envelope_matches_candidate_canonical_signature_contract")
        self.fixture.setUp()
        self.envelope = self.fixture.build()
        self.proof = self.fixture.raw(self.fixture.proof)
        self.public = self.fixture.public_path.read_bytes()
        self.port = FakePort()
        original_pin = release.host.PINNED_SPKI_SHA256
        original_workflow_pin = release.host.PINNED_WORKFLOW_SHA
        release.host.PINNED_SPKI_SHA256 = self.fixture.pin
        release.host.PINNED_WORKFLOW_SHA = self.fixture.context["workflowSha"]
        self.addCleanup(setattr, release.host, "PINNED_SPKI_SHA256", original_pin)
        self.addCleanup(setattr, release.host, "PINNED_WORKFLOW_SHA", original_workflow_pin)
        self.admission_port = host_fixture.configure_admission(
            self, release.host, self.fixture.context, self.fixture.pin,
            self.fixture.now)

    def run_release(self, envelope=None, proof=None, context=None, port=None):
        return release.release_once(
            self.envelope if envelope is None else envelope,
            self.proof if proof is None else proof,
            self.fixture.token, self.public,
            self.fixture.context if context is None else context,
            self.fixture.now, self.port if port is None else port)

    def signed_for_proof(self, proof):
        proof_raw = self.fixture.raw(proof)
        envelope = json.loads(self.envelope)
        envelope["binding"]["proofSha256"] = hashlib.sha256(proof_raw).hexdigest()
        signature = host_fixture.host.sign(
            self.fixture.key,
            host_fixture.host.canonical_payload(envelope["binding"]))
        envelope["signatureBase64"] = base64.b64encode(signature).decode()
        return self.fixture.raw(envelope), proof_raw

    def test_exact_signed_run_claims_then_invokes_once_then_revokes(self):
        first = self.run_release()
        self.assertEqual("complete", first["disposition"])
        self.assertEqual("candidate-complete", first["outcome"])
        self.assertEqual(1, first["invocationCount"])
        self.assertEqual(["claim", "invoke", "revoke"], self.port.calls)
        self.assertNotIn(self.fixture.token, json.dumps(first))

        second = self.run_release()
        self.assertEqual("pending", second["disposition"])
        self.assertEqual("duplicate-refused", second["outcome"])
        self.assertEqual(0, second["invocationCount"])
        self.assertEqual(1, self.port.calls.count("invoke"))

    def test_no_port_or_pin_refuses_before_claim_and_handoff(self):
        with self.assertRaisesRegex(release.Refused, "release-port-unconfigured"):
            self.run_release(port=object())
        release.host.PINNED_SPKI_SHA256 = ""
        with self.assertRaisesRegex(release.Refused, "trust-anchor-unconfigured"):
            self.run_release()
        self.assertEqual([], self.port.calls)

    def test_missing_independent_workflow_admission_refuses_before_port(self):
        release.host.PINNED_WORKFLOW_SHA = ""
        with self.assertRaisesRegex(release.Refused, "workflow-revision-unconfigured"):
            self.run_release()
        self.assertEqual([], self.port.calls)

    def test_self_asserted_workflow_pin_without_protected_admission_refuses(self):
        release.host.PINNED_WORKFLOW_SHA = self.fixture.context["workflowSha"]
        release.host.ADMISSION_PORT = None
        with self.assertRaisesRegex(release.Refused, "admission-unconfigured"):
            self.run_release()
        self.assertEqual([], self.port.calls)

    def test_revoked_native_admission_refuses_before_claim_or_handoff(self):
        self.admission_port.record = {
            **self.admission_port.record, "state": "revoked"}
        with self.assertRaisesRegex(release.Refused, "admission-binding"):
            self.run_release()
        self.assertEqual([], self.port.calls)

    def test_admission_that_aged_after_signing_refuses_release(self):
        later = self.fixture.now + dt.timedelta(minutes=30)
        with self.assertRaisesRegex(release.Refused, "admission-expiry"):
            release.release_once(self.envelope, self.proof, self.fixture.token,
                                 self.public, self.fixture.context, later, self.port)
        self.assertEqual([], self.port.calls)

    def test_wrong_run_candidate_nonce_or_target_refuses_before_port(self):
        changes = [
            {"runId": 99999, "runNonce": f'99999-2-{"b" * 40}'},
            {"runAttempt": 3, "runNonce": f'12345-3-{"b" * 40}'},
            {"candidateSha": "e" * 40, "runNonce": f'12345-2-{"e" * 40}'},
            {"runNonce": "forged"},
            {"workflowSha": "f" * 40, "protectedSha": "f" * 40},
        ]
        for change in changes:
            with self.subTest(change=change):
                with self.assertRaises(release.Refused):
                    self.run_release(context={**self.fixture.context, **change})
        altered = json.loads(self.envelope)
        altered["binding"]["projectNodeId"] = "PVT_foreign"
        with self.assertRaisesRegex(release.Refused, "run-or-target"):
            self.run_release(envelope=self.fixture.raw(altered))
        self.assertEqual([], self.port.calls)

    def test_valid_signature_on_unpinned_workflow_revision_refuses(self):
        alternate = json.loads(self.envelope)
        alternate["binding"]["workflowSha"] = "f" * 40
        signature = host_fixture.host.sign(
            self.fixture.key,
            host_fixture.host.canonical_payload(alternate["binding"]))
        alternate["signatureBase64"] = base64.b64encode(signature).decode()
        changed = {**self.fixture.context, "workflowSha": "f" * 40,
                   "protectedSha": "f" * 40}
        with self.assertRaises(release.Refused):
            self.run_release(envelope=self.fixture.raw(alternate), context=changed)
        self.assertEqual([], self.port.calls)

    def test_unsigned_or_tampered_claim_refuses_before_port(self):
        unsigned = json.loads(self.envelope)
        unsigned.pop("signatureBase64")
        with self.assertRaisesRegex(release.Refused, "envelope"):
            self.run_release(envelope=self.fixture.raw(unsigned))
        tampered = json.loads(self.envelope)
        tampered["signatureBase64"] = "Zm9yZ2Vk"
        with self.assertRaisesRegex(release.Refused, "signature"):
            self.run_release(envelope=self.fixture.raw(tampered))
        with self.assertRaisesRegex(release.Refused, "run-or-target"):
            self.run_release(proof=self.proof + b" ")
        self.assertEqual([], self.port.calls)

    def test_signed_foreign_mint_proof_still_refuses_before_port(self):
        foreign = {**self.fixture.proof,
                   "repository": {**self.fixture.proof["repository"], "id": 1}}
        envelope, proof = self.signed_for_proof(foreign)
        with self.assertRaisesRegex(release.Refused, "mint-proof"):
            self.run_release(envelope=envelope, proof=proof)
        broad = {**self.fixture.proof,
                 "permissions": {**self.fixture.proof["permissions"], "members": "write"}}
        envelope, proof = self.signed_for_proof(broad)
        with self.assertRaisesRegex(release.Refused, "mint-grants"):
            self.run_release(envelope=envelope, proof=proof)
        self.assertEqual([], self.port.calls)

    def test_unknown_claim_never_invokes_and_requires_revocation(self):
        self.port.claim_result = "unknown"
        result = self.run_release()
        self.assertEqual("pending", result["disposition"])
        self.assertEqual("claim-unknown", result["outcome"])
        self.assertEqual(["claim", "revoke"], self.port.calls)

    def test_claim_exception_is_unknown_and_never_hands_off_token(self):
        def unavailable(_binding_id):
            raise RuntimeError("durable claim unavailable")
        self.port.claim_once = unavailable
        result = self.run_release()
        self.assertEqual("claim-unknown", result["outcome"])
        self.assertEqual(0, result["invocationCount"])
        self.assertEqual(["revoke"], self.port.calls)

    def test_unknown_candidate_outcome_is_not_retried(self):
        self.port.invoke_result = "unknown"
        result = self.run_release()
        self.assertEqual("pending", result["disposition"])
        self.assertEqual("candidate-unknown", result["outcome"])
        self.assertEqual(["claim", "invoke", "revoke"], self.port.calls)
        self.run_release()
        self.assertEqual(1, self.port.calls.count("invoke"))

    def test_unconfirmed_revocation_keeps_result_pending(self):
        self.port.revoke_result = "unknown"
        result = self.run_release()
        self.assertEqual("pending", result["disposition"])
        self.assertEqual("unknown", result["revocation"])
        self.assertEqual(["claim", "invoke", "revoke"], self.port.calls)

    def test_revocation_exception_keeps_result_pending_without_token_output(self):
        def unavailable(_token):
            raise RuntimeError("native revocation unavailable")
        self.port.revoke = unavailable
        result = self.run_release()
        self.assertEqual("pending", result["disposition"])
        self.assertEqual("unknown", result["revocation"])
        self.assertNotIn(self.fixture.token, json.dumps(result))


if __name__ == "__main__":
    unittest.main()

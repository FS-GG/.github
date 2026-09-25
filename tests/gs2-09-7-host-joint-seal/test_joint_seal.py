import base64
import copy
import datetime as dt
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[2]


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


joint = load("host_joint_seal", ROOT / "scripts/gs2-09-7-host-joint-seal.py")
signing = load("joint_signing_fixture",
               ROOT / "tests/gs2-09-7-host-joint-seal/signing_fixture.py")


class FakePort:
    pass


class JointSealTests(unittest.TestCase):
    def setUp(self):
        self.now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
        self.seal = {
            "schema": "fsgg.github-substrate-v2.sandbox-host-pending-seal/1",
            "sealId": "f" * 64, "highWater": 3, "pendingCount": 1,
            "pendingSha256": "1" * 64, "mintCount": 3,
            "mintSha256": "2" * 64, "queueResourceId": "queue-test",
            "journalResourceId": "journal-test",
            "finalizerResourceId": "finalizer-test",
            "vaultId": "vault-test", "recoveryResourceId": "recovery-test",
            "workerId": "worker-test", "complete": True,
            "snapshotIsolation": True,
        }
        self.port = FakePort()
        signing.pin(self, joint)
        signing.attach(joint, self.port, lambda: self.seal)

    def verify(self):
        return joint.verify_joint_seal(self.port, self.seal, self.now)

    def test_exact_signed_current_seal_is_sanitized(self):
        verdict = self.verify()
        self.assertEqual(self.seal["sealId"], verdict["sealId"])
        self.assertEqual(1, verdict["generation"])
        self.assertNotIn("signatureBase64", verdict)

    def test_missing_pin_envelope_or_key_refuses(self):
        joint.PINNED_JOINT_SEAL_SPKI_SHA256 = ""
        with self.assertRaisesRegex(joint.Refused, "joint-seal-unconfigured"):
            self.verify()
        joint.PINNED_JOINT_SEAL_SPKI_SHA256 = signing.SIGNER.spki
        self.port.read_joint_seal_envelope = lambda _seal_id: None
        with self.assertRaisesRegex(joint.Refused, "joint-seal-envelope"):
            self.verify()
        signing.attach(joint, self.port, lambda: self.seal)
        self.port.read_joint_seal_public_key = lambda: None
        with self.assertRaisesRegex(joint.Refused, "joint-seal-signature"):
            self.verify()

    def test_foreign_key_and_signature_refuse(self):
        other = signing.FakeSigner()
        self.addCleanup(other.directory.cleanup)
        self.port.joint_key_override = other.public
        with self.assertRaisesRegex(joint.Refused, "joint-seal-trust-anchor"):
            self.verify()
        self.port.joint_key_override = None
        envelope = self.port.read_joint_seal_envelope(self.seal["sealId"])
        envelope["signatureBase64"] = base64.b64encode(b"foreign").decode("ascii")
        self.port.joint_envelope_override = envelope
        with self.assertRaisesRegex(joint.Refused, "joint-seal-signature"):
            self.verify()

    def test_expired_signed_record_refuses(self):
        envelope = self.port.read_joint_seal_envelope(self.seal["sealId"])
        record = envelope["record"]
        record["issuedAt"] = (self.now - dt.timedelta(minutes=20)).isoformat().replace(
            "+00:00", "Z")
        record["expiresAt"] = (self.now - dt.timedelta(minutes=10)).isoformat().replace(
            "+00:00", "Z")
        envelope["signatureBase64"] = base64.b64encode(
            signing.SIGNER.sign(joint.canonical(record))).decode("ascii")
        self.port.joint_envelope_override = envelope
        with self.assertRaisesRegex(joint.Refused, "joint-seal-stale"):
            self.verify()

    def test_old_signed_generation_after_crash_refuses_new_head(self):
        old = self.port.read_joint_seal_envelope(self.seal["sealId"])
        self.assertEqual(1, self.verify()["generation"])
        self.port.joint_generation = 2
        self.port.joint_envelope_override = old
        with self.assertRaisesRegex(joint.Refused, "joint-seal-binding"):
            self.verify()

    def test_replayed_old_head_and_envelope_together_refuses(self):
        old_head = self.port.read_joint_seal_head("b" * 64)["record"]["head"]
        old_envelope = self.port.read_joint_seal_envelope(self.seal["sealId"])
        self.port.joint_generation = 2
        self.port.joint_head_override = old_head
        self.port.joint_replay_head = old_head
        self.port.joint_envelope_override = old_envelope
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-challenge"):
            self.verify()

    def test_same_attestation_replayed_for_second_nonce_refuses(self):
        original = self.port.read_joint_seal_head
        prior = None
        def replay(challenge):
            nonlocal prior
            if prior is None:
                prior = original(challenge)
            return copy.deepcopy(prior)
        self.port.read_joint_seal_head = replay
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-challenge"):
            self.verify()

    def test_stale_or_unsigned_head_attestation_refuses(self):
        original = self.port.read_joint_seal_head
        def stale(challenge):
            value = original(challenge)
            value["record"]["observedAt"] = (
                self.now - dt.timedelta(minutes=2)).isoformat().replace("+00:00", "Z")
            value["signatureBase64"] = base64.b64encode(
                signing.SIGNER.sign(joint.canonical_head(value["record"]))).decode("ascii")
            return value
        self.port.read_joint_seal_head = stale
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-stale"):
            self.verify()
        self.port.read_joint_seal_head = lambda challenge: None
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-attestation"):
            self.verify()
        self.port.read_joint_seal_head = lambda challenge: {
            "schema": joint.HEAD_SCHEMA, "generation": 1}
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-attestation"):
            self.verify()

    def test_foreign_head_signature_and_missing_linearizable_port_refuse(self):
        original = self.port.read_joint_seal_head
        def foreign(challenge):
            value = original(challenge)
            value["signatureBase64"] = base64.b64encode(b"foreign").decode("ascii")
            return value
        self.port.read_joint_seal_head = foreign
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head-signature"):
            self.verify()
        self.port.read_joint_seal_head = original
        descriptor = self.port.describe_joint_seal
        self.port.describe_joint_seal = lambda: {
            **descriptor(), "linearizableHead": False}
        with self.assertRaisesRegex(joint.Refused, "joint-seal-authority"):
            self.verify()

    def test_repeated_generated_challenge_refuses_before_readback(self):
        with patch.object(joint.secrets, "token_hex", return_value="a" * 64):
            with self.assertRaisesRegex(joint.Refused, "joint-seal-head-challenge"):
                self.verify()

    def test_head_drift_within_one_readback_refuses(self):
        original = self.port.read_joint_seal_head
        reads = 0
        def moving(challenge):
            nonlocal reads
            reads += 1
            result = original(challenge)
            if reads == 2:
                result["record"]["head"]["generation"] += 1
                result["signatureBase64"] = base64.b64encode(
                    signing.SIGNER.sign(
                        joint.canonical_head(result["record"]))).decode("ascii")
            return result
        self.port.read_joint_seal_head = moving
        with self.assertRaisesRegex(joint.Refused, "joint-seal-head"):
            self.verify()

    def test_candidate_writable_or_foreign_policy_refuses(self):
        original = self.port.describe_joint_seal
        self.port.describe_joint_seal = lambda: {
            **original(), "candidateCanWrite": True}
        with self.assertRaisesRegex(joint.Refused, "joint-seal-authority"):
            self.verify()
        self.port.describe_joint_seal = original
        envelope = self.port.read_joint_seal_envelope(self.seal["sealId"])
        envelope["record"]["policySha256"] = "8" * 64
        envelope["signatureBase64"] = base64.b64encode(
            signing.SIGNER.sign(joint.canonical(envelope["record"]))).decode("ascii")
        self.port.joint_envelope_override = copy.deepcopy(envelope)
        with self.assertRaisesRegex(joint.Refused, "joint-seal-binding"):
            self.verify()

    def test_signed_boolean_alias_cannot_substitute_integer_generation(self):
        envelope = self.port.read_joint_seal_envelope(self.seal["sealId"])
        envelope["record"]["head"]["generation"] = True
        envelope["signatureBase64"] = base64.b64encode(
            signing.SIGNER.sign(joint.canonical(envelope["record"]))).decode("ascii")
        self.port.joint_envelope_override = envelope
        with self.assertRaisesRegex(joint.Refused, "joint-seal-binding"):
            self.verify()


if __name__ == "__main__":
    unittest.main()

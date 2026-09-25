"""Ephemeral RSA signer for source-only joint-seal tests. Never installed."""

import base64
import copy
import datetime as dt
import hashlib
from pathlib import Path
import subprocess
import tempfile


ORIGIN = "https://protected.example.invalid"
ENDPOINT = ORIGIN + "/joint-seal"
STORE_ID = "protected-joint-seal-store-test"
SIGNER_ID = "protected-joint-seal-signer-test"
POLICY_SHA256 = "7" * 64


class FakeSigner:
    def __init__(self):
        self.directory = tempfile.TemporaryDirectory(prefix="gs2-09-7-test-key-")
        self.private = Path(self.directory.name) / "private.pem"
        result = subprocess.run(
            ["openssl", "genpkey", "-algorithm", "RSA", "-out", str(self.private),
             "-pkeyopt", "rsa_keygen_bits:2048"],
            capture_output=True, check=False)
        if result.returncode != 0:
            raise RuntimeError("ephemeral test signer unavailable")
        result = subprocess.run(
            ["openssl", "pkey", "-in", str(self.private), "-pubout"],
            capture_output=True, check=True)
        self.public = result.stdout
        result = subprocess.run(
            ["openssl", "pkey", "-pubin", "-outform", "DER"],
            input=self.public, capture_output=True, check=True)
        self.spki = hashlib.sha256(result.stdout).hexdigest()

    def sign(self, payload):
        result = subprocess.run(
            ["openssl", "dgst", "-sha256", "-sign", str(self.private),
             "-sigopt", "rsa_padding_mode:pss",
             "-sigopt", "rsa_pss_saltlen:digest"],
            input=payload, capture_output=True, check=True)
        return result.stdout


SIGNER = FakeSigner()


def pin(testcase, joint):
    pins = {
        "PINNED_JOINT_SEAL_ORIGIN": ORIGIN,
        "PINNED_JOINT_SEAL_ENDPOINT": ENDPOINT,
        "PINNED_JOINT_SEAL_STORE_ID": STORE_ID,
        "PINNED_JOINT_SEAL_SIGNER_ID": SIGNER_ID,
        "PINNED_JOINT_SEAL_SPKI_SHA256": SIGNER.spki,
        "PINNED_JOINT_SEAL_POLICY_SHA256": POLICY_SHA256,
    }
    for name, value in pins.items():
        original = getattr(joint, name)
        setattr(joint, name, value)
        testcase.addCleanup(setattr, joint, name, original)


def attach(joint, port, seal_getter):
    port.joint_generation = 1
    port.joint_head_override = None
    port.joint_replay_head = None
    port.joint_envelope_override = None
    port.joint_key_override = None

    def head():
        seal = seal_getter()
        return {"schema": joint.HEAD_SCHEMA,
                "storeResourceId": STORE_ID,
                "generation": port.joint_generation,
                "sealId": seal["sealId"], "highWater": seal["highWater"],
                "pendingSha256": seal["pendingSha256"],
                "mintSha256": seal["mintSha256"]}

    def envelope(seal_id):
        if port.joint_envelope_override is not None:
            return copy.deepcopy(port.joint_envelope_override)
        now = dt.datetime.now(dt.timezone.utc).replace(microsecond=0)
        record = {
            "schema": joint.RECORD_SCHEMA, "seal": copy.deepcopy(seal_getter()),
            "head": head(), "storeResourceId": STORE_ID,
            "signerResourceId": SIGNER_ID, "policySha256": POLICY_SHA256,
            "issuedAt": (now - dt.timedelta(minutes=1)).isoformat().replace("+00:00", "Z"),
            "expiresAt": (now + dt.timedelta(minutes=10)).isoformat().replace("+00:00", "Z"),
        }
        return {"schema": joint.ENVELOPE_SCHEMA, "record": record,
                "signatureBase64": base64.b64encode(
                    SIGNER.sign(joint.canonical(record))).decode("ascii")}

    def attestation(challenge, observed_head=None):
        record = {
            "schema": joint.HEAD_RECORD_SCHEMA,
            "head": copy.deepcopy(head() if observed_head is None else observed_head),
            "challenge": challenge, "storeResourceId": STORE_ID,
            "signerResourceId": SIGNER_ID, "policySha256": POLICY_SHA256,
            "observedAt": dt.datetime.now(dt.timezone.utc).replace(
                microsecond=0).isoformat().replace("+00:00", "Z"),
        }
        return {"schema": joint.HEAD_ATTESTATION_SCHEMA, "record": record,
                "signatureBase64": base64.b64encode(
                    SIGNER.sign(joint.canonical_head(record))).decode("ascii")}

    def signed_head(challenge):
        if port.joint_replay_head is not None:
            return attestation("a" * 64, port.joint_replay_head)
        return attestation(challenge, port.joint_head_override)

    port.describe_joint_seal = lambda: {
        "schema": joint.AUTHORITY_SCHEMA, "origin": ORIGIN,
        "endpoint": ENDPOINT, "storeResourceId": STORE_ID,
        "signerResourceId": SIGNER_ID, "durable": True,
        "appendOnly": True, "nativeReadback": True,
        "linearizableHead": True, "challengeBoundReadback": True,
        "credentialScope": "protected-host-only",
        "candidateCanRead": False, "candidateCanWrite": False,
        "workflowCanWrite": False,
    }
    port.read_joint_seal_head = signed_head
    port.make_joint_head_attestation = attestation
    port.read_joint_seal_envelope = envelope
    port.read_joint_seal_public_key = lambda: (
        port.joint_key_override if port.joint_key_override is not None
        else SIGNER.public)
    return envelope, head

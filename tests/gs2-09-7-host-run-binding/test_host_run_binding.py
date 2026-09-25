import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/gs2-09-7-host-run-binding.py"
SPEC = importlib.util.spec_from_file_location("host_run_binding", SCRIPT)
host = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(host)

ADMISSION_ORIGIN = "https://protected.example.test"
ADMISSION_RESOURCE = "admission-store-v1"
ADMISSION_ENDPOINT = ADMISSION_ORIGIN + "/admission"
POLICY_SHA = "e" * 64


class FakeAdmissionPort:
    def __init__(self, module, context, signer_pin, now):
        self.module = module
        self.calls = []
        self.descriptor = {
            "schema": module.admission.SCHEMA,
            "origin": ADMISSION_ORIGIN,
            "resourceId": ADMISSION_RESOURCE,
            "endpoint": ADMISSION_ENDPOINT,
            "durable": True, "immutable": True, "nativeReadback": True,
            "credentialScope": "protected-host-only",
            "candidateCanRead": False, "candidateCanWrite": False,
            "workflowCanWrite": False,
        }
        self.record = {
            "schema": module.admission.RECORD_SCHEMA,
            "resourceId": ADMISSION_RESOURCE,
            "decisionId": "d" * 64,
            "state": "admitted",
            "workflowRepository": module.HOST_REPOSITORY,
            "workflowPath": module.WORKFLOW,
            "environment": module.ENVIRONMENT,
            "workflowSha": context["workflowSha"],
            "candidateSha": context["candidateSha"],
            "runId": context["runId"],
            "runAttempt": context["runAttempt"],
            "runNonce": context["runNonce"],
            "sandboxRepositoryId": module.SANDBOX_ID,
            "sandboxRepositoryNodeId": module.SANDBOX_NODE,
            "projectNodeId": module.PROJECT_NODE,
            "signerSpkiSha256": signer_pin,
            "releasePolicySha256": POLICY_SHA,
            "issuedAt": now.isoformat(timespec="seconds").replace("+00:00", "Z"),
            "expiresAt": (now + dt.timedelta(minutes=5)).isoformat(
                timespec="seconds").replace("+00:00", "Z"),
            "sealed": True,
        }

    def describe(self):
        self.calls.append("describe")
        return self.descriptor

    def read_admission(self, run_id, run_attempt):
        self.calls.append(("read", run_id, run_attempt))
        return self.record


def configure_admission(test_case, module, context, signer_pin, now):
    pins = {
        "PINNED_ADMISSION_ORIGIN": ADMISSION_ORIGIN,
        "PINNED_ADMISSION_RESOURCE_ID": ADMISSION_RESOURCE,
        "PINNED_ADMISSION_ENDPOINT": ADMISSION_ENDPOINT,
        "PINNED_RELEASE_POLICY_SHA256": POLICY_SHA,
    }
    for name, value in pins.items():
        old = getattr(module.admission, name)
        setattr(module.admission, name, value)
        test_case.addCleanup(setattr, module.admission, name, old)
    old_port = module.ADMISSION_PORT
    port = FakeAdmissionPort(module, context, signer_pin, now)
    module.ADMISSION_PORT = port
    test_case.addCleanup(setattr, module, "ADMISSION_PORT", old_port)
    return port


class HostRunBindingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.key_path = Path(cls.temporary.name) / "fake-key.pem"
        cls.other_path = Path(cls.temporary.name) / "other-key.pem"
        for key in (cls.key_path, cls.other_path):
            subprocess.run(["openssl", "genpkey", "-algorithm", "RSA", "-pkeyopt",
                            "rsa_keygen_bits:2048", "-out", str(key)],
                           capture_output=True, check=True)
        cls.key = cls.key_path.read_bytes()
        cls.other_key = cls.other_path.read_bytes()
        cls.public_path = Path(cls.temporary.name) / "fake-public.pem"
        cls.public_path.write_bytes(subprocess.run(
            ["openssl", "pkey", "-in", str(cls.key_path), "-pubout"],
            capture_output=True, check=True).stdout)
        cls.pin = host.signer_spki_sha256(cls.key)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def setUp(self):
        original_workflow_pin = host.PINNED_WORKFLOW_SHA
        host.PINNED_WORKFLOW_SHA = "a" * 40
        self.addCleanup(setattr, host, "PINNED_WORKFLOW_SHA", original_workflow_pin)
        self.now = dt.datetime(2026, 9, 25, 12, 0, tzinfo=dt.timezone.utc)
        self.token = "fake-sandbox-installation-token-123456789"
        self.context = {
            "workflowRepository": host.HOST_REPOSITORY,
            "workflowRef": "refs/heads/main",
            "workflowRefPath": f"{host.HOST_REPOSITORY}/{host.WORKFLOW}@refs/heads/main",
            "workflowSha": "a" * 40,
            "protectedSha": "a" * 40,
            "runId": 12345,
            "runAttempt": 2,
            "candidateSha": "b" * 40,
            "runNonce": f'12345-2-{"b" * 40}',
        }
        self.preflight = {
            "schema": "fsgg.github-substrate-v2.sandbox-preflight/1",
            "candidateSha": self.context["candidateSha"],
            "runNonce": self.context["runNonce"],
            "viewer": {**host.ACTOR, "id": "BOT_app"},
            "repository": {
                "node_id": host.SANDBOX_NODE, "full_name": host.SANDBOX_NAME,
                "private": True,
                "description": "fsgg-sandbox-gs2-04-9 disposable qualification target; never production",
            },
            "project": {"id": host.PROJECT_NODE, "title": host.PROJECT_TITLE,
                        "closed": False, "public": False},
        }
        self.proof = {
            "schema": "fsgg.github-substrate-v2.sandbox-mint-grants/1",
            "appId": host.APP_ID, "appSlug": host.APP_SLUG,
            "actor": host.ACTOR, "installationId": 123456,
            "repositorySelection": "selected",
            "repository": {"id": host.SANDBOX_ID, "nodeId": host.SANDBOX_NODE,
                           "fullName": host.SANDBOX_NAME},
            "permissions": {**host.REQUIRED_GRANTS, "metadata": "read"},
            "expiresAt": "2026-09-25T13:00:00Z",
            "tokenSha256": hashlib.sha256(self.token.encode()).hexdigest(),
            "mintResponseSha256": "c" * 64,
            "viewerResponseSha256": "d" * 64,
        }
        self.admission_port = configure_admission(
            self, host, self.context, self.pin, self.now)

    @staticmethod
    def raw(value):
        return (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()

    def build(self, context=None, preflight=None, proof=None, key=None, pin=None,
              token=None):
        return host.build(
            self.context if context is None else context,
            self.raw(self.preflight if preflight is None else preflight),
            self.raw(self.proof if proof is None else proof),
            self.token if token is None else token,
            self.key if key is None else key,
            self.pin if pin is None else pin, self.now)

    def test_exact_host_envelope_matches_candidate_canonical_signature_contract(self):
        raw = self.build()
        envelope = json.loads(raw)
        bound = envelope["binding"]
        self.assertEqual(host.SCHEMA, envelope["schema"])
        self.assertEqual(self.context["runNonce"], bound["runNonce"])
        self.assertEqual(hashlib.sha256(self.raw(self.proof)).hexdigest(),
                         bound["proofSha256"])
        self.assertNotIn(self.token.encode(), raw)
        signature = Path(self.temporary.name) / "signature.bin"
        signature.write_bytes(__import__("base64").b64decode(envelope["signatureBase64"]))
        result = subprocess.run(
            ["openssl", "dgst", "-sha256", "-verify", str(self.public_path),
             "-signature", str(signature), "-sigopt", "rsa_padding_mode:pss",
             "-sigopt", "rsa_pss_saltlen:digest"],
            input=host.canonical_payload(bound), capture_output=True, check=False)
        self.assertEqual(0, result.returncode)
        self.assertTrue(host.canonical_payload(bound).startswith(
            b"fsgg.github-substrate-v2.sandbox-run-binding/1\n{"))

    def test_missing_pin_or_foreign_signer_refuses(self):
        with self.assertRaisesRegex(host.Refused, "trust-anchor-unconfigured"):
            self.build(pin="")
        host.PINNED_WORKFLOW_SHA = ""
        with self.assertRaisesRegex(host.Refused, "workflow-revision-unconfigured"):
            self.build()
        host.PINNED_WORKFLOW_SHA = self.context["workflowSha"]
        with self.assertRaisesRegex(host.Refused, "signer-key-mismatch"):
            self.build(key=self.other_key)

    def test_run_candidate_nonce_and_protected_revision_mismatch_refuse(self):
        changes = [
            {"candidateSha": "e" * 40}, {"runNonce": "forged"},
            {"runId": 12346}, {"runAttempt": 3},
            {"workflowSha": "f" * 40}, {"protectedSha": "f" * 40},
            {"workflowRef": "refs/heads/feature"},
            {"workflowRefPath": "FS-GG/.github/.github/workflows/other.yml@refs/heads/main"},
        ]
        for change in changes:
            with self.subTest(change=change):
                with self.assertRaises(host.Refused):
                    self.build(context={**self.context, **change})

    def test_coherently_changed_workflow_revision_refuses(self):
        changed = {**self.context, "workflowSha": "f" * 40,
                   "protectedSha": "f" * 40}
        with self.assertRaises(host.Refused):
            self.build(context=changed)

    def test_self_asserted_workflow_pin_without_protected_admission_refuses(self):
        # The pin is copied from the same runner context. It has no authority.
        host.PINNED_WORKFLOW_SHA = self.context["workflowSha"]
        host.ADMISSION_PORT = None
        with self.assertRaisesRegex(host.Refused, "admission-unconfigured"):
            self.build()

    def test_protected_admission_exact_binding_and_state_refuse_drift(self):
        changes = [
            {"workflowSha": "f" * 40}, {"candidateSha": "f" * 40},
            {"runId": 12346}, {"runAttempt": 3},
            {"runNonce": "stale"}, {"sandboxRepositoryId": 1},
            {"projectNodeId": "PVT_foreign"},
            {"signerSpkiSha256": "0" * 64},
            {"releasePolicySha256": "0" * 64},
            {"state": "revoked"}, {"sealed": False},
        ]
        original = self.admission_port.record
        for change in changes:
            with self.subTest(change=change):
                self.admission_port.record = {**original, **change}
                with self.assertRaisesRegex(host.Refused, "admission-binding"):
                    self.build()
        self.admission_port.record = original

    def test_protected_admission_descriptor_or_readback_failure_refuses(self):
        self.admission_port.descriptor = {
            **self.admission_port.descriptor, "workflowCanWrite": True}
        with self.assertRaisesRegex(host.Refused, "admission-authority"):
            self.build()
        self.admission_port.descriptor["workflowCanWrite"] = False
        self.admission_port.descriptor["candidateCanRead"] = True
        with self.assertRaisesRegex(host.Refused, "admission-authority"):
            self.build()
        self.admission_port.descriptor["candidateCanRead"] = False
        self.admission_port.describe = lambda: (_ for _ in ()).throw(
            RuntimeError("unknown descriptor"))
        with self.assertRaisesRegex(host.Refused, "admission-descriptor-unknown"):
            self.build()
        self.admission_port.describe = lambda: self.admission_port.descriptor
        self.admission_port.read_admission = lambda *_: None
        with self.assertRaisesRegex(host.Refused, "admission-record"):
            self.build()

    def test_old_decision_cannot_sign_fresh_token_for_same_run(self):
        later = self.now + dt.timedelta(days=1)
        fresh_proof = {**self.proof,
                       "expiresAt": "2026-09-26T13:00:00Z"}
        with self.assertRaisesRegex(host.Refused, "admission-expiry"):
            host.build(self.context, self.raw(self.preflight),
                       self.raw(fresh_proof), self.token, self.key,
                       self.pin, later)

    def test_invalid_admission_time_window_refuses(self):
        original = self.admission_port.record
        changes = [
            {"issuedAt": "2026-09-25T12:01:00Z"},
            {"expiresAt": "2026-09-25T12:11:00Z"},
            {"expiresAt": "2026-09-25T12:00:00Z"},
            {"issuedAt": "2026-09-25T12:00:00+00:00"},
            {"issuedAt": 0},
        ]
        for change in changes:
            with self.subTest(change=change):
                self.admission_port.record = {**original, **change}
                with self.assertRaisesRegex(host.Refused, "admission-expiry"):
                    self.build()
        self.admission_port.record = original

    def test_actor_repository_and_project_native_readback_mismatch_refuse(self):
        for key, changed in (
            ("viewer", {**self.preflight["viewer"], "login": "other"}),
            ("repository", {**self.preflight["repository"], "node_id": "foreign"}),
            ("project", {**self.preflight["project"], "id": "foreign"}),
        ):
            with self.subTest(key=key):
                with self.assertRaises(host.Refused):
                    self.build(preflight={**self.preflight, key: changed})

    def test_unselected_foreign_expired_or_wrong_token_mint_refuses(self):
        changes = [
            {"repositorySelection": "all"},
            {"repository": {**self.proof["repository"], "id": 1}},
            {"actor": {**host.ACTOR, "login": "other"}},
            {"permissions": {**self.proof["permissions"], "members": "write"}},
            {"expiresAt": "2026-09-25T11:59:59Z"},
            {"tokenSha256": "0" * 64},
        ]
        for change in changes:
            with self.subTest(change=change):
                with self.assertRaises(host.Refused):
                    self.build(proof={**self.proof, **change})
        with self.assertRaisesRegex(host.Refused, "mint-proof"):
            self.build(token="other-fake-sandbox-installation-token-123456789")

    def test_duplicate_member_and_symlink_input_refuse(self):
        duplicate = self.raw(self.proof).replace(b'"schema":',
                                                 b'"schema":"extra","schema":', 1)
        with self.assertRaisesRegex(host.Refused, "duplicate-member"):
            host.build(self.context, self.raw(self.preflight), duplicate,
                       self.token, self.key, self.pin, self.now)
        with tempfile.TemporaryDirectory() as directory:
            real = Path(directory) / "real.json"
            link = Path(directory) / "link.json"
            real.write_bytes(self.raw(self.proof))
            link.symlink_to(real)
            with self.assertRaises(OSError):
                host.read_regular(link)

    def test_command_refuses_without_installed_pin_before_reading_credentials(self):
        result = subprocess.run([sys.executable, str(SCRIPT)],
                                env={"PATH": os.environ["PATH"]},
                                capture_output=True, text=True, check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("trust-anchor-unconfigured", result.stderr)
        self.assertNotIn(self.token, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()

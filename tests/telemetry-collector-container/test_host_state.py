#!/usr/bin/env python3
import importlib.util
import json
import os
import pathlib
import tempfile
import unittest
from types import SimpleNamespace


ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "deployment/telemetry-collector/qualify_host_state.py"
SPEC = importlib.util.spec_from_file_location("qualify_host_state", MODULE_PATH)
qualification = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(qualification)


class HostStateQualificationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="telemetry-host-state-test-")
        self.root = pathlib.Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def test_config_owns_exact_native_collector_grants_and_revocation(self):
        config = qualification.host_config(
            "https://localhost:7443", self.root / "server.pfx", self.root / "password",
            self.root / "host.lock", self.root / "store", self.root / "active.secret",
            self.root / "revoked.secret")
        self.assertEqual("fsgg.telemetry.host-config/2", config["Schema"])
        self.assertEqual([], config["BrowserPrincipals"])
        self.assertEqual(2, len(config["Credentials"]))
        active, revoked = config["Credentials"]
        self.assertEqual(("native-collector", "grant-controlled-active", 1, False),
                         (active["Role"], active["GrantId"], active["GrantGeneration"], active["Revoked"]))
        self.assertEqual(("native-collector", "grant-controlled-revoked", 1, True),
                         (revoked["Role"], revoked["GrantId"], revoked["GrantGeneration"], revoked["Revoked"]))
        invalid = qualification.host_config(
            "https://localhost:7443", self.root / "server.pfx", self.root / "password",
            self.root / "host.lock", self.root / "store", self.root / "active.secret",
            self.root / "revoked.secret", generation=0)
        self.assertTrue(all(row["GrantGeneration"] == 0 for row in invalid["Credentials"]))

    def test_controlled_envelope_exercises_receipts_without_native_claims(self):
        envelope = json.loads(qualification.controlled_envelope())
        self.assertEqual("fsgg.telemetry.envelope/1", envelope["schema"])
        self.assertEqual("controlled-active", envelope["producerId"])
        self.assertEqual("controlled-empty-1", envelope["batchId"])
        self.assertEqual(0, envelope["payload"]["eventCount"])
        self.assertEqual([], envelope["payload"]["events"])
        serialized = qualification.controlled_envelope().decode()
        for forbidden in ("runtime-native-inventory", "observedModel", "requestedModel",
                          "learn-experiment-assignment", "capture"):
            self.assertNotIn(forbidden, serialized)

    def test_receipt_requires_exact_public_history_shape(self):
        value = {
            "schema": "fsgg.telemetry.receipt/1", "workspaceId": "controlled-state",
            "producerId": "controlled-active", "streamId": "runtime",
            "batchId": "controlled-empty-1", "digest": "a" * 64,
            "status": "applied", "code": None,
        }
        payload = json.dumps(value, separators=(",", ":")).encode()
        self.assertEqual(value, qualification.receipt(payload, "controlled-empty-1", "applied", "a" * 64))
        changed = dict(value, digest="b" * 64, extra=True)
        with self.assertRaisesRegex(qualification.Refusal, "receipt shape"):
            qualification.receipt(json.dumps(changed).encode(), "controlled-empty-1", "applied", "a" * 64)
        with self.assertRaisesRegex(qualification.Refusal, "receipt state"):
            qualification.receipt(payload, "different-batch", "applied", "a" * 64)
        wrong_scope = dict(value, producerId="foreign")
        with self.assertRaisesRegex(qualification.Refusal, "receipt scope"):
            qualification.receipt(json.dumps(wrong_scope).encode(), "controlled-empty-1", "applied", "a" * 64)

    def test_private_write_is_exclusive_and_owner_only(self):
        target = self.root / "secret"
        qualification.write_private(target, b"value\n")
        self.assertEqual(0o600, target.stat().st_mode & 0o777)
        self.assertEqual(os.getuid(), target.stat().st_uid)
        with self.assertRaises(FileExistsError):
            qualification.write_private(target, b"replacement\n")

    def test_source_keeps_controlled_and_genuine_verdicts_separate(self):
        source = MODULE_PATH.read_text(encoding="utf-8")
        for exact in ('"nativeAccessQualified": False', '"modelSupportObserved": False',
                      '"captureApplied": False', '"activationAuthorized": False',
                      '"controlledPayloadFacts": 0'):
            self.assertIn(exact, source)
        self.assertIn("release_tools.verify_release", source)
        self.assertIn('["serve", "--config", str(config_path)]', source)
        self.assertIn('shutil.rmtree(state)', source)

    def test_qualification_refuses_relative_custody_paths_before_io(self):
        arguments = SimpleNamespace(
            repository=pathlib.Path("relative"), package=self.root / "package",
            manifest=self.root / "manifest", journal=self.root / "journal",
            dotnet=self.root / "dotnet", state_root=self.root / "state",
            evidence=self.root / "evidence", certificate=self.root / "certificate",
            certificate_password_file=self.root / "password", ca_certificate=self.root / "ca",
        )
        with self.assertRaisesRegex(qualification.Refusal, "paths must be absolute"):
            qualification.qualify(arguments)


if __name__ == "__main__":
    unittest.main(verbosity=2)

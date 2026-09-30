#!/usr/bin/env python3
import importlib.util
import json
import os
import pathlib
import tempfile
import unittest
from types import SimpleNamespace
from unittest import mock


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

    def arguments(self, state=None, evidence=None):
        repository = self.root / "repository"
        repository.mkdir(exist_ok=True)
        paths = {}
        for name in ("package", "manifest", "journal", "dotnet", "certificate", "password", "ca"):
            path = self.root / name
            if not path.exists():
                path.write_bytes(b"fixture")
            paths[name] = path
        paths["dotnet"].chmod(0o700)
        return SimpleNamespace(
            repository=repository, package=paths["package"], manifest=paths["manifest"],
            journal=paths["journal"], dotnet=paths["dotnet"],
            state_root=state or self.root / "state", evidence=evidence or self.root / "evidence",
            certificate=paths["certificate"], certificate_password_file=paths["password"],
            ca_certificate=paths["ca"], version="0.2.1", source_sha="1" * 40,
            package_sha256="2" * 64, manifest_sha256="3" * 64, journal_sha256="4" * 64,
            listen_url="https://localhost:7443")

    def test_config_owns_exact_native_collector_grants_and_revocation(self):
        browser_key_hash = self.root / "browser-key-hash.json"
        config = qualification.host_config(
            "https://localhost:7443", self.root / "server.pfx", self.root / "password",
            self.root / "host.lock", self.root / "store", self.root / "active.secret",
            self.root / "revoked.secret", browser_key_hash)
        self.assertEqual("fsgg.telemetry.host-config/2", config["Schema"])
        self.assertEqual([{
            "PrincipalId": "controlled-browser-preflight", "KeyHashFile": str(browser_key_hash),
            "WorkspaceIds": ["controlled-state"], "Revoked": False,
        }], config["BrowserPrincipals"])
        self.assertEqual(2, len(config["Credentials"]))
        active, revoked = config["Credentials"]
        self.assertEqual(("native-collector", "grant-controlled-active", 1, False),
                         (active["Role"], active["GrantId"], active["GrantGeneration"], active["Revoked"]))
        self.assertEqual(("native-collector", "grant-controlled-revoked", 1, True),
                         (revoked["Role"], revoked["GrantId"], revoked["GrantGeneration"], revoked["Revoked"]))
        invalid = qualification.host_config(
            "https://localhost:7443", self.root / "server.pfx", self.root / "password",
            self.root / "host.lock", self.root / "store", self.root / "active.secret",
            self.root / "revoked.secret", browser_key_hash, generation=0)
        self.assertTrue(all(row["GrantGeneration"] == 0 for row in invalid["Credentials"]))

    def test_startup_summary_is_bounded_and_never_discloses_output(self):
        stdout, stderr = self.root / "stdout", self.root / "stderr"
        stdout.write_bytes(b"private startup detail")
        stderr.write_bytes(b"private failure detail")
        summary = qualification.startup_output_summary(SimpleNamespace(returncode=2), stdout, stderr)
        self.assertIn("exitCode=2", summary)
        self.assertIn("stdoutBytes=22", summary)
        self.assertIn("stderrBytes=22", summary)
        self.assertNotIn("private", summary)
        self.assertNotIn("detail", summary)

        stdout.write_bytes(b"x" * (256 * 1024 + 1))
        with self.assertRaisesRegex(qualification.Refusal, "exceeded its custody bound"):
            qualification.startup_output_summary(SimpleNamespace(returncode=2), stdout, stderr)

    def test_command_summary_exposes_only_bounded_stage_code_size_and_digest(self):
        completed = SimpleNamespace(
            returncode=3, stdout="private stdout", stderr=json.dumps({
                "schema": "fsgg.telemetry.host-error/1", "code": "storage-unavailable",
                "errors": ["private path detail"],
            }) + "\n")
        summary = qualification.command_output_summary("initialize-store", completed)
        self.assertIn("stage=initialize-store", summary)
        self.assertIn("exitCode=3", summary)
        self.assertIn("errorCode=storage-unavailable", summary)
        self.assertIn("stdoutBytes=14", summary)
        self.assertRegex(summary, r"stderrSha256=[0-9a-f]{64}")
        for private in ("private stdout", "private path detail", "host-error"):
            self.assertNotIn(private, summary)

    def test_command_summary_refuses_to_project_unbounded_error_code(self):
        completed = SimpleNamespace(returncode=3, stdout="", stderr=json.dumps({
            "code": "PRIVATE/PATH/../../secret",
        }))
        summary = qualification.command_output_summary("initialize-store", completed)
        self.assertIn("errorCode=unavailable", summary)
        self.assertNotIn("PRIVATE", summary)

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
        self.assertIn('remove_owned_directory(state, state_identity)', source)

    def test_hosted_state_is_outside_temp_and_has_exact_owned_cleanup(self):
        workflow = (ROOT / ".github/workflows/telemetry-collector-container-qualification.yml").read_text(
            encoding="utf-8")
        state_parent = '$HOME/.local/state/fsgg-host-state-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}'
        self.assertEqual(1, workflow.count(state_parent))
        self.assertIn('--state-root "$host_state_parent/state"', workflow)
        self.assertNotIn('--state-root "$WORK_ROOT/host-state"', workflow)
        self.assertIn('nfs*|cifs*|smb3*|9p*|tmpfs*|overlay*|fuse*', workflow)
        self.assertIn('test ! -L "$host_state_parent"', workflow)
        self.assertIn('create-owned-directory --path "$host_state_parent"', workflow)
        self.assertIn('HOST_STATE_OWNERSHIP=%s|%s', workflow)
        self.assertIn('cleanup-owned-directory --path "$host_state_parent" --identity "$host_state_identity"',
                      workflow)
        self.assertNotIn('rm -rf -- "$host_state_parent"', workflow)

    def test_cleanup_refuses_a_replaced_state_identity(self):
        state = self.root / "state"
        state.mkdir()
        original = state.lstat()
        sentinel = state / "sentinel"
        sentinel.write_text("replacement", encoding="utf-8")
        with self.assertRaisesRegex(qualification.Refusal, "identity changed"):
            qualification.remove_owned_directory(state, (original.st_dev, original.st_ino + 1))
        self.assertEqual("replacement", sentinel.read_text(encoding="utf-8"))

    def test_exclusive_owned_creation_preserves_preexisting_leaf(self):
        parent = self.root / "private-state"
        parent.mkdir(mode=0o700)
        leaf = parent / "fixed-run"
        leaf.mkdir(mode=0o700)
        sentinel = leaf / "sentinel"
        sentinel.write_text("preexisting", encoding="utf-8")
        with self.assertRaises(FileExistsError):
            qualification.create_owned_directory(leaf)
        self.assertEqual("preexisting", sentinel.read_text(encoding="utf-8"))

    def test_owned_creation_and_cleanup_require_exact_identity(self):
        parent = self.root / "private-state"
        parent.mkdir(mode=0o700)
        leaf = parent / "fixed-run"
        identity = qualification.create_owned_directory(leaf)
        self.assertEqual(0o700, leaf.stat().st_mode & 0o777)
        with self.assertRaisesRegex(qualification.Refusal, "identity changed"):
            qualification.remove_owned_directory(leaf, (identity[0], identity[1] + 1))
        self.assertTrue(leaf.is_dir())
        qualification.remove_owned_directory(leaf, identity)
        self.assertFalse(leaf.exists())

    def test_qualification_refuses_relative_custody_paths_before_io(self):
        arguments = SimpleNamespace(
            repository=pathlib.Path("relative"), package=self.root / "package",
            manifest=self.root / "manifest", journal=self.root / "journal",
            dotnet=self.root / "dotnet", state_root=self.root / "state",
            evidence=self.root / "evidence", certificate=self.root / "certificate",
            certificate_password_file=self.root / "password", ca_certificate=self.root / "ca",
        )
        with self.assertRaisesRegex(qualification.Refusal, "absolute normalized path"):
            qualification.qualify(arguments)

    def test_main_never_removes_preexisting_state(self):
        state = self.root / "state"
        state.mkdir(mode=0o700)
        sentinel = state / "sentinel"
        sentinel.write_text("retained", encoding="utf-8")
        arguments = self.arguments(state=state)
        parser = mock.Mock()
        parser.parse_args.return_value = arguments
        with mock.patch.object(qualification, "parser", return_value=parser):
            self.assertEqual(2, qualification.main())
        self.assertEqual("retained", sentinel.read_text(encoding="utf-8"))

    def test_main_never_removes_preexisting_evidence_or_creates_state(self):
        evidence = self.root / "evidence"
        evidence.write_text("retained", encoding="utf-8")
        arguments = self.arguments(evidence=evidence)
        parser = mock.Mock()
        parser.parse_args.return_value = arguments
        with mock.patch.object(qualification, "parser", return_value=parser):
            self.assertEqual(2, qualification.main())
        self.assertEqual("retained", evidence.read_text(encoding="utf-8"))
        self.assertFalse(arguments.state_root.exists())

    def test_symlinked_release_input_refuses_before_state_creation(self):
        arguments = self.arguments()
        target = self.root / "package-target"
        target.write_bytes(b"fixture")
        arguments.package.unlink()
        arguments.package.symlink_to(target)
        with self.assertRaisesRegex(qualification.Refusal, "symbolic-link path component"):
            qualification.qualify(arguments)
        self.assertFalse(arguments.state_root.exists())

    def test_failure_after_exclusive_state_creation_cleans_only_owned_root(self):
        arguments = self.arguments()
        release = {"version": "0.2.1", "sourceSha": "1" * 40}
        with mock.patch.object(qualification.release_tools, "verify_release", return_value=release), \
             mock.patch.object(qualification, "extract_host",
                               side_effect=qualification.Refusal("fixture refusal")):
            with self.assertRaisesRegex(qualification.Refusal, "fixture refusal"):
                qualification.qualify(arguments)
        self.assertFalse(arguments.state_root.exists())
        self.assertFalse(arguments.evidence.exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)

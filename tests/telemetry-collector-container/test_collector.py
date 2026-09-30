#!/usr/bin/env python3
import importlib.util
import json
import os
import pathlib
import shutil
import tempfile
import unittest
import zipfile
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE_PATH = ROOT / "deployment/telemetry-collector/telemetry_collector.py"
SPEC = importlib.util.spec_from_file_location("telemetry_collector", MODULE_PATH)
collector = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(collector)


class CollectorRecipeTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="telemetry-collector-test-")
        self.root = pathlib.Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def package(self, name="host.nupkg", unsafe=None, capture=True):
        path = self.root / name
        with zipfile.ZipFile(path, "w") as archive:
            body = b"host" + (collector.CAPTURE_V2 if capture else b"")
            archive.writestr(collector.TOOLS_PREFIX + "FS.GG.Telemetry.Host.dll", body)
            archive.writestr(collector.TOOLS_PREFIX + "FS.GG.Telemetry.Host.runtimeconfig.json", b"{}")
            archive.writestr(collector.TOOLS_PREFIX + "dependency.dll", b"dependency")
            if unsafe:
                archive.writestr(unsafe, b"escape")
        return path

    def test_selection_accepts_only_real_021_shape(self):
        collector.validate_selection("0.2.1", "1" * 40, "2" * 64, "3" * 64, "4" * 64)
        for values in [
            ("0.2.0", "1" * 40, "2" * 64, "3" * 64, "4" * 64),
            ("0.2.1", "0" * 40, "2" * 64, "3" * 64, "4" * 64),
            ("0.2.1", "1" * 40, "0" * 64, "3" * 64, "4" * 64),
            ("0.2.1", "1" * 40, "2" * 64, "2" * 64, "4" * 64),
        ]:
            with self.assertRaises(collector.Refusal):
                collector.validate_selection(*values)

    def test_package_requires_capture_v2_and_safe_linux_closure(self):
        names = [row.filename for row in collector.package_members(self.package())]
        self.assertIn(collector.TOOLS_PREFIX + "FS.GG.Telemetry.Host.dll", names)
        with self.assertRaisesRegex(collector.Refusal, "capture /2"):
            collector.package_members(self.package("old.nupkg", capture=False))
        with self.assertRaisesRegex(collector.Refusal, "unsafe Host archive path"):
            collector.package_members(self.package("unsafe.nupkg", "../escape"))

    def test_context_contains_only_bound_host_and_fixed_recipe(self):
        package = self.package()
        binding = {
            "schema": "fsgg.telemetry.collector-build-binding/1",
            "version": "0.2.1", "sourceSha": "1" * 40, "packageSha256": "2" * 64,
            "manifestSha256": "3" * 64, "journalSha256": "4" * 64,
        }
        output = self.root / "context"
        collector.prepare_context(package, output, binding)
        self.assertEqual(binding, json.loads((output / "binding.json").read_text()))
        self.assertTrue((output / "host/FS.GG.Telemetry.Host.dll").is_file())
        self.assertFalse((output / "host/escape").exists())
        containerfile = (output / "Containerfile").read_text()
        self.assertIn("@sha256:", containerfile)
        self.assertNotIn("${RUNTIME_IMAGE}", containerfile)

    def test_release_binding_requires_both_feed_payload_proof(self):
        package = self.package()
        package_sha = collector.sha256(package)
        source_sha = "1" * 40
        manifest = self.root / "manifest.json"
        value = {
            "archiveSha256": package_sha, "createdAt": "2026-09-30T00:00:00Z",
            "dependencyLockSha256": "a" * 64, "framework": "net10.0",
            "packageId": "FS.GG.Telemetry.Host", "producerPayloadSha256": "sha256:" + "b" * 64,
            "runtimePrerequisites": ["Microsoft.NETCore.App 10.0"],
            "schema": "fsgg.telemetry.host-release/1", "sourceSha": source_sha,
            "supportedStoreSchemaMax": 12, "supportedStoreSchemaMin": 10,
            "tag": "telemetry-host/v0.2.1", "target": "linux-x64",
            "uiAssetTreeSha256": "c" * 64, "version": "0.2.1",
        }
        manifest.write_text(json.dumps(value, separators=(",", ":")))
        journal = self.root / "journal.json"
        observation = {"version": "0.2.1", "packageId": "FS.GG.Telemetry.Host",
                       "producerPayloadEqual": True, "payloadSha256": "sha256:" + "b" * 64}
        journal.write_text(json.dumps({"schema": "fsgg.telemetry-host-release-journal/v1",
                                       "observations": {"github": observation, "nuget": observation}}))
        journal_before = journal.read_bytes()
        with mock.patch.object(collector.subprocess, "run") as verifier:
            release = collector.verify_release(
                ROOT, package, manifest, journal, "0.2.1", source_sha, package_sha,
                collector.sha256(manifest), collector.sha256(journal))
        self.assertEqual("0.2.1", release["version"])
        verifier.assert_called_once()
        command = verifier.call_args.args[0]
        self.assertEqual("prepared", command[command.index("--feed") + 1])
        self.assertNotIn("--journal", command)
        self.assertEqual(journal_before, journal.read_bytes())
        broken = json.loads(journal.read_text())
        broken["observations"]["nuget"]["producerPayloadEqual"] = False
        journal.write_text(json.dumps(broken))
        with self.assertRaisesRegex(collector.Refusal, "nuget payload is unverified"):
            collector.verify_release(ROOT, package, manifest, journal, "0.2.1", source_sha,
                                     package_sha, collector.sha256(manifest), collector.sha256(journal))

    def test_container_shape_has_fixed_fences_and_disjoint_mounts(self):
        command = collector.create_command(
            pathlib.Path("/usr/bin/podman"), pathlib.Path("/private/root"), pathlib.Path("/private/runroot"),
            "collector", "localhost/collector:test",
            [(pathlib.Path("/private/config"), "/collector-config", False),
             (pathlib.Path("/private/source"), "/collector-config/native-codex-home", False)],
            "/opt/fsgg/controlled-collector")
        rendered = " ".join(map(str, command))
        for required in ("--pull=never", "--read-only", "--network=none", "--cap-drop=all",
                         "--security-opt=no-new-privileges", "--pids-limit=64", "--memory=256m",
                         "--http-proxy=false", "--userns=keep-id:uid=32768,gid=32768",
                         "--user=32768:32768", "/private/config:/collector-config:ro",
                         "/private/source:/collector-config/native-codex-home:ro"):
            self.assertIn(required, rendered)
        self.assertNotIn("--pid=host", rendered)
        self.assertNotIn("--uts=host", rendered)
        self.assertNotIn("podman.sock", rendered)

    def test_keep_id_contract_preserves_outer_receipt_custody_and_cleanup(self):
        state = self.root / "state"
        state.mkdir(mode=0o700)
        receipt = state / "config/native-evidence/controlled-custody.json"
        receipt.parent.mkdir(mode=0o700, parents=True)
        self.assertEqual(0o700, state.stat().st_mode & 0o777)
        receipt.write_text('{"verdict":"controlled-topology-only"}\n', encoding="utf-8")
        receipt.chmod(0o600)
        self.assertEqual(os.getuid(), receipt.stat().st_uid)
        self.assertEqual("controlled-topology-only", json.loads(receipt.read_text())["verdict"])

        inspected = {
            "Config": {"User": "32768:32768", "Env": []},
            "EffectiveCaps": [],
            "HostConfig": {
                "UsernsMode": "private",
                "IDMappings": {"UidMap": ["0:1:32768", "32768:0:1"],
                               "GidMap": ["0:1:32768", "32768:0:1"]},
                "NetworkMode": "none",
                "ReadonlyRootfs": True, "PidMode": "private", "UTSMode": "private",
                "CapAdd": [], "CapDrop": ["CAP_ALL"], "SecurityOpt": ["no-new-privileges"],
                "PidsLimit": 64, "Memory": 256 * 1024 * 1024, "NanoCpus": 1_000_000_000,
            },
            "Mounts": [{"Destination": "/collector-config/native-evidence", "RW": True}],
        }
        collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})
        for equivalent in (["ALL"], sorted(collector.PODMAN_DEFAULT_CAPABILITIES)):
            inspected["HostConfig"]["CapDrop"] = equivalent
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})
        inspected["EffectiveCaps"] = None
        collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})
        inspected["EffectiveCaps"] = []

        inspected["HostConfig"]["CapDrop"] = sorted(collector.PODMAN_DEFAULT_CAPABILITIES)[:-1]
        with self.assertRaisesRegex(collector.Refusal, "bounding fence"):
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})

        inspected["HostConfig"]["CapDrop"] = sorted(collector.PODMAN_DEFAULT_CAPABILITIES)
        inspected["HostConfig"]["CapAdd"] = ["CAP_SYS_ADMIN"]
        with self.assertRaisesRegex(collector.Refusal, "added capabilities"):
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})

        inspected["HostConfig"]["CapAdd"] = []
        inspected["EffectiveCaps"] = ["CAP_CHOWN"]
        with self.assertRaisesRegex(collector.Refusal, "effective capabilities"):
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})

        inspected["EffectiveCaps"] = []
        inspected["HostConfig"]["UsernsMode"] = "host"
        with self.assertRaisesRegex(collector.Refusal, "user namespace"):
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})
        inspected["HostConfig"]["UsernsMode"] = "private"
        inspected["HostConfig"]["IDMappings"]["UidMap"] = ["0:1:32768"]
        with self.assertRaisesRegex(collector.Refusal, "keep-id mapping"):
            collector.inspect_container(inspected, {("/collector-config/native-evidence", True)})

        shutil.rmtree(state)
        self.assertFalse(state.exists())

    def test_controlled_receipt_cannot_claim_native_qualification(self):
        script = (ROOT / "deployment/telemetry-collector/controlled-collector.sh").read_text()
        self.assertIn('"verdict":"controlled-topology-only"', script)
        self.assertIn('"nativeAccessQualified":false', script)
        self.assertIn('"modelSupportObserved":false', script)
        self.assertIn('"captureApplied":false', script)


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Refusal and recovery controls for independent Host release effects."""

from __future__ import annotations

import copy
import io
import contextlib
import urllib.error
import os
import hashlib
import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
import zipfile
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_successor_execution import Dispatch, JournalState, Observation, Refused, advance_effects
from telemetry_host_successor_admission import HostAdmission, FAILED_ORIGINAL, ordinary_route, validate_failed_original
from telemetry_host_successor_execution import effects
from telemetry_host_successor_provider import HostProvider, NotFound


def manifest():
    return {
        "schema": "fsgg.telemetry.host-release/1", "packageId": "FS.GG.Telemetry.Host",
        "version": "0.6.0", "tag": "telemetry-host/v0.6.0", "sourceSha": "a" * 40,
        "archiveSha256": "b" * 64, "producerPayloadSha256": "sha256:" + "c" * 64,
    }


class StoreProfileTests(unittest.TestCase):
    def test_prepared_current_package_manifest_uses_only_schema14(self):
        spec = importlib.util.spec_from_file_location("host_release_prepare", ROOT / "scripts/telemetry-host-release.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            package = root / "fixture.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("FS.GG.Telemetry.Host.nuspec",
                                 "<package><metadata><id>FS.GG.Telemetry.Host</id><version>0.6.0</version></metadata></package>")
                archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll", b"fixture")
            lock = root / "lock.json"
            lock.write_text("{}")
            assets = root / "assets"
            assets.mkdir()
            (assets / "app.js").write_text("fixture")
            prepared = module.build_manifest(SimpleNamespace(package=package, lock=lock, assets=assets,
                                             version="0.6.0", tag="telemetry-host/v0.6.0", source_sha="a" * 40))
            self.assertEqual((prepared["supportedStoreSchemaMin"], prepared["supportedStoreSchemaMax"]), (14, 14))
            self.assertEqual(prepared["version"], "0.6.0")

    def test_current_profile_is_schema14_only_and_legacy_artifact_stays_unchanged(self):
        spec = importlib.util.spec_from_file_location("host_release_profile", ROOT / "scripts/telemetry-host-release.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertEqual(module.store_schema_profile("0.6.0"), (14, 14))
        self.assertEqual(module.store_schema_profile("0.5.0"), (14, 14))
        self.assertEqual(module.store_schema_profile("0.4.0"), (10, 12))
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "manifest.json"
            value = {**manifest(), "framework": "net10.0", "target": "linux-x64",
                     "dependencyLockSha256": "d" * 64, "uiAssetTreeSha256": "e" * 64,
                     "supportedStoreSchemaMin": 14, "supportedStoreSchemaMax": 14,
                     "runtimePrerequisites": ["Microsoft.AspNetCore.App 10.0", "Microsoft.NETCore.App 10.0"],
                     "createdAt": "2026-10-06T00:00:00Z"}
            path.write_text(json.dumps(value))
            self.assertEqual(module.load_manifest(path), value)
            for minimum, maximum in ((10, 14), (13, 14), (14, 15), (10, 12)):
                with self.subTest(minimum=minimum, maximum=maximum):
                    path.write_text(json.dumps({**value, "supportedStoreSchemaMin": minimum,
                                                "supportedStoreSchemaMax": maximum}))
                    with self.assertRaisesRegex(ValueError, "store schema range"):
                        module.load_manifest(path)
            old = {**value, "version": "0.4.0", "tag": "telemetry-host/v0.4.0",
                   "supportedStoreSchemaMin": 10, "supportedStoreSchemaMax": 12}
            path.write_text(json.dumps(old))
            self.assertEqual(module.load_manifest(path), old)
            with self.assertRaisesRegex(Refused, "exact Host 0.6.0"):
                effects(old)
            previous = {**value, "version": "0.5.0", "tag": "telemetry-host/v0.5.0"}
            path.write_text(json.dumps(previous))
            self.assertEqual(module.load_manifest(path), previous)
            with self.assertRaisesRegex(Refused, "exact Host 0.6.0"):
                effects(previous)


class EarlyQualificationTests(unittest.TestCase):
    def test_actual_host_http_and_closed_bindings_run_before_costly_work(self):
        def check(source, expensive, native=False):
            pure = source.index("python3 tests/telemetry-host-successor/run.py")
            test = source.index('--filter "FullyQualifiedName~HostPassiveReceiptTests|FullyQualifiedName~HostSchemaProfileTests|FullyQualifiedName~RemoteTelemetryTests.protected native collector resolves durable dispatch and replays retained envelope"')
            assert pure < test < source.index(expensive)
            assert 'host-passive.trx" --minimum 4' in source
            assert 'timeout-minutes: 5' in source
            assert '--blame ' in source and '--blame-hang' not in source and '--blame-crash' not in source
            assert 'Retain passive Host HTTP diagnostics' in source and 'always()' in source
            assert 'retention-days: 7' in source and 'continue-on-error:' not in source
            assert 'dotnet test tests/FS.GG.Telemetry.Tests/FS.GG.Telemetry.Tests.fsproj -c Release --no-restore\n' in source
            assert 'e["passiveReceiptQualified"] is True' in source
            if native:
                assert 'if: ${{ !(inputs.learn_native_fixture || inputs.learn_native_preflight) }}' in source
        for path, expensive, native in [
            (".github/workflows/telemetry-host-package.yml", "Qualify the typed HOST binding", True),
            (".github/workflows/release-telemetry-host-successor-candidate.yml", "Qualify Host, dashboard and browser", False)]:
            source = (ROOT / path).read_text()
            check(source, expensive, native)
            for mutant in [source.replace('FullyQualifiedName~HostPassiveReceiptTests', 'FullyQualifiedName~Unrelated'),
                           source.replace('FullyQualifiedName~RemoteTelemetryTests.protected native collector', 'FullyQualifiedName~UnrelatedNativeCollector'),
                           source.replace('host-passive.trx" --minimum 4', 'host-passive.trx" --minimum 0'),
                           source.replace('python3 tests/telemetry-host-successor/run.py', 'echo absent'),
                           source.replace('always()', 'success()'),
                           source.replace('e["passiveReceiptQualified"] is True', 'True')]:
                with self.assertRaises((AssertionError, ValueError)):
                    check(mutant, expensive, native)


class InstalledPassiveInputTests(unittest.TestCase):
    def test_actual_embedded_package_producer_preserves_native_identity(self):
        source = (ROOT / "tests/standalone-telemetry-host-package/production.sh").read_text()
        marker = '  python3 - "$WORK/envelope.json" <<' + "'PY'\n"
        block = source.split(marker, 1)[1].split("\nPY\n", 1)[0]
        def identity(program, output):
            subprocess.run([sys.executable, "-c", program, str(output)], check=True, capture_output=True)
            value = json.loads(output.read_text())
            self.assertEqual(value["payload"]["eventCount"], len(value["payload"]["events"]))
            return value["payload"]["events"][1]["identity"]
        expected = "passive-" + hashlib.sha256(b"codex-rollout/1\x1f22222222-2222-2222-2222-222222222222\x1f33333333-3333-3333-3333-333333333333\x1fpackage-response").hexdigest()
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            self.assertEqual(identity(block, root / "good.json"), expected)
            correction_marker = '  python3 - "$WORK/envelope.json" "$WORK/passive-correction.json" <<' + "'PY_PASSIVE'\n"
            correction = source.split(correction_marker, 1)[1].split("\nPY_PASSIVE\n", 1)[0]
            subprocess.run([sys.executable, "-c", correction, str(root / "good.json"), str(root / "correction.json")], check=True, capture_output=True)
            original = json.loads((root / "good.json").read_text())
            corrected = json.loads((root / "correction.json").read_text())
            self.assertNotEqual(original["batchId"], corrected["batchId"])
            self.assertEqual(corrected["batchId"], corrected["payload"]["ingestId"])
            self.assertEqual([2, 1], [value["payload"]["eventCount"] for value in [original, corrected]])
            self.assertEqual([expected], [event["identity"] for event in corrected["payload"]["events"]])
            self.assertEqual(2, corrected["payload"]["events"][0]["revision"])
            calls = [line for line in source.splitlines() if 'node "$ROOT/tests/standalone-telemetry-host-package/installed-browser.js"' in line]
            self.assertEqual(2, len(calls))
            self.assertTrue(all('"$browser_spki" 2 >' in line for line in calls))
            # A syntactically valid escaped-text identity must fail this protocol control.
            bad = block.replace(chr(92) + "x1f", chr(92) * 2 + "x1f")
            self.assertNotEqual(bad, block)
            self.assertNotEqual(identity(bad, root / "bad.json"), expected)

    def test_actual_browser_receipt_check_preserves_old_profile_and_exact_correction_count(self):
        # Execute the maintained script with a page double: no browser or network is launched.
        with tempfile.TemporaryDirectory() as temporary:
            mock = pathlib.Path(temporary) / "playwright.js"
            for applied, rejected, expected, success in [
                (1, 0, None, True), (2, 0, "2", True),
                (1, 0, "2", False), (2, 0, None, False),
                (12, 0, "2", False), (2, 1, "2", False),
                (2, 2, "2", False), (0, 0, "2", False),
                (2, 0, "0", False), (2, 0, "invalid", False)]:
                with self.subTest(applied=applied, rejected=rejected, expected=expected):
                    health = f"0 pending receipts · {applied} applied receipts · {rejected} rejected receipts · consistent"
                    mock.write_text("const control={fill:async()=>{},click:async()=>{},waitFor:async()=>{},innerText:async()=>" + json.dumps(health) + "};\n"
                                    "const page={goto:async()=>({status:()=>200}),locator:()=>control,getByRole:()=>({filter:()=>control,...control})};\n"
                                    "module.exports={chromium:{launch:async()=>({newPage:async()=>page,close:async()=>{}})}};\n")
                    args = ["node", str(ROOT / "tests/standalone-telemetry-host-package/installed-browser.js"), str(mock),
                            "https://fixture.invalid", "fixture-principal", "fixture-key", "fixture-workspace", "fixture-item", "fixture-spki"]
                    if expected is not None:
                        args.append(expected)
                    result = subprocess.run(args, capture_output=True, text=True, timeout=10)
                    self.assertEqual(success, result.returncode == 0, result.stderr)


class Journal:
    def __init__(self, content_id):
        self.state = JournalState(1, content_id, {})

    def read(self):
        return self.state

    def compare_and_swap(self, expected, effect, state):
        if self.state != expected:
            return False
        self.state = JournalState(expected.generation + 1, expected.content_id,
                                  {**expected.effects, effect: state})
        return True


class Admission:
    def __init__(self):
        self.denied = set()

    def authorize(self, content_id, effect, action, request_digest):
        return (effect, action) not in self.denied


class Provider:
    def __init__(self):
        self.visible = {}
        self.writes = []
        self.delayed = set()

    def observe(self, effect):
        value = self.visible.get(effect.identity)
        if value is None:
            return Observation("absent")
        return Observation("matched" if value == effect.target_digest else "mismatched", value)

    def dispatch(self, effect):
        self.writes.append(effect.identity)
        if effect.identity not in self.delayed:
            self.visible[effect.identity] = effect.target_digest
        return Dispatch("unknown")


class HostReleaseTests(unittest.TestCase):
    def setUp(self):
        self.content_id, self.ordered = effects(manifest())
        self.journal = Journal(self.content_id)
        self.admission = Admission()
        self.provider = Provider()

    def advance(self):
        return advance_effects(self.content_id, self.ordered, self.journal, self.admission, self.provider)

    def test_successor_identity_and_journal_are_coherent(self):
        self.assertEqual(self.ordered[0].target_digest, "a" * 40)
        publisher = (ROOT / "scripts" / "telemetry-host-successor-publish.py").read_text()
        candidate = (ROOT / ".github" / "workflows" / "release-telemetry-host-successor-candidate.yml").read_text()
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/utel-host-rel-12"', publisher)
        self.assertNotIn("utel-host-rel-10", publisher)
        self.assertIn('"version": "0.6.0"', publisher)
        self.assertIn("--version 0.6.0 --tag telemetry-host/v0.6.0", candidate)
        self.assertIn("FS.GG.Telemetry.Host.0.6.0.nupkg", candidate)
        self.assertIn("telemetry-host-060-candidate", candidate)
        self.assertIn("telemetry-host-060-publisher", (ROOT / ".github/workflows/release-telemetry-host-successor-publish.yml").read_text())

    def test_every_effect_is_admitted_and_read_back_once(self):
        self.assertEqual(len(self.ordered), 8)
        for effect in self.ordered:
            self.assertEqual(self.advance(), "waiting")
            self.assertEqual(self.advance(), "verified")
            self.assertEqual(self.provider.writes[-1], effect.identity)
        self.assertEqual(self.advance(), "complete")
        self.assertEqual(len(self.provider.writes), 8)

    def test_denied_dispatch_never_mutates_provider(self):
        self.admission.denied.add(("tag", "dispatch"))
        with self.assertRaisesRegex(Refused, "dispatch admission denied"):
            self.advance()
        self.assertEqual(self.provider.writes, [])

    def test_delayed_tag_readback_does_not_retry_or_advance(self):
        self.provider.delayed.add("tag")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.advance(), "waiting")
        self.assertEqual(self.provider.writes, ["tag"])
        self.provider.visible["tag"] = self.ordered[0].target_digest
        self.assertEqual(self.advance(), "verified")

    def test_candidate_archive_identity_and_first_attempt_are_required(self):
        path = ROOT / "scripts" / "telemetry-host-successor-artifact.py"
        spec = importlib.util.spec_from_file_location("host_artifact", path)
        assert spec and spec.loader
        verifier = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(verifier)
        source = "a" * 40
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            archive = root / "candidate.zip"
            package_digest = hashlib.sha256(b"fixture").hexdigest()
            positive = {"schema": "fsgg.telemetry-host-package-evidence/v1", "packageId": "FS.GG.Telemetry.Host",
                        "version": "0.6.0", "archiveSha256": package_digest, "passed": 1, "failed": 0,
                        "productionStorage": "eligible-linux-x64", "syntheticTls": True, "passiveReceiptQualified": True}
            def write_archive(evidence):
                with zipfile.ZipFile(archive, "w") as zipped:
                    zipped.writestr("manifest.json", json.dumps({**manifest(), "archiveSha256": package_digest}))
                    zipped.writestr("package-evidence.json", json.dumps(evidence))
                    zipped.writestr("FS.GG.Telemetry.Host.0.6.0.nupkg", "fixture")
            write_archive(positive)
            digest = hashlib.sha256(archive.read_bytes()).hexdigest()
            run = {"id": 123, "path": verifier.WORKFLOW, "head_sha": source,
                   "head_branch": "main", "event": "workflow_dispatch", "conclusion": "success",
                   "run_attempt": 1, "repository": {"id": verifier.REPOSITORY_ID}}
            artifact = {"id": 456, "name": f"telemetry-host-successor-candidate-{source}-123",
                        "expired": False, "digest": "sha256:" + digest,
                        "workflow_run": {"id": 123, "head_sha": source, "head_branch": "main",
                                         "repository_id": verifier.REPOSITORY_ID,
                                         "head_repository_id": verifier.REPOSITORY_ID}}
            with patch.object(verifier.subprocess, "run"):
                self.assertEqual(verifier.verify(artifact, run, archive, root / "good", source)["version"], "0.6.0")
            for field, value in [("productionStorage", "not-qualified:overlay"), ("passiveReceiptQualified", False),
                                 ("syntheticTls", False), ("failed", 1), ("failed", False), ("version", "0.5.0"),
                                 ("archiveSha256", "0" * 64)]:
                with self.subTest(field=field):
                    write_archive({**positive, field: value})
                    changed_artifact = {**artifact, "digest": "sha256:" + hashlib.sha256(archive.read_bytes()).hexdigest()}
                    with patch.object(verifier.subprocess, "run") as checker:
                        with self.assertRaisesRegex(ValueError, "positive passive receipt"):
                            verifier.verify(changed_artifact, run, archive, root / ("bad-" + field), source)
                        checker.assert_not_called()
            write_archive(positive)
            altered = {**run, "run_attempt": 2}
            with self.assertRaisesRegex(ValueError, "first-attempt"):
                verifier.verify(artifact, altered, archive, root / "rerun", source)
            altered_artifact = {**artifact, "digest": "sha256:" + "0" * 64}
            with self.assertRaisesRegex(ValueError, "digest differs"):
                verifier.verify(altered_artifact, run, archive, root / "tampered", source)

    def test_prior_release_identity_and_wrong_payload_are_refused(self):
        for key, value in (("version", "0.3.0"), ("tag", "telemetry-host/v0.3.0"),
                           ("version", "0.2.1"), ("tag", "telemetry-host/v0.2.1"),
                           ("version", "0.1.3"), ("tag", "telemetry-host/v0.1.3"),
                           ("producerPayloadSha256", "bad")):
            row = manifest(); row[key] = value
            with self.assertRaises(Refused):
                effects(row)

    def test_live_tag_draft_and_original_asset_readback(self):
        class API:
            tag = None
            release = None
            assets = None
            def __init__(self):
                self.assets = {}
                self.writes = []
            def get(self, path):
                if path.endswith("/git/ref/tags/telemetry-host/v0.6.0") and self.tag:
                    return {"object": {"sha": self.tag}}
                if path.endswith("/releases/tags/telemetry-host/v0.6.0") and self.release and not self.release["draft"]:
                    return self.release
                if path.endswith("/releases?per_page=100&page=1"):
                    return [self.release] if self.release else []
                if path.endswith("/releases/1/assets?per_page=100"):
                    return [{"id": i, "name": name} for i, name in enumerate(self.assets, 1)]
                raise NotFound(path)
            def post(self, path, body):
                self.writes.append((path, body))
                if path.endswith("/git/refs"):
                    self.tag = body["sha"]; return {"object": {"sha": self.tag}}
                if path.endswith("/releases"):
                    self.release = {"id": 1, "tag_name": body["tag_name"],
                                    "body": body["body"], "draft": body["draft"]}
                    return self.release
                raise AssertionError(path)
            def upload_asset(self, release_id, name, path):
                self.assets[name] = path.read_bytes()
                return {"id": len(self.assets), "name": name}
            def download_asset(self, asset_id):
                return list(self.assets.values())[asset_id - 1]
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            row = manifest()
            package = root / "FS.GG.Telemetry.Host.0.6.0.nupkg"
            package.write_bytes(b"exact original Host archive")
            row["archiveSha256"] = hashlib.sha256(package.read_bytes()).hexdigest()
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            api = API()
            provider = HostProvider(api, manifest_path, "github-token", "nuget-key")
            _, ordered = effects(row)
            tag, draft, archive = ordered[0], ordered[1], ordered[4]
            self.assertEqual(provider.observe(tag).state, "absent")
            self.assertEqual(provider.dispatch(tag).state, "applied")
            self.assertEqual(provider.observe(tag).state, "matched")
            self.assertEqual(provider.dispatch(draft).state, "applied")
            self.assertEqual(provider.observe(draft).state, "matched")
            self.assertEqual(provider.dispatch(archive).state, "applied")
            self.assertEqual(provider.observe(archive).state, "matched")
            self.assertEqual(provider.dispatch(ordered[5]).state, "applied")
            self.assertEqual(provider.observe(ordered[5]).state, "matched")
            api.assets[package.name] = b"different archive"
            self.assertEqual(provider.observe(archive).state, "mismatched")
            api.tag = "e" * 40
            self.assertEqual(provider.observe(tag).state, "mismatched")

    def test_publication_journal_requires_both_exact_feed_readbacks(self):
        class API:
            def __init__(self, raw):
                self.raw = raw
            def get(self, path):
                if path.endswith("/releases/tags/telemetry-host/v0.6.0"):
                    raise NotFound(path)
                if path.endswith("/releases?per_page=100&page=1"):
                    return [{"id": 1, "tag_name": "telemetry-host/v0.6.0", "draft": True}]
                if path.endswith("/releases/1/assets?per_page=100"):
                    return [{"id": 1, "name": "publication-journal.json"}]
                raise AssertionError(path)
            def download_asset(self, asset_id):
                return self.raw
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            row = manifest()
            manifest_path = root / "manifest.json"
            manifest_path.write_text(json.dumps(row, sort_keys=True, separators=(",", ":")) + "\n")
            raw_package = root / "readback.nupkg"
            raw_package.write_bytes(b"feed package")
            sha = hashlib.sha256(raw_package.read_bytes()).hexdigest()
            content_id, ordered = effects(row)
            journal = {"schema": "fsgg.telemetry-host-release-journal/v1", "manifestSha256": content_id,
                       "observations": {feed: {"archiveSha256": sha,
                                               "payloadSha256": row["producerPayloadSha256"],
                                               "producerPayloadEqual": True} for feed in ("github", "nuget")}}
            api = API(json.dumps(journal).encode())
            provider = HostProvider(api, manifest_path, "github-token", "nuget-key")
            with patch.object(provider, "_download_package", return_value=raw_package):
                self.assertEqual(provider.observe(ordered[6]).state, "matched")
                journal["observations"]["nuget"]["archiveSha256"] = "0" * 64
                api.raw = json.dumps(journal).encode()
                with self.assertRaisesRegex(Exception, "journal and public feed differ"):
                    provider.observe(ordered[6])

    def test_live_admission_revokes_on_changed_main_or_actor(self):
        class API:
            main = "d" * 40
            actor = "EHotwagner"
            def get(self, path):
                if path.endswith("/.github"):
                    return {"id": 1269292704, "full_name": "FS-GG/.github"}
                if "/actions/runs/123" in path:
                    return {"repository": {"id": 1269292704},
                            "path": ".github/workflows/release-telemetry-host-successor-publish.yml",
                            "event": "workflow_dispatch", "head_branch": "main", "head_sha": "d" * 40,
                            "run_attempt": 1, "actor": {"login": self.actor}, "status": "in_progress"}
                if path.endswith("/git/ref/heads/main"):
                    return {"object": {"sha": self.main}}
                raise AssertionError(path)
        api = API()
        admission = HostAdmission(api, manifest(), "d" * 40, 123, "EHotwagner", "refs/heads/main")
        effect = self.ordered[0]
        self.assertTrue(admission.authorize(self.content_id, effect.identity, "intent", effect.request_digest))
        api.main = "e" * 40
        self.assertFalse(admission.authorize(self.content_id, effect.identity, "dispatch", effect.request_digest))
        api.main = "d" * 40; api.actor = "someone-else"
        self.assertFalse(admission.authorize(self.content_id, effect.identity, "settle", effect.request_digest))


# Public release metadata only; no package bytes, credentials or host/session configuration.
RETAINED_MANIFEST = json.loads('{"archiveSha256":"ac77ba38f6d4974586d84d75f928fb0b5edbf4dcd747266ac5453f00d3676af6","createdAt":"2026-10-18T14:29:33.129575Z","dependencyLockSha256":"3984b5c8cb7946815cce4fd65e9ef41423e5ed87940ebd19f72e2f22f48ccd5a","framework":"net10.0","packageId":"FS.GG.Telemetry.Host","producerPayloadSha256":"sha256:b3552790f651ad697a7c24f6e35ae6554974a1dbc9dce116c8c685b1ac64fe98","runtimePrerequisites":["Microsoft.AspNetCore.App 10.0","Microsoft.NETCore.App 10.0"],"schema":"fsgg.telemetry.host-release/1","sourceSha":"54335971452d2dfadb04e7704774ae2e48718e54","supportedStoreSchemaMax":14,"supportedStoreSchemaMin":14,"tag":"telemetry-host/v0.6.0","target":"linux-x64","uiAssetTreeSha256":"fcc9c65ddfd67c6e3e7837242d36d3e510fca00e0746a1ecb6bf788426d49615","version":"0.6.0"}')


class RepairAPI:
    def __init__(self):
        self.source = FAILED_ORIGINAL["sourceSha"]
        self.head = "d" * 40
        self.original = {"id": FAILED_ORIGINAL["runId"], "repository": {"id": 1269292704},
            "path": ".github/workflows/release-telemetry-host-successor-publish.yml", "event": "workflow_dispatch",
            "head_branch": "main", "head_sha": self.source, "run_attempt": 1,
            "actor": {"login": "EHotwagner"}, "status": "completed", "conclusion": "failure"}
        self.rows = [{"path": "src/FS.GG.Telemetry.Host/Program.fs", "mode": "100644", "type": "blob", "sha": "a" * 40},
                     {"path": "scripts/telemetry-host-successor-publish.py", "mode": "100644", "type": "blob", "sha": "b" * 40}]
        self.changed = copy.deepcopy(self.rows)
        self.changed[1]["sha"] = "c" * 40
        self.truncated = False
        self.run_mutation = {}

    def get(self, path):
        if path.endswith("/.github"):
            return {"id": 1269292704, "full_name": "FS-GG/.github"}
        if path.endswith("/git/ref/heads/main"):
            return {"object": {"sha": self.head}}
        if path.endswith("/actions/runs/" + str(FAILED_ORIGINAL["runId"])):
            return copy.deepcopy(self.original)
        if "/actions/runs/" in path:
            return {**copy.deepcopy(self.original), "id": 999, "head_sha": self.head,
                    "status": "in_progress", "conclusion": None, **self.run_mutation}
        if "/compare/" in path:
            return {"status": "ahead", "ahead_by": 1, "behind_by": 0,
                    "base_commit": {"sha": self.source}, "merge_base_commit": {"sha": self.source}}
        if "/git/commits/" in path:
            source = path.rsplit("/", 1)[1]
            return {"sha": source, "tree": {"sha": "1" * 40 if source == self.source else "2" * 40}}
        if "/git/trees/" in path:
            tree = path.rsplit("/", 1)[1].split("?")[0]
            return {"sha": tree, "truncated": self.truncated,
                    "tree": copy.deepcopy(self.rows if tree == "1" * 40 else self.changed)}
        raise AssertionError(path)


class HostDispatchDiagnosticTests(unittest.TestCase):
    def publisher(self):
        spec = importlib.util.spec_from_file_location("host_safe_diagnostic", ROOT / "scripts/telemetry-host-successor-publish.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        return module

    def test_actual_http_permission_failure_is_safe_unknown_and_stale_read_is_not_joined(self):
        from release_successor_provider import GitHubAPI
        secret = "SECRET-credential-argv-body"
        error = urllib.error.HTTPError("https://api.github.com/repos/FS-GG/.github/git/refs", 403, secret,
             {"X-GitHub-Request-Id": "FAKE:123", "Authorization": secret,
              "X-Accepted-GitHub-Permissions": "contents=write; workflows=write"},
             io.BytesIO(json.dumps({"message": "refusing to allow a GitHub App to create or update workflow " + secret + " without workflows permission",
                                   "token": secret}).encode()))
        for failure in (error, urllib.error.URLError(secret)):
            with self.subTest(kind=type(failure).__name__), tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary); path = root / "manifest.json"; path.write_text(json.dumps(manifest()))
                api = GitHubAPI(secret)
                api.last_request_diagnostic = {"method": "GET", "path": "/repos/FS-GG/.github/old-observation", "status": 404}
                provider = HostProvider(api, path, secret, secret)
                with patch("urllib.request.urlopen", side_effect=failure) as opening:
                    outcome = provider.dispatch(effects(manifest())[1][0])
                self.assertEqual(outcome.state, "unknown")
                if isinstance(failure, urllib.error.HTTPError):
                    failure.close()
                self.assertEqual(opening.call_count, 1)
                diagnostic = provider.last_dispatch_diagnostic
                rendered = json.dumps(diagnostic)
                self.assertNotIn(secret, rendered)
                self.assertNotIn("old-observation", rendered)
                if isinstance(failure, urllib.error.HTTPError):
                    self.assertEqual(diagnostic["category"], "provider-refused")
                    self.assertEqual(diagnostic["request"]["status"], 403)
                    self.assertEqual(diagnostic["request"]["responseClass"], "workflow-permission-denied")
                    self.assertEqual(diagnostic["request"]["acceptedPermissions"], "contents=write; workflows=write")
                else:
                    self.assertEqual(diagnostic["category"], "transport-failure")
                    self.assertEqual(diagnostic["request"], {"method": "POST", "path": "/repos/FS-GG/.github/git/refs", "status": None})
                # A later real successful send clears the old error; no inherited diagnostic.
                response = SimpleNamespace(status=201, headers={}, read=lambda: b'{}')
                with patch("urllib.request.urlopen") as opening:
                    opening.return_value.__enter__.return_value = response
                    self.assertEqual(provider.dispatch(effects(manifest())[1][0]).state, "applied")
                self.assertIsNone(provider.last_dispatch_diagnostic)

    def test_process_and_malformed_diagnostics_never_emit_raw_secrets_or_replace_unknown(self):
        secret = "SECRET-process-key-and-output"
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "manifest.json"; path.write_text(json.dumps(manifest()))
            api = SimpleNamespace(last_request_diagnostic={"status": 404})
            provider = HostProvider(api, path, secret, secret)
            failure = subprocess.CalledProcessError(7, ["dotnet", "--api-key", secret], output=secret, stderr=secret)
            with patch.object(subprocess, "run", side_effect=failure):
                self.assertEqual(provider.dispatch(effects(manifest())[1][2]).state, "unknown")
            self.assertEqual(provider.last_dispatch_diagnostic, {"category": "process-failure", "returnCode": 7})
            with patch.object(subprocess, "run", side_effect=failure), \
                 patch("telemetry_host_successor_provider.sanitized_dispatch_diagnostic", side_effect=RuntimeError(secret)):
                self.assertEqual(provider.dispatch(effects(manifest())[1][2]).state, "unknown")
            self.assertEqual(provider.last_dispatch_diagnostic, {"category": "process-failure", "diagnosticUnavailable": True})
            publisher = self.publisher()
            provider.last_dispatch_diagnostic = {"category": "process-failure", "returnCode": 7, "argv": secret,
                "request": {"method": "POST", "path": "/repos/FS-GG/.github/git/refs?token=" + secret,
                            "status": 403, "responseClass": secret, "rawBody": secret, "acceptedPermissions": secret}}
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                publisher.emit_dispatch_diagnostic(provider)
                publisher.emit_dispatch_diagnostic(provider)
            self.assertEqual(output.getvalue().count("Host successor dispatch diagnostic:"), 1)
            self.assertNotIn(secret, output.getvalue())
            self.assertIsNone(provider.last_dispatch_diagnostic)
            with patch.object(publisher, "sanitized_dispatch_diagnostic", side_effect=RuntimeError(secret)), \
                 patch("builtins.print", side_effect=OSError(secret)):
                publisher.emit_dispatch_diagnostic(provider)  # Reporting failure cannot escape into effect control.


class HostMainJournalRepairTests(unittest.TestCase):
    def validate(self, api, **changes):
        values = dict(manifest=RETAINED_MANIFEST, publisher_sha=api.head,
                      candidate_run=FAILED_ORIGINAL["candidateRunId"], artifact_id=FAILED_ORIGINAL["artifactId"],
                      archive_sha=FAILED_ORIGINAL["archiveSha256"])
        values.update(changes)
        return validate_failed_original(api, **values)

    def test_actual_workflow_default_false_and_explicit_flag_shell(self):
        import textwrap
        source = (ROOT / ".github/workflows/release-telemetry-host-successor-publish.yml").read_text()
        declared = source.split("      continue_failed_original:\n", 1)[1].split("      publish:", 1)[0]
        self.assertIn("default: false", declared)
        self.assertIn("type: boolean", declared)
        self.assertIn("CONTINUE_FAILED_ORIGINAL: ${{ inputs.continue_failed_original }}", source)
        script = textwrap.dedent(source.split("      - name: Preflight or publish exact retained Host candidate", 1)[1].split("        run: |\n", 1)[1])
        self.assertEqual(subprocess.run(["bash", "-n"], input=script, text=True, capture_output=True).returncode, 0)
        for selected in ("false", "true"):
            environment = {**os.environ, "CONTINUE_FAILED_ORIGINAL": selected, "PUBLISH": "false",
                           "CANDIDATE_RUN_ID": "1", "CANDIDATE_ARTIFACT_ID": "2", "CANDIDATE_ARCHIVE_SHA256": "digest",
                           "RUNNER_TEMP": "/unused-fixture"}
            result = subprocess.run(["bash", "-c", 'python3() { printf "%s\\n" "$@"; };\n' + script],
                                    env=environment, capture_output=True, text=True, check=True)
            args = result.stdout.splitlines()
            self.assertEqual(args.count("--continue-failed-original"), 1 if selected == "true" else 0)
            self.assertIn("--preflight-only", args)

    def test_closed_original_tuple_and_complete_package_input_tree(self):
        self.validate(RepairAPI())
        for field, value in (("candidate_run", 7), ("artifact_id", 8), ("archive_sha", "e" * 64),
                             ("manifest", {**RETAINED_MANIFEST, "archiveSha256": "e" * 64})):
            with self.subTest(field=field), self.assertRaises(Refused):
                self.validate(RepairAPI(), **{field: value})
        for field, value in (("conclusion", "success"), ("status", "in_progress"), ("run_attempt", 2),
                             ("head_sha", "e" * 40), ("actor", {"login": "other"})):
            api = RepairAPI(); api.original[field] = value
            with self.subTest(field=field), self.assertRaises(Refused):
                self.validate(api)
        for path in ("Directory.Build.props", "src/FS.GG.Telemetry.Host/Program.fs",
                     "scripts/release_successor_journal.py", "scripts/release-successor-publish.py",
                     "docs/atlas/records/" + "a" * 64 + ".json"):
            api = RepairAPI()
            api.changed.append({"path": path, "mode": "100644", "type": "blob", "sha": "e" * 40})
            if path.startswith("docs/atlas/"):
                api.rows.append({**api.changed[-1], "sha": "f" * 40})
            with self.subTest(path=path), self.assertRaises(Refused):
                self.validate(api)
        api = RepairAPI(); api.truncated = True
        with self.assertRaisesRegex(Refused, "incomplete"):
            self.validate(api)

    def test_native_protection_and_canonical_destination_are_real_shared_predicates(self):
        # Reuse the existing provider fixture classes without executing that module's tests.
        import ast
        route = ordinary_route()
        namespace = dict(copy=copy, NotFound=NotFound, AUTHORITY="FS-GG/FS.GG.Coordination.Authority",
                         REF="refs/heads/fsgg/v2/journal/release/utel-host-rel-12",
                         reviewed_binding=route.authority_protection()[1])
        source = ast.parse((ROOT / "tests/release-successor-live/run.py").read_text())
        cls = next(node for node in source.body if isinstance(node, ast.ClassDef) and node.name == "AuthorityAPI")
        exec(compile(ast.Module(body=[cls], type_ignores=[]), "existing-AuthorityAPI", "exec"), namespace)
        Authority = namespace["AuthorityAPI"]
        api = Authority()
        logical = namespace["REF"]
        self.assertEqual(route.classify_journal_destination(api, logical), (True, "a" * 40))
        protection, binding = route.authority_protection()
        protection(api, binding, [logical])
        for control in ("mainWriter", "mainIntegrity", "legacyFence"):
            bad = Authority(); bad.controls[control]["enforcement"] = "disabled"
            with self.subTest(control=control), self.assertRaises(ValueError):
                protection(bad, binding, [logical])
        for mutate in (lambda a: setattr(a, "legacy", True), lambda a: setattr(a, "change_main_at", 2),
                       lambda a: a.override.update({f"repos/{namespace['AUTHORITY']}/git/trees/" + a.root: NotFound("missing object")}),
                       lambda a: a.override.update({f"repos/{namespace['AUTHORITY']}/git/trees/" + a.root: {"sha": a.root, "truncated": True, "tree": []}})):
            bad = Authority(); mutate(bad)
            with self.assertRaises((RuntimeError, ValueError)):
                route.classify_journal_destination(bad, logical)
        self.authority_fixture = Authority

    def test_actual_publisher_genesis_and_eight_effects_preserve_other_authority_state(self):
        self.test_native_protection_and_canonical_destination_are_real_shared_predicates()
        import ast
        import base64
        from release_successor_journal import ProtectedReleaseJournal, REPOSITORY, Refused as JournalRefused
        namespace = dict(os=os, pathlib=pathlib, subprocess=subprocess, tempfile=tempfile, base64=base64,
                         REPOSITORY=REPOSITORY)
        source = ast.parse((ROOT / "tests/release-successor-journal/run.py").read_text())
        cls = next(node for node in source.body if isinstance(node, ast.ClassDef) and node.name == "RealGit")
        exec(compile(ast.Module(body=[cls], type_ignores=[]), "existing-RealGit", "exec"), namespace)
        RealGit = namespace["RealGit"]
        spec = importlib.util.spec_from_file_location("host_publisher_actual_main", ROOT / "scripts/telemetry-host-successor-publish.py")
        publisher = importlib.util.module_from_spec(spec); spec.loader.exec_module(publisher)
        original_run = subprocess.run
        for mode in ("complete", "policy", "unknown"):
            interrupt = mode == "policy"
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                policy = self.authority_fixture()
                class Authority(RealGit):
                    def get(self, path):
                        if "/rulesets/" in path or "/rules/branches/" in path:
                            return policy.get(path)
                        if path == f"repos/{REPOSITORY}/git/ref/{publisher.REF.removeprefix('refs/')}":
                            raise NotFound(path)
                        value = super().get(path)
                        if path.endswith("/git/ref/heads/main"):
                            value["object"]["type"] = "commit"
                        return value
                    def post(self, path, body):
                        self.writes.append((path, copy.deepcopy(body)))
                        self.assert_not_legacy(path)
                        return super().post(path, body)
                    def assert_not_legacy(self, path):
                        if path.endswith("/git/refs"):
                            raise AssertionError("legacy namespace write attempted")
                    def patch(self, path, body):
                        self.writes.append((path, copy.deepcopy(body)))
                        assert path.endswith("/git/refs/heads/main")
                        return super().patch(path, body)
                authority = Authority(pathlib.Path(temporary) / "authority.git")
                authority.writes = []
                authority.sibling("state/releases/unrelated/retained.txt")
                unrelated = authority.command("show", "main:state/releases/unrelated/retained.txt")
                api = RepairAPI()
                base_get = api.get
                def get(path):
                    if "/actions/artifacts/" in path:
                        return {"id": FAILED_ORIGINAL["artifactId"], "digest": "sha256:" + FAILED_ORIGINAL["archiveSha256"]}
                    if path.endswith("/actions/runs/" + str(FAILED_ORIGINAL["candidateRunId"])):
                        return {"id": FAILED_ORIGINAL["candidateRunId"], "head_sha": api.source}
                    return base_get(path)
                api.get = get
                dispatched = []
                visible = set()
                class Provider:
                    def __init__(self, *args): pass
                    def observe(self, effect):
                        return Observation("matched", effect.target_digest) if effect.identity in visible else Observation("absent")
                    def dispatch(self, effect):
                        dispatched.append(effect.identity)
                        if mode == "unknown":
                            self.last_dispatch_diagnostic = {"category": "provider-refused", "request": {
                                "method": "POST", "path": "/repos/FS-GG/.github/git/refs", "status": 403,
                                "responseClass": "workflow-permission-denied", "rawBody": "SECRET-body"}}
                            return Dispatch("unknown")
                        visible.add(effect.identity)
                        return Dispatch("applied")
                original_advance = publisher.advance_effects
                def advance(*args):
                    if interrupt:
                        policy.controls["mainWriter"]["enforcement"] = "disabled"
                    return original_advance(*args)
                work = pathlib.Path(temporary) / "candidate"
                def run(command, **kwargs):
                    if command[0] == "git":
                        return original_run(command, **kwargs)
                    if command[0] == "gh":
                        kwargs["stdout"].write(b"original archive fixture")
                    else:
                        target = pathlib.Path(command[command.index("--output") + 1]); target.mkdir()
                        (target / "manifest.json").write_text(json.dumps(RETAINED_MANIFEST))
                    return SimpleNamespace(returncode=0)
                argv = ["publisher", "--publish", "--continue-failed-original", "--candidate-run-id", str(FAILED_ORIGINAL["candidateRunId"]),
                        "--candidate-artifact-id", str(FAILED_ORIGINAL["artifactId"]), "--candidate-archive-sha256", FAILED_ORIGINAL["archiveSha256"],
                        "--workdir", str(work)]
                env = {"GITHUB_SHA": api.head, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "999",
                       "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "FS-GG/.github",
                       "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1", "GH_TOKEN": "fixture",
                       "ORDINARY_LEDGER_TOKEN": "ledger", "NUGET_API_KEY": "fixture"}
                output = io.StringIO()
                with contextlib.redirect_stdout(output), patch.dict(os.environ, env), patch.object(sys, "argv", argv), \
                     patch.object(publisher, "GitHubAPI", side_effect=[api, authority]), \
                     patch.object(publisher.subprocess, "run", side_effect=run), \
                     patch.object(publisher.time, "sleep"), patch.object(publisher, "HostProvider", Provider), \
                     patch.object(publisher, "advance_effects", side_effect=advance), \
                     patch.object(publisher.time, "monotonic", side_effect=[0, 1, 2701] if mode == "unknown" else None, return_value=0):
                    self.assertEqual(publisher.main(), 0 if mode == "complete" else 1)
                journal = ProtectedReleaseJournal(authority, publisher.REF, main_directory=True)
                state = journal.read()
                self.assertEqual(state.generation, {"policy": 1, "unknown": 2, "complete": 17}[mode])
                self.assertEqual(dispatched, [] if interrupt else ["tag"] if mode == "unknown" else [effect.identity for effect in effects(RETAINED_MANIFEST)[1]])
                self.assertEqual(output.getvalue().count("Host successor dispatch diagnostic: "), 1 if mode == "unknown" else 0)
                if mode == "unknown":
                    self.assertIn('"responseClass":"workflow-permission-denied"', output.getvalue())
                    self.assertEqual(output.getvalue().count("Host successor effect state: waiting"), 2)
                    self.assertEqual(state.effects, {"tag": "intent"})
                self.assertNotIn("SECRET", output.getvalue())
                self.assertEqual(authority.command("show", "main:state/releases/unrelated/retained.txt"), unrelated)
                self.assertEqual(journal.validate_intent({"contentId": FAILED_ORIGINAL["contentId"], "sourceSha": api.source,
                    "version": "0.6.0", "candidateArchiveSha256": FAILED_ORIGINAL["archiveSha256"], "operator": "EHotwagner"}), None)

    def test_actual_publisher_preflight_and_effect_admission_refuse_policy_drift(self):
        self.test_native_protection_and_canonical_destination_are_real_shared_predicates()
        spec = importlib.util.spec_from_file_location("host_publisher_repair", ROOT / "scripts/telemetry-host-successor-publish.py")
        publisher = importlib.util.module_from_spec(spec); spec.loader.exec_module(publisher)
        route = ordinary_route()
        for profile, policy_bad, same_source, expected in ((False, False, False, 1), (False, False, True, 0), (True, False, False, 0), (True, True, False, 1)):
            api, authority = RepairAPI(), self.authority_fixture()
            if same_source:
                api.head = api.source
            if policy_bad:
                authority.controls["legacyFence"]["enforcement"] = "disabled"
            writes = []
            class Journal:
                def __init__(self, api, ref, *, main_directory=False):
                    self.main_directory = main_directory
                    writes.append(("construct", main_directory))
                def initialize(self, intent):
                    writes.append(("initialize", intent))
                def read(self):
                    raise AssertionError("preflight must not read absent journal")
            class Provider:
                def __init__(self, *args): pass
                def observe(self, effect): return Observation("absent")
            original_get = api.get
            def get(path):
                if "/actions/artifacts/" in path:
                    return {"id": FAILED_ORIGINAL["artifactId"], "digest": "sha256:" + FAILED_ORIGINAL["archiveSha256"]}
                if path.endswith("/actions/runs/" + str(FAILED_ORIGINAL["candidateRunId"])):
                    return {"id": FAILED_ORIGINAL["candidateRunId"], "head_sha": api.source}
                return original_get(path)
            api.get = get
            with tempfile.TemporaryDirectory() as temporary:
                work = pathlib.Path(temporary) / "candidate"
                def subprocess_run(command, **kwargs):
                    if command[0] == "gh":
                        kwargs["stdout"].write(b"original archive fixture")
                    else:
                        target = pathlib.Path(command[command.index("--output") + 1]); target.mkdir()
                        (target / "manifest.json").write_text(json.dumps(RETAINED_MANIFEST))
                    return SimpleNamespace(returncode=0)
                argv = [str(ROOT / "scripts/telemetry-host-successor-publish.py"), "--preflight-only",
                        "--candidate-run-id", str(FAILED_ORIGINAL["candidateRunId"]),
                        "--candidate-artifact-id", str(FAILED_ORIGINAL["artifactId"]),
                        "--candidate-archive-sha256", FAILED_ORIGINAL["archiveSha256"], "--workdir", str(work)]
                if profile: argv.append("--continue-failed-original")
                env = {"GITHUB_SHA": api.head, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "999",
                       "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "FS-GG/.github",
                       "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1", "GH_TOKEN": "fixture",
                       "ORDINARY_LEDGER_TOKEN": "ledger", "NUGET_API_KEY": ""}
                with patch.dict(os.environ, env), patch.object(sys, "argv", argv), \
                     patch.object(publisher, "GitHubAPI", side_effect=[api, authority]), \
                     patch.object(publisher.subprocess, "run", side_effect=subprocess_run), \
                     patch.object(publisher, "ProtectedReleaseJournal", Journal), \
                     patch.object(publisher, "HostProvider", Provider):
                    self.assertEqual(publisher.main(), expected)
                self.assertEqual(writes, [("construct", True)])
            host = HostAdmission(api, RETAINED_MANIFEST, api.head, 999, "EHotwagner", "refs/heads/main")
            guarded = route.ProtectedPublisherAdmission(host, authority, publisher.REF)
            content, ordered = effects(RETAINED_MANIFEST)
            if not policy_bad:
                self.assertTrue(guarded.authorize(content, "journal", "intent", content))
                authority.controls["mainIntegrity"]["enforcement"] = "disabled"
            for action in ("intent", "dispatch", "settle"):
                self.assertFalse(guarded.authorize(content, ordered[0].identity, action, ordered[0].request_digest))


if __name__ == "__main__":
    unittest.main()

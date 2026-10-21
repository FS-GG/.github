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
from telemetry_host_successor_admission import HostAdmission, ordinary_route
from telemetry_host_successor_execution import effects
from telemetry_host_successor_provider import HostProvider, NotFound


def manifest():
    return {
        "schema": "fsgg.telemetry.host-release/1", "packageId": "FS.GG.Telemetry.Host",
        "version": "0.8.0", "tag": "telemetry-host/v0.8.0", "sourceSha": "a" * 40,
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
                                 "<package><metadata><id>FS.GG.Telemetry.Host</id><version>0.8.0</version></metadata></package>")
                archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll", b"fixture")
            lock = root / "lock.json"
            lock.write_text("{}")
            assets = root / "assets"
            assets.mkdir()
            (assets / "app.js").write_text("fixture")
            prepared = module.build_manifest(SimpleNamespace(package=package, lock=lock, assets=assets,
                                             version="0.8.0", tag="telemetry-host/v0.8.0", source_sha="a" * 40))
            self.assertEqual((prepared["supportedStoreSchemaMin"], prepared["supportedStoreSchemaMax"]), (14, 14))
            self.assertEqual(prepared["version"], "0.8.0")
            # Renaming a retained older package cannot become a new genuine candidate.
            with zipfile.ZipFile(root / "renamed-old.nupkg", "w") as archive:
                archive.writestr("FS.GG.Telemetry.Host.nuspec", "<package><metadata><id>FS.GG.Telemetry.Host</id><version>0.6.0</version></metadata></package>")
                archive.writestr("tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll", b"older fixture")
            with self.assertRaisesRegex(ValueError, "package identity"):
                module.build_manifest(SimpleNamespace(package=root / "renamed-old.nupkg", lock=lock, assets=assets,
                    version="0.8.0", tag="telemetry-host/v0.8.0", source_sha="a" * 40))

    def test_current_profile_is_schema14_only_and_legacy_artifact_stays_unchanged(self):
        spec = importlib.util.spec_from_file_location("host_release_profile", ROOT / "scripts/telemetry-host-release.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertEqual(module.store_schema_profile("0.8.0"), (14, 14))
        self.assertEqual(module.store_schema_profile("0.7.0"), (14, 14))
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
            with self.assertRaisesRegex(Refused, "exact Host 0.8.0"):
                effects(old)
            previous = {**value, "version": "0.5.0", "tag": "telemetry-host/v0.5.0"}
            path.write_text(json.dumps(previous))
            self.assertEqual(module.load_manifest(path), previous)
            with self.assertRaisesRegex(Refused, "exact Host 0.8.0"):
                effects(previous)
            held = {**value, "version": "0.6.0", "tag": "telemetry-host/v0.6.0"}
            path.write_text(json.dumps(held))
            self.assertEqual(module.load_manifest(path), held)
            with self.assertRaisesRegex(Refused, "exact Host 0.8.0"):
                effects(held)
            published = {**value, "version": "0.7.0", "tag": "telemetry-host/v0.7.0"}
            path.write_text(json.dumps(published))
            self.assertEqual(module.load_manifest(path), published)
            with self.assertRaisesRegex(Refused, "exact Host 0.8.0"):
                effects(published)


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
            assert '(cd tests/FS.GG.Telemetry.Dashboard.Tests && npm ci --ignore-scripts && npm run test:browser -- --reporter=line)' in source
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
                           source.replace('npm run test:browser -- --reporter=line', 'echo browser-not-run'),
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

    @staticmethod
    def passive_snapshot():
        return {"schema": "fsgg.telemetry.private-dashboard/3", "unassignedUsage": {
            "schema": "fsgg.telemetry.unassigned-usage/1", "populationCoverage": "partial",
            "attribution": "unknown", "cost": "unknown", "counterEncoding": "decimal-int64",
            "limit": 256, "truncated": False, "observations": [{
                "ordinal": 1, "revision": "2", "capturedAt": "2026-10-18T12:00:00Z",
                "occurredAt": None, "receivedAt": "2026-10-18T12:00:01Z", "input": "20",
                "cachedInput": None, "cacheWriteInput": "3", "output": "2", "reasoning": None,
                "total": "22"}]}}

    def browser_result(self, snapshot, applied=2, rejected=0, expected="2", text_changes=None):
        # Execute the maintained assertion script with a page double, never a browser or network.
        texts = {
            "#health": f"0 pending receipts · {applied} applied receipts · {rejected} rejected receipts · consistent",
            "#unassigned-rows h3": "Observation 1 · revision 2",
            "#unassigned-rows .passive-total": "Observed total: 22",
            "#unassigned-rows": "Input 20 · cached input unknown · cache writes 3 · output 2 · reasoning unknown",
            "#unassigned-status": "1 displayed observations · partial population coverage · item attribution and billed cost: unknown. Counters are shown per observation without a combined total.",
            "body": "private fixture dashboard"}
        texts.update(text_changes or {})
        with tempfile.TemporaryDirectory() as temporary:
            mock = pathlib.Path(temporary) / "playwright.js"
            mock.write_text("const texts=" + json.dumps(texts) + ";const snapshot=" + json.dumps(snapshot) + ";\n" +
                "const control=selector=>({fill:async()=>{},click:async()=>{},waitFor:async()=>{},count:async()=>1,innerText:async()=>texts[selector]});\n"
                "const response={url:()=>\"https://fixture.invalid/private/dashboard/snapshot\",request:()=>({method:()=>\"POST\"}),status:()=>200,json:async()=>snapshot};\n"
                "const page={goto:async()=>({status:()=>200}),locator:control,getByRole:()=>({filter:()=>control(\"\"),...control(\"\")}),waitForResponse:async predicate=>{if(!predicate(response))throw Error(\"snapshot predicate rejected actual route\");return response;}};\n"
                "module.exports={chromium:{launch:async()=>({newPage:async()=>page,close:async()=>{}})}};\n")
            args = ["node", str(ROOT / "tests/standalone-telemetry-host-package/installed-browser.js"), str(mock),
                    "https://fixture.invalid", "fixture-principal", "fixture-key", "fixture-workspace", "fixture-item", "fixture-spki"]
            if expected is not None:
                args.append(expected)
            return subprocess.run(args, capture_output=True, text=True, timeout=10)

    def test_actual_browser_receipt_check_preserves_old_profile_and_exact_correction_count(self):
        for applied, rejected, expected, success in [
            (1, 0, None, True), (2, 0, "2", True),
            (1, 0, "2", False), (2, 0, None, False),
            (12, 0, "2", False), (2, 1, "2", False),
            (2, 2, "2", False), (0, 0, "2", False),
            (2, 0, "0", False), (2, 0, "invalid", False)]:
            with self.subTest(applied=applied, rejected=rejected, expected=expected):
                result = self.browser_result(self.passive_snapshot(), applied, rejected, expected)
                self.assertEqual(success, result.returncode == 0, result.stderr)

    def test_installed_browser_requires_corrected_private_projection_and_rendered_facts(self):
        good = self.passive_snapshot()
        self.assertEqual(0, self.browser_result(good).returncode)
        mutations = [
            lambda value: value.update(schema="fsgg.telemetry.private-dashboard/2"),
            lambda value: value["unassignedUsage"].update(cost="0"),
            lambda value: value["unassignedUsage"].update(populationCoverage="complete"),
            lambda value: value["unassignedUsage"]["observations"][0].update(revision="1"),
            lambda value: value["unassignedUsage"]["observations"][0].update(total=22),
            lambda value: value["unassignedUsage"]["observations"][0].update(cachedInput="0"),
            lambda value: value["unassignedUsage"]["observations"][0].update(threadId="private-thread"),
            lambda value: value["unassignedUsage"]["observations"].append(copy.deepcopy(value["unassignedUsage"]["observations"][0])),
            lambda value: value.update(leaked="package-response")]
        for mutation in mutations:
            value = copy.deepcopy(good)
            mutation(value)
            with self.subTest(snapshot=value):
                self.assertNotEqual(0, self.browser_result(value).returncode)
        for selector, text in [("#unassigned-rows .passive-total", "Observed total: 12"),
                               ("#unassigned-status", "Complete usage and known cost"),
                               ("body", "package-response")]:
            with self.subTest(selector=selector):
                self.assertNotEqual(0, self.browser_result(good, text_changes={selector: text}).returncode)


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
        import xml.etree.ElementTree as ET
        self.assertEqual(ET.parse(ROOT / "src/FS.GG.Telemetry.Host/FS.GG.Telemetry.Host.fsproj").findtext(".//Version"), "0.8.0")
        publisher = (ROOT / "scripts" / "telemetry-host-successor-publish.py").read_text()
        candidate = (ROOT / ".github" / "workflows" / "release-telemetry-host-successor-candidate.yml").read_text()
        self.assertIn('REF = "refs/heads/fsgg/v2/journal/release/utel-host-rel-14"', publisher)
        self.assertNotIn("utel-host-rel-10", publisher)
        self.assertIn('"version": "0.8.0"', publisher)
        self.assertIn("--version 0.8.0 --tag telemetry-host/v0.8.0", candidate)
        self.assertIn("FS.GG.Telemetry.Host.0.8.0.nupkg", candidate)
        self.assertIn("telemetry-host-080-candidate", candidate)
        self.assertIn("telemetry-host-080-publisher", (ROOT / ".github/workflows/release-telemetry-host-successor-publish.yml").read_text())

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
                        "version": "0.8.0", "archiveSha256": package_digest, "passed": 1, "failed": 0,
                        "productionStorage": "eligible-linux-x64", "syntheticTls": True, "passiveReceiptQualified": True}
            def write_archive(evidence):
                with zipfile.ZipFile(archive, "w") as zipped:
                    zipped.writestr("manifest.json", json.dumps({**manifest(), "archiveSha256": package_digest}))
                    zipped.writestr("package-evidence.json", json.dumps(evidence))
                    zipped.writestr("FS.GG.Telemetry.Host.0.8.0.nupkg", "fixture")
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
                self.assertEqual(verifier.verify(artifact, run, archive, root / "good", source)["version"], "0.8.0")
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
                           ("version", "0.6.0"), ("tag", "telemetry-host/v0.6.0"),
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
                if path.endswith("/git/ref/tags/telemetry-host/v0.8.0") and self.tag:
                    return {"object": {"sha": self.tag}}
                if path.endswith("/releases/tags/telemetry-host/v0.8.0") and self.release and not self.release["draft"]:
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
            package = root / "FS.GG.Telemetry.Host.0.8.0.nupkg"
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
                if path.endswith("/releases/tags/telemetry-host/v0.8.0"):
                    raise NotFound(path)
                if path.endswith("/releases?per_page=100&page=1"):
                    return [{"id": 1, "tag_name": "telemetry-host/v0.8.0", "draft": True}]
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
            status = "in_progress"
            def get(self, path):
                if path.endswith("/.github"):
                    return {"id": 1269292704, "full_name": "FS-GG/.github"}
                if "/actions/runs/123" in path:
                    return {"repository": {"id": 1269292704},
                            "path": ".github/workflows/release-telemetry-host-successor-publish.yml",
                            "event": "workflow_dispatch", "head_branch": "main", "head_sha": "d" * 40,
                            "run_attempt": 1, "actor": {"login": self.actor}, "status": self.status}
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


        api.actor = "EHotwagner"; api.status = "completed"
        for action in ("intent", "dispatch", "settle"):
            self.assertFalse(admission.authorize(self.content_id, effect.identity, action, effect.request_digest))

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


# Synthetic current profile metadata; this is not an authentic new candidate.
CURRENT_SOURCE = "d" * 40
CURRENT_RUN = 700
CURRENT_ARTIFACT = 701
CURRENT_ARCHIVE = "e" * 64
OLD_RUN = 39135954294
OLD_ARTIFACT = 12140420062
OLD_SOURCE = "54335971452d2dfadb04e7704774ae2e48718e54"
CURRENT_MANIFEST = {**manifest(), "sourceSha": CURRENT_SOURCE}


class CurrentAPI:
    def __init__(self):
        self.head = CURRENT_SOURCE
        self.source = CURRENT_SOURCE
        self.status = "in_progress"
        self.reads = []
    def get(self, path):
        self.reads.append(path)
        if path.endswith("/.github"):
            return {"id": 1269292704, "full_name": "FS-GG/.github"}
        if path.endswith("/git/ref/heads/main"):
            return {"object": {"sha": self.head}}
        if path.endswith("/actions/runs/999"):
            return {"id": 999, "repository": {"id": 1269292704},
                "path": ".github/workflows/release-telemetry-host-successor-publish.yml", "event": "workflow_dispatch",
                "head_branch": "main", "head_sha": self.head, "run_attempt": 1,
                "actor": {"login": "EHotwagner"}, "status": self.status}
        if path.endswith("/actions/runs/" + str(CURRENT_RUN)):
            return {"id": CURRENT_RUN, "head_sha": self.source}
        if path.endswith("/actions/artifacts/" + str(CURRENT_ARTIFACT)):
            return {"id": CURRENT_ARTIFACT, "digest": "sha256:" + CURRENT_ARCHIVE}
        if path.endswith("/actions/runs/" + str(OLD_RUN)):
            return {"id": OLD_RUN, "head_sha": OLD_SOURCE}
        if path.endswith("/actions/artifacts/" + str(OLD_ARTIFACT)):
            return {"id": OLD_ARTIFACT, "digest": "sha256:" + CURRENT_ARCHIVE}
        raise AssertionError(path)


class CurrentHostPublisherTests(unittest.TestCase):
    def publisher(self):
        spec = importlib.util.spec_from_file_location("current_host_publisher", ROOT / "scripts/telemetry-host-successor-publish.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        return module

    def authority_fixture(self):
        # Existing inert provider fixture, with the independently selected new logical coordinate.
        import ast
        route = ordinary_route()
        namespace = dict(copy=copy, NotFound=NotFound, AUTHORITY="FS-GG/FS.GG.Coordination.Authority",
                         REF="refs/heads/fsgg/v2/journal/release/utel-host-rel-14",
                         reviewed_binding=route.authority_protection()[1])
        source = ast.parse((ROOT / "tests/release-successor-live/run.py").read_text())
        cls = next(node for node in source.body if isinstance(node, ast.ClassDef) and node.name == "AuthorityAPI")
        exec(compile(ast.Module(body=[cls], type_ignores=[]), "existing-AuthorityAPI", "exec"), namespace)
        return namespace["AuthorityAPI"]

    def invoke(self, publisher, api, authority, root, provider, *, continuation=False, old=False, mode="--preflight-only", journal=None):
        work = root / "candidate"
        def run(command, **kwargs):
            if command[0] == "gh":
                kwargs["stdout"].write(b"synthetic current archive")
            else:
                target = pathlib.Path(command[command.index("--output") + 1]); target.mkdir()
                (target / "manifest.json").write_text(json.dumps(CURRENT_MANIFEST))
            return SimpleNamespace(returncode=0)
        argv = ["publisher", mode, "--candidate-run-id", str(OLD_RUN if old else CURRENT_RUN),
                "--candidate-artifact-id", str(OLD_ARTIFACT if old else CURRENT_ARTIFACT),
                "--candidate-archive-sha256", CURRENT_ARCHIVE, "--workdir", str(work)]
        if continuation: argv.append("--continue-failed-original")
        env = {"GITHUB_SHA": api.head, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "999",
               "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REPOSITORY": "FS-GG/.github",
               "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1", "GH_TOKEN": "fixture",
               "ORDINARY_LEDGER_TOKEN": "ledger", "NUGET_API_KEY": ""}
        with patch.dict(os.environ, env), patch.object(sys, "argv", argv), \
             patch.object(publisher, "GitHubAPI", side_effect=[api, authority]), \
             patch.object(publisher.subprocess, "run", side_effect=run), patch.object(publisher, "HostProvider", provider), \
             patch.object(publisher, "ProtectedReleaseJournal", journal):
            return publisher.main()

    def test_current_source_only_refuses_retained_candidate_continuation_and_terminal_caller(self):
        for case in ("current", "old-candidate", "old-flag", "stale-source", "policy", "terminal-actor"):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temporary:
                api = CurrentAPI(); authority = self.authority_fixture()(); publisher = self.publisher()
                if case == "stale-source": api.source = "a" * 40
                if case == "policy": authority.controls["legacyFence"]["enforcement"] = "disabled"
                if case == "terminal-actor": api.status = "completed"
                writes = []
                class Journal:
                    def __init__(self, api, ref, *, main_directory=False):
                        writes.append(("construct", ref, main_directory))
                    def initialize(self, intent): writes.append(("initialize", intent))
                class Provider:
                    def __init__(self, *args): pass
                    def observe(self, effect): return Observation("absent")
                root = pathlib.Path(temporary)
                result = self.invoke(publisher, api, authority, root, Provider,
                                     continuation=case == "old-flag", old=case == "old-candidate", journal=Journal)
                self.assertEqual(result, 0 if case == "current" else 1)
                self.assertFalse(any(row[0] == "initialize" for row in writes))
                self.assertTrue(all(row[1:] == ("refs/heads/fsgg/v2/journal/release/utel-host-rel-14", True) for row in writes))
                if case == "old-flag":
                    self.assertEqual(api.reads, [])
                    self.assertFalse((root / "candidate").exists())

    def test_late_old_tag_cannot_satisfy_the_new_profile(self):
        class API:
            def __init__(self): self.paths = []
            def get(self, path):
                self.paths.append(path)
                if path.endswith("/git/ref/tags/telemetry-host/v0.6.0"):
                    return {"object": {"sha": OLD_SOURCE}}
                raise NotFound(path)
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "manifest.json"; path.write_text(json.dumps(CURRENT_MANIFEST))
            api = API(); provider = HostProvider(api, path, "fixture", "fixture")
            self.assertEqual(provider.observe(effects(CURRENT_MANIFEST)[1][0]).state, "absent")
            self.assertEqual(api.paths, ["repos/FS-GG/.github/git/ref/tags/telemetry-host/v0.8.0"])

    def test_actual_new_journal_preserves_old12_and_unknown_never_resends(self):
        import ast
        import base64
        from release_successor_journal import ProtectedReleaseJournal, REPOSITORY
        namespace = dict(os=os, pathlib=pathlib, subprocess=subprocess, tempfile=tempfile, base64=base64, REPOSITORY=REPOSITORY)
        source = ast.parse((ROOT / "tests/release-successor-journal/run.py").read_text())
        cls = next(node for node in source.body if isinstance(node, ast.ClassDef) and node.name == "RealGit")
        exec(compile(ast.Module(body=[cls], type_ignores=[]), "existing-RealGit", "exec"), namespace)
        RealGit = namespace["RealGit"]
        original_run = subprocess.run
        for mode in ("complete", "policy", "unknown"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                policy = self.authority_fixture()(); publisher = self.publisher()
                class Authority(RealGit):
                    def get(self, path):
                        if "/rulesets/" in path or "/rules/branches/" in path: return policy.get(path)
                        if path == f"repos/{REPOSITORY}/git/ref/{publisher.REF.removeprefix('refs/')}": raise NotFound(path)
                        value = super().get(path)
                        if path.endswith("/git/ref/heads/main"): value["object"]["type"] = "commit"
                        return value
                    def post(self, path, body):
                        assert not path.endswith("/git/refs"), "no legacy creation"
                        return super().post(path, body)
                    def patch(self, path, body):
                        assert path.endswith("/git/refs/heads/main")
                        return super().patch(path, body)
                authority = Authority(pathlib.Path(temporary) / "authority.git")
                # Opaque synthetic old custody must never be interpreted, reset or overwritten by profile13.
                old_path = "state/releases/utel-host-rel-12/release-state.json"
                authority.sibling(old_path)
                old_bytes = authority.command("show", "main:" + old_path)
                api = CurrentAPI(); dispatched = []; visible = set()
                class Provider:
                    def __init__(self, *args): pass
                    def observe(self, effect): return Observation("matched", effect.target_digest) if effect.identity in visible else Observation("absent")
                    def dispatch(self, effect):
                        dispatched.append(effect.identity)
                        if mode == "unknown":
                            self.last_dispatch_diagnostic = {"category": "provider-refused", "request": {"method": "POST", "path": "/repos/FS-GG/.github/git/refs", "status": 403,
                                "responseClass": "workflow-permission-denied", "rawBody": "SECRET-body"}}
                            return Dispatch("unknown")
                        visible.add(effect.identity); return Dispatch("applied")
                original_advance = publisher.advance_effects
                def advance(*args):
                    if mode == "policy": policy.controls["mainWriter"]["enforcement"] = "disabled"
                    return original_advance(*args)
                root = pathlib.Path(temporary); work = root / "candidate"
                def run(command, **kwargs):
                    if command[0] == "git": return original_run(command, **kwargs)
                    if command[0] == "gh": kwargs["stdout"].write(b"synthetic current archive")
                    else:
                        target = pathlib.Path(command[command.index("--output") + 1]); target.mkdir()
                        (target / "manifest.json").write_text(json.dumps(CURRENT_MANIFEST))
                    return SimpleNamespace(returncode=0)
                argv = ["publisher", "--publish", "--candidate-run-id", str(CURRENT_RUN), "--candidate-artifact-id", str(CURRENT_ARTIFACT),
                        "--candidate-archive-sha256", CURRENT_ARCHIVE, "--workdir", str(work)]
                env = {"GITHUB_SHA": api.head, "GITHUB_ACTOR": "EHotwagner", "GITHUB_RUN_ID": "999", "GITHUB_EVENT_NAME": "workflow_dispatch",
                       "GITHUB_REPOSITORY": "FS-GG/.github", "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1",
                       "GH_TOKEN": "fixture", "ORDINARY_LEDGER_TOKEN": "ledger", "NUGET_API_KEY": "fixture"}
                output = io.StringIO()
                with contextlib.redirect_stdout(output), patch.dict(os.environ, env), patch.object(sys, "argv", argv), \
                     patch.object(publisher, "GitHubAPI", side_effect=[api, authority]), patch.object(publisher.subprocess, "run", side_effect=run), \
                     patch.object(publisher.time, "sleep"), patch.object(publisher, "HostProvider", Provider), \
                     patch.object(publisher, "advance_effects", side_effect=advance), \
                     patch.object(publisher.time, "monotonic", side_effect=[0, 1, 2701] if mode == "unknown" else None, return_value=0):
                    self.assertEqual(publisher.main(), 0 if mode == "complete" else 1)
                journal = ProtectedReleaseJournal(authority, publisher.REF, main_directory=True); state = journal.read()
                self.assertEqual(state.generation, {"policy": 1, "unknown": 2, "complete": 17}[mode])
                self.assertEqual(dispatched, [] if mode == "policy" else ["tag"] if mode == "unknown" else [e.identity for e in effects(CURRENT_MANIFEST)[1]])
                self.assertEqual(authority.command("show", "main:" + old_path), old_bytes)
                self.assertEqual(output.getvalue().count("Host successor dispatch diagnostic: "), 1 if mode == "unknown" else 0)
                if mode == "unknown":
                    self.assertEqual(state.effects, {"tag": "intent"})
                    self.assertEqual(output.getvalue().count("Host successor effect state: waiting"), 2)
                self.assertNotIn("SECRET", output.getvalue())
                journal.validate_intent({"contentId": effects(CURRENT_MANIFEST)[0], "sourceSha": CURRENT_SOURCE, "version": "0.8.0",
                                        "candidateArchiveSha256": CURRENT_ARCHIVE, "operator": "EHotwagner"})

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



if __name__ == "__main__":
    unittest.main()

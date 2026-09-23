#!/usr/bin/env python3
"""Refusal and recovery controls for independent Host release effects."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from release_successor_execution import Dispatch, JournalState, Observation, Refused, advance_effects
from telemetry_host_successor_admission import HostAdmission
from telemetry_host_successor_execution import effects
from telemetry_host_successor_provider import HostProvider, NotFound


def manifest():
    return {
        "schema": "fsgg.telemetry.host-release/1", "packageId": "FS.GG.Telemetry.Host",
        "version": "0.1.6", "tag": "telemetry-host/v0.1.6", "sourceSha": "a" * 40,
        "archiveSha256": "b" * 64, "producerPayloadSha256": "sha256:" + "c" * 64,
    }


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
            with zipfile.ZipFile(archive, "w") as zipped:
                zipped.writestr("manifest.json", json.dumps(manifest()))
                zipped.writestr("package-evidence.json", "{}")
                zipped.writestr("FS.GG.Telemetry.Host.0.1.6.nupkg", "fixture")
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
                self.assertEqual(verifier.verify(artifact, run, archive, root / "good", source)["version"], "0.1.6")
            altered = {**run, "run_attempt": 2}
            with self.assertRaisesRegex(ValueError, "first-attempt"):
                verifier.verify(artifact, altered, archive, root / "rerun", source)
            altered_artifact = {**artifact, "digest": "sha256:" + "0" * 64}
            with self.assertRaisesRegex(ValueError, "digest differs"):
                verifier.verify(altered_artifact, run, archive, root / "tampered", source)

    def test_prior_release_identity_and_wrong_payload_are_refused(self):
        for key, value in (("version", "0.1.3"), ("tag", "telemetry-host/v0.1.3"),
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
                if path.endswith("/git/ref/tags/telemetry-host/v0.1.6") and self.tag:
                    return {"object": {"sha": self.tag}}
                if path.endswith("/releases/tags/telemetry-host/v0.1.6") and self.release and not self.release["draft"]:
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
            package = root / "FS.GG.Telemetry.Host.0.1.6.nupkg"
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
                if path.endswith("/releases/tags/telemetry-host/v0.1.6"):
                    raise NotFound(path)
                if path.endswith("/releases?per_page=100&page=1"):
                    return [{"id": 1, "tag_name": "telemetry-host/v0.1.6", "draft": True}]
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


if __name__ == "__main__":
    unittest.main()

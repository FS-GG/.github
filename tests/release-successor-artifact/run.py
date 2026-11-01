#!/usr/bin/env python3
"""Offline identity and tamper controls for retained candidate archives."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODULE = ROOT / "scripts" / "release-successor-artifact.py"
spec = importlib.util.spec_from_file_location("release_successor_artifact", MODULE)
assert spec and spec.loader
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
SOURCE = "a" * 40
PREDECESSOR = "b" * 40
PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")


def package(path: pathlib.Path, package_id: str, version="0.91.3") -> None:
    nuspec = (
        f'<package><metadata><id>{package_id}</id><version>{version}</version>'
        f'<releaseNotes>{version} fixture</releaseNotes><repository type="git" commit="{SOURCE}" />'
        "</metadata></package>"
    )
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(package_id + ".nuspec", nuspec)
        archive.writestr("payload.txt", package_id)


class CandidateArtifactTests(unittest.TestCase):
    VERSION = "0.91.3"
    PREVIOUS = "0.91.2"
    SELECTED = False
    PREVIOUS_SOURCE = PREDECESSOR
    PREVIOUS_CONTENT = "sha256:" + "c" * 64
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="release-successor-artifact-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.candidate = self.root / "candidate"
        self.candidate.mkdir()
        for package_id in PACKAGES:
            package(self.candidate / f"{package_id}.{self.VERSION}.nupkg", package_id, self.VERSION)
        predecessor = {
            "contentId": self.PREVIOUS_CONTENT,
            "version": self.PREVIOUS,
            "sourceSha": self.PREVIOUS_SOURCE,
            "promotedAt": "2026-09-19T00:00:00Z",
        }
        (self.candidate / "previous-stable-channel.json").write_text(json.dumps(predecessor))
        package_sha = hashlib.sha256((self.candidate / f"FS.GG.Coord.Cli.{self.VERSION}.nupkg").read_bytes()).hexdigest()
        qualification = {
            "schema": "fsgg.telemetry.standalone-qualification/2",
            "qualified": True,
            "binding": {"sourceSha": SOURCE, "packageSha256": package_sha, "sourceBinding": "prepared-for-release-manifest"},
            "checks": {name: True for name in (
                "startupP95Milliseconds", "idleRssPeakBytes", "coldCliSubmissionP95Milliseconds", "warmInProcessAdmissionP95Milliseconds"
            )},
            "limits": {"coldCliSubmissionP95Milliseconds": 1000, "warmInProcessAdmissionP95Milliseconds": 100},
            "packagedStore": {"schema": "fsgg.telemetry.packaged-store-performance/1", "qualified": True, "assessment": "approved-local-durable"},
            "claims": {"processCrashAndFilesystemApi": True, "physicalPowerLoss": False, "mainInstalled": False, "publicRelease": False},
        }
        (self.candidate / "standalone-telemetry-runtime-evidence.json").write_text(json.dumps(qualification))
        (self.candidate / "standalone-telemetry-evidence.json").write_text("{}")
        assets = self.root / "assets"
        assets.mkdir()
        (assets / "index.html").write_text("<!doctype html>")
        subprocess.run([
            sys.executable, str(ROOT / "scripts" / "release-saga.py"), "prepare",
            "--release-id", f"github:{self.VERSION}", "--version", self.VERSION, "--source-sha", SOURCE,
            "--source-tree", "d" * 40, "--policy-version", "release-successor/1",
            "--previous-channel", str(self.candidate / "previous-stable-channel.json"),
            "--artifact-dir", str(self.candidate),
            *(arg for package_id in PACKAGES for arg in ("--expected-package", package_id)),
            "--dashboard-assets", str(assets),
            "--standalone-qualification", str(self.candidate / "standalone-telemetry-runtime-evidence.json"),
            "--output", str(self.candidate / "release-manifest.json"),
        ], check=True, capture_output=True)
        self.archive = self.root / "candidate.zip"
        self.repack()
        self.run = {
            "id": 123, "path": gate.WORKFLOW, "head_sha": SOURCE,
            "head_branch": "main", "event": "workflow_dispatch", "conclusion": "success", "run_attempt": 1,
        }
        self.artifact = {
            "id": 456, "name": f"release-successor-candidate-{SOURCE}-123", "expired": False,
            "workflow_run": {"id": 123, "head_sha": SOURCE, "head_branch": "main",
                             "repository_id": gate.REPOSITORY_ID, "head_repository_id": gate.REPOSITORY_ID},
            "digest": "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest(),
        }
        self.output = self.root / "extracted"

    def repack(self):
        with zipfile.ZipFile(self.archive, "w") as archive:
            for path in sorted(self.candidate.iterdir()):
                archive.write(path, path.name)

    def verify(self):
        return gate.extract_verified(self.artifact, self.run, self.archive, self.output, SOURCE, suspended_0103_successor=self.SELECTED)

    def test_exact_successful_run_and_archive_extract(self):
        result = self.verify()
        self.assertEqual(result["sourceSha"], SOURCE)
        self.assertEqual(len(list(self.output.iterdir())), 7)

    def test_wrong_zip_digest_or_run_identity_refuses_without_output(self):
        self.artifact["digest"] = "sha256:" + "0" * 64
        with self.assertRaisesRegex(ValueError, "digest mismatch"):
            self.verify()
        self.artifact["digest"] = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.run["path"] = ".github/workflows/retired-release.yml"
        with self.assertRaisesRegex(ValueError, "successful first-attempt exact-main"):
            self.verify()
        self.assertFalse(self.output.exists())

    def test_stale_source_or_different_repository_refuses(self):
        self.run["head_sha"] = "f" * 40
        with self.assertRaises(ValueError):
            self.verify()
        self.run["head_sha"] = SOURCE
        self.artifact["workflow_run"]["repository_id"] = 1
        with self.assertRaises(ValueError):
            self.verify()

    def test_rerun_artifact_without_attempt_binding_refuses(self):
        self.run["run_attempt"] = 2
        with self.assertRaisesRegex(ValueError, "first-attempt"):
            self.verify()
        self.assertFalse(self.output.exists())

    def test_changed_qualification_refuses_after_verified_outer_digest(self):
        (self.candidate / "standalone-telemetry-runtime-evidence.json").write_text("{}")
        self.repack()
        self.artifact["digest"] = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        with self.assertRaises(subprocess.CalledProcessError):
            self.verify()
        self.assertFalse(self.output.exists())

    def test_manifest_path_cannot_escape_retained_archive(self):
        path = self.candidate / "release-manifest.json"
        manifest = json.loads(path.read_text())
        manifest["descriptor"]["packages"][0]["artifact"]["path"] = "../outside.nupkg"
        path.write_text(json.dumps(manifest))
        self.repack()
        self.artifact["digest"] = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        with self.assertRaisesRegex(ValueError, "outside the retained archive"):
            self.verify()
        self.assertFalse(self.output.exists())

    def test_extra_or_traversal_member_refuses(self):
        with zipfile.ZipFile(self.archive, "a") as archive:
            archive.writestr("../outside", "bad")
        self.artifact["digest"] = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
        with self.assertRaisesRegex(ValueError, "unsafe member count"):
            self.verify()
        self.assertFalse(self.output.exists())


class LiteralSuccessorArtifactTests(CandidateArtifactTests):
    VERSION = "0.104.0"
    PREVIOUS = "0.102.0"
    SELECTED = True

    def test_literal_selection_cannot_be_inferred_from_archive_or_wrong_pair(self):
        with self.assertRaisesRegex(ValueError, "closed literal successor"):
            gate.extract_verified(self.artifact,self.run,self.archive,self.output,SOURCE)
        self.run["path"] = ".github/workflows/release-improvement-candidate.yml"
        with self.assertRaisesRegex(ValueError, "exact-main"):
            self.verify()
        self.run["path"] = gate.WORKFLOW
        self.artifact["name"] = f"release-improvement-candidate-{SOURCE}-123"
        with self.assertRaisesRegex(ValueError, "artifact identity"):
            self.verify()
        self.assertFalse(self.output.exists())

    def test_selected_literal_refuses_changed_version_or_predecessor(self):
        path = self.candidate / "release-manifest.json"
        original = path.read_bytes()
        for field,value in (("version","0.105.0"),("previousStableVersion","0.103.0")):
            row = json.loads(original); row["descriptor"][field] = value
            path.write_text(json.dumps(row)); self.repack()
            self.artifact["digest"] = "sha256:" + hashlib.sha256(self.archive.read_bytes()).hexdigest()
            with self.subTest(field=field), self.assertRaises(ValueError): self.verify()
        self.assertFalse(self.output.exists())


def _below(version):
    major, minor, patch = (int(part) for part in version.split("."))
    return f"{major}.{minor}.{patch - 1}" if patch else f"{major}.{minor - 1}.0" if minor else f"{major - 1}.0.0"


class CurrentReleaseArtifactTests(CandidateArtifactTests):
    # Derived (#5156): the version bump and a synthetic promoted predecessor below it, never a pin. The
    # publisher binds the real predecessor from its promoted release and tag after this reader passes.
    VERSION = gate.CURRENT_RELEASE["version"]
    PREVIOUS = _below(VERSION)
    PREVIOUS_SOURCE = "d" * 40
    PREVIOUS_CONTENT = "sha256:" + "d" * 64

    def test_current_predecessor_identity_is_checked_by_the_actual_artifact_reader(self):
        path = self.candidate / 'previous-stable-channel.json'
        original = path.read_bytes()
        for field, value in (('sourceSha', 'e' * 39), ('version', self.VERSION), ('version', '9.0.0'),
                             ('version', '0.114'), ('contentId', 'sha256:' + 'e' * 63),
                             ('promotedAt', '2026-09-19')):
            with self.subTest(field=field):
                predecessor = json.loads(original)
                predecessor[field] = value
                path.write_text(json.dumps(predecessor))
                self.repack()
                self.artifact['digest'] = 'sha256:' + hashlib.sha256(self.archive.read_bytes()).hexdigest()
                with self.assertRaisesRegex(ValueError, 'current candidate predecessor identity'):
                    self.verify()
                self.assertFalse(self.output.exists())



if __name__ == "__main__":
    unittest.main()

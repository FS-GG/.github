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


def package(path: pathlib.Path, package_id: str) -> None:
    nuspec = (
        f'<package><metadata><id>{package_id}</id><version>0.91.2</version>'
        f'<releaseNotes>0.91.2 fixture</releaseNotes><repository type="git" commit="{SOURCE}" />'
        "</metadata></package>"
    )
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(package_id + ".nuspec", nuspec)
        archive.writestr("payload.txt", package_id)


class CandidateArtifactTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="release-successor-artifact-test-")
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.candidate = self.root / "candidate"
        self.candidate.mkdir()
        for package_id in PACKAGES:
            package(self.candidate / f"{package_id}.0.91.2.nupkg", package_id)
        predecessor = {
            "contentId": "sha256:" + "c" * 64,
            "version": "0.91.1",
            "sourceSha": PREDECESSOR,
            "promotedAt": "2026-09-19T00:00:00Z",
        }
        (self.candidate / "previous-stable-channel.json").write_text(json.dumps(predecessor))
        package_sha = hashlib.sha256((self.candidate / "FS.GG.Coord.Cli.0.91.2.nupkg").read_bytes()).hexdigest()
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
            "--release-id", "github:0.91.2", "--version", "0.91.2", "--source-sha", SOURCE,
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
        return gate.extract_verified(self.artifact, self.run, self.archive, self.output, SOURCE)

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


if __name__ == "__main__":
    unittest.main()

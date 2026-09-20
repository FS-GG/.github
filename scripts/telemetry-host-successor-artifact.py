#!/usr/bin/env python3
"""Independently bind and extract one read-only Host candidate Actions artifact."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import zipfile

REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-telemetry-host-successor-candidate.yml"
VERSION = "0.1.3"
PACKAGE = f"FS.GG.Telemetry.Host.{VERSION}.nupkg"
MEMBERS = {PACKAGE, "manifest.json", "package-evidence.json"}


def require(ok: bool, detail: str) -> None:
    if not ok:
        raise ValueError(detail)


def verify(artifact: dict, run: dict, archive_path: pathlib.Path, output: pathlib.Path, source_sha: str) -> dict:
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source_sha)), "invalid source SHA")
    run_id = run.get("id")
    require(isinstance(run_id, int) and run_id > 0, "invalid run ID")
    require(
        run.get("path") == WORKFLOW
        and run.get("head_sha") == source_sha
        and run.get("head_branch") == "main"
        and run.get("event") == "workflow_dispatch"
        and run.get("conclusion") == "success"
        and run.get("run_attempt") == 1
        and run.get("repository", {}).get("id") == REPOSITORY_ID,
        "candidate run is not the exact successful first-attempt main dispatch",
    )
    binding = artifact.get("workflow_run") or {}
    require(
        isinstance(artifact.get("id"), int)
        and artifact["id"] > 0
        and artifact.get("name") == f"telemetry-host-successor-candidate-{source_sha}-{run_id}"
        and artifact.get("expired") is False
        and binding.get("id") == run_id
        and binding.get("head_sha") == source_sha
        and binding.get("head_branch") == "main"
        and binding.get("repository_id") == REPOSITORY_ID
        and binding.get("head_repository_id") == REPOSITORY_ID,
        "candidate artifact does not bind the run",
    )
    expected_digest = artifact.get("digest", "")
    require(bool(re.fullmatch(r"sha256:[0-9a-f]{64}", expected_digest)), "candidate archive digest absent")
    with archive_path.open("rb") as stream:
        actual_digest = hashlib.file_digest(stream, "sha256").hexdigest()
    require(expected_digest == "sha256:" + actual_digest, "candidate archive digest differs")
    require(not output.exists(), "candidate extraction target already exists")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        names = [member.filename for member in archive.infolist()]
        require(len(names) == len(MEMBERS) and set(names) == MEMBERS, "candidate archive members differ")
        for member in archive.infolist():
            mode = (member.external_attr >> 16) & 0xFFFF
            require(not member.is_dir() and not stat.S_ISLNK(mode) and member.file_size <= 250_000_000,
                    "unsafe candidate archive member")
        manifest = json.loads(archive.read("manifest.json"))
        require(manifest.get("sourceSha") == source_sha and manifest.get("version") == VERSION,
                "Host manifest source or version differs")
        with tempfile.TemporaryDirectory(prefix="host-candidate-", dir=output.parent) as temporary:
            work = pathlib.Path(temporary)
            for name in names:
                with archive.open(name) as source, (work / name).open("xb") as target:
                    shutil.copyfileobj(source, target)
            checker = pathlib.Path(__file__).with_name("telemetry-host-release.py")
            subprocess.run([sys.executable, str(checker), "verify", "--manifest", str(work / "manifest.json"),
                            "--package", str(work / PACKAGE)], check=True)
            shutil.move(work, output)
    return manifest


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("artifact-json", "run-json", "archive", "output", "source-sha"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        verify(json.loads(pathlib.Path(args.artifact_json).read_text()),
               json.loads(pathlib.Path(args.run_json).read_text()), pathlib.Path(args.archive),
               pathlib.Path(args.output), args.source_sha)
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f"Host candidate refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

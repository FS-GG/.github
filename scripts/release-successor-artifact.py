#!/usr/bin/env python3
"""Bind and safely extract one unpromoted Actions candidate for a later admitted writer."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import zipfile

REPOSITORY_ID = 1269292704  # FS-GG/.github, independent of a caller-supplied slug
WORKFLOW = ".github/workflows/release-successor-candidate.yml"
PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")
OTHER_FILES = {
    "release-manifest.json",
    "standalone-telemetry-evidence.json",
    "standalone-telemetry-runtime-evidence.json",
    "previous-stable-channel.json",
}


def require(condition: bool, detail: str) -> None:
    if not condition:
        raise ValueError(detail)


def verify_metadata(artifact: dict, run: dict, archive: pathlib.Path, source_sha: str) -> None:
    require(bool(re.fullmatch(r"[0-9a-f]{40}", source_sha)), "invalid source SHA")
    run_id = run.get("id")
    require(isinstance(run_id, int) and run_id > 0, "invalid candidate run id")
    require(
        run.get("path") == WORKFLOW
        and run.get("head_sha") == source_sha
        and run.get("head_branch") == "main"
        and run.get("event") == "workflow_dispatch"
        and run.get("conclusion") == "success"
        and run.get("run_attempt") == 1,
        "candidate run is not a successful first-attempt exact-main workflow dispatch",
    )
    expected_name = f"release-successor-candidate-{source_sha}-{run_id}"
    binding = artifact.get("workflow_run") or {}
    require(
        isinstance(artifact.get("id"), int)
        and artifact["id"] > 0
        and artifact.get("name") == expected_name
        and artifact.get("expired") is False
        and binding.get("id") == run_id
        and binding.get("head_sha") == source_sha
        and binding.get("head_branch") == "main"
        and binding.get("repository_id") == REPOSITORY_ID
        and binding.get("head_repository_id") == REPOSITORY_ID,
        "candidate artifact identity does not bind the successful run",
    )
    expected_digest = artifact.get("digest", "")
    require(bool(re.fullmatch(r"sha256:[0-9a-f]{64}", expected_digest)), "artifact digest absent")
    with archive.open("rb") as stream:
        actual_digest = hashlib.file_digest(stream, "sha256").hexdigest()
    require(expected_digest == "sha256:" + actual_digest, "candidate archive digest mismatch")


def verify_members(archive: zipfile.ZipFile, version: str) -> list[str]:
    require(bool(re.fullmatch(r"\d+\.\d+\.\d+", version)), "invalid stable candidate version")
    expected = OTHER_FILES | {f"{package}.{version}.nupkg" for package in PACKAGES}
    members = archive.infolist()
    names = [member.filename for member in members]
    require(len(names) == len(expected) and set(names) == expected, "candidate archive members differ from the exact set")
    for member in members:
        mode = (member.external_attr >> 16) & 0xFFFF
        require(
            not member.is_dir()
            and "/" not in member.filename
            and "\\" not in member.filename
            and member.filename not in {".", ".."}
            and not stat.S_ISLNK(mode),
            "unsafe candidate archive member",
        )
    return names


def extract_verified(
    artifact: dict, run: dict, archive_path: pathlib.Path, output: pathlib.Path, source_sha: str
) -> dict:
    verify_metadata(artifact, run, archive_path, source_sha)
    require(not output.exists(), "candidate extraction target already exists")
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive_path) as archive:
        # Version is read only after the archive identity is proven. The manifest's own
        # descriptor/content ID and package bytes are validated again below.
        require(
            len(archive.infolist()) == 7
            and sum(member.filename == "release-manifest.json" for member in archive.infolist()) == 1
            and all(member.file_size <= 250_000_000 for member in archive.infolist()),
            "candidate archive has an unsafe member count or size",
        )
        manifest = json.loads(archive.read("release-manifest.json"))
        version = manifest["descriptor"]["version"]
        names = verify_members(archive, version)
        require(manifest["descriptor"].get("sourceSha") == source_sha, "manifest source differs from run")
        require(manifest["descriptor"].get("policyVersion") == "release-successor/1", "wrong policy")
        descriptor = manifest["descriptor"]
        require(
            all(
                row.get("artifact", {}).get("path") == f"{row.get('id')}.{version}.nupkg"
                for row in descriptor.get("packages", [])
            )
            and descriptor.get("standaloneTelemetry", {}).get("qualificationPath")
            == "standalone-telemetry-runtime-evidence.json",
            "candidate manifest references files outside the retained archive",
        )
        with tempfile.TemporaryDirectory(prefix="release-successor-", dir=output.parent) as temporary:
            work = pathlib.Path(temporary)
            for name in names:
                with archive.open(name) as source, (work / name).open("xb") as target:
                    shutil.copyfileobj(source, target)
                os.chmod(work / name, 0o600)
            checker = pathlib.Path(__file__).with_name("release-saga.py")
            subprocess.run(
                [sys.executable, str(checker), "assert-artifacts", "--manifest", str(work / "release-manifest.json")],
                check=True,
                capture_output=True,
                text=True,
            )
            predecessor = json.loads((work / "previous-stable-channel.json").read_text())
            require(
                predecessor.get("contentId") == descriptor.get("previousStableContentId")
                and predecessor.get("version") == descriptor.get("previousStableVersion"),
                "candidate predecessor receipt differs from manifest",
            )
            os.rename(work, output)
    return {"sourceSha": source_sha, "version": version, "contentId": manifest["contentId"]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact-json", type=pathlib.Path, required=True)
    parser.add_argument("--run-json", type=pathlib.Path, required=True)
    parser.add_argument("--archive", type=pathlib.Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    try:
        result = extract_verified(
            json.loads(args.artifact_json.read_text()),
            json.loads(args.run_json.read_text()),
            args.archive,
            args.output,
            args.source_sha,
        )
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f"release candidate artifact refused: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

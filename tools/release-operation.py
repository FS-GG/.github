#!/usr/bin/env python3
"""Read a promoted coherent-set release as native PR-less operation completion."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import tempfile
from pathlib import Path
from typing import Any


SHA = re.compile(r"^[0-9a-f]{40}$")
VERSION = re.compile(r"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")
SCHEMA = "fsgg.release-operation-completion/1"


class Refusal(Exception):
    pass


def run(*arguments: str, cwd: Path | None = None) -> str:
    result = subprocess.run(arguments, cwd=cwd, text=True, capture_output=True)
    if result.returncode != 0:
        raise Refusal("command refused: " + " ".join(arguments[:3]) + ": " + " ".join(result.stderr.split()))
    return result.stdout


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Refusal(message)


def tag_source(repo: str, tag: str) -> str:
    reference = json.loads(run("gh", "api", f"repos/{repo}/git/ref/tags/{tag}"))["object"]
    if reference.get("type") == "tag":
        reference = json.loads(run("gh", "api", f"repos/{repo}/git/tags/{reference['sha']}"))["object"]
    require(reference.get("type") == "commit" and SHA.fullmatch(reference.get("sha", "")) is not None,
            "release tag does not resolve to one exact commit")
    return reference["sha"]


def completion(
    repo: str,
    version: str,
    source_sha: str,
    release: dict[str, Any],
    manifest: dict[str, Any],
    channel: dict[str, Any],
    observed_tag_source: str,
) -> dict[str, Any]:
    tag = f"coherent-set/v{version}"
    require(release.get("tagName") == tag and release.get("isDraft") is False and release.get("isImmutable") is True,
            "release is absent, draft, mutable, or names another tag")
    require(observed_tag_source == source_sha, "release tag source differs from the requested immutable source")
    require(manifest.get("schema") == "fsgg.release-saga/1", "release manifest schema is unsupported")
    descriptor = manifest.get("descriptor") if isinstance(manifest.get("descriptor"), dict) else {}
    state = manifest.get("state") if isinstance(manifest.get("state"), dict) else {}
    require(descriptor.get("releaseId") == f"github:{version}" and descriptor.get("version") == version
            and descriptor.get("sourceSha") == source_sha and descriptor.get("policyVersion") == "release-saga/1",
            "release manifest identity differs")
    require(state.get("phase") == "promoted"
            and state.get("channelPromotion", {}).get("state") == "promoted"
            and state.get("feeds", {}).get("github", {}).get("state") == "verified"
            and state.get("feeds", {}).get("nuget", {}).get("state") == "verified",
            "release has not completed both feeds and stable promotion")
    require(channel.get("version") == version and channel.get("sourceSha") == source_sha
            and channel.get("contentId") == manifest.get("contentId"),
            "stable channel differs from the promoted manifest")
    packages = descriptor.get("packages") if isinstance(descriptor.get("packages"), list) else []
    require(bool(packages), "release manifest has no packages")
    compact_packages = []
    for package in packages:
        artifact = package.get("artifact") if isinstance(package.get("artifact"), dict) else {}
        package_id = package.get("id")
        require(isinstance(package_id, str) and package.get("version") == version
                and isinstance(artifact.get("sha256"), str) and isinstance(artifact.get("payloadSha256"), str),
                "package identity or hashes are incomplete")
        for feed in ("github", "nuget"):
            feed_state = state["feeds"][feed].get("packages", {}).get(package_id, {})
            require(feed_state.get("state") == "verified" and feed_state.get("externalPayloadSha256") == artifact["payloadSha256"],
                    f"{feed} has not verified the manifest payload for {package_id}")
        github_state = state["feeds"]["github"]["packages"][package_id]
        require(github_state.get("externalSha256") == artifact["sha256"],
                f"github has not verified the exact manifest archive for {package_id}")
        compact_packages.append({"id": package_id, "archiveSha256": artifact["sha256"], "payloadSha256": artifact["payloadSha256"]})
    return {
        "schema": SCHEMA,
        "operation": "coherent-set-release",
        "releaseClass": "stable-coherent-tooling",
        "status": "completed",
        "repository": repo,
        "release": release.get("url"),
        "tag": tag,
        "version": version,
        "sourceSha": source_sha,
        "contentId": manifest["contentId"],
        "packages": compact_packages,
        "publication": {"github": "verified", "nuget": "verified", "stableChannel": "promoted"},
        "verificationPolicy": {
            "githubArchive": "exact",
            "nugetArchive": "server-signature-tolerated-payload-exact",
            "independentRebuild": "sampled-not-blocking",
            "receiverAdoption": "asynchronous-batch",
        },
        "completionAuthority": "native-release",
        "sourcePullRequest": None,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    try:
        require(VERSION.fullmatch(args.version) is not None, "version is not a canonical stable SemVer triple")
        require(SHA.fullmatch(args.source_sha) is not None, "source SHA is not exact lowercase 40-hex")
        tag = f"coherent-set/v{args.version}"
        with tempfile.TemporaryDirectory(prefix="fsgg-release-operation-") as temporary:
            root = Path(temporary)
            release = json.loads(run("gh", "release", "view", tag, "--repo", args.repo,
                                     "--json", "tagName,isDraft,isImmutable,url"))
            run("gh", "release", "download", tag, "--repo", args.repo, "--dir", str(root))
            manifest_path = root / "release-manifest.json"
            channel_path = root / "stable-channel.json"
            require(manifest_path.is_file() and channel_path.is_file(), "release lacks manifest or stable-channel assets")
            local_tool = Path(__file__).resolve().parents[1] / "scripts/release-saga.py"
            run("python3", str(local_tool), "assert-identity", "--manifest", str(manifest_path),
                "--release-id", f"github:{args.version}", "--version", args.version,
                "--source-sha", args.source_sha, "--policy-version", "release-saga/1")
            run("python3", str(local_tool), "assert-artifacts", "--manifest", str(manifest_path))
            result = completion(args.repo, args.version, args.source_sha, release,
                                json.loads(manifest_path.read_text()), json.loads(channel_path.read_text()),
                                tag_source(args.repo, tag))
        encoded = json.dumps(result, sort_keys=True, separators=(",", ":")) + "\n"
        target = Path(args.output)
        if target.exists() and target.read_text(encoding="utf-8") != encoded:
            raise Refusal("output already records different operation completion")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(encoded, encoding="utf-8")
        print(encoded, end="")
        return 0
    except (OSError, ValueError, KeyError, json.JSONDecodeError, Refusal) as error:
        print(json.dumps({"schema": SCHEMA, "status": "refused", "reason": str(error)}, sort_keys=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

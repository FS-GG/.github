"""Exact one-package Wizard release effect identities for the admitted successor."""

from __future__ import annotations

import hashlib
import json
import re

from release_successor_execution import Effect, Refused

PACKAGE = "FS.GG.NewSddWorkspace"
VERSION = "0.12.0"
TAG = "new-sdd-workspace/v0.12.0"


def effects(manifest: dict) -> tuple[str, tuple[Effect, ...]]:
    if (
        manifest.get("schema") != "fsgg.new-sdd-workspace-release/1"
        or manifest.get("packageId") != PACKAGE
        or manifest.get("version") != VERSION
        or manifest.get("tag") != TAG
        or not re.fullmatch(r"[0-9a-f]{40}", manifest.get("sourceSha", ""))
        or not re.fullmatch(r"[0-9a-f]{64}", manifest.get("archiveSha256", ""))
        or not re.fullmatch(r"sha256:[0-9a-f]{64}", manifest.get("producerPayloadSha256", ""))
    ):
        raise Refused("not an exact Wizard 0.12.0 release manifest")
    canonical = json.dumps(manifest, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    manifest_digest = hashlib.sha256(canonical).hexdigest()
    manifest_archive_digest = hashlib.sha256(canonical + b"\n").hexdigest()
    content_id = "sha256:" + manifest_digest
    archive = manifest["archiveSha256"]
    payload = manifest["producerPayloadSha256"]
    return content_id, (
        Effect("tag", manifest["sourceSha"], content_id),
        Effect("draft", content_id, content_id),
        Effect("github", payload, archive),
        Effect("nuget", payload, archive),
        Effect("package-asset", archive, archive),
        Effect("manifest-asset", manifest_archive_digest, manifest_archive_digest),
        Effect("publication-journal-asset", content_id, content_id),
        Effect("promote", content_id, content_id),
    )

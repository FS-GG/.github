"""Exact one-package Wizard release effect identities for the admitted successor."""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass

from release_successor_execution import Effect, Refused

@dataclass(frozen=True)
class ReleaseBinding:
    package: str
    version: str
    tag: str


# Retained identity only: no current or historical-recovery effect authority.
CURRENT_014 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.14.0", "new-sdd-workspace/v0.14.0")
CURRENT_015 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.15.0", "new-sdd-workspace/v0.15.0")
HISTORICAL_013 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.13.0", "new-sdd-workspace/v0.13.0")
CURRENT_016 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.16.0", "new-sdd-workspace/v0.16.0")
# Explicit source successor; retained 0.16 descriptors do not confer current publication authority.
CURRENT_017 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.17.0", "new-sdd-workspace/v0.17.0")
# Retain the accepted018 identity without granting current publication authority.
CURRENT_018 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.18.0", "new-sdd-workspace/v0.18.0")
CURRENT_019 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.19.0", "new-sdd-workspace/v0.19.0")
# The consumed019 descriptor is retained for authenticated historical readback only.
CURRENT_020 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.20.0", "new-sdd-workspace/v0.20.0")
CURRENT_021 = ReleaseBinding("FS.GG.NewSddWorkspace", "0.21.0", "new-sdd-workspace/v0.21.0")
PACKAGE, VERSION, TAG = CURRENT_021.package, CURRENT_021.version, CURRENT_021.tag


def effects(manifest: dict, *, binding: ReleaseBinding = CURRENT_021) -> tuple[str, tuple[Effect, ...]]:
    if binding is not CURRENT_021 and binding is not CURRENT_020 and binding is not CURRENT_019 and binding is not CURRENT_017 and binding is not CURRENT_016 and binding is not HISTORICAL_013:
        raise Refused("unknown Wizard release binding")
    if (
        manifest.get("schema") != "fsgg.new-sdd-workspace-release/1"
        or manifest.get("packageId") != binding.package
        or manifest.get("version") != binding.version
        or manifest.get("tag") != binding.tag
        or not re.fullmatch(r"[0-9a-f]{40}", manifest.get("sourceSha", ""))
        or not re.fullmatch(r"[0-9a-f]{64}", manifest.get("archiveSha256", ""))
        or not re.fullmatch(r"sha256:[0-9a-f]{64}", manifest.get("producerPayloadSha256", ""))
    ):
        raise Refused(f"not an exact Wizard {binding.version} release manifest")
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

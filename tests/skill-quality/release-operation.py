#!/usr/bin/env python3
"""Exercise native PR-less release completion and mismatch refusal."""

import copy
import importlib.util
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("release_operation", ROOT / "tools/release-operation.py")
module = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(module)

source = "a" * 40
payload = "sha256:" + "b" * 64
archive = "c" * 64
package = {"id": "FS.GG.Kit", "version": "1.2.3", "artifact": {"sha256": archive, "payloadSha256": payload}}
github_verified = {"FS.GG.Kit": {"state": "verified", "externalSha256": archive,
                                  "externalPayloadSha256": payload}}
# NuGet may add its server signature, so its archive hash is deliberately different while its
# producer payload remains exact.
nuget_verified = {"FS.GG.Kit": {"state": "verified", "externalSha256": "e" * 64,
                                 "externalPayloadSha256": payload}}
manifest = {
    "schema": "fsgg.release-saga/1",
    "contentId": "sha256:" + "d" * 64,
    "descriptor": {
        "releaseId": "github:1.2.3", "version": "1.2.3", "sourceSha": source,
        "policyVersion": "release-saga/1", "packages": [package],
    },
    "state": {
        "phase": "promoted", "channelPromotion": {"state": "promoted"},
        "feeds": {"github": {"state": "verified", "packages": github_verified},
                  "nuget": {"state": "verified", "packages": nuget_verified}},
    },
}
channel = {"version": "1.2.3", "sourceSha": source, "contentId": manifest["contentId"]}
release = {"tagName": "coherent-set/v1.2.3", "isDraft": False, "isImmutable": True,
           "url": "https://example.invalid/releases/1.2.3"}

first = module.completion("FS-GG/.github", "1.2.3", source, release, manifest, channel, source)
second = module.completion("FS-GG/.github", "1.2.3", source, release, manifest, channel, source)
assert first == second
assert first["status"] == "completed"
assert first["completionAuthority"] == "native-release"
assert first["sourcePullRequest"] is None
assert first["verificationPolicy"]["receiverAdoption"] == "asynchronous-batch"
assert first["packages"] == [{"id": "FS.GG.Kit", "archiveSha256": archive, "payloadSha256": payload}]

def refused(name, mutate):
    changed_release, changed_manifest, changed_channel, changed_source = copy.deepcopy(release), copy.deepcopy(manifest), copy.deepcopy(channel), source
    changed_source = mutate(changed_release, changed_manifest, changed_channel, changed_source)
    try:
        module.completion("FS-GG/.github", "1.2.3", source, changed_release, changed_manifest, changed_channel, changed_source)
    except module.Refusal:
        return
    raise AssertionError(f"{name} did not refuse")


refused("mutable release", lambda r, m, c, s: (r.update(isImmutable=False), s)[1])
refused("wrong tag source", lambda r, m, c, s: "e" * 40)
refused("unpromoted state", lambda r, m, c, s: (m["state"].update(phase="publishing"), s)[1])
refused("mismatched stable channel", lambda r, m, c, s: (c.update(contentId="sha256:" + "f" * 64), s)[1])
refused("mismatched GitHub archive", lambda r, m, c, s: (m["state"]["feeds"]["github"]["packages"]["FS.GG.Kit"].update(externalSha256="different"), s)[1])
refused("mismatched feed bytes", lambda r, m, c, s: (m["state"]["feeds"]["nuget"]["packages"]["FS.GG.Kit"].update(externalPayloadSha256="different"), s)[1])

print("release-operation: PR-less completion, identical replay, and mismatch refusal pass")

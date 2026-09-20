#!/usr/bin/env python3
"""Fake GitHub API checks for live tag/draft observation and dispatch."""

import json
import hashlib
import pathlib
import sys
import tempfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))

from release_successor_execution import Effect
from release_successor_provider import LiveProvider, NotFound


class FakeAPI:
    def __init__(self):
        self.tag = None
        self.release = None
        self.assets = {}
        self.writes = []

    def get(self, path):
        if path.endswith("/git/ref/tags/coherent-set/v0.91.3") and self.tag:
            return {"object": {"sha": self.tag}}
        if path.endswith("/releases/tags/coherent-set/v0.91.3") and self.release and not self.release["draft"]:
            return self.release
        if path.endswith("/releases?per_page=100&page=1"):
            return [self.release] if self.release else []
        if path.endswith("/releases/1/assets?per_page=100"):
            return [{"id": index, "name": name} for index, name in enumerate(self.assets, 1)]
        raise NotFound(path)

    def upload_asset(self, release_id, name, path):
        assert release_id == 1 and name not in self.assets
        self.assets[name] = path.read_bytes()
        self.writes.append(("asset", name))
        return {"id": len(self.assets), "name": name}

    def download_asset(self, asset_id):
        return list(self.assets.values())[asset_id - 1]

    def post(self, path, body):
        self.writes.append((path, body))
        if path.endswith("/git/refs"):
            self.tag = body["sha"]
            return {"object": {"sha": self.tag}}
        if path.endswith("/releases"):
            self.release = {"id": 1, "tag_name": body["tag_name"], "target_commitish": body["target_commitish"],
                            "body": body["body"], "draft": body["draft"]}
            return self.release
        raise AssertionError(path)


with tempfile.TemporaryDirectory() as temporary:
    root = pathlib.Path(temporary)
    source = "b" * 40
    content_id = "sha256:" + "a" * 64
    manifest = root / "release-manifest.json"
    manifest.write_text(json.dumps({"contentId": content_id, "descriptor": {"sourceSha": source, "version": "0.91.3"}}))
    api = FakeAPI()
    provider = LiveProvider(api, manifest, "github-token", "nuget-key")
    tag = Effect("tag", source, content_id)
    draft = Effect("draft", content_id, content_id)
    assert provider.observe(tag).state == "absent"
    assert provider.dispatch(tag).state == "applied"
    assert provider.observe(tag).state == "matched"
    assert provider.observe(draft).state == "absent"
    assert provider.dispatch(draft).state == "applied"
    assert provider.observe(draft).state == "matched"
    assert api.writes[0][1] == {"ref": "refs/tags/coherent-set/v0.91.3", "sha": source}
    assert len(api.writes) == 2
    package_name = "FS.GG.Kit.0.91.3.nupkg"
    (root / package_name).write_bytes(b"candidate package bytes")
    digest = hashlib.sha256((root / package_name).read_bytes()).hexdigest()
    archive = Effect("archive-asset:FS.GG.Kit", digest, digest)
    assert provider.observe(archive).state == "absent"
    assert provider.dispatch(archive).state == "applied"
    assert provider.observe(archive).state == "matched"
    api.assets[package_name] = b"different bytes"
    assert provider.observe(archive).state == "mismatched"
    api.tag = "c" * 40
    assert provider.observe(tag).state == "mismatched"
    api.release["body"] = "unrelated"
    assert provider.observe(draft).state == "mismatched"

print("release successor live tag/draft fake API cases passed")

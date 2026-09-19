#!/usr/bin/env python3
"""Fake GitHub API checks for live tag/draft observation and dispatch."""

import json
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
        self.writes = []

    def get(self, path):
        if path.endswith("/git/ref/tags/coherent-set/v0.91.0") and self.tag:
            return {"object": {"sha": self.tag}}
        if path.endswith("/releases/tags/coherent-set/v0.91.0") and self.release:
            return self.release
        raise NotFound(path)

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
    manifest.write_text(json.dumps({"contentId": content_id, "descriptor": {"sourceSha": source, "version": "0.91.0"}}))
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
    assert api.writes[0][1] == {"ref": "refs/tags/coherent-set/v0.91.0", "sha": source}
    assert len(api.writes) == 2
    api.tag = "c" * 40
    assert provider.observe(tag).state == "mismatched"
    api.release["body"] = "unrelated"
    assert provider.observe(draft).state == "mismatched"

print("release successor live tag/draft fake API cases passed")

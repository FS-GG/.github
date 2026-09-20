#!/usr/bin/env python3
"""Exercise the protected Git journal's ancestry and compare-and-swap behavior."""

import hashlib
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / "scripts"))

from release_successor_journal import ProtectedReleaseJournal, REF, REPOSITORY, Refused


class FakeGit:
    def __init__(self):
        self.objects = {}
        self.ref = None
        self.ref_reads = 0
        self.conflict = False

    def _sha(self, kind, value):
        import json

        return hashlib.sha1((kind + json.dumps(value, sort_keys=True)).encode()).hexdigest()

    def get(self, path):
        if path == f"repos/{REPOSITORY}":
            return {"id": 1351660651, "full_name": REPOSITORY}
        if path == f"repos/{REPOSITORY}/git/ref/{REF.removeprefix('refs/')}":
            self.ref_reads += 1
            if self.ref is None:
                raise KeyError("404")
            return {"object": {"sha": self.ref}}
        kind, sha = path.rsplit("/", 2)[-2:]
        value = self.objects[kind, sha]
        if kind == "commits":
            return {**value, "tree": {"sha": value["tree"]}, "parents": [{"sha": parent} for parent in value["parents"]]}
        return value

    def post(self, path, body):
        if path.endswith("/git/refs"):
            if self.ref is not None:
                raise ValueError("ref exists")
            self.ref = body["sha"]
            return {"ref": REF, "object": {"sha": self.ref}}
        kind = path.rsplit("/", 1)[-1]
        sha = self._sha(kind, body)
        result = {"sha": sha, **body}
        self.objects[kind, sha] = result
        return result

    def patch(self, path, body):
        if self.conflict:
            raise ValueError("non-fast-forward")
        parent = self.objects["commits", body["sha"]]["parents"]
        if body["force"] is not False or parent != [self.ref]:
            raise ValueError("non-fast-forward")
        self.ref = body["sha"]
        return {"ref": REF, "object": {"sha": self.ref}}


intent = {
    "contentId": "sha256:" + "a" * 64,
    "sourceSha": "b" * 40,
    "version": "0.91.3",
    "candidateArchiveSha256": "c" * 64,
    "operator": "EHotwagner",
}
api = FakeGit()
journal = ProtectedReleaseJournal(api)
start = journal.initialize(intent)
assert (start.generation, start.effects) == (1, {})
assert journal.compare_and_swap(start, "tag", "intent")
intent_state = journal.read()
assert (intent_state.generation, intent_state.effects) == (2, {"tag": "intent"})
assert journal.compare_and_swap(intent_state, "tag", "verified")
assert journal.read().effects == {"tag": "verified"}

try:
    journal.compare_and_swap(start, "draft", "intent")
    raise AssertionError("stale CAS accepted")
except Refused:
    pass

api.conflict = True
try:
    journal.compare_and_swap(journal.read(), "draft", "intent")
    raise AssertionError("non-fast-forward CAS accepted")
except Refused:
    pass
api.conflict = False
assert journal.read().effects == {"tag": "verified"}

api.objects["blobs", api.objects["trees", api.objects["commits", api.ref]["tree"]]["tree"][0]["sha"]]["content"] = "e30="
try:
    journal.read()
    raise AssertionError("tampered journal accepted")
except Refused:
    pass

print("protected release journal lineage and CAS cases passed")

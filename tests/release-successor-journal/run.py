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
        self.commit_reads = 0
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
        if kind == "commits":
            self.commit_reads += 1
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
before = api.commit_reads
assert journal.read().effects == {"tag": "verified"}
assert api.commit_reads - before == 1, "same-head read traversed validated ancestry"

# A changed head validates only its new suffix and must link to the cached head.
prior = journal.read()
fork = journal._create_commit(
    {"schema": "fsgg.release-successor-journal/1", **intent, "generation": 1, "effects": {}}, []
)
api.ref = fork
try:
    journal.read()
    raise AssertionError("non-descendant journal head accepted")
except Refused:
    pass
api.ref = journal._observed.head
assert journal.read() == prior

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

# Exercise the optional representation with actual Git objects, tree overlays and refs.
import base64
import json
import os
import subprocess
import tempfile
from release_successor_journal import PATH, canonical


class RealGit:
    def __init__(self, root):
        self.root = root
        self.env = {**os.environ, "GIT_AUTHOR_NAME": "Fixture", "GIT_AUTHOR_EMAIL": "fixture@example.invalid",
                    "GIT_COMMITTER_NAME": "Fixture", "GIT_COMMITTER_EMAIL": "fixture@example.invalid"}
        self.command("init", "--bare", "--quiet")
        tree = self.command("mktree", data=b"").strip().decode()
        main = self.command("commit-tree", tree, data=b"Fixture main\n").strip().decode()
        self.command("update-ref", "refs/heads/main", main)

    def command(self, *args, data=None, env=None):
        return subprocess.run(["git", "--git-dir", str(self.root), *args], input=data, stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, check=True, env=env or self.env).stdout

    def get(self, path):
        if path == f"repos/{REPOSITORY}":
            return {"id": 1351660651, "full_name": REPOSITORY}
        if "/commits?" in path:
            from urllib.parse import parse_qs
            query = parse_qs(path.split("?", 1)[1])
            history = self.command("log", "--first-parent", "--full-history", "--format=%H", query["sha"][0], "--", query["path"][0]).decode().splitlines()
            page = int(query["page"][0]); limit = int(query["per_page"][0])
            return [{"sha": sha} for sha in history[(page - 1) * limit:page * limit]]
        suffix = path.split("/git/", 1)[1]
        kind, value = suffix.split("/", 1)
        if kind == "ref":
            return {"object": {"sha": self.command("rev-parse", "refs/" + value).strip().decode()}}
        raw = self.command("cat-file", {"commits": "commit", "trees": "tree", "blobs": "blob"}[kind], value)
        if kind == "blobs":
            return {"sha": value, "encoding": "base64", "content": base64.b64encode(raw).decode()}
        if kind == "commits":
            lines = raw.split(b"\n\n", 1)[0].decode().splitlines()
            return {"sha": value, "tree": {"sha": lines[0].split()[1]},
                    "parents": [{"sha": line.split()[1]} for line in lines if line.startswith("parent ")]}
        entries = self.command("ls-tree", value).decode().splitlines()
        return {"sha": value, "tree": [dict(zip(("mode", "type", "sha", "path"), line.replace("\t", " ", 1).split(" ", 3))) for line in entries]}

    def post(self, path, body):
        kind = path.rsplit("/", 1)[-1]
        if kind == "blobs":
            sha = self.command("hash-object", "-w", "--stdin", data=base64.b64decode(body["content"])).strip().decode()
        elif kind == "trees":
            with tempfile.TemporaryDirectory() as scratch:
                env = {**self.env, "GIT_INDEX_FILE": str(pathlib.Path(scratch) / "index")}
                if "base_tree" in body:
                    self.command("read-tree", body["base_tree"], env=env)
                else:
                    self.command("read-tree", "--empty", env=env)
                for entry in body["tree"]:
                    self.command("update-index", "--add", "--cacheinfo", entry["mode"], entry["sha"], entry["path"], env=env)
                sha = self.command("write-tree", env=env).strip().decode()
        elif kind == "commits":
            parents = [item for parent in body["parents"] for item in ("-p", parent)]
            sha = self.command("commit-tree", body["tree"], *parents, data=(body["message"] + "\n").encode()).strip().decode()
        elif kind == "refs":
            self.command("update-ref", body["ref"], body["sha"], "0" * 40)
            return {"object": {"sha": body["sha"]}}
        else:
            raise AssertionError(kind)
        return {"sha": sha}

    def patch(self, path, body):
        assert body["force"] is False
        ref = "refs/" + path.split("/git/refs/", 1)[1]
        proposed = self.get(f"repos/{REPOSITORY}/git/commits/{body['sha']}")
        expected = proposed["parents"][0]["sha"]
        self.command("update-ref", ref, body["sha"], expected)
        return {"object": {"sha": body["sha"]}}

    def sibling(self, name="ordinary.txt"):
        physical = self.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]
        parent = self.get(f"repos/{REPOSITORY}/git/commits/{physical}")
        blob = self.post(f"repos/{REPOSITORY}/git/blobs", {"content": base64.b64encode(b"Sibling state\n").decode()})
        tree = self.post(f"repos/{REPOSITORY}/git/trees", {"base_tree": parent["tree"]["sha"],
                         "tree": [{"path": name, "mode": "100644", "type": "blob", "sha": blob["sha"]}]})
        commit = self.post(f"repos/{REPOSITORY}/git/commits", {"tree": tree["sha"], "parents": [physical], "message": "Sibling"})
        self.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": commit["sha"], "force": False})
        return commit["sha"]


with tempfile.TemporaryDirectory() as scratch:
    real = RealGit(pathlib.Path(scratch) / "authority.git")
    legacy = ProtectedReleaseJournal(real)
    legacy.initialize(intent)
    assert legacy.compare_and_swap(legacy.read(), "tag", "intent")
    assert legacy.compare_and_swap(legacy.read(), "tag", "verified")
    logical = legacy._observed.head
    original_raw = real.command("show", f"{logical}:{PATH}")
    real.sibling()
    main = ProtectedReleaseJournal(real, main_directory=True)
    retained = main.retain_legacy(intent)
    assert retained == legacy.read()
    assert main._observed.head == logical
    assert real.command("show", f"refs/heads/main:{main.directory}/{PATH}") == original_raw
    real.command("merge-base", "--is-ancestor", logical, "refs/heads/main")
    real.sibling("another.txt")
    assert main.read() == retained, "unrelated first-parent update changed logical release"
    # A second directory retains its own chain and does not replace this release.
    other_ref = REF + "-other"
    other = ProtectedReleaseJournal(real, other_ref, main_directory=True)
    assert other.initialize({**intent, "version": "0.91.4"}).generation == 1
    assert main.read() == retained
    assert main.compare_and_swap(retained, "draft", "intent")
    fresh = ProtectedReleaseJournal(real, main_directory=True)
    assert fresh.read().effects == {"tag": "verified", "draft": "intent"}
    for path in ("ordinary.txt", "another.txt", f"{other.directory}/{PATH}"):
        assert real.command("show", f"refs/heads/main:{path}")
    # Native path pages cannot silently omit the original introduction.
    original_get = real.get
    def incomplete_history(path):
        value = original_get(path)
        if "/commits?" in path and value:
            return value[:-1]
        return value
    real.get = incomplete_history
    try:
        ProtectedReleaseJournal(real, main_directory=True).read()
        raise AssertionError("truncated native path history accepted")
    except Refused:
        pass
    real.get = original_get
    # Stale physical CAS refuses without losing another directory's write.
    stale = fresh.read()
    sibling = real.sibling("conflict.txt")
    try:
        fresh.compare_and_swap(stale, "draft", "verified")
        raise AssertionError("stale physical CAS accepted")
    except Refused:
        pass
    assert real.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"] == sibling
    assert fresh.read() == stale
    # An applied write with a lost response remains uncertain until exact reread.
    prior = fresh.read()
    original_patch = real.patch
    def applied_unknown(path, body):
        original_patch(path, body)
        raise RuntimeError("synthetic lost response after real Git CAS")
    real.patch = applied_unknown
    try:
        fresh.compare_and_swap(prior, "draft", "verified")
        raise AssertionError("unknown write response promoted to success")
    except Refused:
        pass
    real.patch = original_patch
    assert fresh.read().effects["draft"] == "verified"
    # A valid new logical state without the required retaining second parent refuses.
    physical = fresh._physical_head
    next_state = {**fresh._observed.state, "generation": fresh._observed.state["generation"] + 1,
                  "effects": {**fresh._observed.state["effects"], "publish": "intent"}}
    next_logical = fresh._create_commit(next_state, [fresh._observed.head])
    overlay = fresh._overlay(physical, next_logical, next_state)
    details = real.get(f"repos/{REPOSITORY}/git/commits/{overlay}")
    unretained = real.post(f"repos/{REPOSITORY}/git/commits", {
        "tree": details["tree"]["sha"], "parents": [physical], "message": "Missing logical parent"})["sha"]
    real.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": unretained, "force": False})
    try:
        ProtectedReleaseJournal(real, main_directory=True).read()
        raise AssertionError("new logical state without retaining parent accepted")
    except Refused as error:
        assert "does not retain" in str(error)
    real.command("update-ref", "refs/heads/main", physical, unretained)
    # Pointer/state disagreement refuses even though both objects independently exist.
    physical = fresh._physical_head
    forged = fresh._overlay(physical, fresh._observed.head, legacy._observed.state)
    real.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": forged, "force": False})
    try:
        ProtectedReleaseJournal(real, main_directory=True).read()
        raise AssertionError("pointer/state mismatch accepted")
    except Refused:
        pass
    real.command("update-ref", "refs/heads/main", physical, forged)  # Local fixture reset only.
    # A rollback pointer and exact old state cannot masquerade as valid lineage.
    rollback = fresh._overlay(fresh._physical_head, logical, legacy._observed.state)
    real.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": rollback, "force": False})
    try:
        ProtectedReleaseJournal(real, main_directory=True).read()
        raise AssertionError("logical pointer rollback accepted")
    except Refused:
        pass

print("real Git optional main-directory retention, sibling CAS and rollback controls passed")

with tempfile.TemporaryDirectory() as scratch:
    real = RealGit(pathlib.Path(scratch) / "bounded.git")
    main = ProtectedReleaseJournal(real, main_directory=True)
    main.initialize(intent)
    logical = main._observed.head
    # Raw selected-state equality is insufficient without the retaining second parent.
    physical = main._physical_head
    overlay = main._overlay(physical, logical, main._observed.state)
    details = real.get(f"repos/{REPOSITORY}/git/commits/{overlay}")
    forged = real.post(f"repos/{REPOSITORY}/git/commits", {
        "tree": details["tree"]["sha"], "parents": [physical], "message": "Unrelated identical overlay"})
    # Identical selected state is an ordinary unchanged update, so it is valid.
    real.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": forged["sha"], "force": False})
    assert main.read().generation == 1
    for index in range(128):
        real.sibling(f"bounded-{index}.txt")
    assert ProtectedReleaseJournal(real, main_directory=True).read().generation == 1, "unrelated physical writes expired quiet logical authority"

for kwargs in ({"main_directory": "yes"}, {"main_directory": True, "ref": REF + "/../foreign"}):
    try:
        ProtectedReleaseJournal(FakeGit(), **kwargs)
        raise AssertionError("invalid optional location accepted")
    except Refused:
        pass
print("unknown response, state identity, explicit location and quiet-release unrelated-traffic controls passed")

# The frozen importer constructs objects only in a temporary proposal store.
import runpy
importer = runpy.run_path(str(pathlib.Path(__file__).resolve().parents[2] / "scripts/authority-state-import.py"))
with tempfile.TemporaryDirectory() as scratch:
    root = pathlib.Path(scratch)
    real = RealGit(root / "source.git")
    legacy = ProtectedReleaseJournal(real)
    legacy.initialize(intent)
    assert legacy.compare_and_swap(legacy.read(), "tag", "intent")
    assert legacy.compare_and_swap(legacy.read(), "tag", "verified")
    epoch_ref = importer["EPOCH"]
    event = {"fleetId": "fs-gg-production", "phase": "OpenV2", "schema": "fsgg.github-substrate.epoch-event/1"}
    event_raw = canonical(event)
    head = {"aggregateId": "fleet-cutover:fs-gg-production", "aggregateDigest": hashlib.sha256(b"30:fleet-cutover:fs-gg-production").hexdigest(),
            "shard": "d5", "journalKind": "cutover", "generation": 2, "eventDigest": hashlib.sha256(event_raw).hexdigest()}
    def commit_files(files, parents=()):
        entries = []
        for path, raw in files.items():
            blob = real.post(f"repos/{REPOSITORY}/git/blobs", {"content": base64.b64encode(raw).decode()})
            entries.append({"path": path, "mode": "100644", "type": "blob", "sha": blob["sha"]})
        tree = real.post(f"repos/{REPOSITORY}/git/trees", {"tree": entries})
        return real.post(f"repos/{REPOSITORY}/git/commits", {"tree": tree["sha"], "parents": list(parents), "message": "Frozen source"})["sha"]
    epoch = commit_files({"head.json": canonical(head), "event.json": event_raw})
    real.command("update-ref", epoch_ref, epoch)
    digest, operation_ref = "00" + "a" * 62, importer["PREFIX"] + "operation/00"
    operation = "operation:fixture"
    doc = {"schema": "fsgg.coordination.ordinary-settlement-authority-document/1", "journalRef": operation_ref,
           "entries": {operation: {"attemptId": "fixture:1", "operationId": operation, "generation": 3,
                      "planDigest": "b" * 64, "receiptDigest": "c" * 64, "stage": "complete"}}, "effects": {operation: "c" * 64}}
    ordinary_path = f"ordinary-v2/{digest}.json"
    operation_head = commit_files({ordinary_path: canonical(doc)})
    real.command("update-ref", operation_ref, operation_head)
    refs = {ref: real.command("rev-parse", ref).decode().strip() for ref in ("refs/heads/main", REF, epoch_ref, operation_ref)}
    for ref, oid in refs.items():
        real.command("update-ref", "refs/cleanup-backup/heads/" + ref.removeprefix("refs/heads/"), oid)
    snapshot = {"repository": REPOSITORY, "repositoryId": 1351660651, "proposalTimestamp": "2000-01-01T00:00:00Z", "refs": refs}
    refs_before = real.command("show-ref")
    object_files_before = sorted(str(path.relative_to(real.root)) for path in (real.root / "objects").rglob("*") if path.is_file())
    first = importer["propose"](real.root, snapshot, root / "first")
    second = importer["propose"](real.root, snapshot, root / "second")
    assert first == second and (root / "first/import-manifest.json").read_bytes() == (root / "second/import-manifest.json").read_bytes()
    assert real.command("show-ref") == refs_before
    assert object_files_before == sorted(str(path.relative_to(real.root)) for path in (real.root / "objects").rglob("*") if path.is_file())
    assert first["unresolved"] == [] and first["adoption"].startswith("blocked")
    # Adopt only in this disposable Git fixture to verify actual output objects.
    for obj in first["objects"]:
        actual = real.command("hash-object", "-t", obj["type"], "-w", "--stdin", data=base64.b64decode(obj["base64"])).decode().strip()
        assert actual == obj["oid"]
    real.command("update-ref", "refs/heads/main", first["desiredMain"], refs["refs/heads/main"])
    assert ProtectedReleaseJournal(real, main_directory=True).read() == legacy.read()
    assert real.command("show", f"refs/heads/main:state/{ordinary_path}") == canonical(doc)
    for oid in refs.values():
        real.command("merge-base", "--is-ancestor", oid, "refs/heads/main")
    # Unknown/pending original effects survive unchanged and block reconciliation.
    real.command("update-ref", "refs/heads/main", refs["refs/heads/main"], first["desiredMain"])
    doc["entries"][operation]["stage"] = "effect-pending"
    pending = commit_files({ordinary_path: canonical(doc)}, [operation_head])
    real.command("update-ref", operation_ref, pending, operation_head)
    backup = "refs/cleanup-backup/heads/" + operation_ref.removeprefix("refs/heads/")
    real.command("update-ref", backup, pending, operation_head)
    snapshot["refs"][operation_ref] = pending
    pending_proposal = importer["propose"](real.root, snapshot, root / "pending")
    assert pending_proposal["reconciliation"] == "required" and pending_proposal["unresolved"][0]["stage"] == "effect-pending"
    for bad in ({**snapshot, "refs": {k: v for k, v in snapshot["refs"].items() if k != operation_ref}},
                {**snapshot, "repositoryId": 1}):
        try:
            importer["propose"](real.root, bad, root / "refused")
            raise AssertionError("incomplete or foreign frozen snapshot accepted")
        except ValueError:
            pass
print("deterministic frozen import, exact raw retention, graph reachability and pending-effect refusal passed")

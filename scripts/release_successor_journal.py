"""Protected, append-only release journal over the ordinary writer App's Git ref.

This is a transport for release_successor_execution.Journal. Each state is one
commit with a single JSON blob, and every update is a non-forced fast-forward
from the exact head just read. GitHub's protected ref only admits the ordinary
writer App; no ambient Actions token can initialize or advance it.

Explicit main_directory=True retains the same logical commit chain using a
second parent of an overlay commit on protected main. Native path history is bounded to 128 selected-directory changes; unrelated
main writes do not consume the logical lineage bound. Callers must qualify main protection and
fence legacy writers before adoption; this module does not activate a cutover.
"""

from __future__ import annotations

import base64
import json
import re
from urllib.parse import quote
from dataclasses import dataclass
from typing import Protocol

from release_successor_execution import JournalState

REPOSITORY = "FS-GG/FS.GG.Coordination.Authority"
REPOSITORY_ID = 1351660651
REF = "refs/heads/fsgg/v2/journal/release/utel-rel-18"
PATH = "release-state.json"
SCHEMA = "fsgg.release-successor-journal/1"


class Refused(RuntimeError):
    pass


class GitAPI(Protocol):
    def get(self, path: str) -> dict | list: ...

    def post(self, path: str, body: dict) -> dict: ...

    def patch(self, path: str, body: dict) -> dict: ...


def canonical(value: dict) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n").encode()


def valid_transition(before: dict, after: dict) -> bool:
    if after.get("generation") != before.get("generation", 0) + 1:
        return False
    fixed = ("schema", "contentId", "sourceSha", "version", "candidateArchiveSha256", "operator")
    if any(after.get(key) != before.get(key) for key in fixed):
        return False
    old, new = before.get("effects"), after.get("effects")
    if not isinstance(old, dict) or not isinstance(new, dict):
        return False
    changes = {key for key in old.keys() | new.keys() if old.get(key) != new.get(key)}
    if len(changes) != 1:
        return False
    effect = next(iter(changes))
    return (old.get(effect), new.get(effect)) in {(None, "intent"), ("intent", "verified")}


@dataclass(frozen=True)
class Observed:
    head: str
    state: dict


class ProtectedReleaseJournal:
    def __init__(self, api: GitAPI, ref: str = REF, *, main_directory: bool = False):
        if type(main_directory) is not bool:
            raise Refused("main-directory selection must be explicit boolean")
        if not ref.startswith("refs/heads/fsgg/v2/journal/release/"):
            raise Refused("journal ref is outside the protected release namespace")
        suffix = ref.removeprefix("refs/heads/fsgg/v2/journal/release/")
        if main_directory and not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", suffix):
            raise Refused("main journal release identifier is invalid")
        self.main_directory = main_directory
        self.directory = f"state/releases/{suffix}"
        self.physical_ref = "refs/heads/main" if main_directory else ref
        self._physical_head: str | None = None
        self.ref = ref
        self.api = api
        repository = api.get(f"repos/{REPOSITORY}")
        if repository.get("id") != REPOSITORY_ID or repository.get("full_name") != REPOSITORY:
            raise Refused("authority repository identity differs")
        self._observed: Observed | None = None
        self._lineage_length = 0

    def _main_files(self, commit: dict) -> tuple[bytes, bytes] | None:
        tree_oid = commit["tree"]["sha"]
        for segment in self.directory.split("/"):
            tree = self.api.get(f"repos/{REPOSITORY}/git/trees/{tree_oid}")
            if tree.get("truncated"):
                raise Refused("main journal tree is incomplete")
            matches = [entry for entry in tree.get("tree", []) if entry.get("path") == segment]
            if not matches:
                return None
            if len(matches) != 1 or matches[0].get("type") != "tree":
                raise Refused("main journal directory is invalid")
            tree_oid = matches[0]["sha"]
        tree = self.api.get(f"repos/{REPOSITORY}/git/trees/{tree_oid}")
        entries = tree.get("tree", [])
        if tree.get("truncated") or {entry.get("path") for entry in entries} != {PATH, "release-head.txt"} or len(entries) != 2:
            raise Refused("main journal directory contains unexpected files")
        def raw(name: str) -> bytes:
            entry = next(entry for entry in entries if entry["path"] == name)
            if entry.get("type") != "blob" or entry.get("mode") != "100644":
                raise Refused("main journal entry is not a regular blob")
            blob = self.api.get(f"repos/{REPOSITORY}/git/blobs/{entry['sha']}")
            if blob.get("sha") != entry["sha"] or blob.get("encoding") != "base64":
                raise Refused("main journal blob identity differs")
            return base64.b64decode(blob["content"], validate=False)
        return raw("release-head.txt"), raw(PATH)

    def _main_commit(self, oid: str) -> dict:
        value = self.api.get(f"repos/{REPOSITORY}/git/commits/{oid}")
        if value.get("sha") != oid or not isinstance(value.get("parents"), list) or len(value["parents"]) > 2:
            raise Refused("main journal commit identity or parents differ")
        return value

    def _main_binding(self, files: tuple[bytes, bytes]) -> tuple[str, str | None]:
        pointer, raw = files
        if not re.fullmatch(rb"[0-9a-f]{40}\n", pointer):
            raise Refused("main journal logical head is invalid")
        head = pointer[:-1].decode("ascii")
        state, parent = self._read_commit(head)
        if raw != canonical(state):
            raise Refused("main journal state differs from its logical head")
        return head, parent

    def _main_head(self, physical: str) -> str:
        commit = self._main_commit(physical)
        files = self._main_files(commit)
        if files is None:
            raise Refused("main journal directory is absent")
        selected, _ = self._main_binding(files)
        changes = []
        complete = False
        # Native, pinned path history excludes unrelated main traffic. Two pages
        # suffice for the existing 128 logical-generation bound and completeness.
        for page in (1, 2):
            values = self.api.get(f"repos/{REPOSITORY}/commits?sha={physical}&path={quote(self.directory, safe='')}&per_page=100&page={page}")
            if not isinstance(values, list) or len(values) > 100:
                raise Refused("main journal path history is incomplete")
            changes.extend(values)
            if len(changes) > 128:
                raise Refused("main journal exceeds bounded logical path history")
            if len(values) < 100:
                complete = True
                break
        if not complete or not changes:
            raise Refused("main journal path history is incomplete")
        seen = set()
        expected_files = files
        for index, change in enumerate(changes):
            oid = change.get("sha") if isinstance(change, dict) else None
            if not isinstance(oid, str) or not re.fullmatch(r"[0-9a-f]{40}", oid) or oid in seen:
                raise Refused("main journal path history identity differs")
            seen.add(oid)
            introducing = self._main_commit(oid)
            actual_files = self._main_files(introducing)
            if actual_files != expected_files:
                raise Refused("main journal path history skipped a selected change")
            logical, logical_parent = self._main_binding(actual_files)
            parents = introducing["parents"]
            if len(parents) != 2 or parents[1].get("sha") != logical:
                raise Refused("main journal introduction does not retain its logical head")
            prior = self._main_commit(parents[0]["sha"])
            prior_files = self._main_files(prior)
            if prior_files == actual_files:
                raise Refused("main journal path history contains an unchanged selected directory")
            if prior_files is None:
                if index != len(changes) - 1:
                    raise Refused("main journal path history extends before introduction")
            else:
                prior_logical, _ = self._main_binding(prior_files)
                if logical_parent != prior_logical:
                    raise Refused("main journal logical head does not extend the prior binding")
                if index == len(changes) - 1:
                    raise Refused("main journal path history truncated prior bindings")
            expected_files = prior_files
        return selected

    def _overlay(self, physical: str, logical: str, state: dict) -> str:
        parent = self._main_commit(physical)
        entries = []
        for name, raw in ((PATH, canonical(state)), ("release-head.txt", (logical + "\n").encode("ascii"))):
            blob = self.api.post(f"repos/{REPOSITORY}/git/blobs", {"content": base64.b64encode(raw).decode(), "encoding": "base64"})
            entries.append({"path": f"{self.directory}/{name}", "mode": "100644", "type": "blob", "sha": blob["sha"]})
        tree = self.api.post(f"repos/{REPOSITORY}/git/trees", {"base_tree": parent["tree"]["sha"], "tree": entries})
        commit = self.api.post(f"repos/{REPOSITORY}/git/commits", {
            "message": f"Release directory generation {state['generation']}", "tree": tree["sha"], "parents": [physical, logical]})
        return commit["sha"]

    def _read_commit(self, oid: str) -> tuple[dict, str | None]:
        commit = self.api.get(f"repos/{REPOSITORY}/git/commits/{oid}")
        if commit.get("sha") != oid:
            raise Refused("journal commit identity differs")
        parents = commit.get("parents", [])
        if not isinstance(parents, list) or len(parents) > 1:
            raise Refused("journal commit has an invalid parent list")
        tree = self.api.get(f"repos/{REPOSITORY}/git/trees/{commit['tree']['sha']}")
        entries = tree.get("tree")
        if not isinstance(entries, list) or len(entries) != 1 or entries[0].get("path") != PATH:
            raise Refused("journal commit tree contains unexpected files")
        blob = self.api.get(f"repos/{REPOSITORY}/git/blobs/{entries[0]['sha']}")
        if blob.get("encoding") != "base64" or blob.get("sha") != entries[0]["sha"]:
            raise Refused("journal blob encoding differs")
        raw = base64.b64decode(blob["content"], validate=False)
        value = json.loads(raw)
        if raw != canonical(value) or value.get("schema") != SCHEMA:
            raise Refused("journal blob is not canonical")
        parent = parents[0].get("sha") if parents else None
        return value, parent

    def read(self) -> JournalState:
        path = f"repos/{REPOSITORY}/git/ref/{self.physical_ref.removeprefix('refs/')}"
        first = self.api.get(path)
        head = first.get("object", {}).get("sha")
        if not isinstance(head, str) or len(head) != 40:
            raise Refused("journal ref has no commit head")
        physical = head
        if self.main_directory:
            head = self._main_head(physical)
        lineage: list[dict] = []
        oid: str | None = head
        previous = self._observed
        while oid is not None and (previous is None or oid != previous.head):
            if self._lineage_length + len(lineage) >= 128:
                raise Refused("journal exceeds bounded lineage")
            state, oid = self._read_commit(oid)
            lineage.append(state)
        if previous is not None and oid is None:
            raise Refused("journal head does not descend from the validated head")
        if previous is not None and not lineage:
            # Recheck the current object, including its canonical blob. Git objects
            # already validated below this head are immutable by identity.
            state, parent = self._read_commit(head)
            if state != previous.state:
                raise Refused("journal head changed without a ref change")
            current = state
            length = self._lineage_length
        else:
            length = self._lineage_length + len(lineage)
            lineage.reverse()
            if previous is None:
                if oid is not None:
                    raise Refused("journal root traversal did not terminate")
                root = lineage[0]
                if (
                    root.get("generation") != 1
                    or root.get("effects") != {}
                    or not all(
                        isinstance(root.get(key), str) and root[key]
                        for key in ("contentId", "sourceSha", "version", "candidateArchiveSha256", "operator")
                    )
                ):
                    raise Refused("journal root is not an empty generation-one release intent")
                preceding = root
            else:
                preceding = previous.state
            for state in lineage if previous is not None else lineage[1:]:
                if not valid_transition(preceding, state):
                    raise Refused("journal lineage contains an invalid transition")
                preceding = state
            current = lineage[-1]
        second = self.api.get(path)
        if second.get("object", {}).get("sha") != physical:
            raise Refused("journal head moved during read")
        self._physical_head = physical
        self._observed = Observed(head, current)
        self._lineage_length = length
        return JournalState(current["generation"], current["contentId"], current["effects"])

    def initialize(self, intent: dict) -> JournalState:
        required = {"contentId", "sourceSha", "version", "candidateArchiveSha256", "operator"}
        if set(intent) != required or not all(isinstance(value, str) and value for value in intent.values()):
            raise Refused("release intent is incomplete")
        root = {"schema": SCHEMA, **intent, "generation": 1, "effects": {}}
        commit = self._create_commit(root, [])
        try:
            if self.main_directory:
                physical = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]
                if self._main_files(self._main_commit(physical)) is not None:
                    raise Refused("main journal directory already exists")
                physical_commit = self._overlay(physical, commit, root)
                self.api.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": physical_commit, "force": False})
            else:
                self.api.post(f"repos/{REPOSITORY}/git/refs", {"ref": self.ref, "sha": commit})
        except Exception as error:
            raise Refused(f"journal creation was not confirmed; reconcile before retry: {error}") from error
        observed = self.read()
        if self._observed is None or self._observed.state != root:
            raise Refused("journal creation readback differs")
        return observed

    def retain_legacy(self, intent: dict) -> JournalState:
        """Opt-in retention of the exact existing logical chain; no release effect is retried."""
        if not self.main_directory:
            raise Refused("legacy retention requires explicit main-directory storage")
        legacy = ProtectedReleaseJournal(self.api, self.ref)
        legacy.read()
        legacy.validate_intent(intent)
        assert legacy._observed is not None
        logical = legacy._observed.head
        physical = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]
        if self._main_files(self._main_commit(physical)) is not None:
            raise Refused("main journal directory already exists")
        fresh = self.api.get(f"repos/{REPOSITORY}/git/ref/{self.ref.removeprefix('refs/')}")
        if fresh.get("object", {}).get("sha") != logical:
            raise Refused("legacy journal moved before retention")
        commit = self._overlay(physical, logical, legacy._observed.state)
        try:
            self.api.patch(f"repos/{REPOSITORY}/git/refs/heads/main", {"sha": commit, "force": False})
        except Exception as error:
            raise Refused(f"journal retention response uncertain; reread before further effects: {error}") from error
        result = self.read()
        if self._observed is None or self._observed.head != logical:
            raise Refused("journal retention readback differs")
        return result

    def validate_intent(self, intent: dict) -> None:
        if self._observed is None or any(self._observed.state.get(key) != value for key, value in intent.items()):
            raise Refused("protected release journal binds a different candidate")

    def _create_commit(self, state: dict, parents: list[str]) -> str:
        blob = self.api.post(
            f"repos/{REPOSITORY}/git/blobs",
            {"content": base64.b64encode(canonical(state)).decode(), "encoding": "base64"},
        )
        tree = self.api.post(
            f"repos/{REPOSITORY}/git/trees",
            {"tree": [{"path": PATH, "mode": "100644", "type": "blob", "sha": blob["sha"]}]},
        )
        commit = self.api.post(
            f"repos/{REPOSITORY}/git/commits",
            {"message": f"Release successor generation {state['generation']}", "tree": tree["sha"], "parents": parents},
        )
        return commit["sha"]

    def compare_and_swap(self, expected: JournalState, effect: str, state: str) -> bool:
        observed = self._observed
        if observed is None or (observed.state["generation"], observed.state["contentId"], observed.state["effects"]) != (
            expected.generation,
            expected.content_id,
            expected.effects,
        ):
            raise Refused("journal CAS has no matching prior read")
        next_state = {**observed.state, "generation": expected.generation + 1, "effects": {**expected.effects, effect: state}}
        if not valid_transition(observed.state, next_state):
            raise Refused("journal CAS transition is invalid")
        commit = self._create_commit(next_state, [observed.head])
        physical_commit = commit
        if self.main_directory:
            if self._physical_head is None:
                raise Refused("main journal CAS has no physical prior read")
            physical_commit = self._overlay(self._physical_head, commit, next_state)
        try:
            self.api.patch(
                f"repos/{REPOSITORY}/git/refs/{self.physical_ref.removeprefix('refs/')}",
                {"sha": physical_commit, "force": False},
            )
        except Exception as error:
            raise Refused(f"journal CAS response uncertain; reread before further effects: {error}") from error
        self.read()
        return self._observed is not None and self._observed.head == commit and self._observed.state == next_state

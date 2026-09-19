"""Protected, append-only release journal over the ordinary writer App's Git ref.

This is a transport for release_successor_execution.Journal. Each state is one
commit with a single JSON blob, and every update is a non-forced fast-forward
from the exact head just read. GitHub's protected ref only admits the ordinary
writer App; no ambient Actions token can initialize or advance it.
"""

from __future__ import annotations

import base64
import json
from dataclasses import dataclass
from typing import Protocol

from release_successor_execution import JournalState

REPOSITORY = "FS-GG/FS.GG.Coordination.Authority"
REPOSITORY_ID = 1351660651
REF = "refs/heads/fsgg/v2/journal/release/utel-rel-02"
PATH = "release-state.json"
SCHEMA = "fsgg.release-successor-journal/1"


class Refused(RuntimeError):
    pass


class GitAPI(Protocol):
    def get(self, path: str) -> dict: ...

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
    def __init__(self, api: GitAPI):
        self.api = api
        repository = api.get(f"repos/{REPOSITORY}")
        if repository.get("id") != REPOSITORY_ID or repository.get("full_name") != REPOSITORY:
            raise Refused("authority repository identity differs")
        self._observed: Observed | None = None

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
        path = f"repos/{REPOSITORY}/git/ref/{REF.removeprefix('refs/')}"
        first = self.api.get(path)
        head = first.get("object", {}).get("sha")
        if not isinstance(head, str) or len(head) != 40:
            raise Refused("journal ref has no commit head")
        lineage: list[dict] = []
        oid: str | None = head
        while oid is not None:
            if len(lineage) >= 128:
                raise Refused("journal exceeds bounded lineage")
            state, oid = self._read_commit(oid)
            lineage.append(state)
        lineage.reverse()
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
        if any(not valid_transition(previous, current) for previous, current in zip(lineage, lineage[1:])):
            raise Refused("journal lineage contains an invalid transition")
        second = self.api.get(path)
        if second.get("object", {}).get("sha") != head:
            raise Refused("journal head moved during read")
        current = lineage[-1]
        self._observed = Observed(head, current)
        return JournalState(current["generation"], current["contentId"], current["effects"])

    def initialize(self, intent: dict) -> JournalState:
        required = {"contentId", "sourceSha", "version", "candidateArchiveSha256", "operator"}
        if set(intent) != required or not all(isinstance(value, str) and value for value in intent.values()):
            raise Refused("release intent is incomplete")
        root = {"schema": SCHEMA, **intent, "generation": 1, "effects": {}}
        commit = self._create_commit(root, [])
        try:
            self.api.post(f"repos/{REPOSITORY}/git/refs", {"ref": REF, "sha": commit})
        except Exception as error:
            raise Refused(f"journal creation was not confirmed; reconcile before retry: {error}") from error
        observed = self.read()
        if self._observed is None or self._observed.state != root:
            raise Refused("journal creation readback differs")
        return observed

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
        try:
            self.api.patch(
                f"repos/{REPOSITORY}/git/refs/{REF.removeprefix('refs/')}",
                {"sha": commit, "force": False},
            )
        except Exception as error:
            raise Refused(f"journal CAS response uncertain; reread before further effects: {error}") from error
        self.read()
        return self._observed is not None and self._observed.head == commit and self._observed.state == next_state

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
import hashlib
import json
import re
from urllib.parse import quote
from dataclasses import dataclass
from typing import Protocol

from release_successor_execution import JournalState

REPOSITORY = "FS-GG/FS.GG.Coordination.Authority"
REPOSITORY_ID = 1351660651
REF = "refs/heads/fsgg/v2/journal/release/utel-rel-19"
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


SUSPENSION_PATH = "state/release-dispositions/utel-rel-21.json"
SUSPENSION_SCHEMA = "fsgg.release-successor-suspension/1"
SUSPENDED_REF = "refs/heads/fsgg/v2/journal/release/utel-rel-21"
ALLOWED_SUCCESSOR = {"version": "0.104.0", "predecessor": "0.102.0",
                     "logicalRef": "refs/heads/fsgg/v2/journal/release/utel-rel-22",
                     "profile": "utel-rel-22/suspended-0103-successor/1"}
SUSPENDED_ORIGINAL = {
    "logicalRef": SUSPENDED_REF, "logicalHead": "80bf2bace917316c79032542660a5c00519a828c",
    "stateSHA256": "67018515b0e6b49f96986d7844729461099e950058d20d0a9fa0b8ba3f3864d4",
    "generation": 2, "version": "0.103.0",
    "contentId": "sha256:0615d0d776f06b824b98193233c749f0ae6983e811d900cc5a166ede5e7e5007",
    "sourceSha": "4d136d5450a7032d0b87c4b8a37f69f020144792",
    "archiveSHA256": "d8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba",
    "runId": 38580684077, "attempt": 1,
    "publisherSha": "6c2747248b9d757316b93d7efe8aaf797a080549", "unresolvedEffect": "tag"}
# Source-owned immutable accepted decision. Neither workflow input nor a caller URL
# can substitute for this anchor. Its source-only acceptance grants no native effect.
SUSPENSION_DECISION = {
    "owner": "EHotwagner", "repository": "FS-GG/.github",
    "path": "docs/roadmaps/utel-release-successor.md",
    "revision": "84e1544675634cd4f35b5ed078aa49a23490b08c",
    "sha256": "161d41f753697eb6e7c9e3d41520697f0014fa6ca6fe03916d4fd7af1229f785"}


def strict_suspension(raw: bytes) -> dict:
    def pairs(items):
        value = {}
        for key, item in items:
            if key in value:
                raise Refused("duplicate suspension JSON key")
            value[key] = item
        return value
    if not isinstance(raw, bytes) or len(raw) > 16384:
        raise Refused("suspension record exceeds its bound")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=pairs,
                           parse_constant=lambda _: (_ for _ in ()).throw(Refused("invalid JSON constant")))
    except (UnicodeError, ValueError) as error:
        raise Refused("suspension JSON is malformed") from error
    if raw != canonical(value):
        raise Refused("suspension record is not canonical")
    expected = {"schema": SUSPENSION_SCHEMA, "disposition": "suspended",
                "original": SUSPENDED_ORIGINAL, "allowedSuccessor": ALLOWED_SUCCESSOR,
                "decision": SUSPENSION_DECISION}
    # Canonical comparison also distinguishes bool from int and rejects extra nested keys.
    if (not SUSPENSION_DECISION or not isinstance(value, dict)
            or canonical(value) != canonical(expected)):
        raise Refused("suspension or accepted decision binding differs")
    return value


class ReleaseSuspension:
    """One release-specific reader; live main is never cached across authorizations."""

    def __init__(self, api):
        self.api = api
        self.objects = ProtectedReleaseJournal(api, SUSPENDED_REF, main_directory=True)
        self.digest = None
        self.decision_verified = False

    def main(self):
        value = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
        obj = value.get("object", {}) if isinstance(value, dict) else {}
        head = obj.get("sha")
        if obj.get("type") != "commit" or not isinstance(head, str) or not re.fullmatch(r"[0-9a-f]{40}", head):
            raise Refused("suspension Authority main identity differs")
        return head

    def file(self, physical, path):
        commit = self.objects._main_commit(physical)
        tree_oid = commit["tree"]["sha"]
        segments = path.split("/")
        for index, segment in enumerate(segments):
            tree = self.objects._immutable_object("trees", tree_oid)
            entries = tree["tree"]
            if len(entries) > 1000 or len({entry["path"] for entry in entries}) != len(entries):
                raise Refused("suspension tree is ambiguous or exceeds bound")
            matches = [entry for entry in entries if entry["path"] == segment]
            if not matches:
                return None
            entry = matches[0]
            if index < len(segments) - 1:
                if entry["type"] != "tree" or entry["mode"] != "040000":
                    raise Refused("suspension parent is not a directory")
                tree_oid = entry["sha"]
            else:
                if entry["type"] != "blob" or entry["mode"] != "100644":
                    raise Refused("suspension entry is not a regular blob")
                blob = self.objects._immutable_object("blobs", entry["sha"])
                return base64.b64decode(blob["content"], validate=False)

    def original(self, physical):
        files = self.objects._main_files(self.objects._main_commit(physical))
        if files is None:
            raise Refused("suspended original journal is absent")
        pointer, raw = files
        if (pointer != (SUSPENDED_ORIGINAL["logicalHead"] + "\n").encode()
                or raw is None or hashlib.sha256(raw).hexdigest() != SUSPENDED_ORIGINAL["stateSHA256"]):
            raise Refused("suspended original journal binding changed")
        # Exact state bytes remain Unknown; this reader never settles or rewrites them.

    def verify_decision(self):
        if not self.decision_verified:
            pin = SUSPENSION_DECISION
            source = self.api.get(f"repos/{pin['repository']}/contents/{pin['path']}?ref={pin['revision']}")
            if (not isinstance(source, dict) or source.get("encoding") != "base64"
                    or not isinstance(source.get("content"), str)
                    or hashlib.sha256(base64.b64decode(source["content"], validate=False)).hexdigest() != pin["sha256"]):
                raise Refused("immutable accepted suspension decision source differs")
            self.decision_verified = True

    def _read_at(self, physical):
        """Validate immutable bytes inside a maintained caller's local main sandwich.

        This performs no live authorization by itself. The caller must open and
        close a fresh main observation before accepting its invocation digest.
        """
        raw = self.file(physical, SUSPENSION_PATH)
        if raw is None:
            raise Refused("required release-specific suspension is absent")
        value = strict_suspension(raw)
        self.verify_decision()
        self.original(physical)
        digest = hashlib.sha256(raw).hexdigest()
        if self.digest is not None and self.digest != digest:
            raise Refused("release-specific suspension changed during invocation")
        return value, digest

    def read(self):
        physical = self.main()
        value, digest = self._read_at(physical)
        if self.main() != physical:
            raise Refused("Authority main moved during suspension read")
        self.digest = digest
        return value

    def authorize(self, logical_ref, version=None):
        if logical_ref not in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]}:
            if version in {"0.103.0", "0.104.0"}:
                raise Refused("sensitive release has a different journal binding")
            return
        self.read()
        if logical_ref == SUSPENDED_REF:
            raise Refused("original .103 continuation is prospectively suspended; Unknown retained")
        if version is not None and version != ALLOWED_SUCCESSOR["version"]:
            raise Refused("allowed successor version differs")


class ProtectedReleaseJournal:
    # One instance validates at most 128 logical generations. Physical history
    # shares trees/blobs; keep a finite cache without caching live authority.
    _MAX_IMMUTABLE_OBJECTS = 2048
    _MAX_IMMUTABLE_BYTES = 16 * 1024 * 1024
    _MAX_IMMUTABLE_OBJECT_BYTES = 1024 * 1024

    def __init__(self, api: GitAPI, ref: str = REF, *, main_directory: bool = False, suspension=None):
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
        if suspension is not None and (not isinstance(suspension, ReleaseSuspension)
                or suspension.api is not api or ref != ALLOWED_SUCCESSOR["logicalRef"] or not main_directory):
            raise Refused("shared successor immutable reader has a different owner or binding")
        self._suspension = suspension
        self._immutable_reader = suspension.objects if suspension is not None else None
        self._observed: Observed | None = None
        self._lineage_length = 0
        self._immutable_objects: dict[tuple[str, str], bytes] = {}
        self._immutable_bytes = 0

    def _authorize_release(self, version=None):
        if self.ref in {SUSPENDED_REF, ALLOWED_SUCCESSOR["logicalRef"]}:
            if not self.main_directory:
                raise Refused("sensitive release requires protected-main directory storage")
            if self._suspension is None:
                self._suspension = ReleaseSuspension(self.api)
            self._suspension.authorize(self.ref, version)
        elif version in {"0.103.0", "0.104.0"}:
            raise Refused("sensitive journal version has a different logical identity")

    def _immutable_object(self, kind: str, oid: str) -> dict:
        if self._immutable_reader is not None:
            # Same invocation/API/repository; delegate only SHA-addressed validated
            # bytes to the existing bounded cache. Never cache refs or admission.
            return self._immutable_reader._immutable_object(kind, oid)
        if kind not in {"commits", "trees", "blobs"} or not isinstance(oid, str) or not re.fullmatch(r"[0-9a-f]{40}", oid):
            raise Refused("physical journal object identity is invalid")
        key = kind, oid
        if key in self._immutable_objects:
            return json.loads(self._immutable_objects[key])
        value = self.api.get(f"repos/{REPOSITORY}/git/{kind}/{oid}")
        if not isinstance(value, dict) or value.get("sha") != oid:
            raise Refused("physical journal object identity differs")
        if kind == "commits" and (
            not isinstance(value.get("parents"), list) or len(value["parents"]) > 2
            or not isinstance(value.get("tree"), dict)
            or not isinstance(value["tree"].get("sha"), str)
            or not re.fullmatch(r"[0-9a-f]{40}", value["tree"]["sha"])
            or any(not isinstance(parent, dict) or not isinstance(parent.get("sha"), str)
                   or not re.fullmatch(r"[0-9a-f]{40}", parent["sha"]) for parent in value["parents"])
        ):
            raise Refused("physical journal commit is incomplete")
        if kind == "trees":
            if value.get("truncated") is not False or not isinstance(value.get("tree"), list):
                raise Refused("physical journal tree is incomplete")
            modes = {"tree": {"040000"}, "blob": {"100644", "100755", "120000"}, "commit": {"160000"}}
            for entry in value["tree"]:
                if (not isinstance(entry, dict) or not isinstance(entry.get("path"), str)
                        or not entry["path"] or "/" in entry["path"] or "\0" in entry["path"]
                        or not isinstance(entry.get("type"), str)
                        or entry["type"] not in modes or not isinstance(entry.get("mode"), str)
                        or entry["mode"] not in modes[entry["type"]]
                        or not isinstance(entry.get("sha"), str)
                        or not re.fullmatch(r"[0-9a-f]{40}", entry["sha"])):
                    raise Refused("physical journal tree entry is incomplete")
        if kind == "blobs":
            if value.get("encoding") != "base64" or not isinstance(value.get("content"), str):
                raise Refused("physical journal blob is incomplete")
            try:
                base64.b64decode(value["content"], validate=False)
            except ValueError as error:
                raise Refused("physical journal blob encoding differs") from error
        try:
            raw = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")
        except (ValueError, TypeError, UnicodeError) as error:
            raise Refused("physical journal object is not serializable") from error
        if (len(raw) > self._MAX_IMMUTABLE_OBJECT_BYTES
                or len(self._immutable_objects) >= self._MAX_IMMUTABLE_OBJECTS
                or self._immutable_bytes + len(raw) > self._MAX_IMMUTABLE_BYTES):
            raise Refused("physical journal immutable cache exceeds its bound")
        self._immutable_objects[key] = raw
        self._immutable_bytes += len(raw)
        return json.loads(raw)

    def _main_files(self, commit: dict) -> tuple[bytes, bytes] | None:
        tree_oid = commit["tree"]["sha"]
        for segment in self.directory.split("/"):
            tree = self._immutable_object("trees", tree_oid)
            if tree.get("truncated"):
                raise Refused("main journal tree is incomplete")
            matches = [entry for entry in tree.get("tree", []) if entry.get("path") == segment]
            if not matches:
                return None
            if len(matches) != 1 or matches[0].get("type") != "tree":
                raise Refused("main journal directory is invalid")
            tree_oid = matches[0]["sha"]
        tree = self._immutable_object("trees", tree_oid)
        entries = tree.get("tree", [])
        if tree.get("truncated") or {entry.get("path") for entry in entries} != {PATH, "release-head.txt"} or len(entries) != 2:
            raise Refused("main journal directory contains unexpected files")
        def raw(name: str) -> bytes:
            entry = next(entry for entry in entries if entry["path"] == name)
            if entry.get("type") != "blob" or entry.get("mode") != "100644":
                raise Refused("main journal entry is not a regular blob")
            blob = self._immutable_object("blobs", entry["sha"])
            if blob.get("sha") != entry["sha"] or blob.get("encoding") != "base64":
                raise Refused("main journal blob identity differs")
            return base64.b64decode(blob["content"], validate=False)
        return raw("release-head.txt"), raw(PATH)

    def _main_commit(self, oid: str) -> dict:
        value = self._immutable_object("commits", oid)
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
        # Main-directory history repeatedly binds the same logical Git objects.
        # Cache only immutable SHA-addressed bytes; refs/path history remain live.
        immutable = self._immutable_object if self.main_directory else lambda kind, sha: self.api.get(f"repos/{REPOSITORY}/git/{kind}/{sha}")
        commit = immutable("commits", oid)
        if commit.get("sha") != oid:
            raise Refused("journal commit identity differs")
        parents = commit.get("parents", [])
        if not isinstance(parents, list) or len(parents) > 1:
            raise Refused("journal commit has an invalid parent list")
        tree = immutable("trees", commit["tree"]["sha"])
        entries = tree.get("tree")
        if not isinstance(entries, list) or len(entries) != 1 or entries[0].get("path") != PATH:
            raise Refused("journal commit tree contains unexpected files")
        blob = immutable("blobs", entries[0]["sha"])
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
        self._authorize_release(intent.get("version"))
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
                self._authorize_release(intent.get("version"))
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
        self._authorize_release(intent.get("version"))
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
        # Only the current shared .104 caller joins overlapping observations at
        # one local boundary. Historical and original-release routes stay intact.
        joined = self.ref == ALLOWED_SUCCESSOR["logicalRef"] and self.main_directory and self._immutable_reader is not None
        if not joined:
            self._authorize_release()
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
        physical = self._physical_head
        if self.main_directory:
            if physical is None:
                raise Refused("main journal CAS has no physical prior read")
            # Provider readback and admission may outlast unrelated main writes.
            # Refresh the physical base without changing the admitted logical state.
            path = f"repos/{REPOSITORY}/git/ref/heads/main"
            fresh = self._suspension.main() if joined else self.api.get(path).get("object", {}).get("sha")
            if not isinstance(fresh, str) or not re.fullmatch(r"[0-9a-f]{40}", fresh):
                raise Refused("main journal CAS has no exact fresh physical head")
            logical = self._main_head(fresh)
            files = self._main_files(self._main_commit(fresh))
            if logical != observed.head or files != ((observed.head + "\n").encode("ascii"), canonical(observed.state)):
                raise Refused("main journal selected binding changed before CAS")
            if joined:
                if observed.state.get("version") != ALLOWED_SUCCESSOR["version"]:
                    raise Refused("allowed successor version differs")
                _, digest = self._suspension._read_at(fresh)
            closing = self._suspension.main() if joined else self.api.get(path).get("object", {}).get("sha")
            if closing != fresh:
                raise Refused("main journal moved during CAS freshness read")
            if joined:
                self._suspension.digest = digest
            physical = fresh
            self._physical_head = physical
        if not joined:
            self._authorize_release(observed.state.get("version"))
        commit = self._create_commit(next_state, [observed.head])
        physical_commit = commit
        if self.main_directory:
            physical_commit = self._overlay(physical, commit, next_state)
            # Do not PATCH a stale overlay or retry after a new physical write.
            before_patch = self._suspension.main() if joined else self.api.get(path).get("object", {}).get("sha")
            if before_patch != physical:
                raise Refused("main journal moved before CAS PATCH")
            if joined:
                # New post-create sandwich; no pre-create authorization survives
                # the intervening POSTs. The same parent ties selected bytes.
                _, digest = self._suspension._read_at(before_patch)
                if self._suspension.main() != before_patch:
                    raise Refused("main journal moved during final CAS suspension read")
                self._suspension.digest = digest
        if not joined:
            self._authorize_release(observed.state.get("version"))
        try:
            self.api.patch(
                f"repos/{REPOSITORY}/git/refs/{self.physical_ref.removeprefix('refs/')}",
                {"sha": physical_commit, "force": False},
            )
        except Exception as error:
            raise Refused(f"journal CAS response uncertain; reread before further effects: {error}") from error
        self.read()
        return self._observed is not None and self._observed.head == commit and self._observed.state == next_state

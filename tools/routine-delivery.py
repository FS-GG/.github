#!/usr/bin/env python3
"""Inspect one routine PR and report its native delivery result exactly once."""

from __future__ import annotations

import argparse
import base64
import math
import hashlib
import importlib.util
import io
import json
import pathlib
import os
import selectors
import stat
import time
import re
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import asdict, dataclass, fields, replace
from datetime import datetime, timedelta, timezone
from typing import Any, Callable, Protocol

sys.dont_write_bytecode = True

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
MAX_SELECTION_ARCHIVE_BYTES = 1_048_576
MAX_SELECTION_UNCOMPRESSED_BYTES = 1_048_576
MAX_SELECTION_ENTRIES = 128
MAX_SELECTION_BYTES = 65_536
PRODUCER_REGISTRATION = "evidence/github-substrate-v2/gs2-06-7/execution-pins-extension.json"
TEMPLATES_ASSERTION_PIN = "tests/composition/lib/skill-union.sh"
ASSERTION_BUNDLE = "dist/skill-union-assert.sh"
PRODUCER_REPOSITORY = "FS-GG/.github"
PRODUCER_PATHS = (
    ".fsgg/routine-development.json", ".github/workflows/routine-eligibility.yml",
    "scripts/check-claim-generation.py", "scripts/check-routine-eligibility-envelope.py",
    "scripts/lib/gate.py",
)

BAR_REPOSITORY = "FS-GG/FSBarV2"
BAR_REPOSITORY_ID = 1393512699
BAR_PREARM = ".github/workflows/prearm-source-controls.yml"
BAR_PREARM_ID = 374109740
BAR_PREARM_BASE_SHA256 = "9e8d89d5b6ab434a13765980dfdf58f53a734e41b07357c3e7318a68f1f36274"
BAR_PREARM_TECHNICAL_STEPS = (
    "Set up job", "Check out the exact candidate", "Bind the checked-out candidate",
    "Set up the exact .NET SDK", "Set up the exact Node.js runtime",
    "Run the fourteen JavaScript source controls", "Run the one-unit and compiler-capture source controls",
    "Restore and build the live boundary source", "Run exactly three synthetic compiled controls",
)
BAR_PREARM_NATIVE_TAIL = ("Post Set up the exact Node.js runtime", "Post Set up the exact .NET SDK",
                         "Post Check out the exact candidate", "Complete job")


def installation_bar_workflow(base: bytes) -> bytes:
    """Derive only the five literal path additions from the authenticated BAR job."""
    if hashlib.sha256(base).hexdigest() != BAR_PREARM_BASE_SHA256:
        raise RuntimeError("installation BAR prearm baseline is not the reviewed source")
    boundary = b"  workflow_dispatch:\n"
    if base.count(boundary) != 1 or any(("      - " + path + "\n").encode() in base for path in PRODUCER_PATHS):
        raise RuntimeError("installation BAR prearm path-list boundary is ambiguous")
    additions = b"".join(("      - " + path + "\n").encode() for path in PRODUCER_PATHS)
    return base.replace(boundary, additions + boundary, 1)


def installation_registration(base: bytes, workflow: bytes) -> bytes:
    """Append only the derived workflow record while preserving every original byte."""
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise RuntimeError("installation registration has duplicate JSON keys")
            result[key] = value
        return result
    try:
        text = base.decode("utf-8")
        decoder = json.JSONDecoder(object_pairs_hook=unique)
        value = json.loads(text, object_pairs_hook=unique)
    except (UnicodeError, ValueError) as error:
        raise RuntimeError("installation registration is malformed") from error
    if (not isinstance(value, dict) or set(value) != {"schema", "complete", "documents"}
            or value["schema"] != "fsgg.coordination.execution-pins-extension/1"
            or value["complete"] is not True or not isinstance(value["documents"], list)
            or len(value["documents"]) > 128):
        raise RuntimeError("installation registration schema is incomplete")
    paths = set()
    for row in value["documents"]:
        if (not isinstance(row, dict) or set(row) != {"kind", "path", "sha256", "externalReferences", "localReferences"}
                or row["kind"] not in ("workflow", "action")
                or not isinstance(row["path"], str) or not row["path"] or len(row["path"]) > 1024
                or row["path"] in paths or row["path"] == PRODUCER_PATHS[1]
                or not isinstance(row["sha256"], str) or not re.fullmatch("[0-9a-f]{64}", row["sha256"])
                or any(not isinstance(row[key], list) or len(row[key]) > 128
                       or any(not isinstance(ref, str) or not ref or len(ref) > 1024 for ref in row[key])
                       for key in ("externalReferences", "localReferences"))):
            raise RuntimeError("installation registration document differs or already exists")
        paths.add(row["path"])
    if re.search(rb"(?m)^\s*(?:-\s*)?uses:\s*", workflow):
        raise RuntimeError("canonical installation workflow references are not empty")
    record = dict(kind="workflow", path=PRODUCER_PATHS[1], sha256=hashlib.sha256(workflow).hexdigest(),
                  externalReferences=[], localReferences=[])
    # Locate the top-level documents array with the same decoder; never rewrite old records.
    cursor = text.index("{") + 1
    while True:
        cursor += len(text[cursor:]) - len(text[cursor:].lstrip())
        key, cursor = decoder.raw_decode(text, cursor)
        cursor += len(text[cursor:]) - len(text[cursor:].lstrip())
        if text[cursor] != ":":
            raise RuntimeError("installation registration key separator differs")
        cursor += 1
        cursor += len(text[cursor:]) - len(text[cursor:].lstrip())
        _, end = decoder.raw_decode(text, cursor)
        if key == "documents":
            insertion = end - 1
            appended = ("," if value["documents"] else "") + "\n" + json.dumps(record, indent=2) + "\n"
            return (text[:insertion] + appended + text[insertion:]).encode("utf-8")
        cursor = end
        cursor += len(text[cursor:]) - len(text[cursor:].lstrip())
        if text[cursor] != ",":
            raise RuntimeError("installation registration documents array is missing")
        cursor += 1


class AmbiguousWrite(RuntimeError):
    """The merge request may have reached GitHub, so readback must decide."""


class NativeMergeRefused(RuntimeError):
    """GitHub synchronously refused the head-conditioned merge request."""


@dataclass(frozen=True)
class TelemetryConfig:
    path: str
    store_root: str
    engine: str
    repository: str | None
    workspace: bool


def _compiled_json(
    command: list[str], *, runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> tuple[int, dict[str, Any]]:
    try:
        completed = runner(
            command, check=False, capture_output=True, text=True, timeout=25,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise RuntimeError("compiled telemetry helper is unavailable") from error
    if len(completed.stdout.encode("utf-8")) > 16 * 1024:
        raise RuntimeError("compiled telemetry helper returned an oversized response")
    try:
        value = json.loads(completed.stdout)
    except json.JSONDecodeError as error:
        raise RuntimeError("compiled telemetry helper returned invalid JSON") from error
    if not isinstance(value, dict):
        raise RuntimeError("compiled telemetry helper returned a non-object response")
    return completed.returncode, value


def discover_telemetry_config(
    explicit: str | None, *, command_engine: str = "fsgg-coord-engine",
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> TelemetryConfig | None:
    command = [command_engine, "skill", "telemetry-config", "discover"]
    if explicit is not None:
        command.extend(["--config", explicit])
    code, value = _compiled_json(command, runner=runner)
    if value == {"schema": "fsgg.telemetry.config-discovery/1", "status": "not-configured"}:
        if code != 2:
            raise RuntimeError("compiled telemetry discovery returned an invalid status")
        return None
    expected = {"schema", "status", "configPath", "storeRoot", "engine", "repository", "workspace"}
    if code != 0 or set(value) != expected or value.get("schema") != "fsgg.telemetry.config-discovery/1" or value.get("status") != "configured":
        raise RuntimeError("compiled telemetry configuration discovery refused")
    path, store, engine = value.get("configPath"), value.get("storeRoot"), value.get("engine")
    repository, workspace = value.get("repository"), value.get("workspace")
    if (not isinstance(path, str) or not pathlib.Path(path).is_absolute()
            or not isinstance(store, str) or not pathlib.Path(store).is_absolute()
            or not isinstance(engine, str) or not engine
            or (repository is not None and not isinstance(repository, str))
            or not isinstance(workspace, bool)):
        raise RuntimeError("compiled telemetry configuration discovery returned an invalid projection")
    return TelemetryConfig(path, store, engine, repository, workspace)


def create_ci_assignment(
    config: TelemetryConfig, *, feature: str, item: str, attempt: str,
    parent_attempt: str | None, command_engine: str = "fsgg-coord-engine",
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> str:
    command = [command_engine, "skill", "roadmap-telemetry", "--config", config.path,
               "ci-assignment", "--feature", feature, "--item", item,
               "--attempt", attempt, "--producer", "routine-delivery"]
    if parent_attempt is not None:
        command.extend(["--parent-attempt", parent_attempt])
    code, value = _compiled_json(command, runner=runner)
    if (code != 0 or set(value) != {"schema", "status", "assignment"}
            or value.get("schema") != "fsgg.telemetry.assignment-result/1"
            or value.get("status") != "ready"
            or not isinstance(value.get("assignment"), str)
            or not pathlib.Path(value["assignment"]).is_absolute()):
        raise RuntimeError("compiled telemetry assignment creation refused")
    return value["assignment"]


@dataclass(frozen=True)
class MergeEffectRequest:
    """Exact native pull-request merge inputs established by fresh readback."""

    repository: str
    pullRequest: int
    expectedHead: str
    baseRef: str
    baseSha: str
    mergeMethod: str


def merge_effect_request(
    repo: str,
    pr: int,
    head: str,
    base_ref: str,
    base_sha: str,
    method: str,
) -> MergeEffectRequest:
    return MergeEffectRequest(
        repository=repo,
        pullRequest=pr,
        expectedHead=head,
        baseRef=base_ref,
        baseSha=base_sha,
        mergeMethod=method,
    )


class NativeApi(Protocol):
    def get_pr(self, repo: str, pr: int) -> dict[str, Any]: ...

    def merge(self, request: MergeEffectRequest) -> dict[str, Any]: ...

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict[str, Any]]: ...

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None: ...

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict[str, Any]]: ...

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None: ...

    def installation_contract(self, repo: str, number: int, pr: dict, head: str,
                              source: str, *, delivered: bool = False, coherent_workflow: str | None = None) -> None: ...


SC2_REPOSITORY = "FS-GG/FS.GG.SC2.Client"
SC2_REPOSITORY_ID = 1390567199
SC2_VERIFY_ID = 368229845
SC2_VERIFY_SHA256 = "561cc8b0ce4d0460518b5344dc2e553caee2083c5f23d8cbe5b402c8902ba06b"
SC2_VERIFY_ENTRY_SHA256 = "5a7228d85f2ca4bcdbf4534fc45cd0c66ed3ed80077d5a15a4ad66bdbcbd176a"

FOURD_REPOSITORY = "FS-GG/FS.GG.FourD"
FOURD_REPOSITORY_ID = 1390568106
FOURD_VERIFY_ID = 368233881
FOURD_VERIFY_SHA256 = "ce73c0de5b7bb12a5ec3e9e5ffd2e872efc86a7193ec0b7319803f5dd7c97a40"
FOURD_VERIFY_ENTRY_SHA256 = "1b440bf8a390fcfcd3d07f405117e397e27e715a526e48630f3e1b3b337dd4eb"
FOURD_SOURCE = ".github/workflows/portable-workspace-source-preparation.yml"
FOURD_SOURCE_ID = 371620519
FOURD_SOURCE_BASE_SHA256 = "1d5e2eef8e1567ea60f50fdbfaa02c3a06c65568635456131759361c01a6941d"
FOURD_INVENTORY_TEST = "tests/portable-workspace/test_native_image.py"
FOURD_INVENTORY_BASE_SHA256 = "01e570fb53469c709b09a1e8a0e889097ebbc738a0a5db13babb7f0fa96cf84e"
FOURD_INVENTORY_DERIVED_SHA256 = "d698f8d0efac398e2b24b3809bea78f60ab94fd41e4d28c22d89a8ff6f093632"
FOURD_SOURCE_ENTRY_SHA256 = "c3ab08d1509cb9f6aeb227d8476250e85fa3c8579ee4df0a0db40f55a8857be9"
FOURD_POLICY_ACTOR_ID = 1645484
FOURD_POLICY_COMMENTS = {
    6335828296: "f016ba866ccd21725fec14ee698b2adbdb5fdc55d2f5701582aca4faf1f6c9de",
    6335951089: "fddc33a04fb9e0a6d9a871c91649ac5132f39a8b4a7d8c81167cb78d9ffc7326",
}
FOURD_PLAN_MESSAGE = "Upgrade to GitHub Pro or make this repository public to enable this feature."
FOURD_POLICY_ROUTES = {
    "branches/main/protection/required_status_checks": "https://docs.github.com/rest/branches/branch-protection#get-status-checks-protection",
    "rules/branches/main?per_page=100": "https://docs.github.com/rest/repos/rules#get-rules-for-a-branch",
    "rulesets?includes_parents=true&per_page=100": "https://docs.github.com/rest/repos/rules#get-all-repository-rulesets",
}
FOURD_VERIFY_STEPS = (
    "Set up job", "Check out the candidate", "Set up the exact .NET SDK", "Set up Node.js",
    "Install the locked browser harness and Chromium", "Restore the test project",
    "Verify .NET, emitted Fable, Node, and Chromium", "Verify retained readers and immutable corpus",
    "Verify FABLE-ADOPT-01.5a shared-package adapters", "Verify the shared runtime closure controls",
    "Verify deterministic archive and extracted product", "Verify old saves against the same extracted archive",
    "Retain the exact qualified evaluation archive",
)
FOURD_SOURCE_STEPS = ("Set up job", "Check out candidate source", "Validate FourD portable source preparation")
FOURD_VERIFY_TAIL = ("Post Set up Node.js", "Post Set up the exact .NET SDK", "Post Check out the candidate", "Complete job")
FOURD_SOURCE_TAIL = ("Post Check out candidate source", "Complete job")

def installation_fourd_workflow(base: bytes) -> bytes:
    """Only add the canonical paths to both authenticated FourD trigger filters."""
    if hashlib.sha256(base).hexdigest() != FOURD_SOURCE_BASE_SHA256:
        raise RuntimeError("installation FourD source workflow baseline differs")
    boundary = b"    paths:\n"
    additions = b"".join(("      - " + path + "\n").encode() for path in PRODUCER_PATHS)
    if base.count(boundary) != 2 or any(("      - " + path + "\n").encode() in base for path in PRODUCER_PATHS):
        raise RuntimeError("installation FourD trigger filters are ambiguous")
    return base.replace(boundary, boundary + additions)


def installation_fourd_inventory(base: bytes) -> bytes:
    """Preserve all guards except the exact canonical fourth workflow allowance."""
    if hashlib.sha256(base).hexdigest() != FOURD_INVENTORY_BASE_SHA256:
        raise RuntimeError("installation FourD inventory baseline differs")
    old, new = b'                    self.assertIn("runs-on: ubuntu-24.04", text)', b'                    if name == ".github/workflows/routine-eligibility.yml":\n                        self.assertEqual([line.strip() for line in text.splitlines() if line.lstrip().startswith("runs-on:")], ["runs-on: ubuntu-latest"])\n                    else:\n                        self.assertIn("runs-on: ubuntu-24.04", text)'
    if base.count(old) != 1:
        raise RuntimeError("installation FourD inventory boundary is ambiguous")
    base = base.replace(old, new, 1)
    old, new = b'        self.assertEqual(set(self.workflows), {"fourd-native-image-qualification.yml", "portable-workspace-source-preparation.yml", "verify.yml"})', b'        self.assertEqual(set(self.workflows), {"fourd-native-image-qualification.yml", "portable-workspace-source-preparation.yml", "verify.yml", "routine-eligibility.yml"})'
    if base.count(old) != 1:
        raise RuntimeError("installation FourD inventory boundary is ambiguous")
    base = base.replace(old, new, 1)
    return base



class GhApi:
    def installation_read(self, endpoint: str) -> Any:
        """Bounded existing transport; permission/transport errors never mean absence."""
        code, raw = bounded_watch_query(["gh", "api", endpoint], time.monotonic() + 31,
                                       max_bytes=1_048_576)
        if code != 0:
            raise RuntimeError("routine producer native identity read failed")
        try:
            return json.loads(raw)
        except (UnicodeDecodeError, ValueError) as error:
            raise RuntimeError("routine producer native read is malformed") from error

    def installation_tree(self, repo: str, commit: str) -> dict[str, dict]:
        value = self.installation_read(f"repos/{repo}/git/commits/{commit}")
        if not isinstance(value, dict) or value.get("sha") != commit:
            raise RuntimeError("installation commit identity differs")
        tree_sha = (value.get("tree") or {}).get("sha")
        if not isinstance(tree_sha, str) or not SHA_RE.fullmatch(tree_sha):
            raise RuntimeError("installation tree identity is missing")
        tree = self.installation_read(f"repos/{repo}/git/trees/{tree_sha}?recursive=1")
        if (not isinstance(tree, dict) or tree.get("sha") != tree_sha
                or tree.get("truncated") is not False or not isinstance(tree.get("tree"), list)
                or len(tree["tree"]) > 10000):
            raise RuntimeError("installation tree population is incomplete")
        entries = {}
        for row in tree["tree"]:
            if (not isinstance(row, dict) or not isinstance(row.get("path"), str)
                    or row["path"] in entries or not row["path"]
                    or any(p in ("", ".", "..") for p in row["path"].split("/"))
                    or not isinstance(row.get("sha"), str) or not SHA_RE.fullmatch(row["sha"])
                    or row.get("type") not in ("tree", "blob", "commit")
                    or row.get("mode") not in {"tree": ("040000",), "blob": ("100644", "100755", "120000"), "commit": ("160000",)}[row["type"]]):
                raise RuntimeError("installation tree entry is malformed")
            entries[row["path"]] = row
        return entries

    def installation_paths(self, repo: str, commit: str, paths: tuple[str, ...]) -> dict[str, dict]:
        """Read only authenticated ancestor directories of the fixed installation closure."""
        if (not isinstance(commit, str) or not SHA_RE.fullmatch(commit)
                or not 1 <= len(paths) <= 8 or len(set(paths)) != len(paths)
                or any(not isinstance(path, str) or not 1 <= len(path.split("/")) <= 5
                       or any(part in ("", ".", "..") for part in path.split("/")) for path in paths)):
            raise RuntimeError("installation selected path population is malformed")
        value = self.installation_read(f"repos/{repo}/git/commits/{commit}")
        root = (value.get("tree") or {}).get("sha") if isinstance(value, dict) else None
        if (not isinstance(value, dict) or value.get("sha") != commit
                or not isinstance(root, str) or not SHA_RE.fullmatch(root)):
            raise RuntimeError("installation selected commit/root identity differs")
        cache: dict[str, dict[str, dict]] = {}
        def directory(sha: str) -> dict[str, dict]:
            if sha in cache:
                return cache[sha]
            tree = self.installation_read(f"repos/{repo}/git/trees/{sha}")
            if (not isinstance(tree, dict) or tree.get("sha") != sha
                    or tree.get("truncated") is not False or not isinstance(tree.get("tree"), list)
                    or len(tree["tree"]) > 10000):
                raise RuntimeError("installation selected directory population is incomplete")
            entries = {}
            for row in tree["tree"]:
                if (not isinstance(row, dict) or not isinstance(row.get("path"), str)
                        or not row["path"] or "/" in row["path"] or row["path"] in (".", "..")
                        or row["path"] in entries or not isinstance(row.get("sha"), str)
                        or not SHA_RE.fullmatch(row["sha"]) or row.get("type") not in ("tree", "blob", "commit")
                        or row.get("mode") not in {"tree": ("040000",), "blob": ("100644", "100755", "120000"),
                                                   "commit": ("160000",)}[row["type"]]):
                    raise RuntimeError("installation selected directory entry is malformed")
                entries[row["path"]] = row
            cache[sha] = entries
            return entries
        selected = {}
        for path in paths:
            sha = root
            parts = path.split("/")
            for index, part in enumerate(parts):
                row = directory(sha).get(part)
                if row is None:
                    raise RuntimeError("installation selected path is missing")
                if index == len(parts) - 1:
                    if row.get("type") != "blob" or row.get("mode") not in ("100644", "100755"):
                        raise RuntimeError("installation selected leaf is not a regular blob")
                    selected[path] = dict(row, path=path)
                else:
                    if row.get("type") != "tree" or row.get("mode") != "040000":
                        raise RuntimeError("installation selected ancestor is not a regular tree")
                    sha = row["sha"]
        return selected

    def installation_blob(self, repo: str, row: dict, *, max_bytes: int = 131072) -> bytes:
        if row.get("type") != "blob" or row.get("mode") not in ("100644", "100755"):
            raise RuntimeError("installation payload is not a regular canonical file")
        value = self.installation_read(f"repos/{repo}/git/blobs/{row['sha']}")
        if (not isinstance(value, dict) or value.get("sha") != row["sha"]
                or value.get("encoding") != "base64" or type(value.get("size")) is not int
                or not 0 < value["size"] <= max_bytes or not isinstance(value.get("content"), str)):
            raise RuntimeError("installation blob identity/encoding is malformed")
        try:
            body = base64.b64decode(value["content"].replace("\n", ""), validate=True)
        except ValueError as error:
            raise RuntimeError("installation blob encoding is malformed") from error
        if (len(body) != value["size"] or hashlib.sha1(
                f"blob {len(body)}\0".encode() + body).hexdigest() != row["sha"]):
            raise RuntimeError("installation blob bytes differ from Git identity")
        return body

    def installation_fourd_inventory_blob(self, repo: str, path: str, row: dict, *, derived: bool) -> bytes:
        """Bound only the pinned FourD inventory file above the ordinary blob limit."""
        if repo != FOURD_REPOSITORY or path != FOURD_INVENTORY_TEST or row.get("mode") != "100644":
            raise RuntimeError("FourD inventory read path/receiver/mode differs")
        body = self.installation_blob(repo, row, max_bytes=262144)
        digest = FOURD_INVENTORY_DERIVED_SHA256 if derived else FOURD_INVENTORY_BASE_SHA256
        if hashlib.sha256(body).hexdigest() != digest:
            raise RuntimeError("FourD inventory read digest differs")
        return body

    def installation_source(self, repo: str, head: str, base_ref: str, base: str,
                            source: str, *, delivered: bool = False) -> dict:
        """Authenticate the pre-PR source delta."""
        if not all(isinstance(value, str) and SHA_RE.fullmatch(value) for value in (head, base, source)):
            raise RuntimeError("source revision malformed")
        metadata = self.installation_read(f"repos/{repo}")
        if (not isinstance(metadata, dict) or metadata.get("full_name") != repo
                or type(metadata.get("id")) is not int or metadata["id"] < 1
                or metadata.get("default_branch") != base_ref):
            raise RuntimeError("source receiver/default differs")
        default = self.installation_read(f"repos/{repo}/git/ref/heads/{base_ref}")
        current = (default.get("object") or {}).get("sha") if isinstance(default, dict) else None
        if not delivered and current != base:
            raise RuntimeError("installation protected base moved")
        owner = self.installation_read(f"repos/{PRODUCER_REPOSITORY}")
        if (not isinstance(owner, dict) or owner.get("full_name") != PRODUCER_REPOSITORY
                or owner.get("id") != 1269292704 or owner.get("default_branch") != "main"):
            raise RuntimeError("canonical installation producer repository differs")
        owner_ref = self.installation_read(f"repos/{PRODUCER_REPOSITORY}/git/ref/heads/main")
        owner_head = (owner_ref.get("object") or {}).get("sha") if isinstance(owner_ref, dict) else None
        if (not isinstance(owner_ref, dict) or owner_ref.get("ref") != "refs/heads/main"
                or (owner_ref.get("object") or {}).get("type") != "commit"
                or not isinstance(owner_head, str) or not SHA_RE.fullmatch(owner_head)):
            raise RuntimeError("current protected producer identity is missing")
        lineage = self.installation_read(f"repos/{PRODUCER_REPOSITORY}/compare/{source}...{owner_head}")
        if (not isinstance(lineage, dict) or (lineage.get("merge_base_commit") or {}).get("sha") != source
                or (lineage.get("base_commit") or {}).get("sha") != source
                or lineage.get("status") not in ("identical", "ahead")):
            raise RuntimeError("installation producer is not a protected-main ancestor")
        canonical = self.installation_paths(PRODUCER_REPOSITORY, source,
            PRODUCER_PATHS + ((ASSERTION_BUNDLE,) if repo == "FS-GG/FS.GG.Templates" else ()))
        def protected_owner_still_current():
            latest = self.installation_read(f"repos/{PRODUCER_REPOSITORY}/git/ref/heads/main")
            if (not isinstance(latest, dict) or latest.get("ref") != "refs/heads/main"
                    or (latest.get("object") or {}).get("type") != "commit"
                    or (latest.get("object") or {}).get("sha") != owner_head):
                raise RuntimeError("protected producer moved")
        current_owner = self.installation_paths(PRODUCER_REPOSITORY, owner_head,
            (*PRODUCER_PATHS, "tools/routine-delivery.py", "src/FS.GG.Drivers/workspace-inventory.json"))
        inventory_row = current_owner.get("src/FS.GG.Drivers/workspace-inventory.json")
        if inventory_row is None:
            raise RuntimeError("maintained driver workspace inventory is missing")
        inventory = json.loads(self.installation_blob(PRODUCER_REPOSITORY, inventory_row))
        if (not isinstance(inventory, dict) or inventory.get("schema") != "fsgg/driver-workspace-inventory/v1"
                or not isinstance(inventory.get("files"), list)
                or any(sum(isinstance(row, dict) and row.get("source") == path and row.get("path") == path
                           for row in inventory["files"]) != 1 for path in PRODUCER_PATHS)):
            raise RuntimeError("first-install closure differs from maintained workspace inventory")
        for path in PRODUCER_PATHS:
            if (path not in canonical or path not in current_owner
                    or any(canonical[path].get(k) != current_owner[path].get(k) for k in ("sha", "mode", "type"))):
                raise RuntimeError("selected producer payload is obsolete at protected main")
        helper = current_owner.get("tools/routine-delivery.py")
        executing = pathlib.Path(__file__).read_bytes()
        # Self-correspondence reads exactly this helper's size; other blobs keep their 128 KiB bound.
        if helper is None or self.installation_blob(PRODUCER_REPOSITORY, helper, max_bytes=len(executing)) != executing:
            raise RuntimeError("running installation helper differs from current protected producer")
        old = self.installation_tree(repo, base)
        candidate = self.installation_tree(repo, head)
        if any(path in old for path in PRODUCER_PATHS):
            raise RuntimeError("producer dependencies already or partially installed")
        leaves = lambda tree: {p: (r.get("mode"), r.get("type"), r.get("sha"))
                               for p, r in tree.items() if r.get("type") != "tree"}
        a, b = leaves(old), leaves(candidate)
        changed = {p for p in a.keys() | b.keys() if a.get(p) != b.get(p)}
        expected = set(PRODUCER_PATHS)
        if PRODUCER_REGISTRATION in changed:
            if repo != "FS-GG/FS.GG.Coordination" or metadata["id"] != 1346720714:
                raise RuntimeError("derived installation registration belongs only to the native Coordination receiver")
            original, registered = old.get(PRODUCER_REGISTRATION), candidate.get(PRODUCER_REGISTRATION)
            if (original is None or registered is None or original.get("type") != "blob"
                    or original.get("mode") != "100644" or registered.get("mode") != "100644"
                    or registered.get("type") != "blob"):
                raise RuntimeError("installation registration must modify an existing regular file without mode change")
            derived = installation_registration(self.installation_blob(repo, original),
                self.installation_blob(PRODUCER_REPOSITORY, canonical[PRODUCER_PATHS[1]]))
            if self.installation_blob(repo, registered) != derived:
                raise RuntimeError("installation registration must append exactly the derived workflow record")
            expected.add(PRODUCER_REGISTRATION)
        if TEMPLATES_ASSERTION_PIN in changed:
            if repo != "FS-GG/FS.GG.Templates" or metadata["id"] != 1281961814:
                raise RuntimeError("assertion pin refresh belongs only to the native Templates receiver")
            original, refreshed = old.get(TEMPLATES_ASSERTION_PIN), candidate.get(TEMPLATES_ASSERTION_PIN)
            if (original is None or refreshed is None or original.get("type") != "blob"
                    or original.get("mode") not in ("100644", "100755")
                    or refreshed.get("type") != "blob" or refreshed.get("mode") != original["mode"]):
                raise RuntimeError("assertion pin refresh must modify an existing regular file without mode change")
            body = self.installation_blob(repo, original)
            assignments = list(re.finditer(rb"^[ \t]*(?:export[ \t]+)?SKILL_ASSERT_REF[ \t]*=.*$", body, re.MULTILINE))
            if len(assignments) != 1 or not re.fullmatch(rb'SKILL_ASSERT_REF="[0-9a-f]{40}"', assignments[0].group()):
                raise RuntimeError("assertion pin refresh requires exactly one literal full-SHA assignment")
            assignment = assignments[0]
            old_pin = assignment.group()[len(b'SKILL_ASSERT_REF="'):-1].decode("ascii")
            # Compare metadata is invariant across pages; page two omits the potentially
            # large first-page historical file diff. Commit/tree/blob identity is read below.
            old_lineage = self.installation_read(f"repos/{PRODUCER_REPOSITORY}/compare/{old_pin}...{source}?per_page=1&page=2")
            if (not isinstance(old_lineage, dict) or (old_lineage.get("base_commit") or {}).get("sha") != old_pin
                    or (old_lineage.get("merge_base_commit") or {}).get("sha") != old_pin
                    or old_lineage.get("status") not in ("identical", "ahead")):
                raise RuntimeError("old assertion pin is not a selected protected producer ancestor")
            pinned = self.installation_paths(PRODUCER_REPOSITORY, old_pin, (ASSERTION_BUNDLE,))
            before_bundle, selected_bundle = pinned.get(ASSERTION_BUNDLE), canonical.get(ASSERTION_BUNDLE)
            if (before_bundle is None or selected_bundle is None
                    or before_bundle.get("type") != "blob" or before_bundle.get("mode") not in ("100644", "100755")
                    or any(before_bundle.get(k) != selected_bundle.get(k) for k in ("sha", "mode", "type"))
                    or self.installation_blob(PRODUCER_REPOSITORY, before_bundle)
                    != self.installation_blob(PRODUCER_REPOSITORY, selected_bundle)):
                raise RuntimeError("assertion pin refresh changes the authenticated assertion bundle")
            derived = body[:assignment.start()] + b'SKILL_ASSERT_REF="' + source.encode("ascii") + b'"' + body[assignment.end():]
            if self.installation_blob(repo, refreshed) != derived:
                raise RuntimeError("assertion pin refresh must preserve every byte except the selected source SHA")
            expected.add(TEMPLATES_ASSERTION_PIN)
        if BAR_PREARM in changed:
            if repo != BAR_REPOSITORY or metadata["id"] != BAR_REPOSITORY_ID:
                raise RuntimeError("derived prearm edit belongs only to the native BAR receiver")
            original, expanded = old.get(BAR_PREARM), candidate.get(BAR_PREARM)
            if (original is None or expanded is None or original.get("type") != "blob" or original.get("mode") != "100644"
                    or expanded.get("type") != "blob" or expanded.get("mode") != "100644"):
                raise RuntimeError("installation BAR prearm must modify an existing regular100644 file")
            if self.installation_blob(repo, expanded) != installation_bar_workflow(self.installation_blob(repo, original)):
                raise RuntimeError("installation BAR prearm must append exactly the five canonical paths")
            expected.add(BAR_PREARM)
        if repo == BAR_REPOSITORY and BAR_PREARM not in changed:
            raise RuntimeError("installation BAR requires the reviewed derived sixth prearm edit")
        if repo == FOURD_REPOSITORY:
            if metadata["id"] != FOURD_REPOSITORY_ID or metadata.get("private") is not True or base_ref != "main":
                raise RuntimeError("installation FourD receiver identity differs")
            for path, derive in ((FOURD_SOURCE, installation_fourd_workflow),
                                 (FOURD_INVENTORY_TEST, installation_fourd_inventory)):
                original, expanded = old.get(path), candidate.get(path)
                if (path not in changed or original is None or expanded is None
                        or any(row.get("type") != "blob" or row.get("mode") != "100644"
                               for row in (original, expanded))):
                    raise RuntimeError("installation FourD requires both exact derived regular-file edits")
                if path == FOURD_INVENTORY_TEST:
                    before = self.installation_fourd_inventory_blob(repo, path, original, derived=False)
                    after = self.installation_fourd_inventory_blob(repo, path, expanded, derived=True)
                else:
                    before, after = self.installation_blob(repo, original), self.installation_blob(repo, expanded)
                if after != derive(before):
                    raise RuntimeError("installation FourD derived bytes differ")
                expected.add(path)
        if changed != expected:
            raise RuntimeError("installation must add canonical five files with only a receiver-specific derived sixth edit")
        for path in PRODUCER_PATHS:
            if path not in canonical or path not in candidate or leaves(candidate)[path] != leaves(canonical).get(path):
                raise RuntimeError("installation payload mode/content differs from protected producer")
            self.installation_blob(PRODUCER_REPOSITORY, canonical[path])
            self.installation_blob(repo, candidate[path])
        workflows = self.installation_read(f"repos/{repo}/actions/workflows?per_page=100")
        if (not isinstance(workflows, dict) or type(workflows.get("total_count")) is not int
                or not 0 <= workflows["total_count"] <= 100 or not isinstance(workflows.get("workflows"), list)
                or len(workflows["workflows"]) != workflows["total_count"]
                or any(not isinstance(row, dict) for row in workflows["workflows"])):
            raise RuntimeError("source workflow population incomplete")
        if not delivered and any(row.get("path") == PRODUCER_PATHS[1] or row.get("name") == "routine-eligibility"
                                 for row in workflows["workflows"]):
            raise RuntimeError("native eligibility producer already exists")
        protected_owner_still_current()
        return dict(metadata=metadata, current=current, candidate=candidate, expected=expected,
                    leaves=leaves, protected_owner_still_current=protected_owner_still_current)

    def installation_contract(self, repo: str, number: int, pr: dict, head: str,
                              source: str, *, delivered: bool = False, coherent_workflow: str | None = None) -> None:
        try:
            self._installation_contract(repo, number, pr, head, source, delivered=delivered, coherent_workflow=coherent_workflow)
        except (KeyError, TypeError, ValueError, AttributeError, IndexError) as error:
            raise RuntimeError("installation native response shape is malformed") from error

    def _installation_contract(self, repo: str, number: int, pr: dict, head: str,
                               source: str, *, delivered: bool = False, coherent_workflow: str | None = None) -> None:
        """Closed canonical payload with receiver-specific derived sixth-file edits."""
        metadata = self.installation_read(f"repos/{repo}")
        if (not isinstance(metadata, dict) or metadata.get("full_name") != repo
                or type(metadata.get("id")) is not int or metadata["id"] < 1
                or not isinstance(metadata.get("default_branch"), str)):
            raise RuntimeError("installation repository identity is unestablished")
        base_ref, base = base_of(pr)
        if (base_ref != metadata["default_branch"] or base is None
                or head_of(pr) != head or pr.get("changed_files") not in ((7,) if repo == FOURD_REPOSITORY else (5, 6))
                or pr.get("draft") is not False
                or pr.get("number") != number or (pr.get("base") or {}).get("repo", {}).get("id") != metadata["id"]
                or type((pr.get("base") or {}).get("repo", {}).get("id")) is not int
                or type((pr.get("head") or {}).get("repo", {}).get("id")) is not int
                or (pr.get("head") or {}).get("repo", {}).get("id") != metadata["id"]
                or (pr.get("base") or {}).get("repo", {}).get("full_name") != repo
                or (pr.get("head") or {}).get("repo", {}).get("full_name") != repo):
            raise RuntimeError("installation PR/default/head/repository identity differs; forks refuse")
        # Reuse the owning parser; a distinct installation route still requires source-change.
        path = pathlib.Path(__file__).with_name("pr-lane-admission.py")
        spec = importlib.util.spec_from_file_location("installation_lane_body", path)
        module = importlib.util.module_from_spec(spec); assert spec and spec.loader
        spec.loader.exec_module(module)
        try:
            valid = module.validate_routine_body(pr.get("body"), pr["head"]["ref"], head)
            markers = module.routine_validator().marker_fields(module.routine_validator().ROUTINE_MARKER_RE, pr["body"])
            if not valid or markers[0].get("operation") != "source-change":
                raise RuntimeError("installation requires the exact source-change routine marker")
        except (module.ToolError, ValueError, KeyError, IndexError) as error:
            raise RuntimeError("installation routine marker is invalid") from error
        facts = self.installation_source(repo, head, base_ref, base, source, delivered=delivered)
        candidate, expected = facts["candidate"], facts["expected"]
        current, leaves = facts["current"], facts["leaves"]
        protected_owner_still_current = facts["protected_owner_still_current"]
        if pr.get("changed_files") != len(expected):
            raise RuntimeError("installation file count differs from canonical source delta")
        files = self.installation_read(f"repos/{repo}/pulls/{number}/files?per_page=100")
        if (not isinstance(files, list) or len(files) != len(expected)
                or any(not isinstance(r, dict) or r.get("status") != ("modified" if r.get("filename") in (PRODUCER_REGISTRATION, TEMPLATES_ASSERTION_PIN, BAR_PREARM, FOURD_SOURCE, FOURD_INVENTORY_TEST) else "added")
                       or r.get("previous_filename") is not None for r in files)
                or {r.get("filename") for r in files} != expected
                or any(r.get("sha") != candidate[r["filename"]]["sha"] for r in files)):
            raise RuntimeError("installation file-list population/status/identity differs")
        if delivered:
            merge = merged_commit_of(pr)
            if not merge or not isinstance(current, str) or not SHA_RE.fullmatch(current):
                raise RuntimeError("installation protected merge identity is unknown")
            merged_tree = leaves(self.installation_tree(repo, merge))
            protected_tree = leaves(self.installation_tree(repo, current))
            if any(merged_tree.get(p) != leaves(candidate)[p] or protected_tree.get(p) != leaves(candidate)[p] for p in expected):
                raise RuntimeError("installation protected payload readback differs")
            ancestry = self.installation_read(f"repos/{repo}/compare/{merge}...{current}")
            if ((ancestry.get("merge_base_commit") or {}).get("sha") != merge
                    or ancestry.get("status") not in ("identical", "ahead")):
                raise RuntimeError("protected payload/ancestry differs")
            protected_owner_still_current()
            return
        # Full native listings, not a workflow404 (which could hide denied metadata).
        workflows = self.installation_read(f"repos/{repo}/actions/workflows?per_page=100")
        checks = self.installation_read(f"repos/{repo}/commits/{head}/check-runs?per_page=100")
        for value, key in ((workflows, "workflows"), (checks, "check_runs")):
            if (not isinstance(value, dict) or type(value.get("total_count")) is not int
                    or not 0 <= value["total_count"] <= 100 or not isinstance(value.get(key), list)
                    or len(value[key]) != value["total_count"] or any(not isinstance(r, dict) for r in value[key])):
                raise RuntimeError("installation native producer population is incomplete")
        if (any(r.get("path") == PRODUCER_PATHS[1] or r.get("name") == "routine-eligibility" for r in workflows["workflows"])
                or any(r.get("name") == "routine-eligibility" for r in checks["check_runs"])):
            raise RuntimeError("native eligibility producer/check already exists")
        self.installation_required_checks(repo, number, head, checks["check_runs"],
                                          metadata=metadata, base=base, candidate=candidate,
                                          coherent_workflow=coherent_workflow, branch=pr["head"]["ref"])
        protected_owner_still_current()

    def installation_required_checks(self, repo: str, number: int, head: str, roster: list[dict],
                                     *, metadata: dict, base: str, candidate: dict,
                                     coherent_workflow: str | None, branch: str) -> None:
        self._installation_policy_fact = None
        # The existing native checks reader remains the receiver-specific requirement authority.
        api = WatchGhApi(time.monotonic() + 31)
        command = ["gh", "pr", "checks", str(number), "--repo", repo,
                   "--required", "--json", WATCH_FIELDS]
        if repo in ("FS-GG/FsQuint", BAR_REPOSITORY, SC2_REPOSITORY, FOURD_REPOSITORY):
            stderr = bytearray()
            code, raw = bounded_watch_query(command, time.monotonic() + 31, stderr_output=stderr)
            absent = (code == 1 and raw == b"" and bytes(stderr) ==
                      f"no required checks reported on the '{branch}' branch\n".encode("utf-8"))
            if code != 0 and not absent:
                raise RuntimeError("installation required-check transport is denied or unknown")
            if not absent:
                try:
                    checks = json.loads(raw)
                except (UnicodeError, ValueError) as error:
                    raise RuntimeError("installation required-check transport is malformed") from error
        else:
            code, checks = api._watch_read(command, (0,))
            absent = False
        if repo == FOURD_REPOSITORY:
            self.installation_fourd_verify(repo, head, roster, metadata=metadata, base=base,
                                           candidate=candidate, coherent_workflow=coherent_workflow, branch=branch)
            self.installation_fourd_policy(repo)
            if absent:
                self._installation_policy_fact = "native-policy-unavailable; no-required-contexts-reported"
                return
        if repo == SC2_REPOSITORY:
            self.installation_sc2_verify(repo, head, roster, metadata=metadata, base=base,
                                         candidate=candidate, coherent_workflow=coherent_workflow, branch=branch)
            if absent:
                self._installation_policy_fact = "no-required-contexts-reported"
                return
        if absent:
            self.installation_empty_policy(repo, head, roster, metadata, base, candidate, coherent_workflow, branch)
            self._installation_policy_fact = "required-policy-observed-empty"
            return
        if code != 0:
            raise RuntimeError("installation required checks read failed")
        if (not isinstance(checks, list) or not 0 < len(checks) <= WATCH_MAX_CHECKS
                or any(not isinstance(r, dict) or set(r) != set(WATCH_FIELDS.split(",")) or r.get("bucket") != "pass"
                       or r.get("state") != "SUCCESS" or not r.get("completedAt") for r in checks)):
            raise RuntimeError("installation required checks are absent, pending or failed")
        # Native links establish exact-head provenance instead of trusting a green name.
        for row in checks:
            match = re.fullmatch(rf"https://github\.com/{re.escape(repo)}/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)", row.get("link", ""))
            if not match:
                raise RuntimeError("installation required check provenance is unestablished")
            run_id, job_id = match.groups()
            native = [check for check in roster if check.get("id") == int(job_id)]
            if (len(native) != 1 or native[0].get("name") != row["name"]
                    or native[0].get("details_url") != row["link"]
                    or (native[0].get("app") or {}).get("id") != 15368
                    or native[0].get("head_sha") != head or native[0].get("status") != "completed"
                    or native[0].get("conclusion") != "success"):
                raise RuntimeError("installation required check differs from complete native roster")
            run = self.installation_read(f"repos/{repo}/actions/runs/{run_id}")
            job = self.installation_read(f"repos/{repo}/actions/jobs/{job_id}")
            if (run.get("id") != int(run_id) or run.get("head_sha") != head
                    or (run.get("repository") or {}).get("full_name") != repo
                    or run.get("status") != "completed" or run.get("conclusion") != "success"
                    or job.get("id") != int(job_id) or job.get("run_id") != int(run_id)
                    or job.get("head_sha") != head or job.get("name") != row.get("name")
                    or job.get("status") != "completed" or job.get("conclusion") != "success"
                    or type((native[0].get("check_suite") or {}).get("id")) is not int
                    or (native[0].get("check_suite") or {}).get("id") != run.get("check_suite_id")
                    or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job_id}"
                    or type(run.get("run_attempt")) is not int or run["run_attempt"] < 1
                    or type(job.get("run_attempt")) is not int
                    or job.get("run_attempt") != run["run_attempt"]):
                raise RuntimeError("installation required check exact-head/run/attempt differs")

        if repo == FOURD_REPOSITORY:
            self._installation_policy_fact = "native-policy-unavailable; required-contexts-passed"

        if repo == BAR_REPOSITORY:
            self.installation_bar_empty_policy(repo, head, roster, metadata, base, candidate,
                                               coherent_workflow, branch, require_empty=False)

    def installation_policy_read(self, endpoint: str) -> tuple[int, Any]:
        """Observe authenticated HTTP status and complete policy body together."""
        code, raw = bounded_watch_query(["gh", "api", "--include", endpoint],
                                       time.monotonic() + 31, max_bytes=1_048_576)
        try:
            headers, body = raw.decode("utf-8").replace("\r\n", "\n").split("\n\n", 1)
            lines = headers.splitlines()
            match = re.fullmatch(r"HTTP/\S+ ([0-9]{3})(?: .*)?", lines[0])
            if not match or any(line.lower().startswith("link:") for line in lines[1:]):
                raise ValueError("unknown HTTP status or paginated policy")
            status = int(match[1])
            fourd_route = next((route for route in FOURD_POLICY_ROUTES
                                if endpoint == f"repos/{FOURD_REPOSITORY}/{route}"), None)
            if ((status == 200 and code != 0) or (status in (404, 403) and code != 1)
                    or status not in ((200, 404, 403) if fourd_route else (200, 404))):
                raise ValueError("denied or contradictory HTTP result")
            def unique(pairs):
                value = {}
                for key, item in pairs:
                    if key in value:
                        raise ValueError("duplicate policy property")
                    value[key] = item
                return value
            value = json.loads(body, object_pairs_hook=unique)
            if status == 403 and value != {"message": FOURD_PLAN_MESSAGE, "status": "403",
                                          "documentation_url": FOURD_POLICY_ROUTES[fourd_route]}:
                raise ValueError("not the named receiver plan refusal")
            return status, value
        except (UnicodeError, ValueError, IndexError) as error:
            raise RuntimeError("installation required-policy read is denied, incomplete or unknown") from error

    def installation_empty_policy(self, repo: str, head: str, roster: list[dict],
                                  metadata: dict, base: str, candidate: dict,
                                  coherent_workflow: str | None, branch: str) -> None:
        if repo == BAR_REPOSITORY:
            self.installation_bar_empty_policy(repo, head, roster, metadata, base, candidate, coherent_workflow, branch)
            return
        # This reviewed exception is only for the authentic first-install FsQuint receiver.
        if (type(metadata.get("id")) is not int or metadata.get("id") != 1375501179 or metadata.get("private") is not False
                or metadata.get("default_branch") != "main"
                or not isinstance(metadata.get("permissions"), dict)
                or metadata["permissions"].get("admin") is not True
                or metadata["permissions"].get("push") is not True
                or metadata["permissions"].get("pull") is not True
                or coherent_workflow != "ci.yml"):
            raise RuntimeError("installation empty-policy receiver/capability/coherent selector differs")
        default = self.installation_read(f"repos/{repo}/git/ref/heads/main")
        if (not isinstance(default, dict) or default.get("ref") != "refs/heads/main"
                or (default.get("object") or {}).get("type") != "commit"
                or (default.get("object") or {}).get("sha") != base):
            raise RuntimeError("installation empty-policy current default/base identity differs")
        classic_status, classic = self.installation_policy_read(f"repos/{repo}/branches/main/protection/required_status_checks")
        rules_status, rules = self.installation_policy_read(f"repos/{repo}/rules/branches/main?per_page=100")
        sets_status, sets = self.installation_policy_read(f"repos/{repo}/rulesets?includes_parents=true&per_page=100")
        if (classic_status != 404 or not isinstance(classic, dict)
                or set(classic) != {"message", "documentation_url", "status"}
                or classic.get("message") != "Branch not protected" or classic.get("status") != "404"
                or classic.get("documentation_url") != "https://docs.github.com/rest/branches/branch-protection#get-status-checks-protection"
                or rules_status != 200 or rules != [] or sets_status != 200 or sets != []):
            raise RuntimeError("installation required policy is not authentically empty")
        self.installation_full_verify(repo, head, roster, metadata=metadata, base=base, candidate=candidate, branch=branch)

    def installation_fourd_policy(self, repo: str) -> None:
        """Authenticate the accepted process policy; a plan refusal is never empty policy."""
        if repo != FOURD_REPOSITORY:
            raise RuntimeError("FourD policy receiver differs")
        for identifier, digest in FOURD_POLICY_COMMENTS.items():
            value = self.installation_read(f"repos/{repo}/issues/comments/{identifier}")
            if (not isinstance(value, dict) or type(value.get("id")) is not int or value.get("id") != identifier
                    or value.get("html_url") != f"https://github.com/{repo}/issues/39#issuecomment-{identifier}"
                    or type((value.get("user") or {}).get("id")) is not int
                    or (value.get("user") or {}).get("id") != FOURD_POLICY_ACTOR_ID
                    or (value.get("user") or {}).get("login") != "EHotwagner"
                    or not isinstance(value.get("body"), str)
                    or hashlib.sha256(value["body"].encode()).hexdigest() != digest):
                raise RuntimeError("FourD accepted process-policy reference differs")
        for route in FOURD_POLICY_ROUTES:
            status, _ = self.installation_policy_read(f"repos/{repo}/{route}")
            if status != 403:
                raise RuntimeError("FourD native-policy capability changed; accepted unavailable policy must be reconciled")

    def installation_fourd_verify(self, repo: str, head: str, roster: list[dict], *, metadata: dict,
                                  base: str, candidate: dict, coherent_workflow: str | None, branch: str) -> None:
        if (repo != FOURD_REPOSITORY or metadata.get("id") != FOURD_REPOSITORY_ID
                or metadata.get("private") is not True or metadata.get("default_branch") != "main"
                or coherent_workflow != "verify.yml"):
            raise RuntimeError("FourD receiver/coherent identity differs")
        old = self.installation_tree(repo, base)
        for path, mode, digest in (("scripts/verify.sh", "100755", FOURD_VERIFY_ENTRY_SHA256),
                                  ("tests/portable-workspace/validate-source-preparation.py", "100644", FOURD_SOURCE_ENTRY_SHA256)):
            entry = old.get(path)
            if (entry is None or entry.get("type") != "blob" or entry.get("mode") != mode
                    or candidate.get(path) != entry
                    or hashlib.sha256(self.installation_blob(repo, entry)).hexdigest() != digest):
                raise RuntimeError("FourD technical entrypoint closure differs")
        if (not isinstance(roster, list) or len(roster) != 2
                or {row.get("name") for row in roster} != {"verify", "source-preparation"}
                or len({row.get("id") for row in roster}) != 2
                or any(type(row.get("id")) is not int or row["id"] <= 0
                       or type((row.get("app") or {}).get("id")) is not int
                       or (row.get("app") or {}).get("id") != 15368
                       or type((row.get("check_suite") or {}).get("id")) is not int for row in roster)):
            raise RuntimeError("FourD requires the complete two-workflow technical cohort")
        for filename, name in (("verify.yml", "verify"), (FOURD_SOURCE.rsplit("/", 1)[1], "source-preparation")):
            self.installation_full_verify(repo, head, [row for row in roster if row.get("name") == name],
                metadata=metadata, base=base, candidate=candidate, branch=branch, fourd_workflow=filename)

    def installation_sc2_verify(self, repo: str, head: str, roster: list[dict], *, metadata: dict,
                                base: str, candidate: dict, coherent_workflow: str | None, branch: str) -> None:
        """Require full SC2 verify even without required contexts."""
        if (repo != SC2_REPOSITORY or metadata.get("id") != SC2_REPOSITORY_ID
                or metadata.get("private") is not True or metadata.get("default_branch") != "main"
                or coherent_workflow != "verify.yml"):
            raise RuntimeError("SC2 receiver/coherent identity differs")
        old = self.installation_tree(repo, base)
        entry = old.get("scripts/verify.sh")
        if (entry is None or candidate.get("scripts/verify.sh") != entry
                or hashlib.sha256(self.installation_blob(repo, entry)).hexdigest() != SC2_VERIFY_ENTRY_SHA256):
            raise RuntimeError("SC2 verifier entrypoint differs")
        self.installation_full_verify(repo, head, roster, metadata=metadata, base=base, candidate=candidate, branch=branch)

    def installation_full_verify(self, repo: str, head: str, roster: list[dict], *, metadata: dict,
                                 base: str, candidate: dict, branch: str, fourd_workflow: str | None = None) -> None:
        if repo == FOURD_REPOSITORY:
            if fourd_workflow == "verify.yml":
                workflow_file, workflow_id, job_name, workflow_digest = "verify.yml", FOURD_VERIFY_ID, "verify", FOURD_VERIFY_SHA256
            elif fourd_workflow == FOURD_SOURCE.rsplit("/", 1)[1]:
                workflow_file, workflow_id, job_name, workflow_digest = fourd_workflow, FOURD_SOURCE_ID, "source-preparation", FOURD_SOURCE_BASE_SHA256
            else:
                raise RuntimeError("FourD technical workflow selection differs")
        elif repo == SC2_REPOSITORY:
            workflow_file, workflow_id, job_name, workflow_digest = "verify.yml", SC2_VERIFY_ID, "verify", SC2_VERIFY_SHA256
        elif repo == "FS-GG/FsQuint":
            workflow_file, workflow_id, job_name = "ci.yml", 361154226, "qualify"
            workflow_digest = "e93a1f3569de84d541de0f52ed08c52848d35631b644e28b79dd187badc0a520"
        else:
            raise RuntimeError("installation full verify receiver differs")
        workflow = self.installation_read(f"repos/{repo}/actions/workflows/{workflow_file}")
        if (not isinstance(workflow, dict) or type(workflow.get("id")) is not int or workflow.get("id") != workflow_id
                or workflow.get("path") != ".github/workflows/" + workflow_file or workflow.get("state") != "active"):
            raise RuntimeError("full CI workflow differs")
        path = workflow["path"]
        baseline = self.installation_tree(repo, base).get(path)
        if baseline is None or baseline.get("type") != "blob":
            raise RuntimeError("full CI baseline is missing")
        baseline_bytes = self.installation_blob(repo, baseline)
        if hashlib.sha256(baseline_bytes).hexdigest() != workflow_digest:
            raise RuntimeError("full CI source is not the reviewed cohort")
        if repo == FOURD_REPOSITORY and path == FOURD_SOURCE:
            selected = candidate.get(path)
            if (selected is None or selected.get("type") != "blob" or selected.get("mode") != "100644"
                    or baseline.get("mode") != "100644"
                    or self.installation_blob(repo, selected) != installation_fourd_workflow(baseline_bytes)):
                raise RuntimeError("FourD technical source workflow differs from exact derived filters")
        elif candidate.get(path) != baseline:
            raise RuntimeError("full CI source differs from base")
        population = self.installation_read(f"repos/{repo}/actions/workflows/{workflow_id}/runs?head_sha={head}&per_page=100")
        if (not isinstance(population, dict) or type(population.get("total_count")) is not int or population.get("total_count") != 1
                or not isinstance(population.get("workflow_runs"), list) or len(population["workflow_runs"]) != 1):
            raise RuntimeError("full CI exact-head run population incomplete/ambiguous")
        run = population["workflow_runs"][0]
        if (not isinstance(run, dict) or type(run.get("id")) is not int or run["id"] < 1
                or run.get("workflow_id") != workflow["id"] or run.get("path") != path
                or run.get("head_sha") != head or run.get("head_branch") != branch
                or run.get("event") != "pull_request" or type(run.get("run_attempt")) is not int or run.get("run_attempt") != 1
                or run.get("status") != "completed" or run.get("conclusion") != "success"
                or (run.get("repository") or {}).get("id") != metadata["id"]
                or (run.get("repository") or {}).get("full_name") != repo
                or type(run.get("check_suite_id")) is not int):
            raise RuntimeError("full CI run identity/attempt/success differs")
        detail = self.installation_read(f"repos/{repo}/actions/runs/{run['id']}")
        if any(detail.get(key) != run.get(key) for key in ("id", "workflow_id", "path", "head_sha", "event", "run_attempt", "status", "conclusion", "check_suite_id", "repository")):
            raise RuntimeError("installation full CI run readback differs")
        if repo == FOURD_REPOSITORY and detail.get("head_branch") != branch:
            raise RuntimeError("FourD technical run branch readback differs")
        jobs = self.installation_read(f"repos/{repo}/actions/runs/{run['id']}/attempts/1/jobs?per_page=100")
        if (not isinstance(jobs, dict) or type(jobs.get("total_count")) is not int or jobs.get("total_count") != 1
                or not isinstance(jobs.get("jobs"), list) or len(jobs["jobs"]) != 1
                or not isinstance(jobs["jobs"][0], dict) or jobs["jobs"][0].get("name") != job_name):
            raise RuntimeError("full CI job cohort incomplete")
        statuses = self.installation_read(f"repos/{repo}/commits/{head}/status?per_page=100")
        if (not isinstance(statuses, dict) or statuses.get("sha") != head
                or type(statuses.get("total_count")) is not int or statuses.get("total_count") != 0 or statuses.get("statuses") != []):
            raise RuntimeError("full CI status provenance unknown")
        if not roster or len({r.get("id") for r in roster}) != len(roster):
            raise RuntimeError("full CI cohort missing/duplicated")
        cohort = jobs["jobs"][0]
        if len(roster) != 1 or cohort.get("id") != roster[0].get("id"):
            raise RuntimeError("full CI check/job population differs")
        check = roster[0]
        steps = cohort.get("steps")
        expected_steps = (["Set up job", "Check out the complete product history", "Install the pinned .NET SDK",
                           "Install the pinned Node runtime", "Install the pinned Rust toolchain", "Verify the product",
                           "Retain the complete qualified product"] if repo == SC2_REPOSITORY else
                          ["Set up job", "Run actions/checkout@v7", "Run actions/setup-dotnet@v6", "Provision pinned Quint",
                           "Check independent CI preflight example", "Qualify core, tooling and external package consumer"])
        if repo == FOURD_REPOSITORY:
            expected_steps = list(FOURD_VERIFY_STEPS if job_name == "verify" else FOURD_SOURCE_STEPS)
        if (not isinstance(steps, list) or not len(expected_steps) <= len(steps) <= 32
                or any(not isinstance(step, dict) or step.get("status") != "completed"
                       or step.get("conclusion") != "success" for step in steps)
                or [step.get("name") for step in steps[:len(expected_steps)]] != expected_steps):
            raise RuntimeError("full CI steps absent/skipped/failed")
        if repo == SC2_REPOSITORY and any(step.get("name") not in
                ["Post Check out the complete product history", "Post Install the pinned Node runtime",
                 "Post Install the pinned .NET SDK", "Complete job"] for step in steps[len(expected_steps):]):
            raise RuntimeError("installation SC2 technical suffix differs")
        if repo == FOURD_REPOSITORY:
            tail = FOURD_VERIFY_TAIL if job_name == "verify" else FOURD_SOURCE_TAIL
            if (steps[-1].get("name") != "Complete job"
                    or any(step.get("name") not in tail for step in steps[len(expected_steps):])
                    or len({step.get("name") for step in steps}) != len(steps)
                    or any(type(step.get("number")) is not int or step["number"] <= 0 for step in steps)):
                raise RuntimeError("FourD technical suffix differs")
        link = f"https://github.com/{repo}/actions/runs/{run['id']}/job/{cohort.get('id')}"
        job = self.installation_read(f"repos/{repo}/actions/jobs/{cohort.get('id')}")
        if (job != cohort or type(job.get("id")) is not int or job["id"] < 1
                or job.get("run_id") != run["id"] or type(job.get("run_attempt")) is not int or job.get("run_attempt") != 1
                or job.get("head_sha") != head or job.get("name") != job_name
                or job.get("status") != "completed" or job.get("conclusion") != "success"
                or not job.get("completed_at")
                or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job['id']}"
                or check.get("head_sha") != head or check.get("name") != job_name
                or check.get("details_url") != link or (check.get("app") or {}).get("id") != 15368
                or (check.get("check_suite") or {}).get("id") != run["check_suite_id"]
                or check.get("status") != "completed" or check.get("conclusion") != "success"):
            raise RuntimeError("full CI check/app/job/run/attempt differs")

        if repo == SC2_REPOSITORY or (repo == FOURD_REPOSITORY and job_name == "verify"):
            artifacts = self.installation_read(f"repos/{repo}/actions/runs/{run['id']}/artifacts?per_page=100")
            if (not isinstance(artifacts, dict) or type(artifacts.get("total_count")) is not int
                    or not 1 <= artifacts["total_count"] <= 100 or not isinstance(artifacts.get("artifacts"), list)
                    or len(artifacts["artifacts"]) != artifacts["total_count"]
                    or any(not isinstance(row, dict) for row in artifacts["artifacts"])):
                raise RuntimeError("SC2 artifact population incomplete")
            pattern = (rf"sc2-qualified-product-([0-9a-f]{{40}})-{run['id']}" if repo == SC2_REPOSITORY
                       else rf"fourd-evaluation-candidate-([0-9a-f]{{40}})-{run['id']}-1")
            selected = [row for row in artifacts["artifacts"] if re.fullmatch(pattern, str(row.get("name", "")))]
            if (len(selected) != 1 or selected[0].get("expired") is not False
                    or type(selected[0].get("size_in_bytes")) is not int or selected[0]["size_in_bytes"] <= 0
                    or (selected[0].get("workflow_run") or {}).get("id") != run["id"]
                    or (selected[0].get("workflow_run") or {}).get("head_sha") != head):
                raise RuntimeError("SC2 artifact identity differs")
            if repo == FOURD_REPOSITORY and (type(selected[0].get("id")) is not int or selected[0]["id"] <= 0):
                raise RuntimeError("FourD artifact native identity differs")
            checkout = re.fullmatch(pattern, selected[0]["name"])[1]
            commit = self.installation_read(f"repos/{repo}/git/commits/{checkout}")
            if (not isinstance(commit, dict) or commit.get("sha") != checkout
                    or not isinstance(commit.get("parents"), list)
                    or [row.get("sha") if isinstance(row, dict) else None for row in commit["parents"]] != [base, head]
                    or self.installation_tree(repo, checkout) != candidate):
                raise RuntimeError("SC2 tested checkout parents/tree differ")

    def installation_bar_empty_policy(self, repo: str, head: str, roster: list[dict],
                                  metadata: dict, base: str, candidate: dict,
                                  coherent_workflow: str | None, branch: str, *, require_empty: bool = True) -> None:
        # Authentically empty policy is never sufficient without this nonempty BAR source cohort.
        if (type(metadata.get("id")) is not int or metadata.get("id") != BAR_REPOSITORY_ID or metadata.get("private") is not False
                or metadata.get("default_branch") != "main"
                or not isinstance(metadata.get("permissions"), dict)
                or metadata["permissions"].get("admin") is not True
                or metadata["permissions"].get("push") is not True
                or metadata["permissions"].get("pull") is not True
                or repo != BAR_REPOSITORY or coherent_workflow != "prearm-source-controls.yml"):
            raise RuntimeError("installation empty-policy receiver/capability/coherent selector differs")
        default = self.installation_read(f"repos/{repo}/git/ref/heads/main")
        if (not isinstance(default, dict) or default.get("ref") != "refs/heads/main"
                or (default.get("object") or {}).get("type") != "commit"
                or (default.get("object") or {}).get("sha") != base):
            raise RuntimeError("installation empty-policy current default/base identity differs")
        if require_empty:
            classic_status, classic = self.installation_policy_read(f"repos/{repo}/branches/main/protection/required_status_checks")
            rules_status, rules = self.installation_policy_read(f"repos/{repo}/rules/branches/main?per_page=100")
            sets_status, sets = self.installation_policy_read(f"repos/{repo}/rulesets?includes_parents=true&per_page=100")
            if (classic_status != 404 or not isinstance(classic, dict)
                    or set(classic) != {"message", "documentation_url", "status"}
                    or classic.get("message") != "Branch not protected" or classic.get("status") != "404"
                    or classic.get("documentation_url") != "https://docs.github.com/rest/branches/branch-protection#get-status-checks-protection"
                    or rules_status != 200 or rules != [] or sets_status != 200 or sets != []):
                raise RuntimeError("installation required policy is not authentically empty")
        workflow = self.installation_read(f"repos/{repo}/actions/workflows/prearm-source-controls.yml")
        if (not isinstance(workflow, dict) or type(workflow.get("id")) is not int or workflow.get("id") != BAR_PREARM_ID
                or workflow.get("path") != BAR_PREARM or workflow.get("state") != "active" or workflow.get("name") != "prearm-source-controls"):
            raise RuntimeError("installation BAR prearm source-control workflow differs")
        path = workflow["path"]
        baseline = self.installation_tree(repo, base).get(path)
        if (baseline is None or baseline.get("type") != "blob" or baseline.get("mode") != "100644"
                or candidate.get(path, {}).get("type") != "blob" or candidate[path].get("mode") != "100644"):
            raise RuntimeError("installation BAR prearm source-control source differs from protected base")
        # Bind the source job and every step to only the reviewed path-filter derivation.
        if self.installation_blob(repo, candidate[path]) != installation_bar_workflow(self.installation_blob(repo, baseline)):
            raise RuntimeError("installation BAR prearm source-control source is not the reviewed technical cohort")
        population = self.installation_read(f"repos/{repo}/actions/workflows/{BAR_PREARM_ID}/runs?head_sha={head}&per_page=100")
        if (not isinstance(population, dict) or type(population.get("total_count")) is not int or population.get("total_count") != 1
                or not isinstance(population.get("workflow_runs"), list) or len(population["workflow_runs"]) != 1):
            raise RuntimeError("installation BAR prearm source-control exact-head run population is incomplete or ambiguous")
        run = population["workflow_runs"][0]
        if (not isinstance(run, dict) or type(run.get("id")) is not int or run["id"] < 1
                or run.get("workflow_id") != workflow["id"] or run.get("path") != path
                or run.get("head_sha") != head or run.get("head_branch") != branch
                or run.get("event") != "pull_request" or type(run.get("run_attempt")) is not int or run.get("run_attempt") != 1
                or run.get("status") != "completed" or run.get("conclusion") != "success"
                or (run.get("repository") or {}).get("id") != metadata["id"]
                or (run.get("repository") or {}).get("full_name") != repo
                or type(run.get("check_suite_id")) is not int or run["check_suite_id"] < 1):
            raise RuntimeError("installation BAR prearm source-control run identity/attempt/success differs")
        detail = self.installation_read(f"repos/{repo}/actions/runs/{run['id']}")
        if any(detail.get(key) != run.get(key) for key in ("id", "workflow_id", "path", "head_sha", "head_branch", "event", "run_attempt", "status", "conclusion", "check_suite_id", "repository")):
            raise RuntimeError("installation BAR prearm source-control run readback differs")
        jobs = self.installation_read(f"repos/{repo}/actions/runs/{run['id']}/attempts/1/jobs?per_page=100")
        if (not isinstance(jobs, dict) or type(jobs.get("total_count")) is not int or jobs.get("total_count") != 1
                or not isinstance(jobs.get("jobs"), list) or len(jobs["jobs"]) != 1
                or not isinstance(jobs["jobs"][0], dict) or jobs["jobs"][0].get("name") != "prearm-source-controls"):
            raise RuntimeError("installation BAR prearm source-control job cohort is incomplete")
        statuses = self.installation_read(f"repos/{repo}/commits/{head}/status?per_page=100")
        if (not isinstance(statuses, dict) or statuses.get("sha") != head
                or type(statuses.get("total_count")) is not int or statuses.get("total_count") != 0 or statuses.get("statuses") != []):
            raise RuntimeError("installation exact-head status provenance is unknown")
        if not roster or len({r.get("id") for r in roster}) != len(roster):
            raise RuntimeError("installation complete technical check cohort is missing or duplicated")
        cohort = jobs["jobs"][0]
        # Empty policy binds this complete source cohort; ordinary required contexts are independently verified.
        technical = [check for check in roster if check.get("name") == "prearm-source-controls"]
        if len(technical) != 1 or (require_empty and len(roster) != 1) or cohort.get("id") != technical[0].get("id"):
            raise RuntimeError("installation native check/job population differs")
        check = technical[0]
        steps = cohort.get("steps")
        if (not isinstance(steps, list) or not len(BAR_PREARM_TECHNICAL_STEPS) <= len(steps) <= len(BAR_PREARM_TECHNICAL_STEPS) + len(BAR_PREARM_NATIVE_TAIL)
                or any(not isinstance(step, dict) or step.get("status") != "completed" or step.get("conclusion") != "success"
                       or type(step.get("number")) is not int or step["number"] < 1 for step in steps)
                or [step.get("number") for step in steps] != sorted(set(step.get("number") for step in steps))
                or tuple(step.get("name") for step in steps[:len(BAR_PREARM_TECHNICAL_STEPS)]) != BAR_PREARM_TECHNICAL_STEPS):
            raise RuntimeError("installation BAR prearm source-control technical steps are absent, skipped or failed")
        tail = [step["name"] for step in steps[len(BAR_PREARM_TECHNICAL_STEPS):]]
        if (any(name not in BAR_PREARM_NATIVE_TAIL for name in tail) or len(set(tail)) != len(tail)
                or tail != sorted(tail, key=BAR_PREARM_NATIVE_TAIL.index)):
            raise RuntimeError("installation BAR native setup/post cohort differs")
        link = f"https://github.com/{repo}/actions/runs/{run['id']}/job/{cohort.get('id')}"
        job = self.installation_read(f"repos/{repo}/actions/jobs/{cohort.get('id')}")
        if (job != cohort or type(job.get("id")) is not int or job["id"] < 1
                or job.get("run_id") != run["id"] or type(job.get("run_attempt")) is not int or job.get("run_attempt") != 1
                or job.get("head_sha") != head or job.get("name") != "prearm-source-controls"
                or job.get("status") != "completed" or job.get("conclusion") != "success"
                or not job.get("completed_at")
                or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job['id']}"
                or check.get("head_sha") != head or check.get("name") != "prearm-source-controls"
                or check.get("details_url") != link or (check.get("app") or {}).get("id") != 15368
                or (check.get("check_suite") or {}).get("id") != run["check_suite_id"]
                or check.get("status") != "completed" or check.get("conclusion") != "success"):
            raise RuntimeError("installation BAR prearm source-control check/app/job/run/attempt provenance differs")

    @staticmethod
    def _run(
        args: list[str], *, timeout: int = 30, input_text: str | None = None,
        mutation: bool = False,
    ) -> dict[str, Any]:
        try:
            result = subprocess.run(
                ["gh", "api", *args],
                check=False,
                capture_output=True,
                text=True,
                input=input_text,
                timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.strip() or result.stdout.strip() or f"gh api exited {result.returncode}"
            if mutation:
                raise NativeMergeRefused(detail)
            raise RuntimeError(detail)
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as error:
            if mutation:
                raise AmbiguousWrite("GitHub returned a non-JSON merge response") from error
            raise RuntimeError("GitHub returned a non-JSON response") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        value = self._run([f"repos/{repo}/pulls/{pr}"])
        if not isinstance(value, dict):
            raise RuntimeError("GitHub returned a non-object pull request")
        return value

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None:
        """Require a complete bounded roster and authenticate every named producer."""
        endpoint = f"repos/{repo}/commits/{head}/check-runs?per_page=100"
        value = self._run([endpoint])
        count = value.get("total_count") if isinstance(value, dict) else None
        checks = value.get("check_runs") if isinstance(value, dict) else None
        if (type(count) is not int or not 0 <= count <= 1000 or not isinstance(checks, list)
                or len(checks) != min(count, 100) or any(not isinstance(check, dict) for check in checks)):
            raise RuntimeError("routine eligibility check population is incomplete")
        checks = list(checks)
        for page in range(2, (count + 99) // 100 + 1):
            value = self._run([endpoint + f"&page={page}"])
            rows = value.get("check_runs") if isinstance(value, dict) else None
            if (not isinstance(value, dict) or type(value.get("total_count")) is not int
                    or value["total_count"] != count or not isinstance(rows, list)
                    or len(rows) != min(100, count - (page - 1) * 100)
                    or any(not isinstance(check, dict) for check in rows)):
                raise RuntimeError("routine eligibility check population is incomplete")
            checks.extend(rows)
        identities = [check.get("id") for check in checks]
        if (len(checks) != count or any(type(identity) is not int or identity < 1 for identity in identities)
                or len(set(identities)) != count):
            raise RuntimeError("routine eligibility check population is incomplete")
        matches = [check for check in checks if check.get("name") == "routine-eligibility"]
        if not matches or len({check.get("id") for check in matches}) != len(matches):
            raise RuntimeError("routine eligibility check is missing or duplicated")
        workflow = self._run([f"repos/{repo}/actions/workflows/routine-eligibility.yml"])
        workflow_id = workflow.get("id") if isinstance(workflow, dict) else None
        if (type(workflow_id) is not int or workflow_id < 1
                or workflow.get("path") != ".github/workflows/routine-eligibility.yml"
                or (repo == "FS-GG/.github" and workflow_id != 352355034)):
            raise RuntimeError("routine eligibility producer identity is unestablished")
        native = {}
        for check in matches:
            if not isinstance(check.get("details_url"), str) or not isinstance(check.get("app"), dict) or not isinstance(check.get("check_suite"), dict):
                raise RuntimeError("routine eligibility check identity is malformed")
            match = re.fullmatch(rf"https://github\.com/{re.escape(repo)}/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)",
                                 check.get("details_url", ""))
            if (match is None or check.get("app", {}).get("id") != 15368
                    or check.get("head_sha") != head):
                raise RuntimeError("routine eligibility has no valid native producer")
            run_id, job_id = map(int, match.groups())
            suite = check.get("check_suite", {}).get("id")
            run = self._run([f"repos/{repo}/actions/runs/{run_id}"])
            job = self._run([f"repos/{repo}/actions/jobs/{job_id}"])
            if (type(suite) is not int or suite < 1 or check.get("id") != job_id
                    or not isinstance(run, dict) or not isinstance(job, dict)
                    or run.get("id") != run_id or run.get("workflow_id") != workflow_id
                    or run.get("path") != workflow["path"] or run.get("event") != "pull_request_target"
                    or run.get("head_sha") != head or run.get("check_suite_id") != suite
                    or not isinstance(run.get("repository"), dict) or run["repository"].get("full_name") != repo
                    or type(run.get("run_attempt")) is not int or run["run_attempt"] < 1
                    or job.get("id") != job_id or job.get("run_id") != run_id
                    or job.get("head_sha") != head or job.get("name") != "routine-eligibility"
                    or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job_id}"
                    or type(job.get("run_attempt")) is not int
                    or not 1 <= job["run_attempt"] <= run["run_attempt"]
                    or job.get("status") != check.get("status") or job.get("conclusion") != check.get("conclusion")):
                raise RuntimeError("routine eligibility run/job/suite/attempt provenance differs")
            native[job_id] = (run, job)
        try:
            newest = max(matches, key=lambda check: (datetime.fromisoformat(check["started_at"].replace("Z", "+00:00")), check["id"]))
        except (KeyError, TypeError, ValueError) as error:
            raise RuntimeError("routine eligibility ordering identity is malformed") from error
        if newest.get("status") != "completed" or newest.get("conclusion") != "success":
            raise RuntimeError("latest routine eligibility check is not successful")
        run, job = native[newest["id"]]
        if (job["run_attempt"] != run["run_attempt"] or run.get("status") != "completed"
                or run.get("conclusion") != "success" or run.get("head_branch") != branch):
            raise RuntimeError("selected routine eligibility is not the successful current run attempt/branch")

    def merge(self, request: MergeEffectRequest) -> dict[str, Any]:
        payload = json.dumps(
            {"sha": request.expectedHead, "merge_method": request.mergeMethod},
            separators=(",", ":"), sort_keys=True,
        )
        response = self._run(
            ["--method", "PUT", f"repos/{request.repository}/pulls/{request.pullRequest}/merge",
             "--input", "-"],
            input_text=payload,
            mutation=True,
        )
        if not isinstance(response, dict):
            raise AmbiguousWrite("GitHub returned a non-object merge response")
        return response

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict[str, Any]]:
        pages = self._run([
            "--paginate", "--slurp",
            f"repos/{repo}/actions/workflows/{workflow}/runs?head_sha={head}&per_page=100",
        ])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned a non-page workflow-run response")
        runs: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("workflow_runs"), list):
                raise RuntimeError("GitHub returned a malformed workflow-run page")
            runs.extend(run for run in page["workflow_runs"] if isinstance(run, dict))
        return runs

    @staticmethod
    def _run_bytes(args: list[str], *, timeout: int = 30) -> bytes:
        try:
            result = subprocess.run(
                ["gh", "api", *args], check=False, capture_output=True, timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.decode(errors="replace").strip() or f"gh api exited {result.returncode}"
            raise RuntimeError(detail)
        return result.stdout

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None:
        pages = self._run(["--paginate", "--slurp", f"repos/{repo}/actions/runs/{run_id}/artifacts?per_page=100"])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned malformed workflow artifact pages")
        artifacts: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("artifacts"), list):
                raise RuntimeError("GitHub returned malformed workflow artifacts")
            artifacts.extend(artifact for artifact in page["artifacts"] if isinstance(artifact, dict))
        expected = f"qualification-selection-{head}"
        matches = [
            artifact for artifact in artifacts
            if isinstance(artifact, dict) and artifact.get("name") == expected
            and artifact.get("expired") is not True and isinstance(artifact.get("archive_download_url"), str)
        ]
        if not matches:
            return None
        if len(matches) != 1:
            raise RuntimeError("GitHub returned duplicate qualification-selection artifacts")
        archive = self._run_bytes([matches[0]["archive_download_url"]])
        if len(archive) > MAX_SELECTION_ARCHIVE_BYTES:
            raise RuntimeError("qualification-selection artifact exceeds 1 MiB")
        try:
            with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
                entries = bundle.infolist()
                if len(entries) > MAX_SELECTION_ENTRIES:
                    raise RuntimeError("qualification-selection archive has an unsafe shape")
                paths: set[str] = set()
                selections: list[zipfile.ZipInfo] = []
                uncompressed = 0
                for entry in entries:
                    name = entry.filename[:-1] if entry.is_dir() and entry.filename.endswith("/") else entry.filename
                    parts = name.split("/")
                    if (not name or name.startswith("/") or "\\" in name or "\x00" in name
                            or re.match(r"^[A-Za-z]:", name) or any(part in {"", ".", ".."} for part in parts)
                            or name in paths):
                        raise RuntimeError("qualification-selection archive has an unsafe shape")
                    paths.add(name)
                    if entry.is_dir():
                        continue
                    uncompressed += entry.file_size
                    if uncompressed > MAX_SELECTION_UNCOMPRESSED_BYTES:
                        raise RuntimeError("qualification-selection archive exceeds 1 MiB uncompressed")
                    if name == "selection.json":
                        selections.append(entry)
                if len(selections) != 1 or selections[0].file_size > MAX_SELECTION_BYTES:
                    raise RuntimeError("qualification-selection archive has an unsafe shape")
                return bundle.read(selections[0])
        except zipfile.BadZipFile as error:
            raise RuntimeError("qualification-selection artifact is not a ZIP archive") from error

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict[str, Any]]:
        pages = self._run(["--paginate", "--slurp", f"repos/{repo}/actions/runs/{run_id}/jobs?per_page=100"])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned a non-page workflow-job response")
        jobs: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("jobs"), list):
                raise RuntimeError("GitHub returned a malformed workflow-job page")
            jobs.extend(job for job in page["jobs"] if isinstance(job, dict))
        return jobs

    # ---- Board-currency merge gate reads/write (design §2b). Every failure raises; callers map it to a
    # fixed verdict and never echo provider text, bodies or tokens.

    def _graphql(self, query: str, variables: dict[str, str | int]) -> dict[str, Any]:
        args = ["graphql", "-f", f"query={query}"]
        for name, value in variables.items():
            args.extend(["-F", f"{name}={value}"] if type(value) is int else ["-f", f"{name}={value}"])
        value = self._run(args)
        if not isinstance(value, dict) or value.get("errors") or not isinstance(value.get("data"), dict):
            raise RuntimeError("board read is incomplete")
        return value["data"]

    def board_pr(self, repo: str, pr: int) -> dict[str, Any]:
        """Body, author, last body editor and state in one read (REST has no body editor)."""
        owner, name = board_repo_parts(repo)
        data = self._graphql(BOARD_PR_QUERY, {"owner": owner, "name": name, "number": pr})
        node = ((data.get("repository") or {}).get("pullRequest"))
        if not isinstance(node, dict) or node.get("number") != pr or not isinstance(node.get("body"), str):
            raise RuntimeError("board pull request read is incomplete")
        edited_at = node.get("lastEditedAt")
        login = lambda actor: actor.get("login") if isinstance(actor, dict) else None
        return {"repo": repo, "number": pr, "state": node.get("state"), "body": node["body"],
                "author": login(node.get("author")), "edited": edited_at is not None,
                "editor": login(node.get("editor"))}

    def board_cards(self, refs: list[str]) -> dict[str, dict | None]:
        """Each ref's Project 3 card (Work level, Status, state, native parent) plus the parent's card."""
        if not refs:
            return {}
        if len(refs) > BOARD_MAX_REFS:
            raise RuntimeError("board read is unbounded")
        variables: dict[str, str | int] = {}
        declared, selected = [], []
        for index, ref in enumerate(refs):
            repo, _, number = ref.partition("#")
            owner, name = board_repo_parts(repo)
            if not number.isdigit():
                raise RuntimeError("board reference is malformed")
            variables.update({f"o{index}": owner, f"n{index}": name, f"k{index}": int(number)})
            declared.append(f"$o{index}:String!,$n{index}:String!,$k{index}:Int!")
            selected.append(f"r{index}:repository(owner:$o{index},name:$n{index}){{issueOrPullRequest(number:$k{index})"
                            "{__typename ...on Issue{...IssueCard parent{...IssueCard}} ...on PullRequest{...PrCard}}}")
        query = (f"query({','.join(declared)}){{{' '.join(selected)}}}"
                 f"fragment IssueCard on Issue{{{BOARD_CARD_SELECTION}}}"
                 f"fragment PrCard on PullRequest{{{BOARD_CARD_SELECTION}}}")
        data = self._graphql(query, variables)
        cards: dict[str, dict | None] = {}
        parents: dict[str, dict | None] = {}
        for index, ref in enumerate(refs):
            node = (data.get(f"r{index}") or {}).get("issueOrPullRequest")
            if not isinstance(node, dict):
                raise RuntimeError("board reference read is incomplete")
            cards[ref] = board_card(node)
            parent = node.get("parent")
            if node.get("__typename") == "Issue" and parent is not None:
                if not isinstance(parent, dict):
                    raise RuntimeError("board parent read is incomplete")
                parents[board_card_key(parent)] = board_card(parent)
        for key, card in parents.items():
            cards.setdefault(key, card)
        return cards

    def board_item(self, ref: str) -> dict[str, Any]:
        """Fresh complete reported state of one issue's Project 3 item and the live Status field schema."""
        repo, _, number = ref.partition("#")
        owner, name = board_repo_parts(repo)
        if not number.isdigit():
            raise RuntimeError("board reference is malformed")
        data = self._graphql(BOARD_ITEM_QUERY, {"owner": owner, "name": name, "number": int(number)})
        issue = (data.get("repository") or {}).get("issue")
        if not isinstance(issue, dict):
            raise RuntimeError("board item read is incomplete")
        return board_item_state(issue)

    def board_set_status(self, project_id: str, item_id: str, field_id: str, option_id: str) -> None:
        """Exactly one native single-select send. Never retried: a failure is reported, not resent."""
        command = ["gh", "project", "item-edit", "--id", item_id, "--project-id", project_id,
                   "--field-id", field_id, "--single-select-option-id", option_id]
        result = subprocess.run(command, check=False, capture_output=True, text=True, timeout=30)
        if result.returncode != 0:
            raise RuntimeError("board status send is unconfirmed")


@dataclass(frozen=True)
class Summary:
    schema: str
    repo: str
    pr: int
    expectedHead: str
    observedHead: str | None
    outcome: str
    codeDelivery: str
    publication: str
    mergeCommit: str | None
    attempts: int
    reason: str | None
    validationDisposition: str
    coherentValidation: str
    baseRef: str | None = None
    baseSha: str | None = None
    outcomeAt: str | None = None
    observedAt: str | None = None
    telemetryHealth: str = "not-configured"
    routineEligibility: str = "not-required"
    producerInstallation: str = "not-selected"
    producerSource: str | None = None
    # Board-currency merge gate (design §2b). Defaults keep every pre-gate result shape valid.
    boardGate: str = "not-applicable"
    boardGateReason: str | None = None
    boardTransition: str = "not-applicable"


# A result written before the board gate existed lacks these fields and stays a valid prior result.
BOARD_RESULT_FIELDS = frozenset({"boardGate", "boardGateReason", "boardTransition"})


def installation_result_shape(value: Any) -> bool:
    current = {f.name for f in fields(Summary)}
    if not isinstance(value, dict):
        return False
    keys = set(value)
    if keys in (current, current - BOARD_RESULT_FIELDS):
        return True
    evidence = value.get("deliveryEvidence")
    return (keys - {"deliveryEvidence"} in (current, current - BOARD_RESULT_FIELDS)
            and "deliveryEvidence" in keys
            and isinstance(evidence, dict)
            and evidence.get("schema") == "fsgg.routine-delivery-evidence/1")


def installation_prior_result(path: str) -> dict:
    """Existing retained Summary is a restriction, never an installation grant."""
    target = pathlib.Path(path)
    if not target.is_absolute() or any(p.is_symlink() for p in (target, *target.parents)):
        raise RuntimeError("installation prior result must be a regular absolute unlinked path")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise RuntimeError("installation prior result has duplicate fields")
            result[key] = value
        return result
    try:
        descriptor = os.open(target, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            if not stat.S_ISREG(os.fstat(descriptor).st_mode):
                raise RuntimeError("installation prior result must be a regular file")
            with os.fdopen(descriptor, "rb", closefd=False) as stream:
                raw = stream.read(65537)
        finally:
            os.close(descriptor)
        if len(raw) > 65536:
            raise RuntimeError("installation prior result exceeds byte bound")
        value = json.loads(raw, object_pairs_hook=unique)
    except (OSError, ValueError) as error:
        raise RuntimeError("installation prior result is unreadable or malformed") from error
    if not installation_result_shape(value):
        raise RuntimeError("installation prior result schema fields differ")
    return value


def observe_candidate(
    summary: Summary,
    *,
    assignment: str,
    store_root: str | None = None,
    config: str | None = None,
    repository: str | None = None,
    engine: str,
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
    deadline: float | None = None,
    clock: Callable[[], float] = time.monotonic,
) -> str:
    """Invoke advisory CI reconciliation with the exact generated delivery JSON."""
    payload = json.dumps(asdict(summary), separators=(",", ":"), sort_keys=True) + "\n"
    reason = "unknown"
    try:
        with tempfile.NamedTemporaryFile(
            "w", encoding="utf-8", prefix="fsgg-routine-delivery-", suffix=".json"
        ) as delivery:
            delivery.write(payload)
            delivery.flush()
            command = [engine, "telemetry", "ci", "reconcile", "--assignment", assignment,
                       "--delivery", delivery.name]
            if store_root is not None:
                command.extend(["--store-root", store_root])
            else:
                if config is not None:
                    command.extend(["--config", config])
                if repository is not None:
                    command.extend(["--repository", repository])
            child_options = {}
            if store_root is not None:
                child_environment = os.environ.copy()
                child_environment.pop("FSGG_TELEMETRY_CONFIG", None)
                child_options["env"] = child_environment
            if deadline is None:
                completed = runner(
                    command, check=False, capture_output=True, text=True, timeout=35,
                    **child_options,
                )
            else:
                code, raw = bounded_watch_query(
                    command, deadline, clock=clock, max_seconds=35, **child_options,
                )
                completed = subprocess.CompletedProcess(command, code, raw.decode("utf-8"), "")
        if completed.returncode != 0:
            # Preserve a useful bounded diagnostic without forwarding provider
            # text, credentials, arguments or private paths to the caller.
            reason = f"reconcile-exit-{completed.returncode}"
            safe_reasons = {
                "first CI population admission requires ready, not-delivered, and an explicit matching observed head": "prospective-ci-admission-missing",
                "GITHUB_TOKEN or GH_TOKEN is required": "provider-credential-unavailable",
                "workspace config and legacy store root cannot both be selected": "destination-selection-conflict",
                "configuration-version": "configuration-version",
            }
            for message, code in safe_reasons.items():
                if any(line.removeprefix("fsgg-coord-engine: telemetry ci: ").strip() == message
                       for line in completed.stderr[:8192].splitlines()):
                    reason += ":" + code
                    break
        if completed.returncode == 0:
            try:
                result = json.loads(completed.stdout)
                health = result.get("driverHealth")
                if health in {"complete", "open", "pending", "missing-outcome"}:
                    return health
            except (json.JSONDecodeError, AttributeError):
                pass
            return "pending"
    except (OSError, subprocess.SubprocessError, RuntimeError, UnicodeDecodeError) as error:
        reason = "helper-" + type(error).__name__
    print(f"fsgg routine telemetry: CI observation unavailable ({reason}); native delivery is unchanged", file=sys.stderr)
    return "unavailable"



def observe_dashboard_event(
    *, engine: str, config: str | None, deadline: float | None,
    clock: Callable[[], float] = time.monotonic,
    query: Callable[..., tuple[int, bytes]] | None = None,
) -> str:
    """One advisory final refresh within the caller's original absolute boundary.

    No activation, retry or credential lookup here. The native publisher owns its
    existing receipt/credential/destination contract and remote verification.
    """
    if deadline is None:
        return "not-run-budget-unavailable"
    if clock() >= deadline - WATCH_CLEANUP_SECONDS:
        return "not-run-budget-exhausted"
    if config is None:
        return "not-run-config-unavailable"
    try:
        code, raw = (query or bounded_watch_query)(
            [engine, "telemetry", "dashboard", "publisher-event", "--config", config],
            deadline, clock=clock, max_seconds=60, max_bytes=8192,
        )
        if code != 0 or clock() > deadline:
            return "unavailable"
        value = json.loads(raw)
        if not isinstance(value, dict):
            return "unavailable"
        status, reason = value.get("status"), value.get("reason")
        if not isinstance(status, str) or not isinstance(reason, str):
            return "unavailable"
        pair = (status, reason)
        if pair in {("published", "PUBLICATION_VERIFIED"),
                    ("unchanged", "SEMANTIC_CONTENT_UNCHANGED")}:
            if (value.get("schema") == "fsgg.telemetry.dashboard-event-health/1"
                    and isinstance(value.get("publicRevision"), str)
                    and re.fullmatch(r"[0-9a-f]{64}", value["publicRevision"])
                    and isinstance(value.get("commit"), str)
                    and SHA_RE.fullmatch(value["commit"])):
                return value["status"]
        if value.get("status") == "skipped":
            return "skipped"
    except (OSError, subprocess.SubprocessError, RuntimeError, ValueError):
        pass
    return "unavailable"


def head_of(pr: dict[str, Any]) -> str | None:
    head = pr.get("head")
    return head.get("sha") if isinstance(head, dict) and isinstance(head.get("sha"), str) else None


def base_of(pr: dict[str, Any]) -> tuple[str | None, str | None]:
    base = pr.get("base")
    if not isinstance(base, dict):
        return None, None
    ref = base.get("ref") if isinstance(base.get("ref"), str) else None
    sha = base.get("sha") if isinstance(base.get("sha"), str) and SHA_RE.fullmatch(base["sha"]) else None
    return ref, sha


def merged_commit_of(pr: dict[str, Any]) -> str | None:
    value = pr.get("merge_commit_sha")
    return value if isinstance(value, str) and SHA_RE.fullmatch(value) else None


def outcome_time_of(pr: dict[str, Any]) -> str | None:
    value = pr.get("merged_at")
    if not isinstance(value, str) or not value.endswith("Z"):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return value if parsed.utcoffset() == timedelta(0) else None


def is_merged(pr: dict[str, Any]) -> bool:
    return (pr.get("merged") is True and pr.get("state") == "closed"
            and outcome_time_of(pr) is not None and merged_commit_of(pr) is not None)


def correlated_merge_readback(pr: dict[str, Any], request: MergeEffectRequest) -> str | None:
    """Return the native merge commit only for the exact preflight identity."""
    if (not is_merged(pr) or head_of(pr) != request.expectedHead
            or base_of(pr) != (request.baseRef, request.baseSha)):
        return None
    return merged_commit_of(pr)


def coherent_state(runs: list[dict[str, Any]], expected_head: str) -> tuple[str, str | None, dict[str, Any] | None]:
    exact = [run for run in runs if run.get("head_sha") == expected_head]
    if not exact:
        return "pending", "no coherent run exists for the exact candidate", None
    active = [run for run in exact if run.get("status") != "completed"]
    if active:
        newest = max(active, key=lambda run: (str(run.get("updated_at") or ""), int(run.get("id") or 0)))
        return "pending", "coherent run for the exact candidate is still pending or running", newest
    latest = max(
        exact,
        key=lambda run: (
            str(run.get("updated_at") or ""),
            int(run.get("run_attempt") or 0),
            int(run.get("id") or 0),
        ),
    )
    if latest.get("conclusion") == "success":
        return "passed", None, latest
    return "failed", f"latest coherent run concluded {latest.get('conclusion') or 'unknown'}", latest


SELECTION_KEYS = [
    "schema", "candidateObligationSha256", "disposition", "reason", "prior", "semanticDelta",
    "bindingCorrespondenceSha256", "coherentRunPending", "coherentState", "selectionSha256",
]


def parse_selection(content: bytes) -> tuple[str, str | None]:
    if len(content) > 65_536 or not content.endswith(b"\n"):
        return "invalid", "qualification selection is oversized or non-canonical"
    try:
        value = json.loads(content)
    except (UnicodeDecodeError, json.JSONDecodeError):
        return "invalid", "qualification selection is malformed JSON"
    if not isinstance(value, dict) or list(value) != SELECTION_KEYS:
        return "invalid", "qualification selection properties are not the exact canonical set"
    digest = value.get("selectionSha256")
    payload = dict(value)
    payload.pop("selectionSha256", None)
    encoded = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode()
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest) or hashlib.sha256(encoded).hexdigest() != digest:
        return "invalid", "qualification selection self digest does not match"
    disposition = value.get("disposition")
    if disposition not in {"current", "reused", "deferred", "failed"}:
        return "invalid", "qualification selection disposition is unsupported"
    if value.get("schema") != "fsgg.coordination.qualification-selection/1":
        return "invalid", "qualification selection schema is unsupported"
    digest64 = lambda item: isinstance(item, str) and re.fullmatch(r"[0-9a-f]{64}", item) is not None
    if not digest64(value.get("candidateObligationSha256")) or not isinstance(value.get("reason"), str) or not value["reason"].strip():
        return "invalid", "qualification selection identity or reason is invalid"
    semantic = value.get("semanticDelta")
    if (not isinstance(semantic, dict)
            or list(semantic) != ["evaluatorSha256", "deltaSha256", "empty"]
            or not digest64(semantic.get("evaluatorSha256"))
            or not digest64(semantic.get("deltaSha256"))
            or not isinstance(semantic.get("empty"), bool)):
        return "invalid", "qualification selection semantic delta is invalid"
    correspondence = value.get("bindingCorrespondenceSha256")
    if correspondence is not None and not digest64(correspondence):
        return "invalid", "qualification selection binding correspondence is invalid"
    if not isinstance(value.get("coherentRunPending"), bool) or value.get("coherentState") not in {"pending", "running", "passed", "blocked", "disputed"}:
        return "invalid", "qualification selection coherent state is invalid"
    if disposition == "reused":
        prior = value.get("prior")
        prior_keys = ["candidateObligationSha256", "runId", "attempt", "executedReceiptSha256", "completedAt", "expiresAt", "authentic", "complete"]
        if (not isinstance(prior, dict) or list(prior) != prior_keys
                or not digest64(prior.get("candidateObligationSha256"))
                or not isinstance(prior.get("runId"), int) or prior["runId"] <= 0
                or not isinstance(prior.get("attempt"), int) or prior["attempt"] <= 0
                or not digest64(prior.get("executedReceiptSha256"))
                or prior.get("authentic") is not True or prior.get("complete") is not True):
            return "invalid", "reused selection lacks authentic complete prior evidence"
        if not isinstance(semantic, dict) or semantic.get("empty") is not True:
            return "invalid", "reused selection lacks an empty semantic delta"
        try:
            completed = datetime.fromisoformat(str(prior.get("completedAt")).replace("Z", "+00:00"))
            expires = datetime.fromisoformat(str(prior.get("expiresAt")).replace("Z", "+00:00"))
        except ValueError:
            return "invalid", "reused selection has invalid prior timestamps"
        now = datetime.now(timezone.utc)
        if completed.tzinfo is None or expires.tzinfo is None or completed > now or expires <= now:
            return "invalid", "reused selection prior evidence is not currently valid"
        if value.get("coherentRunPending") is not True or value.get("coherentState") != "pending":
            return "invalid", "reused selection is not the producer's initial pending decision"
    return disposition, None


def validation_state(api: NativeApi, repo: str, workflow: str, head: str) -> tuple[str, str, str | None]:
    coherent, reason, run = coherent_state(api.coherent_runs(repo, workflow, head), head)
    if run is None or not isinstance(run.get("id"), int):
        return "current", coherent, reason
    failed_jobs = [
        job for job in api.coherent_jobs(repo, run["id"])
        if job.get("status") == "completed"
        and job.get("conclusion") in {"failure", "timed_out", "cancelled", "action_required", "startup_failure"}
    ]
    if failed_jobs:
        return "failed", "failed", "an exact-head coherent job has already failed"
    content = api.qualification_selection(repo, run["id"], head)
    if content is None:
        return "current", coherent, reason or "exact-head reuse has not been validated"
    disposition, selection_reason = parse_selection(content)
    return disposition, coherent, selection_reason or reason


def eligible(pr: dict[str, Any], expected_head: str) -> tuple[bool, str | None]:
    observed = head_of(pr)
    if observed != expected_head:
        return False, f"changed head: expected {expected_head}, observed {observed or 'unreadable'}"
    if is_merged(pr):
        return True, None
    if pr.get("merged") is not False or pr.get("merged_at") is not None:
        return False, "pull request has contradictory or malformed merge evidence"
    if pr.get("state") != "open":
        return False, f"pull request is {pr.get('state') or 'unreadable'}, not open"
    if pr.get("draft") is True:
        return False, "pull request is draft"
    if pr.get("mergeable") is not True:
        return False, "pull request is not currently mergeable"
    merge_state = pr.get("mergeable_state")
    if merge_state not in {"clean", "unstable", "has_hooks"}:
        return False, f"pull request merge state is {merge_state or 'unreadable'}"
    return True, None


def routine_contract(api: NativeApi, repo: str, pr: dict[str, Any], head: str) -> str:
    path = pathlib.Path(__file__).with_name("pr-lane-admission.py")
    spec = importlib.util.spec_from_file_location("routine_lane_body", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    try:
        routine = module.validate_routine_body(pr.get("body"), (pr.get("head") or {}).get("ref"), head)
    except (module.ToolError, ValueError) as error:
        raise RuntimeError(str(error)) from error
    if not routine:
        return "not-required"
    api.routine_eligibility(repo, head, pr["head"]["ref"])
    return "passed"


# ---------------------------------------------------------------------------------------------------
# Board-currency merge gate (docs/coordination/2026-10-31-board-currency-gate.md §2b; Refs #5094).
#
# Before the merge effect, a routine PR applied with --apply is checked for R1 linkage: exactly one
# Project 3 Delivery with a parent Outcome, judged by scripts/board-currency-rules.py (linkage_of), with a
# valid Root override evaluated first. After a confirmed merge, the linked open Delivery's Status moves
# once to In review (or In progress with the delivery-continues marker) under a guarded write. Advisory
# mode (the default until Root's phase 3) never changes the merge decision and never writes; it records
# the verdict and what enforce would write. Result text is a fixed vocabulary: no body, title, login or
# provider message is ever copied into the result or the logs.

BOARD_GATE_MODES = ("advisory", "enforce")
BOARD_OWNER = "FS-GG"
BOARD_NUMBER = 3
BOARD_MAX_REFS = 20
BOARD_ROUTINE_MARKER = "fsgg:routine-development/v1"
BOARD_CONTINUES = "<!-- fsgg:delivery-continues/v1 -->"
BOARD_WRITABLE_PREIMAGES = ("Backlog", "Ready", "In progress", "In review")
_BOARD_NAME = re.compile(r"[A-Za-z0-9_.-]{1,100}")
_BOARD_TOKEN = re.compile(r"[a-z0-9-]{1,40}")
_BOARD_NODE_ID = re.compile(r"[A-Za-z0-9_=-]{1,200}")
# GitHub's closing keywords, as the rules module reads them. A Delivery this merge closes is treated as
# closed: GitHub closes it asynchronously and the built-in "Item closed -> Done" workflow is Done's only writer.
_BOARD_CLOSING = re.compile(
    r"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s+"
    r"((?:[\w.-]+/[\w.-]+)?#\d+(?:\s*(?:,|and|&)\s*(?:[\w.-]+/[\w.-]+)?#\d+)*)", re.I)
_BOARD_ONE_REF = re.compile(r"(?:([\w.-]+/[\w.-]+))?#(\d+)")

BOARD_PR_QUERY = (
    "query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name)"
    "{pullRequest(number:$number){number state body lastEditedAt author{login} editor{login}}}}")
_BOARD_SINGLE = "{__typename ...on ProjectV2ItemFieldSingleSelectValue{name}}"
BOARD_CARD_SELECTION = (
    "number state repository{nameWithOwner} projectItems(first:20,includeArchived:false){totalCount "
    "nodes{project{number owner{__typename ...on Organization{login}}} "
    f'workLevel:fieldValueByName(name:"Work level"){_BOARD_SINGLE} '
    f'status:fieldValueByName(name:"Status"){_BOARD_SINGLE}}}}}')
_BOARD_FIELD = "field{__typename ...on ProjectV2FieldCommon{id name}}"
BOARD_ITEM_QUERY = (
    "query($owner:String!,$name:String!,$number:Int!){repository(owner:$owner,name:$name){issue(number:$number)"
    "{state projectItems(first:20,includeArchived:false){totalCount nodes{id "
    "project{id number owner{__typename ...on Organization{login}} "
    'field(name:"Status"){__typename ...on ProjectV2SingleSelectField{id name options{id name}}}} '
    "fieldValues(first:100){totalCount nodes{__typename "
    f"...on ProjectV2ItemFieldSingleSelectValue{{name optionId {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldTextValue{{text {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldNumberValue{{number {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldDateValue{{date {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldIterationValue{{iterationId title startDate duration {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldLabelValue{{labels(first:100){{totalCount nodes{{name}}}} {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldMilestoneValue{{milestone{{id}} {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldRepositoryValue{{repository{{id}} {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldUserValue{{users(first:100){{totalCount nodes{{login}}}} {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldPullRequestValue{{pullRequests(first:100){{totalCount nodes{{id}}}} {_BOARD_FIELD}}} "
    f"...on ProjectV2ItemFieldReviewerValue{{reviewers(first:100){{totalCount nodes{{__typename "
    f"...on User{{login}} ...on Team{{slug}} ...on Mannequin{{login}}}}}} {_BOARD_FIELD}}}"
    "}}}}}}}")


def board_repo_parts(repo: str) -> tuple[str, str]:
    owner, _, name = repo.partition("/")
    if not _BOARD_NAME.fullmatch(owner) or not _BOARD_NAME.fullmatch(name):
        raise RuntimeError("board reference is malformed")
    return owner, name


def board_card_key(node: dict) -> str:
    repo = (node.get("repository") or {}).get("nameWithOwner")
    if not isinstance(repo, str) or type(node.get("number")) is not int:
        raise RuntimeError("board card identity is incomplete")
    return f"{repo}#{node['number']}"


def _board_project3(nodes: Any) -> list[dict]:
    if not isinstance(nodes, list) or any(not isinstance(node, dict) for node in nodes):
        raise RuntimeError("board item list is malformed")
    return [node for node in nodes
            if (node.get("project") or {}).get("number") == BOARD_NUMBER
            and ((node.get("project") or {}).get("owner") or {}).get("login") == BOARD_OWNER]


def board_card(node: dict) -> dict | None:
    """The rules module's card shape for one issue or PR; None when it is not on Project 3."""
    key = board_card_key(node)
    repo, number = key.rsplit("#", 1)[0], node["number"]
    items = node.get("projectItems")
    if (not isinstance(items, dict) or type(items.get("totalCount")) is not int
            or not isinstance(items.get("nodes"), list) or items["totalCount"] != len(items["nodes"])):
        return {"repo": repo, "number": number, "unreadable": True}
    project3 = _board_project3(items["nodes"])
    if not project3:
        return None
    if len(project3) != 1:
        return {"repo": repo, "number": number, "unreadable": True}

    def single(value: Any) -> str | None:
        return value.get("name") if isinstance(value, dict) and isinstance(value.get("name"), str) else None
    parent = node.get("parent")
    return {"repo": repo, "number": number, "state": node.get("state"),
            "workLevel": single(project3[0].get("workLevel")), "status": single(project3[0].get("status")),
            "parent": ({"repo": board_card_key(parent).rsplit("#", 1)[0], "number": parent["number"]}
                       if isinstance(parent, dict) else None)}


def _board_complete(value: Any) -> None:
    """Every nested connection the read reported must be complete: a truncated value proves nothing."""
    if isinstance(value, dict):
        if "totalCount" in value and (not isinstance(value.get("nodes"), list)
                                      or value["totalCount"] != len(value["nodes"])):
            raise RuntimeError("board item value is truncated")
        for item in value.values():
            _board_complete(item)
    elif isinstance(value, list):
        for item in value:
            _board_complete(item)


def board_item_state(issue: dict) -> dict[str, Any]:
    """Normalize one issue's Project 3 item: identities, live Status options, Status and every other value."""
    state = issue.get("state")
    items = issue.get("projectItems")
    if state not in ("OPEN", "CLOSED") or not isinstance(items, dict) or items.get("totalCount") != len(items.get("nodes") or []):
        raise RuntimeError("board item read is incomplete")
    project3 = _board_project3(items["nodes"])
    if len(project3) != 1:
        raise RuntimeError("board item is absent or ambiguous")
    item = project3[0]
    project, field = item.get("project") or {}, (item.get("project") or {}).get("field") or {}
    options = field.get("options")
    if (not isinstance(item.get("id"), str) or not isinstance(project.get("id"), str)
            or field.get("__typename") != "ProjectV2SingleSelectField" or not isinstance(field.get("id"), str)
            or not isinstance(options, list)
            or any(not isinstance(o, dict) or not isinstance(o.get("id"), str) or not isinstance(o.get("name"), str)
                   for o in options)
            or len({o["name"] for o in options}) != len(options)):
        raise RuntimeError("board Status schema is unreadable")
    values = item.get("fieldValues")
    if not isinstance(values, dict) or values.get("totalCount") != len(values.get("nodes") or []):
        raise RuntimeError("board item values are truncated")
    _board_complete(values["nodes"])
    status_nodes = [v for v in values["nodes"] if isinstance(v, dict)
                    and ((v.get("field") or {}).get("id") == field["id"])]
    others = [v for v in values["nodes"] if v not in status_nodes]
    if len(status_nodes) > 1 or any(not isinstance(v, dict) for v in values["nodes"]):
        raise RuntimeError("board Status value is ambiguous")
    by_name = {o["name"]: o["id"] for o in options}
    status = None
    if status_nodes:
        status = status_nodes[0].get("name")
        if not isinstance(status, str) or by_name.get(status) != status_nodes[0].get("optionId"):
            raise RuntimeError("board Status value is not a live option")
    return {"state": state, "itemId": item["id"], "projectId": project["id"], "statusFieldId": field["id"],
            "options": [[o["id"], o["name"]] for o in options], "status": status,
            "values": sorted(json.dumps(v, sort_keys=True, separators=(",", ":")) for v in others)}


def load_board_rules() -> Any | None:
    """The shared R1 implementation, or None where it is not installed (receiver workspaces)."""
    path = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "board-currency-rules.py"
    try:
        if not path.is_file():
            return None
        spec = importlib.util.spec_from_file_location("routine_board_currency_rules", path)
        if spec is None or spec.loader is None:
            return None
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    except (Exception, SystemExit):
        # A broken or exiting module is an absent one; it never ends the merge helper.
        return None
    params = getattr(module, "PARAMS", None)
    logins = params.get("rootLogins") if isinstance(params, dict) else None
    if (not all(callable(getattr(module, name, None)) for name in ("override_of", "linkage_of", "pr_refs", "visible_text"))
            or not isinstance(logins, list) or not logins
            or not all(isinstance(login, str) and login for login in logins)):
        return None
    return module


@dataclass(frozen=True)
class BoardVerdict:
    verdict: str                     # linked | overridden | refused | unknown | unavailable
    reason: str | None
    delivery: str | None = None      # owner/repo#N of the linked Delivery


def evaluate_board_gate(api: Any, rules: Any, repo: str, number: int, rest_body: Any) -> BoardVerdict:
    """Design §2 evaluation order for one open routine PR. Pure apart from the API reads; never raises."""
    if rules is None:
        return BoardVerdict("unavailable", "rules-module-unavailable")
    try:
        pr = api.board_pr(repo, number)
    except Exception:
        return BoardVerdict("unknown", "pr-read-failed")
    if (not isinstance(pr, dict) or pr.get("body") != rest_body or pr.get("state") != "OPEN"
            or not isinstance(pr.get("edited"), bool)):
        # The REST envelope already passed eligibility; a different body or state is a moved candidate.
        return BoardVerdict("unknown", "pr-read-inconsistent")
    try:
        roots = {login.lower() for login in rules.PARAMS["rootLogins"]}
        if rules.override_of(pr, roots) is not None:
            return BoardVerdict("overridden", "valid-override")
        refs = sorted(rules.pr_refs(repo, pr["body"]) - {f"{repo}#{number}"})
    except Exception:
        return BoardVerdict("unknown", "rules-error")
    if len(refs) > BOARD_MAX_REFS:
        return BoardVerdict("unknown", "too-many-refs")
    try:
        cards = api.board_cards(refs) if refs else {}
    except Exception:
        return BoardVerdict("unknown", "board-read-failed")
    if not isinstance(cards, dict) or any(ref not in cards for ref in refs):
        return BoardVerdict("unknown", "board-read-incomplete")
    try:
        # Each ref is resolved from its own issue, so a miss is a definite "not on Project 3".
        failures, delivery = rules.linkage_of(dict(pr, repo=repo, number=number, state="OPEN"), cards.get,
                                              anonymous_unreadable=False)
    except Exception:
        return BoardVerdict("unknown", "rules-error")
    if not isinstance(failures, list) or any(
            not isinstance(f, tuple) or len(f) != 2 or f[0] not in ("R1", "U")
            or not isinstance(f[1], str) or not _BOARD_TOKEN.fullmatch(f[1]) for f in failures):
        return BoardVerdict("unknown", "rules-error")
    unknown = [reason for rule, reason in failures if rule == "U"]
    if unknown:
        return BoardVerdict("unknown", f"U {unknown[0]}")
    if failures:
        return BoardVerdict("refused", f"R1 {failures[0][1]}")
    if not isinstance(delivery, dict) or not isinstance(delivery.get("repo"), str) or type(delivery.get("number")) is not int:
        return BoardVerdict("unknown", "rules-error")
    return BoardVerdict("linked", None, f"{delivery['repo']}#{delivery['number']}")


def board_closing_refs(rules: Any, repo: str, body: Any) -> set[str]:
    refs: set[str] = set()
    for match in _BOARD_CLOSING.finditer(rules.visible_text(body)):
        for ref in _BOARD_ONE_REF.finditer(match.group(1)):
            refs.add(f"{ref.group(1) or repo}#{int(ref.group(2))}".lower())
    return refs


def _board_item_valid(value: Any) -> bool:
    return (isinstance(value, dict)
            and set(value) == {"state", "itemId", "projectId", "statusFieldId", "options", "status", "values"}
            and value["state"] in ("OPEN", "CLOSED")
            and all(isinstance(value[k], str) and _BOARD_NODE_ID.fullmatch(value[k])
                    for k in ("itemId", "projectId", "statusFieldId"))
            and isinstance(value["options"], list) and isinstance(value["values"], list)
            and all(isinstance(o, list) and len(o) == 2 and all(isinstance(x, str) for x in o) for o in value["options"])
            and len({o[1] for o in value["options"]}) == len(value["options"])
            and (value["status"] is None or isinstance(value["status"], str)))


def board_transition(api: Any, rules: Any, repo: str, body: Any, delivery: str, mode: str) -> str:
    """The one post-merge Delivery Status transition (design §2b steps 1-6). Never raises, never sets Done,
    never resends. Advisory mode reads only and reports what enforce would do."""
    refuse = "refused-preimage" if mode == "enforce" else "would-refuse-preimage"
    try:
        target = "In progress" if BOARD_CONTINUES in rules.visible_text(body) else "In review"
        if delivery.lower() in board_closing_refs(rules, repo, body):
            return "not-applicable"
    except Exception:
        return refuse
    try:
        before = api.board_item(delivery)
    except Exception:
        return refuse
    if not _board_item_valid(before):
        return refuse
    if before["state"] == "CLOSED":
        return "not-applicable"
    if before["status"] == target:
        return "noop"
    options = {name: identity for identity, name in before["options"]}
    if before["status"] not in BOARD_WRITABLE_PREIMAGES or not _BOARD_NODE_ID.fullmatch(options.get(target) or ""):
        # Blocked records Root's judgement; Done, empty and unknown values are never overwritten.
        return refuse
    if mode != "enforce":
        return f"would-write:{target}"
    try:
        adjacent = api.board_item(delivery)
    except Exception:
        return "refused-preimage"
    if adjacent != before:
        return "refused-preimage"
    # Possible-send custody: from here on the only outcomes are a verified write or uncertain.
    print("fsgg routine board transition: possible-send recorded; one Status send follows", file=sys.stderr)
    try:
        api.board_set_status(before["projectId"], before["itemId"], before["statusFieldId"], options[target])
    except Exception:
        return "uncertain"
    try:
        after = api.board_item(delivery)
    except Exception:
        return "uncertain"
    return "written" if after == dict(before, status=target) else "uncertain"


def board_routine_body(body: Any) -> bool:
    return isinstance(body, str) and BOARD_ROUTINE_MARKER in body


def summarize(
    api: NativeApi,
    *,
    repo: str,
    pr_number: int,
    expected_head: str,
    merge_method: str,
    publication_required: bool,
    apply: bool,
    coherent_workflow: str | None = None,
    candidate_observer: Callable[[Summary], None] | None = None,
    producer_source: str | None = None,
    installation_prior: dict | None = None,
    board_gate: str = "advisory",
    board_rules_loader: Callable[[], Any] | None = None,
) -> tuple[int, Summary]:
    if board_gate not in BOARD_GATE_MODES:
        raise RuntimeError("board gate mode is not advisory or enforce")
    if producer_source is not None and repo == FOURD_REPOSITORY and merge_method != "squash":
        raise RuntimeError("FourD producer installation requires the accepted squash merge method")
    if producer_source is not None and (publication_required or not coherent_workflow
                                        or not SHA_RE.fullmatch(producer_source)):
        raise RuntimeError("producer installation requires a pinned source, completed coherent workflow and no publication")
    if installation_prior is not None:
        if (producer_source is None or not isinstance(installation_prior, dict)
                or not installation_result_shape(installation_prior)
                or installation_prior.get("schema") != "fsgg.routine-delivery/v1"
                or installation_prior.get("repo") != repo or installation_prior.get("pr") != pr_number
                or type(installation_prior.get("pr")) is not int
                or installation_prior.get("expectedHead") != expected_head
                or installation_prior.get("producerSource") != producer_source
                or type(installation_prior.get("attempts")) is not int
                or not 0 <= installation_prior["attempts"] <= 1
                or installation_prior.get("routineEligibility") not in ("unavailable-first-install", "unresolved")
                or installation_prior.get("producerInstallation") not in (
                    "ready", "refused", "indeterminate", "delivered-activation-pending", "disputed")
                or installation_prior.get("outcome") not in ("ready", "refused", "indeterminate", "delivered", "delivered-disputed")
                or installation_prior.get("codeDelivery") not in ("not-delivered", "unknown", "delivered")
                or installation_prior.get("publication") != "not-required"
                or not isinstance(installation_prior.get("baseSha"), str)
                or not SHA_RE.fullmatch(installation_prior["baseSha"])
                or not isinstance(installation_prior.get("baseRef"), str) or not installation_prior["baseRef"]):
            raise RuntimeError("installation prior result identity/attempt/schema differs")
    publication = "pending" if publication_required else "not-required"
    before = api.get_pr(repo, pr_number)
    if not isinstance(before, dict):
        raise RuntimeError("native pull request readback is not an object")
    base_ref, base_sha = base_of(before)
    routine_state = "unobserved"
    board = {"boardGate": "not-applicable", "boardGateReason": None, "boardTransition": "not-applicable"}
    def bound(*values: Any, native: dict[str, Any] = before) -> Summary:
        now = datetime.now(timezone.utc)
        outcome_at = outcome_time_of(native)
        if outcome_at:
            parsed_outcome = datetime.fromisoformat(outcome_at.replace("Z", "+00:00"))
            if parsed_outcome > now:
                now = parsed_outcome
        result = Summary(*values, baseRef=base_ref, baseSha=base_sha,
                       outcomeAt=outcome_at,
                       observedAt=now.isoformat().replace("+00:00", "Z"),
                       routineEligibility=routine_state, **board)
        if producer_source is not None:
            if result.reason is None and getattr(api, "_installation_policy_fact", None) in ("required-policy-observed-empty", "no-required-contexts-reported",
                    "native-policy-unavailable; no-required-contexts-reported", "native-policy-unavailable; required-contexts-passed"):
                result = replace(result, reason=api._installation_policy_fact)
            state = "ready" if result.outcome == "ready" else result.outcome
            if result.codeDelivery == "delivered":
                try:
                    api.installation_contract(repo, pr_number, native, expected_head, producer_source, delivered=True)
                except RuntimeError as error:
                    # Source merge history survives, but installation is not accepted.
                    result = replace(result, outcome="delivered-disputed", reason=str(error))
                    state = "disputed"
                else:
                    state = "delivered-activation-pending" if result.outcome == "delivered" else "disputed"
            result = replace(result, producerInstallation=state, producerSource=producer_source)
        if installation_prior is not None:
            result = replace(result, attempts=max(result.attempts, installation_prior["attempts"]))
        return result
    allowed, reason = eligible(before, expected_head)
    observed = head_of(before)
    if installation_prior is not None and base_of(before) != (installation_prior["baseRef"], installation_prior["baseSha"]):
        raise RuntimeError("installation prior result original base differs")
    if installation_prior is not None and installation_prior["attempts"] > 0 and not is_merged(before):
        # Even an open PR does not settle a lost response or authorize another write.
        routine_state = "unresolved"
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "indeterminate", "unknown", publication, None, 0,
            "original installation effect remains unresolved; prior result forces readback-only",
            "current", "unobserved",
        )
    if not allowed:
        return 2, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "refused", "not-delivered", publication, None, 0, reason, "current", "unobserved",
        )
    if not is_merged(before) and (base_ref is None or base_sha is None):
        return 2, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "refused", "not-delivered", publication, None, 0,
            "pull request base identity is unreadable", "current", "unobserved",
        )
    routine_reason = None
    try:
        if producer_source is not None:
            api.installation_contract(repo, pr_number, before, expected_head, producer_source,
                                      delivered=is_merged(before), coherent_workflow=coherent_workflow)
            routine_state = "unavailable-first-install"
        else:
            routine_state = routine_contract(api, repo, before, expected_head)
    except RuntimeError as error:
        routine_state, routine_reason = "unresolved", str(error)
        if not is_merged(before):
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, routine_reason, "current", "unobserved",
            )
    disposition, coherent = "current", "not-required"
    if coherent_workflow:
        disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
        if not is_merged(before) and (disposition in {"invalid", "deferred", "failed"}
                                      or (coherent != "passed" and (producer_source is not None or disposition != "reused"))):
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, reason,
                disposition, coherent,
            )
    if is_merged(before):
        coherent_disputed = coherent == "failed" or disposition in {"invalid", "deferred", "failed"}
        disputed = coherent_disputed or routine_state == "unresolved"
        merged_result = bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "delivered-disputed" if disputed else "delivered", "delivered",
            publication, merged_commit_of(before), 0, routine_reason or (reason if disputed else None), disposition,
            "disputed" if coherent_disputed else coherent,
        )
        return 4 if merged_result.outcome == "delivered-disputed" else 0, merged_result
    if observed == expected_head and candidate_observer is not None:
        candidate_observer(bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "ready", "not-delivered", publication, None, 0, None, disposition, coherent,
        ))
    if not apply:
        return 0, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "ready", "not-delivered", publication, None, 0, None, disposition, coherent,
        )

    if routine_state in ("passed", "unavailable-first-install"):
        current = None
        try:
            current = api.get_pr(repo, pr_number)
            current_allowed, current_reason = eligible(current, expected_head)
            if (not current_allowed or is_merged(current) or base_of(current) != base_of(before)
                    or current.get("body") != before.get("body")
                    or (current.get("head") or {}).get("ref") != (before.get("head") or {}).get("ref")):
                raise RuntimeError(current_reason or "routine PR envelope changed before merge")
            if producer_source is not None:
                api.installation_contract(repo, pr_number, current, expected_head, producer_source,
                                          coherent_workflow=coherent_workflow)
                disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
                if coherent != "passed" or disposition in ("invalid", "deferred", "failed"):
                    raise RuntimeError("installation coherent validation changed before merge")
            else:
                routine_state = routine_contract(api, repo, current, expected_head)
                if routine_state != "passed":
                    raise RuntimeError("routine route changed before merge")
        except RuntimeError as error:
            routine_state = "unresolved"
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(current) if isinstance(current, dict) else None,
                "refused", "not-delivered", publication, None, 0, str(error), disposition, coherent,
            )

    gate: BoardVerdict | None = None
    board_rules = None
    if board_routine_body(before.get("body")):
        # Design §2 evaluation order, before the merge effect and only under --apply (this point).
        board_rules = (board_rules_loader or load_board_rules)()
        gate = evaluate_board_gate(api, board_rules, repo, pr_number, before.get("body"))
        board.update(boardGate=gate.verdict, boardGateReason=gate.reason)
        if board_gate == "enforce" and gate.verdict == "refused":
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, f"board-linkage {gate.reason}",
                disposition, coherent,
            )
        if board_gate == "enforce" and gate.verdict in ("unknown", "unavailable"):
            # Fail closed: no merge attempt, so attempts stay 0 and delivery is certainly absent.
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "indeterminate", "not-delivered", publication, None, 0, "board-linkage unknown",
                disposition, coherent,
            )

    def board_after_merge() -> None:
        # After the merged-PR and merge-commit readback, and only for a linked Delivery.
        if gate is not None and gate.verdict == "linked" and gate.delivery is not None:
            board["boardTransition"] = board_transition(
                api, board_rules, repo, before.get("body"), gate.delivery, board_gate)

    request = merge_effect_request(
        repo, pr_number, expected_head, base_ref, base_sha, merge_method,
    )
    attempts = 1
    response: dict[str, Any] | None = None
    refused: str | None = None
    try:
        response = api.merge(request)
    except NativeMergeRefused as error:
        refused = str(error)
    except (AmbiguousWrite, RuntimeError):
        pass

    try:
        after = api.get_pr(repo, pr_number)
    except RuntimeError:
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, None,
            "indeterminate", "unknown", publication, None, attempts,
            "merge response cannot be confirmed because native PR readback is unavailable",
            disposition, coherent,
        )
    if not isinstance(after, dict):
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head,
            None,
            "indeterminate", "unknown", publication, None, attempts,
            "native PR readback has a malformed shape",
            disposition, coherent,
        )
    readback_commit = correlated_merge_readback(after, request)
    if response is not None and not isinstance(response, dict):
        response = None
    if response is None:
        if readback_commit is None:
            if refused is not None:
                return 2, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "refused", "not-delivered", publication, None, attempts,
                    f"GitHub refused the head-conditioned merge: {refused}", disposition, coherent,
                )
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "ambiguous merge response; exact native merged-state readback is absent",
                disposition, coherent,
            )
        merge_commit = readback_commit
        effect_confirmed = True
    else:
        merge_commit = response.get("sha")
        effect_confirmed = (
            response.get("merged") is True
            and isinstance(merge_commit, str) and SHA_RE.fullmatch(merge_commit) is not None
            and merge_commit == readback_commit
        )
    if coherent_workflow:
        try:
            disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
        except RuntimeError:
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "post-merge coherent validation is unavailable after one effect attempt",
                disposition, coherent,
            )
        if coherent == "failed" or disposition in {"invalid", "deferred", "failed"}:
            if not effect_confirmed:
                return 3, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "indeterminate", "unknown", publication, None, attempts,
                    "coherent validation failed and the merge effect remains unproven",
                    disposition, coherent,
                )
            board_after_merge()
            return 4, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "delivered-disputed", "delivered", publication, merge_commit, attempts, reason,
                disposition, "disputed", native=after,
            )
    if effect_confirmed:
        board_after_merge()
        delivered_result = bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
            "delivered", "delivered", publication, merge_commit, attempts, None,
            disposition, coherent, native=after,
        )
        return 4 if delivered_result.outcome == "delivered-disputed" else 0, delivered_result
    return 3, bound(
        "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
        "indeterminate", "unknown", publication, None, attempts,
        "merge response and native PR readback do not both establish delivery", disposition, coherent,
    )


# This opt-in native observer owns only its direct gh query children. It is not
# a process-tree supervisor, readiness evaluator or delivery authorization.
WATCH_MAX_SECONDS = 600
WATCH_MAX_QUERIES = 384
WATCH_MAX_CHECKS = 128
WATCH_QUERY_BYTES = 262_144
WATCH_EMIT_BYTES = 4_194_304
WATCH_QUERY_SECONDS = 30.0
WATCH_CLEANUP_SECONDS = 0.5
WATCH_INTERVAL_SECONDS = 5.0
WATCH_FIELDS = "bucket,completedAt,event,link,name,startedAt,state,workflow"


class WatchDeadline(RuntimeError):
    """The original watch budget cannot fit another bounded query."""


def bounded_watch_query(
    command: list[str], deadline: float, *, clock: Callable[[], float] = time.monotonic,
    popen: Callable[..., Any] = subprocess.Popen,
    max_seconds: float = WATCH_QUERY_SECONDS, max_bytes: int = WATCH_QUERY_BYTES,
    env: dict[str, str] | None = None, stderr_output: bytearray | None = None,
) -> tuple[int, bytes]:
    """Bound both streams before retaining; kill/reap the same direct child on error."""
    work_end = min(deadline - WATCH_CLEANUP_SECONDS, clock() + max_seconds)
    if work_end <= clock():
        raise WatchDeadline("original watch cleanup reserve reached")
    process_options = {} if env is None else {"env": env}
    process = popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE, close_fds=True, **process_options)
    output = bytearray()
    used = 0
    selector = None
    try:
        selector = selectors.DefaultSelector()
        for stream, stdout in ((process.stdout, True), (process.stderr, False)):
            os.set_blocking(stream.fileno(), False)
            selector.register(stream, selectors.EVENT_READ, stdout)
        while selector.get_map() or process.poll() is None:
            remaining = work_end - clock()
            if remaining <= 0:
                if clock() >= deadline - WATCH_CLEANUP_SECONDS:
                    raise WatchDeadline("native query reached original watch cleanup reserve")
                raise RuntimeError("native query exceeded its clipped timeout")
            for key, _mask in selector.select(min(remaining, 0.1)):
                raw = os.read(key.fileobj.fileno(), min(65_536, max_bytes - used + 1))
                if not raw:
                    selector.unregister(key.fileobj)
                    continue
                if used + len(raw) > max_bytes:
                    raise RuntimeError("native query combined stdout/stderr exceeded byte bound")
                used += len(raw)
                if key.data:
                    output.extend(raw)
                elif stderr_output is not None:
                    stderr_output.extend(raw)
        process.wait(timeout=max(0, min(WATCH_CLEANUP_SECONDS, deadline - clock())))
        return process.returncode, bytes(output)
    finally:
        try:
            if process.poll() is None:
                process.kill()  # Popen retains the original unreaped direct child.
            process.wait(timeout=max(0, min(WATCH_CLEANUP_SECONDS, deadline - clock())))
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("original native query retirement is unknown at watch deadline") from error
        finally:
            if selector is not None:
                selector.close()
            process.stdout.close()
            process.stderr.close()


class WatchGhApi(GhApi):
    """Existing native adapter with watch-only finite query/population controls."""

    def __init__(self, deadline: float, *, query: Callable[..., tuple[int, bytes]] = bounded_watch_query,
                 clock: Callable[[], float] = time.monotonic):
        self.deadline, self.query, self.clock = deadline, query, clock
        self.query_count = 0

    def _watch_read(self, command: list[str], accepted: tuple[int, ...]) -> tuple[int, Any]:
        if self.query_count >= WATCH_MAX_QUERIES:
            raise RuntimeError("native watch query-count bound reached")
        if self.clock() >= self.deadline - WATCH_CLEANUP_SECONDS:
            raise WatchDeadline("original watch cleanup reserve reached")
        self.query_count += 1
        code, raw = self.query(command, self.deadline, clock=self.clock)
        if len(raw) > WATCH_QUERY_BYTES:
            raise RuntimeError("native query response exceeded byte bound")
        if code not in accepted:
            raise RuntimeError(f"native read failed with exit code {code}")
        try:
            return code, json.loads(raw)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise RuntimeError("native read returned malformed JSON") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        _code, value = self._watch_read(["gh", "api", f"repos/{repo}/pulls/{pr}"], (0,))
        if not isinstance(value, dict):
            raise RuntimeError("native read returned a non-object pull request")
        return value

    def checks(self, repo: str, pr: int) -> list[dict[str, Any]]:
        # Native bucket meanings/fields: https://cli.github.com/manual/gh_pr_checks
        code, value = self._watch_read(
            ["gh", "pr", "checks", str(pr), "--repo", repo, "--json", WATCH_FIELDS], (0,),
        )
        if not isinstance(value, list) or len(value) > WATCH_MAX_CHECKS:
            raise RuntimeError("native checks population is malformed or exceeds bound")
        fields = set(WATCH_FIELDS.split(","))
        identities: set[tuple[str, ...]] = set()
        for row in value:
            if (not isinstance(row, dict) or set(row) != fields
                    or any(not isinstance(row[name], str) for name in fields)
                    or not row["name"] or not row["state"]
                    or row["bucket"] not in {"pass", "fail", "pending", "skipping", "cancel"}):
                raise RuntimeError("native checks contain incomplete or unknown fields")
            identity = tuple(row[name] for name in ("name", "workflow", "link", "event"))
            if identity in identities:
                raise RuntimeError("native checks contain duplicate identities")
            identities.add(identity)
        return sorted(value, key=lambda row: tuple(row[name] for name in ("name", "workflow", "link", "event")))



def delivery_evidence(summary: Summary, run_id: int | None, *, api: Any = None, assembled: bool = False) -> dict:
    """Load optional evidence support without expanding the installed merge helper."""
    path = pathlib.Path(__file__).with_name("routine-delivery-evidence.py")
    spec = importlib.util.spec_from_file_location("routine_delivery_evidence", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("optional evidence reader unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    evidence = module.collect(summary, run_id, api=api or WatchGhApi(time.monotonic() + 60),
                              native=sys.modules[__name__])
    return module.assemble(summary, evidence) if assembled else evidence


def watch_checks(
    repo: str, pr: int, head: str, seconds: int, *,
    clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep,
    utc_now: Callable[[], datetime] = lambda: datetime.now(timezone.utc),
    api_factory: Callable[..., Any] = WatchGhApi,
    emit: Callable[[dict[str, Any]], None] | None = None,
) -> int:
    """Emit compact changes only; a terminal native sample never grants apply."""
    if type(seconds) is not int or not 1 <= seconds <= WATCH_MAX_SECONDS:
        raise ValueError("--watch-seconds must be between 1 and 600")
    deadline = clock() + seconds
    api = api_factory(deadline, clock=clock)
    previous = None
    revision = 0
    emitted_bytes = 0

    def publish(event: str, projection: Any = None, reason: str | None = None) -> None:
        nonlocal emitted_bytes
        value = {"schema": "fsgg.routine-check-watch/v1", "event": event,
                 "repository": repo, "pullRequest": pr, "expectedHead": head,
                 "revision": revision, "observedAt": utc_now().isoformat(),
                 "nativeQueries": api.query_count, "projection": projection,
                 "reason": reason, "readiness": "not-evaluated", "applyAuthorized": False}
        raw = json.dumps(value, separators=(",", ":"), sort_keys=True)
        size = len(raw.encode("utf-8")) + 1
        limit = WATCH_EMIT_BYTES if event in {"observation-failure", "deadline"} else WATCH_EMIT_BYTES - 4096
        if emitted_bytes + size > limit:
            raise RuntimeError("watch emitted-output byte bound reached")
        emitted_bytes += size
        if emit is None:
            print(raw, flush=True)
        else:
            emit(value)

    try:
        while True:
            if clock() >= deadline - WATCH_CLEANUP_SECONDS:
                publish("deadline", reason="original watch deadline or cleanup reserve reached")
                return 3
            before = api.get_pr(repo, pr)
            if head_of(before) != head:
                raise RuntimeError("exact expected head changed before native check read")
            checks = api.checks(repo, pr)
            after = api.get_pr(repo, pr)
            if head_of(after) != head:
                raise RuntimeError("exact expected head changed after native check read")
            if (after.get("state") not in {"open", "closed"}
                    or type(after.get("merged")) is not bool or type(after.get("draft")) is not bool):
                raise RuntimeError("native pull request state observation is incomplete")
            if clock() >= deadline:
                raise WatchDeadline("completed read arrived after original watch deadline")
            projection = {"head": head, "state": after["state"], "merged": after["merged"],
                          "draft": after["draft"], "checks": checks,
                          "sourceUpdatedAt": after.get("updated_at")}
            # Actual source check timestamps identify starts/completions. Observer
            # time and unrelated PR update timestamps do not wake the caller.
            material = {key: value for key, value in projection.items() if key != "sourceUpdatedAt"}
            fingerprint = json.dumps(material, separators=(",", ":"), sort_keys=True)
            changed = fingerprint != previous
            if changed:
                revision += 1
            buckets = {row["bucket"] for row in checks}
            failed = bool(buckets & {"fail", "cancel"})
            terminal = after["state"] == "closed" or failed or (bool(checks) and "pending" not in buckets)
            if terminal:
                publish("terminal", projection, "pull request closed" if after["state"] == "closed"
                        else "native failed/cancelled check observed" if failed else "native checks no longer pending")
                return 2 if failed or after["state"] == "closed" else 0
            if changed:
                publish("initial" if previous is None else "revision", projection,
                        "no checks observed; completeness and readiness remain unknown" if not checks else None)
                previous = fingerprint
            sleep(min(WATCH_INTERVAL_SECONDS, max(0, deadline - WATCH_CLEANUP_SECONDS - clock())))
    except WatchDeadline as error:
        publish("deadline", reason=str(error))
        return 3
    except KeyboardInterrupt:
        publish("observation-failure", reason="caller cancelled watch; no retry or mutation")
        return 130
    except (OSError, RuntimeError) as error:
        # Reserve a tiny final failure even if prior material output filled its cap.
        publish("observation-failure", reason=str(error)[:1024])
        return 3


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--repo", required=True, help="OWNER/REPO")
    result.add_argument("--pr", required=True, type=int)
    result.add_argument("--head", required=True)
    result.add_argument("--merge-method", choices=("merge", "squash", "rebase"), default="squash")
    result.add_argument("--publication", choices=("none", "required"), default="none")
    result.add_argument("--coherent-workflow", help="candidate-scoped coherent workflow file or id")
    result.add_argument("--telemetry-unavailable", choices=("local-clr-resource-hold",),
                        help="record an explicit advisory collection gap without starting CLR telemetry")
    result.add_argument("--telemetry-assignment", help="private CI assignment for advisory automatic observation")
    result.add_argument("--telemetry-store-root", help="private durable telemetry store root")
    result.add_argument("--telemetry-config", help="private host telemetry configuration; otherwise use the canonical discovery order")
    result.add_argument("--telemetry-feature", help="stable feature identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-item", help="stable item identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-attempt", help="stable attempt identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-parent-attempt", help="optional stable parent attempt identity")
    result.add_argument("--telemetry-engine", help="installed telemetry-capable coordination engine; overrides host configuration")
    result.add_argument("--telemetry-publisher-event", action="store_true",
                        help="one advisory dashboard refresh after completed final CI observation")
    result.add_argument("--telemetry-publisher-config",
                        help="absolute dashboard projection config; defaults to FSGG_TELEMETRY_DASHBOARD_CONFIG or the telemetry config")
    result.add_argument("--telemetry-advisory-deadline-monotonic", type=float,
                        help="caller-owned absolute monotonic deadline including direct-child cleanup; no default")
    result.add_argument("--watch-checks", action="store_true", help="read-only bounded native-check watch; never grants apply")
    result.add_argument("--watch-seconds", type=int, help="required watch budget, 1..600 seconds")
    result.add_argument("--evidence-run", type=int, help="append verified PR checkout evidence from this native run")
    result.add_argument("--apply", action="store_true")
    result.add_argument("--board-gate", choices=BOARD_GATE_MODES, default="advisory",
                        help="board-currency merge gate under --apply for routine PRs; advisory records only")
    result.add_argument("--install-routine-producer", action="store_true",
                        help="closed first installation only; never ordinary eligibility success")
    result.add_argument("--routine-producer-source", help="immutable protected FS-GG/.github template commit")
    result.add_argument("--installation-prior-result", help="retained installation Summary; prior effects force readback-only")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    if not re.fullmatch(r"[^/\s]+/[^/\s]+", args.repo):
        parser().error("--repo must be OWNER/REPO")
    if args.pr <= 0:
        parser().error("--pr must be positive")
    if not SHA_RE.fullmatch(args.head):
        parser().error("--head must be a lowercase 40-hex commit SHA")
    if args.evidence_run is not None and (args.evidence_run <= 0 or args.watch_checks):
        parser().error("--evidence-run requires a positive run ID and cannot combine with watch")
    if args.evidence_run is not None and args.install_routine_producer:
        parser().error("--evidence-run cannot combine with installation recovery")
    unavailable_audit = None
    if args.telemetry_unavailable:
        if (args.watch_checks or args.telemetry_assignment is not None
                or args.telemetry_store_root is not None or args.telemetry_config is not None
                or args.telemetry_engine is not None or args.telemetry_publisher_event
                or args.telemetry_publisher_config is not None
                or args.telemetry_advisory_deadline_monotonic is not None
                or os.environ.get("FSGG_TELEMETRY_CONFIG")):
            parser().error("--telemetry-unavailable conflicts with watch, telemetry configuration or publication options")
        identities = [args.telemetry_feature, args.telemetry_item, args.telemetry_attempt]
        if (any(value is not None for value in identities)
                and not all(isinstance(value, str) and value.strip() for value in identities)):
            parser().error("unavailable audit requires a complete nonempty feature/item/attempt trio")
        if args.telemetry_parent_attempt is not None and (
                not all(identities) or not args.telemetry_parent_attempt.strip()):
            parser().error("unavailable parent attempt requires a complete identity trio and nonempty value")
        unavailable_audit = {
            "schema": "fsgg.routine-telemetry-unavailable/1", "reason": args.telemetry_unavailable,
            "repo": args.repo, "pr": args.pr, "expectedHead": args.head,
        }
        if all(identities):
            unavailable_audit["identity"] = dict(zip(("feature", "item", "attempt"), identities))
            if args.telemetry_parent_attempt is not None:
                unavailable_audit["identity"]["parentAttempt"] = args.telemetry_parent_attempt
        if len(json.dumps(unavailable_audit, separators=(",", ":")).encode("utf-8")) > 2048:
            parser().error("unavailable audit identity exceeds 2048 bytes")
    if args.install_routine_producer:
        if (args.watch_checks or args.publication != "none" or not args.coherent_workflow
                or not args.routine_producer_source or not SHA_RE.fullmatch(args.routine_producer_source)):
            parser().error("--install-routine-producer requires --routine-producer-source, --coherent-workflow and publication none; no watch")
    elif args.routine_producer_source is not None or args.installation_prior_result is not None:
        parser().error("producer source/prior result requires --install-routine-producer")
    try:
        prior = installation_prior_result(args.installation_prior_result) if args.installation_prior_result else None
    except RuntimeError as error:
        parser().error(str(error))
    deadline = args.telemetry_advisory_deadline_monotonic
    if deadline is not None and (not math.isfinite(deadline) or deadline <= 0):
        parser().error("--telemetry-advisory-deadline-monotonic must be finite and positive")
    if args.watch_checks and (args.telemetry_publisher_event or args.telemetry_publisher_config is not None or deadline is not None):
        parser().error("advisory event/deadline options are incompatible with --watch-checks")
    if args.watch_checks:
        if len(args.repo) > 255:
            parser().error("watch repository identity exceeds 255 characters")
        if args.apply:
            parser().error("--watch-checks is incompatible with --apply; apply requires a fresh canonical recheck")
        if args.watch_seconds is None or not 1 <= args.watch_seconds <= WATCH_MAX_SECONDS:
            parser().error("--watch-checks requires --watch-seconds between 1 and 600")
        return watch_checks(args.repo, args.pr, args.head, args.watch_seconds)
    if args.watch_seconds is not None:
        parser().error("--watch-seconds requires --watch-checks")
    if args.telemetry_store_root and not args.telemetry_assignment:
        parser().error("--telemetry-store-root requires --telemetry-assignment")
    identity_values = [args.telemetry_feature, args.telemetry_item, args.telemetry_attempt]
    if any(identity_values) and not all(identity_values):
        parser().error("--telemetry-feature, --telemetry-item and --telemetry-attempt must be supplied together")
    observer = None
    observation_health: list[str] = []
    assignment, store_root = args.telemetry_assignment, args.telemetry_store_root
    config_path, telemetry_repository = args.telemetry_config, args.repo
    workspace_transport = bool(config_path and not store_root)
    engine = args.telemetry_engine or "fsgg-coord-engine"
    if args.telemetry_unavailable:
        observation_health.append("unavailable")
    elif not assignment and not store_root:
        try:
            config = discover_telemetry_config(args.telemetry_config, command_engine=engine)
            if config is not None:
                engine = args.telemetry_engine or config.engine
                workspace_transport = config.workspace
                config_path = config.path
                telemetry_repository = config.repository or args.repo
                if all(identity_values):
                    assignment = create_ci_assignment(
                        config,
                        feature=args.telemetry_feature, item=args.telemetry_item,
                        attempt=args.telemetry_attempt, parent_attempt=args.telemetry_parent_attempt,
                        command_engine=args.telemetry_engine or "fsgg-coord-engine",
                    )
                    if not config.workspace:
                        store_root = config.store_root
                else:
                    observation_health.append("unavailable")
                    print("fsgg routine telemetry: host is configured but feature/item/attempt identities are missing", file=sys.stderr)
        except (OSError, RuntimeError) as error:
            observation_health.append("unavailable")
            print(f"fsgg routine telemetry: host configuration unavailable: {error}", file=sys.stderr)
    if assignment and (store_root or config_path or not args.telemetry_store_root):
        def observer(summary: Summary) -> None:
            options = {"assignment": assignment, "store_root": store_root, "engine": engine}
            if workspace_transport:
                options.update({"config": config_path, "repository": telemetry_repository})
            if deadline is not None:
                options["deadline"] = deadline
            observation_health.append(observe_candidate(summary, **options))
    try:
        code, result = summarize(
            GhApi(), repo=args.repo, pr_number=args.pr, expected_head=args.head,
            merge_method=args.merge_method, publication_required=args.publication == "required",
            apply=args.apply, coherent_workflow=args.coherent_workflow, candidate_observer=observer,
            producer_source=args.routine_producer_source if args.install_routine_producer else None,
            installation_prior=prior, board_gate=args.board_gate,
        )
    except (RuntimeError, AmbiguousWrite) as error:
        code = 3
        result = Summary(
            "fsgg.routine-delivery/v1", args.repo, args.pr, args.head, None,
            "indeterminate", "unknown",
            "pending" if args.publication == "required" else "not-required",
            None, prior["attempts"] if prior is not None and type(prior.get("attempts")) is int and prior["attempts"] == 1 else 0,
            str(error), "current", "unobserved",
        )
        if args.install_routine_producer:
            joined = prior is not None and all(prior.get(key) == value for key, value in (
                ("repo", args.repo), ("pr", args.pr), ("expectedHead", args.head),
                ("producerSource", args.routine_producer_source)))
            result = replace(result, producerInstallation="indeterminate",
                producerSource=args.routine_producer_source, routineEligibility="unresolved",
                baseRef=prior.get("baseRef") if joined else None,
                baseSha=prior.get("baseSha") if joined else None)
    dashboard_health = "not-run-final-ci-unavailable"
    if observer is not None and result.outcome != "ready":
        observer(result)
        if args.telemetry_publisher_event and observation_health[-1] != "unavailable":
            projection_config = args.telemetry_publisher_config
            if projection_config is None:
                projection_config = os.environ.get("FSGG_TELEMETRY_DASHBOARD_CONFIG")
            if projection_config is not None and not os.path.isabs(projection_config):
                dashboard_health = "not-run-config-invalid"
            else:
                dashboard_health = observe_dashboard_event(
                    engine=engine, config=config_path if projection_config is None else projection_config,
                    deadline=deadline,
                )
    if observer is not None or observation_health:
        result = replace(result, telemetryHealth=observation_health[-1] if observation_health else "unavailable")
    output = asdict(result)
    if args.evidence_run is not None or (args.repo == "FS-GG/.github" and not args.install_routine_producer):
        try:
            output = delivery_evidence(result, args.evidence_run, assembled=True)
        except Exception:
            # Optional acquisition/reporting cannot suppress the original effect readback.
            output["deliveryEvidence"] = {"schema": "fsgg.routine-delivery-evidence/1",
                "scope": "github-pr-checkout", "parity": "unavailable",
                "exceptions": ["collection-unavailable"], "assessment": "attention",
                "issues": ["collection-unavailable"]}
    if args.telemetry_publisher_event:
        output["dashboardPublicationHealth"] = dashboard_health
    if unavailable_audit is not None:
        unavailable_audit.update(outcome=result.outcome, codeDelivery=result.codeDelivery)
        print(json.dumps(unavailable_audit, separators=(",", ":"), sort_keys=True), file=sys.stderr)
    print(json.dumps(output, separators=(",", ":"), sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

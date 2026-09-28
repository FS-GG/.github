#!/usr/bin/env python3
"""Exact-object native Git CAS for the registered Q4 sandbox nonce ref.

Source-only: a protected host must install a separate, independent bootstrap
admission before it may call ``apply``. A successful source test is not a grant.
The S2 declaration remains immutable and non-activating before and after CAS.
"""

from __future__ import annotations

import base64
import datetime as dt
import hashlib
import json
import os
import re
import stat
import subprocess
import tempfile
from contextlib import contextmanager
from pathlib import Path


SCHEMA = "fsgg.gs2-09-7.sandbox-nonce-ref-cas-readback/1"
REPOSITORY = {"id": 1353050537, "nodeId": "R_kgDOUKXpqQ",
              "fullName": "FS-GG/FS.GG.GitHub.Substrate.Sandbox"}
REMOTE = "https://github.com/FS-GG/FS.GG.GitHub.Substrate.Sandbox.git"
INSTALLATION_STATUS = "source-only-uninstalled"
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
MAX_BYTES = 1024 * 1024


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def canonical(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True, allow_nan=False) + "\n").encode("ascii")


def strict_json(raw: bytes) -> dict:
    require(type(raw) is bytes and 0 < len(raw) <= MAX_BYTES, "json-size")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate-json-member")
            result[key] = value
        return result
    try:
        value = json.loads(raw, object_pairs_hook=unique,
                           parse_constant=lambda _: (_ for _ in ()).throw(Refused("nonfinite-number")))
    except (UnicodeError, ValueError, RecursionError) as error:
        raise Refused("malformed-json") from error
    require(type(value) is dict, "json-object")
    return value


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def git_oid(kind: str, raw: bytes) -> str:
    require(kind in ("blob", "tree", "commit"), "git-kind")
    return hashlib.sha1(f"{kind} {len(raw)}\0".encode() + raw).hexdigest()


def nonce_ref(run_id: int, attempt: int, candidate: str) -> str:
    require(type(run_id) is int and run_id > 0 and type(attempt) is int and attempt > 0
            and type(candidate) is str and HEX40.fullmatch(candidate) is not None,
            "run-identity")
    return f"refs/heads/gs2-09-7/{run_id}-{attempt}-{candidate}/seed-journal"


def raw_field(value: dict, name: str) -> bytes:
    encoded = value.get(name)
    require(type(encoded) is str and 0 < len(encoded) <= 2 * MAX_BYTES, name)
    try:
        raw = base64.b64decode(encoded, validate=True)
    except (ValueError, base64.binascii.Error) as error:
        raise Refused(name) from error
    require(0 < len(raw) <= MAX_BYTES, name)
    return raw


def inspect_proposal(value: dict, operation: str = "genesis") -> dict:
    """Validate Coordination S1's exact, precomputed Git objects."""
    require(operation in ("genesis", "advance"), "cas-operation")
    expected = {"runId", "runAttempt", "candidateSha", "workflowSha", "refName",
                "runNonce", "journalGeneration", "stateGeneration", "expectedParent",
                "stateSha256", "stateBytesBase64", "blobOid", "treeBytesBase64",
                "treeOid", "commitBytesBase64", "commitOid", "s2DeclarationSha256",
                "seedPlanSha256"}
    require(type(value) is dict and set(value) == expected, "proposal-shape")
    ref = nonce_ref(value["runId"], value["runAttempt"], value["candidateSha"])
    nonce = f'{value["runId"]}-{value["runAttempt"]}-{value["candidateSha"]}'
    require(value["refName"] == ref and value["runNonce"] == nonce, "proposal-nonce-ref")
    require(type(value["workflowSha"]) is str and HEX40.fullmatch(value["workflowSha"]) is not None
            and type(value["s2DeclarationSha256"]) is str
            and HEX64.fullmatch(value["s2DeclarationSha256"]) is not None
            and type(value["seedPlanSha256"]) is str
            and HEX64.fullmatch(value["seedPlanSha256"]) is not None,
            "proposal-provenance")
    generation = value["journalGeneration"]
    state_generation = value["stateGeneration"]
    old = value["expectedParent"]
    require(type(generation) is int and type(state_generation) is int
            and ((operation == "genesis" and generation == 0 and state_generation == 0
                  and old is None)
                 or (operation == "advance" and generation > 0 and state_generation > 0
                     and type(old) is str and HEX40.fullmatch(old) is not None)),
            "proposal-generation-parent")
    state = raw_field(value, "stateBytesBase64")
    tree = raw_field(value, "treeBytesBase64")
    commit = raw_field(value, "commitBytesBase64")
    require(sha256(state) == value["stateSha256"] and git_oid("blob", state) == value["blobOid"],
            "proposal-state-oid")
    require(tree == b"100644 state.json\0" + bytes.fromhex(value["blobOid"])
            and git_oid("tree", tree) == value["treeOid"], "proposal-tree-oid")
    expected_commit = (
        f'tree {value["treeOid"]}\n'
        + ("" if old is None else f"parent {old}\n")
        + f'author FS.GG Q4 Seed Journal <q4-seed-journal@fs.gg> {generation} +0000\n'
        + f'committer FS.GG Q4 Seed Journal <q4-seed-journal@fs.gg> {generation} +0000\n\n'
        + f'fsgg Q4 seed journal generation {generation}\n'
        f'state-sha256 {value["stateSha256"]}\n'
    ).encode()
    require(commit == expected_commit and git_oid("commit", commit) == value["commitOid"],
            "proposal-commit-oid")
    state_json = strict_json(state)
    binding = state_json.get("binding")
    require(state_json.get("schema") == "fsgg.gs2-09-7.sandbox-seed-execution/1"
            and state_json.get("stateGeneration") == state_generation
            and type(binding) is dict
            and binding.get("runNonce") == nonce and binding.get("workflowSha") == value["workflowSha"]
            and binding.get("protectedHostReceiptSha256") == value["s2DeclarationSha256"]
            and binding.get("seedPlanSha256") == value["seedPlanSha256"],
            "proposal-state-binding")
    effects = state_json.get("effects")
    require(type(effects) is list and len(effects) == 2
            and [effect.get("kind") for effect in effects if type(effect) is dict]
            == ["create-nonce-issue", "add-project-membership"], "proposal-effects")
    if operation == "genesis":
        require(state_json.get("mode") == "forward" and state_json.get("activeIndex") == 0
                and all(effect.get("stage") == "planned"
                    and effect.get("originalEffectId") is None
                    and effect.get("ownership") is None for effect in effects),
                "proposal-genesis-effects")
    return {"ref": ref, "nonce": nonce, "state": state, "tree": tree, "commit": commit,
            "value": value}


def verify_declaration(raw: bytes, plan_raw: bytes, value: dict) -> None:
    declaration = strict_json(raw)
    require(raw == canonical(declaration) and sha256(raw) == value["s2DeclarationSha256"],
            "s2-declaration-bytes")
    unsigned = {name: item for name, item in declaration.items() if name != "fingerprint"}
    require(declaration.get("fingerprint") == sha256(canonical(unsigned)),
            "s2-declaration-fingerprint")
    source = declaration.get("source")
    journal = declaration.get("journal")
    artifacts = declaration.get("artifacts")
    require(type(plan_raw) is bytes and 0 < len(plan_raw) <= 64 * MAX_BYTES
            and sha256(plan_raw) == value["seedPlanSha256"]
            and type(artifacts) is dict and type(artifacts.get("seedPlan")) is dict
            and artifacts["seedPlan"].get("sha256") == value["seedPlanSha256"],
            "seed-plan-custody")
    require(declaration.get("schema") == "fsgg.github-substrate-v2.sandbox-seed-execution-binding/2"
            and declaration.get("status") == "bound-no-write-authority"
            and declaration.get("activation") is False
            and type(source) is dict and type(journal) is dict
            and source.get("workflowSha") == value["workflowSha"]
            and source.get("candidateSha") == value["candidateSha"]
            and source.get("runId") == value["runId"]
            and source.get("runAttempt") == value["runAttempt"]
            and source.get("runNonce") == value["runNonce"]
            and source.get("seedJournalRef") == value["refName"]
            and type(journal.get("profile")) is dict
            and journal["profile"].get("ref") == value["refName"]
            and journal["profile"].get("object", {}).get("path") == "state.json",
            "s2-declaration-identity")


class GitPort:
    """Use a clean Git transport; credentials are supplied only by a host askpass environment."""

    def __init__(self, remote: str, env: dict[str, str] | None = None):
        require(remote == REMOTE or remote.startswith("/"), "remote-target")
        self.remote = remote
        self.env = env or {}

    def run(self, directory: Path, *arguments: str, input_bytes: bytes | None = None,
            allow_failure: bool = False) -> bytes:
        try:
            result = subprocess.run(["git", "-c", "credential.helper=", "-C", str(directory),
                                     *arguments], input=input_bytes,
                                    stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                    env={"PATH": os.environ.get("PATH", "/usr/bin:/bin"),
                                         "HOME": str(directory), "GIT_CONFIG_NOSYSTEM": "1",
                                         "GIT_CONFIG_GLOBAL": "/dev/null", "GIT_TERMINAL_PROMPT": "0",
                                         **self.env},
                                    timeout=30, check=False)
        except (OSError, subprocess.SubprocessError) as error:
            raise Refused("git-transport-unavailable") from error
        require(allow_failure or result.returncode == 0, "git-transport-refused")
        require(len(result.stdout) <= MAX_BYTES, "git-output-size")
        return result.stdout

    def new_repo(self, directory: Path) -> None:
        self.run(directory, "init", "--bare", "-q")

    def install_objects(self, directory: Path, item: dict) -> None:
        for kind, name in (("blob", "state"), ("tree", "tree"), ("commit", "commit")):
            actual = self.run(directory, "hash-object", "-w", "-t", kind, "--stdin",
                              input_bytes=item[name]).decode().strip()
            require(actual == item["value"][{"blob": "blobOid", "tree": "treeOid",
                                              "commit": "commitOid"}[kind]], "git-object-oid-drift")

    def push_genesis(self, directory: Path, item: dict) -> None:
        ref = item["ref"]
        value = item["value"]
        # Empty expected value is Git's exact absence lease. Never retry as an
        # unleased push, including after a lost response.
        self.run(directory, "push", f"--force-with-lease={ref}:", self.remote,
                 f'{value["commitOid"]}:{ref}', allow_failure=True)

    def push_advance(self, directory: Path, item: dict) -> None:
        ref = item["ref"]
        value = item["value"]
        self.run(directory, "push", f'--force-with-lease={ref}:{value["expectedParent"]}',
                 self.remote, f'{value["commitOid"]}:{ref}', allow_failure=True)

    def remove_exact(self, ref: str, old_oid: str) -> None:
        require(type(old_oid) is str and HEX40.fullmatch(old_oid) is not None, "remove-old-oid")
        with tempfile.TemporaryDirectory(prefix="gs2-seed-remove-") as temporary:
            directory = Path(temporary)
            self.new_repo(directory)
            self.run(directory, "push", f"--force-with-lease={ref}:{old_oid}",
                     self.remote, f":{ref}", allow_failure=True)

    def ref_oid(self, directory: Path, ref: str) -> str | None:
        output = self.run(directory, "ls-remote", "--refs", self.remote, ref).decode()
        rows = output.splitlines()
        require(len(rows) <= 1, "native-ref-ambiguous")
        if not rows:
            return None
        fields = rows[0].split("\t")
        require(len(fields) == 2 and fields[1] == ref and HEX40.fullmatch(fields[0]) is not None,
                "native-ref-shape")
        return fields[0]

    def fresh(self, ref: str) -> dict:
        require(re.fullmatch(r"refs/heads/gs2-09-7/[1-9][0-9]*-[1-9][0-9]*-[0-9a-f]{40}/seed-journal", ref) is not None,
                "nonce-ref")
        with tempfile.TemporaryDirectory(prefix="gs2-seed-read-") as temporary:
            directory = Path(temporary)
            self.new_repo(directory)
            first = self.ref_oid(directory, ref)
            require(first is not None, "native-ref-missing")
            self.run(directory, "fetch", "--no-tags", self.remote, ref)
            commit = self.run(directory, "cat-file", "commit", first)
            tree_line = commit.split(b"\n", 1)[0]
            require(re.fullmatch(rb"tree [0-9a-f]{40}", tree_line) is not None,
                    "native-commit-tree")
            tree_oid = tree_line[5:].decode()
            tree = self.run(directory, "cat-file", "tree", tree_oid)
            require(tree.startswith(b"100644 state.json\0") and len(tree) == len(b"100644 state.json\0") + 20,
                    "native-tree")
            blob_oid = tree[-20:].hex()
            state = self.run(directory, "cat-file", "blob", blob_oid)
            require(0 < len(state) <= MAX_BYTES, "native-state-size")
            last = self.ref_oid(directory, ref)
            require(first == last, "native-ref-moved")
            return {"refOid": first, "commit": commit, "tree": tree, "state": state,
                    "treeOid": tree_oid, "blobOid": blob_oid}


@contextmanager
def authenticated_port(token: str):
    """Supply the token to Git through a private askpass process, never an arg or URL."""
    require(type(token) is str and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token), "token")
    with tempfile.TemporaryDirectory(prefix="gs2-seed-askpass-") as temporary:
        script = Path(temporary) / "askpass.sh"
        script.write_text("#!/bin/sh\ncase \"$1\" in *Username*) printf '%s\\n' x-access-token;; "
                          "*) printf '%s\\n' \"$FSGG_GIT_PASSWORD\";; esac\n")
        script.chmod(stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
        yield GitPort(REMOTE, {"GIT_ASKPASS": str(script), "FSGG_GIT_PASSWORD": token})


def readback(item: dict, port: GitPort) -> dict:
    value = item["value"]
    observed = port.fresh(item["ref"])
    require(observed["refOid"] == value["commitOid"]
            and observed["commit"] == item["commit"]
            and observed["tree"] == item["tree"]
            and observed["state"] == item["state"]
            and observed["treeOid"] == value["treeOid"]
            and observed["blobOid"] == value["blobOid"], "native-cas-mismatch")
    return {"schema": SCHEMA, "status": "readback-complete", "outcome": "applied-or-already-applied",
            "complete": True, "repositoryId": REPOSITORY["id"], "repository": REPOSITORY,
            "refName": item["ref"], "runId": value["runId"],
            "runAttempt": value["runAttempt"], "runNonce": item["nonce"],
            "candidateSha": value["candidateSha"], "workflowSha": value["workflowSha"],
            "s2DeclarationSha256": value["s2DeclarationSha256"],
            "seedPlanSha256": value["seedPlanSha256"],
            "oldOid": value["expectedParent"], "newOid": value["commitOid"],
            "commitOid": value["commitOid"],
            "commitParentOid": value["expectedParent"], "treeOid": value["treeOid"],
            "blobOid": value["blobOid"], "payloadSha256": value["stateSha256"],
            "journalGeneration": value["journalGeneration"],
            "stateGeneration": value["stateGeneration"],
            "observedRefOid": observed["refOid"],
            "observedCommitParentOid": value["expectedParent"],
            "observedTreeOid": observed["treeOid"], "observedBlobOid": observed["blobOid"],
            "observedPayloadSha256": sha256(observed["state"]),
            "readbackSource": "fresh-git-fetch-cat-file-terminal-ref-reread",
            "observedAt": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")}


def pending(item: dict, reason: str) -> dict:
    value = item["value"]
    return {"schema": SCHEMA, "status": "pending", "outcome": "unproven",
            "complete": False, "reason": reason, "repositoryId": REPOSITORY["id"],
            "refName": item["ref"], "runId": value["runId"],
            "runAttempt": value["runAttempt"], "runNonce": item["nonce"],
            "candidateSha": value["candidateSha"], "workflowSha": value["workflowSha"],
            "s2DeclarationSha256": value["s2DeclarationSha256"],
            "seedPlanSha256": value["seedPlanSha256"],
            "newOid": value["commitOid"], "observedAt":
            dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")}


def apply(value: dict, declaration_bytes: bytes, seed_plan_bytes: bytes, port: GitPort,
          *, protected_grant_verified: bool = False) -> dict:
    require(INSTALLATION_STATUS == "installed-protected-host"
            and protected_grant_verified, "cas-bootstrap-authority-uninstalled")
    item = inspect_proposal(value)
    verify_declaration(declaration_bytes, seed_plan_bytes, value)
    with tempfile.TemporaryDirectory(prefix="gs2-seed-write-") as temporary:
        directory = Path(temporary)
        port.new_repo(directory)
        port.install_objects(directory, item)
        # Exact absence is checked atomically by the server-side lease.
        try:
            port.push_genesis(directory, item)
        except (Refused, OSError):
            # A timeout or lost response may follow an accepted lease. The
            # independent native object read below is the only settlement.
            pass
    # A rejected lease, timeout or lost response can qualify only through a
    # new independent read that returns the exact precomputed objects.
    try:
        return readback(item, port)
    except (Refused, OSError) as error:
        # A failed, stale or ambiguous read cannot certify an applied write.
        # Retain this non-authorizing envelope for exact later reconciliation.
        reason = "object-mismatch" if str(error) == "native-cas-mismatch" else "readback-unavailable"
        return pending(item, reason)


def advance(value: dict, declaration_bytes: bytes, seed_plan_bytes: bytes, port: GitPort,
            *, protected_capability_verified: bool = False) -> dict:
    require(INSTALLATION_STATUS == "installed-protected-host"
            and protected_capability_verified, "cas-advance-authority-uninstalled")
    item = inspect_proposal(value, "advance")
    verify_declaration(declaration_bytes, seed_plan_bytes, value)
    previous = port.fresh(item["ref"])
    require(previous["refOid"] == value["expectedParent"], "cas-old-oid-conflict")
    old_state = strict_json(previous["state"])
    old_generation = old_state.get("stateGeneration")
    require(type(old_generation) is int and value["stateGeneration"] == old_generation + 1,
            "cas-state-successor")
    old_commit = previous["commit"]
    marker = re.search(rb"\nfsgg Q4 seed journal generation ([0-9]+)\n", old_commit)
    require(marker is not None and value["journalGeneration"] == int(marker.group(1)) + 1,
            "cas-journal-successor")
    with tempfile.TemporaryDirectory(prefix="gs2-seed-advance-") as temporary:
        directory = Path(temporary)
        port.new_repo(directory)
        port.run(directory, "fetch", "--no-tags", port.remote, item["ref"])
        fetched = port.run(directory, "rev-parse", "FETCH_HEAD").decode().strip()
        require(fetched == value["expectedParent"], "cas-fetched-parent-drift")
        port.install_objects(directory, item)
        port.push_advance(directory, item)
    try:
        return readback(item, port)
    except (Refused, OSError) as error:
        reason = "object-mismatch" if str(error) == "native-cas-mismatch" else "readback-unavailable"
        return pending(item, reason)


def remove(ref: str, old_oid: str, port: GitPort,
           *, protected_cleanup_verified: bool = False) -> dict:
    require(INSTALLATION_STATUS == "installed-protected-host"
            and protected_cleanup_verified, "cas-remove-authority-uninstalled")
    require(re.fullmatch(r"refs/heads/gs2-09-7/[1-9][0-9]*-[1-9][0-9]*-[0-9a-f]{40}/seed-journal", ref)
            is not None and type(old_oid) is str and HEX40.fullmatch(old_oid) is not None,
            "cas-remove-identity")
    observed = port.fresh(ref)
    require(observed["refOid"] == old_oid, "cas-remove-old-oid")
    port.remove_exact(ref, old_oid)
    # Two independent remote ref reads are required; a lost response or a
    # remaining ref is pending and cannot be called cleanup.
    try:
        with tempfile.TemporaryDirectory(prefix="gs2-seed-remove-read-") as temporary:
            directory = Path(temporary)
            port.new_repo(directory)
            first = port.ref_oid(directory, ref)
            second = port.ref_oid(directory, ref)
    except (Refused, OSError):
        first = second = "unknown"
    now = dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z")
    return {"schema": SCHEMA, "status": "removed" if first is None and second is None else "pending",
            "outcome": "removed" if first is None and second is None else "unproven",
            "complete": first is None and second is None,
            "repositoryId": REPOSITORY["id"], "refName": ref, "oldOid": old_oid,
            "observedRefOid": second, "readbackSource": "two-fresh-git-ls-remote",
            "observedAt": now}

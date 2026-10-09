#!/usr/bin/env python3
"""Frozen Authority import proposal only. No ref update, remote call or apply mode."""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
from datetime import datetime
from release_successor_journal import canonical, SCHEMA, valid_transition

REPOSITORY = "FS-GG/FS.GG.Coordination.Authority"
EPOCH = "refs/heads/fsgg/v2/journal/cutover/d5"
PREFIX = "refs/heads/fsgg/v2/journal/"


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def load(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate JSON key")
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=pairs)


def ordinary(raw, ref, digest):
    value = load(raw)
    require(raw == canonical(value), "ordinary document is not canonical")
    require(set(value) == {"schema", "journalRef", "entries", "effects"}, "ordinary envelope fields")
    require(value["schema"] == "fsgg.coordination.ordinary-settlement-authority-document/1" and value["journalRef"] == ref,
            "ordinary ref/schema binding")
    require(re.fullmatch(r"[0-9a-f]{64}", digest) and digest[:2] == ref.rsplit("/", 1)[1], "ordinary digest/shard binding")
    require(isinstance(value["entries"], dict) and isinstance(value["effects"], dict), "ordinary map shape")
    unresolved = []
    for key, entry in value["entries"].items():
        fields = {"attemptId", "generation", "operationId", "planDigest", "stage"}
        require(isinstance(entry, dict) and set(entry) in (fields, fields | {"receiptDigest"}), "ordinary entry fields")
        require(entry["operationId"] == key and isinstance(key, str) and key.strip() == key and key,
                "ordinary operation binding")
        require(isinstance(entry["attemptId"], str) and entry["attemptId"].strip() == entry["attemptId"] and entry["attemptId"], "ordinary attempt")
        require(type(entry["generation"]) is int and entry["generation"] >= 1, "ordinary generation")
        for field in ("planDigest", "receiptDigest"):
            require(field not in entry or (isinstance(entry[field], str) and re.fullmatch(r"[0-9a-fA-F]{64}", entry[field])), "ordinary digest")
        require(entry["stage"] in ("intent-persisted", "effect-pending", "complete"), "ordinary stage")
        receipt = value["effects"].get(key)
        if entry["stage"] != "complete" or not receipt or receipt != entry.get("receiptDigest"):
            unresolved.append({"ref": ref, "operationId": key, "stage": entry["stage"], "reason": "native-effect-reconciliation-required"})
    for key, receipt in value["effects"].items():
        require(isinstance(key, str) and key and key.strip() == key and isinstance(receipt, str) and re.fullmatch(r"[0-9a-fA-F]{64}", receipt), "ordinary effect receipt")
        if key not in value["entries"]:
            unresolved.append({"ref": ref, "operationId": key, "reason": "effect-without-entry-reconciliation-required"})
    return unresolved


def propose(mirror: Path, snapshot: dict, output: Path):
    mirror, output = mirror.resolve(), output.resolve()
    require(output != mirror and mirror not in output.parents, "proposal output must not modify the retained mirror")
    clean_env = {key: value for key, value in os.environ.items() if not key.startswith("GIT_")}
    require(snapshot.get("repository") == REPOSITORY and snapshot.get("repositoryId") == 1351660651, "repository identity")
    refs = snapshot.get("refs")
    require(isinstance(refs, dict) and "refs/heads/main" in refs and EPOCH in refs, "frozen main/epoch refs missing")
    require(all(isinstance(ref, str) and ref.startswith("refs/heads/") and isinstance(oid, str) and re.fullmatch(r"[0-9a-f]{40}", oid) for ref, oid in refs.items()), "snapshot ref/head shape")
    proposal_time = datetime.fromisoformat(snapshot["proposalTimestamp"].replace("Z", "+00:00"))
    require(proposal_time.tzinfo is not None, "proposal timestamp must have an explicit timezone")
    timestamp = int(proposal_time.timestamp())
    def source(*args):
        return subprocess.run(["git", "--git-dir", str(mirror), *args], env=clean_env, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30).stdout
    retained = {}
    for line in source("for-each-ref", "--format=%(refname) %(objectname)", "refs/cleanup-backup/heads/").decode().splitlines():
        ref, oid = line.split()
        retained["refs/heads/" + ref.removeprefix("refs/cleanup-backup/heads/")] = oid
    selected = {ref: oid for ref, oid in retained.items() if ref == "refs/heads/main" or ref.startswith(PREFIX)}
    require(all(ref == "refs/heads/main" or ref.startswith(PREFIX) for ref in refs), "unsupported frozen authority ref")
    require(selected == {ref: oid for ref, oid in refs.items() if ref == "refs/heads/main" or ref.startswith(PREFIX)}, "frozen authority refs incomplete or changed")
    def raw(oid, path):
        return source("show", f"{oid}:{path}")
    def paths(oid):
        return source("ls-tree", "-r", "--name-only", oid).decode().splitlines()
    epoch_head, epoch_event = raw(refs[EPOCH], "head.json"), raw(refs[EPOCH], "event.json")
    head, event = load(epoch_head), load(epoch_event)
    aggregate = hashlib.sha256(b"30:fleet-cutover:fs-gg-production").hexdigest()
    require(head.get("aggregateId") == "fleet-cutover:fs-gg-production" and head.get("aggregateDigest") == aggregate
            and head.get("shard") == "d5" and head.get("journalKind") == "cutover"
            and type(head.get("generation")) is int and head["generation"] >= 1
            and head.get("eventDigest") == hashlib.sha256(epoch_event).hexdigest()
            and event == {"fleetId": "fs-gg-production", "phase": "OpenV2", "schema": "fsgg.github-substrate.epoch-event/1"}, "epoch logical binding")
    overlays = [("epoch", refs[EPOCH], {"state/epoch/epoch-head.txt": (refs[EPOCH] + "\n").encode(),
                 "state/epoch/head.json": epoch_head, "state/epoch/event.json": epoch_event})]
    documents, unresolved, releases = {}, [], []
    for ref, oid in sorted(selected.items()):
        if ref.startswith(PREFIX + "operation/"):
            source_paths = paths(oid)
            extra = [path for path in source_paths if not path.startswith("ordinary-v2/")]
            if extra:
                unresolved.append({"ref": ref, "paths": extra, "reason": "nonordinary-journal-reconciliation-required"})
            for path in source_paths:
                if path.startswith("ordinary-v2/"):
                    match = re.fullmatch(r"ordinary-v2/([0-9a-f]{64})\.json", path)
                    require(match is not None, "unsupported ordinary path")
                    payload = raw(oid, path)
                    unresolved.extend(ordinary(payload, ref, match[1]))
                    target = "state/" + path
                    require(target not in documents, "duplicate ordinary destination")
                    documents[target] = payload
        elif ref.startswith(PREFIX + "release/"):
            suffix = ref.removeprefix(PREFIX + "release/")
            require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", suffix), "release identifier")
            chain, current = [], oid
            while current:
                require(len(chain) < 128, "release lineage bound")
                require(paths(current) == ["release-state.json"], "release logical tree shape")
                payload = raw(current, "release-state.json")
                value = load(payload)
                require(payload == canonical(value) and value.get("schema") == SCHEMA, "release canonical state")
                chain.append(value)
                parents = source("rev-list", "--parents", "-n", "1", current).decode().split()[1:]
                require(len(parents) <= 1, "release logical parent shape")
                current = parents[0] if parents else None
            chain.reverse()
            root = chain[0]
            require(root.get("generation") == 1 and root.get("effects") == {} and all(isinstance(root.get(key), str) and root[key] for key in ("contentId", "sourceSha", "version", "candidateArchiveSha256", "operator")), "release root intent")
            require(all(valid_transition(a, b) for a, b in zip(chain, chain[1:])), "release transition lineage")
            latest = chain[-1]
            for effect, status in latest["effects"].items():
                if status != "verified":
                    unresolved.append({"ref": ref, "effect": effect, "stage": status, "reason": "native-release-effect-reconciliation-required"})
            directory = "state/releases/" + suffix
            overlays.append(("release", oid, {directory + "/release-head.txt": (oid + "\n").encode(), directory + "/release-state.json": raw(oid, "release-state.json")}))
            releases.append(ref)
    overlays.append(("ordinary-documents", None, documents))
    # Other retained journals stay reachable without interpreting or rewriting their state.
    for ref, oid in sorted(selected.items()):
        if ref.startswith(PREFIX) and ref != EPOCH and ref not in releases:
            overlays.append(("retain:" + ref, oid, {}))
    with tempfile.TemporaryDirectory() as scratch:
        repo = Path(scratch) / "proposal.git"
        subprocess.run(["git", "init", "--bare", "--quiet", str(repo)], check=True)
        (repo / "objects/info/alternates").write_text(str((mirror / "objects").resolve()) + "\n")
        env = {**clean_env, "GIT_AUTHOR_NAME": "FS.GG Authority dry-run proposal", "GIT_AUTHOR_EMAIL": "authority-proposal@example.invalid",
               "GIT_COMMITTER_NAME": "FS.GG Authority dry-run proposal", "GIT_COMMITTER_EMAIL": "authority-proposal@example.invalid",
               "GIT_AUTHOR_DATE": f"{timestamp} +0000", "GIT_COMMITTER_DATE": f"{timestamp} +0000", "GIT_INDEX_FILE": str(Path(scratch) / "index")}
        def git(*args, data=None):
            return subprocess.run(["git", "--git-dir", str(repo), *args], input=data, env=env, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=30).stdout
        current, steps, inventory = refs["refs/heads/main"], [], []
        existing = set(paths(current))
        for label, retained_head, files in overlays:
            require(not existing.intersection(files), "destination already exists; reconcile instead of overwrite")
            git("read-tree", current)
            for path, payload in sorted(files.items()):
                blob = git("hash-object", "-w", "--stdin", data=payload).decode().strip()
                git("update-index", "--add", "--cacheinfo", "100644", blob, path)
                inventory.append({"path": path, "blob": blob, "sha256": hashlib.sha256(payload).hexdigest(), "bytes": len(payload)})
            tree = git("write-tree").decode().strip()
            parents = [current] + ([retained_head] if retained_head else [])
            args = [item for parent in parents for item in ("-p", parent)]
            commit = git("commit-tree", tree, *args, data=("Frozen Authority import: " + label + "\n").encode()).decode().strip()
            steps.append({"label": label, "expectedMain": current, "parents": parents, "tree": tree, "commit": commit})
            current = commit
            existing.update(files)
        objects = []
        for folder in sorted((repo / "objects").iterdir()):
            if not re.fullmatch(r"[0-9a-f]{2}", folder.name):
                continue
            for leaf in sorted(folder.iterdir()):
                oid = folder.name + leaf.name
                kind = git("cat-file", "-t", oid).decode().strip()
                objects.append({"oid": oid, "type": kind, "base64": base64.b64encode(git("cat-file", kind, oid)).decode()})
        # Actual object graph proof, not a manifest assertion.
        for oid in selected.values():
            git("merge-base", "--is-ancestor", oid, current)
        manifest = {"mode": "dry-run-only", "repository": REPOSITORY, "snapshot": snapshot,
                    "toolSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    "expectedMain": refs["refs/heads/main"], "desiredMain": current, "desiredTree": steps[-1]["tree"],
                    "steps": steps, "files": sorted(inventory, key=lambda item: item["path"]), "objects": objects,
                    "unresolved": unresolved, "reconciliation": "required" if unresolved else "no-unresolved-state-detected",
                    "adopted": False, "reconciliationComplete": False,
                    "adoption": "blocked-pending-writer-fence-protection-native-qualification"}
        output.mkdir(parents=True, exist_ok=True)
        destination = output / "import-manifest.json"
        payload = canonical(manifest)
        require(not destination.exists() or destination.read_bytes() == payload, "prior proposal differs; retain it and select a fresh output directory")
        destination.write_bytes(payload)
        return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mirror", required=True, type=Path)
    parser.add_argument("--snapshot", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        result = propose(args.mirror.resolve(), load(args.snapshot.read_bytes()), args.output)
        print("dry-run proposal retained; adoption blocked; reconciliation=" + result["reconciliation"])
        return 2 if result["unresolved"] else 0
    except (ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print("import refused: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Frozen Authority import: dry-run by default, exact source-owned native admission."""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
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
        # Inventory is a graph difference, independent of packed/loose caches or
        # orphan proposal objects already present in the frozen mirror.
        needed = {line.split(" ",1)[0] for line in git("rev-list","--objects",current,"--not",
                  *sorted(set(selected.values()))).decode().splitlines()}
        objects = []
        for oid in sorted(needed):
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



# Reviewed admission is limited to this frozen proposal and original evidence.
# The proposal generator remains immutable revision A; this later source B is
# bound at execution by protected main/native run identity, never its own SHA.
ADMITTED_IMPORT = {'admission': {'artifactId': 11635712059,
               'artifactSha256': 'c5ac575329660c024100a7d08adff340af380be1833f1fc98ef7486615487b6c',
               'repository': 'FS-GG/FS.GG.Coordination',
               'repositoryId': 1346720714,
               'runId': 37970716764,
               'sourceSha': '5b3c7de05a5a8f0964139d7711343b9130988347',
               'workflowPath': '.github/workflows/callable-cli-release-publish.yml'},
 'executionWorkflowPath': '.github/workflows/authority-state-import.yml',
 'frozenRefs': {'refs/heads/fsgg/v2/journal/cutover/d5': '26d1882af9293b264df17a1fa98515e108313fe5',
                'refs/heads/fsgg/v2/journal/operation/00': '446355e83256fb17387c88b322390ecbf5e7be5e',
                'refs/heads/fsgg/v2/journal/operation/02': 'd49b12139490f34a9a752c5dc7749cdfe479e4f6',
                'refs/heads/fsgg/v2/journal/operation/03': '3cbef2ca0b2535330f04773849dd66b5e0d64ee4',
                'refs/heads/fsgg/v2/journal/operation/04': '1b0605669aeb3da8832a1630775bfda791a9d91f',
                'refs/heads/fsgg/v2/journal/operation/05': 'b2fb170cc5ca44800c9638ff58a14eecbd8f1297',
                'refs/heads/fsgg/v2/journal/operation/06': '4103530f372e594914c46c501b56bd61c3281825',
                'refs/heads/fsgg/v2/journal/operation/07': '77e7d7df18020a46336e3da0a594cdf278b9add4',
                'refs/heads/fsgg/v2/journal/operation/08': '447ec6063ab796109612b8916ebcc6712eada51e',
                'refs/heads/fsgg/v2/journal/operation/09': '211d986ff7634c1274bc3ff8973b34a884ad84fa',
                'refs/heads/fsgg/v2/journal/operation/0a': '4c492d81df4c60d294b8e9db160b2f2b1ff577e5',
                'refs/heads/fsgg/v2/journal/operation/0b': 'e7c9bb7eb32210868bdd3755533923753f7d19ac',
                'refs/heads/fsgg/v2/journal/operation/0c': '22970a7c54152687ab75d8788466bd20c8523a9f',
                'refs/heads/fsgg/v2/journal/operation/0d': '03615a951310a91402d9b2b3ee0d8d33f9091f9e',
                'refs/heads/fsgg/v2/journal/operation/0f': '6fbeb220e4af177a03bd6afe05443d52d0cdc38b',
                'refs/heads/fsgg/v2/journal/operation/10': 'ad89a0f8e497cb93d61d89508bb775203bf01815',
                'refs/heads/fsgg/v2/journal/operation/11': '8e375a1049c8fa0038e530af26749eae88a01086',
                'refs/heads/fsgg/v2/journal/operation/12': '2db1965cfb9f1b53742139bffe1fb1230293fe78',
                'refs/heads/fsgg/v2/journal/operation/13': 'a259b80c81d762bf6e914235bef4f22bb2ba26ff',
                'refs/heads/fsgg/v2/journal/operation/15': '1766badafd89099b52dd39136ed9d67fab0fe932',
                'refs/heads/fsgg/v2/journal/operation/17': '03bb60b130ddf2a093e07f64d13ff21fdc02e7fd',
                'refs/heads/fsgg/v2/journal/operation/18': '9bdb1600fe0c4d538d2f8186afe45c241d67aba1',
                'refs/heads/fsgg/v2/journal/operation/1a': '71756c340a87871fc70bad5adeee6dd9c462869f',
                'refs/heads/fsgg/v2/journal/operation/1b': '96cfd6cb0e3d8e531b179413b5ecd325cf3663f8',
                'refs/heads/fsgg/v2/journal/operation/1c': 'c76ce5c50dc7bece4924a92a61848b38baa66d3b',
                'refs/heads/fsgg/v2/journal/operation/1d': 'ca18835369f58d70b12dc7248f8bb01c36c78ae7',
                'refs/heads/fsgg/v2/journal/operation/1e': 'db7b97d1eed43d38a61ecea1e1ec4b306dc4301c',
                'refs/heads/fsgg/v2/journal/operation/1f': 'f6efd61471fabb8ac6a4cd02caf37a11784f39c6',
                'refs/heads/fsgg/v2/journal/operation/21': '24c22b996e3a451fe3b7a3f15ee40b8bb006adfe',
                'refs/heads/fsgg/v2/journal/operation/22': '1193c880debc5a3d7b8e3292c6b81903c518477d',
                'refs/heads/fsgg/v2/journal/operation/23': '0684f0bb037ca4d09beb5de730ad5eae230b4da6',
                'refs/heads/fsgg/v2/journal/operation/24': 'ac506b1d77c20bb47df096809c9778b677c896b4',
                'refs/heads/fsgg/v2/journal/operation/25': 'a1781abe818ecea4beb52edcda24d45a6f6b99c6',
                'refs/heads/fsgg/v2/journal/operation/26': '1ec3ae522f34ed592e9407c645f7a881903d5e29',
                'refs/heads/fsgg/v2/journal/operation/28': '7c0d16a131c5b3e5f36ec480ece8935d2ea17bd2',
                'refs/heads/fsgg/v2/journal/operation/29': '11fb8812b5ef36cdd49305e6b62fdddcf330136a',
                'refs/heads/fsgg/v2/journal/operation/2a': '6ea88ce77fa87e1d35a932235a9d39f66a5c58a1',
                'refs/heads/fsgg/v2/journal/operation/2b': '2885f4e93c42de80eb8739e9bf60f797f19acb93',
                'refs/heads/fsgg/v2/journal/operation/2c': 'b9aebe4da972725ecca328cafb053f5a2caa72b7',
                'refs/heads/fsgg/v2/journal/operation/2d': '62a274c24aa0cae8f801119f62884e3d71faa0a3',
                'refs/heads/fsgg/v2/journal/operation/2e': 'be4e856a994220158672df3e8f7eab80b2b23d9a',
                'refs/heads/fsgg/v2/journal/operation/2f': '730e5a847560760c5d0e49ffa03345a2716cbd09',
                'refs/heads/fsgg/v2/journal/operation/30': 'fd5317ee0d4bcd6ebf6512648e03baaea892adb5',
                'refs/heads/fsgg/v2/journal/operation/31': '250ff31aae411b036ca441fcb847a9f8f886c859',
                'refs/heads/fsgg/v2/journal/operation/32': 'd8b21ef38cdd280233e9656193c448badf00bee9',
                'refs/heads/fsgg/v2/journal/operation/33': '22ca936990ef2409b7747753078908d6f83e6bbf',
                'refs/heads/fsgg/v2/journal/operation/34': 'e2a0747f9c009f1c7b23e8de39d6f6eb9d3071d0',
                'refs/heads/fsgg/v2/journal/operation/35': '74e5aad30d7e9baf16292151d900e6c7b949d7cf',
                'refs/heads/fsgg/v2/journal/operation/36': 'e2ef6540ed8ae205efa57bb15f8d19a68f14431f',
                'refs/heads/fsgg/v2/journal/operation/37': 'b1add0407b6bf48dd086f1be2f926c192bef554a',
                'refs/heads/fsgg/v2/journal/operation/38': '10ee0897a3dc6121ecb5f3117cb573a68ca07891',
                'refs/heads/fsgg/v2/journal/operation/39': 'b2a5d55ddf0c95fdc41ca50ab62006c5e8c1ea60',
                'refs/heads/fsgg/v2/journal/operation/3a': '5b8f2b8c00877a5ce5fe2143869b95bccb420e96',
                'refs/heads/fsgg/v2/journal/operation/3b': '10513ba0f1cbda651bee341abb670b2736e61dfa',
                'refs/heads/fsgg/v2/journal/operation/3c': 'dad18042c6317b7b78aff6513f566e3b233c14dc',
                'refs/heads/fsgg/v2/journal/operation/3d': '819d9ed5a1610524d7df20d32de823bac9937ca9',
                'refs/heads/fsgg/v2/journal/operation/3e': 'f915029082130e139ba5d2dc65a3af65f6bdee7d',
                'refs/heads/fsgg/v2/journal/operation/3f': '487175d25342e4c1e0f8a94cc31d472572878932',
                'refs/heads/fsgg/v2/journal/operation/40': '55dccbacc4ecc3b202286147a6738c7dd9133242',
                'refs/heads/fsgg/v2/journal/operation/41': 'f649c8f254737c2a636025792e5526460dd1492e',
                'refs/heads/fsgg/v2/journal/operation/44': '3247ff90f396f96ec50aafb14ce5d5d3dbf68314',
                'refs/heads/fsgg/v2/journal/operation/45': 'b8a4c107e49e5e8d15c91e5294000bd040fe187f',
                'refs/heads/fsgg/v2/journal/operation/46': 'c674e8e500ed72fea5384ffbde0885c250b602ae',
                'refs/heads/fsgg/v2/journal/operation/47': 'd4b14ecabaaa6b96b5f9df344535531be3921256',
                'refs/heads/fsgg/v2/journal/operation/48': '65c3bfa31dfb73f7e98dc1a8002cbe61e57be2a4',
                'refs/heads/fsgg/v2/journal/operation/49': '551d5a5bf852c2659386fcdfbddd51a6986b96c6',
                'refs/heads/fsgg/v2/journal/operation/4a': 'e366cd15974968d6247f8ab7d917e97170a85dd8',
                'refs/heads/fsgg/v2/journal/operation/4b': 'c95ffb642c88efbecd31bfad329374661f8487a8',
                'refs/heads/fsgg/v2/journal/operation/4c': 'f8e241004c4029fd4fb4faf0cb1fbdd9dbaf5b56',
                'refs/heads/fsgg/v2/journal/operation/4d': 'd7396beeb5d10db9e6a794bee8b3c77a33acb6ad',
                'refs/heads/fsgg/v2/journal/operation/4e': 'c951f7c06221ffebec131d76a67d1f22d58cba80',
                'refs/heads/fsgg/v2/journal/operation/4f': '334fd3b2193688bffeb0eb65b84c09aab1b299cd',
                'refs/heads/fsgg/v2/journal/operation/50': 'cf4985e6715b8062d2b3fd31d0004fa0bdd9ce83',
                'refs/heads/fsgg/v2/journal/operation/51': 'd0990c97754e63c7fd2f0e982528427b3594b39c',
                'refs/heads/fsgg/v2/journal/operation/52': 'cc6d1ffe4d4e9bd2800916589bacefa5c02d13d5',
                'refs/heads/fsgg/v2/journal/operation/53': '38144de1e4442a60c6394f7a1a37eeb7d4fa7444',
                'refs/heads/fsgg/v2/journal/operation/54': '3c3404d933255770480a7d20f3d9c17eee007f19',
                'refs/heads/fsgg/v2/journal/operation/55': '458324f95fde7fcecbdd50ee87f2936fe1bb348a',
                'refs/heads/fsgg/v2/journal/operation/56': 'ce678832f2732a4be9b5daffed4e2b07bd5a1080',
                'refs/heads/fsgg/v2/journal/operation/57': 'e27efa872fce2fc073055424ce66b445664ff51c',
                'refs/heads/fsgg/v2/journal/operation/58': '381085d844e78669ffd5ba82c568a37c22948d32',
                'refs/heads/fsgg/v2/journal/operation/5a': 'e08002ccd803f90524623668caca17ed419e383e',
                'refs/heads/fsgg/v2/journal/operation/5b': 'dc9a0fad7ab7741f6e64a4e93b77faf4892f014c',
                'refs/heads/fsgg/v2/journal/operation/5c': '2535db9258a666b80a73924e083d1a5cc73b0270',
                'refs/heads/fsgg/v2/journal/operation/5d': '744617f73781749fe2bc6038f0ef7b3fe5c5f4df',
                'refs/heads/fsgg/v2/journal/operation/5e': '923bb011e106cb0600e1a74c055c8349fd14e5cb',
                'refs/heads/fsgg/v2/journal/operation/5f': 'd070dbb7dbaff4c483870759d58651f444b19a54',
                'refs/heads/fsgg/v2/journal/operation/60': '2740251ba3c2463bcf07db865729c8d47bd2fd32',
                'refs/heads/fsgg/v2/journal/operation/61': '776921df86b0d2f7ffd9fed13b59e080e6529f02',
                'refs/heads/fsgg/v2/journal/operation/62': 'f473bf18293cfafa31f7e27c4be828a888f3d63c',
                'refs/heads/fsgg/v2/journal/operation/63': '97caae9806638f0c4344c05199d76225060bfcdc',
                'refs/heads/fsgg/v2/journal/operation/65': 'ab610e92f58f3322a2b9f725cd0f261a28c68508',
                'refs/heads/fsgg/v2/journal/operation/68': 'b3dd70a0efc04ffb54158b6b3f3787cafd6cb16a',
                'refs/heads/fsgg/v2/journal/operation/6a': '7961655931e6b650108826ef857a530eee060128',
                'refs/heads/fsgg/v2/journal/operation/6b': '3d2e6ed9e3e8ab63d9166a8be63b3481cd8a007d',
                'refs/heads/fsgg/v2/journal/operation/6c': 'f811adb24f5bd67ba61c0349ca27c13ed3a7820b',
                'refs/heads/fsgg/v2/journal/operation/6d': 'dd15eae0b47b27f536b79cb16f51e36234413fed',
                'refs/heads/fsgg/v2/journal/operation/6e': '3774c0f31575cb49fb80c257ccb015dcdf26b1d7',
                'refs/heads/fsgg/v2/journal/operation/6f': '9ac9d2fbaeab09df6c33c28094e2900a63f93f56',
                'refs/heads/fsgg/v2/journal/operation/70': '2defac63452403a54cddb0311e5caa55f5fc3d7e',
                'refs/heads/fsgg/v2/journal/operation/72': '9967e4baf1294d46ac11acde47cc0596c8f6ce5c',
                'refs/heads/fsgg/v2/journal/operation/73': '91275fa908de8ec9d7fb2c652f52d615073a3103',
                'refs/heads/fsgg/v2/journal/operation/74': '0c98bbd9b6a482dffcc1a5acddecc423c9d0fbdf',
                'refs/heads/fsgg/v2/journal/operation/75': 'bf2aa69899a5d9088902a3fab8aaecf416b8d691',
                'refs/heads/fsgg/v2/journal/operation/76': 'b84db404a39335bc98b96bf97470dff24ec82869',
                'refs/heads/fsgg/v2/journal/operation/77': 'd1f304c3ebf056fcb7020e8c0e7e810a57aa80ef',
                'refs/heads/fsgg/v2/journal/operation/78': '24c2e8295a2953d77e27adb39e84e1876b993517',
                'refs/heads/fsgg/v2/journal/operation/79': 'dc1273f459f553addec82631cce1405e780d05f0',
                'refs/heads/fsgg/v2/journal/operation/7a': 'a57c724c3f22babf515aa8ab724f184570d7de7c',
                'refs/heads/fsgg/v2/journal/operation/7b': 'b2de33ac26c96461388fd39e812660fa4c505f8d',
                'refs/heads/fsgg/v2/journal/operation/7d': 'c79ce96874a5431c5bbd862b490f496810946319',
                'refs/heads/fsgg/v2/journal/operation/7e': '7f5971217239e4b189803677f43768f9d875c14e',
                'refs/heads/fsgg/v2/journal/operation/7f': '3118835531fd632f9aa29bcc0f1d79d6e718a892',
                'refs/heads/fsgg/v2/journal/operation/80': 'd472968f4bf240348f4e4a4767b70b29a67a373c',
                'refs/heads/fsgg/v2/journal/operation/83': 'c0bf77ef1da87702008b3aae523cfbc20be0443a',
                'refs/heads/fsgg/v2/journal/operation/84': '91ea6751becff7a56e318c08f641c4ae789158b7',
                'refs/heads/fsgg/v2/journal/operation/86': '96a28eddc7aea8327b086fad721823aada7f6b62',
                'refs/heads/fsgg/v2/journal/operation/87': 'e193ecbce7401361fd063b15cb4d6e45214e15dc',
                'refs/heads/fsgg/v2/journal/operation/88': '31a794ce1a2eb00eb118dd244af7c3085fe7c7a1',
                'refs/heads/fsgg/v2/journal/operation/89': '0ba57fc963504ab41208eae4bc90ef469e37214d',
                'refs/heads/fsgg/v2/journal/operation/8a': '90506858b0876f04824912a543205cebff4ea6e4',
                'refs/heads/fsgg/v2/journal/operation/8c': 'cb05c5f6ef89cb45dbd0f510e47c23e1b60dcd55',
                'refs/heads/fsgg/v2/journal/operation/8d': 'c9eec229804b9e8fa04c450791aea35de8580dbb',
                'refs/heads/fsgg/v2/journal/operation/8e': '114c6340bbfff9394a957f43bf39836b47b2c90f',
                'refs/heads/fsgg/v2/journal/operation/8f': '02b195b649a4144808dc98e089fa33dc9d549727',
                'refs/heads/fsgg/v2/journal/operation/90': '23e4304d2ed6c396629699ad8281319c7ca3d944',
                'refs/heads/fsgg/v2/journal/operation/91': '37de66523db3c44538b9286b0a42da11d1af3146',
                'refs/heads/fsgg/v2/journal/operation/92': '62a3be3d419ad762340104f941746ecd1c2a68f5',
                'refs/heads/fsgg/v2/journal/operation/93': 'a514922e502fb24805fc0b9c9c2b57bcf580a9f2',
                'refs/heads/fsgg/v2/journal/operation/94': '973596cc2f09973e80f68402a40c2c4224b6c9f9',
                'refs/heads/fsgg/v2/journal/operation/95': 'd777b210a653cc32ef408c0fa2e3833349e0877c',
                'refs/heads/fsgg/v2/journal/operation/96': 'b665a92cd091983e114221ffd0d382f7061b8e24',
                'refs/heads/fsgg/v2/journal/operation/99': '7b79967b16a89372f02149a26da286382c4082dd',
                'refs/heads/fsgg/v2/journal/operation/9a': '16e5096cc559e94ff7c079d01baec641535c9f7a',
                'refs/heads/fsgg/v2/journal/operation/9b': '5c018c91762ba2b983d276213b91e8efe7931b1a',
                'refs/heads/fsgg/v2/journal/operation/9c': '861f76b0d31a1570ed3f8eb22e2a9ab60fad799d',
                'refs/heads/fsgg/v2/journal/operation/9d': '5b8e671e45c31476ee09533ed953e02ed5883fee',
                'refs/heads/fsgg/v2/journal/operation/9e': '8525bd1253acf6809c83f095efa7cae4543111df',
                'refs/heads/fsgg/v2/journal/operation/9f': 'a0e0512af3fbab528cdcdf7a25ed2a1760a1e676',
                'refs/heads/fsgg/v2/journal/operation/a0': '2e4b957c4e5f361055ef56b6cf9e51220589931f',
                'refs/heads/fsgg/v2/journal/operation/a1': '70c45c81fa5de4cade44e82d680d93a1fcb48cfb',
                'refs/heads/fsgg/v2/journal/operation/a2': 'e0c83ecaac260449bdcb99e55239926d883e373b',
                'refs/heads/fsgg/v2/journal/operation/a3': '7a24d7cd7e2f87ee73813183a9157662ab4e92d5',
                'refs/heads/fsgg/v2/journal/operation/a5': 'e5192f55bcff6635ecce05aed6529f9e262959f0',
                'refs/heads/fsgg/v2/journal/operation/a6': '8d3540df31fed124649df4a35e80e5cbbdfabed2',
                'refs/heads/fsgg/v2/journal/operation/a7': '7b36ab817b1349dc815d854ec0717f2bc2fe8dc2',
                'refs/heads/fsgg/v2/journal/operation/a8': '6867d1c657c04e175322e5c69ee8f631b449166e',
                'refs/heads/fsgg/v2/journal/operation/aa': 'ddd976d7226d45f5825aea4cf6d6c29d02067e6f',
                'refs/heads/fsgg/v2/journal/operation/ab': '7ee0960136a822c560fac3846f0f36f12d560017',
                'refs/heads/fsgg/v2/journal/operation/ac': '92b14951e48f676097a86de24df95e0450ca1c7f',
                'refs/heads/fsgg/v2/journal/operation/ad': 'd912ddf834caf84dd5ef64174819245c4c80cbce',
                'refs/heads/fsgg/v2/journal/operation/ae': '4b0ed59aa60d7a087091557965b14cce72590ad4',
                'refs/heads/fsgg/v2/journal/operation/af': 'ba110053f383339ca07361c174e6e1b5ffe5001f',
                'refs/heads/fsgg/v2/journal/operation/b0': '13b9359a0f4fa5c7cacac2e1b5e4848a03e6c3aa',
                'refs/heads/fsgg/v2/journal/operation/b1': '5f00134c67cbc52e9d180a1cef59515312b7be8d',
                'refs/heads/fsgg/v2/journal/operation/b2': '9cb82c4cdfb1e31f4ba64c619f7c25a1e71bc38b',
                'refs/heads/fsgg/v2/journal/operation/b3': '0edbc0005ed5ba68786026a379aa7949421cbe82',
                'refs/heads/fsgg/v2/journal/operation/b4': '4c5487790bb34615ba61d8ff931a2f73cc7b88b9',
                'refs/heads/fsgg/v2/journal/operation/b5': '1a8fda3385fbd8215494de321e9479fef0ca7cbd',
                'refs/heads/fsgg/v2/journal/operation/b6': 'af4224640a36d05ee2b9187b0d66d1bbbed45b4a',
                'refs/heads/fsgg/v2/journal/operation/b7': 'eded5ce888b3c7834f1e4573afe8b63c937e7e79',
                'refs/heads/fsgg/v2/journal/operation/b8': '861c5944549ca35673af57cca493a2c02e2fdcf3',
                'refs/heads/fsgg/v2/journal/operation/b9': 'fad179918adf39bff87835661baef158502c42eb',
                'refs/heads/fsgg/v2/journal/operation/ba': 'f9d7ab385f6b32a00131d7d0935140beb6b3f92b',
                'refs/heads/fsgg/v2/journal/operation/bb': 'df3b8a25657c0c84662e4aabea6fb18c0e57d64b',
                'refs/heads/fsgg/v2/journal/operation/bc': 'a9214fce05de83be0a01c20026e13c2f66897cb2',
                'refs/heads/fsgg/v2/journal/operation/bd': 'fa428eea1cfafd5ba0ac01406db8e173ac82b50f',
                'refs/heads/fsgg/v2/journal/operation/be': 'fa954236c36cd35e2d616855c4b97016b1631c57',
                'refs/heads/fsgg/v2/journal/operation/bf': '584c8bbe1e482e5e43ba2b0cd9ec671c8fb6ef5d',
                'refs/heads/fsgg/v2/journal/operation/c0': '7a41318ccba0cb51b294168120b72ac5e54e3288',
                'refs/heads/fsgg/v2/journal/operation/c1': '95842330e2285f2f8a9eaca83de5562cf6262520',
                'refs/heads/fsgg/v2/journal/operation/c2': 'a6b7af5e6ea726be19f72dcab09d68e6e89f021b',
                'refs/heads/fsgg/v2/journal/operation/c3': '68719838fc2a0506bc91ad538bb0d8dedec2344e',
                'refs/heads/fsgg/v2/journal/operation/c4': '800ae6118704a4aefa3aa0a3ea0c34c09141f184',
                'refs/heads/fsgg/v2/journal/operation/c6': '9e57ccd9fc568de77369173ad3133f853e3c7944',
                'refs/heads/fsgg/v2/journal/operation/c7': '735dd70e0d26aebb72bb6be849d859179cfb3756',
                'refs/heads/fsgg/v2/journal/operation/c8': '60aa974b9c1a2d90e5cec7340ee479a5c05c5e5a',
                'refs/heads/fsgg/v2/journal/operation/c9': 'feffd383182a716b1c45402f45d6b648f99aeb40',
                'refs/heads/fsgg/v2/journal/operation/ca': 'e8934776a12c52aa54d6545f04140474de3d17b5',
                'refs/heads/fsgg/v2/journal/operation/cb': '8c9cc84d90ebfc40639b7f68b01c7d5a8b57e117',
                'refs/heads/fsgg/v2/journal/operation/cc': '542174d7dbbbb4acf6865523ccbe0c10ca58a2ba',
                'refs/heads/fsgg/v2/journal/operation/cd': 'eebf83cb675e6a186ea99a2552121fccb39f5ca1',
                'refs/heads/fsgg/v2/journal/operation/ce': 'd8ad7dfca24cac7a22795ece2aa55abd090aea63',
                'refs/heads/fsgg/v2/journal/operation/d0': 'abbb905b2621cbd92fcfc3675aa0c4b26f78d8de',
                'refs/heads/fsgg/v2/journal/operation/d1': '2f8dfe5ffde03c1702c490d1c0459bb7800862eb',
                'refs/heads/fsgg/v2/journal/operation/d3': '896de64e1f28d2830c49118b47d31bd18ea6dea6',
                'refs/heads/fsgg/v2/journal/operation/d4': 'fc81f9f819bc711794ad67a934a8d181ff351fe9',
                'refs/heads/fsgg/v2/journal/operation/d5': 'cc133217e8d180b5614a71b35f21d45be4f965c3',
                'refs/heads/fsgg/v2/journal/operation/d6': '83f4c3f7ae31228d8bf67265871312ad8d37d8e3',
                'refs/heads/fsgg/v2/journal/operation/d7': '85ad3069d2a0481639d0c5298d88706f4e6510c7',
                'refs/heads/fsgg/v2/journal/operation/d8': '1900ddfa61f780c64111917ffca4cc644ef195af',
                'refs/heads/fsgg/v2/journal/operation/d9': '49f5bd7046d5b5305f8b432e77550f026ca196c9',
                'refs/heads/fsgg/v2/journal/operation/da': '5f7c82622d39ebe35b205ef742b64df2aa4f0d6d',
                'refs/heads/fsgg/v2/journal/operation/db': 'd9d0d79e80dc55f595bc9fd2afe56f5fa75dc970',
                'refs/heads/fsgg/v2/journal/operation/dc': '3c01a0ce3137f582296e4d648148aa8cade10820',
                'refs/heads/fsgg/v2/journal/operation/dd': 'ac13d332cb95dd8babfb9f74afdd4ab34e9d132e',
                'refs/heads/fsgg/v2/journal/operation/df': '70637a7cab77991219571ff8fdc2a9becf5eb63e',
                'refs/heads/fsgg/v2/journal/operation/e0': 'a2f965a9942e383726283f3beb418c5cfb58501a',
                'refs/heads/fsgg/v2/journal/operation/e1': 'cd6fc27607bc93696008cc4b000db17763e1eab3',
                'refs/heads/fsgg/v2/journal/operation/e3': '6329ec2c89024f2226ab2b95fde3fd30bd22ea7e',
                'refs/heads/fsgg/v2/journal/operation/e4': 'b115645d4336b616d0afb76031cd174c498e6f23',
                'refs/heads/fsgg/v2/journal/operation/e5': '2ab94b31de7ef42e20eb0d49d50cdc5c24bf3958',
                'refs/heads/fsgg/v2/journal/operation/e6': '0d65e8722dc423e954d4ae46f6c336392e80da26',
                'refs/heads/fsgg/v2/journal/operation/e7': '1bfde43ae5a5ef5031419111432442ded495f0e4',
                'refs/heads/fsgg/v2/journal/operation/e8': 'f1f97167d47c32230dca38da5fd804260f924cfe',
                'refs/heads/fsgg/v2/journal/operation/e9': 'f5679921a944379f3efe3ac829f92d010ae0ad0f',
                'refs/heads/fsgg/v2/journal/operation/eb': 'b2a75622bb0878130d3a878154ccaecdc0139ac3',
                'refs/heads/fsgg/v2/journal/operation/ed': '2771f39738c030e033e4c1b048357df93ca9e120',
                'refs/heads/fsgg/v2/journal/operation/ee': 'd2dde0bacbc32c374145190ade6c04329d5c23e6',
                'refs/heads/fsgg/v2/journal/operation/f0': '50a67b9da135d39f01187d21245f2a42e858d4d3',
                'refs/heads/fsgg/v2/journal/operation/f1': '3f5be9faa65f59c29cbd4f035200d3f12cda31b4',
                'refs/heads/fsgg/v2/journal/operation/f2': 'dbeb42b4699bb678683e25330b4a282ddf65ef9d',
                'refs/heads/fsgg/v2/journal/operation/f3': '563e240ee6ee71b7f7a929066fa2296739ae1d82',
                'refs/heads/fsgg/v2/journal/operation/f4': 'f4f28f1ed12cef48eb93eb665e301753903115d8',
                'refs/heads/fsgg/v2/journal/operation/f5': 'a1a80e953856c6390e9f045372aab8cbe27f6b00',
                'refs/heads/fsgg/v2/journal/operation/f6': 'f8c9f979786a5f9a8ec9d9326df6411859661fd2',
                'refs/heads/fsgg/v2/journal/operation/f7': '4948a1b1bb1ab6be56ee96a500df43aac65dff06',
                'refs/heads/fsgg/v2/journal/operation/f8': '8cf6b9255fc34ca546bdd8c155f2d099182a977c',
                'refs/heads/fsgg/v2/journal/operation/f9': 'f6a6afebae6466565df696d9b5a606027e99c67c',
                'refs/heads/fsgg/v2/journal/operation/fa': '2ce4eb7d34d7ee4d2278c34b089b395db18b8bb9',
                'refs/heads/fsgg/v2/journal/operation/fc': 'a414e09913d2dcccce81bc2960b40d16cc88af5f',
                'refs/heads/fsgg/v2/journal/operation/fd': 'b2070fa4d908eb3291d36a0d18587a294bf766bb',
                'refs/heads/fsgg/v2/journal/operation/fe': 'c61940076ffa69cf8d1f1765bbef38dcc077b33c',
                'refs/heads/fsgg/v2/journal/operation/ff': '48f94283ba93d571231c6158122d1b374bd98f09',
                'refs/heads/fsgg/v2/journal/qualification/33330220225-1': '989ef63adb95a03855a4f676e3831a2600e45f07',
                'refs/heads/fsgg/v2/journal/release/board-v2-product-coherent-097': 'fb72758c6e61155dd2e8860adbc8f1cc3088dbb0',
                'refs/heads/fsgg/v2/journal/release/board-v2-product-creator-014': '7ada85fd773a3f098a4c84f11847980151578820',
                'refs/heads/fsgg/v2/journal/release/board-v2-product-creator-015': '4495a822c11b15168c69ed6e138b5ce8a9b7a895',
                'refs/heads/fsgg/v2/journal/release/svg-d5-wizard-012': '251501fc3e2bbb2ef229f0f87c77668c9793d5f1',
                'refs/heads/fsgg/v2/journal/release/tsdd-knowledge-wizard-013': 'e6ef48a4dfa68ce66a6b215dea1880020157dcee',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-01': 'ade613fbf747e545ac53c39e44b0e17734fddd0f',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-02': '5bc15719053cf4483a2123ef85603ea507fbad3f',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-03': '9ef3c55a16a71f2958eb4c3946f958a9dde320f8',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-04': 'bc8047049db3803e971fcfed27f077278df6b582',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-05': '07a1098fa36750bda9e3ee060b57738fceb4c6b2',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-06': 'f25b93fa71c78e36c9a5d65f3eff1ef0caa4ac98',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-07': '91c1879c2de382ee1b94d9bd3f4347582a3c005e',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-08': '072235dd8bbfc8116f0d95f9b86abcae992200ee',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-09': '26b6a2393dd82901cdc2651a2b75a8a942eaed8c',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-10': '203ba60f7df146743d4a7373a52f55fc6c22858e',
                'refs/heads/fsgg/v2/journal/release/utel-host-rel-11': '57a3d1eac66d3e246d4e8de03988e0a35b7b3cb3',
                'refs/heads/fsgg/v2/journal/release/utel-rel-01': 'b6b821bd95d12a19251f87dc92cea8bf8a34de80',
                'refs/heads/fsgg/v2/journal/release/utel-rel-02': '44627b5d3a9eefc4c0edb809dcea854b023cf589',
                'refs/heads/fsgg/v2/journal/release/utel-rel-03': 'd713cda85019202cde2460239166af461c6aeba2',
                'refs/heads/fsgg/v2/journal/release/utel-rel-04': '2a1fede5ad8979df936eb604c257920302bea521',
                'refs/heads/fsgg/v2/journal/release/utel-rel-05': 'e2f332f61f1f29bcd9fdcb7c3c8fb2bf9b27d5e3',
                'refs/heads/fsgg/v2/journal/release/utel-rel-06': 'c02488d6a7d6e94db16c1430c9b4bdb0e74521a5',
                'refs/heads/fsgg/v2/journal/release/utel-rel-07': '2df17c781d151e1b09700b2cc0ee2b7b3fb0c021',
                'refs/heads/fsgg/v2/journal/release/utel-rel-08': 'e859c9c4379e22aeabc909bb8d036fede109a5af',
                'refs/heads/fsgg/v2/journal/release/utel-rel-09': '0a45b80936e4464b891971237fbea6ffdcba37ae',
                'refs/heads/fsgg/v2/journal/release/utel-rel-10': '78eff3ca803517859fa2968fe071d4ceb1fbe1b2',
                'refs/heads/fsgg/v2/journal/release/utel-rel-11': '8d9f5466937a3de73c54b015cd7327b4a8cc6ada',
                'refs/heads/fsgg/v2/journal/release/utel-rel-13': '2ca3267f5b182dad0b94926299f0547de37a6064',
                'refs/heads/fsgg/v2/journal/release/utel-rel-15': '95bcc12dc27f2f0b25368c28ef260e32f020dd6b',
                'refs/heads/fsgg/v2/journal/release/utel-rel-16': 'ba0096a5ff8430bb2aaddded9b0d6e012502f747',
                'refs/heads/fsgg/v2/journal/release/utel-rel-18': '5a4b5fba698ffab516fa739e95840665c29fd385',
                'refs/heads/main': '63cd206a8ed0a57ecbe554499ea88d7ed0287da3'},
 'frozenSnapshot': {'proposalTimestamp': '2026-10-09T19:46:45.449782Z',
                    'refs': {'refs/heads/fsgg/v2/journal/cutover/d5': '26d1882af9293b264df17a1fa98515e108313fe5',
                             'refs/heads/fsgg/v2/journal/operation/00': '446355e83256fb17387c88b322390ecbf5e7be5e',
                             'refs/heads/fsgg/v2/journal/operation/02': 'd49b12139490f34a9a752c5dc7749cdfe479e4f6',
                             'refs/heads/fsgg/v2/journal/operation/03': '3cbef2ca0b2535330f04773849dd66b5e0d64ee4',
                             'refs/heads/fsgg/v2/journal/operation/04': '1b0605669aeb3da8832a1630775bfda791a9d91f',
                             'refs/heads/fsgg/v2/journal/operation/05': 'b2fb170cc5ca44800c9638ff58a14eecbd8f1297',
                             'refs/heads/fsgg/v2/journal/operation/06': '4103530f372e594914c46c501b56bd61c3281825',
                             'refs/heads/fsgg/v2/journal/operation/07': '77e7d7df18020a46336e3da0a594cdf278b9add4',
                             'refs/heads/fsgg/v2/journal/operation/08': '447ec6063ab796109612b8916ebcc6712eada51e',
                             'refs/heads/fsgg/v2/journal/operation/09': '211d986ff7634c1274bc3ff8973b34a884ad84fa',
                             'refs/heads/fsgg/v2/journal/operation/0a': '4c492d81df4c60d294b8e9db160b2f2b1ff577e5',
                             'refs/heads/fsgg/v2/journal/operation/0b': 'e7c9bb7eb32210868bdd3755533923753f7d19ac',
                             'refs/heads/fsgg/v2/journal/operation/0c': '22970a7c54152687ab75d8788466bd20c8523a9f',
                             'refs/heads/fsgg/v2/journal/operation/0d': '03615a951310a91402d9b2b3ee0d8d33f9091f9e',
                             'refs/heads/fsgg/v2/journal/operation/0f': '6fbeb220e4af177a03bd6afe05443d52d0cdc38b',
                             'refs/heads/fsgg/v2/journal/operation/10': 'ad89a0f8e497cb93d61d89508bb775203bf01815',
                             'refs/heads/fsgg/v2/journal/operation/11': '8e375a1049c8fa0038e530af26749eae88a01086',
                             'refs/heads/fsgg/v2/journal/operation/12': '2db1965cfb9f1b53742139bffe1fb1230293fe78',
                             'refs/heads/fsgg/v2/journal/operation/13': 'a259b80c81d762bf6e914235bef4f22bb2ba26ff',
                             'refs/heads/fsgg/v2/journal/operation/15': '1766badafd89099b52dd39136ed9d67fab0fe932',
                             'refs/heads/fsgg/v2/journal/operation/17': '03bb60b130ddf2a093e07f64d13ff21fdc02e7fd',
                             'refs/heads/fsgg/v2/journal/operation/18': '9bdb1600fe0c4d538d2f8186afe45c241d67aba1',
                             'refs/heads/fsgg/v2/journal/operation/1a': '71756c340a87871fc70bad5adeee6dd9c462869f',
                             'refs/heads/fsgg/v2/journal/operation/1b': '96cfd6cb0e3d8e531b179413b5ecd325cf3663f8',
                             'refs/heads/fsgg/v2/journal/operation/1c': 'c76ce5c50dc7bece4924a92a61848b38baa66d3b',
                             'refs/heads/fsgg/v2/journal/operation/1d': 'ca18835369f58d70b12dc7248f8bb01c36c78ae7',
                             'refs/heads/fsgg/v2/journal/operation/1e': 'db7b97d1eed43d38a61ecea1e1ec4b306dc4301c',
                             'refs/heads/fsgg/v2/journal/operation/1f': 'f6efd61471fabb8ac6a4cd02caf37a11784f39c6',
                             'refs/heads/fsgg/v2/journal/operation/21': '24c22b996e3a451fe3b7a3f15ee40b8bb006adfe',
                             'refs/heads/fsgg/v2/journal/operation/22': '1193c880debc5a3d7b8e3292c6b81903c518477d',
                             'refs/heads/fsgg/v2/journal/operation/23': '0684f0bb037ca4d09beb5de730ad5eae230b4da6',
                             'refs/heads/fsgg/v2/journal/operation/24': 'ac506b1d77c20bb47df096809c9778b677c896b4',
                             'refs/heads/fsgg/v2/journal/operation/25': 'a1781abe818ecea4beb52edcda24d45a6f6b99c6',
                             'refs/heads/fsgg/v2/journal/operation/26': '1ec3ae522f34ed592e9407c645f7a881903d5e29',
                             'refs/heads/fsgg/v2/journal/operation/28': '7c0d16a131c5b3e5f36ec480ece8935d2ea17bd2',
                             'refs/heads/fsgg/v2/journal/operation/29': '11fb8812b5ef36cdd49305e6b62fdddcf330136a',
                             'refs/heads/fsgg/v2/journal/operation/2a': '6ea88ce77fa87e1d35a932235a9d39f66a5c58a1',
                             'refs/heads/fsgg/v2/journal/operation/2b': '2885f4e93c42de80eb8739e9bf60f797f19acb93',
                             'refs/heads/fsgg/v2/journal/operation/2c': 'b9aebe4da972725ecca328cafb053f5a2caa72b7',
                             'refs/heads/fsgg/v2/journal/operation/2d': '62a274c24aa0cae8f801119f62884e3d71faa0a3',
                             'refs/heads/fsgg/v2/journal/operation/2e': 'be4e856a994220158672df3e8f7eab80b2b23d9a',
                             'refs/heads/fsgg/v2/journal/operation/2f': '730e5a847560760c5d0e49ffa03345a2716cbd09',
                             'refs/heads/fsgg/v2/journal/operation/30': 'fd5317ee0d4bcd6ebf6512648e03baaea892adb5',
                             'refs/heads/fsgg/v2/journal/operation/31': '250ff31aae411b036ca441fcb847a9f8f886c859',
                             'refs/heads/fsgg/v2/journal/operation/32': 'd8b21ef38cdd280233e9656193c448badf00bee9',
                             'refs/heads/fsgg/v2/journal/operation/33': '22ca936990ef2409b7747753078908d6f83e6bbf',
                             'refs/heads/fsgg/v2/journal/operation/34': 'e2a0747f9c009f1c7b23e8de39d6f6eb9d3071d0',
                             'refs/heads/fsgg/v2/journal/operation/35': '74e5aad30d7e9baf16292151d900e6c7b949d7cf',
                             'refs/heads/fsgg/v2/journal/operation/36': 'e2ef6540ed8ae205efa57bb15f8d19a68f14431f',
                             'refs/heads/fsgg/v2/journal/operation/37': 'b1add0407b6bf48dd086f1be2f926c192bef554a',
                             'refs/heads/fsgg/v2/journal/operation/38': '10ee0897a3dc6121ecb5f3117cb573a68ca07891',
                             'refs/heads/fsgg/v2/journal/operation/39': 'b2a5d55ddf0c95fdc41ca50ab62006c5e8c1ea60',
                             'refs/heads/fsgg/v2/journal/operation/3a': '5b8f2b8c00877a5ce5fe2143869b95bccb420e96',
                             'refs/heads/fsgg/v2/journal/operation/3b': '10513ba0f1cbda651bee341abb670b2736e61dfa',
                             'refs/heads/fsgg/v2/journal/operation/3c': 'dad18042c6317b7b78aff6513f566e3b233c14dc',
                             'refs/heads/fsgg/v2/journal/operation/3d': '819d9ed5a1610524d7df20d32de823bac9937ca9',
                             'refs/heads/fsgg/v2/journal/operation/3e': 'f915029082130e139ba5d2dc65a3af65f6bdee7d',
                             'refs/heads/fsgg/v2/journal/operation/3f': '487175d25342e4c1e0f8a94cc31d472572878932',
                             'refs/heads/fsgg/v2/journal/operation/40': '55dccbacc4ecc3b202286147a6738c7dd9133242',
                             'refs/heads/fsgg/v2/journal/operation/41': 'f649c8f254737c2a636025792e5526460dd1492e',
                             'refs/heads/fsgg/v2/journal/operation/44': '3247ff90f396f96ec50aafb14ce5d5d3dbf68314',
                             'refs/heads/fsgg/v2/journal/operation/45': 'b8a4c107e49e5e8d15c91e5294000bd040fe187f',
                             'refs/heads/fsgg/v2/journal/operation/46': 'c674e8e500ed72fea5384ffbde0885c250b602ae',
                             'refs/heads/fsgg/v2/journal/operation/47': 'd4b14ecabaaa6b96b5f9df344535531be3921256',
                             'refs/heads/fsgg/v2/journal/operation/48': '65c3bfa31dfb73f7e98dc1a8002cbe61e57be2a4',
                             'refs/heads/fsgg/v2/journal/operation/49': '551d5a5bf852c2659386fcdfbddd51a6986b96c6',
                             'refs/heads/fsgg/v2/journal/operation/4a': 'e366cd15974968d6247f8ab7d917e97170a85dd8',
                             'refs/heads/fsgg/v2/journal/operation/4b': 'c95ffb642c88efbecd31bfad329374661f8487a8',
                             'refs/heads/fsgg/v2/journal/operation/4c': 'f8e241004c4029fd4fb4faf0cb1fbdd9dbaf5b56',
                             'refs/heads/fsgg/v2/journal/operation/4d': 'd7396beeb5d10db9e6a794bee8b3c77a33acb6ad',
                             'refs/heads/fsgg/v2/journal/operation/4e': 'c951f7c06221ffebec131d76a67d1f22d58cba80',
                             'refs/heads/fsgg/v2/journal/operation/4f': '334fd3b2193688bffeb0eb65b84c09aab1b299cd',
                             'refs/heads/fsgg/v2/journal/operation/50': 'cf4985e6715b8062d2b3fd31d0004fa0bdd9ce83',
                             'refs/heads/fsgg/v2/journal/operation/51': 'd0990c97754e63c7fd2f0e982528427b3594b39c',
                             'refs/heads/fsgg/v2/journal/operation/52': 'cc6d1ffe4d4e9bd2800916589bacefa5c02d13d5',
                             'refs/heads/fsgg/v2/journal/operation/53': '38144de1e4442a60c6394f7a1a37eeb7d4fa7444',
                             'refs/heads/fsgg/v2/journal/operation/54': '3c3404d933255770480a7d20f3d9c17eee007f19',
                             'refs/heads/fsgg/v2/journal/operation/55': '458324f95fde7fcecbdd50ee87f2936fe1bb348a',
                             'refs/heads/fsgg/v2/journal/operation/56': 'ce678832f2732a4be9b5daffed4e2b07bd5a1080',
                             'refs/heads/fsgg/v2/journal/operation/57': 'e27efa872fce2fc073055424ce66b445664ff51c',
                             'refs/heads/fsgg/v2/journal/operation/58': '381085d844e78669ffd5ba82c568a37c22948d32',
                             'refs/heads/fsgg/v2/journal/operation/5a': 'e08002ccd803f90524623668caca17ed419e383e',
                             'refs/heads/fsgg/v2/journal/operation/5b': 'dc9a0fad7ab7741f6e64a4e93b77faf4892f014c',
                             'refs/heads/fsgg/v2/journal/operation/5c': '2535db9258a666b80a73924e083d1a5cc73b0270',
                             'refs/heads/fsgg/v2/journal/operation/5d': '744617f73781749fe2bc6038f0ef7b3fe5c5f4df',
                             'refs/heads/fsgg/v2/journal/operation/5e': '923bb011e106cb0600e1a74c055c8349fd14e5cb',
                             'refs/heads/fsgg/v2/journal/operation/5f': 'd070dbb7dbaff4c483870759d58651f444b19a54',
                             'refs/heads/fsgg/v2/journal/operation/60': '2740251ba3c2463bcf07db865729c8d47bd2fd32',
                             'refs/heads/fsgg/v2/journal/operation/61': '776921df86b0d2f7ffd9fed13b59e080e6529f02',
                             'refs/heads/fsgg/v2/journal/operation/62': 'f473bf18293cfafa31f7e27c4be828a888f3d63c',
                             'refs/heads/fsgg/v2/journal/operation/63': '97caae9806638f0c4344c05199d76225060bfcdc',
                             'refs/heads/fsgg/v2/journal/operation/65': 'ab610e92f58f3322a2b9f725cd0f261a28c68508',
                             'refs/heads/fsgg/v2/journal/operation/68': 'b3dd70a0efc04ffb54158b6b3f3787cafd6cb16a',
                             'refs/heads/fsgg/v2/journal/operation/6a': '7961655931e6b650108826ef857a530eee060128',
                             'refs/heads/fsgg/v2/journal/operation/6b': '3d2e6ed9e3e8ab63d9166a8be63b3481cd8a007d',
                             'refs/heads/fsgg/v2/journal/operation/6c': 'f811adb24f5bd67ba61c0349ca27c13ed3a7820b',
                             'refs/heads/fsgg/v2/journal/operation/6d': 'dd15eae0b47b27f536b79cb16f51e36234413fed',
                             'refs/heads/fsgg/v2/journal/operation/6e': '3774c0f31575cb49fb80c257ccb015dcdf26b1d7',
                             'refs/heads/fsgg/v2/journal/operation/6f': '9ac9d2fbaeab09df6c33c28094e2900a63f93f56',
                             'refs/heads/fsgg/v2/journal/operation/70': '2defac63452403a54cddb0311e5caa55f5fc3d7e',
                             'refs/heads/fsgg/v2/journal/operation/72': '9967e4baf1294d46ac11acde47cc0596c8f6ce5c',
                             'refs/heads/fsgg/v2/journal/operation/73': '91275fa908de8ec9d7fb2c652f52d615073a3103',
                             'refs/heads/fsgg/v2/journal/operation/74': '0c98bbd9b6a482dffcc1a5acddecc423c9d0fbdf',
                             'refs/heads/fsgg/v2/journal/operation/75': 'bf2aa69899a5d9088902a3fab8aaecf416b8d691',
                             'refs/heads/fsgg/v2/journal/operation/76': 'b84db404a39335bc98b96bf97470dff24ec82869',
                             'refs/heads/fsgg/v2/journal/operation/77': 'd1f304c3ebf056fcb7020e8c0e7e810a57aa80ef',
                             'refs/heads/fsgg/v2/journal/operation/78': '24c2e8295a2953d77e27adb39e84e1876b993517',
                             'refs/heads/fsgg/v2/journal/operation/79': 'dc1273f459f553addec82631cce1405e780d05f0',
                             'refs/heads/fsgg/v2/journal/operation/7a': 'a57c724c3f22babf515aa8ab724f184570d7de7c',
                             'refs/heads/fsgg/v2/journal/operation/7b': 'b2de33ac26c96461388fd39e812660fa4c505f8d',
                             'refs/heads/fsgg/v2/journal/operation/7d': 'c79ce96874a5431c5bbd862b490f496810946319',
                             'refs/heads/fsgg/v2/journal/operation/7e': '7f5971217239e4b189803677f43768f9d875c14e',
                             'refs/heads/fsgg/v2/journal/operation/7f': '3118835531fd632f9aa29bcc0f1d79d6e718a892',
                             'refs/heads/fsgg/v2/journal/operation/80': 'd472968f4bf240348f4e4a4767b70b29a67a373c',
                             'refs/heads/fsgg/v2/journal/operation/83': 'c0bf77ef1da87702008b3aae523cfbc20be0443a',
                             'refs/heads/fsgg/v2/journal/operation/84': '91ea6751becff7a56e318c08f641c4ae789158b7',
                             'refs/heads/fsgg/v2/journal/operation/86': '96a28eddc7aea8327b086fad721823aada7f6b62',
                             'refs/heads/fsgg/v2/journal/operation/87': 'e193ecbce7401361fd063b15cb4d6e45214e15dc',
                             'refs/heads/fsgg/v2/journal/operation/88': '31a794ce1a2eb00eb118dd244af7c3085fe7c7a1',
                             'refs/heads/fsgg/v2/journal/operation/89': '0ba57fc963504ab41208eae4bc90ef469e37214d',
                             'refs/heads/fsgg/v2/journal/operation/8a': '90506858b0876f04824912a543205cebff4ea6e4',
                             'refs/heads/fsgg/v2/journal/operation/8c': 'cb05c5f6ef89cb45dbd0f510e47c23e1b60dcd55',
                             'refs/heads/fsgg/v2/journal/operation/8d': 'c9eec229804b9e8fa04c450791aea35de8580dbb',
                             'refs/heads/fsgg/v2/journal/operation/8e': '114c6340bbfff9394a957f43bf39836b47b2c90f',
                             'refs/heads/fsgg/v2/journal/operation/8f': '02b195b649a4144808dc98e089fa33dc9d549727',
                             'refs/heads/fsgg/v2/journal/operation/90': '23e4304d2ed6c396629699ad8281319c7ca3d944',
                             'refs/heads/fsgg/v2/journal/operation/91': '37de66523db3c44538b9286b0a42da11d1af3146',
                             'refs/heads/fsgg/v2/journal/operation/92': '62a3be3d419ad762340104f941746ecd1c2a68f5',
                             'refs/heads/fsgg/v2/journal/operation/93': 'a514922e502fb24805fc0b9c9c2b57bcf580a9f2',
                             'refs/heads/fsgg/v2/journal/operation/94': '973596cc2f09973e80f68402a40c2c4224b6c9f9',
                             'refs/heads/fsgg/v2/journal/operation/95': 'd777b210a653cc32ef408c0fa2e3833349e0877c',
                             'refs/heads/fsgg/v2/journal/operation/96': 'b665a92cd091983e114221ffd0d382f7061b8e24',
                             'refs/heads/fsgg/v2/journal/operation/99': '7b79967b16a89372f02149a26da286382c4082dd',
                             'refs/heads/fsgg/v2/journal/operation/9a': '16e5096cc559e94ff7c079d01baec641535c9f7a',
                             'refs/heads/fsgg/v2/journal/operation/9b': '5c018c91762ba2b983d276213b91e8efe7931b1a',
                             'refs/heads/fsgg/v2/journal/operation/9c': '861f76b0d31a1570ed3f8eb22e2a9ab60fad799d',
                             'refs/heads/fsgg/v2/journal/operation/9d': '5b8e671e45c31476ee09533ed953e02ed5883fee',
                             'refs/heads/fsgg/v2/journal/operation/9e': '8525bd1253acf6809c83f095efa7cae4543111df',
                             'refs/heads/fsgg/v2/journal/operation/9f': 'a0e0512af3fbab528cdcdf7a25ed2a1760a1e676',
                             'refs/heads/fsgg/v2/journal/operation/a0': '2e4b957c4e5f361055ef56b6cf9e51220589931f',
                             'refs/heads/fsgg/v2/journal/operation/a1': '70c45c81fa5de4cade44e82d680d93a1fcb48cfb',
                             'refs/heads/fsgg/v2/journal/operation/a2': 'e0c83ecaac260449bdcb99e55239926d883e373b',
                             'refs/heads/fsgg/v2/journal/operation/a3': '7a24d7cd7e2f87ee73813183a9157662ab4e92d5',
                             'refs/heads/fsgg/v2/journal/operation/a5': 'e5192f55bcff6635ecce05aed6529f9e262959f0',
                             'refs/heads/fsgg/v2/journal/operation/a6': '8d3540df31fed124649df4a35e80e5cbbdfabed2',
                             'refs/heads/fsgg/v2/journal/operation/a7': '7b36ab817b1349dc815d854ec0717f2bc2fe8dc2',
                             'refs/heads/fsgg/v2/journal/operation/a8': '6867d1c657c04e175322e5c69ee8f631b449166e',
                             'refs/heads/fsgg/v2/journal/operation/aa': 'ddd976d7226d45f5825aea4cf6d6c29d02067e6f',
                             'refs/heads/fsgg/v2/journal/operation/ab': '7ee0960136a822c560fac3846f0f36f12d560017',
                             'refs/heads/fsgg/v2/journal/operation/ac': '92b14951e48f676097a86de24df95e0450ca1c7f',
                             'refs/heads/fsgg/v2/journal/operation/ad': 'd912ddf834caf84dd5ef64174819245c4c80cbce',
                             'refs/heads/fsgg/v2/journal/operation/ae': '4b0ed59aa60d7a087091557965b14cce72590ad4',
                             'refs/heads/fsgg/v2/journal/operation/af': 'ba110053f383339ca07361c174e6e1b5ffe5001f',
                             'refs/heads/fsgg/v2/journal/operation/b0': '13b9359a0f4fa5c7cacac2e1b5e4848a03e6c3aa',
                             'refs/heads/fsgg/v2/journal/operation/b1': '5f00134c67cbc52e9d180a1cef59515312b7be8d',
                             'refs/heads/fsgg/v2/journal/operation/b2': '9cb82c4cdfb1e31f4ba64c619f7c25a1e71bc38b',
                             'refs/heads/fsgg/v2/journal/operation/b3': '0edbc0005ed5ba68786026a379aa7949421cbe82',
                             'refs/heads/fsgg/v2/journal/operation/b4': '4c5487790bb34615ba61d8ff931a2f73cc7b88b9',
                             'refs/heads/fsgg/v2/journal/operation/b5': '1a8fda3385fbd8215494de321e9479fef0ca7cbd',
                             'refs/heads/fsgg/v2/journal/operation/b6': 'af4224640a36d05ee2b9187b0d66d1bbbed45b4a',
                             'refs/heads/fsgg/v2/journal/operation/b7': 'eded5ce888b3c7834f1e4573afe8b63c937e7e79',
                             'refs/heads/fsgg/v2/journal/operation/b8': '861c5944549ca35673af57cca493a2c02e2fdcf3',
                             'refs/heads/fsgg/v2/journal/operation/b9': 'fad179918adf39bff87835661baef158502c42eb',
                             'refs/heads/fsgg/v2/journal/operation/ba': 'f9d7ab385f6b32a00131d7d0935140beb6b3f92b',
                             'refs/heads/fsgg/v2/journal/operation/bb': 'df3b8a25657c0c84662e4aabea6fb18c0e57d64b',
                             'refs/heads/fsgg/v2/journal/operation/bc': 'a9214fce05de83be0a01c20026e13c2f66897cb2',
                             'refs/heads/fsgg/v2/journal/operation/bd': 'fa428eea1cfafd5ba0ac01406db8e173ac82b50f',
                             'refs/heads/fsgg/v2/journal/operation/be': 'fa954236c36cd35e2d616855c4b97016b1631c57',
                             'refs/heads/fsgg/v2/journal/operation/bf': '584c8bbe1e482e5e43ba2b0cd9ec671c8fb6ef5d',
                             'refs/heads/fsgg/v2/journal/operation/c0': '7a41318ccba0cb51b294168120b72ac5e54e3288',
                             'refs/heads/fsgg/v2/journal/operation/c1': '95842330e2285f2f8a9eaca83de5562cf6262520',
                             'refs/heads/fsgg/v2/journal/operation/c2': 'a6b7af5e6ea726be19f72dcab09d68e6e89f021b',
                             'refs/heads/fsgg/v2/journal/operation/c3': '68719838fc2a0506bc91ad538bb0d8dedec2344e',
                             'refs/heads/fsgg/v2/journal/operation/c4': '800ae6118704a4aefa3aa0a3ea0c34c09141f184',
                             'refs/heads/fsgg/v2/journal/operation/c6': '9e57ccd9fc568de77369173ad3133f853e3c7944',
                             'refs/heads/fsgg/v2/journal/operation/c7': '735dd70e0d26aebb72bb6be849d859179cfb3756',
                             'refs/heads/fsgg/v2/journal/operation/c8': '60aa974b9c1a2d90e5cec7340ee479a5c05c5e5a',
                             'refs/heads/fsgg/v2/journal/operation/c9': 'feffd383182a716b1c45402f45d6b648f99aeb40',
                             'refs/heads/fsgg/v2/journal/operation/ca': 'e8934776a12c52aa54d6545f04140474de3d17b5',
                             'refs/heads/fsgg/v2/journal/operation/cb': '8c9cc84d90ebfc40639b7f68b01c7d5a8b57e117',
                             'refs/heads/fsgg/v2/journal/operation/cc': '542174d7dbbbb4acf6865523ccbe0c10ca58a2ba',
                             'refs/heads/fsgg/v2/journal/operation/cd': 'eebf83cb675e6a186ea99a2552121fccb39f5ca1',
                             'refs/heads/fsgg/v2/journal/operation/ce': 'd8ad7dfca24cac7a22795ece2aa55abd090aea63',
                             'refs/heads/fsgg/v2/journal/operation/d0': 'abbb905b2621cbd92fcfc3675aa0c4b26f78d8de',
                             'refs/heads/fsgg/v2/journal/operation/d1': '2f8dfe5ffde03c1702c490d1c0459bb7800862eb',
                             'refs/heads/fsgg/v2/journal/operation/d3': '896de64e1f28d2830c49118b47d31bd18ea6dea6',
                             'refs/heads/fsgg/v2/journal/operation/d4': 'fc81f9f819bc711794ad67a934a8d181ff351fe9',
                             'refs/heads/fsgg/v2/journal/operation/d5': 'cc133217e8d180b5614a71b35f21d45be4f965c3',
                             'refs/heads/fsgg/v2/journal/operation/d6': '83f4c3f7ae31228d8bf67265871312ad8d37d8e3',
                             'refs/heads/fsgg/v2/journal/operation/d7': '85ad3069d2a0481639d0c5298d88706f4e6510c7',
                             'refs/heads/fsgg/v2/journal/operation/d8': '1900ddfa61f780c64111917ffca4cc644ef195af',
                             'refs/heads/fsgg/v2/journal/operation/d9': '49f5bd7046d5b5305f8b432e77550f026ca196c9',
                             'refs/heads/fsgg/v2/journal/operation/da': '5f7c82622d39ebe35b205ef742b64df2aa4f0d6d',
                             'refs/heads/fsgg/v2/journal/operation/db': 'd9d0d79e80dc55f595bc9fd2afe56f5fa75dc970',
                             'refs/heads/fsgg/v2/journal/operation/dc': '3c01a0ce3137f582296e4d648148aa8cade10820',
                             'refs/heads/fsgg/v2/journal/operation/dd': 'ac13d332cb95dd8babfb9f74afdd4ab34e9d132e',
                             'refs/heads/fsgg/v2/journal/operation/df': '70637a7cab77991219571ff8fdc2a9becf5eb63e',
                             'refs/heads/fsgg/v2/journal/operation/e0': 'a2f965a9942e383726283f3beb418c5cfb58501a',
                             'refs/heads/fsgg/v2/journal/operation/e1': 'cd6fc27607bc93696008cc4b000db17763e1eab3',
                             'refs/heads/fsgg/v2/journal/operation/e3': '6329ec2c89024f2226ab2b95fde3fd30bd22ea7e',
                             'refs/heads/fsgg/v2/journal/operation/e4': 'b115645d4336b616d0afb76031cd174c498e6f23',
                             'refs/heads/fsgg/v2/journal/operation/e5': '2ab94b31de7ef42e20eb0d49d50cdc5c24bf3958',
                             'refs/heads/fsgg/v2/journal/operation/e6': '0d65e8722dc423e954d4ae46f6c336392e80da26',
                             'refs/heads/fsgg/v2/journal/operation/e7': '1bfde43ae5a5ef5031419111432442ded495f0e4',
                             'refs/heads/fsgg/v2/journal/operation/e8': 'f1f97167d47c32230dca38da5fd804260f924cfe',
                             'refs/heads/fsgg/v2/journal/operation/e9': 'f5679921a944379f3efe3ac829f92d010ae0ad0f',
                             'refs/heads/fsgg/v2/journal/operation/eb': 'b2a75622bb0878130d3a878154ccaecdc0139ac3',
                             'refs/heads/fsgg/v2/journal/operation/ed': '2771f39738c030e033e4c1b048357df93ca9e120',
                             'refs/heads/fsgg/v2/journal/operation/ee': 'd2dde0bacbc32c374145190ade6c04329d5c23e6',
                             'refs/heads/fsgg/v2/journal/operation/f0': '50a67b9da135d39f01187d21245f2a42e858d4d3',
                             'refs/heads/fsgg/v2/journal/operation/f1': '3f5be9faa65f59c29cbd4f035200d3f12cda31b4',
                             'refs/heads/fsgg/v2/journal/operation/f2': 'dbeb42b4699bb678683e25330b4a282ddf65ef9d',
                             'refs/heads/fsgg/v2/journal/operation/f3': '563e240ee6ee71b7f7a929066fa2296739ae1d82',
                             'refs/heads/fsgg/v2/journal/operation/f4': 'f4f28f1ed12cef48eb93eb665e301753903115d8',
                             'refs/heads/fsgg/v2/journal/operation/f5': 'a1a80e953856c6390e9f045372aab8cbe27f6b00',
                             'refs/heads/fsgg/v2/journal/operation/f6': 'f8c9f979786a5f9a8ec9d9326df6411859661fd2',
                             'refs/heads/fsgg/v2/journal/operation/f7': '4948a1b1bb1ab6be56ee96a500df43aac65dff06',
                             'refs/heads/fsgg/v2/journal/operation/f8': '8cf6b9255fc34ca546bdd8c155f2d099182a977c',
                             'refs/heads/fsgg/v2/journal/operation/f9': 'f6a6afebae6466565df696d9b5a606027e99c67c',
                             'refs/heads/fsgg/v2/journal/operation/fa': '2ce4eb7d34d7ee4d2278c34b089b395db18b8bb9',
                             'refs/heads/fsgg/v2/journal/operation/fc': 'a414e09913d2dcccce81bc2960b40d16cc88af5f',
                             'refs/heads/fsgg/v2/journal/operation/fd': 'b2070fa4d908eb3291d36a0d18587a294bf766bb',
                             'refs/heads/fsgg/v2/journal/operation/fe': 'c61940076ffa69cf8d1f1765bbef38dcc077b33c',
                             'refs/heads/fsgg/v2/journal/operation/ff': '48f94283ba93d571231c6158122d1b374bd98f09',
                             'refs/heads/fsgg/v2/journal/qualification/33330220225-1': '989ef63adb95a03855a4f676e3831a2600e45f07',
                             'refs/heads/fsgg/v2/journal/release/board-v2-product-coherent-097': 'fb72758c6e61155dd2e8860adbc8f1cc3088dbb0',
                             'refs/heads/fsgg/v2/journal/release/board-v2-product-creator-014': '7ada85fd773a3f098a4c84f11847980151578820',
                             'refs/heads/fsgg/v2/journal/release/board-v2-product-creator-015': '4495a822c11b15168c69ed6e138b5ce8a9b7a895',
                             'refs/heads/fsgg/v2/journal/release/svg-d5-wizard-012': '251501fc3e2bbb2ef229f0f87c77668c9793d5f1',
                             'refs/heads/fsgg/v2/journal/release/tsdd-knowledge-wizard-013': 'e6ef48a4dfa68ce66a6b215dea1880020157dcee',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-01': 'ade613fbf747e545ac53c39e44b0e17734fddd0f',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-02': '5bc15719053cf4483a2123ef85603ea507fbad3f',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-03': '9ef3c55a16a71f2958eb4c3946f958a9dde320f8',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-04': 'bc8047049db3803e971fcfed27f077278df6b582',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-05': '07a1098fa36750bda9e3ee060b57738fceb4c6b2',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-06': 'f25b93fa71c78e36c9a5d65f3eff1ef0caa4ac98',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-07': '91c1879c2de382ee1b94d9bd3f4347582a3c005e',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-08': '072235dd8bbfc8116f0d95f9b86abcae992200ee',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-09': '26b6a2393dd82901cdc2651a2b75a8a942eaed8c',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-10': '203ba60f7df146743d4a7373a52f55fc6c22858e',
                             'refs/heads/fsgg/v2/journal/release/utel-host-rel-11': '57a3d1eac66d3e246d4e8de03988e0a35b7b3cb3',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-01': 'b6b821bd95d12a19251f87dc92cea8bf8a34de80',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-02': '44627b5d3a9eefc4c0edb809dcea854b023cf589',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-03': 'd713cda85019202cde2460239166af461c6aeba2',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-04': '2a1fede5ad8979df936eb604c257920302bea521',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-05': 'e2f332f61f1f29bcd9fdcb7c3c8fb2bf9b27d5e3',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-06': 'c02488d6a7d6e94db16c1430c9b4bdb0e74521a5',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-07': '2df17c781d151e1b09700b2cc0ee2b7b3fb0c021',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-08': 'e859c9c4379e22aeabc909bb8d036fede109a5af',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-09': '0a45b80936e4464b891971237fbea6ffdcba37ae',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-10': '78eff3ca803517859fa2968fe071d4ceb1fbe1b2',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-11': '8d9f5466937a3de73c54b015cd7327b4a8cc6ada',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-13': '2ca3267f5b182dad0b94926299f0547de37a6064',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-15': '95bcc12dc27f2f0b25368c28ef260e32f020dd6b',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-16': 'ba0096a5ff8430bb2aaddded9b0d6e012502f747',
                             'refs/heads/fsgg/v2/journal/release/utel-rel-18': '5a4b5fba698ffab516fa739e95840665c29fd385',
                             'refs/heads/main': '63cd206a8ed0a57ecbe554499ea88d7ed0287da3'},
                    'repository': 'FS-GG/FS.GG.Coordination.Authority',
                    'repositoryId': 1351660651},
 'manifestSha256': '1a694847109c7fdf43f7570537d5feda092e55d088882bfe3f8ab9d9c4820c85',
 'operatorLogin': 'EHotwagner',
 'proposalGeneratorRevision': '07c6485044db953590f2b4ae85b47a214dc36633',
 'proposalToolSha256': '4c7e8e24b3a9628c96d47a1110f96f1432ee78efff8b14d08ce8e3ca645838ba',
 'protectionAcceptedAt': '2026-10-09T19:45:41.395149+00:00',
 'publication': {'artifactId': 11641897208,
                 'artifactSha256': '05446ca8843e16f0b24fd963b9c332583483f52065915caeaab5062f08d9584d',
                 'repository': 'FS-GG/FS.GG.Coordination',
                 'repositoryId': 1346720714,
                 'runId': 37983508705,
                 'sourceSha': '401a89e3e63c0eefda02124475e92d0c4b752cfd',
                 'workflowPath': '.github/workflows/callable-cli-release-publish.yml'},
 'publishedCli': {'archiveSha256': 'a8cd6d602e1203257e1241df0b5dfdb9d867334b46dc406d8cdaa8e6d2b3019c',
                  'packageId': 'FS.GG.Coordination.Cli',
                  'version': '0.3.0'},
 'representativeFrozenRefs': ['refs/heads/fsgg/v2/journal/operation/89',
                              'refs/heads/fsgg/v2/journal/release/utel-rel-15',
                              'refs/heads/fsgg/v2/journal/cutover/d5'],
 'rulesets': {'legacyFence': {'_links': {'html': {'href': 'https://github.com/FS-GG/FS.GG.Coordination.Authority/rules/24812732'},
                                         'self': {'href': 'https://api.github.com/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/24812732'}},
                              'bypass_actors': [],
                              'conditions': {'ref_name': {'exclude': [],
                                                          'include': ['refs/heads/fsgg/v2/journal/**/*']}},
                              'created_at': '2026-10-09T21:43:46.624+02:00',
                              'current_user_can_bypass': 'never',
                              'enforcement': 'active',
                              'id': 24812732,
                              'name': 'retired-v2-journal-namespace',
                              'node_id': 'RRS_lACqUmVwb3NpdG9yec5QkLRrzgF6nLw',
                              'rules': [{'type': 'creation'}, {'type': 'update'}],
                              'source': 'FS-GG/FS.GG.Coordination.Authority',
                              'source_type': 'Repository',
                              'target': 'branch',
                              'updated_at': '2026-10-09T21:43:46.660+02:00'},
              'legacyTagFence': {'_links': {'html': {'href': 'https://github.com/FS-GG/FS.GG.Coordination.Authority/rules/24812733'},
                                            'self': {'href': 'https://api.github.com/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/24812733'}},
                                 'bypass_actors': [],
                                 'conditions': {'ref_name': {'exclude': [],
                                                             'include': ['refs/tags/fsgg/v2/fleet-cutover/**/*']}},
                                 'created_at': '2026-10-09T21:43:48.067+02:00',
                                 'current_user_can_bypass': 'never',
                                 'enforcement': 'active',
                                 'id': 24812733,
                                 'name': 'retired-v2-fleet-cutover-tag-creation',
                                 'node_id': 'RRS_lACqUmVwb3NpdG9yec5QkLRrzgF6nL0',
                                 'rules': [{'type': 'creation'}],
                                 'source': 'FS-GG/FS.GG.Coordination.Authority',
                                 'source_type': 'Repository',
                                 'target': 'tag',
                                 'updated_at': '2026-10-09T21:43:48.088+02:00'},
              'mainIntegrity': {'_links': {'html': {'href': 'https://github.com/FS-GG/FS.GG.Coordination.Authority/rules/24802698'},
                                           'self': {'href': 'https://api.github.com/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/24802698'}},
                                'bypass_actors': [],
                                'conditions': {'ref_name': {'exclude': [], 'include': ['refs/heads/main']}},
                                'created_at': '2026-10-09T18:56:42.683+02:00',
                                'current_user_can_bypass': 'never',
                                'enforcement': 'active',
                                'id': 24802698,
                                'name': 'ordinary-v2-main-integrity',
                                'node_id': 'RRS_lACqUmVwb3NpdG9yec5QkLRrzgF6dYo',
                                'rules': [{'type': 'deletion'}, {'type': 'non_fast_forward'}],
                                'source': 'FS-GG/FS.GG.Coordination.Authority',
                                'source_type': 'Repository',
                                'target': 'branch',
                                'updated_at': '2026-10-09T18:56:42.726+02:00'},
              'mainWriter': {'_links': {'html': {'href': 'https://github.com/FS-GG/FS.GG.Coordination.Authority/rules/24802693'},
                                        'self': {'href': 'https://api.github.com/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/24802693'}},
                             'bypass_actors': [{'actor_id': 4882140,
                                                'actor_type': 'Integration',
                                                'bypass_mode': 'always'},
                                               {'actor_id': 5064713,
                                                'actor_type': 'Integration',
                                                'bypass_mode': 'always'}],
                             'conditions': {'ref_name': {'exclude': [], 'include': ['refs/heads/main']}},
                             'created_at': '2026-10-09T18:56:41.118+02:00',
                             'current_user_can_bypass': 'never',
                             'enforcement': 'active',
                             'id': 24802693,
                             'name': 'ordinary-v2-main-writer',
                             'node_id': 'RRS_lACqUmVwb3NpdG9yec5QkLRrzgF6dYU',
                             'rules': [{'type': 'creation'}, {'type': 'update'}],
                             'source': 'FS-GG/FS.GG.Coordination.Authority',
                             'source_type': 'Repository',
                             'target': 'branch',
                             'updated_at': '2026-10-09T18:56:41.235+02:00'}}}
AUTHORITY_REMOTE = "https://github.com/FS-GG/FS.GG.Coordination.Authority.git"


def isolated_git(directory, *arguments, data=None, credentials=None, allow_failure=False):
    env = {"PATH": os.environ.get("PATH", "/usr/bin:/bin"), "GIT_CONFIG_NOSYSTEM": "1",
           "GIT_CONFIG_GLOBAL": "/dev/null", "GIT_TERMINAL_PROMPT": "0", **(credentials or {})}
    result = subprocess.run(["git", "-c", "credential.helper=", "-c", "core.hooksPath=/dev/null",
                             "-c", "http.sslVerify=true", "-C", str(directory), *arguments],
                            input=data, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                            timeout=60, check=False)
    require(allow_failure or result.returncode == 0, "isolated Git operation refused")
    require(len(result.stdout) <= 2 * 1024 * 1024, "isolated Git output exceeds bound")
    return result


def stage_proposal(directory, manifest, fetch):
    isolated_git(directory, "init", "--bare", "--quiet")
    fetch(directory, sorted(set(manifest["snapshot"]["refs"].values())))
    seen = set()
    for obj in manifest["objects"]:
        require(obj["type"] in ("blob", "tree", "commit") and re.fullmatch(r"[0-9a-f]{40}", obj["oid"])
                and obj["oid"] not in seen, "proposal object identity")
        seen.add(obj["oid"])
        payload = base64.b64decode(obj["base64"], validate=True)
        require(len(payload) <= 2 * 1024 * 1024, "proposal object size")
        expected = hashlib.sha1(obj["type"].encode() + b" " + str(len(payload)).encode() + b"\0" + payload).hexdigest()
        require(expected == obj["oid"], "proposal raw object hash differs")
        actual = isolated_git(directory, "hash-object", "-t", obj["type"], "-w", "--stdin", data=payload).stdout.decode().strip()
        require(actual == obj["oid"], "staged proposal object differs")
    current = manifest["expectedMain"]
    require(current == manifest["snapshot"]["refs"]["refs/heads/main"], "proposal expected main differs")
    for step in manifest["steps"]:
        require(step["expectedMain"] == current and step["parents"][0] == current and 1 <= len(step["parents"]) <= 2, "proposal step CAS chain")
        details = isolated_git(directory, "rev-list", "--parents", "-n", "1", step["commit"]).stdout.decode().split()
        require(details == [step["commit"], *step["parents"]], "proposal commit parents differ")
        tree = isolated_git(directory, "rev-parse", step["commit"] + "^{tree}").stdout.decode().strip()
        require(tree == step["tree"], "proposal step tree differs")
        current = step["commit"]
    require(current == manifest["desiredMain"] and manifest["steps"][-1]["tree"] == manifest["desiredTree"], "proposal final binding")
    isolated_git(directory, "fsck", "--strict", "--no-reflogs", current)
    for oid in set(manifest["snapshot"]["refs"].values()):
        isolated_git(directory, "merge-base", "--is-ancestor", oid, current)
    for item in manifest["files"]:
        require(item["path"].startswith("state/") and ".." not in item["path"].split("/"), "proposal file path")
        payload = isolated_git(directory, "show", current + ":" + item["path"]).stdout
        require(hashlib.sha256(payload).hexdigest() == item["sha256"] and len(payload) == item["bytes"], "proposal raw file differs")
        blob = isolated_git(directory, "rev-parse", current + ":" + item["path"]).stdout.decode().strip()
        require(blob == item["blob"], "proposal file blob differs")


def apply_once(manifest, guard, fetch, push, verify, output):
    """Internal effect seam. Live entrypoint supplies source-owned native admission.

    A response is not acceptance. Exactly one lease push is followed by native
    observation even when the push response was lost. No business effects run.
    """
    result = {"importApplied": False, "reconciliationComplete": False,
              "originalUnresolved": manifest["unresolved"], "pushAttempts": 0, "result": "unresolved"}
    output.mkdir(parents=True, exist_ok=True)
    destination = output / "apply-observation.json"
    require(not destination.exists(), "apply evidence already exists; no overwrite or retry")
    with (output / "apply-intent.json").open("xb") as intent:
        intent.write(canonical({"expectedMain":manifest["expectedMain"],"desiredMain":manifest["desiredMain"],
                                "manifestSha256":hashlib.sha256(canonical(manifest)).hexdigest(),"businessEffects":False}))
    guard(manifest["expectedMain"])
    with tempfile.TemporaryDirectory(prefix="authority-import-staging-") as scratch:
        directory = Path(scratch)
        stage_proposal(directory, manifest, fetch)
        guard(manifest["expectedMain"])
        result["pushAttempts"] = 1
        try:
            result["pushResponse"] = "reported-success" if push(directory, manifest["expectedMain"], manifest["desiredMain"]) else "failed-or-unknown"
        except (OSError, subprocess.SubprocessError, RuntimeError, ValueError):
            result["pushResponse"] = "failed-or-unknown"
        try:
            observation = verify(manifest)
            result["observedMain"] = observation.get("observedMain")
            require(observation.get("verified") is True, "native readback incomplete")
            guard(manifest["desiredMain"])
            require(result["observedMain"] == manifest["desiredMain"], "native imported main differs")
            result["importApplied"] = True
            result["result"] = "import-applied-original-reconciliation-unchanged"
        except (OSError, subprocess.SubprocessError, RuntimeError, ValueError, KeyError, TypeError):
            result["readback"] = "incomplete-or-refused"
    destination.write_bytes(canonical(result))
    return result


def validate_native_guard(api, refs, expected_main, binding):
    repository = api.get(f"repos/{REPOSITORY}")
    require(repository.get("id") == 1351660651 and repository.get("full_name") == REPOSITORY, "native Authority identity")
    expected_refs = {**binding["frozenRefs"], "refs/heads/main": expected_main}
    require(refs() == expected_refs, "native frozen ref vector differs")
    controls = binding["rulesets"]
    require(controls["mainWriter"]["id"]==24802693 and controls["mainIntegrity"]["id"]==24802698, "main protection identity")
    accepted=datetime.fromisoformat(binding["protectionAcceptedAt"].replace("Z","+00:00"))
    from datetime import timezone
    require(accepted.tzinfo is not None and accepted <= datetime.now(timezone.utc), "protection acceptance timestamp")
    for name, expected in controls.items():
        native = api.get(f"repos/{REPOSITORY}/rulesets/{expected['id']}")
        fields = ("id", "name", "target", "enforcement", "conditions", "rules")
        require(all(field in native and field in expected and native[field] == expected[field] for field in fields), "native ruleset identity/visibility differs")
        # Existing enrollment contract: the reviewed full actor binding is stabilized,
        # then unchanged native version/visible fields detect drift when actors are hidden.
        # Omission remains unknown, never an empty roster or direct live-roster proof.
        require(isinstance(expected.get("bypass_actors"), list), "reviewed full bypass binding missing")
        if "bypass_actors" in native:
            require(native["bypass_actors"] == expected["bypass_actors"], "native bypass binding differs")
        require(expected["enforcement"] == "active", "native protection is not active")
        updated=datetime.fromisoformat(expected["updated_at"].replace("Z","+00:00"))
        observed_updated=datetime.fromisoformat(native["updated_at"].replace("Z","+00:00"))
        require(updated.tzinfo is not None and observed_updated.tzinfo is not None and updated == observed_updated,
                "native ruleset version differs")
        require(updated.tzinfo is not None and (accepted-updated).total_seconds() >= 60, "protection stabilization bound")
        includes = expected["conditions"]["ref_name"]
        require(includes["exclude"] == [], "native protection exclusions")
        if name.startswith("main"):
            require(expected["target"] == "branch" and includes["include"] == ["refs/heads/main"], "main rule target")
            rule_types = {rule["type"] for rule in expected["rules"]}
            if name == "mainWriter":
                require(rule_types == {"creation", "update"} and sorted(expected["bypass_actors"], key=lambda a:a["actor_id"]) == [
                    {"actor_id":4882140,"actor_type":"Integration","bypass_mode":"always"},
                    {"actor_id":5064713,"actor_type":"Integration","bypass_mode":"always"}], "main writer authority")
            else:
                require(rule_types == {"deletion", "non_fast_forward"} and expected["bypass_actors"] == [], "main integrity authority")
        else:
            require(expected["bypass_actors"] == [], "legacy freeze has a bypass")
            if name == "legacyFence":
                require(expected["target"] == "branch" and includes["include"] == ["refs/heads/fsgg/v2/journal/**/*"]
                        and {r["type"] for r in expected["rules"]} == {"creation", "update"}, "legacy branch freeze")
            elif name == "legacyTagFence":
                require(expected["target"] == "tag" and includes["include"] == ["refs/tags/fsgg/v2/fleet-cutover/**/*"]
                        and {r["type"] for r in expected["rules"]} == {"creation"}, "legacy tag freeze")
            else:
                raise ValueError("unsupported source-owned protection control")
    require(set(controls) == {"mainWriter", "mainIntegrity", "legacyFence", "legacyTagFence"}, "native controls incomplete")
    native_main = api.get(f"repos/{REPOSITORY}/rules/branches/main")
    expected_rules = {("creation",24802693),("update",24802693),("deletion",24802698),("non_fast_forward",24802698)}
    require(isinstance(native_main,list) and len(native_main)==4 and {(r["type"],r["ruleset_id"]) for r in native_main} == expected_rules
            and all(r["ruleset_source_type"]=="Repository" and r["ruleset_source"]==REPOSITORY for r in native_main), "effective main protection differs")
    representatives=binding["representativeFrozenRefs"]
    require(any(ref.startswith(PREFIX+"operation/") for ref in representatives)
            and any(ref.startswith(PREFIX+"release/") for ref in representatives) and EPOCH in representatives, "frozen effective-rule coverage")
    for ref in representatives:
        require(ref in binding["frozenRefs"] and ref.startswith(PREFIX), "frozen effective-rule representative")
        from urllib.parse import quote
        rules = api.get(f"repos/{REPOSITORY}/rules/branches/{quote(ref.removeprefix('refs/heads/'),safe='')}")
        required = {("creation",controls["legacyFence"]["id"]),("update",controls["legacyFence"]["id"])}
        require(isinstance(rules,list) and required <= {(r["type"],r["ruleset_id"]) for r in rules}
                and all(r["ruleset_source_type"]=="Repository" and r["ruleset_source"]==REPOSITORY for r in rules), "effective legacy freeze differs")


def verify_native_import(api, manifest):
    head = api.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]
    require(head == manifest["desiredMain"], "native imported main differs")
    commit = api.get(f"repos/{REPOSITORY}/git/commits/{head}")
    require(commit.get("sha") == head and commit["tree"]["sha"] == manifest["desiredTree"], "native imported root tree differs")
    tree = api.get(f"repos/{REPOSITORY}/git/trees/{manifest['desiredTree']}?recursive=1")
    require(tree.get("sha") == manifest["desiredTree"] and tree.get("truncated") is False, "native tree incomplete")
    entries = {entry["path"]:entry for entry in tree["tree"]}
    for item in manifest["files"]:
        entry = entries.get(item["path"],{})
        require(entry.get("sha")==item["blob"] and entry.get("type")=="blob" and entry.get("mode")=="100644", "native imported file differs")
    for oid in set(manifest["snapshot"]["refs"].values()):
        compare = api.get(f"repos/{REPOSITORY}/compare/{oid}...{head}")
        require(compare.get("status") in ("ahead","identical") and compare.get("merge_base_commit",{}).get("sha")==oid and compare.get("base_commit",{}).get("sha")==oid,
                "native original head ancestry differs")
    from release_successor_journal import ProtectedReleaseJournal
    for ref, oid in manifest["snapshot"]["refs"].items():
        if ref.startswith(PREFIX+"release/"):
            reader = ProtectedReleaseJournal(api, ref, main_directory=True)
            reader.read()
            require(reader._observed is not None and reader._observed.head==oid, "native release logical head differs")
    require(api.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]==head, "native main moved during readback")
    return head


def observe_native_import(api, manifest):
    observed=None
    try:
        observed=api.get(f"repos/{REPOSITORY}/git/ref/heads/main")["object"]["sha"]
        verify_native_import(api,manifest)
        return {"observedMain":observed,"verified":True}
    except (OSError,RuntimeError,ValueError,KeyError,TypeError,subprocess.SubprocessError):
        return {"observedMain":observed,"verified":False}


def validate_execution_source(observer,binding,root,current):
    require(subprocess.run(["git","diff","--quiet","HEAD"],cwd=root).returncode==0, "apply checkout is modified")
    require(os.environ.get("GITHUB_REPOSITORY")=="FS-GG/.github" and os.environ.get("GITHUB_REF")=="refs/heads/main"
            and os.environ.get("GITHUB_SHA")==current and os.environ.get("GITHUB_RUN_ATTEMPT")=="1"
            and os.environ.get("GITHUB_EVENT_NAME")=="workflow_dispatch" and os.environ.get("GITHUB_ACTOR")==binding["operatorLogin"], "protected apply context differs")
    require(observer.get("repos/FS-GG/.github").get("id")==1269292704, "apply source repository differs")
    require(observer.get("repos/FS-GG/.github/git/ref/heads/main")["object"]["sha"]==current, "apply source main moved")
    run=observer.get(f"repos/FS-GG/.github/actions/runs/{os.environ['GITHUB_RUN_ID']}")
    require(run.get("id")==int(os.environ["GITHUB_RUN_ID"]) and run.get("head_sha")==current and run.get("head_branch")=="main"
            and run.get("repository",{}).get("id")==1269292704 and run.get("actor",{}).get("login")==binding["operatorLogin"]
            and run.get("triggering_actor",{}).get("login")==binding["operatorLogin"] and run.get("run_attempt")==1
            and run.get("event")=="workflow_dispatch" and run.get("path")==binding["executionWorkflowPath"]
            and run.get("status")=="in_progress", "native apply run differs")
    tool=observer.get(f"repos/FS-GG/.github/contents/scripts/authority-state-import.py?ref={current}")
    local=Path(__file__).read_bytes()
    blob=hashlib.sha1(b"blob "+str(len(local)).encode()+b"\0"+local).hexdigest()
    require(tool.get("sha")==blob, "native protected apply source bytes differ")


def prepare_frozen_proposal(observer, binding, output, credentials, *, original_objects=False):
    """Rebuild with the immutable reviewed generator; no native refs are written."""
    revision = binding["proposalGeneratorRevision"]
    snapshot = binding["frozenSnapshot"]
    require(isinstance(revision,str) and re.fullmatch(r"[0-9a-f]{40}",revision), "proposal generator revision")
    require(snapshot.get("repository")==REPOSITORY and snapshot.get("repositoryId")==1351660651
            and snapshot["refs"] == binding["frozenRefs"], "source-owned frozen snapshot differs")
    require(all(ref.startswith("refs/heads/") and (ref=="refs/heads/main" or ref.startswith(PREFIX))
                and re.fullmatch(r"[0-9a-f]{40}",oid) for ref,oid in snapshot["refs"].items()), "frozen fetch ref shape")
    require(not output.exists(), "existing hosted preparation; retain it without retry")
    with tempfile.TemporaryDirectory(prefix="authority-frozen-prepare-") as scratch:
        directory=Path(scratch);generator=directory/"generator";generator.mkdir()
        for name in ("authority-state-import.py","release_successor_journal.py","release_successor_execution.py"):
            selected=observer.get(f"repos/FS-GG/.github/contents/scripts/{name}?ref={revision}")
            require(selected.get("encoding")=="base64" and selected.get("type")=="file", "immutable generator source unavailable")
            raw=base64.b64decode(selected["content"])
            require(len(raw)<=2*1024*1024 and selected.get("sha")==hashlib.sha1(b"blob "+str(len(raw)).encode()+b"\0"+raw).hexdigest(), "immutable generator blob differs")
            (generator/name).write_bytes(raw)
        require(hashlib.sha256((generator/"authority-state-import.py").read_bytes()).hexdigest()==binding["proposalToolSha256"], "immutable proposal tool differs")
        mirror=directory/"fresh-frozen.git";mirror.mkdir()
        isolated_git(mirror,"init","--bare","--quiet")
        if original_objects:
            isolated_git(mirror,"fetch","--no-tags",AUTHORITY_REMOTE,*sorted(set(snapshot["refs"].values())),credentials=credentials)
            updates="".join(f"create refs/cleanup-backup/heads/{ref.removeprefix('refs/heads/')} {oid}\n" for ref,oid in sorted(snapshot["refs"].items()))
            isolated_git(mirror,"update-ref","--stdin",data=updates.encode())
        else:
            refspecs=[f"{ref}:refs/cleanup-backup/heads/{ref.removeprefix('refs/heads/')}" for ref in sorted(snapshot["refs"])]
            isolated_git(mirror,"fetch","--no-tags",AUTHORITY_REMOTE,*refspecs,credentials=credentials)
        frozen=directory/"snapshot.json";frozen.write_bytes(canonical(snapshot))
        output.mkdir(parents=True)
        response=subprocess.run([sys.executable,str(generator/"authority-state-import.py"),
                                 "--mirror",str(mirror),"--snapshot",str(frozen),"--output",str(output)],
                                env={"PATH":os.environ.get("PATH","/usr/bin:/bin"),"GIT_CONFIG_NOSYSTEM":"1","GIT_CONFIG_GLOBAL":"/dev/null"},
                                capture_output=True,timeout=180)
        (output/"preparation.stdout").write_bytes(response.stdout)
        (output/"preparation.stderr").write_bytes(response.stderr)
        (output/"preparation-exit-code.txt").write_text(str(response.returncode)+"\n")
        # Pending originals must remain pending; the builder's exit 2 is not an effect retry.
        require(response.returncode in (0,2), "immutable frozen proposal refused")
    return output/"import-manifest.json"


def verify_once(manifest, observe, output):
    destination=output/"verify-observation.json"
    require(not destination.exists(), "existing verification; retain original observation")
    output.mkdir(parents=True,exist_ok=True)
    try:
        readback=observe(manifest)
    except (OSError,RuntimeError,ValueError,KeyError,TypeError,subprocess.SubprocessError):
        readback={"observedMain":None,"verified":False}
    result={"mode":"read-only-verify-admitted","desiredMain":manifest["desiredMain"],
            "observedMain":readback.get("observedMain"),"importVerified":readback.get("verified") is True,
            "pushAttempts":0,"unresolved":manifest["unresolved"],"reconciliationComplete":False}
    result["result"]="import-verified-original-reconciliation-unchanged" if result["importVerified"] else "import-verification-incomplete-or-main-moved"
    destination.write_bytes(canonical(result))
    return result


def apply_admitted(manifest_path, output, *, prepare=False, verify=False):
    binding = ADMITTED_IMPORT
    require(isinstance(binding,dict), "native apply is disabled; reviewed source-owned admission is absent")
    require(not (output/"apply-intent.json").exists() and not (output/"apply-observation.json").exists(), "existing apply attempt; no retry")
    def read_manifest(path):
        raw = path.read_bytes();manifest = load(raw)
        require(raw==canonical(manifest), "admitted manifest is not canonical")
        require(hashlib.sha256(raw).hexdigest()==binding["manifestSha256"] and manifest["toolSha256"]==binding["proposalToolSha256"], "admitted frozen proposal differs")
        require(manifest["snapshot"]["refs"]==binding["frozenRefs"], "admitted frozen ref vector differs")
        return manifest
    require(not prepare or manifest_path is None, "hosted preparation takes no caller manifest")
    require(not verify or prepare, "read-only verification rebuilds the original admitted manifest")
    manifest = None if prepare else read_manifest(manifest_path)
    published=binding["publishedCli"]
    require(set(published)=={"packageId","version","archiveSha256"} and isinstance(published["packageId"],str)
            and published["packageId"]=="FS.GG.Coordination.Cli" and re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9.-]+)?",published["version"])
            and re.fullmatch(r"[0-9a-f]{64}",published["archiveSha256"]), "admitted published CLI package identity")
    from release_successor_provider import GitHubAPI
    observer = GitHubAPI(os.environ["GH_TOKEN"])
    root = Path(__file__).resolve().parents[1]
    current = subprocess.check_output(["git","rev-parse","HEAD"],cwd=root).decode().strip()
    validate_execution_source(observer,binding,root,current)
    for label in ("admission","publication"):
        selected=binding[label]
        evidence=observer.get(f"repos/{selected['repository']}/actions/runs/{selected['runId']}")
        require(evidence.get("id")==selected["runId"] and evidence.get("head_sha")==selected["sourceSha"]
                and evidence.get("path")==selected["workflowPath"] and evidence.get("head_branch")=="main"
                and evidence.get("repository",{}).get("id")==selected["repositoryId"]
                and evidence.get("actor",{}).get("login")==binding["operatorLogin"] and evidence.get("run_attempt")==1
                and evidence.get("status")=="completed" and evidence.get("conclusion")=="success", "native admission/publication differs")
        artifact=observer.get(f"repos/{selected['repository']}/actions/artifacts/{selected['artifactId']}")
        require(artifact.get("id")==selected["artifactId"] and artifact.get("workflow_run",{}).get("id")==selected["runId"]
                and artifact.get("workflow_run",{}).get("head_sha")==selected["sourceSha"]
                and artifact.get("digest")=="sha256:"+selected["artifactSha256"] and artifact.get("expired") is False, "native admitted artifact differs")
    # Reuse the already qualified App JWT/request seam; never accept an ambient write PAT.
    import importlib.util
    spec=importlib.util.spec_from_file_location("ordinary_app_readback",root/"tools/v2-ci-ordinary-app-readback.py")
    seam=importlib.util.module_from_spec(spec);spec.loader.exec_module(seam)
    jwt=seam.app_jwt(5064713,os.environ["FSGG_APP_PRIVATE_KEY"])
    app=seam.request("/app",jwt);installation=seam.request(f"/repos/{REPOSITORY}/installation",jwt)
    permissions={"contents":"write","metadata":"read"}
    require(app.get("id")==5064713 and app.get("name")=="FS-GG Ordinary V2 Settlement" and app.get("owner",{}).get("login")=="FS-GG"
            and app.get("permissions")==permissions and app.get("events")==[], "authorized App identity differs")
    require(installation.get("id")==164553252 and installation.get("app_id")==5064713 and installation.get("account",{}).get("login")=="FS-GG"
            and installation.get("repository_selection")=="selected" and installation.get("permissions")==permissions
            and installation.get("events")==[] and installation.get("suspended_at") is None, "authorized installation differs")
    metadata=seam.request("/app/installations/164553252/access_tokens",jwt,{"permissions":{"metadata":"read"}})
    require(metadata.get("permissions")=={"metadata":"read"} and metadata.get("token"), "metadata-only mint scope")
    visible=GitHubAPI(metadata["token"]).get("installation/repositories?per_page=100")
    require(visible.get("total_count")==1 and [(r.get("id"),r.get("full_name")) for r in visible["repositories"]]==[(1351660651,REPOSITORY)], "unrestricted installation repository scope differs")
    contents_permission="read" if verify else "write"
    token_response=seam.request("/app/installations/164553252/access_tokens",jwt,{"repository_ids":[1351660651],"permissions":{"contents":contents_permission}})
    require(token_response.get("permissions")=={"contents":contents_permission,"metadata":"read"} and isinstance(token_response.get("token"),str) and token_response["token"], "minted native scope differs")
    writer=GitHubAPI(token_response["token"]);visible=writer.get("installation/repositories?per_page=100")
    require(visible.get("total_count")==1 and [(r.get("id"),r.get("full_name")) for r in visible["repositories"]]==[(1351660651,REPOSITORY)], "writer installation repository scope differs")
    with tempfile.TemporaryDirectory(prefix="authority-import-askpass-") as scratch:
        askpass=Path(scratch)/"askpass.sh"
        askpass.write_text('#!/bin/sh\ncase "$1" in *Username*) printf "%s\\n" x-access-token;; *) printf "%s\\n" "$FSGG_GIT_PASSWORD";; esac\n')
        askpass.chmod(0o700)
        credentials={"GIT_ASKPASS":str(askpass),"FSGG_GIT_PASSWORD":token_response["token"]}
        directory=Path(scratch)
        def refs():
            values=isolated_git(directory,"ls-remote","--refs",AUTHORITY_REMOTE,"refs/heads/main","refs/heads/fsgg/v2/journal/*",credentials=credentials).stdout.decode().splitlines()
            return {ref:oid for oid,ref in (line.split() for line in values)}
        def guard(expected):
            validate_execution_source(observer,binding,root,current)
            validate_native_guard(writer,refs,expected,binding)
        if prepare:
            expected=binding["frozenRefs"]["refs/heads/main"]
            if not verify:guard(expected)
            prepared=prepare_frozen_proposal(observer,binding,output,credentials,original_objects=verify)
            validate_execution_source(observer,binding,root,current)
            if not verify:guard(expected)
            manifest=read_manifest(prepared)
        if verify:
            def verification_observation(selected):
                result=observe_native_import(writer,selected)
                try:validate_execution_source(observer,binding,root,current)
                except (OSError,RuntimeError,ValueError,KeyError,TypeError,subprocess.SubprocessError):result["verified"]=False
                return result
            return verify_once(manifest,verification_observation,output)
        def fetch(stage,oids):
            isolated_git(stage,"fetch","--no-tags",AUTHORITY_REMOTE,*oids,credentials=credentials)
        def push(stage,expected,desired):
            response=isolated_git(stage,"push",f"--force-with-lease=refs/heads/main:{expected}",AUTHORITY_REMOTE,
                                  f"{desired}:refs/heads/main",credentials=credentials,allow_failure=True)
            return response.returncode==0
        return apply_once(manifest,guard,fetch,push,lambda m:observe_native_import(writer,m),output)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--mirror", type=Path)
    parser.add_argument("--snapshot", type=Path)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--prepare-and-apply", action="store_true")
    parser.add_argument("--verify-admitted", action="store_true")
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        if args.prepare_and_apply or args.verify_admitted:
            require(not (args.prepare_and_apply and args.verify_admitted) and not args.apply and args.manifest is None and args.mirror is None and args.snapshot is None,
                    "hosted invocation takes only source-owned proposal inputs")
            result = apply_admitted(None,args.output,prepare=True,verify=args.verify_admitted)
            print(result["result"])
            return 0 if result["importVerified" if args.verify_admitted else "importApplied"] else 2
        if args.apply:
            require(args.manifest is not None and args.mirror is None and args.snapshot is None, "apply requires only the exact admitted manifest")
            result = apply_admitted(args.manifest,args.output)
            print(result["result"])
            return 0 if result["importApplied"] else 2
        require(args.mirror is not None and args.snapshot is not None and args.manifest is None, "dry-run requires mirror and frozen snapshot")
        result = propose(args.mirror.resolve(), load(args.snapshot.read_bytes()), args.output)
        print("dry-run proposal retained; adoption blocked; reconciliation=" + result["reconciliation"])
        return 2 if result["unresolved"] else 0
    except (OSError,RuntimeError,ValueError, KeyError, TypeError, subprocess.SubprocessError) as error:
        print("import refused: " + str(error))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

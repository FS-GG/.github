#!/usr/bin/env python3
"""Frozen Authority import: dry-run by default, native apply source-disabled."""
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



# Deliberately disabled. Only a reviewed source change may bind the exact frozen
# manifest, proposal tool, published successor and native admission identities.
ADMITTED_IMPORT = None
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
        fields = ("id", "name", "target", "enforcement", "updated_at", "conditions", "rules", "bypass_actors")
        require(all(field in native and field in expected and native[field] == expected[field] for field in fields), "native ruleset identity/visibility differs")
        require(expected["enforcement"] == "active", "native protection is not active")
        updated=datetime.fromisoformat(expected["updated_at"].replace("Z","+00:00"))
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


def apply_admitted(manifest_path, output):
    binding = ADMITTED_IMPORT
    require(isinstance(binding,dict), "native apply is disabled; reviewed source-owned admission is absent")
    require(not (output/"apply-intent.json").exists() and not (output/"apply-observation.json").exists(), "existing apply attempt; no retry")
    raw = manifest_path.read_bytes();manifest = load(raw)
    require(raw==canonical(manifest), "admitted manifest is not canonical")
    require(hashlib.sha256(raw).hexdigest()==binding["manifestSha256"] and manifest["toolSha256"]==binding["proposalToolSha256"], "admitted frozen proposal differs")
    require(manifest["snapshot"]["refs"]==binding["frozenRefs"], "admitted frozen ref vector differs")
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
    token_response=seam.request("/app/installations/164553252/access_tokens",jwt,{"repository_ids":[1351660651],"permissions":{"contents":"write"}})
    require(token_response.get("permissions")==permissions and isinstance(token_response.get("token"),str) and token_response["token"], "minted writer scope differs")
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
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
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

"""Separate current publication and retained historical recovery admission."""

from __future__ import annotations

import base64
import datetime
import hashlib
import json
import os
import pathlib
import re

from release_successor_provider import GitHubAPI, MetadataRequestBudget
from new_sdd_workspace_successor_execution import effects, CURRENT_020, CURRENT_021, CURRENT_022, HISTORICAL_013

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-new-sdd-workspace.yml"
OPERATOR = "EHotwagner"


class Refused(RuntimeError):
    pass


# A deliberately conservative, source-owned epoch: the complete accepted repository
# complement, not a caller-selected list of apparently relevant build inputs. Every
# script/import/config/workflow addition revokes the report-only exception. The
# policy module seals itself against its executing bytes to avoid a self-hash cycle.
REPORT_DIRECTORY = "docs/reports/progressReports"
POLICY_PATH = "scripts/new_sdd_workspace_successor_admission.py"
KNOWN_GRAPH_SEAL = "ff53b87e5c8c311cccbc378480eaae03f33a725113296df2dff9130f67fce347"
TREE_ENTRY_LIMIT = 10000
TREE_JSON_LIMIT = 2 * 1024 * 1024
REPORT_BYTE_LIMIT = 1024 * 1024
REPORT_ADDITION_LIMIT = 32
REPORT_TOTAL_LIMIT = 8 * 1024 * 1024
SHA = re.compile(r"[0-9a-f]{40}")
REPORT_NAME = re.compile(r"([0-9]{4}-[0-9]{2}-[0-9]{2}-[0-9]{6})Z-[a-z0-9]+(?:-[a-z0-9]+)*\.md")
_POLICY_BYTES = pathlib.Path(__file__).read_bytes()
_POLICY_BLOB = hashlib.sha1(b"blob " + str(len(_POLICY_BYTES)).encode() + b"\0" + _POLICY_BYTES).hexdigest()


def _require(ok, detail):
    if not ok:
        raise Refused(detail)


def _report_path(path):
    prefix = REPORT_DIRECTORY + "/"
    if not path.startswith(prefix):
        return False
    name = path[len(prefix):]
    match = REPORT_NAME.fullmatch(name)
    if match is None:
        return False
    try:
        datetime.datetime.strptime(match[1], "%Y-%m-%d-%H%M%S")
    except ValueError:
        return False
    return True


def graph_seal(tree):
    """All non-report leaves and all directory membership/modes, including absence.

    Directory object IDs encode their children; omit those IDs only because the
    complete recursive entries independently prove every child. No subtree is
    skipped. The report directory must already exist at both revisions.
    """
    rows = []
    for path, row in sorted(tree.items()):
        if _report_path(path) and row["mode"] == "100644" and row["type"] == "blob":
            continue
        rows.append([path, row["mode"], row["type"],
                     "policy" if path == POLICY_PATH else "" if row["type"] == "tree" else row["sha"]])
    return hashlib.sha256(json.dumps(rows, separators=(",", ":"), ensure_ascii=True).encode()).hexdigest()


class SourceCompatibility:
    """Finite immutable-object cache; mutable main/run/protection is never cached.

    Reuse the maintained bounded metadata transport (2MiB response/256 opens),
    without changing historical publisher transports or claiming a new profile.
    A new main does not renew this counter or the caller's original deadline.
    """
    def __init__(self, api):
        self.api = (GitHubAPI(api.token, metadata_budget=MetadataRequestBudget())
                    if isinstance(api, GitHubAPI) and api.metadata_budget is None else api)
        self.trees = {}
        self.blobs = {}

    def tree(self, source):
        _require(isinstance(source, str) and SHA.fullmatch(source), "Wizard source SHA malformed")
        if source in self.trees:
            return self.trees[source]
        _require(len(self.trees) < 6, "Wizard immutable source cache exhausted")
        commit = self.api.get(f"repos/{REPOSITORY}/git/commits/{source}")
        _require(commit.get("sha") == source, "Wizard source commit identity differs")
        tree_sha = commit.get("tree", {}).get("sha")
        _require(isinstance(tree_sha, str) and SHA.fullmatch(tree_sha), "Wizard source tree identity missing")
        result = self.api.get(f"repos/{REPOSITORY}/git/trees/{tree_sha}?recursive=1")
        entries = result.get("tree")
        _require(result.get("sha") == tree_sha and result.get("truncated") is False
                 and isinstance(entries, list) and len(entries) <= TREE_ENTRY_LIMIT,
                 "Wizard source tree incomplete or excessive")
        _require(len(json.dumps(result, ensure_ascii=True).encode()) <= TREE_JSON_LIMIT,
                 "Wizard source tree metadata excessive")
        tree = {}
        for row in entries:
            _require(isinstance(row, dict), "Wizard source tree row malformed")
            path, mode, kind, sha = (row.get(key) for key in ("path", "mode", "type", "sha"))
            _require(isinstance(path, str) and 0 < len(path) <= 1024 and path.isascii()
                     and all(part not in {"", ".", ".."} for part in path.split("/"))
                     and not any(ord(c) < 32 or c in "\\" for c in path) and path not in tree,
                     "Wizard source tree path unsafe or duplicate")
            _require((mode, kind) in {("040000", "tree"), ("100644", "blob"), ("100755", "blob")}
                     and isinstance(sha, str) and SHA.fullmatch(sha), "Wizard source tree mode or object unsafe")
            tree[path] = {"mode": mode, "type": kind, "sha": sha, "size": row.get("size")}
        for path in tree:
            parent = path.rpartition("/")[0]
            _require(not parent or tree.get(parent, {}).get("type") == "tree",
                     "Wizard source tree parent missing")
        self.trees[source] = tree
        return tree

    def report(self, row):
        sha, size = row["sha"], row["size"]
        _require(type(size) is int and 0 <= size <= REPORT_BYTE_LIMIT, "Wizard report size excessive")
        if sha not in self.blobs:
            _require(len(self.blobs) < REPORT_ADDITION_LIMIT, "Wizard report object cache exhausted")
            blob = self.api.get(f"repos/{REPOSITORY}/git/blobs/{sha}")
            _require(blob.get("sha") == sha and blob.get("encoding") == "base64"
                     and blob.get("size") == size and isinstance(blob.get("content"), str)
                     and len(blob["content"]) <= 2 * REPORT_BYTE_LIMIT,
                     "Wizard report object differs")
            try:
                raw = base64.b64decode(blob["content"].replace("\n", ""), validate=True)
            except ValueError:
                raise Refused("Wizard report object encoding malformed") from None
            _require(len(raw) == size and hashlib.sha1(b"blob " + str(size).encode() + b"\0" + raw).hexdigest() == sha,
                     "Wizard report object hash differs")
            self.blobs[sha] = size  # Only a validated immutable digest/size, not report content.
        _require(self.blobs[sha] == size, "Wizard report object size differs")
        return size

    def compatible(self, older, newer):
        if older == newer:
            _require(isinstance(older, str) and SHA.fullmatch(older), "Wizard source SHA malformed")
            return True  # Unknown graphs retain the original exact-source rule.
        old, new = self.tree(older), self.tree(newer)
        for tree in (old, new):
            _require(tree.get(REPORT_DIRECTORY, {}).get("mode") == "040000"
                     and tree.get(POLICY_PATH, {}).get("sha") == _POLICY_BLOB
                     and graph_seal(tree) == KNOWN_GRAPH_SEAL,
                     "Wizard determining source graph differs")
        for path, row in old.items():
            observed = new.get(path)
            _require(observed is not None and (row["mode"], row["type"]) == (observed["mode"], observed["type"])
                     and (row["type"] == "tree" or row["sha"] == observed["sha"]),
                     "Wizard existing source changed or removed")
        additions = [row for path, row in new.items() if path not in old
                     and _report_path(path) and row["mode"] == "100644" and row["type"] == "blob"]
        _require(len(additions) == len(new) - len(old) and 0 < len(additions) <= REPORT_ADDITION_LIMIT,
                 "Wizard source addition is not an admitted report")
        _require(sum(self.report(row) for row in additions) <= REPORT_TOTAL_LIMIT,
                 "Wizard report additions exceed total bound")
        return True

    def current(self, candidate, publisher):
        head = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
        current = head.get("object", {}).get("sha")
        _require(isinstance(current, str) and SHA.fullmatch(current), "Wizard current main missing")
        self.compatible(candidate, publisher)
        self.compatible(publisher, current)
        self.compatible(candidate, current)
        _require(self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main").get("object", {}).get("sha") == current,
                 "Wizard current main changed during source proof")
        return True


def candidate_guard():
    """Read-only, fixed candidate entrypoint; no argv-selected source or closure."""
    source = os.environ["GITHUB_SHA"]
    _require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY
             and os.environ.get("GITHUB_REF") == "refs/heads/main"
             and os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
             and os.environ.get("GITHUB_RUN_ATTEMPT") == "1"
             and os.environ.get("GITHUB_ACTOR") == OPERATOR, "Wizard candidate caller differs")
    api = GitHubAPI(os.environ["GITHUB_TOKEN"], metadata_budget=MetadataRequestBudget())
    repository = api.get(f"repos/{REPOSITORY}")
    run_id = int(os.environ["GITHUB_RUN_ID"])
    run = api.get(f"repos/{REPOSITORY}/actions/runs/{run_id}")
    _require(repository.get("id") == REPOSITORY_ID and repository.get("full_name") == REPOSITORY
             and run.get("id") == run_id and run.get("repository", {}).get("id") == REPOSITORY_ID
             and run.get("path") == ".github/workflows/release-new-sdd-workspace-successor-candidate.yml"
             and run.get("head_sha") == source and run.get("head_branch") == "main"
             and run.get("event") == "workflow_dispatch" and run.get("run_attempt") == 1
             and run.get("actor", {}).get("login") == OPERATOR and run.get("status") == "in_progress",
             "Wizard candidate native identity differs")
    SourceCompatibility(api).current(source, source)


class WizardAdmission:
    def __init__(self, api: GitHubAPI, manifest: dict, publisher_sha: str, run_id: int, actor: str, ref: str,
                 *, release_binding=CURRENT_022):
        self.api = api
        self.publisher_sha = publisher_sha
        self.candidate_sha = manifest.get("sourceSha")
        self.source_compatibility = SourceCompatibility(api)
        self.run_id = run_id
        self.release_binding = release_binding
        self.content_id, ordered = effects(manifest, binding=release_binding)
        self.requests = {effect.identity: effect.request_digest for effect in ordered}
        self.requests["journal"] = self.content_id
        if actor != OPERATOR or ref != "refs/heads/main":
            raise Refused("Wizard publisher caller or branch differs")

    def authorize(self, content_id: str, effect: str, action: str, request_digest: str) -> bool:
        if (
            self.release_binding is not CURRENT_022
            or content_id != self.content_id
            or self.requests.get(effect) != request_digest
            or action not in {"intent", "dispatch", "settle"}
        ):
            return False
        repository = self.api.get(f"repos/{REPOSITORY}")
        if repository.get("id") != REPOSITORY_ID or repository.get("full_name") != REPOSITORY:
            return False
        run = self.api.get(f"repos/{REPOSITORY}/actions/runs/{self.run_id}")
        if (
            run.get("repository", {}).get("id") != REPOSITORY_ID
            or run.get("path") != WORKFLOW
            or run.get("event") != "workflow_dispatch"
            or run.get("head_branch") != "main"
            or run.get("head_sha") != self.publisher_sha
            or run.get("run_attempt") != 1
            or run.get("actor", {}).get("login") != OPERATOR
            or run.get("status") != "in_progress"
        ):
            return False
        try:
            return self.source_compatibility.current(self.candidate_sha, self.publisher_sha)
        except Refused:
            return False

    def authorize_recovery(self, content_id, action, request_digest, binding, mode):
        """Wizard-only selected native recovery; does not change ordinary admission."""
        if (self.release_binding is not HISTORICAL_013
                or content_id != self.content_id or self.requests.get("promote") != request_digest
                or action not in {"dispatch", "settle"} or mode not in {"diagnostic", "complete"}
                or binding.get("heldSource") != self.publisher_sha):
            return False
        from new_sdd_workspace_promote_recovery import native_context
        native_context(self.api, binding, mode, self.run_id)
        repository = self.api.get(f"repos/{REPOSITORY}")
        return repository.get("id") == REPOSITORY_ID and repository.get("full_name") == REPOSITORY


if __name__ == "__main__":
    candidate_guard()

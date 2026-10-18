"""Fresh per-effect native Actions and main-ref admission for Host 0.6.0."""

from __future__ import annotations

import importlib.util
import pathlib
import re

from release_successor_execution import Refused
from release_successor_provider import GitHubAPI
from telemetry_host_successor_execution import effects

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-telemetry-host-successor-publish.yml"
OPERATOR = "EHotwagner"


class HostAdmission:
    def __init__(self, api: GitHubAPI, manifest: dict, publisher_sha: str, run_id: int, actor: str, ref: str):
        self.api = api
        self.publisher_sha = publisher_sha
        self.run_id = run_id
        self.content_id, ordered = effects(manifest)
        self.requests = {effect.identity: effect.request_digest for effect in ordered}
        self.requests["journal"] = self.content_id
        if actor != OPERATOR or ref != "refs/heads/main":
            raise Refused("Host publisher caller or branch differs")

    def authorize(self, content_id: str, effect: str, action: str, request_digest: str) -> bool:
        if (
            content_id != self.content_id
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
        head = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
        return head.get("object", {}).get("sha") == self.publisher_sha


# One retained, genuine first-publication failure. This is not a general source waiver.
FAILED_ORIGINAL = {
    "runId": 39137010941, "candidateRunId": 39135954294, "artifactId": 12140420062,
    "sourceSha": "54335971452d2dfadb04e7704774ae2e48718e54",
    "archiveSha256": "1bd7d77b8ebb6580aa23f8391055ba15bf64afb04a361020d148ca1e9f9ee5b3",
    "contentId": "sha256:082bfb0bd5a94d81e98575972a9e4e0fc71ad2e3daffe5dc9dce4c0f590a031b",
    "packageSha256": "ac77ba38f6d4974586d84d75f928fb0b5edbf4dcd747266ac5453f00d3676af6",
}
REPAIR_PATHS = frozenset({
    "scripts/telemetry-host-successor-publish.py",
    "scripts/telemetry_host_successor_admission.py",
    "tests/telemetry-host-successor/run.py",
    ".github/workflows/release-telemetry-host-successor-publish.yml",
    "docs/coordination/telemetry-host-release.md",
    "registry/driver-skill-manifest.json",
})
ATLAS_ROOTS = ("docs/atlas/", ".agents/skills/atlas/assets/seed/", ".claude/skills/atlas/assets/seed/")


def _require(ok, message):
    if not ok:
        raise Refused(message)


def ordinary_route():
    """Reuse the existing protected-main predicate and bounded directory classifier."""
    spec = importlib.util.spec_from_file_location("host_ordinary_route", pathlib.Path(__file__).with_name("release-successor-publish.py"))
    _require(spec is not None and spec.loader is not None, "ordinary route source unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def repository_entries(api, source):
    commit = api.get(f"repos/{REPOSITORY}/git/commits/{source}")
    _require(commit.get("sha") == source, "repair commit identity differs")
    tree = commit.get("tree", {}).get("sha")
    _require(isinstance(tree, str) and re.fullmatch(r"[0-9a-f]{40}", tree), "repair tree identity differs")
    value = api.get(f"repos/{REPOSITORY}/git/trees/{tree}?recursive=1")
    entries = value.get("tree")
    _require(value.get("sha") == tree and value.get("truncated") is False
             and isinstance(entries, list) and len(entries) <= 20000, "repair tree incomplete")
    result = {}
    for entry in entries:
        path = entry.get("path")
        _require(isinstance(path, str) and path and not path.startswith("/")
                 and all(part not in {"", ".", ".."} for part in path.split("/"))
                 and path not in result and entry.get("type") in {"tree", "blob", "commit"}
                 and isinstance(entry.get("sha"), str) and re.fullmatch(r"[0-9a-f]{40}", entry["sha"]),
                 "repair tree entry malformed")
        result[path] = (entry.get("mode"), entry["type"], entry["sha"])
    return {path: value for path, value in result.items() if value[1] != "tree"}


def validate_failed_original(api, manifest, publisher_sha, candidate_run, artifact_id, archive_sha):
    fixed = FAILED_ORIGINAL
    content_id, _ = effects(manifest)
    _require((candidate_run, artifact_id, archive_sha, manifest.get("sourceSha"), content_id,
              manifest.get("archiveSha256")) == (fixed["candidateRunId"], fixed["artifactId"],
              fixed["archiveSha256"], fixed["sourceSha"], fixed["contentId"], fixed["packageSha256"]),
             "failed-original candidate tuple differs")
    original = api.get(f"repos/{REPOSITORY}/actions/runs/{fixed['runId']}")
    _require(original.get("id") == fixed["runId"] and original.get("repository", {}).get("id") == REPOSITORY_ID
             and original.get("path") == WORKFLOW and original.get("event") == "workflow_dispatch"
             and original.get("head_branch") == "main" and original.get("head_sha") == fixed["sourceSha"]
             and original.get("run_attempt") == 1 and original.get("actor", {}).get("login") == OPERATOR
             and original.get("status") == "completed" and original.get("conclusion") == "failure",
             "genuine failed original differs")
    comparison = api.get(f"repos/{REPOSITORY}/compare/{fixed['sourceSha']}...{publisher_sha}")
    _require(comparison.get("status") == "ahead" and type(comparison.get("ahead_by")) is int
             and comparison["ahead_by"] > 0 and comparison.get("behind_by") == 0
             and comparison.get("base_commit", {}).get("sha") == fixed["sourceSha"]
             and comparison.get("merge_base_commit", {}).get("sha") == fixed["sourceSha"],
             "failed-original publisher ancestry differs")
    before, after = repository_entries(api, fixed["sourceSha"]), repository_entries(api, publisher_sha)
    changes = {path for path in before.keys() | after.keys() if before.get(path) != after.get(path)}
    _require(changes and len(changes) <= 32, "repair change bound differs")
    for path in changes:
        atlas = next((prefix for prefix in ATLAS_ROOTS if path.startswith(prefix)), None)
        # Existing immutable records cannot be changed or removed. New records and catalogs only.
        allowed_atlas = atlas is not None and (path == atlas + "catalog.json" or
            (re.fullmatch(r"records/[0-9a-f]{64}\.json", path[len(atlas):]) and path not in before))
        _require(path in REPAIR_PATHS or allowed_atlas, "publisher repair changes unadmitted source: " + path)
        _require(path in after and after[path][:2] == ("100644", "blob"), "repair path type differs")
    _require("scripts/telemetry-host-successor-publish.py" in changes, "publisher repair missing")

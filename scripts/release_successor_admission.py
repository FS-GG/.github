"""Fresh single-operator admission for the protected release successor.

Every journal and provider effect rereads the source branch and native Actions
run. The exact candidate's content ID and request digests are fixed at setup.
This port never derives authority from workflow input alone.
"""

from __future__ import annotations

from typing import Protocol

from release_successor_execution import ordered_effects

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-successor-publish.yml"
OPERATOR = "EHotwagner"


class Refused(RuntimeError):
    pass


class GitAPI(Protocol):
    def get(self, path: str) -> dict: ...


class SingleOperatorAdmission:
    def __init__(self, api: GitAPI, manifest: dict, source_sha: str, run_id: int, actor: str, ref: str):
        self.api = api
        self.source_sha = source_sha
        self.run_id = run_id
        self.content_id = manifest.get("contentId")
        self.requests: dict[str, str] = {effect.identity: effect.request_digest for effect in ordered_effects(manifest)}
        self.requests["journal"] = self.content_id
        if actor != OPERATOR or ref != "refs/heads/main" or manifest["descriptor"].get("sourceSha") != source_sha:
            raise Refused("publisher caller, branch or source differs from the approved release intent")

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
            or run.get("head_sha") != self.source_sha
            or run.get("run_attempt") != 1
            or run.get("actor", {}).get("login") != OPERATOR
            or run.get("status") != "in_progress"
        ):
            return False
        head = self.api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
        return head.get("object", {}).get("sha") == self.source_sha

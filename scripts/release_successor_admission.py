"""Fresh single-operator admission for the protected release successor.

Every journal and provider effect rereads the source branch and native Actions
run. The exact candidate's content ID and request digests are fixed at setup.
This port never derives authority from workflow input alone.
"""

from __future__ import annotations

from typing import Protocol

import release_source_closure
from release_successor_execution import ordered_effects

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-successor-publish.yml"
OPERATOR = "EHotwagner"
PUBLISHER_CLOSURE = "coherent-set-publisher"


class Refused(RuntimeError):
    pass


class GitAPI(Protocol):
    def get(self, path: str) -> dict: ...


class SingleOperatorAdmission:
    def __init__(self, api: GitAPI, manifest: dict, publisher_sha: str, run_id: int, actor: str, ref: str):
        self.api = api
        self.publisher_sha = publisher_sha
        self.run_id = run_id
        self.content_id = manifest.get("contentId")
        self.requests: dict[str, str] = {effect.identity: effect.request_digest for effect in ordered_effects(manifest)}
        self.requests["journal"] = self.content_id
        if actor != OPERATOR or ref != "refs/heads/main" or not isinstance(manifest["descriptor"].get("sourceSha"), str):
            raise Refused("publisher caller, branch or source differs from the approved release intent")

    def authorize(self, content_id: str, effect: str, action: str, request_digest: str) -> bool:
        if (
            content_id != self.content_id
            or self.requests.get(effect) != request_digest
            or action not in {"intent", "dispatch", "settle"}
        ):
            return False
        return publisher_context_authorized(self.api, self.publisher_sha, self.run_id)


def publisher_context_authorized(api, publisher_sha, run_id, *, hosted_install=False):
    """Share the maintained native run/current-main predicate without a fake manifest."""
    repository = api.get(f"repos/{REPOSITORY}")
    if repository.get("id") != REPOSITORY_ID or repository.get("full_name") != REPOSITORY:
        return False
    run = api.get(f"repos/{REPOSITORY}/actions/runs/{run_id}")
    if (
        run.get("repository", {}).get("id") != REPOSITORY_ID
        or run.get("path") != WORKFLOW
        or run.get("event") != "workflow_dispatch"
        or run.get("head_branch") != "main"
        or run.get("head_sha") != publisher_sha
        or run.get("run_attempt") != 1
        or run.get("actor", {}).get("login") != OPERATOR
        or run.get("status") != "in_progress"
    ):
        return False
    if hosted_install and (
        type(run.get("id")) is not int or run.get("id") != run_id or type(run.get("run_attempt")) is not int
        or run.get("triggering_actor", {}).get("login") != OPERATOR
        or run.get("repository", {}).get("full_name") != REPOSITORY
    ):
        return False
    head = api.get(f"repos/{REPOSITORY}/git/ref/heads/main")
    live = head.get("object", {}).get("sha")
    if hosted_install:
        return live == publisher_sha and head.get("object", {}).get("type") == "commit"
    if live == publisher_sha:
        return True
    # A merge after dispatch no longer revokes the running publisher by itself (#5107): live
    # main must descend from the pinned publisher source with its release and controller
    # closure unchanged. The installer path above keeps the exact-head rule.
    return isinstance(live, str) and release_source_closure.admits(api, REPOSITORY, PUBLISHER_CLOSURE, publisher_sha, live)

"""Fresh per-effect native Actions and main-ref admission for Host 0.9.0."""

from __future__ import annotations

import importlib.util
import pathlib

import release_source_closure
from release_successor_execution import Refused
from release_successor_provider import GitHubAPI
from telemetry_host_successor_execution import effects

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-telemetry-host-successor-publish.yml"
OPERATOR = "EHotwagner"
PUBLISHER_CLOSURE = "telemetry-host-publisher"
CANDIDATE_CLOSURE = "telemetry-host"


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
        live = head.get("object", {}).get("sha")
        if live == self.publisher_sha:
            return True
        # A later main is admitted only as a descendant with an unchanged Host release and
        # publisher closure (#5107); any closure change or non-ancestry still revokes.
        return isinstance(live, str) and release_source_closure.admits(
            self.api, REPOSITORY, PUBLISHER_CLOSURE, self.publisher_sha, live)


def ordinary_route():
    """Reuse the existing protected-main predicate and bounded directory classifier."""
    spec = importlib.util.spec_from_file_location("host_ordinary_route", pathlib.Path(__file__).with_name("release-successor-publish.py"))
    if spec is None or spec.loader is None:
        raise Refused("ordinary route source unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

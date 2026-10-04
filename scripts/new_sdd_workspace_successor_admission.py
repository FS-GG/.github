"""Fresh per-effect native Actions and main-ref admission for Wizard 0.12.0."""

from __future__ import annotations

from release_successor_provider import GitHubAPI
from new_sdd_workspace_successor_execution import effects

REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
WORKFLOW = ".github/workflows/release-new-sdd-workspace.yml"
OPERATOR = "EHotwagner"


class Refused(RuntimeError):
    pass


class WizardAdmission:
    def __init__(self, api: GitHubAPI, manifest: dict, publisher_sha: str, run_id: int, actor: str, ref: str):
        self.api = api
        self.publisher_sha = publisher_sha
        self.run_id = run_id
        self.content_id, ordered = effects(manifest)
        self.requests = {effect.identity: effect.request_digest for effect in ordered}
        self.requests["journal"] = self.content_id
        if actor != OPERATOR or ref != "refs/heads/main":
            raise Refused("Wizard publisher caller or branch differs")

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

    def authorize_recovery(self, content_id, action, request_digest, binding, mode):
        """Wizard-only selected native recovery; does not change ordinary admission."""
        if (content_id != self.content_id or self.requests.get("promote") != request_digest
                or action not in {"dispatch", "settle"} or mode not in {"diagnostic", "complete"}
                or binding.get("heldSource") != self.publisher_sha):
            return False
        from new_sdd_workspace_promote_recovery import native_context
        native_context(self.api, binding, mode, self.run_id)
        repository = self.api.get(f"repos/{REPOSITORY}")
        return repository.get("id") == REPOSITORY_ID and repository.get("full_name") == REPOSITORY

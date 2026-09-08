#!/usr/bin/env python3
"""Merge one routine PR and report its native delivery result exactly once."""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import re
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import asdict, dataclass, replace
from datetime import datetime, timezone
from typing import Any, Callable, Protocol


SHA_RE = re.compile(r"^[0-9a-f]{40}$")


class AmbiguousWrite(RuntimeError):
    """The merge request may have reached GitHub, so readback must decide."""


class NativeApi(Protocol):
    def get_pr(self, repo: str, pr: int) -> dict[str, Any]: ...

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict[str, Any]: ...

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict[str, Any]]: ...

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None: ...

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict[str, Any]]: ...


class GhApi:
    @staticmethod
    def _run(args: list[str], *, timeout: int = 30) -> dict[str, Any]:
        try:
            result = subprocess.run(
                ["gh", "api", *args],
                check=False,
                capture_output=True,
                text=True,
                timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.strip() or result.stdout.strip() or f"gh api exited {result.returncode}"
            raise RuntimeError(detail)
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise RuntimeError("GitHub returned a non-JSON response") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        return self._run([f"repos/{repo}/pulls/{pr}"])

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict[str, Any]:
        return self._run(
            [
                "--method",
                "PUT",
                f"repos/{repo}/pulls/{pr}/merge",
                "-f",
                f"sha={head}",
                "-f",
                f"merge_method={method}",
            ]
        )

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict[str, Any]]:
        pages = self._run([
            "--paginate", "--slurp",
            f"repos/{repo}/actions/workflows/{workflow}/runs?head_sha={head}&per_page=100",
        ])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned a non-page workflow-run response")
        runs: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("workflow_runs"), list):
                raise RuntimeError("GitHub returned a malformed workflow-run page")
            runs.extend(run for run in page["workflow_runs"] if isinstance(run, dict))
        return runs

    @staticmethod
    def _run_bytes(args: list[str], *, timeout: int = 30) -> bytes:
        try:
            result = subprocess.run(
                ["gh", "api", *args], check=False, capture_output=True, timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.decode(errors="replace").strip() or f"gh api exited {result.returncode}"
            raise RuntimeError(detail)
        return result.stdout

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None:
        pages = self._run(["--paginate", "--slurp", f"repos/{repo}/actions/runs/{run_id}/artifacts?per_page=100"])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned malformed workflow artifact pages")
        artifacts: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("artifacts"), list):
                raise RuntimeError("GitHub returned malformed workflow artifacts")
            artifacts.extend(artifact for artifact in page["artifacts"] if isinstance(artifact, dict))
        expected = f"qualification-selection-{head}"
        matches = [
            artifact for artifact in artifacts
            if isinstance(artifact, dict) and artifact.get("name") == expected
            and artifact.get("expired") is not True and isinstance(artifact.get("archive_download_url"), str)
        ]
        if not matches:
            return None
        if len(matches) != 1:
            raise RuntimeError("GitHub returned duplicate qualification-selection artifacts")
        archive = self._run_bytes([matches[0]["archive_download_url"]])
        if len(archive) > 1_048_576:
            raise RuntimeError("qualification-selection artifact exceeds 1 MiB")
        try:
            with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
                files = [entry for entry in bundle.infolist() if not entry.is_dir()]
                if len(files) != 1 or files[0].filename != "selection.json" or files[0].file_size > 65_536:
                    raise RuntimeError("qualification-selection archive has an unsafe shape")
                return bundle.read(files[0])
        except zipfile.BadZipFile as error:
            raise RuntimeError("qualification-selection artifact is not a ZIP archive") from error

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict[str, Any]]:
        pages = self._run(["--paginate", "--slurp", f"repos/{repo}/actions/runs/{run_id}/jobs?per_page=100"])
        if not isinstance(pages, list):
            raise RuntimeError("GitHub returned a non-page workflow-job response")
        jobs: list[dict[str, Any]] = []
        for page in pages:
            if not isinstance(page, dict) or not isinstance(page.get("jobs"), list):
                raise RuntimeError("GitHub returned a malformed workflow-job page")
            jobs.extend(job for job in page["jobs"] if isinstance(job, dict))
        return jobs


@dataclass(frozen=True)
class Summary:
    schema: str
    repo: str
    pr: int
    expectedHead: str
    observedHead: str | None
    outcome: str
    codeDelivery: str
    publication: str
    mergeCommit: str | None
    attempts: int
    reason: str | None
    validationDisposition: str
    coherentValidation: str
    baseRef: str | None = None
    baseSha: str | None = None
    outcomeAt: str | None = None
    observedAt: str | None = None
    telemetryHealth: str = "not-configured"


def observe_candidate(
    summary: Summary,
    *,
    assignment: str,
    store_root: str,
    engine: str,
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> str:
    """Invoke advisory CI reconciliation with the exact generated delivery JSON."""
    payload = json.dumps(asdict(summary), separators=(",", ":"), sort_keys=True) + "\n"
    try:
        with tempfile.NamedTemporaryFile(
            "w", encoding="utf-8", prefix="fsgg-routine-delivery-", suffix=".json"
        ) as delivery:
            delivery.write(payload)
            delivery.flush()
            completed = runner(
                [engine, "telemetry", "ci", "reconcile", "--assignment", assignment,
                 "--delivery", delivery.name, "--store-root", store_root],
                check=False, capture_output=True, text=True, timeout=35,
            )
        if completed.returncode == 0:
            try:
                result = json.loads(completed.stdout)
                health = result.get("driverHealth")
                if health in {"complete", "open", "pending", "missing-outcome"}:
                    return health
            except (json.JSONDecodeError, AttributeError):
                pass
            return "pending"
    except (OSError, subprocess.SubprocessError):
        pass
    print("fsgg routine telemetry: CI observation unavailable; native delivery is unchanged", file=sys.stderr)
    return "unavailable"


def head_of(pr: dict[str, Any]) -> str | None:
    head = pr.get("head")
    return head.get("sha") if isinstance(head, dict) and isinstance(head.get("sha"), str) else None


def base_of(pr: dict[str, Any]) -> tuple[str | None, str | None]:
    base = pr.get("base")
    if not isinstance(base, dict):
        return None, None
    ref = base.get("ref") if isinstance(base.get("ref"), str) else None
    sha = base.get("sha") if isinstance(base.get("sha"), str) and SHA_RE.fullmatch(base["sha"]) else None
    return ref, sha


def merged_commit_of(pr: dict[str, Any]) -> str | None:
    value = pr.get("merge_commit_sha")
    return value if isinstance(value, str) and SHA_RE.fullmatch(value) else None


def outcome_time_of(pr: dict[str, Any]) -> str | None:
    value = pr.get("merged_at")
    return value if isinstance(value, str) and value else None


def is_merged(pr: dict[str, Any]) -> bool:
    return pr.get("merged") is True or pr.get("merged_at") is not None


def coherent_state(runs: list[dict[str, Any]], expected_head: str) -> tuple[str, str | None, dict[str, Any] | None]:
    exact = [run for run in runs if run.get("head_sha") == expected_head]
    if not exact:
        return "pending", "no coherent run exists for the exact candidate", None
    active = [run for run in exact if run.get("status") != "completed"]
    if active:
        newest = max(active, key=lambda run: (str(run.get("updated_at") or ""), int(run.get("id") or 0)))
        return "pending", "coherent run for the exact candidate is still pending or running", newest
    latest = max(
        exact,
        key=lambda run: (
            str(run.get("updated_at") or ""),
            int(run.get("run_attempt") or 0),
            int(run.get("id") or 0),
        ),
    )
    if latest.get("conclusion") == "success":
        return "passed", None, latest
    return "failed", f"latest coherent run concluded {latest.get('conclusion') or 'unknown'}", latest


SELECTION_KEYS = [
    "schema", "candidateObligationSha256", "disposition", "reason", "prior", "semanticDelta",
    "bindingCorrespondenceSha256", "coherentRunPending", "coherentState", "selectionSha256",
]


def parse_selection(content: bytes) -> tuple[str, str | None]:
    if len(content) > 65_536 or not content.endswith(b"\n"):
        return "invalid", "qualification selection is oversized or non-canonical"
    try:
        value = json.loads(content)
    except (UnicodeDecodeError, json.JSONDecodeError):
        return "invalid", "qualification selection is malformed JSON"
    if not isinstance(value, dict) or list(value) != SELECTION_KEYS:
        return "invalid", "qualification selection properties are not the exact canonical set"
    digest = value.get("selectionSha256")
    payload = dict(value)
    payload.pop("selectionSha256", None)
    encoded = json.dumps(payload, separators=(",", ":"), ensure_ascii=False).encode()
    if not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest) or hashlib.sha256(encoded).hexdigest() != digest:
        return "invalid", "qualification selection self digest does not match"
    disposition = value.get("disposition")
    if disposition not in {"current", "reused", "deferred", "failed"}:
        return "invalid", "qualification selection disposition is unsupported"
    if value.get("schema") != "fsgg.coordination.qualification-selection/1":
        return "invalid", "qualification selection schema is unsupported"
    digest64 = lambda item: isinstance(item, str) and re.fullmatch(r"[0-9a-f]{64}", item) is not None
    if not digest64(value.get("candidateObligationSha256")) or not isinstance(value.get("reason"), str) or not value["reason"].strip():
        return "invalid", "qualification selection identity or reason is invalid"
    semantic = value.get("semanticDelta")
    if (not isinstance(semantic, dict)
            or list(semantic) != ["evaluatorSha256", "deltaSha256", "empty"]
            or not digest64(semantic.get("evaluatorSha256"))
            or not digest64(semantic.get("deltaSha256"))
            or not isinstance(semantic.get("empty"), bool)):
        return "invalid", "qualification selection semantic delta is invalid"
    correspondence = value.get("bindingCorrespondenceSha256")
    if correspondence is not None and not digest64(correspondence):
        return "invalid", "qualification selection binding correspondence is invalid"
    if not isinstance(value.get("coherentRunPending"), bool) or value.get("coherentState") not in {"pending", "running", "passed", "blocked", "disputed"}:
        return "invalid", "qualification selection coherent state is invalid"
    if disposition == "reused":
        prior = value.get("prior")
        prior_keys = ["candidateObligationSha256", "runId", "attempt", "executedReceiptSha256", "completedAt", "expiresAt", "authentic", "complete"]
        if (not isinstance(prior, dict) or list(prior) != prior_keys
                or not digest64(prior.get("candidateObligationSha256"))
                or not isinstance(prior.get("runId"), int) or prior["runId"] <= 0
                or not isinstance(prior.get("attempt"), int) or prior["attempt"] <= 0
                or not digest64(prior.get("executedReceiptSha256"))
                or prior.get("authentic") is not True or prior.get("complete") is not True):
            return "invalid", "reused selection lacks authentic complete prior evidence"
        if not isinstance(semantic, dict) or semantic.get("empty") is not True:
            return "invalid", "reused selection lacks an empty semantic delta"
        try:
            completed = datetime.fromisoformat(str(prior.get("completedAt")).replace("Z", "+00:00"))
            expires = datetime.fromisoformat(str(prior.get("expiresAt")).replace("Z", "+00:00"))
        except ValueError:
            return "invalid", "reused selection has invalid prior timestamps"
        now = datetime.now(timezone.utc)
        if completed.tzinfo is None or expires.tzinfo is None or completed > now or expires <= now:
            return "invalid", "reused selection prior evidence is not currently valid"
        if value.get("coherentRunPending") is not True or value.get("coherentState") != "pending":
            return "invalid", "reused selection is not the producer's initial pending decision"
    return disposition, None


def validation_state(api: NativeApi, repo: str, workflow: str, head: str) -> tuple[str, str, str | None]:
    coherent, reason, run = coherent_state(api.coherent_runs(repo, workflow, head), head)
    if run is None or not isinstance(run.get("id"), int):
        return "current", coherent, reason
    failed_jobs = [
        job for job in api.coherent_jobs(repo, run["id"])
        if job.get("status") == "completed"
        and job.get("conclusion") in {"failure", "timed_out", "cancelled", "action_required", "startup_failure"}
    ]
    if failed_jobs:
        return "failed", "failed", "an exact-head coherent job has already failed"
    content = api.qualification_selection(repo, run["id"], head)
    if content is None:
        return "current", coherent, reason or "exact-head reuse has not been validated"
    disposition, selection_reason = parse_selection(content)
    return disposition, coherent, selection_reason or reason


def eligible(pr: dict[str, Any], expected_head: str) -> tuple[bool, str | None]:
    observed = head_of(pr)
    if observed != expected_head:
        return False, f"changed head: expected {expected_head}, observed {observed or 'unreadable'}"
    if is_merged(pr):
        return True, None
    if pr.get("state") != "open":
        return False, f"pull request is {pr.get('state') or 'unreadable'}, not open"
    if pr.get("draft") is True:
        return False, "pull request is draft"
    if pr.get("mergeable") is not True:
        return False, "pull request is not currently mergeable"
    merge_state = pr.get("mergeable_state")
    if merge_state not in {"clean", "unstable", "has_hooks"}:
        return False, f"pull request merge state is {merge_state or 'unreadable'}"
    return True, None


def summarize(
    api: NativeApi,
    *,
    repo: str,
    pr_number: int,
    expected_head: str,
    merge_method: str,
    publication_required: bool,
    apply: bool,
    coherent_workflow: str | None = None,
    candidate_observer: Callable[[Summary], None] | None = None,
) -> tuple[int, Summary]:
    publication = "pending" if publication_required else "not-required"
    before = api.get_pr(repo, pr_number)
    base_ref, base_sha = base_of(before)
    def bound(*values: Any, native: dict[str, Any] = before) -> Summary:
        now = datetime.now(timezone.utc)
        outcome_at = outcome_time_of(native)
        if outcome_at:
            parsed_outcome = datetime.fromisoformat(outcome_at.replace("Z", "+00:00"))
            if parsed_outcome > now:
                now = parsed_outcome
        return Summary(*values, baseRef=base_ref, baseSha=base_sha,
                       outcomeAt=outcome_at,
                       observedAt=now.isoformat().replace("+00:00", "Z"))
    allowed, reason = eligible(before, expected_head)
    observed = head_of(before)
    if not allowed:
        return 2, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "refused", "not-delivered", publication, None, 0, reason, "current", "unobserved",
        )
    disposition, coherent = "current", "not-required"
    if coherent_workflow:
        disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
        if not is_merged(before) and (disposition in {"invalid", "deferred", "failed"}
                                      or (coherent != "passed" and disposition != "reused")):
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, reason,
                disposition, coherent,
            )
    if is_merged(before):
        disputed = coherent == "failed" or disposition in {"invalid", "deferred", "failed"}
        return 4 if disputed else 0, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "delivered-disputed" if disputed else "delivered", "delivered",
            publication, merged_commit_of(before), 0, reason if disputed else None, disposition,
            "disputed" if disputed else coherent,
        )
    if observed == expected_head and candidate_observer is not None:
        candidate_observer(bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "ready", "not-delivered", publication, None, 0, None, disposition, coherent,
        ))
    if not apply:
        return 0, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "ready", "not-delivered", publication, None, 0, None, disposition, coherent,
        )

    attempts = 0
    while attempts < 2:
        attempts += 1
        try:
            response = api.merge(repo, pr_number, expected_head, merge_method)
        except AmbiguousWrite:
            after = api.get_pr(repo, pr_number)
            allowed, reason = eligible(after, expected_head)
            if is_merged(after) and head_of(after) == expected_head:
                if coherent_workflow:
                    disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
                disputed = coherent == "failed" or disposition in {"invalid", "deferred", "failed"}
                return 4 if disputed else 0, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "delivered-disputed" if disputed else "delivered-after-readback",
                    "delivered", publication,
                    merged_commit_of(after), attempts, reason if disputed else None, disposition,
                    "disputed" if disputed else coherent, native=after,
                )
            if not allowed:
                return 2, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "refused", "not-delivered", publication, None, attempts, reason, disposition, coherent,
                )
            if attempts < 2:
                continue
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "two ambiguous merge attempts; native readback still reports an eligible open PR", disposition, coherent,
            )
        except RuntimeError as error:
            after = api.get_pr(repo, pr_number)
            if is_merged(after) and head_of(after) == expected_head:
                if coherent_workflow:
                    disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
                disputed = coherent == "failed" or disposition in {"invalid", "deferred", "failed"}
                return 4 if disputed else 0, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "delivered-disputed" if disputed else "delivered-after-readback",
                    "delivered", publication,
                    merged_commit_of(after), attempts, reason if disputed else None, disposition,
                    "disputed" if disputed else coherent, native=after,
                )
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "refused", "not-delivered", publication, None, attempts, str(error), disposition, coherent,
            )

        after = api.get_pr(repo, pr_number)
        if coherent_workflow:
            disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
            if coherent == "failed" or disposition in {"invalid", "deferred", "failed"}:
                if not is_merged(after) or head_of(after) != expected_head:
                    return 2, bound(
                        "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                        "refused", "not-delivered", publication, None, attempts, reason,
                        disposition, coherent,
                    )
                return 4, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "delivered-disputed", "delivered", publication, merged_commit_of(after), attempts, reason,
                    disposition, "disputed", native=after,
                )
        if response.get("merged") is True and is_merged(after) and head_of(after) == expected_head:
            merge_commit = response.get("sha")
            if not isinstance(merge_commit, str) or not SHA_RE.fullmatch(merge_commit):
                merge_commit = merged_commit_of(after)
            return 0, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "delivered", "delivered", publication, merge_commit, attempts, None, disposition, coherent, native=after,
            )
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
            "indeterminate", "unknown", publication, None, attempts,
            "merge response and native PR readback do not both establish delivery", disposition, coherent,
        )

    raise AssertionError("bounded merge loop escaped")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--repo", required=True, help="OWNER/REPO")
    result.add_argument("--pr", required=True, type=int)
    result.add_argument("--head", required=True)
    result.add_argument("--merge-method", choices=("merge", "squash", "rebase"), default="squash")
    result.add_argument("--publication", choices=("none", "required"), default="none")
    result.add_argument("--coherent-workflow", help="candidate-scoped coherent workflow file or id")
    result.add_argument("--telemetry-assignment", help="private CI assignment for advisory automatic observation")
    result.add_argument("--telemetry-store-root", help="private durable telemetry store root")
    result.add_argument("--telemetry-engine", default="fsgg-coord-engine", help="installed telemetry-capable coordination engine")
    result.add_argument("--apply", action="store_true")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    if not re.fullmatch(r"[^/\s]+/[^/\s]+", args.repo):
        parser().error("--repo must be OWNER/REPO")
    if args.pr <= 0:
        parser().error("--pr must be positive")
    if not SHA_RE.fullmatch(args.head):
        parser().error("--head must be a lowercase 40-hex commit SHA")
    if bool(args.telemetry_assignment) != bool(args.telemetry_store_root):
        parser().error("--telemetry-assignment and --telemetry-store-root must be supplied together")
    observer = None
    observation_health: list[str] = []
    if args.telemetry_assignment and args.telemetry_store_root:
        def observer(summary: Summary) -> None:
            observation_health.append(observe_candidate(
                summary, assignment=args.telemetry_assignment, store_root=args.telemetry_store_root,
                engine=args.telemetry_engine,
            ))
    try:
        code, result = summarize(
            GhApi(), repo=args.repo, pr_number=args.pr, expected_head=args.head,
            merge_method=args.merge_method, publication_required=args.publication == "required",
            apply=args.apply, coherent_workflow=args.coherent_workflow, candidate_observer=observer,
        )
    except (RuntimeError, AmbiguousWrite) as error:
        code = 3
        result = Summary(
            "fsgg.routine-delivery/v1", args.repo, args.pr, args.head, None,
            "indeterminate", "unknown",
            "pending" if args.publication == "required" else "not-required",
            None, 0, str(error), "current", "unobserved",
        )
    if observer is not None and result.outcome != "ready":
        observer(result)
    if observer is not None:
        result = replace(result, telemetryHealth=observation_health[-1] if observation_health else "unavailable")
    print(json.dumps(asdict(result), separators=(",", ":"), sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""Inspect one routine PR and report its native delivery result exactly once."""

from __future__ import annotations

import argparse
import math
import hashlib
import io
import json
import pathlib
import os
import selectors
import time
import re
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import asdict, dataclass, replace
from datetime import datetime, timedelta, timezone
from typing import Any, Callable, Protocol

sys.dont_write_bytecode = True

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
MAX_SELECTION_ARCHIVE_BYTES = 1_048_576
MAX_SELECTION_UNCOMPRESSED_BYTES = 1_048_576
MAX_SELECTION_ENTRIES = 128
MAX_SELECTION_BYTES = 65_536


class AmbiguousWrite(RuntimeError):
    """The merge request may have reached GitHub, so readback must decide."""


class NativeMergeRefused(RuntimeError):
    """GitHub synchronously refused the head-conditioned merge request."""


@dataclass(frozen=True)
class TelemetryConfig:
    path: str
    store_root: str
    engine: str
    repository: str | None
    workspace: bool


def _compiled_json(
    command: list[str], *, runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> tuple[int, dict[str, Any]]:
    try:
        completed = runner(
            command, check=False, capture_output=True, text=True, timeout=25,
        )
    except (OSError, subprocess.SubprocessError) as error:
        raise RuntimeError("compiled telemetry helper is unavailable") from error
    if len(completed.stdout.encode("utf-8")) > 16 * 1024:
        raise RuntimeError("compiled telemetry helper returned an oversized response")
    try:
        value = json.loads(completed.stdout)
    except json.JSONDecodeError as error:
        raise RuntimeError("compiled telemetry helper returned invalid JSON") from error
    if not isinstance(value, dict):
        raise RuntimeError("compiled telemetry helper returned a non-object response")
    return completed.returncode, value


def discover_telemetry_config(
    explicit: str | None, *, command_engine: str = "fsgg-coord-engine",
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> TelemetryConfig | None:
    command = [command_engine, "skill", "telemetry-config", "discover"]
    if explicit is not None:
        command.extend(["--config", explicit])
    code, value = _compiled_json(command, runner=runner)
    if value == {"schema": "fsgg.telemetry.config-discovery/1", "status": "not-configured"}:
        if code != 2:
            raise RuntimeError("compiled telemetry discovery returned an invalid status")
        return None
    expected = {"schema", "status", "configPath", "storeRoot", "engine", "repository", "workspace"}
    if code != 0 or set(value) != expected or value.get("schema") != "fsgg.telemetry.config-discovery/1" or value.get("status") != "configured":
        raise RuntimeError("compiled telemetry configuration discovery refused")
    path, store, engine = value.get("configPath"), value.get("storeRoot"), value.get("engine")
    repository, workspace = value.get("repository"), value.get("workspace")
    if (not isinstance(path, str) or not pathlib.Path(path).is_absolute()
            or not isinstance(store, str) or not pathlib.Path(store).is_absolute()
            or not isinstance(engine, str) or not engine
            or (repository is not None and not isinstance(repository, str))
            or not isinstance(workspace, bool)):
        raise RuntimeError("compiled telemetry configuration discovery returned an invalid projection")
    return TelemetryConfig(path, store, engine, repository, workspace)


def create_ci_assignment(
    config: TelemetryConfig, *, feature: str, item: str, attempt: str,
    parent_attempt: str | None, command_engine: str = "fsgg-coord-engine",
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> str:
    command = [command_engine, "skill", "roadmap-telemetry", "--config", config.path,
               "ci-assignment", "--feature", feature, "--item", item,
               "--attempt", attempt, "--producer", "routine-delivery"]
    if parent_attempt is not None:
        command.extend(["--parent-attempt", parent_attempt])
    code, value = _compiled_json(command, runner=runner)
    if (code != 0 or set(value) != {"schema", "status", "assignment"}
            or value.get("schema") != "fsgg.telemetry.assignment-result/1"
            or value.get("status") != "ready"
            or not isinstance(value.get("assignment"), str)
            or not pathlib.Path(value["assignment"]).is_absolute()):
        raise RuntimeError("compiled telemetry assignment creation refused")
    return value["assignment"]


@dataclass(frozen=True)
class MergeEffectRequest:
    """Exact native pull-request merge inputs established by fresh readback."""

    repository: str
    pullRequest: int
    expectedHead: str
    baseRef: str
    baseSha: str
    mergeMethod: str


def merge_effect_request(
    repo: str,
    pr: int,
    head: str,
    base_ref: str,
    base_sha: str,
    method: str,
) -> MergeEffectRequest:
    return MergeEffectRequest(
        repository=repo,
        pullRequest=pr,
        expectedHead=head,
        baseRef=base_ref,
        baseSha=base_sha,
        mergeMethod=method,
    )


class NativeApi(Protocol):
    def get_pr(self, repo: str, pr: int) -> dict[str, Any]: ...

    def merge(self, request: MergeEffectRequest) -> dict[str, Any]: ...

    def coherent_runs(self, repo: str, workflow: str, head: str) -> list[dict[str, Any]]: ...

    def qualification_selection(self, repo: str, run_id: int, head: str) -> bytes | None: ...

    def coherent_jobs(self, repo: str, run_id: int) -> list[dict[str, Any]]: ...


class GhApi:
    @staticmethod
    def _run(
        args: list[str], *, timeout: int = 30, input_text: str | None = None,
        mutation: bool = False,
    ) -> dict[str, Any]:
        try:
            result = subprocess.run(
                ["gh", "api", *args],
                check=False,
                capture_output=True,
                text=True,
                input=input_text,
                timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.strip() or result.stdout.strip() or f"gh api exited {result.returncode}"
            if mutation:
                raise NativeMergeRefused(detail)
            raise RuntimeError(detail)
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as error:
            if mutation:
                raise AmbiguousWrite("GitHub returned a non-JSON merge response") from error
            raise RuntimeError("GitHub returned a non-JSON response") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        value = self._run([f"repos/{repo}/pulls/{pr}"])
        if not isinstance(value, dict):
            raise RuntimeError("GitHub returned a non-object pull request")
        return value

    def merge(self, request: MergeEffectRequest) -> dict[str, Any]:
        payload = json.dumps(
            {"sha": request.expectedHead, "merge_method": request.mergeMethod},
            separators=(",", ":"), sort_keys=True,
        )
        response = self._run(
            ["--method", "PUT", f"repos/{request.repository}/pulls/{request.pullRequest}/merge",
             "--input", "-"],
            input_text=payload,
            mutation=True,
        )
        if not isinstance(response, dict):
            raise AmbiguousWrite("GitHub returned a non-object merge response")
        return response

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
        if len(archive) > MAX_SELECTION_ARCHIVE_BYTES:
            raise RuntimeError("qualification-selection artifact exceeds 1 MiB")
        try:
            with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
                entries = bundle.infolist()
                if len(entries) > MAX_SELECTION_ENTRIES:
                    raise RuntimeError("qualification-selection archive has an unsafe shape")
                paths: set[str] = set()
                selections: list[zipfile.ZipInfo] = []
                uncompressed = 0
                for entry in entries:
                    name = entry.filename[:-1] if entry.is_dir() and entry.filename.endswith("/") else entry.filename
                    parts = name.split("/")
                    if (not name or name.startswith("/") or "\\" in name or "\x00" in name
                            or re.match(r"^[A-Za-z]:", name) or any(part in {"", ".", ".."} for part in parts)
                            or name in paths):
                        raise RuntimeError("qualification-selection archive has an unsafe shape")
                    paths.add(name)
                    if entry.is_dir():
                        continue
                    uncompressed += entry.file_size
                    if uncompressed > MAX_SELECTION_UNCOMPRESSED_BYTES:
                        raise RuntimeError("qualification-selection archive exceeds 1 MiB uncompressed")
                    if name == "selection.json":
                        selections.append(entry)
                if len(selections) != 1 or selections[0].file_size > MAX_SELECTION_BYTES:
                    raise RuntimeError("qualification-selection archive has an unsafe shape")
                return bundle.read(selections[0])
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
    store_root: str | None = None,
    config: str | None = None,
    repository: str | None = None,
    engine: str,
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
    deadline: float | None = None,
    clock: Callable[[], float] = time.monotonic,
) -> str:
    """Invoke advisory CI reconciliation with the exact generated delivery JSON."""
    payload = json.dumps(asdict(summary), separators=(",", ":"), sort_keys=True) + "\n"
    try:
        with tempfile.NamedTemporaryFile(
            "w", encoding="utf-8", prefix="fsgg-routine-delivery-", suffix=".json"
        ) as delivery:
            delivery.write(payload)
            delivery.flush()
            command = [engine, "telemetry", "ci", "reconcile", "--assignment", assignment,
                       "--delivery", delivery.name]
            if store_root is not None:
                command.extend(["--store-root", store_root])
            else:
                if config is not None:
                    command.extend(["--config", config])
                if repository is not None:
                    command.extend(["--repository", repository])
            if deadline is None:
                completed = runner(
                    command, check=False, capture_output=True, text=True, timeout=35,
                )
            else:
                code, raw = bounded_watch_query(command, deadline, clock=clock, max_seconds=35)
                completed = subprocess.CompletedProcess(command, code, raw.decode("utf-8"), "")
        if completed.returncode == 0:
            try:
                result = json.loads(completed.stdout)
                health = result.get("driverHealth")
                if health in {"complete", "open", "pending", "missing-outcome"}:
                    return health
            except (json.JSONDecodeError, AttributeError):
                pass
            return "pending"
    except (OSError, subprocess.SubprocessError, RuntimeError, UnicodeDecodeError):
        pass
    print("fsgg routine telemetry: CI observation unavailable; native delivery is unchanged", file=sys.stderr)
    return "unavailable"



def observe_dashboard_event(
    *, engine: str, config: str | None, deadline: float | None,
    clock: Callable[[], float] = time.monotonic,
    query: Callable[..., tuple[int, bytes]] | None = None,
) -> str:
    """One advisory final refresh within the caller's original absolute boundary.

    No activation, retry or credential lookup here. The native publisher owns its
    existing receipt/credential/destination contract and remote verification.
    """
    if deadline is None:
        return "not-run-budget-unavailable"
    if clock() >= deadline - WATCH_CLEANUP_SECONDS:
        return "not-run-budget-exhausted"
    if config is None:
        return "not-run-config-unavailable"
    try:
        code, raw = (query or bounded_watch_query)(
            [engine, "telemetry", "dashboard", "publisher-event", "--config", config],
            deadline, clock=clock, max_seconds=60, max_bytes=8192,
        )
        if code != 0 or clock() > deadline:
            return "unavailable"
        value = json.loads(raw)
        if not isinstance(value, dict):
            return "unavailable"
        status, reason = value.get("status"), value.get("reason")
        if not isinstance(status, str) or not isinstance(reason, str):
            return "unavailable"
        pair = (status, reason)
        if pair in {("published", "PUBLICATION_VERIFIED"),
                    ("unchanged", "SEMANTIC_CONTENT_UNCHANGED")}:
            if (value.get("schema") == "fsgg.telemetry.dashboard-event-health/1"
                    and isinstance(value.get("publicRevision"), str)
                    and re.fullmatch(r"[0-9a-f]{64}", value["publicRevision"])
                    and isinstance(value.get("commit"), str)
                    and SHA_RE.fullmatch(value["commit"])):
                return value["status"]
        if value.get("status") == "skipped":
            return "skipped"
    except (OSError, subprocess.SubprocessError, RuntimeError, ValueError):
        pass
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
    if not isinstance(value, str) or not value.endswith("Z"):
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return value if parsed.utcoffset() == timedelta(0) else None


def is_merged(pr: dict[str, Any]) -> bool:
    return (pr.get("merged") is True and pr.get("state") == "closed"
            and outcome_time_of(pr) is not None and merged_commit_of(pr) is not None)


def correlated_merge_readback(pr: dict[str, Any], request: MergeEffectRequest) -> str | None:
    """Return the native merge commit only for the exact preflight identity."""
    if (not is_merged(pr) or head_of(pr) != request.expectedHead
            or base_of(pr) != (request.baseRef, request.baseSha)):
        return None
    return merged_commit_of(pr)


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
    if pr.get("merged") is not False or pr.get("merged_at") is not None:
        return False, "pull request has contradictory or malformed merge evidence"
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
    if not isinstance(before, dict):
        raise RuntimeError("native pull request readback is not an object")
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
    if not is_merged(before) and (base_ref is None or base_sha is None):
        return 2, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "refused", "not-delivered", publication, None, 0,
            "pull request base identity is unreadable", "current", "unobserved",
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

    request = merge_effect_request(
        repo, pr_number, expected_head, base_ref, base_sha, merge_method,
    )
    attempts = 1
    response: dict[str, Any] | None = None
    refused: str | None = None
    try:
        response = api.merge(request)
    except NativeMergeRefused as error:
        refused = str(error)
    except (AmbiguousWrite, RuntimeError):
        pass

    try:
        after = api.get_pr(repo, pr_number)
    except RuntimeError:
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, None,
            "indeterminate", "unknown", publication, None, attempts,
            "merge response cannot be confirmed because native PR readback is unavailable",
            disposition, coherent,
        )
    if not isinstance(after, dict):
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head,
            None,
            "indeterminate", "unknown", publication, None, attempts,
            "native PR readback has a malformed shape",
            disposition, coherent,
        )
    readback_commit = correlated_merge_readback(after, request)
    if response is not None and not isinstance(response, dict):
        response = None
    if response is None:
        if readback_commit is None:
            if refused is not None:
                return 2, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "refused", "not-delivered", publication, None, attempts,
                    f"GitHub refused the head-conditioned merge: {refused}", disposition, coherent,
                )
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "ambiguous merge response; exact native merged-state readback is absent",
                disposition, coherent,
            )
        merge_commit = readback_commit
        effect_confirmed = True
    else:
        merge_commit = response.get("sha")
        effect_confirmed = (
            response.get("merged") is True
            and isinstance(merge_commit, str) and SHA_RE.fullmatch(merge_commit) is not None
            and merge_commit == readback_commit
        )
    if coherent_workflow:
        try:
            disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
        except RuntimeError:
            return 3, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "post-merge coherent validation is unavailable after one effect attempt",
                disposition, coherent,
            )
        if coherent == "failed" or disposition in {"invalid", "deferred", "failed"}:
            if not effect_confirmed:
                return 3, bound(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "indeterminate", "unknown", publication, None, attempts,
                    "coherent validation failed and the merge effect remains unproven",
                    disposition, coherent,
                )
            return 4, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "delivered-disputed", "delivered", publication, merge_commit, attempts, reason,
                disposition, "disputed", native=after,
            )
    if effect_confirmed:
        return 0, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
            "delivered", "delivered", publication, merge_commit, attempts, None,
            disposition, coherent, native=after,
        )
    return 3, bound(
        "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
        "indeterminate", "unknown", publication, None, attempts,
        "merge response and native PR readback do not both establish delivery", disposition, coherent,
    )


# This opt-in native observer owns only its direct gh query children. It is not
# a process-tree supervisor, readiness evaluator or delivery authorization.
WATCH_MAX_SECONDS = 600
WATCH_MAX_QUERIES = 384
WATCH_MAX_CHECKS = 128
WATCH_QUERY_BYTES = 262_144
WATCH_EMIT_BYTES = 4_194_304
WATCH_QUERY_SECONDS = 30.0
WATCH_CLEANUP_SECONDS = 0.5
WATCH_INTERVAL_SECONDS = 5.0
WATCH_FIELDS = "bucket,completedAt,event,link,name,startedAt,state,workflow"


class WatchDeadline(RuntimeError):
    """The original watch budget cannot fit another bounded query."""


def bounded_watch_query(
    command: list[str], deadline: float, *, clock: Callable[[], float] = time.monotonic,
    popen: Callable[..., Any] = subprocess.Popen,
    max_seconds: float = WATCH_QUERY_SECONDS, max_bytes: int = WATCH_QUERY_BYTES,
) -> tuple[int, bytes]:
    """Bound both streams before retaining; kill/reap the same direct child on error."""
    work_end = min(deadline - WATCH_CLEANUP_SECONDS, clock() + max_seconds)
    if work_end <= clock():
        raise WatchDeadline("original watch cleanup reserve reached")
    process = popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE, close_fds=True)
    output = bytearray()
    used = 0
    selector = None
    try:
        selector = selectors.DefaultSelector()
        for stream, stdout in ((process.stdout, True), (process.stderr, False)):
            os.set_blocking(stream.fileno(), False)
            selector.register(stream, selectors.EVENT_READ, stdout)
        while selector.get_map() or process.poll() is None:
            remaining = work_end - clock()
            if remaining <= 0:
                if clock() >= deadline - WATCH_CLEANUP_SECONDS:
                    raise WatchDeadline("native query reached original watch cleanup reserve")
                raise RuntimeError("native query exceeded its clipped timeout")
            for key, _mask in selector.select(min(remaining, 0.1)):
                raw = os.read(key.fileobj.fileno(), min(65_536, max_bytes - used + 1))
                if not raw:
                    selector.unregister(key.fileobj)
                    continue
                if used + len(raw) > max_bytes:
                    raise RuntimeError("native query combined stdout/stderr exceeded byte bound")
                used += len(raw)
                if key.data:
                    output.extend(raw)
        process.wait(timeout=max(0, min(WATCH_CLEANUP_SECONDS, deadline - clock())))
        return process.returncode, bytes(output)
    finally:
        try:
            if process.poll() is None:
                process.kill()  # Popen retains the original unreaped direct child.
            process.wait(timeout=max(0, min(WATCH_CLEANUP_SECONDS, deadline - clock())))
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("original native query retirement is unknown at watch deadline") from error
        finally:
            if selector is not None:
                selector.close()
            process.stdout.close()
            process.stderr.close()


class WatchGhApi(GhApi):
    """Existing native adapter with watch-only finite query/population controls."""

    def __init__(self, deadline: float, *, query: Callable[..., tuple[int, bytes]] = bounded_watch_query,
                 clock: Callable[[], float] = time.monotonic):
        self.deadline, self.query, self.clock = deadline, query, clock
        self.query_count = 0

    def _watch_read(self, command: list[str], accepted: tuple[int, ...]) -> tuple[int, Any]:
        if self.query_count >= WATCH_MAX_QUERIES:
            raise RuntimeError("native watch query-count bound reached")
        if self.clock() >= self.deadline - WATCH_CLEANUP_SECONDS:
            raise WatchDeadline("original watch cleanup reserve reached")
        self.query_count += 1
        code, raw = self.query(command, self.deadline, clock=self.clock)
        if len(raw) > WATCH_QUERY_BYTES:
            raise RuntimeError("native query response exceeded byte bound")
        if code not in accepted:
            raise RuntimeError(f"native read failed with exit code {code}")
        try:
            return code, json.loads(raw)
        except (UnicodeDecodeError, json.JSONDecodeError) as error:
            raise RuntimeError("native read returned malformed JSON") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        _code, value = self._watch_read(["gh", "api", f"repos/{repo}/pulls/{pr}"], (0,))
        if not isinstance(value, dict):
            raise RuntimeError("native read returned a non-object pull request")
        return value

    def checks(self, repo: str, pr: int) -> list[dict[str, Any]]:
        # Native bucket meanings/fields: https://cli.github.com/manual/gh_pr_checks
        code, value = self._watch_read(
            ["gh", "pr", "checks", str(pr), "--repo", repo, "--json", WATCH_FIELDS], (0,),
        )
        if not isinstance(value, list) or len(value) > WATCH_MAX_CHECKS:
            raise RuntimeError("native checks population is malformed or exceeds bound")
        fields = set(WATCH_FIELDS.split(","))
        identities: set[tuple[str, ...]] = set()
        for row in value:
            if (not isinstance(row, dict) or set(row) != fields
                    or any(not isinstance(row[name], str) for name in fields)
                    or not row["name"] or not row["state"]
                    or row["bucket"] not in {"pass", "fail", "pending", "skipping", "cancel"}):
                raise RuntimeError("native checks contain incomplete or unknown fields")
            identity = tuple(row[name] for name in ("name", "workflow", "link", "event"))
            if identity in identities:
                raise RuntimeError("native checks contain duplicate identities")
            identities.add(identity)
        return sorted(value, key=lambda row: tuple(row[name] for name in ("name", "workflow", "link", "event")))


def watch_checks(
    repo: str, pr: int, head: str, seconds: int, *,
    clock: Callable[[], float] = time.monotonic, sleep: Callable[[float], None] = time.sleep,
    utc_now: Callable[[], datetime] = lambda: datetime.now(timezone.utc),
    api_factory: Callable[..., Any] = WatchGhApi,
    emit: Callable[[dict[str, Any]], None] | None = None,
) -> int:
    """Emit compact changes only; a terminal native sample never grants apply."""
    if type(seconds) is not int or not 1 <= seconds <= WATCH_MAX_SECONDS:
        raise ValueError("--watch-seconds must be between 1 and 600")
    deadline = clock() + seconds
    api = api_factory(deadline, clock=clock)
    previous = None
    revision = 0
    emitted_bytes = 0

    def publish(event: str, projection: Any = None, reason: str | None = None) -> None:
        nonlocal emitted_bytes
        value = {"schema": "fsgg.routine-check-watch/v1", "event": event,
                 "repository": repo, "pullRequest": pr, "expectedHead": head,
                 "revision": revision, "observedAt": utc_now().isoformat(),
                 "nativeQueries": api.query_count, "projection": projection,
                 "reason": reason, "readiness": "not-evaluated", "applyAuthorized": False}
        raw = json.dumps(value, separators=(",", ":"), sort_keys=True)
        size = len(raw.encode("utf-8")) + 1
        limit = WATCH_EMIT_BYTES if event in {"observation-failure", "deadline"} else WATCH_EMIT_BYTES - 4096
        if emitted_bytes + size > limit:
            raise RuntimeError("watch emitted-output byte bound reached")
        emitted_bytes += size
        if emit is None:
            print(raw, flush=True)
        else:
            emit(value)

    try:
        while True:
            if clock() >= deadline - WATCH_CLEANUP_SECONDS:
                publish("deadline", reason="original watch deadline or cleanup reserve reached")
                return 3
            before = api.get_pr(repo, pr)
            if head_of(before) != head:
                raise RuntimeError("exact expected head changed before native check read")
            checks = api.checks(repo, pr)
            after = api.get_pr(repo, pr)
            if head_of(after) != head:
                raise RuntimeError("exact expected head changed after native check read")
            if (after.get("state") not in {"open", "closed"}
                    or type(after.get("merged")) is not bool or type(after.get("draft")) is not bool):
                raise RuntimeError("native pull request state observation is incomplete")
            if clock() >= deadline:
                raise WatchDeadline("completed read arrived after original watch deadline")
            projection = {"head": head, "state": after["state"], "merged": after["merged"],
                          "draft": after["draft"], "checks": checks,
                          "sourceUpdatedAt": after.get("updated_at")}
            # Actual source check timestamps identify starts/completions. Observer
            # time and unrelated PR update timestamps do not wake the caller.
            material = {key: value for key, value in projection.items() if key != "sourceUpdatedAt"}
            fingerprint = json.dumps(material, separators=(",", ":"), sort_keys=True)
            changed = fingerprint != previous
            if changed:
                revision += 1
            buckets = {row["bucket"] for row in checks}
            failed = bool(buckets & {"fail", "cancel"})
            terminal = after["state"] == "closed" or failed or (bool(checks) and "pending" not in buckets)
            if terminal:
                publish("terminal", projection, "pull request closed" if after["state"] == "closed"
                        else "native failed/cancelled check observed" if failed else "native checks no longer pending")
                return 2 if failed or after["state"] == "closed" else 0
            if changed:
                publish("initial" if previous is None else "revision", projection,
                        "no checks observed; completeness and readiness remain unknown" if not checks else None)
                previous = fingerprint
            sleep(min(WATCH_INTERVAL_SECONDS, max(0, deadline - WATCH_CLEANUP_SECONDS - clock())))
    except WatchDeadline as error:
        publish("deadline", reason=str(error))
        return 3
    except KeyboardInterrupt:
        publish("observation-failure", reason="caller cancelled watch; no retry or mutation")
        return 130
    except (OSError, RuntimeError) as error:
        # Reserve a tiny final failure even if prior material output filled its cap.
        publish("observation-failure", reason=str(error)[:1024])
        return 3


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
    result.add_argument("--telemetry-config", help="private host telemetry configuration; otherwise use the canonical discovery order")
    result.add_argument("--telemetry-feature", help="stable feature identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-item", help="stable item identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-attempt", help="stable attempt identity for a discovered telemetry assignment")
    result.add_argument("--telemetry-parent-attempt", help="optional stable parent attempt identity")
    result.add_argument("--telemetry-engine", help="installed telemetry-capable coordination engine; overrides host configuration")
    result.add_argument("--telemetry-publisher-event", action="store_true",
                        help="one advisory dashboard refresh after completed final CI observation")
    result.add_argument("--telemetry-advisory-deadline-monotonic", type=float,
                        help="caller-owned absolute monotonic deadline including direct-child cleanup; no default")
    result.add_argument("--watch-checks", action="store_true", help="read-only bounded native-check watch; never grants apply")
    result.add_argument("--watch-seconds", type=int, help="required watch budget, 1..600 seconds")
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
    deadline = args.telemetry_advisory_deadline_monotonic
    if deadline is not None and (not math.isfinite(deadline) or deadline <= 0):
        parser().error("--telemetry-advisory-deadline-monotonic must be finite and positive")
    if args.watch_checks and (args.telemetry_publisher_event or deadline is not None):
        parser().error("advisory event/deadline options are incompatible with --watch-checks")
    if args.watch_checks:
        if len(args.repo) > 255:
            parser().error("watch repository identity exceeds 255 characters")
        if args.apply:
            parser().error("--watch-checks is incompatible with --apply; apply requires a fresh canonical recheck")
        if args.watch_seconds is None or not 1 <= args.watch_seconds <= WATCH_MAX_SECONDS:
            parser().error("--watch-checks requires --watch-seconds between 1 and 600")
        return watch_checks(args.repo, args.pr, args.head, args.watch_seconds)
    if args.watch_seconds is not None:
        parser().error("--watch-seconds requires --watch-checks")
    if args.telemetry_store_root and not args.telemetry_assignment:
        parser().error("--telemetry-store-root requires --telemetry-assignment")
    identity_values = [args.telemetry_feature, args.telemetry_item, args.telemetry_attempt]
    if any(identity_values) and not all(identity_values):
        parser().error("--telemetry-feature, --telemetry-item and --telemetry-attempt must be supplied together")
    observer = None
    observation_health: list[str] = []
    assignment, store_root = args.telemetry_assignment, args.telemetry_store_root
    config_path, telemetry_repository = args.telemetry_config, args.repo
    workspace_transport = bool(config_path and not store_root)
    engine = args.telemetry_engine or "fsgg-coord-engine"
    if not assignment and not store_root:
        try:
            config = discover_telemetry_config(args.telemetry_config, command_engine=engine)
            if config is not None:
                engine = args.telemetry_engine or config.engine
                workspace_transport = config.workspace
                config_path = config.path
                telemetry_repository = config.repository or args.repo
                if all(identity_values):
                    assignment = create_ci_assignment(
                        config,
                        feature=args.telemetry_feature, item=args.telemetry_item,
                        attempt=args.telemetry_attempt, parent_attempt=args.telemetry_parent_attempt,
                        command_engine=args.telemetry_engine or "fsgg-coord-engine",
                    )
                    if not config.workspace:
                        store_root = config.store_root
                else:
                    observation_health.append("unavailable")
                    print("fsgg routine telemetry: host is configured but feature/item/attempt identities are missing", file=sys.stderr)
        except (OSError, RuntimeError) as error:
            observation_health.append("unavailable")
            print(f"fsgg routine telemetry: host configuration unavailable: {error}", file=sys.stderr)
    if assignment and (store_root or config_path or not args.telemetry_store_root):
        def observer(summary: Summary) -> None:
            options = {"assignment": assignment, "store_root": store_root, "engine": engine}
            if workspace_transport:
                options.update({"config": config_path, "repository": telemetry_repository})
            if deadline is not None:
                options["deadline"] = deadline
            observation_health.append(observe_candidate(summary, **options))
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
    dashboard_health = "not-run-final-ci-unavailable"
    if observer is not None and result.outcome != "ready":
        observer(result)
        if args.telemetry_publisher_event and observation_health[-1] != "unavailable":
            dashboard_health = observe_dashboard_event(engine=engine, config=config_path, deadline=deadline)
    if observer is not None or observation_health:
        result = replace(result, telemetryHealth=observation_health[-1] if observation_health else "unavailable")
    output = asdict(result)
    if args.telemetry_publisher_event:
        output["dashboardPublicationHealth"] = dashboard_health
    print(json.dumps(output, separators=(",", ":"), sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

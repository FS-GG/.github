#!/usr/bin/env python3
"""Inspect one routine PR and report its native delivery result exactly once."""

from __future__ import annotations

import argparse
import base64
import math
import hashlib
import importlib.util
import io
import json
import pathlib
import os
import selectors
import stat
import time
import re
import subprocess
import sys
import tempfile
import zipfile
from dataclasses import asdict, dataclass, fields, replace
from datetime import datetime, timedelta, timezone
from typing import Any, Callable, Protocol

sys.dont_write_bytecode = True

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
MAX_SELECTION_ARCHIVE_BYTES = 1_048_576
MAX_SELECTION_UNCOMPRESSED_BYTES = 1_048_576
MAX_SELECTION_ENTRIES = 128
MAX_SELECTION_BYTES = 65_536
PRODUCER_REPOSITORY = "FS-GG/.github"
PRODUCER_PATHS = (
    ".fsgg/routine-development.json", ".github/workflows/routine-eligibility.yml",
    "scripts/check-claim-generation.py", "scripts/check-routine-eligibility-envelope.py",
    "scripts/lib/gate.py",
)


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

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None: ...

    def installation_contract(self, repo: str, number: int, pr: dict, head: str,
                              source: str, *, delivered: bool = False) -> None: ...


class GhApi:
    def installation_read(self, endpoint: str) -> Any:
        """Bounded existing transport; permission/transport errors never mean absence."""
        code, raw = bounded_watch_query(["gh", "api", endpoint], time.monotonic() + 31,
                                       max_bytes=1_048_576)
        if code != 0:
            raise RuntimeError("routine producer native identity read failed")
        try:
            return json.loads(raw)
        except (UnicodeDecodeError, ValueError) as error:
            raise RuntimeError("routine producer native read is malformed") from error

    def installation_tree(self, repo: str, commit: str) -> dict[str, dict]:
        value = self.installation_read(f"repos/{repo}/git/commits/{commit}")
        if not isinstance(value, dict) or value.get("sha") != commit:
            raise RuntimeError("installation commit identity differs")
        tree_sha = (value.get("tree") or {}).get("sha")
        if not isinstance(tree_sha, str) or not SHA_RE.fullmatch(tree_sha):
            raise RuntimeError("installation tree identity is missing")
        tree = self.installation_read(f"repos/{repo}/git/trees/{tree_sha}?recursive=1")
        if (not isinstance(tree, dict) or tree.get("sha") != tree_sha
                or tree.get("truncated") is not False or not isinstance(tree.get("tree"), list)
                or len(tree["tree"]) > 10000):
            raise RuntimeError("installation tree population is incomplete")
        entries = {}
        for row in tree["tree"]:
            if (not isinstance(row, dict) or not isinstance(row.get("path"), str)
                    or row["path"] in entries or not row["path"]
                    or any(p in ("", ".", "..") for p in row["path"].split("/"))
                    or not isinstance(row.get("sha"), str) or not SHA_RE.fullmatch(row["sha"])
                    or row.get("type") not in ("tree", "blob", "commit")
                    or row.get("mode") not in {"tree": ("040000",), "blob": ("100644", "100755", "120000"), "commit": ("160000",)}[row["type"]]):
                raise RuntimeError("installation tree entry is malformed")
            entries[row["path"]] = row
        return entries

    def installation_blob(self, repo: str, row: dict) -> bytes:
        if row.get("type") != "blob" or row.get("mode") not in ("100644", "100755"):
            raise RuntimeError("installation payload is not a regular canonical file")
        value = self.installation_read(f"repos/{repo}/git/blobs/{row['sha']}")
        if (not isinstance(value, dict) or value.get("sha") != row["sha"]
                or value.get("encoding") != "base64" or type(value.get("size")) is not int
                or not 0 < value["size"] <= 131072 or not isinstance(value.get("content"), str)):
            raise RuntimeError("installation blob identity/encoding is malformed")
        try:
            body = base64.b64decode(value["content"].replace("\n", ""), validate=True)
        except ValueError as error:
            raise RuntimeError("installation blob encoding is malformed") from error
        if (len(body) != value["size"] or hashlib.sha1(
                f"blob {len(body)}\0".encode() + body).hexdigest() != row["sha"]):
            raise RuntimeError("installation blob bytes differ from Git identity")
        return body

    def installation_contract(self, repo: str, number: int, pr: dict, head: str,
                              source: str, *, delivered: bool = False) -> None:
        try:
            self._installation_contract(repo, number, pr, head, source, delivered=delivered)
        except (KeyError, TypeError, ValueError, AttributeError, IndexError) as error:
            raise RuntimeError("installation native response shape is malformed") from error

    def _installation_contract(self, repo: str, number: int, pr: dict, head: str,
                               source: str, *, delivered: bool = False) -> None:
        """Closed five-file predicate, independent of ordinary eligibility success."""
        metadata = self.installation_read(f"repos/{repo}")
        if (not isinstance(metadata, dict) or metadata.get("full_name") != repo
                or type(metadata.get("id")) is not int or metadata["id"] < 1
                or not isinstance(metadata.get("default_branch"), str)):
            raise RuntimeError("installation repository identity is unestablished")
        base_ref, base = base_of(pr)
        if (base_ref != metadata["default_branch"] or base is None
                or head_of(pr) != head or pr.get("changed_files") != 5
                or pr.get("draft") is not False
                or pr.get("number") != number or (pr.get("base") or {}).get("repo", {}).get("id") != metadata["id"]
                or type((pr.get("base") or {}).get("repo", {}).get("id")) is not int
                or type((pr.get("head") or {}).get("repo", {}).get("id")) is not int
                or (pr.get("head") or {}).get("repo", {}).get("id") != metadata["id"]
                or (pr.get("base") or {}).get("repo", {}).get("full_name") != repo
                or (pr.get("head") or {}).get("repo", {}).get("full_name") != repo):
            raise RuntimeError("installation PR/default/head/repository identity differs; forks refuse")
        # Reuse the owning parser; a distinct installation route still requires source-change.
        path = pathlib.Path(__file__).with_name("pr-lane-admission.py")
        spec = importlib.util.spec_from_file_location("installation_lane_body", path)
        module = importlib.util.module_from_spec(spec); assert spec and spec.loader
        spec.loader.exec_module(module)
        try:
            valid = module.validate_routine_body(pr.get("body"), pr["head"]["ref"], head)
            markers = module.routine_validator().marker_fields(module.routine_validator().ROUTINE_MARKER_RE, pr["body"])
            if not valid or markers[0].get("operation") != "source-change":
                raise RuntimeError("installation requires the exact source-change routine marker")
        except (module.ToolError, ValueError, KeyError, IndexError) as error:
            raise RuntimeError("installation routine marker is invalid") from error
        default = self.installation_read(f"repos/{repo}/git/ref/heads/{base_ref}")
        current = (default.get("object") or {}).get("sha") if isinstance(default, dict) else None
        if not delivered and current != base:
            raise RuntimeError("installation protected base moved")
        owner = self.installation_read(f"repos/{PRODUCER_REPOSITORY}")
        if (not isinstance(owner, dict) or owner.get("full_name") != PRODUCER_REPOSITORY
                or owner.get("id") != 1269292704 or owner.get("default_branch") != "main"):
            raise RuntimeError("canonical installation producer repository differs")
        lineage = self.installation_read(f"repos/{PRODUCER_REPOSITORY}/compare/{source}...main")
        if (not isinstance(lineage, dict) or (lineage.get("merge_base_commit") or {}).get("sha") != source
                or lineage.get("status") not in ("identical", "ahead")):
            raise RuntimeError("installation producer is not a protected-main ancestor")
        canonical = self.installation_tree(PRODUCER_REPOSITORY, source)
        owner_head = (lineage.get("head_commit") or {}).get("sha")
        if not isinstance(owner_head, str) or not SHA_RE.fullmatch(owner_head):
            raise RuntimeError("current protected producer identity is missing")
        current_owner = self.installation_tree(PRODUCER_REPOSITORY, owner_head)
        inventory_row = current_owner.get("src/FS.GG.Drivers/workspace-inventory.json")
        if inventory_row is None:
            raise RuntimeError("maintained driver workspace inventory is missing")
        inventory = json.loads(self.installation_blob(PRODUCER_REPOSITORY, inventory_row))
        if (not isinstance(inventory, dict) or inventory.get("schema") != "fsgg/driver-workspace-inventory/v1"
                or not isinstance(inventory.get("files"), list)
                or any(sum(isinstance(row, dict) and row.get("source") == path and row.get("path") == path
                           for row in inventory["files"]) != 1 for path in PRODUCER_PATHS)):
            raise RuntimeError("first-install closure differs from maintained workspace inventory")
        for path in PRODUCER_PATHS:
            if (path not in canonical or path not in current_owner
                    or any(canonical[path].get(k) != current_owner[path].get(k) for k in ("sha", "mode", "type"))):
                raise RuntimeError("selected producer payload is obsolete at protected main")
        helper = current_owner.get("tools/routine-delivery.py")
        if helper is None or self.installation_blob(PRODUCER_REPOSITORY, helper) != pathlib.Path(__file__).read_bytes():
            raise RuntimeError("running installation helper differs from current protected producer")
        old = self.installation_tree(repo, base)
        candidate = self.installation_tree(repo, head)
        if any(path in old for path in PRODUCER_PATHS):
            raise RuntimeError("producer dependencies already or partially installed")
        leaves = lambda tree: {p: (r.get("mode"), r.get("type"), r.get("sha"))
                               for p, r in tree.items() if r.get("type") != "tree"}
        a, b = leaves(old), leaves(candidate)
        changed = {p for p in a.keys() | b.keys() if a.get(p) != b.get(p)}
        if changed != set(PRODUCER_PATHS):
            raise RuntimeError("installation must add exactly the canonical five files")
        files = self.installation_read(f"repos/{repo}/pulls/{number}/files?per_page=100")
        if (not isinstance(files, list) or len(files) != 5
                or any(not isinstance(r, dict) or r.get("status") != "added"
                       or r.get("previous_filename") is not None for r in files)
                or {r.get("filename") for r in files} != set(PRODUCER_PATHS)
                or any(r.get("sha") != candidate[r["filename"]]["sha"] for r in files)):
            raise RuntimeError("installation file-list population/status/identity differs")
        for path in PRODUCER_PATHS:
            if path not in canonical or path not in candidate or leaves(candidate)[path] != leaves(canonical).get(path):
                raise RuntimeError("installation payload mode/content differs from protected producer")
            self.installation_blob(PRODUCER_REPOSITORY, canonical[path])
            self.installation_blob(repo, candidate[path])
        if delivered:
            merge = merged_commit_of(pr)
            if not merge or not isinstance(current, str) or not SHA_RE.fullmatch(current):
                raise RuntimeError("installation protected merge identity is unknown")
            merged_tree = leaves(self.installation_tree(repo, merge))
            protected_tree = leaves(self.installation_tree(repo, current))
            if any(merged_tree.get(p) != b[p] or protected_tree.get(p) != b[p] for p in PRODUCER_PATHS):
                raise RuntimeError("installation protected payload readback differs")
            ancestry = self.installation_read(f"repos/{repo}/compare/{merge}...{current}")
            if ((ancestry.get("merge_base_commit") or {}).get("sha") != merge
                    or ancestry.get("status") not in ("identical", "ahead")):
                raise RuntimeError("installation protected payload/ancestry readback differs")
            return
        # Full native listings, not a workflow404 (which could hide denied metadata).
        workflows = self.installation_read(f"repos/{repo}/actions/workflows?per_page=100")
        checks = self.installation_read(f"repos/{repo}/commits/{head}/check-runs?per_page=100")
        for value, key in ((workflows, "workflows"), (checks, "check_runs")):
            if (not isinstance(value, dict) or type(value.get("total_count")) is not int
                    or not 0 <= value["total_count"] <= 100 or not isinstance(value.get(key), list)
                    or len(value[key]) != value["total_count"] or any(not isinstance(r, dict) for r in value[key])):
                raise RuntimeError("installation native producer population is incomplete")
        if (any(r.get("path") == PRODUCER_PATHS[1] or r.get("name") == "routine-eligibility" for r in workflows["workflows"])
                or any(r.get("name") == "routine-eligibility" for r in checks["check_runs"])):
            raise RuntimeError("native eligibility producer/check already exists")
        self.installation_required_checks(repo, number, head, checks["check_runs"])

    def installation_required_checks(self, repo: str, number: int, head: str, roster: list[dict]) -> None:
        # The existing native checks reader remains the receiver-specific requirement authority.
        api = WatchGhApi(time.monotonic() + 31)
        _code, checks = api._watch_read(["gh", "pr", "checks", str(number), "--repo", repo,
                                      "--required", "--json", WATCH_FIELDS], (0,))
        if (not isinstance(checks, list) or not 0 < len(checks) <= WATCH_MAX_CHECKS
                or any(not isinstance(r, dict) or set(r) != set(WATCH_FIELDS.split(",")) or r.get("bucket") != "pass"
                       or r.get("state") != "SUCCESS" or not r.get("completedAt") for r in checks)):
            raise RuntimeError("installation required checks are absent, pending or failed")
        # Native links establish exact-head provenance instead of trusting a green name.
        for row in checks:
            match = re.fullmatch(rf"https://github\.com/{re.escape(repo)}/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)", row.get("link", ""))
            if not match:
                raise RuntimeError("installation required check provenance is unestablished")
            run_id, job_id = match.groups()
            native = [check for check in roster if check.get("id") == int(job_id)]
            if (len(native) != 1 or native[0].get("name") != row["name"]
                    or native[0].get("details_url") != row["link"]
                    or (native[0].get("app") or {}).get("id") != 15368
                    or native[0].get("head_sha") != head or native[0].get("status") != "completed"
                    or native[0].get("conclusion") != "success"):
                raise RuntimeError("installation required check differs from complete native roster")
            run = self.installation_read(f"repos/{repo}/actions/runs/{run_id}")
            job = self.installation_read(f"repos/{repo}/actions/jobs/{job_id}")
            if (run.get("id") != int(run_id) or run.get("head_sha") != head
                    or (run.get("repository") or {}).get("full_name") != repo
                    or run.get("status") != "completed" or run.get("conclusion") != "success"
                    or job.get("id") != int(job_id) or job.get("run_id") != int(run_id)
                    or job.get("head_sha") != head or job.get("name") != row.get("name")
                    or job.get("status") != "completed" or job.get("conclusion") != "success"
                    or type((native[0].get("check_suite") or {}).get("id")) is not int
                    or (native[0].get("check_suite") or {}).get("id") != run.get("check_suite_id")
                    or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job_id}"
                    or type(run.get("run_attempt")) is not int or run["run_attempt"] < 1
                    or type(job.get("run_attempt")) is not int
                    or job.get("run_attempt") != run["run_attempt"]):
                raise RuntimeError("installation required check exact-head/run/attempt differs")

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

    def routine_eligibility(self, repo: str, head: str, branch: str) -> None:
        """Require a complete bounded roster and authenticate every named producer."""
        value = self._run([f"repos/{repo}/commits/{head}/check-runs?per_page=100"])
        count = value.get("total_count") if isinstance(value, dict) else None
        checks = value.get("check_runs") if isinstance(value, dict) else None
        if (type(count) is not int or not 0 <= count <= 100 or not isinstance(checks, list)
                or len(checks) != count or any(not isinstance(check, dict) for check in checks)):
            raise RuntimeError("routine eligibility check population is incomplete")
        matches = [check for check in checks if check.get("name") == "routine-eligibility"]
        if not matches or len({check.get("id") for check in matches}) != len(matches):
            raise RuntimeError("routine eligibility check is missing or duplicated")
        workflow = self._run([f"repos/{repo}/actions/workflows/routine-eligibility.yml"])
        workflow_id = workflow.get("id") if isinstance(workflow, dict) else None
        if (type(workflow_id) is not int or workflow_id < 1
                or workflow.get("path") != ".github/workflows/routine-eligibility.yml"
                or (repo == "FS-GG/.github" and workflow_id != 352355034)):
            raise RuntimeError("routine eligibility producer identity is unestablished")
        native = {}
        for check in matches:
            if not isinstance(check.get("details_url"), str) or not isinstance(check.get("app"), dict) or not isinstance(check.get("check_suite"), dict):
                raise RuntimeError("routine eligibility check identity is malformed")
            match = re.fullmatch(rf"https://github\.com/{re.escape(repo)}/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)",
                                 check.get("details_url", ""))
            if (match is None or check.get("app", {}).get("id") != 15368
                    or check.get("head_sha") != head):
                raise RuntimeError("routine eligibility has no valid native producer")
            run_id, job_id = map(int, match.groups())
            suite = check.get("check_suite", {}).get("id")
            run = self._run([f"repos/{repo}/actions/runs/{run_id}"])
            job = self._run([f"repos/{repo}/actions/jobs/{job_id}"])
            if (type(suite) is not int or suite < 1 or check.get("id") != job_id
                    or not isinstance(run, dict) or not isinstance(job, dict)
                    or run.get("id") != run_id or run.get("workflow_id") != workflow_id
                    or run.get("path") != workflow["path"] or run.get("event") != "pull_request_target"
                    or run.get("head_sha") != head or run.get("check_suite_id") != suite
                    or not isinstance(run.get("repository"), dict) or run["repository"].get("full_name") != repo
                    or type(run.get("run_attempt")) is not int or run["run_attempt"] < 1
                    or job.get("id") != job_id or job.get("run_id") != run_id
                    or job.get("head_sha") != head or job.get("name") != "routine-eligibility"
                    or job.get("check_run_url") != f"https://api.github.com/repos/{repo}/check-runs/{job_id}"
                    or type(job.get("run_attempt")) is not int
                    or not 1 <= job["run_attempt"] <= run["run_attempt"]
                    or job.get("status") != check.get("status") or job.get("conclusion") != check.get("conclusion")):
                raise RuntimeError("routine eligibility run/job/suite/attempt provenance differs")
            native[job_id] = (run, job)
        try:
            newest = max(matches, key=lambda check: (datetime.fromisoformat(check["started_at"].replace("Z", "+00:00")), check["id"]))
        except (KeyError, TypeError, ValueError) as error:
            raise RuntimeError("routine eligibility ordering identity is malformed") from error
        if newest.get("status") != "completed" or newest.get("conclusion") != "success":
            raise RuntimeError("latest routine eligibility check is not successful")
        run, job = native[newest["id"]]
        if (job["run_attempt"] != run["run_attempt"] or run.get("status") != "completed"
                or run.get("conclusion") != "success" or run.get("head_branch") != branch):
            raise RuntimeError("selected routine eligibility is not the successful current run attempt/branch")

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
    routineEligibility: str = "not-required"
    producerInstallation: str = "not-selected"
    producerSource: str | None = None


def installation_prior_result(path: str) -> dict:
    """Existing retained Summary is a restriction, never an installation grant."""
    target = pathlib.Path(path)
    if not target.is_absolute() or any(p.is_symlink() for p in (target, *target.parents)):
        raise RuntimeError("installation prior result must be a regular absolute unlinked path")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise RuntimeError("installation prior result has duplicate fields")
            result[key] = value
        return result
    try:
        descriptor = os.open(target, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
        try:
            if not stat.S_ISREG(os.fstat(descriptor).st_mode):
                raise RuntimeError("installation prior result must be a regular file")
            with os.fdopen(descriptor, "rb", closefd=False) as stream:
                raw = stream.read(65537)
        finally:
            os.close(descriptor)
        if len(raw) > 65536:
            raise RuntimeError("installation prior result exceeds byte bound")
        value = json.loads(raw, object_pairs_hook=unique)
    except (OSError, ValueError) as error:
        raise RuntimeError("installation prior result is unreadable or malformed") from error
    if not isinstance(value, dict) or set(value) != {f.name for f in fields(Summary)}:
        raise RuntimeError("installation prior result schema fields differ")
    return value


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
    reason = "unknown"
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
            child_options = {}
            if store_root is not None:
                child_environment = os.environ.copy()
                child_environment.pop("FSGG_TELEMETRY_CONFIG", None)
                child_options["env"] = child_environment
            if deadline is None:
                completed = runner(
                    command, check=False, capture_output=True, text=True, timeout=35,
                    **child_options,
                )
            else:
                code, raw = bounded_watch_query(
                    command, deadline, clock=clock, max_seconds=35, **child_options,
                )
                completed = subprocess.CompletedProcess(command, code, raw.decode("utf-8"), "")
        if completed.returncode != 0:
            # Preserve a useful bounded diagnostic without forwarding provider
            # text, credentials, arguments or private paths to the caller.
            reason = f"reconcile-exit-{completed.returncode}"
            safe_reasons = {
                "first CI population admission requires ready, not-delivered, and an explicit matching observed head": "prospective-ci-admission-missing",
                "GITHUB_TOKEN or GH_TOKEN is required": "provider-credential-unavailable",
                "workspace config and legacy store root cannot both be selected": "destination-selection-conflict",
                "configuration-version": "configuration-version",
            }
            for message, code in safe_reasons.items():
                if any(line.removeprefix("fsgg-coord-engine: telemetry ci: ").strip() == message
                       for line in completed.stderr[:8192].splitlines()):
                    reason += ":" + code
                    break
        if completed.returncode == 0:
            try:
                result = json.loads(completed.stdout)
                health = result.get("driverHealth")
                if health in {"complete", "open", "pending", "missing-outcome"}:
                    return health
            except (json.JSONDecodeError, AttributeError):
                pass
            return "pending"
    except (OSError, subprocess.SubprocessError, RuntimeError, UnicodeDecodeError) as error:
        reason = "helper-" + type(error).__name__
    print(f"fsgg routine telemetry: CI observation unavailable ({reason}); native delivery is unchanged", file=sys.stderr)
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


def routine_contract(api: NativeApi, repo: str, pr: dict[str, Any], head: str) -> str:
    path = pathlib.Path(__file__).with_name("pr-lane-admission.py")
    spec = importlib.util.spec_from_file_location("routine_lane_body", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    try:
        routine = module.validate_routine_body(pr.get("body"), (pr.get("head") or {}).get("ref"), head)
    except (module.ToolError, ValueError) as error:
        raise RuntimeError(str(error)) from error
    if not routine:
        return "not-required"
    api.routine_eligibility(repo, head, pr["head"]["ref"])
    return "passed"


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
    producer_source: str | None = None,
    installation_prior: dict | None = None,
) -> tuple[int, Summary]:
    if producer_source is not None and (publication_required or not coherent_workflow
                                        or not SHA_RE.fullmatch(producer_source)):
        raise RuntimeError("producer installation requires a pinned source, completed coherent workflow and no publication")
    if installation_prior is not None:
        if (producer_source is None or not isinstance(installation_prior, dict)
                or set(installation_prior) != {f.name for f in fields(Summary)}
                or installation_prior.get("schema") != "fsgg.routine-delivery/v1"
                or installation_prior.get("repo") != repo or installation_prior.get("pr") != pr_number
                or type(installation_prior.get("pr")) is not int
                or installation_prior.get("expectedHead") != expected_head
                or installation_prior.get("producerSource") != producer_source
                or type(installation_prior.get("attempts")) is not int
                or not 0 <= installation_prior["attempts"] <= 1
                or installation_prior.get("routineEligibility") not in ("unavailable-first-install", "unresolved")
                or installation_prior.get("producerInstallation") not in (
                    "ready", "refused", "indeterminate", "delivered-activation-pending", "disputed")
                or installation_prior.get("outcome") not in ("ready", "refused", "indeterminate", "delivered", "delivered-disputed")
                or installation_prior.get("codeDelivery") not in ("not-delivered", "unknown", "delivered")
                or installation_prior.get("publication") != "not-required"
                or not isinstance(installation_prior.get("baseSha"), str)
                or not SHA_RE.fullmatch(installation_prior["baseSha"])
                or not isinstance(installation_prior.get("baseRef"), str) or not installation_prior["baseRef"]):
            raise RuntimeError("installation prior result identity/attempt/schema differs")
    publication = "pending" if publication_required else "not-required"
    before = api.get_pr(repo, pr_number)
    if not isinstance(before, dict):
        raise RuntimeError("native pull request readback is not an object")
    base_ref, base_sha = base_of(before)
    routine_state = "unobserved"
    def bound(*values: Any, native: dict[str, Any] = before) -> Summary:
        now = datetime.now(timezone.utc)
        outcome_at = outcome_time_of(native)
        if outcome_at:
            parsed_outcome = datetime.fromisoformat(outcome_at.replace("Z", "+00:00"))
            if parsed_outcome > now:
                now = parsed_outcome
        result = Summary(*values, baseRef=base_ref, baseSha=base_sha,
                       outcomeAt=outcome_at,
                       observedAt=now.isoformat().replace("+00:00", "Z"),
                       routineEligibility=routine_state)
        if producer_source is not None:
            state = "ready" if result.outcome == "ready" else result.outcome
            if result.codeDelivery == "delivered":
                try:
                    api.installation_contract(repo, pr_number, native, expected_head, producer_source, delivered=True)
                except RuntimeError as error:
                    # Source merge history survives, but installation is not accepted.
                    result = replace(result, outcome="delivered-disputed", reason=str(error))
                    state = "disputed"
                else:
                    state = "delivered-activation-pending" if result.outcome == "delivered" else "disputed"
            result = replace(result, producerInstallation=state, producerSource=producer_source)
        if installation_prior is not None:
            result = replace(result, attempts=max(result.attempts, installation_prior["attempts"]))
        return result
    allowed, reason = eligible(before, expected_head)
    observed = head_of(before)
    if installation_prior is not None and base_of(before) != (installation_prior["baseRef"], installation_prior["baseSha"]):
        raise RuntimeError("installation prior result original base differs")
    if installation_prior is not None and installation_prior["attempts"] > 0 and not is_merged(before):
        # Even an open PR does not settle a lost response or authorize another write.
        routine_state = "unresolved"
        return 3, bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "indeterminate", "unknown", publication, None, 0,
            "original installation effect remains unresolved; prior result forces readback-only",
            "current", "unobserved",
        )
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
    routine_reason = None
    try:
        if producer_source is not None:
            api.installation_contract(repo, pr_number, before, expected_head, producer_source,
                                      delivered=is_merged(before))
            routine_state = "unavailable-first-install"
        else:
            routine_state = routine_contract(api, repo, before, expected_head)
    except RuntimeError as error:
        routine_state, routine_reason = "unresolved", str(error)
        if not is_merged(before):
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, routine_reason, "current", "unobserved",
            )
    disposition, coherent = "current", "not-required"
    if coherent_workflow:
        disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
        if not is_merged(before) and (disposition in {"invalid", "deferred", "failed"}
                                      or (coherent != "passed" and (producer_source is not None or disposition != "reused"))):
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
                "refused", "not-delivered", publication, None, 0, reason,
                disposition, coherent,
            )
    if is_merged(before):
        coherent_disputed = coherent == "failed" or disposition in {"invalid", "deferred", "failed"}
        disputed = coherent_disputed or routine_state == "unresolved"
        merged_result = bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "delivered-disputed" if disputed else "delivered", "delivered",
            publication, merged_commit_of(before), 0, routine_reason or (reason if disputed else None), disposition,
            "disputed" if coherent_disputed else coherent,
        )
        return 4 if merged_result.outcome == "delivered-disputed" else 0, merged_result
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

    if routine_state in ("passed", "unavailable-first-install"):
        current = None
        try:
            current = api.get_pr(repo, pr_number)
            current_allowed, current_reason = eligible(current, expected_head)
            if (not current_allowed or is_merged(current) or base_of(current) != base_of(before)
                    or current.get("body") != before.get("body")
                    or (current.get("head") or {}).get("ref") != (before.get("head") or {}).get("ref")):
                raise RuntimeError(current_reason or "routine PR envelope changed before merge")
            if producer_source is not None:
                api.installation_contract(repo, pr_number, current, expected_head, producer_source)
                disposition, coherent, reason = validation_state(api, repo, coherent_workflow, expected_head)
                if coherent != "passed" or disposition in ("invalid", "deferred", "failed"):
                    raise RuntimeError("installation coherent validation changed before merge")
            else:
                routine_state = routine_contract(api, repo, current, expected_head)
                if routine_state != "passed":
                    raise RuntimeError("routine route changed before merge")
        except RuntimeError as error:
            routine_state = "unresolved"
            return 2, bound(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(current) if isinstance(current, dict) else None,
                "refused", "not-delivered", publication, None, 0, str(error), disposition, coherent,
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
        delivered_result = bound(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
            "delivered", "delivered", publication, merge_commit, attempts, None,
            disposition, coherent, native=after,
        )
        return 4 if delivered_result.outcome == "delivered-disputed" else 0, delivered_result
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
    env: dict[str, str] | None = None,
) -> tuple[int, bytes]:
    """Bound both streams before retaining; kill/reap the same direct child on error."""
    work_end = min(deadline - WATCH_CLEANUP_SECONDS, clock() + max_seconds)
    if work_end <= clock():
        raise WatchDeadline("original watch cleanup reserve reached")
    process_options = {} if env is None else {"env": env}
    process = popen(command, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE, close_fds=True, **process_options)
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
    result.add_argument("--telemetry-publisher-config",
                        help="absolute dashboard projection config; defaults to FSGG_TELEMETRY_DASHBOARD_CONFIG or the telemetry config")
    result.add_argument("--telemetry-advisory-deadline-monotonic", type=float,
                        help="caller-owned absolute monotonic deadline including direct-child cleanup; no default")
    result.add_argument("--watch-checks", action="store_true", help="read-only bounded native-check watch; never grants apply")
    result.add_argument("--watch-seconds", type=int, help="required watch budget, 1..600 seconds")
    result.add_argument("--apply", action="store_true")
    result.add_argument("--install-routine-producer", action="store_true",
                        help="closed first installation only; never ordinary eligibility success")
    result.add_argument("--routine-producer-source", help="immutable protected FS-GG/.github template commit")
    result.add_argument("--installation-prior-result", help="retained installation Summary; prior effects force readback-only")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    if not re.fullmatch(r"[^/\s]+/[^/\s]+", args.repo):
        parser().error("--repo must be OWNER/REPO")
    if args.pr <= 0:
        parser().error("--pr must be positive")
    if not SHA_RE.fullmatch(args.head):
        parser().error("--head must be a lowercase 40-hex commit SHA")
    if args.install_routine_producer:
        if (args.watch_checks or args.publication != "none" or not args.coherent_workflow
                or not args.routine_producer_source or not SHA_RE.fullmatch(args.routine_producer_source)):
            parser().error("--install-routine-producer requires --routine-producer-source, --coherent-workflow and publication none; no watch")
    elif args.routine_producer_source is not None or args.installation_prior_result is not None:
        parser().error("producer source/prior result requires --install-routine-producer")
    try:
        prior = installation_prior_result(args.installation_prior_result) if args.installation_prior_result else None
    except RuntimeError as error:
        parser().error(str(error))
    deadline = args.telemetry_advisory_deadline_monotonic
    if deadline is not None and (not math.isfinite(deadline) or deadline <= 0):
        parser().error("--telemetry-advisory-deadline-monotonic must be finite and positive")
    if args.watch_checks and (args.telemetry_publisher_event or args.telemetry_publisher_config is not None or deadline is not None):
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
            producer_source=args.routine_producer_source if args.install_routine_producer else None,
            installation_prior=prior,
        )
    except (RuntimeError, AmbiguousWrite) as error:
        code = 3
        result = Summary(
            "fsgg.routine-delivery/v1", args.repo, args.pr, args.head, None,
            "indeterminate", "unknown",
            "pending" if args.publication == "required" else "not-required",
            None, prior["attempts"] if prior is not None and type(prior.get("attempts")) is int and prior["attempts"] == 1 else 0,
            str(error), "current", "unobserved",
        )
        if args.install_routine_producer:
            joined = prior is not None and all(prior.get(key) == value for key, value in (
                ("repo", args.repo), ("pr", args.pr), ("expectedHead", args.head),
                ("producerSource", args.routine_producer_source)))
            result = replace(result, producerInstallation="indeterminate",
                producerSource=args.routine_producer_source, routineEligibility="unresolved",
                baseRef=prior.get("baseRef") if joined else None,
                baseSha=prior.get("baseSha") if joined else None)
    dashboard_health = "not-run-final-ci-unavailable"
    if observer is not None and result.outcome != "ready":
        observer(result)
        if args.telemetry_publisher_event and observation_health[-1] != "unavailable":
            projection_config = args.telemetry_publisher_config
            if projection_config is None:
                projection_config = os.environ.get("FSGG_TELEMETRY_DASHBOARD_CONFIG")
            if projection_config is not None and not os.path.isabs(projection_config):
                dashboard_health = "not-run-config-invalid"
            else:
                dashboard_health = observe_dashboard_event(
                    engine=engine, config=config_path if projection_config is None else projection_config,
                    deadline=deadline,
                )
    if observer is not None or observation_health:
        result = replace(result, telemetryHealth=observation_health[-1] if observation_health else "unavailable")
    output = asdict(result)
    if args.telemetry_publisher_event:
        output["dashboardPublicationHealth"] = dashboard_health
    print(json.dumps(output, separators=(",", ":"), sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

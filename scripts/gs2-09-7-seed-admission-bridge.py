#!/usr/bin/env python3
"""Protected Q4 executor bridge to an independently owner-run admission.

The executor publishes sanitized request bytes through upload-artifact. This
module only reads native Actions records. The owner starts the authorizer
workflow separately; this process cannot impersonate that actor or approve it.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time


HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "seed_admission_native_read", HERE / "gs2-09-7-seed-admission-native-read.py")
if SPEC is None or SPEC.loader is None:
    raise RuntimeError("native admission reader unavailable")
reader = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(reader)
authorizer = reader.authorizer
REPO = "repos/FS-GG/.github"
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
ARCHIVE_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
REQUEST_MEMBER = "seed-admission-request.json"
MAX_READ = 1024 * 1024
WAIT_SECONDS = 13 * 60
POLL_SECONDS = 10


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def request_bytes(phase: str, subject: dict) -> bytes:
    authorizer.validate_subject(phase, subject)
    return authorizer.canonical({"schema": authorizer.REQUEST_SCHEMA,
                                 "phase": phase, "subject": subject})


def request_name(phase: str, subject: dict) -> str:
    authorizer.validate_subject(phase, subject)
    return (f'gs2-09-7-seed-admission-request-{phase}-'
            f'{subject["runId"]}-{subject["runAttempt"]}')


def decision_name(phase: str, subject: dict) -> str:
    authorizer.validate_subject(phase, subject)
    return (f'gs2-09-7-seed-admission-decision-{phase}-'
            f'{subject["runId"]}-{subject["runAttempt"]}')


class GitHubReadPort:
    """Use only exact GET routes with the executor's Actions-read token."""

    def __init__(self, token: str):
        require(type(token) is str and len(token) > 20 and token.isascii()
                and not any(char.isspace() for char in token), "actions-read-token")
        self.token = token

    def get(self, route: str, *, limit: int = MAX_READ) -> bytes:
        require(type(route) is str and route.startswith(REPO + "/")
                and ".." not in route and "#" not in route and limit <= MAX_READ,
                "native-read-route")
        try:
            result = subprocess.run(
                ["gh", "api", "-H", "X-GitHub-Api-Version: 2026-03-10", route],
                stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                env={"PATH": os.environ.get("PATH", "/usr/bin:/bin"),
                     "HOME": os.environ.get("HOME", "/tmp"), "GH_TOKEN": self.token},
                timeout=30, check=False)
        except (OSError, subprocess.SubprocessError) as error:
            raise Refused("native-read-unavailable") from error
        require(result.returncode == 0 and 0 < len(result.stdout) <= limit,
                "native-read-unavailable")
        return result.stdout


def get_json(port: GitHubReadPort, route: str) -> object:
    return reader.strict_json(port.get(route))


def artifacts(port: GitHubReadPort, name: str) -> dict:
    require(re.fullmatch(r"gs2-09-7-seed-admission-(?:request|decision)-(?:prepare|final)-[1-9][0-9]*-[1-9][0-9]*",
                         name) is not None, "artifact-name")
    value = get_json(port, f"{REPO}/actions/artifacts?name={name}&per_page=100")
    require(type(value) is dict and type(value.get("total_count")) is int
            and type(value.get("artifacts")) is list
            and value["total_count"] == len(value["artifacts"]),
            "artifact-list")
    return value


def request_artifact(port: GitHubReadPort, phase: str, subject: dict) -> dict:
    name = request_name(phase, subject)
    listing = artifacts(port, name)
    require(listing["total_count"] == 1, "request-artifact-unique")
    item = listing["artifacts"][0]
    binding = item.get("workflow_run") or {}
    require(type(item) is dict and item.get("name") == name
            and type(item.get("id")) is int and item["id"] > 0
            and item.get("expired") is False
            and type(item.get("digest")) is str
            and ARCHIVE_DIGEST.fullmatch(item["digest"]) is not None
            and binding.get("id") == subject["runId"]
            and binding.get("head_sha") == subject["workflowSha"]
            and binding.get("head_branch") == "main"
            and binding.get("repository_id") == authorizer.REPOSITORY_ID
            and binding.get("head_repository_id") == authorizer.REPOSITORY_ID,
            "request-artifact-binding")
    return item


def owner_dispatch_fields(phase: str, subject: dict, artifact: dict) -> dict:
    """Expose exact manual inputs for the independent owner."""
    require(artifact["name"] == request_name(phase, subject), "dispatch-artifact")
    return {"workflow": authorizer.AUTHORIZER_WORKFLOW, "ref": "refs/heads/main",
            "phase": phase, "executor_request_artifact_id": str(artifact["id"]),
            "executor_request_archive_digest": artifact["digest"],
            "executor_run_id": str(subject["runId"]),
            "executor_run_attempt": str(subject["runAttempt"])}


def verified_decision(port: GitHubReadPort, phase: str, subject: dict,
                      producer_sha: str, artifact: dict,
                      listing: dict, now: dt.datetime,
                      request_info: dict) -> object:
    require(type(producer_sha) is str and HEX40.fullmatch(producer_sha) is not None,
            "producer-sha")
    producer_id = (artifact.get("workflow_run") or {}).get("id")
    require(type(producer_id) is int and producer_id > 0, "decision-producer-id")
    producer = get_json(port, f"{REPO}/actions/runs/{producer_id}")
    approvals = get_json(port, f"{REPO}/actions/runs/{producer_id}/approvals")
    environment = get_json(port, f"{REPO}/environments/fleet-v1-admission-owner")
    policies = get_json(port, f"{REPO}/environments/fleet-v1-admission-owner/deployment-branch-policies")
    archive_bytes = port.get(f"{REPO}/actions/artifacts/{artifact['id']}/zip",
                             limit=reader.MAX_ARCHIVE_BYTES)
    with tempfile.TemporaryDirectory(prefix="gs2-seed-decision-") as temporary:
        archive = Path(temporary) / "decision.zip"
        archive.write_bytes(archive_bytes)
        decision = reader.verify_decision(
            phase=phase, expected_subject=subject, expected_producer_sha=producer_sha,
            artifact=artifact, decision_artifacts=listing, producer_run=producer,
            approvals=approvals, environment=environment, policies=policies,
            archive=archive, artifact_id=artifact["id"],
            producer_run_id=producer_id, producer_run_attempt=producer.get("run_attempt"),
            archive_digest=artifact["digest"], now=now)
    request = decision.get("executorRequest") or {}
    require(request.get("artifactId") == request_info["id"]
            and request.get("archiveDigest") == request_info["digest"],
            "decision-request-readback-drift")
    return reader.NativeDecisionPort(decision, phase)


def wait_decision(port: GitHubReadPort, phase: str, subject: dict,
                  producer_sha: str, *, max_seconds: int = WAIT_SECONDS,
                  clock=time.monotonic, pause=time.sleep) -> object:
    authorizer.validate_subject(phase, subject)
    request_info = request_artifact(port, phase, subject)
    require(type(max_seconds) is int and 0 <= max_seconds <= WAIT_SECONDS,
            "decision-wait-bound")
    deadline = clock() + max_seconds
    name = decision_name(phase, subject)
    while True:
        listing = artifacts(port, name)
        require(listing["total_count"] <= 1, "decision-artifact-ambiguous")
        if listing["total_count"] == 1:
            item = listing["artifacts"][0]
            return verified_decision(port, phase, subject, producer_sha, item, listing,
                                     dt.datetime.now(dt.timezone.utc), request_info)
        remaining = deadline - clock()
        require(remaining > 0, "owner-authorization-pending")
        pause(min(POLL_SECONDS, remaining))

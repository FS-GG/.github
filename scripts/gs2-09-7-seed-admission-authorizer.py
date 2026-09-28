#!/usr/bin/env python3
"""Issue one sanitized Q4 seed admission decision from native GitHub evidence."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import zipfile


REPOSITORY = "FS-GG/.github"
REPOSITORY_ID = 1269292704
EXECUTOR_WORKFLOW = ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
AUTHORIZER_WORKFLOW = ".github/workflows/gs2-09-7-seed-admission-authorize.yml"
ENVIRONMENT = "fleet-v1-admission-owner"
ENVIRONMENT_ID = 22582241959
OWNER = {"login": "EHotwagner", "id": 1645484}
TIMER_ACTOR = {"login": "github-actions[bot]", "id": 41898282}
REVIEWER_RULE_ID = 66492942
WAIT_RULE_ID = 66492943
BRANCH_RULE_ID = 66492944
MAIN_POLICY_ID = 60823087
WAIT_MINUTES = 5
TTL_MINUTES = 10
REQUEST_SCHEMA = "fsgg.gs2-09-7.seed-admission-request/1"
FINAL_SCHEMA = "fsgg.gs2-09-7.seed-bootstrap-decision/1"
PREPARE_SCHEMA = "fsgg.gs2-09-7.seed-prepare-decision/1"
FINAL_RESOURCE = "github-actions:FS-GG/.github:seed-admission:final"
PREPARE_RESOURCE = "github-actions:FS-GG/.github:seed-admission:prepare"
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
ARCHIVE_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
MAX_JSON_BYTES = 256 * 1024
FINAL_FIELDS = {
    "workflowRepository", "workflowPath", "environment", "workflowSha",
    "runId", "runAttempt", "candidateSha", "runNonce", "approvedArtifactSourceSha256",
    "sourceManifestSha256", "prestateSha256", "prestateSnapshotSha256",
    "prestateEvidenceSha256", "expectedRefAbsent",
    "sandboxRepositoryId", "sandboxRepositoryNodeId", "projectNodeId", "appId",
    "installationId", "seedPlanSha256", "s2DeclarationSha256", "refName",
    "mintProofSha256", "tokenSha256", "blobOid", "treeOid", "commitOid",
    "expectedOldOid", "operation",
}
PREPARE_FIELDS = {
    "workflowRepository", "workflowPath", "environment", "workflowSha", "candidateSha",
    "runId", "runAttempt", "runNonce", "sourceManifestSha256",
    "approvedArtifactSourceSha256", "sandboxRepositoryId", "sandboxRepositoryNodeId",
    "projectNodeId", "appId", "installationId", "operation",
}


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def unique_pairs(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        require(key not in result, "duplicate-json-member")
        result[key] = value
    return result


def strict_json(raw: bytes) -> object:
    require(type(raw) is bytes and 0 < len(raw) <= MAX_JSON_BYTES, "json-size")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_pairs,
                           parse_constant=lambda _value: (_ for _ in ()).throw(Refused("nonfinite-number")))
    except (UnicodeError, ValueError, TypeError, RecursionError) as error:
        raise Refused("malformed-json") from error
    return value


def canonical(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=True, allow_nan=False) + "\n").encode("ascii")


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def timestamp(value: object, reason: str) -> dt.datetime:
    require(type(value) is str and value.endswith("Z"), reason)
    try:
        parsed = dt.datetime.fromisoformat(value[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused(reason) from error
    require(parsed.tzinfo is not None, reason)
    return parsed


def validate_subject(phase: str, subject: dict) -> None:
    fields = PREPARE_FIELDS if phase == "prepare" else FINAL_FIELDS
    require(type(subject) is dict and set(subject) == fields, f"{phase}-subject-shape")
    require(subject["workflowRepository"] == REPOSITORY
            and subject["workflowPath"] == EXECUTOR_WORKFLOW
            and subject["environment"] == "github-substrate-v2-sandbox"
            and type(subject["workflowSha"]) is str and HEX40.fullmatch(subject["workflowSha"])
            and type(subject["candidateSha"]) is str and HEX40.fullmatch(subject["candidateSha"])
            and type(subject["runId"]) is int and subject["runId"] > 0
            and type(subject["runAttempt"]) is int and subject["runAttempt"] > 0,
            f"{phase}-subject-run")
    nonce = f'{subject["runId"]}-{subject["runAttempt"]}-{subject["candidateSha"]}'
    require(subject["runNonce"] == nonce
            and subject["sandboxRepositoryId"] == 1353050537
            and subject["sandboxRepositoryNodeId"] == "R_kgDOUKXpqQ"
            and subject["projectNodeId"] == "PVT_kwDOEYAWY84BiESo"
            and subject["appId"] == 4166418 and subject["installationId"] == 143110413
            and type(subject["approvedArtifactSourceSha256"]) is str
            and HEX64.fullmatch(subject["approvedArtifactSourceSha256"]),
            f"{phase}-subject-target")
    if phase == "prepare":
        require(subject["operation"] == "prepare-only-no-effect"
                and type(subject["sourceManifestSha256"]) is str
                and HEX64.fullmatch(subject["sourceManifestSha256"])
                and subject["sourceManifestSha256"] ==
                    subject["approvedArtifactSourceSha256"],
                "prepare-credential-only")
    else:
        require(subject["operation"] == "genesis-nonce-seed-journal"
                and subject["refName"] == f"refs/heads/gs2-09-7/{nonce}/seed-journal"
                and subject["expectedOldOid"] is None
                and subject["expectedRefAbsent"] is True
                and subject["sourceManifestSha256"] == subject["approvedArtifactSourceSha256"]
                and all(type(subject[name]) is str and HEX64.fullmatch(subject[name])
                        for name in ("seedPlanSha256", "s2DeclarationSha256",
                                     "mintProofSha256", "tokenSha256",
                                     "prestateSha256", "prestateSnapshotSha256",
                                     "prestateEvidenceSha256"))
                and all(type(subject[name]) is str and HEX40.fullmatch(subject[name])
                        for name in ("blobOid", "treeOid", "commitOid")),
                "final-genesis-only")


def request_from_archive(archive: Path, phase: str) -> tuple[dict, bytes]:
    require(archive.is_file() and not archive.is_symlink(), "request-archive")
    with archive.open("rb") as stream:
        archive_bytes = stream.read(2 * MAX_JSON_BYTES + 1)
    require(0 < len(archive_bytes) <= 2 * MAX_JSON_BYTES, "request-archive-size")
    try:
        with zipfile.ZipFile(archive) as bundle:
            members = bundle.infolist()
            require(len(members) == 1 and members[0].filename == "seed-admission-request.json",
                    "request-artifact-members")
            member = members[0]
            mode = (member.external_attr >> 16) & 0xFFFF
            require(not member.is_dir() and not stat.S_ISLNK(mode)
                    and 0 < member.file_size <= MAX_JSON_BYTES, "request-artifact-member")
            raw = bundle.read(member)
    except (OSError, zipfile.BadZipFile, RuntimeError) as error:
        raise Refused("request-artifact-zip") from error
    request = strict_json(raw)
    require(set(request) == {"schema", "phase", "subject"}
            and request["schema"] == REQUEST_SCHEMA and request["phase"] == phase,
            "request-shape")
    validate_subject(phase, request["subject"])
    require(raw == canonical(request), "request-noncanonical")
    return request, archive_bytes


def validate_executor(phase: str, subject: dict, artifact: dict, run: dict,
                      artifact_id: int, run_id: int, run_attempt: int,
                      archive_digest: str, archive_bytes: bytes) -> None:
    require(type(artifact) is dict and type(run) is dict, "executor-evidence")
    require(artifact_id > 0 and run_id > 0 and run_attempt > 0
            and ARCHIVE_DIGEST.fullmatch(archive_digest) is not None,
            "executor-input")
    require(run.get("id") == run_id == subject["runId"]
            and run.get("run_attempt") == run_attempt == subject["runAttempt"]
            and run.get("path") == EXECUTOR_WORKFLOW
            and run.get("head_sha") == subject["workflowSha"]
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("repository", {}).get("id") == REPOSITORY_ID
            and run.get("status") in {"queued", "in_progress", "waiting", "completed"}
            and (run.get("status") != "completed" or run.get("conclusion") == "success"),
            "executor-run")
    binding = artifact.get("workflow_run") or {}
    require(type(binding) is dict, "executor-artifact")
    require(artifact.get("id") == artifact_id
            and artifact.get("name") == f"gs2-09-7-seed-admission-request-{phase}-{run_id}-{run_attempt}"
            and artifact.get("expired") is False and artifact.get("digest") == archive_digest
            and binding.get("id") == run_id and binding.get("head_sha") == subject["workflowSha"]
            and binding.get("head_branch") == "main"
            and binding.get("repository_id") == REPOSITORY_ID
            and binding.get("head_repository_id") == REPOSITORY_ID,
            "executor-artifact")
    require("sha256:" + digest(archive_bytes) == archive_digest, "executor-artifact-digest")


def native_authorization(run: dict, approvals: list, environment: dict,
                         policies: dict, producer_sha: str, producer_run_id: int,
                         producer_attempt: int, now: dt.datetime) -> dict:
    require(type(run) is dict and type(environment) is dict and type(policies) is dict,
            "native-evidence")
    require(type(now) is dt.datetime and now.tzinfo is not None and now.utcoffset() is not None,
            "clock")
    require(HEX40.fullmatch(producer_sha) is not None and producer_run_id > 0
            and producer_attempt == 1, "producer-input")
    require(run.get("id") == producer_run_id and run.get("run_attempt") == producer_attempt
            and run.get("path") == AUTHORIZER_WORKFLOW and run.get("head_sha") == producer_sha
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("repository", {}).get("id") == REPOSITORY_ID
            and run.get("actor", {}).get("id") == OWNER["id"]
            and run.get("actor", {}).get("login") == OWNER["login"]
            and run.get("status") == "in_progress" and run.get("conclusion") is None,
            "producer-run")
    created = timestamp(run.get("created_at"), "producer-created-at")
    require(now.astimezone(dt.timezone.utc) >= created + dt.timedelta(minutes=WAIT_MINUTES),
            "producer-wait-timer")

    require(environment.get("id") == ENVIRONMENT_ID and environment.get("name") == ENVIRONMENT
            and environment.get("can_admins_bypass") is True
            and environment.get("deployment_branch_policy") == {
                "protected_branches": False, "custom_branch_policies": True},
            "environment")
    rules = environment.get("protection_rules")
    require(type(rules) is list and len(rules) == 3, "environment-rules")
    reviewer = next((item for item in rules if item.get("type") == "required_reviewers"), None)
    timer = next((item for item in rules if item.get("type") == "wait_timer"), None)
    branch = next((item for item in rules if item.get("type") == "branch_policy"), None)
    reviewer_entries = reviewer.get("reviewers") if reviewer is not None else None
    require(reviewer is not None and reviewer.get("id") == REVIEWER_RULE_ID
            and reviewer.get("prevent_self_review") is False
            and type(reviewer_entries) is list and len(reviewer_entries) == 1
            and reviewer_entries[0].get("type") == "User"
            and type(reviewer_entries[0].get("reviewer")) is dict
            and {key: reviewer_entries[0]["reviewer"].get(key) for key in OWNER} == OWNER
            and timer == {"id": WAIT_RULE_ID, "node_id": timer.get("node_id"),
                          "type": "wait_timer", "wait_timer": WAIT_MINUTES}
            and branch == {"id": BRANCH_RULE_ID, "node_id": branch.get("node_id"),
                           "type": "branch_policy"}, "environment-rules")
    require(policies.get("total_count") == 1 and type(policies.get("branch_policies")) is list
            and len(policies["branch_policies"]) == 1
            and policies["branch_policies"][0].get("id") == MAIN_POLICY_ID
            and policies["branch_policies"][0].get("name") == "main"
            and policies["branch_policies"][0].get("type") == "branch",
            "environment-main-policy")

    require(type(approvals) is list and len(approvals) == 2, "native-approvals")
    owner_approvals = []
    timer_approvals = []
    for approval in approvals:
        envs = approval.get("environments")
        require(type(approval) is dict and type(envs) is list and len(envs) == 1
                and envs[0].get("id") == ENVIRONMENT_ID and envs[0].get("name") == ENVIRONMENT
                and approval.get("state") == "approved", "native-approvals")
        user = approval.get("user") or {}
        identity = {"login": user.get("login"), "id": user.get("id")}
        if identity == OWNER:
            owner_approvals.append(approval)
        if identity == TIMER_ACTOR and approval.get("comment") == "5 minute wait timer":
            timer_approvals.append(approval)
    require(len(owner_approvals) == 1 and len(timer_approvals) == 1,
            "native-approval-unique")
    return {
        "repository": REPOSITORY, "repositoryId": REPOSITORY_ID,
        "workflowPath": AUTHORIZER_WORKFLOW, "workflowSha": producer_sha,
        "runId": producer_run_id, "runAttempt": producer_attempt,
        "environment": {
            "id": ENVIRONMENT_ID, "name": ENVIRONMENT,
            "canAdminsBypass": True,
            "requiredReviewerRuleId": REVIEWER_RULE_ID,
            "reviewer": OWNER, "preventSelfReview": False,
            "waitTimerRuleId": WAIT_RULE_ID, "waitTimerMinutes": WAIT_MINUTES,
            "branchRuleId": BRANCH_RULE_ID, "branchPolicyId": MAIN_POLICY_ID,
            "branchPolicy": "main",
        },
        "nativeApprovals": [
            {"kind": "required-reviewer", "state": "approved", "user": OWNER,
             "environmentId": ENVIRONMENT_ID},
            {"kind": "wait-timer", "state": "approved", "user": TIMER_ACTOR,
             "environmentId": ENVIRONMENT_ID, "minutes": WAIT_MINUTES},
        ],
    }


def authorize(*, phase: str, artifact: dict, executor_run: dict, request_archive: Path,
              producer_run: dict, approvals: list, environment: dict, policies: dict,
              existing_decisions: dict,
              artifact_id: int, executor_run_id: int, executor_run_attempt: int,
              archive_digest: str, producer_sha: str, producer_run_id: int,
              producer_attempt: int, now: dt.datetime) -> dict:
    require(phase in {"prepare", "final"}, "phase")
    require(type(existing_decisions) is dict
            and existing_decisions.get("total_count") == 0
            and existing_decisions.get("artifacts") == [], "duplicate-decision")
    request, archive_bytes = request_from_archive(request_archive, phase)
    subject = request["subject"]
    validate_executor(phase, subject, artifact, executor_run, artifact_id,
                      executor_run_id, executor_run_attempt, archive_digest, archive_bytes)
    authorization = native_authorization(producer_run, approvals, environment, policies,
                                         producer_sha, producer_run_id, producer_attempt, now)
    schema = PREPARE_SCHEMA if phase == "prepare" else FINAL_SCHEMA
    resource = PREPARE_RESOURCE if phase == "prepare" else FINAL_RESOURCE
    identifier = hashlib.sha256((schema + "\n").encode("ascii") + canonical({
        "resourceId": resource, **subject})[:-1]).hexdigest()
    issued = now.astimezone(dt.timezone.utc).replace(microsecond=0)
    return {
        "schema": schema, "resourceId": resource, "decisionId": identifier,
        "state": "admitted", "phase": phase, "subject": subject,
        "executorRequest": {
            "artifactId": artifact_id, "archiveDigest": archive_digest,
            "requestSha256": digest(canonical(request)),
            "runId": executor_run_id, "runAttempt": executor_run_attempt,
        },
        "authorization": authorization,
        "issuedAt": issued.isoformat().replace("+00:00", "Z"),
        "expiresAt": (issued + dt.timedelta(minutes=TTL_MINUTES)).isoformat().replace("+00:00", "Z"),
        "sealed": True,
    }


def load(path: str) -> object:
    return strict_json(Path(path).read_bytes())


def main(arguments: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", choices=("prepare", "final"), required=True)
    for name in ("artifact-json", "executor-run-json", "request-archive", "producer-run-json",
                 "approvals-json", "environment-json", "branch-policies-json",
                 "existing-decisions-json", "output"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--artifact-id", required=True, type=int)
    parser.add_argument("--executor-run-id", required=True, type=int)
    parser.add_argument("--executor-run-attempt", required=True, type=int)
    parser.add_argument("--archive-digest", required=True)
    parser.add_argument("--producer-sha", required=True)
    parser.add_argument("--producer-run-id", required=True, type=int)
    parser.add_argument("--producer-run-attempt", required=True, type=int)
    args = parser.parse_args(arguments)
    try:
        result = authorize(
            phase=args.phase, artifact=load(args.artifact_json),
            executor_run=load(args.executor_run_json), request_archive=Path(args.request_archive),
            producer_run=load(args.producer_run_json), approvals=load(args.approvals_json),
            environment=load(args.environment_json), policies=load(args.branch_policies_json),
            existing_decisions=load(args.existing_decisions_json),
            artifact_id=args.artifact_id, executor_run_id=args.executor_run_id,
            executor_run_attempt=args.executor_run_attempt, archive_digest=args.archive_digest,
            producer_sha=args.producer_sha, producer_run_id=args.producer_run_id,
            producer_attempt=args.producer_run_attempt, now=dt.datetime.now(dt.timezone.utc))
        output = Path(args.output)
        descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(canonical(result))
        return 0
    except (OSError, KeyError, TypeError, Refused, zipfile.BadZipFile) as error:
        reason = str(error) if isinstance(error, Refused) else "unavailable-or-malformed"
        print(f"seed admission authorization refused: {reason}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""Verify and expose one immutable, sanitized Q4 seed admission artifact."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import stat
import sys
import zipfile


HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location(
    "seed_admission_authorizer", HERE / "gs2-09-7-seed-admission-authorizer.py")
if SPEC is None or SPEC.loader is None:  # pragma: no cover - installation failure
    raise RuntimeError("authorizer module unavailable")
authorizer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(authorizer)

PORT_SCHEMA = "fsgg.gs2-09-7.seed-bootstrap-admission/1"
PREPARE_PORT_SCHEMA = "fsgg.gs2-09-7.seed-prepare-admission/1"
ORIGIN = "https://api.github.com"
ENDPOINT = "https://api.github.com/repos/FS-GG/.github/actions/artifacts"
ARCHIVE_DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
MAX_ARCHIVE_BYTES = 512 * 1024
DECISION_MEMBER = "gs2-09-7-seed-admission-decision.json"


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def canonical(value: object) -> bytes:
    return authorizer.canonical(value)


def strict_json(raw: bytes) -> object:
    require(type(raw) is bytes and 0 < len(raw) <= authorizer.MAX_JSON_BYTES, "json-size")
    try:
        return json.loads(raw.decode("utf-8"), object_pairs_hook=authorizer.unique_pairs,
                          parse_constant=lambda _value: (_ for _ in ()).throw(Refused("nonfinite-number")))
    except (UnicodeError, ValueError, TypeError, RecursionError) as error:
        raise Refused("malformed-json") from error


def timestamp(value: object, reason: str) -> dt.datetime:
    require(type(value) is str and value.endswith("Z"), reason)
    try:
        parsed = dt.datetime.fromisoformat(value[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused(reason) from error
    require(parsed.tzinfo is not None, reason)
    return parsed


def native_authorization(run: dict, approvals: list, environment: dict,
                         policies: dict, now: dt.datetime) -> dict:
    require(type(run) is dict, "producer-run")
    producer_sha = run.get("head_sha")
    producer_run_id = run.get("id")
    producer_attempt = run.get("run_attempt")
    require(type(producer_sha) is str and authorizer.HEX40.fullmatch(producer_sha)
            and type(producer_run_id) is int and producer_run_id > 0
            and producer_attempt == 1, "producer-run")
    require(run.get("path") == authorizer.AUTHORIZER_WORKFLOW
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("repository", {}).get("id") == authorizer.REPOSITORY_ID
            and run.get("actor", {}).get("id") == authorizer.OWNER["id"]
            and run.get("actor", {}).get("login") == authorizer.OWNER["login"]
            and run.get("status") == "completed" and run.get("conclusion") == "success",
            "producer-run")
    # Reuse the exact native environment, rule, owner, approval and elapsed-timer
    # checks after projecting the immutable completed run to its execution-time state.
    projected = {**run, "status": "in_progress", "conclusion": None}
    return authorizer.native_authorization(
        projected, approvals, environment, policies, producer_sha,
        producer_run_id, producer_attempt, now)


def decision_from_archive(archive: Path) -> tuple[dict, bytes]:
    require(archive.is_file() and not archive.is_symlink(), "decision-archive")
    with archive.open("rb") as stream:
        archive_bytes = stream.read(MAX_ARCHIVE_BYTES + 1)
    require(0 < len(archive_bytes) <= MAX_ARCHIVE_BYTES, "decision-archive-size")
    try:
        with zipfile.ZipFile(archive) as bundle:
            members = bundle.infolist()
            require(len(members) == 1 and members[0].filename == DECISION_MEMBER,
                    "decision-artifact-members")
            member = members[0]
            mode = (member.external_attr >> 16) & 0xFFFF
            require(not member.is_dir() and not stat.S_ISLNK(mode)
                    and 0 < member.file_size <= authorizer.MAX_JSON_BYTES,
                    "decision-artifact-member")
            raw = bundle.read(member)
    except (OSError, zipfile.BadZipFile, RuntimeError) as error:
        raise Refused("decision-artifact-zip") from error
    decision = strict_json(raw)
    require(type(decision) is dict and raw == canonical(decision), "decision-canonical-bytes")
    return decision, archive_bytes


def verify_decision(*, phase: str, expected_subject: dict, expected_producer_sha: str,
                    artifact: dict, decision_artifacts: dict, producer_run: dict, approvals: list,
                    environment: dict, policies: dict, archive: Path,
                    artifact_id: int, producer_run_id: int,
                    producer_run_attempt: int, archive_digest: str,
                    now: dt.datetime) -> dict:
    require(phase in {"prepare", "final"}, "phase")
    authorizer.validate_subject(phase, expected_subject)
    require(type(artifact) is dict and type(decision_artifacts) is dict
            and type(producer_run) is dict, "native-read-evidence")
    require(type(expected_producer_sha) is str
            and authorizer.HEX40.fullmatch(expected_producer_sha), "expected-producer-sha")
    require(type(now) is dt.datetime and now.tzinfo is not None and now.utcoffset() is not None,
            "clock")
    require(artifact_id > 0 and producer_run_id > 0 and producer_run_attempt == 1
            and ARCHIVE_DIGEST.fullmatch(archive_digest) is not None, "read-input")
    decision, archive_bytes = decision_from_archive(archive)
    require("sha256:" + hashlib.sha256(archive_bytes).hexdigest() == archive_digest,
            "decision-artifact-digest")

    binding = artifact.get("workflow_run") or {}
    require(artifact.get("id") == artifact_id
            and artifact.get("name") == (f"gs2-09-7-seed-admission-decision-{phase}-"
                                         f'{expected_subject["runId"]}-{expected_subject["runAttempt"]}')
            and artifact.get("expired") is False and artifact.get("digest") == archive_digest
            and binding.get("id") == producer_run_id
            and binding.get("head_sha") == producer_run.get("head_sha")
            and binding.get("head_branch") == "main"
            and binding.get("repository_id") == authorizer.REPOSITORY_ID
            and binding.get("head_repository_id") == authorizer.REPOSITORY_ID,
            "decision-artifact")
    artifacts = decision_artifacts.get("artifacts")
    require(decision_artifacts.get("total_count") == 1 and type(artifacts) is list
            and len(artifacts) == 1
            and type(artifacts[0]) is dict
            and artifacts[0].get("id") == artifact_id
            and artifacts[0].get("name") == artifact.get("name")
            and artifacts[0].get("digest") == archive_digest
            and artifacts[0].get("expired") is False
            and (artifacts[0].get("workflow_run") or {}).get("id") == producer_run_id,
            "decision-artifact-unique")
    require(producer_run.get("id") == producer_run_id
            and producer_run.get("run_attempt") == producer_run_attempt
            and producer_run.get("head_sha") == expected_producer_sha,
            "producer-run-binding")
    native = native_authorization(producer_run, approvals, environment, policies, now)

    schema = authorizer.PREPARE_SCHEMA if phase == "prepare" else authorizer.FINAL_SCHEMA
    resource = authorizer.PREPARE_RESOURCE if phase == "prepare" else authorizer.FINAL_RESOURCE
    require(set(decision) == {
        "schema", "resourceId", "decisionId", "state", "phase", "subject",
        "executorRequest", "authorization", "issuedAt", "expiresAt", "sealed"},
        "decision-shape")
    identifier = hashlib.sha256((schema + "\n").encode("ascii") + canonical({
        "resourceId": resource, **expected_subject})[:-1]).hexdigest()
    require(decision["schema"] == schema and decision["resourceId"] == resource
            and decision["decisionId"] == identifier and decision["state"] == "admitted"
            and decision["phase"] == phase and decision["subject"] == expected_subject
            and decision["authorization"] == native and decision["sealed"] is True,
            "decision-binding")
    request = decision["executorRequest"]
    require(type(request) is dict and set(request) == {
        "artifactId", "archiveDigest", "requestSha256", "runId", "runAttempt"}
            and type(request["artifactId"]) is int and request["artifactId"] > 0
            and ARCHIVE_DIGEST.fullmatch(request["archiveDigest"]) is not None
            and type(request["requestSha256"]) is str
            and authorizer.HEX64.fullmatch(request["requestSha256"])
            and request["runId"] == expected_subject["runId"]
            and request["runAttempt"] == expected_subject["runAttempt"],
            "executor-request-binding")
    issued = timestamp(decision["issuedAt"], "decision-expiry")
    expires = timestamp(decision["expiresAt"], "decision-expiry")
    current = now.astimezone(dt.timezone.utc)
    require(issued <= current < expires <= issued + dt.timedelta(minutes=authorizer.TTL_MINUTES),
            "decision-expiry")
    return decision


class NativeDecisionPort:
    """Read-only port consumed by ``seed-bootstrap-admission`` after verification."""

    def __init__(self, decision: dict, phase: str):
        require(phase in {"prepare", "final"}, "phase")
        self._decision = decision
        self._phase = phase

    def describe(self) -> dict:
        return {
            "schema": PREPARE_PORT_SCHEMA if self._phase == "prepare" else PORT_SCHEMA,
            "origin": ORIGIN,
            "resourceId": (authorizer.PREPARE_RESOURCE if self._phase == "prepare"
                           else authorizer.FINAL_RESOURCE),
            "endpoint": ENDPOINT,
            "durable": True, "immutable": True, "nativeReadback": True,
            "credentialScope": "actions-read",
            "candidateCanRead": True, "candidateCanWrite": False,
            "executorWorkflowCanWrite": False, "authorizerWorkflowCanWrite": True,
            "decisionWriter": authorizer.AUTHORIZER_WORKFLOW,
        }

    def read_decision(self, run_id: int, run_attempt: int) -> dict:
        subject = self._decision.get("subject") or {}
        require(subject.get("runId") == run_id and subject.get("runAttempt") == run_attempt,
                "decision-subject-lookup")
        return self._decision


def load(path: str) -> object:
    return strict_json(Path(path).read_bytes())


def main(arguments: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--phase", choices=("prepare", "final"), required=True)
    for name in ("subject-json", "artifact-json", "decision-artifacts-json", "producer-run-json",
                 "approvals-json", "environment-json", "branch-policies-json", "archive"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--artifact-id", required=True, type=int)
    parser.add_argument("--producer-run-id", required=True, type=int)
    parser.add_argument("--producer-run-attempt", required=True, type=int)
    parser.add_argument("--producer-sha", required=True)
    parser.add_argument("--archive-digest", required=True)
    args = parser.parse_args(arguments)
    try:
        decision = verify_decision(
            phase=args.phase, expected_subject=load(args.subject_json),
            expected_producer_sha=args.producer_sha,
            artifact=load(args.artifact_json), decision_artifacts=load(args.decision_artifacts_json),
            producer_run=load(args.producer_run_json), approvals=load(args.approvals_json),
            environment=load(args.environment_json), policies=load(args.branch_policies_json),
            archive=Path(args.archive), artifact_id=args.artifact_id,
            producer_run_id=args.producer_run_id,
            producer_run_attempt=args.producer_run_attempt,
            archive_digest=args.archive_digest, now=dt.datetime.now(dt.timezone.utc))
        print(canonical(decision).decode("ascii"), end="")
        return 0
    except (OSError, KeyError, TypeError, Refused, authorizer.Refused, zipfile.BadZipFile) as error:
        reason = str(error) if isinstance(error, (Refused, authorizer.Refused)) else "unavailable-or-malformed"
        print(f"seed admission native read refused: {reason}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

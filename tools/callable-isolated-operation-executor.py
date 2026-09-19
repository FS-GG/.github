#!/usr/bin/env python3
"""Prepare a sealed V2-CALL-01.4b executor runtime-admission packet."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import io
import json
import os
import pathlib
import re
import sys
import zipfile


SCHEMA = "fsgg.github.callable-isolated-operation-executor-readiness/1"
OPERATION_IDENTITY = "v2-call-01-4b-isolated-native-v1"
AUTHORITY_REPOSITORY = "FS-GG/.github"
AUTHORIZATION_WORKFLOW = ".github/workflows/callable-isolated-operation-authorize.yml"
EXECUTOR_WORKFLOW = ".github/workflows/callable-isolated-operation-execute.yml"
ENVIRONMENT = "callable-isolated-operation"
COORDINATION_REPOSITORY = "FS-GG/FS.GG.Coordination"
COORDINATION_REVISION = "8e5ce4cc9aa3e1d2e200d0dc6b1812ec12ae2bce"
CONTRACT_SHA256 = "828855bd5ba0455a1c5bb3d2e1fdad6ccef710fbf2c6205812a1b383a6e07d5c"
CONTRACT_FILE_SHA256 = "39a2a28a5b084881ff6d2e6cfccc24bc4f02128b9b4ed16b49edf12ce926ef9f"
SOURCE_SHA256 = "392617a63fb622eb8d243f7973f1c74d63d13ac256aac2c86532cdeb51c1d5b2"
PACKAGE_ID = "FS.GG.Coordination.Cli"
PACKAGE_VERSION = "0.1.0"
PACKAGE_SERVED_SHA256 = "e7f440a2a1f94d51dbcdd7146494c97e6386f9dcc8034a028e3e851d364390e3"
INSTALLED_COMMAND_SHA256 = "21d36ec3cdbb153453833ed57f2320f9f256a26f35aea52bb75c52a68efada04"
RECEIVER_REVISION = "587f46e15e1404dbe0dc1e9e6b47cf2861d7b502"
TARGET = "FS-GG/FS.GG.Coordination.CallableSandbox"
OID = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]{0,18}")
GRANT_ARTIFACT_SCHEMA = "fsgg.coordination.callable-isolated-operation-grant-artifact-envelope/1"
GRANT_ARTIFACT_PREFIX = "callable-isolated-operation-grant"
GRANT_ARTIFACT_FILE = "callable-isolated-operation-grant.json"
MAX_GRANT_BYTES = 128 * 1024


class Refused(ValueError):
    pass


def positive(name: str, value: str) -> int:
    if not POSITIVE_INTEGER.fullmatch(value):
        raise Refused(f"{name} must be a positive bounded integer")
    parsed = int(value)
    if parsed > 9_223_372_036_854_775_807:
        raise Refused(f"{name} must fit a signed 64-bit integer")
    return parsed


def sha256(name: str, value: str) -> str:
    if not SHA256.fullmatch(value):
        raise Refused(f"{name} must be one lowercase SHA-256")
    return value


def timestamp(name: str, value: str, *, require_future: bool = False) -> str:
    if not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", value):
        raise Refused(f"{name} must be canonical UTC seconds")
    parsed = dt.datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=dt.timezone.utc)
    if parsed.year < 2026 or parsed.year > 2100:
        raise Refused(f"{name} is outside the bounded operating horizon")
    if require_future and parsed <= dt.datetime.now(dt.timezone.utc):
        raise Refused(f"{name} is expired")
    return value


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def grant_artifact_payload(archive: bytes) -> tuple[dict[str, object], bytes]:
    if len(archive) > MAX_GRANT_BYTES * 2:
        raise Refused("grant artifact archive is too large")
    try:
        with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
            entries = bundle.infolist()
            if (len(entries) != 1 or entries[0].filename != GRANT_ARTIFACT_FILE
                    or entries[0].is_dir() or entries[0].file_size > MAX_GRANT_BYTES
                    or entries[0].compress_size > MAX_GRANT_BYTES
                    or entries[0].external_attr >> 16 & 0o170000 == 0o120000):
                raise Refused("grant artifact must contain one bounded canonical payload")
            raw = bundle.read(entries[0])
    except (zipfile.BadZipFile, RuntimeError, KeyError) as error:
        raise Refused("grant artifact must be a valid single-file zip") from error
    try:
        value = json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError) as error:
        raise Refused("grant artifact payload must be canonical JSON") from error
    if not isinstance(value, dict) or canonical(value) != raw.rstrip(b"\n") or "artifact" in value:
        raise Refused("grant artifact payload must be canonical v4 bytes without coordinates")
    return value, canonical(value)


def verify_grant_artifact_bytes(envelope: dict[str, object], archive: bytes) -> dict[str, object]:
    required = {
        "schema", "repository", "artifactId", "artifactName", "artifactSha256",
        "payloadSha256", "workflowRunId", "workflowRunAttempt", "expiresAt",
    }
    if set(envelope) != required or envelope.get("schema") != GRANT_ARTIFACT_SCHEMA:
        raise Refused("grant artifact envelope schema or fields are not exact")
    if envelope.get("repository") != AUTHORITY_REPOSITORY:
        raise Refused("grant artifact envelope repository is not trusted")
    artifact_id = envelope.get("artifactId")
    run_id = envelope.get("workflowRunId")
    run_attempt = envelope.get("workflowRunAttempt")
    if any(not isinstance(value, int) or isinstance(value, bool) or value <= 0
           for value in (artifact_id, run_id, run_attempt)):
        raise Refused("grant artifact envelope identities must be positive integers")
    if envelope.get("artifactName") != f"{GRANT_ARTIFACT_PREFIX}-{run_id}-{run_attempt}":
        raise Refused("grant artifact name does not bind the exact run attempt")
    timestamp("grant-artifact-expires-at", str(envelope.get("expiresAt")), require_future=True)
    archive_digest = envelope.get("artifactSha256")
    payload_digest = envelope.get("payloadSha256")
    if not isinstance(archive_digest, str) or not SHA256.fullmatch(archive_digest):
        raise Refused("grant artifact archive digest must be one lowercase SHA-256")
    if not isinstance(payload_digest, str) or not SHA256.fullmatch(payload_digest):
        raise Refused("grant payload digest must be one lowercase SHA-256")
    if hashlib.sha256(archive).hexdigest() != archive_digest:
        raise Refused("grant artifact archive digest does not match the envelope")
    value, payload = grant_artifact_payload(archive)
    if hashlib.sha256(payload).hexdigest() != payload_digest:
        raise Refused("grant payload digest does not match the envelope")
    return value


def artifact(repository: str, label: str, run_id: str, run_attempt: str, artifact_id: str, digest: str) -> dict[str, object]:
    return {
        "artifactId": positive(f"{label}-artifact-id", artifact_id),
        "repository": repository,
        "runAttempt": positive(f"{label}-run-attempt", run_attempt),
        "runId": positive(f"{label}-run-id", run_id),
        "sha256": sha256(f"{label}-sha256", digest),
    }


def optional_artifact(repository: str, label: str, values: tuple[str, str, str, str]) -> dict[str, object] | None:
    if not any(values):
        return None
    if not all(values):
        raise Refused(f"{label} coordinates must be complete or absent")
    return artifact(repository, label, *values)


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--phase", choices=("creation", "identity-bound-operation"), required=True)
    result.add_argument("--plan-seal", required=True)
    result.add_argument("--grant-run-id", required=True)
    result.add_argument("--grant-run-attempt", required=True)
    result.add_argument("--grant-artifact-id", required=True)
    result.add_argument("--grant-artifact-sha256", required=True)
    result.add_argument("--grant-payload-sha256", required=True)
    result.add_argument("--grant-artifact-expires-at", required=True)
    result.add_argument("--plan-run-id", required=True)
    result.add_argument("--plan-run-attempt", required=True)
    result.add_argument("--plan-artifact-id", required=True)
    result.add_argument("--plan-artifact-sha256", required=True)
    for prefix in ("creation-receipt", "checkpoint"):
        result.add_argument(f"--{prefix}-run-id", default="")
        result.add_argument(f"--{prefix}-run-attempt", default="")
        result.add_argument(f"--{prefix}-artifact-id", default="")
        result.add_argument(f"--{prefix}-artifact-sha256", default="")
    result.add_argument("--workflow-revision", required=True)
    result.add_argument("--workflow-sha256", required=True)
    result.add_argument("--output", required=True)
    return result


def build(args: argparse.Namespace) -> dict[str, object]:
    if not OID.fullmatch(args.workflow_revision):
        raise Refused("authority revision must be one lowercase Git object id")
    authorization_run = {
        "environment": ENVIRONMENT,
        "event": "workflow_dispatch",
        "requiredRunConclusion": "success",
        "runAttempt": positive("grant-run-attempt", args.grant_run_attempt),
        "runId": positive("grant-run-id", args.grant_run_id),
        "workflowPath": AUTHORIZATION_WORKFLOW,
    }
    grant_artifact_envelope = {
        "schema": GRANT_ARTIFACT_SCHEMA,
        "repository": AUTHORITY_REPOSITORY,
        "artifactId": positive("grant-artifact-id", args.grant_artifact_id),
        "artifactName": f"{GRANT_ARTIFACT_PREFIX}-{authorization_run['runId']}-{authorization_run['runAttempt']}",
        "artifactSha256": sha256("grant-artifact-sha256", args.grant_artifact_sha256),
        "payloadSha256": sha256("grant-payload-sha256", args.grant_payload_sha256),
        "workflowRunId": authorization_run["runId"],
        "workflowRunAttempt": authorization_run["runAttempt"],
        "expiresAt": timestamp("grant-artifact-expires-at", args.grant_artifact_expires_at),
    }
    plan = artifact(AUTHORITY_REPOSITORY, "plan", args.plan_run_id, args.plan_run_attempt,
                    args.plan_artifact_id, args.plan_artifact_sha256)
    creation_receipt = optional_artifact(AUTHORITY_REPOSITORY, "creation-receipt", (
        args.creation_receipt_run_id, args.creation_receipt_run_attempt,
        args.creation_receipt_artifact_id, args.creation_receipt_artifact_sha256))
    checkpoint = optional_artifact(AUTHORITY_REPOSITORY, "checkpoint", (
        args.checkpoint_run_id, args.checkpoint_run_attempt,
        args.checkpoint_artifact_id, args.checkpoint_artifact_sha256))
    if args.phase == "creation" and creation_receipt is not None:
        raise Refused("creation phase cannot consume a creation receipt")
    if args.phase == "identity-bound-operation" and creation_receipt is None:
        raise Refused("identity-bound phase requires the exact creation receipt")
    packet = {
        "authorized": False,
        "bindings": {
            "authority": {
                "environment": ENVIRONMENT,
                "repository": AUTHORITY_REPOSITORY,
                "revision": args.workflow_revision,
                "workflowPath": EXECUTOR_WORKFLOW,
                "workflowSha256": sha256("workflow-sha256", args.workflow_sha256),
            },
            "authorizationWorkflow": AUTHORIZATION_WORKFLOW,
            "contractSha256": CONTRACT_SHA256,
            "contractFileSha256": CONTRACT_FILE_SHA256,
            "coordinationRepository": COORDINATION_REPOSITORY,
            "coordinationRevision": COORDINATION_REVISION,
            "operationSourceSha256": SOURCE_SHA256,
            "package": {
                "id": PACKAGE_ID,
                "installedCommandSha256": INSTALLED_COMMAND_SHA256,
                "servedSha256": PACKAGE_SERVED_SHA256,
                "version": PACKAGE_VERSION,
            },
            "receiverRevision": RECEIVER_REVISION,
            "target": TARGET,
        },
        "blockingReasons": [],
        "evidence": {
            "checkpoint": checkpoint,
            "creationReceipt": creation_receipt,
            "authorizationRun": authorization_run,
            "grantArtifactEnvelope": grant_artifact_envelope,
            "plan": plan,
            "planSeal": sha256("plan-seal", args.plan_seal),
        },
        "execution": "pending-live-admission",
        "mutationCredentialsMinted": False,
        "operationIdentity": OPERATION_IDENTITY,
        "phase": args.phase,
        "packageVerification": {
            "cleanToolPathRequired": True,
            "feed": "https://api.nuget.org/v3/index.json",
            "invocation": "fsgg-coordination --version",
            "noCheckoutOrPrivateFeedDependency": True,
        },
        "requiredReadOnlyAdmission": [
            "authorization-run-completed-success-at-exact-attempt",
            "authorization-workflow-path-revision-and-bytes",
            "protected-environment-approval-and-environment-id",
            "required-reviewer-active-membership",
            "grant-artifact-envelope-api-and-downloaded-byte-readback",
            "single-canonical-grant-payload-without-artifact-coordinates",
            "prepared-plan-bytes-digest-seal-and-phase",
            "app-and-installation-identities-per-exact-role",
            "credential-expiry-before-every-effect",
        ],
        "recovery": {
            "automaticMutationRetry": False,
            "checkpoint": "exact-run-attempt-artifact-or-absent",
            "networkUnknown": "pending-requires-authoritative-readback",
            "retentionName": "callable-isolated-operation-readiness-<phase>-<run-id>-<run-attempt>",
        },
        "schema": SCHEMA,
        "status": "runtime-admission-required",
        "operatorIntegration": {
            "argument": "--grant-artifact-envelope",
            "coordinationContract": "fsgg.coordination.callable-isolated-operation-contract/4",
            "envelopeSchema": GRANT_ARTIFACT_SCHEMA,
            "readinessOnly": False,
        },
        "tokenMinting": {
            "allowed": True,
            "order": "only-after-complete-read-only-admission",
            "phaseSeparation": "one-exact-phase-per-protected-grant",
            "targetExecutionScope": [TARGET],
        },
    }
    packet["seal"] = hashlib.sha256(json.dumps(packet, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    return packet


def main() -> int:
    args = parser().parse_args()
    try:
        packet = build(args)
        output = pathlib.Path(args.output)
        if output.exists() or output.is_symlink():
            raise Refused("output must be a new regular file")
        output.write_text(json.dumps(packet, sort_keys=True, separators=(",", ":")) + "\n")
        os.chmod(output, 0o600)
        print("execution-pending:runtime-admission-required")
        return 0
    except (OSError, Refused) as error:
        print(f"callable isolated executor refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())

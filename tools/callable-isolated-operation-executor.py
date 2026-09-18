#!/usr/bin/env python3
"""Prepare a sealed, non-executing V2-CALL-01.4b executor readiness packet."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import re
import sys


SCHEMA = "fsgg.github.callable-isolated-operation-executor-readiness/1"
OPERATION_IDENTITY = "v2-call-01-4b-isolated-native-v1"
AUTHORITY_REPOSITORY = "FS-GG/.github"
AUTHORIZATION_WORKFLOW = ".github/workflows/callable-isolated-operation-authorize.yml"
EXECUTOR_WORKFLOW = ".github/workflows/callable-isolated-operation-execute.yml"
ENVIRONMENT = "callable-isolated-operation"
COORDINATION_REPOSITORY = "FS-GG/FS.GG.Coordination"
COORDINATION_REVISION = "79ffe01f5cc2a0269797f3ec7ff54bf2c23b5c91"
CONTRACT_SHA256 = "cc17065452ee941295a17844facfbdb13d305df179d7634b40459c0f63579a25"
CONTRACT_FILE_SHA256 = "c56cbca184c6477f5d95c3863c227a1eb3eb537e6ed058a193023de6c86a4d3b"
SOURCE_SHA256 = "0335c253aea68061f338cded634f29268303ec1eea31c6b0472b472d7974ba1e"
PACKAGE_ID = "FS.GG.Coordination.Cli"
PACKAGE_VERSION = "0.1.0"
PACKAGE_SERVED_SHA256 = "e7f440a2a1f94d51dbcdd7146494c97e6386f9dcc8034a028e3e851d364390e3"
INSTALLED_COMMAND_SHA256 = "21d36ec3cdbb153453833ed57f2320f9f256a26f35aea52bb75c52a68efada04"
RECEIVER_REVISION = "587f46e15e1404dbe0dc1e9e6b47cf2861d7b502"
TARGET = "FS-GG/FS.GG.Coordination.CallableSandbox"
OID = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]{0,18}")


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
    result.add_argument("--authorization-run-id", required=True)
    result.add_argument("--authorization-run-attempt", required=True)
    result.add_argument("--grant-artifact-id", required=True)
    result.add_argument("--grant-artifact-sha256", required=True)
    result.add_argument("--plan-run-id", required=True)
    result.add_argument("--plan-run-attempt", required=True)
    result.add_argument("--plan-artifact-id", required=True)
    result.add_argument("--plan-artifact-sha256", required=True)
    for prefix in ("creation-receipt", "checkpoint"):
        result.add_argument(f"--{prefix}-run-id", default="")
        result.add_argument(f"--{prefix}-run-attempt", default="")
        result.add_argument(f"--{prefix}-artifact-id", default="")
        result.add_argument(f"--{prefix}-artifact-sha256", default="")
    result.add_argument("--authority-revision", required=True)
    result.add_argument("--workflow-sha256", required=True)
    result.add_argument("--output", required=True)
    return result


def build(args: argparse.Namespace) -> dict[str, object]:
    if not OID.fullmatch(args.authority_revision):
        raise Refused("authority revision must be one lowercase Git object id")
    grant = artifact(AUTHORITY_REPOSITORY, "grant", args.authorization_run_id,
                     args.authorization_run_attempt, args.grant_artifact_id, args.grant_artifact_sha256)
    grant.update({
        "environment": ENVIRONMENT,
        "event": "workflow_dispatch",
        "requiredRunConclusion": "success",
        "workflowPath": AUTHORIZATION_WORKFLOW,
    })
    plan = artifact(COORDINATION_REPOSITORY, "plan", args.plan_run_id, args.plan_run_attempt,
                    args.plan_artifact_id, args.plan_artifact_sha256)
    creation_receipt = optional_artifact(COORDINATION_REPOSITORY, "creation-receipt", (
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
                "revision": args.authority_revision,
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
        "blockingReasons": [
            "grant-payload-artifact-coordinate-self-reference-incompatible-with-coordination-v3",
            "protected-environment-approval-not-observed",
            "reviewer-membership-not-observed",
            "reviewed-app-installations-and-exact-role-grants-unavailable",
            "cross-repository-artifact-read-authority-unavailable",
        ],
        "evidence": {
            "checkpoint": checkpoint,
            "creationReceipt": creation_receipt,
            "grant": grant,
            "plan": plan,
            "planSeal": sha256("plan-seal", args.plan_seal),
        },
        "execution": "unavailable",
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
            "grant-artifact-bytes-id-and-digest",
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
        "status": "prepared-not-authorized",
        "tokenMinting": {
            "allowed": False,
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
        print("execution-unavailable:prepared-not-authorized")
        return 0
    except (OSError, Refused) as error:
        print(f"callable isolated executor refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())

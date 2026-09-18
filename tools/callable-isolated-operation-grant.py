#!/usr/bin/env python3
"""Build the bounded, deliberately unavailable V2-CALL-01.4b grant preparation."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import pathlib
import re
import sys


SCHEMA = "fsgg.coordination.callable-isolated-operation-grant/1"
OPERATION_IDENTITY = "v2-call-01-4b-isolated-native-v1"
CONTRACT_SHA256 = "3ebf436e7e2efdf221b9b08f96b6d5216bbeb22053af26bd7cdd2d0d11ef561d"
SOURCE_SHA256 = "b5a20b2c511bf37833dac99c35eb1fa420f410f5b324fd26883e5af928cb145c"
COORDINATION_REVISION = "d46aa238d0f169c85a5822e62e49ab9df1ebf37d"
AUTHORITY_REPOSITORY = "FS-GG/.github"
WORKFLOW_PATH = ".github/workflows/callable-isolated-operation-authorize.yml"
ENVIRONMENT = "callable-isolated-operation"
TARGET = "FS-GG/FS.GG.Coordination.CallableSandbox"
REVIEWER_ID = 1645484
REVIEWER_LOGIN = "EHotwagner"
SHA256 = re.compile(r"[0-9a-f]{64}")
OID = re.compile(r"[0-9a-f]{40}")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]{0,18}")
ROLE_PERMISSIONS = {
    "app-installation-observer": {},
    "authority-observer": {"actions": "read", "contents": "read", "metadata": "read"},
    "reviewer-membership-observer": {"members": "read", "metadata": "read"},
    "creation": {"administration": "write", "metadata": "read"},
    "setup": {"actions": "write", "administration": "write", "checks": "read", "contents": "write",
              "metadata": "read", "pull_requests": "write", "workflows": "write"},
    "execution": {"checks": "read", "contents": "write", "metadata": "read", "pull_requests": "write"},
    "cleanup": {"administration": "write", "metadata": "read"},
}


class Refused(ValueError):
    pass


def exact(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        raise Refused(f"{name} does not match the reviewed contract")


def bounded_id(name: str, value: str) -> int:
    if not POSITIVE_INTEGER.fullmatch(value):
        raise Refused(f"{name} must be a positive bounded integer")
    parsed = int(value)
    if parsed > 9_223_372_036_854_775_807:
        raise Refused(f"{name} must fit a signed 64-bit integer")
    return parsed


def timestamp(value: str) -> dt.datetime:
    if not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z", value):
        raise Refused("approved-at must be canonical UTC seconds")
    parsed = dt.datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=dt.timezone.utc)
    if parsed.year < 2026 or parsed.year > 2100:
        raise Refused("approved-at is outside the bounded operating horizon")
    return parsed


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--phase", required=True, choices=("creation", "identity-bound-operation"))
    result.add_argument("--plan-seal", required=True)
    result.add_argument("--contract-sha256", required=True)
    result.add_argument("--source-sha256", required=True)
    result.add_argument("--coordination-revision", required=True)
    result.add_argument("--grant-repository", required=True)
    result.add_argument("--grant-event", required=True)
    result.add_argument("--grant-ref", required=True)
    result.add_argument("--workflow-revision", required=True)
    result.add_argument("--workflow-sha256", required=True)
    result.add_argument("--run-id", required=True)
    result.add_argument("--run-attempt", required=True)
    result.add_argument("--environment-id", required=True)
    result.add_argument("--credential-bindings-json", required=True)
    result.add_argument("--approved-at", required=True)
    result.add_argument("--lifetime-minutes", required=True, type=int)
    result.add_argument("--output", required=True)
    return result


def build(args: argparse.Namespace) -> dict[str, object]:
    if args.phase not in {"creation", "identity-bound-operation"}:
        raise Refused("phase is outside the reviewed contract")
    exact("contract SHA", args.contract_sha256, CONTRACT_SHA256)
    exact("operator source SHA", args.source_sha256, SOURCE_SHA256)
    exact("Coordination revision", args.coordination_revision, COORDINATION_REVISION)
    exact("grant repository", args.grant_repository, AUTHORITY_REPOSITORY)
    exact("grant event", args.grant_event, "workflow_dispatch")
    exact("grant ref", args.grant_ref, "refs/heads/main")
    if not SHA256.fullmatch(args.plan_seal):
        raise Refused("plan seal must be a lowercase SHA-256")
    if not OID.fullmatch(args.workflow_revision):
        raise Refused("workflow revision must be a lowercase Git object id")
    if not SHA256.fullmatch(args.workflow_sha256):
        raise Refused("workflow SHA must be a lowercase SHA-256")
    if not 1 <= args.lifetime_minutes <= 120:
        raise Refused("grant lifetime must be between 1 and 120 minutes")
    approved = timestamp(args.approved_at)
    expires = approved + dt.timedelta(minutes=args.lifetime_minutes)
    try:
        bindings = json.loads(args.credential_bindings_json)
    except json.JSONDecodeError as error:
        raise Refused("credential bindings must be valid JSON") from error
    if not isinstance(bindings, dict) or set(bindings) != set(ROLE_PERMISSIONS):
        raise Refused("credential bindings must name the exact reviewed role set")
    credentials = {}
    for role, permissions in ROLE_PERMISSIONS.items():
        identity = bindings[role]
        if not isinstance(identity, dict) or set(identity) != {"appId", "installationId"}:
            raise Refused(f"{role} credential identity is not exact")
        credentials[role] = {
            "appId": bounded_id(f"{role}-app-id", str(identity["appId"])),
            "expiresAt": expires.strftime("%Y-%m-%dT%H:%M:%SZ"),
            "installationId": bounded_id(f"{role}-installation-id", str(identity["installationId"])),
            "kind": "github-app-jwt" if role == "app-installation-observer" else "github-app-installation",
            "permissions": permissions,
        }
    return {
        "approvedAt": approved.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "authority": {
            "environment": ENVIRONMENT,
            "environmentId": bounded_id("environment-id", args.environment_id),
            "repository": AUTHORITY_REPOSITORY,
            "runId": bounded_id("run-id", args.run_id),
            "runAttempt": bounded_id("run-attempt", args.run_attempt),
            "workflowPath": WORKFLOW_PATH,
            "workflowRevision": args.workflow_revision,
            "workflowSha256": args.workflow_sha256,
        },
        "authorized": False,
        "blockingReasons": [
            "protected-environment-and-reviewed-app-installations-not-yet-provisioned",
        ],
        "contractSha256": CONTRACT_SHA256,
        "coordinationRevision": COORDINATION_REVISION,
        "expiresAt": expires.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "credentials": credentials,
        "operationIdentity": OPERATION_IDENTITY,
        "phase": args.phase,
        "planSeal": args.plan_seal,
        "requiredReviewer": {"id": REVIEWER_ID, "login": REVIEWER_LOGIN},
        "schema": SCHEMA,
        "sourceSha256": SOURCE_SHA256,
        "status": "prepared-not-authorized",
        "target": TARGET,
    }


def main() -> int:
    args = parser().parse_args()
    try:
        grant = build(args)
        output = pathlib.Path(args.output)
        if output.is_symlink() or output.exists():
            raise Refused("output must be a new regular file")
        output.write_text(json.dumps(grant, sort_keys=True, separators=(",", ":")) + "\n")
        os.chmod(output, 0o600)
        return 0
    except (OSError, Refused) as error:
        print(f"callable isolated operation grant refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

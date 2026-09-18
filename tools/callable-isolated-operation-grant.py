#!/usr/bin/env python3
"""Build the bounded V2-CALL-01.4b protected authorization grant."""

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
CONTRACT_SHA256 = "2561b7aa978ade63cbb960310a1154ae62495adeb155085b578e66682338e044"
SOURCE_SHA256 = "9d0797035c7b71c2b58434d04ba66c61e9a527cc33829d38f7c5d27b0b6ff03a"
COORDINATION_REVISION = "cbc73c146e6bba88e21ac3c9f3f8b74b54a7bd94"
AUTHORITY_REPOSITORY = "FS-GG/.github"
WORKFLOW_PATH = ".github/workflows/callable-isolated-operation-authorize.yml"
ENVIRONMENT = "callable-isolated-operation"
TARGET = "FS-GG/FS.GG.Coordination.CallableSandbox"
REVIEWER_ID = 1645484
REVIEWER_LOGIN = "EHotwagner"
SHA256 = re.compile(r"[0-9a-f]{64}")
OID = re.compile(r"[0-9a-f]{40}")
POSITIVE_INTEGER = re.compile(r"[1-9][0-9]{0,18}")


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
    result.add_argument("--app-id", required=True)
    result.add_argument("--installation-id", required=True)
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
    return {
        "appId": bounded_id("app-id", args.app_id),
        "approvedAt": approved.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "authority": {
            "environment": ENVIRONMENT,
            "repository": AUTHORITY_REPOSITORY,
            "runId": bounded_id("run-id", args.run_id),
            "workflowPath": WORKFLOW_PATH,
            "workflowRevision": args.workflow_revision,
            "workflowSha256": args.workflow_sha256,
        },
        "authorized": True,
        "contractSha256": CONTRACT_SHA256,
        "coordinationRevision": COORDINATION_REVISION,
        "expiresAt": expires.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "installationId": bounded_id("installation-id", args.installation_id),
        "operationIdentity": OPERATION_IDENTITY,
        "phase": args.phase,
        "planSeal": args.plan_seal,
        "requiredReviewer": {"id": REVIEWER_ID, "login": REVIEWER_LOGIN},
        "schema": SCHEMA,
        "sourceSha256": SOURCE_SHA256,
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

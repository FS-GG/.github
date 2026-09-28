#!/usr/bin/env python3
"""Independent protected admission for one Q4 seed-journal genesis.

No admission origin, resource, credential or read port is installed here.
The decision authorizes only the exact precomputed journal objects/ref. It
does not authorize a seed issue, Project mutation, or later migration effect.
"""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import re
from urllib.parse import urlsplit


SCHEMA = "fsgg.gs2-09-7.seed-bootstrap-admission/1"
DECISION_SCHEMA = "fsgg.gs2-09-7.seed-bootstrap-decision/1"
PREPARE_SCHEMA = "fsgg.gs2-09-7.seed-prepare-admission/1"
PREPARE_DECISION_SCHEMA = "fsgg.gs2-09-7.seed-prepare-decision/1"
PINNED_ORIGIN = ""
PINNED_RESOURCE_ID = ""
PINNED_ENDPOINT = ""
PINNED_PREPARE_ORIGIN = ""
PINNED_PREPARE_RESOURCE_ID = ""
PINNED_PREPARE_ENDPOINT = ""
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
SUBJECT_FIELDS = {
    "workflowRepository", "workflowPath", "environment", "workflowSha",
    "runId", "runAttempt", "candidateSha", "runNonce", "approvedArtifactSourceSha256",
    "sandboxRepositoryId", "sandboxRepositoryNodeId", "projectNodeId", "appId",
    "installationId", "seedPlanSha256", "s2DeclarationSha256", "refName",
    "mintProofSha256", "tokenSha256", "blobOid", "treeOid", "commitOid",
    "expectedOldOid", "operation",
}


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=True, allow_nan=False).encode("ascii")


def validate_subject(subject: dict) -> None:
    require(type(subject) is dict and set(subject) == SUBJECT_FIELDS, "bootstrap-subject-shape")
    require(subject["workflowRepository"] == "FS-GG/.github"
            and subject["workflowPath"] == ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
            and subject["environment"] == "github-substrate-v2-sandbox"
            and type(subject["workflowSha"]) is str and HEX40.fullmatch(subject["workflowSha"]) is not None
            and type(subject["candidateSha"]) is str and HEX40.fullmatch(subject["candidateSha"]) is not None
            and type(subject["runId"]) is int and subject["runId"] > 0
            and type(subject["runAttempt"]) is int and subject["runAttempt"] > 0,
            "bootstrap-run")
    nonce = f'{subject["runId"]}-{subject["runAttempt"]}-{subject["candidateSha"]}'
    require(subject["runNonce"] == nonce
            and subject["refName"] == f"refs/heads/gs2-09-7/{nonce}/seed-journal"
            and subject["sandboxRepositoryId"] == 1353050537
            and subject["sandboxRepositoryNodeId"] == "R_kgDOUKXpqQ"
            and subject["projectNodeId"] == "PVT_kwDOEYAWY84BiESo"
            and subject["appId"] == 4166418 and subject["installationId"] == 143110413
            and subject["expectedOldOid"] is None
            and subject["operation"] == "genesis-nonce-seed-journal"
            and all(type(subject[name]) is str and HEX64.fullmatch(subject[name]) is not None
                    for name in ("approvedArtifactSourceSha256", "seedPlanSha256",
                                 "s2DeclarationSha256", "mintProofSha256", "tokenSha256"))
            and all(type(subject[name]) is str and HEX40.fullmatch(subject[name]) is not None
                    for name in ("blobOid", "treeOid", "commitOid")),
            "bootstrap-subject")


def decision_id(subject: dict) -> str:
    validate_subject(subject)
    return hashlib.sha256((DECISION_SCHEMA + "\n").encode() + canonical({
        "resourceId": PINNED_RESOURCE_ID, **subject})).hexdigest()


def check_port(port) -> None:
    require(all(type(item) is str and item for item in
                (PINNED_ORIGIN, PINNED_RESOURCE_ID, PINNED_ENDPOINT))
            and port is not None and callable(getattr(port, "describe", None))
            and callable(getattr(port, "read_decision", None)), "bootstrap-admission-unconfigured")
    parsed = urlsplit(PINNED_ENDPOINT)
    require(parsed.scheme == "https" and parsed.netloc and not parsed.username
            and not parsed.password and not parsed.query and not parsed.fragment
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_ORIGIN,
            "bootstrap-admission-endpoint")
    require(port.describe() == {
        "schema": SCHEMA, "origin": PINNED_ORIGIN,
        "resourceId": PINNED_RESOURCE_ID, "endpoint": PINNED_ENDPOINT,
        "durable": True, "immutable": True, "nativeReadback": True,
        "credentialScope": "protected-host-only", "candidateCanRead": False,
        "candidateCanWrite": False, "workflowCanWrite": False,
    }, "bootstrap-admission-port")


def require_admitted(port, subject: dict, now: dt.datetime) -> dict:
    validate_subject(subject)
    check_port(port)
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None, "bootstrap-clock")
    try:
        record = port.read_decision(subject["runId"], subject["runAttempt"])
    except Exception as error:
        raise Refused("bootstrap-admission-readback") from error
    require(type(record) is dict and set(record) == {
        "schema", "resourceId", "decisionId", "state", "subject", "issuedAt", "expiresAt",
        "sealed"}, "bootstrap-decision-shape")
    require(record["schema"] == DECISION_SCHEMA
            and record["resourceId"] == PINNED_RESOURCE_ID
            and record["decisionId"] == decision_id(subject)
            and record["state"] == "admitted"
            and record["subject"] == subject
            and record["sealed"] is True, "bootstrap-decision-binding")
    try:
        issued = dt.datetime.fromisoformat(record["issuedAt"].replace("Z", "+00:00"))
        expiry = dt.datetime.fromisoformat(record["expiresAt"].replace("Z", "+00:00"))
    except (TypeError, ValueError, OverflowError) as error:
        raise Refused("bootstrap-decision-expiry") from error
    require(record["issuedAt"].endswith("Z") and record["expiresAt"].endswith("Z")
            and issued <= now < expiry <= issued + dt.timedelta(minutes=10),
            "bootstrap-decision-expiry")
    return record


def require_prepare_admitted(port, context: dict, source_sha256: str,
                             approved_source_sha256: str, now: dt.datetime) -> dict:
    """Independently permit credential preparation without granting a CAS write."""
    require(all(type(item) is str and item for item in
                (PINNED_PREPARE_ORIGIN, PINNED_PREPARE_RESOURCE_ID, PINNED_PREPARE_ENDPOINT))
            and port is not None and callable(getattr(port, "describe", None))
            and callable(getattr(port, "read_decision", None)), "prepare-admission-unconfigured")
    parsed = urlsplit(PINNED_PREPARE_ENDPOINT)
    require(parsed.scheme == "https" and parsed.netloc and not parsed.username
            and not parsed.password and not parsed.query and not parsed.fragment
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_PREPARE_ORIGIN,
            "prepare-admission-endpoint")
    require(port.describe() == {
        "schema": PREPARE_SCHEMA, "origin": PINNED_PREPARE_ORIGIN,
        "resourceId": PINNED_PREPARE_RESOURCE_ID, "endpoint": PINNED_PREPARE_ENDPOINT,
        "durable": True, "immutable": True, "nativeReadback": True,
        "credentialScope": "protected-host-only", "candidateCanRead": False,
        "candidateCanWrite": False, "workflowCanWrite": False,
    }, "prepare-admission-port")
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None
            and type(source_sha256) is str and HEX64.fullmatch(source_sha256) is not None
            and type(approved_source_sha256) is str
            and HEX64.fullmatch(approved_source_sha256) is not None,
            "prepare-admission-input")
    subject = {
        "workflowRepository": "FS-GG/.github",
        "workflowPath": ".github/workflows/github-substrate-v2-sandbox-qualification.yml",
        "environment": "github-substrate-v2-sandbox",
        "workflowSha": context["workflowSha"], "candidateSha": context["candidateSha"],
        "runId": context["runId"], "runAttempt": context["runAttempt"],
        "runNonce": context["runNonce"],
        "sourceManifestSha256": source_sha256,
        "approvedArtifactSourceSha256": approved_source_sha256,
        "sandboxRepositoryId": 1353050537,
        "sandboxRepositoryNodeId": "R_kgDOUKXpqQ",
        "projectNodeId": "PVT_kwDOEYAWY84BiESo",
        "appId": 4166418, "installationId": 143110413,
        "operation": "prepare-only-no-effect",
    }
    require(type(context["workflowSha"]) is str and HEX40.fullmatch(context["workflowSha"]) is not None
            and type(context["candidateSha"]) is str and HEX40.fullmatch(context["candidateSha"]) is not None
            and type(context["runId"]) is int and context["runId"] > 0
            and type(context["runAttempt"]) is int and context["runAttempt"] > 0
            and context["runNonce"] == f'{context["runId"]}-{context["runAttempt"]}-{context["candidateSha"]}',
            "prepare-admission-subject")
    decision_id = hashlib.sha256((PREPARE_DECISION_SCHEMA + "\n").encode()
                                 + canonical({"resourceId": PINNED_PREPARE_RESOURCE_ID,
                                              **subject})).hexdigest()
    try:
        record = port.read_decision(context["runId"], context["runAttempt"])
    except Exception as error:
        raise Refused("prepare-admission-readback") from error
    require(type(record) is dict and set(record) == {
        "schema", "resourceId", "decisionId", "state", "subject",
        "issuedAt", "expiresAt", "sealed"}
            and record["schema"] == PREPARE_DECISION_SCHEMA
            and record["resourceId"] == PINNED_PREPARE_RESOURCE_ID
            and record["decisionId"] == decision_id
            and record["state"] == "admitted"
            and record["subject"] == subject
            and record["sealed"] is True, "prepare-decision-binding")
    try:
        issued = dt.datetime.fromisoformat(record["issuedAt"].replace("Z", "+00:00"))
        expiry = dt.datetime.fromisoformat(record["expiresAt"].replace("Z", "+00:00"))
    except (TypeError, ValueError, OverflowError) as error:
        raise Refused("prepare-decision-expiry") from error
    require(record["issuedAt"].endswith("Z") and record["expiresAt"].endswith("Z")
            and issued <= now < expiry <= issued + dt.timedelta(minutes=10),
            "prepare-decision-expiry")
    return record

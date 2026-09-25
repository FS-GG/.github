#!/usr/bin/env python3
"""Source-only protected release admission readback for GS2-09.7.

No durable authority or adapter is installed. All protected pins are empty.
The caller must keep this port outside the candidate and workflow workspace.
"""

import re
from typing import Protocol
from urllib.parse import urlsplit


SCHEMA = "fsgg.github-substrate-v2.sandbox-host-release-admission/1"
RECORD_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-release-decision/1"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")

PINNED_ADMISSION_ORIGIN = ""
PINNED_ADMISSION_RESOURCE_ID = ""
PINNED_ADMISSION_ENDPOINT = ""
PINNED_RELEASE_POLICY_SHA256 = ""


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedAdmissionPort(Protocol):
    def describe(self) -> dict: ...
    def read_admission(self, run_id: int, run_attempt: int) -> dict: ...


def check_port(port: ProtectedAdmissionPort | None) -> None:
    require(all(type(value) is str and bool(value) for value in (
        PINNED_ADMISSION_ORIGIN, PINNED_ADMISSION_RESOURCE_ID,
        PINNED_ADMISSION_ENDPOINT, PINNED_RELEASE_POLICY_SHA256)),
        "admission-unconfigured")
    require(bool(HEX64.fullmatch(PINNED_RELEASE_POLICY_SHA256)),
            "admission-unconfigured")
    require(port is not None and callable(getattr(port, "describe", None))
            and callable(getattr(port, "read_admission", None)),
            "admission-unconfigured")
    try:
        descriptor = port.describe()
    except Exception as error:
        raise Refused("admission-descriptor-unknown") from error
    require(type(descriptor) is dict and set(descriptor) == {
        "schema", "origin", "resourceId", "endpoint", "durable", "immutable",
        "nativeReadback", "credentialScope", "candidateCanRead",
        "candidateCanWrite", "workflowCanWrite",
    }, "admission-descriptor")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "admission-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("admission-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_ADMISSION_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_ADMISSION_ORIGIN,
            "admission-endpoint")
    require(descriptor == {
        "schema": SCHEMA, "origin": PINNED_ADMISSION_ORIGIN,
        "resourceId": PINNED_ADMISSION_RESOURCE_ID,
        "endpoint": PINNED_ADMISSION_ENDPOINT,
        "durable": True, "immutable": True, "nativeReadback": True,
        "credentialScope": "protected-host-only",
        "candidateCanRead": False, "candidateCanWrite": False,
        "workflowCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["immutable"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["candidateCanRead"] is False
      and descriptor["candidateCanWrite"] is False
      and descriptor["workflowCanWrite"] is False,
            "admission-authority")


def require_admitted(port: ProtectedAdmissionPort | None, context: dict,
                     signer_spki_sha256: str, target: dict) -> dict:
    """Require native current readback of one exact protected decision."""
    check_port(port)
    require(type(context) is dict and type(target) is dict
            and type(signer_spki_sha256) is str
            and HEX64.fullmatch(signer_spki_sha256), "admission-input")
    try:
        record = port.read_admission(context["runId"], context["runAttempt"])
    except Exception as error:
        raise Refused("admission-readback-unknown") from error
    require(type(record) is dict and set(record) == {
        "schema", "resourceId", "decisionId", "state", "workflowRepository",
        "workflowPath", "environment", "workflowSha", "candidateSha",
        "runId", "runAttempt", "runNonce", "sandboxRepositoryId",
        "sandboxRepositoryNodeId", "projectNodeId", "signerSpkiSha256",
        "releasePolicySha256", "sealed",
    }, "admission-record")
    expected = {
        "schema": RECORD_SCHEMA,
        "resourceId": PINNED_ADMISSION_RESOURCE_ID,
        "decisionId": record["decisionId"],
        "state": "admitted",
        "workflowRepository": target["workflowRepository"],
        "workflowPath": target["workflowPath"],
        "environment": target["environment"],
        "workflowSha": context["workflowSha"],
        "candidateSha": context["candidateSha"],
        "runId": context["runId"],
        "runAttempt": context["runAttempt"],
        "runNonce": context["runNonce"],
        "sandboxRepositoryId": target["sandboxRepositoryId"],
        "sandboxRepositoryNodeId": target["sandboxRepositoryNodeId"],
        "projectNodeId": target["projectNodeId"],
        "signerSpkiSha256": signer_spki_sha256,
        "releasePolicySha256": PINNED_RELEASE_POLICY_SHA256,
        "sealed": True,
    }
    require(type(record["decisionId"]) is str
            and HEX64.fullmatch(record["decisionId"])
            and type(record["runId"]) is int
            and type(record["runAttempt"]) is int
            and type(record["sandboxRepositoryId"]) is int
            and record["sealed"] is True
            and record == expected, "admission-binding")
    return record

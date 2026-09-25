#!/usr/bin/env python3
"""No-effect durable generation floor for GS2-09.7 joint-head verification.

There is no installed floor authority, credential, endpoint or writer. The
port is injected only by fake tests until a protected owner installs it.
"""

import copy
import re
from typing import Protocol
from urllib.parse import urlsplit


AUTHORITY_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-head-floor-authority/1"
RECORD_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-head-floor-record/1"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")

PINNED_FLOOR_ORIGIN = ""
PINNED_FLOOR_ENDPOINT = ""
PINNED_FLOOR_RESOURCE_ID = ""


class Refused(Exception):
    pass


class ProtectedHeadFloorPort(Protocol):
    def describe_head_floor(self) -> dict: ...
    def read_head_floor(self) -> dict: ...
    def advance_head_floor_once(self, expected: dict, generation: int,
                                seal_id: str) -> str: ...


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def _record(value: object, store_id: str, signer_id: str,
            policy_sha256: str) -> dict:
    require(type(value) is dict and set(value) == {
        "schema", "floorResourceId", "storeResourceId", "signerResourceId",
        "policySha256", "generation", "sealId", "state",
    } and value["schema"] == RECORD_SCHEMA
      and value["floorResourceId"] == PINNED_FLOOR_RESOURCE_ID
      and value["storeResourceId"] == store_id
      and value["signerResourceId"] == signer_id
      and value["policySha256"] == policy_sha256
      and type(value["generation"]) is int
      and value["generation"] >= 0
      and ((value["generation"] == 0 and value["sealId"] is None)
           or (value["generation"] > 0
               and type(value["sealId"]) is str
               and HEX64.fullmatch(value["sealId"])))
      and value["state"] == "committed", "joint-floor-record")
    return value


def require_monotonic(port: ProtectedHeadFloorPort | None, head: dict,
                      store_id: str, signer_id: str,
                      policy_sha256: str) -> None:
    """CAS a nondecreasing exact (generation, seal) floor before authorization."""
    require(all(type(value) is str and bool(value) for value in (
        PINNED_FLOOR_ORIGIN, PINNED_FLOOR_ENDPOINT,
        PINNED_FLOOR_RESOURCE_ID, store_id, signer_id, policy_sha256)),
        "joint-floor-unconfigured")
    require(type(head) is dict and type(head.get("generation")) is int
            and head["generation"] > 0
            and type(head.get("sealId")) is str
            and HEX64.fullmatch(head["sealId"])
            and type(policy_sha256) is str
            and HEX64.fullmatch(policy_sha256), "joint-floor-input")
    methods = ("describe_head_floor", "read_head_floor",
               "advance_head_floor_once")
    require(port is not None and all(callable(getattr(port, name, None))
                                     for name in methods),
            "joint-floor-unconfigured")
    try:
        descriptor = port.describe_head_floor()
    except Exception as error:
        raise Refused("joint-floor-authority-unknown") from error
    require(type(descriptor) is dict and descriptor == {
        "schema": AUTHORITY_SCHEMA, "origin": PINNED_FLOOR_ORIGIN,
        "endpoint": PINNED_FLOOR_ENDPOINT,
        "resourceId": PINNED_FLOOR_RESOURCE_ID,
        "storeResourceId": store_id, "signerResourceId": signer_id,
        "durable": True, "atomicMax": True, "nativeReadback": True,
        "credentialScope": "protected-host-only",
        "candidateCanRead": False, "candidateCanWrite": False,
        "workflowCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["atomicMax"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["candidateCanRead"] is False
      and descriptor["candidateCanWrite"] is False
      and descriptor["workflowCanWrite"] is False,
            "joint-floor-authority")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "joint-floor-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("joint-floor-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_FLOOR_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_FLOOR_ORIGIN,
            "joint-floor-endpoint")
    try:
        before = _record(copy.deepcopy(port.read_head_floor()),
                         store_id, signer_id, policy_sha256)
    except Refused:
        raise
    except Exception as error:
        raise Refused("joint-floor-readback-unknown") from error
    generation = head["generation"]
    seal_id = head["sealId"]
    require(before["generation"] <= generation, "joint-floor-rollback")
    if before["generation"] == generation:
        require(before["sealId"] == seal_id, "joint-floor-fork")
    else:
        try:
            result = port.advance_head_floor_once(before, generation, seal_id)
        except Exception as error:
            raise Refused("joint-floor-advance-unknown") from error
        require(result in ("committed", "duplicate"), "joint-floor-advance-unknown")
    try:
        after = _record(copy.deepcopy(port.read_head_floor()),
                        store_id, signer_id, policy_sha256)
        again = _record(copy.deepcopy(port.read_head_floor()),
                        store_id, signer_id, policy_sha256)
    except Refused:
        raise
    except Exception as error:
        raise Refused("joint-floor-readback-unknown") from error
    require(type(after["generation"]) is int
            and after == again
            and after["generation"] == generation
            and after["sealId"] == seal_id,
            "joint-floor-readback")

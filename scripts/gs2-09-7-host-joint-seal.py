#!/usr/bin/env python3
"""No-effect authentication of a protected mint/pending joint seal.

The signer, durable store, public-key pin, and adapter are deliberately absent.
An installed host must provide independent protected reads, not candidate data.
"""

import base64
import binascii
import copy
import datetime as dt
import importlib.util
import json
from pathlib import Path
import re
import secrets
from typing import Protocol
from urllib.parse import urlsplit


RELEASE_SOURCE = Path(__file__).with_name("gs2-09-7-host-token-release.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_release_for_joint_seal", RELEASE_SOURCE)
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)

AUTHORITY_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-joint-seal-authority/2"
ENVELOPE_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-joint-seal-envelope/1"
RECORD_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-joint-seal-record/1"
HEAD_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-joint-seal-head/1"
HEAD_ATTESTATION_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-head-attestation/1"
HEAD_RECORD_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-head-record/1"
HEX64 = re.compile(r"[0-9a-f]{64}\Z")

PINNED_JOINT_SEAL_ORIGIN = ""
PINNED_JOINT_SEAL_ENDPOINT = ""
PINNED_JOINT_SEAL_STORE_ID = ""
PINNED_JOINT_SEAL_SIGNER_ID = ""
PINNED_JOINT_SEAL_SPKI_SHA256 = ""
PINNED_JOINT_SEAL_POLICY_SHA256 = ""


class Refused(Exception):
    pass


class ProtectedJointSealPort(Protocol):
    def describe_joint_seal(self) -> dict: ...
    def read_joint_seal_envelope(self, seal_id: str) -> dict: ...
    def read_joint_seal_head(self, challenge: str) -> dict: ...
    def read_joint_seal_public_key(self) -> bytes: ...


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def canonical(record: dict) -> bytes:
    return (RECORD_SCHEMA + "\n").encode("ascii") + _json(record)


def canonical_head(record: dict) -> bytes:
    return (HEAD_RECORD_SCHEMA + "\n").encode("ascii") + _json(record)


def _json(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"),
                      ensure_ascii=True, allow_nan=False).encode("ascii")


def _same_json(left: object, right: object) -> bool:
    try:
        return _json(left) == _json(right)
    except (TypeError, ValueError, UnicodeError):
        return False


def _time(value: object) -> dt.datetime:
    require(type(value) is str and value.endswith("Z"), "joint-seal-time")
    try:
        parsed = dt.datetime.fromisoformat(value[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused("joint-seal-time") from error
    require(parsed.isoformat(timespec="seconds").replace("+00:00", "Z") == value,
            "joint-seal-time")
    return parsed


def _signature(encoded: object) -> bytes:
    require(type(encoded) is str and bool(encoded), "joint-seal-signature")
    try:
        signature = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error) as error:
        raise Refused("joint-seal-signature") from error
    require(bool(signature), "joint-seal-signature")
    return signature


def _attested_head(value: object, challenge: str, key: bytes,
                   now: dt.datetime) -> dict:
    require(type(value) is dict and set(value) == {
        "schema", "record", "signatureBase64",
    } and value["schema"] == HEAD_ATTESTATION_SCHEMA,
            "joint-seal-head-attestation")
    record = value["record"]
    require(type(record) is dict and set(record) == {
        "schema", "head", "challenge", "storeResourceId",
        "signerResourceId", "policySha256", "observedAt",
    } and record["schema"] == HEAD_RECORD_SCHEMA
      and type(record["challenge"]) is str
      and record["challenge"] == challenge
      and type(record["head"]) is dict
      and record["storeResourceId"] == PINNED_JOINT_SEAL_STORE_ID
      and record["signerResourceId"] == PINNED_JOINT_SEAL_SIGNER_ID
      and record["policySha256"] == PINNED_JOINT_SEAL_POLICY_SHA256,
            "joint-seal-head-challenge")
    observed = _time(record["observedAt"])
    require(now - dt.timedelta(seconds=60) <= observed
            <= now + dt.timedelta(seconds=5), "joint-seal-head-stale")
    try:
        release.verify_signature(key, canonical_head(record),
                                 _signature(value["signatureBase64"]))
    except release.Refused as error:
        raise Refused("joint-seal-head-signature") from error
    return record["head"]


def verify_joint_seal(port: ProtectedJointSealPort | None, seal: dict,
                      now: dt.datetime) -> dict:
    """Require a pinned signature plus two fresh signed durable head reads."""
    require(all(type(value) is str and bool(value) for value in (
        PINNED_JOINT_SEAL_ORIGIN, PINNED_JOINT_SEAL_ENDPOINT,
        PINNED_JOINT_SEAL_STORE_ID, PINNED_JOINT_SEAL_SIGNER_ID,
        PINNED_JOINT_SEAL_SPKI_SHA256, PINNED_JOINT_SEAL_POLICY_SHA256)),
        "joint-seal-unconfigured")
    require(HEX64.fullmatch(PINNED_JOINT_SEAL_SPKI_SHA256)
            and HEX64.fullmatch(PINNED_JOINT_SEAL_POLICY_SHA256),
            "joint-seal-unconfigured")
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None, "joint-seal-clock")
    methods = ("describe_joint_seal", "read_joint_seal_envelope",
               "read_joint_seal_head", "read_joint_seal_public_key")
    require(port is not None and all(callable(getattr(port, method, None))
                                     for method in methods),
            "joint-seal-unconfigured")
    require(type(seal) is dict and type(seal.get("sealId")) is str
            and HEX64.fullmatch(seal["sealId"]), "joint-seal-input")
    first_challenge = secrets.token_hex(32)
    second_challenge = secrets.token_hex(32)
    require(type(first_challenge) is str and type(second_challenge) is str
            and HEX64.fullmatch(first_challenge)
            and HEX64.fullmatch(second_challenge)
            and first_challenge != second_challenge,
            "joint-seal-head-challenge")
    try:
        descriptor = port.describe_joint_seal()
        first_attestation = copy.deepcopy(
            port.read_joint_seal_head(first_challenge))
        envelope = copy.deepcopy(port.read_joint_seal_envelope(seal["sealId"]))
        key = port.read_joint_seal_public_key()
        second_attestation = copy.deepcopy(
            port.read_joint_seal_head(second_challenge))
    except Exception as error:
        raise Refused("joint-seal-readback-unknown") from error
    require(type(descriptor) is dict and descriptor == {
        "schema": AUTHORITY_SCHEMA,
        "origin": PINNED_JOINT_SEAL_ORIGIN,
        "endpoint": PINNED_JOINT_SEAL_ENDPOINT,
        "storeResourceId": PINNED_JOINT_SEAL_STORE_ID,
        "signerResourceId": PINNED_JOINT_SEAL_SIGNER_ID,
        "durable": True, "appendOnly": True, "nativeReadback": True,
        "linearizableHead": True, "challengeBoundReadback": True,
        "credentialScope": "protected-host-only",
        "candidateCanRead": False, "candidateCanWrite": False,
        "workflowCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["appendOnly"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["linearizableHead"] is True
      and descriptor["challengeBoundReadback"] is True
      and descriptor["candidateCanRead"] is False
      and descriptor["candidateCanWrite"] is False
      and descriptor["workflowCanWrite"] is False,
            "joint-seal-authority")
    try:
        endpoint = urlsplit(descriptor["endpoint"])
        endpoint.port
    except ValueError as error:
        raise Refused("joint-seal-endpoint") from error
    require(endpoint.scheme == "https" and bool(endpoint.hostname)
            and not endpoint.username and not endpoint.password
            and not endpoint.query and not endpoint.fragment
            and f"{endpoint.scheme}://{endpoint.netloc}" == PINNED_JOINT_SEAL_ORIGIN,
            "joint-seal-endpoint")
    try:
        require(release.public_spki_sha256(key) == PINNED_JOINT_SEAL_SPKI_SHA256,
                "joint-seal-trust-anchor")
    except release.Refused as error:
        raise Refused("joint-seal-signature") from error
    head = _attested_head(first_attestation, first_challenge, key, now)
    head_after = _attested_head(second_attestation, second_challenge, key, now)
    require(type(head) is dict and type(head_after) is dict
            and _same_json(head, head_after) and set(head) == {
                "schema", "storeResourceId", "generation", "sealId",
                "highWater", "pendingSha256", "mintSha256",
            }
            and head["schema"] == HEAD_SCHEMA
            and head["storeResourceId"] == PINNED_JOINT_SEAL_STORE_ID
            and type(head["generation"]) is int and head["generation"] > 0
            and head["sealId"] == seal["sealId"]
            and type(head["highWater"]) is int
            and head["highWater"] == seal.get("highWater")
            and head["pendingSha256"] == seal.get("pendingSha256")
            and head["mintSha256"] == seal.get("mintSha256"),
            "joint-seal-head")
    require(type(envelope) is dict and set(envelope) == {
        "schema", "record", "signatureBase64",
    } and envelope["schema"] == ENVELOPE_SCHEMA, "joint-seal-envelope")
    record = envelope["record"]
    require(type(record) is dict and set(record) == {
        "schema", "seal", "head", "storeResourceId", "signerResourceId",
        "policySha256", "issuedAt", "expiresAt",
    } and record["schema"] == RECORD_SCHEMA
      and type(record["seal"]) is dict and _same_json(record["seal"], seal)
      and type(record["head"]) is dict and _same_json(record["head"], head)
      and record["storeResourceId"] == PINNED_JOINT_SEAL_STORE_ID
      and record["signerResourceId"] == PINNED_JOINT_SEAL_SIGNER_ID
      and record["policySha256"] == PINNED_JOINT_SEAL_POLICY_SHA256,
            "joint-seal-binding")
    issued = _time(record["issuedAt"])
    expiry = _time(record["expiresAt"])
    require(issued <= now < expiry
            and expiry <= issued + dt.timedelta(minutes=15),
            "joint-seal-stale")
    try:
        release.verify_signature(key, canonical(record),
                                 _signature(envelope["signatureBase64"]))
    except release.Refused as error:
        raise Refused("joint-seal-signature") from error
    return {"sealId": seal["sealId"], "generation": head["generation"],
            "storeResourceId": PINNED_JOINT_SEAL_STORE_ID}

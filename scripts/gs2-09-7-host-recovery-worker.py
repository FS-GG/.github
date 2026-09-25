#!/usr/bin/env python3
"""Source-only protected worker for one GS2-09.7 pending revocation.

The worker has no installed scheduler, journal, vault, revoker, or credential.
Every production identity pin remains empty and prevents provider effects.
"""

import importlib.util
import secrets
from pathlib import Path
from typing import Protocol
from urllib.parse import urlsplit


FINALIZER_SOURCE = Path(__file__).with_name("gs2-09-7-host-refusal-finalizer.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_finalizer_for_recovery", FINALIZER_SOURCE)
finalizer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(finalizer)

WORKER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-worker/1"
BINDING_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-binding/1"
CLAIM_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-claim/1"
RECEIPT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-recovery-receipt/1"

PINNED_RECOVERY_ORIGIN = ""
PINNED_RECOVERY_RESOURCE_ID = ""
PINNED_RECOVERY_ENDPOINT = ""
PINNED_RECOVERY_WORKER_ID = ""


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedRecoveryPort(finalizer.ProtectedFinalizerPort, Protocol):
    def describe_recovery(self) -> dict: ...
    def read_binding(self, mint_id: str) -> dict: ...
    def claim_recovery_once(self, mint_id: str, binding_id: str) -> str: ...
    def read_recovery_claim(self, mint_id: str) -> dict: ...
    def append_recovery_receipt(self, mint_id: str, binding_id: str,
                                token_sha256: str) -> str: ...
    def read_recovery_receipt(self, mint_id: str) -> dict: ...


def check_worker(port: ProtectedRecoveryPort | None) -> None:
    require(all((PINNED_RECOVERY_ORIGIN, PINNED_RECOVERY_RESOURCE_ID,
                 PINNED_RECOVERY_ENDPOINT, PINNED_RECOVERY_WORKER_ID)),
            "recovery-unconfigured")
    methods = ("describe_recovery", "read_binding", "claim_recovery_once",
               "read_recovery_claim", "append_recovery_receipt",
               "read_recovery_receipt")
    require(port is not None and all(callable(getattr(port, name, None)) for name in methods),
            "recovery-unconfigured")
    descriptor = port.describe_recovery()
    require(type(descriptor) is dict and set(descriptor) == {
        "schema", "origin", "resourceId", "endpoint", "workerId", "durable",
        "atomicCas", "nativeReadback", "credentialScope", "candidateCanWrite",
    }, "recovery-descriptor")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "recovery-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("recovery-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_RECOVERY_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_RECOVERY_ORIGIN,
            "recovery-endpoint")
    require(descriptor == {
        "schema": WORKER_SCHEMA, "origin": PINNED_RECOVERY_ORIGIN,
        "resourceId": PINNED_RECOVERY_RESOURCE_ID,
        "endpoint": PINNED_RECOVERY_ENDPOINT,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "durable": True, "atomicCas": True, "nativeReadback": True,
        "credentialScope": "protected-host-only", "candidateCanWrite": False,
    } and descriptor["durable"] is True
      and descriptor["atomicCas"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["candidateCanWrite"] is False, "recovery-authority")


def _binding_record(port: ProtectedRecoveryPort, mint_id: str,
                    binding_id: str, mint: dict, token_sha256: str) -> None:
    record = port.read_binding(mint_id)
    require(type(record) is dict and record == {
        "schema": BINDING_SCHEMA,
        "mintId": mint_id,
        "bindingId": binding_id,
        "tokenSha256": token_sha256,
        "contextSha256": mint["contextSha256"],
        "sandboxRepositoryId": finalizer.release.host.SANDBOX_ID,
        "appId": finalizer.release.host.APP_ID,
        "actor": finalizer.release.host.ACTOR,
        "installationId": mint["installationId"],
        "vaultId": finalizer.PINNED_TOKEN_VAULT_ID,
        "finalizerResourceId": finalizer.PINNED_FINALIZER_RESOURCE_ID,
        "verifiedAtCommit": True,
    } and record["verifiedAtCommit"] is True, "recovery-binding")


def _claim_readback(port: ProtectedRecoveryPort, mint_id: str,
                    binding_id: str, token_sha256: str) -> bool:
    try:
        claim = port.read_recovery_claim(mint_id)
    except Exception:
        return False
    return type(claim) is dict and claim == {
        "schema": CLAIM_SCHEMA, "mintId": mint_id,
        "bindingId": binding_id, "tokenSha256": token_sha256,
        "workerId": PINNED_RECOVERY_WORKER_ID,
    }


def _receipt_readback(port: ProtectedRecoveryPort, mint_id: str,
                      binding_id: str, token_sha256: str) -> bool:
    try:
        receipt = port.read_recovery_receipt(mint_id)
    except Exception:
        return False
    return type(receipt) is dict and receipt == {
        "schema": RECEIPT_SCHEMA, "mintId": mint_id,
        "bindingId": binding_id, "tokenSha256": token_sha256,
        "workerId": PINNED_RECOVERY_WORKER_ID,
        "providerState": "revoked",
    }


def _observe(port: ProtectedRecoveryPort, token: str) -> str:
    try:
        state = port.observe(token)
    except Exception:
        return "unknown"
    return state if state in ("active", "revoked") else "unknown"


def recover_one(port: ProtectedRecoveryPort | None, mint_id: str,
                expected_binding_id: str) -> dict:
    """Attempt one native revoke after a fresh protected recovery claim.

    Duplicate or lost claim results permit observation and durable receipt
    only. They never permit another provider mutation or candidate invocation.
    """
    require(type(mint_id) is str and finalizer.release.host.HEX64.fullmatch(mint_id)
            and type(expected_binding_id) is str
            and finalizer.release.host.HEX64.fullmatch(expected_binding_id),
            "recovery-subject")
    check_worker(port)
    finalizer.check_port(port)
    finalizer.check_vault(port)
    finalizer.check_revoker(port)
    token = port.recover_token(mint_id)
    token_sha256 = finalizer.digest_token(token)
    mint = finalizer.mint_record(port, mint_id, token_sha256)
    _binding_record(port, mint_id, expected_binding_id, mint, token_sha256)
    require(finalizer._read_state(port, "read_pending", finalizer.PENDING_SCHEMA,
                                  mint_id, token_sha256), "pending-intent")

    try:
        claim = port.claim_recovery_once(mint_id, expected_binding_id)
    except Exception:
        claim = "unknown"
    claim_state = "fresh" if claim == "committed" else \
                  "duplicate" if claim == "duplicate" else "unknown"
    revocation = "pending"
    claim_readback = _claim_readback(port, mint_id, expected_binding_id,
                                     token_sha256)
    if not claim_readback:
        claim_state = "unknown"
    if claim_readback:
        attempt = finalizer.native_attempt_record(
            mint_id, token_sha256, mint["contextSha256"],
            secrets.token_hex(32))
        prior = finalizer._read_native_attempt(port, mint_id, attempt)
        if prior != "unknown":
            state = _observe(port, token)
            if claim_state == "fresh" and state == "active" and prior == "absent":
                try:
                    claimed = port.claim_native_attempt_once(
                        mint_id, token_sha256, attempt["attemptId"])
                except Exception:
                    claimed = "unknown"
                if claimed == "committed" and finalizer._read_native_attempt(
                        port, mint_id, attempt,
                        exact_attempt_id=True) == "exact":
                    try:
                        port.revoke(token)
                    except Exception:
                        pass
                    state = _observe(port, token)
            if state == "revoked":
                try:
                    port.append_recovery_receipt(mint_id, expected_binding_id,
                                                 token_sha256)
                except Exception:
                    pass
                if _receipt_readback(port, mint_id, expected_binding_id,
                                     token_sha256):
                    revocation = "revoked"
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery-verdict/1",
            "mintId": mint_id, "bindingId": expected_binding_id,
            "claim": claim_state, "revocation": revocation,
            "disposition": "pending"}

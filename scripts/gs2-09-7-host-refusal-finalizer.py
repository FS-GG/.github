#!/usr/bin/env python3
"""Source-only outer finalizer for a protected GS2-09.7 minted token.

No workflow, durable store, token vault, revoker, or credential invokes this
module. All protected identity pins are empty so the model fails closed.
"""

import hashlib
import importlib.util
import json
from pathlib import Path
from typing import Protocol
from urllib.parse import urlsplit


RELEASE_SOURCE = Path(__file__).with_name("gs2-09-7-host-token-release.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_release_for_finalizer", RELEASE_SOURCE)
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)

FINALIZER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-finalizer/1"
MINT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-mint-record/1"
PENDING_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-pending/1"
REVOKED_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-revoked/1"
VAULT_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-token-vault/1"
REVOKER_SCHEMA = "fsgg.github-substrate-v2.sandbox-host-native-revoker/1"

# A protected source review must pin the exact independently credentialed
# ledger and vault. Empty pins prohibit any candidate invocation here.
PINNED_FINALIZER_ORIGIN = ""
PINNED_FINALIZER_RESOURCE_ID = ""
PINNED_FINALIZER_ENDPOINT = ""
PINNED_TOKEN_VAULT_ID = ""
PINNED_REVOKER_ID = ""


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedFinalizerPort(Protocol):
    def describe(self) -> dict: ...
    def describe_vault(self) -> dict: ...
    def describe_revoker(self) -> dict: ...
    def load_mint(self, mint_id: str) -> dict: ...
    def recover_token(self, mint_id: str) -> str: ...
    def append_pending(self, mint_id: str, token_sha256: str) -> str: ...
    def read_pending(self, mint_id: str) -> dict: ...
    def revoke(self, token: str) -> str: ...
    def observe(self, token: str) -> str: ...
    def append_revoked(self, mint_id: str, token_sha256: str) -> str: ...
    def read_revoked(self, mint_id: str) -> dict: ...


def digest_token(token: str) -> str:
    require(type(token) is str and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token), "token")
    return hashlib.sha256(token.encode("ascii")).hexdigest()


def digest_context(context: dict) -> str:
    require(type(context) is dict and set(context) == {
        "workflowRepository", "workflowRef", "workflowRefPath", "workflowSha",
        "protectedSha", "runId", "runAttempt", "candidateSha", "runNonce",
    }, "runner-context")
    require(context["workflowRepository"] == release.host.HOST_REPOSITORY
            and context["workflowRef"] == "refs/heads/main"
            and context["workflowRefPath"] ==
                f"{release.host.HOST_REPOSITORY}/{release.host.WORKFLOW}@refs/heads/main"
            and type(context["workflowSha"]) is str
            and release.host.HEX40.fullmatch(context["workflowSha"])
            and context["protectedSha"] == context["workflowSha"]
            and type(context["runId"]) is int and context["runId"] > 0
            and type(context["runAttempt"]) is int and context["runAttempt"] > 0
            and type(context["candidateSha"]) is str
            and release.host.HEX40.fullmatch(context["candidateSha"])
            and context["runNonce"] ==
                f'{context["runId"]}-{context["runAttempt"]}-{context["candidateSha"]}',
            "runner-context")
    raw = json.dumps(context, sort_keys=True, separators=(",", ":"),
                     ensure_ascii=True).encode("ascii")
    return hashlib.sha256(raw).hexdigest()


def check_port(port: ProtectedFinalizerPort | None) -> None:
    require(all((PINNED_FINALIZER_ORIGIN, PINNED_FINALIZER_RESOURCE_ID,
                 PINNED_FINALIZER_ENDPOINT, PINNED_TOKEN_VAULT_ID)),
            "finalizer-unconfigured")
    methods = ("describe", "load_mint", "recover_token", "append_pending",
               "read_pending", "revoke", "observe", "append_revoked", "read_revoked")
    require(port is not None and all(callable(getattr(port, name, None)) for name in methods),
            "finalizer-unconfigured")
    descriptor = port.describe()
    require(type(descriptor) is dict and set(descriptor) == {
        "schema", "origin", "resourceId", "endpoint", "vaultId", "durable",
        "atomicCas", "nativeReadback", "escrowEncrypted", "credentialScope",
        "candidateCanWrite", "apiOrigin", "nativeRevocation",
    }, "finalizer-descriptor")
    endpoint = descriptor["endpoint"]
    require(type(endpoint) is str, "finalizer-endpoint")
    try:
        parsed = urlsplit(endpoint)
        parsed.port
    except ValueError as error:
        raise Refused("finalizer-endpoint") from error
    require(parsed.scheme == "https" and bool(parsed.hostname)
            and not parsed.username and not parsed.password
            and not parsed.query and not parsed.fragment
            and endpoint == PINNED_FINALIZER_ENDPOINT
            and f"{parsed.scheme}://{parsed.netloc}" == PINNED_FINALIZER_ORIGIN,
            "finalizer-endpoint")
    require(descriptor == {
        "schema": FINALIZER_SCHEMA,
        "origin": PINNED_FINALIZER_ORIGIN,
        "resourceId": PINNED_FINALIZER_RESOURCE_ID,
        "endpoint": PINNED_FINALIZER_ENDPOINT,
        "vaultId": PINNED_TOKEN_VAULT_ID,
        "durable": True,
        "atomicCas": True,
        "nativeReadback": True,
        "escrowEncrypted": True,
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
        "apiOrigin": "https://api.github.com",
        "nativeRevocation": True,
    } and descriptor["durable"] is True
      and descriptor["atomicCas"] is True
      and descriptor["nativeReadback"] is True
      and descriptor["escrowEncrypted"] is True
      and descriptor["candidateCanWrite"] is False
      and descriptor["nativeRevocation"] is True, "finalizer-authority")


def check_vault(port: ProtectedFinalizerPort | None) -> None:
    require(bool(PINNED_TOKEN_VAULT_ID), "vault-unconfigured")
    require(port is not None and callable(getattr(port, "describe_vault", None))
            and callable(getattr(port, "recover_token", None)), "vault-unconfigured")
    descriptor = port.describe_vault()
    require(type(descriptor) is dict and descriptor == {
        "schema": VAULT_SCHEMA, "vaultId": PINNED_TOKEN_VAULT_ID,
        "credentialScope": "protected-host-only",
        "candidateCanRead": False, "candidateCanWrite": False,
        "encrypted": True, "durable": True,
    } and descriptor["candidateCanRead"] is False
      and descriptor["candidateCanWrite"] is False
      and descriptor["encrypted"] is True and descriptor["durable"] is True,
            "vault-authority")


def check_revoker(port: ProtectedFinalizerPort | None) -> None:
    require(bool(PINNED_REVOKER_ID), "revoker-unconfigured")
    require(port is not None and callable(getattr(port, "describe_revoker", None))
            and callable(getattr(port, "revoke", None))
            and callable(getattr(port, "observe", None)), "revoker-unconfigured")
    descriptor = port.describe_revoker()
    require(type(descriptor) is dict and descriptor == {
        "schema": REVOKER_SCHEMA, "resourceId": PINNED_REVOKER_ID,
        "apiOrigin": "https://api.github.com",
        "credentialScope": "protected-host-only", "candidateCanWrite": False,
        "nativeObservation": True,
    } and descriptor["candidateCanWrite"] is False
      and descriptor["nativeObservation"] is True, "revoker-authority")


def mint_record(port: ProtectedFinalizerPort, mint_id: str,
                token_sha256: str, context_sha256: str | None = None) -> dict:
    require(type(mint_id) is str and release.host.HEX64.fullmatch(mint_id), "mint-id")
    record = port.load_mint(mint_id)
    require(type(record) is dict and set(record) == {
        "schema", "mintId", "tokenSha256", "contextSha256", "sandboxRepositoryId",
        "appId", "actor", "installationId", "vaultId", "escrowed",
    }, "mint-record")
    require(record["schema"] == MINT_SCHEMA
            and record["mintId"] == mint_id
            and record["tokenSha256"] == token_sha256
            and type(record["contextSha256"]) is str
            and release.host.HEX64.fullmatch(record["contextSha256"])
            and (context_sha256 is None or record["contextSha256"] == context_sha256)
            and type(record["sandboxRepositoryId"]) is int
            and record["sandboxRepositoryId"] == release.host.SANDBOX_ID
            and type(record["appId"]) is int
            and record["appId"] == release.host.APP_ID
            and record["actor"] == release.host.ACTOR
            and type(record["installationId"]) is int
            and record["installationId"] > 0
            and record["vaultId"] == PINNED_TOKEN_VAULT_ID
            and record["escrowed"] is True, "mint-identity")
    return record


def _read_state(port: ProtectedFinalizerPort, method: str, schema: str,
                mint_id: str, token_sha256: str) -> bool:
    try:
        state = getattr(port, method)(mint_id)
    except Exception:
        return False
    expected = {
        "schema": schema, "mintId": mint_id, "tokenSha256": token_sha256,
    }
    if schema == PENDING_SCHEMA:
        expected["revokeRequired"] = True
    return (type(state) is dict and state == expected
            and (schema != PENDING_SCHEMA or state["revokeRequired"] is True))


def _finalize(port: ProtectedFinalizerPort, mint_id: str, token: str,
              may_attempt_revoke: bool = False) -> str:
    """Observe on validated replay; a known duplicate skips native revoke."""
    try:
        check_revoker(port)
    except Exception:
        return "pending"
    try:
        check_port(port)
    except Exception:
        _emergency_revoke(port, token)
        return "pending"
    token_sha256 = digest_token(token)
    pending = _read_state(port, "read_pending", PENDING_SCHEMA,
                          mint_id, token_sha256)
    try:
        observed = port.observe(token)
    except Exception:
        observed = "unknown"
    if observed == "active" and may_attempt_revoke:
        try:
            port.revoke(token)
        except Exception:
            pass
        try:
            observed = port.observe(token)
        except Exception:
            observed = "unknown"
    # An unknown first observation may follow a lost prior revoke result.
    # Only a protected one-use native effect authority can safely retry it.
    if observed != "revoked" or not pending:
        return "pending"
    try:
        port.append_revoked(mint_id, token_sha256)
    except Exception:
        pass
    return ("revoked" if _read_state(port, "read_revoked", REVOKED_SCHEMA,
                                     mint_id, token_sha256) else "pending")


def _emergency_revoke(port: ProtectedFinalizerPort, token: str) -> None:
    """A store failure cannot inhibit the independently pinned revoker.

    This attempt cannot yield a confirmed receipt without durable journal
    evidence, regardless of the native response.
    """
    check_revoker(port)
    try:
        observed = port.observe(token)
    except Exception:
        observed = "unknown"
    if observed == "revoked":
        return
    try:
        port.revoke(token)
    except Exception:
        pass
    try:
        port.observe(token)
    except Exception:
        pass


def execute_with_finalizer(envelope_raw: bytes, proof_raw: bytes, token: str,
                           public_key_pem: bytes, context: dict, now,
                           release_port: release.ProtectedReleasePort | None,
                           port: ProtectedFinalizerPort | None, mint_id: str) -> dict:
    """Model protected mint handoff with an outer refusal-path finalizer.

    A durable pending record and recoverable token must exist before the
    release call. Only a fresh committed append permits the first invocation.
    """
    token_sha256 = digest_token(token)
    check_revoker(port)
    try:
        check_vault(port)
        check_port(port)
        context_sha256 = digest_context(context)
        mint_record(port, mint_id, token_sha256, context_sha256)
        escrowed = port.recover_token(mint_id)
        require(type(escrowed) is str and escrowed == token, "token-escrow")
    except BaseException as error:
        _emergency_revoke(port, token)
        if not isinstance(error, Exception):
            raise
        return {"schema": "fsgg.github-substrate-v2.sandbox-host-finalization/1",
                "mintId": mint_id, "release": "not-invoked",
                "revocation": "pending", "disposition": "pending"}
    release_status = "not-invoked"
    may_attempt_revoke = True
    try:
        try:
            pending_write = port.append_pending(mint_id, token_sha256)
        except Exception:
            pending_write = "unknown"
        if pending_write == "duplicate":
            may_attempt_revoke = False
        if pending_write == "committed" and _read_state(
                port, "read_pending", PENDING_SCHEMA, mint_id, token_sha256):
            try:
                result = release.release_once(envelope_raw, proof_raw, token,
                                              public_key_pem, context, now, release_port)
                if type(result) is dict and result.get("schema") == \
                        "fsgg.github-substrate-v2.sandbox-host-release/1":
                    release_status = ("complete" if result.get("disposition") == "complete"
                                      else "pending")
            except Exception:
                release_status = "refused-or-unknown"
    finally:
        finalizer_status = _finalize(port, mint_id, token,
                                     may_attempt_revoke)
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-finalization/1",
            "mintId": mint_id, "release": release_status,
            "revocation": finalizer_status,
            "disposition": "pending"}


def recover_pending(port: ProtectedFinalizerPort | None, mint_id: str) -> dict:
    """Crash recovery observes pending effects; it never retries them."""
    check_revoker(port)
    check_vault(port)
    require(type(mint_id) is str and release.host.HEX64.fullmatch(mint_id), "mint-id")
    token = port.recover_token(mint_id)
    digest_token(token)
    try:
        check_port(port)
    except Exception:
        _emergency_revoke(port, token)
        return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery/1",
                "mintId": mint_id, "revocation": "pending", "disposition": "pending"}
    try:
        record = port.load_mint(mint_id)
        require(type(record) is dict and type(record.get("tokenSha256")) is str
                and release.host.HEX64.fullmatch(record["tokenSha256"]), "mint-record")
        mint_record(port, mint_id, record["tokenSha256"])
        require(digest_token(token) == record["tokenSha256"], "token-escrow")
    except Exception:
        _emergency_revoke(port, token)
        return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery/1",
                "mintId": mint_id, "revocation": "pending", "disposition": "pending"}
    if not _read_state(port, "read_pending", PENDING_SCHEMA,
                       mint_id, record["tokenSha256"]):
        # A crash before durable pending intent is outside the pending census.
        _emergency_revoke(port, token)
        return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery/1",
                "mintId": mint_id, "revocation": "pending", "disposition": "pending"}
    finalizer_status = _finalize(port, mint_id, token)
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-recovery/1",
            "mintId": mint_id, "revocation": finalizer_status,
            "disposition": "pending"}

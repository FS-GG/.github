#!/usr/bin/env python3
"""Source-only protected-host release and revocation contract for GS2-09.7.

No workflow invokes this module. A real host must provide a durable one-use
claim, a synchronous candidate invocation, and native token revocation.
"""

import base64
import binascii
import datetime as dt
import hashlib
import importlib.util
from pathlib import Path
import subprocess
import tempfile
from typing import Protocol


HOST_SOURCE = Path(__file__).with_name("gs2-09-7-host-run-binding.py")
SPEC = importlib.util.spec_from_file_location("gs2_09_7_host_binding", HOST_SOURCE)
host = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(host)

BINDING_FIELDS = {
    "audience", "workflowRepository", "workflowPath", "environment",
    "workflowSha", "runId", "runAttempt", "candidateSha", "runNonce",
    "sandboxRepositoryId", "sandboxRepositoryNodeId", "projectNodeId",
    "proofSha256", "tokenSha256", "expiresAt",
}


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ProtectedReleasePort(Protocol):
    def claim_once(self, decision_id: str, binding_id: str,
                   token_sha256: str) -> str:
        """Durable host-owned CAS: 'granted', 'duplicate', or 'unknown'."""

    def invoke_candidate_if_admitted_once(self, decision_id: str,
                                          binding_id: str,
                                          token_sha256: str,
                                          token: str,
                                          binding: dict) -> str:
        """Fenced one-time handoff: 'complete', 'refused', or 'unknown'.

        'refused' certifies no token exposure. Unknown may have exposed it.
        Admission revocation and launch must share a protected interlock.
        """

    def revoke(self, token: str) -> str:
        """Native token revocation: 'confirmed' or 'unknown'."""


def public_spki_sha256(public_key_pem: bytes) -> str:
    require(type(public_key_pem) is bytes and 0 < len(public_key_pem) <= 64 * 1024,
            "public-key")
    try:
        result = subprocess.run(["openssl", "pkey", "-pubin", "-outform", "DER"],
                                input=public_key_pem, capture_output=True, check=False)
    except OSError as error:
        raise Refused("signature-tool-unavailable") from error
    require(result.returncode == 0 and bool(result.stdout), "public-key")
    return hashlib.sha256(result.stdout).hexdigest()


def verify_signature(public_key_pem: bytes, payload: bytes, signature: bytes) -> None:
    with tempfile.TemporaryDirectory() as directory:
        key = Path(directory) / "public.pem"
        sig = Path(directory) / "signature.bin"
        key.write_bytes(public_key_pem)
        sig.write_bytes(signature)
        try:
            result = subprocess.run(
                ["openssl", "dgst", "-sha256", "-verify", str(key),
                 "-signature", str(sig), "-sigopt", "rsa_padding_mode:pss",
                 "-sigopt", "rsa_pss_saltlen:digest"],
                input=payload, capture_output=True, check=False)
        except OSError as error:
            raise Refused("signature-tool-unavailable") from error
    require(result.returncode == 0, "signature")


def verify_envelope(envelope_raw: bytes, proof_raw: bytes, token: str,
                    public_key_pem: bytes, pinned_spki_sha256: str,
                    context: dict, now: dt.datetime,
                    include_admission: bool = False) -> dict | tuple[dict, dict]:
    """Authenticate the exact signed host binding against runner-owned facts."""
    require(type(pinned_spki_sha256) is str and host.HEX64.fullmatch(pinned_spki_sha256),
            "trust-anchor-unconfigured")
    require(type(host.PINNED_WORKFLOW_SHA) is str
            and host.HEX40.fullmatch(host.PINNED_WORKFLOW_SHA),
            "workflow-revision-unconfigured")
    require(public_spki_sha256(public_key_pem) == pinned_spki_sha256,
            "trust-anchor")
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None, "clock")
    require(type(context) is dict and set(context) == {
        "workflowRepository", "workflowRef", "workflowRefPath", "workflowSha",
        "protectedSha", "runId", "runAttempt", "candidateSha", "runNonce",
    }, "runner-context")
    require(context["workflowRepository"] == host.HOST_REPOSITORY
            and context["workflowRef"] == "refs/heads/main"
            and context["workflowRefPath"] ==
                f"{host.HOST_REPOSITORY}/{host.WORKFLOW}@refs/heads/main"
            and type(context["workflowSha"]) is str
            and host.HEX40.fullmatch(context["workflowSha"])
            and context["workflowSha"] == host.PINNED_WORKFLOW_SHA
            and context["protectedSha"] == context["workflowSha"]
            and type(context["runId"]) is int and context["runId"] > 0
            and type(context["runAttempt"]) is int and context["runAttempt"] > 0
            and type(context["candidateSha"]) is str
            and host.HEX40.fullmatch(context["candidateSha"]), "runner-context")
    nonce = f'{context["runId"]}-{context["runAttempt"]}-{context["candidateSha"]}'
    require(context["runNonce"] == nonce, "run-nonce")
    try:
        admission_record = host.require_admission(context, pinned_spki_sha256, now)
    except host.Refused as error:
        raise Refused(str(error)) from error
    require(type(token) is str and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token), "token")
    token_digest = hashlib.sha256(token.encode("ascii")).hexdigest()

    envelope = host.strict_json(envelope_raw)
    require(set(envelope) == {"schema", "binding", "signatureBase64"}
            and envelope["schema"] == host.SCHEMA, "envelope")
    binding = envelope["binding"]
    require(type(binding) is dict and set(binding) == BINDING_FIELDS, "binding-fields")
    fixed = {
        "audience": host.AUDIENCE, "workflowRepository": host.HOST_REPOSITORY,
        "workflowPath": host.WORKFLOW, "environment": host.ENVIRONMENT,
        "workflowSha": context["workflowSha"], "runId": context["runId"],
        "runAttempt": context["runAttempt"], "candidateSha": context["candidateSha"],
        "runNonce": nonce, "sandboxRepositoryId": host.SANDBOX_ID,
        "sandboxRepositoryNodeId": host.SANDBOX_NODE, "projectNodeId": host.PROJECT_NODE,
        "proofSha256": hashlib.sha256(proof_raw).hexdigest(),
        "tokenSha256": token_digest,
    }
    require(all(type(binding[name]) is type(value) and binding[name] == value
                for name, value in fixed.items()), "run-or-target")
    proof = host.strict_json(proof_raw)
    require(set(proof) == {"schema", "appId", "appSlug", "actor", "installationId",
                           "repositorySelection", "repository", "permissions", "expiresAt",
                           "tokenSha256", "mintResponseSha256", "viewerResponseSha256"}
            and proof["schema"] == "fsgg.github-substrate-v2.sandbox-mint-grants/1"
            and type(proof["appId"]) is int and proof["appId"] == host.APP_ID
            and proof["appSlug"] == host.APP_SLUG
            and proof["actor"] == host.ACTOR
            and type(proof["installationId"]) is int and proof["installationId"] > 0
            and proof["repositorySelection"] == "selected"
            and proof["repository"] == {"id": host.SANDBOX_ID,
                                         "nodeId": host.SANDBOX_NODE,
                                         "fullName": host.SANDBOX_NAME}, "mint-proof")
    grants = proof["permissions"]
    require(type(grants) is dict
            and all(grants.get(name) == level for name, level in host.REQUIRED_GRANTS.items())
            and all(type(name) is str and level in ("read", "write")
                    and (level != "write" or name in host.REQUIRED_GRANTS)
                    for name, level in grants.items()), "mint-grants")
    require(type(proof.get("tokenSha256")) is str
            and proof["tokenSha256"] == token_digest
            and type(proof.get("expiresAt")) is str
            and binding["expiresAt"] == proof["expiresAt"], "proof-binding")
    expiry_text = binding["expiresAt"]
    require(type(expiry_text) is str and expiry_text.endswith("Z"), "expiry")
    try:
        expiry = dt.datetime.fromisoformat(expiry_text[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused("expiry") from error
    require(now < expiry <= now + dt.timedelta(hours=2), "expiry")
    encoded = envelope["signatureBase64"]
    require(type(encoded) is str and bool(encoded), "signature")
    try:
        signature = base64.b64decode(encoded, validate=True)
    except (ValueError, binascii.Error) as error:
        raise Refused("signature") from error
    require(bool(signature), "signature")
    verify_signature(public_key_pem, host.canonical_payload(binding), signature)
    return (binding, admission_record) if include_admission else binding


def release_once(envelope_raw: bytes, proof_raw: bytes, token: str,
                 public_key_pem: bytes, context: dict, now: dt.datetime,
                 port: ProtectedReleasePort | None) -> dict:
    """Model one protected synchronous handoff and its revocation verdict.

    There is no installed port. A real port must durably claim before exposing
    the token and must never retry an unknown candidate invocation.
    """
    require(port is not None and all(callable(getattr(port, name, None)) for name in
                                 ("claim_once", "invoke_candidate_if_admitted_once",
                                  "revoke")),
            "release-port-unconfigured")
    binding, admission_record = verify_envelope(
        envelope_raw, proof_raw, token, public_key_pem,
        host.PINNED_SPKI_SHA256, context, now, include_admission=True)
    binding_id = hashlib.sha256(host.canonical_payload(binding)).hexdigest()
    decision_id = admission_record["decisionId"]
    invoked = False
    outcome = "claim-unknown"
    revoked = "unknown"
    try:
        try:
            claim = port.claim_once(decision_id, binding_id, binding["tokenSha256"])
        except Exception:
            claim = "unknown"
        if claim == "granted":
            try:
                result = port.invoke_candidate_if_admitted_once(
                    decision_id, binding_id, binding["tokenSha256"], token, binding)
            except Exception:
                result = "unknown"
            if result == "refused":
                outcome = "handoff-refused"
            else:
                invoked = True  # Unknown means token exposure cannot be excluded.
                outcome = "candidate-complete" if result == "complete" else "candidate-unknown"
        elif claim == "duplicate":
            outcome = "duplicate-refused"
    finally:
        # A cancellation may arrive after exposure; still attempt native revoke.
        try:
            revoked = port.revoke(token)
        except Exception:
            revoked = "unknown"
    revocation = "confirmed" if revoked == "confirmed" else "unknown"
    disposition = "complete" if outcome == "candidate-complete" and revocation == "confirmed" else "pending"
    return {"schema": "fsgg.github-substrate-v2.sandbox-host-release/1",
            "bindingId": binding_id, "disposition": disposition,
            "outcome": outcome, "invocationCount": int(invoked),
            "revocation": revocation}

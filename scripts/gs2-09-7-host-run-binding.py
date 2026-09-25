#!/usr/bin/env python3
"""Protected-host counterpart to Coordination #558's run-binding contract.

This is source-only. The live workflow does not call it, and the trust pin is
intentionally unset until a protected signer and exact key are admitted.
"""

import base64
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys


SCHEMA = "fsgg.github-substrate-v2.sandbox-run-binding/1"
AUDIENCE = "FS-GG/FS.GG.Coordination:gs2-09-7"
WORKFLOW = ".github/workflows/github-substrate-v2-sandbox-qualification.yml"
ENVIRONMENT = "github-substrate-v2-sandbox"
HOST_REPOSITORY = "FS-GG/.github"
SANDBOX_ID = 1353050537
SANDBOX_NODE = "R_kgDOUKXpqQ"
SANDBOX_NAME = "FS-GG/FS.GG.GitHub.Substrate.Sandbox"
PROJECT_NODE = "PVT_kwDOEYAWY84BiESo"
PROJECT_TITLE = "fsgg-sandbox-gs2-04-9"
APP_ID = 4166418
APP_SLUG = "fs-gg-cross-repo-dispatch"
ACTOR = {"login": "fs-gg-cross-repo-dispatch[bot]", "databaseId": 297630107}
REQUIRED_GRANTS = {"administration": "write", "contents": "write", "issues": "write",
                   "pull_requests": "write", "organization_projects": "write"}
HEX40 = re.compile(r"[0-9a-f]{40}\Z")
HEX64 = re.compile(r"[0-9a-f]{64}\Z")
MAX_JSON_BYTES = 64 * 1024

# An exact reviewed SPKI SHA-256 must be committed with the corresponding
# Coordination public key before a protected host may issue this envelope.
PINNED_SPKI_SHA256 = ""
# The protected release authority must supply this independently admitted
# commit at invocation time. It cannot be self-pinned in that same commit;
# no installed port supplies it, so the source remains fail closed.
PINNED_WORKFLOW_SHA = ""

ADMISSION_SOURCE = Path(__file__).with_name("gs2-09-7-host-release-admission.py")
ADMISSION_SPEC = importlib.util.spec_from_file_location(
    "gs2_09_7_host_release_admission", ADMISSION_SOURCE)
admission = importlib.util.module_from_spec(ADMISSION_SPEC)
ADMISSION_SPEC.loader.exec_module(admission)
# Only a separately protected host adapter may supply this port. No adapter
# is installed, so a self-asserted runner SHA cannot create admission.
ADMISSION_PORT = None


class Refused(Exception):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def unique_pairs(pairs: list[tuple[str, object]]) -> dict:
    value = {}
    for name, item in pairs:
        require(name not in value, "duplicate-member")
        value[name] = item
    return value


def reject_nonfinite(_value: str) -> object:
    raise Refused("nonfinite-number")


def strict_json(raw: bytes) -> dict:
    require(type(raw) is bytes and 0 < len(raw) <= MAX_JSON_BYTES, "json-size")
    try:
        value = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_pairs,
                           parse_constant=reject_nonfinite)
    except (UnicodeError, ValueError, TypeError, RecursionError) as error:
        raise Refused("malformed-json") from error
    require(type(value) is dict, "object-required")
    return value


def read_regular(path: Path) -> bytes:
    flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
    descriptor = os.open(path, flags)
    with os.fdopen(descriptor, "rb") as stream:
        require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), "nonregular-input")
        raw = stream.read(MAX_JSON_BYTES + 1)
    require(0 < len(raw) <= MAX_JSON_BYTES, "json-size")
    return raw


def canonical_payload(binding: dict) -> bytes:
    return (SCHEMA + "\n").encode("ascii") + json.dumps(
        binding, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
        allow_nan=False).encode("ascii")


def with_key(private_key_pem: bytes, arguments: list[str], input_bytes: bytes) -> bytes:
    require(type(private_key_pem) is bytes and 0 < len(private_key_pem) <= MAX_JSON_BYTES,
            "signer-key")
    require(hasattr(os, "memfd_create"), "signer-fd-unavailable")
    descriptor = os.memfd_create("gs2-09-7-run-binding-key", flags=0)
    try:
        require(os.write(descriptor, private_key_pem) == len(private_key_pem),
                "signer-fd")
        os.lseek(descriptor, 0, os.SEEK_SET)
        command = ["openssl", *(f"/proc/self/fd/{descriptor}" if arg == "@KEY@" else arg
                                for arg in arguments)]
        try:
            result = subprocess.run(command, input=input_bytes, capture_output=True,
                                    check=False, pass_fds=(descriptor,))
        except OSError as error:
            raise Refused("signer-tool-unavailable") from error
        require(result.returncode == 0 and bool(result.stdout), "signer-key")
        return result.stdout
    finally:
        os.close(descriptor)


def signer_spki_sha256(private_key_pem: bytes) -> str:
    der = with_key(private_key_pem,
                   ["pkey", "-in", "@KEY@", "-pubout", "-outform", "DER"], b"")
    return hashlib.sha256(der).hexdigest()


def sign(private_key_pem: bytes, payload: bytes) -> bytes:
    return with_key(private_key_pem,
                    ["dgst", "-sha256", "-sign", "@KEY@",
                     "-sigopt", "rsa_padding_mode:pss",
                     "-sigopt", "rsa_pss_saltlen:digest"], payload)


def require_admission(context: dict, signer_spki_sha256: str) -> None:
    target = {
        "workflowRepository": HOST_REPOSITORY,
        "workflowPath": WORKFLOW,
        "environment": ENVIRONMENT,
        "sandboxRepositoryId": SANDBOX_ID,
        "sandboxRepositoryNodeId": SANDBOX_NODE,
        "projectNodeId": PROJECT_NODE,
    }
    try:
        admission.require_admitted(ADMISSION_PORT, context,
                                   signer_spki_sha256, target)
    except admission.Refused as error:
        raise Refused(str(error)) from error


def build(context: dict, preflight_raw: bytes, proof_raw: bytes, token: str,
          private_key_pem: bytes, pinned_spki_sha256: str, now: dt.datetime) -> bytes:
    """Build a signed envelope from protected runner facts and host readback."""
    require(type(pinned_spki_sha256) is str and HEX64.fullmatch(pinned_spki_sha256),
            "trust-anchor-unconfigured")
    require(type(PINNED_WORKFLOW_SHA) is str and HEX40.fullmatch(PINNED_WORKFLOW_SHA),
            "workflow-revision-unconfigured")
    require(signer_spki_sha256(private_key_pem) == pinned_spki_sha256,
            "signer-key-mismatch")
    require(type(now) is dt.datetime and now.tzinfo is not None
            and now.utcoffset() is not None, "clock")
    require(type(context) is dict and set(context) == {
        "workflowRepository", "workflowRef", "workflowRefPath",
        "workflowSha", "protectedSha",
        "runId", "runAttempt", "candidateSha", "runNonce",
    }, "runner-context")
    require(context["workflowRepository"] == HOST_REPOSITORY
            and context["workflowRef"] == "refs/heads/main"
            and context["workflowRefPath"] ==
                f"{HOST_REPOSITORY}/{WORKFLOW}@refs/heads/main"
            and type(context["workflowSha"]) is str and HEX40.fullmatch(context["workflowSha"])
            and context["workflowSha"] == PINNED_WORKFLOW_SHA
            and context["protectedSha"] == context["workflowSha"]
            and type(context["candidateSha"]) is str and HEX40.fullmatch(context["candidateSha"])
            and type(context["runId"]) is int and context["runId"] > 0
            and type(context["runAttempt"]) is int and context["runAttempt"] > 0,
            "runner-context")
    nonce = f'{context["runId"]}-{context["runAttempt"]}-{context["candidateSha"]}'
    require(context["runNonce"] == nonce, "run-nonce")
    require_admission(context, pinned_spki_sha256)
    require(type(token) is str and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token), "token")
    token_digest = hashlib.sha256(token.encode("ascii")).hexdigest()

    preflight = strict_json(preflight_raw)
    require(set(preflight) == {"schema", "candidateSha", "runNonce", "viewer",
                               "repository", "project"}
            and preflight["schema"] == "fsgg.github-substrate-v2.sandbox-preflight/1"
            and preflight["candidateSha"] == context["candidateSha"]
            and preflight["runNonce"] == nonce, "preflight-binding")
    viewer = preflight["viewer"]
    repository = preflight["repository"]
    project = preflight["project"]
    require(type(viewer) is dict and viewer.get("login") == ACTOR["login"]
            and type(viewer.get("databaseId")) is int
            and viewer["databaseId"] == ACTOR["databaseId"]
            and type(viewer.get("id")) is str and bool(viewer["id"]), "preflight-actor")
    require(type(repository) is dict and repository == {
        "node_id": SANDBOX_NODE, "full_name": SANDBOX_NAME, "private": True,
        "description": "fsgg-sandbox-gs2-04-9 disposable qualification target; never production",
    }, "preflight-repository")
    require(type(project) is dict and project == {
        "id": PROJECT_NODE, "title": PROJECT_TITLE, "closed": False, "public": False,
    }, "preflight-project")

    proof = strict_json(proof_raw)
    require(set(proof) == {"schema", "appId", "appSlug", "actor", "installationId",
                           "repositorySelection", "repository", "permissions", "expiresAt",
                           "tokenSha256", "mintResponseSha256", "viewerResponseSha256"}
            and proof["schema"] == "fsgg.github-substrate-v2.sandbox-mint-grants/1"
            and type(proof["appId"]) is int and proof["appId"] == APP_ID
            and proof["appSlug"] == APP_SLUG and proof["actor"] == ACTOR
            and type(proof["installationId"]) is int and proof["installationId"] > 0
            and proof["repositorySelection"] == "selected"
            and proof["repository"] == {"id": SANDBOX_ID, "nodeId": SANDBOX_NODE,
                                         "fullName": SANDBOX_NAME}
            and proof["tokenSha256"] == token_digest, "mint-proof")
    grants = proof["permissions"]
    require(type(grants) is dict and all(grants.get(name) == level
                                         for name, level in REQUIRED_GRANTS.items())
            and all(type(name) is str and level in ("read", "write")
                    and (level != "write" or name in REQUIRED_GRANTS)
                    for name, level in grants.items()), "mint-grants")
    require(all(type(proof[name]) is str and HEX64.fullmatch(proof[name])
                for name in ("mintResponseSha256", "viewerResponseSha256")),
            "mint-digests")
    expiry_text = proof["expiresAt"]
    require(type(expiry_text) is str and expiry_text.endswith("Z"), "expiry")
    try:
        expiry = dt.datetime.fromisoformat(expiry_text[:-1] + "+00:00")
    except (ValueError, OverflowError) as error:
        raise Refused("expiry") from error
    require(now < expiry <= now + dt.timedelta(hours=2), "expiry")

    binding = {
        "audience": AUDIENCE, "workflowRepository": HOST_REPOSITORY,
        "workflowPath": WORKFLOW, "environment": ENVIRONMENT,
        "workflowSha": context["workflowSha"], "runId": context["runId"],
        "runAttempt": context["runAttempt"], "candidateSha": context["candidateSha"],
        "runNonce": nonce, "sandboxRepositoryId": SANDBOX_ID,
        "sandboxRepositoryNodeId": SANDBOX_NODE, "projectNodeId": PROJECT_NODE,
        "proofSha256": hashlib.sha256(proof_raw).hexdigest(),
        "tokenSha256": token_digest, "expiresAt": expiry_text,
    }
    signature = sign(private_key_pem, canonical_payload(binding))
    return (json.dumps({"schema": SCHEMA, "binding": binding,
                        "signatureBase64": base64.b64encode(signature).decode("ascii")},
                       sort_keys=True, separators=(",", ":")) + "\n").encode("ascii")


def main() -> int:
    try:
        require(type(PINNED_SPKI_SHA256) is str
                and HEX64.fullmatch(PINNED_SPKI_SHA256),
                "trust-anchor-unconfigured")
        require(type(PINNED_WORKFLOW_SHA) is str
                and HEX40.fullmatch(PINNED_WORKFLOW_SHA),
                "workflow-revision-unconfigured")
        try:
            admission.check_port(ADMISSION_PORT)
        except admission.Refused as error:
            raise Refused(str(error)) from error
        directory = Path(os.environ["FSGG_SANDBOX_EVIDENCE_DIR"])
        require(directory.is_dir(), "evidence-directory")
        descriptor = int(os.environ["FSGG_SANDBOX_RUN_BINDING_PRIVATE_KEY_FD"])
        require(descriptor > 2 and stat.S_ISREG(os.fstat(descriptor).st_mode), "signer-fd")
        private_key = os.read(descriptor, MAX_JSON_BYTES + 1)
        context = {
            "workflowRepository": os.environ["GITHUB_REPOSITORY"],
            "workflowRef": os.environ["GITHUB_REF"],
            "workflowRefPath": os.environ["GITHUB_WORKFLOW_REF"],
            "workflowSha": os.environ["GITHUB_SHA"],
            "protectedSha": os.environ["FSGG_PROTECTED_SHA"],
            "runId": int(os.environ["GITHUB_RUN_ID"]),
            "runAttempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
            "candidateSha": os.environ["FSGG_CANDIDATE_SHA"],
            "runNonce": os.environ["FSGG_SANDBOX_RUN_NONCE"],
        }
        envelope = build(context, read_regular(directory / "preflight.json"),
                         read_regular(directory / "mint-grants.json"),
                         os.environ["FSGG_SANDBOX_TOKEN"], private_key,
                         PINNED_SPKI_SHA256, dt.datetime.now(dt.timezone.utc))
        output = directory / "run-binding.json"
        require(not output.exists(), "binding-exists")
        descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(envelope)
        print("GS2-RUN-BINDING: signed protected envelope retained")
        return 0
    except (Refused, OSError, KeyError, ValueError, TypeError, OverflowError) as error:
        reason = str(error) if isinstance(error, Refused) else "unavailable-or-malformed"
        print(f"GS2-RUN-BINDING: refused {reason}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

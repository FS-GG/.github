#!/usr/bin/env python3
"""Seal the existing ordinary-v2 custody for the fixed Net environment.

This bridge performs no destination mutation. Secret plaintext is read from the
process environment, removed immediately, and passed only through memory. The
only file it creates is the destination-key-encrypted handoff packet.
"""

from __future__ import annotations

import base64
import ctypes
import ctypes.util
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


API = "https://api.github.com"
SOURCE_REPOSITORY = "FS-GG/.github"
SOURCE_REPOSITORY_ID = "1269292704"
SOURCE_WORKFLOW = ".github/workflows/c3-net-ordinary-enrollment-bridge.yml"
SOURCE_APP_ID = 5064713
SOURCE_APP_NAME = "FS-GG Ordinary V2 Settlement"
SOURCE_INSTALLATION_ID = 164553252
SOURCE_AUTHORITY_REPOSITORY = "FS-GG/FS.GG.Coordination.Authority"
SOURCE_AUTHORITY_REPOSITORY_ID = 1351660651
SOURCE_AUTHORIZER_KEY_ID = "ordinary-v2-production-6121c3f2ab38acf3"
SOURCE_AUTHORIZER_SPKI_SHA256 = "6121c3f2ab38acf38a37f782eb7775da78598b9cfabc0054917d3d59fe50e3ee"
PACKET_SIGNING_ALGORITHM = "RSA-PSS-SHA256"
PACKET_SIGNING_DOMAIN = b"fsgg.github.c3-net-ordinary-enrollment-ciphertexts/1\0"

DESTINATION_REPOSITORY = "FS-GG/FS.GG.Net"
DESTINATION_REPOSITORY_ID = 1305845505
DESTINATION_ENVIRONMENT = "ordinary-v2"
DESTINATION_ENVIRONMENT_ID = 22920188172
DESTINATION_ENVIRONMENT_NODE_ID = "EN_kwDOTdWfAc8AAAAFViZRDA"
DESTINATION_PROTECTION_RULE_ID = 66955538
DESTINATION_PROTECTION_RULE_NODE_ID = "GA_kwDOTdWfAc4D_akS"
DESTINATION_BRANCH_POLICY_ID = 61287584
DESTINATION_BRANCH_POLICY_NODE_ID = "MDE2OkdhdGVCcmFuY2hQb2xpY3k2MTI4NzU4NA=="
DESTINATION_PUBLIC_KEY_ID = "3380204578043523366"
DESTINATION_PUBLIC_KEY_BASE64 = "YMrUCeTLunrm18yIRI5l0i6Zu4uL2gIwI8W0SsomFys="
DESTINATION_PUBLIC_KEY_SHA256 = "2611312487aded24b6bd796b9f593f7466f810ae94916b0f889c57f492f68c4f"

SECRET_NAMES = (
    "V2_ORDINARY_APP_ID",
    "V2_ORDINARY_APP_PRIVATE_KEY",
    "V2_ORDINARY_AUTHORIZER_PRIVATE_KEY",
)
SHA = re.compile(r"[0-9a-f]{40}")
KEY_ID = re.compile(r"[0-9]{1,64}")


class Refusal(RuntimeError):
    pass


def encoded(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def request_json(path: str, token: str | None = None, body: dict | None = None) -> dict:
    data = None if body is None else json.dumps(body, separators=(",", ":")).encode()
    headers = {
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "fsgg-c3-net-ordinary-enrollment-bridge",
    }
    if token is not None:
        headers["Authorization"] = "Bearer " + token
    if data is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(
        API + "/" + path.lstrip("/"), data=data, headers=headers,
        method="POST" if data is not None else "GET",
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            result = json.load(response)
    except (urllib.error.HTTPError, urllib.error.URLError, TimeoutError):
        raise Refusal("required GitHub identity readback failed") from None
    if not isinstance(result, dict):
        raise Refusal("required GitHub identity readback was not an object")
    return result


def openssl_sign(private_key: bytes, message: bytes) -> bytes:
    read_fd, write_fd = os.pipe()
    process = None
    try:
        process = subprocess.Popen(
            ["openssl", "dgst", "-sha256", "-sign", f"/dev/fd/{read_fd}"],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            pass_fds=(read_fd,),
        )
        os.close(read_fd)
        read_fd = -1
        try:
            remaining = memoryview(private_key)
            while remaining:
                remaining = remaining[os.write(write_fd, remaining):]
        finally:
            os.close(write_fd)
            write_fd = -1
        signature, _ = process.communicate(message, timeout=20)
    except (OSError, subprocess.SubprocessError):
        if process is not None and process.poll() is None:
            process.kill()
            process.wait()
        raise Refusal("source App private key could not sign the identity probe") from None
    finally:
        if read_fd >= 0:
            os.close(read_fd)
        if write_fd >= 0:
            os.close(write_fd)
    if process.returncode != 0 or not signature:
        raise Refusal("source App private key could not sign the identity probe")
    return signature


def authorizer_identity(private_key: bytes) -> tuple[str, bytes]:
    try:
        result = subprocess.run(
            ["openssl", "pkey", "-pubout", "-outform", "DER"],
            input=private_key,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            check=True,
            timeout=20,
        )
    except (OSError, subprocess.SubprocessError):
        raise Refusal("source authorizer private key is invalid") from None
    return hashlib.sha256(result.stdout).hexdigest(), result.stdout


def openssl_pss_sign(private_key: bytes, message: bytes) -> bytes:
    read_fd, write_fd = os.pipe()
    process = None
    try:
        process = subprocess.Popen(
            [
                "openssl", "dgst", "-sha256", "-sign", f"/dev/fd/{read_fd}",
                "-sigopt", "rsa_padding_mode:pss", "-sigopt", "rsa_pss_saltlen:digest",
            ],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            pass_fds=(read_fd,),
        )
        os.close(read_fd)
        read_fd = -1
        try:
            remaining = memoryview(private_key)
            while remaining:
                remaining = remaining[os.write(write_fd, remaining):]
        finally:
            os.close(write_fd)
            write_fd = -1
        signature, _ = process.communicate(message, timeout=20)
    except (OSError, subprocess.SubprocessError):
        if process is not None and process.poll() is None:
            process.kill()
            process.wait()
        raise Refusal("source authorizer could not sign the ciphertext packet") from None
    finally:
        if read_fd >= 0:
            os.close(read_fd)
        if write_fd >= 0:
            os.close(write_fd)
    if process.returncode != 0 or not signature:
        raise Refusal("source authorizer could not sign the ciphertext packet")
    return signature


def packet_signature_message(packet: dict) -> bytes:
    attestation = packet.get("attestation")
    if not isinstance(attestation, dict):
        raise Refusal("ciphertext packet has no authorizer attestation")
    unsigned = dict(packet)
    unsigned_attestation = dict(attestation)
    unsigned_attestation.pop("signature", None)
    unsigned["attestation"] = unsigned_attestation
    return PACKET_SIGNING_DOMAIN + json.dumps(
        unsigned, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
    ).encode("ascii")


def verify_packet_attestation(packet: dict, verifier) -> None:
    attestation = packet.get("attestation")
    if not isinstance(attestation, dict) or set(attestation) != {
        "algorithm", "keyId", "publicKeySpki", "publicKeySpkiSha256", "signature"
    }:
        raise Refusal("ciphertext packet authorizer attestation has the wrong shape")
    if (
        attestation["algorithm"] != PACKET_SIGNING_ALGORITHM
        or attestation["keyId"] != SOURCE_AUTHORIZER_KEY_ID
        or attestation["publicKeySpkiSha256"] != SOURCE_AUTHORIZER_SPKI_SHA256
    ):
        raise Refusal("ciphertext packet authorizer identity differs")
    try:
        public_key = base64.b64decode(attestation["publicKeySpki"], validate=True)
        signature = base64.b64decode(attestation["signature"], validate=True)
    except (TypeError, ValueError, base64.binascii.Error):
        raise Refusal("ciphertext packet authorizer encoding is invalid") from None
    if hashlib.sha256(public_key).hexdigest() != SOURCE_AUTHORIZER_SPKI_SHA256:
        raise Refusal("ciphertext packet authorizer public key differs from the pin")
    if verifier(public_key, packet_signature_message(packet), signature) is not True:
        raise Refusal("ciphertext packet authorizer signature is invalid")


def app_jwt(app_id: int, private_key: bytes, signer=openssl_sign, now: int | None = None) -> str:
    issued = int(time.time()) if now is None else now
    header = encoded(b'{"alg":"RS256","typ":"JWT"}')
    claims = json.dumps(
        {"iat": issued - 60, "exp": issued + 540, "iss": str(app_id)},
        separators=(",", ":"),
    ).encode()
    unsigned = f"{header}.{encoded(claims)}".encode()
    return f"{unsigned.decode()}.{encoded(signer(private_key, unsigned))}"


def libsodium_seal(public_key: bytes, plaintext: bytes) -> bytes:
    library_name = ctypes.util.find_library("sodium")
    if not library_name:
        raise Refusal("LibSodium is unavailable")
    sodium = ctypes.CDLL(library_name)
    if sodium.sodium_init() < 0:
        raise Refusal("LibSodium initialization failed")
    sodium.crypto_box_sealbytes.restype = ctypes.c_size_t
    overhead = sodium.crypto_box_sealbytes()
    output = ctypes.create_string_buffer(len(plaintext) + overhead)
    message = ctypes.create_string_buffer(plaintext, len(plaintext))
    key = ctypes.create_string_buffer(public_key, len(public_key))
    sodium.crypto_box_seal.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_ulonglong, ctypes.c_void_p]
    sodium.crypto_box_seal.restype = ctypes.c_int
    if sodium.crypto_box_seal(output, message, len(plaintext), key) != 0:
        raise Refusal("LibSodium sealed-box encryption failed")
    return output.raw


def destination_public_key(environ: dict[str, str]) -> tuple[str, bytes]:
    forbidden = {
        "FSGG_DESTINATION_KEY_ID",
        "FSGG_DESTINATION_PUBLIC_KEY",
        "FSGG_DESTINATION_PUBLIC_KEY_SHA256",
    }
    if forbidden.intersection(environ):
        raise Refusal("caller-selected destination public keys are forbidden")
    try:
        public_key = base64.b64decode(DESTINATION_PUBLIC_KEY_BASE64, validate=True)
    except (ValueError, base64.binascii.Error):
        raise Refusal("pinned destination public key is invalid") from None
    if (
        not KEY_ID.fullmatch(DESTINATION_PUBLIC_KEY_ID)
        or len(public_key) != 32
        or base64.b64encode(public_key).decode() != DESTINATION_PUBLIC_KEY_BASE64
        or hashlib.sha256(public_key).hexdigest() != DESTINATION_PUBLIC_KEY_SHA256
    ):
        raise Refusal("pinned destination public key identity is inconsistent")
    return DESTINATION_PUBLIC_KEY_ID, public_key


def verify_runtime(environ: dict[str, str]) -> None:
    expected_ref = f"{SOURCE_REPOSITORY}/{SOURCE_WORKFLOW}@refs/heads/main"
    sha = environ.get("GITHUB_SHA", "")
    if (
        environ.get("GITHUB_REPOSITORY") != SOURCE_REPOSITORY
        or environ.get("GITHUB_REPOSITORY_ID") != SOURCE_REPOSITORY_ID
        or environ.get("GITHUB_EVENT_NAME") != "workflow_dispatch"
        or environ.get("GITHUB_REF") != "refs/heads/main"
        or environ.get("GITHUB_WORKFLOW_REF") != expected_ref
        or not SHA.fullmatch(sha)
    ):
        raise Refusal("bridge is restricted to its protected-main source workflow")
    if not environ.get("GITHUB_RUN_ID", "").isdecimal() or not environ.get("GITHUB_RUN_ATTEMPT", "").isdecimal():
        raise Refusal("workflow run identity is invalid")


def verify_destination(api=request_json) -> None:
    repository = api(f"repositories/{DESTINATION_REPOSITORY_ID}")
    if {
        "id": repository.get("id"),
        "full_name": repository.get("full_name"),
        "default_branch": repository.get("default_branch"),
        "archived": repository.get("archived"),
        "disabled": repository.get("disabled"),
    } != {
        "id": DESTINATION_REPOSITORY_ID,
        "full_name": DESTINATION_REPOSITORY,
        "default_branch": "main",
        "archived": False,
        "disabled": False,
    }:
        raise Refusal("destination repository identity differs from enrollment")

    environment = api(f"repos/{DESTINATION_REPOSITORY}/environments/{DESTINATION_ENVIRONMENT}")
    observed_environment = {
        "id": environment.get("id"),
        "node_id": environment.get("node_id"),
        "name": environment.get("name"),
        "can_admins_bypass": environment.get("can_admins_bypass"),
        "protection_rules": environment.get("protection_rules"),
        "deployment_branch_policy": environment.get("deployment_branch_policy"),
    }
    expected_environment = {
        "id": DESTINATION_ENVIRONMENT_ID,
        "node_id": DESTINATION_ENVIRONMENT_NODE_ID,
        "name": DESTINATION_ENVIRONMENT,
        "can_admins_bypass": True,
        "protection_rules": [{
            "id": DESTINATION_PROTECTION_RULE_ID,
            "node_id": DESTINATION_PROTECTION_RULE_NODE_ID,
            "type": "branch_policy",
        }],
        "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
    }
    if observed_environment != expected_environment:
        raise Refusal("destination environment policy differs from enrollment")

    policies = api(
        f"repos/{DESTINATION_REPOSITORY}/environments/{DESTINATION_ENVIRONMENT}/deployment-branch-policies"
    )
    if policies != {
        "total_count": 1,
        "branch_policies": [{
            "id": DESTINATION_BRANCH_POLICY_ID,
            "node_id": DESTINATION_BRANCH_POLICY_NODE_ID,
            "name": "main",
            "type": "branch",
        }],
    }:
        raise Refusal("destination environment is not bound only to main")


def verify_current_source(environ: dict[str, str], api=request_json) -> None:
    repository = api(f"repositories/{SOURCE_REPOSITORY_ID}")
    if {
        "id": repository.get("id"),
        "full_name": repository.get("full_name"),
        "default_branch": repository.get("default_branch"),
    } != {
        "id": int(SOURCE_REPOSITORY_ID),
        "full_name": SOURCE_REPOSITORY,
        "default_branch": "main",
    }:
        raise Refusal("source repository identity differs from the protected workflow")
    main = api(f"repos/{SOURCE_REPOSITORY}/git/ref/heads/main")
    if main.get("ref") != "refs/heads/main" or main.get("object", {}).get("sha") != environ["GITHUB_SHA"]:
        raise Refusal("workflow source is no longer current protected main")


def verify_source_app(app_id: int, private_key: bytes, api=request_json, signer=openssl_sign) -> None:
    if app_id != SOURCE_APP_ID:
        raise Refusal("source App ID differs from ordinary-v2 custody")
    jwt = app_jwt(app_id, private_key, signer=signer)
    app = api("app", jwt)
    expected_permissions = {"contents": "write", "metadata": "read"}
    if (
        app.get("id") != SOURCE_APP_ID
        or app.get("name") != SOURCE_APP_NAME
        or app.get("owner", {}).get("login") != "FS-GG"
        or app.get("permissions") != expected_permissions
        or app.get("events") != []
    ):
        raise Refusal("source App identity or permissions differ from ordinary-v2 custody")
    installation = api(f"repos/{SOURCE_AUTHORITY_REPOSITORY}/installation", jwt)
    if (
        installation.get("id") != SOURCE_INSTALLATION_ID
        or installation.get("app_id") != SOURCE_APP_ID
        or installation.get("account", {}).get("login") != "FS-GG"
        or installation.get("repository_selection") != "selected"
        or installation.get("permissions") != expected_permissions
        or installation.get("events") != []
        or installation.get("suspended_at") is not None
    ):
        raise Refusal("source App installation differs from ordinary-v2 custody")
    token = api(
        f"app/installations/{SOURCE_INSTALLATION_ID}/access_tokens",
        jwt,
        {"repositories": ["FS.GG.Coordination.Authority"], "permissions": {"metadata": "read"}},
    ).get("token")
    if not isinstance(token, str) or not token:
        raise Refusal("source App installation token was unavailable")
    repositories = api("installation/repositories?per_page=100", token)
    visible = [(item.get("id"), item.get("full_name")) for item in repositories.get("repositories", [])]
    if repositories.get("total_count") != 1 or visible != [(
        SOURCE_AUTHORITY_REPOSITORY_ID, SOURCE_AUTHORITY_REPOSITORY
    )]:
        raise Refusal("source App repository selection differs from ordinary-v2 custody")


def create_packet(
    environ: dict[str, str], *, api=request_json, signer=openssl_sign,
    authorizer_reader=authorizer_identity, authorizer_signer=openssl_pss_sign,
    seal=libsodium_seal,
) -> dict:
    verify_runtime(environ)
    key_id, destination_key = destination_public_key(environ)
    values = {name: environ.pop(name, "").encode() for name in SECRET_NAMES}
    if any(not value for value in values.values()):
        raise Refusal("ordinary-v2 source custody is incomplete")
    try:
        app_id = int(values["V2_ORDINARY_APP_ID"].decode("ascii"))
    except (UnicodeDecodeError, ValueError):
        raise Refusal("source App ID is invalid") from None

    verify_current_source(environ, api)
    verify_destination(api)
    verify_source_app(app_id, values["V2_ORDINARY_APP_PRIVATE_KEY"], api, signer)
    authorizer_digest, authorizer_public_key = authorizer_reader(
        values["V2_ORDINARY_AUTHORIZER_PRIVATE_KEY"]
    )
    if authorizer_digest != SOURCE_AUTHORIZER_SPKI_SHA256:
        raise Refusal("source authorizer identity differs from ordinary-v2 custody")

    ciphertexts = []
    for name in SECRET_NAMES:
        encrypted = seal(destination_key, values[name])
        if len(encrypted) != len(values[name]) + 48:
            raise Refusal("sealed-box ciphertext has an invalid shape")
        ciphertexts.append({
            "name": name,
            "key_id": key_id,
            "encrypted_value": base64.b64encode(encrypted).decode("ascii"),
        })
    packet = {
        "schema": "fsgg.github.c3-net-ordinary-enrollment-ciphertexts/1",
        "source": {
            "repository": SOURCE_REPOSITORY,
            "repositoryId": int(SOURCE_REPOSITORY_ID),
            "workflow": SOURCE_WORKFLOW,
            "sha": environ["GITHUB_SHA"],
            "runId": int(environ["GITHUB_RUN_ID"]),
            "runAttempt": int(environ["GITHUB_RUN_ATTEMPT"]),
            "appId": SOURCE_APP_ID,
            "installationId": SOURCE_INSTALLATION_ID,
            "authorizerKeyId": SOURCE_AUTHORIZER_KEY_ID,
            "authorizerSpkiSha256": SOURCE_AUTHORIZER_SPKI_SHA256,
        },
        "destination": {
            "repository": DESTINATION_REPOSITORY,
            "repositoryId": DESTINATION_REPOSITORY_ID,
            "environment": DESTINATION_ENVIRONMENT,
            "environmentId": DESTINATION_ENVIRONMENT_ID,
            "publicKeyId": key_id,
            "publicKeySha256": hashlib.sha256(destination_key).hexdigest(),
        },
        "secrets": ciphertexts,
        "attestation": {
            "algorithm": PACKET_SIGNING_ALGORITHM,
            "keyId": SOURCE_AUTHORIZER_KEY_ID,
            "publicKeySpki": base64.b64encode(authorizer_public_key).decode("ascii"),
            "publicKeySpkiSha256": SOURCE_AUTHORIZER_SPKI_SHA256,
        },
    }
    packet["attestation"]["signature"] = base64.b64encode(authorizer_signer(
        values["V2_ORDINARY_AUTHORIZER_PRIVATE_KEY"], packet_signature_message(packet)
    )).decode("ascii")
    return packet


def main() -> None:
    if len(sys.argv) != 2:
        raise Refusal("expected one ciphertext output path")
    output = Path(sys.argv[1])
    if output.exists() or output.is_symlink() or output.parent.resolve() != output.parent:
        raise Refusal("ciphertext output path is not a fresh resolved path")
    packet = create_packet(os.environ)
    descriptor = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
        json.dump(packet, stream, sort_keys=True, separators=(",", ":"))
        stream.write("\n")
    print("sealed three ordinary-v2 values for the fixed Net environment")


if __name__ == "__main__":
    try:
        main()
    except (Refusal, KeyError) as error:
        print(f"C3 Net enrollment bridge refused: {error}", file=sys.stderr)
        sys.exit(1)

#!/usr/bin/env python3
"""Verify, and only with --submit install, a fixed Governance ciphertext packet.

The Governance administrator token is read from inherited file descriptor 3. This
helper never prints the token or ciphertext and never handles secret plaintext.
"""

import argparse
import base64
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.github.com"
SOURCE_REPOSITORY = "FS-GG/.github"
SOURCE_REPOSITORY_ID = 1269292704
SOURCE_WORKFLOW = ".github/workflows/c3-governance-ordinary-enrollment-bridge.yml"
SOURCE_APP_ID = 5064713
SOURCE_INSTALLATION_ID = 164553252
SOURCE_AUTHORIZER_KEY_ID = "ordinary-v2-production-6121c3f2ab38acf3"
SOURCE_AUTHORIZER_SPKI_SHA256 = "6121c3f2ab38acf38a37f782eb7775da78598b9cfabc0054917d3d59fe50e3ee"
SIGNING_ALGORITHM = "RSA-PSS-SHA256"
SIGNING_DOMAIN = b"fsgg.github.c3-governance-ordinary-enrollment-ciphertexts/1\0"
REPOSITORY = "FS-GG/FS.GG.Governance"
REPOSITORY_ID = 1273065119
ENVIRONMENT = "ordinary-v2"
ENVIRONMENT_ID = 22921081283
ENVIRONMENT_NODE_ID = "EN_kwDOS-Fun88AAAAFVjPxww"
PROTECTION_RULE_ID = 66956743
PROTECTION_RULE_NODE_ID = "GA_kwDOS-Fun84D_a3H"
BRANCH_POLICY_ID = 61288925
BRANCH_POLICY_NODE_ID = "MDE2OkdhdGVCcmFuY2hQb2xpY3k2MTI4ODkyNQ=="
PUBLIC_KEY_ID = "3380204578043523366"
PUBLIC_KEY_SHA256 = "e3bdc7b76290328659caed70272a2ba4480922c61e491ae49e08eaae595f5640"
SHA = re.compile(r"[0-9a-f]{40}")
EXPECTED_NAMES = {
    "V2_ORDINARY_APP_ID",
    "V2_ORDINARY_APP_PRIVATE_KEY",
    "V2_ORDINARY_AUTHORIZER_PRIVATE_KEY",
}


class Refusal(RuntimeError):
    pass


def api(path, token=None, method="GET", body=None):
    data = None if body is None else json.dumps(body, separators=(",", ":")).encode()
    request = urllib.request.Request(
        API + "/" + path.lstrip("/"), data=data, method=method,
        headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "fsgg-c3-governance-enrollment-submitter",
            **({"Authorization": "Bearer " + token} if token is not None else {}),
            **({"Content-Type": "application/json"} if data is not None else {}),
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            raw = response.read()
            return {} if not raw else json.loads(raw)
    except urllib.error.HTTPError as error:
        raise Refusal(f"GitHub refused {method} {path} with HTTP {error.code}") from None
    except (urllib.error.URLError, TimeoutError):
        raise Refusal(f"GitHub readback failed for {method} {path}") from None


def signature_message(packet):
    attestation = packet.get("attestation")
    if not isinstance(attestation, dict):
        raise Refusal("ciphertext packet has no authorizer attestation")
    unsigned = dict(packet)
    unsigned_attestation = dict(attestation)
    unsigned_attestation.pop("signature", None)
    unsigned["attestation"] = unsigned_attestation
    return SIGNING_DOMAIN + json.dumps(
        unsigned, sort_keys=True, separators=(",", ":"), ensure_ascii=True,
    ).encode("ascii")


def verify_signature(packet):
    attestation = packet.get("attestation")
    if not isinstance(attestation, dict) or set(attestation) != {
        "algorithm", "keyId", "publicKeySpki", "publicKeySpkiSha256", "signature"
    }:
        raise Refusal("ciphertext packet authorizer attestation has the wrong shape")
    if (
        attestation["algorithm"] != SIGNING_ALGORITHM
        or attestation["keyId"] != SOURCE_AUTHORIZER_KEY_ID
        or attestation["publicKeySpkiSha256"] != SOURCE_AUTHORIZER_SPKI_SHA256
    ):
        raise Refusal("ciphertext packet authorizer identity differs")
    try:
        public_key = base64.b64decode(attestation["publicKeySpki"], validate=True)
        signature = base64.b64decode(attestation["signature"], validate=True)
    except (KeyError, TypeError, ValueError):
        raise Refusal("ciphertext packet authorizer encoding is invalid") from None
    if hashlib.sha256(public_key).hexdigest() != SOURCE_AUTHORIZER_SPKI_SHA256:
        raise Refusal("ciphertext packet authorizer public key differs from the pin")
    with tempfile.TemporaryDirectory(prefix="c3-governance-packet-verify-") as directory:
        public_path = Path(directory) / "authorizer.der"
        signature_path = Path(directory) / "signature.bin"
        public_path.write_bytes(public_key)
        signature_path.write_bytes(signature)
        try:
            result = subprocess.run(
                [
                    "openssl", "dgst", "-sha256", "-verify", str(public_path),
                    "-keyform", "DER", "-signature", str(signature_path),
                    "-sigopt", "rsa_padding_mode:pss", "-sigopt", "rsa_pss_saltlen:digest",
                ],
                input=signature_message(packet), stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL, timeout=20,
            )
        except (OSError, subprocess.SubprocessError):
            raise Refusal("ciphertext packet signature verification was unavailable") from None
    if result.returncode != 0:
        raise Refusal("ciphertext packet authorizer signature is invalid")


def validate_packet(packet, token, request=api, signature_verifier=verify_signature):
    if not isinstance(packet, dict) or set(packet) != {
        "schema", "source", "destination", "secrets", "attestation"
    } or packet.get("schema") != "fsgg.github.c3-governance-ordinary-enrollment-ciphertexts/1":
        raise Refusal("unexpected ciphertext packet schema")
    destination = packet.get("destination", {})
    if set(destination) != {
        "repository", "repositoryId", "environment", "environmentId", "publicKeyId", "publicKeySha256"
    } or (destination.get("repository"), destination.get("repositoryId"),
            destination.get("environment"), destination.get("environmentId")) != (
            REPOSITORY, REPOSITORY_ID, ENVIRONMENT, ENVIRONMENT_ID):
        raise Refusal("ciphertext packet has the wrong fixed destination")
    secrets = packet.get("secrets")
    if (
        not isinstance(secrets, list)
        or len(secrets) != 3
        or any(not isinstance(item, dict) or set(item) != {"name", "key_id", "encrypted_value"}
               for item in secrets)
        or {item.get("name") for item in secrets} != EXPECTED_NAMES
    ):
        raise Refusal("ciphertext packet does not contain the exact secret population")

    source = packet.get("source")
    if not isinstance(source, dict) or set(source) != {
        "repository", "repositoryId", "workflow", "sha", "runId", "runAttempt",
        "appId", "installationId", "authorizerKeyId", "authorizerSpkiSha256",
    }:
        raise Refusal("ciphertext packet source has the wrong shape")
    if (
        source["repository"] != SOURCE_REPOSITORY
        or source["repositoryId"] != SOURCE_REPOSITORY_ID
        or source["workflow"] != SOURCE_WORKFLOW
        or not isinstance(source["sha"], str)
        or not SHA.fullmatch(source["sha"])
        or not isinstance(source["runId"], int)
        or source["runId"] < 1
        or not isinstance(source["runAttempt"], int)
        or source["runAttempt"] < 1
        or source["appId"] != SOURCE_APP_ID
        or source["installationId"] != SOURCE_INSTALLATION_ID
        or source["authorizerKeyId"] != SOURCE_AUTHORIZER_KEY_ID
        or source["authorizerSpkiSha256"] != SOURCE_AUTHORIZER_SPKI_SHA256
    ):
        raise Refusal("ciphertext packet source identity differs")
    signature_verifier(packet)

    current_main = request(f"repos/{SOURCE_REPOSITORY}/git/ref/heads/main")
    if current_main.get("ref") != "refs/heads/main" or current_main.get("object", {}).get("sha") != source["sha"]:
        raise Refusal("ciphertext packet source is no longer current protected main")
    run = request(f"repos/{SOURCE_REPOSITORY}/actions/runs/{source['runId']}")
    if (
        run.get("id") != source["runId"]
        or run.get("run_attempt") != source["runAttempt"]
        or run.get("event") != "workflow_dispatch"
        or run.get("head_branch") != "main"
        or run.get("head_sha") != source["sha"]
        or run.get("path") != SOURCE_WORKFLOW
        or run.get("status") != "completed"
        or run.get("conclusion") != "success"
        or run.get("repository", {}).get("id") != SOURCE_REPOSITORY_ID
        or run.get("repository", {}).get("full_name") != SOURCE_REPOSITORY
    ):
        raise Refusal("ciphertext packet does not identify a current successful protected-main run attempt")

    environment = request(f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}", token)
    observed_environment = {
        "id": environment.get("id"),
        "node_id": environment.get("node_id"),
        "name": environment.get("name"),
        "can_admins_bypass": environment.get("can_admins_bypass"),
        "protection_rules": environment.get("protection_rules"),
        "deployment_branch_policy": environment.get("deployment_branch_policy"),
    }
    if observed_environment != {
        "id": ENVIRONMENT_ID,
        "node_id": ENVIRONMENT_NODE_ID,
        "name": ENVIRONMENT,
        "can_admins_bypass": True,
        "protection_rules": [{
            "id": PROTECTION_RULE_ID,
            "node_id": PROTECTION_RULE_NODE_ID,
            "type": "branch_policy",
        }],
        "deployment_branch_policy": {"protected_branches": False, "custom_branch_policies": True},
    }:
        raise Refusal("current Governance environment policy differs")
    policies = request(f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}/deployment-branch-policies", token)
    if policies != {
        "total_count": 1,
        "branch_policies": [{
            "id": BRANCH_POLICY_ID,
            "node_id": BRANCH_POLICY_NODE_ID,
            "name": "main",
            "type": "branch",
        }],
    }:
        raise Refusal("current Governance environment is not main-only")
    public = request(f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}/secrets/public-key", token)
    try:
        public_bytes = base64.b64decode(public["key"], validate=True)
    except (KeyError, ValueError):
        raise Refusal("current Governance environment public key is invalid") from None
    if (
        public.get("key_id") != PUBLIC_KEY_ID
        or destination.get("publicKeyId") != PUBLIC_KEY_ID
        or hashlib.sha256(public_bytes).hexdigest() != PUBLIC_KEY_SHA256
        or destination.get("publicKeySha256") != PUBLIC_KEY_SHA256
    ):
        raise Refusal("ciphertext packet is not bound to the current Governance environment key")
    for item in secrets:
        if item.get("key_id") != public["key_id"]:
            raise Refusal("ciphertext uses a different destination key ID")
        try:
            ciphertext = base64.b64decode(item["encrypted_value"], validate=True)
        except (KeyError, ValueError):
            raise Refusal("ciphertext payload is invalid") from None
        if len(ciphertext) <= 48:
            raise Refusal("ciphertext payload is too short")

    inventory = request(f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}/secrets?per_page=100", token)
    if inventory.get("total_count") != 0 or inventory.get("secrets") != []:
        raise Refusal("Governance ordinary-v2 secret inventory is not empty; replay or partial enrollment refused")

    return secrets


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("packet")
    parser.add_argument("--submit", action="store_true")
    args = parser.parse_args()
    token = os.read(3, 65536).decode("ascii").strip()
    if not token:
        raise Refusal("Governance administrator token file descriptor 3 is empty")
    with open(args.packet, "r", encoding="utf-8") as stream:
        packet = json.load(stream)
    secrets = validate_packet(packet, token)
    if not args.submit:
        print("verified signed current-run packet, exact Governance policy/key, and empty inventory; no PUT performed")
        return
    for item in secrets:
        api(
            f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}/secrets/{item['name']}",
            token, "PUT", {"encrypted_value": item["encrypted_value"], "key_id": item["key_id"]},
        )
    final_inventory = api(f"repositories/{REPOSITORY_ID}/environments/{ENVIRONMENT}/secrets?per_page=100", token)
    if final_inventory.get("total_count") != 3 or {
        item.get("name") for item in final_inventory.get("secrets", [])
    } != EXPECTED_NAMES:
        raise Refusal("post-submit Governance secret inventory readback differs")
    print("submitted and read back the exact three Governance ordinary-v2 secret names")


if __name__ == "__main__":
    try:
        main()
    except (Refusal, OSError, UnicodeError, json.JSONDecodeError) as error:
        print(f"Governance enrollment submission refused: {error}", file=sys.stderr)
        sys.exit(1)

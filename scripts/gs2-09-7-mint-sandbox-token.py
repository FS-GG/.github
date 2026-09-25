#!/usr/bin/env python3
"""Mint a sandbox-only App token and retain its provider-returned grant proof.

This runs from the protected .github revision. Never run it from a checked-out
Coordination candidate: that candidate may only receive the scoped token and
the sanitized proof after this script has validated both.
"""

import base64
import datetime
import hashlib
import json
import os
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path


API = "https://api.github.com"
APP_SLUG = "fs-gg-cross-repo-dispatch"
APP_ACTOR = "fs-gg-cross-repo-dispatch[bot]"
APP_ACTOR_ID = 297630107
OWNER = "FS-GG"
REPOSITORY = "FS.GG.GitHub.Substrate.Sandbox"
REPOSITORY_ID = 1353050537
REPOSITORY_NODE_ID = "R_kgDOUKXpqQ"
PERMISSIONS = {
    "administration": "write",
    "contents": "write",
    "issues": "write",
    "pull_requests": "write",
    "organization_projects": "write",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def base64url(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")


def app_jwt(app_id: int, private_key: str) -> str:
    now = int(time.time())
    header = base64url(b'{"alg":"RS256","typ":"JWT"}')
    payload = base64url(json.dumps({"iat": now - 60, "exp": now + 540, "iss": str(app_id)}, separators=(",", ":")).encode())
    signing_input = f"{header}.{payload}"
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8") as key_file:
        os.chmod(key_file.name, 0o600)
        key_file.write(private_key)
        key_file.flush()
        signature = subprocess.run(
            ["openssl", "dgst", "-sha256", "-sign", key_file.name],
            input=signing_input.encode(), capture_output=True, check=True,
        ).stdout
    return f"{signing_input}.{base64url(signature)}"


def request_json(method: str, path: str, bearer: str, body: dict | None = None) -> tuple[dict, bytes]:
    encoded = None if body is None else json.dumps(body, separators=(",", ":")).encode()
    request = urllib.request.Request(
        API + path,
        data=encoded,
        method=method,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {bearer}",
            "X-GitHub-Api-Version": "2022-11-28",
            **({"Content-Type": "application/json"} if encoded is not None else {}),
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read(1024 * 1024 + 1)
    except urllib.error.HTTPError as error:
        raise ValueError(f"GitHub {method} {path} returned HTTP {error.code}") from None
    require(len(raw) <= 1024 * 1024, "GitHub response exceeded one MiB")
    value = json.loads(raw)
    require(isinstance(value, dict), "GitHub returned a non-object JSON response")
    return value, raw


def revoke_token(token: str) -> None:
    request = urllib.request.Request(
        API + "/installation/token", method="DELETE",
        headers={"Accept": "application/vnd.github+json",
                 "Authorization": f"Bearer {token}",
                 "X-GitHub-Api-Version": "2022-11-28"},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            require(response.status == 204, "GitHub did not confirm token revocation")
    except urllib.error.HTTPError as error:
        raise ValueError(f"GitHub token revocation returned HTTP {error.code}") from None


def validate_mint_response(response: dict) -> dict:
    token = response.get("token")
    require(isinstance(token, str) and len(token) > 20 and token.isascii()
            and not any(character.isspace() for character in token),
            "GitHub did not return an installation token")
    require(response.get("repository_selection") == "selected",
            "minted token is not limited to selected repositories")
    repositories = response.get("repositories")
    require(isinstance(repositories, list) and len(repositories) == 1,
            "minted token does not select exactly one repository")
    repository = repositories[0]
    require(isinstance(repository, dict) and repository.get("id") == REPOSITORY_ID
            and repository.get("node_id") == REPOSITORY_NODE_ID
            and repository.get("full_name") == f"{OWNER}/{REPOSITORY}",
            "minted token repository differs from the registered sandbox")
    grants = response.get("permissions")
    require(isinstance(grants, dict), "GitHub omitted minted token permissions")
    for permission, level in PERMISSIONS.items():
        require(grants.get(permission) == level,
                f"minted token lacks the required {permission}:{level} grant")
    require(all(level != "write" or name in PERMISSIONS for name, level in grants.items()),
            "minted token contains an unrequested write grant")
    expires_at = response.get("expires_at")
    require(isinstance(expires_at, str) and expires_at.endswith("Z"),
            "GitHub omitted token expiry")
    expiry = datetime.datetime.fromisoformat(expires_at.replace("Z", "+00:00"))
    require(expiry > datetime.datetime.now(datetime.timezone.utc),
            "GitHub returned an expired installation token")
    return {
        "permissions": grants,
        "repositorySelection": response["repository_selection"],
        "repository": {"id": repository["id"], "nodeId": repository["node_id"],
                       "fullName": repository["full_name"]},
        "expiresAt": expires_at,
        "tokenSha256": hashlib.sha256(token.encode()).hexdigest(),
    }


def main() -> None:
    app_id = int(os.environ["FSGG_DISPATCH_APP_ID"])
    private_key = os.environ["FSGG_DISPATCH_APP_PRIVATE_KEY"]
    evidence_dir = Path(os.environ["FSGG_SANDBOX_EVIDENCE_DIR"])
    output = Path(os.environ["GITHUB_OUTPUT"])
    require(app_id > 0 and "PRIVATE KEY" in private_key, "App identity or private key is unavailable")
    jwt = app_jwt(app_id, private_key)
    app, _ = request_json("GET", "/app", jwt)
    require(app.get("id") == app_id and app.get("slug") == APP_SLUG,
            "App JWT does not identify the registered dispatch App")
    installation, _ = request_json("GET", f"/repos/{OWNER}/{REPOSITORY}/installation", jwt)
    installation_id = installation.get("id")
    require(isinstance(installation_id, int) and installation_id > 0
            and installation.get("app_id") == app_id
            and installation.get("app_slug") == APP_SLUG
            and installation.get("account", {}).get("login") == OWNER,
            "sandbox installation does not match the registered App and owner")

    token = None
    complete = False
    try:
        response, raw = request_json(
            "POST", f"/app/installations/{installation_id}/access_tokens", jwt,
            {"repository_ids": [REPOSITORY_ID], "permissions": PERMISSIONS},
        )
        token = response.get("token")
        if isinstance(token, str):
            print(f"::add-mask::{token}", flush=True)
        proof = validate_mint_response(response)
        viewer_response, viewer_raw = request_json(
            "POST", "/graphql", token,
            {"query": "{viewer{login databaseId}}"},
        )
        viewer = viewer_response.get("data", {}).get("viewer", {})
        require(not viewer_response.get("errors") and viewer.get("login") == APP_ACTOR
                and viewer.get("databaseId") == APP_ACTOR_ID,
                "minted token does not identify the registered App actor")
        proof.update({
            "schema": "fsgg.github-substrate-v2.sandbox-mint-grants/1",
            "appId": app_id,
            "appSlug": APP_SLUG,
            "actor": {"login": viewer["login"], "databaseId": viewer["databaseId"]},
            "installationId": installation_id,
            "mintResponseSha256": hashlib.sha256(raw).hexdigest(),
            "viewerResponseSha256": hashlib.sha256(viewer_raw).hexdigest(),
        })
        evidence_dir.mkdir(parents=True, exist_ok=True)
        (evidence_dir / "mint-grants.json").write_text(
            json.dumps(proof, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
        with output.open("a", encoding="utf-8") as stream:
            stream.write(f"token={token}\n")
        complete = True
    finally:
        if isinstance(token, str) and not complete:
            revoke_token(token)


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""One-time, read-only GitHub App enrollment readback on protected main."""

import base64
import json
import os
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path


API = "https://api.github.com"


def encoded(value):
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def app_jwt(app_id, private_key):
    now = int(time.time())
    header = encoded(b'{"alg":"RS256","typ":"JWT"}')
    payload = encoded(json.dumps({"iat": now - 60, "exp": now + 540, "iss": str(app_id)}, separators=(",", ":")).encode())
    unsigned = f"{header}.{payload}".encode()
    with tempfile.TemporaryDirectory(prefix="ordinary-v2-app-readback-") as directory:
        key_path = Path(directory) / "key.pem"
        key_path.write_text(private_key, encoding="utf-8")
        key_path.chmod(0o600)
        signature = subprocess.run(
            ["openssl", "dgst", "-sha256", "-sign", str(key_path)],
            input=unsigned,
            capture_output=True,
            check=True,
        ).stdout
    return f"{header}.{payload}.{encoded(signature)}"


def request(path, token, body=None):
    data = None if body is None else json.dumps(body, separators=(",", ":")).encode()
    req = urllib.request.Request(
        API + path,
        data=data,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": "Bearer " + token,
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "fsgg-ordinary-v2-enrollment-readback",
            **({"Content-Type": "application/json"} if data is not None else {}),
        },
        method="POST" if data is not None else "GET",
    )
    try:
        with urllib.request.urlopen(req, timeout=20) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"GitHub {path} returned HTTP {error.code}") from None


def main():
    app_id = int(os.environ["FSGG_APP_ID"])
    expected_app_id = int(os.environ["FSGG_EXPECTED_APP_ID"])
    expected_name = os.environ["FSGG_EXPECTED_NAME"]
    expected_repository = os.environ["FSGG_EXPECTED_REPOSITORY"]
    expected_repository_id = int(os.environ["FSGG_EXPECTED_REPOSITORY_ID"])
    if app_id <= 0 or (expected_app_id and app_id != expected_app_id):
        raise RuntimeError("App ID does not match enrollment")

    jwt = app_jwt(app_id, os.environ["FSGG_APP_PRIVATE_KEY"])
    app = request("/app", jwt)
    expected_permissions = {"contents": "write", "metadata": "read"}
    observed_app = {
        "appIdDigits": [int(digit) for digit in str(app.get("id", "")) if digit.isdigit()],
        "name": app.get("name"),
        "owner": app.get("owner", {}).get("login"),
        "permissions": app.get("permissions"),
        "events": app.get("events"),
    }
    if (
        app.get("id") != app_id
        or app.get("name") != expected_name
        or app.get("owner", {}).get("login") != "FS-GG"
        or app.get("permissions") != expected_permissions
        or app.get("events") != []
    ):
        raise RuntimeError("App identity or permission ceiling differs from enrollment: " + json.dumps(observed_app, sort_keys=True))

    installation = request(f"/repos/{expected_repository}/installation", jwt)
    if (
        installation.get("app_id") != app_id
        or installation.get("account", {}).get("login") != "FS-GG"
        or installation.get("repository_selection") != "selected"
        or installation.get("permissions") != expected_permissions
        or installation.get("events") != []
        or installation.get("suspended_at") is not None
    ):
        raise RuntimeError("App installation differs from enrollment")

    installation_id = installation["id"]
    # Mint a metadata-only token to inspect the installation's complete repository set.
    token_response = request(
        f"/app/installations/{installation_id}/access_tokens",
        jwt,
        {"permissions": {"metadata": "read"}},
    )
    repositories = request("/installation/repositories?per_page=100", token_response["token"])
    visible = [(item["id"], item["full_name"]) for item in repositories["repositories"]]
    if repositories.get("total_count") != 1 or visible != [(expected_repository_id, expected_repository)]:
        raise RuntimeError("App installation repository set differs from enrollment")

    print(json.dumps({
        # GitHub masks the exact App ID in logs because it is also an environment secret.
        "appIdDigits": [int(digit) for digit in str(app_id)],
        "appName": app["name"],
        "owner": "FS-GG",
        "installationId": installation_id,
        "repositorySelection": "selected",
        "repository": expected_repository,
        "repositoryId": expected_repository_id,
        "permissions": expected_permissions,
        "events": [],
    }, sort_keys=True))


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"ordinary-v2 App readback refused: {error}", file=sys.stderr)
        sys.exit(1)

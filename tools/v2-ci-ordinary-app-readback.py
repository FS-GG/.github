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
MAIN_RULESETS = (24802693, 24802698)


class ReadbackUnavailable(RuntimeError):
    def __init__(self, path, status):
        self.status = status
        super().__init__(f"GitHub {path} returned HTTP {status}")


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
        raise ReadbackUnavailable(path, error.code) from None


def main_rules_readback(repository, token):
    """Read visibility with the selected production token; never replace absent actors with []."""
    rulesets = []
    for identifier in MAIN_RULESETS:
        path = f"/repos/{repository}/rulesets/{identifier}"
        try:
            value = request(path, token)
        except ReadbackUnavailable as error:
            rulesets.append({"id": identifier, "available": False, "httpStatus": error.status,
                             "bypassActorsFieldPresent": None, "bypassActors": None})
            continue
        if not isinstance(value, dict) or value.get("id") != identifier:
            raise RuntimeError("main ruleset identity differs")
        present = "bypass_actors" in value
        actors = value.get("bypass_actors")
        if present and (not isinstance(actors, list) or any(
            not isinstance(actor, dict) or type(actor.get("actor_id")) is not int
            or not isinstance(actor.get("actor_type"), str) or not isinstance(actor.get("bypass_mode"), str)
            for actor in actors
        )):
            raise RuntimeError("main ruleset bypass actors are malformed")
        rulesets.append({
            "id": identifier, "available": True, "httpStatus": 200,
            "name": value.get("name"), "target": value.get("target"),
            "enforcement": value.get("enforcement"), "updatedAt": value.get("updated_at"),
            "conditions": value.get("conditions"),
            "ruleTypes": [rule.get("type") for rule in value.get("rules", [])],
            "bypassActorsFieldPresent": present,
            # Numeric digit arrays preserve public App identifiers under GitHub's secret-ID masking.
            "bypassActors": [{"actorIdDigits": [int(digit) for digit in str(actor["actor_id"])],
                              "actorType": actor["actor_type"], "bypassMode": actor["bypass_mode"]}
                             for actor in actors] if present else None,
        })
    try:
        effective = request(f"/repos/{repository}/rules/branches/main", token)
    except ReadbackUnavailable as error:
        observed_effective = {"available": False, "httpStatus": error.status, "rules": None}
    else:
        if not isinstance(effective, list) or any(not isinstance(rule, dict) for rule in effective):
            raise RuntimeError("effective main rules are malformed")
        observed_effective = {"available": True, "httpStatus": 200, "rules": [
            {"type": rule.get("type"), "rulesetId": rule.get("ruleset_id"),
             "sourceType": rule.get("ruleset_source_type"), "source": rule.get("ruleset_source")}
            for rule in effective]}
    return {"rulesets": rulesets, "effectiveMain": observed_effective, "policyAccepted": False,
            "bypassRosterAvailable": all(rule["available"] and rule["bypassActorsFieldPresent"] for rule in rulesets),
            "observationComplete": all(rule["available"] for rule in rulesets) and observed_effective["available"]}


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

    main_readback = None
    if os.environ.get("FSGG_MAIN_RULE_READBACK") == "1":
        if (app_id, installation_id, expected_repository, expected_repository_id) != (
            5064713, 164553252, "FS-GG/FS.GG.Coordination.Authority", 1351660651
        ):
            raise RuntimeError("main rule readback requires the selected production enrollment")
        # Match the installed production caller's narrowed token without changing App permissions.
        selected = request(f"/app/installations/{installation_id}/access_tokens", jwt,
                           {"repository_ids": [expected_repository_id], "permissions": {"contents": "write"}})
        if selected.get("permissions") != expected_permissions:
            raise RuntimeError("selected production token permission ceiling differs")
        narrowed = request("/installation/repositories?per_page=100", selected["token"])
        if narrowed.get("total_count") != 1 or [(item["id"], item["full_name"]) for item in narrowed["repositories"]] != visible:
            raise RuntimeError("selected production token repository scope differs")
        main_readback = main_rules_readback(expected_repository, selected["token"])

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
        **({"mainRuleReadback": main_readback} if main_readback is not None else {}),
    }, sort_keys=True))
    if main_readback is not None and (not main_readback["observationComplete"] or not main_readback["bypassRosterAvailable"]):
        return 2
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (KeyError, ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"ordinary-v2 App readback refused: {error}", file=sys.stderr)
        sys.exit(1)

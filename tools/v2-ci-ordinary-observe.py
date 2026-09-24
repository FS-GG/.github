#!/usr/bin/env python3
"""Collect native, public evidence for the secret-free ordinary-v2 predecessor.

The caller supplies only the GitHub run identity. Every qualification fact is
fetched from GitHub or the checked-out protected source; no PR artifact is read.
"""

from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
POLICY_PATH = ROOT / "policy/v2-ci-ordinary-settlement.json"
SPEC = importlib.util.spec_from_file_location(
    "v2_ci_ordinary_qualification", ROOT / "tools/v2-ci-ordinary-qualification.py"
)
QUALIFICATION = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(QUALIFICATION)


def api(path: str) -> dict | list:
    result = subprocess.run(
        ["gh", "api", "--header", "Accept: application/vnd.github+json", path],
        check=False, capture_output=True, text=True, timeout=30,
    )
    if result.returncode:
        raise QUALIFICATION.Refusal(f"native GitHub evidence unavailable: {path}")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise QUALIFICATION.Refusal(f"malformed native GitHub evidence: {path}") from error


def observe(environ: dict[str, str]) -> dict:
    policy = QUALIFICATION.read_json(str(POLICY_PATH))
    source = environ.get("GITHUB_SHA", "")
    repository = policy["repository"]
    if not QUALIFICATION.SHA.fullmatch(source):
        raise QUALIFICATION.Refusal("invalid triggering SHA")
    if environ.get("GITHUB_REPOSITORY") != repository:
        raise QUALIFICATION.Refusal("wrong triggering repository")
    if environ.get("GITHUB_EVENT_NAME") != "push" or environ.get("GITHUB_REF") != "refs/heads/main":
        raise QUALIFICATION.Refusal("only a protected-main push is admitted")
    if environ.get("EXPECTED_WORKFLOW_SHA") != source:
        raise QUALIFICATION.Refusal("workflow revision differs from triggering source")
    expected_workflow_ref = f"{repository}/{policy['workflow']['path']}@refs/heads/main"
    if environ.get("GITHUB_WORKFLOW_REF") != expected_workflow_ref:
        raise QUALIFICATION.Refusal("workflow path/ref differs from protected source")
    checkout = subprocess.run(["git", "rev-parse", "HEAD"], check=True,
                              capture_output=True, text=True, cwd=ROOT).stdout.strip()
    if checkout != source:
        raise QUALIFICATION.Refusal("checkout differs from triggering source")
    run_id, attempt = environ.get("GITHUB_RUN_ID", ""), environ.get("GITHUB_RUN_ATTEMPT", "")
    if not run_id.isdecimal() or not attempt.isdecimal() or int(run_id) < 1 or int(attempt) < 1:
        raise QUALIFICATION.Refusal("invalid workflow run identity")

    associations = api(f"repos/{repository}/commits/{source}/pulls?per_page=100")
    if not isinstance(associations, list) or len(associations) != 1:
        raise QUALIFICATION.Refusal("expected exactly one native merged PR association")
    pull = associations[0]
    number = pull.get("number")
    if not isinstance(number, int) or isinstance(number, bool) or number < 1:
        raise QUALIFICATION.Refusal("invalid associated PR number")
    current_pull = api(f"repos/{repository}/pulls/{number}")
    for key in ("number", "merged_at", "merge_commit_sha"):
        if pull.get(key) != current_pull.get(key):
            raise QUALIFICATION.Refusal("associated PR changed between native reads")
    if pull.get("head", {}).get("sha") != current_pull.get("head", {}).get("sha"):
        raise QUALIFICATION.Refusal("associated PR head changed between native reads")
    head = current_pull["head"]["sha"]
    if not QUALIFICATION.SHA.fullmatch(head):
        raise QUALIFICATION.Refusal("invalid associated PR head")

    checks_response = api(f"repos/{repository}/commits/{head}/check-runs?per_page=100")
    if not isinstance(checks_response, dict) or checks_response.get("total_count", 101) > 100:
        raise QUALIFICATION.Refusal("check-run population exceeds bounded native read")
    checks = checks_response.get("check_runs")
    if not isinstance(checks, list) or len(checks) != checks_response["total_count"]:
        raise QUALIFICATION.Refusal("incomplete native check-run population")
    required = policy["qualification"]["requiredChecks"]
    selected = []
    for name in required:
        matches = [check for check in checks if check.get("name") == name]
        if len(matches) != 1 or matches[0].get("app", {}).get("id") != policy["qualification"]["requiredCheckAppId"]:
            raise QUALIFICATION.Refusal(f"missing, duplicate or wrong-app check: {name}")
        check = matches[0]
        selected.append({"name": name, "conclusion": check.get("conclusion"),
                         "sourceSha": check.get("head_sha"),
                         "appId": check.get("app", {}).get("id")})

    digest = hashlib.sha256(POLICY_PATH.read_bytes()).hexdigest()
    runtime = {
        "schema": "fsgg.github.v2-ci-runtime/1", "eventName": environ["GITHUB_EVENT_NAME"],
        "repository": repository, "ref": environ["GITHUB_REF"],
        "eventAfter": source, "sourceSha": source,
        "workflowPath": policy["workflow"]["path"], "workflowRevision": source,
        "environment": policy["credentialJob"]["environment"],
        "runner": "github-hosted-ephemeral", "operationClass": policy["operationClass"],
        "phase": "secret-free-predecessor", "credentialAccess": False,
    }
    evidence = {
        "schema": "fsgg.github.v2-ci-qualification-evidence/1", "status": "passed",
        "subject": {
            "sourceSha": source, "qualificationSha": head,
            "workflowPath": policy["workflow"]["path"], "workflowRevision": source,
            "environment": policy["credentialJob"]["environment"],
            "operationClass": policy["operationClass"], "policySha256": digest,
        },
        "checks": selected,
    }
    receipt = QUALIFICATION.qualify(policy, digest, runtime, [current_pull], evidence)
    receipt["runId"] = int(run_id)
    receipt["runAttempt"] = int(attempt)
    receipt["activation"] = bool(policy["credentialJob"]["installed"])
    return receipt


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in ("produce", "verify"):
        print("usage: v2-ci-ordinary-observe.py produce|verify RECEIPT_PATH", file=sys.stderr)
        return 2
    try:
        actual = observe(dict(os.environ))
        target = pathlib.Path(sys.argv[2])
        encoded = json.dumps(actual, sort_keys=True, separators=(",", ":")) + "\n"
        if sys.argv[1] == "produce":
            if target.exists() or target.is_symlink():
                raise QUALIFICATION.Refusal("receipt path already exists")
            target.write_text(encoded, encoding="utf-8")
        elif target.read_bytes() != encoded.encode():
            raise QUALIFICATION.Refusal("downloaded receipt differs from independent native observation")
        return 0
    except (OSError, KeyError, TypeError, ValueError, subprocess.SubprocessError,
            QUALIFICATION.Refusal) as error:
        print(f"ordinary-v2 native observation refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())

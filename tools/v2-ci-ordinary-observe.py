#!/usr/bin/env python3
"""Collect native, public evidence for the secret-free ordinary-v2 predecessor.

The caller supplies only the GitHub run identity. Every qualification fact is
fetched from GitHub or the checked-out protected source; no PR artifact is read.
"""

from __future__ import annotations

import hashlib
import importlib.util
import base64
import json
import os
import pathlib
import re
import subprocess
import sys
from datetime import datetime

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


def current_file(repository: str, path: str, revision: str) -> bytes:
    response = api(f"repos/{repository}/contents/{path}?ref={revision}")
    if not isinstance(response, dict) or response.get("encoding") != "base64":
        raise QUALIFICATION.Refusal(f"current protected file unavailable: {path}")
    try:
        return base64.b64decode("".join(response["content"].split()), validate=True)
    except (KeyError, TypeError, ValueError) as error:
        raise QUALIFICATION.Refusal(f"current protected file malformed: {path}") from error


def current_authority(repository: str, policy: dict) -> None:
    """Fence a queued or rerun job against the present protected source."""
    before = api(f"repos/{repository}/git/ref/heads/main")
    revision = before.get("object", {}).get("sha") if isinstance(before, dict) else None
    if not isinstance(revision, str) or not QUALIFICATION.SHA.fullmatch(revision):
        raise QUALIFICATION.Refusal("current main ref is malformed")
    paths = ["policy/v2-ci-ordinary-settlement.json", policy["workflow"]["path"]]
    if policy["credentialJob"]["installed"]:
        paths.append("policy/v2-ci-ordinary-settlement-anchor.json")
    for path in paths:
        if current_file(repository, path, revision) != (ROOT / path).read_bytes():
            raise QUALIFICATION.Refusal(f"current protected authority changed: {path}")
    after = api(f"repos/{repository}/git/ref/heads/main")
    if not isinstance(after, dict) or after.get("object", {}).get("sha") != revision:
        raise QUALIFICATION.Refusal("current main ref moved during authority read")


def required_checks(repository: str, policy: dict) -> list[str]:
    branch = api(f"repos/{repository}/branches/main")
    if not isinstance(branch, dict) or branch.get("protected") is not True:
        raise QUALIFICATION.Refusal("live main branch protection unavailable")
    protection = branch.get("protection")
    required = protection.get("required_status_checks") if isinstance(protection, dict) else None
    checks = required.get("checks") if isinstance(required, dict) else None
    if not isinstance(checks, list) or not checks:
        raise QUALIFICATION.Refusal("live required-check population unavailable")
    names = []
    for check in checks:
        if not isinstance(check, dict) or check.get("app_id") != policy["qualification"]["requiredCheckAppId"]:
            raise QUALIFICATION.Refusal("live required-check App differs from policy")
        names.append(check.get("context"))
    if (not all(isinstance(name, str) and name for name in names)
            or len(names) != len(set(names))
            or set(names) != set(policy["qualification"]["requiredGateChecks"])):
        raise QUALIFICATION.Refusal("live required-check population differs from policy")
    return names


def equivalent_tree(repository: str, head: str, source: str) -> str:
    trees = []
    for sha in (head, source):
        commit = api(f"repos/{repository}/git/commits/{sha}")
        tree = commit.get("tree", {}).get("sha") if isinstance(commit, dict) else None
        if not isinstance(tree, str) or not QUALIFICATION.SHA.fullmatch(tree):
            raise QUALIFICATION.Refusal("native commit tree is malformed")
        trees.append(tree)
    if trees[0] != trees[1]:
        raise QUALIFICATION.Refusal("merged source tree differs from qualified PR head")
    return trees[0]


def observe(environ: dict[str, str]) -> dict:
    policy = QUALIFICATION.read_json(str(POLICY_PATH))
    source = environ.get("GITHUB_SHA", "")
    repository = policy["repository"]
    if (repository != "FS-GG/.github"
            or policy["workflow"]["path"] != ".github/workflows/v2-ci-ordinary-settlement.yml"
            or not isinstance(policy["credentialJob"]["installed"], bool)):
        raise QUALIFICATION.Refusal("unexpected protected repository, workflow or activation")
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
    current_authority(repository, policy)
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
    for key in ("number", "node_id", "merged_at", "merge_commit_sha"):
        if pull.get(key) != current_pull.get(key):
            raise QUALIFICATION.Refusal("associated PR changed between native reads")
    if pull.get("head", {}).get("sha") != current_pull.get("head", {}).get("sha"):
        raise QUALIFICATION.Refusal("associated PR head changed between native reads")
    if pull.get("base", {}).get("sha") != current_pull.get("base", {}).get("sha"):
        raise QUALIFICATION.Refusal("associated PR base changed between native reads")
    head = current_pull["head"]["sha"]
    if not QUALIFICATION.SHA.fullmatch(head):
        raise QUALIFICATION.Refusal("invalid associated PR head")
    tree = equivalent_tree(repository, head, source)
    required_checks(repository, policy)

    checks_response = api(f"repos/{repository}/commits/{head}/check-runs?per_page=100")
    if not isinstance(checks_response, dict) or checks_response.get("total_count", 101) > 100:
        raise QUALIFICATION.Refusal("check-run population exceeds bounded native read")
    checks = checks_response.get("check_runs")
    if not isinstance(checks, list) or len(checks) != checks_response["total_count"]:
        raise QUALIFICATION.Refusal("incomplete native check-run population")
    expected_producers = policy["qualification"]["checkProducers"]
    expected_names = set(policy["qualification"]["requiredChecks"] + policy["qualification"]["requiredGateChecks"])
    if set(expected_producers) != expected_names:
        raise QUALIFICATION.Refusal("check-producer population differs from required checks")
    run_cache: dict[int, dict] = {}

    def producer(check: dict, name: str) -> dict:
        url = check.get("details_url")
        pattern = rf"https://github\.com/{re.escape(repository)}/actions/runs/([1-9][0-9]*)/job/([1-9][0-9]*)"
        match = re.fullmatch(pattern, url) if isinstance(url, str) else None
        if match is None:
            raise QUALIFICATION.Refusal(f"check has no native Actions job identity: {name}")
        run_id, job_id = map(int, match.groups())
        suite = check.get("check_suite")
        suite_id = suite.get("id") if isinstance(suite, dict) else None
        if check.get("id") != job_id or not isinstance(suite_id, int) or isinstance(suite_id, bool) or suite_id < 1:
            raise QUALIFICATION.Refusal(f"check/job/suite identity differs: {name}")
        if run_id not in run_cache:
            run = api(f"repos/{repository}/actions/runs/{run_id}")
            if not isinstance(run, dict):
                raise QUALIFICATION.Refusal(f"native Actions run unavailable: {name}")
            run_cache[run_id] = run
        run = run_cache[run_id]
        job = api(f"repos/{repository}/actions/jobs/{job_id}")
        expected = expected_producers[name]
        attempt = job.get("run_attempt") if isinstance(job, dict) else None
        current_attempt = run.get("run_attempt")
        run_repository = run.get("repository")
        if (run.get("id") != run_id or run.get("workflow_id") != expected["workflowId"]
                or run.get("path") != expected["path"] or run.get("event") != expected["event"]
                or run.get("head_sha") != head or run.get("check_suite_id") != suite_id
                or not isinstance(run_repository, dict) or run_repository.get("full_name") != repository
                or not isinstance(current_attempt, int) or isinstance(current_attempt, bool) or current_attempt < 1
                or not isinstance(job, dict) or job.get("id") != job_id or job.get("run_id") != run_id
                or job.get("head_sha") != head or job.get("name") != name
                or job.get("check_run_url") != f"https://api.github.com/repos/{repository}/check-runs/{job_id}"
                or job.get("status") != check.get("status") or job.get("conclusion") != check.get("conclusion")
                or not isinstance(attempt, int) or isinstance(attempt, bool)
                or attempt < 1 or attempt > current_attempt):
            raise QUALIFICATION.Refusal(f"check came from an unexpected workflow or attempt: {name}")
        return {"checkRunId": job_id, "workflowRunId": run_id, "runAttempt": attempt,
                "checkSuiteId": suite_id, "workflowId": expected["workflowId"],
                "workflowPath": expected["path"]}

    def select(name: str) -> dict:
        matches = [check for check in checks if check.get("name") == name]
        if not matches or any(
            check.get("app", {}).get("id") != policy["qualification"]["requiredCheckAppId"]
            or check.get("head_sha") != head
            for check in matches
        ):
            raise QUALIFICATION.Refusal(f"missing, stale or wrong-app check: {name}")
        native = {check["id"]: producer(check, name) for check in matches}
        if len(native) != len(matches):
            raise QUALIFICATION.Refusal(f"duplicate native check identity: {name}")
        try:
            def rank(check: dict) -> tuple[datetime, int]:
                started = datetime.fromisoformat(check["started_at"].replace("Z", "+00:00"))
                identifier = check["id"]
                if started.tzinfo is None or not isinstance(identifier, int) or identifier < 1:
                    raise ValueError("invalid check identity")
                return started, identifier
            check = max(matches, key=rank)
        except (KeyError, TypeError, ValueError) as error:
            raise QUALIFICATION.Refusal(f"malformed native check identity: {name}") from error
        if check.get("status") != "completed" or check.get("conclusion") != "success":
            raise QUALIFICATION.Refusal(f"latest native check is not successful: {name}")
        return {"name": name, "conclusion": check["conclusion"],
                "sourceSha": check["head_sha"], "appId": check["app"]["id"],
                **native[check["id"]]}

    selected = [select(name) for name in policy["qualification"]["requiredChecks"]]
    gate_selected = [select(name) for name in policy["qualification"]["requiredGateChecks"]]

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
        "gateChecks": gate_selected,
    }
    receipt = QUALIFICATION.qualify(policy, digest, runtime, [current_pull], evidence)
    receipt["runId"] = int(run_id)
    receipt["runAttempt"] = int(attempt)
    receipt["activation"] = bool(policy["credentialJob"]["installed"])
    receipt["qualifiedTreeSha"] = tree
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

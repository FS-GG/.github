#!/usr/bin/env python3
"""Serialize and enforce admission for managed pull-request lanes."""

from __future__ import annotations

import argparse
import contextlib
import fcntl
import hashlib
import importlib.util
import json
import os
import pathlib
import re
import stat
import subprocess
import sys
import tempfile
import urllib.parse
from typing import Any, Iterator


MARKER_PREFIX = "<!-- fsgg-pr-lane-admission:v1 "
MARKER_RE = re.compile(
    r"^<!-- fsgg-pr-lane-admission:v1 "
    r"campaign=([A-Za-z0-9][A-Za-z0-9._-]{0,127}) "
    r"chain=([A-Za-z0-9][A-Za-z0-9._-]{0,127}) -->$",
    re.MULTILINE,
)
ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")
REPO_RE = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
SHA_RE = re.compile(r"^[0-9a-f]{40}(?:[0-9a-f]{24})?$")


class Refusal(Exception):
    def __init__(self, reason: str, **details: Any) -> None:
        super().__init__(reason)
        self.reason = reason
        self.details = details


class ToolError(Exception):
    pass


class JsonArgumentParser(argparse.ArgumentParser):
    def error(self, message: str) -> None:
        raise ToolError(message)


def marker(campaign_id: str, chain_id: str) -> str:
    return (
        f"{MARKER_PREFIX}campaign={campaign_id} "
        f"chain={chain_id} -->"
    )


def marker_from_body(body: Any) -> tuple[str, str] | None:
    if not isinstance(body, str):
        return None
    matches = list(MARKER_RE.finditer(body))
    if len(matches) != 1:
        return None
    return matches[0].group(1), matches[0].group(2)


def append_marker(body: str, generated_marker: str) -> str:
    if MARKER_PREFIX in body:
        raise ToolError("body file already contains a PR lane admission marker")
    if body.endswith("\n\n"):
        separator = ""
    elif body.endswith("\n"):
        separator = "\n"
    else:
        separator = "\n\n"
    return f"{body}{separator}{generated_marker}\n"


def routine_validator():
    """Reuse the owning gate's parser and policy without invoking its CLI."""
    path = pathlib.Path(__file__).resolve().parents[1] / "scripts/check-claim-generation.py"
    spec = importlib.util.spec_from_file_location("routine_body_validator", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def validate_routine_body(body: str, branch: str, head: str) -> bool:
    if not isinstance(branch, str) or not branch:
        raise ToolError("pull request branch is unreadable")
    if not branch.startswith("routine/"):
        return False
    if not isinstance(body, str):
        raise ToolError("routine pull request body is unreadable")
    gate = routine_validator()
    if not gate.ROUTINE_BRANCH_RE.fullmatch(branch) or not gate.HEAD_SHA_RE.fullmatch(head):
        raise ToolError("routine branch and head must match the routine branch/40-hex contract")
    matches = list(gate.ROUTINE_MARKER_RE.finditer(body))
    if len(matches) != 1 or body.count("fsgg:routine-development") != 1:
        raise ToolError("exactly one complete routine-development marker is required")
    fields = gate.marker_fields(gate.ROUTINE_MARKER_RE, body)[0]
    policy = gate.load_routine_policy(str(pathlib.Path(__file__).resolve().parents[1] / ".fsgg/routine-development.json"), None)
    if fields.get("__duplicates__") or fields.get("head") != head:
        raise ToolError("routine marker has duplicate fields or a stale/missing head")
    operation = fields.get("operation")
    if operation not in policy["allowedOperations"] or operation in policy["protectedOperations"]:
        raise ToolError("routine marker operation is missing, unknown or protected")
    return True


def prepare_body(body: str, branch: str, head: str, campaign: str, chain: str) -> str:
    """Prepare reviewed metadata once; never silently rebind an existing marker."""
    if branch.startswith("routine/"):
        if "fsgg:routine-development" not in body:
            separator = "" if body.endswith("\n\n") else "\n" if body.endswith("\n") else "\n\n"
            body = body + separator + (
                f"<!-- fsgg:routine-development/v1 head={head} operation=source-change -->\n"
            )
        validate_routine_body(body, branch, head)
    expected = marker(campaign, chain)
    if "fsgg-pr-lane-admission" in body:
        if marker_from_body(body) != (campaign, chain) or body.count("fsgg-pr-lane-admission") != 1:
            raise ToolError("PR lane admission marker is malformed, duplicated or conflicts")
        return body
    return append_marker(body, expected)


def machine_markers(body: str) -> dict[str, str]:
    """Retain even unknown FS.GG metadata; never guess how to rewrite it."""
    matches = list(re.finditer(r"<!--\s*(fsgg[^\s>]*)(?:\s.*?)?-->", body, re.DOTALL))
    markers: dict[str, str] = {}
    for match in matches:
        key = match.group(1)
        if key in markers:
            raise ToolError("duplicate machine-owned PR metadata")
        markers[key] = match.group(0)
    if len(re.findall(r"<!--\s*fsgg", body)) != len(matches):
        raise ToolError("incomplete machine-owned PR metadata")
    return markers


def update_body(fresh: str, proposed: str, branch: str, head: str,
                previous_head: str, campaign: str, chain: str) -> str:
    """Only the routine head may change; native metadata owns every other field."""
    if marker_from_body(fresh) != (campaign, chain) or fresh.count("fsgg-pr-lane-admission") != 1:
        raise ToolError("existing PR admission identity is missing, malformed or conflicts")
    original = machine_markers(fresh)
    desired = dict(original)
    if branch.startswith("routine/"):
        # Idempotent retries can observe the already rebound marker.
        try:
            validate_routine_body(fresh, branch, head)
        except ToolError:
            validate_routine_body(fresh, branch, previous_head)
        key = "fsgg:routine-development/v1"
        desired[key] = re.sub(r"(?<!\S)head=[0-9a-f]+(?=\s)", "head=" + head, original[key])
    submitted = machine_markers(proposed)
    for key, value in submitted.items():
        allowed = {original[key], desired[key]} if key in original else set()
        if key == "fsgg:routine-development/v1" and key in desired:
            allowed.add(re.sub(r"(?<!\S)head=[0-9a-f]+(?=\s)", "head=" + previous_head, desired[key]))
        if value not in allowed:
            raise ToolError("proposed body conflicts with native machine-owned metadata")
    prose = proposed
    for value in submitted.values():
        prose = prose.replace(value, "", 1)
    result = prose.rstrip() + "\n\n" + "\n".join(desired.values()) + "\n"
    validate_routine_body(result, branch, head)
    return result


def update_pull(arguments: argparse.Namespace, proposed: str, identity: dict[str, Any]) -> dict[str, Any]:
    endpoint = f"/repos/{arguments.repo}/pulls/{arguments.update_pr}"

    def read() -> dict[str, Any]:
        value = run_gh(arguments.gh, ["--method", "GET", endpoint])
        if not isinstance(value, dict) or not isinstance(value.get("body"), str):
            raise ToolError("existing pull-request response was unreadable")
        head = value.get("head")
        base = value.get("base")
        repo = head.get("repo") if isinstance(head, dict) else None
        if (not isinstance(head, dict) or not isinstance(base, dict) or not isinstance(repo, dict)
                or value.get("number") != arguments.update_pr or value.get("state") != "open"
                or head.get("ref") != arguments.head
                or head.get("sha") != arguments.head_sha
                or str(repo.get("full_name", "")).lower() != arguments.repo.lower()
                or base.get("ref") != arguments.base):
            raise Refusal("update-candidate-mismatch", **identity)
        return value

    with repository_lock(arguments.repo):
        before = read()
        body = update_body(before["body"], proposed, arguments.head, arguments.head_sha,
                           arguments.previous_head_sha, arguments.campaign_id, arguments.dependency_chain_id)
        if current_head_sha(arguments.gh, arguments.repo, arguments.head) != arguments.head_sha:
            raise Refusal("head-moved", **identity)
        # This route cannot atomically compare-and-swap a PR body. Re-read
        # immediately before mutation and read back after; never retry an uncertain write.
        if read() != before:
            raise Refusal("update-conflict", **identity)
        unchanged = before["body"] == body and before.get("title") == arguments.title
        if not arguments.inspect and not unchanged:
            run_gh(arguments.gh, ["--method", "PATCH", endpoint, "--input", "-"],
                   stdin=json.dumps({"body": body, "title": arguments.title}, ensure_ascii=False))
            after = read()
            if after["body"] != body or after.get("title") != arguments.title:
                raise Refusal("update-readback-mismatch", **identity)
        return {"ok": True, "action": "admitted" if arguments.inspect else "unchanged" if unchanged else "updated",
                **identity, "number": arguments.update_pr, "headSha": arguments.head_sha,
                "url": before.get("html_url")}


def run_gh(gh: str, arguments: list[str], stdin: str | None = None) -> Any:
    try:
        completed = subprocess.run(
            [gh, "api", *arguments],
            input=stdin,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
    except OSError as error:
        raise ToolError(f"could not execute gh: {error.strerror or error}") from error
    if completed.returncode != 0:
        # Do not repeat stderr: authentication tooling may include sensitive context.
        raise ToolError(f"gh api failed with exit code {completed.returncode}")
    try:
        return json.loads(completed.stdout)
    except json.JSONDecodeError as error:
        raise ToolError("gh api returned invalid JSON") from error


def open_pulls(gh: str, repo: str) -> list[dict[str, Any]]:
    endpoint = f"/repos/{repo}/pulls?state=open&per_page=100"
    value = run_gh(gh, ["--method", "GET", "--paginate", "--slurp", endpoint])
    if not isinstance(value, list):
        raise ToolError("open pull-request response was not a JSON array")
    pages = value if not value or isinstance(value[0], list) else [value]
    pulls: list[dict[str, Any]] = []
    for page in pages:
        if not isinstance(page, list) or not all(isinstance(item, dict) for item in page):
            raise ToolError("open pull-request page had an unexpected shape")
        pulls.extend(page)
    return pulls


def current_head_sha(gh: str, repo: str, head: str) -> str:
    encoded_head = urllib.parse.quote(head, safe="")
    endpoint = f"/repos/{repo}/git/ref/heads/{encoded_head}"
    value = run_gh(gh, ["--method", "GET", endpoint])
    try:
        sha = value["object"]["sha"]
    except (KeyError, TypeError) as error:
        raise ToolError("head ref response did not contain object.sha") from error
    if not isinstance(sha, str):
        raise ToolError("head ref object.sha was not a string")
    return sha


def pull_summary(pull: dict[str, Any]) -> dict[str, Any]:
    head = pull.get("head")
    head = head if isinstance(head, dict) else {}
    return {
        "number": pull.get("number"),
        "url": pull.get("html_url"),
        "headRef": head.get("ref"),
        "headSha": head.get("sha"),
    }


def enforce_lane(
    pulls: list[dict[str, Any]], campaign_id: str, chain_id: str
) -> int:
    managed_in_repo: list[dict[str, Any]] = []
    same_chain: list[dict[str, Any]] = []
    unresolved: list[dict[str, Any]] = []
    for pull in pulls:
        body = pull.get("body")
        parsed = marker_from_body(body)
        if parsed is None:
            head = pull.get("head")
            branch = head.get("ref") if isinstance(head, dict) else None
            # Reservation is distinct from source eligibility. A body rewrite
            # or a stale routine head must not manufacture a free managed slot.
            routine = (isinstance(branch, str)
                       and routine_validator().ROUTINE_BRANCH_RE.fullmatch(branch) is not None)
            footprint = isinstance(body, str) and "<!-- fsgg-pr-lane-admission" in body
            if not routine and not footprint:
                continue  # Genuine unmanaged feature PR policy is unchanged.
            unresolved.append(pull)
        managed_in_repo.append(pull)
        if parsed == (campaign_id, chain_id):
            same_chain.append(pull)
    population = {
        "openNativePullRequestCount": len(pulls),
        "nativeOpenPullRequests": [pull_summary(pull) for pull in pulls],
        "openManagedPullRequestCount": len(managed_in_repo),
    }
    if same_chain:
        raise Refusal(
            "same-chain-open",
            **population,
            matchingPullRequests=[pull_summary(pull) for pull in same_chain],
        )
    if len(managed_in_repo) >= 2:
        raise Refusal(
            "repository-open-pr-cap",
            **population,
            matchingPullRequests=[pull_summary(pull) for pull in managed_in_repo],
        )
    if unresolved:
        raise Refusal(
            "managed-reservation-identity-unknown",
            **population,
            matchingPullRequests=[pull_summary(pull) for pull in unresolved],
        )
    return len(managed_in_repo)


def lock_root() -> pathlib.Path:
    root = pathlib.Path(tempfile.gettempdir()) / f"fsgg-pr-lane-admission-{os.getuid()}"
    try:
        root.mkdir(mode=0o700)
    except FileExistsError:
        pass
    info = root.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
        raise ToolError("unsafe PR lane admission lock directory")
    if stat.S_IMODE(info.st_mode) & 0o077:
        raise ToolError("PR lane admission lock directory permissions are too broad")
    return root


@contextlib.contextmanager
def repository_lock(repo: str) -> Iterator[None]:
    digest = hashlib.sha256(repo.lower().encode("utf-8")).hexdigest()
    flags = os.O_RDWR | os.O_CREAT
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    try:
        descriptor = os.open(lock_root() / f"{digest}.lock", flags, 0o600)
    except OSError as error:
        raise ToolError(f"could not open PR lane admission lock: {error}") from error
    try:
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX)
        except OSError as error:
            raise ToolError(f"could not acquire PR lane admission lock: {error}") from error
        yield
    finally:
        with contextlib.suppress(OSError):
            fcntl.flock(descriptor, fcntl.LOCK_UN)
        os.close(descriptor)


def parser() -> argparse.ArgumentParser:
    result = JsonArgumentParser(description=__doc__)
    result.add_argument("--repo", required=True, help="GitHub owner/repository")
    result.add_argument("--campaign-id", required=True)
    result.add_argument("--dependency-chain-id", required=True)
    result.add_argument("--head", required=True, help="Head branch in the target repository")
    result.add_argument("--head-sha", required=True, help="Exact expected head commit")
    result.add_argument("--base", required=True)
    result.add_argument("--title", required=True)
    result.add_argument("--body-file", required=True, type=pathlib.Path)
    result.add_argument("--update-pr", type=int, help="Update an existing managed PR, preserving native machine metadata")
    result.add_argument("--previous-head-sha", help="Explicit previous routine marker head for an update")
    result.add_argument("--inspect", action="store_true", help="Check admission without creating a PR")
    result.add_argument("--gh", default="gh", help=argparse.SUPPRESS)
    return result


def validate(arguments: argparse.Namespace) -> None:
    if arguments.update_pr is not None and (arguments.update_pr <= 0 or not arguments.previous_head_sha
            or not SHA_RE.fullmatch(arguments.previous_head_sha)):
        raise ToolError("update requires a positive PR number and an exact previous head SHA")
    if arguments.previous_head_sha and arguments.update_pr is None:
        raise ToolError("previous head SHA requires --update-pr")
    if not REPO_RE.fullmatch(arguments.repo):
        raise ToolError("repo must have owner/repository form")
    for label, value in (
        ("campaign ID", arguments.campaign_id),
        ("dependency-chain ID", arguments.dependency_chain_id),
    ):
        if not ID_RE.fullmatch(value):
            raise ToolError(f"{label} must match {ID_RE.pattern}")
    if not SHA_RE.fullmatch(arguments.head_sha):
        raise ToolError("head SHA must be a lowercase 40- or 64-character hexadecimal commit ID")
    if not arguments.head or "\n" in arguments.head or "\r" in arguments.head:
        raise ToolError("head branch must be non-empty and single-line")
    if not arguments.base or "\n" in arguments.base or "\r" in arguments.base:
        raise ToolError("base branch must be non-empty and single-line")
    if not arguments.title or "\n" in arguments.title or "\r" in arguments.title:
        raise ToolError("title must be non-empty and single-line")


def execute(arguments: argparse.Namespace) -> dict[str, Any]:
    validate(arguments)
    try:
        original_body = arguments.body_file.read_text(encoding="utf-8")
    except (OSError, UnicodeError) as error:
        raise ToolError(f"could not read UTF-8 body file: {error}") from error
    identity = {
        "repo": arguments.repo,
        "campaignId": arguments.campaign_id,
        "dependencyChainId": arguments.dependency_chain_id,
    }

    if arguments.update_pr is not None:
        return update_pull(arguments, original_body, identity)
    managed_body = prepare_body(original_body, arguments.head, arguments.head_sha,
                                arguments.campaign_id, arguments.dependency_chain_id)
    with repository_lock(arguments.repo):
        pulls = open_pulls(arguments.gh, arguments.repo)
        try:
            open_count = enforce_lane(
                pulls, arguments.campaign_id, arguments.dependency_chain_id
            )
        except Refusal as refusal:
            refusal.details = {**identity, **refusal.details}
            raise
        observed_sha = current_head_sha(arguments.gh, arguments.repo, arguments.head)
        if observed_sha != arguments.head_sha:
            raise Refusal(
                "head-moved",
                **identity,
                openNativePullRequestCount=len(pulls),
                nativeOpenPullRequests=[pull_summary(pull) for pull in pulls],
                expectedHeadSha=arguments.head_sha,
                observedHeadSha=observed_sha,
            )
        common = {
            **identity,
            "head": arguments.head,
            "headSha": arguments.head_sha,
            "base": arguments.base,
            "openManagedPullRequestCount": open_count,
            "openNativePullRequestCount": len(pulls),
            "nativeOpenPullRequests": [pull_summary(pull) for pull in pulls],
        }
        if arguments.inspect:
            return {"ok": True, "action": "admitted", **common}

        payload = json.dumps(
            {
                "title": arguments.title,
                "head": arguments.head,
                "base": arguments.base,
                "body": managed_body,
            },
            ensure_ascii=False,
        )
        created = run_gh(
            arguments.gh,
            ["--method", "POST", f"/repos/{arguments.repo}/pulls", "--input", "-"],
            stdin=payload,
        )
        if not isinstance(created, dict) or not isinstance(created.get("number"), int):
            raise ToolError("created pull-request response did not contain a number")
        return {
            "ok": True,
            "action": "created",
            **common,
            "number": created["number"],
            "url": created.get("html_url"),
        }


def main(argv: list[str] | None = None) -> int:
    try:
        arguments = parser().parse_args(argv)
        result = execute(arguments)
    except Refusal as refusal:
        result = {"ok": False, "action": "refused", "reason": refusal.reason, **refusal.details}
        print(json.dumps(result, separators=(",", ":"), sort_keys=True))
        return 3
    except ToolError as error:
        result = {"ok": False, "action": "error", "reason": str(error)}
        print(json.dumps(result, separators=(",", ":"), sort_keys=True))
        return 2
    print(json.dumps(result, separators=(",", ":"), sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

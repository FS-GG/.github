#!/usr/bin/env python3
"""Serialize and enforce admission for managed pull-request lanes."""

from __future__ import annotations

import argparse
import contextlib
import fcntl
import hashlib
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
    return {
        "number": pull.get("number"),
        "url": pull.get("html_url"),
    }


def enforce_lane(
    pulls: list[dict[str, Any]], campaign_id: str, chain_id: str
) -> int:
    managed_in_repo: list[dict[str, Any]] = []
    same_chain: list[dict[str, Any]] = []
    for pull in pulls:
        parsed = marker_from_body(pull.get("body"))
        if parsed is None:
            continue
        managed_in_repo.append(pull)
        if parsed == (campaign_id, chain_id):
            same_chain.append(pull)
    if same_chain:
        raise Refusal(
            "same-chain-open",
            matchingPullRequests=[pull_summary(pull) for pull in same_chain],
        )
    if len(managed_in_repo) >= 2:
        raise Refusal(
            "repository-open-pr-cap",
            openManagedPullRequestCount=len(managed_in_repo),
            matchingPullRequests=[pull_summary(pull) for pull in managed_in_repo],
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
    result.add_argument("--inspect", action="store_true", help="Check admission without creating a PR")
    result.add_argument("--gh", default="gh", help=argparse.SUPPRESS)
    return result


def validate(arguments: argparse.Namespace) -> None:
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
    managed_body = append_marker(
        original_body, marker(arguments.campaign_id, arguments.dependency_chain_id)
    )
    identity = {
        "repo": arguments.repo,
        "campaignId": arguments.campaign_id,
        "dependencyChainId": arguments.dependency_chain_id,
    }

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
                expectedHeadSha=arguments.head_sha,
                observedHeadSha=observed_sha,
            )
        common = {
            **identity,
            "head": arguments.head,
            "headSha": arguments.head_sha,
            "base": arguments.base,
            "openManagedPullRequestCount": open_count,
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

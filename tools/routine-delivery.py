#!/usr/bin/env python3
"""Merge one routine PR and report its native delivery result exactly once."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from dataclasses import asdict, dataclass
from typing import Any, Protocol


SHA_RE = re.compile(r"^[0-9a-f]{40}$")


class AmbiguousWrite(RuntimeError):
    """The merge request may have reached GitHub, so readback must decide."""


class NativeApi(Protocol):
    def get_pr(self, repo: str, pr: int) -> dict[str, Any]: ...

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict[str, Any]: ...


class GhApi:
    @staticmethod
    def _run(args: list[str], *, timeout: int = 30) -> dict[str, Any]:
        try:
            result = subprocess.run(
                ["gh", "api", *args],
                check=False,
                capture_output=True,
                text=True,
                timeout=timeout,
            )
        except (subprocess.TimeoutExpired, OSError) as error:
            raise AmbiguousWrite(str(error)) from error
        if result.returncode != 0:
            detail = result.stderr.strip() or result.stdout.strip() or f"gh api exited {result.returncode}"
            raise RuntimeError(detail)
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError as error:
            raise RuntimeError("GitHub returned a non-JSON response") from error

    def get_pr(self, repo: str, pr: int) -> dict[str, Any]:
        return self._run([f"repos/{repo}/pulls/{pr}"])

    def merge(self, repo: str, pr: int, head: str, method: str) -> dict[str, Any]:
        return self._run(
            [
                "--method",
                "PUT",
                f"repos/{repo}/pulls/{pr}/merge",
                "-f",
                f"sha={head}",
                "-f",
                f"merge_method={method}",
            ]
        )


@dataclass(frozen=True)
class Summary:
    schema: str
    repo: str
    pr: int
    expectedHead: str
    observedHead: str | None
    outcome: str
    codeDelivery: str
    publication: str
    mergeCommit: str | None
    attempts: int
    reason: str | None


def head_of(pr: dict[str, Any]) -> str | None:
    head = pr.get("head")
    return head.get("sha") if isinstance(head, dict) and isinstance(head.get("sha"), str) else None


def merged_commit_of(pr: dict[str, Any]) -> str | None:
    value = pr.get("merge_commit_sha")
    return value if isinstance(value, str) and SHA_RE.fullmatch(value) else None


def is_merged(pr: dict[str, Any]) -> bool:
    return pr.get("merged") is True or pr.get("merged_at") is not None


def eligible(pr: dict[str, Any], expected_head: str) -> tuple[bool, str | None]:
    observed = head_of(pr)
    if observed != expected_head:
        return False, f"changed head: expected {expected_head}, observed {observed or 'unreadable'}"
    if is_merged(pr):
        return True, None
    if pr.get("state") != "open":
        return False, f"pull request is {pr.get('state') or 'unreadable'}, not open"
    if pr.get("draft") is True:
        return False, "pull request is draft"
    if pr.get("mergeable") is not True:
        return False, "pull request is not currently mergeable"
    merge_state = pr.get("mergeable_state")
    if merge_state not in {"clean", "unstable", "has_hooks"}:
        return False, f"pull request merge state is {merge_state or 'unreadable'}"
    return True, None


def summarize(
    api: NativeApi,
    *,
    repo: str,
    pr_number: int,
    expected_head: str,
    merge_method: str,
    publication_required: bool,
    apply: bool,
) -> tuple[int, Summary]:
    publication = "pending" if publication_required else "not-required"
    before = api.get_pr(repo, pr_number)
    allowed, reason = eligible(before, expected_head)
    observed = head_of(before)
    if not allowed:
        return 2, Summary(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "refused", "not-delivered", publication, None, 0, reason,
        )
    if is_merged(before):
        return 0, Summary(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "delivered", "delivered", publication, merged_commit_of(before), 0, None,
        )
    if not apply:
        return 0, Summary(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, observed,
            "ready", "not-delivered", publication, None, 0, None,
        )

    attempts = 0
    while attempts < 2:
        attempts += 1
        try:
            response = api.merge(repo, pr_number, expected_head, merge_method)
        except AmbiguousWrite:
            after = api.get_pr(repo, pr_number)
            allowed, reason = eligible(after, expected_head)
            if is_merged(after) and head_of(after) == expected_head:
                return 0, Summary(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "delivered-after-readback", "delivered", publication,
                    merged_commit_of(after), attempts, None,
                )
            if not allowed:
                return 2, Summary(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "refused", "not-delivered", publication, None, attempts, reason,
                )
            if attempts < 2:
                continue
            return 3, Summary(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "indeterminate", "unknown", publication, None, attempts,
                "two ambiguous merge attempts; native readback still reports an eligible open PR",
            )
        except RuntimeError as error:
            after = api.get_pr(repo, pr_number)
            if is_merged(after) and head_of(after) == expected_head:
                return 0, Summary(
                    "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                    "delivered-after-readback", "delivered", publication,
                    merged_commit_of(after), attempts, None,
                )
            return 2, Summary(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "refused", "not-delivered", publication, None, attempts, str(error),
            )

        after = api.get_pr(repo, pr_number)
        if response.get("merged") is True and is_merged(after) and head_of(after) == expected_head:
            merge_commit = response.get("sha")
            if not isinstance(merge_commit, str) or not SHA_RE.fullmatch(merge_commit):
                merge_commit = merged_commit_of(after)
            return 0, Summary(
                "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
                "delivered", "delivered", publication, merge_commit, attempts, None,
            )
        return 3, Summary(
            "fsgg.routine-delivery/v1", repo, pr_number, expected_head, head_of(after),
            "indeterminate", "unknown", publication, None, attempts,
            "merge response and native PR readback do not both establish delivery",
        )

    raise AssertionError("bounded merge loop escaped")


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--repo", required=True, help="OWNER/REPO")
    result.add_argument("--pr", required=True, type=int)
    result.add_argument("--head", required=True)
    result.add_argument("--merge-method", choices=("merge", "squash", "rebase"), default="squash")
    result.add_argument("--publication", choices=("none", "required"), default="none")
    result.add_argument("--apply", action="store_true")
    return result


def main(argv: list[str]) -> int:
    args = parser().parse_args(argv)
    if not re.fullmatch(r"[^/\s]+/[^/\s]+", args.repo):
        parser().error("--repo must be OWNER/REPO")
    if args.pr <= 0:
        parser().error("--pr must be positive")
    if not SHA_RE.fullmatch(args.head):
        parser().error("--head must be a lowercase 40-hex commit SHA")
    try:
        code, result = summarize(
            GhApi(), repo=args.repo, pr_number=args.pr, expected_head=args.head,
            merge_method=args.merge_method, publication_required=args.publication == "required",
            apply=args.apply,
        )
    except (RuntimeError, AmbiguousWrite) as error:
        code = 3
        result = Summary(
            "fsgg.routine-delivery/v1", args.repo, args.pr, args.head, None,
            "indeterminate", "unknown",
            "pending" if args.publication == "required" else "not-required",
            None, 0, str(error),
        )
    print(json.dumps(asdict(result), separators=(",", ":"), sort_keys=True))
    return code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

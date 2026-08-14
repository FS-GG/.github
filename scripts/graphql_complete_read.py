#!/usr/bin/env python3
"""Temporary Python frontend to the cross-language GraphQL complete-read contract.

GraphQl.fs is the canonical typed implementation. This compatibility frontend exists only for the
audit/archive entry points that cannot yet call equivalent F# CLI operations. M6 removes this file
once typed F# CLI project-visibility, project-id, repository-policy, board-scan, archive-mutation,
and meter operations exist and have completed three stable operating cycles. Until then the
architectural checker permits raw envelope/page fields here and nowhere else in those surfaces.
"""
from __future__ import annotations

import argparse
import json
import subprocess
from dataclasses import dataclass
from enum import Enum
from typing import Any, Callable


class RetryClassification(Enum):
    RETRYABLE = "retryable"
    NOT_RETRYABLE = "not-retryable"


class RateLimitKind(Enum):
    PRIMARY = "primary"
    SECONDARY = "secondary"


@dataclass(frozen=True)
class FailureMetadata:
    retry: RetryClassification
    rate_limit: RateLimitKind | None = None
    reset_at: str | None = None
    retry_after_seconds: int | None = None


class GraphQlReadError(RuntimeError):
    def __init__(self, message: str, metadata: FailureMetadata | None = None):
        super().__init__(message)
        self.metadata = metadata or FailureMetadata(RetryClassification.NOT_RETRYABLE)


@dataclass(frozen=True)
class PageResult:
    items: list[Any]
    pages: int
    spent: int


def execute(query: str, variables: dict[str, Any], decode: Callable[[dict[str, Any]], Any]) -> Any:
    cmd = ["gh", "api", "graphql", "-f", f"query={query}"]
    for name, value in variables.items():
        if value is not None:
            cmd.extend(["-F", f"{name}={value}"])
    try:
        proc = subprocess.run(cmd, input=json.dumps({"query": query, "variables": variables}, sort_keys=True),
                              capture_output=True, text=True, timeout=60, check=False)
    except (OSError, subprocess.SubprocessError) as exc:
        raise GraphQlReadError(f"could not run GraphQL transport: {exc}") from exc
    if proc.returncode:
        raise GraphQlReadError(f"GraphQL transport failed ({proc.returncode}): {proc.stderr.strip()}")
    try:
        envelope = json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise GraphQlReadError(f"GraphQL returned invalid JSON: {exc}") from exc
    if not isinstance(envelope, dict):
        raise GraphQlReadError("GraphQL envelope was not an object")
    errors = envelope.get("errors")
    if errors:
        records = [e for e in errors if isinstance(e, dict)]
        messages = [str(e.get("message", "(no message)")) for e in records]
        lowered = " ".join(messages).lower()
        secondary = "secondary rate limit" in lowered or "abuse" in lowered
        primary = not secondary and ("rate limit" in lowered or "rate_limit" in lowered)
        extension = next((e.get("extensions") for e in records if isinstance(e.get("extensions"), dict)), {})
        reset_at = extension.get("resetAt") if isinstance(extension.get("resetAt"), str) else None
        retry_after = extension.get("retryAfter")
        retry_after = retry_after if isinstance(retry_after, int) and retry_after >= 0 else None
        rate_kind = RateLimitKind.SECONDARY if secondary else RateLimitKind.PRIMARY if primary else None
        metadata = FailureMetadata(RetryClassification.RETRYABLE if rate_kind else RetryClassification.NOT_RETRYABLE,
                                   rate_kind, reset_at, retry_after)
        kind = f"{rate_kind.value} rate-limited/retryable" if rate_kind else "not-retryable"
        raise GraphQlReadError(f"GraphQL refused the query ({kind}): {'; '.join(messages)}", metadata)
    data = envelope.get("data")
    if not isinstance(data, dict):
        raise GraphQlReadError("GraphQL envelope carried neither object data nor errors")
    try:
        return decode(data)
    except (KeyError, TypeError, ValueError) as exc:
        raise GraphQlReadError(f"GraphQL data had an invalid shape: {exc}") from exc


def drain(query: str, variables: dict[str, Any], connection: Callable[[dict[str, Any]], dict[str, Any]],
          decode_node: Callable[[dict[str, Any]], Any], key: Callable[[Any], str],
          *, max_pages: int = 100, max_items: int = 10000, cursor_name: str = "cursor",
          underfull_window: int | None = None) -> PageResult:
    out: list[Any] = []
    cursor: str | None = None
    seen_cursors: set[str] = set()
    seen_keys: set[str] = set()
    expected_total: int | None = None
    spent = 0
    for page_no in range(1, max_pages + 1):
        def decode(data: dict[str, Any]) -> tuple[dict[str, Any], int]:
            conn = connection(data)
            if not isinstance(conn, dict):
                raise TypeError("connection was not an object")
            meter = data.get("rateLimit") or {}
            return conn, int(meter.get("cost", 0))
        conn, cost = execute(query, {**variables, cursor_name: cursor}, decode)
        spent += cost
        nodes, info = conn.get("nodes"), conn.get("pageInfo")
        if isinstance(nodes, list) and info is None and underfull_window and len(nodes) < underfull_window:
            info = {"hasNextPage": False, "endCursor": None}
            conn = {**conn, "totalCount": len(nodes)}
        if not isinstance(nodes, list) or not isinstance(info, dict):
            raise GraphQlReadError("connection omitted nodes or pageInfo")
        total = conn.get("totalCount")
        if not isinstance(total, int) or total < 0:
            raise GraphQlReadError("connection omitted a valid totalCount")
        if expected_total is None:
            expected_total = total
        elif expected_total != total:
            raise GraphQlReadError(f"board item count changed while paging ({expected_total} -> {total})")
        for raw in nodes:
            item = decode_node(raw)
            identity = key(item)
            if not identity or identity in seen_keys:
                raise GraphQlReadError(f"connection repeated or omitted stable identity {identity!r}")
            seen_keys.add(identity); out.append(item)
            if len(out) > max_items:
                raise GraphQlReadError(f"connection exceeded {max_items} items")
        has_next = info.get("hasNextPage")
        if not isinstance(has_next, bool):
            raise GraphQlReadError("pageInfo.hasNextPage was not Boolean")
        if not has_next:
            if expected_total != len(out):
                raise GraphQlReadError(f"connection ended with {len(out)} of {expected_total} items")
            return PageResult(out, page_no, spent)
        next_cursor = info.get("endCursor")
        if not isinstance(next_cursor, str) or not next_cursor or next_cursor in seen_cursors or not nodes:
            raise GraphQlReadError("board reported hasNextPage=true but no endCursor to page with, repeated it, or returned no nodes")
        seen_cursors.add(next_cursor); cursor = next_cursor
    raise GraphQlReadError(f"connection did not terminate within {max_pages} pages")


PROJECTS = """query($owner:String!,$cursor:String){organization(login:$owner){projectsV2(first:100,after:$cursor){totalCount pageInfo{hasNextPage endCursor} nodes{id title public}}} rateLimit{cost remaining}}"""
PROJECT = """query($owner:String!,$number:Int!){organization(login:$owner){projectV2(number:$number){id title}} rateLimit{cost remaining}}"""
REPOSITORY = """query($owner:String!,$name:String!){repository(owner:$owner,name:$name){issueCreationPolicy hasIssuesEnabled}}"""

def main() -> int:
    ap = argparse.ArgumentParser(); sub = ap.add_subparsers(dest="cmd", required=True)
    p = sub.add_parser("project-visibility"); p.add_argument("--owner", required=True); p.add_argument("--title", required=True)
    p = sub.add_parser("project-id"); p.add_argument("--owner", required=True); p.add_argument("--number", required=True, type=int)
    p = sub.add_parser("repository-policy"); p.add_argument("--owner", required=True); p.add_argument("--name", required=True)
    a = ap.parse_args()
    if a.cmd == "project-visibility":
        result = drain(PROJECTS, {"owner": a.owner}, lambda d: d["organization"]["projectsV2"],
                       lambda n: n, lambda n: n["id"], underfull_window=100)
        matches = [n for n in result.items if n.get("title") == a.title]
        if len(matches) > 1: raise GraphQlReadError(f"expected one project named {a.title!r}, found {len(matches)}")
        print("null" if not matches else str(matches[0]["public"]).lower())
    elif a.cmd == "project-id":
        print(execute(PROJECT, {"owner": a.owner, "number": a.number}, lambda d: d["organization"]["projectV2"]["id"]))
    else:
        print(json.dumps(execute(REPOSITORY, {"owner": a.owner, "name": a.name}, lambda d: d["repository"]), sort_keys=True))
    return 0

if __name__ == "__main__":
    try: raise SystemExit(main())
    except GraphQlReadError as exc: print(f"graphql-complete-read: {exc}", file=__import__("sys").stderr); raise SystemExit(2)

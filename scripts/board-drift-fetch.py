#!/usr/bin/env python3
"""Read Project 3 and its linked open PRs and recent comments into one JSON snapshot (Refs #4730).

READ-ONLY, BOUNDED, FAIL-CLOSED. This is the only place the board observer touches the network to
read. It issues:

  * one paginated Projects v2 `items(first: 100)` query (about 1 GraphQL point per page; the board is
    ~100 cards after archiving, capped here at --max-pages pages), including each card's repository
    `isPrivate`;
  * one org-wide search for open PRs (REST, search budget), capped at 1,000 results by GitHub and
    refused when that cap is reached, plus one `repos/{repo}` read per PR repository the board does not
    already tell us the visibility of (a failed read is "unknown", never a fetch failure);
  * one REST comments read per card that could carry a finding (every card except a Done, closed one
    with all its fields), since that card's last Status change.

The snapshot holds private-repository titles and PR bodies. It lives only in a private scratch
directory and is never uploaded or printed; the observer publishes placeholders for anything private
or of unknown visibility (see board-drift-observer.py, PRIVACY).

It writes the snapshot only when every read completed: a GraphQL error, a missing `data`, a page that
says `hasNextPage` after --max-pages, `incomplete_results` from search, an item count different from
`totalCount`, or any failed `gh` call exits non-zero and leaves NO output file. "Could not read" is
never "no drift" (.github#266).

The GraphQL here runs as the workflow's App installation, which has its own rate limit and spends none
of the fleet's shared user budget. It is NOT behind the typed `OperationalGraphQl` boundary: the typed
`archive-scan` does not expose the Track, Work level, per-field timestamps and visibility this observer
needs. `scripts/check-graphql-boundary.py` therefore covers this file under a named, owned exemption
(owner Programme/root, tracking `Refs #4730`). Moving the read to a typed `OperationalGraphQl` verb is the
tracked follow-up; until then the query, pagination and response checks stay in this one module.

Environment: GH_TOKEN must carry organization Projects v2 read, issues read and pull requests read.
`BOARD_DRIFT_GH` overrides the `gh` binary (tests).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

SNAPSHOT_SCHEMA = "fsgg:board-drift-snapshot/v1"
COMMENT_BODY_LIMIT = 2000

QUERY = """
query($owner:String!,$number:Int!,$cursor:String){
  organization(login:$owner){ projectV2(number:$number){
    id title
    items(first:100, after:$cursor){
      totalCount
      pageInfo{ hasNextPage endCursor }
      nodes{
        id updatedAt
        status: fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name updatedAt } }
        track: fieldValueByName(name:"Track"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
        workLevel: fieldValueByName(name:"Work level"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
        content{
          __typename
          ... on Issue{ number title state updatedAt closedAt repository{ nameWithOwner isPrivate } }
          ... on PullRequest{ number title state updatedAt closedAt repository{ nameWithOwner isPrivate } }
          ... on DraftIssue{ title }
        }
      }
    }
  }}
}
"""


class FetchError(Exception):
    """A failed read. The message is built ONLY from fixed text: this runs in a public repository's
    Actions log, and an argv (`repos/<private>/issues/7/comments`) or a response body would publish a
    private repository's name, a card number or API text. Never put either in a FetchError."""


# The only call labels a failure may name.
LABELS = ("graphql", "search", "comments", "repo-visibility")


def gh(label: str, args: list[str]) -> str:
    assert label in LABELS, label
    cmd = [os.environ.get("BOARD_DRIFT_GH", "gh"), *args]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=180, check=False)
    except subprocess.TimeoutExpired as exc:
        raise FetchError(f"{label} call timed out") from None
    except OSError as exc:
        raise FetchError(f"{label} call could not start ({type(exc).__name__})") from None
    if proc.returncode:
        raise FetchError(f"{label} call failed (exit {proc.returncode})")
    return proc.stdout


def ndjson(text: str) -> list[dict]:
    try:
        return [json.loads(line) for line in text.splitlines() if line.strip()]
    except json.JSONDecodeError:
        raise FetchError("comments output is not parseable") from None


def fetch_items(owner: str, number: int, max_pages: int) -> tuple[dict, list[dict]]:
    items: list[dict] = []
    cursor: str | None = None
    project: dict = {}
    total: int | None = None
    for _page in range(max_pages):
        args = ["api", "graphql", "-F", f"owner={owner}", "-F", f"number={number}", "-f", f"query={QUERY}"]
        if cursor:
            args += ["-f", f"cursor={cursor}"]
        try:
            response = json.loads(gh("graphql", args))
        except json.JSONDecodeError:
            raise FetchError("graphql response is not JSON") from None
        if response.get("errors"):
            raise FetchError("graphql response carries errors")
        node = ((response.get("data") or {}).get("organization") or {}).get("projectV2")
        if not node or not isinstance(node.get("items"), dict):
            raise FetchError("GraphQL response has no organization.projectV2.items (token scope or project number?)")
        page = node["items"]
        project = {"owner": owner, "number": number, "id": node.get("id"), "title": node.get("title")}
        total = page.get("totalCount")
        nodes = page.get("nodes")
        info = page.get("pageInfo") or {}
        if not isinstance(nodes, list) or not isinstance(total, int) or "hasNextPage" not in info:
            raise FetchError("GraphQL page is missing nodes, totalCount or pageInfo")
        items.extend(nodes)
        if not info["hasNextPage"]:
            break
        cursor = info.get("endCursor")
        if not cursor:
            raise FetchError("GraphQL page says hasNextPage without an endCursor")
    else:
        raise FetchError(f"project still has more pages after {max_pages} page(s): refusing a partial board")
    if total is None or len(items) != total:
        raise FetchError(f"read {len(items)} item(s) but the project reports totalCount {total}")
    project["totalCount"] = total
    return project, items


def _visibility(value: object) -> bool | None:
    """Only a real boolean is a visibility; anything else (a string, a number, null) is unknown."""
    return value if isinstance(value, bool) else None


def normalize_item(node: dict) -> dict:
    content = node.get("content")
    status = node.get("status") or {}
    item: dict = {
        "itemId": node.get("id"),
        "status": status.get("name"),
        "statusUpdatedAt": status.get("updatedAt"),
        "track": (node.get("track") or {}).get("name"),
        "workLevel": (node.get("workLevel") or {}).get("name"),
        "itemUpdatedAt": node.get("updatedAt"),
        "comments": [],
    }
    if not isinstance(content, dict):
        item["unreadable"] = True
        return item
    kind = content.get("__typename")
    item["type"] = kind
    item["title"] = content.get("title")
    if kind in ("Issue", "PullRequest"):
        item.update(
            repo=(content.get("repository") or {}).get("nameWithOwner"),
            # Missing or null stays None: unknown visibility is treated as private downstream.
            repoPrivate=_visibility((content.get("repository") or {}).get("isPrivate")),
            number=content.get("number"),
            state=content.get("state"),
            closedAt=content.get("closedAt"),
            contentUpdatedAt=content.get("updatedAt"),
        )
        if not item["repo"] or item["number"] is None or not item["state"]:
            item["unreadable"] = True
    elif kind != "DraftIssue":
        item["unreadable"] = True
    return item


def repo_visibility(repo: str) -> bool | None:
    """True when private, False when public, None when it cannot be told (treated as private)."""
    try:
        value = json.loads(gh("repo-visibility", ["api", f"repos/{repo}", "--jq", ".private"]) or "null")
    except (FetchError, json.JSONDecodeError):
        return None
    return value if isinstance(value, bool) else None


def fetch_prs(org: str, known: dict[str, bool | None] | None = None) -> list[dict]:
    known = dict(known or {})
    prs: list[dict] = []
    page, total = 1, None
    while True:
        out = gh("search", ["api", f"search/issues?q=org:{org}+is:pr+is:open&per_page=100&page={page}&sort=updated&order=desc"])
        try:
            doc = json.loads(out)
        except json.JSONDecodeError as exc:
            raise FetchError("search response is not JSON") from None
        if doc.get("incomplete_results") is not False or not isinstance(doc.get("items"), list):
            raise FetchError("PR search returned incomplete results")
        total = doc.get("total_count")
        if not isinstance(total, int) or total > 1000:
            raise FetchError(f"PR search total_count {total!r} is not a trustworthy count (cap is 1000)")
        for raw in doc["items"]:
            repo_url = str(raw.get("repository_url", ""))
            repo = repo_url.split("/repos/", 1)[-1] if "/repos/" in repo_url else ""
            if not repo or raw.get("number") is None:
                raise FetchError("PR search item lacks repository_url or number")
            prs.append({
                "repo": repo, "number": raw["number"], "draft": bool(raw.get("draft")),
                "title": raw.get("title") or "",
                "state": raw.get("state"), "updatedAt": raw.get("updated_at"),
                "url": raw.get("html_url"), "body": raw.get("body") or "",
            })
        if len(prs) >= total or not doc["items"]:
            break
        page += 1
        if page > 10:
            raise FetchError("PR search needs more than 10 pages")
    if len(prs) != total:
        raise FetchError(f"read {len(prs)} PR(s) but search reports {total}")
    # Visibility of each PR's repository: from the board's own cards where known, else one REST read per
    # repository. A failed read is None, which the analysis treats as private (never published).
    for pr in prs:
        if pr["repo"] not in known:
            known[pr["repo"]] = repo_visibility(pr["repo"])
        pr["repoPrivate"] = known[pr["repo"]]
    return prs


def fetch_comments(repo: str, number: int, since: str) -> list[dict]:
    out = gh("comments", ["api", f"repos/{repo}/issues/{number}/comments?since={since}&per_page=100", "--paginate", "--jq", ".[]"])
    return [
        {"id": c.get("id"), "author": (c.get("user") or {}).get("login", ""), "createdAt": c.get("created_at"),
         "url": c.get("html_url"), "body": (c.get("body") or "")[:COMMENT_BODY_LIMIT]}
        for c in ndjson(out)
    ]


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--owner", default="FS-GG")
    ap.add_argument("--project", type=int, default=3)
    ap.add_argument("--out", required=True)
    ap.add_argument("--max-pages", type=int, default=5, help="GraphQL pages of 100 items (default 5)")
    ap.add_argument("--fallback-comment-days", type=int, default=7,
                    help="comment window for an open card with no Status timestamp (default 7)")
    args = ap.parse_args(argv)

    try:
        started = dt.datetime.now(dt.timezone.utc)
        project, nodes = fetch_items(args.owner, args.project, args.max_pages)
        items = [normalize_item(n) for n in nodes]
        board_visibility = {i["repo"]: i.get("repoPrivate") for i in items if i.get("repo")}
        prs = fetch_prs(args.owner, board_visibility)
        fallback = (started - dt.timedelta(days=args.fallback_comment_days)).strftime("%Y-%m-%dT%H:%M:%SZ")
        for item in items:
            if item.get("unreadable") or item.get("type") not in ("Issue", "PullRequest"):
                continue
            # A card that is Done and closed with all its fields can carry no finding, so it needs no
            # comments (rule (e) and every ack route read them). Everything else does.
            finished = item.get("state") in ("CLOSED", "MERGED") and item.get("status") == "Done"
            if finished and item.get("track") and item.get("workLevel"):
                continue
            since = item.get("statusUpdatedAt") or item.get("itemUpdatedAt") or fallback
            item["comments"] = fetch_comments(item["repo"], item["number"], since)
    except FetchError as exc:
        print(f"board-drift-fetch: FAILED (no snapshot written): {exc}", file=sys.stderr)
        return 1
    except Exception as exc:  # noqa: BLE001 - a traceback would print source lines and values
        print(f"board-drift-fetch: FAILED (no snapshot written): unexpected {type(exc).__name__}", file=sys.stderr)
        return 1

    snapshot = {
        "schema": SNAPSHOT_SCHEMA,
        "complete": True,
        "fetchedAt": started.strftime("%Y-%m-%dT%H:%M:%SZ"),
        "project": project,
        "items": items,
        "pullRequests": prs,
    }
    out = Path(args.out)
    # Write beside the target, then rename: a reader never sees a half-written snapshot.
    fd, tmp = tempfile.mkstemp(dir=out.parent, prefix=out.name + ".")
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        json.dump(snapshot, handle, indent=2, sort_keys=True)
        handle.write("\n")
    os.replace(tmp, out)
    print(f"board-drift-fetch: {len(items)} item(s), {len(prs)} open PR(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

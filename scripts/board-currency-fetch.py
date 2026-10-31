#!/usr/bin/env python3
"""Read Project 3 with its native hierarchy, blockers and PRs into one board-currency snapshot (Refs #4730).

READ-ONLY, BOUNDED, FAIL-CLOSED. This is the only place the board-currency rules touch the network. It
issues:

  * one paginated Projects v2 `items(first: 50)` query carrying, per issue card, its state and state
    reason, native parent, up to 100 native sub-issues and up to 20 native blocked-by issues (with their
    totals, so a truncated list is visible as incomplete rather than read as complete);
  * two org-wide PR searches (REST search budget): every open PR, and every PR merged inside
    --merged-hours; each refused at GitHub's 1,000-result cap;
  * one paginated REST comments read for each card a marker rule needs (open Blocked cards, Outcomes
    open or closed since the rule set's effective date, active Deliveries);
  * one batched GraphQL state read for blocker issues named in a marker that are not on the board.

The snapshot holds private-repository titles, PR bodies and comment text. It lives only in a private
scratch directory and is never uploaded or printed. Any failed call, GraphQL error, page past
--max-pages, incomplete search or count mismatch exits non-zero and writes NO snapshot: "could not
read" is never "current" (.github#266). An unresolvable marker blocker is left out of refStates; the
rules then report Unknown for that card rather than a verdict.

Like board-drift-fetch.py this read is outside the typed OperationalGraphQl boundary: the typed verbs
expose neither Work level, native parent/sub-issue/blocked-by links nor state reasons.
`scripts/check-graphql-boundary.py` covers this file under a named, owned exemption (owner
Programme/root, Refs #4730).

Environment: GH_TOKEN must carry organization Projects read, issues read and pull requests read.
`BOARD_CURRENCY_GH` overrides the `gh` binary (tests).
"""
from __future__ import annotations

import argparse
import datetime as dt
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

SNAPSHOT_SCHEMA = "fsgg:board-currency-snapshot/v1"
# Markers count only in Root-author comments, so those bodies are kept WHOLE: a newer marker past a cut
# could otherwise be missed (newest blocked-on marker wins). Other authors' comments feed only R4's
# activity set, which reads createdAt, so their bodies are bounded. `truncated` records any cut; the
# marker rules return Unknown if a Root comment ever carries it.
COMMENT_BODY_LIMIT = 4000

QUERY = """
query($owner:String!,$number:Int!,$cursor:String){
  organization(login:$owner){ projectV2(number:$number){
    id title
    items(first:50, after:$cursor){
      totalCount
      pageInfo{ hasNextPage endCursor }
      nodes{
        id updatedAt
        status: fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name updatedAt } }
        track: fieldValueByName(name:"Track"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
        workLevel: fieldValueByName(name:"Work level"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
        content{
          __typename
          ... on Issue{
            number title state stateReason updatedAt closedAt repository{ nameWithOwner isPrivate }
            parent{ number repository{ nameWithOwner } }
            subIssues(first:100){ totalCount nodes{ number state repository{ nameWithOwner } } }
            blockedBy(first:20){ totalCount nodes{ number state repository{ nameWithOwner } } }
          }
          ... on PullRequest{ number title state updatedAt closedAt repository{ nameWithOwner isPrivate } }
          ... on DraftIssue{ title }
        }
      }
    }
  }}
}
"""

LABELS = ("graphql", "search", "comments", "refstates", "editors")


class FetchError(Exception):
    """A failed read. Built ONLY from fixed text: an argv or response body would publish private names."""


def _rules():
    path = Path(__file__).with_name("board-currency-rules.py")
    spec = importlib.util.spec_from_file_location("board_currency_rules", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def gh(label: str, args: list[str]) -> str:
    assert label in LABELS, label
    cmd = [os.environ.get("BOARD_CURRENCY_GH", "gh"), *args]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=180, check=False)
    except subprocess.TimeoutExpired:
        raise FetchError(f"{label} call timed out") from None
    except OSError as exc:
        raise FetchError(f"{label} call could not start ({type(exc).__name__})") from None
    if proc.returncode:
        raise FetchError(f"{label} call failed (exit {proc.returncode})")
    return proc.stdout


def graphql(label: str, args: list[str]) -> dict:
    try:
        response = json.loads(gh(label, ["api", "graphql", *args]))
    except json.JSONDecodeError:
        raise FetchError(f"{label} response is not JSON") from None
    if not isinstance(response, dict) or response.get("errors"):
        raise FetchError(f"{label} response carries errors")
    return response.get("data") or {}


def fetch_items(owner: str, number: int, max_pages: int) -> tuple[dict, list[dict]]:
    items: list[dict] = []
    cursor: str | None = None
    project: dict = {}
    total: int | None = None
    for _page in range(max_pages):
        args = ["-F", f"owner={owner}", "-F", f"number={number}", "-f", f"query={QUERY}"]
        if cursor:
            args += ["-f", f"cursor={cursor}"]
        node = (graphql("graphql", args).get("organization") or {}).get("projectV2")
        if not node or not isinstance(node.get("items"), dict):
            raise FetchError("graphql response has no organization.projectV2.items")
        page = node["items"]
        project = {"owner": owner, "number": number, "id": node.get("id"), "title": node.get("title")}
        total, nodes, info = page.get("totalCount"), page.get("nodes"), page.get("pageInfo") or {}
        if not isinstance(nodes, list) or not isinstance(total, int) or "hasNextPage" not in info:
            raise FetchError("graphql page is missing nodes, totalCount or pageInfo")
        items.extend(nodes)
        if not info["hasNextPage"]:
            break
        cursor = info.get("endCursor")
        if not cursor:
            raise FetchError("graphql page says hasNextPage without an endCursor")
    else:
        raise FetchError(f"project still has more pages after {max_pages} page(s): refusing a partial board")
    if total is None or len(items) != total:
        raise FetchError("item count differs from the project's totalCount")
    project["totalCount"] = total
    return project, items


def _visibility(value: object) -> bool | None:
    return value if isinstance(value, bool) else None


def _links(conn: object) -> dict | None:
    """A native connection as {total, nodes:[{repo, number, state}]}; None when absent or malformed."""
    if not isinstance(conn, dict) or not isinstance(conn.get("nodes"), list) or not isinstance(conn.get("totalCount"), int):
        return None
    nodes = []
    for n in conn["nodes"]:
        if not isinstance(n, dict):
            return None
        nodes.append({"repo": (n.get("repository") or {}).get("nameWithOwner"), "number": n.get("number"),
                      "state": n.get("state")})
    return {"total": conn["totalCount"], "nodes": nodes}


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
        "comments": None,
    }
    if not isinstance(content, dict):
        item["unreadable"] = True
        return item
    kind = content.get("__typename")
    item["type"] = kind
    item["title"] = content.get("title")
    if kind in ("Issue", "PullRequest"):
        repo = (content.get("repository") or {}).get("nameWithOwner")
        item.update(repo=repo, repoPrivate=_visibility((content.get("repository") or {}).get("isPrivate")),
                    number=content.get("number"), state=content.get("state"),
                    stateReason=content.get("stateReason"), closedAt=content.get("closedAt"),
                    contentUpdatedAt=content.get("updatedAt"))
        if kind == "Issue":
            parent = content.get("parent")
            item["parent"] = ({"repo": (parent.get("repository") or {}).get("nameWithOwner"),
                               "number": parent.get("number")} if isinstance(parent, dict) else None)
            item["subIssues"] = _links(content.get("subIssues"))
            item["blockedBy"] = _links(content.get("blockedBy"))
            if item["subIssues"] is None or item["blockedBy"] is None:
                item["unreadable"] = True
        if not repo or item["number"] is None or not item["state"]:
            item["unreadable"] = True
    elif kind != "DraftIssue":
        item["unreadable"] = True
    return item


def search_prs(query: str, merged: bool) -> list[dict]:
    prs: list[dict] = []
    page = 1
    while True:
        out = gh("search", ["api", f"search/issues?q={query}&per_page=100&page={page}&sort=updated&order=desc"])
        try:
            doc = json.loads(out)
        except json.JSONDecodeError:
            raise FetchError("search response is not JSON") from None
        if doc.get("incomplete_results") is not False or not isinstance(doc.get("items"), list):
            raise FetchError("PR search returned incomplete results")
        total = doc.get("total_count")
        if not isinstance(total, int) or total >= 1000:
            raise FetchError("PR search total_count is not a trustworthy count (cap is 1000)")
        for raw in doc["items"]:
            repo_url = str(raw.get("repository_url", ""))
            repo = repo_url.split("/repos/", 1)[-1] if "/repos/" in repo_url else ""
            if not repo or raw.get("number") is None:
                raise FetchError("PR search item lacks repository_url or number")
            merged_at = (raw.get("pull_request") or {}).get("merged_at")
            if merged and not merged_at:
                raise FetchError("merged PR search item lacks merged_at")
            prs.append({"repo": repo, "number": raw["number"], "draft": bool(raw.get("draft")),
                        "author": (raw.get("user") or {}).get("login"),
                        "title": raw.get("title") or "", "state": "MERGED" if merged else "OPEN",
                        "updatedAt": raw.get("updated_at"), "mergedAt": merged_at, "body": raw.get("body") or ""})
        if len(prs) >= total or not doc["items"]:
            break
        page += 1
        if page > 10:
            raise FetchError("PR search needs more than 10 pages")
    if len(prs) != total:
        raise FetchError("PR count differs from the search total")
    return prs


def repo_visibility(repo: str, known: dict[str, bool | None]) -> bool | None:
    if repo in known:
        return known[repo]
    try:
        value = json.loads(gh("refstates", ["api", f"repos/{repo}", "--jq", ".private"]) or "null")
    except (FetchError, json.JSONDecodeError):
        value = None
    known[repo] = value if isinstance(value, bool) else None
    return known[repo]


def fetch_comments(repo: str, number: int, roots: set[str]) -> list[dict]:
    out = gh("comments", ["api", f"repos/{repo}/issues/{number}/comments?per_page=100", "--paginate", "--jq", ".[]"])
    try:
        rows = [json.loads(line) for line in out.splitlines() if line.strip()]
    except json.JSONDecodeError:
        raise FetchError("comments output is not parseable") from None
    out = []
    for c in rows:
        author = (c.get("user") or {}).get("login", "")
        body = c.get("body") or ""
        keep = body if str(author).lower() in roots else body[:COMMENT_BODY_LIMIT]
        out.append({"id": c.get("id"), "author": author, "createdAt": c.get("created_at"), "body": keep,
                    "truncated": len(keep) < len(body)})
    return out


def needs_comments(item: dict, effective: dt.datetime, rules) -> bool:
    if item.get("unreadable") or item.get("type") != "Issue":
        return False
    open_ = item.get("state") == "OPEN"
    level, status = item.get("workLevel"), item.get("status")
    if open_ and status == "Blocked":
        return True
    if level == "Outcome":
        closed_at = rules.parse_time(item.get("closedAt"))
        return open_ or (closed_at is not None and closed_at >= effective)
    return open_ and level == "Delivery" and status in ("In progress", "In review")


def fetch_editors(prs: list[dict], rules) -> None:
    """Add `edited`/`editor` to every PR whose body carries an override marker, so the rules can check
    the authorship rule. A failed read leaves them absent, and the rules then do not honour the override."""
    wanted = [pr for pr in prs if rules.OVERRIDE.search(rules.visible_text(pr.get("body")))]
    for start in range(0, len(wanted), 50):
        chunk = wanted[start:start + 50]
        parts = []
        for n, pr in enumerate(chunk):
            owner, name = pr["repo"].split("/", 1)
            parts.append(f'p{n}: repository(owner:{json.dumps(owner)},name:{json.dumps(name)})'
                         f'{{ pullRequest(number:{int(pr["number"])}){{ lastEditedAt editor{{login}} author{{login}} }} }}')
        try:
            data = graphql("editors", ["-f", "query={" + " ".join(parts) + "}"])
        except FetchError:
            continue
        for n, pr in enumerate(chunk):
            node = (data.get(f"p{n}") or {}).get("pullRequest")
            if isinstance(node, dict):
                pr["author"] = (node.get("author") or {}).get("login")
                pr["edited"] = node.get("lastEditedAt") is not None
                pr["editor"] = (node.get("editor") or {}).get("login")


def fetch_ref_states(refs: list[str]) -> dict[str, str]:
    """Batched state of marker-named blockers not on the board. A miss stays absent (rules: Unknown)."""
    states: dict[str, str] = {}
    for start in range(0, len(refs), 50):
        chunk = refs[start:start + 50]
        parts = []
        for n, ref in enumerate(chunk):
            repo, number = ref.split("#", 1)
            owner, name = repo.split("/", 1)
            parts.append(f'r{n}: repository(owner:{json.dumps(owner)},name:{json.dumps(name)})'
                         f'{{ issueOrPullRequest(number:{int(number)}){{ ... on Issue{{state}} ... on PullRequest{{state}} }} }}')
        try:
            data = graphql("refstates", ["-f", "query={" + " ".join(parts) + "}"])
        except FetchError:
            continue
        for n, ref in enumerate(chunk):
            state = ((data.get(f"r{n}") or {}).get("issueOrPullRequest") or {}).get("state")
            if isinstance(state, str):
                states[ref] = state
    return states


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--owner", default="FS-GG")
    ap.add_argument("--project", type=int, default=3)
    ap.add_argument("--out", required=True)
    ap.add_argument("--max-pages", type=int, default=10, help="GraphQL pages of 50 items (default 10)")
    args = ap.parse_args(argv)
    rules = _rules()
    try:
        started = dt.datetime.now(dt.timezone.utc)
        project, nodes = fetch_items(args.owner, args.project, args.max_pages)
        items = [normalize_item(n) for n in nodes]
        known = {i["repo"]: i.get("repoPrivate") for i in items if i.get("repo")}
        since = (started - dt.timedelta(hours=rules.PARAMS["mergedWindowHours"])).strftime("%Y-%m-%dT%H:%M:%SZ")
        prs = search_prs(f"org:{args.owner}+is:pr+is:open", merged=False)
        prs += search_prs(f"org:{args.owner}+is:pr+is:merged+merged:%3E%3D{since}", merged=True)
        # v2 carry: routine PRs merged inside the carry window, and every merged PR carrying an override.
        marker = "%22fsgg:routine-development/v1%22+in:body"
        carry_from = max(rules.parse_time(rules.PARAMS["carryFrom"]),
                         started - dt.timedelta(days=rules.PARAMS["carryDays"]))
        if carry_from < started - dt.timedelta(hours=rules.PARAMS["mergedWindowHours"]):
            prs += search_prs(f"org:{args.owner}+is:pr+is:merged+{marker}+merged:%3E%3D"
                              f"{carry_from.strftime('%Y-%m-%dT%H:%M:%SZ')}", merged=True)
        prs += search_prs(f"org:{args.owner}+is:pr+is:merged+%22fsgg:board-gate-override/v1%22+in:body", merged=True)
        unique: dict[tuple[str, int], dict] = {}
        for pr in prs:
            unique.setdefault((pr["repo"], pr["number"]), pr)
        prs = list(unique.values())
        fetch_editors(prs, rules)
        for pr in prs:
            pr["repoPrivate"] = repo_visibility(pr["repo"], known)
        effective = rules.parse_time(rules.PARAMS["effectiveFrom"])
        on_board = {rules.ref_key(i["repo"], i["number"]) for i in items if i.get("repo")}
        wanted: set[str] = set()
        roots = {login.lower() for login in rules.PARAMS["rootLogins"]}
        for item in items:
            if needs_comments(item, effective, rules):
                item["comments"] = fetch_comments(item["repo"], item["number"], roots)
                marker = rules.blocked_on_refs(rules.root_comments(item, roots) or [])
                wanted.update(ref for ref in marker or [] if ref not in on_board)
        ref_states = fetch_ref_states(sorted(wanted))
    except FetchError as exc:
        Path(args.out).unlink(missing_ok=True)  # never leave an earlier run's snapshot to be read as this one
        print(f"board-currency-fetch: FAILED (no snapshot written): {exc}", file=sys.stderr)
        return 1
    except Exception as exc:  # noqa: BLE001 - a traceback would print source lines and values
        Path(args.out).unlink(missing_ok=True)
        print(f"board-currency-fetch: FAILED (no snapshot written): unexpected {type(exc).__name__}", file=sys.stderr)
        return 1

    snapshot = {"schema": SNAPSHOT_SCHEMA, "complete": True, "fetchedAt": started.strftime("%Y-%m-%dT%H:%M:%SZ"),
                "project": project, "items": items, "pullRequests": prs, "refStates": ref_states}
    out = Path(args.out)
    fd, tmp = tempfile.mkstemp(dir=out.parent, prefix=out.name + ".")
    with os.fdopen(fd, "w", encoding="utf-8") as handle:
        json.dump(snapshot, handle, indent=2, sort_keys=True)
        handle.write("\n")
    os.replace(tmp, out)
    print(f"board-currency-fetch: {len(items)} item(s), {len(prs)} PR(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Read the registered GS2-09.7 sandbox prestate without provider effects."""

from __future__ import annotations

import datetime as dt
import hashlib
import json
import re
import urllib.error
import urllib.parse
import urllib.request


OWNER = "FS-GG"
REPOSITORY = "FS.GG.GitHub.Substrate.Sandbox"
REPOSITORY_ID = 1353050537
REPOSITORY_NODE_ID = "R_kgDOUKXpqQ"
REPOSITORY_FULL_NAME = f"{OWNER}/{REPOSITORY}"
REPOSITORY_DESCRIPTION = "fsgg-sandbox-gs2-04-9 disposable qualification target; never production"
PROJECT_NUMBER = 2
PROJECT_NODE_ID = "PVT_kwDOEYAWY84BiESo"
PROJECT_TITLE = "fsgg-sandbox-gs2-04-9"
API = "https://api.github.com"
API_VERSION = "2026-03-10"
SCHEMA = "fsgg.gs2-09-7.sandbox-seed-prestate/1"
SOURCE = "fresh-native-issue-and-project-pages"
NONCE = re.compile(r"[1-9][0-9]*-[1-9][0-9]*-[0-9a-f]{40}\Z")

class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def digest(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def canonical(value: object) -> bytes:
    return (json.dumps(value, sort_keys=True, separators=(",", ":"),
                       ensure_ascii=False) + "\n").encode()


def strict_json(raw: bytes) -> object:
    require(0 < len(raw) <= 16 * 1024 * 1024, "response-size")

    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, "duplicate-json-member")
            result[key] = value
        return result

    try:
        return json.loads(raw, object_pairs_hook=unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(
                              Refused("nonfinite-number")))
    except (UnicodeError, ValueError) as error:
        if isinstance(error, Refused):
            raise
        raise Refused("response-json") from error


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise Refused("redirect-refused")


def _http_request(method: str, url: str, token: str,
                  body: bytes | None = None) -> tuple[int, dict[str, str], bytes]:
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "User-Agent": "fsgg-gs2-09-7-seed-prestate/1",
        "X-GitHub-Api-Version": API_VERSION,
    }
    if body is not None:
        headers["Content-Type"] = "application/json"
    request = urllib.request.Request(url, data=body, headers=headers, method=method)
    try:
        with urllib.request.build_opener(_NoRedirect).open(request, timeout=30) as response:
            raw = response.read(16 * 1024 * 1024 + 1)
            return response.status, dict(response.headers.items()), raw
    except Refused:
        raise
    except urllib.error.HTTPError as error:
        raw = error.read(16 * 1024 * 1024 + 1)
        return error.code, dict(error.headers.items()), raw
    except (OSError, urllib.error.URLError) as error:
        raise Refused("transport-refused") from error


HTTP_PORT = _http_request


def _header(headers: dict[str, str], name: str) -> str | None:
    values = [value for key, value in headers.items() if key.lower() == name.lower()]
    require(len(values) <= 1, "duplicate-response-header")
    return values[0] if values else None


def _request(method: str, url: str, token: str,
             body: bytes | None = None) -> tuple[dict[str, str], bytes]:
    status, headers, raw = HTTP_PORT(method, url, token, body)
    require(type(status) is int, "response-status")
    if status in (401, 403, 404):
        raise Refused(f"http-{status}")
    require(status == 200, "http-status")
    require(type(headers) is dict and type(raw) is bytes, "response-shape")
    return headers, raw


def _repository(token: str) -> dict:
    _, raw = _request("GET", f"{API}/repos/{OWNER}/{REPOSITORY}", token)
    value = strict_json(raw)
    require(type(value) is dict
            and type(value.get("id")) is int and not isinstance(value.get("id"), bool)
            and value.get("id") == REPOSITORY_ID
            and value.get("node_id") == REPOSITORY_NODE_ID
            and value.get("full_name") == REPOSITORY_FULL_NAME
            and value.get("private") is True
            and value.get("description") == REPOSITORY_DESCRIPTION,
            "foreign-repository")
    return {"sha256": digest(raw)}


def _next_issue_url(link: str | None, page_size: int, seen: set[str]) -> str | None:
    if not link:
        require(page_size < 100, "missing-pagination")
        return None
    relations = {}
    for entry in link.split(","):
        match = re.fullmatch(r'\s*<([^>]+)>;\s*rel="([a-z]+)"\s*', entry)
        require(match is not None, "pagination-link")
        target, relation = match.groups()
        require(relation not in relations, "duplicate-pagination-relation")
        relations[relation] = target
    target = relations.get("next")
    if target is None:
        require(page_size < 100, "missing-pagination")
        return None
    parsed = urllib.parse.urlsplit(target)
    require(parsed.scheme == "https" and parsed.netloc == "api.github.com"
            and parsed.fragment == "" and parsed.username is None
            and parsed.path in (f"/repos/{OWNER}/{REPOSITORY}/issues",
                                f"/repositories/{REPOSITORY_ID}/issues"),
            "escaped-pagination")
    pairs = urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
    require(len(pairs) == len(dict(pairs)), "duplicate-pagination-query")
    query = dict(pairs)
    require(query.get("state") == "all" and query.get("per_page") == "100"
            and set(query).issubset({"state", "per_page", "page", "after"})
            and any(query.get(name, "") for name in ("page", "after")),
            "escaped-pagination")
    require(target not in seen, "pagination-cycle")
    return target


def _issues(token: str, nonce: str) -> tuple[dict, dict[str, tuple[int, bool]]]:
    url = f"{API}/repos/{OWNER}/{REPOSITORY}/issues?state=all&per_page=100"
    seen_urls: set[str] = set()
    records = []
    page_digests = []
    identities: dict[str, tuple[int, bool]] = {}
    numbers: set[int] = set()
    nonce_nodes: list[str] = []
    marker = f"[fsgg:gs2-09-7:{nonce}]"
    while url is not None:
        require(url not in seen_urls and len(seen_urls) < 1000, "pagination-cycle")
        seen_urls.add(url)
        headers, raw = _request("GET", url, token)
        page_digests.append(digest(raw))
        page = strict_json(raw)
        require(type(page) is list and len(page) <= 100, "issue-page")
        for row in page:
            require(type(row) is dict, "issue-row")
            database_id, node_id, number = row.get("id"), row.get("node_id"), row.get("number")
            title, body, state = row.get("title"), row.get("body"), row.get("state")
            require(type(database_id) is int and not isinstance(database_id, bool) and database_id > 0
                    and type(node_id) is str and node_id
                    and type(number) is int and not isinstance(number, bool) and number > 0
                    and type(title) is str and (body is None or type(body) is str)
                    and state in ("open", "closed"), "issue-shape")
            pull = "pull_request" in row
            require(node_id not in identities and number not in numbers, "duplicate-issue")
            identities[node_id] = (number, pull)
            numbers.add(number)
            owned = marker in title or (body is not None and marker in body)
            require(not (pull and owned), "nonce-pull-request")
            if owned:
                nonce_nodes.append(node_id)
            records.append({"id": database_id, "nodeId": node_id, "number": number,
                            "kind": "pull-request" if pull else "issue",
                            "sha256": digest(canonical(row))})
        url = _next_issue_url(_header(headers, "link"), len(page), seen_urls)
    records.sort(key=lambda row: (row["id"], row["nodeId"]))
    nonce_nodes.sort()
    return ({"pages": len(seen_urls), "pageSha256": page_digests, "records": records,
             "nonceIssueNodeIds": nonce_nodes}, identities)


def _next_project_url(link: str | None, page_size: int, seen: set[str]) -> str | None:
    if not link:
        require(page_size < 100, "missing-project-pagination")
        return None
    relations = {}
    for entry in link.split(","):
        match = re.fullmatch(r'\s*<([^>]+)>;\s*rel="([a-z]+)"\s*', entry)
        require(match is not None, "project-pagination-link")
        target, relation = match.groups()
        require(relation not in relations, "duplicate-project-pagination-relation")
        relations[relation] = target
    target = relations.get("next")
    if target is None:
        require(page_size < 100, "missing-project-pagination")
        return None
    parsed = urllib.parse.urlsplit(target)
    require(parsed.scheme == "https" and parsed.netloc == "api.github.com"
            and parsed.fragment == "" and parsed.username is None
            and parsed.path == f"/orgs/{OWNER}/projectsV2/{PROJECT_NUMBER}/items",
            "escaped-project-pagination")
    pairs = urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
    require(len(pairs) == len(dict(pairs)), "duplicate-project-pagination-query")
    query = dict(pairs)
    require(query.get("per_page") == "100"
            and set(query).issubset({"per_page", "after"})
            and bool(query.get("after")), "escaped-project-pagination")
    require(target not in seen, "project-pagination-cycle")
    return target


def _project(token: str, issue_identities: dict[str, tuple[int, bool]],
             nonce_nodes: list[str]) -> dict:
    project_url = f"{API}/orgs/{OWNER}/projectsV2/{PROJECT_NUMBER}"
    _, project_raw = _request("GET", project_url, token)
    project = strict_json(project_raw)
    require(type(project) is dict
            and project.get("node_id") == PROJECT_NODE_ID
            and project.get("number") == PROJECT_NUMBER
            and project.get("title") == PROJECT_TITLE
            and project.get("public") is False
            and project.get("state") == "open"
            and project.get("closed_at") is None
            and type(project.get("owner")) is dict
            and project["owner"].get("login") == OWNER,
            "foreign-project")
    url: str | None = project_url + "/items?per_page=100"
    seen_urls: set[str] = set()
    rows = []
    item_ids: set[str] = set()
    content_ids: set[str] = set()
    page_digests = []
    while url is not None:
        require(url not in seen_urls and len(seen_urls) < 1000,
                "project-pagination-cycle")
        seen_urls.add(url)
        headers, raw = _request("GET", url, token)
        page_digests.append(digest(raw))
        nodes = strict_json(raw)
        require(type(nodes) is list and len(nodes) <= 100, "project-page")
        for row in nodes:
            require(type(row) is dict and type(row.get("node_id")) is str and row["node_id"],
                    "project-item")
            item_id = row["node_id"]
            require(item_id not in item_ids, "duplicate-project-item")
            item_ids.add(item_id)
            content = row.get("content")
            require(row.get("content_type") == "Issue" and type(content) is dict,
                    "non-issue-project-item")
            issue_id, number = content.get("node_id"), content.get("number")
            require(type(issue_id) is str and issue_id
                    and type(number) is int and not isinstance(number, bool) and number > 0
                    and content.get("repository_url") ==
                    f"{API}/repos/{OWNER}/{REPOSITORY}",
                    "foreign-project-content")
            require(issue_id in issue_identities
                    and issue_identities[issue_id] == (number, False),
                    "unknown-project-ownership")
            require(issue_id not in content_ids, "duplicate-project-content")
            content_ids.add(issue_id)
            rows.append({"itemNodeId": item_id, "issueNodeId": issue_id,
                         "issueNumber": number, "sha256": digest(canonical(row))})
        url = _next_project_url(_header(headers, "link"), len(nodes), seen_urls)
    rows.sort(key=lambda row: row["itemNodeId"])
    nonce_set = set(nonce_nodes)
    nonce_items = [row["itemNodeId"] for row in rows if row["issueNodeId"] in nonce_set]
    return {"identitySha256": digest(project_raw), "pages": len(seen_urls),
            "pageSha256": page_digests, "totalCount": len(rows), "records": rows,
            "nonceProjectItemNodeIds": nonce_items}


def _capture(token: str, nonce: str) -> dict:
    repository = _repository(token)
    issues, identities = _issues(token, nonce)
    project = _project(token, identities, issues["nonceIssueNodeIds"])
    return {"repository": repository, "issues": issues, "project": project}


def describe() -> dict:
    return {
        "schema": "fsgg.gs2-09-7.sandbox-seed-prestate-producer/1",
        "repositoryId": REPOSITORY_ID,
        "projectNodeId": PROJECT_NODE_ID,
        "source": SOURCE,
        "credentialScope": "protected-host-only",
        "candidateCanWrite": False,
    }


def produce(token: str, facts: dict) -> dict:
    """Capture two complete equal native snapshots and return only their summary."""
    require(type(token) is str and token.strip() == token and len(token) >= 20,
            "installation-token")
    require(type(facts) is dict and type(facts.get("runNonce")) is str
            and NONCE.fullmatch(facts["runNonce"]) is not None,
            "run-nonce")
    nonce = facts["runNonce"]
    first = _capture(token, nonce)
    second = _capture(token, nonce)
    require(first == second, "changed-population")
    snapshot_sha = digest(canonical(first))
    issue_count = len(first["issues"]["nonceIssueNodeIds"])
    project_count = len(first["project"]["nonceProjectItemNodeIds"])
    require(type(issue_count) is int and type(project_count) is int, "typed-counts")
    summary = {
        "schema": SCHEMA,
        "complete": True,
        "repositoryId": REPOSITORY_ID,
        "projectNodeId": PROJECT_NODE_ID,
        "nonceIssueCount": issue_count,
        "nonceProjectItemCount": project_count,
        "snapshotSha256": snapshot_sha,
    }
    now = dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
    capture_id = digest(canonical({"schema": "fsgg.gs2-09-7.sandbox-seed-prestate-capture/1",
                                   "runNonce": nonce, "snapshotSha256": snapshot_sha}))
    return {"runNonce": nonce, "raw": canonical(summary), "captureId": capture_id,
            "observedAt": now, "source": SOURCE}

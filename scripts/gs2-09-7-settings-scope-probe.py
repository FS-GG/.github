#!/usr/bin/env python3
"""Probe the registered sandbox's private settings scope without retaining payloads."""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path


API = "https://api.github.com"
API_VERSION = "2022-11-28"
OWNER = "FS-GG"
REPOSITORY = "FS.GG.GitHub.Substrate.Sandbox"
REPOSITORY_ID = 1353050537
REPOSITORY_NODE_ID = "R_kgDOUKXpqQ"
APP_SLUG = "fs-gg-cross-repo-dispatch"
PROJECT_NUMBER = 2
PROJECT_NODE_ID = "PVT_kwDOEYAWY84BiESo"
MAX_BODY = 1024 * 1024
MAX_PAGES = 100
EXPECTED_WRITES = {
    "administration": "write",
    "contents": "write",
    "issues": "write",
    "pull_requests": "write",
    "organization_projects": "write",
}

STATIC_PROBES = (
    ("repository-identity", f"/repos/{OWNER}/{REPOSITORY}", False, "metadata:read"),
    ("repository-actions", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions", False, "administration:read"),
    ("repository-actions-access", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions/access", False, "administration:read"),
    ("repository-private-fork-workflows", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions/fork-pr-workflows-private-repos", False, "administration:read"),
    ("repository-environments", f"/repos/{OWNER}/{REPOSITORY}/environments?per_page=100", True, "actions:read"),
    ("repository-rulesets", f"/repos/{OWNER}/{REPOSITORY}/rulesets?includes_parents=true&per_page=100", True, "administration:read"),
    ("organization-actions", f"/orgs/{OWNER}/actions/permissions", False, "organization_administration:read"),
    ("organization-private-fork-workflows", f"/orgs/{OWNER}/actions/permissions/fork-pr-workflows-private-repos", False, "organization_administration:read"),
    ("organization-custom-property-schema", f"/orgs/{OWNER}/properties/schema?per_page=100", True, "organization_custom_properties:read"),
    ("organization-immutable-releases", f"/orgs/{OWNER}/settings/immutable-releases", False, "organization_administration:read"),
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def allowed_url(value: str, allow_page_one: bool = False) -> bool:
    parsed = urllib.parse.urlsplit(value)
    if (parsed.scheme, parsed.netloc) != ("https", "api.github.com") or parsed.fragment:
        return False
    environment_secrets = re.fullmatch(
        rf"/repos/{re.escape(OWNER)}/{re.escape(REPOSITORY)}/environments/[^/]+/secrets",
        parsed.path,
    )
    query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
    if any(len(values) != 1 for values in query.values()):
        return False
    if environment_secrets is not None:
        expected = {"per_page": ["100"]}
        paginated = True
    else:
        matched = [item for item in STATIC_PROBES
                   if urllib.parse.urlsplit(API + item[1]).path == parsed.path]
        if len(matched) != 1:
            return False
        expected = urllib.parse.parse_qs(urllib.parse.urlsplit(API + matched[0][1]).query)
        paginated = matched[0][2]
    permitted = set(expected) | ({"page"} if paginated else set())
    return set(query).issubset(permitted) and all(query.get(key) == values for key, values in expected.items()) \
        and ("page" not in query or query["page"][0].isdigit()
             and int(query["page"][0]) >= (1 if allow_page_one else 2))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):  # noqa: ANN001
        return None


def get(url: str, token: str, opener=None) -> tuple[int, dict[str, str], bytes]:
    require(allowed_url(url), "request escaped the protected GET allowlist")
    request = urllib.request.Request(
        url,
        method="GET",
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "fsgg-gs2-09-7-settings-scope-probe",
            "X-GitHub-Api-Version": API_VERSION,
        },
    )
    client = opener or urllib.request.build_opener(NoRedirect)
    try:
        response = client.open(request, timeout=30)
    except urllib.error.HTTPError as error:
        response = error
    raw = response.read(MAX_BODY + 1)
    require(len(raw) <= MAX_BODY, "provider response exceeded one MiB")
    return response.status, {key.lower(): value for key, value in response.headers.items()}, raw


def link_relations(headers: dict[str, str]) -> dict[str, str]:
    links = headers.get("link")
    if not links:
        return {}
    relations = {}
    for item in links.split(","):
        match = re.fullmatch(r'\s*<([^>]+)>;\s*rel="([^"]+)"\s*', item)
        require(match is not None, "malformed provider Link header")
        relation = match.group(2)
        require(relation in {"first", "prev", "next", "last"}, "unknown pagination relation")
        require(relation not in relations, f"duplicate {relation} pagination relation")
        require(allowed_url(match.group(1), allow_page_one=True),
                f"{relation} pagination relation escaped the allowlist")
        relations[relation] = match.group(1)
    return relations


def page_number(url: str) -> int:
    query = urllib.parse.parse_qs(urllib.parse.urlsplit(url).query)
    return int(query.get("page", ["1"])[0])


def same_population(left: str, right: str) -> bool:
    first = urllib.parse.urlsplit(left)
    second = urllib.parse.urlsplit(right)
    if first.path != second.path:
        return False
    first_query = urllib.parse.parse_qs(first.query)
    second_query = urllib.parse.parse_qs(second.query)
    first_query.pop("page", None)
    second_query.pop("page", None)
    return first_query == second_query


def disposition(status: int) -> dict[str, str]:
    access = {
        200: "accessible",
        403: "forbidden",
        404: "not-found",
        429: "rate-limited",
    }.get(status, "provider-status")
    return {
        "access": access,
        "plan": "unknown",
        "feature": "unknown",
        "inheritance": "unknown",
    }


def decode_object(raw: bytes, label: str) -> dict:
    try:
        value = json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} returned malformed JSON") from error
    require(isinstance(value, dict), f"{label} returned a non-object")
    return value


def decode_collection(raw: bytes, field: str | None, label: str) -> list:
    try:
        value = json.loads(raw)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} returned malformed JSON") from error
    if field is not None:
        require(isinstance(value, dict), f"{label} returned a non-object")
        value = value.get(field)
    require(isinstance(value, list), f"{label} returned a non-list population")
    return value


def page_rows(name: str, raw: bytes) -> list:
    if name == "repository-environments":
        return decode_collection(raw, "environments", name)
    if name.startswith("environment-secrets-"):
        return decode_collection(raw, "secrets", name)
    return decode_collection(raw, None, name)


def validate_page_links(name: str, url: str, status: int, raw: bytes,
                        relations: dict[str, str], expected_last: int | None) -> tuple[str | None, int | None]:
    current = page_number(url)
    require(all(same_population(url, target) for target in relations.values()),
            f"{name} pagination changed endpoint")
    if status != 200:
        require(not relations, f"{name} error response unexpectedly paginated")
        return None, expected_last
    rows = page_rows(name, raw)
    if current == 1:
        require("first" not in relations and "prev" not in relations,
                f"{name} first page has prior relations")
    else:
        require("first" in relations and "prev" in relations,
                f"{name} prior pagination relations are incomplete")
        require(page_number(relations["first"]) == 1
                and page_number(relations["prev"]) == current - 1,
                f"{name} prior pagination relation is inconsistent")
    following = relations.get("next")
    last = relations.get("last")
    if following is None:
        require(last is None, f"{name} terminal page has a last relation")
        require(expected_last is None or current == expected_last,
                f"{name} terminated before the declared last page")
        return None, expected_last
    require(len(rows) == 100, f"{name} short page has a next relation")
    require(page_number(following) == current + 1, f"{name} next page is not sequential")
    require(last is not None and page_number(last) >= current + 1,
            f"{name} last pagination relation is incomplete")
    declared_last = page_number(last)
    require(expected_last is None or declared_last == expected_last,
            f"{name} last pagination relation drifted")
    return following, declared_last


def page_record(url: str, status: int, raw: bytes, relations: dict[str, str], index: int) -> dict:
    next_url = relations.get("next")
    return {
        "index": index,
        "method": "GET",
        "endpoint": url,
        "apiVersion": API_VERSION,
        "status": status,
        "responseSha256": sha256(raw),
        "links": dict(sorted(relations.items())),
        "nextEndpoint": next_url,
        "terminal": next_url is None,
    }


def probe_pages(name: str, path: str, paginated: bool, permission: str, token: str, opener=None) -> tuple[dict, list[dict]]:
    url = API + path
    pages = []
    decoded = []
    seen = set()
    expected_last = None
    while True:
        require(url not in seen and len(pages) < MAX_PAGES, f"{name} pagination escaped its bound")
        seen.add(url)
        status, headers, raw = get(url, token, opener)
        relations = link_relations(headers)
        require(not relations or paginated, f"{name} unexpectedly paginated")
        if paginated:
            following, expected_last = validate_page_links(
                name, url, status, raw, relations, expected_last)
        else:
            following = None
        pages.append(page_record(url, status, raw, relations, len(pages) + 1))
        if status == 200:
            decoded.append(raw)
        if status != 200 or following is None:
            break
        url = following
    result = {
        "name": name,
        "requiredPermission": permission,
        "classification": disposition(pages[-1]["status"]),
        "pages": pages,
    }
    return result, decoded


def load_mint_proof(path: Path, token: str) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    require(isinstance(value, dict) and value.get("schema") == "fsgg.github-substrate-v2.sandbox-mint-grants/1",
            "mint proof schema is unavailable")
    require(value.get("appSlug") == APP_SLUG and type(value.get("appId")) is int and value["appId"] > 0,
            "mint proof App identity mismatch")
    require(type(value.get("installationId")) is int and value["installationId"] > 0,
            "mint proof installation identity mismatch")
    require(value.get("repositorySelection") == "selected", "mint proof repository selection mismatch")
    actor = value.get("actor")
    require(isinstance(actor, dict) and actor.get("login") == "fs-gg-cross-repo-dispatch[bot]"
            and actor.get("databaseId") == 297630107, "mint proof actor identity mismatch")
    require(value.get("repository") == {"id": REPOSITORY_ID, "nodeId": REPOSITORY_NODE_ID,
                                        "fullName": f"{OWNER}/{REPOSITORY}"},
            "mint proof repository mismatch")
    grants = value.get("permissions")
    require(isinstance(grants, dict) and all(grants.get(key) == level for key, level in EXPECTED_WRITES.items()),
            "mint proof omits the current sandbox grant")
    require(all(re.fullmatch(r"[a-z][a-z0-9_]*", key) and level in ("read", "write")
                for key, level in grants.items()), "mint proof contains a malformed grant")
    require(all(level != "write" or key in EXPECTED_WRITES for key, level in grants.items()),
            "mint proof contains an unexpected write grant")
    require(value.get("tokenSha256") == sha256(token.encode()), "token differs from its mint proof")
    return {
        "appId": value["appId"],
        "appSlug": value["appSlug"],
        "actor": actor,
        "installationId": value["installationId"],
        "repositorySelection": value.get("repositorySelection"),
        "permissions": dict(sorted(grants.items())),
        "mintResponseSha256": value.get("mintResponseSha256"),
    }


def source_binding(workspace: Path, revision: str) -> dict:
    require(re.fullmatch(r"[0-9a-f]{40}", revision) is not None, "protected revision must be full lowercase hex")
    head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=workspace, check=True,
                          capture_output=True, text=True).stdout.strip()
    require(head == revision, "checked-out head differs from protected revision")
    workflow = Path(".github/workflows/github-substrate-v2-settings-scope-probe.yml")
    script = Path("scripts/gs2-09-7-settings-scope-probe.py")
    return {
        "repository": "FS-GG/.github",
        "headSha": revision,
        "workflow": {"path": str(workflow), "sha256": sha256((workspace / workflow).read_bytes())},
        "script": {"path": str(script), "sha256": sha256((workspace / script).read_bytes())},
    }


def build_report(token: str, mint_path: Path, workspace: Path, revision: str, opener=None) -> dict:
    mint = load_mint_proof(mint_path, token)
    probes = []
    environment_parents = []
    repository = None
    for name, path, paginated, permission in STATIC_PROBES:
        result, bodies = probe_pages(name, path, paginated, permission, token, opener)
        probes.append(result)
        if name == "repository-identity" and result["pages"][-1]["status"] == 200:
            repository = decode_object(bodies[0], name)
        elif name == "repository-environments" and result["pages"][-1]["status"] == 200:
            population = []
            totals = []
            for raw in bodies:
                value = decode_object(raw, name)
                require(type(value.get("total_count")) is int and value["total_count"] >= 0,
                        "environment total count is unavailable")
                totals.append(value["total_count"])
                population.extend(decode_collection(raw, "environments", name))
            require(len(set(totals)) == 1 and len(population) == totals[0],
                    "environment population is incomplete")
            identities = []
            for item in population:
                require(isinstance(item, dict) and type(item.get("id")) is int
                        and item["id"] > 0 and isinstance(item.get("node_id"), str)
                        and item["node_id"] and isinstance(item.get("name"), str) and item["name"],
                        "environment parent identity is incomplete")
                identities.append((item["id"], item["node_id"], item["name"]))
            require(all(len(values) == len(set(values)) for values in zip(*identities)) if identities else True,
                    "environment parent identity is duplicated")
            for identifier, node_id, environment_name in identities:
                environment_parents.append({"kind": "environment", "id": identifier, "nodeId": node_id})
                encoded = urllib.parse.quote(environment_name, safe="")
                child_path = f"/repos/{OWNER}/{REPOSITORY}/environments/{encoded}/secrets?per_page=100"
                child, _ = probe_pages(f"environment-secrets-{identifier}", child_path, True,
                                       "environments:read", token, opener)
                child["conditionalParent"] = {"kind": "environment", "id": identifier, "nodeId": node_id}
                probes.append(child)
        elif name == "repository-rulesets" and result["pages"][-1]["status"] == 200:
            inherited = []
            all_rulesets = []
            for raw in bodies:
                for item in decode_collection(raw, None, name):
                    require(isinstance(item, dict) and type(item.get("id")) is int
                            and item["id"] > 0 and isinstance(item.get("node_id"), str) and item["node_id"]
                            and item.get("source_type") in {"Repository", "Organization", "Enterprise"},
                            "ruleset parent identity is incomplete")
                    all_rulesets.append((item["id"], item["node_id"]))
                    if item["source_type"] != "Repository":
                        inherited.append({"kind": "ruleset", "id": item["id"],
                                          "nodeId": item["node_id"], "sourceType": item["source_type"]})
            require(len(all_rulesets) == len(set(all_rulesets))
                    and len({item[0] for item in all_rulesets}) == len(all_rulesets)
                    and len({item[1] for item in all_rulesets}) == len(all_rulesets),
                    "ruleset parent identity is duplicated")
            environment_parents.extend(inherited)
            result["classification"]["inheritance"] = \
                "observed-parent-ids" if inherited else "observed-no-parent-ids"
    require(repository is not None, "repository identity was inaccessible")
    require(repository.get("id") == REPOSITORY_ID and repository.get("node_id") == REPOSITORY_NODE_ID
            and repository.get("full_name") == f"{OWNER}/{REPOSITORY}" and repository.get("private") is True,
            "provider repository identity differs from the registered sandbox")
    owner = repository.get("owner")
    require(isinstance(owner, dict) and owner.get("login") == OWNER and type(owner.get("id")) is int
            and isinstance(owner.get("node_id"), str), "provider account identity is incomplete")
    report = {
        "schema": "fsgg.github-substrate-v2.settings-scope-probe/1",
        "activation": False,
        "source": source_binding(workspace, revision),
        "sandbox": {
            "repository": {"id": REPOSITORY_ID, "nodeId": REPOSITORY_NODE_ID,
                           "fullName": f"{OWNER}/{REPOSITORY}"},
            "project": {"number": PROJECT_NUMBER, "nodeId": PROJECT_NODE_ID,
                        "identitySource": "protected-workflow-constant"},
        },
        "app": mint,
        "account": {"login": owner["login"], "id": owner["id"], "nodeId": owner["node_id"]},
        "conditionalParents": sorted(environment_parents, key=lambda item: (item["id"], item["nodeId"])),
        "probes": probes,
        "authority": {
            "installedAppGrant": "unknown",
            "settingsAuthority": "unavailable",
            "reason": "current-token-probe-only",
        },
    }
    report["fingerprint"] = sha256(canonical(report))
    return report


def main() -> None:
    token = os.environ["FSGG_SANDBOX_TOKEN"]
    require(len(token) > 20 and token.isascii() and not any(character.isspace() for character in token),
            "sandbox token is unavailable")
    workspace = Path(os.environ.get("GITHUB_WORKSPACE", ".")).resolve()
    evidence = Path(os.environ["FSGG_SANDBOX_EVIDENCE_DIR"])
    report = build_report(token, Path(os.environ["FSGG_SANDBOX_MINT_PROOF"]), workspace,
                          os.environ["FSGG_PROTECTED_SHA"])
    evidence.mkdir(parents=True, exist_ok=True)
    (evidence / "settings-scope-probe.json").write_bytes(canonical(report) + b"\n")


if __name__ == "__main__":
    main()

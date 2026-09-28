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
MAX_RULESETS = 1000
EXPECTED_WRITES = {
    "administration": "write",
    "contents": "write",
    "issues": "write",
    "pull_requests": "write",
    "organization_projects": "write",
}
EXPECTED_READS = {
    "metadata": "read",
}

STATIC_PROBES = (
    ("organization-identity", f"/orgs/{OWNER}", False, "metadata:read"),
    ("repository-identity", f"/repos/{OWNER}/{REPOSITORY}", False, "metadata:read"),
    ("repository-actions", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions", False, "administration:read"),
    ("repository-actions-access", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions/access", False, "administration:read"),
    ("repository-private-fork-workflows", f"/repos/{OWNER}/{REPOSITORY}/actions/permissions/fork-pr-workflows-private-repos", False, "administration:read"),
    ("repository-environments", f"/repos/{OWNER}/{REPOSITORY}/environments?per_page=100", True, "actions:read"),
    ("repository-rulesets", f"/repos/{OWNER}/{REPOSITORY}/rulesets?includes_parents=true&per_page=100", True, "metadata:read"),
    ("organization-actions", f"/orgs/{OWNER}/actions/permissions", False, "organization_administration:read"),
    ("organization-private-fork-workflows", f"/orgs/{OWNER}/actions/permissions/fork-pr-workflows-private-repos", False, "organization_administration:read"),
    ("organization-custom-property-schema", f"/orgs/{OWNER}/properties/schema", False, "organization_custom_properties:read"),
    ("repository-custom-property-values", f"/repos/{OWNER}/{REPOSITORY}/properties/values", False, "metadata:read"),
    ("organization-immutable-releases", f"/orgs/{OWNER}/settings/immutable-releases", False, "organization_administration:read"),
)

CUSTOM_PROPERTY_PROBES = {
    name: (path, paginated, permission)
    for name, path, paginated, permission in STATIC_PROBES
    if name in {"organization-custom-property-schema", "repository-custom-property-values"}
}
RULESET_TYPES = {
    "creation", "update", "deletion", "required_linear_history", "required_deployments",
    "required_signatures", "pull_request", "required_status_checks", "non_fast_forward",
    "merge_queue", "code_scanning", "commit_message_pattern", "commit_author_email_pattern",
    "committer_email_pattern", "branch_name_pattern", "tag_name_pattern",
}
RULESET_ACTOR_TYPES = {
    "Integration", "OrganizationAdmin", "RepositoryRole", "Team", "DeployKey",
    "EnterpriseOwner", "EnterpriseRole", "User",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def sha256(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def canonical(value: object) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def allowed_url(value: str, allow_page_one: bool = False) -> bool:
    parsed = urllib.parse.urlsplit(value)
    if ((parsed.scheme, parsed.netloc) != ("https", "api.github.com") or parsed.fragment
            or "%" in parsed.query or "+" in parsed.query):
        return False
    environment_secrets = re.fullmatch(
        rf"/repos/{re.escape(OWNER)}/{re.escape(REPOSITORY)}/environments/[^/]+/secrets",
        parsed.path,
    )
    ruleset_detail = re.fullmatch(
        rf"/repos/{re.escape(OWNER)}/{re.escape(REPOSITORY)}/rulesets/([1-9][0-9]*)",
        parsed.path,
    )
    query = urllib.parse.parse_qs(parsed.query, keep_blank_values=True)
    if any(len(values) != 1 for values in query.values()):
        return False
    if environment_secrets is not None:
        expected = {"per_page": ["100"]}
        paginated = True
    elif ruleset_detail is not None:
        expected = {}
        paginated = False
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
        401: "unauthorized",
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


def refusal_reason(statuses: list[int]) -> str | None:
    if 401 in statuses:
        return "authentication-or-token-grant"
    if 403 in statuses:
        return "permission-or-plan"
    if 404 in statuses:
        return "resource-feature-plan-or-installation-selection"
    if any(status != 200 for status in statuses):
        return "provider-status"
    return None


def decode_json(raw: bytes, label: str) -> object:
    def unique_object(pairs):
        value = {}
        for key, item in pairs:
            require(key not in value, f"{label} returned duplicate JSON member {key}")
            value[key] = item
        return value

    try:
        return json.loads(raw, object_pairs_hook=unique_object)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError(f"{label} returned malformed JSON") from error


def decode_object(raw: bytes, label: str) -> dict:
    value = decode_json(raw, label)
    require(isinstance(value, dict), f"{label} returned a non-object")
    return value


def decode_collection(raw: bytes, field: str | None, label: str) -> list:
    value = decode_json(raw, label)
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


def page_shapes(result: dict) -> list[dict]:
    return [
        {key: page[key] for key in (
            "index", "endpoint", "status", "responseSha256", "links", "nextEndpoint", "terminal")}
        for page in result["pages"]
    ]


def probe_pages(name: str, path: str, paginated: bool, permission: str, token: str, opener=None) -> tuple[dict, list[bytes]]:
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


def validate_repository(value: dict) -> dict:
    require(value.get("id") == REPOSITORY_ID and value.get("node_id") == REPOSITORY_NODE_ID
            and value.get("full_name") == f"{OWNER}/{REPOSITORY}" and value.get("private") is True
            and value.get("visibility") == "private" and value.get("fork") is False,
            "provider repository identity differs from the registered sandbox")
    require(isinstance(value.get("default_branch"), str) and value["default_branch"]
            and isinstance(value.get("updated_at"), str) and value["updated_at"],
            "provider repository revision identity is incomplete")
    require(value.get("source") is None, "registered sandbox unexpectedly reports a fork source")
    owner = value.get("owner")
    require(isinstance(owner, dict) and owner.get("login") == OWNER and type(owner.get("id")) is int
            and owner["id"] > 0 and isinstance(owner.get("node_id"), str) and owner["node_id"],
            "provider account identity is incomplete")
    return owner


def repository_identity(value: dict) -> tuple:
    """Bind the registered repository without treating volatile metadata as identity."""
    owner = validate_repository(value)
    return (value["id"], value["node_id"], value["full_name"], value["private"],
            value["visibility"], value["fork"], value.get("source"),
            owner["login"], owner["id"], owner["node_id"])


def validate_organization(value: dict, repository_owner: dict) -> dict:
    require(value.get("login") == OWNER and type(value.get("id")) is int and value["id"] > 0
            and isinstance(value.get("node_id"), str) and value["node_id"],
            "provider organization identity is incomplete")
    require(value["id"] == repository_owner["id"] and value["node_id"] == repository_owner["node_id"],
            "repository owner differs from the probed organization")
    return {"login": value["login"], "id": value["id"], "nodeId": value["node_id"]}


def property_value_valid(definition: dict, value: object) -> bool:
    if value is None:
        return True
    kind = definition["value_type"]
    allowed = definition["allowed_values"]
    if kind in {"string", "url"}:
        return isinstance(value, str)
    if kind == "single_select":
        return isinstance(value, str) and value in allowed
    if kind == "multi_select":
        return (isinstance(value, list) and all(isinstance(item, str) for item in value)
                and len(value) == len(set(value)) and all(item in allowed for item in value))
    return isinstance(value, str) and value in {"true", "false"}


def custom_property_summary(schema_bodies: list[bytes], value_bodies: list[bytes]) -> dict:
    definitions = {}
    type_counts = {kind: 0 for kind in ("string", "url", "single_select", "multi_select", "true_false")}
    for raw in schema_bodies:
        for definition in decode_collection(raw, None, "organization-custom-property-schema"):
            require(isinstance(definition, dict), "custom property definition is not an object")
            name = definition.get("property_name")
            kind = definition.get("value_type")
            require(isinstance(name, str) and name and name not in definitions,
                    "custom property definition identity is missing or duplicated")
            require(kind in type_counts and definition.get("source_type") == "organization",
                    "custom property definition type or source is unsupported")
            require(definition.get("url") == f"{API}/orgs/{OWNER}/properties/schema/{urllib.parse.quote(name, safe='')}",
                    "custom property definition URL escaped the bound organization")
            require(("required" not in definition or type(definition["required"]) is bool)
                    and ("require_explicit_values" not in definition
                         or type(definition["require_explicit_values"]) is bool)
                    and definition.get("values_editable_by") in {None, "org_actors", "org_and_repo_actors"},
                    "custom property definition controls are incomplete")
            allowed = definition.get("allowed_values")
            allowed = [] if allowed is None else allowed
            require(isinstance(allowed, list) and all(isinstance(item, str) for item in allowed)
                    and len(allowed) == len(set(allowed)),
                    "custom property allowed values are malformed")
            definition = {**definition, "allowed_values": allowed}
            require(property_value_valid(definition, definition.get("default_value")),
                    "custom property default value is malformed")
            definitions[name] = definition
            type_counts[kind] += 1

    returned = {}
    for raw in value_bodies:
        for item in decode_collection(raw, None, "repository-custom-property-values"):
            require(isinstance(item, dict), "repository custom property value is not an object")
            name = item.get("property_name")
            require(isinstance(name, str) and name in definitions and name not in returned,
                    "repository custom property value is unknown or duplicated")
            require(property_value_valid(definitions[name], item.get("value")),
                    "repository custom property value violates its definition")
            returned[name] = item.get("value")

    explicit = 0
    equal_default_unknown = 0
    for name, value in returned.items():
        default = definitions[name].get("default_value")
        if value == default:
            equal_default_unknown += 1
        elif value is not None:
            explicit += 1
    return {
        "definitionCount": len(definitions),
        "definitionTypes": type_counts,
        "returnedValueCount": len(returned),
        "providerProvenExplicitCount": explicit,
        "equalToDefaultProvenanceUnknownCount": equal_default_unknown,
        "omittedProvenanceUnknownCount": len(definitions) - len(returned),
    }


def ruleset_population(bodies: list[bytes]) -> list[dict]:
    population = []
    identities = set()
    node_ids = set()
    for raw in bodies:
        for item in decode_collection(raw, None, "repository-rulesets"):
            require(isinstance(item, dict) and type(item.get("id")) is int and item["id"] > 0
                    and isinstance(item.get("node_id"), str) and item["node_id"],
                    "ruleset identity is incomplete")
            require(item["id"] not in identities and item["node_id"] not in node_ids,
                    "ruleset identity is duplicated")
            require(item.get("source_type") == "Repository"
                    and item.get("source") == f"{OWNER}/{REPOSITORY}",
                    "inherited or foreign ruleset list row is unsupported")
            identities.add(item["id"])
            node_ids.add(item["node_id"])
            population.append(item)
    require(len(population) <= MAX_RULESETS, "ruleset population exceeded its bound")
    return population


def validate_ruleset_detail(value: dict, expected: dict) -> bool:
    require(value.get("id") == expected["id"] and value.get("node_id") == expected["node_id"],
            "ruleset detail identity differs from its list row")
    require(value.get("source_type") == "Repository" and value.get("source") == f"{OWNER}/{REPOSITORY}",
            "inherited or foreign ruleset cannot qualify repository-local authority")
    require(value.get("target") in {"branch", "tag"}, "push or unknown ruleset target is unsupported")
    require(value.get("enforcement") in {"disabled", "active", "evaluate"},
            "ruleset enforcement is unsupported")

    conditions = value.get("conditions")
    require(isinstance(conditions, dict) and set(conditions) == {"ref_name"},
            "ruleset conditions have an unknown shape")
    refs = conditions["ref_name"]
    require(isinstance(refs, dict) and set(refs) == {"include", "exclude"},
            "ruleset ref-name condition has an unknown shape")
    for field in ("include", "exclude"):
        entries = refs[field]
        require(isinstance(entries, list) and all(isinstance(item, str) and item for item in entries)
                and len(entries) == len(set(entries)), "ruleset ref-name condition is malformed")

    rules = value.get("rules")
    require(isinstance(rules, list), "ruleset rules are unavailable")
    for rule in rules:
        require(isinstance(rule, dict) and set(rule).issubset({"type", "parameters"})
                and rule.get("type") in RULESET_TYPES,
                "ruleset contains an unknown rule shape")
        require("parameters" not in rule or isinstance(rule["parameters"], dict),
                "ruleset rule parameters are malformed")

    if "bypass_actors" not in value:
        return False
    actors = value["bypass_actors"]
    require(isinstance(actors, list), "ruleset bypass actors are malformed")
    seen = set()
    for actor in actors:
        require(isinstance(actor, dict) and set(actor) == {"actor_id", "actor_type", "bypass_mode"},
                "ruleset bypass actor has an unknown shape")
        actor_type = actor["actor_type"]
        actor_id = actor["actor_id"]
        require(actor_type in RULESET_ACTOR_TYPES and actor["bypass_mode"] in {"always", "pull_request", "exempt"},
                "ruleset bypass actor is unsupported")
        if actor_type == "DeployKey":
            require(actor_id is None, "deploy-key bypass actor has an unexpected identity")
        elif actor_type in {"OrganizationAdmin", "EnterpriseOwner"}:
            require(actor_id is None or type(actor_id) is int and actor_id > 0,
                    "administrative bypass actor identity is malformed")
        else:
            require(type(actor_id) is int and actor_id > 0, "ruleset bypass actor identity is incomplete")
        require(actor["bypass_mode"] != "pull_request"
                or value["target"] == "branch" and actor_type != "DeployKey",
                "ruleset bypass mode is incompatible with its actor or target")
        marker = (actor_type, actor_id, actor["bypass_mode"])
        require(marker not in seen, "ruleset bypass actor is duplicated")
        seen.add(marker)
    return True


def probe_ruleset_details(population: list[dict], token: str, pass_number: int, opener=None) -> tuple[list[dict], dict[int, bytes], bool]:
    results = []
    bodies = {}
    bypass_visible = True
    for item in population:
        identifier = item["id"]
        result, raw = probe_pages(
            f"repository-ruleset-{identifier}-pass-{pass_number}",
            f"/repos/{OWNER}/{REPOSITORY}/rulesets/{identifier}", False,
            "metadata:read+administration:write-for-bypass-visibility", token, opener)
        results.append(result)
        status = result["pages"][-1]["status"]
        if status == 200:
            require(len(raw) == 1, "ruleset detail response population is malformed")
            detail = decode_object(raw[0], result["name"])
            bypass_visible = validate_ruleset_detail(detail, item) and bypass_visible
            bodies[identifier] = raw[0]
        else:
            bypass_visible = False
    return results, bodies, bypass_visible


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
    require(all(grants.get(key) == level for key, level in EXPECTED_READS.items()),
            "mint proof omits the observed repository read grant")
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
    observations = {}
    environment_parents = []
    repository = None
    organization = None
    for name, path, paginated, permission in STATIC_PROBES:
        result, bodies = probe_pages(name, path, paginated, permission, token, opener)
        probes.append(result)
        observations[name] = (result, bodies)
        if name == "organization-identity" and result["pages"][-1]["status"] == 200:
            organization = decode_object(bodies[0], name)
        elif name == "repository-identity" and result["pages"][-1]["status"] == 200:
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
    require(organization is not None, "organization identity was inaccessible")
    owner = validate_repository(repository)
    account = validate_organization(organization, owner)

    first_ruleset_result, first_ruleset_bodies = observations["repository-rulesets"]
    first_ruleset_status = first_ruleset_result["pages"][-1]["status"]
    first_population = ruleset_population(first_ruleset_bodies) if first_ruleset_status == 200 else []
    first_details, first_detail_bodies, first_bypass_visible = \
        probe_ruleset_details(first_population, token, 1, opener)
    probes.extend(first_details)

    repo_path, repo_paginated, repo_permission = next(
        (path, paginated, permission) for name, path, paginated, permission in STATIC_PROBES
        if name == "repository-identity")
    close_one, close_one_bodies = probe_pages(
        "repository-identity-ruleset-pass-1-close", repo_path, repo_paginated,
        repo_permission, token, opener)
    start_two, start_two_bodies = probe_pages(
        "repository-identity-ruleset-pass-2-start", repo_path, repo_paginated,
        repo_permission, token, opener)
    probes.extend([close_one, start_two])

    rules_path, rules_paginated, rules_permission = next(
        (path, paginated, permission) for name, path, paginated, permission in STATIC_PROBES
        if name == "repository-rulesets")
    second_ruleset_result, second_ruleset_bodies = probe_pages(
        "repository-rulesets-pass-2", rules_path, rules_paginated, rules_permission, token, opener)
    probes.append(second_ruleset_result)
    second_ruleset_status = second_ruleset_result["pages"][-1]["status"]
    second_population = ruleset_population(second_ruleset_bodies) if second_ruleset_status == 200 else []
    second_details, second_detail_bodies, second_bypass_visible = \
        probe_ruleset_details(second_population, token, 2, opener)
    probes.extend(second_details)
    close_two, close_two_bodies = probe_pages(
        "repository-identity-ruleset-pass-2-close", repo_path, repo_paginated,
        repo_permission, token, opener)
    probes.append(close_two)

    first_repository_bytes = observations["repository-identity"][1]
    repository_reads = [first_repository_bytes, close_one_bodies, start_two_bodies, close_two_bodies]
    require(all(len(bodies) == 1 for bodies in repository_reads),
            "repository identity response is unavailable")
    repository_objects = [decode_object(bodies[0], "repository-identity")
                          for bodies in repository_reads]
    require(all(repository_identity(value) == repository_identity(repository)
                for value in repository_objects),
            "registered repository identity changed during ruleset reads")
    for label, bodies in (("ruleset-pass-1-close", close_one_bodies),
                          ("ruleset-pass-2-start", start_two_bodies),
                          ("ruleset-pass-2-close", close_two_bodies)):
        require(len(bodies) == 1, f"{label} repository response is unavailable")
        validate_repository(decode_object(bodies[0], label))

    require([page["status"] for page in first_ruleset_result["pages"]]
            == [page["status"] for page in second_ruleset_result["pages"]],
            "repository ruleset status changed between reads")
    require(page_shapes(first_ruleset_result) == page_shapes(second_ruleset_result),
            "repository ruleset request URI or terminal pagination changed between reads")
    if first_ruleset_status == 200:
        require(first_ruleset_bodies == second_ruleset_bodies,
                "repository ruleset raw population changed between reads")
        require(first_population == second_population,
                "repository ruleset typed population changed between reads")
        require(first_detail_bodies.keys() == second_detail_bodies.keys(),
                "repository ruleset detail population changed between reads")
        require(len(first_details) == len(second_details),
                "repository ruleset detail evidence changed between reads")
        for first_detail, second_detail in zip(first_details, second_details):
            require(page_shapes(first_detail) == page_shapes(second_detail),
                    "repository ruleset detail request URI or terminal state changed between reads")
        for identifier in first_detail_bodies:
            require(first_detail_bodies[identifier] == second_detail_bodies[identifier],
                    "repository ruleset detail bytes changed between reads")
            require(decode_json(first_detail_bodies[identifier], "repository-ruleset-detail")
                    == decode_json(second_detail_bodies[identifier], "repository-ruleset-detail"),
                    "repository ruleset typed detail changed between reads")

    ruleset_statuses = [first_ruleset_status, second_ruleset_status]
    ruleset_statuses.extend(
        item["pages"][-1]["status"] for item in first_details + second_details)
    plan = organization.get("plan")
    qualifying_plan = isinstance(plan, dict) and plan.get("name") in {"team", "enterprise"}
    if 401 in ruleset_statuses:
        ruleset_verdict = "refused-unauthorized"
    elif 403 in ruleset_statuses:
        ruleset_verdict = "refused-forbidden"
    elif 404 in ruleset_statuses:
        ruleset_verdict = "unknown-not-found"
    elif any(status != 200 for status in ruleset_statuses):
        ruleset_verdict = "refused-provider-status"
    elif not first_population:
        ruleset_verdict = "unknown-empty-population"
    elif not qualifying_plan:
        ruleset_verdict = "unknown-private-plan"
    elif not (first_bypass_visible and second_bypass_visible):
        ruleset_verdict = "partial-bypass-actors-omitted"
    else:
        ruleset_verdict = "qualified-current-token-read"

    confirmation_specs = {
        **CUSTOM_PROPERTY_PROBES,
        "organization-identity": next((path, paginated, permission) for name, path, paginated, permission
                                      in STATIC_PROBES if name == "organization-identity"),
        "repository-identity": next((path, paginated, permission) for name, path, paginated, permission
                                    in STATIC_PROBES if name == "repository-identity"),
    }
    confirmations = {}
    for name, (path, paginated, permission) in confirmation_specs.items():
        confirmation, bodies = probe_pages(f"{name}-confirmation", path, paginated, permission, token, opener)
        probes.append(confirmation)
        confirmations[name] = (confirmation, bodies)
        first, first_bodies = observations[name]
        require([page["status"] for page in confirmation["pages"]]
                == [page["status"] for page in first["pages"]], f"{name} status changed between reads")
        if name == "repository-identity":
            require([{key: value for key, value in page.items() if key != "responseSha256"}
                     for page in page_shapes(confirmation)]
                    == [{key: value for key, value in page.items() if key != "responseSha256"}
                        for page in page_shapes(first)],
                    "repository-identity request URI or terminal state changed between reads")
        else:
            require(page_shapes(confirmation) == page_shapes(first),
                    f"{name} request URI, response digest, or terminal state changed between reads")
        if first["pages"][-1]["status"] == 200:
            if name == "repository-identity":
                require(len(bodies) == 1 and
                        repository_identity(decode_object(bodies[0], name))
                        == repository_identity(repository),
                        "registered repository identity changed during settings reads")
            else:
                require(first_bodies == bodies, f"{name} raw response changed between reads")
                require([decode_json(raw, name) for raw in first_bodies]
                        == [decode_json(raw, name) for raw in bodies],
                        f"{name} typed response changed between reads")

    confirmed_repository = decode_object(confirmations["repository-identity"][1][0], "repository-identity-confirmation")
    confirmed_owner = validate_repository(confirmed_repository)
    repository_reads.append(confirmations["repository-identity"][1])
    repository_objects.append(confirmed_repository)
    repository_raw_stable = all(bodies == first_repository_bytes for bodies in repository_reads)
    repository_revision_stable = all(
        (value["default_branch"], value["updated_at"])
        == (repository["default_branch"], repository["updated_at"])
        for value in repository_objects)
    repository_changed_keys = sorted({
        key for value in repository_objects[1:]
        for key in set(repository) | set(value) if repository.get(key) != value.get(key)
    })
    confirmed_organization = decode_object(confirmations["organization-identity"][1][0],
                                           "organization-identity-confirmation")
    require(validate_organization(confirmed_organization, confirmed_owner) == account,
            "organization identity changed between settings reads")

    schema_result, schema_bodies = observations["organization-custom-property-schema"]
    values_result, value_bodies = observations["repository-custom-property-values"]
    custom_statuses = [schema_result["pages"][-1]["status"], values_result["pages"][-1]["status"]]
    if custom_statuses == [200, 200]:
        property_summary = custom_property_summary(schema_bodies, value_bodies)
        property_verdict = "qualified-current-token-read"
    else:
        property_summary = None
        if 401 in custom_statuses:
            property_verdict = "refused-unauthorized"
        elif 403 in custom_statuses:
            property_verdict = "refused-forbidden"
        elif 404 in custom_statuses:
            property_verdict = "unknown-not-found"
        else:
            property_verdict = "refused-provider-status"
    custom_property_grant = mint["permissions"].get("organization_custom_properties")
    expected_statuses = {200, 401, 403, 404}
    observed_statuses = [
        page["status"]
        for item in probes
        for page in item["pages"]
    ]
    report = {
        "schema": "fsgg.github-substrate-v2.settings-scope-probe/2",
        "activation": False,
        "source": source_binding(workspace, revision),
        "sandbox": {
            "repository": {"id": REPOSITORY_ID, "nodeId": REPOSITORY_NODE_ID,
                           "fullName": f"{OWNER}/{REPOSITORY}"},
            "project": {"number": PROJECT_NUMBER, "nodeId": PROJECT_NODE_ID,
                        "identitySource": "protected-workflow-constant"},
        },
        "app": mint,
        "account": account,
        "repositoryIdentity": {
            "stable": True,
            "rawStable": repository_raw_stable,
            "revisionStable": repository_revision_stable,
            "changedTopLevelKeys": repository_changed_keys,
            "observationSha256": [sha256(bodies[0]) for bodies in repository_reads],
        },
        "conditionalParents": sorted(environment_parents, key=lambda item: (item["id"], item["nodeId"])),
        "probes": probes,
        "customProperties": {
            "schemaStatus": custom_statuses[0],
            "repositoryValuesStatus": custom_statuses[1],
            "permissionEvidence": {
                "required": "organization_custom_properties:read",
                "observedMintGrant": custom_property_grant,
                "disposition": ("observed-read" if custom_property_grant == "read"
                                else "missing-from-mint-proof"),
            },
            "verdict": property_verdict,
            "refusalReason": refusal_reason(custom_statuses),
            "summary": property_summary,
            "provenanceRule": "equal-default-and-omitted-values-remain-unknown",
        },
        "repositoryRulesets": {
            "listStatus": first_ruleset_status,
            "rulesetCount": len(first_population),
            "detailStatuses": sorted(item["pages"][-1]["status"]
                                     for item in first_details),
            "bypassActorsVisible": first_bypass_visible and second_bypass_visible,
            "permissionEvidence": {
                "metadata": mint["permissions"].get("metadata"),
                "administration": mint["permissions"].get("administration"),
            },
            "privatePlan": plan.get("name") if qualifying_plan else "unknown",
            "verdict": ruleset_verdict,
            "refusalReason": refusal_reason(ruleset_statuses),
        },
        "authority": {
            "installedAppGrant": "unknown",
            "settingsAuthority": "unavailable",
            "qualified": False,
            "reason": "source-only-diagnostic-current-token-read-does-not-install-authority",
        },
        "diagnostic": {
            "collected": True,
            "verdict": "collected",
            "expectedStatusSet": all(status in expected_statuses for status in observed_statuses),
            "canonicalAuthorityQualified": False,
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

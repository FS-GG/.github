#!/usr/bin/env python3
"""Historical candidate preflight for the retained intake protected authority.

Live receipt production is retired. Static validation and historical receipt verification
remain available; this script cannot activate or replay live expected-absence checks.

The historical evaluator checks immutable source bytes, protection and expected ref absence
without accepting native approval, authorizer, environment, credentials or activation.
Its pure fixture controls and blocked receipts remain available; no live CLI producer remains.
"""

from __future__ import annotations

import base64
import hashlib
import json
import os
import pathlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone

ROOT = pathlib.Path(__file__).resolve().parents[1]
POLICY_PATH = ROOT / "policy/protected-intake-preflight.json"
ANCHOR_PATH = ROOT / "policy/protected-intake-authority-candidate.json"
WORKFLOW_PATH = ROOT / ".github/workflows/protected-intake-preflight.yml"
SCRIPT_PATH = ROOT / "tools/protected-intake-preflight.py"
API = "https://api.github.com"
SHA40 = re.compile(r"[0-9a-f]{40}")
MARKER = "<!-- fsgg:review-decision/v2 -->"


class Refusal(RuntimeError):
    pass


def read_json(path: pathlib.Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise Refusal(f"candidate JSON unavailable: {path.relative_to(ROOT)}") from error
    if not isinstance(value, dict):
        raise Refusal(f"candidate JSON is not an object: {path.relative_to(ROOT)}")
    return value


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def static_candidate() -> tuple[dict, dict]:
    policy, anchor = read_json(POLICY_PATH), read_json(ANCHOR_PATH)
    expected = {
        "policySchema": "fsgg.github.protected-intake-preflight-policy/1",
        "anchorSchema": "fsgg.github.protected-intake-authority-candidate/1",
        "candidateForSchema": "fsgg.github.protected-intake-authority/1",
        "policyId": "gs2-09-7-retained-intake-production-v1",
        "operationClass": "retained-intake-journal",
        "aggregateId": "intake-transactions:fs-gg-production",
        "ref": "refs/heads/fsgg/v2/journal/operation/13",
        "operationId": "preinstalled-empty-genesis",
        "repository": "FS-GG/FS.GG.Coordination.Authority",
        "repositoryId": 1351660651,
        "appId": 5064713,
        "installationId": 164553252,
        "workflow": ".github/workflows/protected-intake-preflight.yml",
        "script": "tools/protected-intake-preflight.py",
        "environment": "retained-intake-production",
        "domain": "fsgg.github.protected-intake-authority/v1",
    }
    operation, authority = policy.get("operation", {}), policy.get("authority", {})
    workflow = policy.get("workflow", {})
    preflight = policy.get("preflight", {})
    authorizer, native = policy.get("authorizer", {}), policy.get("nativeApproval", {})
    expected_writer = {
        "id": 21872113,
        "name": "v2-journal-writer",
        "target": "branch",
        "enforcement": "active",
        "updatedAt": "2026-09-24T19:41:44.504000Z",
        "include": ["refs/heads/fsgg/v2/journal/**/*"],
        "exclude": ["refs/heads/fsgg/v2/journal/cutover/d5"],
        "rules": ["creation", "update"],
        "bypassAppIds": [4882140, 5064713],
    }
    expected_integrity = {
        "id": 21872115,
        "name": "v2-journal-integrity",
        "target": "branch",
        "enforcement": "active",
        "updatedAt": "2026-08-30T18:43:34.946000Z",
        "include": ["refs/heads/fsgg/v2/journal/**/*"],
        "exclude": [],
        "rules": ["deletion", "non_fast_forward"],
        "bypassAppIds": [],
    }
    expected_effective = [
        {"type": "creation", "rulesetId": 21872113, "rulesetSourceType": "Repository", "rulesetSource": expected["repository"]},
        {"type": "update", "rulesetId": 21872113, "rulesetSourceType": "Repository", "rulesetSource": expected["repository"]},
        {"type": "deletion", "rulesetId": 21872115, "rulesetSourceType": "Repository", "rulesetSource": expected["repository"]},
        {"type": "non_fast_forward", "rulesetId": 21872115, "rulesetSourceType": "Repository", "rulesetSource": expected["repository"]},
    ]
    expected_genesis = {
        "planSha256": "fb398c6936db857447f648bcef022aa8efd613570fbc0f22d62c494af572fe5f",
        "eventBlobOid": "79d0c2a97c350cb9283eb8ff59b42e25634df275",
        "eventBlobSha256": "842442511d1e5d2a988a0af1b3d4a7ac9bc87b01a15149049122f72c5d1fab69",
        "headBlobOid": "b6a297ee76c55838a6f706fdcb3a6326ed7bc8d3",
        "headBlobSha256": "9076b916d17055f0572dacbb9f400399ca01403f01648b2a4f1c8914db62dbc4",
        "treeOid": "fd004ec56625905eee77a149345a5176733ba5c4",
        "treeSha256": "081bf3fa9ae5384825754734ddcd079a6e2007aef9a7c01ab5313b41ed61ed3c",
        "commitOid": "b51adad418f0c89f7f420a973da403ca1c8fa075",
        "commitSha256": "ba6af79a40a1ecea50c922472d3195321add6fc1a9b8d131f2e03c4005d48b98",
        "parentOid": None,
        "forceWithLease": "--force-with-lease=refs/heads/fsgg/v2/journal/operation/13:",
    }
    if (
        policy.get("schema") != expected["policySchema"]
        or policy.get("policyId") != expected["policyId"]
        or policy.get("status") != "candidate-source-only"
        or policy.get("activation") is not False
        or operation
        != {
            "class": expected["operationClass"],
            "aggregateId": expected["aggregateId"],
            "ref": expected["ref"],
            "intent": "genesis",
            "expectedOldObjectId": None,
            "operationId": expected["operationId"],
        }
        or workflow.get("path") != expected["workflow"]
        or workflow.get("script") != expected["script"]
        or workflow.get("event") != "push"
        or workflow.get("ref") != "refs/heads/main"
        or workflow.get("credentialAccess") is not False
        or workflow.get("mutationAccess") is not False
        or preflight.get("publicRulesetReadback")
        != "bypass-actors-incomplete-without-authorized-read"
        or preflight.get("exactBypassRosterRequiresAuthorizedRead") is not True
        or sorted(workflow.get("manualEventsForbidden", []))
        != ["repository_dispatch", "workflow_dispatch"]
        or sorted(workflow.get("requestEventsForbidden", []))
        != ["pull_request", "pull_request_target"]
        or authority.get("repository") != expected["repository"]
        or authority.get("repositoryId") != expected["repositoryId"]
        or authority.get("writerAppId") != expected["appId"]
        or authority.get("installationId") != expected["installationId"]
        or authority.get("permissions") != {"contents": "write", "metadata": "read"}
        or authority.get("environment") != expected["environment"]
        or authorizer.get("algorithm") != "RSA-PSS-SHA256"
        or authorizer.get("signingDomain") != expected["domain"]
        or authorizer.get("configured") is not False
        or authorizer.get("keyId") is not None
        or authorizer.get("publicKeySpkiSha256") is not None
        or native.get("marker") != MARKER
        or native.get("schema") != "fsgg.coord.review-decision/v2"
        or native.get("requiredKind") != "acceptance"
        or native.get("requiredVerdict") != "accepted"
        or native.get("configured") is not False
    ):
        raise Refusal("intake preflight policy drift")

    if (
        anchor.get("schema") != expected["anchorSchema"]
        or anchor.get("candidateForSchema") != expected["candidateForSchema"]
        or anchor.get("policyId") != expected["policyId"]
        or anchor.get("status") != "candidate-not-accepted"
        or anchor.get("operationClass") != expected["operationClass"]
        or anchor.get("aggregateId") != expected["aggregateId"]
        or anchor.get("ref") != expected["ref"]
        or anchor.get("intent")
        != {"kind": "genesis", "expectedOldObjectId": None, "operationId": expected["operationId"]}
        or anchor.get("nativeAuthorization") is not None
        or anchor.get("sourceCommit") is not None
        or anchor.get("workflowSha256") is not None
        or anchor.get("policySha256") is not None
        or anchor.get("acceptedAt") is not None
        or anchor.get("authorizer", {}).get("installed") is not False
        or anchor.get("writer", {}).get("credentialInstalled") is not False
        or anchor.get("workflow", {}).get("installed") is not False
        or anchor.get("rulesets", {}).get("writer") != expected_writer
        or anchor.get("rulesets", {}).get("integrity") != expected_integrity
        or anchor.get("effectiveRules") != expected_effective
        or anchor.get("genesis") != expected_genesis
        or anchor.get("protectedReadback", {}).get("observedAt") is not None
        or anchor.get("protectedReadback", {}).get("refBefore") is not None
        or anchor.get("protectedReadback", {}).get("refAfter") is not None
        or anchor.get("protectedReadback", {}).get("objects") is not None
    ):
        raise Refusal("intake authority candidate could be mistaken for accepted authority")
    return policy, anchor


def _utc(value: str) -> str:
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    return parsed.isoformat(timespec="microseconds").replace("+00:00", "Z")


def normalize_ruleset(value: dict) -> dict:
    condition = value.get("conditions", {}).get("ref_name", {})
    bypass = value.get("bypass_actors", [])
    return {
        "id": value.get("id"),
        "name": value.get("name"),
        "target": value.get("target"),
        "enforcement": value.get("enforcement"),
        "updatedAt": _utc(value.get("updated_at", "")),
        "include": condition.get("include", []),
        "exclude": condition.get("exclude", []),
        "rules": [rule.get("type") for rule in value.get("rules", [])],
        "bypassAppIds": [actor.get("actor_id") for actor in bypass],
    }


def normalize_effective_rules(value: object) -> list[dict]:
    if not isinstance(value, list):
        raise Refusal("effective rules readback malformed")
    order = {"creation": 0, "update": 1, "deletion": 2, "non_fast_forward": 3}
    normalized = []
    for rule in value:
        if not isinstance(rule, dict) or rule.get("type") not in order:
            raise Refusal("effective rules readback malformed")
        normalized.append(
            {
                "type": rule.get("type"),
                "rulesetId": rule.get("rulesetId", rule.get("ruleset_id")),
                "rulesetSourceType": rule.get(
                    "rulesetSourceType", rule.get("ruleset_source_type")
                ),
                "rulesetSource": rule.get("rulesetSource", rule.get("ruleset_source")),
            }
        )
    return sorted(normalized, key=lambda rule: order[rule["type"]])


def _get(path: str, allow_not_found: bool = False) -> tuple[int, object, dict]:
    request = urllib.request.Request(
        API + path,
        headers={"Accept": "application/vnd.github+json", "User-Agent": "fsgg-protected-intake-preflight/1"},
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.status, json.load(response), dict(response.headers.items())
    except urllib.error.HTTPError as error:
        if allow_not_found and error.code == 404:
            return 404, None, dict(error.headers.items())
        raise Refusal(f"public GitHub read unavailable: {path} ({error.code})") from error
    except (OSError, json.JSONDecodeError) as error:
        raise Refusal(f"public GitHub read unavailable: {path}") from error


def _json(path: str) -> object:
    status, value, _ = _get(path)
    if status != 200:
        raise Refusal(f"unexpected public GitHub status: {path} ({status})")
    return value


def _header(headers: dict, name: str) -> str | None:
    if any(not isinstance(key, str) for key in headers):
        raise Refusal("pagination headers malformed")
    values = [value for key, value in headers.items() if key.lower() == name.lower()]
    if len(values) > 1 or (values and not isinstance(values[0], str)):
        raise Refusal("pagination headers malformed")
    return values[0] if values else None


def _link_relations(headers: dict) -> dict[str, str]:
    raw = _header(headers, "Link")
    if raw is None:
        return {}
    if not raw or any(character in raw for character in ("\\", "\r", "\n")):
        raise Refusal("pagination Link malformed or escaped")
    relations: dict[str, str] = {}
    for part in raw.split(","):
        match = re.fullmatch(
            r'\s*<([^<>\s]+)>\s*;\s*rel="(next|prev|first|last)"\s*', part
        )
        if match is None:
            raise Refusal("pagination Link malformed or escaped")
        url, relation = match.groups()
        if relation in relations:
            raise Refusal("pagination Link has duplicate relation")
        relations[relation] = url
    return relations


def _page_from_link(url: str, endpoint: str) -> int:
    if "%" in url:
        raise Refusal("pagination Link malformed or escaped")
    parsed = urllib.parse.urlsplit(url)
    if (
        parsed.scheme != "https"
        or parsed.netloc != "api.github.com"
        or parsed.path != endpoint
        or parsed.fragment
        or parsed.username is not None
        or parsed.password is not None
    ):
        raise Refusal("pagination Link left the requested endpoint")
    query = urllib.parse.parse_qsl(parsed.query, keep_blank_values=True)
    if len(query) != 2 or {key for key, _ in query} != {"per_page", "page"}:
        raise Refusal("pagination Link query malformed")
    values = dict(query)
    if values["per_page"] != "100" or not re.fullmatch(r"[1-9][0-9]*", values["page"]):
        raise Refusal("pagination Link query malformed")
    return int(values["page"])


def _paged_dicts(endpoint: str, population: str, fetch=_get) -> list[dict]:
    if not endpoint.startswith("/repos/") or "?" in endpoint or "%" in endpoint:
        raise Refusal(f"{population} request identity malformed")
    values: list[dict] = []
    visited: set[int] = set()
    page = 1
    for _ in range(100):
        if page in visited:
            raise Refusal(f"{population} pagination repeated a page")
        visited.add(page)
        status, result, headers = fetch(f"{endpoint}?per_page=100&page={page}")
        if status != 200 or not isinstance(result, list) or not isinstance(headers, dict):
            raise Refusal(f"{population} population malformed")
        if any(not isinstance(item, dict) for item in result):
            raise Refusal(f"{population} population contains a malformed row")
        values.extend(result)
        relations = _link_relations(headers)
        linked_pages = {
            relation: _page_from_link(url, endpoint) for relation, url in relations.items()
        }
        if "first" in linked_pages and linked_pages["first"] != 1:
            raise Refusal(f"{population} pagination first page drift")
        if "prev" in linked_pages and linked_pages["prev"] != page - 1:
            raise Refusal(f"{population} pagination previous page drift")
        if "next" not in linked_pages:
            if "last" in linked_pages and linked_pages["last"] != page:
                raise Refusal(f"{population} terminal page drift")
            return values
        next_page = linked_pages["next"]
        if next_page != page + 1 or next_page in visited:
            raise Refusal(f"{population} pagination repeated or skipped a page")
        if "last" in linked_pages and linked_pages["last"] < next_page:
            raise Refusal(f"{population} pagination last page drift")
        page = next_page
    raise Refusal(f"{population} population exceeded bound")


def collect_live(env: dict[str, str]) -> dict:
    source = env.get("GITHUB_SHA", "")
    if (
        env.get("GITHUB_REPOSITORY") != "FS-GG/.github"
        or env.get("GITHUB_EVENT_NAME") != "push"
        or env.get("GITHUB_REF") != "refs/heads/main"
        or env.get("EXPECTED_WORKFLOW_SHA") != source
        or env.get("GITHUB_WORKFLOW_REF")
        != "FS-GG/.github/.github/workflows/protected-intake-preflight.yml@refs/heads/main"
        or not SHA40.fullmatch(source)
    ):
        raise Refusal("preflight is not the exact protected-main workflow invocation")

    repository = "FS-GG/.github"
    main_first = _json(f"/repos/{repository}/git/ref/heads/main")
    if not isinstance(main_first, dict) or main_first.get("object", {}).get("sha") != source:
        raise Refusal("protected main does not equal the workflow source revision")

    file_hashes = {}
    for candidate in (POLICY_PATH, ANCHOR_PATH, WORKFLOW_PATH, SCRIPT_PATH):
        relative = str(candidate.relative_to(ROOT))
        response = _json(f"/repos/{repository}/contents/{relative}?ref={source}")
        if not isinstance(response, dict) or response.get("encoding") != "base64":
            raise Refusal(f"protected source file readback malformed: {relative}")
        try:
            raw = base64.b64decode("".join(response["content"].split()), validate=True)
        except (KeyError, TypeError, ValueError) as error:
            raise Refusal(f"protected source file readback malformed: {relative}") from error
        if raw != candidate.read_bytes():
            raise Refusal(f"protected source file differs from checkout: {relative}")
        file_hashes[relative] = sha256(raw)

    pulls = _paged_dicts(
        f"/repos/{repository}/commits/{source}/pulls", "native pull request association"
    )
    if len(pulls) != 1:
        raise Refusal("protected source has no unique native pull request")
    pull = pulls[0]
    if pull.get("state") != "closed" or pull.get("merged_at") is None or pull.get("merge_commit_sha") != source:
        raise Refusal("protected source pull request is not the exact native merge")
    number = pull.get("number")
    if not isinstance(number, int) or number <= 0:
        raise Refusal("protected source pull request identity malformed")
    comments = _paged_dicts(
        f"/repos/{repository}/issues/{number}/comments", "native approval comment"
    )
    review_records = []
    for comment in comments:
        body = comment.get("body")
        if isinstance(body, str) and body.startswith(MARKER + "\n"):
            review_records.append({"id": comment.get("id"), "bodySha256": sha256(body.encode("utf-8"))})

    writer = _json("/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/21872113")
    integrity = _json("/repos/FS-GG/FS.GG.Coordination.Authority/rulesets/21872115")
    effective = _json(
        "/repos/FS-GG/FS.GG.Coordination.Authority/rules/branches/"
        + urllib.parse.quote("fsgg/v2/journal/operation/13", safe="")
    )
    status, ref_value, _ = _get(
        "/repos/FS-GG/FS.GG.Coordination.Authority/git/ref/heads/fsgg/v2/journal/operation/13",
        allow_not_found=True,
    )
    main_second = _json(f"/repos/{repository}/git/ref/heads/main")
    if main_second != main_first:
        raise Refusal("protected main moved during preflight")
    return {
        "schema": "fsgg.github.protected-intake-live-evidence/1",
        "sourceSha": source,
        "sourceFiles": file_hashes,
        "pullRequest": {
            "number": number,
            "baseSha": pull.get("base", {}).get("sha"),
            "headSha": pull.get("head", {}).get("sha"),
            "mergeCommitSha": pull.get("merge_commit_sha"),
        },
        "reviewCommentsComplete": True,
        "reviewDecisionRecords": review_records,
        "writerRuleset": normalize_ruleset(writer if isinstance(writer, dict) else {}),
        "integrityRuleset": normalize_ruleset(integrity if isinstance(integrity, dict) else {}),
        # GitHub omits bypass actor identities from an unauthenticated ruleset read.
        # Preserve that epistemic boundary: an empty array cannot prove an empty roster.
        "bypassActorsReadbackComplete": False,
        "effectiveRules": normalize_effective_rules(effective),
        "refBefore": "absent" if status == 404 and ref_value is None else "present-or-unreadable",
    }


def evaluate(evidence: dict) -> dict:
    _, anchor = static_candidate()
    if evidence.get("schema") != "fsgg.github.protected-intake-live-evidence/1":
        raise Refusal("live evidence schema drift")
    source = evidence.get("sourceSha")
    if not isinstance(source, str) or not SHA40.fullmatch(source):
        raise Refusal("live evidence source revision malformed")
    expected_files = {
        str(path.relative_to(ROOT)): sha256(path.read_bytes())
        for path in (POLICY_PATH, ANCHOR_PATH, WORKFLOW_PATH, SCRIPT_PATH)
    }
    if evidence.get("sourceFiles") != expected_files:
        raise Refusal("protected source byte readback drift")
    writer = evidence.get("writerRuleset")
    integrity = evidence.get("integrityRuleset")
    if not isinstance(writer, dict) or {
        key: value for key, value in writer.items() if key != "bypassAppIds"
    } != {
        key: value
        for key, value in anchor["rulesets"]["writer"].items()
        if key != "bypassAppIds"
    }:
        raise Refusal("writer ruleset drift")
    if not isinstance(integrity, dict) or {
        key: value for key, value in integrity.items() if key != "bypassAppIds"
    } != {
        key: value
        for key, value in anchor["rulesets"]["integrity"].items()
        if key != "bypassAppIds"
    }:
        raise Refusal("integrity ruleset drift")
    bypass_complete = evidence.get("bypassActorsReadbackComplete") is True
    if bypass_complete and writer.get("bypassAppIds") != anchor["rulesets"]["writer"]["bypassAppIds"]:
        raise Refusal("writer ruleset drift")
    if bypass_complete and integrity.get("bypassAppIds") != anchor["rulesets"]["integrity"]["bypassAppIds"]:
        raise Refusal("integrity ruleset drift")
    if evidence.get("effectiveRules") != anchor["effectiveRules"]:
        raise Refusal("effective rules drift")
    if evidence.get("refBefore") != "absent":
        raise Refusal("operation/13 is not proven absent")
    if evidence.get("reviewCommentsComplete") is not True:
        raise Refusal("native approval comment readback incomplete")
    pull = evidence.get("pullRequest", {})
    if (
        not isinstance(pull, dict)
        or pull.get("mergeCommitSha") != source
        or not SHA40.fullmatch(str(pull.get("baseSha", "")))
        or not SHA40.fullmatch(str(pull.get("headSha", "")))
        or not isinstance(pull.get("number"), int)
    ):
        raise Refusal("native pull request readback drift")

    blockers = [
        "candidate-anchor-not-accepted",
        "intake-authorizer-not-installed",
        "native-review-decision-v2-not-bound",
        "intake-environment-reviewers-not-installed",
        "intake-app-credential-not-installed",
        "accepted-protected-readback-not-installed",
    ]
    if not bypass_complete:
        blockers.append("exact-bypass-roster-readback-unavailable")
    return {
        "schema": "fsgg.github.protected-intake-preflight-receipt/1",
        "policyId": anchor["policyId"],
        "sourceSha": source,
        "operationRef": anchor["ref"],
        "genesisCommitOid": anchor["genesis"]["commitOid"],
        "visibleProtectionMatchesCandidate": True,
        "exactBypassRosterReadback": bypass_complete,
        "protectionMatchesCandidate": bypass_complete,
        "expectedAbsentObserved": True,
        "nativeReviewRecordCount": len(evidence.get("reviewDecisionRecords", [])),
        "activation": False,
        "blockers": blockers,
    }


def verify_receipt(path: pathlib.Path) -> None:
    receipt = read_json(path)
    expected_blockers = [
        "candidate-anchor-not-accepted",
        "intake-authorizer-not-installed",
        "native-review-decision-v2-not-bound",
        "intake-environment-reviewers-not-installed",
        "intake-app-credential-not-installed",
        "accepted-protected-readback-not-installed",
        "exact-bypass-roster-readback-unavailable",
    ]
    if (
        receipt.get("schema") != "fsgg.github.protected-intake-preflight-receipt/1"
        or receipt.get("policyId") != "gs2-09-7-retained-intake-production-v1"
        or receipt.get("operationRef") != "refs/heads/fsgg/v2/journal/operation/13"
        or receipt.get("genesisCommitOid") != "b51adad418f0c89f7f420a973da403ca1c8fa075"
        or receipt.get("activation") is not False
        or receipt.get("exactBypassRosterReadback") is not False
        or receipt.get("protectionMatchesCandidate") is not False
        or receipt.get("blockers") != expected_blockers
    ):
        raise Refusal("preflight receipt is not the exact blocked candidate")


def main(argv: list[str]) -> int:
    try:
        if argv == ["static"]:
            static_candidate()
        elif len(argv) == 2 and argv[0] == "produce":
            raise Refusal("live candidate preflight retired; use static or verify for historical evidence")
        elif len(argv) == 2 and argv[0] == "verify":
            verify_receipt(pathlib.Path(argv[1]))
        else:
            raise Refusal("usage: protected-intake-preflight.py static|produce FILE|verify FILE")
        return 0
    except (Refusal, KeyError, TypeError, ValueError) as error:
        print(f"protected-intake preflight refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

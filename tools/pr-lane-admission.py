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
from types import SimpleNamespace
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
                previous_head: str, campaign: str, chain: str, *, adopt_legacy: bool = False,
                declare_continues: bool = False, retain_legacy: bool = False) -> str:
    """Preserve native metadata, optionally adding a genuinely absent admission identity."""
    if retain_legacy and (adopt_legacy or declare_continues or "fsgg-pr-lane-admission" in fresh or "fsgg-pr-lane-admission" in proposed):
        raise ToolError("legacy continuation cannot assign or change admission metadata")
    adopting = adopt_legacy and "fsgg-pr-lane-admission" not in fresh
    if adopting and not branch.startswith("routine/"):
        raise ToolError("legacy adoption requires an existing routine PR")
    if not adopting and not retain_legacy and (marker_from_body(fresh) != (campaign, chain)
                         or fresh.count("fsgg-pr-lane-admission") != 1):
        raise ToolError("existing PR admission identity is missing, malformed or conflicts")
    original = machine_markers(fresh)
    desired = dict(original)
    if declare_continues:
        declaration = "<!-- fsgg:delivery-continues/v1 -->"
        keys = ("fsgg:delivery-continues/v1", "fsgg:delivery-completes/v1")
        if fresh.count("fsgg:delivery-") != sum(key in original for key in keys):
            raise ToolError("unrecognized delivery declaration")
        if any(key in original for key in keys):
            if original.get(keys[0]) != declaration or keys[1] in original:
                raise ToolError("existing delivery declaration conflicts")
        elif "fsgg:delivery-" in fresh:
            raise ToolError("unrecognized delivery declaration")
        desired[keys[0]] = declaration
    if adopting:
        desired["fsgg-pr-lane-admission:v1"] = marker(campaign, chain)
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
        if adopting and key == "fsgg-pr-lane-admission:v1":
            allowed.add(desired[key])
        if declare_continues and key == "fsgg:delivery-continues/v1":
            allowed.add(desired[key])
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


# Metadata updates preserve native identity, openness/capability and exact prose. Mergeability,
# rebaseability, synthetic merge commits, activity counts and cache timestamps belong to merge
# qualification, not this action. Repository subresources are projected, never compared wholesale.
_METADATA_FIELDS = {
    "number": "id", "id": "id", "node_id": "text", "state": "state",
    "merged": "bool", "draft": "bool", "locked": "bool", "maintainer_can_modify": "bool",
    "author_association": "association", "html_url": "text", "title": "text", "body": "body",
}
for _prefix in ("user", "head.user", "base.user", "head.repo.owner", "base.repo.owner"):
    _METADATA_FIELDS.update({_prefix + "." + key: kind for key, kind in
                             (("id", "id"), ("node_id", "text"), ("login", "text"), ("type", "text"))})
for _prefix in ("head", "base"):
    _METADATA_FIELDS.update({_prefix + ".ref": "text", _prefix + ".sha": "sha",
                             _prefix + ".repo.id": "id", _prefix + ".repo.node_id": "text",
                             _prefix + ".repo.full_name": "repository"})
_OPTIONAL_METADATA_FIELDS = {}
for _prefix in ("head.repo", "base.repo"):
    _OPTIONAL_METADATA_FIELDS.update({_prefix + "." + key: kind for key, kind in
        (("private", "bool"), ("archived", "bool"), ("disabled", "bool"),
         ("visibility", "visibility"), ("permissions", "permissions"))})
_MISSING = object()
_MALFORMED_PARENT = object()


def metadata_field(value: Any, path: str) -> Any:
    for name in path.split("."):
        if not isinstance(value, dict):
            return _MALFORMED_PARENT
        if name not in value:
            return _MISSING
        value = value[name]
    return value


def metadata_fingerprint(value: Any) -> dict[str, Any]:
    """Only fixed selected fields; malformed containers never expose or digest a native resource."""
    if value is _MISSING:
        return {"kind": "missing"}
    if value is _MALFORMED_PARENT:
        return {"kind": "malformed-parent"}
    kind = {str: "string", bool: "boolean", int: "integer", float: "number",
            list: "array", dict: "object", type(None): "null"}.get(type(value), "unknown")
    permission_flags = (isinstance(value, dict) and set(value) <= {"admin", "maintain", "push", "triage", "pull"}
                        and all(type(flag) is bool for flag in value.values()))
    if isinstance(value, (dict, list)) and not permission_flags:
        return {"kind": kind}
    encoded = json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return {"kind": kind, "sha256": hashlib.sha256(encoded).hexdigest(), "bytes": len(encoded)}


def metadata_difference(field: str, expected: Any, observed: Any) -> dict[str, Any]:
    return {"field": field, "expected": metadata_fingerprint(expected),
            "observed": metadata_fingerprint(observed)}


def metadata_projection(value: Any) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    def valid(kind: str, item: Any) -> bool:
        if kind == "bool":
            return type(item) is bool
        if kind == "id":
            return type(item) is int and item > 0
        if kind == "permissions":
            return (isinstance(item, dict) and set(item) <= {"admin", "maintain", "push", "triage", "pull"}
                    and all(type(flag) is bool for flag in item.values()))
        if not isinstance(item, str):
            return False
        return {"body": lambda: True, "text": lambda: bool(item.strip()),
                "sha": lambda: SHA_RE.fullmatch(item) is not None,
                "repository": lambda: REPO_RE.fullmatch(item) is not None,
                "state": lambda: item in {"open", "closed"},
                "visibility": lambda: item in {"public", "private", "internal"},
                "association": lambda: item in {"COLLABORATOR", "CONTRIBUTOR", "FIRST_TIMER", "FIRST_TIME_CONTRIBUTOR", "MANNEQUIN", "MEMBER", "NONE", "OWNER"}}[kind]()
    projection, problems = {}, []
    for field, kind in {**_METADATA_FIELDS, **_OPTIONAL_METADATA_FIELDS}.items():
        item = metadata_field(value, field)
        if item is _MISSING and field in _OPTIONAL_METADATA_FIELDS:
            projection[field] = item
        elif item is _MISSING or item is _MALFORMED_PARENT or not valid(kind, item):
            problems.append({"field": field, "status": "missing" if item is _MISSING else "malformed",
                             "observed": metadata_fingerprint(item)})
        else:
            projection[field] = item
    return projection, problems


def update_pull(arguments: argparse.Namespace, proposed: str, identity: dict[str, Any]) -> dict[str, Any]:
    identity = {**identity, "number": arguments.update_pr, "headSha": arguments.head_sha}
    endpoint = f"/repos/{arguments.repo}/pulls/{arguments.update_pr}"
    write_attempted = False

    def refuse(reason: str, stage: str, status: str, differences=()) -> None:
        raise Refusal(reason, **identity, stage=stage, writeAttempted=write_attempted,
                      diagnostic={"status": status, "fields": list(differences)})

    def read(stage: str) -> tuple[dict[str, Any], dict[str, Any]]:
        try:
            value = run_gh(arguments.gh, ["--method", "GET", endpoint])
        except ToolError:
            refuse("update-read-unresolved", stage, "read-error")
        guarded, problems = metadata_projection(value)
        if problems:
            refuse("update-candidate-mismatch", stage, "unreadable", problems)
        expected = {"number": arguments.update_pr, "state": "open", "merged": False,
                    "head.ref": arguments.head, "head.sha": arguments.head_sha,
                    "head.repo.full_name": arguments.repo, "base.ref": arguments.base,
                    "base.repo.full_name": arguments.repo,
                    # Repository argument spelling is case-insensitive; native URL spelling is
                    # canonical only after the native base repository passes the scope check below.
                    "html_url": f"https://github.com/{guarded['base.repo.full_name']}/pull/{arguments.update_pr}"}
        differences = [metadata_difference(field, item, guarded[field]) for field, item in expected.items()
                       if (item.lower() != guarded[field].lower() if field.endswith("repo.full_name")
                           else item != guarded[field])]
        if differences:
            refuse("update-candidate-mismatch", stage, "mismatch", differences)
        return value, guarded

    with repository_lock(arguments.repo):
        before, before_guard = read("initial-candidate")
        adopting = arguments.adopt_legacy_pr and "fsgg-pr-lane-admission" not in before["body"]
        try:
            body = update_body(before["body"], proposed, arguments.head, arguments.head_sha,
                               arguments.previous_head_sha, arguments.campaign_id, arguments.dependency_chain_id,
                               adopt_legacy=arguments.adopt_legacy_pr,
                               declare_continues=arguments.declare_delivery_continues)
        except ToolError:
            refuse("update-metadata-conflict", "metadata-preparation", "policy-or-marker-conflict",
                   [metadata_difference("body", before["body"], proposed)])

        def adoption_roster(stage: str) -> list[dict[str, Any]]:
            try:
                pulls = open_pulls(arguments.gh, arguments.repo)
            except ToolError:
                refuse("update-adoption-read-unresolved", stage, "read-error")
            numbers = [pull.get("number") for pull in pulls]
            if (any(type(number) is not int or number <= 0 for number in numbers)
                    or len(set(numbers)) != len(numbers) or numbers.count(arguments.update_pr) != 1):
                refuse("update-adoption-population-mismatch", stage, "unreadable")
            candidate = next(pull for pull in pulls if pull["number"] == arguments.update_pr)
            fields = ("number", "id", "node_id", "state", "title", "body", "head.ref", "head.sha",
                      "head.repo.full_name", "base.ref", "base.repo.full_name")
            differences = [metadata_difference(field, before_guard[field], metadata_field(candidate, field))
                           for field in fields if metadata_field(candidate, field) != before_guard[field]]
            if differences:
                refuse("update-adoption-population-mismatch", stage, "mismatch", differences)
            others = [pull for pull in pulls if pull["number"] != arguments.update_pr]
            # A parsed marker accompanied by damaged/duplicate identity text is never trustworthy.
            for pull in others:
                text = pull.get("body")
                if isinstance(text, str) and "fsgg-pr-lane-admission" in text:
                    if marker_from_body(text) is None or text.count("fsgg-pr-lane-admission") != 1:
                        refuse("managed-reservation-identity-unknown", stage, "policy-or-marker-conflict")
            try:
                enforce_lane(others, arguments.campaign_id, arguments.dependency_chain_id)
            except Refusal as error:
                raise Refusal(error.reason, **error.details, **identity, stage=stage,
                              writeAttempted=False) from error
            return [{field: metadata_field(pull, field) for field in fields}
                    for pull in sorted(pulls, key=lambda pull: pull["number"])]

        initial_roster = adoption_roster("initial-adoption-population") if adopting else None
        # Independently preserve the push-before-update guard, including unreadable branch facts.
        encoded_head = urllib.parse.quote(arguments.head, safe="")
        try:
            branch = run_gh(arguments.gh, ["--method", "GET", f"/repos/{arguments.repo}/git/ref/heads/{encoded_head}"])
        except ToolError:
            refuse("head-read-unresolved", "branch-ref", "read-error")
        observed_sha = metadata_field(branch, "object.sha")
        if observed_sha is _MISSING or observed_sha is _MALFORMED_PARENT or not isinstance(observed_sha, str) or not SHA_RE.fullmatch(observed_sha):
            refuse("head-moved", "branch-ref", "missing" if observed_sha is _MISSING else "malformed",
                   [metadata_difference("branch.sha", arguments.head_sha, observed_sha)])
        if observed_sha != arguments.head_sha:
            refuse("head-moved", "branch-ref", "mismatch",
                   [metadata_difference("branch.sha", arguments.head_sha, observed_sha)])
        if adopting and adoption_roster("prewrite-adoption-population") != initial_roster:
            refuse("update-adoption-population-conflict", "prewrite-adoption-population", "mismatch")
        # No atomic compare-and-swap is available. Re-read immediately before the one possible PATCH.
        _, deciding_guard = read("prewrite-comparison")
        differences = [metadata_difference(field, before_guard[field], item)
                       for field, item in deciding_guard.items() if item != before_guard[field]]
        if differences:
            refuse("update-conflict", "prewrite-comparison", "mismatch", differences)
        unchanged = before["body"] == body and before["title"] == arguments.title
        if not arguments.inspect and not unchanged:
            write_attempted = True
            try:
                run_gh(arguments.gh, ["--method", "PATCH", endpoint, "--input", "-"],
                       stdin=json.dumps({"body": body, "title": arguments.title}, ensure_ascii=False))
            except ToolError:
                # A failed response does not establish whether GitHub applied it. Never retry.
                refuse("update-write-unresolved", "metadata-patch", "write-error")
            try:
                _, after_guard = read("postwrite-readback")
            except Refusal as error:
                raise Refusal("post-write-readback-unresolved", **error.details) from error
            expected_guard = {**before_guard, "body": body, "title": arguments.title}
            differences = [metadata_difference(field, expected_guard[field], item)
                           for field, item in after_guard.items() if item != expected_guard[field]]
            if differences:
                refuse("update-readback-mismatch", "postwrite-readback", "mismatch", differences)
        return {"ok": True, "action": "admitted" if arguments.inspect else "unchanged" if unchanged else "updated",
                **identity, "number": arguments.update_pr, "headSha": arguments.head_sha,
                "url": before["html_url"]}


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
    pulls: list[dict[str, Any]], campaign_id: str, chain_id: str, *, sc2_bootstrap: bool = False
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
    if len(managed_in_repo) >= (3 if sc2_bootstrap else 2):
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


SC2_PREDECESSOR_CHAIN = "sc2-routine-producer-bootstrap-20261017"
SC2_PRESERVED = {
    43: ("routine/sc2-headless-runtime-image-20261014", "bce493a635ec6f0286eeb7b61778a9b773ae764e",
         "unified-roadmap-20261003", "sc2-headless-runtime-image"),
    44: ("routine/sc2-shared-wasm-adapter-source-20261017", "7bee6a9b7d61ff819f0672426f6675cf6c5b3194",
         "outcome-led-programme-20261016", "sc2-shared-wasm-adapter-source-20261017"),
}


def installation_api(gh: str):
    # Import the maintained source predicate, never a candidate validator or PR's code.
    path = pathlib.Path(__file__).with_name("routine-delivery.py")
    spec = importlib.util.spec_from_file_location("lane_source_installation", path)
    module = importlib.util.module_from_spec(spec); assert spec and spec.loader
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    class SourceApi(module.GhApi):
        def installation_read(self, endpoint):
            return run_gh(gh, ["--method", "GET", endpoint])
    return module, SourceApi()


def sc2_predecessor(arguments: argparse.Namespace, pulls: list[dict[str, Any]]) -> None:
    """One selected predecessor, not a caller-selected general PR-cap override."""
    if (arguments.repo != "FS-GG/FS.GG.SC2.Client" or arguments.base != "main"
            or arguments.campaign_id != "outcome-led-programme-20261016"
            or arguments.dependency_chain_id != SC2_PREDECESSOR_CHAIN
            or not arguments.head.startswith("routine/")
            or len(pulls) != 2 or {row.get("number") for row in pulls} != set(SC2_PRESERVED)):
        raise Refusal("sc2-predecessor-reservation-mismatch")
    module, api = installation_api(arguments.gh)
    for pull in pulls:
        number = pull["number"]
        branch, head, campaign, chain = SC2_PRESERVED[number]
        if (pull.get("state") != "open" or metadata_field(pull, "head.ref") != branch
                or metadata_field(pull, "head.sha") != head
                or marker_from_body(pull.get("body")) != (campaign, chain)
                or pull["body"].count("fsgg-pr-lane-admission") != 1):
            raise Refusal("sc2-predecessor-reservation-mismatch")
        native = run_gh(arguments.gh, ["--method", "GET", f"/repos/{arguments.repo}/pulls/{number}"])
        if (not isinstance(native, dict) or native.get("number") != number or native.get("state") != "open"
                or metadata_field(native, "head.sha") != head
                or metadata_field(native, "head.ref") != branch
                or marker_from_body(native.get("body")) != (campaign, chain)
                or metadata_field(native, "head.repo.id") != 1390567199
                or metadata_field(native, "base.repo.id") != 1390567199
                or metadata_field(native, "base.ref") != "main"
                or type(native.get("changed_files")) is not int):
            raise Refusal("sc2-predecessor-preserved-pr-mismatch")
        pages = run_gh(arguments.gh, ["--method", "GET", "--paginate", "--slurp",
            f"/repos/{arguments.repo}/pulls/{number}/files?per_page=100"])
        if not isinstance(pages, list) or any(not isinstance(page, list) for page in pages):
            raise Refusal("sc2-predecessor-preserved-files-unreadable")
        files = [row for page in pages for row in page]
        if len(files) != native["changed_files"] or any(not isinstance(row, dict) for row in files):
            raise Refusal("sc2-predecessor-preserved-files-incomplete")
        paths = [row.get("filename") for row in files]
        if len(paths) != native["changed_files"] or not all(isinstance(path, str) for path in paths) or len(set(paths)) != len(paths):
            raise Refusal("sc2-predecessor-preserved-files-incomplete")
        if set(paths) & set(module.PRODUCER_PATHS):
            raise Refusal("sc2-predecessor-preserved-path-overlap")
    metadata = api.installation_read(f"repos/{arguments.repo}")
    if (not isinstance(metadata, dict) or metadata.get("id") != 1390567199
            or metadata.get("private") is not True):
        raise Refusal("sc2-predecessor-receiver-mismatch")
    default = api.installation_read(f"repos/{arguments.repo}/git/ref/heads/main")
    base = (default.get("object") or {}).get("sha") if isinstance(default, dict) else None
    try:
        api.installation_source(arguments.repo, arguments.head_sha, arguments.base, base,
                                arguments.sc2_producer_predecessor_source)
    except (RuntimeError, KeyError, ValueError, TypeError, AttributeError) as error:
        raise Refusal("sc2-predecessor-source-refused") from error


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


# A known-roster recovery is a separate bounded metadata operation, not a relaxed lane check.
ROSTER_SCHEMA = "fsgg.known-legacy-pr-roster/1"


def text_sha(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def read_known_roster(path: pathlib.Path, repo: str) -> dict[str, Any]:
    def unique(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ToolError("duplicate roster JSON key")
            value[key] = item
        return value
    try:
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 65536:
            raise ToolError("roster must be a regular file of at most 64KiB")
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique)
    except (OSError, ValueError, UnicodeError) as error:
        raise ToolError("unreadable known legacy roster") from error
    if (not isinstance(value, dict) or set(value) != {"schema", "repo", "base", "baseSha", "pulls"}
            or value["schema"] != ROSTER_SCHEMA or value["repo"] != repo
            or value["base"] != "main" or not isinstance(value["baseSha"], str)
            or not re.fullmatch(r"[0-9a-f]{40}", value["baseSha"])
            or not isinstance(value["pulls"], list) or len(value["pulls"]) != 2):
        raise ToolError("known roster requires exactly two explicit PRs on one exact main base")
    fields = {"number", "id", "nodeId", "branch", "headSha", "baseSha", "campaign", "chain", "bodySha256", "titleSha256", "declareContinues"}
    for row in value["pulls"]:
        if (not isinstance(row, dict) or set(row) != fields
                or any(type(row[key]) is not int or row[key] <= 0 for key in ("number", "id"))
                or not isinstance(row["nodeId"], str) or not row["nodeId"]
                or any(not isinstance(row[key], str) or not ID_RE.fullmatch(row[key]) for key in ("campaign", "chain"))
                or not isinstance(row["branch"], str) or not routine_validator().ROUTINE_BRANCH_RE.fullmatch(row["branch"])
                or any(not isinstance(row[key], str) or not re.fullmatch(r"[0-9a-f]{40}", row[key]) for key in ("headSha", "baseSha"))
                or any(not isinstance(row[key], str) or not re.fullmatch(r"[0-9a-f]{64}", row[key]) for key in ("bodySha256", "titleSha256"))
                or type(row["declareContinues"]) is not bool):
            raise ToolError("invalid explicit known-roster PR assignment")
    for key in ("number", "id", "nodeId", "branch", "chain"):
        if len({row[key] for row in value["pulls"]}) != 2:
            raise ToolError("known roster has duplicate native or chain identities")
    if [row["number"] for row in value["pulls"]] != sorted(row["number"] for row in value["pulls"]):
        raise ToolError("known roster PRs must be in increasing number order")
    return value


def roster_custody_root() -> pathlib.Path:
    root = pathlib.Path(os.environ.get("XDG_STATE_HOME", pathlib.Path.home() / ".local/state")) / "fs-gg/pr-lane-known-roster"
    if not root.is_absolute():
        raise ToolError("known-roster custody requires an absolute state directory")
    root.mkdir(mode=0o700, parents=True, exist_ok=True)
    info = root.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077:
        raise ToolError("unsafe known-roster custody directory")
    return root


def save_roster_custody(path: pathlib.Path, value: dict[str, Any], *, create: bool = False) -> None:
    """Fsync intent/possible-send before any provider effect; keep raw PR text out of custody."""
    raw = (json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n").encode()
    flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW
    if create:
        descriptor = os.open(path, flags, 0o600)
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    else:
        descriptor, temp = tempfile.mkstemp(prefix=".known-roster-", dir=path.parent)
        try:
            with os.fdopen(descriptor, "wb") as stream:
                stream.write(raw); stream.flush(); os.fsync(stream.fileno())
            os.replace(temp, path)
        finally:
            with contextlib.suppress(FileNotFoundError):
                os.unlink(temp)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def adopt_known_roster(arguments: argparse.Namespace) -> dict[str, Any]:
    spec = read_known_roster(arguments.adopt_known_roster, arguments.repo)
    raw_spec = json.dumps(spec, sort_keys=True, separators=(",", ":"))
    intent_sha = text_sha(raw_spec)
    # Stable across caller files, spec edits and process restarts. A changed plan cannot reset an uncertain send.
    key = text_sha(arguments.repo.lower() + ":" + ",".join(str(row["id"]) for row in spec["pulls"]))
    identity = {"repo": arguments.repo, "intentSha256": intent_sha}
    expected: dict[int, dict[str, Any]] = {}
    guards: dict[int, dict[str, Any]] = {}
    desired: dict[int, str] = {}
    custody: dict[str, Any] = {"schema": ROSTER_SCHEMA, **identity, "intent": spec, "phase": "validated", "steps": []}
    def refuse(reason, **details):
        raise Refusal(reason, **identity, steps=custody["steps"], **details)
    def get(endpoint):
        return run_gh(arguments.gh, ["--method", "GET", endpoint])
    def projection(native):
        guard, problems = metadata_projection(native)
        if problems:
            refuse("known-roster-native-unreadable")
        return guard
    def guard_sha(guard):
        # Bind full deciding metadata without retaining native prose or repository subresources.
        return text_sha(json.dumps({field: metadata_fingerprint(item) for field, item in guard.items()},
                                   sort_keys=True, separators=(",", ":")))
    def fresh_population(initial=False):
        pulls = open_pulls(arguments.gh, arguments.repo)
        numbers = [row.get("number") for row in pulls]
        if (len(pulls) != 2 or any(type(number) is not int for number in numbers)
                or set(numbers) != {row["number"] for row in spec["pulls"]} or len(set(numbers)) != 2):
            refuse("known-roster-population-mismatch")
        base = current_head_sha(arguments.gh, arguments.repo, spec["base"])
        if base != spec["baseSha"]:
            refuse("known-roster-base-moved")
        for row in spec["pulls"]:
            number = row["number"]
            native = get(f"/repos/{arguments.repo}/pulls/{number}")
            guard = projection(native)
            roster_entry = next(pull for pull in pulls if pull["number"] == number)
            # GitHub's list response omits merged/maintainer_can_modify. Compare its
            # identity/preimage fields, then retain the full detail response as the guard.
            population_fields = ("number", "id", "node_id", "state", "title", "body", "head.ref", "head.sha",
                                 "head.repo.full_name", "base.ref", "base.sha", "base.repo.full_name")
            if any(metadata_field(roster_entry, field) != guard[field] for field in population_fields):
                refuse("known-roster-population-disagrees", number=number)
            if current_head_sha(arguments.gh, arguments.repo, row["branch"]) != row["headSha"]:
                refuse("known-roster-head-moved", number=number)
            if initial:
                wanted = {"number": number, "id": row["id"], "node_id": row["nodeId"], "state": "open", "merged": False,
                          "head.ref": row["branch"], "head.sha": row["headSha"], "base.ref": spec["base"],
                          "base.sha": row["baseSha"], "head.repo.full_name": arguments.repo, "base.repo.full_name": arguments.repo,
                          "html_url": f"https://github.com/{arguments.repo}/pull/{number}"}
                if (any(guard[field] != value for field, value in wanted.items())
                        or text_sha(native["title"]) != row["titleSha256"] or text_sha(native["body"]) != row["bodySha256"]
                        or "fsgg-pr-lane-admission" in native["body"]):
                    refuse("known-roster-preimage-mismatch", number=number)
                validate_routine_body(native["body"], row["branch"], row["headSha"])
                desired[number] = update_body(native["body"], native["body"], row["branch"], row["headSha"], row["headSha"],
                                              row["campaign"], row["chain"], adopt_legacy=True,
                                              declare_continues=row["declareContinues"])
                expected[number], guards[number] = native, guard
            elif guard != guards[number]:
                refuse("known-roster-preimage-drift", number=number)
        # Reuse the normal policy over the fully assigned hypothetical population. No capacity exception.
        assigned = [{**expected[row["number"]], "body": desired[row["number"]]} for row in spec["pulls"]]
        for row in spec["pulls"]:
            enforce_lane([pull for pull in assigned if pull["number"] != row["number"]], row["campaign"], row["chain"])
    with repository_lock(arguments.repo):
        root = roster_custody_root()
        path = root / (key + ".json")
        if path.exists() or path.is_symlink():
            # Re-entry never sends, even after a failed acknowledgement or a partial cohort.
            info = path.lstat()
            if not stat.S_ISREG(info.st_mode) or info.st_uid != os.getuid() or stat.S_IMODE(info.st_mode) & 0o077:
                raise ToolError("unsafe known-roster custody file")
            try:
                prior = json.loads(path.read_text())
                if not isinstance(prior, dict) or prior.get("schema") != ROSTER_SCHEMA:
                    raise ValueError("custody schema")
            except (ValueError, OSError) as error:
                raise ToolError("unreadable prior custody; no resend") from error
            observations = []
            for row in spec["pulls"]:
                native = get(f"/repos/{arguments.repo}/pulls/{row['number']}")
                observations.append({"number": row["number"], "headSha": metadata_field(native, "head.sha"),
                                     "bodySha256": text_sha(native["body"]) if isinstance(native.get("body"), str) else None})
            raise Refusal("known-roster-custody-exists-no-resend", **identity,
                          priorIntentSha256=prior.get("intentSha256"), phase=prior.get("phase"), steps=prior.get("steps"),
                          readback=observations, writeAttempted=False)
        fresh_population(initial=True)
        fresh_population()
        if arguments.inspect:
            return {"ok": True, "action": "admitted", **identity, "numbers": [row["number"] for row in spec["pulls"]]}
        custody["plannedBodySha256"] = {str(number): text_sha(body) for number, body in desired.items()}
        save_roster_custody(path, custody, create=True)
        for row in spec["pulls"]:
            number = row["number"]
            fresh_population()
            custody["phase"] = "possible-send"
            step = {"number": number, "headSha": row["headSha"], "beforeBodySha256": row["bodySha256"],
                    "afterBodySha256": text_sha(desired[number]), "beforeMetadataSha256": guard_sha(guards[number]),
                    "expectedMetadataSha256": guard_sha({**guards[number], "body": desired[number]}), "phase": "possible-send"}
            custody["steps"].append(step)
            save_roster_custody(path, custody)
            try:
                run_gh(arguments.gh, ["--method", "PATCH", f"/repos/{arguments.repo}/pulls/{number}", "--input", "-"],
                       stdin=json.dumps({"body": desired[number]}, ensure_ascii=False))
            except ToolError:
                refuse("known-roster-send-uncertain", number=number, writeAttempted=True)
            try:
                after = projection(get(f"/repos/{arguments.repo}/pulls/{number}"))
            except (ToolError, Refusal):
                refuse("known-roster-readback-unresolved", number=number, writeAttempted=True)
            if after != {**guards[number], "body": desired[number]}:
                refuse("known-roster-readback-mismatch", number=number, writeAttempted=True)
            guards[number] = after
            step["readbackMetadataSha256"] = guard_sha(after)
            step["phase"] = "confirmed"
            custody["phase"] = "partial-confirmed"
            save_roster_custody(path, custody)
        fresh_population()
        custody["phase"] = "complete"
        save_roster_custody(path, custody)
        return {"ok": True, "action": "adopted-known-roster", **identity, "steps": custody["steps"]}


def parser() -> argparse.ArgumentParser:
    result = JsonArgumentParser(description=__doc__)
    result.add_argument("--repo", required=True, help="GitHub owner/repository")
    result.add_argument("--campaign-id")
    result.add_argument("--dependency-chain-id")
    result.add_argument("--head", help="Head branch in the target repository")
    result.add_argument("--head-sha", help="Exact expected head commit")
    result.add_argument("--base")
    result.add_argument("--title")
    result.add_argument("--body-file", type=pathlib.Path)
    result.add_argument("--update-pr", type=int, help="Update an existing managed PR, preserving native machine metadata")
    result.add_argument("--previous-head-sha", help="Explicit previous routine marker head for an update")
    result.add_argument("--adopt-legacy-pr", action="store_true",
                        help="Explicitly assign the supplied campaign/chain to an existing routine PR with absent admission metadata")
    result.add_argument("--sc2-producer-predecessor-source", help="Protected canonical producer pin for the selected SC2 first-install predecessor")
    result.add_argument("--recover-legacy-update", type=pathlib.Path, help="Explicit already-owned legacy PR metadata continuation plan")
    result.add_argument("--adopt-known-roster", type=pathlib.Path, help="Explicit exact two-legacy-PR roster; separate guarded metadata operation")
    result.add_argument("--declare-delivery-continues", action="store_true", help="Explicitly initialize a missing continues declaration during an existing PR update")
    result.add_argument("--inspect", action="store_true", help="Check admission without creating a PR")
    result.add_argument("--gh", default="gh", help=argparse.SUPPRESS)
    return result


def validate(arguments: argparse.Namespace) -> None:
    if not REPO_RE.fullmatch(arguments.repo):
        raise ToolError("repo must have owner/repository form")
    if arguments.recover_legacy_update:
        if (any(getattr(arguments, key) is not None for key in ("campaign_id", "dependency_chain_id", "head", "head_sha", "base", "update_pr", "previous_head_sha", "sc2_producer_predecessor_source", "adopt_known_roster"))
                or arguments.adopt_legacy_pr or arguments.declare_delivery_continues
                or arguments.body_file is None or not isinstance(arguments.title, str)
                or not arguments.title.strip() or "\n" in arguments.title or "\r" in arguments.title):
            raise ToolError("legacy continuation requires only a plan, title and body")
        return
    if arguments.adopt_known_roster:
        if any(getattr(arguments, key) is not None for key in ("campaign_id", "dependency_chain_id", "head", "head_sha", "base", "title", "body_file", "update_pr", "previous_head_sha", "sc2_producer_predecessor_source")) or arguments.adopt_legacy_pr or arguments.declare_delivery_continues:
            raise ToolError("known-roster mode cannot mix ordinary PR arguments")
        return
    if any(getattr(arguments, key) is None for key in ("campaign_id", "dependency_chain_id", "head", "head_sha", "base", "title", "body_file")):
        raise ToolError("ordinary mode requires campaign, chain, branch, head, base, title and body")
    if arguments.declare_delivery_continues and arguments.update_pr is None:
        raise ToolError("initial delivery declaration requires an existing PR update")
    if arguments.adopt_legacy_pr and arguments.update_pr is None:
        raise ToolError("legacy PR adoption requires --update-pr")
    if arguments.sc2_producer_predecessor_source and (arguments.update_pr is not None
            or not re.fullmatch(r"[0-9a-f]{40}", arguments.sc2_producer_predecessor_source)):
        raise ToolError("SC2 predecessor requires an exact producer pin and a new PR")
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
    if arguments.recover_legacy_update:
        path = pathlib.Path(__file__).with_name("legacy-pr-update.py")
        spec = importlib.util.spec_from_file_location("legacy_pr_update", path)
        module = importlib.util.module_from_spec(spec)
        assert spec and spec.loader
        spec.loader.exec_module(module)
        try:
            return module.execute(SimpleNamespace(**globals()), arguments)
        except OSError as error:
            raise ToolError("legacy continuation I/O failed; retain custody and do not resend") from error
    if arguments.adopt_known_roster:
        try:
            return adopt_known_roster(arguments)
        except OSError as error:
            raise ToolError("known-roster I/O failed; retain custody and do not resend") from error
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
        bootstrap = bool(arguments.sc2_producer_predecessor_source)
        if bootstrap:
            gate = routine_validator()
            if gate.marker_fields(gate.ROUTINE_MARKER_RE, managed_body)[0].get("operation") != "source-change":
                raise Refusal("sc2-predecessor-operation-mismatch", **identity)
            sc2_predecessor(arguments, pulls)
        try:
            open_count = enforce_lane(
                pulls, arguments.campaign_id, arguments.dependency_chain_id, sc2_bootstrap=bootstrap
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
        if bootstrap:
            common["selectedException"] = "sc2-exact-five-file-predecessor"
            managed_body += "\nSelected SC2 first-install predecessor; preserved PR43/44 remain owned and unchanged.\n"
        if arguments.inspect:
            return {"ok": True, "action": "admitted", **common}
        if bootstrap:
            # Recheck complete reservations and source immediately before the one possible create.
            deciding = open_pulls(arguments.gh, arguments.repo)
            # The closed predicate rechecks relevant identities; volatile REST fields are not a lock.
            sc2_predecessor(arguments, deciding)
            if current_head_sha(arguments.gh, arguments.repo, arguments.head) != arguments.head_sha:
                raise Refusal("head-moved", **common)

        payload = json.dumps(
            {
                "title": arguments.title,
                "head": arguments.head,
                "base": arguments.base,
                "body": managed_body,
            },
            ensure_ascii=False,
        )
        try:
            created = run_gh(
                arguments.gh,
                ["--method", "POST", f"/repos/{arguments.repo}/pulls", "--input", "-"],
                stdin=payload,
            )
        except ToolError as error:
            if bootstrap:
                raise Refusal("sc2-predecessor-create-unresolved", **common, writeAttempted=True) from error
            raise
        if not isinstance(created, dict) or type(created.get("number")) is not int or created["number"] <= 0:
            if bootstrap:
                raise Refusal("sc2-predecessor-create-unresolved", **common, writeAttempted=True)
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

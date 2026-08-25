#!/usr/bin/env python3
"""Fail-closed, source-bound controls for the GS2-00 Q0 evidence."""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[2]
SHA256 = re.compile(r"^[0-9a-f]{64}$")
GIT_SHA = re.compile(r"^[0-9a-f]{40}$")
TREE_INVENTORIES = {
    "workflows": ".github/workflows", "scripts": "scripts", "core": "src/FS.GG.Coord.Core",
    "github": "src/FS.GG.Coord.GitHub", "cli": "src/FS.GG.Coord.Cli",
    "lifecycle": "src/FS.GG.Coord.Cli.Lifecycle", "boardOps": "src/FS.GG.Coord.Cli.BoardOps",
    "registry": "registry", "skills": ".agents/skills",
}
COMMAND_INVENTORIES = {
    "protocolFacts": ["scripts/fsgg-coord", "facts", "--json"],
    "commandContract": ["scripts/fsgg-coord", "command-contract", "--json"],
}
REQUIRED_AUTHORITY = {
    "issueBody", "comment", "project", "registry", "workflow", "command", "jsonContract",
    "environment", "file", "package", "schedule", "setting", "external",
}
REQUIRED_MUTATION = {"command", "workflow", "release", "repairScript", "administrative", "appRoute"}
REQUIRED_CORPUS = {
    "claim", "touchSet", "dependency", "hierarchy", "intake", "review", "delivery", "merge",
    "release", "pagination", "rateLimit", "partialWrite", "staleRead", "selfHosting", "churn72h",
    "mutationEntry", "protocolString", "replay", "omission", "misclassification", "byteCompatibility",
}
REQUIRED_CLASSIFICATIONS = {"Preserve", "Migrate", "Seal", "Retire"}
REQUIRED_HANDOFF = {
    "v2-unit", "v2-blocker", "parallel-product", "candidate-input-change",
    "superseded-inventory", "cutover-deferred",
}
REQUIRED_THREAT_BOUNDARIES = {
    "protectedEpoch", "administrativePrincipal", "githubMutableState",
    "packageSupplyChain", "crossRepositoryReceiver",
}
REQUIRED_REVIEW_ROLES = {"architecture", "security", "operations", "crossRepository"}
REVIEW_EVIDENCE = re.compile(r"^https://github\.com/FS-GG/\.github/pull/3001#issuecomment-[1-9][0-9]*$")


def digest_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def canonical_digest(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    return digest_bytes(encoded)


def run_bytes(command: list[str]) -> bytes:
    return subprocess.run(command, cwd=ROOT, check=True, stdout=subprocess.PIPE).stdout


def git_paths(commit: str, root: str) -> list[str]:
    return run_bytes(["git", "ls-tree", "-r", "--name-only", commit, "--", root]).decode().splitlines()


def review_subject(data: dict[str, Any]) -> dict[str, Any]:
    """The exact non-circular Q0 material every role signs."""
    return {
        key: data[key]
        for key in (
            "sourceBase", "inventories", "authorities", "mutations", "corpus",
            "compatibilityDeletion", "handoff", "runtimeDecision", "liveProjection", "threatModel",
            "adminSettingsReport",
        )
    }


def ids_unique(rows: list[dict[str, Any]], label: str, errors: list[str]) -> None:
    ids = [row.get("id") for row in rows]
    if None in ids or len(ids) != len(set(ids)):
        errors.append(f"{label}: missing or duplicate id")


def require_fields(rows: list[dict[str, Any]], fields: set[str], label: str, errors: list[str]) -> None:
    for index, row in enumerate(rows):
        missing = sorted(field for field in fields if not row.get(field))
        if missing:
            errors.append(f"{label}[{index}]: missing {','.join(missing)}")


def validate(data: dict[str, Any], acceptance: bool = False) -> list[str]:
    errors: list[str] = []
    if data.get("schema") != "fsgg.github-substrate.q0-evidence/v1":
        errors.append("schema: unsupported")

    source_base = data.get("sourceBase", "")
    if not GIT_SHA.fullmatch(source_base):
        errors.append("sourceBase: expected exact lowercase 40-hex git object")
    else:
        try:
            run_bytes(["git", "cat-file", "-e", f"{source_base}^{{commit}}"])
        except subprocess.CalledProcessError:
            errors.append("sourceBase: commit is unavailable")

    inventories = data.get("inventories", [])
    ids_unique(inventories, "inventories", errors)
    require_fields(inventories, {"id", "root", "count", "pathListSha256"}, "inventories", errors)
    by_id = {row.get("id"): row for row in inventories}
    expected_inventory_ids = set(TREE_INVENTORIES) | set(COMMAND_INVENTORIES)
    if set(by_id) != expected_inventory_ids:
        errors.append(f"inventories: id mismatch missing={sorted(expected_inventory_ids-set(by_id))} extra={sorted(set(by_id)-expected_inventory_ids)}")
    if GIT_SHA.fullmatch(source_base):
        for inventory_id, root in TREE_INVENTORIES.items():
            if inventory_id not in by_id:
                continue
            paths = git_paths(source_base, root)
            expected_digest = digest_bytes(("\n".join(paths) + "\n").encode())
            row = by_id[inventory_id]
            if row.get("root") != root or row.get("count") != len(paths) or row.get("pathListSha256") != expected_digest:
                errors.append(f"inventories[{inventory_id}]: does not match independently enumerated sourceBase tree")
    for inventory_id, command in COMMAND_INVENTORIES.items():
        if inventory_id not in by_id:
            continue
        try:
            output = run_bytes(command)
            parsed = json.loads(output)
            count = 1 if inventory_id == "protocolFacts" else len(parsed.get("commands", []))
            row = by_id[inventory_id]
            if row.get("root") != " ".join(command) or row.get("count") != count or row.get("pathListSha256") != digest_bytes(output):
                errors.append(f"inventories[{inventory_id}]: does not match independently executed command output")
        except (subprocess.CalledProcessError, json.JSONDecodeError) as error:
            errors.append(f"inventories[{inventory_id}]: derivation failed: {error}")
    for row in inventories:
        if not SHA256.fullmatch(str(row.get("pathListSha256", ""))):
            errors.append(f"inventories[{row.get('id')}]: invalid SHA-256")

    authorities = data.get("authorities", [])
    ids_unique(authorities, "authorities", errors)
    require_fields(authorities, {"id", "category", "subject", "authority", "revision", "completeness", "owner", "disposition", "unit"}, "authorities", errors)
    present_authority = {row.get("category") for row in authorities}
    if present_authority != REQUIRED_AUTHORITY:
        errors.append(f"authorities: category mismatch missing={sorted(REQUIRED_AUTHORITY-present_authority)} extra={sorted(present_authority-REQUIRED_AUTHORITY)}")
    invalid_dispositions = {row.get("disposition") for row in authorities} - REQUIRED_CLASSIFICATIONS
    if invalid_dispositions:
        errors.append(f"authorities: unsupported dispositions {sorted(invalid_dispositions)}")

    mutations = data.get("mutations", [])
    ids_unique(mutations, "mutations", errors)
    require_fields(mutations, {"id", "class", "routes", "endpoint", "precondition", "permission", "v2", "unit"}, "mutations", errors)
    mutation_classes = {row.get("class") for row in mutations}
    if mutation_classes != REQUIRED_MUTATION:
        errors.append(f"mutations: class mismatch missing={sorted(REQUIRED_MUTATION-mutation_classes)} extra={sorted(mutation_classes-REQUIRED_MUTATION)}")
    if any(not isinstance(row.get("routes"), list) or not row["routes"] for row in mutations):
        errors.append("mutations: empty or invalid route coverage")

    corpus = data.get("corpus", [])
    ids_unique(corpus, "corpus", errors)
    require_fields(corpus, {"id", "kind", "source", "expected"}, "corpus", errors)
    corpus_kinds = {row.get("kind") for row in corpus}
    if corpus_kinds != REQUIRED_CORPUS:
        errors.append(f"corpus: kind mismatch missing={sorted(REQUIRED_CORPUS-corpus_kinds)} extra={sorted(corpus_kinds-REQUIRED_CORPUS)}")

    deletion = data.get("compatibilityDeletion", [])
    ids_unique(deletion, "compatibilityDeletion", errors)
    require_fields(deletion, {"id", "surface", "classification", "deleteUnit", "absenceTest"}, "compatibilityDeletion", errors)
    classifications = {row.get("classification") for row in deletion}
    if classifications != REQUIRED_CLASSIFICATIONS:
        errors.append(f"compatibilityDeletion: classification mismatch missing={sorted(REQUIRED_CLASSIFICATIONS-classifications)} extra={sorted(classifications-REQUIRED_CLASSIFICATIONS)}")

    handoff = data.get("handoff", [])
    require_fields(handoff, {"ref", "classification", "decision"}, "handoff", errors)
    if len({row.get("ref") for row in handoff}) != len(handoff):
        errors.append("handoff: duplicate ref")
    handoff_classes = {row.get("classification") for row in handoff}
    if not handoff_classes <= REQUIRED_HANDOFF:
        errors.append(f"handoff: unsupported classifications {sorted(handoff_classes-REQUIRED_HANDOFF)}")
    if not {"v2-blocker", "parallel-product", "candidate-input-change", "superseded-inventory", "cutover-deferred"} <= handoff_classes:
        errors.append("handoff: required disposition class absent")

    runtime = data.get("runtimeDecision", {})
    require_fields([runtime], {"posture", "hostedBoundary", "owner", "availability", "secrets", "ingress", "observability", "upgrades", "incidentResponse", "retention", "cost", "disasterRecovery"}, "runtimeDecision", errors)
    if runtime.get("posture") != "scheduled-audit-authoritative" or runtime.get("hostedBoundary") != "rejected-for-cutover":
        errors.append("runtimeDecision: unratified cutover posture")

    threat = data.get("threatModel", {})
    require_fields([threat], {"path", "sha256", "boundaries"}, "threatModel", errors)
    threat_path = ROOT / str(threat.get("path", ""))
    if not threat_path.is_file():
        errors.append("threatModel: source file missing")
    elif threat.get("sha256") != digest_bytes(threat_path.read_bytes()):
        errors.append("threatModel: digest does not match source bytes")
    if not SHA256.fullmatch(str(threat.get("sha256", ""))):
        errors.append("threatModel: invalid SHA-256")
    if set(threat.get("boundaries", [])) != REQUIRED_THREAT_BOUNDARIES:
        errors.append("threatModel: protected-boundary census mismatch")

    admin_report = data.get("adminSettingsReport", {})
    require_fields([admin_report], {"path", "sha256"}, "adminSettingsReport", errors)
    admin_report_path = ROOT / str(admin_report.get("path", ""))
    if not admin_report_path.is_file():
        errors.append("adminSettingsReport: source file missing")
    elif admin_report.get("sha256") != digest_bytes(admin_report_path.read_bytes()):
        errors.append("adminSettingsReport: digest does not match source bytes")
    if not SHA256.fullmatch(str(admin_report.get("sha256", ""))):
        errors.append("adminSettingsReport: invalid SHA-256")

    projection = data.get("liveProjection", {})
    if set(projection.get("blockedBy", {})) != {"FS-GG/.github#2963", "FS-GG/.github#2964", "FS-GG/.github#2965"}:
        errors.append("liveProjection: program dependency census incomplete")
    expected_children = {f"FS-GG/.github#{number}" for number in range(2954, 2966)}
    if set(projection.get("childBoundaryCitations", [])) != expected_children:
        errors.append("liveProjection: ADR/rollback child citation census incomplete")
    bootstrap = projection.get("bootstrapRepository", {})
    if bootstrap.get("ref") != "FS-GG/FS.GG.Coordination" or bootstrap.get("visibility") != "PUBLIC" or bootstrap.get("defaultBranch") != "main" or bootstrap.get("state") != "inert" or not GIT_SHA.fullmatch(str(bootstrap.get("initialHead", ""))):
        errors.append("liveProjection: early bootstrap repository receipt is incomplete or active")
    if projection.get("pendingBoardWrites") != 0:
        errors.append("liveProjection: pending board writes")

    try:
        expected_review_fingerprint = canonical_digest(review_subject(data))
    except KeyError as error:
        errors.append(f"reviewFingerprint: missing subject {error.args[0]}")
        expected_review_fingerprint = ""
    if not SHA256.fullmatch(str(data.get("reviewFingerprint", ""))) or data.get("reviewFingerprint") != expected_review_fingerprint:
        errors.append("reviewFingerprint: does not bind the canonical Q0 subject")

    required_reviews = set(data.get("reviewsRequired", []))
    if required_reviews != REQUIRED_REVIEW_ROLES or len(data.get("reviewsRequired", [])) != len(REQUIRED_REVIEW_ROLES):
        errors.append("reviewsRequired: exact independent role policy is mandatory")

    if acceptance:
        reviews = data.get("reviews", [])
        require_fields(reviews, {"role", "verdict", "fingerprint", "evidenceRef", "reviewer"}, "reviews", errors)
        accepted = {row.get("role") for row in reviews if row.get("verdict") == "accepted"}
        if required_reviews != accepted or len(reviews) != len(required_reviews):
            errors.append(f"reviews: acceptance mismatch missing={sorted(required_reviews-accepted)} extra={sorted(accepted-required_reviews)}")
        for row in reviews:
            if row.get("fingerprint") != expected_review_fingerprint or not SHA256.fullmatch(str(row.get("fingerprint", ""))):
                errors.append(f"reviews[{row.get('role')}]: fingerprint does not bind the canonical Q0 subject")
        reviewers = [row.get("reviewer") for row in reviews]
        if len(set(reviewers)) != len(REQUIRED_REVIEW_ROLES):
            errors.append("reviews: every role requires a distinct independent reviewer")
        evidence_refs = [row.get("evidenceRef") for row in reviews]
        if len(set(evidence_refs)) != len(REQUIRED_REVIEW_ROLES) or any(not REVIEW_EVIDENCE.fullmatch(str(ref)) for ref in evidence_refs):
            errors.append("reviews: every role requires a distinct canonical PR-comment evidence URL")
    return errors


def self_test(data: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    candidate_mutations = [
        ("inventory digest", lambda d: d["inventories"][0].__setitem__("pathListSha256", "not-a-digest")),
        ("unknown writer", lambda d: d["mutations"].append({**d["mutations"][0], "id": "M-unknown", "class": "unknownWriter"})),
        ("corpus subject", lambda d: d["corpus"][0].__setitem__("expected", "weakened")),
        ("deletion unit", lambda d: d["compatibilityDeletion"][0].__setitem__("deleteUnit", "GS2-never")),
        ("threat source", lambda d: d["threatModel"].__setitem__("sha256", "0" * 64)),
        ("administrator report", lambda d: d["adminSettingsReport"].__setitem__("sha256", "0" * 64)),
        ("pending projection", lambda d: d["liveProjection"].__setitem__("pendingBoardWrites", 1)),
    ]
    for name, mutate in candidate_mutations:
        candidate = copy.deepcopy(data)
        mutate(candidate)
        if not validate(candidate):
            failures.append(f"mutation survived: {name}")

    acceptance = copy.deepcopy(data)
    acceptance["reviews"] = [
        {"role": role, "verdict": "accepted", "fingerprint": "x", "evidenceRef": "https://example.invalid/review", "reviewer": role}
        for role in acceptance["reviewsRequired"]
    ]
    if not validate(acceptance, acceptance=True):
        failures.append("mutation survived: arbitrary reviewer fingerprint")
    erased = copy.deepcopy(data)
    erased["reviewsRequired"] = []
    erased["reviews"] = []
    if not validate(erased, acceptance=True):
        failures.append("mutation survived: erased review policy")
    forged = copy.deepcopy(data)
    forged["reviews"] = [
        {"role": role, "verdict": "accepted", "fingerprint": data["reviewFingerprint"],
         "evidenceRef": "https://example.invalid/review", "reviewer": "same-reviewer"}
        for role in forged["reviewsRequired"]
    ]
    if not validate(forged, acceptance=True):
        failures.append("mutation survived: forged or non-independent role reviews")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--acceptance", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    data = json.loads(args.evidence.read_text(encoding="utf-8"))
    errors = validate(data, args.acceptance)
    if args.self_test:
        errors.extend(self_test(data))
    if errors:
        for error in errors:
            print(f"Q0-RED: {error}")
        return 1
    mode = "acceptance" if args.acceptance else "candidate"
    print(f"Q0-GREEN: {mode}; authorities={len(data['authorities'])}; mutations={len(data['mutations'])}; corpus={len(data['corpus'])}; deletions={len(data['compatibilityDeletion'])}; controls={'on' if args.self_test else 'off'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

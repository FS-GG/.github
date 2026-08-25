#!/usr/bin/env python3
"""Fail-closed structural and omission controls for the GS2-00 Q0 evidence."""

from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
from typing import Any


REQUIRED_AUTHORITY = {
    "issueBody", "comment", "project", "registry", "workflow", "command",
    "jsonContract", "environment", "file", "package", "schedule", "setting", "external",
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

    inventories = data.get("inventories", [])
    ids_unique(inventories, "inventories", errors)
    require_fields(inventories, {"id", "root", "count", "pathListSha256"}, "inventories", errors)
    if not inventories or any(not isinstance(row.get("count"), int) or row["count"] <= 0 for row in inventories):
        errors.append("inventories: empty or non-positive census")

    authorities = data.get("authorities", [])
    ids_unique(authorities, "authorities", errors)
    require_fields(authorities, {"id", "category", "subject", "authority", "revision", "completeness", "owner", "disposition", "unit"}, "authorities", errors)
    present_authority = {row.get("category") for row in authorities}
    if present_authority != REQUIRED_AUTHORITY:
        errors.append(f"authorities: category mismatch missing={sorted(REQUIRED_AUTHORITY-present_authority)} extra={sorted(present_authority-REQUIRED_AUTHORITY)}")

    mutations = data.get("mutations", [])
    ids_unique(mutations, "mutations", errors)
    require_fields(mutations, {"id", "class", "routes", "endpoint", "precondition", "permission", "v2", "unit"}, "mutations", errors)
    mutation_classes = {row.get("class") for row in mutations}
    if not REQUIRED_MUTATION <= mutation_classes:
        errors.append(f"mutations: missing classes {sorted(REQUIRED_MUTATION-mutation_classes)}")
    if any(not row.get("routes") for row in mutations):
        errors.append("mutations: empty route coverage")

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
    allowed = REQUIRED_HANDOFF
    if not handoff_classes <= allowed:
        errors.append(f"handoff: unsupported classifications {sorted(handoff_classes-allowed)}")
    if not {"v2-blocker", "parallel-product", "candidate-input-change", "superseded-inventory", "cutover-deferred"} <= handoff_classes:
        errors.append("handoff: required disposition class absent")

    runtime = data.get("runtimeDecision", {})
    require_fields([runtime], {"posture", "hostedBoundary", "owner", "availability", "secrets", "ingress", "observability", "upgrades", "incidentResponse", "retention", "cost", "disasterRecovery"}, "runtimeDecision", errors)
    if runtime.get("posture") != "scheduled-audit-authoritative" or runtime.get("hostedBoundary") != "rejected-for-cutover":
        errors.append("runtimeDecision: unratified cutover posture")

    projection = data.get("liveProjection", {})
    if set(projection.get("blockedBy", {})) != {"FS-GG/.github#2963", "FS-GG/.github#2964", "FS-GG/.github#2965"}:
        errors.append("liveProjection: program dependency census incomplete")
    if projection.get("pendingBoardWrites") != 0:
        errors.append("liveProjection: pending board writes")

    if acceptance:
        required_reviews = set(data.get("reviewsRequired", []))
        accepted = {row.get("role") for row in data.get("reviews", []) if row.get("verdict") == "accepted" and row.get("fingerprint")}
        if required_reviews != accepted:
            errors.append(f"reviews: acceptance mismatch missing={sorted(required_reviews-accepted)} extra={sorted(accepted-required_reviews)}")
    return errors


def self_test(data: dict[str, Any]) -> list[str]:
    failures: list[str] = []
    mutations = [
        ("authority omission", lambda d: d["authorities"].pop()),
        ("writer omission", lambda d: d["mutations"].pop()),
        ("corpus omission", lambda d: d["corpus"].pop()),
        ("deletion proof omission", lambda d: d["compatibilityDeletion"][0].__setitem__("absenceTest", "")),
        ("runtime ambiguity", lambda d: d["runtimeDecision"].__setitem__("posture", "webhook-authoritative")),
        ("pending projection", lambda d: d["liveProjection"].__setitem__("pendingBoardWrites", 1)),
    ]
    for name, mutate in mutations:
        candidate = copy.deepcopy(data)
        mutate(candidate)
        if not validate(candidate):
            failures.append(f"mutation survived: {name}")
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

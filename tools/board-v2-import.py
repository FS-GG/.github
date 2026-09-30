#!/usr/bin/env python3
"""Validate and summarize a bounded Coordination V2 import manifest.

This tool is deliberately offline. Project creation, membership and field writes must
go through the separately qualified Projects operation route. The tool makes the
approved input and the expected pilot readback deterministic without becoming that
writer.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

SCHEMA = "fsgg.coordination-board-v2-import/v1"
ISSUE = re.compile(r"^[^/]+/[^#]+#[1-9][0-9]*$")

EXPECTED_FIELDS = {
    "Status": ("single-select", ["Backlog", "Ready", "In progress", "Blocked", "Done"], "human-scheduling", False),
    "Roadmap": ("text", [], "owning-plan", False),
    "Track": ("single-select", ["Active delivery", "Follow-up"], "human-scheduling", False),
    "Observation": ("single-select", ["Verified", "Stale", "Unknown"], "restricted-refresh", True),
}
ADD_DECISIONS = {"import", "follow-up"}


class InvalidManifest(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise InvalidManifest(message)


def load(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise InvalidManifest(str(error)) from error
    require(isinstance(value, dict), "manifest root must be an object")
    return value


def validate(value: dict) -> dict:
    require(value.get("schema") == SCHEMA, f"schema must be {SCHEMA}")
    source = value.get("source", {})
    require(
        source == {
            "owner": "FS-GG",
            "title": "Coordination",
            "number": 1,
            "id": "PVT_kwDOEYAWY84Bb08W",
            "treatment": "retain-as-legacy",
        },
        "source must be the exact retained legacy Coordination Project 1",
    )
    target = value.get("target", {})
    require(target.get("ownerKind") == "organization", "target ownerKind must be organization")
    require(target.get("owner") == "FS-GG", "target owner must be FS-GG")
    require(target.get("title") == "Coordination V2", "target title must be Coordination V2")
    require(target.get("schemaVersion") == 1, "target schemaVersion must be 1")
    creation = target.get("creationState")
    require(creation in {"pending-authorized-operation", "created-and-read-back"}, "invalid target creationState")
    if creation == "pending-authorized-operation":
        require(target.get("number") is None and target.get("id") is None, "pending target must not invent identities")
    else:
        require(isinstance(target.get("number"), int) and target["number"] > 0, "created target needs a number")
        require(isinstance(target.get("id"), str) and target["id"].startswith("PVT_"), "created target needs a PVT id")

    fields = value.get("fields")
    require(isinstance(fields, list) and len(fields) == 4, "exactly four project fields are required")
    by_name = {field.get("name"): field for field in fields if isinstance(field, dict)}
    require(set(by_name) == set(EXPECTED_FIELDS), "field names must be exactly Status, Roadmap, Track and Observation")
    for name, expected in EXPECTED_FIELDS.items():
        kind, options, owner, refresh = expected
        field = by_name[name]
        require(field.get("kind") == kind, f"{name} kind differs")
        require(field.get("options") == options, f"{name} options differ")
        require(field.get("owner") == owner, f"{name} owner differs")
        require(field.get("importerMaySeed") is True, f"{name} must permit the bounded initial seed")
        require(field.get("refreshMayWrite") is refresh, f"{name} refresh ownership differs")
        if creation == "created-and-read-back":
            require(isinstance(field.get("id"), str) and field["id"], f"{name} needs a read-back field id")
        else:
            require(field.get("id") is None, f"pending {name} must not invent an id")

    items = value.get("items")
    require(isinstance(items, list) and items, "items must be a non-empty array")
    seen_refs: set[str] = set()
    seen_nodes: set[str] = set()
    pilot_count = 0
    unresolved = 0
    for index, item in enumerate(items):
        require(isinstance(item, dict), f"item {index} must be an object")
        ref = item.get("issue")
        node = item.get("nodeId")
        require(isinstance(ref, str) and ISSUE.fullmatch(ref), f"item {index} has an invalid issue ref")
        require(isinstance(node, str) and node.startswith("I_"), f"{ref} has an invalid source node id")
        require(ref not in seen_refs, f"duplicate issue identity {ref}")
        require(node not in seen_nodes, f"duplicate issue node identity {node}")
        seen_refs.add(ref)
        seen_nodes.add(node)
        decision = item.get("decision")
        require(decision in ADD_DECISIONS | {"omit-delivered", "omit-superseded", "adjudicate"}, f"{ref} has an invalid decision")
        add = decision in ADD_DECISIONS
        require(not item.get("pilot") or add, f"{ref} cannot be a pilot when its decision is {decision}")
        require(not add or bool(item.get("remainingOutcome")), f"{ref} import needs a remaining outcome")
        require(not add or bool(item.get("roadmap")), f"{ref} import needs an owning roadmap")
        if item.get("pilot"):
            pilot_count += 1
        if decision == "adjudicate":
            unresolved += 1
        dependencies = item.get("dependencies")
        require(isinstance(dependencies, list), f"{ref} dependencies must be an array")
        dep_refs: set[str] = set()
        for dep in dependencies:
            require(isinstance(dep, dict) and ISSUE.fullmatch(str(dep.get("issue", ""))), f"{ref} has an invalid dependency")
            require(dep["issue"] not in dep_refs, f"{ref} repeats dependency {dep['issue']}")
            dep_refs.add(dep["issue"])
            require(dep.get("observedState") in {"open", "closed", "unknown"}, f"{ref} dependency state is invalid")
            require(dep.get("evidence") in {"native", "issue-body", "owning-plan", "unknown"}, f"{ref} dependency evidence is invalid")

    require(1 <= pilot_count <= 5, "the representative pilot must contain one to five existing issues")
    return {
        "schema": SCHEMA,
        "candidateCount": len(items),
        "approvedImportCount": sum(item["decision"] in ADD_DECISIONS for item in items),
        "pilotCount": pilot_count,
        "omittedCount": sum(item["decision"].startswith("omit-") for item in items),
        "unresolvedCount": unresolved,
        "targetCreationState": creation,
    }


def pilot_plan(value: dict) -> dict:
    summary = validate(value)
    target = value["target"]
    return {
        "schema": "fsgg.coordination-board-v2-pilot-plan/v1",
        "target": {"ownerKind": target["ownerKind"], "owner": target["owner"], "title": target["title"], "number": target["number"], "id": target["id"]},
        "precondition": "created target and all four field identities read back",
        "items": [
            {
                "issue": item["issue"],
                "nodeId": item["nodeId"],
                "seed": {"Status": item["status"], "Roadmap": item["roadmap"], "Track": item["track"], "Observation": item["observation"]},
                "dependencies": item["dependencies"],
                "readback": ["same issue nodeId", "single target membership", "four seeded fields", "unchanged dependency refs"],
            }
            for item in value["items"]
            if item["pilot"]
        ],
        "counts": summary,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--pilot-plan", action="store_true", help="emit the deterministic representative pilot plan")
    args = parser.parse_args()
    try:
        value = load(args.manifest)
        result = pilot_plan(value) if args.pilot_plan else validate(value)
    except InvalidManifest as error:
        print(f"board-v2 import refused: {error}", file=sys.stderr)
        return 2
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

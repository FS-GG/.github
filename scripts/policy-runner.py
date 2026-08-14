#!/usr/bin/env python3
"""Discover and run repository policy subjects from one neutral inventory."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile


class PolicyError(Exception):
    pass


def read_json(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise PolicyError(f"cannot read {path}: {error}") from error
    if not isinstance(value, dict):
        raise PolicyError(f"{path}: root must be an object")
    return value


def subjects(root: Path) -> list[dict]:
    document = read_json(root / "policy/subjects.json")
    rows = document.get("subjects")
    if document.get("schema_version") != 1 or not isinstance(rows, list) or not rows:
        raise PolicyError("policy/subjects.json must be a non-empty schema-v1 inventory")
    seen: set[str] = set()
    for row in rows:
        if not isinstance(row, dict) or not isinstance(row.get("id"), str):
            raise PolicyError("each policy subject must have an id")
        if row["id"] in seen:
            raise PolicyError(f"duplicate policy subject: {row['id']}")
        seen.add(row["id"])
        for field in ("command",):
            command = row.get(field)
            if not isinstance(command, list) or not command or not all(isinstance(x, str) and x for x in command):
                raise PolicyError(f"{row['id']}: {field} must be a non-empty string array")
    return rows


def line_count(path: Path) -> int:
    with path.open("rb") as stream:
        return sum(1 for _ in stream)


def shared_bootstrap(root: Path) -> str:
    action_path = root / ".github/actions/setup-policy-python/action.yml"
    action = action_path.read_text(encoding="utf-8") if action_path.is_file() else ""
    for required in ("actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97", "PyYAML==6.0.3"):
        if required not in action:
            raise PolicyError(f"shared policy bootstrap is missing pin: {required}")
    workflows = sorted((root / ".github/workflows").glob("*.yml"))
    bypass = [p.relative_to(root).as_posix() for p in workflows
              if re.search(r"pip\s+install[^\n]*\bpyyaml\b", p.read_text(encoding="utf-8"), re.I)]
    if bypass:
        raise PolicyError("workflow(s) bypass shared policy bootstrap: " + ", ".join(bypass))
    uses = sum(p.read_text(encoding="utf-8").count("uses: ./.github/actions/setup-policy-python") for p in workflows)
    if uses == 0:
        raise PolicyError("shared policy bootstrap has no workflow consumers")
    return f"shared policy bootstrap: {uses} workflow uses, PyYAML==6.0.3"


def inventory(root: Path, inventory_path: Path) -> list[str]:
    document = read_json(inventory_path)
    threshold, rows = document.get("large_checker_threshold_lines"), document.get("checkers")
    if not isinstance(threshold, int) or threshold <= 0 or not isinstance(rows, list):
        raise PolicyError("checker inventory requires a positive threshold and checkers array")
    registered: set[str] = set()
    summaries: list[str] = []
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise PolicyError(f"checkers[{index}] must be an object")
        missing = [f for f in ("source", "owner", "fixture", "workflow")
                   if not isinstance(row.get(f), str) or not row[f].strip()]
        if missing:
            raise PolicyError(f"checkers[{index}] has blank/missing fields: {', '.join(missing)}")
        source = row["source"]
        if source in registered:
            raise PolicyError(f"duplicate checker registration: {source}")
        registered.add(source)
        for label, value in (("source", source), ("fixture", row["fixture"]), ("workflow", row["workflow"])):
            if not (root / value).is_file():
                raise PolicyError(f"{source}: {label} does not exist: {value}")
        lines = line_count(root / source)
        if lines < threshold:
            raise PolicyError(f"{source}: registered as large but has only {lines} lines (< {threshold})")
        workflow = (root / row["workflow"]).read_text(encoding="utf-8")
        if row["fixture"] not in workflow or source not in workflow:
            raise PolicyError(f"{source}: workflow does not execute checker and fixture")
        summaries.append(f"{source}: {lines} lines, owner={row['owner']}, fixture={row['fixture']}")
    discovered = {p.relative_to(root).as_posix() for p in (root / "scripts").glob("check-*")
                  if p.is_file() and line_count(p) >= threshold}
    if discovered - registered:
        raise PolicyError("large checker(s) missing from inventory: " + ", ".join(sorted(discovered - registered)))
    if registered - discovered:
        raise PolicyError("stale checker inventory row(s): " + ", ".join(sorted(registered - discovered)))
    return summaries


def inventory_self_test() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "scripts").mkdir()
        (root / "tests/alpha").mkdir(parents=True)
        (root / ".github/actions/setup-policy-python").mkdir(parents=True)
        (root / ".github/workflows").mkdir(parents=True)
        (root / "scripts/check-alpha.py").write_text("# line\n" * 5, encoding="utf-8")
        (root / "tests/alpha/run.sh").write_text("#!/bin/sh\n", encoding="utf-8")
        (root / ".github/workflows/alpha.yml").write_text(
            "uses: ./.github/actions/setup-policy-python\nrun: python scripts/check-alpha.py && bash tests/alpha/run.sh\n",
            encoding="utf-8")
        (root / ".github/actions/setup-policy-python/action.yml").write_text(
            "uses: actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97\nrun: pip install PyYAML==6.0.3\n",
            encoding="utf-8")
        data = {"large_checker_threshold_lines": 5, "checkers": [{"source": "scripts/check-alpha.py",
                "owner": "maintainers", "fixture": "tests/alpha/run.sh", "workflow": ".github/workflows/alpha.yml"}]}
        path = root / "scripts/policy-checkers.json"
        path.write_text(json.dumps(data), encoding="utf-8")
        inventory(root, path); shared_bootstrap(root)
        data["checkers"][0]["owner"] = ""; path.write_text(json.dumps(data), encoding="utf-8")
        try:
            inventory(root, path)
        except PolicyError as error:
            if "owner" not in str(error): raise
        else:
            raise AssertionError("blank owner unexpectedly passed")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("list", "run", "inventory"))
    parser.add_argument("subject", nargs="?")
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    try:
        if args.action == "inventory":
            if args.self_test:
                inventory_self_test(); print("policy inventory self-test: ok"); return 0
            print("ok " + shared_bootstrap(root))
            rows = inventory(root, root / "scripts/policy-checkers.json")
            print(f"policy inventory: ok ({len(rows)} large checkers)"); return 0
        rows = subjects(root)
        if args.action == "list":
            for row in rows: print(row["id"])
            return 0
        selected = rows if args.subject in (None, "all") else [r for r in rows if r["id"] == args.subject]
        if not selected: raise PolicyError(f"unknown policy subject: {args.subject}")
        for row in selected:
            if row.get("self_test"): subprocess.run(row["self_test"], cwd=root, check=True)
            subprocess.run(row["command"], cwd=root, check=True)
            print(f"policy subject: ok: {row['id']}")
        return 0
    except (PolicyError, OSError, subprocess.CalledProcessError) as error:
        print(f"policy runner: ERROR: {error}", file=sys.stderr); return 1


if __name__ == "__main__":
    raise SystemExit(main())

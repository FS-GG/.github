#!/usr/bin/env python3
"""Require every large policy checker to name an owner and an exercised fixture."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import tempfile


class InventoryError(Exception):
    pass


def line_count(path: Path) -> int:
    with path.open("rb") as stream:
        return sum(1 for _ in stream)


def load_inventory(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise InventoryError(f"cannot read {path}: {error}") from error
    if not isinstance(value, dict):
        raise InventoryError(f"{path}: root must be an object")
    return value


def check_shared_bootstrap(root: Path) -> str:
    action_path = root / ".github/actions/setup-policy-python/action.yml"
    if not action_path.is_file():
        raise InventoryError("shared policy bootstrap is missing")
    action = action_path.read_text(encoding="utf-8")
    for required in (
        "actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97",
        "PyYAML==6.0.3",
    ):
        if required not in action:
            raise InventoryError(f"shared policy bootstrap is missing pin: {required}")

    workflows = sorted((root / ".github/workflows").glob("*.yml"))
    direct_installs = [
        path.relative_to(root).as_posix()
        for path in workflows
        if re.search(r"pip\s+install[^\n]*\bpyyaml\b", path.read_text(encoding="utf-8"), re.IGNORECASE)
    ]
    if direct_installs:
        raise InventoryError("workflow(s) bypass shared policy bootstrap: " + ", ".join(direct_installs))

    use_count = sum(
        path.read_text(encoding="utf-8").count("uses: ./.github/actions/setup-policy-python")
        for path in workflows
    )
    if use_count == 0:
        raise InventoryError("shared policy bootstrap has no workflow consumers")
    return f"shared policy bootstrap: {use_count} workflow uses, PyYAML==6.0.3"


def check(root: Path, inventory_path: Path) -> list[str]:
    document = load_inventory(inventory_path)
    threshold = document.get("large_checker_threshold_lines")
    rows = document.get("checkers")
    if not isinstance(threshold, int) or threshold <= 0:
        raise InventoryError("large_checker_threshold_lines must be a positive integer")
    if not isinstance(rows, list):
        raise InventoryError("checkers must be an array")

    registered: set[str] = set()
    summaries: list[str] = []
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise InventoryError(f"checkers[{index}] must be an object")
        missing = [
            field
            for field in ("source", "owner", "fixture", "workflow")
            if not isinstance(row.get(field), str) or not row[field].strip()
        ]
        if missing:
            raise InventoryError(f"checkers[{index}] has blank/missing fields: {', '.join(missing)}")

        source = row["source"]
        if source in registered:
            raise InventoryError(f"duplicate checker registration: {source}")
        registered.add(source)

        source_path = root / source
        fixture_path = root / row["fixture"]
        workflow_path = root / row["workflow"]
        for label, path in (
            ("source", source_path),
            ("fixture", fixture_path),
            ("workflow", workflow_path),
        ):
            if not path.is_file():
                raise InventoryError(f"{source}: {label} does not exist: {path.relative_to(root)}")

        lines = line_count(source_path)
        if lines < threshold:
            raise InventoryError(f"{source}: registered as large but has only {lines} lines (< {threshold})")

        workflow = workflow_path.read_text(encoding="utf-8")
        if row["fixture"] not in workflow:
            raise InventoryError(f"{source}: workflow does not exercise fixture {row['fixture']}")
        if source not in workflow:
            raise InventoryError(f"{source}: workflow does not execute checker source")
        summaries.append(f"{source}: {lines} lines, owner={row['owner']}, fixture={row['fixture']}")

    discovered = {
        path.relative_to(root).as_posix()
        for path in (root / "scripts").glob("check-*")
        if path.is_file() and line_count(path) >= threshold
    }
    missing = sorted(discovered - registered)
    stale = sorted(registered - discovered)
    if missing:
        raise InventoryError("large checker(s) missing from inventory: " + ", ".join(missing))
    if stale:
        raise InventoryError("inventory row(s) no longer meet discovery rule: " + ", ".join(stale))
    return summaries


def self_test() -> None:
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "scripts").mkdir()
        (root / "tests/alpha").mkdir(parents=True)
        (root / ".github/actions/setup-policy-python").mkdir(parents=True)
        (root / ".github/workflows").mkdir(parents=True)
        source = root / "scripts/check-alpha.py"
        source.write_text("# line\n" * 5, encoding="utf-8")
        (root / "tests/alpha/run.sh").write_text("#!/usr/bin/env bash\n", encoding="utf-8")
        (root / ".github/workflows/alpha.yml").write_text(
            "uses: ./.github/actions/setup-policy-python\n"
            "run: python scripts/check-alpha.py && bash tests/alpha/run.sh\n",
            encoding="utf-8",
        )
        (root / ".github/actions/setup-policy-python/action.yml").write_text(
            "uses: actions/setup-python@5fda3b95a4ea91299a34e894583c3862153e4b97\n"
            "run: pip install PyYAML==6.0.3\n",
            encoding="utf-8",
        )
        inventory = root / "scripts/policy-checkers.json"
        valid = {
            "large_checker_threshold_lines": 5,
            "checkers": [
                {
                    "source": "scripts/check-alpha.py",
                    "owner": "maintainers",
                    "fixture": "tests/alpha/run.sh",
                    "workflow": ".github/workflows/alpha.yml",
                }
            ],
        }
        inventory.write_text(json.dumps(valid), encoding="utf-8")
        check(root, inventory)
        check_shared_bootstrap(root)

        invalid = json.loads(json.dumps(valid))
        invalid["checkers"][0]["owner"] = ""
        inventory.write_text(json.dumps(invalid), encoding="utf-8")
        try:
            check(root, inventory)
        except InventoryError as error:
            if "owner" not in str(error):
                raise AssertionError(f"blank-owner case gave wrong error: {error}") from error
        else:
            raise AssertionError("blank-owner case unexpectedly passed")

        invalid = json.loads(json.dumps(valid))
        invalid["checkers"] = []
        inventory.write_text(json.dumps(invalid), encoding="utf-8")
        try:
            check(root, inventory)
        except InventoryError as error:
            if "missing from inventory" not in str(error):
                raise AssertionError(f"unregistered case gave wrong error: {error}") from error
        else:
            raise AssertionError("unregistered checker case unexpectedly passed")

        inventory.write_text(json.dumps(valid), encoding="utf-8")
        workflow = root / ".github/workflows/alpha.yml"
        workflow.write_text(workflow.read_text() + "run: pip install pyyaml\n", encoding="utf-8")
        try:
            check_shared_bootstrap(root)
        except InventoryError as error:
            if "bypass" not in str(error):
                raise AssertionError(f"bootstrap-bypass case gave wrong error: {error}") from error
        else:
            raise AssertionError("bootstrap-bypass case unexpectedly passed")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path("."))
    parser.add_argument("--inventory", type=Path, default=Path("scripts/policy-checkers.json"))
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        print("policy-checker inventory self-test: ok")
        return 0
    try:
        bootstrap = check_shared_bootstrap(args.root.resolve())
        summaries = check(args.root.resolve(), (args.root / args.inventory).resolve())
    except InventoryError as error:
        print(f"policy-checker inventory: ERROR: {error}")
        return 1
    print(f"ok {bootstrap}")
    for summary in summaries:
        print(f"ok {summary}")
    print(f"policy-checker inventory: ok ({len(summaries)} large checkers)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

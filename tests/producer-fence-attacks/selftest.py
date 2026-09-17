#!/usr/bin/env python3
"""Mutation checks proving that the GS2-08.6 evidence validator fails closed."""

from __future__ import annotations

import importlib.util
import json
import pathlib
import shutil
import tempfile
import xml.etree.ElementTree as ET

HERE = pathlib.Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("producer_fence_validator", HERE / "run.py")
assert SPEC is not None and SPEC.loader is not None
validator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)


def must_fail(action, message: str) -> None:
    try:
        action()
    except SystemExit:
        return
    raise SystemExit(f"producer-fence-validator-selftest: accepted {message}")


def write_trx(path: pathlib.Path, names: list[str]) -> None:
    root = ET.Element("TestRun")
    results = ET.SubElement(root, "Results")
    for index, name in enumerate(names):
        ET.SubElement(
            results,
            "UnitTestResult",
            testName=name,
            executionId=f"00000000-0000-0000-0000-{index:012d}",
            outcome="Passed",
        )
    ET.SubElement(root, "Counters", total=str(len(names)), passed=str(len(names)))
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def main() -> None:
    oracle = json.loads(validator.ORACLE_PATH.read_text())
    exact = sorted(validator.expected_test_names(oracle))

    with tempfile.TemporaryDirectory() as temporary:
        temp = pathlib.Path(temporary)
        trx = temp / "evidence.trx"
        write_trx(trx, exact)
        validator.validate_trx(trx, set(exact))

        write_trx(trx, exact[:-1])
        must_fail(lambda: validator.validate_trx(trx, set(exact)), "a removed exact test")

        write_trx(trx, exact + [exact[0]])
        must_fail(lambda: validator.validate_trx(trx, set(exact)), "a duplicated exact test")

        source_root = temp / "source"
        for relative in oracle["testOracle"]["sourceAssertionMinimums"]:
            target = source_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(validator.ROOT / relative, target)
        validator.validate_assertion_floors(oracle, source_root)

        relative, minimum = next(iter(oracle["testOracle"]["sourceAssertionMinimums"].items()))
        target = source_root / relative
        source = target.read_text()
        target.write_text(source.replace("Assert.", "AssertionRemoved.", source.count("Assert.") - minimum + 1))
        must_fail(lambda: validator.validate_assertion_floors(oracle, source_root), "removed assertions")

    print(f"producer-fence-validator-selftest: pass ({len(exact)} exact tests)")


if __name__ == "__main__":
    main()

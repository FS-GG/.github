#!/usr/bin/env python3
"""Mutation checks proving that the GS2-08.6 evidence validator fails closed."""

from __future__ import annotations

import importlib.util
import hashlib
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

    historical = next(row for row in json.loads(validator.EXTERNAL_PATH.read_text())["routes"]
                      if row["path"] == ".github/workflows/kit-materialize.yml")
    successor = json.loads(validator.READ_ONLY_SUCCESSORS_PATH.read_text())["routes"][0]
    source = (validator.ROOT / historical["path"]).read_bytes()
    validator.validate_kit_read_only_successor(historical, successor, source)

    def successor_for(candidate: bytes) -> dict:
        return {**successor, "sha256": hashlib.sha256(candidate).hexdigest()}

    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, {**successor, "sha256": "0" * 64}, source), "a forged successor source hash")
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, {**successor, "predecessorSha256": "0" * 64}, source),
        "a forged historical predecessor")
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, {**successor, "disposition": "remote-writer"}, source),
        "a forged read-only disposition")
    new_job = source.replace(b"  receiver-validate:\n", b"  new-writer:\n    runs-on: ubuntu-latest\n  receiver-validate:\n")
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, successor_for(new_job), new_job), "an added writer job with a matching hash")
    write_permission = source.replace(b"  contents: read\n", b"  contents: write\n", 1)
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, successor_for(write_permission), write_permission),
        "a write permission with a matching hash")
    app_token = source.replace(b"        uses: actions/setup-dotnet@v6\n",
                               b"        uses: actions/create-github-app-token@v2\n", 1)
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, successor_for(app_token), app_token), "an App token with a matching hash")
    write_command = source.replace(b"          dotnet restore \"$RECEIVER_PROJECT\" -v minimal\n",
                                   b"          gh api -X POST repos/FS-GG/.github/dispatches\n", 1)
    must_fail(lambda: validator.validate_kit_read_only_successor(
        historical, successor_for(write_command), write_command),
        "a provider write command with a matching hash")

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

#!/usr/bin/env python3
"""Mutation controls for the GS2-08.7 candidate identity boundary."""

from __future__ import annotations

import argparse
import json
import pathlib
import shutil
import subprocess
import tempfile
import zipfile

HERE = pathlib.Path(__file__).resolve().parent
RUNNER = HERE / "run.py"


def expect_refusal(name: str, package: pathlib.Path, binding: pathlib.Path, contains: str) -> None:
    result = subprocess.run(
        ["python3", str(RUNNER), "verify", "--identity-only", "--package", str(package), "--binding", str(binding)],
        text=True, capture_output=True, check=False,
    )
    output = result.stdout + result.stderr
    if result.returncode == 0 or contains not in output:
        raise SystemExit(f"bridge-package-selftest: {name} was accepted or gave the wrong refusal: {output}")
    print(f"PASS  {name}")


def expect_describe_refusal(name: str, package: pathlib.Path, binding: dict[str, object], contains: str) -> None:
    result = subprocess.run(
        ["python3", str(RUNNER), "describe", "--package", str(package),
         "--source-commit", binding["source"]["commit"], "--source-tree", binding["source"]["tree"]],
        text=True, capture_output=True, check=False,
    )
    output = result.stdout + result.stderr
    if result.returncode == 0 or contains not in output:
        raise SystemExit(f"bridge-package-selftest: {name} was accepted or gave the wrong refusal: {output}")
    print(f"PASS  {name}")


def write_json(path: pathlib.Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def rewrite_package(source: pathlib.Path, target: pathlib.Path, *, remove: str | None = None, change: str | None = None) -> None:
    with zipfile.ZipFile(source) as original, zipfile.ZipFile(target, "w") as mutant:
        for info in original.infolist():
            if info.filename == remove:
                continue
            value = original.read(info.filename)
            if info.filename == change:
                value += b"same-version-substitution"
            mutant.writestr(info, value)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--package", type=pathlib.Path, required=True)
    parser.add_argument("--binding", type=pathlib.Path, required=True)
    args = parser.parse_args()
    package, original_binding = args.package.resolve(), json.loads(args.binding.read_text(encoding="utf-8"))

    with tempfile.TemporaryDirectory(prefix="fsgg-bridge-negative-") as temporary:
        root = pathlib.Path(temporary)

        missing = root / package.name
        rewrite_package(package, missing, remove="tools/net10.0/any/FS.GG.Coord.GitHub.dll")
        expect_describe_refusal("missing bridge assembly", missing, original_binding, "candidate is missing bridge assembly")

        changed = root / ("changed-" + package.name)
        rewrite_package(package, changed, change="tools/net10.0/any/FS.GG.Coord.GitHub.dll")
        changed_binding = root / "changed-binding.json"
        refreshed = subprocess.run(
            ["python3", str(RUNNER), "describe", "--package", str(changed),
             "--source-commit", original_binding["source"]["commit"], "--source-tree", original_binding["source"]["tree"]],
            text=True, capture_output=True, check=False,
        )
        if refreshed.returncode:
            raise SystemExit(f"bridge-package-selftest: could not prepare changed-assembly control: {refreshed.stderr}")
        changed_binding.write_text(refreshed.stdout, encoding="utf-8")
        changed_value = json.loads(changed_binding.read_text(encoding="utf-8"))
        changed_value["installedAssemblyDigests"]["FS.GG.Coord.GitHub.dll"] = original_binding["installedAssemblyDigests"]["FS.GG.Coord.GitHub.dll"]
        write_json(changed_binding, changed_value)
        expect_refusal("changed bridge assembly", changed, changed_binding, "changed bridge assembly digest")

        wrong_source = root / "wrong-source.json"
        value = dict(original_binding); value["source"] = dict(value["source"]); value["source"]["tree"] = "0" * 40
        write_json(wrong_source, value)
        expect_refusal("wrong source binding", package, wrong_source, "wrong source tree binding")

        wrong_receipt = root / "wrong-receipt.json"
        value = dict(original_binding); value["acceptedReceipts"] = dict(value["acceptedReceipts"]); value["acceptedReceipts"]["gs2-08.6"] = "0" * 64
        write_json(wrong_receipt, value)
        expect_refusal("wrong receipt binding", package, wrong_receipt, "wrong accepted receipt binding")

        incomplete = root / "incomplete.json"
        value = dict(original_binding); del value["payloadSha256"]
        write_json(incomplete, value)
        expect_refusal("incomplete evidence", package, incomplete, "incomplete or unknown candidate evidence")

        substituted = root / ("substituted-" + package.name)
        shutil.copy2(package, substituted)
        with substituted.open("ab") as stream:
            stream.write(b"same-version-package-substitution")
        expect_refusal("substituted same-version package", substituted, args.binding, "substituted same-version package archive")

    print("bridge-package-selftest: PASS (6 refusal controls)")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Mutation coverage for the fail-closed v1 writer census."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CENSUS = ROOT / "docs/coordination/v1-writer-census.json"
FIXTURES = Path(__file__).resolve().parent / "fixtures"
passed = 0
failed = 0


def ok(label: str) -> None:
    global passed
    passed += 1
    print(f"PASS  {label}")


def bad(label: str, detail: str = "") -> None:
    global failed
    failed += 1
    print(f"FAIL  {label}{': ' + detail if detail else ''}", file=sys.stderr)


def run(*args: str, root: Path = ROOT) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(root / "scripts/check-v1-writer-census.py"), "--root", str(root), *args],
        text=True,
        capture_output=True,
        check=False,
    )


def expect_red(result: subprocess.CompletedProcess[str], needle: str, label: str) -> None:
    if result.returncode != 0 and needle in result.stderr:
        ok(label)
    else:
        bad(label, f"rc={result.returncode}; stderr={result.stderr.strip()!r}")


with tempfile.TemporaryDirectory(prefix="v1-writer-census-") as temporary:
    work = Path(temporary)
    structural = run("--structural")
    if structural.returncode == 0 and "54 command roots: 20 always, 6 conditional, 28 never" in structural.stdout:
        ok("checked-in census covers the tracked executable population")
    else:
        bad("checked-in census covers the tracked executable population", structural.stderr.strip())

    supplied_candidate = os.environ.get("V1_WRITER_CENSUS_CANDIDATE")
    if supplied_candidate:
        engine = ROOT / supplied_candidate
        build_error = "workflow-supplied candidate does not exist"
    else:
        build = subprocess.run(
            [str(ROOT / "scripts/build-gate-engine")],
            text=True,
            capture_output=True,
            check=False,
        )
        engine = Path(build.stdout.strip())
        build_error = build.stderr[-1000:]
    if not engine.is_file():
        bad("candidate build supplies one typed command contract", build_error)
        contract = {"schema": "invalid", "commands": []}
    else:
        emitted = subprocess.run([str(engine), "command-contract"], text=True, capture_output=True, check=False)
        try:
            contract = json.loads(emitted.stdout)
        except json.JSONDecodeError:
            contract = {"schema": "invalid", "commands": []}
        if emitted.returncode == 0 and contract.get("schema") == "fsgg.coord.commands/1":
            ok("candidate build supplies one typed command contract")
        else:
            bad("candidate build supplies one typed command contract", emitted.stderr.strip())

    contract_path = work / "contract.json"
    contract_path.write_text(json.dumps(contract), encoding="utf-8")
    typed = run("--contract", str(contract_path))
    if typed.returncode == 0 and "candidate-built" in typed.stdout:
        ok("candidate-built metadata agrees with the census")
    else:
        bad("candidate-built metadata agrees with the census", typed.stderr.strip())

    base_census = json.loads(CENSUS.read_text(encoding="utf-8"))

    omitted = json.loads(json.dumps(base_census))
    omitted["commandRoots"] = omitted["commandRoots"][1:]
    omitted_path = work / "omitted.json"
    omitted_path.write_text(json.dumps(omitted), encoding="utf-8")
    expect_red(run("--census", str(omitted_path), "--contract", str(contract_path)), "omitted/new", "omitted command root reds")

    extra_contract = json.loads(json.dumps(contract))
    extra_contract["commands"].append(json.loads((FIXTURES / "unknown-command.json").read_text(encoding="utf-8")))
    extra_path = work / "extra-contract.json"
    extra_path.write_text(json.dumps(extra_contract), encoding="utf-8")
    expect_red(run("--contract", str(extra_path)), "omitted/new", "new command root reds")

    wrong = json.loads(json.dumps(base_census))
    for row in wrong["commandRoots"]:
        if row["name"] == "add":
            row["writes"] = "never"
    wrong_path = work / "wrong.json"
    wrong_path.write_text(json.dumps(wrong), encoding="utf-8")
    expect_red(run("--census", str(wrong_path), "--contract", str(contract_path)), "misclassified", "misclassified command root reds")

    missing_source = json.loads(json.dumps(base_census))
    missing_source["sources"] = [row for row in missing_source["sources"] if row["path"] != "tools/routine-delivery.py"]
    missing_source_path = work / "missing-source.json"
    missing_source_path.write_text(json.dumps(missing_source), encoding="utf-8")
    expect_red(run("--census", str(missing_source_path), "--structural"), "unknown or unresolved writer source", "omitted direct REST writer reds")

    wrong_source = json.loads(json.dumps(base_census))
    for row in wrong_source["sources"]:
        if row["path"] == "tools/routine-delivery.py":
            row["disposition"] = "read-only"
            row["nonWriterJustification"] = "Mutation deliberately proves a direct writer cannot be called read-only."
    wrong_source_path = work / "wrong-source.json"
    wrong_source_path.write_text(json.dumps(wrong_source), encoding="utf-8")
    expect_red(run("--census", str(wrong_source_path), "--structural"), "direct REST PUT merge", "misclassified direct REST writer reds")

    clone = work / "repo"
    cloned = subprocess.run(["git", "clone", "--quiet", "--shared", str(ROOT), str(clone)], text=True, capture_output=True, check=False)
    if cloned.returncode != 0:
        bad("new dynamic writer reds", cloned.stderr.strip())
    else:
        shutil.copy2(FIXTURES / "new-dynamic-writer.py", clone / "scripts/dynamic-writer.py")
        subprocess.run(["git", "-C", str(clone), "add", "scripts/dynamic-writer.py"], check=True)
        dynamic = run("--structural", root=clone)
        expect_red(dynamic, "scripts/dynamic-writer.py", "new dynamic writer reds")

    unreadable = work / "unreadable.json"
    unreadable.write_text("{", encoding="utf-8")
    expect_red(run("--census", str(unreadable), "--structural"), "unreadable or malformed", "malformed census reds")

    change = (ROOT / "scripts/change-completeness").read_text(encoding="utf-8")
    coord = (ROOT / ".github/workflows/coord-engine.yml").read_text(encoding="utf-8")
    release = (ROOT / ".github/workflows/release-coord-engine.yml").read_text(encoding="utf-8")
    wiring = (
        "check-v1-writer-census.py\" --structural" in change
        and "check-v1-writer-census.py --candidate" in coord
        and "python3 tests/v1-writer-census/run.py" in coord
        and "check-v1-writer-census.py --candidate" in release
        and release.index("check-v1-writer-census.py --candidate") < release.index("dotnet nuget push")
    )
    ok("universal, candidate-build, and pre-publish wiring is present") if wiring else bad("universal, candidate-build, and pre-publish wiring is present")

print(f"\nv1 writer census fixture: {passed} passed, {failed} failed")
raise SystemExit(0 if failed == 0 else 1)

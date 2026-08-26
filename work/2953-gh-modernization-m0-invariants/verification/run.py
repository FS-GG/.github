#!/usr/bin/env python3
"""Run Q0 authoring gates and emit a source-bound JUnit report."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
WORK = ROOT / "work/2953-gh-modernization-m0-invariants"
EVIDENCE = WORK / "q0-evidence.json"
OUTPUT = WORK / "artifacts/q0-verification.junit.xml"


@dataclass
class Case:
    name: str
    command: list[str]
    expected: int = 0
    contains: str | None = None


def run(case: Case) -> tuple[bool, str, float]:
    started = time.monotonic()
    completed = subprocess.run(case.command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    elapsed = time.monotonic() - started
    ok = completed.returncode == case.expected and (case.contains is None or case.contains in completed.stdout)
    return ok, completed.stdout, elapsed


def whitespace_mutation_self_test() -> int:
    with tempfile.TemporaryDirectory() as directory:
        repo = Path(directory)
        def git(*args: str) -> subprocess.CompletedProcess[str]:
            return subprocess.run(["git", *args], cwd=repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if git("init", "-q").returncode != 0:
            return 1
        git("config", "user.name", "Q0 mutation")
        git("config", "user.email", "q0-mutation@example.invalid")
        subject = repo / "subject.md"
        subject.write_text("clean\n", encoding="utf-8")
        git("add", "subject.md")
        if git("commit", "-qm", "baseline").returncode != 0:
            return 1
        subject.write_text("committed trailing whitespace  \n", encoding="utf-8")
        git("add", "subject.md")
        if git("commit", "-qm", "mutation").returncode != 0:
            return 1
        measured = git("diff", "--check", "HEAD~1...HEAD")
        if measured.returncode == 0 or "trailing whitespace" not in measured.stdout:
            return 1
        print("committed whitespace mutation rejected by exact commit range")
        return 0


def coherent_sdd_analysis(payload: object) -> bool:
    if not isinstance(payload, dict):
        return False
    analysis = payload.get("analysis")
    return (
        payload.get("outcome") == "noChange"
        and payload.get("coherent") is True
        and isinstance(analysis, dict)
        and analysis.get("status") == "implementationReady"
        and analysis.get("readiness") == "implementationReady"
        and analysis.get("blockingCount") == 0
        and analysis.get("staleSourceCount") == 0
        and analysis.get("generatedViewFindingCount") == 0
        and payload.get("diagnostics") == []
    )


def sdd_analysis_check() -> int:
    completed = subprocess.run(
        ["fsgg-sdd", "analyze", "--work", "2953-gh-modernization-m0-invariants", "--json"],
        cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
    )
    try:
        payload = json.loads(completed.stdout)
    except json.JSONDecodeError:
        print(completed.stdout)
        print("SDD-RED: analysis output is not exact JSON")
        return 1
    if completed.returncode != 0 or not coherent_sdd_analysis(payload):
        print(completed.stdout)
        print("SDD-RED: analysis must be noChange, coherent, implementationReady, and diagnostic-free")
        return 1
    print("SDD-GREEN: noChange, coherent, implementationReady, diagnostic-free")
    return 0


def sdd_analysis_mutation_self_test() -> int:
    incoherent = {
        "outcome": "noChange", "coherent": False, "diagnostics": [],
        "analysis": {"status": "implementationReady", "readiness": "implementationReady",
                     "blockingCount": 0, "staleSourceCount": 0, "generatedViewFindingCount": 0},
    }
    mutating = {**incoherent, "coherent": True, "outcome": "succeeded"}
    if coherent_sdd_analysis(incoherent) or coherent_sdd_analysis(mutating):
        return 1
    print("incoherent readiness-string and mutating analysis payloads rejected")
    return 0


def main() -> int:
    evidence = json.loads(EVIDENCE.read_text(encoding="utf-8"))
    validator_spec = importlib.util.spec_from_file_location("q0_validator", WORK / "validate_q0.py")
    if validator_spec is None or validator_spec.loader is None:
        raise RuntimeError("cannot load Q0 validator")
    validator = importlib.util.module_from_spec(validator_spec)
    validator_spec.loader.exec_module(validator)
    _, review_errors = validator.discover_live_reviews(evidence["reviewFingerprint"])
    reviews_complete = not review_errors
    acceptance_expected = 0 if reviews_complete else 1
    acceptance_text = "Q0-GREEN: acceptance" if reviews_complete else "expected exactly one unedited accepted current-head attestation"
    cases = [
        Case("q0-candidate-and-mutation-controls", [sys.executable, str(WORK / "validate_q0.py"), str(EVIDENCE), "--self-test"], contains="Q0-GREEN: candidate"),
        Case("q0-review-boundary", [sys.executable, str(WORK / "validate_q0.py"), str(EVIDENCE), "--acceptance"], expected=acceptance_expected, contains=acceptance_text),
        Case("adr-corpus-coherence", [sys.executable, "scripts/check-adr-coherence.py"], contains="adr-coherence: OK"),
        Case("q0-json-parse", [sys.executable, "-m", "json.tool", str(EVIDENCE)]),
        Case("source-diff-whitespace", ["git", "diff", "--check", "origin/main...HEAD"]),
        Case("source-diff-whitespace-mutation", [sys.executable, str(Path(__file__)), "--whitespace-mutation-self-test"], contains="committed whitespace mutation rejected"),
        Case("sdd-coherent-implementation-ready", [sys.executable, str(Path(__file__)), "--sdd-analysis-check"], contains="SDD-GREEN: noChange, coherent"),
        Case("sdd-incoherent-readiness-mutation", [sys.executable, str(Path(__file__)), "--sdd-analysis-mutation-self-test"], contains="incoherent readiness-string"),
        Case("source-tree-clean-after-gates", ["git", "diff", "--exit-code"]),
    ]

    suite = ET.Element("testsuite", name="GS2-00-Q0", tests=str(len(cases)), timestamp="2026-08-26T00:00:00+02:00")
    failures = 0
    total = 0.0
    for case in cases:
        ok, output, elapsed = run(case)
        total += elapsed
        node = ET.SubElement(suite, "testcase", name=case.name, time=f"{elapsed:.6f}")
        if not ok:
            failures += 1
            failure = ET.SubElement(node, "failure", message=f"expected exit {case.expected} and text {case.contains!r}")
            failure.text = output
        else:
            ET.SubElement(node, "system-out").text = output
    suite.set("failures", str(failures))
    suite.set("errors", "0")
    suite.set("skipped", "0")
    suite.set("time", f"{total:.6f}")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    ET.ElementTree(suite).write(OUTPUT, encoding="utf-8", xml_declaration=True)
    digest = hashlib.sha256(OUTPUT.read_bytes()).hexdigest()
    print(f"q0-verification: {'PASS' if failures == 0 else 'FAIL'} {len(cases)-failures}/{len(cases)} sha256:{digest}")
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    if "--whitespace-mutation-self-test" in sys.argv:
        raise SystemExit(whitespace_mutation_self_test())
    if "--sdd-analysis-check" in sys.argv:
        raise SystemExit(sdd_analysis_check())
    if "--sdd-analysis-mutation-self-test" in sys.argv:
        raise SystemExit(sdd_analysis_mutation_self_test())
    raise SystemExit(main())

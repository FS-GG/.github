#!/usr/bin/env python3
"""Run Q0 authoring gates and emit a source-bound JUnit report."""

from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
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
        Case("source-diff-whitespace", ["git", "diff", "--check"]),
        Case("sdd-implementation-ready", ["fsgg-sdd", "analyze", "--work", "2953-gh-modernization-m0-invariants", "--json"], contains='"readiness": "implementationReady"'),
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
    raise SystemExit(main())

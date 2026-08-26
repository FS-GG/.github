#!/usr/bin/env python3
"""Fail closed if the required Q0 workflow can skip live role acceptance."""

from __future__ import annotations

import argparse
from pathlib import Path


REQUIRED_STEP = """      - name: Require exact live role-bound acceptance
        run: >-
          python3 work/2953-gh-modernization-m0-invariants/validate_q0.py
          work/2953-gh-modernization-m0-invariants/q0-evidence.json --acceptance
"""

RETIRED_GATES = (
    "jq '.reviews | length'",
    "jq '.reviewsRequired | length'",
    'if [ "$reviews"',
)


def errors(text: str) -> list[str]:
    findings: list[str] = []
    if text.count(REQUIRED_STEP) != 1:
        findings.append("the exact unconditional live-acceptance step must occur once")
    for retired in RETIRED_GATES:
        if retired in text:
            findings.append(f"retired checked-in review gate remains: {retired}")
    return findings


def self_test(text: str) -> list[str]:
    failures: list[str] = []
    mutants = {
        "missing-live-acceptance": text.replace(REQUIRED_STEP, "", 1),
        "conditional-live-acceptance": text.replace(
            "      - name: Require exact live role-bound acceptance\n",
            "      - name: Require exact live role-bound acceptance\n        if: ${{ false }}\n",
            1,
        ),
        "checked-in-row-gate": text + "\n# jq '.reviews | length'\n",
    }
    for name, mutant in mutants.items():
        if not errors(mutant):
            failures.append(f"mutation survived: {name}")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("workflow", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    text = args.workflow.read_text(encoding="utf-8")
    findings = errors(text)
    if args.self_test and not findings:
        findings.extend(self_test(text))
    if findings:
        for finding in findings:
            print(f"Q0-WORKFLOW-RED: {finding}")
        return 1
    print("Q0-WORKFLOW-GREEN: live acceptance is unconditional; 3/3 inversions rejected")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

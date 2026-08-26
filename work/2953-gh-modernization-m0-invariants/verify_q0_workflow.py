#!/usr/bin/env python3
"""Fail closed if the required Q0 workflow can skip live role acceptance."""

from __future__ import annotations

import argparse
import re
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
REQUIRED_PR_PATHS = {
    ".github/workflows/github-substrate-q0.yml",
    "docs/adr/0078-github-substrate-v2-new-only-coordination-authority.md",
    "docs/coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md",
    "docs/github-substrate-v2-roadmap.md",
    "work/2953-gh-modernization-m0-invariants/**",
}


def errors(text: str) -> list[str]:
    findings: list[str] = []
    if text.count(REQUIRED_STEP) != 1:
        findings.append("the exact unconditional live-acceptance step must occur once")
    trigger_match = re.search(r"(?ms)^  pull_request:\n(?P<body>.*?)^  push:\n", text)
    if trigger_match is None:
        findings.append("the pull_request trigger is absent or unreadable")
    else:
        trigger_paths = set(re.findall(r'^      - "([^"]+)"$', trigger_match.group("body"), re.MULTILINE))
        if trigger_paths != REQUIRED_PR_PATHS:
            findings.append("the pull_request trigger does not cover the exact Q0 subject")
    job_match = re.search(r"(?ms)^  authority-census-is-bound:\n(?P<body>.*)\Z", text)
    if job_match is None:
        findings.append("the authority-census-is-bound job is absent or unreadable")
    elif re.search(r"(?m)^    (?:if|continue-on-error):", job_match.group("body")):
        findings.append("the authority-census-is-bound job is conditional or non-blocking")
    step_start = text.find("      - name: Require exact live role-bound acceptance\n")
    if step_start >= 0:
        step_end = text.find("\n      - ", step_start + 1)
        step = text[step_start:] if step_end < 0 else text[step_start:step_end]
        if re.search(r"(?m)^        (?:if|continue-on-error):", step):
            findings.append("the live-acceptance step is conditional or non-blocking")
    for retired in RETIRED_GATES:
        if retired in text:
            findings.append(f"retired checked-in review gate remains: {retired}")
    return findings


def self_test(text: str) -> list[str]:
    failures: list[str] = []
    mutants = {
        "missing-live-acceptance": text.replace(REQUIRED_STEP, "", 1),
        "conditional-live-acceptance-step": text.replace(
            "      - name: Require exact live role-bound acceptance\n",
            "      - name: Require exact live role-bound acceptance\n        if: ${{ false }}\n",
            1,
        ),
        "nonblocking-live-acceptance": text.replace(
            "      - name: Require exact live role-bound acceptance\n",
            "      - name: Require exact live role-bound acceptance\n        continue-on-error: true\n",
            1,
        ),
        "conditional-authority-job": text.replace(
            "  authority-census-is-bound:\n",
            "  authority-census-is-bound:\n    if: ${{ false }}\n",
            1,
        ),
        "nonmatching-pull-request-trigger": text.replace(
            '      - "work/2953-gh-modernization-m0-invariants/**"\n',
            '      - "unrelated/**"\n',
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
    print("Q0-WORKFLOW-GREEN: live acceptance is independently reachable; 6/6 inversions rejected")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

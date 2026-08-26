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
REQUIRED_TRIGGER_BODY = """    paths:
      - ".github/workflows/github-substrate-q0.yml"
      - "docs/adr/0077-quint-first-typed-specification-authority.md"
      - "docs/adr/0078-github-substrate-v2-new-only-coordination-authority.md"
      - "docs/coordination/2026-08-25-github-substrate-v2-fleet-cutover-design.md"
      - "docs/github-substrate-v2-roadmap.md"
      - "docs/2026-08-26-fs-gg-coordination-admin-settings-report.md"
      - "work/2953-gh-modernization-m0-invariants/**"
"""
REQUIRED_JOB_PREFIX = """  authority-census-is-bound:
    runs-on: ubuntu-latest
    timeout-minutes: 15
    env:
      GH_TOKEN: ${{ github.token }}
    steps:
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
    trigger_match = re.search(r"(?ms)^  pull_request:\n(?P<pull>.*?)^  push:\n(?P<push>.*?)^  workflow_dispatch:\s*$", text)
    if trigger_match is None or trigger_match.group("pull") != REQUIRED_TRIGGER_BODY or trigger_match.group("push") != "    branches: [main]\n" + REQUIRED_TRIGGER_BODY:
        findings.append("pull_request/push triggers do not exactly cover the complete Q0 subject")
    top_level_keys = re.findall(r"(?m)^([A-Za-z0-9_-]+):(?:\s.*)?$", text)
    if top_level_keys != ["name", "on", "permissions", "concurrency", "jobs"]:
        findings.append("the workflow has an extra top-level default, wrapper, or execution key")
    jobs_tail = text.split("\njobs:\n", 1)[1] if "\njobs:\n" in text else ""
    job_ids = re.findall(r"(?m)^  ([A-Za-z0-9_-]+):\s*$", jobs_tail)
    authority_body = jobs_tail.split("  authority-census-is-bound:\n", 1)[1] if jobs_tail.startswith("  authority-census-is-bound:\n") else ""
    job_keys = re.findall(r"(?m)^    ([A-Za-z0-9_-]+):(?:\s.*)?$", authority_body)
    if (
        job_ids != ["authority-census-is-bound"]
        or job_keys != ["runs-on", "timeout-minutes", "env", "steps"]
        or text.count(REQUIRED_JOB_PREFIX) != 1
    ):
        findings.append("the authority job has an extra dependency, wrapper, default, or job")
    step_start = text.find("      - name: Require exact live role-bound acceptance\n")
    if step_start >= 0:
        step_end = text.find("\n      - ", step_start + 1)
        step = text[step_start:] if step_end < 0 else text[step_start:step_end] + "\n"
        if step != REQUIRED_STEP:
            findings.append("the live-acceptance step has an extra condition, wrapper, shell, or continuation")
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
        "folded-exit-mask": text.replace(
            "          work/2953-gh-modernization-m0-invariants/q0-evidence.json --acceptance\n",
            "          work/2953-gh-modernization-m0-invariants/q0-evidence.json --acceptance\n          || true\n",
            1,
        ),
        "conditional-authority-job": text.replace(
            "  authority-census-is-bound:\n",
            "  authority-census-is-bound:\n    if: ${{ false }}\n",
            1,
        ),
        "job-default-shell-mask": text.replace(
            "    runs-on: ubuntu-latest\n",
            "    runs-on: ubuntu-latest\n    defaults:\n      run:\n        shell: bash {0} || true\n",
            1,
        ),
        "workflow-default-shell-mask": text.replace(
            "permissions:\n",
            "defaults:\n  run:\n    shell: bash {0} || true\n\npermissions:\n",
            1,
        ),
        "trailing-conditional-authority-job": text.rstrip("\n")
        + "\n      - name: Harmless trailing step\n        run: echo reached\n    if: ${{ false }}\n",
        "trailing-authority-needs": text.rstrip("\n") + "\n    needs: prerequisite\n",
        "trailing-job-default-shell-mask": text.rstrip("\n")
        + "\n    defaults:\n      run:\n        shell: bash {0} || true\n",
        "skipped-prerequisite": text.replace(
            "  authority-census-is-bound:\n",
            "  prerequisite:\n    if: ${{ false }}\n    runs-on: ubuntu-latest\n    steps: [{run: true}]\n  authority-census-is-bound:\n    needs: prerequisite\n",
            1,
        ),
        "nonmatching-pull-request-trigger": text.replace(
            '      - "work/2953-gh-modernization-m0-invariants/**"\n',
            '      - "unrelated/**"\n',
            1,
        ),
        "narrow-pull-request-types": text.replace("  pull_request:\n", "  pull_request:\n    types: [closed]\n", 1),
        "narrow-pull-request-branches": text.replace("  pull_request:\n", "  pull_request:\n    branches: [never]\n", 1),
        "missing-admin-report-pull-path": text.replace(
            '      - "docs/2026-08-26-fs-gg-coordination-admin-settings-report.md"\n', "", 1
        ),
        "missing-admin-report-push-path": text.rsplit(
            '      - "docs/2026-08-26-fs-gg-coordination-admin-settings-report.md"\n', 1
        )[0] + text.rsplit('      - "docs/2026-08-26-fs-gg-coordination-admin-settings-report.md"\n', 1)[1],
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
    print("Q0-WORKFLOW-GREEN: live acceptance is independently reachable; 17/17 inversions rejected")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

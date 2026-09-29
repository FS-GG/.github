#!/usr/bin/env python3
"""Validate the exact safe retirement shape of coord-board-reconcile.yml.

The legacy workflow used automatic triggers, an App credential, REST budget probing, a source build,
and `reconcile --apply` to write v1 projections. ADR-0091 retired that caller. The remaining workflow
is a manual, credential-free diagnostic whose only claim is that board state was not evaluated and
the V2 projection writer was not activated.

Exit 0 means the exact retired shape is present. Exit 1 means an executable legacy behavior or false
clean claim was introduced. Exit 3 means the workflow could not be inspected. This is a
static check: it performs no API or network access.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import sys

WORKFLOW_REL = ".github/workflows/coord-board-reconcile.yml"
MESSAGE = "retired under ADR-0091; board state not evaluated; V2 projection writer not activated"
RUN = f'        run: echo "{MESSAGE}"'
EXPECTED = (
    "name: Retired board lifecycle projection diagnostic",
    "on:",
    "  workflow_dispatch:",
    "permissions: {}",
    "jobs:",
    "  retired-board-projection-diagnostic:",
    "    name: Retired board projection diagnostic (manual only)",
    "    runs-on: ubuntu-latest",
    "    timeout-minutes: 2",
    "    steps:",
    "      - name: Report the retired boundary",
    RUN,
)


def fail(message: str, code: int) -> int:
    print(f"check-reconcile-concurrency-scope: {message}", file=sys.stderr)
    return code


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args()
    path = Path(args.root) / WORKFLOW_REL
    if not path.is_file():
        return fail(f"NO VERDICT (permanent) — {path} does not exist", 3)
    try:
        source = path.read_text(encoding="utf-8")
    except OSError as error:
        return fail(f"NO VERDICT (permanent) — workflow is unreadable: {error}", 3)
    executable = tuple(
        line.rstrip()
        for line in source.splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    )
    findings: list[str] = []
    if executable != EXPECTED:
        findings.append(
            "executable YAML differs from the exact manual-only retired diagnostic shape "
            "(automatic trigger reintroduced, credential/action, mutation, extra job, or changed verdict)"
        )

    raw = source.lower()
    forbidden = {
        "secrets.": "App or secret credential reference",
        "github_token": "token credential reference",
        "create-github-app-token": "App token mint",
        "actions/checkout": "source checkout",
        "setup-dotnet": "engine build setup",
        "dotnet ": "engine build",
        "curl ": "REST call",
        "gh api": "API call",
        "reconcile --apply": "board mutation",
        "fsgg-coord": "coordination client invocation",
        "board clean": "false clean verdict",
        "successfully reconciled": "false reconciliation verdict",
    }
    for needle, description in forbidden.items():
        if needle in raw:
            findings.append(f"{description} is forbidden in the retired diagnostic")

    if findings:
        for finding in dict.fromkeys(findings):
            print(f"check-reconcile-concurrency-scope: FINDING — {finding}", file=sys.stderr)
        return 1
    print(
        "check-reconcile-concurrency-scope: OK — manual-only, credential-free retired diagnostic; "
        "board state not evaluated; V2 projection writer not activated."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())

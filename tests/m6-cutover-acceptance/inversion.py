#!/usr/bin/env python3
"""Execute one M6 cutover evidence inversion; a caught mutant exits nonzero."""

from __future__ import annotations

import argparse
import copy
import importlib.util
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("acceptance", ROOT / "scripts/m6-cutover-acceptance.py")
acceptance = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(acceptance)


def complete_candidate() -> dict:
    candidate = json.loads(
        (ROOT / "docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json").read_text()
    )
    sha = candidate["implementation"]["sha"]
    artifact = {
        "path": "tests/m6-cutover-acceptance/fixture-result.json",
        "sha256": "be4352a404fd5bf598acdd1e1f51e8a0e6252b65a59cb4910cddf5e04e772c88",
    }
    present = {row["family"] for row in candidate["test_results"]}
    for family in sorted(acceptance.REQUIRED_TESTS - present):
        candidate["test_results"].append({
            "family": family, "outcome": "pass", "implementation_sha": sha, "tree_sha": sha,
            "command": ["fixture", family], "expected_exit": 0, "observed_exit": 0,
            "counts": {"passed": 1, "failed": 0}, "stdout_sha256": "1" * 64,
            "artifact": artifact,
        })
    candidate["release"].update({
        "prepared_manifest_verified": True, "package_bytes_identical": True,
        "github_feed_observed": True, "nuget_feed_observed": True,
        "promoted": True, "adopted_and_pinned": True,
    })
    return candidate


MARKERS = {
    "lifecycle-old-reducer": "FSGG_COORD_LIFECYCLE_PROJECTION",
    "graphql-raw-envelope": "let private graphQlData",
    "route-v1-authority": "fsgg:delivery-route/v1",
    "review-prose-authority": "fsgg:independent-review:v1",
    "release-local-pack": "pack locally (dry run)",
}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--case", required=True, choices=sorted(acceptance.REQUIRED_MUTATIONS))
    args = parser.parse_args()
    candidate = complete_candidate()
    baseline = acceptance.validate(candidate, ROOT)
    if baseline:
        print(f"M6 inversion {args.case}: invalid baseline: {baseline[0]}", file=sys.stderr)
        return 2
    mutant = copy.deepcopy(candidate)
    subject = ROOT / "tests/m6-cutover-acceptance/.inversion-subject"
    try:
        if args.case in MARKERS:
            subject.write_text(MARKERS[args.case] + "\n", encoding="utf-8")
            mutant["deletion_inventory"]["absent_markers"].append({
                "marker": MARKERS[args.case],
                "roots": ["tests/m6-cutover-acceptance/.inversion-subject"],
            })
        elif args.case == "evidence-manifest-byte-drift":
            mutant["trx_archive"]["sha256"] = "0" * 64
        elif args.case == "acceptance-missing-binding":
            mutant["implementation"]["sha"] = "0" * 40
        failures = acceptance.validate(mutant, ROOT)
    finally:
        subject.unlink(missing_ok=True)
    if not failures:
        print(f"M6 inversion {args.case}: SURVIVED", file=sys.stderr)
        return 0
    print(f"M6 inversion {args.case}: rejected: {failures[0]}")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())

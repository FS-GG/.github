#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

python3 - "$ROOT" <<'PY'
import copy
import importlib.util
import json
from pathlib import Path
import sys

root = Path(sys.argv[1])
spec = importlib.util.spec_from_file_location("acceptance", root / "scripts/m6-cutover-acceptance.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
candidate = json.loads((root / "docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json").read_text())
artifact = {"path": "tests/m6-cutover-acceptance/fixture-result.json", "sha256": "be4352a404fd5bf598acdd1e1f51e8a0e6252b65a59cb4910cddf5e04e772c88"}
sha = candidate["implementation"]["sha"]
candidate["test_results"] = [
    {"family": name, "outcome": "pass", "implementation_sha": sha, "tree_sha": sha,
     "command": ["fixture", name], "expected_exit": 0, "observed_exit": 0,
     "counts": {"passed": 1, "failed": 0}, "stdout_sha256": "1" * 64, "artifact": artifact}
    for name in sorted(module.REQUIRED_TESTS)
]
candidate["mutation_results"] = [
    {"mutation": name, "outcome": "red", "implementation_sha": sha, "tree_sha": sha,
     "command": ["fixture", name], "expected_exit": "nonzero", "observed_exit": 1,
     "counts": {"red": 1}, "stdout_sha256": "2" * 64, "artifact": artifact}
    for name in sorted(module.REQUIRED_MUTATIONS)
]
candidate["release"].update({
    "prepared_manifest_verified": True,
    "package_bytes_identical": True,
    "github_feed_observed": True,
    "nuget_feed_observed": True,
    "promoted": True,
    "adopted_and_pinned": True,
})
candidate["live_acceptance"] = {
    "exact_main_sha": sha,
    "new_only_smoke": True,
    "same_class_open_issues": 0,
    "issue_2569_closed": True,
}
assert module.validate(candidate, root) == [], module.validate(candidate, root)

mutations = {
    "calendar-history-rewritten": lambda d: d["superseded_history"].update(disposition="passed"),
    "implementation-unbound": lambda d: d["implementation"].update(sha="0" * 40),
    "retired-path-returned": lambda d: d["deletion_inventory"]["absent_paths"].append("src/FS.GG.Coord.Core/StructuredDecision.fs"),
    "retired-marker-returned": lambda d: d["deletion_inventory"]["absent_markers"].append({"marker": "module StructuredDecision", "roots": ["src"]}),
    "census-byte-drift": lambda d: d["decision_census"].update(sha256="0" * 64),
    "seed-unbound": lambda d: d["lifecycle_seed"].update(sha256="0" * 64),
    "archive-unbound": lambda d: d["trx_archive"].update(sha256="0" * 64),
    "test-family-missing": lambda d: d.update(test_results=d["test_results"][1:]),
    "mutation-did-not-red": lambda d: d["mutation_results"][0].update(outcome="pass"),
    "release-not-promoted": lambda d: d["release"].update(promoted=False),
    "live-successor-open": lambda d: d["live_acceptance"].update(same_class_open_issues=1),
}
for name, mutate in mutations.items():
    changed = copy.deepcopy(candidate)
    mutate(changed)
    failures = module.validate(changed, root)
    assert failures, f"{name}: mutation unexpectedly passed"
    print(f"PASS {name}: {failures[0]}")
print(f"m6-cutover-acceptance fixture: positive plus {len(mutations)} fail-closed inversions passed")
PY

if python3 "$ROOT/scripts/m6-cutover-acceptance.py" \
  "$ROOT/docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json" --root "$ROOT" >/dev/null 2>&1; then
  echo "pending production evidence unexpectedly passed" >&2
  exit 1
fi
echo "m6-cutover-acceptance pending evidence: correctly blocked until tests/release/live bindings exist"

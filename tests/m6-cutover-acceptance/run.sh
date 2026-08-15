#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

python3 - "$ROOT" <<'PY'
import copy
import importlib.util
import json
from pathlib import Path
import sys
import tempfile

root = Path(sys.argv[1])
spec = importlib.util.spec_from_file_location("acceptance", root / "scripts/m6-cutover-acceptance.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
seed_spec = importlib.util.spec_from_file_location("seed_verifier", root / "scripts/verify-m6-live-intent-seed.py")
seed_module = importlib.util.module_from_spec(seed_spec)
seed_spec.loader.exec_module(seed_module)
candidate = json.loads((root / "docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json").read_text())
artifact = {"path": "tests/m6-cutover-acceptance/fixture-result.json", "sha256": "be4352a404fd5bf598acdd1e1f51e8a0e6252b65a59cb4910cddf5e04e772c88"}
sha = candidate["implementation"]["sha"]
candidate["test_results"] = [
    {"family": name, "outcome": "pass", "implementation_sha": sha, "tree_sha": sha,
     "command": ["fixture", name], "expected_exit": 0, "observed_exit": 0,
     "counts": {"passed": 1, "failed": 0}, "stdout_sha256": "1" * 64, "artifact": artifact}
    for name in sorted(module.REQUIRED_TESTS)
]
candidate["release"].update({
    "prepared_manifest_verified": True,
    "package_bytes_identical": True,
    "github_feed_observed": True,
    "nuget_feed_observed": True,
    "promoted": True,
    "adopted_and_pinned": True,
})
assert module.validate(candidate, root) == [], module.validate(candidate, root)

with tempfile.TemporaryDirectory() as directory:
    temporary = Path(directory)
    repository = temporary / "repo"
    repository.mkdir()
    external = temporary / "external.json"
    external.write_text("{}")
    (repository / "escape.json").symlink_to(external)
    path_failures = []
    assert module.relative_file(repository, "escape.json", "symlink", path_failures) is None
    assert path_failures == ["symlink: resolved path escapes repository: escape.json"], path_failures
    print(f"PASS symlink-escape: {path_failures[0]}")

seed = json.loads((root / "docs/reports/evidence/2026-08-15-m6-live-intent-seed.json").read_text())
replay = json.loads((root / "docs/reports/evidence/2026-08-15-m6-live-intent-seed-replay.json").read_text())
assert seed_module.validate(seed, replay, sha) == []
seed_mutations = {
    "seed-comment-missing": lambda value: value.update(comments=value["comments"][:-1]),
    "seed-marker-drift": lambda value: value["comments"][0].update(marker_sha256="0" * 64),
    "seed-pagination-incomplete": lambda value: value["board"].update(pagination_complete=False),
    "seed-second-pass-writes": lambda value: value["second_pass"].update(would_post=1),
    "seed-implementation-unbound": lambda value: value.update(implementation_sha="0" * 40),
}
for name, mutate in seed_mutations.items():
    changed = copy.deepcopy(replay)
    mutate(changed)
    seed_failures = seed_module.validate(seed, changed, sha)
    assert seed_failures, f"{name}: mutation unexpectedly passed"
    print(f"PASS {name}: {seed_failures[0]}")

mutations = {
    "calendar-history-rewritten": lambda d: d["superseded_history"].update(disposition="passed"),
    "implementation-unbound": lambda d: d["implementation"].update(sha="0" * 40),
    "retired-path-returned": lambda d: d["deletion_inventory"]["absent_paths"].append("src/FS.GG.Coord.Core/StructuredDecision.fs"),
    "retired-marker-returned": lambda d: d["deletion_inventory"]["absent_markers"].append({"marker": "module StructuredDecision", "roots": ["src"]}),
    "census-byte-drift": lambda d: d["decision_census"].update(sha256="0" * 64),
    "seed-unbound": lambda d: d["lifecycle_seed"].update(sha256="0" * 64),
    "archive-unbound": lambda d: d["trx_archive"].update(sha256="0" * 64),
    "engine-mutation-unbound": lambda d: d["engine_mutation_matrix"].update(sha256="0" * 64),
    "test-family-missing": lambda d: d.update(test_results=d["test_results"][1:]),
    "mutation-did-not-red": lambda d: d["mutation_results"][0].update(outcome="pass"),
    "release-not-promoted": lambda d: d["release"].update(promoted=False),
    "release-source-unrelated": lambda d: d["release"].update(source_sha="0" * 40),
    "live-main-unrelated": lambda d: d["live_acceptance"].update(verified_main_sha="0" * 40),
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

for inversion in \
  lifecycle-old-reducer graphql-raw-envelope route-v1-authority review-prose-authority \
  release-local-pack evidence-manifest-byte-drift acceptance-missing-binding; do
  set +e
  output="$(python3 "$ROOT/tests/m6-cutover-acceptance/inversion.py" --case "$inversion" 2>&1)"
  status=$?
  set -e
  [ "$status" -eq 1 ] || { echo "$inversion: expected rc=1, got $status: $output" >&2; exit 1; }
  case "$output" in
    "M6 inversion $inversion: rejected: "*) ;;
    *) echo "$inversion: unexpected output: $output" >&2; exit 1 ;;
  esac
  printf '%s\n' "$output"
done

python3 "$ROOT/scripts/m6-cutover-acceptance.py" \
  "$ROOT/docs/reports/evidence/2026-08-15-m6-cutover-acceptance.json" --root "$ROOT"

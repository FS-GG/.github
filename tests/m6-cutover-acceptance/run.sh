#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"

python3 - "$ROOT" <<'PY'
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
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

git_env = dict(os.environ, GIT_AUTHOR_NAME="M6 fixture", GIT_AUTHOR_EMAIL="m6-fixture@example.invalid",
               GIT_COMMITTER_NAME="M6 fixture", GIT_COMMITTER_EMAIL="m6-fixture@example.invalid",
               GIT_AUTHOR_DATE="2000-01-01T00:00:00Z", GIT_COMMITTER_DATE="2000-01-01T00:00:00Z")
empty_tree = subprocess.check_output(["git", "mktree"], cwd=root, input=b"").decode().strip()
unrelated_sha = subprocess.check_output(
    ["git", "commit-tree", empty_tree], cwd=root, input=b"M6 unrelated ancestry fixture\n", env=git_env
).decode().strip()

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
    "release-source-unrelated": lambda d: d["release"].update(source_sha=unrelated_sha),
    "live-main-unrelated": lambda d: d["live_acceptance"].update(verified_main_sha=unrelated_sha),
    "release-source-wrong-direction": lambda d: d["release"].update(source_sha=d["implementation"]["base_sha"]),
    "live-main-wrong-direction": lambda d: d["live_acceptance"].update(verified_main_sha=d["implementation"]["sha"]),
    "live-command-fabricated": lambda d: d["live_acceptance"].update(commands=[{"command": ["fabricated"], "observed_exit": 0, "stdout_sha256": "0" * 64}]),
    "live-successor-open": lambda d: d["live_acceptance"].update(same_class_open_issues=1),
}
for name, mutate in mutations.items():
    changed = copy.deepcopy(candidate)
    mutate(changed)
    failures = module.validate(changed, root)
    assert failures, f"{name}: mutation unexpectedly passed"
    print(f"PASS {name}: {failures[0]}")

terminal_source = root / "docs/reports/evidence/2026-08-15-m6-terminal-live-release.json"
terminal_mutations = {
    "live-count-drift": lambda d: d["live"]["commands"][1]["counts"].update(rows=0),
    "same-class-definition-drift": lambda d: d["live"]["same_class_searches"][0]["command"].__setitem__(-1, "nonsense"),
}
for name, mutate in terminal_mutations.items():
    changed_terminal = json.loads(terminal_source.read_text())
    mutate(changed_terminal)
    changed = copy.deepcopy(candidate)
    changed["live_acceptance"]["command_matrix_sha256"] = module.hashlib.sha256(
        json.dumps(changed_terminal["live"]["commands"], sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()
    changed["live_acceptance"]["same_class_searches_sha256"] = module.hashlib.sha256(
        json.dumps(changed_terminal["live"]["same_class_searches"], sort_keys=True, separators=(",", ":")).encode()
    ).hexdigest()
    with tempfile.TemporaryDirectory(dir=root / "tests/m6-cutover-acceptance") as directory:
        path = Path(directory) / "terminal.json"
        path.write_text(json.dumps(changed_terminal, sort_keys=True), encoding="utf-8")
        relative = str(path.relative_to(root))
        terminal_hash = module.digest(path)
        binding = {"path": relative, "sha256": terminal_hash}
        changed["release"]["evidence"] = binding
        changed["live_acceptance"]["evidence"] = binding
        live_row = next(row for row in changed["test_results"] if row["family"] == "live-new-only-smoke")
        live_row["artifact"] = binding
        live_row["stdout_sha256"] = terminal_hash
        failures = module.validate(changed, root)
        assert failures, f"{name}: mutation unexpectedly passed"
        print(f"PASS {name}: {failures[0]}")
print(f"m6-cutover-acceptance fixture: positive plus {len(mutations) + len(terminal_mutations)} fail-closed inversions passed")
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

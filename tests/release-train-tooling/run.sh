#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

dotnet fsi "$ROOT/scripts/release-train-audit.fsx" -- --selftest
dotnet fsi "$ROOT/scripts/release-train-verify.fsx" -- --selftest
dotnet fsi "$ROOT/scripts/release-train-workflows.fsx" -- --selftest
dotnet fsi "$ROOT/scripts/release-train-status.fsx" -- --selftest
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- selftest

WORK="$(mktemp -d "${TMPDIR:-/tmp}/release-train-tooling.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

mkdir -p "$WORK/repo/.github/workflows"
printf '%s\n' \
  'name: broken release' \
  'on: workflow_dispatch' \
  'permissions:' \
  '  contents: write' \
  '  id-token: write' \
  'jobs:' \
  '  publish:' \
  '    runs-on: ubuntu-latest' \
  '    steps:' \
  '      - uses: NuGet/login@v1' \
  '      - run: gh release create "$GITHUB_REF_NAME"' \
  > "$WORK/repo/.github/workflows/release.yml"

set +e
dotnet fsi "$ROOT/scripts/release-train-workflows.fsx" -- --repo "$WORK/repo" --json \
  > "$WORK/broken.json"
broken_rc=$?
set -e

if [ "$broken_rc" -ne 1 ]; then
  echo "expected checkout-free gh release fixture to exit 1, got $broken_rc" >&2
  exit 1
fi
jq -e '.results[0].errors[] | select(.rule == "github-release-repository")' \
  "$WORK/broken.json" >/dev/null

printf '%s\n' \
  'name: fixed release' \
  'on: workflow_dispatch' \
  'permissions:' \
  '  contents: write' \
  '  id-token: write' \
  'jobs:' \
  '  publish:' \
  '    runs-on: ubuntu-latest' \
  '    steps:' \
  '      - uses: NuGet/login@v1' \
  '      - env:' \
  '          GH_REPO: FS-GG/example' \
  '        run: gh release create "$GITHUB_REF_NAME"' \
  > "$WORK/repo/.github/workflows/release.yml"

dotnet fsi "$ROOT/scripts/release-train-workflows.fsx" -- --repo "$WORK/repo" --json \
  > "$WORK/fixed.json"
jq -e '([.results[].errors[]] | length) == 0' "$WORK/fixed.json" >/dev/null

printf '%s\n' \
  '{"repositories":[{"id":"producer","baselineTag":"v0.1.0","originMain":"abc","packages":[{"packageId":"Example"}]}]}' \
  > "$WORK/audit.json"
printf '%s\n' '{"results":[{"errors":[],"warnings":[]}]}' > "$WORK/workflows.json"

dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
  --run "$WORK/release-run.json" \
  --audit "$WORK/audit.json" \
  --workflows "$WORK/workflows.json" \
  --registry "$ROOT/registry/dependencies.yml" \
  > "$WORK/inspect.json"
jq -e '.nextAction.kind == "classify-release" or .nextAction.kind == "verify-packages" or .nextAction.kind == "verify-tag" or .nextAction.kind == "await-workflow" or .nextAction.kind == "publish"' \
  "$WORK/inspect.json" >/dev/null

dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance \
  --run "$WORK/release-run.json" \
  --release-id producer \
  --decision release-owed \
  --subject-commit abc \
  --evidence https://example.test/decision \
  > "$WORK/advance.json"
jq -e '[.kind, .missingReceipt] | length == 2' "$WORK/advance.json" >/dev/null

echo "release-train-tooling fixture: passed"

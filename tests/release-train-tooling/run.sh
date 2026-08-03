#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODE="${1:-all}"
case "$MODE" in
  all|workflow|state|core|partial|topology) ;;
  *) echo "usage: $0 [all|workflow|state|core|partial|topology]" >&2; exit 2 ;;
esac

if [ "$MODE" = all ] || [ "$MODE" = workflow ]; then
  dotnet fsi "$ROOT/scripts/release-train-audit.fsx" -- --selftest
  dotnet fsi "$ROOT/scripts/release-train-verify.fsx" -- --selftest
  dotnet fsi "$ROOT/scripts/release-train-workflows.fsx" -- --selftest
  dotnet fsi "$ROOT/scripts/release-train-status.fsx" -- --selftest
fi
if [ "$MODE" != workflow ]; then
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- selftest
fi

WORK="$(mktemp -d "${TMPDIR:-/tmp}/release-train-tooling.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

if [ "$MODE" = all ] || [ "$MODE" = workflow ]; then
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
fi

if [ "$MODE" != workflow ]; then
if [ "$MODE" = all ] || [ "$MODE" = state ] || [ "$MODE" = core ]; then
printf '%s\n' \
  '{"repositories":[{"id":"producer","baselineTag":"v0.1.0","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
  > "$WORK/audit.json"
printf '%s\n' '{"results":[{"errors":[],"warnings":[]}]}' > "$WORK/workflows.json"
printf '%s\n' \
  '{"releaseId":"producer","subjectCommit":"abc","workflowRun":"https://example.test/runs/1","conclusion":"success"}' \
  > "$WORK/workflow-receipt.json"

dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
  --run "$WORK/release-run.json" \
  --audit "$WORK/audit.json" \
  --workflows "$WORK/workflows.json" \
  --registry "$ROOT/registry/dependencies.yml" \
  > "$WORK/inspect.json"
jq -e '.nextAction.kind == "classify-release" or .nextAction.kind == "verify-packages" or .nextAction.kind == "verify-tag" or .nextAction.kind == "await-workflow" or .nextAction.kind == "publish"' \
  "$WORK/inspect.json" >/dev/null

printf '%s\n' \
  '{"releaseId":"producer","subjectCommit":"forged","workflowRun":"https://example.test/runs/forged","conclusion":"failure"}' \
  > "$WORK/failed-workflow-receipt.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance \
  --run "$WORK/release-run.json" --release-id producer --decision release-owed \
  --subject-commit abc --evidence https://example.test/decision \
  --workflow-receipt "$WORK/failed-workflow-receipt.json" > /dev/null
failed_receipt_rc=$?
set -e
if [ "$failed_receipt_rc" -ne 1 ]; then
  echo "expected failed/mismatched workflow receipt to fail closed, got $failed_receipt_rc" >&2
  exit 1
fi

dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance \
  --run "$WORK/release-run.json" \
  --release-id producer \
  --decision release-owed \
  --subject-commit abc \
  --evidence https://example.test/decision \
  --workflow-receipt "$WORK/workflow-receipt.json" \
  > "$WORK/advance.json"
jq -e '[.kind, .missingReceipt] | length == 2' "$WORK/advance.json" >/dev/null

printf '%s\n' \
  '{"name":"producer","subjectCommit":"abc","conclusion":"success","expectedPackages":1,"observedPackages":1,"tagCommit":"abc","tagMatchesExpectedCommit":true,"githubAvailable":true,"nugetAvailable":true,"packages":[{"payloadIdentical":true}]}' \
  > "$WORK/verification.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify \
  --run "$WORK/release-run.json" \
  --verification "$WORK/verification.json" \
  > "$WORK/verify.json"
jq -e '.kind == "verify-propagation"' "$WORK/verify.json" >/dev/null

jq '.registry.canonicalMerged = true | .releases[0].downstreamVerified = true' \
  "$WORK/release-run.json" > "$WORK/restart.json"
mv "$WORK/restart.json" "$WORK/release-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/release-run.json" > "$WORK/complete.json"
jq -e '.kind == "complete"' "$WORK/complete.json" >/dev/null
cp "$WORK/release-run.json" "$WORK/complete-before-replan.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/release-run.json" > "$WORK/complete-replan.json"
jq -e '.kind == "complete"' "$WORK/complete-replan.json" >/dev/null
cmp "$WORK/complete-before-replan.json" "$WORK/release-run.json"

printf '%s\n' ' ' >> "$WORK/audit.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/release-run.json" > /dev/null
stale_rc=$?
set -e
if [ "$stale_rc" -ne 1 ]; then
  echo "expected stale receipt to fail closed, got $stale_rc" >&2
  exit 1
fi

printf '%s\n' \
  '{"repositories":[{"id":"producer","baselineTag":"v0.1.0","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
  > "$WORK/audit.json"

fi

if [ "$MODE" = all ] || [ "$MODE" = state ] || [ "$MODE" = partial ]; then
printf '%s\n' \
  '{"repositories":[{"id":"producer","baselineTag":"v0.1.0","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
  > "$WORK/audit.json"
printf '%s\n' '{"results":[{"errors":[],"warnings":[]}]}' > "$WORK/workflows.json"
printf '%s\n' \
  '{"releaseId":"producer","subjectCommit":"abc","workflowRun":"https://example.test/runs/partial","conclusion":"success"}' \
  > "$WORK/partial-workflow-receipt.json"

for feed in org public; do
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
    --run "$WORK/$feed-run.json" \
    --audit "$WORK/audit.json" \
    --workflows "$WORK/workflows.json" \
    --registry "$ROOT/registry/dependencies.yml" \
    > "$WORK/$feed-inspect.json"
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance \
    --run "$WORK/$feed-run.json" --release-id producer --decision release-owed \
    --subject-commit abc --evidence https://example.test/decision --workflow-receipt "$WORK/partial-workflow-receipt.json" \
    > /dev/null
  if [ "$feed" = org ]; then github=true; nuget=false; expected=0; else github=false; nuget=true; expected=0; fi
  printf '{"name":"producer","subjectCommit":"abc","conclusion":"success","expectedPackages":1,"observedPackages":%s,"tagCommit":"abc","tagMatchesExpectedCommit":true,"githubAvailable":%s,"nugetAvailable":%s,"packages":[]}' "$expected" "$github" "$nuget" > "$WORK/$feed.json"
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/$feed-run.json" --verification "$WORK/$feed.json" > "$WORK/$feed-action.json"
  jq -e --arg state "$feed-only" '.kind == "human-escalation"' "$WORK/$feed-action.json" >/dev/null
  jq -e --arg state "$feed-only" '.releases[0].feedState == $state' "$WORK/$feed-run.json" >/dev/null
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/$feed-run.json" > "$WORK/$feed-replan.json"
  jq -e '.kind == "human-escalation" and .terminal == true' "$WORK/$feed-replan.json" >/dev/null
done
fi

if [ "$MODE" = all ] || [ "$MODE" = state ] || [ "$MODE" = topology ]; then
printf '%s\n' '{"results":[{"errors":[],"warnings":[]}]}' > "$WORK/workflows.json"
printf '%s\n' \
  'contracts:' \
  '  - id: core' \
  '    owner: upstream' \
  '    consumers: [downstream]' \
  > "$WORK/topology.yml"
printf '%s\n' \
  '{"repositories":[{"id":"upstream","baselineTag":"v1","originMain":"up","packages":[{}],"findings":[]},{"id":"downstream","baselineTag":"v1","originMain":"down","packages":[{}],"findings":[]}]}' \
  > "$WORK/topology-audit.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect --run "$WORK/topology-run.json" --audit "$WORK/topology-audit.json" --workflows "$WORK/workflows.json" --registry "$WORK/topology.yml" > /dev/null
jq -e '.releases[] | select(.id == "downstream" and .kind == "consumer" and .dependsOn == ["upstream"] and .coherentSets == [])' "$WORK/topology-run.json" >/dev/null
printf '%s\n' '{"releaseId":"upstream","subjectCommit":"up","workflowRun":"https://example.test/runs/up","conclusion":"success"}' > "$WORK/upstream-receipt.json"
printf '%s\n' '{"releaseId":"downstream","subjectCommit":"down","workflowRun":"https://example.test/runs/down","conclusion":"success"}' > "$WORK/downstream-receipt.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id upstream --decision release-owed --subject-commit up --evidence https://example.test/up --workflow-receipt "$WORK/upstream-receipt.json" > /dev/null
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id downstream --decision release-owed --subject-commit down --evidence https://example.test/down --workflow-receipt "$WORK/downstream-receipt.json" > /dev/null
jq '.releases |= map(.expectedPackages = 1 | .observedPackages = 1 | .tagCommit = .mainCommit | .downstreamVerified = true | .consumerEmbeddingVerified = true | if .id == "upstream" then .feedState = "none" | .artifactVerified = false else .feedState = "both-equivalent" | .artifactVerified = true end)' "$WORK/topology-run.json" > "$WORK/topology-ready.json"
mv "$WORK/topology-ready.json" "$WORK/topology-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/topology-run.json" > "$WORK/topology-action.json"
jq -e '.kind == "await-producer" and .releaseId == "downstream"' "$WORK/topology-action.json" >/dev/null || {
  jq . "$WORK/topology-action.json" >&2
  exit 1
}
fi

echo "release-train-tooling fixture: passed"
fi

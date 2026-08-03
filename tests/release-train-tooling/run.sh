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

verification_report() {
  local target="$1" name="$2" commit="$3" github="$4" nuget="$5" identical="$6"
  printf '{"schemaVersion":2,"generatedAt":"2026-08-03T00:00:00Z","name":"%s","expectedPackages":1,"observedPackages":1,"tag":"v1","expectedCommit":"%s","subjectCommit":"%s","tagCommit":"%s","tagMatchesExpectedCommit":true,"conclusion":"success","gitHubAvailable":%s,"nuGetAvailable":%s,"packages":[{"packageId":"Example","version":"1.0.0","gitHubUrl":"https://github.test/example","nuGetUrl":"https://nuget.test/example","gitHubArchiveSha256":"a","nuGetArchiveSha256":"b","payloadFiles":1,"payloadIdentical":%s,"differences":[],"gitHubAvailable":%s,"nuGetAvailable":%s}]}' "$name" "$commit" "$commit" "$commit" "$github" "$nuget" "$identical" "$github" "$nuget" > "$target"
}

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

verification_report "$WORK/verification.json" producer abc true true true
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify \
  --run "$WORK/release-run.json" \
  --verification "$WORK/verification.json" \
  > "$WORK/verify.json"
jq -e '.kind == "verify-propagation"' "$WORK/verify.json" >/dev/null

printf '%s\n' '{"kind":"propagation","releaseId":"producer","subjectCommit":"abc","conclusion":"failure"}' > "$WORK/failed-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/failed-propagation.json" > /dev/null
failed_completion_rc=$?
set -e
if [ "$failed_completion_rc" -ne 1 ]; then
  echo "expected failed completion receipt to fail closed, got $failed_completion_rc" >&2
  exit 1
fi
printf '%s\n' '{"kind":"propagation","releaseId":"producer","subjectCommit":"abc","conclusion":"success"}' > "$WORK/propagation.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/propagation.json" > "$WORK/propagation-action.json"
jq -e '.kind == "flip-registry"' "$WORK/propagation-action.json" >/dev/null
registry_sha="$(sha256sum "$ROOT/registry/dependencies.yml" | awk '{print $1}')"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$ROOT/registry/dependencies.yml" "$registry_sha" > "$WORK/registry-receipt.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/registry-receipt.json" > "$WORK/complete.json"
jq -e '.kind == "complete"' "$WORK/complete.json" >/dev/null
cp "$WORK/release-run.json" "$WORK/complete-before-restart.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/release-run.json" > "$WORK/complete-restart.json"
jq -e '.kind == "complete"' "$WORK/complete-restart.json" >/dev/null
cmp "$WORK/complete-before-restart.json" "$WORK/release-run.json"
cp "$WORK/release-run.json" "$WORK/complete-before-reimport.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/registry-receipt.json" > "$WORK/complete-reimport.json"
jq -e '.kind == "complete"' "$WORK/complete-reimport.json" >/dev/null
cmp "$WORK/complete-before-reimport.json" "$WORK/release-run.json"

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
  if [ "$feed" = org ]; then github=true; nuget=false; else github=false; nuget=true; fi
  verification_report "$WORK/$feed.json" producer abc "$github" "$nuget" false
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
verification_report "$WORK/upstream-none.json" upstream up false false false
verification_report "$WORK/downstream-both.json" downstream down true true true
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/upstream-none.json" --verification "$WORK/downstream-both.json" > "$WORK/topology-observed.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/topology-run.json" > "$WORK/topology-action.json"
jq -e '.kind == "await-producer" and .releaseId == "downstream"' "$WORK/topology-action.json" >/dev/null || {
  jq . "$WORK/topology-action.json" >&2
  exit 1
}
printf '%s\n' '{"kind":"consumer-embedding","releaseId":"downstream","subjectCommit":"down","producerId":"upstream","conclusion":"success"}' > "$WORK/consumer-embedding.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > /dev/null
blocked_embedding_rc=$?
set -e
if [ "$blocked_embedding_rc" -ne 1 ]; then
  echo "expected consumer embedding before verified producer to fail closed, got $blocked_embedding_rc" >&2
  exit 1
fi
verification_report "$WORK/upstream-both.json" upstream up true true true
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/upstream-both.json" > "$WORK/producer-live.json"
jq -e '.kind == "verify-consumer" and .releaseId == "downstream"' "$WORK/producer-live.json" >/dev/null
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > "$WORK/consumer-import.json"
jq -e '.kind == "verify-propagation"' "$WORK/consumer-import.json" >/dev/null
printf '%s\n' '{"kind":"propagation","releaseId":"upstream","subjectCommit":"up","conclusion":"success"}' > "$WORK/upstream-propagation.json"
printf '%s\n' '{"kind":"propagation","releaseId":"downstream","subjectCommit":"down","conclusion":"success"}' > "$WORK/downstream-propagation.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/upstream-propagation.json" > /dev/null
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/downstream-propagation.json" > "$WORK/topology-registry-action.json"
jq -e '.kind == "flip-registry"' "$WORK/topology-registry-action.json" >/dev/null
topology_sha="$(sha256sum "$WORK/topology.yml" | awk '{print $1}')"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$WORK/topology.yml" "$topology_sha" > "$WORK/topology-registry.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/topology-registry.json" > "$WORK/topology-complete.json"
jq -e '.kind == "complete"' "$WORK/topology-complete.json" >/dev/null
printf '%s\n' ' ' >> "$WORK/downstream-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/topology-run.json" > /dev/null
stale_completion_rc=$?
set -e
if [ "$stale_completion_rc" -ne 1 ]; then
  echo "expected stale completion receipt to fail closed, got $stale_completion_rc" >&2
  exit 1
fi
fi

echo "release-train-tooling fixture: passed"
fi

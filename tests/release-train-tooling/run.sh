#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
MODE="${1:-all}"
case "$MODE" in
  all|workflow|state|core|partial|topology|multiset) ;;
  *) echo "usage: $0 [all|workflow|state|core|partial|topology|multiset]" >&2; exit 2 ;;
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
  local differences='["payloads differ or a feed is unavailable"]'
  if [ "$identical" = true ]; then differences='[]'; fi
  printf '{"schemaVersion":2,"generatedAt":"2026-08-03T00:00:00Z","name":"%s","expectedPackages":1,"observedPackages":1,"tag":"v1","expectedCommit":"%s","subjectCommit":"%s","tagCommit":"%s","tagMatchesExpectedCommit":true,"conclusion":"success","gitHubAvailable":%s,"nuGetAvailable":%s,"packages":[{"packageId":"Example","version":"1.0.0","gitHubUrl":"https://github.test/example","nuGetUrl":"https://nuget.test/example","gitHubArchiveSha256":"a","nuGetArchiveSha256":"b","payloadFiles":1,"payloadIdentical":%s,"differences":%s,"gitHubAvailable":%s,"nuGetAvailable":%s}]}' "$name" "$commit" "$commit" "$commit" "$github" "$nuget" "$identical" "$differences" "$github" "$nuget" > "$target"
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
  '{"repositories":[{"id":"producer","baselineTag":"v1","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
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
registry_sha="$(sha256sum "$ROOT/registry/dependencies.yml" | awk '{print $1}')"
registry_topology="$(jq -r '.registry.canonicalTopologySha256' "$WORK/release-run.json")"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$ROOT/registry/dependencies.yml" "$registry_sha" "$registry_topology" > "$WORK/early-registry-receipt.json"
cp "$WORK/release-run.json" "$WORK/before-early-registry.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/early-registry-receipt.json" > /dev/null
early_registry_rc=$?
set -e
if [ "$early_registry_rc" -ne 1 ]; then
  echo "expected canonical registry import before flip-registry to fail closed, got $early_registry_rc" >&2
  exit 1
fi
cmp "$WORK/before-early-registry.json" "$WORK/release-run.json"

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
printf '%s\n' '{"kind":"propagation","releaseId":"producer","subjectCommit":"abc","conclusion":"success"}' > "$WORK/premature-propagation.json"
cp "$WORK/release-run.json" "$WORK/before-premature-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/premature-propagation.json" > /dev/null
premature_propagation_rc=$?
set -e
if [ "$premature_propagation_rc" -ne 1 ]; then
  echo "expected propagation before verify-propagation to fail closed, got $premature_propagation_rc" >&2
  exit 1
fi
cmp "$WORK/before-premature-propagation.json" "$WORK/release-run.json"

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
printf '%s\n' '{"kind":"propagation","releaseId":"wrong-release","subjectCommit":"abc","conclusion":"success"}' > "$WORK/wrong-release-propagation.json"
cp "$WORK/release-run.json" "$WORK/before-wrong-release-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/wrong-release-propagation.json" > /dev/null
wrong_release_propagation_rc=$?
set -e
if [ "$wrong_release_propagation_rc" -ne 1 ]; then
  echo "expected wrong-release propagation to fail closed, got $wrong_release_propagation_rc" >&2
  exit 1
fi
cmp "$WORK/before-wrong-release-propagation.json" "$WORK/release-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/propagation.json" > "$WORK/propagation-action.json"
jq -e '.kind == "flip-registry"' "$WORK/propagation-action.json" >/dev/null
cp "$WORK/release-run.json" "$WORK/after-propagation.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/propagation.json" > "$WORK/propagation-reimport-action.json"
jq -e '.kind == "flip-registry"' "$WORK/propagation-reimport-action.json" >/dev/null
cmp "$WORK/after-propagation.json" "$WORK/release-run.json"
jq '.note = "changed propagation"' "$WORK/propagation.json" > "$WORK/changed-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/changed-propagation.json" > /dev/null
changed_propagation_rc=$?
set -e
if [ "$changed_propagation_rc" -ne 1 ]; then
  echo "expected non-identical propagation replay to fail closed, got $changed_propagation_rc" >&2
  exit 1
fi
cmp "$WORK/after-propagation.json" "$WORK/release-run.json"
cp "$WORK/propagation.json" "$WORK/propagation.backup.json"
printf '%s\n' ' ' >> "$WORK/propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/propagation.json" > /dev/null
stale_propagation_rc=$?
set -e
if [ "$stale_propagation_rc" -ne 1 ]; then
  echo "expected stale recorded propagation replay to fail closed, got $stale_propagation_rc" >&2
  exit 1
fi
cmp "$WORK/after-propagation.json" "$WORK/release-run.json"
mv "$WORK/propagation.backup.json" "$WORK/propagation.json"
printf '%s\n' 'not the canonical registry' > "$WORK/not-canonical.yml"
not_canonical_sha="$(sha256sum "$WORK/not-canonical.yml" | awk '{print $1}')"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$WORK/not-canonical.yml" "$not_canonical_sha" "$registry_topology" > "$WORK/arbitrary-registry-receipt.json"
cp "$WORK/release-run.json" "$WORK/before-arbitrary-registry.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/arbitrary-registry-receipt.json" > /dev/null
arbitrary_registry_rc=$?
set -e
if [ "$arbitrary_registry_rc" -ne 1 ]; then
  echo "expected arbitrary /tmp registry receipt to fail closed, got $arbitrary_registry_rc" >&2
  exit 1
fi
cmp "$WORK/before-arbitrary-registry.json" "$WORK/release-run.json"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"stale","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$ROOT/registry/dependencies.yml" "$registry_topology" > "$WORK/stale-registry-receipt.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/stale-registry-receipt.json" > /dev/null
stale_registry_rc=$?
set -e
if [ "$stale_registry_rc" -ne 1 ]; then
  echo "expected stale canonical digest to fail closed, got $stale_registry_rc" >&2
  exit 1
fi
cmp "$WORK/before-arbitrary-registry.json" "$WORK/release-run.json"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$ROOT/registry/dependencies.yml" "$registry_sha" "$registry_topology" > "$WORK/registry-receipt.json"
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
jq '.note = "semantically changed after completion"' "$WORK/registry-receipt.json" > "$WORK/changed-registry-receipt.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/changed-registry-receipt.json" > /dev/null
changed_reimport_rc=$?
set -e
if [ "$changed_reimport_rc" -ne 1 ]; then
  echo "expected changed canonical receipt reimport after completion to fail closed, got $changed_reimport_rc" >&2
  exit 1
fi
cmp "$WORK/complete-before-reimport.json" "$WORK/release-run.json"
cp "$WORK/registry-receipt.json" "$WORK/registry-receipt.backup.json"
printf '%s\n' ' ' >> "$WORK/registry-receipt.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/release-run.json" --receipt "$WORK/registry-receipt.json" > /dev/null
stale_reimport_rc=$?
set -e
if [ "$stale_reimport_rc" -ne 1 ]; then
  echo "expected stale recorded canonical receipt reimport after completion to fail closed, got $stale_reimport_rc" >&2
  exit 1
fi
cmp "$WORK/complete-before-reimport.json" "$WORK/release-run.json"
mv "$WORK/registry-receipt.backup.json" "$WORK/registry-receipt.json"

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
  '{"repositories":[{"id":"producer","baselineTag":"v1","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
  > "$WORK/audit.json"

fi

if [ "$MODE" = all ] || [ "$MODE" = state ] || [ "$MODE" = partial ]; then
printf '%s\n' \
  '{"repositories":[{"id":"producer","baselineTag":"v1","originMain":"abc","packages":[{"packageId":"Example"}],"findings":[]}]}' \
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
cp "$WORK/topology-run.json" "$WORK/before-wrong-order-classification.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id downstream --decision release-owed --subject-commit down --evidence https://example.test/down --workflow-receipt "$WORK/downstream-receipt.json" > /dev/null
wrong_order_classification_rc=$?
set -e
if [ "$wrong_order_classification_rc" -ne 1 ]; then
  echo "expected downstream classification while upstream is current to fail closed, got $wrong_order_classification_rc" >&2
  exit 1
fi
cmp "$WORK/before-wrong-order-classification.json" "$WORK/topology-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id upstream --decision release-owed --subject-commit up --evidence https://example.test/up --workflow-receipt "$WORK/upstream-receipt.json" > /dev/null
cp "$WORK/topology-run.json" "$WORK/after-upstream-classification.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id upstream --decision release-owed --subject-commit up --evidence https://example.test/up --workflow-receipt "$WORK/upstream-receipt.json" > /dev/null
cmp "$WORK/after-upstream-classification.json" "$WORK/topology-run.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id upstream --decision release-owed --subject-commit up --evidence https://example.test/changed --workflow-receipt "$WORK/upstream-receipt.json" > /dev/null
changed_classification_rc=$?
set -e
if [ "$changed_classification_rc" -ne 1 ]; then
  echo "expected changed classification replay to fail closed, got $changed_classification_rc" >&2
  exit 1
fi
cmp "$WORK/after-upstream-classification.json" "$WORK/topology-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/topology-run.json" --release-id downstream --decision release-owed --subject-commit down --evidence https://example.test/down --workflow-receipt "$WORK/downstream-receipt.json" > /dev/null
verification_report "$WORK/upstream-none.json" upstream up false false false
verification_report "$WORK/downstream-both.json" downstream down true true true
cp "$WORK/topology-run.json" "$WORK/before-wrong-release-verification.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/downstream-both.json" > /dev/null
wrong_release_verification_rc=$?
set -e
if [ "$wrong_release_verification_rc" -ne 1 ]; then
  echo "expected downstream verification while upstream is current to fail closed, got $wrong_release_verification_rc" >&2
  exit 1
fi
cmp "$WORK/before-wrong-release-verification.json" "$WORK/topology-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/upstream-none.json" > "$WORK/topology-observed.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- plan --run "$WORK/topology-run.json" > "$WORK/topology-action.json"
jq -e '.kind == "publish" and .releaseId == "upstream"' "$WORK/topology-action.json" >/dev/null || {
  jq . "$WORK/topology-action.json" >&2
  exit 1
}
printf '%s\n' '{"kind":"consumer-embedding","releaseId":"downstream","subjectCommit":"down","producerId":"upstream","conclusion":"success"}' > "$WORK/consumer-embedding.json"
cp "$WORK/topology-run.json" "$WORK/before-blocked-embedding.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > /dev/null
blocked_embedding_rc=$?
set -e
if [ "$blocked_embedding_rc" -ne 1 ]; then
  echo "expected consumer embedding before verified producer to fail closed, got $blocked_embedding_rc" >&2
  exit 1
fi
cmp "$WORK/before-blocked-embedding.json" "$WORK/topology-run.json"
verification_report "$WORK/upstream-both.json" upstream up true true true
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/upstream-both.json" > "$WORK/producer-live.json"
jq -e '.kind == "verify-consumer" and .releaseId == "downstream"' "$WORK/producer-live.json" >/dev/null
printf '%s\n' '{"kind":"consumer-embedding","releaseId":"upstream","subjectCommit":"up","producerId":"downstream","conclusion":"success"}' > "$WORK/wrong-consumer-embedding.json"
cp "$WORK/topology-run.json" "$WORK/before-wrong-consumer-embedding.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/wrong-consumer-embedding.json" > /dev/null
wrong_consumer_embedding_rc=$?
set -e
if [ "$wrong_consumer_embedding_rc" -ne 1 ]; then
  echo "expected wrong consumer embedding receipt to fail closed, got $wrong_consumer_embedding_rc" >&2
  exit 1
fi
cmp "$WORK/before-wrong-consumer-embedding.json" "$WORK/topology-run.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > "$WORK/consumer-import.json"
jq -e '.kind == "verify-packages" and .releaseId == "downstream"' "$WORK/consumer-import.json" >/dev/null
cp "$WORK/topology-run.json" "$WORK/after-consumer-embedding.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > "$WORK/consumer-reimport.json"
jq -e '.kind == "verify-packages" and .releaseId == "downstream"' "$WORK/consumer-reimport.json" >/dev/null
cmp "$WORK/after-consumer-embedding.json" "$WORK/topology-run.json"
jq '.note = "changed embedding"' "$WORK/consumer-embedding.json" > "$WORK/changed-consumer-embedding.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/changed-consumer-embedding.json" > /dev/null
changed_consumer_embedding_rc=$?
set -e
if [ "$changed_consumer_embedding_rc" -ne 1 ]; then
  echo "expected changed consumer embedding replay to fail closed, got $changed_consumer_embedding_rc" >&2
  exit 1
fi
cmp "$WORK/after-consumer-embedding.json" "$WORK/topology-run.json"
cp "$WORK/consumer-embedding.json" "$WORK/consumer-embedding.backup.json"
printf '%s\n' ' ' >> "$WORK/consumer-embedding.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/consumer-embedding.json" > /dev/null
stale_consumer_embedding_rc=$?
set -e
if [ "$stale_consumer_embedding_rc" -ne 1 ]; then
  echo "expected stale recorded consumer embedding replay to fail closed, got $stale_consumer_embedding_rc" >&2
  exit 1
fi
cmp "$WORK/after-consumer-embedding.json" "$WORK/topology-run.json"
mv "$WORK/consumer-embedding.backup.json" "$WORK/consumer-embedding.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/downstream-both.json" > "$WORK/downstream-live.json"
jq -e '.kind == "verify-propagation" and .releaseId == "upstream"' "$WORK/downstream-live.json" >/dev/null
cp "$WORK/topology-run.json" "$WORK/after-downstream-verification.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/downstream-both.json" > "$WORK/downstream-verification-replay.json"
jq -e '.kind == "verify-propagation" and .releaseId == "upstream"' "$WORK/downstream-verification-replay.json" >/dev/null
cmp "$WORK/after-downstream-verification.json" "$WORK/topology-run.json"
jq '.note = "changed verification"' "$WORK/downstream-both.json" > "$WORK/changed-downstream-verification.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/changed-downstream-verification.json" > /dev/null
changed_downstream_verification_rc=$?
set -e
if [ "$changed_downstream_verification_rc" -ne 1 ]; then
  echo "expected changed verification replay to fail closed, got $changed_downstream_verification_rc" >&2
  exit 1
fi
cmp "$WORK/after-downstream-verification.json" "$WORK/topology-run.json"
cp "$WORK/downstream-both.json" "$WORK/downstream-both.backup.json"
printf '%s\n' ' ' >> "$WORK/downstream-both.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/topology-run.json" --verification "$WORK/downstream-both.json" > /dev/null
stale_downstream_verification_rc=$?
set -e
if [ "$stale_downstream_verification_rc" -ne 1 ]; then
  echo "expected stale recorded verification replay to fail closed, got $stale_downstream_verification_rc" >&2
  exit 1
fi
cmp "$WORK/after-downstream-verification.json" "$WORK/topology-run.json"
mv "$WORK/downstream-both.backup.json" "$WORK/downstream-both.json"
printf '%s\n' '{"kind":"propagation","releaseId":"upstream","subjectCommit":"up","conclusion":"success"}' > "$WORK/upstream-propagation.json"
printf '%s\n' '{"kind":"propagation","releaseId":"downstream","subjectCommit":"down","conclusion":"success"}' > "$WORK/downstream-propagation.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/upstream-propagation.json" > /dev/null
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/topology-run.json" --receipt "$WORK/downstream-propagation.json" > "$WORK/topology-registry-action.json"
jq -e '.kind == "flip-registry"' "$WORK/topology-registry-action.json" >/dev/null
topology_sha="$(sha256sum "$WORK/topology.yml" | awk '{print $1}')"
topology_fingerprint="$(jq -r '.registry.canonicalTopologySha256' "$WORK/topology-run.json")"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$WORK/topology.yml" "$topology_sha" "$topology_fingerprint" > "$WORK/topology-registry.json"
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

if [ "$MODE" = all ] || [ "$MODE" = state ] || [ "$MODE" = multiset ]; then
MULTI="$WORK/github-multi"
mkdir -p "$MULTI/.github/workflows" "$MULTI/registry" \
  "$MULTI/src/FS.GG.Coord.Cli" "$MULTI/src/FS.GG.Drivers" "$MULTI/src/FS.GG.Kit" \
  "$MULTI/scripts/NewSddWorkspace"
printf '%s\n' 'repos:' '  - { id: .github, full: FS-GG/.github, role: authority }' > "$MULTI/registry/repos.yml"
printf '%s\n' \
  'contracts:' \
  '  - id: coord-engine' \
  '    owner: github' \
  '    consumers: []' \
  '  - id: new-sdd-workspace' \
  '    owner: github' \
  '    consumers: []' \
  > "$MULTI/registry/dependencies.yml"

make_packable() {
  local project="$1" package="$2" version="$3"
  printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><IsPackable>true</IsPackable><PackageId>%s</PackageId><Version>%s</Version></PropertyGroup></Project>\n' \
    "$package" "$version" > "$MULTI/$project"
}
make_release_workflow() {
  local name="$1" pattern="$2" project="$3"
  printf '%s\n' \
    "name: release-$name" \
    'on:' \
    '  push:' \
    "    tags: ['$pattern']" \
    'jobs:' \
    '  publish:' \
    '    runs-on: ubuntu-latest' \
    '    steps:' \
    "      - run: dotnet pack $project" \
    > "$MULTI/.github/workflows/release-$name.yml"
}
make_packable 'src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj' 'FS.GG.Coord.Cli' '0.18.0'
make_packable 'src/FS.GG.Drivers/FS.GG.Drivers.csproj' 'FS.GG.Drivers' '0.16.0'
make_packable 'src/FS.GG.Kit/FS.GG.Kit.csproj' 'FS.GG.Kit' '0.35.0'
make_packable 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj' 'FS.GG.NewSddWorkspace' '0.8.0'
make_release_workflow 'coord-engine' 'coord-engine/v*' 'src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj'
make_release_workflow 'drivers' 'drivers/v*' 'src/FS.GG.Drivers/FS.GG.Drivers.csproj'
make_release_workflow 'kit' 'kit/v*' 'src/FS.GG.Kit/FS.GG.Kit.csproj'
make_release_workflow 'new-sdd-workspace' 'new-sdd-workspace/v*' 'scripts/NewSddWorkspace/NewSddWorkspace.fsproj'

git -C "$MULTI" init -q -b main
git -C "$MULTI" config user.name fixture
git -C "$MULTI" config user.email fixture@example.test
git -C "$MULTI" add .
git -C "$MULTI" commit -q -m baseline
multi_commit="$(git -C "$MULTI" rev-parse HEAD)"
git -C "$MULTI" tag coord-engine/v0.18.0
git -C "$MULTI" tag drivers/v0.15.0
git -C "$MULTI" tag kit/v0.35.0
git -C "$MULTI" tag new-sdd-workspace/v0.8.0
git -C "$MULTI" update-ref refs/remotes/origin/main "$multi_commit"

dotnet fsi "$ROOT/scripts/release-train-audit.fsx" -- --root "$MULTI" --repo .github --json --output "$WORK/multiset-audit.json" > /dev/null
jq -e '
  .schemaVersion == 2
  and (.repositories[0].releaseSets as $sets
  | ($sets | length) == 4
  and ($sets[] | select(.id == ".github:drivers") | .packages | length) == 1
  and ($sets[] | select(.id == ".github:drivers") | .expectedTags) == ["drivers/v0.16.0"]
  and ($sets[] | select(.id == ".github:drivers") | .baselineTag) == "drivers/v0.15.0"
  and ($sets[] | select(.id == ".github:kit") | .expectedTags) == ["kit/v0.35.0"])
' "$WORK/multiset-audit.json" >/dev/null
printf '%s\n' '{"results":[{"errors":[],"warnings":[]}]}' > "$WORK/multiset-workflows.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
  --run "$WORK/multiset-run.json" \
  --audit "$WORK/multiset-audit.json" \
  --workflows "$WORK/multiset-workflows.json" \
  --registry "$MULTI/registry/dependencies.yml" \
  > /dev/null
multi_registry_sha="$(sha256sum "$MULTI/registry/dependencies.yml" | awk '{print $1}')"
multi_registry_topology="$(jq -r '.registry.canonicalTopologySha256' "$WORK/multiset-run.json")"
printf '{"kind":"canonical-registry","registryPath":"%s","registrySha256":"%s","registryTopologySha256":"%s","canonicalMerged":true,"projectionCurrent":true,"conclusion":"success"}' "$MULTI/registry/dependencies.yml" "$multi_registry_sha" "$multi_registry_topology" > "$WORK/multiset-early-registry.json"
cp "$WORK/multiset-run.json" "$WORK/multiset-before-early-registry.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/multiset-run.json" --receipt "$WORK/multiset-early-registry.json" > /dev/null
multiset_early_registry_rc=$?
set -e
if [ "$multiset_early_registry_rc" -ne 1 ]; then
  echo "expected four-set canonical registry import before classification to fail closed, got $multiset_early_registry_rc" >&2
  exit 1
fi
cmp "$WORK/multiset-before-early-registry.json" "$WORK/multiset-run.json"
printf '{"kind":"propagation","releaseId":".github:drivers","subjectCommit":"%s","conclusion":"success"}' "$multi_commit" > "$WORK/multiset-early-propagation.json"
cp "$WORK/multiset-run.json" "$WORK/multiset-before-early-propagation.json"
set +e
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- import --run "$WORK/multiset-run.json" --receipt "$WORK/multiset-early-propagation.json" > /dev/null
multiset_early_propagation_rc=$?
set -e
if [ "$multiset_early_propagation_rc" -ne 1 ]; then
  echo "expected four-set propagation before verification to fail closed, got $multiset_early_propagation_rc" >&2
  exit 1
fi
cmp "$WORK/multiset-before-early-propagation.json" "$WORK/multiset-run.json"
jq -e '
  .schemaVersion == 2
  and (.releases | length) == 4
  and (.releases[] | select(.id == ".github:drivers") | {tag,baselineTag,expectedPackages,packages,expectedArtifacts})
      == {tag:"drivers/v0.16.0",baselineTag:"drivers/v0.15.0",expectedPackages:1,packages:["FS.GG.Drivers"],expectedArtifacts:[{packageId:"FS.GG.Drivers",version:"0.16.0"}]}
  and (.releases[] | select(.id == ".github:kit") | .tag) == "kit/v0.35.0"
  and (.releases[] | select(.id == ".github:coord-engine") | .coherentSets) == ["coord-engine"]
  and (.releases[] | select(.id == ".github:new-sdd-workspace") | .coherentSets) == ["new-sdd-workspace"]
' "$WORK/multiset-run.json" >/dev/null
jq '.repositories[0].releaseSets |= map(select(.id == ".github:drivers"))' "$WORK/multiset-audit.json" > "$WORK/drivers-audit.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
  --run "$WORK/drivers-run.json" \
  --audit "$WORK/drivers-audit.json" \
  --workflows "$WORK/multiset-workflows.json" \
  --registry "$MULTI/registry/dependencies.yml" \
  > /dev/null
jq -e '.releases | length == 1 and .[0].id == ".github:drivers" and .[0].expectedPackages == 1' "$WORK/drivers-run.json" >/dev/null
printf '{"releaseId":".github:drivers","subjectCommit":"%s","workflowRun":"https://example.test/runs/drivers","conclusion":"success"}' "$multi_commit" > "$WORK/drivers-workflow-receipt.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/drivers-run.json" --release-id .github:drivers --decision release-owed --subject-commit "$multi_commit" --evidence https://example.test/drivers-decision --workflow-receipt "$WORK/drivers-workflow-receipt.json" > /dev/null
printf '{"schemaVersion":2,"generatedAt":"2026-08-03T00:00:00Z","name":".github:drivers","expectedPackages":1,"observedPackages":1,"tag":"drivers/v0.16.0","expectedCommit":"%s","subjectCommit":"%s","tagCommit":"%s","tagMatchesExpectedCommit":true,"conclusion":"success","gitHubAvailable":true,"nuGetAvailable":true,"packages":[{"packageId":"FS.GG.Drivers","version":"0.16.0","gitHubUrl":"https://github.test/drivers","nuGetUrl":"https://nuget.test/drivers","gitHubArchiveSha256":"a","nuGetArchiveSha256":"b","payloadFiles":1,"payloadIdentical":true,"differences":[],"gitHubAvailable":true,"nuGetAvailable":true}]}' \
  "$multi_commit" "$multi_commit" "$multi_commit" > "$WORK/correct-drivers.json"
jq '.tag = "kit/v0.35.0"' "$WORK/correct-drivers.json" > "$WORK/wrong-set-tag.json"
jq '.packages[0].packageId = "FS.GG.Kit" | .packages[0].version = "0.35.0"' "$WORK/correct-drivers.json" > "$WORK/wrong-package-id.json"
jq '.packages[0].version = "0.15.0"' "$WORK/correct-drivers.json" > "$WORK/wrong-package-version.json"
jq '.packages = [] | .expectedPackages = 0 | .observedPackages = 0' "$WORK/correct-drivers.json" > "$WORK/missing-package.json"
jq '.packages += [.packages[0]] | .expectedPackages = 2 | .observedPackages = 2' "$WORK/correct-drivers.json" > "$WORK/duplicate-package.json"
jq '.packages += [(.packages[0] | .packageId = "FS.GG.Kit" | .version = "0.35.0")] | .expectedPackages = 2 | .observedPackages = 2' "$WORK/correct-drivers.json" > "$WORK/extra-package.json"
jq '.expectedPackages = 99' "$WORK/correct-drivers.json" > "$WORK/forged-expected-count.json"
jq '.observedPackages = 99' "$WORK/correct-drivers.json" > "$WORK/forged-observed-count.json"
jq '.nuGetAvailable = false' "$WORK/correct-drivers.json" > "$WORK/contradictory-org-only.json"
jq '.gitHubAvailable = false' "$WORK/correct-drivers.json" > "$WORK/contradictory-public-only.json"
jq '.packages[0].gitHubAvailable = false | .packages[0].payloadIdentical = false' "$WORK/correct-drivers.json" > "$WORK/contradictory-disagree.json"
jq '.gitHubAvailable = false | .nuGetAvailable = false' "$WORK/correct-drivers.json" > "$WORK/contradictory-unavailable.json"
jq '.gitHubAvailable = false | .nuGetAvailable = false | .packages[0].gitHubAvailable = false | .packages[0].nuGetAvailable = false' "$WORK/correct-drivers.json" > "$WORK/unavailable-equivalent.json"
jq '.packages[0].differences = ["drivers/driver-skill-manifest.json differs"]' "$WORK/correct-drivers.json" > "$WORK/identical-with-differences.json"
jq '.packages[0].payloadIdentical = false' "$WORK/correct-drivers.json" > "$WORK/nonidentical-without-differences.json"
jq '.packages[0].differences = "not-an-array"' "$WORK/correct-drivers.json" > "$WORK/nonarray-differences.json"
jq '.packages[0].differences = [""]' "$WORK/correct-drivers.json" > "$WORK/empty-difference.json"
for invalid in wrong-set-tag wrong-package-id wrong-package-version missing-package duplicate-package extra-package forged-expected-count forged-observed-count contradictory-org-only contradictory-public-only contradictory-disagree contradictory-unavailable unavailable-equivalent identical-with-differences nonidentical-without-differences nonarray-differences empty-difference; do
  cp "$WORK/drivers-run.json" "$WORK/multiset-before-invalid.json"
  set +e
  dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/drivers-run.json" --verification "$WORK/$invalid.json" > /dev/null
  invalid_rc=$?
  set -e
  if [ "$invalid_rc" -ne 1 ]; then
    echo "expected $invalid Drivers receipt to fail closed, got $invalid_rc" >&2
    exit 1
  fi
  cmp "$WORK/multiset-before-invalid.json" "$WORK/drivers-run.json"
done
cp "$WORK/drivers-run.json" "$WORK/multiset-disagree-run.json"
jq '.packages[0].payloadIdentical = false | .packages[0].differences = ["drivers/driver-skill-manifest.json differs"]' "$WORK/correct-drivers.json" > "$WORK/disagree-drivers.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/multiset-disagree-run.json" --verification "$WORK/disagree-drivers.json" > /dev/null
jq -e '.releases[] | select(.id == ".github:drivers" and .expectedPackages == 1 and .observedPackages == 1 and .artifactVerified == false and .feedState == "disagree")' "$WORK/multiset-disagree-run.json" >/dev/null
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/drivers-run.json" --verification "$WORK/correct-drivers.json" > /dev/null
jq -e '.releases[] | select(.id == ".github:drivers" and .expectedPackages == 1 and .observedPackages == 1 and .artifactVerified == true and .feedState == "both-equivalent")' "$WORK/drivers-run.json" >/dev/null

printf '%s\n' \
  '{"repositories":[{"id":"feed-multi","baselineTag":"v1","originMain":"feed-commit","packages":[{"packageId":"Feed.One","version":"1.0.0"},{"packageId":"Feed.Two","version":"2.0.0"}],"findings":[]}]}' \
  > "$WORK/feed-multi-audit.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- inspect \
  --run "$WORK/feed-multi-run.json" \
  --audit "$WORK/feed-multi-audit.json" \
  --workflows "$WORK/multiset-workflows.json" \
  --registry "$MULTI/registry/dependencies.yml" \
  > /dev/null
printf '%s\n' '{"releaseId":"feed-multi","subjectCommit":"feed-commit","workflowRun":"https://example.test/runs/feed-multi","conclusion":"success"}' > "$WORK/feed-multi-workflow-receipt.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- advance --run "$WORK/feed-multi-run.json" --release-id feed-multi --decision release-owed --subject-commit feed-commit --evidence https://example.test/feed-multi-decision --workflow-receipt "$WORK/feed-multi-workflow-receipt.json" > /dev/null
printf '%s\n' \
  '{"schemaVersion":2,"generatedAt":"2026-08-03T00:00:00Z","name":"feed-multi","expectedPackages":2,"observedPackages":2,"tag":"v1","expectedCommit":"feed-commit","subjectCommit":"feed-commit","tagCommit":"feed-commit","tagMatchesExpectedCommit":true,"conclusion":"success","gitHubAvailable":true,"nuGetAvailable":true,"packages":[{"packageId":"Feed.One","version":"1.0.0","gitHubUrl":"https://github.test/one","nuGetUrl":"https://nuget.test/one","gitHubArchiveSha256":"a","nuGetArchiveSha256":"a","payloadFiles":1,"payloadIdentical":true,"differences":[],"gitHubAvailable":true,"nuGetAvailable":true},{"packageId":"Feed.Two","version":"2.0.0","gitHubUrl":"https://github.test/two","nuGetUrl":"https://nuget.test/two","gitHubArchiveSha256":"b","nuGetArchiveSha256":"b","payloadFiles":1,"payloadIdentical":true,"differences":[],"gitHubAvailable":true,"nuGetAvailable":true}]}' \
  > "$WORK/feed-multi-correct.json"
dotnet fsi "$ROOT/scripts/release-train-state.fsx" -- verify --run "$WORK/feed-multi-run.json" --verification "$WORK/feed-multi-correct.json" > /dev/null
jq -e '.releases[] | select(.id == "feed-multi" and .expectedPackages == 2 and .observedPackages == 2 and .artifactVerified == true and .feedState == "both-equivalent")' "$WORK/feed-multi-run.json" >/dev/null
fi

echo "release-train-tooling fixture: passed"
fi

#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
itf_root="$(mktemp -d)"
trap 'rm -rf -- "$itf_root"' EXIT
quint typecheck "$model"
quint test "$model_test" --backend typescript \
  --match '^(success|lost|mismatch|cancellation|cleanupFailureCase|stale|duplicateStore|duplicateProcess|lateDeadline|lateCancellation)$' \
  --max-samples 1 --out-itf "$itf_root/{test}-{seq}.itf.json" | tee "$itf_root/quint-tests.log"
grep -E '^  10 passing \(' "$itf_root/quint-tests.log"
test "$(find "$itf_root" -maxdepth 1 -name '*.itf.json' -type f | wc -l)" -eq 10
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode
dotnet run --project "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode
runner_tool="${PERSISTENT_V3_RUNNER_TOOL:-$(type -P quint)}"
PERSISTENT_V3_RUNNER_ITF_ROOT="$itf_root" \
PERSISTENT_V3_RUNNER_MODEL="$model" \
PERSISTENT_V3_RUNNER_TOOL="$runner_tool" \
PERSISTENT_V3_RUNNER_ADAPTER="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/Runner.fs" \
PERSISTENT_V3_RUNNER_PROFILE="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" \
PERSISTENT_V3_RUNNER_PRODUCTION_DLL="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" \
dotnet run --project "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure-mutant/PersistentV3.ImageClosure.Mutant.fsproj" --locked-mode
set +e
dotnet run --project "$repo_root/tests/telemetry-collector-persistent-v3-image-closure-mutant/PersistentV3.ImageClosure.Mutant.fsproj" --no-restore
mutant_code=$?
set -e
test "$mutant_code" -eq 1
set +e
dotnet run --project "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3.ImageClosure.fsproj" --no-restore -- "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" >"$itf_root/production.stdout" 2>"$itf_root/production.stderr"
code=$?
set -e
test "$code" -eq 2
test ! -s "$itf_root/production.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/production.stderr"
cli="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll"
printf '%s' '{"IdentityClass":"synthetic-test","IdentityClass":"synthetic-test"}' >"$itf_root/duplicate.json"
set +e
dotnet "$cli" "$itf_root/duplicate.json" >"$itf_root/duplicate.stdout" 2>"$itf_root/duplicate.stderr"
duplicate_code=$?
set -e
test "$duplicate_code" -eq 3
test ! -s "$itf_root/duplicate.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-is-not-closed' "$itf_root/duplicate.stderr"
truncate -s 4194305 "$itf_root/oversize.json"
set +e
dotnet "$cli" "$itf_root/oversize.json" >"$itf_root/oversize.stdout" 2>"$itf_root/oversize.stderr"
oversize_code=$?
set -e
test "$oversize_code" -eq 3
test ! -s "$itf_root/oversize.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-size' "$itf_root/oversize.stderr"

#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
itf_root="$(mktemp -d)"
trap 'rm -rf -- "$itf_root"' EXIT
quint typecheck "$model"
quint test "$model_test" --backend typescript \
  --match '^(success|lost|mismatch|cancellation|cleanupFailureCase|stale|duplicateStore|duplicateProcess|lateDeadline|lateCancellation|c4Success|c4Stale|c4LateDeadline|c4LateCancellation|c4CleanupFailure|c4SealFailure)$' \
  --max-samples 1 --out-itf "$itf_root/{test}-{seq}.itf.json" | tee "$itf_root/quint-tests.log"
grep -E '^  16 passing \(' "$itf_root/quint-tests.log"
test "$(find "$itf_root" -maxdepth 1 -name '*.itf.json' -type f | wc -l)" -eq 16
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.Tests.dll"
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
runner_tool="${PERSISTENT_V3_RUNNER_TOOL:-$(type -P quint)}"
PERSISTENT_V3_RUNNER_ITF_ROOT="$itf_root" \
PERSISTENT_V3_RUNNER_MODEL="$model" \
PERSISTENT_V3_RUNNER_TOOL="$runner_tool" \
PERSISTENT_V3_RUNNER_ADAPTER="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/Runner.fs" \
PERSISTENT_V3_RUNNER_PROFILE="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" \
PERSISTENT_V3_RUNNER_PRODUCTION_DLL="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" \
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/bin/Debug/net10.0/PersistentV3.ImageClosure.Correspondence.dll"
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
set +e
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/bin/Debug/net10.0/PersistentV3.ImageClosure.Mutant.dll"
mutant_code=$?
set -e
test "$mutant_code" -eq 1
set +e
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" >"$itf_root/production.stdout" 2>"$itf_root/production.stderr"
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

# Explicit command preserves the immutable placeholder before any effect.
set +e
dotnet "$cli" prepare --selection "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" --trusted-native-selection "$itf_root/missing-trusted.json" >"$itf_root/explicit.stdout" 2>"$itf_root/explicit.stderr"
explicit_code=$?
set -e
test "$explicit_code" -eq 2
test ! -s "$itf_root/explicit.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/explicit.stderr"
set +e
dotnet "$cli" prepare --selection "$itf_root/duplicate.json" --selection "$itf_root/duplicate.json" >"$itf_root/flags.stdout" 2>"$itf_root/flags.stderr"
flags_code=$?
set -e
test "$flags_code" -eq 3
test ! -s "$itf_root/flags.stdout"
grep -Fx 'persistent-v3-image-closure-refused: duplicate-flag-or-relative-path' "$itf_root/flags.stderr"

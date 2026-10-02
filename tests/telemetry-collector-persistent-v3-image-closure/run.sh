#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
itf_root="$(mktemp -d)"
trap 'rm -rf -- "$itf_root"' EXIT
quint typecheck "$model"
quint test "$model_test" --backend typescript \
  --match '^(success|lost|mismatch|cancellation|cleanupFailureCase|stale)$' \
  --max-samples 1 --out-itf "$itf_root/{test}-{seq}.itf.json" | tee "$itf_root/quint-tests.log"
grep -E '^  6 passing \(' "$itf_root/quint-tests.log"
test "$(find "$itf_root" -maxdepth 1 -name '*.itf.json' -type f | wc -l)" -eq 6
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode
dotnet run --project "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode
PERSISTENT_V3_RUNNER_ITF_ROOT="$itf_root" dotnet run --project "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore
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

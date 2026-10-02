#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
itf_root="$(mktemp -d)"
trap 'rm -rf -- "$itf_root"' EXIT
quint typecheck "$model"
quint test "$model_test"
quint run "$model" --main=PersistentV3Runner --init=init --step=successStep --max-steps=9 --max-samples=1 --out-itf="$itf_root/success.itf.json"
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode
dotnet run --project "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode
PERSISTENT_V3_RUNNER_ITF="$itf_root/success.itf.json" dotnet run --project "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore
set +e
dotnet run --project "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3.ImageClosure.fsproj" --no-restore -- "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" >"$itf_root/production.stdout" 2>"$itf_root/production.stderr"
code=$?
set -e
test "$code" -eq 2
test ! -s "$itf_root/production.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/production.stderr"

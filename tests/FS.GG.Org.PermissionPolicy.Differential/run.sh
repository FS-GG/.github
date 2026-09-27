#!/usr/bin/env bash
set -euo pipefail

project=tests/FS.GG.Org.PermissionPolicy.Differential/DifferentialRunner.fsproj
artifacts="$(mktemp -d "${TMPDIR:-/tmp}/permission-differential.XXXXXX")"
trap 'rm -rf "$artifacts"' EXIT

dotnet restore "$project" --locked-mode --artifacts-path "$artifacts"
dotnet build "$project" --no-restore --artifacts-path "$artifacts"

runner="$artifacts/bin/DifferentialRunner/debug/DifferentialRunner.dll"
[ -f "$runner" ] || {
  echo "permission differential: build produced no runner at $runner" >&2
  exit 1
}
FSGG_PERMISSION_DIFFERENTIAL_RUNNER="$runner" \
  python3 tests/FS.GG.Org.PermissionPolicy.Differential/differential.py

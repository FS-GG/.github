#!/usr/bin/env bash
# Read-only inactive preparation reader controls; no Manager/Host process invocation.
set -euo pipefail
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1
export MSBUILDDISABLENODEREUSE=1
export DOTNET_PROCESSOR_COUNT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_root/tests/telemetry-host-inactive-preparation/TelemetryHost.InactivePreparation.Tests.fsproj"
dotnet restore "$project" --locked-mode --disable-parallel --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$project" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
dotnet "$repo_root/tests/telemetry-host-inactive-preparation/bin/Debug/net10.0/TelemetryHost.InactivePreparation.Tests.dll"

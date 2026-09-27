#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
dotnet run --project "$root/tests/skill-fsharp/adapter/AdapterHarness.fsproj" --configuration Release

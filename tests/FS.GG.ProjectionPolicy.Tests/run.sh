#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
project="$root/tests/FS.GG.ProjectionPolicy.Tests/FS.GG.ProjectionPolicy.Tests.fsproj"
dotnet restore "$project" --locked-mode
dotnet run --project "$project" --configuration Release --no-restore

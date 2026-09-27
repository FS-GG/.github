#!/usr/bin/env bash
set -euo pipefail

project=tests/FS.GG.Org.PermissionPolicy.Tests/FS.GG.Org.PermissionPolicy.Tests.fsproj
dotnet restore "$project" --locked-mode
dotnet run --project "$project" --no-restore

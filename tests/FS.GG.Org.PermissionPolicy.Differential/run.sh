#!/usr/bin/env bash
set -euo pipefail

project=tests/FS.GG.Org.PermissionPolicy.Differential/DifferentialRunner.fsproj
dotnet restore "$project" --locked-mode
dotnet build "$project" --no-restore
python3 tests/FS.GG.Org.PermissionPolicy.Differential/differential.py

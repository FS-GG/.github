#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
dotnet restore "$ROOT/tests/skill-fsharp/preflight/SkillPreflight.Tests.fsproj" --locked-mode
dotnet run --project "$ROOT/tests/skill-fsharp/preflight/SkillPreflight.Tests.fsproj" --no-restore -- "$ROOT"

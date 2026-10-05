#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet fsi --exec tests/work-programme/acceptance.fsx
dotnet fsi --exec tests/work-programme/context-delta.fsx
dotnet fsi --exec tests/work-programme/context-evidence.fsx
python3 tests/work-programme/context-baseline/test_collect.py

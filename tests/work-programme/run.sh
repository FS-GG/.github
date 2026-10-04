#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet fsi --exec tests/work-programme/acceptance.fsx

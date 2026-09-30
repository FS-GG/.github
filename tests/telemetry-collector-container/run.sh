#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
export PYTHONPYCACHEPREFIX="${TMPDIR:-/tmp}/fsgg-telemetry-collector-pycache"
python3 -m py_compile "$root/deployment/telemetry-collector/telemetry_collector.py"
python3 "$root/tests/telemetry-collector-container/test_collector.py"

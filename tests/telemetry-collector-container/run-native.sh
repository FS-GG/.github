#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
export PYTHONPYCACHEPREFIX="${TMPDIR:-/tmp}/fsgg-telemetry-native-pycache"
python3 -m py_compile \
  "$root/deployment/telemetry-collector/native_egress_gate.py" \
  "$root/deployment/telemetry-collector/native_topology.py"
python3 "$root/tests/telemetry-collector-container/test_native_network.py"

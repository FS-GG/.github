#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 "$ROOT/tests/v2-ci-ordinary-qualification/run.py"
python3 "$ROOT/tests/v2-ci-ordinary-observe/run.py"

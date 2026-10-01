#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../../.." && pwd)
export PYTHONDONTWRITEBYTECODE=1
exec python3 -m unittest discover -s "$ROOT/tests/telemetry-collector-container/persistent" -p 'test_*.py' -v

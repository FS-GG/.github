#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cp "$ROOT/default.json" "$WORK/good.json"
python3 "$ROOT/scripts/check-preset-resolution.py" --file "$WORK/good.json"

python3 - "$WORK/good.json" "$WORK/nonexistent-preset.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf8") as source:
    preset = json.load(source)
preset["extends"] = ["github>FS-GG/this-preset-does-not-exist-9f3a"]
with open(sys.argv[2], "w", encoding="utf8") as destination:
    json.dump(preset, destination)
PY

set +e
python3 "$ROOT/scripts/check-preset-resolution.py" --file "$WORK/nonexistent-preset.json"
status=$?
set -e
if [[ "$status" -ne 1 ]]; then
  echo "nonexistent preset returned $status; expected 1" >&2
  exit 1
fi

set +e
python3 "$ROOT/scripts/check-preset-resolution.py" --simulate-network-failure
status=$?
set -e
if [[ "$status" -ne 2 ]]; then
  echo "simulated network failure returned $status; expected 2" >&2
  exit 1
fi

echo "preset-resolution fixture — OK"

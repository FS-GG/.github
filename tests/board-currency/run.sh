#!/usr/bin/env bash
# Fixture and mutation controls for scripts/board-currency-rules.py and scripts/board-currency-fetch.py
# (Refs #4730). Every rule has a firing, a silent and (where input can be missing) an Unknown leg; the
# mutation controls prove each verdict is pinned by at least one test. No token, board or network.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
cd "$ROOT"
python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'
python3 -I "$HERE/mutate.py"

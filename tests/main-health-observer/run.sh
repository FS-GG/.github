#!/usr/bin/env bash
# Fixture for scripts/main-health-observer.py (Refs #4730).
#
# The observer is a DETECTOR that must never lie in the quiet direction, so the property under test is not
# "does it flag red" but "does every path to a green verdict require that every check was actually judged".
# The derivation is exercised on synthetic workflow trees (one leg per exclusion reason, and one that proves
# an eligible workflow stays eligible), the planner is pure, and collect/report are driven through a
# recording `gh` stub, so no token, runner or network is involved. The real repository is read-only input.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
cd "$ROOT"
exec python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'

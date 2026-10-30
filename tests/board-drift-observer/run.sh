#!/usr/bin/env bash
# Fixture for scripts/board-drift-observer.py and scripts/board-drift-fetch.py (Refs #4730).
#
# The observer is a DETECTOR that must never lie in the quiet direction, so the property under test is
# not "does it flag drift" but "does every rule have a leg that proves it can say NO as well as yes, and
# does a failed or partial read stay red instead of reporting no drift". The analysis and the report
# planner are pure; the fetch and the issue writes are driven through a recording `gh` stub, so no
# token, board or network is involved.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
cd "$ROOT"
exec python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'

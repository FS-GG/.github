#!/usr/bin/env bash
# Fixture for scripts/coord-board-archive.py, the Project 3 Done-retention archiver (#5119).
# Every guard has a negative case that proves it can say NO; see test_archive.py.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
cd "$ROOT"
exec python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'

#!/usr/bin/env bash
# Fixture for deployment/fdev-board-tend (.github#5136 PR 8). Offline, credential-free, stdlib only.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
exec python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'

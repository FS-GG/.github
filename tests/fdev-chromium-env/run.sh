#!/usr/bin/env bash
# Fixture for deployment/fdev-chromium/fdev-chromium-env (.github#5240). Offline, credential-free, stdlib only.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
exec python3 -I -m unittest discover -v -s "$HERE" -p 'test_*.py'

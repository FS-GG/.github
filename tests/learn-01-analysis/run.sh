#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
python3 -m unittest discover -s "$root/tests/learn-01-analysis" -p 'test_*.py' -v
python3 "$root/tools/learn-01-analysis.py" \
  "$root/policy/learn-01-current-focused-v1.json" \
  "$root/tests/learn-01-analysis/fixtures/synthetic.json"

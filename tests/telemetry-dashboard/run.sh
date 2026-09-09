#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 -m unittest discover -s "$ROOT/tests/telemetry-dashboard" -p 'test_*.py' -v
if [ "${FSGG_DASHBOARD_SKIP_BROWSER:-false}" != true ] && command -v npm >/dev/null 2>&1; then
  npm --prefix "$ROOT/tests/telemetry-dashboard" ci
  npx --prefix "$ROOT/tests/telemetry-dashboard" playwright install chromium
  python3 -m http.server 8473 --directory "$ROOT/telemetry-dashboard" > /tmp/fsgg-dashboard-http.log 2>&1 &
  server=$!
  trap 'kill "$server" 2>/dev/null || true' EXIT
  "$ROOT/tests/telemetry-dashboard/node_modules/.bin/playwright" test "$ROOT/tests/telemetry-dashboard/dashboard.spec.js" --config "$ROOT/tests/telemetry-dashboard/playwright.config.js"
fi

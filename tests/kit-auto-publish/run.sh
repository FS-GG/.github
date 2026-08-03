#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"; trap 'rm -rf "$work"' EXIT
case_run() {
  local name="$1" expected="$2" facts="$3"
  printf '%s' "$facts" > "$work/$name.json"
  actual="$(python3 "$root/scripts/kit-auto-publish.py" --facts "$work/$name.json" --json | python3 -c 'import json,sys; print(json.load(sys.stdin)["action"])')"
  [ "$actual" = "$expected" ] || { echo "$name: expected $expected, got $actual" >&2; exit 1; }
}
case_run eligible tag '{"version":"0.27.1","mergedPrReachable":true,"prArm":"pass","orgFeed":"absent","nugetFeed":"absent","tagExists":false}'
case_run major refuse '{"version":"1.0.0","mergedPrReachable":true,"prArm":"pass","orgFeed":"absent","nugetFeed":"absent","tagExists":false}'
case_run existing openEvidencePr '{"version":"0.27.1","sourceSha":"abc","mergedPrReachable":true,"prArm":"pass","orgFeed":"present","nugetFeed":"present","tagExists":true,"releaseRun":{"id":"42","url":"https://example.test/run/42","nuspecCommit":"abc"}}'
case_run partial stickyEscalate '{"version":"0.27.1","mergedPrReachable":true,"prArm":"pass","orgFeed":"present","nugetFeed":"absent","tagExists":false}'
case_run red-gate refuse '{"version":"0.27.1","mergedPrReachable":true,"prArm":"fail","orgFeed":"absent","nugetFeed":"absent","tagExists":false}'
case_run unknown-feed refuse '{"version":"0.27.1","mergedPrReachable":true,"prArm":"pass","orgFeed":"unknown","nugetFeed":"absent","tagExists":false}'
case_run missing-fact refuse '{"version":"0.27.1","mergedPrReachable":true,"prArm":"pass","orgFeed":"absent","nugetFeed":"absent"}'
case_run mismatched-nuspec stickyEscalate '{"version":"0.27.1","sourceSha":"abc","mergedPrReachable":true,"prArm":"pass","orgFeed":"present","nugetFeed":"present","tagExists":true,"releaseRun":{"id":"42","url":"https://example.test/run/42","nuspecCommit":"def"}}'
echo 'kit auto-publish state machine: 8 passed'

#!/usr/bin/env bash
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
out="${1:-$root/work/3014-repair-phase-turnover/test-results/stateful-turnover.junit.xml}"
log="$(mktemp)"
trap 'rm -f "$log"' EXIT

cd "$root"
PATH="${FSGG_VETTED_SDD_DIR:-/tmp/fsgg-sdd-1.0}:$PATH" \
  bash tests/coord-engine-e2e/writes.sh | tee "$log"

grep -Fq 'PASS  .github#3014: admitted round-4 exhaustion binds its terminal record and enters one typed repair phase' "$log"
grep -Fq 'PASS  .github#2819: one structured escalation crosses immutable round-three pass + settled-red claim turnover and every mutation refuses before write' "$log"
grep -Fq 'coord-engine writes: 190 assertion(s), 190 passed, 0 failed' "$log"

mkdir -p "$(dirname "$out")"
cat >"$out" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<testsuite name="repair-phase turnover stateful acceptance" tests="3" failures="0" errors="0" skipped="0">
  <testcase classname="repair-phase-turnover" name="admitted round-4 terminal binds typed escalation and repair-phase receipt" />
  <testcase classname="repair-phase-turnover" name="historical exact round-3 turnover remains accepted" />
  <testcase classname="repair-phase-turnover" name="full stateful engine write suite passes 190 assertions" />
</testsuite>
XML

printf 'REPAIR-PHASE-TURNOVER-GREEN: 3/3; stateful engine 190/190\n'

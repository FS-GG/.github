#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
CHECK="$ROOT/scripts/coordination-retirement-readiness.py"
LIVE="$ROOT/docs/reports/evidence/2026-08-14-m6-retirement-readiness.json"

if python3 "$CHECK" "$LIVE" >"$WORK/live.out"; then
  echo 'current not-ready census unexpectedly passed' >&2; exit 1
fi
grep -q 'issue creation must be below closure (0 !< 0)' "$WORK/live.out"
grep -q 'same-class successor census is not empty' "$WORK/live.out"

python3 - "$WORK/pass.json" <<'PY'
import json, sys
rows = []
for index, start in enumerate(("2026-08-17", "2026-08-24", "2026-08-31")):
    end = ("2026-08-24", "2026-08-31", "2026-09-07")[index]
    rows.append({
        "id": f"week-{index + 1}", "start": start + "T00:00:00Z", "end": end + "T00:00:00Z",
        "issues_created": 1, "issues_closed": 2, "repair_commits": 20,
        "statement_only_repairs": 1, "intent_reversals": 0, "partial_success_reads": 0,
        "ambiguous_release_states": 0, "release_outcomes": ["no-release-owed"],
        "policy_implementations_start": 10-index, "policy_implementations_end": 9-index,
        "check_scripts_start": 49-index, "check_scripts_end": 48-index,
        "workflows_start": 102-index, "workflows_end": 101-index,
        "generated_evidence_bytes_delta": 5, "core_and_test_bytes_delta": 10,
        "verification": ["fixture"]})
json.dump({"schema_version": 1, "measured_at": "2026-09-07T00:00:00Z", "source_sha": "a"*40,
           "candidate_periods": rows, "same_class_open": [],
           "successor_queries": ["fixture-query"],
           "successor_census": [{"url":"https://example.invalid/not-same", "disposition":"not-same-class", "reason":"fixture"}]},
          open(sys.argv[1], "w"))
PY
cat >"$WORK/live.json" <<'JSON'
{"source_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","periods":[{"id":"week-1","issues_created":1,"issues_closed":2},{"id":"week-2","issues_created":1,"issues_closed":2},{"id":"week-3","issues_created":1,"issues_closed":2}],"successor_urls":["https://example.invalid/not-same"]}
JSON
if python3 "$CHECK" "$WORK/pass.json" >"$WORK/offline.out"; then
  echo 'caller-authored positive evidence passed without live authentication' >&2; exit 1
fi
grep -q 'positive readiness requires --live-github' "$WORK/offline.out"
FSGG_RETIREMENT_FIXTURE_OK=1 python3 "$CHECK" "$WORK/pass.json" \
  --fixture-live-snapshot "$WORK/live.json" >"$WORK/pass.out"
grep -q 'retirement readiness: PASS' "$WORK/pass.out"

for mutation in equality gap partial successor evidence_growth nonweekly future inventory_reset live_mismatch; do
  jq --arg mutation "$mutation" '
    if $mutation == "equality" then .candidate_periods[1].issues_closed = 1
    elif $mutation == "gap" then .candidate_periods[1].start = "2026-08-25T00:00:00Z"
    elif $mutation == "partial" then .candidate_periods[2].partial_success_reads = 1
    elif $mutation == "successor" then .same_class_open = [{url:"https://example.invalid/1",reason:"fixture"}]
    elif $mutation == "evidence_growth" then .candidate_periods[0].generated_evidence_bytes_delta = 10
    elif $mutation == "nonweekly" then .candidate_periods[0].end = "2026-08-18T00:00:00Z"
    elif $mutation == "future" then .measured_at = "2026-08-30T00:00:00Z"
    elif $mutation == "inventory_reset" then .candidate_periods[1].check_scripts_start = 100
    elif $mutation == "live_mismatch" then .source_sha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
    else . end' "$WORK/pass.json" >"$WORK/$mutation.json"
  if FSGG_RETIREMENT_FIXTURE_OK=1 python3 "$CHECK" "$WORK/$mutation.json" \
       --fixture-live-snapshot "$WORK/live.json" >"$WORK/$mutation.out"; then
    echo "$mutation mutation unexpectedly passed" >&2; exit 1
  fi
done

FSGG_RETIREMENT_FIXTURE_OK=1 python3 "$CHECK" "$WORK/pass.json" \
  --fixture-live-snapshot "$WORK/live.json" >"$WORK/repeat.out"
cmp "$WORK/pass.out" "$WORK/repeat.out"
echo 'coordination retirement readiness: 13 passed, 0 failed'

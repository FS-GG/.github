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

python3 - "$WORK/pass.json" "$WORK" <<'PY'
import hashlib, json, pathlib, sys
rows = []
root = pathlib.Path(sys.argv[2]); (root / "observations").mkdir()
for index, start in enumerate(("2026-08-17", "2026-08-24", "2026-08-31")):
    end = ("2026-08-24", "2026-08-31", "2026-09-07")[index]
    row = {
        "id": f"week-{index + 1}", "start": start + "T00:00:00Z", "end": end + "T00:00:00Z",
        "issues_created": 1, "issues_closed": 2, "repair_commits": 20,
        "statement_only_repairs": 1, "intent_reversals": 0, "partial_success_reads": 0,
        "ambiguous_release_states": 0, "release_outcomes": ["no-release-owed"],
        "policy_implementations_start": 10-index, "policy_implementations_end": 9-index,
        "check_scripts_start": 49-index, "check_scripts_end": 48-index,
        "workflows_start": 102-index, "workflows_end": 101-index,
        "generated_evidence_bytes_delta": 5, "core_and_test_bytes_delta": 10,
        "verification": ["fixture"]}
    reproduce = ["python3","scripts/coordination-health-collector.py","--root",".","--output-dir","docs/reports/evidence/coordination-health"]
    observation = {"schema_version":1, "source_sha":"a"*40, "measured_at":"2026-09-07T00:00:00Z",
                   "period_id": row["id"], "start":row["start"], "end":row["end"],
                   "reproduce":reproduce, **{key: row[key] for key in (
        "issues_created", "issues_closed", "repair_commits", "statement_only_repairs", "intent_reversals", "partial_success_reads",
        "ambiguous_release_states", "release_outcomes", "policy_implementations_start",
        "policy_implementations_end", "check_scripts_start", "check_scripts_end", "workflows_start",
        "workflows_end", "generated_evidence_bytes_delta", "core_and_test_bytes_delta")}}
    observation["raw"] = {
        "created": [{"id": 1}], "closed": [{"id": 1}, {"id": 2}],
        "repair_classification": ([{"statement_only": True}] + [{"statement_only": False}] * 19),
        "intent_reversal_events": [], "partial_success_events": [],
        "release_classification": [], "prose_citation_gate": "prose-citations: ok (fixture)"}
    payload = json.dumps(observation, sort_keys=True, separators=(",", ":")).encode()
    artifact = pathlib.Path("observations") / f"week-{index + 1}.json"
    (root / artifact).write_bytes(payload)
    row["provenance"] = {"artifact": str(artifact), "sha256": hashlib.sha256(payload).hexdigest(),
                         "reproduce": reproduce}
    rows.append(row)
json.dump({"schema_version": 1, "measured_at": "2026-09-07T00:00:00Z", "source_sha": "a"*40,
           "collector":{"schema_version":1,"command":["python3","scripts/coordination-health-collector.py","--root",".","--output-dir","docs/reports/evidence/coordination-health"]},
           "candidate_periods": rows, "same_class_open": [],
           "successor_queries": [
             'repo:FS-GG/.github is:open is:issue "LIFECYCLE-PROJECTION-LAG"',
             'repo:FS-GG/.github is:open is:issue GraphQL pagination',
             'repo:FS-GG/.github is:open is:issue "partial read"',
             'repo:FS-GG/.github is:open is:issue "feed coherence"',
             'repo:FS-GG/.github is:open is:issue "partial publish"',
             'repo:FS-GG/.github is:open is:issue "body hash"',
             'repo:FS-GG/.github is:open is:issue "delivery-route receipt"',
             'repo:FS-GG/.github is:open is:issue "legacy-only"',
             'repo:FS-GG/.github is:open is:issue "statement" "projection"',
             'repo:FS-GG/.github is:open is:issue "bulky evidence"'],
           "successor_census": [{"url":"https://example.invalid/not-same", "disposition":"not-same-class", "reason":"fixture"}]},
          open(sys.argv[1], "w"))
PY
cat >"$WORK/live.json" <<'JSON'
{"source_sha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","periods":[{"id":"week-1","issues_created":1,"issues_closed":2},{"id":"week-2","issues_created":1,"issues_closed":2},{"id":"week-3","issues_created":1,"issues_closed":2}],"successor_urls":["https://example.invalid/not-same"]}
JSON
if python3 "$CHECK" "$WORK/pass.json" --root "$WORK" >"$WORK/offline.out"; then
  echo 'caller-authored positive evidence passed without live authentication' >&2; exit 1
fi
grep -q 'positive readiness requires --live-github' "$WORK/offline.out"
python3 "$ROOT/tests/coordination-retirement-readiness/fixture_validate.py" \
  "$CHECK" "$WORK/pass.json" "$WORK/live.json" "$WORK" >"$WORK/pass.out"

for mutation in equality gap partial successor evidence_growth nonweekly future inventory_reset live_mismatch query_narrow provenance_tamper reproduce_tamper; do
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
    elif $mutation == "query_narrow" then .successor_queries = [.successor_queries[0]]
    elif $mutation == "provenance_tamper" then .candidate_periods[0].repair_commits = 19
    elif $mutation == "reproduce_tamper" then .candidate_periods[0].provenance.reproduce = ["arbitrary-command"]
    else . end' "$WORK/pass.json" >"$WORK/$mutation.json"
  if python3 "$ROOT/tests/coordination-retirement-readiness/fixture_validate.py" \
       "$CHECK" "$WORK/$mutation.json" "$WORK/live.json" "$WORK" >"$WORK/$mutation.out"; then
    echo "$mutation mutation unexpectedly passed" >&2; exit 1
  fi
done

python3 - "$CHECK" <<'PY'
import importlib.util, sys
spec=importlib.util.spec_from_file_location("readiness", sys.argv[1]); module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
assert module.ACCEPTANCE_ENABLED is False, "preparatory gate unexpectedly enabled production acceptance"
PY
ln -s /etc/passwd "$WORK/observations/escape.json"
jq --arg digest "$(sha256sum /etc/passwd | cut -d' ' -f1)" \
  '.candidate_periods[0].provenance.artifact="observations/escape.json" | .candidate_periods[0].provenance.sha256=$digest' \
  "$WORK/pass.json" >"$WORK/symlink.json"
if python3 "$ROOT/tests/coordination-retirement-readiness/fixture_validate.py" \
     "$CHECK" "$WORK/symlink.json" "$WORK/live.json" "$WORK" >"$WORK/symlink.out"; then
  echo 'symlink escape unexpectedly passed' >&2; exit 1
fi
grep -q 'cannot read artifact' "$WORK/symlink.out"

python3 "$ROOT/tests/coordination-retirement-readiness/fixture_validate.py" \
  "$CHECK" "$WORK/pass.json" "$WORK/live.json" "$WORK" >"$WORK/repeat.out"
cmp "$WORK/pass.out" "$WORK/repeat.out"
echo 'coordination retirement readiness: 18 passed, 0 failed'

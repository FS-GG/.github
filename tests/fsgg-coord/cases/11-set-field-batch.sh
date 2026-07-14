#!/usr/bin/env bash
# case: set field batch
# tier: full
# covers: set-field issues
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="11-set-field-batch"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #448: set-field --batch — N fields, ONE GraphQL request -------------------------------------
# THE ACCEPTANCE CRITERION IS THE CALL COUNT. GitHub bills these mutations at the 1-point floor, so
# cost tracks requests: three separate writes are three points, and the same three aliased into one
# document is one. The assertion has to be on the COUNT, not on the document's contents, or it would
# pass a "batch" that quietly looped.
: >"$GH_LOG"
before_batch="$(gcount)"
run set-field --batch 'FS.GG.SDD#42' 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=fs-gg-ui-template' >/dev/null
assert_eq "#448: THREE fields cost exactly ONE GraphQL call" "$((before_batch + 1))" "$(gcount)"
batch_doc="$(cat "$GH_LOG")"
assert_contains "#448: ...emitted as ONE aliased mutation document" "batch-mutation mutation {" "$batch_doc"
assert_contains "#448: alias f0 is a field mutation"  "f0: updateProjectV2ItemFieldValue" "$batch_doc"
assert_contains "#448: alias f2 rides the SAME document" "f2: updateProjectV2ItemFieldValue" "$batch_doc"
assert_contains "#448: SINGLE_SELECT routes to singleSelectOptionId" '{singleSelectOptionId: "opt_p2"}' "$batch_doc"
assert_contains "#448: DATE routes to date"   '{date: "2026-08-01"}'         "$batch_doc"
assert_contains "#448: TEXT routes to text"   '{text: "fs-gg-ui-template"}'  "$batch_doc"
assert_contains "#448: the resolved item and project ids are carried" 'itemId: "PVTI_coord123"' "$batch_doc"
# The single path is `gh project item-edit`. If the batch fell back to it, the count assertion above
# could still pass while the writes went out one-by-one on a DIFFERENT transport — so pin the negative.
case "$batch_doc" in
  *item-edit*) bad "#448: the batch does NOT fall back to per-field item-edit" "item-edit in: $batch_doc" ;;
  *)           ok  "#448: the batch does NOT fall back to per-field item-edit" ;;
esac

# An empty value CLEARS — and `update` with an empty value is a no-op on the real API, not a clear,
# so the batch must reach for the distinct clear mutation exactly as the single path reaches for
# `--clear`. Getting this wrong leaves the OLD value in place while reporting a successful write.
: >"$GH_LOG"
run set-field --batch 'FS.GG.SDD#42' 'Contract=' >/dev/null
assert_contains "#448: an empty value emits clearProjectV2ItemFieldValue, not an empty update" \
  "f0: clearProjectV2ItemFieldValue" "$(cat "$GH_LOG")"

# A value may legitimately contain '='. Split on the FIRST one only, or `Contract=a=b` silently
# becomes a different value than the caller asked for.
: >"$GH_LOG"
run set-field --batch 'FS.GG.SDD#42' 'Contract=a=b' >/dev/null
assert_contains "#448: Field=Value splits on the FIRST '=' (a value may contain one)" \
  '{text: "a=b"}' "$(cat "$GH_LOG")"

# A REFUSED pair must cost ZERO GraphQL — the same invariant the single write holds. Here it is
# load-bearing twice: a bad pair caught late would fail the document AFTER its earlier aliases had
# already been written to the board.
before_bad="$(gcount)"
rc448=0; run set-field --batch 'FS.GG.SDD#42' 'No Such Field=x' >/dev/null 2>&1 || rc448=$?
assert_eq "#448: an unknown field is refused"                  "1"            "$rc448"
assert_eq "#448: ...and a refused pair spends ZERO GraphQL"    "$before_bad"  "$(gcount)"

# An unknown single-select OPTION is refused the same way — and this is a DIFFERENT code path from the
# unknown FIELD above, which is exactly why it needs its own assertion. `option_id` dies from inside
# `field_value_literal`, which the document builder used to call INSIDE a `$( )`. A die there unwinds
# only the SUBSTITUTION (see die_rc), so the loop carried on, left `value: ` empty, and SENT a document
# it already knew was malformed — paying a GraphQL point to be told, in a parse error, what it knew
# before the call. The count assertion is the one that catches that: the old code spent 1, not 0.
: >"$GH_LOG"
before_opt="$(gcount)"
rcopt=0; opt_out="$(run set-field --batch 'FS.GG.SDD#42' 'Phase=No Such Option' 2>&1)" || rcopt=$?
assert_eq "#448: an unknown single-select OPTION is refused"        "1"            "$rcopt"
assert_eq "#448: ...and spends ZERO GraphQL (the die must abort the BUILD, not the substitution)" \
  "$before_opt" "$(gcount)"
assert_eq "#448: ...so no document is ever sent" "0" "$(grep -c '^batch-mutation' "$GH_LOG" || true)"
assert_contains "#448: ...and the reason is the OPTION, not a GraphQL parse error" "No Such Option" "$opt_out"

# Same hazard, same path, for a NUMBER that is built from legal characters but is not a number. A
# character-class filter passes '1.2.3' and emits {number: 1.2.3} — unparseable, so it fails the WHOLE
# batch, including the fields that were fine.
before_num="$(gcount)"
rcnum=0; num_out="$(run set-field --batch 'FS.GG.SDD#42' 'Estimate=1.2.3' 2>&1)" || rcnum=$?
case "$num_out" in
  *"no field named 'Estimate'"*)   ok "#448: (no NUMBER field on this board — NUMBER guard covered by unit shape)" ;;
  *)  assert_eq "#448: a malformed NUMBER is refused"               "1"            "$rcnum"
      assert_eq "#448: ...and spends ZERO GraphQL"                  "$before_num"  "$(gcount)"
      assert_contains "#448: ...naming it as NOT a NUMBER" "is not a NUMBER" "$num_out" ;;
esac

# THE PARTIAL-APPLICATION STORY — the half of this item that is not about speed (#448, and #266's
# class). Mutations in one document run SERIALLY: when f1 fails, f0 has ALREADY been written to the
# board. Reporting that as a failure would claim nothing happened; reporting it as success is the bug
# the issue forbade by name. It must be its own answer, and it must not be queued.
: >"$GH_LOG"
part_out="$(GH_BATCH_FAIL_ALIAS=f1 run set-field --batch 'FS.GG.SDD#42' 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=x' 2>&1)" && part_rc=0 || part_rc=$?
assert_eq "#448: a per-alias failure is NOT success"                    "4"  "$part_rc"
assert_contains "#448: ...it says PARTIALLY APPLIED"      "PARTIALLY APPLIED"        "$part_out"
assert_contains "#448: ...and names the field that WAS written"  "APPLIED  Phase='P2 SDD'" "$part_out"
assert_contains "#448: ...and the field that failed"             "FAILED   Target="        "$part_out"
assert_contains "#448: ...carrying the API's own reason"         "stub: f1 rejected"       "$part_out"
assert_contains "#448: ...and says the board is half-written"    "half-written"            "$part_out"
assert_eq "#448: a PARTIAL batch is NEVER queued (replaying it would rewrite what landed)" "0" \
  "$(grep -c '"ref":"FS.GG.SDD#42"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"

# A rate limit refuses the document OUTRIGHT — nothing is applied — so the whole batch is deferrable,
# and EVERY pair must land in the queue. This is the arm that must be tested BEFORE the partial arm in
# the client: a 403 that fell through to the partial reporter would describe a half-written board that
# does not exist.
rm -f "$FSGG_COORD_CACHE/pending.jsonl"
rate_rc=0
GH_RATELIMIT=1 run set-field --batch 'FS.GG.SDD#42' 'Phase=P2 SDD' 'Target=2026-08-01' >/dev/null 2>&1 || rate_rc=$?
assert_eq "#448: an exhausted budget exits EX_RATE (75), not a generic 1" "75" "$rate_rc"
assert_eq "#448: ...and QUEUES every pair in the batch (nothing was applied, so nothing is lost)" "2" \
  "$(grep -c '"ref":"FS.GG.SDD#42"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"
rm -f "$FSGG_COORD_CACHE/pending.jsonl"

# (4) issues over REST with ETag; unchanged repeat -> 304 served from cache.
body1="$(run issues FS.GG.SDD --label cross-repo 2>/dev/null)"
assert_contains "issues: first call returns the list" '"number":42' "$body1"
rest_after_first="$(rcount)"
err2="$(run issues FS.GG.SDD --label cross-repo 2>&1 >/dev/null)"
assert_contains "issues: repeat revalidates and 304s to cache" "304 Not Modified — served from cache" "$err2"
assert_eq "issues: the 304 path used the cached body" '42' "$(run issues FS.GG.SDD --label cross-repo --jq '.[].number' 2>/dev/null)"

# (5) bad names fail loudly.
assert_fails "field-id on an unknown field fails"  run field-id Bogus
assert_fails "option-id on an unknown option fails" run option-id Phase 'P9 Nope'


harness_report

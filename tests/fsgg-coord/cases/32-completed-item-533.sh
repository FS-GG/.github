#!/usr/bin/env bash
# case: completed item 533
# tier: full
# covers: done release
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="32-completed-item-533"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #533: a COMPLETED item must not keep its lock. ---------------------------------------------
# `done --flip` verified the merge, set Status, rolled up the epic — and never touched the marker.
# `release` was the only path that dropped it, and `release` REWRITES Status, so running it on an item
# you just stamped Done clobbers the stamp you just earned. So on the SUCCESS path there was no action,
# in the tool or the recipe, that dropped the lock. It stayed live for the rest of the 120m lease, and
# a live marker's `Paths:` keep reserving its touch-set.
#
# It bites hardest exactly where the protocol is working: the items most likely to overlap a
# just-finished item are its own FOLLOW-UP findings — the ones §4 tells you to file BECAUSE you were
# standing in those files. The recipe reliably produced an item its own author had locked out.
jq -n --arg ts "$fresh_ts" '[{id:860, body:"<!-- fsgg:claim worker=vole-533 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-42.json"
assert_eq "#533: precondition — the finished item is claimed" "vole-533" "$(workers_on 42)"
: >"$GH_LOG"
run_worker_done="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker vole-533 done 'FS.GG.SDD#42' --pr 7 --flip 2>&1 || true)"
assert_contains "#533: the stamp is still earned" "FSGG-DONE   FS.GG.SDD#42" "$run_worker_done"
assert_eq "#533: ...and `done --flip` DROPS the claim — a finished item must not reserve its files" \
  "" "$(workers_on 42)"

# It must only drop OUR OWN marker. Deleting another worker's claim is `reap`'s job, and it is
# destructive — `done` may not do it silently just because the item happens to be finished.
jq -n --arg ts "$fresh_ts" '[{id:861, body:"<!-- fsgg:claim worker=other-999 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-42.json"
other_done="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker vole-533 done 'FS.GG.SDD#42' --pr 7 --flip 2>&1 || true)"
assert_eq "#533: ...but it must NOT delete a claim that is not ours" "other-999" "$(workers_on 42)"
assert_contains "#533: ...it says so instead, and points at reap" "still holds its claim" "$other_done"
: >"$STORE/comments-42.json"; echo '[]' >"$STORE/comments-42.json"


harness_report

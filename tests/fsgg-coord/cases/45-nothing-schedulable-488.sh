#!/usr/bin/env bash
# case: nothing schedulable 488
# tier: full
# covers: batch take next
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="45-nothing-schedulable-488"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #488: "nothing schedulable" must be COUNTED, never inferred from an empty stderr -----------
#
# `take`'s else-branch asserted the strongest available negative — "no open board item at all — this
# is an empty queue, not a blocked one" — on the sole evidence that `batch` wrote nothing to STDERR.
# An empty stderr is not an empty board. It was #440's defect reintroduced inside #440's own fix, and
# it pointed the worker AWAY from the true cause: it does not merely omit "blocked", it rules it out.
#
# TWO distinct ways a queue starves while looking empty, and they fail differently:
#   A. every candidate is BLOCKED. `batch` used to drop blocked items BEFORE the skip-reason loop, so
#      they left no trace at all — the one state most likely to starve a queue was the one state that
#      said nothing. `batch` now reports it as a reason, which is where a reason belongs.
#   B. every open item is in some NON-STARTABLE column (In review here; In progress in real life).
#      These are never Ready/Backlog, so they are never candidates and `batch` has nothing to skip —
#      stderr is legitimately empty. This is the case ONLY `take`'s own count can catch, and the one
#      the old code got exactly backwards. (`In review` rather than `In progress` on purpose: the
#      latter makes `active_claims` go read the item's claim markers, which needs an issue fixture
#      this board does not have. Same branch, no unrelated dependency.)
# FSGG_COORD_SCAN_TTL_SEC=0 on every leg below: `take` reads through the SHARED 90s scan cache
# (CACHED=1), which is keyed on the BOARD, not on GH_BOARD_SET — so a cache warmed by an earlier
# test's default board would be served here and the swap silently ignored. Turning the cache off is
# what makes these legs actually see board-starved. (Found the hard way: every leg failed because
# `take` was scheduling against a board that had none of these items.)
cat >"$FIXTURES/board-starved.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":{"name":"P7 Audio"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#999"},"content":{"__typename":"Issue","number":301,"title":"Blocked on an open, unverifiable blocker","url":"https://github.com/FS-GG/FS.GG.Audio/issues/301","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"In review"},"phase":{"name":"P3 Governance"},"blockedBy":null,"content":{"__typename":"Issue","number":302,"title":"Open, but in review — not startable, not blocked","url":"https://github.com/FS-GG/FS.GG.Governance/issues/302","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Done"},"phase":{"name":"P3 Governance"},"blockedBy":null,"content":{"__typename":"Issue","number":303,"title":"Finished, and must not be counted as open","url":"https://github.com/FS-GG/FS.GG.Governance/issues/303","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":{"name":"P4 Templates"},"blockedBy":null,"content":{"__typename":"Issue","number":304,"title":"A real, startable item in ANOTHER repo","url":"https://github.com/FS-GG/FS.GG.Templates/issues/304","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4988}}
JSON

# A. BLOCKED queue — `batch` must now NAME the blocker instead of dropping the item in silence.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo audio 2>&1 || true)"
assert_contains "#488 A: a blocked candidate is reported, not silently dropped" "blocked by" "$out"
assert_contains "#488 A: ...and it names the blocker"                           "FS.GG.SDD#999" "$out"
case "$out" in
  *"empty queue"*) bad "#488 A: a BLOCKED queue must not be called empty" "$out" ;;
  *)               ok  "#488 A: a BLOCKED queue is not reported as 'an empty queue, not a blocked one'" ;;
esac

# B. IN PROGRESS queue — stderr is legitimately empty, so only a COUNT can tell empty from starved.
# This is the exact sentence #488 was filed about, and the exact board that falsifies it.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo governance 2>&1 || true)"
case "$out" in
  *"empty queue"*) bad "#488 B: an open non-startable item must not read as 'no open board item at all'" "$out" ;;
  *)               ok  "#488 B: a queue whose items are all non-startable is not called empty" ;;
esac
assert_contains "#488 B: it COUNTS the open items instead of guessing" "1 open board item" "$out"
assert_contains "#488 B: ...and says what state they are in"           "In review"          "$out"
# Done must not be counted as open — otherwise every finished board reads as "blocked".
case "$out" in
  *"2 open board item"*) bad "#488 B: a Done item was counted as open" "$out" ;;
  *)                     ok  "#488 B: a Done item is not counted as open" ;;
esac

# C. GENUINELY empty — the message it was always meant for must still fire, or the fix has merely
# swapped one false sentence for another.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved run take --repo sdd 2>&1 || true)"
assert_contains "#488 C: a genuinely empty queue still says so" "empty queue, not a blocked one" "$out"

# D. UNREADABLE — "I could not look" and "I looked, and it is empty" must not produce the same
# sentence (#266's ratified rule). A failed count may never render as an empty queue.
out="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=starved GH_FAIL_BOARD=1 run take --repo governance 2>&1 || true)"
case "$out" in
  *"empty queue"*) bad "#488 D: an UNREADABLE board must not read as an empty queue" "$out" ;;
  *)               ok  "#488 D: an unreadable board is a no-verdict, never 'an empty queue'" ;;
esac


harness_report

#!/usr/bin/env bash
# case: claim restores column
# tier: full
# covers: claim release
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="21-claim-restores-column"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #481: undoing a claim RESTORES the column it overwrote; it does not guess `Ready` -----------
# #331 taught release/reap to leave a deliberately-chosen column alone, and to reset only the
# `In progress` a claim had written. Resetting it to `Ready` was a faithful undo — while `claim` was
# reachable ONLY from `Ready`. #440 gave `take` a Ready-else-Backlog fallback, and from that commit on
# every undo path (`release`, `take`'s lost-race retry, `reap`) PROMOTED a Backlog item it touched:
# a slow, invisible drain of the untriaged column into the one humans read as scheduled work — made
# self-reinforcing by the very fallback that caused it.
#
# The pre-claim column is knowable at exactly one instant — before the claim overwrites it — so the
# claim records it in its own marker, which is created, inherited, stolen and destroyed with the
# lease. Everything below is about that record surviving long enough to be used.
bodies_on() { jq -r '.[].body' "$STORE/comments-$1.json"; }
mark_stale() {  # mark_stale <num> <marker-id> <worker> [prev-key]
  jq -n --arg ts "$stale_ts" --argjson id "$2" --arg b "<!-- fsgg:claim worker=$3 lease=120${4:+ prev=$4} -->
dead" '[{id:$id, body:$b, user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' \
    >"$STORE/comments-$1.json"
}
for n in 350 351 352 353 354 355 356 357 358; do seed_issue "$n" "restore case $n" "src/Rst$n/**"; done

# (a) THE DEFECT. `take` claims a Backlog item; `release` must not hand it back as Ready.
printf 'Backlog' >"$STORE/itemstatus-350"
as pika-r01 claim 'FS.GG.SDD#350' --force >/dev/null 2>&1
assert_contains "#481: the claim RECORDS the column it overwrote, in its own marker" \
  "prev=Backlog" "$(bodies_on 350)"
printf 'In progress' >"$STORE/itemstatus-350"; : >"$GH_LOG"   # ...which is what the claim wrote
rel350="$(as pika-r01 release 'FS.GG.SDD#350' 2>/dev/null)"
assert_contains "#481: release RESTORES Backlog instead of promoting it to Ready" "board: Backlog" "$rel350"
assert_contains "#481: ...and says it restored a column, rather than reset one" "restored" "$rel350"
assert_contains "#481: ...with a real board write, not just a claim about one" "opt_backlog" "$(cat "$GH_LOG")"
assert_eq "#481: ...and the lease is still dropped" "" "$(workers_on 350)"

# (b) Marker keys are SPACE-separated; Status names are not. `In review` must survive the round trip
#     — an encoding that loses at the space would silently truncate the restore target to `In`.
printf 'In review' >"$STORE/itemstatus-351"
as pika-r01 claim 'FS.GG.SDD#351' --force >/dev/null 2>&1
assert_contains "#481: a Status with a space is percent-encoded into the marker" \
  "prev=In%20review" "$(bodies_on 351)"
printf 'In progress' >"$STORE/itemstatus-351"; : >"$GH_LOG"
assert_contains "#481: ...and decodes back to the real column name on release" "board: In review" \
  "$(as pika-r01 release 'FS.GG.SDD#351' 2>/dev/null)"
assert_contains "#481: ...resolving the real option id, so the write is not a no-op" "opt_review" "$(cat "$GH_LOG")"

# (c) A heartbeat REWRITES the whole marker body. Anything it does not carry forward is destroyed —
#     so a claim that beats for two hours would forget the column long before it released it.
printf 'Backlog' >"$STORE/itemstatus-352"
as pika-r01 claim 'FS.GG.SDD#352' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-352"
as pika-r01 heartbeat 'FS.GG.SDD#352' >/dev/null 2>&1
assert_contains "#481: a heartbeat rewrites the marker and CARRIES the recorded column" \
  "prev=Backlog" "$(bodies_on 352)"
: >"$GH_LOG"
assert_contains "#481: ...so a long-running claim still restores Backlog" "board: Backlog" \
  "$(as pika-r01 release 'FS.GG.SDD#352' 2>/dev/null)"

# (d) `reap` is the other way a claim goes away, so it owes the board the same answer — and the dead
#     worker's marker is the only thing that still knows what its claim overwrote.
mark_stale 353 853 ghost-353 Backlog
printf 'In progress' >"$STORE/itemstatus-353"; : >"$GH_LOG"
reaped353="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "#481: reap RESTORES the column the dead claim overwrote" "board: Backlog" "$reaped353"
assert_eq "#481: ...and still collects the lease" "" "$(workers_on 353)"

# (e) THE SUBTLE ONE. Re-claiming over an expired lease: `claim` GARBAGE-COLLECTS the dead marker
#     before running the CAS. Read the board after that and it says `In progress` — the DEAD claim's
#     footprint — so the new claim would conclude nothing was overwritten and record nothing, and the
#     Backlog would be lost at the reap-and-reclaim rather than at the release. The record has to be
#     inherited from the marker being collected, which means reading it BEFORE the GC deletes it.
mark_stale 354 854 ghost-354 Backlog
printf 'In progress' >"$STORE/itemstatus-354"
as pika-r01 claim 'FS.GG.SDD#354' --force >/dev/null 2>&1
assert_contains "#481: a re-claim over a dead lease INHERITS the column that claim overwrote" \
  "prev=Backlog" "$(bodies_on 354)"
: >"$GH_LOG"
assert_contains "#481: ...so Backlog survives a reap-and-reclaim, not merely a clean release" \
  "board: Backlog" "$(as pika-r01 release 'FS.GG.SDD#354' 2>/dev/null)"

# (f) A --force steal evicts the holder and claims over the same already-overwritten column. Same
#     inheritance, or the thief releases the item into the wrong queue.
printf 'Backlog' >"$STORE/itemstatus-355"
as pika-r01 claim 'FS.GG.SDD#355' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-355"
as wren-c22 claim 'FS.GG.SDD#355' --force >/dev/null 2>&1
assert_contains "#481: a --force steal inherits the column the EVICTED claim overwrote" \
  "prev=Backlog" "$(bodies_on 355)"
: >"$GH_LOG"
assert_contains "#481: ...so the thief's release restores Backlog too" "board: Backlog" \
  "$(as wren-c22 release 'FS.GG.SDD#355' 2>/dev/null)"

# (g) BACKWARD COMPATIBILITY. Markers minted before this change carry no record, and there are live
#     ones on the board right now. They must keep releasing to `Ready` — the old behaviour, now
#     scoped to the one case where there is genuinely nothing to restore.
mark_stale 356 856 pika-r01           # a marker with no prev= key at all
printf 'In progress' >"$STORE/itemstatus-356"; : >"$GH_LOG"
assert_contains "#481: a marker minted BEFORE this change still falls back to Ready" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#356' 2>/dev/null)"

# (h) A claim that somehow recorded `In progress` recorded its own footprint, not a column anybody
#     chose. Restoring it would leave the item looking claimed with no claim on it.
mark_stale 357 857 pika-r01 'In%20progress'
printf 'In progress' >"$STORE/itemstatus-357"; : >"$GH_LOG"
assert_contains "#481: a recorded 'In progress' is a footprint, not a column — not restored" \
  "board: Ready" "$(as pika-r01 release 'FS.GG.SDD#357' 2>/dev/null)"

# (h2) An UNREADABLE column writes nothing (#331) — but we still know what the claim overwrote, and
#      the repair we hand the operator must say THAT. Telling them to set `Ready` over a Backlog item
#      would reintroduce the promotion in the advice, on the one path that can still prove it wrong.
seed_issue 361 "unreadable, but the marker remembers" "src/Rst361/**"
printf 'Backlog' >"$STORE/itemstatus-361"
as pika-r01 claim 'FS.GG.SDD#361' --force >/dev/null 2>&1
: >"$GH_LOG"
rel361="$(rel GH_FAIL_ITEM_STATUS=361 release 'FS.GG.SDD#361' 2>&1)"
assert_contains "#481: an unreadable column still writes nothing (#331 holds)" "Status unchanged" "$rel361"
assert_contains "#481: ...but the repair names the column the claim OVERWROTE, not 'Ready'" \
  "set-field FS.GG.SDD#361 Status Backlog" "$rel361"
assert_eq "#481: ...and no Status is written on an unreadable column" "0" "$(edits_to_status)"

# (i) #331 SURVIVES: a column set deliberately DURING the lease still beats the recorded one. The
#     record answers "what did the claim overwrite", never "what should this item be now".
printf 'Backlog' >"$STORE/itemstatus-358"
as pika-r01 claim 'FS.GG.SDD#358' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-358"; : >"$GH_LOG"
rel358="$(as pika-r01 release 'FS.GG.SDD#358' 2>/dev/null)"
assert_contains "#481: a column set DURING the lease still wins over the recorded one (#331 holds)" \
  "board: Blocked (preserved" "$rel358"
assert_eq "#481: ...writing no Status at all, rather than a matching one" "0" "$(edits_to_status)"

# (j) WHAT IT COSTS. Reading the pre-claim column is a GraphQL read, and `claim` is the hottest path
#     on the scarcest resource in this org: every worker runs `take` -> `claim` every round, against
#     ONE 5,000 pt/hr budget shared by the whole ACCOUNT — which this repo has already exhausted to
#     the point of taking the board down (#418). So the cost is pinned, not asserted: #481 buys the
#     restore with exactly ONE narrow item read (the `set_field` write is the other call). The failure
#     this guards is a later change reaching for a board SCAN here — seven points, times N workers,
#     times every round — which would be invisible until the budget was gone.
seed_issue 359 "what a claim costs" "src/Rst359/**"
printf 'Backlog' >"$STORE/itemstatus-359"; : >"$GH_GRAPHQL_COUNT"
as pika-r01 claim 'FS.GG.SDD#359' --force >/dev/null 2>&1
assert_eq "#418: a claim spends 2 GraphQL reads — the item lookup, plus #481's pre-claim column" \
  "2" "$(gcount)"
# ...and RELEASE is unchanged. It still makes the ONE read #331 needs (is this column one somebody
# chose during the lease?), and the restore target rides in on the marker for free — so restoring a
# column costs strictly less than re-deriving it would have.
printf 'In progress' >"$STORE/itemstatus-359"; : >"$GH_GRAPHQL_COUNT"
as pika-r01 release 'FS.GG.SDD#359' >/dev/null 2>&1
assert_eq "#418: a release still spends exactly 1 — the marker carries the column, adding NO read" \
  "1" "$(gcount)"


harness_report

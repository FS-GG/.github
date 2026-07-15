#!/usr/bin/env bash
# case: failclosed and budget
# tier: full
# covers: batch next ready take claim release done add
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="40-failclosed-and-budget"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #344: an unreadable board must FAIL CLOSED, not render as a confident empty answer ----------
# `gql` dies inside a `$( )`, and `exit` there only unwinds the subshell; `set -e` does not carry the
# substitution's non-zero across a bare `out="$(...)"` assignment. So every scheduler read used to get
# an empty payload and carry on — `take`/`next`/`ready`/`batch` reported an empty, itemised board and
# exited 0 when the board could not be read at all. `die` now signals the top-level shell, so a failed
# read halts the whole command at any nesting depth. GH_FAIL_BOARD makes the board scan a 401.
#
# THE SCAN CACHE (#418) DOES NOT WEAKEN THIS, and the two rules have to be said together:
#   * a failed READ is never rescued by the cache — the read dies, the command dies. That is what the
#     loop below asserts, with the cache cold (FSGG_COORD_SCAN_TTL_SEC=0), which is the only state in
#     which a read actually happens;
#   * a cache HIT is not a read. `next`/`take` served from a <90s scan never touch the network, so
#     there is no unreachable board for them to fail closed ON. That is the deal the cache makes, and
#     it is safe precisely because the claim CAS — REST markers, never cached — is what grants the
#     item. A stale schedule costs a lost race and a retry; it cannot cost a double claim.
# The old fixture passed this loop with a cache warmed by earlier tests, which asserted neither rule.
rm -f "$FSGG_COORD_CACHE"/scan-*.json
for spec in "ready" "next" "batch" "take --repo .github"; do
  if FSGG_COORD_SCAN_TTL_SEC=0 GH_FAIL_BOARD=1 run $spec >/dev/null 2>&1; then
    bad "#344: '$spec' fails closed (non-zero) when the board is unreachable"
  else
    ok  "#344: '$spec' fails closed (non-zero) when the board is unreachable"
  fi
done
# ...and the cache must not be a back door around it: a MISS + a 401 is still a hard failure, even
# with caching switched fully on. (A stale file cannot mask it either — this one is empty.)
rm -f "$FSGG_COORD_CACHE"/scan-*.json
assert_fails "#418: a cache MISS + an unreachable board still fails closed (the cache never rescues a failed read)" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_BOARD=1 FSGG_COORD_SCAN_TTL_SEC=90 \
    bash "$COORD" next --repo FS.GG.SDD
assert_eq "#418: ...and a failed scan is NOT written to the cache" "no" \
  "$(ls "$FSGG_COORD_CACHE"/scan-*.json >/dev/null 2>&1 && echo yes || echo no)"
# The headline regression: `take` must not turn "I could not read the board" into the confident claim
# "every candidate is blocked, claimed, overlapping, or undeclared." (Cache off: `take` is a SCHEDULING
# read, so a warm cache would legitimately answer without a read at all — see the two rules above. The
# regression under test is what happens when it DOES read and the read fails.)
take_unreadable="$(FSGG_COORD_SCAN_TTL_SEC=0 GH_FAIL_BOARD=1 run take --repo .github 2>&1 || true)"
case "$take_unreadable" in
  *"every candidate is"*) bad "#344: take does NOT assert an empty queue on an unreadable board" "got: $take_unreadable" ;;
  *)                      ok  "#344: take does NOT assert an empty queue on an unreadable board" ;;
esac
# ...and the failure is surfaced, not swallowed: `batch` (the 'see why' target) names the read error.
assert_contains "#344: batch surfaces the read failure instead of an empty result" \
  "GraphQL call failed" "$(GH_FAIL_BOARD=1 run batch --repo .github 2>&1 || true)"
# The DELIBERATE exceptions stay soft under the now-fatal `die`. (1) A best-effort board write whose
# subject is off-board must NOT unwind a claim whose lock is already held — the marker is the lock.
# Pure signal-die would abort the whole claim after the marker was posted; `SOFT_DIE=1` on the
# `( … set_field … )` subshell keeps that `die` local, so the claim completes and reports "not on
# board". This is the path the earlier tests missed (they seed off-board markers directly rather than
# driving `claim`), so it is asserted here explicitly.
seed_issue 779 "An item that is not on the board" "src/Off344/**"
# env-via-`env`, like `rel`: a shell prefix on the `as` FUNCTION would not export to the inner `bash`.
claim_off="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  env GH_OFFBOARD_ITEM=779 bash "$COORD" --worker osprey-344 claim 'FS.GG.SDD#779' 2>&1 || true)"
assert_contains "#344: an off-board claim still lands (best-effort flip's die stays local)" \
  "not on board" "$claim_off"
assert_contains "#344: ...and it reports the claim rather than a fatal abort" \
  "claimed FS.GG.SDD#779 by worker osprey-344" "$claim_off"
assert_eq "#344: ...and the marker (the lock) was actually posted" "yes" \
  "$([ -f "$STORE/comments-779.json" ] && [ -n "$(claims_on 779)" ] && echo yes || echo no)"
# (2) release/reap must still drop a lease when the board is unreadable (item_status marks its read
# SOFT_DIE=1) — proven above at "release: an unreadable Status is not overwritten". So "unreachable"
# and "readable, and empty" no longer share an exit code (#266), and neither strands a claim.

# ================================================================================================
# #418 — THE SHARED BUDGET. N workers, one GitHub account, one 5,000-pt/hr GraphQL budget. The bug
# this section pins down is not "we ran out of points"; it is what running out USED to do quietly:
# `claim` took the lock over REST, could not write `Status: In progress` because GraphQL was gone,
# swallowed the 403, and printed "not on board". The board then said `Backlog` while a worker held
# the item, and `next` hid it from every other worker — the CLAIM-STATUS-LAG drift /check-board
# reconciles. So the fixture asserts the three properties that close it, with the budget stubbed OUT
# (GH_RATELIMIT=1) rather than reasoned about:
#   (1) exhaustion is RECOGNISED and NAMED — exit EX_RATE (75), not a mystery 1;
#   (2) the claim still LANDS (REST lock) and the board write is QUEUED, not lost, and says so;
#   (3) `flush` replays the queue once the budget returns — and the board write actually happens.
# Plus the lever that keeps the budget from running out at all: (4) the shared scan cache, which is
# what turns N workers looping `next` into ONE board scan per TTL window.

seed_issue 810 "Rate-limited claim" "src/Rl810/**"

# (1)+(2) claim under an exhausted budget.
: >"$GH_LOG"; rl_before_rest="$(rcount)"
claim_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
  env GH_RATELIMIT=1 bash "$COORD" --worker vole-418 claim 'FS.GG.SDD#810' 2>&1 || true)"
assert_contains "#418: a rate-limited claim still LANDS — the lock is REST, not GraphQL" \
  "claimed FS.GG.SDD#810 by worker vole-418" "$claim_rl"
assert_contains "#418: ...and the board write is reported DEFERRED, not silently dropped" \
  "DEFERRED" "$claim_rl"
assert_contains "#418: ...and names the condition (exhausted budget), NOT 'not on board'" \
  "GraphQL budget exhausted" "$claim_rl"
# The regression that mattered: the old code printed "not on board" for a 403. If that string comes
# back for a rate-limited claim, the drift is back with it.
case "$claim_rl" in *"not on board"*) bad "#418: a rate-limit must NOT be misreported as 'not on board'" "$claim_rl" ;;
                    *) ok "#418: a rate-limit must NOT be misreported as 'not on board'" ;; esac
assert_eq "#418: the marker (the lock) was posted despite the exhausted budget" "yes" \
  "$([ -n "$(claims_on 810)" ] && echo yes || echo no)"
# Count THIS item's entry, not the queue's depth: a transient 502 earlier in the fixture legitimately
# queues a write too (any write the board refuses is queued — only an off-board item is dropped).
assert_eq "#418: the deferred Status write is QUEUED" "1" \
  "$(grep -c '"ref":"FS.GG.SDD#810","field":"Status"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"
assert_eq "#418: ...and no board mutation was reported as done" "0" \
  "$(grep -c '^item-edit' "$GH_LOG" || true)"

# #421 (the #266 class, folded in here because it is the same defect and the same lines): a lookup
# that FAILED must never be reported as a lookup that found nothing. With the budget exhausted,
# `item-id`/`set-field` used to print "issue … is not on board 'Coordination' — add it first: gh
# project item-add …" for an issue that WAS on the board — a confident absence, complete with a
# remediation that would have created a DUPLICATE item. The read failing and the item being absent are
# different facts, and only the second may be reported.
itemid_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" item-id 'FS.GG.SDD#42' --refresh 2>&1 || true)"
case "$itemid_rl" in
  *"not on board"*|*"item-add"*)
    bad "#421: a rate-limited item-id must NOT be reported as 'not on board'" "$itemid_rl" ;;
  *) ok "#421: a rate-limited item-id must NOT be reported as 'not on board'" ;;
esac
assert_contains "#421: ...it names the real cause (the exhausted budget)" "GraphQL budget EXHAUSTED" "$itemid_rl"
rc_itemid=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" item-id 'FS.GG.SDD#42' --refresh >/dev/null 2>&1 || rc_itemid=$?
assert_eq "#421: ...and exits EX_RATE (75), not the off-board code (3)" "75" "$rc_itemid"



# (1) the exit code a worker loop backs off on — 75 (EX_TEMPFAIL), from `take`, not a generic 1.
rc_take=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
  env GH_RATELIMIT=1 bash "$COORD" --worker vole-418 take --repo FS.GG.SDD >/dev/null 2>&1 || rc_take=$?
assert_eq "#418: an exhausted budget exits EX_RATE (75), the back-off signal" "75" "$rc_take"

# (3) flush replays the queue once the budget is back — and the write REALLY lands on the board.
#
# THE DRAIN MUST BE OBSERVED, NOT INFERRED FROM AN ABSENCE (#436). This assertion used to read the
# queue depth as `wc -l <pending.jsonl 2>/dev/null || echo 0` and compare it to 0 — which is green
# when the queue was drained AND green when the file never existed, never was written, or lived at a
# different path. `flush` UNLINKS the file when it empties it, so the absent case is the one that
# actually occurs: the assertion could not fail. That is epic #266's own shape — a coherence check
# that reports green on a missing subject — guarding the mechanism that stops an exhausted budget
# from silently dropping a board write. So: depth is ABSENT or a number (never conflated), the queue
# is proven NON-EMPTY first, and the flush is made to account for exactly what was in it.
pending_depth() {   # ABSENT | <count> — the two facts the old `|| echo 0` collapsed into one
  local f="$FSGG_COORD_CACHE/pending.jsonl"
  if [ -f "$f" ]; then wc -l <"$f" | tr -d ' '; else echo ABSENT; fi
}
depth_before="$(pending_depth)"
case "$depth_before" in
  ABSENT|0) bad "#418: the queue holds the deferred write(s) BEFORE flush" \
              "depth=$depth_before — nothing was queued, so the drain below would prove nothing" ;;
  *)        ok  "#418: the queue holds the deferred write(s) BEFORE flush ($depth_before queued)" ;;
esac
: >"$GH_LOG"
flush_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 bash "$COORD" flush 2>&1 || true)"
assert_contains "#418: flush replays the queued board write" "written" "$flush_out"
assert_contains "#418: ...as a real Status mutation" "PVTSSF_status" "$(cat "$GH_LOG")"
# Every queued write is accounted for — not just "at least one was written".
assert_contains "#418: ...and accounts for EVERY queued write" "$depth_before written" "$flush_out"
# ...and the drained queue is UNLINKED. Distinguishing this from "0 lines" is the whole point: only
# one of the two can be produced by a flush that ran, and it is this one.
assert_eq "#418: ...leaving the queue drained and UNLINKED (not merely unreadable)" \
  "ABSENT" "$(pending_depth)"

# ================================================================================================
# #510 — THE PROMISE THAT WAS ONLY TRUE FOR `claim`. Every one of the assertions above is about
# `claim`, and `claim` was the ONLY command that called defer_write. Every OTHER board write —
# `set-field` (which the recipes drive several times in a row when filing a finding), `done --flip`,
# `release`, `reap` — fell through to the shared EX_RATE handler, which prints:
#
#     "... Board WRITES are queued: see `fsgg-coord flush`."
#
# and queued NOTHING. The write was gone, and the message told the worker it was safe — so the worker
# did the correct thing, trusted it, carried on, and the finding they had just filed landed on the
# board with no Status, no Repo Scope and no Phase. `flush` then reported "nothing pending" and
# CONFIRMED the lie. (Family: epic #416 — a surface that runs, reports success, and does nothing.)
#
# There is now exactly ONE board write (`board_write`) and it queues. These assertions are the
# reproduction from the issue, verbatim: two `set-field` calls on an exhausted budget, then `flush`.

seed_issue 811 "Rate-limited set-field" "src/Rl811/**"
rm -f "$FSGG_COORD_CACHE/pending.jsonl"

sf_rl_rc=0
sf_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 \
  bash "$COORD" set-field 'FS.GG.SDD#811' Contract 'fs-gg-ui-template' 2>&1)" || sf_rl_rc=$?
assert_eq "#510: a rate-limited set-field exits EX_RATE (75), the back-off signal" "75" "$sf_rl_rc"
assert_contains "#510: ...and SAYS the write is queued" "QUEUED" "$sf_rl"
# THE BUG, in one assertion: the message promised a queue and the queue was empty.
assert_eq "#510: ...and the write is ACTUALLY in the queue — the promise is kept, not printed" "1" \
  "$(grep -c '"ref":"FS.GG.SDD#811","field":"Contract"' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"

# The recipe drives several in a row (cross-repo-coordination + pnext-item §4 both do). Every one of
# them must survive, not just the first — the worker filing a finding sets Status, Repo Scope, Phase.
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 \
  bash "$COORD" set-field 'FS.GG.SDD#811' Status Backlog >/dev/null 2>&1 || true
assert_eq "#510: a SECOND recipe-driven write queues too (a finding sets 3 fields, not 1)" "2" \
  "$(pending_depth)"

# ...but ONLY a transient failure is queued. A REFUSED write — an unknown field, an unknown option, a
# `Blocked by` that is not a ref — must NEVER be queued: replaying it could not succeed on the tenth
# attempt either, and the queue would carry it forever while swallowing the refusal that says why.
# This is the trap the first cut of the fix fell into, so it is pinned here.
refused_rc=0
refused="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw \
  bash "$COORD" set-field 'FS.GG.SDD#811' 'No Such Field' x 2>&1)" || refused_rc=$?
assert_eq "#510: a REFUSED write is not EX_RATE — it is a plain error" "1" "$refused_rc"
# Assert the REFUSED write's absence from the queue directly, not via the queue's depth: this call
# runs with the budget restored, so `autoflush` legitimately drains the two writes queued above
# first. Depth would then be green for the wrong reason — the #436 shape, and the very trap this
# fixture already calls out one section up.
assert_eq "#510: ...and is NOT queued (replaying it would never succeed)" "0" \
  "$(grep -c 'No Such Field' "$FSGG_COORD_CACHE/pending.jsonl" 2>/dev/null || echo 0)"
assert_contains "#510: ...and the refusal REACHES the worker, naming the fields that do exist" \
  "no field named" "$refused"
# The diagnostic itself used to die: the jq filter was written `join(\", \")` inside a $( ) — the
# backslashes survive, jq gets a syntax error, and the message that explains the refusal IS the
# refusal. A diagnostic that cannot render is a diagnostic that does not exist.
case "$refused" in *"jq: error"*) bad "#510: the refusal message must RENDER, not throw a jq syntax error" "$refused" ;;
                   *)             ok "#510: the refusal message must RENDER, not throw a jq syntax error" ;; esac

# `done --flip` is the other silent dropper: it flipped Status and, on an exhausted budget, said
# "board: Done" over a write that never landed. The stamp stays GREEN (the work IS merged and done —
# a red stamp on correct work is how a red stamp becomes noise, #558), but the board note must not
# claim Done, and the exit must be EX_RATE so a loop backs off and flushes.
done_rl_rc=0
done_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 env GH_RATELIMIT=1 \
  bash "$COORD" done 'FS.GG.SDD#801' --flip 2>&1)" || done_rl_rc=$?
case "$done_rl" in
  *"board: Done"*) bad "#510: done --flip must NOT report 'board: Done' over a write it only queued" "$done_rl" ;;
  *)               ok  "#510: done --flip must NOT report 'board: Done' over a write it only queued" ;;
esac
assert_eq "#510: ...it exits EX_RATE (75) so the loop backs off and flushes" "75" "$done_rl_rc"

# `release` restores the column the claim overwrote (#481). On an exhausted budget it used to report
# "not on board" — #418's exact misdiagnosis, still live here — and DROP the restore, stranding the
# item in the column the claim overwrote with no claim on it. It now names the real cause.
#
# NOTE the leg this does NOT cover: under exhaustion `release` fails at the *read* of the current
# Status, before it ever reaches a write, so what it reports is "could not read it" and the restore is
# still not queued. That is a real residue — the WRITE path is fixed here, the READ path is a separate
# defect — and it is honest rather than false, which is the bar this issue sets. Do not read the
# passing assertion below as "release under exhaustion restores the column"; it does not.
rel_rl="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 env GH_RATELIMIT=1 \
  bash "$COORD" --worker vole-418 release 'FS.GG.SDD#810' 2>&1 || true)"
case "$rel_rl" in
  *"not on board"*) bad "#510: a rate-limited release must NOT be misreported as 'not on board'" "$rel_rl" ;;
  *)                ok  "#510: a rate-limited release must NOT be misreported as 'not on board'" ;;
esac
# Whatever it says, it must not ASSERT a board column it did not write.
case "$rel_rl" in *"board: Ready"*|*"board: Backlog"*)
    bad "#510: release must not claim a restore it never performed" "$rel_rl" ;;
  *) ok "#510: release must not claim a restore it never performed" ;; esac

rm -f "$FSGG_COORD_CACHE/pending.jsonl"

# (4) THE SCAN CACHE — the lever that stops the budget running out in the first place. Two `next`
# calls inside the TTL must cost ONE board scan, because the second serves the first's scan from the
# shared (user-level, therefore cross-worker) cache. This is the assertion that would have caught the
# original burn: five workers looping `next` were paying five full scans a round.
rm -f "$FSGG_COORD_CACHE"/scan-*.json
before_scan="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD >/dev/null 2>&1 || true
mid_scan="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD >/dev/null 2>&1 || true
after_scan="$(gcount)"
assert_eq "#418: the first 'next' pays for the board scan" "1" "$((mid_scan - before_scan))"
assert_eq "#418: a second 'next' inside the TTL spends ZERO GraphQL (the shared scan cache)" \
  "0" "$((after_scan - mid_scan))"

# ...and --fresh must still be able to buy the truth. A cache you cannot bypass is a cache you cannot
# trust — `take`'s retry-after-a-lost-race depends on this.
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" next --repo FS.GG.SDD --fresh >/dev/null 2>&1 || true
assert_eq "#418: --fresh bypasses the cache and rescans" "1" "$(( $(gcount) - after_scan ))"
# The line the TTL is drawn on: RECONCILING reads never serve a cached board. `ready` is what
# /check-board snapshots, and a reconciler that reports yesterday's drift is worse than none.
before_ready_fresh="$(gcount)"
PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_SCAN_TTL_SEC=90 bash "$COORD" ready --repo FS.GG.SDD >/dev/null 2>&1 || true
assert_eq "#418: 'ready' (a TRUTH read) always scans fresh — it never serves the cache" \
  "1" "$(( $(gcount) - before_ready_fresh ))"

# ================================================================================================
# #587 — `add`: THE VERB WHOSE ABSENCE MADE THE MONOPOLY UNENFORCEABLE
# ================================================================================================
# Every recipe said `gh project item-add`, and so did this tool's own "not on board" message — so a
# worker COULD NOT put an item on the board without reaching past the client, onto a GraphQL budget
# the whole fleet shares and nothing else meters, caches, or queues against. A rule you cannot obey
# is not a rule, it is a reprimand: the monopoly needed this verb before it could need a gate.
#
# NOTE the placement: these run AFTER the #418 block, not inside it. `add` calls `autoflush` like
# every other board-writing command, so running it mid-#418 drains the very queue that section is
# about to assert on — which is how the first cut of these tests broke four assertions above.

# The tool must no longer PRESCRIBE the bypass it exists to replace.
offboard_msg="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_OFFBOARD_ITEM=777 bash "$COORD" item-id 'FS.GG.SDD#777' --refresh 2>&1 || true)"
case "$offboard_msg" in
  *"gh project item-add"*) bad "#587: the tool must not prescribe the bypass it is meant to replace" "$offboard_msg" ;;
  *) ok "#587: the tool must not prescribe the bypass it is meant to replace" ;;
esac
assert_contains "#587: ...it names its own verb instead" "fsgg-coord add" "$offboard_msg"

# An issue NOT on the board is added, and the new item id is printed.
: >"$GH_LOG"
add_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_OFFBOARD_ITEM=777 bash "$COORD" add 'FS.GG.SDD#777' 2>&1 || true)"
assert_contains "#587: add puts an off-board issue ON the board" "added FS.GG.SDD#777" "$add_out"
assert_eq "#587: ...with exactly one item-add call" "1" "$(grep -c '^item-add' "$GH_LOG" || true)"

# IDEMPOTENT. N parallel workers running the file-a-finding recipe would otherwise each create a
# board item for the same issue — #464's shape, one layer down.
: >"$GH_LOG"
again="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" add 'FS.GG.SDD#42' 2>&1 || true)"
assert_contains "#587: add is IDEMPOTENT — an item already on the board is not added twice" \
  "already on board" "$again"
assert_eq "#587: ...and spends no item-add" "0" "$(grep -c '^item-add' "$GH_LOG" || true)"

# A FAILED LOOKUP IS NOT AN ABSENCE (#421, one verb along). On an exhausted budget the "is it already
# there?" read cannot run — and adding on a read that FAILED is exactly how you create a duplicate
# board item. It must REFUSE, not add.
: >"$GH_LOG"
add_rl_rc=0
PATH="$STUB:$PATH" GH_BOARD_SET=pw env GH_RATELIMIT=1 bash "$COORD" add 'FS.GG.SDD#820' >/dev/null 2>&1 || add_rl_rc=$?
assert_eq "#587: a rate-limited add REFUSES rather than adding on a failed lookup (#421)" \
  "0" "$(grep -c '^item-add' "$GH_LOG" || true)"
assert_eq "#587: ...and exits EX_RATE (75), the back-off signal" "75" "$add_rl_rc"

# ================================================================================================
# #520 / #485 RESIDUE — THE SURVIVING DISAGREEMENTS
# ================================================================================================
# #485's fix landed (`batch` is THE predicate; `next` and `take` delegate to it). But consolidating
# five copies into one only helps if the one is RIGHT, and three disagreements outlived the merge.


harness_report

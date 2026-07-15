#!/usr/bin/env bash
# case: expired lease
# tier: full
# covers: reap claim
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="26-expired-lease"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# --- state the monolith inherited from an earlier section; stated explicitly here ---
seed_offboard_world

# ---- #581: an expired lease is EVIDENCE of abandonment. It is not PROOF. ------------------------
# The false positive is systematic, not incidental: WORK THAT TAKES LONGER THAN THE LEASE. And the
# protocol's own remedy — heartbeat — is what a busy worker forgets precisely when the work is long.
# `take --repo rendering` handed out FS.GG.Rendering#429 with PR #433 OPEN on `item/429-*`, because a
# loaded box stretched one build past 120 minutes. Then it happened AGAIN, to the worker fixing #485:
# the claim lapsed mid-test-cycle and was reaped with the work uncommitted. It survived only because
# the reaping worker chose to preserve the worktree as a WIP commit. That is generosity, not a lock.
#
# An open PR on `item/<n>-*` is the worktree protocol's OWN artifact, and it is server-side proof.

# Seed our OWN stale claim: the reap test above legitimately collected #216's, and a test that
# depends on another test's leftovers is a test that passes for the wrong reason.
jq -n --arg ts "$stale_ts" '[{id:880, body:"<!-- fsgg:claim worker=ghost-222 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-216.json"
assert_eq "#581: precondition — the expired claim is back" "ghost-222" "$(workers_on 216)"

# reap must REFUSE to destroy work that is demonstrably alive.
reap_live="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="216:433" \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>&1 || true)"
assert_contains "#581: reap REFUSES a claim whose PR is open — the lease lapsed, the WORK did not" \
  "REFUSING" "$reap_live"
assert_contains "#581: ...and names the PR, so the refusal is checkable" "#433" "$reap_live"
# THE ONE THAT MATTERS: the marker must still be there. A refusal that deleted anyway is the bug.
assert_eq "#581: ...and the claim SURVIVES — this is the leg that reaped live work twice" \
  "ghost-222" "$(workers_on 216)"

# ...and with NO open PR it still reaps. Without this the assertion above is satisfied by a reap that
# simply stopped working — the #436 shape, guarding the mechanism that stops live work being destroyed.
reap_dead="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_FAIL_ITEM_EDIT=1 \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>/dev/null || true)"
assert_contains "#581: an expired claim with NO open PR is still reaped (the negative control)" \
  "reaped  FS.GG.Rendering#216" "$reap_dead"
assert_eq "#581: ...and its marker is gone" "" "$(workers_on 216)"

# `who` must SAY it. `STALE` and `STALE (PR #433 OPEN)` are not the same fact, and `who` is what a
# human reads immediately before deciding to reap. Its own subject, in its own repo — a test that
# leans on another test's leftovers passes for the wrong reason.
seed_issue 890 "Long build, lapsed lease" 'src/Long890/**'
jq -n --arg ts "$stale_ts" '[{id:890, body:"<!-- fsgg:claim worker=shrike-a91 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-890.json"
who_live="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="890:433" \
  bash "$COORD" who --repo sdd --json 2>/dev/null || true)"
assert_eq "#581: who carries the proof of life on the STALE row" "#433 item/890-live-work" \
  "$(jq -r '.[] | select(.number == 890) | .livePr // ""' <<<"$who_live")"
who_txt="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="890:433" \
  bash "$COORD" who --repo sdd 2>/dev/null || true)"
assert_contains "#581: ...and the human-facing row says STALE (#433 OPEN), not a bare STALE" \
  "STALE (#433 OPEN)" "$who_txt"
# The negative control: with no open PR it is a bare STALE, and a reaper may collect it.
who_dead="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" who --repo sdd 2>/dev/null || true)"
case "$who_dead" in
  *"STALE (#"*) bad "#581: with no open PR the row must be a BARE stale" "$who_dead" ;;
  *)            ok  "#581: with no open PR the row must be a BARE stale" ;;
esac


harness_report

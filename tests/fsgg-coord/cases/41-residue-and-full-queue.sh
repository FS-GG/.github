#!/usr/bin/env bash
# case: residue and full queue
# tier: full
# covers: lint next batch take
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="41-residue-and-full-queue"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- 1. A CLOSED ISSUE IS NOT SCHEDULABLE (#520). The sixth disagreement. ------------------------
# Candidate selection read the board `Status` column and NOTHING ELSE. `board_items` has carried
# `.state` all along; nothing ever read it. So an issue whose column was never flipped to `Done`
# stayed a candidate FOREVER — FS.GG.Rendering#502 was handed to a worker two hours after it was
# closed as completed, and #481's restore-the-column logic then promoted it Backlog -> Ready on
# release, re-arming it for the next one. `lint` always knew (`select(.state == "OPEN")`); the
# scheduler simply never asked.
b520="$(run batch --repo FS.GG.Rendering --json 2>/dev/null || true)"
case "$b520" in
  *502*) bad "#520: a CLOSED issue must NOT be schedulable, however the board column reads" "$b520" ;;
  *)     ok  "#520: a CLOSED issue must NOT be schedulable, however the board column reads" ;;
esac
# ...and it must SAY so, not drop it silently. A queue that shrinks without explanation is #440.
b520r="$(run batch --repo FS.GG.Rendering 2>&1 || true)"
assert_contains "#520: ...and the reason names the issue state, not a mystery" \
  "the issue is closed" "$b520r"
# The board column is a PROJECTION; the issue is the WORK. `ready` must still SHOW it — only the
# SCHEDULER refuses to hand it out — because /check-board is what reconciles the column.
r520="$(run ready --repo FS.GG.Rendering 2>/dev/null || true)"
assert_contains "#520: ...but `ready` still SHOWS it, so /check-board can reconcile the column" \
  "502" "$r520"

# ---- 2. A MERGED BLOCKER IS RESOLVED — IN EVERY SURFACE, NOT JUST `.blocked` (#476). -------------
# #476 taught `board_annotate` that a `Blocked by` naming a PR clears on CLOSED *or* MERGED. Two
# copies of the pre-#476 rule survived: `board_table`'s BLOCKED BY column and `take`'s "a BLOCKED
# queue, not an empty one" diagnostic. So `.blocked` said false while the display and the diagnostic
# both named a MERGED pull request as the reason — sending a worker to go and look at FINISHED work.
# There is ONE definition now (JQ_BLOCKER_RULE) and every site consumes it.
r476="$(run ready --repo FS.GG.Templates 2>/dev/null || true)"
merged_row="$(printf '%s\n' "$r476" | grep '350' || true)"
case "$merged_row" in
  *"#449"*) bad "#476: a MERGED blocker must not be listed as still blocking (board_table)" "$merged_row" ;;
  *)        ok  "#476: a MERGED blocker must not be listed as still blocking (board_table)" ;;
esac
# ...and the item is genuinely schedulable, not merely displayed as unblocked.
b476="$(run batch --repo FS.GG.Templates --json 2>/dev/null || true)"
assert_contains "#476: ...and the item it blocked is startable again" "350" "$b476"

# ---- 3. `lint` WAS GREEN OVER AN ITEM `batch` WILL NEVER SCHEDULE (#496, reopened). --------------
# `touchset_decl` asked only whether a `Paths:` line EXISTED — it never applied the grammar. So an
# item whose every token is unmatchable (`**/packages.lock.json`: a leading `**/` matches nothing,
# and a token that matches nothing CONFLICTS with nothing) was skipped forever by `batch` while the
# one surface whose job is board health reported `0 error(s)`. That is #496's own defect, reopened
# for the unmatchable case, and it is why the rule is now shared rather than re-spelled.
lint31="$(run lint --repo FS.GG.Audio --json 2>/dev/null || true)"
assert_contains "#496: lint goes RED on an item whose every touch-set token is unmatchable" \
  "BAD-TOUCH-SET" "$lint31"
assert_contains "#496: ...and names the token, and the grammar" "packages.lock.json" "$lint31"
# The two deaths are DIFFERENT deaths, and must not be conflated: one declared nothing, one declared
# something unusable. #496's whole point was making the distinction machine-readable.
assert_eq "#496: ...and it is NOT reported as NO-TOUCH-SET — a declaration was made, it is just dead" \
  "0" "$(printf '%s' "$lint31" | jq -r '[.[] | select(.id | test("#31")) | select(.code == "NO-TOUCH-SET")] | length' 2>/dev/null || echo 0)"
# ...and a WELL-FORMED touch-set stays green. Without this the assertion above is satisfied by a lint
# that simply reds everything.
lint_ok="$(run lint --repo FS.GG.Templates --json 2>/dev/null || true)"
case "$lint_ok" in
  *BAD-TOUCH-SET*) bad "#496: a well-formed touch-set must NOT be flagged" "$lint_ok" ;;
  *)               ok  "#496: a well-formed touch-set must NOT be flagged" ;;
esac

# ================================================================================================
# #440 — A FULL QUEUE MUST NOT READ AS AN EMPTY ONE. `next` picks "the first Ready, else the first
# Backlog"; `take` stopped at Ready. So on a board with zero Ready items and startable Backlog ones —
# which is what `.github` looked like — `take` reported:
#
#   no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared.
#
# Every clause of that was false, and the true reason (the item is in Backlog) was not among them. A
# worker that believes the message idles in front of work it could have taken. The two commands a
# worker treats as "what do I do next" must agree about what EXISTS, and neither may name a cause it
# did not observe.
bl() { PATH="$STUB:$PATH" GH_BOARD_SET=bl GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
         bash "$COORD" --worker crake-440 "$@"; }
seed_issue 520 "Startable, but merely Backlog" "src/Bl520/**"
seed_issue 521 "Backlog, and undeclared" ""

take440="$(bl take --repo FS.GG.SDD 2>&1 || true)"
assert_contains "#440: take falls back to Backlog when no Ready item exists" \
  "claimed FS.GG.SDD#520" "$take440"
assert_contains "#440: ...and SAYS the pick came from Backlog (it is not a Ready item)" \
  "from Backlog" "$take440"
assert_eq "#440: ...and the claim marker really landed" "crake-440" "$(workers_on 520)"
# The undeclared Backlog item is still refused — the fallback widens WHICH statuses are reachable,
# not what counts as schedulable. An item with no touch-set can still not be scheduled.
case "$take440" in
  *"#521"*) bad "#440: an undeclared item must NOT become schedulable via the Backlog fallback" "$take440" ;;
  *)        ok  "#440: an undeclared item must NOT become schedulable via the Backlog fallback" ;;
esac

# Now the empty case: with #520 claimed, only the undeclared #521 remains. `take` must report the
# reason BATCH observed, per item — not recite a fixed list of causes.
take440b="$(bl take --repo FS.GG.SDD 2>&1 || true)"
assert_contains "#440: an empty queue names the REAL per-item reason" \
  "#521 — no 'Paths:' declared" "$take440b"
case "$take440b" in
  *"every candidate is blocked, claimed, overlapping, or undeclared"*)
    bad "#440: take must not recite a guessed list of causes" "$take440b" ;;
  *) ok "#440: take must not recite a guessed list of causes" ;;
esac
# ...and `batch --json` must EMIT those reasons on stderr, or `take` has nothing true to relay. This
# is the assertion that keeps the two halves of the fix from drifting apart.
berr440="$(bl batch --repo FS.GG.SDD --include-backlog -n 1 --json 2>&1 >/dev/null || true)"
assert_contains "#440: batch --json still reports 'passed over' reasons on stderr" \
  "no 'Paths:' declared" "$berr440"


harness_report

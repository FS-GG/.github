#!/usr/bin/env bash
# case: pr existence 697
# tier: full
# covers: who adopt
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="30-pr-existence-697"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #697: the protocol reads WHETHER a PR exists, and never WHAT IT SAYS. ----------------------
# #581 (above) taught the tools that an open `item/<n>-*` PR is proof of life, and stopped there. So
# `reap` refuses such a claim — correctly — and then offers exactly ONE exit: *close it, then reap*.
# For a PR that is green, reviewed and mergeable, that exit DESTROYS the best work on the board, and
# it is the path of least resistance: a worker who follows the tool's own advice literally does it.
#
#   open, still being worked         -> leave it             (#581)
#   open, abandoned mid-flight       -> close it, then reap  (#581)
#   open, FINISHED: green+mergeable  -> LAND IT              (this — the row the tool used to bin)
#
# The third row is the SUCCESS path of a worker whose harness died between "green" and "merge", and
# that window is minutes long on EVERY item this protocol produces. FS.GG.Rendering#681 sat done and
# green for 18 hours behind a dead worker.
GREEN_SHA=deadbeefcafe
cat >"$FIXTURES/pr-701.json" <<'JSON'
{"number":701,"mergeable":true,"head":{"ref":"item/970-finished-work","sha":"deadbeefcafe"}}
JSON
cat >"$FIXTURES/checks-deadbeefcafe.json" <<'JSON'
{"check_runs":[{"name":"build","status":"completed","conclusion":"success","app":{"slug":"github-actions"}},
               {"name":"lint","status":"completed","conclusion":"skipped","app":{"slug":"github-actions"}}]}
JSON
# The SAME green PR, as WORKFLOW RUNS — which is what pr_landable now scores (#720). The check-runs
# fixture above stays: it is still read, but only for check-runs from NON-Actions apps (see #720's
# fail-open leg below), so these two must agree about this PR being green.
cat >"$FIXTURES/runs-deadbeefcafe.json" <<'JSON'
{"workflow_runs":[
  {"path":".github/workflows/build.yml","event":"pull_request","head_branch":"item/970-finished-work",
   "run_number":1,"status":"completed","conclusion":"success","pull_requests":[{"number":701}]},
  {"path":".github/workflows/lint.yml","event":"pull_request","head_branch":"item/970-finished-work",
   "run_number":1,"status":"completed","conclusion":"skipped","pull_requests":[{"number":701}]}]}
JSON
seed_issue 970 "Finished, green, and orphaned" 'src/Orphan970/**'
jq -n --arg ts "$stale_ts" '[{id:970, body:"<!-- fsgg:claim worker=ghost-970 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-970.json"

g() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="970:701" \
      bash "$COORD" --worker heron-697 "$@"; }

# 1. `who` must SAY the work is finished. It is what a human reads immediately before reaping, so it
#    is exactly where "GREEN: LAND IT" has to appear — a bare `STALE (#701 OPEN)` reads as somebody's
#    abandoned branch, and the reader reaches for `reap`.
who697="$(g who --repo sdd 2>&1 || true)"
assert_contains "#697: who says a stale claim's GREEN PR is FINISHED work, not an abandoned branch" \
  "STALE (#701 OPEN — GREEN: LAND IT)" "$who697"
assert_contains "#697: ...and points at the command that lands it, not the one that bins it" \
  "fsgg-coord adopt" "$who697"
assert_eq "#697: who carries the PR's STATE on the stale row, not just its existence" "green" \
  "$(g who --repo sdd --json 2>/dev/null | jq -r '.[] | select(.number == 970) | .prState // ""')"

# 2. THE ONE THAT MATTERS. `reap` must not point the destructive verb at finished work.
reap697="$(g reap --repo sdd --apply 2>&1 || true)"
assert_contains "#697: reap REFUSES a claim whose PR is green and mergeable" "REFUSING" "$reap697"
assert_contains "#697: ...and calls the work FINISHED" "FINISHED" "$reap697"
assert_contains "#697: ...and names \`adopt\` as the remedy" "fsgg-coord adopt" "$reap697"
case "$reap697" in
  *"close it, then reap"*)
    bad "#697: reap must NEVER advise closing a GREEN, mergeable PR — that is the loaded gun" "$reap697" ;;
  *) ok "#697: reap must NEVER advise closing a GREEN, mergeable PR — that is the loaded gun" ;;
esac
assert_eq "#697: ...and the claim SURVIVES the refusal" "ghost-970" "$(workers_on 970)"

# 3. `adopt` transfers the lock and hands over the merge.
adopt697="$(g adopt 'FS.GG.SDD#970' 2>&1 || true)"
assert_contains "#697: adopt confirms the PR is green and mergeable before touching anything" \
  "GREEN and MERGEABLE" "$adopt697"
assert_contains "#697: adopt hands the worker the MERGE, and says not to close the PR" \
  "Do NOT rebuild it, and do NOT close PR #701" "$adopt697"
assert_eq "#697: adopt TRANSFERS the claim — one marker, one lock, the CAS's total order intact" \
  "heron-697" "$(workers_on 970)"

# 4. THE GATE. `adopt` lands FINISHED work and NOTHING else. Each refusal below is a state in which
#    "adopt" would mean something other than *finish somebody's finished work*.

# 4a. A conflicted PR is not finished — rebasing it is AUTHORING, not landing. And note it is exactly
#     the state the caller is most likely to be staring at, because a conflicted PR gets NO CI at all.
cat >"$FIXTURES/pr-702.json" <<'JSON'
{"number":702,"mergeable":false,"head":{"ref":"item/971-conflicted","sha":"c0nflicted"}}
JSON
seed_issue 971 "Orphaned but conflicted" 'src/Orphan971/**'
jq -n --arg ts "$stale_ts" '[{id:971, body:"<!-- fsgg:claim worker=ghost-971 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-971.json"
conf="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="971:702" \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#971' 2>&1 || true)"
assert_contains "#697: adopt REFUSES a conflicted PR — rebasing is authoring, not landing" \
  "CONFLICTED" "$conf"
assert_eq "#697: ...and does NOT take the lock on it" "ghost-971" "$(workers_on 971)"

# 4b. ZERO check runs is NOT green (#606). "Every check passed" and "CI never started" are the SAME
#     EMPTY SET, and a conflicted PR has zero check runs forever. An absent subject is a finding, not
#     a pass — so this must refuse, and refusing it is the whole of epic #266 in one assertion.
cat >"$FIXTURES/pr-703.json" <<'JSON'
{"number":703,"mergeable":true,"head":{"ref":"item/972-no-checks","sha":"n0checks"}}
JSON
cat >"$FIXTURES/checks-n0checks.json" <<'JSON'
{"check_runs":[]}
JSON
# The same "CI never started" state, as WORKFLOW RUNS — the rollup pr_landable actually scores (#720).
# This is ALSO the permanent state of a CONFLICTED PR: GitHub cannot build refs/pull/N/merge while it
# conflicts, so no workflow ever starts and the head SHA has zero runs forever.
cat >"$FIXTURES/runs-n0checks.json" <<'JSON'
{"workflow_runs":[]}
JSON
seed_issue 972 "Orphaned, mergeable, and never tested" 'src/Orphan972/**'
jq -n --arg ts "$stale_ts" '[{id:972, body:"<!-- fsgg:claim worker=ghost-972 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-972.json"
nock="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="972:703" \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#972' 2>&1 || true)"
assert_contains "#697/#606: a mergeable PR with ZERO check runs is NOT green — adopt refuses it" \
  "NOT green" "$nock"
assert_eq "#697/#606: ...and does NOT take the lock on untested work" "ghost-972" "$(workers_on 972)"

# 4c. A LIVE claim is not an orphan. Adopting one is a STEAL, and the steal has its own flag.
seed_issue 973 "Alive and well" 'src/Orphan973/**'
jq -n --arg ts "$fresh_ts" '[{id:973, body:"<!-- fsgg:claim worker=busy-973 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-973.json"
livec="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="973:701" \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#973' 2>&1 || true)"
assert_contains "#697: adopt REFUSES a LIVE claim — a worker that is alive is not an orphan" \
  "held by a LIVE claim" "$livec"
assert_eq "#697: ...and the live worker keeps its lock" "busy-973" "$(workers_on 973)"

# 4d. No PR at all: nothing to land. The claim is merely DEAD, and `reap` is the right tool — an
#     `adopt` that fell through to a plain claim here would quietly become a second, unguarded steal.
seed_issue 974 "Dead, with nothing to show for it" 'src/Orphan974/**'
jq -n --arg ts "$stale_ts" '[{id:974, body:"<!-- fsgg:claim worker=ghost-974 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-974.json"
nopr="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#974' 2>&1 || true)"
assert_contains "#697: adopt REFUSES an item with no open PR — there is no finished work to land" \
  "no finished work to adopt" "$nopr"
assert_eq "#697: ...and leaves the dead claim for reap" "ghost-974" "$(workers_on 974)"

# 4e. A MERGEABLE PR WHOSE CHECKS ARE STILL RUNNING IS NOT ABANDONED — it is one CI run from being
#     FINISHED. `adopt` must refuse it (a pending check is not a passing one), but `reap` must NOT
#     tell anyone to close it: that is the same loaded gun this change exists to remove, fired a few
#     minutes early. "Not green YET" is not "not green".
cat >"$FIXTURES/pr-705.json" <<'JSON'
{"number":705,"mergeable":true,"head":{"ref":"item/976-still-running","sha":"pendingsha"}}
JSON
# `app.slug` is NOT decoration: pr_landable scores Actions via WORKFLOW RUNS and reads check-runs only
# for the NON-Actions apps (#720). A fixture with no app would be counted as a third-party check, and
# these legs would pass for entirely the wrong reason.
cat >"$FIXTURES/checks-pendingsha.json" <<'JSON'
{"check_runs":[{"name":"build","status":"completed","conclusion":"success","app":{"slug":"github-actions"}},
               {"name":"test","status":"in_progress","conclusion":null,"app":{"slug":"github-actions"}}]}
JSON
cat >"$FIXTURES/runs-pendingsha.json" <<'JSON'
{"workflow_runs":[
  {"path":".github/workflows/build.yml","event":"pull_request","head_branch":"item/976-still-running",
   "run_number":1,"status":"completed","conclusion":"success","pull_requests":[{"number":705}]},
  {"path":".github/workflows/test.yml","event":"pull_request","head_branch":"item/976-still-running",
   "run_number":1,"status":"in_progress","conclusion":null,"pull_requests":[{"number":705}]}]}
JSON
seed_issue 976 "Orphaned mid-CI" 'src/Orphan976/**'
jq -n --arg ts "$stale_ts" '[{id:976, body:"<!-- fsgg:claim worker=ghost-976 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-976.json"
pend_adopt="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="976:705" \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#976' 2>&1 || true)"
assert_contains "#697: adopt refuses a PR whose checks are still RUNNING — pending is not passing" \
  "checks RUNNING" "$pend_adopt"
pend_reap="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="976:705" \
  bash "$COORD" --worker heron-697 reap --repo sdd --apply 2>&1 || true)"
case "$pend_reap" in
  *"close it, then reap"*)
    bad "#697: reap must NOT advise closing a MERGEABLE PR whose checks are still running" "$pend_reap" ;;
  *) ok "#697: reap must NOT advise closing a MERGEABLE PR whose checks are still running" ;;
esac
assert_contains "#697: ...it says the work is UNFINISHED, not abandoned, and to look again" \
  "Do NOT close it" "$pend_reap"
assert_eq "#697: ...and the claim survives" "ghost-976" "$(workers_on 976)"

# 5. `mergeable` IS COMPUTED LAZILY. The first read of an untested PR returns null, and only a later
#    read carries the truth. A client that believed the first read would call a CONFLICTED PR
#    "unknown" — or, with jq's `//` operator, would fold `false` into the fallback and call it that
#    too. Observed live while this item was being worked: PR #692 read null, then resolved to dirty.
#    GH_PR_LAZY makes the stub do exactly what GitHub does.
cat >"$FIXTURES/pr-704.json" <<'JSON'
{"number":704,"mergeable":false,"head":{"ref":"item/975-lazy","sha":"lazysha"}}
JSON
seed_issue 975 "Mergeability not computed yet" 'src/Orphan975/**'
jq -n --arg ts "$stale_ts" '[{id:975, body:"<!-- fsgg:claim worker=ghost-975 lease=120 -->\ndead",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-975.json"
lazy="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_LIVE_PR="975:704" GH_PR_LAZY=704 \
  bash "$COORD" --worker heron-697 adopt 'FS.GG.SDD#975' 2>&1 || true)"
assert_contains "#697: a null \`mergeable\` is re-read, and the PR's REAL state (conflicted) is seen" \
  "CONFLICTED" "$lazy"
assert_eq "#697: ...and the lock is not taken on a PR we misread as landable" "ghost-975" "$(workers_on 975)"


harness_report

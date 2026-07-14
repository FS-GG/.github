#!/usr/bin/env bash
# case: child subissues
# tier: full
# covers: child
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="15-child-subissues"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- `child`: the only thing that actually creates the edge rollup reads ------------------------
# The epic #302 and the child #47 the rollup above just refused to roll up over — written here
# (`seed_issue` is defined further down, with the ADR-0027 fixtures) so the fix reads next to the
# failure it repairs. `id` is NOT the number: the endpoint keys on the REST id.
for n in 302 47 49; do
  jq -n --argjson n "$n" '{id:($n + 1000), number:$n, title:"fixture", body:"", assignees:[],
    state:"open", repo:"FS-GG/FS.GG.SDD"}' >"$STORE/issue-$n.json"
  echo '[]' >"$STORE/comments-$n.json"
done
: >"$GH_LOG"
linked="$(run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>/dev/null)"
assert_contains "child: links the issue as a sub-issue" "linked FS.GG.SDD#47 as a sub-issue of FS.GG.SDD#302" "$linked"
# The endpoint keys on the REST id (1047), never the issue number (47).
assert_contains "child: POSTs the child's REST id, not its number" "sub_issue_id=1047" "$(cat "$GH_LOG")"
assert_contains "child: sends it with -F, the typed form" "-F sub_issue_id=1047" "$(cat "$GH_LOG")"
# ...and that success is not vacuous: the stub really does 422 the `-f` string form, so `child`
# passing above means it used `-F`. A failure leg that cannot fire is the defect this epic is about
# (#266) — SDD#299's `/tmp` fixture is the same shape of mistake.
assert_fails "child: the stub's -f leg is real — a string sub_issue_id 422s" \
  env PATH="$STUB:$PATH" gh api "repos/FS-GG/FS.GG.SDD/issues/302/sub_issues" -f sub_issue_id=1047
# Re-linking is success, not a 422 — a worker re-running its close-out must not have to check first.
again="$(run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>/dev/null)"
assert_contains "child: is idempotent" "already a sub-issue" "$again"
assert_fails "child: refuses a missing argument" run child 'FS.GG.SDD#302'

# An unreachable existing-links read must FAIL CLOSED. Swallowing it would make "I could not check"
# indistinguishable from "the edge is absent": `child` would POST, collect a 422, and blame the
# token. That is #320's defect exactly — reappearing inside the fix for its own epic.
: >"$GH_LOG"
unreachable="$(GH_FAIL_SUBISSUES_GET=302 run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' 2>&1 || true)"
assert_contains "child: an unreachable sub-issue read is refused, not guessed" \
  "refusing to guess whether" "$unreachable"
assert_eq "child: ...and it exits non-zero" "1" \
  "$(GH_FAIL_SUBISSUES_GET=302 run child 'FS.GG.SDD#302' 'FS.GG.SDD#47' >/dev/null 2>&1; echo $?)"
assert_eq "child: ...and POSTs nothing while it cannot tell" "0" \
  "$(grep -c 'sub-issue-add' "$GH_LOG" || true)"
# A failing POST surfaces gh's own diagnosis (422 vs 403), not a guessed cause.
assert_contains "child: a failed link reports the API's error, not a guess" "422" \
  "$(GH_FORCE_SUBISSUE_POST_FAIL=1 run child 'FS.GG.SDD#302' 'FS.GG.SDD#49' 2>&1 || true)"

# budget reads both meters.
bud="$(run budget)"
assert_contains "budget: reports graphql meter" "graphql" "$bud"
assert_contains "budget: reports remaining"     "remaining" "$bud"

# ================================================================================================
# ADR-0027 — parallel intra-repo work: worker identity, the comment-order claim lock, the
# schedulable batch, the worker channel, and the touch-set drift check.
#
# The regression this whole section exists for: under ADR-0021 the lock was the issue ASSIGNEE, and
# N agents authenticating as ONE GitHub account all resolve `@me` to the same login — so a second
# worker's claim on a held item sailed through and both worked it. The lock is now keyed on a WORKER
# ID and resolved by comment-order CAS. `claim: a second worker on the SAME account is refused`
# below is the assertion that would have caught the original bug.
# ================================================================================================
echo "--- ADR-0027: parallel intra-repo work ---"

# The parallel-work board: three items In progress (held / stale / unclaimed) and five Ready.

harness_report

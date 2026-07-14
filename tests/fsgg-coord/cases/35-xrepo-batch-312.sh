#!/usr/bin/env bash
# case: xrepo batch 312
# tier: full
# covers: batch take
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="35-xrepo-batch-312"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #312: batch/take qualify their touch-set comparison by repo, even when scheduling ALL repos --
# #353 fixed the single-item comparers (`widen`, `overlap --active`) by SCOPING their input to one
# repo. `batch` cannot do that: with no `--repo` it schedules across the whole board, so its
# reservation set legitimately mixes repos. It used to flatten every live claim's tokens into one bare
# list and hand it to `conflicts_between` — which cannot see a repo — so `src/Physics/**` held in one
# repo phantom-collided with `src/Physics/**` Ready in ANOTHER, and the scheduler passed over an item
# nothing was actually holding (#312). The fix tags each reserved token with its owning repo and
# compares only within a repo.
#
# The fixture is built on repos with EMPTY stores (Templates/Governance/Audio/Game) so unscoped
# `active_claims` sees only these planted claims — not the polluted SDD/Rendering stores earlier tests
# left behind. The token `src/Physics/**` is unique to this block for the same reason.
#   #420 Templates  Ready        src/Physics/**          — candidate; only a CROSS-repo namesake holds it
#   #421 Governance Ready        src/Physics/**          — candidate in a THIRD repo, SAME bare token
#   #422 Audio      Ready        src/Physics/Solver.fs   — REAL same-repo overlap of the in-flight #424
#   #425 Templates  Ready        src/Physics/Gun.fs      — REAL same-repo overlap of the batch-mate #420
#   #423 Game       In progress  src/Physics/**          — cross-repo phantom (its own repo, no candidate)
#   #424 Audio      In progress  src/Physics/**          — the genuine same-repo neighbour #422 clashes with
cat >"$FIXTURES/board-xbatch.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":420,"title":"Templates A","url":"https://github.com/FS-GG/FS.GG.Templates/issues/420","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":421,"title":"Governance B","url":"https://github.com/FS-GG/FS.GG.Governance/issues/421","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":422,"title":"Audio control","url":"https://github.com/FS-GG/FS.GG.Audio/issues/422","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":425,"title":"Templates mate","url":"https://github.com/FS-GG/FS.GG.Templates/issues/425","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Templates"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":423,"title":"Game phantom","url":"https://github.com/FS-GG/FS.GG.Game/issues/423","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Game"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":424,"title":"Audio neighbour","url":"https://github.com/FS-GG/FS.GG.Audio/issues/424","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4970}}
JSON
seed_issue 420 "Templates A"      "src/Physics/**"        "FS-GG/FS.GG.Templates"
seed_issue 421 "Governance B"     "src/Physics/**"        "FS-GG/FS.GG.Governance"
seed_issue 422 "Audio control"    "src/Physics/Solver.fs" "FS-GG/FS.GG.Audio"
seed_issue 425 "Templates mate"   "src/Physics/Gun.fs"    "FS-GG/FS.GG.Templates"
seed_issue 423 "Game phantom"     "src/Physics/**"        "FS-GG/FS.GG.Game"
seed_issue 424 "Audio neighbour"  "src/Physics/**"        "FS-GG/FS.GG.Audio"
# Live markers on the two in-flight items (a claim marker IS a comment; the store is the source).
jq -n --arg ts "$fresh_ts" '[{id:8423, body:"<!-- fsgg:claim worker=game-x1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-423.json"
jq -n --arg ts "$fresh_ts" '[{id:8424, body:"<!-- fsgg:claim worker=audio-n1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-424.json"
# Runs from a directory that is NO CHECKOUT, deliberately. Since #480 a bare `batch` scopes to the repo
# you are standing in, and this section is about the ONE case that can only be seen across repos: a
# phantom cross-repo overlap. Standing nowhere is how you legitimately ask for the whole board — and it
# keeps this test measuring `conflicts_between`, not the cwd of whoever ran the fixture.
pb()    { ( cd "$NOGIT" && PATH="$STUB:$PATH" GH_BOARD_SET=xbatch GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@" ); }
# ...and the same stub, STANDING in a Templates checkout, to prove the scope actually bites here.
pb_tpl() { ( cd "$CO_TPL" && PATH="$STUB:$PATH" GH_BOARD_SET=xbatch GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@" ); }

# The whole board, no --repo: the cross-repo phantom (#423) and same-repo neighbour (#424) are BOTH
# in flight, but only #422/#425 (real same-repo overlaps) may be dropped. #420 clears the phantom;
# #421 rides alongside #420 though both declare the same bare token in different repos.
xb_json="$(pb batch --json 2>/dev/null)"
assert_eq "batch: schedules cross-repo candidates sharing only a repo-relative token (#312)" \
  '["FS.GG.Templates#420","FS.GG.Governance#421"]' "$(jq -c '.' <<<"$xb_json")"
xb_err="$(pb batch 2>&1 >/dev/null || true)"
# The two REAL same-repo overlaps are still caught — scoping narrowed the comparison, not the check.
assert_contains "batch: a genuine same-repo IN-FLIGHT overlap is still caught (#312)" \
  "#422 — overlaps in-flight work" "$xb_err"
assert_contains "batch: a genuine same-repo BATCH-MATE overlap is still caught (#312)" \
  "#425 — overlaps batch member FS.GG.Templates#420" "$xb_err"

# #480, where it bites: the SAME board, but STANDING in the Templates checkout. Org-wide it schedules
# FS.GG.Templates#420 AND FS.GG.Governance#421 — a worker in the Templates tree must be offered only
# #420. The Governance item is real work, it is simply not work you can do from this checkout, and
# handing it over comes with a worktree command built against the wrong repository's origin/main.
assert_eq "batch: standing in Templates, the Governance candidate is NOT offered [#480]" \
  '["FS.GG.Templates#420"]' "$(pb_tpl batch --json 2>/dev/null | jq -c '.')"
# The ssh remote form resolves identically — a worker who cloned over ssh is not a different worker.
# ($CO_TPL's origin is git@github.com:FS-GG/FS.GG.Templates.git, so this leg also proves that parse.)
assert_eq "batch: ...and the ssh remote form resolves to the same scope [#480]" \
  "1" "$(pb_tpl batch --json 2>/dev/null | jq 'length')"
# ...and neither cross-repo candidate is ever passed over for a phantom.
case "$xb_err" in
  *"#420 — overlaps"*|*"#421 — overlaps"*)
    bad "batch: never drops a candidate for a cross-repo phantom (#312)" "$xb_err" ;;
  *) ok "batch: never drops a candidate for a cross-repo phantom (#312)" ;;
esac


harness_report

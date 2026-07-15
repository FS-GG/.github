#!/usr/bin/env bash
# case: xrepo touchset 353
# tier: full
# covers: widen overlap
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="34-xrepo-touchset-353"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #353: widen / overlap --active scope their touch-set check to ONE repo ---------------------
# `Paths:` tokens are repo-relative. `batch` compares only within a repo (`active_claims "$repo"`),
# but `widen` and `overlap --active` used to call `active_claims` with NO repo — so an item's tokens
# were compared against EVERY repo's live claims. `scripts/fsgg-coord` in one repo then "collided"
# with `scripts/fsgg-coord` in another, two strings that name files in two different repositories.
# The fail is not the dangerous direction (it never hands two workers one file), but it stops a
# worker who has nothing to stop for and posts a false "sequence behind me" notice onto a stranger's
# item — and it is INCOHERENT with the scheduler, which would happily run the pair in parallel.
#
# The fixture: #401 (SDD) is the item we probe. #402 (Rendering, in flight) declares the SAME bare
# token `scripts/fsgg-coord` — a phantom across the repo boundary. #403 (SDD, in flight) declares
# `src/Scene/**` — a REAL same-repo neighbour, the positive control that proves scoping did not
# simply blind the check.
cat >"$FIXTURES/board-xrepo.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":401,"title":"SDD widen target","url":"https://github.com/FS-GG/FS.GG.SDD/issues/401","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":402,"title":"Rendering bystander","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/402","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":403,"title":"SDD sibling","url":"https://github.com/FS-GG/FS.GG.SDD/issues/403","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4975}}
JSON
seed_issue 401 "SDD widen target"    "scripts/fsgg-coord"  "FS-GG/FS.GG.SDD"
seed_issue 402 "Rendering bystander" "scripts/fsgg-coord"  "FS-GG/FS.GG.Rendering"
seed_issue 403 "SDD sibling"         "src/Scene/**"        "FS-GG/FS.GG.SDD"
# Live markers on the two in-flight items (a claim marker IS a comment; the store is the source).
jq -n --arg ts "$fresh_ts" '[{id:8402, body:"<!-- fsgg:claim worker=render-x1 lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-402.json"
jq -n --arg ts "$fresh_ts" '[{id:8403, body:"<!-- fsgg:claim worker=sdd-sib lease=120 -->\nheld",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-403.json"
px()  { PATH="$STUB:$PATH" GH_BOARD_SET=xrepo GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
asx() { local w="$1"; shift
        PATH="$STUB:$PATH" GH_BOARD_SET=xrepo GH_ISSUES_FROM_STORE=1 bash "$COORD" --worker "$w" "$@"; }

# overlap --active: the cross-repo namesake is NOT a collision.
ovx="$(px overlap 'FS.GG.SDD#401' --active 2>&1 || true)"
assert_contains "overlap --active: a same-named path in ANOTHER repo is not a collision (#353)" "DISJOINT" "$ovx"
case "$ovx" in *OVERLAP*|*Rendering#402*)
    bad "overlap --active: never invents a cross-repo overlap (#353)" "$ovx" ;;
  *) ok "overlap --active: never invents a cross-repo overlap (#353)" ;; esac
if px overlap 'FS.GG.SDD#401' --active >/dev/null 2>&1
  then ok  "overlap --active: exits 0 when the only namesake claim is in another repo (#353)"
  else bad "overlap --active: exits 0 when the only namesake claim is in another repo (#353)" "exited non-zero"; fi

# Pairwise `overlap a b` closes the same trap. #401 (SDD) and #402 (Rendering) BOTH declare the bare
# token `scripts/fsgg-coord` right now — the phantom-collision setup. Repo-relative tokens in two
# different repos can never name the same file, so this is DISJOINT, not the OVERLAP `conflicts_between`
# would report if handed the two token lists raw (#353). (Run before the widen tests below mutate #401.)
pov="$(px overlap 'FS.GG.SDD#401' 'FS-GG/FS.GG.Rendering#402' 2>&1 || true)"
assert_contains "overlap a b: different repos are DISJOINT even on a same-named token (#353)" \
  "different repos" "$pov"
case "$pov" in *OVERLAP*) bad "overlap a b: never invents a cross-repo overlap (#353)" "$pov" ;;
               *) ok "overlap a b: never invents a cross-repo overlap (#353)" ;; esac
if px overlap 'FS.GG.SDD#401' 'FS-GG/FS.GG.Rendering#402' >/dev/null 2>&1
  then ok  "overlap a b: exits 0 for a cross-repo pair (#353)"
  else bad "overlap a b: exits 0 for a cross-repo pair (#353)" "exited non-zero"; fi

# widen: the cross-repo namesake is NOT a collision, and its holder is NOT pestered.
before402="$(jq 'length' "$STORE/comments-402.json")"
wx="$(asx kite-t01 widen 'FS.GG.SDD#401' --paths 'scripts/fsgg-coord' 2>&1 || true)"
assert_contains "widen: a same-named path in ANOTHER repo is not a collision (#353)" "DISJOINT" "$wx"
case "$wx" in *OVERLAP*|*Rendering#402*)
    bad "widen: never invents a cross-repo overlap (#353)" "$wx" ;;
  *) ok "widen: never invents a cross-repo overlap (#353)" ;; esac
assert_eq "widen: leaves the innocent cross-repo bystander uncommented (#353)" \
  "$before402" "$(jq 'length' "$STORE/comments-402.json")"
if asx kite-t01 widen 'FS.GG.SDD#401' --paths 'scripts/fsgg-coord' >/dev/null 2>&1
  then ok  "widen: exits 0 when the only namesake claim is in another repo (#353)"
  else bad "widen: exits 0 when the only namesake claim is in another repo (#353)" "exited non-zero"; fi

# Positive control: a REAL same-repo overlap is still caught — scoping narrowed the set, not the test.
wc353="$(asx kite-t01 widen 'FS.GG.SDD#401' --paths 'src/Scene/**' 2>&1 || true)"
assert_contains "widen: a genuine SAME-repo overlap is STILL detected (#353)" \
  "now collides with FS.GG.SDD#403" "$wc353"
assert_contains "widen: ...and still notifies the same-repo worker (#353)" \
  "notified worker sdd-sib on FS.GG.SDD#403" "$wc353"
assert_fails "widen: a real same-repo collision still exits non-zero (#353)" \
  asx kite-t01 widen 'FS.GG.SDD#401' --paths 'src/Scene/**'

# ...and a genuine SAME-repo pairwise overlap is still caught (Test C left both declaring src/Scene/**).
assert_contains "overlap a b: a real same-repo overlap is STILL detected (#353)" "OVERLAP" \
  "$(px overlap 'FS.GG.SDD#401' 'FS.GG.SDD#403' 2>&1 || true)"
assert_fails "overlap a b: a real same-repo overlap still exits non-zero (#353)" \
  px overlap 'FS.GG.SDD#401' 'FS.GG.SDD#403'


harness_report

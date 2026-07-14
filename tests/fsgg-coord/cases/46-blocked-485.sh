#!/usr/bin/env bash
# case: blocked 485
# tier: full
# covers: batch next blocked
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="46-blocked-485"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ================================================================================================
# =================================================================================================
# #485 — "is this item startable?" was computed in five places and agreed in none.
#
# Four legs, asserted together because they were ONE bug: every command that answers "what can I
# start?" had its own idea of the answer, and no two agreed.
# =================================================================================================
echo "--- #485: one predicate for 'startable' ---"

# A board whose blockers are PULL REQUESTS — the case the old code could never resolve, because a PR is
# never a board item, so it could only ever be UNKNOWN, and UNKNOWN blocks. Forever.
cat >"$FIXTURES/board-blk.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#701"},"content":{"__typename":"Issue","number":700,"title":"blocked by a MERGED pr","url":"https://github.com/FS-GG/FS.GG.SDD/issues/700","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#703"},"content":{"__typename":"Issue","number":702,"title":"blocked by an OPEN pr","url":"https://github.com/FS-GG/FS.GG.SDD/issues/702","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#705"},"content":{"__typename":"Issue","number":704,"title":"blocked by a pr closed UNMERGED","url":"https://github.com/FS-GG/FS.GG.SDD/issues/704","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":706,"title":"declares its touch-set in markdown","url":"https://github.com/FS-GG/FS.GG.SDD/issues/706","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#709"},"content":{"__typename":"Issue","number":708,"title":"the board says its blocker is closed","url":"https://github.com/FS-GG/FS.GG.SDD/issues/708","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":709,"title":"STALE on the board: says CLOSED, is open","url":"https://github.com/FS-GG/FS.GG.SDD/issues/709","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":710,"title":"declares NOTHING at all","url":"https://github.com/FS-GG/FS.GG.SDD/issues/710","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":null,"content":{"__typename":"Issue","number":712,"title":"declares a token no matcher can honour","url":"https://github.com/FS-GG/FS.GG.SDD/issues/712","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
]}}}},"rateLimit":{"cost":1,"remaining":4999}}
JSON

# The same fail-open, ALONE on a board, so `take` has no choice but to reach for it (leg f).
cat >"$FIXTURES/board-blkf.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{"pageInfo":{"hasNextPage":false,"endCursor":null},"nodes":[
  {"status":{"name":"Ready"},"phase":{"name":"P2 SDD"},"blockedBy":{"text":"FS-GG/FS.GG.SDD#709"},"content":{"__typename":"Issue","number":708,"title":"the board says its blocker is closed","url":"https://github.com/FS-GG/FS.GG.SDD/issues/708","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
  {"status":{"name":"Done"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":709,"title":"STALE on the board: says CLOSED, is open","url":"https://github.com/FS-GG/FS.GG.SDD/issues/709","state":"CLOSED","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
]}}}},"rateLimit":{"cost":1,"remaining":4999}}
JSON

mkpr_state() {   # <num> <state> <merged_at|""> — a PR exactly as REST /issues/<n> serves one
  jq -n --argjson n "$1" --arg s "$2" --arg m "$3" --arg r "FS-GG/FS.GG.SDD" \
    '{number:$n, state:$s, repo:$r,
      pull_request:{ merged_at: (if $m == "" then null else $m end) },
      html_url:("https://github.com/"+$r+"/pull/"+($n|tostring))}' >"$STORE/issue-$1.json"
  echo '[]' >"$STORE/comments-$1.json"
}
mkpr_state 701 closed "2026-07-11T10:00:00Z"    # MERGED           -> resolved
mkpr_state 703 open   ""                        # OPEN             -> still blocks
mkpr_state 705 closed ""                        # closed UNMERGED  -> resolved (abandoned)

seed_issue_in FS-GG/FS.GG.SDD 700 "merged-pr blocker"   "src/S700.fs"
seed_issue_in FS-GG/FS.GG.SDD 702 "open-pr blocker"     "src/S702.fs"
seed_issue_in FS-GG/FS.GG.SDD 704 "closed-pr blocker"   "src/S704.fs"
seed_issue_in FS-GG/FS.GG.SDD 708 "stale-board blocker" "src/S708.fs"
seed_issue_in FS-GG/FS.GG.SDD 710 "declares nothing"    ""
# leg (b): the touch-set written the way a human writes one — in markdown.
seed_issue_raw 706 "markdown paths" 'Some description.

Paths: `src/S706.fs`, `tests/S706/**`' FS-GG/FS.GG.SDD
# ...and one that is genuinely broken: a LEADING `**/` matches no file, ever. Stripping backticks must
# not launder this into acceptance — an unmatchable token reserves nothing, so it stays refused (#273).
seed_issue_raw 712 "unmatchable" 'Paths: **/everything.fs' FS-GG/FS.GG.SDD
# The board says #709 is CLOSED. The SERVER says it is open. That disagreement IS leg (f).
mkissue 709 FS-GG/FS.GG.SDD open

blk() { PATH="$STUB:$PATH" GH_BOARD_SET=blk GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
# TTL=0 wherever a board SET matters: the scan cache is keyed by the board, and GH_BOARD_SET is a
# stub-side fiction the cache knows nothing about — a CACHED read would happily serve an earlier set.
blkf() { PATH="$STUB:$PATH" GH_BOARD_SET=blkf GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 bash "$COORD" "$@"; }
blkjson="$(blk batch --repo sdd -n 9 --json 2>/dev/null)"

# ---- leg (e): MERGED != CLOSED, and a PR is NEVER on the board (was #476) -------------------------
# `blocked: any(.state != "CLOSED")` is exactly right for an Issue (OPEN|CLOSED) and silently wrong for
# a PullRequest (OPEN|CLOSED|**MERGED**). The gate opened when the blocking work was ABANDONED, and
# shut forever once it was FINISHED. Live on a critical path: FS.GG.SDD#350 blocked by .github#449.
assert_eq "485(e): a MERGED pr no longer blocks forever — #700 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#700")' <<<"$blkjson")"
assert_eq "485(e): an OPEN pr still blocks — #702 is NOT offered" \
  "false" "$(jq -r 'any(.[]; . == "FS.GG.SDD#702")' <<<"$blkjson")"
assert_eq "485(e): a pr closed UNMERGED resolves too — #704 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#704")' <<<"$blkjson")"
# A bare `batch` reads the board twice (directly, and again inside active_claims) — that is the
# pre-existing baseline. Resolving three off-board PR blockers must add NOTHING to it: they are answered
# over REST, which is a different budget from the one every worker shares and this loop drains (#418).
: >"$GH_GRAPHQL_COUNT"; blk batch --repo sdd -n 9 --json >/dev/null 2>&1 || true
assert_eq "485(e): off-board blockers cost ZERO extra GraphQL — resolved over REST (#418)" \
  "2" "$(gcount)"

# ---- leg (b): a touch-set written in markdown is still a touch-set (was #435) ---------------------
# ``Paths: `src/x/**` `` normalized to tokens with the BACKTICKS ATTACHED, and the trailing-glob strip
# is anchored at end-of-string — so `/\*\*$` never matched, the `**` survived, `invalid_paths` flagged
# it, and the item was refused. A grammar-legal declaration rejected for its markdown: the item vanished
# from `batch` while `take` reported an empty queue. FS.GG.Audio#29 and #31 were both filed this way.
assert_eq "485(b): a backticked 'Paths:' line is a valid declaration — #706 is startable" \
  "true" "$(jq -r 'any(.[]; . == "FS.GG.SDD#706")' <<<"$blkjson")"
assert_contains "485(b): ...and its tokens are the paths, without the markdown" \
  "src/S706.fs" "$(blk overlap 'FS.GG.SDD#706' 'FS.GG.SDD#706' 2>&1 || true)"
assert_contains "485(b): a REAL unmatchable token is still refused, and now NAMES the grammar" \
  "no glob matcher" "$(blk batch --repo sdd 2>&1 >/dev/null || true)"

# ---- leg (a): `next` no longer recommends what `batch` refuses (was #431) -------------------------
nx="$(blk next --repo sdd 2>/dev/null)"
refute_contains "485(a): next does NOT recommend an item with no declared touch-set (#710)" \
  "FS.GG.SDD#710" "$nx"
# `-n 1` stops at the first pick, so `next` only reports on what it examined. The REASON is `batch`'s,
# in `batch`'s words — ask the full scheduling pass for it.
assert_contains "485(a): ...and the reason is the refusing check's own words, not next's guess" \
  "#710 — no 'Paths:' declared" "$(blk batch --repo sdd 2>&1 >/dev/null || true)"
# The agreement IS the fix: what `next` recommends must be what `batch` would schedule.
assert_eq "485(a): next's pick is one batch would schedule — one predicate, not two" \
  "true" "$(nxid="$(printf '%s' "$nx" | sed -n 's/^→ \([^ ]*\).*/\1/p')"; \
            jq -r --arg id "$nxid" 'any(.[]; . == $id)' <<<"$blkjson")"

# ---- leg (f): the scheduler's last word — ask the SERVER, not the cached flag (was #343) ----------
# #708's blocker is #709. The BOARD says #709 is CLOSED (a stale `content.state` — the shape #343's
# unpinned fail-open is suspected to take), so `blocked` is FALSE and `batch` offers it. The SERVER says
# #709 is OPEN. `take` must refuse rather than point a worker at work another item still owns.
assert_eq "485(f): the stale board really does offer #708 (the fail-open, reproduced)" \
  "true" "$(blkf batch --repo sdd --json 2>/dev/null | jq -r 'any(.[]; . == "FS.GG.SDD#708")')"
takeout="$(PATH="$STUB:$PATH" GH_BOARD_SET=blkf GH_ISSUES_FROM_STORE=1 FSGG_COORD_SCAN_TTL_SEC=0 \
             bash "$COORD" --worker hawk-f01 take --repo sdd 2>&1 >/dev/null || true)"
assert_contains "485(f): take REFUSES an item whose blocker is open on the server" \
  "refusing to claim" "$takeout"
assert_contains "485(f): ...and names the blocker it caught the board lying about" \
  "FS-GG/FS.GG.SDD#709" "$takeout"
# `grep -c` prints 0 AND exits 1 on no-match, so `|| echo 0` would print it twice.
assert_eq "485(f): ...and claims nothing — no marker is posted" \
  "0" "$(grep -c 'comment-post FS-GG/FS.GG.SDD 708' "$GH_LOG" || true)"


harness_report

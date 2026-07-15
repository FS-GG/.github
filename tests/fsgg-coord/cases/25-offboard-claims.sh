#!/usr/bin/env bash
# case: offboard claims
# tier: full
# covers: who reap batch inbox
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="25-offboard-claims"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# --- state the monolith inherited from an earlier section; stated explicitly here ---
seed_offboard_world

# ---- who: every live marker, wherever the board thinks the item is ------------------------------
blind_json="$(bl who --repo rendering --json 2>/dev/null)"
assert_eq "who: reports exactly the in-flight items — no more, no less" "[210,211,215,216]" \
  "$(jq -c '[.[].number] | sort' <<<"$blind_json")"
assert_eq "who: a claim whose board Status flip FAILED is held, not invisible" "held" \
  "$(jq -r '.[] | select(.number==211) | .state' <<<"$blind_json")"
assert_eq "who: ...and names its worker" "wren-c22" \
  "$(jq -r '.[] | select(.number==211) | .worker' <<<"$blind_json")"
assert_eq "who: a claim on an item that is NOT ON THE BOARD is held" "held" \
  "$(jq -r '.[] | select(.number==215) | .state' <<<"$blind_json")"
assert_eq "who: ...and carries its touch-set, read from the issue body" '["src/Off"]' \
  "$(jq -c '.[] | select(.number==215) | .paths' <<<"$blind_json")"
assert_eq "who: an off-board claim past its lease is STALE" "stale" \
  "$(jq -r '.[] | select(.number==216) | .state' <<<"$blind_json")"
assert_eq "who: In progress with no marker is still UNCLAIMED (only the column can say so)" "unclaimed" \
  "$(jq -r '.[] | select(.number==210) | .state' <<<"$blind_json")"
assert_eq "who: a markerless item's touch-set still resolves (arm-A body read)" '["src/Orphan2"]' \
  "$(jq -c '.[] | select(.number==210) | .paths' <<<"$blind_json")"
assert_eq "who: a chatty open issue with no marker is NOT in-flight work" "" \
  "$(jq -r '.[] | select(.number==217) | .number // empty' <<<"$blind_json")"

# ---- reap: a dead worker's off-board claim is collectable, so the lease self-heals --------------
assert_contains "reap: finds an expired claim the board never knew about" \
  "would reap  FS.GG.Rendering#216  worker ghost-222" "$(bl reap --repo rendering 2>/dev/null)"

# ---- batch: an off-board claim RESERVES its touch-set ------------------------------------------
# #211 is claimed (Status says Ready — the lock disagrees). #213 overlaps src/Off, which off-board
# #215 is holding. Only #212 is genuinely free.
blind_batch="$(bl batch --repo rendering --json 2>/dev/null)"
assert_eq "batch: schedules only the item no live marker touches" '["FS.GG.Rendering#212"]' \
  "$(jq -c '.' <<<"$blind_batch")"
blind_err="$(bl batch --repo rendering 2>&1 >/dev/null)"
assert_contains "batch: skips a Ready item that a marker actually holds" \
  "#211 — already claimed by worker wren-c22" "$blind_err"
assert_contains "batch: refuses to schedule over an OFF-BOARD claim's touch-set" \
  "#213 — overlaps in-flight work" "$blind_err"

# ---- #428: a skip reason must name the HOLDER and the LEASE, not just the obstacle ---------------
# The touch-set is a lock, and a lock has an owner and an expiry. Reporting only the collision tells a
# worker they are blocked and withholds both facts every remedy needs: WHO to talk to, and WHETHER the
# wait is worth it. `wren-c22`/`puffin-h11` hold FRESH claims here, so the window is the full lease.
#
# The exact MINUTE is deliberately not asserted: these claims age in real time as the fixture runs, so
# a `~120m` needle passes on a fast machine and reds on a slow one. Assert the shape (a window exists,
# and it is a countdown rather than an expiry); the EXPIRED case below pins the other branch exactly.
assert_contains "#428: a claimed item names the lease window, not just the holder" \
  "#211 — already claimed by worker wren-c22 (lease frees in ~" "$blind_err"
assert_contains "#428: an OVERLAP names the worker holding the colliding paths" \
  "#213 — overlaps in-flight work held by puffin-h11 on FS.GG.Rendering#215" "$blind_err"
assert_contains "#428: ...and its lease window" \
  "held by puffin-h11 on FS.GG.Rendering#215 (lease frees in ~" "$blind_err"
assert_contains "#428: ...and still shows WHICH paths collided" \
  "src/Off/Sub  ⇄  src/Off" "$blind_err"

# ================================================================================================
# #428: a STARVED queue is BUSY, not empty — say so, name the holders, and give a lease to wait on
# ================================================================================================
# The chokepoint this is filed against: in a repo where one file is the touch-set of nearly every
# item, ONE claim serialises the whole queue. `batch` correctly hands out nothing — and "nothing
# schedulable" reads exactly like an empty backlog, so the worker goes home from a repo with four
# items in it. The per-item reasons say why each item is out; only a LEASE says whether to wait.
#
# #223 (held, fresh) reserves src/Starve/**; #222 is claimed outright; #216's holder is DEAD, and its
# touch-set is still reserved — a claim may only be broken by `reap`, never by scheduling over it.
echo "--- #428: a starved queue names its holders and its leases ---"

cat >"$FIXTURES/board-starved.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":221,"title":"Overlaps a live claim","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/221","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":222,"title":"Claimed outright","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/222","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":224,"title":"Overlaps a DEAD holder's claim","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/224","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":225,"title":"Overlaps a MARKERLESS In progress item","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/225","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}},
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":226,"title":"In progress, outside the protocol","url":"https://github.com/FS-GG/FS.GG.Rendering/issues/226","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4978}}
JSON

seed_issue 221 "Overlaps a live claim"          "src/Starve/Sub/**" "$RND"
seed_issue 222 "Claimed outright"               "src/Solo/**"       "$RND"
seed_issue 223 "Holds src/Starve"               "src/Starve/**"     "$RND"
seed_issue 224 "Overlaps a DEAD holder's claim" "src/Dead/Sub/**"   "$RND"
seed_issue 225 "Overlaps a MARKERLESS item"     "src/Ghostly/Sub/**" "$RND"
seed_issue 226 "In progress, outside protocol"  "src/Ghostly/**"    "$RND"   # NO comments = no marker

mk_claim 222 840 kite-z01 fresh | jq -s '.' >"$STORE/comments-222.json"
mk_claim 223 841 tern-y99 fresh | jq -s '.' >"$STORE/comments-223.json"
# (#216 already carries ghost-222's STALE marker on src/Dead/**, seeded above.)

sv() { PATH="$STUB:$PATH" GH_BOARD_SET=starved GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }

starved_json="$(sv batch --repo rendering --json 2>/dev/null)"
assert_eq "#428: a starved queue schedules NOTHING (the lock still holds)" "[]" \
  "$(jq -c '.' <<<"$starved_json")"

starved_err="$(sv batch --repo rendering 2>&1 >/dev/null)"
assert_contains "#428: the starved queue is called BUSY, not empty" \
  "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by:" "$starved_err"
assert_contains "#428: ...and it names every holder, so the worker knows who to talk to" \
  "held by: ghost-222, kite-z01, tern-y99" "$starved_err"
assert_contains "#428: ...and gives a lease to decide against" \
  "soonest: lease EXPIRED — reapable" "$starved_err"
assert_contains "#428: ...and says plainly that this is not an empty backlog" \
  "this queue is BUSY, not empty" "$starved_err"

# A DEAD holder is the one blocker a worker can clear themselves — so it must not read as a wait.
assert_contains "#428: an EXPIRED lease is a reap, never a wait" \
  "#224 — overlaps in-flight work held by ghost-222 on FS.GG.Rendering#216 (lease EXPIRED — reapable)" \
  "$starved_err"
assert_contains "#428: ...and the starved-queue advice says how to collect it" \
  "1 of those lease(s) have EXPIRED — collect them: fsgg-coord reap --repo FS.GG.Rendering --apply" "$starved_err"

# A MARKERLESS `In progress` item reserves its touch-set too — `active_claims` is right to, since
# something is evidently editing those files. But there is no worker to name, no lease to wait out and
# nobody to `say` to, so it must NOT be dressed up as a holder. "held by — (lease unknown)" would
# invite a worker to wait for a marker that is never coming, and would put "—" in the holder list.
assert_contains "#428: a markerless In progress item is not reported as a HOLDER" \
  "#225 — overlaps FS.GG.Rendering#226, which the board says is In progress with NO claim marker" \
  "$starved_err"
assert_contains "#428: ...and it says there is no lease to wait out" \
  "there is no lease to wait out; see: fsgg-coord who" "$starved_err"
refute_contains "#428: ...and an unnameable reserver never appears as a holder named '—'" \
  "held by —" "$starved_err"
# It reserves, so it must not be scheduled over — but it is NOT a lease, so it must not inflate the
# queued-behind-claims count either. Three items are queued behind real claims; #225 is not one.
assert_contains "#428: ...and it is not counted as a lease the worker can wait out" \
  "3 item(s) are QUEUED BEHIND LIVE CLAIMS held by: ghost-222, kite-z01, tern-y99" "$starved_err"
assert_eq "#428: ...and the markerless item's files are still RESERVED (never scheduled over)" "[]" \
  "$(jq -c '.' <<<"$starved_json")"

# The advice must not fire when the worker actually GOT something — that queue is not starved, and a
# "this queue is BUSY" banner on a successful schedule is noise that trains workers to skip stderr.
refute_contains "#428: a queue that DID hand out work prints no starved-queue banner" \
  "QUEUED BEHIND LIVE CLAIMS" "$blind_err"

# ---- inbox: messages ride off-board claims too --------------------------------------------------
blas puffin-h11 say 'FS.GG.Rendering#215' --to hoopoe-i22 'I hold src/Off — stay out.' >/dev/null 2>&1
assert_contains "inbox: delivers a message posted on an off-board claim" "I hold src/Off — stay out." \
  "$(blas hoopoe-i22 inbox --repo rendering 2>/dev/null)"

# ---- HOW the candidate list is fetched: the lock may not be capped, nor read from a cache --------
# `issues` (the ETag'd command) asks for ONE page of 100. Had the candidate scan reused it, a live
# claim on a repo's 101st open issue would be invisible — and `batch` would hand its touch-set away.
# A conditional request is equally forbidden: a 304 serving a pre-claim `comments: 0` hides a marker.
: >"$GH_LOG"
bl who --repo rendering >/dev/null 2>&1
assert_contains "who: the open-issue scan PAGINATES (a lock has no 100-issue limit)" \
  "issue-list FS-GG/FS.GG.Rendering paginate=1" "$(cat "$GH_LOG")"
assert_contains "who: ...and is never a conditional request (no cache may hide a live marker)" \
  "inm=none" "$(cat "$GH_LOG")"

# ---- reap: an off-board claim has no board entry to reset, and reap must not pretend it did ------
reap215="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 GH_FAIL_ITEM_EDIT=1 \
  bash "$COORD" --worker wren-c22 reap --repo rendering --apply 2>/dev/null)"
assert_contains "reap --apply: still collects the claim when the board write fails" \
  "reaped  FS.GG.Rendering#216  worker ghost-222" "$reap215"
assert_contains "reap --apply: ...and does NOT claim a board reset it never performed" \
  "not on board (marker cleared; nothing to reset)" "$reap215"
assert_eq "reap --apply: the marker is gone — the lock released, board or no board" "" "$(workers_on 216)"

# ================================================================================================
# THE CLAIM'S LIFETIME IS THE WORK'S LIFETIME (#581, #533, #516)
# ================================================================================================
# Three symptoms, one missing concept. A claim marker should live exactly as long as the work does,
# and it failed at BOTH ENDS and in the middle:
#
#   #581  the WORK outlives the CLAIM — lease expiry is treated as proof of abandonment
#   #533  the CLAIM outlives the WORK — `done --flip` never dropped the marker
#   #516  one worker, N claims  — the CAS protects the ITEM; nobody protected the WORKER
#

harness_report

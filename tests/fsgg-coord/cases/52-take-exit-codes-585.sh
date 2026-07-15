#!/usr/bin/env bash
# case: take exit codes 585
# tier: full
# covers: take
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="52-take-exit-codes-585"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #585: `take`'s exit code must tell "I claimed you an item" from "I claimed you NOTHING" ------
#
# `take` is the one command in every worker's loop, run N-way in parallel, and `/pnext-item` teaches
# workers to key on its exit code. The natural wrapper is `take && work_it` — which proceeds on NOTHING
# if `take` returns 0 when it claimed nothing, and starts editing files with no claim and no touch-set
# reservation: the exact collision this protocol exists to prevent, and the worst kind (no marker for
# the other worker to see). So `take` gets a DISTINCT code per outcome, and only `0` means "you hold it":
#
#   0            an item was CLAIMED (and only then)
#   EX_NONE (5)  looked; nothing startable — an empty OR an all-blocked queue
#   EX_PARTIAL(4) could not read the board — a NO-VERDICT, never confused with an empty queue (#266)
#   EX_CONTENDED(6) lost every race — the board is contended; back off and retry
#   EX_RATE (75) the GraphQL budget is exhausted — back off until the reset
#
# This REVERSES #480 ("the empty queue exits cleanly (0)"), by decision on #585. The typed engine carries
# the identical contract (`Client.fs`); its side is held over HTTP by `tests/coord-engine-parity/`.

# The starved board (lifted from case 45): FS.GG.SDD has NO items (empty); FS.GG.Audio#301 is Ready but
# blocked; FS.GG.Governance#302 is open-but-in-review. SCAN_TTL_SEC=0 so each leg sees this board, not a
# cache warmed by an earlier one.
cat >"$FIXTURES/board-takerc.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":{"text":"FS-GG/FS.GG.SDD#999"},"content":{"__typename":"Issue","number":301,"title":"Blocked","url":"https://github.com/FS-GG/FS.GG.Audio/issues/301","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Audio"}}},
    {"status":{"name":"In review"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":302,"title":"In review","url":"https://github.com/FS-GG/FS.GG.Governance/issues/302","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.Governance"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4988}}
JSON
trc() { local rc=0; FSGG_COORD_SCAN_TTL_SEC=0 GH_BOARD_SET=takerc run take "$@" >/dev/null 2>&1 || rc=$?; printf '%s' "$rc"; }

# EMPTY — FS.GG.SDD has no board item at all. Nothing claimed → EX_NONE (5), NOT 0. This is the #480
# reversal, and the whole point: `take && work_it` must not fire here.
assert_eq "#585: an EMPTY queue exits EX_NONE (5), not 0 — nothing was claimed" \
  "5" "$(trc --repo sdd)"

# BLOCKED — FS.GG.Audio#301 is Ready but blocked. Also "nothing startable" → EX_NONE (5). A blocked queue
# and an empty one share a code (both are "no work for you right now"); the DIAGNOSTIC tells them apart.
assert_eq "#585: an all-BLOCKED queue also exits EX_NONE (5)" \
  "5" "$(trc --repo audio)"

# NO-VERDICT — the board read fails (a 401). "I could not look" is NOT "I looked and it is empty": it
# exits NON-ZERO and, crucially, a DIFFERENT code from EX_NONE (#266's ratified rule, now on the exit
# code too). The exact value depends on WHERE the read died — a hard board-read failure propagates
# fatally (#344), which both engines exit the same way — so this asserts the property, not a literal.
urc="$(GH_FAIL_BOARD=1 trc --repo sdd)"
[ "$urc" -ne 0 ] && [ "$urc" != "5" ] \
  && ok "#585: an UNREADABLE board is a no-verdict — non-zero, and NEVER EX_NONE (5) (#266)" \
  || bad "#585: unreadable must be non-zero and not EX_NONE (5)" "got: $urc"

# BUDGET — an exhausted GraphQL budget is the back-off signal, unchanged: EX_RATE (75).
assert_eq "#585: an exhausted budget exits EX_RATE (75) — unchanged" \
  "75" "$(GH_RATELIMIT=1 trc --repo sdd)"

# CLAIMED — the one outcome that is 0. On the pw board FS.GG.SDD#70 is startable; a fresh worker claims
# it. `as` runs on board-pw with a live store, so this is a real claim, not a canned answer.
# SCAN_TTL_SEC=0: the trc legs above warmed the shared scan cache with the (SDD-empty) takerc board, and
# the cache is keyed on the BOARD, not the fixture set — so without this, `as` is served that stale scan
# and finds nothing to claim (the very #488 shape). Turning the cache off makes it see board-pw.
crc=0; FSGG_COORD_SCAN_TTL_SEC=0 as osprey-585 take --repo sdd >/dev/null 2>&1 || crc=$?
assert_eq "#585: a CLAIMED item is the ONLY outcome that exits 0" "0" "$crc"

# THE DISTINCTIONS #585 IS ABOUT — no two of these outcomes may share a code, or the caller is back to
# grepping prose. Assert the pairs that would silently break a worker loop if they collided.
[ "$(trc --repo sdd)" != "0" ] \
  && ok "#585: a claim (0) and an empty queue do NOT share a code — 'take && work_it' cannot fire on nothing" \
  || bad "#585: claim and empty must not share a code" "empty queue returned 0"
[ "$(trc --repo sdd)" != "$(GH_FAIL_BOARD=1 trc --repo sdd)" ] \
  && ok "#585: an empty queue (EX_NONE) and an unreadable board (EX_PARTIAL) do NOT share a code (#266)" \
  || bad "#585: empty and unreadable must not share a code" "both the same"


harness_report

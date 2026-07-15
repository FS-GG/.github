#!/usr/bin/env bash
# case: claimscan failclosed 461
# tier: full
# covers: widen batch
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="42-claimscan-failclosed-461"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# --- state the monolith inherited from an earlier section; stated explicitly here ---
seed_offboard_world

# ---- #461: the claim scan FAILS CLOSED — an unreadable lock is never an empty lock --------------
# The fail-open lived on the LOCK ITSELF. If the claim-candidate read came back as bytes that are not
# JSON (a truncated page, a proxy error body, a 5xx rendered as text — `gh` EXITS 0), then `$cand` was
# the empty string, `jq 'length'` printed nothing AND EXITED 0 (so `set -euo pipefail` never fired),
# `n` was "", `[ 0 -lt "" ]` errored with `integer expected`, the loop body never ran, and
# `active_claims` returned `[]` — a failed read wearing an empty set's clothes.
#
# THESE ARE FAILURE-LEG ASSERTIONS, and they are the whole point (#266): a happy-path test passes
# against the BROKEN code and proves nothing. Each one below asserts a REFUSAL — that the command
# declines to act on a lock it could not read, rather than proceeding as if nothing were claimed.
# Seed a REAL live claim first, so "nothing is claimed" is a provably WRONG answer, not a vacuous one.
mal() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 GH_ISSUE_LIST_MALFORMED=1 \
          bash "$COORD" --worker "$1" "${@:2}"; }

# `who` must not answer "nothing is in flight" off a scan that did not succeed.
rc_who461=0
who461="$(mal kite-461 who --repo FS.GG.SDD 2>&1)" || rc_who461=$?
case "$who461" in
  *"nothing is in flight"*) bad "#461: who must NOT report 'nothing is in flight' off a failed scan" "$who461" ;;
  *) ok "#461: who must NOT report 'nothing is in flight' off a failed scan" ;;
esac
[ "$rc_who461" -ne 0 ] \
  && ok "#461: ...it exits NON-ZERO (fails closed)" \
  || bad "#461: ...it exits NON-ZERO (fails closed)" "rc=$rc_who461"
assert_contains "#461: ...and NAMES the unreadable read" "malformed" "$who461"

# `take` must not schedule an item off a claim set it could not read — that is the double-book.
rc_take461=0
take461="$(mal kite-461 take --repo FS.GG.SDD 2>&1)" || rc_take461=$?
[ "$rc_take461" -ne 0 ] \
  && ok "#461: take REFUSES to schedule off an unreadable claim set" \
  || bad "#461: take REFUSES to schedule off an unreadable claim set" "rc=0: $take461"
case "$take461" in
  *"claimed "*) bad "#461: ...and claims NOTHING (no double-book)" "$take461" ;;
  *) ok "#461: ...and claims NOTHING (no double-book)" ;;
esac

# `widen` is the guard a worker TRUSTS before editing shared files. A confident DISJOINT off a failed
# scan sends two workers into the same paths. It must refuse instead.
rc_widen461=0
widen461="$(mal kite-461 widen 'FS.GG.SDD#74' --paths 'src/Audio/**' 2>&1)" || rc_widen461=$?
case "$widen461" in
  *DISJOINT*) bad "#461: widen must NEVER report DISJOINT off a failed claim scan" "$widen461" ;;
  *) ok "#461: widen must NEVER report DISJOINT off a failed claim scan" ;;
esac
[ "$rc_widen461" -ne 0 ] \
  && ok "#461: ...and exits non-zero rather than blessing the touch-set" \
  || bad "#461: ...and exits non-zero rather than blessing the touch-set" "rc=0"

# The GUARD MUST NOT FIRE ON A LEGITIMATE EMPTY SET. `n=0` (a real, successful scan that found no
# claims) is a valid answer and must still be reported as "nothing is in flight" — otherwise the fix
# is just a different fail-closed bug, refusing to work on a healthy, idle repo.
who461ok="$(PATH="$STUB:$PATH" GH_BOARD_SET=blind GH_ISSUES_FROM_STORE=1 bash "$COORD" \
              --worker kite-461 who --repo FS.GG.Rendering 2>&1 || true)"
case "$who461ok" in
  *"nothing is in flight"*|*WORKER*) ok "#461: a SUCCESSFUL scan with no claims still reports an empty set" ;;
  *) bad "#461: a SUCCESSFUL scan with no claims still reports an empty set" "$who461ok" ;;
esac


harness_report

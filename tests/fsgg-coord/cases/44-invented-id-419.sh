#!/usr/bin/env bash
# case: invented id 419
# tier: full
# covers: claim whoami
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="44-invented-id-419"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- .github#419: an id the agent INVENTS is an id the agent SHARES ------------------------------
# ADR-0027 moved the lock off the shared GitHub account and onto a worker id. #419 is that same bug
# one level down: `whoami` warned "set FSGG_WORKER explicitly", agents obliged, and eight live workers
# drew from four of the twenty words — two of them independently picking the suffix `7c2`. A lock
# whose key two workers can both hold is not a lock. Two defences, tested here:
#   1. mint, don't invent — an executable remedy, so no literal is there to copy; and
#   2. `claim` refuses a marker carrying our id but a DIFFERENT session, instead of adopting it.
echo "--- .github#419: colliding worker ids ---"

# 1. The remedy is a command, and its stdout is EXACTLY one eval-able line — commentary must go to
#    stderr or `eval "$(… --mint)"` executes the prose.
mint_out="$(pw whoami --mint 2>/dev/null)"
assert_eq "#419: --mint prints exactly one line on stdout" "1" "$(printf '%s\n' "$mint_out" | wc -l | tr -d ' ')"
assert_contains "#419: ...and it is an eval-able export" "export FSGG_WORKER=" "$mint_out"
# The whole point: the id must be UNIQUE per call. `$RANDOM` alone is seeded from pid+time, so agents
# a harness fans out in one second drew the same word — hence both halves now come from /dev/urandom.
m1="$(pw whoami --mint 2>/dev/null)"; m2="$(pw whoami --mint 2>/dev/null)"; m3="$(pw whoami --mint 2>/dev/null)"
assert_eq "#419: successive mints do NOT collide" "3" \
  "$(printf '%s\n%s\n%s\n' "$m1" "$m2" "$m3" | sort -u | wc -l | tr -d ' ')"
# eval-ing it must actually name the worker — the ritual §0 now tells workers to run.
assert_eq "#419: the minted id is the one eval takes effect as" \
  "$(sed 's/^export FSGG_WORKER=//' <<<"$m1")" \
  "$(eval "$m1"; PATH="$STUB:$PATH" bash "$COORD" whoami 2>/dev/null | awk '/^worker/{print $2}')"

# 2. The warning must point at the COMMAND and name no id — a literal is what agents pattern-match on.
shared_warn="$(PATH="$STUB:$PATH" env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID -u FSGG_WORKER \
  CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064 \
  bash -c 'cd "$1" && exec bash "$2" whoami' _ "$WORK" "$COORD" 2>&1 >/dev/null)"
assert_contains "#419: the shared-id warning points at the MINT command" "whoami --mint" "$shared_warn"
assert_contains "#419: ...and tells the worker not to invent one" "do NOT invent" "$shared_warn"
assert_eq "#419: ...and offers NO literal id to copy (the old 'finch-a3f' attractor)" "0" \
  "$(grep -cE '(finch|heron|wren)-[0-9a-f]{3}' <<<"$shared_warn" || true)"

# 3. THE REGRESSION. A live marker with OUR worker id but a DIFFERENT session is a twin, not us.
#    Before #419 this landed in `mine` and the heartbeat path renewed it — "held (lease renewed)" —
#    silently putting two workers on one item. It must now refuse.
twin_ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
jq -n --arg ts "$twin_ts" '[{id:819, body:"<!-- fsgg:claim worker=heron-7c2 lease=120 harness=claude-code session=79b9e347 -->\ntheirs",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-74.json"
twin() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
           CLAUDE_CODE_SESSION_ID=ed60050b FSGG_WORKER=heron-7c2 bash "$COORD" "$@"; }
if twin claim 'FS.GG.SDD#74' >/dev/null 2>&1; then
  bad "#419: claim REFUSES a marker with our id but another session" \
      "session ed60050b adopted session 79b9e347's lock on #74 — two workers, one item"
else
  ok "#419: claim REFUSES a marker with our id but another session"
fi
twin_err="$(twin claim 'FS.GG.SDD#74' 2>&1 || true)"
assert_contains "#419: ...naming it as two workers sharing one id" "two workers share one id" "$twin_err"
assert_contains "#419: ...and reporting the OTHER session"        "79b9e347" "$twin_err"
assert_contains "#419: ...and offering the mint as the way out"   "whoami --mint" "$twin_err"
assert_eq "#419: ...and the twin's marker is left intact"  "819" "$(claims_on 74)"
assert_eq "#419: ...and NO second marker was posted"       "heron-7c2" "$(workers_on 74)"

# --force steals another WORKER's item. This is not a contested item, it is a broken identity — and
# the fix for a broken identity is a new identity. Forcing here deletes a marker our twin is working
# behind, so the refusal must survive --force.
if twin claim 'FS.GG.SDD#74' --force >/dev/null 2>&1; then
  bad "#419: --force does NOT override the twin refusal" "--force let a twin steal its own id's lock"
else
  ok "#419: --force does NOT override the twin refusal"
fi
assert_eq "#419: ...so --force left the twin's marker alone" "819" "$(claims_on 74)"

# 4. Back-compat, and the boundary of the rule. We may only conclude "twin" when BOTH sessions are
#    known. A marker with no `session=` (a human, a harness that exports none, any pre-#419 marker) is
#    genuinely indistinguishable from ours — so it keeps the old behaviour (ours, warned about) rather
#    than failing closed on old data and locking workers out of items they really do hold. #42's
#    fixture marker is exactly that: worker=finch-a3f, no session.
#    The marker is seeded HERE rather than reusing #42's: an earlier test re-claims #42, and that
#    heartbeat rewrites its marker with whatever session the AMBIENT shell exports — so on a developer's
#    machine (which exports CLAUDE_CODE_SESSION_ID) #42's marker is not sessionless by the time we get
#    here, and this assertion would fail on correct code while passing in CI. Same trap `hless` above
#    documents; state the environment explicitly rather than inheriting it.
jq -n --arg ts "$twin_ts" '[{id:820, body:"<!-- fsgg:claim worker=dunlin-9f1 lease=120 -->\nsessionless, as a human or a pre-#419 marker is",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-71.json"
assert_contains "#419: a SESSIONLESS marker with our id is still ours (heartbeat, not refusal)" \
  "lease renewed" "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
     env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
     CLAUDE_CODE_SESSION_ID=ed60050b FSGG_WORKER=dunlin-9f1 bash "$COORD" claim 'FS.GG.SDD#71' 2>/dev/null)"
# ...and the same session re-claiming its OWN marker is a heartbeat, not a twin. Without this, a
# worker could never renew its own claim — the refusal would fire on itself.
same() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
           env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
           CLAUDE_CODE_SESSION_ID=79b9e347 FSGG_WORKER=heron-7c2 bash "$COORD" "$@"; }
assert_contains "#419: the SAME session re-claiming its own marker is a heartbeat" \
  "lease renewed" "$(same claim 'FS.GG.SDD#74' 2>/dev/null)"


harness_report

#!/usr/bin/env bash
# case: worker id and lock
# tier: smoke
# covers: whoami claim release heartbeat reap who
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="20-worker-id-and-lock"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"


# ---- worker identity ---------------------------------------------------------------------------
assert_eq "whoami: --worker wins everything" "heron-b71" \
  "$(as heron-b71 whoami | awk '/^worker/{print $2}')"
assert_eq "whoami: reports which rule derived the id" "--worker flag" \
  "$(as heron-b71 whoami | sed -n 's/^source  //p')"
assert_eq "whoami: \$FSGG_WORKER is honoured" "wren-c22" \
  "$(PATH="$STUB:$PATH" FSGG_WORKER=wren-c22 bash "$COORD" whoami | awk '/^worker/{print $2}')"

# ---- harness session ids (rule 4) ---------------------------------------------------------------
# The CI runner exports no harness vars and the developer's shell may export several, so every case
# below states its own environment explicitly. `env -u` strips whatever the outer harness set.
#
# It also runs from "$WORK", which is NOT a git checkout. Identity rule 3 (the worktree name) OUTRANKS
# the session id, so a developer running this fixture from inside a linked worktree — which ADR-0021
# tells them to work in — would otherwise see every rule-4/5 assertion below fail on correct code.
# CI only passed because it happens to check out the primary worktree. The rule under test is
# "no harness, no worktree", so the fixture must supply "no worktree" rather than inherit it.
hless() { PATH="$STUB:$PATH" env -u CLAUDE_CODE_SESSION_ID -u OPENCODE_SESSION_ID \
            -u FSGG_AGENT_SESSION_ID -u FSGG_WORKER "$@" \
            bash -c 'cd "$1" && exec bash "$2" whoami' _ "$WORK" "$COORD" 2>&1; }

# A session id is NOT an identity where the harness shares it across subagents. Claude Code does
# (anthropics/claude-code#7881) — so it names the worker only as a fallback, and it WARNS.
cc="$(hless CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064)"
assert_contains "whoami: a claude-code session id names the worker (rule 4)" \
  "source  claude-code session id (309bd638-8a1c-42b7-952b-898efb8d1064)" "$cc"
assert_contains "whoami: ...and reports the harness"          "harness claude-code" "$cc"
assert_contains "whoami: ...flagging that subagents share it" "(shared by subagents)" "$cc"
assert_contains "whoami: ...and WARNS, because a shared id cannot lock" \
  "every subagent of this claude-code session shares this session id" "$cc"

# Deterministic: the same session must always name the same worker (no persistence, no drift).
cc2="$(hless CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064)"
assert_eq "whoami: the session-derived name is deterministic" \
  "$(awk '/^worker/{print $2}' <<<"$cc")" "$(awk '/^worker/{print $2}' <<<"$cc2")"
assert_eq "whoami: a DIFFERENT session gets a different name" "false" \
  "$([ "$(hless CLAUDE_CODE_SESSION_ID=aaaa | awk '/^worker/{print $2}')" \
     = "$(hless CLAUDE_CODE_SESSION_ID=bbbb | awk '/^worker/{print $2}')" ] && echo true || echo false)"
assert_contains "whoami: the derived name is memorable, not a UUID" "-" \
  "$(hless CLAUDE_CODE_SESSION_ID=aaaa | awk '/^worker/{print $2}')"

# OpenCode spawns subagents as CHILD sessions with their own ids, so there its session id IS
# per-worker — and must not warn. The cardinality is a property of the harness, not of the name.
oc="$(hless OPENCODE_SESSION_ID=ses_abc123)"
assert_contains "whoami: an opencode session id is per-worker"  "(per-worker)" "$oc"
assert_eq "whoami: ...so it does NOT warn" "0" "$(grep -c 'WARNING' <<<"$oc" || true)"

# An unknown harness may declare itself; absent that, we assume its session is shared (fail safe).
unk="$(hless FSGG_AGENT_SESSION_ID=zz-99)"
assert_contains "whoami: an unknown harness's session id still works" "session zz-99" "$unk"
assert_contains "whoami: ...but is assumed shared until proven otherwise" "shared by subagents" "$unk"

# Rule 5, the last resort: no harness, no worktree -> a per-checkout name, and a different warning.
none="$(hless)"
assert_contains "whoami: with no harness, falls back to a per-checkout name" \
  "generated, persisted per-checkout" "$none"
assert_contains "whoami: ...and says why THAT id may not be unique" \
  "every worker sharing this checkout gets this same id" "$none"

# Precedence: an explicit id always beats a session id, and never warns.
expl="$(PATH="$STUB:$PATH" CLAUDE_CODE_SESSION_ID=309bd638 FSGG_WORKER=wren-c22 bash "$COORD" whoami 2>&1)"
assert_contains "whoami: \$FSGG_WORKER beats a harness session id" "source  \$FSGG_WORKER" "$expl"
assert_eq "whoami: an explicit id never warns" "0" "$(grep -c 'WARNING' <<<"$expl" || true)"
assert_contains "whoami: ...but the session is still recorded as provenance" "harness claude-code" "$expl"

# ---- the lock: THE regression. Same account, different worker -> refused. -----------------------
: >"$GH_LOG"
if as heron-b71 claim 'FS.GG.SDD#42' >/dev/null 2>&1; then
  bad "claim: a second worker on the SAME account is refused (ADR-0021 regression)" \
      "heron-b71 claimed an item already held by finch-a3f"
else
  ok "claim: a second worker on the SAME account is refused (ADR-0021 regression)"
fi
assert_contains "claim: refusal names the holding WORKER, not the login" "held by worker 'finch-a3f'" \
  "$(as heron-b71 claim 'FS.GG.SDD#42' 2>&1 || true)"
assert_eq "claim: a refused claim leaves exactly one marker" "finch-a3f" "$(workers_on 42)"

# The holder re-claiming is a heartbeat, not a second marker (so `take` retries stay idempotent).
assert_contains "claim: the holder re-claiming renews its lease" "lease renewed" \
  "$(as finch-a3f claim 'FS.GG.SDD#42' 2>/dev/null)"
assert_eq "claim: re-claiming does not add a second marker" "801" "$(claims_on 42)"

# An uncontended claim on a free item: marker posted, assignee set, Status flipped, branch printed.
: >"$GH_LOG"
out70="$(as heron-b71 claim 'FS.GG.SDD#70' 2>/dev/null)"
assert_contains "claim: uncontended claim succeeds"       "claimed FS.GG.SDD#70 by worker heron-b71" "$out70"
assert_contains "claim: prints the isolation worktree"    "git worktree add ../FS.GG.SDD-70 -b item/70-scene-graph" "$out70"
# .github#319: the base ref is load-bearing, and the assertion above cannot see it — that needle is a
# PREFIX of the base-less command, so it passed happily for as long as the bug existed. `git worktree
# add -b <new>` with no commit-ish branches from the shared checkout's HEAD, routinely another worker's
# unmerged branch since N workers pass through that checkout, and the item's PR then carries that
# branch's commits too. The base ref therefore gets its own assertion: check what FOLLOWS the branch
# name, not that the branch name appears.
isolate70="$(printf '%s\n' "$out70" | grep 'git worktree add' || true)"
base70="${isolate70##* }"   # the trailing commit-ish; on the base-less command this is the branch name
if [ "$base70" = "origin/main" ]; then
  ok "claim: the isolation worktree names an explicit base ref"
else
  bad "claim: the isolation worktree names an explicit base ref" \
    "expected a commit-ish after '-b <branch>', got trailing token '$base70' — that branches off the shared checkout's HEAD: $isolate70"
fi
assert_contains "claim: prints the attribution trailer"   'FSGG-Worker: heron-b71' "$out70"
assert_contains "claim: flips the board to In progress"   "board: In progress" "$out70"
# The assignee is set over REST, not `gh issue edit` (#418): same courtesy to human readers, 4 fewer
# GraphQL points per claim (and 4 more per release) — on a budget every worker shares. A regression to
# `gh issue edit` would be invisible except here.
assert_contains "claim: assigns @me for the humans — over REST" \
  "assignee-post FS-GG/FS.GG.SDD 70" "$(cat "$GH_LOG")"
assert_eq "claim: does NOT spend GraphQL on the assignee (no 'gh issue edit')" \
  "0" "$(grep -c '^issue-edit' "$GH_LOG" || true)"
assert_eq "claim: exactly one marker on the item" "heron-b71" "$(workers_on 70)"

# PROVENANCE. The marker records WHICH agent transcript claimed the item — the question #255 could
# only answer with mtimes and `ps`. `worker=` must stay the first key, or parse_claims stops matching.
: >"$STORE/comments-71.json"; echo '[]' >"$STORE/comments-71.json"
# --force, and it is not a workaround: heron-b71 GENUINELY holds #70 at this point (asserted just
# above), so the #516 guard is right to refuse a second bare claim. The monolith got a plain `claim`
# through only because that guard rides a 90-second scan cache (`CACHED=1 active_claims`) which, by
# then, was stale enough to have missed the #70 marker — i.e. it passed by cache accident, not by
# rule. `--force` is this file's own sanctioned "yes, I mean to hold two" (see `rel` above), and it
# leaves every assertion below — which are about PROVENANCE, not #516 — testing exactly what they did.
PATH="$STUB:$PATH" GH_BOARD_SET=pw CLAUDE_CODE_SESSION_ID=sess-1234 \
  bash "$COORD" --worker heron-b71 claim 'FS.GG.SDD#71' --force >/dev/null 2>&1
marker71="$(jq -r '.[] | select(.body | test("^<!--\\s*fsgg:claim")) | .body' "$STORE/comments-71.json")"
assert_contains "claim: the marker records the harness"  "harness=claude-code" "$marker71"
assert_contains "claim: the marker records the session"  "session=sess-1234"   "$marker71"
assert_contains "claim: the human line names the agent"  "session \`sess-1234\`" "$marker71"
assert_eq "claim: provenance keys do NOT break the worker= capture the lock parses" "heron-b71" \
  "$(workers_on 71)"
assert_contains "claim: worker= is still the FIRST key after the marker" \
  "fsgg:claim worker=heron-b71 lease=120 harness=claude-code" "$marker71"
# ...and a marker carrying provenance is still a lock: a second worker is refused.
assert_fails "claim: a provenance-carrying marker still excludes another worker" \
  as wren-c22 claim 'FS.GG.SDD#71'
# Leave #71 free for the scheduler assertions further down.
PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" --worker heron-b71 release 'FS.GG.SDD#71' >/dev/null 2>&1

# ---- the CAS: a rival marker lands with a LOWER id between our post and our re-read -------------
# The loser must delete its own marker and exit non-zero, leaving the winner's marker alone.
: >"$GH_LOG"
race_out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_RACE_INJECT=rook-d19 GH_RACE_ISSUE=74 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#74' 2>&1 || true)"
assert_contains "claim CAS: the loser knows it lost, and to whom" "lost the claim race on FS.GG.SDD#74 to worker 'rook-d19'" "$race_out"
assert_contains "claim CAS: the loser backs off cleanly"          "backed off cleanly" "$race_out"
assert_eq "claim CAS: exactly ONE marker survives the race"       "rook-d19" "$(workers_on 74)"
assert_contains "claim CAS: the loser deleted its OWN marker"     "comment-delete" "$(cat "$GH_LOG")"
# The race winner really holds #74 — hand it back, or every scheduler assertion below inherits it.
as rook-d19 release 'FS.GG.SDD#74' >/dev/null 2>&1
assert_eq "claim CAS: the winner's claim is a real lock it must release" "" "$(workers_on 74)"

# ---- heartbeat / release -----------------------------------------------------------------------
: >"$GH_LOG"
assert_contains "heartbeat: renews the holder's lease" "renewed FS.GG.SDD#70" "$(as heron-b71 heartbeat 'FS.GG.SDD#70' 2>/dev/null)"
assert_contains "heartbeat: patches the marker in place" "comment-patch" "$(cat "$GH_LOG")"
assert_fails "heartbeat: a non-holder cannot renew" as wren-c22 heartbeat 'FS.GG.SDD#70'

assert_fails "release: a non-holder cannot release another worker's claim" as wren-c22 release 'FS.GG.SDD#70'
assert_eq "release: the refused release left the marker intact" "heron-b71" "$(workers_on 70)"
assert_contains "release: --force releases another worker's claim" "released FS.GG.SDD#70" \
  "$(as wren-c22 release 'FS.GG.SDD#70' --force 2>/dev/null)"
assert_eq "release: the marker is gone" "" "$(workers_on 70)"

# ---- release: "drop the lease" is not "this item is startable" (#331) ---------------------------
# `release` used to force Status=Ready unconditionally, so the documented blocked-item sequence
# (`set-field Status Blocked` then `release`) had its Blocked silently reverted on the very next
# line — leaving a board row whose Status contradicted its own `Blocked by`. It must now reset the
# `In progress` that `claim` set, and ONLY that.
#
# Every case below clears GH_LOG *after* the claim: `claim` writes Status=In progress itself, so a
# log still holding that write would let a "release wrote no Status" assertion pass for free.
for n in 340 341 342 343 344 346 347; do seed_issue "$n" "release case $n" "src/Rel$n/**"; done
edits_to_status() { grep -c 'PVTSSF_status' "$GH_LOG" 2>/dev/null || true; }
# rel <VAR=value> <args...> — `as pika-r01`, plus one stub knob for the Status read.
#
# These scenarios walk ONE worker through item after item to exercise release/`Paths:` semantics, and
# a few deliberately do not release in between — so they claim with `--force`, which is the sanctioned
# way to say "yes, I mean to hold more than one" (#516). The rule itself — at most one item per worker,
# because a claim RESERVES A TOUCH-SET and a second unattended one locks files nobody is editing — is
# asserted in its own section below, with its own failure leg. `--force` on an unheld item steals
# nothing; it only opts out of the #516 guard.
rel() { local e="$1"; shift
        PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
          env "$e" bash "$COORD" --worker pika-r01 "$@"; }

# (a) the ordinary path is unchanged: the lease drops and the item returns to the pool.
as pika-r01 claim 'FS.GG.SDD#340' --force >/dev/null 2>&1
printf 'In progress' >"$STORE/itemstatus-340"; : >"$GH_LOG"
assert_contains "release: resets the 'In progress' that claim set" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#340' 2>/dev/null)"
assert_contains "release: ...with a real board write" "opt_ready" "$(cat "$GH_LOG")"
assert_eq "release: ...and the marker is gone" "" "$(workers_on 340)"

# (b) THE REGRESSION. A deliberately-set Blocked survives its own release.
as pika-r01 claim 'FS.GG.SDD#341' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-341"; : >"$GH_LOG"
rel341="$(as pika-r01 release 'FS.GG.SDD#341' 2>/dev/null)"
assert_contains "release: PRESERVES a deliberately-set Blocked" "board: Blocked (preserved" "$rel341"
assert_eq "release: ...writing no Status at all, rather than a matching one" "0" "$(edits_to_status)"
assert_eq "release: ...and the lease is still dropped" "" "$(workers_on 341)"

# (c) `--status S` is the caller stating the end state, and still wins over the preserve rule.
as pika-r01 claim 'FS.GG.SDD#342' --force >/dev/null 2>&1
printf 'Blocked' >"$STORE/itemstatus-342"; : >"$GH_LOG"
assert_contains "release --status: an explicit end state overrides preserve" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#342' --status Ready 2>/dev/null)"
assert_contains "release --status: ...and is actually written" "opt_ready" "$(cat "$GH_LOG")"

# (d) preserve is not Blocked-specific: release never downgrades a terminal column either.
as pika-r01 claim 'FS.GG.SDD#343' --force >/dev/null 2>&1
printf 'Done' >"$STORE/itemstatus-343"; : >"$GH_LOG"
assert_contains "release: never downgrades a Done item to Ready" "board: Done (preserved" \
  "$(as pika-r01 release 'FS.GG.SDD#343' 2>/dev/null)"
assert_eq "release: ...Done is left untouched" "0" "$(edits_to_status)"

# (e) A Status we could not READ is not a Status we may overwrite — forcing Ready over an unknown
#     column is the same fail-open, one turn in. The lease still drops: the marker is the lock.
as pika-r01 claim 'FS.GG.SDD#344' --force >/dev/null 2>&1
: >"$GH_LOG"
rel344="$(rel GH_FAIL_ITEM_STATUS=344 release 'FS.GG.SDD#344' 2>&1)"
assert_contains "release: an unreadable Status is not overwritten" "Status unchanged" "$rel344"
assert_contains "release: ...and says so, naming the repair" "set-field" "$rel344"
assert_eq "release: ...no Status write on an unreadable column" "0" "$(edits_to_status)"
assert_eq "release: ...but the lease IS dropped" "" "$(workers_on 344)"

# (f) On the board, but no Status set: nothing was chosen, so there is nothing to preserve.
as pika-r01 claim 'FS.GG.SDD#346' --force >/dev/null 2>&1
: >"$STORE/itemstatus-346"; : >"$GH_LOG"
assert_contains "release: an item with no Status set returns to the pool" "board: Ready" \
  "$(as pika-r01 release 'FS.GG.SDD#346' 2>/dev/null)"

# (g) An off-board item has no column to reset, and release must not pretend it did.
as pika-r01 claim 'FS.GG.SDD#347' --force >/dev/null 2>&1
: >"$GH_LOG"
rel347="$(rel GH_OFFBOARD_ITEM=347 release 'FS.GG.SDD#347' 2>/dev/null)"
assert_contains "release: an off-board item reports no board, not a phantom write" "not on board" "$rel347"
assert_eq "release: ...and writes no Status" "0" "$(edits_to_status)"
assert_eq "release: ...and the lock is released, board or no board" "" "$(workers_on 347)"

# ---- who: what is actually running, without spelunking through worktrees ------------------------
who="$(pw who --repo sdd 2>/dev/null)"
assert_contains "who: names the worker holding each item" "finch-a3f" "$who"
assert_contains "who: flags a claim past its lease as STALE" "STALE" "$who"
assert_contains "who: flags In-progress work with NO marker as UNCLAIMED" "UNCLAIMED" "$who"
whoerr="$(pw who --repo sdd 2>&1 >/dev/null)"
assert_contains "who: warns that someone is working outside the protocol" \
  "In progress with NO claim marker" "$whoerr"
assert_eq "who --json: the unclaimed item has a null worker" "null" \
  "$(pw who --repo sdd --json 2>/dev/null | jq -r '.[] | select(.number==60) | .worker')"

# ---- reap: collect a dead worker's claim -------------------------------------------------------
assert_contains "reap: dry-run reports, does not release" "would reap  FS.GG.SDD#43  worker ghost-000" \
  "$(pw reap --repo sdd 2>/dev/null)"
assert_eq "reap: dry-run left the marker in place" "ghost-000" "$(workers_on 43)"
: >"$GH_LOG"
reaped="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "reap --apply: releases the expired claim" "reaped  FS.GG.SDD#43  worker ghost-000" "$reaped"
assert_contains "reap --apply: returns the item to the pool" "board: Ready" "$reaped"
assert_eq "reap --apply: the stale marker is gone" "" "$(workers_on 43)"
assert_eq "reap --apply: it TELLS the reaped worker (a message, not a silent steal)" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-000"))] | length' "$STORE/comments-43.json")"

# `reap` is the OTHER way a claim goes away, so it owes the board the same answer as `release` and
# used to force Ready just as hard (#331). A worker whose lease expires on an item it had marked
# Blocked must not have that column reset on its way out — the reaper is collecting a LEASE, and it
# knows nothing about whether the item became startable. Seeded after the block above so the stale
# marker cannot perturb the reap tests that precede it.
seed_issue 348 "reaped while blocked" "src/Rel348/**"
jq -n --arg ts "$stale_ts" '[{id:848, body:"<!-- fsgg:claim worker=ghost-348 lease=120 -->\ndead",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-348.json"
printf 'Blocked' >"$STORE/itemstatus-348"; : >"$GH_LOG"
reaped348="$(as wren-c22 reap --repo sdd --apply 2>/dev/null)"
assert_contains "reap --apply: collects the expired claim" "reaped  FS.GG.SDD#348" "$reaped348"
assert_contains "reap --apply: PRESERVES a deliberately-set Blocked" "board: Blocked (preserved)" "$reaped348"
assert_eq "reap --apply: ...writing no Status at all" "0" "$(edits_to_status)"
assert_eq "reap --apply: ...but the lease IS collected" "" "$(workers_on 348)"


harness_report

#!/usr/bin/env bash
# End-to-end: the compiled engine's WRITE commands, over HTTP, against a STATEFUL fixture — no token, no net.
#
# The read fixture proves scan→decide. This proves the other half: the claim CAS and the capability-typed
# writes, driven as real CLI commands against a server that remembers what they posted. The CAS is the
# sharp one — `claim` posts a marker, RE-READS, and wins only if its marker is the lowest live one; a
# fixture that forgot the marker would make the command fail its own re-read, so a green here is a green
# over a real read-modify-reread.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -x "$ENGINE" ] || { echo "FAIL  build the engine first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2; exit 1; }

SRV_OUT="$(mktemp)"; CACHE_DIR="$(mktemp -d)"; PREDICATE_FIX="$(mktemp -d)"
python3 "$HERE/stateful_server.py" >"$SRV_OUT" 2>/dev/null &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT"; rm -rf "$CACHE_DIR" "$PREDICATE_FIX"' EXIT

PORT=""
for _ in $(seq 1 50); do PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"; [ -n "$PORT" ] && break; sleep 0.1; done
[ -n "$PORT" ] || { bad "the fixture bound a port"; echo "writes: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"
export GITHUB_TOKEN="fixture-token"
export FSGG_COORD_OWNER="FS-GG" FSGG_COORD_PROJECT="Coordination"
export FSGG_COORD_CACHE="$CACHE_DIR" FSGG_COORD_SCAN_TTL_SEC=0
# A clean identity, so the derivation never depends on the CI runner's env.
#
# `FSGG_WORKER=""` ALONE WAS NOT THAT, and .github#1646 is what made it visible. `Identity.resolve` reads
# `--worker` first, then `$FSGG_WORKER`, then the HARNESS SESSION — so an empty `FSGG_WORKER` falls through
# to `CLAUDE_CODE_SESSION_ID`/`OPENCODE_SESSION_ID`/`FSGG_AGENT_SESSION_ID` and this process derives an
# identity from whatever agent ran the script. That was invisible while nothing consulted it. It is
# consulted now: every `--worker vole-418` below is measured against the id this process derives for
# itself, and the lock verbs refuse the disagreement over vole-418's live marker (#1646). Measured: 13 of
# the 47 assertions here fail under an exported `CLAUDE_CODE_SESSION_ID` and pass without one.
#
# So unset the whole ladder, not just its second rung. What remains is a caller that derives NOTHING and
# names itself with `--worker` — the human-operator case the flag exists for, and what this fixture is.
unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS
export FSGG_WORKER=""

run() { "$ENGINE" "$@" --worker vole-418; }

# ---- the claim CAS: post, re-read, WIN -------------------------------------------------------------
out="$(run claim FS.GG.SDD#42 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'claimed FS.GG.SDD#42 by worker vole-418'; then
  ok "claim WINS the CAS and reports the holder"
else
  bad "claim wins the CAS" "rc=$rc: $out"
fi

# ---- a SECOND worker loses to the live marker ------------------------------------------------------
lost="$("$ENGINE" claim FS.GG.SDD#42 --worker kite-461 2>&1)"; lrc=$?
if [ "$lrc" -ne 0 ] && printf '%s' "$lost" | grep -qi 'already held by vole-418'; then
  ok "a second worker LOSES to the live lock (no double-claim)"
else
  bad "a second worker loses to the live lock" "rc=$lrc: $lost"
fi

# ---- heartbeat renews the held lease ---------------------------------------------------------------
hb="$(run heartbeat FS.GG.SDD#42 2>&1)"; hrc=$?
if [ "$hrc" -eq 0 ] && printf '%s' "$hb" | grep -q 'heartbeat FS.GG.SDD#42'; then
  ok "heartbeat renews the lease the worker holds"
else
  bad "heartbeat renews the held lease" "rc=$hrc: $hb"
fi

# ---- a NON-HOLDER cannot heartbeat -----------------------------------------------------------------
hbx="$("$ENGINE" heartbeat FS.GG.SDD#42 --worker kite-461 2>&1)"; hxrc=$?
if [ "$hxrc" -ne 0 ] && printf '%s' "$hbx" | grep -qi 'held by vole-418'; then
  ok "a non-holder cannot heartbeat (it names the real holder)"
else
  bad "a non-holder cannot heartbeat" "rc=$hxrc: $hbx"
fi

# ---- .github#1620: THE INSTRUCTION `adopt` PRINTS, FOLLOWED VERBATIM, MUST WORK ---------------------
#
# This is #1620's acceptance criterion 2 made executable, and it is the one that has to run against a REAL
# lock rather than a unit fixture, because the defect was never in one component: `adopt` refused a live
# claim and told the operator to run `claim <ref> --force`; `claim` parsed `--force`, scoped it correctly
# to itself, and read it in a pre-check that had nothing to do with the holder. Every part behaved as
# documented, and the composition dead-ended. So the assertion is a COMPOSITION: take the command the tool
# prints, run THAT, and require it to do what the sentence promised.
#
# It is extracted from the refusal rather than retyped. A test that retypes the command asserts that a
# command works; this asserts that the ADVICE works, which is the thing that was false.
adoptout="$("$ENGINE" adopt FS.GG.SDD#42 --worker kite-461 2>&1)"; adrc=$?
# The last `scripts/fsgg-coord ...` token run on the refusal — the remedy line, with the shim's name
# dropped so the compiled engine can run the same argv.
advice="$(printf '%s\n' "$adoptout" | grep -o 'scripts/fsgg-coord claim [^ ]* --force' | tail -n1 | sed 's|^scripts/fsgg-coord ||')"
if [ "$adrc" -ne 0 ] && [ -n "$advice" ]; then
  ok ".github#1620: adopt refuses a LIVE claim and prints a remedy command to follow"
else
  bad ".github#1620: adopt must refuse a live claim and name a remedy" "rc=$adrc advice='$advice': $adoptout"
fi

# shellcheck disable=SC2086 # $advice is the tool's OWN argv, split on purpose — that is what "verbatim" means
steal="$("$ENGINE" $advice --worker kite-461 2>&1)"; strc=$?
if [ "$strc" -eq 0 ] && printf '%s' "$steal" | grep -q 'STOLE FS.GG.SDD#42'; then
  ok ".github#1620: ...and running it VERBATIM takes the item — the advertised route is real"
else
  bad ".github#1620: adopt's remedy must work when followed" "rc=$strc: $steal"
fi

# THE DISPLACED WORKER MUST FIND OUT. It is still running — that is the whole difference between a steal
# and a stale collection — so a heartbeat that quietly succeeded would leave two workers on one item.
#
# It must also say WHY it might be held by someone else, or this asserts nothing the pre-existing
# non-holder leg above does not already produce: `held by kite-461` is what a worker sees when it simply
# never held the item. A worker that DID hold it needs to be told its claim was taken, not left to read a
# generic non-holder refusal as its own mistake.
hbs="$(run heartbeat FS.GG.SDD#42 2>&1)"; hbsrc=$?
if [ "$hbsrc" -ne 0 ] && printf '%s' "$hbs" | grep -qi 'held by kite-461' \
     && printf '%s' "$hbs" | grep -q -- '--force'; then
  ok ".github#1620: the displaced holder's heartbeat FAILS LOUDLY and names the worker that took it"
else
  bad ".github#1620: a displaced holder must not heartbeat successfully" "rc=$hbsrc: $hbs"
fi

# AND THE THEFT IS RECORDED ON THE ITEM, reachable by the displaced worker through its own inbox. The
# evicted MARKER was deleted, so this notice is the only surviving trace of the claim that was taken.
# `--repo` because this fixture serves no Status column, so the board scan yields no in-progress row for
# the mailbox to derive its repo set from. On a real board the item's own `In progress` row supplies it.
ibx="$(run inbox --repo FS.GG.SDD 2>&1)"; ibxrc=$?
if [ "$ibxrc" -eq 0 ] && printf '%s' "$ibx" | grep -q 'kite-461' && printf '%s' "$ibx" | grep -q 'TAKEN'; then
  ok ".github#1620: ...and the steal is recorded on the item, in the displaced worker's inbox"
else
  bad ".github#1620: a steal must be announced to the worker it displaced" "rc=$ibxrc: $ibx"
fi

# Hand #42 back, so the legs below run against the state they were written for: kite-461 drops the lock it
# stole and vole-418 re-takes it. A fixture leg that leaves the world moved is a leg that breaks its
# neighbours for reasons that have nothing to do with them.
"$ENGINE" release FS.GG.SDD#42 --worker kite-461 >/dev/null 2>&1
run claim FS.GG.SDD#42 >/dev/null 2>&1

# ---- release drops the lock ------------------------------------------------------------------------
rel="$(run release FS.GG.SDD#42 2>&1)"; rrc=$?
if [ "$rrc" -eq 0 ] && printf '%s' "$rel" | grep -q 'released FS.GG.SDD#42'; then
  ok "release drops the held lock"
else
  bad "release drops the held lock" "rc=$rrc: $rel"
fi

# ...and after release, the item is claimable again (the marker really went away).
reclaim="$(run claim FS.GG.SDD#42 2>&1)"; rcrc=$?
[ "$rcrc" -eq 0 ] && ok "the item is claimable again after release (the marker was removed)" \
  || bad "the item is claimable again after release" "rc=$rcrc: $reclaim"
run release FS.GG.SDD#42 >/dev/null 2>&1

# ---- set-field writes a board column ---------------------------------------------------------------
sf="$(run set-field FS.GG.SDD#43 Status 'In progress' 2>&1)"; sfrc=$?
if [ "$sfrc" -eq 0 ] && printf '%s' "$sf" | grep -q 'set FS.GG.SDD#43 Status = In progress'; then
  ok "set-field writes a board column"
else
  bad "set-field writes a board column" "rc=$sfrc: $sf"
fi

# ---- an UNKNOWN field is refused, costing no mutation ----------------------------------------------
sfx="$(run set-field FS.GG.SDD#43 Nonexistent x 2>&1)"; sfxrc=$?
[ "$sfxrc" -ne 0 ] && printf '%s' "$sfx" | grep -qi 'no field named' \
  && ok "set-field refuses an unknown field" \
  || bad "set-field refuses an unknown field" "rc=$sfxrc: $sfx"

# ---- child attaches by id --------------------------------------------------------------------------
ch="$(run child FS.GG.SDD#99 FS.GG.SDD#43 2>&1)"; chrc=$?
[ "$chrc" -eq 0 ] && printf '%s' "$ch" | grep -q 'linked FS.GG.SDD#43 as a sub-issue of FS.GG.SDD#99' \
  && ok "child attaches the sub-issue" \
  || bad "child attaches the sub-issue" "rc=$chrc: $ch"

# ---- say needs no lock -----------------------------------------------------------------------------
sy="$(run say FS.GG.SDD#43 --to kite-461 --message 'heads up, our paths overlap' 2>&1)"; syrc=$?
[ "$syrc" -eq 0 ] && printf '%s' "$sy" | grep -q 'said to kite-461' \
  && ok "say posts a message with no lock required" \
  || bad "say posts a message" "rc=$syrc: $sy"

# ---- widen requires the HELD claim (#706) ----------------------------------------------------------
# Not holding #43 → widen must refuse (the ownership check is an argument, not an if).
wx="$("$ENGINE" widen FS.GG.SDD#43 --worker ghost-000 --paths 'src/New/**' 2>&1)"; wxrc=$?
[ "$wxrc" -ne 0 ] && printf '%s' "$wx" | grep -qi 'does not hold' \
  && ok "#706: widen refuses when the caller does not hold the claim" \
  || bad "#706: widen refuses without the lock" "rc=$wxrc: $wx"

# Now hold it, then widen succeeds.
run claim FS.GG.SDD#43 >/dev/null 2>&1
wd="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths 'src/New/**' 'docs/**' 2>&1)"; wdrc=$?
[ "$wdrc" -eq 0 ] && printf '%s' "$wd" | grep -q 'widened FS.GG.SDD#43 → Paths: src/Other/\*\*, src/New/\*\*, docs/\*\*' \
  && ok "#1377: widen unions new paths into a HELD item's existing touch-set" \
  || bad "#1377: widen preserves a held item's existing touch-set" "rc=$wdrc: $wd"

# A second call preserves both earlier generations; repeating it is idempotent (one token, not two).
wd2="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths 'src/Third/**' 2>&1)"; wd2rc=$?
[ "$wd2rc" -eq 0 ] && printf '%s' "$wd2" | grep -q 'Paths: src/Other/\*\*, src/New/\*\*, docs/\*\*, src/Third/\*\*' \
  && ok "#1377: a second widen preserves every prior token" \
  || bad "#1377: two widening calls produce the normalized union" "rc=$wd2rc: $wd2"
wd3="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths './src/Third/**,' 2>&1)"; wd3rc=$?
third_count="$(printf '%s' "$wd3" | head -n1 | grep -o 'src/Third/\*\*' | wc -l | tr -d ' ')"
[ "$wd3rc" -eq 0 ] && [ "$third_count" = 1 ] \
  && ok "#1377: repeated widen is idempotent" \
  || bad "#1377: repeated widen must not duplicate a token" "rc=$wd3rc count=$third_count: $wd3"

# ---- an unmatchable token is refused BEFORE any write (#273/#523) ----------------------------------
before_bad="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" | jq -r .body)"
wu="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths '**/never.fs' 2>&1)"; wurc=$?
after_bad="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/43" | jq -r .body)"
[ "$wurc" -ne 0 ] && printf '%s' "$wu" | grep -qi 'reserve NOTHING' && [ "$after_bad" = "$before_bad" ] \
  && ok "#273/#1377: an unmatchable new token is refused without modifying the union" \
  || bad "#273/#1377: an unmatchable token must leave the existing declaration intact" "rc=$wurc: $wu"

# Replacement remains available, but only under an explicit name; this is the narrowing operation.
sp="$("$ENGINE" set-paths FS.GG.SDD#43 --worker vole-418 --paths 'src/Narrow/**' 2>&1)"; sprc=$?
[ "$sprc" -eq 0 ] && printf '%s' "$sp" | grep -q 'set FS.GG.SDD#43 → Paths: src/Narrow/\*\*' \
  && ok "#1377: set-paths explicitly replaces (and can narrow) the touch-set" \
  || bad "#1377: set-paths is the explicit replacement operation" "rc=$sprc: $sp"

# ---- done stamps a completed item ------------------------------------------------------------------
dn="$(run "done" FS.GG.SDD#42 2>&1)"; dnrc=$?   # quoted: the coord VERB, not the loop keyword (SC1010, #648)
[ "$dnrc" -eq 0 ] && printf '%s' "$dn" | grep -q 'FSGG-DONE' \
  && ok "done stamps an item closed by a merged PR" \
  || bad "done stamps a completed item" "rc=$dnrc: $dn"

# ---- #1151: done SURFACES a DEFERRED Status write, keeps the GREEN stamp, and flush completes it -----
# The exact condition #1151 closes: on an exhausted budget the Status=Done write returns `Deferred`
# (QUEUED, and nothing replays it on its own). The Green arm used to `|> ignore` that outcome, so `done`
# printed green, exited 0, and said NOTHING about the flush the board now needs — silent drift. It must
# instead keep the green verdict (the WORK is done), DROP the claim (the lock's lifetime is the work's,
# #533), and PRINT the flush remedy. Then `flush` lands the queued stamp.
run claim FS.GG.SDD#42 >/dev/null 2>&1                                            # a live marker for done to drop
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null      # arm: the next field write RATE-LIMITS
dd="$(run "done" FS.GG.SDD#42 --flip 2>&1)"; ddrc=$?
if [ "$ddrc" -eq 0 ] \
   && printf '%s' "$dd" | grep -q 'FSGG-DONE' \
   && printf '%s' "$dd" | grep -qi 'DEFERRED' \
   && printf '%s' "$dd" | grep -q 'scripts/fsgg-coord flush'; then
  ok "#1151: done keeps the GREEN stamp but SURFACES the deferred Status write with flush advice"
else
  bad "#1151: done surfaces the deferred Status write with flush advice" "rc=$ddrc: $dd"
fi

# ...and the claim marker is DROPPED even though the column deferred — the lock must not outlive the work.
after1151="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/FS.GG.SDD/issues/42/comments")"
printf '%s' "$after1151" | grep -q 'fsgg:claim' \
  && bad "#1151: done drops the claim marker on a deferred write" "the marker survives: $after1151" \
  || ok "#1151: done drops the claim marker even when the Status write DEFERS"

# ...and flush REPLAYS the queued Status=Done write against the now-healthy server — the stamp is not lost.
fdd="$(run flush 2>&1)"; fddrc=$?
[ "$fddrc" -eq 0 ] && ! printf '%s' "$fdd" | grep -qi 'DROPPED' \
  && ok "#1151: flush replays the deferred Status=Done write (the queued stamp lands)" \
  || bad "#1151: flush replays the deferred stamp" "rc=$fddrc: $fdd"

# ---- #1086: a worker mid-item in ANOTHER repo is NOT idle, and is offered nothing --------------------
# The whole of #1086, end to end. `vole-418` holds a live claim on FS.GG.SDD#43 (taken above and never
# released), and is here stamping a `.github` item. Condition 3 says they must not be handed a side-quest:
# they are mid-lease with a live touch-set — in a different repo, which is exactly what makes it invisible.
#
# It was invisible. The offer asked "are you idle?" of a board scoped to `.github`, in which that SDD claim
# does not appear, so the honest guard answered "idle" and handed over the chore. The board is read
# UNFILTERED now and the scope rides in the type, so the claim is seen and the offer is withheld.
#
# IT RUNS FIRST, BEFORE ANY LEG TAKES THE CHORE LOCK, and that ordering is the whole assertion. Written
# after the snipe-733 legs it PASSED WITH THE BUG SIMULATED: snipe-733 holds `.github#1033` by then, so
# vole-418's offer lost the LOCK and returned None for a reason having nothing to do with idleness. A leg
# that cannot fail is not evidence — it is the "shaped to pass" defect this issue's own review history
# (plover-a4cf, #733) caught one level up. So: lock free, chore real, worker busy — one variable.
#
# The chore is REAL and offerable on exactly this board: snipe-733 is handed it by the very next leg.
vb="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker vole-418 2>&1)"; vbrc=$?
[ "$vbrc" -eq 0 ] && printf '%s' "$vb" | grep -q 'FSGG-DONE' && ! printf '%s' "$vb" | grep -qi 'chore' \
  && ok "#1086: a worker holding a claim in ANOTHER repo is not idle — no chore, though one is on offer" \
  || bad "#1086: cross-repo claim makes us busy" "rc=$vbrc: $vb"

# ---- #733/§4.6: `done` is a SAFE POINT, and it is the one a working fleet reaches --------------------
# Condition 3 names two boundaries — after `done`, or at `next`. #1056 wired `next`; `Chore.AfterDone` was
# a case Core declared and NOTHING minted. That matters because of WHERE the two sit in the recipe:
# `/pnext-item` takes (§1), stamps (§5), and loops back to `take` (§6), calling `next` only in its
# "take found nothing" DIAGNOSTIC. An offer that fires only at `next` fires only when the board has no
# work — and a board with no work has no fleet to conscript. This leg is the whole claim: stamping an item
# offers a chore.
#
# .github#51 is stamped; .github#50 is the chore (CLOSED, board column still Ready → CLOSED-ISSUE-NOT-DONE).
dc="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker snipe-733 2>&1)"; dcrc=$?
[ "$dcrc" -eq 0 ] && printf '%s' "$dc" | grep -q 'FSGG-DONE' \
  && printf '%s' "$dc" | grep -qi 'chore' && printf '%s' "$dc" | grep -q '#50' \
  && ok "#733: done --flip offers a chore at AfterDone — the safe point the happy path reaches" \
  || bad "#733: done offers a chore at AfterDone" "rc=$dcrc: $dc"

# THE OFFER IS A COURTESY, AND THE STAMP OUTRANKS IT. The chore rides on STDERR, never stdout: `done`'s
# stdout carries the FSGG-DONE verdict a caller greps, and an offer printed there would corrupt the answer
# it is attached to — the same rule `next` keeps for its item ref.
dso="$("$ENGINE" "done" FS-GG/.github#51 --flip --worker snipe-733 2>/dev/null)"
printf '%s' "$dso" | grep -q 'FSGG-DONE' && ! printf '%s' "$dso" | grep -qi 'chore' \
  && ok "#733: the AfterDone offer is on stderr — done's stdout verdict is untouched" \
  || bad "#733: offer does not pollute done's stdout" "$dso"

# #1087 — A RECEIVER NOW DRAINS. Before #1087 `choreLockRef` knew only `.github#1033`, so a `done` in any
# receiver was refused for want of a lock and the queue drained in `.github` alone. The six receivers now
# have closed `[chore-lock]` issues (SDD#518 among them) and the map resolves all seven, so stamping an SDD
# item offers an SDD chore under SDD's OWN lock. This is the rollout's whole point, and it reds on pre-#1087
# code (SDD had no lock). SDD#45 is the chore (CLOSED, board still Ready → CLOSED-ISSUE-NOT-DONE).
rc="$("$ENGINE" "done" FS.GG.SDD#42 --worker snipe-1087 2>&1)"; rcrc=$?
[ "$rcrc" -eq 0 ] && printf '%s' "$rc" | grep -q 'FSGG-DONE' \
  && printf '%s' "$rc" | grep -qi 'chore' && printf '%s' "$rc" | grep -q '#45' \
  && printf '%s' "$rc" | grep -q 'FS.GG.SDD#518' \
  && ok "#1087: a RECEIVER (FS.GG.SDD) now offers a chore under its OWN lock — the queue drains org-wide" \
  || bad "#1087: a receiver drains its chore queue" "rc=$rcrc: $rc"

# AN UNROSTERED REPO IS REFUSED, FOR FREE. All seven FS-GG repos have a lock now, so the honest "no lock"
# case is a repo `choreLockRef` does not know (FS.GG.Legacy). `Chores.offer`'s step 1 is that pure string
# match, placed first "because it spends nothing" — so a `done` there stamps, offers nothing, and never
# reads the board. No assertion on OUTPUT can catch a stray read (the output is identical either way:
# nothing), so the fixture counts board reads and this asserts the count does not move.
br0="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | sed 's/[^0-9]//g')"
dn2="$("$ENGINE" "done" FS.GG.Legacy#60 --worker snipe-1087 2>&1)"; dn2rc=$?
[ "$dn2rc" -eq 0 ] && printf '%s' "$dn2" | grep -q 'FSGG-DONE' && ! printf '%s' "$dn2" | grep -qi 'chore' \
  && ok "#1087: an UNROSTERED repo is offered nothing, and still stamps" \
  || bad "#1087: unrostered repo offers nothing" "rc=$dn2rc: $dn2"
br1="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | sed 's/[^0-9]//g')"
[ "$br0" = "$br1" ] \
  && ok "#1087: an unrostered repo costs NO board read — the free question is asked first (${br0}→${br1})" \
  || bad "#1087: unrostered done spends no board read" "board reads went ${br0} → ${br1}"

# ---- .github#1535: `next` TAKES THE CHORE LOCK, and `batch` does not --------------------------------
# The decision #1535 asked for, pinned as behaviour: `next` WRITES. Its contract everywhere a caller met
# it — `/pnext-item` §1, the take exit-code table, and `tests/coord-engine-parity/shim.sh`, which used it
# as the canonical READ verb in the stale-engine guard's read leg for that leg's whole life until #1528 —
# said "tell me what to work on", and after printing that answer it POSTs a claim marker taking the
# repo's chore lock (`.github#1033`,
# ADR-0041). The write is real, it is deliberate (#733/§4.6 conscription), and it is now DECLARED rather
# than discovered. This leg is what stops the declaration and the code drifting apart again.
#
# THE MARKER IS THE ASSERTION, NOT THE PRINTED OFFER. The chore text on stderr is what a human sees, and
# a leg that grepped only for it would pass on an engine that printed the offer and took no lock — the
# offer is a courtesy, the LOCK is the write, and only one of them is #1535's subject. So this reads the
# comment thread on #1033 and asserts a marker naming THIS worker appeared on it.
#
# THE PRECONDITION IS ESTABLISHED, NOT ASSUMED, and that is the whole reason for the release and the
# BEFORE assertion. `snipe-733` still holds #1033 from the AfterDone legs above; a leg written without
# this would find a marker on #1033 either way and pass WITHOUT `next` having written anything — the
# "shaped to pass" defect this file's own #1086 note records being caught one level up. Empty before,
# ours after, one variable.
"$ENGINE" release "FS-GG/.github#1033" --worker snipe-733 >/dev/null 2>&1
lk0="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
printf '%s' "$lk0" | grep -q 'fsgg:claim' \
  && bad ".github#1535: the chore lock is FREE before the probe" "a marker survives: $lk0" \
  || ok ".github#1535: the chore lock is free before the probe (the one variable is the verb)"

# THE NEGATIVE CONTROL FIRST, and it is load-bearing rather than tidy. `batch` is the same scheduling
# decision uncapped and makes NO offer, so it is the spelling a stale engine still permits (`BOARD_READS`)
# and the one the recipe now sends an idling worker to. If `batch` took the lock too, that advice would be
# wrong — so this asserts the substitute really is a read, on the same board, in the same breath.
#
# `--text` IS PART OF THE SPELLING UNDER TEST, not decoration: `batch` defaults to JSON (`Both Json`), and
# the recipe tells a worker to run `batch --text -n 1` precisely because the JSON arm prints `[]` rather
# than the sentence `next` prints. Drive what the docs say to drive, or the leg guards a different command.
#
# THE SAME WORKER RUNS BOTH VERBS, so the pair differs in ONE variable. Two ids would leave "maybe the
# other worker simply was not idle" as an unexcluded explanation for the empty thread — and an offer is
# withheld from a non-idle caller (condition 3), which is exactly how this control could pass while
# proving nothing. `teal-1535` takes no claim from `batch`, so it is still idle at the `next` below.
bn="$("$ENGINE" batch --text --repo .github -n 1 --worker teal-1535 2>&1)"; bnrc=$?
lkb="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
[ "$bnrc" -eq 0 ] && ! printf '%s' "$lkb" | grep -q 'fsgg:claim' \
  && ok ".github#1535: \`batch --text\` is the same decision and takes NO lock — the read half of the pair" \
  || bad ".github#1535: batch takes no chore lock" "rc=$bnrc: $bn / lock thread: $lkb"

# ...and it really is the SAME decision, not merely another quiet command: it prints the sentence `next`
# prints. Without this the control could be satisfied by a `batch` that answered nothing at all.
#
# THE WORDS ARE WHAT THIS LEG GRADES, NOT THE STREAM. .github#1562 took `next`'s empty arm off the shared
# `printChosen` so its headline could go to STDERR (`next`'s stdout is a bare-ref machine contract); `batch
# --text` still prints it to STDOUT. Both `bn` here and `nx` below capture MERGED (`2>&1`), so this leg is
# deliberately blind to that split and asserts only the thing #1535's advice rests on — that the substitute
# ANSWERS, in the one `nothingSchedulable` spelling both verbs still share.
printf '%s' "$bn" | grep -q 'nothing schedulable right now.' \
  && ok ".github#1535: ...and it is the same ANSWER — one shared \`nothingSchedulable\` spelling, so the words cannot drift" \
  || bad ".github#1535: batch --text prints next's answer" "rc=$bnrc: $bn"

# ...AND `next`, same worker, same board, POSTS.
nx="$("$ENGINE" next --repo .github --worker teal-1535 2>&1)"; nxrc=$?
lk1="$(curl -fsS "$FSGG_GITHUB_API_BASE/repos/FS-GG/.github/issues/1033/comments")"
[ "$nxrc" -eq 0 ] && printf '%s' "$lk1" | grep -q 'fsgg:claim' \
  && printf '%s' "$lk1" | grep -q 'worker=teal-1535' \
  && ok ".github#1535: \`next\` POSTs a claim marker to .github#1033 — it WRITES, and is declared so" \
  || bad ".github#1535: next takes the chore lock" "rc=$nxrc: $nx / lock thread: $lk1"

# ...and the offer it printed names the chore it took the lock FOR, so the write and the reason a caller
# is given for it are the same fact. A lock taken for a chore the caller is never told about would be the
# write with none of the conscription that justifies it.
#
# ANCHORED ON THE CHORE LINE, not searched for `#50` anywhere in the output: `next` also prints `#50` in
# its passed-over list, so an unanchored grep would be satisfied by an offer naming a DIFFERENT subject —
# a leg that cannot see the thing it claims to check.
printf '%s' "$nx" | grep -q '^chore \[quick\] \.github#50:' \
  && ok ".github#1535: the lock \`next\` took is the one the offer it printed names (#50)" \
  || bad ".github#1535: next's offer names its subject" "rc=$nxrc: $nx"

# Hand the lock back, so nothing downstream inherits a held chore lock from a diagnostic verb.
"$ENGINE" release "FS-GG/.github#1033" --worker teal-1535 >/dev/null 2>&1

# ---- verify-paths: a PR inside its touch-set is OK -------------------------------------------------
vp="$("$ENGINE" verify-paths --pr 500 --repo FS.GG.SDD 2>&1)"; vprc=$?
[ "$vprc" -eq 0 ] && printf '%s' "$vp" | grep -q 'FSGG-PATHS OK' \
  && ok "verify-paths: a PR inside its touch-set is OK (exit 0)" \
  || bad "verify-paths OK" "rc=$vprc: $vp"

# ---- verify-paths: a PR that DRIFTS names the file and fails ---------------------------------------
vd="$("$ENGINE" verify-paths --pr 501 --repo FS.GG.SDD 2>&1)"; vdrc=$?
[ "$vdrc" -ne 0 ] && printf '%s' "$vd" | grep -q 'FSGG-PATHS DRIFT' && printf '%s' "$vd" | grep -q 'docs/x.md' \
  && ok "verify-paths: a drifting PR names the out-of-bounds file and fails" \
  || bad "verify-paths DRIFT" "rc=$vdrc: $vd"

# ---- verify-paths --warn downgrades DRIFT to advisory (exit 0) -------------------------------------
vw="$("$ENGINE" verify-paths --pr 501 --repo FS.GG.SDD --warn 2>&1)"; vwrc=$?
[ "$vwrc" -eq 0 ] && printf '%s' "$vw" | grep -q 'FSGG-PATHS DRIFT' \
  && ok "verify-paths --warn downgrades DRIFT to advisory (exit 0)" \
  || bad "verify-paths --warn is advisory" "rc=$vwrc: $vw"

# ---- #498/ADR-0044: verify-paths subtracts the GENERATED, CI-GATED artifacts -----------------------
# §1 forbids declaring a generated artifact, and `verify-paths` then reported it as DRIFT anyway — the
# gate firing on the behaviour the protocol mandates. These legs pin the subtraction AND, more
# importantly, the three ways it must FAIL CLOSED.
#
# HERMETIC ON PURPOSE, IN A WAY THE OTHER LEGS DO NOT HAVE TO BE. The subtraction is gated on "the
# checkout IS the PR's repo", which the engine answers from `git config remote.origin.url` of its CWD.
# Reading that from the ambient checkout would make these legs pass or fail on WHERE THEY WERE RUN — green
# in CI, red in a fork or a worktree whose remote is a mirror, for a reason having nothing to do with the
# engine. So the CWD is a throwaway git repo whose remote is set explicitly, and the answer is the same
# everywhere. `-c` rather than global config: a fixture must not read the runner's ~/.gitconfig (#709).
VP_CO="$(mktemp -d)"
git -c init.defaultBranch=main init -q "$VP_CO"
git -C "$VP_CO" remote add origin https://github.com/FS-GG/.github.git

# The kit root the engine asks for `scripts/generated-paths` is the REAL repo — so this leg drives the
# REAL roster, not a restatement of it. A stub would prove the plumbing and nothing about the contract.
vg="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; vgrc=$?
[ "$vgrc" -eq 0 ] && printf '%s' "$vg" | grep -q 'FSGG-PATHS OK' \
  && printf '%s' "$vg" | grep -q 'regenerated (expected)' && printf '%s' "$vg" | grep -q 'registry/repos.lock' \
  && ok "#498: a regenerated CI-gated artifact is subtracted from drift, and reported as expected" \
  || bad "#498: regenerated artifact subtracted" "rc=$vgrc: $vg"

# The split is the deliverable: a real overrun must stay a finding, and must not be buried beside a file
# the reader is not being asked to act on.
vs="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 503 --repo .github 2>/dev/null)"; vsrc=$?
[ "$vsrc" -ne 0 ] && printf '%s' "$vs" | grep -q 'FSGG-PATHS DRIFT' \
  && printf '%s' "$vs" | sed -n '/undeclared (review)/,/regenerated/p' | grep -q 'docs/x.md' \
  && ! printf '%s' "$vs" | sed -n '/undeclared (review)/,/regenerated/p' | grep -q 'repos.lock' \
  && ok "#498: real drift stays RED and is reported apart from the regenerated artifact" \
  || bad "#498: drift/regenerated split" "rc=$vsrc: $vs"

# FAIL CLOSED 1 — the checkout is NOT the PR's repo. The local generators say nothing about another repo's
# artifacts, so subtracting this repo's set there would suppress REAL drift in a repo nobody asked. This is
# the one fail-open direction the roster itself cannot see, because it is a fact about the CALL.
vx="$(cd "$VP_CO" && FSGG_KIT_ROOT="$REPO_ROOT" "$ENGINE" verify-paths --pr 502 --repo FS.GG.SDD 2>/dev/null)"; vxrc=$?
[ "$vxrc" -ne 0 ] && printf '%s' "$vx" | grep -q 'repos.lock' \
  && ok "#498: subtracts NOTHING when the checkout is not the PR's repo (drift stays reported)" \
  || bad "#498: cross-repo subtraction is refused" "rc=$vxrc: $vx"

# FAIL CLOSED 2 and 3 — an ABSENT `generated-paths`, and one that FAILS. Both must subtract NOTHING and
# leave drift exactly as it is today: "I could not ask what is generated" and "nothing is generated" are
# opposite facts, and only one is safe to act on (#266).
VP_EMPTY="$(mktemp -d)"
va="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_EMPTY" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; varc=$?
[ "$varc" -ne 0 ] && printf '%s' "$va" | grep -q 'repos.lock' \
  && ok "#498: an ABSENT generated-paths subtracts nothing (fails closed)" \
  || bad "#498: absent generated-paths fails closed" "rc=$varc: $va"

VP_BAD="$(mktemp -d)"; mkdir -p "$VP_BAD/scripts"
printf '#!/bin/sh\necho boom >&2\nexit 2\n' >"$VP_BAD/scripts/generated-paths"
chmod +x "$VP_BAD/scripts/generated-paths"
vf="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; vfrc=$?
[ "$vfrc" -ne 0 ] && printf '%s' "$vf" | grep -q 'repos.lock' \
  && ok "#498: a FAILING generated-paths subtracts nothing (fails closed)" \
  || bad "#498: failing generated-paths fails closed" "rc=$vfrc: $vf"

# ...and it SAYS SO, naming the generator's own reason. Failing closed silently is only half a remedy:
# the artifact reappears under `undeclared (review):`, the recipe tells the worker to go look at the
# generator, and without this nothing says WHICH one or why. `generated-paths` goes out of its way to
# put that on stderr; the engine must not be the thing that throws it away (#266 — a right verdict with
# an unreadable reason).
vm="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>&1 >/dev/null)"
printf '%s' "$vm" | grep -q 'generated-paths exited 2' && printf '%s' "$vm" | grep -q 'boom' \
  && ok "#498: a failing generated-paths forwards its REASON — fails closed, and readably" \
  || bad "#498: failing generated-paths names its reason" "$vm"

# ...and one that SUCCEEDS while listing nothing. Distinct from the failing case: exit 0 is the code a
# reader is most tempted to trust, and an empty answer from a healthy generator still is not a licence to
# subtract everything.
printf '#!/bin/sh\nexit 0\n' >"$VP_BAD/scripts/generated-paths"
ve="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_BAD" "$ENGINE" verify-paths --pr 502 --repo .github 2>/dev/null)"; verc=$?
[ "$verc" -ne 0 ] && printf '%s' "$ve" | grep -q 'repos.lock' \
  && ok "#498: an EMPTY generated-paths subtracts nothing (fails closed)" \
  || bad "#498: empty generated-paths fails closed" "rc=$verc: $ve"

# FAIL CLOSED 4 — a HANGING generator. The other three fail closed by returning; this one fails closed
# only because the wait is BOUNDED. It is pinned because the first cut of the bound DID NOT WORK and
# looked like it did: a blocking `ReadToEnd` on stdout ran before the timeout, a stuck child never closes
# stdout, so the read never returned and the timeout was never reached. `verify-paths` — the merge gate —
# hung for as long as it was allowed to. The bug was invisible to every other leg here, because a healthy
# generator exercises none of it. Timeout tunable so this costs ~1s instead of 30.
VP_HANG="$(mktemp -d)"; mkdir -p "$VP_HANG/scripts"
printf '#!/bin/sh\nexec sleep 987654\n' >"$VP_HANG/scripts/generated-paths"
chmod +x "$VP_HANG/scripts/generated-paths"
vh_start="$(date +%s)"
vh="$(cd "$VP_CO" && FSGG_KIT_ROOT="$VP_HANG" FSGG_GENERATED_PATHS_TIMEOUT_MS=1000 \
       "$ENGINE" verify-paths --pr 502 --repo .github 2>&1)"; vhrc=$?
vh_elapsed=$(( $(date +%s) - vh_start ))
[ "$vhrc" -ne 0 ] && printf '%s' "$vh" | grep -q 'repos.lock' && printf '%s' "$vh" | grep -q 'was killed' \
  && [ "$vh_elapsed" -lt 15 ] \
  && ok "#498: a HANGING generated-paths is killed and subtracts nothing (bounded, ${vh_elapsed}s)" \
  || bad "#498: hanging generated-paths is bounded" "rc=$vhrc elapsed=${vh_elapsed}s: $vh"

# ...and the hang is REAPED, not orphaned. `Kill true` takes the process tree: killing the script while
# leaving the generator it is blocked on alive would leak the actual hang, one process per PR.
sleep 0.5
[ "$(pgrep -f 'sleep 987654' | wc -l)" -eq 0 ] \
  && ok "#498: the killed generator leaves no orphaned process behind" \
  || bad "#498: hanging generator is reaped" "$(pgrep -af 'sleep 987654')"

rm -rf "$VP_CO" "$VP_EMPTY" "$VP_BAD" "$VP_HANG"

# ---- .github#1740 / .github#1779: a live claim reserves whatever its board COLUMN says ---------------
#
# THE DEFECT, CONSTRUCTED RATHER THAN WAITED FOR. `activeCollisions` picked which claims to check by
# reading the board's `Status` COLUMN, so a claim whose MARKER was live but whose column had not landed
# reserved nothing — and answered `DISJOINT`, the one verdict the touch-set protocol exists to make
# trustworthy. Measured live on 2026-07-28: two workers declared `src/FS.GG.Coord.Cli/Client.fs` 41
# seconds apart and `widen` said DISJOINT 53 seconds later.
#
# WHY THESE LEGS AND NOT A TIMING TEST. The failure is a race, and a race reproduced by sleeping is a test
# that passes when the timing happens to work. The fixture's `defer-next-field-write` /
# `fail-next-field-write` / `off_board` make each state DETERMINISTIC: `claim` posts its marker, its
# `Status` write meets the injected condition, and the column is left where it was. That is not a
# simulation of the bug's state, it is the bug's state, reached the way production reaches it.
#
# THREE STATES, AND THEY ARE NOT VARIATIONS OF ONE. `claim` distinguishes `statusWrite: deferred | failed
# | not-on-board`, exits GREEN on all three, and #510 queues only the FIRST. #1740 closed it by reading
# the deferral queue, which by construction says nothing about the other two. Each leg below asserts its
# own precondition off the RECEIPT and off the BOARD before asserting the verdict — a leg whose state was
# never built would otherwise measure the ordinary `In progress` path and pass for the wrong reason.
#
# THE UNIT LEGS COVER THE CACHE HALF (this script runs with FSGG_COORD_SCAN_TTL_SEC=0, so there is no
# stale scan here to have). These cover the halves no amount of freshness can reach.

# #43 leaves vole-418's hands declaring the SAME path #44 declares, and parked in a NON-claim column.
"$ENGINE" set-paths FS.GG.SDD#43 --worker vole-418 --paths 'src/Verify/**' >/dev/null 2>&1
"$ENGINE" release FS.GG.SDD#43 --worker vole-418 --status Ready >/dev/null 2>&1

# ARM, then claim: the marker lands, the column write does not.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null
cl="$("$ENGINE" claim FS.GG.SDD#43 --worker otter-777 --json 2>&1)"; clrc=$?
sw="$(printf '%s' "$cl" | jq -r '.statusWrite' 2>/dev/null)"

# THE PRECONDITION IS ITSELF AN ASSERTION, and it is read off the BOARD rather than off the receipt. If
# the claim converged, the state under test was never built and everything below would be measuring the
# ordinary `In progress` path — passing for the wrong reason. This is AC3's fixture stated literally: the
# row still reads `Ready`, and it is not lying, because the write genuinely has not happened.
col43="$("$ENGINE" ready --repo FS.GG.SDD --worker vole-418 2>/dev/null | jq -r '.[] | select(.number==43) | .status')"
[ "$clrc" -eq 0 ] && [ "$sw" = "deferred" ] && [ "$col43" = "Ready" ] \
  && ok "#1740: the fixture built the state — marker live, Status write DEFERRED, board row still reads '$col43'" \
  || bad "#1740: build a live claim whose Status column has not landed" "rc=$clrc statusWrite=$sw column=$col43: $cl"

# THE ASSERTION. #44 declares `src/Verify/**`; #43 now declares it too and is HELD by otter-777. The
# column says `Ready` and is not lying — the write genuinely has not happened.
ov="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovrc=$?
[ "$ovrc" -eq 6 ] && printf '%s' "$ov" | grep -q 'OVERLAP' && printf '%s' "$ov" | grep -q 'otter-777' \
  && ok "#1740: a live claim whose Status column has NOT landed still COLLIDES (no false DISJOINT)" \
  || bad "#1740: the collision scan sees a claim the board column does not" "rc=$ovrc: $ov"

# .github#1779 — THE QUEUE IS NO LONGER WHAT CARRIES THE RESERVATION, AND THIS IS WHERE THAT IS PROVED.
#
# This leg was #1740's control, and it asserted the opposite: take the DEFERRAL QUEUE away (a fresh cache
# root has none) and the reservation was supposed to vanish with it. It did, and that was the bug one
# level up — #43's marker was live and its declaration collided in both runs, so `DISJOINT` was never the
# right answer here; the queue's presence was the only thing standing between a worker and a file another
# worker held. The candidate set is the repo's OPEN ISSUES now, so the empty queue changes nothing.
EMPTY_CACHE="$(mktemp -d)"
ovc="$(FSGG_COORD_CACHE="$EMPTY_CACHE" "$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovcrc=$?
rm -rf "$EMPTY_CACHE"
[ "$ovcrc" -eq 6 ] && printf '%s' "$ovc" | grep -q 'OVERLAP' && printf '%s' "$ovc" | grep -q 'otter-777' \
  && ok "#1779: the SAME state with an EMPTY deferral queue still collides — the marker carries it, not the queue" \
  || bad "#1779: an empty queue must not resurrect the false DISJOINT" "rc=$ovcrc: $ovc"

# THE CONTROL THAT MATTERS NOW, and without it every leg here is satisfied by "report every open issue".
# Same board, same column, same declaration — the MARKER is released. A declaration nobody holds is work
# nobody is doing, and reporting it would stop a worker who has nothing to stop for.
"$ENGINE" release FS.GG.SDD#43 --worker otter-777 --status Ready >/dev/null 2>&1
ovn="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovnrc=$?
[ "$ovnrc" -eq 0 ] && printf '%s' "$ovn" | grep -q 'DISJOINT' \
  && ok "#1779: control — the same colliding declaration with NO live claim reads DISJOINT (the marker is the lock)" \
  || bad "#1779: colliding tokens alone must not reserve" "rc=$ovnrc: $ovn"

# ---- .github#1779 leg 1: a `Status` write that FAILED PERMANENTLY still reserves ---------------------
#
# #510 QUEUES A DEFERRAL AND REFUSES TO QUEUE A FAILURE, and that is correct — a write replayed forever is
# a promise nobody can keep. The consequence is that nothing will EVER write this column, so no freshness
# tier and no queue read can conjure it. #1740 named this as its declined remainder.
# THE QUEUE DEPTH IS READ BEFORE AND AFTER, NOT COMPARED TO ZERO. `pendingBoardWrites` is the depth of
# the whole shared queue, and the DEFERRED leg above legitimately left an entry in it — an assertion of
# `== 0` would be testing "no other test deferred anything", which is a fact about test ordering and not
# about #510. The load-bearing fact is that this failure ADDED NOTHING: a permanent failure is not queued,
# so nothing will ever replay it, so nothing will ever write that column.
q_before="$("$ENGINE" budget --json --worker vole-418 2>/dev/null | jq -r '.pendingBoardWrites')"
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/fail-next-field-write" >/dev/null
clf="$("$ENGINE" claim FS.GG.SDD#43 --worker stoat-311 --json 2>&1)"; clfrc=$?
swf="$(printf '%s' "$clf" | jq -r '.statusWrite' 2>/dev/null)"
q_after="$(printf '%s' "$clf" | jq -r '.pendingBoardWrites' 2>/dev/null)"
colf="$("$ENGINE" ready --repo FS.GG.SDD --worker vole-418 2>/dev/null | jq -r '.[] | select(.number==43) | .status')"

# THE UNCHANGED QUEUE DEPTH IS WHAT SEPARATES THIS LEG FROM THE ONE ABOVE. Without it "failed" and
# "deferred" are indistinguishable from outside the receipt, and this would be the deferral-queue leg
# again wearing a different name — passing on the very mechanism it claims not to depend on.
[ "$clfrc" -eq 0 ] && [ "$swf" = "failed" ] && [ "$q_after" = "$q_before" ] && [ "$colf" = "Ready" ] \
  && ok "#1779: the fixture built the state — marker live, Status write FAILED, queue depth UNCHANGED at $q_after, column still '$colf'" \
  || bad "#1779: build a live claim whose Status write failed permanently" "rc=$clfrc statusWrite=$swf queue $q_before -> $q_after column=$colf: $clf"

ovf="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovfrc=$?
[ "$ovfrc" -eq 6 ] && printf '%s' "$ovf" | grep -q 'OVERLAP' && printf '%s' "$ovf" | grep -q 'stoat-311' \
  && ok "#1779: a live claim whose Status write FAILED PERMANENTLY still COLLIDES" \
  || bad "#1779: a permanently-failed column write must not cost a false DISJOINT" "rc=$ovfrc: $ovf"

"$ENGINE" release FS.GG.SDD#43 --worker stoat-311 --status Ready >/dev/null 2>&1

# ---- .github#1779 leg 2: a live claim on an item that is NOT ON THE BOARD AT ALL ---------------------
#
# There is no row, so a row-derived candidate set cannot select it by construction — not by being fresher,
# and not by reading the queue. #46 is open in the repo, declares `src/Verify/**`, and the fixture's
# `projectItems` lookup answers with no node for it, so `boardWrite` returns `NotOnBoard`.
cln="$("$ENGINE" claim FS.GG.SDD#46 --worker heron-822 --json 2>&1)"; clnrc=$?
swn="$(printf '%s' "$cln" | jq -r '.statusWrite' 2>/dev/null)"
cvn="$(printf '%s' "$cln" | jq -r '.converged' 2>/dev/null)"
# READ OFF THE BOARD TOO, not only off the receipt: #46 must be absent from `ready`'s rows entirely.
row46="$("$ENGINE" ready --repo FS.GG.SDD --status any --worker vole-418 2>/dev/null | jq -r '[.[] | select(.number==46)] | length')"
[ "$clnrc" -eq 0 ] && [ "$swn" = "not-on-board" ] && [ "$cvn" = "false" ] && [ "$row46" = "0" ] \
  && ok "#1779: the fixture built the state — marker live on #46, statusWrite=not-on-board, NO board row" \
  || bad "#1779: build a live claim on an item that is not on the board" "rc=$clnrc statusWrite=$swn converged=$cvn rows=$row46: $cln"

ovn2="$("$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 2>&1)"; ovn2rc=$?
[ "$ovn2rc" -eq 6 ] && printf '%s' "$ovn2" | grep -q 'OVERLAP' && printf '%s' "$ovn2" | grep -q 'heron-822' \
  && ok "#1779: a live claim on an item the board has NEVER LISTED still COLLIDES" \
  || bad "#1779: an off-board claim must not cost a false DISJOINT" "rc=$ovn2rc: $ovn2"

# ---- .github#1779 AC2: the API cost, MEASURED across the process boundary ---------------------------
#
# `.github#1086` got this same trade wrong by an order of magnitude by ESTIMATING it, and the first draft
# of #1779 declined the whole design over an estimate of "~74 REST marker reads per widen". So the fixture
# counts. `/_fixture/rest-reads` reports and resets; `/_fixture/board-reads` is the GraphQL board query,
# which this path must no longer make at all.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads" >/dev/null   # reset the meter
br_before="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | jq -r '.boardReads')"
"$ENGINE" overlap FS.GG.SDD#44 --active --worker vole-418 >/dev/null 2>&1
meter="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/rest-reads")"
br_after="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/board-reads" | jq -r '.boardReads')"
rest_n="$(printf '%s' "$meter" | jq -r '.count')"
lists="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("^GET /repos/[^/]+/[^/]+/issues$"))] | length')"
markers="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("/comments$"))] | length')"
# THE NEGATIVE HALF, AND IT IS THE HALF THAT PINS THE CLAIM. A count alone would keep passing if the scan
# read some OTHER two issues' markers. #42 declares `src/Thing/**` and #99 declares `none`; both are open,
# both are candidates for a scan that reads first and filters second, and NEITHER can ever collide with
# `src/Verify/**`. Their markers must not be read at all.
noise="$(printf '%s' "$meter" | jq -r '[.paths[] | select(test("/(42|99)/comments$"))] | length')"

# The repo has FIVE open issues. Two of them declare `src/Verify/**` — #43 (whose marker was released
# above, so it reserves nothing) and #46 (held by heron-822) — and #44 is the subject. So: ONE issue list,
# TWO marker reads, one per COLLIDING row rather than one per open row or one per `In progress` row, and
# ZERO GraphQL board reads. The old scan read a marker AND a body for every `In progress` row whether or
# not its tokens could ever collide, and paid a board query on top.
[ "$lists" = "1" ] && [ "$markers" = "2" ] && [ "$noise" = "0" ] && [ "$br_before" = "$br_after" ] \
  && ok "#1779 AC2: overlap --active spent $rest_n REST calls (1 issue list + 1 marker per COLLIDING row, 2 of 5 open) and 0 GraphQL board reads" \
  || bad "#1779 AC2: the measured cost is not the claimed cost" "rest=$rest_n lists=$lists markers=$markers noise=$noise boardReads $br_before -> $br_after: $meter"

"$ENGINE" release FS.GG.SDD#46 --worker heron-822 >/dev/null 2>&1
# #43 back into vole-418's hands, declaring `src/Verify/**`, for the AC5 legs below.
"$ENGINE" claim FS.GG.SDD#43 --worker otter-777 >/dev/null 2>&1

# ---- .github#1740 AC5: a NARROWING is never reported as having INTRODUCED a collision ----------------
# A token-subset names strictly fewer files, so it cannot introduce a collision — whatever the scan finds
# predates the command. Saying "the path update introduced a collision" over one sent the worker who filed
# #1740 looking for a mistake in their own narrowing instead of at the claim that was already there.
"$ENGINE" claim FS.GG.SDD#44 --worker vole-418 >/dev/null 2>&1
"$ENGINE" widen FS.GG.SDD#43 --worker otter-777 --paths 'docs/**' >/dev/null 2>&1
nr="$("$ENGINE" set-paths FS.GG.SDD#43 --worker otter-777 --paths 'src/Verify/**' 2>&1)"; nrrc=$?
if [ "$nrrc" -eq 6 ] \
   && printf '%s' "$nr" | grep -qi 'NARROWED' \
   && printf '%s' "$nr" | grep -qi 'cannot have introduced' \
   && ! printf '%s' "$nr" | grep -qi 'may or may not'; then
  ok "#1740 AC5: a narrowing that collides is reported as PRE-EXISTING, not as introduced"
else
  bad "#1740 AC5: a narrowing must not be blamed for a collision it cannot have caused" "rc=$nrrc: $nr"
fi

# THE COUNTER-EXAMPLE, and without it the leg above is satisfied by a constant. Same board, same live
# claim, same colliding token — but the declaration GROWS, so the tool must NOT say the overlap predates
# it. The negated grep above only means something because this leg proves the other sentence exists.
wn="$("$ENGINE" widen FS.GG.SDD#43 --worker otter-777 --paths 'src/Thing/**' 2>&1)"; wnrc=$?
if [ "$wnrc" -eq 6 ] \
   && printf '%s' "$wn" | grep -qi 'may or may not' \
   && ! printf '%s' "$wn" | grep -qi 'cannot have introduced'; then
  ok "#1740 AC5: a WIDENING that collides is NOT reported as pre-existing (the sentences differ)"
else
  bad "#1740 AC5: a widening must not borrow the narrowing's exoneration" "rc=$wnrc: $wn"
fi

# ---- #1569: read-contract ledger, first executable group -------------------------------------------
no_mutation() {
  local name="$1"; shift
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  "$@" >/dev/null 2>&1; local rc=$?
  local ledger
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$rc" -eq 0 ] && [ "$(printf '%s' "$ledger" | jq -r .count)" = 0 ]; then
    ok "#1569: $name is a valid never-write invocation (wire ledger empty)"
  else
    bad "#1569: $name must not mutate" "rc=$rc ledger=$ledger"
  fi
}

no_mutation "batch" run batch --repo FS.GG.SDD --text
no_mutation "board" run board
no_mutation "bootstrap" run bootstrap
no_mutation "budget" run budget
no_mutation "command-contract" run command-contract --json
no_mutation "facts" run facts
no_mutation "field-id" run field-id Status
no_mutation "inbox" run inbox --repo FS.GG.SDD
no_mutation "issues" run issues FS.GG.SDD
no_mutation "option-id" run option-id Status Ready

# The first cut above deliberately started with the cache-shaped readers.  Keep extending the
# ledger with SUCCESSFUL invocations: a parser refusal costs no wire mutation too, but proves
# nothing about a command that actually reached its implementation (#266).
no_mutation "scan" run scan --repo FS.GG.SDD
no_mutation "ready" run ready --repo FS.GG.SDD
no_mutation "who" run who --repo FS.GG.SDD
no_mutation "reap (bare)" run reap --repo FS.GG.SDD
no_mutation "reconcile (bare)" run reconcile --repo FS.GG.SDD
no_mutation "landable" run landable 500 --repo FS.GG.SDD
no_mutation "overlap" run overlap FS.GG.SDD#42 FS.GG.SDD#43
no_mutation "verify-paths" run verify-paths --pr 500 --repo FS.GG.SDD
no_mutation "item-id" run item-id FS.GG.SDD#42
no_mutation "lint" run lint --repo FS.GG.SDD
no_mutation "whoami" run whoami

# `followup add` is intentionally a local-file write, not a shared-board write.  It is a valid
# (and therefore non-vacuous) driver for the command-contract row; `list` then proves the add
# reached the command rather than being accepted and ignored.
no_mutation "followup" run followup add FS.GG.SDD#42
followups="$(run followup list 2>&1)"; followups_rc=$?
if [ "$followups_rc" -eq 0 ] && printf '%s' "$followups" | grep -q 'FS.GG.SDD#42'; then
  ok "#1569: followup's local driver completed successfully"
else
  bad "#1569: followup's local driver must be valid" "rc=$followups_rc: $followups"
fi

# These two pure commands consume the real snapshot that `scan` emitted.  Supplying that
# snapshot, rather than malformed JSON, makes an empty wire ledger evidence about the execution
# path rather than an accidental parser refusal.
no_mutation_snapshot() {
  local name="$1" command="$2"
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  snapshot="$(run scan --repo FS.GG.SDD)"; snapshot_rc=$?
  result="$(printf '%s' "$snapshot" | "$ENGINE" "$command" --worker vole-418 2>&1)"; result_rc=$?
  ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
  if [ "$snapshot_rc" -eq 0 ] && [ "$result_rc" -eq 0 ] && [ "$(printf '%s' "$ledger" | jq -r .count)" = 0 ]; then
    ok "#1569: $name is a valid never-write invocation (wire ledger empty)"
  else
    bad "#1569: $name must execute without mutating" "scan_rc=$snapshot_rc rc=$result_rc output=$result ledger=$ledger"
  fi
}
no_mutation_snapshot "decide" decide
no_mutation_snapshot "lanes" lanes

# `predicate` is local too, but its successful arm needs a registry and its owning manifest.
# The dedicated predicate suite owns the broader truth table; this small fixture merely makes the
# command-contract driver's no-wire claim executable here.
mkdir -p "$PREDICATE_FIX/registry" "$PREDICATE_FIX/.repos/FS.GG.Game/template/skill-manifest"
printf '%s\n' \
  'schemaVersion: 1' \
  'skills:' \
  '  - { id: contract-probe, scope: product, owner: fs-gg-game, source: x, sha256: x, mirrored: false }' \
  >"$PREDICATE_FIX/registry/skills.yml"
printf '%s\n' '{"skills":[{"id":"contract-probe","mirrored":false}]}' \
  >"$PREDICATE_FIX/.repos/FS.GG.Game/template/skill-manifest/skill-manifest.json"
no_mutation "predicate" env FSGG_REGISTRY="$PREDICATE_FIX/registry/skills.yml" FSGG_REPOS_ROOT="$PREDICATE_FIX/.repos" "$ENGINE" predicate contract-probe mirrored false --worker vole-418

# `--apply` is a valid alternative argv shape even when this fixture finds no safe repair/reap.
# Do not call a non-zero no-op (or a parser refusal) evidence: both commands must complete their
# read/decision path.  Their bare arms above are the no-write proofs; these establish the gate's
# other spelling as executable.
valid_driver() {
  local name="$1"; shift
  curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
  "$@" >/dev/null 2>&1; local rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "#1569: $name is a valid driver"
  else
    bad "#1569: $name must not be a parser refusal" "rc=$rc"
  fi
}
valid_driver "reap --apply" run reap --repo FS.GG.SDD --apply
valid_driver "reconcile --apply" run reconcile --repo FS.GG.SDD --apply

# `next` is the one conditional row whose mutation cannot be inferred from argv.  The exemption
# is taken from the emitted field, so a renamed/new argvCannotSay row is not silently omitted.
next_reason="$(run command-contract --json | jq -r '.commands[] | select(.name == "next") | .writesWhen.argvCannotSay // empty')"
if [ -n "$next_reason" ]; then
  valid_driver "next (argvCannotSay: $next_reason)" run next --repo FS.GG.SDD
else
  bad "#1569: next must declare its argvCannotSay exemption" "command-contract omitted writesWhen.argvCannotSay"
fi

# `flush` is the opposite polarity from the two `--apply` commands: dry-run is the read arm.
# A deferred field write supplies real pending work, so neither invocation is being accepted over
# an empty, unexercised queue.  The normal arm must then land that queued write.
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/defer-next-field-write" >/dev/null
run set-field FS.GG.SDD#42 Status Ready >/dev/null 2>&1
no_mutation "flush --dry-run" run flush --dry-run
curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations" >/dev/null
flush_out="$(run flush 2>&1)"; flush_rc=$?
flush_ledger="$(curl -fsS "$FSGG_GITHUB_API_BASE/_fixture/mutations")"
if [ "$flush_rc" -eq 0 ] && [ "$(printf '%s' "$flush_ledger" | jq -r .count)" -gt 0 ]; then
  ok "#1569: flush without --dry-run mutates the queued board write"
else
  bad "#1569: flush without --dry-run must mutate pending work" "rc=$flush_rc output=$flush_out ledger=$flush_ledger"
fi

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine writes: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine writes FAILED"; exit 1; }
echo "green — the engine's write commands land, over HTTP, hermetically."

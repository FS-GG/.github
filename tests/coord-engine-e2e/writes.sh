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

SRV_OUT="$(mktemp)"; CACHE_DIR="$(mktemp -d)"
python3 "$HERE/stateful_server.py" >"$SRV_OUT" 2>/dev/null &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT"; rm -rf "$CACHE_DIR"' EXIT

PORT=""
for _ in $(seq 1 50); do PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"; [ -n "$PORT" ] && break; sleep 0.1; done
[ -n "$PORT" ] || { bad "the fixture bound a port"; echo "writes: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"
export GITHUB_TOKEN="fixture-token"
export FSGG_COORD_OWNER="FS-GG" FSGG_COORD_PROJECT="Coordination"
export FSGG_COORD_CACHE="$CACHE_DIR" FSGG_COORD_SCAN_TTL_SEC=0
# A clean identity, so the derivation never depends on the CI runner's env.
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
[ "$wdrc" -eq 0 ] && printf '%s' "$wd" | grep -q 'widened FS.GG.SDD#43 → Paths: src/New/\*\*, docs/\*\*' \
  && ok "widen rewrites the touch-set of a HELD item" \
  || bad "widen rewrites a held item's touch-set" "rc=$wdrc: $wd"

# ---- an unmatchable token is refused BEFORE any write (#273/#523) ----------------------------------
wu="$("$ENGINE" widen FS.GG.SDD#43 --worker vole-418 --paths '**/never.fs' 2>&1)"; wurc=$?
[ "$wurc" -ne 0 ] && printf '%s' "$wu" | grep -qi 'reserve NOTHING' \
  && ok "#273: an unmatchable token is refused before the write" \
  || bad "#273: an unmatchable token is refused" "rc=$wurc: $wu"

# ---- done stamps a completed item ------------------------------------------------------------------
dn="$(run "done" FS.GG.SDD#42 2>&1)"; dnrc=$?   # quoted: the coord VERB, not the loop keyword (SC1010, #648)
[ "$dnrc" -eq 0 ] && printf '%s' "$dn" | grep -q 'FSGG-DONE' \
  && ok "done stamps an item closed by a merged PR" \
  || bad "done stamps a completed item" "rc=$dnrc: $dn"

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

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine writes: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine writes FAILED"; exit 1; }
echo "green — the engine's write commands land, over HTTP, hermetically."

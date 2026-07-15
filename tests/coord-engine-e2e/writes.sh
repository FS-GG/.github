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
[ "$chrc" -eq 0 ] && printf '%s' "$ch" | grep -q 'attached FS.GG.SDD#43 as a child of FS.GG.SDD#99' \
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
dn="$(run done FS.GG.SDD#42 2>&1)"; dnrc=$?
[ "$dnrc" -eq 0 ] && printf '%s' "$dn" | grep -q 'FSGG-DONE' \
  && ok "done stamps an item closed by a merged PR" \
  || bad "done stamps a completed item" "rc=$dnrc: $dn"

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

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine writes: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine writes FAILED"; exit 1; }
echo "green — the engine's write commands land, over HTTP, hermetically."

#!/usr/bin/env bash
# SHIM PARITY (ADR-0040 Phase D.2): the D.1 corpus is green THROUGH the ADR-0034 §4.4 shim.
#
# D.1 proved the compiled engine, over HTTP, returns every answer the shell corpus certifies for bash
# (tests/coord-engine-parity/run.sh, 445 assertions). D.2's swap cut `scripts/fsgg-coord` down to the
# ~40-line SHIM that resolves the engine and execs it. This asserts the cut changes nothing the corpus can see:
#
#   1. THE WHOLE D.1 CORPUS, THROUGH THE SHIM. run.sh, re-run with its engine indirected through
#      `scripts/fsgg-coord` (now the shim) — every one of the 445 assertions is now decided by `shim <args>`
#      instead of `engine <args>`. A shim that dropped an arg, swallowed stdout/stderr, or mangled an
#      exit code would red one of them. This is the literal D.2 exit: "the corpus is green through the shim."
#
#   2. RESOLUTION + REFUSAL — the part pass-through cannot show. The shim resolves ONE engine by a fixed
#      order and REFUSES rather than silently no-op when it cannot: an explicit-but-missing bin is refused
#      (never fallen back from), an unset environment still resolves the from-source build (the anti-#266
#      property — a resolver that finds nothing must say so), and a truly empty environment exits non-zero
#      with advice, never 0.
#
# The golden is run.sh's own contract, so this cannot drift from what the engine actually certifies.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"
SHIM="$REPO_ROOT/scripts/fsgg-coord"          # D.2 swap: the entrypoint IS the shim now

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -x "$ENGINE" ] || { echo "FAIL  build the engine first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2; exit 1; }
[ -x "$SHIM" ]   || { echo "FAIL  the shim is missing or not executable: $SHIM" >&2; exit 1; }

# ---- 1. THE WHOLE D.1 CORPUS, THROUGH THE SHIM ---------------------------------------------------
# run.sh drives its engine from FSGG_COORD_ENGINE_BIN. Point it at a wrapper that hands the shim the
# real engine (tier 1) and execs it — so run.sh's 445 assertions are decided by the shim, transparently.
# (The wrapper must RESET the var: run.sh exports it pointing at the wrapper, and the shim would
# otherwise resolve the wrapper as its "explicit bin" and loop.)
WRAP="$(mktemp)"
cat >"$WRAP" <<EOF
#!/usr/bin/env bash
FSGG_COORD_ENGINE_BIN="$ENGINE" exec "$SHIM" "\$@"
EOF
chmod +x "$WRAP"
RUNLOG="$(mktemp)"
if FSGG_COORD_ENGINE_BIN="$WRAP" bash "$HERE/run.sh" >"$RUNLOG" 2>&1; then
  n="$(grep -c '^PASS ' "$RUNLOG" 2>/dev/null || echo 0)"
  ok "the full D.1 parity corpus ($n assertions) is green THROUGH the shim — no arg, byte of output, or exit code lost"
else
  bad "the D.1 corpus is NOT green through the shim" "$(tail -25 "$RUNLOG")"
fi
rm -f "$WRAP" "$RUNLOG"

# ---- 2. RESOLUTION + REFUSAL --------------------------------------------------------------------
# tier 1, honoured: an explicit, runnable bin is exec'd and its exit code returned unchanged.
out="$(FSGG_COORD_ENGINE_BIN="$ENGINE" "$SHIM" --version 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && [ -n "$out" ]; then
  ok "tier 1: FSGG_COORD_ENGINE_BIN is honoured — the named engine runs and its version prints through"
else
  bad "tier 1: an explicit engine bin must be exec'd" "rc=$rc out=$out"
fi

# tier 1, REFUSED — an explicit path that is not there is an error, NEVER a fall-through to some other
# engine the caller did not choose (bash's rule; a stale build answering for the one you meant is worse
# than an honest failure).
err="$(FSGG_COORD_ENGINE_BIN=/no/such/engine "$SHIM" --version 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$err" | grep -q '/no/such/engine'; then
  ok "tier 1: an explicit-but-missing bin is REFUSED (exit $rc), naming it — never a silent fall-back"
else
  bad "tier 1: a missing explicit bin must refuse, not fall back" "rc=$rc err=$err"
fi

# tier 4, the from-source build: with the env unset, from inside .github, the shim still resolves an
# engine and answers --version. The anti-#266 property — an unset knob is not "no engine", and a
# resolver that finds one must not pretend it found none.
out="$(cd "$REPO_ROOT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && [ -n "$out" ]; then
  ok "tier 4: with the env unset, the shim resolves an engine from source and answers (version $out)"
else
  bad "tier 4: an unset env must still resolve the from-source engine in .github" "rc=$rc out=$out"
fi

# REFUSAL, not silent no-op: no explicit bin, no engine on PATH, no manifest, no repo — the shim exits
# NON-ZERO with advice, never 0. A non-git cwd with the env unset stands in for a bare receiver that
# never restored the tool: tiers 3/4 need a git toplevel (there is none here), tier 1 is unset, and
# tier 2 (a global tool on PATH) does not exist — so nothing resolves and the shim must SAY so.
NONGIT="$(mktemp -d)"
err="$(cd "$NONGIT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$err" | grep -qi 'no fsgg-coord engine found'; then
  ok "refusal: an unresolvable environment exits non-zero ($rc) with advice — never a silent green no-op (#266)"
else
  bad "refusal: no engine anywhere must be a loud non-zero, not a silent 0" "rc=$rc err=$err"
fi
rmdir "$NONGIT" 2>/dev/null || true

echo
total=$((pass+failcount))
if [ "$failcount" -eq 0 ]; then
  echo "coord-engine shim parity: $total assertion(s), $pass passed, 0 failed"
  echo "green — the D.1 corpus is green through the shim, and the shim resolves or refuses, never no-ops."
  exit 0
else
  echo "coord-engine shim parity: $total assertion(s), $pass passed, $failcount failed"
  exit 1
fi

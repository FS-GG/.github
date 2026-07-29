#!/usr/bin/env bash
# End-to-end: the COMPILED `fsgg-coord-engine`, over HTTP, against a hermetic fixture — no token, no network.
#
# `scan | decide` is the whole claim of ADR-0040: the engine reads its own board and decides, with no bash
# in the pipeline. That was proven once, by hand, against the live board — which CI cannot do (it needs a
# token). This makes it a gate: the binary talks to a fixture GitHub through the configurable API base
# (FSGG_GITHUB_API_BASE, ADR-0040 C1), and a correct engine returns exactly the one schedulable item the
# fixture's board contains.
#
# It is deliberately NOT a coverage test — the unit suites (edge 164) and the corpus (880) own that. It is
# the ONE test that exercises the real HttpTransport, the real argument parser, the real scan→decide
# composition, and the real exit codes, all in one process boundary, hermetically. If the engine could not
# reach GitHub at all, every other test would still be green and this is the only one that would not.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

if [ ! -x "$ENGINE" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE)" >&2
  exit 1
fi

# Start the fixture; it prints its chosen port on its first stdout line.
SRV_OUT="$(mktemp)"
python3 "$HERE/fixture_server.py" >"$SRV_OUT" 2>/dev/null &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT" "${SNAP:-}" "${SNAP_ERR:-}" "${DEC:-}"; rm -rf "${CACHE_DIR:-}"' EXIT

# Wait for the port line (bounded — a fixture that never binds must fail, not hang).
PORT=""
for _ in $(seq 1 50); do
  PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"
  [ -n "$PORT" ] && break
  sleep 0.1
done
[ -n "$PORT" ] || { bad "the fixture server bound a port"; echo "coord-engine-e2e: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"
export GITHUB_TOKEN="fixture-token"          # scan REQUIRES a token; the fixture ignores its value
export FSGG_COORD_OWNER="FS-GG"
export FSGG_COORD_PROJECT="Coordination"
CACHE_DIR="$(mktemp -d)"; export FSGG_COORD_CACHE="$CACHE_DIR"  # isolate the scan cache to this run
export FSGG_COORD_SCAN_TTL_SEC=0              # no caching — every leg reads the fixture
# Leg 5 owns this variable; every OTHER leg must be blind to it. Without this a maintainer who has
# `FSGG_CLAIM_LEASE_MIN` exported runs a different test than CI does — and if it is exported MALFORMED,
# assertions 1-6 fail with messages about scan and decide, naming neither the variable nor the shell.
unset FSGG_CLAIM_LEASE_MIN

# ---- 1. scan reaches the fixture and emits a well-formed snapshot ----------------------------------
# The snapshot is scan's STDOUT (the contract); the receipt is its stderr (commentary). Keep them apart —
# mixing the receipt into the document is exactly what would make `decide` choke, and this test must not
# make that mistake on the engine's behalf.
SNAP="$(mktemp)"
SNAP_ERR="$(mktemp)"
"$ENGINE" scan --repo FS.GG.SDD >"$SNAP" 2>"$SNAP_ERR"
scan_rc=$?
[ "$scan_rc" -eq 0 ] && ok "scan exits 0 against the fixture" \
  || bad "scan exits 0 against the fixture" "rc=$scan_rc: $(cat "$SNAP_ERR")"

# The snapshot is the wire contract `decide` consumes: it must carry our one item.
if grep -q '"number":42' "$SNAP" && grep -q '"schema":"fsgg.coord.snapshot/1"' "$SNAP"; then
  ok "scan emits the fsgg.coord.snapshot/1 document, carrying the board item"
else
  bad "scan emits the snapshot carrying the board item" "$(cat "$SNAP")"
fi

# ---- 2. decide turns that snapshot into the right verdict ------------------------------------------
DEC="$(mktemp)"
"$ENGINE" decide --text <"$SNAP" >"$DEC" 2>&1
dec_rc=$?
[ "$dec_rc" -eq 0 ] && ok "decide exits 0 (green) on the fixture snapshot" \
  || bad "decide exits 0 on the fixture snapshot" "rc=$dec_rc: $(cat "$DEC")"

if grep -q 'FS.GG.SDD#42' "$DEC" && grep -qi 'schedulable in parallel' "$DEC"; then
  ok "decide reports FS.GG.SDD#42 schedulable"
else
  bad "decide reports FS.GG.SDD#42 schedulable" "$(cat "$DEC")"
fi

# ---- 3. the WHOLE pipeline, no bash anywhere in it -------------------------------------------------
pipe_out="$("$ENGINE" scan --repo FS.GG.SDD 2>/dev/null | "$ENGINE" decide --text 2>&1)"
if printf '%s' "$pipe_out" | grep -q 'FS.GG.SDD#42'; then
  ok "scan | decide is a complete scheduling pass with no bash in it"
else
  bad "scan | decide is a complete scheduling pass" "$pipe_out"
fi

# ---- 4. it FAILS CLOSED with no token — an empty board is never invented ---------------------------
notok_out="$(env -u GITHUB_TOKEN -u GH_TOKEN "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
notok_rc=$?
[ "$notok_rc" -ne 0 ] && ok "scan REFUSES without a token (never invents an empty board)" \
  || bad "scan refuses without a token" "rc=0: $notok_out"

# ---- 5. FSGG_CLAIM_LEASE_MIN is HONOURED — the variable seventeen documents promised ---------------
# `.github#1677`: this variable was named by ADR-0027, ADR-0038, `docs/coordination/parallel-work.md`,
# the generated `Protocol.fs` region and the KIT-MIRRORED skill copies it emits into every receiver —
# and read by NO code. It survived because it is UNFALSIFIABLE by inspection: an ignored environment
# variable looks exactly like an honoured one that happened to match the default. So the test may not
# assert the default and stop; it has to observe the lease CHANGE, and change to a value the default
# could not have produced. That is what "an env var with no test is how this happened" asks for.
#
# It belongs HERE and not in a unit suite: `Options.parse` is only half the claim. The other half is
# that the parsed value survives into the artifact the fleet actually reads — the snapshot's
# `leaseMinutes`, which is what every downstream staleness verdict is computed against. This leg runs
# the real binary, through the real parser, over the real transport, and reads the real document.
lease_default="$(env -u FSGG_CLAIM_LEASE_MIN "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if printf '%s' "$lease_default" | grep -qE '"leaseMinutes":[[:space:]]*120'; then
  ok "unset FSGG_CLAIM_LEASE_MIN leaves the documented 120-minute default"
else
  bad "unset FSGG_CLAIM_LEASE_MIN leaves the 120-minute default" "$(printf '%s' "$lease_default" | head -c 200)"
fi

# 45 is the load-bearing number: it is not 120, so a snapshot carrying it CANNOT have come from the
# default, and this assertion fails on the pre-#1677 engine that ignored the variable.
lease_env="$(FSGG_CLAIM_LEASE_MIN=45 "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if printf '%s' "$lease_env" | grep -qE '"leaseMinutes":[[:space:]]*45'; then
  ok "FSGG_CLAIM_LEASE_MIN=45 actually changes the lease the snapshot carries"
else
  bad "FSGG_CLAIM_LEASE_MIN=45 changes the snapshot's lease" "$(printf '%s' "$lease_env" | head -c 200)"
fi

# PRECEDENCE: an explicit flag beats the ambient environment. Both are set, and they disagree on
# purpose — an assertion where they agreed would pass whichever one won.
lease_both="$(FSGG_CLAIM_LEASE_MIN=45 "$ENGINE" scan --repo FS.GG.SDD --lease 30 2>/dev/null)"
if printf '%s' "$lease_both" | grep -qE '"leaseMinutes":[[:space:]]*30'; then
  ok "--lease 30 beats FSGG_CLAIM_LEASE_MIN=45 (flag > env > default)"
else
  bad "--lease beats FSGG_CLAIM_LEASE_MIN" "$(printf '%s' "$lease_both" | head -c 200)"
fi

# A MALFORMED value is REFUSED, not silently dropped back to 120. Falling back would rebuild the exact
# unfalsifiability #1677 is about: a typo'd value would produce no error and no change. The variable is
# `--lease` arriving down another channel, so it gets `--lease`'s contract — including its exit code.
bad_lease_out="$(FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
bad_lease_rc=$?
if [ "$bad_lease_rc" -ne 0 ] && printf '%s' "$bad_lease_out" | grep -q 'FSGG_CLAIM_LEASE_MIN'; then
  ok "a malformed FSGG_CLAIM_LEASE_MIN is REFUSED by name, never silently defaulted"
else
  bad "a malformed FSGG_CLAIM_LEASE_MIN is refused by name" "rc=$bad_lease_rc: $(printf '%s' "$bad_lease_out" | head -c 200)"
fi

# A NON-POSITIVE value is refused too, and this leg exists to kill a specific mutant: relaxing the
# guard from `n > 0` to `n >= 0` keeps every assertion above green. A lease of 0 minutes would make
# every claim instantly reapable — `Snapshot.fs` refuses exactly that on the wire, and the env channel
# must not be the way round it.
zero_lease_out="$(FSGG_CLAIM_LEASE_MIN=0 "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
zero_lease_rc=$?
if [ "$zero_lease_rc" -ne 0 ] && printf '%s' "$zero_lease_out" | grep -q 'FSGG_CLAIM_LEASE_MIN'; then
  ok "FSGG_CLAIM_LEASE_MIN=0 is REFUSED — the env channel cannot mint an instantly-reapable lease"
else
  bad "FSGG_CLAIM_LEASE_MIN=0 is refused" "rc=$zero_lease_rc: $(printf '%s' "$zero_lease_out" | head -c 200)"
fi

# WHITESPACE-ONLY is "not configured", not "malformed" — the value an unset shell variable expands to
# inside a quoted export. It must take the default, not hard-fail every command.
blank_lease="$(FSGG_CLAIM_LEASE_MIN='   ' "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if printf '%s' "$blank_lease" | grep -qE '"leaseMinutes":[[:space:]]*120'; then
  ok "a whitespace-only FSGG_CLAIM_LEASE_MIN reads as UNSET, not as malformed"
else
  bad "a whitespace-only FSGG_CLAIM_LEASE_MIN reads as unset" "$(printf '%s' "$blank_lease" | head -c 200)"
fi

# The DIAGNOSTIC verbs answer even when the variable is garbage, because a misconfigured shell is
# exactly when an operator reaches for them — and `scripts/fsgg-coord` probes `--version` to decide
# whether the tool is restored, so gating it would turn one typo into a restore on every invocation.
help_rc=0;  FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" --help    >/dev/null 2>&1 || help_rc=$?
vers_rc=0;  FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" --version >/dev/null 2>&1 || vers_rc=$?
if [ "$help_rc" -eq 0 ] && [ "$vers_rc" -eq 0 ]; then
  ok "--help and --version still answer under a malformed FSGG_CLAIM_LEASE_MIN"
else
  bad "--help/--version answer under a malformed FSGG_CLAIM_LEASE_MIN" "help_rc=$help_rc vers_rc=$vers_rc"
fi

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine-e2e: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine-e2e FAILED"; exit 1; }
echo "green — the engine reads its own board, over HTTP, hermetically."

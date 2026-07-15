#!/usr/bin/env bash
# PARITY: does the compiled engine, over HTTP, return the SAME answer the shell corpus certifies for bash?
#
# The corpus (case 22) certifies, on the parallel-work board:
#   bash scripts/fsgg-coord batch --repo sdd --json   →   ["FS.GG.SDD#70","FS.GG.SDD#74"]
# and the skip reasons for #71 (in-flight overlap), #72 (no touch-set), #73 (batch-member overlap).
#
# This drives `fsgg-coord-engine batch` against that exact board, served over HTTP (`pw_server.py`,
# lifted verbatim from the corpus fixtures), and asserts the engine reaches the SAME answer with NO
# bash in the pipeline. This is the first slice of the Phase-D corpus-through-shim gate: it turns
# "I modelled the engine on bash's output" into "the engine produces bash's certified output".
#
# The golden is the corpus's OWN certified contract, so this cannot drift from what bash actually does:
# if case 22 changes, this must change with it, and vice versa.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -x "$ENGINE" ] || { echo "FAIL  build the engine first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2; exit 1; }

SRV_OUT="$(mktemp)"; CACHE="$(mktemp -d)"
python3 "$HERE/pw_server.py" >"$SRV_OUT" 2>/dev/null &
SRV=$!
trap 'kill "$SRV" 2>/dev/null; rm -f "$SRV_OUT"; rm -rf "$CACHE"' EXIT
PORT=""; for _ in $(seq 1 50); do PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"; [ -n "$PORT" ] && break; sleep 0.1; done
[ -n "$PORT" ] || { bad "fixture bound a port"; echo "parity: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT" GITHUB_TOKEN=t
export FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$CACHE" FSGG_COORD_SCAN_TTL_SEC=0

# ---- batch --json: the machine contract `take` consumes -------------------------------------------
out="$("$ENGINE" batch --repo FS.GG.SDD --json 2>/dev/null)"; rc=$?
golden='["FS.GG.SDD#70","FS.GG.SDD#74"]'
if [ "$rc" -eq 0 ] && [ "$out" = "$golden" ]; then
  ok "batch --json equals the corpus's certified answer (byte for byte)"
else
  bad "batch --json parity" "expected $golden, got (rc=$rc): $out"
fi

# ---- batch -n 1: the width is honoured, and it is the first chosen --------------------------------
out1="$("$ENGINE" batch --repo FS.GG.SDD -n 1 --json 2>/dev/null)"
[ "$out1" = '["FS.GG.SDD#70"]' ] \
  && ok "batch -n 1 --json honours the requested width" \
  || bad "batch -n 1 --json" "expected [\"FS.GG.SDD#70\"], got: $out1"

# ---- the skip reasons name the right items and causes (stderr; the corpus asserts substrings) -----
err="$("$ENGINE" batch --repo FS.GG.SDD 2>&1 >/dev/null)"
check_skip() { printf '%s' "$err" | grep -q "$1" && ok "$2" || bad "$2" "not in stderr: $err"; }
check_skip "FS.GG.SDD#71" "batch: names #71 among the passed-over (the in-flight overlap)"
check_skip "FS.GG.SDD#72" "batch: names #72 among the passed-over (no touch-set)"
check_skip "FS.GG.SDD#73" "batch: names #73 among the passed-over (batch-member overlap)"

# ---- next: the first schedulable item ------------------------------------------------------------
nxt="$("$ENGINE" next --repo FS.GG.SDD 2>/dev/null)"
[ "$nxt" = "FS.GG.SDD#70" ] \
  && ok "next returns the first schedulable item (FS.GG.SDD#70)" \
  || bad "next parity" "expected FS.GG.SDD#70, got: $nxt"

# ---- take: pick + claim in one step (case 22 certifies "claimed FS.GG.SDD#70 by worker smew-f31") --
tk="$("$ENGINE" take --repo FS.GG.SDD --worker smew-f31 2>&1)"; tkrc=$?
if [ "$tkrc" -eq 0 ] && printf '%s' "$tk" | grep -q 'claimed FS.GG.SDD#70 by worker smew-f31'; then
  ok "take claims the first schedulable item and names the holder (case 22's certified line)"
else
  bad "take parity" "rc=$tkrc: $tk"
fi

# ---- BLOCKED (case 46): the #476 blocker rule, on its own board ------------------------------------
BLK_OUT="$(mktemp)"
python3 "$HERE/blocked_server.py" >"$BLK_OUT" 2>/dev/null &
BLK=$!
BPORT=""; for _ in $(seq 1 50); do BPORT="$(head -n1 "$BLK_OUT" 2>/dev/null)"; [ -n "$BPORT" ] && break; sleep 0.1; done
if [ -n "$BPORT" ]; then
  bjson="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$BPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" batch --repo FS.GG.SDD -n 9 --json 2>/dev/null)"
  # #476: a MERGED blocker (#701) and a closed-UNMERGED one (#705) resolve; an OPEN one (#703) blocks.
  printf '%s' "$bjson" | grep -q 'FS.GG.SDD#700' \
    && ok "#476: a MERGED-PR blocker no longer blocks — #700 is offered" \
    || bad "#476: #700 startable" "got: $bjson"
  printf '%s' "$bjson" | grep -q 'FS.GG.SDD#704' \
    && ok "#476: a closed-UNMERGED-PR blocker resolves too — #704 is offered" \
    || bad "#476: #704 startable" "got: $bjson"
  printf '%s' "$bjson" | grep -q 'FS.GG.SDD#702' \
    && bad "#476: an OPEN-PR blocker still blocks — #702 must NOT be offered" "got: $bjson" \
    || ok "#476: an OPEN-PR blocker still blocks — #702 is NOT offered"
  # #520: a CLOSED issue with a Ready column must NOT be schedulable, and the reason must name the state.
  printf '%s' "$bjson" | grep -q 'FS.GG.SDD#799' \
    && bad "#520: a CLOSED-but-Ready issue must NOT be offered" "got: $bjson" \
    || ok "#520: a CLOSED-but-Ready issue is NOT offered"
  berr="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$BPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" batch --repo FS.GG.SDD -n 9 2>&1 >/dev/null)"
  printf '%s' "$berr" | grep -qi 'closed' \
    && ok "#520: ...and the reason names the issue state (closed)" \
    || bad "#520: the reason names the closed state" "stderr: $berr"
else
  bad "blocked fixture bound a port"
fi
kill "$BLK" 2>/dev/null; rm -f "$BLK_OUT"

echo
echo "coord-engine parity: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine parity FAILED"; exit 1; }
echo "green — the engine matches the corpus's certified answer, with no bash in the pipeline."

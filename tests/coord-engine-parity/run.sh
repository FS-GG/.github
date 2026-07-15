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

# ---- the skip reasons name the right items AND the right CAUSE KIND (case 22 certifies these exact
#      substrings). It is not enough to name #71 — the reason must say WHY, and the WHY is the #428
#      distinction below.
err="$("$ENGINE" batch --repo FS.GG.SDD 2>&1 >/dev/null)"
check_skip() { printf '%s' "$err" | grep -q "$1" && ok "$2" || bad "$2" "not in stderr: $err"; }
check_skip "FS.GG.SDD#71 — overlaps in-flight work"           "batch: #71 is a LIVE-CLAIM overlap — 'overlaps in-flight work' (case 22)"
check_skip "FS.GG.SDD#72 — no 'Paths:' declared"              "batch: #72 is UNDECLARED — 'no Paths: declared' (case 22)"
check_skip "FS.GG.SDD#73 — overlaps batch member FS.GG.SDD#70" "batch: #73 is a BATCH-MEMBER overlap — names its peer #70 (case 22)"

# ---- #428: a LIVE-CLAIM overlap and a BATCH-MEMBER overlap are the SAME verdict (skipped) but two
#      DIFFERENT instructions — one is queued behind a holder's lease, the other clashes a peer being
#      scheduled right now. The flip's #428 defect dropped the lease window + holder and collapsed the
#      two into one line, so 'wait for a lease' and 'reorder your batch' read identically. The corpus
#      certifies the two phrasings ('in-flight work' vs 'batch member'); parity holds the engine to the
#      DISTINCTION, over HTTP.
l71="$(printf '%s' "$err" | grep 'FS.GG.SDD#71')"
l73="$(printf '%s' "$err" | grep 'FS.GG.SDD#73')"
if printf '%s' "$l71" | grep -q 'in-flight work' \
   && printf '%s' "$l73" | grep -q 'batch member' \
   && [ "$l71" != "$l73" ]; then
  ok "#428: a live-claim overlap and a batch-member overlap are DISTINGUISHABLE (same verdict, different instruction)"
else
  bad "#428: the live-claim and batch-member collision lines must not read alike" "71:$l71 | 73:$l73"
fi
# ...and the live-claim line carries the holder — #42's holder is finch-a3f (case 22 certifies that
# name in the widen collision); a 'batch member' line names a peer ITEM, never a worker.
printf '%s' "$l71" | grep -q 'finch-a3f' \
  && ok "#428: the live-claim overlap names the HOLDER (finch-a3f) — the fact a batch-member line has not got" \
  || bad "#428: live-claim overlap names its holder" "$l71"
case "$l73" in
  *finch-a3f*|*"held by"*) bad "#428: a batch-member line must NOT name a worker/holder — it is not a live claim" "$l73" ;;
  *) ok "#428: the batch-member overlap names a peer item, not a worker — the two lines cannot be confused" ;;
esac

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

# ---- FAIL-CLOSED (case 42): #461 — an unreadable LOCK is never an empty lock ----------------------
#
# #266's thesis at the sharpest point: a claim-marker read that comes back as non-JSON bytes (a
# truncated page, a proxy 502 rendered as text) must be a FAILED READ, not an empty claim set. A
# scheduler that folds it into "nothing is claimed" hands a live item to a second worker — the
# double-book. The malformed read is served on #42, which finch-a3f DEMONSTRABLY holds, so "nothing
# claimed" is a provably WRONG answer, not a vacuous one. Same pw board, one faulted read.
MAL_OUT="$(mktemp)"
FSGG_PARITY_MALFORMED_COMMENTS=42 python3 "$HERE/pw_server.py" >"$MAL_OUT" 2>/dev/null &
MAL=$!
MALPORT=""; for _ in $(seq 1 50); do MALPORT="$(head -n1 "$MAL_OUT" 2>/dev/null)"; [ -n "$MALPORT" ] && break; sleep 0.1; done
if [ -n "$MALPORT" ]; then
  malenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$MALPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  # `who` must not answer off a scan that did not succeed.
  who461="$(malenv who --repo FS.GG.SDD 2>&1)"; rcw=$?
  [ "$rcw" -ne 0 ] \
    && ok "#461: who over an unreadable claim marker FAILS CLOSED (non-zero)" \
    || bad "#461: who must fail closed on a malformed marker read" "rc=$rcw: $who461"
  case "$who461" in
    *"nothing is in flight"*) bad "#461: who must NOT report 'nothing is in flight' off a failed scan" "$who461" ;;
    *) ok "#461: ...and does NOT report 'nothing is in flight' — a failed read is not an empty one" ;;
  esac
  printf '%s' "$who461" | grep -qi 'malformed\|not JSON\|FAILED READ' \
    && ok "#461: ...and NAMES the unreadable read, rather than swallowing it" \
    || bad "#461: who must name the failed read" "$who461"
  # `take` must not schedule/claim off a claim set it could not read — that is the double-book.
  take461="$(malenv take --repo FS.GG.SDD --worker kite-461 2>&1)"; rct=$?
  [ "$rct" -ne 0 ] \
    && ok "#461: take REFUSES to schedule off an unreadable claim set (non-zero)" \
    || bad "#461: take must fail closed on a malformed marker read" "rc=$rct: $take461"
  case "$take461" in
    *"claimed "*) bad "#461: ...and claims NOTHING — no double-book" "$take461" ;;
    *) ok "#461: ...and claims NOTHING — no double-book" ;;
  esac
else
  bad "malformed-marker fixture bound a port"
fi
kill "$MAL" 2>/dev/null; rm -f "$MAL_OUT"

# The GUARD MUST NOT FIRE ON A LEGITIMATE EMPTY SET (#461's positive control): a SUCCESSFUL scan that
# found no claims is a valid answer, and must still report an empty set — otherwise the fix is just a
# different fail-closed bug, refusing to work on a healthy, idle repo. FS.GG.Rendering has no items on
# the pw board, so its scan succeeds and legitimately finds nothing.
okwho="$("$ENGINE" who --repo FS.GG.Rendering 2>&1)"; rcok=$?
if [ "$rcok" -eq 0 ] && printf '%s' "$okwho" | grep -qi 'nothing is in flight'; then
  ok "#461: a SUCCESSFUL scan with no claims still reports an empty set (the guard does not over-fire)"
else
  bad "#461: a healthy empty scan must report 'nothing is in flight', not fail" "rc=$rcok: $okwho"
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

# ---- STARVED (case 45): #488 — "nothing schedulable" is OBSERVED, and its causes are told apart -----
#
# The corpus certifies that a starved queue must never be inferred from an empty stderr: a blocked
# candidate, a non-startable-column candidate, a genuinely empty queue, and an unreadable board are FOUR
# outcomes, and the old bash code collapsed them into one wrong sentence. Bash carries the fix as a COUNT
# ("1 open board item"). The engine carries it structurally: it emits a per-item reason for every
# non-startable candidate, so a starved board leaves a trace an empty one does not — the same property,
# reached without a count. Parity here asserts that PROPERTY, not bash's count-prose (as #520 above
# asserts the decision and the named state, not bash's exact sentence).
STV_OUT="$(mktemp)"
python3 "$HERE/starved_server.py" >"$STV_OUT" 2>/dev/null &
STV=$!
SPORT=""; for _ in $(seq 1 50); do SPORT="$(head -n1 "$STV_OUT" 2>/dev/null)"; [ -n "$SPORT" ] && break; sleep 0.1; done
if [ -n "$SPORT" ]; then
  stv()  { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SPORT" FSGG_COORD_CACHE="$(mktemp -d)" \
    "$ENGINE" take --repo "$1" --worker smew-f31 2>&1; }
  # `batch --json` is the machine contract — the authoritative "what is offered". A starved repo offers
  # `[]`; asserting an id's absence there is byte-exact, the same idiom the #520 leg above uses.
  soff() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SPORT" FSGG_COORD_CACHE="$(mktemp -d)" \
    "$ENGINE" batch --repo "$1" -n 9 --json 2>/dev/null; }
  # A. BLOCKED (audio) — #301 leaves a trace naming its blocker, is NOT offered, and is NOT called empty.
  A="$(stv FS.GG.Audio)"
  printf '%s' "$A" | grep -q 'FS.GG.Audio#301' && printf '%s' "$A" | grep -q 'FS.GG.SDD#999' \
    && ok "#488 A: a blocked candidate leaves a trace naming its blocker (not silently dropped)" \
    || bad "#488 A: blocked candidate traced" "got: $A"
  printf '%s' "$(soff FS.GG.Audio)" | grep -q 'FS.GG.Audio#301' \
    && bad "#488 A: a blocked item must NOT be offered" "batch --json: $(soff FS.GG.Audio)" \
    || ok "#488 A: the blocked item is not offered"
  printf '%s' "$A" | grep -qi 'empty queue' \
    && bad "#488 A: a BLOCKED queue must not be called an empty one (the exact #488 defect)" "got: $A" \
    || ok "#488 A: a blocked queue is not reported as an empty one"

  # B. NON-STARTABLE COLUMN (governance) — #302 (In review) leaves a trace naming its column, so a
  # starved queue is DISTINGUISHABLE from an empty one; the Done #303 is not a live candidate.
  B="$(stv FS.GG.Governance)"
  printf '%s' "$B" | grep -q 'FS.GG.Governance#302' && printf '%s' "$B" | grep -q 'In review' \
    && ok "#488 B: a non-startable open item leaves a trace naming its column (starved, not empty)" \
    || bad "#488 B: non-startable item traced" "got: $B"
  printf '%s' "$(soff FS.GG.Governance)" | grep -q 'FS.GG.Governance#303' \
    && bad "#488 B: a Done/closed item must NOT be offered" "batch --json: $(soff FS.GG.Governance)" \
    || ok "#488 B: the Done item is not a live candidate"

  # C. GENUINELY EMPTY (sdd) — no items at all, so NO passed-over trace. This is the signature that
  # tells empty from starved, and it must differ from B — the whole point of #488.
  C="$(stv FS.GG.SDD)"
  printf '%s' "$C" | grep -qi 'passed over' \
    && bad "#488 C: a genuinely empty queue has nothing to pass over" "got: $C" \
    || ok "#488 C: a genuinely empty queue leaves no passed-over trace"
  [ "$B" != "$C" ] \
    && ok "#488: a starved queue and an empty one are DISTINGUISHABLE (the defect #488 was filed on)" \
    || bad "#488: starved and empty produced the same output" "both: $C"

  # D. UNREADABLE — a failed read is a no-verdict, never an empty queue (#266's rule, #488's leg D).
  DSRV_OUT="$(mktemp)"
  FSGG_PARITY_FAIL_BOARD=1 python3 "$HERE/starved_server.py" >"$DSRV_OUT" 2>/dev/null &
  DSRV=$!
  DPORT=""; for _ in $(seq 1 50); do DPORT="$(head -n1 "$DSRV_OUT" 2>/dev/null)"; [ -n "$DPORT" ] && break; sleep 0.1; done
  if [ -n "$DPORT" ]; then
    D="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$DPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" take --repo FS.GG.Governance --worker smew-f31 2>&1)"; drc=$?
    if [ "$drc" -ne 0 ] && ! printf '%s' "$D" | grep -qiE 'empty queue|nothing schedulable'; then
      ok "#488 D: an unreadable board fails closed — a no-verdict, never an empty queue"
    else
      bad "#488 D: unreadable board must fail closed as a no-verdict" "rc=$drc: $D"
    fi
  else
    bad "#488 D: fail fixture bound a port"
  fi
  kill "$DSRV" 2>/dev/null; rm -f "$DSRV_OUT"
else
  bad "starved fixture bound a port"
fi
kill "$STV" 2>/dev/null; rm -f "$STV_OUT"

# ---- CROSS-REPO (case 35): #312 — batch qualifies its touch-set comparison BY REPO ----------------
#
# `Paths:` tokens are repo-relative, so `src/Physics/**` HELD in one repo names different files than the
# same bare token READY in another. Scheduling the whole board (no --repo), the engine must not let a
# cross-repo namesake phantom-block a candidate, while still catching a GENUINE same-repo overlap. The
# corpus certifies both on board-xbatch — the two cross-repo namesakes ride together, the two real
# same-repo overlaps drop. This is the FIRST multi-repo fixture in this harness; the earlier slices are
# all single-repo, and repo-scoping is invisible to a single-repo board.
XB_OUT="$(mktemp)"
python3 "$HERE/xbatch_server.py" >"$XB_OUT" 2>/dev/null &
XB=$!
XBPORT=""; for _ in $(seq 1 50); do XBPORT="$(head -n1 "$XB_OUT" 2>/dev/null)"; [ -n "$XBPORT" ] && break; sleep 0.1; done
if [ -n "$XBPORT" ]; then
  xbenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$XBPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  # The machine contract: both cross-repo namesakes ride together, byte for byte (the corpus's answer).
  xbj="$(xbenv batch -n 9 --json 2>/dev/null)"
  [ "$xbj" = '["FS.GG.Templates#420","FS.GG.Governance#421"]' ] \
    && ok "#312: two cross-repo candidates sharing only a repo-relative token BOTH schedule (byte-exact)" \
    || bad "#312: cross-repo batch parity" "expected [\"FS.GG.Templates#420\",\"FS.GG.Governance#421\"], got: $xbj"
  # Neither cross-repo candidate is ever dropped FOR the phantom (#423 Game / #424 Audio hold the same
  # bare token in OTHER repos). This is the exact defect #312 was filed on.
  xberr="$(xbenv batch -n 9 2>&1 >/dev/null)"
  if printf '%s' "$xberr" | grep -qE 'FS.GG.(Templates#420|Governance#421) — overlaps'; then
    bad "#312: a candidate must NOT be dropped for a cross-repo phantom" "$xberr"
  else
    ok "#312: neither candidate is passed over for a cross-repo phantom"
  fi
  # ...and scoping narrowed the comparison, it did not BLIND the check: the two GENUINE same-repo
  # overlaps are still caught, each naming its real same-repo neighbour.
  printf '%s' "$xberr" | grep -q 'FS.GG.Audio#422 — overlaps in-flight work held by audio-n1' \
    && ok "#312: a genuine same-repo in-flight overlap is still caught (#422 ⇄ Audio#424)" \
    || bad "#312: same-repo in-flight overlap caught" "$xberr"
  printf '%s' "$xberr" | grep -q 'FS.GG.Templates#425 — overlaps batch member FS.GG.Templates#420' \
    && ok "#312: a genuine same-repo batch-member overlap is still caught (#425 ⇄ Templates#420)" \
    || bad "#312: same-repo batch-member overlap caught" "$xberr"
else
  bad "xbatch fixture bound a port"
fi
kill "$XB" 2>/dev/null; rm -f "$XB_OUT"

# ---- UNMATCHABLE TOUCH-SET (case 33): #273 — a token that matches nothing is REFUSED, never cleared -
#
# The docs once promised globs; the matcher does exact paths + subtree containment. A token that keeps a
# wildcard (`**/x`, `src/*/x`) matches NO file — and a token that matches nothing CONFLICTS WITH NOTHING.
# So the failure was OPEN: the scheduler read it as DISJOINT and handed two workers the same real files.
# board-pw4: #300 declares `**/packages.lock.json` (unmatchable); #301 declares a real lockfile path. The
# engine must schedule ONLY #301 and pass over #300 with its reason — never offer it, never clear it.
PW4_OUT="$(mktemp)"
python3 "$HERE/pw4_server.py" >"$PW4_OUT" 2>/dev/null &
PW4=$!
PW4PORT=""; for _ in $(seq 1 50); do PW4PORT="$(head -n1 "$PW4_OUT" 2>/dev/null)"; [ -n "$PW4PORT" ] && break; sleep 0.1; done
if [ -n "$PW4PORT" ]; then
  pw4env() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$PW4PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  pw4j="$(pw4env batch --repo FS.GG.SDD -n 9 --json 2>/dev/null)"
  # The machine contract: only the honest item. A fail-open would clear #300 and offer BOTH.
  [ "$pw4j" = '["FS.GG.SDD#301"]' ] \
    && ok "#273: only the honestly-declared item schedules — the unmatchable one is not cleared into a double-book" \
    || bad "#273: unmatchable-token batch parity" "expected [\"FS.GG.SDD#301\"], got: $pw4j"
  printf '%s' "$pw4j" | grep -q 'FS.GG.SDD#300' \
    && bad "#273: an item whose only token matches NOTHING must never be offered" "$pw4j" \
    || ok "#273: the unmatchable item is never offered"
  pw4err="$(pw4env batch --repo FS.GG.SDD -n 9 2>&1 >/dev/null)"
  printf '%s' "$pw4err" | grep -q "unmatchable 'Paths:' token(s): \*\*/packages.lock.json" \
    && ok "#273: ...and it is passed over WITH its reason — the token that matches nothing is NAMED" \
    || bad "#273: names the unmatchable token" "$pw4err"
  # The fail-open, in one word: an unmatchable token must never read as DISJOINT (that is the clear).
  printf '%s' "$pw4err" | grep -qi 'disjoint' \
    && bad "#273: an unmatchable token must NEVER be reported DISJOINT — that is the fail-open (#273)" "$pw4err" \
    || ok "#273: the unmatchable token is not reported DISJOINT — 'unschedulable beats mis-scheduled'"
else
  bad "unmatchable-token fixture bound a port"
fi
kill "$PW4" 2>/dev/null; rm -f "$PW4_OUT"

# ---- FENCED DECLARATION (case 33): #277 — a quoted `Paths:` line is not a declaration -------------
#
# #273's token was UNMATCHABLE; this one is FABRICATED — every token is well-formed, so a naive parser
# reserves the WRONG files with confidence. A `Paths:` line inside a ``` fence (a repro, a suggested
# widen) must not be ACQUIRED. board-pw5: #317's ONLY `Paths:` line is fenced (reserves nothing); #311
# declares the same file for real. The engine must schedule ONLY #311 and pass over #317 as an OMISSION
# — NOT as an overlap, which would mean the fenced quote was read (the #277 fail-open).
PW5_OUT="$(mktemp)"
python3 "$HERE/pw5_server.py" >"$PW5_OUT" 2>/dev/null &
PW5=$!
PW5PORT=""; for _ in $(seq 1 50); do PW5PORT="$(head -n1 "$PW5_OUT" 2>/dev/null)"; [ -n "$PW5PORT" ] && break; sleep 0.1; done
if [ -n "$PW5PORT" ]; then
  pw5env() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$PW5PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  pw5j="$(pw5env batch --repo FS.GG.SDD -n 9 --json 2>/dev/null)"
  [ "$pw5j" = '["FS.GG.SDD#311"]' ] \
    && ok "#277: only the honestly-declared item schedules — a fenced quote is not a declaration" \
    || bad "#277: fenced-declaration batch parity" "expected [\"FS.GG.SDD#311\"], got: $pw5j"
  printf '%s' "$pw5j" | grep -q 'FS.GG.SDD#317' \
    && bad "#277: an item whose only 'Paths:' is fenced must never be scheduled on a fabricated touch-set" "$pw5j" \
    || ok "#277: the fenced-only item is never offered"
  pw5err="$(pw5env batch --repo FS.GG.SDD -n 9 2>&1 >/dev/null)"
  # THE distinguishing signal: #317 is an OMISSION (declares nothing), NOT an overlap. Had the fence been
  # read, #317 would declare scripts/fsgg-coord — the very file #311 declares — and skip as a batch-mate.
  printf '%s' "$pw5err" | grep -q "FS.GG.SDD#317 — no 'Paths:' declared" \
    && ok "#277: ...and #317 is passed over as an OMISSION — the fence was NOT read" \
    || bad "#277: fenced-only item must read as no-declaration" "$pw5err"
  printf '%s' "$pw5err" | grep -qE 'FS.GG.SDD#317 —.*overlaps' \
    && bad "#277: #317 must NOT skip as an overlap — that means the fenced quote WAS read (the fail-open)" "$pw5err" \
    || ok "#277: #317 does not skip as an overlap — it reserved nothing, so it clashed with nothing"
else
  bad "fenced-declaration fixture bound a port"
fi
kill "$PW5" 2>/dev/null; rm -f "$PW5_OUT"

echo
echo "coord-engine parity: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine parity FAILED"; exit 1; }
echo "green — the engine matches the corpus's certified answer, with no bash in the pipeline."

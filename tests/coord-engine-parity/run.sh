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
  # #584: #461's fix guarded the CANDIDATE read; the sibling defect was the IN-FLIGHT claim read one
  # variable over, where a transient failure made a LIVE claim INVISIBLE and the scheduler handed out an
  # item overlapping it (the double-book with no marker to see). In the engine there is no missed call
  # site: a marker read is a typed `Result`, so a failure is `Error` everywhere, never an empty set. #42
  # is the in-flight HOLDER of src/Audio here, and its marker read is the faulted one — `batch` must
  # refuse rather than schedule #71 (which overlaps #42) off a claim set it could not read.
  b584j="$(malenv batch --repo FS.GG.SDD -n 9 --json 2>/dev/null)"; rcb=$?
  [ "$rcb" -ne 0 ] \
    && ok "#584: batch FAILS CLOSED on a faulted IN-FLIGHT marker read (not just the candidate read)" \
    || bad "#584: batch must fail closed when a live claim's marker is unreadable" "rc=$rcb: $b584j"
  printf '%s' "$b584j" | grep -q 'FS.GG.SDD#71' \
    && bad "#584: a candidate overlapping the unreadable claim must NOT be offered (the fail-open double-book)" "$b584j" \
    || ok "#584: ...and #71 (which overlaps the unreadable in-flight claim) is not offered — no invisible-claim double-book"
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

# ---- ONE ITEM PER WORKER (case 33): #516 — a second live hold is refused ---------------------------
#
# The CAS is keyed on the ITEM (one worker per item); nothing guarded the WORKER (one item per worker).
# A second claim reserves a touch-set on files nobody is editing, and `batch` then refuses everything
# overlapping it. Bash carried this guard; the engine's `claim` did not (the CAS ran with no scan of the
# worker's other holds) — the ADR-0040 "half that was never ported." This is the slice that closes it.
# Each mutating leg gets a FRESH server (a claim posts a marker; the state must not leak between legs).
hclaim() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$1" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" claim "${@:2}"; }

# 1. godwit-b49 already holds #870; claiming #871 must REFUSE, name #870, and cite the touch-set.
H1_OUT="$(mktemp)"; python3 "$HERE/holds_server.py" >"$H1_OUT" 2>/dev/null & HP1=$!
hp=""; for _ in $(seq 1 50); do hp="$(head -n1 "$H1_OUT" 2>/dev/null)"; [ -n "$hp" ] && break; sleep 0.1; done
rm -f "$H1_OUT"
if [ -n "$hp" ]; then
  r1="$(hclaim "$hp" FS.GG.SDD#871 --worker godwit-b49 2>&1)"; rc1=$?
  [ "$rc1" -ne 0 ] \
    && ok "#516: a worker who already holds an item cannot silently claim a second (fails, non-zero)" \
    || bad "#516: a second claim must be refused" "rc=$rc1: $r1"
  printf '%s' "$r1" | grep -q 'FS.GG.SDD#870' \
    && ok "#516: ...and the refusal NAMES the item they already hold (#870)" \
    || bad "#516: refusal names the held item" "$r1"
  printf '%s' "$r1" | grep -qi 'reserves a touch-set' \
    && ok "#516: ...and says WHY it is not merely untidy (the touch-set stays reserved)" \
    || bad "#516: refusal cites the reserved touch-set" "$r1"
  printf '%s' "$r1" | grep -qi 'claimed FS.GG.SDD#871' \
    && bad "#516: ...and the second item must NOT be claimed" "$r1" \
    || ok "#516: ...and the second item is not claimed (refused before the write)"
  # 2. --force is the deliberate override — the same server, #871 still free.
  r2="$(hclaim "$hp" FS.GG.SDD#871 --worker godwit-b49 --force 2>&1)"
  printf '%s' "$r2" | grep -q 'claimed FS.GG.SDD#871' \
    && ok "#516: ...but --force holds two deliberately (a rule with no escape hatch gets worked around)" \
    || bad "#516: --force must override the guard" "$r2"
  kill "$HP1" 2>/dev/null
else
  bad "one-item-per-worker fixture bound a port"
fi

# 3. A DIFFERENT worker is unaffected — the rule is one item per WORKER, not one per repo. FRESH server.
H2_OUT="$(mktemp)"; python3 "$HERE/holds_server.py" >"$H2_OUT" 2>/dev/null & HP2=$!
hp2=""; for _ in $(seq 1 50); do hp2="$(head -n1 "$H2_OUT" 2>/dev/null)"; [ -n "$hp2" ] && break; sleep 0.1; done
rm -f "$H2_OUT"
if [ -n "$hp2" ]; then
  r3="$(hclaim "$hp2" FS.GG.SDD#871 --worker stoat-c71 2>&1)"
  if printf '%s' "$r3" | grep -q 'claimed FS.GG.SDD#871' && ! printf '%s' "$r3" | grep -qi 'ALREADY HOLDS'; then
    ok "#516: a DIFFERENT worker claims #871 freely — the guard is one item per WORKER, not per repo"
  else
    bad "#516: a different worker must not be blocked by godwit-b49's hold" "$r3"
  fi
  kill "$HP2" 2>/dev/null
else
  bad "one-item-per-worker fixture (2) bound a port"
fi

# ---- BUDGET BACK-OFF (case 40): #418 — an exhausted GraphQL budget makes `take` exit EX_RATE (75) ---
#
# The GraphQL budget is the first to die under fan-out (#418 — the reason this client exists), and its
# exhaustion is a DISTINCT outcome: not an empty queue (0), not a lost race, not an unreadable board — a
# BACK-OFF signal (75). `/pnext-item` teaches a worker to key on it. The board items read (the one that
# spends the budget) 403s with a rate-limit body; the engine must NOT retry it and must exit 75.
RL_OUT="$(mktemp)"
python3 "$HERE/ratelimit_server.py" >"$RL_OUT" 2>/dev/null &
RL=$!
RLPORT=""; for _ in $(seq 1 50); do RLPORT="$(head -n1 "$RL_OUT" 2>/dev/null)"; [ -n "$RLPORT" ] && break; sleep 0.1; done
if [ -n "$RLPORT" ]; then
  rlout="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$RLPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" take --repo FS.GG.SDD --worker vole-418 2>&1)"; rlrc=$?
  [ "$rlrc" -eq 75 ] \
    && ok "#418: take on an exhausted GraphQL budget exits EX_RATE (75) — the back-off signal (case 40)" \
    || bad "#418: take must exit 75 on an exhausted budget" "rc=$rlrc: $rlout"
  # ...and it is NOT confused with the other non-zero take outcomes (a lost race, an unreadable board).
  printf '%s' "$rlout" | grep -qi 'budget' \
    && ok "#418: ...and it names the budget, not a protocol error or a lost race" \
    || bad "#418: the message must name the exhausted budget" "$rlout"
else
  bad "rate-limit fixture bound a port"
fi
kill "$RL" 2>/dev/null; rm -f "$RL_OUT"

# ---- TAKE EXIT CODES (case 52): #585 — `take` tells a claim (0) from claiming NOTHING ---------------
#
# The engine's side of the #585 contract (bash's is `52-take-exit-codes-585.sh`): `take` exits 0 ONLY
# when it claimed an item, so `take && work_it` never fires on nothing. The starved board has no SDD item
# (empty) — the engine must exit EX_NONE (2), NOT 0. Budget (75) and unreadable (non-zero) are covered by
# #418 and #461 above; the pw `take` above proves the claim path is 0. (EX_NONE is 5, not 2 — 2 is the
# engine's ExitDefect.)
S585_OUT="$(mktemp)"
python3 "$HERE/starved_server.py" >"$S585_OUT" 2>/dev/null &
S585=$!
S585PORT=""; for _ in $(seq 1 50); do S585PORT="$(head -n1 "$S585_OUT" 2>/dev/null)"; [ -n "$S585PORT" ] && break; sleep 0.1; done
if [ -n "$S585PORT" ]; then
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$S585PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" take --repo FS.GG.SDD --worker w585 >/dev/null 2>&1
  ec=$?
  [ "$ec" -eq 5 ] \
    && ok "#585: a nothing-startable queue exits EX_NONE (5), not 0 — the engine agrees with bash [#480→#585]" \
    || bad "#585: engine take on an empty queue must exit 5 (EX_NONE)" "got: $ec"
else
  bad "#585 fixture bound a port"
fi
kill "$S585" 2>/dev/null; rm -f "$S585_OUT"

# ---- COMPLETED ITEM DROPS ITS LOCK (case 32): #533 — `done --flip` releases the worker's own claim ---
#
# `done --flip` verified the merge, set Status Done, and rolled the epic up — and, before #533, never
# touched the CLAIM MARKER. `release` was the only path that dropped it, and `release` REWRITES Status, so
# running it on an item you just stamped clobbers the stamp you just earned — so on the success path
# nothing dropped the lock. It stayed live for the rest of the 120m lease, and a live marker's `Paths:`
# keep reserving its touch-set: the item most likely to overlap a just-finished one is its own FOLLOW-UP
# findings, and the recipe reliably produced an item its own author had locked out. Bash carries the fix
# (case 32); the engine's `done` never dropped the marker — the ADR-0040 "half that was never ported."
# Each mutating leg gets a FRESH server (done DELETEs a marker; the state must not leak between legs).
# Read #42's live markers straight off the fixture (python3 is already the fixture runtime, so this adds
# no dependency) — the machine fact this slice turns on is "is the claim marker still there after done?".
dclaims() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/repos/FS-GG/FS.GG.SDD/issues/42/comments").read().decode())' "$1" 2>/dev/null; }

# 1. vole-533 stamps #42 (closed by merged PR #7) and DROPS its OWN marker — a finished item stops
#    reserving its files. The stamp is still earned; the lock is gone.
D1_OUT="$(mktemp)"; DONE_HOLDER=vole-533 python3 "$HERE/done_server.py" >"$D1_OUT" 2>/dev/null & DP1=$!
dp1=""; for _ in $(seq 1 50); do dp1="$(head -n1 "$D1_OUT" 2>/dev/null)"; [ -n "$dp1" ] && break; sleep 0.1; done
rm -f "$D1_OUT"
if [ -n "$dp1" ]; then
  d1="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$dp1" FSGG_COORD_CACHE="$(mktemp -d)" \
    "$ENGINE" done 'FS.GG.SDD#42' --worker vole-533 --pr 7 --flip 2>&1)"; d1rc=$?
  [ "$d1rc" -eq 0 ] && printf '%s' "$d1" | grep -q 'FSGG-DONE   FS.GG.SDD#42' \
    && ok "#533: the stamp is still earned (green, FSGG-DONE) — dropping the lock does not touch the verdict" \
    || bad "#533: done --flip stamps the item green" "rc=$d1rc: $d1"
  # The machine fact: after `done`, the claim marker is GONE. A live marker's `Paths:` keep reserving.
  printf '%s' "$(dclaims "$dp1")" | grep -q 'worker=vole-533' \
    && bad "#533: a finished item must NOT keep its claim — the marker is still live after done" "$(dclaims "$dp1")" \
    || ok "#533: done --flip DROPS the worker's own claim — the finished item stops reserving its files"
  kill "$DP1" 2>/dev/null
else
  bad "#533 fixture (own marker) bound a port"
fi

# 2. It drops ONLY OUR OWN marker. Deleting another worker's claim is `reap`'s job, and it is destructive
#    — `done` must not do it silently just because the item is finished. Here other-999 holds #42; vole-533
#    stamps it Done, and the guarantee is structural: a `Held` is obtainable only by confirming the live
#    winner is US (verifyHeld), so `release` here CANNOT touch a marker that is not ours. FRESH server.
D2_OUT="$(mktemp)"; DONE_HOLDER=other-999 python3 "$HERE/done_server.py" >"$D2_OUT" 2>/dev/null & DP2=$!
dp2=""; for _ in $(seq 1 50); do dp2="$(head -n1 "$D2_OUT" 2>/dev/null)"; [ -n "$dp2" ] && break; sleep 0.1; done
rm -f "$D2_OUT"
if [ -n "$dp2" ]; then
  d2="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$dp2" FSGG_COORD_CACHE="$(mktemp -d)" \
    "$ENGINE" done 'FS.GG.SDD#42' --worker vole-533 --pr 7 --flip 2>&1)"
  printf '%s' "$(dclaims "$dp2")" | grep -q 'worker=other-999' \
    && ok "#533: ...but it must NOT delete a claim that is not ours — other-999's marker is left intact" \
    || bad "#533: done deleted a stranger's claim" "$(dclaims "$dp2")"
  printf '%s' "$d2" | grep -q 'still holds its claim' \
    && ok "#533: ...it says so instead — names the other holder, drops only your own lock" \
    || bad "#533: done names the claim it left" "$d2"
  printf '%s' "$d2" | grep -qi 'reap' \
    && ok "#533: ...and points at reap — the destructive path a stranger's claim actually needs" \
    || bad "#533: done points at reap for another's claim" "$d2"
  kill "$DP2" 2>/dev/null
else
  bad "#533 fixture (stranger's marker) bound a port"
fi

# ---- READY IS THE MACHINE CONTRACT, AND A TRUTH READ (case 12): --json + the #520 column-not-state rule -
#
# The corpus certifies `ready` as a thrifty, machine-readable TRUTH read: `ready --json` is a JSON ARRAY
# (the contract /check-board and `next` consume), it excludes Done by DEFAULT and nothing else, and it
# shows what is ON THE BOARD — including items the SCHEDULER refuses. The engine's `ready` carried a
# double port gap: it IGNORED --json (always printed the human table, so a `jq` consumer choked), and it
# filtered by the ISSUE STATE, not the board Status column — so a CLOSED-but-Ready row (the #520 residue
# the truth read exists to surface) was HIDDEN. This slice holds the engine to case 12's answers, over
# HTTP: the machine contract is JSON, Done is the only default exclusion, the closed-but-Ready row is
# shown, and --status/--all/--repo widen and scope exactly as bash's `board_filter` does.
RDY_OUT="$(mktemp)"
python3 "$HERE/ready_server.py" >"$RDY_OUT" 2>/dev/null &
RDY=$!
RDYPORT=""; for _ in $(seq 1 50); do RDYPORT="$(head -n1 "$RDY_OUT" 2>/dev/null)"; [ -n "$RDYPORT" ] && break; sleep 0.1; done
if [ -n "$RDYPORT" ]; then
  rdy() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$RDYPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" ready "$@"; }

  # THE MACHINE CONTRACT: `ready --json` is a JSON array a consumer can parse — not the human table it
  # used to print regardless of --json (the port gap). `jq -e` fails non-zero if the stream is not JSON.
  rdyj="$(rdy --repo FS.GG.SDD --json 2>/dev/null)"
  if printf '%s' "$rdyj" | jq -e 'type == "array"' >/dev/null 2>&1; then
    ok "ready --json is a JSON array — the machine contract, not the human table (the port gap closed)"
  else
    bad "ready --json must emit a JSON array" "got: $rdyj"
  fi

  # DEFAULT excludes Done, and NOTHING ELSE: Ready, Backlog and In-progress all stay.
  nums="$(printf '%s' "$rdyj" | jq -c '[.[].number] | sort')"
  [ "$nums" = '[99,127,200,201]' ] \
    && ok "ready: excludes Done by default and keeps everything else (Ready/Backlog/In-progress), byte-exact" \
    || bad "ready default set" "expected [99,127,200,201], got: $nums"
  printf '%s' "$rdyj" | jq -e 'any(.[]; .number==55)' >/dev/null 2>&1 \
    && bad "ready: the Done item (#55) must NOT appear by default" "$rdyj" \
    || ok "ready: the Done item (#55) is excluded by default"

  # #520 — THE TRUTH READ. A CLOSED issue whose board column still says Ready is SHOWN: `ready` reads the
  # COLUMN, never the issue state, because the column is the projection /check-board reconciles. The old
  # engine filtered `State = Open` and would have hidden exactly this row.
  cbr="$(printf '%s' "$rdyj" | jq -c '.[] | select(.number==201)')"
  printf '%s' "$cbr" | jq -e '.state == "CLOSED"' >/dev/null 2>&1 \
    && ok "#520: a CLOSED-but-Ready row is SHOWN by ready, and reports its CLOSED state (the truth read)" \
    || bad "#520: ready must show the closed-but-Ready row, filtering on the column not the state" "got: $cbr"

  # --status WIDENS to exactly that column (Done included) — the corpus's `ready --status Done -> #54`.
  donej="$(rdy --repo FS.GG.SDD --status Done --json 2>/dev/null | jq -c '[.[].number] | sort')"
  [ "$donej" = '[55]' ] \
    && ok "ready --status Done widens past the not-Done default to exactly the Done column (#55)" \
    || bad "ready --status Done" "expected [55], got: $donej"

  # `.github`'s only item is Done, so it is EMPTY by default and non-empty under --all — the corpus's
  # `ready --repo .github -> empty`, the same board shape (#54 Done), one transport over.
  ghdef="$(rdy --repo .github --json 2>/dev/null)"
  [ "$ghdef" = '[]' ] \
    && ok "ready --repo .github: its only item is Done, so the not-Done default makes it empty" \
    || bad "ready --repo .github default" "expected [], got: $ghdef"
  rdy --repo .github --all --json 2>/dev/null | jq -e 'any(.[]; .number==54)' >/dev/null 2>&1 \
    && ok "ready --all widens past the default — the Done #54 appears (a TRUTH read shows the whole board)" \
    || bad "ready --all must show the Done item" "$(rdy --repo .github --all --json 2>/dev/null)"

  # --repo SCOPES: a Ready namesake in another repo (#202 Rendering) is not shown under --repo FS.GG.SDD.
  printf '%s' "$rdyj" | jq -e 'any(.[]; .number==202)' >/dev/null 2>&1 \
    && bad "ready --repo FS.GG.SDD must not reach into another repo (#202 Rendering)" "$rdyj" \
    || ok "ready --repo scopes — a cross-repo namesake column is not shown"

  # The human projection is still reachable (--text), and it is a TABLE, not JSON — the same row set.
  rdyt="$(rdy --repo FS.GG.SDD --text 2>/dev/null)"
  if printf '%s' "$rdyt" | grep -q 'Ready .*FS.GG.SDD#99' && ! printf '%s' "$rdyt" | grep -q '^\['; then
    ok "ready --text still renders the human table (JSON is the default, text is opt-in — as batch is)"
  else
    bad "ready --text must render the human table" "got: $rdyt"
  fi
else
  bad "ready fixture bound a port"
fi
kill "$RDY" 2>/dev/null; rm -f "$RDY_OUT"

# ---- WHO IS THE TRUTH READ OF THE LOCK (case 20): held / stale / unclaimed, --json + the NULL worker -----
#
# The corpus (case 20, and case 25 off-board) certifies `who` as a truth read of the LOCK, not the board
# column: it lists the IN-FLIGHT items — HELD (a live marker), STALE (a marker past its lease, still a lock
# only `reap` may break), and UNCLAIMED (In progress with NO marker — work outside the protocol) — and NONE
# of the Ready candidates nobody has claimed. `who --json` is the machine contract a consumer keys on
# (`.number/.worker/.state/.paths`), with a NULL worker where only the column, not a marker, puts the item
# in flight. The engine carried the SAME sibling gap `ready` did (#789): `who` IGNORED --json (always the
# human table, so a `jq` consumer choked) and reported ONLY live holders — dropping the STALE and UNCLAIMED
# rows the truth read exists to surface. This holds the fixed engine to case 20's answers on the SAME
# board case 22 certifies `batch` against (board-pw), one transport over: #42 is held by finch-a3f (fresh),
# #43 by ghost-000 (5h old → stale), #60 is In progress with no marker, and #70–74 are Ready candidates.
#
# A FRESH pw server: the top-level one is MUTABLE and the `take` leg above claimed #70 on it (smew-f31's
# marker is now live there), so #70 would read as held. `who` reads the LOCK, so it correctly sees that
# marker — the state must not leak between legs, exactly as the #516/#533 mutating legs take a fresh server.
WHO_OUT="$(mktemp)"
python3 "$HERE/pw_server.py" >"$WHO_OUT" 2>/dev/null &
WHO=$!
WHOPORT=""; for _ in $(seq 1 50); do WHOPORT="$(head -n1 "$WHO_OUT" 2>/dev/null)"; [ -n "$WHOPORT" ] && break; sleep 0.1; done
rm -f "$WHO_OUT"
if [ -z "$WHOPORT" ]; then
  bad "who fixture bound a port"
else
whor() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$WHOPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" who --repo FS.GG.SDD "$@"; }

# THE MACHINE CONTRACT: `who --json` is a JSON array a consumer can parse — not the human table it used to
# print regardless of --json (the exact port gap #789 closed for `ready`, one command over).
whoj="$(whor --json 2>/dev/null)"
if printf '%s' "$whoj" | jq -e 'type == "array"' >/dev/null 2>&1; then
  ok "who --json is a JSON array — the machine contract, not the human table (the sibling port gap to ready)"
else
  bad "who --json must emit a JSON array" "got: $whoj"
fi

# IN-FLIGHT, NO MORE NO LESS: the three claimed/orphaned items, and NONE of the Ready candidates (#70–74).
# `who` reports the LOCK; a Ready item nobody has claimed is not in flight, and the old live-only who would
# have shown only #42.
whonums="$(printf '%s' "$whoj" | jq -c '[.[].number] | sort')"
[ "$whonums" = '[42,43,60]' ] \
  && ok "who: lists exactly the in-flight items (held/stale/unclaimed), never the Ready candidates" \
  || bad "who in-flight set" "expected [42,43,60], got: $whonums"

# HELD: #42's live marker is 'held', names finch-a3f (case 20's holder), and carries the paths it reserves.
h42="$(printf '%s' "$whoj" | jq -c '.[] | select(.number==42)')"
printf '%s' "$h42" | jq -e '.state=="held" and .worker=="finch-a3f"' >/dev/null 2>&1 \
  && ok "who: a live marker is 'held' and names its worker (finch-a3f) — case 20's holder" \
  || bad "who: #42 held by finch-a3f" "got: $h42"
printf '%s' "$h42" | jq -e '.paths | any(. == "src/Audio/**")' >/dev/null 2>&1 \
  && ok "who: ...and carries the touch-set the claim reserves (src/Audio), read from the item body" \
  || bad "who: #42 carries its reserved paths" "got: $h42"

# STALE: #43's marker is 5h old against a 120m lease, so it is a lock PAST its lease — 'stale', not dropped
# (the old who filtered stale markers out via `winner`), and it still names the (likely dead) ghost-000.
s43="$(printf '%s' "$whoj" | jq -c '.[] | select(.number==43)')"
printf '%s' "$s43" | jq -e '.state=="stale" and .worker=="ghost-000"' >/dev/null 2>&1 \
  && ok "who: a marker past its lease is 'stale' (still a lock only reap breaks) and still names its holder" \
  || bad "who: #43 stale ghost-000" "got: $s43"

# UNCLAIMED: #60 is In progress with NO marker — only the COLUMN, not a marker, makes it in flight, so its
# worker is NULL (case 20's certified `who --json ... | .worker == null`), and its declared paths resolve.
u60="$(printf '%s' "$whoj" | jq -c '.[] | select(.number==60)')"
printf '%s' "$u60" | jq -e '.state=="unclaimed" and .worker==null' >/dev/null 2>&1 \
  && ok "who --json: an In-progress item with no marker is 'unclaimed' with a NULL worker (case 20)" \
  || bad "who: #60 unclaimed null worker" "got: $u60"
printf '%s' "$u60" | jq -e '.paths | any(. == "src/Orphan/**")' >/dev/null 2>&1 \
  && ok "who: ...and a markerless item's touch-set still resolves (the files something is evidently editing)" \
  || bad "who: #60 carries its paths" "got: $u60"

# THE HUMAN TABLE is the DEFAULT (no --json, as case 20 reads it): it names the holder, and it FLAGS the two
# rows the old live-only who dropped on the floor.
whot="$(whor 2>/dev/null)"
printf '%s' "$whot" | grep -q 'finch-a3f' \
  && ok "who (text default): names the worker holding each item (finch-a3f) — JSON is opt-in, unlike ready" \
  || bad "who text names holder" "got: $whot"
printf '%s' "$whot" | grep -qE 'FS.GG.SDD#43.*STALE' \
  && ok "who (text): flags a claim past its lease as STALE (the row the old who filtered out)" \
  || bad "who text STALE" "got: $whot"
printf '%s' "$whot" | grep -qE 'FS.GG.SDD#60.*UNCLAIMED' \
  && ok "who (text): flags In-progress work with NO marker as UNCLAIMED (the row the old who never had)" \
  || bad "who text UNCLAIMED" "got: $whot"

# ...and it WARNS on stderr about the markerless item — where case 20 looks — so a `who` whose stdout is
# piped to a consumer still surfaces the one drift no reconciler can fix by itself.
whoerr="$(whor 2>&1 >/dev/null)"
printf '%s' "$whoerr" | grep -q 'In progress with NO claim marker' \
  && ok "who: warns on stderr that someone is working outside the protocol (case 20)" \
  || bad "who stderr warning" "got: $whoerr"

# THE DISTINCTION, stated once more against the machine contract: a Ready candidate nobody has claimed is
# NOT in flight — because `who` reads the lock, not the board column.
printf '%s' "$whoj" | jq -e 'any(.[]; .number==70)' >/dev/null 2>&1 \
  && bad "who: a Ready, unclaimed candidate (#70) must NOT be listed as in flight" "$whoj" \
  || ok "who: a Ready candidate nobody claimed is not in flight — who reports the lock, not the column"
fi
kill "$WHO" 2>/dev/null

# ---- CHILD LINKS A SUB-ISSUE, AND FAILS CLOSED READING THE EDGE (case 15): #320 -------------------
#
# `child` attaches an issue as a native SUB-ISSUE of a parent so `done --flip`'s epic rollup can see it.
# The corpus certifies four properties, and each is a fail-closed rule in a different coat: the POST
# carries the child's REST INTEGER ID as a NUMBER (#266 — the quoted `-f` string form 422s, and the id
# never the number, since two repos can each have an issue #7); a re-link is idempotent (SUCCESS, keyed by
# id); the existing-links READ fails closed (#320 — an unreachable read is NOT an absent edge, or `child`
# would POST, collect a 422, and blame the token); and a failed POST surfaces the API's OWN diagnosis.
#
# The engine's `child` POSTed BLINDLY — no existing-links read at all, so no idempotency and no #320
# fail-closed: the ADR-0040 "half that was never ported." This slice closes it and holds the fixed engine
# to case 15's answers over HTTP. The corpus's `-F sub_issue_id=1047` (typed number, not the string that
# 422s) is re-expressed at the HTTP layer: the fixture records the POST body and serves it on `/_posts`, so
# the assertion is that `sub_issue_id` arrives as a JSON NUMBER — the same property, one transport over.
# Each leg spawns a FRESH server (a POST mutates the edge set), exactly as the #516/#533 mutating legs do.
childsrv() {  # childsrv <env-kv...> --  ; sets globals CHILD_PORT and CHILD_SRV for the spawned fixture
  local envs=() ; while [ "$1" != "--" ]; do envs+=("$1"); shift; done; shift
  local out; out="$(mktemp)"
  # `${envs[@]+…}` so an empty env list is not an unbound-variable error under `set -u` (bash < 4.4).
  env ${envs[@]+"${envs[@]}"} python3 "$HERE/child_server.py" >"$out" 2>/dev/null &
  local srv=$! port=""
  for _ in $(seq 1 50); do port="$(head -n1 "$out" 2>/dev/null)"; [ -n "$port" ] && break; sleep 0.1; done
  rm -f "$out"
  CHILD_PORT="$port"; CHILD_SRV="$srv"
}
# Read the POST bodies the fixture recorded — python3 is already the fixture runtime, so this adds no
# dependency (the same idiom the #533 `dclaims` leg uses to read state straight off its server).
posts_on() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_posts").read().decode())' "$1" 2>/dev/null; }

# 1. THE LINK. An empty parent, a fresh child: `child` links it, names the edge in the sub-issue's own
#    vocabulary (case 15's "linked … as a sub-issue of …"), and POSTs the child's REST id as a NUMBER.
childsrv -- child
if [ -z "$CHILD_PORT" ]; then bad "child fixture bound a port"; else
  cenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$CHILD_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  c1="$(cenv child FS.GG.SDD#302 FS.GG.SDD#47 2>&1)"; c1rc=$?
  { [ "$c1rc" -eq 0 ] && printf '%s' "$c1" | grep -q 'linked FS.GG.SDD#47 as a sub-issue of FS.GG.SDD#302'; } \
    && ok "child: links the issue as a sub-issue and names the edge (case 15's certified line)" \
    || bad "child link parity" "rc=$c1rc: $c1"
  # #266: the id is POSTed as a JSON NUMBER (1047 = #47's REST id), never the quoted string that 422s, and
  # never the issue number 47 — the HTTP-level form of the corpus's `-F sub_issue_id=1047`.
  p1="$(posts_on "$CHILD_PORT")"
  printf '%s' "$p1" | jq -e '.[0].sub_issue_id == 1047 and (.[0].sub_issue_id | type) == "number"' >/dev/null 2>&1 \
    && ok "#266: child POSTs the child's REST id (1047) as a JSON NUMBER, not its number and not a string" \
    || bad "#266: sub_issue_id must be the numeric REST id" "posts: $p1"
  kill "$CHILD_SRV" 2>/dev/null
fi

# 2. IDEMPOTENT BY ID. The parent already has #47 (id 1047) as a sub-issue: re-linking is SUCCESS, and it
#    POSTs NOTHING — a worker re-running its close-out never has to check the edge first.
childsrv FSGG_PARITY_LINKED=1047 -- child
if [ -z "$CHILD_PORT" ]; then bad "child idempotent fixture bound a port"; else
  c2="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$CHILD_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" child FS.GG.SDD#302 FS.GG.SDD#47 2>&1)"; c2rc=$?
  { [ "$c2rc" -eq 0 ] && printf '%s' "$c2" | grep -q 'already a sub-issue'; } \
    && ok "child: re-linking an already-attached child is idempotent SUCCESS (keyed by id)" \
    || bad "child idempotent parity" "rc=$c2rc: $c2"
  [ "$(posts_on "$CHILD_PORT")" = '[]' ] \
    && ok "child: ...and an idempotent re-link POSTs nothing — the edge already exists" \
    || bad "child: an idempotent re-link must not POST" "posts: $(posts_on "$CHILD_PORT")"
  kill "$CHILD_SRV" 2>/dev/null
fi

# 3. FAIL CLOSED ON THE EDGE READ (#320). The existing-links read 500s: `child` must REFUSE, name the
#    unreadable subject, and POST NOTHING — an unreachable read is not an absent edge (or it POSTs, collects
#    a 422, and blames the token). This is #266's thesis on the sub-issue graph.
childsrv FSGG_PARITY_SUBISSUES_FAIL=1 -- child
if [ -z "$CHILD_PORT" ]; then bad "child fail-closed fixture bound a port"; else
  c3="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$CHILD_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" child FS.GG.SDD#302 FS.GG.SDD#47 2>&1)"; c3rc=$?
  [ "$c3rc" -ne 0 ] \
    && ok "#320: an unreachable existing-links read fails closed (non-zero)" \
    || bad "#320: child must refuse on an unreadable sub-issue read" "rc=$c3rc: $c3"
  printf '%s' "$c3" | grep -q 'refusing to guess whether' \
    && ok "#320: ...and it NAMES the refusal — an unreachable read is not an absent edge" \
    || bad "#320: child names the refusal to guess" "$c3"
  [ "$(posts_on "$CHILD_PORT")" = '[]' ] \
    && ok "#320: ...and it POSTs NOTHING while it cannot tell (no 422-and-blame-the-token)" \
    || bad "#320: child must not POST on a failed edge read" "posts: $(posts_on "$CHILD_PORT")"
  kill "$CHILD_SRV" 2>/dev/null
fi

# 4. SURFACE THE API'S OWN ERROR. The link POST 422s: `child` reports the API's diagnosis (422), never a
#    guessed cause — a 422 (already linked / cross-repo refusal) and a 403 (no `issues: write`) are
#    different problems with different fixes.
childsrv FSGG_PARITY_POST_FAIL=1 -- child
if [ -z "$CHILD_PORT" ]; then bad "child POST-fail fixture bound a port"; else
  c4="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$CHILD_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" child FS.GG.SDD#302 FS.GG.SDD#47 2>&1)"; c4rc=$?
  [ "$c4rc" -ne 0 ] && printf '%s' "$c4" | grep -q '422' \
    && ok "child: a failed link reports the API's own error (422), not a guessed cause" \
    || bad "child POST-fail parity" "rc=$c4rc: $c4"
  kill "$CHILD_SRV" 2>/dev/null
fi

# 5. A MISSING ARGUMENT IS REFUSED. `child` needs BOTH refs; one is a usage error, not a link.
childsrv -- child
if [ -z "$CHILD_PORT" ]; then bad "child missing-arg fixture bound a port"; else
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$CHILD_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" child FS.GG.SDD#302 >/dev/null 2>&1
  [ "$?" -ne 0 ] \
    && ok "child: refuses a missing argument (both refs are required)" \
    || bad "child must refuse a single argument"
  kill "$CHILD_SRV" 2>/dev/null
fi

# ---- TAKE/NEXT NAME THE OBSERVED REASON, NOT A GUESSED LIST (case 41 §4): #440 --------------------
#
# The corpus (`tests/fsgg-coord/cases/41-residue-and-full-queue.sh` §4, #440) certifies that a full
# queue must never read as an empty one, and — the leg this slice pins — that `take` must NOT recite a
# GUESSED list of causes. Bash's own defect was the sentence "no schedulable item — every candidate is
# blocked, claimed, overlapping, or undeclared": every clause could be false, and the true reason (the
# item is blocked / in review / undeclared) was not among them, so a worker idled in front of work it
# could see the reason for. `batch`/`decide` in the engine already answer #440 the honest way — the
# per-item "passed over" reasons ARE the answer — but `take` and `next` slapped that same guessed
# headline back on top in their empty branch, reintroducing the exact sentence #440 was filed on. This
# slice holds the fixed engine to #440's PROPERTY over HTTP: a starved queue names the OBSERVED reason
# for each candidate and never the guessed list. It reuses case 45's `board-starved` server (Audio #301
# blocked, Governance #302 in review) — that world is already a full-but-unschedulable queue, one
# transport over, so nothing new has to be fabricated.
#
# THE BACKLOG-FALLBACK LEG OF §4 IS A DELIBERATE DIVERGENCE, NOT A PORT. Bash's `take` falls back to
# Backlog by default ("from Backlog"); the engine makes that fallback an explicit `--include-backlog`
# flag and certifies the decision in `SchedulabilityTests` ("#440 ...NOT startable when it is off — the
# fallback is a decision, not a default"). So this slice asserts the guessed-list property, which the
# engine holds, and does NOT assert bash's default Backlog promotion, which the engine deliberately does
# not do — the disposition, on the record, rather than a silently skipped assertion.
GUESSED='every candidate is blocked, claimed, overlapping, or undeclared'
G_OUT="$(mktemp)"
python3 "$HERE/starved_server.py" >"$G_OUT" 2>/dev/null &
GSRV=$!
GPORT=""; for _ in $(seq 1 50); do GPORT="$(head -n1 "$G_OUT" 2>/dev/null)"; [ -n "$GPORT" ] && break; sleep 0.1; done
if [ -n "$GPORT" ]; then
  ge() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$GPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@" 2>&1; }
  # A. take over a BLOCKED queue (#301 blocked by #999) must NOT recite the guessed list — the #440 defect.
  tA="$(ge take --repo FS.GG.Audio --worker w440)"
  printf '%s' "$tA" | grep -qF "$GUESSED" \
    && bad "#440: take must NOT recite a guessed list of causes over a starved queue (case 41 §4)" "got: $tA" \
    || ok "#440: take does not recite the guessed 'every candidate is blocked/claimed/...' list (case 41 §4)"
  # ...and it names the OBSERVED reason instead — #301's blocker, the fact a worker actually needs.
  printf '%s' "$tA" | grep -q 'FS.GG.Audio#301' && printf '%s' "$tA" | grep -q 'FS.GG.SDD#999' \
    && ok "#440: ...and names the OBSERVED per-item reason (#301 blocked by #999), not a guess" \
    || bad "#440: take must name the observed blocker on a starved queue" "got: $tA"
  # ...on the honest headline `batch` already uses — "nothing schedulable right now.", never a false list.
  printf '%s' "$tA" | grep -q 'nothing schedulable right now' \
    && ok "#440: ...under the honest headline (the shape batch/decide already emit)" \
    || bad "#440: take must print the honest 'nothing schedulable right now.' headline" "got: $tA"
  # B. next carries the SAME contract — it is `batch` capped at one, so it cannot recite the list either.
  nA="$(ge next --repo FS.GG.Audio --worker w440)"
  printf '%s' "$nA" | grep -qF "$GUESSED" \
    && bad "#440: next must NOT recite the guessed list either (it is batch capped at one)" "got: $nA" \
    || ok "#440: next does not recite the guessed list — the same honest #440 contract as take"
  printf '%s' "$nA" | grep -q 'FS.GG.Audio#301' \
    && ok "#440: ...and next names the observed reason too" \
    || bad "#440: next must name the observed reason" "got: $nA"
  # C. A NON-STARTABLE COLUMN is a different observed reason (#302 In review), and still not the guess —
  #    proving the fix names what it saw rather than swapping one fixed sentence for another.
  tG="$(ge take --repo FS.GG.Governance --worker w440)"
  printf '%s' "$tG" | grep -qF "$GUESSED" \
    && bad "#440: a non-startable-column queue must not recite the guessed list" "got: $tG" \
    || ok "#440: a non-startable-column queue names its OWN reason, not the guess"
  printf '%s' "$tG" | grep -q 'FS.GG.Governance#302' && printf '%s' "$tG" | grep -q 'In review' \
    && ok "#440: ...naming #302 and its column (In review) — the reason a worker acts on" \
    || bad "#440: take must name the non-startable column" "got: $tG"
else
  bad "starved fixture (for #440) bound a port"
fi
kill "$GSRV" 2>/dev/null; rm -f "$G_OUT"

# ---- SET-FIELD --BATCH: N FIELDS, ONE GRAPHQL REQUEST (case 11): #448 ------------------------------
#
# The corpus (`tests/fsgg-coord/cases/11-set-field-batch.sh`, #448) certifies that N field writes aliased
# into ONE mutation document cost ONE GraphQL point, not N — the whole reason the batch path exists. The
# shell corpus proves it by COUNTING `gh` invocations; an F# tool calling HTTPS is invisible to that stub,
# so this slice re-expresses the count at the HTTP layer (ADR-0040 C1): `setfield_server.py` records every
# FIELD mutation document the engine POSTs, and the assertion is that a three-field batch emits exactly ONE
# of them, carrying f0/f1/f2. The property is unchanged; it is counted one transport over.
#
# This was a PORT GAP, not a divergence. `Board.setFieldBatch` (the aliased-document builder, #448) existed
# in the engine but had NO caller — `set-field` only ever took a single `<ref> <field> <value>`. This slice
# lands with the fix that wires `set-field --batch` to it (and `Board.boardWriteBatch`, the batch sibling of
# the one board write, carrying the same deferral policy). The corpus's harder halves are held too: an empty
# value reaches the DISTINCT clear mutation; a refused pair spends ZERO GraphQL (a rejected value must not
# spend the budget that dies first); a per-alias failure is EX_PARTIAL and NEVER queued (replaying rewrites
# what landed); and an exhausted budget refused the whole document, so EVERY pair is queued.
sfsrv() {  # sfsrv <env-kv...> --  ; sets globals SF_PORT and SF_SRV for the spawned fixture
  local envs=() ; while [ "$1" != "--" ]; do envs+=("$1"); shift; done; shift
  local out; out="$(mktemp)"
  env ${envs[@]+"${envs[@]}"} python3 "$HERE/setfield_server.py" >"$out" 2>/dev/null &
  local srv=$! port=""
  for _ in $(seq 1 50); do port="$(head -n1 "$out" 2>/dev/null)"; [ -n "$port" ] && break; sleep 0.1; done
  rm -f "$out"
  SF_PORT="$port"; SF_SRV="$srv"
}
# The mutation count and the last document the fixture recorded — `gcount` + `cat "$GH_LOG"`, one transport
# over (the same python3-off-the-server idiom the #533/#320 legs use).
muts_on()  { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_mutations").read().decode())' "$1" 2>/dev/null; }

# 1. THE COUNT IS THE CRITERION. Three fields — a SINGLE_SELECT, a DATE and a TEXT — cost exactly ONE
#    mutation request, aliased f0/f1/f2, each value routed to its field's own mutation shape.
sfsrv -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field fixture bound a port"; else
  sfenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  b1="$(sfenv set-field --batch FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=fs-gg-ui-template' 2>&1)"; b1rc=$?
  mj="$(muts_on "$SF_PORT")"
  { [ "$b1rc" -eq 0 ] && [ "$(printf '%s' "$mj" | jq -r '.count')" = "1" ]; } \
    && ok "#448: THREE fields cost exactly ONE GraphQL mutation request (the count, one transport over)" \
    || bad "#448: a three-field batch must be one request" "rc=$b1rc count=$(printf '%s' "$mj" | jq -r '.count') out=$b1"
  doc="$(printf '%s' "$mj" | jq -r '.last')"
  printf '%s' "$doc" | grep -q 'f0: updateProjectV2ItemFieldValue' && printf '%s' "$doc" | grep -q 'f2: updateProjectV2ItemFieldValue' \
    && ok "#448: ...emitted as ONE aliased document — f0 and f2 ride the SAME mutation" \
    || bad "#448: the batch must alias f0..f2 into one document" "doc: $doc"
  printf '%s' "$doc" | grep -q 'singleSelectOptionId: "opt_p2"' \
    && ok "#448: SINGLE_SELECT routes 'P2 SDD' to singleSelectOptionId (opt_p2)" \
    || bad "#448: SINGLE_SELECT routing" "doc: $doc"
  printf '%s' "$doc" | grep -q 'date: "2026-08-01"' && printf '%s' "$doc" | grep -q 'text: "fs-gg-ui-template"' \
    && ok "#448: DATE routes to date, TEXT routes to text — each value to its field's own shape" \
    || bad "#448: DATE/TEXT routing" "doc: $doc"
  printf '%s' "$doc" | grep -q 'itemId: "PVTI_item42"' \
    && ok "#448: the resolved board item id is carried on every alias" \
    || bad "#448: the item id must be carried" "doc: $doc"
  # The batch must not fall back to per-field writes on a different transport — pin the negative.
  [ "$(printf '%s' "$mj" | jq -r '.count')" = "1" ] \
    && ok "#448: the batch does NOT fall back to N per-field writes (count stays 1, not 3)" \
    || bad "#448: the batch must not loop per field" "count=$(printf '%s' "$mj" | jq -r '.count')"
  kill "$SF_SRV" 2>/dev/null
fi

# 2. AN EMPTY VALUE CLEARS — and `update` with an empty value is a NO-OP on the real API, not a clear, so
#    the batch must reach the DISTINCT clear mutation, exactly as the single path reaches for it.
sfsrv -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field clear fixture bound a port"; else
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --batch FS.GG.SDD#42 'Contract=' >/dev/null 2>&1
  printf '%s' "$(muts_on "$SF_PORT" | jq -r '.last')" | grep -q 'f0: clearProjectV2ItemFieldValue' \
    && ok "#448: an empty value emits clearProjectV2ItemFieldValue, not an empty update" \
    || bad "#448: empty value must clear" "doc: $(muts_on "$SF_PORT" | jq -r '.last')"
  kill "$SF_SRV" 2>/dev/null
fi

# 3. A VALUE MAY CONTAIN '='. Split on the FIRST one only, or `Contract=a=b` silently becomes a different
#    value than the caller asked for.
sfsrv -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field split fixture bound a port"; else
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --batch FS.GG.SDD#42 'Contract=a=b' >/dev/null 2>&1
  printf '%s' "$(muts_on "$SF_PORT" | jq -r '.last')" | grep -q 'text: "a=b"' \
    && ok "#448: Field=Value splits on the FIRST '=' (a value may legitimately contain one)" \
    || bad "#448: split must be on the first '='" "doc: $(muts_on "$SF_PORT" | jq -r '.last')"
  kill "$SF_SRV" 2>/dev/null
fi

# 4. A REFUSED PAIR SPENDS ZERO GRAPHQL — the same invariant the single write holds. An unknown FIELD and an
#    unknown single-select OPTION are DIFFERENT code paths, so each needs its own assertion: both must be
#    refused BEFORE a document is sent, or a bad pair caught late fails the batch AFTER earlier aliases landed.
sfsrv -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field refuse fixture bound a port"; else
  sfr() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@" 2>&1; }
  uf="$(sfr set-field --batch FS.GG.SDD#42 'No Such Field=x')"; ufrc=$?
  { [ "$ufrc" -eq 1 ] && [ "$(muts_on "$SF_PORT" | jq -r '.count')" = "0" ]; } \
    && ok "#448: an unknown field is refused (exit 1) and spends ZERO GraphQL" \
    || bad "#448: unknown field must refuse before sending" "rc=$ufrc count=$(muts_on "$SF_PORT" | jq -r '.count') out=$uf"
  uo="$(sfr set-field --batch FS.GG.SDD#42 'Phase=No Such Option')"; uorc=$?
  { [ "$uorc" -eq 1 ] && [ "$(muts_on "$SF_PORT" | jq -r '.count')" = "0" ]; } \
    && ok "#448: an unknown single-select OPTION is refused and spends ZERO GraphQL (the build aborts, not the value)" \
    || bad "#448: unknown option must refuse before sending" "rc=$uorc count=$(muts_on "$SF_PORT" | jq -r '.count') out=$uo"
  printf '%s' "$uo" | grep -q 'No Such Option' \
    && ok "#448: ...and the reason names the OPTION, not a GraphQL parse error" \
    || bad "#448: the refusal must name the option" "out: $uo"
  kill "$SF_SRV" 2>/dev/null
fi

# 5. THE PARTIAL ARM. Mutations run SERIALLY: when f1 fails, f0 has ALREADY been written. Reporting that as
#    a failure claims nothing happened; reporting it as success is the bug #448 forbade by name. It is its
#    own answer — EX_PARTIAL (4), naming what landed and what did not — and it is NEVER queued.
SFCACHE="$(mktemp -d)"
sfsrv SF_FAIL_ALIAS=f1 -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field partial fixture bound a port"; else
  pout="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$SFCACHE" "$ENGINE" set-field --batch FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=x' 2>&1)"; prc=$?
  [ "$prc" -eq 4 ] \
    && ok "#448: a per-alias failure is EX_PARTIAL (4), not success and not a generic error" \
    || bad "#448: partial must exit 4" "rc=$prc out=$pout"
  printf '%s' "$pout" | grep -q 'PARTIALLY APPLIED' && printf '%s' "$pout" | grep -q 'half-written' \
    && ok "#448: ...it says PARTIALLY APPLIED and that the board is half-written" \
    || bad "#448: partial must announce the half-written board" "out: $pout"
  printf '%s' "$pout" | grep -q "APPLIED  Phase='P2 SDD'" \
    && ok "#448: ...naming the field that WAS written (Phase)" \
    || bad "#448: partial must name the applied field" "out: $pout"
  printf '%s' "$pout" | grep -q 'FAILED   Target=' && printf '%s' "$pout" | grep -q 'stub: f1 rejected' \
    && ok "#448: ...and the field that FAILED (Target), carrying the API's own reason" \
    || bad "#448: partial must name the failed field and its reason" "out: $pout"
  [ ! -s "$SFCACHE/pending.jsonl" ] || [ "$(grep -c '#42' "$SFCACHE/pending.jsonl" 2>/dev/null || echo 0)" = "0" ] \
    && ok "#448: a PARTIAL batch is NEVER queued — replaying it would rewrite what already landed" \
    || bad "#448: partial must not queue" "pending: $(cat "$SFCACHE/pending.jsonl" 2>/dev/null)"
  kill "$SF_SRV" 2>/dev/null
fi
rm -rf "$SFCACHE"

# 6. AN EXHAUSTED BUDGET refuses the document OUTRIGHT — nothing is applied — so the whole batch is
#    deferrable and EVERY pair must land in the queue. This is the arm that must be tested BEFORE the partial
#    arm in the client: a rate limit that fell through to the partial reporter would describe a half-written
#    board that does not exist.
RLCACHE="$(mktemp -d)"
sfsrv SF_RATELIMIT=1 -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field rate-limit fixture bound a port"; else
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$RLCACHE" "$ENGINE" set-field --batch FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' >/dev/null 2>&1; rlrc=$?
  [ "$rlrc" -eq 75 ] \
    && ok "#448: an exhausted budget exits EX_RATE (75), the back-off signal — not a generic 1" \
    || bad "#448: rate-limited batch must exit 75" "rc=$rlrc"
  [ "$(grep -c '#42' "$RLCACHE/pending.jsonl" 2>/dev/null || echo 0)" = "2" ] \
    && ok "#448: ...and QUEUES every pair (nothing was applied, so nothing is lost)" \
    || bad "#448: rate-limited batch must queue both pairs" "pending: $(cat "$RLCACHE/pending.jsonl" 2>/dev/null)"
  kill "$SF_SRV" 2>/dev/null
fi
rm -rf "$RLCACHE"

echo
echo "coord-engine parity: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine parity FAILED"; exit 1; }
echo "green — the engine matches the corpus's certified answer, with no bash in the pipeline."

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
  # Org-wide (cross-repo) batch is what you get OUTSIDE a checkout: with no detectable git remote,
  # `scope_repo` yields nothing and the scan spans the whole org (#480). The corpus reaches this by
  # running from its non-checkout `$WORK`; mirror that with a scopeless CWD, so a bare `batch` here is
  # org-wide rather than scoped to `.github` (this harness's own checkout).
  XB_NOGIT="$(mktemp -d)"
  xbenv() { ( cd "$XB_NOGIT" && FSGG_GITHUB_API_BASE="http://127.0.0.1:$XBPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@" ); }
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

  # ...and it names the RIGHT one. This scan dies on the GraphQL POST (`items(first`), and the fixture
  # says so in `x-ratelimit-resource: graphql` — exactly as GitHub does.
  #
  # THIS ASSERTION IS THE ONE THAT WAS MISSING. `explain` hardcoded the word "GraphQL" for every rate
  # limit it ever rendered, so a grep for 'budget' — or even for 'GraphQL' — passed whether the engine
  # had read the resource or merely assumed it. The REST leg below is the other half: the same engine,
  # the same word, and it must come out DIFFERENT. Only the pair can tell reading from guessing.
  printf '%s' "$rlout" | grep -q 'GraphQL budget EXHAUSTED' \
    && ok "#418: ...and it names GRAPHQL — the budget this scan actually spent" \
    || bad "#418: a graphql-resource 403 must name the GraphQL budget" "$rlout"

  # ...and it names the RESET, read from `X-RateLimit-Reset`. `/pnext-item` §1 tells the worker to "back
  # off until the reset it names"; before this, a rate limit could not name one.
  printf '%s' "$rlout" | grep -q 'resets in ~' \
    && ok "#418: ...and it names the RESET from X-RateLimit-Reset, so the back-off is a time, not a shrug" \
    || bad "#418: the reset header must be read" "$rlout"

  # ---- THE REST LEG: the same engine, a `core` 403, and it must NOT say "GraphQL" ----
  #
  # Measured live on 2026-07-16: REST core sat at 0/5000 and 403'd every read while GraphQL had 3,639
  # points left, and the engine reported "GraphQL budget EXHAUSTED … REST-only work still runs" — naming
  # the healthy budget and then recommending the dead one.
  #
  # `issues` is the probe because it is REST-FIRST: `who`/`take` scan the board over GraphQL before they
  # touch a marker, so on this fixture they die on the POST leg and never reach the GET. `issues` is also
  # the command that matters most here — §4 sends every worker to it for dedupe precisely BECAUSE it is
  # REST, so it is the one that dies alone when REST is what went.
  issout="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$RLPORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" issues FS.GG.SDD 2>&1)"; issrc=$?

  # EX_RATE, not a generic 1. `issues` hand-rolled `explain` + `ExitError` instead of calling `fail`, so it
  # flattened a TEMPORARY back-off into the code a caller reads as a PERMANENT protocol error — in the one
  # command a worker reaches for when the other budget is gone.
  [ "$issrc" -eq 75 ] \
    && ok "REST budget: issues on an exhausted REST budget exits EX_RATE (75), not a generic 1" \
    || bad "REST budget: issues must exit 75 on a rate limit — a back-off is not a protocol error" "rc=$issrc: $issout"

  printf '%s' "$issout" | grep -q 'REST budget EXHAUSTED' \
    && ok "REST budget: ...and it names REST — the budget that ACTUALLY died" \
    || bad "a core-resource 403 must name the REST budget, not GraphQL" "$issout"

  # THE REGRESSION ITSELF. The old sentence recommended REST-only work at the exact moment REST was the
  # thing that had stopped — the tool pointing the worker at the one remedy that cannot work.
  printf '%s' "$issout" | grep -q 'REST-only work' \
    && bad "a REST limit must NEVER recommend REST-only work — that is the #266 regression" "$issout" \
    || ok "REST budget: ...and it does NOT recommend REST-only work on a REST limit"

  # THE PAIR IS THE PROOF. One fixture, two legs, two DIFFERENT budget names out of the same binary. A
  # hardcoded word cannot produce both, so this is what distinguishes reading the resource from assuming
  # it — and it is the assertion whose absence let "GraphQL" stand in for every limit for so long.
  printf '%s' "$rlout" | grep -q 'GraphQL budget EXHAUSTED' && printf '%s' "$issout" | grep -q 'REST budget EXHAUSTED' \
    && ok "REST budget: ...and the SAME engine names GraphQL vs REST per X-RateLimit-Resource (read, not guessed)" \
    || bad "the two legs must disagree — that is what proves the resource is read" "graphql-leg=$rlout || rest-leg=$issout"
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
    "$ENGINE" "done" 'FS.GG.SDD#42' --worker vole-533 --pr 7 --flip 2>&1)"; d1rc=$?
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
    "$ENGINE" "done" 'FS.GG.SDD#42' --worker vole-533 --pr 7 --flip 2>&1)"
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

  # #962 — EVERY DOCUMENTED `--repo` SPELLING NAMES ONE QUEUE. `--repo` takes a registry short-id, an
  # `owner/repo`, or a bare repo name (the skill's Setup section says so in as many words), so all three
  # must agree. `ready` resolved NONE of them: it compared the raw token against the row's repo verbatim,
  # so `--repo sdd` matched nothing and printed `[]` with exit 0 over a full board.
  #
  # NOTE WHAT MADE THIS CORPUS BLIND TO IT, because it is the reason the leg is written this way: every
  # `ready` leg above says `--repo FS.GG.SDD` — the BARE NAME, the one spelling a verbatim compare gets
  # right. `--repo .github` passes too, and is a COINCIDENCE, not a control: `.github` is the one repo
  # whose short-id and repo name are the same string. A corpus can be thorough about a flag's semantics
  # and never once ask whether its ARGUMENT is resolved. So these compare spellings against each other
  # rather than against a literal: the assertion is that they AGREE, which is the actual contract.
  rdy_bare="$(rdy --repo FS.GG.SDD --json 2>/dev/null | jq -c '[.[].number]|sort')"
  for spelling in sdd SDD FS-GG/FS.GG.SDD; do
    got="$(rdy --repo "$spelling" --json 2>/dev/null | jq -c '[.[].number]|sort')"
    [ "$got" = "$rdy_bare" ] \
      && ok "#962: ready --repo $spelling names the same queue as the bare repo name ($rdy_bare)" \
      || bad "#962: ready --repo $spelling must resolve to FS.GG.SDD" "expected $rdy_bare, got: $got"
  done

  # #962 — AND THE RECONCILER DEFAULT SURVIVES THE FIX. This is the half a careless repair breaks: the
  # obvious way to resolve `ready`'s `--repo` is to add `Ready` to the #480 scoping list, which ALSO hands
  # it the git-remote default — silently shrinking `/check-board`'s org-wide `ready --all` to whatever
  # checkout it runs in, i.e. trading this bug for a strictly worse one in the tool that exists to catch
  # it. A bare `ready` must still span every repo on the board.
  bare_repos="$(rdy --all --json 2>/dev/null | jq -c '[.[].repo] | unique | length')"
  [ "${bare_repos:-0}" -gt 1 ] \
    && ok "#962: a bare 'ready --all' stays ORG-WIDE ($bare_repos repos) — resolution did not import the #480 checkout default" \
    || bad "#962: a bare ready must reconcile the WHOLE board, not one repo" "distinct repos: ${bare_repos:-unknown}"

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
deletes_on() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_deletes").read().decode())' "$1" 2>/dev/null; }
patches_on() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_patches").read().decode())' "$1" 2>/dev/null; }

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
  b1="$(sfenv set-field --batch --worker sf-448 FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=fs-gg-ui-template' 2>&1)"; b1rc=$?
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
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --batch --worker sf-448 FS.GG.SDD#42 'Contract=' >/dev/null 2>&1
  printf '%s' "$(muts_on "$SF_PORT" | jq -r '.last')" | grep -q 'f0: clearProjectV2ItemFieldValue' \
    && ok "#448: an empty value emits clearProjectV2ItemFieldValue, not an empty update" \
    || bad "#448: empty value must clear" "doc: $(muts_on "$SF_PORT" | jq -r '.last')"
  kill "$SF_SRV" 2>/dev/null
fi

# 3. A VALUE MAY CONTAIN '='. Split on the FIRST one only, or `Contract=a=b` silently becomes a different
#    value than the caller asked for.
sfsrv -- set-field
if [ -z "$SF_PORT" ]; then bad "set-field split fixture bound a port"; else
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --batch --worker sf-448 FS.GG.SDD#42 'Contract=a=b' >/dev/null 2>&1
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
  uf="$(sfr set-field --batch --worker sf-448 FS.GG.SDD#42 'No Such Field=x')"; ufrc=$?
  { [ "$ufrc" -eq 1 ] && [ "$(muts_on "$SF_PORT" | jq -r '.count')" = "0" ]; } \
    && ok "#448: an unknown field is refused (exit 1) and spends ZERO GraphQL" \
    || bad "#448: unknown field must refuse before sending" "rc=$ufrc count=$(muts_on "$SF_PORT" | jq -r '.count') out=$uf"
  uo="$(sfr set-field --batch --worker sf-448 FS.GG.SDD#42 'Phase=No Such Option')"; uorc=$?
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
  pout="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$SFCACHE" "$ENGINE" set-field --batch --worker sf-448 FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' 'Contract=x' 2>&1)"; prc=$?
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
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$SF_PORT" FSGG_COORD_CACHE="$RLCACHE" "$ENGINE" set-field --batch --worker sf-448 FS.GG.SDD#42 'Phase=P2 SDD' 'Target=2026-08-01' >/dev/null 2>&1; rlrc=$?
  [ "$rlrc" -eq 75 ] \
    && ok "#448: an exhausted budget exits EX_RATE (75), the back-off signal — not a generic 1" \
    || bad "#448: rate-limited batch must exit 75" "rc=$rlrc"
  [ "$(grep -c '#42' "$RLCACHE/pending.jsonl" 2>/dev/null || echo 0)" = "2" ] \
    && ok "#448: ...and QUEUES every pair (nothing was applied, so nothing is lost)" \
    || bad "#448: rate-limited batch must queue both pairs" "pending: $(cat "$RLCACHE/pending.jsonl" 2>/dev/null)"
  kill "$SF_SRV" 2>/dev/null
fi
rm -rf "$RLCACHE"

# ---- CLAIM RECORDS THE COLUMN IT OVERWRITES (case 21): #481 ---------------------------------------
#
# The corpus (`tests/fsgg-coord/cases/21-claim-restores-column.sh`, #481) certifies that undoing a claim
# RESTORES the board column the claim overwrote — it does not guess `Ready`. The column is knowable at
# exactly one instant, before the `In progress` write erases it, so the claim reads it and records it in
# its own marker (`prev=<column>`); `release` reads that back and puts the column where it was.
#
# This was a PORT GAP, not a divergence. The engine's marker machinery — `prev=` encode/decode, the release
# restore, the heartbeat carry-forward — all EXISTED, but `Client.claim` passed `None` for the pre-claim
# column: the originating read was stubbed out, so a fresh claim recorded nothing and every release fell
# back to `Ready`. This slice lands with the fix (`Board.itemStatus` — the `fieldValueByName` resolver read
# — wired into `claim`), and holds the engine to the corpus's answer over HTTP: `restore_server.py` records
# every posted/patched marker body so the `prev=` key can be read back, records which Status OPTION each
# board write carried (bash asserts `opt_backlog`/`opt_review` in its `GH_LOG`), and counts GraphQL by
# category so the cost pin can be re-expressed at the HTTP layer.
#
# The corpus's cost assertion (§j, "a claim spends 2 GraphQL reads") is BASH's absolute count under its own
# board-metadata caching; the engine's bootstrap/itemId read plan differs, so this re-expresses the property
# #481 actually protects (ADR-0040 §5): the pre-claim column is exactly ONE item-scoped read, spent ONLY on
# the winning post path — a lost race pays ZERO — and it is NEVER the seven-point board SCAN that #418
# forbids on this, the hottest path in the org.
#
# DISPOSED ON THE RECORD, not silently skipped (the engine lacks the surface, or the leg is a distinct
# defect from #481):
#   • §d/§e (`reap` restores / re-claim-over-dead-lease inherits) — the engine has no `reap` command yet
#     (the same disposition as case 41 §3's `lint`); the marker-inheritance path they exercise is covered
#     at the CAS by the live re-claim, which returns the existing marker's recorded column.
#   • §f (a `--force` steal inherits the EVICTED claim's column) — the engine's `--force` overrides the #516
#     self-hold check; it does not evict another worker's LIVE lease, so there is no steal to inherit
#     through. That is a separate matter from #481.
#   (§i/§h2 — #331's preserve half — were disposed here for the life of the port and are now PORTED; they
#   live below as (i)/(i2)/(i3)/(i4). The disposition said "#481 changes what release restores TO, not
#   WHETHER it first reads the live column", which was true and was also the whole defect: the recipe
#   promised the preserve behaviour throughout, so the corpus knew, the doc asserted, and nothing
#   connected the two (#911).)
#   • the human wording (`board: Backlog`, `restored`) is asserted here at the HTTP layer (the board write's
#     option id), not on stdout — the engine's `release` reports `released <ref> → <column>`, which names the
#     column but not in bash's words (#867 added the column; the silent `released <ref>` is what let the
#     ignored `--status` look like it had worked).
rsrv() {  # rsrv <env-kv...> --  ; sets globals RS_PORT and RS_SRV for a FRESH restore fixture
  local envs=() ; while [ "$1" != "--" ]; do envs+=("$1"); shift; done; shift
  local out; out="$(mktemp)"
  env ${envs[@]+"${envs[@]}"} python3 "$HERE/restore_server.py" >"$out" 2>/dev/null &
  local srv=$! port=""
  for _ in $(seq 1 50); do port="$(head -n1 "$out" 2>/dev/null)"; [ -n "$port" ] && break; sleep 0.1; done
  rm -f "$out"; RS_PORT="$port"; RS_SRV="$srv"
}
rget() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+sys.argv[2]).read().decode())' "$1" "$2" 2>/dev/null; }
renv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$RS_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
rbodies() { rget "$RS_PORT" "/repos/FS-GG/FS.GG.SDD/issues/$1/comments" | jq -r '.[].body'; }
rlastopt() { rget "$RS_PORT" /_writes | jq -r '.last.optionId'; }

# (a) THE DEFECT. A claim over a Backlog item records `prev=Backlog`; release must put Backlog back, not Ready.
rsrv FSGG_PARITY_STATUS=Backlog --
if [ -z "$RS_PORT" ]; then bad "restore fixture (a) bound a port"; else
  renv claim FS.GG.SDD#350 --force --worker pika-r01 >/dev/null 2>&1; crc=$?
  rbodies 350 | grep -q 'prev=Backlog' \
    && ok "#481: the claim RECORDS the column it overwrote (prev=Backlog) in its own marker, over HTTP" \
    || bad "#481: the claim must record prev=Backlog" "rc=$crc bodies=$(rbodies 350)"
  [ "$(rget "$RS_PORT" /_gql | jq -r '.itemStatus')" = "1" ] \
    && ok "#481/#418: a winning claim spends EXACTLY ONE item-scoped Status read (fieldValueByName), not a board scan" \
    || bad "#481: the pre-claim read must be exactly one item read" "gql=$(rget "$RS_PORT" /_gql)"
  renv release FS.GG.SDD#350 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_backlog" ] \
    && ok "#481: release RESTORES Backlog (writes opt_backlog), instead of promoting it to Ready" \
    || bad "#481: release must restore Backlog" "last write=$(rlastopt)"
  [ -z "$(rbodies 350)" ] \
    && ok "#481: ...and the lease is dropped (the marker is deleted)" \
    || bad "#481: release must drop the marker" "bodies=$(rbodies 350)"
  kill "$RS_SRV" 2>/dev/null
fi

# (b) A Status name with a SPACE must survive the round trip — percent-encoded in the marker, decoded back
#     to the real column on release. An encoding that lost at the space would truncate `In review` to `In`.
rsrv FSGG_PARITY_STATUS="In review" --
if [ -z "$RS_PORT" ]; then bad "restore fixture (b) bound a port"; else
  renv claim FS.GG.SDD#351 --force --worker pika-r01 >/dev/null 2>&1
  rbodies 351 | grep -q 'prev=In%20review' \
    && ok "#481: a Status with a space is percent-encoded into the marker (prev=In%20review)" \
    || bad "#481: the space must be percent-encoded" "bodies=$(rbodies 351)"
  renv release FS.GG.SDD#351 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_review" ] \
    && ok "#481: ...and decodes back to the real column, resolving opt_review (not a no-op, not 'In')" \
    || bad "#481: release must restore In review" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (c) A heartbeat REWRITES the whole marker body; anything it does not carry forward is destroyed. A claim
#     that beats for hours must still know the column it overwrote.
rsrv FSGG_PARITY_STATUS=Backlog --
if [ -z "$RS_PORT" ]; then bad "restore fixture (c) bound a port"; else
  renv claim FS.GG.SDD#352 --force --worker pika-r01 >/dev/null 2>&1
  renv heartbeat FS.GG.SDD#352 --worker pika-r01 >/dev/null 2>&1
  rbodies 352 | grep -q 'prev=Backlog' \
    && ok "#481: a heartbeat rewrites the marker and CARRIES the recorded column forward (prev=Backlog)" \
    || bad "#481: heartbeat must carry prev= forward" "bodies=$(rbodies 352)"
  renv release FS.GG.SDD#352 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_backlog" ] \
    && ok "#481: ...so a long-running claim still restores Backlog" \
    || bad "#481: release after heartbeat must restore Backlog" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (g) BACKWARD COMPATIBILITY. A marker minted before #481 carries no `prev=` key. It must keep releasing to
#     `Ready` — the old behaviour, now scoped to the one case where there is genuinely nothing to restore.
#
#     THE LIVE COLUMN IS SEEDED `In progress`, and that is the premise, not a detail. The recorded-column
#     fallback is only REACHABLE when the live column is the claim's own footprint — any other live column is
#     preserved by (i) below, whatever the marker recorded. This leg seeded the fixture's default (`Backlog`)
#     until #331 landed the live read, which made it assert `Ready` for a world where bash preserves
#     `Backlog`: a held item sitting in `Backlog` is a worker's deliberate park, not a claim to undo. The
#     assertion is unchanged; the world it runs in is now one the board can actually produce.
rsrv FSGG_PARITY_STATUS='In progress' 'FSGG_PARITY_MARKERS=[{"n":356,"id":856,"worker":"pika-r01"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (g) bound a port"; else
  renv release FS.GG.SDD#356 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_ready" ] \
    && ok "#481: a marker minted BEFORE #481 (no prev=) still falls back to Ready (opt_ready)" \
    || bad "#481: a pre-#481 marker must restore Ready" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (h) A claim that recorded `In progress` recorded its OWN footprint, not a column anybody chose. Restoring
#     it would leave the item looking claimed with no claim on it — so it, too, falls back to Ready.
#     The live column is seeded `In progress` for (g)'s reason: it is the only world where the recorded
#     column is consulted at all.
rsrv FSGG_PARITY_STATUS='In progress' 'FSGG_PARITY_MARKERS=[{"n":357,"id":857,"worker":"pika-r01","prev":"In%20progress"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (h) bound a port"; else
  renv release FS.GG.SDD#357 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_ready" ] \
    && ok "#481: a recorded 'In progress' is a footprint, not a column — release falls back to Ready" \
    || bad "#481: a recorded In progress must not be restored" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (k) #867 — `release --status S` LANDS THE COLUMN THE CALLER NAMES, beating the recorded restore.
#     THE REGRESSION GUARD, and the reason it is here rather than in a unit test: the flag PARSED all along
#     (`OptionsTests` was green on it for the whole life of the port) and `release` simply never read
#     `opts.Status`. So the only assertion that can catch a re-drop is one that watches the BOARD WRITE —
#     exactly what this fixture already records for #481. #867's own body says "nothing was positioned to
#     notice"; this is the thing positioned to notice.
#
#     The claim records prev=Backlog, so a `release` that ignores `--status` writes opt_backlog — which is
#     precisely the bug: it looked like a correct #481 restore, and exited 0.
rsrv FSGG_PARITY_STATUS=Backlog --
if [ -z "$RS_PORT" ]; then bad "restore fixture (k) bound a port"; else
  renv claim FS.GG.SDD#358 --force --worker pika-r01 >/dev/null 2>&1
  rout="$(renv release FS.GG.SDD#358 --worker pika-r01 --status Blocked 2>/dev/null)"; krc=$?
  [ "$(rlastopt)" = "opt_blocked" ] \
    && ok "#867: release --status Blocked writes opt_blocked — the named column BEATS the recorded restore (prev=Backlog)" \
    || bad "#867: release --status must land the named column" "last write=$(rlastopt) rc=$krc"
  printf '%s' "$rout" | grep -q 'released FS.GG.SDD#358 → Blocked' \
    && ok "#867: ...and stdout NAMES the column it landed in (the bare 'released <ref>' is what hid the no-op)" \
    || bad "#867: release must name the column on stdout" "stdout=$rout"
  [ -z "$(rbodies 358)" ] \
    && ok "#867: ...and the lease is still dropped (the marker is deleted)" \
    || bad "#867: release --status must still drop the marker" "bodies=$(rbodies 358)"
  kill "$RS_SRV" 2>/dev/null
fi

# (k2) #867 — an UNKNOWN column is refused BEFORE the marker is dropped. Order is the property: validate
#      after the release and a typo costs the caller their lock AND the column, leaving an item nobody holds
#      and nobody parked. A refused write spends no GraphQL and drops no lease.
rsrv FSGG_PARITY_STATUS=Backlog --
if [ -z "$RS_PORT" ]; then bad "restore fixture (k2) bound a port"; else
  renv claim FS.GG.SDD#359 --force --worker pika-r01 >/dev/null 2>&1
  renv release FS.GG.SDD#359 --worker pika-r01 --status Blocke >/dev/null 2>&1; brc=$?
  [ "$brc" -ne 0 ] \
    && ok "#867: release --status with an unknown column is REFUSED (non-zero), not silently defaulted" \
    || bad "#867: an unknown --status must be refused" "rc=$brc"
  rbodies 359 | grep -q 'fsgg:claim' \
    && ok "#867: ...and the lock is STILL HELD — the refusal lands before the marker is dropped" \
    || bad "#867: a refused --status must not drop the lease" "bodies=$(rbodies 359)"
  kill "$RS_SRV" 2>/dev/null
fi

# (i) #331 — THE DEFECT, AND IT IS THE RECIPE'S OWN PRESCRIBED SEQUENCE. A worker hits a blocker and parks
#     the item (`set-field Status Blocked`), then releases — pnext-item §4's blocked-item fence, verbatim.
#     `release` must PRESERVE the Blocked, because a column set DURING the lease was chosen deliberately and
#     is not the claim's to undo.
#
#     THE ASSERTION IS THE ABSENCE OF A WRITE, and that is the whole design (the changelog's "with no Status
#     write at all rather than a redundant matching one"). A `release` that wrote `opt_blocked` here would
#     reach the same end state and be indistinguishable from one that preserved — so the observable has to be
#     that `release` added nothing after the worker's own write. Pre-fix, the trailing write is `opt_ready`:
#     the deliberate Blocked reverted, reported as `released → Ready`, exit 0. Verified to fire.
rsrv FSGG_PARITY_STATUS=Ready --
if [ -z "$RS_PORT" ]; then bad "restore fixture (i) bound a port"; else
  renv claim FS.GG.SDD#374 --force --worker pika-r01 >/dev/null 2>&1
  renv set-field FS.GG.SDD#374 Status Blocked --worker pika-r01 >/dev/null 2>&1
  iout="$(renv release FS.GG.SDD#374 --worker pika-r01 2>/dev/null)"
  [ "$(rget "$RS_PORT" /_writes | jq -r '[.writes[].optionId] | join(",")')" = "opt_wip,opt_blocked" ] \
    && ok "#331: a column set DURING the lease is PRESERVED — release adds NO Status write (not even a matching one)" \
    || bad "#331: release must not write over a column chosen during the lease" "writes=$(rget "$RS_PORT" /_writes | jq -c '[.writes[].optionId]')"
  printf '%s' "$iout" | grep -q 'column left at Blocked' \
    && ok "#331: ...and stdout says the column was LEFT, never claiming release put it there" \
    || bad "#331: release must report the preserved column honestly" "stdout=$iout"
  [ -z "$(rbodies 374)" ] \
    && ok "#331: ...and the lease is still dropped (the marker is deleted)" \
    || bad "#331: a preserving release must still drop the marker" "bodies=$(rbodies 374)"
  kill "$RS_SRV" 2>/dev/null
fi

# (i2) #331/#266 — A COLUMN WE COULD NOT READ IS NOT A COLUMN WE MAY OVERWRITE. The read fails (502); the
#      lease must still drop, the column must be left ALONE, and the failure must be SAID.
#
#      This is the fail-CLOSED arm, and it is the one the obvious implementation gets wrong in whichever
#      direction it guesses: read-compare-write with an unreadable read either preserves blindly (leaving a
#      dead claim's `In progress` on the board forever) or reverts blindly (#331 again, on a transient 502).
#      Neither is knowledge. The marker records `prev=Backlog`, so a release that fell back to the recorded
#      column on a failed read would write `opt_backlog` — which would look exactly like a correct #481
#      restore.
rsrv FSGG_PARITY_FAIL_STATUS=1 'FSGG_PARITY_MARKERS=[{"n":375,"id":875,"worker":"pika-r01","prev":"Backlog"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (i2) bound a port"; else
  # No rc captured: this leg's contract is the WRITES (none), the marker (dropped), and the message —
  # asserted below. `release` exits 0 on an unreadable column by design (#914), so an rc here would
  # assert nothing. (#648)
  i2out="$(renv release FS.GG.SDD#375 --worker pika-r01 2>&1)"
  [ "$(rget "$RS_PORT" /_writes | jq -r '.count')" = "0" ] \
    && ok "#331/#266: an UNREADABLE column is left ALONE — release writes nothing rather than guessing" \
    || bad "#331: a failed column read must not be written over" "writes=$(rget "$RS_PORT" /_writes | jq -c '[.writes[].optionId]')"
  [ -z "$(rbodies 375)" ] \
    && ok "#331: ...and the lease is STILL dropped — a board we cannot read never strands a lock" \
    || bad "#331: an unreadable column must not strand the lease" "bodies=$(rbodies 375)"
  printf '%s' "$i2out" | grep -q 'could not be read' \
    && ok "#331: ...and it SAYS so, naming the repair (a silent leave-alone is indistinguishable from a preserve)" \
    || bad "#331: an unreadable column must be reported" "out=$i2out"
  kill "$RS_SRV" 2>/dev/null
fi

# (i2b) #331 — NO COLUMN TO RESET IS SAID, NOT SWALLOWED. The item is on the board with NO `Status` set
#       (`fieldValueByName` null), which `itemStatus` answers `Ok None` — the same answer it gives for an
#       item that is not on this board at all. Either way there is nothing to reset and nothing to preserve,
#       so no write is correct; being SILENT about it is not. A bare `released <ref>` is this recipe's
#       documented tell for "the column did NOT land — stderr says why", so emitting one with an empty
#       stderr raises that alarm with nothing behind it, and drops the plain "not an item on this board"
#       the pre-#331 write path reported.
rsrv FSGG_PARITY_STATUS= 'FSGG_PARITY_MARKERS=[{"n":378,"id":878,"worker":"pika-r01","prev":"Backlog"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (i2b) bound a port"; else
  i2bout="$(renv release FS.GG.SDD#378 --worker pika-r01 2>/dev/null)"
  [ "$(rget "$RS_PORT" /_writes | jq -r '.count')" = "0" ] \
    && ok "#331: an item with NO column set gets no write — there is no footprint to reset" \
    || bad "#331: a no-column item must not be written" "writes=$(rget "$RS_PORT" /_writes | jq -c '[.writes[].optionId]')"
  printf '%s' "$i2bout" | grep -q 'no column to reset' \
    && ok "#331: ...and release SAYS so, rather than a bare 'released <ref>' that reads as a failed write" \
    || bad "#331: a no-column release must not be silent" "stdout=$i2bout"
  kill "$RS_SRV" 2>/dev/null
fi

# (i3) #331/#418 — `--status S` SPENDS NO LIVE READ. The read exists ONLY to derive the default; a caller who
#      states the end state has left no default to derive. Cheap to get wrong (read first, then notice the
#      flag), and it would put a GraphQL point on the budget that dies first, per release, for an answer
#      nothing consults.
rsrv FSGG_PARITY_STATUS=Backlog --
if [ -z "$RS_PORT" ]; then bad "restore fixture (i3) bound a port"; else
  renv claim FS.GG.SDD#376 --force --worker pika-r01 >/dev/null 2>&1
  base="$(rget "$RS_PORT" /_gql | jq -r '.itemStatus')"   # the claim's own pre-claim read
  renv release FS.GG.SDD#376 --worker pika-r01 --status Blocked >/dev/null 2>&1
  [ "$(rget "$RS_PORT" /_gql | jq -r '.itemStatus')" = "$base" ] \
    && ok "#331/#418: release --status spends ZERO item-Status reads — the caller stated the end state, so no default is derived" \
    || bad "#331: --status must not pay the live read" "before=$base after=$(rget "$RS_PORT" /_gql | jq -r '.itemStatus')"
  [ "$(rlastopt)" = "opt_blocked" ] \
    && ok "#331/#867: ...and it still lands the named column, beating the live column AND the recorded one" \
    || bad "#867: --status must land the named column" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (i4) #331 — THE SAME QUESTION IN `reap`. bash closed this split by making both verbs ask ONE question
#      (`unclaim_status`); the port re-opened it by giving each its own marker-only copy. A reaper collects a
#      LEASE and knows nothing about whether the item became startable — so a worker whose lease lapsed on an
#      item it had deliberately marked `Blocked` had that column reset on its way out. That is #331 with a
#      dead worker instead of a live one, and a fix landing only in `release` leaves it live.
#
#      The marker is stale (age 3h > the 120m lease) and has no PR, so #581's proof-of-life gate passes it to
#      the delete. It records `prev=Ready`, so a reap that consulted the marker alone writes `opt_ready`.
rsrv FSGG_PARITY_STATUS=Blocked 'FSGG_PARITY_MARKERS=[{"n":377,"id":877,"worker":"pika-r01","prev":"Ready","age_hours":-3}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (i4) bound a port"; else
  i4out="$(renv reap --repo FS.GG.SDD --apply --worker pika-r02 2>&1)"
  [ "$(rget "$RS_PORT" /_writes | jq -r '.count')" = "0" ] \
    && ok "#331: reap PRESERVES a column chosen during the lease — it collects a lease, not a decision" \
    || bad "#331: reap must not reset a deliberate column" "writes=$(rget "$RS_PORT" /_writes | jq -c '[.writes[].optionId]') out=$i4out"
  [ -z "$(rbodies 377)" ] \
    && ok "#331: ...and the stale marker is still collected (the lock is broken; only the column is spared)" \
    || bad "#331: reap must still break the stale lock" "bodies=$(rbodies 377)"
  kill "$RS_SRV" 2>/dev/null
fi

# (j') THE #418 PROPERTY, re-expressed. The pre-claim read must sit on the WINNING post path only: a claim
#      that loses the CAS to a live holder must spend ZERO item-Status reads — otherwise every losing `take`
#      round would pay a GraphQL point on the budget that dies first under fan-out.
rsrv 'FSGG_PARITY_MARKERS=[{"n":360,"id":860,"worker":"finch-a3f"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (j') bound a port"; else
  renv claim FS.GG.SDD#360 --force --worker pika-r01 >/dev/null 2>&1; lrc=$?
  { [ "$lrc" -ne 0 ] && [ "$(rget "$RS_PORT" /_gql | jq -r '.itemStatus')" = "0" ]; } \
    && ok "#418: a claim that LOSES to a live holder spends ZERO pre-claim reads — the read is on the win path only" \
    || bad "#418: a losing claim must not pay the pre-claim read" "rc=$lrc gql=$(rget "$RS_PORT" /_gql)"
  kill "$RS_SRV" 2>/dev/null
fi

# ==================================================================================================
# CASE 13 (#480): a WORKER command scopes to the repo you are STANDING IN; a reconciler stays org-wide
# ==================================================================================================
# The corpus certifies that `next`/`take`/`batch`/`who` default to the current checkout — resolved from
# the git remote, FREE and offline (never `gh repo view`, so it cannot burn budget or misread an
# exhausted one as "not in a checkout", #430) — while `ready`/`lint` stay org-wide RECONCILERS that
# /check-board runs BARE over the whole board. bash's own bug was the opposite: a bare command
# initialised `repo=""`, which every board read treats as the whole org, so a `.github` worker was
# handed another repo's item and a worktree command against the WRONG `origin`. This drives the ENGINE
# from FAKE CHECKOUTS (temp dirs whose `origin` names one repo) against a small multi-repo board, over
# HTTP — the scope resolved from `git config remote.origin.url`, exactly as bash resolves it, one
# transport under. Items are Ready (not Backlog) so the property under test is SCOPE, not the engine's
# `--include-backlog` divergence (case 41 §4). The board's three repos make a leak visible: a bare SDD
# scope that picked Templates or Game would be the org-wide default #480 deletes.
SC_OUT="$(mktemp)"; SC_CACHE="$(mktemp -d)"; CO="$(mktemp -d)"
python3 "$HERE/scope_server.py" >"$SC_OUT" 2>/dev/null &
SC_SRV=$!
SC_PORT=""; for _ in $(seq 1 50); do SC_PORT="$(head -n1 "$SC_OUT" 2>/dev/null)"; [ -n "$SC_PORT" ] && break; sleep 0.1; done
if [ -z "$SC_PORT" ]; then
  bad "#480: scope fixture bound a port"
else
  # Fake checkouts — the git remote is the ONLY signal `scope_repo` reads. Two URL forms (https, ssh)
  # prove the parser handles both; `nogit` is deliberately NOT a git repo (an undetectable scope).
  mkdir -p "$CO/sdd"  && git -C "$CO/sdd"  init -q && git -C "$CO/sdd"  remote add origin https://github.com/FS-GG/FS.GG.SDD.git
  mkdir -p "$CO/tmpl" && git -C "$CO/tmpl" init -q && git -C "$CO/tmpl" remote add origin git@github.com:FS-GG/FS.GG.Templates.git
  mkdir -p "$CO/nogit"
  # A per-invocation runner: from a chosen directory, pointed at the scope server with a fresh cache.
  scoped() { local dir="$1"; shift; ( cd "$dir" \
      && FSGG_GITHUB_API_BASE="http://127.0.0.1:$SC_PORT" FSGG_COORD_CACHE="$SC_CACHE" \
         FSGG_COORD_SCAN_TTL_SEC=0 "$ENGINE" "$@" ); }

  # (1) A bare worker command takes the checkout's repo — and ONLY that repo.
  n_sdd="$(scoped "$CO/sdd" next 2>/dev/null)"
  [ "$n_sdd" = "FS.GG.SDD#127" ] \
    && ok "#480: a bare 'next' from an FS.GG.SDD checkout picks THAT repo's item (FS.GG.SDD#127)" \
    || bad "#480: bare next scopes to the checkout" "expected FS.GG.SDD#127, got: $n_sdd"
  case "$n_sdd" in
    *Templates*|*Game*) bad "#480: a bare SDD-checkout next must not reach another repo" "$n_sdd" ;;
    *) ok "#480: ...and never reaches into Templates or Game — the org-wide default is gone" ;;
  esac

  # (2) An explicit --repo SPELLS OUT the scope and wins over the checkout — and a registry short-id
  #     (`templates`) resolves to the repo name board rows carry, exactly as bash's resolve_repo maps it.
  n_expl="$(scoped "$CO/sdd" next --repo templates 2>/dev/null)"
  [ "$n_expl" = "FS.GG.Templates#99" ] \
    && ok "#480: an explicit '--repo templates' wins over the SDD checkout AND resolves the short-id (#381)" \
    || bad "#480: explicit --repo wins + short-id resolves" "expected FS.GG.Templates#99, got: $n_expl"

  # (3) The default is READ from the remote, not hard-wired: the same bare command in a Templates
  #     checkout picks Templates. (An ssh-form origin, so the parser is exercised on both URL shapes.)
  n_tmpl="$(scoped "$CO/tmpl" next 2>/dev/null)"
  [ "$n_tmpl" = "FS.GG.Templates#99" ] \
    && ok "#480: a bare 'next' from a Templates checkout (ssh remote) picks Templates#99 — the scope is the remote" \
    || bad "#480: bare next reads the actual remote" "expected FS.GG.Templates#99, got: $n_tmpl"

  # (4) `batch` — the engine `take` schedules from — scopes the same way, so the two cannot disagree.
  b_sdd="$(scoped "$CO/sdd" batch --json 2>/dev/null)"
  [ "$b_sdd" = '["FS.GG.SDD#127"]' ] \
    && ok "#480: a bare 'batch --json' from the SDD checkout schedules only SDD's item" \
    || bad "#480: bare batch scopes to the checkout" "expected [\"FS.GG.SDD#127\"], got: $b_sdd"

  # (5) `take` ACTS, so an UNDETECTABLE scope is a hard error — never a quiet widen to the whole org,
  #     which is the failure that handed a `.github` worker another repo's item. The refusal precedes
  #     any network read (no budget spent), so it needs no board at all.
  t_nogit="$(scoped "$CO/nogit" take 2>&1)"; t_rc=$?
  { printf '%s' "$t_nogit" | grep -q -- '--repo required' && [ "$t_rc" -ne 0 ]; } \
    && ok "#480: 'take' outside a checkout REFUSES ('--repo required'), never scans the whole org" \
    || bad "#480: take must refuse an undetectable scope" "rc=$t_rc: $t_nogit"

  # (6) THE REGRESSION GUARD. `ready` is an org-wide RECONCILER — /check-board runs a bare
  #     `ready --all --json` to reconcile the WHOLE board. Defaulting it to the checkout would silently
  #     shrink the reconciler to one repo, trading this scope bug for a strictly worse one. It must stay
  #     org-wide even from inside a checkout.
  r_repos="$(scoped "$CO/sdd" ready --all --json 2>/dev/null | jq -r '[.[].repo] | unique | length')"
  [ "$r_repos" = "3" ] \
    && ok "#480: a bare 'ready --all' from a checkout stays ORG-WIDE (all 3 repos) — /check-board depends on it" \
    || bad "#480: ready must not be scoped to the checkout" "expected 3 repos, got: $r_repos"

  # (7) An unknown --repo has nothing schedulable — and, crucially, does NOT fall back to another repo's
  #     queue. bash prints "no startable item"; the engine re-expresses #440 (case 41 §4) as the honest
  #     "nothing schedulable right now." with NO item ref — the same property, the engine's own words.
  n_nope="$(scoped "$CO/sdd" next --repo nope 2>/dev/null)"
  { printf '%s' "$n_nope" | grep -q 'nothing schedulable' && ! printf '%s' "$n_nope" | grep -qE '#[0-9]'; } \
    && ok "#480: 'next --repo nope' reports nothing schedulable and names no item — never another repo's queue" \
    || bad "#480: an unknown repo must not borrow another's queue" "$n_nope"

  kill "$SC_SRV" 2>/dev/null
fi
rm -f "$SC_OUT"; rm -rf "$SC_CACHE" "$CO"
# ---- case 13: the `Blocked by` WRITE gate — a typed dependency edge, not a resolution log ------------
#
# Projects v2 has no dependency field, so `Blocked by` is TEXT. In bash it drifted back into a free-form
# LOG ("RESOLVED: #8 closed, shipped @d80a8ae"), and `.blocked` — which reads the field back as refs —
# could not parse it, so an item the board displayed as blocked reached the scheduler UNBLOCKED. The gate
# is on the WRITE: `set-field <issue> 'Blocked by' <value>` canonicalizes every accepted form
# (owner/repo#n, repo#n, a bare #n adopting the item's own repo, an issue URL) to one `owner/repo#n`,
# de-dupes refs that canonicalize alike, and REFUSES prose — before any board read, so a refused value
# spends no GraphQL (the budget that dies first). The corpus (case 13 lines 153-243) counts `gh`; this
# drives the ENGINE over HTTP against `blockedby_server.py`, which records each field mutation (the field,
# whether it SET or CLEARED, the text it carried — mapped from the `fieldId` variable) and counts the
# GraphQL requests, so "a refused write spends no GraphQL" is a request count of ZERO. The `--text`
# wording is the API's; parity holds the PROPERTY (the canonical value the mutation carries), one transport
# under the corpus's `gh` log.
BB_OUT="$(mktemp)"; python3 "$HERE/blockedby_server.py" >"$BB_OUT" 2>/dev/null & BB_SRV=$!; BB_PORT=""
for _ in $(seq 1 50); do BB_PORT="$(head -n1 "$BB_OUT" 2>/dev/null)"; [ -n "$BB_PORT" ] && break; sleep 0.1; done
rm -f "$BB_OUT"
bbget() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+sys.argv[2]).read().decode())' "$1" "$2" 2>/dev/null; }
if [ -z "$BB_PORT" ]; then bad "#480: Blocked by fixture bound a port"; else
  BBCACHE="$(mktemp -d)"
  # The WRITE path shares one cache — the first write warms bootstrap, each records its mutation.
  bbw() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$BBCACHE" \
              "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' "$1" 2>&1; }
  bblast() { bbget "$BB_PORT" /_writes | jq -r "$1"; }

  # 1. A full ref passes through, canonical.
  bbw 'FS-GG/FS.GG.SDD#8' >/dev/null
  [ "$(bblast '.last | "\(.op) \(.field) \(.text)"')" = "set Blocked by FS-GG/FS.GG.SDD#8" ] \
    && ok "Blocked by: a full owner/repo#n ref writes as-is (canonical --text, one transport over)" \
    || bad "Blocked by: full ref passthrough" "$(bblast '.last')"

  # 2. A bare #n adopts the BLOCKED item's own repo (SDD#42 -> FS-GG/FS.GG.SDD#33).
  bbw '#33' >/dev/null
  [ "$(bblast '.last.text')" = "FS-GG/FS.GG.SDD#33" ] \
    && ok "Blocked by: a bare #n adopts the blocked item's repo (#33 -> FS-GG/FS.GG.SDD#33)" \
    || bad "Blocked by: bare #n adoption" "$(bblast '.last.text')"

  # 3. A LIST canonicalizes every form — a repo#n and an issue URL, in order.
  bbw 'FS.GG.Rendering#33 , https://github.com/FS-GG/FS.GG.Templates/issues/8' >/dev/null
  [ "$(bblast '.last.text')" = "FS-GG/FS.GG.Rendering#33, FS-GG/FS.GG.Templates#8" ] \
    && ok "Blocked by: a list canonicalizes EVERY form (repo#n + URL), in order" \
    || bad "Blocked by: list canonicalization" "$(bblast '.last.text')"

  # 4. Refs that canonicalize alike are DE-DUPED — one edge, not two.
  bbw '#8, FS-GG/FS.GG.SDD#8' >/dev/null
  [ "$(bblast '.last.text')" = "FS-GG/FS.GG.SDD#8" ] \
    && ok "Blocked by: refs that canonicalize alike are de-duped (#8 == FS-GG/FS.GG.SDD#8)" \
    || bad "Blocked by: de-dupe" "$(bblast '.last.text')"

  # 5. An EMPTY value CLEARS — via the distinct clear mutation, never an empty --text (a no-op on the API).
  bbw '' >/dev/null
  [ "$(bblast '.last.op')" = "clear" ] \
    && ok "Blocked by: an empty value CLEARS the field (clearProjectV2ItemFieldValue, not an empty update)" \
    || bad "Blocked by: empty clears" "$(bblast '.last')"

  # 6. A REFUSED write spends ZERO GraphQL — validation PRECEDES item resolution. Run it against a FRESH
  #    cache, so if the gate did NOT fire, bootstrap WOULD hit /graphql: the delta of 0 proves precedence.
  before="$(bbget "$BB_PORT" /_gqlcount | jq -r '.count')"
  pr_refuse="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$(mktemp -d)" \
                 "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' 'RESOLVED: #8 closed, shipped @d80a8ae' 2>&1)"; refrc=$?
  after="$(bbget "$BB_PORT" /_gqlcount | jq -r '.count')"
  { [ "$refrc" -ne 0 ] && [ "$before" = "$after" ]; } \
    && ok "Blocked by: a refused write (a delivery log) is rejected AND spends ZERO GraphQL (validation precedes resolution)" \
    || bad "Blocked by: refused write must cost no GraphQL" "rc=$refrc before=$before after=$after out=$pr_refuse"

  # 7. The delivery log, the inverted edge, and a ref TRAILED by prose are all prose — all refused.
  ( set +e
    FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' 'blocks FS.GG.Governance#14' >/dev/null 2>&1
    [ $? -ne 0 ] ) \
    && ok "Blocked by: the inverted 'blocks X' edge is refused (wrong direction)" \
    || bad "Blocked by: inverted edge must refuse"
  ( set +e
    FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' 'FS-GG/FS.GG.SDD#8 (republish vehicle)' >/dev/null 2>&1
    [ $? -ne 0 ] ) \
    && ok "Blocked by: prose TRAILING a valid ref is refused — the anchored match will not swallow it" \
    || bad "Blocked by: trailing prose must refuse"

  # 8. The prose refusal REDIRECTS: names Status as the home for 'the item IS blocked'.
  prose_out="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' 'not a ref' 2>&1)"
  printf '%s' "$prose_out" | grep -q 'set-field <issue> Status Blocked' \
    && ok "Blocked by: the prose refusal names Status as the right home for 'is blocked'" \
    || bad "Blocked by: prose refusal must name Status" "$prose_out"

  # 9. A '-'/'none' PLACEHOLDER is a distinct refusal — it points at CLEARING, not at Status.
  ph_out="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 'Blocked by' 'none' 2>&1)"; phrc=$?
  { [ "$phrc" -ne 0 ] && printf '%s' "$ph_out" | grep -q "'Blocked by' ''"; } \
    && ok "Blocked by: a '-'/'none' placeholder is refused TOWARD clearing (points at 'Blocked by' '')" \
    || bad "Blocked by: placeholder must point at clearing" "rc=$phrc out=$ph_out"

  # 10. THE GATE IS SCOPED to `Blocked by`. Every other TEXT field stays free-form — Contract takes prose.
  FSGG_GITHUB_API_BASE="http://127.0.0.1:$BB_PORT" FSGG_COORD_CACHE="$BBCACHE" "$ENGINE" set-field --worker bb-13 FS.GG.SDD#42 Contract 'fs-gg-ui-template (0.3.1, preview)' >/dev/null 2>&1
  [ "$(bblast '.last | "\(.field) \(.text)"')" = "Contract fs-gg-ui-template (0.3.1, preview)" ] \
    && ok "Blocked by: the gate is SCOPED — Contract (and every other TEXT field) still takes free-form text" \
    || bad "Blocked by: gate must not touch other fields" "$(bblast '.last')"

  kill "$BB_SRV" 2>/dev/null
fi

# ---- case 13: the `issues` short-id command — resolve the repo like everything else (#446) ----------
#
# `issues` lists a repo's issues over REST with ETag revalidation — the read both coordination skills
# advertise as THE way to read issues WITHOUT spending GraphQL (a 304 costs nothing, #418). The corpus
# (case 13 lines 99-121) certifies that it resolves its `<repo>` argument like EVERY other repo-taking
# command: an `owner/repo` passes through split, a bare short-id maps through `resolve_repo` to the repo
# NAME. bash's bug (#446): `issues` was the ONE command that took the bare token VERBATIM — so `issues
# game` asked for `repos/FS-GG/game` and 404'd, while `--repo game` resolved everywhere else. The natural
# recovery from that 404 is `gh issue list` — 2 GraphQL points a call, the exact budget the command exists
# to save. The corpus counts `gh` (`issue-list FS-GG/<repo>` in `$GH_LOG`); this drives the ENGINE over
# HTTP against `issues_server.py`, which records the `owner/repo` (and state/label/If-None-Match) of every
# `/repos/*/issues` request — so the assertion becomes "the fixture was asked for FS-GG/FS.GG.Game, NEVER
# FS-GG/game", one transport under. `issues` is a pure REST read (it never bootstraps the board), so the
# fixture serves no GraphQL. Disposed on the record (ADR-0040 §5): bash's `--jq EXPR` is an ERGONOMIC —
# the engine emits the raw JSON array and the caller projects it with real jq (the Json-is-contract rule),
# so `issues … | jq` here IS the port of `issues … --jq …`, and the engine refuses an unknown `--jq` flag.
ISS_OUT="$(mktemp)"; python3 "$HERE/issues_server.py" >"$ISS_OUT" 2>/dev/null & ISS_SRV=$!; ISS_PORT=""
for _ in $(seq 1 50); do ISS_PORT="$(head -n1 "$ISS_OUT" 2>/dev/null)"; [ -n "$ISS_PORT" ] && break; sleep 0.1; done
rm -f "$ISS_OUT"
issget() { python3 -c 'import sys,urllib.request; sys.stdout.write(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_requests").read().decode())' "$1" 2>/dev/null; }
if [ -z "$ISS_PORT" ]; then bad "#446: issues fixture bound a port"; else
  ISSCACHE="$(mktemp -d)"
  iss() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$ISS_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$ISSCACHE" "$ENGINE" issues "$@"; }
  # The nwo of the LAST /repos/*/issues request the fixture saw — the resolved REST path, `$GH_LOG`'s
  # `issue-list <nwo>` one transport over. `.inm` proves the conditional (ETag) read.
  isslast() { issget "$ISS_PORT" | jq -r ".[-1] | $1"; }
  issnwos() { issget "$ISS_PORT" | jq -r '[.[].nwo] | join(" ")'; }

  # 1. A bare short-id resolves to the repo board rows carry — the ENGINE emits the issue array (jq'd by the
  #    CALLER, the port of bash's `--jq`), and the fixture was asked for the RESOLVED owner/repo. #641: a
  #    PULL REQUEST is an issue in REST, and `issues` must NOT list it — 777 (which carries `pull_request`)
  #    is dropped, so the §4 duplicate-check cannot read a PR as "already filed" and suppress a real finding.
  nums="$(iss sdd | jq -c '[.[].number]')"
  [ "$nums" = "[501,502]" ] \
    && ok "#641: 'issues sdd' emits genuine issues only — the PR (777) is filtered out" \
    || bad "#641: issues must drop pull requests" "got: $nums"
  printf '%s' "$nums" | grep -q '777' \
    && bad "#641: a PR (777) must never appear in the issues listing" "got: $nums" \
    || ok "#641: ...and the §4 duplicate-check never sees a PR as an already-filed issue"
  [ "$(isslast '.nwo')" = "FS-GG/FS.GG.SDD" ] \
    && ok "#446: 'issues sdd' reads FS-GG/FS.GG.SDD over REST — the short-id resolves like --repo does" \
    || bad "#446: issues short-id resolves" "requested: $(isslast '.nwo')"

  # 2. THE #446 POSTER CHILD. `game` is one of the two short-ids resolve_repo once let fall through to the
  #    literal token (#381), so `issues game` asked for `repos/FS-GG/game` and 404'd. It must resolve to
  #    FS-GG/FS.GG.Game, and the bare `FS-GG/game` must NEVER reach the fixture.
  iss game >/dev/null
  [ "$(isslast '.nwo')" = "FS-GG/FS.GG.Game" ] \
    && ok "#446: 'issues game' resolves to FS-GG/FS.GG.Game (the short-id that once 404'd as repos/FS-GG/game)" \
    || bad "#446: issues game must resolve, not 404" "requested: $(isslast '.nwo')"
  case " $(issnwos) " in
    *" FS-GG/game "*) bad "#446: the bare short-id must NEVER reach GitHub unresolved" "saw FS-GG/game" ;;
    *) ok "#446: ...and 'FS-GG/game' NEVER reaches the fixture — the 404 (and the gh-issue-list fallback) is gone" ;;
  esac

  # 3. An EXPLICIT owner/repo is authoritative — split and passed through untouched, never re-resolved.
  iss FS-GG/FS.GG.Game >/dev/null
  [ "$(isslast '.nwo')" = "FS-GG/FS.GG.Game" ] \
    && ok "#446: an explicit owner/repo ('FS-GG/FS.GG.Game') passes through untouched" \
    || bad "#446: owner/repo passthrough" "requested: $(isslast '.nwo')"

  # 4. A non-original-four short-id resolves too — resolve_repo covers the WHOLE roster (#381), not just
  #    the framework repos. `audio` fell through to the literal token in the same bug class.
  iss audio >/dev/null
  [ "$(isslast '.nwo')" = "FS-GG/FS.GG.Audio" ] \
    && ok "#446/#381: 'issues audio' resolves across the whole roster (not just the original four repos)" \
    || bad "#446: roster-wide resolution" "requested: $(isslast '.nwo')"

  # 5. THE BUDGET-FREE 304 (#418) — the reason `issues` exists. A second read of the SAME listing, with the
  #    same cache, sends the stored ETag; the fixture answers 304, and the engine serves the body FROM CACHE.
  #    That is a conditional request (`inm` carries the validator, not `none`) served for zero fresh body —
  #    the ETag revalidation the command is built on.
  iss sdd >/dev/null            # warms the body+etag cache (the FILTERED body is what is stored, #641)
  again="$(iss sdd | jq -c 'length')"
  [ "$again" = "2" ] && [ "$(isslast '.inm')" != "none" ] \
    && ok "#418: a repeat 'issues sdd' sends the ETag and is served a 304 from cache — the budget-free read" \
    || bad "#418: issues revalidates with the stored ETag (304 is free)" "count=$again inm=$(isslast '.inm')"

  # 6. `--state` and `--label` shape the REST path (and the cache key) — the query the listing is scoped by.
  iss sdd --state closed --label bug >/dev/null
  [ "$(isslast '"\(.state) \(.label)"')" = "closed bug" ] \
    && ok "#446: --state and --label shape the REST listing (state=closed, labels=bug)" \
    || bad "#446: issues --state/--label" "$(isslast '"\(.state) \(.label)"')"

  # 7. No repo is a hard refusal — `issues` cannot default to a checkout (it is not a scoped worker command;
  #    the repo is its one required positional). It refuses rather than guessing.
  iss_norepo="$(iss 2>&1 >/dev/null)"; iss_rc=$?
  { printf '%s' "$iss_norepo" | grep -q 'a repo is required' && [ "$iss_rc" -ne 0 ]; } \
    && ok "#446: 'issues' with no repo REFUSES (a repo is required) — never a silent org-wide read" \
    || bad "#446: issues must require a repo" "rc=$iss_rc: $iss_norepo"

  kill "$ISS_SRV" 2>/dev/null; rm -rf "$ISSCACHE"
fi

# CASE 13 IS NOW FULL. Its last leg — `reap` (the DESTRUCTIVE worker command) scoping to the checkout you
# are standing in (#480) — is proven in the "REAP SCOPES TO THE CHECKOUT" section below (a bare reap from
# an SDD checkout considers only SDD's claims; from a Rendering checkout, Rendering's; outside a checkout
# it REFUSES). The `Blocked by` canonicalization gate and the `issues` short-id command (#446) landed
# above; the epic-rollup / NO-TOUCH-SET `lint` rules (#496) landed with case 14.

# ---- VERIFY-PATHS: DID THE PR STAY INSIDE ITS TOUCH-SET? (case 23) --------------------------------
#
# The corpus (`tests/fsgg-coord/cases/23-verify-paths-boundary.sh`) certifies `verify-paths` as the merge
# boundary's touch-set gate, and — the property this whole command exists for — that "I could not check"
# is NEVER one of its verdicts (#322). It resolves the issue a PR implements from its `item/<n>-…` branch
# (else what it closes), reads that issue's `Paths:` touch-set, and diffs the PR's changed files against
# it, reaching exactly one of:
#   OK      every changed file is inside the touch-set (exit 0).
#   DRIFT   a file falls outside it (named); by DEFAULT this exits NON-ZERO — the `touch-set-drift.yml`
#           gate greps this line — and `--warn` downgrades it to advisory (exit 0), the CI advisory mode.
#   SKIP    nothing to verify against (the PR implements no tracked item, or the item declared no
#           touch-set). GREEN — a PR with nothing to check is not a failure.
#   (error) the head ref / body / files could not be READ (#322): NO verdict, non-zero, EVEN under --warn.
# `verifypaths_server.py` is that world one transport over — #70's touch-set is the SAME Scene one case 22
# seeds, so PR 7 (Scene files) is OK and PR 8 (an Audio file + README) is DRIFT: the corpus's certified
# pair. The DRIFT-names-the-count wording ("touches 2 file(s) outside") is bash's; the engine names the
# files themselves, so parity holds the PROPERTY (a DRIFT verdict that NAMES the offending file and exits
# non-zero), not bash's literal sentence — the ADR-0040 §5 re-expression, as everywhere else.
vpsrv() {  # vpsrv <env-kv...> --  ; sets globals VP_PORT and VP_SRV for the spawned fixture
  local envs=() ; while [ "$1" != "--" ]; do envs+=("$1"); shift; done; shift
  local out; out="$(mktemp)"
  env ${envs[@]+"${envs[@]}"} python3 "$HERE/verifypaths_server.py" >"$out" 2>/dev/null &
  local srv=$! port=""
  for _ in $(seq 1 50); do port="$(head -n1 "$out" 2>/dev/null)"; [ -n "$port" ] && break; sleep 0.1; done
  rm -f "$out"
  VP_PORT="$port"; VP_SRV="$srv"
}

vpsrv -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths fixture bound a port"; else
  vp() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" verify-paths "$@" 2>&1; }

  # 1. INSIDE the touch-set → OK, exit 0 (case 23's certified "a PR inside its touch-set is OK").
  v7="$(vp --pr 7 --repo FS.GG.SDD)"; v7rc=$?
  { [ "$v7rc" -eq 0 ] && printf '%s' "$v7" | grep -q 'FSGG-PATHS OK'; } \
    && ok "verify-paths: a PR inside its touch-set is OK and exits 0 (case 23)" \
    || bad "verify-paths OK parity" "rc=$v7rc: $v7"

  # 2. DRIFT names the offending file(s) and, by default, exits NON-ZERO — the gate the drift workflow
  #    greps. The corpus counts ("2 file(s) outside"); the engine names them — same property, re-expressed.
  v8="$(vp --pr 8 --repo FS.GG.SDD)"; v8rc=$?
  { [ "$v8rc" -ne 0 ] && printf '%s' "$v8" | grep -q 'FSGG-PATHS DRIFT'; } \
    && ok "verify-paths: drift is reported and exits non-zero by default (case 23)" \
    || bad "verify-paths DRIFT exit parity" "rc=$v8rc: $v8"
  printf '%s' "$v8" | grep -q 'src/Audio/Mixer.fs' \
    && ok "verify-paths: DRIFT names the offending file (src/Audio/Mixer.fs) (case 23)" \
    || bad "verify-paths must name the drifting file" "$v8"
  printf '%s' "$v8" | grep -q 'widen' \
    && ok "verify-paths: DRIFT points at the remedy (widen) (case 23)" \
    || bad "verify-paths must point at the widen remedy" "$v8"

  # 3. --warn downgrades the DRIFT verdict to advisory: SAME verdict, exit 0 (the advisory CI gate).
  v8w="$(vp --pr 8 --repo FS.GG.SDD --warn)"; v8wrc=$?
  { [ "$v8wrc" -eq 0 ] && printf '%s' "$v8w" | grep -q 'FSGG-PATHS DRIFT'; } \
    && ok "verify-paths --warn: reports the drift but exits 0 (case 23)" \
    || bad "verify-paths --warn downgrade parity" "rc=$v8wrc: $v8w"

  # 4. A PR that implements no tracked item is SKIP, never a silent OK — and it must not leak an OK
  #    verdict into a SKIP (case 23: "SKIP is not mistaken for OK"). PR 9's branch is not item/<n>-… and
  #    it closes nothing, so resolution falls through to SKIP.
  #    The reason is pinned too (not just "some SKIP"): the SKIP must be the CANNOT-IDENTIFY one, so a
  #    right-verdict-wrong-reason SKIP cannot pass — the fidelity the corpus keeps (case 23 line 55).
  v9="$(vp --pr 9 --repo FS.GG.SDD)"
  { printf '%s' "$v9" | grep -q 'FSGG-PATHS SKIP' && printf '%s' "$v9" | grep -q 'cannot tell which issue' \
      && ! printf '%s' "$v9" | grep -q 'FSGG-PATHS OK'; } \
    && ok "verify-paths: an unlinked PR is SKIP (cannot tell which issue), not OK (case 23)" \
    || bad "verify-paths unlinked-SKIP parity" "$v9"

  # 5. An item that declares no 'Paths:' is SKIP too — nothing to verify against (case 23). PR 10
  #    implements #72, which declares no touch-set. The reason is pinned to the DECLARES-NONE SKIP (case
  #    23 line 57), so a SKIP for any OTHER reason (e.g. a mis-resolved issue) would not pass here.
  v10="$(vp --pr 10 --repo FS.GG.SDD)"
  { printf '%s' "$v10" | grep -q 'FSGG-PATHS SKIP' && printf '%s' "$v10" | grep -q "declares no 'Paths:'"; } \
    && ok "verify-paths: an undeclared touch-set is SKIP (declares no 'Paths:') (case 23)" \
    || bad "verify-paths undeclared-SKIP parity" "$v10"

  kill "$VP_SRV" 2>/dev/null
fi

# 6. #322: "I COULD NOT CHECK" IS NEVER A VERDICT. The PR read 503s, so the head ref is unreadable and
#    the engine cannot tell which issue the PR implements. It must reach NO verdict and fail closed — and
#    --warn, which downgrades a DRIFT to advisory, cannot license a verdict on a subject nobody read.
#    A fresh server (the toggle changes what /pulls/7 answers).
vpsrv FSGG_PARITY_HEADREF_FAIL=7 -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths #322 fixture bound a port"; else
  vpf() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" verify-paths "$@" 2>&1; }
  vf="$(vpf --pr 7 --repo FS.GG.SDD)"; vfrc=$?
  { [ "$vfrc" -ne 0 ] && ! printf '%s' "$vf" | grep -q 'FSGG-PATHS'; } \
    && ok "#322: an unreadable head ref reaches NO verdict and fails closed by default (case 23)" \
    || bad "#322: verify-paths must not invent a verdict from an unread PR" "rc=$vfrc: $vf"
  vfw="$(vpf --pr 7 --repo FS.GG.SDD --warn)"; vfwrc=$?
  { [ "$vfwrc" -ne 0 ] && ! printf '%s' "$vfw" | grep -q 'FSGG-PATHS'; } \
    && ok "#322: ...and it fails closed under --warn too — --warn cannot downgrade a read that never happened (case 23)" \
    || bad "#322: verify-paths must fail closed under --warn" "rc=$vfwrc: $vfw"
  kill "$VP_SRV" 2>/dev/null
fi

# ---- case 23-remainder / case 24: `verify-paths --issue` and the repo boundary (#479 / #494) --------
# The core-verdicts slice (#797) deferred `--issue` — checking a PR against an EXPLICITLY named issue's
# touch-set, bypassing the branch/closing-ref resolution — and the repo-boundary refusals it enables. That
# was a real PORT GAP: `verifyPaths` had no `--issue` path at all. It is closed here. `--issue`'s repo is
# authoritative, which gives three certified properties (case 23 lines 85-89, case 24 lines 56-142):
#   * #479 — a `--issue` in a DIFFERENT repo than `--repo` is a STRADDLE the tool refuses: a touch-set
#     there says nothing about the files changed here, so it reaches NO verdict (no OK/DRIFT on stdout for
#     the drift gate to grep) and FAILS CLOSED — by default AND under --warn (--warn downgrades a real
#     DRIFT to advisory; it cannot license a verdict on a subject that was never compared).
#   * #494 — the issue read is REPO-QUALIFIED: SDD#494 (Scene) and Rendering#494 (Audio) share a NUMBER
#     but are different touch-sets, so the same PR 7 (Scene files) is OK against one and DRIFT against the
#     other. A store keyed by number alone could not tell them apart — the fixture keys issues by repo.
#   * `--repo` is reduced the way every worker command reduces it (a registry short-id, a differently-cased
#     owner/repo), and a bare-repo `--issue` (owner defaults) is not a FALSE conflict.
# Re-expressed at the HTTP layer (ADR-0040 §5): bash counts `gh` reads and logs each read's repo; here the
# fixture serves repo-keyed issue bodies and the PROPERTY (right verdict per repo, refusal across the
# boundary) is what parity holds — the boundary is enforced by the ANSWER, not by a call count.
vpsrv -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths --issue fixture bound a port"; else
  vpi() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" verify-paths "$@" 2>&1; }

  # 1. --issue agreeing with --repo names the issue directly → OK (case 24 line 134-136).
  i1="$(vpi --pr 7 --repo FS.GG.SDD --issue FS-GG/FS.GG.SDD#70)"; i1rc=$?
  { [ "$i1rc" -eq 0 ] && printf '%s' "$i1" | grep -q 'FSGG-PATHS OK'; } \
    && ok "verify-paths --issue: a same-repo named issue is checked directly → OK (case 24)" \
    || bad "verify-paths --issue OK parity" "rc=$i1rc: $i1"

  # 2. #479: --repo and --issue in DIFFERENT repos reach NO verdict, name the other repo, say the
  #    touch-set was NOT checked, and fail closed — by default (case 23 lines 110-117).
  m="$(vpi --pr 7 --repo FS.GG.SDD --issue FS-GG/FS.GG.Rendering#70)"; mrc=$?
  { [ "$mrc" -ne 0 ] && ! printf '%s' "$m" | grep -qE 'FSGG-PATHS (OK|DRIFT)'; } \
    && ok "#479: a cross-repo --repo/--issue straddle reaches NO verdict and fails by default (case 23)" \
    || bad "#479 straddle parity" "rc=$mrc: $m"
  printf '%s' "$m" | grep -q 'FS-GG/FS.GG.Rendering' \
    && ok "#479: ...and names the other repo it was asked to straddle (case 23)" \
    || bad "#479 must name the other repo" "$m"
  printf '%s' "$m" | grep -q 'touch-set was NOT checked' \
    && ok "#479: ...and says the touch-set was NOT checked (case 23)" \
    || bad "#479 must say the touch-set was not checked" "$m"
  # ...and --warn does not downgrade a straddle to advisory: a verdict on the wrong subject is never
  #    licensed (case 23 lines 120-125).
  mw="$(vpi --pr 7 --repo FS.GG.SDD --issue FS-GG/FS.GG.Rendering#70 --warn)"; mwrc=$?
  { [ "$mwrc" -ne 0 ] && ! printf '%s' "$mw" | grep -qE 'FSGG-PATHS (OK|DRIFT)'; } \
    && ok "#479: ...and it fails closed under --warn too — a straddle is never advisory (case 23)" \
    || bad "#479 --warn fail-closed parity" "rc=$mwrc: $mw"

  # 3. #494: the issue read is repo-qualified — same PR, same issue NUMBER, opposite verdict by repo
  #    (case 24 lines 56-62). SDD#494 (Scene) → OK; Rendering#494 (Audio) → DRIFT on PR 7's Scene files.
  q1="$(vpi --pr 7 --repo FS.GG.SDD --issue FS-GG/FS.GG.SDD#494)"; q1rc=$?
  { [ "$q1rc" -eq 0 ] && printf '%s' "$q1" | grep -q 'FSGG-PATHS OK'; } \
    && ok "#494: SDD#494 (Scene) declares the PR's files → OK (case 24)" \
    || bad "#494 SDD OK parity" "rc=$q1rc: $q1"
  q2="$(vpi --pr 7 --repo FS.GG.Rendering --issue FS-GG/FS.GG.Rendering#494)"; q2rc=$?
  { [ "$q2rc" -ne 0 ] && printf '%s' "$q2" | grep -q 'FSGG-PATHS DRIFT'; } \
    && ok "#494: Rendering#494 — same number, other repo — does NOT: DRIFT (case 24)" \
    || bad "#494 Rendering DRIFT parity" "rc=$q2rc: $q2"

  # 4. --repo reductions and a bare-repo --issue are not false conflicts (case 24 lines 134-142).
  s1="$(vpi --pr 7 --repo sdd --issue FS-GG/FS.GG.SDD#70)"
  printf '%s' "$s1" | grep -q 'FSGG-PATHS OK' \
    && ok "verify-paths --issue: a registry short-id --repo agrees with the issue's repo (case 24)" \
    || bad "short-id --repo agreement" "$s1"
  s2="$(vpi --pr 7 --repo FS-GG/fs.gg.sdd --issue FS-GG/FS.GG.SDD#70)"
  printf '%s' "$s2" | grep -q 'FSGG-PATHS OK' \
    && ok "verify-paths --issue: a differently-cased --repo is not a conflict (case 24)" \
    || bad "case-insensitive --repo agreement" "$s2"
  s3="$(vpi --pr 7 --repo FS.GG.SDD --issue FS.GG.SDD#70)"
  printf '%s' "$s3" | grep -q 'FSGG-PATHS OK' \
    && ok "verify-paths --issue: a bare-repo --issue (owner defaults) is not a conflict (case 24)" \
    || bad "bare-repo --issue agreement" "$s3"

  # 5. --issue decides the repo when --repo is ABSENT — the issue, not the checkout (case 23 line 132-135).
  n1="$(vpi --pr 7 --issue FS-GG/FS.GG.SDD#70)"; n1rc=$?
  { [ "$n1rc" -eq 0 ] && printf '%s' "$n1" | grep -q 'FSGG-PATHS OK'; } \
    && ok "verify-paths --issue: the issue decides the repo when --repo is absent (case 23)" \
    || bad "--issue decides repo parity" "rc=$n1rc: $n1"
  kill "$VP_SRV" 2>/dev/null
fi

# 6. --issue BYPASSES the head-ref read entirely (case 23 lines 85-89): even when the PR read 503s — so
#    the branch could not be resolved — a run that NAMED its issue still reaches a verdict. The fix guards
#    the CALL (prHeadRef), not the whole resolution step. A fresh server with the head-ref fail toggle.
vpsrv FSGG_PARITY_HEADREF_FAIL=7 -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths --issue bypass fixture bound a port"; else
  vpb() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" verify-paths "$@" 2>&1; }
  b1="$(vpb --pr 7 --repo FS.GG.SDD --issue FS-GG/FS.GG.SDD#70)"; b1rc=$?
  { [ "$b1rc" -eq 0 ] && printf '%s' "$b1" | grep -q 'FSGG-PATHS OK'; } \
    && ok "verify-paths --issue: bypasses the head-ref read — OK even when the PR read fails (case 23)" \
    || bad "--issue head-ref bypass parity" "rc=$b1rc: $b1"
  kill "$VP_SRV" 2>/dev/null
fi

# ---- case 23-remainder: #430 — the repo comes off the git REMOTE (neither --repo nor --issue) --------
#
# With neither --repo nor --issue, verify-paths derives the PR's repo from the checkout you are standing
# in — `git config remote.origin.url`, the same FREE/offline signal `next`/`take`/`batch`/`who` scope to
# (#480), now wired into verify-paths. The corpus (case 23 lines 145-208) certifies this against
# `gh repo view`; the engine deliberately does NOT call `gh repo view` (the disposition below), so the
# property that survives the transport is: the PR is read from the repo the REMOTE names, and resolving it
# spends NO GraphQL — asserted on the REQUEST, because the verdict is identical from either repo (the whole
# trap: the fixtures would serve a healthy OK from the wrong repo too — case 23 lines 158-169).
VP_REQLOG="$(mktemp)"; VP_CO="$(mktemp -d)"
vpsrv FSGG_PARITY_REQLOG="$VP_REQLOG" -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths #430 fixture bound a port"; else
  # A fake checkout whose ONLY signal is its remote; and a checkout with NO remote (bash's CO_NOREMOTE).
  mkdir -p "$VP_CO/sdd" && git -C "$VP_CO/sdd" init -q && git -C "$VP_CO/sdd" remote add origin https://github.com/FS-GG/FS.GG.SDD.git
  mkdir -p "$VP_CO/noremote" && git -C "$VP_CO/noremote" init -q
  vpco() { local dir="$1"; shift; ( cd "$dir" \
      && FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" \
         "$ENGINE" verify-paths "$@" 2>&1 ); }

  # (a) THE ACCEPTANCE: a bare verify-paths from an FS.GG.SDD checkout reaches a verdict — repo off the
  #     remote, no flag given.
  : >"$VP_REQLOG"
  r7="$(vpco "$VP_CO/sdd" --pr 7)"; r7rc=$?
  { [ "$r7rc" -eq 0 ] && printf '%s' "$r7" | grep -q 'FSGG-PATHS OK'; } \
    && ok "#430: a bare verify-paths (no --repo/--issue) from an SDD checkout reaches OK — repo off the remote (case 23)" \
    || bad "#430 git-remote default OK parity" "rc=$r7rc: $r7"
  # (a') …and the PR was read from THE REMOTE's repo — assert on the request, not the identical verdict.
  grep -q 'GET /repos/FS-GG/FS.GG.SDD/pulls/7' "$VP_REQLOG" \
    && ok "#430: ...and the PR is read from FS.GG.SDD — the repo the git remote named (case 23)" \
    || bad "#430 PR read must hit the remote's repo" "$(cat "$VP_REQLOG")"
  # (a'') …and resolving that repo spent NO GraphQL — the #430 acceptance re-expressed at the transport.
  #       bash's bug was reading `gh repo view`'s empty result (a spent GraphQL call that had FAILED, its
  #       reason `2>/dev/null || true`-d away) as "not inside a checkout". The engine reads `git config` —
  #       offline, free — so a dead budget can never be misreported as a checkout problem: no /graphql.
  grep -q 'POST /graphql' "$VP_REQLOG" \
    && bad "#430: resolving the repo must spend NO GraphQL" "spent a /graphql call: $(cat "$VP_REQLOG")" \
    || ok "#430: ...and resolved the repo with NO GraphQL — a dead budget can never be blamed on the checkout (case 23)"

  # (b) No remote AND no flag: there is no subject to check, so it REFUSES — an EARNED refusal, since
  #     `git config` failing is not a rate limit dressed up as one (the engine never spends budget to
  #     resolve the repo, so the EX_RATE-vs-checkout ambiguity bash's `gh repo view` fallback risks — case
  #     23 lines 171-208 — cannot arise here at all; see the disposition below).
  nr="$(vpco "$VP_CO/noremote" --pr 7)"; nrrc=$?
  { [ "$nrrc" -ne 0 ] \
      && printf '%s' "$nr" | grep -q 'not inside a GitHub checkout' \
      && printf '%s' "$nr" | grep -q -- '--repo FS-GG/<repo>'; } \
    && ok "#430: no remote + no flag REFUSES with the earned 'not inside a checkout' + the --repo remedy (case 23)" \
    || bad "#430 no-remote refusal parity" "rc=$nrrc: $nr"
  case "$nr" in
    *"FSGG-PATHS"*) bad "#430: a refusal is NOT a verdict" "printed a verdict with no subject: $nr" ;;
    *) ok "#430: ...and reaches NO FSGG-PATHS verdict — a repo it could not name is a subject it never looked at (case 23)" ;;
  esac
  kill "$VP_SRV" 2>/dev/null
fi
rm -f "$VP_REQLOG"; rm -rf "$VP_CO"

# ---- case 24: the cross-repo CLOSING-ref — a PR closing ANOTHER repo's issue (lines 148-165) ---------
#
# GitHub lets a PR close an issue in a DIFFERENT repo, so the closing-ref fallback can hand back a
# cross-repo ref all on its own — no --issue flag involved. PR 11's branch is not item/<n>-…, so
# resolution falls through to the GraphQL query, which answers FS-GG/FS.GG.Rendering#70. A touch-set there
# says nothing about the files changed in SDD, so the only safe outcome is SKIP, naming the other repo —
# never a verdict across the boundary. (The closing-ref answer is keyed to the PR in the query variables,
# so the pre-existing unlinked SKIP — PR 9, which closes NOTHING — must survive the new arm untouched.)
vpsrv -- verifypaths
if [ -z "$VP_PORT" ]; then bad "verify-paths closing-ref fixture bound a port"; else
  vpx() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$VP_PORT" FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" verify-paths "$@" 2>&1; }
  x11="$(vpx --pr 11 --repo FS.GG.SDD)"; x11rc=$?
  printf '%s' "$x11" | grep -q 'FSGG-PATHS SKIP' \
    && ok "verify-paths: a PR closing ANOTHER repo's issue is SKIP, not a verdict (case 24)" \
    || bad "cross-repo closing-ref SKIP parity" "rc=$x11rc: $x11"
  printf '%s' "$x11" | grep -q 'FS.GG.Rendering#70' \
    && ok "verify-paths: ...and names the other repo it would have straddled (case 24)" \
    || bad "cross-repo closing-ref must name the other repo" "$x11"
  case "$x11" in
    *"FSGG-PATHS OK"*|*"FSGG-PATHS DRIFT"*) bad "verify-paths: a cross-repo close reaches NO verdict" "printed a verdict across the boundary: $x11" ;;
    *) ok "verify-paths: ...reaching no OK/DRIFT across the repo boundary (case 24)" ;;
  esac
  x9="$(vpx --pr 9 --repo FS.GG.SDD)"
  printf '%s' "$x9" | grep -q 'FSGG-PATHS SKIP' \
    && ok "verify-paths: a PR that closes NOTHING is still the unlinked SKIP — the PR-keyed arm did not swallow it (case 24)" \
    || bad "unlinked SKIP must survive the closing-ref arm" "$x9"
  kill "$VP_SRV" 2>/dev/null
fi

# DISPOSITION ON THE RECORD (not silently skipped): what verify-paths' repo boundary still leaves —
#   * SKIP EXIT CODE is a DELIBERATE DIVERGENCE. bash FAILS non-zero on an unlinked/undeclared PR (and on a
#     cross-repo close) without `--warn` (case 23 line 59; case 24 line 164); the engine makes SKIP always
#     exit 0 — a PR with genuinely nothing to check is not a merge-blocking failure. Ported throughout as
#     the engine's certified behaviour (SKIP is green both ways), NOT as bash's rc. The verdict TEXT (SKIP
#     vs OK, naming the other repo) still carries the distinction the touch-set-drift gate needs.
#   * #430's `gh repo view` FALLBACK is a DELIBERATE DIVERGENCE the engine does not implement. When there is
#     no git remote, bash asks `gh repo view` (a GraphQL call) and must classify its FAILURE — a rate limit
#     is EX_RATE(75), any other failure reports gh's own words, and only a clean empty answer is the earned
#     "not inside a checkout" (case 23 lines 171-208). The engine resolves the repo ONLY from `git config`
#     (free, offline) and has no gh-repo-view leg at all — so those failure modes are structurally absent:
#     repo resolution can never touch the budget, which is the very property #430 exists to guarantee. The
#     no-remote case is a plain refusal (leg (b) above), not a rate-limit classification.

# ---- AN ID TWO WORKERS SHARE IS NOT A LOCK (case 44): #419 ---------------------------------------
#
# The corpus (`tests/fsgg-coord/cases/44-invented-id-419.sh`, #419) certifies two defences against the
# double-claim ADR-0027 moved the lock off the shared GitHub account to prevent — one level down, where the
# id itself is shared. Eight live agents once drew from four of twenty words and two independently picked
# the same suffix; a lock whose key two workers hold is not a lock.
#
#   1. MINT, don't invent — the remedy is a COMMAND (`whoami --mint`), so there is no literal id in any
#      warning for an agent to pattern-match and paste. Its stdout is EXACTLY one eval-able line, and the id
#      is unique per call (both halves from the CSPRNG — a pid+time seed drew the same word for every agent
#      a harness fanned out in one second).
#   2. `claim` REFUSES a live marker carrying OUR worker id but a DIFFERENT session — a TWIN, not us. Before
#      #419 that landed in the heartbeat path and silently renewed, putting two workers on one item.
#
# This was mostly ALREADY in the engine: `Identity.mint` (CSPRNG, both halves), `whoami --mint` (one line to
# stdout), and markers that carry `session=` (`Reads.sessionRe`) all existed. The PORT GAP was the twin
# refusal — `Writes.claim`'s "already ours by id" branch adopted the marker unconditionally, never comparing
# sessions — and the `whoami` shared-session WARNING, which explained the hazard on stdout but named no
# remedy. This slice lands the fix (a `Twin` outcome the CAS returns when both sessions are known and differ;
# a stderr warning pointing at the mint command) and holds the engine to the corpus's answers over HTTP.
#
# Re-expressed at the HTTP layer (ADR-0040 §5): the corpus drives the twin scenario through a PATH-shim `gh`
# and a comment store; here `restore_server.py` seeds the same markers (a twin with `session=79b9e347`; a
# sessionless one), answers the CAS's marker reads, and records that NO second marker is posted — so the
# "left the twin's marker intact" property is read off the served comment set, not `gh` call counts.
#
# DISPOSED ON THE RECORD (not silently skipped): the corpus's "lease renewed" WORDING (case 44 lines 90-100)
# is bash's; the engine reports a successful re-claim as `claimed <ref> by worker <w>` (a `claim` that finds
# the marker already ours is a no-op Won, not a lease-rewriting heartbeat — the lease matters more than the
# courtesy string). This re-expresses the PROPERTY #419 leg 4 protects — a sessionless or same-session marker
# with our id SUCCEEDS (is ours), it is not refused — asserted on the exit code and the absence of a refusal,
# not on the literal "lease renewed".

# (offline, no server) 1. THE REMEDY IS A COMMAND. Its stdout is EXACTLY one eval-able line — commentary
#    goes to stderr, or `eval "$(… --mint)"` executes the prose.
m1="$("$ENGINE" whoami --mint 2>/dev/null)"
[ "$(printf '%s\n' "$m1" | wc -l | tr -d ' ')" = "1" ] \
  && ok "#419: whoami --mint prints EXACTLY one line on stdout (eval-safe — no prose to execute) (case 44)" \
  || bad "#419: --mint must print one line" "$m1"
case "$m1" in export\ FSGG_WORKER=*) ok "#419: ...and it is an eval-able 'export FSGG_WORKER=' (case 44)" ;;
             *) bad "#419: --mint must emit an eval-able export" "$m1" ;; esac
m2="$("$ENGINE" whoami --mint 2>/dev/null)"; m3="$("$ENGINE" whoami --mint 2>/dev/null)"
[ "$(printf '%s\n%s\n%s\n' "$m1" "$m2" "$m3" | sort -u | wc -l | tr -d ' ')" = "3" ] \
  && ok "#419: successive mints do NOT collide — both halves are CSPRNG, not a pid+time seed (case 44)" \
  || bad "#419: successive mints collided" "$(printf '%s / %s / %s' "$m1" "$m2" "$m3")"
# The minted id is the one `eval` takes effect as — the ritual §0 tells each worker to run.
minted_id="$(printf '%s' "$m1" | sed 's/^export FSGG_WORKER=//')"
eval_id="$(eval "$m1"; "$ENGINE" whoami 2>/dev/null | awk '/^worker/{print $2}')"
[ "$minted_id" = "$eval_id" ] \
  && ok "#419: the minted id is the one eval takes effect as (round-trips through whoami) (case 44)" \
  || bad "#419: minted id must round-trip through eval" "minted=$minted_id eval=$eval_id"

# (offline) 2. THE SHARED-ID WARNING points at the MINT COMMAND, tells the worker not to invent one, and
#    offers NO literal id to copy — a warning that named `finch-a3f` is exactly the attractor #419 documents.
#    A CLAUDE_CODE session shares one id across every subagent, so a bare `whoami` deriving from it warns.
sw="$(env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID -u FSGG_WORKER \
        CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064 "$ENGINE" whoami 2>&1 >/dev/null)"
printf '%s' "$sw" | grep -q 'whoami --mint' \
  && ok "#419: the shared-id warning points at the MINT command (case 44)" \
  || bad "#419: shared-id warning must name 'whoami --mint'" "$sw"
printf '%s' "$sw" | grep -q 'do NOT invent' \
  && ok "#419: ...and tells the worker not to invent one (case 44)" \
  || bad "#419: shared-id warning must say 'do NOT invent'" "$sw"
[ "$(printf '%s' "$sw" | grep -cE '(finch|heron|wren)-[0-9a-f]{3}' || true)" = "0" ] \
  && ok "#419: ...and offers NO literal id to copy (no 'finch-a3f' attractor) (case 44)" \
  || bad "#419: shared-id warning must offer no literal id" "$sw"

# (fixture) 3. THE REGRESSION. A live marker with OUR id but a DIFFERENT session is a TWIN, not us — it
#    must REFUSE, not adopt-and-heartbeat. `restore_server.py` seeds #74 held by heron-7c2 session=79b9e347
#    and #71 held sessionlessly by dunlin-9f1.
TW_OUT="$(mktemp)"
FSGG_PARITY_MARKERS='[{"n":74,"id":819,"worker":"heron-7c2","session":"79b9e347"},{"n":71,"id":820,"worker":"dunlin-9f1"}]' \
  python3 "$HERE/restore_server.py" >"$TW_OUT" 2>/dev/null &
TW_SRV=$!; TW_PORT=""
for _ in $(seq 1 50); do TW_PORT="$(head -n1 "$TW_OUT" 2>/dev/null)"; [ -n "$TW_PORT" ] && break; sleep 0.1; done
rm -f "$TW_OUT"
if [ -z "$TW_PORT" ]; then bad "twin fixture bound a port"; else
  # tclaim <session> <worker> <claim-args...> — a claim as a named session/worker against the twin fixture.
  tclaim() { local s="$1" w="$2"; shift 2
    env FSGG_GITHUB_API_BASE="http://127.0.0.1:$TW_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
        FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
        env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID CLAUDE_CODE_SESSION_ID="$s" FSGG_WORKER="$w" \
        "$ENGINE" claim "$@"; }
  tmarkers() { python3 -c 'import sys,urllib.request; print(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/repos/FS-GG/FS.GG.SDD/issues/"+sys.argv[2]+"/comments").read().decode())' "$TW_PORT" "$1" 2>/dev/null; }

  twin_err="$(tclaim ed60050b heron-7c2 FS.GG.SDD#74 2>&1 >/dev/null)"; twin_rc=$?
  [ "$twin_rc" -ne 0 ] \
    && ok "#419: claim REFUSES a marker with our id but another session (non-zero) (case 44)" \
    || bad "#419: a twin claim must be refused" "rc=$twin_rc err=$twin_err"
  printf '%s' "$twin_err" | grep -q 'two workers share one id' \
    && ok "#419: ...naming it as two workers sharing one id (case 44)" \
    || bad "#419: the refusal must name the shared-id hazard" "$twin_err"
  printf '%s' "$twin_err" | grep -q '79b9e347' \
    && ok "#419: ...and reporting the OTHER session (79b9e347) (case 44)" \
    || bad "#419: the refusal must report the other session" "$twin_err"
  printf '%s' "$twin_err" | grep -q 'whoami --mint' \
    && ok "#419: ...and offering the mint as the way out (case 44)" \
    || bad "#419: the refusal must offer whoami --mint" "$twin_err"
  # THE ONE THAT MATTERS: the twin's marker is untouched and NO second marker was posted (the refusal is
  # before the CAS's post). Read off the served comment set — the HTTP re-expression of `claims_on 74`.
  ids74="$(tmarkers 74 | jq -r 'sort_by(.id) | map(.id|tostring) | join(",")')"
  [ "$ids74" = "819" ] \
    && ok "#419: ...and the twin's marker is left intact — no second marker posted (case 44)" \
    || bad "#419: the twin's marker must be untouched and alone" "ids=$ids74"

  # --force steals a CONTESTED item; a twin is a broken IDENTITY, not a contested item, so the refusal must
  # SURVIVE --force (forcing would delete a lock our twin is working behind).
  force_err="$(tclaim ed60050b heron-7c2 FS.GG.SDD#74 --force 2>&1 >/dev/null)"; force_rc=$?
  { [ "$force_rc" -ne 0 ] && printf '%s' "$force_err" | grep -q 'two workers share one id'; } \
    && ok "#419: --force does NOT override the twin refusal (case 44)" \
    || bad "#419: --force must not override the twin refusal" "rc=$force_rc err=$force_err"
  ids74f="$(tmarkers 74 | jq -r 'sort_by(.id) | map(.id|tostring) | join(",")')"
  [ "$ids74f" = "819" ] \
    && ok "#419: ...so --force left the twin's marker alone (case 44)" \
    || bad "#419: --force must leave the twin's marker alone" "ids=$ids74f"

  # 4. BACK-COMPAT, the boundary of the rule. We may only conclude "twin" when BOTH sessions are known. A
  #    SESSIONLESS marker (a human, a harness exporting none, any pre-#419 marker) is indistinguishable from
  #    ours — it stays OURS (a successful claim), rather than failing closed and locking a worker out.
  tclaim ed60050b dunlin-9f1 FS.GG.SDD#71 >/dev/null 2>&1; sless_rc=$?
  [ "$sless_rc" -eq 0 ] \
    && ok "#419: a SESSIONLESS marker with our id is still ours — the claim SUCCEEDS, not refused (case 44)" \
    || bad "#419: a sessionless marker with our id must not be refused" "rc=$sless_rc"
  # ...and the SAME session re-claiming its OWN marker is a heartbeat, never a twin — or a worker could
  #    never renew its own claim (the refusal firing on itself).
  tclaim 79b9e347 heron-7c2 FS.GG.SDD#74 >/dev/null 2>&1; same_rc=$?
  [ "$same_rc" -eq 0 ] \
    && ok "#419: the SAME session re-claiming its own marker SUCCEEDS (a heartbeat, not a twin) (case 44)" \
    || bad "#419: same-session re-claim must not be refused as a twin" "rc=$same_rc"
  kill "$TW_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 10 (cache-and-budget) — THE CALL-COUNTING TRANSFORMATION (ADR-0040 §3/§5). The corpus counts `gh`
# invocations against a stub; the engine speaks HTTP, so every "costs N GraphQL calls" assertion is
# re-expressed as an HTTP request count read off the fixture's `/_gql` counter. `bootstrap` costs TWO
# points and DAY-caches the field/option id map; `board`/`field-id`/`option-id` read it for ZERO more;
# `item-id` resolves in ONE and then serves from cache — the #418 win: the budget that dies first is now
# paid once, not once per invocation. One cache dir spans the case, as the corpus's HARNESS_COLD run shares
# one across its assertions.
C10_OUT="$(mktemp)"; python3 "$HERE/cache_server.py" >"$C10_OUT" 2>/dev/null & C10_SRV=$!; C10_PORT=""
for _ in $(seq 1 50); do C10_PORT="$(head -n1 "$C10_OUT" 2>/dev/null)"; [ -n "$C10_PORT" ] && break; sleep 0.1; done
rm -f "$C10_OUT"
if [ -z "$C10_PORT" ]; then bad "cache-and-budget fixture bound a port"; else
  C10CACHE="$(mktemp -d)"
  c10() { env FSGG_GITHUB_API_BASE="http://127.0.0.1:$C10_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$C10CACHE" "$ENGINE" "$@"; }
  gc10() { python3 -c 'import sys,urllib.request,json; print(json.load(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_gql"))["total"])' "$C10_PORT" 2>/dev/null; }
  wr10() { python3 -c 'import sys,urllib.request; print(urllib.request.urlopen("http://127.0.0.1:"+sys.argv[1]+"/_writes").read().decode())' "$C10_PORT" 2>/dev/null; }

  # (1) bootstrap once — exactly TWO GraphQL calls (projects + fields).
  c10 bootstrap >/dev/null 2>&1; brc=$?
  ab="$(gc10)"
  { [ "$brc" -eq 0 ] && [ "$ab" = "2" ]; } \
    && ok "case10: bootstrap costs exactly TWO GraphQL calls, projects + fields (#418)" \
    || bad "case10: bootstrap must exit 0 and cost 2 gql" "rc=$brc gql=$ab"

  # (2) board / field-id / option-id read the day-cache — ZERO further GraphQL.
  board="$(c10 board 2>/dev/null)"
  [ "$(jq -r '.number' <<<"$board")" = "12" ] \
    && ok "case10: board number cached (12)" || bad "case10: board .number" "$board"
  [ "$(jq -r '.id' <<<"$board")" = "PVT_coord" ] \
    && ok "case10: board node id cached (PVT_coord)" || bad "case10: board .id" "$board"
  [ "$(jq -r '.fields.Phase.dataType' <<<"$board")" = "SINGLE_SELECT" ] \
    && ok "case10: Phase is SINGLE_SELECT" || bad "case10: Phase dataType" "$board"
  [ "$(jq -r '.fields.Phase.options["P2 SDD"]' <<<"$board")" = "opt_p2" ] \
    && ok "case10: Phase option id cached (opt_p2)" || bad "case10: Phase option id" "$board"
  [ "$(jq -r '.fields.Target.dataType' <<<"$board")" = "DATE" ] \
    && ok "case10: Target is DATE" || bad "case10: Target dataType" "$board"
  [ "$(c10 field-id Phase 2>/dev/null)" = "PVTSSF_phase" ] \
    && ok "case10: field-id Phase from cache (PVTSSF_phase)" || bad "case10: field-id Phase"
  [ "$(c10 option-id Phase 'P2 SDD' 2>/dev/null)" = "opt_p2" ] \
    && ok "case10: option-id Phase 'P2 SDD' (opt_p2)" || bad "case10: option-id Phase 'P2 SDD'"
  ac="$(gc10)"
  [ "$ac" = "$ab" ] \
    && ok "case10: board/field-id/option-id add ZERO GraphQL calls" \
    || bad "case10: warm reads must add zero gql" "before=$ab after=$ac"

  # (3) item-id: exactly ONE GraphQL call, then served from cache (zero) — across the owner/repo#n and URL
  # spellings of the same issue.
  bi="$(gc10)"
  [ "$(c10 item-id 'FS.GG.SDD#42' 2>/dev/null)" = "PVTI_coord123" ] \
    && ok "case10: item-id resolves the Coordination item (PVTI_coord123)" || bad "case10: item-id resolve"
  ai="$(gc10)"
  [ "$ai" = "$((bi + 1))" ] \
    && ok "case10: item-id costs exactly ONE GraphQL call" || bad "case10: item-id must cost 1" "before=$bi after=$ai"
  c10 item-id 'FS.GG.SDD#42' >/dev/null 2>&1
  [ "$(gc10)" = "$ai" ] \
    && ok "case10: item-id again is served from cache (zero calls)" || bad "case10: item-id must cache" "after=$(gc10)"
  [ "$(c10 item-id 'FS-GG/FS.GG.SDD#42' 2>/dev/null)" = "PVTI_coord123" ] \
    && ok "case10: item-id accepts owner/repo#n form" || bad "case10: item-id owner/repo#n"
  [ "$(c10 item-id 'https://github.com/FS-GG/FS.GG.SDD/issues/42' 2>/dev/null)" = "PVTI_coord123" ] \
    && ok "case10: item-id accepts a full URL" || bad "case10: item-id URL"

  # (4) set-field auto-routes by dataType, ids resolved from cache. Re-expressed at HTTP: the mutation the
  # engine emits carries the right value var (optionId / date / text) on the resolved field id.
  c10 set-field 'FS.GG.SDD#42' Phase 'P2 SDD' --worker smew-c10 >/dev/null 2>&1
  c10 set-field 'FS.GG.SDD#42' Target '2026-08-01' --worker smew-c10 >/dev/null 2>&1
  c10 set-field 'FS.GG.SDD#42' Contract 'fs-gg-ui-template' --worker smew-c10 >/dev/null 2>&1
  writes="$(wr10)"
  echo "$writes" | jq -e '.writes[] | select(.fieldId=="PVTSSF_phase" and .kind=="optionId" and .value=="opt_p2")' >/dev/null \
    && ok "case10: set-field SINGLE_SELECT routes to the resolved option id (opt_p2 on PVTSSF_phase)" || bad "case10: set-field single-select route" "$writes"
  echo "$writes" | jq -e '.writes[] | select(.fieldId=="PVTF_target" and .kind=="date" and .value=="2026-08-01")' >/dev/null \
    && ok "case10: set-field DATE routes to date" || bad "case10: set-field date route" "$writes"
  echo "$writes" | jq -e '.writes[] | select(.fieldId=="PVTF_contract" and .kind=="text" and .value=="fs-gg-ui-template")' >/dev/null \
    && ok "case10: set-field TEXT routes to text" || bad "case10: set-field text route" "$writes"
  c10 set-field 'FS.GG.SDD#42' Contract '' --worker smew-c10 >/dev/null 2>&1
  echo "$(wr10)" | jq -e '.writes[] | select(.fieldId=="PVTF_contract" and .kind=="clear")' >/dev/null \
    && ok "case10: set-field empty value CLEARS via the clear mutation (not an empty set)" || bad "case10: set-field clear route" "$(wr10)"

  # (5) --refresh drops the day-cache and re-resolves — bootstrap pays the two points again.
  br="$(gc10)"
  c10 bootstrap --refresh >/dev/null 2>&1
  ar="$(gc10)"
  [ "$ar" = "$((br + 2))" ] \
    && ok "case10: bootstrap --refresh drops the cache and re-resolves (2 more gql)" \
    || bad "case10: --refresh must re-resolve" "before=$br after=$ar"

  kill "$C10_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 14 (no-touch-set-and-done) — the `lint` SCHEDULABILITY rules (#496). NO-TOUCH-SET / BAD-TOUCH-SET
# are the rule whose absence let `lint` report `0 error(s)` over a DEAD queue: a Ready/Backlog OPEN issue
# no worker can ever pick up (no `Paths:`, or every token unmatchable) is an error — while the `Paths:
# none` sentinel, a fenced-only declaration (#277), a real touch-set, an In progress item, and a closed
# one are all clean. The epic ROLL-UP-graph rules (EPIC-*, DONE-STATUS, EPIC-UNLINKED-CHILD) + the
# done --flip rollup are a later slice — this ports case 14's NO-TOUCH-SET block (lines 16-57).
LINT_OUT="$(mktemp)"; python3 "$HERE/lint_server.py" >"$LINT_OUT" 2>/dev/null & LINT_SRV=$!; LINT_PORT=""
for _ in $(seq 1 50); do LINT_PORT="$(head -n1 "$LINT_OUT" 2>/dev/null)"; [ -n "$LINT_PORT" ] && break; sleep 0.1; done
rm -f "$LINT_OUT"
if [ -z "$LINT_PORT" ]; then bad "lint fixture bound a port"; else
  lt() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$LINT_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" lint "$@"; }
  ljson="$(lt --json 2>/dev/null)"
  nts() { jq -r "[.[] | select(.code==\"NO-TOUCH-SET\") | .id | sub(\"^[^/]+/\";\"\")] | sort | join(\",\")" <<<"$ljson"; }

  # NO-TOUCH-SET fires on EXACTLY the unschedulable items — #420 (no Paths) and #421 (fenced-only, #277).
  [ "$(nts)" = "FS.GG.SDD#420,FS.GG.SDD#421" ] \
    && ok "case14: NO-TOUCH-SET fires on EXACTLY the unschedulable items (#420, #421)" \
    || bad "case14: NO-TOUCH-SET must fire on exactly 420,421" "$(nts)"
  d420="$(jq -r '.[] | select(.code=="NO-TOUCH-SET" and (.id|test("420"))) | .detail' <<<"$ljson")"
  printf '%s' "$d420" | grep -q 'no worker can ever pick it up' \
    && ok "case14: NO-TOUCH-SET says nobody can ever pick it up (#420)" \
    || bad "case14: NO-TOUCH-SET detail must say nobody can pick it up" "$d420"
  printf '%s' "$d420" | grep -q 'Paths: none' \
    && ok "case14: NO-TOUCH-SET offers the 'Paths: none' sentinel by name" \
    || bad "case14: NO-TOUCH-SET must offer the sentinel" "$d420"
  case "$(nts)" in *421*) ok "case14: a FENCED-only Paths: line is no declaration at all (fails closed, #277)" ;;
                   *)     bad "case14: a fenced-only Paths: must still be NO-TOUCH-SET" "$(nts)" ;; esac

  # The negatives — the rule must NOT fire on any of these, or a gate always-red is a gate nobody reads.
  case "$(nts)" in *400*) bad "case14: must NOT fire on an epic declaring 'Paths: none'" "$(nts)" ;;
                   *)     ok "case14: 'Paths: none' suppresses NO-TOUCH-SET — the sentinel is the whole point" ;; esac
  case "$(nts)" in *422*) bad "case14: must NOT fire on a decision item declaring 'Paths: none'" "$(nts)" ;;
                   *)     ok "case14: a decision item declaring 'Paths: none' is clean" ;; esac
  case "$(nts)" in *407*) bad "case14: must NOT fire on an item with a real Paths: line" "$(nts)" ;;
                   *)     ok "case14: an item with a real touch-set is clean" ;; esac
  case "$(nts)" in *423*) bad "case14: must NOT fire on an In progress item" "$(nts)" ;;
                   *)     ok "case14: NO-TOUCH-SET is scoped to Ready/Backlog — not items in flight" ;; esac
  case "$(nts)" in *424*) bad "case14: must NOT fire on a CLOSED issue" "$(nts)" ;;
                   *)     ok "case14: NO-TOUCH-SET does not fire on a closed issue" ;; esac

  # BAD-TOUCH-SET (#496, reopened for the unmatchable case): a declared touch-set the scheduler cannot use
  # is just as dead. #430 declares only `**/only-unmatchable` (ALL unmatchable); #431 declares a real subtree
  # AND `**/nope-unmatchable` (SOME unmatchable, #646) — which lint used to stay green over.
  bts="$(jq -r '[.[] | select(.code=="BAD-TOUCH-SET") | .id | sub("^[^/]+/";"")] | sort | join(",")' <<<"$ljson")"
  [ "$bts" = "FS.GG.SDD#430,FS.GG.SDD#431" ] \
    && ok "#646: BAD-TOUCH-SET fires on the ALL-unmatchable item (#430) AND the PARTIAL one (#431)" \
    || bad "#646: BAD-TOUCH-SET must fire on exactly 430,431" "$bts"
  d430="$(jq -r '.[] | select(.code=="BAD-TOUCH-SET" and (.id|test("430"))) | .detail' <<<"$ljson")"
  printf '%s' "$d430" | grep -q 'only-unmatchable' \
    && ok "case14: BAD-TOUCH-SET names the unmatchable token (#430)" \
    || bad "case14: BAD-TOUCH-SET must name the token" "$d430"
  printf '%s' "$d430" | grep -q 'no worker can ever pick this up' \
    && ok "case14: the ALL-unmatchable detail says nobody can ever pick it up (#430)" \
    || bad "case14: BAD-TOUCH-SET detail" "$d430"

  # #646 — the PARTIAL item: names ONLY the unmatchable token, NOT the matchable subtree, and says why a
  # partial declaration is worse (the silent-reservation double-book).
  d431="$(jq -r '.[] | select(.code=="BAD-TOUCH-SET" and (.id|test("431"))) | .detail' <<<"$ljson")"
  printf '%s' "$d431" | grep -q 'nope-unmatchable' \
    && ok "#646: the PARTIAL item's detail names the unmatchable token (#431)" \
    || bad "#646: partial BAD-TOUCH-SET must name the offending token" "$d431"
  printf '%s' "$d431" | grep -q 'src/Partial' \
    && bad "#646: the partial detail must NOT flag the MATCHABLE token — only the offending subset" "$d431" \
    || ok "#646: ...and does NOT flag the matchable subtree (src/Partial/**) — only the offending subset"
  printf '%s' "$d431" | grep -qi 'invisible to every other worker' \
    && ok "#646: ...and explains WHY a partial declaration is worse (silent reservation → double-book)" \
    || bad "#646: partial detail must explain the silent-reservation risk" "$d431"

  # The --json scratch field of the (deferred) EPIC-UNLINKED-CHILD rule must never leak — a finding schema
  # with an `unlinked` key is the internal probe list, not the contract.
  [ "$(jq -r '[.[] | select(has("unlinked"))] | length' <<<"$ljson")" = "0" ] \
    && ok "case14: no scratch field leaks into --json (schema is code/severity/id/status/url/detail)" \
    || bad "case14: --json must not expose an 'unlinked' scratch field" "$ljson"

  # The text projection: `FSGG-LINT <SEV>  <CODE>  <short-id>  — <detail>` (owner stripped from the id).
  ltext="$(lt 2>/dev/null)"
  printf '%s' "$ltext" | grep -q '^FSGG-LINT ERROR  NO-TOUCH-SET  FS.GG.SDD#420  — ' \
    && ok "case14: the text line is 'FSGG-LINT <SEV>  <CODE>  <short-id>  — <detail>'" \
    || bad "case14: text line format" "$ltext"

  # Exit codes: a board with errors fails the gate (1); a clean repo scope passes (0). --repo resolves a
  # short-id, and scopes the scan.
  lt >/dev/null 2>&1; erc=$?
  [ "$erc" -eq 1 ] && ok "case14: lint over a board with errors fails the gate (exit 1)" || bad "case14: errors must exit 1" "rc=$erc"
  lt --repo sdd >/dev/null 2>&1; src=$?
  [ "$src" -eq 1 ] && ok "case14: lint --repo sdd (has errors) exits 1 — the short-id resolves and scopes" || bad "case14: --repo sdd exit" "rc=$src"
  lt --repo game >/dev/null 2>&1; grc=$?
  [ "$grc" -eq 0 ] && ok "case14: lint --repo game (clean) exits 0 — scoped away from the SDD errors" || bad "case14: --repo game exit" "rc=$grc"
  # game's clean scope really is scanned (not just empty): its one Ready item declares a real touch-set.
  [ "$(lt --repo game --json 2>/dev/null | jq -c '.')" = "[]" ] \
    && ok "case14: ...and that clean scope produced zero findings (a real item, really scanned)" \
    || bad "case14: --repo game must be clean+empty" "$(lt --repo game --json 2>/dev/null)"

  kill "$LINT_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 14 (no-touch-set-and-done) — the `lint` EPIC-ROLL-UP-GRAPH rules (#325/#346/#266/#235). An [epic]
# with zero sub-issues (EPIC-NO-CHILDREN); a TRUNCATED child list rollup cannot verify
# (EPIC-CHILDREN-TRUNCATED); a board-Done epic over an open child (EPIC-DONE-OPEN-CHILD); a board-Done
# issue still open (DONE-STATUS-OPEN-ISSUE, a note); and an epic whose BODY declares a child the graph does
# not contain (EPIC-UNLINKED-CHILD) — with a body-cited PR ref DROPPED (#346), a prose mention ignored, and
# an unresolvable ref KEPT (fail closed, #266).
LE_OUT="$(mktemp)"; python3 "$HERE/lintepic_server.py" >"$LE_OUT" 2>/dev/null & LE_SRV=$!; LE_PORT=""
for _ in $(seq 1 50); do LE_PORT="$(head -n1 "$LE_OUT" 2>/dev/null)"; [ -n "$LE_PORT" ] && break; sleep 0.1; done
rm -f "$LE_OUT"
if [ -z "$LE_PORT" ]; then bad "lint-epic fixture bound a port"; else
  le() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$LE_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" lint "$@"; }
  lej="$(le --json 2>/dev/null)"
  codeids() { jq -r "[.[] | select(.code==\"$1\") | .id | sub(\"^[^/]+/\";\"\")] | sort | join(\",\")" <<<"$lej"; }

  [ "$(codeids EPIC-NO-CHILDREN)" = "FS.GG.SDD#440" ] \
    && ok "case14: EPIC-NO-CHILDREN fires on an open [epic] with zero sub-issues (#440)" \
    || bad "case14: EPIC-NO-CHILDREN" "$(codeids EPIC-NO-CHILDREN)"
  [ "$(codeids EPIC-CHILDREN-TRUNCATED)" = "FS.GG.Rendering#404" ] \
    && ok "case14: EPIC-CHILDREN-TRUNCATED fires on the epic whose graph is truncated (#404)" \
    || bad "case14: EPIC-CHILDREN-TRUNCATED" "$(codeids EPIC-CHILDREN-TRUNCATED)"
  jq -r '.[] | select(.code=="EPIC-CHILDREN-TRUNCATED") | .detail' <<<"$lej" | grep -q '5 sub-issues, only 2 visible' \
    && ok "case14: ...and names the total vs the visible count (5 sub-issues, only 2 visible)" \
    || bad "case14: truncated detail" "$(jq -r '.[]|select(.code=="EPIC-CHILDREN-TRUNCATED")|.detail' <<<"$lej")"
  [ "$(codeids EPIC-DONE-OPEN-CHILD)" = "FS.GG.SDD#450" ] \
    && ok "case14: EPIC-DONE-OPEN-CHILD fires on a board-Done epic over an open child (#450)" \
    || bad "case14: EPIC-DONE-OPEN-CHILD" "$(codeids EPIC-DONE-OPEN-CHILD)"
  jq -r '.[] | select(.code=="EPIC-DONE-OPEN-CHILD") | .detail' <<<"$lej" | grep -q 'FS.GG.SDD#451' \
    && ok "case14: ...and names the open child (#451)" \
    || bad "case14: done-open-child names the child" "$(jq -r '.[]|select(.code=="EPIC-DONE-OPEN-CHILD")|.detail' <<<"$lej")"
  # DONE-STATUS-OPEN-ISSUE (note): board Done but the issue is still open — the epic #450 and the plain #460.
  [ "$(codeids DONE-STATUS-OPEN-ISSUE)" = "FS.GG.Game#480,FS.GG.SDD#450,FS.GG.SDD#460" ] \
    && ok "case14: DONE-STATUS-OPEN-ISSUE (note) fires on every board-Done-but-open issue (#450, #460, #480)" \
    || bad "case14: DONE-STATUS-OPEN-ISSUE" "$(codeids DONE-STATUS-OPEN-ISSUE)"

  # EPIC-UNLINKED-CHILD (#325): #409's body declares #414 (unlinked), PR #418 (dropped, #346), and #413
  # (linked); prose #415 is not a child. The graph {#413} is complete, so the rule may reason.
  [ "$(codeids EPIC-UNLINKED-CHILD)" = "FS.GG.SDD#409" ] \
    && ok "case14: EPIC-UNLINKED-CHILD fires on exactly the epic that has one (#409)" \
    || bad "case14: EPIC-UNLINKED-CHILD" "$(codeids EPIC-UNLINKED-CHILD)"
  unamed="$(jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' <<<"$lej" | sed 's/.*rollup cannot see them: //')"
  [ "$unamed" = "FS.GG.SDD#414" ] \
    && ok "case14: ...and names the declared-but-unlinked child, and ONLY it (#414)" \
    || bad "case14: unlinked names exactly #414" "$unamed"
  [ "$(jq -r '[.[] | select(.detail|test("415|#413|418"))] | length' <<<"$lej")" = "0" ] \
    && ok "case14: a PR ref is dropped (#346), a prose mention and a linked child are not unlinked" \
    || bad "case14: #415/#413/#418 must not appear as unlinked" "$lej"
  # A truncated epic yields NO unlinked-child verdict — "unlinked" is unknowable over an incomplete set.
  [ "$(jq -r '[.[] | select(.code=="EPIC-UNLINKED-CHILD" and (.id|test("404")))] | length' <<<"$lej")" = "0" ] \
    && ok "case14: a truncated epic (#404) yields no unlinked-child verdict (#266)" \
    || bad "case14: truncated epic must not fire unlinked" "$lej"
  # The internal PR-probe scratch list must not leak into the schema.
  [ "$(jq -r '[.[] | select(has("unlinked"))] | length' <<<"$lej")" = "0" ] \
    && ok "case14: the PR-probe scratch field is not exposed in --json" \
    || bad "case14: no 'unlinked' scratch field" "$lej"
  # #470 is a healthy epic (its one declared child is linked) — the negative control.
  [ "$(jq -r '[.[] | select(.id|test("470"))] | length' <<<"$lej")" = "0" ] \
    && ok "case14: a healthy epic (linked children, complete graph) yields nothing (#470)" \
    || bad "case14: #470 must be clean" "$lej"

  # Fail closed (#266): a ref the PR-probe cannot resolve is KEPT, never dropped. Force #414's probe to 502.
  lefc_out="$(mktemp)"; FSGG_PARITY_FAIL_ISSUE=414 python3 "$HERE/lintepic_server.py" >"$lefc_out" 2>/dev/null & LEFC=$!; LEFC_PORT=""
  for _ in $(seq 1 50); do LEFC_PORT="$(head -n1 "$lefc_out" 2>/dev/null)"; [ -n "$LEFC_PORT" ] && break; sleep 0.1; done
  rm -f "$lefc_out"
  if [ -z "$LEFC_PORT" ]; then bad "lint-epic fail-closed fixture bound a port"; else
    fcnamed="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$LEFC_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
                 FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" lint --json 2>/dev/null \
               | jq -r '.[] | select(.code=="EPIC-UNLINKED-CHILD") | .detail' | sed 's/.*rollup cannot see them: //')"
    [ "$fcnamed" = "FS.GG.SDD#414" ] \
      && ok "case14: an unresolvable PR-probe ref is KEPT, not silently dropped (fail closed, #266)" \
      || bad "case14: fail-closed keep" "$fcnamed"
    kill "$LEFC" 2>/dev/null
  fi

  # --repo scopes the scan: only #404's finding is in Rendering; a note-only Game scope passes, and
  # --strict makes that note fatal.
  [ "$(le --repo rendering --json 2>/dev/null | jq -r '[.[].code] | unique | join(",")')" = "EPIC-CHILDREN-TRUNCATED" ] \
    && ok "case14: --repo rendering scopes to only #404's finding (EPIC-CHILDREN-TRUNCATED)" \
    || bad "case14: --repo rendering scope" "$(le --repo rendering --json 2>/dev/null)"
  le --repo game >/dev/null 2>&1; gnrc=$?
  [ "$gnrc" -eq 0 ] && ok "case14: lint --repo game (a note, no error) passes the gate (exit 0)" || bad "case14: note-only exit 0" "rc=$gnrc"
  le --repo game --strict >/dev/null 2>&1; gsrc=$?
  [ "$gsrc" -eq 1 ] && ok "case14: lint --repo game --strict makes the note fatal (exit 1)" || bad "case14: --strict note fatal" "rc=$gsrc"

  kill "$LE_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 14 (no-touch-set-and-done) — the `done --flip` EPIC ROLLUP (#235/#583/#325/#346). Stamping a child
# with --flip climbs to its parent and rolls it up ONLY when genuinely finished: HOLDS while a sibling is
# open (#235/#583), FLIPS when every child is Done + closed, REFUSES when the epic BODY declares a child
# the sub-issue graph does not contain (#325) — the EpicBody/subIssues check landed for lint, now reused —
# while a body-cited PR ref does NOT block the flip (#346). bash's "1/2 children Done+closed — holding"
# wording is re-expressed (ADR-0040 §5) as the engine's own rollup rendering: the PROPERTY (held vs flipped,
# names the blocker) is what is held.
DF_OUT="$(mktemp)"; python3 "$HERE/doneflip_server.py" >"$DF_OUT" 2>/dev/null & DF_SRV=$!; DF_PORT=""
for _ in $(seq 1 50); do DF_PORT="$(head -n1 "$DF_OUT" 2>/dev/null)"; [ -n "$DF_PORT" ] && break; sleep 0.1; done
rm -f "$DF_OUT"
if [ -z "$DF_PORT" ]; then bad "done-flip fixture bound a port"; else
  df() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$DF_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "done" "$@" 2>&1; }

  # A — HOLD (#235/#583): #42 stamps DONE, but its parent #301 is HELD because sibling #44 is still open.
  a="$(df 'FS.GG.SDD#42' --worker w-df --flip)"
  printf '%s' "$a" | grep -q 'FSGG-DONE   FS.GG.SDD#42' \
    && ok "case14: done --flip stamps the child DONE (#42)" || bad "case14: child not stamped" "$a"
  printf '%s' "$a" | grep -q 'FS.GG.SDD#301 left OPEN' \
    && ok "case14: rollup HOLDS the parent while a sibling is open (#235/#583)" || bad "case14: parent must hold" "$a"
  printf '%s' "$a" | grep -q 'still OPEN: #44' \
    && ok "case14: ...and names the open sibling (#44)" || bad "case14: names open sibling" "$a"
  printf '%s' "$a" | grep -q '301 stamped Done and closed' \
    && bad "case14: a held parent must NOT be flipped" "$a" || ok "case14: a held parent is not flipped"

  # B — FLIP: #62 stamps DONE and its parent #302 flips (every child Done + closed, body clean).
  b="$(df 'FS.GG.SDD#62' --worker w-df --flip)"
  { printf '%s' "$b" | grep -q 'FSGG-DONE   FS.GG.SDD#62' && printf '%s' "$b" | grep -q 'FS.GG.SDD#302 stamped Done and closed'; } \
    && ok "case14: rollup FLIPS when every child is Done + closed (parent stamped Done AND closed, #613)" \
    || bad "case14: parent must flip" "$b"

  # C — REFUSE (#325): #72 stamps DONE, but #303's BODY declares #74, absent from the graph -> held, named.
  c="$(df 'FS.GG.SDD#72' --worker w-df --flip)"
  printf '%s' "$c" | grep -q 'FSGG-DONE   FS.GG.SDD#72' \
    && ok "case14: the child still stamps DONE even when the parent will refuse (#72)" || bad "case14: child stamp" "$c"
  printf '%s' "$c" | grep -q 'FS.GG.SDD#303 left OPEN' \
    && ok "case14: rollup REFUSES when the body declares an unlinked child (#325)" || bad "case14: must refuse" "$c"
  printf '%s' "$c" | grep -q 'does not contain: FS.GG.SDD#74' \
    && ok "case14: ...and names the unlinked child (#74)" || bad "case14: names unlinked child" "$c"
  printf '%s' "$c" | grep -q 'fsgg-coord child' \
    && ok "case14: ...and points at the verb that fixes it (fsgg-coord child)" || bad "case14: points at child verb" "$c"
  printf '%s' "$c" | grep -q '303 stamped Done and closed' \
    && bad "case14: a body-unlinked parent must NOT be flipped" "$c" || ok "case14: a body-unlinked parent is not flipped"

  # D — FLIP over a body-cited PR ref (#346): #304's body cites PR #920, which is not an unlinked child.
  d="$(df 'FS.GG.SDD#82' --worker w-df --flip)"
  printf '%s' "$d" | grep -q 'FS.GG.SDD#304 stamped Done and closed' \
    && ok "case14: rollup FLIPS over a body-cited PR ref — a PR is not an unlinked child (#346)" || bad "case14: PR ref must not block flip" "$d"
  printf '%s' "$d" | grep -q 'does not contain' \
    && bad "case14: a body-cited PR must not read as an unlinked child" "$d" || ok "case14: a body-cited PR does not block the rollup"

  # E — #614: the SAME only-child #62/#302 world as leg B, but the child is declared a PARTIAL fix with
  #     `--partial`. The roll-up must LEAVE #302 OPEN (naming why), not close it on the strength of "all
  #     children are done". This is the exact bug #614 names: an only child that was a partial fix closed
  #     its open parent, because the roll-up assumed children partition the parent. Leg B (bare --flip)
  #     remains the positive control that a completing child still closes the parent.
  e="$(df 'FS.GG.SDD#62' --worker w-df --flip --partial 'the API rename landed; migrating the callers is a separate child')"
  printf '%s' "$e" | grep -q 'FSGG-DONE   FS.GG.SDD#62' \
    && ok "#614: done --flip --partial still stamps the child DONE (#62)" || bad "#614: child not stamped under --partial" "$e"
  printf '%s' "$e" | grep -q 'FS.GG.SDD#302 left OPEN' \
    && ok "#614: ...but --partial LEAVES THE PARENT OPEN — a partial child does not discharge its parent" || bad "#614: parent must stay open under --partial" "$e"
  printf '%s' "$e" | grep -q '302 stamped Done and closed' \
    && bad "#614: --partial must NOT close the parent (the exact #614 bug)" "$e" || ok "#614: ...and the parent is NOT stamped Done and closed"
  printf '%s' "$e" | grep -q 'migrating the callers is a separate child' \
    && ok "#614: ...naming WHY it is partial, so the left-open parent is explained" || bad "#614: names the partial reason" "$e"

  kill "$DF_SRV" 2>/dev/null
fi

# case 14 (no-touch-set-and-done) — the `done` PR-PROVENANCE legs (#342/#558/#543). With no `--pr`, `done`
# stamps the PR that ACTUALLY closed the issue — the LATEST-merged among true closers — and refuses a mere
# prose mention; a commit-subject keyword (routed to the PR title, where `closingIssuesReferences` never
# looks) is rescued by GitHub's own CLOSED_EVENT; a commit closer resolves through to its PR; and `--pr`
# overrides WHICH pull request the stamp names, never WHETHER it closed the issue. `closedByPullRequestsReferences`
# is a SUPERSET (mentions too, lowest-number-first), so the engine keeps the whole set and decides the closer
# from ClosesThis / the close event — the #342 fix, re-expressed over HTTP.
#
# DISPOSED ON THE RECORD (ADR-0040 §5): bash exits 1 on a red NOT-DONE; the engine's certified exit for a Red
# verdict is ExitRed=3 (see Program.fs). The corpus's "with a non-zero exit" assertions are re-expressed as the
# PROPERTY — a refused stamp exits NON-ZERO — not bash's literal 1.
DP_OUT="$(mktemp)"; python3 "$HERE/doneprov_server.py" >"$DP_OUT" 2>/dev/null & DP_SRV=$!; DP_PORT=""
for _ in $(seq 1 50); do DP_PORT="$(head -n1 "$DP_OUT" 2>/dev/null)"; [ -n "$DP_PORT" ] && break; sleep 0.1; done
rm -f "$DP_OUT"
if [ -z "$DP_PORT" ]; then bad "done-provenance fixture bound a port"; else
  dp() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$DP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "done" "$@" 2>&1; }

  # #342 — the stamp names the CLOSER (#92 @ 09c836e), never the earlier prose mention #85.
  p84="$(dp 'FS.GG.SDD#84' --worker w-dp)"; rc84=$?
  { [ "$rc84" -eq 0 ] && printf '%s' "$p84" | grep -q 'FSGG-DONE   FS.GG.SDD#84'; } \
    && ok "case14: done stamps the closer, green (#84)" || bad "case14: #84 should be DONE" "rc=$rc84: $p84"
  printf '%s' "$p84" | grep -q 'merged PR #92 @ 09c836e' \
    && ok "case14: ...naming the PR that CLOSED it — #92 @ 09c836e (#342)" || bad "case14: #84 names the closer" "$p84"
  printf '%s' "$p84" | grep -qE '#85|410843e' \
    && bad "case14: the earlier MENTION #85 must not be stamped (#342)" "$p84" \
    || ok "case14: ...and never the earlier mention #85/410843e (#342)"

  # #342 — a merged PR that merely MENTIONS the issue (its body names another) closes nothing.
  p86="$(dp 'FS.GG.SDD#86' --worker w-dp)"; rc86=$?
  printf '%s' "$p86" | grep -qE 'FSGG-NOT-DONE +FS.GG.SDD#86' \
    && ok "case14: done REFUSES when only a mention exists — red NOT-DONE (#86, #342)" || bad "case14: #86 should refuse" "$p86"
  printf '%s' "$p86" | grep -q 'no merged PR closes this issue' \
    && ok "case14: ...saying no merged PR closes this issue (#342)" || bad "case14: #86 reason" "$p86"
  [ "$rc86" -ne 0 ] \
    && ok "case14: ...and exits NON-ZERO (engine ExitRed=3; bash's literal 1 disposed on the record)" || bad "case14: #86 non-zero exit" "rc=$rc86"

  # #342 — among two TRUE closers, the LATEST-merged wins, not the lowest-numbered.
  p88="$(dp 'FS.GG.SDD#88' --worker w-dp)"
  printf '%s' "$p88" | grep -q 'merged PR #95 @ 2222bbb' \
    && ok "case14: among two closers, the LATEST-merged wins — #95 @ 2222bbb (#342)" || bad "case14: #88 latest-merged" "$p88"
  printf '%s' "$p88" | grep -qE '#89|1111aaa' \
    && bad "case14: the earlier-merged, lower-numbered #89 must not be stamped (#342)" "$p88" \
    || ok "case14: ...and never the earlier-merged, lower-numbered #89/1111aaa (#342)"

  # #558 — a keyword in the commit SUBJECT still earns the stamp (GitHub's own CLOSED_EVENT closer).
  p165="$(dp 'FS.GG.SDD#165' --worker w-dp --flip)"; rc165=$?
  { [ "$rc165" -eq 0 ] && printf '%s' "$p165" | grep -q 'FSGG-DONE   FS.GG.SDD#165'; } \
    && ok "case14: a commit-SUBJECT keyword still earns the stamp — the CLOSED_EVENT closer (#558)" \
    || bad "case14: #165 should be DONE via the close event" "rc=$rc165: $p165"

  # #558 — a COMMIT closer (a squash) resolves through to its associated PR.
  p166="$(dp 'FS.GG.SDD#166' --worker w-dp --flip)"
  printf '%s' "$p166" | grep -q 'FSGG-DONE   FS.GG.SDD#166' \
    && ok "case14: a COMMIT closer resolves through to its PR (#558)" || bad "case14: #166 commit closer" "$p166"

  # #928 — THE SHAPE GITHUB ACTUALLY RETURNS. #165/#166 above both LIST the PR; GitHub does not, when the
  # PR's body never named the issue. With the list EMPTY the close event is the only record — and leg (B)
  # was a FILTER over that list, so it could never fire. This is #558's own case, and it stamped red for
  # #558's whole life (measured on .github#622 / PR #926).
  p167="$(dp 'FS.GG.SDD#167' --worker w-dp --flip)"; rc167=$?
  { [ "$rc167" -eq 0 ] && printf '%s' "$p167" | grep -q 'FSGG-DONE   FS.GG.SDD#167'; } \
    && ok "case14: a closer named ONLY by the close event still earns the stamp (#928)" \
    || bad "case14: #167 should be DONE — the CLOSED_EVENT closer must ENTER the candidate set" "rc=$rc167: $p167"
  printf '%s' "$p167" | grep -q 'merged PR #926 @ 4cf06e1' \
    && ok "case14: ...naming the PR the event resolved through to — #926 @ 4cf06e1 (#928)" \
    || bad "case14: #167 must name the closer the event named" "$p167"

  # #928 — ...and the union may not launder an UNMERGED PR that merely contains the closing commit.
  p168="$(dp 'FS.GG.SDD#168' --worker w-dp)"; rc168=$?
  printf '%s' "$p168" | grep -qE 'FSGG-NOT-DONE +FS.GG.SDD#168' \
    && ok "case14: an UNMERGED closer named by the event closes nothing (#928/#543)" \
    || bad "case14: #168 should refuse — an unmerged PR has landed no work" "rc=$rc168: $p168"
  printf '%s' "$p168" | grep -q '#927' \
    && ok "case14: ...and the refusal NAMES it, not 'names no PR or commit' (#928/#266)" \
    || bad "case14: #168 refusal must describe the subject it read" "$p168"
  printf '%s' "$p168" | grep -q 'names no PR or commit' \
    && bad "case14: the refusal must not claim the event named nobody — it named #927 (#928)" "$p168" \
    || ok "case14: ...and never claims the close event named nobody when it did (#928)"

  # #543 — `--pr` may not launder a mention: PR 97 closes #70, not #96.
  p96="$(dp 'FS.GG.SDD#96' --pr 97 --flip --worker w-dp)"; rc96=$?
  printf '%s' "$p96" | grep -qE 'FSGG-NOT-DONE +FS.GG.SDD#96' \
    && ok "case14: --pr REFUSES a PR that only MENTIONS the issue (#543)" || bad "case14: #96 --pr should refuse" "$p96"
  case "$p96" in
    *FSGG-DONE*) bad "case14: --pr must not be an override of PROVENANCE (#543)" "$p96" ;;
    *) ok "case14: --pr overrides WHICH pr, never WHETHER it closed the issue (#543)" ;;
  esac
  [ "$rc96" -ne 0 ] \
    && ok "case14: ...and exits NON-ZERO (engine ExitRed=3; bash's literal 1 disposed on the record)" || bad "case14: #96 non-zero exit" "rc=$rc96"

  # ---- #600's NO-PR GREEN PATH (#1028) ---------------------------------------------------------------
  #
  # `done --evidence` is the ONE green path that stamps an item Done with no merged PR to anchor it, and it
  # had no parity case (#839 residual 3 of 4). Every other green verdict is anchored to a fact GitHub
  # records — a bug in the check is caught by the merge not existing. This one is anchored to a free-text
  # string, so it is the only place `done` can be TALKED INTO a stamp, which is exactly why its refusal
  # legs need pinning.
  #
  # #170 has no closer of either kind, so all three legs below reach the same branch and differ only by the
  # flag under test.

  # #600 — closed, no PR, non-blank evidence => GREEN, naming the evidence.
  p170="$(dp 'FS.GG.SDD#170' --evidence 'obsolete: the scaffold it gated was deleted in #838' --worker w-dp)"; rc170=$?
  { [ "$rc170" -eq 0 ] && printf '%s' "$p170" | grep -q 'FSGG-DONE   FS.GG.SDD#170'; } \
    && ok "#1028: done --evidence stamps work resolved WITHOUT a PR — green (#600)" \
    || bad "#1028: #170 should be DONE on evidence" "rc=$rc170: $p170"
  printf '%s' "$p170" | grep -q 'resolved without a PR: obsolete: the scaffold it gated was deleted in #838' \
    && ok "#1028: ...and the stamp NAMES the evidence it was talked into (#600)" \
    || bad "#1028: #170 must render the evidence" "$p170"

  # #600 — ...and BLANK evidence is refused. A green path that took no argument would not be a stamp; it
  # would be a way of switching the stamp off, reached for by exactly the people it was not meant for.
  p170b="$(dp 'FS.GG.SDD#170' --evidence '' --worker w-dp)"; rc170b=$?
  printf '%s' "$p170b" | grep -qE 'FSGG-NOT-DONE +FS.GG.SDD#170' \
    && ok "#1028: BLANK evidence is REFUSED — red NOT-DONE (#600, Done.fs:172)" \
    || bad "#1028: #170 blank evidence should refuse" "rc=$rc170b: $p170b"
  printf '%s' "$p170b" | grep -q 'the evidence offered for resolving it without one is blank' \
    && ok "#1028: ...saying the evidence is blank, not that no PR closes it (#266)" \
    || bad "#1028: #170 blank-evidence reason" "$p170b"
  [ "$rc170b" -ne 0 ] \
    && ok "#1028: ...and exits NON-ZERO (engine ExitRed=3)" || bad "#1028: #170 blank non-zero exit" "rc=$rc170b"

  # The DISCRIMINATOR: with no --evidence at all, #170 takes the pre-existing refusal. Without this leg the
  # two above merely assert that a flag changes the output — this is what establishes that #170 genuinely
  # has no closer, so the green above is #600's path and not a closer being found.
  p170c="$(dp 'FS.GG.SDD#170' --worker w-dp)"; rc170c=$?
  { [ "$rc170c" -ne 0 ] && printf '%s' "$p170c" | grep -qE 'FSGG-NOT-DONE +FS.GG.SDD#170'; } \
    && ok "#1028: ...and with NO evidence, #170 still reds — the no-closer branch is the one under test" \
    || bad "#1028: #170 bare should refuse" "rc=$rc170c: $p170c"
  printf '%s' "$p170c" | grep -q 'resolved WITHOUT a pull request' \
    && ok "#1028: ...and the refusal POINTS AT the green path rather than dead-ending (#600)" \
    || bad "#1028: #170 bare must name the --evidence remedy" "$p170c"

  # #1028 DECIDES #600's open question: the read path ignores `state_reason`, deliberately. #171 is closed
  # as NOT_PLANNED — the state of an obsolete item or a transplanted duplicate, i.e. exactly the population
  # `--evidence` was built to green. Requiring `completed` here would re-break #600 in the one place it
  # exists to fix. The engine WRITES `state_reason: completed` (Done.fs:529) because that is an ASSERTION
  # that work completed; the read asks whether the item is RESOLVED, and not_planned is a resolution. Two
  # questions, one word. This leg goes red the day someone reads stateReason — which is the point.
  p171="$(dp 'FS.GG.SDD#171' --evidence 'duplicate: detail transplanted into #838' --worker w-dp)"; rc171=$?
  { [ "$rc171" -eq 0 ] && printf '%s' "$p171" | grep -q 'FSGG-DONE   FS.GG.SDD#171'; } \
    && ok "#1028: an issue closed NOT_PLANNED still greens on evidence — state_reason is not read, BY DECISION (#600)" \
    || bad "#1028: #171 not_planned+evidence should be DONE — see doneprov_server.py's docstring before 'fixing' this" "rc=$rc171: $p171"

  kill "$DP_SRV" 2>/dev/null
fi

# case 34 (xrepo-touchset-353) — the `overlap` command (#353). `Paths:` tokens are repo-relative, so a
# touch-set comparison is only meaningful WITHIN a repo. `overlap` scopes to ONE repo: a same-named token in
# ANOTHER repo is not a collision (two files in two repositories), and its holder is never named — while a
# GENUINE same-repo overlap is still caught (scoping narrowed the set, not the test). Two shapes:
# `overlap <ref> --active` (the item vs its repo's live claims) and `overlap <a> <b>` (the two items, or
# DISJOINT-by-construction across a repo boundary). The engine already has `TouchSet.conflicts` repo-scoped
# (case 35); this ports the command surface bash's #353 fixed.
#
# DISPOSED ON THE RECORD (ADR-0040 §5): the OVERLAP exit is the engine's `ExitContended=6`; the corpus's
# `assert_fails` is the PROPERTY (a real overlap exits NON-ZERO), not bash's literal code. `widen`'s
# collision-DETECT-and-notify half (case 34's second block) needs a notify write and is deferred as
# 34-remainder — this slice ports the read-only `overlap` diagnostic.
OV_OUT="$(mktemp)"; python3 "$HERE/overlap_server.py" >"$OV_OUT" 2>/dev/null & OV_SRV=$!; OV_PORT=""
for _ in $(seq 1 50); do OV_PORT="$(head -n1 "$OV_OUT" 2>/dev/null)"; [ -n "$OV_PORT" ] && break; sleep 0.1; done
rm -f "$OV_OUT"
if [ -z "$OV_PORT" ]; then bad "overlap fixture bound a port"; else
  ov() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$OV_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" overlap "$@" 2>&1; }

  # --active: the cross-repo namesake (Rendering#402, same bare token) is NOT a collision.
  oa="$(ov 'FS.GG.SDD#401' --active)"; oarc=$?
  printf '%s' "$oa" | grep -q 'DISJOINT' \
    && ok "case34: overlap --active — a same-named path in ANOTHER repo is not a collision (#353)" || bad "case34: 401 --active should be DISJOINT" "$oa"
  case "$oa" in
    *OVERLAP*|*Rendering*|*402*) bad "case34: overlap --active must never invent a cross-repo overlap (#353)" "$oa" ;;
    *) ok "case34: overlap --active names no cross-repo holder (#353)" ;;
  esac
  [ "$oarc" -eq 0 ] \
    && ok "case34: overlap --active exits 0 when the only namesake claim is in another repo (#353)" || bad "case34: 401 --active exit 0" "rc=$oarc"

  # pairwise: different repos are DISJOINT by construction (no body read, no invented collision).
  op="$(ov 'FS.GG.SDD#401' 'FS-GG/FS.GG.Rendering#402')"; oprc=$?
  printf '%s' "$op" | grep -q 'different repos' \
    && ok "case34: overlap a b — different repos are DISJOINT even on a same-named token (#353)" || bad "case34: cross-repo pair" "$op"
  case "$op" in
    *OVERLAP*) bad "case34: overlap a b must never invent a cross-repo overlap (#353)" "$op" ;;
    *) ok "case34: overlap a b names no cross-repo collision (#353)" ;;
  esac
  [ "$oprc" -eq 0 ] \
    && ok "case34: overlap a b exits 0 for a cross-repo pair (#353)" || bad "case34: cross-repo pair exit 0" "rc=$oprc"

  # POSITIVE CONTROL (pairwise): a REAL same-repo overlap is STILL detected — scoping narrowed the set.
  os="$(ov 'FS.GG.SDD#403' 'FS.GG.SDD#405')"; osrc=$?
  printf '%s' "$os" | grep -q 'OVERLAP' \
    && ok "case34: overlap a b — a real SAME-repo overlap is STILL detected (#353)" || bad "case34: same-repo pair overlap" "$os"
  [ "$osrc" -ne 0 ] \
    && ok "case34: ...and a real same-repo overlap exits NON-ZERO (engine ExitContended=6; bash's code disposed)" || bad "case34: same-repo pair non-zero" "rc=$osrc"

  # POSITIVE CONTROL (--active): a real same-repo LIVE CLAIM is caught, and its holder named.
  oac="$(ov 'FS.GG.SDD#405' --active)"; oacrc=$?
  printf '%s' "$oac" | grep -q 'OVERLAP' \
    && ok "case34: overlap --active — a genuine same-repo live claim is STILL detected (#353)" || bad "case34: 405 --active overlap" "$oac"
  printf '%s' "$oac" | grep -q 'FS.GG.SDD#403' \
    && ok "case34: ...and names the colliding same-repo item (#403)" || bad "case34: names colliding item" "$oac"
  printf '%s' "$oac" | grep -q 'sdd-sib' \
    && ok "case34: ...and names the holder it queues behind (sdd-sib) (#353)" || bad "case34: names holder" "$oac"
  [ "$oacrc" -ne 0 ] \
    && ok "case34: ...and exits NON-ZERO (engine ExitContended=6; bash's code disposed on the record)" || bad "case34: 405 --active non-zero" "rc=$oacrc"

  kill "$OV_SRV" 2>/dev/null
fi

# case 24 leg (k) — `paths_of` FAILS CLOSED. An empty touch-set reads as "disjoint from everything", so a
# failed BODY read (rate limit, network) must NOT be mis-read as "the issue declared nothing" — that is the
# #266 fail-open, one subtree down, which would let the scheduler hand out work overlapping a held item. The
# tell is WHICH diagnosis comes out: a failed read must refuse ("refusing to schedule against an unknown
# touch-set"), never "no 'Paths:' touch-set declared". The engine's `overlap` reads the subject's touch-set
# through `failSchedule`, which swaps the generic IO explain for the scheduler refusal while carrying the
# IoError's own exit code. Re-expressed at the HTTP layer: `OVERLAP_FAIL_ISSUE=403` 500s the body read the
# corpus faults with `GH_FAIL_ISSUE_GET=94`. DISPOSED (ADR-0040 §5): bash's `die` exits 1; the engine keeps
# the read's own code (the corpus greps the SENTENCE, not the literal — `|| true`), and the property is the
# refusal wording, not the exit number.
OVK_OUT="$(mktemp)"; OVERLAP_FAIL_ISSUE=403 python3 "$HERE/overlap_server.py" >"$OVK_OUT" 2>/dev/null & OVK_SRV=$!; OVK_PORT=""
for _ in $(seq 1 50); do OVK_PORT="$(head -n1 "$OVK_OUT" 2>/dev/null)"; [ -n "$OVK_PORT" ] && break; sleep 0.1; done
rm -f "$OVK_OUT"
if [ -z "$OVK_PORT" ]; then bad "overlap fail-closed fixture bound a port"; else
  # `overlap 403 405` — same repo, but 403's body read FAULTS (500) before any comparison can be made.
  ovk="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$OVK_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
            FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
            "$ENGINE" overlap 'FS.GG.SDD#403' 'FS.GG.SDD#405' 2>&1 || true)"
  printf '%s' "$ovk" | grep -q 'refusing to schedule' \
    && ok "case24(k): a failed touch-set read refuses to schedule against an unknown touch-set" \
    || bad "case24(k): failed read must refuse to schedule" "$ovk"
  case "$ovk" in
    *"no 'Paths:' touch-set declared"*|*"declared nothing"*)
      bad "case24(k): a failed read must NOT be diagnosed as 'the issue declared nothing'" "$ovk" ;;
    *) ok "case24(k): a failed read is not mis-diagnosed as an empty declaration (#266 fail-open avoided)" ;;
  esac
  # ...and it never fell through to a DISJOINT — the fail-OPEN that hands out the overlapping tree.
  case "$ovk" in
    *DISJOINT*) bad "case24(k): a failed read must never read as DISJOINT (the fail-open double-book)" "$ovk" ;;
    *) ok "case24(k): a failed read never reads as DISJOINT (fails closed, not open)" ;;
  esac
  kill "$OVK_SRV" 2>/dev/null
fi

# case 34-remainder (xrepo-touchset-353) — `widen`'s collision-DETECT-and-NOTIFY half (#353). The
# read-only `overlap` command (#809) ported the repo-scoped collision COMPUTATION; this is the write half
# ADR-0021 named ("re-declare AND re-check overlap before continuing") and the part a worker cannot do
# alone: after the widen LANDS, re-check the NEW touch-set against the live claims in THIS item's repo, and
# NOTIFY each worker it now collides with, on their own issue. The engine reuses the exact #353 collision
# scan `overlap --active` runs (`activeCollisions`), so scoping cannot drift between the two surfaces.
#
# DISPOSED ON THE RECORD (ADR-0040 §5):
#   - The engine's `widen` requires the widener to HOLD the lock (#706); bash's does not. So #401 carries a
#     kite-t01 claim the corpus fixture omits — an engine STRENGTHENING, not a change to the property under
#     test (verifyHeld's fail-closed refusal is proven on its own elsewhere).
#   - The OVERLAP exit is the engine's ExitContended=6; the corpus's `assert_fails` is the PROPERTY (a real
#     collision exits NON-ZERO), not bash's literal 1.
WN_OUT="$(mktemp)"; python3 "$HERE/widennotify_server.py" >"$WN_OUT" 2>/dev/null & WN_SRV=$!; WN_PORT=""
for _ in $(seq 1 50); do WN_PORT="$(head -n1 "$WN_OUT" 2>/dev/null)"; [ -n "$WN_PORT" ] && break; sleep 0.1; done
rm -f "$WN_OUT"
if [ -z "$WN_PORT" ]; then bad "widen-notify fixture bound a port"; else
  wn() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$WN_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" widen "$@" 2>&1; }

  # CROSS-REPO: widening #401 to the bare token `scripts/fsgg-coord` names a file in THIS repo; the same
  # string on Rendering#402 is a different file, so it is DISJOINT — the widen lands, and the innocent
  # cross-repo holder is NEVER pestered. (Run first, exactly as the corpus does, before the widen below.)
  wx="$(wn 'FS.GG.SDD#401' --worker kite-t01 --paths 'scripts/fsgg-coord')"; wxrc=$?
  printf '%s' "$wx" | grep -q 'widened FS.GG.SDD#401' \
    && ok "case34: widen lands the re-declaration (#353)" || bad "case34: widen should land" "$wx"
  printf '%s' "$wx" | grep -q 'DISJOINT' \
    && ok "case34: widen — a same-named path in ANOTHER repo is not a collision (#353)" || bad "case34: 401 cross-repo widen DISJOINT" "$wx"
  case "$wx" in
    *OVERLAP*|*Rendering*|*402*|*render-x1*) bad "case34: widen must never invent a cross-repo overlap (#353)" "$wx" ;;
    *) ok "case34: widen names no cross-repo holder (#353)" ;;
  esac
  [ "$wxrc" -eq 0 ] \
    && ok "case34: widen exits 0 when the only namesake claim is in another repo (#353)" || bad "case34: cross-repo widen exit 0" "rc=$wxrc"
  # ...and it left the innocent cross-repo bystander UNCOMMENTED (the corpus's before/after count check).
  [ "$(posts_on "$WN_PORT" | jq -r '."402" // 0')" = "0" ] \
    && ok "case34: widen leaves the cross-repo bystander #402 uncommented (#353)" || bad "case34: #402 should have no notify" "$(posts_on "$WN_PORT")"

  # POSITIVE CONTROL — a REAL same-repo overlap is STILL detected: widen #401 to `src/Scene/**`, which #403
  # (held by sdd-sib) already declares. The engine names the collision, NOTIFIES sdd-sib on #403, and exits
  # non-zero. Scoping narrowed the live set, not the test.
  wc="$(wn 'FS.GG.SDD#401' --worker kite-t01 --paths 'src/Scene/**')"; wcrc=$?
  printf '%s' "$wc" | grep -q 'now collides with FS.GG.SDD#403' \
    && ok "case34: widen — a genuine SAME-repo overlap is STILL detected, naming #403 (#353)" || bad "case34: same-repo widen detects #403" "$wc"
  printf '%s' "$wc" | grep -q 'notified worker sdd-sib on FS.GG.SDD#403' \
    && ok "case34: ...and NOTIFIES the same-repo worker sdd-sib on their own issue (#353)" || bad "case34: notify sdd-sib on #403" "$wc"
  [ "$wcrc" -ne 0 ] \
    && ok "case34: ...and a real same-repo collision exits NON-ZERO (engine ExitContended=6; bash's 1 disposed)" || bad "case34: same-repo widen non-zero" "rc=$wcrc"
  # ...and the notify actually landed as ONE comment on #403 (the write half, counted at the HTTP layer).
  [ "$(posts_on "$WN_PORT" | jq -r '."403" // 0')" = "1" ] \
    && ok "case34: the notify lands as exactly one comment on #403 (#353)" || bad "case34: #403 should get one notify" "$(posts_on "$WN_PORT")"

  kill "$WN_SRV" 2>/dev/null
fi

# ---- WIDEN re-checks BEFORE it writes (#523) — an unreadable scan REFUSES, body untouched -----------
# The #523 defect: `widen` PATCHed the declaration and re-checked overlap AFTERWARDS, so on an exhausted
# GraphQL budget the touch-set landed UNVERIFIED and the colliding workers were never told. The fix orders
# the #353 collision scan BEFORE the write and lets its verdict gate the PATCH. This fixture lets the REST
# legs (verifyHeld, issueBody) succeed and then rate-limits the scan's GraphQL: the widen must exit EX_RATE
# and land ZERO writes. The PATCH/POST counts are read back, so a widen that wrote first cannot pass.
WB_OUT="$(mktemp)"; python3 "$HERE/widenbudget_server.py" >"$WB_OUT" 2>/dev/null & WB_SRV=$!; WB_PORT=""
for _ in $(seq 1 50); do WB_PORT="$(head -n1 "$WB_OUT" 2>/dev/null)"; [ -n "$WB_PORT" ] && break; sleep 0.1; done
rm -f "$WB_OUT"
if [ -z "$WB_PORT" ]; then bad "widen-budget fixture bound a port"; else
  wb() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$WB_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" widen "$@" 2>&1; }
  wbout="$(wb 'FS.GG.SDD#401' --worker kite-t01 --paths 'src/new/**')"; wbrc=$?

  [ "$wbrc" -eq 75 ] \
    && ok "#523: widen exits EX_RATE (75) when the pre-PATCH collision scan hits the budget" \
    || bad "#523: widen should exit 75 on an exhausted scan" "rc=$wbrc :: $wbout"
  printf '%s' "$wbout" | grep -qi 'budget' \
    && ok "#523: ...and names the budget, not a protocol error or a lost race" || bad "#523: widen names the budget" "$wbout"
  [ "$(patches_on "$WB_PORT" | jq -r '."401" // 0')" = "0" ] \
    && ok "#523: ...and the body is UNTOUCHED — no PATCH landed before the verdict (the core of #523)" \
    || bad "#523: widen must not PATCH before the scan verdict" "patches: $(patches_on "$WB_PORT")"
  [ "$(posts_on "$WB_PORT" | jq -r 'to_entries | map(.value) | add // 0')" = "0" ] \
    && ok "#523: ...and no notify was posted (there was no verdict to notify from)" \
    || bad "#523: a refused widen must post no notify" "posts: $(posts_on "$WB_PORT")"

  kill "$WB_SRV" 2>/dev/null
fi

# ---- #651: a MARKERLESS item with an open item/<n>-* PR is NOT offered ------------------------------
# #581 read the open-PR proof-of-life only THROUGH a claim marker; a Ready item with NO marker but an open
# `item/<n>-*` PR fell through to Startable and was handed out twice. The board carries #700 (markerless, an
# open PR on item/700-*) and #701 (markerless, no PR): #700 must be skipped as `item-pr-open`, #701 offered.
IP_OUT="$(mktemp)"; python3 "$HERE/itempr_server.py" >"$IP_OUT" 2>/dev/null & IP_SRV=$!; IP_PORT=""
for _ in $(seq 1 50); do IP_PORT="$(head -n1 "$IP_OUT" 2>/dev/null)"; [ -n "$IP_PORT" ] && break; sleep 0.1; done
rm -f "$IP_OUT"
if [ -z "$IP_PORT" ]; then bad "item-pr fixture bound a port"; else
  ipenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$IP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
                FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
                "$ENGINE" "$@"; }

  ipjson="$(ipenv batch --repo FS.GG.SDD --json 2>/dev/null)"
  [ "$ipjson" = '["FS.GG.SDD#701"]' ] \
    && ok "#651: batch offers the markerless-no-PR item (#701) and SKIPS the markerless-open-PR item (#700)" \
    || bad "#651: batch must skip a markerless item with an open item/<n> PR" "got: $ipjson"

  iperr="$(ipenv batch --repo FS.GG.SDD 2>&1 >/dev/null)"
  { printf '%s' "$iperr" | grep 'FS.GG.SDD#700' | grep -qi 'already in flight'; } \
    && ok "#651: ...and the #700 skip names it as an implementation ALREADY IN FLIGHT (its open PR)" \
    || bad "#651: the #700 skip must name the open-PR reason" "$iperr"
  printf '%s' "$iperr" | grep 'FS.GG.SDD#700' | grep -q '812' \
    && ok "#651: ...naming the PR (#812), so the refusal is checkable" \
    || bad "#651: the skip should name the PR number" "$iperr"

  ipnext="$(ipenv next --repo FS.GG.SDD 2>/dev/null)"
  [ "$ipnext" = "FS.GG.SDD#701" ] \
    && ok "#651: next returns #701 — a markerless item with NO PR is STILL startable (the control)" \
    || bad "#651: a markerless-no-PR item must stay startable" "got: $ipnext"

  kill "$IP_SRV" 2>/dev/null
fi

# ---- REAP: an expired lease is EVIDENCE of abandonment, not PROOF (#581) — case 26 ----------------
# The false positive is SYSTEMATIC: work that simply outlasts its lease. bash's reaper broke a lock on
# expiry alone and collected the claims of workers who were visibly still working, TWICE. #581's fix: an
# open PR on the item's own `item/<n>-*` branch is the worktree protocol's own server-side proof of life,
# so `reap` LOOKS for it and REFUSES when one is open. This holds the engine's new `reap` command to case
# 26's certified answers over HTTP: the SAME off-board stale claim (#216, ghost-222, lease lapsed), reaped
# when its work is dead and REFUSED when its PR is open — with the deletes counted at the HTTP layer, so a
# refusal that deleted anyway (the exact #581 bug) cannot pass. `reap_server.py` is that world one
# transport over; `--apply` gates the destructive break, and the bare form is a dry run (case 25).
RP_OUT="$(mktemp)"; python3 "$HERE/reap_server.py" >"$RP_OUT" 2>/dev/null & RP_SRV=$!; RP_PORT=""
for _ in $(seq 1 50); do RP_PORT="$(head -n1 "$RP_OUT" 2>/dev/null)"; [ -n "$RP_PORT" ] && break; sleep 0.1; done
rm -f "$RP_OUT"
if [ -z "$RP_PORT" ]; then bad "reap fixture bound a port"; else
  rp() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$RP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" reap "$@" 2>&1; }

  # DRY RUN (case 25): a bare `reap` reports what --apply WOULD collect and deletes NOTHING. A destructive
  # lock-break is never the default — the operator opts in.
  rdry="$(rp --repo FS.GG.Rendering)"
  printf '%s' "$rdry" | grep -q 'would reap  FS.GG.Rendering#216  worker ghost-222' \
    && ok "reap: a bare reap is a DRY RUN — 'would reap …' names the item and its dead worker (case 25)" \
    || bad "reap dry-run line" "$rdry"
  [ "$(deletes_on "$RP_PORT")" = '[]' ] \
    && ok "reap: ...and the DRY RUN deletes NOTHING (the break is gated behind --apply)" \
    || bad "reap dry-run must not delete" "deletes: $(deletes_on "$RP_PORT")"

  # --apply, DEAD lease (case 26 / case 25): the expired claim with NO open PR is collected — the negative
  # control that keeps the #581 refusal below from being satisfied by a reap that simply stopped working.
  rapp="$(rp --repo FS.GG.Rendering --apply)"
  printf '%s' "$rapp" | grep -q 'reaped  FS.GG.Rendering#216  worker ghost-222' \
    && ok "#581: an expired claim with NO open PR is reaped, naming the item + dead worker (case 26)" \
    || bad "reap --apply reaped line" "$rapp"
  [ "$(deletes_on "$RP_PORT")" = '[880]' ] \
    && ok "#581: ...and the marker (comment 880) is actually DELETED — the lock released (case 26)" \
    || bad "reap --apply must delete the marker" "deletes: $(deletes_on "$RP_PORT")"
  # OFF-BOARD honesty (case 25): #216 is not on the board, and reap must not claim a reset it never did.
  printf '%s' "$rapp" | grep -q 'not on board (marker cleared; nothing to reset)' \
    && ok "reap: an OFF-BOARD claim's reset is 'not on board (nothing to reset)' — none invented (case 25)" \
    || bad "reap off-board reset honesty" "$rapp"

  kill "$RP_SRV" 2>/dev/null
fi

# LIVE-PR server: the SAME lapsed lease, but an OPEN PR #433 on item/216-* — the work is demonstrably alive.
RL_OUT="$(mktemp)"; REAP_LIVE_PR=216:433 python3 "$HERE/reap_server.py" >"$RL_OUT" 2>/dev/null & RL_SRV=$!; RL_PORT=""
for _ in $(seq 1 50); do RL_PORT="$(head -n1 "$RL_OUT" 2>/dev/null)"; [ -n "$RL_PORT" ] && break; sleep 0.1; done
rm -f "$RL_OUT"
if [ -z "$RL_PORT" ]; then bad "reap live-PR fixture bound a port"; else
  rl="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$RL_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
          FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
          "$ENGINE" reap --repo FS.GG.Rendering --apply 2>&1)"
  printf '%s' "$rl" | grep -q 'REFUSING' \
    && ok "#581: reap REFUSES a claim whose PR is open — the lease lapsed, the WORK did not (case 26)" \
    || bad "reap must refuse a live PR" "$rl"
  printf '%s' "$rl" | grep -q '#433' \
    && ok "#581: ...and names the PR (#433), so the refusal is checkable (case 26)" \
    || bad "reap refusal names the PR" "$rl"
  # THE ONE THAT MATTERS: the marker must SURVIVE. A refusal that deleted anyway is the bug #581 is for.
  [ "$(deletes_on "$RL_PORT")" = '[]' ] \
    && ok "#581: ...and the claim SURVIVES — nothing deleted (the leg that reaped live work TWICE) (case 26)" \
    || bad "reap refusal must not delete" "deletes: $(deletes_on "$RL_PORT")"

  kill "$RL_SRV" 2>/dev/null
fi

# ---- REAP SCOPES TO THE CHECKOUT YOU ARE STANDING IN (#480, case 13) — the destructive one ---------
# `reap --apply` is the ONE worker command that DELETES another worker's state, so an org-wide default is
# the worst place to keep one: a janitor run from `.github` would collect claims in five repos it was
# never pointed at. Like `next`/`take`/`batch`/`who`, a bare `reap` takes the repo of the checkout you
# are standing in — read FREE and offline from `git config remote.origin.url` — and considers ONLY that
# repo's claims; an explicit `--repo` wins; OUTSIDE a checkout it REFUSES rather than scan the whole org.
# The corpus (case 13, line 54) asserts this on the DRY RUN: a bare reap from an SDD checkout must NOT
# name a Templates/Rendering/… claim. This drives the ENGINE from FAKE CHECKOUTS against a MULTI-REPO
# world (`reap_scope_server.py` — a dead stale claim in SDD AND Rendering), so a leak is visible two ways:
# the dry-run line names the checkout's repo, and the `/_requests` ledger shows which repo's `/issues`
# was fetched — the corpus's "considers only THAT repo's claims" (`gh`-counted) one transport under.
RS_OUT="$(mktemp)"; RS_CACHE="$(mktemp -d)"; RSCO="$(mktemp -d)"
python3 "$HERE/reap_scope_server.py" >"$RS_OUT" 2>/dev/null & RS_SRV=$!
RS_PORT=""; for _ in $(seq 1 50); do RS_PORT="$(head -n1 "$RS_OUT" 2>/dev/null)"; [ -n "$RS_PORT" ] && break; sleep 0.1; done
if [ -z "$RS_PORT" ]; then
  bad "#480: reap scope fixture bound a port"
else
  # Fake checkouts — the git remote is the ONLY signal the scope reads. Two URL forms (https, ssh) prove
  # the parser handles both; `nogit` is deliberately NOT a git repo (an undetectable scope).
  mkdir -p "$RSCO/sdd"   && git -C "$RSCO/sdd"   init -q && git -C "$RSCO/sdd"   remote add origin https://github.com/FS-GG/FS.GG.SDD.git
  mkdir -p "$RSCO/rnd"   && git -C "$RSCO/rnd"   init -q && git -C "$RSCO/rnd"   remote add origin git@github.com:FS-GG/FS.GG.Rendering.git
  mkdir -p "$RSCO/nogit"
  rscoped() { local dir="$1"; shift; ( cd "$dir" \
      && FSGG_GITHUB_API_BASE="http://127.0.0.1:$RS_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
         FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$RS_CACHE" \
         "$ENGINE" reap "$@" 2>&1 ); }

  # (1) A bare `reap` (DRY RUN) from an SDD checkout considers ONLY SDD's claim — names it, and NOT
  #     Rendering's. The dry run is the corpus's own surface: the point is WHICH claims it considers.
  r_sdd="$(rscoped "$RSCO/sdd")"
  printf '%s' "$r_sdd" | grep -q 'would reap  FS.GG.SDD#301  worker mole-s1' \
    && ok "#480: a bare 'reap' from an SDD checkout names THAT repo's claim (FS.GG.SDD#301)" \
    || bad "#480: bare reap scopes to the checkout" "$r_sdd"
  printf '%s' "$r_sdd" | grep -qE 'FS\.GG\.(Templates|Governance|Rendering|Audio|Game)#' \
    && bad "#480: a bare SDD-checkout reap must NOT reach another repo (the destructive default)" "$r_sdd" \
    || ok "#480: ...and never names a Rendering/Templates/… claim — the org-wide default #480 deletes is gone"
  # AT THE TRANSPORT: the fixture was asked for SDD's issues, NEVER Rendering's — the corpus's
  # "considers only THAT repo's claims" re-expressed as which repo's `/issues` the scan fetched.
  rs_reqs="$(issget "$RS_PORT")"
  { printf '%s' "$rs_reqs" | jq -e 'index("FS.GG.SDD") != null and index("FS.GG.Rendering") == null' >/dev/null 2>&1; } \
    && ok "#480: ...and only SDD's /issues was fetched, never Rendering's (scope proven at the transport)" \
    || bad "#480: reap must fetch only the checkout's repo issues" "requests: $rs_reqs"

  # (2) The default is READ from the remote, not hard-wired: the same bare reap from a Rendering checkout
  #     (ssh-form origin, so both URL shapes are exercised) picks Rendering's claim, not SDD's.
  r_rnd="$(rscoped "$RSCO/rnd")"
  { printf '%s' "$r_rnd" | grep -q 'would reap  FS.GG.Rendering#302  worker mole-r1' \
      && ! printf '%s' "$r_rnd" | grep -q 'FS.GG.SDD#'; } \
    && ok "#480: a bare 'reap' from a Rendering checkout (ssh remote) picks Rendering#302 — the scope is the remote" \
    || bad "#480: bare reap reads the actual remote" "$r_rnd"

  # (3) An explicit --repo SPELLS OUT the scope and wins over the checkout: from the SDD checkout,
  #     `--repo rendering` (a registry short-id) resolves and considers Rendering's claim, not SDD's.
  r_expl="$(rscoped "$RSCO/sdd" --repo rendering)"
  { printf '%s' "$r_expl" | grep -q 'would reap  FS.GG.Rendering#302  worker mole-r1' \
      && ! printf '%s' "$r_expl" | grep -q 'FS.GG.SDD#'; } \
    && ok "#480: an explicit '--repo rendering' wins over the SDD checkout AND resolves the short-id (#381)" \
    || bad "#480: explicit --repo wins + short-id resolves" "$r_expl"

  # (4) OUTSIDE a checkout, `reap` — the DESTRUCTIVE command — REFUSES rather than fall back to an
  #     org-wide scan. The refusal precedes any network read, so it deletes across zero repos, never five.
  r_nogit="$(rscoped "$RSCO/nogit")"; r_rc=$?
  { printf '%s' "$r_nogit" | grep -q -- '--repo required' && [ "$r_rc" -ne 0 ]; } \
    && ok "#480: 'reap' outside a checkout REFUSES ('--repo required'), never scans the whole org (the destructive default)" \
    || bad "#480: reap must refuse an undetectable scope" "rc=$r_rc: $r_nogit"
  # NOTHING was deleted along the way — every leg above was a dry run or a refusal.
  [ "$(deletes_on "$RS_PORT")" = '[]' ] \
    && ok "#480: ...and across every scope leg NOTHING was deleted (dry runs + a refusal, never a break)" \
    || bad "#480: reap scope legs must not delete" "deletes: $(deletes_on "$RS_PORT")"

  kill "$RS_SRV" 2>/dev/null
fi
rm -f "$RS_OUT"; rm -rf "$RS_CACHE" "$RSCO"

# ---- WHO READS THE LOCK OFF THE BOARD TOO (cases 25 + 26): #461/#581 -------------------------------
#
# `who` is the truth read of the LOCK, and the lock is NOT the board column — a marker sits on the ISSUE,
# whose board Status may be Ready (a column flip that FAILED) or nowhere at all (a claim that never
# reached the board). Case 20 above proves `who` on the board; this proves the half a board scan cannot
# reach: `who --repo` scans the repo's OPEN ISSUES and reads the marker on each. The world (case 25's
# `seed_offboard_world` + case 26's proof-of-life seed, one transport over): #210 In progress w/ no marker
# (UNCLAIMED), #211 board-says-Ready but a live marker HOLDS it, #215 an off-board HELD claim, #216 an
# off-board DEAD stale claim (bare STALE), #217 a chatty markerless issue (NOT in flight), and #890 an
# off-board stale claim whose `item/890-*` PR is OPEN — #581 proof of life. The off-board scan must also
# PAGINATE (a lock has no 100-issue limit) and never be conditional (a 304 could hide a fresh marker);
# both are re-expressed at the HTTP layer via the fixture's `/_requests` ledger.
OB_OUT="$(mktemp)"; python3 "$HERE/offboard_server.py" >"$OB_OUT" 2>/dev/null & OB_SRV=$!; OB_PORT=""
for _ in $(seq 1 50); do OB_PORT="$(head -n1 "$OB_OUT" 2>/dev/null)"; [ -n "$OB_PORT" ] && break; sleep 0.1; done
rm -f "$OB_OUT"
if [ -z "$OB_PORT" ]; then bad "off-board who fixture bound a port"; else
  obenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$OB_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
              "$ENGINE" "$@"; }
  obj="$(obenv who --repo FS.GG.Rendering --json 2>/dev/null)"

  # IN FLIGHT, NO MORE NO LESS: the five claimed/orphaned items — INCLUDING the two off the board (#215,
  # #216) and #890 — and NOT the chatty markerless #217. This is the whole of case 25's `who` contract.
  obnums="$(printf '%s' "$obj" | jq -c '[.[].number] | sort')"
  [ "$obnums" = '[210,211,215,216,890]' ] \
    && ok "who: reports every in-flight item WHEREVER the board thinks it is — never the chatty #217 (case 25)" \
    || bad "who off-board in-flight set" "expected [210,211,215,216,890], got: $obnums"

  # OFF THE BOARD, STILL A LOCK: #215 is on NO board item, yet its live marker is HELD, names puffin-h11,
  # and carries the touch-set read from the issue body — a reservation a board scan would miss entirely.
  o215="$(printf '%s' "$obj" | jq -c '.[] | select(.number==215)')"
  printf '%s' "$o215" | jq -e '.state=="held" and .worker=="puffin-h11"' >/dev/null 2>&1 \
    && ok "who: an OFF-BOARD claim is HELD and names its worker (case 25 — the board never knew about #215)" \
    || bad "who: #215 off-board held" "got: $o215"
  printf '%s' "$o215" | jq -e '.paths == ["src/Off"]' >/dev/null 2>&1 \
    && ok "who: ...and carries its touch-set, read from the issue body (case 25)" \
    || bad "who: #215 off-board paths" "got: $o215"

  # THE LOCK, NOT THE COLUMN: the board says #211 is Ready (its In-progress flip FAILED), but a live marker
  # holds it — so `who` reports HELD. Reading the column instead would call a held item free.
  printf '%s' "$obj" | jq -e '.[] | select(.number==211) | .state=="held" and .worker=="wren-c22"' >/dev/null 2>&1 \
    && ok "who: a claim whose board Status flip FAILED is HELD, not free — who reads the lock (case 25)" \
    || bad "who: #211 held despite Ready column" "$obj"

  # UNCLAIMED rides the off-board scan too: #210 is In progress with no marker, so ONLY the column puts it
  # in flight — null worker — and its declared touch-set still resolves.
  printf '%s' "$obj" | jq -e '.[] | select(.number==210) | .state=="unclaimed" and .worker==null and (.paths==["src/Orphan2"])' >/dev/null 2>&1 \
    && ok "who: an In-progress markerless item is UNCLAIMED with a null worker, even amid off-board claims (case 25)" \
    || bad "who: #210 unclaimed" "$obj"

  # #581 PROOF OF LIFE: #890's lease lapsed, but PR #433 is OPEN on item/890-* — the worktree protocol's
  # own artifact. `who --json` carries it on the STALE row (`livePr`), and #216 — a stale claim with NO
  # open PR — is a BARE stale (livePr null), the one a reaper may actually collect.
  printf '%s' "$obj" | jq -e '.[] | select(.number==890) | .state=="stale" and .livePr=="#433 item/890-live-work"' >/dev/null 2>&1 \
    && ok "#581: who carries the proof of life on the STALE row — livePr '#433 item/890-live-work' (case 26)" \
    || bad "#581: #890 livePr" "$(printf '%s' "$obj" | jq -c '.[] | select(.number==890)')"
  printf '%s' "$obj" | jq -e '.[] | select(.number==216) | .state=="stale" and .livePr==null' >/dev/null 2>&1 \
    && ok "#581: a stale claim with NO open PR is a BARE stale (livePr null) — a reaper may collect it (case 26)" \
    || bad "#581: #216 bare stale" "$(printf '%s' "$obj" | jq -c '.[] | select(.number==216)')"

  # THE HUMAN ROW #581 is FOR: `STALE (#433 OPEN)` on the live one, a bare `STALE` on the dead one. `who`
  # is what a human reads immediately before deciding to reap, so the two must not read the same.
  obt="$(obenv who --repo FS.GG.Rendering 2>/dev/null)"
  printf '%s' "$obt" | grep -qE 'FS.GG.Rendering#890.*STALE \(#433 OPEN\)' \
    && ok "#581: ...and the human row says STALE (#433 OPEN), not a bare STALE (case 26)" \
    || bad "#581: #890 text STALE (#433 OPEN)" "$obt"
  printf '%s' "$obt" | grep FS.GG.Rendering#216 | grep -q 'OPEN' \
    && bad "#581: #216 must be a BARE stale in the text row (no OPEN)" "$(printf '%s' "$obt" | grep '#216')" \
    || ok "#581: ...and #216, whose work is dead, is a BARE STALE (no PR to name) (case 26)"

  # THE SCAN ITSELF (case 25): a lock has no 100-issue limit, so the open-issue scan PAGINATES; and it is
  # NEVER conditional, because a 304 could serve a `comments: 0` captured before a marker was posted and
  # hide a live lock. `inm=none` and `paginate=1`, one transport over — read off the fixture's ledger.
  reqs="$(curl -s "http://127.0.0.1:$OB_PORT/_requests")"
  printf '%s' "$reqs" | jq -e 'any(.[]; .page=="2")' >/dev/null 2>&1 \
    && ok "who: the open-issue scan PAGINATES — page 2 is fetched (a lock has no 100-issue limit) (case 25)" \
    || bad "who scan must paginate" "issue-list requests: $reqs"
  printf '%s' "$reqs" | jq -e 'all(.[]; .inm==false)' >/dev/null 2>&1 \
    && ok "who: ...and is NEVER a conditional request — no If-None-Match may let a 304 hide a marker (case 25)" \
    || bad "who scan must be unconditional (inm=none)" "issue-list requests: $reqs"

  kill "$OB_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 25 (offboard-claims) — BATCH RESERVES an off-board claim's touch-set (#461/#581). The `who` legs
# above prove the engine can READ a lock off the board; this proves the SCHEDULER honours it. Disjointness
# is only sound if the reserved set is complete, and a claim lives off the board too: a marker on an issue
# whose column flip failed (the board says Ready), or on one the board never listed. bash's `active_claims`
# scans the repo's OPEN ISSUES (arm B) for exactly this; the engine's scan now does too, or `batch` hands
# out a double-book. World (case 25's `seed_offboard_world`, batch slice, one transport over): #211 Ready
# but HELD by wren-c22, #212 genuinely free, #213 Ready declaring `src/Off/Sub`, #215 an OFF-BOARD claim
# held by puffin-h11 on `src/Off`. Only #212 has files no live marker touches.
# ==================================================================================================
OBB_OUT="$(mktemp)"; python3 "$HERE/offboardbatch_server.py" >"$OBB_OUT" 2>/dev/null & OBB_SRV=$!; OBB_PORT=""
for _ in $(seq 1 50); do OBB_PORT="$(head -n1 "$OBB_OUT" 2>/dev/null)"; [ -n "$OBB_PORT" ] && break; sleep 0.1; done
rm -f "$OBB_OUT"
if [ -z "$OBB_PORT" ]; then bad "off-board batch fixture bound a port"; else
  obbenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$OBB_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
               FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
               "$ENGINE" "$@"; }

  # THE MACHINE CONTRACT: only the item no live marker touches. #211 is held (the board says Ready, the lock
  # disagrees), #213 overlaps the OFF-BOARD #215's `src/Off`, and #210 is In progress — so #212 alone is free.
  obbj="$(obbenv batch --repo FS.GG.Rendering --json 2>/dev/null)"; rc=$?
  if [ "$rc" -eq 0 ] && [ "$obbj" = '["FS.GG.Rendering#212"]' ]; then
    ok "batch: schedules only the item no live marker touches — reserves the off-board claim (case 25)"
  else
    bad "batch off-board --json" "expected [\"FS.GG.Rendering#212\"], got (rc=$rc): $obbj"
  fi

  obberr="$(obbenv batch --repo FS.GG.Rendering 2>&1 >/dev/null)"

  # A BOARD ITEM WHOSE COLUMN LIES: #211 is Ready on the board, but its live marker HOLDS it. Reading the
  # column instead of the lock would schedule an item a worker is standing in.
  printf '%s' "$obberr" | grep -q 'FS.GG.Rendering#211 — already claimed by worker wren-c22' \
    && ok "batch: skips a Ready item a marker actually holds — names the worker (case 25)" \
    || bad "batch: #211 held-not-free" "$obberr"
  # #428: the skip carries the LEASE WINDOW — 'should I wait?' is a number, not just 'it's taken'.
  printf '%s' "$obberr" | grep -q 'already claimed by worker wren-c22 (lease frees in ~' \
    && ok "batch: ...and names the lease window, not just the holder (#428)" \
    || bad "batch: #211 lease window" "$obberr"

  # THE OFF-BOARD RESERVATION: #213 declares `src/Off/Sub`, a subtree of the off-board #215's `src/Off`. A
  # board-only scan never sees #215, so #213 would be handed puffin-h11's tree. The reservation must name the
  # HOLDER, its ITEM, and the colliding paths — everything a blocked worker needs to act.
  o213="$(printf '%s' "$obberr" | grep 'FS.GG.Rendering#213')"
  printf '%s' "$o213" | grep -q 'overlaps in-flight work' \
    && ok "batch: refuses to schedule over an OFF-BOARD claim's touch-set (case 25)" \
    || bad "batch: #213 overlap" "$o213"
  printf '%s' "$o213" | grep -q 'held by puffin-h11 on FS.GG.Rendering#215' \
    && ok "batch: ...and names the OFF-BOARD holder and its item (#428, #461)" \
    || bad "batch: #213 names off-board holder" "$o213"
  printf '%s' "$o213" | grep -q 'held by puffin-h11 on FS.GG.Rendering#215 (lease frees in ~' \
    && ok "batch: ...and the off-board claim's lease window (#428)" \
    || bad "batch: #213 off-board lease window" "$o213"
  printf '%s' "$o213" | grep -q 'src/Off/Sub  ⇄  src/Off' \
    && ok "batch: ...and still shows WHICH paths collided (case 25)" \
    || bad "batch: #213 collision paths" "$o213"

  # #480/case 25: `next` shares batch's scan, so it reserves the off-board claim too — capped at one, the
  # free item is what it hands out, never #213's double-book.
  obbnext="$(obbenv next --repo FS.GG.Rendering 2>/dev/null)"
  [ "$obbnext" = "FS.GG.Rendering#212" ] \
    && ok "next: shares batch's off-board reservation — hands out the free item, not the collision (case 25)" \
    || bad "next off-board" "expected FS.GG.Rendering#212, got: $obbnext"

  # THE SCAN ITSELF (case 25, lines 156–165): had the candidate scan reused the ETag'd `issues` command, a
  # live claim on the repo's 101st open issue would be invisible and `batch` would hand its touch-set away. So
  # the off-board scan PAGINATES (page 2 fetched) and is NEVER conditional (a 304 could serve a pre-marker
  # `comments: 0`). The SAME `Reads.openIssues` `who` uses, proven again at the SCHEDULER's surface.
  obbreqs="$(curl -s "http://127.0.0.1:$OBB_PORT/_requests")"
  printf '%s' "$obbreqs" | jq -e 'any(.[]; .page=="2")' >/dev/null 2>&1 \
    && ok "batch: the candidate scan PAGINATES — page 2 is fetched, so a claim past page 1 is not missed (case 25)" \
    || bad "batch scan must paginate" "issue-list requests: $obbreqs"
  printf '%s' "$obbreqs" | jq -e 'all(.[]; .inm==false)' >/dev/null 2>&1 \
    && ok "batch: ...and is NEVER conditional — no 304 may serve a pre-marker comments:0 (case 25)" \
    || bad "batch scan must be unconditional (inm=none)" "issue-list requests: $obbreqs"

  kill "$OBB_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 25 (offboard-claims) — the STARVED-QUEUE BANNER (#428). The scheduler above reserves off-board
# and stale claims; this proves the AGGREGATE it renders when that reservation leaves NOTHING to hand
# out. "nothing schedulable" over a busy repo reads as an empty backlog and sends a worker home — so a
# starved queue must be called BUSY, name every holder, give the soonest lease, and — for a lease that
# has EXPIRED — point at `reap`, the one blocker a worker clears alone. World (case 25's starved
# section, one transport over): #221/#222/#224 are queued behind live claims (tern fresh, kite fresh,
# ghost EXPIRED); #225 overlaps a MARKERLESS In-progress reserver (#226) — reserved, but no holder to
# name and no lease to wait out; only #212's world (above) ever hands out work.
# ==================================================================================================
SQ_OUT="$(mktemp)"; python3 "$HERE/starvedqueue_server.py" >"$SQ_OUT" 2>/dev/null & SQ_SRV=$!; SQ_PORT=""
for _ in $(seq 1 50); do SQ_PORT="$(head -n1 "$SQ_OUT" 2>/dev/null)"; [ -n "$SQ_PORT" ] && break; sleep 0.1; done
rm -f "$SQ_OUT"
if [ -z "$SQ_PORT" ]; then bad "starved-queue fixture bound a port"; else
  sqenv() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SQ_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
              "$ENGINE" "$@"; }

  # THE LOCKS STILL HOLD: a starved queue schedules nothing (the machine array is empty), but it is NOT
  # the same fact as an empty backlog — the banner below is the whole difference.
  sqj="$(sqenv batch --repo FS.GG.Rendering --json 2>/dev/null)"; rc=$?
  [ "$rc" -eq 0 ] && [ "$sqj" = '[]' ] \
    && ok "#428: a starved queue schedules NOTHING — the locks still hold (case 25)" \
    || bad "#428: starved batch --json" "expected [] (rc=0), got (rc=$rc): $sqj"

  sqerr="$(sqenv batch --repo FS.GG.Rendering 2>&1 >/dev/null)"

  # THE BANNER: the queue is BUSY, and it names every holder so the worker knows who to talk to.
  printf '%s' "$sqerr" | grep -q '3 item(s) are QUEUED BEHIND LIVE CLAIMS held by: ghost-222, kite-z01, tern-y99' \
    && ok "#428: the starved queue is called BUSY and names every holder (case 25)" \
    || bad "#428: queued-behind-claims banner" "$sqerr"
  printf '%s' "$sqerr" | grep -q 'this queue is BUSY, not empty' \
    && ok "#428: ...and says plainly this is not an empty backlog (case 25)" \
    || bad "#428: BUSY-not-empty line" "$sqerr"

  # THE SOONEST LEASE decides the wait — and an EXPIRED lease is a reap, not a wait, so it is the soonest
  # of all (it frees NOW) and the advice points at `reap`, the one blocker a worker can clear alone.
  printf '%s' "$sqerr" | grep -q 'soonest: lease EXPIRED — reapable' \
    && ok "#428: the soonest lease is named — an EXPIRED one frees now (case 25)" \
    || bad "#428: soonest lease" "$sqerr"
  printf '%s' "$sqerr" | grep -q '1 of those lease(s) have EXPIRED — collect them: scripts/fsgg-coord reap --repo FS.GG.Rendering --apply' \
    && ok "#428: ...and an expired lease is a REAP, with the exact command (case 25)" \
    || bad "#428: expired-lease reap advice" "$sqerr"

  # A DEAD holder is not a wait: the per-item reason says EXPIRED, not a countdown, so a worker does not
  # go off to wait for a holder who is very likely gone.
  printf '%s' "$sqerr" | grep -q '#224 — overlaps in-flight work held by ghost-222 on FS.GG.Rendering#216 (lease EXPIRED — reapable)' \
    && ok "#428: an EXPIRED lease is a reap, never a wait — named on the item too (case 25)" \
    || bad "#428: #224 expired per-item" "$sqerr"

  # A MARKERLESS In-progress item RESERVES its files (arm A), so it must not be scheduled over — but it
  # has no worker and no lease, so it is NOT a holder, NOT counted in the 3, and NEVER named "—".
  printf '%s' "$sqerr" | grep -q '#225 — overlaps FS.GG.Rendering#226, which the board says is In progress with NO claim marker' \
    && ok "#428: a markerless In-progress item is a reserver, not a holder (case 25)" \
    || bad "#428: #225 markerless reserver" "$sqerr"
  printf '%s' "$sqerr" | grep -q 'there is no lease to wait out; see: scripts/fsgg-coord who' \
    && ok "#428: ...and it says there is no lease to wait out (case 25)" \
    || bad "#428: #225 no-lease advice" "$sqerr"
  printf '%s' "$sqerr" | grep -q 'held by —' \
    && bad "#428: an unnameable reserver must never appear as a holder named '—'" "$sqerr" \
    || ok "#428: an unnameable reserver never appears as a holder named '—' (case 25)"

  # THE BANNER DOES NOT FIRE ON A HEALTHY QUEUE: the off-board world above HANDED OUT #212, so its stderr
  # carries the per-item skips but NEVER the BUSY banner — a banner on a schedule that worked is noise
  # that trains workers to skip stderr (#440). Re-run that world here and confirm the silence.
  printf '%s' "$obberr" | grep -q 'QUEUED BEHIND LIVE CLAIMS' \
    && bad "#428: a queue that DID hand out work must print no starved-queue banner" "$obberr" \
    || ok "#428: a queue that handed out work prints NO starved-queue banner (case 25)"

  kill "$SQ_SRV" 2>/dev/null
fi

# ---- INBOX: the worker mailbox, and the message that rides an OFF-BOARD claim (cases 22 + 25) --------
#
# `inbox` delivers what `say` posts. Case 22 certifies the mailbox contract (addressed + broadcast
# delivery, the item named, the cursor advancing, a worker not seeing its OWN mail, `--peek` not
# consuming); case 25 certifies that a message posted on an OFF-BOARD claim is delivered — which forces
# `inbox` onto the SAME open-issue scan `who`/`reap`/`batch` run, or it drops the message. This is a pure
# engine round-trip: the engine `say`s each message over HTTP and the engine `inbox`es it back — the
# fixture seeds NO message, it only stores what the engine POSTs. #215 is OFF the board (only the
# open-issue scan reaches it), so a mailbox reading the board's In-progress column alone would miss it.
#
# ONE shared cache dir for the whole slice (unlike the per-call `mktemp -d` elsewhere): the per-worker
# inbox cursor is a file in it, and the cursor advancing between reads is half of what case 22 asserts.
IBX_OUT="$(mktemp)"; python3 "$HERE/inbox_server.py" >"$IBX_OUT" 2>/dev/null & IBX_SRV=$!; IBX_PORT=""
for _ in $(seq 1 50); do IBX_PORT="$(head -n1 "$IBX_OUT" 2>/dev/null)"; [ -n "$IBX_PORT" ] && break; sleep 0.1; done
rm -f "$IBX_OUT"
if [ -z "$IBX_PORT" ]; then bad "inbox fixture bound a port"; else
  IBX_CACHE="$(mktemp -d)"
  ibx() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$IBX_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
            FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$IBX_CACHE" \
            "$ENGINE" "$@"; }

  # puffin-h11 (who holds the OFF-BOARD #215) messages hoopoe-i22, then broadcasts to whoever is here.
  ibx say --worker puffin-h11 'FS.GG.Rendering#215' --to hoopoe-i22 --message 'I hold src/Off — stay out.' >/dev/null 2>&1
  ibx say --worker puffin-h11 'FS.GG.Rendering#215' --to '*' --message 'Broadcast to whoever is here.' >/dev/null 2>&1

  inbox1="$(ibx inbox --worker hoopoe-i22 --repo FS.GG.Rendering 2>/dev/null)"

  # OFF-BOARD DELIVERY (case 25): the message rode #215, an issue the board never listed — delivered only
  # because `inbox` ran the off-board open-issue scan, not the board column.
  printf '%s' "$inbox1" | grep -q 'I hold src/Off — stay out.' \
    && ok "inbox: delivers a message posted on an OFF-BOARD claim (case 25 — #215 is not on the board)" \
    || bad "inbox off-board delivery" "$inbox1"
  # A BROADCAST (to=*) reaches whoever reads (case 22).
  printf '%s' "$inbox1" | grep -q 'Broadcast to whoever is here.' \
    && ok "inbox: delivers a broadcast (to=*) (case 22)" \
    || bad "inbox broadcast" "$inbox1"
  # ...and it NAMES the item the message rode in on (case 22).
  printf '%s' "$inbox1" | grep -q 'FS.GG.Rendering#215' \
    && ok "inbox: names the item the message rode in on (case 22)" \
    || bad "inbox names item" "$inbox1"

  # THE CURSOR ADVANCED: a second read shows nothing new (case 22). The mail was consumed, not re-shown.
  inbox2="$(ibx inbox --worker hoopoe-i22 --repo FS.GG.Rendering 2>/dev/null)"
  [ "$inbox2" = "no new messages for worker hoopoe-i22." ] \
    && ok "inbox: the cursor advanced — nothing new on a second read (case 22)" \
    || bad "inbox cursor advance" "expected 'no new messages...', got: $inbox2"

  # A WORKER DOES NOT SEE ITS OWN MESSAGES (case 22): puffin-h11 sent both, so its own inbox is empty.
  inboxself="$(ibx inbox --worker puffin-h11 --repo FS.GG.Rendering 2>/dev/null)"
  [ "$inboxself" = "no new messages for worker puffin-h11." ] \
    && ok "inbox: a worker does not see its OWN messages (case 22)" \
    || bad "inbox self-filter" "expected 'no new messages...', got: $inboxself"

  # --PEEK SHOWS NEW MAIL WITHOUT CONSUMING IT (case 22): post one more, peek it, then a plain read still
  # sees it — the peek left the cursor where it was.
  ibx say --worker puffin-h11 'FS.GG.Rendering#215' --to hoopoe-i22 --message 'One more.' >/dev/null 2>&1
  peek="$(ibx inbox --worker hoopoe-i22 --repo FS.GG.Rendering --peek 2>/dev/null)"
  printf '%s' "$peek" | grep -q 'One more.' \
    && ok "inbox --peek: shows new mail (case 22)" \
    || bad "inbox --peek shows" "$peek"
  plain="$(ibx inbox --worker hoopoe-i22 --repo FS.GG.Rendering 2>/dev/null)"
  printf '%s' "$plain" | grep -q 'One more.' \
    && ok "inbox --peek: did NOT advance the cursor — the mail is still new (case 22)" \
    || bad "inbox --peek no-advance" "$plain"

  # case 24 (n): `say --to` NORMALIZES its target to a worker id. Ids are slug()'d at creation and `inbox`
  # matches `.to` by EXACT string, so an unslugged `--to Heron-B71` would post a message its recipient
  # (heron-b71) could never see — the message lands on the item but is addressed to an id nobody holds.
  # The engine slugs the target and WARNS that it did so; `*` stays the literal broadcast (proven above).
  normerr="$(ibx say --worker puffin-h11 'FS.GG.Rendering#215' --to 'Heron-B71' --message 'the impl is yours' 2>&1 >/dev/null)"
  printf '%s' "$normerr" | grep -q "normalized from 'Heron-B71'" \
    && ok "say: a mis-cased --to is normalized to the worker id (case 24 n)" \
    || bad "say --to normalize warning" "$normerr"
  # THE PROPERTY, round-tripped through the engine: the marker addresses the SLUG, so the slugged worker
  # inboxes it. Had the engine posted `to=Heron-B71` verbatim, heron-b71 (exact-string match) would never
  # see it — so delivery here IS the proof that `--to` was normalized to the id `inbox` matches.
  inboxnorm="$(ibx inbox --worker heron-b71 --repo FS.GG.Rendering 2>/dev/null)"
  printf '%s' "$inboxnorm" | grep -q 'the impl is yours' \
    && ok "say: ...and the marker addresses the slug, so inbox matches it (case 24 n)" \
    || bad "say --to slug round-trip" "$inboxnorm"

  kill "$IBX_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 30 (pr-existence-697) — WHAT the orphaned PR SAYS, not merely that it exists. `who`/`reap` legs.
#
# #581 taught the tools that an open `item/<n>-*` PR is proof of life, and stopped there — so `reap` refused
# such a claim and offered exactly one exit, "close it, then reap". For a PR that is GREEN and MERGEABLE that
# exit DESTROYS the best work on the board. #697 reads the landable verdict (#720) so `who` flies the right
# flag and `reap` speaks the right refusal. World (case 30's #697 seeds, one transport over): two OFF-BOARD
# stale claims — #970 whose PR #701 is GREEN and MERGEABLE (LAND IT), #976 whose PR #705 is mergeable but has
# checks still RUNNING (pending). `landable_server.py` scores each off its head SHA's workflow runs + check
# runs (#720). The `adopt` command itself (case 30 parts 3–5) and case 31's superseded-run scoring are
# separate slices; this proves the two TRUTH READS speak the verdict.
# ==================================================================================================
LND_OUT="$(mktemp)"; python3 "$HERE/landable_server.py" >"$LND_OUT" 2>/dev/null & LND_SRV=$!; LND_PORT=""
for _ in $(seq 1 50); do LND_PORT="$(head -n1 "$LND_OUT" 2>/dev/null)"; [ -n "$LND_PORT" ] && break; sleep 0.1; done
rm -f "$LND_OUT"
if [ -z "$LND_PORT" ]; then bad "landable fixture bound a port"; else
  lnd() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$LND_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
            FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
            "$ENGINE" "$@"; }

  # 1. `who` SAYS the work is finished. It is what a human reads immediately before reaping, so it is
  #    exactly where "GREEN: LAND IT" has to appear — a bare `STALE (#701 OPEN)` reads as an abandoned
  #    branch and the reader reaches for `reap` (#697).
  wtext="$(lnd who --repo FS.GG.SDD 2>&1)"
  printf '%s' "$wtext" | grep -q 'FS.GG.SDD#970 .*STALE (#701 OPEN — GREEN: LAND IT)' \
    && ok "#697: who says a stale claim's GREEN PR is FINISHED work — 'STALE (#701 OPEN — GREEN: LAND IT)' (case 30)" \
    || bad "#697: who GREEN: LAND IT row" "$wtext"
  printf '%s' "$wtext" | grep -q 'fsgg-coord adopt FS.GG.SDD#970' \
    && ok "#697: ...and points at the command that LANDS it, not the one that bins it (case 30)" \
    || bad "#697: who points at adopt" "$wtext"
  # A conflicted/pending PR is NOT green: #976's checks are still running, so its row must NOT say LAND IT.
  printf '%s' "$wtext" | grep -q 'FS.GG.SDD#976 .*STALE (#705 OPEN — checks running)' \
    && ok "#697: a mergeable PR whose checks are RUNNING is 'checks running', not LAND IT (case 30)" \
    || bad "#697: who pending row" "$wtext"

  # 2. `who --json` carries the PR's STATE on the stale row, not just its existence.
  wjson="$(lnd who --repo FS.GG.SDD --json 2>/dev/null)"
  [ "$(printf '%s' "$wjson" | jq -r '.[] | select(.number==970) | .prState')" = "green" ] \
    && ok "#697: who --json carries prState 'green' on the finished orphan (case 30)" \
    || bad "#697: #970 prState" "$(printf '%s' "$wjson" | jq -c '.[] | select(.number==970)')"
  [ "$(printf '%s' "$wjson" | jq -r '.[] | select(.number==976) | .prState')" = "pending" ] \
    && ok "#697: ...and 'pending' on the one whose CI has not settled (case 30)" \
    || bad "#697: #976 prState" "$(printf '%s' "$wjson" | jq -c '.[] | select(.number==976)')"

  # 3. THE ONE THAT MATTERS. `reap` must not point the destructive verb at finished work: it REFUSES the
  #    green orphan, calls it FINISHED, names `adopt`, and NEVER advises "close it, then reap".
  rerp="$(lnd reap --repo FS.GG.SDD --apply 2>&1)"
  printf '%s' "$rerp" | grep -q 'REFUSING to reap FS.GG.SDD#970' \
    && ok "#697: reap REFUSES a claim whose PR is green and mergeable (case 30)" \
    || bad "#697: reap refuses #970" "$rerp"
  printf '%s' "$rerp" | grep -q 'FS.GG.SDD#970.*GREEN and MERGEABLE' \
    && ok "#697: ...and calls the work GREEN and MERGEABLE — FINISHED (case 30)" \
    || bad "#697: reap #970 FINISHED" "$rerp"
  printf '%s' "$rerp" | grep -q 'fsgg-coord adopt FS.GG.SDD#970' \
    && ok "#697: ...and names \`adopt\` as the remedy (case 30)" \
    || bad "#697: reap #970 names adopt" "$rerp"
  case "$rerp" in
    *"close it, then reap"*)
      bad "#697: reap must NEVER advise closing a GREEN, mergeable PR — that is the loaded gun" "$rerp" ;;
    *) ok "#697: reap must NEVER advise closing a GREEN, mergeable PR — that is the loaded gun (case 30)" ;;
  esac

  # 4. A MERGEABLE PR WHOSE CHECKS ARE STILL RUNNING is not abandoned — `reap` must refuse it too, and must
  #    NOT tell anyone to close it: "Not green YET" is not "not green" (case 30, #697 4e's reap leg).
  printf '%s' "$rerp" | grep -q 'REFUSING to reap FS.GG.SDD#976' \
    && ok "#697: reap REFUSES a PR whose checks are still RUNNING — pending is not passing (case 30)" \
    || bad "#697: reap refuses #976" "$rerp"
  p976="$(printf '%s' "$rerp" | grep -A2 'FS.GG.SDD#976')"
  printf '%s' "$p976" | grep -q 'Do NOT close it' \
    && ok "#697: ...it says the work is UNFINISHED, and to look again — 'Do NOT close it' (case 30)" \
    || bad "#697: reap #976 Do NOT close it" "$p976"

  # 5. THE #581 GUARANTEE STILL HOLDS through the new verdict: a refusal DELETES NOTHING — both live claims
  #    survive. A refusal that deleted anyway is the exact bug #581/#697 exist to prevent.
  [ "$(deletes_on "$LND_PORT")" = '[]' ] \
    && ok "#697: both claims SURVIVE the refusals — reap deleted NOTHING (case 30)" \
    || bad "#697: reap refusal must not delete" "deletes: $(deletes_on "$LND_PORT")"

  kill "$LND_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 30 (pr-existence-697) — the `adopt` COMMAND. Land another worker's orphaned PR through ONE verified
# command that cannot be talked into landing anything else. The GATE is what makes it safe: `adopt` lands
# FINISHED work (green + mergeable) and nothing else. The transfer reuses `claim` (one lock, one CAS). World
# (case 30's #697 seeds, one transport over — `adopt_server.py`): #970 GREEN (LAND IT, transfer), #971
# CONFLICTED, #972 mergeable-but-ZERO-checks (NOT green, #606), #973 a LIVE claim (a steal, not an orphan),
# #974 NO open PR (merely dead), #975 mergeable=null-then-false (the lazy re-read sees the conflict), #976
# checks RUNNING (pending). Every refusal leaves the lock UNTOUCHED.
# ==================================================================================================
ADP_OUT="$(mktemp)"; python3 "$HERE/adopt_server.py" >"$ADP_OUT" 2>/dev/null & ADP_SRV=$!; ADP_PORT=""
for _ in $(seq 1 50); do ADP_PORT="$(head -n1 "$ADP_OUT" 2>/dev/null)"; [ -n "$ADP_PORT" ] && break; sleep 0.1; done
rm -f "$ADP_OUT"
if [ -z "$ADP_PORT" ]; then bad "adopt fixture bound a port"; else
  adp() { FSGG_GITHUB_API_BASE="http://127.0.0.1:$ADP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
            FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_MERGEABLE_RETRY_MS=0 \
            FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" "$@"; }
  # The workers whose markers sit on an issue right now (the transfer POSTs; a refusal does not).
  workers_on() { curl -s "http://127.0.0.1:$ADP_PORT/repos/FS-GG/FS.GG.SDD/issues/$1/comments" \
                   | jq -r '[.[].body | capture("worker=(?<w>[^\\s>]+)").w] | join(",")' 2>/dev/null; }

  # THE REFUSALS FIRST (they touch nothing), so the green transfer's POSTed marker cannot leak into them.

  # 4a. A CONFLICTED PR is not finished — rebasing it is AUTHORING, not landing.
  conf="$(adp adopt FS.GG.SDD#971 --worker heron-697 2>&1 || true)"
  printf '%s' "$conf" | grep -q 'CONFLICTED' \
    && ok "#697: adopt REFUSES a conflicted PR — rebasing is authoring, not landing (case 30)" \
    || bad "#697: adopt #971 conflicted" "$conf"
  [ "$(workers_on 971)" = "ghost-971" ] \
    && ok "#697: ...and does NOT take the lock on it (case 30)" \
    || bad "#697: adopt #971 lock leaked" "workers: $(workers_on 971)"

  # 4b. ZERO check runs is NOT green (#606) — an absent subject is a finding, not a pass.
  nock="$(adp adopt FS.GG.SDD#972 --worker heron-697 2>&1 || true)"
  printf '%s' "$nock" | grep -q 'NOT green' \
    && ok "#697/#606: a mergeable PR with ZERO check runs is NOT green — adopt refuses it (case 30)" \
    || bad "#697: adopt #972 zero-checks" "$nock"
  [ "$(workers_on 972)" = "ghost-972" ] \
    && ok "#697/#606: ...and does NOT take the lock on untested work (case 30)" \
    || bad "#697: adopt #972 lock leaked" "workers: $(workers_on 972)"

  # 4c. A LIVE claim is not an orphan. Adopting one is a STEAL.
  livec="$(adp adopt FS.GG.SDD#973 --worker heron-697 2>&1 || true)"
  printf '%s' "$livec" | grep -q 'held by a LIVE claim' \
    && ok "#697: adopt REFUSES a LIVE claim — a worker that is alive is not an orphan (case 30)" \
    || bad "#697: adopt #973 live" "$livec"
  [ "$(workers_on 973)" = "busy-973" ] \
    && ok "#697: ...and the live worker keeps its lock (case 30)" \
    || bad "#697: adopt #973 lock stolen" "workers: $(workers_on 973)"

  # 4d. No PR at all: nothing to land — the claim is merely DEAD, and `reap` is the right tool.
  nopr="$(adp adopt FS.GG.SDD#974 --worker heron-697 2>&1 || true)"
  printf '%s' "$nopr" | grep -q 'no finished work to adopt' \
    && ok "#697: adopt REFUSES an item with no open PR — there is no finished work to land (case 30)" \
    || bad "#697: adopt #974 no-pr" "$nopr"
  [ "$(workers_on 974)" = "ghost-974" ] \
    && ok "#697: ...and leaves the dead claim for reap (case 30)" \
    || bad "#697: adopt #974 lock leaked" "workers: $(workers_on 974)"

  # 5. `mergeable` IS COMPUTED LAZILY: the first read is `null`, a later one carries the truth. A null must
  #    be RE-READ, not believed — else a CONFLICTED PR reads as landable.
  lazy="$(adp adopt FS.GG.SDD#975 --worker heron-697 2>&1 || true)"
  printf '%s' "$lazy" | grep -q 'CONFLICTED' \
    && ok "#697: a null \`mergeable\` is re-read, and the PR's REAL state (conflicted) is seen (case 30)" \
    || bad "#697: adopt #975 lazy" "$lazy"
  [ "$(workers_on 975)" = "ghost-975" ] \
    && ok "#697: ...and the lock is not taken on a PR we misread as landable (case 30)" \
    || bad "#697: adopt #975 lock leaked" "workers: $(workers_on 975)"

  # 4e. Checks still RUNNING — a pending check is not a passing one.
  pend="$(adp adopt FS.GG.SDD#976 --worker heron-697 2>&1 || true)"
  printf '%s' "$pend" | grep -q 'checks RUNNING' \
    && ok "#697: adopt refuses a PR whose checks are still RUNNING — pending is not passing (case 30)" \
    || bad "#697: adopt #976 pending" "$pend"
  [ "$(workers_on 976)" = "ghost-976" ] \
    && ok "#697: ...and does NOT take the lock on unfinished work (case 30)" \
    || bad "#697: adopt #976 lock leaked" "workers: $(workers_on 976)"

  # 3. THE TRANSFER. A GREEN, mergeable orphan is adopted: adopt confirms GREEN and MERGEABLE, hands the
  #    worker the MERGE (not a rebuild, not a close), and TRANSFERS the claim under `claim`'s CAS.
  adopt970="$(adp adopt FS.GG.SDD#970 --worker heron-697 2>&1 || true)"
  printf '%s' "$adopt970" | grep -q 'GREEN and MERGEABLE' \
    && ok "#697: adopt confirms the PR is green and mergeable before touching anything (case 30)" \
    || bad "#697: adopt #970 GREEN banner" "$adopt970"
  printf '%s' "$adopt970" | grep -q 'Do NOT rebuild it, and do NOT close PR #701' \
    && ok "#697: adopt hands the worker the MERGE, and says not to close the PR (case 30)" \
    || bad "#697: adopt #970 epilogue" "$adopt970"
  # THE TRANSFER ITSELF: heron-697's marker is now on #970, so the live winner (ghost-970 is stale) is the
  # adopter — the claim is theirs, under one CAS, the total order intact.
  printf '%s' "$(workers_on 970)" | grep -q 'heron-697' \
    && ok "#697: adopt TRANSFERS the claim — the adopter's marker is posted under the CAS (case 30)" \
    || bad "#697: adopt #970 transfer" "workers: $(workers_on 970)"

  kill "$ADP_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 31 (superseded-run-720) — a SUPERSEDED run is not a RED one, driven through the `landable` COMMAND.
#
# `Landable.score`/`supersede` (Core, unit-tested) fixed the scoring for case 30's SINGLE-run worlds. Case
# 31's world is MULTIPLE runs on one SHA — a force-pushed PR whose first suite was `cancelled` when a second
# trigger of its own concurrency group replaced it. The raw aggregate saw `cancelled` and called green,
# mergeable, finished work red, and it was `adopt` (whose whole population is force-pushed PRs) that paid.
# This slice surfaces the verdict as a first-class QUERY — `landable <pr> --repo` prints the word on stdout
# and puts the decision in the exit code — and drives case 31's #720 legs through the engine over HTTP
# (`landable_super_server.py`, one PR per leg, no board/claim machinery — the scoring, isolated).
#
# Disposed on the record (ADR-0040 §5): (a) the exit CODES — bash numbers the poll loop 0/3/1
# (green/pending/red), the engine keeps 3==red across every verdict command and gives PENDING its own 7, so
# the LITERALS differ while the PROPERTY (green 0; pending a distinct retryable code; red a distinct
# do-not-wait code) does not; (b) leg 9's argv-128KB cap is bash's — the bash rollup piped both lists to jq
# through argv and a real run set tripped MAX_ARG_STRLEN; the engine reads the JSON off HttpClient, so the
# failure mode is STRUCTURALLY ABSENT, and the fat payload is served only to prove the engine rolls it up.
# NOT covered here (a follow-up sub-slice, #724): `landable --wait` — the poll loop that does not believe an
# early green and waits for the run set to STOP GROWING. Case 31 stays PARTIAL until it lands.
# ==================================================================================================
SUP_OUT="$(mktemp)"; python3 "$HERE/landable_super_server.py" >"$SUP_OUT" 2>/dev/null & SUP_SRV=$!; SUP_PORT=""
for _ in $(seq 1 50); do SUP_PORT="$(head -n1 "$SUP_OUT" 2>/dev/null)"; [ -n "$SUP_PORT" ] && break; sleep 0.1; done
rm -f "$SUP_OUT"
if [ -z "$SUP_PORT" ]; then bad "landable-super fixture bound a port"; else
  sup()    { FSGG_GITHUB_API_BASE="http://127.0.0.1:$SUP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
               FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_MERGEABLE_RETRY_MS=0 \
               FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" landable "$1" --repo FS.GG.SDD 2>/dev/null || true; }
  # >/dev/null 2>&1, BOTH: the command prints its VERDICT on stdout and the DECISION in the exit code —
  # leaking stdout here would make this helper return "green0" and every leg compare against a word it
  # never expected. `|| rc=$?` is not optional under `set -e`: the whole point of legs below is a NON-zero
  # exit, and letting it escape would kill the run on a PASSING assertion.
  sup_rc() { local rc=0; FSGG_GITHUB_API_BASE="http://127.0.0.1:$SUP_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
               FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_MERGEABLE_RETRY_MS=0 \
               FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" landable "$1" --repo FS.GG.SDD >/dev/null 2>&1 || rc=$?; printf '%s' "$rc"; }

  # 1. THE BUG, IN THE SHAPE .github#718 CARRIED. A cancelled run REPLACED by a later run of its own
  #    concurrency group is superseded — evidence of nothing — and so are ITS check-runs; both drop.
  [ "$(sup 801)" = "green" ] \
    && ok "#720: a cancelled run REPLACED by a later run of its own group is superseded — the PR is GREEN (case 31)" \
    || bad "#720: superseded -> green" "got: $(sup 801)"

  # 2. ...and the drop rule is not a hole. A cancelled run NOBODY re-ran is still a finding.
  [ "$(sup 802)" = "red" ] \
    && ok "#720: a cancelled run with NO later run of its group is still RED — the drop is not a hole (case 31)" \
    || bad "#720: lone cancelled -> red" "got: $(sup 802)"

  # 3. THE TRAP THE OBVIOUS FIX FALLS INTO. A workflow_dispatch run shares the SHA and path but is a
  #    DIFFERENT concurrency group, so it supersedes NOTHING — keying on the path alone would let its
  #    vacuous green license the drop of a real cancelled run (#703).
  [ "$(sup 803)" = "red" ] \
    && ok "#720: a workflow_dispatch run does NOT supersede the pull_request run it shares a SHA with — still RED (case 31)" \
    || bad "#720: cross-group no supersede -> red" "got: $(sup 803)"

  # 4. #606 SURVIVES THE REWRITE. Zero runs is an EMPTY SUBJECT, not a clean one.
  [ "$(sup 804)" = "red" ] \
    && ok "#720/#606: ZERO workflow runs is a FINDING, not a pass — the rewrite does not fail open (case 31)" \
    || bad "#720: zero runs -> red" "got: $(sup 804)"

  # 5. A run still going is not a run that passed.
  [ "$(sup 805)" = "pending" ] \
    && ok "#720: an in-flight run is PENDING, never green (case 31)" \
    || bad "#720: in_progress -> pending" "got: $(sup 805)"

  # 6. THE FAIL-OPEN THE FIX ITSELF COULD HAVE INTRODUCED. Scoring runs ALONE would go blind to a
  #    third-party app, which appears only in the check-runs — a failing codecov must still red the PR.
  [ "$(sup 806)" = "red" ] \
    && ok "#720: a FAILING third-party check still reds the PR — the Actions rollup does not go blind (case 31)" \
    || bad "#720: foreign failing check -> red" "got: $(sup 806)"

  # 7. A RUN CAN FAIL WITH NO CHECK RUNS. `startup_failure` (malformed YAML) concludes the RUN `failure`
  #    and never creates a job — and its GREEN SIBLING is the assertion: a check-runs-only rollup would see
  #    an all-green check set and merge a PR whose workflow never even parsed.
  [ "$(sup 807)" = "red" ] \
    && ok "#720: a startup_failure run reds the PR — even with NO check runs, and a green sibling workflow (case 31)" \
    || bad "#720: startup_failure -> red" "got: $(sup 807)"

  # 8. ...AND THE MIRROR: a CHECK RUN can fail while its RUN succeeds (job-level continue-on-error). Branch
  #    protection scores check-runs, so scoring runs alone would call this green. The verdict is the UNION.
  [ "$(sup 808)" = "red" ] \
    && ok "#720: a FAILED check-run reds the PR even though its workflow run concluded success (case 31)" \
    || bad "#720: failed check, green run -> red" "got: $(sup 808)"

  # 9. A REAL-SIZED PAYLOAD. bash's rollup handed both lists to jq through ARGV and a ~150KB run set tripped
  #    MAX_ARG_STRLEN (128KB) — jq died, the verdict was `unknown`, `adopt` refused every real PR. The
  #    engine reads the JSON off HttpClient: no argv, the failure mode is structurally absent. Proven green.
  [ "$(sup 809)" = "green" ] \
    && ok "#720: a REAL-SIZED payload still rolls up GREEN — the engine reads JSON, never argv (128KB cap absent) (case 31)" \
    || bad "#720: fat payload -> green" "got: $(sup 809)"

  # 10. THE EXIT CODE carries the decision, so a poll loop tells "keep waiting" from "stop" without parsing
  #    prose (#724, /pnext-item §5). green 0 · pending 7 (the ONE worth retrying) · red 3 (do NOT wait).
  #    bash numbers these 0/3/1 — disposed above as the property (three distinguishable states, green 0).
  [ "$(sup_rc 801)" = "0" ] \
    && ok "#720: landable exits 0 on green (case 31)" \
    || bad "#720: exit 0 on green" "got: $(sup_rc 801)"
  [ "$(sup_rc 805)" = "7" ] \
    && ok "#720: landable exits 7 on pending — the ONLY verdict worth retrying (bash 3, disposed) (case 31)" \
    || bad "#720: exit 7 on pending" "got: $(sup_rc 805)"
  [ "$(sup_rc 802)" = "3" ] \
    && ok "#720: landable exits 3 on red — do not merge, and do not wait (bash 1, disposed) (case 31)" \
    || bad "#720: exit 3 on red" "got: $(sup_rc 802)"

  kill "$SUP_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 31 (#724) — `landable --wait`, the poll loop that does NOT believe an early green.
#
# The single-shot verdict above fixed the SCORING; `--wait` carries the one thing a single read cannot —
# refusing a PREMATURE green. GitHub registers a PR's runs over 20-60s, so the subject set is empty at first
# (a `red` that is really "not started yet") and then GROWS (an early all-green is a PARTIAL rollup). The
# engine's `Landable.settled` decides break-vs-wait: it keeps waiting while zero runs have registered, and it
# believes a green only once the subject count has STOPPED GROWING across two consecutive polls.
#
# `landable_wait_server.py` is STATEFUL where it must be: sha810's runs/checks GROW on the SECOND read
# (exactly as GitHub schedules them), so the 810 leg is invoked ONCE and its exit captured — a second call
# would advance the fixture's read counter and score against a set that had already grown.
#
# Disposed on the record (ADR-0040 §5): the exit CODES are the engine's own — green 0, red/conflicted 3 —
# where bash's poll loop numbers green/red 0/1; run.sh asserts the PROPERTY (green 0; red/conflicted a
# distinct do-not-wait code), not bash's literals. `--interval 0` drives the poll with no wall-clock.
# ==================================================================================================
WAIT_OUT="$(mktemp)"; python3 "$HERE/landable_wait_server.py" >"$WAIT_OUT" 2>/dev/null & WAIT_SRV=$!; WAIT_PORT=""
for _ in $(seq 1 50); do WAIT_PORT="$(head -n1 "$WAIT_OUT" 2>/dev/null)"; [ -n "$WAIT_PORT" ] && break; sleep 0.1; done
rm -f "$WAIT_OUT"
if [ -z "$WAIT_PORT" ]; then bad "landable-wait fixture bound a port"; else
  # `landable --wait --tries N --interval 0` -> the command's exit status (the poll-loop contract). Both
  # stdout and stderr to /dev/null: the verdict word on stdout would otherwise fold into the captured value.
  lndw() { local rc=0; FSGG_GITHUB_API_BASE="http://127.0.0.1:$WAIT_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_MERGEABLE_RETRY_MS=0 \
             FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" landable "$1" --repo FS.GG.SDD --wait --tries "${2:-3}" --interval 0 >/dev/null 2>&1 \
             || rc=$?; printf '%s' "$rc"; }

  # 1. --wait AGREES with the single-shot verdict on a SETTLED PR. 801 is the superseded-but-green PR: its
  #    run set does not grow, so the count is stable on the second poll and the green is believed.
  wr="$(lndw 801)"
  [ "$wr" = "0" ] \
    && ok "#724: --wait returns GREEN (exit 0) on a settled, superseded-but-green PR (case 31)" \
    || bad "#724: --wait settled green -> 0" "got: $wr"

  # 2. TRAP ONE — THE REGISTRATION RACE. 804 has ZERO runs, forever. Zero runs score red (#606), but a
  #    waiter must read that as "CI has not started YET" and keep waiting; only when the runs never register
  #    (tries exhausted) does the red stand — the honest #606 finding. Engine red is exit 3 (bash 1, disposed).
  wr="$(lndw 804 2)"
  [ "$wr" = "3" ] \
    && ok "#724: zero runs is 'CI has not started', not 'CI failed' — but if they never register, RED (exit 3) (case 31)" \
    || bad "#724: --wait registration race -> 3" "got: $wr"

  # 3. TRAP TWO — THE PARTIAL ROLLUP, the one that MERGES A BAD PR. sha810 grows: the first poll sees one
  #    green run, the next sees that run PLUS a failed one. A waiter that trusts the first all-green returns
  #    green; the engine waits for the set to STOP GROWING and returns RED (exit 3). Invoked ONCE — the
  #    fixture is stateful, so a second call would score against an already-grown set.
  wr="$(lndw 810 4)"
  [ "$wr" = "3" ] \
    && ok "#724: --wait does NOT believe an early all-green — it waits for the run set to STOP GROWING (exit 3) (case 31)" \
    || bad "#724: --wait growing set -> 3" "got: $wr"

  # 4. A CONFLICTED PR gets no CI at all (GitHub cannot build refs/pull/N/merge while it conflicts), so
  #    waiting on one waits forever. It must come back AT ONCE — --tries 30 with no wall-clock proves it did
  #    not spin. Engine conflicted is exit 3 (bash 1, disposed).
  wr="$(lndw 704 30)"
  [ "$wr" = "3" ] \
    && ok "#724: --wait returns CONFLICTED immediately (exit 3) — no amount of waiting fixes a conflict (case 31)" \
    || bad "#724: --wait conflicted -> 3" "got: $wr"

  kill "$WAIT_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 32 (#737) — `--require NAME` / `--sha SHA`, the assertions the CALLER adds to the verdict.
#
# These are what let the LAST hand-rolled copy of this gate — `skill-registry-autofix.yml`, which merges
# unattended — call `landable` rather than carry its own rollup (#724). Each closes a hole the command
# could not see:
#
#   --require  the check that DECIDES the bot's PR (`registry-coherence`) is NOT required by branch
#              protection and must not be (#549), so nothing else ever looks at it — and an ABSENT check
#              reads exactly like a passing one to any "is anything red?" rollup (#606).
#   --sha      the bot force-pushes and then gates; `pulls/{n}` lags, so for a moment it names the
#              PREVIOUS commit, whose checks are green and are about code that would not be merged.
#
# THE LOAD-BEARING LEGS ARE THE PAIRS (902 and 905): the SAME fixture world scores GREEN without the flag
# and PENDING with it. Anything less than that contrast would not prove the flag does anything.
#
# An unmet assertion is PENDING (exit 7), never green: it is usually transient (registration, a superseded
# suite's replacement, GitHub catching up with a push), so `--wait` rides it out and refuses when the tries
# run out. Single-shot here — one read, one verdict, no wall-clock.
# ==================================================================================================
REQ_OUT="$(mktemp)"; python3 "$HERE/landable_require_server.py" >"$REQ_OUT" 2>/dev/null & REQ_SRV=$!; REQ_PORT=""
for _ in $(seq 1 50); do REQ_PORT="$(head -n1 "$REQ_OUT" 2>/dev/null)"; [ -n "$REQ_PORT" ] && break; sleep 0.1; done
rm -f "$REQ_OUT"
if [ -z "$REQ_PORT" ]; then bad "landable-require fixture bound a port"; else
  # `lndr <pr> [extra flags...]` -> the verdict WORD on stdout. `lndr_rc` -> the exit code.
  lndr() { local pr="$1"; shift; FSGG_GITHUB_API_BASE="http://127.0.0.1:$REQ_PORT" GITHUB_TOKEN=t \
             FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
             FSGG_COORD_MERGEABLE_RETRY_MS=0 FSGG_COORD_CACHE="$(mktemp -d)" \
             "$ENGINE" landable "$pr" --repo FS.GG.SDD "$@" 2>/dev/null || true; }
  lndr_rc() { local pr="$1" rc=0; shift; FSGG_GITHUB_API_BASE="http://127.0.0.1:$REQ_PORT" GITHUB_TOKEN=t \
                FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
                FSGG_COORD_MERGEABLE_RETRY_MS=0 FSGG_COORD_CACHE="$(mktemp -d)" \
                "$ENGINE" landable "$pr" --repo FS.GG.SDD "$@" >/dev/null 2>&1 || rc=$?; printf '%s' "$rc"; }

  # 1. The required check reported and is green — so is the PR. --require must not make a green PR unlandable.
  r="$(lndr 901 --require registry-coherence)"
  [ "$r" = "green" ] \
    && ok "#737: --require is satisfied by a green check of that name -> green (case 32)" \
    || bad "#737: --require satisfied -> green" "got: $r"

  # 2. THE LOAD-BEARING LEG. 902 is green on every check it HAS, and the subject is absent. Without
  #    --require that is a GREEN — the merge the bot's own gate exists to refuse (#642/#425).
  r="$(lndr 902)"
  [ "$r" = "green" ] \
    && ok "#737: WITHOUT --require, a PR whose subject never reported is GREEN — the #606 hole (case 32)" \
    || bad "#737: 902 without --require -> green" "got: $r"

  # ...and WITH it, the same world is pending. The flag is the whole difference.
  r="$(lndr 902 --require registry-coherence)"
  [ "$r" = "pending" ] \
    && ok "#737: WITH --require, the SAME world is PENDING — an absent check is never a green (case 32)" \
    || bad "#737: 902 with --require -> pending" "got: $r"

  # ...and it is exit 7 (pending), not 0. The exit code is what the bot's `|| exit` reads.
  r="$(lndr_rc 902 --require registry-coherence)"
  [ "$r" = "7" ] \
    && ok "#737: an unmet --require exits 7 (pending), never 0 (case 32)" \
    || bad "#737: 902 --require -> exit 7" "got: $r"

  # 3. A RED check outranks a missing required one. `red` is settled and must be reported AT ONCE; making
  #    --wait spin out its whole budget before announcing a failure it already knew would be a nuisance
  #    gate, and a nuisance gate is one people learn to skip (#498).
  r="$(lndr 903 --require registry-coherence)"
  [ "$r" = "red" ] \
    && ok "#737: a RED check outranks a missing required one — a finding is not a 'not yet' (case 32)" \
    || bad "#737: 903 -> red" "got: $r"

  # 4. A SUPERSEDED copy of the required check does NOT satisfy it (#710). The cancelled suite's
  #    registry-coherence is dropped with its run — and it is exactly the check whose verdict we lack.
  r="$(lndr 904 --require registry-coherence)"
  [ "$r" = "pending" ] \
    && ok "#737: a SUPERSEDED copy of the required check does NOT satisfy it (case 32)" \
    || bad "#737: 904 -> pending" "got: $r"

  # 5. THE OTHER LOAD-BEARING LEG. 905's PR still names shaOld, whose checks are green. Without --sha the
  #    command takes the PR's head on trust and scores the OLD commit: GREEN. That is the read the bot's
  #    gate avoids by pinning `git rev-parse HEAD`, and it is why --sha exists.
  r="$(lndr 905)"
  [ "$r" = "green" ] \
    && ok "#737: WITHOUT --sha, a lagging PR object scores the PREVIOUS commit's green checks (case 32)" \
    || bad "#737: 905 without --sha -> green" "got: $r"

  # ...and naming the commit we MEAN refuses it until GitHub catches up.
  r="$(lndr 905 --sha shaNew)"
  [ "$r" = "pending" ] \
    && ok "#737: WITH --sha, a PR that still names another head is PENDING, never green (case 32)" \
    || bad "#737: 905 with --sha -> pending" "got: $r"

  # ...and --sha naming the head the PR ACTUALLY has is a no-op: the assertion is met, so it scores as before.
  r="$(lndr 905 --sha shaOld)"
  [ "$r" = "green" ] \
    && ok "#737: --sha that AGREES with the PR's head changes nothing (case 32)" \
    || bad "#737: 905 --sha shaOld -> green" "got: $r"

  # 6. The pending verdict SAYS WHICH assertion is unmet, on stderr. One word is honest and useless on the
  #    case that never resolves (a RENAMED job): the operator is left with no thread to pull.
  err="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$REQ_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
           FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_MERGEABLE_RETRY_MS=0 \
           FSGG_COORD_CACHE="$(mktemp -d)" \
           "$ENGINE" landable 902 --repo FS.GG.SDD --require registry-coherence 2>&1 >/dev/null || true)"
  printf '%s' "$err" | grep -q 'registry-coherence' \
    && ok "#737: a pending on an unmet assertion NAMES it on stderr (case 32)" \
    || bad "#737: pending names the check" "got: $err"

  kill "$REQ_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 24 — THE LOCK FAILS CLOSED UNDER ADVERSARIAL INTERLEAVINGS.
#
# Case 24's hardest legs are the interleavings in which two workers could end up believing they hold ONE
# item — the failure the whole ADR-0027 protocol exists to prevent. The engine already implements the
# fail-closed behaviour (a forged marker does not hold; a malformed one BLOCKS; an expired worker cannot
# resurrect its claim; a failed or empty CAS re-read is a LOSS, never an orphaned lock); this drives those
# certified answers through the compiled binary over HTTP, with no bash in the pipeline. The letters match
# the legs in `tests/fsgg-coord/cases/24-issue-boundary-adversarial.sh`.
#
# `casadversarial_server.py` is one FS.GG.SDD world: each issue carries the marker state its leg needs, and
# legs (g)/(i) MUTATE it — `claim` POSTs a marker, the re-read faults (g) or comes back empty (i), and the
# withdraw DELETEs the marker it posted. `/_deletes` proves the failed CAS removed our OWN marker (no
# orphan); `/_patches` proves a refused heartbeat patched NOTHING.
#
# Disposed on the record (ADR-0040 §5): where the engine's wording differs from bash's literal, the
# PROPERTY is asserted, not the spelling — (c) `held by heron-b71` vs `worker 'heron-b71' does` (both name
# the holder); (d) `claim --force` vs `fsgg-coord claim` (both point at re-claiming); (f) `unparseable
# lock` vs `unparsed-marker` (both BLOCK the item); (g) `could not take … a LOSS` vs `removed our marker`/
# `nothing was claimed` (both DELETE the posted marker at `/_deletes` and claim NOTHING via a non-zero exit).
# ==================================================================================================
ADV_OUT="$(mktemp)"; python3 "$HERE/casadversarial_server.py" >"$ADV_OUT" 2>/dev/null & ADV_SRV=$!; ADV_PORT=""
for _ in $(seq 1 50); do ADV_PORT="$(head -n1 "$ADV_OUT" 2>/dev/null)"; [ -n "$ADV_PORT" ] && break; sleep 0.1; done
rm -f "$ADV_OUT"
if [ -z "$ADV_PORT" ]; then bad "cas-adversarial fixture bound a port"; else
  ADV_BASE="http://127.0.0.1:$ADV_PORT"
  adv() { FSGG_GITHUB_API_BASE="$ADV_BASE" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
            FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
            "$ENGINE" "$@"; }
  # Capture stdout+stderr AND the exit code in ONE invocation into `ADV_OUT`/`ADV_RC`. The legs below
  # drive stateful, MUTATING commands (claim POSTs and withdraws), so running a command twice — once for
  # its text, once for its exit code — would exercise a DIFFERENT read on the second call (e.g. #90's
  # re-read fault would land on the second run's INITIAL read). One invocation per leg keeps the exit-code
  # assertion and the text assertion about the SAME run.
  adv_cap() { ADV_OUT="$(adv "$@" 2>&1)"; ADV_RC=$?; }

  # (e) A claim marker QUOTED inside a message does not forge a lock — the marker is only a marker at the
  #     START of a comment body (`^<!--\s*fsgg:claim`). #88 carries a `fsgg:msg` that quotes a claim marker
  #     in prose and no real marker, so the item is FREE and claims cleanly.
  adv_cap claim 'FS.GG.SDD#88' --worker vole-c88
  { [ "$ADV_RC" = "0" ] && printf '%s' "$ADV_OUT" | grep -q 'claimed FS.GG.SDD#88'; } \
    && ok "case24(e): a marker quoted inside a message does NOT hold the item — it is still claimable" \
    || bad "case24(e): forged-marker-in-message must not block the claim" "rc=$ADV_RC got: $ADV_OUT"

  # (f) A marker we cannot parse a worker out of FAILS CLOSED: it BLOCKS the item rather than reading as
  #     free (a lock you cannot read is still a lock). #89's marker has no `worker=`. Engine says
  #     "unparseable lock" (bash: "unparsed-marker" — disposed as the property).
  adv_cap claim 'FS.GG.SDD#89' --worker vole-c89
  [ "$ADV_RC" != "0" ] \
    && ok "case24(f): a malformed marker BLOCKS the item (claim fails closed, non-zero)" \
    || bad "case24(f): a malformed marker must block the claim" "rc=$ADV_RC $ADV_OUT"
  printf '%s' "$ADV_OUT" | grep -q 'unparseable lock' \
    && ok "case24(f): ...and the refusal names the unparseable marker (bash 'unparsed-marker', disposed)" \
    || bad "case24(f): refusal must name the unparseable lock" "$ADV_OUT"

  # (c) THE RESURRECTION BUG. A worker whose lease expired must NOT heartbeat its marker back to life once
  #     another worker legitimately holds the item — it must be told to STOP. #86 carries ghost-222 STALE
  #     under heron-b71 FRESH.
  adv_cap heartbeat 'FS.GG.SDD#86' --worker ghost-222
  [ "$ADV_RC" != "0" ] \
    && ok "case24(c): an expired worker cannot resurrect its claim under a NEW holder (heartbeat refused)" \
    || bad "case24(c): heartbeat under a new holder must fail" "rc=$ADV_RC $ADV_OUT"
  printf '%s' "$ADV_OUT" | grep -q 'heron-b71' && printf '%s' "$ADV_OUT" | grep -q 'STOP working' \
    && ok "case24(c): ...it names the worker that now holds it (heron-b71) and says STOP working it" \
    || bad "case24(c): refusal must name the holder and say STOP working" "$ADV_OUT"
  # ...and the refused renew patched NOTHING — the fixture recorded zero comment PATCHes.
  [ "$(curl -s "$ADV_BASE/_patches")" = "[]" ] \
    && ok "case24(c): the refused renew patched NOTHING (no comment PATCH reached the fixture)" \
    || bad "case24(c): a refused heartbeat must not PATCH" "$(curl -s "$ADV_BASE/_patches")"

  # (d) An expired lease is refused even when nobody else took the item — the promise lapsed; re-claim.
  adv_cap heartbeat 'FS.GG.SDD#87' --worker ghost-333
  [ "$ADV_RC" != "0" ] \
    && ok "case24(d): an expired lease cannot be renewed in place (heartbeat refused)" \
    || bad "case24(d): an expired lease heartbeat must fail" "rc=$ADV_RC $ADV_OUT"
  printf '%s' "$ADV_OUT" | grep -q 'EXPIRED' && printf '%s' "$ADV_OUT" | grep -qi 'claim' \
    && ok "case24(d): ...it says the lease EXPIRED and points at re-claiming (bash 'fsgg-coord claim', disposed)" \
    || bad "case24(d): refusal must say EXPIRED and name the re-claim remedy" "$ADV_OUT"

  # (g) A transient read failure on the CAS re-read must not ORPHAN the marker we just posted — an orphaned
  #     live marker blocks every other worker for a full lease while nobody works the item. #90 starts
  #     empty; its re-read FAULTS (502). The claim withdraws its own marker (ONE invocation — the fixture
  #     faults every #90 read after the first, so a second run would fault the initial read instead).
  del_before="$(curl -s "$ADV_BASE/_deletes")"
  adv_cap claim 'FS.GG.SDD#90' --worker teal-e55
  [ "$ADV_RC" != "0" ] \
    && ok "case24(g): a failed CAS re-read is a LOSS (claim exits non-zero — nothing was claimed)" \
    || bad "case24(g): a failed CAS re-read must not announce a claim" "rc=$ADV_RC $ADV_OUT"
  # The posted marker was DELETEd — "removed our marker" (bash), proven at the transport: /_deletes grew.
  del_after="$(curl -s "$ADV_BASE/_deletes")"
  [ "$del_after" != "$del_before" ] \
    && ok "case24(g): ...and it REMOVED our own marker — no orphan survives the failed re-read (a DELETE fired)" \
    || bad "case24(g): a failed re-read must withdraw the just-posted marker" "before=$del_before after=$del_after"

  # (i) THE FAIL-OPEN. If the CAS re-read shows NO live marker, our own marker is missing — a peer collected
  #     it, or the read lagged our write. We cannot demonstrate we hold the lock, so we must NOT announce
  #     that we do. "We cannot tell" is a LOSS. #92 starts empty and its re-read stays empty (vanished).
  adv_cap claim 'FS.GG.SDD#92' --worker teal-e55
  [ "$ADV_RC" != "0" ] \
    && ok "case24(i): an empty CAS re-read is a LOSS, not a win (claim exits non-zero)" \
    || bad "case24(i): an empty CAS re-read must not be a win" "rc=$ADV_RC $ADV_OUT"
  printf '%s' "$ADV_OUT" | grep -q 'marker vanished' \
    && ok "case24(i): ...it says the marker vanished" \
    || bad "case24(i): refusal must say the marker vanished" "$ADV_OUT"
  case "$ADV_OUT" in
    *"claimed FS.GG.SDD#92"*) bad "case24(i): must not announce a lock it cannot show" "$ADV_OUT" ;;
    *) ok "case24(i): ...and it does NOT announce a lock it cannot show" ;;
  esac

  # The workers whose claim markers sit on an issue right now — the `worker=` of every LIVE marker the
  # /comments read serves back (the fixture reflects the DELETEs, so a collected stale marker is gone).
  adv_workers_on() { curl -s "$ADV_BASE/repos/FS-GG/FS.GG.SDD/issues/$1/comments" \
                       | jq -r '[.[].body | capture("worker=(?<w>[^\\s>]+)").w] | join(",")' 2>/dev/null; }
  # Messages addressed `to=<w>` on an issue — the collect NOTIFY is an ordinary `fsgg:msg` comment.
  adv_msgto() { curl -s "$ADV_BASE/repos/FS-GG/FS.GG.SDD/issues/$1/comments" \
                  | jq --arg w "$2" '[.[] | select(.body|test("fsgg:msg")) | select(.body|test("to="+$w))] | length' 2>/dev/null; }

  # (a) A stale marker must be COLLECTED by the next claimant, never merely ignored — an ignored marker is
  #     what `heartbeat` later resurrects underneath the new holder (two live markers, one item). #84 carries
  #     ghost-111's STALE claim; heron-b71 claims over it (--force to skip the #516 pre-hold scan).
  adv_cap claim 'FS.GG.SDD#84' --worker heron-b71 --force
  printf '%s' "$ADV_OUT" | grep -q "collected worker 'ghost-111' expired claim" \
    && ok "case24(a): claim COLLECTS the stale marker it claims over (names the evicted worker)" \
    || bad "case24(a): claim must collect the stale marker it claims over" "$ADV_OUT"
  [ "$(adv_workers_on 84)" = "heron-b71" ] \
    && ok "case24(a): ...and exactly ONE marker survives — the stale one is gone" \
    || bad "case24(a): exactly one marker must survive" "workers: $(adv_workers_on 84)"
  [ "$(adv_msgto 84 ghost-111)" = "1" ] \
    && ok "case24(a): ...and the collected worker is TOLD, not silently evicted (a fsgg:msg to=ghost-111)" \
    || bad "case24(a): the collected worker must be notified" "msgs to=ghost-111: $(adv_msgto 84 ghost-111)"

  # (b) Re-claiming when MY OWN marker went stale must renew a SINGLE marker, not mint a second. #85 carries
  #     otter-b55's own STALE claim; otter-b55 re-claims (no --force — it holds nothing else live).
  adv_cap claim 'FS.GG.SDD#85' --worker otter-b55
  { [ "$ADV_RC" = "0" ] && [ "$(adv_workers_on 85)" = "otter-b55" ]; } \
    && ok "case24(b): a worker whose OWN marker went stale ends with ONE marker (a renew, not a duplicate)" \
    || bad "case24(b): a self-renew must leave exactly one marker" "rc=$ADV_RC workers: $(adv_workers_on 85)"
  # Renewing your OWN stale marker is not an eviction to announce — you do not message yourself.
  case "$ADV_OUT" in
    *"collected worker 'otter-b55'"*) bad "case24(b): a self-renew must not announce collecting itself" "$ADV_OUT" ;;
    *) ok "case24(b): ...and it does NOT announce collecting its own marker (no self-eviction)" ;;
  esac

  # (l) Two claimants collecting the SAME expired marker: the loser's collect DELETE 404s because the winner
  #     already removed it. "Already gone" is the goal state of a collector, so the claim still WINS. #95's
  #     ghost-444 marker DELETEs with a 404 (GH_DELETE_404=818, the corpus's model).
  adv_cap claim 'FS.GG.SDD#95' --worker heron-b71 --force
  { [ "$ADV_RC" = "0" ] && printf '%s' "$ADV_OUT" | grep -q 'claimed FS.GG.SDD#95'; } \
    && ok "case24(l): a 404 collecting an already-gone stale marker is NOT fatal — the claim wins" \
    || bad "case24(l): a benign 404 on collection must not fail the claim" "rc=$ADV_RC $ADV_OUT"
  [ "$(adv_workers_on 95)" = "heron-b71" ] \
    && ok "case24(l): ...and 'already gone' leaves exactly the new holder" \
    || bad "case24(l): a concurrent-GC 404 must leave only the new holder" "workers: $(adv_workers_on 95)"

  # (j) A marker bearing OUR id is NOT proof it is ours — rules 4/5 (#419) can hand one id to several
  #     workers — and the re-claim (heartbeat) path bypasses the CAS entirely, so it is exactly where a
  #     same-id sibling silently adopts another worker's lock. It must WARN there, not only on the fresh
  #     path. #93 carries a FRESH marker whose worker id is DERIVED from a shared claude-code session id
  #     (`Identity.nameFromSeed`); re-claiming under that SAME session (no --worker — the id comes from
  #     CLAUDE_CODE_SESSION_ID) renews the ONE marker in place (a PATCH, not a duplicate) and warns.
  #     Disposed on the record (ADR-0040 §5): the engine's wording differs from bash's literal — engine
  #     `held … (lease renewed)` + `NOTE — … adopted ITS lock` + `WARNING — … may not be unique to this
  #     worker`, bash the same three strings — the PROPERTY is asserted (renew in place, one marker, warn
  #     the shared-id hazard), not the exact spelling.
  j93="$(FSGG_GITHUB_API_BASE="$ADV_BASE" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
           FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
           env -u FSGG_WORKER -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
               CLAUDE_CODE_SESSION_ID=309bd638-8a1c-42b7-952b-898efb8d1064 \
           "$ENGINE" claim 'FS.GG.SDD#93' 2>&1 || true)"
  printf '%s' "$j93" | grep -q 'lease renewed' \
    && ok "case24(j): a re-claim of our own live marker RENEWS it in place (lease renewed)" \
    || bad "case24(j): a re-claim must renew in place, not duplicate" "$j93"
  printf '%s' "$j93" | grep -q 'adopted ITS lock' \
    && ok "case24(j): ...and WARNS it never ran the CAS (adopted ITS lock)" \
    || bad "case24(j): a re-claim under a shared id must warn 'adopted ITS lock'" "$j93"
  printf '%s' "$j93" | grep -q 'may not be unique to this worker' \
    && ok "case24(j): ...and names the shared-id hazard (may not be unique to this worker)" \
    || bad "case24(j): a re-claim must name the shared-id hazard" "$j93"
  j93n="$(curl -s "$ADV_BASE/repos/FS-GG/FS.GG.SDD/issues/93/comments" | jq '[.[]|select(.body|test("fsgg:claim"))]|length' 2>/dev/null)"
  [ "$j93n" = "1" ] \
    && ok "case24(j): ...and still exactly ONE marker (a renew, not a duplicate)" \
    || bad "case24(j): a re-claim must leave exactly one marker" "markers on #93: $j93n"

  kill "$ADV_SRV" 2>/dev/null
fi

# ==================================================================================================
# case 24 (legs h + m) — reap's MUTATING interleavings: it does not cause the double-hold it CLEANS UP,
# and a failed delete is REPORTED, not swallowed.
#
# `Reapable` is a SNAPSHOT verdict — proven against the scan's read — so `reap` RE-VERIFIES the marker's
# freshness immediately before breaking the lock, and DELETES before it would ever notify. One
# `reap --repo FS.GG.SDD --apply` over `reap_race_server.py`'s two-item world drives both legs:
#   (h) #91  the holder HEARTBEATED between the scan and the delete (the marker's `updated_at` flips
#            stale→fresh on the RE-VERIFY read) → reap SKIPS it: "renewed since the scan", marker SURVIVES.
#            This is `GH_REAP_RACE=91` re-expressed at the HTTP layer.
#   (m) #96  the marker's DELETE FAILS (500, `GH_FAIL_DELETE=819`) → reap REPORTS "FAILED", LEAVES the
#            marker (still held), and does NOT tell the worker (reap posts no notify — the delete comes
#            first, so a failed delete never leaves a worker told-to-stop over a marker that still holds).
#
# Disposed on the record (ADR-0040 §5): the engine's reap posts NO notify (leg m's "worker not notified"
# is structural, not an ordering it could get wrong), and its FAILED/skipped wording is its own — the
# PROPERTY is asserted (a renewed lock is skipped and survives; a failed delete is reported and the marker
# stands), counted at the HTTP layer via `/_deletes` and the /comments read-back.
# ==================================================================================================
RR_OUT="$(mktemp)"; python3 "$HERE/reap_race_server.py" >"$RR_OUT" 2>/dev/null & RR_SRV=$!; RR_PORT=""
for _ in $(seq 1 50); do RR_PORT="$(head -n1 "$RR_OUT" 2>/dev/null)"; [ -n "$RR_PORT" ] && break; sleep 0.1; done
rm -f "$RR_OUT"
if [ -z "$RR_PORT" ]; then bad "reap-race fixture bound a port"; else
  RR_BASE="http://127.0.0.1:$RR_PORT"
  rr_workers_on() { curl -s "$RR_BASE/repos/FS-GG/FS.GG.SDD/issues/$1/comments" \
                      | jq -r '[.[].body | capture("worker=(?<w>[^\\s>]+)").w] | join(",")' 2>/dev/null; }
  rr_out="$(FSGG_GITHUB_API_BASE="$RR_BASE" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
              "$ENGINE" reap --repo FS.GG.SDD --apply 2>&1 || true)"

  # (h) A claim renewed between the scan and the delete is SKIPPED, and its marker SURVIVES.
  printf '%s' "$rr_out" | grep -q 'renewed since the scan' \
    && ok "case24(h): reap RE-VERIFIES — a claim renewed between the scan and the delete is SKIPPED" \
    || bad "case24(h): a claim renewed since the scan must be skipped" "$rr_out"
  [ "$(rr_workers_on 91)" = "finch-a3f" ] \
    && ok "case24(h): ...and its marker SURVIVES the reap (finch-a3f still holds #91)" \
    || bad "case24(h): a renewed claim's marker must survive" "workers on #91: $(rr_workers_on 91)"
  # The skip DELETED nothing on #91 — the re-verify short-circuited before the break.
  [ "$(curl -s "$RR_BASE/_deletes" | jq 'index(816)')" = "null" ] \
    && ok "case24(h): ...and reap DELETED nothing on the renewed claim (no 816 in /_deletes)" \
    || bad "case24(h): a skipped reap must delete nothing" "deletes: $(curl -s "$RR_BASE/_deletes")"

  # (m) A failed DELETE is REPORTED ("FAILED"), the marker STAYS, and the worker is NOT told (reap deletes
  #     before it would notify, and this engine's reap posts no notify — so a failed delete strands nobody).
  printf '%s' "$rr_out" | grep -q 'FAILED' \
    && ok "case24(m): reap reports a failed delete ('FAILED'), it is not swallowed" \
    || bad "case24(m): a failed delete must be reported" "$rr_out"
  [ "$(rr_workers_on 96)" = "ghost-555" ] \
    && ok "case24(m): ...and a failed delete leaves the marker in place (ghost-555 still holds #96)" \
    || bad "case24(m): a failed delete must leave the marker" "workers on #96: $(rr_workers_on 96)"
  # No fsgg:msg addressed to ghost-555 anywhere — reap posts no notify, so nothing told the worker it was
  # released while its marker still held the item (the ordering leg m guards, structural in the engine).
  [ "$(curl -s "$RR_BASE/repos/FS-GG/FS.GG.SDD/issues/96/comments" | jq '[.[]|select(.body|test("fsgg:msg"))|select(.body|test("to=ghost-555"))]|length')" = "0" ] \
    && ok "case24(m): ...and does NOT tell the worker it was released (no fsgg:msg to=ghost-555)" \
    || bad "case24(m): a failed reap must not notify the worker" "messages to ghost-555 present"

  kill "$RR_SRV" 2>/dev/null
fi

# ==================================================================================================
# CASE 43 (kit-digest-and-argv) — THE LAST CORPUS CASE. Two guarantees, and case 43 is FULL with them.
# ==================================================================================================
# (A) THE KIT-DIGEST OBLIGATION IS OBSERVED, NOT INFERRED (#469/#563/#588). `registry/repos.lock` pins a
# content digest of every kit source (ADR-0019, #527); editing one and not relocking reds `main`. The
# warning that names it used to INFER the obligation from what a worker DECLARED ("is `registry/repos.yml`
# in your touch-set?"), which FAILED OPEN after #527 moved the digests into the generated `repos.lock`:
# declaring `repos.yml` silenced the warning while the lock was still stale. A DECLARATION is not the
# obligation; a MATCHING DIGEST is — so the engine recomputes the digest off the tree and LOOKS. That read
# is a pure filesystem read (`FSGG_KIT_ROOT` stands a throwaway tree up), independent of the transport; the
# fixture exists only so the `widen` it rides can LAND (#706 requires the widener to hold the lock). #74 is
# held by kite-469 with no neighbour, so the widen lands and the #353 re-check is DISJOINT (exit 0).
KITROOT="$(mktemp -d)/kitroot"
mkdir -p "$KITROOT/.claude/skills/pnext-item" "$KITROOT/.agents/skills/pnext-item" \
         "$KITROOT/scripts" "$KITROOT/registry"
# (re)write the tree and relock it, so the lock is HONEST before each scenario (the corpus's `kit_seed`).
kit_seed() {
  printf 'skill body v1\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
  printf '#!/usr/bin/env bash\n# client v1\n' >"$KITROOT/scripts/fsgg-coord"
  { printf '# registry/repos.lock — GENERATED.\n'
    printf '%s  .claude/skills/pnext-item\n' "$(sha256sum "$KITROOT/.claude/skills/pnext-item/SKILL.md" | cut -d' ' -f1)"
    printf '%s  scripts/fsgg-coord\n'        "$(sha256sum "$KITROOT/scripts/fsgg-coord" | cut -d' ' -f1)"
  } >"$KITROOT/registry/repos.lock"
}
KIT_OUT="$(mktemp)"; python3 "$HERE/kit_server.py" >"$KIT_OUT" 2>/dev/null & KIT_SRV=$!; KIT_PORT=""
for _ in $(seq 1 50); do KIT_PORT="$(head -n1 "$KIT_OUT" 2>/dev/null)"; [ -n "$KIT_PORT" ] && break; sleep 0.1; done
rm -f "$KIT_OUT"
if [ -z "$KIT_PORT" ]; then bad "kit fixture bound a port"; else
  # widen with an EXPLICIT kit root, so the digest can be made genuinely stale.
  kd() { FSGG_KIT_ROOT="$KITROOT" FSGG_GITHUB_API_BASE="http://127.0.0.1:$KIT_PORT" GITHUB_TOKEN=t \
           FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
           FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" widen "$1" --worker kite-469 --paths "$2" 2>&1; }

  # NEGATIVE CONTROL FIRST — a tree whose lock MATCHES must produce NO warning. If this ever goes green by
  # accident (a broken root, an unreadable lock), every positive assertion below is vacuous (#563).
  kit_seed; w_clean="$(kd 'FS.GG.SDD#74' 'scripts/fsgg-coord')"
  printf '%s' "$w_clean" | grep -q 'KIT DIGEST' \
    && bad "#563: a lock that MATCHES must NOT warn — the obligation is met (case 43)" "$w_clean" \
    || ok "#563: a lock that MATCHES must NOT warn — the obligation is met (case 43)"

  # (1) A STALE CLIENT digest is OBSERVED and named — regardless of what the touch-set declares.
  kit_seed; printf '# client v2 — edited\n' >>"$KITROOT/scripts/fsgg-coord"
  w469="$(kd 'FS.GG.SDD#74' 'scripts/fsgg-coord, tests/fsgg-coord/run.sh')"
  printf '%s' "$w469" | grep -q 'KIT DIGEST' \
    && ok "#469: widen NAMES a kit source whose digest is now STALE (case 43)" || bad "#469 KIT DIGEST" "$w469"
  printf '%s' "$w469" | grep -q 'scripts/fsgg-coord' \
    && ok "#469: ...naming the stale source itself (case 43)" || bad "#469 names the stale source" "$w469"
  printf '%s' "$w469" | grep -q 'repos.sh relock' \
    && ok "#469: ...and prints the CURRENT regenerate command (case 43)" || bad "#469 relock command" "$w469"
  printf '%s' "$w469" | grep -q 'repos-registry-selftest' \
    && ok "#469: ...and says which gate will red main (case 43)" || bad "#469 names the gate" "$w469"
  printf '%s' "$w469" | grep -q 'do NOT reserve it' \
    && ok "#469: ...and says NOT to reserve the generated lock (#309/#527) (case 43)" || bad "#469 do-not-reserve" "$w469"
  # #588: the advice must NOT name `repos.sh digest` — it still exists but writes nothing now.
  printf '%s' "$w469" | grep -q 'repos.sh digest' \
    && bad "#588: the advice must not name repos.sh digest — it writes nothing now (case 43)" "$w469" \
    || ok "#588: the advice does NOT name the no-op repos.sh digest (case 43)"
  # ...and it STILL widens. Advisory, never fatal: `repos-registry-selftest` is the authority.
  printf '%s' "$w469" | grep -q 'widened FS.GG.SDD#74' \
    && ok "#469: ...while STILL widening (advisory, not fatal) (case 43)" || bad "#469 still widens" "$w469"

  # (2) THE FAIL-OPEN, PINNED. Declaring `registry/repos.yml` used to SILENCE this. It must NOT: the lock
  #     is still stale, and main is still red. This is the assertion #563 exists for.
  w_yml="$(kd 'FS.GG.SDD#74' 'scripts/fsgg-coord, registry/repos.yml')"
  printf '%s' "$w_yml" | grep -q 'KIT DIGEST' \
    && ok "#563: declaring registry/repos.yml must NOT silence a genuinely stale lock (case 43)" \
    || bad "#563 repos.yml must not silence" "$w_yml"

  # (3) A STALE SKILL digest is observed too — the coupling is not client-specific.
  kit_seed; printf 'skill body v2\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  cp "$KITROOT/.claude/skills/pnext-item/SKILL.md" "$KITROOT/.agents/skills/pnext-item/SKILL.md"
  w469s="$(kd 'FS.GG.SDD#74' '.claude/skills/pnext-item/**')"
  printf '%s' "$w469s" | grep -q '.claude/skills/pnext-item' \
    && ok "#469: a SKILL source is content-addressed too, and is named (case 43)" || bad "#469 skill source named" "$w469s"

  # (4) SKILL ROOTS — the byte-identical union (ADR-0011/0014) is OBSERVED. Edit one root and not the other:
  #     the `roots` gate reds main, and the tool must say so with the mirror command that fixes it.
  kit_seed; printf 'skill body v2 — one root only\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  w_roots="$(kd 'FS.GG.SDD#74' '.claude/skills/pnext-item/**')"
  printf '%s' "$w_roots" | grep -q 'SKILL ROOTS' \
    && ok "#563: diverged skill roots are NAMED (case 43)" || bad "#563 SKILL ROOTS" "$w_roots"
  printf '%s' "$w_roots" | grep -q '.agents/skills/pnext-item' \
    && ok "#563: ...with the mirror command that fixes it (case 43)" || bad "#563 mirror command" "$w_roots"
  # ...and a CLIENT kit has no mirror, so a client-only staleness must NOT nag about roots.
  kit_seed; printf '# client v2\n' >>"$KITROOT/scripts/fsgg-coord"
  w_client="$(kd 'FS.GG.SDD#74' 'scripts/fsgg-coord')"
  printf '%s' "$w_client" | grep -q 'SKILL ROOTS' \
    && bad "#469: a CLIENT kit must NOT be told to mirror skill roots (case 43)" "$w_client" \
    || ok "#469: a CLIENT kit is NOT told to mirror skill roots (case 43)"

  # (4b) THE ROOTS RULE IS SCOPED TO THE KIT'S OWN SKILLS (#647). The check used to enumerate
  #      `.claude/skills/*/` off the FILESYSTEM, so it policed skills the kit does not own and does not
  #      sync: 33 repo-local skills flagged in FS.GG.Rendering, 28 in FS.GG.SDD, both on GREEN `main`s,
  #      while it stayed silent about the four it actually governs (which were fine). ADR-0014 §4 scopes
  #      the rule to the kit; §1 requires a manifest, never a directory scan. The lock IS that manifest —
  #      `kit_digest_stale` next door already read it — so a repo-local skill cannot reach the check.
  #
  #      THE FIXTURE'S OWN BLIND SPOT IS WHY THIS SHIPPED: KITROOT only ever held the kit's `pnext-item`,
  #      so every root under the glob WAS a kit skill and the two were indistinguishable. Give the
  #      receiver skills of its own — which is the normal state of every repo downstream — and they part.
  kit_seed
  mkdir -p "$KITROOT/.claude/skills/fs-gg-product-layout" "$KITROOT/.agents/skills/fs-gg-product-layout" \
           "$KITROOT/.claude/skills/speckit-analyze"
  # BOTH roots, legitimately differing: the per-agent wrapper line is the POINT of having two roots
  # (specs/227-layout-product-skill/data-model.md pins the Codex-active/Claude-active pair), and
  # `skill-parity` validates them by PAIRING, never by byte-identity.
  printf 'This is the Claude-active wrapper.\n' >"$KITROOT/.claude/skills/fs-gg-product-layout/SKILL.md"
  printf 'This is the Codex-active wrapper.\n'  >"$KITROOT/.agents/skills/fs-gg-product-layout/SKILL.md"
  # `.claude` ONLY — a repo-local skill with no `.agents` twin. ABSENT IS NOT DIVERGED (#610).
  printf 'local only\n' >"$KITROOT/.claude/skills/speckit-analyze/SKILL.md"

  w_local="$(kd 'FS.GG.SDD#74' 'scripts/fsgg-coord')"
  printf '%s' "$w_local" | grep -q 'SKILL ROOTS' \
    && bad "#647: repo-local skills are NOT the kit's business — no roots warning on a green tree (case 43)" "$w_local" \
    || ok "#647: repo-local skills are NOT the kit's business — no roots warning on a green tree (case 43)"
  # The named-and-shamed half, stated separately so a future regression says WHICH arm broke.
  printf '%s' "$w_local" | grep -q 'fs-gg-product-layout' \
    && bad "#647: a repo-local skill differing across BOTH roots must not be named (case 43)" "$w_local" \
    || ok "#647: a repo-local skill differing across BOTH roots is not named (case 43)"
  printf '%s' "$w_local" | grep -q 'speckit-analyze' \
    && bad "#610: a repo-local skill absent from .agents must not be named — absent is not diverged (case 43)" "$w_local" \
    || ok "#610: a repo-local skill absent from .agents is not named — absent is not diverged (case 43)"

  # ...and the scoping must not COST the real signal: a genuinely diverged KIT skill, in that same tree,
  # is still named. A check scoped to nothing would pass every assertion above (#266).
  printf 'skill body v2 — one root only\n' >"$KITROOT/.claude/skills/pnext-item/SKILL.md"
  w_kit="$(kd 'FS.GG.SDD#74' '.claude/skills/pnext-item/**')"
  printf '%s' "$w_kit" | grep -q 'cp .claude/skills/pnext-item/SKILL.md .agents/skills/pnext-item/SKILL.md' \
    && ok "#647: ...while a genuinely diverged KIT skill IS still named, beside them (case 43)" \
    || bad "#647: a diverged KIT skill must still be named" "$w_kit"

  # (4b-ii) A ROOT THIS TREE HAS NOT GOT IS THIS TREE'S DECISION, NOT DRIFT (#647). `AGENT_SKILL_ROOTS` is
  #      configurable — `coordination-sync`'s two roots are its DEFAULT, not a law — so a receiver may hold
  #      the kit in ONE root. Telling it to `cp` a skill into a root it deliberately does not keep would be
  #      this very issue's bug in a narrower coat: a warning, on a green tree, about nobody's mistake. The
  #      scan being replaced got this RIGHT (`Directory.Exists agentsDir || return []`) and the scoping
  #      rewrite is what put it at risk — so it is asserted here rather than left to survive by luck.
  ONEROOT="$(mktemp -d)/oneroot"
  mkdir -p "$ONEROOT/.claude/skills/pnext-item" "$ONEROOT/registry"   # NO .agents root at all
  printf 'skill body\n' >"$ONEROOT/.claude/skills/pnext-item/SKILL.md"
  printf '%s  .claude/skills/pnext-item\n' \
    "$(sha256sum "$ONEROOT/.claude/skills/pnext-item/SKILL.md" | cut -d' ' -f1)" >"$ONEROOT/registry/repos.lock"
  w_one="$(FSGG_KIT_ROOT="$ONEROOT" FSGG_GITHUB_API_BASE="http://127.0.0.1:$KIT_PORT" GITHUB_TOKEN=t \
             FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
             FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" widen 'FS.GG.SDD#74' --worker kite-469 --paths 'x' 2>&1)"
  printf '%s' "$w_one" | grep -q 'SKILL ROOTS' \
    && bad "#647: a receiver holding the kit in ONE root must not be told to create the other (case 43)" "$w_one" \
    || ok "#647: a receiver holding the kit in ONE root is not told to create the other (case 43)"

  # ...but an absent FILE inside a root the tree DOES keep is real drift, and must still be named. The two
  # differ by one `mkdir`, which is the whole distinction — assert it, or the rule above swallows both.
  mkdir -p "$ONEROOT/.agents/skills"
  w_two="$(FSGG_KIT_ROOT="$ONEROOT" FSGG_GITHUB_API_BASE="http://127.0.0.1:$KIT_PORT" GITHUB_TOKEN=t \
             FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
             FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" widen 'FS.GG.SDD#74' --worker kite-469 --paths 'x' 2>&1)"
  printf '%s' "$w_two" | grep -q 'cp .claude/skills/pnext-item/SKILL.md .agents/skills/pnext-item/SKILL.md' \
    && ok "#647: ...while a mirror MISSING from a root the tree does keep is still named (case 43)" \
    || bad "#647: an absent mirror inside an existing root is drift and must be named" "$w_two"

  # (4c) THE REMEDY RUNS THE MIRROR FROM THE DECLARED SOURCE — never backwards over it (#647/#555).
  #      The emitted `cp` used to be hardcoded `.claude` → `.agents`, so in every repo whose source root
  #      is `.agents/` (`materialize-skill-roots.fsx` fans `.agents/` → `.claude/`/`.codex/`) the advice
  #      DESTROYED the source of truth. Direction is now read off the registry's declared `source`, so
  #      declare `.agents/` and the copy must reverse. #555's test ask was never satisfied because
  #      `assert_contains ".agents/skills/pnext-item"` passes for the BROKEN string too — a substring
  #      cannot tell a source from a destination. Assert the ORDERED PAIR.
  BACKROOT="$(mktemp -d)/backroot"
  mkdir -p "$BACKROOT/.claude/skills/pnext-item" "$BACKROOT/.agents/skills/pnext-item" "$BACKROOT/registry"
  printf 'SOURCE OF TRUTH — Codex-active\n' >"$BACKROOT/.agents/skills/pnext-item/SKILL.md"
  printf 'stale mirror\n'                   >"$BACKROOT/.claude/skills/pnext-item/SKILL.md"
  printf '%s  .agents/skills/pnext-item\n' \
    "$(sha256sum "$BACKROOT/.agents/skills/pnext-item/SKILL.md" | cut -d' ' -f1)" >"$BACKROOT/registry/repos.lock"
  w_back="$(FSGG_KIT_ROOT="$BACKROOT" FSGG_GITHUB_API_BASE="http://127.0.0.1:$KIT_PORT" GITHUB_TOKEN=t \
              FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
              FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" widen 'FS.GG.SDD#74' --worker kite-469 --paths 'x' 2>&1)"
  printf '%s' "$w_back" | grep -q 'cp .agents/skills/pnext-item/SKILL.md .claude/skills/pnext-item/SKILL.md' \
    && ok "#647: the remedy copies FROM the declared source root, reversing with it (case 43)" \
    || bad "#647: the remedy must mirror FROM the declared source" "$w_back"
  printf '%s' "$w_back" | grep -q 'cp .claude/skills/pnext-item/SKILL.md .agents/skills/pnext-item/SKILL.md' \
    && bad "#647: the remedy must NEVER copy the mirror back over the declared source (case 43)" "$w_back" \
    || ok "#647: ...and NEVER copies the mirror back over the declared source (case 43)"

  # (5) No lock to read — a RECEIVER repo mirrors the kit but not the registry. Stay silent rather than
  #     nagging every worker in every downstream repo about a file they do not have.
  w469r="$(FSGG_KIT_ROOT="$(dirname "$KITROOT")/no-such-root" FSGG_GITHUB_API_BASE="http://127.0.0.1:$KIT_PORT" \
             GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 \
             FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" widen 'FS.GG.SDD#74' --worker kite-469 --paths 'scripts/fsgg-coord' 2>&1)"
  printf '%s' "$w469r" | grep -q 'KIT DIGEST' \
    && bad "#469: no lock to read -> silent (receiver repos have no registry) (case 43)" "$w469r" \
    || ok "#469: no lock to read -> silent (receiver repos have no registry) (case 43)"

  kill "$KIT_SRV" 2>/dev/null
fi

# (B) THE CLAIM SCAN MUST NOT TRAVEL THROUGH `argv` (#497) — STRUCTURALLY ABSENT IN THE ENGINE, and proven.
# bash's `active_claims` funnelled the whole candidate set back through the jq COMMAND LINE, so once the
# org's open-issue bodies crossed MAX_ARG_STRLEN (128 KiB, July 2026), `execve` returned E2BIG, jq never
# ran, and EVERY claim-aware read (who/reap/batch/take/inbox/widen) died at once — a loud outage (#461
# refused to report the empty set as "nobody holds anything"), but one no waiting would clear. DISPOSED ON
# THE RECORD (ADR-0040 §5, exactly as case 31 leg 9's argv-128 KiB cap): the engine reads each body as JSON
# off `HttpClient` and never marshals the set through argv, so E2BIG is STRUCTURALLY ABSENT. The fixture
# serves a candidate set BIGGER than the cap only to prove the engine READS a real-sized set — the property
# the corpus pins — rather than the plumbing failing at size.
ARGV_OUT="$(mktemp)"; python3 "$HERE/argv_server.py" >"$ARGV_OUT" 2>/dev/null & ARGV_SRV=$!; ARGV_PORT=""
for _ in $(seq 1 50); do ARGV_PORT="$(head -n1 "$ARGV_OUT" 2>/dev/null)"; [ -n "$ARGV_PORT" ] && break; sleep 0.1; done
rm -f "$ARGV_OUT"
if [ -z "$ARGV_PORT" ]; then bad "argv fixture bound a port"; else
  fatwho="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$ARGV_PORT" GITHUB_TOKEN=t FSGG_COORD_OWNER=FS-GG \
              FSGG_COORD_PROJECT=Coordination FSGG_COORD_SCAN_TTL_SEC=0 FSGG_COORD_CACHE="$(mktemp -d)" \
              "$ENGINE" who --repo FS.GG.Audio --json 2>&1 || true)"
  # The fixture really is over the cap — otherwise everything below is vacuous. Three ~50 KiB bodies.
  fatbytes="$(FSGG_GITHUB_API_BASE="http://127.0.0.1:$ARGV_PORT" curl -s "http://127.0.0.1:$ARGV_PORT/repos/FS-GG/FS.GG.Audio/issues" | wc -c)"
  [ "$fatbytes" -gt 131072 ] \
    && ok "#497: the fixture candidate set really exceeds MAX_ARG_STRLEN ($fatbytes > 131072 bytes) (case 43)" \
    || bad "#497: the fixture set must EXCEED 131072 bytes or it tests nothing" "$fatbytes"
  # The scan READS it. Pre-fix bash died with `Argument list too long` / `invalid JSON text` / #461's
  # `cannot read the claim set`; the engine never touches argv, so none of those can appear.
  case "$fatwho" in
    *"cannot read the claim set"*|*"Argument list too long"*|*"--argjson"*)
      bad "#497: a claim set over the arg cap must still be READ, not die (case 43)" "$fatwho" ;;
    *) ok "#497: a claim set over the arg cap is still READ, not died (structurally absent in the engine) (case 43)" ;;
  esac
  [ "$(printf '%s' "$fatwho" | jq -r '.[] | select(.number==530) | .worker' 2>/dev/null)" = "kite-497" ] \
    && ok "#497: ...and the claim inside that oversized set is reported, with its holder (case 43)" \
    || bad "#497: the claim in the oversized set must be reported" "$fatwho"
  # The scan stays HONEST at size: the two chatty-but-markerless issues are not in-flight work, and a body
  # big enough to break the plumbing must not become a claim.
  [ "$(printf '%s' "$fatwho" | jq -c '[.[] | select(.number >= 530 and .number <= 532) | .number] | sort' 2>/dev/null)" = "[530]" ] \
    && ok "#497: ...and chatty markerless issues in that set are still not claims (case 43)" \
    || bad "#497: chatty markerless issues must not become claims" "$fatwho"

  kill "$ARGV_SRV" 2>/dev/null
fi

# ---- #966: FLUSH TELLS A SKIP FROM A DROP FROM A REPLAY --------------------------------------------
#
# A SKIP and a DROP are opposite facts — a dropped write is gone forever, a skipped one is queued against
# ANOTHER board and is still owed — and #963 taught the ENGINE to tell them apart while the CLI went on
# telling the worker neither. `replayed 0 of 1` and nothing else is exactly the "my write did not replay
# and nothing said why" that #882 felt like from the outside.
#
# NO SERVER, and that is the point rather than a convenience: `flush --dry-run` is the read that must work
# when NO board read can (an exhausted budget is the only reason a queue exists), so it must answer "which
# board is this owed to?" from the queue and the environment alone. If any leg here starts needing HTTP,
# that property has been lost.
FLCACHE="$(mktemp -d)"
cat >"$FLCACHE/pending.jsonl" <<'JSONL'
{"ref":".github#100","field":"Status","value":"Done","at":"2026-07-17T05:00:00Z","worker":"w-here","boardOwner":"FS-GG","boardTitle":"Coordination"}
{"ref":"FS.GG.SDD#200","field":"Status","value":"Ready","at":"2026-07-17T05:01:00Z","worker":"w-other","boardOwner":"FS-GG","boardTitle":"OtherBoard"}
{"ref":".github#300","field":"Status","value":"","at":"2026-07-17T05:02:00Z","worker":"w-legacy"}
JSONL

fl="$(FSGG_COORD_CACHE="$FLCACHE" "$ENGINE" flush --dry-run 2>&1)"; flrc=$?

[ "$flrc" -eq 0 ] \
  && ok "#966: flush --dry-run over a mixed queue exits 0 (a dry run reports; it never replays)" \
  || bad "#966: flush --dry-run must exit 0" "rc=$flrc: $fl"

# THE BOARD IS NAMED. "Which board is this owed to?" has to be answerable exactly here.
printf '%s' "$fl" | grep -q "FS-GG/Coordination" \
  && ok "#966: flush --dry-run names the board a flush HERE would write" \
  || bad "#966: flush --dry-run must name this flush's board" "$fl"

# THE CROSS-BOARD ENTRY IS MARKED, and rendered differently from the one that would replay. Before this,
# an entry this pass would SKIP printed identically to one it would land.
printf '%s' "$fl" | grep -E 'FS.GG.SDD#200.*SKIP' | grep -q "FS-GG/OtherBoard" \
  && ok "#966: ...and marks the cross-board entry as one a flush here would SKIP, naming ITS board" \
  || bad "#966: the cross-board entry must be marked SKIP and name its own board" "$fl"

# THE REMEDY THAT WORKS. "Re-run flush after the reset" can never land another board's entry, so the
# cross-board count gets the remedy that does: re-point the board.
printf '%s' "$fl" | grep -q "FSGG_COORD_PROJECT" \
  && ok "#966: ...and gives the remedy that fixes it (re-point the board), not 'flush again here'" \
  || bad "#966: a skipped write must carry the re-point remedy" "$fl"

# AN ENTRY THIS PASS WOULD LAND IS NOT MARKED. The signal is worthless if it fires on everything.
printf '%s' "$fl" | grep -E '\.github#100' | grep -q "SKIP" \
  && bad "#966: an entry queued against THIS board must NOT be marked SKIP" "$fl" \
  || ok "#966: an entry queued against this board is not marked — the signal means something"

# A PRE-#882 ENTRY RECORDED NO BOARD. It replays against the current board — the behaviour it was queued
# under — and that is the one entry whose target is a DEFAULT rather than a fact, so it is said out loud.
printf '%s' "$fl" | grep -E '\.github#300' | grep -q "no board recorded" \
  && ok "#966: a legacy entry with no recorded board is named as such, not silently defaulted" \
  || bad "#966: a pre-#882 entry must be distinguishable from one that names this board" "$fl"

# AND NO NOISE WHEN THERE IS NOTHING TO SAY: drop the cross-board entry and the remedy must go with it.
grep -v OtherBoard "$FLCACHE/pending.jsonl" >"$FLCACHE/p2" && mv "$FLCACHE/p2" "$FLCACHE/pending.jsonl"
fl2="$(FSGG_COORD_CACHE="$FLCACHE" "$ENGINE" flush --dry-run 2>&1)"
printf '%s' "$fl2" | grep -q "FSGG_COORD_PROJECT" \
  && bad "#966: the re-point remedy must not fire when no entry is cross-board" "$fl2" \
  || ok "#966: ...and a queue with no cross-board entry says nothing about re-pointing"

rm -rf "$FLCACHE"

echo
echo "coord-engine parity: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine parity FAILED"; exit 1; }
echo "green — the engine matches the corpus's certified answer, with no bash in the pipeline."

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
#   • §i/§h2 (#331: a column set DURING the lease is preserved; the unreadable-column repair advice) — the
#     engine's `release` does not yet read-and-preserve the current column; #331 is a distinct defect with
#     its own read (bash's "release spends 1"), ported separately. #481 changes what release restores TO,
#     not WHETHER it first reads the live column.
#   • the human wording (`board: Backlog`, `restored`) is asserted here at the HTTP layer (the board write's
#     option id), not on stdout — the engine's `release` reports `released <ref>` without naming the column.
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
rsrv 'FSGG_PARITY_MARKERS=[{"n":356,"id":856,"worker":"pika-r01"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (g) bound a port"; else
  renv release FS.GG.SDD#356 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_ready" ] \
    && ok "#481: a marker minted BEFORE #481 (no prev=) still falls back to Ready (opt_ready)" \
    || bad "#481: a pre-#481 marker must restore Ready" "last write=$(rlastopt)"
  kill "$RS_SRV" 2>/dev/null
fi

# (h) A claim that recorded `In progress` recorded its OWN footprint, not a column anybody chose. Restoring
#     it would leave the item looking claimed with no claim on it — so it, too, falls back to Ready.
rsrv 'FSGG_PARITY_MARKERS=[{"n":357,"id":857,"worker":"pika-r01","prev":"In%20progress"}]' --
if [ -z "$RS_PORT" ]; then bad "restore fixture (h) bound a port"; else
  renv release FS.GG.SDD#357 --worker pika-r01 >/dev/null 2>&1
  [ "$(rlastopt)" = "opt_ready" ] \
    && ok "#481: a recorded 'In progress' is a footprint, not a column — release falls back to Ready" \
    || bad "#481: a recorded In progress must not be restored" "last write=$(rlastopt)"
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
# DISPOSITION ON THE RECORD (not silently skipped): case 13's remaining legs are separate work —
#   * `reap` scopes to the checkout too (#480, the destructive one): NO `reap` command in the engine yet
#     (the case 21 §d/§e / case 26 mold).
#   * resolve_repo across the full roster + `issues` short-id (#381/#446), the `Blocked by`
#     canonicalization gate, and the epic-rollup / NO-TOUCH-SET `lint` rules (#496): a `lint`/`issues`
#     command the engine does not have. Tracked for cases 13-remainder / 14.

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
  # is just as dead. #430 declares only `**/only-unmatchable`.
  bts="$(jq -r '[.[] | select(.code=="BAD-TOUCH-SET") | .id | sub("^[^/]+/";"")] | sort | join(",")' <<<"$ljson")"
  [ "$bts" = "FS.GG.SDD#430" ] \
    && ok "case14: BAD-TOUCH-SET fires on the item whose every token is unmatchable (#430)" \
    || bad "case14: BAD-TOUCH-SET must fire on exactly 430" "$bts"
  d430="$(jq -r '.[] | select(.code=="BAD-TOUCH-SET") | .detail' <<<"$ljson")"
  printf '%s' "$d430" | grep -q 'only-unmatchable' \
    && ok "case14: BAD-TOUCH-SET names the unmatchable token" \
    || bad "case14: BAD-TOUCH-SET must name the token" "$d430"
  printf '%s' "$d430" | grep -q 'no worker can ever pick this up' \
    && ok "case14: BAD-TOUCH-SET says nobody can ever pick it up" \
    || bad "case14: BAD-TOUCH-SET detail" "$d430"

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
             FSGG_COORD_PROJECT=Coordination FSGG_COORD_CACHE="$(mktemp -d)" "$ENGINE" done "$@" 2>&1; }

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

  kill "$DF_SRV" 2>/dev/null
fi

echo
echo "coord-engine parity: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine parity FAILED"; exit 1; }
echo "green — the engine matches the corpus's certified answer, with no bash in the pipeline."

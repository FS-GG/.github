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
# THE GUARD MODULE — sourced by the shim at tiers 2/2b, and NOT kit content (.github#1586). The verb
# partition and both guards live here now, so the legs below that read the partition's TEXT read this
# file; the legs that drive the guards' BEHAVIOUR are unchanged, because behaviour did not change.
GUARDS="$REPO_ROOT/scripts/fsgg-coord-guards.sh"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# THE DIAGNOSTIC FOR AN INNER CORPUS'S LOG (.github#1818). `tail -N` of a 600-line run shows the END of
# the log — the summary — not the `FAIL` lines that produced it, so a corpus that fails early and passes
# late reports N lines containing no cause at all. This names the actual failures (capped, so one runaway
# leg cannot flood the outer suite) plus the inner run's own summary line, which is what run.sh prints
# unconditionally at the very end (`echo "coord-engine parity: ... failed"`) regardless of where the
# first failure fell.
INNER_FAIL_CAP=25
inner_diagnostic() {   # $1 = path to the inner run's captured stdout+stderr
  local log="$1" fails nfails summary detail
  fails="$(grep '^FAIL ' "$log" 2>/dev/null || true)"
  if [ -z "$fails" ]; then
    # No FAIL line at all — the corpus died before it could name anything (a shell syntax error, a
    # crash). The tail is the only signal that exists, so fall back to it rather than print nothing.
    printf 'no FAIL line found in the inner run — raw tail:\n%s' "$(tail -25 "$log" 2>/dev/null)"
    return
  fi
  nfails="$(printf '%s\n' "$fails" | grep -c '^FAIL ' || true)"
  detail="$(printf '%s\n' "$fails" | head -"$INNER_FAIL_CAP")"
  if [ "$nfails" -gt "$INNER_FAIL_CAP" ]; then
    detail="$detail
    ... $((nfails - INNER_FAIL_CAP)) more FAIL line(s) not shown"
  fi
  summary="$(grep '^coord-engine parity:' "$log" 2>/dev/null || true)"
  if [ -n "$summary" ]; then
    detail="$detail
$summary"
  fi
  printf '%s' "$detail"
}

[ -x "$ENGINE" ] || { echo "FAIL  build the engine first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2; exit 1; }
[ -x "$SHIM" ]   || { echo "FAIL  the shim is missing or not executable: $SHIM" >&2; exit 1; }
[ -f "$GUARDS" ] || { echo "FAIL  the guard module is missing: $GUARDS" >&2; exit 1; }

# ---- 0. THIS FIXTURE DECIDES WHICH WORKER IT IS TOO (.github#1751) --------------------------------
# CHECKED, and the honest answer is "affected, but only through leg 1". None of legs 2+ can resolve an
# identity at all: they drive a FAKE engine (`fixture()`'s `echo "ENGINE RAN: $*"`), so `Identity.resolve`
# is never reached, and no leg in this file passes `--worker`. The one identity-sensitive thing here is
# leg 1, which re-runs the whole D.1 corpus — and that corpus's identity is now decided by run.sh itself.
#
# That is why this file reported the same defect as ONE failed assertion while run.sh reported 58: leg 1
# folds 593 results into a single line, so the leak was 58x louder in one suite and 1x in the other while
# being the same fault. A reader who saw only this file's single red had almost no signal about why.
#
# The scrub below is therefore INSURANCE, not the repair — the repair is in run.sh. It is here because
# this file is a certifying harness whose legs are added to over time, and the next leg that reaches a
# real engine with a `--worker` would silently re-acquire the defect. Costs nothing: nothing here wants
# an ambient identity. The ASSERTION of the property lives in run.sh (its #1751 pair), which leg 1 runs.
unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS
export FSGG_WORKER=""

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
  bad "the D.1 corpus is NOT green through the shim" "$(inner_diagnostic "$RUNLOG")"
fi
rm -f "$WRAP" "$RUNLOG"

# ---- 1b. THE DIAGNOSTIC NAMES THE CAUSE, NOT THE TAIL (.github#1818) ------------------------------
# §1 above only exercises the green path (the corpus IS green today), so a regression back to a plain
# `tail -25` would slip past this file unnoticed — the exact way the original defect shipped. This proves
# `inner_diagnostic` against a SYNTHETIC inner-run log built to fail EARLY and keep talking long past it,
# which is precisely the shape `tail -N` cannot see through. The synthetic log mimics run.sh's own
# contract: `PASS `/`FAIL ` lines and the unconditional `coord-engine parity: ... failed` summary at the
# end (run.sh's own final `echo`), so the assertion is about the extraction, not about run.sh's wording.
SYNTHRUN="$(mktemp)"
{
  i=1
  while [ "$i" -le 5 ]; do echo "PASS  warm-up assertion $i"; i=$((i+1)); done
  echo "FAIL  the early leg that actually broke"
  echo "    | this is the cause a tail -25 would never show"
  i=1
  while [ "$i" -le 40 ]; do echo "PASS  padding assertion $i, to push the log well past the last 25 lines"; i=$((i+1)); done
  echo
  echo "coord-engine parity: 46 assertion(s), 45 passed, 1 failed"
  echo "coord-engine parity: 0 not measured"
} >"$SYNTHRUN"

diag="$(inner_diagnostic "$SYNTHRUN")"
tailonly="$(tail -25 "$SYNTHRUN")"
if printf '%s' "$diag" | grep -q 'the early leg that actually broke' \
   && printf '%s' "$diag" | grep -q 'coord-engine parity: 46 assertion(s), 45 passed, 1 failed' \
   && ! printf '%s' "$tailonly" | grep -q 'the early leg that actually broke'; then
  ok ".github#1818: the diagnostic names an early failure that a tail -25 of the same log would have hidden, plus the inner run's summary"
else
  bad ".github#1818: the diagnostic must name an early cause, not only repeat the log's tail" "diag=$diag"
fi
rm -f "$SYNTHRUN"

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

# tier 2, the source build: with the env unset, from inside .github, the shim still resolves an
# engine and answers --version. The anti-#266 property — an unset knob is not "no engine", and a
# resolver that finds one must not pretend it found none.
out="$(cd "$REPO_ROOT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && [ -n "$out" ]; then
  ok "tier 2: with the env unset, the shim resolves an engine from source and answers (version $out)"
else
  bad "tier 2: an unset env must still resolve the from-source engine in .github" "rc=$rc out=$out"
fi

# REFUSAL, not silent no-op: no explicit bin, no engine on PATH, no manifest, no repo — the shim exits
# NON-ZERO with advice, never 0. A non-git cwd with the env unset stands in for a bare receiver that
# never restored the tool: tiers 2/4 need a git toplevel (there is none here), tier 1 is unset, and
# tier 3 (a global tool on PATH) does not exist — so nothing resolves and the shim must SAY so.
NONGIT="$(mktemp -d)"
err="$(cd "$NONGIT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$err" | grep -qi 'no fsgg-coord engine found'; then
  ok "refusal: an unresolvable environment exits non-zero ($rc) with advice — never a silent green no-op (#266)"
else
  bad "refusal: no engine anywhere must be a loud non-zero, not a silent 0" "rc=$rc err=$err"
fi
rmdir "$NONGIT" 2>/dev/null || true

# ---- 3. STALENESS: THE ARTIFACT IS NOT A REF (#929) ----------------------------------------------
# Tier 2 execs a build output nothing keeps in step with the `src/` beside it, so the shim can hand a
# worker code that is not in their tree — and twice on 2026-07-16 it handed them an engine that
# silently ignored `release --status` and put a merged item back to Ready.
#
# These legs use a SYNTHETIC source-build checkout: a git toplevel with a fake engine and a fake source tree.
# That is deliberate on two counts. It asserts the shim's mtime rule directly, with no 5s `dotnet build`
# per leg; and it does not depend on the REAL bin/ being stale or fresh at test time — which is whatever
# the last person happened to build, i.e. the very thing under test.
# EVERY mtime IS SET EXPLICITLY, none left at "now". `-newer` is a STRICT comparison, so a fixture that
# wrote the .dll and then touched the source would be asserting that two writes a few microseconds apart
# land on different timestamps — true on ext4's nanosecond stamps, false the moment this runs on a
# coarser filesystem, and a parity red is supposed to be EVIDENCE rather than a coin toss.
FIXSRC="src/FS.GG.Coord.Core/Protocol.fs"

# A FIXTURE REF THAT CANNOT RESOLVE — BELT AND BRACES (#1008). These legs pass a ref to a board WRITE verb,
# and they are safe only while the fixture engine is the one that receives it. That is now structural (the
# shim resolves the source build above any packaged engine, §4 below), but a fixture whose safety rests on
# resolution being correct is exactly what #1008 was: the placeholder used to be `.github#1`, which RESOLVES
# — and when tier 2 preempted these legs, `claim .github#1` was posted to this repo FOR REAL, twice. The
# blast radius was small only by luck: `.github#1` is a pull request, so the board's GraphQL could not
# resolve it as an Issue and #331 failed closed. A live ITEM number would have been claimed out from under
# its holder and had its Status rewritten. So the ref is now one that cannot exist anywhere: if the
# isolation ever breaks again, the write fails on the ref instead of landing on somebody's work.
FIXREF="fixture/repo#999999"
fixture() {   # $1 = dir. A source-build checkout whose engine is NEWER than its source (i.e. FRESH).
  mkdir -p "$1/src/FS.GG.Coord.Cli/bin/Release/net10.0" "$1/src/FS.GG.Coord.Core"
  ( cd "$1" && git init -q . ) >/dev/null 2>&1
  printf '// source\n' >"$1/$FIXSRC"
  FIXBIN="$1/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
  printf '#!/usr/bin/env bash\necho "ENGINE RAN: $*"\n' >"$FIXBIN"; chmod +x "$FIXBIN"
  : >"$FIXBIN.dll"
  touch -d '3 hours ago' "$1/$FIXSRC"                  # source, then...
  touch -d '2 hours ago' "$FIXBIN" "$FIXBIN.dll"       # ...the build that FOLLOWED it: 1h clear
}
stale() { touch -d '1 hour ago' "$1/$FIXSRC"; }        # edited an hour AFTER the build: 1h clear

FIX="$(mktemp -d)"; fixture "$FIX"

# FRESH: an engine newer than its source is the happy path, and it must stay SILENT. A guard that cried
# wolf here would fire on every worker after every legitimate build — teaching the fleet to skim it.
#
# AND `ENGINE RAN` IS ASSERTED, NOT JUST THE SILENCE (#1008). An assertion on silence ALONE cannot tell
# "the guard stayed quiet" from "the guard never ran" — the fixture engine going unreached is silent too,
# and passes. That is the property that would have caught #1008 on day one, and it costs one `grep`: this
# leg was green for the whole life of the bug precisely because it never checked WHOSE silence it heard.
err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>&1 >/dev/null)"; rc=$?
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'ENGINE RAN'; then
  ok "staleness: an engine NEWER than its src/ is silent — no warning on the happy path, and the FIXTURE is what ran"
else
  bad "staleness: a fresh engine must not warn, and must be the engine under test" "rc=$rc out=$out err=$err"
fi

# STALE + a READ verb: WARN, but still run. A stale read misinforms one worker; blocking it would halt
# the fleet the moment anyone touches src/, on a repo whose premise is N workers in one checkout.
#
# THE VERB HERE WAS `next`, AND `next` IS NOT A READ (.github#1528). After printing its answer it makes the
# #733 chore offer — `offerChoreAtNext` → `Chores.offer` → `Writes.claim` — which POSTs a claim marker for
# the repo's chore lock (`.github`'s is #1033, so it fires in this very repo). This leg therefore certified
# "a stale engine may take a chore lock" while reading as the guard's read-side proof. `who` is the
# replacement because it is the same shape — a board read an operator runs to diagnose — with no write
# anywhere in its handler and no application of one; `next` now belongs to the refusal set below.
stale "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>/dev/null)"; rc=$?
err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>&1 >/dev/null)"
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && printf '%s' "$err" | grep -qi 'stale'; then
  ok "staleness: a stale engine WARNS on a read verb ('who') — and still runs, exit code intact"
else
  bad "staleness: a read must warn and still run" "rc=$rc out=$out err=$err"
fi

# STALE + a BOARD WRITE: REFUSE. A stale write corrupts state the whole fleet shares (#929's two live
# incidents), so the engine must NOT run — asserted on the output, not merely the exit code.
for verb in release set-field "done" claim; do   # "done" quoted: a coord VERB in a list, not this loop's keyword (SC1010, #648)
  out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$verb" "$FIXREF" 2>&1)"; rc=$?
  if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
     && printf '%s' "$out" | grep -qi 'refused'; then
    ok "staleness: a stale engine REFUSES the board write '$verb' (exit $rc) — the engine never ran"
  else
    bad "staleness: '$verb' is a board write and must be refused on a stale engine" "rc=$rc out=$out"
  fi
done

# NO SOURCE — the receivers' shape (ADR-0034 §4.4 tiers 3/4): there is nothing to be stale AGAINST, so
# the guard must not fire. Asserted at tier 2 with the sources removed, which is the shim's own test for
# it: no `*.fs` under src/, so no comparison exists to fail.
find "$FIX/src" -name '*.fs' -delete
err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" release "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" release "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'ENGINE RAN'; then
  ok "staleness: with NO source present, even a board write is unaffected — nothing to be stale against"
else
  bad "staleness: a source-less checkout must not warn or refuse" "rc=$rc out=$out err=$err"
fi

# AN EXPLICIT BIN IS EXEMPT, and structurally: tier 1 execs before tier 2 is ever reached. This is why
# the D.1 corpus above (which drives the shim through FSGG_COORD_ENGINE_BIN) sees no warnings, and why
# the receivers' shape cannot be broken by this guard.
fixture "$FIX"; stale "$FIX"
err="$(cd "$FIX" && FSGG_COORD_ENGINE_BIN="$FIXBIN" "$SHIM" release "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$FIX" && FSGG_COORD_ENGINE_BIN="$FIXBIN" "$SHIM" release "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'ENGINE RAN'; then
  ok "staleness: an explicit FSGG_COORD_ENGINE_BIN is honoured silently — an instruction, not a hint"
else
  bad "staleness: tier 1 must not consult staleness" "rc=$rc out=$out err=$err"
fi

# ---- 3b. THE VERB PARTITION IS TOTAL AND EXACT (.github#1528) ------------------------------------
# §3 proves the guard REFUSES the verbs the shim calls writes. It cannot prove the shim calls the right
# verbs writes, and for most of the guard's life it did not: `set-paths` reached the same `Writes.widen`
# PATCH as `widen`, through the same `updateTouchSet` helper, and was absent from the list — so the guard
# covered one of two verbs that are literally one function, and the RECOVERY verb was the uncovered one.
# `room` and `reconcile` were absent too.
#
# THE DEFECT IS THE ENUMERATION, NOT THE FOUR MISSING WORDS. A hand-kept list of a property of the engine,
# with nothing comparing the two, is wrong the moment somebody adds a verb and does not remember. Adding
# the missing words fixes today and guarantees tomorrow. Deriving the list instead is the repair that ends
# the class — and it cannot be done in the shim, for a reason that is worth stating rather than assuming
# (the shim's own header carries the long form): the engine that would answer "which verbs write?" is the
# STALE one under suspicion, an engine built before such a field existed answers with nothing, and "nothing
# writes" permits everything. A guard that needs a current engine to decide whether the engine is current
# is circular, and it fails OPEN on the oldest artifacts — the dangerous ones.
#
# SO THE DERIVATION MOVES HERE, WHERE IT IS SOUND. At test time both halves are available and neither is
# suspect: a FRESHLY BUILT engine ($ENGINE, which the top of this file requires) and the shim's text. The
# engine's verb surface is itself derived — `command-contract` reflects over the `Command` union — so it
# needs no maintenance and a new verb appears in it the moment it is added. This section asserts a BIJECTION
# between that surface and the shim's three sets. A verb added to the engine lands in no set and reds; a set
# naming a verb the engine dropped reds; a verb in two sets reds. The list stays hand-written and stops
# being able to be quietly incomplete, which is the property the four missing words could not buy.
#
# WHAT IT DOES NOT PROVE, said plainly: that each verb is in the RIGHT set. Nothing here can — write-ness is
# a fact about F# control flow, and a text analysis of it over-approximates badly (advice strings in `who`
# and `lint` name `claim`, `reap` and `done`, so a naive reachability pass calls those verbs writes). What
# this buys is that the decision must be MADE, by a person, before the build goes green. §3c below then
# makes each decision executable.
#
# THE EXTRACTION REFUSES ANYTHING BUT A LITERAL. The three assignments are lifted and eval'd, so the
# pattern is anchored to a plain double-quoted literal with no `$`, backtick or `(` inside it: a
# future spelling that used a substitution or a line continuation reds this leg instead of being executed.
#
# LIFTED FROM THE GUARD MODULE, NOT THE SHIM (.github#1586). The sets moved to
# `scripts/fsgg-coord-guards.sh` — same three literals, same bijection, same gate. §3f below asserts they
# are no longer kit content, which is the whole reason they moved; this leg is unchanged in substance and
# reads the file that now holds them.
partition_ok=1
PART="$(grep -E '^BOARD_(WRITES|WRITES_CONDITIONAL|READS)="[^"$`(]*"$' "$GUARDS")"
if [ "$(printf '%s\n' "$PART" | grep -c .)" -ne 3 ]; then
  bad "partition: scripts/fsgg-coord-guards.sh must declare exactly 3 literal verb sets (BOARD_WRITES, BOARD_WRITES_CONDITIONAL, BOARD_READS)" "$PART"
  partition_ok=0
else
  BOARD_WRITES=""; BOARD_WRITES_CONDITIONAL=""; BOARD_READS=""
  eval "$PART"
fi

if [ "$partition_ok" -eq 1 ]; then
  # `awk '{print $1}'` — the shim dispatches on `$1`, and `room open` is the engine's one two-word verb, so
  # the token this guard can ever see is `room`. Comparing the full contract name would leave `room open`
  # permanently "unclassified" and this gate permanently red for a reason that is not a bug.
  #
  # `jq` IS CHECKED FOR BY NAME, not inferred from an empty result. Without this, a machine with no `jq`
  # produces an empty `$CONTRACT` and the leg below reports "the engine answered nothing" — a cause it
  # structurally cannot observe, blaming the engine for the toolchain. That is the #266 shape this whole
  # file exists to refuse, and `run.sh` (which §1 already ran) needs `jq` 151 times, so a missing one is a
  # fact worth naming rather than a condition worth tolerating.
  CONTRACT=""; CONTRACT_JSON=""
  if ! command -v jq >/dev/null 2>&1; then
    bad "partition: jq is not installed — this leg cannot read the engine's command contract, and that is NOT a verdict about the shim"
  else
    CONTRACT_JSON="$("$ENGINE" command-contract --json 2>/dev/null)"
    CONTRACT="$(printf '%s' "$CONTRACT_JSON" | jq -r '.commands[].name' | awk '{print $1}' | sort -u)"
  fi
  CLASSIFIED="$(printf '%s %s %s' "$BOARD_WRITES" "$BOARD_WRITES_CONDITIONAL" "$BOARD_READS" \
                 | tr ' ' '\n' | grep -v '^$' | sort)"

  if [ -z "$CONTRACT" ]; then
    command -v jq >/dev/null 2>&1 \
      && bad "partition: could not read the engine's command surface — 'command-contract --json' answered nothing"
  else
    unclassified="$(comm -13 <(printf '%s\n' "$CLASSIFIED" | sort -u) <(printf '%s\n' "$CONTRACT"))"
    phantom="$(comm -23 <(printf '%s\n' "$CLASSIFIED" | sort -u) <(printf '%s\n' "$CONTRACT"))"
    dupes="$(printf '%s\n' "$CLASSIFIED" | uniq -d)"

    if [ -z "$unclassified" ]; then
      ok "partition: every verb the engine advertises ($(printf '%s\n' "$CONTRACT" | grep -c .)) is classified by the shim — a new verb cannot slip in unguarded"
    else
      bad "partition: the engine has verb(s) the shim classifies NOWHERE — decide whether each writes, then add it to BOARD_WRITES, BOARD_WRITES_CONDITIONAL or BOARD_READS" "$unclassified"
    fi

    if [ -z "$phantom" ]; then
      ok "partition: every verb the shim classifies is a verb the engine actually has — no set has rotted past the surface it describes"
    else
      bad "partition: the shim classifies verb(s) the engine does not have — a renamed or removed verb left its name behind" "$phantom"
    fi

    if [ -z "$dupes" ]; then
      ok "partition: the three sets are DISJOINT — no verb is classified twice, so there is one answer per verb"
    else
      bad "partition: verb(s) appear in more than one set" "$dupes"
    fi

    # The engine's `writes` answers whether an invocation can mutate; the shim's two write lists
    # separately document flag dependence, so their UNION is the comparable set (#1570).
    missing_writes="$(printf '%s' "$CONTRACT_JSON" | jq -r '.commands[] | select(.writes == null) | .name' | awk '{print $1}')"
    engine_writes="$(printf '%s' "$CONTRACT_JSON" | jq -r '.commands[] | select(.writes == "always" or .writes == "conditional") | .name' | awk '{print $1}' | sort -u)"
    engine_reads="$(printf '%s' "$CONTRACT_JSON" | jq -r '.commands[] | select(.writes == "never") | .name' | awk '{print $1}' | sort -u)"
    shim_writes="$(printf '%s %s' "$BOARD_WRITES" "$BOARD_WRITES_CONDITIONAL" | tr ' ' '\n' | grep -v '^$' | sort -u)"
    shim_reads="$(printf '%s' "$BOARD_READS" | tr ' ' '\n' | grep -v '^$' | sort -u)"
    if [ -z "$missing_writes" ] && [ "$shim_writes" = "$engine_writes" ] && [ "$shim_reads" = "$engine_reads" ]; then
      ok "partition: shim membership matches every engine writes contract"
    else
      bad "partition: writes keys must be complete and shim membership must match the engine" "missing=$missing_writes"
    fi
  fi
fi

# ---- 3c. THE PARTITION IS BEHAVIOUR, NOT A STRING (.github#1528) ---------------------------------
# §3b proves the sets are total; this proves the guard OBEYS them, verb by verb, against a stale engine.
# Two directions, and the second matters as much as the first: refusing a genuine READ under a stale engine
# would be a worse bug than the omission being fixed — it would halt diagnosis at the moment the fleet most
# needs it — so every read is asserted to warn AND still run, not merely "not refused".
#
# `$FIXREF` on the refusal legs, for §3's reason (#1008): a ref that cannot resolve anywhere, so if the
# fixture isolation ever breaks the write fails on the ref rather than landing on somebody's work. The read
# legs need no such belt now that `next` has moved out of them — no verb in `BOARD_READS` can write even
# if a real engine were resolved, which is exactly the claim `BOARD_READS` is making.
if [ "$partition_ok" -eq 1 ]; then
  fixture "$FIX"; stale "$FIX"

  # ONE `ok` PER DIRECTION, and it is claimed only when the whole loop held. A summary line printed
  # unconditionally beside per-verb `bad`s would assert "every write verb is refused" in the same run that
  # just proved one is not — a green sentence over a red result is how a suite stops being read.
  refused=""; permitted=""; wfail=0; rfail=0
  for verb in $BOARD_WRITES $BOARD_WRITES_CONDITIONAL; do
    out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$verb" "$FIXREF" 2>&1)"; rc=$?
    if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
       && printf '%s' "$out" | grep -qi 'refused'; then
      refused="$refused $verb"
    else
      wfail=1
      bad "staleness: '$verb' writes shared state and MUST be refused on a stale engine — the engine must never run" "rc=$rc out=$out"
    fi
  done
  [ "$wfail" -eq 0 ] && ok "staleness: every write verb the shim declares is refused on a stale engine —$refused"

  for verb in $BOARD_READS; do
    out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$verb" 2>/dev/null)"; rc=$?
    err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$verb" 2>&1 >/dev/null)"
    if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' \
       && printf '%s' "$err" | grep -qi 'stale'; then
      permitted="$permitted $verb"
    else
      rfail=1
      bad "staleness: '$verb' is a READ and must WARN and still run — refusing it halts diagnosis for nothing" "rc=$rc out=$out err=$err"
    fi
  done
  [ "$rfail" -eq 0 ] && ok "staleness: every read verb the shim declares still runs (with a warning) on a stale engine —$permitted"

  # `delivery-route` is deliberately conditional: validate/show are evidence reads, while record posts a
  # receipt. The stale guard receives the first subcommand token, so pin both read spellings as
  # warning-but-run and the record spelling as refusal-before-engine; a new top-level command cannot hide
  # a write arm behind its friendly read modes.
  for route_read in "delivery-route validate FS.GG.SDD#70 receipt.json" "delivery-route show FS.GG.SDD#70"; do
    out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN $SHIM $route_read 2>/dev/null)"; rc=$?
    err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN $SHIM $route_read 2>&1 >/dev/null)"
    if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && printf '%s' "$err" | grep -qi stale; then
      ok "staleness: stale $route_read warns and runs as a delivery-route read"
    else
      bad "staleness: stale $route_read must warn and run" "rc=$rc out=$out err=$err"
    fi
  done
  out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" delivery-route record FS.GG.SDD#70 receipt.json 2>&1)"; rc=$?
  if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' && printf '%s' "$out" | grep -qi refused; then
    ok "staleness: delivery-route record is refused before its receipt write"
  else
    bad "staleness: stale delivery-route record must be refused before write" "rc=$rc out=$out"
  fi

  # THE PAIR THAT STARTED IT (.github#1528). `widen` and `set-paths` are one function — both reach
  # `Writes.widen` through `updateTouchSet` — and the guard covered only `widen`. Asserted by NAME as well
  # as by the loop above, because the loop's subject is whatever the shim says and this leg's subject is the
  # bug: if a future edit drops `set-paths` from every set, §3b reds; if it moves it to `BOARD_READS`, the
  # loop above happily certifies the wrong answer and only this leg says so.
  for verb in widen set-paths; do
    out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$verb" "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
    if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
       && printf '%s' "$out" | grep -qi 'refused'; then
      ok "staleness: '$verb' is REFUSED on a stale engine (exit $rc) — the touch-set pair cannot drift apart again"
    else
      bad "staleness: '$verb' rewrites a live touch-set and must be refused on a stale engine" "rc=$rc out=$out"
    fi
  done

  # AN UNCLASSIFIED VERB IS REFUSED — the runtime half of §3b, and the leg that makes the partition worth
  # more than a CI report. §3b reds when a verb is added to the engine and not to the shim, but it reds on
  # the NEXT CI run; between the two the fleet is running the verb. So the shim refuses a first token it
  # does not classify, and this asserts it with a token no engine has.
  out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" not-a-real-verb "$FIXREF" 2>&1)"; rc=$?
  if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
     && printf '%s' "$out" | grep -q 'does not classify'; then
    ok "staleness: a verb the shim classifies NOWHERE is REFUSED on a stale engine (exit $rc) — unknown write-ness fails closed"
  else
    bad "staleness: an unclassified verb must be refused, not permitted — that is the omission this partition exists to stop" "rc=$rc out=$out"
  fi

  # ...AND THE FLAG SPELLINGS ARE STILL EXEMPT, by grammar rather than by a list. `--help`/`-h`/`--version`
  # are not verbs and are in no set, so the refusal above would swallow them if its exemption were a list
  # somebody has to remember. It asks whether the token begins with `-`, which is the same question the
  # engine's own dispatch asks, so a fourth spelling needs no edit here.
  for flag in --help -h --version; do
    out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$flag" 2>&1)"; rc=$?
    if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && ! printf '%s' "$out" | grep -q 'REFUSED'; then
      ok "staleness: '$flag' is not a verb and is not refused — the exemption is the grammar, not a list"
    else
      bad "staleness: '$flag' must reach the engine on a stale checkout" "rc=$rc out=$out"
    fi
  done

  # A BARE INVOCATION IS NOT A BOARD WRITE. `${1:-}` is EMPTY here, and the guard matches `*" $verb "*` —
  # so a set concatenation that introduced a double space would make the empty verb match and refuse
  # `scripts/fsgg-coord` with no arguments (which the engine parses as `--help`). Cheap to assert, invisible
  # to every other leg, and the exact failure a second verb set invites.
  # `grep -q 'REFUSED'` CASE-SENSITIVELY, and that is the assertion, not a typo. The stale WARNING ends
  # "Board writes are refused until you do." — so the `-qi 'refused'` the refusal legs above use (sound
  # there, because they also require a non-zero exit and no `ENGINE RAN`) matches the permitted path too.
  # Here the exit IS zero and the engine DID run, so only the die()'s own upper-case word separates them.
  out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" 2>&1)"; rc=$?
  if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && ! printf '%s' "$out" | grep -q 'REFUSED'; then
    ok "staleness: a BARE invocation (empty verb) is not matched as a board write — it warns and reaches the engine"
  else
    bad "staleness: an empty verb must not be refused as a board write" "rc=$rc out=$out"
  fi
fi

# ---- 3d. GENERATED `obj/` SOURCES ARE NOT SOURCES (.github#1572) ---------------------------------
# The FALSE-RED direction of the same `find`, and it HALTS a worker where §3's direction misleads one.
# MSBuild generates `.fs` under `src/` — `<proj>/obj/<cfg>/net10.0/<proj>.AssemblyInfo.fs` and
# `.NETCoreApp,Version=v10.0.AssemblyAttributes.fs`, one pair per project per CONFIGURATION. An
# unfiltered `find "$top/src" -name '*.fs'` counted them, so a plain `dotnet test` (which builds Debug)
# re-stamped the Debug stubs and the guard reported STALE against a CURRENT Release binary over a tree
# with zero edited sources — refusing `heartbeat`, `done`, `claim`, `release` and `widen` at exit 69,
# citing an assembly-attribute stub no human wrote. Measured on this host while closing .github#1534.
#
# IT AIMS AT THE SANCTIONED FLOW, which is why it is worth a leg rather than a filter. pnext-item §2 says
# work in a worktree, build there, run the tests — and running the tests is exactly what re-stamps those
# stubs. A worker whose lease then lapses cannot `heartbeat` through it, and the remedy the message prints
# (`dotnet build -c Release`) clears it only until the next `dotnet test`.
fixture "$FIX"    # FRESH: source 3h ago, engine 2h ago

# GENERATED OUTPUT UNDER `obj/`, newer than the engine — the exact bytes MSBuild writes, at the exact
# path it writes them. Nothing hand-written is newer, so a board write must LAND.
mkdir -p "$FIX/src/FS.GG.Coord.Core/obj/Debug/net10.0"
printf '// <auto-generated>\n' >"$FIX/src/FS.GG.Coord.Core/obj/Debug/net10.0/FS.GG.Coord.Core.AssemblyInfo.fs"
printf '// <auto-generated>\n' >"$FIX/src/FS.GG.Coord.Core/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.fs"
touch -d '1 hour ago' "$FIX/src/FS.GG.Coord.Core/obj/Debug/net10.0"/*.fs
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1572: a newer generated 'obj/**/*.fs' is NOT source — the board write lands, exit 0"
else
  bad ".github#1572: MSBuild's generated obj/ stubs must not false-STALE a board write — a dotnet test would halt the fleet" "rc=$rc out=$out"
fi

# ...AND `bin/` TOO. It is the other half of the same `.gitignore` line, and the engine's own output
# directory: a `.fs` copied beside the binary is an OUTPUT, and nothing there is a source it is behind.
printf '// <auto-generated>\n' >"$FIX/src/FS.GG.Coord.Cli/bin/Release/net10.0/Generated.fs"
touch -d '1 hour ago' "$FIX/src/FS.GG.Coord.Cli/bin/Release/net10.0/Generated.fs"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1572: a newer '.fs' under 'bin/' is build OUTPUT, not input — the board write lands"
else
  bad ".github#1572: files under bin/ are outputs and must not be measured as sources" "rc=$rc out=$out"
fi

# THE DIRECTION THAT WORKS MUST KEEP WORKING, ASSERTED IN THE SAME RUN. #1572's own instruction: do not
# fix the false-red by weakening the guard. With the generated files still sitting there — newer than the
# engine and now ignored — a genuinely edited HAND-WRITTEN source must still refuse the write. A pruning
# bug that pruned too much would pass every leg above and only this one says so.
stale "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q "$FIXSRC"; then
  ok ".github#1572: a newer HAND-WRITTEN src/**/*.fs still REFUSES the board write, and is the file NAMED"
else
  bad ".github#1572: the real staleness direction must survive the obj/ pruning — this is the leg that stops a weakened guard" "rc=$rc out=$out"
fi
rm -rf "$FIX"; FIX="$(mktemp -d)"; fixture "$FIX"

# ---- 3e. THE CHECKOUT IS NOT A REF EITHER (.github#1549) -----------------------------------------
# §3 asks "is the ARTIFACT behind the source beside it?" and it was answered NO — correctly — while the
# fleet ran pre-#1516 code and wrote a `--json` token into a live claim's `Paths:` line, ninety minutes
# after this repo closed #1507. The shared checkout every worker resolves through was 14 commits behind
# `origin/main` and its engine was a FAITHFUL build of that older source: binary and source agreed, and
# BOTH lagged `main`. Two workers hit it on the same board on the same day.
#
# THE FIXTURE IS A REAL CLONE, for §5's reason applied to a different git question. Only `git clone`
# writes `refs/remotes/origin/HEAD`, which is how the guard learns the default branch's NAME; a hand-built
# lookalike would silently exercise the `main`/`master` fallback and certify a path the real repo does not
# take. And only a real `fetch` advances a remote-tracking ref without touching a single working-tree
# mtime — which IS the bug's shape, and what makes §3's guard structurally unable to see it.
upstream_fixture() {  # $1 = upstream dir, $2 = clone dir, $3 = engine marker
  mkdir -p "$1/src/FS.GG.Coord.Core" "$1/docs"
  ( cd "$1" && git init -q . && git symbolic-ref HEAD refs/heads/main ) >/dev/null 2>&1
  printf 'bin/\nobj/\n' >"$1/.gitignore"
  printf '// source\n' >"$1/$FIXSRC"
  printf 'notes\n' >"$1/docs/notes.md"
  ( cd "$1" && git add -A && git -c user.email=t@t -c user.name=t commit -qm init ) >/dev/null 2>&1
  git clone -q "$1" "$2" >/dev/null 2>&1
  local bin="$2/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
  mkdir -p "$(dirname "$bin")"
  printf '#!/usr/bin/env bash\necho "%s RAN: $*"\n' "$3" >"$bin"; chmod +x "$bin"
  : >"$bin.dll"
  # EVERY mtime EXPLICIT (§3's rule): the clone's checkout stamps every file at `now`, so without this
  # the engine would be older than its own source and these legs would read §3's verdict, not this one.
  touch -d '3 hours ago' "$2/$FIXSRC" "$2/docs/notes.md"
  touch -d '2 hours ago' "$bin" "$bin.dll"
}
push_upstream() {   # $1 = upstream dir, $2 = clone dir, $3 = path to touch. Commit there, fetch here.
  printf '// upstream moved\n' >>"$1/$3"
  ( cd "$1" && git add -A && git -c user.email=t@t -c user.name=t commit -qm "move $3" ) >/dev/null 2>&1
  ( cd "$2" && git fetch -q origin ) >/dev/null 2>&1
}

UP="$(mktemp -d)/up"; CL="$(mktemp -d)/clone"
upstream_fixture "$UP" "$CL" "CLONE ENGINE"

# LEVEL WITH ITS UPSTREAM → SILENT. The happy path, and the one a careless implementation ruins: this
# check runs on EVERY invocation of every worker, so a false positive here is a fleet-wide outage.
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1549: a checkout LEVEL with its upstream is silent — a board write lands, exit 0"
else
  bad ".github#1549: a current checkout must not be called stale" "rc=$rc out=$out"
fi

# BEHIND ON SOMETHING THAT IS NOT THE ENGINE → STILL SILENT. #1549's second criterion, and the objection
# this file recorded for a year as the reason not to ask at all: *"it would fire on every checkout that is
# merely BEHIND — the normal state of a shared checkout nobody fetches."* The answer is the SUBJECT. A
# checkout behind on docs, workflows or registry rows is not running different code, and halting it would
# be the manufactured outage the old comment feared.
push_upstream "$UP" "$CL" "docs/notes.md"
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1549: BEHIND on a non-engine path is not stale — the bar is drift in the engine's own source trees"
else
  bad ".github#1549: 'merely behind' must not halt a worker — only drift under src/FS.GG.Coord.* counts" "rc=$rc out=$out"
fi

# BEHIND UNDER THE ENGINE'S SOURCE TREES → REFUSE THE WRITE. The reported defect, reproduced: nothing in
# the working tree moved (the fetch touches no mtime), so §3's guard is silent BY CONSTRUCTION and this
# is the only thing that can speak. `widen` is the verb both reports were bitten on.
push_upstream "$UP" "$CL" "$FIXSRC"
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q 'BEHIND'; then
  ok ".github#1549: a checkout BEHIND main under the engine's source trees REFUSES 'widen' (exit $rc) — the engine never ran"
else
  bad ".github#1549: an engine built from a commit main has moved past must be refused a board write" "rc=$rc out=$out"
fi

# THE REMEDY NAMES THE CHECKOUT AND THE BUILD (#1549's third criterion). The worker who hits this is
# standing in a CURRENT worktree and has no reason to suspect a binary somewhere else, so "rebuild it"
# without a path sends them to rebuild the tree that was never stale. Asserted on the absolute paths.
#
# THE FF-ONLY SPELLING IS `merge`, NOT `pull`, AND THE REF IS THE ONE THE COUNT WAS TAKEN AGAINST
# (.github#1664). This leg only READS the line; the detached-HEAD legs below RUN it, which is what would
# have caught the defect this pins — a remedy can name both absolute paths correctly and still not
# execute in the state its reader is standing in.
if printf '%s' "$out" | grep -q "git -C $CL merge --ff-only refs/remotes/origin/main" \
   && printf '%s' "$out" | grep -q "dotnet build $CL/src/FS.GG.Coord.Cli -c Release"; then
  ok ".github#1549: the refusal names WHICH checkout to update and WHICH build to re-run, by absolute path"
else
  bad ".github#1549: the refusal must name the checkout that owns the stale build — a relative path names the wrong tree at tier 2b" "out=$out"
fi

# ...AND A READ STILL RUNS. #929's trade is preserved verbatim: this changes what "stale" MEANS, never
# what it costs. Refusing diagnosis at the moment the fleet is degraded would be the worse bug.
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>/dev/null)"; rc=$?
err="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>&1 >/dev/null)"
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' && printf '%s' "$err" | grep -qi 'stale'; then
  ok ".github#1549: a READ on a behind-main checkout WARNS and still runs — reads warn, writes refuse, unchanged"
else
  bad ".github#1549: the read side of #929's trade must survive the new comparison" "rc=$rc out=$out err=$err"
fi

# AND IT REACHES THE READER THROUGH TIER 2b, which is the shape every worker actually has. #1549's
# reporter was standing in a worktree freshly branched from `origin/main` — their SOURCE was current, and
# the ENGINE they exec'd came from a shared checkout that was not. Tier 2b measures $SHARED, so the
# verdict must follow the engine rather than the caller.
CLWT="$(mktemp -d)/wt"; ( cd "$CL" && git worktree add -q --detach "$CLWT" ) >/dev/null 2>&1
out="$(cd "$CLWT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q "$CL"; then
  ok ".github#1549: a CURRENT worktree resolving a BEHIND shared engine is refused, and the SHARED checkout is the one named (#931 + #1549)"
else
  bad ".github#1549: tier 2b must carry the verdict about the engine it resolved, not about the caller's tree" "rc=$rc out=$out"
fi
( cd "$CL" && git worktree remove --force "$CLWT" ) >/dev/null 2>&1

# CATCHING UP CLEARS IT, and that is what makes the refusal a stall rather than a wall. A guard whose
# printed remedy does not clear it is the one the fleet learns to route around (#1572's `dotnet test`
# treadmill, one level up).
#
# AND THE REMEDY IS EXTRACTED FROM THE MESSAGE AND RUN AS PRINTED, never restated here (.github#1664).
# This leg used to claim "the printed remedy is the real one" while executing a DIFFERENT command than
# the one printed: the message said `pull --ff-only` and the test ran `merge --ff-only`. So the two were
# free to disagree, and for the whole life of the leg they did — the assertion certified its own
# restatement rather than the guard's output, which is precisely how a remedy that exits 1 in the field
# stayed green here. Grepping the line out and `eval`ing it is what makes the claim testable.
printed_ff() {  # $1 = refusal text → the ff-only remedy line it printed, verbatim, leading space trimmed
  printf '%s\n' "$1" | grep -E '^[[:space:]]*git -C .*--ff-only' | head -1 | sed 's/^[[:space:]]*//'
}

out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"
ff="$(printed_ff "$out")"
eval "$ff" >/dev/null 2>&1; ffrc=$?
touch -d '3 hours ago' "$CL/$FIXSRC" "$CL/docs/notes.md"   # the merge stamps the worktree; §3's rule
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$ffrc" -eq 0 ] && [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' \
   && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1549: the remedy AS PRINTED (${ff/$CL/\$CL}) clears the refusal on a checkout that is on a branch"
else
  bad ".github#1549: a refusal whose remedy does not clear it teaches the fleet to ignore the remedy" \
      "ff=$ff ffrc=$ffrc rc=$rc out=$out"
fi

# AND IT RUNS ON A DETACHED HEAD — THE STATE THE SHARED CHECKOUT IS ACTUALLY IN (.github#1664).
# (A leg of §3e, deliberately not a section of its own: `§3f` is already spoken for by the #1586
# kit-content section below, and ADR-0068, `registry/repos.CHANGELOG.md`, `scripts/fsgg-coord` and
# `scripts/fsgg-coord-guards.sh` all cite it by that letter.)
#
# The fixture above is a `git clone`, so it sits on a BRANCH, and `git pull --ff-only` is correct on a
# branch. That is exactly why the defect survived §3e for its whole life: the one state the remedy is
# addressed to was the one state never constructed. The checkout it names is the SHARED one, and on this
# host `git worktree list` reports it DETACHED — where `pull --ff-only` exits 1 with "You are not
# currently on a branch" and moves nothing.
#
# The stakes are what make this worth its own leg rather than a parameter. The refusal the remedy is
# attached to is fail-closed on EVERY board write in the fleet (.github#1549), so its reader is by
# construction blocked: `claim`, `heartbeat`, `done`, `set-field` and `widen` are all refused until the
# shared checkout is current. A remedy that exits 1 there hands that reader a git error about branches
# with no visible connection to engine staleness, leaves the refusal standing, and invites the one
# conclusion that is wrong — that the GUARD is broken rather than the escape hatch.
push_upstream "$UP" "$CL" "$FIXSRC"
( cd "$CL" && git checkout -q --detach HEAD ) >/dev/null 2>&1
touch -d '3 hours ago' "$CL/$FIXSRC" "$CL/docs/notes.md"
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q 'BEHIND'; then
  ok ".github#1664: a DETACHED checkout behind main still REFUSES the write — the state is reachable here"
else
  bad ".github#1664: the detached fixture must reproduce the refusal the remedy is attached to" "rc=$rc out=$out"
fi

ff="$(printed_ff "$out")"
eval "$ff" >/dev/null 2>&1; ffrc=$?
touch -d '3 hours ago' "$CL/$FIXSRC" "$CL/docs/notes.md"
out="$(cd "$CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$ffrc" -eq 0 ] && [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'CLONE ENGINE RAN' \
   && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1664: the remedy AS PRINTED (${ff/$CL/\$CL}) fast-forwards a DETACHED checkout and clears the refusal"
else
  bad ".github#1664: the printed remedy must run AS PRINTED from the state the shared checkout is in — 'git pull --ff-only' exits 1 on a detached HEAD and leaves the fail-closed refusal standing" \
      "ff=$ff ffrc=$ffrc rc=$rc out=$out"
fi
rm -rf "$UP" "$CL" "$CLWT"

# NO SUBJECT vs NO ANSWER — the boundary this change chose, asserted from both sides so it cannot be
# moved by accident.
#
# NO `origin` AT ALL → SILENT. There is no default branch to be behind: a `git init` scratch tree, a
# parity fixture, an air-gapped clone. Same shape as "no IL to measure against" and `dirty_guard`'s
# "no HEAD, no baseline". (Every §3-§6 leg depends on this; it is asserted once, on purpose, so the
# dependence is visible rather than incidental.)
fixture "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok ".github#1549: a checkout with NO origin remote has no default branch to be behind — no verdict, not a refusal"
else
  bad ".github#1549: a remote-less checkout must be silent, or every fixture and air-gapped clone halts" "rc=$rc out=$out"
fi

# AN `origin` WITH NO RESOLVABLE DEFAULT-BRANCH REF → NO VERDICT, AND A NO-VERDICT REFUSES THE WRITE.
# #1549's fourth criterion, and the file's own repeated lesson: `-quit` on BSD, `showUntrackedFiles=no`,
# `--no-optional-locks` on old git — three probes whose FAILURE was invisible, so their silence read as a
# clean answer. This is the fourth, pre-empted. "I could not look" and "I looked, and it is current" must
# not share an exit code, and this is the one place fail-closed costs only a stall.
( cd "$FIX" && git remote add origin https://example.invalid/nope.git ) >/dev/null 2>&1
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q 'could NOT be decided'; then
  ok ".github#1549: an unanswerable staleness question REFUSES the board write (exit $rc) — it does not read as freshness (#266)"
else
  bad ".github#1549: 'I could not look' must never share an exit code with 'I looked, and it is current'" "rc=$rc out=$out"
fi

# ...AND EVEN THEN A READ RUNS. Fail-closed is scoped to writes here too. A no-verdict that also blinded
# `who`, `ready` and `landable` would take away the tools for diagnosing it.
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>/dev/null)"; rc=$?
err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" who 2>&1 >/dev/null)"
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' && printf '%s' "$err" | grep -qi 'stale'; then
  ok ".github#1549: a no-verdict still PERMITS a read — the refusal is scoped to writes, as #929 scoped it"
else
  bad ".github#1549: a no-verdict must not blind the verbs used to diagnose it" "rc=$rc out=$out err=$err"
fi
rm -rf "$FIX"; FIX="$(mktemp -d)"

# AND A PATHSPEC THAT MATCHES NOTHING IS A NO-VERDICT TOO — the fail-open INSIDE this repair, closed
# before it could be shipped. `ENGINE_SOURCE_TREES` is three hard-coded directory names; rename or move a
# project and `rev-list --count` answers `0` forever, over nothing, and `0` is exactly what "current"
# looks like. That is the shape `check-engine-freshness.py` guards with `_assert_exists` and the shape
# `-quit`, `showUntrackedFiles=no` and `--no-optional-locks` each wore before it — a probe whose failure
# is invisible. The fixture is a real clone whose history never touches `src/FS.GG.Coord.*`.
NOSUBJ_UP="$(mktemp -d)/up"; NOSUBJ_CL="$(mktemp -d)/clone"
mkdir -p "$NOSUBJ_UP/docs"
( cd "$NOSUBJ_UP" && git init -q . && git symbolic-ref HEAD refs/heads/main ) >/dev/null 2>&1
printf 'bin/\nobj/\n' >"$NOSUBJ_UP/.gitignore"
printf 'notes\n' >"$NOSUBJ_UP/docs/notes.md"
( cd "$NOSUBJ_UP" && git add -A && git -c user.email=t@t -c user.name=t commit -qm init ) >/dev/null 2>&1
git clone -q "$NOSUBJ_UP" "$NOSUBJ_CL" >/dev/null 2>&1
mkdir -p "$NOSUBJ_CL/src/FS.GG.Coord.Cli/bin/Release/net10.0" "$NOSUBJ_CL/src/FS.GG.Coord.Core"
NOSUBJ_BIN="$NOSUBJ_CL/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
printf '#!/usr/bin/env bash\necho "NOSUBJ ENGINE RAN: $*"\n' >"$NOSUBJ_BIN"; chmod +x "$NOSUBJ_BIN"
: >"$NOSUBJ_BIN.dll"
printf '// untracked source\n' >"$NOSUBJ_CL/$FIXSRC"
touch -d '3 hours ago' "$NOSUBJ_CL/$FIXSRC"
touch -d '2 hours ago' "$NOSUBJ_BIN" "$NOSUBJ_BIN.dll"
out="$(cd "$NOSUBJ_CL" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" widen "$FIXREF" --paths src/Foo.fs 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'NOSUBJ ENGINE RAN' \
   && printf '%s' "$out" | grep -qi 'refused' && printf '%s' "$out" | grep -q 'measuring nothing'; then
  ok ".github#1549: an engine-source pathspec that matches NO commit is a no-verdict, not a zero — the guard refuses to report on an unwatched subject"
else
  bad ".github#1549: a moved or renamed engine project must not leave this guard silently counting zero forever" "rc=$rc out=$out"
fi
rm -rf "$NOSUBJ_UP" "$NOSUBJ_CL"


# ---- 3f. THE GUARD MODULE IS NOT KIT CONTENT, AND ITS ABSENCE IS A REFUSAL (.github#1586) ---------
# #1586's whole subject: `scripts/fsgg-coord` is a `coordination-kit` row, so every edit to it is
# mirrored byte-identical into seven receivers and obliges a kit republish that stales all seven. Three
# of the six churn events measured in one day (#1528, #1534, #1535) changed nothing but the verb
# partition — code no receiver can reach, because the guards load only where a source build resolved and
# only the repo owning coord's source has one.
#
# So the partition moved to `scripts/fsgg-coord-guards.sh`, which has no kit row. These legs assert the
# two properties that make that true and safe, and they MEASURE the payoff rather than asserting it
# (#1586 criterion 5): (a) it is genuinely not packable; (b) its absence REFUSES rather than silently
# un-guarding. (c) pins what is deliberately NOT fixed.
#
# ITS OWN FIXTURE, NOT `$FIX`. These legs delete an engine and drive a shim copy; reusing the shared
# fixture would leave §4 and §5 asserting against a tree this section had quietly rearranged.

# a. NOT PACKABLE — asked of the roster the packer itself reads. `src/FS.GG.Kit/stage-kit.sh` stages
#    exactly the `kit:` rows of `registry/repos.yml` through `scripts/repos.sh` (ADR-0058: derive, don't
#    restate), so "is it kit content?" IS "does a kit row name it?" and nothing else. Asking the roster
#    rather than diffing a staged tree keeps the leg honest without a `dotnet pack` on every run.
KITSRC="$(bash "$REPO_ROOT/scripts/repos.sh" kit --field source --registry "$REPO_ROOT/registry/repos.yml" 2>/dev/null)"
if [ -z "$KITSRC" ]; then
  bad "#1586: could not read the kit rows from registry/repos.yml (is PyYAML present?) — this leg cannot decide, and that is NOT a verdict about the split"
else
  if printf '%s\n' "$KITSRC" | grep -qx 'scripts/fsgg-coord'; then
    ok "#1586: 'scripts/fsgg-coord' IS a kit row — the split's premise still holds (if this ever flips the split is moot, not passing)"
  else
    bad "#1586: 'scripts/fsgg-coord' is no longer a kit row — re-read this section's premise before trusting it" "$KITSRC"
  fi
  if printf '%s\n' "$KITSRC" | grep -qx 'scripts/fsgg-coord-guards.sh'; then
    bad "#1586: the guard module was GIVEN a kit row — the partition is kit content again, and editing it stales seven receivers once more" "$KITSRC"
  else
    ok "#1586: the guard module has NO kit row, so the packer cannot stage it — a partition edit is now a .github commit, not a republish + 7-receiver fan-out"
  fi
fi

# b. ITS ABSENCE IS A REFUSAL, NOT A SILENT SKIP — the leg that makes the split safe rather than merely
#    tidy. `[ -f ] && source` would turn a missing module into "no guards": silence indistinguishable
#    from a clean tree, which is precisely the #266 shape the module warns about three times over
#    `find -quit`, `status.showUntrackedFiles` and `--no-optional-locks`. Rebuilding that hole while
#    moving the file would trade a churn problem for a correctness one.
#
#    The module is resolved from `${BASH_SOURCE[0]}`'s directory, so copying the shim ALONE into an empty
#    directory is exactly the "module missing" state. The verb is a board WRITE and the source is STALE,
#    so the pass condition is the strong one: non-zero, the module named, and the fixture engine unrun.
M86="$(mktemp -d)"; fixture "$M86"; stale "$M86"
NOMOD="$(mktemp -d)"
cp "$SHIM" "$NOMOD/fsgg-coord"; chmod +x "$NOMOD/fsgg-coord"
out="$(cd "$M86" && env -u FSGG_COORD_ENGINE_BIN "$NOMOD/fsgg-coord" release "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && ! printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && printf '%s' "$out" | grep -q 'fsgg-coord-guards.sh'; then
  ok "#1586: a source build with NO guard module beside the shim REFUSES the board write (exit $rc) and NAMES the module — an absent guard is not a clean bill of health (#266)"
else
  bad "#1586: a missing guard module must refuse a board write, never exec unguarded" "rc=$rc out=$out"
fi

#    ...AND THE RECEIVERS' SHAPE IS UNAFFECTED BY THAT REFUSAL, which is the other half of the trade. A
#    receiver has no source build, so it never asks for the module and must not be broken by its absence.
#    Same module-less shim, same tree with its engine removed: tier 2 misses and resolution proceeds
#    exactly as it did before this change.
find "$M86/src" -name 'fsgg-coord-engine*' -delete 2>/dev/null
err="$(cd "$M86" && env -u FSGG_COORD_ENGINE_BIN "$NOMOD/fsgg-coord" release "$FIXREF" 2>&1 >/dev/null)"; rc=$?
if printf '%s' "$err" | grep -qi 'no fsgg-coord engine found'; then
  ok "#1586: with NO source build, the missing guard module is never asked for — the receivers' shape resolves (or refuses) exactly as before"
else
  bad "#1586: a source-less checkout must not be broken by the absent guard module" "rc=$rc err=$err"
fi
rm -rf "$NOMOD" "$M86"

# c. THE FOLLOW-UP LANDED, AND THIS LEG IS NOW THE OTHER WAY UP (#1615, 2026-07-28, ADR-0068).
#
#    THIS LEG USED TO READ, and the wording is kept because it is the record of what was true:
#      *"THE KNOWN THAT IS NOT FIXED, PINNED SO IT CANNOT BE MISREAD AS FIXED. The engine's tool
#      manifest is still a kit row, so an ENGINE version bump still republishes the kit and still
#      stales every receiver — the other three of the six churn events (#1507, #1517, #1523). …If this
#      leg ever flips, that follow-up landed, and the 'reduced, not removed' wording in both shim
#      files should be revisited with it."*
#
#    It flipped, deliberately, and this is the revisit that sentence asked for. #1615 took
#    `dist/dotnet/.config/dotnet-tools.json` off the `kit:` block: the engine's version now reaches
#    receivers through Renovate's nuget manager, which already reads `/(^|/)dotnet-tools\.json$/` as a
#    shipped `managerFilePattern`. So an engine bump no longer edits kit content, no longer stales
#    `registry/repos.lock`, and no longer obliges a republish.
#
#    #1586's criterion 5 IS NOW MEETABLE, and the assertion below is what holds it. Between #1586 and
#    #1615 the criterion was retired as unachievable; it is un-retired, because the two doors it named
#    are both shut — the verb partition by #1586 (leg (a) above), the version pin by #1615.
#
#    #1077's INVARIANT IS NOT GONE. It moved from `repos.sh validate` (which asserted two rows share a
#    fabric — `f(this roster)`) to `repos-audit.sh`'s engine-manifest sweep (which reads each
#    receiver's actual `.config/dotnet-tools.json` — `f(roster, receiver tree)`). That is strictly
#    stronger: a receiver that deletes its own manifest was invisible to the old rule and reds on the
#    new one. See ADR-0068 and `tests/repos-audit/run.sh`'s engine-manifest legs, which are
#    mutation-proven.
#
#    IF THIS LEG EVER FLIPS BACK, somebody re-added the manifest to the kit. That is not forbidden —
#    but it re-opens the republish door #1615 closed, so read ADR-0068 before believing it was
#    intended, and re-retire criterion 5 if it was.
if printf '%s\n' "$KITSRC" | grep -qx 'dist/dotnet/.config/dotnet-tools.json'; then
  bad "#1615: the engine tool manifest is a kit row AGAIN — an engine bump republishes the kit and stales seven receivers once more, and #1586's criterion 5 is unmeetable again. Read ADR-0068 before accepting this" "$KITSRC"
else
  ok "#1615: the engine tool manifest has NO kit row (ADR-0068), so an engine version bump is a .github commit and a per-receiver Renovate PR — not a republish + 7-receiver fan-out. With leg (a), #1586's criterion 5 is now MEETABLE"
fi

# d. …AND #1077's INVARIANT IS STILL ASSERTED SOMEWHERE — the leg that stops (c) being a licence to
#    simply delete a rule. #1615's AC2 is explicit that the co-fabric rule may be REPLACED and not
#    merely dropped, so this asserts the replacement EXISTS rather than trusting the ADR's prose. It
#    is deliberately a check on the audit, not on the roster: the whole point of the replacement is
#    that it lives where it can read a receiver's tree.
if grep -q 'the engine-manifest sweep (#1615)' "$REPO_ROOT/scripts/repos-audit.sh" \
   && grep -q 'fs.gg.coord.cli' "$REPO_ROOT/scripts/repos-audit.sh"; then
  ok "#1615/#1077: the engine-manifest sweep is present in repos-audit.sh — the invariant the kit row used to buy by construction is asserted against every receiver's tree (AC2)"
else
  bad "#1615: the engine manifest left the kit and NOTHING replaced #1077's invariant — templates and audio can hold a fsgg-coord shim with no engine again, which is exactly the defect #1077 closed" "the sweep is missing from scripts/repos-audit.sh"
fi
# ---- 4. THE SOURCE BUILD OUTRANKS A PACKAGED ENGINE (#1018, #1008) -------------------------------
# EVERY LEG IN §3 DRIVES THE SHIM WITH `env -u FSGG_COORD_ENGINE_BIN`, WHICH UNSETS TIER 1 AND NOTHING
# ELSE — and that was never enough. Under the old order a global tool on PATH exec'd BEFORE the source
# build was ever considered, so on any machine with `dotnet tool install -g` — the receivers' DOCUMENTED
# shape, and the first remedy the shim's own `die()` prints — every fixture leg above silently drove the
# REAL engine at the REAL board. It made 2 live claims on `.github#1` and the guard legs passed vacuously
# (#1008); the same preemption falsely closed epic #889 in production, with the #1005 guard that refuses
# it sitting BUILT in `src/` (#1018). One cause: the ORDER. The guard was never mis-scoped, it was
# unreachable, and §3 could not see that because §3 was unreachable in the same breath.
#
# So PATH IS THE VARIABLE, and these legs pin what #1008 measured as a 6-assertion swing on nothing but
# PATH: same fixture, same argv, a global tool ADDED — same verdict. §3 cannot go vacuous again without
# reddening here.
#
# THE FAKE TOOL NEVER TALKS TO GITHUB. A leg that can post a claim is the bug, not the test for it.
GLOBALDIR="$(mktemp -d)"
printf '#!/usr/bin/env bash\necho "GLOBAL TOOL RAN: $*"\n' >"$GLOBALDIR/fsgg-coord-engine"
chmod +x "$GLOBALDIR/fsgg-coord-engine"

# FRESH source build + a global tool on PATH: the SOURCE BUILD runs. ADR-0034 decision 2 in one assertion
# — `.github` builds the engine from source and never depends on the feed, so a feed build sitting on PATH
# may not answer for it. Under the old order this leg returned "GLOBAL TOOL RAN".
fixture "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN PATH="$GLOBALDIR:$PATH" "$SHIM" next 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && ! printf '%s' "$out" | grep -q 'GLOBAL TOOL RAN'; then
  ok "precedence: a global tool on PATH does NOT preempt the source build — .github never runs the feed (ADR-0034 decision 2)"
else
  bad "precedence: the source build must outrank a global tool on PATH" "rc=$rc out=$out"
fi

# STALE source build + a BOARD WRITE + a global tool on PATH: still REFUSED, and the global tool never ran.
# This is the exact shape that falsely closed #889: a guard is only a guard if PATH cannot route around it.
stale "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN PATH="$GLOBALDIR:$PATH" "$SHIM" release "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$out" | grep -qi 'refused' \
   && ! printf '%s' "$out" | grep -q 'GLOBAL TOOL RAN'; then
  ok "precedence: a global tool cannot route a board write around the STALE guard (exit $rc) — #929's guard is REACHABLE (#1018)"
else
  bad "precedence: a stale board write must be refused even with a global tool on PATH" "rc=$rc out=$out"
fi

# TIER 1 STILL OUTRANKS THE SOURCE BUILD. The reorder lifted the source build above the two PACKAGED forms,
# NOT above the explicit instruction — so an operator who means the global tool here still says so and is
# obeyed, silently. That is the escape hatch that makes this reorder safe to land on a live fleet.
fixture "$FIX"; stale "$FIX"
out="$(cd "$FIX" && FSGG_COORD_ENGINE_BIN="$GLOBALDIR/fsgg-coord-engine" PATH="$GLOBALDIR:$PATH" "$SHIM" release "$FIXREF" 2>/dev/null)"; rc=$?
err="$(cd "$FIX" && FSGG_COORD_ENGINE_BIN="$GLOBALDIR/fsgg-coord-engine" PATH="$GLOBALDIR:$PATH" "$SHIM" release "$FIXREF" 2>&1 >/dev/null)"
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'GLOBAL TOOL RAN' && [ -z "$err" ]; then
  ok "precedence: tier 1 still outranks the source build — an explicit bin is an instruction, and the operator's escape hatch"
else
  bad "precedence: an explicit bin must still win over the source build" "rc=$rc out=$out err=$err"
fi

# A RECEIVER IS BYTE-FOR-BYTE UNTOUCHED: no source build, so the global tool answers exactly as before. The
# reorder keys on the BUILD's existence, not on a repo name — only the repo that owns coord's source can
# have that path — so there is no `.github` special-case here to drift out of step with the roster.
RCV="$(mktemp -d)"; ( cd "$RCV" && git init -q . ) >/dev/null 2>&1
out="$(cd "$RCV" && env -u FSGG_COORD_ENGINE_BIN PATH="$GLOBALDIR:$PATH" "$SHIM" release "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'GLOBAL TOOL RAN'; then
  ok "precedence: a receiver (no source build) still resolves the global tool — the reorder keys on the BUILD, not a repo name"
else
  bad "precedence: a receiver's resolution must be unchanged by the reorder" "rc=$rc out=$out"
fi
rm -rf "$RCV" "$GLOBALDIR"
rm -rf "$FIX"

# ---- 5. TIER 2b — THE SHARED CHECKOUT'S BUILD, FROM AN ITEM WORKTREE (#931) -----------------------
# `/pnext-item` §2 MANDATES an item worktree, and a fresh worktree has no `bin/`. So for EVERY worker
# following the recipe, tier 2a missed, tier 3 found no global tool, tier 4 found no manifest, and the
# shim died 69 — while the tool printed 25 commands into exactly that. Both spellings were broken:
# `fsgg-coord-engine <verb>` is 127, `scripts/fsgg-coord <verb>` was 69. These legs pin the repair.
#
# THE FIXTURE IS A REAL WORKTREE, not a directory that resembles one. `--git-dir` vs `--git-common-dir`
# is the shim's own question, and only `git worktree add` makes them diverge — a hand-built lookalike
# would answer it wrong and pass for the wrong reason.
committed_fixture() {  # $1 = dir, $2 = marker. A COMMITTED source-build checkout, engine NEWER than source.
  mkdir -p "$1/src/FS.GG.Coord.Cli/bin/Release/net10.0" "$1/src/FS.GG.Coord.Core"
  printf '// source\n' >"$1/$FIXSRC"
  # `bin/` is IGNORED, exactly as the real repo ignores it — and it is what makes this fixture test the
  # right thing. A committed `bin/` would be checked out INTO the worktree, so tier 2a would resolve
  # there and tier 2b would never be reached: the leg would pass while asserting nothing.
  printf 'bin/\nobj/\n' >"$1/.gitignore"
  ( cd "$1" && git init -q . \
      && git add -A && git -c user.email=t@t -c user.name=t commit -qm init ) >/dev/null 2>&1
  local bin="$1/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
  printf '#!/usr/bin/env bash\necho "%s RAN: $*"\n' "$2" >"$bin"; chmod +x "$bin"
  : >"$bin.dll"
  touch -d '3 hours ago' "$1/$FIXSRC"
  touch -d '2 hours ago' "$bin" "$bin.dll"
}

SH="$(mktemp -d)/shared"; committed_fixture "$SH" "SHARED ENGINE"
WT="$(mktemp -d)/wt"; ( cd "$SH" && git worktree add -q --detach "$WT" ) >/dev/null 2>&1

# THE 69 IS GONE: a worktree with no build of its own resolves the SHARED checkout's engine.
out="$(cd "$WT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'SHARED ENGINE RAN'; then
  ok "tier 2b: an item worktree with NO build resolves the SHARED checkout's engine — not exit 69 (#931)"
else
  bad "tier 2b: a worktree must fall back to the shared build" "rc=$rc out=$out"
fi

# NO FALSE STALE — THE LEG THAT MATTERS, and the one an obvious implementation fails. `git worktree add`
# writes every file at checkout time, so the worktree's `.fs` mtimes are NEWER than any shared build BY
# CONSTRUCTION. A guard comparing the CALLER's src/ against the SHARED build therefore reports STALE on
# every fresh worktree — and `stale_guard` REFUSES board writes, so it would halt the whole fleet the
# moment it started working: fail-closed, fleet-wide, hiding inside an obviously-correct-looking line.
# The guards must measure the SHARED tree's own src/ ↔ build pair. Asserted with a BOARD WRITE, because
# a read would warn where a write refuses, and the refusal is the damage.
out="$(cd "$WT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" release "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'SHARED ENGINE RAN' \
   && ! printf '%s' "$out" | grep -qi 'stale'; then
  ok "tier 2b: a FRESH worktree is not STALE — the guards measure the shared tree's own src/↔build, so board writes still land (#931)"
else
  bad "tier 2b: a fresh worktree must not false-STALE a board write — that halts the fleet" "rc=$rc out=$out"
fi

# ...AND THE SHARED TREE'S REAL STALENESS STILL BITES. The mirror of the leg above: measuring the shared
# pair must not mean measuring nothing. Edit the SHARED source after its build, and a board write driven
# from the worktree is refused — the engine the worktree is about to run really is behind its source.
touch -d '1 hour ago' "$SH/$FIXSRC"
out="$(cd "$WT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" release "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$out" | grep -qi 'refused' \
   && ! printf '%s' "$out" | grep -q 'SHARED ENGINE RAN'; then
  ok "tier 2b: a genuinely STALE shared engine still REFUSES a board write driven from a worktree — the guard measures, it does not skip"
else
  bad "tier 2b: shared staleness must still refuse through the fallback" "rc=$rc out=$out"
fi
touch -d '3 hours ago' "$SH/$FIXSRC"

# #709's WARNING NOW REACHES THE READER IT IS ABOUT. `dirty_guard` exempts a linked worktree because
# nobody else resolves an engine through it — true, and it says nothing about the SHARED checkout whose
# WIP this worktree is now running. That is precisely #709: one worker editing the kit in the shared
# checkout silently decides every other worker's claims. Before tier 2b the worktree reader could not
# even reach that engine; now they run it, so they are told.
printf '// WIP\n' >>"$SH/$FIXSRC"
touch -d '3 hours ago' "$SH/$FIXSRC"
err="$(cd "$WT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q 'UNCOMMITTED'; then
  ok "tier 2b: a DIRTY shared checkout warns the worktree worker whose claims that WIP decides (#709)"
else
  bad "tier 2b: the shared checkout's dirt must reach the worktree reader running its engine" "rc=$rc err=$err"
fi
( cd "$SH" && git checkout -q -- "$FIXSRC" ) >/dev/null 2>&1

# TIER 2a STILL WINS. A kit author who builds IN their worktree must get THEIR build — preempting it
# with the shared engine would silently discard the very edits they are testing, which is the one
# workflow this fallback must not break.
OWN="$WT/src/FS.GG.Coord.Cli/bin/Release/net10.0"; mkdir -p "$OWN"
printf '#!/usr/bin/env bash\necho "OWN ENGINE RAN: $*"\n' >"$OWN/fsgg-coord-engine"; chmod +x "$OWN/fsgg-coord-engine"
: >"$OWN/fsgg-coord-engine.dll"
touch -d '3 hours ago' "$WT/$FIXSRC"; touch -d '2 hours ago' "$OWN/fsgg-coord-engine" "$OWN/fsgg-coord-engine.dll"
out="$(cd "$WT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" --version 2>/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'OWN ENGINE RAN'; then
  ok "tier 2b: a worktree that HAS its own build runs it — the fallback is after 2a, never before it (#931)"
else
  bad "tier 2b: the caller's own build must outrank the shared one" "rc=$rc out=$out"
fi

( cd "$SH" && git worktree remove --force "$WT" ) >/dev/null 2>&1
rm -rf "$SH" "$WT"

# ---- 6. DIRTINESS: THE SOURCE IS NOT A REF EITHER (#709) -----------------------------------------
# §3 pins `stale_guard`: is the ARTIFACT behind the source beside it? These pin `dirty_guard`, which asks
# the other half — is that SOURCE on main? — and the difference is not academic. #929's guard separates
# the two states that do NOT matter:
#
#   edited, NOT built  → .fs newer than .dll → stale_guard FIRES.  The engine is the OLD, merged code.
#   edited AND built   → .dll newer than .fs → stale_guard SILENT. The engine is somebody's WIP.
#
# It is loudest where nothing is wrong and mute where the whole fleet is running uncommitted code — and
# building is what a worker does NEXT, so #929's window closes itself exactly as the risk arrives. #709
# observed the consequence live: ` M scripts/fsgg-coord` in a checkout the reporter had not touched, found
# only because they diffed against HEAD afterwards. Under `src/` live the scheduler, the claim CAS and the
# board writer, and every worker on this board runs THIS checkout's engine.
#
# SO EVERY LEG BELOW KEEPS THE BUILD NEWER THAN ITS SOURCE — `stale_guard` silent by construction — and
# asserts what `dirty_guard` ALONE can see. A fixture that let the two guards overlap would pass on the
# wrong one's output, which is the failure these legs exist to make impossible.
#
# AND THE FIXTURE MUST BE COMMITTED, which is the one thing §3's is not: `fixture()` does `git init` and
# never commits, so `dirty_guard` returns at its no-HEAD gate for every leg above and §3 says nothing
# about this guard either way. `committed_fixture` is the shape with a baseline to be dirty AGAINST.
D="$(mktemp -d)/main"; committed_fixture "$D" "MAIN ENGINE"
DBIN="$D/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
# AND IT PINS ITS OWN `status.showUntrackedFiles`, because `git init` INHERITS the global config and the
# guard's probe obeys it: `--porcelain` does not override that setting (measured — it is a formatting flag,
# not a scope one). A developer who sets it to `no` — the usual remedy for a huge repo — would red the
# untracked and truncation legs below against a guard that has not changed. These legs measure the GUARD,
# not the environment of whoever runs them; a parity red has to be evidence rather than a coin toss (§3).
# That the guard ITSELF goes silent under that setting is a real hole, and NOT what these legs are for.
( cd "$D" && git config status.showUntrackedFiles normal ) >/dev/null 2>&1
# The guard's pathspec is wider than `src/`, so the fixture must carry the rest of it to be tested for it.
mkdir -p "$D/scripts"
for f in scripts/fsgg-coord Directory.Build.props Directory.Packages.props global.json; do
  printf 'seed\n' >"$D/$f"
done
( cd "$D" && git add -A && git -c user.email=t@t -c user.name=t commit -qm paths ) >/dev/null 2>&1

# CLEAN → SILENT, asserted on a BOARD WRITE. The happy path is what a false positive would ruin, and this
# fixture is the happy path in full: it HAS a built `bin/` sitting in the tree (written after the commit),
# gitignored exactly as the real repo ignores it. Were it not, this guard would fire on every worker after
# every legitimate build — a permanent false positive, and a fleet taught to skim the one warning that
# matters. `MAIN ENGINE RAN` is asserted, not just the silence (#1008): silence alone cannot tell "the
# guard stayed quiet" from "the fixture engine was never reached", and that is how #1008 stayed green for
# its whole life.
err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'MAIN ENGINE RAN'; then
  ok "dirtiness: a COMMITTED checkout is silent — a built, gitignored bin/ is not dirt (#709)"
else
  bad "dirtiness: a clean checkout must not warn — building is not modifying" "rc=$rc out=$out err=$err"
fi

# DIRTY + a BOARD WRITE: WARN, AND STILL RUN. This is the decision the issue asked for ("warn always,
# refuse never") and the line where this guard parts company with §3's. #929 may refuse a write because
# its remedy is local, cheap and YOURS: rebuild, and you are clear. Dirtiness has no such exit — the
# checkout is dirty because somebody ELSE is mid-edit, and nothing you can run will clean it. Refusing
# would let one worker's §2 violation halt every OTHER worker's board writes: a fleet-wide outage
# manufactured by the guard rather than by the bug. So `refused` must NOT appear and the engine MUST run.
#
# THE MTIME IS RESTORED AFTER THE EDIT, and that is load-bearing rather than tidy: appending makes the
# source newer than the build, which is `stale_guard`'s trigger — it would REFUSE this write, and the leg
# would go green on the wrong guard's word while proving nothing about this one.
printf '// WIP\n' >>"$D/$FIXSRC"; touch -d '3 hours ago' "$D/$FIXSRC"
out="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>&1)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'UNCOMMITTED' \
   && printf '%s' "$out" | grep -q 'MAIN ENGINE RAN' \
   && ! printf '%s' "$out" | grep -qi 'refused'; then
  ok "dirtiness: a dirty checkout WARNS on the board write 'claim' and STILL RUNS it — warn, never refuse (#709)"
else
  bad "dirtiness: a board write on a dirty checkout must warn and still run, unlike a STALE one" "rc=$rc out=$out"
fi

# STAGED WIP IS STILL NOT ON MAIN. This is why the guard reads `status --porcelain` and not the
# `diff --quiet` the issue proposed: `git add` hides an edit from `diff` entirely, and a staged edit
# compiles into the engine exactly as an unstaged one does.
( cd "$D" && git add -A ) >/dev/null 2>&1
err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q 'UNCOMMITTED'; then
  ok "dirtiness: STAGED work still warns — 'status --porcelain' sees what 'diff --quiet' cannot"
else
  bad "dirtiness: a staged edit is still uncommitted and must warn" "rc=$rc err=$err"
fi
( cd "$D" && git reset -q --hard ) >/dev/null 2>&1; touch -d '3 hours ago' "$D/$FIXSRC"

# AND SO IS AN UNTRACKED ONE — `diff --quiet` is blind to it too, and a new module arrives untracked.
printf '// new module\n' >"$D/src/FS.GG.Coord.Core/New.fs"; touch -d '3 hours ago' "$D/src/FS.GG.Coord.Core/New.fs"
err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q 'UNCOMMITTED'; then
  ok "dirtiness: an UNTRACKED source file warns — it is not on main either"
else
  bad "dirtiness: an untracked file under src/ must warn" "rc=$rc err=$err"
fi
rm -f "$D/src/FS.GG.Coord.Core/New.fs"

# THE SUBJECT IS BIGGER THAN src/, and this is the leg that keeps the guard from rebuilding #266 inside
# itself. The root `Directory.Build.props`, `Directory.Packages.props` and `global.json` are imported
# implicitly by every project beneath them, so a dirty package pin or SDK band changes the compiled engine
# exactly as an edited `.fs` does — and #672 is an epic whose whole subject is editing those files.
# `scripts/fsgg-coord` is in the pathspec for the plainest reason of all: it is the file #709 actually
# watched somebody edit, and it is the resolver every one of these tiers runs through.
#
# THE MATCH IS ON THE PORCELAIN MARKER (` M <path>`), NOT THE BARE PATH, and that is not fussiness: the
# guard's FIXED prose already contains the string `scripts/fsgg-coord` ("board runs THIS
# scripts/fsgg-coord…"), which it prints whatever is dirty. A bare `grep "$f"` would therefore match
# boilerplate rather than the listing, and that one leg would pass while asserting nothing — #1008's
# shape, rebuilt inside the legs written to end it. The status code only ever appears in the DETAIL.
for f in scripts/fsgg-coord Directory.Build.props Directory.Packages.props global.json; do
  printf 'x\n' >>"$D/$f"
  err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
  if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q 'UNCOMMITTED' && printf '%s' "$err" | grep -q "M $f"; then
    ok "dirtiness: a dirty '$f' warns and is NAMED — it ends up inside the engine as surely as a .fs does"
  else
    bad "dirtiness: '$f' is part of what builds the engine and must be watched" "rc=$rc err=$err"
  fi
  ( cd "$D" && git checkout -q -- "$f" ) >/dev/null 2>&1
done

# THE MESSAGE TRUNCATES AT 5 AND COUNTS THE REST — real arithmetic, on a path nobody reads until the day
# it matters, and a worker mid-rollout of #672 dirties more than five.
for i in 1 2 3 4 5 6 7; do printf '// wip\n' >"$D/src/FS.GG.Coord.Core/f$i.fs"; done
touch -d '3 hours ago' "$D"/src/FS.GG.Coord.Core/*.fs
err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q '7 path(s)' && printf '%s' "$err" | grep -q 'and 2 more'; then
  ok "dirtiness: the warning lists 5 paths and counts the remainder — '7 path(s)', 'and 2 more'"
else
  bad "dirtiness: the message must count every dirty path and truncate the list" "rc=$rc err=$err"
fi
rm -f "$D"/src/FS.GG.Coord.Core/f?.fs

# TIER 1 IS EXEMPT, STRUCTURALLY — an explicit bin execs before tier 2 is ever reached. This is why §1's
# corpus, which drives the shim through FSGG_COORD_ENGINE_BIN from a checkout that may well be dirty
# (somebody is always working this repo), cannot be polluted by this guard.
printf '// WIP\n' >>"$D/$FIXSRC"; touch -d '3 hours ago' "$D/$FIXSRC"
err="$(cd "$D" && FSGG_COORD_ENGINE_BIN="$DBIN" "$SHIM" claim "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$D" && FSGG_COORD_ENGINE_BIN="$DBIN" "$SHIM" claim "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'MAIN ENGINE RAN'; then
  ok "dirtiness: an explicit FSGG_COORD_ENGINE_BIN never consults the guard — tier 1 execs first"
else
  bad "dirtiness: tier 1 must not consult dirtiness" "rc=$rc out=$out err=$err"
fi
( cd "$D" && git checkout -q -- "$FIXSRC" ) >/dev/null 2>&1; touch -d '3 hours ago' "$D/$FIXSRC"

# A LINKED WORKTREE IS EXEMPT, AND GETTING THIS WRONG AIMS THE GUARD AT THE ONE WORKER OBEYING THE RULE IT
# ENFORCES. pnext-item §2 ORDERS you to work the item in a worktree; a kit author who does that and builds
# there resolves their OWN build (tier 2a) against a tree their own correct edits have made dirty. Warning
# them would accuse them of the §2 violation they are in the middle of avoiding — and there is nothing to
# warn about: no other worker resolves an engine through their worktree, so that WIP is reachable by
# nobody but them. Only the MAIN checkout is shared, and only its dirt is everyone's problem.
#
# THE COMPLEMENT IS §5's LAST-BUT-ONE LEG, and the two must be read together: there, a worktree with NO
# build of its own resolves the SHARED engine, and the shared checkout's dirt DOES reach that reader. The
# exemption is about whose tree is being measured, never about suppressing the warning.
#
# THE FIXTURE IS A REAL WORKTREE (§5's rule): `--git-dir` vs `--git-common-dir` is the guard's own
# question, and only `git worktree add` makes them diverge. A hand-built lookalike would answer it wrong
# and pass for the wrong reason.
DWT="$(mktemp -d)/kitwt"; ( cd "$D" && git worktree add -q --detach "$DWT" ) >/dev/null 2>&1
OWN2="$DWT/src/FS.GG.Coord.Cli/bin/Release/net10.0"; mkdir -p "$OWN2"
printf '#!/usr/bin/env bash\necho "WORKTREE ENGINE RAN: $*"\n' >"$OWN2/fsgg-coord-engine"
chmod +x "$OWN2/fsgg-coord-engine"; : >"$OWN2/fsgg-coord-engine.dll"
printf '// my own correct edit, in the worktree §2 ordered me into\n' >>"$DWT/$FIXSRC"
touch -d '3 hours ago' "$DWT/$FIXSRC"
touch -d '2 hours ago' "$OWN2/fsgg-coord-engine" "$OWN2/fsgg-coord-engine.dll"
err="$(cd "$DWT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$DWT" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'WORKTREE ENGINE RAN'; then
  ok "dirtiness: a kit author's DIRTY worktree is exempt — §2's sanctioned flow is not the violation it enforces (#709)"
else
  bad "dirtiness: a linked worktree must be exempt — nobody else resolves an engine through it" "rc=$rc out=$out err=$err"
fi
( cd "$D" && git worktree remove --force "$DWT" ) >/dev/null 2>&1

# THE CALLER'S GIT CONFIG CANNOT BLIND THE GUARD (#1043). The fixture's own pin above makes the legs
# measure the guard rather than the environment; this leg is the mirror of it, and the one that measures
# the guard's IMMUNITY. It forces `status.showUntrackedFiles=no` — a normal setting, the usual remedy for
# a slow `git status` on a big repo, inherited from ~/.gitconfig and therefore NOT something the shared
# checkout controls — and asserts the warning still arrives.
#
# BEFORE #1043 THIS RETURNED NOTHING AT ALL: `--porcelain` is a formatting flag and does not override that
# setting, so the probe came back empty on a tree full of WIP, "empty" read as "clean", and the guard went
# silent exactly where it is the only thing looking. The repair forces the scope at the call site
# (`-c status.showUntrackedFiles=normal`), so the answer no longer depends on who is asking.
( cd "$D" && git config status.showUntrackedFiles no ) >/dev/null 2>&1
printf '// uncommitted new module\n' >"$D/src/FS.GG.Coord.Core/Hostile.fs"
touch -d '3 hours ago' "$D/src/FS.GG.Coord.Core/Hostile.fs"
err="$(cd "$D" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"; rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$err" | grep -q 'UNCOMMITTED' \
   && printf '%s' "$err" | grep -q '?? src/FS.GG.Coord.Core/Hostile.fs'; then
  ok "dirtiness: 'status.showUntrackedFiles=no' cannot blind the guard — the probe forces its own scope (#1043)"
else
  bad "dirtiness: the caller's git config must not silence the guard" "rc=$rc err=$err"
fi
rm -f "$D/src/FS.GG.Coord.Core/Hostile.fs"
( cd "$D" && git config --unset status.showUntrackedFiles ) >/dev/null 2>&1

# NO HEAD, NO VERDICT. A checkout with no commit has no baseline to be dirty AGAINST, so there is nothing
# to assert — the same shape as `stale_guard`'s "no IL to measure against" (§3), and as `release`'s "a
# column we cannot read is not one we may overwrite" (#331). Everything here is untracked and the guard
# must still say nothing.
NOHEAD="$(mktemp -d)/nohead"; fixture "$NOHEAD"
err="$(cd "$NOHEAD" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>&1 >/dev/null)"; rc=$?
out="$(cd "$NOHEAD" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" claim "$FIXREF" 2>/dev/null)"
if [ "$rc" -eq 0 ] && [ -z "$err" ] && printf '%s' "$out" | grep -q 'ENGINE RAN'; then
  ok "dirtiness: a checkout with NO HEAD has no baseline to be dirty against — no verdict, not a false alarm"
else
  bad "dirtiness: a commit-less checkout must not warn" "rc=$rc out=$out err=$err"
fi
rm -rf "$NOHEAD" "$D" "$DWT"

# ---- 7. TIER 4 IS A PIPE TOO: `dotnet tool run` EATS --help (#1029) -------------------------------
#
# The shim's header promises a transparent pipe that "adds no output of its own" and passes "argv through
# unchanged". On TIER 4 — the RECEIVERS' shape (`.config/dotnet-tools.json` + `dotnet tool run`) — it was
# not one. `dotnet tool run` has its OWN `-?, -h, --help` option and CONSUMES those flags before the tool
# is ever reached, answering with its own help and EXIT 0. So on every receiver repo a worker who asked the
# coordination tool for help got dotnet's, and nothing reported a failure. `--help` is how a worker
# discovers a verb or a flag (#891) — what was undeliverable was the whole usage, not one line of it.
#
# WHY IT SURVIVED IS STRUCTURAL, AND IT IS THIS FILE'S FAULT. `.github` builds the engine from source and
# resolves at TIER 2 (ADR-0034 decision 2), which is a genuine `exec` and genuinely transparent — so the ONE
# repo that owns the shim is the one repo where the bug cannot reproduce, and not one leg above drove tier 4
# at all. Every tier the owner can reach was pinned; the tier only the receivers reach was not.
#
# WHY `dotnet` IS FAKED. Driving the real `dotnet tool run` needs the tool RESTORED from the auth-walled org
# feed, which no leg here may depend on. So the fake emulates exactly ONE behaviour, MEASURED against the
# real dotnet 10.0.302 rather than assumed — and nothing else:
#
#   dotnet tool run fsgg-coord-engine --help           -> dotnet's own help, exit 0  (eaten)
#   dotnet tool run fsgg-coord-engine next --help      -> dotnet's own help, exit 0  (eaten; NOT positional)
#   dotnet tool run fsgg-coord-engine --version        -> reaches the tool           (NOT eaten)
#   dotnet tool run fsgg-coord-engine -- next --help   -> reaches the tool
#
# Leg (a) needs no model at all: it asserts what the SHIM HANDED dotnet, which is the header's contract
# stated as an argv. Legs (b)-(d) show why that matters, against a dotnet that eats what the real one eats.
T4="$(mktemp -d)"; DOTNETDIR="$(mktemp -d)"; ARGV_LOG="$T4/argv.log"
( cd "$T4" && git init -q . ) >/dev/null 2>&1
mkdir -p "$T4/.config"
cat >"$T4/.config/dotnet-tools.json" <<'JSON'
{ "version": 1, "isRoot": true,
  "tools": { "fs.gg.coord.cli": { "version": "0.3.0", "commands": ["fsgg-coord-engine"] } } }
JSON
cat >"$DOTNETDIR/dotnet" <<'SH'
#!/usr/bin/env bash
# A fake `dotnet`. It emulates ONE measured behaviour of `dotnet tool run`: its own -?/-h/--help is
# consumed BEFORE any `--`, and answered by dotnet itself with exit 0. Everything after a `--` is the
# tool's. It NEVER talks to GitHub — a leg that can reach the real board is the bug, not the test for it.
if [ "${1:-}" = "tool" ] && [ "${2:-}" = "run" ]; then
  shift 2
  printf '%s\n' "$*" >>"$ARGV_LOG_PATH"
  shift                                     # the tool name
  for a in "$@"; do
    case "$a" in
      --) break ;;
      -h|-\?|--help) echo "Description:"; echo "  Run a local tool."; exit 0 ;;
    esac
  done
  [ "${1:-}" = "--" ] && shift
  echo "ENGINE RAN: $*"
  exit 0
fi
echo "fake dotnet: unhandled $*" >&2; exit 64
SH
chmod +x "$DOTNETDIR/dotnet"
t4run() { ( cd "$T4" && env -u FSGG_COORD_ENGINE_BIN ARGV_LOG_PATH="$ARGV_LOG" PATH="$DOTNETDIR:$PATH" "$SHIM" "$@" 2>&1 ); }

# a. THE CONTRACT, AS AN ARGV. What the shim hands dotnet must separate the caller's argv from dotnet's own
#    options. This leg models nothing about dotnet — it reads what was handed over.
: >"$ARGV_LOG"; out="$(t4run next --help)"; rc=$?
handed="$(tail -1 "$ARGV_LOG" 2>/dev/null)"
[ "$handed" = "fsgg-coord-engine -- next --help" ] \
  && ok "#1029: tier 4 hands dotnet a '--' separator, so the caller's argv is dotnet's tool args, not dotnet's own" \
  || bad "#1029: the shim must separate the caller's argv from dotnet's options" "handed: $handed"

# b. AND SO THE ENGINE ANSWERS. The same call, against a dotnet that eats what the real one eats.
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN: next --help'; } \
  && ok "#1029: ...so '--help' reaches the ENGINE on a receiver — the pipe is transparent at tier 4" \
  || bad "#1029: --help must reach the engine through tier 4" "rc=$rc out=$out"

# c. THE NEGATIVE, and it is the whole bug: dotnet must not be the thing that answers. Exit 0 is what made
#    this silent — a wrong answer that reported success.
printf '%s' "$out" | grep -q 'Run a local tool' \
  && bad "#1029: dotnet answered the help request — the shim is not a pipe at tier 4" "out: $out" \
  || ok "#1029: ...and dotnet does NOT answer for the engine (no 'Run a local tool', which exited 0 and read as success)"

# d. `-h` IS THE SAME OPTION, and a fix that only separated `--help` would leave it eaten.
: >"$ARGV_LOG"; out="$(t4run -h)"
{ printf '%s' "$out" | grep -q 'ENGINE RAN: -h' && ! printf '%s' "$out" | grep -q 'Run a local tool'; } \
  && ok "#1029: '-h' reaches the engine too — dotnet's short form is separated by the same '--'" \
  || bad "#1029: -h must reach the engine as well" "out: $out"
rm -rf "$T4" "$DOTNETDIR"

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

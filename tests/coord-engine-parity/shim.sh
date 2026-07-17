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
stale "$FIX"
out="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>/dev/null)"; rc=$?
err="$(cd "$FIX" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" next 2>&1 >/dev/null)"
if [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'ENGINE RAN' \
   && printf '%s' "$err" | grep -qi 'stale'; then
  ok "staleness: a stale engine WARNS on a read verb ('next') — and still runs, exit code intact"
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

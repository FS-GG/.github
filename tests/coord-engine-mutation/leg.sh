#!/usr/bin/env bash
# ONE LEG of the `.github#1794` mutation matrix: rebuild the engine, then run every check that claims to
# defend the `Reads.openIssues` fail-open, and report ONE tally over all of them.
#
# WHY A WRAPPER AT ALL. `scripts/lib/mutation.py` grades a leg from a single command's output, and the
# subject here is not one fixture: `#1794`'s guards are defended by unit tests in
# `FS.GG.Coord.GitHub.Tests` (the parse arms), unit tests in `FS.GG.Coord.Cli.Tests` (the collision
# scan's call sites) and the parity corpus over real HTTP (everything that needs a `Link` header, which
# no unit test can reach — `Fake.Recorder` implements `IGitHubTransport` directly, and that is the
# interface `Transport.Send` sits BEHIND). Grading on the parity suite alone would report M4 as ESCAPED
# when it is defended, correctly, by exactly one unit test.
#
# AND A REBUILD. The mutations edit F# source; the parity corpus drives a COMPILED binary. Without the
# build the mutant would be measured against the pristine engine and every leg would report ESCAPED —
# the harness answering a question it had not asked.
#
# THE TALLY LINE IS THIS FILE'S, AND THAT IS THE POINT (.github#1825, specification point 3). Every
# mutation target below is engine source or a fixture SERVER; none of them is this file, so the anchor
# cannot be deleted by the thing it is anchoring. `scripts/lib/mutation.py` refuses a spec that gets this
# wrong at load time and re-hashes the producer either side of the edit at run time.
#
# WHAT THE TALLY COUNTS, STATED PLAINLY BECAUSE IT LOOKS LIKE A COLLAPSE AND IS NOT. A parity leg that
# reports NOT-MEASURED is counted here as a FAILED assertion. Those are two different questions at two
# different levels:
#
#   - inside the parity corpus: "did this assertion pass, fail, or obtain no measurement?" — three
#     valued, printed three ways, and preserved in the log this file writes.
#   - here: "did the gate FIRE on the mutant?" The gate is the whole body of checks, and a corpus that
#     REFUSES TO CERTIFY is a corpus that noticed. `#1794`'s M10 is precisely this: breaking the fixture
#     must make `refute` say NOT MEASURED instead of PASS, and that is the guard firing.
#
# The harness's OWN `NOT_MEASURED` stays reserved for its own question — control not green, mutation did
# not apply, build broke, anchor absent, run crashed — and this file feeds that by printing NO TALLY AT
# ALL when any sub-suite failed to produce its own summary. An unmatched anchor is then the honest
# outcome, rather than a tally invented over a run that did not happen (`#1582`'s eleven import deaths).
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
# OUTSIDE THE REPO BY DEFAULT. This file is appended to on every run, and a sweep runs it twenty times;
# defaulting it into the working tree would leave an untracked file that the next `git status` reads as
# part of the change under measurement.
JOURNAL="${COORD_MUT_JOURNAL:-${TMPDIR:-/tmp}/coord-engine-mutation-journal.log}"

say() { printf '%s\n' "$*"; }
journal() { printf '%s\n' "$*" >>"$JOURNAL"; }

STAMP="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
journal ""
journal "===== RUN $STAMP ====="

# ---- 1. REBUILD ------------------------------------------------------------------------------------
# A mutation that will not COMPILE is not a gate that held. It is no measurement at all, and it says so:
# no tally is printed, so the harness's anchor cannot match and the leg grades NOT_MEASURED (#1812 — a
# subject that errored is not a gate that fired).
BUILD_LOG="$(mktemp)"
if ! dotnet build "$ROOT/src/FS.GG.Coord.Cli" -c Release --nologo >"$BUILD_LOG" 2>&1; then
  say "coord-engine-mutation leg: NOT MEASURED — the mutated tree DID NOT COMPILE, so nothing was run"
  say "--- build output (tail) ---"
  tail -30 "$BUILD_LOG"
  journal "NOT MEASURED: build failed"
  rm -f "$BUILD_LOG"
  exit 3
fi
rm -f "$BUILD_LOG"

upass=0; ufail=0

# ---- 2. THE UNIT LEGS ------------------------------------------------------------------------------
# Both projects that carry `#1794` surface. `FS.GG.Coord.Core` has none — its types are upstream of the
# read — so it is not run, and that is a scoping decision rather than an omission.
for proj in FS.GG.Coord.GitHub.Tests FS.GG.Coord.Cli.Tests; do
  LOG="$(mktemp)"
  dotnet test "$ROOT/tests/$proj" -c Release --nologo >"$LOG" 2>&1
  # THE COUNTS COME FROM THE RUNNER'S OWN SUMMARY, never from the exit code. An exit code cannot tell a
  # failed assertion from a host that died before collecting one, which is the whole of #1812.
  SUMMARY="$(grep -Eo 'Failed: +[0-9]+, Passed: +[0-9]+' "$LOG" | tail -1)"
  if [ -z "$SUMMARY" ]; then
    say "coord-engine-mutation leg: NOT MEASURED — $proj produced no test summary, so it never ran to a verdict"
    say "--- $proj output (tail) ---"
    tail -30 "$LOG"
    journal "NOT MEASURED: $proj produced no summary"
    rm -f "$LOG"
    exit 3
  fi
  f="$(printf '%s' "$SUMMARY" | sed -E 's/Failed: +([0-9]+).*/\1/')"
  p="$(printf '%s' "$SUMMARY" | sed -E 's/.*Passed: +([0-9]+)/\1/')"
  ufail=$((ufail + f)); upass=$((upass + p))
  journal "unit $proj: $p passed, $f failed"
  # Name the checks that fired. "KILLED by 5" is not a verdict a reader can check; five test names are.
  grep -E '^\s*(Failed|\[FAIL\])' "$LOG" | sed 's/^/  unit-FAIL /' | tee -a "$JOURNAL"
  rm -f "$LOG"
done

# ---- 3. THE PARITY CORPUS --------------------------------------------------------------------------
PLOG="$(mktemp)"
bash "$ROOT/tests/coord-engine-parity/run.sh" >"$PLOG" 2>&1
PTALLY="$(grep -Eo 'coord-engine parity: [0-9]+ assertion\(s\), [0-9]+ passed, [0-9]+ failed' "$PLOG" | tail -1)"
PUNMEAS="$(grep -Eo 'coord-engine parity: [0-9]+ not measured' "$PLOG" | tail -1)"
if [ -z "$PTALLY" ] || [ -z "$PUNMEAS" ]; then
  say "coord-engine-mutation leg: NOT MEASURED — the parity corpus printed no terminal tally, so it died mid-run"
  say "--- parity output (tail) ---"
  tail -40 "$PLOG"
  journal "NOT MEASURED: parity printed no tally"
  rm -f "$PLOG"
  exit 3
fi
ppass="$(printf '%s' "$PTALLY" | sed -E 's/.*, ([0-9]+) passed.*/\1/')"
pfail="$(printf '%s' "$PTALLY" | sed -E 's/.*, ([0-9]+) failed.*/\1/')"
punm="$(printf '%s' "$PUNMEAS" | sed -E 's/.*: ([0-9]+) not measured/\1/')"
journal "parity: $ppass passed, $pfail failed, $punm not measured"
grep -E '^(FAIL|NOT-MEASURED) ' "$PLOG" | sed 's/^/  parity-/' | tee -a "$JOURNAL"
rm -f "$PLOG"

# ---- 4. ONE TALLY, AND THE BREAKDOWN THAT KEEPS THE THREE VALUES READABLE ---------------------------
say "coord-engine-mutation leg: unit $upass passed / $ufail failed; parity $ppass passed / $pfail failed / $punm NOT MEASURED"
total_pass=$((upass + ppass))
total_fail=$((ufail + pfail + punm))
say "coord-engine-mutation leg: $total_pass passed, $total_fail failed"
journal "TALLY: $total_pass passed, $total_fail failed"
[ "$total_fail" -eq 0 ] || exit 1
exit 0

#!/usr/bin/env bash
# Fixture for scripts/check-signature-coverage.py — the gate that asserts every compiled F#
# implementation has a signature file, and reports signature files too small to be stating a
# contract (.github#2731, epic #266).
#
# THE GATE HAS TWO LEGS THAT FAIL DIFFERENTLY, so this fixture proves them separately. The EXISTENCE
# leg is an error and must go red; the SUBSTANCE leg is a warning and must go yellow WITHOUT
# reddening. A fixture that only checked exit codes could not tell those apart, and the second one
# would be free to become a no-op without anything noticing.
#
# Most of this file's length is on the failure legs, because a gate that cannot say NO manufactures
# confidence. Every negative leg asserts the REASON, not merely a non-zero exit — the #266
# vacuous-failure defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard
# rather than from the thing under test. `must_fail` therefore takes a required pattern.
#
# AND THE BOUNDARY IS TESTED, NOT JUST THE BRANCH. Reaching the ratio comparison is not the same as
# testing it: legs 9 and 10 sit at exactly 10.0% and at 9.0% over the same implementation, so a
# mutation of `<` to `<=` (or of the constant) is caught. Coverage of a branch is not coverage of
# its predicate.
#
# THE LAST THREE LEGS ARE HARNESS CONTROLS — they check this fixture rather than the gate, after
# .github#2395's L5 leg. A fixture nobody has shown can fail is worth as much as a gate nobody has
# shown can fail.
#
# Throwaway trees under a temp dir. NO NETWORK: the gate reads a checked-out source tree from a path.
# Mirrors tests/source-coherence/run.sh.

set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-signature-coverage.py"
REPO="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/signature-coverage-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
failcount=0
ok() {
  echo "PASS  $1"
  pass=$((pass + 1))
}
bad() {
  echo "FAIL  $1"
  if [ -n "${2:-}" ]; then printf '%s\n' "$2" | sed 's/^/    | /'; fi
  failcount=$((failcount + 1))
}

# gate <root> [extra args...] -- prints combined output, returns the gate's exit code. Assigned in
# two statements so the exit code read is the GATE's and never a pipeline's last stage.
gate() {
  local root="$1"
  shift
  python3 "$GATE" --root "$root" "$@" 2>&1
}

must_pass() { # must_pass <name> <root> [args...]
  local name="$1" root="$2" out rc
  shift 2
  out="$(gate "$root" "$@")" && rc=0 || rc=$?
  if [ "$rc" -ne 0 ]; then bad "$name (expected green, exit $rc)" "$out"; else ok "$name"; fi
}

must_fail() { # must_fail <name> <pattern> <root> [args...]  -- exit 1 AND the right reason
  local name="$1" pat="$2" root="$3" out rc
  shift 3
  out="$(gate "$root" "$@")" && rc=0 || rc=$?
  if [ "$rc" -ne 1 ]; then
    bad "$name (expected exit 1, got $rc)" "$out"
  elif ! grep -qE "$pat" <<<"$out"; then
    bad "$name (red, but for the wrong reason — wanted /$pat/)" "$out"
  else
    ok "$name"
  fi
}

must_no_verdict() { # must_no_verdict <name> <pattern> <root>
  local name="$1" pat="$2" root="$3" out rc
  out="$(gate "$root")" && rc=0 || rc=$?
  if [ "$rc" -ne 3 ]; then
    bad "$name (expected exit 3 NO VERDICT, got $rc)" "$out"
  elif ! grep -qE "$pat" <<<"$out"; then
    bad "$name (exit 3, but for the wrong reason — wanted /$pat/)" "$out"
  else
    ok "$name"
  fi
}

must_warn() { # must_warn <name> <pattern> <root> [args...] -- exit 0 AND the warning present
  local name="$1" pat="$2" root="$3" out rc
  shift 3
  out="$(gate "$root" "$@")" && rc=0 || rc=$?
  if [ "$rc" -ne 0 ]; then
    bad "$name (a substance warning must NOT fail the gate; exit $rc)" "$out"
  elif ! grep -qE "$pat" <<<"$out"; then
    bad "$name (green, but the expected warning /$pat/ was not printed)" "$out"
  else
    ok "$name"
  fi
}

must_not_warn() { # must_not_warn <name> <pattern> <root> [args...]
  local name="$1" pat="$2" root="$3" out rc
  shift 3
  out="$(gate "$root" "$@")" && rc=0 || rc=$?
  if [ "$rc" -ne 0 ]; then
    bad "$name (expected green, exit $rc)" "$out"
  elif grep -qE "$pat" <<<"$out"; then
    bad "$name (warned when it should not have — /$pat/ matched)" "$out"
  else
    ok "$name"
  fi
}

# ---- tree builders ---------------------------------------------------------------------------
#
# The gate's exemption table is REAL and is not stubbed here: these trees create the two exempt
# paths at their true spellings, so every exemption leg exercises the shipped table rather than a
# copy of it that could drift away from it.

entry_points() { # entry_points <root> -- both real exempt paths, both genuine entry points
  local r="$1"
  mkdir -p "$r/src/FS.GG.Coord.Cli" "$r/scripts/NewSddWorkspace"
  printf 'module Program\n[<EntryPoint>]\nlet main _ = 0\n' >"$r/src/FS.GG.Coord.Cli/Program.fs"
  printf 'module P\n[<EntryPoint>]\nlet main _ = 0\n' >"$r/scripts/NewSddWorkspace/Program.fs"
}

body() { # body <n> -- n substantive lines
  local n="$1" i=1
  while [ "$i" -le "$n" ]; do
    echo "let v$i = $i"
    i=$((i + 1))
  done
}

sig_body() { # sig_body <n> -- n substantive signature lines
  local n="$1" i=1
  while [ "$i" -le "$n" ]; do
    echo "val v$i: int"
    i=$((i + 1))
  done
}

pair() { # pair <root> <name> <impl-lines> <sig-lines>
  local r="$1" name="$2"
  mkdir -p "$r/src/Demo"
  body "$3" >"$r/src/Demo/$name.fs"
  sig_body "$4" >"$r/src/Demo/$name.fsi"
}

# ---- 1. the GREEN baseline --------------------------------------------------------------------

G="$WORK/green"
entry_points "$G"
pair "$G" "Alpha" 40 20
must_pass "green: every .fs has a sibling .fsi and both exemptions hold" "$G"

# ---- 2-3. the EXISTENCE leg: the error this gate exists to raise --------------------------------

M="$WORK/missing"
entry_points "$M"
pair "$M" "Alpha" 40 20
mkdir -p "$M/src/Demo"
body 30 >"$M/src/Demo/Orphan.fs"
must_fail "existence: a .fs with no sibling .fsi is a finding" "Orphan\.fs: no sibling \.fsi" "$M"
must_fail "existence: the finding names the remedy file" "Orphan\.fsi" "$M"

# ---- 4-6. the EXEMPTION table validates itself --------------------------------------------------

S="$WORK/stale-exemption"
mkdir -p "$S/src/Demo"
pair "$S" "Alpha" 40 20
must_fail "exemption: a listed path that does not exist is STALE" "STALE EXEMPTION" "$S"

U="$WORK/unjustified"
mkdir -p "$U/src/FS.GG.Coord.Cli" "$U/scripts/NewSddWorkspace"
printf 'module Program\nlet main _ = 0\n' >"$U/src/FS.GG.Coord.Cli/Program.fs" # no [<EntryPoint>]
printf 'module P\n[<EntryPoint>]\nlet main _ = 0\n' >"$U/scripts/NewSddWorkspace/Program.fs"
pair "$U" "Alpha" 40 20
must_fail "exemption: a listed path with no [<EntryPoint>] is UNJUSTIFIED" "UNJUSTIFIED EXEMPTION" "$U"

# A blank reason cannot be reached through a tree, because the reasons live in the gate. Reach it
# the only honest way — patch the shipped table in memory and call the shipped entry point.
B="$WORK/blank-reason"
entry_points "$B"
pair "$B" "Alpha" 40 20
blank_out="$(
  python3 - "$GATE" "$B" <<'PY' 2>&1 || true
import importlib.util, sys
spec = importlib.util.spec_from_file_location("gate", sys.argv[1])
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
mod.EXEMPT = dict.fromkeys(mod.EXEMPT, "   ")
sys.exit(mod.main(["--root", sys.argv[2]]))
PY
)"
if grep -qE "exempt with no reason written" <<<"$blank_out"; then
  ok "exemption: a listed path with a blank reason is a finding"
else
  bad "exemption: a blank reason must be a finding" "$blank_out"
fi

# ---- 7-8. NO VERDICT: could-not-check is neither pass nor fail ----------------------------------

must_no_verdict "no verdict: no subject root exists at all" "NO VERDICT" "$WORK/absent"

E="$WORK/empty-roots"
mkdir -p "$E/src" "$E/scripts"
must_no_verdict "no verdict: subject roots exist but hold no .fs" "found no \.fs file" "$E"

# ---- 9-11. the SUBSTANCE leg, AT ITS BOUNDARY ---------------------------------------------------
#
# One implementation of exactly 100 substantive lines, three signatures over it. The threshold is
# `ratio < 10`, so 10 lines (10.0%) must NOT warn and 9 (9.0%) must. A mutation of `<` to `<=`, or
# of the constant, changes exactly one of these two answers.

ON="$WORK/boundary-on"
entry_points "$ON"
pair "$ON" "Edge" 100 10
must_not_warn "substance: exactly 10.0% does NOT warn (the predicate boundary)" "WARN" "$ON"

OFF="$WORK/boundary-off"
entry_points "$OFF"
pair "$OFF" "Edge" 100 9
must_warn "substance: 9.0% warns" "Edge\.fsi: 9\.0%" "$OFF"

# ...and a warning is ADVICE. It must never redden the gate; that is the whole reason it is a
# warning and the property most likely to be lost in a refactor.
must_warn "substance: a warning exits 0 and does not fail the build" "do not fail this gate" "$OFF"

# ---- 12. SUBSTANTIVE counting is load-bearing, not decorative -----------------------------------
#
# The same 9-line signature padded to 200 raw lines with blanks and `//` commentary. Under a RAW
# line ratio this clears the threshold comfortably; under substantive counting it is still 9.0%.
# This leg is what stops the measure being silently swapped back.

P="$WORK/padded"
entry_points "$P"
pair "$P" "Edge" 100 9
{
  i=1
  while [ "$i" -le 100 ]; do
    echo ""
    echo "// filler commentary line $i"
    i=$((i + 1))
  done
} >>"$P/src/Demo/Edge.fsi"
must_warn "substance: blank and // padding does not clear the threshold" "Edge\.fsi: 9\.0%" "$P"

# `///` is contract and DOES count -- the other half of the same split, and the half that would
# make the gate punish people for writing contracts if it were lost.
D="$WORK/doc-counts"
entry_points "$D"
pair "$D" "Edge" 100 9
{
  i=1
  while [ "$i" -le 20 ]; do
    echo "/// contract prose line $i"
    i=$((i + 1))
  done
} >>"$D/src/Demo/Edge.fsi"
must_not_warn "substance: /// contract lines count toward the ratio" "WARN" "$D"

# ---- 13. obj/ and bin/ are pruned ---------------------------------------------------------------
#
# A built tree carries generated implementations with no signature files. A gate that walked into
# them would be green on a clean checkout and red after a build -- a verdict about the machine.

O="$WORK/built"
entry_points "$O"
pair "$O" "Alpha" 40 20
mkdir -p "$O/src/Demo/obj/Release/net10.0" "$O/src/Demo/bin/Release"
body 5 >"$O/src/Demo/obj/Release/net10.0/Demo.AssemblyInfo.fs"
body 5 >"$O/src/Demo/bin/Release/Leftover.fs"
must_pass "pruning: generated .fs under obj/ and bin/ are not subjects" "$O"

# ---- 14. block comments do not count, and they nest ----------------------------------------------

C="$WORK/block-comments"
entry_points "$C"
pair "$C" "Edge" 100 9
printf '(*\n(* nested *)\nstill inside\n*)\n' >>"$C/src/Demo/Edge.fsi"
must_warn "substance: a nested (* *) block adds no substantive lines" "Edge\.fsi: 9\.0%" "$C"

# ---- 15-16. against the REAL repository, which is the tree that actually matters ------------------
#
# A fixture green on synthetic strings while the shipped tree rots is not reachable from here.

must_pass "repository: the real tree passes the existence leg" "$REPO"

R="$WORK/repo-copy"
mkdir -p "$R"
cp -R "$REPO/src" "$R/src"
cp -R "$REPO/scripts" "$R/scripts"
body 12 >"$R/src/FS.GG.Coord.Core/PlantedOffender.fs"
must_fail "repository: an offender planted in a copy of the real tree is caught" \
  "PlantedOffender\.fs: no sibling \.fsi" "$R"

# ---- 17-19. HARNESS CONTROLS: this fixture, not the gate ------------------------------------------
#
# .github#2395's L5 leg. Three properties, each of which would silently hollow this file out.

# (a) `must_fail` discriminates REASONS, not just exit codes. The missing-.fsi tree is genuinely
#     red -- but red for a reason that is NOT the exemption reasons. If this assertion ever
#     matched, every `must_fail` above would be satisfiable by any red at all.
control_out="$(gate "$M" || true)"
if grep -qE "STALE EXEMPTION|UNJUSTIFIED EXEMPTION" <<<"$control_out"; then
  bad "CONTROL: must_fail's reason matching is not discriminating" "$control_out"
else
  ok "CONTROL: a red tree does not match unrelated failure reasons"
fi

# (b) The gate is not hardwired to red -- proven by a green that is not the trivial empty tree.
#     Leg 1 already asserts this, so the control is that the two disagree on the SAME harness:
#     one tree greens and one reds through identical machinery.
if [ "$(gate "$G" >/dev/null 2>&1 && echo green || echo red)" = "green" ] &&
  [ "$(gate "$M" >/dev/null 2>&1 && echo green || echo red)" = "red" ]; then
  ok "CONTROL: identical machinery greens one tree and reds another"
else
  bad "CONTROL: the harness does not separate the green and red trees"
fi

# (c) THE STRONGEST ONE: would this fixture notice a gate that stopped checking? Neuter the
#     existence leg in a COPY of the shipped gate and confirm the missing-.fsi tree then passes.
#     A fixture that still went red here would be testing something other than the gate.
MUT="$WORK/mutant-gate.py"
sed 's/^    missing = \[p for p in sources.*$/    missing = []/' "$GATE" >"$MUT"
if ! grep -qE '^    missing = \[\]$' "$MUT"; then
  bad "CONTROL: could not build the mutant gate (the anchor line moved)" "$(grep -n 'missing = ' "$GATE")"
elif python3 "$MUT" --root "$M" >/dev/null 2>&1; then
  ok "CONTROL: a gate with its existence leg removed passes the offending tree — the leg is real"
else
  bad "CONTROL: the mutant gate still reds, so leg 2 is not testing the existence check"
fi

# ---- verdict --------------------------------------------------------------------------------

echo
echo "signature-coverage fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
if [ "$failcount" -ne 0 ]; then
  echo "signature-coverage fixture — FAILED"
  exit 1
fi
echo "signature-coverage fixture — OK"

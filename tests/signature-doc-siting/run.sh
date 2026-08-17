#!/usr/bin/env bash
# Fixture for scripts/check-signature-doc-siting.py — the gate that refuses an F# XML documentation
# comment in an implementation file whose sibling `.fsi` makes the compiler discard it (.github#2730,
# epic #266).
#
# THE GATE'S OWN FAILURE MODE IS "PASSES BECAUSE IT FOUND NOTHING TO LOOK AT", so this fixture spends
# most of its length on the legs where the gate must NOT report green: a subject that discovered
# nothing, a baseline it could not read, a count that moved in either direction, and an offender
# planted into a copy of the real tree.
#
# IT ALSO SPENDS LENGTH ON THE OPPOSITE FAILURE — the gate firing on CORRECT code. That is the failure
# that turns a policy gate into one contributors learn to suppress, and it is unexercised by the real
# tree: `src/` today contains no `///` inside a block comment, inside any of the three string forms, or
# spelled with four slashes, so every one of those has to be CONSTRUCTED here. A precaution the corpus
# cannot exercise is a precaution nothing proves.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect was a "must fail" test whose non-zero exit came from a path guard rather than from the thing
# under test. `must_fail` therefore takes a required pattern.
#
# Throwaway trees under a temp dir, plus three legs over the repository's own content: REAL-1 runs the
# gate READ-ONLY against `$REPO` itself, so a fixture that is green while the SHIPPED baseline has
# rotted is not a reachable state; REAL-2/REAL-3 run it against a COPY of `src/`, so planting an
# offender never writes into the tree this fixture was invoked from.
#
# NO NETWORK, no build, no `dotnet`: the gate is a function of committed source.

set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
GATE="$REPO/scripts/check-signature-doc-siting.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/signature-doc-siting-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
failcount=0
ok() {
  echo "PASS  $1"
  pass=$((pass + 1))
}
bad() {
  echo "FAIL  $1"
  [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'
  failcount=$((failcount + 1))
}

# `grep -qE -- "$re" <<<"$haystack"` rather than a pipe into `grep -q`: under `pipefail` a pipeline
# ending in an early-exiting reader can report 141 for a haystack that plainly matches (.github#2668).
matches() { grep -qE -- "$2" <<<"$1"; }

# run_gate <root> [<baseline>] -> sets RC and OUT
run_gate() {
  local root="$1" baseline="${2:-}"
  set +e
  if [ -n "$baseline" ]; then
    OUT="$(python3 "$GATE" --root "$root" --baseline "$baseline" 2>&1)"
  else
    OUT="$(python3 "$GATE" --root "$root" 2>&1)"
  fi
  RC=$?
  set -e
}

# must_exit <name> <expected-rc> <pattern> <root> [<baseline>]
must_exit() {
  local name="$1" want="$2" pat="$3" root="$4" baseline="${5:-}"
  run_gate "$root" "$baseline"
  if [ "$RC" -ne "$want" ]; then
    bad "$name (wanted exit $want, got $RC)" "$OUT"
  elif [ -n "$pat" ] && ! matches "$OUT" "$pat"; then
    bad "$name (exit $want, but for the wrong reason — wanted /$pat/)" "$OUT"
  else
    ok "$name"
  fi
}

# ---- a synthetic tree, built from parts -------------------------------------------------------

# mktree <name> -> echoes the root; creates <root>/src/Proj and <root>/tests/signature-doc-siting
mktree() {
  local root="$WORK/$1"
  mkdir -p "$root/src/Proj" "$root/tests/signature-doc-siting"
  : >"$root/tests/signature-doc-siting/baseline.txt"
  echo "$root"
}

# A pair with a clean implementation: the contract prose is in the signature, where the compiler
# keeps it, and the implementation carries `//` only.
write_clean_pair() {
  local root="$1"
  cat >"$root/src/Proj/Clean.fsi" <<'FSI'
namespace Proj

module Clean =
    /// What the caller may rely on. This is the text the compiler emits.
    val run: int -> int
FSI
  cat >"$root/src/Proj/Clean.fs" <<'FS'
namespace Proj

module Clean =
    // Why THIS implementation and not the obvious one: the accumulator has to be seeded at 1
    // because the caller's contract is multiplicative. Invisible to the gate, and correctly so.
    let run n = n * 1
FS
}

# ---- GREEN: a tree whose implementations carry no doc comment ---------------------------------

GREEN="$(mktree green)"
write_clean_pair "$GREEN"
must_exit "green: an implementation with a sibling .fsi and no doc comment" 0 "OK: every subject file matches" "$GREEN"

# The green run must still SAY what it looked at. A gate that reports a pass without naming its
# subject cannot be distinguished from one whose subject silently emptied.
run_gate "$GREEN"
if matches "$OUT" "discovered: [0-9]+ \.fs file\(s\).*[0-9]+ with a sibling \.fsi"; then
  ok "green run states its discovered subject counts"
else
  bad "green run states its discovered subject counts" "$OUT"
fi

# ---- THE ASSERTION ITSELF: a doc comment in an implementation that has a signature ------------

OFF="$(mktree offender)"
write_clean_pair "$OFF"
cat >"$OFF/src/Proj/Bad.fsi" <<'FSI'
namespace Proj

module Bad =
    val run: int -> int
FSI
cat >"$OFF/src/Proj/Bad.fs" <<'FS'
namespace Proj

module Bad =
    /// This sentence reaches no consumer: the compiler takes `Bad.fsi`'s documentation, not this.
    let run n = n
FS
must_exit "red: a doc comment in an implementation that has a sibling .fsi" 1 "Bad\.fs: 1 XML documentation comment" "$OFF"

# It must name the LINE, so the reader can go to it.
run_gate "$OFF"
if matches "$OUT" "Line\(s\): 4"; then
  ok "the finding names the offending line"
else
  bad "the finding names the offending line" "$OUT"
fi

# ---- THE OPPOSITE FAILURE: the gate must NOT fire on correct code -----------------------------

# 1. No sibling `.fsi` at all. The compiler keeps these, so they are correct where they are.
NOSIB="$(mktree nosibling)"
write_clean_pair "$NOSIB"
cat >"$NOSIB/src/Proj/Alone.fs" <<'FS'
namespace Proj

module Alone =
    /// This one IS emitted — there is no signature file to override it.
    let run n = n
FS
must_exit "green: a .fs with NO sibling .fsi is not a subject" 0 "OK: every subject file matches" "$NOSIB"

# 2. Four slashes. F# lexes `////...` as an ordinary comment, so it is not a doc comment and
#    reporting it would be the gate firing on correct code.
FOUR="$(mktree fourslash)"
write_clean_pair "$FOUR"
cat >"$FOUR/src/Proj/Four.fsi" <<'FSI'
namespace Proj

module Four =
    val run: int -> int
FSI
cat >"$FOUR/src/Proj/Four.fs" <<'FS'
namespace Proj

module Four =
    //// Four slashes are an ordinary comment in F#, not XML documentation.
    ///// So are five.
    let run n = n
FS
must_exit "green: four (and five) slashes are not doc comments" 0 "OK: every subject file matches" "$FOUR"

# 3. `///` inside a block comment, inside all three string forms, and in a nested block comment.
QUOTED="$(mktree quoted)"
write_clean_pair "$QUOTED"
cat >"$QUOTED/src/Proj/Quoted.fsi" <<'FSI'
namespace Proj

module Quoted =
    val url: string
    val sample: string
    val verbatim: string
    val triple: string
    val run: int -> int
FSI
cat >"$QUOTED/src/Proj/Quoted.fs" <<'FS'
namespace Proj

module Quoted =
    // Two ordinary-string cases, and the second one is load-bearing. In the URL the `//` of the
    // scheme comes FIRST, so a scanner with no string tracking at all would stop there and never
    // reach the `///` — the case would pass for the wrong reason. `sample` has no earlier `//`.
    let url = "https://example.invalid///path"
    let sample = "the /// marker, written out"
    let verbatim = @"C:\a///b"
    let triple = """a /// b"""

    (*
       /// Inside a block comment. F# block comments NEST:
       (* /// and this one is nested. *)
    *)
    let run n = n
FS
must_exit "green: /// inside strings and (nested) block comments is not a doc comment" 0 "OK: every subject file matches" "$QUOTED"

# ...and the same tree must go RED the moment a real one is added beside them, or the leg above
# proves only that the gate is asleep.
cat >>"$QUOTED/src/Proj/Quoted.fs" <<'FS'

    /// A real one, beside all of the above.
    let other n = n
FS
must_exit "red: a real doc comment beside those non-doc-comments is still found" 1 "Quoted\.fs: 1 XML documentation comment" "$QUOTED"

# 4. A doc comment that is not line-leading. `src/FS.GG.Coord.Core/Protocol.fs` carries these after
#    a `{` on the same physical line; a `^\s*///` grep misses every one of them.
MIDLINE="$(mktree midline)"
write_clean_pair "$MIDLINE"
cat >"$MIDLINE/src/Proj/Mid.fsi" <<'FSI'
namespace Proj

module Mid =
    type R = { A: int }
FSI
cat >"$MIDLINE/src/Proj/Mid.fs" <<'FS'
namespace Proj

module Mid =
    type R =
        { /// Not line-leading, and still a doc comment.
          A: int }
FS
must_exit "red: a doc comment after code on the same physical line is found" 1 "Mid\.fs: 1 XML documentation comment" "$MIDLINE"

# ---- NO VERDICT: never a pass ------------------------------------------------------------------

EMPTY="$(mktree emptysrc)"
must_exit "no verdict: src/ exists but holds no .fs file" 3 "discovery found no \.fs file" "$EMPTY"

NOSRC="$WORK/nosrc"
mkdir -p "$NOSRC/tests/signature-doc-siting"
: >"$NOSRC/tests/signature-doc-siting/baseline.txt"
must_exit "no verdict: there is no src/ directory at all" 3 "is not a directory" "$NOSRC"

NOSIG="$(mktree nosignatures)"
cat >"$NOSIG/src/Proj/Only.fs" <<'FS'
namespace Proj

module Only =
    let run n = n
FS
must_exit "no verdict: .fs files exist but NOT ONE has a sibling .fsi" 3 "not one has a sibling" "$NOSIG"

must_exit "no verdict: the baseline cannot be read" 3 "cannot read the baseline" "$GREEN" "$WORK/absent-baseline.txt"

printf 'not-a-number src/Proj/Clean.fs\n' >"$WORK/bad-count.txt"
must_exit "no verdict: a baseline count that is not an integer" 3 "count is not an integer" "$GREEN" "$WORK/bad-count.txt"

printf 'lonely-token\n' >"$WORK/bad-shape.txt"
must_exit "no verdict: a baseline line that is not <count> <path>" 3 "not a .<count> <path>. line" "$GREEN" "$WORK/bad-shape.txt"

printf '0 src/Proj/Clean.fs\n' >"$WORK/zero-count.txt"
must_exit "no verdict: a zero baseline count (an entry nobody maintains)" 3 "count must be positive" "$GREEN" "$WORK/zero-count.txt"

printf '1 src/Proj/Clean.fs\n1 src/Proj/Clean.fs\n' >"$WORK/dupe.txt"
must_exit "no verdict: a duplicated baseline entry" 3 "duplicate entry" "$GREEN" "$WORK/dupe.txt"

# ---- THE BASELINE IS EXACT IN BOTH DIRECTIONS -------------------------------------------------

BASE="$(mktree baselined)"
write_clean_pair "$BASE"
cat >"$BASE/src/Proj/Residue.fsi" <<'FSI'
namespace Proj

module Residue =
    val run: int -> int
FSI
cat >"$BASE/src/Proj/Residue.fs" <<'FS'
namespace Proj

module Residue =
    /// One.
    /// Two.
    let run n = n
FS

printf '2 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "green: a baselined residue whose count matches the tree exactly" 0 "OK: every subject file matches" "$BASE"

printf '1 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: MORE doc comments than the baseline allows (a new offender)" 1 "1 new one" "$BASE"

printf '3 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: FEWER doc comments than the baseline claims (a stale baseline)" 1 "STALE BASELINE: write 2" "$BASE"

printf '2 src/Proj/Residue.fs\n1 src/Proj/Gone.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: a baseline line naming a path that is not a subject file" 1 "STALE BASELINE: delete this line" "$BASE"

# A repair that does not decrement the baseline is still red — which is what makes the file shrink
# rather than merely exist.
cat >"$BASE/src/Proj/Residue.fs" <<'FS'
namespace Proj

module Residue =
    // One.
    // Two.
    let run n = n
FS
printf '2 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: a repair landed without its decrement" 1 "STALE BASELINE: delete this line" "$BASE"

printf '' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "green: the repair and its decrement, together" 0 "OK: every subject file matches" "$BASE"

# ---- THE REAL TREE ----------------------------------------------------------------------------
#
# Everything above runs on strings this file wrote. These three legs run on the repository's own
# content, so a fixture that is green while the SHIPPED baseline has rotted cannot exist.

must_exit "REAL-1: the shipped baseline describes the real tree exactly" 0 "OK: every subject file matches" "$REPO"

# REAL-2: plant an offender into a COPY of the real tree, so the gate is exercised against real
# content without this fixture ever writing into the repository it is run from. The subject has to be
# a file .github#2730 actually swept: planting into a baselined one would only move a count.
REAL_COPY="$WORK/real"
mkdir -p "$REAL_COPY/tests/signature-doc-siting"
cp -r "$REPO/src" "$REAL_COPY/src"
find "$REAL_COPY/src" -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} + 2>/dev/null || true
cp "$REPO/tests/signature-doc-siting/baseline.txt" "$REAL_COPY/tests/signature-doc-siting/baseline.txt"

must_exit "REAL-2: the copy of the real tree is green before anything is planted" 0 "OK: every subject file matches" "$REAL_COPY"

printf '\n/// A reintroduced doc comment. The compiler discards it; this gate must not.\n' \
  >>"$REAL_COPY/src/FS.GG.Coord.Core/Landable.fs"
must_exit "REAL-3: a doc comment reintroduced into a swept file reds the gate" 1 "Landable\.fs: 1 XML documentation comment" "$REAL_COPY"

echo
echo "signature-doc-siting fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

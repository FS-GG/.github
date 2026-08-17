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

# 3a. NESTING ITSELF, DISCRIMINATED — AND THE LEG ABOVE DOES NOT DO IT.
#
# `Quoted.fs` above contains a nested block comment, and its leg is named for one. But its inner
# `///` sits BEFORE the inner `*)`, so it is skipped at depth 1 whether or not `(*` nests, and the
# leg cannot tell the two lexers apart. Measured at review: with the `depth += 1` inside
# `doc_comment_lines`' `if depth > 0:` branch neutralised — the one line that makes `(* *)` nest at
# all — the ENTIRE fixture still passed, 34 legs, 0 failures. A precaution nothing can fail on is a
# precaution nothing proves, which is this fixture's own stated subject.
#
# The construct that separates them is a `///` AFTER the inner `*)`. With nesting, that `*)` takes
# the depth from 2 to 1 and the `///` is still inside the OUTER comment, correctly invisible.
# Without it, the same `*)` closes the only comment the lexer is tracking, depth reaches 0 mid-line,
# and the `///` is REPORTED — the false positive this module calls the one failure it must not have.
# Shipped: `([], None)`. Nesting neutralised: `([6], None)`.
NEST="$(mktree nesting)"
write_clean_pair "$NEST"
cat >"$NEST/src/Proj/Nest.fsi" <<'FSI'
namespace Proj

module Nest =
    val run: int -> int
FSI
cat >"$NEST/src/Proj/Nest.fs" <<'FS'
namespace Proj

module Nest =
    (*
       An outer block comment. F# block comments NEST, so the inner one below closes only
       (* itself *) /// and this, which follows that inner close, is still comment text.
    *)
    let run n = n
FS
must_exit "green: a /// AFTER an inner *) is still inside the outer block comment" 0 "OK: every subject file matches" "$NEST"

# ...and the same tree must go RED once the outer comment has genuinely closed, or the leg above
# proves only that the gate stopped looking somewhere in the middle.
cat >>"$NEST/src/Proj/Nest.fs" <<'FS'

    /// A real one, after the outer block comment has closed.
    let other n = n
FS
must_exit "red: a real doc comment after a nested block comment is still found" 1 "Nest\.fs: 1 XML documentation comment" "$NEST"

# 3b. STRINGS SPAN NEWLINES, AND THE LEXER MUST CARRY THAT ACROSS LINES — IN BOTH DIRECTIONS.
#
# An earlier version of the gate reset ordinary- and verbatim-string state at every line, on the
# stated ground that neither "spans a newline in a well-formed F# file". Both halves are false, and
# `dotnet fsi` says so: `@"a\n(* b\nc"` and `"a\n/// b\nc"` each evaluate to a THREE-LINE string.
#
# The cost was not theoretical. Reading `@"..."` a line at a time meant a continuation line holding
# `(*` set the block-comment depth to 1, which silenced every `///` to the end of the file: a
# genuine contract sentence planted in a swept file returned exit 0, `OK: every subject file
# matches`. On a baselined file the same shape printed `STALE BASELINE: delete this line`, and
# following that printed remedy reached exit 0 while `grep -c '///'` still returned 455. Mirrored, a
# `///` on a continuation line INSIDE such a string was reported as a doc comment.
#
# `src/` DOES hold multi-line strings — 320 continuation lines across 4 subject files — but not one
# of them happens to carry a `///` or a `(*`, so the corpus cannot reach either failure. That is why
# both are constructed here. Latent is not harmless: the subject is `src/**` in perpetuity, and the
# recorded `Cli` residue is edited by every extraction lane.

SPAN="$(mktree spanning)"
write_clean_pair "$SPAN"
cat >"$SPAN/src/Proj/Span.fsi" <<'FSI'
namespace Proj

module Span =
    val banner: string
    val ordinary: string
    val run: int -> int
FSI
cat >"$SPAN/src/Proj/Span.fs" <<'FS'
namespace Proj

module Span =
    // The continuation line opens what LOOKS like a block comment but is string content. A lexer
    // that reset string state per line read that `(*` as real and went blind from here on.
    let banner = @"first line
(* second line, still inside the verbatim string
   third line"

    // The same hazard in an ordinary string literal, which also spans newlines.
    let ordinary = "first line
(* second line, still inside the ordinary string
   third line"

    /// A REAL doc comment, after both strings have closed. Being found is the whole assertion.
    let run n = n
FS
must_exit "red: a multi-line string does NOT silence a doc comment that follows it" 1 "Span\.fs: 1 XML documentation comment" "$SPAN"

# ...and it must be the RIGHT line, not an accidental hit inside one of the strings.
run_gate "$SPAN"
if matches "$OUT" "Line\(s\): 15$"; then
  ok "the doc comment after a multi-line string is found at its own line"
else
  bad "the doc comment after a multi-line string is found at its own line" "$OUT"
fi

# The mirror: a `///` on a continuation line INSIDE a multi-line string is string content, and
# reporting it is the false positive this gate must not have.
INSIDE="$(mktree inside)"
write_clean_pair "$INSIDE"
cat >"$INSIDE/src/Proj/Inside.fsi" <<'FSI'
namespace Proj

module Inside =
    val verbatim: string
    val ordinary: string
    val triple: string
    val run: int -> int
FSI
cat >"$INSIDE/src/Proj/Inside.fs" <<'FS'
namespace Proj

module Inside =
    let verbatim = @"first line
/// this is string content, not a doc comment
last line"

    let ordinary = "first line
/// this is string content too
last line"

    let triple = """first line
/// and so is this
last line"""

    let run n = n
FS
must_exit "green: a /// on a continuation line INSIDE a multi-line string is not a doc comment" 0 "OK: every subject file matches" "$INSIDE"

# ...and the same tree must go RED the moment a real one is added after them, or the leg above
# proves only that the gate went blind at the first string and never recovered.
cat >>"$INSIDE/src/Proj/Inside.fs" <<'FS'

    /// A real one, after all three multi-line strings.
    let other n = n
FS
must_exit "red: a real doc comment after those multi-line strings is still found" 1 "Inside\.fs: 1 XML documentation comment" "$INSIDE"

# 3c. `'"'` IS A CHARACTER LITERAL, NOT A STRING OPENER — and unlike everything else in this
# section it is LIVE: `src/FS.GG.Coord.Core/RegistryPredicate.fs:40` and `SemanticDiff.fs:103` both
# carry one today. Once string state carries across lines, misreading that quote as an opener puts
# the pass in the wrong state for the rest of the file. `'T` and `xs'` must NOT be eaten in the
# attempt — `src/` holds both shapes in quantity: at code level, 106 character literals against 40
# `'` that are not one, over the 66 `.fs` files under `src/`. (An earlier version of this line said
# "1,933 of those against 29 character literals"; neither figure survived re-derivation at review,
# and `char_literal_end`'s docstring records the method and what each one was actually counting.)
CHARLIT="$(mktree charliteral)"
write_clean_pair "$CHARLIT"
cat >"$CHARLIT/src/Proj/Chars.fsi" <<'FSI'
namespace Proj

module Chars =
    val classify: char -> int
    val identity: 'a -> 'a
    val run: int -> int
FSI
cat >"$CHARLIT/src/Proj/Chars.fs" <<'FS'
namespace Proj

module Chars =
    let classify (c: char) =
        match c with
        | '"' -> 1
        | '\'' -> 2
        | '\\' -> 3
        | '\n' -> 4
        | _ -> 0

    // A generic type parameter and a primed identifier: neither is a character literal, and eating
    // either as one would consume the code around it.
    let identity (x: 'a) : 'a = x
    let run' n = n

    /// A REAL doc comment after every one of those. Being found is the assertion.
    let run n = run' n
FS
must_exit "red: a doc comment after a '\"' character literal is still found" 1 "Chars\.fs: 1 XML documentation comment" "$CHARLIT"

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

# A SUBJECT FILE THAT CANNOT BE READ. Until .github#2730's review this arm shipped in three places
# and was asserted by no leg at all: stubbing `if unreadable:` to `if False:` left the whole fixture
# green, 0 failures. A surviving inversion is material by definition, and a NO-VERDICT arm nothing
# asserts is #266's own shape inside the change that serves #266.
UNREADABLE="$(mktree unreadable)"
write_clean_pair "$UNREADABLE"
cat >"$UNREADABLE/src/Proj/Undecodable.fsi" <<'FSI'
namespace Proj

module Undecodable =
    val run: int -> int
FSI
# Not valid UTF-8 in any position, so `open(..., encoding="utf-8").read()` raises.
printf 'namespace Proj\n\xff\xfe\x00garbage\n' >"$UNREADABLE/src/Proj/Undecodable.fs"
must_exit "no verdict: a subject file cannot be decoded" 3 "could not be read" "$UNREADABLE"

# A SUBJECT FILE THAT CANNOT BE LEXED. Carrying string state across lines is what makes this gate
# see a multi-line string at all, and it restores the original author's real fear: a construct
# opened and never closed leaves the pass in the wrong state for everything after it. The answer is
# to REFUSE rather than to reset — a count taken after a mis-parse is worse than no count, because
# it looks like one.
UNLEXABLE="$(mktree unlexable)"
write_clean_pair "$UNLEXABLE"
cat >"$UNLEXABLE/src/Proj/Unclosed.fsi" <<'FSI'
namespace Proj

module Unclosed =
    val banner: string
FSI
cat >"$UNLEXABLE/src/Proj/Unclosed.fs" <<'FS'
namespace Proj

module Unclosed =
    let banner = @"this verbatim string is never closed
    /// and so this line cannot be classified either way
FS
must_exit "no verdict: a subject file ends inside an unterminated string" 3 "could not be lexed" "$UNLEXABLE"

UNCLOSEDCOMMENT="$(mktree unclosedcomment)"
write_clean_pair "$UNCLOSEDCOMMENT"
cat >"$UNCLOSEDCOMMENT/src/Proj/Dangling.fsi" <<'FSI'
namespace Proj

module Dangling =
    val run: int -> int
FSI
cat >"$UNCLOSEDCOMMENT/src/Proj/Dangling.fs" <<'FS'
namespace Proj

module Dangling =
    (* this block comment is never closed
    let run n = n
FS
must_exit "no verdict: a subject file ends inside an unclosed block comment" 3 "could not be lexed" "$UNCLOSEDCOMMENT"

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

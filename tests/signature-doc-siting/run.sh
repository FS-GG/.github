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
# The gate orders projects with Python's `sorted`, which is codepoint order. `sort` must agree, or
# the independent derivation below would disagree with a correct gate for a reason that is about
# neither of them.
export LC_ALL=C

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

# must_line <name> <exact-line> <root> [<baseline>] — the gate's output must contain this line
# BYTE FOR BYTE. A regex over a printed count is what let .github#2730's M5 through: the leg written
# for exactly the "a silently emptied subject must be visible" property asserted
# `discovered: [0-9]+ \.fs file\(s\)`, so pinning the count to a literal `999` satisfied it, and so
# did dropping a whole project. A count is asserted by its VALUE or it is not asserted.
must_line() {
  local name="$1" want="$2" root="$3" baseline="${4:-}"
  run_gate "$root" "$baseline"
  if grep -qxF -- "$want" <<<"$OUT"; then
    ok "$name"
  else
    bad "$name (no line equal to: $want)" "$OUT"
  fi
}

# must_text <name> <exact-text> <root> [<baseline>] — the gate's output must CONTAIN this text byte
# for byte, as a FIXED STRING (`grep -F`) rather than a pattern.
#
# `must_line` is the stronger form and stays the default. This one exists for the two lines whose
# tail is the OPERATING SYSTEM's own words — an `OSError` or a `UnicodeDecodeError` message — which
# this fixture must not pin, because the assertion would then be about the C library and the Python
# version rather than about the gate. Everything the GATE contributes to those lines is still
# asserted by value: its sentence, the path it names, its `: ` separator and its two-space indent.
# Those separators and indents were what nothing asserted (.github#2730 repair phase): the sweep's
# `str` operator could not reach them at all until it stopped skipping literals with no alphanumeric
# character in them, and the first sweep that could reach them found five survivors here.
must_text() {
  local name="$1" want="$2" root="$3" baseline="${4:-}"
  run_gate "$root" "$baseline"
  if grep -qF -- "$want" <<<"$OUT"; then
    ok "$name"
  else
    bad "$name (no text equal to: $want)" "$OUT"
  fi
}

# THE EXPECTED VALUES ARE DERIVED INDEPENDENTLY, WITH `find` AND THE SHELL — never by asking the
# gate. A leg that checks the gate's count against the gate's own idea of the count asserts nothing,
# and this row has already shipped one assertion that could not fail. These two functions are a
# second implementation of `discover()`, and they are deliberately written in a different language
# from it, so a defect would have to occur twice, in two syntaxes, to stay invisible.

# subject_files <root> — every .fs under <root>/src, outside obj|bin, that has a sibling .fsi
subject_files() {
  local f
  # An explicit `if`, not `[ -f … ] && printf`: under `set -e` the AND-list is the last command in
  # the loop body, and whether a false test then ends the loop is a subtlety no reader should have
  # to adjudicate. Measured either way here (the loop does continue, and `src/` has 4 non-subjects
  # of which the first sorts near the front), but a fixture that truncates its own corpus silently
  # is this row's subject, so the construct is written so it cannot.
  while IFS= read -r f; do
    if [ -f "${f}i" ]; then printf '%s\n' "$f"; fi
  done < <(find "$1/src" -name '*.fs' -not -path '*/obj/*' -not -path '*/bin/*' | sort)
}

# expected_breakdown <root> — "subjects by project: <dir> <n>, ..." as the gate must print it
expected_breakdown() {
  local root="$1" body
  body="$(subject_files "$root" \
    | sed "s|^$root/src/||" \
    | awk -F/ 'NF>1 {print $1; next} {print "(root)"}' \
    | sort | uniq -c | sort -k2,2 \
    | awk '{printf "%s%s %s", (NR>1 ? ", " : ""), $2, $1} END {print ""}')"
  if [ -z "$body" ]; then
    echo "subjects by project: (none)"
  else
    echo "subjects by project: $body"
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

# AND THE `--root` DEFAULT ITSELF, WHICH EVERY OTHER LEG IN THIS FILE ROUTES AROUND. `run_gate`
# always passes `--root`, and so does production (`signature-doc-siting.yml:132` passes `--root .`),
# so `argparse`'s default was a constant no leg could reach: mutating it survived the whole mutation
# sweep. It is not dead code, though — a contributor debugging this gate runs it with no arguments
# from the repository root, and that is the path exercised here.
set +e
NOFLAG_OUT="$(cd "$GREEN" && python3 "$GATE" 2>&1)"
NOFLAG_RC=$?
set -e
if [ "$NOFLAG_RC" -eq 0 ] && grep -qxF -- "OK: every subject file matches its baseline entry exactly." <<<"$NOFLAG_OUT"; then
  ok "green: with NO --root at all, the default resolves to the working directory"
else
  bad "green: with NO --root at all, the default resolves to the working directory (exit $NOFLAG_RC)" "$NOFLAG_OUT"
fi

# The green run must still SAY what it looked at. A gate that reports a pass without naming its
# subject cannot be distinguished from one whose subject silently emptied.
#
# THIS LEG USED TO ASSERT A REGEX, AND THAT IS THE FINDING THAT EXHAUSTED THIS ROW'S ORDINARY CHAIN.
# It read `discovered: [0-9]+ \.fs file\(s\).*[0-9]+ with a sibling \.fsi` — written for exactly the
# property named above, and satisfied by any number at all. Measured at round 3: pinning the printed
# subject count to the literal `999` left this leg green, and so did narrowing `discover()` to drop a
# whole project. So every count below is asserted BY VALUE, and the values come from `find`.
must_line "green run states its discovered counts by value, not by regex" \
  "discovered: 1 .fs file(s) under $GREEN/src, 1 with a sibling .fsi, 0 carrying 0 XML documentation comment line(s); baseline records 0 file(s), 0 line(s)" \
  "$GREEN"
must_line "green run names its subject population per project" \
  "$(expected_breakdown "$GREEN")" "$GREEN"

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
# The pattern pins the NO-BASELINE-ENTRY branch's own sentence, not just `<file>: N XML
# documentation comment`. The gate has four finding shapes and two of them open with those same
# words, so the shared prefix cannot tell them apart: deleting this branch outright (`if allowed ==
# 0:` -> `if False:`) lets the `len(seen) > allowed` branch catch the same file and emit `baseline
# allows 0 -- 1 new one(s)`, and the prefix-only assertion this leg used to carry stayed green at 36
# passed, 0 failed. That was the same defect as the unlexable arms below, in the finding path.
must_exit "red: a doc comment in an implementation that has a sibling .fsi" 1 \
  "Bad\.fs: 1 XML documentation comment\(s\) in an implementation file that has a sibling \.fsi, and no baseline entry" "$OFF"

# It must name the LINE, so the reader can go to it.
run_gate "$OFF"
if matches "$OUT" "Line\(s\): 4$"; then
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
# `src/` DOES hold multi-line strings — 323 continuation lines across the 5 SUBJECT files that have
# them (320 across 4 at `0ddd4b88`, before `.github#2724` gave `Cli/Client.fs` a sibling `.fsi` and
# moved its 3 into the subject population) — but not one
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
must_exit "no verdict: src/ exists but holds no .fs file" 3 \
  "NO VERDICT: discovery found no \.fs file under .*/src\." "$EMPTY"

NOSRC="$WORK/nosrc"
mkdir -p "$NOSRC/tests/signature-doc-siting"
: >"$NOSRC/tests/signature-doc-siting/baseline.txt"
must_exit "no verdict: there is no src/ directory at all" 3 "NO VERDICT: .* is not a directory -- there is no subject to scan" "$NOSRC"

NOSIG="$(mktree nosignatures)"
cat >"$NOSIG/src/Proj/Only.fs" <<'FS'
namespace Proj

module Only =
    let run n = n
FS
must_exit "no verdict: .fs files exist but NOT ONE has a sibling .fsi" 3 \
  "NO VERDICT: 1 \\.fs file\\(s\\) under .*, but not one has a sibling \\.fsi" "$NOSIG"
# ...and the whole line, byte for byte. The pattern above spends a `.*` exactly where the scanned
# ROOT is printed, so anything at all could stand there — measured: replacing that root with the
# subject list, or with a function object, satisfied it. A `.*` in an assertion is a slot the
# assertion has given up on, which is this row's entire subject.
must_line "the no-subject refusal names the ROOT it scanned, not merely something" \
  "NO VERDICT: 1 .fs file(s) under $NOSIG/src, but not one has a sibling .fsi. Scanning nothing is a failure to scan, not a clean tree." \
  "$NOSIG"

must_exit "no verdict: the baseline cannot be read" 3 "NO VERDICT: cannot read the baseline .*absent-baseline" "$GREEN" "$WORK/absent-baseline.txt"

# ...and it says WHICH file and WHY, with a separator between them. The leg above spends a `.*`
# across both, so the sentence, the path and the OS reason could run together into one word and it
# would still pass. Everything up to and including the `: ` is the gate's own text; what follows is
# the operating system's, and pinning that would be an assertion about the C library.
must_text "the baseline-unreadable refusal separates the path it names from the reason" \
  "NO VERDICT: cannot read the baseline $WORK/absent-baseline.txt: " "$GREEN" "$WORK/absent-baseline.txt"

# EVERY NO-VERDICT PATH THAT PRINTS A POPULATION PRINTS THE WHOLE OF IT. `main` reports its discovered
# counts on the baseline-unreadable path as well as on the green one, and a refusal that misdescribes
# what it was looking at sends the reader to repair the wrong thing.
# NOSIB, not GREEN: it holds 2 sources and 1 subject, so the two count slots hold DIFFERENT numbers.
# On GREEN both are 1, and two adjacent slots that are always equal are two slots no leg can tell
# apart — measured: swapping `len(sources)` and `len(subjects)` here left the fixture fully green.
must_line "the baseline-unreadable refusal states its counts by value, and not each other's" \
  "discovered: 2 .fs file(s) under $NOSIB/src, 1 with a sibling .fsi" "$NOSIB" "$WORK/absent-baseline.txt"
must_line "the baseline-unreadable refusal names its population per project" \
  "$(expected_breakdown "$NOSIB")" "$NOSIB" "$WORK/absent-baseline.txt"

# ...and the refusal for a tree with NO subject at all says so in the same words, rather than
# omitting the line and leaving `(none)` to be inferred from its absence.
must_line "the no-subject refusal names an EMPTY population explicitly" \
  "subjects by project: (none)" "$NOSIG"

# ...AND THE SAME EMPTY POPULATION THROUGH THE OTHER DOOR. The leg above reaches the `not subjects`
# refusal, which names the empty case directly. The BASELINE-unreadable refusal is checked FIRST, so
# on a subject-less tree it is the only path that reaches `subject_breakdown` with nothing to count —
# and that function's own empty-case branch is reachable from nowhere else. Neutralising it there
# yields a bare `subjects by project: ` with a trailing space, which is a gate reporting an empty
# subject set as though it had simply forgotten to say. Found by CI at a parallelism this fixture's
# author was not running; see the race note in `mutants.py`.
must_line "an empty population is named through the BASELINE refusal too, not just the subject one" \
  "subjects by project: (none)" "$NOSIG" "$WORK/absent-baseline.txt"
must_line "and an empty population is what the independent walk finds too" \
  "$(expected_breakdown "$NOSIG")" "$NOSIG"

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
must_exit "no verdict: a subject file cannot be decoded" 3 "NO VERDICT: a subject file could not be read, so the scan is incomplete:" "$UNREADABLE"
# ...and the ENTRY under that sentence names the file, indented, with a separator before the reason.
# The leg above asserts only the heading, so the listing beneath it could have lost its indent or run
# the path into the decoder's message and nothing would have noticed. Both were measured surviving
# the sweep before this leg existed.
must_text "the unreadable listing indents its entry and separates the path from the reason" \
  "  $UNREADABLE/src/Proj/Undecodable.fs: " "$UNREADABLE"

# A SUBJECT FILE THAT CANNOT BE LEXED. Carrying string state across lines is what makes this gate
# see a multi-line string at all, and it restores the original author's real fear: a construct
# opened and never closed leaves the pass in the wrong state for everything after it. The answer is
# to REFUSE rather than to reset — a count taken after a mis-parse is worse than no count, because
# it looks like one.
#
# `doc_comment_lines` REFUSES THROUGH FOUR DISTINCT RETURNS, AND EACH GETS ITS OWN LEG ASSERTING ITS
# OWN WORDS. Until .github#2730's round-3 review this section held two legs for the four arms, and
# both asserted the CALLER's generic prefix `could not be lexed` rather than the arm's own sentence.
# That cannot discriminate the four even in principle, and it was measured failing in both of the
# ways a shared assertion fails:
#
#   * CONDITION-BLIND. `if in_triple:` -> `if False:` at the triple arm, and `if in_str:` -> `if
#     False:` at the ordinary-string arm, each left the whole fixture at 36 passed, 0 failed — the
#     two arms had no leg at all. A real subject file ending inside `"""...` or `"...` with a
#     genuine `///` after the opening quote then shipped exit 0, `OK: every subject file matches its
#     baseline entry exactly`, against exit 3 unmutated. That is a mis-parse presenting as green,
#     which this module names the one failure it must not have.
#   * MESSAGE-BLIND. The verbatim and block-comment arms DID red on a condition mutation (35/1
#     each), yet swapping their two message strings — each arm still refusing, but reporting the
#     other's words — also left 36 passed, 0 failed. An arm asserted only by a shared prefix is an
#     arm whose identity nothing checks.
#
# So each leg below pins the arm's own discriminating sentence, and the depth-counting arm gets a
# second leg at a different depth, because `{depth} unclosed block comment(s)` is a COUNT and a leg
# that never varies it cannot tell 1 from 2. Mutate any one of the four conditions to `if False:`,
# or swap any two of the four messages, and exactly the corresponding leg(s) red.

UNLEXTRIPLE="$(mktree unlextriple)"
write_clean_pair "$UNLEXTRIPLE"
cat >"$UNLEXTRIPLE/src/Proj/Triple.fsi" <<'FSI'
namespace Proj

module Triple =
    val banner: string
FSI
cat >"$UNLEXTRIPLE/src/Proj/Triple.fs" <<'FS'
namespace Proj

module Triple =
    let banner = """this triple-quoted string is never closed
    /// and so this line cannot be classified either way
FS
must_exit "no verdict: a subject file ends inside an unterminated TRIPLE-QUOTED string" 3 \
  "reached end of file inside a triple-quoted string" "$UNLEXTRIPLE"

UNLEXSTR="$(mktree unlexstr)"
write_clean_pair "$UNLEXSTR"
cat >"$UNLEXSTR/src/Proj/Ordinary.fsi" <<'FSI'
namespace Proj

module Ordinary =
    val banner: string
FSI
cat >"$UNLEXSTR/src/Proj/Ordinary.fs" <<'FS'
namespace Proj

module Ordinary =
    let banner = "this ordinary string is never closed
    /// and so this line cannot be classified either way
FS
must_exit "no verdict: a subject file ends inside an unterminated ORDINARY string" 3 \
  "reached end of file inside a string literal" "$UNLEXSTR"

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
must_exit "no verdict: a subject file ends inside an unterminated VERBATIM string" 3 \
  "reached end of file inside a verbatim @\"\.\.\.\" string" "$UNLEXABLE"

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
must_exit "no verdict: a subject file ends inside ONE unclosed block comment" 3 \
  "reached end of file inside 1 unclosed block comment\(s\)" "$UNCLOSEDCOMMENT"

# THE FOUR LEGS ABOVE NOW ASSERT THE ARM'S OWN SENTENCE, WHICH LEFT THE CALLER'S UNASSERTED. Round 3
# repaired a shared-prefix defect by moving every assertion onto the arm, and the sentence the READER
# sees first — the one naming what went wrong and why a count from it cannot be trusted — then had no
# leg at all. Replacing it wholesale left the fixture green.
must_line "the lexer refusal states the caller's reason, not only the arm's words" \
  "NO VERDICT: a subject file could not be lexed, so its count cannot be trusted:" "$UNCLOSEDCOMMENT"

# ...AND THE ENTRY BENEATH IT, WHOLE. Every leg above asserts either the heading or the arm's own
# sentence; not one asserts the line that joins them, so the indent and the `: ` between the path and
# the reason were unasserted in both listings. The FINDINGS listing's identical indent IS asserted
# (`the green run states its observed and baseline totals by value` reaches it), which is what makes
# this a gap rather than a convention: the same two characters were pinned in one place and free in
# two others. Here the whole line is the gate's own text — the arm supplies the reason — so this one
# is `must_line` rather than `must_text`.
must_line "the unlexable listing indents its entry and separates the path from the arm's reason" \
  "  $UNCLOSEDCOMMENT/src/Proj/Dangling.fs: reached end of file inside 1 unclosed block comment(s)" \
  "$UNCLOSEDCOMMENT"

# The depth is a COUNT, and the leg above holds it at 1 forever. `(*` NESTS in F#, so a file may end
# two deep, and a refusal that says `1` when the truth is `2` is a refusal that lost the very state
# it is refusing on behalf of.
NESTEDUNCLOSED="$(mktree nestedunclosed)"
write_clean_pair "$NESTEDUNCLOSED"
cat >"$NESTEDUNCLOSED/src/Proj/TwoDeep.fsi" <<'FSI'
namespace Proj

module TwoDeep =
    val run: int -> int
FSI
cat >"$NESTEDUNCLOSED/src/Proj/TwoDeep.fs" <<'FS'
namespace Proj

module TwoDeep =
    (* outer, never closed
       (* inner, never closed either
    let run n = n
FS
must_exit "no verdict: a subject file ends TWO unclosed block comments deep" 3 \
  "reached end of file inside 2 unclosed block comment\(s\)" "$NESTEDUNCLOSED"

printf 'not-a-number src/Proj/Clean.fs\n' >"$WORK/bad-count.txt"
must_exit "no verdict: a baseline count that is not an integer" 3 "NO VERDICT: .*:1: count is not an integer: .not-a-number." "$GREEN" "$WORK/bad-count.txt"

printf 'lonely-token\n' >"$WORK/bad-shape.txt"
must_exit "no verdict: a baseline line that is not <count> <path>" 3 "NO VERDICT: .*:1: not a .<count> <path>. line: .lonely-token." "$GREEN" "$WORK/bad-shape.txt"

printf '0 src/Proj/Clean.fs\n' >"$WORK/zero-count.txt"
must_exit "no verdict: a zero baseline count (an entry nobody maintains)" 3 "NO VERDICT: .*:1: count must be positive" "$GREEN" "$WORK/zero-count.txt"

printf '1 src/Proj/Clean.fs\n1 src/Proj/Clean.fs\n' >"$WORK/dupe.txt"
must_exit "no verdict: a duplicated baseline entry" 3 "NO VERDICT: .*:2: duplicate entry for src/Proj/Clean\\.fs" "$GREEN" "$WORK/dupe.txt"

# THE BASELINE PARSER REPORTS A LINE NUMBER, AND EVERY LEG ABOVE PUT THE BAD LINE ON LINE 1 — where
# a number that counts from the wrong place is indistinguishable from one that counts correctly.
# A real baseline opens with 40 lines of comment, so line 1 is the one position the defect can never
# be at.
printf '# a comment\n\n1 src/Proj/Clean.fs\nlonely-token\n' >"$WORK/late-bad.txt"
must_exit "no verdict: the baseline error names the line the bad entry is ON" 3 \
  "NO VERDICT: .*:4: not a .<count> <path>. line: .lonely-token." "$GREEN" "$WORK/late-bad.txt"

# ...and it quotes the OFFENDING TOKEN, not some other field of the same line. Both are on the line,
# so a leg that asserts only the sentence cannot tell which one the gate actually printed.
printf 'not-a-number src/Proj/Clean.fs\n' >"$WORK/bad-count2.txt"
must_exit "no verdict: the integer error quotes the COUNT, not the path" 3 \
  "count is not an integer: .not-a-number.$" "$GREEN" "$WORK/bad-count2.txt"

# A PATH MAY CONTAIN A SPACE, and the split that separates count from path is bounded so that it
# can. Nothing in `src/` has one today, so only a constructed subject reaches this.
SPACED="$(mktree spacedpath)"
cat >"$SPACED/src/Proj/With Space.fsi" <<'FSI'
namespace Proj

module Spaced =
    val run: int -> int
FSI
cat >"$SPACED/src/Proj/With Space.fs" <<'FS'
namespace Proj

module Spaced =
    /// One.
    /// Two.
    let run n = n
FS
printf '2 src/Proj/With Space.fs\n' >"$SPACED/tests/signature-doc-siting/baseline.txt"
must_exit "green: a baselined path containing a space is one path, not two fields" 0 \
  "OK: every subject file matches" "$SPACED"

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

# The green line above is the ONLY place the observed-hit and baseline totals are ever non-zero, and
# until now nothing asserted either. `1 carrying 2` and `records 1 file(s), 2 line(s)` are four
# independent counts; a regex over them is satisfied by any four numbers at all.
must_line "the green run states its observed and baseline totals by value" \
  "discovered: 2 .fs file(s) under $BASE/src, 2 with a sibling .fsi, 1 carrying 2 XML documentation comment line(s); baseline records 1 file(s), 2 line(s)" \
  "$BASE"

printf '1 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: MORE doc comments than the baseline allows (a new offender)" 1 "Residue\\.fs: 2 XML documentation comment\\(s\\), baseline allows 1 -- 1 new one\\(s\\)\\. Line\\(s\\): 4, 5" "$BASE"

printf '3 src/Proj/Residue.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: FEWER doc comments than the baseline claims (a stale baseline)" 1 "Residue\\.fs: 2 XML documentation comment\\(s\\), baseline claims 3\\. STALE BASELINE: write 2\\." "$BASE"

printf '2 src/Proj/Residue.fs\n1 src/Proj/Gone.fs\n' >"$BASE/tests/signature-doc-siting/baseline.txt"
must_exit "red: a baseline line naming a path that is not a subject file" 1 \
  "Gone\\.fs: baseline claims 1 XML documentation comment\\(s\\) and the tree has none\\. STALE BASELINE: delete this line\\." "$BASE"

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

# ---- THE SUBJECT POPULATION, PER PROJECT AND NOT IN AGGREGATE ---------------------------------
#
# THIS IS THE FINDING THAT EXHAUSTED THIS ROW'S ORDINARY CHAIN, AND IT IS NOT "ONE MORE PROJECT".
# The gate's aggregate `discovered:` count is exactly what a gate whose subject silently emptied
# would also print, and the exact-match baseline check cannot miss a file it never looked at,
# because a project holding no baseline entry contributes nothing to compare. Measured at round 3:
# narrowing `discover()` to drop `src/FS.GG.Coord.GitHub` left the gate green over 49 of 62
# subjects with the fixture at 39 passed, 0 failed.
#
# So the population is asserted the way it is CONSTITUTED — per project — and against a count this
# file derives itself with `find`, never against a regex and never against the gate's own arithmetic.

MULTI="$(mktree multiproject)"
mkdir -p "$MULTI/src/Alpha" "$MULTI/src/Beta/obj" "$MULTI/src/Alpha/bin"
for pair in Alpha/One Beta/Two Beta/Three; do
  cat >"$MULTI/src/$pair.fsi" <<'FSI'
namespace Proj

module M =
    val run: int -> int
FSI
  cat >"$MULTI/src/$pair.fs" <<'FS'
namespace Proj

module M =
    // Implementation prose, invisible to the gate.
    let run n = n
FS
done
rmdir "$MULTI/src/Proj"

# BUILD OUTPUT IS NOT SOURCE. `obj/` and `bin/` are pruned, and nothing asserted that: no tree above
# has ever held either, and REAL-2 deletes them from its copy before the gate ever runs. A generated
# file carries doc comments the compiler discards just as a hand-written one does, so if pruning
# broke, the gate would report findings nobody can act on — and the first repair anyone reaches for
# is to widen the baseline, which is how a gate stops being believed.
for junk in Beta/obj/Generated Alpha/bin/Generated; do
  cat >"$MULTI/src/$junk.fsi" <<'FSI'
namespace Gen

module G =
    val run: int -> int
FSI
  cat >"$MULTI/src/$junk.fs" <<'FS'
namespace Gen

module G =
    /// Generated, and under a build-output directory the gate must never walk into.
    let run n = n
FS
done

must_exit "green: build output under obj/ and bin/ is not source" 0 "OK: every subject file matches" "$MULTI"
must_line "the discovered counts exclude obj/ and bin/, by value" \
  "discovered: 3 .fs file(s) under $MULTI/src, 3 with a sibling .fsi, 0 carrying 0 XML documentation comment line(s); baseline records 0 file(s), 0 line(s)" \
  "$MULTI"
must_line "the population is named PER PROJECT, so one project cannot empty in silence" \
  "subjects by project: Alpha 1, Beta 2" "$MULTI"
# ...and that expected line is the one an INDEPENDENT walk of the same tree produces. If the two
# ever disagree, the leg above is asserting the gate against a copy of the gate's own mistake.
must_line "the per-project population agrees with an independent find(1) walk" \
  "$(expected_breakdown "$MULTI")" "$MULTI"

# A SUBJECT DIRECTLY UNDER `src/`, IN NO PROJECT AT ALL. Every path in `src/` today has a project
# component, so the branch that names such a file has never been reached — and a breakdown that
# silently attributed a loose file to a project called `Loose.fs` would read as a real project.
ROOTLEVEL="$(mktree rootlevel)"
rmdir "$ROOTLEVEL/src/Proj"
mkdir -p "$ROOTLEVEL/src/Alpha"
for pair in Loose Alpha/One; do
  cat >"$ROOTLEVEL/src/$pair.fsi" <<'FSI'
namespace Proj

module M =
    val run: int -> int
FSI
  cat >"$ROOTLEVEL/src/$pair.fs" <<'FS'
namespace Proj

module M =
    let run n = n
FS
done
must_line "a subject in no project at all is named, not attributed to one" \
  "subjects by project: (root) 1, Alpha 1" "$ROOTLEVEL"
must_line "and the independent walk names it the same way" \
  "$(expected_breakdown "$ROOTLEVEL")" "$ROOTLEVEL"

# Dropping ONE project's signature file must move exactly that project's number, and no other.
rm "$MULTI/src/Beta/Three.fsi"
must_line "removing one project's sibling .fsi moves that project's count alone" \
  "subjects by project: Alpha 1, Beta 1" "$MULTI"
must_line "and still agrees with the independent walk" "$(expected_breakdown "$MULTI")" "$MULTI"

# ---- THE LEXER'S CHARACTER-LEVEL TRANSITIONS ---------------------------------------------------
#
# EVERY LEG ABOVE ASSERTS A WHOLE-FILE VERDICT, AND THE LEXER IS NOT A WHOLE-FILE FUNCTION. It is a
# left-to-right pass whose every step is a STRIDE — how far `i` advances past a character literal,
# past `"""`, past `@"`, past a doubled `""`, past `(*` and `*)`. `.github#2730`'s mechanical
# mutation sweep (`tests/signature-doc-siting/mutants.py`) found that not one of those strides was
# asserted: incrementing any of eleven of them left the fixture fully green, because every
# construct in the legs above is followed by enough slack that a one-character error resynchronises
# before it can change a verdict.
#
# That slack is the whole problem. A stride error does not corrupt the character it lands on; it
# corrupts whichever character it SKIPS, and only when that character was itself significant. So
# each leg below is built so the skipped character is a `/`, a `"` or a `*` — the three that decide
# something — and each is paired with a `///` whose reporting is then the observable difference.
#
# These are read as a group: each file is minimal and named for the stride it discriminates, so a
# failure here points at one line of the gate rather than at "the lexer".

# stride <name> <want-rc> <pattern> <body...> — a one-file tree carrying <body> as Stride.fs
stride() {
  local name="$1" want="$2" pat="$3" body="$4"
  local root
  root="$(mktree "stride-$(printf '%s' "$name" | tr -c 'a-zA-Z0-9' '-')")"
  cat >"$root/src/Proj/Stride.fsi" <<'FSI'
namespace Proj

module Stride =
    val run: int -> int
FSI
  # `%b`, not `%s`: each body below spells its F# escapes and its line breaks with backslashes, and
  # `%s` would write them through literally — a file that does not contain the construct the leg is
  # named for, passing for a reason that has nothing to do with the gate.
  printf 'namespace Proj\n\nmodule Stride =\n%b\n    let run n = n\n' "$body" >"$root/src/Proj/Stride.fs"
  must_exit "$name" "$want" "$pat" "$root"
}

FOUND='Stride\.fs: 1 XML documentation comment'

# A simple character literal is consumed WHOLE: `i` lands on the `/` that follows it, not past it.
stride "stride: a doc comment immediately after a simple character literal" 1 "$FOUND" \
  "    let b = 'x'/// A doc comment with no space between it and the literal before it."

# The same for an ESCAPED literal, whose closing quote is found by a search rather than by offset.
stride "stride: a doc comment immediately after an escaped character literal" 1 "$FOUND" \
  "    let c = '\\\\n'/// A doc comment immediately after an escaped character literal."

# The escape search must start AT the earliest possible closing quote. Starting one later finds the
# apostrophe inside the string that follows, and the pass then enters that string in the wrong state.
stride "stride: the escaped-literal close is searched from the right offset" 1 "$FOUND" \
  "    let d = ('\\\\n', \"it's fine\")/// A doc comment after a literal and a string holding one."

# THE ESCAPE SEARCH MUST NOT STOP AT ITS FIRST CANDIDATE. `'A'` closes four characters further
# on than `'\n'` does, and a search that returns the first index it looks at is right for every
# THREE-character escape — which is all of them but the numeric ones — so the shorter forms above
# cannot tell the two apart. `('A','"')` can: stopping early leaves the pass to re-read the real
# closing quote as the OPENING quote of a `','` literal, which lands it on the `"` of the second
# element and opens a string that runs to the end of the file. Both halves are valid F#.
stride "stride: the escape search finds the REAL closing quote, not the first index it tries" 1 "$FOUND" \
  "    let t = ('\\\\u0041','\"')\n    /// A doc comment after a tuple of two character literals."

# ...AND IT GIVES UP AT THE RIGHT DISTANCE. The search window is bounded so a stray `'` cannot
# swallow the line behind it, and the bound is tight rather than arbitrary: F#'s longest escape is
# `\U` with eight hex digits, whose closing quote sits eleven characters past the opening one, so
# anything further out is not an escape and must not be consumed as one. `src/` holds 40 quotes that
# are not character literals, so a bound that reaches too far is a live hazard rather than a
# theoretical one. Below, the would-be literal is one character too long: the bound must refuse it,
# leaving the `'"'` that follows to be read as the character literal it is. One character further
# and the pass consumes the wrong quote and opens a string instead.
stride "stride: a would-be escape longer than any F# escape is not consumed as one" 1 "$FOUND" \
  "    let x = '\\\\aaaaaaaaaa'\"'\n    /// A doc comment after a stray quote the bound refused."

# `'\"'` IS AN ESCAPED QUOTE CHARACTER. Read as anything but a whole literal, its `\"` leaves a bare
# `\"` that opens a string, and every line after it in the file is string content.
stride "stride: an escaped-QUOTE character literal does not open a string" 1 "$FOUND" \
  "    let q = '\\\\\"'\n    /// A doc comment after an escaped-quote character literal."

# A character literal at the very END of a line: the guard that decides whether one can start here
# is a boundary, and the literal is `'\"'`, so getting it wrong opens a string that never closes.
stride "stride: a character literal ending exactly at end of line is still consumed" 1 "$FOUND" \
  "    let e = '\"'\n    /// A doc comment after a line that ENDS with a quote character literal."

# A `'` that is NOT a literal advances by exactly one, so the character behind it is still seen.
# `ok'` is a primed identifier and the `\"` after it opens a real string.
stride "stride: a primed identifier advances one character, not past the quote behind it" 1 "$FOUND" \
  "    let ok' = 1\n    let h = ok'\"x\"\n    /// A doc comment after a primed identifier and a string."

# `\"\"\"\"\"\"` IS AN EMPTY TRIPLE-QUOTED STRING — six quotes, no content. It is the only shape in
# which the open stride and the close stride are adjacent, so each is discriminated alone.
stride "stride: an EMPTY triple-quoted string opens and closes at the right offsets" 1 "$FOUND" \
  "    let t = \"\"\"\"\"\"/// A doc comment straight after an empty triple-quoted string."

# `@\"\"` is an empty verbatim string. One character further and the pass never leaves it.
stride "stride: an EMPTY verbatim string is entered at the right offset" 1 "$FOUND" \
  "    let v = @\"\"\n    /// A doc comment after an empty verbatim string."

# Inside a verbatim string the pass walks one character at a time; two at a time steps OVER the
# closing quote and the rest of the file becomes string content.
stride "stride: a verbatim string is walked one character at a time" 1 "$FOUND" \
  "    let w = @\"abc\"\n    /// A doc comment after a short verbatim string."

# The same inside an ordinary string literal.
stride "stride: an ordinary string is walked one character at a time" 1 "$FOUND" \
  "    let s = \"abc\"\n    /// A doc comment after a short ordinary string."

# LEAVING A STRING IS ITS OWN STRIDE, AND THE TWO LEGS ABOVE CANNOT SEE IT. They put the doc comment
# on the NEXT line, so the pass has a whole line break in which to resynchronise: overshooting the
# closing quote by one lands harmlessly inside the remaining whitespace. The character immediately
# after the close is the only position that discriminates, and it has to be the first `/` of a `///`.
stride "stride: the character right after a verbatim string's close is not skipped" 1 "$FOUND" \
  "    let w2 = @\"abc\"/// A doc comment touching the verbatim string that precedes it."

stride "stride: the character right after an ordinary string's close is not skipped" 1 "$FOUND" \
  "    let s2 = \"abc\"/// A doc comment touching the ordinary string that precedes it."

# `(**)` is an empty block comment: the open stride lands exactly on the `*` of the close.
stride "stride: an EMPTY block comment opens and closes at the right offsets" 1 "$FOUND" \
  "    let x = 1 (**)/// A doc comment straight after an empty block comment."

# `(*(**)*)` is an empty block comment nested inside another: the INNER open stride, taken at
# depth 1, lands on the `*` of the inner close.
stride "stride: a NESTED empty block comment opens and closes at the right offsets" 1 "$FOUND" \
  "    let y = 1 (*(**)*)/// A doc comment straight after a nested empty block comment."

# `@\"x\"\"\"` closes a verbatim string whose last content character is an escaped quote. The doubled
# `\"\"` must consume exactly two characters: three leaves the pass inside the string for ever.
stride "stride: a doubled quote inside a verbatim string consumes exactly two characters" 1 "$FOUND" \
  "    let z = @\"x\"\"\"\n    /// A doc comment after a verbatim string ending in a doubled quote."

# ...and the MIRROR, which is the only construct that tells `\"\"`-is-an-escape from
# `\"\"`-is-close-then-reopen: both leave an unterminated file, but they leave it inside DIFFERENT
# constructs, and each arm reports its own words. Reading the doubling as a close would say `string
# literal` where the truth is `verbatim`.
VERBDOUBLE="$(mktree verbdouble)"
write_clean_pair "$VERBDOUBLE"
cat >"$VERBDOUBLE/src/Proj/Doubled.fsi" <<'FSI'
namespace Proj

module Doubled =
    val banner: string
FSI
cat >"$VERBDOUBLE/src/Proj/Doubled.fs" <<'FS'
namespace Proj

module Doubled =
    let banner = @"a""b
FS
must_exit "no verdict: an unterminated verbatim string with a DOUBLED quote is still verbatim" 3 \
  "reached end of file inside a verbatim @\"\.\.\.\" string" "$VERBDOUBLE"

# FOUR SLASHES AT END OF LINE. The four-slash disqualification reads the character after `///`, and
# the guard that keeps that read in bounds is a boundary the `//// text` case above never reaches:
# there, a fifth character always exists. A line that is EXACTLY `////` is the only input that
# separates the bound from the read, and reporting it would be the gate firing on correct code.
BAREFOUR="$(mktree barefourslash)"
write_clean_pair "$BAREFOUR"
cat >"$BAREFOUR/src/Proj/BareFour.fsi" <<'FSI'
namespace Proj

module BareFour =
    val run: int -> int
FSI
printf 'namespace Proj\n\nmodule BareFour =\n    ////\n    let run n = n\n' \
  >"$BAREFOUR/src/Proj/BareFour.fs"
must_exit "green: a line that is EXACTLY four slashes is not a doc comment" 0 \
  "OK: every subject file matches" "$BAREFOUR"

# ---- THE FINDING RENDERER: THE CAP, AND WHAT IT SAYS IT DROPPED --------------------------------
#
# `render` prints at most twelve line numbers and then `and N more`, so a 455-line offender cannot
# bury every other finding in the job log. Nothing asserted either half: no leg above has more than
# two hits in one file, so the cap never engaged and the continuation text was never emitted.

MANY="$(mktree manyhits)"
write_clean_pair "$MANY"
cat >"$MANY/src/Proj/Many.fsi" <<'FSI'
namespace Proj

module Many =
    val run: int -> int
FSI
{
  printf 'namespace Proj\n\nmodule Many =\n'
  for i in $(seq 1 15); do printf '    /// Doc comment number %s.\n' "$i"; done
  printf '    let run n = n\n'
} >"$MANY/src/Proj/Many.fs"
must_exit "red: fifteen doc comments in one file are a finding" 1 "Many\.fs: 15 XML documentation comment" "$MANY"
must_line "the finding lists twelve lines and says how many it dropped" \
  "  src/Proj/Many.fs: 15 XML documentation comment(s) in an implementation file that has a sibling .fsi, and no baseline entry -- the compiler discards them. Line(s): 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, and 3 more" \
  "$MANY"
must_line "the finding count is stated, and it is the number of findings" "FAILED -- 1 finding(s):" "$MANY"

# THE OBSERVED TOTAL AND THE BASELINE TOTAL MUST BE SEEN TO DIFFER SOMEWHERE, OR NEITHER IS PINNED.
# Every other tree that prints this line is GREEN, and green is precisely the state in which the two
# are EQUAL — so `carrying N` and `records ... N line(s)` could be the same expression and no leg
# would know. Measured: replacing `total_hits` with the baseline's own sum left the whole fixture at
# 82 passed, 0 failed. This is the only leg where the two numbers are 15 and 0.
must_line "the observed total is the OBSERVED total, not the baseline's" \
  "discovered: 2 .fs file(s) under $MANY/src, 2 with a sibling .fsi, 1 carrying 15 XML documentation comment line(s); baseline records 0 file(s), 0 line(s)" \
  "$MANY"

# ---- WHAT THE GATE TELLS SOMEONE WHO ASKS IT WHAT IT IS ---------------------------------------
#
# `--help` is the only description of this gate a contributor reaches from the command line, and it
# is the one output no leg touched: every string in the argument parser could be replaced wholesale
# and the fixture stayed green. A gate whose own description has drifted from what it does is the
# same defect as a stale baseline, one level up.

# argparse re-wraps both the description and each option's help to the terminal width, so the line
# breaks in the output are a property of the formatter rather than of the text. Compare the text.
helptext() {
  set +e
  # COLUMNS pins the formatter's width: at the default it hyphen-breaks the default baseline PATH
  # across two lines, and a leg written around that break would assert the terminal, not the gate.
  HELP="$(COLUMNS=200 python3 "$GATE" --help 2>&1 | tr '\n' ' ' | tr -s ' ')"
  HRC=$?
  set -e
}
helptext
if [ "$HRC" -ne 0 ]; then
  bad "--help exits 0" "$HELP"
elif ! matches "$HELP" "Assert no F# XML documentation comment sits in an implementation file that has a signature file"; then
  bad "--help states what the gate asserts" "$HELP"
elif ! matches "$HELP" "repository root to scan"; then
  bad "--help documents --root" "$HELP"
elif ! matches "$HELP" "baseline file \(default: <root>/tests/signature-doc-siting/baseline.txt\)"; then
  bad "--help documents --baseline, with the real default path" "$HELP"
else
  ok "--help states what the gate asserts and documents both of its options"
fi

# ---- IMPORTING THE GATE MUST NOT RUN IT -------------------------------------------------------
#
# The `if __name__ == "__main__":` guard is the last unasserted branch in the file, and it is not
# ceremony: this gate's lexer is reusable, and re-deriving the character-literal population quoted in
# `char_literal_end`'s docstring is done by importing the module and calling that function directly.
# With the guard forced true, that import runs `main()` and then `sys.exit`s inside the importer —
# so the check someone reaches for to VERIFY this gate's own documentation would instead be silently
# hijacked by it. Cheap to assert, and asserted nowhere else.
cat >"$WORK/import-probe.py" <<'PY'
import importlib.util
import sys

spec = importlib.util.spec_from_file_location("gate_under_test", sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
# A character literal is three characters wide, so the lexer reports the index just past it.
print("imported cleanly; char_literal_end =", module.char_literal_end("'x'", 0))
PY
set +e
IMPORTOUT="$(python3 "$WORK/import-probe.py" "$GATE" 2>&1)"
IMPORTRC=$?
set -e
if [ "$IMPORTRC" -ne 0 ]; then
  bad "importing the gate as a module does not run or exit it" "$IMPORTOUT"
elif ! matches "$IMPORTOUT" "^imported cleanly; char_literal_end = 3$"; then
  bad "importing the gate exposes its lexer without side effects" "$IMPORTOUT"
else
  ok "importing the gate as a module does not run it, and exposes its lexer"
fi

# ---- THE REAL TREE ----------------------------------------------------------------------------
#
# Everything above runs on strings this file wrote. These three legs run on the repository's own
# content, so a fixture that is green while the SHIPPED baseline has rotted cannot exist.

must_exit "REAL-1: the shipped baseline describes the real tree exactly" 0 "OK: every subject file matches" "$REPO"

# REAL-1a: AND IT LOOKED AT EVERY PROJECT WHILE DOING SO. This is the leg .github#2730's ordinary
# chain died without: REAL-1 above is an exact-match check, and an exact-match check cannot miss a
# file it never opened. Every one of the 13 baselined files lives in ONE project, so the other two
# contribute nothing to REAL-1 — narrowing `discover()` to drop `src/FS.GG.Coord.GitHub`, the project
# this row emptied of 1,117 doc-comment lines, left REAL-1 green over 49 of 62 subjects.
#
# The expected value is not written here. It is walked out of the tree with `find`, so the leg keeps
# its force when a project is added, when one is renamed, and when the counts move — the three events
# that would otherwise turn a hard-coded number into a leg someone updates without reading.
must_line "REAL-1a: the real tree's population is stated per project, by value" \
  "$(expected_breakdown "$REPO")" "$REPO"

# REAL-1b: and the aggregate counts agree with the same independent walk. `discovered:` opens with
# two numbers that no leg has ever compared against anything.
REAL_SOURCES="$(find "$REPO/src" -name '*.fs' -not -path '*/obj/*' -not -path '*/bin/*' | wc -l)"
REAL_SUBJECTS="$(subject_files "$REPO" | wc -l)"
must_line "REAL-1b: the real tree's aggregate counts agree with an independent walk" \
  "discovered: $REAL_SOURCES .fs file(s) under $REPO/src, $REAL_SUBJECTS with a sibling .fsi, $(
    awk 'NF && $1 !~ /^#/ {f++; l+=$1} END {printf "%d carrying %d", f, l}' \
      "$REPO/tests/signature-doc-siting/baseline.txt"
  ) XML documentation comment line(s); baseline records $(
    awk 'NF && $1 !~ /^#/ {f++; l+=$1} END {printf "%d file(s), %d line(s)", f, l}' \
      "$REPO/tests/signature-doc-siting/baseline.txt"
  )" "$REPO"

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
must_exit "REAL-3: a doc comment reintroduced into a swept file reds the gate" 1 \
  "Landable\.fs: 1 XML documentation comment\(s\) in an implementation file that has a sibling \.fsi, and no baseline entry" "$REAL_COPY"

echo
echo "signature-doc-siting fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

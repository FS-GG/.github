#!/usr/bin/env python3
r"""No F# XML documentation comment may sit in an implementation file that has a signature file.

.github#2730, epic #266 (the rule nothing asserts).

THE DEFECT. When an F# module has a signature file, the `///` documentation comments in the
IMPLEMENTATION are discarded by the compiler. The signature's documentation is what reaches the
generated XML file, the IDE tooltip, and every downstream reader. Measured at `0ddd4b88`: 2,970
`///` lines sat in 37 implementation files under `src/FS.GG.Coord.Core` and
`src/FS.GG.Coord.GitHub` that already had a sibling `.fsi`, of which 2,511 were substantive and
written only there. Not one reached a consumer.

THAT MEASUREMENT IS A CONTROLLED EXPERIMENT, NOT A GREP THAT FOUND NOTHING. An empty result set is
not a negative finding: "0 hits" and "the search could not have hit" are the same bytes. So the
claim rests on one declaration used as a two-legged control -- `FS.GG.Coord.Core/IntakeReceipt.validate`,
one `dotnet build src/FS.GG.Coord.Core -c Release`, one `grep -c` over the emitted
`FS.GG.Coord.Core.xml`:

    sentinel written in IntakeReceipt.fs   -> 0 hits
    sentinel written in IntakeReceipt.fsi  -> 1 hit, under M:FS.GG.Coord.IntakeReceipt.validate(...)

Same file, same build, same grep; only which of the two files the sentence was typed into differs.
The positive leg is what makes the negative leg mean anything.

AND THE COMPILER CANNOT WARN. `///` binds silently to the next declaration, so a doc block whose
subject has been deleted or moved re-binds to whatever now follows it rather than erroring. The
Release build of all three projects emits zero warnings over 3,913 such lines. The worked example
the row cites is still live at `0ddd4b88`, in a file this gate reports but this row did not sweep:
`src/FS.GG.Coord.Cli/Client.fs` lines 718-752 are a 35-line block describing an implementation of
`boardBlockingCounts` that no longer exists -- the declaration it now attaches to, at line 753, is
the one-line forward `let boardBlockingCounts = BoardFactsApplication.blockingCounts` -- and lines
755-770 describe `enrichBoardFacts`, defined 120 lines earlier at line 635, while binding to
`let mutable private generatedPathCollector` at line 778.

WHAT THIS GATE DECIDES ON, AND WHAT IT DELIBERATELY CANNOT SEE. Its subject is the comment MARKER,
never the content. It never asks what a comment says, so it can form no opinion about whether a
sentence is contract prose or implementation prose -- and a `//` comment is invisible to it
entirely. That is the whole reason it is safe to enforce: prose that is genuinely about the
implementation (why THIS loop, this ordering constraint, the incident that produced this branch) is
correctly placed in the `.fs` and is never reported, because it is written `//`. The only thing
refused is a `///` in a file whose `///` the compiler provably discards, which is wrong
independently of what it says. A gate that had to classify prose would be one people learn to
suppress; this one has nothing to suppress.

IT LEXES RATHER THAN GREPS, and each clause below has a measured reason:

  * A doc comment need not be line-leading. `src/FS.GG.Coord.Core/Protocol.fs:59` and seven other
    sites in `src/` carry one after a `{` on the same physical line, and a `^\s*///` grep misses
    every one of them (8 in the two swept projects, 2 more in `Cli`).
  * FOUR OR MORE SLASHES ARE NOT A DOC COMMENT. F# lexes `////...` as an ordinary comment. Verified
    the same way as the claim above: `////` and `///` sentinels on one declaration in
    `IntakeReceipt.fsi`, one Release build -- the `///` sentinel appears in the emitted XML, the
    `////` sentinel appears nowhere. Reporting a `////` line would be this gate firing on correct
    code, which is the one failure it must not have.
  * `(* ... *)` nests in F#, and a `///` inside one is a comment about a comment.
  * An ordinary string, a verbatim `@`-string, and a triple-quoted string can all contain `///` --
    a URL, a path, an example of this very grammar. ALL THREE SPAN NEWLINES, so all three are
    tracked across lines; `@"..."` does so by design, and reading it a line at a time made this gate
    return exit 0 on a planted contract sentence. See `doc_comment_lines`.
  * `'"'` IS A CHARACTER LITERAL, NOT A QUOTE. It occurs in `src/` today, and it must not open a
    string -- while `'T` and `xs'` must not be mistaken for literals. See `char_literal_end`.

WHAT IS LIVE HERE AND WHAT IS LATENT, measured rather than assumed. Multi-line strings are LIVE:
`src/` holds 320 continuation lines inside them across 4 subject files -- 245 triple-quoted and 75
ordinary, chiefly `Cli/Options.fs` (248) and `GitHub/Scan.fs` (44) -- and, at code level, 106
character literals against 40 `'` that are not one, the two `'"'` above among the literals (see
`char_literal_end` for the method, and for two earlier figures here that did not survive
re-derivation). What is latent is the multi-line VERBATIM string specifically:
`src/` holds none today.

That distinction is exactly why the defect survived authoring. None of the 320 live continuation
lines happens to carry a `///` or a `(*`, so the reset-per-line version of this lexer produced the
same 943 as this one and looked correct -- and it still returned exit 0, `OK: every subject file
matches`, on a genuine contract sentence planted behind a two-line `@"..."`. Latent is not
harmless: the subject is `src/**` in perpetuity, the recorded `Cli` residue is edited by every
extraction lane, and the miss is silent and unbounded. So `tests/signature-doc-siting/run.sh`
constructs the multi-line cases synthetically, in BOTH directions. A precaution the corpus cannot
exercise is a precaution nothing proves, and #266 is the register of those.

BEHAVIOUR ON TODAY'S TREE IS UNCHANGED BY THAT REPAIR, and that is checked rather than hoped:
line-for-line over all 66 `.fs` files under `src/`, the reset-per-line lexer and this one return
IDENTICAL hit lists (0 differing, 0 unlexable). The repair changes what happens to shapes the tree
does not yet contain, which is the only kind of change a gate over `src/**` in perpetuity can make
safely.

THE BASELINE, AND WHY IT IS EXACT IN BOTH DIRECTIONS. `src/FS.GG.Coord.Cli` was outside .github#2730's
lane -- .github#2724 held `Client.fs`/`Client.fsi` while it ran, and the extraction programme gives
each extracted module a proper `.fsi` with its prose moved as part of that work. Sweeping it there
would have collided with every extraction lane for no benefit. So its 943 lines across 12 files are
RECORDED rather than dropped, as `<count> <path>` lines whose counts must match the tree EXACTLY:

    more than the number here  -> a new doc comment arrived. Repair it; raising the number is not a
                                  remedy.
    fewer than the number here -> the baseline is STALE. Lower it in the same commit as the repair.
    a subject file with hits and no line here -> a new offender in a new file.
    a line here naming a path that is not a subject file -> delete the line.

So a repair cannot land without its decrement and a decrement cannot land without its repair, which
is the property that makes the file a true statement about the tree rather than a list nobody
maintains. `tests/pipefail-assertions/baseline.txt` (.github#2689) is the same mechanism for a
different subject; this one is deliberately spelled the same way.

EXIT CODES -- three, and no fourth:

    0  every subject file matches its baseline entry exactly.
    1  a finding: a new doc comment, an unbaselined offender, or a stale baseline.
    3  NO VERDICT. Discovery found no `.fs` under `src/`, or none with a sibling `.fsi`, or the
       baseline could not be read or parsed, or a subject file could not be READ, or one could not
       be LEXED (it ends inside a string or a block comment, so the pass lost its place and every
       line after that point was scanned in the wrong state). Scanning nothing is a failure to
       scan; without the baseline the gate cannot tell a recorded residue from a new offender; and
       a count taken after a mis-parse is worse than no count, because it looks like one. This is
       never a pass, and it is a distinct code because "I could not check" must not be mistaken for
       "I checked, and it is fine" (#266) nor for "I checked, and it is broken" (#320).

The discovered counts are printed on EVERY run, green ones included, so a subject that silently
shrank is visible in the log rather than inferred from a pass.

Pure stdlib. No network, no build, no `dotnet`: the verdict is a function of the committed tree.
"""

from __future__ import annotations

import argparse
import os
import sys

EX_OK = 0
EX_FINDING = 1
EX_NO_VERDICT = 3

DEFAULT_BASELINE = os.path.join("tests", "signature-doc-siting", "baseline.txt")
SOURCE_ROOT = "src"


def char_literal_end(line: str, i: int) -> int | None:
    """Index just past the F# character literal starting at `line[i] == "'"`, or None if it is not one.

    A `'` in F# is not reliably a quote: `'T` is a generic type parameter and `xs'` is a primed
    identifier, and `src/` holds BOTH shapes in quantity, so neither can be assumed away. So a `'` is
    read as a literal only when its closing `'` is where the grammar puts it, and otherwise as an
    ordinary character.

    THE POPULATION, MEASURED THE WAY THIS FUNCTION SEES IT. Replay `doc_comment_lines`' pass over
    `discover()`'s output and tally every `'` it reaches AT CODE LEVEL -- outside strings and block
    comments, the only place this function is consulted -- and today's `src/` gives 106 character
    literals against 40 `'` that are not one, over the 66 `.fs` files; 81 against 36 over the 61 with
    a sibling `.fsi`. The literals span 25 distinct spellings, `'"'` among them; the non-literals are
    type parameters (`'a`, `'item`, `'value`) and primed identifiers (`done'`, `member'`, `base'`,
    `inline'`, `o'`).

    AN EARLIER VERSION OF THIS PARAGRAPH SAID "1,933 of those against 29 character literals", and
    neither number survived re-derivation at review. `29` reproduces under no scoping tried: this
    lexer reaches 106/81, and a raw `grep -ohE "'(\\.|[^'\\])'" $(find src -name '*.fs' -not -path
    '*/obj/*' -not -path '*/bin/*') | wc -l` gives 120 -- higher, not lower, because a raw grep also
    counts what sits inside strings and comments. `1,933` is only reachable by a raw-text scan of
    that same kind (a `'`-adjacent-identifier regex gives ~2,000 today), which counts a population
    this function never decides over. The ratio they jointly asserted is also backwards. The argument
    never needed a ratio: both shapes occur, and either one misread costs the pass its place for the
    rest of the file -- which is what the paragraph below actually establishes.

    THIS IS LOAD-BEARING, NOT DECORATIVE. `'"'` occurs in `src/` TODAY -- `RegistryPredicate.fs:40`
    and `SemanticDiff.fs:103`, both subject files -- and once string state carries across lines (it
    must; see `doc_comment_lines`) reading that quote as a string OPENER puts the pass into the
    wrong state for the rest of the file, until some later unpaired quote happens to resynchronize
    it. Any `///` inside that window is lost in silence. Measured: with this function stubbed to
    return None, a `///` placed after a `| '"' ->` match arm goes unreported and the gate says
    `OK: every subject file matches` -- the same blindness this module exists to refuse, in a
    construct the tree already contains, so the two are repaired together. Today's two sites are
    resynchronized by a later quote and the shipped counts are unaffected either way; that is luck,
    not a property, and `tests/signature-doc-siting/run.sh` pins the behaviour rather than the luck.
    """
    n = len(line)
    if i + 2 >= n:
        return None
    if line[i + 1] == "\\":
        # `'\n'`, `'\''`, `'\\'`, `'\u0041'`: an escape consumes at least one character, so the
        # closing quote cannot fall before i+3. The bound keeps a stray `'` from swallowing a line.
        for j in range(i + 3, min(n, i + 12)):
            if line[j] == "'":
                return j + 1
        return None
    if line[i + 2] == "'":
        return i + 3
    return None


def doc_comment_lines(text: str) -> tuple[list[int], str | None]:
    """(1-based line numbers carrying an F# XML documentation comment, unterminated construct or None).

    A single left-to-right pass whose every state carries ACROSS LINES: `(* *)` nesting depth, and
    each of the three string forms.

    AN EARLIER VERSION RESET THE TWO STRING STATES AT EACH LINE, on the stated ground that neither
    spans a newline in a well-formed F# file. That is false, and `@"..."` is the plain
    counterexample: an F# verbatim string spans newlines BY DESIGN, and an ordinary `"..."` may too.
    Resetting cost the gate the thing it exists to do, in both directions and silently:

      * a verbatim string whose continuation line held a `(*` left `depth == 1`, which suppressed
        every `///` to the end of the file -- so a genuine new contract sentence planted in a swept
        file returned exit 0, `OK: every subject file matches`;
      * on a baselined file the same shape read as `STALE BASELINE: delete this line`, and following
        that printed remedy reached exit 0 while `grep -c '///'` still returned 455 -- a green gate
        endorsing a baseline that is no longer a true statement about the tree;
      * mirrored, a `///` on a continuation line INSIDE a verbatim string was reported as a doc
        comment, which is the false positive this module calls the one failure it must not have.

    So the state carries, and `tests/signature-doc-siting/run.sh` asserts both directions.

    AND A MIS-PARSE MUST NEVER AGAIN PRESENT AS GREEN. Carrying state is correct but it restores the
    original author's real fear: a construct opened and never closed blinds the rest of the file.
    The answer is not to reset it -- that is the defect above -- but to REFUSE. A file that ends
    inside a string or a block comment is malformed F# the lexer could not follow, so the second
    element of the return names it and the caller turns it into a NO VERDICT. `I could not lex this`
    is then loud rather than silent, which is the whole of #266.
    """
    hits: list[int] = []
    depth = 0
    in_triple = False
    in_str = False
    in_verbatim = False

    for lineno, line in enumerate(text.split("\n"), 1):
        i = 0
        n = len(line)

        while i < n:
            char = line[i]

            if in_triple:
                if line.startswith('"""', i):
                    in_triple = False
                    i += 3
                else:
                    i += 1
                continue

            if in_verbatim:
                # `@"..."` escapes a quote by doubling it, and has no backslash escapes at all.
                if char == '"':
                    if line.startswith('""', i):
                        i += 2
                    else:
                        in_verbatim = False
                        i += 1
                else:
                    i += 1
                continue

            if in_str:
                if char == "\\":
                    i += 2
                elif char == '"':
                    in_str = False
                    i += 1
                else:
                    i += 1
                continue

            if depth > 0:
                # F# block comments NEST, so `(*` inside one is not noise.
                if line.startswith("(*", i):
                    depth += 1
                    i += 2
                elif line.startswith("*)", i):
                    depth -= 1
                    i += 2
                else:
                    i += 1
                continue

            if line.startswith('"""', i):
                in_triple = True
                i += 3
            elif line.startswith('@"', i):
                in_verbatim = True
                i += 2
            elif char == '"':
                in_str = True
                i += 1
            elif char == "'":
                # A character literal is consumed WHOLE, so a quote inside one (`'"'`) is inert
                # rather than a string opener. A `'` that is not one -- `'T`, `xs'` -- advances by
                # a single character, exactly as before.
                end = char_literal_end(line, i)
                i = i + 1 if end is None else end
            elif line.startswith("(*", i):
                depth += 1
                i += 2
            elif line.startswith("///", i):
                # A FOURTH SLASH DISQUALIFIES. F# lexes `////...` as an ordinary comment, so
                # reporting it would be this gate firing on correct code.
                if i + 3 < n and line[i + 3] == "/":
                    break
                hits.append(lineno)
                break
            elif line.startswith("//", i):
                break
            else:
                i += 1

    if in_triple:
        return hits, "reached end of file inside a triple-quoted string"
    if in_verbatim:
        return hits, 'reached end of file inside a verbatim @"..." string'
    if in_str:
        return hits, "reached end of file inside a string literal"
    if depth > 0:
        return hits, f"reached end of file inside {depth} unclosed block comment(s)"
    return hits, None


def discover(root: str) -> tuple[list[str], list[str]]:
    """(every .fs under <root>/src outside obj|bin, those of them with a sibling .fsi)."""
    sources: list[str] = []
    src_root = os.path.join(root, SOURCE_ROOT)
    for dirpath, dirnames, filenames in os.walk(src_root):
        dirnames[:] = sorted(d for d in dirnames if d not in ("obj", "bin"))
        for name in sorted(filenames):
            if name.endswith(".fs"):
                sources.append(os.path.join(dirpath, name))
    subjects = [p for p in sources if os.path.exists(p + "i")]
    return sources, subjects


def read_baseline(path: str) -> tuple[dict[str, int] | None, str | None]:
    """(counts keyed by repo-relative POSIX path, error). Exactly one of the two is None."""
    try:
        with open(path, encoding="utf-8") as handle:
            raw = handle.read()
    except OSError as exc:
        return None, f"cannot read the baseline {path}: {exc}"

    counts: dict[str, int] = {}
    for lineno, line in enumerate(raw.split("\n"), 1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        parts = stripped.split(None, 1)
        if len(parts) != 2:
            return None, f"{path}:{lineno}: not a `<count> <path>` line: {stripped!r}"
        try:
            count = int(parts[0])
        except ValueError:
            return None, f"{path}:{lineno}: count is not an integer: {parts[0]!r}"
        if count <= 0:
            return None, (
                f"{path}:{lineno}: count must be positive -- a file with no doc comments carries "
                f"no line at all, so a `0` entry is an unmaintained one: {stripped!r}"
            )
        entry = parts[1].strip()
        if entry in counts:
            return None, f"{path}:{lineno}: duplicate entry for {entry}"
        counts[entry] = count
    return counts, None


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Assert no F# XML documentation comment sits in an implementation file that has a "
            "signature file, where the compiler discards it."
        )
    )
    parser.add_argument("--root", default=".", help="repository root to scan (default: .)")
    parser.add_argument(
        "--baseline",
        default=None,
        help=f"baseline file (default: <root>/{DEFAULT_BASELINE})",
    )
    args = parser.parse_args(argv)

    root = args.root
    baseline_path = args.baseline or os.path.join(root, DEFAULT_BASELINE)

    src_root = os.path.join(root, SOURCE_ROOT)
    if not os.path.isdir(src_root):
        print(f"NO VERDICT: {src_root} is not a directory -- there is no subject to scan.")
        return EX_NO_VERDICT

    sources, subjects = discover(root)

    baseline, baseline_error = read_baseline(baseline_path)
    if baseline is None:
        print(f"NO VERDICT: {baseline_error}")
        print(
            f"discovered: {len(sources)} .fs file(s) under {src_root}, "
            f"{len(subjects)} with a sibling .fsi"
        )
        return EX_NO_VERDICT

    if not sources:
        print(f"NO VERDICT: discovery found no .fs file under {src_root}.")
        return EX_NO_VERDICT

    if not subjects:
        print(
            f"NO VERDICT: {len(sources)} .fs file(s) under {src_root}, but not one has a sibling "
            ".fsi. Scanning nothing is a failure to scan, not a clean tree."
        )
        return EX_NO_VERDICT

    observed: dict[str, list[int]] = {}
    unreadable: list[str] = []
    unlexable: list[str] = []
    for path in subjects:
        try:
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
        except (OSError, UnicodeDecodeError) as exc:
            unreadable.append(f"{path}: {exc}")
            continue
        lines, unterminated = doc_comment_lines(text)
        if unterminated is not None:
            unlexable.append(f"{path}: {unterminated}")
            continue
        if lines:
            observed[os.path.relpath(path, root).replace(os.sep, "/")] = lines

    if unreadable:
        print("NO VERDICT: a subject file could not be read, so the scan is incomplete:")
        for entry in unreadable:
            print(f"  {entry}")
        return EX_NO_VERDICT

    if unlexable:
        # A construct opened and never closed means the pass lost its place, and everything after it
        # in that file was scanned in the wrong state. Reporting a count from it would be the exact
        # silent blindness this gate exists to refuse, so it refuses to report one.
        print("NO VERDICT: a subject file could not be lexed, so its count cannot be trusted:")
        for entry in unlexable:
            print(f"  {entry}")
        return EX_NO_VERDICT

    total_hits = sum(len(v) for v in observed.values())
    print(
        f"discovered: {len(sources)} .fs file(s) under {src_root}, "
        f"{len(subjects)} with a sibling .fsi, "
        f"{len(observed)} carrying {total_hits} XML documentation comment line(s); "
        f"baseline records {len(baseline)} file(s), {sum(baseline.values())} line(s)"
    )

    findings: list[str] = []

    def render(lines: list[int], limit: int = 12) -> str:
        # A capped list, because a 455-line offender would otherwise bury every other finding in
        # the job log -- and the point of naming lines at all is that the reader can go to one.
        head = ", ".join(str(n) for n in lines[:limit])
        return head if len(lines) <= limit else f"{head}, and {len(lines) - limit} more"

    for path in sorted(set(observed) | set(baseline)):
        seen = observed.get(path, [])
        allowed = baseline.get(path, 0)
        if len(seen) == allowed:
            continue
        if allowed == 0:
            findings.append(
                f"{path}: {len(seen)} XML documentation comment(s) in an implementation file that "
                f"has a sibling .fsi, and no baseline entry -- the compiler discards them. "
                f"Line(s): {render(seen)}"
            )
        elif len(seen) > allowed:
            findings.append(
                f"{path}: {len(seen)} XML documentation comment(s), baseline allows {allowed} -- "
                f"{len(seen) - allowed} new one(s). Line(s): {render(seen)}"
            )
        elif not seen:
            findings.append(
                f"{path}: baseline claims {allowed} XML documentation comment(s) and the tree has "
                f"none. STALE BASELINE: delete this line."
            )
        else:
            findings.append(
                f"{path}: {len(seen)} XML documentation comment(s), baseline claims {allowed}. "
                f"STALE BASELINE: write {len(seen)}."
            )

    if findings:
        print()
        print(f"FAILED -- {len(findings)} finding(s):")
        for finding in findings:
            print(f"  {finding}")
        return EX_FINDING

    print("OK: every subject file matches its baseline entry exactly.")
    return EX_OK


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""signature-coverage -- every compiled F# implementation states a contract, and states a real one.

.github#2731, epic .github#266 (the rule nothing asserts).

TWO LEGS, AND THEY ARE NOT THE SAME KIND OF CLAIM.

  EXISTENCE (an ERROR).  Every `.fs` under the subject roots has a sibling `.fsi`, unless it is one
  of the explicitly listed entry points below.

  SUBSTANCE (a WARNING).  A signature file whose substantive size is under 10% of its implementation's
  is reported, because that is the signal that a seam was DECLARED but never DESCRIBED.

The second leg is why this gate exists at all. An existence check on its own is satisfiable by a
token file, and at `d1632c4e` four of them already existed: `CycleLedgerApplication.fsi` stated one
`val` and no contract against a 258-line implementation. A gate that counted it as covered would
have reported the tree clean while the convention it was enforcing had been hollowed out -- which
is #266's shape exactly, and is the reason the row asked for both halves in one instrument.

WHY SUBSTANCE IS A WARNING AND NEVER AN ERROR. The ratio is a HEURISTIC over a real quantity, and a
small module with a genuinely small surface must be able to pass. `GraphQlEnvelope.fsi` is 6 lines
against 31 and is complete; nothing is missing from it. An error here would either be wrong about
that file or would have to grow exceptions until it meant nothing. So the ratio informs a human and
never fails a build, and the only thing that fails a build is the binary, checkable fact that a
signature file is absent.

WHAT COUNTS AS A LINE, AND WHY THIS DEFINITION. A SUBSTANTIVE line is one that is neither blank nor
a non-doc comment. `///` COUNTS -- it is the contract, and in a `.fsi` it is most of the point --
while `//` and `(* ... *)` do not. That split is not invented here: it is the one
`scripts/check-signature-doc-siting.py` (.github#2730) already ships and enforces across this same
tree, where `///` is contract prose that reaches consumers and `//` is prose about the
implementation. Reusing it means the two gates cannot disagree about what a contract line is.

Counting substantive lines rather than raw ones also closes the obvious way to satisfy a ratio
without satisfying its intent: 200 blank lines, a banner comment, or a commented-out draft would all
move a raw ratio and move none of this one.

THE THRESHOLD IS AN EMPIRICAL GAP, NOT A ROUND NUMBER SOMEONE LIKED. Measured over all 68 `.fsi`
files in this repository at the time of writing, sorted by substantive ratio:

    1.2%  CycleLedgerApplication      <- the row's token signatures
    2.2%  ReviewApplication
    6.1%  DeliveryRouteApplication
   ------------------------------------ 10%
   10.6%  KitDigest
   13.0%  Snapshot
   13.7%  CycleLedger
   14.9%  DeliveryApplication
    ...   up to 170.9% (Types.fsi, larger than its own implementation)

There is a genuine gap between 6.1% and 10.6%, and 10% sits inside it. The substantive measure is
what makes that gap safe to stand on: under RAW line counts the same boundary is knife-edge, with
`Snapshot.fsi` at 10.1% -- one line from tripping a warning it does not deserve. Under substantive
counting it is 13.0%, comfortably clear, because raw counting was charging its implementation for
387 lines of `//` commentary. A threshold defended by a gap is only as good as the measure that
produces the gap, so the measure had to move first.

RECORDED BECAUSE A FUTURE READER WILL RE-DERIVE THIS: the row's own body cites `DeliveryApplication`
at 7.9% (36/455). That figure is STALE -- it was measured at `d1632c4e` and the file has since grown
to 63/563 raw, 14.9% substantive. It is above the threshold today and this gate does not warn on it.
The row's other three token signatures are still below it, and are repaired in the same change that
adds this gate.

TWO ALTERNATIVE DEFINITIONS OF "TOKEN SIGNATURE" WERE MEASURED AND REJECTED.

  DOCUMENTATION COVERAGE of exported declarations -- what fraction of `val`/`type` in a `.fsi` carry
  a `///`. Rejected as the error leg because the tree cannot bear it: repo-wide coverage is 81.9%
  (691 of 844 declarations, 33 of 65 files at 100%), so it would fire on half the repository on the
  day it shipped. It also DISAGREES WITH THE ROW: `DeliveryApplication.fsi` is at 90% coverage and
  the row names it a token signature, while `GraphQlEnvelope.fsi` is at 0% and is fine. A measure
  that ranks the row's own examples backwards is not measuring the thing the row is about.

  DECLARATION-SET EQUALITY between the `.fsi`'s `val`s and the `.fs`'s `let`s. Rejected outright,
  and this is the one worth stating loudly because it is the shape that has already gone wrong
  nearby. `tests/receiver-validate/run.sh` asserted SET EQUALITY between a gate's required fields
  and its producer's, "which reds the moment the producer becomes correct", and was corrected to
  containment. The same trap is here: a `.fsi` deliberately exposes a SUBSET of its implementation
  -- restricting the surface is what a signature file is FOR -- so equality would red on every
  private helper, and the remedy a contributor would reach for is deleting the signature's value.
  The relation between a signature and its implementation is containment, in one direction only,
  and the F# compiler already enforces it far better than this gate could. This gate does not
  attempt it.

EXEMPTIONS ARE LISTED, NOT PATTERN-MATCHED, AND THEY VALIDATE THEMSELVES. The row asked for the
entry-point exceptions to be explicit, and a `Program.fs` glob would have been the lazy reading of
that: it would exempt any file anyone named `Program.fs`, forever, silently. Instead each exemption
is one exact path with one written reason, and the gate CHECKS ITS OWN EXEMPTIONS -- a listed path
that no longer exists is a finding, a listed path that carries no `[<EntryPoint>]` is a finding, and
a listed path with a blank reason is a finding. An exemption is therefore a claim the gate can
falsify, not a hole in it. That is the difference between this list and a directory nobody looks at
again (`tests/signature-doc-siting/baseline.txt` makes the same argument about its own numbers).

THE `[<EntryPoint>]` CHECK IS A SUBSTRING MATCH, NOT A PARSE, and that is a known weakness rather
than an unexamined one. A `//`-commented or string-literal `[<EntryPoint>]` would satisfy it. It is
latent today because the marker occurs exactly once in each exempt file, at the real attribute, and
because reaching it at all requires someone to have already added a deliberate exemption entry with
a written reason -- so the substring is the second lock, not the first. Tightening it to a parse
would mean carrying an F# attribute reader here for two files; if the exemption list ever grows
past a handful, that trade changes and this should become a parse.

`obj/` AND `bin/` ARE PRUNED, AND THAT IS CORRECTNESS RATHER THAN TIDINESS. A built tree contains
generated implementations -- `FS.GG.Coord.Core.AssemblyInfo.fs` and friends, 8 of them here after a
Release build -- and not one has or should have a `.fsi`. A gate that walked into them would be
GREEN ON A CLEAN CHECKOUT AND RED AFTER A BUILD, which is a verdict about the machine rather than
about the tree. `tests/signature-coverage/run.sh` pins this with its own leg.

EXIT CODES -- three, and no fourth:

    0  every subject has a signature file. Substance warnings may have been printed; they are
       advice, they name real files, and they do not fail this gate.
    1  a finding: a subject with no sibling `.fsi`, or an exemption that does not hold up.
    3  NO VERDICT. No subject root exists, discovery found no `.fs` at all, or a file that had to be
       read could not be. Scanning nothing is a failure to scan, and it is a distinct code because
       "I could not check" must not be mistaken for "I checked, and it is fine" (#266) nor for
       "I checked, and it is broken" (#320).

This gate is deliberately under `scripts/policy-checkers.json`'s `large_checker_threshold_lines`
(500) and is therefore NOT registered there. That is compliance, not evasion: `policy-runner.py:92`
refuses a registered checker "as large but has only N lines", so registering a file this size would
itself be an error. If it ever crosses 500 lines it MUST gain an inventory row in the same change.
"""

from __future__ import annotations

import argparse
import os
import sys

# Subject roots, relative to the repository root. `src/` is the library and CLI surface; `scripts/`
# is here for `NewSddWorkspace`, which is a compiled, published F# project that simply does not live
# under `src/`. Test projects are deliberately absent: a test is not a consumed surface, and
# requiring signatures for them would be a large change that buys no caller anything.
SUBJECT_ROOTS = ("src", "scripts")

# Below this percentage, a signature file is reported as declaring a seam it never described.
# See the module docstring for how this number was derived; it is a gap in a measured distribution.
WARN_RATIO_PERCENT = 10.0

ENTRY_POINT_MARKER = "[<EntryPoint>]"

# Exact paths, each with the reason it needs no signature file. Read the docstring above before
# adding one: the gate validates every entry, so a bad exemption is a finding rather than a hole.
EXEMPT: dict[str, str] = {
    "src/FS.GG.Coord.Cli/Program.fs": (
        "The `fsgg-coord-engine` composition root. Its only export is `[<EntryPoint>] main`, which "
        "the runtime invokes and no F# caller can reach; a signature file would restrict a surface "
        "that has no callers and document a contract nobody can bind to."
    ),
    "scripts/NewSddWorkspace/Program.fs": (
        "The `new-sdd-workspace` composition root -- `OutputType=Exe` with `PackAsTool=true`, and "
        "no `.fsproj` in this repository carries a `ProjectReference` to it, so nothing can bind to "
        "`NewSddWorkspace.Program` at all: the package ships a command, not a library. Recorded as "
        "a DELIBERATE exception under .github#2731 AC6. At 2,278 lines the row is right that this "
        "is a module rather than a `main`, but the remedy for that is splitting the file -- a "
        "change to a published tool's internals that belongs to its own row -- and giving an "
        "unreachable surface a signature would not make it any smaller."
    ),
}


def discover(root: str) -> tuple[list[str], list[str]]:
    """(every .fs under the subject roots outside obj|bin, those with a sibling .fsi).

    Paths come back repository-relative with forward slashes, so a finding reads the same on every
    platform and matches the spelling used in EXEMPT.
    """
    sources: list[str] = []
    for subject_root in SUBJECT_ROOTS:
        absolute = os.path.join(root, subject_root)
        if not os.path.isdir(absolute):
            continue
        for dirpath, dirnames, filenames in os.walk(absolute):
            dirnames[:] = sorted(d for d in dirnames if d not in ("obj", "bin"))
            for name in sorted(filenames):
                if name.endswith(".fs"):
                    full = os.path.join(dirpath, name)
                    sources.append(os.path.relpath(full, root).replace(os.sep, "/"))
    sources.sort()
    covered = [p for p in sources if os.path.exists(os.path.join(root, p + "i"))]
    return sources, covered


def substantive_lines(text: str) -> tuple[int, bool]:
    """Lines that are neither blank nor a non-doc comment.

    `///` counts (it is the contract). `//` does not. `(* ... *)` does not, and nesting is tracked
    because F# block comments nest.

    A `(*` IS ONLY A COMMENT OPENER WHERE F# WOULD READ ONE, and getting that wrong is not a
    rounding error -- it is a FAIL-OPEN. An earlier revision of this function counted `(*` and `*)`
    anywhere on a line, including inside string literals and inside the `///` prose it had just
    counted. `src/FS.GG.Coord.Core/SemanticDiff.fs:99` contains `StartsWith("(*")` and the file
    holds no `*)` at all, so that revision opened a block comment at line 99 and never closed it:
    612 lines collapsed to 86 substantive, and `SemanticDiff.fsi` scored 96.51% against a true
    17.89%. THE ERROR WAS +78.6 POINTS, IN THE GREEN DIRECTION, ON A SHIPPED FILE. A single
    unmatched `(*` in a string can swallow the rest of any implementation and lift a genuinely thin
    signature above the threshold, which is precisely the "reports green because it could not see
    its subject" shape this gate was written against (#266). It changed no verdict at the time only
    by luck -- 17.89% and 96.51% sit on the same side of 10%.

    So the scan below tracks the four F# lexical states that can contain a `(*`: line comments
    (`//`, `///`) run to end of line, ordinary strings honour `\\` escapes, `@"..."` verbatim
    strings honour `""` as an escaped quote, and `\"\"\"..."\"\"` triple-quoted strings run until
    their closing triple. Character literals are recognised too -- `'"'` occurs in this very tree
    (`SemanticDiff.fs:103`, `RegistryPredicate.fs:40`) and would otherwise open a phantom string --
    and are distinguished from generic type variables (`'a`, `'result`) by requiring the closing
    quote.

    `(*)` IS THE MULTIPLICATION OPERATOR, NOT A COMMENT OPENER. `let mul = (*) 2 3` compiles and
    evaluates to 6; `List.reduce (*)` is ordinary F#. Verified against the compiler itself rather
    than against this file's own model -- `dotnet fsi` prints `val mul: int = 6`. Read as a comment
    opener it runs away exactly like the string case: one `List.reduce (*)` took a 200-line
    implementation to 1 substantive line and a true 5.0% ratio to 1000.0%, suppressing the warning.
    There are no occurrences in this tree today, so it was latent -- which is what `SemanticDiff.fs`
    also was, right up until it swallowed 513 lines.

    THIS SCAN CAN ERR IN BOTH DIRECTIONS, AND EITHER DIRECTION CAN SUPPRESS A WARNING. An earlier
    revision of this docstring claimed the residual "can only undercount". That was wrong twice
    over. An undercount RAISES the ratio when it lands in the implementation -- the `SemanticDiff`
    incident above, which is the very case this function was repaired for. And an unterminated
    verbatim or triple-quoted string OVERCOUNTS, because every following line is read as string
    content and counted. No one-directional safety property is claimed here, because none holds.

    WHAT BOUNDS IT IS A STATE CHECK RATHER THAN AN ARGUMENT. This function reports whether it
    finished outside every construct, and `main` refuses to score a pair where either file did not.
    Every runaway known to this gate leaves the scan mid-construct at end of file, so it is reported
    UNMEASURED rather than silently scored. What remains is a BALANCED mis-lex -- a phantom opener
    later cancelled by a phantom closer -- which is local rather than a runaway. That is a real
    residual and it is stated, not argued away.

    The existence leg counts no lines and is untouched by any of this.

    Returns `(substantive line count, finished in a clean lexical state)`.
    """
    count = 0
    depth = 0  # (* *) nesting
    in_triple = False
    in_verbatim = False

    for raw in text.split("\n"):
        line = raw.strip()
        substantive = False
        i = 0
        n = len(line)

        while i < n:
            if depth:
                if line.startswith("(*", i):
                    depth += 1
                    i += 2
                elif line.startswith("*)", i):
                    depth -= 1
                    i += 2
                else:
                    i += 1
                continue

            if in_triple:
                substantive = True
                if line.startswith('"""', i):
                    in_triple = False
                    i += 3
                else:
                    i += 1
                continue

            if in_verbatim:
                substantive = True
                if line.startswith('""', i):
                    i += 2
                elif line[i] == '"':
                    in_verbatim = False
                    i += 1
                else:
                    i += 1
                continue

            # `///` is contract and counts; `//` is implementation prose and does not. Either way
            # the rest of the line is comment text and holds no delimiter this scan may honour.
            if line.startswith("///", i):
                substantive = True
                break
            if line.startswith("//", i):
                break

            # `(*)` is the multiplication operator and must be consumed before `(*` is read as an
            # opener, or the rest of the file becomes a comment.
            if line.startswith("(*)", i):
                substantive = True
                i += 3
                continue
            if line.startswith("(*", i):
                depth += 1
                i += 2
                continue
            if line.startswith('"""', i):
                in_triple = True
                substantive = True
                i += 3
                continue
            if line.startswith('@"', i):
                in_verbatim = True
                substantive = True
                i += 2
                continue
            if line[i] == '"':
                substantive = True
                i += 1
                while i < n:
                    if line[i] == "\\":
                        i += 2
                        continue
                    if line[i] == '"':
                        i += 1
                        break
                    i += 1
                continue
            if line[i] == "'":
                # A char literal closes; a generic type variable (`'a`, `'result`) does not.
                consumed = _char_literal_length(line, i)
                substantive = True
                i += consumed if consumed else 1
                continue

            if not line[i].isspace():
                substantive = True
            i += 1

        if substantive:
            count += 1
    return count, (depth == 0 and not in_triple and not in_verbatim)


def _char_literal_length(line: str, i: int) -> int:
    """Length of the F# character literal starting at `line[i]`, or 0 if this is not one.

    `'"'` and `'\\n'` are literals; `'a` and `'result` are type variables. Only the closing quote
    tells them apart, which is why this is a lookahead rather than a flag.
    """
    if line.startswith("'\\", i):
        j = i + 2
        while j < len(line) and line[j] != "'":
            j += 1
        return (j - i + 1) if j < len(line) else 0
    if i + 2 < len(line) and line[i + 2] == "'":
        return 3
    return 0


def read(root: str, relative: str) -> str:
    with open(os.path.join(root, relative), encoding="utf-8") as handle:
        return handle.read()


def exemption_findings(root: str, sources: list[str]) -> list[str]:
    """Every way a listed exemption can fail to be a claim this gate could falsify."""
    findings: list[str] = []
    known = set(sources)
    for path in sorted(EXEMPT):
        reason = EXEMPT[path]
        if not reason or not reason.strip():
            findings.append(f"{path}: exempt with no reason written. An exemption is a claim; state it.")
            continue
        if path not in known:
            findings.append(
                f"{path}: listed as an exempt entry point but no such .fs was discovered. "
                "STALE EXEMPTION: delete the entry, or correct the path."
            )
            continue
        try:
            text = read(root, path)
        except (OSError, UnicodeDecodeError) as exc:
            findings.append(f"{path}: listed as exempt but could not be read: {exc}")
            continue
        if ENTRY_POINT_MARKER not in text:
            findings.append(
                f"{path}: listed as an exempt entry point but carries no {ENTRY_POINT_MARKER}. "
                "UNJUSTIFIED EXEMPTION: only a composition root is exempt -- give this file a "
                "sibling .fsi, or remove the entry."
            )
    return findings


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Assert every compiled F# implementation has a signature file, and report signature "
            "files too small to be stating a contract."
        )
    )
    parser.add_argument("--root", default=".", help="repository root to scan (default: .)")
    parser.add_argument(
        "--warn-below",
        type=float,
        default=WARN_RATIO_PERCENT,
        help=f"substantive ratio below which a signature is reported (default: {WARN_RATIO_PERCENT})",
    )
    args = parser.parse_args(argv)
    root = args.root

    present = [r for r in SUBJECT_ROOTS if os.path.isdir(os.path.join(root, r))]
    if not present:
        joined = ", ".join(SUBJECT_ROOTS)
        print(f"NO VERDICT: none of the subject roots ({joined}) exist under {root} -- nothing to scan.")
        return 3

    sources, covered = discover(root)
    if not sources:
        joined = ", ".join(present)
        print(f"NO VERDICT: discovery found no .fs file under {joined}.")
        return 3

    print(
        f"discovered: {len(sources)} .fs file(s) under {', '.join(present)}, "
        f"{len(covered)} with a sibling .fsi, {len(EXEMPT)} exempt entry point(s)"
    )

    findings = exemption_findings(root, sources)

    missing = [p for p in sources if p not in set(covered) and p not in EXEMPT]
    for path in missing:
        findings.append(
            f"{path}: no sibling .fsi. Every compiled F# implementation states its contract in a "
            "signature file; add {name}i, or -- if this is a composition root -- add it to EXEMPT "
            "in this gate with the reason.".replace("{name}", os.path.basename(path))
        )

    # The substance leg. It reads only files that HAVE a signature, so it can neither mask nor
    # duplicate the existence leg above.
    warnings: list[str] = []
    unmeasured: list[str] = []
    unreadable: list[str] = []
    for path in covered:
        signature = path + "i"
        try:
            implementation_text, signature_text = read(root, path), read(root, signature)
        except (OSError, UnicodeDecodeError) as exc:
            unreadable.append(f"{path}: {exc}")
            continue
        implementation, impl_clean = substantive_lines(implementation_text)
        signature_count, sig_clean = substantive_lines(signature_text)
        if not impl_clean or not sig_clean:
            # The scan ended inside a string or a block comment, so its count is a guess about a
            # file it lost its place in. Every runaway this gate knows about lands here, and a
            # runaway moves the ratio far enough to invent or suppress a warning. Say so instead.
            unmeasured.append(
                f"{signature if not sig_clean else path}: the scan ended inside a string or block "
                "comment, so no ratio was computed for this pair."
            )
            continue
        if implementation == 0:
            # No substantive implementation to be a fraction of; a ratio here would divide by zero
            # and would mean nothing if it did not.
            continue
        ratio = 100.0 * signature_count / implementation
        if ratio < args.warn_below:
            warnings.append(
                f"{signature}: {ratio:.1f}% of its implementation's substantive lines "
                f"(below {args.warn_below:g}%). A seam declared but not described -- state what each "
                "exported value promises and refuses."
            )

    if unreadable:
        print("NO VERDICT: a file that had to be read could not be, so the scan is incomplete:")
        for entry in unreadable:
            print(f"  {entry}")
        return 3

    for entry in unmeasured:
        print(f"UNMEASURED  {entry}")

    for warning in warnings:
        print(f"WARN  {warning}")

    if findings:
        print()
        print(f"FAILED -- {len(findings)} finding(s):")
        for finding in findings:
            print(f"  {finding}")
        return 1

    tail = f" {len(warnings)} substance warning(s), which do not fail this gate." if warnings else ""
    print(f"OK: every compiled F# implementation under {', '.join(present)} has a signature file.{tail}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

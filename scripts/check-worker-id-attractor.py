#!/usr/bin/env python3
"""Assert no protocol doc or skill hands a worker an id to copy, or a second way to mint one.

.github#570, epic #266 (the rule nothing asserts). Found while working .github#551.

THE DEFECT THIS CLOSES. #419 found the collision attractor: agents asked to pick a worker id
converge on the same corner of the name space, and an id two workers share is an id the claim lock
cannot separate — `release` drops the other's claim mid-flight, `heartbeat` renews a marker that is
not yours, `say`/`inbox` cross-deliver. This board carried FOUR `finch-*` workers at once, every one
of them lifted from the example that used to sit in the recipe. The attractor is the WORD, not the
suffix: randomising `-a3f` does not help if the next reader still reaches for the bird they just
read.

It has now been removed BY HAND TWICE. #532 took it out of the four SKILL.md files; it grew straight
back in `docs/coordination/parallel-work.md` — the document those skills are a projection OF — and
#551 took it out a second time. Twice removed, twice by hand, because nothing asserts it. #551's own
acceptance criteria were literally two greps in an issue body, and a rule enforced by whoever happens
to remember it is a rule that decays. It had already decayed once, in the window between #532 and
#551.

So: gate it.

WHAT IT ASSERTS, over every Markdown file in the agent-skill roots and in docs/:

  1. NO LITERAL WORKER ID in a copyable position. Both spellings, because the commit-trailer example
     is how one of the two survived #532:

         FSGG_WORKER=w-4f2a91c7            <- a shell assignment
         FSGG-Worker: w-4f2a91c7           <- the git trailer

     A PLACEHOLDER is fine and is the whole point — `<id>`, `<the id claim printed>`, `$FSGG_WORKER`.
     Nobody pastes a placeholder and collides with anybody. A CONCRETE id is a loaded gun on the
     page: the next worker copies the line, and now two workers share a lock.

  2. EXACTLY ONE MINT IDIOM, and it is `eval "$(scripts/fsgg-coord whoami --mint)"`. Any other way
     to conjure an id is a finding:

         - a rival substitution on the right of `FSGG_WORKER=` (a hand-rolled mint);
         - a randomness primitive anywhere in the surface (`od -An`, `/dev/urandom`, `uuidgen`,
           `openssl rand`, `$RANDOM`, `shuf`). There is exactly one sanctioned source of worker-id
           randomness and it lives INSIDE fsgg-coord, so a doc that shows one is, definitionally, a
           second idiom. That is the check #551 wrote as `grep -rn 'od -An -tx1'`.

  3. THE SANCTIONED IDIOM IS STILL TAUGHT. "Exactly one" has a floor as well as a ceiling: a surface
     that no longer shows anyone how to mint an id is a surface whose readers will invent one, which
     is #419 again from the other end.

...and over the ENGINE SOURCE (`src/`, `scripts/`), one more:

  4. THE REMEDY THE TOOL PRINTS RUNS AS PRINTED (#569). Rule 2 says there is exactly one mint idiom
     — but nothing read the TOOL's own strings, so the tool was exempt, and it drifted:

         eval "$(fsgg-coord-engine whoami --mint)"     <- what all five sites printed
         eval "$(scripts/fsgg-coord whoami --mint)"    <- what every doc teaches, and what runs

     `fsgg-coord-engine` is not on PATH. `scripts/fsgg-coord` is the ADR-0034 §4.4 resolver that
     finds the engine and execs it, and it is the only spelling that runs from a plain checkout.

     This is #551's own bug one level down — the tool and the docs teaching two forms of one command
     — except that this fork was a BROKEN one, so it was worse: the divergent idiom at least worked.
     And the surface it broke on has the highest readership of any, because a worker meets it at the
     moment `whoami` warns, before they have read anything else. #551 unified the docs; the tool was
     the last holdout, for the structural reason that a doc-only gate could not see it.

FAILS CLOSED (epic #266). Auditing zero files is a failure to audit, not a clean audit — and so is
auditing files in which the extractor finds no worker-id mention at all, because these documents
demonstrably carry them. If the glob breaks, the gate goes RED, not green.

THIS GATE IS PURE AND OFFLINE, so it never exits 2. It reads the working tree and nothing else: no
API, no network, no credentials. There is no condition under which "try again" is the right advice,
and a gate that can emit a retryable verdict it can never mean is a gate whose exit-code contract
lies. Exit 2 is therefore deliberately absent from the vocabulary rather than reserved.

Usage:
  check-worker-id-attractor.py [--root <dir>] [--surface <dir> ...]
Exit: 0 = no attractor and one mint idiom; 1 = a literal id, a rival mint, or no mint taught at all;
3 = no verdict, PERMANENT — a missing surface directory, or an audit that examined nothing.

"I could not check" must never share an exit code with "I checked, and it's fine" (#266) — nor with
"I checked, and it's broken" (#320).
"""
from __future__ import annotations

import argparse
import re
import sys
import traceback
from pathlib import Path

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# The roots are READ FROM `.agent-skill-roots`, not hardcoded (#517, as skill-union-assert.sh and
# check-recipe-pagination.py do). A root added there is audited without touching this file; a private
# copy of the list would go stale silently, and a root nobody audits is a root the attractor can grow
# back in while this gate reports green.
ROOTS_DECL = ".agent-skill-roots"
FALLBACK_ROOTS = (".claude/skills", ".agents/skills")

# ...plus the protocol docs. THIS IS NOT OPTIONAL: docs/coordination/parallel-work.md is the document
# the skills are a projection of, and it is exactly where the attractor grew back after #532 removed
# it from the skills alone. A gate over the skill roots only would have reported green through the
# entire window that #551 was needed to close.
DOC_SURFACE = ("docs",)

# ...plus the ENGINE SOURCE, because the remedy a worker actually runs is the one the TOOL prints,
# and it is the copy with the highest readership: a worker meets it the moment `whoami` warns, before
# they have read any doc. #569: all five sites printed `eval "$(fsgg-coord-engine whoami --mint)"`,
# and `fsgg-coord-engine` is not on PATH — so the one mint idiom's own remedy was `command not
# found`, and a worker who did exactly as told got nothing. A doc-only gate cannot see that. This is
# rule 2 ("exactly one mint idiom") applied to the surface that was exempt from it, which is why the
# tool was the last holdout after #551 unified the docs.
CODE_SURFACE = ("src", "scripts")
CODE_SUFFIXES = (".fs", ".fsi", ".py", ".sh")

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266).
#
# `check-paths-coherence.py` reads this constant BY AST — no import, no execution — and reds
# `worker-id-attractor.yml` if its `paths:` does not select every entry. Without it, this gate could
# not fire on a change to its own subject: the filter omitted `src/**` for as long as #569 had been
# auditing it, so a PR reintroducing the exact `command not found` regression this gate was written
# to catch — touching only `src/FS.GG.Coord.Cli/Client.fs` — did not run it. Green by absence, on the
# gate whose purpose is closing a #266 fail-open.
#
# IT IS COMPOSED, NEVER RETYPED, and that is the whole reason it can be trusted. A literal list here
# would be a second copy of the surface, free to drift from the constants above the moment one of
# them changes — which is #865 exactly: a hand-maintained source moves the drift upstream and makes
# it authoritative. Built this way, widening CODE_SURFACE widens the trigger, and no one has to
# remember. What a reader must still do is ADD a new surface constant here when they add one above;
# that is the seam this shape does not close, and it is a much narrower one than a free-standing copy.
#
# FALLBACK_ROOTS, not the roots this tree actually declares: the workflow filter is static and the
# roots are read at RUN time from ROOTS_DECL. So the declaration file itself is a subject — a root
# added there changes what this gate audits — and it is listed for exactly that reason.
PATHS_SUBJECT = DOC_SURFACE + CODE_SURFACE + (ROOTS_DECL,) + FALLBACK_ROOTS
# The engine, specifically. Its remedies are the non-vacuity subject below: this program demonstrably
# prints them, so finding none means the extractor is broken, not that the tree is clean.
ENGINE_SURFACE = "src/FS.GG.Coord.Cli"
# Build output carries a COPY of every doc comment (bin|obj/…/*.xml, and .fs the compiler generates).
# It is not a surface anybody edits, so a finding there is one nobody can act on.
CODE_EXCLUDE_DIRS = frozenset(("bin", "obj"))

# THIS GATE ITSELF is exempt from rule 4, and only from rule 4. It is the one file that must NAME the
# broken idiom in order to forbid it: the docstring above quotes `fsgg-coord-engine whoami --mint` as
# the counter-example, and the finding message below builds the sanctioned line from a constant. Both
# read as violations to a scanner and are the opposite — they are the rule being STATED. This is the
# same carve-out `is_literal_trailer` makes for prose ABOUT the trailer: a gate that reds on the
# sentence teaching its own rule is a gate somebody deletes.
#
# Matched by BASENAME, not by `__file__`: the fixture audits a COPY of the shipped tree, and a
# path-identity check would exempt the running gate while red-lighting its own copy — the gate
# passing on the real tree and failing on a verbatim copy of it. Narrow enough: this program prints
# no remedy to any worker, so it has nothing rule 4 protects. The TOOL does, and the tool is not
# exempt — which is the entire point of #569.
SELF_BASENAME = "check-worker-id-attractor.py"

# The ONE sanctioned mint. `fsgg-coord whoami --mint` prints one shell line and nothing else on
# stdout, so `eval "$(...)"` is the whole ritual.
SANCTIONED_MINT = re.compile(r"fsgg-coord\s+whoami\s+--mint")

# A mint remedy a reader can PASTE, captured by the COMMAND it names rather than by the `eval "$(…)"`
# wrapper around it. Keying on the wrapper is the obvious way to write this and it FAILS OPEN: a site
# printing `run: fsgg-coord-engine whoami --mint` — the same `command not found`, minus the eval —
# sails past, and the summary then asserts that N remedies "run as printed" while one of them does
# not. The command is the subject; the wrapper is decoration.
#
# The character class is what keeps this off prose. A command token is `[\w./-]+`, so the forms that
# legitimately mention the mint without naming a command do not match, and are not findings:
#     `whoami --mint` prints one line       <- prose ABOUT the ritual: no command token
#     eval "$(… whoami --mint)"             <- an ellipsis placeholder: `…` is not in the class
#     grep -q 'whoami --mint'               <- a test asserting on the string: quote, not a token
#     "$ENGINE" whoami --mint               <- a test invoking a built binary: quote, not a token
MINT_CALL = re.compile(r"(?P<cmd>[\w./-]+)\s+whoami\s+--mint")

# The one command that RUNS as printed, from a plain checkout, with nothing on PATH: the ADR-0034
# §4.4 resolver, whose whole job is to find the engine and exec it. The engine's own binary
# (`fsgg-coord-engine`) is NOT installed on PATH — that is exactly what #569 got wrong.
RUNNABLE_MINT_CMD = "scripts/fsgg-coord"

# THE ENGINE'S OWN VERBS, READ FROM ITS DISPATCH — never retyped here (#931). A literal list would be a
# second copy of the engine's command set, free to drift the moment somebody adds a verb: the gate would
# then be silent on the newest sites, which are the ones nothing has audited yet. `Options.fs` dispatches
# on `| "<verb>" :: rest ->` and REFUSES anything else (`unknown command: --repo`), so this list is the
# engine's own answer to "what is a verb", not our guess at it.
DISPATCH_SOURCE = "src/FS.GG.Coord.Cli/Options.fs"
DISPATCH_VERB = re.compile(r'^\s*\|\s*"(?P<verb>[a-z][a-z-]*)"\s*::\s*rest\b')

# WHERE A PRINTED COMMAND CAN LIVE. The engine's own source, and only the F# in it: `scripts/` holds CI
# linters whose docstrings DISCUSS commands ("`fsgg-coord landable` is the only sanctioned reader") — prose
# naming a command, which nobody pastes and which must never be a finding.
PRINTED_SURFACE = "src"
PRINTED_SUFFIXES = (".fs", ".fsi")

# THE VERB IS REQUIRED, AND IT IS WHAT KEEPS THIS OFF PROSE. Matching `fsgg-coord\s+\S` instead would read
# "the fsgg-coord shim resolves…" as the verb `shim` and red a correct sentence. Anchoring on the engine's
# real verbs means a command is recognised as a command.
#
# THE COLON DISCRIMINATOR, WHICH IS THE WHOLE REASON THIS CAN BE GATED AT ALL (#931). The engine prints
# 124 `fsgg-coord-engine: <prose>` LOG PREFIXES, and they are not instances — a gate that flagged its own
# log prefix would cry wolf 124 times on a clean tree, and a gate that cries wolf is one somebody deletes.
# `\s+` after the command is the entire separation: a prefix is followed by `:`, which is not whitespace,
# so `fsgg-coord-engine: claim failed` cannot match no matter what word follows the colon. Measured, not
# reasoned: 124 prefixes, 0 findings.
#
# BOTH BROKEN SPELLINGS, because both are `command not found` and the issue's acceptance names both:
# `fsgg-coord-engine` is the engine's apphost, which is on nobody's PATH (#569), and a BARE `fsgg-coord`
# is not on PATH either — measured at 127. Only the resolver, at the path the docs teach, runs.
PRINTED_CMD = r"(?P<cmd>(?:[\w.-]+/)*fsgg-coord(?:-engine)?)"

# The one command that RUNS as printed, from a plain checkout, with nothing on PATH: the ADR-0034 §4.4
# resolver, whose whole job is to find the engine and exec it. Since #931 it resolves from an item
# worktree too — which is what makes this the right answer for EVERY printed command rather than only
# the ones a worker happens to run from the shared checkout.
RUNNABLE_CMD = "scripts/fsgg-coord"

# A placeholder is prose REFERRING to the ritual (`eval "$(... whoami --mint)"` in a doc comment
# about minting), not a line anybody pastes — fine here for the same reason `<id>` is fine above:
# nobody runs a placeholder and gets `command not found`.
#
# Only the ASCII spelling needs listing. The unicode `…` is not in MINT_CALL's command class, so it
# never reaches this check at all — listing it would claim a guard that does no work.
MINT_PLACEHOLDER = frozenset(("...",))

# What an actual worker id looks like: a word, a hyphen, and a hex-ish tail (`w-4f2a91c7`,
# `finch-a3f`). Anything with `<`, `$`, or a backtick in it is a placeholder or a substitution, and
# is the correct thing for a recipe to show.
WORKER_ID = r"[A-Za-z][A-Za-z0-9]*-[0-9a-fA-F]{3,}"

# `FSGG_WORKER=<value>` (optionally `export`ed, optionally quoted) and the `FSGG-Worker: <value>`
# git trailer. Captured separately so a finding can say WHICH spelling leaked.
ASSIGNMENT = re.compile(r"FSGG_WORKER\s*=\s*(?P<q>[\"']?)(?P<value>[^\"'\s]*)")
TRAILER = re.compile(r"FSGG-Worker:[ \t]*(?P<q>[\"']?)(?P<value>[^\"'\s]*)")

# Any mention at all — used only to prove the extractor is not silently seeing nothing.
ANY_MENTION = re.compile(r"FSGG_WORKER|FSGG-Worker")

# What actually MINTS: a command substitution. `$(…)` or a backtick. A bare `$VAR` / `${VAR}`
# expansion does not mint — `FSGG_WORKER=$FSGG_WORKER cmd` forwards an id the worker already holds.
MINTS_SOMETHING = re.compile(r"\$\(|`")

# A second source of randomness is a second mint idiom, whatever it is dressed as.
RIVAL_RANDOMNESS = re.compile(
    r"od\s+-An|/dev/urandom|uuidgen|openssl\s+rand|\$RANDOM\b|\bshuf\b", re.IGNORECASE
)


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def _is_pasteable(value: str) -> bool:
    """A concrete, bare token — not a placeholder, not a substitution, not a backticked reference.

    `<id>`, `$FSGG_WORKER`, `$(…)`, an empty value: all exactly what a recipe SHOULD show. Nobody
    pastes a placeholder and collides with anybody.
    """
    if not value:
        return False
    return value[0] not in "<$`({[" and "$" not in value


def is_literal_assignment(value: str) -> bool:
    """Is the right-hand side of `FSGG_WORKER=` a concrete id?

    ANY bare token is, and the check is deliberately not narrowed to hex-shaped ids. The rule is "no
    id a worker can paste", and `FSGG_WORKER=w-alice` is every bit as pasteable — and every bit as
    collidable — as `FSGG_WORKER=w-4f2a91c7`. #551's acceptance grep (`FSGG_WORKER=[a-z]*-[0-9a-f]`)
    would have missed it; matching that grep's blind spot rather than the rule it was reaching for is
    how a gate ships already-broken. An assignment's RHS is never prose, so there is nothing else it
    could legitimately be.
    """
    return _is_pasteable(value)


def is_literal_trailer(value: str) -> bool:
    """Is the value after `FSGG-Worker:` a concrete id?

    Stricter than the assignment case, because a TRAILER can be followed by prose — "the
    `FSGG-Worker:` trailer that `claim` prints" — and flagging the word "trailer" as a worker id
    would be the gate crying wolf on the very sentence that teaches the rule. So the value must also
    LOOK like an id: it must carry a hyphen, which an English word in this position does not.
    """
    return _is_pasteable(value) and "-" in value


def surface_files(root: Path, surfaces: list[str]) -> list[tuple[Path, str]]:
    files: list[tuple[Path, str]] = []
    for d in surfaces:
        base = root / d
        if not base.is_dir():
            raise GateError(
                f"surface directory '{d}' does not exist under {root} — the glob is broken, and a "
                f"gate that audits a directory it cannot find would report green over anything"
            )
        for p in sorted(base.rglob("*.md")):
            files.append((p, str(p.relative_to(root))))
    if not files:
        raise GateError(
            f"found NO Markdown files under {', '.join(surfaces)}. Examining nothing is a failure "
            f"to audit, not a clean audit (#266)."
        )
    return files


def code_files(root: Path) -> list[tuple[Path, str]]:
    """The source files that can PRINT a mint remedy.

    Unlike the doc surface, a missing directory is not an error: a tree with no engine in it has
    nothing that prints a remedy, so there is genuinely nothing to audit. The fail-closed guard for
    this surface is the engine's own non-vacuity check in main() — this program demonstrably prints
    the remedy, so finding NO remedy in it means the extractor broke.
    """
    files: list[tuple[Path, str]] = []
    for d in CODE_SURFACE:
        base = root / d
        if not base.is_dir():
            continue
        # PRUNE at the directory, rather than rglob-ing everything and discarding: on a built
        # checkout `bin/`/`obj/` hold thousands of artifacts, and rglob("*") would stat and sort
        # every one of them only to throw them away — work that grows with the build output instead
        # of with the source.
        stack = [base]
        found: list[Path] = []
        while stack:
            for p in sorted(stack.pop().iterdir()):
                if p.is_dir():
                    if p.name not in CODE_EXCLUDE_DIRS:
                        stack.append(p)
                elif p.name != SELF_BASENAME:
                    # `scripts/fsgg-coord` is extensionless, and it is the resolver the remedy names.
                    if p.suffix in CODE_SUFFIXES or (d == "scripts" and p.suffix == ""):
                        found.append(p)
        for p in sorted(found):
            files.append((p, str(p.relative_to(root))))
    return files


def engine_verbs(root: Path) -> list[str]:
    """The verbs `Options.fs` dispatches on. Empty when the engine is not in this tree at all."""
    src = root / DISPATCH_SOURCE
    try:
        text = src.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError):
        return []
    verbs = [m.group("verb") for line in text.splitlines() if (m := DISPATCH_VERB.match(line))]
    # LONGEST FIRST. Python's alternation is first-match, not longest-match, so `add|adopt` would match
    # `add` inside `adopt` and leave `opt` — reading a correct `adopt` line as the verb `add` plus junk.
    return sorted(set(verbs), key=len, reverse=True)


# WHAT "THE TOOL PRINTS" MEANS, MECHANICALLY: the command sits inside a double-quoted string literal.
#
# This is the acceptance's own discriminator, and it is load-bearing in BOTH directions. A remedy the tool
# prints lives in a string; prose ABOUT a command lives in a comment. Both spell the command identically,
# so nothing cheaper than "is it in a string?" separates them:
#
#     eprint "  Talk to them:  fsgg-coord say …"      <- PRINTED. A worker pastes it. A finding.
#     // exactly this: `fsgg-coord issues` listed …   <- a COMMENT naming a command. Not a finding.
#
# Flagging the second kind is not a harmless over-reach — it is how this gate dies. #931 is explicit that
# a gate crying wolf on prose gets deleted, and the deleted gate then fails to catch the real thing.
#
# THE COMMENT TAIL GOES FIRST, AND "QUOTED" ALONE WOULD NOT HAVE BEEN ENOUGH. A doc comment quotes the
# ritual it describes — `Identity.fsi` says: /// to pass `--worker` or `eval "$(scripts/fsgg-coord whoami
# --mint)"` — so a rule reading only "is it between quotes?" counts that as something the tool PRINTS. It
# is a comment. Measured: it was the one "printed command" a src/ stripped of every other .fs still
# reported, which is how it was found. Benign there only because the command it names is the compliant
# one; a comment quoting the BROKEN spelling would have been a finding nobody could act on, in a line
# nobody prints — crying wolf, which is how this gate gets deleted.
#
# `//` INSIDE A STRING IS NOT A COMMENT (`"https://…"`), so the scan tracks quote state rather than
# calling `line.split("//")`. Escapes are honoured INSIDE a string (`\"` neither opens nor closes),
# which keeps a printed `--message '\"…\"'` from inverting every span after it.
def printed_spans(line: str) -> list[tuple[int, int]]:
    spans: list[tuple[int, int]] = []
    i = 0
    start: int | None = None
    while i < len(line):
        c = line[i]
        if c == "\\" and start is not None:
            i += 2
            continue
        if c == '"':
            if start is None:
                start = i
            else:
                spans.append((start, i))
                start = None
            i += 1
            continue
        if c == "/" and start is None and line.startswith("//", i):
            break  # a comment tail: nothing after it is printed, whatever it quotes
        i += 1
    return spans


def printed_files(root: Path) -> list[tuple[Path, str]]:
    """The engine's F# source — the surface that PRINTS to a worker."""
    base = root / PRINTED_SURFACE
    if not base.is_dir():
        return []
    out: list[tuple[Path, str]] = []
    stack = [base]
    while stack:
        for p in sorted(stack.pop().iterdir()):
            if p.is_dir():
                if p.name not in CODE_EXCLUDE_DIRS:
                    stack.append(p)
            elif p.suffix in PRINTED_SUFFIXES:
                out.append((p, str(p.relative_to(root))))
    return sorted(out, key=lambda t: t[1])


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="repo root (default: .)")
    ap.add_argument(
        "--surface", action="append", default=None,
        help=f"DOC directory to audit for rules 1-3, repeatable (default: the roots in "
             f"{ROOTS_DECL}, plus docs/). Does not affect rule 4, whose surface is the code that "
             f"prints a remedy ({', '.join(CODE_SURFACE)}/) and is not selectable — a flag that "
             f"could switch the tool's own strings off is a way to green a tree without fixing it.",
    )
    args = ap.parse_args(argv)
    root = Path(args.root).resolve()

    if args.surface:
        surfaces = args.surface
    else:
        decl = root / ROOTS_DECL
        if decl.is_file():
            roots = [
                ln.strip() for ln in decl.read_text(encoding="utf-8").splitlines()
                if ln.strip() and not ln.lstrip().startswith("#")
            ]
            if not roots:
                raise GateError(f"{ROOTS_DECL} declares no roots")
        else:
            roots = list(FALLBACK_ROOTS)
        surfaces = roots + list(DOC_SURFACE)

    findings: list[str] = []
    mentions = 0
    sanctioned = 0

    for path, rel in surface_files(root, surfaces):
        try:
            text = path.read_text(encoding="utf-8")
        except OSError as e:
            raise GateError(f"cannot read {rel}: {e}") from e

        for lineno, line in enumerate(text.splitlines(), 1):
            if ANY_MENTION.search(line):
                mentions += 1
            if SANCTIONED_MINT.search(line):
                sanctioned += 1

            # 1. A concrete id, in either spelling.
            for kind, rx, test in (
                ("FSGG_WORKER=", ASSIGNMENT, is_literal_assignment),
                ("FSGG-Worker:", TRAILER, is_literal_trailer),
            ):
                for m in rx.finditer(line):
                    value = m.group("value")
                    if test(value):
                        findings.append(
                            f"{rel}:{lineno}: `{kind}{value}` is a LITERAL worker id, on a line a "
                            f"worker can paste. Ids picked by reading converge (#419: four `finch-*` "
                            f"workers at once, all from the example on this page), and two workers "
                            f"sharing an id is an id the claim lock cannot separate. Show a "
                            f"placeholder (`<id>`) or the mint, never a usable id."
                        )

            # 2b. A randomness primitive anywhere in the surface IS a second mint idiom: the only
            #     sanctioned source of worker-id randomness lives inside fsgg-coord.
            randomness = RIVAL_RANDOMNESS.search(line)
            if randomness:
                findings.append(
                    f"{rel}:{lineno}: `{randomness.group(0)}` is a hand-rolled source of randomness. "
                    f"The mint is a solved problem with exactly one idiom, "
                    f"`eval \"$(scripts/fsgg-coord whoami --mint)\"`, and it lives inside fsgg-coord "
                    f"so that it cannot drift. A doc that rolls its own is the second idiom #570 "
                    f"exists to keep out."
                )

            # 2a. A rival mint on the right of the assignment.
            #
            # A COMMAND SUBSTITUTION is what mints something. A bare variable expansion does not —
            # `FSGG_WORKER=$FSGG_WORKER cmd` forwards the id a worker already has, which is the
            # opposite of conjuring a new one, and flagging it would be the gate crying wolf at the
            # very idiom the protocol depends on.
            #
            # Skipped when 2b already fired on this line: `FSGG_WORKER="w-$(od -An …)"` is ONE
            # mistake, and reporting it twice trains the reader to skim the gate's output.
            if not randomness and not SANCTIONED_MINT.search(line):
                for m in ASSIGNMENT.finditer(line):
                    if MINTS_SOMETHING.search(m.group("value")):
                        findings.append(
                            f"{rel}:{lineno}: `FSGG_WORKER=` is assigned from a command substitution "
                            f"that is not the sanctioned mint. There is exactly ONE way to mint a "
                            f"worker id — `eval \"$(scripts/fsgg-coord whoami --mint)\"` — and a "
                            f"second idiom is a second thing to keep correct, in a place nobody looks."
                        )

    # 4. THE REMEDY THE TOOL PRINTS MUST RUN AS PRINTED (#569).
    #
    # Rule 2 says there is exactly one mint idiom. The engine was exempt from it — nothing read the
    # tool's own strings — so it drifted to `fsgg-coord-engine whoami --mint`, a command that is on
    # nobody's PATH. Every doc said `scripts/fsgg-coord`; the tool said otherwise, to the one reader
    # who is holding a warning and looking for what to do about it.
    engine_sites = 0
    for path, rel in code_files(root):
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            # Not a text file, or unreadable: it prints nothing a worker pastes.
            continue

        # A path-component prefix, not a string prefix: `startswith` would count a future
        # `src/FS.GG.Coord.Cli.Tests/` as the engine, and remedies in a TEST project would then
        # satisfy the non-vacuity guard below while the real engine printed nothing — the exact
        # vacuity it exists to prevent.
        in_engine = rel == ENGINE_SURFACE or rel.startswith(ENGINE_SURFACE + "/")
        for lineno, line in enumerate(text.splitlines(), 1):
            for m in MINT_CALL.finditer(line):
                if in_engine:
                    engine_sites += 1
                cmd = m.group("cmd")
                if cmd == RUNNABLE_MINT_CMD or cmd in MINT_PLACEHOLDER:
                    continue
                findings.append(
                    f"{rel}:{lineno}: this prints a mint remedy naming `{cmd}`, which is not on "
                    f"PATH — a worker who pastes it, exactly as the tool told them to, gets "
                    f"`command not found` (#569). Print the command the docs teach and the resolver "
                    f"ships: `eval \"$({RUNNABLE_MINT_CMD} whoami --mint)\"`. A remedy that does not "
                    f"run is worse than no remedy: it is read at the one moment the worker is doing "
                    f"as they are told."
                )

    # Fail closed for the code surface, on its real subject. The engine PRINTS these remedies — five
    # of them, at #569. If we found none, the glob, the suffix list, or MINT_CALL is broken, and the
    # leg above is worthless rather than clean. Skipped when there is no engine in the tree at all
    # (a doc-only fixture): nothing there prints a remedy, so auditing nothing is honest.
    if (root / ENGINE_SURFACE).is_dir() and engine_sites == 0:
        raise GateError(
            f"found NO mint remedy in {ENGINE_SURFACE}, which prints them — the extractor is "
            f"broken, not the tree. Examining nothing is a failure to audit, not a clean audit "
            f"(#266)."
        )

    # 5. EVERY COMMAND THE TOOL PRINTS MUST RUN AS PRINTED (#931).
    #
    # Rule 4 is this rule, scoped to ONE verb. It asserts that a MINT remedy names a runnable command —
    # so it audited the file the other sites live in, called it clean, and reported that N remedies "run
    # as printed" while twenty of them did not. That is #266's shape INSIDE the change that closed a #266
    # fail-open, and it is the strongest argument that #569's rule was scoped to the symptom rather than
    # the defect. The defect is not about minting. It is: the tool tells a worker to run something, and
    # the something does not run.
    #
    # BOTH SPELLINGS WERE BROKEN, WHICH IS WHY THIS GATE COULD NOT LAND ALONE (#931). `fsgg-coord-engine
    # <verb>` is 127 (`command not found`). `scripts/fsgg-coord <verb>` was 69 (`no engine found`) from an
    # ITEM WORKTREE — and pnext-item §2 MANDATES a worktree, so that was every worker following the
    # recipe. Rewriting the sites to the other spelling would have traded one broken remedy for a MORE
    # convincing one, because 69 looks like a real diagnostic. The resolver's tier 2b fallback is what
    # makes `scripts/fsgg-coord` true from where its reader actually stands, and only then is there a
    # right answer for this rule to demand. That is the "from where" clause in #931's acceptance, and it
    # is why a rule reading merely "names a real path" would have been satisfied by the 69.
    verbs = engine_verbs(root)
    printed_calls = 0
    if verbs:
        printed_call = re.compile(PRINTED_CMD + r"\s+(?P<verb>" + "|".join(verbs) + r")(?![\w-])")
        for path, rel in printed_files(root):
            try:
                text = path.read_text(encoding="utf-8")
            except (OSError, UnicodeDecodeError):
                continue
            for lineno, line in enumerate(text.splitlines(), 1):
                spans = printed_spans(line)
                for m in printed_call.finditer(line):
                    if not any(a < m.start() < b for a, b in spans):
                        continue  # a comment naming a command, not a remedy anybody pastes
                    printed_calls += 1
                    cmd = m.group("cmd")
                    if cmd == RUNNABLE_CMD:
                        continue
                    findings.append(
                        f"{rel}:{lineno}: this PRINTS `{cmd} {m.group('verb')}`, which is not on PATH — "
                        f"a worker who pastes it, exactly as the tool told them to, gets `command not "
                        f"found` (#931, #569). Print the resolver the docs teach: "
                        f"`{RUNNABLE_CMD} {m.group('verb')}`. It resolves from an item worktree too, "
                        f"which is where pnext-item §2 puts every reader of this line."
                    )

        # Fail closed, on the real subject: the engine demonstrably prints these. Zero means the verb
        # extractor, the surface walk, or the quoted-span reader is broken — and a gate that examines
        # nothing must never be mistaken for a clean tree (#266). Counted over COMPLIANT sites too, so
        # this stays honest once the tree is fixed: it asserts the gate can still SEE, not that the tree
        # is still broken.
        if printed_calls == 0:
            raise GateError(
                f"found NO printed command in {PRINTED_SURFACE}/, which prints them — the extractor is "
                f"broken, not the tree. Examining nothing is a failure to audit, not a clean audit "
                f"(#266)."
            )
    elif (root / DISPATCH_SOURCE).exists():
        # The dispatch is THERE and yielded nothing: its shape changed, and this rule is now silent on
        # every site. That is a broken gate, not a clean tree — say so rather than pass.
        raise GateError(
            f"{DISPATCH_SOURCE} exists but no verbs parsed out of it — DISPATCH_VERB no longer matches "
            f"the engine's dispatch, so rule 5 would audit nothing and report green (#266)."
        )

    # Fail closed: an extractor that sees nothing must not be mistaken for a clean surface. These
    # documents demonstrably discuss the worker id — if we found no mention of it at all, the glob or
    # the regex is broken, and every "ok" above is worthless.
    if mentions == 0:
        raise GateError(
            f"audited the surface and found NO mention of a worker id at all, in any file. These "
            f"documents carry them, so the extractor is broken — examining nothing is a failure to "
            f"audit, not a clean audit (#266)."
        )

    # 3. "Exactly one" has a floor as well as a ceiling.
    if sanctioned == 0:
        findings.append(
            "the surface no longer shows the sanctioned mint "
            "(`eval \"$(scripts/fsgg-coord whoami --mint)\"`) ANYWHERE. A recipe that does not tell a "
            "worker how to mint an id is a recipe whose readers will invent one — which is #419 "
            "again, from the other end."
        )

    if findings:
        for f in findings:
            print(f"::error::check-worker-id-attractor: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} finding(s). Both of this gate's subjects decayed while a human was the "
            f"only thing asserting them: the collision attractor was removed BY HAND twice (#532, "
            f"#551), and the remedy the tool prints drifted to a command that does not run (#569, "
            f"#931). This gate exists so there is no third time of either.",
            file=sys.stderr,
        )
        return FINDING

    # The counts are the POINT, not decoration: each one is a surface this gate proved it can still see.
    # A summary reporting only "ok" cannot tell a clean tree from an extractor that matched nothing —
    # which is the failure every fail-closed guard above exists to make impossible (#266).
    print(
        f"ok: no literal worker id, and exactly one mint idiom — {mentions} worker-id mention(s) "
        f"audited, {sanctioned} showing the sanctioned mint; {engine_sites} printed remedy(s) in "
        f"{ENGINE_SURFACE} run as printed; {printed_calls} printed command(s) across "
        f"{PRINTED_SURFACE}/ ({len(verbs)} engine verb(s)) run as printed."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a literal worker id is shipping
    in a recipe". A crash would therefore be dressed up as a specific, confident, WRONG claim about
    somebody's doc. "I could not check" must never share a code with "I checked, and it's broken"
    (#266, #320).
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-worker-id-attractor: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-worker-id-attractor: the gate crashed, so it has NO VERDICT. This is not "
            "a finding about any document — it is a bug in the gate. See the traceback above.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))

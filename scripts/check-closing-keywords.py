#!/usr/bin/env python3
"""Assert a PR closes exactly the issues its body DECLARES — no more.

.github#643, epic #266 (coherence gates that fail open). Found the hard way on PR #640/#422.

GitHub scans a PR body for `close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved`
followed by an issue reference, and links the two. **It does not parse the sentence.** The string
`does not close #422` contains `close #422`, and the negation is invisible to it.

PR #640 said, in as many words:

    **It does not close #422.** A producer PR that breaks the mirror still cannot be failed [...]

On merge, GitHub closed #422. The Projects auto-workflow then flipped Status to Done, and `release`
reported "board: Done (preserved)" — correctly, since it only resets the claim set. So an open,
unfinished, explicitly-not-done item was closed and stamped Done, and the only reason it was caught
is that the worker re-read the release output and disbelieved it. Left alone, #422 would have sat
closed and Done with its three acceptance criteria unmet.

This is the third face of one coin, and the org has now hit all three:
  - #558 — a keyword in the TITLE, where GitHub never looks: the link is silently MISSING.
  - #616 — an unclosed code fence: a real `Closes #N` is silently VOIDED.
  - #643 — this one: a keyword fires when it was never meant to.
In each, the author's intent and GitHub's parse disagree, and nothing tells the author.

WHAT IT ASSERTS
  **The set of issues GitHub will close must be the set the author DECLARED.** Reading the body
  exactly as GitHub does, every closing reference must sit on a declaration line — a line that is
  nothing but `Closes #N` (`Closes:`/`Fixes`/`Resolves`, one or more refs, optional bullet, optional
  full stop). Any other closing keyword bound to an issue ref is a FINDING.

  A NEGATED reference gets a specifically-worded finding, because that is the case whose author is
  most certain they are safe. But negation is not the test, and it must not be:

WHY NOT JUST DETECT THE NEGATION (the first draft of this gate, and why it was wrong)
  Because a closing keyword does not need a negation to fire when it was not meant to. It only needs
  to be adjacent to an issue number.

  The first draft of this gate flagged only negated references. Run against the body of the very PR
  that introduced it — a body that argues, at length, about how GitHub mis-parses `close #422` — it
  reported "OK: on merge this PR will close #422, #123, #643". The offenders were not negations:

      On merge, GitHub closed #422; the Projects auto-workflow stamped it Done
                       ^^^^^^^^^^^ narrative past tense. GitHub binds it. #422 closes AGAIN.

      ...fires on "Nothing was skipped and this closes #123"
                                               ^^^^^^^^^^^^ an EXAMPLE, quoted in prose.

  So the negation-only gate would have passed the PR that fixes negation, and silently re-closed the
  exact issue whose wrongful closure is the bug (#422). Narrative past tense, a quoted example, a
  deferral (`a follow-up will resolve #N`), a `fixes #N` copied out of a log — none carries a
  negator, and every one of them closes an issue.

  There is no such thing as a harmless closing keyword in a PR body. The only rule that holds is:
  say what you close, on a line that says nothing else, and let the gate check the rest is quiet.

  The cost is real and it is small: prose that narrates a close must write it as code
  (`closed #422`), reword it ("closed that issue"), or use `Refs #422`. That is the same discipline
  the platform already forces on anyone who has been bitten once — this gate just makes it before
  the merge instead of after.

MODELLING GITHUB'S PARSE (the whole gate rests on this, so it is deliberately faithful)
  - Keywords are case-insensitive; an optional colon is tolerated (`Closes: #10`).
  - A reference is `#123`, `GH-123`, `owner/repo#123`, or a full issue/PR URL.
  - CODE IS NOT PROSE. GitHub does not link inside a fenced block, an inline code span, or a
    4-space-INDENTED block, which is precisely what #616 is about. We blank all three out before
    scanning — preserving line count, so reported line numbers stay true.
    The one trap there is a LIST CONTINUATION: it is indented four spaces and is ordinary prose, so
    GitHub parses it and closes the issue. Blanking it would make this gate fail OPEN, which is the
    one direction #266 forbids, so an indented run counts as code only outside a list.
  - An UNCLOSED fence swallows the rest of the body (that IS #616's mechanism), so we model it: no
    closing reference can be found after it. That is not this gate's bug to fix, but pretending
    otherwise would make it lie about what GitHub does.

WHAT COUNTS AS A NEGATION
  Only an explicit verbal negator, within the FIVE tokens immediately preceding the keyword:
  `not`, any `n't` contraction, `never`, `cannot`, `unable to`, `fail(s|ed) to`, `no longer`.

  The window is tight and the vocabulary is small ON PURPOSE. Every real form puts the negator
  within a token or two of the keyword — "does not close #N", "cannot close #N", "could not fix
  #N", "will not resolve #N", "unlike #300, this does not close #N" — while a loose window and a
  fat negator list (`no`, `nothing`, `neither`) starts firing on innocent prose like "Nothing was
  skipped and this closes #123". A gate that cries wolf on correct bodies is one authors learn to
  scroll past, and this gate only gets one chance to be believed.

WHAT IT DELIBERATELY DOES NOT DO
  It does not read the network. The body arrives on stdin or in a file; the workflow feeds it from
  the event payload. So the gate is pure, offline, and testable — and its fixture can prove it says
  NO, which is the only thing that makes a green from it worth anything (#266).

  It does not check the TITLE. GitHub never honours a keyword there (#558), so a title cannot close
  anything, and flagging one would be noise. `done --flip` reads GitHub's own CLOSED_EVENT, which is
  the fix that direction already got.

EXIT CODES  (the contract; the workflow greps nothing)
  0  every closing reference is declared (the declared set, if any, is printed)
  1  FINDING — the body closes an issue it did not declare (negated, narrated, quoted, or deferred)
  3  NO VERDICT (permanent) — no body was supplied, or it could not be read

  There is no exit 2 ("no verdict, retryable"): this gate is pure and offline. It reads text, makes
  no network call, and has no condition a re-run could resolve.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

KEYWORDS = (
    "close", "closes", "closed",
    "fix", "fixes", "fixed",
    "resolve", "resolves", "resolved",
)

# An issue reference GitHub will bind to a keyword: #123, GH-123, owner/repo#123, or a full URL.
REF = r"""(?:
      (?:[A-Za-z0-9._-]+/[A-Za-z0-9._-]+)?\#[0-9]+
    | GH-[0-9]+
    | https?://github\.com/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+/(?:issues|pull)/[0-9]+
)"""

# What may sit between the keyword and the ref: whitespace, or a colon with OPTIONAL whitespace after
# it. `Closes: #1`, `Closes #1` and `Closes:#1` all bind; the colon form is common in changelog-style
# bodies and it must not become a hole. Requiring whitespace unconditionally (`\s*:?\s+`) missed
# `Closes:#1` entirely and reported the body GREEN — a fail-OPEN, which is the one direction this gate
# may never take (#266). A bare `closes#1` is NOT matched: nothing binds it, and nobody writes it.
SEP = r"(?::[ \t]*|\s+)"

# KEYWORD <sep> <ref>.  Case-insensitive. The keyword is captured so we can report it, and its start
# offset is what the negation window is measured back from.
CLOSING = re.compile(
    rf"\b(?P<kw>{'|'.join(KEYWORDS)})\b{SEP}(?P<ref>{REF})",
    re.IGNORECASE | re.VERBOSE,
)

KW_ALT = r"(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)"

# A DECLARATION LINE: a line that is nothing but a closing declaration. This is the ONLY place a
# closing keyword may appear. An optional bullet, then KEYWORD + ref — repeated, KEYWORD AND ALL, for
# every issue — an optional full stop, and nothing else. "and nothing else" is the whole point: it is
# what makes the declaration a deliberate act rather than a sentence that happens to contain the
# right two words in a row.
#
# THE KEYWORD REPEATS. `Closes #1, closes #2` — not `Closes #1, #2`. GitHub binds a keyword to the
# ONE ref that follows it, so in `Closes #1, #2` the `#2` is bound to nothing and is NOT closed. The
# author who writes that has declared two issues and will close one, and neither GitHub nor the board
# will ever mention the other. That is this same bug in the under-closing direction (#558's family),
# and DECLARATION_LOOSE below exists solely to catch it: it is what a declaration line looks like
# when a keyword is missing, so a line matching LOOSE but not DECLARATION has a bare ref in it.
DECLARATION = re.compile(
    rf"""^\s*(?:[-*+]\s+)?
         {KW_ALT}{SEP}{REF}
         (?:\s*(?:,|and)\s*{KW_ALT}{SEP}{REF})*
         \s*\.?\s*$""",
    re.IGNORECASE | re.VERBOSE,
)

DECLARATION_LOOSE = re.compile(
    rf"""^\s*(?:[-*+]\s+)?
         {KW_ALT}{SEP}{REF}
         (?:\s*(?:,|and)\s*(?:{KW_ALT}{SEP})?{REF})*
         \s*\.?\s*$""",
    re.IGNORECASE | re.VERBOSE,
)

BARE_REF = re.compile(REF, re.VERBOSE)

# Explicit verbal negators only — used to WORD the finding, not to decide it. See the docstring:
# negation is the loudest case, not the test.
NEGATORS = {"not", "never", "cannot", "unable", "fails", "failed", "fail"}
NEGATION_WINDOW = 5

TOKEN = re.compile(r"[A-Za-z']+")


class NoVerdict(Exception):
    """The gate could not run against a valid subject. Never a silent pass — see the exit contract."""


def strip_code(text: str) -> str:
    """Blank out fenced blocks and inline code spans, preserving line count and offsets.

    GitHub does not create closing links inside code, so neither do we. Every character we remove is
    replaced by a space (and newlines are kept), so a finding's line/column still points at the real
    body — a lint that points at the wrong line is one workers stop believing.

    An UNCLOSED fence swallows everything after it. That is not a quirk of this implementation; it is
    exactly the mechanism of #616, where an unclosed fence silently VOIDED a real `Closes #N`. We
    model GitHub, including where GitHub is unhelpful.
    """
    out = list(text)
    n = len(text)

    def blank(a: int, b: int) -> None:
        for i in range(a, min(b, n)):
            if out[i] != "\n":
                out[i] = " "

    # Fenced blocks first: ``` or ~~~ at the start of a line (up to 3 leading spaces).
    fence = re.compile(r"(?m)^[ ]{0,3}(?P<f>`{3,}|~{3,})[^\n]*$")
    i = 0
    while True:
        m = fence.search(text, i)
        if not m:
            break
        marker = m.group("f")[0] * 3
        close = re.compile(rf"(?m)^[ ]{{0,3}}{re.escape(marker)}[`~]*[ \t]*$")
        c = close.search(text, m.end())
        end = c.end() if c else n  # unclosed fence: swallows the rest (#616)
        blank(m.start(), end)
        if not c:
            break
        i = c.end()

    # INDENTED code blocks (4 spaces or a tab). GitHub honours these as code too, so a body that
    # shows a command or a quoted log this way binds no keyword — and a gate that scanned them would
    # fire on a body that had done nothing wrong.
    #
    # The guard is a LIST CONTINUATION, and it is the one that matters: the continuation text of a
    # list item is indented four spaces and is ORDINARY PROSE, which GitHub does parse. Blanking it
    # would make this gate fail OPEN — the one direction #266 forbids — so an indented run only
    # counts as code when the paragraph it follows is not a list. Everything else here errs toward
    # scanning: a false finding is a nuisance, a missed close is the bug.
    lines = "".join(out).split("\n")
    offsets, pos = [], 0
    for ln in lines:
        offsets.append(pos)
        pos += len(ln) + 1

    in_list = False
    in_indented_code = False
    prev_blank = True
    for idx, ln in enumerate(lines):
        if not ln.strip():
            prev_blank = True              # a blank line does NOT end an indented block
            continue
        indented = ln.startswith("    ") or ln.startswith("\t")
        if re.match(r"^\s{0,3}(?:[-*+]|\d+[.)])\s", ln):
            in_list = True                 # a list opened; its continuations are prose, not code
            in_indented_code = False
        elif not indented:
            in_list = False                # a plain unindented paragraph ends both contexts
            in_indented_code = False
        # An indented block CONTINUES until an unindented line. Keying only off `prev_blank` blanked
        # the block's FIRST line and scanned every line after it — so a two-line quoted log produced a
        # finding for its second line, which is a false positive on a body that did nothing wrong.
        if indented and not in_list and (prev_blank or in_indented_code):
            blank(offsets[idx], offsets[idx] + len(ln))
            in_indented_code = True
        prev_blank = False

    # Then inline spans, over what survives — so a backtick inside an already-blanked fence cannot
    # open a bogus span.
    partial = "".join(out)
    for m in re.finditer(r"(?<!`)(`+)(?!`).*?(?<!`)\1(?!`)", partial, re.DOTALL):
        blank(m.start(), m.end())

    return "".join(out)


def is_negated(prose: str, kw_start: int) -> str | None:
    """The negator governing the keyword at `kw_start`, or None.

    Looks back over the NEGATION_WINDOW tokens immediately preceding the keyword. Any `n't`
    contraction counts (doesn't, won't, can't, didn't); so does a bare negator from NEGATORS.
    """
    before = prose[:kw_start]
    tokens = TOKEN.findall(before)[-NEGATION_WINDOW:]

    for i, tok in enumerate(reversed(tokens)):
        low = tok.lower()
        if low.endswith("n't") or low in NEGATORS:
            return tok
        # `no longer` is the one negator that needs two tokens. `longer` alone cannot join NEGATORS:
        # it would fire on "this takes longer to close #123", where nothing is negated at all.
        if low == "longer" and i + 1 < len(tokens) and tokens[-(i + 2)].lower() == "no":
            return "no longer"
    return None


def line_of(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def excerpt(text: str, offset: int) -> str:
    """The body's real line at `offset` — from the ORIGINAL text, not the blanked copy."""
    start = text.rfind("\n", 0, offset) + 1
    end = text.find("\n", offset)
    if end == -1:
        end = len(text)
    return text[start:end].strip()


def line_text(prose: str, offset: int) -> str:
    """The line of the BLANKED text containing `offset` — what the declaration test reads."""
    start = prose.rfind("\n", 0, offset) + 1
    end = prose.find("\n", offset)
    if end == -1:
        end = len(prose)
    return prose[start:end]


def audit(body: str) -> tuple[list[str], list[str]]:
    """(findings, declared closing refs) for one PR body.

    A closing reference is legitimate ONLY on a declaration line. Everything else is a finding —
    whether it is negated, narrated, quoted or deferred — because GitHub will close it regardless.
    """
    prose = strip_code(body)
    findings: list[str] = []
    declared: list[str] = []

    # A declaration line that DROPS a ref: `Closes #1, #2` binds only #1. The author declared two and
    # will close one. Reported per line, before the per-match pass, so the finding names every ref
    # GitHub is about to ignore.
    for ln0, line in enumerate(prose.splitlines()):
        if not DECLARATION_LOOSE.match(line) or DECLARATION.match(line):
            continue
        bound = {m.group("ref") for m in CLOSING.finditer(line)}
        bare = [r for r in BARE_REF.findall(line) if r not in bound]
        if bare:
            findings.append(
                f"line {ln0 + 1}: {', '.join(bare)} will NOT be closed — GitHub binds a keyword to "
                f"the ONE ref that follows it, so a bare ref in a declaration is bound to nothing. "
                f"Repeat the keyword: `Closes #1, closes #2`.\n      {line.strip()}"
            )

    for m in CLOSING.finditer(prose):
        ref = m.group("ref")
        kw = m.group("kw")

        if DECLARATION_LOOSE.match(line_text(prose, m.start())):
            declared.append(ref)
            continue

        ln = line_of(body, m.start())
        neg = is_negated(prose, m.start("kw"))
        if neg:
            why = (
                f'`{neg} {kw} {ref}` — GitHub does NOT read the word "{neg}". '
                f"It will CLOSE {ref} on merge."
            )
        else:
            why = (
                f"`{kw} {ref}` is not a declaration — but GitHub does not care where it reads it. "
                f"It will CLOSE {ref} on merge."
            )
        findings.append(f"line {ln}: {why}\n      {excerpt(body, m.start())}")

    return findings, declared


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--body", help="file holding the PR body (default: read stdin)")
    args = ap.parse_args()

    try:
        if args.body:
            p = Path(args.body)
            if not p.is_file():
                raise NoVerdict(f"no body to check: '{args.body}' does not exist")
            body = p.read_text(encoding="utf-8")
        else:
            if sys.stdin.isatty():
                raise NoVerdict("no body supplied on stdin and no --body given")
            body = sys.stdin.read()
    except NoVerdict as e:
        print(f"check-closing-keywords: no verdict: {e}", file=sys.stderr)
        return 3
    except OSError as e:
        print(f"check-closing-keywords: no verdict: cannot read body: {e}", file=sys.stderr)
        return 3

    findings, declared = audit(body)

    if findings:
        print(
            f"check-closing-keywords: {len(findings)} undeclared closing reference(s) — "
            "GitHub WILL close these on merge:\n",
            file=sys.stderr,
        )
        for f in findings:
            print(f"  - {f}", file=sys.stderr)
        print(
            "\nGitHub scans the body for `close|closes|closed|fix|fixes|fixed|resolve|resolves|"
            "resolved`\nfollowed by an issue ref, and links the two. It DOES NOT PARSE THE SENTENCE"
            " (#643):\nnot the word \"not\", not a past tense, not a quotation, not a deferral.\n"
            "\n  IF YOU MEANT TO CLOSE IT — declare it on a line of its own:\n"
            "        Closes #643.\n"
            "\n  IF YOU DID NOT — GitHub must not be able to bind the keyword to the number:\n"
            "        write it as code       `closed #422`\n"
            "        reword it              closed that issue / does NOT complete #422\n"
            "        drop the verb          Refs #422.\n",
            file=sys.stderr,
        )
        return 1

    if declared:
        uniq = sorted(set(declared), key=declared.index)
        print(f"check-closing-keywords: OK — on merge this PR will close: {', '.join(uniq)}")
    else:
        print("check-closing-keywords: OK — this PR body closes no issue.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

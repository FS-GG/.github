#!/usr/bin/env python3
# The docstring is RAW: it quotes regex fragments (`\s+`) and escape sequences (`\n`) as literal
# text. Without the r-prefix Python reads them as escapes — `\n` became a real newline in the
# rendered help, and `\s` raised a SyntaxWarning on every single run, which the workflow captures
# into the gate's output and shows on every PR.
r"""Assert a PR closes exactly the issues its body DECLARES — no more, and none of them silently.

.github#643 and #683, epic #266 (coherence gates that fail open). Found the hard way on PR #640,
then again on PR #681 — the PR that shipped the first version of this gate.

TWO PARSERS READ THIS BODY, AND ONLY ONE OF THEM WAS EVER MODELLED (#683)
  A PR body is consumed twice, by two different parsers with two different rules, and they disagree
  about code:

    1. THE MARKDOWN PARSER, while the PR is open, builds `closingIssuesReferences` — the link
       GitHub shows on the PR itself. It honours markdown: a keyword inside a fence, an inline span
       or an indented block is NOT linked.

    2. THE COMMIT PARSER, on merge, is what actually CLOSES the issue. The squash commit message is
       PLAIN TEXT. Backticks, fences and indentation are ordinary characters in it. Every reference
       the markdown parser skipped, this one binds.

  The first version of this gate modelled (1) faithfully and was blind to (2). So it certified as
  green a body whose merge closed an issue — which is the exact fail-open of #266, inside the gate
  written to close one.

  It did it to its own PR. #681 argued about this bug for forty lines and, following the guidance
  #681 itself shipped ("if you did not mean to close it, write it as code"), wrote every dangerous
  example in backticks. `closingIssuesReferences` for #681 was `#643` and only `#643` — correct. The
  squash commit message contained, in backticks, a closing keyword next to #422, and GitHub closed
  #422 for the second time. Its CLOSED_EVENT closer is the commit, not the PR. Both records are
  accurate; they describe different parsers, and the destructive one is the one nobody modelled.

  So the remedy the recipe taught WAS the bug, and the gate certified the result green.

WHAT IT ASSERTS
  **The set of issues the MERGE will close must be the set the author DECLARED.** Every closing
  reference must sit on a declaration line — a line that is nothing but `Closes #N` (`Closes:`/
  `Fixes`/`Resolves`, one or more refs, optional bullet, optional full stop).

  The scan is over the RAW body, because the destructive parser reads raw text. A closing keyword
  bound to an issue ref is a FINDING **wherever it appears — including inside code**. There is no
  code exemption in the commit parser, so there is none here.

  And the reverse: a declaration the MARKDOWN parser cannot see — one inside a fence, or after an
  unclosed one — still closes the issue on merge but leaves the PR with no recorded link. That is
  #616, and it is also a finding. The two scans together are the only honest model.

WHY NOT JUST DETECT THE NEGATION (the first draft of this gate, and why it was wrong)
  Because a closing keyword does not need a negation to fire when it was not meant to. It only needs
  to be adjacent to an issue number.

  The first draft of this gate flagged only negated references. Run against the body of the very PR
  that introduced it — a body that argues, at length, about how GitHub mis-parses a closing ref — it
  reported "OK: on merge this PR will close #422, #123, #643". The offenders were not negations:
  narrative past tense ("On merge, GitHub closed the issue"), an EXAMPLE quoted in prose, a deferral
  ("a follow-up will resolve it"), a keyword copied out of a log. None carries a negator, and every
  one of them closes an issue.

  There is no such thing as a harmless closing keyword in a PR body. The only rule that holds is:
  say what you close, on a line that says nothing else, and let the gate check the rest is quiet.

THE REMEDIES THAT SURVIVE A SQUASH (and the one that does not)
  Writing the offending string AS CODE does NOT work, and this gate no longer says it does. It is
  the remedy that closed #422 twice. The two that survive, because they deny the parser the binding
  rather than dressing it up:

    - REWORD THE VERB.  "does NOT complete", "addresses", "supersedes", "re-closed that issue".
                        GitHub scans for a fixed keyword list; a verb outside it binds nothing.
    - DROP THE VERB.    `Refs #422.`  A bare reference is a link, not a close.

  A third, when you must quote a body verbatim: BREAK THE ADJACENCY — quote the number without its
  `#` (`closed 422`). What binds is a keyword followed by whitespace and then a ref, so removing the
  ref removes the binding.

  DO NOT reach for a NEWLINE to break it. `closes\n#123` still binds: the separator is `\s+`, and a
  newline is whitespace — to GitHub and to this gate alike. An earlier draft of this docstring
  offered "put them on different lines" as a remedy, which would have failed this gate on a body it
  had just told the author to write. Fixture leg 10f pins it.

MODELLING THE PARSERS (the whole gate rests on this, so it is deliberately faithful)
  - Keywords are case-insensitive; an optional colon is tolerated (`Closes: #10`).
  - A reference is `#123`, `GH-123`, `owner/repo#123`, or a full issue/PR URL.
  - The COMMIT parser has no notion of code. We scan the body raw. This is the scan that decides
    what closes, and it is deliberately the permissive one: it is a superset of the markdown scan,
    so it cannot fail open relative to it.
  - The MARKDOWN parser skips fenced blocks, inline spans and 4-space-INDENTED blocks; an UNCLOSED
    fence swallows the rest of the body (#616's mechanism). `strip_code` models exactly that, and it
    is used for ONE question only: will the PR record a link for what the author declared?
    The trap there is a LIST CONTINUATION — indented four spaces, but ordinary prose, which markdown
    parses — so an indented run counts as code only outside a list.

WHAT COUNTS AS A NEGATION
  Only an explicit verbal negator, within the FIVE tokens immediately preceding the keyword:
  `not`, any `n't` contraction, `never`, `cannot`, `unable to`, `fail(s|ed) to`, `no longer`.

  Negation is used to WORD the finding, never to decide it — the negated author is the one most
  certain they are safe, so their finding names the negator back to them. The window is tight and
  the vocabulary small on purpose: a loose window starts firing on innocent prose, and a gate that
  cries wolf is one authors learn to scroll past.

WHAT IT DELIBERATELY DOES NOT DO
  It does not read the network. The body arrives on stdin or in a file; the workflow feeds it from
  the event payload. So the gate is pure, offline, and testable — and its fixture can prove it says
  NO, which is the only thing that makes a green from it worth anything (#266).

  It does not check the TITLE. GitHub never honours a keyword there (#558), so a title cannot close
  anything, and flagging one would be noise.

  IT DOES NOT READ THE BRANCH'S COMMIT MESSAGES, and under this repo's squash settings
  (`squash_merge_commit_message: COMMIT_MESSAGES`) those are literally what becomes the squash
  message. The gate reads the PR BODY as a proxy for them, which is sound here only because the
  recipe builds the body FROM the commit body. If the two are edited apart, a closing keyword can
  reach the commit without ever appearing in the body this gate sees. That residual hole is #684.

EXIT CODES  (the contract; the workflow greps nothing)
  0  every closing reference is declared (the declared set, if any, is printed)
  1  FINDING — the merge closes an issue the body did not declare, or declares one it will not link
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
    """What the MARKDOWN parser sees: code blanked out, line count and offsets preserved.

    This models parser (1) in the docstring — the one that builds `closingIssuesReferences`, the link
    shown on the PR. It does NOT model what closes the issue. Nothing here may be used to excuse a
    keyword from the raw scan: that was the #683 bug, and this function's output is now consulted for
    exactly one question — will the PR record a link for what the author declared?

    Every character removed is replaced by a space (and newlines are kept), so an offset into this
    string is an offset into the original body, and a finding's line still points at the real line.

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

    Two parsers, both modelled (#683):

      - The scan that decides what CLOSES is over the RAW body, because the squash commit message is
        plain text. A closing reference is legitimate ONLY on a declaration line; everything else is
        a finding — negated, narrated, quoted, deferred, OR IN CODE. Backticks are not a defence.

      - The scan that decides what the PR LINKS is over `strip_code(body)`. A declaration the
        markdown parser cannot see still closes the issue on merge, but the PR records no link for
        it (#616). That is a finding too, in the opposite direction.
    """
    linkable = strip_code(body)  # what the markdown parser can still see
    findings: list[str] = []
    declared: list[str] = []

    # A declaration line that DROPS a ref: `Closes #1, #2` binds only #1. The author declared two and
    # will close one. Reported per line, before the per-match pass, so the finding names every ref
    # GitHub is about to ignore. Over the RAW body: both parsers bind left-to-right the same way.
    for ln0, line in enumerate(body.splitlines()):
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

    for m in CLOSING.finditer(body):
        ref = m.group("ref")
        kw = m.group("kw")
        ln = line_of(body, m.start())

        # Was this occurrence blanked by the markdown parser — i.e. is it inside code? The offsets of
        # `linkable` line up with `body` exactly (strip_code preserves them), so this is a direct read.
        in_code = linkable[m.start() : m.end()] != body[m.start() : m.end()]

        if DECLARATION_LOOSE.match(line_text(body, m.start())):
            declared.append(ref)
            if in_code:
                # The commit still closes it. The PR just never says so.
                findings.append(
                    f"line {ln}: `{kw} {ref}` is a declaration INSIDE CODE. The squash commit will "
                    f"still CLOSE {ref} — a commit message is plain text — but the markdown parser "
                    f"skips code, so the PR will record no link for it (closingIssuesReferences will "
                    f"be empty). Move the declaration out of the code block, onto a line of its own."
                    f"\n      {excerpt(body, m.start())}"
                )
            continue

        neg = is_negated(body, m.start("kw"))
        if in_code:
            why = (
                f"`{kw} {ref}` is in CODE, and code is NOT a defence. The squash commit message is "
                f"plain text: backticks, fences and indentation are ordinary characters in it, so "
                f"GitHub WILL CLOSE {ref} on merge (#683). Reword the verb, or drop it (`Refs {ref}`)."
            )
        elif neg:
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
            "\nGitHub scans for `close|closes|closed|fix|fixes|fixed|resolve|resolves|resolved`"
            "\nfollowed by an issue ref, and links the two. It DOES NOT PARSE THE SENTENCE (#643):"
            "\nnot the word \"not\", not a past tense, not a quotation, not a deferral."
            "\n\nAND CODE IS NOT A DEFENCE (#683). The markdown parser skips code, but the thing that"
            "\nCLOSES the issue is the squash COMMIT MESSAGE, and that is plain text — backticks and"
            "\nfences are ordinary characters in it. A keyword in a fence closes the issue just as"
            "\ndead as one in prose. This is how #422 got closed a SECOND time, by the PR that"
            "\nshipped the gate against closing it.\n"
            "\n  IF YOU MEANT TO CLOSE IT — declare it on a line of its own, nothing else on it:\n"
            "        Closes #643.\n"
            "\n  IF YOU DID NOT — deny the parser the binding. Dressing it up does not work:\n"
            "        reword the verb        does NOT complete / addresses / supersedes\n"
            "        drop the verb          Refs #422.\n"
            "        break the adjacency    quote the number without its `#`\n",
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

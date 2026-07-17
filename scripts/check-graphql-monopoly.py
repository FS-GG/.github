#!/usr/bin/env python3
"""The coordination client is the org's ONLY GraphQL principal — assert it (FS-GG/.github#587).

GitHub's GraphQL primary limit is 5,000 points/hour and it is SHARED BY THE WHOLE FLEET: N agents
authenticate as one account. Five workers looping `take` drained it in ~15 minutes (#418). And
`docs/coordination/graphql-budget.md` is unambiguous about what running out actually does:

    "When it runs out, it takes the WRITES with it — which is how the board starts lying. ... The
     protocol's failure mode is load-dependent: the more you fan out, the more the board lies — and
     fanning out is the point of the protocol."

`fsgg-coord` is the one thing that can meter that budget, cache against it, and queue behind it. A
recipe that reaches PAST the client is an unmetered principal on a shared budget whose exhaustion
silently corrupts the board. This is not hypothetical:

  * #528 — pnext-item §5 was GraphQL-only, so a worker who followed the recipe could not land the
    work they had just finished.
  * #538 — check-board §3 resolved blockers over GraphQL: it drained the very budget it needed to
    do its job.

AN ALLOW-LIST, NOT A DENY-LIST (FS-GG/.github#1161, D3 of #1158)

This gate polices an OPEN-ENDED space — any shell line a worker might copy — and for its whole life
it did so with a CLOSED enumeration of banned `gh` subcommands. That is a fail-open by construction:
a real violation the list forgot to name passes GREEN. It forgot two, and both spend the budget:
`gh issue comment` and `gh pr comment` (the `## Response` a cross-repo recipe posts, the note a worker
leaves on an item) were nowhere in the deny-list, so the checker written to keep the fleet off GraphQL
waved through the single most common write a recipe makes. A backslash-CONTINUED `gh api graphql`
slipped the one-line regex the same way.

So the subject is INVERTED. Every `gh` invocation on a runnable fenced line is a violation UNLESS it
is on the sanctioned allow-list — the small, closed set of `gh` calls that demonstrably do NOT spend
the shared user GraphQL budget:

    gh api <rest-path>   REST — metered on REST's own budget, not this one. `gh api graphql` is NOT
                         this: it is the raw GraphQL endpoint, and it is refused.
    gh auth …            the OAuth/device flow; touches no board budget.
    gh run …             the Actions REST API (re-run, view a run); REST, not GraphQL.

Anything else — `gh project`, EVERY `gh issue`/`gh pr` subcommand (`comment` included), `gh api
graphql` — spends the budget and is refused. The allow-list is enumerated ONCE, here, and a new
budget-spending `gh` verb is caught the day it appears rather than the day someone remembers to add
it to a deny-list (#724: "a fifth copy is not discouraged; it is unwritable").

WHAT THIS ASSERTS, AND WHAT IT DELIBERATELY DOES NOT

A worker copies FENCED CODE. Prose that *warns you off* a command is the opposite of a violation —
`graphql-budget.md`'s cost table exists precisely to say "never use `gh project item-list`", and a
checker that flagged it would be demanding the docs stop teaching the rule they teach.

So the subject is a RUNNABLE LINE INSIDE A FENCE, and nothing else. That is the same fence-awareness
`Paths:` parsing already needs (#277), for the same reason: a declaration is a line you wrote as one.
A backslash-continued command is ONE runnable line, and is joined before it is judged, so a `gh`
verb split across a continuation cannot hide from the allow-list.

An exempt block is opted out EXPLICITLY, on the line before it:

    <!-- graphql-monopoly: exempt — <why> -->

Used for one-time board PROVISIONING (`gh project create` / `field-create`), which a human runs once
with admin rights and no worker ever executes. Explicit, greppable, reviewable — never inferred.

EXIT CODES
  0  clean
  1  findings
  3  NO_VERDICT — the extractor found no subjects at all. These files demonstrably contain fenced
     shell, so silence means the extractor broke, not that the tree is clean. An unverifiable
     subject must not report green (epic #266). This is the guard the whole repo is built on.
"""

import re
import sys
from pathlib import Path

NO_VERDICT_PERMANENT = 3

# Where a WORKER reads its instructions. Workflows are deliberately NOT here: a GitHub Actions run
# authenticates as GITHUB_TOKEN or an App installation, which has its OWN rate limit — it does not
# spend the workers' shared user budget. The rule is about the fleet's budget, not about GraphQL.
SURFACES = [
    ".claude/skills",
    ".agents/skills",
    "docs/coordination",
]

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266). `check-paths-coherence.py`
# reads this BY AST and reds `graphql-monopoly.yml` if its `paths:` does not select every entry.
#
# This one is already correct, and it is declared anyway — a rule only ever exercised on the
# workflows that violate it is a rule nobody can trust, because nothing proves it can say "yes". It
# is the SURFACES list itself, not a copy of it: the thing this gate walks is the thing the filter
# is checked against, so widening one widens the other.
PATHS_SUBJECT = SURFACES

# THE ALLOW-LIST. A `gh` invocation whose subcommand is NOT one of these spends the shared GraphQL
# budget and is a finding. Matched against the text immediately after `gh ` on a (joined) fenced line.
#   api <rest-path>   REST, metered on REST's own budget — but `gh api graphql` is the GraphQL
#                     endpoint and is NOT sanctioned (the negative lookahead excludes it).
#   auth …            OAuth/device flow; no board budget.
#   run …             the Actions REST API.
SANCTIONED = re.compile(r"(?:auth|run)\b|api\b(?!\s+graphql\b)")

# The `gh` CLI, at a word boundary. `github.com` and `fsgg-coord` do not contain the token `gh `.
GH = re.compile(r"\bgh\s+")

EXEMPT = re.compile(r"<!--\s*graphql-monopoly:\s*exempt\b")
FENCE = re.compile(r"^\s*```")

# A fence inside a BLOCKQUOTE is still a fence, and a worker still copies out of it. The first cut of
# this checker matched `^\s*```` and therefore could not see `> ```sh` — and `docs/coordination/
# README.md` has a blockquoted provisioning block, so the gate reported GREEN over a real subject on
# its very first run. That is epic #266's shape, inside the gate written to enforce #266's rule.
# Strip the quote markers before deciding anything.
QUOTE = re.compile(r"^(\s*>\s?)+")


def unquote(line: str) -> str:
    return QUOTE.sub("", line)


def gh_violation(code: str):
    """The first unsanctioned `gh` invocation on a line of runnable code, or None.

    Returns the matched `gh …` fragment (up to ~2 tokens) for the message. A REST `gh api`, `gh auth`
    or `gh run` is sanctioned; every other `gh` — `gh project`, any `gh issue`/`gh pr` INCLUDING
    `comment`, and `gh api graphql` — is a budget spender.
    """
    for m in GH.finditer(code):
        rest = code[m.end():]
        if SANCTIONED.match(rest):
            continue
        verb = " ".join(rest.split()[:2]) or "?"
        return f"gh {verb}"
    return None


def logical_lines(lines, start):
    """Join `\\`-continued physical lines starting at index `start` into one logical line.

    Stops at a fence close (which is never part of a continued command). Returns
    (joined_code, physical_count) — physical_count is how many source lines were consumed, so the
    caller advances past them AND counts each as an examined subject.
    """
    parts = []
    i = start
    while i < len(lines):
        line = unquote(lines[i])
        if i != start and FENCE.match(line):
            break
        parts.append(line)
        if not line.rstrip().endswith("\\"):
            break
        i += 1
    joined = " ".join(p.rstrip().rstrip("\\").strip() for p in parts)
    return joined, len(parts)


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    root = Path(__file__).resolve().parent.parent
    if "--root" in argv:
        root = Path(argv[argv.index("--root") + 1]).resolve()
    findings = []
    subjects = 0
    files = 0

    for surface in SURFACES:
        base = root / surface
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*.md")):
            files += 1
            lines = path.read_text(encoding="utf-8").splitlines()
            in_fence = False
            fence_exempt = False
            pending_exempt = False
            i = 0
            while i < len(lines):
                raw = lines[i]
                line = unquote(raw)
                if EXEMPT.search(line):
                    pending_exempt = True
                    i += 1
                    continue
                if FENCE.match(line):
                    if in_fence:
                        in_fence, fence_exempt = False, False
                    else:
                        in_fence, fence_exempt = True, pending_exempt
                    pending_exempt = False
                    i += 1
                    continue
                if pending_exempt and line.strip():
                    pending_exempt = False
                if not in_fence:
                    i += 1
                    continue

                # A runnable fenced line — possibly `\`-continued. Judge the JOINED command, but
                # count and consume every physical line it spanned.
                joined, span = logical_lines(lines, i)
                subjects += span
                if not fence_exempt:
                    code = joined.split("#", 1)[0]
                    hit = gh_violation(code)
                    if hit:
                        rel = path.relative_to(root)
                        findings.append((rel, i + 1, line.strip(), hit))
                i += span

    # NON-VACUITY. These surfaces demonstrably carry fenced shell; if we extracted none, the
    # extractor is broken and a green verdict would be a lie about a tree we never read (#266).
    if files == 0 or subjects == 0:
        print(
            f"::error::check-graphql-monopoly: found {files} file(s) and {subjects} fenced line(s) — "
            "the extractor reached nothing. These surfaces DO carry fenced shell, so this is a broken "
            "checker, not a clean tree. Refusing to report green (NO_VERDICT, epic #266).",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT

    if not findings:
        print(
            f"ok: the coordination client is the only GraphQL principal — "
            f"{subjects} fenced line(s) across {files} worker-facing file(s), no direct GraphQL."
        )
        return 0

    for rel, n, line, hit in findings:
        print(
            f"::error file={rel},line={n}::check-graphql-monopoly: `{hit}` is not on the sanctioned "
            f"allow-list (REST `gh api <path>`, `gh auth`, `gh run`), so it spends the SHARED GraphQL "
            f"budget (5,000 pts/hr across the whole fleet, #418) from a recipe a worker copies and "
            f"runs. Going around `fsgg-coord` means nothing meters it, caches it, or queues it when "
            f"the budget is gone — and an exhausted budget takes the board WRITES with it, so the "
            f"board starts lying (#528, #538). Use `fsgg-coord`, or `gh api <rest-path>`.\n"
            f"    {line}",
            file=sys.stderr,
        )

    print(
        f"\n{len(findings)} finding(s). The client is the only thing that can see the budget the "
        f"whole fleet is spending; a recipe that reaches past it is an unmetered principal on it.\n"
        f"If a block is genuinely exempt (one-time board provisioning, run once by a human with "
        f"admin rights), mark it on the line before the fence:\n"
        f"    <!-- graphql-monopoly: exempt — one-time board provisioning, never run by a worker -->",
        file=sys.stderr,
    )
    return 1


if __name__ == "__main__":
    sys.exit(main())

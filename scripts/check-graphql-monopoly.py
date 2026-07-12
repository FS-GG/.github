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

WHAT THIS ASSERTS, AND WHAT IT DELIBERATELY DOES NOT

A worker copies FENCED CODE. Prose that *warns you off* a command is the opposite of a violation —
`graphql-budget.md`'s cost table exists precisely to say "never use `gh project item-list`", and a
checker that flagged it would be demanding the docs stop teaching the rule they teach.

So the subject is a RUNNABLE LINE INSIDE A FENCE, and nothing else. That is the same fence-awareness
`Paths:` parsing already needs (#277), for the same reason: a declaration is a line you wrote as one.

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

# Commands that spend the shared GraphQL budget. `gh` prefers GraphQL for all of these.
#   gh api graphql   — the raw thing
#   gh project …     — Projects v2 is GraphQL-only (`item-list --limit 5` costs SIX points)
#   gh issue …       — view/list/edit are 2–4 pts each; `fsgg-coord issues` is REST + ETag, i.e. FREE
#   gh pr …          — every `gh pr` subcommand is GraphQL (#528: the merge a worker cannot perform)
BANNED = [
    (re.compile(r"\bgh\s+api\s+graphql\b"), "gh api graphql", "use `fsgg-coord`, or `gh api <rest-path>`"),
    (re.compile(r"\bgh\s+project\s+item-add\b"), "gh project item-add", "`fsgg-coord add <issue>`"),
    (re.compile(r"\bgh\s+project\s+item-list\b"), "gh project item-list", "`fsgg-coord ready` / `next` — item-list costs 6 pts to read FIVE items"),
    (re.compile(r"\bgh\s+project\s+item-edit\b"), "gh project item-edit", "`fsgg-coord set-field`"),
    (re.compile(r"\bgh\s+project\b"), "gh project", "`fsgg-coord` (Projects v2 is GraphQL-only)"),
    (re.compile(r"\bgh\s+issue\s+(view|list)\b"), "gh issue view/list", "`fsgg-coord issues <repo>` — REST + ETag, 0 pts"),
    (re.compile(r"\bgh\s+issue\s+edit\b"), "gh issue edit", "`gh api -X PATCH repos/<o>/<r>/issues/<n>` (REST)"),
    (re.compile(r"\bgh\s+issue\s+create\b"), "gh issue create", "`gh api -X POST repos/<o>/<r>/issues` (REST)"),
    (re.compile(r"\bgh\s+pr\s+(create|merge|edit|view|list|checks|close|ready|review)\b"), "gh pr …", "`gh api … repos/<o>/<r>/pulls…` (REST) — every `gh pr` subcommand is GraphQL (#528, #564)"),
]

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
            for n, raw in enumerate(lines, 1):
                line = unquote(raw)
                if EXEMPT.search(line):
                    pending_exempt = True
                    continue
                if FENCE.match(line):
                    if in_fence:
                        in_fence, fence_exempt = False, False
                    else:
                        in_fence, fence_exempt = True, pending_exempt
                    pending_exempt = False
                    continue
                if pending_exempt and line.strip():
                    pending_exempt = False
                if not in_fence:
                    continue
                subjects += 1
                if fence_exempt:
                    continue
                code = line.split("#", 1)[0]
                for pat, label, remedy in BANNED:
                    if pat.search(code):
                        rel = path.relative_to(root)
                        findings.append((rel, n, line.strip(), label, remedy))
                        break

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

    for rel, n, line, label, remedy in findings:
        print(
            f"::error file={rel},line={n}::check-graphql-monopoly: `{label}` spends the SHARED "
            f"GraphQL budget (5,000 pts/hr across the whole fleet, #418) from a recipe a worker "
            f"copies and runs. Going around `fsgg-coord` means nothing meters it, caches it, or "
            f"queues it when the budget is gone — and an exhausted budget takes the board WRITES "
            f"with it, so the board starts lying (#528, #538). Use {remedy}.\n"
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

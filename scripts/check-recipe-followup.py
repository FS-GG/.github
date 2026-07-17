#!/usr/bin/env python3
"""Assert no recipe HAND-ROLLS the follow-up queue. It must call `fsgg-coord followup`.

.github#1073 (the remaining half of #1063), epic #266 (stateful logic in prose that nothing executes).

WHY A GATE, AND NOT JUST A MIGRATION

`/pnext-item` §4 case 2 is a promise — *"I can fix this, just not in THIS PR; take it next."* The queue
that keeps the promise was, for one commit, TEN LINES OF SHELL in the recipe: an `echo >>` to append, a
`grep -m1` to peek, a `sed -i` to pop, a `printf >>` to requeue. #1063's complaint is not that the queue
was wrong — it is that the queue lived at *"the wrong altitude for queue state"*, in prose that nothing
executes.

That is #724's argument, one verb over. The merge-gate rollup was wrong four times in four copies because
nothing executes a recipe, so nothing tests one. #1061's queue was wrong FOUR ways on first writing — a
shared default path under a fan-out, control flow in the comments and not the shell, a pop that spent the
ref before the take (so an `EX_RATE` dropped the promise), and a hardcoded owner re-arming the bare-ref
trap — and all four were caught by one hand review. A one-off manual drive is not a gate.

So the queue moved into `fsgg-coord followup` (its own module, `.fsi`, and legs), and the recipe now
CALLS it. This gate is what stops a FIFTH copy: the logic lives in one tested place, and a recipe may
only NAME it.

AND IT IS WHAT MAKES A STALE RECIPE HARMLESS. A SKILL.md is copied into an agent's context at session
start and never refreshed, while N parallel workers merge protocol fixes all day. A recipe that hand-rolls
the queue carries whichever of #1061's four bugs its snapshot froze; a recipe that CALLS the verb reads
the CURRENT one off disk at run time. The prose may drift; the behaviour cannot (#609's "an import cannot
drift by construction", applied to the protocol).

WHAT IS FLAGGED

Inside a ```sh fence in a recipe (or a workflow `run:` block), a line that names the RAW QUEUE FILE:

    $HOME/.fsgg-followups-<worker>      the shell queue's path
    $FSGG_FOLLOWUPS / ${FSGG_FOLLOWUPS} the env override that pointed at it

...is hand-rolling the queue — you cannot append/peek/pop it without naming its file — and is refused.
`fsgg-coord followup add|peek|pop|list` is the only sanctioned interface, and it owns the path so the
recipe never has to.

WHAT IS NOT

  * PROSE. The docs must be able to DESCRIBE the old idiom and why it was retired — in code spans and
    tables — or the lesson cannot be written down. Only ```sh fences (and workflow `run:` blocks) are
    scanned.
  * The tool itself (scripts/fsgg-coord) and its tests: that IS the one implementation.
  * The engine's OWN queue path (`<cache>/followups/<worker>.txt`) — a different string, internal to
    `Followups.fs`, and never written into a recipe.
  * A fence marked `<!-- followup-exempt: why -->` on the line before it (a workflow `run:` block uses
    `# followup-exempt:`, since `<!--` is a syntax error in shell). There is no legitimate use today; it
    exists so a future need is a DELIBERATE, reviewed act with a reason attached, rather than a reason to
    delete this gate — which is how gates die (#737).

This mirrors `check-recipe-landable.py` deliberately: same shape, same escape hatch, same fail-closed
"an empty subject is a finding, not a pass". Two gates that read the same way are two a reviewer can hold
at once.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import yaml

# The recipes this rule governs. A SKILL.md is the agent-facing, copied-not-imported artifact.
ROOTS = (".claude/skills", ".agents/skills", "docs/coordination")

# The workflows it governs, parsed not fence-scanned (a workflow's shell lives in `run:` blocks, and its
# comments are prose that must stay legal) — the same split check-recipe-landable.py draws for #737.
WORKFLOW_ROOT = ".github/workflows"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (epic #266). `check-paths-coherence.py` reads this
# BY AST and reds `recipe-followup.yml` if its `paths:` does not select every entry. Already correct, and
# declared anyway — a rule only ever exercised on violators is one nobody can trust.
PATHS_SUBJECT = ROOTS + (WORKFLOW_ROOT,)

# The fence may be INDENTED — a ```sh fence nested under a list item carries leading whitespace, and
# anchoring at column 0 left every list-nested fence unscanned. `\s*` allows the indent; a blockquoted
# `> ```sh` still does not match, because a `>` is prose (where the docs describe the retired idiom).
FENCE = re.compile(r"^\s*```(\w*)\s*$")

# The escape hatch, in the comment syntax of the thing being scanned — HTML comment for markdown, `#`
# for a shell `run:` block, where `<!--` is a syntax error rather than a comment.
EXEMPT = re.compile(r"<!--\s*followup-exempt:")
EXEMPT_SH = re.compile(r"#\s*followup-exempt:")

# THE RAW QUEUE FILE — the one string a hand-rolled queue cannot avoid. Matched loosely: any `$HOME/`
# prefix, any `-<worker>` suffix, and the env override in either `$FSGG_FOLLOWUPS` or `${FSGG_FOLLOWUPS}`
# spelling. The engine's own `followups/<worker>.txt` under the cache root does NOT match `.fsgg-followups`.
BANNED = (
    (re.compile(r"\.fsgg-followups"), "the raw follow-up queue file (`$HOME/.fsgg-followups-<worker>`)"),
    (re.compile(r"FSGG_FOLLOWUPS"), "the raw follow-up queue path (`$FSGG_FOLLOWUPS`)"),
)

REMEDY = (
    "call the verb instead — it OWNS the queue file, so the recipe never names it:\n"
    "        scripts/fsgg-coord followup add <ref>   # append a promise (refuses a bare ref)\n"
    "        scripts/fsgg-coord followup pop          # head ref on stdout, removed atomically; exit 5 = empty\n"
    "    It keys the file on the resolved worker id (no shared file to race), stores refs qualified,\n"
    "    and pops atomically — the four bugs #1061's shell shipped. Do not re-derive it (#1063/#724)."
)


def scan(path: Path) -> list[str]:
    """Findings for one recipe: the banned queue path inside a ```sh fence."""
    findings: list[str] = []
    lines = path.read_text(encoding="utf-8").splitlines()
    in_sh = False
    exempt = False
    for i, line in enumerate(lines, start=1):
        m = FENCE.match(line)
        if m:
            if in_sh:
                in_sh = False
            else:
                in_sh = m.group(1) in ("sh", "bash", "shell")
                prev = next((l for l in reversed(lines[: i - 1]) if l.strip()), "")
                exempt = bool(EXEMPT.search(prev))
            continue
        if not in_sh or exempt:
            continue
        for pat, what in BANNED:
            if pat.search(line):
                findings.append(
                    f"{path}:{i}: a recipe must not hand-roll the follow-up queue — this names {what}.\n"
                    f"    {line.strip()}\n"
                    f"    {REMEDY}"
                )
                break
    return findings


def scan_workflow(path: Path) -> list[str]:
    """Findings for one workflow: the banned queue path inside `jobs.*.steps[*].run`.

    PARSED, not regexed over raw text — a workflow's comments are prose, and the YAML parser drops them,
    which draws the same prose/code line the fence rule draws for a recipe. An UNPARSEABLE workflow is a
    finding, not a pass (#266): a file we could not read is one whose gate we could not check.
    """
    findings: list[str] = []
    try:
        doc = yaml.safe_load(path.read_text(encoding="utf-8"))
    except yaml.YAMLError as exc:
        return [f"{path}: could not be parsed, so its steps were NOT scanned — that is a finding, "
                f"not a pass (#266): {exc}"]
    if not isinstance(doc, dict):
        return []

    jobs = doc.get("jobs")
    if not isinstance(jobs, dict):
        return []

    for job_name, job in jobs.items():
        if not isinstance(job, dict):
            continue
        steps = job.get("steps")
        if not isinstance(steps, list):
            continue
        for i, step in enumerate(steps):
            if not isinstance(step, dict):
                continue
            script = step.get("run")
            if not isinstance(script, str):
                continue
            if EXEMPT_SH.search(script):
                continue
            where = step.get("name") or f"step {i}"
            for pat, what in BANNED:
                if pat.search(script):
                    findings.append(
                        f"{path}: job `{job_name}`, `{where}`: a workflow must not hand-roll the "
                        f"follow-up queue — this names {what}.\n"
                        f"    {REMEDY}"
                    )
                    break
    return findings


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    recipes = [p for root in ROOTS for p in (repo / root).rglob("*.md")]
    if not recipes:
        # An empty subject is a finding, not a pass — this gate's own lesson, applied to itself (#266).
        print("::error::check-recipe-followup: found NO recipes to scan. That is a broken gate, not a clean one.")
        return 1

    workflows = sorted(
        p for ext in ("*.yml", "*.yaml") for p in (repo / WORKFLOW_ROOT).rglob(ext)
    )

    findings: list[str] = []
    for p in sorted(recipes):
        findings.extend(scan(p))
    for p in workflows:
        findings.extend(scan_workflow(p))

    if findings:
        for f in findings:
            print(f"::error::{f}" if len(f.splitlines()) == 1 else f)
        print(f"\ncheck-recipe-followup: {len(findings)} hand-rolled follow-up queue(s) — see above.")
        print("::error::check-recipe-followup FAILED")
        return 1

    print(f"check-recipe-followup: OK — {len(recipes)} recipe(s) and {len(workflows)} workflow(s) "
          "scanned, none hand-rolls the follow-up queue.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

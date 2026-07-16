#!/usr/bin/env python3
"""Assert no recipe HAND-ROLLS the merge gate. It must call `fsgg-coord landable`.

.github#724, epic #266 (gates that fail open). Found while working .github#720.

WHY A GATE, AND NOT JUST A FIX

The "is this PR safe to merge?" rollup has been wrong FOUR times, and every fix edited a COPY:

  #547  the read was unpaginated  -> a failing check on page 2 was invisible, the aggregate read
        green, and the recipe merged a red PR.
  #606  ZERO checks read as GREEN -> "every check passed" and "CI never started" are the SAME EMPTY
        SET. A conflicted PR gets no CI at all, so it merged an entirely untested PR.
  #698  a SUPERSEDED (cancelled) run read as RED -> correct, green work was called failed, on the
        happy path the recipe itself manufactures (push, then push again: the second run cancels the
        first).
  #710  the same, in the skill-registry autofix BOT -> it read its own superseded runs and refused to
        merge the standing PR it had just pushed.
  #720  the same, in `pr_landable` -> `adopt` refused to land finished, green, FORCE-PUSHED work,
        which is the only kind it exists to land.

NOTHING EXECUTES A RECIPE, SO NOTHING TESTS ONE. That is the whole disease. The result was exactly
backwards: the copy that WAS testable (`pr_landable`, with a mock harness in tests/fsgg-coord/run.sh)
was the copy still carrying the bug, while the untested prose in the SKILL.md was right.

A recipe is COPIED, not imported. Fixing the four files fixes today's copies and nothing else; the next
hand-written rollup reintroduces the whole family. So the rule gets a gate: the logic lives in ONE
tested place, and a recipe may only NAME it.

AND IT IS WHAT MAKES A STALE RECIPE HARMLESS. A SKILL.md is copied into an agent's context at session
start and never refreshed, while N parallel workers merge protocol fixes all day — so a worker can
execute a gate the org fixed hours ago. Measured: PR #718 was red-lit by a nine-commit-stale snapshot
of `/pnext-item` §5, and its worker then re-filed the already-fixed bug as #719. A recipe that CALLS
the tool reads the CURRENT one off disk at run time: the prose may drift, the BEHAVIOUR cannot. That is
#609's "an import cannot drift by construction", applied to the protocol itself.

WHAT IS FLAGGED

Inside a ```sh fence in a recipe, a line that reads GitHub's check state:

    repos/<...>/actions/runs        (workflow runs)
    repos/<...>/commits/<...>/check-runs

...is a hand-rolled merge gate, and is refused. `fsgg-coord landable` is the only sanctioned reader.

WHAT IS NOT

  * PROSE. The docs must be able to *describe* the bug — including in code spans and tables — or the
    lesson cannot be written down at all. Only ```sh fences are scanned.
  * The tool itself (scripts/fsgg-coord) and its tests: that IS the one implementation.
  * A fence marked `<!-- landable-exempt: why -->` on the line before it. There is no legitimate use
    today; it exists so that a future need is a DELIBERATE, reviewed act with a reason attached,
    rather than a reason to delete this gate.

AND WORKFLOWS, SINCE .github#737. This gate used to scan recipes ONLY, and said so: the last copy lived
in `.github/workflows/skill-registry-autofix.yml` (the #710 copy — the bot that read its OWN superseded
runs as red and refused to merge the PR it had just pushed), it was CORRECT, and red-lighting a workflow
that was doing the right thing would have been a gate firing on correct behaviour, which is one people
learn to skip (#498). #737 converted that bot to call `landable` — which needed the command to learn
`--require` (its subject, `registry-coherence`, is not a required check and nothing else would ever look
at it) and `--sha` (it force-pushes, and `pulls/{n}` lags). With the last copy gone, the root goes in:
a sixth copy is now unwritable in a WORKFLOW as well as in a recipe.

A WORKFLOW IS NOT SCANNED THE WAY A RECIPE IS, and the difference matters. A recipe is scanned for ```sh
fences, because its prose must be able to DESCRIBE the bug — that is where the lesson is written down. A
workflow has no fences, and its shell lives in `run:` blocks. It is also, in this repo, more comment than
code: `skill-registry-autofix.yml` discusses `actions/runs` at length, in comments, for good reasons. So
workflows are PARSED, and only `jobs.*.steps[*].run` is scanned — the YAML parser drops comments for us,
which is exactly the prose/code line the fence rule draws by hand. A whole-file regex would fire on the
very comments that explain why the rule exists.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

import yaml

# The recipes this rule governs. A SKILL.md is the agent-facing, copied-not-imported artifact.
ROOTS = (".claude/skills", ".agents/skills", "docs/coordination")

# The workflows it governs (#737). Scanned by PARSING, not by fence — see the docstring.
WORKFLOW_ROOT = ".github/workflows"

FENCE = re.compile(r"^```(\w*)\s*$")

# The escape hatch, in the comment syntax of the thing being scanned. A recipe is markdown, so it is an
# HTML comment before the fence; a workflow's `run:` block is SHELL, where `<!--` is not a comment but a
# syntax error — so there it is a `#` comment. One rule, two spellings, because a hatch you cannot write
# is not a hatch: it would leave "delete the gate" as the only way past it, which is the opposite of the
# point (#737).
EXEMPT = re.compile(r"<!--\s*landable-exempt:")
EXEMPT_SH = re.compile(r"#\s*landable-exempt:")

# The two endpoints that ARE the merge gate. Matched loosely on purpose: any shape of `gh api` call,
# any quoting, any interpolation.
BANNED = (
    (re.compile(r"actions/runs\?"), "workflow runs (`actions/runs?head_sha=…`)"),
    (re.compile(r"/check-runs"), "check runs (`commits/<sha>/check-runs`)"),
)

REMEDY = (
    "call the tool instead — it is the ONE tested implementation:\n"
    "        scripts/fsgg-coord landable <pr> --wait     # exits 0 only on green\n"
    "    It handles pagination (#547), zero-subject (#606), supersession (#698/#720), the\n"
    "    registration race, and the runs-vs-check-runs blind spots. Do not re-derive it: that is\n"
    "    what this gate exists to stop (#724)."
)


def scan(path: Path) -> list[str]:
    """Return a list of human-readable findings for one file."""
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
                # A fence opens. It is exempt only if the PRECEDING non-blank line says so.
                in_sh = m.group(1) in ("sh", "bash", "shell")
                prev = next((l for l in reversed(lines[: i - 1]) if l.strip()), "")
                exempt = bool(EXEMPT.search(prev))
            continue
        if not in_sh or exempt:
            continue
        for pat, what in BANNED:
            if pat.search(line):
                findings.append(
                    f"{path}:{i}: a recipe must not hand-roll the merge gate — this reads {what}.\n"
                    f"    {line.strip()}\n"
                    f"    {REMEDY}"
                )
                break
    return findings


def scan_workflow(path: Path) -> list[str]:
    """Findings for one workflow: the banned endpoints inside `jobs.*.steps[*].run` (#737).

    PARSED, not regexed over the raw text. A workflow's comments are prose — this repo's workflows argue
    about `actions/runs` at length, correctly — and the YAML parser drops them, which draws the same
    prose/code line the fence rule draws for a recipe. What is left is the shell that actually RUNS.

    An UNPARSEABLE workflow is a finding, not a pass: this gate's own lesson (#266). A file we could not
    read is one whose gate we could not check, and reporting that as clean is the fail-open shape.
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
            # The same escape hatch as a recipe fence, in SHELL comment syntax — `<!--` is a syntax error
            # in a `run:` block, so the markdown spelling would be a hatch nobody could write. There is no
            # legitimate use today; it exists so a future need is a DELIBERATE, reviewed act with a reason
            # attached, rather than a reason to delete this gate.
            if EXEMPT_SH.search(script):
                continue
            where = step.get("name") or f"step {i}"
            for pat, what in BANNED:
                if pat.search(script):
                    findings.append(
                        f"{path}: job `{job_name}`, `{where}`: a workflow must not hand-roll the merge "
                        f"gate — this reads {what}.\n"
                        f"    {REMEDY}"
                    )
                    break
    return findings


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    recipes = [p for root in ROOTS for p in (repo / root).rglob("*.md")]
    if not recipes:
        # An empty subject is a finding, not a pass — this gate's own lesson, applied to itself (#266).
        print("::error::check-recipe-landable: found NO recipes to scan. That is a broken gate, not a clean one.")
        return 1

    # The workflow root is scanned when it EXISTS. Unlike the recipes it is not asserted non-empty: this
    # script is copied into repos that have no `.github/workflows` of their own, and a gate that reds
    # there would be teaching people to ignore it (#498). Where the directory exists, it is scanned.
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
        print(f"\ncheck-recipe-landable: {len(findings)} hand-rolled merge gate(s) — see above.")
        print("::error::check-recipe-landable FAILED")
        return 1

    print(f"check-recipe-landable: OK — {len(recipes)} recipe(s) and {len(workflows)} workflow(s) "
          "scanned, none hand-rolls the merge gate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

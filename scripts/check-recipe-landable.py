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

AND ONE COPY IS STILL OUT THERE, DELIBERATELY. `.github/workflows/skill-registry-autofix.yml` carries
its own rollup — the #710 copy. It is CORRECT today, and it is not a recipe: it is an auto-merge bot
whose gate is entangled with a read-only token for `actions/runs`, `mergeStateStatus` polling, and a
by-name assertion on `registry-coherence`. Converting it to call `landable` is a real refactor of a bot
that merges without a human, so it is its own item (.github#737) rather than a rider on this one, and
`.github/workflows/` is deliberately NOT in ROOTS until it lands — adding it now would red-light a
workflow that is doing the right thing. So: this gate makes a fifth copy unwritable IN A RECIPE, which
is where four of the five lived. It does not yet make one unwritable in a workflow. Say what the gate
does, not what you wish it did (#266).
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

# The recipes this rule governs. A SKILL.md is the agent-facing, copied-not-imported artifact.
ROOTS = (".claude/skills", ".agents/skills", "docs/coordination")

FENCE = re.compile(r"^```(\w*)\s*$")
EXEMPT = re.compile(r"<!--\s*landable-exempt:")

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


def main() -> int:
    repo = Path(__file__).resolve().parent.parent
    targets = [p for root in ROOTS for p in (repo / root).rglob("*.md")]
    if not targets:
        # An empty subject is a finding, not a pass — this gate's own lesson, applied to itself (#266).
        print("::error::check-recipe-landable: found NO recipes to scan. That is a broken gate, not a clean one.")
        return 1

    findings: list[str] = []
    for p in sorted(targets):
        findings.extend(scan(p))

    if findings:
        for f in findings:
            print(f"::error::{f}" if len(f.splitlines()) == 1 else f)
        print(f"\ncheck-recipe-landable: {len(findings)} hand-rolled merge gate(s) — see above.")
        print("::error::check-recipe-landable FAILED")
        return 1

    print(f"check-recipe-landable: OK — {len(targets)} recipe(s) scanned, none hand-rolls the merge gate.")
    return 0


if __name__ == "__main__":
    sys.exit(main())

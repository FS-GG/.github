#!/usr/bin/env python3
"""Fail closed when the shared board review bound or its escalation contract drifts."""

from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".agents", ".claude")
MAX_ROUNDS = 3


def read(runtime: str, relative: str) -> str:
    return (ROOT / runtime / "skills" / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    relative = "pnext-item/references/independent-review.md"
    texts = [read(runtime, relative) for runtime in RUNTIMES]
    require(texts[0] == texts[1], "independent-review contract differs between authored roots")

    contract = texts[0]
    require("operator authoriz" not in contract.lower(), "review contract retains an operator-authorization gate")
    require("when authorized" not in contract.lower(), "review contract retains a conditional authorization branch")
    match = re.search(r"`max-automated-repair-rounds: (\d+)`", contract)
    require(match is not None, "review contract has no machine-readable repair-round limit")
    require(int(match.group(1)) == MAX_ROUNDS, f"repair-round limit is not {MAX_ROUNDS}")

    normalized = " ".join(contract.split())
    for literal in (
        "`round-numbering: 1-based`",
        "`round-four-action: automatic-repair-phase`",
        "`repair-phase-entry: automatic-after-ordinary-exhaustion`",
        "`human-escalation-sentinel: Blocked on: human/action`",
        "<!-- fsgg:independent-review-escalation:v1 -->",
        "all three ordered confirmation URLs",
        "count-before-routing gate",
        "stops without merging",
        "Only a human or the automatic repair-phase transition may retire that sentinel",
        "sets `Status: Ready`",
        "without human interaction",
        "exhausted PR cannot reset its counter",
    ):
        require(literal in normalized, f"review contract is missing escalation invariant: {literal}")

    # .github#2087 — the bot-driven player journey gate is blocking, not advisory, and is falsifiable
    # here: delete any one of these literals and this test reds, exactly like the escalation invariants
    # above.
    for literal in (
        "This gate is **blocking**, not advisory",
        "Direct `Msg` injection, a test-only API, or any seam that exists solely for tests is **not "
        "evidence**",
        "boot at the product's real entry point",
        "reported as uncovered, never silently absent",
        "the critic returns `changes-required` and records that the gate cannot run and why",
        "not by itself material under this gate",
    ):
        require(literal in normalized, f"review contract is missing player-journey-gate invariant: {literal}")

    # .github#2086 — runtime-route claims cannot be certified by source reading alone. These literals
    # form a falsifiable fixture: deleting the production execution, built-artifact, report-completeness,
    # or reusable Rogue3 comparison language makes this contract test red.
    for literal in (
        "Source review remains required, but it is not sufficient for a runtime-route divergence claim",
        "critic **must execute or measure** at least one comparison",
        "production route against the built artifact",
        "A report that cites only source reading for such a claim is incomplete",
        "Every **passing** initial or confirmation marker carries exactly one machine-readable applicability shape",
        "route-applicability: meaningful",
        "built-artifact: <artifact exercised>",
        "executed-command: <command or measurement performed>",
        "compared-routes: <production route and comparison route>",
        "observed-result: <observed equality or divergence>",
        "route-applicability: not-meaningful",
        "route-not-meaningful-reason: <bounded reason tied to this review subject>",
        "fail the live review-marker parser",
        "built product route emitted `[]` while direct dispatch emitted `[PlaySfx",
    ):
        require(literal in normalized, f"review contract is missing runtime-route evidence invariant: {literal}")

    # .github#2223 — ten measured instances in one run of a gate that passed green and could not fail
    # on the thing it claimed. The remedy is a NUMBERED critic step, not a virtue some critics happen
    # to have, so each acceptance criterion is pinned to a literal here: delete any one of them and
    # this contract test reds, exactly like the runtime-route and player-journey invariants above.
    # Each entry is annotated with the criterion it holds, so a later edit cannot drop one silently.
    for literal in (
        # AC1 — a numbered step, applied to every gate the change adds or modifies.
        "**Inventory the gates the change touches.**",
        # Subject-breaking is the PRIMARY form. Predicate inversion reds necessarily, so it proves
        # reachability and never connection-to-subject: offered as an equal it would certify the very
        # decorative gate step 4's own `C4` instance measured at 30/30 green under a subject mutation.
        # It is a strictly weaker fallback that grades NOT_MEASURED, never JUSTIFIED.
        "**Invert each one exactly once, by breaking its SUBJECT.**",
        "not the gate's own predicate",
        "**Predicate inversion is not an equal alternative, and on its own it proves nothing.**",
        "never that it is connected to its subject",
        "grade the gate `NOT_MEASURED` — never `JUSTIFIED`",
        # AC1 — a surviving inversion is material BY DEFINITION, never a judgement call.
        "**A surviving inversion is material by definition.**",
        "a gate the change adds or modifies stays green when inverted",
        # AC2 — bounded to touched gates; one mutation each. Without this the step is unbounded.
        "bounded to gates the diff **adds or modifies** — never the whole suite",
        "One mutation per touched gate is the bound",
        # AC3 — the test that CLAIMS a property must be the test that PROVIDES it.
        "**The test that claims a property must be the test that provides it.**",
        "record which test provides it",
        "breaking `collisions()` outright left it 30/30 green",
        # AC5 — the non-answer-graded-as-an-answer sub-shape a happy-path mutation cannot catch.
        "**a non-answer reported as a confident answer.**",
        "invert the **unreadable input** as well",
        "was graded a confident negative",
        # Fixture sub-shape (issue comment, FS.GG.Templates#379): a world simpler than production.
        "**A fixture must reproduce the production condition the gate will actually face.**",
        "records what it does not reproduce",
        # Environment sub-shape (issue comment, FS.GG.Templates#392): a world richer than production.
        "must prove it did not.**",
        "**aborts** when the confound is still present",
        "a run that self-aborts unless the tool is absent is",
        # Repair criterion: the demonstration must catch the escape that was actually found.
        "**A repair must catch the escape that was actually found.**",
        "without re-running the original escape has not been shown to close it",
        # Strengthened-assertion sub-shape (issue comment, FS.GG.Templates#349): a repair may broaden
        # what a gate CLAIMS without broadening what it checks, and the evidence for the weaker claim
        # does not carry forward. Distinct from the three world-mismatch shapes: nothing about the test
        # world changed, only the confidence of the sentence the gate emits.
        "**When a repair strengthens what a gate ASSERTS, re-check the evidence for the strengthened assertion.**",
        "the evidence that supported the weaker claim does not carry forward",
        "**A gate that gains eloquence faster than correctness is worse than one that stayed vague**",
        "a strengthened assertion with unchanged evidence is a material finding",
        # AC6 — a worked example drawn from one of the measured instances.
        "### Worked example",
        'so that deleting the step reds too"',
        "**step 2 was already satisfied and the defect still escaped**",
        "reverting only the anchor now gives `FAIL=1`",
        # #266 — an unobtained measurement is never rounded into a pass.
        "which is `NOT_MEASURED`, never a pass",
    ):
        require(literal in normalized, f"review contract is missing gate-inversion invariant: {literal}")

    # .github#2223 AC4 — the implementer-side mirror in `pnext-item` §3. Authoring a gate without
    # evidence it can fail makes the critic's step a discovery instead of a confirmation, so the
    # obligation is stated on BOTH sides of the handoff. Both authored roots must carry it, and the
    # identity check is what stops one runtime keeping the obligation while the other drops it.
    skill_texts = [read(runtime, "pnext-item/SKILL.md") for runtime in RUNTIMES]
    require(skill_texts[0] == skill_texts[1], "pnext-item SKILL.md differs between authored roots")
    skill_normalized = " ".join(skill_texts[0].split())
    for literal in (
        "**ships with evidence it can fail**",
        "invert it, run the suite, and record the mutation and the observed red",
        "A test that passes both before and after the fix has not tested the fix",
        "a gate whose inversion survives is a material finding at review by definition",
    ):
        require(
            literal in skill_normalized,
            f"pnext-item SKILL.md is missing the implementer gate-evidence mirror: {literal}",
        )

    for runtime in RUNTIMES:
        for relative in (
            "pnext-item/SKILL.md",
            "work-board/SKILL.md",
            "work-board/references/host-loop.md",
            "drive-board/SKILL.md",
            "drive-board/references/host-loop.md",
        ):
            text = read(runtime, relative)
            require("round three" in text or "third round" in text, f"{runtime}/{relative} omits round three")
            require("round four" in text or "fourth round" in text, f"{runtime}/{relative} omits round-four refusal")

        for relative in (
            "work-board/SKILL.md",
            "work-board/references/host-loop.md",
            "drive-board/SKILL.md",
            "drive-board/references/host-loop.md",
            "work-board-best/SKILL.md",
            "work-board-normal/SKILL.md",
            "drive-board-best/SKILL.md",
            "drive-board-normal/SKILL.md",
        ):
            text = read(runtime, relative)
            require("automatically" in text, f"{runtime}/{relative} does not automatically enter repair phase")
            require("operator authoriz" not in text.lower(), f"{runtime}/{relative} retains an operator-authorization gate")

    print(
        "review-round-contract: three rounds, automatic repair phase, ordered evidence, "
        "gate-inversion evidence, and terminal human escalation hold"
    )


if __name__ == "__main__":
    main()

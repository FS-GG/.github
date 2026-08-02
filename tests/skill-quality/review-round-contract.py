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
    match = re.search(r"`max-automated-repair-rounds: (\d+)`", contract)
    require(match is not None, "review contract has no machine-readable repair-round limit")
    require(int(match.group(1)) == MAX_ROUNDS, f"repair-round limit is not {MAX_ROUNDS}")

    normalized = " ".join(contract.split())
    for literal in (
        "`round-numbering: 1-based`",
        "`round-four-action: human-escalation`",
        "`human-escalation-sentinel: Blocked on: human/action`",
        "<!-- fsgg:independent-review-escalation:v1 -->",
        "all three ordered confirmation URLs",
        "count-before-routing gate",
        "stops without merging",
        "Only a human may retire that sentinel",
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
        "built product route emitted `[]` while direct dispatch emitted `[PlaySfx",
    ):
        require(literal in normalized, f"review contract is missing runtime-route evidence invariant: {literal}")

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

    print("review-round-contract: three rounds, ordered evidence, and human escalation hold")


if __name__ == "__main__":
    main()

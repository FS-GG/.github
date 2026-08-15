#!/usr/bin/env python3
"""Fail closed when the structured review authority or authored roots drift."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".agents", ".claude")


def read(runtime: str, relative: str) -> str:
    return (ROOT / runtime / "skills" / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    relative = "pnext-item/references/independent-review.md"
    texts = [read(runtime, relative) for runtime in RUNTIMES]
    require(texts[0] == texts[1], "independent-review contract differs between authored roots")
    contract = " ".join(texts[0].split())

    for literal in (
        "`fsgg.coord.review-decision/v2`",
        "`initial, confirmation, escalation, repair-phase, acceptance`",
        "ordinary repair ceiling | 3",
        "repair-phase ceiling | 10",
        "scripts/fsgg-coord review record",
        "Revisions start at one and are contiguous",
        "`previousDigest` binds the prior canonical record",
        "prose is never authority",
        "A moved head retires the accepted older generation",
        "malformed, stale, partial-coverage, mixed-head, or byte-drifted evidence",
        "--require fsgg:review-decision/v2",
    ):
        require(literal in contract, f"structured review contract is missing: {literal}")

    retired_parts = (
        "fsgg:independent-review" + ":v1",
        "fsgg:independent-review-confirmation" + ":v1",
        "fsgg:independent-review-escalation" + ":v1",
        "fsgg:independent-review-repair-phase" + ":v1",
        "fsgg:review-accepted" + ":v1",
    )
    active_roots = (ROOT / ".agents", ROOT / ".claude")
    for active_root in active_roots:
        for path in active_root.rglob("*.md"):
            body = path.read_text(encoding="utf-8")
            for retired in retired_parts:
                require(retired not in body, f"retired prose authority remains in {path}: {retired}")

    for relative, acceptance_literal in (
        ("pnext-item/SKILL.md", "structured v2 acceptance record"),
        ("drive-board/references/host-loop.md", "fsgg:review-decision/v2"),
        ("work-board/references/host-loop.md", "fsgg:review-decision/v2"),
    ):
        authored = [read(runtime, relative) for runtime in RUNTIMES]
        require(authored[0] == authored[1], f"{relative} differs between authored roots")
        require(acceptance_literal in authored[0], f"{relative} does not consume structured acceptance")

    print("review-round-contract: structured v2 ledger, digest chain, round bounds, and new-only authority hold")


if __name__ == "__main__":
    main()

#!/usr/bin/env python3
"""Semantic guard for the reduced routine route across every board driver."""

from pathlib import Path
import sys


ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
AGENTS = ROOT / ".agents" / "skills"
CLAUDE = ROOT / ".claude" / "skills"


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"routine-board-route: {message}")


skills = (
    "work-board",
    "work-board-normal",
    "work-board-best",
    "drive-board",
    "drive-board-normal",
    "drive-board-best",
    "pnext-item",
)
for skill in skills:
    agents = (AGENTS / skill / "SKILL.md").read_text(encoding="utf-8")
    claude = (CLAUDE / skill / "SKILL.md").read_text(encoding="utf-8")
    require(agents == claude, f"{skill} differs between .agents and .claude")

for driver in ("work-board", "drive-board"):
    body = (AGENTS / driver / "SKILL.md").read_text(encoding="utf-8")
    folded = " ".join(body.lower().split())
    for phrase in (
        "Choose each item's route before scheduling",
        ".fsgg/routine-development.json",
        "one accountable owner",
        "one PR",
        "no live claim or existing strict delivery state",
        "asynchronous and cannot block",
        "applies only to strict items",
        "never create a second PR",
        "Multiple or ambiguous open PRs refuse routine admission",
    ):
        require(" ".join(phrase.lower().split()) in folded, f"{driver} lost routine-route clause {phrase!r}")
    for omitted in (
        "independent critic",
        "lifecycle ledger",
        "delivery receipt",
        "metadata-`Done` write",
    ):
        require(" ".join(omitted.split()) in " ".join(body.split()), f"{driver} no longer names omitted ceremony {omitted!r}")

for variant in ("work-board-normal", "work-board-best", "drive-board-normal", "drive-board-best"):
    body = (AGENTS / variant / "SKILL.md").read_text(encoding="utf-8")
    require("routine route unchanged" in body, f"{variant} does not inherit its base routine route")
    require("does not authorize a critic" in body, f"{variant} makes routine review ambiguous")
    require("strict item's three-round review chain" in body, f"{variant} does not scope repair dispatch to strict work")
    require("ordinary three-round chain" not in body, f"{variant} can route routine work into strict repair ceremony")

for driver in ("work-board", "drive-board"):
    triage = (AGENTS / driver / "references" / "backlog-triage.md").read_text(encoding="utf-8")
    normalized = " ".join(triage.split())
    for phrase in (
        "Routine admissions enter the one-owner plan without a board write",
        "A routine-only pass does not create a planning receipt or content-disposition artifact",
        "For routine items, verify only the exact-head required checks, native PR merge/readback, and native issue-closing link",
        "are not routine gates",
    ):
        require(phrase in normalized, f"{driver} backlog triage lost routine exemption {phrase!r}")

pnext = (AGENTS / "pnext-item" / "SKILL.md").read_text(encoding="utf-8")
pnext_normalized = " ".join(pnext.split())
routine_start = pnext.find("## Choose the route before lifecycle work")
strict_start = pnext.find("## Strict item state machine")
ledger_start = pnext.find("## Lifecycle ledger")
require(0 <= routine_start < strict_start < ledger_start, "pnext-item does not short-circuit before strict lifecycle work")
for phrase in (
    "The routine route ends here",
    "Do not execute the lifecycle, identity, claim, critique, receipt, feedback,",
    "one fresh `routine/<item-slug>` branch and one PR",
    "native merge boundary",
    "adopt that branch, and continue the same PR; never open a second PR",
    "Multiple or ambiguous candidate PRs refuse routine admission",
    "best-effort,\n   asynchronous observations and cannot invalidate the merge",
):
    require(" ".join(phrase.split()) in pnext_normalized, f"pnext-item lost routine owner contract {phrase!r}")

print("routine-board-route: ok")

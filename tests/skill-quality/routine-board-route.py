#!/usr/bin/env python3
"""Semantic guard for the reduced routine route across every board driver."""

import importlib.util
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

# Canonical V2 behavior and wrapper inheritance now share one production predicate.
spec = importlib.util.spec_from_file_location("skill_quality", ROOT / "scripts/check-skill-quality.py")
quality = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quality)
errors = []
quality.validate_organization_consumer(ROOT, errors)
quality.validate_work_board_coordinator(ROOT, errors)
quality.validate_lifecycle_routes(ROOT, errors)
require(not errors, "; ".join(errors))
for variant, parent in (("work-board-normal", "work-board"), ("work-board-best", "work-board"),
                        ("drive-board-normal", "drive-board"), ("drive-board-best", "drive-board")):
    body = " ".join((AGENTS / variant / "SKILL.md").read_text().split())
    require(f"../{parent}/SKILL.md" in body, f"{variant} lost its canonical parent")
    require("explicitly to every" in body and "do not downgrade" in body, f"{variant} lost actual runtime routing")
    require("strict item's three-round review chain" not in body, f"{variant} restores retired critic ceremony")

pnext = (AGENTS / "pnext-item" / "SKILL.md").read_text(encoding="utf-8")
pnext_normalized = " ".join(pnext.split())
routine_start = pnext.find("## Choose the route before lifecycle work")
strict_link = pnext.find("references/strict-item-lifecycle.md")
require(0 <= routine_start < pnext.find("The routine route ends here") < strict_link,
        "pnext-item does not short-circuit before conditional strict lifecycle traversal")
require("## Lifecycle ledger" not in pnext and "## 0. Establish identity" not in pnext,
        "pnext-item eagerly loads strict lifecycle work")
strict = (AGENTS / "pnext-item/references/strict-item-lifecycle.md").read_text(encoding="utf-8")
require(0 <= strict.find("## Lifecycle ledger") < strict.find("## 0. Establish identity"),
        "strict reference lost lifecycle-before-identity ordering")
for phrase in (
    "The routine route ends here",
    "A routine refusal blocks the affected delivery until repaired",
    "it does not select heavyweight",
    "Do not execute the lifecycle, identity, claim, critique, receipt, feedback,",
    "one fresh `routine/<item-slug>` branch and one PR",
    "native merge boundary",
    "adopt that branch, and continue the same PR; never open a second PR",
    "Multiple or ambiguous candidate PRs refuse routine admission",
    "best-effort, asynchronous observations and cannot invalidate the merge",
    "close only its fulfilled promise",
    "including publication, installed, participant and recovery obligations",
    "continues clearing",
):
    require(" ".join(phrase.split()) in pnext_normalized, f"pnext-item lost routine owner contract {phrase!r}")

print("routine-board-route: ok")

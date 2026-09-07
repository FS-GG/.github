#!/usr/bin/env python3
"""Keep the prospective reduced route aligned across policy, executable gate, and guidance."""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ABSENCES = {
    "issue", "claim", "sdd-artifacts", "phase-lifecycle-ledger", "independent-critic",
    "feedback-report", "receipt-cycle", "metadata-done",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"routine-development-route: {message}")


policy = json.loads((ROOT / ".fsgg/routine-development.json").read_text())
require(policy.get("schema") == "fsgg.routine-development-policy/v1", "wrong policy schema")
require(policy.get("status") == "pilot" and policy.get("prospective") is True, "route is not prospective pilot")
require(set(policy.get("notRequired", [])) == ABSENCES, "reduced requirements drifted")
require(policy.get("legacyAuthority") == "strict", "legacy authority is not retained")
for operation in ("publish", "deploy", "credential-change", "destructive-effect", "migration-cutover", "external-contract-acceptance", "strict-item-continuation"):
    require(operation in policy.get("protectedOperations", []), f"protected operation missing: {operation}")

agent_skill = (ROOT / ".agents/skills/work-roadmap/SKILL.md").read_bytes()
claude_skill = (ROOT / ".claude/skills/work-roadmap/SKILL.md").read_bytes()
require(agent_skill == claude_skill, "Claude/Codex work-roadmap twins differ")
skill = agent_skill.decode()
for phrase in ("one accountable owner", "Do **not** create or require an issue", "exact base/head", "strict operation"):
    require(phrase in skill, f"work-roadmap omitted {phrase!r}")

constitution = (ROOT / ".fsgg/constitution.md").read_text()
require("Prospective Routine-Development Pilot" in constitution, "constitution did not adopt pilot")
for number in ("0053", "0079", "0080", "0081", "0082"):
    adr = next((ROOT / "docs/adr").glob(f"{number}-*.md")).read_text()
    require("Narrowed prospectively on 2026-09-07" in adr, f"ADR-{number} is not explicitly narrowed")

roadmap = (ROOT / "docs/2026-09-07-074251-radical-development-bureaucracy-reduction-design-and-roadmap.md").read_text()
for name in (
    "R0 — Adopt the smaller contract", "R1 — Implement one-owner, one-PR delivery",
    "R2 — Fix economics reporting and make telemetry non-blocking",
    "R3 — Simplify release, registry and recovery paths",
    "R4 — Measure the current route and retire obsolete ceremony",
    "R5 — Carry improvements into ordinary v2 development and verify receiver adoption",
):
    require(name in roadmap, f"roadmap mapping omitted {name}")

gate = (ROOT / "scripts/check-claim-generation.py").read_text()
workflow = (ROOT / ".github/workflows/coherence.yml").read_text()
require("evaluate_routine" in gate and "ROUTINE_NOT_REQUIRED" in gate, "required check does not enforce routine policy")
require('--base-sha "$BASE_SHA"' in workflow, "required workflow does not bind routine base/head")
print("PASS  routine development route: policy, required gate, strict boundary, ADRs, roadmap, and twins agree")

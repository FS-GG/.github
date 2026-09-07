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
require(policy.get("trustModel", {}).get("repositoryWriters") == "trusted", "trusted-writer model is not explicit")
require("adversarial protection" in policy.get("trustModel", {}).get("excludedClaim", ""), "trust-model non-guarantee is absent")
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
routine_workflow = (ROOT / ".github/workflows/routine-eligibility.yml").read_text()
selftest_workflow = (ROOT / ".github/workflows/routine-eligibility-selftest.yml").read_text()
require("evaluate_routine" in gate and "ROUTINE_NOT_REQUIRED" in gate, "required check does not enforce routine policy")
require('--routine-policy-ref' not in workflow, "candidate-controlled coherence workflow still decides routine eligibility")
for phrase in ("pull_request_target:", "contents: read", 'show "$BASE_SHA:scripts/check-claim-generation.py"', "Repository writers are trusted"):
    require(phrase in routine_workflow, f"trusted routine workflow omitted {phrase!r}")
require("actions/checkout" not in routine_workflow and "GITHUB_TOKEN" not in routine_workflow, "trusted routine workflow can execute candidate bytes or exposes a token")
require("pull_request:" in selftest_workflow and "routine-eligibility-fixture" in selftest_workflow, "ordinary fixture workflow is absent")
require("bash tests/routine-eligibility/run.sh" in selftest_workflow, "ordinary fixture does not execute its test")
print("PASS  routine development route: policy, required gate, strict boundary, ADRs, roadmap, and twins agree")

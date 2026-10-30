#!/usr/bin/env python3
"""Keep routine-by-default doctrine aligned across policy, executable gate, and guidance."""

from __future__ import annotations

import json
import re
import shutil
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
ABSENCES = {
    "issue", "claim", "sdd-artifacts", "phase-lifecycle-ledger", "independent-critic",
    "feedback-report", "receipt-cycle", "metadata-done", "projection-pr",
}
NON_TRIGGERS = {
    "strict-label", "gs2-registration", "protected-path", "policy-change", "modeled-work",
    "protected-operation", "existing-strict-state",
}


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"routine-development-route: {message}")


policy = json.loads((ROOT / ".fsgg/routine-development.json").read_text())
require(policy.get("schema") == "fsgg.routine-development-policy/v1", "wrong policy schema")
require(policy.get("status") == "active" and policy.get("defaultRoute") == "routine", "routine is not active default")
require(policy.get("heavyProcessSelection") == {
    "trigger": "recorded-explicit-human-instruction",
    "namedScopeRequired": True,
    "absenceOrAmbiguity": "routine",
}, "heavy route is not selected only by recorded explicit human instruction")
require(set(policy.get("heavyProcessNonTriggers", [])) == NON_TRIGGERS, "heavy-process non-triggers drifted")
require(set(policy.get("notRequired", [])) == ABSENCES, "reduced requirements drifted")
require(policy.get("legacyAuthority") == "evidence-retained-not-a-route-selector", "legacy strict state still selects process")
require(policy.get("safeguardRule") == "technical-checks-and-operation-authority-remain-independent-and-fail-closed", "technical safeguards drifted")
require(policy.get("trustModel", {}).get("repositoryWriters") == "trusted", "trusted-writer model is not explicit")
require("adversarial protection" in policy.get("trustModel", {}).get("excludedClaim", ""), "trust-model non-guarantee is absent")
for operation in ("publish", "deploy", "credential-change", "destructive-effect", "migration-cutover", "external-contract-acceptance"):
    require(operation in policy.get("protectedOperations", []), f"protected operation missing: {operation}")

agent_skill = (ROOT / ".agents/skills/work-roadmap/SKILL.md").read_bytes()
claude_skill = (ROOT / ".claude/skills/work-roadmap/SKILL.md").read_bytes()
require(agent_skill == claude_skill, "Claude/Codex work-roadmap twins differ")

# Conditional branches remain reachable without eagerly loading their complete contracts.
ROUTES = {
    "pnext-item": {
        "strict-item-lifecycle.md": ("Recorded explicit human instruction", "## Lifecycle ledger", "## 7. Stamp and stop"),
    },
    "work-roadmap": {
        "routine-producer-installation.md": ("Receiver has no routine producer", "--install-routine-producer", "readback-only"),
        "runtime-observation.md": ("Native dispatch", "## Record repository-owned dispatches by default", "usage-reconcile"),
        "heavyweight-roadmap.md": ("Recorded explicit human instruction", "## Lifecycle log", "## Status position line"),
        "execution-diagnostics.md": ("before diagnostic execution", "Preserve the first cause", "partial failures cannot"),
        "performance-first.md": ("Interactive/game implementation", "## PERF-PLAN", "## PERF-RELEASE"),
    },
}
LINK = re.compile(r"\[[^\]]+\]\(([^)]+)\)")


def validate_routes(contents: dict[str, str]) -> None:
    for name, references in ROUTES.items():
        entry = contents[f"{name}/SKILL.md"]
        for heading in ("## Lifecycle ledger", "## Lifecycle log", "## Status position line",
                        "## 0. Establish identity", "## First installation of the routine producer",
                        "## Record repository-owned dispatches by default"):
            require(heading not in entry, f"{name} entrypoint eagerly loads {heading}")
        for reference, (condition, *witnesses) in references.items():
            relative = f"{name}/references/{reference}"
            require(f"references/{reference}" in entry and condition in entry,
                    f"{name} omitted applicability or link for {reference}")
            require(relative in contents, f"{name} cannot load {reference}")
            for witness in witnesses:
                require(witness in contents[relative], f"{relative} lost {witness!r}")
        for relative in (f"{name}/SKILL.md", *(f"{name}/references/{r}" for r in references)):
            source = ROOT / ".agents/skills" / relative
            for target in LINK.findall(contents[relative]):
                if ":" in target or target.startswith("#"):
                    continue
                require((source.parent / target.split("#", 1)[0]).is_file(),
                        f"unreadable applicable reference: {relative}: {target}")


contents = {
    f"{name}/{relative}": (ROOT / ".agents/skills" / name / relative).read_text()
    for name, references in ROUTES.items()
    for relative in ("SKILL.md", *(f"references/{r}" for r in references))
}
# The roadmap is an adapter: validate its reachable owner contract rather than
# requiring a second copy of the routine protocol in the adapter body.
ROUTINE_OWNER_LINK = "https://github.com/FS-GG/.github/blob/main/.agents/skills/pnext-item/SKILL.md"
ROUTINE_CONTRACTS = {
    "work-roadmap/SKILL.md": (
        "not a scheduling ledger", "Execute the already assigned slice through",
        "complete plan/issue acceptance, existing owner, attempt, worktree and PR",
        "ADR-0084", "content/semantic classifier first", "dependent acceptance or activation",
    ),
    "pnext-item/SKILL.md": (
        "one accountable owner at a time and one continuous PR",
        "adopt that branch, and continue the same PR; never open a second PR",
        "Multiple or ambiguous candidate PRs refuse routine admission",
        "one independent source review of the exact candidate, relevant tests and final required CI",
        "A moved head must be reviewed and rebound",
        "Only a recorded explicit human instruction selects heavyweight process for named scope",
        "absence or ambiguity stays routine",
        "A live claim, inherited strict state, protected operation/path, policy or modeled change, or GS2 registration is not a heavyweight trigger",
        "After the exact-head required checks pass, use the repository's native merge boundary",
        "Invalid or unknown technical or operation authorization blocks the affected effect",
        "The routine route ends here",
    ),
}


def validate_routine_owner(contents: dict[str, str]) -> None:
    adapter = contents["work-roadmap/SKILL.md"]
    require(ROUTINE_OWNER_LINK in LINK.findall(adapter),
            "work-roadmap cannot reach the canonical pnext-item owner contract")
    for relative, obligations in ROUTINE_CONTRACTS.items():
        require(relative in contents, f"unreadable routine owner contract: {relative}")
        normalized = " ".join(contents[relative].split())
        for obligation in obligations:
            require(obligation in normalized, f"{relative} lost routine obligation {obligation!r}")
    owner = contents["pnext-item/SKILL.md"]
    require(0 <= owner.find("The routine route ends here") < owner.find("references/strict-item-lifecycle.md"),
            "routine owner no longer stops before the explicitly selected heavy protocol")


validate_routine_owner(contents)
# Remove each actual obligation or dispatch edge, including the target body. A
# keyword elsewhere in the adapter cannot stand in for the delegated contract.
for relative, obligations in ROUTINE_CONTRACTS.items():
    for obligation in obligations:
        mutated = dict(contents)
        normalized = " ".join(mutated[relative].split())
        mutated[relative] = normalized.replace(obligation, "removed-routine-obligation")
        try:
            validate_routine_owner(mutated)
        except SystemExit:
            pass
        else:
            raise SystemExit(f"routine obligation mutation survived: {relative}: {obligation}")
mutated = dict(contents)
mutated["pnext-item/SKILL.md"] = "references/strict-item-lifecycle.md\n" + mutated["pnext-item/SKILL.md"]
try:
    validate_routine_owner(mutated)
except SystemExit:
    pass
else:
    raise SystemExit("heavy protocol before routine stop passed")
for replacement in ("https://example.invalid/pnext-item/SKILL.md", "pnext-item"):
    mutated = dict(contents)
    mutated["work-roadmap/SKILL.md"] = mutated["work-roadmap/SKILL.md"].replace(ROUTINE_OWNER_LINK, replacement)
    try:
        validate_routine_owner(mutated)
    except SystemExit:
        pass
    else:
        raise SystemExit("unreachable delegated routine owner passed")
mutated = dict(contents)
del mutated["pnext-item/SKILL.md"]
try:
    validate_routine_owner(mutated)
except SystemExit:
    pass
else:
    raise SystemExit("missing delegated routine owner passed")

validate_routes(contents)
for relative, body in contents.items():
    require(body == (ROOT / ".claude/skills" / relative).read_text(),
            f"authored mirrors differ: {relative}")
# Each route has a failing control for lost applicability, unreadable content and omitted obligations.
for name, references in ROUTES.items():
    for reference, (condition, *witnesses) in references.items():
        for relative, witness in ((f"{name}/SKILL.md", condition),
                                  (f"{name}/SKILL.md", f"references/{reference}"),
                                  *((f"{name}/references/{reference}", w) for w in witnesses)):
            mutated = dict(contents)
            mutated[relative] = mutated[relative].replace(witness, "removed-route-witness")
            try:
                validate_routes(mutated)
            except SystemExit:
                pass
            else:
                raise SystemExit(f"conditional-route mutation survived: {relative}: {witness}")
        mutated = dict(contents)
        del mutated[f"{name}/references/{reference}"]
        try:
            validate_routes(mutated)
        except SystemExit:
            pass
        else:
            raise SystemExit(f"unreadable conditional reference passed: {reference}")

# Materialize the complete CoordinationKit, then traverse every pnext entry/reference link.
# Drivers' independent fixture separately proves its optional-kit-free carrier closure.
kit = json.loads((ROOT / "registry/coordination-kit-skill-manifest.json").read_text())
with tempfile.TemporaryDirectory(prefix="pnext-carrier-links-") as scratch:
    workspace = Path(scratch)
    for runtime in (".agents", ".claude"):
        for row in kit["skills"]:
            for entry in row["files"]:
                relative = Path(runtime) / "skills" / row["id"] / entry["path"]
                target = workspace / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(ROOT / relative, target)
        markdown_files = list((workspace / runtime / "skills/pnext-item").rglob("*.md"))
        def materialized_links() -> None:
            for source in markdown_files:
                for target in LINK.findall(source.read_text()):
                    if ":" in target or target.startswith("#"):
                        continue
                    require((source.parent / target.split("#", 1)[0]).is_file(),
                            f"pnext carrier cannot load {source.relative_to(workspace)}: {target}")
        materialized_links()
        performance = workspace / runtime / "skills/pnext-item/references/performance-first.md"
        original = performance.read_bytes()
        performance.unlink()
        try:
            materialized_links()
        except SystemExit:
            pass
        else:
            raise SystemExit("missing materialized performance gate passed")
        performance.write_bytes(original)

constitution = (ROOT / ".fsgg/constitution.md").read_text()
require("Routine Delivery Is the Default" in constitution, "constitution did not adopt routine-by-default doctrine")
for number in ("0053", "0079", "0080", "0081", "0082"):
    adr = next((ROOT / "docs/adr").glob(f"{number}-*.md")).read_text()
    require("routine" in adr.lower(), f"ADR-{number} does not acknowledge routine delivery")

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
for phrase in ("pull_request_target:", "contents: read", 'show "$observed_base:scripts/check-claim-generation.py"', "Repository writers are trusted"):
    require(phrase in routine_workflow, f"trusted routine workflow omitted {phrase!r}")
require("actions/checkout" not in routine_workflow and "GITHUB_TOKEN" not in routine_workflow, "trusted routine workflow can execute candidate bytes or exposes a token")
require("pull_request:" in selftest_workflow and "routine-eligibility-fixture" in selftest_workflow, "ordinary fixture workflow is absent")
require("bash tests/routine-eligibility/run.sh" in selftest_workflow, "ordinary fixture does not execute its test")
print("PASS  routine development route: active default, human-only heavy trigger, safeguards, ADRs, roadmap, and twins agree")

#!/usr/bin/env python3
"""Focused boundary tests for the skill catalog metadata validator."""

from __future__ import annotations

import importlib.machinery
import importlib.util
import json
import pathlib
import shutil
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[2]
TOOL = REPO / "scripts" / "generate-driver-manifest"
loader = importlib.machinery.SourceFileLoader("driver_manifest", str(TOOL))
spec = importlib.util.spec_from_loader(loader.name, loader)
assert spec is not None
module = importlib.util.module_from_spec(spec)
loader.exec_module(module)


def skill_text(name: str, description: str) -> str:
    return f"---\nname: {name}\ndescription: {description}\n---\n\n# {name}\n"


def make_catalog(base: pathlib.Path, description: str) -> None:
    fixtures = {}
    for root in module.SKILL_ROOTS:
        path = base / root / "sample-skill" / "SKILL.md"
        path.parent.mkdir(parents=True)
        path.write_text(skill_text("sample-skill", description))
    fixtures["sample-skill"] = {
        kind: f"{kind} routing prompt" for kind in module.TRIGGER_KINDS
    }
    fixture_path = base / module.TRIGGER_FIXTURES
    fixture_path.parent.mkdir(parents=True)
    fixture_path.write_text(json.dumps(fixtures))


def findings(base: pathlib.Path) -> list[str]:
    return module.validate_catalog(str(base))[1]


with tempfile.TemporaryDirectory(prefix="skill-catalog-") as raw:
    root = pathlib.Path(raw)
    valid = "Use when " + "x" * (module.DESCRIPTION_MIN - len("Use when "))
    make_catalog(root, valid)
    assert not findings(root), findings(root)

    skill = root / ".agents/skills/sample-skill/SKILL.md"
    skill.write_text(skill_text("sample-skill", "x" * (module.DESCRIPTION_MIN - 1)))
    assert any("description length" in item for item in findings(root))
    skill.write_text(skill_text("sample-skill", "x" * (module.DESCRIPTION_MAX + 1)))
    assert any("description length" in item for item in findings(root))
    skill.write_text(skill_text("sample-skill", "placeholder"))
    errors = findings(root)
    assert any("placeholder" in item for item in errors)

    shutil.copyfile(
        root / ".codex/skills/sample-skill/SKILL.md",
        root / ".agents/skills/sample-skill/SKILL.md",
    )
    skill.write_text(skill_text("wrong-name", valid))
    assert any("must match directory" in item for item in findings(root))

    skill.write_text(skill_text("sample-skill", valid))
    bad = root / ".agents/skills/Bad_Name"
    bad.mkdir()
    (bad / "SKILL.md").write_text(skill_text("Bad_Name", valid))
    assert any("lower-kebab-case" in item for item in findings(root))

    shutil.rmtree(bad)
    skill.write_text("---\nname: sample-skill\ndescription: 123\n---\n")
    assert any("must be a string" in item for item in findings(root))

    budget_root = root / "budget"
    budget_fixtures = {}
    budget_skill_count = module.DESCRIPTION_CEILING // module.DESCRIPTION_MAX + 2
    for index in range(budget_skill_count):
        name = f"budget-skill-{index}"
        for runtime in module.SKILL_ROOTS:
            path = budget_root / runtime / name / "SKILL.md"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(skill_text(name, "x" * module.DESCRIPTION_MAX))
        budget_fixtures[name] = {
            kind: f"{kind} routing prompt" for kind in module.TRIGGER_KINDS
        }
    budget_fixture_path = budget_root / module.TRIGGER_FIXTURES
    budget_fixture_path.parent.mkdir(parents=True)
    budget_fixture_path.write_text(json.dumps(budget_fixtures))
    budget_errors = findings(budget_root)
    assert any("descriptions cost" in item for item in budget_errors)
    assert any("names + descriptions + paths cost" in item for item in budget_errors)
    assert any("Codex effective exposure costs" in item for item in budget_errors)

print("catalog metadata boundaries: ok")


def assert_live_status_contract(skill_name: str) -> None:
    paths = [
        REPO / root / skill_name / "SKILL.md"
        for root in module.SKILL_ROOTS
    ]
    bodies = [path.read_bytes() for path in paths]
    assert len(set(bodies)) == 1, f"{skill_name} must remain byte-identical across all skill roots"

    text = " ".join(bodies[0].decode().split())
    required = [
        "Report live item state immediately.",
        "<item> — <new status>: <work in progress or gate being awaited>",
        "listing every currently active item and its current activity or gate.",
        "Do not defer either line to a wave summary or final response.",
        "Keep the driver turn alive while any item remains active",
    ]
    missing = [fragment for fragment in required if fragment not in text]
    assert not missing, f"{skill_name} lost its live status reporting contract: {missing}"


for board_driver in ("drive-board", "work-board"):
    assert_live_status_contract(board_driver)

print("board-driver live status contract: ok")


def mirrored_skill_files(skill_name: str, relative_path: str) -> list[pathlib.Path]:
    return [
        REPO / root / skill_name / relative_path
        for root in module.SKILL_ROOTS
    ]


def assert_performance_first_contract() -> None:
    reference_paths = mirrored_skill_files(
        "pnext-item", "references/performance-first.md"
    )
    reference_bodies = [path.read_bytes() for path in reference_paths]
    assert len(set(reference_bodies)) == 1, (
        "performance-first guidance must remain byte-identical across all skill roots"
    )
    reference = " ".join(reference_bodies[0].decode().split())

    phases = ["PERF-PLAN", "PERF-SMOKE", "PERF-IMPLEMENT", "PERF-RELEASE", "PERF-REPORT"]
    offsets = [reference.index(phase) for phase in phases]
    assert offsets == sorted(offsets), "performance phases must remain ordered before implementation"

    required = [
        "A non-interactive product with no active typed performance intent has no performance gate",
        "Invoke each focused product or subsystem skill by name",
        "A `Placeholder`, synthetic-only, missing, or stale workload cannot pass this gate.",
        "State expected scale and structural budgets before code changes.",
        "Smoke is iteration evidence only; it is never ship evidence.",
        "If worker-created scope changes the route, workload, expected scale, budget, or touched subsystem, return to PERF-PLAN",
        "scene nodes, search expansions, blocker-index builds, allocation/update counts, raw-input-to-applied ratio, and moving-versus-interpolated actors.",
        "full Release `Test`/`Verify` performance route against the exact candidate",
        "independent Governance verdict",
        "linked blocking performance-debt issue.",
        "Surface a human decision or environment/capability blocker once with the next action and stop retrying it; do not spin.",
    ]
    missing = [fragment for fragment in required if fragment not in reference]
    assert not missing, f"performance-first worker contract lost required behavior: {missing}"

    for driver in ("work-board", "work-roadmap"):
        driver_paths = mirrored_skill_files(driver, "SKILL.md")
        driver_bodies = [path.read_bytes() for path in driver_paths]
        assert len(set(driver_bodies)) == 1, (
            f"{driver} must remain byte-identical across all skill roots"
        )
        text = " ".join(driver_bodies[0].decode().split())
        assert "During worker setup, interactive/game work must explicitly invoke" in text
        assert "`pnext-item` performance-first planning gate" in text
        assert "../pnext-item/" not in text
        assert text.index("performance-first planning") < text.index(
            "before implementation begins"
        )

    pnext_paths = mirrored_skill_files("pnext-item", "SKILL.md")
    pnext_bodies = [path.read_bytes() for path in pnext_paths]
    assert len(set(pnext_bodies)) == 1, (
        "pnext-item must remain byte-identical across all skill roots"
    )
    pnext = " ".join(pnext_bodies[0].decode().split())
    assert "Before implementing interactive/game work" in pnext
    assert "references/performance-first.md" in pnext


assert_performance_first_contract()
print("performance-first driver contract: ok")

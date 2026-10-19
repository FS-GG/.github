#!/usr/bin/env python3
"""Focused boundary tests for the skill catalog metadata validator."""

from __future__ import annotations

import importlib.machinery
import importlib.util
import json
import pathlib
import re
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
    manifest_path = base / module.KIT_MANIFEST_PATH
    manifest_path.parent.mkdir(parents=True)
    manifest_path.write_text(json.dumps({"schemaVersion": 2, "skills": [{"id": "sample-skill"}]}))


def findings(base: pathlib.Path) -> list[str]:
    return module.validate_catalog(str(base))[1]


with tempfile.TemporaryDirectory(prefix="skill-catalog-") as raw:
    root = pathlib.Path(raw)
    valid = "Use when " + "x" * (module.DESCRIPTION_MIN - len("Use when "))
    make_catalog(root, valid)
    assert not findings(root), findings(root)

    # The catalog must not silently accept a directory omitted from both producer classifications.
    (root / module.KIT_MANIFEST_PATH).write_text(json.dumps({"schemaVersion": 2, "skills": []}))
    assert any("undeclared skill directories" in item for item in findings(root))
    (root / module.KIT_MANIFEST_PATH).write_text(
        json.dumps({"schemaVersion": 2, "skills": [{"id": "sample-skill"}]})
    )

    skill = root / ".agents/skills/sample-skill/SKILL.md"
    skill.write_text(skill_text("sample-skill", "x" * (module.DESCRIPTION_MIN - 1)))
    assert any("description length" in item for item in findings(root))
    skill.write_text(skill_text("sample-skill", "x" * (module.DESCRIPTION_MAX + 1)))
    assert any("description length" in item for item in findings(root))
    skill.write_text(skill_text("sample-skill", "placeholder"))
    errors = findings(root)
    assert any("placeholder" in item for item in errors)

    # Restore the mutated root from the OTHER root's pristine copy, so the next assertion sees a
    # metadata finding rather than a cross-root divergence. The source was `.codex` until ADR-0067 §5
    # retired it (.github#1636); `.claude` is the surviving counterpart of `.agents`.
    shutil.copyfile(
        root / ".claude/skills/sample-skill/SKILL.md",
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
    # Exercise every independent ceiling; raising one does not imply another is
    # crossed by a fixture sized only from the description allowance.
    minimum_entry_chars = (
        module.DESCRIPTION_MAX
        + len("budget-skill-0")
        + min(len(f"{runtime}/budget-skill-0/SKILL.md") for runtime in module.SKILL_ROOTS)
    )
    budget_skill_count = max(
        module.DESCRIPTION_CEILING // module.DESCRIPTION_MAX,
        module.ROOT_CATALOG_CEILING // minimum_entry_chars,
        module.CODEX_EFFECTIVE_CEILING // (minimum_entry_chars * len(module.CODEX_SKILL_ROOTS)),
    ) + 2
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


quality_spec = importlib.util.spec_from_file_location("skill_quality", REPO / "scripts/check-skill-quality.py")
quality = importlib.util.module_from_spec(quality_spec)
quality_spec.loader.exec_module(quality)


def assert_live_status_contract(skill_name: str) -> None:
    sources = ("SKILL.md", "references/lifecycle-contract.md")
    for relative in sources:
        bodies = [(REPO / runtime / skill_name / relative).read_bytes() for runtime in module.SKILL_ROOTS]
        assert len(set(bodies)) == 1, f"{skill_name}/{relative} must remain byte-identical across all skill roots"
    required = (
        "Update on deliverables, changed blockers, decisions or changed promised times, plus one daily active summary.",
        "Use existing owning/native evidence",
        "Failed/partial reads keep Unknown and the hold; silence releases nothing.",
        "No fixed polling, unchanged-hold comments, reporting-only workers or CI-tick projections.",
        "Project 3 or the selected product board supplies current planning",
    )
    def valid(candidate):
        normalized = " ".join(candidate.split())
        return all(fragment in normalized for fragment in required) and "driver --events --cursor" not in normalized
    for runtime in module.SKILL_ROOTS:
        errors = []
        text = quality.work_board_instruction_text(REPO, runtime, errors)
        assert not errors, errors
        assert valid(text), f"{skill_name} lost its scoped owning/native reporting contract"
        # Mutate actual on-disk carriers and resolve them through the production reader. A
        # correctly preserved reference cannot rescue a missing entry link or an absent target.
        with tempfile.TemporaryDirectory(prefix="work-board-reporting-") as raw:
            root = pathlib.Path(raw)
            directory = root / runtime / skill_name
            originals = {relative: (REPO / runtime / skill_name / relative).read_text() for relative in sources}
            def restore():
                for relative, body in originals.items():
                    path = directory / relative
                    path.parent.mkdir(parents=True, exist_ok=True)
                    path.write_text(body)
            def read():
                errors = []
                text = quality.work_board_instruction_text(root, runtime, errors)
                return text, errors
            restore()
            assert valid(read()[0]) and not read()[1]
            for fragment in required:
                pattern = r"\s+".join(re.escape(word) for word in fragment.split())
                for relative, body in originals.items():
                    (directory / relative).write_text(re.sub(pattern, "", body))
                candidate, errors = read()
                assert not errors, errors
                assert not valid(candidate), f"missing reporting clause accepted: {fragment}"
                restore()
            for relative in sources:
                path = directory / relative
                path.write_text(path.read_text() + "\nRun driver --events --cursor current.json --text.\n")
                assert not valid(read()[0]), f"V1 event authority returned in {relative}"
                restore()
            entry = directory / "SKILL.md"
            entry.write_text(originals["SKILL.md"].replace("references/lifecycle-contract.md", "references/unused-contract.md"))
            assert read()[1], "unreachable reporting reference accepted"
            restore()
            (directory / "references/lifecycle-contract.md").unlink()
            assert read()[1], "missing reporting reference accepted"


# Organization and selected product load the same reporting boundary through the real
# progressive entrypoint. Optional read-only reporting remains separate from scheduling.
assert_live_status_contract("work-board")

org_errors = []
quality.validate_organization_consumer(REPO, org_errors)
assert not org_errors, org_errors

print("canonical scoped reporting and organization contracts: ok")


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
        relative = "references/heavyweight-roadmap.md" if driver == "work-roadmap" else "references/lifecycle-contract.md"
        driver_paths = mirrored_skill_files(driver, relative)
        driver_bodies = [path.read_bytes() for path in driver_paths]
        assert len(set(driver_bodies)) == 1, (
            f"{driver} must remain byte-identical across all skill roots"
        )
        if driver == "work-roadmap":
            entries = [path.read_bytes() for path in mirrored_skill_files(driver, "SKILL.md")]
            assert len(set(entries)) == 1, "work-roadmap entrypoints differ across skill roots"
            entry = " ".join(entries[0].decode().split())
            assert "Interactive/game implementation" in entry
            assert "references/performance-first.md" in entry and "before implementation begins" in entry
            assert "../pnext-item/" not in entry, "routine performance guidance must be local to the driver carrier"
            for path in mirrored_skill_files(driver, "references/performance-first.md"):
                assert path.read_bytes() == reference_bodies[0], "driver-local performance gate lost canonical obligations"
        text = " ".join(driver_bodies[0].decode().split())
        if driver == "work-board":
            for runtime in module.SKILL_ROOTS:
                errors = []
                text = " ".join(quality.work_board_instruction_text(REPO, runtime, errors).split())
                assert not errors, errors
                carriers = [REPO / runtime / driver / path for path in ("SKILL.md", relative)]
                targets = {(carrier.parent / target).resolve() for carrier in carriers
                           for target in quality.LINK.findall(carrier.read_text())
                           if not target.startswith(("https:", "http:", "#"))}
                assert (REPO / runtime / "work-roadmap/SKILL.md").resolve() in targets
                assert (REPO / runtime / "work-roadmap/references/performance-first.md").resolve() in targets
                assert ("Interactive/game implementation loads the "
                        "[performance-first planning gate](../../work-roadmap/references/performance-first.md) "
                        "before implementation begins") in text
                assert "does not select a product lane or restart profiling" in text
            continue
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
    assert "Interactive/game implementation" in pnext
    assert "references/performance-first.md" in pnext


assert_performance_first_contract()
print("performance-first driver contract: ok")

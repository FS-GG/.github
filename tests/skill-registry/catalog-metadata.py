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

print("catalog metadata boundaries: ok")

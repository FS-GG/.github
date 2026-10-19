#!/usr/bin/env python3
"""Exercise production lifecycle routing with real temporary caller trees and causal mutations.

This proves source instruction/caller closure only; it does not simulate native scheduling, GitHub
writes, installed activation or acceptance. The existing qualified runtime tests own those effects.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path
import shutil
import sys
import tempfile

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quality", ROOT / "scripts/check-skill-quality.py")
quality = importlib.util.module_from_spec(spec)
spec.loader.exec_module(quality)


def inspect(root: Path) -> list[str]:
    errors = []
    quality.validate_lifecycle_routes(root, errors)
    return errors


def mutate(root: Path, relative: str, before: str, after: str, expected: str) -> None:
    path = root / relative
    original = path.read_text()
    assert before in original, (relative, before)
    path.write_text(original.replace(before, after, 1))
    try:
        errors = inspect(root)
        assert any(expected in error for error in errors), (relative, errors)
    finally:
        path.write_text(original)
    assert not inspect(root), inspect(root)


with tempfile.TemporaryDirectory(prefix="board-lifecycle-") as temporary:
    tree = Path(temporary)
    # Authored instructions are projected into each runtime in the isolated candidate. The root
    # delivery separately verifies real mirror generation; this test does not mutate generated files.
    for runtime in quality.ROOTS:
        shutil.copytree(ROOT / ".claude/skills", tree / runtime)
    shutil.copytree(ROOT / ".claude/agents", tree / ".claude/agents")
    assert not inspect(tree), inspect(tree)
    controls = 0
    for runtime in quality.ROOTS:
        mutations = (
            ("p-add/SKILL.md", "../work-board/references/intake.md", "../padd-item/references/legacy-product-intake.md", "must reach work-board/references/intake.md"),
            ("work-roadmap/SKILL.md", "https://github.com/FS-GG/.github/blob/main/.agents/skills/pnext-item/SKILL.md", "https://github.com/FS-GG/.github/blob/main/.agents/skills/pnext-item/references/strict-item-lifecycle.md", "must reach pnext-item/SKILL.md"),
            ("padd-item/SKILL.md", "references/legacy-product-intake.md", "references/missing-product-intake.md", "must reach padd-item/references/legacy-product-intake.md"),
            ("drive-board/references/host-loop.md", "../../work-board/references/host-loop.md", "../../work-programme/SKILL.md", "must reach work-board/references/host-loop.md"),
            ("pnext-item/SKILL.md", "## Conditional instructions", "```sh\nscripts/fsgg-coord claim 4702\n```\n\n## Conditional instructions", "prescribes retired V1 operation"),
            ("intra-repo-parallel-work/SKILL.md", "# Parallel work", "```sh\nscripts/fsgg-coord take --repo .github\n```\n\n# Parallel work", "prescribes retired V1 operation"),
        )
        for relative, before, after, expected in mutations:
            mutate(tree, f"{runtime}/{relative}", before, after, expected)
            controls += 1
        # Removing a referenced endpoint must fail even if every caller still names its expected URL.
        for reference in ("intake.md", "lifecycle-contract.md"):
            endpoint = tree / runtime / "work-board/references" / reference
            saved = endpoint.read_text()
            endpoint.unlink()
            errors = inspect(tree)
            assert any(f"must reach work-board/references/{reference}" in error for error in errors), errors
            endpoint.write_text(saved)
            controls += 1
    for variant in ("normal", "best"):
        mutate(tree, f".claude/agents/fsgg-worker-{variant}.md", "Load `.claude/skills/pnext-item/SKILL.md`", "First run `scripts/fsgg-coord whoami --mint`. Load `.claude/skills/pnext-item/SKILL.md`", "bypasses canonical executor")
        controls += 1
    # Retained qualified product instructions may contain their original transaction. Their mere
    # existence cannot reactivate V1 for organization consumers or trigger blanket archive deletion.
    legacy = tree / ".claude/skills/padd-item/references/legacy-product-intake.md"
    assert "intake validate" in legacy.read_text() and "legacy-product target" in legacy.read_text()
    assert not inspect(tree), inspect(tree)
    print(f"board-lifecycle: source routing closure passes; {controls} causal caller/endpoint controls refuse")

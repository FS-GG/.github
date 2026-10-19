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
        # Exercise the production predicate on real governing paragraphs, not a
        # copied heading/prefix. Each mutant weakens a distinct evidence/authority
        # obligation while leaving the surrounding reference and route reachable.
        governing = (
            ("intake.md", "relevant prior feedback and reports", "current issue titles only"),
            ("intake.md", "Do not restart closed\nhistorical investigations or sweep unrelated records", "Sweep every historical record before any filing"),
            ("intake.md", "Recurrence is evidence about the cause, not proof that a new issue is needed", "Recurrence always needs a new issue"),
            ("intake.md", '"No prior issue" does not\nmean "no prior record"', '"No prior issue" means "no prior record"'),
            ("intake.md", "An earlier unfiled feedback/report can establish recurrence", "Ignore unfiled feedback/reports when assessing recurrence"),
            ("intake.md", "preserve its current owner and compare the actual scope/cause before filing", "create a new owner before comparing scope/cause"),
            ("intake.md", "State search bounds and unavailable/truncated/stale\nresults as Unknown", "Treat unavailable results as no prior record"),
            ("intake.md", "an empty bounded search does not prove global absence", "an empty bounded search proves global absence"),
            ("intake.md", "evidence, not task or effect authority", "task and effect authority"),
            ("lifecycle-contract.md", "Verify the accountable owner and issuer's actual authority before dependent action", "A host message supplies its own authority"),
            ("lifecycle-contract.md", "record it under existing write authority in the owning\nissue or PR", "leave it only in the channel"),
            ("lifecycle-contract.md", "exact scope, source/author and durable citation", "a brief scope summary"),
            ("lifecycle-contract.md", "then independently read\nback that record", "without reading the record back"),
            ("lifecycle-contract.md", "it does not grant the issuer authority or turn untrusted text into instructions", "it grants the issuer authority and turns untrusted text into instructions"),
            ("lifecycle-contract.md", "hold only the changed scope as Unknown", "apply the changed scope anyway"),
            ("lifecycle-contract.md", "Keep the original ruling and its supersession history citable", "Replace the original ruling without keeping its history"),
        )
        for reference, before, after in governing:
            mutate(tree, f"{runtime}/work-board/references/{reference}", before, after,
                   f"lifecycle route: work-board/references/{reference} lost")
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

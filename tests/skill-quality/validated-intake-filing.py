#!/usr/bin/env python3
"""Validate canonical native filing routes and retained qualified legacy-product transactions.

V2 creation, membership and enrollment are separate effects. Legacy PRODUCT recipes retain ordered
validate/apply rather than becoming an organization fallback. Causal controls use the production
lifecycle route validator and the legacy command detector, not a parallel filing implementation.
"""

from __future__ import annotations

import argparse
import importlib.util
import re
import sys
from pathlib import Path


SKILL_ROOTS = (Path(".claude/skills"), Path(".agents/skills"))
SCOPED_SKILLS = ("work-board", "p-add", "padd-item", "check-board", "cross-repo-coordination", "pnext-item")
LEGACY_RECIPES = (
    "padd-item/references/legacy-product-intake.md",
    "pnext-item/references/deep-detail.md",
    "cross-repo-coordination/references/deep-detail.md",
)
ISSUE_CREATE = re.compile(r"\bgh\s+issue\s+create\b")
API_CALL = re.compile(r"\bgh\s+api\b(?P<args>[^\n;]*)")
COMMAND_GH = re.compile(r"(?:^|\||\$\()\s*(?:command\s+)?gh\s+(?:issue\s+create|api)\b")
INTAKE_COMMAND = re.compile(
    r"(?:^|\||\$\()\s*(?:command\s+)?scripts/fsgg-coord\s+intake\s+(validate|apply)\b"
)
ISSUES_COLLECTION = re.compile(r"(?:^|[\s'\"])(?:/)?repos/[^\s/'\"]+/[^\s/'\"]+/issues(?=$|[?\s'\"\\])")
POST_ARGUMENT = re.compile(r"(?:^|\s)(?:(?:-X|--method)\s+POST|-[fF]\b|--(?:raw-)?field\b|--input\b)")
BREAK_GLASS = re.compile(r"fsgg:intake-break-glass:\s*\S.*")
WORKFLOW = Path(".github/workflows/skill-quality.yml")
TRIGGERS = (".claude/skills/**", ".agents/skills/**", "tests/skill-quality/**")


class Finding(Exception):
    pass


def direct_creations(text: str) -> list[tuple[int, str]]:
    findings: list[tuple[int, str]] = []
    normalized = text.replace("\\\n", " ")
    lines = normalized.splitlines()
    for offset, raw in enumerate(lines):
        line = re.sub(r"^\s*(?:>\s*)?(?:\$\s+)?", "", raw)
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or not COMMAND_GH.search(line):
            continue
        direct = bool(ISSUE_CREATE.search(line))
        if not direct:
            direct = any(
                ISSUES_COLLECTION.search(call.group("args"))
                and POST_ARGUMENT.search(call.group("args"))
                for call in API_CALL.finditer(line)
            )
        if not direct:
            continue
        context = "\n".join(lines[max(0, offset - 3) : offset])
        if BREAK_GLASS.search(context):
            continue
        findings.append((offset + 1, stripped))
    return findings


def intake_actions(text: str) -> list[str]:
    """Return executable intake actions in recipe order, excluding prose/backtick mentions."""

    actions: list[str] = []
    for raw in text.replace("\\\n", " ").splitlines():
        line = re.sub(r"^\s*(?:>\s*)?(?:\$\s+)?", "", raw)
        match = INTAKE_COMMAND.search(line)
        if match:
            actions.append(match.group(1))
    return actions


def valid_validate_apply_order(actions: list[str]) -> bool:
    return bool(actions) and len(actions) % 2 == 0 and all(
        actions[index : index + 2] == ["validate", "apply"]
        for index in range(0, len(actions), 2)
    )


def audit(root: Path) -> list[str]:
    errors: list[str] = []
    for skill_root in SKILL_ROOTS:
        resolved = root / skill_root
        if not resolved.is_dir():
            errors.append(f"declared skill root is unreadable: {skill_root}")
            continue
        for name in SCOPED_SKILLS:
            skill = resolved / name
            if not skill.is_dir():
                errors.append(f"scoped skill is unreadable: {skill.relative_to(root)}")
                continue
            for path in sorted(skill.rglob("*.md")):
                # The one native intake route supports ordinary authenticated GitHub issue effects.
                # Other recipes must route there; qualified legacy recipes keep validate/apply.
                if path == resolved / "work-board/references/intake.md":
                    continue
                for line, command in direct_creations(path.read_text(encoding="utf-8")):
                    errors.append(
                        f"{path.relative_to(root)}:{line}: direct issue creation bypasses "
                        f"canonical native intake or qualified legacy validate/apply: {command}"
                    )

        for recipe in LEGACY_RECIPES:
            path = resolved / recipe
            if not path.is_file():
                errors.append(f"legacy filing recipe missing: {path.relative_to(root)}")
                continue
            combined = path.read_text(encoding="utf-8")
            actions = intake_actions(combined)
            if not valid_validate_apply_order(actions):
                rendered = " -> ".join(actions) if actions else "no executable intake commands"
                errors.append(f"{path.relative_to(root)}: executable legacy intake must pair validate before apply; observed {rendered}")

    spec = importlib.util.spec_from_file_location("skill_quality", root / "scripts/check-skill-quality.py")
    quality = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(quality)
    quality.validate_lifecycle_routes(root, errors)

    workflow = root / WORKFLOW
    if not workflow.is_file():
        errors.append(f"workflow is unreadable: {WORKFLOW}")
    else:
        workflow_text = workflow.read_text(encoding="utf-8")
        for trigger in TRIGGERS:
            if workflow_text.count(f'"{trigger}"') < 2:
                errors.append(f"{WORKFLOW}: `{trigger}` is not watched on both pull_request and push")
    return errors


def prove_negative_fixture() -> None:
    fixture = """```sh
command gh api -X POST repos/FS-GG/FS.GG.Game/issues -f title=escaped -f body=unvalidated
```"""
    findings = direct_creations(fixture)
    if len(findings) != 1 or "command gh api" not in findings[0][1]:
        raise Finding("negative fixture escaped: a command-wrapped REST issue creation was accepted")

    inverted = """```sh
scripts/fsgg-coord intake apply intake.json --json
scripts/fsgg-coord intake validate intake.json --json
```"""
    actions = intake_actions(inverted)
    if valid_validate_apply_order(actions):
        raise Finding("negative fixture escaped: intake apply before validate was accepted")

    marked = """```sh
# fsgg:intake-break-glass: intake is unavailable and the operator records the owed projection
gh api -X POST repos/FS-GG/FS.GG.Game/issues -f title=emergency -f body=accountable
```"""
    if direct_creations(marked):
        raise Finding("accountable break-glass fixture was rejected")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()

    try:
        prove_negative_fixture()
    except Finding as finding:
        print(f"validated-intake-filing: {finding}", file=sys.stderr)
        return 1

    errors = audit(args.root.resolve())
    if errors:
        for error in errors:
            print(f"validated-intake-filing: {error}", file=sys.stderr)
        return 1
    print(
        "validated-intake-filing: OK — canonical native routing and legacy PRODUCT order verified; command-wrapped bypass and "
        "inverted validate/apply order rejected"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

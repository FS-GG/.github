#!/usr/bin/env python3
"""Reject skill recipes that bypass the validated intake transaction (.github#2736).

The filing contract has two independent halves:

1. Filing-capable skills show a complete ``fsgg.coord.intake/v1`` draft and prescribe
   ``intake validate`` followed by ``intake apply``.
2. No shell recipe creates an issue directly. A genuinely necessary escape hatch must carry an
   adjacent ``fsgg:intake-break-glass: <conditions>`` comment, so an exception is accountable and
   searchable rather than inferred from a historical explanation.

Both declared runtime roots are scanned. The negative fixture at the bottom deliberately injects a
direct creation recipe and proves the same detector rejects it.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path


SKILL_ROOTS = (Path(".claude/skills"), Path(".agents/skills"))
SCOPED_SKILLS = ("p-add", "padd-item", "check-board", "cross-repo-coordination", "pnext-item")
FILING_SKILLS = ("p-add", "padd-item", "cross-repo-coordination", "pnext-item")
ISSUE_CREATE = re.compile(r"\bgh\s+issue\s+create\b")
API_CALL = re.compile(r"\bgh\s+api\b(?P<args>[^\n;]*)")
COMMAND_GH = re.compile(r"(?:^|\||\$\()\s*gh\s+(?:issue\s+create|api)\b")
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
                for line, command in direct_creations(path.read_text(encoding="utf-8")):
                    errors.append(
                        f"{path.relative_to(root)}:{line}: direct issue creation bypasses "
                        f"`intake validate` + `intake apply`: {command}"
                    )

        for name in FILING_SKILLS:
            skill = resolved / name
            if not skill.is_dir():
                continue
            combined = "\n".join(
                path.read_text(encoding="utf-8") for path in sorted(skill.rglob("*.md"))
            )
            for required in (
                "fsgg.coord.intake/v1",
                "scripts/fsgg-coord intake validate",
                "scripts/fsgg-coord intake apply",
            ):
                if required not in combined:
                    errors.append(
                        f"{skill.relative_to(root)}: filing contract does not prescribe `{required}`"
                    )

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
gh api -X POST repos/FS-GG/FS.GG.Game/issues -f title=escaped -f body=unvalidated
```"""
    findings = direct_creations(fixture)
    if len(findings) != 1 or "gh api" not in findings[0][1]:
        raise Finding("negative fixture escaped: a direct REST issue creation recipe was accepted")

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
    print("validated-intake-filing: OK — validated filing prescribed; negative bypass rejected")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

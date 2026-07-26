#!/usr/bin/env python3
"""End-to-end driver delivery and feedback-state regression fixture."""

from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".claude", ".codex", ".agents")
DRIVERS = ("work-roadmap", "work-board")
LINK = re.compile(r"\[[^\]]+\]\(([^)#]+)(?:#[^)]+)?\)")


def check(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def report(cycle: str, events: int, reason: str, phases: str) -> str:
    return f"""---
feedbackSchema: 2
date: 2026-07-26
workspace: fixture
cycle: {cycle}
lane: sdd
toolVersion: fixture
commit: fixture
---

## §1 Provenance and confidence

- **activation:** active
- **phases:** {phases}
- **material events:** {events}
- **zero-event reason:** {reason}
"""


def run_gate(root: Path, driver: str, cycle: str, report_path: str, phases: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            sys.executable,
            str(root / ".agents" / "skills" / driver / "scripts" / "validate-feedback-state.py"),
            "--root",
            str(root),
            "--cycle",
            cycle,
            "--report",
            report_path,
            "--phases",
            phases,
        ],
        text=True,
        capture_output=True,
        check=False,
    )


def main() -> None:
    manifest = json.loads((ROOT / "registry/driver-skill-manifest.json").read_text())
    rows = {row["id"]: row for row in manifest["skills"]}

    with tempfile.TemporaryDirectory(prefix="driver-feedback-delivery-") as temp:
        workspace = Path(temp)
        for runtime in RUNTIMES:
            # The coordination kit is a separate delivered prerequisite. Seed the one cross-skill path
            # these driver bodies reference so this fixture checks the composed workspace, not a package
            # in isolation.
            for prerequisite in ("check-board", "intra-repo-parallel-work", "pnext-item"):
                shutil.copytree(
                    ROOT / runtime / "skills" / prerequisite,
                    workspace / runtime / "skills" / prerequisite,
                )

            lifecycle = workspace / runtime / "skills" / "fs-gg-sdd-lifecycle" / "SKILL.md"
            lifecycle.parent.mkdir(parents=True, exist_ok=True)
            lifecycle.write_text("# fixture lifecycle skill\n")

            feedback = workspace / runtime / "skills" / "fs-gg-feedback-report"
            (feedback / "scripts").mkdir(parents=True, exist_ok=True)
            (feedback / "SKILL.md").write_text("# fixture feedback skill\n")
            (feedback / "scripts" / "feedback-tool.fsx").write_text("// fixture feedback tool\n")

            for driver in DRIVERS:
                row = rows[driver]
                files = row.get("files")
                check(isinstance(files, list) and len(files) > 1, f"{driver}: manifest lost directory files")
                source = ROOT / runtime / "skills" / driver
                target = workspace / runtime / "skills" / driver
                for entry in files:
                    relative = Path(entry["path"])
                    src = source / relative
                    dst = target / relative
                    check(src.is_file(), f"{driver}: declared source missing: {src}")
                    check(
                        entry["sha256"]
                        == __import__("hashlib").sha256(src.read_bytes()).hexdigest(),
                        f"{driver}: digest drift for {relative}",
                    )
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(src, dst)
                    mode = dst.stat().st_mode
                    if entry["executable"]:
                        dst.chmod(mode | 0o111)
                    else:
                        dst.chmod(mode & ~0o111)

        for runtime in RUNTIMES:
            for driver in DRIVERS:
                directory = workspace / runtime / "skills" / driver
                for markdown in directory.rglob("*.md"):
                    for raw in LINK.findall(markdown.read_text()):
                        target = (markdown.parent / raw).resolve()
                        check(target.exists(), f"{markdown}: dangling materialized link {raw}")

        board_phases = (
            "onboarding-first-build,lifecycle-authoring-or-not-used,"
            "implementation-test-evidence,verify-ship-pr"
        )
        cycle = "item-1482-feedback-gate"
        feedback_dir = workspace / "feedback"
        feedback_dir.mkdir()

        missing = run_gate(workspace, "work-board", cycle, "feedback/missing.md", board_phases)
        check(missing.returncode == 1 and "missing or unreadable" in missing.stderr, "missing report passed")

        malformed_path = feedback_dir / "malformed.md"
        malformed_path.write_text("not a schema-v2 report\n")
        malformed = run_gate(workspace, "work-board", cycle, "feedback/malformed.md", board_phases)
        check(malformed.returncode == 1, "malformed report passed")

        unreadable_path = feedback_dir / "unreadable.md"
        unreadable_path.mkdir()
        unreadable = run_gate(workspace, "work-board", cycle, "feedback/unreadable.md", board_phases)
        check(unreadable.returncode == 1 and "missing or unreadable" in unreadable.stderr, "unreadable report passed")

        zero_path = feedback_dir / "zero.md"
        zero_path.write_text(
            report(
                cycle,
                0,
                "All named phases were exercised; no observation exceeded the materiality threshold.",
                board_phases,
            )
        )
        zero = run_gate(workspace, "work-board", cycle, "feedback/zero.md", board_phases)
        check(zero.returncode == 0, f"valid zero-event state failed: {zero.stderr}")

        event_path = feedback_dir / "event.md"
        event_path.write_text(report(cycle, 1, "n/a", board_phases))
        checkpoint = feedback_dir / "checkpoints" / f"{cycle}.jsonl"
        checkpoint.parent.mkdir()
        checkpoint.write_text(json.dumps({"cycle": cycle}) + "\n")
        event = run_gate(workspace, "work-board", cycle, "feedback/event.md", board_phases)
        check(event.returncode == 0, f"valid checkpoint state failed: {event.stderr}")

        rogue = feedback_dir / "rogue2-final.md"
        rogue.write_text(
            "---\nfeedbackSchema: 2\ndate: 2026-07-26\nworkspace: Rogue2\n"
            f"cycle: {cycle}\nlane: sdd\ntoolVersion: fixture\ncommit: ship-ready\n---\n"
            "Eleven milestones completed and ship-ready.\n"
        )
        rogue_result = run_gate(workspace, "work-board", cycle, "feedback/rogue2-final.md", board_phases)
        check(rogue_result.returncode == 1, "Rogue2 no-feedback shape passed as complete")

    print("driver feedback delivery fixture: all cases passed")


if __name__ == "__main__":
    main()

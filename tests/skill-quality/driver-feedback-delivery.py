#!/usr/bin/env python3
"""End-to-end driver delivery and feedback-state regression fixture."""

from __future__ import annotations

import json
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".claude", ".codex", ".agents")
DRIVERS = ("work-roadmap", "work-board", "padd-item")
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

## §2 What worked

None observed.
"""


def audit(report_path: str, report_text: str) -> str:
    digest = hashlib.sha256(report_text.replace("\r\n", "\n").replace("\r", "\n").encode()).hexdigest()
    return json.dumps(
        {
            "auditSchema": 1,
            "report": report_path,
            "reportSha256": digest,
            "criticMode": "fresh-context-subagent",
            "criticPromptVersion": "actionability-v1",
            "findings": [],
        }
    )


def run_gate(
    root: Path,
    driver: str,
    cycle: str,
    report_path: str,
    audit_path: str,
    phases: str,
) -> subprocess.CompletedProcess[str]:
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
            "--audit",
            audit_path,
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
            # The SDD lifecycle is guaranteed in a product scaffold. Coordination-kit skills are not:
            # a board driver may require them textually when the workspace is wired, but must not
            # advertise their conditional presence as a filesystem link.
            lifecycle = workspace / runtime / "skills" / "fs-gg-sdd-lifecycle" / "SKILL.md"
            lifecycle.parent.mkdir(parents=True, exist_ok=True)
            lifecycle.write_text("# fixture lifecycle skill\n")

            feedback = workspace / runtime / "skills" / "fs-gg-feedback-report"
            (feedback / "scripts").mkdir(parents=True, exist_ok=True)
            (feedback / "SKILL.md").write_text("# fixture feedback skill\n")
            (feedback / "scripts" / "feedback-tool.fsx").write_text(
                """open System
open System.IO

let argv = fsi.CommandLineArgs |> Array.skip 1
match argv with
| [| "validate"; report; "--audit"; audit |]
    when File.Exists report && File.Exists audit ->
    printfn "fixture: compatible report/audit validation seam"
| [| "validate-checkpoints"; checkpoints |] when File.Exists checkpoints ->
    printfn "fixture: compatible checkpoint validation seam"
| _ ->
    eprintfn "fixture: invalid feedback validation command or missing state"
    exit 1
"""
            )

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

        missing = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/missing.md",
            "feedback/audits/missing.audit.json",
            board_phases,
        )
        check(missing.returncode == 1 and "missing or unreadable" in missing.stderr, "missing report passed")

        malformed_path = feedback_dir / "malformed.md"
        malformed_path.write_text("not a schema-v2 report\n")
        malformed = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/malformed.md",
            "feedback/audits/malformed.audit.json",
            board_phases,
        )
        check(malformed.returncode == 1, "malformed report passed")

        unreadable_path = feedback_dir / "unreadable.md"
        unreadable_path.mkdir()
        unreadable = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/unreadable.md",
            "feedback/audits/unreadable.audit.json",
            board_phases,
        )
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
        audit_dir = feedback_dir / "audits"
        audit_dir.mkdir()
        zero_audit = audit_dir / "zero.audit.json"
        zero_audit.write_text(audit("feedback/zero.md", zero_path.read_text()))
        zero = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/zero.md",
            "feedback/audits/zero.audit.json",
            board_phases,
        )
        check(zero.returncode == 0, f"valid zero-event state failed: {zero.stderr}")
        product_validate = subprocess.run(
            [
                "dotnet",
                "fsi",
                str(
                    workspace
                    / ".agents"
                    / "skills"
                    / "fs-gg-feedback-report"
                    / "scripts"
                    / "feedback-tool.fsx"
                ),
                "--",
                "validate",
                "feedback/zero.md",
                "--audit",
                "feedback/audits/zero.audit.json",
            ],
            cwd=workspace,
            text=True,
            capture_output=True,
            check=False,
        )
        check(product_validate.returncode == 0, "compatible report/audit product validation failed")

        event_path = feedback_dir / "event.md"
        event_path.write_text(report(cycle, 1, "n/a", board_phases))
        event_audit = audit_dir / "event.audit.json"
        event_audit.write_text(audit("feedback/event.md", event_path.read_text()))
        checkpoint = feedback_dir / "checkpoints" / f"{cycle}.jsonl"
        checkpoint.parent.mkdir()
        checkpoint.write_text(json.dumps({"cycle": cycle}) + "\n")
        event = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/event.md",
            "feedback/audits/event.audit.json",
            board_phases,
        )
        check(event.returncode == 0, f"valid checkpoint state failed: {event.stderr}")

        misplaced_path = feedback_dir / "misplaced.md"
        misplaced_text = (
            report(cycle, 0, "qualified reason", board_phases)
            .replace(
                "- **activation:** active\n"
                f"- **phases:** {board_phases}\n"
                "- **material events:** 0\n"
                "- **zero-event reason:** qualified reason\n",
                "",
            )
            + "\n## §4 Findings\n\n"
            "- **activation:** active\n"
            f"- **phases:** {board_phases}\n"
            "- **material events:** 0\n"
            "- **zero-event reason:** qualified reason\n"
        )
        misplaced_path.write_text(misplaced_text)
        misplaced_audit = audit_dir / "misplaced.audit.json"
        misplaced_audit.write_text(audit("feedback/misplaced.md", misplaced_text))
        misplaced = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/misplaced.md",
            "feedback/audits/misplaced.audit.json",
            board_phases,
        )
        check(misplaced.returncode == 1, "activation fields outside §1 passed")

        rogue = feedback_dir / "rogue2-final.md"
        rogue_text = (
            "---\nfeedbackSchema: 2\ndate: 2026-07-26\nworkspace: Rogue2\n"
            f"cycle: {cycle}\nlane: sdd\ntoolVersion: fixture\ncommit: ship-ready\n---\n"
            "## §1 Provenance and confidence\n\nEleven milestones completed and ship-ready.\n"
            "## §2 What worked\n\nNone observed.\n"
        )
        rogue.write_text(rogue_text)
        rogue_audit = audit_dir / "rogue2-final.audit.json"
        rogue_audit.write_text(audit("feedback/rogue2-final.md", rogue_text))
        rogue_result = run_gate(
            workspace,
            "work-board",
            cycle,
            "feedback/rogue2-final.md",
            "feedback/audits/rogue2-final.audit.json",
            board_phases,
        )
        check(rogue_result.returncode == 1, "Rogue2 no-feedback shape passed as complete")

    print("driver feedback delivery fixture: all cases passed")


if __name__ == "__main__":
    main()

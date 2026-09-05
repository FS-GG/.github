#!/usr/bin/env python3
"""Semantic inversions for the permanent orchestrator status room."""

import json
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CHECK = ROOT / "scripts/check-orchestrator-room"


def comment(comment_id: int, actor: str, worker: str, state: str, *, malformed: bool = False) -> dict:
    status = (
        f"ORCHESTRATOR-STATUS worker={worker} state={state} item=FS-GG/.github#3210 "
        "claim=5551016843 head=78f76065ca68436237e78b0082caf3e303863704 note=test"
    )
    if malformed:
        status = status.replace(" state=yielded", " state=maybe")
    return {"id": comment_id, "body": f"<!-- fsgg:msg from={actor} to=* -->\n**status**\n\n{status}"}


def run(comments: list[dict]) -> subprocess.CompletedProcess[str]:
    with tempfile.NamedTemporaryFile("w", suffix=".json", encoding="utf-8") as fixture:
        json.dump(comments, fixture)
        fixture.flush()
        return subprocess.run([str(CHECK), "--input", fixture.name, "--json"], text=True, capture_output=True)


ACTIVATION = 5551249226
valid = run([comment(ACTIVATION, "finch-3178", "finch-3178", "active"), comment(ACTIVATION + 1, "finch-3178", "finch-3178", "yielded")])
assert valid.returncode == 0, valid.stderr
assert json.loads(valid.stdout)["finch-3178"]["state"] == "yielded"

forged = run([comment(ACTIVATION, "finch-3178", "finch-3178", "active"), comment(ACTIVATION + 2, "attacker-9999", "finch-3178", "yielded")])
assert forged.returncode != 0 and "differs from fsgg:msg actor" in forged.stderr

malformed_newest = run([comment(ACTIVATION, "finch-3178", "finch-3178", "active"), comment(ACTIVATION + 3, "finch-3178", "finch-3178", "yielded"), comment(ACTIVATION + 4, "finch-3178", "finch-3178", "yielded", malformed=True)])
assert malformed_newest.returncode != 0 and "malformed status line" in malformed_newest.stderr

for procedure in [
    ROOT / ".agents/skills/drive-board/references/orchestrator-room.md",
    ROOT / ".agents/skills/work-board/references/orchestrator-room.md",
    ROOT / ".claude/skills/drive-board/references/orchestrator-room.md",
    ROOT / ".claude/skills/work-board/references/orchestrator-room.md",
    ROOT / "docs/coordination/orchestrator-room.md",
]:
    text = procedure.read_text(encoding="utf-8")
    assert "scripts/check-orchestrator-room --json" in text, procedure
    assert "must equal the surrounding `fsgg:msg from=` actor" in text, procedure

print("PASS  permanent orchestrator room authenticates actors and fails closed")

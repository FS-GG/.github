#!/usr/bin/env python3
"""Stage-A black-box differential gate for the four frozen skill helpers (#3208)."""

from __future__ import annotations

import importlib.util
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.dont_write_bytecode = True
ENGINE = ROOT / "src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine.dll"
WORK = ROOT / ".agents/skills/work-roadmap/scripts"
PNEXT = ROOT / ".agents/skills/pnext-item/scripts"


def run(args: list[str], *, check: bool = False) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(args, cwd=ROOT, text=True, capture_output=True)
    if check and result.returncode:
        raise SystemExit(f"failed ({result.returncode}): {' '.join(args)}\n{result.stderr}")
    return result


def engine(*args: str) -> subprocess.CompletedProcess[str]:
    return run(["dotnet", str(ENGINE), *args])


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def collector_parity(directory: Path) -> int:
    counts = {"input_tokens": 10, "cached_input_tokens": 4, "cache_write_input_tokens": 0,
              "output_tokens": 5, "reasoning_output_tokens": 2, "total_tokens": 15}
    rows = [
        {"timestamp": "2026-01-01T00:00:00Z", "type": "session_meta", "payload": {"cli_version": "1.2.3"}},
        {"timestamp": "2026-01-01T00:00:00Z", "type": "turn_context", "payload": {"turn_id": "turn-1", "model": "gpt-test-sol", "effort": "high"}},
        {"timestamp": "2026-01-01T00:01:00Z", "type": "token_usage_record", "payload": {"thread_id": "thread-1", "turn_id": "turn-1", "session_id": "session-1", "response_id": "response-1", "usage": counts, "turn_token_usage": counts, "thread_token_usage": counts}},
    ]
    session = directory / "session.jsonl"
    session.write_text("\n".join(json.dumps(row, separators=(",", ":")) for row in rows) + "\n", encoding="utf-8")
    common = ["codex", "--session-file", str(session), "--task", "repo#1/claim", "--coord-version", "4.5.6", "--sdd-version", "7.8.9", "--contracts-version", "10.0.0"]
    for output_format in ("csv", "json"):
        python = run(["python3", str(PNEXT / "collect-runtime-usage.py"), *common, "--format", output_format], check=True)
        compiled = engine("telemetry", "usage", "collect", *common, "--format", output_format)
        if compiled.returncode or compiled.stdout != python.stdout:
            raise SystemExit(f"collector {output_format} parity failed\npython={python.stdout!r}\ncompiled={compiled.stdout!r}\n{compiled.stderr}")
    return 2


def lifecycle_parity(directory: Path) -> int:
    oracle = load("lifecycle_oracle", PNEXT / "validate-lifecycle-log.py")
    first = oracle.valid_fixture()[0]
    body = "<!-- fsgg:item-lifecycle/v1 -->\n```json\n" + json.dumps(first, sort_keys=True, separators=(",", ":")) + "\n```\n"
    comments = directory / "comments.json"
    comments.write_text(json.dumps([[{"id": 1, "created_at": "2026-09-04T08:00:00Z", "updated_at": "2026-09-04T08:00:00Z", "body": body}]]), encoding="utf-8")
    python = run(["python3", str(PNEXT / "validate-lifecycle-log.py"), "--root", "/", "--run", "roadmap-v2", "--unit", "GS2-01.1", "--export-comments", str(comments)], check=True)
    compiled = engine("telemetry", "lifecycle", "export-comments", "--run", "roadmap-v2", "--unit", "GS2-01.1", "--comments", str(comments))
    if compiled.returncode or compiled.stdout != python.stdout:
        raise SystemExit(f"lifecycle export parity failed\n{compiled.stderr}")
    return 1


def critique_parity() -> int:
    count = 0
    for artifact in sorted((ROOT / "reviews/roadmap").glob("*.json")):
        data = json.loads(artifact.read_text(encoding="utf-8"))
        cycle, head = data.get("cycle_id"), data.get("confirmation", {}).get("reviewed_commit")
        if not isinstance(cycle, str) or not isinstance(head, str):
            continue
        python = run(["python3", str(WORK / "validate-critique-state.py"), "--root", str(ROOT), "--cycle", cycle, "--artifact", str(artifact.relative_to(ROOT))])
        compiled = engine("telemetry", "critique", "validate", "--cycle", cycle, "--head", head, "--artifact", str(artifact))
        if (python.returncode == 0) != (compiled.returncode == 0):
            raise SystemExit(f"critique parity failed: {artifact}\npython={python.stderr}\ncompiled={compiled.stderr}")
        count += 1
    return count


def feedback_parity() -> int:
    count = 0
    for report in sorted((ROOT / "feedback").glob("*.md")):
        text = report.read_text(encoding="utf-8")
        cycle = re.search(r"(?m)^cycle:\s*(\S+)\s*$", text)
        phases = re.search(r"(?m)^- \*\*phases:\*\*\s*(.+?)\s*$", text)
        audit = ROOT / "feedback/audits" / (report.stem + ".audit.json")
        if not cycle or not phases or not audit.is_file():
            continue
        phase_value = phases.group(1)
        python = run(["python3", str(WORK / "validate-feedback-state.py"), "--root", str(ROOT), "--cycle", cycle.group(1), "--report", str(report.relative_to(ROOT)), "--audit", str(audit.relative_to(ROOT)), "--phases", phase_value])
        compiled = engine("telemetry", "feedback", "validate", "--cycle", cycle.group(1), "--report", str(report.relative_to(ROOT)), "--audit", str(audit.relative_to(ROOT)), "--phases", phase_value)
        if (python.returncode == 0) != (compiled.returncode == 0):
            raise SystemExit(f"feedback parity failed: {report}\npython={python.stderr}\ncompiled={compiled.stderr}")
        count += 1
    return count


def main() -> int:
    run(["dotnet", "build", str(ROOT / "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"), "-c", "Release", "--no-restore"], check=True)
    run(["python3", str(PNEXT / "collect-runtime-usage.py"), "--self-test"], check=True)
    run(["python3", str(PNEXT / "validate-lifecycle-log.py"), "--self-test"], check=True)
    with tempfile.TemporaryDirectory(prefix="fsgg-3208-parity-") as path:
        total = collector_parity(Path(path)) + lifecycle_parity(Path(path))
    total += critique_parity() + feedback_parity()
    print(f"telemetry-parity: pass ({total} positive differential cases; frozen oracle mutation self-tests pass)")
    return 0


if __name__ == "__main__":
    sys.exit(main())

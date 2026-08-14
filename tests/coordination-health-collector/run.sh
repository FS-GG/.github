#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
python3 - "$ROOT" <<'PY'
from datetime import datetime, timezone
import importlib.util
from pathlib import Path
import subprocess
import sys
import tempfile

root = Path(sys.argv[1])
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module

collector = load("collector", root / "scripts/coordination-health-collector.py")
readiness = load("readiness", root / "scripts/coordination-retirement-readiness.py")
try:
    collector.exact_windows(datetime(2026, 9, 6, 23, 59, 59, tzinfo=timezone.utc))
    raise AssertionError("future week accepted")
except ValueError as error:
    assert "2026-09-07T00:00:00Z" in str(error)
windows = collector.exact_windows(datetime(2026, 9, 7, tzinfo=timezone.utc))
assert [(collector.iso(a), collector.iso(b)) for a, b in windows] == [
    ("2026-08-17T00:00:00Z", "2026-08-24T00:00:00Z"),
    ("2026-08-24T00:00:00Z", "2026-08-31T00:00:00Z"),
    ("2026-08-31T00:00:00Z", "2026-09-07T00:00:00Z")]
assert collector.SUCCESSOR_QUERIES == readiness.CANONICAL_SUCCESSOR_QUERIES

with tempfile.TemporaryDirectory() as work:
    repo = Path(work)
    def git(*args):
        return subprocess.check_output(["git", *args], cwd=repo, text=True).strip()
    subprocess.run(["git", "init", "-q"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.email", "fixture@example.invalid"], cwd=repo, check=True)
    subprocess.run(["git", "config", "user.name", "fixture"], cwd=repo, check=True)
    (repo / "docs").mkdir(); (repo / "docs/a.md").write_text("before\n")
    subprocess.run(["git", "add", "."], cwd=repo, check=True); subprocess.run(["git", "commit", "-qm", "base"], cwd=repo, check=True)
    start = git("rev-parse", "HEAD")
    (repo / "docs/a.md").write_text("after\n")
    subprocess.run(["git", "commit", "-qam", "docs: repair statement"], cwd=repo, check=True)
    (repo / "scripts").mkdir(); (repo / "scripts/gate.py").write_text("print('ok')\n")
    subprocess.run(["git", "add", "."], cwd=repo, check=True); subprocess.run(["git", "commit", "-qm", "fix: repair behavior"], cwd=repo, check=True)
    end = git("rev-parse", "HEAD")
    repairs, statements, rows = collector.commit_measure(repo, start, end)
    assert (repairs, statements) == (2, 1)
    assert [row["statement_only"] for row in rows] == [True, False]

parser_text = (root / "scripts/coordination-health-collector.py").read_text()
assert 'parser.add_argument("--period' not in parser_text
assert 'parser.add_argument("--verdict' not in parser_text
assert 'source_sha != remote_sha' in parser_text
assert readiness.ACCEPTANCE_ENABLED is False
print("coordination health collector: 8 passed, 0 failed")
PY

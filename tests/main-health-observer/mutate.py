#!/usr/bin/env python3
"""Mutation controls for the dispatcher fallback and the current-run exclusion of the freshness check (Refs #4730, #5055).

Each mutant is one deliberate defect. The fixture (run.sh's unittest discovery) is re-run against a COPY of the
files the tests read (scripts/, .github/workflows/, tests/main-health-observer/); a mutant that leaves every
test green means no test pins that behaviour, and this script exits 1 naming it. The unmutated copy must pass
first. The live tree is never edited. Run: `python3 -I tests/main-health-observer/mutate.py` (about a minute;
not part of run.sh, so CI does not pay for it on every edit). It prints the counts quoted in the PR.
"""
from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
S, WF = "scripts/main-health-observer.py", ".github/workflows/main-health-observer.yml"

# (name, file, exact original text, replacement). Every original must occur exactly once.
MUTANTS = [
    ("dispatch actor unchecked", S, '                    and ((r.get("actor") or {}).get("login") == dispatcher_login)\n', ""),
    ("dispatch triggering actor unchecked", S, '                    and ((r.get("triggering_actor") or {}).get("login") == dispatcher_login)]', "]"),
    ("dispatch event unchecked", S, 'and r.get("event") == "workflow_dispatch" and r.get("head_branch")', 'and r.get("head_branch")'),
    ("dispatch branch unchecked on the row", S, ' and r.get("head_branch") == "main"', ""),
    ("dispatch read not filtered to main", S, "runs?branch=main&event=workflow_dispatch&per_page={DISPATCH_READ}", "runs?event=workflow_dispatch&per_page={DISPATCH_READ}"),
    ("fallback applies to every scheduled workflow", S, "if dispatcher_login and name in dispatcher_workflows:", "if dispatcher_login:"),
    ("oldest dispatcher run judged", S, 'dispatched = max(mine, key=lambda r: r["created_at"]) if mine else None',
     'dispatched = min(mine, key=lambda r: r["created_at"]) if mine else None'),
    ("current run kept in dispatch list", S, 'if isinstance(r, dict) and str(r.get("id")) != str(run_id)\n                    and r.get("event")',
     'if isinstance(r, dict)\n                    and r.get("event")'),
    ("current run kept in schedule list", S, 'listed = [r for r in listed if isinstance(r, dict) and str(r.get("id")) != str(run_id)]',
     'listed = [r for r in listed if isinstance(r, dict)]'),
    ("schedule page of one", S, "SCHEDULE_READ = 3", "SCHEDULE_READ = 1"),
    ("dispatch ignored by freshness", S, "if dispatched is not None:\n        got,", "if False:\n        got,"),
    ("older run judged", S, "if newest is None or theirs is None or got > theirs:", "if newest is None or theirs is None or got < theirs:"),
    ("dispatch fulfils only when no schedule run", S, "if newest is None or theirs is None or got > theirs:", "if newest is None or theirs is None:"),
    ("dispatcher label lost", S, 'newest, kind = dispatched, "dispatcher"', 'newest, kind = dispatched, "scheduled"'),
    ("dispatch read failure swallowed", S, '                raise RuntimeError("unexpected dispatch run-list shape")', "                rows = []"),
    ("dispatcher result dropped", S, "now, got[2])", "now, None)"),
    ("login without workflows accepted", S, "if bool(args.dispatcher_login) != bool(args.dispatcher_workflow):", "if False:"),
    ("workflow without a cron accepted", S, "        if unknown:\n            raise NoVerdict(", "        if False:\n            raise NoVerdict("),
    ("flag not passed to collect", S, "now, args.dispatcher_login, tuple(args.dispatcher_workflow))", "now)"),
    ("login dropped from the workflow", WF, "--dispatcher-login EHotwagner \\\n", ""),
    ("workflow allowlist dropped from the workflow", WF, " \\\n            --dispatcher-workflow board-drift-observer.yml --out", " --out"),
]


def run_fixture(tree: Path) -> bool:
    env = dict(os.environ, TMPDIR=os.environ.get("TMPDIR", tempfile.gettempdir()))
    proc = subprocess.run([sys.executable, "-I", "-m", "unittest", "discover", "-s", "tests/main-health-observer", "-p", "test_*.py"],
                          cwd=tree, capture_output=True, text=True, env=env)
    return proc.returncode == 0


def main() -> int:
    survivors = []
    with tempfile.TemporaryDirectory() as tmp:
        tree = Path(tmp)
        for sub in ("scripts", ".github/workflows", "tests/main-health-observer"):
            shutil.copytree(ROOT / sub, tree / sub, ignore=shutil.ignore_patterns("__pycache__"))
        if not run_fixture(tree):
            print("the unmutated copy does not pass; fix the fixture first", file=sys.stderr)
            return 2
        for name, rel, old, new in MUTANTS:
            path = tree / rel
            src = path.read_text(encoding="utf-8")
            if src.count(old) != 1:
                print(f"NOAPPLY  {name}: {src.count(old)} matches")
                survivors.append(name)
                continue
            path.write_text(src.replace(old, new), encoding="utf-8")
            try:
                killed = not run_fixture(tree)
            finally:
                path.write_text(src, encoding="utf-8")
            print(("killed   " if killed else "SURVIVED ") + name)
            if not killed:
                survivors.append(name)
    print(f"mutation controls: {len(MUTANTS) - len(survivors)}/{len(MUTANTS)} killed")
    return 1 if survivors else 0


if __name__ == "__main__":
    sys.exit(main())

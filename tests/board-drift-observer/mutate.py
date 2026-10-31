#!/usr/bin/env python3
"""Mutation controls for rule s and the schedule fetch (Refs #4730, #5054).

Each mutant is one deliberate defect. The fixture (run.sh's unittest discovery) is re-run against a COPY of the
files the tests read (scripts/, .github/workflows/, tests/board-drift-observer/); a mutant that leaves every
test green means no test pins that behaviour, and this script exits 1 naming it. The unmutated copy must pass
first. The live tree is never edited. Run: `python3 -I tests/board-drift-observer/mutate.py` (about a minute;
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
OBS, FETCH, WF = "scripts/board-drift-observer.py", "scripts/board-drift-schedule-fetch.py", ".github/workflows/board-drift-observer.yml"

# (name, file, exact original text, replacement). Every original must occur exactly once.
MUTANTS = [
    ("peer limit 2d->3d", OBS, '(dt.timedelta(days=2), "peer")', '(dt.timedelta(days=3), "peer")'),
    ("self limit 2h->3h", OBS, '(dt.timedelta(hours=2), "self")', '(dt.timedelta(hours=3), "self")'),
    ("strict > to >=", OBS, "        if now - newest > limit:", "        if now - newest >= limit:"),
    ("never-ran dropped", OBS, "            if now - created > limit:", "            if False:"),
    ("dispatch ignored in judgement", OBS, 'times = [parse_time((entry or {}).get(k)) for k in ("newestScheduleRunAt", "newestDispatchRunAt")]',
     'times = [parse_time((entry or {}).get("newestScheduleRunAt"))]'),
    ("oldest instead of newest", OBS, "newest = max((t for t in times if t is not None), default=None)",
     "newest = min((t for t in times if t is not None), default=None)"),
    ("bad dispatch timestamp collapsed", OBS, "or any(g and t is None for g, t in zip(given, times))", "or (given[0] and times[0] is None)"),
    ("disabled dropped", OBS, 'if entry.get("workflowState") != "active":', "if False:"),
    ("absent dropped", OBS, 'if state == "absent":', "if False:"),
    ("unreadable collapsed to ok", OBS, 'if state != "ok" or created is None or', 'if created is None or'),
    ("merge into analyze dropped", OBS, "        out += schedule_findings(schedule, now)", "        pass"),
    ("current-run exclusion dropped (schedule)", FETCH, 'str(r.get("id")) != str(current_run)\n              and r.get("event") == "schedule"',
     'r.get("event") == "schedule"'),
    ("current-run exclusion dropped (dispatch)", FETCH, 'and str(r.get("id")) != str(current_run)\n                  and r.get("event") == "workflow_dispatch"',
     'and r.get("event") == "workflow_dispatch"'),
    ("schedule event filter dropped", FETCH, 'and r.get("event") == "schedule" ', ""),
    ("dispatch event filter dropped", FETCH, 'and r.get("event") == "workflow_dispatch" and r.get("head_branch")', 'and r.get("head_branch")'),
    ("dispatch branch not checked on the row", FETCH, ' and r.get("head_branch") == DEFAULT_BRANCH', ""),
    ("dispatch read not filtered to main", FETCH, "event=workflow_dispatch&branch={DEFAULT_BRANCH}&per_page", "event=workflow_dispatch&per_page"),
    ("dispatch actor not checked", FETCH, 'and login(r.get("actor")) == dispatcher ', ""),
    ("dispatch triggering actor not checked", FETCH, ' and login(r.get("triggering_actor")) == dispatcher]', "]"),
    ("dispatch read failure ignored", FETCH,
     '    if status != "ok" or not isinstance(runs, dict) or not isinstance(runs.get("workflow_runs"), list):\n        return entry\n    # No qualifying',
     "    if False:\n        return entry\n    # No qualifying"),
    ("dispatch never recorded", FETCH, 'entry["newestDispatchRunAt"], entry["newestDispatchRunId"] = newest["created_at"], newest["id"]', "pass"),
    ("404 treated as unreadable", FETCH, '"absent", None) if "HTTP 404" in', '"unreadable", None) if "HTTP 404" in'),
    ("partial page accepted", FETCH, "if not isinstance(total, int) or (not others and total > len(listed)):", "if False:"),
    ("fetch clock ignored", FETCH, '"fetchedAt": now_iso(now)', '"fetchedAt": now_iso()'),
    ("bad --now accepted", FETCH, "if now is not None and now.tzinfo is None:", "if False:"),
    ("ack text for every rule", OBS, 'elif f["rule"] == "s":', "elif True:"),
    ("rule s missing from body sections", OBS, 'for rule in ("a", "b", "c", "d", "e", "s", "x") + CURRENCY_RULES:',
     'for rule in ("a", "b", "c", "d", "e", "x") + CURRENCY_RULES:'),
    ("rule s foot text dropped", OBS, '"firing) has no ack either', '"firing) has an ack either'),
    ("rule s not commented", OBS, "notable = [k for k in added + removed if key_rule(k) not in CURRENCY_RULES]",
     "notable = [k for k in added + removed if key_rule(k) not in CURRENCY_RULES and key_rule(k) != 's']"),
    ("check ignores schedule", OBS, "                           schedule=schedule)", "                           schedule=None)"),
    ("workflow drops --schedule", WF, '            --schedule "$SCRATCH/schedule.json" \\\n', ""),
    ("workflow drops --dispatcher-login", WF, "            --dispatcher-login EHotwagner \\\n", ""),
    ("workflow drops actions: read", WF, "      contents: read\n      actions: read\n", "      contents: read\n"),
    ("schedule read uses the App token", WF,
     "          GH_TOKEN: ${{ github.token }}\n          SCRATCH: ${{ steps.scratch.outputs.dir }}\n        run: |\n          set -euo pipefail\n          python3 scripts/board-drift-schedule-fetch.py",
     "          GH_TOKEN: ${{ steps.app-token.outputs.token }}\n          SCRATCH: ${{ steps.scratch.outputs.dir }}\n        run: |\n          set -euo pipefail\n          python3 scripts/board-drift-schedule-fetch.py"),
    ("watch list drops the peer", WF, "--workflow main-health-observer.yml --workflow board-drift-observer.yml",
     "--workflow board-drift-observer.yml"),
]


def run_fixture(tree: Path) -> bool:
    env = dict(os.environ, TMPDIR=os.environ.get("TMPDIR", tempfile.gettempdir()))
    proc = subprocess.run([sys.executable, "-I", "-m", "unittest", "discover", "-s", "tests/board-drift-observer", "-p", "test_*.py"],
                          cwd=tree, capture_output=True, text=True, env=env)
    return proc.returncode == 0


def main() -> int:
    survivors = []
    with tempfile.TemporaryDirectory() as tmp:
        tree = Path(tmp)
        for sub in ("scripts", ".github/workflows", "tests/board-drift-observer"):
            shutil.copytree(ROOT / sub, tree / sub, ignore=shutil.ignore_patterns("__pycache__"))
        for extra in ("docs/coordination",):  # read by prose assertions, if any
            if (ROOT / extra).is_dir():
                shutil.copytree(ROOT / extra, tree / extra)
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

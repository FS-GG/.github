#!/usr/bin/env python3
"""Read the newest `schedule` run of each watched workflow into one JSON document (Refs #4730).

WHY: the board-drift observer and the main-health observer each run on a cron, and a cron that GitHub
stops firing leaves no red run and no issue. The two watch each other: the main-health observer judges
board-drift's schedule (its freshness check), and this read lets board-drift judge the nightly
main-health observer and its own previous scheduled run. The judgement is a pure function in
board-drift-observer.py (rule s); this file is the only IO.

READ-ONLY and BOUNDED: three REST reads per workflow (the workflow object, its five newest runs with
`event=schedule`, its ten newest runs with `event=workflow_dispatch`), made with the workflow's GITHUB_TOKEN under `actions: read`. The Project 3 App token is
not used and not widened.

A read that fails is RECORDED, not fatal: the entry carries state `unreadable` (or `absent` for HTTP 404),
and the observer turns that into a finding. "I could not look" is never "no drift" (.github#266), and a
dead Actions API must not also silence the board report, so a per-workflow failure does not fail the step.
Only a misconfigured call (no repository, no workflow) exits non-zero.

A DISPATCH BY THE DISPATCHER FULFILS THE SCHEDULE (Root decision on #4990). GitHub drops cron runs; the hourly
fdev dispatcher fires the observer with `workflow_dispatch` as the designed fallback. A dispatch run qualifies
only when its event is `workflow_dispatch`, it ran on `main` (a feature-branch dry run must not mask a dead
schedule: the read is filtered with `branch=main` and each row's `head_branch` is checked), and both its `actor` and `triggering_actor` are `--dispatcher-login`
(the token owner). A dispatch by anyone else, or a re-run (a different triggering actor), does not. The fetch
records the newest qualifying run; whether it is recent enough is the judgement's, against the same limit.

THE CURRENT RUN IS EXCLUDED from "newest scheduled run" (GITHUB_RUN_ID), for both kinds of run. Otherwise a scheduled board-drift
run would always find itself and the self-check could never fire: what is judged is the run BEFORE this one.

Failure text is fixed (a label and an exit code), never a response body.

Environment: GH_TOKEN (actions: read), GITHUB_REPOSITORY, GITHUB_RUN_ID (optional), BOARD_DRIFT_GH (tests).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys
from pathlib import Path

SCHEDULE_SCHEMA = "fsgg:board-drift-schedule/v1"
RUNS_PER_PAGE = 5
DISPATCH_RUNS_PER_PAGE = 10
DEFAULT_BRANCH = "main"


def now_iso(now: dt.datetime | None = None) -> str:
    """The clock is injectable (`--now`): only `fetchedAt` is recorded from it; the judgement in
    board-drift-observer.py takes its own `now`, so no test depends on the wall clock."""
    return (now or dt.datetime.now(dt.timezone.utc)).strftime("%Y-%m-%dT%H:%M:%SZ")


def gh_json(path: str) -> tuple[str, object]:
    """('ok', parsed) | ('absent', None) for HTTP 404 | ('unreadable', None). Never raises, never echoes."""
    cmd = [os.environ.get("BOARD_DRIFT_GH", "gh"), "api", path]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120, check=False)
    except (subprocess.TimeoutExpired, OSError):
        return "unreadable", None
    if proc.returncode:
        return ("absent", None) if "HTTP 404" in (proc.stdout + proc.stderr) else ("unreadable", None)
    try:
        return "ok", json.loads(proc.stdout)
    except json.JSONDecodeError:
        return "unreadable", None


def login(user: object) -> str | None:
    return user.get("login") if isinstance(user, dict) and isinstance(user.get("login"), str) else None


def read_workflow(repo: str, file: str, current_run: str | None, dispatcher: str) -> dict:
    entry: dict = {"file": file, "state": "unreadable", "workflowState": None, "createdAt": None,
                   "newestScheduleRunAt": None, "newestScheduleRunId": None,
                   "newestDispatchRunAt": None, "newestDispatchRunId": None}
    status, workflow = gh_json(f"repos/{repo}/actions/workflows/{file}")
    if status != "ok":
        entry["state"] = status
        return entry
    if not isinstance(workflow, dict) or not workflow.get("state") or not workflow.get("created_at"):
        return entry
    entry["workflowState"], entry["createdAt"] = workflow["state"], workflow["created_at"]

    status, runs = gh_json(f"repos/{repo}/actions/workflows/{file}/runs?event=schedule&per_page={RUNS_PER_PAGE}")
    if status != "ok" or not isinstance(runs, dict) or not isinstance(runs.get("workflow_runs"), list):
        return entry
    listed = runs["workflow_runs"]
    others = [r for r in listed if isinstance(r, dict) and str(r.get("id")) != str(current_run)
              and r.get("event") == "schedule" and r.get("created_at")]
    total = runs.get("total_count")
    if not isinstance(total, int) or (not others and total > len(listed)):
        # A count that disagrees with the page, or a page of nothing usable behind a larger count: a
        # partial read proves nothing.
        return entry
    if others:
        newest = max(others, key=lambda r: r["created_at"])
        entry["newestScheduleRunAt"], entry["newestScheduleRunId"] = newest["created_at"], newest["id"]

    status, runs = gh_json(f"repos/{repo}/actions/workflows/{file}/runs?event=workflow_dispatch&branch={DEFAULT_BRANCH}&per_page={DISPATCH_RUNS_PER_PAGE}")
    if status != "ok" or not isinstance(runs, dict) or not isinstance(runs.get("workflow_runs"), list):
        return entry
    # No qualifying row in the page is not an error: the dispatcher is only a second way to fulfil the
    # schedule. A failed or malformed read IS (above): the judgement must not call a schedule stale when
    # it could not look at the fallback.
    dispatched = [r for r in runs["workflow_runs"] if isinstance(r, dict) and str(r.get("id")) != str(current_run)
                  and r.get("event") == "workflow_dispatch" and r.get("head_branch") == DEFAULT_BRANCH and r.get("created_at")
                  and login(r.get("actor")) == dispatcher and login(r.get("triggering_actor")) == dispatcher]
    if dispatched:
        newest = max(dispatched, key=lambda r: r["created_at"])
        entry["newestDispatchRunAt"], entry["newestDispatchRunId"] = newest["created_at"], newest["id"]
    entry["state"] = "ok"
    return entry


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--workflow", action="append", required=True, help="workflow file name; repeatable")
    ap.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY"))
    ap.add_argument("--dispatcher-login", required=True,
                    help="login of the hourly dispatcher's token owner; its workflow_dispatch runs fulfil the schedule")
    ap.add_argument("--out", required=True)
    ap.add_argument("--now", help="ISO timestamp recorded as fetchedAt (default: the wall clock; tests inject)")
    args = ap.parse_args(argv)
    try:
        now = dt.datetime.fromisoformat(args.now.replace("Z", "+00:00")) if args.now else None
        if now is not None and now.tzinfo is None:
            raise ValueError("no timezone")
    except ValueError:
        print("board-drift-schedule-fetch: --now is not an ISO timestamp with a timezone", file=sys.stderr)
        return 1
    if not args.repo or "/" not in args.repo:
        print("board-drift-schedule-fetch: no repository (--repo or GITHUB_REPOSITORY)", file=sys.stderr)
        return 1
    current = os.environ.get("GITHUB_RUN_ID") or None
    entries = [read_workflow(args.repo, f, current, args.dispatcher_login) for f in args.workflow]
    doc = {"schema": SCHEDULE_SCHEMA, "fetchedAt": now_iso(now), "currentRunId": current, "workflows": entries}
    Path(args.out).write_text(json.dumps(doc, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    for e in entries:
        print(f"board-drift-schedule-fetch: {e['file']} {e['state']} newest={e['newestScheduleRunAt']} dispatched={e['newestDispatchRunAt']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

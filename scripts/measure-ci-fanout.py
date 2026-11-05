#!/usr/bin/env python3
"""Summarize saved CI observations; never launch, cancel or query a workload.

Inputs are saved Actions run pages (or arrays), and an optional run-id -> jobs
map. A head population is not a push: body edits and reruns can share its SHA.
Job timestamps, rather than run_started_at, measure the observed queue wait.
"""

from __future__ import annotations

import argparse
from collections import defaultdict
from datetime import datetime
import importlib.util
import json
import math
from pathlib import Path
import statistics
from typing import Any


def timestamp(value: str | None) -> datetime | None:
    if value is None:
        return None
    parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None:
        raise ValueError("timestamps must include a timezone")
    return parsed


def distribution(values: list[float]) -> dict[str, Any]:
    ordered = sorted(values)
    return {
        "observations": len(ordered),
        "p50": statistics.median(ordered) if ordered else None,
        "p90": ordered[math.ceil(len(ordered) * .9) - 1] if ordered else None,
        "maximum": ordered[-1] if ordered else None,
    }


def load_runs(paths: list[Path]) -> tuple[list[dict], list[dict]]:
    runs: dict[int, dict] = {}
    pages = []
    for path in paths:
        data = json.loads(path.read_text())
        rows = data if isinstance(data, list) else data["workflow_runs"]
        if not isinstance(rows, list):
            raise ValueError(f"{path}: runs must be an array")
        pages.append({"file": str(path), "observed": len(rows),
                      "queryTotal": data.get("total_count") if isinstance(data, dict) else None})
        for row in rows:
            rid = row["id"]
            if not isinstance(rid, int) or isinstance(rid, bool):
                raise ValueError(f"{path}: noninteger run identity")
            prior = runs.get(rid)
            if prior:
                for key in ("head_sha", "event", "path"):
                    if prior.get(key) != row.get(key):
                        raise ValueError(f"run {rid}: conflicting {key}")
                # Observations may overlap or refresh status. Never sum duplicates.
                before = timestamp(prior.get("updated_at"))
                after = timestamp(row.get("updated_at"))
                if before and (not after or before > after):
                    continue
            runs[rid] = row
    return list(runs.values()), pages


def workflow_filters(root: Path) -> dict[str, dict]:
    # Reuse the existing gate's validated GitHub glob semantics. fnmatch('*')
    # crosses directories and would silently overstate filter coverage.
    import yaml

    spec = importlib.util.spec_from_file_location("paths_coherence", root / "scripts/check-paths-coherence.py")
    assert spec and spec.loader
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    result = {}
    for path in sorted((root / ".github/workflows").glob("*.yml")):
        data = yaml.safe_load(path.read_text())
        events = data.get("on", data.get(True, {}))
        if not isinstance(events, dict):
            continue
        for event in ("pull_request", "pull_request_target"):
            if event not in events:
                continue
            trigger = events[event] or {}
            patterns = trigger.get("paths")
            # The helper's grammar is literal characters, * and **. Unsupported
            # syntax stays Unknown rather than looking like a proven exclusion.
            unsupported = "paths-ignore" in trigger or (patterns is not None and (
                not isinstance(patterns, list) or not patterns or any(
                    not isinstance(p, str) or any(c in p for c in "!?+[]{}") for p in patterns)))
            result[(path.relative_to(root).as_posix(), event)] = {
                "patterns": patterns, "unsupported": unsupported, "selects": module.selects,
            }
    return result


def summarize(runs: list[dict], jobs: dict, start: str, end: str,
              filters: dict | None = None, head_paths: dict | None = None) -> dict:
    first, last = timestamp(start), timestamp(end)
    if not first or not last or first >= last:
        raise ValueError("window must be nonempty and increasing")
    selected = []
    for run in runs:
        created = timestamp(run["created_at"])
        if not created:
            raise ValueError("run is missing created_at")
        if first <= created < last and run["event"] in ("pull_request", "pull_request_target"):
            selected.append(run)
    per_workflow: dict[str, dict] = {}
    per_head: dict[tuple, list] = defaultdict(list)
    all_queues, all_work = [], []
    for run in selected:
        key = run["path"]
        row = per_workflow.setdefault(key, {
            "runs": 0, "headPopulations": set(), "observedJobRuns": 0,
            "observedJobs": 0, "runnerJobsWithCompleteTiming": 0,
            "jobsWithoutCompleteTiming": 0, "invalidTimingJobs": 0,
            "queueSeconds": [], "workSeconds": [], "currentFilterReplay": defaultdict(int),
        })
        row["runs"] += 1
        row["headPopulations"].add((run["event"], run["head_sha"]))
        per_head[(run["event"], run["head_sha"])].append(run)
        replay = "unknown"
        policy = (filters or {}).get((key, run["event"]))
        files = (head_paths or {}).get(run["head_sha"])
        if policy and not policy["unsupported"]:
            if policy["patterns"] is None:
                replay = "unfiltered"
            elif files and files.get("complete") is True:
                paths = files.get("paths")
                if not isinstance(paths, list) or any(not isinstance(p, str) for p in paths):
                    raise ValueError("complete head file observations need a string paths array")
                replay = "matched" if any(policy["selects"](p, policy["patterns"]) for p in paths) else "excluded"
        row["currentFilterReplay"][replay] += 1
        observed = jobs.get(str(run["id"]))
        if observed is None:
            continue
        if not isinstance(observed, list):
            raise ValueError("job observations must be an array or null")
        row["observedJobRuns"] += 1
        row["observedJobs"] += len(observed)
        for job in observed:
            if job.get("conclusion") == "skipped":
                continue
            if not all(job.get(k) for k in ("created_at", "started_at", "completed_at", "runner_name")):
                row["jobsWithoutCompleteTiming"] += 1
                continue
            queue = (timestamp(job["started_at"]) - timestamp(job["created_at"])).total_seconds()
            work = (timestamp(job["completed_at"]) - timestamp(job["started_at"])).total_seconds()
            if queue < 0 or work < 0:
                row["invalidTimingJobs"] += 1
                continue
            row["runnerJobsWithCompleteTiming"] += 1
            row["queueSeconds"].append(queue)
            row["workSeconds"].append(work)
            all_queues.append(queue)
            all_work.append(work)
    output_rows = []
    for workflow, row in sorted(per_workflow.items()):
        row["workflow"] = workflow
        row["headPopulations"] = len(row["headPopulations"])
        row["jobRunsWithoutObservations"] = row["runs"] - row["observedJobRuns"]
        row["queueSeconds"] = distribution(row["queueSeconds"])
        work = row.pop("workSeconds")
        row["observedRunnerMinutes"] = sum(work) / 60 if work else None
        row["runnerSeconds"] = distribution(work)
        row["currentFilterReplay"] = dict(row["currentFilterReplay"])
        output_rows.append(row)
    events = {}
    for event in ("pull_request", "pull_request_target"):
        populations = [rows for (kind, _), rows in per_head.items() if kind == event]
        events[event] = {
            "runs": sum(len(rows) for rows in populations), "headPopulations": len(populations),
            "runsPerHeadPopulation": distribution([len(rows) for rows in populations]),
        }
    return {
        "schema": "fsgg.ci-fanout-measurement/1", "window": {"start": start, "endExclusive": end},
        "runs": len(selected), "headPopulations": len(per_head),
        "events": events,
        "runsPerHeadPopulation": distribution([len(v) for v in per_head.values()]),
        "observedJobRuns": sum(row["observedJobRuns"] for row in output_rows),
        "jobRunsWithoutObservations": sum(row["jobRunsWithoutObservations"] for row in output_rows),
        "observedJobs": sum(row["observedJobs"] for row in output_rows),
        "runnerJobsWithCompleteTiming": len(all_work), "queueSeconds": distribution(all_queues),
        "observedRunnerMinutes": sum(all_work) / 60 if all_work else None,
        "workflows": output_rows,
        "limitations": [
            "Input pages do not establish complete window coverage; retain the collector's pagination manifest.",
            "A head population groups event and SHA; body edits and reruns prevent calling it a push count.",
            "Jobs are saved observations, not proof of all attempts or complete per-run job pagination.",
            "Runner minutes are observed job wall time, not billed minutes or savings.",
            "Filter replay uses the supplied current tree, not the historical workflow at each event.",
            "Missing timing and changed-file observations are Unknown, never zero.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=Path, action="append", required=True)
    parser.add_argument("--jobs", type=Path)
    parser.add_argument("--start", required=True)
    parser.add_argument("--end", required=True)
    parser.add_argument("--root", type=Path, help="optional current workflow tree for filter replay; needs pinned PyYAML")
    parser.add_argument("--head-paths", type=Path, help="SHA -> {complete: true, paths: [...]} saved observations")
    args = parser.parse_args()
    runs, pages = load_runs(args.runs)
    jobs = json.loads(args.jobs.read_text()) if args.jobs else {}
    paths = json.loads(args.head_paths.read_text()) if args.head_paths else {}
    result = summarize(runs, jobs, args.start, args.end,
                       workflow_filters(args.root) if args.root else None, paths)
    result["inputPages"] = pages
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

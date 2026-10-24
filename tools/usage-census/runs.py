#!/usr/bin/env python3
"""List every workflow run created from the earliest member's creation day to the window end day.

Only repositories that have members are listed. The runs listing caps a filtered query at 1000 rows,
so a day at the cap is re-listed in hourly slices, which must each stay below the cap.
"""
import datetime

from census_common import cfg, dump, gh_json, load

C = cfg()
pop = load("prs.json")
repos = sorted({p["repo"] for p in pop["prs"]})
first = min(p["createdAt"] for p in pop["prs"])[:10]
last = C["hi"][:10]
if C.get("run_from"):
    first = C["run_from"]


def listing(repo, rng):
    runs, page = [], 1
    while True:
        d = gh_json("api", f"repos/{repo}/actions/runs?created={rng}&per_page=100&page={page}")
        runs += d["workflow_runs"]
        if len(d["workflow_runs"]) < 100:
            return d["total_count"], runs
        page += 1


allruns = {}
for repo in repos:
    day = datetime.date.fromisoformat(first)
    end = datetime.date.fromisoformat(last)
    while day <= end:
        total, found = listing(repo, f"{day}T00:00:00Z..{day}T23:59:59Z")
        if total >= 1000 or len(found) < total or len(found) >= 1000:
            found = []
            for hour in range(24):
                t2, r2 = listing(repo, f"{day}T{hour:02d}:00:00Z..{day}T{hour:02d}:59:59Z")
                assert len(r2) == t2 and t2 < 1000, (repo, day, hour, t2, len(r2))
                found += r2
        for x in found:
            allruns[x["id"]] = {k: x[k] for k in ("id", "name", "event", "status", "conclusion", "head_sha", "head_branch", "run_attempt", "created_at", "updated_at", "run_started_at", "path")} | {"repo": repo, "prs": [p["number"] for p in x.get("pull_requests") or []]}
        day += datetime.timedelta(days=1)
dump("runs.json", list(allruns.values()))
print(len(allruns))

#!/usr/bin/env python3
"""Jobs API (`filter=all`, all attempts) for every matched run. Resumable, sharded and rate-paced.

usage: jobs.py [--extra] [--floor N] [--pace SECONDS] [--shard K --of N]

Reads match.json (or match_extra.json with --extra) and appends to data/jobs.<main|extra>.<K>.jsonl.
A run already present in any file of the same family is skipped. Throttles while the remaining REST
quota is below --floor, because the quota is shared by every agent on the account.
"""
import argparse
import glob
import json
import os
import subprocess
import sys
import time

from census_common import DATA, load

ap = argparse.ArgumentParser()
ap.add_argument("--extra", action="store_true")
ap.add_argument("--floor", type=int, default=1500)
ap.add_argument("--pace", type=float, default=1.0)
ap.add_argument("--shard", type=int, default=0)
ap.add_argument("--of", type=int, default=1)
a = ap.parse_args()
family = "extra" if a.extra else "main"
m = load("match_extra.json" if a.extra else "match.json")
runs = {r["id"]: r for r in load("runs.json")}
ids = sorted({i for v in m.values() for i in v["pre"] + v["post"]})
out = os.path.join(DATA, f"jobs.{family}.{a.shard}.jsonl")
done = set()
for fn in glob.glob(os.path.join(DATA, f"jobs.{family}.*.jsonl")):
    with open(fn, encoding="utf-8") as handle:
        for line in handle:
            o = json.loads(line)
            if "error" not in o:
                done.add(o["run_id"])
todo = [i for i in ids if i not in done][a.shard::a.of]


def quota():
    d = json.loads(subprocess.check_output(["gh", "api", "rate_limit"]))["resources"]["core"]
    return d["remaining"], d["reset"]


def fetch(i):
    r = runs[i]
    jobs, page, tries = [], 1, 0
    while True:
        p = subprocess.run(["gh", "api", f"repos/{r['repo']}/actions/runs/{i}/jobs?filter=all&per_page=100&page={page}"], capture_output=True, text=True)
        if p.returncode:
            if tries < 3 and any(s in p.stderr.lower() for s in ("rate limit", "403", "429")):
                tries += 1
                _, reset = quota()
                time.sleep(max(60, reset - time.time() + 5))
                continue
            return {"run_id": i, "error": p.stderr.strip()[:300]}
        d = json.loads(p.stdout)
        jobs += [{k: j.get(k) for k in ("id", "run_attempt", "name", "status", "conclusion", "started_at", "completed_at", "labels", "runner_name", "runner_group_name")} for j in d["jobs"]]
        if len(jobs) >= d["total_count"] or not d["jobs"]:
            return {"run_id": i, "total": d["total_count"], "jobs": jobs}
        page += 1


print("todo", len(todo), flush=True)
with open(out, "a", encoding="utf-8") as handle:
    for k in range(0, len(todo), 100):
        rem, reset = quota()
        while rem < a.floor:
            print("throttle", rem, "until", reset, flush=True)
            time.sleep(max(30, reset - time.time() + 5))
            rem, reset = quota()
        for i in todo[k:k + 100]:
            t0 = time.time()
            handle.write(json.dumps(fetch(i)) + "\n")
            time.sleep(max(0, a.pace - (time.time() - t0)))
        handle.flush()
        print(min(k + 100, len(todo)), rem, flush=True)

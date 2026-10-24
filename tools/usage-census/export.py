#!/usr/bin/env python3
"""Compact public evidence (tabular, well under the 64 KiB telemetry-evidence cap): census.json -> evidence JSON.

usage: export.py OUTPUT.json
"""
import json
import sys

from census_common import load

from census_common import cfg
C = cfg()
c = load("census.json")
PRC = ["member", "head", "merge", "createdAt", "mergedAt", "route_marker", "review_class", "review", "required_checks", "pr_runs", "pr_jobs", "pr_job_seconds", "merge_push_runs", "merge_push_jobs", "merge_push_job_seconds", "elapsed_open_to_merge_seconds_including_overlapping_queues", "claude_tokens_attributed"]
IC = ["member", "closedAt", "stateReason", "projectStatus", "closer", "population_prs_declaring_close"]


def pr(r):
    t = r["native_tokens"]
    rev = r["review"].split(": ", 1)[1] if r["review_class"] == "V" else r["review"][len("unsampled ("):-1]
    return [r["member"].replace(C["org"] + "/", ""), r["head"], r["merge"], r["createdAt"], r["mergedAt"], r["route_marker"].replace("routine-development/v1 operation=", "rd/v1 ").replace(" head=final", ""), r["review_class"], rev, r["required_checks"], r["pr_runs"], r["pr_jobs"], r["pr_job_seconds"], r["merge_runs"], r["merge_jobs"], r["merge_job_seconds"], r["elapsed_open_to_merge_seconds_including_overlapping_queues"], (t["total"] if isinstance(t, dict) else None)]


def it(i):
    return [i["member"].replace(C["org"] + "/", ""), i["closedAt"], i["stateReason"], i["projectStatus"], i["closer"].replace(C["org"] + "/", ""), [x.replace(C["org"] + "/", "") for x in i["population_prs_declaring_close"]]]


out = {k: c[k] for k in ("schema", "issue", "window", "population", "actions")}
out["review_classes"] = {"V": "independent verdict bound to the exact final head", "A": "unsampled: independent verdict stated, head not named", "E": "unsampled: verdict recorded at an earlier head", "N": "unsampled: no independent verdict in the PR record"}
out["usage"] = c["usage"]
out["repository_prefix"] = C["org"] + "/"
out["prs"] = {"columns": PRC, "rows": [pr(r) for r in c["prs"]]}
out["project3"] = {"columns": IC, "rows": [it(i) for i in c["project3"]]}
text = json.dumps(out, separators=(",", ":"), ensure_ascii=False) + "\n"
if len(text.encode()) >= 65536:
    sys.exit("evidence JSON would reach the 64 KiB cap")
open(sys.argv[1], "w", encoding="utf-8").write(text)
print(len(text.encode()), "bytes")

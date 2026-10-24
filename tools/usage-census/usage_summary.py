#!/usr/bin/env python3
"""Deduplicate reader output by provider response ID and join Claude Code responses to members.

A resumed Claude Code session copies earlier records into its new transcript, so totals must be
deduplicated by provider response ID across files. A response joins a member only when the `gitBranch`
on its own record equals the member's unique head branch and its timestamp is no later than the merge.
Everything else is unattributed.
"""
import collections
import csv
import json

from census_common import data_path, dump, load

idx = [line.rstrip("\n").split("\t") for line in open(data_path("usage/index.tsv"), encoding="utf-8")]
jk, pop = load("joinkeys.json"), load("prs.json")
bybranch = {}
for p in pop["prs"]:
    bybranch.setdefault(p["headRefName"], []).append(p)
F = ["input", "cached_input", "cache_write_input", "output", "reasoning", "total"]
files, refusals = collections.Counter(), collections.Counter()
tot, attr = collections.defaultdict(collections.Counter), collections.defaultdict(collections.Counter)
models = collections.defaultdict(collections.Counter)
resp, attrresp, sessions = collections.Counter(), collections.Counter(), collections.defaultdict(set)
seen, dupes = set(), 0
for rt, fid, rc, path, err in idx:
    cls = "subagent" if "/subagents/" in path else "main"
    files[(rt, cls, "ok" if rc == "0" else "refused")] += 1
    if rc != "0":
        e = err.split("fsgg-coord-engine: telemetry usage: ")[1] if "usage: " in err else err
        e = "no completed usage record (totals withheld)" if "no completed usage record" in e else e
        refusals[(rt, cls, e.strip())] += 1
        continue
    for r in csv.DictReader(open(data_path(f"usage/{rt}/{fid}.csv"), encoding="utf-8")):
        key = (rt, r["response_id"])
        if key in seen:
            dupes += 1
            continue
        seen.add(key)
        sessions[rt].add(r["session_id"])
        resp[rt] += 1
        for f in F:
            tot[rt][f] += int(r[f] or 0)
            models[(rt, r["model"])][f] += int(r[f] or 0)
        models[(rt, r["model"])]["responses"] += 1
        target = None
        if rt == "claude":
            m = jk[fid]["msgs"].get(r["response_id"])
            if m and m["branch"] in bybranch and len(bybranch[m["branch"]]) == 1:
                p = bybranch[m["branch"]][0]
                if r["timestamp"] <= p["mergedAt"]:
                    target = f'{p["repo"]}#{p["number"]}'
        if target:
            attrresp[(rt, target)] += 1
            for f in F:
                attr[(rt, target)][f] += int(r[f] or 0)
res = {"files": [[*k, v] for k, v in sorted(files.items())], "refusals": [[*k, v] for k, v in sorted(refusals.items())],
       "dupes": dupes, "responses": dict(resp), "sessions": {k: len(v) for k, v in sessions.items()}, "totals": {k: dict(v) for k, v in tot.items()},
       "models": [[*k, dict(v)] for k, v in sorted(models.items())],
       "attributed": [[*k, attrresp[k], dict(v)] for k, v in sorted(attr.items())]}
dump("usage_summary.json", res, indent=1)
print(json.dumps({k: res[k] for k in ("dupes", "responses", "sessions", "totals")}, indent=1))

#!/usr/bin/env python3
"""Exclusive run-to-member assignment.

PR phase: among members whose commit set holds the run's head SHA, take (1) the native
pull_requests link, else (2) the member whose head branch equals the run's head branch, else (3) the
member that was open when the run was created; otherwise the run belongs to another PR and is
excluded. Only runs created at or before the chosen member's merge count. Merge commit: `push` runs
on the merge SHA only (issues, schedule, dispatch and dynamic runs on that SHA are not caused by the
merge).

Runs on a member's head branch inside its open interval whose SHA is no longer in the PR's commit
list (rebases, force-pushes) are written to match_extra.json. They are disclosed separately and are
not in the totals, as in the #4823 report.
"""
import collections

from census_common import dump, load

pop, det, runs = load("prs.json"), load("prdetail.json"), load("runs.json")
M = {f'{p["repo"]}#{p["number"]}': p for p in pop["prs"]}
shas = {k: {c["commit"]["oid"] for c in det[k]["commits"]["nodes"]} | {p["headRefOid"]} for k, p in M.items()}
bysha = collections.defaultdict(list)
for k, s in shas.items():
    for x in s:
        bysha[(M[k]["repo"], x)].append(k)
merge = {(p["repo"], p["mergeCommit"]["oid"]): k for k, p in M.items()}
byb = collections.defaultdict(list)
for k, p in M.items():
    byb[(p["repo"], p["headRefName"])].append(k)
m = {k: {"pre": [], "post": []} for k in M}
stats = collections.Counter()
for r in runs:
    key = (r["repo"], r["head_sha"])
    if key in merge:
        k = merge[key]
        if r["event"] == "push":
            m[k]["post"].append(r["id"])
        else:
            stats["merge_sha_nonpush_excluded"] += 1
            stats["nonpush:" + r["event"]] += 1
        continue
    linked = [f'{r["repo"]}#{n}' for n in r["prs"] if f'{r["repo"]}#{n}' in M]
    cands = bysha.get(key, [])
    if linked:
        k = ([x for x in linked if x in cands] or linked)[0]
        how = "link"
    elif cands:
        b = [x for x in cands if r["head_branch"] in (M[x]["headRefName"], "refs/pull/%d/head" % M[x]["number"])]
        o = [x for x in cands if M[x]["createdAt"] <= r["created_at"] <= M[x]["mergedAt"]]
        if b:
            k, how = sorted(b, key=lambda x: M[x]["createdAt"])[0], "branch"
        elif o:
            k, how = sorted(o, key=lambda x: M[x]["createdAt"])[0], "open"
        else:
            stats["other_branch_before_open_excluded"] += 1
            continue
    else:
        continue
    if r["created_at"] > M[k]["mergedAt"]:
        stats["after_merge_excluded"] += 1
        continue
    m[k]["pre"].append(r["id"])
    stats["pre_" + how] += 1
matched = {i for v in m.values() for i in v["pre"] + v["post"]}
extra = {}
for r in runs:
    if r["id"] in matched or r["event"] != "pull_request":
        continue
    cand = [k for k in byb.get((r["repo"], r["head_branch"]), []) if M[k]["createdAt"] <= r["created_at"] <= M[k]["mergedAt"]]
    if cand:
        extra.setdefault(sorted(cand, key=lambda k: M[k]["createdAt"])[0], {"pre": [], "post": []})["pre"].append(r["id"])
dump("match.json", m)
dump("match_extra.json", extra)
dump("match_stats.json", dict(stats), indent=1)
print(dict(stats))
print("matched runs:", sum(len(v["pre"]) for v in m.values()), sum(len(v["post"]) for v in m.values()))
print("force-pushed-head runs:", sum(len(v["pre"]) for v in extra.values()), "on", len(extra), "members")
print("members without PR-phase runs:", [k for k, v in m.items() if not v["pre"]])
print("runs not completed:", collections.Counter(r["status"] for r in runs if r["id"] in matched and r["status"] != "completed"))

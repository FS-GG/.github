#!/usr/bin/env python3
"""Join every input into one census document (data/census.json) with private members redacted.

Inputs under data/: prs, prdetail, match, match_extra, runs, jobs.main.*.jsonl, jobs.extra.*.jsonl,
reqcheck, issues, project3, usage_summary, repos, adjudication (hand-made, see adjudication.example.json).
"""
import collections
import glob
import hashlib
import json
import os
import re

from census_common import DATA, cfg, dump, load, pr_digest, ts

C = cfg()
pop, det, match, extra = load("prs.json"), load("prdetail.json"), load("match.json"), load("match_extra.json")
reqc, issues, pj, usage = load("reqcheck.json"), load("issues.json"), load("project3.json"), load("usage_summary.json")
adj = load("adjudication.json")
PRIVATE = set(load("repos.json")["private"])


def read_jobs(family):
    jobs = {}
    for fn in sorted(glob.glob(os.path.join(DATA, f"jobs.{family}.*.jsonl"))):
        for line in open(fn, encoding="utf-8"):
            o = json.loads(line)
            if "error" not in o or o["run_id"] not in jobs:
                jobs[o["run_id"]] = o
    return jobs


jobs = read_jobs("main")
YES, EARLIER, UNBOUND, NOTE = dict(adj["V"]), dict(adj["E"]), list(adj["A"]), dict(adj["NOTE"])
for k, v in EARLIER.items():
    NOTE[k] = v
for k in UNBOUND:
    NOTE[k] = "independent verdict stated; head not named"


def jobsecs(ids, source=None):
    source = source or jobs
    s, n, miss, incomplete, negative = 0.0, 0, 0, 0, 0
    for i in ids:
        j = source.get(i)
        if not j or "error" in j:
            miss += 1
            continue
        for x in j["jobs"]:
            if x["started_at"] and x["completed_at"]:
                d = (ts(x["completed_at"]) - ts(x["started_at"])).total_seconds()
                if d > 0:
                    s += d
                elif d < 0:
                    negative += 1  # counted as 0 s; the report states the rule
                n += 1
            else:
                incomplete += 1
    return s, n, miss, incomplete, negative


attr = {a[1]: a for a in usage["attributed"]}
rows = []
for p in pop["prs"]:
    k = f'{p["repo"]}#{p["number"]}'
    m = match[k]
    marker = re.findall(r"<!--\s*fsgg:routine-development/v1\s+head=([0-9a-f]+)\s+operation=([\w.-]+)\s*-->", p["body"] or "")
    rv = "independent verdict at exact head: " + YES[k] if k in YES else "unsampled (" + NOTE.get(k, "no independent verdict in PR record") + ")"
    rcat = "V" if k in YES else ("E" if k in EARLIER else ("A" if k in UNBOUND else "N"))
    pre, post = jobsecs(m["pre"]), jobsecs(m["post"])
    el = (ts(p["mergedAt"]) - ts(p["createdAt"])).total_seconds()
    a = attr.get(k)
    route = (f"routine-development/v1 operation={marker[0][1]} head={'final' if marker[0][0] == p['headRefOid'] else 'stale'}" if marker
             else (", ".join(sorted(set(re.findall(r"<!--\s*(fsgg[\w:./-]*)", p["body"] or "")))) or "none"))
    rows.append({"member": k, "title": p["title"], "head": p["headRefOid"], "merge": p["mergeCommit"]["oid"], "createdAt": p["createdAt"], "mergedAt": p["mergedAt"],
                 "route_marker": route, "review": rv, "review_class": rcat, "required_checks": reqc[k]["result"],
                 "required_detail": reqc[k]["detail"] if reqc[k]["result"] != "pass" else None,
                 "pr_runs": len(m["pre"]), "pr_jobs": pre[1], "pr_job_seconds": round(pre[0]), "pr_runs_unread": pre[2], "pr_jobs_incomplete": pre[3],
                 "merge_runs": len(m["post"]), "merge_jobs": post[1], "merge_job_seconds": round(post[0]), "merge_runs_unread": post[2],
                 "elapsed_open_to_merge_seconds_including_overlapping_queues": round(el),
                 "native_tokens": ({"runtime": "claude", "responses": a[2], **a[3], "join": "record gitBranch == PR head branch, timestamp <= mergedAt"} if a else "unattributed")})
allpre = {i for v in match.values() for i in v["pre"]}
allpost = {i for v in match.values() for i in v["post"]} - allpre
P, Q = jobsecs(sorted(allpre)), jobsecs(sorted(allpost))
shared = collections.Counter(i for v in match.values() for i in v["pre"] + v["post"])
closing = collections.defaultdict(list)
for k, v in det.items():
    for n in v["closingIssuesReferences"]["nodes"]:
        closing[(n["repository"]["nameWithOwner"], n["number"])].append(k)
ir = []
for d in issues:
    ev = d["timelineItems"]["nodes"][-1] if d["timelineItems"]["nodes"] else None
    cl = (ev or {}).get("closer")
    closer = ("PR " + cl["repository"]["nameWithOwner"] + "#" + str(cl["number"])) if cl and cl["__typename"] == "PullRequest" else (("commit " + cl["oid"][:12]) if cl else "no closer recorded")
    ir.append({"member": d["repo"] + "#" + str(d["number"]), "title": d["title"], "closedAt": d["closedAt"], "stateReason": d["stateReason"], "projectStatus": d["projectStatus"], "closer": closer, "population_prs_declaring_close": closing.get((d["repo"], d["number"]), [])})
# Redact private-repository members from the public report: keep only counted metrics.
pn = 0
placeholder = {}
for r in rows:
    if r["member"].split("#")[0] in PRIVATE:
        pn += 1
        placeholder[r["member"]] = f"private-repository-member-{pn}"
        r.update({"member": f"private-repository-member-{pn}", "title": None, "head": None, "merge": None, "createdAt": None, "mergedAt": None, "required_detail": None,
                  "review": ("independent verdict at exact head: redacted" if r["review_class"] == "V" else "unsampled (redacted)")})
qn = 0
for i in ir:
    if i["member"].split("#")[0] in PRIVATE:
        qn += 1
        i.update({"member": f"private-repository-item-{qn}", "title": None, "closedAt": "redacted", "closer": "redacted", "population_prs_declaring_close": []})
    else:
        if any(repo in i["closer"] for repo in PRIVATE):
            i["closer"] = "redacted"
        i["population_prs_declaring_close"] = [placeholder.get(x, x) for x in i["population_prs_declaring_close"]]
for a_ in usage["attributed"]:
    a_[1] = placeholder.get(a_[1], a_[1])
# Redacted members are listed last so their position does not reveal their timestamps.
rows.sort(key=lambda r: r["member"].startswith("private-"))
ir.sort(key=lambda i: i["member"].startswith("private-"))
iids = "\n".join(f'{i["content"]["repository"]["nameWithOwner"]}#{i["content"]["number"]} {i["content"]["__typename"]} {i["content"]["closedAt"]}' for i in pj["closed"]) + "\n"
# Force-pushed-head runs: disclosed outside the totals.
xjobs = read_jobs("extra")
xids = sorted({i for v in extra.values() for i in v["pre"]})
X = jobsecs(xids, xjobs) if xids else (0.0, 0, 0, 0, 0)
res = {"schema": "fsgg.census.usage-review/1", "issue": C["issue"], "window": {"from": C["lo"], "to_exclusive": C["hi"]},
       "population": {"merged_prs": len(rows), "pr_member_digest_sha256": pr_digest(pop["prs"]), "project3_closed_items": len(ir),
                      "project3_member_digest_sha256": hashlib.sha256(iids.encode()).hexdigest(), "project3_items_visible": pj["total"]},
       "actions": {"pr_phase_runs": len(allpre), "pr_phase_jobs": P[1], "pr_phase_job_seconds": round(P[0]), "pr_runs_unread": P[2],
                   "merge_commit_runs": len(allpost), "merge_commit_jobs": Q[1], "merge_commit_job_seconds": round(Q[0]), "merge_runs_unread": Q[2],
                   "runs_shared_by_members": sum(1 for c in shared.values() if c > 1), "jobs_incomplete": P[3] + Q[3],
                   "jobs_with_completed_before_started_counted_as_zero": {"pr_phase": P[4], "merge_push": Q[4], "force_pushed_head": X[4]},
                   "force_pushed_head_pr_runs_not_in_totals": {"runs": len(xids), "members": len(extra), "jobs": X[1], "job_seconds": round(X[0]), "unread": X[2]}},
       "usage": usage, "prs": rows, "project3": ir}
blob = json.dumps(res)
for repo in PRIVATE:  # fail closed: no private repository name may reach a published file
    if repo in blob or repo.split("/", 1)[1] in blob:
        raise SystemExit(f"private repository {repo} would be published; extend the redaction")
dump("census.json", res, indent=1, ensure_ascii=False)
print(json.dumps({k: res[k] for k in ("population", "actions")}, indent=1))
print(collections.Counter(r["review_class"] for r in rows))

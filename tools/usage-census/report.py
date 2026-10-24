#!/usr/bin/env python3
"""Render the per-member tables of the census report, and the figures its prose cites.

usage: report.py OUTPUT.md [--previous EVIDENCE.json] [--template TEMPLATE.md]

Always writes the two per-member tables. Writes data/ctx.json with every computed figure. With
--template the template is formatted with those figures and placed before the tables; the template
`report-template.md` is the prose of a continuation report and needs --previous (the evidence JSON of
the segment this one continues, for the "Together" columns). Re-read the prose against the data:
the figures are computed, the sentences about the window are not.
"""
import argparse
import collections
import csv
import hashlib
import json
import statistics

from census_common import SESSION_COL, cfg, data_path, dump, load

ap = argparse.ArgumentParser()
ap.add_argument("output")
ap.add_argument("--previous")
ap.add_argument("--template")
args = ap.parse_args()
C = cfg()
ORG = C["org"] + "/"
c = load("census.json")
U, A, rows, P = c["usage"], c["actions"], c["prs"], c["population"]


def hm(s):
    s = int(s)
    return f"{s // 3600}h{(s % 3600) // 60:02d}m"


def hrs(s):
    return f"{s / 3600:,.1f} h"


el = sorted(r["elapsed_open_to_merge_seconds_including_overlapping_queues"] for r in rows)
rvc = collections.Counter(r["review_class"] for r in rows)
rq = collections.Counter(r["required_checks"] for r in rows)
mk = collections.Counter(r["route_marker"] for r in rows)
byrepo = collections.Counter(("private repository" if r["member"].startswith("private-") else r["member"].split("#")[0].replace(ORG, "")) for r in rows)
attr = U["attributed"]
attr_total = sum(a[3]["total"] for a in attr)
cl_total = U["totals"].get("claude", {}).get("total", 0)
cx_total = U["totals"].get("codex", {}).get("total", 0)
X = A["force_pushed_head_pr_runs_not_in_totals"]
NEG = A["jobs_with_completed_before_started_counted_as_zero"]
ms = load("match_stats.json")
ctx = dict(
    n_new=P["merged_prs"], p3_new=P["project3_closed_items"], p3_vis=P["project3_items_visible"], V_new=rvc["V"], U_new=P["merged_prs"] - rvc["V"],
    rv_a=rvc["A"], rv_e=rvc["E"], rv_n=rvc["N"],
    pr_runs=f"{A['pr_phase_runs']:,}", pr_jobs=f"{A['pr_phase_jobs']:,}", pr_h=hrs(A["pr_phase_job_seconds"]),
    mg_runs=f"{A['merge_commit_runs']:,}", mg_jobs=f"{A['merge_commit_jobs']:,}", mg_h=hrs(A["merge_commit_job_seconds"]),
    el_med=hm(statistics.median(el)), el_p90=hm(el[int(0.9 * len(el))]), el_max=hm(el[-1]),
    cl_resp=f"{U['responses'].get('claude', 0):,}", cx_resp=f"{U['responses'].get('codex', 0):,}", cl_total=f"{cl_total:,}", cx_total=f"{cx_total:,}",
    attr_total=f"{attr_total:,}", attr_n=len(attr), attr_share=f"{100 * attr_total / cl_total:.1f}%" if cl_total else "n/a", dupes=U["dupes"],
    pr_digest=P["pr_member_digest_sha256"], p3_digest=P["project3_member_digest_sha256"],
    n_priv=sum(1 for r in rows if r["member"].startswith("private-")),
    req_new=", ".join(f"{k} {n}" for k, n in rq.most_common()), mk_new="; ".join(f"{n} × `{k}`" for k, n in mk.most_common()),
    by_repo=", ".join(f"{k} {n}" for k, n in byrepo.most_common()), repo_count=len(load("prs.json")["repos"]),
    all_runs=f"{len(load('runs.json')):,}", attr_rows="\n".join(f"| {a[1].replace(ORG, '')} | {a[2]:,} | {a[3]['total']:,} |" for a in attr),
    extra_runs=f"{X['runs']:,}", extra_members=X["members"], extra_jobs=f"{X['jobs']:,}", extra_h=hrs(X["job_seconds"]),
    pr_h_plus=hrs(A["pr_phase_job_seconds"] + X["job_seconds"]), neg_pr=NEG["pr_phase"], neg_mg=NEG["merge_push"], neg_ex=NEG["force_pushed_head"],
    ms_merge_sha_nonpush_excluded=ms.get("merge_sha_nonpush_excluded", 0), ms_issues=ms.get("nonpush:issues", 0), ms_schedule=ms.get("nonpush:schedule", 0),
    ms_workflow_dispatch=ms.get("nonpush:workflow_dispatch", 0), ms_dynamic=ms.get("nonpush:dynamic", 0),
)
# reader / model / session tables
idx = [line.rstrip("\n").split("\t") for line in open(data_path("usage/index.tsv"), encoding="utf-8")]
rt = collections.defaultdict(collections.Counter)
for r_, fid, rc, path, err in idx:
    k = "codex rollout" if r_ == "codex" else r_ + (" subagent" if "/subagents/" in path else " main")
    rt[k]["files"] += 1
    if rc == "0":
        rt[k]["read"] += 1
    elif "matched the requested turn/time window" in err:
        rt[k]["oow"] += 1
    elif "no Claude API responses found" in err:
        rt[k]["none"] += 1
    else:
        rt[k]["bad"] += 1
ctx["reader_rows"] = "\n".join(f"| {k} | {x['files']} | {x['read']} | {x['oow']} | {x['none']} | {x['bad']} |" for k, x in sorted(rt.items()))
ctx["model_rows"] = "\n".join(f"| {m[0]} | {m[1]} | {m[2]['responses']:,} | {m[2]['input']:,} | {m[2]['cached_input']:,} | {m[2]['cache_write_input']:,} | {m[2]['output']:,} | {m[2]['total']:,} |" for m in U["models"])
seen, per, thr, span = set(), collections.defaultdict(collections.Counter), collections.defaultdict(set), collections.defaultdict(list)
for r_, fid, rc, path, err in idx:
    if rc != "0":
        continue
    for r in csv.DictReader(open(data_path(f"usage/{r_}/{fid}.csv"), encoding="utf-8")):
        if (r_, r["response_id"]) in seen:
            continue
        seen.add((r_, r["response_id"]))
        s = (r_, hashlib.sha256(r[SESSION_COL].encode()).hexdigest()[:10])
        per[s]["responses"] += 1
        per[s]["total"] += int(r["total"])
        thr[s].add(r["thread_id"])
        span[s].append(r["timestamp"])
ctx["session_rows"] = "\n".join(f"| {k[0]} | `{k[1]}` | {len(thr[k])} | {x['responses']:,} | {x['total']:,} | {min(span[k])} | {max(span[k])} |" for k, x in sorted(per.items(), key=lambda kv: min(span[kv[0]])))
if args.previous:
    old = json.load(open(args.previous, encoding="utf-8"))
    OU, OA, OP = old["usage"], old["actions"], old["population"]
    old_rev = collections.Counter(r[6] for r in old["prs"]["rows"])
    old_req = collections.Counter(r[8] for r in old["prs"]["rows"])
    n_all = P["merged_prs"] + OP["merged_prs"]
    V_all = rvc["V"] + old_rev["V"]
    ocl, ocx = OU["totals"].get("claude", {}).get("total", 0), OU["totals"].get("codex", {}).get("total", 0)
    oattr = sum(a[3]["total"] for a in OU["attributed"])
    ctx.update(n_old=OP["merged_prs"], p3_old=OP["project3_closed_items"], n_all=n_all, p3_all=P["project3_closed_items"] + OP["project3_closed_items"],
               V_old=old_rev["V"], V_all=V_all, U_all=n_all - V_all, req_all=", ".join(f"{k} {n}" for k, n in (rq + old_req).most_common()),
               all_pr_runs=f"{A['pr_phase_runs'] + OA['pr_phase_runs']:,}", all_pr_jobs=f"{A['pr_phase_jobs'] + OA['pr_phase_jobs']:,}",
               all_mg_runs=f"{A['merge_commit_runs'] + OA['merge_commit_runs']:,}", all_mg_jobs=f"{A['merge_commit_jobs'] + OA['merge_commit_jobs']:,}",
               all_pr_h=hrs(A["pr_phase_job_seconds"] + OA["pr_phase_job_seconds"]), all_mg_h=hrs(A["merge_commit_job_seconds"] + OA["merge_commit_job_seconds"]),
               all_cl=f"{cl_total + ocl:,}", all_cx=f"{cx_total + ocx:,}", all_clr=f"{U['responses'].get('claude', 0) + OU['responses'].get('claude', 0):,}",
               all_cxr=f"{U['responses'].get('codex', 0) + OU['responses'].get('codex', 0):,}", all_attr=f"{attr_total + oattr:,}", all_attr_n=len(attr) + len(OU["attributed"]))
dump("ctx.json", ctx, indent=1, default=str)
with open(args.output, "w", encoding="utf-8") as out:
    W = out.write
    if args.template:
        W(open(args.template, encoding="utf-8").read().format(**ctx))
    W("\n## Per-member results: merged PRs\n\n")
    W("Review: `V` = independent verdict at the exact final head found in the PR record; `U` = unsampled. Checks: required-check conclusion on the final head (rules read at census time). PR jobs and merge jobs: Jobs API `started_at`→`completed_at` summed over PR-phase runs and merge-commit runs. Open→merge includes overlapping queues; it is not idle or critical-path time. Tokens: Claude Code tokens joined by branch, or `—` (unattributed).\n\n")
    W("| Member | Head | Merge | Route | Review | Checks | PR runs/jobs | PR job time | Merge push runs/jobs | Merge job time | Open→merge | Tokens |\n|---|---|---|---|---|---|---|---|---|---|---|---|\n")
    for r in rows:
        rm = r["route_marker"].replace("routine-development/v1 operation=", "").replace(" head=final", "")
        rvs = "V" if r["review_class"] == "V" else "U"
        note = r["review"].split(": ", 1)[1] if rvs == "V" else (r["review"][len("unsampled"):].strip(" ()") or "")
        pj = f'{r["pr_runs"]}/{r["pr_jobs"]}' + (f' ({r["pr_runs_unread"]} unread)' if r["pr_runs_unread"] else "")
        mj = f'{r["merge_runs"]}/{r["merge_jobs"]}' + (f' ({r["merge_runs_unread"]} unread)' if r["merge_runs_unread"] else "")
        mem = r["member"].replace(ORG, "")
        hd = f'`{r["head"][:8]}`' if r["head"] else "redacted"
        mg = f'`{r["merge"][:8]}`' if r["merge"] else "redacted"
        tok = r["native_tokens"]
        tk = f'{tok["total"]:,} ({tok["responses"]} resp)' if isinstance(tok, dict) else "—"
        W(f'| {mem} | {hd} | {mg} | {rm} | {rvs}{": " + note if note else ""} | {r["required_checks"]} | {pj} | {hm(r["pr_job_seconds"])} | {mj} | {hm(r["merge_job_seconds"])} | {hm(r["elapsed_open_to_merge_seconds_including_overlapping_queues"])} | {tk} |\n')
    W("\n## Per-member results: Project 3 items closed\n\n| Member | Closed | Reason | Status | Closer | Population PRs declaring close |\n|---|---|---|---|---|---|\n")
    for i in c["project3"]:
        W(f'| {i["member"].replace(ORG, "")} | {i["closedAt"]} | {i["stateReason"]} | {i["projectStatus"]} | {i["closer"].replace(ORG, "")} | {", ".join(x.replace(ORG, "") for x in i["population_prs_declaring_close"]) or "—"} |\n')
print("wrote", args.output)

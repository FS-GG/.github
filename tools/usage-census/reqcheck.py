#!/usr/bin/env python3
"""Score each member's final-head checks against the required contexts read by required_rules.py."""
import collections

from census_common import dump, load

pop, req, rules = load("prs.json"), load("required.json"), load("required_rules.json")
out = {}
for p in pop["prs"]:
    key = f'{p["repo"]}#{p["number"]}'
    c, rule = req[key], rules.get(p["repo"])
    if rule is None or not rule["readable"] or c.get("error"):
        out[key] = {"result": "unknown", "detail": "required-check rules or head checks unreadable with census credentials"}
        continue
    names = rule["contexts"]
    if not names:
        out[key] = {"result": "none-configured", "detail": "no required status checks configured"}
        continue
    by = collections.defaultdict(list)
    for n in (c["statusCheckRollup"] or {"contexts": {"nodes": []}})["contexts"]["nodes"]:
        if n["__typename"] == "CheckRun":
            by[n["name"]].append((n.get("completedAt") or "", (n["conclusion"] or n["status"] or "").lower()))
        else:
            by[n["context"]].append(("", n["state"].lower()))
    det = {}
    for name in names:
        det[name] = sorted(by[name])[-1][1] if name in by else "missing"
    vals = set(det.values())
    res = "pass" if vals <= {"success"} else ("fail" if vals & {"failure", "cancelled", "timed_out", "error"} else ("missing" if "missing" in vals else "other"))
    if vals <= {"success", "skipped", "neutral"} and res != "pass":
        res = "pass (with skipped/neutral)"
    out[key] = {"result": res, "detail": {n: v for n, v in det.items() if v != "success"}}
dump("reqcheck.json", out, indent=1)
print(collections.Counter(v["result"] for v in out.values()))
for key, value in out.items():
    if value["result"] != "pass":
        print(key, value)

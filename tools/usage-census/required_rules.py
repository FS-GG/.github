#!/usr/bin/env python3
"""Read the required status check contexts of each population repository's default branch, now.

The union of base-branch ruleset `required_status_checks` and classic branch protection is the rule
set. A repository whose rules cannot be read (HTTP 403, for example a private repository on a plan
without rules) is recorded as unreadable, and its members score `unknown`, never `pass`.
"""
import json
import subprocess

from census_common import cfg, dump, load

ORG = cfg()["org"]
pop = load("prs.json")
repos = sorted({p["repo"] for p in pop["prs"]})
base = {p["repo"]: p["baseRefName"] for p in pop["prs"]}  # fallback when the repository record is unreadable


def call(path):
    proc = subprocess.run(["gh", "api", path], capture_output=True, text=True)
    return proc.returncode, proc.stdout, proc.stderr


out = {}
for repo in repos:
    rc0, so0, _ = call(f"repos/{repo}")
    branch = json.loads(so0)["default_branch"] if not rc0 else base[repo]
    contexts, readable, notes = set(), True, []
    rc, so, se = call(f"repos/{repo}/rules/branches/{branch}")
    if rc:
        readable = False
        notes.append("rules unreadable: " + se.strip()[:120])
    else:
        for rule in json.loads(so):
            if rule.get("type") == "required_status_checks":
                contexts |= {c["context"] for c in rule["parameters"]["required_status_checks"]}
    rc, so, se = call(f"repos/{repo}/branches/{branch}/protection/required_status_checks")
    if rc and "404" in se:
        notes.append("no classic protection")  # readable: protection is simply absent
    elif rc:
        readable = False
        notes.append("classic protection unreadable: " + se.strip()[:120])
    else:
        contexts |= set(json.loads(so)["contexts"])
    out[repo] = {"readable": readable, "contexts": sorted(contexts), "notes": notes}
dump("required_rules.json", out, indent=1, ensure_ascii=False)
for repo, value in out.items():
    print(repo, "readable" if value["readable"] else "UNREADABLE", len(value["contexts"]))

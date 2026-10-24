#!/usr/bin/env python3
"""Read the check runs and statuses on each member's final head."""
import subprocess
import json
from concurrent.futures import ThreadPoolExecutor

from census_common import dump, load

pop = load("prs.json")
Q = """query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){commits(last:1){nodes{commit{oid statusCheckRollup{state contexts(first:100){totalCount pageInfo{hasNextPage} nodes{__typename ... on CheckRun{name conclusion status startedAt completedAt checkSuite{workflowRun{databaseId}}} ... on StatusContext{context state}}}}}}}}}}"""
Q2 = """query($o:String!,$r:String!,$sha:GitObjectID!,$c:String){repository(owner:$o,name:$r){object(oid:$sha){... on Commit{statusCheckRollup{contexts(first:100,after:$c){pageInfo{hasNextPage endCursor} nodes{__typename ... on CheckRun{name conclusion status startedAt completedAt checkSuite{workflowRun{databaseId}}} ... on StatusContext{context state}}}}}}}}"""


def get(p):
    owner, repo = p["repo"].split("/")
    proc = subprocess.run(["gh", "api", "graphql", "-f", "query=" + Q, "-f", f"o={owner}", "-f", f"r={repo}", "-F", f"n={p['number']}"], capture_output=True, text=True)
    key = f"{p['repo']}#{p['number']}"
    if proc.returncode:
        return key, {"oid": p["headRefOid"], "error": proc.stderr.strip()[:200], "statusCheckRollup": None}
    return key, json.loads(proc.stdout)["data"]["repository"]["pullRequest"]["commits"]["nodes"][0]["commit"]


with ThreadPoolExecutor(2) as pool:
    res = dict(pool.map(get, pop["prs"]))
for p in pop["prs"]:
    key = f"{p['repo']}#{p['number']}"
    c = res[key]
    if c["oid"] != p["headRefOid"]:
        print("HEADMISMATCH", key)
    roll = c.get("statusCheckRollup")
    if roll and roll["contexts"]["pageInfo"]["hasNextPage"]:  # page the remaining contexts
        owner, repo = p["repo"].split("/")
        nodes, cursor = [], None
        while True:
            args = ["api", "graphql", "-f", "query=" + Q2, "-f", f"o={owner}", "-f", f"r={repo}", "-f", f"sha={c['oid']}"] + (["-f", f"c={cursor}"] if cursor else [])
            page = json.loads(subprocess.check_output(["gh", *args], text=True))["data"]["repository"]["object"]["statusCheckRollup"]["contexts"]
            nodes += page["nodes"]
            if not page["pageInfo"]["hasNextPage"]:
                break
            cursor = page["pageInfo"]["endCursor"]
        roll["contexts"]["nodes"] = nodes
        roll["contexts"]["pageInfo"]["hasNextPage"] = False
dump("required.json", res)
print("errors:", [k for k, c in res.items() if c.get("error")])

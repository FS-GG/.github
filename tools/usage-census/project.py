#!/usr/bin/env python3
"""Freeze the Project 3 items whose content closed inside the window."""
import hashlib

from census_common import cfg, dump, gh_json

C = cfg()
LO, HI, ORG = C["lo"], C["hi"], C["org"]
Q = """query($c:String){organization(login:"%s"){projectV2(number:%d){title items(first:100,after:$c){totalCount pageInfo{hasNextPage endCursor} nodes{id type isArchived content{__typename ... on Issue{number url repository{nameWithOwner} title state stateReason closedAt createdAt} ... on PullRequest{number url repository{nameWithOwner} title state closedAt mergedAt createdAt}} fieldValueByName(name:"Status"){... on ProjectV2ItemFieldSingleSelectValue{name}}}}}}}""" % (ORG, C.get("project", 3))
items, cursor, total = [], None, None
while True:
    args = ["api", "graphql", "-f", "query=" + Q] + (["-f", f"c={cursor}"] if cursor else [])
    page = gh_json(*args)["data"]["organization"]["projectV2"]["items"]
    total = page["totalCount"]
    items += page["nodes"]
    if not page["pageInfo"]["hasNextPage"]:
        break
    cursor = page["pageInfo"]["endCursor"]
sel = [i for i in items if i["content"] and i["content"].get("closedAt") and LO <= i["content"]["closedAt"] < HI]
sel.sort(key=lambda i: (i["content"]["closedAt"], i["content"]["repository"]["nameWithOwner"], i["content"]["number"]))
dump("project3.json", {"total": total, "fetched": len(items), "closed": sel}, indent=1)
ids = "\n".join(f'{i["content"]["repository"]["nameWithOwner"]}#{i["content"]["number"]} {i["content"]["__typename"]} {i["content"]["closedAt"]}' for i in sel) + "\n"
print(total, len(items), len(sel), hashlib.sha256(ids.encode()).hexdigest())

#!/usr/bin/env python3
"""Freeze the merged-PR population of the window: every organization repository, GraphQL, mergedAt in [lo, hi)."""
from census_common import cfg, dump, gh_json, gql, member_key, pr_digest

C = cfg()
LO, HI, ORG = C["lo"], C["hi"], C["org"]
Q = """query($o:String!,$r:String!,$c:String){repository(owner:$o,name:$r){pullRequests(states:MERGED,first:50,after:$c,orderBy:{field:UPDATED_AT,direction:DESC}){pageInfo{hasNextPage endCursor} nodes{number title url createdAt mergedAt updatedAt headRefName headRefOid baseRefName mergeCommit{oid} body author{login} mergedBy{login}}}}}"""
repos = gh_json("repo", "list", ORG, "--limit", "500", "--json", "name,isArchived,visibility")
members = []
for name in sorted(r["name"] for r in repos):
    cursor = None
    while True:
        page = gql(Q, o=ORG, r=name, c=cursor)["data"]["repository"]["pullRequests"]
        stop = False
        for node in page["nodes"]:
            if node["updatedAt"] < LO:
                stop = True
                break
            if LO <= node["mergedAt"] < HI:
                node["repo"] = f"{ORG}/{name}"
                members.append(node)
        if stop or not page["pageInfo"]["hasNextPage"]:
            break
        cursor = page["pageInfo"]["endCursor"]
members.sort(key=lambda n: (n["mergedAt"], n["repo"], n["number"]))
dump("prs.json", {"window": [LO, HI], "repos": sorted(r["name"] for r in repos), "prs": members}, indent=1)
dump("repos.json", {"private": sorted(f'{ORG}/{r["name"]}' for r in repos if r["visibility"] != "PUBLIC")})
print(len(members), pr_digest(members))

#!/usr/bin/env python3
"""Read the closer of each closed Project 3 item."""
from census_common import dump, gh_json, load

pj = load("project3.json")
Q = """query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){issue(number:$n){number title createdAt closedAt stateReason state
 timelineItems(itemTypes:[CLOSED_EVENT],last:5){nodes{... on ClosedEvent{createdAt stateReason closer{__typename ... on PullRequest{number repository{nameWithOwner} mergedAt} ... on Commit{oid repository{nameWithOwner}}}}}}}}}"""
out = []
for item in pj["closed"]:
    c = item["content"]
    owner, repo = c["repository"]["nameWithOwner"].split("/")
    d = gh_json("api", "graphql", "-f", "query=" + Q, "-f", f"o={owner}", "-f", f"r={repo}", "-F", f"n={c['number']}")["data"]["repository"]["issue"]
    d["repo"] = c["repository"]["nameWithOwner"]
    d["projectStatus"] = (item["fieldValueByName"] or {}).get("name")
    out.append(d)
dump("issues.json", out, indent=1)
print(len(out))

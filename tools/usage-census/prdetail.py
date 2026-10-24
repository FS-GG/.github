#!/usr/bin/env python3
"""Read commits, submitted reviews, comments and closing references of every member PR."""
from concurrent.futures import ThreadPoolExecutor

from census_common import dump, gh_json, load

pop = load("prs.json")
Q = """query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){pullRequest(number:$n){
 commits(first:100){totalCount nodes{commit{oid}}}
 reviews(first:100){totalCount nodes{author{login} state submittedAt commit{oid} body}}
 comments(first:100){totalCount nodes{databaseId author{login} createdAt updatedAt body}}
 closingIssuesReferences(first:20){nodes{number repository{nameWithOwner}}}
}}}"""


def get(p):
    owner, repo = p["repo"].split("/")
    d = gh_json("api", "graphql", "-f", "query=" + Q, "-f", f"o={owner}", "-f", f"r={repo}", "-F", f"n={p['number']}")
    return f"{p['repo']}#{p['number']}", d["data"]["repository"]["pullRequest"]


with ThreadPoolExecutor(2) as pool:  # CPU budget: at most two
    res = dict(pool.map(get, pop["prs"]))
dump("prdetail.json", res)
for key, value in res.items():
    for field in ("commits", "reviews", "comments"):
        if value[field]["totalCount"] > 100:
            print("TRUNCATED", key, field, value[field]["totalCount"])
print(len(res))

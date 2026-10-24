#!/usr/bin/env python3
"""Candidate finder and full-text dump for the hand adjudication of review evidence.

usage: review.py [--dump]

Writes review_raw.json (regex candidates, advisory only). With --dump it prints, for every member,
the FULL text of every body/comment paragraph that mentions a review, critic or reviewer, so that the
adjudication reads the whole statement. Paragraph-level regex matching alone gives false negatives
and truncated excerpts hide a second statement in the same comment: adjudicate from the full text.

A member scores V only when ONE review statement or ONE dedicated review comment (a) names an
independent review or critic, (b) gives an explicit verdict (PASS, ACCEPT, APPROVE, "no outstanding
in-scope defects"), and (c) binds that verdict to the FINAL head SHA in the same statement or comment.
Text inside HTML comments (the routine marker) does not count. A submitted GitHub review counts as a
dedicated review comment. A final line `VERDICT: APPROVE|REQUEST_CHANGES @ <40-hex head>` (the critic
convention) is recorded exactly in `verdict_lines`, with `final_head` true when it names the final head. See adjudication.example.json for the table the adjudication produces.
"""
import collections
import re
import sys

from census_common import dump, load

pop, det = load("prs.json"), load("prdetail.json")
VERD = re.compile(r"(PASS(?:ED)?|ACCEPT(?:ED|S)?|APPROVE[SD]?|REQUEST[_ ]CHANGES|REJECT(?:ED|S)?|no outstanding in-scope defects)", re.I)
REV = re.compile(r"(independent|critic|review)", re.I)
KW = re.compile(r"independent|critic|reviewer|review", re.I)
strip = lambda t: re.sub(r"<!--.*?-->", "", t or "", flags=re.S)


def units(text):
    return [u for u in re.split(r"\n\s*\n|\n(?=[-*] )|\n(?=#)", text) if u.strip()]


def nearest(u, pos):
    best = None
    for m in VERD.finditer(u):
        d = abs(m.start() - pos)
        if best is None or d < best[0]:
            best = (d, m.group(1))
    return best[1] if best else None


VLINE = re.compile(r"^VERDICT: (APPROVE|REQUEST_CHANGES) @ ([0-9a-f]{40})\s*$", re.M)
out = {}
for p in pop["prs"]:
    key = f'{p["repo"]}#{p["number"]}'
    head = p["headRefOid"].lower()
    pref = head[:7]
    d = det[key]
    sources = [("C%s" % c["databaseId"], strip(c["body"])) for c in d["comments"]["nodes"]] + [("body", strip(p["body"]))]
    sources += [("R%s" % r["submittedAt"], strip(r["body"])) for r in d["reviews"]["nodes"]]
    found = []
    for kind, text in sources:
        for u in units(text):
            i = u.lower().find(pref)
            if i >= 0 and REV.search(u) and VERD.search(u):
                found.append({"source": kind, "verdict": nearest(u, i), "excerpt": re.sub(r"\s+", " ", u)[:260]})
    # Machine-readable verdict lines (critic convention): exact, and bound to the final head or not.
    vlines = [{"source": kind, "verdict": m.group(1), "head": m.group(2), "final_head": m.group(2) == head}
              for kind, text in sources for m in VLINE.finditer(text)]
    mention = bool(re.search(r"independent[^.\n]{0,80}review|fsgg-critic|critic", (p["body"] or "") + "\n".join(c["body"] or "" for c in d["comments"]["nodes"]), re.I))
    out[key] = {"found": found, "review_mentioned": mention, "submitted_reviews": d["reviews"]["totalCount"], "verdict_lines": vlines}
dump("review_raw.json", out, indent=1)
print(collections.Counter(("hit" if v["found"] else ("mention" if v["review_mentioned"] else "none")) for v in out.values()))
print("members with submitted GitHub reviews:", [k for k, v in out.items() if v["submitted_reviews"]])
if "--dump" in sys.argv[1:]:
    for p in pop["prs"]:
        key = f'{p["repo"]}#{p["number"]}'
        d = det[key]
        print("=====", key, "final head", p["headRefOid"])
        for r in d["reviews"]["nodes"]:
            print(f'  [submitted review {r["state"]} {r["submittedAt"]} commit {(r["commit"] or {}).get("oid")}] {r["body"]}')
        for kind, text in [("body", strip(p["body"]))] + [("C%s" % c["databaseId"], strip(c["body"])) for c in d["comments"]["nodes"]]:
            for u in units(text):
                if KW.search(u):
                    print(f"  [{kind}] {u.strip()}")

#!/usr/bin/env python3
"""Offline smoke test of tools/usage-census.

Builds a tiny synthetic work directory outside the repository (two member PRs, one in a private
repository; five runs; two transcripts) and runs every offline tool over it in order: match, reqcheck,
review, joinkeys, usage_summary, assemble, export and report. The network tools (population, project,
prdetail, required_rules, required, issues, runs, jobs) and the reader build are only compiled here:
they need GitHub and a .NET build, which a smoke test must not.

Properties asserted: exclusive run assignment, push-only merge runs, the force-pushed-head run kept
out of the totals, the zero-second rule for a job that ends before it starts, V/unsampled scoring from
the adjudication table, unknown (never pass) for an unreadable repository, deduplication of a resumed
session's response, the branch join, redaction of the private member in both published files, and the
evidence size cap.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TOOLS = ROOT / "tools" / "usage-census"
sys.dont_write_bytecode = True

for tool in sorted(TOOLS.glob("*.py")):
    compile(tool.read_text(encoding="utf-8"), str(tool), "exec")

HEAD_A, HEAD_B = "a" * 40, "b" * 40
MERGE_A, MERGE_B = "c" * 40, "d" * 40
OLD_A, GONE = "e" * 40, "f" * 40


def check(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit("FAIL  " + message)
    print("ok    " + message)


def tool(work: Path, name: str, *args: str) -> str:
    env = dict(os.environ, CENSUS_WORK=str(work), PYTHONDONTWRITEBYTECODE="1", PYTHONPATH=str(TOOLS))
    proc = subprocess.run([sys.executable, str(TOOLS / name), *args], env=env, capture_output=True, text=True)
    if proc.returncode:
        raise SystemExit(f"FAIL  {name} exited {proc.returncode}\n{proc.stdout}\n{proc.stderr}")
    return proc.stdout


def write(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value) if not isinstance(value, str) else value, encoding="utf-8")


def pr(repo, number, head, merge, branch, created, merged, body):
    return {"repo": repo, "number": number, "title": f"title {number}", "url": "u", "createdAt": created, "mergedAt": merged, "updatedAt": merged,
            "headRefName": branch, "headRefOid": head, "baseRefName": "main", "mergeCommit": {"oid": merge}, "body": body}


def run(i, repo, event, sha, branch, created):
    return {"id": i, "name": "w", "event": event, "status": "completed", "conclusion": "success", "head_sha": sha, "head_branch": branch,
            "run_attempt": 1, "created_at": created, "updated_at": created, "run_started_at": created, "path": "w.yml", "repo": repo, "prs": []}


def job(start, end):
    return {"id": 1, "run_attempt": 1, "name": "j", "status": "completed", "conclusion": "success", "started_at": start, "completed_at": end}


def csv_row(ts, session, response, total, model="claude-opus-5-5"):
    return f"{ts},t,{session},{session},turn,{response},Anthropic,{model},high,1,61526e94,unknown,unknown,1,{total},0,0,0,0,{total}"


CSV_HEAD = "timestamp,task,session_id,thread_id,turn_id,response_id,provider,model,effort,runtime_version,coordination_version,sdd_version,contracts_version,ledger_schema,input,cached_input,cache_write_input,output,reasoning,total"

with tempfile.TemporaryDirectory(prefix="usage-census-smoke-") as tmp:
    work = Path(tmp) / "work"
    data = work / "data"
    marker = lambda head: f"<!-- fsgg:routine-development/v1 head={head} operation=internal-docs -->"
    prs = [pr("FS-GG/.github", 1, HEAD_A, MERGE_A, "routine/a", "2026-10-23T01:00:00Z", "2026-10-23T02:00:00Z", "body " + marker(HEAD_A)),
           pr("FS-GG/Private.Repo", 2, HEAD_B, MERGE_B, "routine/b", "2026-10-23T02:30:00Z", "2026-10-23T03:00:00Z", "body")]
    write(work / "config.json", {"org": "FS-GG", "project": 3, "issue": "FS-GG/.github#0", "task": "t", "lo": "2026-10-23T00:00:00Z", "hi": "2026-10-24T00:00:00Z",
                                 "coord_version": "61526e94", "reader": "unused", "receipt_store": str(Path(tmp) / "rs"), "claude_home": "unused", "codex_home": "unused"})
    write(data / "prs.json", {"window": ["a", "b"], "repos": [".github", "Private.Repo"], "prs": prs})
    write(data / "repos.json", {"private": ["FS-GG/Private.Repo"]})
    no_review = {"reviews": {"totalCount": 0, "nodes": []}, "closingIssuesReferences": {"nodes": []}}
    write(data / "prdetail.json", {
        "FS-GG/.github#1": {"commits": {"totalCount": 2, "nodes": [{"commit": {"oid": OLD_A}}, {"commit": {"oid": HEAD_A}}]},
                            "comments": {"totalCount": 1, "nodes": [{"databaseId": 7, "author": {"login": "x"}, "createdAt": "t", "updatedAt": "t",
                                                                     "body": f"Independent review (fsgg-critic-normal) of exact head {HEAD_A}: APPROVE\n\nVERDICT: APPROVE @ {HEAD_A}"}]}, **no_review},
        "FS-GG/Private.Repo#2": {"commits": {"totalCount": 1, "nodes": [{"commit": {"oid": HEAD_B}}]}, "comments": {"totalCount": 1, "nodes": [{"databaseId": 8, "author": {"login": "x"}, "createdAt": "t", "updatedAt": "t", "body": f"earlier head\n\nVERDICT: REQUEST_CHANGES @ {OLD_A}"}]}, **no_review}})
    runs = [run(1, "FS-GG/.github", "pull_request", HEAD_A, "routine/a", "2026-10-23T01:10:00Z"),
            run(2, "FS-GG/.github", "push", MERGE_A, "main", "2026-10-23T02:01:00Z"),
            run(3, "FS-GG/.github", "schedule", MERGE_A, "main", "2026-10-23T02:02:00Z"),
            run(4, "FS-GG/.github", "pull_request", GONE, "routine/a", "2026-10-23T01:30:00Z"),
            run(5, "FS-GG/Private.Repo", "pull_request", HEAD_B, "routine/b", "2026-10-23T02:40:00Z"),
            run(6, "FS-GG/.github", "pull_request", HEAD_A, "routine/a", "2026-10-23T02:30:00Z")]  # created after A merged: excluded
    write(data / "runs.json", runs)
    out = tool(work, "match.py")
    match = json.loads((data / "match.json").read_text())
    extra = json.loads((data / "match_extra.json").read_text())
    check(match["FS-GG/.github#1"] == {"pre": [1], "post": [2]}, "PR-phase run and push-on-merge run assigned; the scheduled run on the merge SHA excluded")
    check(match["FS-GG/Private.Repo#2"]["pre"] == [5], "the private member's run is assigned")
    check(6 not in match["FS-GG/.github#1"]["pre"] and 6 not in [i for v in extra.values() for i in v["pre"]], "a run created after the member's merge is excluded")
    check(extra == {"FS-GG/.github#1": {"pre": [4], "post": []}}, "a run on a force-pushed head is kept out of the match")

    t = "2026-10-23T01:"
    jobs = {1: [job(t + "10:00Z", t + "20:00Z"), job(t + "20:00Z", t + "25:00Z"), job(t + "30:00Z", t + "29:00Z")], 2: [job(t + "10:00Z", t + "11:40Z")], 5: [job(t + "10:00Z", t + "13:20Z")]}
    write(data / "jobs.main.0.jsonl", "".join(json.dumps({"run_id": i, "total": len(j), "jobs": j}) + "\n" for i, j in jobs.items()))
    write(data / "jobs.extra.0.jsonl", json.dumps({"run_id": 4, "total": 1, "jobs": [job(t + "10:00Z", t + "10:50Z")]}) + "\n")

    write(data / "required_rules.json", {"FS-GG/.github": {"readable": True, "contexts": ["gate"], "notes": []}, "FS-GG/Private.Repo": {"readable": False, "contexts": [], "notes": ["403"]}})
    gate = {"statusCheckRollup": {"contexts": {"pageInfo": {"hasNextPage": False}, "nodes": [{"__typename": "CheckRun", "name": "gate", "conclusion": "SUCCESS", "status": "COMPLETED", "completedAt": "2026-10-23T01:59:00Z"}]}}}
    write(data / "required.json", {"FS-GG/.github#1": {"oid": HEAD_A, **gate}, "FS-GG/Private.Repo#2": {"oid": HEAD_B, "error": "403", "statusCheckRollup": None}})
    tool(work, "reqcheck.py")
    req = json.loads((data / "reqcheck.json").read_text())
    check(req["FS-GG/.github#1"]["result"] == "pass" and req["FS-GG/Private.Repo#2"]["result"] == "unknown", "required checks: pass where readable, unknown (never pass) where not")

    out = tool(work, "review.py")
    raw = json.loads((data / "review_raw.json").read_text())
    check(bool(raw["FS-GG/.github#1"]["found"]) and not raw["FS-GG/Private.Repo#2"]["found"], "review finder proposes the member with a head-bound verdict only")
    check(raw["FS-GG/.github#1"]["verdict_lines"] == [{"source": "C7", "verdict": "APPROVE", "head": HEAD_A, "final_head": True}], "a final VERDICT line is recorded exactly and bound to the final head")
    check(raw["FS-GG/Private.Repo#2"]["verdict_lines"] == [{"source": "C8", "verdict": "REQUEST_CHANGES", "head": OLD_A, "final_head": False}], "a VERDICT line naming another head is not bound to the final head")
    write(data / "adjudication.json", {"V": {"FS-GG/.github#1": "APPROVE (C7)"}, "E": {}, "A": [], "NOTE": {}})

    claude = Path(tmp) / "claude"
    transcript = claude / "s1.jsonl"
    transcript.parent.mkdir(parents=True)
    lines = [{"type": "assistant", "sessionId": "S", "gitBranch": b, "message": {"id": m}} for m, b in (("m1", "routine/a"), ("m2", "main"), ("m3", "main"), ("m4", "routine/a"), ("m5", "routine/b"))]
    transcript.write_text("".join(json.dumps(x) + "\n" for x in lines), encoding="utf-8")
    ud = data / "usage"
    (ud / "claude").mkdir(parents=True)
    rows = [csv_row("2026-10-23T01:20:00.000Z", "S", "m1", 100), csv_row("2026-10-23T01:21:00.000Z", "S", "m2", 50), csv_row("2026-10-23T01:22:00.000Z", "S", "m3", 25),
            csv_row("2026-10-23T02:30:00.000Z", "S", "m4", 10), csv_row("2026-10-23T02:45:00.000Z", "S", "m5", 7)]
    (ud / "claude" / "aaaa.csv").write_text(CSV_HEAD + "\n" + "\n".join(rows) + "\n")
    # A resumed session copies m2 into a second transcript: it must be counted once.
    (ud / "claude" / "bbbb.csv").write_text(CSV_HEAD + "\n" + rows[1] + "\n")
    refused = "fsgg-coord-engine: telemetry usage: no Claude responses matched the requested turn/time window"
    (ud / "index.tsv").write_text(f"claude\taaaa\t0\t{transcript}\t\nclaude\tbbbb\t0\t{claude}/s2.jsonl\t\nclaude\tcccc\t1\t{claude}/subagents/s3.jsonl\t{refused}\n")
    (claude / "s2.jsonl").write_text(json.dumps({"type": "assistant", "sessionId": "S", "gitBranch": "main", "message": {"id": "m2"}}) + "\n")
    tool(work, "joinkeys.py")
    tool(work, "usage_summary.py")
    usage = json.loads((data / "usage_summary.json").read_text())
    check(usage["totals"]["claude"]["total"] == 192 and usage["dupes"] == 1, "usage totals deduplicate the copied response by provider response ID")
    check({a[1]: a[3]["total"] for a in usage["attributed"]} == {"FS-GG/.github#1": 100, "FS-GG/Private.Repo#2": 7}, "only branch-joined responses at or before the merge are attributed (a later response on the same branch is not)")

    write(data / "project3.json", {"total": 5, "fetched": 5, "closed": [{"content": {"__typename": "Issue", "number": 9, "repository": {"nameWithOwner": "FS-GG/.github"}, "closedAt": "2026-10-23T05:00:00Z"}, "fieldValueByName": {"name": "Done"}}]})
    write(data / "issues.json", [{"repo": "FS-GG/.github", "number": 9, "title": "t", "closedAt": "2026-10-23T05:00:00Z", "stateReason": "COMPLETED", "projectStatus": "Done", "timelineItems": {"nodes": []}}])
    tool(work, "assemble.py")
    census = json.loads((data / "census.json").read_text())
    a = census["actions"]
    check(a["pr_phase_runs"] == 2 and a["pr_phase_job_seconds"] == 600 + 300 + 200 and a["merge_commit_job_seconds"] == 100, "Actions totals sum job wall time over matched runs")
    check(a["jobs_with_completed_before_started_counted_as_zero"]["pr_phase"] == 1, "a job that ends before it starts counts as 0 s and is counted")
    check(a["force_pushed_head_pr_runs_not_in_totals"]["runs"] == 1 and a["force_pushed_head_pr_runs_not_in_totals"]["job_seconds"] == 50, "the force-pushed-head run is disclosed outside the totals")
    classes = {r["member"]: r["review_class"] for r in census["prs"]}
    check(classes["FS-GG/.github#1"] == "V" and classes["private-repository-member-1"] == "N", "review scoring follows the adjudication table")
    ev = Path(tmp) / "evidence.json"
    tool(work, "export.py", str(ev))
    md = Path(tmp) / "report.md"
    tool(work, "report.py", str(md))
    text = md.read_text() + ev.read_text()
    text += (data / "census.json").read_text()
    check(all(s not in text for s in ("Private.Repo", HEAD_B[:8], MERGE_B[:8], "title 2")), "the private member is redacted in the report, the evidence and the census document (attribution included)")
    check("private-repository-member-1" in json.dumps(census["usage"]["attributed"]), "a private member's token attribution is kept under its placeholder")
    capped = subprocess.run([sys.executable, str(TOOLS / "export.py"), str(Path(tmp) / "capped.json")], env=dict(os.environ, CENSUS_WORK=str(work), CENSUS_EVIDENCE_CAP="100"), capture_output=True, text=True)
    check(capped.returncode != 0 and not (Path(tmp) / "capped.json").exists(), "the evidence cap refuses an oversized document")
    inside = subprocess.run([sys.executable, str(TOOLS / "match.py")], env=dict(os.environ, CENSUS_WORK=str(ROOT)), capture_output=True, text=True)
    check(inside.returncode != 0 and "inside a git work tree" in inside.stderr + inside.stdout, "a work directory inside a repository is refused")
    check(len(ev.read_bytes()) < 65536 and json.loads(ev.read_text())["prs"]["rows"][0][0] == ".github#1", "evidence JSON is valid, tabular and under the 64 KiB cap")
    check("| .github#1 |" in md.read_text() and "private-repository-member-1" in md.read_text(), "the member tables are rendered, private members last")
    # The continuation template formats with --previous (here: this segment's own evidence stands in).
    md2 = Path(tmp) / "report2.md"
    tool(work, "report.py", str(md2), "--previous", str(ev), "--template", str(TOOLS / "continuation-report.example.md"))
    text2 = md2.read_text()
    check("## Coverage of the settled population" in text2 and "{n_" not in text2 and "{all_" not in text2, "the continuation template formats against the computed figures")
print("usage-census smoke: all checks passed")

#!/usr/bin/env python3
"""Allowlisted join-key extraction only: identities, branch, cwd. No message content is read into output.

The strict reader emits no branch or cwd. For Claude Code transcripts the join key of a response is the
`gitBranch` on its own record; for Codex rollouts only the session-start identity, branch and cwd are
recorded, which is not enough to attribute coordinator-run usage to a PR.
Only transcripts the reader accepted (exit code 0) are parsed.
"""
import json

from census_common import data_path, dump

index = [line.rstrip("\n").split("\t") for line in open(data_path("usage/index.tsv"), encoding="utf-8")]
out = {}
for runtime, fid, rc, path, err in index:
    if rc != "0":
        continue
    rec = {"runtime": runtime, "path_class": "subagent" if "/subagents/" in path else "main"}
    if runtime == "claude":
        msgs, sessions = {}, set()
        for line in open(path, encoding="utf-8"):
            try:
                o = json.loads(line)
            except Exception:
                continue
            if o.get("sessionId"):
                sessions.add(o["sessionId"])
            if o.get("type") == "assistant" and isinstance(o.get("message"), dict) and o["message"].get("id"):
                msgs[o["message"]["id"]] = {"branch": o.get("gitBranch"), "cwd": o.get("cwd"), "agent": o.get("agentId"), "side": o.get("isSidechain")}
        rec["sessions"] = sorted(sessions)
        rec["msgs"] = msgs
    else:
        turns = {}
        for line in open(path, encoding="utf-8"):
            o = json.loads(line)
            t = o.get("type")
            if t == "session_meta":
                p = o["payload"]
                rec["meta"] = {"id": p.get("id"), "cwd": p.get("cwd"), "branch": (p.get("git") or {}).get("branch"), "source": p.get("source") if isinstance(p.get("source"), str) else json.dumps(p.get("source"))[:80]}
            elif t == "turn_context":
                p = o["payload"]
                turns[p.get("turn_id")] = p.get("cwd")
        rec["turn_cwd"] = turns
    out[fid] = rec
dump("joinkeys.json", out)
print(len(out), "transcripts parsed")

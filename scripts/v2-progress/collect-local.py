#!/usr/bin/env python3
"""Read-only local Codex JSONL diagnostic input for the F# V2 renderer.

This does not authenticate account scope, emit orchestration-runner turns, or
produce an applied telemetry Host receipt. The F# CLI always treats it as a
LocalCounterDiagnostic and refuses to promote it to collector-verified usage.
"""

import argparse
import datetime as dt
import json
import math
import subprocess
from pathlib import Path

UTC = dt.timezone.utc


def stamp(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(UTC)


def collect(metadata, root_id, sessions_dir):
    cutoff = dt.datetime.now(UTC)
    metas = {}
    for path in sessions_dir.rglob("*.jsonl"):
        try:
            with path.open() as source:
                for line in source:
                    event = json.loads(line)
                    if event.get("type") == "session_meta":
                        payload = event["payload"]
                        origin = payload.get("source") or {}
                        parent = payload.get("parent_thread_id")
                        if not parent and isinstance(origin, dict):
                            parent = origin.get("parent_thread_id")
                        metas[payload["id"]] = (path, parent, stamp(event["timestamp"]))
                        break
        except (OSError, ValueError, KeyError):
            continue
    if root_id not in metas:
        raise ValueError("root session metadata is absent")
    family = {root_id}
    for _ in metas:
        expanded = family | {sid for sid, (_, parent, _) in metas.items() if parent in family}
        if expanded == family:
            break
        family = expanded
    started = metas[root_id][2]
    periods = math.floor((cutoff - started).total_seconds() / 600)
    if periods < 1:
        raise ValueError("no complete root-anchored ten-minute period")
    end = started + dt.timedelta(minutes=10 * periods)
    sessions = []
    for sid in sorted(family):
        path, parent, session_start = metas[sid]
        events = []
        with path.open() as source:
            for line in source:
                event = json.loads(line)
                payload = event.get("payload") or {}
                if event.get("type") != "event_msg" or payload.get("type") != "token_count":
                    continue
                usage = (payload.get("info") or {}).get("total_token_usage")
                if not usage:
                    continue
                observed = stamp(event["timestamp"])
                if observed > cutoff:
                    continue
                primary_rate = None
                if sid == root_id:
                    limits = payload.get("rate_limits") or {}
                    primary = limits.get("primary") or {}
                    if primary.get("used_percent") is not None and primary.get("resets_at") is not None:
                        primary_rate = {
                            "accountScopeId": "local-unverified-account-scope",
                            "limitId": str(limits.get("limit_id") or "unknown"),
                            "windowMinutes": 10080,
                            "usedPercent": primary["used_percent"],
                            "resetsAt": dt.datetime.fromtimestamp(primary["resets_at"], UTC).isoformat(),
                        }
                events.append({
                    "observedAt": observed.isoformat(), "ordinal": len(events),
                    "counters": {
                        "inputTokens": int(usage["input_tokens"]),
                        "cachedInputTokens": int(usage["cached_input_tokens"]),
                        "outputTokens": int(usage["output_tokens"]),
                        "totalTokens": int(usage["total_tokens"]),
                    },
                    "primaryRate": primary_rate,
                })
        sessions.append({
            "sessionId": sid, "parentSessionId": parent,
            "startedAt": session_start.isoformat(), "historyComplete": True,
            "evidenceId": "local-jsonl:" + str(path), "events": events,
        })
    result = dict(metadata)
    result["asOf"] = cutoff.isoformat()
    result["localCounterWindow"] = {
        "rootSessionId": root_id, "windowStart": (end - dt.timedelta(minutes=10)).isoformat(),
        "windowEnd": end.isoformat(), "declaredSessionCount": len(sessions),
        "sessions": sessions,
    }
    return result


def authenticated_readiness(metadata, repository):
    workspace = metadata["telemetry"]["workspaceId"]
    health = subprocess.run(
        ["fdev-telemetry", "health"], check=True, capture_output=True, text=True,
    )
    health_at = dt.datetime.now(UTC).isoformat()
    health_status = json.loads(health.stdout)
    board = subprocess.run(
        ["fdev-telemetry", "exec", "fsgg-coord-engine", "telemetry", "workspace",
         "status", "--workspace", workspace, "--repository", repository, "--json"],
        check=True, capture_output=True, text=True,
    )
    board_at = dt.datetime.now(UTC).isoformat()
    board_status = json.loads(board.stdout)
    if (health_status.get("status") != "ready"
            or board_status.get("status") != "configured"
            or board_status.get("workspaceId") != workspace
            or board_status.get("pending") != 0
            or board_status.get("pendingUnacknowledged") != 0
            or board_status.get("unacknowledgedLossy") is not False):
        raise ValueError("authenticated telemetry readiness is not ready/configured with a zero lossless queue")
    metadata["telemetry"] = {
        "workspaceId": workspace,
        "health": {"authenticated": True, "ready": True, "collectorVerified": True,
                   "observedAt": health_at,
                   "evidenceId": "direct authenticated fdev-telemetry health at " + health_at},
        "workspace": {"configured": True, "collectorVerified": True,
                      "observedAt": board_at,
                      "evidenceId": "direct authenticated fdev-telemetry workspace status at " + board_at,
                      "pending": 0, "pendingUnacknowledged": 0, "unacknowledgedLossy": False},
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--root-session", required=True)
    parser.add_argument("--repository", required=True)
    parser.add_argument("--sessions-dir", type=Path, default=Path.home() / ".codex/sessions")
    args = parser.parse_args()
    metadata = json.loads(args.metadata.read_text())
    authenticated_readiness(metadata, args.repository)
    report = collect(metadata, args.root_session, args.sessions_dir)
    args.output.write_text(json.dumps(report, separators=(",", ":"), sort_keys=True) + "\n")
    print(f"local diagnostic snapshot: {len(report['localCounterWindow']['sessions'])} family sessions; {sum(len(s['events']) for s in report['localCounterWindow']['sessions'])} token_count events; no Host receipt")


if __name__ == "__main__":
    main()

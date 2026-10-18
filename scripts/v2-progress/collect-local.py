#!/usr/bin/env python3
"""Bounded local token_count diagnostics, independent of receiver availability.

These cumulative samples are not fully attributed native usage or Host receipts.
Modern token_usage_record capture uses the maintained native reader separately.
"""
import argparse
import datetime as dt
import hashlib
import json
import math
from pathlib import Path

UTC = dt.timezone.utc
MAX_FILES = 512
MAX_SCAN_BYTES = 64 * 1024 * 1024
MAX_FILE_BYTES = 8 * 1024 * 1024
MAX_LINE_BYTES = 1024 * 1024
MAX_EVENTS = 10000
MAX_OUTPUT_BYTES = 8 * 1024 * 1024


def stamp(value):
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.utcoffset() is None:
        raise ValueError("timestamp lacks offset")
    return parsed.astimezone(UTC)


def collect(metadata, root_id, sessions_dir):
    cutoff = dt.datetime.now(UTC)
    gaps = set()
    metas = {}
    remaining = MAX_SCAN_BYTES
    try:
        paths = []
        for path in sessions_dir.rglob("*.jsonl"):
            if len(paths) >= MAX_FILES:
                gaps.add("session file population exceeded scan bound")
                break
            paths.append(path)
    except OSError:
        paths = []
        gaps.add("session file inventory unavailable")
    for path in sorted(paths):
        try:
            before = path.stat()
            limit = min(MAX_FILE_BYTES, remaining)
            with path.open("rb") as source:
                raw = source.read(limit + 1)
            remaining -= min(len(raw), limit)
            if len(raw) > limit:
                gaps.add("session byte bound reached")
                raw = raw[:limit]
            after = path.stat()
            complete = (len(raw) == before.st_size and before.st_ino == after.st_ino
                        and before.st_size == after.st_size and before.st_mtime_ns == after.st_mtime_ns)
            if not complete:
                gaps.add("session changed, rotated or exceeded byte bound")
            if raw and not raw.endswith(b"\n"):
                complete = False
                gaps.add("partial final session line")
                raw = raw.rsplit(b"\n", 1)[0] if b"\n" in raw else b""
            identity = None
            events = []
            for line in raw.splitlines():
                try:
                    if len(line) > MAX_LINE_BYTES:
                        raise ValueError("line bound")
                    event = json.loads(line)
                    payload = event.get("payload") or {}
                    if event.get("type") == "session_meta":
                        if identity is not None:
                            raise ValueError("duplicate session identity")
                        origin = payload.get("source") or {}
                        parent = payload.get("parent_thread_id")
                        if not parent and isinstance(origin, dict):
                            parent = origin.get("parent_thread_id")
                        sid = payload["id"]
                        if (not isinstance(sid, str) or not 1 <= len(sid) <= 128
                                or (parent is not None and (not isinstance(parent, str) or not 1 <= len(parent) <= 128))):
                            raise ValueError("bounded session identity")
                        identity = (sid, parent, stamp(event["timestamp"]))
                    if event.get("type") != "event_msg" or payload.get("type") != "token_count":
                        continue
                    usage = (payload.get("info") or {}).get("total_token_usage")
                    if not usage:
                        continue
                    observed = stamp(event["timestamp"])
                    if observed > cutoff:
                        continue
                    counters = {name: usage[key] for name, key in [
                        ("inputTokens", "input_tokens"), ("cachedInputTokens", "cached_input_tokens"),
                        ("outputTokens", "output_tokens"), ("totalTokens", "total_tokens")]}
                    if any(type(value) is not int or not 0 <= value <= 2**63 - 1 for value in counters.values()):
                        raise ValueError("invalid counters")
                    if (counters["cachedInputTokens"] > counters["inputTokens"]
                            or counters["totalTokens"] != counters["inputTokens"] + counters["outputTokens"]):
                        raise ValueError("counter components disagree")
                    if len(events) >= MAX_EVENTS:
                        raise ValueError("event bound")
                    events.append({"observedAt": observed.isoformat(), "ordinal": len(events),
                                   "counters": counters, "primaryRate": None})
                except (ValueError, KeyError, TypeError, AttributeError, OverflowError):
                    complete = False
                    gaps.add("malformed or bounded session record")
            if identity is None:
                gaps.add("session metadata unavailable; family population unproved")
                continue
            sid, parent, started = identity
            if sid in metas:
                gaps.add("duplicate or rotated session identity")
                complete = False
            metas[sid] = (parent, started, complete, events)
        except (OSError, ValueError, TypeError):
            gaps.add("session source unreadable")
        if remaining <= 0:
            gaps.add("aggregate scan byte bound reached")
            break
    family = {root_id} if root_id in metas else set()
    for _ in metas:
        expanded = family | {sid for sid, (parent, _, _, _) in metas.items() if parent in family}
        if expanded == family:
            break
        family = expanded
    if not family:
        gaps.add("root session metadata unavailable")
    elif metas[root_id][0] is not None:
        gaps.add("selected root has an unresolved parent")
    for parent, _, _, _ in metas.values():
        if parent is not None and parent not in metas:
            gaps.add("session parent population unavailable")
    sessions = []
    for sid in sorted(family):
        parent, started, complete, events = metas[sid]
        # A zero baseline is accepted only when actually observed, never invented.
        if (not events or any(events[0]["counters"].values())
                or stamp(events[0]["observedAt"]) != started):
            gaps.add("authentic zero baseline unavailable")
            complete = False
        for previous, current in zip(events, events[1:]):
            if (stamp(previous["observedAt"]) > stamp(current["observedAt"])
                    or any(current["counters"][key] < value for key, value in previous["counters"].items())
                    or current["counters"]["cachedInputTokens"] - previous["counters"]["cachedInputTokens"]
                       > current["counters"]["inputTokens"] - previous["counters"]["inputTokens"]):
                gaps.add("cumulative counter reset or time order gap")
                complete = False
        if started > cutoff or (family and started < metas[root_id][1]):
            gaps.add("selected session time or lineage unavailable")
            complete = False
        if any(stamp(event["observedAt"]) < started for event in events):
            gaps.add("counter observation precedes session")
            complete = False
        if not complete:
            gaps.add("selected session history incomplete")
        sessions.append({"sessionId": sid, "parentSessionId": parent,
                         "startedAt": started.isoformat(), "historyComplete": complete,
                         "evidenceId": "local-session:" + hashlib.sha256(sid.encode()).hexdigest(),
                         "events": events})
    window = None
    if family:
        started = metas[root_id][1]
        periods = math.floor((cutoff - started).total_seconds() / 600)
        if periods < 1:
            gaps.add("no complete root-anchored ten-minute diagnostic period")
        elif not gaps:
            end = started + dt.timedelta(minutes=10 * periods)
            window = {"rootSessionId": root_id, "windowStart": (end - dt.timedelta(minutes=10)).isoformat(),
                      "windowEnd": end.isoformat(), "declaredSessionCount": len(sessions), "sessions": sessions}
    result = dict(metadata)
    result["asOf"] = cutoff.isoformat()
    # Never reuse caller-supplied health or queue claims as newly observed facts.
    result["telemetry"] = {"workspaceId": metadata["telemetry"]["workspaceId"],
                           "health": None, "workspace": None}
    result["localCounterWindow"] = window
    observed = sum(len(session["events"]) for session in sessions)
    samples = []
    for session in sessions[:8]:
        if session["events"]:
            event = session["events"][-1]
            c = event["counters"]
            samples.append(f"latest cumulative input={c['inputTokens']}, cached={c['cachedInputTokens']}, "
                           f"output={c['outputTokens']}, total={c['totalTokens']} at {event['observedAt']}")
    detail = (f"Observed {observed} cumulative token_count samples in {len(sessions)} local family sessions; "
              "samples are not summed, item attribution incomplete, receiver delivery unknown. "
              + "; ".join(samples) + ". " + "; ".join(sorted(gaps)))
    result["risks"] = [risk for risk in result.get("risks", []) if risk["name"] != "Local counter coverage"] + [
        {"name": "Local counter coverage", "state": "unknown" if gaps else "pending", "detail": detail}]
    result["localCounterCoverage"] = {"observedSamples": observed, "familySessions": len(sessions),
                                      "gaps": sorted(gaps), "completeDiagnostic": window is not None}
    if len(json.dumps(result).encode("utf-8")) > MAX_OUTPUT_BYTES:
        result["localCounterWindow"] = None
        result["localCounterCoverage"]["completeDiagnostic"] = False
        result["localCounterCoverage"]["gaps"].append("diagnostic output byte bound reached")
        result["risks"][-1]["state"] = "unknown"
        result["risks"][-1]["detail"] += "; diagnostic output byte bound reached"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--root-session", required=True)
    parser.add_argument("--repository", required=True)  # Kept by the maintained wrapper; no remote IO.
    parser.add_argument("--sessions-dir", type=Path, default=Path.home() / ".codex/sessions")
    args = parser.parse_args()
    if args.metadata.stat().st_size > 1024 * 1024:
        raise ValueError("metadata exceeds 1 MiB")
    report = collect(json.loads(args.metadata.read_text()), args.root_session, args.sessions_dir)
    with args.output.open("w") as output:
        json.dump(report, output, separators=(",", ":"), sort_keys=True)
        output.write("\n")
    print(f"local diagnostic snapshot: {report['localCounterCoverage']['observedSamples']} cumulative samples; "
          "attribution incomplete; delivery unknown; no Host receipt")


if __name__ == "__main__":
    main()

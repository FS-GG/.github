#!/usr/bin/env python3
"""Synthetic read-only Codex App Server fixture; contains no conversation content."""

import json
import os
import sys
import time

PARENT = "11111111-1111-1111-1111-111111111111"
THREAD = "22222222-2222-2222-2222-222222222222"
TURN_1 = "33333333-3333-3333-3333-333333333333"
TURN_2 = "44444444-4444-4444-4444-444444444444"


def send(request_id, result, final=False):
    mode = os.environ.get("SKILL_FS_01_WIRE_MODE", "lf")
    terminator = b"" if final and mode == "eof-final" else b"\r\n" if mode == "crlf" else b"\n"
    payload = json.dumps({"id": request_id, "result": result}, separators=(",", ":")).encode("utf-8")
    if final and mode == "oversize":
        payload += b" " * (1024 * 1024)
    sys.stdout.buffer.write(payload + terminator)
    sys.stdout.buffer.flush()


def main():
    if sys.argv[1:] != ["app-server"]:
        return 2
    mode = os.environ.get("SKILL_FS_01_MODE", "complete")
    for line in sys.stdin:
        request = json.loads(line)
        if "id" not in request:
            continue
        request_id = request["id"]
        method = request["method"]
        params = request.get("params", {})
        if mode == "timeout" and method == "thread/list":
            time.sleep(10)
            continue
        if method == "initialize":
            send(request_id, {})
        elif method == "thread/list":
            send(request_id, {"data": [{"id": THREAD}], "nextCursor": "loop" if mode == "cycle" else None})
        elif method == "thread/read":
            thread = {
                "id": THREAD, "parentThreadId": PARENT,
                "path": os.environ["SKILL_FS_01_ROLLOUT"],
                "source": {"subAgent": {"thread_spawn": {
                    "parent_thread_id": PARENT, "agent_path": "/fixture/child_1"
                }}},
                "modelProvider": "openai", "model": "fixture-model", "reasoningEffort": "medium"
            }
            if mode == "missing-profile":
                thread.pop("modelProvider")
            send(request_id, {"thread": thread})
        elif method == "thread/turns/list":
            send(request_id, {"data": [
                {"id": TURN_1, "status": "completed"},
                {"id": TURN_2, "status": "failed"}
            ], "nextCursor": None}, final=True)
            if os.environ.get("SKILL_FS_01_WIRE_MODE") == "eof-final":
                return 0
        else:
            print(json.dumps({"id": request_id, "error": {"message": "unsupported"}}), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Offline gh fixture for pr-lane-admission tests."""

import json
import os
import pathlib
import sys


state_path = pathlib.Path(os.environ["FAKE_GH_STATE"])
state = json.loads(state_path.read_text(encoding="utf-8"))
arguments = sys.argv[1:]
stdin = sys.stdin.read()

with pathlib.Path(os.environ["FAKE_GH_LOG"]).open("a", encoding="utf-8") as log:
    log.write(json.dumps({"arguments": arguments, "stdin": stdin}) + "\n")

endpoint = arguments[-1] if arguments[-1] != "-" else arguments[-3]
method = arguments[arguments.index("--method") + 1]
if method == "GET" and endpoint.endswith("pulls?state=open&per_page=100"):
    sequence = state.get("open_read_sequence")
    value = sequence.pop(0) if sequence else state.get("open_pages", [[]])
    state_path.write_text(json.dumps(state))
    print(json.dumps(value))
elif method == "GET" and "/pulls/" in endpoint:
    if state.get("patched") and state.get("fail_read_after_patch"):
        raise SystemExit(8)
    sequence = state.get("read_sequence")
    value = sequence.pop(0) if sequence else state["pull"]
    state_path.write_text(json.dumps(state))
    print(json.dumps(value))
elif method == "PATCH" and "/pulls/" in endpoint:
    if state.get("patch_fail_before"):
        raise SystemExit(7)
    state["pull"].update(json.loads(stdin))
    state["patched"] = True
    state_path.write_text(json.dumps(state))
    if state.get("patch_fail_after"):
        raise SystemExit(7)
    print(json.dumps(state["pull"]))
elif method == "GET" and "/git/ref/heads/" in endpoint:
    print(json.dumps({"object": {"sha": state["head_sha"]}}))
elif method == "POST" and endpoint.endswith("/pulls"):
    print(json.dumps(state.get("created", {"number": 42, "html_url": "https://example.test/pull/42"})))
else:
    print("unexpected fake gh invocation", file=sys.stderr)
    raise SystemExit(9)

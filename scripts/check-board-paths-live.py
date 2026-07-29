#!/usr/bin/env python3
"""Report retired runtime-root tokens in open issue ``Paths:`` declarations (#1881).

This is intentionally not a generic filesystem-liveness check: a declared path may be a planned
new file.  Only roots explicitly retired by the runtime contract are stale declarations.
"""
from __future__ import annotations
import argparse
import json
import re
import sys

RETIRED_ROOTS = (".codex/skills",)
PATHS = re.compile(r"(?m)^\s{0,3}Paths:\s*(.+?)\s*$")

def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--items", required=True, help="JSON [{number, state, body}] from the board reader")
    args = ap.parse_args()
    try:
        items = json.load(open(args.items, encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as e:
        print(f"::error::board-paths-live: cannot read board items: {e}", file=sys.stderr); return 2
    if not isinstance(items, list):
        print("::error::board-paths-live: board items are not a list", file=sys.stderr); return 2
    reports = 0
    for item in items:
        if not isinstance(item, dict) or item.get("state") != "OPEN" or not isinstance(item.get("body"), str):
            continue
        match = PATHS.search(item["body"])
        if not match: continue
        for token in re.split(r"[\s,]+", match.group(1)):
            token = token.rstrip("/")
            if any(token == root or token.startswith(root + "/") for root in RETIRED_ROOTS):
                print(f"::warning::board-paths-live: #{item.get('number')} declares retired root token {token}")
                reports += 1
    print(f"board-paths-live: {reports} retired-root token(s) reported; planned new paths are allowed")
    return 0

if __name__ == "__main__": raise SystemExit(main())

#!/usr/bin/env python3
"""Synthetic local store engine for the compiled CLI process test."""
import base64
import os
from pathlib import Path
import sys


def main():
    args = sys.argv[1:]
    if "--input" in args:
        source = Path(args[args.index("--input") + 1])
        log = Path(os.environ["SKILL_FS_01_CLI_LOG"])
        with log.open("a", encoding="ascii") as stream:
            stream.write(base64.b64encode(source.read_bytes()).decode("ascii") + "\n")
    if "publish" in args:
        marker = os.environ.get("SKILL_FS_01_CLI_FAIL_ONCE")
        if marker and not Path(marker).exists():
            Path(marker).write_text("failed", encoding="ascii")
            print("delivery outcome unknown", file=sys.stderr)
            return 1
        return 0
    if "drain" in args or "status" in args:
        return 0
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

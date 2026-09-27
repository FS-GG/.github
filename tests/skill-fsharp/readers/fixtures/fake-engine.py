#!/usr/bin/env python3
"""Read-only workspace binding/status fixture."""

import json
import os
import sys


def main():
    args = sys.argv[1:]
    if args[:3] == ["telemetry", "workspace", "binding"]:
        config = args[args.index("--config") + 1]
        repository = args[args.index("--repository") + 1]
        print(json.dumps({
            "schema": "fsgg.telemetry.workspace-binding/1",
            "configPath": config,
            "repository": repository,
            "producerId": "fixture-producer",
            "bindingDigest": "fixture-binding-digest",
            "destination": "fixture-destination",
            "privateStateRoot": os.environ["SKILL_FS_01_STATE_ROOT"],
        }, separators=(",", ":")))
        return 0
    if args[:3] == ["telemetry", "workspace", "status"]:
        return 0
    return 2


if __name__ == "__main__":
    raise SystemExit(main())

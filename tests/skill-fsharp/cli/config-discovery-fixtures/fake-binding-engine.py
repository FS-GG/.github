#!/usr/bin/env python3
"""Synthetic read-only workspace binding provider."""

import json
import os
import sys


if os.environ.get("CONFIG_DISCOVERY_BINDING_FAILURE"):
    print("binding refused: synthetic-secret-value", file=sys.stderr)
    raise SystemExit(9)

if len(sys.argv) != 8 or sys.argv[1:5] != ["telemetry", "workspace", "binding", "--config"]:
    raise SystemExit(8)

config_path = sys.argv[5]
if sys.argv[6] != "--repository":
    raise SystemExit(8)

repository = sys.argv[7]
print(json.dumps({
    "schema": "fsgg.telemetry.workspace-binding/1",
    "configPath": config_path,
    "repository": repository,
    "producerId": "fixture-producer",
    "bindingDigest": "fixture-binding-digest",
    "destination": "remote",
    "privateStateRoot": os.environ["CONFIG_DISCOVERY_STATE_ROOT"],
}, separators=(",", ":")))

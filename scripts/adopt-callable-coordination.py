#!/usr/bin/env python3
"""Opt in or out of the pinned callable Coordination tool without touching peers."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import tempfile


PACKAGE_KEY = "fs.gg.coordination.cli"
PIN = {
    "version": "0.1.0",
    "commands": ["fsgg-coordination"],
    "rollForward": False,
}


def read_manifest(path: Path) -> dict:
    if not path.exists():
        return {"version": 1, "isRoot": True, "tools": {}}
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict) or not isinstance(value.get("tools"), dict):
        raise ValueError("manifest must be an object with a tools object")
    return value


def write_atomic(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    encoded = (json.dumps(value, indent=2, ensure_ascii=False) + "\n").encode()
    descriptor, temporary = tempfile.mkstemp(prefix=f".{path.name}.", dir=path.parent)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            stream.write(encoded)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
        directory = os.open(path.parent, os.O_RDONLY)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def install(path: Path) -> str:
    manifest = read_manifest(path)
    current = manifest["tools"].get(PACKAGE_KEY)
    if current == PIN:
        return "already-installed"
    if current is not None:
        raise ValueError(f"conflicting {PACKAGE_KEY} entry; no changes written")
    manifest["tools"][PACKAGE_KEY] = PIN
    write_atomic(path, manifest)
    return "installed"


def uninstall(path: Path) -> str:
    manifest = read_manifest(path)
    current = manifest["tools"].get(PACKAGE_KEY)
    if current is None:
        return "already-absent"
    if current != PIN:
        raise ValueError(f"conflicting {PACKAGE_KEY} entry; no changes written")
    del manifest["tools"][PACKAGE_KEY]
    write_atomic(path, manifest)
    return "uninstalled"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("operation", choices=("install", "uninstall"))
    parser.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = install(args.manifest) if args.operation == "install" else uninstall(args.manifest)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        parser.exit(3, f"callable-coordination-adoption: refused: {error}\n")
    print(f"callable-coordination-adoption: {result}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Generate and verify the Wizard's pinned coordination kit (FS-GG/.github#4964).

The Wizard vendors the coordination kit from FS-GG/.github. It names one reviewed commit and three
sha256 anchors in scripts/NewSddWorkspace/coordination-kit-pin.json, embedded in the tool:

  * the generated skill manifest (registry/coordination-kit-skill-manifest.json), whose rows carry the
    sha256 of every skill file, so verifying it pins the whole closed skill set transitively;
  * the fsgg-coord shim (scripts/fsgg-coord);
  * the engine tool manifest (dist/dotnet/.config/dotnet-tools.json).

`write REVISION` records the anchors from that commit; `check` recomputes them from the git object
store and requires every manifest row to equal its blob, so a pin edit cannot drift from the commit
it names. Both need the revision's objects (CI uses a full-history checkout).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
PIN = ROOT / "scripts/NewSddWorkspace/coordination-kit-pin.json"
SCHEMA = "fsgg.new-sdd-workspace-coordination-kit-pin/1"
REPOSITORY = "FS-GG/.github"
MANIFEST = "registry/coordination-kit-skill-manifest.json"
SHIM = "scripts/fsgg-coord"
TOOLS = "dist/dotnet/.config/dotnet-tools.json"
ANCHORS = (MANIFEST, SHIM, TOOLS)


def fail(message: str) -> None:
    raise SystemExit(f"coordination kit pin: {message}")


def blob(revision: str, path: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), "show", f"{revision}:{path}"], timeout=30)


def strict_text(body: bytes, path: str) -> None:
    if body.startswith(b"\xef\xbb\xbf"):
        fail(f"{path} carries a UTF-8 BOM; the Wizard digests the served bytes and writes UTF-8 text")
    try:
        body.decode("utf-8")
    except UnicodeDecodeError:
        fail(f"{path} is not valid UTF-8")


def build(revision: str) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        fail("revision must be a full lowercase 40-hex commit")
    anchors = {}
    for path in ANCHORS:
        body = blob(revision, path)
        strict_text(body, path)
        anchors[path] = hashlib.sha256(body).hexdigest()
    return {"schema": SCHEMA, "repository": REPOSITORY, "revision": revision, "anchors": anchors}


def check_manifest_rows(revision: str) -> int:
    manifest = json.loads(blob(revision, MANIFEST))
    if manifest.get("schemaVersion") != 2:
        fail("the skill manifest is not schemaVersion 2")
    count = 0
    for skill in manifest["skills"]:
        for row in skill["files"]:
            path = f".claude/skills/{skill['id']}/{row['path']}"
            body = blob(revision, path)
            strict_text(body, path)
            if hashlib.sha256(body).hexdigest() != row["sha256"]:
                fail(f"{path} differs from its manifest digest at {revision}")
            count += 1
    return count


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    write = sub.add_parser("write")
    write.add_argument("revision")
    sub.add_parser("check")
    args = parser.parse_args()
    if args.command == "write":
        pin = build(args.revision)
        check_manifest_rows(args.revision)
        PIN.write_text(json.dumps(pin, indent=2) + "\n")
        print(f"wrote {PIN.relative_to(ROOT)} at {args.revision}")
        return 0
    pin = json.loads(PIN.read_text())
    if set(pin) != {"schema", "repository", "revision", "anchors"} or pin["schema"] != SCHEMA or pin["repository"] != REPOSITORY:
        fail("pin document shape differs from the closed schema")
    if PIN.read_text() != json.dumps(pin, indent=2) + "\n":
        fail("pin document is not in canonical form")
    expected = build(pin["revision"])
    if pin != expected:
        fail("pinned anchors differ from the blobs at the pinned revision")
    rows = check_manifest_rows(pin["revision"])
    print(json.dumps({"revision": pin["revision"], "anchors": pin["anchors"], "manifestRowsVerified": rows}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

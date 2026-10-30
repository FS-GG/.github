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

EMBEDDED SKILLS. The Wizard embeds some skills (the fsproj `EmbeddedResource` entries under
`.claude/skills/<id>/`). An embedded skill is authoritative in the tool and is listed in the pin's
`embeddedSkills`, which excludes it from kit vendoring and reconcile. `check` requires, for every skill the
Wizard embeds, that it is either excluded that way or byte-equal to the pinned manifest row, and that every
excluded skill really is embedded and really is a row of the pinned manifest. Pin and embedded copies
therefore cannot silently disagree (the pin names a pre-merge commit, so an embedded skill that changes in
the same PR is necessarily excluded rather than re-pinned).
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
FSPROJ = ROOT / "scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
DEFAULT_EMBEDDED = ["initialize-sdd-workspace"]


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


def embedded_skill_files() -> dict[str, dict[str, pathlib.Path]]:
    """Skills the Wizard embeds, from the fsproj: {id: {file-relative-path: source path}}."""
    found: dict[str, dict[str, pathlib.Path]] = {}
    for include in re.findall(r'<EmbeddedResource\s+Include="([^"]+)"', FSPROJ.read_text()):
        match = re.fullmatch(r"\.\./\.\./\.claude/skills/([^/]+)/(.+)", include)
        if match:
            found.setdefault(match.group(1), {})[match.group(2)] = (ROOT / ".claude/skills" / match.group(1) / match.group(2))
    return found


def check_embedded(revision: str, excluded: list[str]) -> int:
    embedded = embedded_skill_files()
    manifest = json.loads(blob(revision, MANIFEST))
    rows = {skill["id"]: {row["path"]: row["sha256"] for row in skill["files"]} for skill in manifest["skills"]}
    if sorted(excluded) != excluded or len(set(excluded)) != len(excluded):
        fail("embeddedSkills must be sorted and unique")
    for skill in excluded:
        if skill not in embedded:
            fail(f"{skill} is excluded from the kit but the Wizard does not embed it")
        if skill not in rows:
            fail(f"{skill} is excluded from the kit but is not a row of the pinned manifest")
    for skill, files in embedded.items():
        if skill in excluded:
            continue
        pinned = rows.get(skill)
        if pinned is None:
            continue  # not a kit skill: nothing to disagree with
        for rel, path in files.items():
            if pinned.get(rel) != hashlib.sha256(path.read_bytes()).hexdigest():
                fail(f"embedded skill {skill}/{rel} differs from the pinned kit row and is not excluded via embeddedSkills")
    return len(embedded)


def build(revision: str, embedded: list[str]) -> dict:
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        fail("revision must be a full lowercase 40-hex commit")
    anchors = {}
    for path in ANCHORS:
        body = blob(revision, path)
        strict_text(body, path)
        anchors[path] = hashlib.sha256(body).hexdigest()
    return {"schema": SCHEMA, "repository": REPOSITORY, "revision": revision, "anchors": anchors, "embeddedSkills": embedded}


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
    check = sub.add_parser("check")
    check.add_argument("--pin", type=pathlib.Path, default=PIN, help="pin document to check (default: the embedded one)")
    args = parser.parse_args()
    if args.command == "write":
        previous = json.loads(PIN.read_text()).get("embeddedSkills", DEFAULT_EMBEDDED) if PIN.exists() else DEFAULT_EMBEDDED
        pin = build(args.revision, previous)
        check_manifest_rows(args.revision)
        check_embedded(args.revision, previous)
        PIN.write_text(json.dumps(pin, indent=2) + "\n")
        print(f"wrote {PIN.relative_to(ROOT)} at {args.revision}")
        return 0
    pin_path = args.pin
    pin = json.loads(pin_path.read_text())
    if set(pin) != {"schema", "repository", "revision", "anchors", "embeddedSkills"} or pin["schema"] != SCHEMA or pin["repository"] != REPOSITORY:
        fail("pin document shape differs from the closed schema")
    if pin_path.read_text() != json.dumps(pin, indent=2) + "\n":
        fail("pin document is not in canonical form")
    expected = build(pin["revision"], pin["embeddedSkills"])
    if pin != expected:
        fail("pinned anchors differ from the blobs at the pinned revision")
    rows = check_manifest_rows(pin["revision"])
    embedded = check_embedded(pin["revision"], pin["embeddedSkills"])
    print(json.dumps({"revision": pin["revision"], "anchors": pin["anchors"], "embeddedSkills": pin["embeddedSkills"],
                      "embeddedSkillsChecked": embedded, "manifestRowsVerified": rows}, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())

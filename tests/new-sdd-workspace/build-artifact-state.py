#!/usr/bin/env python3
"""Read-only identity of caller artifacts that a source fixture must preserve."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import stat

ROOT = Path(__file__).resolve().parents[2]


def file_identity(path):
    info = path.lstat()
    result = {"mode": stat.S_IMODE(info.st_mode), "size": info.st_size, "mtimeNs": info.st_mtime_ns}
    if path.is_symlink():
        result["linkSha256"] = hashlib.sha256(str(path.readlink()).encode()).hexdigest()
    elif path.is_file():
        digest = hashlib.sha256()
        with path.open("rb") as source:
            for block in iter(lambda: source.read(65536), b""):
                digest.update(block)
        result["sha256"] = digest.hexdigest()
    return result


def capture(root, caller):
    artifacts = {}
    projects = {p.parent for p in root.glob("src/**/*.fsproj")} | {p.parent for p in root.glob("scripts/NewSddWorkspace/*.fsproj")}
    projects |= {root / f"src/FS.GG.Coord.{name}" for name in ("Cli", "Core", "GitHub")}
    for project in sorted(projects):
        for kind in ("bin", "obj"):
            directory = project / kind
            key = str(directory.relative_to(root))
            artifacts[key] = None if not directory.exists() else file_identity(directory)
            if directory.is_dir() and not directory.is_symlink():
                for path in sorted(directory.rglob("*")):
                    artifacts[str(path.relative_to(root))] = file_identity(path)
    selected = None
    if caller is not None:
        selected = {"entry": file_identity(caller),
                    "resolvedPathSha256": hashlib.sha256(str(caller.resolve()).encode()).hexdigest(),
                    "payload": file_identity(caller.resolve())}
    return {"schema": "fsgg.creator-caller-artifact-state/1", "artifacts": artifacts, "selectedCaller": selected}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--root", type=Path, default=ROOT)
    args = parser.parse_args()
    found = shutil.which("fsgg-coord-engine")
    args.output.write_text(json.dumps(capture(args.root, Path(found) if found else None), sort_keys=True) + "\n")

#!/usr/bin/env python3
"""Import and verify the protected GS2-08.5 Coordination source subset."""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import subprocess
import sys


SOURCE_COMMIT = "48fa43e67de52d4e728a9abff30686fc029d1d8d"
SOURCE_TREE = "484e6c53f9f474bfedfd10f22ac301e3227478cd"
SOURCE_PREFIX = "src/FS.GG.Coordination.GitHub"
DESTINATION = pathlib.Path("src/FS.GG.Coord.GitHub/Generated/Coordination")
FILES = (
    ("ShardedJournalAdapter.fsi", "790f6b3d6d787a0e613f4eacd97dceee79cdca17518a365d5c2f981df8f1136b"),
    ("ShardedJournalAdapter.fs", "f0b6a5854c208c4cc88cccf003516d80d781ae5c4140ae9c123dc82942a17e09"),
    ("V1EffectFenceAdapter.fsi", "281c87ec29e69268ce8bacefe7167e814114c9b2a36f0f21564cbaf8df3d158a"),
    ("V1EffectFenceAdapter.fs", "1e0bb9ae31dfe2a72ee34abdb25b3b7c79401197766486fc00dff60501bc9dfd"),
    ("V1AdmissionRegistry.fsi", "7e4736da1efb91cd5ffcd176d1e72ddd561048f8b4b8d8c034a4071531ccafb2"),
    ("V1AdmissionRegistry.fs", "5da24c4ea07f8a9d8934bdb6f716662c4c412f641369dc9ef6250f21cecaa081"),
)


def fail(message: str) -> None:
    raise SystemExit(f"v1-admission import refused: {message}")


def digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def git(source: pathlib.Path, *args: str) -> bytes:
    completed = subprocess.run(
        ["git", "-C", str(source), *args],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(completed.stderr.decode("utf-8", errors="replace").strip())
    return completed.stdout


def manifest() -> dict[str, object]:
    return {
        "schema": "fsgg.github.v1-admission-source-subset/1",
        "unit": "GS2-08.5",
        "sourceRepository": "FS-GG/FS.GG.Coordination",
        "sourceCommit": SOURCE_COMMIT,
        "sourceTree": SOURCE_TREE,
        "importMode": "exact-byte-copy",
        "compilationOrder": [name for name, _ in FILES],
        "files": [
            {
                "sourcePath": f"{SOURCE_PREFIX}/{name}",
                "destinationPath": (DESTINATION / name).as_posix(),
                "sha256": expected,
            }
            for name, expected in FILES
        ],
    }


def canonical_manifest_bytes() -> bytes:
    return (json.dumps(manifest(), indent=2, ensure_ascii=False) + "\n").encode("utf-8")


def verify_destination(root: pathlib.Path) -> None:
    expected_manifest = canonical_manifest_bytes()
    manifest_path = root / DESTINATION / "source-subset.json"
    if not manifest_path.is_file() or manifest_path.read_bytes() != expected_manifest:
        fail(f"{manifest_path.relative_to(root)} is missing or differs from the pinned manifest")

    for name, expected in FILES:
        path = root / DESTINATION / name
        if not path.is_file():
            fail(f"missing imported file {path.relative_to(root)}")
        actual = digest(path.read_bytes())
        if actual != expected:
            fail(f"{path.relative_to(root)} sha256 {actual} != {expected}")


def import_from(root: pathlib.Path, source: pathlib.Path) -> None:
    source = source.resolve()
    if not (source / ".git").exists():
        # Worktrees have a .git file, while ordinary checkouts have a directory.
        if not (source / ".git").is_file():
            fail(f"source is not a Git checkout: {source}")

    commit = git(source, "rev-parse", f"{SOURCE_COMMIT}^{{commit}}").decode().strip()
    tree = git(source, "show", "-s", "--format=%T", SOURCE_COMMIT).decode().strip()
    if commit != SOURCE_COMMIT or tree != SOURCE_TREE:
        fail(f"source identity differs: commit={commit} tree={tree}")

    imported: list[tuple[pathlib.Path, bytes]] = []
    for name, expected in FILES:
        value = git(source, "show", f"{SOURCE_COMMIT}:{SOURCE_PREFIX}/{name}")
        actual = digest(value)
        if actual != expected:
            fail(f"protected source {name} sha256 {actual} != {expected}")
        imported.append((root / DESTINATION / name, value))

    (root / DESTINATION).mkdir(parents=True, exist_ok=True)
    for path, value in imported:
        path.write_bytes(value)
    (root / DESTINATION / "source-subset.json").write_bytes(canonical_manifest_bytes())
    verify_destination(root)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--source", type=pathlib.Path, help="FS.GG.Coordination checkout used for the exact import")
    mode.add_argument("--check", action="store_true", help="verify the checked-in subset without reading a source checkout")
    mode.add_argument("--list", action="store_true", help="list generated outputs for scripts/generated-paths")
    args = parser.parse_args()

    root = pathlib.Path(__file__).resolve().parents[1]

    if args.list:
        for name, _ in FILES:
            print(f"v1-admission-source\t{(DESTINATION / name).as_posix()}\t")
        print(f"v1-admission-source\t{(DESTINATION / 'source-subset.json').as_posix()}\t")
        return 0
    elif args.check:
        verify_destination(root)
    else:
        import_from(root, args.source)

    print(f"V1_ADMISSION_SOURCE_OK commit={SOURCE_COMMIT} tree={SOURCE_TREE} files={len(FILES)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

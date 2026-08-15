#!/usr/bin/env python3
"""Verify a permanent, immutable-release archive of historical Git evidence."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import tarfile


SHA256 = re.compile(r"[0-9a-f]{64}")
GIT_SHA = re.compile(r"[0-9a-f]{40}")


class EvidenceError(Exception):
    pass


def sha256_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_string(value: object, label: str, pattern: re.Pattern[str] | None = None) -> str:
    if not isinstance(value, str) or not value:
        raise EvidenceError(f"{label} must be a non-empty string")
    if pattern is not None and pattern.fullmatch(value) is None:
        raise EvidenceError(f"{label} has an invalid format")
    return value


def safe_path(raw: object, label: str) -> str:
    value = require_string(raw, label)
    path = PurePosixPath(value)
    if path.is_absolute() or ".." in path.parts or "." in path.parts or str(path) != value:
        raise EvidenceError(f"{label} is not a canonical relative path: {value}")
    return value


def git(root: Path, *args: str) -> bytes:
    result = subprocess.run(
        ["git", "-C", str(root), *args],
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode != 0:
        raise EvidenceError(result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def validate(manifest: object) -> tuple[dict[str, object], list[dict[str, object]]]:
    if not isinstance(manifest, dict) or manifest.get("schema_version") != 1:
        raise EvidenceError("manifest must be a schema-v1 object")
    source_sha = require_string(manifest.get("source_sha"), "source_sha", GIT_SHA)
    release = manifest.get("release")
    if not isinstance(release, dict) or release.get("immutable") is not True:
        raise EvidenceError("release must declare immutable=true")
    tag = require_string(release.get("tag"), "release.tag")
    if source_sha[:8] not in tag:
        raise EvidenceError("release.tag must be content-addressed by source SHA")
    for field in ("url", "asset_url"):
        value = require_string(release.get(field), f"release.{field}")
        if not value.startswith("https://github.com/") or f"/releases/{'tag' if field == 'url' else 'download'}/" not in value:
            raise EvidenceError(f"release.{field} must be a GitHub release URL")
    archive = manifest.get("archive")
    if not isinstance(archive, dict):
        raise EvidenceError("archive must be an object")
    safe_path(archive.get("name"), "archive.name")
    require_string(archive.get("sha256"), "archive.sha256", SHA256)
    if not isinstance(archive.get("bytes"), int) or archive["bytes"] <= 0:
        raise EvidenceError("archive.bytes must be positive")
    prefix = safe_path(archive.get("prefix"), "archive.prefix")
    rows = manifest.get("files")
    if not isinstance(rows, list) or not rows:
        raise EvidenceError("files must be a non-empty array")
    seen: set[str] = set()
    canonical: list[str] = []
    total = 0
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise EvidenceError(f"files[{index}] must be an object")
        path = safe_path(row.get("path"), f"files[{index}].path")
        if path in seen:
            raise EvidenceError(f"duplicate file path: {path}")
        seen.add(path)
        size = row.get("bytes")
        if not isinstance(size, int) or size < 0:
            raise EvidenceError(f"{path}: bytes must be non-negative")
        digest = require_string(row.get("sha256"), f"{path}.sha256", SHA256)
        blob = require_string(row.get("git_blob"), f"{path}.git_blob", GIT_SHA)
        canonical.append(f"{size}\t{digest}\t{blob}\t{path}\n")
        total += size
    paths = [str(row["path"]) for row in rows]
    if paths != sorted(paths):
        raise EvidenceError("files must be sorted by path")
    if manifest.get("file_count") != len(rows):
        raise EvidenceError("file_count does not match files")
    if manifest.get("source_bytes") != total:
        raise EvidenceError("source_bytes does not match files")
    expected_rows = require_string(manifest.get("canonical_rows_sha256"), "canonical_rows_sha256", SHA256)
    if sha256_bytes("".join(canonical).encode()) != expected_rows:
        raise EvidenceError("canonical row digest mismatch")
    archive["prefix"] = prefix
    return archive, rows


def verify_archive(archive_path: Path, archive: dict[str, object], rows: list[dict[str, object]]) -> None:
    if not archive_path.is_file():
        raise EvidenceError(f"archive not found: {archive_path}")
    if archive_path.stat().st_size != archive["bytes"]:
        raise EvidenceError("archive byte count mismatch")
    if sha256_file(archive_path) != archive["sha256"]:
        raise EvidenceError("archive SHA-256 mismatch")
    prefix = f"{archive['prefix']}/"
    expected = {f"{prefix}{row['path']}": row for row in rows}
    expected_directories = {
        str(parent)
        for name in expected
        for parent in PurePosixPath(name).parents
        if str(parent) != "."
    }
    observed: set[str] = set()
    with tarfile.open(archive_path, "r:gz") as bundle:
        for member in bundle.getmembers():
            if member.isdir() and member.name.rstrip("/") in expected_directories:
                continue
            safe_path(member.name, "archive member")
            if not member.isfile() or member.name not in expected:
                raise EvidenceError(f"unexpected archive member: {member.name}")
            if member.name in observed:
                raise EvidenceError(f"duplicate archive member: {member.name}")
            observed.add(member.name)
            stream = bundle.extractfile(member)
            if stream is None:
                raise EvidenceError(f"cannot read archive member: {member.name}")
            payload = stream.read()
            row = expected[member.name]
            if len(payload) != row["bytes"] or sha256_bytes(payload) != row["sha256"]:
                raise EvidenceError(f"archive member content mismatch: {member.name}")
    missing = sorted(set(expected) - observed)
    if missing:
        raise EvidenceError("archive is missing: " + ", ".join(missing))


def verify_git(root: Path, source_sha: str, rows: list[dict[str, object]]) -> None:
    resolved = git(root, "rev-parse", source_sha).decode().strip()
    if resolved != source_sha:
        raise EvidenceError("source_sha does not resolve exactly")
    for row in rows:
        path = str(row["path"])
        blob = git(root, "rev-parse", f"{source_sha}:{path}").decode().strip()
        if blob != row["git_blob"]:
            raise EvidenceError(f"Git blob mismatch: {path}")
        payload = git(root, "show", f"{source_sha}:{path}")
        if len(payload) != row["bytes"] or sha256_bytes(payload) != row["sha256"]:
            raise EvidenceError(f"Git payload mismatch: {path}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--archive", type=Path)
    parser.add_argument("--git-root", type=Path)
    args = parser.parse_args()
    try:
        manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
        archive, rows = validate(manifest)
        if args.archive is not None:
            verify_archive(args.archive, archive, rows)
        if args.git_root is not None:
            verify_git(args.git_root, str(manifest["source_sha"]), rows)
        if args.archive is None and args.git_root is None:
            raise EvidenceError("at least one of --archive or --git-root is required")
        print(f"historical evidence: ok ({len(rows)} files, {manifest['source_bytes']} bytes)")
        return 0
    except (EvidenceError, OSError, json.JSONDecodeError, tarfile.TarError) as error:
        print(f"historical evidence: ERROR: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())

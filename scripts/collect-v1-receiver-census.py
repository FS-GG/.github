#!/usr/bin/env python3
"""Capture the reviewed v1 receiver source set through fixed GitHub GET routes only."""

from __future__ import annotations

import argparse
import base64
import hashlib
import io
import json
import os
import re
import subprocess
import tarfile
from pathlib import Path, PurePosixPath
from typing import Any


SCHEMA = "fsgg.v1-receiver-source-capture/1"
REVIEWED = (
    ("sdd", "FS-GG/FS.GG.SDD", "8d648c8deaf1edc16b942d0cfccee722c3a0a24c", "0.87.0"),
    ("rendering", "FS-GG/FS.GG.Rendering", "86102999e7f60a494bed74e825e36b284fef6d62", "0.75.4"),
    ("governance", "FS-GG/FS.GG.Governance", "e0580e402c07f1c0576183eb8c6433bf6b1185bd", "0.58.0"),
    ("templates", "FS-GG/FS.GG.Templates", "61091078337689c6ab1aac139bc03f6a07ca8f99", "0.75.4"),
    ("game", "FS-GG/FS.GG.Game", "24f79084fdd289f34387f91b1d4398c78fde16eb", "0.75.4"),
    ("audio", "FS-GG/FS.GG.Audio", "04ca17810c2a00efa20a689405f943a20c06769a", "0.75.4"),
    ("net", "FS-GG/FS.GG.Net", "e66d38d7ed7fb5cc36984f4d33434bd9c6eaa802", "0.75.4"),
)
REVIEWED_TOOL_TAGS = (
    ("0.58.0", "src/FS.GG.Coord.Cli/Options.fs"),
    ("0.75.4", "src/FS.GG.Coord.Cli.Kernel/Options.fs"),
    ("0.87.0", "src/FS.GG.Coord.Cli.Kernel/Options.fs"),
)
REVIEWED_CALLEES = (
    ("FS-GG/.github", "5fed2838f9ed085ffca09f4cc18b4f7bc59c1294", ".github/workflows/dispatch-sender.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/contract-coherence.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/coordination-coherence.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/kit-materialize.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/lock-range-coherence.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/lockfile-sync.yml"),
    ("FS-GG/.github", "c1ad41e059c18376685b5c71256149c35c341652", ".github/workflows/skill-union-assert.yml"),
)
RELEVANT = re.compile(
    r"^(?:\.github/(?:workflows|actions)/|scripts/|tools/|\.agents/skills/|\.claude/skills/)"
    r"|(?:^|/)(?:\.config/dotnet-tools\.json|global\.json|Directory\.(?:Build|Packages)\.(?:props|targets)|NuGet\.config)$"
    r"|\.(?:fsproj|props|targets|sh|py)$"
)


class CaptureError(RuntimeError):
    pass


def gh_get(endpoint: str) -> bytes:
    result = subprocess.run(["gh", "api", "--method", "GET", endpoint], capture_output=True, check=False)
    if result.returncode != 0:
        raise CaptureError(f"source GET denied or incomplete for {endpoint}: {result.stderr.decode(errors='replace').strip()}")
    return result.stdout


def git_blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob " + str(len(data)).encode() + b"\0" + data).hexdigest()


def canonical_sha(value: Any) -> str:
    encoded = json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()
    return hashlib.sha256(encoded).hexdigest()


def roster(root: Path) -> list[tuple[str, str]]:
    text = (root / "registry/repos.yml").read_text(encoding="utf-8")
    rows = re.findall(r"^\s+- \{ id: ([a-z0-9.-]+),\s+full: (FS-GG/[^,]+),\s+role: framework,.*coordination-kit", text, re.M)
    return rows


def safe_tar_files(payload: bytes) -> dict[str, bytes]:
    result: dict[str, bytes] = {}
    with tarfile.open(fileobj=io.BytesIO(payload), mode="r:gz") as archive:
        members = archive.getmembers()
        roots = {PurePosixPath(member.name).parts[0] for member in members if member.name}
        if len(roots) != 1:
            raise CaptureError("source archive does not have one repository root")
        root = next(iter(roots))
        for member in members:
            path = PurePosixPath(member.name)
            if not member.isfile() or not path.parts or path.parts[0] != root:
                continue
            relative = PurePosixPath(*path.parts[1:])
            if relative.is_absolute() or ".." in relative.parts:
                raise CaptureError("source archive contains path traversal")
            extracted = archive.extractfile(member)
            if extracted is not None:
                result[str(relative)] = extracted.read()
    return result


def resolve_tag(repository: str, tag: str) -> str:
    payload = json.loads(gh_get(f"repos/{repository}/git/ref/tags/{tag}"))
    object_value = payload.get("object", {})
    if object_value.get("type") == "tag":
        object_value = json.loads(gh_get(f"repos/{repository}/git/tags/{object_value.get('sha')}")).get("object", {})
    if object_value.get("type") != "commit" or not re.fullmatch(r"[0-9a-f]{40}", str(object_value.get("sha"))):
        raise CaptureError(f"reviewed tag {repository}:{tag} does not resolve to one commit")
    return str(object_value["sha"])


def exact_path(repository: str, revision: str, path: str) -> dict[str, Any]:
    payload = json.loads(gh_get(f"repos/{repository}/contents/{path}?ref={revision}"))
    if payload.get("type") != "file" or payload.get("encoding") != "base64":
        raise CaptureError(f"reviewed callee is not one source file: {repository}@{revision}:{path}")
    data = base64.b64decode(payload.get("content", ""), validate=False)
    if git_blob_sha(data) != payload.get("sha"):
        raise CaptureError(f"reviewed callee bytes differ from GitHub blob identity: {repository}@{revision}:{path}")
    return {"repository": repository, "revision": revision, "path": path, "blobSha1": payload["sha"], "sha256": hashlib.sha256(data).hexdigest(), "bytesBase64": base64.b64encode(data).decode()}


def capture(root: Path) -> dict[str, Any]:
    reviewed_roster = [(row[0], row[1]) for row in REVIEWED]
    if roster(root) != reviewed_roster:
        raise CaptureError("registry/repos.yml receiver order or repository differs from the reviewed finite source set")
    receivers = []
    for receiver_id, repository, revision, tool_version in REVIEWED:
        commit_bytes = gh_get(f"repos/{repository}/git/commits/{revision}")
        commit_payload = json.loads(commit_bytes)
        if commit_payload.get("sha") != revision or not isinstance(commit_payload.get("tree", {}).get("sha"), str):
            raise CaptureError(f"{repository} exact revision did not resolve to one commit and tree")
        tree_oid = commit_payload["tree"]["sha"]
        tree_bytes = gh_get(f"repos/{repository}/git/trees/{tree_oid}?recursive=1")
        tree_payload = json.loads(tree_bytes)
        if tree_payload.get("sha") != tree_oid or tree_payload.get("truncated") is not False or not isinstance(tree_payload.get("tree"), list):
            raise CaptureError(f"{repository} recursive tree is incomplete or truncated")
        tree = tree_payload["tree"]
        manifest = sorted([
            {key: row.get(key) for key in ("path", "mode", "type", "sha", "size") if row.get(key) is not None}
            for row in tree
        ], key=lambda row: row.get("path", ""))
        if any(not isinstance(row.get("path"), str) or not isinstance(row.get("sha"), str) for row in manifest):
            raise CaptureError(f"{repository} tree contains a malformed identity")
        archive_bytes = gh_get(f"repos/{repository}/tarball/{revision}")
        archive = safe_tar_files(archive_bytes)
        blobs = {row["path"]: row for row in manifest if row.get("type") == "blob"}
        relevant = []
        for path in sorted(name for name in archive if RELEVANT.search(name)):
            if path not in blobs:
                raise CaptureError(f"{repository} archive path is absent from its exact tree: {path}")
            data = archive[path]
            if git_blob_sha(data) != blobs[path]["sha"]:
                raise CaptureError(f"{repository} archive bytes differ from the exact tree: {path}")
            relevant.append({"path": path, "blobSha1": blobs[path]["sha"], "sha256": hashlib.sha256(data).hexdigest(), "bytesBase64": base64.b64encode(data).decode()})
        receivers.append({
            "id": receiver_id, "repository": repository, "revision": revision,
            "tree": tree_oid, "installedCoordCliVersion": tool_version,
            "commitResponseSha256": hashlib.sha256(commit_bytes).hexdigest(),
            "treeResponseSha256": hashlib.sha256(tree_bytes).hexdigest(),
            "archiveResponseSha256": hashlib.sha256(archive_bytes).hexdigest(),
            "sourceManifestSha256": canonical_sha(manifest), "manifest": manifest,
            "relevantSources": relevant,
        })
    tool_sources = []
    for version, options_path in REVIEWED_TOOL_TAGS:
        revision = resolve_tag("FS-GG/.github", f"coord-engine/v{version}")
        commit_payload = json.loads(gh_get(f"repos/FS-GG/.github/git/commits/{revision}"))
        tree_oid = commit_payload.get("tree", {}).get("sha")
        if commit_payload.get("sha") != revision or not isinstance(tree_oid, str):
            raise CaptureError(f"coord-engine/v{version} source commit is incomplete")
        source = exact_path("FS-GG/.github", revision, options_path)
        project = exact_path("FS-GG/.github", revision, "src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj")
        tool_sources.append({"version": version, "repository": "FS-GG/.github", "revision": revision, "tree": tree_oid, "options": source, "project": project})
    callees = [exact_path(repository, revision, path) for repository, revision, path in REVIEWED_CALLEES]
    return {"schema": SCHEMA, "source": "fixed-github-rest-get/1", "terminal": True, "receivers": receivers, "toolSources": tool_sources, "reviewedCallees": callees}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        value = capture(args.root.resolve())
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n", encoding="utf-8")
    except (CaptureError, OSError, UnicodeError, json.JSONDecodeError, tarfile.TarError) as error:
        print(f"v1 receiver capture refused: {error}", file=os.sys.stderr)
        return 1
    print(f"V1_RECEIVER_CAPTURE_OK receivers={len(value['receivers'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

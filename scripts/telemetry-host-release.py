#!/usr/bin/env python3
"""Prepare and verify the independent FS.GG.Telemetry.Host release artifact.

This is deliberately a one-package helper.  It imports the accepted NuGet payload
normalisation from release-saga.py but does not join that three-member transaction.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import pathlib
import re
import sys
import zipfile
from datetime import datetime, timezone
from xml.etree import ElementTree

ROOT = pathlib.Path(__file__).resolve().parents[1]
SCHEMA = "fsgg.telemetry.host-release/1"
JOURNAL_SCHEMA = "fsgg.telemetry-host-release-journal/v1"
PACKAGE_ID = "FS.GG.Telemetry.Host"
TAG_PREFIX = "telemetry-host/v"


def sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def canonical(value: object) -> bytes:
    # Byte-identical to scripts/release-saga.py canonical(): no trailing newline.
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()


def release_saga():
    path = ROOT / "scripts" / "release-saga.py"
    spec = importlib.util.spec_from_file_location("fsgg_release_saga", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load release-saga.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def package_identity(path: pathlib.Path) -> tuple[str, str]:
    with zipfile.ZipFile(path) as archive:
        nuspecs = [name for name in archive.namelist() if name.endswith(".nuspec") and "/" not in name]
        if len(nuspecs) != 1:
            raise ValueError("package must contain exactly one root nuspec")
        root = ElementTree.fromstring(archive.read(nuspecs[0]))
    values: dict[str, list[str]] = {}
    for element in root.iter():
        values.setdefault(element.tag.rsplit("}", 1)[-1], []).append(element.text or "")
    ids, versions = values.get("id", []), values.get("version", [])
    if len(ids) != 1 or len(versions) != 1:
        raise ValueError("package nuspec identity is ambiguous")
    return ids[0], versions[0]


def tree_digest(root: pathlib.Path) -> str:
    if not root.is_dir():
        raise ValueError(f"asset root is missing: {root}")
    rows = []
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        rows.append({"path": path.relative_to(root).as_posix(), "sha256": sha256(path), "bytes": path.stat().st_size})
    if not rows:
        raise ValueError("asset root is empty")
    return hashlib.sha256(canonical(rows)).hexdigest()


def build_manifest(args: argparse.Namespace) -> dict:
    package = pathlib.Path(args.package).resolve()
    lock = pathlib.Path(args.lock).resolve()
    assets = pathlib.Path(args.assets).resolve()
    package_id, version = package_identity(package)
    if package_id != PACKAGE_ID or version != args.version:
        raise ValueError(f"package identity is {package_id} {version}, expected {PACKAGE_ID} {args.version}")
    if not re.fullmatch(r"[0-9a-f]{40}", args.source_sha):
        raise ValueError("source SHA must be exactly 40 lowercase hexadecimal characters")
    if args.tag != TAG_PREFIX + args.version:
        raise ValueError("tag must be telemetry-host/v<version>")
    saga = release_saga()
    return {
        "schema": SCHEMA,
        "packageId": PACKAGE_ID,
        "version": args.version,
        "tag": args.tag,
        "sourceSha": args.source_sha,
        "framework": "net10.0",
        "target": "linux-x64",
        "supportedStoreSchemaMin": 9,
        "supportedStoreSchemaMax": 9,
        "runtimePrerequisites": ["Microsoft.AspNetCore.App 10.0", "Microsoft.NETCore.App 10.0"],
        "archiveSha256": sha256(package),
        "producerPayloadSha256": saga.payload_id(package),
        "dependencyLockSha256": sha256(lock),
        "uiAssetTreeSha256": tree_digest(assets),
        "createdAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
    }


def load_manifest(path: pathlib.Path) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema") != SCHEMA:
        raise ValueError("release manifest schema is invalid")
    required = {"schema","packageId","version","tag","sourceSha","archiveSha256","producerPayloadSha256","framework","target","dependencyLockSha256","uiAssetTreeSha256","supportedStoreSchemaMin","supportedStoreSchemaMax","runtimePrerequisites","createdAt"}
    string_fields = required - {"supportedStoreSchemaMin", "supportedStoreSchemaMax", "runtimePrerequisites"}
    if set(data) != required or any(not isinstance(data.get(name), str) for name in string_fields):
        raise ValueError("release manifest shape or identity is invalid")
    if data["packageId"] != PACKAGE_ID or data["tag"] != TAG_PREFIX + data["version"]:
        raise ValueError("release manifest shape or identity is invalid")
    digest = re.compile(r"^[0-9a-f]{64}$")
    if not re.fullmatch(r"[0-9a-f]{40}", data["sourceSha"]):
        raise ValueError("release manifest source SHA is invalid")
    if not digest.fullmatch(data["archiveSha256"]) or not digest.fullmatch(data["dependencyLockSha256"]) or not digest.fullmatch(data["uiAssetTreeSha256"]):
        raise ValueError("release manifest digest is invalid")
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", data["producerPayloadSha256"]):
        raise ValueError("release manifest producer payload digest is invalid")
    if data["framework"] != "net10.0" or data["target"] != "linux-x64":
        raise ValueError("release manifest runtime profile is invalid")
    if data["supportedStoreSchemaMin"] != 9 or data["supportedStoreSchemaMax"] != 9:
        raise ValueError("release manifest store schema range is invalid")
    if data["runtimePrerequisites"] != ["Microsoft.AspNetCore.App 10.0", "Microsoft.NETCore.App 10.0"]:
        raise ValueError("release manifest runtime prerequisites are invalid")
    try:
        created = datetime.fromisoformat(data["createdAt"].replace("Z", "+00:00"))
    except (TypeError, ValueError) as error:
        raise ValueError("release manifest creation time is invalid") from error
    if created.tzinfo is None or not data["createdAt"].endswith("Z"):
        raise ValueError("release manifest creation time must be RFC3339 UTC")
    return data


def verify_artifact(manifest_path: pathlib.Path, artifact: pathlib.Path) -> dict:
    data = load_manifest(manifest_path)
    expected = data
    package_id, version = package_identity(artifact)
    if package_id != expected["packageId"] or version != expected["version"]:
        raise ValueError("observed package identity differs from prepared manifest")
    saga = release_saga()
    archive_sha = sha256(artifact)
    payload_sha = saga.payload_id(artifact)
    return {
        "packageId": package_id,
        "version": version,
        "archiveSha256": archive_sha,
        "payloadSha256": payload_sha,
        "preparedArchiveEqual": archive_sha == expected["archiveSha256"],
        "producerPayloadEqual": payload_sha == expected["producerPayloadSha256"],
    }


def journal_record(path: pathlib.Path, manifest: dict, feed: str, observation: dict) -> None:
    if feed not in {"github", "nuget"}:
        raise ValueError("feed must be github or nuget")
    if path.exists():
        journal = json.loads(path.read_text(encoding="utf-8"))
        manifest_digest = "sha256:" + hashlib.sha256(canonical(manifest)).hexdigest()
        if journal.get("schema") != JOURNAL_SCHEMA or journal.get("manifestSha256") != manifest_digest:
            raise ValueError("release journal belongs to a different manifest")
    else:
        journal = {"schema": JOURNAL_SCHEMA, "manifestSha256": "sha256:" + hashlib.sha256(canonical(manifest)).hexdigest(), "observations": {}}
    prior = journal["observations"].get(feed)
    row = {**observation, "observedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")}
    if prior and any(prior.get(key) != row.get(key) for key in ("archiveSha256", "payloadSha256")):
        raise ValueError(f"immutable {feed} observation conflicts with the journal")
    journal["observations"][feed] = prior or row
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_bytes(canonical(journal))
    temporary.replace(path)


def resolve_remote_tag(tag: str, path: pathlib.Path) -> str:
    direct = f"refs/tags/{tag}"
    peeled = direct + "^{}"
    found: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        sha, separator, reference = line.partition("\t")
        if not separator or reference not in (direct, peeled) or reference in found or not re.fullmatch(r"[0-9a-f]{40}", sha):
            raise ValueError("invalid remote tag observation")
        found[reference] = sha
    if direct not in found:
        raise ValueError("remote tag observation omitted the direct ref")
    return found.get(peeled, found[direct])


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    prepare = sub.add_parser("prepare")
    prepare.add_argument("--package", required=True)
    prepare.add_argument("--lock", required=True)
    prepare.add_argument("--assets", required=True)
    prepare.add_argument("--source-sha", required=True)
    prepare.add_argument("--version", required=True)
    prepare.add_argument("--tag", required=True)
    prepare.add_argument("--output", required=True)
    verify = sub.add_parser("verify")
    verify.add_argument("--manifest", required=True)
    verify.add_argument("--package", required=True)
    verify.add_argument("--feed", choices=("prepared", "github", "nuget"), default="prepared")
    verify.add_argument("--journal")
    verify.add_argument("--payload-only", action="store_true", help="compare producer payload while allowing pack metadata/archive differences")
    tag = sub.add_parser("resolve-tag")
    tag.add_argument("--tag", required=True)
    tag.add_argument("--input", required=True)
    args = parser.parse_args()
    try:
        if args.command == "prepare":
            output = pathlib.Path(args.output)
            output.write_bytes(canonical(build_manifest(args)) + b"\n")
            return 0
        if args.command == "resolve-tag":
            print(resolve_remote_tag(args.tag, pathlib.Path(args.input)))
            return 0
        manifest_path, artifact = pathlib.Path(args.manifest), pathlib.Path(args.package)
        data = load_manifest(manifest_path)
        observation = verify_artifact(manifest_path, artifact)
        if not observation["producerPayloadEqual"]:
            raise ValueError("observed package producer payload differs from the prepared artifact")
        if args.feed == "prepared" and not args.payload_only and not observation["preparedArchiveEqual"]:
            raise ValueError("prepared package archive digest differs from its manifest")
        if args.feed != "prepared":
            if not args.journal:
                raise ValueError("external feed verification requires --journal")
            journal_record(pathlib.Path(args.journal), data, args.feed, observation)
        print(json.dumps(observation, sort_keys=True, separators=(",", ":")))
        return 0
    except (OSError, ValueError, KeyError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        print(f"telemetry-host-release: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())

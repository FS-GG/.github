#!/usr/bin/env python3
"""Create and verify compact Git evidence manifests for immutable CI artifacts."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sys
from urllib.parse import urlparse

SCHEMA = 1
SHA256 = re.compile(r"^[0-9a-f]{64}$")


class EvidenceError(Exception):
    pass


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def timestamp(value: str) -> datetime:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise EvidenceError(f"invalid RFC3339 timestamp: {value}") from error
    if parsed.tzinfo is None:
        raise EvidenceError(f"timestamp has no timezone: {value}")
    return parsed.astimezone(timezone.utc)


def validate(document: object, now: datetime, allow_expired: bool = False) -> list[str]:
    if not isinstance(document, dict) or document.get("schema_version") != SCHEMA:
        raise EvidenceError("manifest must be a schema-v1 object")
    for field in ("cycle_id", "source_sha", "created_at", "reproduce"):
        if not isinstance(document.get(field), str) or not document[field].strip():
            raise EvidenceError(f"missing non-empty {field}")
    if not re.fullmatch(r"[0-9a-f]{40}", document["source_sha"]):
        raise EvidenceError("source_sha must be an exact 40-character Git SHA")
    timestamp(document["created_at"])
    artifacts = document.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        raise EvidenceError("artifacts must be a non-empty array")
    seen: set[str] = set(); summaries: list[str] = []
    for index, row in enumerate(artifacts):
        if not isinstance(row, dict): raise EvidenceError(f"artifacts[{index}] must be an object")
        for field in ("name", "sha256", "url", "expires_at"):
            if not isinstance(row.get(field), str) or not row[field].strip():
                raise EvidenceError(f"artifacts[{index}] missing non-empty {field}")
        if row["name"] in seen: raise EvidenceError(f"duplicate artifact name: {row['name']}")
        seen.add(row["name"])
        if not SHA256.fullmatch(row["sha256"]): raise EvidenceError(f"{row['name']}: invalid sha256")
        if not isinstance(row.get("bytes"), int) or row["bytes"] < 0:
            raise EvidenceError(f"{row['name']}: bytes must be a non-negative integer")
        parsed = urlparse(row["url"])
        if parsed.scheme != "https" or parsed.netloc != "github.com" or "/actions/runs/" not in parsed.path:
            raise EvidenceError(f"{row['name']}: url must be an HTTPS GitHub Actions run/artifact URL")
        expiry = timestamp(row["expires_at"])
        if expiry <= now and not allow_expired:
            raise EvidenceError(f"{row['name']}: artifact expired at {row['expires_at']}; rerun the reproduce command and replace this manifest")
        summaries.append(f"{row['name']} sha256={row['sha256']} bytes={row['bytes']}")
    return summaries


def create(args: argparse.Namespace) -> dict:
    names = args.name or []
    if len(names) != len(args.file): raise EvidenceError("provide exactly one --name per --file")
    expires = timestamp(args.expires_at)
    created = timestamp(args.created_at)
    if expires <= created: raise EvidenceError("expires-at must be later than created-at")
    rows = []
    for name, file in zip(names, args.file):
        path = Path(file)
        if not path.is_file(): raise EvidenceError(f"artifact does not exist: {path}")
        rows.append({"name": name, "sha256": digest(path), "bytes": path.stat().st_size,
                     "url": args.url, "expires_at": args.expires_at})
    return {"schema_version": SCHEMA, "cycle_id": args.cycle, "source_sha": args.source_sha,
            "created_at": args.created_at, "reproduce": args.reproduce, "artifacts": rows}


def main() -> int:
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="action", required=True)
    make = sub.add_parser("create")
    make.add_argument("--cycle", required=True); make.add_argument("--source-sha", required=True)
    make.add_argument("--created-at", required=True); make.add_argument("--expires-at", required=True)
    make.add_argument("--reproduce", required=True); make.add_argument("--url", required=True)
    make.add_argument("--name", action="append"); make.add_argument("--file", action="append", required=True)
    make.add_argument("--output", type=Path, required=True)
    check = sub.add_parser("verify")
    check.add_argument("manifest", type=Path); check.add_argument("--now")
    check.add_argument("--allow-expired", action="store_true")
    check.add_argument("--artifact", action="append", default=[], metavar="NAME=PATH")
    args = parser.parse_args()
    try:
        if args.action == "create":
            document = create(args)
            validate(document, timestamp(args.created_at))
            args.output.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
            print(f"wrote {args.output} ({len(document['artifacts'])} artifact(s))")
        else:
            document = json.loads(args.manifest.read_text(encoding="utf-8"))
            now = timestamp(args.now) if args.now else datetime.now(timezone.utc)
            rows = validate(document, now, args.allow_expired)
            declared = {row["name"]: row for row in document["artifacts"]}
            for binding in args.artifact:
                if "=" not in binding: raise EvidenceError("--artifact must be NAME=PATH")
                name, raw_path = binding.split("=", 1)
                if name not in declared: raise EvidenceError(f"artifact binding names undeclared artifact: {name}")
                path = Path(raw_path)
                if not path.is_file(): raise EvidenceError(f"artifact payload does not exist: {path}")
                actual = digest(path)
                if actual != declared[name]["sha256"]:
                    raise EvidenceError(f"{name}: payload sha256 mismatch: expected {declared[name]['sha256']}, got {actual}")
            print(f"evidence manifest: ok ({len(rows)} artifact(s))")
        return 0
    except (EvidenceError, OSError, json.JSONDecodeError) as error:
        print(f"evidence manifest: ERROR: {error}", file=sys.stderr); return 1


if __name__ == "__main__":
    raise SystemExit(main())

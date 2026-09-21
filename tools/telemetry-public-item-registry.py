#!/usr/bin/env python3
"""Resolve approved public PR selectors to private telemetry item identities.

The public registry contains no private item IDs. This command runs beside the
private store, verifies a complete canonical item-detail snapshot, and emits the
existing closed dashboard-labels/1 format. Its report contains only counts and
digests.
"""

from __future__ import annotations

import argparse
import base64
import gzip
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import tempfile

MAX_ENVELOPE = 16 * 1024 * 1024
MAX_SNAPSHOT = 64 * 1024 * 1024
REPOSITORY = re.compile(r"FS-GG/[A-Za-z0-9_.-]+\Z")
KEY = re.compile(r"[a-z0-9][a-z0-9-]{0,63}\Z")
PUBLIC_URL = re.compile(r"https://github\.com/FS-GG/[A-Za-z0-9_.-]+/(?:issues|pull)/[1-9][0-9]*\Z")


class Refusal(ValueError):
    pass


def exact(value, fields, name):
    if not isinstance(value, dict) or set(value) != set(fields):
        raise Refusal(f"invalid {name}")
    return value


def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=True) + "\n").encode()


def load_regular(path: Path, maximum: int, private: bool):
    before = path.lstat()
    mode = stat.S_IMODE(before.st_mode)
    if (path.is_symlink() or not stat.S_ISREG(before.st_mode) or before.st_size > maximum
            or (private and mode != 0o600) or (not private and mode & 0o022)):
        raise Refusal("unsafe input")
    data = path.read_bytes()
    after = path.lstat()
    if len(data) != before.st_size or (before.st_dev, before.st_ino) != (after.st_dev, after.st_ino):
        raise Refusal("input changed during read")
    try:
        return json.loads(data)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise Refusal("invalid JSON input") from error


def load_snapshot(envelope):
    exact(envelope, {"schema", "observedAt", "revision", "canonicalSnapshotGzip", "operational"}, "item detail")
    if envelope["schema"] != "fsgg.telemetry.item-detail/2" or not re.fullmatch(r"[0-9a-f]{64}", envelope["revision"]):
        raise Refusal("invalid item detail")
    try:
        compressed = base64.b64decode(envelope["canonicalSnapshotGzip"], validate=True)
        selected = gzip.decompress(compressed)
    except (TypeError, ValueError, OSError) as error:
        raise Refusal("invalid canonical snapshot") from error
    if len(selected) > MAX_SNAPSHOT or hashlib.sha256(selected).hexdigest() != envelope["revision"]:
        raise Refusal("canonical snapshot digest mismatch")
    try:
        snapshot = json.loads(selected)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise Refusal("invalid canonical snapshot") from error
    selection = snapshot.get("selection") if isinstance(snapshot, dict) else None
    store = snapshot.get("store") if isinstance(snapshot, dict) else None
    operational = envelope["operational"]
    if (not isinstance(selection, dict) or selection.get("mode") != "all" or selection.get("complete") is not True
            or not isinstance(store, dict) or store.get("schemaVersion") != 10 or store.get("journalMode") != "wal"
            or not isinstance(operational, dict) or operational.get("pendingBatches") != 0):
        raise Refusal("private snapshot is not complete and settled")
    return snapshot


def validate_registry(value):
    exact(value, {"schema", "items"}, "public registry")
    if value["schema"] != "fsgg.telemetry.public-item-registry/1" or not isinstance(value["items"], dict) or len(value["items"]) > 200:
        raise Refusal("invalid public registry")
    selectors = set()
    for key, item in value["items"].items():
        if not isinstance(key, str) or not KEY.fullmatch(key):
            raise Refusal("invalid public key")
        exact(item, {"label", "url", "repositories", "deliveries", "notes"}, "public registry item")
        if not isinstance(item["label"], str) or not 1 <= len(item["label"]) <= 120 or not PUBLIC_URL.fullmatch(item["url"]):
            raise Refusal("invalid public label")
        repositories = item["repositories"]
        if (not isinstance(repositories, list) or not 1 <= len(repositories) <= 8
                or len(repositories) != len(set(repositories)) or any(not isinstance(repo, str) or not REPOSITORY.fullmatch(repo) for repo in repositories)):
            raise Refusal("invalid public repositories")
        deliveries = item["deliveries"]
        if not isinstance(deliveries, list) or not 1 <= len(deliveries) <= 8:
            raise Refusal("invalid public deliveries")
        for delivery in deliveries:
            exact(delivery, {"repository", "pullRequest"}, "public delivery")
            selector = (delivery["repository"], delivery["pullRequest"])
            if (not isinstance(selector[0], str) or selector[0] not in repositories or not REPOSITORY.fullmatch(selector[0])
                    or not isinstance(selector[1], int) or isinstance(selector[1], bool) or selector[1] <= 0
                    or selector in selectors):
                raise Refusal("invalid or duplicate public delivery")
            selectors.add(selector)
        if item["notes"] != []:
            raise Refusal("public notes require separate review")
    return value


def validate_base(value):
    exact(value, {"schema", "items", "models", "efforts", "scopes"}, "base labels")
    if value["schema"] != "fsgg.telemetry.dashboard-labels/1" or any(not isinstance(value[k], dict) for k in ("items", "models", "efforts", "scopes")):
        raise Refusal("invalid base labels")
    return value


def materialize(registry, snapshot, base):
    outcomes = snapshot.get("outcomes")
    populations = snapshot.get("populations")
    if not isinstance(outcomes, list) or not isinstance(populations, list) or len(outcomes) > 10000 or len(populations) > 10000:
        raise Refusal("private snapshot relations are invalid")
    originals = {}
    completed = set()
    for row in populations:
        if not isinstance(row, dict) or not isinstance(row.get("item_id"), str) or not isinstance(row.get("original_item_id"), str):
            raise Refusal("private population is invalid")
        previous = originals.setdefault(row["item_id"], row["original_item_id"])
        if previous != row["original_item_id"]:
            raise Refusal("ambiguous private population")
        if row.get("state") == "completed":
            completed.add(row["item_id"])
    by_delivery = {}
    for row in outcomes:
        if not isinstance(row, dict):
            raise Refusal("private outcome is invalid")
        selector = (row.get("repository"), row.get("pr_number"))
        if (not isinstance(selector[0], str) or not isinstance(selector[1], int)
                or row.get("outcome") not in {"delivered", "delivered-after-readback"} or row.get("code_delivery") != "delivered"):
            continue
        original = originals.get(row.get("item_id"))
        if original is None or row.get("item_id") not in completed:
            continue
        by_delivery.setdefault(selector, set()).add(original)

    output = json.loads(json.dumps(base))
    used_originals = set(output["items"])
    for key, item in registry["items"].items():
        matches = set()
        for selector in ((d["repository"], d["pullRequest"]) for d in item["deliveries"]):
            selected = by_delivery.get(selector, set())
            if len(selected) != 1:
                raise Refusal("public delivery does not resolve exactly once")
            matches.update(selected)
        if len(matches) != 1:
            raise Refusal("public deliveries span private items")
        original = next(iter(matches))
        if original in used_originals:
            raise Refusal("public item already mapped")
        used_originals.add(original)
        output["items"][original] = {"key": key, "label": item["label"], "url": item["url"],
                                      "repositories": item["repositories"], "notes": item["notes"]}
    return output


def atomic_private(path: Path, data: bytes):
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix="." + path.name + ".", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "wb") as stream:
            fd = -1
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
        parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
        try:
            os.fsync(parent)
        finally:
            os.close(parent)
    finally:
        if fd >= 0:
            os.close(fd)
        if os.path.exists(name):
            os.unlink(name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--registry", type=Path, required=True)
    parser.add_argument("--item-detail", type=Path, required=True)
    parser.add_argument("--base-labels", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    registry = validate_registry(load_regular(args.registry, 65536, False))
    envelope = load_regular(args.item_detail, MAX_ENVELOPE, True)
    base = validate_base(load_regular(args.base_labels, 65536, True))
    result = materialize(registry, load_snapshot(envelope), base)
    raw = canonical(result)
    atomic_private(args.output, raw)
    print(json.dumps({"schema": "fsgg.telemetry.public-label-materialization/1", "status": "materialized",
                      "registryDigest": hashlib.sha256(canonical(registry)).hexdigest(),
                      "labelsDigest": hashlib.sha256(raw).hexdigest(), "approvedItems": len(result["items"])},
                     sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except (KeyError, TypeError, OSError, Refusal) as error:
        print(f"public label materialization refused: {error}", file=os.sys.stderr)
        raise SystemExit(1)

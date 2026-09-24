#!/usr/bin/env python3
"""Read the isolated Authority document for one exact rehearsal receipt."""

from __future__ import annotations

import base64
import binascii
import hashlib
import json
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

REPOSITORY = "FS-GG/FS.GG.Coordination.Authority.Sandbox"
REPOSITORY_ID = 1385801070
SOURCE_REPOSITORY_ID = 1269292704
DOCUMENT_SCHEMA = "fsgg.coordination.ordinary-settlement-authority-document/1"


def address(receipt: dict) -> tuple[str, str]:
    if (receipt.get("schema") != "fsgg.github.v2-ci-secret-free-predecessor-receipt/1"
            or receipt.get("policyId") != "v2-ci-i1-ordinary-settlement-rehearsal-v1"
            or receipt.get("status") != "qualified"):
        raise ValueError("wrong rehearsal receipt")
    node = receipt.get("pullRequestNodeId")
    if not isinstance(node, str) or not node or len(node) > 200:
        raise ValueError("invalid pull request node identity")
    aggregate = f"ordinary:{SOURCE_REPOSITORY_ID}:{node}".lower().encode("utf-8")
    digest = hashlib.sha256(str(len(aggregate)).encode("ascii") + b":" + aggregate).hexdigest()
    ref = f"refs/heads/fsgg/v2/journal/operation/{digest[:2]}"
    return digest, ref


def require_public_repository() -> None:
    url = f"https://api.github.com/repos/{REPOSITORY}"
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "fsgg-v2-rehearsal-readback",
               "X-GitHub-Api-Version": "2022-11-28"}
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
            raw = response.read(2_000_001)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"sandbox repository read unavailable: HTTP {error.code}") from error
    if len(raw) > 2_000_000:
        raise ValueError("sandbox repository response is oversized")
    repository = json.loads(raw)
    if (not isinstance(repository, dict) or repository.get("id") != REPOSITORY_ID
            or repository.get("full_name") != REPOSITORY or repository.get("private") is not False):
        raise ValueError("sandbox repository identity or public visibility changed")


def document(digest: str, ref: str) -> dict | None:
    path = f"repos/{REPOSITORY}/contents/ordinary-v2/{digest}.json"
    url = f"https://api.github.com/{path}?ref={urllib.parse.quote(ref, safe='')}"
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "fsgg-v2-rehearsal-readback",
               "X-GitHub-Api-Version": "2022-11-28"}
    try:
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
            raw_response = response.read(2_000_001)
    except urllib.error.HTTPError as error:
        if error.code == 404:
            return None
        raise RuntimeError(f"sandbox read unavailable: HTTP {error.code}") from error
    if len(raw_response) > 2_000_000:
        raise ValueError("sandbox API response is oversized")
    envelope = json.loads(raw_response)
    if not isinstance(envelope, dict) or envelope.get("encoding") != "base64":
        raise ValueError("sandbox document encoding changed")
    raw = base64.b64decode(envelope["content"].replace("\n", ""), validate=True)
    if len(raw) > 1_000_000:
        raise ValueError("sandbox document is oversized")
    parsed = json.loads(raw)
    if (not isinstance(parsed, dict) or parsed.get("schema") != DOCUMENT_SCHEMA or parsed.get("journalRef") != ref
            or not isinstance(parsed.get("entries"), dict)
            or not isinstance(parsed.get("effects"), dict)):
        raise ValueError("sandbox document binding changed")
    return parsed


def main() -> int:
    if len(sys.argv) != 3 or sys.argv[1] not in ("absent", "pending-effect", "complete"):
        print("usage: v2-ci-ordinary-rehearsal-readback.py absent|pending-effect|complete RECEIPT_PATH", file=sys.stderr)
        return 2
    try:
        receipt = json.loads(pathlib.Path(sys.argv[2]).read_bytes())
        digest, ref = address(receipt)
        require_public_repository()
        current = document(digest, ref)
        if sys.argv[1] == "absent":
            if current is not None:
                raise ValueError("rehearsal aggregate is already present")
        else:
            if current is None or len(current["entries"]) != 1:
                raise ValueError("rehearsal aggregate entry is absent or ambiguous")
            operation, entry = next(iter(current["entries"].items()))
            if entry.get("operationId") != operation:
                raise ValueError("rehearsal operation identity differs")
            if sys.argv[1] == "pending-effect":
                if entry.get("stage") != "effect-pending" or set(current["effects"]) != {operation}:
                    raise ValueError("unknown-reply effect was not durable")
            elif (entry.get("stage") != "complete"
                  or current["effects"].get(operation) != entry.get("receiptDigest")):
                raise ValueError("rehearsal receipt did not settle")
        print(json.dumps({"status": sys.argv[1], "aggregateDigest": digest, "journalRef": ref}, sort_keys=True))
        return 0
    except (OSError, ValueError, KeyError, TypeError, RuntimeError, binascii.Error) as error:
        print(f"isolated rehearsal readback refused: {error}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())

#!/usr/bin/env python3
"""Reject unverified ``sourcesDigest`` fields in ship verdicts.

``readiness/*/ship-verdict.json`` is committed evidence.  A digest is accepted
only when this checkout can independently recompute it from the sibling
``ship.json`` source inventory using the producer's canonical path/digest
pre-image.  Legacy verdicts without a digest remain valid; stale, malformed, or
orphaned digests fail closed (.github#2208, .github#2738).

Exit: 0 clean; 1 forbidden field present; 3 no verdict (missing or invalid
subject).  This gate is static, so retryable no-verdict is impossible.

``--fix`` remains a compatibility repair for an invalid legacy digest: it
strips the field and then runs the same verifier.  CI never mutates evidence.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib.gate import ExitCode, GateError, run  # noqa: E402


def verdict_files(root: Path) -> list[Path]:
    readiness = root / "readiness"
    if not readiness.is_dir():
        raise GateError(f"{readiness}: no readiness directory to audit")
    files = sorted(readiness.glob("*/ship-verdict.json"))
    if not files:
        raise GateError(f"{readiness}: no ship-verdict.json files to audit")
    return files


def load_verdict(path: Path, relative: Path) -> dict:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except OSError as e:
        raise GateError(f"{relative}: cannot read subject — {e}") from e
    except json.JSONDecodeError as e:
        raise GateError(f"{relative}: invalid JSON — {e}") from e
    if not isinstance(document, dict):
        raise GateError(f"{relative}: top-level JSON is not an object")
    return document


def strip_sources_digest(path: Path, relative: Path) -> bool:
    """Remove ``sourcesDigest`` from one verdict if present. Returns whether it changed.

    Re-serializes with ``json.dumps(..., indent=2)`` and no trailing newline — the same shape
    ``fsgg-sdd ship`` itself writes — so popping the one forbidden key is the only byte the diff
    shows; every surviving key keeps its original relative order (dict insertion order, unaffected
    by deleting an unrelated key), matching a hand-stripped file exactly.
    """
    document = load_verdict(path, relative)
    if "sourcesDigest" not in document:
        return False
    del document["sourcesDigest"]
    path.write_text(json.dumps(document, indent=2), encoding="utf-8")
    return True


def verify_sources_digest(path: Path, relative: Path, document: dict) -> str | None:
    """Return a finding when a claimed digest cannot be independently reproduced."""
    claimed = document.get("sourcesDigest")
    if claimed is None:
        return None
    if not isinstance(claimed, dict) or claimed.get("algorithm") != "sha256":
        return f"{relative}: sourcesDigest must be an object using sha256"
    claimed_value = claimed.get("value")
    if not isinstance(claimed_value, str) or re.fullmatch(r"[0-9a-f]{64}", claimed_value) is None:
        return f"{relative}: sourcesDigest.value must be 64 lowercase hexadecimal characters"

    ship_path = path.with_name("ship.json")
    ship_relative = relative.with_name("ship.json")
    if not ship_path.is_file():
        return f"{relative}: sourcesDigest is unverifiable because {ship_relative} is missing"
    ship = load_verdict(ship_path, ship_relative)
    sources = ship.get("sources")
    if not isinstance(sources, list):
        return f"{relative}: sourcesDigest is unverifiable because {ship_relative} has no sources array"

    canonical: list[tuple[str, str]] = []
    for index, source in enumerate(sources):
        if not isinstance(source, dict) or not isinstance(source.get("path"), str):
            return f"{relative}: {ship_relative} sources[{index}] has no string path"
        digest = source.get("digest")
        if digest is None:
            rendered = ""
        elif (
            isinstance(digest, dict)
            and isinstance(digest.get("algorithm"), str)
            and isinstance(digest.get("value"), str)
        ):
            rendered = f"{digest['algorithm']}:{digest['value']}"
        else:
            return f"{relative}: {ship_relative} sources[{index}] has a malformed digest"
        canonical.append((source["path"], rendered))

    preimage = "\n".join(f"{source_path}|{digest}" for source_path, digest in sorted(canonical))
    observed = hashlib.sha256(preimage.encode("utf-8")).hexdigest()
    if observed != claimed_value:
        return (
            f"{relative}: sourcesDigest does not match {ship_relative}; "
            f"claimed {claimed_value}, recomputed {observed}"
        )
    return None


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".", help="repository root (default: .)")
    ap.add_argument(
        "--fix",
        action="store_true",
        help="strip sourcesDigest fields before checking (legacy compatibility repair; CI never uses it)",
    )
    args = ap.parse_args(argv)
    root = Path(args.root)

    if args.fix:
        fixed: list[str] = []
        for path in verdict_files(root):
            relative = path.relative_to(root)
            if strip_sources_digest(path, relative):
                fixed.append(str(relative))
        if fixed:
            print(f"fixed: stripped sourcesDigest from {len(fixed)} verdict(s):")
            for relative in fixed:
                print(f"  {relative}")
        else:
            print("fixed: nothing to strip — no verdict carried sourcesDigest.")

    findings: list[str] = []
    for path in verdict_files(root):
        relative = path.relative_to(root)
        document = load_verdict(path, relative)
        finding = verify_sources_digest(path, relative, document)
        if finding is not None:
            findings.append(finding)

    if findings:
        for finding in findings:
            print(f"::error::ship-verdict-provenance: {finding}", file=sys.stderr)
        print(f"{len(findings)} unverifiable sourcesDigest field(s) found.", file=sys.stderr)
        return int(ExitCode.FINDING)
    print(f"ok: {len(verdict_files(root))} ship verdict(s) carry no unverifiable sourcesDigest.")
    return int(ExitCode.OK)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="ship-verdict-provenance"))

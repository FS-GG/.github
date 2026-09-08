#!/usr/bin/env python3
"""Reject private/raw telemetry from candidate Git index blobs."""

from __future__ import annotations

import argparse
import subprocess

MAX_PUBLIC_EVIDENCE = 64 * 1024
RAW_MARKERS = (
    b'"type":"response_' + b'item"', b'"type": "response_' + b'item"',
    b'"session_' + b'id"', b'"conversation_' + b'id"', b'"encrypted_' + b'content"',
    b'fsgg.telemetry.runtime-usage-' + b'receipt/', b'fsgg.telemetry.lifecycle-' + b'event/',
    b'sqlite format ' + b'3\x00',
    b'"schema":"fsgg.telemetry.' + b'ingest/1"',
    b'"schema": "fsgg.telemetry.' + b'ingest/1"',
)
PRIVATE_SUFFIXES = (
    ".sqlite", ".sqlite3", ".sqlite-wal", ".sqlite-shm", ".sqlite-journal",
    ".sqlite3-wal", ".sqlite3-shm", ".sqlite3-journal", ".ready", ".rejected",
    ".budget-batch", ".budget-ref", "drain.cursor", "writer.lock",
)


def git(repo: str, *args: str, binary: bool = False):
    result = subprocess.run(["git", "-C", repo, *args], capture_output=True, check=False,
                            text=not binary)
    if result.returncode != 0:
        raise RuntimeError("git-command-failed")
    return result.stdout


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=".")
    parser.add_argument("--base", help="inspect committed candidate blobs changed since this revision")
    args = parser.parse_args()
    try:
        diff_args = ["diff", "--name-only", "--diff-filter=ACMR"]
        diff_args += [f"{args.base}..HEAD"] if args.base else ["--cached"]
        paths = git(args.repo, *diff_args).splitlines()
        for path in paths:
            normalized = "/" + path.replace("\\", "/").lower()
            if ("/.codex/sessions/" in normalized or normalized.endswith(".jsonl")
                    or normalized.endswith(PRIVATE_SUFFIXES) or "raw-receipt" in normalized
                    or "receipt-store/" in normalized or "telemetry-inbox/" in normalized
                    or "telemetry-quarantine/" in normalized or "private-export" in normalized
                    or "private-analytics" in normalized or ".ready.rejected" in normalized):
                raise ValueError("unsafe-telemetry-path")
            blob = git(args.repo, "show", f"HEAD:{path}" if args.base else f":{path}", binary=True)
            lowered = blob.lower()
            # The size cap is for checked-in evidence, not implementation source whose module name
            # happens to contain "Telemetry". Raw markers and unsafe suffixes still apply everywhere.
            telemetry_evidence = not normalized.startswith(("/src/", "/tests/")) and any(
                word in normalized for word in ("telemetry", "usage", "receipt"))
            if telemetry_evidence and len(blob) > MAX_PUBLIC_EVIDENCE:
                raise ValueError("telemetry-evidence-too-large")
            if any(marker in lowered for marker in RAW_MARKERS):
                raise ValueError("raw-telemetry-content")
    except (RuntimeError, ValueError) as error:
        print(f"telemetry-git-safety: refused ({error})")
        return 1
    print("telemetry-git-safety: candidate index blobs are bounded public evidence")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

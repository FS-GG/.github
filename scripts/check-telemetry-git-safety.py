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
                    or "raw-receipt" in normalized or "receipt-store/" in normalized):
                raise ValueError("unsafe-telemetry-path")
            blob = git(args.repo, "show", f"HEAD:{path}" if args.base else f":{path}", binary=True)
            lowered = blob.lower()
            telemetry_evidence = any(word in normalized for word in ("telemetry", "usage", "receipt"))
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

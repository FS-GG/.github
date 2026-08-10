#!/usr/bin/env python3
"""Reject unverified ``sourcesDigest`` fields in hand-authored ship verdicts.

``readiness/*/ship-verdict.json`` is committed evidence, not an artifact this
repository can regenerate.  A digest without a local producer and verifier is
an assertion that cannot be kept true.  The supported contract is therefore
absence: a verdict may report its readiness, but it must not claim source
provenance this checkout cannot establish (.github#2208).

Exit: 0 clean; 1 forbidden field present; 3 no verdict (missing or invalid
subject).  This gate is static, so retryable no-verdict is impossible.
"""
from __future__ import annotations

import argparse
import json
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


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".", help="repository root (default: .)")
    args = ap.parse_args(argv)
    root = Path(args.root)
    findings: list[str] = []

    for path in verdict_files(root):
        relative = path.relative_to(root)
        try:
            document = json.loads(path.read_text(encoding="utf-8"))
        except OSError as e:
            raise GateError(f"{relative}: cannot read subject — {e}") from e
        except json.JSONDecodeError as e:
            raise GateError(f"{relative}: invalid JSON — {e}") from e
        if not isinstance(document, dict):
            raise GateError(f"{relative}: top-level JSON is not an object")
        if "sourcesDigest" in document:
            findings.append(
                f"{relative}: contains `sourcesDigest`; this repository has no pinned producer or "
                "verifier for it, so the field is unverifiable provenance. Remove it rather than "
                "carrying a stale digest (.github#2208)."
            )

    if findings:
        for finding in findings:
            print(f"::error::ship-verdict-provenance: {finding}", file=sys.stderr)
        print(f"{len(findings)} unverifiable sourcesDigest field(s) found.", file=sys.stderr)
        return int(ExitCode.FINDING)
    print(f"ok: {len(verdict_files(root))} ship verdict(s) carry no unverifiable sourcesDigest.")
    return int(ExitCode.OK)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="ship-verdict-provenance"))

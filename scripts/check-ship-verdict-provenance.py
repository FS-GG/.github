#!/usr/bin/env python3
"""Reject unverified ``sourcesDigest`` fields in hand-authored ship verdicts.

``readiness/*/ship-verdict.json`` is committed evidence, not an artifact this
repository can regenerate.  A digest without a local producer and verifier is
an assertion that cannot be kept true.  The supported contract is therefore
absence: a verdict may report its readiness, but it must not claim source
provenance this checkout cannot establish (.github#2208).

Exit: 0 clean; 1 forbidden field present; 3 no verdict (missing or invalid
subject).  This gate is static, so retryable no-verdict is impossible.

``--fix`` strips ``sourcesDigest`` from every offending verdict in place and
then re-runs the same check, so the command that repairs the escape and the
command that verifies the repair cannot disagree.  ``fsgg-sdd ship``
unconditionally re-adds the field on every run (.github#2393) — run
``check-ship-verdict-provenance.py --fix`` once afterward instead of hand-
editing the file.  ``--fix`` is never invoked by CI: the workflow calls the
gate bare, so a verdict that reaches CI still carrying the field reds exactly
as before, and repair stays a step a human or agent takes locally, not one a
pipeline performs silently on their behalf.
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


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".", help="repository root (default: .)")
    ap.add_argument(
        "--fix",
        action="store_true",
        help=(
            "strip `sourcesDigest` from every readiness/*/ship-verdict.json that carries it "
            "before checking, so a fresh `fsgg-sdd ship` run's re-added field is repaired by the "
            "same command that verifies it is gone (.github#2393). CI never passes this flag."
        ),
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

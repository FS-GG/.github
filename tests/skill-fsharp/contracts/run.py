#!/usr/bin/env python3
"""Validate the frozen SKILL-FS corpus after the Python oracle retirement."""

from __future__ import annotations

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent

RETIRED_IMPLEMENTATIONS = (
    "work-roadmap/scripts/fsgg_telemetry_defaults.py",
    "work-roadmap/scripts/native_collaboration_usage.py",
    "work-roadmap/scripts/roadmap-telemetry.py",
    "pipeline-preflight/scripts/preflight.py",
)


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> int:
    corpus = json.loads((HERE / "acceptance-corpus.json").read_text(encoding="utf-8"))
    require(corpus["schema"] == "fsgg.skill-python-fsharp-contract/1", "corpus schema changed")
    require(tuple(corpus["implementations"]) == RETIRED_IMPLEMENTATIONS,
            "frozen implementation inventory changed")
    require(len({case["id"] for case in corpus["cases"]}) == len(corpus["cases"]),
            "duplicate case id")

    for relative in RETIRED_IMPLEMENTATIONS:
        for root in (ROOT / ".agents/skills", ROOT / ".claude/skills"):
            require(not (root / relative).exists(), f"retired Python implementation returned: {root / relative}")

    for fixture in (HERE / "fixtures").iterdir():
        if fixture.suffix == ".json":
            json.loads(fixture.read_text(encoding="utf-8"))

    print("SKILL-FS-01.1 frozen corpus and retirement boundary: PASS")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

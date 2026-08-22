#!/usr/bin/env python3
"""Focused contract test for the roadmap-health reporter."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import tempfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/report-roadmap-health.py"
spec = importlib.util.spec_from_file_location("roadmap_health", SCRIPT)
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture() -> dict:
    return {"schema": "fsgg.coord.churn-reading/v1", "window": {"start": "2026-08-14T00:00:00Z", "end": "2026-08-21T00:00:00Z"}, "rowsOpened": 9, "rowsClosed": 10}


def main() -> None:
    reading = module.report(fixture(), ROOT, None)
    assert reading["schema"] == "fsgg.coord.roadmap-health/v1"
    assert [row["id"] for row in reading["measures"]] == [item[0] for item in module.MEASURES]
    assert reading["measures"][0]["verdict"] == "unverified"
    assert all("verdict" in row and "reason" in row for row in reading["measures"])

    cli = subprocess.run(
        ["python3", str(SCRIPT), "--fixture", str(ROOT / "tests/FS.GG.Coord.Core.Tests/fixtures/churn-readings/worked-2026-08-15.json"), "--format", "json"],
        text=True, capture_output=True, check=True,
    )
    assert len(json.loads(cli.stdout)["measures"]) == 7

    with tempfile.TemporaryDirectory() as directory:
        broken = Path(directory) / "broken.json"
        broken.write_text(json.dumps({"schema": "fsgg.coord.churn-reading/v1", "window": {}}))
        try:
            module.read_fixture(broken)
        except ValueError as error:
            assert "rowsOpened" in str(error)
        else:
            raise AssertionError("an incomplete fixture must fail closed")
    print("report-roadmap-health: complete inventory and unreadable input fail closed")


if __name__ == "__main__":
    main()

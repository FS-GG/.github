#!/usr/bin/env python3
"""Run the work-roadmap driver's packaged telemetry adapter."""

from __future__ import annotations

import pathlib
import runpy
import sys


if __name__ == "__main__":
    adapter = pathlib.Path(__file__).resolve().parents[1] / ".claude" / "skills" / "work-roadmap" / "scripts" / "roadmap-telemetry.py"
    sys.path.insert(0, str(adapter.parent))
    runpy.run_path(str(adapter), run_name="__main__")

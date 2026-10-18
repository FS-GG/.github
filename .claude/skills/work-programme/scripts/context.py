#!/usr/bin/env python3
"""Historical helper-path compatibility; the canonical owner is work-board."""
import runpy
from pathlib import Path
runpy.run_path(str(Path(__file__).resolve().parents[2] / "work-board/scripts/context.py"), run_name="__main__")

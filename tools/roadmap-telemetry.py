#!/usr/bin/env python3
"""Compatibility launcher for the packaged F# roadmap telemetry adapter."""

from __future__ import annotations

import os
import sys


if __name__ == "__main__":
    try:
        os.execvp(
            "fsgg-coord-engine",
            ["fsgg-coord-engine", "skill", "roadmap-telemetry", *sys.argv[1:]],
        )
    except OSError:
        print("fsgg roadmap telemetry: installed coordination engine is unavailable", file=sys.stderr)
        raise SystemExit(2)

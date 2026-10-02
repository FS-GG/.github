#!/usr/bin/env python3
"""Compatibility entry point; F# owns offline import validation and plan decisions."""
from pathlib import Path
import subprocess
import sys

if __name__ == "__main__":
    project = Path(__file__).resolve().parent / "BoardV2Import" / "BoardV2Import.fsproj"
    raise SystemExit(subprocess.run(["dotnet", "run", "--project", str(project), "--no-launch-profile", "--", *sys.argv[1:]]).returncode)

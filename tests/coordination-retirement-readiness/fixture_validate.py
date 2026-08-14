#!/usr/bin/env python3
"""Test-only direct validator harness; it cannot emit production CLI acceptance."""
import importlib.util
import json
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location("readiness", sys.argv[1])
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)
document = json.loads(Path(sys.argv[2]).read_text())
observed = json.loads(Path(sys.argv[3]).read_text())
failures = module.validate(document, Path(sys.argv[4])) + module.validate_live(document, observed)
for failure in failures:
    print(f"BLOCKED: {failure}")
raise SystemExit(1 if failures else 0)

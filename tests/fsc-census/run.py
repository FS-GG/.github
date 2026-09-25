#!/usr/bin/env python3
"""Focused read-only census controls; run with PyYAML installed."""

import importlib.util
import subprocess
import tempfile
from pathlib import Path

source = Path(__file__).resolve().parents[2] / "scripts/fsc-census.py"
spec = importlib.util.spec_from_file_location("fsc_census", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

with tempfile.TemporaryDirectory() as folder:
    root = Path(folder)
    subprocess.run(["git", "init", "-q", str(root)], check=True)
    (root / "scripts").mkdir()
    (root / ".github/workflows").mkdir(parents=True)
    (root / "scripts/tool").write_text("#!/usr/bin/env bash\necho okay\n")
    (root / "scripts/unused.py").write_text("#!/usr/bin/env python3\n")
    (root / "scripts/untracked.py").write_text("#!/usr/bin/env python3\n")
    (root / ".github/workflows/check.yml").write_text(
        "name: check\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n"
        "    steps:\n      # run: scripts/unused.py\n"
        "      - uses: actions/checkout@v7\n"
        "      - run: bash scripts/tool\n"
    )
    subprocess.run(["git", "-C", str(root), "add", "scripts/tool", "scripts/unused.py",
                    ".github/workflows/check.yml"], check=True)
    subprocess.run(["git", "-C", str(root), "-c", "user.name=FSC Fixture",
                    "-c", "user.email=fsc@example.invalid", "commit", "-qm", "fixture"], check=True)
    observed = module.census(root)
    assert {s["path"] for s in observed["scripts"]} == {"scripts/tool", "scripts/unused.py"}
    assert observed["workflow_steps"][0]["targets"] == ["scripts/tool"]
    assert len(observed["workflow_action_refs"]) == 1
    assert all(s["path"] != "scripts/untracked.py" for s in observed["scripts"])
    assert module.script_kind("bin/tool", "not a shebang", True) == "executable-unknown"
    (root / ".github/workflows/check.yml").write_text("jobs: [broken]\n")
    invalid = module.census(root)
    assert invalid["workflow_steps"] == [{"workflow": ".github/workflows/check.yml", "error": "jobs is not a mapping"}]
print("fsc-census controls: 6 passed")

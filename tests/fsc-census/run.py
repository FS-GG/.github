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
    (root / "template/assets").mkdir(parents=True)
    (root / ".config").mkdir()
    (root / ".github/workflows").mkdir(parents=True)
    (root / ".github/actions/setup").mkdir(parents=True)
    (root / "scripts/tool").write_text("#!/usr/bin/env bash\necho okay\n")
    (root / "scripts/unused.py").write_text("#!/usr/bin/env python3\n")
    (root / "scripts/untracked.py").write_text("#!/usr/bin/env python3\n")
    (root / "template/assets/generated-tool.py").write_text("#!/usr/bin/env python3\n")
    (root / ".config/dotnet-tools.json").write_text('{"tools":{"fs.gg.coord.cli":{"version":"0.91.4"}}}')
    (root / ".github/workflows/check.yml").write_text(
        "name: check\non: push\njobs:\n  test:\n    runs-on: ubuntu-latest\n"
        "    steps:\n      # run: scripts/unused.py\n"
        "      - uses: actions/checkout@v7\n"
        "      - run: bash scripts/tool\n"
        "      - shell: bash\n        run: |\n          python3 - <<'PY'\n          print('inline')\n          PY\n"
    )
    (root / ".github/actions/setup/action.yml").write_text(
        "name: setup\nruns:\n  using: composite\n  steps:\n"
        "    - shell: bash\n      run: |\n        python3 - <<'PY'\n        print('inline')\n        PY\n"
        "    - shell: bash\n      run: bash scripts/tool\n"
    )
    subprocess.run(["git", "-C", str(root), "add", "scripts/tool", "scripts/unused.py",
                    "template/assets/generated-tool.py", ".config/dotnet-tools.json",
                    ".github/workflows/check.yml", ".github/actions/setup/action.yml"], check=True)
    subprocess.run(["git", "-C", str(root), "-c", "user.name=FSC Fixture",
                    "-c", "user.email=fsc@example.invalid", "commit", "-qm", "fixture"], check=True)
    observed = module.census(root)
    assert {s["path"] for s in observed["scripts"]} == {
        "scripts/tool", "scripts/unused.py", "template/assets/generated-tool.py"}
    assert observed["workflow_steps"][0]["targets"] == ["scripts/tool"]
    assert observed["workflow_steps"][1]["interpreter_hints"] == ["python", "shell"]
    assert len(observed["workflow_action_refs"]) == 1
    assert len(observed["action_steps"]) == 2
    assert observed["action_steps"][0]["interpreter_hints"] == ["python", "shell"]
    assert observed["action_steps"][1]["targets"] == ["scripts/tool"]
    assert observed["dotnet_tool_pins"] == {"fs.gg.coord.cli": "0.91.4"}
    assert all(s["path"] != "scripts/untracked.py" for s in observed["scripts"])
    assert module.script_kind("bin/tool", "not a shebang", True) == "executable-unknown"
    (root / ".github/workflows/check.yml").write_text("jobs: [broken]\n")
    invalid = module.census(root)
    assert invalid["workflow_steps"] == [{"workflow": ".github/workflows/check.yml", "error": "jobs is not a mapping"}]
    (root / ".github/actions/setup/action.yml").write_text("runs: [broken]\n")
    invalid_action = module.census(root)
    assert ".github/actions/setup/action.yml: action runs is not a mapping" in invalid_action["issues"]
    (root / ".github/actions/setup/action.yml").write_text(
        "runs:\n  using: composite\n  steps:\n    - shell: []\n      run: python3 -V\n")
    invalid_shell = module.census(root)
    assert ".github/actions/setup/action.yml: composite action run shell is not a scalar" in invalid_shell["issues"]
print("fsc-census controls: 13 passed")

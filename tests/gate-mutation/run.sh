#!/usr/bin/env bash
# Negative control for the mutation harness (.github#1810; spec in #1808).
#
# Runs tests/gate-mutation/selftest.py, which drives scripts/lib/mutation.py against a synthetic gate
# whose behaviour is known by construction and demands every verdict the harness can return —
# including DECORATIVE, which is the only leg that proves the harness can say NO at all.
#
# It also runs the SPEC VALIDATION over the shipped corpus, so a spec that could not yield a
# measurement (an anchor produced by the file the mutation edits — #1794) is refused here rather than
# discovered mid-sweep.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

python3 "$HERE/selftest.py"

# The shipped corpus must LOAD. This is cheap and it is not redundant with the sweep: `gate-mutate.py`
# runs the corpus for minutes, and a spec defect that only surfaces at load time should surface in
# seconds, on every PR that edits the corpus.
python3 - "$REPO_ROOT" <<'PY'
import sys
from pathlib import Path
root = Path(sys.argv[1])
sys.path.insert(0, str(root / "scripts"))
from lib.mutation import load_specs
specs = load_specs(root / "tests/gate-mutation/specs.yml", root)
print(f"gate-mutation corpus — {len(specs)} spec(s) load and validate")
PY

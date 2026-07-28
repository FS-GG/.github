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
# THE FLEET CORPORA ARE VALIDATED HERE TOO, AND THEY CANNOT BE SWEPT HERE (.github#1830). Their
# targets are paths in OTHER repositories — `.agents/skills`, a receiver project — so a sweep against
# this root would report `NOT_MEASURED: mutation target does not exist` for every leg and turn this
# repo's clean exit 0 into a 3. Loading is a different question from running: it is pure, it is
# instant, and it is the half that catches a spec which could never yield a measurement. Skipping it
# because "those legs run elsewhere" is how the fleet corpus rots unobserved.
python3 - "$REPO_ROOT" <<'PY'
import sys
from pathlib import Path
root = Path(sys.argv[1])
sys.path.insert(0, str(root / "scripts"))
from lib.mutation import load_specs
specs = load_specs(root / "tests/gate-mutation/specs.yml", root)
print(f"gate-mutation corpus — {len(specs)} spec(s) load and validate")

fleet_dir = root / "tests/gate-mutation/specs-fleet"
fleet = sorted(fleet_dir.glob("*.yml"))
if not fleet:
    # An empty corpus is not a clean sweep (#266), and a glob that silently matches nothing is how a
    # renamed directory turns into a passing check that validates zero files.
    sys.exit(f"::error::no fleet corpora found under {fleet_dir} — a corpus that vanished is not a corpus that passed")
for path in fleet:
    n = len(load_specs(path, root))
    print(f"gate-mutation fleet corpus — {path.name}: {n} spec(s) load and validate")
PY

#!/usr/bin/env bash
# Behavioral fixture for the unified semantic skill-quality gate (.github#1415).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-quality-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# Prove the checked-in catalog passes through the exact local/CI entry point first.
"$ROOT/scripts/check-skill-quality"
python3 "$ROOT/tests/skill-quality/driver-feedback-delivery.py"

ENGINE="$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
"$ENGINE" command-contract --json >"$WORK/contract.json"

pass=0
fail=0

seed() {
  rm -rf "$WORK/tree"
  mkdir -p "$WORK/tree/scripts" "$WORK/tree/tests"
  cp -a "$ROOT/.claude" "$ROOT/.agents" "$WORK/tree/"
  cp -a "$ROOT/.github" "$ROOT/docs" "$ROOT/profile" "$WORK/tree/"
  cp -a "$ROOT/registry" "$WORK/tree/"
  cp "$ROOT/default.json" "$WORK/tree/"
  cp -a "$ROOT/tests/skill-registry" "$ROOT/tests/skill-quality" "$WORK/tree/tests/"
  cp "$ROOT/scripts/generate-driver-manifest" "$WORK/tree/scripts/"
  cp "$ROOT/scripts/generate-projections" "$WORK/tree/scripts/"
  mkdir -p "$WORK/tree/src/FS.GG.Coord.Cli/bin/Release"
  cp -a "$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0" \
    "$WORK/tree/src/FS.GG.Coord.Cli/bin/Release/"
}

expect_rejection() {
  local label="$1" evidence="$2" contract="${3:-$WORK/contract.json}"
  local rc=0
  python3 "$ROOT/scripts/check-skill-quality.py" \
    --root "$WORK/tree" --contract "$contract" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted exit 1 containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
}

mutate_workboard_phrase() {
  python3 - "$WORK/tree" "$1" "$2" <<'PY'
import sys
from pathlib import Path

root, old, new = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/work-board/references/backlog-triage.md"
    text = path.read_text()
    if old not in text:
        raise SystemExit(f"fixture phrase missing: {old}")
    path.write_text(text.replace(old, new))
PY
}

seed
printf '\n```sh\nscripts/fsgg-coord widen .github#1 --apply\n```\n' \
  >>"$WORK/tree/docs/coordination/semantic-regression.md"
expect_rejection "documented option must belong to its verb" "--apply is not a flag of widen"

seed
printf '\n[missing reference](references/does-not-exist.md)\n' \
  >>"$WORK/tree/.claude/skills/check-board/SKILL.md"
expect_rejection "broken relative links fail before distribution" "broken relative link"

seed
python3 - "$WORK/tree/tests/skill-quality/forward-triggers.json" <<'PY'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
doc = json.loads(path.read_text())
doc["cases"] = doc["cases"][:-1]
path.write_text(json.dumps(doc))
PY
expect_rejection "a missing forward trigger class is rejected" "forward trigger classes differ"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/drive-board/references/backlog-triage.md"
    path.write_text(path.read_text().replace("An empty Ready batch is not completion", "A dry wave may stop"))
PY
expect_rejection "drive-board cannot terminate over actionable backlog" \
  "backlog planning contract lost 'An empty Ready batch is not completion'"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/p-add/SKILL.md"
    path.write_text(path.read_text().replace("reuse an existing matching issue", "always create a new issue"))
PY
expect_rejection "p-add cannot duplicate an existing matching issue" \
  "p-add: filing contract lost 'reuse an existing matching issue'"

seed
python3 - "$WORK/tree" <<'PY'
import sys
from pathlib import Path

root = Path(sys.argv[1])
for runtime in (".claude", ".agents"):
    path = root / runtime / "skills/padd-item/SKILL.md"
    path.write_text(path.read_text().replace("Never silently fall back", "Silently fall back"))
PY
expect_rejection "padd-item cannot fall back from the workspace's configured board" \
  "padd-item: workspace filing contract lost 'Never silently fall back'"

seed
mutate_workboard_phrase "without mutating the board" "after attempting a board write"
expect_rejection "work-board missing wiring fails without mutation" \
  "work-board: backlog planning contract lost 'without mutating the board'"

seed
mutate_workboard_phrase "Promotion changes eligibility, not assignment" "Promotion assigns the chosen item"
expect_rejection "work-board promotion remains scheduler-selected and collision-safe" \
  "work-board: backlog planning contract lost 'Promotion changes eligibility, not assignment'"

seed
mutate_workboard_phrase "follow-up filed by the preceding wave" "follow-up already present at startup"
expect_rejection "work-board sees worker-filed backlog on the next wave" \
  "work-board: backlog planning contract lost 'follow-up filed by the preceding wave'"

seed
mutate_workboard_phrase "An empty Ready batch is not completion" "An empty Ready batch is completion"
expect_rejection "work-board cannot terminate over actionable backlog" \
  "work-board: backlog planning contract lost 'An empty Ready batch is not completion'"

seed
mutate_workboard_phrase "Do not spin" "Poll the same rows until they change"
expect_rejection "work-board reports parked and human backlog without spinning" \
  "work-board: backlog planning contract lost 'Do not spin'"

# .github#1574: a schema id the semantic gate does not support must be an ERROR from the semantic
# gate itself. Its sibling validate_invocations already shouts, and that shout is repairable by
# editing one literal — which is exactly how the polarity assertions got switched off unnoticed.
seed
python3 - "$WORK/contract.json" "$WORK/contract-unsupported-schema.json" <<'PY'
import json
import sys
from pathlib import Path

source, target = Path(sys.argv[1]), Path(sys.argv[2])
doc = json.loads(source.read_text())
doc["schema"] = "fsgg.coord.commands/2"
target.write_text(json.dumps(doc))
PY
expect_rejection "an unsupported contract schema cannot silently disarm the polarity gate" \
  "semantic polarity gate cannot run" "$WORK/contract-unsupported-schema.json"

seed
sed -i '/GENERATED: fsgg-versions/{n;s/^/STALE /;}' "$WORK/tree/docs/architecture.md"
rc=0
bash "$WORK/tree/scripts/generate-projections" --check >"$WORK/out" 2>&1 || rc=$?
if [ "$rc" -eq 1 ] && grep -Fq -- "STALE" "$WORK/out"; then
  echo "PASS  stale generated board/version truth is rejected"
  pass=$((pass+1))
else
  echo "FAIL  stale generated board/version truth was not rejected (exit $rc)" >&2
  sed 's/^/    | /' "$WORK/out" >&2
  fail=$((fail+1))
fi

if [ "$fail" -ne 0 ]; then
  echo "skill-quality fixture: $fail failure(s), $pass pass(es)" >&2
  exit 1
fi
echo "skill-quality fixture: all $pass rejection cases passed"

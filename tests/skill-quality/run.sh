#!/usr/bin/env bash
# Behavioral fixture for the unified semantic skill-quality gate (.github#1415).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-quality-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# Prove the checked-in catalog passes through the exact local/CI entry point first.
"$ROOT/scripts/check-skill-quality"

ENGINE="$ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
"$ENGINE" command-contract --json >"$WORK/contract.json"

pass=0
fail=0

seed() {
  rm -rf "$WORK/tree"
  mkdir -p "$WORK/tree/scripts" "$WORK/tree/tests"
  cp -a "$ROOT/.claude" "$ROOT/.codex" "$ROOT/.agents" "$WORK/tree/"
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
  local label="$1" evidence="$2"
  local rc=0
  python3 "$ROOT/scripts/check-skill-quality.py" \
    --root "$WORK/tree" --contract "$WORK/contract.json" >"$WORK/out" 2>&1 || rc=$?
  if [ "$rc" -eq 1 ] && grep -Fq -- "$evidence" "$WORK/out"; then
    echo "PASS  $label"
    pass=$((pass+1))
  else
    echo "FAIL  $label (wanted exit 1 containing: $evidence; got exit $rc)" >&2
    sed 's/^/    | /' "$WORK/out" >&2
    fail=$((fail+1))
  fi
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

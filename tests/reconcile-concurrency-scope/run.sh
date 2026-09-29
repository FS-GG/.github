#!/usr/bin/env bash
# Fault fixture for the exact ADR-0091 retirement shape of coord-board-reconcile.yml.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-reconcile-concurrency-scope.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/reconcile-retirement-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok() { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -z "${2:-}" ] || printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

expect() {
  local name="$1" want="$2" needle="$3" root="$4" out rc=0
  out="$(python3 "$TOOL" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then bad "$name (exit $rc, want $want)" "$out"
  elif ! grep -qF "$needle" <<<"$out"; then bad "$name (missing reason: $needle)" "$out"
  else ok "$name"; fi
}

copy_subject() {
  local root="$1"
  mkdir -p "$root/.github/workflows"
  cp "$REPO_ROOT/.github/workflows/coord-board-reconcile.yml" "$root/.github/workflows/coord-board-reconcile.yml"
}

expect "REGRESSION: real workflow has the exact retired diagnostic shape" 0 "OK — manual-only" "$REPO_ROOT"

AUTO="$WORK/automatic"; copy_subject "$AUTO"
python3 - "$AUTO/.github/workflows/coord-board-reconcile.yml" <<'PY'
from pathlib import Path
p=Path(__import__('sys').argv[1]); s=p.read_text(); p.write_text(s.replace("on:\n  workflow_dispatch:\n", "on:\n  workflow_dispatch:\n  schedule:\n    - cron: '17 * * * *'\n"))
PY
expect "NEGATIVE: automatic schedule trigger is refused" 1 "automatic trigger reintroduced" "$AUTO"

APP="$WORK/app-token"; copy_subject "$APP"
python3 - "$APP/.github/workflows/coord-board-reconcile.yml" <<'PY'
from pathlib import Path
p=Path(__import__('sys').argv[1]); s=p.read_text(); p.write_text(s.replace("      - name: Report the retired boundary\n", "      - uses: actions/create-github-app-token@v3\n        with:\n          app-id: ${{ secrets.APP_ID }}\n      - name: Report the retired boundary\n"))
PY
expect "NEGATIVE: App credential mint is refused" 1 "credential/action" "$APP"

WRITE="$WORK/mutation"; copy_subject "$WRITE"
python3 - "$WRITE/.github/workflows/coord-board-reconcile.yml" <<'PY'
from pathlib import Path
p=Path(__import__('sys').argv[1]); s=p.read_text(); p.write_text(s.replace('run: echo "retired under ADR-0091; board state not evaluated; V2 projection writer not activated"', 'run: scripts/fsgg-coord reconcile --apply'))
PY
expect "NEGATIVE: legacy board mutation is refused" 1 "board mutation" "$WRITE"

CLEAN="$WORK/false-clean"; copy_subject "$CLEAN"
python3 - "$CLEAN/.github/workflows/coord-board-reconcile.yml" <<'PY'
from pathlib import Path
p=Path(__import__('sys').argv[1]); s=p.read_text(); p.write_text(s.replace("board state not evaluated", "board clean"))
PY
expect "NEGATIVE: false board-clean verdict is refused" 1 "changed verdict" "$CLEAN"

LEGACY="$WORK/historical-active-writer"; mkdir -p "$LEGACY/.github/workflows"
printf '%s\n' \
  'name: historical active writer' \
  'on: { pull_request: {}, schedule: [{ cron: "17 * * * *" }] }' \
  'permissions: { contents: read }' \
  'jobs:' \
  '  reconcile:' \
  '    runs-on: ubuntu-latest' \
  '    steps:' \
  '      - uses: actions/create-github-app-token@v3' \
  '      - run: scripts/fsgg-coord reconcile --apply' \
  > "$LEGACY/.github/workflows/coord-board-reconcile.yml"
expect "HISTORICAL NEGATIVE: active v1 writer remains refused" 1 "automatic trigger reintroduced" "$LEGACY"

MISSING="$WORK/missing"; mkdir -p "$MISSING"
expect "NO VERDICT: missing workflow is distinct from a clean retirement" 3 "NO VERDICT" "$MISSING"

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1

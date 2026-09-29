#!/usr/bin/env bash
# The active writer and its REST-budget preflight were retired together under ADR-0091. This fixture
# now proves the shipped diagnostic has no REST/budget path and that restoring the historical
# budget-bearing writer is rejected. It is an explicit retirement test, not a skipped old fixture.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
WF="$REPO_ROOT/.github/workflows/coord-board-reconcile.yml"
TOOL="$REPO_ROOT/scripts/check-reconcile-concurrency-scope.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/rest-budget-retirement-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok() { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -z "${2:-}" ] || printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

if python3 "$TOOL" --root "$REPO_ROOT" >/dev/null; then
  ok "R1  the active workflow satisfies the exact retired shape"
else
  bad "R1  the active workflow satisfies the exact retired shape"
fi

if grep -Eq '(^|[[:space:]])id:[[:space:]]*budget|curl |gh api|/rate_limit|x-ratelimit' "$WF"; then
  bad "R2  no active REST-budget step remains"
else
  ok "R2  no active REST-budget step remains"
fi
if grep -Eq '^[[:space:]]*-[[:space:]]+uses:' "$WF"; then
  bad "R3  no action can load credentials or source"
else
  ok "R3  no action can load credentials or source"
fi
if grep -Eqi 'secrets\.|github_token|create-github-app-token' "$WF"; then
  bad "R4  no credential reference remains"
else
  ok "R4  no credential reference remains"
fi

# Historical negative: reintroduce the old interaction family—automatic schedule, budget REST read,
# App credential, and mutation—in one synthetic workflow. The shared retirement checker must refuse
# it before anyone could mistake the old preflight's defer result for authority to write.
ROOT="$WORK/historical-writer"; mkdir -p "$ROOT/.github/workflows"
printf '%s\n' \
  'name: historical board writer' \
  'on: { schedule: [{ cron: "17 * * * *" }], workflow_dispatch: {} }' \
  'permissions: { contents: read }' \
  'jobs:' \
  '  reconcile:' \
  '    runs-on: ubuntu-latest' \
  '    steps:' \
  '      - uses: actions/create-github-app-token@v3' \
  '      - id: budget' \
  '        run: curl -D - https://api.github.com/repos/FS-GG/.github' \
  '      - run: scripts/fsgg-coord reconcile --apply' \
  > "$ROOT/.github/workflows/coord-board-reconcile.yml"

out="$(python3 "$TOOL" --root "$ROOT" 2>&1)" && rc=0 || rc=$?
if [ "$rc" -eq 1 ] && grep -qF "automatic trigger reintroduced" <<<"$out" && grep -qF "board mutation" <<<"$out"; then
  ok "R5  historical automatic budget-bearing writer is refused"
else
  bad "R5  historical automatic budget-bearing writer is refused" "exit=$rc; $out"
fi

echo
echo "-- $pass passed, $failcount failed --"
[ "$failcount" -eq 0 ] || exit 1

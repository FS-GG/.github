#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORKFLOW="$ROOT/.github/workflows/routine-eligibility.yml"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/routine-eligibility.XXXXXX")"
trap 'rm -rf "$TMP"' EXIT

require() {
  grep -qF "$1" "$WORKFLOW" || { echo "missing trusted-workflow invariant: $1" >&2; exit 1; }
}

require 'pull_request_target:'
require 'contents: read'
require 'name: routine-eligibility'
require 'git -C "$inspect" show "$BASE_SHA:scripts/check-claim-generation.py"'
require 'git -C "$inspect" show "$BASE_SHA:scripts/lib/gate.py"'
require 'refs/pull/${PR_NUMBER}/head:refs/remotes/origin/routine-head'
require 'bash tests/routine-eligibility/run.sh'
if grep -qF 'actions/checkout' "$WORKFLOW"; then
  echo "trusted workflow must not check out candidate code" >&2
  exit 1
fi
if grep -qF 'GITHUB_TOKEN' "$WORKFLOW"; then
  echo "trusted workflow must not expose a write-capable token" >&2
  exit 1
fi

# Model a hostile candidate that replaces the validator and deletes its copy of
# the workflow. Exact-base extraction must still execute the base validator.
repo="$TMP/repository"
mkdir -p "$repo/scripts/lib" "$repo/.github/workflows"
git -C "$repo" init -q
git -C "$repo" config user.email fixture@example.invalid
git -C "$repo" config user.name fixture
printf '%s\n' '#!/usr/bin/env python3' 'print("BASE-TRUSTED")' > "$repo/scripts/check-claim-generation.py"
printf '%s\n' '# base dependency' > "$repo/scripts/lib/gate.py"
cp "$WORKFLOW" "$repo/.github/workflows/routine-eligibility.yml"
git -C "$repo" add .
git -C "$repo" commit -qm base
base="$(git -C "$repo" rev-parse HEAD)"

printf '%s\n' '#!/usr/bin/env python3' 'print("CANDIDATE-CONTROLLED")' > "$repo/scripts/check-claim-generation.py"
git -C "$repo" rm -q .github/workflows/routine-eligibility.yml
git -C "$repo" add scripts/check-claim-generation.py
git -C "$repo" commit -qm hostile-candidate

mkdir -p "$TMP/trusted/lib"
git -C "$repo" show "$base:scripts/check-claim-generation.py" > "$TMP/trusted/check-claim-generation.py"
git -C "$repo" show "$base:scripts/lib/gate.py" > "$TMP/trusted/lib/gate.py"
result="$(python3 "$TMP/trusted/check-claim-generation.py")"
test "$result" = BASE-TRUSTED || {
  echo "candidate replacement influenced trusted execution: $result" >&2
  exit 1
}

test ! -e "$repo/.github/workflows/routine-eligibility.yml"
test "$(python3 "$repo/scripts/check-claim-generation.py")" = CANDIDATE-CONTROLLED
echo "routine-eligibility: default-branch workflow and base validator remain outside candidate authority"

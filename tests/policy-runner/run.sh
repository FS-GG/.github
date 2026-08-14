#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/policy"

jq '{schema_version, subjects: [.subjects[0]]}' "$ROOT/policy/subjects.json" >"$WORK/policy/subjects.json"
[ "$(python3 "$ROOT/scripts/policy-runner.py" list --root "$WORK")" = checker-inventory ]

jq '.subjects = []' "$ROOT/policy/subjects.json" >"$WORK/policy/subjects.json"
if python3 "$ROOT/scripts/policy-runner.py" list --root "$WORK"; then
  echo 'empty subject inventory unexpectedly passed' >&2; exit 1
fi

jq '.subjects += [.subjects[0]]' "$ROOT/policy/subjects.json" >"$WORK/policy/subjects.json"
if python3 "$ROOT/scripts/policy-runner.py" list --root "$WORK"; then
  echo 'duplicate policy subject unexpectedly passed' >&2; exit 1
fi
echo 'policy runner fixture: ok'

# The consolidated workflow must trigger when any discovered subject implementation or fixture moves.
workflow="$ROOT/.github/workflows/policy.yml"
for required in 'policy/**' 'scripts/policy-runner.py' 'scripts/evidence-manifest.py' \
                'tests/projection/**' 'tests/policy-runner/**' 'tests/evidence-manifest/**'; do
  [ "$(grep -cF -- "- \"$required\"" "$workflow")" -eq 2 ] || {
    echo "policy workflow does not cover subject dependency in both triggers: $required" >&2; exit 1
  }
done
echo 'policy trigger coverage: ok'

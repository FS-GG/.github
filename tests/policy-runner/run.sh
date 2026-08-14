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

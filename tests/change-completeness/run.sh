#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
pass=0
fail=0
ok() { pass=$((pass + 1)); printf 'PASS  %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf 'FAIL  %s\n' "$1" >&2; }

printf 'docs/readme.md\n' >"$WORK/docs.paths"
printf 'src/FS.GG.Coord.Core/Review.fs\n' >"$WORK/engine.paths"

GITHUB_OUT="$WORK/docs.out" "$ROOT/scripts/change-completeness" \
  --paths-file "$WORK/docs.paths" --github-output "$WORK/docs.out" >"$WORK/docs.log" \
  && ok 'unrelated changes take the bounded non-engine route' || bad 'unrelated route failed'
grep -qx 'engine_changed=false' "$WORK/docs.out" && ok 'unrelated changes do not schedule expensive engine work' || bad 'unrelated impact was misclassified'

GITHUB_OUT="$WORK/engine.out" "$ROOT/scripts/change-completeness" \
  --paths-file "$WORK/engine.paths" --github-output "$WORK/engine.out" >/dev/null \
  && ok 'engine changes run the focused structural route' || bad 'engine route failed'
grep -qx 'engine_changed=true' "$WORK/engine.out" && ok 'engine changes schedule the expensive successor' || bad 'engine impact was misclassified'

# The production runner must name every structural family. These are observable diagnostics, not prose:
# deleting a stage makes this fixture red before a PR can silently stop running that family.
for label in \
  'closing-keyword and commit-message contract' \
  'SDD ship-verdict provenance' \
  'command catalogue, parser, render, write-ness, contract, and help closure' \
  'handler ownership and production registration' \
  'delivery, review, declared-path, and focused production-route parity'; do
  grep -Fq "$label" "$ROOT/scripts/change-completeness" && ok "named stage: $label" || bad "missing named stage: $label"
done

grep -Fq 'needs: change-completeness' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'expensive engine job depends on change-completeness' \
  || bad 'engine job can start before change-completeness'
pull_request_trigger="$(sed -n '/^  pull_request:/,/^  push:/p' "$ROOT/.github/workflows/coord-engine.yml")"
if grep -q 'paths:' <<<"$pull_request_trigger"; then
  bad 'required change-completeness context is path-filtered'
else
  ok 'required change-completeness context reports on every pull-request head'
fi
grep -Fq 'timeout-minutes: 5' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'workflow encodes the five-minute target' \
  || bad 'five-minute target is not encoded'

printf '\nchange-completeness fixture: %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]

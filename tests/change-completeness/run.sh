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
printf '%s\n' \
  'scripts/ci-gate-impact.py' \
  'tests/ci-runtime-optimization/run.sh' \
  'src/FS.GG.Coord.Core/Review.fs' >"$WORK/engine.paths"

GITHUB_OUT="$WORK/docs.out" "$ROOT/scripts/change-completeness" \
  --paths-file "$WORK/docs.paths" --github-output "$WORK/docs.out" >"$WORK/docs.log" \
  && ok 'unrelated changes take the bounded non-engine route' || bad 'unrelated route failed'
grep -qx 'engine_changed=false' "$WORK/docs.out" && ok 'unrelated changes do not schedule expensive engine work' || bad 'unrelated impact was misclassified'

for path in 'scripts/ci-gate-impact.py' 'tests/ci-runtime-optimization/run.sh'; do
  printf '%s\n' "$path" >"$WORK/focused-ci.paths"
  : >"$WORK/focused-ci.out"
  GITHUB_OUT="$WORK/focused-ci.out" "$ROOT/scripts/change-completeness" \
    --paths-file "$WORK/focused-ci.paths" --github-output "$WORK/focused-ci.out" >"$WORK/focused-ci.log" \
    && ok "$path takes the bounded non-engine route" || bad "$path focused route failed"
  grep -qx 'engine_changed=false' "$WORK/focused-ci.out" \
    && ok "$path does not schedule the full engine job" \
    || bad "$path was misclassified"
done

GITHUB_OUT="$WORK/engine.out" "$ROOT/scripts/change-completeness" \
  --paths-file "$WORK/engine.paths" --github-output "$WORK/engine.out" >/dev/null \
  && ok 'mixed focused-CI and engine changes run the focused structural route' || bad 'mixed engine route failed'
grep -qx 'engine_changed=true' "$WORK/engine.out" \
  && ok 'an engine change cannot hide inside a focused CI selector change' \
  || bad 'mixed engine impact was incorrectly skipped'

selector="$(grep -F 'if grep -Eq ' "$ROOT/scripts/change-completeness")"
for required_pattern in \
  'src/FS\.GG\.(Coord|Telemetry)\.' \
  '\.github/workflows/coord-engine\.yml' \
  'Directory\.(Build|Packages)' \
  'dist/dotnet/Directory\.Build\.props' \
  'global\.json'; do
  [[ "$selector" == *"$required_pattern"* ]] \
    && ok "engine selector retains $required_pattern" \
    || bad "engine selector lost $required_pattern"
done

if grep -Fq 'FS.GG.Coord.Cli.Lifecycle.Tests.fsproj' "$ROOT/scripts/change-completeness" \
  && ! grep -Fq 'dotnet test "$ROOT/tests/FS.GG.Coord.Cli.Tests/FS.GG.Coord.Cli.Tests.fsproj" -c Release --no-restore' "$ROOT/scripts/change-completeness"; then
  ok 'focused lifecycle filters execute in the owning Lifecycle assembly'
else
  bad 'focused lifecycle filters still target the residual CLI assembly'
fi
grep -Fq 'read-trx-count.py" "$WORK/lifecycle-focus/lifecycle-focus.trx"' "$ROOT/scripts/change-completeness" \
  && grep -Fq -- '--minimum 1 --label "change-completeness (Lifecycle focused)"' "$ROOT/scripts/change-completeness" \
  && ok 'focused lifecycle selection is guarded against zero-match success' \
  || bad 'focused lifecycle selection can pass vacuously with zero matches'

dotnet test "$ROOT/tests/FS.GG.Coord.Cli.Lifecycle.Tests/FS.GG.Coord.Cli.Lifecycle.Tests.fsproj" \
  -c Release --no-restore --filter FullyQualifiedName~DefinitelyNoLifecycleTestMatches \
  --logger "trx;LogFileName=zero.trx" --results-directory "$WORK/zero" >/dev/null
if python3 "$ROOT/scripts/read-trx-count.py" "$WORK/zero/zero.trx" \
  --minimum 1 --label "change-completeness zero-match mutation" >/dev/null 2>&1; then
  bad 'zero-match mutation passed the non-vacuity guard'
else
  ok 'zero-match mutation reds the non-vacuity guard'
fi

# The production runner must name every structural family. These are observable diagnostics, not prose:
# deleting a stage makes this fixture red before a PR can silently stop running that family.
for label in \
  'closing-keyword and commit-message contract' \
  'SDD ship-verdict provenance' \
  'GS2-08.5 protected source import' \
  'v1 writer census structural closure' \
  'v1 receiver source census offline closure' \
  'GS2-08.6 independent producer fence attacks' \
  'command catalogue, parser, render, write-ness, contract, and help closure' \
  'v1 writer census candidate-built metadata' \
  'handler ownership and production registration' \
  'delivery, review, declared-path, and focused production-route parity'; do
  grep -Fq "$label" "$ROOT/scripts/change-completeness" && ok "named stage: $label" || bad "missing named stage: $label"
done

grep -Fq 'import-coordination-v1-admission.py" --check' "$ROOT/scripts/change-completeness" \
  && ok 'protected admission source import is verified on every change' \
  || bad 'protected admission source import verification is not universal'

grep -Fq 'producer-fence-attacks/selftest.py' "$ROOT/scripts/change-completeness" \
  && grep -Fq 'producer-fence-attacks/probe_legacy.py" --verify' "$ROOT/scripts/change-completeness" \
  && ok 'producer evidence mutation checks and historical probe verification are universal' \
  || bad 'producer evidence hardening is absent from universal validation'

grep -Fq 'check-v1-writer-census.py" --structural' "$ROOT/scripts/change-completeness" \
  && ok 'cheap writer census runs on every change' \
  || bad 'writer census is not universal'
grep -Fq 'check-v1-receiver-census.py' "$ROOT/scripts/change-completeness" \
  && grep -Fq 'tests/v1-receiver-census/run.py' "$ROOT/scripts/change-completeness" \
  && ok 'receiver source census runs offline on every change' \
  || bad 'receiver source census is not universal'
grep -Fq 'check-v1-writer-census.py --candidate' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'engine workflow checks candidate-built writer metadata' \
  || bad 'engine workflow omits candidate-built writer metadata'
grep -Fq 'check-v1-receiver-census.py' "$ROOT/.github/workflows/coord-engine.yml" \
  && grep -Fq 'tests/v1-receiver-census/run.py' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'engine workflow checks receiver source census offline' \
  || bad 'engine workflow omits receiver source census'
release_census_line="$(awk '/check-v1-writer-census.py --candidate/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
release_push_line="$(awk '/dotnet nuget push/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
if [ -n "$release_census_line" ] && [ -n "$release_push_line" ] && [ "$release_census_line" -lt "$release_push_line" ]; then
  ok 'release census gates pack and publish'
else
  bad 'release can publish before writer census'
fi
release_receiver_line="$(awk '/check-v1-receiver-census.py/ { print NR; exit }' "$ROOT/.github/workflows/release-coord-engine.yml")"
if [ -n "$release_receiver_line" ] && [ -n "$release_push_line" ] && [ "$release_receiver_line" -lt "$release_push_line" ]; then
  ok 'release receiver source census gates publish'
else
  bad 'release can publish before receiver source census'
fi

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

focused_fixture_calls="$(awk '
  /^  change-completeness:/ { inside=1; next }
  /^  engine:/ { inside=0 }
  inside && /run: bash tests\/ci-runtime-optimization\/run.sh/ { count++ }
  END { print count+0 }
' "$ROOT/.github/workflows/coord-engine.yml")"
if [ "$focused_fixture_calls" = 1 ]; then
  ok 'required predecessor directly owns exactly one focused CI selector fixture invocation'
else
  bad "required predecessor has $focused_fixture_calls focused CI selector fixture invocations"
fi
full_engine_fixture_calls="$(awk '
  /^  engine:/ { inside=1; next }
  inside && /^  [A-Za-z][A-Za-z0-9_-]*:/ { inside=0 }
  inside && /run: bash tests\/ci-runtime-optimization\/run.sh/ { count++ }
  END { print count+0 }
' "$ROOT/.github/workflows/coord-engine.yml")"
if [ "$full_engine_fixture_calls" = 0 ]; then
  ok 'full engine job does not repeat the focused CI selector fixture'
else
  bad 'full engine job still repeats the focused CI selector fixture'
fi
grep -Fq "if: github.event_name == 'workflow_dispatch' || needs.change-completeness.outputs.engine_changed == 'true'" \
  "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'workflow uses the bounded impact decision for pull requests and pushes' \
  || bad 'workflow can bypass the bounded impact decision outside manual dispatch'
grep -Fq -- '- "scripts/ci-gate-impact.py"' "$ROOT/.github/workflows/coord-engine.yml" \
  && ok 'push trigger reaches the bounded selector check when its implementation changes' \
  || bad 'push trigger omits the CI selector implementation'

# This unfiltered required context independently proves the Q0 job is reachable. A checker
# that only runs inside the checked job cannot detect its own job/trigger being disabled.
if python3 "$ROOT/work/2953-gh-modernization-m0-invariants/verify_q0_workflow.py" \
  "$ROOT/.github/workflows/github-substrate-q0.yml" --self-test; then
  ok 'independent gate proves Q0 live acceptance is trigger-, job-, and step-reachable'
else
  bad 'Q0 live acceptance can be skipped outside its own job'
fi

printf '\nchange-completeness fixture: %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]

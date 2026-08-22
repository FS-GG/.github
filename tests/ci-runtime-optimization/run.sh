#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
pass=0
fail=0

ok() { pass=$((pass + 1)); printf 'PASS: %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf 'FAIL: %s\n' "$1" >&2; }

decision() {
  local gate="$1" paths="$2"
  printf '%s\n' "$paths" >"$WORK/paths"
  python3 "$ROOT/scripts/ci-gate-impact.py" "$gate" --paths-file "$WORK/paths"
}

assert_run() {
  local gate="$1" paths="$2" expected="$3" label="$4" out
  out="$(decision "$gate" "$paths")"
  if python3 -c 'import json,sys; assert json.load(sys.stdin)["run"] is (sys.argv[1] == "true")' "$expected" <<<"$out"; then
    ok "$label"
  else
    bad "$label ($out)"
  fi
}

assert_run signature-doc 'docs/readme.md' false 'signature sweep omits an unrelated docs-only change'
assert_run signature-doc 'src/NewProject/NewProject.fsproj' true 'signature sweep runs for source topology'
assert_run signature-doc 'src/Module/Foo.fsi' true 'signature sweep runs for a new signature sibling'
assert_run signature-doc 'tests/signature-doc-siting/mutants.py' true 'signature sweep runs for its mutation harness'
out="$(python3 "$ROOT/scripts/ci-gate-impact.py" signature-doc --base invalid --head invalid)"
if python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["run"] and d["reason"] == "revision-input-unavailable"' <<<"$out"; then
  ok 'signature classifier fails closed when revisions are unavailable'
else
  bad "signature classifier must fail closed ($out)"
fi

diff_repo="$WORK/diff-repo"
git init -q "$diff_repo"
git -C "$diff_repo" config user.name fixture
git -C "$diff_repo" config user.email fixture@example.invalid
mkdir -p "$diff_repo/src/Fixture"
printf 'module Fixture\n' >"$diff_repo/src/Fixture/Fixture.fs"
git -C "$diff_repo" add .
git -C "$diff_repo" commit -qm base
base_sha="$(git -C "$diff_repo" rev-parse HEAD)"
mkdir -p "$diff_repo/docs"
git -C "$diff_repo" mv src/Fixture/Fixture.fs docs/Fixture.fs
git -C "$diff_repo" commit -qm rename
head_sha="$(git -C "$diff_repo" rev-parse HEAD)"
out="$(python3 "$ROOT/scripts/ci-gate-impact.py" signature-doc --root "$diff_repo" --base "$base_sha" --head "$head_sha")"
if python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["run"] and "src/Fixture/Fixture.fs" in d["matched"]' <<<"$out"; then
  ok 'signature classifier treats a rename out of its subject as affected'
else
  bad "signature classifier lost the deleted side of a rename ($out)"
fi

assert_run shell-fixture 'docs/readme.md' false 'shell fixture omits an unrelated docs-only change'
assert_run shell-fixture 'scripts/lint-shell.sh' true 'shell fixture runs for its live checker contract'
assert_run shell-fixture '.github/workflows/shell-lint.yml' true 'shell fixture runs for workflow wiring'
assert_run shell-fixture 'scripts/lib/extract-workflow-shell.py' true 'shell fixture runs for workflow-shell extraction'

trx() {
  local path="$1" attrs="$2"
  printf '%s\n' '<?xml version="1.0"?><TestRun><ResultSummary><Counters '"$attrs"' /></ResultSummary></TestRun>' >"$path"
}
trx "$WORK/pass.trx" 'total="30" executed="30" passed="30" failed="0" error="0"'
python3 "$ROOT/scripts/read-trx-count.py" "$WORK/pass.trx" --minimum 20 --label sample >/dev/null \
  && ok 'TRX parser accepts one above-floor successful execution' \
  || bad 'TRX parser rejected valid evidence'

for case in zero below failed malformed duplicate missing; do
  case "$case" in
    zero) trx "$WORK/$case.trx" 'total="0" executed="0" passed="0" failed="0" error="0"' ;;
    below) trx "$WORK/$case.trx" 'total="3" executed="3" passed="3" failed="0" error="0"' ;;
    failed) trx "$WORK/$case.trx" 'total="30" executed="30" passed="29" failed="1" error="0"' ;;
    malformed) printf '<TestRun><ResultSummary>' >"$WORK/$case.trx" ;;
    duplicate) printf '%s\n' '<TestRun><Counters total="30" executed="30" passed="30" failed="0" error="0"/><Counters total="30" executed="30" passed="30" failed="0" error="0"/></TestRun>' >"$WORK/$case.trx" ;;
    missing) : ;;
  esac
  if python3 "$ROOT/scripts/read-trx-count.py" "$WORK/$case.trx" --minimum 20 --label sample >/dev/null 2>&1; then
    bad "TRX parser must refuse $case evidence"
  else
    ok "TRX parser refuses $case evidence"
  fi
done

if rg -n 'run_gate "\$REPO_ROOT"' "$ROOT/tests/shell-lint/run.sh" >/dev/null; then
  bad 'shell fixture must not rerun the live repository'
else
  ok 'shell fixture and live-tree verdict are separated'
fi

duplicate_runs="$(rg -c 'out="\$\(dotnet test .*--no-build' "$ROOT/.github/workflows/coord-engine.yml" || true)"
if [ "${duplicate_runs:-0}" = 0 ]; then
  ok 'coord-engine has no second test execution for non-vacuity'
else
  bad "coord-engine still has $duplicate_runs duplicate test executions"
fi

printf '\nci-runtime-optimization: %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]

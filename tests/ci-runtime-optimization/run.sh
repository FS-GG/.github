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

mkdir -p "$diff_repo/src"
printf 'module Hostile\n' >"$diff_repo/"$'src/hostile\nrun=false'
git -C "$diff_repo" add .
git -C "$diff_repo" commit -qm 'add newline path'
hostile_sha="$(git -C "$diff_repo" rev-parse HEAD)"
python3 "$ROOT/scripts/ci-gate-impact.py" signature-doc --root "$diff_repo" \
  --base "$head_sha" --head "$hostile_sha" --github-output "$WORK/github-output" >"$WORK/decision.json"
if python3 - "$WORK/github-output" "$WORK/decision.json" <<'PY'
import json
import pathlib
import sys

lines = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines()
decision = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding="utf-8"))
assert len(lines) == 3, lines
assert lines[0] == "run=true", lines
assert lines[1] == "reason=affected-subject", lines
assert json.loads(lines[2].removeprefix("matched=")) == decision["matched"]
assert "src/hostile\nrun=false" in decision["matched"]
PY
then
  ok 'signature classifier encodes newline Git paths in workflow outputs'
else
  bad 'signature classifier allowed a Git path to inject workflow output keys'
fi

assert_run shell-fixture 'docs/readme.md' false 'shell fixture omits an unrelated docs-only change'
assert_run shell-fixture 'scripts/lint-shell.sh' true 'shell fixture runs for its live checker contract'
assert_run shell-fixture '.github/workflows/shell-lint.yml' true 'shell fixture runs for workflow wiring'
assert_run shell-fixture 'scripts/lib/extract-workflow-shell.py' true 'shell fixture runs for workflow-shell extraction'
assert_run shell-fixture 'scripts/lib/select-shellcheck-findings.py' true 'shell fixture runs for structured ShellCheck selection'

analysis_invocations="$(grep -Ec '"\$SHELLCHECK" .* -f json1' "$ROOT/scripts/lint-shell.sh" || true)"
if [ "$analysis_invocations" = 2 ]; then
  ok 'shell lint has exactly one structured analysis per file/workflow projection'
else
  bad "shell lint has $analysis_invocations structured ShellCheck analysis sites instead of 2"
fi
if grep -Eq 'SHELL_LINT_MANIFEST|SHELL_LINT_RECEIPT|upload-artifact@043fb46d1a93c77aae656e7c1c64a875d1fc6a0a' \
  "$ROOT/.github/workflows/shell-lint.yml" >/dev/null \
  && grep -Eq 'fsgg.shell-lint-manifest/v1|fsgg.shell-lint-receipt/v1' \
    "$ROOT/scripts/lib/select-shellcheck-findings.py" >/dev/null; then
  ok 'shell lint preserves its deterministic manifest and critical-path receipt'
else
  bad 'shell lint workflow must preserve the manifest and receipt emitted by the selector'
fi

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

# Inspect actual literal steps, then prove the same check rejects late execution and lost evidence.
if python3 - "$ROOT/.github/workflows/coord-engine.yml" <<'PY_EARLY'
import pathlib, re, sys
source = pathlib.Path(sys.argv[1]).read_text()
def early(value):
    parts = re.split(r"(?m)^      - name: ", value.split("  engine:\n",1)[1])
    steps = [(part.split("\n",1)[0],part) for part in parts[1:]]
    names = [name for name,_ in steps]
    required = ["Build the engine", "Test — core telemetry contracts and source binding",
                "Test — workspace telemetry routes before expensive qualification",
                "Test — the wire contract, and the residue rule",
                "Test the shared projection and real loopback browser"]
    positions = [names.index(name) for name in required]
    assert positions == sorted(positions), "test assertions must execute before expensive qualification"
    by_name = dict(steps)
    focus = by_name[required[2]]
    full = by_name[required[3]]
    assert 'FullyQualifiedName~WorkspaceTelemetryApplicationTests' in focus and 'timeout-minutes: 5' in focus
    assert '--filter' not in full and 'FS.GG.Coord.Cli.Tests.fsproj' in full, "full CLI coverage must remain"
    for step in (focus,full):
        assert '--blame' in step and '--blame-hang' not in step and '--blame-crash' not in step
    host = by_name['Test — passive Host HTTP authority before expensive qualification']
    assert names.index('Build the engine') < names.index('Test — passive Host HTTP authority before expensive qualification') < names.index(required[4])
    assert 'FullyQualifiedName~HostPassiveReceiptTests|FullyQualifiedName~HostSchemaProfileTests|FullyQualifiedName~RemoteTelemetryTests.protected native collector resolves durable dispatch and replays retained envelope' in host and 'timeout-minutes: 5' in host
    assert 'coord-engine/host-passive/host-passive.trx" --minimum 4' in value
    assert 'coord-engine/workspace/workspace.trx" --minimum 20' in value
    evidence = by_name['Retain early executable test diagnostics']
    assert 'if: ${{ always() }}' in evidence and 'coord-engine/' in evidence and 'retention-days: 7' in evidence
    assert 'timeout-minutes: 40' in value and 'continue-on-error:' not in value
    for name in ['Pack and qualify the standalone telemetry workspace',
                 "Parity — the engine matches the corpus's certified batch answer",
                 'Test — standalone telemetry HTTPS contracts and host boundaries',
                 'Test — lifecycle handlers and typed completion dependency',
                 'Test — BoardOps handlers and production registration agreement',
                 "Test — the kernel's argument grammar, identity, and ref parsing"]:
        assert name in names, "existing coherent coverage lost: " + name
early(source)
start = source.index('      - name: Test — workspace telemetry routes before expensive qualification')
end = source.index('      - name: Non-vacuity — the focused workspace routes executed',start)
focus_block = source[start:end]
late = source[:start] + source[end:] + focus_block
for bad in [late,source.replace('Test — workspace telemetry routes before expensive qualification','late workspace test'),
            source.replace('--filter "FullyQualifiedName~WorkspaceTelemetryApplicationTests"','--filter "unrelated"'),
            source.replace('--blame ',''),
            source.replace('FullyQualifiedName~HostPassiveReceiptTests','FullyQualifiedName~UnrelatedHost'),
            source.replace('FullyQualifiedName~RemoteTelemetryTests.protected native collector','FullyQualifiedName~UnrelatedNativeCollector'),
            source.replace('host-passive.trx" --minimum 4','host-passive.trx" --minimum 0'),
            source.replace('if: ${{ always() }}','if: success()'),
            source.replace('Test — the wire contract, and the residue rule','missing full suite')]:
    try: early(bad)
    except (AssertionError,ValueError,KeyError): pass
    else: raise AssertionError('late/missing executable coverage mutation survived')
PY_EARLY
then
  ok 'actual engine runs focused and full assertions early and retains failure diagnostics'
else
  bad 'engine executable ordering/diagnostic preservation regressed'
fi

printf '\nci-runtime-optimization: %d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]

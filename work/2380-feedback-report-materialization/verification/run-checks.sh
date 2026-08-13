#!/usr/bin/env bash
# Re-executes the measurements spec.md records for .github#2380, and emits a JUnit report.
#
# This is the VO-001 obligation made runnable. The package ships a record, not a compiled artifact,
# so its verification obligation is that the recorded measurements are re-executable and still say
# what the record says they say. Every assertion below RUNS the real evaluator
# (scripts/skill-union-assert.sh) against a committed fixture — none of them looks for a string in a
# file, and none of them re-implements the predicate grammar.
#
# Hermetic: no network, no board, no scaffold. It reads two committed fixtures and this repository's
# own gate script. Safe to run from any checkout of .github.
#
#   usage: work/2380-feedback-report-materialization/verification/run-checks.sh [out.xml]
#
# Exit 0 when every check passes; 1 otherwise (and the JUnit report records which).
#
# GATE-INVERSION: set FSGG_2380_INVERT to the name of one check to flip its EXPECTED value, proving
# the check can go red. See verification-evidence.md for the recorded inversions and their output.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
FIXTURES="$REPO_ROOT/work/2380-feedback-report-materialization/verification/fixtures"
GATE="$REPO_ROOT/scripts/skill-union-assert.sh"
OBJ="$FIXTURES/fable-game-provenance.object.json"
ARR="$FIXTURES/sir-provenance.array.json"
OUT="${1:-$REPO_ROOT/work/2380-feedback-report-materialization/verification/junit.xml}"
INVERT="${FSGG_2380_INVERT:-}"

[ -x "$GATE" ] || { echo "gate not executable: $GATE" >&2; exit 1; }
[ -f "$OBJ" ] && [ -f "$ARR" ] || { echo "fixtures missing under $FIXTURES" >&2; exit 1; }

PASS=0; FAIL=0; CASES=""

record() { # name, ok(0/1), message
  local name="$1" ok="$2" msg="$3"
  if [ "$ok" -eq 0 ]; then
    PASS=$((PASS+1)); CASES="$CASES    <testcase classname=\"github2380\" name=\"$name\"/>
"
  else
    FAIL=$((FAIL+1)); CASES="$CASES    <testcase classname=\"github2380\" name=\"$name\"><failure message=\"$msg\"/></testcase>
"
    echo "FAIL $name: $msg" >&2
  fi
}

# --- 1. predicate evaluation, run through the real evaluator -------------------------------------
# Each row is: check-name | predicate | expected. These are spec.md F5's table.
check_predicate() {
  local name="$1" predicate="$2" expected="$3"
  [ "$INVERT" = "$name" ] && { [ "$expected" = "true" ] && expected="false" || expected="true"; }
  local actual rc
  actual="$("$GATE" --eval-when "$predicate" --params "$OBJ" 2>&1)"; rc=$?
  if [ "$rc" -ne 0 ]; then
    record "$name" 1 "evaluator exited $rc for '$predicate': $actual"; return
  fi
  if [ "$actual" = "$expected" ]; then
    record "$name" 0 ""
  else
    record "$name" 1 "predicate '$predicate' evaluated '$actual', expected '$expected'"
  fi
}

# `always` is the ONLY product predicate that holds on a scaffold with no `profile` parameter — which
# is exactly why fs-gg-feedback-report is the only product row whose absence is detectable there.
check_predicate always_holds                   "always"                                                        true
check_predicate profile_five_is_false          "profile in [app, headless-scene, governed, sample-pack, game]" false
check_predicate profile_game_samplepack_false  "profile in [game, sample-pack]"                                false
check_predicate profile_eq_samplepack_false    "profile == sample-pack"                                        false
# FS.GG.Templates' own manifest vocabulary: `template` is not a parameter this repo declares, so its
# predicates are unevaluatable here and answer a constant false (.github#2547).
check_predicate template_vocabulary_false      "template in [fable-game, fable-bindings]"                      false
# An unset parameter resolves to the empty string, so a NEGATED clause fires. Latent hazard, recorded.
check_predicate negated_unset_param_true       "lifecycle != spec-kit"                                         true

# --- 2. the array-shaped provenance the gate cannot parse (.github#2546) --------------------------
# Asserts the DEFECT still reproduces: exit 2 and the specific refusal, not merely "nonzero".
name="array_provenance_refused"
expected_rc=2
[ "$INVERT" = "$name" ] && expected_rc=0
out="$("$GATE" --eval-when "always" --params "$ARR" 2>&1)"; rc=$?
if [ "$rc" -ne "$expected_rc" ]; then
  record "$name" 1 "expected exit $expected_rc against array-shaped effectiveParameters, got $rc: $out"
elif [ "$expected_rc" -eq 2 ] && ! printf '%s' "$out" | grep -q "has no .effectiveParameters object"; then
  record "$name" 1 "exit 2 but not the documented refusal: $out"
else
  record "$name" 0 ""
fi

# --- 3. the two shapes really do differ, so check 2 is not vacuous --------------------------------
name="fixture_shapes_differ"
o="$(python3 -c "import json;print(type(json.load(open('$OBJ'))['effectiveParameters']).__name__)")"
a="$(python3 -c "import json;print(type(json.load(open('$ARR'))['effectiveParameters']).__name__)")"
# Inversion swaps the EXPECTED shapes, so a run with the variable set must go red against the real
# fixtures. Without this the check would pass under its own inversion — which is not a gate.
exp_o="dict"; exp_a="list"
[ "$INVERT" = "$name" ] && { exp_o="list"; exp_a="dict"; }
if [ "$o" = "$exp_o" ] && [ "$a" = "$exp_a" ]; then record "$name" 0 ""; else
  record "$name" 1 "expected $exp_o/$exp_a effectiveParameters in the two fixtures, got $o/$a"
fi

TOTAL=$((PASS+FAIL))
mkdir -p "$(dirname "$OUT")"
cat > "$OUT" <<XML
<?xml version="1.0" encoding="UTF-8"?>
<testsuites>
  <testsuite name="github2380.verification" tests="$TOTAL" failures="$FAIL" errors="0">
$CASES  </testsuite>
</testsuites>
XML

echo "github2380 verification: $PASS passed, $FAIL failed (report: $OUT)"
[ "$FAIL" -eq 0 ]

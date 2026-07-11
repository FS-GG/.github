#!/usr/bin/env bash
# Cross-implementation conformance for the ADR-0017 `materializes-when` grammar — FS-GG/.github#398
# (deferred "ideally" clause of #385; the #266/#292 fail-open shape).
#
# The tiny predicate language `always | <clause> [(and|or) <clause>]...` (clause = `p == v | p != v
# | p in [v, ...]`) is implemented TWICE, and a divergence between them fails OPEN in a
# direction no single side detects: `registry/skills.CHANGELOG.md` (#292) records the union gate once
# read a real predicate as *false* and silently dropped a skill. The two implementations, both here:
#   1. the shell union-gate evaluator  — scripts/skill-union-assert.sh   (eval_condition, via --eval-when)
#   2. the Python drift normalizer      — scripts/fsgg-skill-registry-check (normalize_when, via --normalize-when)
# There is no third leg: FS.GG.Contracts' Fsgg.Registry is the registry document-schema validator
# (ADR-0015), not a `materializes-when` evaluator — SDD's process skills are all `always` by ADR-0017
# construction, so no typed conditional evaluator exists there to pin (.github#408).
#
# This harness drives the SHARED fixture table (materializes-when.fixtures.json) through the two
# implementations reachable from this repo and asserts, for each (predicate, params) -> expected:
#   (a) the shell evaluator returns `expected`; and
#   (b) it STILL returns `expected` after the predicate is round-tripped through normalize_when — i.e.
#       the Python normalizer never changes the evaluated meaning (the exact quoted-literal leg #292
#       hit, and the leg #396 fixed on the shell side but did not pin cross-implementation).
# An `unparseable: true` fixture asserts the shell evaluator REJECTS it (exit 2), not a silent false.
#
# Self-contained: no network, no other repos. `--normalize-when` is pure-stdlib, so no PyYAML.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ASSERT="$HERE/../../scripts/skill-union-assert.sh"
CHECK="$HERE/../../scripts/fsgg-skill-registry-check"
FIXTURES="$HERE/materializes-when.fixtures.json"

command -v jq >/dev/null 2>&1      || { echo "conformance: jq required" >&2; exit 2; }
command -v python3 >/dev/null 2>&1 || { echo "conformance: python3 required" >&2; exit 2; }
[ -f "$FIXTURES" ]                 || { echo "conformance: fixtures not found: $FIXTURES" >&2; exit 2; }

# Guard the harness's OWN fail-open (the very shape this test exists to prevent): a corrupt or empty
# fixtures file must never read as green. `jq length` here also validates the JSON parses — a parse
# error aborts under `set -e` rather than silently yielding zero rows — and the table must be
# non-empty. After the loop we confirm every declared fixture actually ran (a truncated stream reads
# as fewer rows, not as success).
total="$(jq '.fixtures | length' "$FIXTURES")"
[ "$total" -ge 1 ] || { echo "conformance: fixture table is empty or malformed: $FIXTURES" >&2; exit 2; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/mw-conformance.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
PROV="$WORK/prov.json"

pass=0; failcount=0
ok()   { echo "PASS  $1"; pass=$((pass+1)); }
bad()  { echo "FAIL  $1"; failcount=$((failcount+1)); }

n=0
while IFS= read -r fx; do
  n=$((n+1))
  pred="$(jq -r '.predicate'      <<<"$fx")"
  params="$(jq -c '.params'       <<<"$fx")"
  note="$(jq -r '.note // ""'      <<<"$fx")"
  unparseable="$(jq -r '.unparseable // false' <<<"$fx")"
  # A scaffold-provenance.json the assertion reads via --params, built with the SAME shape the real
  # gate consumes (.effectiveParameters), so the fixture exercises the gate's own param extraction.
  jq -n --argjson p "$params" '{effectiveParameters: $p}' >"$PROV"

  if [ "$unparseable" = "true" ]; then
    rc=0; bash "$ASSERT" --eval-when "$pred" --params "$PROV" >/dev/null 2>&1 || rc=$?
    if [ "$rc" -eq 2 ]; then ok "[$n] unparseable rejected (exit 2): '$pred'"
    else bad "[$n] unparseable NOT rejected (want exit 2, got $rc): '$pred'  [$note]"; fi
    continue
  fi

  expected="$(jq -r '.expected' <<<"$fx")"

  # (a) shell evaluator directly on the authored predicate
  if got="$(bash "$ASSERT" --eval-when "$pred" --params "$PROV")"; then
    if [ "$got" = "$expected" ]; then ok "[$n] shell        '$pred' -> $got"
    else bad "[$n] shell        '$pred' -> $got (want $expected)  [$note]"; fi
  else
    bad "[$n] shell        '$pred' died (rc=$?)  [$note]"
  fi

  # (b) round-trip: normalize_when(predicate) must evaluate to the SAME bool. If normalization ever
  # mangles a value (e.g. splitting a quoted literal) the meaning flips and this reds — #292's shape.
  norm="$(python3 "$CHECK" --normalize-when "$pred")"
  if gotn="$(bash "$ASSERT" --eval-when "$norm" --params "$PROV")"; then
    if [ "$gotn" = "$expected" ]; then ok "[$n] normalize+eval '$norm' -> $gotn"
    else bad "[$n] normalize CHANGED MEANING: '$pred' -> normalize '$norm' -> $gotn (want $expected)  [$note]"; fi
  else
    bad "[$n] normalize+eval '$norm' died (rc=$?)  [$note]"
  fi
done < <(jq -c '.fixtures[]' "$FIXTURES")

[ "$n" -eq "$total" ] || { echo "conformance: ran $n of $total fixtures — the table did not stream fully" >&2; exit 2; }

echo "--------------------------------------------"
echo "materializes-when conformance: $pass passed, $failcount failed across $n fixture(s)"
[ "$failcount" -eq 0 ] || exit 1

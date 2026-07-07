#!/usr/bin/env bash
# Fixture for scripts/fsgg-surface-impact — the consumer-impact enumerator for the
# shipped-surface-mutation event (ADR-0025, .github#236). Proves the tool answers "who consumes
# this contract?" correctly off a temp registry: it lists declared consumers, finds the carrying
# dependency edges by a TOKEN match on `via` (so a shorter id is not matched as a prefix of a
# longer one), emits stable --json, lists ids with --list, and fails loudly (exit 2) on an unknown
# id. Pure-stdlib + PyYAML; no network, no board writes. Mirrors tests/repos-audit/run.sh in shape.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/fsgg-surface-impact"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/surface-impact-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# A registry with a deliberate prefix trap: `alpha` is a prefix of `alpha-ext`. The edge that
# carries `alpha-ext` must NOT be attributed to `alpha`, and vice versa.
REG="$WORK/dependencies.yml"
cat > "$REG" <<'YAML'
schemaVersion: 1
repos:
  one:   { name: Repo.One,   role: "producer" }
  two:   { name: Repo.Two,   role: "consumer" }
  three: { name: Repo.Three, role: "consumer" }
contracts:
  - id: alpha
    version: "1.2.0"
    owner: one
    surface: "the alpha surface"
    consumers: [two, three]
  - id: alpha-ext
    version: "0.1.0"
    owner: one
    surface: "the alpha-ext surface"
    consumers: [two]
dependencies:
  - { from: two,   to: one, via: "alpha@1.2.0 (the plain alpha edge)" }
  - { from: three, to: one, via: "alpha@1.2.0 consumed by three" }
  - { from: two,   to: one, via: "alpha-ext@0.1.0 (the extension edge)" }
YAML

run() { "$TOOL" "$@" "$REG"; }        # explicit registry path is the last positional arg

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# 1. declared consumers are listed (human)
out="$(run alpha)"
{ grep -q 'two' <<<"$out" && grep -q 'three' <<<"$out"; } \
  && ok "alpha lists declared consumers two + three" || bad "alpha consumers" "$out"

# 2. carrying edges found by token match — alpha gets its 2 edges, NOT the alpha-ext edge
j="$(run alpha --json)"
n_alpha="$(python3 -c 'import json,sys; print(len(json.load(sys.stdin)["edges"]))' <<<"$j")"
[ "$n_alpha" = "2" ] && ok "alpha matches exactly its 2 edges (no prefix bleed)" \
  || bad "alpha edge count" "want 2 got $n_alpha: $j"
grep -q 'alpha-ext' <<<"$j" && bad "alpha wrongly captured the alpha-ext edge" "$j" \
  || ok "alpha does NOT capture the alpha-ext edge"

# 3. the longer id resolves to only its own edge + consumer
j2="$(run alpha-ext --json)"
n_ext="$(python3 -c 'import json,sys; d=json.load(sys.stdin); print(len(d["edges"]), len(d["consumers"]))' <<<"$j2")"
[ "$n_ext" = "1 1" ] && ok "alpha-ext resolves to its 1 edge + 1 consumer" \
  || bad "alpha-ext counts" "want '1 1' got '$n_ext': $j2"

# 4. --list prints both ids
out="$("$TOOL" --list "$REG")"
{ grep -qx 'alpha' <<<"$out" && grep -qx 'alpha-ext' <<<"$out"; } \
  && ok "--list prints both contract ids" || bad "--list" "$out"

# 5. unknown id fails loudly with exit 2 and names available ids
out="$(run nope 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && grep -q 'alpha' <<<"$out"; } \
  && ok "unknown id -> exit 2, lists known ids" || bad "unknown id" "rc=$rc: $out"

echo "surface-impact fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::surface-impact fixture FAILED"; exit 1; }
echo "surface-impact fixture — OK"

#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$ROOT/scripts/check-ship-verdict-provenance.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/ship-verdict-provenance.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; fail=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; fail=$((fail + 1)); }
expect() {
  local name="$1" want="$2" needle="$3" root="$4" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ] || ! grep -qF "$needle" <<<"$out"; then bad "$name"; printf '%s\n' "$out"; else ok "$name"; fi
}
make_root() { mkdir -p "$1/readiness/sample"; printf '%s\n' '{"status":"shipReady"}' > "$1/readiness/sample/ship-verdict.json"; }

GOOD="$WORK/good"; make_root "$GOOD"
expect "clean hand-authored verdict passes" 0 "carry no unverifiable" "$GOOD"

STALE="$WORK/stale"; make_root "$STALE"
printf '%s\n' '{"status":"shipReady","sourcesDigest":{"algorithm":"sha256","value":"stale"}}' > "$STALE/readiness/sample/ship-verdict.json"
expect "REGRESSION #2208: a stale digest is red" 1 "unverifiable provenance" "$STALE"

BAD="$WORK/bad"; make_root "$BAD"
printf '%s\n' '{not json' > "$BAD/readiness/sample/ship-verdict.json"
expect "invalid subject is no verdict, not clean" 3 "invalid JSON" "$BAD"

EMPTY="$WORK/empty"; mkdir -p "$EMPTY/readiness"
expect "no subjects is no verdict, never a vacuous green" 3 "no ship-verdict.json" "$EMPTY"

if python3 "$GATE" --root "$ROOT" >/dev/null; then ok "the shipped readiness surface passes"; else bad "the shipped readiness surface passes"; fi
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]

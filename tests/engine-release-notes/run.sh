#!/usr/bin/env bash
# Fixture for check-engine-release-notes.py. Uses real MSBuild evaluation over tiny projects so the
# test covers the same property expansion and JSON shape the release workflow consumes.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
CHECK="$ROOT/scripts/check-engine-release-notes.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/engine-release-notes.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
fail=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; printf '%s\n' "${2:-}" | sed 's/^/    | /'; fail=$((fail + 1)); }

project() { # path, version, notes
  local path="$1" version="$2" notes="$3"
  mkdir -p "$(dirname "$path")"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    printf '    <Version>%s</Version>\n' "$version"
    printf '    <PackageReleaseNotes>%s</PackageReleaseNotes>\n' "$notes"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$path"
}

run() {
  set +e
  out="$(python3 "$CHECK" --project "$1" 2>&1)"
  rc=$?
  set -e
}

expect() { # wanted rc, label, output pattern
  local wanted="$1" label="$2" pattern="$3"
  if [ "$rc" -ne "$wanted" ]; then
    bad "$label (wanted exit $wanted, got $rc)" "$out"
  elif ! grep -q -- "$pattern" <<<"$out"; then
    bad "$label (missing: $pattern)" "$out"
  else
    ok "$label"
  fi
}

echo "== engine release-notes coherence =="

P="$WORK/coherent/Test.fsproj"
project "$P" "1.2.3" $'1.2.3 — the correct release\n\nDetails follow.'
run "$P"
expect 0 "matching evaluated version and notes are green" "Version 1.2.3 agrees"

P="$WORK/stale/Test.fsproj"
project "$P" "1.2.4" "1.2.3 — stale notes"
run "$P"
expect 1 "the measured stale-notes shape is red" "Version is 1.2.4"

P="$WORK/empty/Test.fsproj"
project "$P" "1.2.4" ""
run "$P"
expect 1 "empty release notes are red" "PackageReleaseNotes is empty"

run "$WORK/missing/NoSuch.fsproj"
expect 2 "an unevaluable project is no verdict, never coherent" "could not evaluate"

run "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
expect 0 "the shipped engine project is coherent" "Version 0.16.0 agrees"

release_workflow="$ROOT/.github/workflows/release-coord-engine.yml"
checker_line="$(grep -n 'python3 scripts/check-engine-release-notes.py' "$release_workflow" | head -1 | cut -d: -f1 || true)"
pack_line="$(grep -n '^      - name: Pack$' "$release_workflow" | head -1 | cut -d: -f1 || true)"
if [ -n "$checker_line" ] && [ -n "$pack_line" ] && [ "$checker_line" -lt "$pack_line" ]; then
  ok "the release workflow refuses incoherent notes before packing"
else
  bad "the release workflow must run the checker before Pack" \
    "checker line=${checker_line:-missing}; Pack line=${pack_line:-missing}"
fi

echo
echo "engine release-notes fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ]

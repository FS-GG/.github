#!/usr/bin/env bash
# Fixture for check-coherent-set-version.py (.github#2402). Uses REAL MSBuild evaluation over tiny
# synthetic projects — same technique tests/engine-release-notes/run.sh uses for the same reason: the
# gate's own claim is about what MSBuild resolves, and a fixture that only inspected text would not
# exercise the property expansion the gate exists to trust.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
CHECK="$ROOT/scripts/check-coherent-set-version.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/coherent-set-version.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
fail=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; printf '%s\n' "${2:-}" | sed 's/^/    | /'; fail=$((fail + 1)); }

# Builds one coherent-set fixture: a Directory.Build.props declaring FsggCoherentSetVersion=$1, and
# three sibling projects under $dir/{kit,drivers,cli}/Test.csproj. $2/$3/$4 are each project's
# authored <Version> element BODY — pass the literal string '$(FsggCoherentSetVersion)' for the
# correct, coherent shape, or any other literal to simulate a reintroduced independent version.
fixture() {
  local dir="$WORK/$1" set_version="$2" kit_version="$3" drivers_version="$4" cli_version="$5"
  rm -rf "$dir"
  mkdir -p "$dir/kit" "$dir/drivers" "$dir/cli"
  {
    echo '<Project>'
    echo '  <PropertyGroup>'
    printf '    <FsggCoherentSetVersion>%s</FsggCoherentSetVersion>\n' "$set_version"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$dir/Directory.Build.props"
  for pair in "kit:$kit_version" "drivers:$drivers_version" "cli:$cli_version"; do
    local name="${pair%%:*}" version="${pair#*:}"
    {
      echo '<Project Sdk="Microsoft.NET.Sdk">'
      echo '  <PropertyGroup>'
      echo '    <TargetFramework>net10.0</TargetFramework>'
      printf '    <Version>%s</Version>\n' "$version"
      echo '  </PropertyGroup>'
      echo '</Project>'
    } > "$dir/$name/Test.csproj"
  done
}

run() { # props path, project paths...
  local props="$1"; shift
  local args=()
  for p in "$@"; do args+=(--project "$p"); done
  set +e
  out="$(python3 "$CHECK" --props "$props" "${args[@]}" 2>&1)"
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

echo "== coherent-set-version =="

# --- The correct shape: all three reference the shared property, and it resolves. ---
fixture coherent 9.9.9 '$(FsggCoherentSetVersion)' '$(FsggCoherentSetVersion)' '$(FsggCoherentSetVersion)'
run "$WORK/coherent/Directory.Build.props" "$WORK/coherent/kit/Test.csproj" "$WORK/coherent/drivers/Test.csproj" "$WORK/coherent/cli/Test.csproj"
expect 0 "all three referencing the shared property, evaluating equal, is green" "evaluate to '9.9.9'"

# --- GATE-INVERSION: reintroduce ONE independent literal (the exact defect .github#2402 fixes). ---
fixture mutated-drivers 9.9.9 '$(FsggCoherentSetVersion)' '0.18.1' '$(FsggCoherentSetVersion)'
run "$WORK/mutated-drivers/Directory.Build.props" "$WORK/mutated-drivers/kit/Test.csproj" "$WORK/mutated-drivers/drivers/Test.csproj" "$WORK/mutated-drivers/cli/Test.csproj"
expect 1 "MUTATION: one project reverted to an independent literal is red" "declares <Version>0.18.1</Version>, not"

# ...and reverting the mutation (same fixture as the first leg) is green again — proves the leg above
# is a real gate-inversion pair, not a fixture that is always red.
run "$WORK/coherent/Directory.Build.props" "$WORK/coherent/kit/Test.csproj" "$WORK/coherent/drivers/Test.csproj" "$WORK/coherent/cli/Test.csproj"
expect 0 "...restoring the property reference is green again" "evaluate to '9.9.9'"

# --- A DIFFERENT independent literal, on the FIRST project checked, is caught the same way. ---
fixture mutated-kit 9.9.9 '1.2.3' '$(FsggCoherentSetVersion)' '$(FsggCoherentSetVersion)'
run "$WORK/mutated-kit/Directory.Build.props" "$WORK/mutated-kit/kit/Test.csproj" "$WORK/mutated-kit/drivers/Test.csproj" "$WORK/mutated-kit/cli/Test.csproj"
expect 1 "MUTATION: the FIRST-checked project reverted to a literal is red" "declares <Version>1.2.3</Version>, not"

# --- Fail-closed reads. Each is a NO-VERDICT, and a no-verdict is RED (epic #266). ---

# The shared-property file itself: missing, empty, and ambiguous (duplicate declaration).
run "$WORK/no-such-Directory.Build.props" "$WORK/coherent/kit/Test.csproj"
expect 1 "an unreadable shared-property file is a no-verdict" "cannot read"

printf '<Project></Project>\n' > "$WORK/no-property.props"
run "$WORK/no-property.props" "$WORK/coherent/kit/Test.csproj"
expect 1 "a shared-property file declaring zero FsggCoherentSetVersion is a no-verdict" "declares 0 <FsggCoherentSetVersion> element(s)"

printf '<Project><PropertyGroup><FsggCoherentSetVersion>1.0.0</FsggCoherentSetVersion><FsggCoherentSetVersion>2.0.0</FsggCoherentSetVersion></PropertyGroup></Project>\n' > "$WORK/dup-property.props"
run "$WORK/dup-property.props" "$WORK/coherent/kit/Test.csproj"
expect 1 "a shared-property file declaring FsggCoherentSetVersion twice is a no-verdict" "declares 2 <FsggCoherentSetVersion> element(s)"

# A project file: missing, no <Version>, and ambiguous (two <Version> elements).
run "$WORK/coherent/Directory.Build.props" "$WORK/no-such/Test.csproj"
expect 1 "an unreadable project file is a no-verdict" "cannot read"

mkdir -p "$WORK/no-version"
printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n' > "$WORK/no-version/Test.csproj"
run "$WORK/coherent/Directory.Build.props" "$WORK/no-version/Test.csproj"
expect 1 "a project declaring zero <Version> elements is a no-verdict" "declares 0 <Version> element(s)"

mkdir -p "$WORK/dup-version"
printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version></PropertyGroup><PropertyGroup><Version>2.0.0</Version></PropertyGroup></Project>\n' > "$WORK/dup-version/Test.csproj"
run "$WORK/coherent/Directory.Build.props" "$WORK/dup-version/Test.csproj"
expect 1 "a project declaring <Version> twice is a no-verdict (structural scan, before evaluation)" "declares 2 <Version> element(s)"

# --- The live-repo leg: the three REAL shipped project files, default args, must be coherent. ---
run "$ROOT/Directory.Build.props" "$ROOT/src/FS.GG.Kit/FS.GG.Kit.csproj" "$ROOT/src/FS.GG.Drivers/FS.GG.Drivers.csproj" "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
expect 0 "the three real shipped project files are coherent" "all reference \${FsggCoherentSetVersion}"

set +e
out="$(cd "$ROOT" && python3 "$CHECK" 2>&1)"
rc=$?
set -e
expect 0 "the default (no-argument) invocation names the three real shipped projects" "all reference \${FsggCoherentSetVersion}"

# --- Wiring: the workflow runs this fixture before the live gate, exactly as engine-release-notes.yml does. ---
workflow="$ROOT/.github/workflows/coherent-set-version.yml"
fixture_line="$(grep -n 'bash tests/coherent-set-version/run.sh' "$workflow" | head -1 | cut -d: -f1 || true)"
gate_line="$(grep -n 'python3 scripts/check-coherent-set-version.py' "$workflow" | head -1 | cut -d: -f1 || true)"
if [ -n "$fixture_line" ] && [ -n "$gate_line" ] && [ "$fixture_line" -lt "$gate_line" ]; then
  ok "the workflow runs this fixture before the live gate"
else
  bad "the workflow must run the fixture before the live gate" \
    "fixture line=${fixture_line:-missing}; gate line=${gate_line:-missing}"
fi

echo
echo "coherent-set-version fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ]

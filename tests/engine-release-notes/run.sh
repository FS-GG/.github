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

# A SENTINEL SO THE FIXTURE IS HERMETIC. MSBuild walks UP from a project looking for the nearest
# Directory.Build.props; without a stop at the fixture root it would keep climbing out of $TMPDIR and
# could bind whatever happens to sit above it on the runner. Each case below writes its own props
# file beside its project, so this one is only ever the backstop for the deliberately propsless case.
mkdir -p "$WORK"
printf '<Project />\n' > "$WORK/Directory.Build.props"

# THE PRODUCTION SHAPE, REPRODUCED (.github#2512 / .github#2402): the version is NOT a literal in the
# project. It is `FsggCoherentSetVersion`, declared in a Directory.Build.props the project inherits,
# and `<Version>` merely references it. Building the fixture any other way would test a shape this
# repo does not ship — and would have gone on passing while the real defect (a bump that lands only
# in Directory.Build.props) stayed invisible to this suite.
project() { # path, version, notes
  local path="$1" version="$2" notes="$3"
  mkdir -p "$(dirname "$path")"
  {
    echo '<Project>'
    echo '  <PropertyGroup>'
    printf '    <FsggCoherentSetVersion>%s</FsggCoherentSetVersion>\n' "$version"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$(dirname "$path")/Directory.Build.props"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    echo '    <Version>$(FsggCoherentSetVersion)</Version>'
    printf '    <PackageReleaseNotes>%s</PackageReleaseNotes>\n' "$notes"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$path"
}

# The pre-.github#2402 shape: an independent `<Version>` literal, with no coherent-set scalar behind
# it. This must be REFUSED rather than blessed — otherwise "which version is being announced" has an
# answer this gate cannot trace back to Directory.Build.props.
literal_project() { # path, version, notes
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

P="$WORK/literal/Test.fsproj"
literal_project "$P" "1.2.3" "1.2.3 — notes agreeing with an untraceable independent literal"
run "$P"
expect 1 "a version with no coherent-set scalar behind it is red, never green" \
  "FsggCoherentSetVersion is empty"

P="$WORK/divergent/Test.fsproj"
project "$P" "1.2.4" "1.2.4 — notes agreeing with an overridden Version"
# Re-declare <Version> as a literal that disagrees with the inherited scalar. The notes and <Version>
# agree here, so the ORIGINAL check is satisfied — only the coherent-set arm can see this.
sed -i 's|<Version>\$(FsggCoherentSetVersion)</Version>|<Version>9.9.9</Version>|' "$P"
sed -i 's|1.2.4 — notes|9.9.9 — notes|' "$P"
run "$P"
expect 1 "a Version that did not come from the coherent-set scalar is red" \
  "FsggCoherentSetVersion is 1.2.4"

run "$WORK/missing/NoSuch.fsproj"
expect 2 "an unevaluable project is no verdict, never coherent" "could not evaluate"

shipped="$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
shipped_version="$(dotnet msbuild "$shipped" -getProperty:Version -nologo)"
run "$shipped"
expect 0 "the shipped engine project is coherent" "Version $shipped_version agrees"

release_workflow="$ROOT/.github/workflows/release-coord-engine.yml"
checker_line="$(grep -n 'python3 scripts/check-engine-release-notes.py' "$release_workflow" | head -1 | cut -d: -f1 || true)"
pack_line="$(grep -n '^      - name: Pack$' "$release_workflow" | head -1 | cut -d: -f1 || true)"
if [ -n "$checker_line" ] && [ -n "$pack_line" ] && [ "$checker_line" -lt "$pack_line" ]; then
  ok "the release workflow refuses incoherent notes before packing"
else
  bad "the release workflow must run the checker before Pack" \
    "checker line=${checker_line:-missing}; Pack line=${pack_line:-missing}"
fi

# .github#2512's ACTUAL DEFECT, PINNED HERE. Every case above proves the CHECKER is right; none of
# them could have caught 0.50.5, because the checker was never invoked. The bump moved
# `FsggCoherentSetVersion` in Directory.Build.props and nothing else this workflow selected, so the
# PR arm did not run and `check-engine-release-notes` first spoke inside `release-coord-engine` —
# after two of the three packages had published irrevocably. A gate is only as wide as its trigger,
# so the trigger is asserted here, against the checker's own declared subject rather than a retyped
# list: widen PATHS_SUBJECT and this fails until the workflow follows.
if filter_report="$(python3 - "$ROOT" <<'PY'
import ast
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
checker = root / "scripts" / "check-engine-release-notes.py"
workflow = root / ".github" / "workflows" / "engine-release-notes.yml"

# Read PATHS_SUBJECT by AST, never by import: this fixture must not execute the gate it measures.
module = ast.parse(checker.read_text())
constants: dict[str, object] = {}


def fold(node):
    """PATHS_SUBJECT as check-paths-coherence rule (c) folds it: literals, module-level names, `+`."""
    if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
        return fold(node.left) + fold(node.right)
    if isinstance(node, (ast.Tuple, ast.List)):
        return [entry for elt in node.elts for entry in fold(elt)]
    if isinstance(node, ast.Name):
        value = constants[node.id]
        return list(value) if isinstance(value, (list, tuple)) else [value]
    value = ast.literal_eval(node)
    return list(value) if isinstance(value, (list, tuple)) else [value]


subject = ()
for node in module.body:
    if not isinstance(node, ast.Assign) or len(node.targets) != 1:
        continue
    target = node.targets[0]
    if not isinstance(target, ast.Name):
        continue
    if target.id == "PATHS_SUBJECT":
        subject = tuple(fold(node.value))
    else:
        try:
            constants[target.id] = ast.literal_eval(node.value)
        except ValueError:
            pass

if not subject:
    print("could not fold PATHS_SUBJECT out of the checker")
    sys.exit(1)

# Indentation walk over the two trigger blocks. `on:` sits at column 0, each trigger at 2, `paths:`
# at 4, entries at 6.
selected: dict[str, list[str]] = {}
trigger = None
in_paths = False
for line in workflow.read_text().splitlines():
    stripped = line.strip()
    if line[:3] == "  " + stripped[:1] and stripped.endswith(":") and not line.startswith("   "):
        trigger, in_paths = stripped[:-1], False
    elif line.startswith("    paths:"):
        in_paths = True
        selected.setdefault(trigger, [])
    elif in_paths and line.startswith("      - "):
        selected[trigger].append(stripped[2:].strip().strip('"'))
    elif stripped and not line.startswith("      "):
        in_paths = False

problems = []
for want in ("pull_request", "push"):
    have = selected.get(want)
    if have is None:
        problems.append(f"{want}: declares no paths filter at all")
        continue
    for entry in subject:
        if entry not in have and f"{entry}/**" not in have:
            problems.append(f"{want}: does not select declared subject {entry!r}")

if problems:
    print("; ".join(problems))
    sys.exit(1)
print(f"both triggers select all {len(subject)} declared subject(s): {', '.join(subject)}")
PY
)"; then
  ok "both trigger filters select every PATHS_SUBJECT entry (a version-only bump runs this gate)"
else
  bad "engine-release-notes.yml must select every PATHS_SUBJECT entry in BOTH triggers" \
    "$filter_report"
fi

echo
echo "engine release-notes fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ]

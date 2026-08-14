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

# The standing advisories every fixture project declares. The REAL text is 2,000-odd characters of
# DO-NOT-ADOPT warnings; what this fixture needs is only that the property exists, is non-empty, and
# is referenced — its content is the shipped project's business, asserted against the real tree at
# the end of this file.
ADVISORIES='DO NOT ADOPT 0.0.1. Fixture advisory.'
ADVISORY_REF='$(FsggStandingAdvisories)'

# THE PRODUCTION SHAPE, REPRODUCED (.github#2512 / .github#2402): the version is NOT a literal in the
# project. It is `FsggCoherentSetVersion`, declared in a Directory.Build.props the project inherits,
# and `<Version>` merely references it. Building the fixture any other way would test a shape this
# repo does not ship — and would have gone on passing while the real defect (a bump that lands only
# in Directory.Build.props) stayed invisible to this suite.
#
# THE COHERENT-SET DECLARATION IS REPRODUCED THE SAME WAY (.github#2579). The checker folds its
# length-arm subject out of `check-coherent-set-version.py`'s `PROJECTS` by AST, so each case writes
# a one-entry stand-in beside its project and passes it with --coherent-set-source. That keeps every
# case hermetic — no fixture run measures the real repository's packages by accident — while driving
# the same folding code path the production run uses.
project() { # path, version, notes, [advisories]
  local path="$1" version="$2" notes="$3" advisories="${4-$ADVISORIES}"
  local dir
  dir="$(dirname "$path")"
  mkdir -p "$dir"
  {
    echo '<Project>'
    echo '  <PropertyGroup>'
    printf '    <FsggCoherentSetVersion>%s</FsggCoherentSetVersion>\n' "$version"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$dir/Directory.Build.props"
  printf 'PROJECTS = (\n    %s,\n)\n' "\"$path\"" > "$dir/coherent-set.py"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    echo '    <Version>$(FsggCoherentSetVersion)</Version>'
    printf '    <FsggStandingAdvisories>%s</FsggStandingAdvisories>\n' "$advisories"
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
  local dir
  dir="$(dirname "$path")"
  mkdir -p "$dir"
  printf 'PROJECTS = (\n    %s,\n)\n' "\"$path\"" > "$dir/coherent-set.py"
  {
    echo '<Project Sdk="Microsoft.NET.Sdk">'
    echo '  <PropertyGroup>'
    printf '    <Version>%s</Version>\n' "$version"
    printf '    <FsggStandingAdvisories>%s</FsggStandingAdvisories>\n' "$ADVISORIES"
    printf '    <PackageReleaseNotes>%s</PackageReleaseNotes>\n' "$notes"
    echo '  </PropertyGroup>'
    echo '</Project>'
  } > "$path"
}

run() { # project, [coherent-set source]
  local source="${2-$(dirname "$1")/coherent-set.py}"
  set +e
  out="$(python3 "$CHECK" --project "$1" --coherent-set-source "$source" 2>&1)"
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
project "$P" "1.2.3" "1.2.3 — the correct release

Details follow.

$ADVISORY_REF"
run "$P"
expect 0 "matching evaluated version and notes are green" "Version 1.2.3 agrees"

P="$WORK/stale/Test.fsproj"
project "$P" "1.2.4" "1.2.3 — stale notes

$ADVISORY_REF"
run "$P"
expect 1 "the measured stale-notes shape is red" "Version is 1.2.4"

P="$WORK/empty/Test.fsproj"
project "$P" "1.2.4" ""
run "$P"
expect 1 "empty release notes are red" "PackageReleaseNotes is empty"

P="$WORK/literal/Test.fsproj"
literal_project "$P" "1.2.3" "1.2.3 — notes agreeing with an untraceable independent literal

$ADVISORY_REF"
run "$P"
expect 1 "a version with no coherent-set scalar behind it is red, never green" \
  "FsggCoherentSetVersion is empty"

P="$WORK/divergent/Test.fsproj"
project "$P" "1.2.4" "1.2.4 — notes agreeing with an overridden Version

$ADVISORY_REF"
# Re-declare <Version> as a literal that disagrees with the inherited scalar. The notes and <Version>
# agree here, so the ORIGINAL check is satisfied — only the coherent-set arm can see this.
sed -i 's|<Version>\$(FsggCoherentSetVersion)</Version>|<Version>9.9.9</Version>|' "$P"
sed -i 's|1.2.4 — notes|9.9.9 — notes|' "$P"
run "$P"
expect 1 "a Version that did not come from the coherent-set scalar is red" \
  "FsggCoherentSetVersion is 1.2.4"

run "$WORK/missing/NoSuch.fsproj" "$WORK/missing/coherent-set.py"
expect 2 "an unevaluable project is no verdict, never coherent" "could not evaluate"

echo
echo "== nuget.org's length limit (.github#2579) =="

# THE BOUNDARY, BOTH SIDES, EXACTLY. nuget.org refuses "more than 35000 characters", so 35,000 is
# publishable and 35,001 is not. The two projects below are generated to those EVALUATED lengths —
# not to their authored lengths, which differ, because `$(FsggStandingAdvisories)` expands. Getting
# this wrong in the obvious direction is the whole subject of the item: the failure was a check that
# measured something adjacent to what the registry enforces.
LIMIT="$(python3 - "$ROOT" <<'PY'
import ast, pathlib, sys
src = pathlib.Path(sys.argv[1], "scripts", "check-engine-release-notes.py").read_text()
for node in ast.parse(src).body:
    if isinstance(node, ast.Assign) and getattr(node.targets[0], "id", "") == "NUGET_ORG_RELEASE_NOTES_LIMIT":
        print(ast.literal_eval(node.value))
        break
else:
    raise SystemExit("NUGET_ORG_RELEASE_NOTES_LIMIT not found in the checker")
PY
)"
if [ -z "$LIMIT" ]; then
  bad "the fixture must read the limit from the checker rather than restate it" "no constant folded"
  LIMIT=35000
else
  ok "the limit is folded out of the checker, so this fixture holds no second copy of 35000"
fi

boundary_project() { # path, evaluated-length
  python3 - "$1" "$2" "$ADVISORIES" <<'PY'
import pathlib, sys
path, want, advisories = pathlib.Path(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
path.parent.mkdir(parents=True, exist_ok=True)
(path.parent / "Directory.Build.props").write_text(
    "<Project><PropertyGroup><FsggCoherentSetVersion>1.2.3</FsggCoherentSetVersion>"
    "</PropertyGroup></Project>\n"
)
(path.parent / "coherent-set.py").write_text(f'PROJECTS = (\n    "{path}",\n)\n')
head = "1.2.3 padding "
tail = "\n" + advisories          # what $(FsggStandingAdvisories) expands to, plus its own newline
pad = want - len(head) - len(tail)
assert pad > 0, pad
body = head + ("x" * pad) + "\n$(FsggStandingAdvisories)"
path.write_text(
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
    "<Version>$(FsggCoherentSetVersion)</Version>"
    f"<FsggStandingAdvisories>{advisories}</FsggStandingAdvisories>"
    f"<PackageReleaseNotes>{body}</PackageReleaseNotes>"
    "</PropertyGroup></Project>\n",
    encoding="utf-8",
)
PY
}

P="$WORK/at-limit/Test.fsproj"
boundary_project "$P" "$LIMIT"
run "$P"
expect 0 "notes of exactly the limit publish" "$LIMIT of $LIMIT characters used"

P="$WORK/over-limit/Test.fsproj"
boundary_project "$P" "$((LIMIT + 1))"
run "$P"
expect 1 "one character over the limit is red" "exceeds nuget.org's limit of $LIMIT by 1"

# HEADROOM ON BOTH PATHS. "ok" at 34,999 tells an author nothing, and neither does a bare refusal:
# 0.52.0's author had 1,241 characters of headroom and wrote 3,575 (.github#2579 criterion 2).
P="$WORK/headroom/Test.fsproj"
project "$P" "1.2.3" "1.2.3 — short notes

$ADVISORY_REF"
run "$P"
expect 0 "the green path reports remaining headroom, not only a verdict" "characters used"
if grep -q "% of nuget.org's budget" <<<"$out"; then
  ok "the green path reports percent-of-budget"
else
  bad "the green path must report percent-of-budget" "$out"
fi
run "$WORK/over-limit/Test.fsproj"
if grep -q "OVER BUDGET by" <<<"$out"; then
  ok "the red path reports the overage as well as refusing"
else
  bad "the red path must report headroom too" "$out"
fi

# CRITERION 1: EVERY MEMBER OF THE COHERENT SET, NOT ONLY THE ANNOUNCING ONE. The announcing project
# here is perfectly publishable; a SIBLING is over budget. A gate scoped to coord-engine alone is
# green on this tree — and that is the shape in which the next partial publish arrives, because the
# three release workflows run in parallel on three sibling tags and the siblings push first.
mkdir -p "$WORK/set/announcer" "$WORK/set/sibling"
printf '<Project><PropertyGroup><FsggCoherentSetVersion>1.2.3</FsggCoherentSetVersion></PropertyGroup></Project>\n' \
  > "$WORK/set/Directory.Build.props"
project "$WORK/set/announcer/A.fsproj" "1.2.3" "1.2.3 — fine

$ADVISORY_REF"
boundary_project "$WORK/set/sibling/B.fsproj" "$((LIMIT + 500))"
printf 'PROJECTS = (\n    "%s",\n    "%s",\n)\n' \
  "$WORK/set/announcer/A.fsproj" "$WORK/set/sibling/B.fsproj" > "$WORK/set/coherent-set.py"
run "$WORK/set/announcer/A.fsproj" "$WORK/set/coherent-set.py"
expect 1 "a NON-announcing coherent-set member over budget is red" \
  "$WORK/set/sibling/B.fsproj's evaluated PackageReleaseNotes"

# FAIL CLOSED ON THE SUBJECT ITSELF. "I cannot tell which packages to measure" must never share an
# exit code with "measured, and they are fine" (epic #266).
mkdir -p "$WORK/nosubject"
project "$WORK/nosubject/Test.fsproj" "1.2.3" "1.2.3 — fine

$ADVISORY_REF"
printf 'NOT_PROJECTS = ("x",)\n' > "$WORK/nosubject/coherent-set.py"
run "$WORK/nosubject/Test.fsproj"
expect 2 "a coherent-set declaration with no PROJECTS is no verdict, never coherent" \
  "declares no non-empty PROJECTS"

run "$WORK/nosubject/Test.fsproj" "$WORK/nosubject/absent.py"
expect 2 "an unreadable coherent-set declaration is no verdict, never coherent" \
  "cannot read the coherent-set declaration"

echo
echo "== the standing advisories (.github#2579 DEC-002) =="

# WHY THIS ARM EXISTS. 4fccc76d replaced <PackageReleaseNotes> wholesale and deleted every
# poisoned-set warning with it; this gate was GREEN across that deletion, because it checked the
# first token and nothing else. Both cases below are trees where the length arm and the first-token
# arm are BOTH satisfied — so if this arm did not exist, both would publish.
P="$WORK/no-reference/Test.fsproj"
project "$P" "1.2.3" "1.2.3 — notes that inline the warnings instead of referencing them

DO NOT ADOPT 0.0.1. Fixture advisory."
run "$P"
expect 1 "notes that stop referencing the advisory property are red, even when the text is inlined" \
  "does not reference \$(FsggStandingAdvisories)"

P="$WORK/empty-advisories/Test.fsproj"
project "$P" "1.2.3" "1.2.3 — notes referencing an emptied advisory property

$ADVISORY_REF" ""
run "$P"
expect 1 "an emptied advisory property is red" "FsggStandingAdvisories is empty or undeclared"

echo
echo "== the real tree this item repaired (.github#2579 criterion 4) =="

# THE MEASUREMENT, NOT A RE-ENACTMENT OF IT. `release-notes-at-2579.xmlfragment` is the VERBATIM
# <PackageReleaseNotes> element text from the tree that could not publish, evaluated here by the same
# MSBuild the release workflow uses. Its raw text is 37,334 characters and it evaluates to 37,279 —
# a 55-character difference, of which 19 come from the `$(FsggCoherentSetVersion)` reference the
# notes themselves contain and 36 from `&lt;`/`&gt;` unescaping. The nuspec receives the EVALUATED
# value, so the smaller number is the one nuget.org counted when it returned its 400. A gate that
# grepped the file would have scored the wrong quantity, which is this item's own defect class.
FRAGMENT="$HERE/regression/release-notes-at-2579.xmlfragment"
if [ ! -s "$FRAGMENT" ]; then
  bad "the pre-repair regression fragment must exist and be non-empty" "$FRAGMENT"
else
  mkdir -p "$WORK/pre-repair"
  printf '<Project><PropertyGroup><FsggCoherentSetVersion>0.52.0</FsggCoherentSetVersion></PropertyGroup></Project>\n' \
    > "$WORK/pre-repair/Directory.Build.props"
  {
    printf '<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup>\n'
    printf '    <Version>$(FsggCoherentSetVersion)</Version>\n'
    printf '    <PackageReleaseNotes>'
    cat "$FRAGMENT"
    printf '</PackageReleaseNotes>\n  </PropertyGroup>\n</Project>\n'
  } > "$WORK/pre-repair/Test.fsproj"
  printf 'PROJECTS = (\n    "%s",\n)\n' "$WORK/pre-repair/Test.fsproj" > "$WORK/pre-repair/coherent-set.py"

  run "$WORK/pre-repair/Test.fsproj"
  expect 1 "the tree that took nuget.org's 400 is RED on the length arm" \
    "is 37279 characters, which exceeds nuget.org's limit of 35000 by 2279"

  # NON-VACUITY: the fragment is the real thing, not a stub someone shortened. Asserted on the
  # EVALUATED length, in characters, because the file's byte count is a third, different number.
  if grep -q "37279 of 35000 characters used" <<<"$out"; then
    ok "the pre-repair fragment still evaluates to the measured 37,279 characters"
  else
    bad "the pre-repair fragment no longer evaluates to 37,279 characters" "$out"
  fi

  # AND THE SAME TREE WAS ALSO MISSING THE ADVISORY REFERENCE — which is what made a naive trim the
  # dangerous repair, and why the length arm did not ship alone.
  if grep -q "does not reference \$(FsggStandingAdvisories)" <<<"$out"; then
    ok "the pre-repair tree is also red on the advisory arm"
  else
    bad "the pre-repair tree should also be red on the advisory arm" "$out"
  fi
fi

shipped="$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj"
shipped_version="$(dotnet msbuild "$shipped" -getProperty:Version -getProperty:PackageId -nologo \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["Properties"]["Version"])')"
set +e
out="$(cd "$ROOT" && python3 "$CHECK" 2>&1)"
rc=$?
set -e
expect 0 "the shipped engine project is coherent, in-budget and carries its advisories" \
  "Version $shipped_version agrees"

# THE SHIPPED LISTING STILL CARRIES BOTH PERMANENT TWO-OF-THREE WARNINGS. This is the content
# 5d45ced4 had to restore once already; bounding the field must not lose it a second time. Read off
# the EVALUATED property, so an advisory that is declared but does not resolve into the published
# field fails here.
shipped_notes="$(cd "$ROOT" && dotnet msbuild "$shipped" -getProperty:PackageReleaseNotes -getProperty:Version -nologo \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["Properties"]["PackageReleaseNotes"])')"
missing=""
for advisory in "DO NOT ADOPT 0.50.1" "DO NOT ADOPT 0.50.5"; do
  grep -qF -- "$advisory" <<<"$shipped_notes" || missing="$missing $advisory;"
done
if [ -z "$missing" ]; then
  ok "the shipped listing still warns about both permanent two-of-three sets"
else
  bad "the shipped listing lost a permanent two-of-three warning" "missing:$missing"
fi

echo
echo "== reach: subject, declaration, trigger =="

release_workflow="$ROOT/.github/workflows/release-coord-engine.yml"
checker_line="$(grep -n 'python3 scripts/check-engine-release-notes.py' "$release_workflow" | head -1 | cut -d: -f1 || true)"
pack_line="$(grep -n '^      - name: Pack$' "$release_workflow" | head -1 | cut -d: -f1 || true)"
if [ -n "$checker_line" ] && [ -n "$pack_line" ] && [ "$checker_line" -lt "$pack_line" ]; then
  ok "the release workflow refuses incoherent notes before packing"
else
  bad "the release workflow must run the checker before Pack" \
    "checker line=${checker_line:-missing}; Pack line=${pack_line:-missing}"
fi

# THE LIMIT IS EXPRESSED ONCE (.github#2579 criterion 2). A second copy in a workflow or a fixture is
# a copy that drifts the day nuget.org moves the number, and the drifting copy is the one an author
# will read.
#
# "EXPRESSED" MEANS AN EXECUTABLE DEFINITION, NOT AN OCCURRENCE OF THE DIGITS. The checker's own
# docstring QUOTES nuget.org's refusal verbatim — "may not be more than 35000 characters long" — and
# that citation is the point of naming the constraint as external rather than a second authority to
# drift from. So this leg asserts two separable things: exactly one assignment, and no occurrence at
# all outside the file that carries it. Neither alone is the claim.
holder="scripts/check-engine-release-notes.py"
assignments="$(cd "$ROOT" && grep -c "^NUGET_ORG_RELEASE_NOTES_LIMIT = $LIMIT\$" "$holder" || true)"
elsewhere="$(cd "$ROOT" && grep -rn --exclude-dir=regression -- "$LIMIT" \
  scripts/ .github/workflows/ tests/ \
  | grep -v "^$holder:" | grep -v '^tests/engine-release-notes/run.sh:' || true)"
if [ "$assignments" = "1" ] && [ -z "$elsewhere" ]; then
  ok "nuget.org's limit has exactly one definition, in the checker, and no copy anywhere else"
else
  bad "nuget.org's limit must be defined once in $holder and appear in no other file" \
    "assignments in $holder: $assignments (want 1); occurrences elsewhere:
$elsewhere"
fi

# .github#2512's ACTUAL DEFECT, PINNED HERE, AND .github#2579's EXTENSION OF IT. Every case above
# proves the CHECKER is right; none of them could have caught 0.50.5, because the checker was never
# invoked. The bump moved `FsggCoherentSetVersion` in Directory.Build.props and nothing else this
# workflow selected, so the PR arm did not run and `check-engine-release-notes` first spoke inside
# `release-coord-engine` — after two of the three packages had published irrevocably. A gate is only
# as wide as its trigger, so the trigger is asserted here, against the checker's own declared subject
# rather than a retyped list: widen PATHS_SUBJECT and this fails until the workflow follows.
#
# .github#2579 ADDS THE LINK BEFORE THAT ONE. The length arm's subject is whatever
# `check-coherent-set-version.py`'s `PROJECTS` says it is, so a fourth coherent-set member would be
# MEASURED automatically while its `.csproj` sat outside this workflow's filter — silently, and in
# exactly the direction #2512 already paid for. The chain asserted below is therefore
# PROJECTS ⊆ PATHS_SUBJECT ⊆ both triggers' paths, folded from the real files, never retyped.
if filter_report="$(python3 - "$ROOT" <<'PY'
import ast
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
checker = root / "scripts" / "check-engine-release-notes.py"
coherent = root / "scripts" / "check-coherent-set-version.py"
workflow = root / ".github" / "workflows" / "engine-release-notes.yml"


def fold_name(path, wanted):
    """Fold a module-level tuple as check-paths-coherence rule (c) folds it: literals, names, `+`.

    Read by AST, never by import: this fixture must not execute the gates it measures.
    """
    module = ast.parse(path.read_text())
    constants: dict[str, object] = {}

    def fold(node):
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            return fold(node.left) + fold(node.right)
        if isinstance(node, (ast.Tuple, ast.List)):
            return [entry for elt in node.elts for entry in fold(elt)]
        if isinstance(node, ast.Name):
            value = constants[node.id]
            return list(value) if isinstance(value, (list, tuple)) else [value]
        value = ast.literal_eval(node)
        return list(value) if isinstance(value, (list, tuple)) else [value]

    found = ()
    for node in module.body:
        if not isinstance(node, ast.Assign) or len(node.targets) != 1:
            continue
        target = node.targets[0]
        if not isinstance(target, ast.Name):
            continue
        if target.id == wanted:
            found = tuple(fold(node.value))
        else:
            try:
                constants[target.id] = ast.literal_eval(node.value)
            except ValueError:
                pass
    return found


subject = fold_name(checker, "PATHS_SUBJECT")
members = fold_name(coherent, "PROJECTS")

if not subject:
    print("could not fold PATHS_SUBJECT out of the checker")
    sys.exit(1)
if not members:
    print("could not fold PROJECTS out of check-coherent-set-version.py")
    sys.exit(1)

problems = []

# Link one: everything the length arm MEASURES must be something this gate's trigger can SEE.
for member in members:
    if member not in subject:
        problems.append(
            f"coherent-set member {member!r} is not in the checker's PATHS_SUBJECT, so an edit to it "
            f"alone would not start this gate"
        )

# Link two: everything in PATHS_SUBJECT must be selected by BOTH triggers.
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
print(
    f"all {len(members)} coherent-set member(s) are in PATHS_SUBJECT, and both triggers select all "
    f"{len(subject)} declared subject(s): {', '.join(subject)}"
)
PY
)"; then
  ok "PROJECTS is inside PATHS_SUBJECT, and both trigger filters select every PATHS_SUBJECT entry"
else
  bad "the reach chain PROJECTS -> PATHS_SUBJECT -> both triggers is broken" \
    "$filter_report"
fi

echo
echo "engine release-notes fixture: $pass passed, $fail failed"
[ "$fail" -eq 0 ]

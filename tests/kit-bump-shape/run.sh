#!/usr/bin/env bash
# Fixture for scripts/check-kit-bump-shape.py — .github#1693, correcting a premise of #1587.
#
# NO NETWORK, NO DOTNET. The gate's three inputs are a git diff, a package's kit-manifest.tsv +
# nuspec, and the receiver's evaluated MSBuild properties. All three are files, so every leg here
# builds a real git repository and a real (synthetic) extracted package and runs the gate for real.
#
# THE POSITIVE LEG IS THE MEASURED SHAPE, not an invented one. FS.GG.Net 0.8.0 -> 0.15.0,
# materialized against a COPY of the real receiver (#1693), is 35 paths: 1 pin, 3 added and 9
# modified materialized outputs, and 23 deletions under `.codex/skills` — and FS.GG.SDD and
# FS.GG.Templates, the other two pin locations, measure the same shape. Leg 1 is that shape,
# scaled down to two skills; leg 2 is the reason this gate had to be corrected at all — the SAME
# diff, judged the way #1587 was written, is refused.
#
# The manifest's sha256 column is a placeholder throughout: this gate NEVER hashes anything. It
# decides a diff's shape from declared destinations. The materialize itself is content-addressed
# (ADR-0014) and is a different assertion in a different place.

set -uo pipefail
export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-kit-bump-shape.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/kit-bump-shape-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

SHA=$(printf '0%.0s' {1..64})

# ---------------------------------------------------------------------------------------------
# The target package. `kind: skill` destinations are root-relative; everything else is
# receiver-root-relative. This is the 0.15.0 row set in miniature: two skills, the two clients
# (#1696 added `skill-view`), two configs, and the opt-in build-config row.
# ---------------------------------------------------------------------------------------------
make_package() { # $1 = dir, $2 = version, $3 = "with-skills" | "no-skills" | "no-manifest"
  local dir="$1" version="$2" mode="${3:-with-skills}"
  mkdir -p "$dir/kit"
  printf '<?xml version="1.0"?><package><metadata><id>FS.GG.Kit</id><version>%s</version></metadata></package>\n' \
    "$version" > "$dir/fs.gg.kit.nuspec"
  [ "$mode" = "no-manifest" ] && return 0
  {
    if [ "$mode" = "with-skills" ]; then
      printf 'skill\tskills/check-board/SKILL.md\tcheck-board/SKILL.md\t%s\tfalse\n' "$SHA"
      printf 'skill\tskills/check-board/references/deep-detail.md\tcheck-board/references/deep-detail.md\t%s\tfalse\n' "$SHA"
      printf 'skill\tskills/pnext-item/SKILL.md\tpnext-item/SKILL.md\t%s\tfalse\n' "$SHA"
    fi
    printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\ttrue\n' "$SHA"
    printf 'client\tclient/skill-view\tscripts/skill-view\t%s\ttrue\n' "$SHA"
    printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\tfalse\n' "$SHA"
    printf 'config\tconfig/roots.sh\tscripts/lib/roots.sh\t%s\tfalse\n' "$SHA"
    printf 'build-config\tbuild-config/Directory.Build.props\tDirectory.Build.props\t%s\tfalse\n' "$SHA"
  } > "$dir/kit/kit-manifest.tsv"
}

# `dotnet msbuild <receiver.proj> -getProperty:...` emits exactly this object. Hand-writing it here
# stands in for the receiver's evaluation; the gate treats it as opaque and reads no default of its own.
make_props() { # $1 = file, $2 = live, $3 = retired, $4 = view, $5 = build-config
  cat > "$1" <<EOF
{
  "Properties": {
    "FsggKitSkillRoots": "$2",
    "FsggKitRetiredSkillRoots": "$3",
    "FsggKitViewSkillRoots": "$4",
    "FsggKitMaterializeBuildConfig": "$5"
  }
}
EOF
}

GIT() { git -C "$1" -c user.email=fixture@fs.gg -c user.name=fixture -c commit.gpgsign=false "${@:2}"; }

# The receiver BEFORE the bump: pinned at 0.8.0, three committed skill roots (the pre-0.14.0 world),
# the client, the tool manifest, one hand-authored source file, and — deliberately — a skill of the
# receiver's OWN under the root that 0.14.0 retires. The materializer never touches that one.
make_receiver() { # $1 = dir
  local d="$1"
  mkdir -p "$d/.claude/skills/check-board" "$d/.claude/skills/pnext-item" \
           "$d/.agents/skills/check-board" "$d/.agents/skills/pnext-item" \
           "$d/.codex/skills/check-board" "$d/.codex/skills/pnext-item" \
           "$d/.codex/skills/receiver-own" "$d/scripts" "$d/.config" "$d/src"
  cat > "$d/Directory.Packages.props" <<'EOF'
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="FS.GG.Kit" Version="0.8.0" />
  </ItemGroup>
</Project>
EOF
  for root in .claude/skills .agents/skills .codex/skills; do
    echo "old check-board" > "$d/$root/check-board/SKILL.md"
    echo "old pnext-item"  > "$d/$root/pnext-item/SKILL.md"
  done
  echo "a skill this receiver wrote itself" > "$d/.codex/skills/receiver-own/SKILL.md"
  echo "old client"  > "$d/scripts/fsgg-coord"
  echo "old tools"   > "$d/.config/dotnet-tools.json"
  echo "hand-authored" > "$d/src/app.txt"
  GIT "$d" init -q -b main
  GIT "$d" add -A
  GIT "$d" commit -q -m "receiver at FS.GG.Kit 0.8.0"
  GIT "$d" tag base
}

# The MEASURED 0.15.0 materialize, in miniature: the pin moves; the two live roots are written and
# gain a new reference file; the two new clients/configs appear; and the retired root's KIT skill
# directories are deleted while the receiver's own survives.
apply_bump() { # $1 = dir
  local d="$1"
  sed -i 's/Version="0.8.0"/Version="0.15.0"/' "$d/Directory.Packages.props"
  for root in .claude/skills .agents/skills; do
    echo "new check-board" > "$d/$root/check-board/SKILL.md"
    mkdir -p "$d/$root/check-board/references"
    echo "new detail" > "$d/$root/check-board/references/deep-detail.md"
  done
  echo "new client" > "$d/scripts/fsgg-coord"
  echo "new tools"  > "$d/.config/dotnet-tools.json"
  echo "view tool"  > "$d/scripts/skill-view"
  mkdir -p "$d/scripts/lib"; echo "roots lib" > "$d/scripts/lib/roots.sh"
  rm -rf "$d/.codex/skills/check-board" "$d/.codex/skills/pnext-item"
}

# $1 = expected exit, $2 = name, $3 = repo, $4 = package dir, $5 = props file.
# The gate's own output lands in $LEG_OUT — a global, NOT this function's stdout, so that a leg
# which also asserts the WORDING cannot swallow its own PASS/FAIL line in a command substitution
# (and cannot lose the counters to a subshell, which is how a fixture reports 19/1 for 20 legs).
LEG_OUT=""
run_leg() {
  local want="$1" name="$2" repo="$3" pkg="$4" props="$5" rc=0
  GIT "$repo" add -A
  GIT "$repo" commit -q -m "bump" --allow-empty
  LEG_OUT="$(python3 "$GATE" --repo "$repo" --base base --head HEAD --kit-dir "$pkg" --properties "$props" 2>&1)" || rc=$?
  if [ "$rc" = "$want" ]; then ok "$name (exit $rc)"; else bad "$name — expected exit $want, got $rc" "$LEG_OUT"; fi
}

fresh() { # $1 = leg name -> echoes a fresh receiver dir
  local d="$WORK/$1"
  rm -rf "$d"; make_receiver "$d" >/dev/null 2>&1; echo "$d"
}

make_package "$WORK/pkg-0.15.0" "0.15.0"
make_package "$WORK/pkg-0.14.0" "0.14.0"
make_package "$WORK/pkg-noskills" "0.15.0" "no-skills"
make_package "$WORK/pkg-nomanifest" "0.15.0" "no-manifest"
PKG="$WORK/pkg-0.15.0"

# The receiver's evaluation of 0.15.0's declarations, and the three variants the legs need.
make_props "$WORK/props.json"          ".claude/skills;.agents/skills" ".codex/skills" ""               "false"
make_props "$WORK/props-1587.json"     ".claude/skills;.agents/skills" ""              ""               "false"
make_props "$WORK/props-view.json"     ".claude/skills"                ".codex/skills" ".agents/skills" "false"
make_props "$WORK/props-bc.json"       ".claude/skills;.agents/skills" ".codex/skills" ""               "true"
make_props "$WORK/props-clash.json"    ".claude/skills;.agents/skills" ".codex/skills" ".codex/skills"  "false"
make_props "$WORK/props-noroots.json"  ""                              ".codex/skills" ""               "false"

echo "== the class this admits =="

# 1 — AC 1 of #1693 / AC 1 of #1587. The measured shape is MECHANICAL.
d=$(fresh mechanical); apply_bump "$d"
run_leg 0 "the measured 0.8.0 -> 0.15.0 shape (pin + two-root materialize + retired-root deletions) is mechanical" "$d" "$PKG" "$WORK/props.json"
case "$LEG_OUT" in
  *"mechanical FS.GG.Kit bump to 0.15.0"*) ok "  and says so by naming the target version" ;;
  *) bad "  the pass did not name the target version" "$LEG_OUT" ;;
esac
# The bump touches 9 paths: the pin, 4 modified and 4 added materialized outputs, 2 deletions. The
# summary must count the pin AS the pin — a human checking it by hand against `git diff --stat` has
# to be able to add the numbers up, and folding the pin into the modified count makes them not.
case "$LEG_OUT" in
  *"4 added / 4 modified"*) ok "  and counts the pin as the pin, not as a materialized output" ;;
  *) bad "  the summary miscounts the pin among the materialized outputs" "$LEG_OUT" ;;
esac

# 2 — THE PREMISE CORRECTION ITSELF, as a negative control. The same diff, judged the way #1587 was
# written (materialize targets = FsggKitSkillRoots, nothing else), is refused. If this leg ever goes
# green, the correction has been undone and every receiver's bump is about to be blocked again.
d=$(fresh premise); apply_bump "$d"
run_leg 1 "#1587 as written (no retired-root state) REFUSES the same measured bump — the defect #1693 corrects" "$d" "$PKG" "$WORK/props-1587.json"
case "$LEG_OUT" in
  *".codex/skills/check-board/SKILL.md"*) ok "  and names the retired-root deletions as the reason" ;;
  *) bad "  the refusal did not name the retired-root deletions" "$LEG_OUT" ;;
esac

echo "== it still refuses what it exists to refuse =="

# 3 — #1587 AC 2: a contaminated tree. One hand-edited source file rides along.
d=$(fresh contaminated); apply_bump "$d"; echo "a change nobody reviewed" >> "$d/src/app.txt"
run_leg 1 "a bump carrying an unrelated source edit is contaminated" "$d" "$PKG" "$WORK/props.json"

# 4 — #1693 AC 2: a deletion under a root the target version does NOT declare retired. The extra
# root must exist AT THE BASE — a create-then-delete inside the same `base...head` range cancels
# out and the leg silently tests nothing, which is exactly how this fixture first passed wrongly.
d=$(fresh undeclared-root)
mkdir -p "$d/.opencode/skills/check-board"; echo x > "$d/.opencode/skills/check-board/SKILL.md"
GIT "$d" add -A; GIT "$d" commit -q -m "receiver also holds a root nothing declares"
GIT "$d" tag -f base >/dev/null 2>&1
apply_bump "$d"; rm -rf "$d/.opencode/skills/check-board"
run_leg 1 "a deletion under a root declared nowhere is contaminated" "$d" "$PKG" "$WORK/props.json"

# 5 — the sharp edge. The retired-root sweep removes the KIT's own skill directories and leaves a
# receiver's own alone, so removing one is not something a materialize can produce.
d=$(fresh own-skill); apply_bump "$d"; rm -rf "$d/.codex/skills/receiver-own"
run_leg 1 "deleting the receiver's OWN skill under the retired root is contaminated" "$d" "$PKG" "$WORK/props.json"

# 6 / 7 — #1693 AC 3: a retired root admits deletions ONLY.
d=$(fresh retired-add); apply_bump "$d"
mkdir -p "$d/.codex/skills/check-board"; echo "written back" > "$d/.codex/skills/check-board/SKILL.md"
run_leg 1 "an ADD under a retired root is contaminated" "$d" "$PKG" "$WORK/props.json"

d=$(fresh retired-mod); apply_bump "$d"; echo "edited" > "$d/.codex/skills/receiver-own/SKILL.md"
run_leg 1 "a MODIFY under a retired root is contaminated" "$d" "$PKG" "$WORK/props.json"

echo "== the third root kind (#1696) =="

# 8 — a VIEW root's previously-materialized copies are swept exactly as a retired root's, so its
# deletions are admissible. Here `.agents/skills` is the view root: it is still a runtime root, but
# its content is generated by `scripts/skill-view`, never committed and never materialized into.
d=$(fresh view-delete)
sed -i 's/Version="0.8.0"/Version="0.15.0"/' "$d/Directory.Packages.props"
echo "new check-board" > "$d/.claude/skills/check-board/SKILL.md"
mkdir -p "$d/.claude/skills/check-board/references"; echo d > "$d/.claude/skills/check-board/references/deep-detail.md"
echo "new client" > "$d/scripts/fsgg-coord"; echo "new tools" > "$d/.config/dotnet-tools.json"
echo v > "$d/scripts/skill-view"; mkdir -p "$d/scripts/lib"; echo r > "$d/scripts/lib/roots.sh"
rm -rf "$d/.codex/skills/check-board" "$d/.codex/skills/pnext-item"
rm -rf "$d/.agents/skills/check-board" "$d/.agents/skills/pnext-item"
run_leg 0 "deletions under a declared VIEW root are admitted, like a retired root's" "$d" "$PKG" "$WORK/props-view.json"
case "$LEG_OUT" in
  *".agents/skills"*) ok "  and the pass names the view root among the swept roots" ;;
  *) bad "  the pass did not name the view root" "$LEG_OUT" ;;
esac

# 9 — but a view root is delete-only too: nothing may be written there. `skill-view` refuses to
# generate over a root git tracks, so a committed file under a view root is exactly the wedge #1696
# describes, not a materialize output.
d=$(fresh view-add)
sed -i 's/Version="0.8.0"/Version="0.15.0"/' "$d/Directory.Packages.props"
echo "new client" > "$d/scripts/fsgg-coord"; echo "new tools" > "$d/.config/dotnet-tools.json"
echo v > "$d/scripts/skill-view"; mkdir -p "$d/scripts/lib"; echo r > "$d/scripts/lib/roots.sh"
echo "new check-board" > "$d/.claude/skills/check-board/SKILL.md"
mkdir -p "$d/.claude/skills/check-board/references"; echo d > "$d/.claude/skills/check-board/references/deep-detail.md"
rm -rf "$d/.codex/skills/check-board" "$d/.codex/skills/pnext-item"
echo "materialized into a view root" > "$d/.agents/skills/check-board/SKILL.md"
run_leg 1 "a WRITE under a declared view root is contaminated" "$d" "$PKG" "$WORK/props-view.json"

echo "== the sets are DERIVED, not restated (#1693 AC 4, #1587 AC 4) =="

# 10 — drop `pnext-item` from the TARGET package's manifest and its deletions stop being admissible,
# with no edit to the gate. That is the whole claim of AC 4, exercised rather than asserted.
make_package "$WORK/pkg-one-skill" "0.15.0"
grep -v 'pnext-item' "$WORK/pkg-0.15.0/kit/kit-manifest.tsv" > "$WORK/pkg-one-skill/kit/kit-manifest.tsv"
d=$(fresh derived); apply_bump "$d"
run_leg 1 "a skill absent from the TARGET manifest is not sweepable — the answer follows the package" "$d" "$WORK/pkg-one-skill" "$WORK/props.json"

# 11 / 12 — build-config is opt-in PER RECEIVER, so the same diff has two correct answers and the
# gate must read the receiver's evaluation rather than the package's row set.
d=$(fresh bc-off); apply_bump "$d"; echo "<Project />" > "$d/Directory.Build.props"
run_leg 1 "a build-config write is contaminated for a receiver that does not receive build-config" "$d" "$PKG" "$WORK/props.json"

d=$(fresh bc-on); apply_bump "$d"; echo "<Project />" > "$d/Directory.Build.props"
run_leg 0 "the SAME write is mechanical for a receiver that does" "$d" "$PKG" "$WORK/props-bc.json"

echo "== fail closed: 'I cannot decide' is spelled differently from 'it is fine' =="

# 13 — abstention. A PR that moves no pin gets NO verdict, and the exit code says so distinctly.
d=$(fresh no-pin); echo "new client" > "$d/scripts/fsgg-coord"
run_leg 2 "a PR with no pin change ABSTAINS (exit 2), which is not a pass" "$d" "$PKG" "$WORK/props.json"
case "$LEG_OUT" in
  *"NOT a pass"*) ok "  and says in words that abstention must not automerge" ;;
  *) bad "  abstention did not say it is not a pass" "$LEG_OUT" ;;
esac

# 14 — the pin file is not a licence to edit that file. A second changed line is a refusal.
d=$(fresh dirty-pin); apply_bump "$d"
sed -i 's|<ManagePackageVersionsCentrally>true|<ManagePackageVersionsCentrally>false|' "$d/Directory.Packages.props"
run_leg 3 "a pin file with a second, non-pin changed line is REFUSED" "$d" "$PKG" "$WORK/props.json"

# 14b — removing the pin is an OFFBOARDING, not a bump. There is no mechanical class for it, and
# saying so is different from calling the diff contaminated.
d=$(fresh drop-pin); apply_bump "$d"
sed -i '/Include="FS.GG.Kit"/d' "$d/Directory.Packages.props"
run_leg 3 "a PR that REMOVES the pin is REFUSED, not classified as a bump" "$d" "$PKG" "$WORK/props.json"

# 15 — the package handed to the gate must be the version the diff bumps TO, or there is no verdict.
d=$(fresh wrong-pkg); apply_bump "$d"
run_leg 3 "a --kit-dir that is not the version the pin moves to is REFUSED" "$d" "$WORK/pkg-0.14.0" "$WORK/props.json"

# 16 — the materializer refuses a root declared twice rather than choosing a disposition; so must this.
d=$(fresh clash); apply_bump "$d"
run_leg 3 "a root declared BOTH retired and view is REFUSED" "$d" "$PKG" "$WORK/props-clash.json"

# 17 / 18 — a subject that cannot support a verdict is never an empty pass (epic #266).
d=$(fresh noskills); apply_bump "$d"
run_leg 3 "a target package whose manifest has no skill rows is REFUSED" "$d" "$WORK/pkg-noskills" "$WORK/props.json"

d=$(fresh nomanifest); apply_bump "$d"
run_leg 3 "a target package with no kit-manifest.tsv is REFUSED" "$d" "$WORK/pkg-nomanifest" "$WORK/props.json"

# 19 — an empty live root set would make every materialized path a finding; refuse instead.
d=$(fresh noroots); apply_bump "$d"
run_leg 3 "an empty FsggKitSkillRoots is REFUSED" "$d" "$PKG" "$WORK/props-noroots.json"

# 20 — the evaluated properties must actually carry the declarations. A caller who forgets the
# view-root property gets a refusal, not a silently narrower class.
cat > "$WORK/props-short.json" <<'EOF'
{ "Properties": { "FsggKitSkillRoots": ".claude/skills", "FsggKitRetiredSkillRoots": ".codex/skills" } }
EOF
d=$(fresh short-props); apply_bump "$d"
run_leg 3 "properties missing FsggKitViewSkillRoots are REFUSED, not defaulted" "$d" "$PKG" "$WORK/props-short.json"

echo
echo "kit-bump-shape: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

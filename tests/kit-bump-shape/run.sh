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

# The same, with NO package and NO evaluated properties — the cheap first pass a receiver-side caller
# makes on EVERY pull request before it pays for an SDK, a restore and an MSBuild evaluation
# (.github#1713). Only two answers are reachable from a git diff alone, and neither may be a pass.
run_leg_bare() {
  local want="$1" name="$2" repo="$3" rc=0
  GIT "$repo" add -A
  GIT "$repo" commit -q -m "bump" --allow-empty
  LEG_OUT="$(python3 "$GATE" --repo "$repo" --base base --head HEAD 2>&1)" || rc=$?
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

# 3 — the MIDDLE CLASS (#1726 / #1713). One hand-edited receiver source file rides along. Kit
# territory is untouched, so this is `mechanical+repair` (exit 4) and NOT a pass — see legs 21-27,
# which measure the class on the real pull request it was filed from.
d=$(fresh repair); apply_bump "$d"; echo "a change nobody reviewed" >> "$d/src/app.txt"
run_leg 4 "a bump carrying a receiver-authored source edit is mechanical + repair, not contamination" "$d" "$PKG" "$WORK/props.json"
case "$LEG_OUT" in
  *"MECHANICAL + RECEIVER-SIDE REPAIR"*"src/app.txt"*) ok "  and names the class in words, then the file (#1726 AC 3)" ;;
  *) bad "  the report did not name the class and the receiver-authored file" "$LEG_OUT" ;;
esac
case "$LEG_OUT" in
  *"NOT A PASS"*) ok "  and says the milder verdict is still not a pass (#266)" ;;
  *) bad "  the report did not say it is not a pass" "$LEG_OUT" ;;
esac

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

echo "== the counterexample, at full size: FS.GG.Rendering#1088 (#1726 AC 1) =="

# THE REAL 36 PATHS, not a reconstruction. `gh api repos/FS-GG/FS.GG.Rendering/pulls/1088/files`,
# 2026-07-28: head 06a2a7748a0d642c5139ed34a47559bf0f6182c9, base f822e88ae874757ac0b7768dac4d60dace909f42,
# merged 2d64ee55c71ef2dfba6e312244e039d68a246a2d, `chore(deps): update dependency fs.gg.kit to 0.15.0`.
# The five classes below are that listing, partitioned; they sum to 36 and the fixture asserts it.
#
# THE PROPERTIES ARE THE ONES THAT PR WAS JUDGED UNDER, read from `.config/kit/FS.GG.Kit.receiver.proj`
# AT ITS HEAD SHA — where the file declared `FsggKitMaterializeBuildConfig=true` and NO root
# overrides, so the roots are 0.15.0's package defaults. Rendering has since moved `.agents/skills`
# to the view disposition (#1747); reading today's file would judge that PR under a tree it never had.
R1088_SKILL_DESTS="
check-board/SKILL.md
check-board/agents/openai.yaml
check-board/references/deep-detail.md
check-board/references/judgement-findings.md
check-board/references/mechanical-reconciliation.md
cross-repo-coordination/SKILL.md
cross-repo-coordination/agents/openai.yaml
cross-repo-coordination/references/coherent-releases.md
cross-repo-coordination/references/contract-changes.md
cross-repo-coordination/references/deep-detail.md
cross-repo-coordination/references/mailbox-and-board.md
intra-repo-parallel-work/SKILL.md
intra-repo-parallel-work/agents/openai.yaml
intra-repo-parallel-work/references/deep-detail.md
intra-repo-parallel-work/references/protocol-facts.md
intra-repo-parallel-work/references/worktrees-and-overlap.md
pnext-item/SKILL.md
pnext-item/agents/openai.yaml
pnext-item/references/command-contracts.md
pnext-item/references/deep-detail.md
pnext-item/references/findings-and-filing.md
pnext-item/references/merge-and-release.md
pnext-item/references/performance-first.md
"
# The three skill files the bump actually rewrote, in EACH of the two live roots (6 of the 36).
R1088_TOUCHED="pnext-item/SKILL.md pnext-item/references/command-contracts.md pnext-item/references/deep-detail.md"

# 0.15.0's non-skill rows, with the kinds registry/repos.yml `kit:` declares (#1696 added the last
# three). `build-config` is Directory.Build.props + Directory.Packages.props, from sync-build-config.sh.
mkdir -p "$WORK/pkg-1088/kit"
printf '<?xml version="1.0"?><package><metadata><id>FS.GG.Kit</id><version>0.15.0</version></metadata></package>\n' \
  > "$WORK/pkg-1088/fs.gg.kit.nuspec"
{
  for dest in $R1088_SKILL_DESTS; do printf 'skill\tskills/%s\t%s\t%s\tfalse\n' "$dest" "$dest" "$SHA"; done
  printf 'client\tclient/fsgg-coord\tscripts/fsgg-coord\t%s\ttrue\n' "$SHA"
  printf 'client\tclient/skill-view\tscripts/skill-view\t%s\ttrue\n' "$SHA"
  printf 'config\tconfig/dotnet-tools.json\t.config/dotnet-tools.json\t%s\tfalse\n' "$SHA"
  printf 'config\tconfig/args.sh\tscripts/lib/args.sh\t%s\tfalse\n' "$SHA"
  printf 'config\tconfig/roots.sh\tscripts/lib/roots.sh\t%s\tfalse\n' "$SHA"
  printf 'build-config\tbuild-config/Directory.Build.props\tDirectory.Build.props\t%s\tfalse\n' "$SHA"
  printf 'build-config\tbuild-config/Directory.Packages.props\tDirectory.Packages.props\t%s\tfalse\n' "$SHA"
} > "$WORK/pkg-1088/kit/kit-manifest.tsv"
make_props "$WORK/props-1088.json" ".claude/skills;.agents/skills" ".codex/skills" "" "true"

# Rendering AT f822e88: pinned 0.14.0 in Directory.Packages.local.props (its OWN file — not a
# build-config member, which is why it is a legal pin location here), three committed skill roots
# holding all 23 kit destinations, the one client and the one config the kit shipped at 0.14.0, and
# `scripts/materialize-skill-roots.sh` — the file Rendering wrote, which the bump had to repair.
make_1088_base() { # $1 = dir
  local d="$1" root dest
  mkdir -p "$d/scripts/lib" "$d/.config"
  cat > "$d/Directory.Packages.local.props" <<'EOF'
<Project>
  <ItemGroup>
    <PackageVersion Include="FS.GG.Kit" Version="0.14.0" />
  </ItemGroup>
</Project>
EOF
  for root in .claude/skills .agents/skills .codex/skills; do
    for dest in $R1088_SKILL_DESTS; do
      mkdir -p "$d/$root/$(dirname "$dest")"
      echo "0.14.0 $dest" > "$d/$root/$dest"
    done
  done
  echo "0.14.0 client"  > "$d/scripts/fsgg-coord"
  echo "0.14.0 tools"   > "$d/.config/dotnet-tools.json"
  # DEFAULT_ROOTS=".claude/skills .codex/skills .agents/skills" — the stale three-root expectation.
  echo 'DEFAULT_ROOTS=".claude/skills .codex/skills .agents/skills"' > "$d/scripts/materialize-skill-roots.sh"
  GIT "$d" init -q -b main
  GIT "$d" add -A
  GIT "$d" commit -q -m "FS.GG.Rendering at FS.GG.Kit 0.14.0 (f822e88, in miniature)"
  GIT "$d" tag base
}

# The 35 paths the materializer and Renovate between them produced.
apply_1088_mechanical() { # $1 = dir
  local d="$1" root dest
  sed -i 's/Version="0.14.0"/Version="0.15.0"/' "$d/Directory.Packages.local.props"
  for root in .claude/skills .agents/skills; do
    for dest in $R1088_TOUCHED; do echo "0.15.0 $dest" > "$d/$root/$dest"; done
  done
  echo "0.15.0 client" > "$d/scripts/fsgg-coord"
  echo "0.15.0 tools"  > "$d/.config/dotnet-tools.json"
  echo "0.15.0 view"   > "$d/scripts/skill-view"
  echo "0.15.0 args"   > "$d/scripts/lib/args.sh"
  echo "0.15.0 roots"  > "$d/scripts/lib/roots.sh"
  rm -rf "$d/.codex/skills"
}

# The 36th: the receiver-authored repair. `.codex/skills` left the contract at 0.14.0, so the
# three-root expectation is now wrong — and it is wrong ONLY from this commit onward.
apply_1088_repair() { echo 'DEFAULT_ROOTS=".claude/skills .agents/skills"' > "$1/scripts/materialize-skill-roots.sh"; }

fresh_1088() { local d="$WORK/$1"; rm -rf "$d"; make_1088_base "$d" >/dev/null 2>&1; echo "$d"; }

# 21 — the fixture is the real diff, or it proves nothing. A leg built from a path list that has
# drifted from the pull request would still go green, which is the failure mode this asserts away.
d=$(fresh_1088 r1088); apply_1088_mechanical "$d"; apply_1088_repair "$d"
GIT "$d" add -A; GIT "$d" commit -q -m "chore(deps): update dependency fs.gg.kit to 0.15.0"
n=$(GIT "$d" diff --no-renames --name-only base...HEAD | wc -l | tr -d ' ')
if [ "$n" = 36 ]; then ok "the FS.GG.Rendering#1088 fixture carries all 36 of the pull request's paths"
else bad "the FS.GG.Rendering#1088 fixture carries $n paths, not the pull request's 36"; fi

# 22 — THE DECIDED VERDICT (#1726 AC 1). 35 mechanical paths plus one receiver-authored repair.
rc=0
LEG_OUT="$(python3 "$GATE" --repo "$d" --base base --head HEAD --kit-dir "$WORK/pkg-1088" --properties "$WORK/props-1088.json" 2>&1)" || rc=$?
if [ "$rc" = 4 ]; then ok "FS.GG.Rendering#1088 is MECHANICAL + REPAIR (exit 4)"
else bad "FS.GG.Rendering#1088 — expected exit 4, got $rc" "$LEG_OUT"; fi
case "$LEG_OUT" in
  *"scripts/materialize-skill-roots.sh"*) ok "  and names scripts/materialize-skill-roots.sh as the file to read" ;;
  *) bad "  the report did not name the receiver-authored file" "$LEG_OUT" ;;
esac
case "$LEG_OUT" in
  *"1 receiver-authored file(s)"*) ok "  and bounds the reading at exactly one file of the thirty-six" ;;
  *) bad "  the report did not bound the reading" "$LEG_OUT" ;;
esac

echo "== and each verdict class reds when its own cause is mutated =="

# 23 — MUTATION of leg 22: drop the repair and the SAME 35 paths are plain `mechanical`. This is what
# proves leg 22's exit 4 is caused by `scripts/materialize-skill-roots.sh` and not by the other 35.
d=$(fresh_1088 r1088-nofix); apply_1088_mechanical "$d"
run_leg 0 "MUTATION: FS.GG.Rendering#1088 WITHOUT the repair is plain mechanical" "$d" "$WORK/pkg-1088" "$WORK/props-1088.json"

# 24 — MUTATION: the same receiver-authored file, DELETED instead of edited. A deletion is the
# materializer's own vocabulary and this script can attribute this one to no sweep, so the milder
# class is not available: a human reads the whole diff.
d=$(fresh_1088 r1088-del); apply_1088_mechanical "$d"; rm -f "$d/scripts/materialize-skill-roots.sh"
run_leg 1 "MUTATION: DELETING the receiver-authored file is not-mechanical, not a repair" "$d" "$WORK/pkg-1088" "$WORK/props-1088.json"

# 25 — MUTATION: move the extra edit INSIDE kit territory. Same count, same statuses, different
# ground — and the verdict must flip, or `territory` is decorative.
d=$(fresh_1088 r1088-kit); apply_1088_mechanical "$d"
mkdir -p "$d/.codex/skills/pnext-item"; echo "written back" > "$d/.codex/skills/pnext-item/SKILL.md"
run_leg 1 "MUTATION: the same-sized edit INSIDE kit territory is not-mechanical" "$d" "$WORK/pkg-1088" "$WORK/props-1088.json"

# 26 — MUTATION: a repair AND a kit-territory finding together. The milder class is not a union, and
# the message must say which of the two decided it.
d=$(fresh_1088 r1088-both); apply_1088_mechanical "$d"; apply_1088_repair "$d"
mkdir -p "$d/.codex/skills/pnext-item"; echo "written back" > "$d/.codex/skills/pnext-item/SKILL.md"
run_leg 1 "MUTATION: a repair BESIDE a kit-territory finding is not-mechanical" "$d" "$WORK/pkg-1088" "$WORK/props-1088.json"
case "$LEG_OUT" in
  *"they are not what makes this verdict"*) ok "  and says the repair is not what decided it" ;;
  *) bad "  the refusal blamed the repair, or said nothing about it" "$LEG_OUT" ;;
esac

# 27 — MUTATION: the OTHER direction on build-config. Rendering receives it; a receiver that does not
# would have the same `Directory.Packages.props` write land in KIT territory as a finding, never as a
# repair — which is why `declared_dests` reads every row's dest and `flat` reads only the opted-in ones.
d=$(fresh_1088 r1088-bc); apply_1088_mechanical "$d"; echo "<Project />" > "$d/Directory.Packages.props"
run_leg 0 "MUTATION: a build-config write IS mechanical for Rendering, which receives build-config" "$d" "$WORK/pkg-1088" "$WORK/props-1088.json"
make_props "$WORK/props-1088-nobc.json" ".claude/skills;.agents/skills" ".codex/skills" "" "false"
d=$(fresh_1088 r1088-bc-off); apply_1088_mechanical "$d"; echo "<Project />" > "$d/Directory.Packages.props"
run_leg 1 "MUTATION: the SAME write is not-mechanical (kit territory) for a receiver that does not" "$d" "$WORK/pkg-1088" "$WORK/props-1088-nobc.json"
case "$LEG_OUT" in
  *"kit territory"*) ok "  and attributes it to kit territory, not to a receiver-authored repair" ;;
  *) bad "  the refusal did not say whose ground the finding is on" "$LEG_OUT" ;;
esac

echo "== the cheap first pass a receiver-side reporter makes on every pull request (#1713) =="

# 28 — no pin change, no package: the abstention costs a git diff and nothing else. This is what lets
# the receiver-side job run UNGATED on every pull request without an SDK (#1508's requirement that a
# producible context be produced on EVERY pull request, not just the ones with a paths: match).
d=$(fresh bare-nopin); echo "new client" > "$d/scripts/fsgg-coord"
run_leg_bare 2 "with no package at all, a PR that moves no pin still ABSTAINS" "$d"

# 29 — and the same call on a diff that DOES move the pin is a REFUSAL, never an abstention. If this
# leg ever returned 2, every receiver's bump PR would report 'not a kit bump' the moment a restore
# failed — the #266 fail-open this whole file exists to keep shut.
d=$(fresh_1088 bare-pin); apply_1088_mechanical "$d"
run_leg_bare 3 "with no package, a PR that DOES move the pin is REFUSED, not abstained" "$d"
case "$LEG_OUT" in
  *"--kit-dir"*"--properties"*) ok "  and names both inputs it was not given" ;;
  *) bad "  the refusal did not name the missing inputs" "$LEG_OUT" ;;
esac

echo "== the receiver-side producer's context name is a contract (#1713 AC 2) =="

# 30 — the context a receiver's branch protection would one day require is `<caller job> / <callee
# job display>`, and the right-hand half is defined in exactly ONE place: the `name:` of the
# `bump-shape` job in this repo's reusable kit-materialize.yml. `check-reusable-job-ids.py` already
# makes renaming it a loud, opt-out-able breaking change; this asserts the string ITSELF, so the
# documented context and the workflow cannot drift apart silently.
#
# Read with the standard library ALONE, like everything else here: this fixture installs nothing, and
# `actions/setup-python` does not ship PyYAML. The block is delimited by indentation, which is
# sufficient for a file this test also constrains.
JOBREAD='
import io, sys
want = sys.argv[2]
block, inside = [], False
for line in io.open(sys.argv[1], encoding="utf-8"):
    if line.startswith("  ") and not line.startswith("   ") and line.rstrip().endswith(":"):
        inside = line.strip() == "bump-shape:"
        continue
    if inside:
        block.append(line)
if want == "name":
    got = [l.split(":", 1)[1].strip() for l in block if l.startswith("    name:")]
    print(got[0] if got else "bump-shape")
elif want == "gate":
    print("gated" if any(l.startswith("    if:") for l in block) else "ungated")
elif want == "pin-include":
    # The ERE pass one greps with. Leg 33 builds the expected value from PIN_INCLUDE in the rule
    # itself and compares, so this is a translation of the rule, not a second opinion about pins.
    # Only the shell quotes come off — the ERE itself ENDS in a double quote, and stripping that
    # would compare a truncated pattern and pass on one that is not the rule.
    got = [l.split("=", 1)[1].strip().strip("\x27") for l in block
           if l.strip().startswith("PIN_INCLUDE=")]
    print(got[0] if got else "")
elif want == "probe-script":
    # The `run:` body of the step whose id is `probe`, dedented into a runnable script. The legs
    # below EXECUTE this rather than re-implementing it: a fixture that mirrors the shell it is
    # testing proves the mirror, and every drift between the two is invisible by construction.
    out, inside, taking = [], False, False
    for l in block:
        if l.startswith("      - "):
            inside, taking = False, False
        if l.strip() == "id: probe":
            inside = True
        if inside and l.startswith("        run: |"):
            taking = True
            continue
        if taking:
            if l.strip() and not l.startswith("          "):
                taking = False
                continue
            out.append(l[10:] if l.startswith("          ") else l)
    sys.stdout.write("".join(out))
elif want == "raw":
    # Code only. A `#` line is a comment in both YAML and the shell bodies, and this file discusses
    # the very constructs leg 38 forbids — matching prose would make the leg unpassable.
    sys.stdout.write("".join(l for l in block if not l.lstrip().startswith("#")))
elif want == "abstain-arm":
    # The `2)` arm of the exit-code mapping in the verdict step, flattened. Leg 37 asserts it does
    # not believe a bare exit 2 — which is also what argparse exits when it rejects a command line.
    print(" ".join(l.strip() for l in block).split("2) case")[-1].split(";;")[0]
          if "2) case" in " ".join(l.strip() for l in block) else "")
elif want == "foreign-checkouts":
    # Every step that checks out ANOTHER repository, as `<repository>|<ref>`. `with:` sits at 8
    # spaces inside a step, so its keys sit at 10; a step begins at 6. One line per such step, so
    # a leg can assert BOTH what is checked out and how its ref is chosen.
    steps, cur = [], None
    for l in block:
        if l.startswith("      - "):
            if cur is not None:
                steps.append(cur)
            cur = {}
            l = "        " + l[8:]
        if cur is None:
            continue
        s = l.strip()
        for key in ("repository:", "ref:"):
            if l.startswith("          " + key):
                cur[key[:-1]] = s.split(":", 1)[1].strip()
    if cur is not None:
        steps.append(cur)
    for st in steps:
        if "repository" in st:
            print("%s|%s" % (st["repository"], st.get("ref", "")))
elif want == "steps":
    # Every step in order, as `<what>|<id>|<if>`, so a leg can assert what runs BEFORE the cheap
    # probe decides and what is conditioned on it.
    steps, cur = [], None
    for l in block:
        if l.startswith("      - "):
            if cur is not None:
                steps.append(cur)
            cur = {"what": "", "id": "", "if": ""}
            l = "        " + l[8:]
        if cur is None:
            continue
        for key in ("uses:", "run:", "id:", "if:"):
            if l.startswith("        " + key):
                v = l.strip().split(":", 1)[1].strip()
                if key in ("uses:", "run:"):
                    cur["what"] = cur["what"] or (v if key == "uses:" else "run")
                else:
                    cur[key[:-1]] = cur[key[:-1]] or v
    if cur is not None:
        steps.append(cur)
    for st in steps:
        print("%s|%s|%s" % (st["what"], st["id"], st["if"]))
'
WF="$HERE/../../.github/workflows/kit-materialize.yml"
CONTEXT_CALLEE="$(python3 -c "$JOBREAD" "$WF" name)"
if [ "$CONTEXT_CALLEE" = "kit-bump-shape" ]; then
  ok "kit-materialize.yml publishes the callee context name 'kit-bump-shape'"
else
  bad "kit-materialize.yml publishes '$CONTEXT_CALLEE', not 'kit-bump-shape' — every receiver's caller job id is 'materialize', so the context is 'materialize / <this>'"
fi
# 31 — and the job must be UNGATED. An `if:` on it, or a paths filter on the workflow, is #1508's
# deadlock: GitHub creates no check run at all for a filtered-out job, and a branch that required
# the context would hold every pull request at "Expected — waiting for status to be reported".
if [ "$(python3 -c "$JOBREAD" "$WF" gate)" = "ungated" ]; then
  ok "  and the job carries no 'if:' — it reports on every pull request (#1508)"
else
  bad "  the bump-shape job is gated by an 'if:' — a filtered-out job creates NO check run (#1508)"
fi

echo "== the rule the reporter runs is fetched at the ref the RECEIVER's pin names (#1772, #1584) =="

# 32 — THE HEADLINE LEG, and the one that can fail. ADR-0067 §2: a gate's verdict must be a pure
# function of (tree under test, PINNED ref). #1713 shipped this job checking the rule out of
# `FS-GG/.github@main`, so a receiver's report was a function of when it ran — the measured #1584
# defect (`FS.GG.SDD#724`: green on `0376309` at 08:15Z, red on byte-identical content at 08:21Z).
#
# The property that forbids it, asserted structurally: every foreign-repository checkout in this job
# takes its `ref:` from a STEP OUTPUT — the commit resolved from the receiver's own restored pin —
# and never from a literal. Restoring `ref: main`, or any other branch or tag written by hand, fails
# this leg. That is the mutation, and it is the whole point of the leg existing.
FOREIGN="$(python3 -c "$JOBREAD" "$WF" foreign-checkouts)"
if [ -z "$FOREIGN" ]; then
  bad "the bump-shape job checks out no foreign repository at all — it cannot be running the rule"
else
  unpinned=""
  wrongrepo=""
  while IFS='|' read -r repo ref; do
    [ -n "$repo" ] || continue
    [ "$repo" = "FS-GG/.github" ] || wrongrepo="$wrongrepo $repo"
    case "$ref" in
      '${{ steps.'*'.outputs.'*'}}') : ;;
      *) unpinned="$unpinned $repo@${ref:-<none>}" ;;
    esac
  done <<EOF
$FOREIGN
EOF
  if [ -n "$wrongrepo" ]; then
    bad "the bump-shape job checks out an unexpected repository:$wrongrepo"
  else
    ok "the bump-shape job's only foreign checkout is FS-GG/.github (the rule's canonical home)"
  fi
  if [ -z "$unpinned" ]; then
    ok "  and its ref comes from a resolved step output, never a literal ref (#1772, ADR-0067 §2)"
  else
    bad "  a foreign checkout uses a LITERAL ref:$unpinned — the rule would come from a moving ref, so a receiver's verdict would change with no change to the receiver (#1584)"
  fi
fi

# 33 — pass one's pin test is the RULE's pin test, mechanically. The expected ERE is DERIVED here
# from `PIN_INCLUDE` in check-kit-bump-shape.py by the one documented translation (`\s` ->
# `[[:space:]]`), so the workflow cannot quietly narrow the predicate it is supposed to be a superset
# of. Editing either side without the other fails this leg.
WF_PIN="$(python3 -c "$JOBREAD" "$WF" pin-include)"
RULE_PIN="$(python3 - "$GATE" <<'PY'
import io, re, sys
m = re.search(r"^PIN_INCLUDE\s*=\s*re\.compile\(r'(.*)'\)", io.open(sys.argv[1], encoding="utf-8").read(), re.M)
print(m.group(1).replace(r"\s", "[[:space:]]") if m else "")
PY
)"
if [ -n "$WF_PIN" ] && [ "$WF_PIN" = "$RULE_PIN" ]; then
  ok "pass one greps the rule's own PIN_INCLUDE, translated to an ERE ('$WF_PIN')"
else
  bad "pass one greps '$WF_PIN' but the rule's PIN_INCLUDE translates to '$RULE_PIN' — the cheap probe and the rule disagree about what a pin declaration IS"
fi

# 37 — and the verdict step must not believe a bare exit 2. The rule is PINNED and this workflow is
# not, so the pinned rule can be older than the argv this file passes it — and `argparse` exits 2 on
# a command line it rejects, the same code as an abstention. Mapping that to "abstains" would report
# a green non-verdict for a rule that never ran. The arm must demand the word.
# 38 — and no leg of this job may use `grep -q`. Every grep here sits at the end of a pipeline under
# `set -o pipefail`: `-q` exits on the first match and closes the pipe, SIGPIPEing the greps upstream,
# and that 141 becomes the pipeline status — so a diff that DID match reads as no-match and the job
# abstains. It is a fail-open that only fires on inputs large enough to fill a pipe buffer, which is
# to say on real bump PRs and never in a fixture. `-c` consumes its whole input; use it.
qgreps="$(python3 -c "$JOBREAD" "$WF" raw | grep -cE 'grep +-[a-zA-Z]*q')" || qgreps=0
if [ "${qgreps:-0}" -eq 0 ]; then
  ok "  and no step in the job uses 'grep -q' — under pipefail that SIGPIPEs its own pipeline (#266)"
else
  bad "  $qgreps step(s) use 'grep -q' — under pipefail the SIGPIPE it causes upstream becomes the pipeline's status, so a matching diff reads as no-match and the job abstains"
fi

ABSTAIN_ARM="$(python3 -c "$JOBREAD" "$WF" abstain-arm)"
case "$ABSTAIN_ARM" in
  *ABSTAINS*) ok "  and a bare exit 2 is not believed to be an abstention unless the rule says ABSTAINS (argparse exits 2 too)" ;;
  *) bad "  the verdict step maps exit 2 straight to 'abstains' — but argparse exits 2 when it REJECTS the command line, so a pinned rule that does not accept this workflow's arguments would report a green non-verdict (#266)" ;;
esac

# 34/35 — THE SOUNDNESS OF THE CHEAP PATH, measured rather than argued. Pass one decides from a git
# diff alone whether to pay for an SDK, a restore and a pinned checkout, and it must never suppress a
# verdict the rule would have reached (#266). The implication asserted is:
#
#     the rule does NOT abstain  ==>  pass one sends the pull request on
#
# It is sound because the rule finds a pin file by a changed line matching `Include="FS.GG.Kit"`, and
# every such line contains the package id pass one greps for. Sound is not the same as true of the
# code, so it is checked here against real diffs — including the two measured bump shapes this file
# already builds. The converse is deliberately NOT asserted: pass one is a strict superset, and a
# diff that merely mentions the package may be sent on to abstain at the rule's own hands.
# THE WORKFLOW'S OWN PROBE, EXECUTED — not a re-implementation of it. Extracted from the `run:` body
# of the `probe` step and driven with the two environment variables the workflow env-passes it, so
# every leg below tests the shell that actually runs in seven repositories. A mirror would only ever
# prove the mirror: the first draft of this file had one, and deleting a whole question from the real
# probe left all of its legs green.
PROBE_SH="$WORK/probe.sh"
python3 -c "$JOBREAD" "$WF" probe-script > "$PROBE_SH"
if [ -s "$PROBE_SH" ] && grep -q 'GITHUB_OUTPUT' "$PROBE_SH"; then
  ok "the bump-shape probe step's shell was extracted and is what the legs below run"
else
  bad "could not extract the bump-shape probe step's shell from $WF — the legs below would prove nothing"
fi
prefilter() { # $1 = repo dir; exit 0 iff the workflow's own pass one would send this diff on
  local d="$1" out="$WORK/probe-out"
  : > "$out"
  ( cd "$d" && BASE=base HEAD=HEAD GITHUB_OUTPUT="$out" GITHUB_STEP_SUMMARY=/dev/null \
      bash "$PROBE_SH" ) >/dev/null 2>&1
  grep -q '^moved=true$' "$out"
}
# The rule's verdict on the SAME diff, with the package and properties a receiver-side pass two
# would have — so the corpus below exercises real verdicts (0/1/4), not just the exit 3 that a
# package-less call always returns. Without that this whole section would assert the superset
# property only against refusals, which is the weakest case it has.
check_prefilter() { # $1 = repo dir, $2 = description, $3 = package dir, $4 = properties
  local d="$1" what="$2" pkg="${3:-}" props="${4:-}" rc=0
  GIT "$d" add -A; GIT "$d" commit -q -m "bump" --allow-empty
  if [ -n "$pkg" ]; then
    python3 "$GATE" --repo "$d" --base base --head HEAD --kit-dir "$pkg" --properties "$props" >/dev/null 2>&1 || rc=$?
  else
    python3 "$GATE" --repo "$d" --base base --head HEAD >/dev/null 2>&1 || rc=$?
  fi
  if [ "$rc" = 2 ]; then
    if prefilter "$d"; then
      ok "  $what: the rule abstains and pass one sent it on anyway (a superset, which is allowed)"
    else
      ok "  $what: the rule abstains and pass one stops for free — no SDK, no restore"
    fi
  else
    if prefilter "$d"; then
      ok "  $what: the rule reaches a verdict (exit $rc) and pass one sends it on"
    else
      bad "  $what: the rule reaches a verdict (exit $rc) but pass one would STOP — that verdict would be silently reported as 'abstains' (#266 fail-open)"
    fi
  fi
}
d=$(fresh pf-nopin); echo "new client" > "$d/scripts/fsgg-coord"
check_prefilter "$d" "a pull request that moves no pin" "$PKG" "$WORK/props.json"
d=$(fresh pf-pin); apply_bump "$d"
check_prefilter "$d" "the measured 0.8.0 -> 0.15.0 bump, MECHANICAL" "$PKG" "$WORK/props.json"
d=$(fresh_1088 pf-1088); apply_1088_mechanical "$d"; apply_1088_repair "$d"
check_prefilter "$d" "FS.GG.Rendering#1088, MECHANICAL+REPAIR" "$WORK/pkg-1088" "$WORK/props-1088.json"
d=$(fresh_1088 pf-1088-bad); apply_1088_mechanical "$d"
mkdir -p "$d/.codex/skills/pnext-item"; echo "written back" > "$d/.codex/skills/pnext-item/SKILL.md"
check_prefilter "$d" "a bump contaminated inside kit territory, NOT MECHANICAL" "$WORK/pkg-1088" "$WORK/props-1088.json"
# The rule refuses on any diff status outside A/M/D, and that needs no kit content at all — so a
# `T`-status pull request is a verdict pass one must not suppress. This is the case a package-id
# probe got wrong: it mentions nothing kit-related, so it would have abstained GREEN where the rule
# refuses RED.
d=$(fresh pf-typechange); rm -f "$d/src/app.txt"; ln -s /dev/null "$d/src/app.txt"
check_prefilter "$d" "a file replaced by a symlink (diff status T), nothing kit-related" "$PKG" "$WORK/props.json"
# 35 — and the cheap path is still cheap: the cases that stop for free must actually stop.
d=$(fresh pf-cheap); echo "new client" > "$d/scripts/fsgg-coord"
GIT "$d" add -A; GIT "$d" commit -q -m "bump" --allow-empty
if prefilter "$d"; then
  bad "a pull request touching no FS.GG.Kit declaration would pay for an SDK and a restore — the ungated cost #1508 requires is gone"
else
  ok "a pull request touching no FS.GG.Kit declaration costs a git diff and nothing else (#1508)"
fi
# ...including one that MENTIONS FS.GG.Kit without declaring a version. A probe that grepped the
# package NAME would restore for this; the rule's own pin predicate does not.
d=$(fresh pf-mention); printf '# see FS.GG.Kit for the kit\n' >> "$d/src/app.txt"
GIT "$d" add -A; GIT "$d" commit -q -m "bump" --allow-empty
if prefilter "$d"; then
  bad "a pull request that merely names FS.GG.Kit in prose pays for an SDK and a restore"
else
  ok "  and so does one that merely names FS.GG.Kit in prose — the predicate is the pin, not the word"
fi

# 36 — AND THAT CHEAP PATH IS STRUCTURAL, not a claim about the shell inside one step. This job runs
# UNGATED on every pull request in seven repositories, so what it does BEFORE deciding whether this is
# a bump is the whole of its standing cost. Asserted in the workflow's own step order:
#
#   * exactly one step precedes the probe, and it is the receiver checkout — no SDK, no Python
#     install, and no second repository cloned before the question is even asked;
#   * every step AFTER the probe is conditioned on its answer, so a non-bump pull request runs none
#     of them. A missing `if:` here is how a restore silently becomes the ungated cost of every PR.
#
# This is what makes #1772's pinning affordable: resolving the rule from the receiver's pin needs the
# restore, and the restore is on the expensive side of this line.
STEPS="$(python3 -c "$JOBREAD" "$WF" steps)"
pre=0; unconditional=""; seen_probe=0; first=""
while IFS='|' read -r what id cond; do
  [ -n "$what" ] || continue
  if [ "$seen_probe" = 0 ] && [ "$id" != "probe" ]; then
    pre=$((pre+1)); first="${first:-$what}"
  elif [ "$id" = "probe" ]; then
    seen_probe=1
  else
    [ "$cond" = "steps.probe.outputs.moved == 'true'" ] || unconditional="$unconditional ${id:-$what}"
  fi
done <<EOF
$STEPS
EOF
if [ "$seen_probe" = 1 ] && [ "$pre" = 1 ] && [ "${first#actions/checkout}" != "$first" ]; then
  ok "the only thing an ungated pull request pays before the probe is the receiver checkout (#1508)"
else
  bad "$pre step(s) run before the bump-shape probe (first: '${first:-none}') — every pull request in seven repositories pays for them"
fi
if [ -z "$unconditional" ]; then
  ok "  and every step after it — SDK, restore, ref resolution, rule checkout, verdict — is conditioned on it"
else
  bad "  these run even when no pin moved:$unconditional — the SDK/restore cost is no longer paid only by bumps"
fi


# ---------------------------------------------------------------------------------------------
# THE OUTPUT-INJECTION GUARD, DRIVEN BOTH WAYS (.github#1797)
#
# WHY THESE LEGS EXIST. The guard that stops a pull-request-controlled `folder` forging extra
# `key=value` lines into `$GITHUB_OUTPUT` was written as:
#
#     case "$version$folder" in *"$(printf '\n')"*|*"$(printf '\r')"*) … exit 1 ;; esac
#
# Command substitution STRIPS TRAILING NEWLINES, so `$(printf '\n')` is the EMPTY STRING and that
# arm is `*""*` — i.e. `*`. It matched everything. From 12:30Z to 15:0xZ on 2026-07-28 the guard
# refused EVERY bump PR in EVERY receiver: a check that cannot PASS, shipped inside the pull
# request that was hardening this very block (#1772/#1780). Three sibling 0.18.0 bump branches
# were red simultaneously (FS.GG.Templates#328, and Audio/Net runs 30369036247 / 30369118964).
#
# WHAT THE LEGS HAVE TO COVER, and it is the lesson rather than the line: the original had only
# ever been exercised with hostile input, where "refuse" is the right answer and a `*` pattern is
# indistinguishable from a correct one. So BOTH directions are asserted here — refuse the newline
# AND the lone `\r`, and ACCEPT a clean pair. A leg set that omitted the accept case would
# reproduce the defect in the test, which is exactly how this shipped.
#
# THE GUARD IS EXTRACTED FROM THE WORKFLOW, NEVER RESTATED (ADR-0058). A copy of the `case` pasted
# here would pass forever while the workflow said something else — the #1059 class, in the fixture
# written to catch it. `WF` is already resolved above.
# STDLIB ONLY, NO PyYAML — this fixture's job installs neither PyYAML nor dotnet, which is the
# point of it ("no network, no dotnet"). An earlier draft of these legs used `yaml.safe_load` and
# died on `ModuleNotFoundError` in CI. It FAILED rather than passing, because the extraction guard
# below treats "I could not read the guard" as a finding and not a verdict (#266) — which is the
# behaviour these legs are supposed to have, demonstrated on themselves.
echo
guard="$(python3 - "$WF" <<'PY'
import re, sys
src = open(sys.argv[1], encoding="utf-8").read()
m = re.search(r'^([ \t]*)case "\$version\$folder" in\n.*?^\1esac$', src, re.S | re.M)
if not m:
    sys.stderr.write("could not locate the version/folder case in the workflow\n"); sys.exit(1)
indent = m.group(1)
print("\n".join(l[len(indent):] if l.startswith(indent) else l
                for l in m.group(0).splitlines()))
PY
)" || guard=""

if [ -z "$guard" ]; then
  bad "#1797: could not extract the version/folder shape guard from kit-materialize.yml — these legs did not run, and that is NOT a verdict about the guard"
else
  # drive <version> <folder> -> "refused" | "accepted"; runs the REAL extracted case under bash,
  # which is what Actions runs a shell-less `run:` block as on ubuntu-latest.
  drive() {
    bash -c '
      set -uo pipefail
      version="$1"; folder="$2"
      '"$guard"'
      echo accepted
    ' _ "$1" "$2" 2>/dev/null | tail -1
  }

  # (1) THE ACCEPT CASE — the leg whose absence let the bug ship. A real restored pair.
  if [ "$(drive '0.18.0' '/home/runner/.nuget/packages/')" = "accepted" ]; then
    ok "#1797: a clean single-line version + folder is ACCEPTED — the guard can PASS"
  else
    bad "#1797: the guard refuses a legitimate restored pair — every bump PR in all seven receivers is blocked (this is the 2026-07-28 defect)"
  fi

  # (2) …and it is not accepting because it never fires. A LITERAL newline in the folder — the
  #     real attack: a fork's nuget.config can put `&#xA;` in globalPackagesFolder.
  if [ "$(drive '0.18.0' "$(printf '/tmp/a\nkit-dir=/evil')")" != "accepted" ]; then
    ok "#1797: a folder carrying a literal NEWLINE is REFUSED — \$GITHUB_OUTPUT cannot be forged"
  else
    bad "#1797: a multi-line folder was accepted — the guard cannot FIRE, and a fork can forge step outputs"
  fi

  # (3) The carriage return separately, because it is the arm that was NOT degenerate and a
  #     "simplification" to `wc -l` (which counts newlines) would silently drop it.
  if [ "$(drive '0.18.0' "$(printf '/tmp/a\rkit-dir=/evil')")" != "accepted" ]; then
    ok "#1797: a lone CARRIAGE RETURN is REFUSED too — a newline-counting rewrite would miss this"
  else
    bad "#1797: a \\r-carrying folder was accepted — Actions treats CR as a line break in \$GITHUB_OUTPUT"
  fi

  # (4) The version side is guarded as well, not only the folder.
  if [ "$(drive "$(printf '0.18.0\nkit-version=99')" '/home/runner/.nuget/packages/')" != "accepted" ]; then
    ok "#1797: a multi-line VERSION is refused too — both halves of the concatenation are guarded"
  else
    bad "#1797: only the folder is guarded; a forged version reaches \$GITHUB_OUTPUT"
  fi

  # (5) THE REGRESSION LEG, and the one that names the defect rather than its symptom. The bug was
  #     a pattern that DEGENERATES TO THE EMPTY STRING. Assert the guard does not spell it that
  #     way, so a revert to `"$(printf '\n')"` reds here with the reason attached even if somebody
  #     also weakened legs (1)-(4).
  if printf '%s' "$guard" | grep -q '\$(printf'; then
    bad "#1797: the guard matches on a COMMAND SUBSTITUTION — \$(printf '\\n') strips the trailing newline and is the empty string, making the arm '*' and the check unable to pass" "$guard"
  else
    ok "#1797: the guard does not build its pattern with a command substitution — the construct that made it '*'"
  fi
fi

echo
echo "kit-bump-shape: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

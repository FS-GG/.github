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
# argv[3] = which job block to read; `bump-shape` unless named. #1815 added a SECOND job to this
# workflow, so a reader hard-wired to one job would silently answer about the wrong one.
jobid = sys.argv[3] if len(sys.argv) > 3 else "bump-shape"
block, inside = [], False
for line in io.open(sys.argv[1], encoding="utf-8"):
    if line.startswith("  ") and not line.startswith("   ") and line.rstrip().endswith(":"):
        inside = line.strip() == jobid + ":"
        continue
    if inside:
        block.append(line)
if want == "name":
    # A job with no `name:` publishes its JOB ID — so the fallback must be the id ASKED FOR, not a
    # literal. Hard-wiring "bump-shape" here made a DELETED job report the other job'"'"'s name, which
    # is a wrong answer dressed as a verdict (#266); the #1815 mutation run found it.
    got = [l.split(":", 1)[1].strip() for l in block if l.startswith("    name:")]
    print(got[0] if got else (jobid if block else "<no such job>"))
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
# 31 — and the job must report on EVERY pull request. An `if:` that excludes one, or a paths filter
# on the workflow, is #1508's deadlock: GitHub creates no check run at all for a filtered-out job,
# and a branch that required the context would hold every pull request at "Expected — waiting for
# status to be reported".
#
# THIS LEG USED TO BE STRUCTURAL — "the job carries no `if:`" — AND IT IS NOW SEMANTIC (.github#1845).
# The job carries `if: github.event_name == 'pull_request'`, which is a TAUTOLOGY on the event it
# reports about: the check run is still created on 100% of pull requests, and what it excludes is the
# on-demand `workflow_dispatch` run where there is no pull request to grade and no branch protection
# waiting on anything. The structural leg cannot tell that from a real narrowing, so the property is
# now asserted by EVALUATING the real expression against every pull-request shape the fixture can
# build — see the "#1845: which jobs run, on which events" section at the end of this file, where it
# is also mutation-proven (M3 narrows the `if:` by head ref and the legs go red). That is a strict
# superset of what this leg checked. What survives HERE is the half that is still structural and
# still cheap: there must be no `paths:`/`paths-ignore:` filter on the route, because a filtered-out
# job is the #1508 deadlock no expression evaluator can see.
if grep -qE '^[[:space:]]*paths(-ignore)?:' "$WF"; then
  bad "  kit-materialize.yml declares a paths filter — GitHub creates NO check run for a job a filter excluded, and branch protection cannot tell that from 'has not reported yet' (#1508)"
else
  ok "  and no paths filter exists anywhere on the route (#1508); which pull requests the job reports on is asserted semantically below"
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
# THE SECOND CONTEXT: `materialize / kit-bump-mechanical` (.github#1815)
#
# WHY IT EXISTS. `kit-bump-shape` is green on `mechanical` (0), on `mechanical + repair` (4) AND on
# `abstain` (2) — deliberately, and legs 22/27 above measure why 4 must stay green: merging
# FS.GG.Rendering#1088 was CORRECT. But a check run carries ONE BOOLEAN across the branch-protection
# and automerge boundary, so that context cannot express #1587's re-decision, "automerge the
# `mechanical` class only". Nothing reads an exit code there; both `automergeType: pr` (GitHub's
# merge API, consulting branch protection) and Renovate's own pre-merge poll read the conclusion.
#
# So the verdict gets a SECOND rendering, green iff the rule exits 0. The two contexts answer
# different questions and differ on exactly one class:
#
#     kit-bump-shape       "is this bump CONTAMINATED?"    green on 0, 2, 4
#     kit-bump-mechanical  "does this bump NEED A HUMAN?"  green on 0, 2
#
# BOTH MAPPINGS ARE EXTRACTED FROM THE WORKFLOW AND EXECUTED, NEVER RESTATED (ADR-0058) — the same
# discipline as the #1797 legs below, and for the same reason: a `case` pasted here would pass
# forever while the workflow said something else. The legs then drive them over ALL FIVE exit codes
# plus the no-verdict case, in both directions, so neither mapping can be one that cannot fail.
# ---------------------------------------------------------------------------------------------
echo
echo "== the second context, and that it disagrees with the first on exactly one class (#1815) =="

# 39 — the callee half of the second context string, defined in the workflow and nowhere else.
MECH_CALLEE="$(python3 -c "$JOBREAD" "$WF" name bump-mechanical)"
if [ "$MECH_CALLEE" = "kit-bump-mechanical" ]; then
  ok "kit-materialize.yml publishes the second callee context name 'kit-bump-mechanical'"
else
  bad "kit-materialize.yml's bump-mechanical job publishes '$MECH_CALLEE', not 'kit-bump-mechanical' — the context #1587 would arm is 'materialize / kit-bump-mechanical'"
fi

# 40 — THE FAIL-OPEN THIS JOB IS MOST EXPOSED TO, and the one leg that has to be structural because
# no shell can be driven to show it. The job takes `needs: bump-shape`, and `needs` ALONE SKIPS a
# dependent when its dependency FAILS. GitHub reports a skipped job as `conclusion: skipped`, which
# branch protection counts as SATISFIED — so `needs:` without an always-run `if:` inverts the mapping
# exactly where it matters: `not mechanical` (1) and `REFUSED` (3), the two classes that must NEVER
# automerge, would be the two that report GREEN. That is the "check that cannot fail" class this
# repo found ten of on 2026-07-27/28. Mutating the `if:` away, or narrowing it to a condition that
# is false when `bump-shape` fails, fires this leg.
MECH_IF="$(python3 -c "$JOBREAD" "$WF" gate bump-mechanical)"
MECH_RAW="$(python3 -c "$JOBREAD" "$WF" raw bump-mechanical)"
mech_needs="$(printf '%s\n' "$MECH_RAW" | grep -cE '^    needs: *bump-shape *$')" || mech_needs=0
# `!cancelled()` IS MATCHED AS A TERM, NOT AS THE WHOLE `if:` (.github#1845). The job now also
# carries `&& github.event_name == 'pull_request'`, so the old whole-line match would fail on a file
# that still has the property. What is load-bearing is that SOME status-check function appears at
# all: with none, GitHub applies the implicit `success()` and the job is skipped the moment
# `bump-shape` fails — which is this leg's entire subject. The `pull_request` term cannot restore
# that fail-open, because it is TRUE on every event where `bump-shape` can fail at all; that half is
# driven, not argued, in the "#1845: which jobs run, on which events" section below.
mech_always="$(printf '%s\n' "$MECH_RAW" | grep -cE '^    if: *\$\{\{ .*!cancelled\(\).*\}\} *$')" || mech_always=0
if [ "${mech_needs:-0}" -ge 1 ] && [ "${mech_always:-0}" -ge 1 ]; then
  ok "  and it is 'needs: bump-shape' + an 'if:' carrying '!cancelled()' — it still runs when bump-shape FAILS"
else
  bad "  the bump-mechanical job is needs=$mech_needs always=$mech_always (gate: $MECH_IF) — with 'needs:' and no always-run 'if:', GitHub SKIPS it when bump-shape fails, and branch protection counts a skipped job as SATISFIED: verdicts 1 and 3 would report GREEN"
fi

# 41 — and it must stay CHEAP, for the same reason leg 36 exists. This job runs ungated on every pull
# request in seven repositories. It re-derives nothing: no checkout, no SDK, no Python, no second
# clone — it reads the verdict `bump-shape` already reached. An independent job that re-ran pass one
# would make every non-bump PR in the fleet pay a second `fetch-depth: 0` checkout and every real
# bump a second restore, which is exactly the ungated cost #1508 forbids.
MECH_STEPS="$(python3 -c "$JOBREAD" "$WF" steps bump-mechanical)"
mech_n=0; mech_expensive=""
while IFS='|' read -r what id cond; do
  [ -n "$what" ] || continue
  mech_n=$((mech_n+1))
  case "$what" in run) : ;; *) mech_expensive="$mech_expensive $what" ;; esac
done <<EOF
$MECH_STEPS
EOF
if [ "$mech_n" = 1 ] && [ -z "$mech_expensive" ]; then
  ok "  and it is ONE 'run:' step — no checkout, no SDK, no Python: abstention stays a git diff (#1508)"
else
  bad "  the bump-mechanical job has $mech_n step(s), using:$mech_expensive — it re-derives what bump-shape already decided, and every pull request in seven repositories pays for it"
fi

# 42 — the verdict crosses between the two jobs by a declared job output, and the DEFAULT is empty.
# Every way bump-shape can end without grading anything — a failed restore, an unresolvable
# `kit/v<version>` tag, a dead runner, a failure path added later — leaves this unset, and leg 48
# asserts empty is a FAILURE on the new context. That is what makes #266 the default rather than a
# list somebody has to remember to extend.
SHAPE_RAW="$(python3 -c "$JOBREAD" "$WF" raw)"
if printf '%s\n' "$SHAPE_RAW" | grep -cE '^      verdict: \$\{\{ steps\.report\.outputs\.verdict \|\| steps\.probe\.outputs\.verdict \}\}$' >/dev/null; then
  ok "  and bump-shape publishes 'verdict' — the rule's own code, falling back to the probe's"
else
  bad "  bump-shape does not declare the 'verdict' job output the second context reads" "$SHAPE_RAW"
fi

# THE TWO MAPPINGS, EXTRACTED FROM THE WORKFLOW. If either cannot be found, that is a FINDING and not
# a verdict (#266) — the legs below would otherwise silently test nothing, which is how the #1797
# guard shipped unable to pass.
mech_case="$(python3 - "$WF" <<'PY'
import re, sys
src = open(sys.argv[1], encoding="utf-8").read()
m = re.search(r'^([ \t]*)case "\$VERDICT" in\n.*?^\1esac$', src, re.S | re.M)
if not m:
    sys.stderr.write("could not locate the VERDICT mapping in bump-mechanical\n"); sys.exit(1)
ind = m.group(1)
print("\n".join(l[len(ind):] if l.startswith(ind) else l for l in m.group(0).splitlines()))
PY
)" || mech_case=""
shape_case="$(python3 - "$WF" <<'PY'
import re, sys
src = open(sys.argv[1], encoding="utf-8").read()
# The LAST `case "$rc" in` in the file is the conclusion mapping; the earlier one picks the headline.
ms = list(re.finditer(r'^([ \t]*)case "\$rc" in\n.*?^\1esac$', src, re.S | re.M))
if not ms:
    sys.stderr.write("could not locate the rc conclusion mapping in bump-shape\n"); sys.exit(1)
m = ms[-1]
if "exit 0" not in m.group(0) or "exit 1" not in m.group(0):
    sys.stderr.write("the last rc case is not the conclusion mapping\n"); sys.exit(1)
ind = m.group(1)
print("\n".join(l[len(ind):] if l.startswith(ind) else l for l in m.group(0).splitlines()))
PY
)" || shape_case=""

if [ -z "$mech_case" ] || [ -z "$shape_case" ]; then
  bad "#1815: could not extract both conclusion mappings from kit-materialize.yml — the legs below did NOT run, and that is not a verdict about either context"
else
  ok "both conclusion mappings were extracted from the workflow and are what the legs below run"

  # `success` / `failure` — the two conclusions a job can carry. Anything else means the extracted
  # code neither exited 0 nor 1, which is a broken leg and must not read as either verdict.
  mech_conclusion() { # $1 = the verdict code bump-shape published
    local rc=0
    env GITHUB_STEP_SUMMARY=/dev/null bash -c '
      set -uo pipefail
      VERDICT="$1"
      '"$mech_case"'
      exit 99
    ' _ "$1" >/dev/null 2>&1 || rc=$?
    case "$rc" in 0) echo success ;; 1) echo failure ;; *) echo "BROKEN($rc)" ;; esac
  }
  shape_conclusion() { # $1 = the rule's exit code, after the 2 -> 3 remap
    local rc=0
    bash -c '
      set -uo pipefail
      rc="$1"
      '"$shape_case"'
      exit 99
    ' _ "$1" >/dev/null 2>&1 || rc=$?
    case "$rc" in 0) echo success ;; 1) echo failure ;; *) echo "BROKEN($rc)" ;; esac
  }

  # 43-48 — THE FULL TABLE, both contexts, all five exit codes plus the no-verdict case. Driven in
  # BOTH directions by construction: if either mapping could not fail, its `failure` rows would fail;
  # if either could not pass, its `success` rows would. Column 2 is #1815's whole change and column 1
  # is the proof that #1815 changed nothing about the existing context.
  #
  #    verdict           kit-bump-shape   kit-bump-mechanical
  while read -r code want_shape want_mech what; do
    [ -n "$code" ] || continue
    # A heredoc cannot carry an empty field, so the no-verdict row spells it.
    if [ "$code" = "EMPTY" ]; then code=""; fi
    got_mech="$(mech_conclusion "$code")"
    if [ "$got_mech" = "$want_mech" ]; then
      ok "  kit-bump-mechanical on ${code:-<no verdict>} ($what) is $want_mech"
    else
      bad "  kit-bump-mechanical on ${code:-<no verdict>} ($what) is $got_mech, want $want_mech"
    fi
    # The existing context, UNCHANGED. A no-verdict bump-shape is a FAILED JOB rather than a
    # conclusion this mapping chose, so it has no row here — the mapping never runs.
    if [ "$want_shape" != "-" ]; then
      got_shape="$(shape_conclusion "$code")"
      if [ "$got_shape" = "$want_shape" ]; then
        ok "    and kit-bump-shape on $code is still $want_shape — unchanged by #1815"
      else
        bad "    kit-bump-shape on $code is now $got_shape, want $want_shape — #1815 changed the EXISTING context's verdict"
      fi
    fi
  done <<'TABLE'
0     success   success   mechanical
2     success   success   abstains
4     success   failure   mechanical+repair
1     failure   failure   not-mechanical
3     failure   failure   REFUSED
EMPTY -         failure   bump-shape published no verdict
TABLE

  # 49 — AC 4, END TO END, ON THE REAL SUBJECT. Not a hand-written code: the REAL 36-path
  # FS.GG.Rendering#1088 diff, graded by the REAL rule, and its exit code fed to the REAL workflow
  # mappings. This is the whole claim of #1815 in one leg — the pull request whose merge was correct
  # stays green on `kit-bump-shape`, and stops being automergeable.
  d=$(fresh_1088 r1088-contexts); apply_1088_mechanical "$d"; apply_1088_repair "$d"
  GIT "$d" add -A; GIT "$d" commit -q -m "chore(deps): update dependency fs.gg.kit to 0.15.0"
  rc=0
  python3 "$GATE" --repo "$d" --base base --head HEAD --kit-dir "$WORK/pkg-1088" \
    --properties "$WORK/props-1088.json" >/dev/null 2>&1 || rc=$?
  if [ "$rc" = 4 ]; then
    if [ "$(shape_conclusion "$rc")" = success ] && [ "$(mech_conclusion "$rc")" = failure ]; then
      ok "FS.GG.Rendering#1088's real 36 paths: kit-bump-shape SUCCESS, kit-bump-mechanical FAILURE (#1815 AC 4)"
    else
      bad "FS.GG.Rendering#1088 (exit 4) grades shape=$(shape_conclusion "$rc") mechanical=$(mech_conclusion "$rc") — want success/failure. Reddening it on kit-bump-shape punishes the merge that was RIGHT; greening it on kit-bump-mechanical automerges the class #1587 reserves for a person"
    fi
  else
    bad "FS.GG.Rendering#1088 graded exit $rc, not 4 — leg 22 should have caught this first"
  fi

  # 50 — MUTATION, so the row above is not a coincidence of a mapping that reds everything. The SAME
  # real diff WITHOUT the receiver-authored repair is exit 0, and then it IS automergeable. This is
  # what proves `kit-bump-mechanical` discriminates the two classes rather than merely refusing.
  d=$(fresh_1088 r1088-contexts-nofix); apply_1088_mechanical "$d"
  GIT "$d" add -A; GIT "$d" commit -q -m "chore(deps): update dependency fs.gg.kit to 0.15.0"
  rc=0
  python3 "$GATE" --repo "$d" --base base --head HEAD --kit-dir "$WORK/pkg-1088" \
    --properties "$WORK/props-1088.json" >/dev/null 2>&1 || rc=$?
  if [ "$rc" = 0 ] && [ "$(mech_conclusion "$rc")" = success ]; then
    ok "  MUTATION: the SAME 35 paths without the repair are exit 0 and kit-bump-mechanical is GREEN"
  else
    bad "  MUTATION: dropping the repair gave exit $rc / $(mech_conclusion "$rc") — kit-bump-mechanical does not discriminate the two classes, it just reds"
  fi

  # 51 — and the second context must not be spelled as a filter on the first. If `bump-mechanical`
  # ever grows its own `paths:`-shaped gate — an `if:` conditioned on the event, the branch, or the
  # verdict being present — a pull request it excluded would create NO check run, and a receiver that
  # required it would hold every PR at "Expected — waiting for status to be reported" (#1508). The
  # ONLY `if:` permitted here is the always-run one leg 40 demands.
  mech_ifs="$(printf '%s\n' "$MECH_RAW" | grep -cE '^    if:')" || mech_ifs=0
  if [ "${mech_ifs:-0}" = 1 ]; then
    ok "  and the job carries exactly ONE 'if:' — the always-run one, not a filter that would suppress the check run (#1508)"
  else
    bad "  the bump-mechanical job carries $mech_ifs 'if:' conditions — a job an 'if:' excludes creates NO check run, and a repo requiring the context would wait forever (#1508)"
  fi
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

# =================================================================================================
# .github#1845 — WHICH JOBS RUN, ON WHICH EVENTS. THE ON-DEMAND MATERIALIZE, AND THE SKIP THAT
# WOULD HAVE MADE ITS BUTTON A LIE.
#
# THE DEFECT THESE LEGS EXIST FOR. Measured under #1834 (`4fadc88`): across 842 runs in all seven
# receivers, ZERO materialize ran on `main`. The callers trigger on `pull_request` alone and the
# `materialize` job was gated to `renovate/*` same-repo heads — so `kit / coordination-kit`'s DRIFT
# summary told readers to "re-run the materialize" and there was no button anywhere that could.
#
# ADDING `workflow_dispatch` TO A CALLER IS NOT ENOUGH, AND THE WAY IT FAILS IS THE POINT (#1815).
# On a dispatched run there is no `github.event.pull_request`, so the OLD expression evaluated FALSE
# and the job was SKIPPED — and GitHub reports a skipped job as `conclusion: skipped`, which branch
# protection counts as satisfied and a human reads as a tick. An operator would have pressed the
# button, watched a green run, and believed the repair had happened. That is strictly worse than no
# button, it is invisible to every happy-path assertion, and it is what leg M1 below reproduces.
#
# THESE LEGS EVALUATE THE REAL EXPRESSIONS, EXTRACTED FROM THE REAL WORKFLOW (ADR-0058). A copy of
# the `if:` restated here would pass forever while the workflow said something else — the #1059 class,
# in the fixture written to catch it.
#
# #1808's FOUR POINTS, EACH NAMED WHERE IT IS DISCHARGED:
#   1. an UNMUTATED CONTROL that must pass  -> the `pr-renovate-same-repo` row, which is the behaviour
#      that existed before #1845 and must be untouched by it;
#   2. NOT MEASURED distinct from FAIL and PASS -> the evaluator REFUSES any token it does not
#      understand and every leg reports `NOT MEASURED` rather than a verdict (#266);
#   3. ANCHORS INDEPENDENT OF THE GUARD UNDER TEST -> the verdicts are computed by an evaluator that
#      knows nothing about kit-materialize.yml, from event contexts written here, and are checked
#      against a table written here — never against the workflow's own comments or logs;
#   4. A LEG THAT MUTATES THE FIXTURE SO THE HELPER IS PROVEN TO FIRE -> M1-M4 below, each of which
#      applies a specific realistic regression and asserts the corresponding leg goes FAIL.
# =================================================================================================
echo
echo "== .github#1845: which jobs run, on which events =="

# A GitHub-expression evaluator, in the stdlib, for the sub-grammar these three `if:`s use. It is
# DELIBERATELY TOTAL AND DELIBERATELY NARROW: `||` `&&` `!` `==` `!=` parentheses, string literals,
# `true`/`false`, dotted context paths, and the calls `startsWith`/`endsWith`/`contains`/`cancelled`/
# `always`/`success`/`failure`. Anything else — a new operator, an unknown function, a stray token —
# EXITS 3 and prints NOT-MEASURED. It never guesses and never defaults to true or false, because a
# permissive evaluator would silently green every leg below the day someone edited an expression
# into a shape it could not read (#266).
EVAL="$WORK/ghexpr.py"
cat > "$EVAL" <<'PY'
import json, re, sys

TOKEN = re.compile(r"""\s*(?:
    (?P<str>'(?:[^']|'')*')
  | (?P<op>\|\||&&|==|!=|<=|>=|!|\(|\)|,|<|>)
  | (?P<word>[A-Za-z_][A-Za-z0-9_.\-]*)
)""", re.X)

def lex(src):
    out, i = [], 0
    while i < len(src):
        if src[i].isspace():
            i += 1; continue
        m = TOKEN.match(src, i)
        if not m or m.end() == i:
            raise ValueError("cannot tokenise at %r" % src[i:i + 24])
        i = m.end()
        if m.group("str") is not None:
            out.append(("str", m.group("str")[1:-1].replace("''", "'")))
        elif m.group("op") is not None:
            out.append(("op", m.group("op")))
        else:
            out.append(("word", m.group("word")))
    return out

class P:
    def __init__(self, toks, ctx):
        self.t, self.i, self.ctx = toks, 0, ctx
    def peek(self):
        return self.t[self.i] if self.i < len(self.t) else (None, None)
    def eat(self, val):
        k, v = self.peek()
        if k == "op" and v == val:
            self.i += 1; return True
        return False
    def expect(self, val):
        if not self.eat(val):
            raise ValueError("expected %r at token %d" % (val, self.i))
    # or := and ('||' and)*
    #
    # THE RIGHT-HAND SIDE IS PARSED UNCONDITIONALLY, and that is not a style choice. Written as
    # `v = truthy(v) or truthy(self.p_and())`, Python's own short-circuit skips the CALL — so a
    # false left operand leaves the right operand's tokens unconsumed, the parser stops mid-stream,
    # and the totality check at the bottom reports NOT-MEASURED for an expression that is perfectly
    # well formed. Every leg fed a false-on-the-left expression would have gone NOT MEASURED, which
    # is at least loud; a laxer totality check would have made them silently pass. GitHub expressions
    # have no side effects, so evaluating both sides is free and correct.
    def p_or(self):
        v = self.p_and()
        while self.eat("||"):
            rhs = self.p_and()
            v = truthy(v) or truthy(rhs)
        return v
    def p_and(self):
        v = self.p_not()
        while self.eat("&&"):
            rhs = self.p_not()
            v = truthy(v) and truthy(rhs)
        return v
    def p_not(self):
        if self.eat("!"):
            return not truthy(self.p_not())
        return self.p_cmp()
    def p_cmp(self):
        v = self.p_atom()
        while True:
            k, o = self.peek()
            if k == "op" and o in ("==", "!="):
                self.i += 1
                r = self.p_atom()
                # GitHub coerces null to '' when compared with a string.
                a = "" if v is None else v
                b = "" if r is None else r
                v = (a == b) if o == "==" else (a != b)
            else:
                return v
    def p_atom(self):
        k, v = self.peek()
        if k == "op" and v == "(":
            self.i += 1
            inner = self.p_or()
            self.expect(")")
            return inner
        if k == "str":
            self.i += 1; return v
        if k == "word":
            self.i += 1
            if self.eat("("):
                args = []
                if not self.eat(")"):
                    args.append(self.p_or())
                    while self.eat(","):
                        args.append(self.p_or())
                    self.expect(")")
                return self.call(v, args)
            if v == "true":  return True
            if v == "false": return False
            if v == "null":  return None
            return self.lookup(v)
        raise ValueError("unexpected token %r" % (v,))
    def call(self, name, args):
        s = lambda x: "" if x is None else (x if isinstance(x, str) else str(x))
        if name == "startsWith": return s(args[0]).startswith(s(args[1]))
        if name == "endsWith":   return s(args[0]).endswith(s(args[1]))
        if name == "contains":   return s(args[1]) in s(args[0])
        # Status functions come from the drive context, never from a default: a leg that forgot to
        # say whether the run was cancelled must not silently get "no".
        if name in ("cancelled", "always", "success", "failure"):
            if name not in self.ctx:
                raise ValueError("the drive context does not say what %s() is" % name)
            return bool(self.ctx[name])
        raise ValueError("unknown function %s()" % name)
    def lookup(self, path):
        cur = self.ctx
        for part in path.split("."):
            if not isinstance(cur, dict) or part not in cur:
                return None          # an absent context value is null, exactly as GitHub has it
            cur = cur[part]
        return cur

def truthy(v):
    if v is None: return False
    if isinstance(v, bool): return v
    if isinstance(v, str):  return v != ""
    return bool(v)

try:
    expr = sys.argv[1]
    ctx = json.loads(sys.argv[2])
    expr = expr.strip()
    if expr.startswith("${{") and expr.endswith("}}"):
        expr = expr[3:-2]
    toks = lex(expr)
    p = P(toks, ctx)
    val = p.p_or()
    if p.i != len(toks):
        raise ValueError("trailing tokens from %d" % p.i)
    print("RUN" if truthy(val) else "SKIP")
except Exception as e:                                    # noqa: BLE001 — every failure is one thing
    print("NOT-MEASURED")
    sys.stderr.write("ghexpr: %s\n" % e)
    sys.exit(3)
PY

# THE EVALUATOR IS ITSELF CHECKED BEFORE ANYTHING IS BELIEVED FROM IT (#1808 point 2/3). If it could
# only ever answer one way, every table row below would be a tautology; if it silently accepted
# nonsense, a mangled `if:` would read as a verdict.
ev() { python3 "$EVAL" "$1" "$2" 2>/dev/null; }
selfcheck_ok=1
[ "$(ev 'true'  '{}')" = "RUN"  ] || selfcheck_ok=0
[ "$(ev 'false' '{}')" = "SKIP" ] || selfcheck_ok=0
[ "$(ev "startsWith(a.b, 'renovate/')" '{"a":{"b":"renovate/x"}}')" = "RUN"  ] || selfcheck_ok=0
[ "$(ev "startsWith(a.b, 'renovate/')" '{"a":{"b":"feature/x"}}')" = "SKIP" ] || selfcheck_ok=0
[ "$(ev "a.missing == 'x'"             '{}')"                      = "SKIP" ] || selfcheck_ok=0
[ "$(ev "frobnicate(1)"                '{}')"                      = "NOT-MEASURED" ] || selfcheck_ok=0
[ "$(ev "a %% b"                       '{}')"                      = "NOT-MEASURED" ] || selfcheck_ok=0
if [ "$selfcheck_ok" = 1 ]; then
  ok "#1845: the expression evaluator answers RUN, SKIP and NOT-MEASURED, and refuses a token it cannot read"
else
  bad "#1845: the expression evaluator is broken — the legs below did NOT measure anything, and that is not a verdict about any job (#266)"
fi

# THE THREE `if:`s, EXTRACTED FROM THE REAL WORKFLOW. `WF` is resolved above. Extraction failure is a
# FINDING, never a skipped leg — that is exactly how the #1797 guard shipped unable to pass.
IFREAD='
import sys
job = sys.argv[2]
lines = open(sys.argv[1], encoding="utf-8").read().split("\n")
# The same job-block walk `JOBREAD` uses above: a two-space key that ends in `:` opens a job.
block, inside = [], False
for line in lines:
    if line.startswith("  ") and not line.startswith("   ") and line.rstrip().endswith(":"):
        inside = line.strip() == job + ":"
        continue
    if inside:
        block.append(line)
if not block:
    sys.stderr.write("no job %s\n" % job); sys.exit(1)
idx = [i for i, l in enumerate(block) if l.startswith("    if:")]
if not idx:
    sys.stderr.write("job %s declares no if:\n" % job); sys.exit(2)
i = idx[0]
first = block[i].split(":", 1)[1].strip()
if first in (">-", ">", "|-", "|"):
    # A folded scalar: every following line indented deeper than the key belongs to it.
    out = []
    for l in block[i + 1:]:
        if l.strip() == "":
            continue
        if not l.startswith("      "):
            break
        out.append(l.strip())
    print(" ".join(out))
else:
    print(first)
'
IF_MAT="$(python3 -c "$IFREAD" "$WF" materialize 2>/dev/null)"
IF_SHAPE="$(python3 -c "$IFREAD" "$WF" bump-shape 2>/dev/null)"
IF_MECH="$(python3 -c "$IFREAD" "$WF" bump-mechanical 2>/dev/null)"

# THE EVENT CONTEXTS. Written here, from what GitHub actually delivers, and knowing nothing about the
# expressions they are fed to (#1808 point 3).
C_PR_RENOVATE='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"renovate/fs.gg.kit-0.x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_PLAIN='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"feature/some-work","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_FORK='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"renovate/fs.gg.kit-0.x","repo":{"full_name":"attacker/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_DOCS='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"docs/readme","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_DISPATCH_MAIN='{"github":{"event_name":"workflow_dispatch","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"}}},"cancelled":false}'
C_DISPATCH_BRANCH='{"github":{"event_name":"workflow_dispatch","repository":"FS-GG/FS.GG.SDD","ref_name":"fix/kit-drift","event":{"repository":{"default_branch":"main"}}},"cancelled":false}'
C_PRTARGET='{"github":{"event_name":"pull_request_target","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"renovate/fs.gg.kit-0.x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":false}'
C_PR_CANCELLED='{"github":{"event_name":"pull_request","repository":"FS-GG/FS.GG.SDD","ref_name":"main","event":{"repository":{"default_branch":"main"},"pull_request":{"head":{"ref":"renovate/fs.gg.kit-0.x","repo":{"full_name":"FS-GG/FS.GG.SDD"}}}}},"cancelled":true}'

# THE TABLE. `assert_run <if-expr> <ctx> <want RUN|SKIP> <what>` — and it reports NOT MEASURED rather
# than a verdict when the expression could not be extracted or the evaluator refused it.
assert_run() { # $1 = expr  $2 = ctx json  $3 = RUN|SKIP  $4 = description
  local got
  if [ -z "$1" ]; then
    bad "#1845: NOT MEASURED — the if: for this leg could not be extracted from kit-materialize.yml ($4)"
    return
  fi
  got="$(ev "$1" "$2")"
  case "$got" in
    RUN|SKIP)
      if [ "$got" = "$3" ]; then ok "#1845: $4 — $3"; else bad "#1845: $4 — got $got, want $3" "$1"; fi ;;
    *) bad "#1845: NOT MEASURED — the evaluator refused this expression, so this is not a verdict about the job ($4)" "$1" ;;
  esac
}

# --- `materialize` -------------------------------------------------------------------------------
# 1. THE UNMUTATED CONTROL (#1808 point 1). The pre-#1845 behaviour, which this change must not move.
assert_run "$IF_MAT" "$C_PR_RENOVATE"     RUN  "materialize runs on a same-repo renovate/* bump PR (the CONTROL — unchanged behaviour)"
assert_run "$IF_MAT" "$C_PR_PLAIN"        SKIP "materialize does NOT run on a human same-repo PR"
assert_run "$IF_MAT" "$C_PR_DOCS"         SKIP "materialize does NOT run on a docs PR"
# 2. THE TRUST BOUNDARY, which #1845 must not have widened: the App token never pushes to a fork head.
assert_run "$IF_MAT" "$C_PR_FORK"         SKIP "materialize does NOT run on a renovate/* head in a FORK — the App token never touches an untrusted head"
assert_run "$IF_MAT" "$C_PRTARGET"        SKIP "materialize does NOT run from pull_request_target, which also populates github.event.pull_request"
# 3. THE HEADLINE. This is the leg the whole item is about, and M1 below proves it can fail.
assert_run "$IF_MAT" "$C_DISPATCH_MAIN"   RUN  "materialize RUNS on a workflow_dispatch against the default branch — the button is not a skip"
assert_run "$IF_MAT" "$C_DISPATCH_BRANCH" RUN  "materialize RUNS on a workflow_dispatch against any other branch"

# --- `bump-shape` / `bump-mechanical`: #1508, asserted SEMANTICALLY --------------------------------
# The old leg was structural ("the job carries no `if:`"). #1845 gives it one, so the property it
# stood for is asserted directly instead: BOTH reporter contexts must report on EVERY pull request,
# whatever its head ref, base, author or subject — because a required context that fails to create a
# check run holds the pull request at "Expected — waiting for status to be reported" forever. This is
# a strict superset of the old leg: an `if:` that narrows by ANY pull-request property fails here,
# and so does one that is absent-but-wrong.
while IFS='|' read -r shape ctxval; do
  [ -n "$shape" ] || continue
  assert_run "$IF_SHAPE" "$ctxval" RUN "#1508: kit-bump-shape reports on a $shape pull request — a required context must create a check run on EVERY pull request"
  assert_run "$IF_MECH"  "$ctxval" RUN "#1508: kit-bump-mechanical reports on a $shape pull request"
done <<EOF
renovate|$C_PR_RENOVATE
human-branch|$C_PR_PLAIN
fork|$C_PR_FORK
docs|$C_PR_DOCS
EOF
# ...and they are quiet on a dispatched repair run, where there is no pull request to report about
# and no branch protection is waiting on anything.
assert_run "$IF_SHAPE" "$C_DISPATCH_MAIN"   SKIP "kit-bump-shape does not report on a workflow_dispatch — there is no pull request to grade"
assert_run "$IF_MECH"  "$C_DISPATCH_MAIN"   SKIP "kit-bump-mechanical does not report on a workflow_dispatch either"
# `!cancelled()` is still load-bearing and is driven separately from the event term.
assert_run "$IF_MECH"  "$C_PR_CANCELLED"    SKIP "kit-bump-mechanical still stands down on a CANCELLED run (!cancelled() survives the #1845 edit)"

# --- M1-M4: THE MUTATIONS (#1808 point 4) ---------------------------------------------------------
# Each applies a specific, realistic regression to the EXTRACTED expression and asserts the leg above
# flips. A leg that cannot fail is decoration, and four harnesses mis-reported their own results in
# the two days before this was written.
mutate_expect() { # $1 = mutated expr  $2 = ctx  $3 = expected (the WRONG answer)  $4 = what regressed
  local got; got="$(ev "$1" "$2")"
  if [ "$got" = "$3" ]; then
    ok "#1845 mutation: $4 — the leg above FIRES (mutant answers $got)"
  else
    bad "#1845 mutation: $4 — the mutant answers $got, so the leg above is NOT proven to fire and its green means nothing" "$1"
  fi
}
# M1 — THE DEFECT ITSELF: the pre-#1845 expression, verbatim. It is what every receiver's caller
#      would have hit the moment someone added `workflow_dispatch` to it, and the answer it gives is
#      SKIP, which GitHub reports as `conclusion: skipped` and a human reads as a tick.
M1="startsWith(github.event.pull_request.head.ref, 'renovate/') && github.event.pull_request.head.repo.full_name == github.repository"
mutate_expect "$M1" "$C_DISPATCH_MAIN"   SKIP "reverting materialize's if: to the pre-#1845 renovate/*-only form silently skips the dispatched repair"
mutate_expect "$M1" "$C_PR_RENOVATE"     RUN  "...and the same mutant still RUNS the control, so the control leg is not what caught it"
# M2 — the tempting narrowing the workflow's own comment warns against: `&& <anything>` on the
#      dispatch term. Realistic, well-intentioned, and it restores the fail-open.
M2="( github.event_name == 'workflow_dispatch' && startsWith(github.ref_name, 'renovate/') ) || ( github.event_name == 'pull_request' && startsWith(github.event.pull_request.head.ref, 'renovate/') && github.event.pull_request.head.repo.full_name == github.repository )"
mutate_expect "$M2" "$C_DISPATCH_MAIN"   SKIP "conditioning the dispatch arm on the ref makes the button a skip again"
# M3 — the #1508 regression, in the shape someone would actually write it: narrow the reporter to
#      the pull requests it "has something to say about". That is the deadlock, exactly.
M3="github.event_name == 'pull_request' && startsWith(github.event.pull_request.head.ref, 'renovate/')"
mutate_expect "$M3" "$C_PR_PLAIN"        SKIP "narrowing kit-bump-shape's if: by head ref stops it reporting on a human PR — #1508's permanent 'Expected — waiting for status' deadlock"
mutate_expect "$M3" "$C_PR_RENOVATE"     RUN  "...and that mutant is still green on a bump PR, which is why only the non-bump rows catch it"
# M4 — dropping `!cancelled()` from bump-mechanical, which is the #1815 inversion the job's own
#      header calls out. Driven on the term rather than argued about.
M4="github.event_name == 'pull_request'"
mutate_expect "$M4" "$C_PR_CANCELLED"    RUN  "dropping !cancelled() from kit-bump-mechanical makes it report on a cancelled run"

# =================================================================================================
# .github#1845 — AND WHAT THE ON-DEMAND ARM IS ALLOWED TO WRITE.
#
# The `if:` above decides only that the job RUNS. What it may WRITE is decided by the `plan` step,
# and the whole `main` answer is four lines of it: a materialize WRITES, so it is a repair and not a
# check, and `kit / coordination-kit` is a required context precisely so the kit's bytes are graded
# BEFORE they reach the default branch. A repair pushed straight to `main` would bypass the gate
# whose red caused it. So the repair goes to a branch, and the default branch is never written.
#
# THE GUARD IS EXTRACTED AND DRIVEN, NEVER RESTATED (ADR-0058, and the #1797 legs' own lesson).
# =================================================================================================
echo
plan="$(python3 - "$WF" <<'PY'
import re, sys
src = open(sys.argv[1], encoding="utf-8").read()
m = re.search(r'^([ \t]*)case "\$EVENT" in\n.*?^\1esac$', src, re.S | re.M)
if not m:
    sys.stderr.write("could not locate the plan step's EVENT case\n"); sys.exit(1)
ind = m.group(1)
print("\n".join(l[len(ind):] if l.startswith(ind) else l for l in m.group(0).splitlines()))
PY
)" || plan=""

if [ -z "$plan" ]; then
  bad "#1845: NOT MEASURED — could not extract the plan step's EVENT case from kit-materialize.yml, so nothing below ran and that is not a verdict about what the on-demand arm writes"
else
  ok "#1845: the plan step's write-decision was extracted from the workflow and is what the legs below run"

  # drive <event> <pr-head> <dispatch-ref> <default-branch> [plan-source]
  #   -> "<checkout-ref>|<push-target>|<in-place>" or "REFUSED"
  # `refuse` is stubbed so the driver sees the refusal rather than a summary file; everything else is
  # the workflow's own text, run by the same bash Actions runs a shell-less `run:` block as.
  drive_plan() {
    local src="${5:-$plan}" out rc=0
    out="$(GITHUB_OUTPUT="$WORK/plan.out" \
      EVENT="$1" PR_HEAD="$2" DISPATCH_REF="$3" DEFAULT_BRANCH="$4" RUN_ID="4242" \
      bash -c '
        set -uo pipefail
        : > "$GITHUB_OUTPUT"
        refuse() { echo "REFUSED"; exit 9; }
        '"$src"'
      ' 2>/dev/null)" || rc=$?
    if [ "$rc" = 9 ] || [ "$out" = "REFUSED" ]; then echo "REFUSED"; return; fi
    [ "$rc" = 0 ] || { echo "BROKEN($rc)"; return; }
    local co pt ip
    co="$(sed -n 's/^checkout-ref=//p' "$WORK/plan.out")"
    pt="$(sed -n 's/^push-target=//p'  "$WORK/plan.out")"
    ip="$(sed -n 's/^in-place=//p'     "$WORK/plan.out")"
    echo "$co|$pt|$ip"
  }

  # 1. THE CONTROL AGAIN, on the write side: a bump PR still materializes onto its own head in place.
  got="$(drive_plan pull_request 'renovate/fs.gg.kit-0.x' '' main)"
  if [ "$got" = "renovate/fs.gg.kit-0.x|renovate/fs.gg.kit-0.x|true" ]; then
    ok "#1845: a bump PR still pushes to its own head in place (the CONTROL — unchanged by #1845)"
  else
    bad "#1845: the pull_request arm's write plan changed — got '$got', want 'renovate/fs.gg.kit-0.x|renovate/fs.gg.kit-0.x|true'"
  fi

  # 2. THE HEADLINE ON THE WRITE SIDE. Dispatched on the DEFAULT branch: the repair goes elsewhere.
  got="$(drive_plan workflow_dispatch '' main main)"
  case "$got" in
    "main|kit-materialize/repair-4242|false")
      ok "#1845: dispatched on the DEFAULT branch, the push target is a NEW repair branch and the default branch is not written" ;;
    "main|main|"*)
      bad "#1845: dispatched on the default branch, the plan targets \`main\` ITSELF — a materialize WRITES, so this would push an ungraded repair past the very gate that detected the drift" ;;
    *)
      bad "#1845: dispatched on the default branch, the write plan is '$got', want 'main|kit-materialize/repair-4242|false'" ;;
  esac

  # 3. Any OTHER branch is repaired in place — that is the useful case and it must not be routed
  #    through a repair branch nobody wants to merge into their own feature branch.
  got="$(drive_plan workflow_dispatch '' fix/kit-drift main)"
  if [ "$got" = "fix/kit-drift|fix/kit-drift|true" ]; then
    ok "#1845: dispatched on a non-default branch, the repair is pushed in place"
  else
    bad "#1845: dispatched on a non-default branch, the write plan is '$got', want 'fix/kit-drift|fix/kit-drift|true'"
  fi

  # 4. THE DEFAULT BRANCH IS READ, NEVER ASSUMED. `main` is hardcoded nowhere: a receiver whose
  #    default branch is `trunk` must get the repair-branch arm on `trunk`, and the plain string
  #    `main` must get the in-place arm there.
  got="$(drive_plan workflow_dispatch '' trunk trunk)"
  case "$got" in
    "trunk|kit-materialize/repair-4242|false") ok "#1845: the default branch is read from the event payload — a receiver defaulting to \`trunk\` gets the repair-branch arm on \`trunk\`" ;;
    *) bad "#1845: a receiver whose default branch is \`trunk\` got '$got' — \`main\` is being assumed somewhere" ;;
  esac
  got="$(drive_plan workflow_dispatch '' main trunk)"
  case "$got" in
    "main|main|true") ok "#1845: ...and \`main\` in such a repo is an ordinary branch, repaired in place" ;;
    *) bad "#1845: \`main\` in a trunk-default repo got '$got', want in-place — the branch NAME is not what decides this" ;;
  esac

  # 5. AN UNREADABLE DEFAULT BRANCH IS A REFUSAL, NOT A GUESS. This is the #266 arm and it is the one
  #    a "simplification" removes first: with no `default_branch` in the payload, `main != ''` is
  #    true, the else-arm fires, and the job pushes the repair straight to the branch it must never
  #    write — while its log says it checked. M5 below proves this leg fires.
  got="$(drive_plan workflow_dispatch '' main '')"
  if [ "$got" = "REFUSED" ]; then
    ok "#1845: an empty repository.default_branch is REFUSED — the job never assumes the dispatched ref is not the default branch"
  else
    bad "#1845: with no default_branch in the payload the plan is '$got' — it guessed, and the guess writes the one branch this job must not (#266)"
  fi

  # 6. Any other event is refused, so loosening the job's `if:` cannot silently reach an App-token
  #    push from an event nobody reviewed.
  for ev_name in push schedule pull_request_target repository_dispatch; do
    got="$(drive_plan "$ev_name" 'renovate/x' main main)"
    if [ "$got" = "REFUSED" ]; then
      ok "#1845: a \`$ev_name\` event is REFUSED by the plan step, whatever the job's if: says"
    else
      bad "#1845: a \`$ev_name\` event produced the write plan '$got' — an App-token push is reachable from an unreviewed event"
    fi
  done

  # M5 — THE MUTATION FOR LEG 5, applied to the extracted source. Delete the default-branch guard —
  #      the exact "this line looks redundant" edit — and the empty-payload drive must stop refusing.
  plan_m5="$(printf '%s\n' "$plan" | grep -v 'DEFAULT_BRANCH" \] || refuse')"
  if [ "$plan_m5" = "$plan" ]; then
    bad "#1845 mutation M5: NOT MEASURED — the default-branch guard line was not found to remove, so leg 5 is not proven to fire"
  else
    got="$(drive_plan workflow_dispatch '' main '' "$plan_m5")"
    if [ "$got" = "REFUSED" ]; then
      bad "#1845 mutation M5: removing the default-branch guard changed nothing — leg 5 is NOT proven to fire and its green means nothing"
    else
      ok "#1845 mutation M5: removing the default-branch guard makes the empty-payload drive plan '$got' instead of REFUSED — leg 5 FIRES"
    fi
  fi

  # M6 — and the mutation for leg 2, which is the item's whole subject: make the default-branch arm
  #      push in place. The repair would land on `main` unreviewed and ungraded.
  plan_m6="$(printf '%s\n' "$plan" | sed "s#printf 'push-target=kit-materialize/repair-%s\\\\n' \"\$RUN_ID\"#printf 'push-target=%s\\\\n' \"\$DISPATCH_REF\"#")"
  if [ "$plan_m6" = "$plan" ]; then
    bad "#1845 mutation M6: NOT MEASURED — the repair-branch line was not found to mutate, so leg 2 is not proven to fire"
  else
    got="$(drive_plan workflow_dispatch '' main main "$plan_m6")"
    case "$got" in
      "main|main|"*) ok "#1845 mutation M6: pointing the default-branch arm at the dispatched ref makes the plan '$got' — leg 2 FIRES" ;;
      *)             bad "#1845 mutation M6: the mutant plans '$got', so leg 2 is NOT proven to fire" ;;
    esac
  fi
fi

echo
echo "kit-bump-shape: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1

#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Kit gate (ADR-0062). Run by .github/workflows/kit-package.yml, and
# locally. Nothing executes a workflow, so the verification lives in a script the workflow CALLS — the
# same reason the landable/coherence gates moved out of hand-copied YAML (#724).
#
# It proves the things the package must get right:
#   1. DERIVED, NOT RESTATED — coordination-kit digests match registry/repos.lock exactly (ADR-0058),
#      and the build-config member count matches sync-build-config.sh's FILES (its derive source).
#   2. PACKS — `dotnet pack` produces a nupkg carrying every manifest member + the build logic.
#   3. MATERIALIZES — a receiver gets the skills on disk in every root, both clients executable, and
#      the two libraries `skill-view` sources (ADR-0062's load-bearing half). It does NOT get the
#      engine manifest: that left kit ownership in .github#1615 (ADR-0068), and the leg asserting it
#      was delivered is INVERTED rather than deleted, so re-adding the row reds beside its reason.
#      build-config is withheld unless the consumer opts in, and (3b) lands at the receiver root when
#      it does — global.json never carried.
#      (3e) ADR-0067 §9's replacement is not merely PRESENT but RUNS from the receiver's own tree, and
#      (3f/3g/3h) a VIEW root is swept, never copied into, certified or LOUDLY refused, refused outright
#      when a root is named in two dispositions, and wholly inert when none is declared (.github#1696).
#   4. FAILS LOUD — a tampered kit file is a build ERROR, never a silently missing/stale file (ADR-0014).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "$HERE/../.." && pwd)"
LOCK="$SRC_ROOT/registry/repos.lock"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
fail() { echo "verify-package: FAIL — $*" >&2; exit 1; }

echo "== 1. stage + derive parity (coordination kit vs repos.lock; build-config vs sync-build-config) =="
bash "$HERE/stage-kit.sh" "$WORK/stage" >/dev/null
n_ck=0; n_bc=0; n_skill_files=0
while IFS=$'\t' read -r kind pkgrel dest sha _executable; do
  if [ "$kind" = "build-config" ]; then
    # build-config has no repos.lock row (ADR-0036 pin model): its sha256 is a self-consistent
    # integrity record, checked at materialize, not a cross-check against repos.lock.
    n_bc=$((n_bc + 1))
  elif [ "$kind" != "skill" ] || [[ "$dest" == */SKILL.md ]]; then
    # repos.lock remains the legacy SKILL.md/file pin during publish-before-flip. Additional files
    # are content-addressed by their v2 manifest row and the directory tree digest.
    grep -qi "^$sha  " "$LOCK" || fail "staged digest $sha ($pkgrel) is not in registry/repos.lock (derive-don't-restate broken)"
    n_ck=$((n_ck + 1))
  fi
  [ "$kind" != "skill" ] || n_skill_files=$((n_skill_files + 1))
done < "$WORK/stage/kit-manifest.tsv"
# COUNT parity, not just subset: the manifest must carry EVERY row of each capability, derived from the
# same source, so a member added/removed never needs an edit here (ADR-0058) — a truncated manifest (a
# silently smaller kit) fails instead of passing the subset check above.
n_rows=0
for kind in skill client config; do
  c="$(bash "$SRC_ROOT/scripts/repos.sh" kit --field source --kind "$kind" --registry "$SRC_ROOT/registry/repos.yml" | grep -c .)" || true
  n_rows=$((n_rows + c))
done
[ "$n_ck" -eq "$n_rows" ] || fail "staged $n_ck coordination-kit file(s) but registry names $n_rows row(s) — the derived set is incomplete"
n_files="$(sed -n '/^FILES=(/,/^)/{/^FILES=(/d;/^)/d;s/#.*//;s/[[:space:]"]//g;/^$/d;p}' "$SRC_ROOT/scripts/sync-build-config.sh" | grep -c .)"
[ "$n_bc" -eq "$n_files" ] || fail "staged $n_bc build-config file(s) but sync-build-config names $n_files FILES member(s)"
echo "   $n_ck coordination-kit file(s) = $n_rows registry rows (digests in repos.lock); $n_bc build-config file(s) = $n_files sync-build-config FILES"

echo "== 2. pack + content assert =="
dotnet pack "$HERE/FS.GG.Kit.csproj" -c Release -o "$WORK/out" >/dev/null
nupkg="$(echo "$WORK"/out/FS.GG.Kit.*.nupkg)"
[ -f "$nupkg" ] || fail "no nupkg produced"
entries="$(unzip -Z1 "$nupkg")"
# Fixed build logic the package always ships...
for want in "build/FS.GG.Kit.props" "build/FS.GG.Kit.targets" "README.md" "kit/kit-manifest.tsv"; do
  grep -qx "$want" <<<"$entries" || fail "nupkg is missing $want"
done
# ...and EVERY member the manifest names, derived — not a restated list of skill names (ADR-0058).
while IFS=$'\t' read -r _kind pkgrel _dest _sha _executable; do
  grep -qx "kit/$pkgrel" <<<"$entries" || fail "nupkg is missing kit/$pkgrel (named by the manifest)"
done < "$WORK/stage/kit-manifest.tsv"
echo "   nupkg carries every kit member + manifest + build logic"

echo "== 3. materialize into a fresh receiver root =="
unzip -q "$nupkg" "kit/*" "build/*" -d "$WORK/unpacked"
# Reproduce the global-packages-cache metadata seen on Linux: NuGet can extract ordinary nupkg
# entries as executable even though their central-directory attributes are 0644. The materializer,
# not the package cache, owns the modes receiver repos commit.
find "$WORK/unpacked/kit" -type f -exec chmod a+x {} +
recv="$WORK/recv"; mkdir -p "$recv"
# A receiver as it stands TODAY: carrying the `.codex/skills` copies an older kit wrote, plus one skill
# of its own. Seeded BEFORE the first materialize so the retired-root sweep is measured on the real
# migration shape rather than on an empty tree (ADR-0067 §5, .github#1636).
mkdir -p "$recv/.codex/skills/check-board" "$recv/.codex/skills/receiver-own-skill"
printf 'stale kit copy\n' > "$recv/.codex/skills/check-board/SKILL.md"
printf 'the receiver own skill\n' > "$recv/.codex/skills/receiver-own-skill/SKILL.md"
# ...and one retired-root entry that is a generated VIEW rather than a copy (the phase-2 shape,
# .github#1635). The sweep must UNLINK it, never recurse through it into the canonical tree.
mkdir -p "$WORK/canonical-victim"
printf 'canonical, must survive\n' > "$WORK/canonical-victim/SKILL.md"
ln -s "$WORK/canonical-victim" "$recv/.codex/skills/pnext-item"
cat > "$WORK/materialize.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$recv</FsggKitReceiverRoot>
  </PropertyGroup>
</Project>
EOF
dotnet msbuild "$WORK/materialize.proj" -t:FsggKitMaterialize -nologo >/dev/null
for root in .claude/skills .agents/skills; do
  for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
    [ -f "$recv/$root/$s/SKILL.md" ] || fail "skill not materialized: $root/$s/SKILL.md"
  done
done
[ -x "$recv/scripts/fsgg-coord" ]        || fail "client not materialized executable at scripts/fsgg-coord"
[ -x "$recv/scripts/skill-view" ]        || fail "client not materialized executable at scripts/skill-view"
[ -f "$recv/scripts/lib/args.sh" ]       || fail "skill-view library not materialized at scripts/lib/args.sh (#1696)"
[ -f "$recv/scripts/lib/roots.sh" ]      || fail "skill-view library not materialized at scripts/lib/roots.sh (#1696)"
# THE ENGINE MANIFEST IS NO LONGER MATERIALIZED, AND THAT IS ASSERTED RATHER THAN MERELY UNCHECKED
# (#1615, ADR-0068). This line used to read:
#     [ -f "$recv/.config/dotnet-tools.json" ] || fail "engine manifest not materialized at .config/…"
# Dropping it to nothing would leave the strongest statement about the kit's delivered set silent on
# a member that was deliberately removed — so the assertion is INVERTED instead. A kit that starts
# shipping `.config/dotnet-tools.json` again reds HERE, next to the reason, rather than being noticed
# when a receiver's manifest is silently overwritten by a fabric that no longer owns it.
[ ! -e "$recv/.config/dotnet-tools.json" ] \
  || fail "the kit materialized .config/dotnet-tools.json — the engine manifest left kit ownership in #1615 (ADR-0068) and each receiver now owns its own. Overwriting it would undo the per-repo Renovate bump this decision relies on. If the row is genuinely being restored, read ADR-0068 first and update this leg with it."
# The retired root is swept BY THE MATERIALIZER (ADR-0067 §5). Both halves are load-bearing: the kit's
# own copy goes, and the receiver's own skill under that root SURVIVES — a sweep that took the whole
# directory would be the hand-deletion ADR-0065's transport contract forbids, wearing a build task.
[ ! -e "$recv/.codex/skills/check-board" ] \
  || fail "retired root .codex/skills still carries the kit's check-board after materialize"
[ -f "$recv/.codex/skills/receiver-own-skill/SKILL.md" ] \
  || fail "retired-root sweep destroyed a skill the kit does not own"
[ ! -e "$recv/.codex/skills/pnext-item" ] && [ ! -L "$recv/.codex/skills/pnext-item" ] \
  || fail "retired-root sweep left a generated view behind at .codex/skills/pnext-item"
[ -f "$WORK/canonical-victim/SKILL.md" ] \
  || fail "retired-root sweep RECURSED THROUGH A SYMLINK and destroyed the canonical tree"
echo "   retired root .codex/skills swept: kit copies removed, a view unlinked not followed, the receiver's own skill kept"
unexpected_exec="$(find "$recv/.claude/skills" "$recv/.agents/skills" \
  "$recv/scripts/lib" -type f -perm /111 -print)"
[ -z "$unexpected_exec" ] || fail "non-client receiver output is executable after fresh materialize: $unexpected_exec"
# build-config is OPT-IN: the default materialize must NOT write it.
[ ! -f "$recv/Directory.Build.props" ]    || fail "build-config materialized without opt-in (Directory.Build.props at receiver root)"
echo "   4 skills × 2 roots + 2 executable clients + 2 libraries; engine manifest correctly NOT delivered (#1615); build-config correctly withheld (opt-in off)"

echo "== 3a. overwrite + byte-identical materialization normalize metadata =="
probe="$recv/.agents/skills/check-board/SKILL.md"
printf '\nSTALE\n' >> "$probe"
chmod a-x "$probe"
dotnet msbuild "$WORK/materialize.proj" -t:FsggKitMaterialize -nologo >/dev/null
[ ! -x "$probe" ] || fail "overwrite inherited executable cache metadata for $probe"
chmod a+x "$probe"
dotnet msbuild "$WORK/materialize.proj" -t:FsggKitMaterialize -nologo >/dev/null
[ ! -x "$probe" ] || fail "byte-identical materialize left non-client executable: $probe"
[ -x "$recv/scripts/fsgg-coord" ] || fail "byte-identical materialize removed client execute mode"
echo "   copied and hash-equal non-client destinations normalized non-executable; client remains executable"

echo "== 3b. build-config materializes at the receiver root when opted in =="
recv2="$WORK/recv-bc"; mkdir -p "$recv2"
cat > "$WORK/materialize-bc.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$recv2</FsggKitReceiverRoot>
    <FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig>
  </PropertyGroup>
</Project>
EOF
dotnet msbuild "$WORK/materialize-bc.proj" -t:FsggKitMaterialize -nologo >/dev/null
for f in Directory.Build.props Directory.Packages.props; do
  [ -f "$recv2/$f" ] || fail "build-config not materialized with opt-in: $f"
  [ ! -x "$recv2/$f" ] || fail "build-config materialized executable: $f"
done
# and global.json must NOT be carried (.github#903 — deliberately unmanaged)
[ ! -f "$recv2/global.json" ] || fail "global.json was materialized — it must stay unmanaged (.github#903)"
echo "   Directory.Build.props + Directory.Packages.props materialized (opt-in); global.json correctly withheld"

echo "== 3c. adopt/marker safety: a hand-authored .props is REFUSED, and --adopt preserves it (.github#387) =="
recv3="$WORK/recv-adopt"; mkdir -p "$recv3"
# A pre-existing, hand-authored Directory.Build.props (no 'Source of truth' marker).
printf '<Project>\n  <PropertyGroup><HandAuthored>keepme</HandAuthored></PropertyGroup>\n</Project>\n' > "$recv3/Directory.Build.props"
handauthored_sha="$(sha256sum "$recv3/Directory.Build.props" | cut -d' ' -f1)"
cat > "$WORK/adopt.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$recv3</FsggKitReceiverRoot>
    <FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig>
  </PropertyGroup>
</Project>
EOF
# Default (no adopt): materialize MUST refuse and leave the hand-authored file byte-for-byte untouched.
if dotnet msbuild "$WORK/adopt.proj" -t:FsggKitMaterialize -nologo >/dev/null 2>&1; then
  fail "materialize CLOBBERED a hand-authored Directory.Build.props — the .github#387 refusal is not firing"
fi
[ "$(sha256sum "$recv3/Directory.Build.props" | cut -d' ' -f1)" = "$handauthored_sha" ] \
  || fail "hand-authored Directory.Build.props was modified despite the refusal"
[ ! -f "$recv3/Directory.Build.local.props" ] \
  || fail "refusal path created a *.local.props — it must not touch anything"
# --adopt (FsggKitAdoptBuildConfig=true): move the hand-authored file to *.local.props, then write canonical.
dotnet msbuild "$WORK/adopt.proj" -t:FsggKitMaterialize -nologo -p:FsggKitAdoptBuildConfig=true >/dev/null
[ "$(sha256sum "$recv3/Directory.Build.local.props" | cut -d' ' -f1)" = "$handauthored_sha" ] \
  || fail "adopt did not preserve the hand-authored file as Directory.Build.local.props"
grep -q "Source of truth: FS-GG/.github" "$recv3/Directory.Build.props" \
  || fail "adopt did not write the canonical (marked) Directory.Build.props"
# A second run is now idempotent: the canonical file carries the marker, so no refusal, no re-adopt.
dotnet msbuild "$WORK/adopt.proj" -t:FsggKitMaterialize -nologo >/dev/null \
  || fail "materialize refused a canonical (marked) .props — the marker check is too strict"
echo "   unmarked .props refused (and left intact); adopt moved it to *.local.props; marked .props overwritten freely"

echo "== 3d. a multi-file skill transports bytes, mode, and a closed file set =="
mkdir -p "$WORK/unpacked/kit/skills/check-board/references"
printf '# packaged reference\n' > "$WORK/unpacked/kit/skills/check-board/references/transport.md"
chmod a+x "$WORK/unpacked/kit/skills/check-board/references/transport.md"
ref_sha="$(sha256sum "$WORK/unpacked/kit/skills/check-board/references/transport.md" | cut -d' ' -f1)"
printf 'skill\tskills/check-board/references/transport.md\tcheck-board/references/transport.md\t%s\ttrue\n' \
  "$ref_sha" >> "$WORK/unpacked/kit/kit-manifest.tsv"
mkdir -p "$recv/.claude/skills/check-board"
printf 'stale\n' > "$recv/.claude/skills/check-board/undeclared.txt"
dotnet msbuild "$WORK/materialize.proj" -t:FsggKitMaterialize -nologo >/dev/null
for root in .claude/skills .agents/skills; do
  [ -x "$recv/$root/check-board/references/transport.md" ] \
    || fail "multi-file skill reference was not materialized executable: $root"
done
[ ! -e "$recv/.claude/skills/check-board/undeclared.txt" ] \
  || fail "extra undeclared file survived closed-set skill materialization"
echo "   auxiliary reference reached all roots with its mode; stale undeclared file removed"

echo "== 3e. the phase-4 REPLACEMENT is delivered and RUNS on a receiver (.github#1696) =="
# ADR-0067 §9 precondition 1 is "the replacement is present and EXECUTABLE in R", and #1696 measured
# that no published kit had ever satisfied it. `[ -x ]` alone would not measure it either: skill-view
# sources two libraries at startup, so a receiver could hold an executable file that dies on line 87
# while precondition 1 read as satisfied. So this leg RUNS the tool from the receiver's own tree.
[ -x "$recv/scripts/skill-view" ]      || fail "skill-view not materialized executable at scripts/skill-view"
[ -f "$recv/scripts/lib/args.sh" ]     || fail "skill-view's lib/args.sh not materialized at scripts/lib/args.sh"
[ -f "$recv/scripts/lib/roots.sh" ]    || fail "skill-view's lib/roots.sh not materialized at scripts/lib/roots.sh"
( cd "$recv" && ./scripts/skill-view check --source .claude/skills --tree . >/dev/null 2>&1 ) \
  || fail "the materialized scripts/skill-view could not run in the receiver tree (its libraries did not arrive, or it is broken)"
echo "   scripts/skill-view + lib/args.sh + lib/roots.sh materialized; the tool RUNS from the receiver tree"

echo "== 3f. a VIEW root: swept, never copied into, and CERTIFIED — or the build fails (.github#1696) =="
recv4="$WORK/recv-view"; mkdir -p "$recv4"
# A receiver mid-migration: `.claude/skills` is the live root, `.agents/skills` is being turned into a
# generated view, and it still holds the copies an OLDER kit materialized there — plus one skill of the
# receiver's own, which is not the kit's to remove.
mkdir -p "$recv4/.agents/skills/check-board" "$recv4/.agents/skills/receiver-own-skill"
printf 'stale kit copy\n' > "$recv4/.agents/skills/check-board/SKILL.md"
printf 'the receiver own skill\n' > "$recv4/.agents/skills/receiver-own-skill/SKILL.md"
cat > "$WORK/view.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$recv4</FsggKitReceiverRoot>
    <FsggKitSkillRoots>.claude/skills</FsggKitSkillRoots>
    <FsggKitViewSkillRoots>.agents/skills</FsggKitViewSkillRoots>
  </PropertyGroup>
</Project>
EOF
# PASS 1 — the sweep runs, and then §8 REFUSES to certify a root with no view in it. The refusal is
# the point: a view root is a root the kit does not write, so it is a root nothing else can vouch for.
# THE LOUD-FAILURE LEG, and since FsggKitGenerateSkillView it fires one step EARLIER: the kit now
# generates the view on the materialize path, and `skill-view generate` REFUSES a root that is an
# occupied real directory. So the build dies at the generate rather than at §8's certification — the
# failure MOVED, it did not vanish. The output is asserted, not just the exit code: a leg that checked
# only "non-zero" would pass on any unrelated breakage, and would have stayed green through this very
# change while asserting nothing about it.
view_out1="$(dotnet msbuild "$WORK/view.proj" -t:FsggKitMaterialize -nologo 2>&1)" && \
  fail "materialize SUCCEEDED with an ungenerated, OCCUPIED view root — neither the generate's refusal
  nor ADR-0067 §8's absence check is firing"
printf '%s' "$view_out1" | grep -qiE "skill-view|view root|refus|tracked" \
  || fail "materialize failed on an occupied view root, but for an UNSTATED reason — the red must name
  the view root or the generate that refused it, or this leg is green on unrelated breakage. Got:
$view_out1"
[ ! -e "$recv4/.agents/skills/check-board" ] \
  || fail "view root still carries the kit's materialized check-board copy after materialize"
[ -f "$recv4/.agents/skills/receiver-own-skill/SKILL.md" ] \
  || fail "the view-root sweep destroyed a skill the kit does not own"
for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
  [ -f "$recv4/.claude/skills/$s/SKILL.md" ] || fail "live root not materialized while a view root was declared: $s"
  [ ! -e "$recv4/.agents/skills/$s" ]        || fail "the materializer COPIED into a view root: $s"
done
echo "   pass 1: kit copies swept from the view root, the receiver's own skill kept, nothing copied in, §8 refused to certify"

# The receiver resolves its own skill (moving it under the live source is the migration ADR-0067 §6
# describes), and the now-empty root is removed BY THE MATERIALIZER — never by hand (ADR-0065).
rm -rf "$recv4/.agents/skills/receiver-own-skill"
# PASS 2 RE-ENCODED. This asserted that an ABSENT view root made the materialize FAIL. It no longer
# can: FsggKitGenerateSkillView generates the view on the materialize path, so "absent" is a state the
# kit REPAIRS rather than reports.
#
# §8 says "a rewrite that removes the loud failure and adds the quiet one is worse than no rewrite",
# and this is deliberately not that. Absence was only ever a symptom of "the receiver forgot to wire a
# generate target" — seven receivers each hand-wrote one to avoid it. Making the package do it removes
# the failure mode BY CONSTRUCTION, which beats detecting it. What remains detectable is asserted
# elsewhere: pass 1 above (an occupied root the generate refuses), and every §8 absence class on the
# DETECT path, which this target is deliberately not wired into (see the target's own comment).
if ! dotnet msbuild "$WORK/view.proj" -t:FsggKitMaterialize -nologo >/dev/null 2>&1; then
  fail "materialize FAILED on an absent view root — the kit's own FsggKitGenerateSkillView should have
  generated it before §8 asserted it"
fi
[ -e "$recv4/.agents/skills" ] \
  || fail "the materializer left no view root at all — it must GENERATE one, not tolerate absence"
[ -L "$recv4/.agents/skills" ] || [ -L "$recv4/.agents/skills/check-board" ] \
  || fail "the view root is a real directory of real files — the kit COPIED where it must GENERATE"
for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
  [ -f "$recv4/.agents/skills/$s/SKILL.md" ] \
    || fail "the generated view does not carry $s"
done
echo "   pass 2: an absent view root is GENERATED by the kit and then certified — no receiver wiring"

# PASS 3 — generate the view WITH THE TOOL THE KIT JUST DELIVERED, then materialize again. This is the
# whole point of the item in one command: a receiver, holding only what the package gave it, produces
# its own second root and the kit certifies it.
( cd "$recv4" && ./scripts/skill-view generate --source .claude/skills --tree . --roots ".agents/skills" >/dev/null ) \
  || fail "the materialized skill-view could not generate the view root"
# Either shape skill-view can produce, because the sweep has to recognise BOTH as "already a view":
# a symlink where the filesystem allows one, a copy carrying a `.skill-view` receipt where it does not.
[ -L "$recv4/.agents/skills" ] || [ -f "$recv4/.agents/skills/.skill-view" ] \
  || fail "skill-view produced neither a link nor a .skill-view-receipted copy at the view root"
dotnet msbuild "$WORK/view.proj" -t:FsggKitMaterialize -nologo >/dev/null \
  || fail "materialize FAILED over a correctly generated view root — §8's check rejects a valid view"
for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
  [ -f "$recv4/.agents/skills/$s/SKILL.md" ] || fail "skill not visible through the generated view: $s"
  [ -f "$recv4/.claude/skills/$s/SKILL.md" ] || fail "the view-root sweep RECURSED THROUGH THE LINK and destroyed the live root: $s"
done
# And the assertion stands alone, with no materialize — the shape a receiver wires into its own gate.
dotnet msbuild "$WORK/view.proj" -t:FsggKitCheckSkillView -nologo >/dev/null \
  || fail "FsggKitCheckSkillView failed as a standalone target over a valid view"
echo "   pass 3: view generated by the delivered tool, certified by the kit, live root intact through the link"

# A COPY-mode view — what skill-view produces where the filesystem refuses a symlink (the
# `core.symlinks=false` Windows case is exactly a receiver that cannot have the link shape). The sweep
# must recognise the `.skill-view` receipt as "already a view" and leave it alone, or the second
# materialize would delete the generated copies and the check would then fail over its own doing.
rm -f "$recv4/.agents/skills"
( cd "$recv4" && ./scripts/skill-view generate --source .claude/skills --tree . --roots ".agents/skills" --mode copy >/dev/null ) \
  || fail "skill-view could not generate a COPY-mode view"
[ -f "$recv4/.agents/skills/.skill-view" ] || fail "copy-mode view carries no .skill-view receipt"
dotnet msbuild "$WORK/view.proj" -t:FsggKitMaterialize -nologo >/dev/null \
  || fail "materialize FAILED over a COPY-mode view root — the .skill-view receipt guard is not recognised"
for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
  [ -f "$recv4/.agents/skills/$s/SKILL.md" ] || fail "the sweep destroyed a COPY-mode view it should have left alone: $s"
done
echo "   a copy-mode view (the filesystem-refuses-symlinks shape) is recognised by its receipt and not swept"

# The three silent classes phase 1 measured must each be LOUD here.
rm -rf "$recv4/.agents/skills"; ln -s "$recv4/nowhere" "$recv4/.agents/skills"
if dotnet msbuild "$WORK/view.proj" -t:FsggKitCheckSkillView -nologo >/dev/null 2>&1; then
  fail "a DANGLING view root was certified — both runtimes would resolve zero skills and exit 0"
fi
rm -rf "$recv4/.agents/skills"; printf '../.claude/skills\n' > "$recv4/.agents/skills"
if dotnet msbuild "$WORK/view.proj" -t:FsggKitCheckSkillView -nologo >/dev/null 2>&1; then
  fail "a TEXT-FILE view root (core.symlinks=false checkout) was certified"
fi
rm -rf "$recv4/.agents/skills"; mkdir -p "$recv4/.agents/skills/check-board"
cp "$recv4/.claude/skills/check-board/SKILL.md" "$recv4/.agents/skills/check-board/SKILL.md"
if dotnet msbuild "$WORK/view.proj" -t:FsggKitCheckSkillView -nologo >/dev/null 2>&1; then
  fail "a PARTLY populated view root was certified — a partial root is as silent as an empty one"
fi
echo "   dangling link, core.symlinks=false text file, and a partial root each rejected"

echo "== 3g. a root cannot hold two dispositions at once =="
cat > "$WORK/view-conflict.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$WORK/recv-conflict</FsggKitReceiverRoot>
    <FsggKitSkillRoots>.claude/skills;.agents/skills</FsggKitSkillRoots>
    <FsggKitViewSkillRoots>.agents/skills</FsggKitViewSkillRoots>
  </PropertyGroup>
</Project>
EOF
# THE CONFLICT IS DECLARED, NOT INHERITED (ADR-0067 §9 stage 2, .github#1676). This fixture used to
# name `.agents/skills` as a view root ONLY, and relied on it also being in the DEFAULT
# FsggKitSkillRoots to create the overlap. Stage 2 narrowed that default to one root, so the overlap
# silently disappeared and this leg stopped testing a conflict at all — it passed because there was
# nothing to refuse. A leg whose subject is manufactured by a default is a leg that evaporates when
# the default moves (.github#1849). Both properties now name the root explicitly.
#
# Silently picking either reading destroys something: materializing overwrites a view, and viewing
# deletes the only copies. It must refuse.
if dotnet msbuild "$WORK/view-conflict.proj" -t:FsggKitMaterialize -nologo >/dev/null 2>&1; then
  fail "a root declared BOTH materialized and view was accepted — the materializer picked a disposition silently"
fi
echo "   a root named in two dispositions is refused, not resolved"

echo "== 3h. the DEFAULT dispositions: one materialized root, one generated view =="
# THE LEG THAT PINS THE DEFAULT, and stage 2 is precisely a change to it (ADR-0067 §9, .github#1676).
#
# It used to read "no view roots declared: the kit behaves exactly as 0.14.0 did" and assert that a
# receiver configuring nothing materialized BOTH roots — because FsggKitViewSkillRoots was empty by
# default and #1696 was stage 0, which deliberately retired nothing. Stage 1 is now 7 of 7, so the
# default describes the END STATE instead: ONE materialized root, and `.agents/skills` as a generated
# VIEW.
#
# THE RUNTIME CONTRACT IS UNCHANGED — it is the union of the materialized and view roots, still
# ADR-0065's two — so this leg asserts the union AND the disposition. Asserting only that the skills
# are readable at both roots would pass just as well on the second byte-copy stage 2 exists to stop.
recv5="$WORK/recv-default"; mkdir -p "$recv5"
cat > "$WORK/default.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/unpacked/kit</FsggKitDir>
    <FsggKitReceiverRoot>$recv5</FsggKitReceiverRoot>
  </PropertyGroup>
</Project>
EOF
dotnet msbuild "$WORK/default.proj" -t:FsggKitMaterialize -nologo >/dev/null \
  || fail "the DEFAULT materialize failed — a receiver that configures nothing must still get a
  working kit, and stage 2's defaults must not require any receiver-side wiring"
# THE UNION: every skill visible at BOTH runtime roots, which is what the contract promises.
for root in .claude/skills .agents/skills; do
  for s in cross-repo-coordination intra-repo-parallel-work check-board pnext-item; do
    [ -f "$recv5/$root/$s/SKILL.md" ] || fail "default materialize lost a runtime root: $root/$s"
  done
done
# THE DISPOSITION: one of those two is a GENERATED VIEW, not a second copy. Without this the leg above
# is satisfied by exactly the duplication ADR-0067 retired.
[ -L "$recv5/.agents/skills" ] || [ -L "$recv5/.agents/skills/check-board" ] \
  || fail "the default materialized .agents/skills as a real directory of real files — stage 2's
  default must make it a generated VIEW, or the narrowing bought nothing"
[ ! -L "$recv5/.claude/skills" ] \
  || fail "the default made the SOURCE root a link — the materialized root must hold the real bytes"
echo "   default: one materialized root + one generated view; the runtime union is still ADR-0065's two"

echo "== 4. a tampered kit file is a LOUD failure =="
cp -r "$WORK/unpacked/kit" "$WORK/tampered"
echo "CORRUPT" >> "$WORK/tampered/skills/pnext-item/SKILL.md"     # bytes drift from the recorded sha256
cat > "$WORK/tamper.proj" <<EOF
<Project>
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.props" />
  <Import Project="$WORK/unpacked/build/FS.GG.Kit.targets" />
  <PropertyGroup>
    <FsggKitDir>$WORK/tampered</FsggKitDir>
    <FsggKitReceiverRoot>$WORK/recv-tamper</FsggKitReceiverRoot>
  </PropertyGroup>
</Project>
EOF
if dotnet msbuild "$WORK/tamper.proj" -t:FsggKitMaterialize -nologo >/dev/null 2>&1; then
  fail "materialize SUCCEEDED against a tampered kit — the content-addressed verify is not firing"
fi
echo "   tampered kit rejected (build error), as required"

echo "verify-package: OK"

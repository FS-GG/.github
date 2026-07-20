#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Kit gate (ADR-0062). Run by .github/workflows/kit-package.yml, and
# locally. Nothing executes a workflow, so the verification lives in a script the workflow CALLS — the
# same reason the landable/coherence gates moved out of hand-copied YAML (#724).
#
# It proves the things the package must get right:
#   1. DERIVED, NOT RESTATED — coordination-kit digests match registry/repos.lock exactly (ADR-0058),
#      and the build-config member count matches sync-build-config.sh's FILES (its derive source).
#   2. PACKS — `dotnet pack` produces a nupkg carrying every manifest member + the build logic.
#   3. MATERIALIZES — a receiver gets the skills on disk in every root, the client executable, and the
#      engine manifest at .config/ (ADR-0062's load-bearing half); build-config is withheld unless the
#      consumer opts in, and (3b) lands at the receiver root when it does — global.json never carried.
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
n_ck=0; n_bc=0
while IFS=$'\t' read -r kind pkgrel _dest sha; do
  if [ "$kind" = "build-config" ]; then
    # build-config has no repos.lock row (ADR-0036 pin model): its sha256 is a self-consistent
    # integrity record, checked at materialize, not a cross-check against repos.lock.
    n_bc=$((n_bc + 1))
  else
    grep -qi "^$sha  " "$LOCK" || fail "staged digest $sha ($pkgrel) is not in registry/repos.lock (derive-don't-restate broken)"
    n_ck=$((n_ck + 1))
  fi
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
n_files="$(sed -n '/^FILES=(/,/^)/{/^FILES=(/d;/^)/d;s/#.*//;s/"//g;p}' "$SRC_ROOT/scripts/sync-build-config.sh" | grep -c .)"
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
while IFS=$'\t' read -r _kind pkgrel _dest _sha; do
  grep -qx "kit/$pkgrel" <<<"$entries" || fail "nupkg is missing kit/$pkgrel (named by the manifest)"
done < "$WORK/stage/kit-manifest.tsv"
echo "   nupkg carries every kit member + manifest + build logic"

echo "== 3. materialize into a fresh receiver root =="
unzip -q "$nupkg" "kit/*" -d "$WORK/unpacked"
recv="$WORK/recv"; mkdir -p "$recv"
cat > "$WORK/materialize.proj" <<EOF
<Project>
  <Import Project="$HERE/build/FS.GG.Kit.props" />
  <Import Project="$HERE/build/FS.GG.Kit.targets" />
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
[ -f "$recv/.config/dotnet-tools.json" ] || fail "engine manifest not materialized at .config/dotnet-tools.json"
# build-config is OPT-IN: the default materialize must NOT write it.
[ ! -f "$recv/Directory.Build.props" ]    || fail "build-config materialized without opt-in (Directory.Build.props at receiver root)"
echo "   4 skills × 2 roots + executable client + engine manifest; build-config correctly withheld (opt-in off)"

echo "== 3b. build-config materializes at the receiver root when opted in =="
recv2="$WORK/recv-bc"; mkdir -p "$recv2"
cat > "$WORK/materialize-bc.proj" <<EOF
<Project>
  <Import Project="$HERE/build/FS.GG.Kit.props" />
  <Import Project="$HERE/build/FS.GG.Kit.targets" />
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
done
# and global.json must NOT be carried (.github#903 — deliberately unmanaged)
[ ! -f "$recv2/global.json" ] || fail "global.json was materialized — it must stay unmanaged (.github#903)"
echo "   Directory.Build.props + Directory.Packages.props materialized (opt-in); global.json correctly withheld"

echo "== 4. a tampered kit file is a LOUD failure =="
cp -r "$WORK/unpacked/kit" "$WORK/tampered"
echo "CORRUPT" >> "$WORK/tampered/skills/pnext-item/SKILL.md"     # bytes drift from the recorded sha256
cat > "$WORK/tamper.proj" <<EOF
<Project>
  <Import Project="$HERE/build/FS.GG.Kit.props" />
  <Import Project="$HERE/build/FS.GG.Kit.targets" />
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

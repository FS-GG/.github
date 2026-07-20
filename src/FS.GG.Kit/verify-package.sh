#!/usr/bin/env bash
# verify-package.sh — the FS.GG.Kit gate (ADR-0062). Run by .github/workflows/kit-package.yml, and
# locally. Nothing executes a workflow, so the verification lives in a script the workflow CALLS — the
# same reason the landable/coherence gates moved out of hand-copied YAML (#724).
#
# It proves the four things the package must get right:
#   1. DERIVED, NOT RESTATED — the staged kit's digests match registry/repos.lock exactly, so the
#      package and the byte-copy fabric ship the identical content-addressed set (ADR-0058).
#   2. PACKS — `dotnet pack` produces a nupkg carrying every kit member + the manifest + build logic.
#   3. MATERIALIZES — a receiver referencing the package gets the skills on disk in every skill root,
#      the client executable, and the engine manifest at .config/ (ADR-0062's load-bearing half).
#   4. FAILS LOUD — a tampered kit file is a build ERROR, never a silently missing/stale skill (ADR-0014).
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC_ROOT="$(cd "$HERE/../.." && pwd)"
LOCK="$SRC_ROOT/registry/repos.lock"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
fail() { echo "verify-package: FAIL — $*" >&2; exit 1; }

echo "== 1. stage + digest parity vs registry/repos.lock =="
bash "$HERE/stage-kit.sh" "$WORK/stage" >/dev/null
while IFS=$'\t' read -r _kind _pkgrel _dest sha; do
  # every packed digest must be the one repos.lock records for some kit source
  grep -qi "^$sha  " "$LOCK" || fail "staged digest $sha is not in registry/repos.lock (derive-don't-restate broken)"
done < "$WORK/stage/kit-manifest.tsv"
n_staged="$(wc -l < "$WORK/stage/kit-manifest.tsv")"
echo "   $n_staged staged kit file(s), all digests present in repos.lock"

echo "== 2. pack + content assert =="
dotnet pack "$HERE/FS.GG.Kit.csproj" -c Release -o "$WORK/out" >/dev/null
nupkg="$(echo "$WORK"/out/FS.GG.Kit.*.nupkg)"
[ -f "$nupkg" ] || fail "no nupkg produced"
entries="$(unzip -Z1 "$nupkg")"
for want in \
  "build/FS.GG.Kit.props" "build/FS.GG.Kit.targets" "README.md" "kit/kit-manifest.tsv" \
  "kit/client/fsgg-coord" "kit/config/dotnet-tools.json" \
  "kit/skills/cross-repo-coordination/SKILL.md" "kit/skills/intra-repo-parallel-work/SKILL.md" \
  "kit/skills/check-board/SKILL.md" "kit/skills/pnext-item/SKILL.md"; do
  grep -qx "$want" <<<"$entries" || fail "nupkg is missing $want"
done
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
echo "   4 skills × 2 roots + executable client + engine manifest all present"

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

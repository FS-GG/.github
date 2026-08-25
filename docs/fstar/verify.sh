#!/usr/bin/env bash
set -euo pipefail

experiment_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
toolchain_version="2026.08.23"
archive_name="fstar-v${toolchain_version}-Linux-x86_64.tar.gz"
archive_sha256="4d53a5e591e6a22248093e2b3d1f243f57e8d5c1b1b329dd94706db4f0f7abb3"
download_url="https://github.com/FStarLang/FStar/releases/download/v${toolchain_version}/${archive_name}"
cache_root="${FSTAR_SPIKE_CACHE:-${XDG_CACHE_HOME:-/tmp}/fsgg-fstar-specification-spike}"

if [[ -n "${FSTAR_EXE:-}" ]]; then
  fstar_executable="$FSTAR_EXE"
else
  install_root="$cache_root/fstar-v${toolchain_version}"
  fstar_executable="$install_root/fstar/bin/fstar.exe"

  if [[ ! -x "$fstar_executable" ]]; then
    mkdir -p "$cache_root"
    archive_path="$cache_root/$archive_name"
    if [[ ! -f "$archive_path" ]]; then
      curl --fail --location --silent --show-error "$download_url" --output "$archive_path"
    fi

    actual_sha256="$(sha256sum "$archive_path" | awk '{print $1}')"
    if [[ "$actual_sha256" != "$archive_sha256" ]]; then
      echo "F* archive digest mismatch: expected $archive_sha256, got $actual_sha256" >&2
      exit 1
    fi

    staging_root="$(mktemp -d "$cache_root/extract.XXXXXX")"
    tar -xzf "$archive_path" -C "$staging_root"
    rm -rf "$install_root"
    mv "$staging_root" "$install_root"
  fi
fi

if [[ ! -x "$fstar_executable" ]]; then
  echo "F* executable is unavailable: $fstar_executable" >&2
  exit 1
fi

work_root="$(mktemp -d /tmp/fsgg-fstar-specifications.XXXXXX)"
cache_dir="$work_root/checked"
extraction_dir="$work_root/fsharp"
mkdir -p "$cache_dir" "$extraction_dir"

modules=(
  "$experiment_root/combat/SIR.CombatConsequences.fst"
  "$experiment_root/communication/SIR.CommunicationNetwork.fst"
)

"$fstar_executable" --version

for module_path in "${modules[@]}"; do
  "$fstar_executable" \
    --cache_checked_modules \
    --cache_dir "$cache_dir" \
    --ext fly_deps=false \
    "$module_path"
done

for module_path in "${modules[@]}"; do
  "$fstar_executable" \
    --cache_checked_modules \
    --cache_dir "$cache_dir" \
    --ext fly_deps=false \
    --codegen FSharp \
    --odir "$extraction_dir" \
    "$module_path"
done

echo "Verified 2 F* modules."
echo "Extracted F# evidence: $extraction_dir"

if [[ -n "${FSTAR_DOTNET:-}" ]]; then
  if [[ ! -x "$FSTAR_DOTNET" ]]; then
    echo "FSTAR_DOTNET is not executable: $FSTAR_DOTNET" >&2
    exit 1
  fi

  (
    cd "$experiment_root/fsharp-smoke"
    export DOTNET_ROOT="$(cd "$(dirname "$FSTAR_DOTNET")" && pwd)"
    export DOTNET_MULTILEVEL_LOOKUP=0
    "$FSTAR_DOTNET" build \
      ExtractionSmoke.fsproj \
      --property:FStarExtractedPath="$extraction_dir" \
      --nologo \
      --verbosity quiet
    "$FSTAR_DOTNET" bin/Debug/net8.0/ExtractionSmoke.dll
  )
fi

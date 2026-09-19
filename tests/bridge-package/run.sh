#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
version="$(dotnet msbuild "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" -getProperty:Version | tr -d '[:space:]')"
if [ "$version" != 0.90.0 ]; then
  # GS2-08.7 qualifies the immutable 0.90 bridge, not whichever coherent-set
  # source version happens to be in the current PR. Re-run its unchanged probe
  # from the accepted release commit, keeping source and package identities
  # together. The successor has its own candidate and publication gates.
  accepted=3adada5a9738464291088830c47a30a3a8fc9561
  historical="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-accepted-bridge.XXXXXX")"
  cleanup_historical() {
    if [ -d "$historical/source" ]; then
      git -C "$ROOT" worktree remove --force "$historical/source"
    fi
    rmdir "$historical"
  }
  trap cleanup_historical EXIT
  git -C "$ROOT" worktree add --detach "$historical/source" "$accepted"
  bash "$historical/source/tests/bridge-package/run.sh"
  echo "bridge-package: accepted 0.90 source proof passed; current $version is a separate candidate"
  exit 0
fi
WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-bridge-package.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

package="${1:-}"
if [ -z "$package" ]; then
  cache="$WORK/build-cache"
  mkdir -p "$cache/packages" "$cache/http" "$cache/home" "$WORK/packages"
  export NUGET_PACKAGES="$cache/packages"
  export NUGET_HTTP_CACHE_PATH="$cache/http"
  export DOTNET_CLI_HOME="$cache/home"
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  dotnet restore "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" --locked-mode
  dotnet pack "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" -c Release --no-restore -o "$WORK/packages"
  package="$WORK/packages/FS.GG.Coord.Cli.0.90.0.nupkg"
fi

package="$(realpath "$package")"
head="$(git -C "$ROOT" rev-parse HEAD)"
tree="$(git -C "$ROOT" rev-parse 'HEAD^{tree}')"
binding="$WORK/candidate.json"

python3 "$HERE/run.py" describe --package "$package" --source-commit "$head" --source-tree "$tree" >"$binding"
python3 "$HERE/run.py" verify --package "$package" --binding "$binding"
python3 "$HERE/selftest.py" --package "$package" --binding "$binding"
if [ -n "${FSGG_BRIDGE_BINDING_OUTPUT:-}" ]; then
  install -m 0644 "$binding" "$FSGG_BRIDGE_BINDING_OUTPUT"
fi

echo "bridge-package: PASS"

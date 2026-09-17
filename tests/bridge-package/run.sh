#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
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
  package="$WORK/packages/FS.GG.Coord.Cli.0.89.0.nupkg"
fi

package="$(realpath "$package")"
head="$(git -C "$ROOT" rev-parse HEAD)"
tree="$(git -C "$ROOT" rev-parse 'HEAD^{tree}')"
binding="$WORK/candidate.json"

python3 "$HERE/run.py" describe --package "$package" --source-commit "$head" --source-tree "$tree" >"$binding"
python3 "$HERE/run.py" verify --package "$package" --binding "$binding"
python3 "$HERE/selftest.py" --package "$package" --binding "$binding"

echo "bridge-package: PASS"

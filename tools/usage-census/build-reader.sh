#!/usr/bin/env bash
# Build the strict usage reader (`fsgg-coord-engine`) from source at a pinned commit.
#
# usage: build-reader.sh OUT_DIR [COMMIT]
#
# The #4823 and #4639 censuses used the CLI at 61526e947ff595c5eb416efd810ce3c1348bedc5 (PR #4802),
# built read-only and outside any repository. A built reader is NOT checked in: it is a 67 MB .NET build
# output tied to one commit. Build it on the coordinator host and point config.json `reader` at
# OUT_DIR/fsgg-coord-engine. OUT_DIR must lie outside every repository and the temporary directory.
set -euo pipefail

mkdir -p "${1:?usage: build-reader.sh OUT_DIR [COMMIT]}" "${HOME}/.cache"
out=$(cd "$1" && pwd)
commit=${2:-61526e947ff595c5eb416efd810ce3c1348bedc5}
repo=$(git -C "$(dirname "$0")" rev-parse --show-toplevel)
src=$(mktemp -d "${HOME}/.cache/usage-census-reader-src.XXXXXX")
cleanup() { git -C "$repo" worktree remove --force "$src" >/dev/null 2>&1 || rm -rf "$src"; }
trap cleanup EXIT

git -C "$repo" worktree add --detach "$src" "$commit" >/dev/null
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# Two build processes at most: the census runs under a two-CPU budget.
(cd "$src" && dotnet build src/FS.GG.Coord.Cli -c Release -o "$out" -m:2)
test -x "$out/fsgg-coord-engine"
echo "built $out/fsgg-coord-engine from $commit"

#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src/FS.GG.Coord.GitHub" "$work/scripts" "$work/.github/workflows"
for f in projects-audit.sh repos-audit.sh coord-board-archive.py check-roster-closure.py; do : >"$work/scripts/$f"; done
: >"$work/.github/workflows/coord-board-archive.yml"
: >"$work/src/FS.GG.Coord.GitHub/GraphQl.fs"
: >"$work/src/FS.GG.Coord.GitHub/GraphQlEnvelope.fs"
: >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null
printf '%s\n' 'let bypass root = root.TryGetProperty "data"' >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a raw Budget envelope selector" >&2; exit 1
fi
: >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
printf '%s\n' 'gh api graphql -f query=x' >"$work/scripts/projects-audit.sh"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a production shell transport bypass" >&2; exit 1
fi
echo "graphql-boundary inversion fixture: OK"

#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; TOOL="$ROOT/scripts/check-registry-changelog.py"; WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
cp "$ROOT/registry/dependencies.yml" "$WORK/dependencies.yml"; cp "$ROOT/registry/CHANGELOG.md" "$WORK/CHANGELOG.md"
python3 "$TOOL" --dependencies "$WORK/dependencies.yml" --changelog "$WORK/CHANGELOG.md" --changed registry/dependencies.yml --changed registry/CHANGELOG.md
! python3 "$TOOL" --dependencies "$WORK/dependencies.yml" --changelog "$WORK/CHANGELOG.md" --changed registry/dependencies.yml
sed -i '3i- **2000-01-01** — misplaced dated entry' "$WORK/CHANGELOG.md"
! python3 "$TOOL" --dependencies "$WORK/dependencies.yml" --changelog "$WORK/CHANGELOG.md" --changed registry/dependencies.yml --changed registry/CHANGELOG.md
sed -i '3d' "$WORK/CHANGELOG.md"
sed -i 's/^updated: ".*"/updated: "2000-01-01"/' "$WORK/dependencies.yml"
! python3 "$TOOL" --dependencies "$WORK/dependencies.yml" --changelog "$WORK/CHANGELOG.md" --changed registry/dependencies.yml --changed registry/CHANGELOG.md

#!/usr/bin/env bash
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-bridge-receiver.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

curl -fsSL 'https://github.com/FS-GG/.github/releases/download/coherent-set%2Fv0.90.0/release-manifest.json' \
  -o "$WORK/release-manifest.json"
for package in fs.gg.coord.cli fs.gg.kit; do
  curl -fsSL "https://api.nuget.org/v3-flatcontainer/$package/0.90.0/$package.0.90.0.nupkg" \
    -o "$WORK/$package.0.90.0.nupkg"
done

revision="$(git -C "$ROOT" rev-parse HEAD)"
tree="$(git -C "$ROOT" rev-parse 'HEAD^{tree}')"
python3 "$HERE/run.py" qualify \
  --receiver-root "$ROOT" \
  --receiver-revision "$revision" \
  --receiver-tree "$tree" \
  --evidence "$ROOT/docs/reports/gs2-08-8-bridge-adoption.json" \
  --release-manifest "$WORK/release-manifest.json" \
  --packages "$WORK"
python3 "$HERE/selftest.py" \
  --receiver-root "$ROOT" \
  --receiver-revision "$revision" \
  --receiver-tree "$tree" \
  --release-manifest "$WORK/release-manifest.json" \
  --packages "$WORK"

echo "bridge-receivers: PASS"

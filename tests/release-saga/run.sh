#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOOL="$ROOT/scripts/release-saga.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/release-saga.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/artifacts" "$WORK/github" "$WORK/nuget"

make_package() {
  python3 - "$1" "$2" "$3" <<'PY'
import pathlib, sys, zipfile
target, package_id, version = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
nuspec = f'''<?xml version="1.0"?><package><metadata><id>{package_id}</id><version>{version}</version><authors>FS-GG</authors><description>fixture</description><releaseNotes>{version} release</releaseNotes><dependencies><group targetFramework="net10.0"><dependency id="FSharp.Core" version="[10.0.100, )" /></group></dependencies></metadata></package>'''
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    archive.writestr(f"{package_id}.nuspec", nuspec)
    archive.writestr("content/payload.txt", f"{package_id}:{version}\n")
PY
}

for package in FS.GG.Coord.Cli FS.GG.Kit FS.GG.Drivers; do
  make_package "$WORK/artifacts/$package.9.8.7.nupkg" "$package" 9.8.7
done

python3 "$TOOL" prepare \
  --release-id fixture-9.8.7 --version 9.8.7 \
  --source-sha 0123456789012345678901234567890123456789 \
  --policy-version release-saga/1 --artifact-dir "$WORK/artifacts" \
  --expected-package FS.GG.Coord.Cli --expected-package FS.GG.Kit --expected-package FS.GG.Drivers \
  --output "$WORK/manifest.json"
python3 "$TOOL" preflight --manifest "$WORK/manifest.json" --feed both

# GitHub accepts the first package, then the publisher dies.  The failure is durable, and a
# restarted process uses the same manifest and exact package bytes rather than packing again.
cp "$WORK/artifacts/FS.GG.Coord.Cli.9.8.7.nupkg" "$WORK/github/"
python3 "$TOOL" record-observed --manifest "$WORK/manifest.json" --feed github \
  --observed "FS.GG.Coord.Cli=$WORK/github/FS.GG.Coord.Cli.9.8.7.nupkg" --detail "fixture first push"
python3 "$TOOL" record-failure --manifest "$WORK/manifest.json" --feed github \
  --package FS.GG.Kit --detail "forced fixture failure after first irreversible push"
jq -e '.state.feeds.github.state == "partial" and .state.recovery.lastFailure.package == "FS.GG.Kit"' "$WORK/manifest.json" >/dev/null

# Drift is rejected before the resume can make another irreversible write.
cp "$WORK/artifacts/FS.GG.Kit.9.8.7.nupkg" "$WORK/drift.nupkg"
printf 'drift' >> "$WORK/artifacts/FS.GG.Kit.9.8.7.nupkg"
if python3 "$TOOL" assert-artifacts --manifest "$WORK/manifest.json" >/dev/null 2>&1; then
  echo "expected artifact byte drift to be rejected" >&2; exit 1
fi
mv "$WORK/drift.nupkg" "$WORK/artifacts/FS.GG.Kit.9.8.7.nupkg"

cp "$WORK/artifacts/FS.GG.Kit.9.8.7.nupkg" "$WORK/github/"
cp "$WORK/artifacts/FS.GG.Drivers.9.8.7.nupkg" "$WORK/github/"
python3 "$TOOL" record-observed --manifest "$WORK/manifest.json" --feed github \
  --observed "FS.GG.Kit=$WORK/github/FS.GG.Kit.9.8.7.nupkg" \
  --observed "FS.GG.Drivers=$WORK/github/FS.GG.Drivers.9.8.7.nupkg" --detail "fixture resumed from durable manifest"

# nuget.org may add an archive signature.  The externally observed archive hash is retained while
# payload identity (excluding .signature.p7s) proves it served the manifest-bound package.
for package in FS.GG.Coord.Cli FS.GG.Kit FS.GG.Drivers; do
  python3 - "$WORK/artifacts/$package.9.8.7.nupkg" "$WORK/nuget/$package.9.8.7.nupkg" <<'PY'
import sys, zipfile
with zipfile.ZipFile(sys.argv[1]) as source, zipfile.ZipFile(sys.argv[2], "w", zipfile.ZIP_DEFLATED) as target:
    for name in source.namelist(): target.writestr(name, source.read(name))
    target.writestr(".signature.p7s", b"server signature fixture")
PY
done
python3 "$TOOL" record-observed --manifest "$WORK/manifest.json" --feed nuget \
  --observed "FS.GG.Coord.Cli=$WORK/nuget/FS.GG.Coord.Cli.9.8.7.nupkg" \
  --observed "FS.GG.Kit=$WORK/nuget/FS.GG.Kit.9.8.7.nupkg" \
  --observed "FS.GG.Drivers=$WORK/nuget/FS.GG.Drivers.9.8.7.nupkg" --detail "fixture public-feed observation"
python3 "$TOOL" promote --manifest "$WORK/manifest.json" --channel-output "$WORK/stable.json"

jq -e '
  .schema == "fsgg.release-saga/1" and
  (.descriptor.packages | length) == 3 and
  ([.descriptor.packages[].dependencies[] | select(.id == "FSharp.Core")] | length) == 3 and
  .state.preflight.github.state == "passed" and .state.preflight.nuget.state == "passed" and
  .state.feeds.github.state == "verified" and .state.feeds.nuget.state == "verified" and
  .state.recovery.resumptions >= 1 and
  .state.channelPromotion.state == "promoted" and .state.phase == "promoted" and
  ([.state.feeds.nuget.packages[].externalSha256 | length > 0] | all)
' "$WORK/manifest.json" >/dev/null

# Promotion fails closed when even one target feed is incomplete.
python3 "$TOOL" prepare --release-id incomplete --version 9.8.7 \
  --source-sha 0123456789012345678901234567890123456789 --policy-version release-saga/1 \
  --artifact-dir "$WORK/artifacts" --expected-package FS.GG.Coord.Cli \
  --expected-package FS.GG.Kit --expected-package FS.GG.Drivers --output "$WORK/incomplete.json" >/dev/null
python3 "$TOOL" preflight --manifest "$WORK/incomplete.json" --feed both >/dev/null
# Policy order is part of state, not workflow prose: public-feed progress before a complete org set
# must be rejected even when the observed bytes themselves are correct.
if python3 "$TOOL" record-observed --manifest "$WORK/incomplete.json" --feed nuget \
  --observed "FS.GG.Coord.Cli=$WORK/nuget/FS.GG.Coord.Cli.9.8.7.nupkg" --detail "wrong order" >/dev/null 2>&1; then
  echo "expected nuget-before-org observation to fail closed" >&2; exit 1
fi
if python3 "$TOOL" promote --manifest "$WORK/incomplete.json" >/dev/null 2>&1; then
  echo "expected incomplete stable promotion to fail closed" >&2; exit 1
fi

echo "release saga: forced mid-publish recovery, byte drift, dual-feed observation, and promotion passed"

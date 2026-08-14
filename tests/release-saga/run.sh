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
  --policy-version release-saga/1 --previous-version 9.8.6 --artifact-dir "$WORK/artifacts" \
  --expected-package FS.GG.Coord.Cli --expected-package FS.GG.Kit --expected-package FS.GG.Drivers \
  --output "$WORK/manifest.json"
python3 "$TOOL" preflight --manifest "$WORK/manifest.json" --feed both
cp "$WORK/manifest.json" "$WORK/merge-base.json"
python3 "$TOOL" assert-identity --manifest "$WORK/manifest.json" --release-id fixture-9.8.7 \
  --version 9.8.7 --source-sha 0123456789012345678901234567890123456789 --policy-version release-saga/1
if python3 "$TOOL" assert-identity --manifest "$WORK/manifest.json" --release-id fixture-9.8.7 \
  --version 9.8.7 --source-sha ffffffffffffffffffffffffffffffffffffffff --policy-version release-saga/1 >/dev/null 2>&1; then
  echo "expected source identity mismatch to fail closed" >&2; exit 1
fi

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
printf '%s\n' '{"contentId":"sha256:previous","version":"9.8.6","sourceSha":"previous","promotedAt":"2026-08-13T00:00:00Z"}' > "$WORK/previous-stable.json"
# First-ever saga channel: no coherent-set receipt exists yet, so the content-bound descriptor's
# previousStableVersion is the monotonic baseline.
python3 "$TOOL" promote --manifest "$WORK/manifest.json" --channel-output "$WORK/stable.json"
# A queued observer after the first successful publication sees the current release as latest. The
# already-promoted manifest makes this an exact idempotent replay, not a baseline regression.
cp "$WORK/stable.json" "$WORK/current-stable.json"
python3 "$TOOL" promote --manifest "$WORK/manifest.json" --previous-channel "$WORK/current-stable.json" --channel-output "$WORK/stable-replay.json"
cmp "$WORK/stable.json" "$WORK/stable-replay.json"
printf '%s\n' '{"contentId":"sha256:newer","version":"9.9.0","sourceSha":"newer","promotedAt":"2026-08-13T00:00:00Z"}' > "$WORK/newer-stable.json"
if python3 "$TOOL" promote --manifest "$WORK/manifest.json" --previous-channel "$WORK/newer-stable.json" >/dev/null 2>&1; then
  echo "expected an older already-promoted manifest to refuse a newer live channel" >&2; exit 1
fi
python3 "$TOOL" merge-journals --manifest "$WORK/merge-base.json" --journal "$WORK/manifest.json"
jq -e '.state.feeds.github.state == "verified" and .state.feeds.nuget.state == "verified"' "$WORK/merge-base.json" >/dev/null
if python3 "$TOOL" promote --manifest "$WORK/merge-base.json" --previous-channel "$WORK/newer-stable.json" >/dev/null 2>&1; then
  echo "expected cross-release stable-channel regression to fail closed" >&2; exit 1
fi

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
  --source-sha 0123456789012345678901234567890123456789 --policy-version release-saga/1 --previous-version 9.8.6 \
  --artifact-dir "$WORK/artifacts" --expected-package FS.GG.Coord.Cli \
  --expected-package FS.GG.Kit --expected-package FS.GG.Drivers --output "$WORK/incomplete.json" >/dev/null
python3 "$TOOL" preflight --manifest "$WORK/incomplete.json" --feed both >/dev/null
# Policy order is part of state, not workflow prose: public-feed progress before a complete org set
# must be rejected even when the observed bytes themselves are correct.
if python3 "$TOOL" record-observed --manifest "$WORK/incomplete.json" --feed nuget \
  --observed "FS.GG.Coord.Cli=$WORK/nuget/FS.GG.Coord.Cli.9.8.7.nupkg" --detail "wrong order" >/dev/null 2>&1; then
  echo "expected nuget-before-org observation to fail closed" >&2; exit 1
fi
if python3 "$TOOL" promote --manifest "$WORK/incomplete.json" --previous-channel "$WORK/previous-stable.json" >/dev/null 2>&1; then
  echo "expected incomplete stable promotion to fail closed" >&2; exit 1
fi

python3 - "$ROOT" <<'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
subjects = {
    "release-coord-engine.yml": "FS.GG.Coord.Cli",
    "release-kit.yml": "FS.GG.Kit",
    "release-drivers.yml": "FS.GG.Drivers",
}
for name, package in subjects.items():
    text = (root / ".github/workflows" / name).read_text()
    ordered = [
        f"release-saga-ci.sh init {package}",
        f"release-saga-ci.sh github {package}",
        f"release-saga-ci.sh nuget-probe {package}",
        "uses: NuGet/login@v1",
        f"release-saga-ci.sh nuget-record {package}",
        f"release-saga-ci.sh failure {package}",
    ]
    positions = [text.find(token) for token in ordered]
    assert all(position >= 0 for position in positions), (name, ordered, positions)
    assert positions == sorted(positions), (name, positions)
    assert "--skip-duplicate" not in text, name
    assert "source_sha:" in text, name
    assert 'echo "source_sha=$source_sha"' in text, name
    assert 'steps.v.outputs.source_sha' in text, name
    assert 'tagged" != "$GITHUB_SHA' not in text, name
    if name == "release-coord-engine.yml":
        assert '[[ "$DISPATCH_SOURCE_SHA" =~ ^[0-9a-f]{40}$ ]]' in text, name
adapter = (root / "scripts/release-saga-ci.sh").read_text()
assert "github_base | sed 's:/*$::'" in adapter
assert "printf '%s/%s/%s/%s.%s.nupkg'" in adapter
assert 'lastFailure.feed == "nuget"' in adapter
assert "never issue a blind duplicate push" in adapter
assert adapter.count('[ "$observed" = true ] ||') == 2
prepare = (root / ".github/workflows/release-saga-prepare.yml").read_text()
for project in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"):
    assert prepare.count(f"dotnet pack src/{project}/") == 1, project
promote = (root / ".github/workflows/release-saga-promote.yml").read_text()
for token in ("assert-identity", "merge-journals", "record-observed", "stable-channel.json", "--draft=false"):
    assert token in promote, token
for token in ("source_sha=", 'refs/tags/$tag^{}', "head -1 | sed 's:/*$::'", '"${github_base}/${lower}'):
    assert token in promote, token
print("production saga topology: pack-once, durable journals, org barrier, resume probes, identity, and promotion wired")
PY

echo "release saga: forced mid-publish recovery, byte drift, dual-feed observation, and promotion passed"

#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOOL="$ROOT/scripts/release-saga.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/release-saga.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/artifacts" "$WORK/github" "$WORK/nuget"

# make_package TARGET ID VERSION [CORE_PROPERTIES_GUID] [BODY] [MANIFEST_RELATIONSHIP_ID]
#
# The OPC shape is copied from real `dotnet pack` output, because two of its parts are the whole
# subject of .github#2664. Measured on `src/FS.GG.Kit/FS.GG.Kit.csproj` at 2cd9518e, two consecutive
# packs of one clean tree produced core-properties parts named 9c6d21a2a7774fb2bbc48858e7e6d136 and
# 341955db2e8847439fdf05b771ee2c5c with byte-identical contents, `_rels/.rels` entries differing only
# in the core-properties `Relationship`'s `Target` and its derived `Id` (the manifest relationship's
# `Id` was identical in both), and an identical `[Content_Types].xml`. The three knobs those legs
# need: GUID reproduces an honest re-pack, BODY a genuine content divergence, and
# MANIFEST_RELATIONSHIP_ID a change inside `_rels/.rels` that the normalization must NOT absorb.
make_package() {
  python3 - "$1" "$2" "$3" "${4:-9c6d21a2a7774fb2bbc48858e7e6d136}" "${5:-fixture}" "${6:-R411317ADCBB7CC3C}" <<'PY'
import pathlib, sys, zipfile
target, package_id, version = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
guid, body, manifest_relationship_id = sys.argv[4], sys.argv[5], sys.argv[6]
nuspec = f'''<?xml version="1.0"?><package><metadata><id>{package_id}</id><version>{version}</version><authors>FS-GG</authors><description>fixture</description><releaseNotes>{version} release</releaseNotes><dependencies><group targetFramework="net10.0"><dependency id="FSharp.Core" version="[10.0.100, )" /></group></dependencies></metadata></package>'''
core_properties = f'''<?xml version="1.0" encoding="utf-8"?>
<coreProperties xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns="http://schemas.openxmlformats.org/package/2006/metadata/core-properties">
  <dc:identifier>{package_id}</dc:identifier>
  <version>{version}</version>
</coreProperties>'''
relationships = f'''<?xml version="1.0" encoding="utf-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{package_id}.nuspec" Id="{manifest_relationship_id}" />
  <Relationship Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="/package/services/metadata/core-properties/{guid}.psmdcp" Id="R{guid[:16].upper()}" />
</Relationships>'''
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    archive.writestr("_rels/.rels", relationships)
    archive.writestr(f"{package_id}.nuspec", nuspec)
    archive.writestr("content/payload.txt", f"{package_id}:{version}:{body}\n")
    archive.writestr(f"package/services/metadata/core-properties/{guid}.psmdcp", core_properties)
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
# Independent workflows observe identical immutable bytes at different times. The journal merge
# accepts timestamp drift while retaining the latest observation, but still rejects hash drift.
cp "$WORK/manifest.json" "$WORK/later-journal.json"
jq '(.state.feeds.github.packages[].observedAt) = "2099-01-01T00:00:00Z"' \
  "$WORK/later-journal.json" > "$WORK/later-journal.tmp"
mv "$WORK/later-journal.tmp" "$WORK/later-journal.json"
python3 "$TOOL" merge-journals --manifest "$WORK/merge-base.json" --journal "$WORK/later-journal.json"
jq -e '[.state.feeds.github.packages[].observedAt == "2099-01-01T00:00:00Z"] | all' "$WORK/merge-base.json" >/dev/null
cp "$WORK/later-journal.json" "$WORK/conflicting-journal.json"
jq '.state.feeds.github.packages["FS.GG.Kit"].externalSha256 = "different"' \
  "$WORK/conflicting-journal.json" > "$WORK/conflicting-journal.tmp"
mv "$WORK/conflicting-journal.tmp" "$WORK/conflicting-journal.json"
if python3 "$TOOL" merge-journals --manifest "$WORK/merge-base.json" \
  --journal "$WORK/conflicting-journal.json" >/dev/null 2>&1; then
  echo "expected conflicting external archive hash to fail closed" >&2; exit 1
fi
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
    for retired in (
        "DISPATCH_PUBLISH",
        "inputs.publish",
        "steps.v.outputs.push",
        "pack locally",
        "Pack-only dry run",
        "dotnet pack",
    ):
        assert retired not in text, (name, retired)
    dispatch = text.split("workflow_dispatch:", 1)[1].split("permissions:", 1)[0]
    assert "source_sha:" in dispatch and "required: true" in dispatch, name
    assert "Use the saga-prepared package" in text, name
    assert "gh release download" in text, name
    assert "release-manifest.json" in text, name
    assert "source_sha:" in text, name
    assert 'echo "source_sha=$source_sha"' in text, name
    assert 'steps.v.outputs.source_sha' in text, name
    assert 'tagged" != "$GITHUB_SHA' not in text, name
    exact_sha_checks = (
        '[[ "$DISPATCH_SOURCE_SHA" =~ ^[0-9a-f]{40}$ ]]',
        'case "$DISPATCH_SOURCE_SHA" in',
    )
    assert any(check in text for check in exact_sha_checks), name
adapter = (root / "scripts/release-saga-ci.sh").read_text()
assert "github_base | sed 's:/*$::'" in adapter
assert "printf '%s/%s/%s/%s.%s.nupkg'" in adapter
assert 'lastFailure.feed == "nuget"' in adapter
assert "never issue a blind duplicate push" in adapter
assert adapter.count('[ "$observed" = true ] ||') == 2
prepare = (root / ".github/workflows/release-saga-prepare.yml").read_text()
for project in ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers"):
    assert prepare.count(f"dotnet pack src/{project}/") == 1, project
# The reuse-existing-draft branch decides on re-pack-stable payload, never on the raw archive bytes
# or the `contentId` computed over them — those differ on every honest re-pack (.github#2664).
assert "release-saga.py assert-reusable" in prepare, "reuse branch no longer asks for a re-pack-stable verdict"
assert "jq -r .contentId /tmp/prior" not in prepare, "reuse branch compares contentId again (.github#2664)"
assert 'cmp "/tmp/prior/' not in prepare, "reuse branch compares raw archive bytes again (.github#2664)"
promote = (root / ".github/workflows/release-saga-promote.yml").read_text()
for token in ("assert-identity", "merge-journals", "record-observed", "stable-channel.json", "--draft=false"):
    assert token in promote or token in (root / "scripts/release-saga-promote-release.sh").read_text(), token
for token in ("source_sha=", 'refs/tags/$tag^{}', "head -1 | sed 's:/*$::'", '"${github_base}/${lower}'):
    assert token in promote, token
assert 'journal_count=$((journal_count + 1))' in promote
assert '[ "$journal_count" -eq 3 ]' in promote
assert '[ "${#journals[@]}" -eq 3 ]' not in promote
assert "release-saga-promote-release.sh" in promote
print("production saga topology: pack-once, durable journals, org barrier, resume probes, identity, and promotion wired")
PY

# Once GitHub publishes a release, repository immutable-release enforcement activates. A queued
# observer must compare the already-published content and return success; it may not try to clobber
# immutable assets. Exercise both the first promotion and the exact replay against a fake gh.
mkdir -p "$WORK/promote-bin" "$WORK/promote-assets"
cp "$WORK/manifest.json" "$WORK/promote-assets/release-manifest.json"
cp "$WORK/stable.json" "$WORK/promote-assets/stable-channel.json"
cat > "$WORK/promote-bin/gh" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$FAKE_GH_CALLS"
case "$1 $2" in
  "release view")
    if [ "$FAKE_RELEASE_MODE" = immutable ]; then printf '%s\n' '{"isDraft":false,"isImmutable":true}'
    else printf '%s\n' '{"isDraft":true,"isImmutable":false}'; fi
    ;;
  "release download")
    while [ "$#" -gt 0 ]; do
      if [ "$1" = --dir ]; then shift; target="$1"; break; fi
      shift
    done
    mkdir -p "$target"
    cp "$FAKE_RELEASE_ASSETS/release-manifest.json" "$target/"
    cp "$FAKE_RELEASE_ASSETS/stable-channel.json" "$target/"
    ;;
  "release upload"|"release edit") ;;
  *) echo "unexpected fake gh call: $*" >&2; exit 2 ;;
esac
SH
chmod +x "$WORK/promote-bin/gh"
: > "$WORK/promote-calls.log"
PATH="$WORK/promote-bin:$PATH" FAKE_RELEASE_MODE=immutable \
  FAKE_RELEASE_ASSETS="$WORK/promote-assets" FAKE_GH_CALLS="$WORK/promote-calls.log" \
  bash "$ROOT/scripts/release-saga-promote-release.sh" example/repo coherent-set/v9.8.7 \
    "$WORK/manifest.json" "$WORK/stable.json"
! grep -Eq '^release (upload|edit)' "$WORK/promote-calls.log"
cp "$WORK/promote-assets/stable-channel.json" "$WORK/promote-assets/stable-channel.good"
jq '.contentId = "sha256:drift"' "$WORK/promote-assets/stable-channel.json" > "$WORK/promote-assets/drift"
mv "$WORK/promote-assets/drift" "$WORK/promote-assets/stable-channel.json"
if PATH="$WORK/promote-bin:$PATH" FAKE_RELEASE_MODE=immutable \
  FAKE_RELEASE_ASSETS="$WORK/promote-assets" FAKE_GH_CALLS="$WORK/promote-calls.log" \
  bash "$ROOT/scripts/release-saga-promote-release.sh" example/repo coherent-set/v9.8.7 \
    "$WORK/manifest.json" "$WORK/stable.json" >/dev/null 2>&1; then
  echo "expected immutable promotion with a different channel receipt to fail closed" >&2; exit 1
fi
mv "$WORK/promote-assets/stable-channel.good" "$WORK/promote-assets/stable-channel.json"
: > "$WORK/promote-calls.log"
PATH="$WORK/promote-bin:$PATH" FAKE_RELEASE_MODE=draft \
  FAKE_RELEASE_ASSETS="$WORK/promote-assets" FAKE_GH_CALLS="$WORK/promote-calls.log" \
  bash "$ROOT/scripts/release-saga-promote-release.sh" example/repo coherent-set/v9.8.7 \
    "$WORK/manifest.json" "$WORK/stable.json"
grep -Eq '^release upload .*--clobber' "$WORK/promote-calls.log"
grep -Eq '^release edit .*--draft=false' "$WORK/promote-calls.log"

# ---------------------------------------------------------------------------------------------
# Re-preparing over an existing draft resumes instead of wedging (.github#2664).
#
# When a run creates the draft and something after it does not finish, every later run re-packs and
# re-enters the reuse branch. `dotnet pack` is not reproducible (.github#2240), so those re-packed
# archives are never byte-identical to the stored ones and their manifests never share a `contentId`.
# The three sets below reproduce that exactly: `first` and `second` are honest re-packs of one tree —
# same content, different core-properties part — and `divergent` is a real content change.
REPACK="$WORK/repack"
mkdir -p "$REPACK/first" "$REPACK/second" "$REPACK/divergent" "$REPACK/other-source" "$REPACK/partial" \
  "$REPACK/manifest-relationship"
for package in FS.GG.Coord.Cli FS.GG.Kit FS.GG.Drivers; do
  make_package "$REPACK/first/$package.9.8.7.nupkg" "$package" 9.8.7 9c6d21a2a7774fb2bbc48858e7e6d136
  make_package "$REPACK/second/$package.9.8.7.nupkg" "$package" 9.8.7 341955db2e8847439fdf05b771ee2c5c
  make_package "$REPACK/divergent/$package.9.8.7.nupkg" "$package" 9.8.7 341955db2e8847439fdf05b771ee2c5c "rebuilt from different source"
  # An honest re-pack that ALSO alters the manifest relationship, for the width bound below. `Id`
  # is deliberately the weakest field of that element to perturb — see that leg's own comment for
  # why choosing the weakest is what makes the bound the strong one.
  make_package "$REPACK/manifest-relationship/$package.9.8.7.nupkg" "$package" 9.8.7 \
    341955db2e8847439fdf05b771ee2c5c fixture RF00DFACE00DFACE0
done
cp "$REPACK/second/"*.nupkg "$REPACK/other-source/"
cp "$REPACK/second/"*.nupkg "$REPACK/partial/"

prepare_repack_set() {
  python3 "$TOOL" prepare --release-id fixture-9.8.7 --version 9.8.7 \
    --source-sha "${2:-0123456789012345678901234567890123456789}" --policy-version release-saga/1 \
    --previous-version 9.8.6 --artifact-dir "$1" \
    --expected-package FS.GG.Coord.Cli --expected-package FS.GG.Kit --expected-package FS.GG.Drivers \
    --output "$1/release-manifest.json" >/dev/null
}
for set_dir in first second divergent partial manifest-relationship; do prepare_repack_set "$REPACK/$set_dir"; done
prepare_repack_set "$REPACK/other-source" ffffffffffffffffffffffffffffffffffffffff

kit_payload_sha() {
  jq -r '.descriptor.packages[] | select(.id == "FS.GG.Kit") | .artifact.payloadSha256' "$1"
}

# A refusal has to say the right thing, not merely happen: assert on its text, and say which
# expectation failed rather than letting `set -e` abort a bare `grep` with no diagnosis.
diagnosis_names() {
  grep -q -- "$2" "$1" || { echo "refusal in $1 does not name '$2'" >&2; exit 1; }
}
diagnosis_omits() {
  ! grep -q -- "$2" "$1" || { echo "refusal in $1 names '$2', which is normalized away" >&2; exit 1; }
}

# The fixture really does reproduce what the old comparison asserted against, or the legs below prove
# nothing: raw bytes differ, and so does the `contentId` computed over their hashes and sizes.
if cmp -s "$REPACK/first/FS.GG.Kit.9.8.7.nupkg" "$REPACK/second/FS.GG.Kit.9.8.7.nupkg"; then
  echo "fixture does not reproduce dotnet pack's per-invocation core-properties part" >&2; exit 1
fi
if [ "$(jq -r .contentId "$REPACK/first/release-manifest.json")" = "$(jq -r .contentId "$REPACK/second/release-manifest.json")" ]; then
  echo "fixture does not reproduce the contentId instability the reuse branch asserted against" >&2; exit 1
fi
# The field that exists to be registry- and re-pack-independent now actually is.
if [ "$(kit_payload_sha "$REPACK/first/release-manifest.json")" != "$(kit_payload_sha "$REPACK/second/release-manifest.json")" ]; then
  echo "payloadSha256 is still unstable across a re-pack" >&2; exit 1
fi
python3 "$TOOL" assert-reusable --stored "$REPACK/first/release-manifest.json" \
  --candidate "$REPACK/second/release-manifest.json"

# Both directions, because a normalization that waved a materially different package through would be
# the fail-open .github#2240 and .github#2428 exist to prevent. A real content change is refused, and
# the refusal names the archive entry that differs rather than one guaranteed to.
if python3 "$TOOL" assert-reusable --stored "$REPACK/first/release-manifest.json" \
  --candidate "$REPACK/divergent/release-manifest.json" >"$WORK/divergent.log" 2>&1; then
  echo "expected a genuine payload divergence to fail closed" >&2; exit 1
fi
diagnosis_names "$WORK/divergent.log" "content/payload.txt"
diagnosis_names "$WORK/divergent.log" "FS.GG.Kit"
diagnosis_omits "$WORK/divergent.log" "_rels/.rels"
# The normalization is bounded in WIDTH, not only in effect. Dropping the core-properties
# relationship is the whole licence it has; the manifest relationship stays under the hash, and the
# only leg that can say so is one whose `_rels/.rels` differs there and nowhere else. Without this,
# widening the predicate in `normalized_relationships` to drop EVERY relationship leaves this suite
# green — a normalization that answers "equal" for two packages whose relationship parts disagree is
# the fail-open .github#2240 and .github#2428 depend on this function not having.
#
# WHY `Id` AND NOT `Type` OR `Target`, since only one field can be perturbed at a time and the
# choice decides what this leg proves: `Id` is the WEAKEST of the three. It is an OPC uniqueness
# token carrying no semantics — nothing resolves through it — whereas `Type` and `Target` name what
# the relationship IS and what it points AT. All three are attributes of the one element the
# normalization either preserves or drops, so it cannot distinguish them: a normalization that
# still refuses on the field with the least meaning necessarily refuses on the two with more. Had
# this leg perturbed `Target` instead, it would pass under a normalization that kept `Target` and
# discarded `Id`, and would prove the weaker statement.
if python3 "$TOOL" assert-reusable --stored "$REPACK/second/release-manifest.json" \
  --candidate "$REPACK/manifest-relationship/release-manifest.json" >"$WORK/manifest-relationship.log" 2>&1; then
  echo "expected a changed manifest relationship to fail closed; the .rels normalization is too wide" >&2
  exit 1
fi
# `second` and `manifest-relationship` share a core-properties part and all content, so `_rels/.rels`
# is the sole difference in the whole archive — the narrowest statement of the bound.
diagnosis_names "$WORK/manifest-relationship.log" "_rels/.rels"
diagnosis_omits "$WORK/manifest-relationship.log" "content/payload.txt"
# And the same holds through an honest re-pack: against `first` the two also disagree on the
# core-properties part, which the normalization IS licensed to absorb. It must absorb that half and
# still refuse on the other, naming only `_rels/.rels`.
if python3 "$TOOL" assert-reusable --stored "$REPACK/first/release-manifest.json" \
  --candidate "$REPACK/manifest-relationship/release-manifest.json" >"$WORK/manifest-repack.log" 2>&1; then
  echo "expected a changed manifest relationship to survive a re-pack and fail closed" >&2
  exit 1
fi
diagnosis_names "$WORK/manifest-repack.log" "_rels/.rels"
diagnosis_omits "$WORK/manifest-repack.log" "content/payload.txt"

# Identical bytes prepared as a different release are not a re-pack either.
if python3 "$TOOL" assert-reusable --stored "$REPACK/first/release-manifest.json" \
  --candidate "$REPACK/other-source/release-manifest.json" >"$WORK/identity.log" 2>&1; then
  echo "expected a sourceSha change to fail closed" >&2; exit 1
fi
diagnosis_names "$WORK/identity.log" "descriptor.sourceSha"
# A draft whose asset upload did not finish is not reusable, and says so before anything is tagged.
rm "$REPACK/partial/FS.GG.Kit.9.8.7.nupkg"
if python3 "$TOOL" assert-reusable --stored "$REPACK/partial/release-manifest.json" \
  --candidate "$REPACK/second/release-manifest.json" >"$WORK/partial.log" 2>&1; then
  echo "expected an incomplete stored draft to fail closed" >&2; exit 1
fi
diagnosis_names "$WORK/partial.log" "manifest-bound artifact is missing"

# GATE INVERSION (.github#2664 AC4). Restore payload()'s previous shape — relationship parts hashed
# verbatim — and the re-pack comparison above must go red, naming the entry that made it red. A gate
# whose inversion survives has not tested the fix.
python3 - "$TOOL" "$WORK/inverted-release-saga.py" <<'PY'
import pathlib, sys
source = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
marker = "    return normalized_relationships(raw) if is_relationship_part(name) else raw"
assert source.count(marker) == 1, "gate-inversion marker moved; update tests/release-saga/run.sh"
pathlib.Path(sys.argv[2]).write_text(source.replace(marker, "    return raw"), encoding="utf-8")
PY
if python3 "$WORK/inverted-release-saga.py" assert-reusable \
  --stored "$REPACK/first/release-manifest.json" \
  --candidate "$REPACK/second/release-manifest.json" >"$WORK/inverted.log" 2>&1; then
  echo "gate inversion survived: payload() normalization is not what makes an honest re-pack reusable" >&2
  exit 1
fi
diagnosis_names "$WORK/inverted.log" "_rels/.rels"

echo "release saga: forced mid-publish recovery, byte drift, dual-feed observation, draft reuse, and promotion passed"

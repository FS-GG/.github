#!/usr/bin/env bash
set -euo pipefail

report="${1:?usage: verify-release-receipt.sh REPORT.xml}"
root="$(git rev-parse --show-toplevel)"
receipt="$root/work/2794-coord-engine-release/artifacts/2794-release-receipt.json"
scratch="$(mktemp -d)"
trap 'rm -r "$scratch"' EXIT

jq -e '.schema == "fsgg.coord-engine-release-evidence/1"' "$receipt" >/dev/null
jq -e '.version == "0.68.0" and .sourceSha == "1d5f8e246a0db64e87f8b98204cf5b09f5f156dd"' "$receipt" >/dev/null
jq -e '.prepared.run == 32474831515 and .prepared.contentId == "sha256:d289364236b4987aaa445ce913a6bf4dbebcc4921a49bb9a8071d135834ed7a4"' "$receipt" >/dev/null
for tag in kit/v0.68.0 drivers/v0.68.0 coord-engine/v0.68.0; do
  test "$(git rev-parse "$tag^{commit}")" = 1d5f8e246a0db64e87f8b98204cf5b09f5f156dd
done
release_current="$(gh release view coherent-set/v0.68.0 --repo FS-GG/.github --json isDraft,targetCommitish \
  --jq '(.isDraft == false) and (.targetCommitish == "1d5f8e246a0db64e87f8b98204cf5b09f5f156dd")')"
test "$release_current" = true
gh release download coherent-set/v0.68.0 --repo FS-GG/.github --pattern release-manifest.json --dir "$scratch"
test "$(sha256sum "$scratch/release-manifest.json" | cut -d' ' -f1)" = 0de907fe34cc9c5821a7596f066e9958bab2d9e5be5b4e64182992db5da8f7b1
jq -e '.state.phase == "promoted" and .state.channelPromotion.state == "promoted"' "$scratch/release-manifest.json" >/dev/null
jq -e '[.state.feeds.github.packages[].state] | length == 3 and all(. == "verified")' "$scratch/release-manifest.json" >/dev/null
jq -e '[.state.feeds.nuget.packages[].state] | length == 3 and all(. == "verified")' "$scratch/release-manifest.json" >/dev/null
jq -e '.state.feeds as $f | [.descriptor.packages[].id] | all(. as $id | $f.github.packages[$id].externalPayloadSha256 == $f.nuget.packages[$id].externalPayloadSha256)' "$scratch/release-manifest.json" >/dev/null
curl -fsSL https://api.nuget.org/v3-flatcontainer/fs.gg.coord.cli/0.68.0/fs.gg.coord.cli.0.68.0.nupkg -o "$scratch/coord.nupkg"
test "$(sha256sum "$scratch/coord.nupkg" | cut -d' ' -f1)" = 3be1a52532f3b6b11c6108d34f435b4c9726dd6e472c06ca5dbbd65b15b6cddb
dotnet tool install FS.GG.Coord.Cli --version 0.68.0 --tool-path "$scratch/tool" --source https://api.nuget.org/v3/index.json >/dev/null
test "$("$scratch/tool/fsgg-coord-engine" --version)" = 0.68.0.0
python3 "$root/scripts/check-engine-freshness.py" --repo "$root" --ref origin/main --report "$scratch/freshness.json" >/dev/null
jq -e '.feedVersion == "0.68.0" and .unreleasedCount == 0 and .wireCount == 0 and .defectCount == 0 and (.releaseOwed | not) and (.red | not)' "$scratch/freshness.json" >/dev/null

{
  echo '<?xml version="1.0" encoding="utf-8"?>'
  echo '<testsuite name="coord-engine-0.68.0-release" tests="13" failures="0" errors="0" skipped="0">'
  for name in receipt-schema version-source prepared-content-id atomic-tags published-release manifest-digest promoted-state github-feed nuget-feed payload-identity public-archive public-install cleared-freshness; do
    printf '  <testcase classname="release-2794" name="%s" />\n' "$name"
  done
  echo '</testsuite>'
} > "$report"

#!/usr/bin/env bash
set -euo pipefail

repo="${1:?repository is required}"
tag="${2:?release tag is required}"
manifest="${3:?release manifest is required}"
channel="${4:?stable-channel receipt is required}"
version="${tag#coherent-set/v}"

release_state="$(gh release view "$tag" --repo "$repo" --json isDraft,isImmutable)"
if [ "$(jq -r .isImmutable <<<"$release_state")" = true ]; then
  [ "$(jq -r .isDraft <<<"$release_state")" = false ] \
    || { echo "immutable release $tag is unexpectedly still a draft" >&2; exit 1; }
  prior="$(mktemp -d "${RUNNER_TEMP:-/tmp}/release-saga-prior.XXXXXX")"
  trap 'rm -rf "$prior"' EXIT
  gh release download "$tag" --repo "$repo" \
    --pattern release-manifest.json --pattern stable-channel.json --dir "$prior"
  [ "$(jq -r .contentId "$prior/release-manifest.json")" = "$(jq -r .contentId "$manifest")" ] \
    || { echo "immutable release $tag has a different manifest content ID" >&2; exit 1; }
  cmp "$prior/stable-channel.json" "$channel" \
    || { echo "immutable release $tag has a different stable-channel receipt" >&2; exit 1; }
  echo "Immutable release $tag already contains the identical content and channel receipt; promotion is idempotent."
  exit 0
fi

[ "$(jq -r .isDraft <<<"$release_state")" = true ] \
  || { echo "mutable non-draft release $tag is not a valid promotion target" >&2; exit 1; }
gh release upload "$tag" --repo "$repo" --clobber "$manifest" "$channel"
gh release edit "$tag" --repo "$repo" --draft=false --latest \
  --title "FS.GG coherent set $version" \
  --notes "Both feeds serve all manifest-bound packages. See release-manifest.json and stable-channel.json for hashes, observations, recovery, and promotion receipt."

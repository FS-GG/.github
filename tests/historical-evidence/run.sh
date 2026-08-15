#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

repo="$WORK/repo"
mkdir -p "$repo/evidence"
git -C "$repo" init -q
git -C "$repo" config user.name fixture
git -C "$repo" config user.email fixture@example.invalid
printf 'historical result\n' >"$repo/evidence/result.trx"
git -C "$repo" add evidence/result.trx
git -C "$repo" commit -qm fixture
source_sha="$(git -C "$repo" rev-parse HEAD)"
blob="$(git -C "$repo" rev-parse "$source_sha:evidence/result.trx")"
bytes="$(git -C "$repo" cat-file -s "$source_sha:evidence/result.trx")"
payload_sha="$(git -C "$repo" show "$source_sha:evidence/result.trx" | sha256sum | cut -d' ' -f1)"
prefix="fixture-${source_sha:0:8}"
archive="$WORK/fixture.tar.gz"
git -C "$repo" archive --format=tar.gz --prefix="$prefix/" -o "$archive" "$source_sha" -- evidence/result.trx
archive_sha="$(sha256sum "$archive" | cut -d' ' -f1)"
archive_bytes="$(stat -c %s "$archive")"
rows_sha="$(printf '%s\t%s\t%s\t%s\n' "$bytes" "$payload_sha" "$blob" evidence/result.trx | sha256sum | cut -d' ' -f1)"
manifest="$WORK/manifest.json"

jq -n \
  --arg source "$source_sha" --arg blob "$blob" --arg payload_sha "$payload_sha" \
  --arg prefix "$prefix" --arg archive_sha "$archive_sha" --arg rows_sha "$rows_sha" \
  --argjson bytes "$bytes" --argjson archive_bytes "$archive_bytes" \
  '{schema_version:1,source_sha:$source,release:{tag:("evidence/test-" + ($source[0:8])),url:("https://github.com/example/repo/releases/tag/evidence/test-" + ($source[0:8])),immutable:true,asset_url:("https://github.com/example/repo/releases/download/evidence/test-" + ($source[0:8]) + "/fixture.tar.gz")},archive:{name:"fixture.tar.gz",prefix:$prefix,bytes:$archive_bytes,sha256:$archive_sha},file_count:1,source_bytes:$bytes,canonical_rows_sha256:$rows_sha,files:[{path:"evidence/result.trx",bytes:$bytes,sha256:$payload_sha,git_blob:$blob}]}' \
  >"$manifest"

python3 "$ROOT/scripts/verify-historical-evidence.py" "$manifest" --archive "$archive" --git-root "$repo"
if python3 "$ROOT/scripts/verify-historical-evidence.py" "$manifest"; then
  echo 'metadata-only verification unexpectedly passed' >&2; exit 1
fi

jq '.release.immutable=false' "$manifest" >"$WORK/mutable.json"
if python3 "$ROOT/scripts/verify-historical-evidence.py" "$WORK/mutable.json" --archive "$archive"; then
  echo 'mutable release unexpectedly passed' >&2; exit 1
fi
jq '.files[0].path="../result.trx"' "$manifest" >"$WORK/traversal.json"
if python3 "$ROOT/scripts/verify-historical-evidence.py" "$WORK/traversal.json" --archive "$archive"; then
  echo 'unsafe path unexpectedly passed' >&2; exit 1
fi
jq '.files[0].git_blob="0000000000000000000000000000000000000000"' "$manifest" >"$WORK/blob.json"
if python3 "$ROOT/scripts/verify-historical-evidence.py" "$WORK/blob.json" --git-root "$repo"; then
  echo 'mutated blob unexpectedly passed' >&2; exit 1
fi
cp "$archive" "$WORK/tampered.tar.gz"
printf x >>"$WORK/tampered.tar.gz"
if python3 "$ROOT/scripts/verify-historical-evidence.py" "$manifest" --archive "$WORK/tampered.tar.gz"; then
  echo 'mutated archive unexpectedly passed' >&2; exit 1
fi

# The production manifest remains bound to the exact pre-deletion Git objects.
python3 "$ROOT/scripts/verify-historical-evidence.py" \
  "$ROOT/docs/reports/evidence/2026-08-15-m6-historical-trx-archive.json" --git-root "$ROOT"
echo 'historical evidence fixture: ok'

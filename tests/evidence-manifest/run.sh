#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
printf 'bounded execution output\n' >"$WORK/result.trx"

create() {
  python3 "$ROOT/scripts/evidence-manifest.py" create \
    --cycle roadmap-coordination-churn-redesign-m5-evidence-ci-consolidation \
    --source-sha 7a9b8742f259af4d82b9562bfbe8e40d963b0be7 \
    --created-at 2026-08-14T10:00:00Z --expires-at 2026-11-12T10:00:00Z \
    --reproduce 'bash tests/evidence-manifest/run.sh' \
    --url https://github.com/FS-GG/.github/actions/runs/31800000000/artifacts/1 \
    --name tests --file "$WORK/result.trx" --output "$WORK/manifest.json"
}

create
python3 "$ROOT/scripts/evidence-manifest.py" verify "$WORK/manifest.json" --now 2026-08-15T00:00:00Z --artifact "tests=$WORK/result.trx"

# Integrity is content-addressed: changing the local payload changes the produced digest.
first="$(jq -r '.artifacts[0].sha256' "$WORK/manifest.json")"
printf 'tampered\n' >>"$WORK/result.trx"
if python3 "$ROOT/scripts/evidence-manifest.py" verify "$WORK/manifest.json" --now 2026-08-15T00:00:00Z --artifact "tests=$WORK/result.trx"; then
  echo 'tampered payload unexpectedly passed' >&2; exit 1
fi
create
second="$(jq -r '.artifacts[0].sha256' "$WORK/manifest.json")"
[ "$first" != "$second" ] || { echo 'digest did not detect mutation' >&2; exit 1; }

# Manifest tampering and retention expiry fail closed.
jq '.artifacts[0].sha256 = "bad"' "$WORK/manifest.json" >"$WORK/tampered.json"
if python3 "$ROOT/scripts/evidence-manifest.py" verify "$WORK/tampered.json" --now 2026-08-15T00:00:00Z; then
  echo 'tampered manifest unexpectedly passed' >&2; exit 1
fi
if python3 "$ROOT/scripts/evidence-manifest.py" verify "$WORK/manifest.json" --now 2026-11-13T00:00:00Z; then
  echo 'expired artifact unexpectedly passed' >&2; exit 1
fi
python3 "$ROOT/scripts/evidence-manifest.py" verify "$WORK/manifest.json" --now 2026-11-13T00:00:00Z --allow-expired
echo 'evidence manifest fixture: ok'

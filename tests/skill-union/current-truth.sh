#!/usr/bin/env bash
# Focused regression fixture for the M0 skill current-truth repair (.github#1410).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
CHECK="$HERE/check-current-truth.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/skill-current-truth.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0
failcount=0
ok() { echo "PASS  $1"; pass=$((pass + 1)); }
bad() { echo "FAIL  $1"; failcount=$((failcount + 1)); }

if python3 "$CHECK" --root "$ROOT"; then
  ok "the real three-root skills carry one coherent current truth"
else
  bad "the real two-root skill audit failed"
fi

seed() {
  local target="$1"
  for runtime in .agents/skills .claude/skills; do
    mkdir -p "$target/$runtime"
    for skill in check-board cross-repo-coordination cut-nuget-release drive-board \
      intra-repo-parallel-work lane-steward p-add padd-item pnext-item publishing-and-deployment \
      spectre-console work-board work-roadmap; do
      cp -R "$ROOT/$runtime/$skill" "$target/$runtime/$skill"
    done
  done
  mkdir -p "$target/docs/adr" "$target/.github/workflows"
  cp "$ROOT/docs/adr/0012-dual-publish-to-nuget-org.md" "$target/docs/adr/"
  cp "$ROOT/docs/adr/0013-trusted-publishing-oidc-for-nuget-org.md" "$target/docs/adr/"
  cp "$ROOT/.github/workflows/dispatch-sender.yml" "$target/.github/workflows/"
  cp "$ROOT/default.json" "$target/"
}

expect_finding() {
  local name="$1" needle="$2" target="$3" out rc=0
  out="$(python3 "$CHECK" --root "$target" 2>&1)" || rc=$?
  if [ "$rc" -eq 1 ] && grep -qF "$needle" <<<"$out"; then
    ok "$name"
  else
    bad "$name (wanted exit 1 + '$needle', got exit $rc: $out)"
  fi
}

LANE="$WORK/lane"
seed "$LANE"
for runtime in .agents/skills .claude/skills; do
  sed -i 's|scripts/fsgg-coord set-paths <issue> --paths|scripts/fsgg-coord widen <issue> --paths|' \
    "$LANE/$runtime/lane-steward/SKILL.md"
done
expect_finding \
  "an additive widen recipe cannot masquerade as narrowing" \
  "narrowing must use set-paths" \
  "$LANE"

PUBLISH="$WORK/publish"
seed "$PUBLISH"
for runtime in .agents/skills .claude/skills; do
  sed -i 's/## Historical rollout record/## Public nuget.org (decided, wiring pending — ADR-0012 + ADR-0013)/' \
    "$PUBLISH/$runtime/publishing-and-deployment/SKILL.md"
done
expect_finding \
  "stable dual-publish guidance rejects a resurrected pending-rollout claim" \
  "stale live claim returned" \
  "$PUBLISH"

echo "--------------------------------------------"
echo "skill current-truth fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
